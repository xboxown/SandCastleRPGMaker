/*
 * Vehicles, as the Windows engine has them (GameTestWindow.Vehicles /
 * VehicleTravel), from Exult's Barge_object: ships, carts, flying carpets -
 * whatever is built in the Vehicles tab.
 *  - A vehicle is its pieces (objects marked with it) over a footprint;
 *    everything on it - loose things, characters, the player - is gathered
 *    before each step and goes along (Barge_object::gather).
 *  - Taking the controls: double-click its controls, or sit in its driver's
 *    seat; a deed may be needed (someone in the party carries it); its
 *    dashboard paperdoll opens if it has one.
 *  - Driving: the direction keys. It turns first (quarter turns, every piece
 *    swung round its middle, showing its look for the new way), then moves
 *    an 8px unit at a time at its speed: Land over walkable ground, Sea on
 *    water only, Air anywhere; nothing solid may be where it goes.
 *  - Wheels show their Moving look while it rolls; the moving sound loops.
 *  - Autopilot (Travel Vehicle): it makes its own way there, a side at a
 *    time, while everyone aboard walks about it. Air vehicles rise to floor
 *    11 to fly and come down only on clear, level, dry ground.
 */
#include "vehicles.h"
#include "world.h"
#include "items.h"
#include "events.h"
#include "sound.h"
#include "dolls.h"
#include "platform.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

enum { ROLE_NONE, ROLE_SEAT, ROLE_CONTROLS, ROLE_WHEEL };
enum { MOVE_LAND, MOVE_SEA, MOVE_AIR };
enum { DIR_N = 0, DIR_E = 2, DIR_S = 4, DIR_W = 6 };
#define FLY_LIFT 50          /* floor 11: one floor above the birds */
#define SOUND_OWNER 9101     /* the moving sound's owner */
#define MAX_ABOARD 512

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static double dbl(cJSON *o, const char *k, double fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valuedouble : fallback; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }
static void set(cJSON *o, const char *k, double v) { cJSON *e = get(o, k); if (cJSON_IsNumber(e)) cJSON_SetNumberValue(e, v); else if (e) cJSON_ReplaceItemInObject(o, k, cJSON_CreateNumber(v)); else cJSON_AddNumberToObject(o, k, v); }
static cJSON *project(void) { return get((cJSON *)world_json(), "Project"); }

static cJSON *object_def(int id)
{
    cJSON *o;
    cJSON_ArrayForEach(o, get(get(project(), "Objects"), "Objects")) if (num(o, "Id", -2) == id) return o;
    return NULL;
}

/* The vehicles built on this map (VehicleState): kept in the map itself, so a save carries them. */
static cJSON *states(void) { return get(get(project(), "Map"), "VehicleStates"); }
static cJSON *state_by_id(int id) { cJSON *v; cJSON_ArrayForEach(v, states()) if (num(v, "Id", -2) == id) return v; return NULL; }
static int alive_state(cJSON *v) { cJSON *s; cJSON_ArrayForEach(s, states()) if (s == v) return 1; return 0; }

static cJSON *template_of(cJSON *v)
{
    cJSON *h;
    cJSON_ArrayForEach(h, get(project(), "Houses")) if (num(h, "Id", -2) == num(v, "TemplateId", -1)) return h;
    return NULL;
}
static cJSON *settings_of(cJSON *v) { return get(template_of(v), "Vehicle"); }
static const char *vehicle_name(cJSON *v) { const char *n = str(template_of(v), "Name"); return n[0] ? n : "vehicle"; }
static int movement_of(cJSON *v) { return num(settings_of(v), "Movement", MOVE_LAND); }
static double speed_of(cJSON *v) { double s = dbl(v, "Speed", 0); if (s > 0) return s; s = dbl(settings_of(v), "CellsPerSecond", 3); return s < 0.5 ? 0.5 : s; }
static int role_of(int uid) { return num(object_def(world_thing_object_id(uid)), "VehicleRole", ROLE_NONE); }

static void say(const char *line) { game_log(line); }

/* ---------------- What's on it ---------------- */

typedef struct Aboard { int uids[MAX_ABOARD], nu, actors[64], na, player, top; } Aboard;

