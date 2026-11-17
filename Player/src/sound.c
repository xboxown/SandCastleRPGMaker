/*
 * Sounds and music, as the Windows engine plays them (SoundPlayback): a
 * sound by its id from the project's Sound list, at its volume (or one
 * given), once or looping; sounds that belong to something (the event
 * music, the map's ambient sound) can be stopped or faded together; the
 * game's music and sound volumes (Set Volume) apply to everything playing.
 * Files are read as they were made: WAV (and Exult's .u7o, a WAV), MP3, and
 * Amiga IFF 8SVX - decoded as they play, mixed to 44.1 kHz stereo.
 */
#include "sound.h"
#include "platform.h"
#include "conditions.h"
#include "pack.h"
void game_log(const char *line);
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define DR_WAV_IMPLEMENTATION
#define DR_WAV_NO_STDIO
#define DR_WAV_NO_WCHAR
#include "../third_party/dr_wav.h"
#define DR_MP3_IMPLEMENTATION
#define DR_MP3_NO_STDIO
#include "../third_party/dr_mp3.h"

char *read_whole_file(const char *path, long *length);

#define MAX_VOICES 24
#define CHUNK 2048

/* A sound file: kept encrypted (stream), decrypted as it plays; an 8SVX one decrypted whole when first played (data). */
typedef struct SoundFile { int id, music; double volume; char file[64]; PackStream *stream; unsigned char *data; long size; } SoundFile;
static SoundFile *files; static int fileCount;
static char folder[1024];
static Archive *soundArchive;

enum { DEC_NONE, DEC_WAV, DEC_MP3, DEC_8SVX };

typedef struct Voice
{
    int used, soundId, music, loop, owner, kind;
    double base, fade, fadeFrom, fadeTo, fadeSeconds, fadeAt; int fading, stopAtEnd;
    drwav wav; drmp3 mp3;
    const signed char *svx; long svxLength, svxAt; int svxRate;
    int channels, rate;
    int16_t buffer[CHUNK * 2]; int buffered, at;  /* decoded frames (stereo), and where we are in them */
    unsigned frac;                                /* the position between frames, for resampling (FRAC_ONE = a whole frame) */
    int16_t lastL, lastR;
    const PackStream *stream; long pos;           /* the file it's read from, and where */
    int lbal, rbal;                               /* heard from one side (world_hearing_at): 0..256 each side */
#ifdef PLATFORM_PS2
    /* decoded ahead by the reader task (stereo frames): it adds, the mixer takes - each counter written by one task only */
    int16_t *ring; unsigned ringSize; volatile unsigned produced, consumed; volatile int ended, closing;
    /* what was last read from the file, decrypted (b578): the ADPCM decoder asks 4 bytes at a time - each would decrypt a
       whole 16-byte block again */
    unsigned char cache[4096]; long cacheAt, cacheLength;
#endif
} Voice;
#define FRAC_BITS 15 /* whole numbers, not doubles: the PS2's (and many Amigas') processors do doubles slowly, in software */
#define FRAC_ONE (1u << FRAC_BITS)

static Voice voices[MAX_VOICES];
static double musicVolume = 1, soundVolume = 1;
static int audioOn, outRate = 44100;
static FILE *dump;
#define STREAM_FRAMES 65536
static int16_t stream[STREAM_FRAMES * 2]; static volatile int streamRead, streamCount; static int streamOn, streamVolume = 256; static double streamFrac;

static SoundFile *file_of(int id) { for (int i = 0; i < fileCount; i++) if (files[i].id == id) return &files[i]; return NULL; }

/* ---------------- Decoding ---------------- */

/* The decoders read the file through these: decrypted as they read it. */
static size_t on_read(void *user, void *out, size_t n)
{
    Voice *v = (Voice *)user;
#ifdef PLATFORM_PS2
    /* small reads through the voice's cache (4 KB from a 16-byte boundary); big ones straight through */
    if (n < sizeof v->cache / 2)
    {
        unsigned char *to = (unsigned char *)out;
        size_t done = 0;
        while (done < n)
        {
            if (v->pos < v->cacheAt || v->pos >= v->cacheAt + v->cacheLength)
            {
                v->cacheAt = v->pos & ~15L;
                v->cacheLength = pack_stream_read(v->stream, v->cacheAt, v->cache, (long)sizeof v->cache);
                if (v->pos >= v->cacheAt + v->cacheLength) { v->cacheLength = 0; break; } /* the end */
            }
            long take = v->cacheAt + v->cacheLength - v->pos;
            if (take > (long)(n - done)) take = (long)(n - done);
            memcpy(to + done, v->cache + (v->pos - v->cacheAt), (size_t)take);
            done += (size_t)take; v->pos += take;
        }
        return done;
    }
#endif
    long got = pack_stream_read(v->stream, v->pos, out, (long)n);
    v->pos += got;
    return (size_t)got;
}

