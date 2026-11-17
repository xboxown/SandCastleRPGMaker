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
#include "u6.h"
#include "u8.h"
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
#include <math.h>
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
#ifdef PLATFORM_PS2
    (void)gameFolder;
    snprintf(savesFolder, sizeof savesFolder, "%s", ps2_saves_folder()); /* the memory card in slot 1 (platform_ps2.c) */
    return;
#endif
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

/* An installed game (Ubuntu's .deb, the Mac's .app): its folder can't be written - the saves go in the player's own folder
   (platform_installed_paths). */
void save_use_folder(const char *folder) { snprintf(savesFolder, sizeof savesFolder, "%s", folder); }

static void path_in(char *out, size_t size, const char *folder, const char *file) { snprintf(out, size, "%s/%s", folder, file); }

/* ---------------- The screenshot ---------------- */

void save_take_screenshot(const uint32_t *pixels)
{
    if (!shot) shot = (uint32_t *)malloc(sizeof(uint32_t) * BASE_W * BASE_H);
    if (!shot) return;
    for (int y = 0; y < BASE_H; y++)  /* a save's picture stays 320 x 200, whatever the screen: its middle (Fill the screen) */
        for (int x = 0; x < BASE_W; x++) shot[y * BASE_W + x] = pixels[(BASE_Y + y) * frame_scale * FRAME_W + (BASE_X + x) * frame_scale];
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

static cJSON *capture_inner(void);
static cJSON *capture_light_inner(void);
/* Ultima 8, Crusader: written as if the main character were controlled, and saying who was (u8.c). */
static cJSON *capture(void) { u8_save_begin(); cJSON *r = capture_inner(); u8_save(get(r, "Runtime")); u8_save_end(); return r; }
static cJSON *capture_light(void) { u8_save_begin(); cJSON *r = capture_light_inner(); u8_save(get(r, "Runtime")); u8_save_end(); return r; }
static cJSON *capture_inner(void)
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
    u6_save(runtime); /* Ultima 6: the HUD frame in play (Change HUD Frame) */
    if (worldStates) cJSON_AddItemToObject(runtime, "WorldStates", cJSON_Duplicate(worldStates, 1)); /* the other worlds as they were left */
    cJSON_AddItemToObject(root, "Runtime", runtime);
    return root;
}

cJSON *save_capture(void) { return capture(); }

/* ---------------- Writing a save without copying the game (b576) ----------------
   The save is the game's whole data with what changed in play. capture() copies all of it, and printing it made it once more
   as one long text: on the PS2 (32 MB) that didn't fit - "not enough memory". Here only what the save changes is new (the
   map's own entries, the characters); everything else is the game's own data, pointed at (references: never freed with the
   save), and the text goes to the file a piece at a time. The file is the same as before, byte for byte. */
static cJSON *shallow(cJSON *o)
{
    if (!o) return NULL;
    cJSON *copy = cJSON_IsArray(o) ? cJSON_CreateArray() : cJSON_CreateObject(), *c;
    cJSON_ArrayForEach(c, o)
        if (cJSON_IsArray(copy)) cJSON_AddItemReferenceToArray(copy, c); else cJSON_AddItemReferenceToObject(copy, c->string, c);
    return copy;
}
/* In a shallow copy: the entry k made its own (shallow) copy too, so changing what's in it never changes the game's. */
static cJSON *own(cJSON *o, const char *k)
{
    cJSON *was = get(o, k);
    if (!was || !(cJSON_IsObject(was) || cJSON_IsArray(was))) return was;
    cJSON *copy = shallow(was);
    cJSON_ReplaceItemInObject(o, k, copy);
    return copy;
}