static void foot(cJSON *v, int *l, int *t, int *w, int *d) { *l = num(v, "Left", 0); *t = num(v, "Top", 0); *w = num(v, "Width", 1); *d = num(v, "Depth", 1); }

/* Barge_object::gather: its own pieces, and whatever is on it - from its floor up to above its highest piece. */
static void gather(cJSON *v, Aboard *a)
{
    memset(a, 0, sizeof *a);
    int l, t, w, d, lift = num(v, "Lift", 0), id = num(v, "Id", -1);
    foot(v, &l, &t, &w, &d);
    int near[MAX_ABOARD], n = world_things_in(l - 2, t - 2, w + 4, d + 4, near, MAX_ABOARD);
    a->top = lift + 1;
    for (int i = 0; i < n; i++)
        if (world_thing_vehicle(near[i]) == id)
        {
            int bl, bt, bw, bd, bz, bh;
            world_thing_box(near[i], &bl, &bt, &bw, &bd, &bz, &bh);
            if (bz + bh > a->top) a->top = bz + bh;
            if (a->nu < MAX_ABOARD) a->uids[a->nu++] = near[i];
        }
    for (int i = 0; i < n; i++)
    {
        if (world_thing_vehicle(near[i])) continue;
        int bl, bt, bw, bd, bz, bh, kind = world_thing_kind(near[i]);
        world_thing_box(near[i], &bl, &bt, &bw, &bd, &bz, &bh);
        if (bl >= l + w || l >= bl + bw || bt >= t + d || t >= bt + bd) continue;
        if (bz < lift || bz >= a->top + 5) continue;
        if (kind == 2 || (kind == 1 && !world_thing_solid(near[i])) || kind == 9) continue; /* fixed to the world: a wall, a flat tile, a trap */
        if (a->nu < MAX_ABOARD) a->uids[a->nu++] = near[i];
    }
    for (int actor = 0; actor < world_actor_count(); actor++)
    {
        int al, at, aw, ad, alift, ah;
        world_actor_foot(actor, &al, &at, &aw, &ad, &alift, &ah);
        if (!aw || al >= l + w || l >= al + aw || at >= t + d || t >= at + ad) continue;
        if (alift < lift || alift >= a->top + 5) continue;
        if (actor == 0) a->player = 1;
        if (a->na < 64) a->actors[a->na++] = actor;
    }
}

/* Whether it can cover the new footprint: the ground it moves onto suits it, nothing solid that isn't aboard is in the way. */
static int can_cover(cJSON *v, const Aboard *a, int tl, int tt, int tw, int td, int fl, int ft, int fw, int fd, char *why, size_t size)
{
    int ux = world_units_x(), uy = world_units_y(), lift = num(v, "Lift", 0), movement = movement_of(v);
    if (tl < 0 || tt < 0 || tl + tw > world_map_w() * ux || tt + td > world_map_h() * uy) { snprintf(why, size, "Blocked: the edge of the map."); return 0; }
    for (int y = tt; y < tt + td; y++)
        for (int x = tl; x < tl + tw; x++)
        {
            if (x >= fl && x < fl + fw && y >= ft && y < ft + fd) continue; /* already under it */
            if (lift == 0 && movement != MOVE_AIR)
            {
                int tile, walk, wet;
                world_ground(x / ux, y / uy, &tile, &walk, &wet);
                if (movement == MOVE_SEA && !wet) { snprintf(why, size, "Blocked: a ship sails only on water."); return 0; }
                if (movement == MOVE_LAND && (!tile || !walk || wet)) { snprintf(why, size, "Blocked: it can't go over that ground."); return 0; }
            }
            const char *what = world_blocked(x, y, 1, 1, lift + 1, a->top, a->uids, a->nu, a->actors, a->na);
            if (what) { snprintf(why, size, "Blocked: %s %s in the way.", what, strcmp(what, "you") ? "is" : "are"); return 0; }
        }
    return 1;
}

static void move_aboard(const Aboard *a, int dx, int dy)
{
    for (int i = 0; i < a->nu; i++) world_shift_thing(a->uids[i], dx, dy, 0);
    for (int i = 0; i < a->na; i++) world_shift_actor(a->actors[i], dx * 8.0, dy * 8.0, 0);
}

