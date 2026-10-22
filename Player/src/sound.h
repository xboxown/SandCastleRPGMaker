/* Sounds and music from the project's Sound list (SoundPlayback): SDL audio; AHI or Paula on the Amiga. */
#ifndef SANDCASTLE_SOUND_H
#define SANDCASTLE_SOUND_H
#include <stdint.h>

/* Who a sound belongs to (stopped and faded together). */
enum { OWNER_NONE = 0, OWNER_MUSIC = 1, OWNER_AMBIENT = 2 };

void sound_load(const char *gameFolder, int enabled); /* enabled 0: "nosound" */
const char *sound_status(void);                       /* for Game.log */
/* A sound by its resource id (-1 = none), at its own volume, once. */
void sound_play(int resourceId);
/* volume -1 = its own; startFade 0 = silent (for a fade in) .. 1 */
int sound_play_ex(int resourceId, double volume, int loop, int owner, double startFade);
void sound_stop_id(int resourceId);
void sound_stop_owner(int owner);
void sound_stop_all(void);
int sound_owner_playing(int owner);
void sound_fade(int owner, double seconds, int fadeIn);
void sound_set_volumes(double music, double sounds);
void sound_update(double ms);
void sound_stream_begin(double volume);
void sound_stream_push(const int16_t *pcm, int frames, int rate, int channels);
double sound_stream_buffered(void);
void sound_stream_end(void);

#endif
