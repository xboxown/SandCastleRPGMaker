/* Saved games: written and listed beside the game, encrypted with the game's key (GameSaveStore). */
#ifndef SANDCASTLE_SAVE_H
#define SANDCASTLE_SAVE_H
#include "../third_party/cJSON.h"
#include <stddef.h>
#include <stdint.h>

#define SAVE_FORMAT 1

typedef struct SaveInfo { char folder[1200], name[64], gameTime[48], savedAt[32], playerName[64]; int slot; } SaveInfo;

void save_init(const char *gameFolder);
void save_take_screenshot(const uint32_t *pixels);   /* as the Save/Load screen opens, before it covers the view */
int save_list(SaveInfo *out, int max);               /* newest first */
int save_write(const char *name, int slot, const char *overwrite, char *error, size_t errorSize);
int save_delete(const char *folder);
char *save_read(const char *folder, char *error, size_t errorSize);
const char *save_screenshot_path(const char *folder, char *out, size_t size);

cJSON *save_capture(void);
void save_set_world_states(cJSON *states);
int save_has_world(int id);   /* a world visited and left */

#endif
