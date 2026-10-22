/* The things that work: keys, lockpicks, keychains, windows, levers, ladders, sextant, map, clock, instruments (GameTestWindow.Locks / Travel / Mechanisms). */
#ifndef SANDCASTLE_MECHANISMS_H
#define SANDCASTLE_MECHANISMS_H
#include <stdint.h>
#include "items.h"

int mechanisms_use_map(int uid);        /* double-clicked on the map: 1 = handled */
int mechanisms_use_carried(Item *it);   /* double-clicked where it's carried */
void mechanisms_update(double ms);      /* buttons springing back */
void mechanisms_reset(void);

/* A map being looked at (display_map): over everything, the next click puts it away. */
int mechanisms_map_open(void);
int mechanisms_map_click(void);
void mechanisms_draw(uint32_t *pixels);

#endif