static int seek_to(Voice *v, long offset, int origin) /* 0 from the start, 1 from here, 2 from the end */
{
    long length = pack_stream_length(v->stream), at = origin == 0 ? offset : origin == 1 ? v->pos + offset : length + offset;
    if (at < 0 || at > length) return 0;
    v->pos = at;
    return 1;
}

static drwav_bool32 wav_seek(void *u, int offset, drwav_seek_origin o) { return (drwav_bool32)seek_to((Voice *)u, offset, o == DRWAV_SEEK_SET ? 0 : o == DRWAV_SEEK_CUR ? 1 : 2); }
static drwav_bool32 wav_tell(void *u, drwav_int64 *cursor) { *cursor = ((Voice *)u)->pos; return DRWAV_TRUE; }
static drmp3_bool32 mp3_seek(void *u, int offset, drmp3_seek_origin o) { return (drmp3_bool32)seek_to((Voice *)u, offset, o == DRMP3_SEEK_SET ? 0 : o == DRMP3_SEEK_CUR ? 1 : 2); }
static drmp3_bool32 mp3_tell(void *u, drmp3_int64 *cursor) { *cursor = ((Voice *)u)->pos; return DRMP3_TRUE; }

static int read_be16(const unsigned char *p) { return p[0] << 8 | p[1]; }
static long read_be32(const unsigned char *p) { return (long)p[0] << 24 | (long)p[1] << 16 | (long)p[2] << 8 | p[3]; }

/* IFF 8SVX: VHDR (its rate) and BODY (signed 8-bit, mono); uncompressed only. */
static int open_8svx(Voice *v, const unsigned char *d, long n)
{
    if (n < 12 || memcmp(d, "FORM", 4) || memcmp(d + 8, "8SVX", 4)) return 0;
    long at = 12;
    v->svxRate = 8363;
    while (at + 8 <= n)
    {
        long size = read_be32(d + at + 4);
        if (!memcmp(d + at, "VHDR", 4) && size >= 16) { v->svxRate = read_be16(d + at + 8 + 12); if (d[at + 8 + 15] != 0) return 0; }
        if (!memcmp(d + at, "BODY", 4)) { v->svx = (const signed char *)(d + at + 8); v->svxLength = size < n - at - 8 ? size : n - at - 8; v->svxAt = 0; v->channels = 1; v->rate = v->svxRate > 0 ? v->svxRate : 8363; return 1; }
        at += 8 + size + (size & 1);
    }
    return 0;
}

/* The next frames of the voice, as stereo, into out (CHUNK frames' room); how many - 0 at the end. */
static long decode_frames(Voice *v, int16_t *out)
{
    /* Not on the stack: the PS3's SDL gives its audio thread 16 KB, and this (8 KB) with the WAV decoder's own overflowed it
       (b595: the mixer crashed in a door's sound). Only ever used with the audio lock held (the mixer, or a sound starting). */
    static int16_t raw[CHUNK * 2];
    long got = 0;
    if (v->kind == DEC_WAV) got = (long)drwav_read_pcm_frames_s16(&v->wav, CHUNK, raw);
    else if (v->kind == DEC_MP3) got = (long)drmp3_read_pcm_frames_s16(&v->mp3, CHUNK, raw);
    else if (v->kind == DEC_8SVX)
    {
        while (got < CHUNK && v->svxAt < v->svxLength) raw[got++] = (int16_t)(v->svx[v->svxAt++] * 256);
    }
    if (got <= 0)
    {
        if (!v->loop) return 0;
        /* looping: from the start again, until it's stopped */
        if (v->kind == DEC_WAV) drwav_seek_to_pcm_frame(&v->wav, 0);
        else if (v->kind == DEC_MP3) drmp3_seek_to_pcm_frame(&v->mp3, 0);
        else v->svxAt = 0;
        if (v->kind == DEC_WAV) got = (long)drwav_read_pcm_frames_s16(&v->wav, CHUNK, raw);
        else if (v->kind == DEC_MP3) got = (long)drmp3_read_pcm_frames_s16(&v->mp3, CHUNK, raw);
        else while (got < CHUNK && v->svxAt < v->svxLength) raw[got++] = (int16_t)(v->svx[v->svxAt++] * 256);
        if (got <= 0) return 0;
    }
    for (long i = 0; i < got; i++)
    {
        int16_t l = raw[i * v->channels], r = v->channels > 1 ? raw[i * v->channels + 1] : l;
        out[i * 2] = l; out[i * 2 + 1] = r;
    }
    return got;
}

