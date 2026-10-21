/*
 * The Sandcastle player: runs a game built in Sandcastle RPG Maker on
 * machines the Windows engine doesn't (Linux, Mac, MorphOS, AmigaOS 4,
 * Amiga 68k). It reads the game from the folder "<program>_Data" beside it,
 * or the folder given on the command line.
 */
#include "platform.h"
#include "world.h"
#include "font.h"
#include <stdio.h>
#include <string.h>

static uint32_t pixels[SCREEN_W * SCREEN_H];

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
    const char *shot = NULL, *keys = NULL, *say = NULL;
    for (int i = 1; i < argc; i++)
    {
        if (strcmp(argv[i], "--shot") == 0 && i + 1 < argc) shot = argv[++i];
        else if (strcmp(argv[i], "--keys") == 0 && i + 1 < argc) keys = argv[++i];
        else if (strcmp(argv[i], "--say") == 0 && i + 1 < argc) say = argv[++i]; /* testing: words drawn in the game's fonts */
        else snprintf(folder, sizeof folder, "%s", argv[i]);
    }
#ifdef __amigaos__
    /* An Amiga needs the game's memory free before it starts (128 MB at least; the user's rule: say so, don't crash). */
    long freeMb = platform_free_memory_mb();
    if (freeMb >= 0 && freeMb < GAME_MIN_RAM_MB)
    {
        char text[256];
        snprintf(text, sizeof text, "This game needs %d MB of free memory;\nonly %ld MB is free.\n\nClose other programs, or add memory.", GAME_MIN_RAM_MB, freeMb);
        platform_message("Not enough memory", text);
        return 20;
    }
#endif
    if (!world_load(folder))
    {
        char text[700];
        snprintf(text, sizeof text, "The game couldn't start:\n%s", world_error());
        fprintf(stderr, "%s\n", text);
        platform_message("Game", text);
        return 1;
    }
    if (!platform_open(world_title()[0] ? world_title() : "Game", 0))
    {
        char text[400];
        snprintf(text, sizeof text, "The game couldn't open its window:\n%s", platform_error());
        fprintf(stderr, "%s\n", text);
        platform_message("Game", text);
        return 1;
    }
    int held[4] = { 0, 0, 0, 0 }; /* left, right, up, down: when pressed (0 = not held) */
    int order = 0, running = 1;
    uint32_t last = platform_ticks(), start = last;
    while (running)
    {
        PlatformEvent e;
        while (platform_poll(&e))
        {
            if (e.type == EV_QUIT) running = 0;
            else if (e.type == EV_KEY_DOWN || e.type == EV_KEY_UP)
            {
                int down = e.type == EV_KEY_DOWN;
                switch (e.key)
                {
                    case KEY_LEFT: held[0] = down ? ++order : 0; break;
                    case KEY_RIGHT: held[1] = down ? ++order : 0; break;
                    case KEY_UP: held[2] = down ? ++order : 0; break;
                    case KEY_DOWN: held[3] = down ? ++order : 0; break;
                    case KEY_ESCAPE: if (down) running = 0; break;
                    case KEY_F11: if (down) platform_toggle_fullscreen(); break;
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
        if (keys) /* testing: the script's key for this moment, then the shot */
        {
            double t = (now - start) / 1000.0 - 0.5, at = 0;
            const char *k = keys;
            dx = dy = 0;
            int done = 1;
            while (*k && t >= 0)
            {
                char c = *k; double secs = 0;
                sscanf(k + 2, "%lf", &secs);
                if (t < at + secs) { dx = c == 'L' ? -1 : c == 'R' ? 1 : 0; dy = c == 'U' ? -1 : c == 'D' ? 1 : 0; done = 0; break; }
                at += secs;
                k = strchr(k, ',');
                if (!k) break;
                k++;
            }
            if (t < 0) done = 0;
            if (done && shot) { world_draw(pixels, now); save_bmp(shot); break; }
        }
        else if (shot && now - start > 1000)
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
        world_update(dx, dy, seconds);
        world_draw(pixels, now);
        platform_present(pixels);
    }
    platform_close();
    return 0;
}
