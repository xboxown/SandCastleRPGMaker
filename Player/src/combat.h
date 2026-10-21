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

#endif
