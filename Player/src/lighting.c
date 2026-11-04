/*
 * The sky's light, as the Windows engine does it (GameTestWindow.Lighting):
 * Exult's time-of-day palettes - day; at dawn the night lifts through the
 * dawn colour, at dusk it falls through the dusk colour; night - with the
 * hours and colours of System Settings > Day & Night. In the dark, lit lights
 * on screen lift it (gamerend.cc Get_light_strength): each adds brightness x
 * (75 - 2 x tiles across - 3 x tiles down) from the main character; under 224
 * a candle's glow, under 640 a single light, more: many lights - or, set so,
 * the dusk colour or full daylight. Change Day/Night sets the sky until the
 * next hour. The sky is a colour laid over the world, fading in 0.4 s.
 */
#include "blend.h"
#include "lighting.h"
#include "events.h"
#include "world.h"
#include "platform.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdlib.h>
#include <string.h>

typedef struct Sky { uint32_t rgb; double opacity; } Sky;

static int enabled = 1;
static double dawnStart = 5, dayStart = 7, duskStart = 19, nightStart = 21, overcastStrength = 0.25;
static Sky night = { 0x155752, 0.6 }, dusk = { 0x9C680C, 0.5 };

static Sky overrideSky; static int overrideHour = -1;
static Sky shown = { 0, -1 };                 /* what the sky was last set to */
static uint32_t drawnColor; static double drawnOpacity, fromOpacity, targetOpacity, fadeMs = -1;
static double checkMs, fadeLength = 400;
static double specialLight; /* game minutes of a light spell's glow left */
static int weatherKind, weatherIntensity, weatherClouds, weatherCover; /* the overcast */

static const double DARKNESS_LEFT[4] = { 1.0, 0.75, 0.55, 0.35 }; /* none, candle, single light, many lights */

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static double dbl(cJSON *o, const char *k, double fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valuedouble : fallback; }

static uint32_t parse_colour(const char *hex, uint32_t fallback)
{
    if (!hex) return fallback;
    if (*hex == '#') hex++;
    size_t n = strlen(hex);
    if (n != 6 && n != 8) return fallback;
    char *end;
    unsigned long v = strtoul(hex, &end, 16);
    if (*end) return fallback;
    return (uint32_t)(v & 0xFFFFFF); /* #AARRGGBB: the colour (its own alpha isn't used, as the engine's) */
}

static double clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

void lighting_load(void)
{
    cJSON *project = get((cJSON *)world_json(), "Project"), *d = get(get(project, "Settings"), "DayNight");
    overrideHour = -1; shown = (Sky){ 0, -1 }; checkMs = 0; /* a new game: no Change Day/Night of the last one */
    if (!d) return;
    enabled = !cJSON_IsFalse(get(d, "Enabled"));
    dawnStart = dbl(d, "DawnStartHour", 5); dayStart = dbl(d, "DayStartHour", 7);
    duskStart = dbl(d, "DuskStartHour", 19); nightStart = dbl(d, "NightStartHour", 21);
    night.rgb = parse_colour(cJSON_GetStringValue(get(d, "NightColor")), 0x155752); night.opacity = clamp01(dbl(d, "NightStrength", 0.6));
    dusk.rgb = parse_colour(cJSON_GetStringValue(get(d, "DuskColor")), 0x9C680C); dusk.opacity = clamp01(dbl(d, "DuskStrength", 0.5));
    overcastStrength = clamp01(dbl(d, "OvercastStrength", 0.25));
}

static Sky mix(Sky a, Sky b, double t)
{
    t = clamp01(t);
    Sky m;
    if (a.opacity <= 0) m.rgb = b.rgb;
    else if (b.opacity <= 0) m.rgb = a.rgb;
    else
    {
        int ar = (a.rgb >> 16) & 255, ag = (a.rgb >> 8) & 255, ab = a.rgb & 255, br = (b.rgb >> 16) & 255, bg = (b.rgb >> 8) & 255, bb = b.rgb & 255;
        m.rgb = (uint32_t)((int)(ar + (br - ar) * t) << 16 | (int)(ag + (bg - ag) * t) << 8 | (int)(ab + (bb - ab) * t));
    }
    m.opacity = a.opacity + (b.opacity - a.opacity) * t;
    return m;
}

/* SkyNow: the sky for this time of day, and how much of it is night (the part lights lift). */
static Sky sky_now(double *nightAmount)
{
    Sky day = { dusk.rgb, 0 };
    if (overrideHour >= 0 && events_hour() == overrideHour) { *nightAmount = overrideSky.opacity >= night.opacity - 0.01 ? 1 : 0; return overrideSky; }
    overrideHour = -1;
    *nightAmount = 0;
    if (!enabled) return day;
    double h = events_hour() + events_minute() / 60.0;
    double ds = dayStart > dawnStart + 0.01 ? dayStart : dawnStart + 0.01, ns = nightStart > duskStart + 0.01 ? nightStart : duskStart + 0.01;
    if (h >= dawnStart && h < ds)
    {
        double t = (h - dawnStart) / (ds - dawnStart);
        if (t < 0.5) { *nightAmount = 1 - t * 2; return mix(night, dusk, t * 2); }
        return mix(dusk, day, (t - 0.5) * 2);
    }
    if (h >= duskStart && h < ns)
    {
        double t = (h - duskStart) / (ns - duskStart);
        if (t < 0.5) return mix(day, dusk, t * 2);
        *nightAmount = (t - 0.5) * 2;
        return mix(dusk, night, (t - 0.5) * 2);
    }
    if (h >= ds && h < duskStart) return day;
    *nightAmount = 1;
    return night;
}

