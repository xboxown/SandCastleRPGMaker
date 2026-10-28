/*
 * The Sandcastle player: runs a game built in Sandcastle RPG Maker on
 * machines the Windows engine doesn't (Linux, Mac, MorphOS, AmigaOS 4,
 * Amiga 68k). It reads the game from the folder "<program>_Data" beside it,
 * or the folder given on the command line.
 */
#include "platform.h"
#include "world.h"
#include "font.h"
#include "dialog.h"
#include "events.h"
#include "lighting.h"
#include "schedules.h"
#include "use.h"
#include "sound.h"
#include "weather.h"
#include "screen.h"
#include "vitals.h"
#include "combat.h"
#include "gumps.h"
#include "dolls.h"
#include "save.h"
#include "magic.h"
#include "mechanisms.h"
#include "vehicles.h"
#include "video.h"
#include "books.h"
#include "prompt.h"
#include "work.h"
#include "traps.h"
#include "eggs.h"
#include "shop.h"
#include "slider.h"
#include "script.h"
#include "conditions.h"
#include "items.h"
#include "pack.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

double sc_strtod(const char *text, char **end);

static uint32_t pixels[SCREEN_W * SCREEN_H];
void main_take_screenshot(void) { save_take_screenshot(pixels); }

/* Game.log beside the game: each step of starting, so a game that stops can say where. */
static void note(const char *step)
{
#if defined(__amigaos__) && !defined(__MORPHOS__)
    static int fresh = 0; /* amiga_start.c began Game.log */
#else
    static int fresh = 1;
#endif
    FILE *f = fopen("Game.log", fresh ? "w" : "a");
    fresh = 0;
    if (!f) return;
    fprintf(f, "%s\n", step);
    fclose(f);
}

void game_log(const char *line) { note(line); }

/* Testing: a key pressed at a moment ("--press 2:E,3.5:1": Enter at 2 s, the 1 key at 3.5 s; T7 = double-click the character with id 7). */
static int testDx, testDy; /* testing: a direction held by --press "J..." */
static int forceWindow; /* "window" / --window: a window, not full screen */

