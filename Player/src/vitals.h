/* Health, food, ailments, knock-outs and death (GameTestWindow.Vitals and friends). */
#ifndef SANDCASTLE_VITALS_H
#define SANDCASTLE_VITALS_H

void vitals_load(void);
void vitals_update(double ms);                     /* not while the game is paused */
int vitals_hp(int characterId, int *hp);           /* 0 if it has no Hits stat */
int vitals_max_hp(int characterId);
void vitals_set_hp(int characterId, int hp);
void vitals_damage(int characterId, int amount, const char *cause);
void vitals_kill(int characterId, const char *cause);
int vitals_dead(int characterId);
int vitals_unconscious(int characterId);
int vitals_incapacitated(int characterId);
int vitals_food(int characterId);
int vitals_can_get_hungry(int characterId);
int vitals_game_over(void);
void vitals_set_game_over(void);
int vitals_has(int characterId, int ailment);
int vitals_inflict(int characterId, int ailment);
void vitals_cure(int characterId, int ailment);
int vitals_ailment_of(const char *name);           /* "Poisoned" -> its flag */
void vitals_change_food(int characterId, const char *operation, int amount);
void vitals_heal_fully(int characterId);
int vitals_bark(int characterId, int kind);
#include "../third_party/cJSON.h"
void vitals_save(cJSON *runtime);
void vitals_add(int characterId);
int vitals_resurrect(int characterId);
void vitals_restore(cJSON *runtime);        /* VerboseKind: Hungry 0 .. Uneasy 10 */

#endif
