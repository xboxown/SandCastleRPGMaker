/* Tiles, traps, fires and flying projectiles (GameTestWindow.ObjectsTick / Projectiles). */
#ifndef SANDCASTLE_TRAPS_H
#define SANDCASTLE_TRAPS_H
#include <stdint.h>

void traps_update(double ms);
void traps_draw(uint32_t *pixels, uint32_t ms);              /* projectiles in the air */
void traps_dropped(int uid, int category, int itemId);       /* put down on the map: By Item Dropped On It */
void traps_launch(int objectId, double x, double y, double dx, double dy, int lift, int shooter);
void traps_reset(void);

#endif
