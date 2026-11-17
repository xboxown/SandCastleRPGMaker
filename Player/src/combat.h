/* Combat, factions, theft, experience and combat music (GameTestWindow.Combat and friends). */
#ifndef SANDCASTLE_COMBAT_H
#define SANDCASTLE_COMBAT_H
#include <stdint.h>

void combat_load(void);
void combat_update(double ms);                 /* not while the game is paused */
void combat_draw(uint32_t *pixels, uint32_t ms); /* shots in the air */
int combat_busy(int actor);                    /* fighting, fleeing or hounding: its schedule waits */
int combat_mode(void);
int combat_hostile_nearby(void);               /* foes in a fight on the screen (Ultima 6: no resting) */
void combat_toggle_mode(void);                 /* C */
void combat_player_target(int actor);          /* double-click in combat mode */
int combat_attack(int attacker, int target);   /* the Attack command */
void combat_theft(int ownerCharacterId, int factionId);
int combat_relation(int viewer, int target);
void combat_set_relation(int viewer, int target, int value);
void combat_change_relation(int viewer, int target, const char *operation, int amount, int bothWays);
int combat_change_faction(const char *who, int factionId);
void combat_gain_experience(int actor, int amount);
int combat_silences_ambient(void);
void combat_death_music(void);
void combat_stop_cue(void);
void combat_forget_unseen(int actor);   /* those who no longer see it stop fighting it */
int combat_stat(int actor, int kind, int fallback);  /* a built-in stat: 1 Strength, 2 Dexterity... */
void combat_set_stat(int actor, int kind, int value);
void combat_pose_back(int actor, double ms);
void combat_u8_swing(int kick, int dx, int dy);  /* Ultima 8: a swing or a kick the way given (0, 0: the way it faces) */
void combat_u8_block(int on);
void combat_u8_next_target(void);              /* Crusader's crosshair: the Next Target key */
void combat_u8_control_changed(int otherActor);
int combat_u8_blocking(void);
int combat_would_attack(int viewer, int other); /* viewer's Attitude toward other is Attack */
int combat_enemies(int a, int b);       /* either would attack the other (Attitude: Attack) */
#include "pictures.h"
int combat_launch(const Sprite *sprite, int from, int target, double tx, double ty, void (*arrive)(void *, int, double, double), void *context);
void combat_fight_back(int defender, int attacker);
void combat_start(int actor, int target);
int combat_target_of(int actor);
void combat_flee(int actor, int from);
void combat_calm(int actor);
void combat_stop_fighting(int actor);
int combat_line_clear(int a, int b);
double combat_tiles_between(int a, int b);
int combat_friends(int a, int b);
void combat_owned_theft(int uid);
void combat_strike_object(int uid, int damage);
void combat_force_fuse(int uid);
void combat_doors_init(void);
int combat_foes_in_sight(void);
int combat_douse_fuse(int uid);
#include "../third_party/cJSON.h"
void combat_save(cJSON *runtime);
void combat_restore(cJSON *runtime);
int combat_can_break(int uid);
void combat_strike_thing(int uid, int striker);  /* striker: an actor (0 = the player: a crime if it's owned) */
int combat_player_thing(int uid);                 /* double-click in combat mode: 1 = attacked */
int combat_light_fuse(int uid);                   /* an explosive double-clicked: 1 = handled */
void combat_attack_thing_begin(int actor, int uid);
int combat_attack_thing_tick(int actor, double ms);

#endif