static void scripted_presses(const char *presses, double t, double *done)
{
    for (const char *k = presses; k && *k; k = strchr(k, ','), k = k ? k + 1 : NULL)
    {
        double at = sc_strtod(k, NULL);
        const char *what = strchr(k, ':');
        if (!what || at <= *done || at > t) continue;
        what++;
        if (*what == 'T') { int actor = world_actor_by_character(atoi(what + 1)); if (actor > 0) events_talk(actor); }
        else if (*what == 'W') { char name[64]; snprintf(name, sizeof name, "%.*s", (int)strcspn(what + 1, ","), what + 1); events_set_variable(name, 1); } /* Wname: a switch on */
        else if (*what == 'C') { const char *semi = strchr(what, ';'); if (semi) use_at(atoi(what + 1), atoi(semi + 1)); } /* C160;100: a double-click there */
        else if (*what == 'K') combat_toggle_mode(); /* K: C pressed (combat mode) */
        else if (*what == 'q') { int x = atoi(what + 1), y = 0, id = -1; const char *comma = strchr(what, ' '); comma = strchr(what, '/'); const char *semi = strchr(what, ';'); if (comma) y = atoi(comma + 1); if (semi) id = atoi(semi + 1); int uid = world_thing_at_cell(4, id, x, y, 0); char line[96]; snprintf(line, sizeof line, "DBG qty: floor %d/%d %d, carried %d", x, y, uid ? world_thing_quantity(uid) : 0, items_count_any(world_actor_character(0), 4, id)); note(line); } /* q11/7;80: the stack of object 80 on cell 11,7, and how many of it the player carries */
        else if (*what == 'B') books_read(atoi(what + 1)); /* B1: read the book with id 1 */
        else if (*what == 'Z') gumps_next_stats(); /* Z: the stats paperdoll */
        else if (*what == 'Q') { for (int a = 0; a < world_actor_count(); a++) { int x, y, f; world_actor_cell(a, &x, &y, &f); int dbgHp = 0; vitals_hp(world_actor_character(a), &dbgHp); char line[160]; snprintf(line, sizeof line, "DBG actor %d char %d at (%d,%d) floor %d gone %d food %d hp %d seat %d fight %d", a, world_actor_character(a), x, y, f, world_actor_gone(a), vitals_food(world_actor_character(a)), dbgHp, world_seated_on(a), combat_busy(a)); note(line); } } /* Q: where everyone is (testing) */
        else if (*what == 'J') { char d = what[1]; testDx = d == 'e' ? 1 : d == 'w' ? -1 : 0; testDy = d == 's' ? 1 : d == 'n' ? -1 : 0; } /* Jn/Je/Js/Jw: hold a direction, Jx: let go */
        else if (*what == 'U') { int uid = world_thing_by_instance(atoi(what + 1)); if (uid) use_thing(uid); } /* U38: double-click the placed thing with instance id 38 */
        else if (*what == 'I') gumps_open_paperdoll(world_actor_character(0)); /* I: the inventory */
        else if (*what == 'D' || *what == 'V' || *what == 'R') /* D160;100 press, V moves, R releases there */
        {
            const char *semi = strchr(what, ';');
            if (semi) { int x = atoi(what + 1), y = atoi(semi + 1); if (*what == 'D') { if (!video_input(1)) gumps_mouse_down(x, y); } else if (*what == 'V') { platform_warp_mouse(x, y); gumps_mouse_move(x, y); } else gumps_mouse_up(x, y); }
        }
        else if (*what == 'X') gumps_close_top(); /* X: Escape on a window */
        else if (*what == 'L') { const char *t = what + 1; size_t n = strcspn(t, ","); for (size_t q = 0; q < n; q++) if (!prompt_key(0, t[q])) dolls_key(0, t[q]); } /* Lname: letters typed */
        else if (*what == 'F') { save_take_screenshot(pixels); dolls_open_save_screen(); } /* F: S pressed (the Save/Load screen) */
        else if (*what == 'A') combat_attack(0, world_actor_by_character(atoi(what + 1))); /* A7: attack the character with id 7 */
        else if (*what == 'G') { int uid = world_thing_by_instance(atoi(what + 1)); uint32_t t0 = platform_ticks(); int ok = uid ? world_reachable_thing(uid) : -1; char line[96]; snprintf(line, sizeof line, "DBG reach %d: %d (%u ms)", atoi(what + 1), ok, (unsigned)(platform_ticks() - t0)); note(line); } /* G1107: can the player reach placed thing 1107 */
        else if (*what == 'H') { int actor = world_actor_by_character(atoi(what + 1)); double ax, ay, ox, oy; world_actor_pixel(actor, &ax, &ay); world_camera_origin(&ox, &oy); int sx = (int)(ax - ox), sy = (int)(ay - oy - 4); char line[96]; snprintf(line, sizeof line, "DBG click char %d at screen (%d,%d) hits actor %d", atoi(what + 1), sx, sy, world_actor_at(sx, sy)); note(line); gumps_mouse_down(sx, sy); gumps_mouse_up(sx, sy); } /* H4: a click on character 4's middle */
        else if (*what == 'Y') gumps_flash_pointer(atoi(what + 1)); /* Y8: the Blocked pointer flashed (testing) */
        else if (*what == 'P') { int actor = world_actor_by_character(atoi(what + 1)); if (actor > 0 && !items_in_party(atoi(what + 1))) { items_set_in_party(atoi(what + 1), 1); schedules_join(actor, 1); } } /* P2: character 2 joins the party */
        else if (*what == 'N') { int id = atoi(what + 1); const char *a = strchr(what, ';'), *b = a ? strchr(a + 1, ';') : NULL; int actor = world_actor_by_character(id); if (b && actor > 0) { const char *c = strchr(b + 1, ';'); if (c) world_actor_place(actor, atoi(a + 1), atoi(b + 1), atoi(c + 1)); else world_teleport(actor, atoi(a + 1), atoi(b + 1)); } } /* N2;12;12: character 2 put at (12, 12) (N2;12;12;1: on floor 1) */
        else if (*what == 'M') { const char *comma = strchr(what, ';'); if (comma) world_teleport(0, atoi(what + 1), atoi(comma + 1)); } /* M7;10: the player to (7, 10) */
        else
        {
            enum PlatformKey key = *what == 'E' ? KEY_ENTER : *what == 'S' ? KEY_SPACE : *what >= '1' && *what <= '9' ? (enum PlatformKey)(KEY_1 + (*what - '1'))
                : *what == 'u' ? KEY_UP : *what == 'd' ? KEY_DOWN : *what == 'l' ? KEY_LEFT : *what == 'r' ? KEY_RIGHT : *what == 'e' ? KEY_ESCAPE : KEY_OTHER;
            if (slider_key(key)) { } /* the slider (slider.c) */
            else if (dialog_showing()) dialog_key(key);
            else if (!prompt_key(key, 0) && !dolls_key(key, 0)) mechanisms_key(key); /* a question, a paperdoll, the sleep slider */
        }
    }
    *done = t;
}

#if defined(__amigaos__) || defined(__MORPHOS__)
/* libnix: the stack the game runs with, whatever the icon or shell gives it. */
unsigned long __stack = 262144; /* 256 KB: the MP3 decoder alone needs some 17 KB when music starts */
#endif

/* The least free memory an Amiga needs to start the game (megabytes). */
#ifndef GAME_MIN_RAM_MB
#define GAME_MIN_RAM_MB 128
#endif

/* Testing: the frame as a 24-bit BMP (--shot), arrow keys held by a script (--keys "R:1.5,U:0.5"). */
static void save_bmp(const char *path)
{
    FILE *f = fopen(path, "wb");
    if (!f) return;
    unsigned char h[54] = { 'B', 'M' };
    int size = 54 + SCREEN_W * SCREEN_H * 3;
    h[2] = size & 255; h[3] = (size >> 8) & 255; h[4] = (size >> 16) & 255; h[5] = (size >> 24) & 255;
    h[10] = 54; h[14] = 40; h[18] = SCREEN_W & 255; h[19] = SCREEN_W >> 8; h[22] = SCREEN_H & 255; h[23] = SCREEN_H >> 8; h[26] = 1; h[28] = 24;
    fwrite(h, 1, 54, f);
    for (int y = SCREEN_H - 1; y >= 0; y--)
        for (int x = 0; x < SCREEN_W; x++)
        {
            uint32_t c = pixels[y * SCREEN_W + x];
            unsigned char px[3] = { c & 255, (c >> 8) & 255, (c >> 16) & 255 };
            fwrite(px, 1, 3, f);
        }
    fclose(f);
}