/* Barge_object::step: one 8px unit that way. */
static int step(cJSON *v, int dx, int dy, char *why, size_t size)
{
    Aboard *a = (Aboard *)malloc(sizeof(Aboard));
    gather(v, a);
    int l, t, w, d;
    foot(v, &l, &t, &w, &d);
    int ok = can_cover(v, a, l + dx, t + dy, w, d, l, t, w, d, why, size);
    if (ok) { set(v, "Left", l + dx); set(v, "Top", t + dy); move_aboard(a, dx, dy); }
    free(a);
    return ok;
}

/* Barge_object::face_direction: a quarter turn right or left, or right round, if there's room for its new footprint. */
static int turn(cJSON *v, int want, char *why, size_t size)
{
    int quarters = (((want - num(v, "Direction", DIR_S)) / 2) % 4 + 4) % 4;
    if (!quarters) return 1;
    Aboard *a = (Aboard *)malloc(sizeof(Aboard));
    gather(v, a);
    int l, t, w, d;
    foot(v, &l, &t, &w, &d);
    double cx = l + w / 2.0, cy = t + d / 2.0;
    int sideways = quarters % 2, nw = sideways ? d : w, nd = sideways ? w : d;
    int nl = (int)floor(cx - nw / 2.0 + 0.5), nt = (int)floor(cy - nd / 2.0 + 0.5);
    if (!can_cover(v, a, nl, nt, nw, nd, l, t, w, d, why, size))
    {
        if (!strncmp(why, "Blocked:", 8)) { char rest[200]; snprintf(rest, sizeof rest, "No room to turn:%s", why + 8); snprintf(why, size, "%s", rest); }
        free(a);
        return 0;
    }
    for (int i = 0; i < a->nu; i++)
    {
        int bl, bt, bw, bd, bz, bh;
        world_thing_box(a->uids[i], &bl, &bt, &bw, &bd, &bz, &bh);
        double x = bl + bw / 2.0, y = bt + bd / 2.0, mx, my;
        if (quarters == 1) { mx = cx - (y - cy); my = cy + (x - cx); }       /* a quarter turn right, as the screen is seen */
        else if (quarters == 2) { mx = 2 * cx - x; my = 2 * cy - y; }
        else { mx = cx + (y - cy); my = cy - (x - cx); }                      /* a quarter turn left */
        world_turn_thing(a->uids[i], quarters, mx, my);
    }
    for (int i = 0; i < a->na; i++)
    {
        int actor = a->actors[i];
        double px, py;
        world_actor_pixel(actor, &px, &py);
        double x = px / 8, y = py / 8, mx, my;
        if (quarters == 1) { mx = cx - (y - cy); my = cy + (x - cx); }
        else if (quarters == 2) { mx = 2 * cx - x; my = 2 * cy - y; }
        else { mx = cx + (y - cy); my = cy - (x - cx); }
        world_shift_actor(actor, mx * 8 - px, my * 8 - py, 0);
        world_actor_set_facing(actor, world_actor_facing_of(actor) + quarters * 2);
    }
    set(v, "Left", nl); set(v, "Top", nt); set(v, "Width", nw); set(v, "Depth", nd); set(v, "Direction", want);
    free(a);
    return 1;
}

/* A lift up or down with everything aboard, if nothing that isn't aboard is in the way. */
static int lift_by(cJSON *v, int dz)
{
    Aboard *a = (Aboard *)malloc(sizeof(Aboard));
    gather(v, a);
    int l, t, w, d, lift = num(v, "Lift", 0), bottom = lift + dz, ok = bottom >= 0;
    foot(v, &l, &t, &w, &d);
    if (ok && dz > 0 && world_blocked(l, t, w, d, a->top, a->top + dz, a->uids, a->nu, a->actors, a->na)) ok = 0; /* a roof, a bridge overhead */
    if (ok && dz < 0 && world_blocked(l, t, w, d, bottom, lift, a->uids, a->nu, a->actors, a->na)) ok = 0;   /* the ground, a roof below */
    if (ok)
    {
        set(v, "Lift", bottom);
        for (int i = 0; i < a->nu; i++) world_shift_thing(a->uids[i], 0, 0, dz);
        for (int i = 0; i < a->na; i++) world_shift_actor(a->actors[i], 0, 0, dz);
    }
    free(a);
    return ok;
}

