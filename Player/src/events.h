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
void events_fire_item(int category, int itemId, int trigger);

/* Schedules: whether an event is using a character now (busy: carrying out a command; held: its event isn't over). */
int events_actor_busy(int actor);
int events_actor_held(int actor);
void events_log_once(int key, const char *text);
#include "../third_party/cJSON.h"
void events_run_commands(cJSON *commands, int actor);
int events_object_event(int uid, int trigger, int activator);

/* The game clock (Exult's: from System Settings > Clock). */
int events_hour(void);
int events_minute(void);

long events_total_minutes(void);
int events_day(void);
double events_variable(const char *name);
int events_last_result(void);

void events_knocked_out(int characterId);
int events_saving_forbidden(void);
int events_combat_forbidden(void);
int events_keys_forbidden(void);   /* Allow Controls > Keys: only walking */
int events_quit_requested(void);
/* An error by number (System Settings > Errors): its words, the maker's else the engine's. */
void game_error(int number, const char *detail);

/* Game.log (main.c). */
void game_log(const char *line);

#endif