/* The timing diary: every 10 seconds Game.log says the frame rate and where a frame's time went (so a slow
   machine can say why). Parts: 0 keys and sounds, 1 events, 2 the world going on, 3 the world drawn,
   4 clouds and light, 5 weather, 6 barks/fights/traps, 7 windows/boxes/pointer, 8 put on the screen. */
#define TIMING_PARTS 9
static double timingMs[TIMING_PARTS], timingMark, timingFrom;
static int timingFrames;
static void timing(int part) { double t = platform_ms_precise(); timingMs[part] += t - timingMark; timingMark = t; }
static void timing_frame_done(void)
{
    timingFrames++;
    double span = timingMark - timingFrom;
    if (span < 10000) return;
    static const char *NAMES[TIMING_PARTS] = { "input", "events", "update", "world", "light", "weather", "barks", "windows", "present" };
    char line[400];
    /* tenths as whole numbers: the Amiga's C library prints some floating-point numbers wrongly */
    int fps10 = (int)(timingFrames * 10000.0 / span + 0.5);
    int n = snprintf(line, sizeof line, "Timing: %d.%d fps, ms a frame -", fps10 / 10, fps10 % 10);
    for (int i = 0; i < TIMING_PARTS && n < (int)sizeof line; i++) { int t10 = (int)(timingMs[i] * 10 / timingFrames + 0.5); n += snprintf(line + n, sizeof line - n, " %s %d.%d", NAMES[i], t10 / 10, t10 % 10); }
    int things, drawnNow;
    world_draw_stats(&things, &drawnNow);
    if (n < (int)sizeof line) snprintf(line + n, sizeof line - n, " (%d things, %d drawn)", things, drawnNow);
    note(line);
    for (int i = 0; i < TIMING_PARTS; i++) timingMs[i] = 0;
    timingFrames = 0;
    timingFrom = timingMark;
}

/* A save to load (the Save/Load screen, Continue): done between frames, the game built anew from it. */
static char loadFolder[1200], loadName[64]; static int loadRequested;
void dolls_request_load(const char *folder, const char *name) { snprintf(loadFolder, sizeof loadFolder, "%s", folder); snprintf(loadName, sizeof loadName, "%s", name); loadRequested = 1; }

/* LoadGame: the game built anew from a save, as a new game is built from the project, then what was only in play put back
   (the clock, the main character's exact spot, variables, hunger and ailments, fired events, weather). The last game is let go. */
static int rebuild(cJSON *root, cJSON *runtime);
static int title_with_intro(void);

static int load_game(const char *folder, const char *name)
{
    char error[300];
    char *text = save_read(folder, error, sizeof error);
    if (!text) { game_error(108, ""); return 0; }
    cJSON *root = cJSON_Parse(text);
    free(text);
    cJSON *runtime = cJSON_GetObjectItem(root, "Runtime");
    if (!root || !runtime || !cJSON_GetObjectItem(root, "Project")) { cJSON_Delete(root); game_error(108, ""); return 0; }
    cJSON *format = cJSON_GetObjectItem(runtime, "Format");
    if (cJSON_IsNumber(format) && format->valueint > SAVE_FORMAT) { cJSON_Delete(root); game_error(109, ""); return 0; }
    int ok = rebuild(root, runtime);
    if (ok > 0) { char line[160]; snprintf(line, sizeof line, "Loaded \"%s\".", name); note(line); }
    return ok;
}

/* New Game (during play) / Return To Title: a fresh game from game.sdbx, as when the game starts. */
static char gameFolder[1024];
static int new_game(int toTitle)
{
    char path[1100];
    snprintf(path, sizeof path, "%s/game.sdbx", gameFolder);
    char *text = (char *)pack_read(path, NULL);
    cJSON *root = text ? cJSON_Parse(text) : NULL;
    free(text);
    if (!root) { game_error(303, "game.sdbx"); return 0; }
    int ok = rebuild(root, NULL);
    if (ok > 0) { note(toTitle ? "Back to the Title Screen." : "A new game."); if (toTitle) title_with_intro(); }
    return ok;
}

