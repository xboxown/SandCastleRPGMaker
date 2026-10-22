/* Screen commands: fades, flashes, pictures over the game, shaking (GameTestWindow.ScriptGame). */
#ifndef SANDCASTLE_SCREEN_H
#define SANDCASTLE_SCREEN_H

#include <stdint.h>
#include "pictures.h"

uint32_t screen_parse_colour(const char *text, uint32_t fallback);
double screen_fade(uint32_t colour, int out, double seconds);   /* how long it takes (ms) */
void screen_flash(uint32_t colour, double seconds);
void screen_show_picture(int number, const Sprite *sprite, double x, double y, double opacity, uint32_t now);
void screen_erase_picture(int number);                          /* 0 = all */
void screen_shake(int intensity, double seconds);
void screen_update(double ms);                                   /* paused or not */
void screen_draw(uint32_t *pixels, uint32_t now);
void screen_shake_apply(uint32_t *pixels);

void screen_reset(void);

#endif
