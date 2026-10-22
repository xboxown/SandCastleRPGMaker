/*
 * Weather, as the Windows engine makes it (GameTestWindow.Weather, from
 * Exult's weather eggs): the map's weather (Map Editor > Map Weather) and
 * Change Weather - rain slanted by the wind, snow drifting, hail, storms
 * (heavy rain, lightning flashing the screen white, thunder after it);
 * cloud shadows drifting over the land with the wind; the rain sound
 * looping. Not drawn while the main character is under a roof. It can come
 * and go by itself, hour by hour. It stops while the game is paused.
 */
#include "weather.h"
#include "events.h"
#include "lighting.h"
#include "sound.h"
#include "world.h"
#include "platform.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdlib.h>
#include <string.h>

enum { W_CLEAR, W_RAIN, W_SNOW, W_HAIL, W_STORM };
#define OWNER_WEATHER 3
#define MAX_PARTICLES 300
#define MAX_CLOUDS 20

typedef struct Particle { double x, y, vx, vy, phase; } Particle;
typedef struct Cloud { double x, y, w, h; } Cloud;

static int kind, intensity = 50, clouds, cloudCover = 40, wind = 2, changes, changeChance, rainSound = -1, thunderSound = -1;
static int mapKind;
static Particle particles[MAX_PARTICLES]; static int particleCount;
static Cloud cloudList[MAX_CLOUDS]; static int cloudCount;
static double lightningMs = -1, flashMs = -1, thunderMs = -1;
static int weatherHour = -1, indoors;

static unsigned rng = 98765u;
static double rnd01(void) { rng = rng * 1103515245u + 12345u; return ((rng >> 8) & 0xFFFF) / 65536.0; }
static int rnd(int n) { return n > 0 ? (int)(rnd01() * n) : 0; }

static double wind_x(void) { return wind == 2 || wind == 1 || wind == 3 ? 1 : wind == 6 || wind == 7 || wind == 5 ? -1 : 0; }
static double wind_y(void) { return wind == 4 || wind == 3 || wind == 5 ? 1 : wind == 0 || wind == 1 || wind == 7 ? -1 : 0; }

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }

static void new_particle(Particle *p)
{
    double wx = wind_x();
    p->phase = rnd01() * 6.28;
    if (kind == W_SNOW) { p->vx = wx * 12; p->vy = 22 + rnd01() * 14; }
    else if (kind == W_HAIL) { p->vx = wx * 30; p->vy = 170 + rnd01() * 40; }
    else { p->vx = wx * (kind == W_STORM ? 110 : 60); p->vy = (kind == W_STORM ? 260 : 200) + rnd01() * 60; }
}

/* BuildWeather: the drops and clouds for the weather now, and its rain sound. */
static void build(void)
{
    double heavy = (intensity < 1 ? 1 : intensity > 100 ? 100 : intensity) / 100.0;
    particleCount = kind == W_RAIN ? (int)(30 + 140 * heavy) : kind == W_STORM ? (int)(80 + 200 * heavy) : kind == W_SNOW ? (int)(25 + 110 * heavy) : kind == W_HAIL ? (int)(20 + 90 * heavy) : 0;
    if (particleCount > MAX_PARTICLES) particleCount = MAX_PARTICLES;
    for (int i = 0; i < particleCount; i++) { new_particle(&particles[i]); particles[i].x = rnd01() * SCREEN_W; particles[i].y = rnd01() * SCREEN_H; }
    sound_stop_owner(OWNER_WEATHER);
    if ((kind == W_RAIN || kind == W_STORM) && rainSound >= 0) sound_play_ex(rainSound, -1, 1, OWNER_WEATHER, 1);
    lightningMs = kind == W_STORM ? 3000 + rnd(6000) : -1;
    cloudCount = clouds ? (cloudCover / 8 < 1 ? 1 : cloudCover / 8 > 14 ? 14 : cloudCover / 8) : 0;
    if (kind == W_STORM || kind == W_RAIN) cloudCount += 3;
    if (cloudCount > MAX_CLOUDS) cloudCount = MAX_CLOUDS;
    double cx, cy;
    world_view_center(&cx, &cy);
    double left = cx - SCREEN_W / 2.0, top = cy - SCREEN_H / 2.0;
    for (int i = 0; i < cloudCount; i++)
    {
        Cloud *c = &cloudList[i];
        c->w = 90 + rnd(110); c->h = 50 + rnd(60);
        c->x = left + rnd01() * (SCREEN_W + 200) - 100; c->y = top + rnd01() * (SCREEN_H + 160) - 80;
    }
    lighting_weather(kind, intensity, clouds, cloudCover);
}

