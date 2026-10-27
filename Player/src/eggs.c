/*
 * Spawn points - eggs - as the Windows engine has them (GameTestWindow.Spawns.cs, from Exult's objs/egg.cc):
 *  - When one hatches (Egg_object::is_active): CachedIn as the map is entered; PartyNear / AvatarNear when someone of
 *    the party / the main character comes into its area (its distance round it, on its floor); AvatarFar when the main
 *    character goes out of it; the footpads on its own square; SomethingOn when a thing (not a character) is put down
 *    in its area; External only by a Button egg. Nocturnal: only 21:00 - 04:59.
 *  - Egg_object::hatch: marked hatched; a roll of 1-100 at most its Probability does its kind's hatch_now; Once: gone.
 *    Auto-reset (unhatch): it can hatch again once the party has left - a continuous music or sound egg stops then.
 *  - Its kinds run the same event commands the Windows engine runs for them (Play Music, Play Sound, Call Common Event,
 *    Change Map, Change Weather); monster eggs put their copies on the map (world_spawn_now); missile eggs shoot their
 *    projectile, again while the main character stays within range (Missile_launcher); button eggs set off External ones.
 */
#include "eggs.h"
#include "world.h"
#include "events.h"
#include "items.h"
#include "vitals.h"
#include "traps.h"
#include "commands.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

enum { K_MONSTER, K_MUSIC, K_SOUND, K_USECODE, K_MISSILE, K_TELEPORT, K_WEATHER, K_PATH, K_BUTTON };
enum { E_CACHED_IN, E_PARTY_NEAR, E_AVATAR_NEAR, E_AVATAR_FAR, E_AVATAR_FOOTPAD, E_PARTY_FOOTPAD, E_SOMETHING_ON, E_EXTERNAL };
#define MAX_EGGS 512

typedef struct EggState
{
    int avatarIn, partyIn, thingsIn, launching;
    double launchMs;
    long weatherUntil;
    cJSON *hatchList, *unhatchList; /* its effect as event commands (kept: the event runner knows them by where they are) */
} EggState;

static EggState states[MAX_EGGS];
static int started;
static double checkMs;
static cJSON *clearWeather;

static unsigned rng = 1597334677u;
static int rnd(int n) { rng = rng * 1103515245u + 12345u; return n > 0 ? (int)((rng >> 8) % (unsigned)n) : 0; }

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static double dbl(cJSON *o, const char *k, double fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valuedouble : fallback; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }
static void set_flag(cJSON *o, const char *k, int v) { cJSON_DeleteItemFromObject(o, k); cJSON_AddItemToObject(o, k, cJSON_CreateBool(v)); }
static cJSON *eggs(void) { return get(get(get((cJSON *)world_json(), "Project"), "Map"), "SpawnPoints"); }

void eggs_reset(void)
{
    for (int i = 0; i < MAX_EGGS; i++) { cJSON_Delete(states[i].hatchList); cJSON_Delete(states[i].unhatchList); }
    memset(states, 0, sizeof states);
    for (int i = 0; i < MAX_EGGS; i++) { states[i].thingsIn = -1; states[i].weatherUntil = -1; }
    started = 0; checkMs = 0;
}

/* A list of one event command: { Type, Parameters { key: "value"... } }. */
static cJSON *one_command(int type, const char *const *pairs)
{
    cJSON *list = cJSON_CreateArray(), *c = cJSON_CreateObject(), *p = cJSON_CreateObject();
    cJSON_AddNumberToObject(c, "Type", type);
    for (int i = 0; pairs[i]; i += 2) cJSON_AddStringToObject(p, pairs[i], pairs[i + 1]);
    cJSON_AddItemToObject(c, "Parameters", p);
    cJSON_AddItemToArray(list, c);
    return list;
}

static void run(cJSON **slot, cJSON *made) { if (!*slot) *slot = made; else cJSON_Delete(made); events_run_commands(*slot, 0); }

/* Exult: a nocturnal egg is only active from 21:00 to 04:59. */
static int night_allows(cJSON *egg) { int h = events_hour(); return !flag(egg, "Nocturnal") || h >= 21 || h <= 4; }