static cJSON *capture_light_inner(void)
{
    cJSON *root = shallow((cJSON *)world_json()), *project = own(root, "Project");
    cJSON *map = own(project, "Map");
    cJSON *layers = own(map, "Layers"), *l;
    /* each layer its own (world_save puts its tiles in), and each character (items_save puts its belongings in) */
    for (l = layers ? layers->child : NULL; l; l = l->next)
    {
        cJSON *copy = shallow(l);
        cJSON_ReplaceItemViaPointer(layers, l, copy);
        l = copy;
    }
    cJSON *characters = own(project, "Characters"), *list = own(characters, "Characters"), *c;
    for (c = list ? list->child : NULL; c; c = c->next)
    {
        cJSON *copy = shallow(c);
        cJSON_ReplaceItemViaPointer(list, c, copy);
        c = copy;
    }
    cJSON *runtime = cJSON_CreateObject();
    cJSON_AddNumberToObject(runtime, "Format", SAVE_FORMAT);
    world_save(map, runtime, items_save_thing);
    items_save(list);
    events_save(runtime);
    script_save(runtime);
    vitals_save(runtime);
    combat_save(runtime);
    weather_save(runtime);
    conditions_save(runtime);
    books_save(runtime);
    u6_save(runtime); /* Ultima 6: the HUD frame in play (Change HUD Frame) */
    if (worldStates) cJSON_AddItemReferenceToObject(runtime, "WorldStates", worldStates);
    if (get(root, "Runtime")) cJSON_DeleteItemFromObject(root, "Runtime");
    cJSON_AddItemToObject(root, "Runtime", runtime);
    return root;
}

/* The text as cJSON_PrintUnformatted makes it, a piece at a time: counted (w NULL), or encrypted into the file. */
typedef struct Sink { PackWriter *w; FILE *plain; long count; char buf[4096]; int n; } Sink;
static void sink_flush(Sink *s) { if (s->w && s->n) pack_writer_put(s->w, s->buf, s->n); if (s->plain && s->n) fwrite(s->buf, 1, (size_t)s->n, s->plain); s->count += s->n; s->n = 0; }
static void sink_put(Sink *s, const char *p, size_t n)
{
    while (n)
    {
        size_t room = sizeof s->buf - (size_t)s->n, take = n < room ? n : room;
        memcpy(s->buf + s->n, p, take); s->n += (int)take; p += take; n -= take;
        if (s->n == (int)sizeof s->buf) sink_flush(s);
    }
}
static void sink_str(Sink *s, const char *t) { sink_put(s, t, strlen(t)); }
static void sink_quoted(Sink *s, const char *t)
{
    sink_put(s, "\"", 1);
    if (t)
        for (const unsigned char *p = (const unsigned char *)t; *p; p++)
        {
            const unsigned char *run = p;
            while (*p > 31 && *p != '"' && *p != '\\') p++;
            if (p > run) sink_put(s, (const char *)run, (size_t)(p - run));
            if (!*p) break;
            char e[8];
            switch (*p)
            {
                case '\\': sink_str(s, "\\\\"); break;
                case '"': sink_str(s, "\\\""); break;
                case '\b': sink_str(s, "\\b"); break;
                case '\f': sink_str(s, "\\f"); break;
                case '\n': sink_str(s, "\\n"); break;
                case '\r': sink_str(s, "\\r"); break;
                case '\t': sink_str(s, "\\t"); break;
                default: snprintf(e, sizeof e, "\\u%04x", *p); sink_str(s, e); break;
            }
        }
    sink_put(s, "\"", 1);
}
static void sink_int(Sink *s, int v) { char t[16]; int n = snprintf(t, sizeof t, "%d", v); sink_put(s, t, (size_t)n); }
static void emit(Sink *s, const cJSON *item)
{
    int type = item->type & 0xFF;
    if (type == cJSON_Object || type == cJSON_Array)
    {
        int object = type == cJSON_Object;
        sink_put(s, object ? "{" : "[", 1);
        const int *packed; int count;
        if (!object && (packed = cJSON_GetPackedInts(item, &count)) != NULL)
            for (int i = 0; i < count; i++) { if (i) sink_put(s, ",", 1); sink_int(s, packed[i]); }
        for (const cJSON *c = item->child; c; c = c->next)
        {
            if (object) { sink_quoted(s, c->string); sink_put(s, ":", 1); }
            emit(s, c);
            if (c->next) sink_put(s, ",", 1);
        }
        sink_put(s, object ? "}" : "]", 1);
        return;
    }
    if (type == cJSON_Number && !isnan(item->valuedouble) && !isinf(item->valuedouble) && item->valuedouble == (double)item->valueint) { sink_int(s, item->valueint); return; }
    if (type == cJSON_String) { sink_quoted(s, item->valuestring); return; }
    /* anything else (numbers with fractions, true, false, null, raw): cJSON's own printing of it */
    char *t = cJSON_PrintUnformatted(item);
    if (t) { sink_str(s, t); free(t); }
}

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
    cJSON *root = capture_light();
    /* the text counted first (the file's header says how long it is), then written - encrypted as it goes */
    Sink *sink = (Sink *)calloc(1, sizeof(Sink));
    if (!sink) { cJSON_Delete(root); snprintf(error, errorSize, "not enough memory"); cJSON_Delete(index); return 0; }
    emit(sink, root); sink_flush(sink);
    long plainLength = sink->count;
    /* written beside, then put in place: a failed write never spoils the save that was there */
    char path[1300], temp[1300];
    path_in(path, sizeof path, folder, "game.sav");
    path_in(temp, sizeof temp, folder, "game.sav.saving");
    FILE *f = fopen(temp, "wb");
    PackWriter *w = f ? pack_writer_open(f, plainLength) : NULL;
    if (f && !w)
    {
        fclose(f); remove(temp); free(sink); cJSON_Delete(root);
        snprintf(error, errorSize, "the game's key couldn't be read"); cJSON_Delete(index); return 0;
    }
    int ok = 0;
    if (w)
    {
        memset(sink, 0, sizeof *sink); sink->w = w;
        emit(sink, root); sink_flush(sink);
        ok = pack_writer_close(w) && sink->count == plainLength;
    }
    free(sink);
    cJSON_Delete(root);
    if (f && fclose(f) != 0) ok = 0;
    if (!ok) { remove(temp); snprintf(error, errorSize, "the disk is full or can't be written to"); cJSON_Delete(index); return 0; }
    remove(path);
    if (rename(temp, path) != 0) { snprintf(error, errorSize, "%s couldn't be written", path); cJSON_Delete(index); return 0; }
    if (shot) { path_in(path, sizeof path, folder, "shot.png"); write_png(path, shot, BASE_W, BASE_H); }
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

