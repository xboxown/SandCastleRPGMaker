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
#include "sound.h"
#include "work.h"
#include "magic.h"
#include "events.h"
#include "world.h"
#include "platform.h"
#include "items.h"
#include "vitals.h"
#include "combat.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define STD_DELAY 200
#define STOP_PAUSE_MS 3500

enum { S_HPACE = 1, S_VPACE = 2, S_DANCE = 4, S_FARM = 6, S_MINER = 8, S_STAND = 10, S_LOITER = 11, S_WANDER = 12, S_BLACKSMITH = 13,
       S_SLEEP = 14, S_WAIT = 15, S_SIT = 16, S_BAKE = 18, S_SEW = 19, S_LAB = 21, S_WAITER = 23, S_EAT = 26, S_CUSTOM = 0x80 };

typedef struct Entry { int period, type, x, y; cJSON *custom; int zone; cJSON *json; } Entry;

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
    int dormant, missedHour, joined;
    int meal, mealFood;        /* MealStep (GameTestWindow.Meals) and the food it's going to */
    double mealCheckMs, ruleCheckMs;
    int rule;                  /* the When... rule it follows (+1; 0: its timetable) */
    Entry *ruleEntries; int ruleCount;
    int waiter, customer, dish;/* a waiter's step, its customer (actor), the dish it brings */
    int served[8], servedCount;/* dishes put in front of it (uids) */
} Npc;

static Npc *npcs; static int npcCount;
static int leaderOf[256];          /* per actor: the actor it follows + 1 (0: nobody) */
static unsigned char sittingWithPlayer[256]; /* party members who sat because the main character did */
static double seatLookAgainAt[256], followClockMs;
static long untilMinute[256];      /* per actor: when a Change Schedule for some hours ends (0: never) */
static double followThinkMs;
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
    double dx = fabs(world_wrap_dx(x * world_tile_w() + world_tile_w() / 2.0 - px)), dy = fabs(world_wrap_dy(y * world_tile_h() + world_tile_h() / 2.0 - py));
    return (int)floor(fmax(dx, dy) / 8 + 0.5);
}

static int cells_for_tiles(int tiles) { int c = (int)floor(tiles * 8.0 / world_tile_w() + 0.5); return c < 1 ? 1 : c; }

void schedules_load(void)
{
    schedules_house_keys();
    combat_doors_init(); /* hostile NPCs break doors down */
    cJSON *project = get((cJSON *)world_json(), "Project"), *c;
    cJSON *characters = get(get(project, "Characters"), "Characters");
    free(npcs); npcCount = 0; lastHour = -1; followThinkMs = 0;
    memset(leaderOf, 0, sizeof leaderOf); memset(untilMinute, 0, sizeof untilMinute); memset(sittingWithPlayer, 0, sizeof sittingWithPlayer); memset(seatLookAgainAt, 0, sizeof seatLookAgainAt);
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
            en->json = e;
        }
        /* its When... rules: each one's line */
        cJSON *rules = get(data, "ScheduleRules"), *r;
        n->ruleEntries = (Entry *)calloc((size_t)cJSON_GetArraySize(rules) + 1, sizeof(Entry));
        cJSON_ArrayForEach(r, rules)
        {
            cJSON *e2 = get(r, "Do");
            Entry *en = &n->ruleEntries[n->ruleCount++];
            en->period = -1; en->type = num(e2, "Type", S_STAND);
            en->x = num(e2, "X", -1); en->y = num(e2, "Y", -1);
            en->custom = get(e2, "CustomCommands");
            en->json = e2;
            en->zone = num(e2, "ZoneId", -1) >= 0 || (cJSON_GetStringValue(get(e2, "ZonePurpose")) && cJSON_GetStringValue(get(e2, "ZonePurpose"))[0]);
            en->zone = num(e, "ZoneId", -1) >= 0 || (cJSON_GetStringValue(get(e, "ZonePurpose")) && cJSON_GetStringValue(get(e, "ZonePurpose"))[0]);
        }
    }
}

static Npc *npc_of(int actor) { for (int i = 0; i < npcCount; i++) if (npcs[i].actor == actor) return &npcs[i]; return NULL; }

static int zone_spot(const Npc *n, cJSON *line, int *x, int *y);
static void waiter_now_what(Npc *n);
static void inn_customer_now_what(Npc *n);
static void check_rules(Npc *n, double ms);
static void check_hunger(Npc *n, double ms);
static void meal_now_what(Npc *n);

