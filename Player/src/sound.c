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

typedef struct SoundFile { int id, music; double volume; char file[64]; unsigned char *data; long size; } SoundFile;
static SoundFile *files; static int fileCount;
static char folder[1024];

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
} Voice;

static Voice voices[MAX_VOICES];
static double musicVolume = 1, soundVolume = 1;
static int audioOn, outRate = 44100;
static FILE *dump;

static SoundFile *file_of(int id) { for (int i = 0; i < fileCount; i++) if (files[i].id == id) return &files[i]; return NULL; }

/* ---------------- Decoding ---------------- */

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

static void close_voice(Voice *v)
{
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
        int volume = (int)(level * 256 + 0.5);
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
            mix[i * 2] += l * volume >> 8; mix[i * 2 + 1] += r * volume >> 8;
            v->frac += step;
        }
    }
    for (int i = 0; i < frames * 2; i++) out[i] = (int16_t)(mix[i] > 32767 ? 32767 : mix[i] < -32768 ? -32768 : mix[i]);
    if (dump) fwrite(out, 4, (size_t)frames, dump); /* testing: SANDCASTLE_AUDIO_DUMP=file - what was heard, raw 16-bit stereo */
}

/* ---------------- Starting and stopping ---------------- */

void sound_load(const char *gameFolder)
{
    snprintf(folder, sizeof folder, "%s", gameFolder);
    char path[1100];
    snprintf(path, sizeof path, "%s/sounds.json", gameFolder);
    char *text = read_whole_file(path, NULL);
    cJSON *root = text ? cJSON_Parse(text) : NULL, *s;
    free(text);
    cJSON *list = cJSON_GetObjectItem(root, "Sounds");
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
    cJSON_Delete(root);
    if (getenv("SANDCASTLE_AUDIO_DUMP")) dump = fopen(getenv("SANDCASTLE_AUDIO_DUMP"), "wb");
    audioOn = platform_audio_open(44100, fill);
    if (audioOn > 1) outRate = audioOn; /* the rate the machine plays at */
}

static Voice *start(int id, double volume, int loop, int owner, double startFade)
{
    SoundFile *f = file_of(id);
    if (!f || !audioOn) return NULL;
    if (!f->data)
    {
        char path[1200];
        snprintf(path, sizeof path, "%s/sounds/%s", folder, f->file);
        f->data = (unsigned char *)read_whole_file(path, &f->size);
        if (!f->data) return NULL;
    }
    platform_audio_lock();
    Voice *v = NULL;
    for (int i = 0; i < MAX_VOICES && !v; i++) if (!voices[i].used) v = &voices[i];
    if (!v) { platform_audio_unlock(); return NULL; }
    memset(v, 0, sizeof *v);
    if (drwav_init_memory(&v->wav, f->data, (size_t)f->size, NULL)) { v->kind = DEC_WAV; v->channels = v->wav.channels; v->rate = (int)v->wav.sampleRate; }
    else if (open_8svx(v, f->data, f->size)) v->kind = DEC_8SVX;
    else if (drmp3_init_memory(&v->mp3, f->data, (size_t)f->size, NULL)) { v->kind = DEC_MP3; v->channels = (int)v->mp3.channels; v->rate = (int)v->mp3.sampleRate; }
    if (v->kind == DEC_NONE || v->channels < 1 || v->rate < 1000) { if (v->kind) close_voice(v); platform_audio_unlock(); return NULL; }
    if (v->channels > 2) { /* more than stereo: only the first two are heard */ }
    v->soundId = id; v->music = f->music; v->loop = loop; v->owner = owner;
    v->base = volume >= 0 ? volume : f->volume; v->fade = startFade;
    v->frac = 1;
    if (decode(v)) { v->lastL = v->buffer[0]; v->lastR = v->buffer[1]; v->used = 1; }
    else close_voice(v);
    platform_audio_unlock();
    return v->used ? v : NULL;
}

void sound_play(int resourceId) { if (resourceId >= 0) start(resourceId, -1, 0, 0, 1); }

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

/* Each frame: fades move on. */
void sound_update(double ms)
{
    platform_audio_lock();
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