#ifdef SAVE_SELFTEST
/* Testing only (the Linux build): the save's text both ways - the old (copy, print) and the light one - to two files. */
void save_selftest(const char *oldPath, const char *newPath)
{
    cJSON *root = capture();
    char *json = cJSON_PrintUnformatted(root);
    cJSON_Delete(root);
    FILE *f = fopen(oldPath, "wb"); if (f && json) fwrite(json, 1, strlen(json), f); if (f) fclose(f);
    free(json);
    root = capture_light();
    Sink *s = (Sink *)calloc(1, sizeof(Sink));
    s->plain = fopen(newPath, "wb");
    emit(s, root); sink_flush(s);
    if (s->plain) fclose(s->plain);
    free(s);
    cJSON_Delete(root);
    /* and a real save, read back */
    char error[300];
    if (!save_write("selftest", 0, NULL, error, sizeof error)) { printf("SELFTEST save failed: %s\n", error); return; }
    SaveInfo list[4];
    if (save_list(list, 4) < 1) { printf("SELFTEST no save listed\n"); return; }
    char *text = save_read(list[0].folder, error, sizeof error);
    f = fopen("save_read.json", "wb"); if (f && text) fwrite(text, 1, strlen(text), f); if (f) fclose(f);
    free(text);
    printf("SELFTEST done (%s)\n", list[0].folder);
}
#endif

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
