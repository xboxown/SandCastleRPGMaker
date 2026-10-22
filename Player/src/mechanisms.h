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

/* Growing, making, sleeping, liquids (GameTestWindow.Crafting / Travel / Consumables). */
void mechanisms_lay_down(int uid);                              /* the player lay down on a bed or bedroll: how long to sleep */
int mechanisms_key(int key);                                    /* the sleep slider's keys: 1 = taken */
int mechanisms_pausing(void);                                   /* the slider up, or the party asleep: the game waits */
int mechanisms_drop_on(int uid, int category, int itemId);      /* a seed dropped on its soil: sown (1) */
int mechanisms_click(int x, int y);                             /* the slider, the map, sleeping: 1 = taken */
void mechanisms_frame(double ms);                               /* every frame, paused or not (the sleep fade) */

#endif