#ifndef PLATFORM_PS2
/* The next frames of the voice into its buffer; 0 at the end. */
static int decode(Voice *v)
{
    long got = decode_frames(v, v->buffer);
    if (got <= 0) return 0;
    v->buffered = (int)got; v->at = 0;
    return 1;
}
#else
void ps2_decoder_lock(void); void ps2_decoder_unlock(void); void ps2_decoder_wake(void);

/* The reader task's part (and start_at's, to begin with), the decoder lock held: one more chunk into the voice's buffer
   if it has room; 1 if it did something. */
static int read_ahead(Voice *v)
{
    static int16_t chunk[CHUNK * 2];
    if (v->ended || v->ringSize - (v->produced - v->consumed) < CHUNK) return 0;
    long got = decode_frames(v, chunk);
    if (got <= 0) { v->ended = 1; return 1; }
    unsigned at = v->produced % v->ringSize;
    for (long i = 0; i < got; i++) { v->ring[at * 2] = chunk[i * 2]; v->ring[at * 2 + 1] = chunk[i * 2 + 1]; if (++at == v->ringSize) at = 0; }
    v->produced += (unsigned)got;
    return 1;
}

/* The mixer's part: the next frames from what the reader decoded; 0 at the end. If the reader is behind (the disc slow
   to answer), a moment of quiet - never the same sound again. */
static int decode(Voice *v)
{
    unsigned have = v->produced - v->consumed;
    if (have == 0)
    {
        if (v->ended) return 0;
#ifdef PS2_DIAG_SHOTS
        { extern unsigned long ps2_underruns; ps2_underruns++; }
#endif
        for (int i = 0; i < 64; i++) { v->buffer[i * 2] = v->lastL; v->buffer[i * 2 + 1] = v->lastR; }
        v->buffered = 64; v->at = 0;
        return 1;
    }
    if (have > CHUNK) have = CHUNK;
    unsigned at = v->consumed % v->ringSize;
    for (unsigned i = 0; i < have; i++) { v->buffer[i * 2] = v->ring[at * 2]; v->buffer[i * 2 + 1] = v->ring[at * 2 + 1]; if (++at == v->ringSize) at = 0; }
    v->consumed += have;
    v->buffered = (int)have; v->at = 0;
    return 1;
}
#endif

static volatile int stoppedIds[64]; static volatile int stoppedCount; /* sounds that ended (on the audio thread), recorded on the game's */

/* The decoder let go of (on the PS2 by the reader task or start_at, the decoder lock held - never while it reads). */
static void finish_close(Voice *v)
{
    if (v->kind == DEC_WAV) drwav_uninit(&v->wav);
    else if (v->kind == DEC_MP3) drmp3_uninit(&v->mp3);
    v->kind = DEC_NONE;
#ifdef PLATFORM_PS2
    if (v->ringSize > 16384) { free(v->ring); v->ring = NULL; v->ringSize = 0; } /* a long sound's big buffer back to the game */
    v->closing = 0;
#endif
}

static void close_voice(Voice *v)
{
    if (v->used && stoppedCount < 64) stoppedIds[stoppedCount++] = v->soundId;
#ifdef PLATFORM_PS2
    if (v->used || v->kind) { v->used = 0; v->closing = 1; } /* the reader task lets its decoder go */
    return;
#else
    finish_close(v);
    v->used = 0;
#endif
}