/* ---------------- Driving ---------------- */

static cJSON *driving;        /* the vehicle the player has the controls of */
static int drivingFrom;       /* the piece they took them at */
static int seatToDriveFrom;   /* a driver's seat they're walking to */
static double driveUnits;
static int rolling = -1;      /* the rolling vehicle's id (-1: none) */

typedef struct Pilot { cJSON *v; double toX, toY, units; int token, landing; } Pilot;
static Pilot pilots[8]; static int pilotCount;
typedef struct Climb { cJSON *v; int to; } Climb;
static Climb climbs[8]; static int climbCount;
static double climbMs;

static int on_autopilot(cJSON *v) { for (int i = 0; i < pilotCount; i++) if (pilots[i].v == v) return 1; return 0; }

static void climb_to(cJSON *v, int lift)
{
    for (int i = 0; i < climbCount; i++) if (climbs[i].v == v) { climbs[i].to = lift; return; }
    if (climbCount < 8) climbs[climbCount++] = (Climb){ v, lift };
}
static int climbing(cJSON *v, int *to) { for (int i = 0; i < climbCount; i++) if (climbs[i].v == v) { *to = climbs[i].to; return 1; } return 0; }

/* An Air vehicle goes up to floor 11. */
static void take_off(cJSON *v) { if (movement_of(v) == MOVE_AIR && num(v, "Lift", 0) < FLY_LIFT) climb_to(v, FLY_LIFT); }

/* Barge_object::okay_to_land: every unit under it clear down to the same level ground, and not water - then it comes down there. */
static int land(cJSON *v)
{
    if (num(v, "Lift", 0) == 0) return 1;
    Aboard *a = (Aboard *)malloc(sizeof(Aboard));
    gather(v, a);
    int l, t, w, d, lift = num(v, "Lift", 0), level = -1;
    foot(v, &l, &t, &w, &d);
    for (int y = t; y < t + d; y++)
        for (int x = l; x < l + w; x++)
        {
            /* the highest solid under this unit */
            int top = 0, lo = 0, hi = lift;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (world_blocked(x, y, 1, 1, mid - 1, lift, a->uids, a->nu, a->actors, a->na)) lo = mid; else hi = mid - 1;
            }
            top = lo;
            if (top == 0)
            {
                int tile, walk, wet;
                world_ground(x / world_units_x(), y / world_units_y(), &tile, &walk, &wet);
                if (wet) { say("There's no place to land here: water below."); free(a); return 0; }
            }
            if (level < 0) level = top;
            else if (level != top) { say("There's no place to land here: the ground below isn't clear and level."); free(a); return 0; }
        }
    free(a);
    climb_to(v, level < 0 ? 0 : level);
    return 1;
}

/* Wheels turning and the moving sound while it rolls; the stop sound as it stops. */
static void set_rolling(cJSON *v, int on)
{
    int id = on ? num(v, "Id", -1) : -1;
    if (id == rolling) return;
    rolling = id;
    cJSON *s = settings_of(v);
    if (on) sound_play_ex(num(s, "MovingSoundResourceId", -1), 1.0, 1, SOUND_OWNER, 0);
    else { sound_stop_owner(SOUND_OWNER); sound_play(num(s, "StopSoundResourceId", -1)); }
    int l, t, w, d, near[MAX_ABOARD];
    foot(v, &l, &t, &w, &d);
    int n = world_things_in(l - 2, t - 2, w + 4, d + 4, near, MAX_ABOARD);
    for (int i = 0; i < n; i++)
    {
        if (world_thing_vehicle(near[i]) != num(v, "Id", -2) || role_of(near[i]) != ROLE_WHEEL) continue;
        cJSON *look = get(object_def(world_thing_object_id(near[i])), "MovingLook");
        if (!flag(look, "IsSet")) continue;
        world_thing_look(near[i], on ? sprite_find(str(look, "SpriteGroup"), str(look, "SpriteName")) : NULL);
    }
}

