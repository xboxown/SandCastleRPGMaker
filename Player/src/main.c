/*
 * The Sandcastle player: runs a game built in Sandcastle RPG Maker on
 * machines the Windows engine doesn't (Linux, Mac, MorphOS, AmigaOS 4,
 * Amiga 68k). It reads the game from the folder "<program>_Data" beside it,
 * or the folder given on the command line.
 */
#include "platform.h"
#include "crt.h"
#include "world.h"
#include "u6.h"
#include "u8.h"
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
#ifdef PS2_DIAG_SHOTS
#include <sys/stat.h>
#endif

double sc_strtod(const char *text, char **end);

static uint32_t *pixels; /* FRAME_W x FRAME_H, once the display is open */
void main_take_screenshot(void) { save_take_screenshot(pixels); }
/* A window over the last frame, shown now (the save / load progress, between its steps). */
void main_show_now(void)
{
    uint32_t now = platform_ticks();
    gumps_draw(pixels, now);
    gumps_draw_pointer(pixels, now);
    platform_present(pixels);
}

/* Game.log beside the game: each step of starting, so a game that stops can say where. */
static void note(const char *step)
{
#ifdef PLATFORM_PS2
    extern long ps2_memory_used_kb(void);
    printf("Game: %s  [memory in use: %ld KB]\n", step, ps2_memory_used_kb()); /* the disc can't be written: the log goes to the console (PCSX2's log) */
    return;
#endif
#if defined(__amigaos__) && !defined(__MORPHOS__) && !defined(__amigaos4__)
    static int fresh = 0; /* amiga_start.c began Game.log (the 68k's; AmigaOS 4 starts it here) */
#else
    static int fresh = 1;
#endif
#ifdef PLATFORM_PS3
    void ps3_netlog(const char *line);
    ps3_netlog(step); /* testing: the line sent at once over the network (platform_ps3.c), if asked */
    FILE *f = fopen("/dev_hdd0/tmp/sandcastle_game.log", fresh ? "w" : "a"); /* the PS3 has no current folder: fetched over FTP */
#else
    FILE *f = fopen("Game.log", fresh ? "w" : "a");
#endif
    fresh = 0;
    if (!f) return;
    fprintf(f, "%s\n", step);
    fclose(f);
}

void game_log(const char *line) { note(line); }

/* Testing: a key pressed at a moment ("--press 2:E,3.5:1": Enter at 2 s, the 1 key at 3.5 s; T7 = double-click the character with id 7). */
static int testDx, testDy; /* testing: a direction held by --press "J..." */
/* testing: key events put in front of the platform's (--press "!Up" a key down, "!Up^" up again, "!Ctrl+Q") */
static PlatformEvent injected[16]; static int injectedCount;
static int next_event(PlatformEvent *e)
{
    if (injectedCount) { *e = injected[0]; memmove(injected, injected + 1, sizeof injected[0] * (size_t)--injectedCount); return 1; }
    return platform_poll(e);
}
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
        else if (*what == 'y') { SaveInfo list[8]; if (save_list(list, 8) > 0) { dolls_request_load(list[0].folder, list[0].name); note("DBG loading the newest save"); } } /* y: the newest save loaded (testing) */
        else if (*what == '%') { for (int a = 1; a < world_actor_count(); a++) { int x, y, f; double px, py; world_actor_cell(a, &x, &y, &f); world_actor_pixel(a, &px, &py); char line[128]; snprintf(line, sizeof line, "DBG actor %d (character %d): pixel %.1f,%.1f cell %d,%d floor %d", a, world_actor_character(a), px, py, x, y, f); note(line); } } /* %: every character's exact spot (testing) */
        else if (*what == 'q') { int x = atoi(what + 1), y = 0, id = -1; const char *comma = strchr(what, ' '); comma = strchr(what, '/'); const char *semi = strchr(what, ';'); if (comma) y = atoi(comma + 1); if (semi) id = atoi(semi + 1); int uid = world_thing_at_cell(4, id, x, y, 0); char line[96]; snprintf(line, sizeof line, "DBG qty: floor %d/%d %d, carried %d", x, y, uid ? world_thing_quantity(uid) : 0, items_count_any(world_actor_character(0), 4, id)); note(line); } /* q11/7;80: the stack of object 80 on cell 11,7, and how many of it the player carries */
        else if (*what == 'B') books_read(atoi(what + 1)); /* B1: read the book with id 1 */
        else if (*what == 'Z') gumps_next_stats(); /* Z: the stats paperdoll */