#ifdef PLATFORM_PS2
/* The reader task: every playing sound topped up a chunk at a time; voices that stopped let go. 1 while there's more to do.
   Up to 1.5 s ahead as fast as it can; beyond that (to the buffer's 8 s) gently - a chunk on two of every three mixes
   (fresh: the first call after a mix), a little faster than it plays - so the game isn't stopped while it fills. */
int sound_decode_ahead(int fresh)
{
    static unsigned mixes;
    if (fresh) mixes++;
    int did = 0;
    for (int k = 0; k < MAX_VOICES; k++)
    {
        Voice *v = &voices[k];
        if (!v->used && !v->closing) continue;
        ps2_decoder_lock();
        if (v->closing) finish_close(v);
        else if (v->used && v->ring)
        {
            if (v->produced - v->consumed < 66150) did |= read_ahead(v);
            else if (fresh && mixes % 3) read_ahead(v);
        }
        ps2_decoder_unlock();
    }
    return did;
}
#endif

/* ---------------- Mixing (the audio thread) ---------------- */

static double level_of(const Voice *v) { double l = v->base * v->fade * (v->music ? musicVolume : soundVolume); return l < 0 ? 0 : l > 1 ? 1 : l; }

#if defined(PLATFORM_PS2) && defined(PS2_DIAG_SHOTS)
extern unsigned long ps2_discLoads, ps2_discMicros, ps2_discMaxMicros;
unsigned long ps2_micros(void);
unsigned long ps2_underruns; int audsrv_queued(void);
static void fill_inner(int16_t *out, int frames);
/* The diagnostic build: how long each mix takes, the gaps between them, and the disc's reads - printed every 400 mixes. */
static void fill(int16_t *out, int frames)
{
    static unsigned long calls, last, total, most, mostGap, gaps, framesAsked;
    unsigned long t0 = ps2_micros();
    static int leastQueued = 1 << 30; int q = audsrv_queued(); if (q < leastQueued) leastQueued = q;
    if (last) { unsigned long gap = t0 - last; gaps += gap; if (gap > mostGap) mostGap = gap; }
    fill_inner(out, frames);
    unsigned long t1 = ps2_micros(), took = t1 - t0;
    last = t1; calls++; total += took; framesAsked += (unsigned long)frames; if (took > most) most = took;
    if (calls <= 3 || calls % 100 == 0)
    {
        int playing = 0; for (int k = 0; k < MAX_VOICES; k++) playing += voices[k].used;
        printf("Game: Audio: reader behind %lu times, chip queue least %d bytes\n", ps2_underruns, leastQueued);
        ps2_underruns = 0; leastQueued = 1 << 30;
        printf("Game: Audio: %lu mixes of %lu frames, mix avg %lu us max %lu us, gap avg %lu us max %lu us, voices %d, disc loads %lu (avg %lu us, max %lu us)\n",
               calls, framesAsked / (calls <= 3 ? 1 : 100), total / (calls <= 3 ? 1 : 100), most, gaps / (calls <= 3 ? 1 : 100), mostGap, playing, ps2_discLoads, ps2_discLoads ? ps2_discMicros / ps2_discLoads : 0, ps2_discMaxMicros);
        total = most = mostGap = gaps = framesAsked = 0; ps2_discLoads = ps2_discMicros = ps2_discMaxMicros = 0;
    }
}
#define fill fill_inner
#endif
#ifdef SANDCASTLE_NETLOG
/* testing: the PS3's watchdog - mixes done, and whether the audio thread is in one now (and at which voice) */
extern volatile int ps3dbg_fills, ps3dbg_infill, ps3dbg_mixstep, ps3dbg_mixiter, ps3dbg_mixv[8];
static void fill_mix(int16_t *out, int frames);
static void fill(int16_t *out, int frames) { ps3dbg_infill = 1; fill_mix(out, frames); ps3dbg_infill = 0; ps3dbg_fills++; }
#define fill fill_mix
#endif
static void fill(int16_t *out, int frames)
{
    static int32_t mix[8192 * 2];
    if (frames > 8192) frames = 8192;
    memset(mix, 0, sizeof(int32_t) * (size_t)frames * 2);
    for (int k = 0; k < MAX_VOICES; k++)
    {
        Voice *v = &voices[k];
        if (!v->used) continue;
#ifdef SANDCASTLE_NETLOG
        ps3dbg_infill = 1000 + v->soundId; /* testing: the sound being mixed */
#define MIX_STEP(n) (ps3dbg_mixstep = (n))
#else
#define MIX_STEP(n) ((void)0)
#endif
        unsigned step = (unsigned)(((unsigned long long)v->rate << FRAC_BITS) / (unsigned)outRate);
        double level = level_of(v);
        int volume = (int)(level * 256 + 0.5), lvol = volume * v->lbal >> 8, rvol = volume * v->rbal >> 8;
        for (int i = 0; i < frames; i++)
        {
#ifdef SANDCASTLE_NETLOG
            ps3dbg_mixiter++; ps3dbg_mixv[0] = (int)v->frac; ps3dbg_mixv[1] = (int)step; ps3dbg_mixv[2] = v->at; ps3dbg_mixv[3] = v->buffered; ps3dbg_mixv[4] = v->rate; ps3dbg_mixv[5] = v->kind; ps3dbg_mixv[6] = i; ps3dbg_mixv[7] = frames;
#endif
            while (v->frac >= FRAC_ONE)
            {
                v->frac -= FRAC_ONE;
                v->lastL = v->buffer[v->at * 2]; v->lastR = v->buffer[v->at * 2 + 1];
                MIX_STEP(1); /* decoding */
                if (++v->at >= v->buffered && !decode(v)) { MIX_STEP(2); /* closing */ close_voice(v); MIX_STEP(0); break; }
                MIX_STEP(0);
            }
            if (!v->used) break;
            /* between the last frame and this one (the difference times the fraction fits in an int: 65535 x 32767) */
            int f = (int)v->frac;
            int l = v->lastL + ((v->buffer[v->at * 2] - v->lastL) * f >> FRAC_BITS), r = v->lastR + ((v->buffer[v->at * 2 + 1] - v->lastR) * f >> FRAC_BITS);
            mix[i * 2] += l * lvol >> 8; mix[i * 2 + 1] += r * rvol >> 8;
            v->frac += step;
        }
    }
    if (streamOn) /* a video's sound */
        for (int i = 0; i < frames && streamCount > 0; i++)
        {
            mix[i * 2] += stream[streamRead * 2] * streamVolume >> 8; mix[i * 2 + 1] += stream[streamRead * 2 + 1] * streamVolume >> 8;
            streamRead = (streamRead + 1) % STREAM_FRAMES; streamCount--;
        }
    for (int i = 0; i < frames * 2; i++) out[i] = (int16_t)(mix[i] > 32767 ? 32767 : mix[i] < -32768 ? -32768 : mix[i]);
    if (dump) fwrite(out, 4, (size_t)frames, dump); /* testing: SANDCASTLE_AUDIO_DUMP=file - what was heard, raw 16-bit stereo */
}
#undef fill