static int in_area(cJSON *egg, int x, int y, int onFloor)
{
    if (onFloor != num(egg, "Floor", 1)) return 0;
    int crit = num(egg, "Criteria", 0), reach = crit == E_AVATAR_FOOTPAD || crit == E_PARTY_FOOTPAD ? 0 : num(egg, "Distance", 3);
    if (reach < 0) reach = 0;
    return abs(x - num(egg, "X", 0)) <= reach && abs(y - num(egg, "Y", 0)) <= reach;
}

static int in_party(int a) { return a == 0 || (!world_actor_gone(a) && items_in_party(world_actor_character(a)) && !vitals_dead(world_actor_character(a))); }

/* Its projectile from its middle: in its direction, or at a party member within its range. */
static void launch_from(cJSON *egg)
{
    int ex = num(egg, "X", 0), ey = num(egg, "Y", 0), f = num(egg, "Floor", 1), range = num(egg, "RangeTiles", 12), dir = num(egg, "Direction", -1);
    double x = ex * world_tile_w() + world_tile_w() / 2.0, y = ey * world_tile_h() + world_tile_h() / 2.0;
    int lift = (f - 1) * 5 + 1;
    static const int DX[8] = { 0, 1, 1, 1, 0, -1, -1, -1 }, DY[8] = { -1, -1, 0, 1, 1, 1, 0, -1 };
    if (dir >= 0 && dir < 8) { traps_launch(num(egg, "ProjectileId", -1), x, y, DX[dir], DY[dir], lift, -1); return; }
    int targets[64], n = 0;
    for (int a = 0; a < world_actor_count() && n < 64; a++)
    {
        if (!in_party(a)) continue;
        int ax, ay, af;
        world_actor_cell(a, &ax, &ay, &af);
        if ((abs(ax - ex) > abs(ay - ey) ? abs(ax - ex) : abs(ay - ey)) <= range) targets[n++] = a;
    }
    if (!n) return;
    double tx, ty;
    world_actor_pixel(targets[rnd(n)], &tx, &ty);
    double dx = tx - x, dy = ty - y, length = sqrt(dx * dx + dy * dy);
    if (length < 1) return;
    traps_launch(num(egg, "ProjectileId", -1), x, y, dx / length, dy / length, lift, -1);
}

static void hatch(int i, cJSON *egg);

