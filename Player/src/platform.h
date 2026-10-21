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
    KEY_NONE, KEY_LEFT, KEY_RIGHT, KEY_UP, KEY_DOWN, KEY_ESCAPE, KEY_ENTER, KEY_SPACE, KEY_F11, KEY_OTHER
};

typedef struct PlatformEvent
{
    enum { EV_NONE, EV_QUIT, EV_KEY_DOWN, EV_KEY_UP, EV_MOUSE_DOWN, EV_MOUSE_UP, EV_MOUSE_MOVE } type;
    enum PlatformKey key;
    int mouseX, mouseY;   /* in screen pixels (0..319, 0..199) */
    int button;           /* 1 left, 2 right */
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

/* A message box (the Amiga's requester): why a game can't start, started from an icon with no console to print to. */
void platform_message(const char *heading, const char *text);

#endif