void weather_load(void)
{
    cJSON *w = get(get(get((cJSON *)world_json(), "Project"), "Map"), "Weather");
    kind = mapKind = num(w, "Kind", 0); intensity = num(w, "Intensity", 50);
    clouds = cJSON_IsTrue(get(w, "Clouds")); cloudCover = num(w, "CloudCover", 40); wind = num(w, "Wind", 2);
    changes = cJSON_IsTrue(get(w, "Changes")); changeChance = num(w, "ChangeChancePercent", 0);
    rainSound = num(w, "RainSoundResourceId", -1); thunderSound = num(w, "ThunderSoundResourceId", -1);
    weatherHour = events_hour();
    build();
}

/* Change Weather (and the weather changing by itself). clouds: -1 = as it is. */
void weather_change(int newKind, int newIntensity, int newClouds)
{
    kind = newKind;
    intensity = newIntensity < 1 ? 1 : newIntensity > 100 ? 100 : newIntensity;
    if (newClouds >= 0) clouds = newClouds;
    build();
}

void weather_update(double ms)
{
    if (changes && events_hour() != weatherHour)
    {
        weatherHour = events_hour();
        if (rnd(100) < (changeChance < 0 ? 0 : changeChance > 100 ? 100 : changeChance))
            weather_change(kind == W_CLEAR ? (mapKind == W_CLEAR ? W_RAIN : mapKind) : W_CLEAR, 20 + rnd(81), -1); /* clear, or the map's own weather */
    }
    double s = ms / 1000.0;
    indoors = world_indoors();
    if (!indoors)
        for (int i = 0; i < particleCount; i++)
        {
            Particle *p = &particles[i];
            p->phase += s * 2;
            p->x += (p->vx + (kind == W_SNOW ? sin(p->phase) * 10 : 0)) * s;
            p->y += p->vy * s;
            if (p->y > SCREEN_H) { p->y -= SCREEN_H + 6; p->x = rnd01() * SCREEN_W; }
            if (p->x > SCREEN_W) p->x -= SCREEN_W; else if (p->x < 0) p->x += SCREEN_W;
        }
    /* clouds drift with the wind; one gone off the downwind side comes back on the other */
    double dx = (wind_x() == 0 && wind_y() == 0 ? 1 : wind_x()) * 8 * s, dy = wind_y() * 8 * s, cx, cy;
    world_view_center(&cx, &cy);
    double left = cx - SCREEN_W / 2.0, top = cy - SCREEN_H / 2.0;
    for (int i = 0; i < cloudCount; i++)
    {
        Cloud *c = &cloudList[i];
        c->x += dx; c->y += dy;
        if (c->x > left + SCREEN_W + 120) c->x = left - c->w - rnd(80);
        else if (c->x < left - c->w - 120) c->x = left + SCREEN_W + rnd(80);
        if (c->y > top + SCREEN_H + 100) c->y = top - c->h - rnd(60);
        else if (c->y < top - c->h - 100) c->y = top + SCREEN_H + rnd(60);
    }
    if (kind == W_STORM && lightningMs >= 0 && (lightningMs -= ms) <= 0)
    {
        lightningMs = 4000 + rnd(9000) * (1.5 - intensity / 100.0);
        if (!indoors) flashMs = 0;
        if (thunderSound >= 0) thunderMs = 300 + rnd(1500);
    }
    if (flashMs >= 0 && (flashMs += ms) > 520) flashMs = -1;
    if (thunderMs >= 0 && (thunderMs -= ms) <= 0) { thunderMs = -1; sound_play(thunderSound); }
}

static void blend(uint32_t *d, uint32_t rgb, int a)
{
    uint32_t c = *d, k = (uint32_t)(255 - a);
    *d = 0xFF000000u | (((((c >> 16) & 255) * k + ((rgb >> 16) & 255) * (uint32_t)a) / 255) << 16) | (((((c >> 8) & 255) * k + ((rgb >> 8) & 255) * (uint32_t)a) / 255) << 8) | (((c & 255) * k + (rgb & 255) * (uint32_t)a) / 255);
}