/* Egg_object::hatch: marked hatched; its chance rolled; its kind's hatch_now; a "once" egg is gone. */
static void hatch(int i, cJSON *egg)
{
    if (flag(egg, "GoneRuntime") || !night_allows(egg)) return;
    set_flag(egg, "HatchedRuntime", 1);
    if (flag(egg, "Once")) set_flag(egg, "GoneRuntime", 1);
    if (rnd(100) + 1 > num(egg, "Probability", 100)) return;
    EggState *s = &states[i];
    char a[32], b[32], c[32], d[32];
    switch (num(egg, "Kind", 0))
    {
        case K_MONSTER: world_spawn_now(egg); break;
        case K_MUSIC:
        {
            snprintf(a, sizeof a, "%d", num(egg, "SoundId", -1));
            const char *pairs[] = { "MusicResourceId", a, "Loop", flag(egg, "Continuous") ? "True" : "False", NULL };
            run(&s->hatchList, one_command(C_PlayMusic, pairs));
            break;
        }
        case K_SOUND:
            if (flag(egg, "Continuous"))
            {
                snprintf(a, sizeof a, "%d", num(egg, "SoundId", -1));
                const char *pairs[] = { "SoundResourceId", a, "Loop", "True", NULL };
                run(&s->hatchList, one_command(C_PlaySound, pairs));
            }
            else world_sound_at_point(num(egg, "SoundId", -1), num(egg, "X", 0) * world_tile_w() + world_tile_w() / 2.0, num(egg, "Y", 0) * world_tile_h() + world_tile_h() / 2.0,
                                      (num(egg, "Floor", 1) - 1) * 5); /* from the egg, by the distance */
            break;
        case K_USECODE:
            if (num(egg, "CommonEventId", -1) >= 0)
            {
                snprintf(a, sizeof a, "%d", num(egg, "CommonEventId", -1));
                const char *pairs[] = { "CommonEventId", a, NULL };
                run(&s->hatchList, one_command(C_CallCommonEvent, pairs));
            }
            break;
        case K_MISSILE:
            launch_from(egg);
            if (num(egg, "Criteria", 0) == E_PARTY_NEAR || num(egg, "Criteria", 0) == E_AVATAR_NEAR) { s->launching = 1; s->launchMs = (dbl(egg, "DelaySeconds", 2) > 0.2 ? dbl(egg, "DelaySeconds", 2) : 0.2) * 1000; }
            break;
        case K_TELEPORT:
        {
            int world = num(egg, "TeleportWorld", -1), x = num(egg, "TeleportX", -1), y = num(egg, "TeleportY", -1), f = num(egg, "TeleportFloor", 1);
            if (num(egg, "TeleportPath", -1) >= 0)
            {
                cJSON *path = NULL, *e;
                cJSON_ArrayForEach(e, eggs()) if (num(e, "Kind", 0) == K_PATH && num(e, "PathNumber", 0) == num(egg, "TeleportPath", -1)) { path = e; break; }
                if (!path) break;
                world = -1; x = num(path, "X", 0); y = num(path, "Y", 0); f = num(path, "Floor", 1);
            }
            if (x < 0 || y < 0) break;
            if (world > 0) snprintf(a, sizeof a, "%d", world); else a[0] = 0;
            snprintf(b, sizeof b, "%d", x); snprintf(c, sizeof c, "%d", y); snprintf(d, sizeof d, "%d", f);
            const char *pairs[] = { "World", a, "X", b, "Y", c, "Floor", d, "Fade", "False", NULL };
            run(&s->hatchList, one_command(C_ChangeMap, pairs));
            break;
        }
        case K_WEATHER:
        {
            snprintf(a, sizeof a, "%d", num(egg, "WeatherIntensity", 50));
            const char *pairs[] = { "Kind", str(egg, "WeatherKind")[0] ? str(egg, "WeatherKind") : "Rain", "Intensity", a, "Clouds", "", NULL };
            run(&s->hatchList, one_command(C_ChangeWeather, pairs));
            s->weatherUntil = num(egg, "WeatherMinutes", 0) > 0 ? events_total_minutes() + num(egg, "WeatherMinutes", 0) : -1;
            break;
        }
        case K_BUTTON:
        {
            cJSON *other;
            int k = 0;
            cJSON_ArrayForEach(other, eggs())
            {
                if (other != egg && k < MAX_EGGS && num(other, "Criteria", 0) == E_EXTERNAL && (!flag(other, "HatchedRuntime") || flag(other, "AutoReset"))
                    && num(other, "Floor", 1) == num(egg, "Floor", 1) && abs(num(other, "X", 0) - num(egg, "X", 0)) <= num(egg, "Distance", 3) && abs(num(other, "Y", 0) - num(egg, "Y", 0)) <= num(egg, "Distance", 3))
                    hatch(k, other);
                k++;
            }
            break;
        }
    }
}

/* Exult's unhatch: it can hatch again; a continuous music or sound egg stops. */
static void unhatch(int i, cJSON *egg)
{
    set_flag(egg, "HatchedRuntime", 0);
    states[i].launching = 0;
    char a[32];
    if (num(egg, "Kind", 0) == K_MUSIC && flag(egg, "Continuous"))
    {
        const char *pairs[] = { "Seconds", "0", NULL };
        run(&states[i].unhatchList, one_command(C_StopMusic, pairs));
    }
    if (num(egg, "Kind", 0) == K_SOUND && flag(egg, "Continuous"))
    {
        snprintf(a, sizeof a, "%d", num(egg, "SoundId", -1));
        const char *pairs[] = { "SoundResourceId", a, NULL };
        run(&states[i].unhatchList, one_command(C_StopSound, pairs));
    }
}