/* ---------------- Starting and stopping ---------------- */

static char status[120] = "Sound: not started.";
const char *sound_status(void) { return status; }

void sound_load(const char *gameFolder, int enabled)
{
    snprintf(folder, sizeof folder, "%s", gameFolder);
    char path[1100];
    snprintf(path, sizeof path, "%s/sounds.sdbx", gameFolder);
    soundArchive = pack_archive_open(path);
    cJSON *s, *list = cJSON_GetObjectItem((cJSON *)pack_archive_index(soundArchive), "Sounds");
    files = (SoundFile *)calloc((size_t)cJSON_GetArraySize(list) + 1, sizeof(SoundFile));
    cJSON_ArrayForEach(s, list)
    {
        SoundFile *f = &files[fileCount++];
        f->id = cJSON_GetObjectItem(s, "Id")->valueint;
        f->music = cJSON_IsTrue(cJSON_GetObjectItem(s, "Music"));
        cJSON *vol = cJSON_GetObjectItem(s, "Volume");
        f->volume = cJSON_IsNumber(vol) ? vol->valuedouble : 1;
        snprintf(f->file, sizeof f->file, "%s", cJSON_GetStringValue(cJSON_GetObjectItem(s, "File")));
    }
    if (getenv("SANDCASTLE_AUDIO_DUMP")) dump = fopen(getenv("SANDCASTLE_AUDIO_DUMP"), "wb");
    if (!soundArchive) { snprintf(status, sizeof status, "Sound: sounds.sdbx couldn't be read - no sound."); return; }
    if (!enabled) { snprintf(status, sizeof status, "Sound: off."); return; }
    game_log("Sound archive read. Starting the sound task...");
    audioOn = platform_audio_open(44100, fill);
    snprintf(status, sizeof status, "Sound: %d files, %s at %d Hz.", fileCount, platform_audio_name(), audioOn);
    if (audioOn > 1) outRate = audioOn; /* the rate the machine plays at */
}

