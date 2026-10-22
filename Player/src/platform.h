/*
 * The Sandcastle player's platform layer: the one part that differs from
 * machine to machine. SDL2 (platform_sdl2.c) serves Windows (for testing),
 * Linux, Mac, MorphOS and AmigaOS 4; the Amiga 68k layer (RTG through
 * cybergraphics, AHI or Paula sound) has its own file. Everything else of
 * the player is plain C99 that only calls these.
 */
#ifndef SANDCASTLE_PLATFORM_H
#define SANDCASTLE_PLATFORM_H

#include <stdint.h>

/* The game's screen: 320 x 200 pixels (as Ultima VII's), 0xAARRGGBB. */
#define SCREEN_W 320
#define SCREEN_H 200

enum PlatformKey
{
    KEY_NONE, KEY_LEFT, KEY_RIGHT, KEY_UP, KEY_DOWN, KEY_ESCAPE, KEY_ENTER, KEY_SPACE, KEY_F11, KEY_TAB, KEY_PAGEUP, KEY_PAGEDOWN,
    KEY_1, KEY_2, KEY_3, KEY_4, KEY_5, KEY_6, KEY_7, KEY_8, KEY_9, KEY_C, KEY_I, KEY_S, KEY_BACKSPACE, KEY_Z, KEY_OTHER
};

typedef struct PlatformEvent
{
    enum { EV_NONE, EV_QUIT, EV_KEY_DOWN, EV_KEY_UP, EV_MOUSE_DOWN, EV_MOUSE_UP, EV_MOUSE_MOVE, EV_TEXT } type;
    enum PlatformKey key;
    int mouseX, mouseY;   /* in screen pixels (0..319, 0..199) */
    int button;           /* 1 left, 2 right */
    int ch;               /* EV_TEXT: the character typed (printable ASCII) */
} PlatformEvent;

/* Opens the game's window (or screen) titled so; 0 on failure (the reason in platform_error()). */
int platform_open(const char *title, int fullscreen);
void platform_close(void);
const char *platform_error(void);

/* The next thing that happened (0 when there's nothing left this frame). */
int platform_poll(PlatformEvent *event);

/* Shows the 320 x 200 frame, scaled to the window. */
void platform_present(const uint32_t *pixels);

/* Milliseconds since some fixed moment. */
uint32_t platform_ticks(void);
void platform_sleep(uint32_t ms);

/* Window <-> full screen. */
void platform_toggle_fullscreen(void);

/* Free memory, in megabytes (-1 if unknown). The Amiga checks a game's needs against it. */
long platform_free_memory_mb(void);

/* Whether the machine can run the game at all (an Amiga: a 68060 or 68080 with its FPU); 0 with the reason in platform_error(). */
int platform_can_run(void);

/* Sound out: the mixer is asked (on another thread) for frames of 16-bit stereo at this rate. 0 when there's no sound. */
typedef void (*AudioFill)(int16_t *stereo, int frames);
int platform_audio_open(int rate, AudioFill fill);   /* the rate it plays at (0: no sound) */
void platform_audio_close(void);
void platform_audio_lock(void);
void platform_audio_unlock(void);
const char *platform_audio_name(void);

/* Videos: the machine's own decoder (Windows: Media Foundation); 0 from open where there's none. */
int platform_video_open(const char *path, int *width, int *height);
int platform_video_next(uint32_t *frame, double *seconds);                 /* the next frame (width x height) and its time; 0 at the end */
int platform_video_audio(int16_t *out, int max, int *rate, int *channels);  /* the next sound decoded (interleaved 16-bit frames) */
int platform_video_audio_ended(void);
void platform_video_close(void);

/* Folders for the saves: made (1 if it's there now), removed when empty. */
int platform_make_dir(const char *path);
int platform_remove_dir(const char *path);
void platform_audio_route(int which);   /* Amiga: 0 AHI else Paula, 1 Paula only, 2 AHI only */  /* what it plays through ("AHI", "Paula", "SDL", "none") */

/* A message box (the Amiga's requester): why a game can't start, started from an icon with no console to print to. */
void platform_message(const char *heading, const char *text);

#endif
