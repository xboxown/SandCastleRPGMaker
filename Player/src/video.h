/* Play Video and the title's intro video (GameTestWindow.Video): the machine's own decoder, skipped where there's none. */
#ifndef SANDCASTLE_VIDEO_H
#define SANDCASTLE_VIDEO_H
#include <stdint.h>

void video_load(const char *gameFolder);
int video_exists(int id);
int video_play(int id, int skippable, double volume, void (*done)(int ok, void *context), void *context);
int video_playing(void);              /* the game waits while one plays */
void video_update(double ms);
void video_draw(uint32_t *pixels);
int video_input(int skipping);        /* a key or click while one plays: 1 = taken */
void video_stop(void);                /* a new game, or quitting: it just stops */

#endif
