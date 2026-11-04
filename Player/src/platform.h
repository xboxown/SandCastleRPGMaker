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

/* The picture itself: the game still thinks in those 320 x 200, but on a machine built with FRAME_HIRES (MorphOS) each of them is
   frame_scale pixels across, so art finer than 320 x 200 keeps all its pixels, as the Windows engine draws it. On the
   Amiga 68k it is simply 1, at compile time: the same code, not a cycle slower. */
#ifdef FRAME_HIRES
extern int frame_scale;
#else
#define frame_scale 1
#endif
#define FRAME_W (SCREEN_W * frame_scale)
#define FRAME_H (SCREEN_H * frame_scale)
#define FRAME_PIXELS (FRAME_W * FRAME_H)

/* A rectangle in the game's 320 x 200, clipped, as rows in the picture. */
#define FRAME_CLIP(x, y, w, h, x0, y0, x1, y1) \
    int x0 = (x) < 0 ? 0 : (x) * frame_scale, y0 = (y) < 0 ? 0 : (y) * frame_scale, \
        x1 = (x) + (w) >= SCREEN_W ? FRAME_W : ((x) + (w)) * frame_scale, y1 = (y) + (h) >= SCREEN_H ? FRAME_H : ((y) + (h)) * frame_scale

static inline void frame_fill(uint32_t *p, int x, int y, int w, int h, uint32_t c)
{
    FRAME_CLIP(x, y, w, h, x0, y0, x1, y1);
    for (int j = y0; j < y1; j++) for (int i = x0; i < x1; i++) p[j * FRAME_W + i] = c;
}

/* One of the game's pixels: a block of the picture's. */
static inline void frame_dot(uint32_t *p, int x, int y, uint32_t c) { if (x >= 0 && y >= 0 && x < SCREEN_W && y < SCREEN_H) frame_fill(p, x, y, 1, 1, c); }

enum PlatformKey
{
    KEY_NONE, KEY_LEFT, KEY_RIGHT, KEY_UP, KEY_DOWN, KEY_ESCAPE, KEY_ENTER, KEY_SPACE, KEY_F11, KEY_TAB, KEY_PAGEUP, KEY_PAGEDOWN,
    KEY_1, KEY_2, KEY_3, KEY_4, KEY_5, KEY_6, KEY_7, KEY_8, KEY_9, KEY_C, KEY_I, KEY_S, KEY_BACKSPACE, KEY_Z, KEY_CTRL_Q, KEY_OTHER
};

typedef struct PlatformEvent
{
    enum { EV_NONE, EV_QUIT, EV_KEY_DOWN, EV_KEY_UP, EV_MOUSE_DOWN, EV_MOUSE_UP, EV_MOUSE_MOVE, EV_TEXT } type;
    enum PlatformKey key;
    int mouseX, mouseY;   /* in screen pixels (0..319, 0..199) */
    int button;           /* 1 left, 2 right */
    int ch;               /* EV_TEXT: the character typed (printable ASCII) */
} PlatformEvent;

/* The window's size the project asks for (System Settings > Game Window): 2 to 6 times 320 x 200. Before platform_open. */
void platform_want_window_scale(int scale);

/* Opens the game's window (or screen) titled so; 0 on failure (the reason in platform_error()). */
int platform_open(const char *title, int fullscreen);
void platform_close(void);
const char *platform_error(void);

/* The next thing that happened (0 when there's nothing left this frame). */
int platform_poll(PlatformEvent *event);

/* Shows the frame (FRAME_W x FRAME_H), scaled to the window. */
void platform_present(const uint32_t *pixels);

/* Milliseconds since some fixed moment. */
uint32_t platform_ticks(void);
/* Ctrl held down now (a drop with Ctrl: the whole stack, no slider - Exult's alternate drop). */
int platform_ctrl_held(void);
double platform_ms_precise(void);
void platform_warp_mouse(int x, int y);   /* testing: the system's pointer put there (game pixels) */   /* milliseconds since starting, to a fraction (the timing diary in Game.log) */
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
