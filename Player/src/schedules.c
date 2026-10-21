/*
 * Every character's schedule, as the Windows engine runs it
 * (GameTestWindow.Schedules, from Exult):
 *  - each hour (Game_window::schedule_npcs) each NPC looks at its entry for
 *    the 3-hour period, or the closest earlier one (update_schedule) - not
 *    while told to Wait, nor while an event holds it;
 *  - set_schedule_and_loc: the same activity close to its spot isn't
 *    interrupted; otherwise it walks there (Walk_to_schedule: within 3 tiles
 *    is there; a leg that gets no nearer is a try; after 5, it's put there);
 *  - the activities: Stand, Wait, Loiter (12 tiles), Wander (legs of 32
 *    tiles within 128), Pace east-west / north-south, Dance, Sleep (lying
 *    down), Custom (its event commands); the rest stand for now;
 *  - dormant: off screen and more than a screenful away it does nothing; one
 *    walking to its schedule is put there (Walk_to_schedule::im_dormant).
 * Party members and the main player don't follow schedules.
 */
#include "schedules.h"
#include "events.h"
#include "world.h"
#include "platform.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define STD_DELAY 200
#define STOP_PAUSE_MS 3500

enum { S_HPACE = 1, S_VPACE = 2, S_DANCE = 4, S_FARM = 6, S_MINER = 8, S_STAND = 10, S_LOITER = 11, S_WANDER = 12, S_BLACKSMITH = 13,
       S_SLEEP = 14, S_WAIT = 15, S_SIT = 16, S_BAKE = 18, S_SEW = 19, S_LAB = 21, S_WAITER = 23, S_EAT = 26, S_CUSTOM = 0x80 };

typedef struct Entry { int period, type, x, y; cJSON *custom; int zone; } Entry;

typedef struct Npc
{
    int actor, characterId;
    Entry *entries; int entryCount;
    int activity;              /* 0 = none (walking to it, or no schedule) */
    int walkingToSchedule;
    const Entry *entry;
    int destX, destY, oldX, oldY, hasOld, homeX, homeY, centerX, centerY;
    int retries, phase, paceDirection, bestDistance;
    double waitMs;
    int walking;               /* on a path it was given */
    int dormant, missedHour;
} Npc;

static Npc *npcs; static int npcCount;
static int lastHour = -1;

static unsigned rng = 1234567u;
static int rnd(int n) { rng = rng * 1103515245u + 12345u; return n > 0 ? (int)((rng >> 8) % (unsigned)n) : 0; }

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }

/* Exult tiles (8 pixels) between the character and a cell's middle, the larger of the two axes. */
static int tile_distance(int actor, int x, int y)
{
    double px, py;
    world_actor_pixel(actor, &px, &py);
    double dx = fabs(x * world_tile_w() + world_tile_w() / 2.0 - px), dy = fabs(y * world_tile_h() + world_tile_h() / 2.0 - py);
    return (int)floor(fmax(dx, dy) / 8 + 0.5);
}

static int cells_for_tiles(int tiles) { int c = (int)floor(tiles * 8.0 / world_tile_w() + 0.5); return c < 1 ? 1 : c; }

void schedules_load(void)
{
    cJSON *project = get((cJSON *)world_json(), "Project"), *c;
    cJSON *characters = get(get(project, "Characters"), "Characters");
    npcs = (Npc *)calloc((size_t)world_actor_count() + 1, sizeof(Npc));
    for (int a = 1; a < world_actor_count(); a++)
    {
        int id = world_actor_character(a);
        cJSON *data = NULL;
        cJSON_ArrayForEach(c, characters) if (num(c, "Id", -2) == id) data = c;
        Npc *n = &npcs[npcCount++];
        n->actor = a; n->characterId = id; n->paceDirection = 1;
        world_actor_cell(a, &n->homeX, &n->homeY, &(int){ 0 });
        if (!data || cJSON_IsTrue(get(data, "IsInParty")) || cJSON_IsTrue(get(data, "IsTestPlayer"))) continue;
        cJSON *list = get(data, "Schedules"), *e;
        n->entries = (Entry *)calloc((size_t)cJSON_GetArraySize(list) + 1, sizeof(Entry));
        cJSON_ArrayForEach(e, list)
        {
            Entry *en = &n->entries[n->entryCount++];
            en->period = num(e, "Period", 0); en->type = num(e, "Type", S_STAND);
            en->x = num(e, "X", -1); en->y = num(e, "Y", -1);
            en->custom = get(e, "CustomCommands");
            en->zone = num(e, "ZoneId", -1) >= 0 || (cJSON_GetStringValue(get(e, "ZonePurpose")) && cJSON_GetStringValue(get(e, "ZonePurpose"))[0]);
        }
    }
}

static Npc *npc_of(int actor) { for (int i = 0; i < npcCount; i++) if (npcs[i].actor == actor) return &npcs[i]; return NULL; }

