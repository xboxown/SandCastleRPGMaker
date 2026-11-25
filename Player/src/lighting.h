/* The sky's light over the world: time of day, lights in the dark, Change Day/Night (GameTestWindow.Lighting). */
#ifndef SANDCASTLE_LIGHTING_H
#define SANDCASTLE_LIGHTING_H

#include <stdint.h>

void lighting_load(void);
void lighting_update(double ms);
int lighting_shade(uint32_t *keep, uint32_t *rbAdd, uint32_t *gAdd);   /* the sky's light, drawn into the world (world.c) */
void lighting_period(const char *period);
void lighting_palette(const char *hex, double opacity);
void lighting_weather(int kind, int intensity, int clouds, int cover);

void lighting_special(double minutes);

/* Ultima 6's darkness (Engine/U6Lighting.cs): the avatar's globe level now (0: day), the light spell, a globe added into a
   grid, a lit light's level by its brightness, whether a world pixel of a cell with light 0-4 is dark. */
int lighting_u6_level(void);
int lighting_u6_spell(void);
void lighting_u6_globe(unsigned char *shade, int w, int h, int cx, int cy, int level);
int lighting_u6_object_level(int brightness);
int lighting_u6_dark(int light, int px, int py);
void lighting_minutes_passed(double minutes);

#endif