static cJSON *get_(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num_(cJSON *o, const char *k, int fallback) { cJSON *v = get_(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static void setn(cJSON *o, const char *k, double v) { if (get_(o, k)) cJSON_ReplaceItemInObject(o, k, cJSON_CreateNumber(v)); else cJSON_AddNumberToObject(o, k, v); }

/* Change Map to another world (GameTestWindow.Worlds): the game as it is now, the world left remembered as it was, the party
   taken along to the other one (as it was left, or fresh), and the game goes on there as a loaded game does. */
static int travel(int worldId, int hasSpot, int x, int y, int floorOf)
{
    cJSON *root = save_capture(), *project = get_(root, "Project"), *runtime = get_(root, "Runtime");
    cJSON *states = get_(runtime, "WorldStates");
    if (!states) { states = cJSON_CreateObject(); cJSON_AddItemToObject(runtime, "WorldStates", states); }
    cJSON *leaving = cJSON_DetachItemFromObject(project, "Map"), *w, *arriving = NULL;
    /* the party goes with the main character: out of this world's placements */
    cJSON *party = cJSON_CreateArray(), *placed = get_(leaving, "Characters");
    for (int i = cJSON_GetArraySize(placed) - 1; i >= 0; i--)
    {
        cJSON *pc = cJSON_GetArrayItem(placed, i);
        int id = num_(pc, "CharacterId", -1);
        if (items_in_party(id) && !vitals_dead(id)) cJSON_AddItemToArray(party, cJSON_DetachItemFromArray(placed, i));
    }
    /* the events and things of the world left: what of their pages has fired stays with it */
    cJSON *fired = get_(runtime, "FiredPages"), *kept = cJSON_CreateArray();
    for (int i = cJSON_GetArraySize(fired) - 1; i >= 0; i--)
    {
        const char *owner = cJSON_GetStringValue(get_(cJSON_GetArrayItem(fired, i), "Owner"));
        if (owner && (!strncmp(owner, "Event:", 6) || !strncmp(owner, "Object:", 7))) cJSON_AddItemToArray(kept, cJSON_DetachItemFromArray(fired, i));
    }
    cJSON_DeleteItemFromObject(leaving, "FiredPagesRuntime");
    cJSON_AddItemToObject(leaving, "FiredPagesRuntime", kept);
    int leavingId = num_(leaving, "Id", 0);
    if (leavingId <= 0) leavingId = num_(cJSON_GetArrayItem(get_(project, "Worlds"), 0), "Id", 1); /* a save from before worlds: the starting world */
    char key[16];
    snprintf(key, sizeof key, "%d", leavingId);
    cJSON_DeleteItemFromObject(states, key);
    cJSON_AddItemToObject(states, key, leaving);
    snprintf(key, sizeof key, "%d", worldId);
    arriving = cJSON_DetachItemFromObject(states, key);
    if (!arriving) cJSON_ArrayForEach(w, get_(project, "Worlds")) if (num_(w, "Id", -1) == worldId) { arriving = cJSON_Duplicate(w, 1); break; }
    if (!arriving) { cJSON_Delete(party); cJSON_Delete(root); return 0; }
    int width = num_(arriving, "Width", 1), height = num_(arriving, "Height", 1);
    if (!hasSpot) { x = num_(arriving, "StartX", -1); y = num_(arriving, "StartY", -1); floorOf = num_(arriving, "StartFloor", 1); }
    if (x < 0 || y < 0 || x >= width || y >= height) { x = width / 2; y = height / 2; }
    if (floorOf < 1) floorOf = 1;
    if (floorOf > 9) floorOf = 9;
    setn(arriving, "StartX", x); setn(arriving, "StartY", y); setn(arriving, "StartFloor", floorOf);
    /* the party beside them */
    cJSON *there = get_(arriving, "Characters");
    if (!there) { there = cJSON_CreateArray(); cJSON_AddItemToObject(arriving, "Characters", there); }
    int nextId = 1, n = 0;
    cJSON_ArrayForEach(w, there) if (num_(w, "Id", 0) >= nextId) nextId = num_(w, "Id", 0) + 1;
    while (cJSON_GetArraySize(party) > 0)
    {
        cJSON *pc = cJSON_DetachItemFromArray(party, 0);
        int px = x + n % 3 - 1, py = y + 1 + n / 3;
        setn(pc, "Id", nextId++);
        setn(pc, "X", px < 0 ? 0 : px >= width ? width - 1 : px); setn(pc, "Y", py < 0 ? 0 : py >= height ? height - 1 : py); setn(pc, "Floor", floorOf);
        cJSON_AddItemToArray(there, pc);
        n++;
    }
    cJSON_Delete(party);
    cJSON *back = cJSON_DetachItemFromObject(arriving, "FiredPagesRuntime"); /* its own, as they were left */
    if (back && fired) while (cJSON_GetArraySize(back) > 0) cJSON_AddItemToArray(fired, cJSON_DetachItemFromArray(back, 0));
    cJSON_Delete(back);
    cJSON_AddItemToObject(project, "Map", arriving);
    /* the main character at the spot; the new world's own weather; not fighting */
    cJSON *tileset = get_(arriving, "Tileset");
    int tw = num_(tileset, "TileWidth", 32), th = num_(tileset, "TileHeight", 32);
    setn(runtime, "PlayerPixelX", x * tw + tw / 2.0); setn(runtime, "PlayerPixelY", y * th + th / 2.0); setn(runtime, "PlayerLift", (floorOf - 1) * 5);
    cJSON_DeleteItemFromObject(runtime, "Weather");
    if (get_(runtime, "CombatMode")) cJSON_ReplaceItemInObject(runtime, "CombatMode", cJSON_CreateFalse());
    const char *name = cJSON_GetStringValue(get_(arriving, "MapName"));
    char line[200];
    snprintf(line, sizeof line, "You arrive in %s.", name ? name : "another world");
    int ok = rebuild(root, runtime);
    if (ok > 0) { note(line); screen_fade(0, 1, 0); screen_fade(0, 0, 0.5); } /* black, then the world fades in */
    return ok;
}

/* The title, after its intro video if it has one (System Settings > Title Screen). 1 = there's a title (now, or when the video ends). */
static int introPending; /* the title's intro video playing: as the title, nothing in the world goes on */
static void intro_done(int ok, void *context) { (void)ok; (void)context; introPending = 0; if (dolls_open_title()) note("Title Screen."); }

static int title_with_intro(void)
{
    cJSON *title = cJSON_GetObjectItem(cJSON_GetObjectItem(cJSON_GetObjectItem((cJSON *)world_json(), "Project"), "Settings"), "Title");
    cJSON *intro = cJSON_GetObjectItem(title, "IntroVideoId"), *design = cJSON_GetObjectItem(title, "DollDesignId");
    if (cJSON_IsNumber(intro) && intro->valueint >= 0 && video_exists(intro->valueint) && cJSON_IsNumber(design) && dolls_design_exists(design->valueint))
        return introPending = video_play(intro->valueint, 1, 1.0, intro_done, NULL);
    return dolls_open_title();
}

/* The last game let go of; the game built from root (a save's: then runtime puts back what was only in play). -1: it can't go on. */
static int rebuild(cJSON *root, cJSON *runtime)
{
    cJSON *old = (cJSON *)world_json();
    /* the last game, let go of */
    video_stop();
    prompt_reset();
    work_reset();
    traps_reset();
    sound_stop_all();
    gumps_reset(); dolls_reset(); dialog_reset(); screen_reset(); magic_reset(); mechanisms_reset(); vehicles_reset(); slider_reset();
    events_reset(); items_reset(); script_reset(); world_reset();
    /* the worlds left behind (a save's, or none: a new game) */
    save_set_world_states(runtime && cJSON_GetObjectItem(runtime, "WorldStates") ? cJSON_Duplicate(cJSON_GetObjectItem(runtime, "WorldStates"), 1) : NULL);
    /* the saved one */
    if (!world_build(root)) { note("The saved game couldn't be built."); cJSON_Delete(root); return -1; }
    events_load();
    if (runtime) { events_restore(runtime); script_restore(runtime); books_restore(runtime); }
    else books_reset();
    dolls_load();
    vitals_load();
    if (runtime) vitals_restore(runtime);
    combat_load();
    if (runtime) { combat_restore(runtime); conditions_restore(runtime); world_restore_player(runtime); }
    else conditions_restore(NULL);
    eggs_reset(); /* the eggs look afresh (eggs.c) */
    shop_reset();
    schedules_load();
    schedules_start();
    weather_load();
    if (runtime) weather_restore(runtime);
    /* the dead lie in their bodies, holding what they had */
    cJSON *it;
    if (runtime) cJSON_ArrayForEach(it, cJSON_GetObjectItem(cJSON_GetObjectItem(cJSON_GetObjectItem(root, "Project"), "Map"), "Items"))
    {
        cJSON *id = cJSON_GetObjectItem(it, "ItemId");
        if (!cJSON_IsNumber(id) || id->valueint > -1000) continue;
        int actor = world_actor_by_character(-1000 - id->valueint);
        cJSON *z = cJSON_GetObjectItem(it, "Z");
        int floorOf = (cJSON_IsNumber(z) ? z->valueint : 0) / 5 + 1;
        int uid = actor >= 0 ? world_put_body(actor, cJSON_GetObjectItem(it, "X")->valueint, cJSON_GetObjectItem(it, "Y")->valueint, floorOf) : -1;
        if (uid >= 0) items_park(uid, items_body_from_json(cJSON_GetObjectItem(it, "OwnContents")));
    }
    events_player_moved();
    cJSON_Delete(old);
    return 1;
}

int main(int argc, char **argv)
{
    char folder[1024] = "Game_Data";
    const char *shot = NULL, *keys = NULL, *say = NULL, *presses = NULL;
    int noSound = 0, noTitle = 0;
    double shotAt = -1, pressedTill = -1;
    for (int i = 1; i < argc; i++)
    {
        if (strcmp(argv[i], "--shot") == 0 && i + 1 < argc) shot = argv[++i];
        else if (strcmp(argv[i], "--keys") == 0 && i + 1 < argc) keys = argv[++i];
        else if (strcmp(argv[i], "--say") == 0 && i + 1 < argc) say = argv[++i]; /* testing: words drawn in the game's fonts */
        else if (strcmp(argv[i], "--press") == 0 && i + 1 < argc) presses = argv[++i];
        else if (strcmp(argv[i], "--shot-at") == 0 && i + 1 < argc) shotAt = sc_strtod(argv[++i], NULL);
        else if (!strcmp(argv[i], "nosound") || !strcmp(argv[i], "NOSOUND") || !strcmp(argv[i], "--nosound")) noSound = 1;
        else if (!strcmp(argv[i], "--notitle")) noTitle = 1; /* testing: straight into the game */
        else if (!strcmp(argv[i], "--window") || !strcmp(argv[i], "window") || !strcmp(argv[i], "WINDOW")) forceWindow = 1; /* in a window whatever the game's setting */
        else if (!strcmp(argv[i], "paula") || !strcmp(argv[i], "PAULA")) platform_audio_route(1);
        else if (!strcmp(argv[i], "ahi") || !strcmp(argv[i], "AHI")) platform_audio_route(2);
        else snprintf(folder, sizeof folder, "%s", argv[i]);
    }
    note("Started.");
    if (!platform_can_run())
    {
        note(platform_error());
        platform_message("This Amiga can't run the game", platform_error());
        return 20;
    }
    note("The machine can run it.");
#if defined(__amigaos__) || defined(__MORPHOS__)
    /* An Amiga needs the game's memory free before it starts (128 MB at least; the user's rule: say so, don't crash). */
    long freeMb = platform_free_memory_mb();
    if (freeMb >= 0 && freeMb < GAME_MIN_RAM_MB)
    {
        char text[256];
        snprintf(text, sizeof text, "This game needs %d MB of free memory;\nonly %ld MB is free.\n\nClose other programs, or add memory.", GAME_MIN_RAM_MB, freeMb);
        note(text);
        platform_message("Not enough memory", text);
        return 20;
    }
    {
        char text[64];
        snprintf(text, sizeof text, "Memory: %ld MB free in one piece.", freeMb);
        note(text);
    }
#endif
    note("Loading the game...");
    if (!world_load(folder))
    {
        char text[700];
        snprintf(text, sizeof text, "The game couldn't start:\n%s", world_error());
        fprintf(stderr, "%s\n", text);
        note(text);
        platform_message("Game", text);
        return 1;
    }
    note("Loaded. Opening the window...");
    /* System Settings > Game Window: in a window, or full screen (DisplaySettings.Mode 1) */
    cJSON *display = cJSON_GetObjectItem(cJSON_GetObjectItem(cJSON_GetObjectItem((cJSON *)world_json(), "Project"), "Settings"), "Display");
    cJSON *mode = cJSON_GetObjectItem(display, "Mode");
    int fullScreen = cJSON_IsNumber(mode) && mode->valueint == 1 && !forceWindow;
    if (!platform_open(world_title()[0] ? world_title() : "Game", fullScreen))
    {
        char text[400];
        snprintf(text, sizeof text, "The game couldn't open its window:\n%s", platform_error());
        fprintf(stderr, "%s\n", text);
        note(text);
        platform_message("Game", text);
        return 1;
    }
    note("Window open. Starting.");
    save_init(folder); /* the saves: beside the game */
    snprintf(gameFolder, sizeof gameFolder, "%s", folder);
    { FILE *quiet = fopen("nosound", "rb"); if (quiet) { fclose(quiet); noSound = 1; } } /* a file called nosound beside the game: no sound */
    { FILE *p = fopen("paula", "rb"); if (p) { fclose(p); platform_audio_route(1); note("Sound through Paula (paula)."); } } /* a file called paula: audio.device, never AHI */
    { FILE *p = fopen("ahi", "rb"); if (p) { fclose(p); platform_audio_route(2); note("Sound through AHI only (ahi)."); } }
    note(noSound ? "Sound off (nosound)." : "Starting the sound...");
    sound_load(folder, !noSound); video_load(folder);
    note(sound_status());
    events_load();
    dolls_load();
    note("Events ready.");
    vitals_load();
    combat_load();
    eggs_reset(); /* the eggs look afresh (eggs.c) */
    shop_reset();
    schedules_load();
    schedules_start();
    note("Characters ready.");
    weather_load();
    events_player_moved(); /* where the player starts: arriving doesn't set its event off */
    note("Running.");
    if (!noTitle && title_with_intro() && dolls_title_open()) note("Title Screen."); /* System Settings > Title Screen: before play, the game waits */
    int held[4] = { 0, 0, 0, 0 }; /* left, right, up, down: when pressed (0 = not held) */
    int order = 0, running = 1;
    uint32_t last = platform_ticks(), start = last, worldMs = 0;
    timingMark = timingFrom = platform_ms_precise();
    while (running)
    {
        PlatformEvent e;
        while (platform_poll(&e))
        {
            if (e.type == EV_QUIT) running = 0;
            else if (e.type == EV_MOUSE_MOVE) { if (slider_open_now()) { slider_mouse_move(e.mouseX, e.mouseY); continue; } dialog_mouse_move(e.mouseX, e.mouseY); if (!dialog_showing()) gumps_mouse_move(e.mouseX, e.mouseY); }
            else if (e.type == EV_MOUSE_DOWN && e.button == 1)
            {
                if (video_input(1)) continue; /* a click skips a video */
                if (slider_mouse_down(e.mouseX, e.mouseY)) continue; /* the slider up: its clicks */
                if (dialog_showing()) dialog_click(e.mouseX, e.mouseY);
                else if (!vitals_game_over() || dolls_window_count()) gumps_mouse_down(e.mouseX, e.mouseY); /* identify, use, drag (GameTestWindow.Interaction); dead: only the Save/Load screen */
                else
                {
                    /* the game over: a double click on the main character's body opens Save/Load (to load a game) */
                    static uint32_t lastDeadClick;
                    int actor, uid;
                    world_hit(e.mouseX, e.mouseY, &actor, &uid);
                    uint32_t at = platform_ticks();
                    if (actor == 0 || (uid && world_body_of(world_actor_character(0)) == uid))
                    {
                        if (at - lastDeadClick < 400) { save_take_screenshot(pixels); dolls_open_save_screen(); lastDeadClick = 0; }
                        else lastDeadClick = at;
                    }
                }
            }
            else if (e.type == EV_MOUSE_UP && e.button == 1 && slider_open_now()) slider_mouse_up(e.mouseX, e.mouseY);
            else if (e.type == EV_MOUSE_UP && e.button == 1 && !dialog_showing()) gumps_mouse_up(e.mouseX, e.mouseY);
            else if (e.type == EV_TEXT) { if (!prompt_key(0, e.ch)) dolls_key(0, e.ch); } /* a letter for a question's box, or a paperdoll's text box */
            else if (e.type == EV_KEY_DOWN || e.type == EV_KEY_UP)
            {
                int down = e.type == EV_KEY_DOWN;
                if (down && !dialog_showing() && e.key != KEY_F11 && dolls_key(e.key, 0)) continue; /* typing, or a modal paperdoll: nothing else hears it */
                if (down && e.key != KEY_F11 && prompt_key(e.key, 0)) continue; /* a question being answered */
                if (down && e.key != KEY_F11 && video_input(e.key == KEY_ESCAPE || e.key == KEY_SPACE || e.key == KEY_ENTER)) continue; /* a video playing: these skip it */
                if (down && e.key != KEY_F11 && slider_key(e.key)) continue; /* the slider (sleeping, haggling) */
                if (down && e.key != KEY_F11 && mechanisms_key(e.key)) continue; /* sleeping */
                if (down && dialog_showing() && e.key != KEY_F11)
                {
                    dialog_key(e.key); /* while a box is up the keys are its own */
                    continue;
                }
                switch (e.key)
                {
                    case KEY_LEFT: held[0] = down ? ++order : 0; break;
                    case KEY_RIGHT: held[1] = down ? ++order : 0; break;
                    case KEY_UP: held[2] = down ? ++order : 0; break;
                    case KEY_DOWN: held[3] = down ? ++order : 0; break;
                    case KEY_ESCAPE: if (down) gumps_close_top(); break; /* Escape closes the topmost window only - it never quits (Ctrl+Q asks) */
                    case KEY_S: /* Exult: S for the Save/Load screen (the view as it is now is its screenshot) */
                        if (down && !dolls_title_open() && !dolls_modal_open()) { save_take_screenshot(pixels); dolls_open_save_screen(); }
                        break;
                    case KEY_I: if (down && !events_cinema() && !events_keys_forbidden()) gumps_next_inventory(); break; /* Exult: I for the party's inventories, one after another */
                    case KEY_Z: if (down && !events_cinema() && !events_keys_forbidden()) gumps_next_stats(); break; /* Exult: Z for their stats */
                    case KEY_F11: if (down) platform_toggle_fullscreen(); break;
                    case KEY_CTRL_Q: if (down) dolls_ask_to_quit(); break; /* Ctrl+Q: the Quit prompt, any time */
                    case KEY_C: if (down && !vitals_game_over() && !events_combat_forbidden() && !events_keys_forbidden()) combat_toggle_mode(); break; /* Exult: C for combat */
                    default: break;
                }
            }
        }
        /* The most recently pressed direction still held wins (as the Windows engine). */
        int best = 0, dx = 0, dy = 0;
        for (int i = 0; i < 4; i++) if (held[i] > best) best = held[i];
        if (best)
        {
            if (held[0] == best) dx = -1; else if (held[1] == best) dx = 1; else if (held[2] == best) dy = -1; else dy = 1;
        }
        else { dx = testDx; dy = testDy; }
        uint32_t now = platform_ticks();
        if (presses) scripted_presses(presses, (now - start) / 1000.0, &pressedTill);
        if (shotAt >= 0 && (now - start) / 1000.0 >= shotAt)
        {
            if (dolls_title_open()) memset(pixels, 0, sizeof(uint32_t) * SCREEN_W * SCREEN_H);
            else
            {
                world_draw(pixels, now);
                weather_draw_clouds(pixels);
                weather_draw(pixels);
                world_draw_barks(pixels);
            }
            combat_draw(pixels, worldMs);
            traps_draw(pixels, worldMs);
            gumps_draw(pixels, worldMs);
            screen_draw(pixels, now);
            dialog_draw(pixels, now); slider_draw(pixels, now);
            video_draw(pixels);
            gumps_draw_pointer(pixels, now);
            save_bmp(shot);
            break;
        }
        if (keys) /* testing: the script's key for this moment, then the shot */
        {
            double t = (now - start) / 1000.0 - 0.5, at = 0;
            const char *k = keys;
            dx = dy = 0;
            int done = 1;
            while (*k && t >= 0)
            {
                char c = *k; double secs = 0;
                secs = sc_strtod(k + 2, NULL);
                if (t < at + secs) { dx = c == 'L' ? -1 : c == 'R' ? 1 : 0; dy = c == 'U' ? -1 : c == 'D' ? 1 : 0; done = 0; break; }
                at += secs;
                k = strchr(k, ',');
                if (!k) break;
                k++;
            }
            if (t < 0) done = 0;
            if (done && shot && shotAt < 0) { world_draw(pixels, now); dialog_draw(pixels, now); slider_draw(pixels, now); save_bmp(shot); break; }
        }
        else if (shot && shotAt < 0 && now - start > 1000)
        {
            world_draw(pixels, now);
            if (say)
            {
                const Font *a = font_get("InGameFont"), *b = font_get("DialogFont");
                font_draw(a, pixels, 6, 6, say, 0);
                const char *starts[8]; int lengths[8];
                int n = font_wrap(b, say, 150, 8, starts, lengths);
                for (int l = 0; l < n; l++) { char line[256]; snprintf(line, sizeof line, "%.*s", lengths[l], starts[l]); font_draw(b, pixels, 6, 30 + l * font_line_height(b), line, 0); }
            }
            save_bmp(shot);
            break;
        }
        double seconds = (now - last) / 1000.0;
        if (seconds > 0.1) seconds = 0.1;
        last = now;
        sound_update(seconds * 1000);
        screen_update(seconds * 1000);
        int title = dolls_title_open() || introPending; /* the Title Screen (or its intro video): nothing in the world goes on behind it */
        video_update(seconds * 1000);
        timing(0);
        int paused = dialog_showing() || title || mechanisms_pausing() || slider_open_now() || video_playing() || books_open() || prompt_open() || gumps_pausing() || vitals_game_over(); /* a box up: the game waits, as in an Exult conversation (and while the party sleeps) */
        if (!title) events_update(seconds * 1000, paused);
        else gumps_update(seconds * 1000);
        timing(1);
        if (events_quit_requested()) running = 0; /* Quit Game */
        if (loadRequested) { loadRequested = 0; if (load_game(loadFolder, loadName) < 0) running = 0; }
        { int w, hs, wx, wy, wf; if (events_world_trip(&w, &hs, &wx, &wy, &wf) && travel(w, hs, wx, wy, wf) < 0) running = 0; } /* Change Map to another world */
        if (events_restart_requested()) { int title = events_restart_requested() == 2; events_clear_restart(); if (new_game(title) < 0) running = 0; } /* New Game, Return To Title */
        /* Exult's "gumps pause the game" (GameTestWindow.UpdateGamePause): as it pauses, a direction held is let go and the
           avatar stands where it is - the key has to be pressed again (so an event's Walk To isn't taken for the player taking over). */
        static int wasPaused, wasCinema;
        int cinemaNow = events_cinema(); /* Cinema Mode starting lets go of the keys too (GameTestWindow.Cinema) */
        if ((paused && !wasPaused) || (cinemaNow && !wasCinema)) { for (int i = 0; i < 4; i++) held[i] = 0; testDx = testDy = 0; world_keys_let_go(); }
        wasPaused = paused; wasCinema = cinemaNow;
        if (paused) dx = dy = 0;
        if (!paused)
        {
            if (events_cinema()) dx = dy = 0; /* Cinema Mode: the player can't move */
            if (vitals_game_over() || vitals_incapacitated(world_actor_character(0))) dx = dy = 0; /* dead, knocked out, asleep, paralyzed */
            if (vehicles_drive(dx, dy, seconds)) dx = dy = 0; /* at a vehicle's controls: the keys drive it */
            world_update(dx, dy, seconds);
            schedules_update(seconds * 1000);
            weather_update(seconds * 1000);
            vitals_update(seconds * 1000);
            combat_update(seconds * 1000);
            magic_update(seconds * 1000);
            mechanisms_update(seconds * 1000);
            vehicles_update(seconds * 1000);
            work_update(seconds * 1000);
            traps_update(seconds * 1000);
            eggs_update(seconds * 1000); /* spawn points and the other eggs */
            world_update_bleeding(seconds * 1000);
            events_player_moved();
        }
        if (!title) gumps_update(seconds * 1000); /* the windows themselves go on while the game waits (dragging, their animations) */
        mechanisms_frame(seconds * 1000); /* the sleep fade runs while the world waits */
        if (gumps_save_screen_wanted() && !dolls_title_open()) { save_take_screenshot(pixels); dolls_open_save_screen(); } /* the paperdoll's disk */
        if (!paused) worldMs += (uint32_t)(seconds * 1000); /* the world's own time: still while a box is up (Exult: paused) */
        timing(2);
        if (title) memset(pixels, 0, sizeof(uint32_t) * SCREEN_W * SCREEN_H); /* the world isn't seen behind the title */
        else
        {
            world_draw(pixels, worldMs);
            timing(3);
            weather_draw_clouds(pixels);
            timing(4);
            weather_draw(pixels);
            timing(5);
            world_draw_barks(pixels);
            combat_draw(pixels, worldMs);
            traps_draw(pixels, worldMs);
            timing(6);
        }
        gumps_draw(pixels, title ? now : worldMs);
        if (!title) screen_draw(pixels, now);
        dialog_draw(pixels, now); slider_draw(pixels, now);
        screen_shake_apply(pixels);
        video_draw(pixels); /* a video over everything */
        gumps_draw_pointer(pixels, now); /* the pointer over everything */
        timing(7);
        platform_present(pixels);
        timing(8);
        timing_frame_done();
        { static int frames; if (++frames == 1 || frames == 100) note(frames == 1 ? "First frame shown." : "100 frames shown."); } /* how far it got, if it ever stops */
    }
    platform_audio_close();
    platform_close();
    note("Closed normally.");
    return 0;
}