static void spot_of(const Npc *n, const Entry *e, int *x, int *y)
{
    if (e->zone) events_log_once(300, "A schedule goes to a zone - zones aren't in the player yet (its cell, or its placed spot, instead).");
    if (e->x >= 0 && e->y >= 0)
    {
        *x = e->x < 0 ? 0 : e->x >= world_map_w() ? world_map_w() - 1 : e->x;
        *y = e->y < 0 ? 0 : e->y >= world_map_h() ? world_map_h() - 1 : e->y;
        return;
    }
    *x = n->homeX; *y = n->homeY;
}

static void stop_walking(Npc *n) { world_walk_cancel(n->actor); n->walking = 0; n->waitMs = -1; }

static void start_path(Npc *n, int x, int y)
{
    if (world_walk_to(n->actor, x, y, 0, 0)) n->walking = 1;
    else n->waitMs = 2 * STD_DELAY + rnd(2000);
}

/* Actor::set_schedule_and_loc. */
static void set_schedule_and_loc(Npc *n, const Entry *e, int x, int y, int delayMs)
{
    if (n->entry == e && (n->walkingToSchedule || (n->activity == e->type && tile_distance(n->actor, x, y) <= 16))) return; /* don't interrupt */
    stop_walking(n);
    n->entry = e; n->destX = x; n->destY = y;
    n->activity = 0;
    n->walkingToSchedule = 1;
    n->retries = 0; n->bestDistance = 1 << 30;
    n->waitMs = (delayMs >= 0 ? delayMs : rnd(5000)) + rnd(1000); /* Walk_to_schedule: 0-5 s, then 0-1 s more */
}

/* Npc_actor::update_schedule. */
static void update_schedule(Npc *n, int period, int delayMs)
{
    if (!n->entryCount) return;
    const Entry *e = NULL;
    for (int i = 0; i < n->entryCount; i++) if (n->entries[i].period == period) { e = &n->entries[i]; break; }
    if (!e)
    {
        /* the closest earlier one - unless already in it */
        int best = 99;
        for (int i = 0; i < n->entryCount; i++) { int back = (period - n->entries[i].period + 8) % 8; if (back < best) { best = back; e = &n->entries[i]; } }
        int sx, sy;
        spot_of(n, e, &sx, &sy);
        if ((n->activity == e->type || (n->walkingToSchedule && n->entry && n->entry->type == e->type)) && n->hasOld && n->oldX == sx && n->oldY == sy) return;
    }
    int sx, sy;
    spot_of(n, e, &sx, &sy);
    n->oldX = sx; n->oldY = sy; n->hasOld = 1;
    set_schedule_and_loc(n, e, sx, sy, delayMs);
}

/* Arrived (or put there): the activity itself. */
static void begin_activity(Npc *n)
{
    n->walkingToSchedule = 0;
    const Entry *e = n->entry;
    if (!e) return;
    n->activity = e->type;
    world_actor_cell(n->actor, &n->centerX, &n->centerY, &(int){ 0 });
    n->phase = 0;
    world_walk_blocked(n->actor);
    int facing = world_actor_facing(n->actor);
    switch (e->type)
    {
        case S_SLEEP:
            /* Sleep_schedule: a bed within 24 tiles (beds come with furniture); none: lies down here */
            if (!world_set_pose(n->actor, "Sleeping", facing)) world_set_pose(n->actor, "Idle", facing);
            break;
        case S_DANCE: world_set_pose(n->actor, "Dancing", facing); n->waitMs = 2 * STD_DELAY; break;
        case S_LOITER: case S_WANDER: world_set_pose(n->actor, "Idle", facing); n->waitMs = rnd(2000); break;
        case S_HPACE: case S_VPACE: world_set_pose(n->actor, "Idle", facing); n->waitMs = STD_DELAY; break;
        case S_CUSTOM:
            world_set_pose(n->actor, "Idle", facing);
            events_run_commands(e->custom, n->actor);
            break;
        case S_SIT: case S_WAITER: case S_EAT: case S_FARM: case S_MINER: case S_BLACKSMITH: case S_BAKE: case S_SEW: case S_LAB:
            events_log_once(301, "Sit, Waiter, Eat at Inn and the work schedules aren't in the player yet - they stand at their spot.");
            world_set_pose(n->actor, "Idle", facing);
            break;
        default: world_set_pose(n->actor, "Idle", facing); break; /* Stand, Wait */
    }
}

/* Walk_to_schedule::now_what. */
static void walk_to_schedule(Npc *n)
{
    int distance = tile_distance(n->actor, n->destX, n->destY);
    if (distance <= 3) { begin_activity(n); return; }
    if (distance < n->bestDistance) { n->bestDistance = distance; n->retries = 0; }
    else n->retries++;
    if (n->retries >= 5) { world_teleport(n->actor, n->destX, n->destY); begin_activity(n); return; } /* no way there: Exult's jump */
    if (world_walk_to(n->actor, n->destX, n->destY, 0, 0)) n->walking = 1;
    else n->waitMs = 1000 + 500 * n->retries;
}

