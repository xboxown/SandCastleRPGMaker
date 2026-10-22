/* The sky's light over the world: time of day, lights in the dark, Change Day/Night (GameTestWindow.Lighting). */
#ifndef SANDCASTLE_LIGHTING_H
#define SANDCASTLE_LIGHTING_H

#include <stdint.h>

void lighting_load(void);
void lighting_update(double ms);
void lighting_draw(uint32_t *pixels);   /* over the world, under the boxes */
void lighting_period(const char *period);
void lighting_palette(const char *hex, double opacity);
void lighting_weather(int kind, int intensity, int clouds, int cover);

void lighting_special(double minutes);
void lighting_minutes_passed(double minutes);

#endif
