/*
 * The game world in the player: the map (its floors), the things on it, the
 * characters, the main player; drawing it as the Windows engine does, and
 * walking by Exult's rules. (Milestone 1: a world to walk in.)
 */
#ifndef SANDCASTLE_WORLD_H
#define SANDCASTLE_WORLD_H

#include <stdint.h>

/* Loads <folder>/game.json and sprites.json; 0 on failure (why in world_error()). */
int world_load(const char *folder);
const char *world_error(void);
const char *world_title(void);

/* The direction keys held now (dx, dy: -1, 0 or 1; the latest pressed wins), and the time passed. */
void world_update(int dx, int dy, double seconds);

/* Draws the 320 x 200 view into pixels (0xAARRGGBB). */
void world_draw(uint32_t *pixels, uint32_t ms);

#endif