static void show(Sky sky)
{
    if (shown.rgb == sky.rgb && fabs(shown.opacity - sky.opacity) < 0.005) return;
    shown = sky;
    drawnColor = sky.rgb;
    fromOpacity = drawnOpacity; targetOpacity = clamp01(sky.opacity); fadeMs = 0; fadeLength = 400; /* a 0.4 s fade to it */
}

void lighting_update(double ms)
{
    if (fadeMs >= 0)
    {
        fadeMs += ms;
        double t = fadeMs >= fadeLength ? 1 : fadeMs / fadeLength;
        drawnOpacity = fromOpacity + (targetOpacity - fromOpacity) * t;
        if (t >= 1) fadeMs = -1;
    }
    if ((checkMs -= ms) > 0) return;
    checkMs = 250;
    double nightAmount;
    Sky sky = sky_now(&nightAmount);
    int inCave = events_player_in_cave();
    if (inCave) { sky.rgb = 0; sky.opacity = 1; nightAmount = 1; } /* a Cave: complete darkness, day or night - only a light source lifts it */
    /* Overcast: weather and clouds darken the day (and the night a little more). */
    double gloom = (weatherKind == 0 ? 0 : weatherIntensity / 100.0) + (weatherClouds ? weatherCover / 200.0 : 0);
    gloom = inCave ? 0 : clamp01(gloom) * overcastStrength;
    if (gloom > 0.001)
    {
        Sky grey = { 0x20242C, gloom };
        Sky m = mix(sky, grey, sky.opacity <= 0 ? 1 : 0.35);
        sky.rgb = m.rgb;
        sky.opacity = 1 - (1 - sky.opacity) * (1 - gloom);
    }
    if (nightAmount > 0)
    {
        int gives = 0, strength = world_light_strength(&gives);
        if (specialLight > 0 && strength < 640) strength = 640; /* a light spell: as many lights (add_special_light) */
        if (strength > 0)
        {
            if (gives == 2) sky.opacity *= 1 - nightAmount;            /* full daylight */
            else if (gives == 1) sky = mix(sky, dusk, nightAmount);      /* the dusk look */
            else
            {
                int level = strength < 224 ? 1 : strength < 640 ? 2 : 3;
                sky.opacity *= 1 - nightAmount * (1 - DARKNESS_LEFT[level]);
            }
        }
    }
    show(sky);
}

/* The sky's light over the world (the Windows engine's overlay over the map): each pixel c becomes
   ((c & 0xFF00FF) * keep + rbAdd) >> 8 for red and blue, the same with 0xFF00 for green. The world draws its
   pictures that way already darkened (world.c shade) - the same picture, since mixing is linear - instead of
   going over the whole view again. 0: no light over it now. */
int lighting_shade(uint32_t *keep, uint32_t *rbAdd, uint32_t *gAdd)
{
    int a = (int)(drawnOpacity * 255 + 0.5);
    if (a <= 0) return 0;
    uint32_t a1 = (uint32_t)a + ((uint32_t)a >> 7);
    *keep = 256 - a1;
    *rbAdd = (drawnColor & 0xFF00FFu) * a1;
    *gAdd = (drawnColor & 0xFF00u) * a1;
    return 1;
}

/* Change Day/Night: Morning (clear), Afternoon (the dusk colour), Evening (the night) - until the next hour. */
void lighting_period(const char *period)
{
    if (!strcmp(period, "Morning")) overrideSky = (Sky){ 0, 0 };
    else if (!strcmp(period, "Afternoon")) overrideSky = dusk;
    else if (!strcmp(period, "Evening")) overrideSky = night;
    else return;
    overrideHour = events_hour();
    checkMs = 0;
}

/* Apply Palette Color: a colour over the world at once (kept till the sky next changes). */
void lighting_palette(const char *hex, double opacity)
{
    if (!hex || !hex[0]) return;
    drawnColor = parse_colour(hex, drawnColor);
    fromOpacity = drawnOpacity; targetOpacity = clamp01(opacity); fadeMs = 0; fadeLength = 600; /* faded in over 0.6 s */
}

void lighting_weather(int kind, int intensity, int clouds, int cover)
{
    weatherKind = kind; weatherIntensity = intensity; weatherClouds = clouds; weatherCover = cover;
    checkMs = 0; /* the sky darkens or brightens with it */
}

/* A light spell (or potion): the night lit as by many lights for that many game minutes; counted down by the minute. */
void lighting_special(double minutes) { if (minutes > specialLight) specialLight = minutes; checkMs = 0; }
void lighting_minutes_passed(double minutes) { if (specialLight > 0) { specialLight -= minutes; if (specialLight < 0) specialLight = 0; } }
