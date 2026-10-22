/*
 * The screen commands, as the Windows engine does them
 * (GameTestWindow.ScriptGame, PlayRealShakeEffect): Fade Screen (to a
 * colour or back, over some seconds), Flash Screen, pictures over the game
 * (Show / Erase Picture, numbered 1-50, animated), Shake Screen; Move
 * Camera and Camera Follows are the world's. Fades and flashes run whether
 * the game is paused or not, as the engine's screen tweens do.
 */
#include "screen.h"
#include "pictures.h"
#include "platform.h"
#include <math.h>
#include <stdlib.h>
#include <string.h>

static uint32_t fadeColor; static double fadeOpacity, fadeFrom, fadeTo, fadeTotal = -1, fadeMs;
typedef struct Flash { uint32_t color; double total, ms; } Flash;
static Flash flashes[8]; static int flashCount;
typedef struct Pic { const Sprite *sprite; double x, y, opacity; int shown; uint32_t since; } Pic;
static Pic pics[51];
static double shakeLeft, shakeMax; static int shakeX, shakeY;
static unsigned rng = 4242u;
static double rnd01(void) { rng = rng * 1103515245u + 12345u; return ((rng >> 8) & 0xFFFF) / 65536.0; }

uint32_t screen_parse_colour(const char *text, uint32_t fallback)
{
    static const struct { const char *name; uint32_t rgb; } NAMES[] = {
        { "Black", 0x000000 }, { "White", 0xFFFFFF }, { "Red", 0xFF0000 }, { "Green", 0x008000 }, { "Blue", 0x0000FF }, { "Yellow", 0xFFFF00 },
        { "Gray", 0x808080 }, { "Grey", 0x808080 }, { "Orange", 0xFFA500 }, { "Purple", 0x800080 }, { "Brown", 0xA52A2A } };
    if (!text) return fallback;
    while (*text == ' ') text++;
    for (size_t i = 0; i < sizeof NAMES / sizeof NAMES[0]; i++)
    {
        const char *a = NAMES[i].name, *b = text;
        while (*a && *b && ((*a | 32) == (*b | 32))) { a++; b++; }
        if (!*a && (!*b || *b == ' ')) return NAMES[i].rgb;
    }
    if (*text == '#') text++;
    char *end;
    unsigned long v = strtoul(text, &end, 16);
    if (end == text) return fallback;
    size_t n = (size_t)(end - text);
    if (n == 3) return (uint32_t)(((v >> 8) & 15) * 0x110000 + ((v >> 4) & 15) * 0x1100 + (v & 15) * 0x11);
    return (uint32_t)(v & 0xFFFFFF); /* #RRGGBB, or #AARRGGBB's colour */
}

/* Fade Screen: to the colour (out) or back from it (in); returns how long it takes (ms). */
double screen_fade(uint32_t colour, int out, double seconds)
{
    fadeColor = colour;
    fadeFrom = fadeOpacity; fadeTo = out ? 1 : 0;
    fadeTotal = seconds > 0 ? seconds * 1000 : 0; fadeMs = 0;
    if (fadeTotal <= 0) { fadeOpacity = fadeTo; fadeTotal = -1; return 0; }
    return fadeTotal;
}

void screen_flash(uint32_t colour, double seconds)
{
    if (flashCount >= 8) return;
    flashes[flashCount++] = (Flash){ colour, (seconds > 0.05 ? seconds : 0.05) * 1000, 0 };
}

void screen_show_picture(int number, const Sprite *sprite, double x, double y, double opacity, uint32_t now)
{
    if (number < 1) number = 1;
    if (number > 50) number = 50;
    pics[number] = (Pic){ sprite, x, y, opacity < 0 ? 0 : opacity > 1 ? 1 : opacity, sprite != NULL, now };
}

void screen_erase_picture(int number)
{
    for (int i = 1; i <= 50; i++) if (number <= 0 || i == number) pics[i].shown = 0;
}

void screen_shake(int intensity, double seconds) { shakeMax = intensity * 2.0; shakeLeft = seconds * 1000; }