static void begin_driving(cJSON *v, int from)
{
    driving = v; drivingFrom = from; driveUnits = 0;
    sound_play(num(settings_of(v), "StartSoundResourceId", -1));
    take_off(v);
    char line[240];
    snprintf(line, sizeof line, "You have the controls of the %s: the direction keys drive it. %s to stop.", vehicle_name(v), role_of(from) == ROLE_SEAT ? "Get up" : "Double-click the controls again");
    say(line);
}

void vehicles_stop_driving(void)
{
    if (!driving) return;
    cJSON *v = driving;
    set_rolling(v, 0);
    driving = NULL; drivingFrom = 0;
    say("You let go of the controls.");
    if (movement_of(v) == MOVE_AIR && num(v, "Lift", 0) > 0 && !on_autopilot(v)) land(v); /* comes down if it can */
}

/* Ultima VII's deed: someone in the party carries it, if the vehicle needs one. */
static int has_deed(cJSON *v)
{
    int deed = num(settings_of(v), "DeedObjectId", -1);
    if (deed < 0 || items_party_has(CAT_OBJECT, deed)) return 1;
    const char *text = get(settings_of(v), "NeedDeedText") ? str(settings_of(v), "NeedDeedText") : "You need the deed to use this.";
    if (text[0]) { say(text); world_bark(0, text, 3000); }
    return 0;
}

static int open_dashboard(cJSON *v)
{
    int doll = num(settings_of(v), "DashboardDollId", -1);
    return doll >= 0 && dolls_open_form(doll, 0, 0, 0, 0);
}

/* Double-clicking a piece of a vehicle: its controls take or give up the controls; its driver's seat, sat in, drives. 0: not a vehicle's controls. */
int vehicles_use_part(int uid)
{
    cJSON *v = state_by_id(world_thing_vehicle(uid));
    if (!v) return 0;
    switch (role_of(uid))
    {
        case ROLE_CONTROLS:
            if (!has_deed(v)) return 1;
            if (open_dashboard(v)) return 1; /* its dashboard paperdoll: its buttons drive it */
            if (driving == v) { vehicles_stop_driving(); return 1; }
            if (driving) vehicles_stop_driving();
            begin_driving(v, uid);
            return 1;
        case ROLE_SEAT:
            if (driving == v && drivingFrom == uid) { vehicles_stop_driving(); world_stand_up(0); return 1; }
            if (!has_deed(v)) return 1;
            seatToDriveFrom = uid; /* drives once seated in it */
            return 0;              /* the chair's own sitting goes on */
    }
    return 0;
}

/* A frame while driving: the direction keys turn and move the vehicle (the driver stays at the controls). 0 when not driving. */
int vehicles_drive(int dx, int dy, double seconds)
{
    /* a seat taken to drive from starts the driving; getting up from it stops it */
    if (seatToDriveFrom && world_seated_on(0) == seatToDriveFrom)
    {
        cJSON *v = state_by_id(world_thing_vehicle(seatToDriveFrom));
        int seat = seatToDriveFrom;
        seatToDriveFrom = 0;
        if (v) { begin_driving(v, seat); open_dashboard(v); }
    }
    else if (seatToDriveFrom && !world_seated(0) && !world_walking(0)) seatToDriveFrom = 0; /* walked off instead */
    if (driving && drivingFrom && role_of(drivingFrom) == ROLE_SEAT && world_seated_on(0) != drivingFrom) vehicles_stop_driving();
    if (!driving) return 0;
    if (!alive_state(driving)) { driving = NULL; return 0; }
    if (on_autopilot(driving)) return 0; /* it's going somewhere by itself: the keys walk about it */
    if (!dx && !dy) { set_rolling(driving, 0); driveUnits = 0; return 1; }
    int want = dy < 0 ? DIR_N : dx > 0 ? DIR_E : dx < 0 ? DIR_W : DIR_S;
    char why[200];
    if (want != num(driving, "Direction", DIR_S))
    {
        if (!turn(driving, want, why, sizeof why)) { say(why); set_rolling(driving, 0); return 1; }
        driveUnits = 0;
        return 1; /* the turn is this frame's move */
    }
    driveUnits += speed_of(driving) * world_units_x() * seconds;
    int moved = 0;
    while (driveUnits >= 1)
    {
        driveUnits -= 1;
        if (!step(driving, dx, dy, why, sizeof why)) { say(why); driveUnits = 0; break; }
        moved = 1;
    }
    set_rolling(driving, moved || driveUnits > 0);
    return 1;
}

