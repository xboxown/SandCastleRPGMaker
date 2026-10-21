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
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

double sc_strtod(const char *text, char **end);

static uint32_t pixels[SCREEN_W * SCREEN_H];

/* Game.log beside the game: each step of starting, so a game that stops can say where. */
static void note(const char *step)
{
#ifdef __amigaos__
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
        else if (*what == 'I') gumps_open_paperdoll(world_actor_character(0)); /* I: the inventory */
        else if (*what == 'D' || *what == 'V' || *what == 'R') /* D160;100 press, V moves, R releases there */
        {
            const char *semi = strchr(what, ';');
            if (semi) { int x = atoi(what + 1), y = atoi(semi + 1); if (*what == 'D') gumps_mouse_down(x, y); else if (*what == 'V') gumps_mouse_move(x, y); else gumps_mouse_up(x, y); }
        }
        else if (*what == 'X') gumps_close_top(); /* X: Escape on a window */
        else if (*what == 'A') combat_attack(0, world_actor_by_character(atoi(what + 1))); /* A7: attack the character with id 7 */
        else if (*what == 'M') { const char *comma = strchr(what, ';'); if (comma) world_teleport(0, atoi(what + 1), atoi(comma + 1)); } /* M7;10: the player to (7, 10) */
        else
        {
            enum PlatformKey key = *what == 'E' ? KEY_ENTER : *what == 'S' ? KEY_SPACE : *what >= '1' && *what <= '9' ? (enum PlatformKey)(KEY_1 + (*what - '1'))
                : *what == 'u' ? KEY_UP : *what == 'd' ? KEY_DOWN : KEY_OTHER;
            if (dialog_showing()) dialog_key(key);
        }
    }
    *done = t;
}

#ifdef __amigaos__
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

