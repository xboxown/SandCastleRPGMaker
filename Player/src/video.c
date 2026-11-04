/*
 * Play Video, as the Windows engine has it (GameTestWindow.Video): a Video
 * Library video over the whole game screen, black bars keeping its shape,
 * the game paused while it plays (as Exult halts the game for its FLIC
 * cutscenes). A click, Escape, Space or Enter skips it when allowed. What
 * waits on it goes on when it ends, is skipped, or can't be played here (the
 * machine's own decoder plays it - on a machine without one it's skipped).
 */
#include "video.h"
#include "pack.h"
#include "sound.h"
#include "save.h"
#include "platform.h"
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

void game_log(const char *line);

static Archive *archive;
typedef struct Playing { int on, skippable, w, h, haveFrame; double t, nextAt; uint32_t *frame, *next; void (*done)(int ok, void *context); void *context; char temp[1200]; } Playing;
static Playing v;

void video_load(const char *gameFolder)
{
    char path[1100];
    snprintf(path, sizeof path, "%s/videos.sdbx", gameFolder);
    archive = pack_archive_open(path); /* none: the game has no videos */
}

static cJSON *entry_of(int id)
{
    cJSON *e;
    cJSON_ArrayForEach(e, cJSON_GetObjectItem((cJSON *)pack_archive_index(archive), "Sounds")) /* the archive's own list (named so for every archive) */
        if (cJSON_IsNumber(cJSON_GetObjectItem(e, "Id")) && cJSON_GetObjectItem(e, "Id")->valueint == id) return e;
    return NULL;
}

int video_exists(int id) { return archive && entry_of(id) != NULL; }
int video_playing(void) { return v.on; }

static void finish(int ok)
{
    if (!v.on) return;
    v.on = 0;
    platform_video_close();
    sound_stream_end();
    free(v.frame); free(v.next); v.frame = v.next = NULL;
    remove(v.temp);
    if (v.done) v.done(ok, v.context);
}

/* Starts it; done(ok, context) when it ends, is skipped (ok) or can't be played (0). 0: it can't start (done isn't called). */
int video_play(int id, int skippable, double volume, void (*done)(int ok, void *context), void *context)
{
    char line[300];
    if (v.on) { game_log("A video is already playing."); return 0; }
    cJSON *e = entry_of(id);
    if (!e) { game_log("Play Video: that video isn't in the game."); return 0; }
    const char *file = cJSON_GetStringValue(cJSON_GetObjectItem(e, "File")), *name = cJSON_GetStringValue(cJSON_GetObjectItem(e, "Name"));
    /* out of the archive into a file of its own for the decoder, beside the saves */
    PackStream *s = pack_archive_stream(archive, file);
    if (!s) { game_log("Play Video: the video couldn't be read."); return 0; }
    platform_make_dir(save_folder());
    snprintf(v.temp, sizeof v.temp, "%s/video_%d.tmp.mp4", save_folder(), id);
    FILE *out = fopen(v.temp, "wb");
    long length = pack_stream_length(s), at = 0;
    static unsigned char chunk[65536];
    while (out && at < length)
    {
        long n = pack_stream_read(s, at, chunk, sizeof chunk);
        if (n <= 0 || fwrite(chunk, 1, (size_t)n, out) != (size_t)n) { fclose(out); out = NULL; break; }
        at += n;
    }
    pack_stream_close(s);
    if (!out) { remove(v.temp); game_log("Play Video: the video couldn't be written out to play."); return 0; }
    fclose(out);
    int w, h;
    if (!platform_video_open(v.temp, &w, &h))
    {
        remove(v.temp);
        snprintf(line, sizeof line, "\"%s\" can't be played on this machine (%s) - skipped.", name ? name : "The video", cJSON_GetStringValue(cJSON_GetObjectItem(e, "Codec")) ? cJSON_GetStringValue(cJSON_GetObjectItem(e, "Codec")) : "?");
        game_log(line);
        if (done) done(0, context);
        return 1;
    }
    v.on = 1; v.skippable = skippable; v.w = w; v.h = h; v.t = 0; v.haveFrame = 0; v.done = done; v.context = context;
    v.frame = (uint32_t *)calloc((size_t)w * h, 4); v.next = (uint32_t *)calloc((size_t)w * h, 4);
    if (!v.frame || !v.next) { finish(0); return 1; }
    v.nextAt = platform_video_next(v.next, &v.nextAt) ? v.nextAt : -1;
    sound_stream_begin(volume < 0 ? 0 : volume > 1 ? 1 : volume);
    snprintf(line, sizeof line, "Playing \"%s\".", name ? name : "a video");
    game_log(line);
    return 1;
}

void video_update(double ms)
{
    if (!v.on) return;
    v.t += ms / 1000;
    /* the frames due by now: the latest one is shown */
    while (v.nextAt >= 0 && v.nextAt <= v.t)
    {
        uint32_t *swap = v.frame; v.frame = v.next; v.next = swap;
        v.haveFrame = 1;
        double at;
        v.nextAt = platform_video_next(v.next, &at) ? at : -1;
    }
    /* the sound kept a little ahead */
    static int16_t pcm[16384];
    int rate, channels, guard = 0;
    while (sound_stream_buffered() < 0.5 && !platform_video_audio_ended() && guard++ < 8)
    {
        int n = platform_video_audio(pcm, 16384 / 2, &rate, &channels);
        if (n > 0) sound_stream_push(pcm, n, rate, channels);
    }
    if (v.nextAt < 0 && (platform_video_audio_ended() || sound_stream_buffered() <= 0)) finish(1); /* the end */
}

/* Over everything: its picture scaled to the screen keeping its shape, black bars round it. */
void video_draw(uint32_t *pixels)
{
    if (!v.on) return;
    for (int i = 0; i < FRAME_PIXELS; i++) pixels[i] = 0xFF000000u;
    if (!v.haveFrame) return;
    double scale = (double)FRAME_W / v.w < (double)FRAME_H / v.h ? (double)FRAME_W / v.w : (double)FRAME_H / v.h;
    int dw = (int)(v.w * scale), dh = (int)(v.h * scale), ox = (FRAME_W - dw) / 2, oy = (FRAME_H - dh) / 2;
    for (int y = 0; y < dh; y++)
    {
        const uint32_t *row = v.frame + (size_t)(y * v.h / dh) * v.w;
        uint32_t *dst = pixels + (size_t)(oy + y) * FRAME_W + ox;
        for (int x = 0; x < dw; x++) dst[x] = row[x * v.w / dw];
    }
}

/* A click, Escape, Space or Enter: skipped if it may be. 1 = taken (every key and click is while it plays). */
int video_input(int skipping)
{
    if (!v.on) return 0;
    if (skipping && v.skippable) finish(1);
    return 1;
}

void video_stop(void) { v.done = NULL; finish(0); }
