/* Combat, factions, theft, experience and combat music (GameTestWindow.Combat and friends). */
#ifndef SANDCASTLE_COMBAT_H
#define SANDCASTLE_COMBAT_H
#include <stdint.h>

void combat_load(void);
void combat_update(double ms);                 /* not while the game is paused */
void combat_draw(uint32_t *pixels, uint32_t ms); /* shots in the air */
int combat_busy(int actor);                    /* fighting, fleeing or hounding: its schedule waits */
int combat_mode(void);
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
#include "pictures.h"
int combat_launch(const Sprite *sprite, int from, int target, double tx, double ty, void (*arrive)(void *, int, double, double), void *context);
void combat_fight_back(int defender, int attacker);
void combat_start(int actor, int target);
int combat_target_of(int actor);
void combat_flee(int actor, int from);
void combat_calm(int actor);
int combat_line_clear(int a, int b);
double combat_tiles_between(int a, int b);
int combat_friends(int a, int b);
void combat_owned_theft(int uid);
void combat_strike_object(int uid, int damage);
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