int main(int argc, char **argv)
{
    char folder[1024] = "Game_Data";
    const char *shot = NULL, *keys = NULL, *say = NULL, *presses = NULL;
    int noSound = 0;
    double shotAt = -1, pressedTill = -1;
    for (int i = 1; i < argc; i++)
    {
        if (strcmp(argv[i], "--shot") == 0 && i + 1 < argc) shot = argv[++i];
        else if (strcmp(argv[i], "--keys") == 0 && i + 1 < argc) keys = argv[++i];
        else if (strcmp(argv[i], "--say") == 0 && i + 1 < argc) say = argv[++i]; /* testing: words drawn in the game's fonts */
        else if (strcmp(argv[i], "--press") == 0 && i + 1 < argc) presses = argv[++i];
        else if (strcmp(argv[i], "--shot-at") == 0 && i + 1 < argc) shotAt = sc_strtod(argv[++i], NULL);
        else if (!strcmp(argv[i], "nosound") || !strcmp(argv[i], "NOSOUND") || !strcmp(argv[i], "--nosound")) noSound = 1;
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
#ifdef __amigaos__
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
    if (!platform_open(world_title()[0] ? world_title() : "Game", 0))
    {
        char text[400];
        snprintf(text, sizeof text, "The game couldn't open its window:\n%s", platform_error());
        fprintf(stderr, "%s\n", text);
        note(text);
        platform_message("Game", text);
        return 1;
    }
    note("Window open. Starting.");
    { FILE *quiet = fopen("nosound", "rb"); if (quiet) { fclose(quiet); noSound = 1; } } /* a file called nosound beside the game: no sound */
    { FILE *p = fopen("paula", "rb"); if (p) { fclose(p); platform_audio_route(1); note("Sound through Paula (paula)."); } } /* a file called paula: audio.device, never AHI */
    { FILE *p = fopen("ahi", "rb"); if (p) { fclose(p); platform_audio_route(2); note("Sound through AHI only (ahi)."); } }
    note(noSound ? "Sound off (nosound)." : "Starting the sound...");
    sound_load(folder, !noSound);
    note(sound_status());
    events_load();
    note("Events ready.");
    vitals_load();
    combat_load();
    schedules_load();
    schedules_start();
    note("Characters ready.");
    weather_load();
    events_player_moved(); /* where the player starts: arriving doesn't set its event off */
    note("Running.");
    int held[4] = { 0, 0, 0, 0 }; /* left, right, up, down: when pressed (0 = not held) */
    int order = 0, running = 1;
    uint32_t last = platform_ticks(), start = last, worldMs = 0;
    while (running)
    {
        PlatformEvent e;
        while (platform_poll(&e))
        {
            if (e.type == EV_QUIT) running = 0;
            else if (e.type == EV_MOUSE_MOVE) { dialog_mouse_move(e.mouseX, e.mouseY); if (!dialog_showing()) gumps_mouse_move(e.mouseX, e.mouseY); }
            else if (e.type == EV_MOUSE_DOWN && e.button == 1)
            {
                if (dialog_showing()) dialog_click(e.mouseX, e.mouseY);
                else if (!vitals_game_over()) gumps_mouse_down(e.mouseX, e.mouseY); /* identify, use, drag (GameTestWindow.Interaction) */
            }
            else if (e.type == EV_MOUSE_UP && e.button == 1 && !dialog_showing()) gumps_mouse_up(e.mouseX, e.mouseY);
            else if (e.type == EV_KEY_DOWN || e.type == EV_KEY_UP)
            {
                int down = e.type == EV_KEY_DOWN;
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
                    case KEY_ESCAPE: if (down && !gumps_close_top()) running = 0; break; /* Escape closes the top window first */
                    case KEY_I: if (down && !events_cinema() && !events_keys_forbidden()) gumps_open_paperdoll(world_actor_character(0)); break; /* Exult: I for the inventory */
                    case KEY_F11: if (down) platform_toggle_fullscreen(); break;
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
        uint32_t now = platform_ticks();
        if (presses) scripted_presses(presses, (now - start) / 1000.0, &pressedTill);
        if (shotAt >= 0 && (now - start) / 1000.0 >= shotAt)
        {
            world_draw(pixels, now);
            weather_draw_clouds(pixels);
            lighting_draw(pixels);
            weather_draw(pixels);
            world_draw_barks(pixels);
            combat_draw(pixels, worldMs);
            gumps_draw(pixels, worldMs);
            screen_draw(pixels, now);
            dialog_draw(pixels, now);
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
            if (done && shot && shotAt < 0) { world_draw(pixels, now); dialog_draw(pixels, now); save_bmp(shot); break; }
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
        int paused = dialog_showing(); /* a box up: the game waits, as in an Exult conversation */
        events_update(seconds * 1000, paused);
        if (events_quit_requested()) running = 0; /* Quit Game */
        if (!dialog_showing())
        {
            if (events_cinema()) dx = dy = 0; /* Cinema Mode: the player can't move */
            if (vitals_game_over() || vitals_incapacitated(world_actor_character(0))) dx = dy = 0; /* dead, knocked out, asleep, paralyzed */
            world_update(dx, dy, seconds);
            schedules_update(seconds * 1000);
            weather_update(seconds * 1000);
            vitals_update(seconds * 1000);
            combat_update(seconds * 1000);
            gumps_update(seconds * 1000);
            events_player_moved();
        }
        if (!paused) worldMs += (uint32_t)(seconds * 1000); /* the world's own time: still while a box is up (Exult: paused) */
        world_draw(pixels, worldMs);
        weather_draw_clouds(pixels);
        lighting_draw(pixels);
        weather_draw(pixels);
        world_draw_barks(pixels);
        combat_draw(pixels, worldMs);
        gumps_draw(pixels, worldMs);
        screen_draw(pixels, now);
        dialog_draw(pixels, now);
        screen_shake_apply(pixels);
        platform_present(pixels);
        { static int frames; if (++frames == 1 || frames == 100) note(frames == 1 ? "First frame shown." : "100 frames shown."); } /* how far it got, if it ever stops */
    }
    platform_audio_close();
    platform_close();
    note("Closed normally.");
    return 0;
}
