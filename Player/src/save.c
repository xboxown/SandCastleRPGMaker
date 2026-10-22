/*
 * Saved games (GameSaveStore, GameTestWindow.SaveLoad): a save holds
 * everything the game has changed - the map as it is now, every character
 * (belongings, stats, party), the factions, the clock, variables and the
 * rest of the script data, which event pages have fired, hunger and
 * ailments, the weather. It's the game's own data with all that written
 * back into it, plus what's only in play (Runtime): loading builds the game
 * from it as a new game is built from the project.
 *
 * On disk, beside the game: Saves/<save folder>/info.json (its listing),
 * game.sav (encrypted with the game's own key, as Game_Data is) and
 * shot.png (the view when the Save/Load screen opened). Saves/saves.json
 * lists the folders, newest first.
 */
#include "save.h"
#include "books.h"
#include "world.h"
#include "items.h"
#include "events.h"
#include "script.h"
#include "vitals.h"
#include "combat.h"
#include "weather.h"
#include "conditions.h"
#include "pack.h"
#include "platform.h"
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

char *read_whole_file(const char *path, long *length);

static char savesFolder[1100];
static uint32_t *shot; /* the view as the Save/Load screen opened */

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }

/* Beside the game: <folder holding Game_Data>/Saves (Amiga paths too: "PROGDIR:Game_Data" -> "PROGDIR:Saves"). */
void save_init(const char *gameFolder)
{
    char parent[1100];
    snprintf(parent, sizeof parent, "%s", gameFolder);
    size_t n = strlen(parent);
    while (n && (parent[n - 1] == '/' || parent[n - 1] == '\\')) parent[--n] = 0;
    char *cut = NULL;
    for (char *p = parent; *p; p++) if (*p == '/' || *p == '\\' || *p == ':') cut = p;
    if (cut && *cut == ':') cut[1] = 0;              /* "PROGDIR:Game_Data" -> "PROGDIR:" */
    else if (cut) cut[1] = 0;                        /* ".../Game_Data" -> ".../" */
    else parent[0] = 0;                               /* "Game_Data" -> "" (here) */
    snprintf(savesFolder, sizeof savesFolder, "%sSaves", parent);
}

static void path_in(char *out, size_t size, const char *folder, const char *file) { snprintf(out, size, "%s/%s", folder, file); }

/* ---------------- The screenshot ---------------- */

void save_take_screenshot(const uint32_t *pixels)
{
    if (!shot) shot = (uint32_t *)malloc(sizeof(uint32_t) * SCREEN_W * SCREEN_H);
    if (shot) memcpy(shot, pixels, sizeof(uint32_t) * SCREEN_W * SCREEN_H);
}

static uint32_t crc_table[256];
static uint32_t crc32_of(const unsigned char *d, size_t n, uint32_t c)
{
    if (!crc_table[1]) for (uint32_t i = 0; i < 256; i++) { uint32_t v = i; for (int k = 0; k < 8; k++) v = v & 1 ? 0xEDB88320u ^ (v >> 1) : v >> 1; crc_table[i] = v; }
    c = ~c;
    for (size_t i = 0; i < n; i++) c = crc_table[(c ^ d[i]) & 255] ^ (c >> 8);
    return ~c;
}

static void put32(unsigned char *p, uint32_t v) { p[0] = (unsigned char)(v >> 24); p[1] = (unsigned char)(v >> 16); p[2] = (unsigned char)(v >> 8); p[3] = (unsigned char)v; }

static void chunk(FILE *f, const char *type, const unsigned char *data, uint32_t n)
{
    unsigned char head[8];
    put32(head, n); memcpy(head + 4, type, 4);
    fwrite(head, 1, 8, f);
    if (n) fwrite(data, 1, n, f);
    uint32_t c = crc32_of((const unsigned char *)type, 4, 0);
    c = crc32_of(data, n, c);
    unsigned char tail[4];
    put32(tail, c);
    fwrite(tail, 1, 4, f);
}