static void loiter(Npc *n, int dist)
{
    int x = n->centerX - dist + rnd(2 * dist + 1), y = n->centerY - dist + rnd(2 * dist + 1);
    if (x >= 0 && y >= 0 && x < world_map_w() && y < world_map_h() && world_walk_to(n->actor, x, y, 0, 0)) n->walking = 1;
    else n->waitMs = 2 * STD_DELAY + rnd(2000);
}

static int clampi(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

static void wander(Npc *n)
{
    int leg = cells_for_tiles(32), dist = cells_for_tiles(128), cx, cy;
    world_actor_cell(n->actor, &cx, &cy, &(int){ 0 });
    int x = clampi(cx - leg + rnd(2 * leg + 1), n->centerX - dist, n->centerX + dist), y = clampi(cy - leg + rnd(2 * leg + 1), n->centerY - dist, n->centerY + dist);
    x = clampi(x, 0, world_map_w() - 1); y = clampi(y, 0, world_map_h() - 1);
    if (world_walk_to(n->actor, x, y, 0, 0)) n->walking = 1;
    else n->waitMs = 250 + rnd(3000);
}

static int quarter_turn(int facing) { return facing == 0 ? 2 : facing == 2 ? 4 : facing == 4 ? 6 : 0; }

/* Pace_schedule: along the line until blocked, stand, turn round in two quarter turns, back. */
static void pace(Npc *n, int horizontal)
{
    switch (n->phase)
    {
        case 0: n->phase = 1; n->waitMs = STD_DELAY; break;
        case 1:
        {
            if (world_walk_blocked(n->actor)) { n->phase = 2; n->waitMs = STD_DELAY; break; }
            int cx, cy;
            world_actor_cell(n->actor, &cx, &cy, &(int){ 0 });
            int nx = cx + (horizontal ? n->paceDirection : 0), ny = cy + (horizontal ? 0 : n->paceDirection);
            if (nx >= 0 && ny >= 0 && nx < world_map_w() && ny < world_map_h() && world_can_stand(n->actor, nx, ny, 0) && world_walk_to(n->actor, nx, ny, 0, 0)) n->walking = 1;
            else { n->phase = 2; n->waitMs = STD_DELAY; }
            break;
        }
        case 2: n->phase = 3; world_set_pose(n->actor, "Idle", -1); n->waitMs = 2 * STD_DELAY; break;
        case 3: n->phase = 4; world_set_pose(n->actor, "Idle", quarter_turn(world_actor_facing(n->actor))); n->waitMs = 2 * STD_DELAY; break;
        default:
            n->paceDirection = -n->paceDirection;
            world_set_pose(n->actor, "Idle", horizontal ? (n->paceDirection > 0 ? 2 : 6) : (n->paceDirection > 0 ? 4 : 0));
            n->phase = 1;
            n->waitMs = 2 * STD_DELAY;
            break;
    }
}

static void dance(Npc *n)
{
    static const int DIRS[4] = { 0, 2, 4, 6 };
    if (rnd(4) == 0)
    {
        int x = n->centerX + rnd(3) - 1, y = n->centerY + rnd(3) - 1, cx, cy;
        world_actor_cell(n->actor, &cx, &cy, &(int){ 0 });
        if ((x != cx || y != cy) && x >= 0 && y >= 0 && x < world_map_w() && y < world_map_h() && world_walk_to(n->actor, x, y, 0, 0)) { n->walking = 1; return; }
    }
    world_set_pose(n->actor, "Dancing", DIRS[rnd(4)]);
    n->waitMs = 2 * STD_DELAY + rnd(4 * STD_DELAY);
}

static void now_what(Npc *n)
{
    if (n->walkingToSchedule) { walk_to_schedule(n); return; }
    switch (n->activity)
    {
        case S_LOITER: loiter(n, cells_for_tiles(12)); break;
        case S_WANDER: wander(n); break;
        case S_HPACE: pace(n, 1); break;
        case S_VPACE: pace(n, 0); break;
        case S_DANCE: dance(n); break;
        default: break;
    }
}

/* FinishPath: a walk done (or stopped): the pose for the activity, and the pause before what's next. */
static void finish_path(Npc *n)
{
    n->walking = 0;
    world_set_pose(n->actor, n->activity == S_DANCE ? "Dancing" : "Idle", -1);
    n->waitMs = n->activity == S_LOITER ? 2 * STD_DELAY + rnd(2000) : n->activity == S_WANDER ? rnd(2000) : n->activity == S_DANCE ? 2 * STD_DELAY : STD_DELAY;
    /* stopped walking: it stands a moment, long enough to be clicked */
    if (n->waitMs >= 0 && n->waitMs < STOP_PAUSE_MS) n->waitMs = STOP_PAUSE_MS;
}

/* Dormant: off screen and more than a screenful away - it does nothing (one walking to its schedule is put there). */
static int keep_dormant(Npc *n)
{
    int exempt = events_actor_busy(n->actor) || events_actor_held(n->actor);
    double px, py;
    world_actor_pixel(n->actor, &px, &py);
    int onScreen = world_on_screen(px, py, 6 * 8);
    if (n->dormant)
    {
        if (!onScreen && !exempt) return 1;
        n->dormant = 0;
        if (!n->walking && n->waitMs < 500) n->waitMs = 500;
        return 0;
    }
    if (exempt || onScreen) return 0;
    double cx, cy;
    world_view_center(&cx, &cy);
    if (fmax(fabs(px - cx), fabs(py - cy)) <= SCREEN_W + 8) return 0;
    stop_walking(n);
    n->dormant = 1;
    if (n->walkingToSchedule && n->entry)
    {
        world_teleport(n->actor, n->destX, n->destY);
        begin_activity(n);
        stop_walking(n);
    }
    return 1;
}

void schedules_start(void)
{
    lastHour = events_hour();
    for (int i = 0; i < npcCount; i++) update_schedule(&npcs[i], lastHour / 3, -1);
}

void schedules_update(double ms)
{
    int hour = events_hour();
    if (lastHour < 0) lastHour = hour;
    if (hour != lastHour)
    {
        /* Game_window::schedule_npcs: each hour; those told to Wait are left alone, those an event holds wait till it lets go */
        lastHour = hour;
        for (int i = 0; i < npcCount; i++)
        {
            Npc *n = &npcs[i];
            if (events_actor_held(n->actor)) { n->missedHour = 1; continue; }
            if (n->activity == S_WAIT) continue;
            update_schedule(n, hour / 3, hour % 3 == 0 ? -1 : 0);
        }
    }
    for (int i = 0; i < npcCount; i++)
    {
        Npc *n = &npcs[i];
        if (!n->entryCount) continue;
        if (keep_dormant(n)) continue;
        if (events_actor_busy(n->actor) || events_actor_held(n->actor)) { n->walking = 0; continue; } /* an event has them: their schedule waits */
        if (n->walking)
        {
            if (world_walking(n->actor)) continue;
            finish_path(n);
            continue;
        }
        if (n->waitMs < 0) continue;
        if ((n->waitMs -= ms) > 0) continue;
        n->waitMs = -1;
        now_what(n);
    }
}

/* An event let them go: back to their schedule (the hour they missed, if any). */
void schedules_let_go(int actor)
{
    Npc *n = npc_of(actor);
    if (!n) return;
    if (n->missedHour) { n->missedHour = 0; update_schedule(n, events_hour() / 3, 0); }
    if (!n->walking) n->waitMs = STD_DELAY;
}

/* Change Behavior: an activity right where it is (till the schedule next changes), or back to the schedule now. */
int schedules_change_behavior(int actor, const char *behavior)
{
    Npc *n = npc_of(actor);
    if (!n) return 0;
    stop_walking(n);
    n->walkingToSchedule = 0;
    if (!strcmp(behavior, "Resume"))
    {
        n->entry = NULL; n->activity = 0; n->hasOld = 0;
        update_schedule(n, events_hour() / 3, 0);
        return 1;
    }
    static const struct { const char *name; int type; } TYPES[] = {
        { "HorizPace", S_HPACE }, { "VertPace", S_VPACE }, { "Dance", S_DANCE }, { "Stand", S_STAND }, { "Loiter", S_LOITER }, { "Wander", S_WANDER },
        { "Sleep", S_SLEEP }, { "Wait", S_WAIT }, { "Sit", S_SIT }, { "Waiter", S_WAITER }, { "EatAtInn", S_EAT }, { "Custom", S_CUSTOM },
        { "Farm", S_FARM }, { "Miner", S_MINER }, { "Blacksmith", S_BLACKSMITH }, { "Bake", S_BAKE }, { "Sew", S_SEW }, { "Lab", S_LAB } };
    for (size_t i = 0; i < sizeof TYPES / sizeof TYPES[0]; i++)
        if (!strcmp(TYPES[i].name, behavior))
        {
            static Entry made[64]; static int madeCount;
            Entry *e = &made[madeCount++ % 64];
            memset(e, 0, sizeof *e);
            e->period = -1; e->type = TYPES[i].type;
            world_actor_cell(actor, &e->x, &e->y, &(int){ 0 });
            n->entry = e; n->destX = e->x; n->destY = e->y;
            begin_activity(n);
            return 1;
        }
    return 0;
}