/* ---------------- Commands ---------------- */

/* The vehicle a command names: the built copy of it nearest the player. */
static cJSON *vehicle_for(int templateId)
{
    double px, py, best = 1e18;
    world_player_pixel(&px, &py);
    px /= 8; py /= 8;
    cJSON *v, *found = NULL;
    cJSON_ArrayForEach(v, states())
    {
        if (num(v, "TemplateId", -2) != templateId) continue;
        double dist = fabs(num(v, "Left", 0) + num(v, "Width", 0) / 2.0 - px) + fabs(num(v, "Top", 0) + num(v, "Depth", 0) / 2.0 - py);
        if (dist < best) { best = dist; found = v; }
    }
    return found;
}

static int first_piece(cJSON *v, int role)
{
    int l, t, w, d, near[MAX_ABOARD];
    foot(v, &l, &t, &w, &d);
    int n = world_things_in(l - 2, t - 2, w + 4, d + 4, near, MAX_ABOARD);
    for (int i = 0; i < n; i++) if (world_thing_vehicle(near[i]) == num(v, "Id", -2) && (role < 0 || role_of(near[i]) == role)) return near[i];
    return 0;
}

int vehicles_drive_command(int templateId)
{
    cJSON *v = vehicle_for(templateId);
    if (!v) { say("Drive Vehicle: there's no such vehicle on this map."); return 0; }
    if (driving) vehicles_stop_driving();
    int from = first_piece(v, ROLE_CONTROLS);
    if (!from) from = first_piece(v, ROLE_SEAT);
    if (!from) from = first_piece(v, -1);
    if (!from) return 0;
    begin_driving(v, from);
    if (role_of(from) == ROLE_SEAT)
    {
        /* put at the controls without sitting: getting up isn't what stops it */
        int other = 0, l, t, w, d, near[MAX_ABOARD];
        foot(v, &l, &t, &w, &d);
        int n = world_things_in(l - 2, t - 2, w + 4, d + 4, near, MAX_ABOARD);
        for (int i = 0; i < n && !other; i++) if (world_thing_vehicle(near[i]) == num(v, "Id", -2) && role_of(near[i]) != ROLE_SEAT) other = near[i];
        drivingFrom = other ? other : from;
    }
    return 1;
}

int vehicles_stop_command(void) { if (!driving) return 0; vehicles_stop_driving(); return 1; }

int vehicles_speed_command(int templateId, double speed)
{
    cJSON *v = vehicle_for(templateId);
    if (!v) return 0;
    set(v, "Speed", speed < 0 ? 0 : speed > 60 ? 60 : speed);
    return 1;
}

/* Travel Vehicle: off it goes to the spot (its middle there); flying ones take off first and land there. token: the event waiting (0: none). */
int vehicles_travel_command(int templateId, int x, int y, int token)
{
    cJSON *v = vehicle_for(templateId);
    if (!v) { say("Travel Vehicle: there's no such vehicle on this map."); return 0; }
    if (x < 0 || y < 0 || x >= world_map_w() || y >= world_map_h()) return 0;
    for (int i = 0; i < pilotCount; i++) if (pilots[i].v == v) pilots[i--] = pilots[--pilotCount];
    if (pilotCount >= 8) return 0;
    pilots[pilotCount++] = (Pilot){ v, (x + 0.5) * world_units_x(), (y + 0.5) * world_units_y(), 0, token, 0 };
    take_off(v);
    sound_play(num(settings_of(v), "StartSoundResourceId", -1));
    return 1;
}

/* ---------------- Each frame: autopilots, climbing ---------------- */