/* A PNG of the view (RGB, its rows deflated as stored blocks - no compression needed for a small picture). */
static int write_png(const char *path, const uint32_t *pixels, int w, int h)
{
    FILE *f = fopen(path, "wb");
    if (!f) return 0;
    static const unsigned char sig[8] = { 137, 'P', 'N', 'G', 13, 10, 26, 10 };
    fwrite(sig, 1, 8, f);
    unsigned char ihdr[13];
    put32(ihdr, (uint32_t)w); put32(ihdr + 4, (uint32_t)h);
    ihdr[8] = 8; ihdr[9] = 2; ihdr[10] = ihdr[11] = ihdr[12] = 0;
    chunk(f, "IHDR", ihdr, 13);
    size_t rawSize = (size_t)h * (size_t)(1 + w * 3);
    unsigned char *raw = (unsigned char *)malloc(rawSize);
    size_t blocks = (rawSize + 65534) / 65535;
    unsigned char *z = (unsigned char *)malloc(2 + rawSize + blocks * 5 + 4);
    if (!raw || !z) { free(raw); free(z); fclose(f); return 0; }
    size_t at = 0;
    for (int y = 0; y < h; y++)
    {
        raw[at++] = 0;
        for (int x = 0; x < w; x++) { uint32_t c = pixels[y * w + x]; raw[at++] = (unsigned char)(c >> 16); raw[at++] = (unsigned char)(c >> 8); raw[at++] = (unsigned char)c; }
    }
    size_t zn = 0;
    z[zn++] = 0x78; z[zn++] = 0x01;
    uint32_t a = 1, b = 0;
    for (size_t i = 0; i < rawSize; i++) { a = (a + raw[i]) % 65521; b = (b + a) % 65521; }
    for (size_t off = 0; off < rawSize; off += 65535)
    {
        size_t len = rawSize - off < 65535 ? rawSize - off : 65535;
        z[zn++] = off + len >= rawSize ? 1 : 0;
        z[zn++] = (unsigned char)len; z[zn++] = (unsigned char)(len >> 8);
        z[zn++] = (unsigned char)~len; z[zn++] = (unsigned char)(~len >> 8);
        memcpy(z + zn, raw + off, len); zn += len;
    }
    put32(z + zn, (b << 16) | a); zn += 4;
    chunk(f, "IDAT", z, (uint32_t)zn);
    chunk(f, "IEND", NULL, 0);
    free(raw); free(z);
    int ok = !ferror(f);
    fclose(f);
    return ok;
}

/* ---------------- The list of saves ---------------- */

static cJSON *read_index(void)
{
    char path[1200];
    path_in(path, sizeof path, savesFolder, "saves.json");
    long n;
    char *text = read_whole_file(path, &n);
    cJSON *index = text ? cJSON_Parse(text) : NULL;
    free(text);
    if (!cJSON_IsArray(index)) { cJSON_Delete(index); index = cJSON_CreateArray(); }
    return index;
}

static int write_text(const char *path, const char *text)
{
    FILE *f = fopen(path, "wb");
    if (!f) return 0;
    size_t n = strlen(text);
    int ok = fwrite(text, 1, n, f) == n;
    return fclose(f) == 0 && ok;
}

static void write_index(cJSON *index)
{
    char path[1200], *text = cJSON_PrintUnformatted(index);
    path_in(path, sizeof path, savesFolder, "saves.json");
    if (text) write_text(path, text);
    free(text);
}

/* Every save, newest first: its listing (info.json). */
int save_list(SaveInfo *out, int max)
{
    cJSON *index = read_index(), *e;
    int n = 0;
    cJSON_ArrayForEach(e, index)
    {
        if (n >= max) break;
        char folder[1200], path[1300];
        snprintf(folder, sizeof folder, "%s/%s", savesFolder, cJSON_GetStringValue(e) ? cJSON_GetStringValue(e) : "");
        path_in(path, sizeof path, folder, "info.json");
        long len;
        char *text = read_whole_file(path, &len);
        cJSON *info = text ? cJSON_Parse(text) : NULL;
        free(text);
        path_in(path, sizeof path, folder, "game.sav");
        FILE *probe = fopen(path, "rb");
        if (!info || !probe) { if (probe) fclose(probe); cJSON_Delete(info); continue; } /* a broken save is left out */
        fclose(probe);
        SaveInfo *s = &out[n++];
        memset(s, 0, sizeof *s);
        snprintf(s->folder, sizeof s->folder, "%s", folder);
        snprintf(s->name, sizeof s->name, "%s", str(info, "Name"));
        snprintf(s->gameTime, sizeof s->gameTime, "%s", str(info, "GameTime"));
        snprintf(s->savedAt, sizeof s->savedAt, "%s", str(info, "SavedAtLocal"));
        snprintf(s->playerName, sizeof s->playerName, "%s", str(info, "PlayerName"));
        s->slot = num(info, "Slot", 0);
        cJSON_Delete(info);
    }
    cJSON_Delete(index);
    return n;
}

