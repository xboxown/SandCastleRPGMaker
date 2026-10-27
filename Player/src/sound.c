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
    double frac;                                  /* the position between frames, for resampling */
    int16_t lastL, lastR;
    const PackStream *stream; long pos;           /* the file it's read from, and where */
    int lbal, rbal;                               /* heard from one side (world_hearing_at): 0..256 each side */
} Voice;

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

/* The next frames of the voice into its buffer, as stereo; 0 at the end. */
static int decode(Voice *v)
{
    int16_t raw[CHUNK * 2];
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
        v->buffer[i * 2] = l; v->buffer[i * 2 + 1] = r;
    }
    v->buffered = (int)got; v->at = 0;
    return 1;
}

static volatile int stoppedIds[64]; static volatile int stoppedCount; /* sounds that ended (on the audio thread), recorded on the game's */

static void close_voice(Voice *v)
{
    if (v->used && stoppedCount < 64) stoppedIds[stoppedCount++] = v->soundId;
    if (v->kind == DEC_WAV) drwav_uninit(&v->wav);
    else if (v->kind == DEC_MP3) drmp3_uninit(&v->mp3);
    v->used = 0; v->kind = DEC_NONE;
}

/* ---------------- Mixing (the audio thread) ---------------- */

static double level_of(const Voice *v) { double l = v->base * v->fade * (v->music ? musicVolume : soundVolume); return l < 0 ? 0 : l > 1 ? 1 : l; }

static void fill(int16_t *out, int frames)
{
    static int32_t mix[8192 * 2];
    if (frames > 8192) frames = 8192;
    memset(mix, 0, sizeof(int32_t) * (size_t)frames * 2);
    for (int k = 0; k < MAX_VOICES; k++)
    {
        Voice *v = &voices[k];
        if (!v->used) continue;
        double step = (double)v->rate / outRate, level = level_of(v);
        int volume = (int)(level * 256 + 0.5), lvol = volume * v->lbal >> 8, rvol = volume * v->rbal >> 8;
        for (int i = 0; i < frames; i++)
        {
            while (v->frac >= 1)
            {
                v->frac -= 1;
                v->lastL = v->buffer[v->at * 2]; v->lastR = v->buffer[v->at * 2 + 1];
                if (++v->at >= v->buffered && !decode(v)) { close_voice(v); break; }
            }
            if (!v->used) break;
            /* between the last frame and this one */
            int l = v->lastL + (int)((v->buffer[v->at * 2] - v->lastL) * v->frac), r = v->lastR + (int)((v->buffer[v->at * 2 + 1] - v->lastR) * v->frac);
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
    if (!f->stream)
    {
        f->stream = pack_archive_stream(soundArchive, f->file); /* read when first played, then kept - still encrypted */
        if (!f->stream) return NULL;
    }
    unsigned char head[12];
    int svx = pack_stream_read(f->stream, 0, head, 12) == 12 && !memcmp(head, "FORM", 4) && !memcmp(head + 8, "8SVX", 4);
    if (svx && !f->data)
    {
        f->size = pack_stream_length(f->stream); /* 8SVX is played from memory: decrypted whole (they're small) */
        f->data = (unsigned char *)malloc((size_t)f->size);
        if (!f->data || pack_stream_read(f->stream, 0, f->data, f->size) != f->size) { free(f->data); f->data = NULL; return NULL; }
    }
    platform_audio_lock();
    Voice *v = NULL;
    for (int i = 0; i < MAX_VOICES && !v; i++) if (!voices[i].used) v = &voices[i];
    if (!v) { platform_audio_unlock(); return NULL; }
    memset(v, 0, sizeof *v);
    v->stream = f->stream; v->pos = 0;
    if (svx) { if (open_8svx(v, f->data, f->size)) v->kind = DEC_8SVX; }
    else if (drwav_init(&v->wav, on_read, wav_seek, wav_tell, v, NULL)) { v->kind = DEC_WAV; v->channels = v->wav.channels; v->rate = (int)v->wav.sampleRate; }
    else if ((v->pos = 0, drmp3_init(&v->mp3, on_read, mp3_seek, mp3_tell, NULL, v, NULL))) { v->kind = DEC_MP3; v->channels = (int)v->mp3.channels; v->rate = (int)v->mp3.sampleRate; }
    if (v->kind == DEC_NONE || v->channels < 1 || v->rate < 1000) { if (v->kind) close_voice(v); platform_audio_unlock(); return NULL; }
    if (v->channels > 2) { /* more than stereo: only the first two are heard */ }
    v->soundId = id; v->music = f->music; v->loop = loop; v->owner = owner;
    v->base = (volume >= 0 ? volume : f->volume) * (scale < 0 ? 0 : scale > 1 ? 1 : scale); v->fade = startFade;
    v->lbal = balance > 0 ? (int)((1 - balance) * 256) : 256; v->rbal = balance < 0 ? (int)((1 + balance) * 256) : 256;
    v->frac = 1;
    if (decode(v)) { v->lastL = v->buffer[0]; v->lastR = v->buffer[1]; v->used = 1; }
    else close_voice(v);
    platform_audio_unlock();
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
void sound_update(double ms)
{
    platform_audio_lock();
    for (int i = 0; i < stoppedCount; i++) { char key[32]; snprintf(key, sizeof key, "soundstop:%d", stoppedIds[i]); conditions_record(key); }
    stoppedCount = 0;
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
