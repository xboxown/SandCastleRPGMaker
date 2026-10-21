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
const char *world_map_name(void);
int world_light_strength(int *gives);
void world_actor_pixel(int actor, double *px, double *py);
int world_tile_w(void);
int world_tile_h(void);
int world_map_w(void);
int world_map_h(void);
int world_walk_blocked(int actor);
int world_on_screen(double px, double py, double margin);
void world_view_center(double *x, double *y);
int world_indoors(void);
void world_camera_hold(int held, double x, double y);
void world_camera_center(double *x, double *y);
void world_camera_follow(int actor);
int world_actor_gone(int actor);
void world_actor_remove(int actor);
void world_actor_place(int actor, int x, int y, int onFloor);
void world_change_look(int actor, int other);        /* other -1: its own look again */
void world_thing_look(int uid, const Sprite *sprite);
void world_thing_move(int uid, int x, int y, int onFloor);
void world_door_set(int uid, const char *state);
int world_set_tile(int x, int y, int onFloor, int tile);
void world_light_state(int uid, int *lit, int *spent, double *burnLeft);
void world_set_light_state(int uid, int lit, int spent, double burnLeft);
int world_light_spent(int uid);
void world_light_lit_now(int uid);
void world_burn_lights(double minutes);
void *world_thing_own_contents(int uid);   /* a cJSON array, or NULL */
int world_lever_on(int objectId, int on);
int world_door_open_any(int objectId, int open);
/* Chairs and beds: sitting / lying down, getting up, the Sit and Sleep schedules, the main character's walk-up-and-sit. */
int world_go_to_furniture(int actor, int bed, int onlyUid);
int world_arrive_furniture(int actor);      /* 1 seated, -1 the spot was taken meanwhile, 0 wasn't heading for one */
void world_stand_up(int actor);
int world_seated(int actor);
int world_seated_on(int actor);
int world_player_sit(int uid);
int world_player_sitting_down(void);
void world_cancel_player_sit(void);
void world_hit(int sx, int sy, int *actor, int *uid);
int world_thing_type(int uid);
/* A chair's or bed's own event pages (By Enter / By Exit when someone sits or gets up) - events.c. */
void world_furniture_event(int uid, int trigger, int actor);
/* Using things: doors, lights, covers. */
int world_thing_object_id(int uid);
int world_thing_instance(int uid);
void world_door_state(int uid, int *open, int *locked);
void world_door_open(int uid);
int world_door_close(int uid);                    /* 0: someone is in the doorway */
void world_door_set_locked(int uid, int locked);
int world_light_lit(int uid);
void world_light_set(int uid, int lit);
int world_bed_under_cover(int uid);
int world_nearest_bed(void);
void world_bark_thing(int uid, const char *text, int ms);
int world_actor_facing(int actor);
/* Walk To Location: a way there (A* by Exult's walking rules), walked at the player's pace; 0 when there's none just now. floor 0 = any. */
int world_walk_to(int actor, int x, int y, int floor, int beside);
int world_walking(int actor);
int world_keys_held(void); /* a direction key held (it takes over from an event's walk) */
void world_walk_cancel(int actor);
void world_actor_cell(int actor, int *x, int *y, int *onFloor);
int world_can_stand(int actor, int x, int y, int onFloor);
/* Things on the map (uid: a thing's own number while the game runs; category: Book 3, Object 4, Container 5). */
int world_find_object(int instanceId, int objectId, int x, int y);
int world_find_item(int category, int itemId, int x, int y);
int world_thing_there(int uid);
void world_thing_info(int uid, int *category, int *itemId, int *quantity, int *x, int *y, int *instanceId);
void world_thing_remove(int uid);
int world_thing_put(int category, int itemId, int quantity, int x, int y, int onFloor);
int world_pose_length(int actor, const char *pose);
void world_face_cell(int actor, int x, int y, const char *pose);
/* Words over someone's head for a while (Show Bark), drawn by world_draw_barks over everything. */
void world_bark(int actor, const char *text, int ms);
void world_draw_barks(uint32_t *pixels);
/* The game (game.json) as read, kept while the game runs (a cJSON root). */
void *world_json(void);

#endif