static Voice *start_at(int id, double volume, int loop, int owner, double startFade, double scale, double balance);
static Voice *start(int id, double volume, int loop, int owner, double startFade) { return start_at(id, volume, loop, owner, startFade, 1, 0); }

/* scale: how loud it's heard from where it is (0..1); balance -1 left .. 1 right (AudioChannel::calculate2DVolume) */
static Voice *start_at(int id, double volume, int loop, int owner, double startFade, double scale, double balance)
{
    SoundFile *f = file_of(id);
    if (!f || !audioOn) return NULL;
#ifdef PLATFORM_PS2
    /* The PS2: set up under the reader task's lock (it reads the disc through the same windows), not the mixer's - the
       mixer goes on mixing meanwhile; it's given the voice once its first second is decoded. */
#define LOCK_DECODER() ps2_decoder_lock()
#define UNLOCK_DECODER() ps2_decoder_unlock()
#else
#define LOCK_DECODER() platform_audio_lock() /* the sound task decodes as it mixes */
#define UNLOCK_DECODER() platform_audio_unlock()
#endif
#ifdef PLATFORM_PS2
    LOCK_DECODER(); /* opening reads the disc's windows too */
#endif
    if (!f->stream)
    {
        f->stream = pack_archive_stream(soundArchive, f->file); /* read when first played, then kept - still encrypted */
#ifdef PLATFORM_PS2
        if (!f->stream) { UNLOCK_DECODER(); return NULL; }
#else
        if (!f->stream) return NULL;
#endif
    }
#ifndef PLATFORM_PS2
    LOCK_DECODER();
#endif
    unsigned char head[12];
    int svx = pack_stream_read(f->stream, 0, head, 12) == 12 && !memcmp(head, "FORM", 4) && !memcmp(head + 8, "8SVX", 4);
    if (svx && !f->data)
    {
        f->size = pack_stream_length(f->stream); /* 8SVX is played from memory: decrypted whole (they're small) */
        f->data = (unsigned char *)malloc((size_t)f->size);
        if (!f->data || pack_stream_read(f->stream, 0, f->data, f->size) != f->size) { free(f->data); f->data = NULL; UNLOCK_DECODER(); return NULL; }
    }
    Voice *v = NULL;
    for (int i = 0; i < MAX_VOICES && !v; i++) if (!voices[i].used) v = &voices[i];
    if (!v) { UNLOCK_DECODER(); return NULL; }
#ifdef PLATFORM_PS2
    if (v->closing) finish_close(v);
    int16_t *ring = v->ring; unsigned ringSize = v->ringSize;      /* kept from its last sound */
    /* Read ahead far enough to outlast the disc: a real PS2's drive (a burned DVD-R most of all) can take seconds to find
       its place or spin up again - music 8 s ahead (1.4 MB), any other sound longer than one disc window 3 s; a short
       sound 0.4 s, all of it in memory after its first read. Less if memory is short. */
    long length = pack_stream_length(f->stream);
    /* (b576: the PS2 disc's sounds are raw 16-bit - the build turns MP3 into it - so a long one, music or not, is read 8 s ahead too) */
    unsigned want = f->music || length > 2L * 1024 * 1024 ? 352800 : length > 96 * 1024 ? 132300 : 16384;
    if (ringSize < want)
    {
        free(ring); ring = NULL; ringSize = 0;
        for (unsigned size = want; !ring && size >= 16384; size /= 2)
            if ((ring = (int16_t *)malloc((size_t)size * 4)) != NULL) ringSize = size;
    }
    memset(v, 0, sizeof *v);
    v->ring = ring; v->ringSize = ringSize;
    if (!ring) { UNLOCK_DECODER(); return NULL; }
#else
    memset(v, 0, sizeof *v);
#endif
    v->stream = f->stream; v->pos = 0;
#ifdef PLATFORM_PS2
    v->cacheLength = 0;
#endif
    if (svx) { if (open_8svx(v, f->data, f->size)) v->kind = DEC_8SVX; }
    else if (drwav_init(&v->wav, on_read, wav_seek, wav_tell, v, NULL)) { v->kind = DEC_WAV; v->channels = v->wav.channels; v->rate = (int)v->wav.sampleRate; }
    else if ((v->pos = 0, drmp3_init(&v->mp3, on_read, mp3_seek, mp3_tell, NULL, v, NULL))) { v->kind = DEC_MP3; v->channels = (int)v->mp3.channels; v->rate = (int)v->mp3.sampleRate; }
    if (v->kind == DEC_NONE || v->channels < 1 || v->rate < 1000) { finish_close(v); UNLOCK_DECODER(); return NULL; }
    if (v->channels > 2) { /* more than stereo: only the first two are heard */ }
    v->soundId = id; v->music = f->music; v->loop = loop; v->owner = owner;
    v->base = (volume >= 0 ? volume : f->volume) * (scale < 0 ? 0 : scale > 1 ? 1 : scale); v->fade = startFade;
    v->lbal = balance > 0 ? (int)((1 - balance) * 256) : 256; v->rbal = balance < 0 ? (int)((1 + balance) * 256) : 256;
    v->frac = FRAC_ONE;
#ifdef PLATFORM_PS2
    while (v->produced < 8192 && !v->ended) read_ahead(v); /* a start (0.2 s), before the mixer has it; the reader does the rest */
    printf("Game: Sound %d: %s, %d Hz, %d channels, %ld bytes, %u frames read ahead\n", id, v->kind == DEC_WAV ? "WAV" : v->kind == DEC_MP3 ? "MP3" : "8SVX", v->rate, v->channels, pack_stream_length(f->stream), v->ringSize);
    if (decode(v) && v->buffered > 0 && (v->produced > 0))
    {
        v->lastL = v->buffer[0]; v->lastR = v->buffer[1];
        platform_audio_lock(); v->used = 1; platform_audio_unlock();
    }
    else finish_close(v);
    UNLOCK_DECODER();
    ps2_decoder_wake();
#else
    if (decode(v)) { v->lastL = v->buffer[0]; v->lastR = v->buffer[1]; v->used = 1; }
    else close_voice(v);
    UNLOCK_DECODER();
#endif
#undef LOCK_DECODER
#undef UNLOCK_DECODER
    if (v->used) { char key[32]; snprintf(key, sizeof key, "sound:%d", id); conditions_record(key); } /* Sound played */
    return v->used ? v : NULL;
}