/* ---------------- Capturing and writing ---------------- */

/* The game as it is now: its own data, everything changed written back into it, and what's only in play. */
static cJSON *worldStates;

static cJSON *capture(void)
{
    cJSON *root = cJSON_Duplicate((cJSON *)world_json(), 1), *project = get(root, "Project");
    cJSON *runtime = cJSON_CreateObject();
    cJSON_AddNumberToObject(runtime, "Format", SAVE_FORMAT);
    world_save(get(project, "Map"), runtime, items_save_thing);
    items_save(get(get(project, "Characters"), "Characters"));
    events_save(runtime);
    script_save(runtime);
    vitals_save(runtime);
    combat_save(runtime);
    weather_save(runtime);
    conditions_save(runtime);
    books_save(runtime);
    if (worldStates) cJSON_AddItemToObject(runtime, "WorldStates", cJSON_Duplicate(worldStates, 1)); /* the other worlds as they were left */
    cJSON_AddItemToObject(root, "Runtime", runtime);
    return root;
}

cJSON *save_capture(void) { return capture(); }

/* The worlds visited and left (by world id): kept from a save, or from Change Map. Takes it. */
void save_set_world_states(cJSON *states) { cJSON_Delete(worldStates); worldStates = states; }
int save_has_world(int id) { char key[16]; snprintf(key, sizeof key, "%d", id); return worldStates && cJSON_GetObjectItem(worldStates, key) != NULL; }

static void game_time_text(char *out, size_t size)
{
    int hour = events_hour(), h12 = hour % 12 == 0 ? 12 : hour % 12;
    snprintf(out, size, "Day %d, %d:%02d %s", events_day() + 1, h12, events_minute(), hour < 12 ? "AM" : "PM");
}