void screen_update(double ms)
{
    if (fadeTotal > 0)
    {
        fadeMs += ms;
        double t = fadeMs >= fadeTotal ? 1 : fadeMs / fadeTotal;
        fadeOpacity = fadeFrom + (fadeTo - fadeFrom) * t;
        if (t >= 1) fadeTotal = -1;
    }
    for (int i = 0; i < flashCount; i++)
        if ((flashes[i].ms += ms) >= flashes[i].total) { flashes[i] = flashes[--flashCount]; i--; }
    if (shakeLeft > 0)
    {
        shakeLeft -= ms;
        shakeX = shakeLeft > 0 ? (int)((rnd01() * 2 - 1) * shakeMax) : 0;
        shakeY = shakeLeft > 0 ? (int)((rnd01() * 2 - 1) * shakeMax) : 0;
    }
}

static void cover(uint32_t *pixels, uint32_t rgb, double opacity)
{
    int a = (int)(opacity * 255 + 0.5);
    if (a <= 0) return;
    uint32_t k = (uint32_t)(255 - a), r = ((rgb >> 16) & 255) * (uint32_t)a, g = ((rgb >> 8) & 255) * (uint32_t)a, b = (rgb & 255) * (uint32_t)a;
    for (int i = 0; i < SCREEN_W * SCREEN_H; i++)
    {
        uint32_t c = pixels[i];
        pixels[i] = 0xFF000000u | (((((c >> 16) & 255) * k + r) / 255) << 16) | (((((c >> 8) & 255) * k + g) / 255) << 8) | (((c & 255) * k + b) / 255);
    }
}

/* Over the world: the pictures (by number), the flashes, the fade - under the dialog boxes. */
void screen_draw(uint32_t *pixels, uint32_t now)
{
    for (int n = 1; n <= 50; n++)
    {
        Pic *p = &pics[n];
        if (!p->shown) continue;
        const Picture *pic = sprite_picture_at(p->sprite, now - p->since);
        if (!pic) continue;
        int a0 = (int)(p->opacity * 255);
        for (int y = 0; y < pic->height; y++)
        {
            int sy = (int)p->y + y;
            if (sy < 0 || sy >= SCREEN_H) continue;
            for (int x = 0; x < pic->width; x++)
            {
                int sx = (int)p->x + x;
                if (sx < 0 || sx >= SCREEN_W) continue;
                uint32_t c = pic->pixels[y * pic->width + x], a = ((c >> 24) * (uint32_t)a0) / 255;
                if (!a) continue;
                uint32_t *d = &pixels[sy * SCREEN_W + sx], k = 255 - a;
                *d = 0xFF000000u | (((((*d >> 16) & 255) * k + ((c >> 16) & 255) * a) / 255) << 16) | (((((*d >> 8) & 255) * k + ((c >> 8) & 255) * a) / 255) << 8) | (((*d & 255) * k + (c & 255) * a) / 255);
            }
        }
    }
    for (int i = 0; i < flashCount; i++) cover(pixels, flashes[i].color, 1 - flashes[i].ms / flashes[i].total);
    cover(pixels, fadeColor, fadeOpacity);
}

/* Shake: the whole picture moved (black where it left). */
void screen_shake_apply(uint32_t *pixels)
{
    if (!shakeX && !shakeY) return;
    static uint32_t copy[SCREEN_W * SCREEN_H];
    memcpy(copy, pixels, sizeof copy);
    for (int y = 0; y < SCREEN_H; y++)
        for (int x = 0; x < SCREEN_W; x++)
        {
            int fx = x - shakeX, fy = y - shakeY;
            pixels[y * SCREEN_W + x] = fx >= 0 && fy >= 0 && fx < SCREEN_W && fy < SCREEN_H ? copy[fy * SCREEN_W + fx] : 0xFF000000u;
        }
}

/* A new game: no fades, flashes, pictures or shaking left over. */
void screen_reset(void) { flashCount = 0; memset(pics, 0, sizeof pics); shakeLeft = shakeMax = 0; shakeX = shakeY = 0; }
