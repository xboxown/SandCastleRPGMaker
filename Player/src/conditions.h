/* Game conditions and the game's history (GameTestWindow.Conditions). */
#ifndef SANDCASTLE_CONDITIONS_H
#define SANDCASTLE_CONDITIONS_H

#include "../third_party/cJSON.h"

void conditions_record(const char *key);
void conditions_record_item(const char *what, int category, int itemId);
void conditions_record_person_item(const char *what, int characterId, int category, int itemId);
int conditions_test(cJSON *condition, const char *evaluatingEvent);
int conditions_all(cJSON *list, const char *evaluatingEvent);

#endif
