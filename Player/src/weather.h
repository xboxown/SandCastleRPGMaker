/* Weather: rain, snow, hail, storms, cloud shadows (GameTestWindow.Weather). */
#ifndef SANDCASTLE_WEATHER_H
#define SANDCASTLE_WEATHER_H

#include <stdint.h>

void weather_load(void);
void weather_change(int kind, int intensity, int clouds);   /* Clear 0, Rain 1, Snow 2, Hail 3, Storm 4; clouds -1 = as it is */
void weather_update(double ms);                              /* not while the game is paused */
void weather_draw_clouds(uint32_t *pixels);                  /* over the world, under the sky's light */
void weather_draw(uint32_t *pixels);                         /* over the sky's light */

#endif
