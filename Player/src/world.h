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

/* ---- The characters, for events (actor 0 is the main player, 1.. the others on the map) ---- */
#include "pictures.h"
int world_actor_count(void);
int world_actor_by_character(int characterId);   /* -1 when not on this map */
int world_actor_character(int actor);
const Sprite *world_actor_face(int actor);
int world_actor_in_sight(int actor);
int world_actor_at(int screenX, int screenY);    /* the last frame drawn; -1 for none */
int world_direction_of(const char *name);        /* "North".."NorthWest" -> 0..7, "" -> -1 */
int world_set_pose(int actor, const char *pose, int facing);
int world_teleport(int actor, int x, int y);
void world_player_cell(int *x, int *y, int *onFloor);
void world_turn_to_player(int actor);
/* Walk To Location: a way there (A* by Exult's walking rules), walked at the player's pace; 0 when there's none just now. floor 0 = any. */
int world_walk_to(int actor, int x, int y, int floor);
int world_walking(int actor);
int world_keys_held(void); /* a direction key held (it takes over from an event's walk) */
void world_walk_cancel(int actor);
void world_actor_cell(int actor, int *x, int *y, int *onFloor);
int world_can_stand(int actor, int x, int y, int onFloor);
/* The game (game.json) as read, kept while the game runs (a cJSON root). */
void *world_json(void);

#endif