#ifdef SAVE_SELFTEST
        else if (*what == 'Y') { void save_selftest(const char *, const char *); save_selftest("save_old.json", "save_new.json"); note("DBG save self-test written"); } /* Y: the save's text both ways (testing) */
#endif
        else if (*what == 'Q') { for (int a = 0; a < world_actor_count(); a++) { int x, y, f; world_actor_cell(a, &x, &y, &f); int dbgHp = 0; vitals_hp(world_actor_character(a), &dbgHp); char line[160]; snprintf(line, sizeof line, "DBG actor %d lift %d walknow %d walking %d char %d at (%d,%d) floor %d gone %d food %d hp %d seat %d fight %d", a, world_actor_lift(a), world_actor_walking_now(a), world_walking(a), world_actor_character(a), x, y, f, world_actor_gone(a), vitals_food(world_actor_character(a)), dbgHp, world_seated_on(a), combat_busy(a)); note(line); } { char line[64]; snprintf(line, sizeof line, "DBG box %d cinema %d", dialog_showing(), events_cinema()); note(line); } } /* Q: where everyone is (testing) */
        else if (*what == 'J') { char d = what[1]; testDx = d == 'e' ? 1 : d == 'w' ? -1 : 0; testDy = d == 's' ? 1 : d == 'n' ? -1 : 0; } /* Jn/Je/Js/Jw: hold a direction, Jx: let go */
        else if (*what == 'U') { int uid = world_thing_by_instance(atoi(what + 1)); if (uid) use_thing(uid); } /* U38: double-click the placed thing with instance id 38 */
        else if (*what == 'I') gumps_open_paperdoll(world_actor_character(0)); /* I: the inventory */
        else if (*what == 'O') gumps_open_paperdoll(atoi(what + 1)); /* O2: the paperdoll of the character with id 2 */
        else if (*what == 'D' || *what == 'V' || *what == 'R') /* D160;100 press, V moves, R releases there */
        {
            const char *semi = strchr(what, ';');
            if (semi) { int x = atoi(what + 1), y = atoi(semi + 1); if (*what == 'D') { if (!video_input(1)) { if (dialog_showing()) dialog_click(x, y); else gumps_mouse_down(x, y); } } else if (*what == 'V') { platform_warp_mouse(x, y); gumps_mouse_move(x, y); } else gumps_mouse_up(x, y); }
        }
        else if (*what == 'X') gumps_close_top(); /* X: Escape on a window */
        else if (*what == '#') /* #L, #Ctrl+S, #Up: a key by its name, as Ultima 6 reads it (u6.c) */
        {
            PlatformEvent ke; memset(&ke, 0, sizeof ke);
            ke.type = EV_KEY_DOWN;
            const char *name = what + 1;
            if (!strncmp(name, "Ctrl+", 5)) { ke.keyMods |= KEYMOD_CTRL; name += 5; }
            snprintf(ke.keyName, sizeof ke.keyName, "%.*s", (int)strcspn(name, ","), name);
            if (dialog_showing()) u6_talk_key(&ke);
            else { u6_key(&ke); ke.type = EV_KEY_UP; u6_key(&ke); }
        }
        else if (*what == '!' && injectedCount < 16) /* !Up, !Up^, !Ctrl+Q: a key event through the game's own loop */
        {
            PlatformEvent ke; memset(&ke, 0, sizeof ke);
            const char *name = what + 1;
            if (!strncmp(name, "Ctrl+", 5)) { ke.keyMods |= KEYMOD_CTRL; name += 5; }
            if (!strncmp(name, "Shift+", 6)) { ke.keyMods |= KEYMOD_SHIFT; name += 6; }
            size_t n = strcspn(name, ",^");
            snprintf(ke.keyName, sizeof ke.keyName, "%.*s", (int)n, name);
            ke.type = name[n] == '^' ? EV_KEY_UP : EV_KEY_DOWN;
            ke.key = KEY_OTHER;
            injected[injectedCount++] = ke;
        }
        else if (*what == '~') { for (const char *c = what + 1; *c && *c != ','; c++) u6_talk_text(*c); } /* ~job: letters typed */
        else if (*what == '@') { const char *semi = strchr(what, ';'); if (semi && !u6_mouse_down(atoi(what + 1), atoi(semi + 1))) { gumps_mouse_down(atoi(what + 1), atoi(semi + 1)); gumps_mouse_up(atoi(what + 1), atoi(semi + 1)); } } /* @x;y: a click there (Ultima 6's HUD first) */
        else if (*what == 'k') { void dolls_vk_test(void); dolls_vk_test(); } /* k: the on-screen keyboard for slot 1 (testing) */
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
#ifdef __amigaos4__
/* AmigaOS 4 reads the stack a program needs from this text in it (newlib), whatever the icon or shell gives it. */
static const char __attribute__((used)) os4StackCookie[] = "$STACK: 262144";
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
    /* testing with SANDCASTLE_CRT: the picture as the screen gets it - the CRT effect's, twice the size (crt.c) */
    const uint32_t *shown = pixels;
    int W = FRAME_W, H = FRAME_H;
    uint32_t *doubled = NULL;
    if (getenv("SANDCASTLE_CRT") && (doubled = (uint32_t *)malloc(sizeof(uint32_t) * 4 * FRAME_PIXELS)) != NULL)
    { crt_double(pixels, FRAME_W, FRAME_H, doubled, 2 * FRAME_W, 0, FRAME_H); shown = doubled; W = 2 * FRAME_W; H = 2 * FRAME_H; }
    unsigned char h[54] = { 'B', 'M' };
    int pad = (4 - W * 3 % 4) % 4; /* each row a multiple of 4 bytes (Fill the screen's width can be any) */
    int size = 54 + (W * 3 + pad) * H;
    h[2] = size & 255; h[3] = (size >> 8) & 255; h[4] = (size >> 16) & 255; h[5] = (size >> 24) & 255;
    h[10] = 54; h[14] = 40; h[18] = W & 255; h[19] = W >> 8; h[22] = H & 255; h[23] = H >> 8; h[26] = 1; h[28] = 24;
    fwrite(h, 1, 54, f);
    for (int y = H - 1; y >= 0; y--)
        for (int x = 0; x < W; x++)
        {
            uint32_t c = shown[y * W + x];
            unsigned char px[3] = { c & 255, (c >> 8) & 255, (c >> 16) & 255 };
            fwrite(px, 1, 3, f);
            if (x == W - 1 && pad) { static const unsigned char zero[3]; fwrite(zero, 1, (size_t)pad, f); }
        }
    fclose(f);
    free(doubled);
}