void sound_play(int resourceId) { if (resourceId >= 0) start(resourceId, -1, 0, 0, 1); }

/* A sound heard from somewhere (world_hearing_at): quieter by scale, turned to one side by balance. */
void sound_play_at(int resourceId, double scale, double balance) { if (resourceId >= 0 && scale > 0) start_at(resourceId, -1, 0, 0, 1, scale, balance); }

int sound_play_ex(int resourceId, double volume, int loop, int owner, double startFade) { return start(resourceId, volume, loop, owner, startFade) != NULL; }

void sound_stop_id(int resourceId)
{
    platform_audio_lock();
    for (int i = 0; i < MAX_VOICES; i++) if (voices[i].used && voices[i].soundId == resourceId) close_voice(&voices[i]);
    platform_audio_unlock();
}

void sound_stop_owner(int owner)
{
    platform_audio_lock();
    for (int i = 0; i < MAX_VOICES; i++) if (voices[i].used && voices[i].owner == owner) close_voice(&voices[i]);
    platform_audio_unlock();
}

void sound_stop_all(void)
{
    platform_audio_lock();
    for (int i = 0; i < MAX_VOICES; i++) if (voices[i].used) close_voice(&voices[i]);
    platform_audio_unlock();
}

int sound_owner_playing(int owner)
{
    for (int i = 0; i < MAX_VOICES; i++) if (voices[i].used && voices[i].owner == owner) return 1;
    return 0;
}

