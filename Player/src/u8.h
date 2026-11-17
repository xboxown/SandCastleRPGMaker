/* The Ultima 8 engine's own rules (u8.c): its keys, its commands. The diamond view is world.c's. */
#ifndef SANDCASTLE_U8_H
#define SANDCASTLE_U8_H
#include <stddef.h>
#include "platform.h"
struct cJSON;

void u8_load(void);           /* after world_load: an Ultima 8 project's own rules on */
int u8_on(void);

/* The key pressed turned into the engine's own (KEY_UP, KEY_ENTER...) by the action it is now; KEY_OTHER: none of the engine's
   (u8_action_id says which of Ultima 8's own, "" none). onScreen: a box, a slider, a question is up; typing: letters write. */
void u8_translate(PlatformEvent *e, int onScreen, int typing);
const char *u8_action_id(void);
int u8_key_is(const PlatformEvent *e, const char *actionId);
int u8_held(const char *actionId);             /* Run, Whole Stack: its key is down now */
void u8_release_all(void);
void u8_key_words(const char *actionId, char *out, size_t size);  /* "Enter", "Ctrl+Q" */
void u8_display_key(const char *name, char *out, size_t size);
void u8_fill_key_words(const char *text, char *out, size_t size); /* a paperdoll's %key... */

/* Ask For Key: the event waits for the next key. */
int u8_asking_key(void);
int u8_key_ask_event(const PlatformEvent *e);  /* 1: the key went to it */
void u8_cancel_key_ask(void);

/* The Ultima 8 tab's commands: 1 done, -1 failed, 0 waiting for its token's answer, -2 not this file's. */
int u8_command(int type, struct cJSON *params, int whoCharacter, int activator, int token, double (*formula)(const char *key), const char *(*text)(const char *key), int thingUid);
void u8_update(double ms);                  /* each frame the game runs: commands that take their time, view events */
void u8_flip_frame(uint32_t *pixels);       /* Flip Screen: before the frame is shown */
void u8_draw_screen(uint32_t *pixels);      /* a reading screen, a keypad, a computer */
int u8_screen_open(void);
int u8_screen_click(int x, int y);
int u8_screen_key(const PlatformEvent *e);
void u8_update_view_events(double ms);
/* Ultima 8's rules (U8Reactions): stats grow by use, eating and healing. */
int u8_stats_grow_by_use(void);
int u8_eating_as_u8(void);
void u8_update_healing(double ms);
void u8_feed(int characterId, int food);
void u8_walk_input(int left, int right, int up, int down, int *dx, int *dy); /* the arrows held: the world way the Avatar goes, its speed */
int u8_mouse(const PlatformEvent *e);           /* the right button walks, both jump: 1 = taken */
int u8_combat_mouse(const PlatformEvent *e);
int u8_pointer_arrow(const char **set, int *direction); /* the arrow the pointer shows now (0: the resting pointer) */
int u8_world_key(const char *actionId);      /* a world key of Ultima 8's own (Menu...): 1 = handled */
/* The Avatar's Ultima 8 rules the movement (world.c) asks about - U8Reactions, U8Liquids, U8Party, U8Crusader. */
int u8_knocked_down_by_fall(int damage);
int u8_holding_ledge(void);
int u8_hanging(void);
void u8_climb_from_hang(void);
int u8_stunned(void);
void u8_tick_stun(double seconds);
int u8_balancing_at_edge(int dx, int dy);
void u8_build_up_from_climb(int rise);
int u8_liquid_step_ok(int l, int t, int w, int d);
void u8_note_leap(double fromX, double fromY, int fromLift, double toX, double toY, int toLift);
void u8_note_drop_landed(void);
int u8_kneeling(void);
void u8_kneel(int down);
/* Crusader's control (who the player is) */
int u8_main_character(void);
int u8_controlling_other(void);
void u8_control_next(void);
void u8_control_main(void);
void u8_control_back_on_death(int dyingCharacter);
void u8_control_back(void);
void u8_save_begin(void);
void u8_save_end(void);
int u8_saved_controlled(void);
int u8_saved_locked(void);
void u8_restore_control(int controlledCharacter, int locked);
void u8_reset_control(void);
void u8_save(struct cJSON *runtime);
void u8_restore(struct cJSON *runtime);
int u8_control_command(int type, struct cJSON *params, int whoCharacter);
int u8_party_full(int characterId);   /* Join Party: 1 when there's no room (its words said) */
int u8_leaping(int actor);     /* a party member climbing after the player: in the air on purpose (U8Party) */
int u8_direction_held(void);   /* a way held (the keys or the right button): hanging from a ledge */
void u8_drop_arrows(void);     /* knocked down: the walking keys held must be pressed again */
int u8_take_drop_arrows(void);
/* Combat (combat.c asks; U8Extras, U8Reactions, U8Crusader). */
int u8_player_dodging(void);
int u8_player_dodges_shot(void);
void u8_build_up_from_swing(int actor);
void u8_build_up_from_blow(int attacker, int defender, int damage, int kick);
void u8_stumble_from_hit(int actor);
void u8_maybe_surrender(int actor);
int u8_after_difficulty(int characterId, int damage);
int u8_after_energy_shield(int characterId, int damage);
int u8_crosshair_target(void);
int u8_crosshair_can_take(int actor);
const char *u8_player_back_to_pose(int inCombat);
#endif
