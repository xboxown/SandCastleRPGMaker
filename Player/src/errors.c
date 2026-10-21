/*
 * The game's errors (GameErrors, System Settings > Errors): every error has
 * a number and words - the maker's, else the engine's - with %errdetail
 * filled in. Shown in Game.log (the error paperdoll opens them on screen).
 */
#include "events.h"
#include "world.h"
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <string.h>
#include <ctype.h>

static const struct { int number; const char *words; } DEFAULTS[] = {
    { 101, "This game has no Save/Load screen." }, { 102, "You can't save now: %errdetail." }, { 103, "Click a slot to save into first." },
    { 104, "Pick a save first." }, { 105, "That slot is empty." }, { 106, "The game couldn't be saved: %errdetail" },
    { 107, "The saved game couldn't be loaded: %errdetail" }, { 108, "The saved game is damaged and can't be loaded." },
    { 109, "This save was made by a newer version of the game." }, { 110, "The save couldn't be deleted: %errdetail" },
    { 111, "Type a name for the save first." }, { 112, "Saving needs the project to be saved once first." },
    { 201, "There isn't enough room on the disk." }, { 202, "The disk can't be written to - it's read-only, or access is denied." },
    { 203, "A file is missing: %errdetail" }, { 301, "A game data file is missing: %errdetail" },
    { 302, "A game data file isn't this game's own: %errdetail" }, { 303, "The game's data is damaged: %errdetail" },
    { 304, "The game couldn't start: %errdetail" }, { 900, "Something went wrong: %errdetail" },
};

/* Describe: the maker's words, else the engine's, else (an unknown number) the detail itself. */
void error_describe(int number, const char *detail, char *out, size_t size)
{
    const char *words = NULL;
    cJSON *m, *settings = cJSON_GetObjectItem(cJSON_GetObjectItem((cJSON *)world_json(), "Project"), "Settings");
    cJSON_ArrayForEach(m, cJSON_GetObjectItem(cJSON_GetObjectItem(settings, "Errors"), "Messages"))
        if (cJSON_IsNumber(cJSON_GetObjectItem(m, "Number")) && cJSON_GetObjectItem(m, "Number")->valueint == number) words = cJSON_GetStringValue(cJSON_GetObjectItem(m, "Description"));
    for (size_t i = 0; !words && i < sizeof DEFAULTS / sizeof DEFAULTS[0]; i++) if (DEFAULTS[i].number == number) words = DEFAULTS[i].words;
    const char *p = words;
    while (p && isspace((unsigned char)*p)) p++;
    if (!p || !*p) { if (*detail) snprintf(out, size, "%s", detail); else snprintf(out, size, "Error %d.", number); return; }
    size_t n = 0;
    while (*p && n + 1 < size)
    {
        if (!strncmp(p, "%errdetail", 10) || !strncmp(p, "%ERRDETAIL", 10)) { for (const char *d = detail; *d && n + 1 < size; d++) out[n++] = *d; p += 10; }
        else out[n++] = *p++;
    }
    while (n && isspace((unsigned char)out[n - 1])) n--;
    out[n] = 0;
}

void game_error(int number, const char *detail)
{
    char words[400], line[460];
    error_describe(number, detail ? detail : "", words, sizeof words);
    snprintf(line, sizeof line, "Error %d: %s", number, words);
    game_log(line);
}
