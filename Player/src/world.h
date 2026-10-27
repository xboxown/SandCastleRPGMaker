/*
 * The game world in the player: the map (its floors), the things on it, the
 * characters, the main player; drawing it as the Windows engine does, and
 * walking by Exult's rules. (Milestone 1: a world to walk in.)
 */
#ifndef SANDCASTLE_WORLD_H
#define SANDCASTLE_WORLD_H

#include <stdint.h>
#include "../third_party/cJSON.h"

/* Loads <folder>/game.json and sprites.json; 0 on failure (why in world_error()). */
int world_load(const char *folder);
int world_build(cJSON *root);   /* from a save's data */
void world_reset(void);
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
int world_get_tile(int x, int y, int onFloor);
int world_effect_start(const Sprite *sprite, int follows, int cellX, int cellY);   /* follows: an actor, or -1 for the cell */
void world_effect_stop(int id);
void world_hit_outline(int actor);
int world_actor_lift(int actor);
int world_character_source(int characterId);
int world_thing_hp(int uid, int initial);
void world_thing_set_hp(int uid, int hp);
int world_thing_z(int uid);
int world_thing_floor(int uid);
int world_thing_spent(int uid);
int world_thing_belongs_to(int uid);
void world_thing_set_belongs(int uid, int characterId);
int world_trap_sprung(int uid);
void world_trap_set_sprung(int uid);
int world_thing_facing(int uid);
void world_camera(double *x, double *y);
void world_maybe_bleed(int actor, int damage);
/* A map that wraps around: distances the short way round (pixels, cells). */
double world_wrap_dx(double dx);
double world_wrap_dy(double dy);
int world_wrap_cells_x(int d);
int world_wrap_cells_y(int d);
int world_wraps(void);
void world_update_bleeding(double ms);
void world_set_key_check(int (*check)(int actor, int doorUid));
void world_set_door_breaker(int (*breaks)(int actor, int doorUid, int locked), void (*bash)(int actor, int doorUid));
void world_lock_own_doors(int actor, int cells);
int world_heading_to_seat(int actor);
void world_thing_break_glass(int uid, const Sprite *broken);
void world_thing_center(int uid, double *px, double *py);
int world_things_near(double px, double py, double reach, int *out, int max);
void world_effect_once(const Sprite *sprite, int cellX, int cellY);
void world_blit(uint32_t *pixels, const Picture *picture, int x, int y, int w, int h);
const Sprite *world_item_look(int category, int itemId, int lit, int spent, int locked, int quantity, int liquid);
const Sprite *world_thing_sprite(int uid);
void world_screen_cell(int sx, int sy, int *x, int *y, int *onFloor);
int world_reachable(int x, int y, int z);
int world_reachable_thing(int uid);   /* Exult is_grabable for a thing on the map (its own box) */
void world_thing_add_quantity(int uid, int n);
int world_thing_quantity(int uid);
int world_thing_at_cell(int category, int itemId, int x, int y, int except);
void world_thing_drop_at(int uid, int screenX, int screenY, int onFloor);
/* Saving and loading. */
void world_save(cJSON *map, cJSON *runtime, cJSON *(*extra)(int uid, cJSON *entry));
void world_restore_player(cJSON *runtime);
cJSON *world_thing_placed(int uid);
int world_thing_count(void);
int world_thing_uid_at(int i);
const char *world_actor_name(int actor);
int world_add_actor(cJSON *character, int x, int y, int onFloor);
int world_body_of(int characterId);
int world_thing_open(int uid);
void world_thing_set_open(int uid, int open);
int world_thing_lit(int uid);
void world_thing_set_lit(int uid, int lit);
void world_thing_set_spent(int uid, int spent);
int world_thing_stage(int uid);
int world_thing_stage_count(int uid);
void world_thing_set_stage(int uid, int stage);
double world_thing_grow(int uid, double minutes);
void world_thing_set_grow(int uid, double minutes);
int world_thing_liquid(int uid);
void world_thing_set_liquid(int uid, int liquid);
void world_thing_set_frame(int uid, int frame);
cJSON *world_thing_entry(int uid);
int world_thing_by_instance(int instanceId);
int world_things_of_type(int type, int *out, int max);
void world_actor_place_lift(int actor, int x, int y, int lift);
void world_player_units(int *ux, int *uy);
void world_player_pixel(double *px, double *py);
void world_thing_become(int uid, int objectId);
void world_ground(int cx, int cy, int *tile, int *walk, int *wet);
int world_units_x(void);
int world_units_y(void);
int world_thing_vehicle(int uid);
void world_thing_box(int uid, int *l, int *tp, int *w, int *d, int *z, int *h);
int world_thing_solid(int uid);
int world_thing_kind(int uid);
int world_things_in(int l, int tp, int w, int d, int *out, int max);
void world_actor_foot(int actor, int *l, int *tp, int *w, int *d, int *lift, int *h);
const char *world_blocked(int l, int tp, int w, int d, int zFrom, int zTo, const int *uids, int nu, const int *actors, int na);
void world_shift_thing(int uid, int dx, int dy, int dz);
void world_turn_thing(int uid, int quarters, double cx, double cy);
void world_shift_actor(int actor, double dx, double dy, int dz);
void world_actor_set_facing(int actor, int facing);
int world_actor_facing_of(int actor);
int world_line_clear(double fromX, double fromY, int fromLift, double toX, double toY);
void world_draw_sprite(uint32_t *pixels, const Sprite *sprite, double x, double y, int lift, uint32_t ms);
int world_put_body(int actor, int x, int y, int onFloor);
#include <stddef.h>
int world_tile_key(int x, int y, int onFloor, char *key, size_t size);
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
int world_go_to_chair_within(int actor, int tiles);   /* the closest free chair it can reach within that many tiles */
int world_arrive_furniture(int actor);      /* 1 seated, -1 the spot was taken meanwhile, 0 wasn't heading for one */
void world_stand_up(int actor);
int world_seated(int actor);
int world_seated_on(int actor);
int world_in_bed(int actor);
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
void world_draw_stats(int *thingsOnMap, int *drawnLast);
int world_hearing_at(double drawnX, double drawnY, double *volume, double *balance);   /* Exult's sound positions: 0 too far */
void world_sound_from_actor(int resourceId, int actor);   /* heard from where they are (the main character: as it is) */
void world_sound_at_thing(int resourceId, int uid);       /* heard from a thing on the map */
int world_actor_heard(int actor);
void world_sound_at_point(int resourceId, double px, double py, int lift);   /* heard from a point on the map */                         /* within hearing (24 tiles) */
void world_camera_origin(double *x, double *y);   /* the view's top-left in world pixels */
uint32_t world_shade_colour(uint32_t c);   /* a colour under the sky's light (for what is drawn under it: clouds) */   /* for the timing diary */
void world_keys_let_go(void);   /* the game paused or Cinema Mode began: no direction counts as held till pressed again */
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