static void pilot_rolling(cJSON *v, int on) { if (driving == v && !on_autopilot(v)) return; set_rolling(v, on); }

static void finish(int i, int arrived)
{
    Pilot p = pilots[i];
    pilots[i] = pilots[--pilotCount];
    pilot_rolling(p.v, 0);
    if (!arrived && p.token) { char line[160]; snprintf(line, sizeof line, "The %s couldn't get there.", vehicle_name(p.v)); say(line); }
    if (p.token) events_doll_answered(p.token, arrived);
}

void vehicles_update(double ms)
{
    double seconds = ms / 1000;
    /* climbing or coming down: a lift every 60 ms */
    if (climbCount)
    {
        climbMs += ms;
        while (climbMs >= 60 && climbCount)
        {
            climbMs -= 60;
            for (int i = 0; i < climbCount; i++)
            {
                Climb *c = &climbs[i];
                int lift = alive_state(c->v) ? num(c->v, "Lift", 0) : c->to;
                if (lift == c->to) { climbs[i--] = climbs[--climbCount]; continue; }
                int dz = c->to > lift ? 1 : -1;
                if (!lift_by(c->v, dz)) { say(dz > 0 ? "It can't go any higher here." : "It can't come down any further here."); climbs[i--] = climbs[--climbCount]; }
            }
        }
    }
    else climbMs = 0;
    char why[200];
    for (int i = 0; i < pilotCount; i++)
    {
        Pilot *p = &pilots[i];
        cJSON *v = p->v;
        int to;
        if (!alive_state(v)) { finish(i--, 0); continue; }
        if (climbing(v, &to) && to > num(v, "Lift", 0)) continue; /* taking off first */
        if (p->landing) { if (!climbing(v, &to)) finish(i--, 1); continue; }
        double cx = num(v, "Left", 0) + num(v, "Width", 0) / 2.0, cy = num(v, "Top", 0) + num(v, "Depth", 0) / 2.0, rx = p->toX - cx, ry = p->toY - cy;
        if (fabs(rx) < 1 && fabs(ry) < 1)
        {
            pilot_rolling(v, 0);
            if (movement_of(v) == MOVE_AIR && num(v, "Lift", 0) > 0) { if (land(v)) { p->landing = 1; continue; } finish(i--, 0); continue; }
            finish(i--, 1);
            continue;
        }
        /* keep the heading while it still gets nearer that way; then the other side (one turn, not a zigzag) */
        int dir = num(v, "Direction", DIR_S), alongX0 = dir == DIR_E || dir == DIR_W;
        double ahead = alongX0 ? rx * (dir == DIR_E ? 1 : -1) : ry * (dir == DIR_S ? 1 : -1);
        int alongX = ahead >= 1 ? alongX0 : fabs(rx) >= 1 && (fabs(rx) >= fabs(ry) || fabs(ry) < 1);
        int dx = alongX ? (rx > 0) - (rx < 0) : 0, dy = alongX ? 0 : (ry > 0) - (ry < 0);
        int want = dy < 0 ? DIR_N : dx > 0 ? DIR_E : dx < 0 ? DIR_W : DIR_S;
        if (want != dir)
        {
            if (!turn(v, want, why, sizeof why)) { say(why); finish(i--, 0); continue; }
            p->units = 0;
            continue;
        }
        p->units += speed_of(v) * world_units_x() * seconds;
        int stopped = 0;
        while (p->units >= 1)
        {
            double left = alongX ? p->toX - (num(v, "Left", 0) + num(v, "Width", 0) / 2.0) : p->toY - (num(v, "Top", 0) + num(v, "Depth", 0) / 2.0);
            if (fabs(left) < 1) break;
            p->units -= 1;
            if (!step(v, dx, dy, why, sizeof why)) { say(why); finish(i--, 0); stopped = 1; break; }
        }
        if (!stopped) pilot_rolling(v, 1);
    }
}

void vehicles_reset(void) { driving = NULL; drivingFrom = seatToDriveFrom = 0; driveUnits = 0; rolling = -1; pilotCount = climbCount = 0; climbMs = 0; sound_stop_owner(SOUND_OWNER); }