static void spot_of(const Npc *n, const Entry *e, int *x, int *y)
{
    if (e->zone && zone_spot(n, e->json, x, y)) return;
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
    work_schedule_changed(n->actor, e->type); /* a round of work stops; tools it borrowed go back first */
    world_stand_up(n->actor); /* off the chair / out of bed */
    stop_walking(n);
    n->entry = e; n->destX = x; n->destY = y;
    if (world_actor_stationary(n->actor)) { int cx, cy, cf; world_actor_cell(n->actor, &cx, &cy, &cf); n->destX = cx; n->destY = cy; } /* does it where it stands */
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
            world_lock_own_doors(n->actor, cells_for_tiles(24)); /* LockHouseDoorsForNight */
            /* Sleep_schedule: the nearest free bed within 24 tiles; none: lies down here */
            if (world_go_to_furniture(n->actor, 1, 0)) n->walking = world_walking(n->actor);
            else if (!world_set_pose(n->actor, "Sleeping", facing)) world_set_pose(n->actor, "Idle", facing);
            break;
        case S_SIT:
            world_set_pose(n->actor, "Idle", facing);
            if (world_go_to_furniture(n->actor, 0, 0)) n->walking = world_walking(n->actor);
            else n->waitMs = 1000 + rnd(1000); /* Sit_schedule: none free, try again */
            break;
        case S_DANCE: world_set_pose(n->actor, "Dancing", facing); n->waitMs = 2 * STD_DELAY; break;
        case S_LOITER: case S_WANDER: world_set_pose(n->actor, "Idle", facing); n->waitMs = rnd(2000); break;
        case S_HPACE: case S_VPACE: world_set_pose(n->actor, "Idle", facing); n->waitMs = STD_DELAY; break;
        case S_CUSTOM:
            world_set_pose(n->actor, "Idle", facing);
            events_run_commands(e->custom, n->actor);
            break;
        case S_FARM: case S_MINER: case S_BLACKSMITH: case S_BAKE: case S_SEW: case S_LAB:
            world_set_pose(n->actor, "Idle", facing);
            n->waitMs = STD_DELAY; /* to work */
            break;
        case S_WAITER: n->waiter = 0; n->customer = -1; world_set_pose(n->actor, "Idle", facing); n->waitMs = STD_DELAY; break;
        case S_EAT: world_set_pose(n->actor, "Idle", facing); n->waitMs = STD_DELAY; break;
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
    int leg = cells_for_tiles(32), dist = n->entry && num(n->entry->json, "WorkAreaTiles", 12) == 0 ? world_map_w() + world_map_h() : cells_for_tiles(128), cx, cy; /* a spawn point's free copies: no bounds */
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
        case S_LOITER: loiter(n, cells_for_tiles(n->entry && num(n->entry->json, "WorkAreaTiles", 12) > 0 ? num(n->entry->json, "WorkAreaTiles", 12) : 12)); break; /* Exult's 12 tiles; a spawn point's copies: its reach */
        case S_WANDER: wander(n); break;
        case S_HPACE: pace(n, 1); break;
        case S_VPACE: pace(n, 0); break;
        case S_DANCE: dance(n); break;
        case S_WAITER: waiter_now_what(n); break;
        case S_EAT: inn_customer_now_what(n); break;
        case S_FARM: case S_MINER: case S_BLACKSMITH: case S_BAKE: case S_SEW: case S_LAB:
            if (n->entry) work_now_what(n->actor, n->activity, n->entry->json, n->destX, n->destY);
            n->waitMs = 1000; /* the work's own rounds and pauses */
            break;
        case S_SIT:
            if (!world_seated(n->actor))
            {
                if (world_go_to_furniture(n->actor, 0, 0)) n->walking = world_walking(n->actor);
                else n->waitMs = 1000 + rnd(1000);
            }
            break;
        case S_SLEEP:
            if (!world_seated(n->actor) && !world_go_to_furniture(n->actor, 1, 0)) world_set_pose(n->actor, "Sleeping", -1); /* no bed: the floor */
            else if (!world_seated(n->actor)) n->walking = world_walking(n->actor);
            break;
        default: break;
    }
}

/* FinishPath: a walk done (or stopped): the pose for the activity, and the pause before what's next. */
static void finish_path(Npc *n)
{
    n->walking = 0;
    int seat = world_arrive_furniture(n->actor); /* was heading for a seat or bed: sits / lies down */
    if (seat > 0) { if (n->meal || n->activity == S_EAT) n->waitMs = 1000; return; } /* at the inn: waiting to be served */
    if (seat < 0) { n->waitMs = 1000 + rnd(1000); return; } /* taken meanwhile: look again shortly */
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
    for (int a = 1; a < world_actor_count() && a < 256; a++)
        if (!world_actor_gone(a) && items_in_party(world_actor_character(a))) { leaderOf[a] = 1; Npc *n = npc_of(a); if (n) n->joined = 1; }
    lastHour = events_hour();
    for (int i = 0; i < npcCount; i++) update_schedule(&npcs[i], lastHour / 3, -1);
}

static void update_followers(double ms);
int schedules_following(int actor);