/* Cloud shadows: soft dark ellipses over the land (under the sky's light). */
void weather_draw_clouds(uint32_t *pixels)
{
    if (indoors || !cloudCount) return;
    double cx, cy;
    world_view_center(&cx, &cy);
    double left = cx - SCREEN_W / 2.0, top = cy - SCREEN_H / 2.0;
    for (int i = 0; i < cloudCount; i++)
    {
        const Cloud *c = &cloudList[i];
        double ox = c->x - left, oy = c->y - top, rx = c->w / 2, ry = c->h / 2, mx = ox + rx, my = oy + ry;
        int x0 = (int)ox < 0 ? 0 : (int)ox, x1 = (int)(ox + c->w) > SCREEN_W ? SCREEN_W : (int)(ox + c->w);
        int y0 = (int)oy < 0 ? 0 : (int)oy, y1 = (int)(oy + c->h) > SCREEN_H ? SCREEN_H : (int)(oy + c->h);
        for (int y = y0; y < y1; y++)
            for (int x = x0; x < x1; x++)
            {
                double ex = (x + 0.5 - mx) / rx, ey = (y + 0.5 - my) / ry, r = sqrt(ex * ex + ey * ey);
                if (r >= 1) continue;
                blend(&pixels[y * SCREEN_W + x], 0, (int)(70 * (1 - r))); /* RadialGradientBrush: 70 alpha black in the middle, none at the edge */
            }
    }
}

/* Rain, snow, hail - and a lightning flash - over the world. */
void weather_draw(uint32_t *pixels)
{
    if (!indoors)
        for (int i = 0; i < particleCount; i++)
        {
            const Particle *p = &particles[i];
            if (kind == W_SNOW || kind == W_HAIL)
            {
                uint32_t colour = kind == W_SNOW ? 0xFFFFFF : 0xE8F0FF;
                int a = kind == W_SNOW ? 230 : 255;
                for (int yy = 0; yy < 2; yy++)
                    for (int xx = 0; xx < 2; xx++)
                    {
                        int x = (int)p->x + xx, y = (int)p->y + yy;
                        if (x >= 0 && y >= 0 && x < SCREEN_W && y < SCREEN_H) blend(&pixels[y * SCREEN_W + x], colour, a);
                    }
                continue;
            }
            /* a streak along its fall, 1 pixel wide */
            double len = 0.025, x2 = p->x + p->vx * len, y2 = p->y + p->vy * len;
            int steps = (int)fmax(fabs(x2 - p->x), fabs(y2 - p->y)) + 1;
            for (int k = 0; k <= steps; k++)
            {
                int x = (int)(p->x + (x2 - p->x) * k / steps), y = (int)(p->y + (y2 - p->y) * k / steps);
                if (x >= 0 && y >= 0 && x < SCREEN_W && y < SCREEN_H) blend(&pixels[y * SCREEN_W + x], 0xA8B8D8, 170);
            }
        }
    if (flashMs >= 0)
    {
        /* the flash: 0.85 at 40 ms, 0.2 at 120, 0.6 at 170, gone at 520 */
        double t = flashMs, o = t < 40 ? 0.85 * t / 40 : t < 120 ? 0.85 + (0.2 - 0.85) * (t - 40) / 80 : t < 170 ? 0.2 + 0.4 * (t - 120) / 50 : 0.6 * (1 - (t - 170) / 350);
        int a = (int)(o * 255);
        if (a > 0) for (int i = 0; i < SCREEN_W * SCREEN_H; i++) blend(&pixels[i], 0xFFFFFF, a);
    }
}

/* ---------------- Saving: the weather as it is ---------------- */
void weather_save(cJSON *runtime)
{
    cJSON *w = cJSON_CreateObject();
    cJSON_AddNumberToObject(w, "Kind", kind); cJSON_AddNumberToObject(w, "Intensity", intensity); cJSON_AddBoolToObject(w, "Clouds", clouds);
    cJSON_AddNumberToObject(w, "CloudCover", cloudCover); cJSON_AddNumberToObject(w, "Wind", wind);
    cJSON_AddItemToObject(runtime, "Weather", w);
}

void weather_restore(cJSON *runtime)
{
    cJSON *w = get(runtime, "Weather");
    if (!w) return;
    kind = num(w, "Kind", kind); intensity = num(w, "Intensity", intensity); clouds = cJSON_IsTrue(get(w, "Clouds"));
    cloudCover = num(w, "CloudCover", cloudCover); wind = num(w, "Wind", wind);
    build();
}