/* The timing diary: every 10 seconds Game.log says the frame rate and where a frame's time went (so a slow
   machine can say why). Parts: 0 keys and sounds, 1 events, 2 the world going on, 3 the world drawn,
   4 clouds and light, 5 weather, 6 barks/fights/traps, 7 windows/boxes/pointer, 8 put on the screen. */
#define TIMING_PARTS 9
static double timingMs[TIMING_PARTS], timingMark, timingFrom;
static int timingFrames;
#ifdef SANDCASTLE_NETLOG
extern volatile int ps3dbg_stage, ps3dbg_frame; /* testing: the PS3's watchdog says where a frame stopped (platform_ps3.c) */
#endif
static void timing(int part)
{
#ifdef SANDCASTLE_NETLOG
    ps3dbg_stage = part;
#endif
    double t = platform_ms_precise(); timingMs[part] += t - timingMark; timingMark = t;
}
static void timing_frame_done(void)
{
#ifdef SANDCASTLE_NETLOG
    ps3dbg_frame++;
#endif
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
        /* not the exact spot they had in the world left (a save's PixelXRuntime...): built there, they stayed at their old place */
        cJSON_DeleteItemFromObject(pc, "PixelXRuntime"); cJSON_DeleteItemFromObject(pc, "PixelYRuntime"); cJSON_DeleteItemFromObject(pc, "LiftRuntime");
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
    if (u6_on()) { u6_show_title(); return 1; } /* Ultima 6's own menu, not a paperdoll (U6GameWindow.Title ShowTitleIfAny) */
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
    u6_load(); /* Ultima 6: its HUD read again from the game just built (the old one is gone) */
    u8_load(); /* Ultima 8: its keys (u8.c) */
    events_load();
    if (runtime) { events_restore(runtime); script_restore(runtime); books_restore(runtime); u6_restore(runtime); u8_restore(runtime); }
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
#ifdef PLATFORM_PS2
    ps2_boot(argc, argv, folder, sizeof folder); /* the PS2's drivers; the data on the disc (platform_ps2.c) */
    argc = 1;
#endif
#ifdef SANDCASTLE_INSTALLED
    platform_installed_data(argc > 0 ? argv[0] : "", folder, sizeof folder); /* installed (Ubuntu's .deb, the Mac's .app): the data where it was installed */
#endif
#ifdef PLATFORM_PS3
    void ps3_boot(int argc, char **argv, char *folder, int folderSize);
    ps3_boot(argc, argv, folder, sizeof folder); /* the game's data beside the EBOOT on the hard drive (platform_ps3.c) */
    argc = 1;
#endif
    const char *shot = NULL, *keys = NULL, *say = NULL, *presses = NULL;
#ifdef PS2_DIAG_SHOTS
    presses = "10:D170;117,10.3:R170;117,25:Jn,27:Jx,28:Je,31:Jx,33:I,34:E,34.5:E,35:E,36:F,38:k,40:LHello,42:r,42.5:d"; /* testing: New Game, a walk, the inventory, the Save screen, a slot, a name typed */
#endif
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
    u6_load(); /* an Ultima 6 project: its HUD, the world in its Game View (u6.c) */
    u8_load(); /* an Ultima 8 project: its keys, its own rules (u8.c) */
    note("Loaded. Opening the window...");
    /* System Settings > Game Window: in a window, or full screen (DisplaySettings.Mode 1) */
    cJSON *display = cJSON_GetObjectItem(cJSON_GetObjectItem(cJSON_GetObjectItem((cJSON *)world_json(), "Project"), "Settings"), "Display");
    cJSON *mode = cJSON_GetObjectItem(display, "Mode");
    int fullScreen = cJSON_IsNumber(mode) && mode->valueint == 1 && !forceWindow;
    { cJSON *windowScale = cJSON_GetObjectItem(display, "WindowScale"); platform_want_window_scale(cJSON_IsNumber(windowScale) ? windowScale->valueint : 2); }
    platform_want_crt(cJSON_IsTrue(cJSON_GetObjectItem(display, "CrtEffect")) || getenv("SANDCASTLE_CRT") != NULL); /* System Settings > Game Window > CRT effect (SANDCASTLE_CRT: testing) */
    platform_want_fill((cJSON_IsTrue(cJSON_GetObjectItem(display, "FillScreen")) || getenv("SANDCASTLE_FILL") != NULL) && !u6_on()); /* System Settings > Game Window > Fill the screen (Ultima 6: its HUD is 320 x 200; SANDCASTLE_FILL: testing) */
    if (!platform_open(world_title()[0] ? world_title() : "Game", fullScreen))
    {
        char text[400];
        snprintf(text, sizeof text, "The game couldn't open its window:\n%s", platform_error());
        fprintf(stderr, "%s\n", text);
        note(text);
        platform_message("Game", text);
        return 1;
    }
    pixels = (uint32_t *)calloc((size_t)FRAME_PIXELS, sizeof(uint32_t));
    if (!pixels) { platform_message("Game", "There isn't enough memory for the picture."); return 1; }
    world_screen_opened(); /* the screen's size known now (Fill the screen may have widened it): the camera's view the same */
    { char line[96]; snprintf(line, sizeof line, "Screen: %d x %d game pixels, drawn %d x %d.", SCREEN_W, SCREEN_H, FRAME_W, FRAME_H); note(line); }
    note("Window open. Starting.");
    save_init(folder); /* the saves: beside the game */
#ifdef SANDCASTLE_INSTALLED
    { char saves[1100]; if (platform_installed_saves(saves, sizeof saves)) save_use_folder(saves); } /* installed (Ubuntu, Mac): in the player's own folder */
#endif
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
    if (!noTitle && title_with_intro() && (dolls_title_open() || u6_title_on())) note("Title Screen."); /* System Settings > Title Screen: before play, the game waits */
    int held[4] = { 0, 0, 0, 0 }; /* left, right, up, down: when pressed (0 = not held) */
    int order = 0, running = 1;
    uint32_t last = platform_ticks(), start = last, worldMs = 0;
    timingMark = timingFrom = platform_ms_precise();
#ifdef SANDCASTLE_NETLOG
#define DBG_STEP(n) (ps3dbg_stage = (n)) /* testing: the PS3's watchdog says which step of a frame's start it's in */
#else
#define DBG_STEP(n) ((void)0)
#endif
    while (running)
    {
        PlatformEvent e;
        DBG_STEP(9);
        while ((memset(&e, 0, sizeof e), next_event(&e)))
        {
            if (e.keyRepeat && !u6_on()) continue; /* a held key's repeats: only Ultima 6's cursor uses them */
            if (u8_on() && u8_key_ask_event(&e)) continue; /* Ultima 8's Ask For Key: the next key is its answer */
            if (u8_screen_open() && e.type == EV_MOUSE_DOWN && e.button == 1 && u8_screen_click(e.mouseX, e.mouseY)) continue; /* Ultima 8: its screens take the clicks */
            if (u8_screen_open() && e.type == EV_KEY_DOWN && e.key != KEY_F11) { u8_screen_key(&e); continue; } /* and the keys */
            if (u8_on()) u8_translate(&e, dialog_showing() || slider_open_now() || prompt_open() || dolls_modal_open() || dolls_save_screen_open() || video_playing() || dolls_typing(), dolls_typing() || dolls_save_screen_open()); /* the key as the action it is now (U8Keys) */
            if (e.type != EV_QUIT && dolls_vk_open()) /* the on-screen keyboard up: everything is its own */
            {
                if (e.type == EV_MOUSE_MOVE) { gumps_pointer_at(e.mouseX, e.mouseY); dolls_vk_event(0, 0, 0, 0, e.mouseX, e.mouseY); continue; }
                int vt = e.type == EV_MOUSE_DOWN ? 1 : e.type == EV_MOUSE_UP ? 2 : e.type == EV_KEY_DOWN ? 3 : e.type == EV_TEXT ? 4 : -1;
                if (vt < 0 || dolls_vk_event(vt, e.key, e.ch, e.button, e.mouseX, e.mouseY)) continue;
            }
            if (u8_on() && (e.type == EV_MOUSE_DOWN || e.type == EV_MOUSE_UP || e.type == EV_MOUSE_MOVE) && u8_mouse(&e)) continue; /* Ultima 8: the right button walks, both jump (u8.c) */
            if (e.type == EV_QUIT) running = 0;
            else if (e.type == EV_MOUSE_MOVE) { gumps_pointer_at(e.mouseX, e.mouseY); /* the pointer follows the mouse whatever is up: a box, a slider */ if (slider_open_now()) { slider_mouse_move(e.mouseX, e.mouseY); continue; } dialog_mouse_move(e.mouseX, e.mouseY); if (!dialog_showing()) gumps_mouse_move(e.mouseX, e.mouseY); }
            else if (e.type == EV_MOUSE_DOWN && e.button == 1)
            {
                if (video_input(1)) continue; /* a click skips a video */
                if (slider_mouse_down(e.mouseX, e.mouseY)) continue; /* the slider up: its clicks */
                if (dialog_showing()) { if (!u6_talk_click(e.mouseX, e.mouseY)) dialog_click(e.mouseX, e.mouseY); }
                else if (!vitals_game_over() || dolls_window_count()) { if (dolls_window_count() || gumps_open() || !u6_mouse_down(e.mouseX, e.mouseY)) gumps_mouse_down(e.mouseX, e.mouseY); } /* Ultima 6's HUD first (u6.c); identify, use, drag (GameTestWindow.Interaction); dead: only the Save/Load screen */
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
            else if (e.type == EV_TEXT) { if (!u6_talk_text(e.ch) && !prompt_key(0, e.ch)) dolls_key(0, e.ch); } /* Ultima 6's typed answer, a letter for a question's box, or a paperdoll's text box */
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
                    if (u6_talk_key(&e)) continue; /* Ultima 6: the talk in the text area (u6.c) */
                    dialog_key(e.key); /* while a box is up the keys are its own */
                    continue;
                }
                /* Ultima 6's keys (u6.c): every key not Ultima 6's is swallowed - Escape and F11 still reach the engine below */
                if (u6_on() && !dolls_title_open() && !events_cinema() && !vitals_game_over() && u6_key(&e)) continue;
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
                    default: if (down && u8_on()) u8_world_key(u8_action_id()); break; /* Ultima 8's own keys (u8.c) */
                }
            }
        }
        DBG_STEP(10);
        if (u8_on() && u8_take_drop_arrows()) { memset(held, 0, sizeof held); u8_release_all(); } /* Ultima 8: knocked down - the keys held must be pressed again */
        /* The most recently pressed direction still held wins (as the Windows engine). */
        int best = 0, dx = 0, dy = 0;
        for (int i = 0; i < 4; i++) if (held[i] > best) best = held[i];
        if (best)
        {
            if (held[0] == best) dx = -1; else if (held[1] == best) dx = 1; else if (held[2] == best) dy = -1; else dy = 1;
        }
        else { dx = testDx; dy = testDy; }
        if (u6_hud() && !best && !testDx && !testDy) u6_walk(&dx, &dy); /* Ultima 6's walking keys (diagonals too) */
        if (u8_on() && (best || (!testDx && !testDy))) u8_walk_input(held[0] != 0, held[1] != 0, held[2] != 0, held[3] != 0, &dx, &dy); /* Ultima 8: the screen's way, 8 directions, the right button too */
        uint32_t now = platform_ticks();
        if (presses) scripted_presses(presses, (now - start) / 1000.0, &pressedTill);
        if (shotAt >= 0 && (now - start) / 1000.0 >= shotAt)
        {
            if (dolls_title_open()) memset(pixels, 0, sizeof(uint32_t) * FRAME_PIXELS);
            else
            {
                world_draw(pixels, now);
                weather_draw_clouds(pixels);
                weather_draw(pixels);
                world_draw_barks(pixels);
            }
            combat_draw(pixels, worldMs);
            gumps_draw_thrown(pixels); /* Ultima 8: something thrown in the air */
            traps_draw(pixels, worldMs);
            u6_draw(pixels, now);
            gumps_draw(pixels, worldMs);
            screen_draw(pixels, now);
            dialog_draw(pixels, now); slider_draw(pixels, now);
            u6_draw_title(pixels, now); u6_draw_scene(pixels, now);
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
        DBG_STEP(11);
        sound_update(seconds * 1000);
        DBG_STEP(12);
        screen_update(seconds * 1000);
        int title = dolls_title_open() || introPending || u6_title_on(); /* the Title Screen (or its intro video, or Ultima 6's menu): nothing in the world goes on behind it */
        DBG_STEP(13);
        video_update(seconds * 1000);
        timing(0);
        int paused = dialog_showing() || title || mechanisms_pausing() || slider_open_now() || video_playing() || books_open() || prompt_open() || gumps_pausing() || vitals_game_over() || u6_scene_on(); /* a box up: the game waits, as in an Exult conversation (and while the party sleeps); an Ultima 6 scene */
        if (u8_on()) paused = title || vitals_game_over() || gumps_choosing() || mechanisms_pausing() || slider_open_now() || video_playing() || prompt_open(); /* Ultima 8 (U8GameWindow.Pause): no window, box, book or spellbook pauses - only choosing a target, a map looked at, sleeping, a video, a number being typed */
        if (!title) events_update(seconds * 1000, paused);
        if (u6_scene_on()) events_tick_waits(seconds * 1000); /* Wait counts in a scene (the world waits) */
        else gumps_update(seconds * 1000);
        timing(1);
        if (events_quit_requested()) running = 0; /* Quit Game */
        if (loadRequested) { loadRequested = 0; if (load_game(loadFolder, loadName) < 0) running = 0; }
        { int w, hs, wx, wy, wf; if (events_world_trip(&w, &hs, &wx, &wy, &wf) && (u8_control_back(), travel(w, hs, wx, wy, wf) < 0)) running = 0; } /* Change Map to another world */
        if (events_restart_requested()) { int title = events_restart_requested() == 2; events_clear_restart(); if (new_game(title) < 0) running = 0; } /* New Game, Return To Title */
        /* Exult's "gumps pause the game" (GameTestWindow.UpdateGamePause): as it pauses, a direction held is let go and the
           avatar stands where it is - the key has to be pressed again (so an event's Walk To isn't taken for the player taking over). */
        static int wasPaused, wasCinema;
        int cinemaNow = events_cinema(); /* Cinema Mode starting lets go of the keys too (GameTestWindow.Cinema) */
        if ((paused && !wasPaused) || (cinemaNow && !wasCinema)) { for (int i = 0; i < 4; i++) held[i] = 0; testDx = testDy = 0; world_keys_let_go(); }
        wasPaused = paused; wasCinema = cinemaNow;
        if (paused) dx = dy = 0;
        int frozen = !paused && u6_turn_frozen(seconds * 1000); /* Ultima 6 combat: the world waits for the avatar's turn */
        if (frozen) dx = dy = 0;
        if (!paused && !frozen)
        {
            if (events_cinema()) dx = dy = 0; /* Cinema Mode: the player can't move */
            if (vitals_game_over() || vitals_incapacitated(world_actor_character(0))) dx = dy = 0; /* dead, knocked out, asleep, paralyzed */
            if (vehicles_drive(dx, dy, seconds)) dx = dy = 0; /* at a vehicle's controls: the keys drive it */
            if (u6_solo_step(dx, dy)) dx = dy = 0; /* Ultima 6 solo mode: the member moves, the avatar stands */
            world_update(dx, dy, seconds);
            schedules_update(seconds * 1000);
            weather_update(seconds * 1000);
            vitals_update(seconds * 1000);
            u8_update(seconds * 1000); /* Ultima 8: platforms, a flip, a recharge, view events (u8.c) */
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
        if (!paused && !frozen) worldMs += (uint32_t)(seconds * 1000); /* the world's own time: still while a box is up (Exult: paused) */
        timing(2);
        if (title) memset(pixels, 0, sizeof(uint32_t) * FRAME_PIXELS); /* the world isn't seen behind the title */
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
            gumps_draw_thrown(pixels); /* Ultima 8: something thrown in the air */
            traps_draw(pixels, worldMs);
            u6_draw(pixels, now); /* Ultima 6: the HUD round the Game View (u6.c) */
            timing(6);
        }
        gumps_draw(pixels, now); /* the windows on real time, as on Windows (GameTestWindow.Pause: "the interface keeps animating while the game is paused") - on the world's time a paperdoll's eyes stopped whenever a window or message paused the game */
        if (!title) screen_draw(pixels, now);
        dialog_draw(pixels, now); slider_draw(pixels, now);
        u6_draw_title(pixels, now); /* Ultima 6's title menu */
        u6_draw_scene(pixels, now); /* an Ultima 6 scene over everything (the title menu too) */
        screen_shake_apply(pixels);
        video_draw(pixels); /* a video over everything */
        dolls_vk_draw(pixels); /* the on-screen keyboard, over every window */
        u8_draw_screen(pixels); /* Ultima 8: a reading screen, a keypad, a computer */
        u8_flip_frame(pixels); /* Ultima 8: Flip Screen */
        gumps_draw_pointer(pixels, now); /* the pointer over everything */
        timing(7);
        platform_present(pixels);
        timing(8);
        timing_frame_done();
        { static int frames; if (++frames == 1 || frames == 100) note(frames == 1 ? "First frame shown." : "100 frames shown."); /* how far it got, if it ever stops */
#ifdef PS2_DIAG_SHOTS
          /* testing in PCSX2: frames as pictures on the memory card in slot 1 (read off its image with a memory card tool) */
          if (frames % 150 == 0 && frames <= 2400) { char name[40]; mkdir("mc0:/SANDDIAG", 0777); snprintf(name, sizeof name, "mc0:/SANDDIAG/F%04d.BMP", frames); save_bmp(name); note(name); }
#endif
        }
    }
    platform_audio_close();
    platform_close();
    note("Closed normally.");
    return 0;
}