/* Fades what an owner plays up to full or down to silent over some seconds; down to silent, it stops at the end. */
void sound_fade(int owner, double seconds, int fadeIn)
{
    platform_audio_lock();
    for (int i = 0; i < MAX_VOICES; i++)
    {
        Voice *v = &voices[i];
        if (!v->used || v->owner != owner) continue;
        if (seconds <= 0) { if (fadeIn) v->fade = 1; else close_voice(v); continue; }
        v->fading = 1; v->fadeFrom = v->fade; v->fadeTo = fadeIn ? 1 : 0; v->fadeSeconds = seconds; v->fadeAt = 0; v->stopAtEnd = !fadeIn;
    }
    platform_audio_unlock();
}

void sound_set_volumes(double music, double sounds)
{
    musicVolume = music < 0 ? 0 : music > 1 ? 1 : music;
    soundVolume = sounds < 0 ? 0 : sounds > 1 ? 1 : sounds;
}

/* Each frame: fades move on; sounds that ended are recorded (Sound stopped). */
#ifdef SANDCASTLE_NETLOG
extern volatile int ps3dbg_sound; /* testing: the PS3's watchdog - 1 waiting for the lock, 2 recording ended sounds, 3 fades, 0 done */
#define SOUND_STEP(n) (ps3dbg_sound = (n))
#else
#define SOUND_STEP(n) ((void)0)
#endif
void sound_update(double ms)
{
    SOUND_STEP(1);
    platform_audio_lock();
    SOUND_STEP(2);
    for (int i = 0; i < stoppedCount; i++) { char key[32]; snprintf(key, sizeof key, "soundstop:%d", stoppedIds[i]); conditions_record(key); }
    stoppedCount = 0;
    SOUND_STEP(3);
    for (int i = 0; i < MAX_VOICES; i++)
    {
        Voice *v = &voices[i];
        if (!v->used || !v->fading) continue;
        v->fadeAt += ms / 1000;
        double t = v->fadeAt >= v->fadeSeconds ? 1 : v->fadeAt / v->fadeSeconds;
        v->fade = v->fadeFrom + (v->fadeTo - v->fadeFrom) * t;
        if (t >= 1) { v->fading = 0; if (v->stopAtEnd) close_voice(v); }
    }
    platform_audio_unlock();
    SOUND_STEP(0);
}

int sound_is_music(int resourceId) { SoundFile *f = file_of(resourceId); return f && f->music; }

/* ---------------- A video's sound ---------------- */

void sound_stream_begin(double volume)
{
    platform_audio_lock();
    streamOn = 1; streamRead = streamCount = 0; streamFrac = 0;
    streamVolume = (int)(volume * soundVolume * 256 + 0.5);
    platform_audio_unlock();
}

/* Decoded sound in: resampled to the output rate (straight lines between), mono made stereo. */
void sound_stream_push(const int16_t *pcm, int frames, int rate, int channels)
{
    if (!streamOn || !audioOn || rate <= 0 || channels <= 0) return;
    double step = (double)rate / outRate;
    platform_audio_lock();
    int write = (streamRead + streamCount) % STREAM_FRAMES;
    for (double f = streamFrac; f < frames - 1 && streamCount < STREAM_FRAMES; f += step)
    {
        int i = (int)f;
        double t = f - i;
        const int16_t *a = pcm + (size_t)i * channels, *b = pcm + (size_t)(i + 1) * channels;
        int l = (int)(a[0] + (b[0] - a[0]) * t), r = channels > 1 ? (int)(a[1] + (b[1] - a[1]) * t) : l;
        stream[write * 2] = (int16_t)l; stream[write * 2 + 1] = (int16_t)r;
        write = (write + 1) % STREAM_FRAMES; streamCount++;
        streamFrac = f;
    }
    streamFrac = streamFrac + step - (frames - 1);
    if (streamFrac < 0) streamFrac = 0;
    platform_audio_unlock();
}

/* Seconds of it waiting to be heard (no sound at all: none). */
double sound_stream_buffered(void) { return audioOn && streamOn ? (double)streamCount / outRate : 0; }

void sound_stream_end(void) { platform_audio_lock(); streamOn = 0; streamCount = 0; platform_audio_unlock(); }