/* SomethingOn: a thing (not a character) more in its area than before. */
static int things_in_area(cJSON *egg, EggState *s)
{
    int count = 0;
    for (int t = 0; t < world_thing_count(); t++)
    {
        int uid = world_thing_uid_at(t), c, id, q, x, y, inst, l, tp, w, d, z, h;
        if (!uid || !world_thing_there(uid)) continue;
        world_thing_info(uid, &c, &id, &q, &x, &y, &inst);
        world_thing_box(uid, &l, &tp, &w, &d, &z, &h);
        if (in_area(egg, x, y, world_floor_of_lift(z))) count++;
    }
    int more = s->thingsIn >= 0 && count > s->thingsIn;
    s->thingsIn = count;
    return more;
}

void eggs_update(double ms)
{
    cJSON *list = eggs(), *egg;
    if (!cJSON_GetArraySize(list)) return;
    int i = 0;
    cJSON_ArrayForEach(egg, list)
    {
        if (i >= MAX_EGGS) break;
        EggState *s = &states[i];
        if (s->launching)
        {
            int px, py, pf;
            world_actor_cell(0, &px, &py, &pf);
            int far = abs(px - num(egg, "X", 0)) > abs(py - num(egg, "Y", 0)) ? abs(px - num(egg, "X", 0)) : abs(py - num(egg, "Y", 0));
            if (flag(egg, "GoneRuntime") || pf != num(egg, "Floor", 1) || far > num(egg, "RangeTiles", 12)) s->launching = 0; /* out of range: it stops */
            else if ((s->launchMs -= ms) <= 0) { s->launchMs = (dbl(egg, "DelaySeconds", 2) > 0.2 ? dbl(egg, "DelaySeconds", 2) : 0.2) * 1000; launch_from(egg); }
        }
        if (s->weatherUntil >= 0 && events_total_minutes() >= s->weatherUntil)
        {
            s->weatherUntil = -1;
            if (!clearWeather) { const char *pairs[] = { "Kind", "Clear", "Intensity", "0", "Clouds", "", NULL }; clearWeather = one_command(C_ChangeWeather, pairs); }
            events_run_commands(clearWeather, 0);
        }
        i++;
    }
    if ((checkMs -= ms) > 0) return;
    checkMs = 250;
    int px, py, pf;
    world_actor_cell(0, &px, &py, &pf);
    i = 0;
    cJSON_ArrayForEach(egg, list)
    {
        if (i >= MAX_EGGS) break;
        EggState *s = &states[i];
        if (flag(egg, "GoneRuntime")) { i++; continue; }
        int avatarIn = in_area(egg, px, py, pf), partyIn = 0;
        for (int a = 0; a < world_actor_count() && !partyIn; a++)
        {
            if (!in_party(a)) continue;
            int ax, ay, af;
            world_actor_cell(a, &ax, &ay, &af);
            partyIn = in_area(egg, ax, ay, af);
        }
        int wasAvatarIn = s->avatarIn, wasPartyIn = s->partyIn, crit = num(egg, "Criteria", 0);
        s->avatarIn = avatarIn; s->partyIn = partyIn;
        if (!started && crit == E_CACHED_IN && !flag(egg, "HatchedRuntime")) { hatch(i, egg); i++; continue; } /* as the map is entered */
        int fire = crit == E_PARTY_NEAR || crit == E_PARTY_FOOTPAD ? partyIn && !wasPartyIn
                 : crit == E_AVATAR_NEAR || crit == E_AVATAR_FOOTPAD ? avatarIn && !wasAvatarIn
                 : crit == E_AVATAR_FAR ? !avatarIn && wasAvatarIn
                 : crit == E_SOMETHING_ON ? things_in_area(egg, s) : 0;
        if (fire && !flag(egg, "HatchedRuntime")) hatch(i, egg); /* a hatched auto-reset egg is armed again by unhatch */
        int left = crit == E_PARTY_NEAR || crit == E_PARTY_FOOTPAD ? !partyIn && wasPartyIn
                 : crit == E_AVATAR_NEAR || crit == E_AVATAR_FOOTPAD ? !avatarIn && wasAvatarIn
                 : crit == E_AVATAR_FAR ? avatarIn && !wasAvatarIn : 0;
        if (left && flag(egg, "HatchedRuntime") && flag(egg, "AutoReset")) unhatch(i, egg);
        i++;
    }
    started = 1;
}