void schedules_update(double ms)
{
    update_followers(ms);
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
            if (n->joined || schedules_following(n->actor)) continue;
            if (n->activity == S_WAIT) continue;
            update_schedule(n, hour / 3, hour % 3 == 0 ? -1 : 0);
        }
    }
    for (int i = 0; i < npcCount; i++)
    {
        Npc *n = &npcs[i];
        if (!n->entryCount || n->joined || schedules_following(n->actor)) continue; /* the party and followers have no schedule */
        if (keep_dormant(n)) continue;
        if (magic_frozen(n->actor)) continue; /* Time Stop */
        if (events_actor_busy(n->actor) || events_actor_held(n->actor)) { n->walking = 0; continue; } /* an event has them: their schedule waits */
        if (world_actor_gone(n->actor) || vitals_incapacitated(n->characterId)) continue; /* dead, knocked out, asleep or paralyzed: does nothing */
        if (combat_busy(n->actor)) { n->walking = 0; continue; } /* fighting, fleeing or hounding: its schedule waits */
        if (work_busy(n->actor)) continue; /* at work (or tidying up after it) */
        check_rules(n, ms);
        check_hunger(n, ms);
        if (n->walking)
        {
            if (world_walking(n->actor)) continue;
            finish_path(n);
            continue;
        }
        if (n->waitMs < 0) continue;
        if ((n->waitMs -= ms) > 0) continue;
        n->waitMs = -1;
        if (n->meal) meal_now_what(n);
        else now_what(n);
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
    work_schedule_changed(actor, 0);
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

/* ---------------- Followers (Join Party, Follow) and timed schedules (Change Schedule) ---------------- */


/* Join Party / Leave: a party member has no schedule (Exult) and walks with the main character; one who leaves goes back to its day. */
void schedules_join(int actor, int join)
{
    Npc *n = npc_of(actor);
    if (actor <= 0 || actor >= 256) return;
    leaderOf[actor] = 0;
    world_walk_cancel(actor);
    if (!n) return;
    n->walking = 0;
    if (join)
    {
        n->entry = NULL; n->activity = 0; n->walkingToSchedule = 0; n->waitMs = -1;
        leaderOf[actor] = 0 + 1; /* the main character */
        n->entryCount = n->entryCount; /* its timetable is kept for when it leaves */
        n->joined = 1;
    }
    else
    {
        n->joined = 0;
        update_schedule(n, events_hour() / 3, 0);
    }
}

/* Follow: walk after someone (leader -1: stop following). */
void schedules_follow(int actor, int leader)
{
    Npc *n = npc_of(actor);
    if (actor <= 0 || actor >= 256) return;
    if (leader < 0) { leaderOf[actor] = 0; world_walk_cancel(actor); if (n) n->walking = 0; return; }
    if (n) { n->entry = NULL; n->activity = 0; n->walkingToSchedule = 0; }
    leaderOf[actor] = leader + 1;
}

void schedules_until(int actor, long minute) { if (actor > 0 && actor < 256) untilMinute[actor] = minute; }

/* GameTestWindow.SitsWithPlayer: the main character sat down on a chair - a party member following them looks for a seat too,
   as Exult's Sit_schedule::set_action does: the closest chair within 24 tiles with a free seat it can walk to, walked to and
   sat on. None free: it stays standing near them (following as before) and looks again every 1-2 seconds (Sit_schedule::now_what:
   try again later). The main character getting up: those who sat with them get up and follow. 1 while sitting or on the way. */
static int sits_with_player(int a)
{
    int playerSits = world_seated(0) && !world_in_bed(0);
    if (!playerSits)
    {
        seatLookAgainAt[a] = 0;
        if (sittingWithPlayer[a])
        {
            sittingWithPlayer[a] = 0;
            if (world_heading_to_seat(a)) world_walk_cancel(a);
            world_stand_up(a);
        }
        return 0;
    }
    if (!items_in_party(world_actor_character(a))) return 0;
    if (world_seated(a) || world_heading_to_seat(a)) sittingWithPlayer[a] = 1; /* gets up with them, however it came to sit */
    if (world_seated(a)) return 1;
    if (world_heading_to_seat(a))
    {
        if (!world_walking(a) && world_arrive_furniture(a) < 0) { seatLookAgainAt[a] = followClockMs + 1000 + rand() % 1000; return 0; } /* taken meanwhile */
        return 1;
    }
    if (seatLookAgainAt[a] > followClockMs) return 0; /* none free last time: standing near, as before */
    if (world_go_to_chair_within(a, 24)) { sittingWithPlayer[a] = 1; seatLookAgainAt[a] = 0; return 1; }
    seatLookAgainAt[a] = followClockMs + 1000 + rand() % 1000;
    return 0;
}

/* UpdateFollowers: every 2/5 s, a follower more than 2 cells from its leader walks up beside it. */
static void update_followers(double ms)
{
    followClockMs += ms;
    if ((followThinkMs -= ms) > 0) return;
    followThinkMs = 400;
    long now = events_total_minutes();
    for (int a = 1; a < 256 && a < world_actor_count(); a++)
    {
        if (untilMinute[a] && now >= untilMinute[a]) { untilMinute[a] = 0; schedules_change_behavior(a, "Resume"); }
        int leader = leaderOf[a] - 1;
        if (leader < 0 || world_actor_gone(a) || world_actor_gone(leader)) continue;
        if (events_actor_busy(a) || combat_busy(a)) continue; /* carrying out an event's command, or fighting: it isn't pulled back */
        if (leader == 0 && sits_with_player(a)) continue; /* sitting with them, or on the way to a chair */
        int lx, ly, lf, x, y, f;
        world_actor_cell(leader, &lx, &ly, &lf);
        world_actor_cell(a, &x, &y, &f);
        if ((abs(lx - x) > abs(ly - y) ? abs(lx - x) : abs(ly - y)) <= 2) continue;
        if (!world_walking(a)) world_walk_to(a, lx, ly, lf, 1);
    }
}

int schedules_following(int actor) { return actor > 0 && actor < 256 && leaderOf[actor]; }

int schedules_sleeping(int actor) { Npc *n = npc_of(actor); return n && n->activity == S_SLEEP; }

/* A fight over: back to its normal schedule. */
void schedules_resume(int actor)
{
    Npc *n = npc_of(actor);
    if (!n || !n->entryCount || n->joined || schedules_following(actor)) return;
    n->walking = 0;
    update_schedule(n, events_hour() / 3, 0);
}

/* ---------------- Zones, When... rules, meals, the inn, waiters, house keys (GameTestWindow.Zones / Meals) ---------------- */

static cJSON *project_json(void) { return get((cJSON *)world_json(), "Project"); }
static const char *text_of(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }
static int flag_of(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static cJSON *character_of(int id) { cJSON *c; cJSON_ArrayForEach(c, get(get(project_json(), "Characters"), "Characters")) if (num(c, "Id", -2) == id) return c; return NULL; }
static cJSON *object_of(int id) { cJSON *o; cJSON_ArrayForEach(o, get(get(project_json(), "Objects"), "Objects")) if (num(o, "Id", -2) == id) return o; return NULL; }
static int same_text(const char *a, const char *b)
{
    while (*a == ' ') a++;
    while (*b == ' ') b++;
    for (; *a || *b; a++, b++) { char x = *a >= 'A' && *a <= 'Z' ? (char)(*a + 32) : *a, y = *b >= 'A' && *b <= 'Z' ? (char)(*b + 32) : *b; if (x != y) return (x == ' ' && !y) || (!x && y == ' '); }
    return 1;
}

/* ZoneSpotOf: the middle of the line's zone - by its id, or the zone of its purpose nearest to where it is now. */
static int zone_spot(const Npc *n, cJSON *line, int *x, int *y)
{
    cJSON *z, *best = NULL;
    int zoneId = num(line, "ZoneId", -1), bestD = 1 << 30, ax, ay, af;
    world_actor_cell(n->actor, &ax, &ay, &af);
    const char *purpose = text_of(line, "ZonePurpose");
    cJSON_ArrayForEach(z, get(get(project_json(), "Map"), "Zones"))
    {
        int cx = num(z, "X", 0) + num(z, "Width", 1) / 2, cy = num(z, "Y", 0) + num(z, "Height", 1) / 2;
        if (zoneId >= 0) { if (num(z, "Id", -2) == zoneId) { best = z; break; } continue; }
        if (!purpose[0] || !same_text(text_of(z, "Purpose"), purpose)) continue;
        int d = abs(cx - ax) > abs(cy - ay) ? abs(cx - ax) : abs(cy - ay);
        if (d < bestD) { bestD = d; best = z; }
    }
    if (!best) return 0;
    *x = num(best, "X", 0) + num(best, "Width", 1) / 2; *y = num(best, "Y", 0) + num(best, "Height", 1) / 2;
    if (*x < 0) *x = 0;
    if (*y < 0) *y = 0;
    if (*x >= world_map_w()) *x = world_map_w() - 1;
    if (*y >= world_map_h()) *y = world_map_h() - 1;
    return 1;
}

/* ---- When... rules: checked every 2 seconds, ahead of the timetable ---- */

static int rule_holds(const Npc *n, cJSON *rule, int starting)
{
    cJSON *me = character_of(n->characterId), *s = get(me, "Survival");
    switch (num(rule, "Condition", 0))
    {
        case 0:
        {
            if (cJSON_IsFalse(get(s, "CanGetHungry"))) return 0;
            int food = vitals_food(n->characterId);
            return starting ? food <= num(s, "HungryAtOrBelow", 9) : food < 31;
        }
        case 1:
        {
            int hp, most = vitals_max_hp(n->characterId);
            if (most <= 0 || !vitals_hp(n->characterId, &hp)) return 0;
            return starting ? hp * 100 <= num(rule, "InjuredAtOrBelowPercent", 50) * most : hp * 100 < num(rule, "HealedAtPercent", 100) * most;
        }
        default:
        {
            const char *var = text_of(rule, "VariableName");
            if (!var[0]) return 0;
            double left = events_variable(var), right = get(rule, "Value") && cJSON_IsNumber(get(rule, "Value")) ? get(rule, "Value")->valuedouble : 0;
            switch (num(rule, "Comparison", 0))
            {
                case 0: return fabs(left - right) < 0.0001;
                case 1: return fabs(left - right) >= 0.0001;
                case 2: return left > right;
                case 3: return left >= right;
                case 4: return left < right;
                default: return left <= right;
            }
        }
    }
}

static void check_rules(Npc *n, double ms)
{
    cJSON *rules = get(character_of(n->characterId), "ScheduleRules");
    if (!n->ruleCount || n->joined) return;
    if ((n->ruleCheckMs -= ms) > 0) return;
    n->ruleCheckMs = 2000;
    if (n->rule)
    {
        if (rule_holds(n, cJSON_GetArrayItem(rules, n->rule - 1), 0)) return;
        /* done (full, healed, the variable changed): back to the timetable */
        n->rule = 0; n->entry = NULL; n->activity = 0;
        world_stand_up(n->actor);
        stop_walking(n);
        world_set_pose(n->actor, "Idle", -1);
        update_schedule(n, events_hour() / 3, 0);
        return;
    }
    if (n->meal) return; /* off to eat its own way (Health tab) */
    for (int i = 0; i < n->ruleCount; i++)
    {
        if (!rule_holds(n, cJSON_GetArrayItem(rules, i), 1)) continue;
        n->rule = i + 1;
        int x, y;
        spot_of(n, &n->ruleEntries[i], &x, &y);
        set_schedule_and_loc(n, &n->ruleEntries[i], x, y, 0);
        return;
    }
}

/* ---- Meals: the hungry feed themselves (its own food, food it carries, the inn) ---- */

enum { MEAL_NONE, MEAL_OWN_FOOD, MEAL_CARRIED, MEAL_TO_INN, MEAL_AT_INN };
enum { B_MUNCHING = 11, B_WANTS_FOOD = 12, B_FOOD_STOLEN = 13, B_WAITER_ASKS = 14, B_WAITER_SERVES = 15 };
#define FOOD_FULL 31

static int near_tiles(void) { int t = (int)ceil(world_tile_w() / 8.0) + 1; return t > 2 ? t : 2; }
static int human_of(const Npc *n) { return !cJSON_IsFalse(get(character_of(n->characterId), "IsHuman")); }

static int owner_of_thing(int uid)
{
    int owner = world_thing_belongs_to(uid);
    return owner >= 0 ? owner : num(object_of(world_thing_object_id(uid)), "BelongToId", -1);
}

static int is_solid_food(int objectId) { cJSON *d = object_of(objectId); return num(d, "Type", -1) == 17 && !flag_of(get(d, "Food"), "IsLiquid"); }

/* The nearest solid food on the map that belongs to it (within 64 tiles). */
static int nearest_own_food(const Npc *n)
{
    int ax, ay, af, best = 0, bestD = 1 << 30, range = cells_for_tiles(64);
    world_actor_cell(n->actor, &ax, &ay, &af);
    for (int i = 0; i < world_thing_count(); i++)
    {
        int uid = world_thing_uid_at(i), c, id, q, x, y, k;
        if (!uid) continue;
        world_thing_info(uid, &c, &id, &q, &x, &y, &k);
        if (c != CAT_OBJECT || !is_solid_food(id) || owner_of_thing(uid) != n->characterId) continue;
        if (abs(x - ax) > range || abs(y - ay) > range) continue;
        if (abs(x - ax) + abs(y - ay) < bestD) { bestD = abs(x - ax) + abs(y - ay); best = uid; }
    }
    return best;
}

static Item *carried_food(const Npc *n)
{
    Item *all[128];
    int count = items_carried_all(n->characterId, all, 128);
    for (int i = 0; i < count; i++) if (items_category(all[i]) == CAT_OBJECT && is_solid_food(items_item_id(all[i]))) return all[i];
    return NULL;
}

/* A hungry character's inn: its cell, else the middle of the nearest zone of its inn purpose. */
static int inn_spot(const Npc *n, int *x, int *y)
{
    cJSON *s = get(character_of(n->characterId), "Survival");
    if (num(s, "InnX", -1) >= 0 && num(s, "InnX", -1) < world_map_w() && num(s, "InnY", -1) >= 0 && num(s, "InnY", -1) < world_map_h()) { *x = num(s, "InnX", -1); *y = num(s, "InnY", -1); return 1; }
    const char *purpose = text_of(s, "InnZonePurpose");
    if (!purpose[0]) return 0;
    cJSON *line = cJSON_CreateObject();
    cJSON_AddStringToObject(line, "ZonePurpose", purpose);
    int ok = zone_spot(n, line, x, y);
    cJSON_Delete(line);
    return ok;
}

/* The food's nutrition and effects, its eat sound, and (three times in four) a munch. */
static void nourish(const Npc *n, int objectId)
{
    cJSON *food = get(object_of(objectId), "Food");
    vitals_change_food(n->characterId, "Add", num(food, "Nutrition", 0));
    world_sound_from_actor(num(food, "EatSoundResourceId", -1), n->actor);
    if (rnd(4) > 0) vitals_bark(n->characterId, B_MUNCHING);
    magic_apply_effects(get(food, "Effects"), n->actor, text_of(object_of(objectId), "Name"), -1);
}

static void eat_placed(const Npc *n, int uid)
{
    int c, id, q, x, y, k;
    world_thing_info(uid, &c, &id, &q, &x, &y, &k);
    if (q > 1) world_thing_add_quantity(uid, -1);
    else { items_free(items_unpark(uid, c, id, q)); world_thing_remove(uid); }
    nourish(n, id);
}

static int walk_towards(Npc *n, int x, int y)
{
    if (tile_distance(n->actor, x, y) <= near_tiles()) return 0;
    if (!world_walk_to(n->actor, x, y, 0, 1)) return 0;
    n->walking = 1;
    return 1;
}

static void begin_meal(Npc *n, int step)
{
    if (!n->meal) { world_stand_up(n->actor); stop_walking(n); n->walkingToSchedule = 0; work_schedule_changed(n->actor, 0); }
    n->meal = step; n->mealFood = 0;
}

static void finish_meal(Npc *n)
{
    n->meal = 0; n->mealFood = 0; n->mealCheckMs = 60000;
    world_stand_up(n->actor);
    n->entry = NULL; n->activity = 0;
    world_set_pose(n->actor, "Idle", -1);
    update_schedule(n, events_hour() / 3, 0); /* back to what it should be doing now */
}

static int plan_meal(Npc *n)
{
    cJSON *s = get(character_of(n->characterId), "Survival");
    int food, x, y;
    if (!cJSON_IsFalse(get(s, "EatsOwnFood")) && (food = nearest_own_food(n)) != 0)
    {
        begin_meal(n, MEAL_OWN_FOOD);
        n->mealFood = food;
        n->waitMs = 0;
        return 1;
    }
    if (!cJSON_IsFalse(get(s, "EatsCarriedFood")) && carried_food(n)) { begin_meal(n, MEAL_CARRIED); n->waitMs = 800; return 1; }
    if (flag_of(s, "GoesToInn") && inn_spot(n, &x, &y)) { begin_meal(n, MEAL_TO_INN); n->waitMs = 0; return 1; }
    return 0;
}

/* Every frame: hungry and free, it sets out to eat (checked every 2 seconds). */
static void check_hunger(Npc *n, double ms)
{
    if (n->rule || n->meal || (n->mealCheckMs -= ms) > 0) return;
    n->mealCheckMs = 2000;
    cJSON *me = character_of(n->characterId), *s = get(me, "Survival");
    if (n->joined || items_in_party(n->characterId) || cJSON_IsFalse(get(s, "CanGetHungry"))) return;
    if (cJSON_IsFalse(get(s, "EatsOwnFood")) && cJSON_IsFalse(get(s, "EatsCarriedFood")) && !flag_of(s, "GoesToInn")) return;
    if (n->activity == S_SLEEP || n->activity == S_EAT || world_in_bed(n->actor)) return;
    if (vitals_food(n->characterId) > num(s, "HungryAtOrBelow", 9)) return;
    if (!plan_meal(n)) n->mealCheckMs = 30000; /* nothing to eat anywhere: later */
}

/* Its own dishes in front of it (within 2 tiles - Exult's find_nearby 2). */
static int dish_for(const Npc *n)
{
    for (int i = 0; i < world_thing_count(); i++)
    {
        int uid = world_thing_uid_at(i), c, id, q, x, y, k;
        if (!uid) continue;
        world_thing_info(uid, &c, &id, &q, &x, &y, &k);
        if (c == CAT_OBJECT && world_thing_belongs_to(uid) == n->characterId && num(object_of(id), "Type", -1) == 17 && tile_distance(n->actor, x, y) <= near_tiles() + 1) return uid;
    }
    return 0;
}

/* Eat_at_inn_schedule::now_what: sit; a dish of its own in front of it eaten (with a munch), else it asks for food. */
static void inn_customer_now_what(Npc *n)
{
    if (!world_seated(n->actor) && world_heading_to_seat(n->actor)) { n->waitMs = 1000; return; } /* still walking to its chair */
    if (!world_seated(n->actor) && world_go_to_furniture(n->actor, 0, 0)) { n->walking = world_walking(n->actor); n->waitMs = 1000; return; }
    for (int i = 0; i < n->servedCount; i++)
        if (!world_thing_there(n->served[i])) { n->served[i--] = n->served[--n->servedCount]; vitals_bark(n->characterId, B_FOOD_STOLEN); } /* taken, and not by its mouth */
    int dish = dish_for(n);
    if (dish)
    {
        for (int i = 0; i < n->servedCount; i++) if (n->served[i] == dish) n->served[i--] = n->served[--n->servedCount];
        eat_placed(n, dish);
        if (n->meal == MEAL_AT_INN && vitals_food(n->characterId) >= FOOD_FULL) { finish_meal(n); return; }
        n->waitMs = 3000 + rnd(3000);
        return;
    }
    if (human_of(n) && rnd(4) > 0) vitals_bark(n->characterId, B_WANTS_FOOD);
    n->waitMs = 5000 + rnd(12000); /* Exult: "Wake up in a little while" - 5 to 17 s */
}

static void meal_now_what(Npc *n)
{
    if (vitals_food(n->characterId) >= FOOD_FULL) { finish_meal(n); return; }
    cJSON *s = get(character_of(n->characterId), "Survival");
    switch (n->meal)
    {
        case MEAL_OWN_FOOD:
        {
            int food = n->mealFood, c, id, q, x, y, k;
            if (!food || !world_thing_there(food) || owner_of_thing(food) != n->characterId)
            {
                if (food && !world_thing_there(food)) vitals_bark(n->characterId, B_FOOD_STOLEN); /* it's gone: taken */
                if (!plan_meal(n)) finish_meal(n);
                return;
            }
            world_thing_info(food, &c, &id, &q, &x, &y, &k);
            if (tile_distance(n->actor, x, y) > near_tiles())
            {
                if (!walk_towards(n, x, y) && !plan_meal(n)) finish_meal(n);
                return;
            }
            eat_placed(n, food);
            n->mealFood = 0;
            n->waitMs = 1500 + rnd(1500);
            if (vitals_food(n->characterId) < FOOD_FULL && !plan_meal(n)) finish_meal(n);
            return;
        }
        case MEAL_CARRIED:
        {
            Item *food = carried_food(n);
            int x, y;
            if (!food)
            {
                if (flag_of(s, "GoesToInn") && inn_spot(n, &x, &y)) { n->meal = MEAL_TO_INN; n->waitMs = 0; }
                else finish_meal(n);
                return;
            }
            int id = items_item_id(food);
            Item *got[4];
            int got_n = 0;
            if (items_take(n->characterId, CAT_OBJECT, id, 1, got, 4, &got_n)) for (int g = 0; g < got_n; g++) items_free(got[g]);
            nourish(n, id);
            n->waitMs = 2000 + rnd(1500);
            return;
        }
        case MEAL_TO_INN:
        {
            int x, y;
            if (!inn_spot(n, &x, &y)) { finish_meal(n); return; }
            if (tile_distance(n->actor, x, y) > near_tiles() + 1)
            {
                if (!walk_towards(n, x, y)) n->waitMs = 3000; /* someone in the way: shortly */
                return;
            }
            n->meal = MEAL_AT_INN;
            if (!world_seated(n->actor) && world_go_to_furniture(n->actor, 0, 0)) n->walking = world_walking(n->actor);
            else n->waitMs = 1000;
            return;
        }
        case MEAL_AT_INN: inn_customer_now_what(n); return;
    }
}

/* ---- The waiter (Waiter_schedule) ---- */

static int waiting_customer(const Npc *c)
{
    if (vitals_dead(c->characterId) || vitals_incapacitated(c->characterId) || world_actor_gone(c->actor)) return 0;
    int atInn = c->meal == MEAL_AT_INN || (!c->meal && c->activity == S_EAT && !c->walkingToSchedule);
    if (!atInn || world_heading_to_seat(c->actor)) return 0;
    return !dish_for(c);
}

static int order_taken(int actor)
{
    for (int i = 0; i < npcCount; i++) if (npcs[i].activity == S_WAITER && npcs[i].customer == actor && npcs[i].waiter > 0) return 1;
    return 0;
}

/* serve_food: on a table next to them if there is one, else on the floor in front of them. It's theirs. */
static int serve_dish(int foodId, const Npc *customer)
{
    if (!object_of(foodId)) return 0;
    int cx, cy, cf, spotX, spotY, table = 0, best = 1 << 30;
    world_actor_cell(customer->actor, &cx, &cy, &cf);
    for (int i = 0; i < world_thing_count(); i++)
    {
        int uid = world_thing_uid_at(i), c, id, q, x, y, k;
        if (!uid || world_thing_type(uid) != 32) continue;
        world_thing_info(uid, &c, &id, &q, &x, &y, &k);
        if (abs(x - cx) > 2 || abs(y - cy) > 2) continue;
        if (abs(x - cx) + abs(y - cy) < best) { best = abs(x - cx) + abs(y - cy); table = uid; spotX = x; spotY = y; }
    }
    if (!table)
    {
        static const int DX[8] = { 0, 1, 1, 1, 0, -1, -1, -1 }, DY[8] = { -1, -1, 0, 1, 1, 1, 0, -1 };
        int f = world_actor_facing(customer->actor) & 7;
        spotX = cx + DX[f]; spotY = cy + DY[f];
        spotX = spotX < 0 ? 0 : spotX >= world_map_w() ? world_map_w() - 1 : spotX;
        spotY = spotY < 0 ? 0 : spotY >= world_map_h() ? world_map_h() - 1 : spotY;
    }
    int uid = world_thing_put(CAT_OBJECT, foodId, 1, spotX, spotY, cf);
    if (uid < 0) return 0;
    world_thing_set_belongs(uid, customer->characterId);
    return uid;
}

static void face_towards(const Npc *n, int other)
{
    int x, y, f, ox, oy;
    world_actor_cell(n->actor, &x, &y, &f);
    world_actor_cell(other, &ox, &oy, &f);
    int dx = (ox > x) - (ox < x), dy = (oy > y) - (oy < y);
    static const int dirs[3][3] = { { 7, 0, 1 }, { 6, 4, 2 }, { 5, 4, 3 } };
    world_set_pose(n->actor, "Idle", dirs[dy + 1][dx + 1]);
}

static void waiter_now_what(Npc *n)
{
    Npc *c = n->customer > 0 ? npc_of(n->customer) : NULL;
    int ok = c && waiting_customer(c);
    int cx = 0, cy = 0, cf;
    if (c) world_actor_cell(c->actor, &cx, &cy, &cf);
    switch (n->waiter)
    {
        case 0: /* find a customer within 32 tiles */
        {
            Npc *next = NULL;
            int best = 1 << 30;
            for (int i = 0; i < npcCount; i++)
            {
                Npc *o = &npcs[i];
                if (o == n || !waiting_customer(o) || order_taken(o->actor)) continue;
                int ox, oy, of;
                world_actor_cell(o->actor, &ox, &oy, &of);
                int d = tile_distance(n->actor, ox, oy);
                if (d <= 32 && d < best) { best = d; next = o; }
            }
            if (!next)
            {
                /* no one to serve: back to the counter, waiting there (Exult wait_at_counter: 2-4 s) */
                if (tile_distance(n->actor, n->destX, n->destY) > 3 && walk_towards(n, n->destX, n->destY)) return;
                n->waitMs = 2000 + rnd(2000);
                return;
            }
            n->customer = next->actor;
            n->waiter = 1;
            int ox, oy, of;
            world_actor_cell(next->actor, &ox, &oy, &of);
            if (!walk_towards(n, ox, oy)) n->waitMs = 0;
            return;
        }
        case 1: /* at the customer: the order */
            if (!ok) { n->waiter = 0; n->waitMs = 500; return; }
            if (walk_towards(n, cx, cy)) return;
            face_towards(n, c->actor);
            vitals_bark(n->characterId, B_WAITER_ASKS);
            n->waiter = 2;
            n->waitMs = 1200;
            return;
        case 2: /* back to the counter */
            if (walk_towards(n, n->destX, n->destY)) return;
            n->waiter = 3;
            world_set_pose(n->actor, "Attacking Melee", -1); /* working at the counter (Exult's Prep_animation) */
            n->waitMs = 1500 + rnd(1500);
            return;
        case 3: /* prepared: a dish from its menu */
        {
            world_set_pose(n->actor, "Idle", -1);
            cJSON *menu = get(character_of(n->characterId), "WaiterServesFoodIds");
            if (!ok || !cJSON_GetArraySize(menu)) { n->waiter = 0; n->customer = -1; n->waitMs = 1000; return; }
            n->dish = cJSON_GetArrayItem(menu, rnd(cJSON_GetArraySize(menu)))->valueint;
            n->waiter = 4;
            if (!walk_towards(n, cx, cy)) n->waitMs = 0;
            return;
        }
        case 4: /* brought */
        {
            if (!ok) { n->waiter = 0; n->customer = -1; n->waitMs = 500; return; }
            if (walk_towards(n, cx, cy)) return;
            face_towards(n, c->actor);
            int dish = serve_dish(n->dish, c);
            if (dish)
            {
                if (c->servedCount < 8) c->served[c->servedCount++] = dish;
                sound_play(num(get(get(project_json(), "Settings"), "Sounds"), "DropOnMapSoundId", -1)); /* Exult: the drop sound */
                if (rnd(3) > 0) vitals_bark(n->characterId, B_WAITER_SERVES);
            }
            n->customer = -1;
            n->waiter = 0;
            n->waitMs = 1000 + rnd(1000);
            return;
        }
    }
}

/* ---- House doors: the key to its own door, carried (Character Editor > Verbose > Locks its house door) ---- */

static int holds_house_key(int actor, int doorUid)
{
    int id = world_actor_character(actor);
    cJSON *me = character_of(id), *door = get(object_of(world_thing_object_id(doorUid)), "Door");
    if (!flag_of(me, "LocksHouseDoor") || cJSON_IsFalse(get(me, "IsHuman")) || flag_of(door, "MagicallyLocked")) return 0;
    int keyId = num(door, "RequiredKeyItemId", -1);
    cJSON *key = keyId >= 0 ? object_of(keyId) : NULL;
    if (!key || num(key, "BelongToId", -1) != id) return 0;
    return items_count_any(id, CAT_OBJECT, keyId) > 0;
}

void schedules_house_keys(void) { world_set_key_check(holds_house_key); }