/* Writes a save - into overwrite's folder, or a new one. 0 on failure (why in error). */
int save_write(const char *name, int slot, const char *overwrite, char *error, size_t errorSize)
{
    error[0] = 0;
    if (!platform_make_dir(savesFolder)) { snprintf(error, errorSize, "%s couldn't be made", savesFolder); return 0; }
    char folder[1200], leaf[64];
    cJSON *index = read_index();
    if (overwrite && overwrite[0]) snprintf(folder, sizeof folder, "%s", overwrite);
    else
    {
        /* a fresh folder: save_<n>, one more than any there */
        int top = 0;
        cJSON *e;
        cJSON_ArrayForEach(e, index) { const char *f = cJSON_GetStringValue(e); if (f && !strncmp(f, "save_", 5) && atoi(f + 5) > top) top = atoi(f + 5); }
        snprintf(leaf, sizeof leaf, "save_%d", top + 1);
        snprintf(folder, sizeof folder, "%s/%s", savesFolder, leaf);
    }
    if (!platform_make_dir(folder)) { snprintf(error, errorSize, "%s couldn't be made", folder); cJSON_Delete(index); return 0; }
    cJSON *root = capture();
    char *json = cJSON_PrintUnformatted(root);
    cJSON_Delete(root);
    if (!json) { snprintf(error, errorSize, "not enough memory"); cJSON_Delete(index); return 0; }
    long size;
    unsigned char *sealed = pack_encrypt((const unsigned char *)json, (long)strlen(json), &size);
    free(json);
    if (!sealed) { snprintf(error, errorSize, "the game's key couldn't be read"); cJSON_Delete(index); return 0; }
    /* written beside, then put in place: a failed write never spoils the save that was there */
    char path[1300], temp[1300];
    path_in(path, sizeof path, folder, "game.sav");
    path_in(temp, sizeof temp, folder, "game.sav.saving");
    FILE *f = fopen(temp, "wb");
    int ok = f && fwrite(sealed, 1, (size_t)size, f) == (size_t)size;
    if (f && fclose(f) != 0) ok = 0;
    free(sealed);
    if (!ok) { remove(temp); snprintf(error, errorSize, "the disk is full or can't be written to"); cJSON_Delete(index); return 0; }
    remove(path);
    if (rename(temp, path) != 0) { snprintf(error, errorSize, "%s couldn't be written", path); cJSON_Delete(index); return 0; }
    if (shot) { path_in(path, sizeof path, folder, "shot.png"); write_png(path, shot, SCREEN_W, SCREEN_H); }
    /* its listing */
    cJSON *info = cJSON_CreateObject();
    char text[64];
    time_t now = time(NULL);
    cJSON_AddStringToObject(info, "Name", name);
    strftime(text, sizeof text, "%Y-%m-%dT%H:%M:%SZ", gmtime(&now));
    cJSON_AddStringToObject(info, "SavedAtUtc", text);
    strftime(text, sizeof text, "%Y-%m-%d %H:%M", localtime(&now));
    cJSON_AddStringToObject(info, "SavedAtLocal", text);
    game_time_text(text, sizeof text);
    cJSON_AddStringToObject(info, "GameTime", text);
    cJSON *player = NULL, *c;
    cJSON_ArrayForEach(c, get(get(get((cJSON *)world_json(), "Project"), "Characters"), "Characters")) if (num(c, "Id", -2) == world_actor_character(0)) player = c;
    cJSON_AddStringToObject(info, "PlayerName", str(player, "Name"));
    cJSON_AddNumberToObject(info, "Slot", slot);
    char *infoText = cJSON_PrintUnformatted(info);
    cJSON_Delete(info);
    path_in(path, sizeof path, folder, "info.json");
    if (!infoText || !write_text(path, infoText)) { free(infoText); snprintf(error, errorSize, "%s couldn't be written", path); cJSON_Delete(index); return 0; }
    free(infoText);
    /* newest first in the list */
    const char *leafName = strrchr(folder, '/') ? strrchr(folder, '/') + 1 : folder;
    for (int i = cJSON_GetArraySize(index) - 1; i >= 0; i--)
        if (!strcmp(cJSON_GetStringValue(cJSON_GetArrayItem(index, i)) ? cJSON_GetStringValue(cJSON_GetArrayItem(index, i)) : "", leafName)) cJSON_DeleteItemFromArray(index, i);
    cJSON_InsertItemInArray(index, 0, cJSON_CreateString(leafName));
    write_index(index);
    cJSON_Delete(index);
    return 1;
}

int save_delete(const char *folder)
{
    const char *files[] = { "game.sav", "info.json", "shot.png", "game.sav.saving" };
    char path[1300];
    for (int i = 0; i < 4; i++) { path_in(path, sizeof path, folder, files[i]); remove(path); }
    platform_remove_dir(folder);
    cJSON *index = read_index();
    const char *leaf = strrchr(folder, '/') ? strrchr(folder, '/') + 1 : folder;
    for (int i = cJSON_GetArraySize(index) - 1; i >= 0; i--)
        if (!strcmp(cJSON_GetStringValue(cJSON_GetArrayItem(index, i)) ? cJSON_GetStringValue(cJSON_GetArrayItem(index, i)) : "", leaf)) cJSON_DeleteItemFromArray(index, i);
    write_index(index);
    cJSON_Delete(index);
    return 1;
}

/* A save's game data, decrypted (malloc'd text), or NULL - why in error. */
char *save_read(const char *folder, char *error, size_t errorSize)
{
    char path[1300];
    path_in(path, sizeof path, folder, "game.sav");
    long n;
    char *text = (char *)pack_read(path, &n);
    if (!text) snprintf(error, errorSize, "the saved game is damaged and can't be loaded");
    return text;
}

const char *save_screenshot_path(const char *folder, char *out, size_t size) { path_in(out, size, folder, "shot.png"); return out; }

const char *save_folder(void) { return savesFolder; }
