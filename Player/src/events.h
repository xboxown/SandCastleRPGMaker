/*
 * Events in the player, as the Windows engine runs them
 * (EventCommandInterpreter, GameTestWindow): map events set off by stepping
 * on, staying on and leaving their cell; talking to a character runs its
 * Talk commands; a character's Always pages run when their conditions come
 * true. Each page's conditions and cycle (Once, so many times, Always) are
 * checked as BookEventConditionEvaluator does, and its commands run - Show
 * Text and its replies, switches and variables, branches, labels, waits,
 * Cinema Mode, Teleport, Change Pose. Commands the player can't do yet are
 * skipped and written in Game.log.
 */
#ifndef SANDCASTLE_EVENTS_H
#define SANDCASTLE_EVENTS_H

/* Reads the events from the game (world_json); call after world_load. */
void events_load(void);

/* Each frame: ms passed; paused while a dialog box is up. */
void events_update(double ms, int paused);

/* After the player moved: leaving and entering map events. */
void events_player_moved(void);

/* Talking to a character (double-click): its Talk commands. */
void events_talk(int actor);

/* An event has taken the controls away (Cinema Mode). */
int events_cinema(void);

void events_set_variable(const char *name, double value);

/* Things that happened: to a character (By Equip...), to everyone's belongings (By Having / Not Having An Item: every map event), to an item (By Pickup / Drop Item: its own pages). */
void events_fire_character(int characterId, int trigger);
void events_fire_map(int trigger);
int events_fire_item(int category, int itemId, int trigger); /* 1 when a page ran */

/* Schedules: whether an event is using a character now (busy: carrying out a command; held: its event isn't over). */
int events_actor_busy(int actor);
int events_actor_held(int actor);
void events_log_once(int key, const char *text);
#include "../third_party/cJSON.h"
void events_run_commands(cJSON *commands, int actor);
int events_object_event(int uid, int trigger, int activator);

/* The game clock (Exult's: from System Settings > Clock). */
int events_hour(void);
void events_advance_minutes(int minutes);
int events_minute(void);

long events_total_minutes(void);
int events_day(void);
double events_variable(const char *name);
int events_last_result(void);

void events_knocked_out(int characterId);
/* Paperdolls: a part's own pages; the paperdoll an object's events act on; an answer heard. */
int events_fire_doll(cJSON *pages, int trigger, void *state, int part, int itemCategory, int itemId, int combinedPartId);
void events_set_doll_context(void *doll);
void events_doll_answered(int token, int yes);
void events_save(cJSON *runtime);
const char *events_save_blocker(void);
void events_restore(cJSON *runtime);
void events_reset(void);
/* The error box shown (System Settings > Errors has one): token hears when it closes. 0: no box (the words went to Game.log). */
int game_error_shown(int number, const char *detail, int token);
int events_saving_forbidden(void);
int events_combat_forbidden(void);
int events_keys_forbidden(void);   /* Allow Controls > Keys: only walking */
int events_quit_requested(void);
void events_request_quit(void);
void main_take_screenshot(void);   /* main.c: the view as it is now, the next save's screenshot */
void events_request_restart(int toTitle);   /* New Game during play (0), Return To Title (1) */
int events_restart_requested(void);
int events_has_combine(int category, int itemId);
int events_has_trigger(int category, int itemId, int trigger);
int events_dropped_on(int uid, int category, int itemId);
int events_dropped_at_cell(int x, int y, int lift, int category, int itemId);
int events_combine(int uid, int ontoCategory, int ontoId, int category, int itemId);
int events_world_trip(int *world, int *hasSpot, int *x, int *y, int *floorOf); /* a Change Map to another world, due now */        /* 1 new game, 2 to the title */
void events_clear_restart(void);
/* An error by number (System Settings > Errors): its words, the maker's else the engine's. */
void game_error(int number, const char *detail);

/* Game.log (main.c). */
void game_log(const char *line);

#endif
