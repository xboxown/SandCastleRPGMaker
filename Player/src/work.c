/*
 * The work schedules, as the Windows engine has them (GameTestWindow.Work),
 * after Exult's schedule.cc: Bake, Sew, Blacksmith and Lab work the crafting
 * stations round their spot; Farm and Miner harvest (and sow) the plants
 * there. A worker really does what the player would:
 *  - its things: its own first, then nobody's, inside its work area (the
 *    schedule's zone, or so many tiles round its spot) - never someone else's;
 *  - a station's recipe it can make: the ingredients carried, else fetched
 *    from the area's chests and tables (walked to, taken as the player takes
 *    them);
 *  - the tool the recipe needs: carried, or fetched (remembered, to be put
 *    back), readied in its right hand (what was there goes into its pack);
 *  - the work at the station: its working look and sound, the worker's work
 *    pose, the ingredients used up; what it makes appears on the station
 *    (By Made Something), then goes to the schedule's output place - a chest
 *    or a spot - unless another station here needs it (thread for the loom);
 *  - fields: the nearest ripe plant harvested with its tool, the produce
 *    picked up and taken to the output place (or its own chest) when its
 *    arms are full (8) or nothing more is ripe; then empty soil sown;
 *  - the Lab's mage sits and reads between brews;
 *  - when its schedule changes it puts back the tools it took
 *    (Tool_schedule::ending).
 */
#include "work.h"
#include "world.h"
#include "items.h"
#include "events.h"
#include "vitals.h"
#include "combat.h"
#include "sound.h"
#include "mechanisms.h"
void game_log(const char *line);
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

enum { OT_CHAIR = 7, OT_PLANT = 13, OT_TOOL = 26, OT_STATION = 27, OT_TABLE = 32 };
enum { S_FARM = 6, S_MINER = 8, S_BLACKSMITH = 13, S_BAKE = 18, S_SEW = 19, S_LAB = 21 };
enum { SLOT_RIGHT_HAND = 9 };
enum { ST_FETCH, ST_GET_TOOL, ST_READY_TOOL, ST_GOTO, ST_WORK, ST_DELIVER_MADE, ST_HARVEST, ST_PICK_PRODUCE, ST_DELIVER_CARRIED, ST_SOW, ST_READ, ST_PUT_BACK };

static uint32_t rng = 2891336453u;
static int rnd(int n) { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return n > 0 ? (int)(((uint64_t)rng * (uint32_t)n) >> 32) : 0; }

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static double dbl(cJSON *o, const char *k, double fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valuedouble : fallback; }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }
static cJSON *project(void) { return get((cJSON *)world_json(), "Project"); }

static cJSON *object_def(int id) { cJSON *o; cJSON_ArrayForEach(o, get(get(project(), "Objects"), "Objects")) if (num(o, "Id", -2) == id) return o; return NULL; }
static cJSON *container_def(int id) { cJSON *o; cJSON_ArrayForEach(o, get(get(project(), "Containers"), "Containers")) if (num(o, "Id", -2) == id) return o; return NULL; }
static cJSON *character_json(int id) { cJSON *o; cJSON_ArrayForEach(o, get(get(project(), "Characters"), "Characters")) if (num(o, "Id", -2) == id) return o; return NULL; }

static int same_kind(const char *a, const char *b)
{
    while (*a == ' ') a++;
    while (*b == ' ') b++;
    for (; *a || *b; a++, b++)
    {
        char x = *a >= 'A' && *a <= 'Z' ? (char)(*a + 32) : *a, y = *b >= 'A' && *b <= 'Z' ? (char)(*b + 32) : *b;
        if (x != y) return (x == ' ' && !y) || (!x && y == ' ');
    }
    return 1;
}

/* ---------------- One worker ---------------- */

typedef struct Step { int kind, a, b, c; char text[48]; } Step;
typedef struct Borrowed { int objectId, chest, x, y; } Borrowed; /* a tool taken: from a chest (its uid) or a spot */

typedef struct Worker
{
    int on, actor, activity;
    cJSON *entry;                  /* its schedule entry (zone, area, output place, work pose) */
    int destX, destY;              /* its schedule's spot */
    Step steps[16]; int stepCount, step, phase;
    int target, tries;             /* the thing a step is going to */
    double ms, beat;
    int made, produce;             /* what it just made / harvested (uids) */
    int produceIds[16], produceCount;
    Borrowed borrowed[8]; int borrowedCount;
    int saidIdle, tidying;
    double waitMs;                 /* before it looks for work again */
} Worker;
static Worker workers[256];
static int stationUser[2048]; /* per thing uid: the actor working at it + 1 */

static int is_station_work(int a) { return a == S_BAKE || a == S_SEW || a == S_BLACKSMITH || a == S_LAB; }
static int is_field_work(int a) { return a == S_FARM || a == S_MINER; }
int work_is_work(int activity) { return is_station_work(activity) || is_field_work(activity); }

static int me_id(const Worker *w) { return world_actor_character(w->actor); }
static int heard(int actor) { return combat_tiles_between(actor, 0) <= 24; } /* its sounds are heard only near the player */

/* Who a placed thing belongs to: this copy's own owner, else its kind's Belong To. */
static int owner_of(int uid)
{
    int owner = world_thing_belongs_to(uid);
    if (owner >= 0) return owner;
    int c, i, q, x, y, n;
    world_thing_info(uid, &c, &i, &q, &x, &y, &n);
    return num(c == CAT_CONTAINER ? container_def(i) : object_def(i), "BelongToId", -1);
}
static int mine_or_nobodys(const Worker *w, int uid) { int o = owner_of(uid); return o < 0 || o == me_id(w); }

static void thing_cell(int uid, int *x, int *y) { int c, i, q, n; world_thing_info(uid, &c, &i, &q, x, y, &n); }

/* Inside its work area, on its floor: the schedule's zone, or so many tiles round its spot. */
static int in_area(const Worker *w, int uid)
{
    int x, y, ax, ay, af;
    thing_cell(uid, &x, &y);
    world_actor_cell(w->actor, &ax, &ay, &af);
    if (world_thing_floor(uid) != af) return 0;
    cJSON *zones = get(get(project(), "Map"), "Zones"), *z;
    int zoneId = num(w->entry, "ZoneId", -1);
    const char *purpose = str(w->entry, "ZonePurpose");
    cJSON_ArrayForEach(z, zones)
    {
        int match = zoneId >= 0 ? num(z, "Id", -2) == zoneId : purpose[0] && same_kind(str(z, "Purpose"), purpose);
        if (!match) continue;
        int zx = num(z, "X", 0), zy = num(z, "Y", 0), zw = num(z, "Width", 1), zh = num(z, "Height", 1);
        if (zoneId >= 0 || (w->destX >= zx && w->destY >= zy && w->destX < zx + zw && w->destY < zy + zh))
            return x >= zx && y >= zy && x < zx + zw && y < zy + zh;
    }
    int tiles = num(w->entry, "WorkAreaTiles", 12);
    int reach = (int)floor((tiles > 1 ? tiles : 1) * 8.0 / world_tile_w() + 0.5);
    if (reach < 1) reach = 1;
    return abs(x - w->destX) <= reach && abs(y - w->destY) <= reach;
}

static int distance_to(const Worker *w, int uid) { int x, y, ax, ay, af; thing_cell(uid, &x, &y); world_actor_cell(w->actor, &ax, &ay, &af); return abs(x - ax) + abs(y - ay); }

/* ---------------- Counting and taking ---------------- */

static int count_in(Item *box, int objectId)
{
    Item **list;
    int n = items_contents(box, &list), total = 0;
    for (int i = 0; i < n; i++) total += (items_category(list[i]) == CAT_OBJECT && items_item_id(list[i]) == objectId ? items_quantity(list[i]) : 0) + count_in(list[i], objectId);
    return total;
}

static int is_tool_kind(int objectId, const char *kind) { cJSON *d = object_def(objectId); return num(d, "Type", -1) == OT_TOOL && same_kind(str(get(d, "Tool"), "ToolKind"), kind); }

static Item *find_in(Item *box, int objectId, const char *toolKind)
{
    Item **list;
    int n = items_contents(box, &list);
    for (int i = 0; i < n; i++)
    {
        if (items_category(list[i]) == CAT_OBJECT && (toolKind ? is_tool_kind(items_item_id(list[i]), toolKind) : items_item_id(list[i]) == objectId)) return list[i];
        Item *inside = find_in(list[i], objectId, toolKind);
        if (inside) return inside;
    }
    return NULL;
}

static Item *carried_tool(const Worker *w, const char *kind)
{
    Item *all[128];
    int n = items_carried_all(me_id(w), all, 128);
    for (int i = 0; i < n; i++) if (items_category(all[i]) == CAT_OBJECT && is_tool_kind(items_item_id(all[i]), kind)) return all[i];
    return NULL;
}

/* The chests in its area it may take from (unlocked): its own first, then the nearest. */
static int work_chests(const Worker *w, int *out, int max)
{
    int n = 0;
    for (int i = 0; i < world_thing_count() && n < max; i++)
    {
        int uid = world_thing_uid_at(i), c, id, q, x, y, k;
        if (!uid) continue;
        world_thing_info(uid, &c, &id, &q, &x, &y, &k);
        if (c != CAT_CONTAINER || id <= -1000 || !in_area(w, uid) || !mine_or_nobodys(w, uid)) continue;
        if (items_is_locked(items_parked(uid, c, id, q))) continue;
        out[n++] = uid;
    }
    for (int i = 1; i < n; i++)
        for (int j = i; j > 0; j--)
        {
            int a = out[j - 1], b = out[j];
            int ka = (owner_of(a) == me_id(w) ? 0 : 100000) + distance_to(w, a), kb = (owner_of(b) == me_id(w) ? 0 : 100000) + distance_to(w, b);
            if (kb >= ka) break;
            out[j - 1] = b; out[j] = a;
        }
    return n;
}

static Item *chest_item(int uid) { int c, i, q, x, y, n; world_thing_info(uid, &c, &i, &q, &x, &y, &n); return items_parked(uid, c, i, q); }

/* Things lying in its area it may take (an object kind, or a tool kind): the nearest of its own, else of nobody's. */
static int loose_in_area(const Worker *w, int objectId, const char *toolKind)
{
    int best = 0, bestKey = 1 << 30;
    for (int i = 0; i < world_thing_count(); i++)
    {
        int uid = world_thing_uid_at(i), c, id, q, x, y, k;
        if (!uid) continue;
        world_thing_info(uid, &c, &id, &q, &x, &y, &k);
        if (c != CAT_OBJECT || (toolKind ? !is_tool_kind(id, toolKind) : id != objectId)) continue;
        if (world_thing_vehicle(uid) || !in_area(w, uid) || !mine_or_nobodys(w, uid)) continue;
        int key = (owner_of(uid) == me_id(w) ? 0 : 100000) + distance_to(w, uid);
        if (key < bestKey) { bestKey = key; best = uid; }
    }
    return best;
}

static int available(const Worker *w, int objectId)
{
    int total = items_count_any(me_id(w), CAT_OBJECT, objectId), chests[64], n = work_chests(w, chests, 64);
    for (int i = 0; i < n; i++) total += count_in(chest_item(chests[i]), objectId);
    for (int i = 0; i < world_thing_count(); i++)
    {
        int uid = world_thing_uid_at(i), c, id, q, x, y, k;
        if (!uid) continue;
        world_thing_info(uid, &c, &id, &q, &x, &y, &k);
        if (c == CAT_OBJECT && id == objectId && in_area(w, uid) && mine_or_nobodys(w, uid)) total += q;
    }
    return total;
}

static int tool_available(const Worker *w, const char *kind)
{
    if (!kind[0]) return 1;
    if (carried_tool(w, kind)) return 1;
    int chests[64], n = work_chests(w, chests, 64);
    for (int i = 0; i < n; i++) if (find_in(chest_item(chests[i]), -1, kind)) return 1;
    return loose_in_area(w, -1, kind) != 0;
}

/* Up to this many out of a chest into its pack (as the player takes them). How many it got. */
static int take_out_of(const Worker *w, int chest, int objectId, const char *toolKind, int quantity)
{
    Item *box = chest_item(chest);
    int got = 0;
    while (got < quantity)
    {
        Item *it = find_in(box, objectId, toolKind);
        if (!it) break;
        ItemPlace from;
        if (!items_detach(it, &from)) break;
        if (!items_give_to(me_id(w), it)) { items_restore(&from, it); break; } /* too heavy, no room: it stays */
        got += items_quantity(it);
        if (toolKind) break;
    }
    return got;
}

/* A thing on the map picked up into its pack. */
static int pick_up(const Worker *w, int uid)
{
    if (!world_thing_there(uid)) return 0;
    int c, i, q, x, y, n, fl = world_thing_floor(uid);
    world_thing_info(uid, &c, &i, &q, &x, &y, &n);
    Item *it = items_unpark(uid, c, i, q);
    world_thing_remove(uid);
    if (items_give_to(me_id(w), it)) return 1;
    int back = world_thing_put(c, i, q, x, y, fl); /* refused: back where it was */
    if (back >= 0) items_park(back, it); else items_free(it);
    return 0;
}

/* Carried things put down on a cell (a table, a counter). */
static void put_down(const Worker *w, int objectId, int count, int x, int y, int fl)
{
    Item *got[100];
    int n = 0;
    if (!items_take(me_id(w), CAT_OBJECT, objectId, count, got, 100, &n)) return;
    for (int k = 0; k < n; k++)
    {
        int uid = world_thing_put(CAT_OBJECT, items_item_id(got[k]), items_quantity(got[k]), x, y, fl);
        if (uid >= 0) items_park(uid, got[k]); else items_free(got[k]);
    }
}

/* ---------------- Steps ---------------- */

static void add(Worker *w, int kind, int a, int b, int c, const char *text)
{
    if (w->stepCount >= 16) return;
    Step *s = &w->steps[w->stepCount++];
    s->kind = kind; s->a = a; s->b = b; s->c = c;
    snprintf(s->text, sizeof s->text, "%s", text ? text : "");
}

static void begin_round(Worker *w) { w->on = 1; w->step = 0; w->phase = 0; w->ms = 0; w->target = 0; w->tries = 0; w->made = w->produce = 0; }

static const char *work_pose(const Worker *w) { const char *p = str(w->entry, "WorkPose"); return p[0] ? p : "Attacking Melee"; }

/* Next to it (a cell away): there. */
static int beside(const Worker *w, int uid)
{
    int x, y, ax, ay, af;
    thing_cell(uid, &x, &y);
    world_actor_cell(w->actor, &ax, &ay, &af);
    return abs(x - ax) <= 1 && abs(y - ay) <= 1;
}

/* Walking to it: 1 there, 0 still going, -1 can't get there. */
static int approach(Worker *w, int uid)
{
    if (!world_thing_there(uid)) return -1;
    if (beside(w, uid)) return 1;
    if (world_walking(w->actor)) return 0;
    if (w->phase == 1) return -1; /* walked and still not there */
    int x, y;
    thing_cell(uid, &x, &y);
    if (!world_walk_to(w->actor, x, y, world_thing_floor(uid), 1)) return -1;
    w->phase = 1;
    return 0;
}

static void face(const Worker *w, int uid)
{
    int x, y, ax, ay, af;
    thing_cell(uid, &x, &y);
    world_actor_cell(w->actor, &ax, &ay, &af);
    int dx = (x > ax) - (x < ax), dy = (y > ay) - (y < ay);
    static const int dirs[3][3] = { { 7, 0, 1 }, { 6, 4, 2 }, { 5, 4, 3 } };
    world_set_pose(w->actor, "Idle", dirs[dy + 1][dx + 1]);
}

static void next_step(Worker *w) { w->step++; w->phase = 0; w->ms = 0; w->target = 0; w->tries = 0; }

/* The station's recipe at b (index), made: inputs used, the work, the product on it. */
static cJSON *recipe_at(int station, int index)
{
    return cJSON_GetArrayItem(get(get(object_def(world_thing_object_id(station)), "Crafting"), "Recipes"), index);
}

/* Its output place: the chest at (or beside) the schedule's output spot, else that spot. 0: none. */
static int output_place(const Worker *w, int *chest, int *x, int *y)
{
    *chest = 0;
    int ox = num(w->entry, "OutputX", -1), oy = num(w->entry, "OutputY", -1);
    if (ox >= 0 && oy >= 0 && ox < world_map_w() && oy < world_map_h())
    {
        *x = ox; *y = oy;
        int best = 1 << 30;
        for (int i = 0; i < world_thing_count(); i++)
        {
            int uid = world_thing_uid_at(i), c, id, q, tx, ty, k;
            if (!uid) continue;
            world_thing_info(uid, &c, &id, &q, &tx, &ty, &k);
            if (c != CAT_CONTAINER || id <= -1000 || abs(tx - ox) > 1 || abs(ty - oy) > 1) continue;
            int d = abs(tx - ox) + abs(ty - oy);
            if (d < best) { best = d; *chest = uid; }
        }
        return 1;
    }
    if (is_field_work(w->activity))
    {
        /* a farmer with no output place: its own chest in the field (a granary) */
        int chests[64], n = work_chests(w, chests, 64);
        for (int i = 0; i < n; i++) if (owner_of(chests[i]) == me_id(w)) { *chest = chests[i]; thing_cell(chests[i], x, y); return 1; }
    }
    return 0;
}

/* Something another recipe in its area uses (the spinner's thread, the loom's): a step on the way, not finished work. */
static int needed_in_workshop(const Worker *w, int objectId)
{
    for (int i = 0; i < world_thing_count(); i++)
    {
        int uid = world_thing_uid_at(i);
        if (!uid || world_thing_type(uid) != OT_STATION || !in_area(w, uid) || !mine_or_nobodys(w, uid)) continue;
        cJSON *r, *in;
        cJSON_ArrayForEach(r, get(get(object_def(world_thing_object_id(uid)), "Crafting"), "Recipes"))
            cJSON_ArrayForEach(in, get(r, "Inputs")) if (num(in, "ObjectId", -1) == objectId) return 1;
    }
    return 0;
}

static int station_busy(int uid, int actor)
{
    if (mechanisms_station_busy(uid)) return 1;
    return uid < 2048 && stationUser[uid] && stationUser[uid] - 1 != actor;
}

/* One frame of the step it's on. 1 = this step done, 0 = still at it, -1 = the round can't go on. */
static int run_step(Worker *w, Step *s, double ms)
{
    int me = me_id(w);
    switch (s->kind)
    {
        case ST_FETCH:
        {
            /* this many of a thing into its pack: carried, then from the area's chests and tables - walking to each */
            if (items_count_any(me, CAT_OBJECT, s->a) >= s->b) return 1;
            if (!w->target)
            {
                if (++w->tries > 8) return -1;
                int chests[64], n = work_chests(w, chests, 64);
                for (int i = 0; i < n && !w->target; i++) if (count_in(chest_item(chests[i]), s->a) > 0) w->target = chests[i];
                if (!w->target) w->target = loose_in_area(w, s->a, NULL);
                if (!w->target) return -1;
                w->phase = 0;
            }
            int at = approach(w, w->target);
            if (at < 0) return -1;
            if (!at) return 0;
            if (w->phase < 2) { world_set_pose(w->actor, "Picking", -1); w->phase = 2; w->ms = 600; return 0; }
            if ((w->ms -= ms) > 0) return 0;
            int c, i, q, x, y, n;
            world_thing_info(w->target, &c, &i, &q, &x, &y, &n);
            if (c == CAT_CONTAINER) take_out_of(w, w->target, s->a, NULL, s->b - items_count_any(me, CAT_OBJECT, s->a));
            else pick_up(w, w->target);
            world_set_pose(w->actor, "Idle", -1);
            w->target = 0;
            return items_count_any(me, CAT_OBJECT, s->a) >= s->b ? 1 : 0;
        }
        case ST_GET_TOOL:
        {
            if (carried_tool(w, s->text)) return 1;
            if (!w->target)
            {
                int chests[64], n = work_chests(w, chests, 64);
                for (int i = 0; i < n && !w->target; i++) if (find_in(chest_item(chests[i]), -1, s->text)) w->target = chests[i];
                if (!w->target) w->target = loose_in_area(w, -1, s->text);
                if (!w->target) return -1;
            }
            int at = approach(w, w->target);
            if (at < 0) return -1;
            if (!at) return 0;
            if (w->phase < 2) { world_set_pose(w->actor, "Picking", -1); w->phase = 2; w->ms = 600; return 0; }
            if ((w->ms -= ms) > 0) return 0;
            int c, i, q, x, y, n;
            world_thing_info(w->target, &c, &i, &q, &x, &y, &n);
            int ok = c == CAT_CONTAINER ? take_out_of(w, w->target, -1, s->text, 1) > 0 : pick_up(w, w->target);
            Item *tool = carried_tool(w, s->text);
            if (ok && tool && w->borrowedCount < 8) w->borrowed[w->borrowedCount++] = (Borrowed){ items_item_id(tool), c == CAT_CONTAINER ? w->target : 0, x, y }; /* remembered, to be put back */
            world_set_pose(w->actor, "Idle", -1);
            return ok ? 1 : -1;
        }
        case ST_READY_TOOL:
        {
            /* the tool in its right hand; what was there goes into its pack */
            Item *tool = carried_tool(w, s->text);
            if (!tool) return -1;
            if (items_in_slot(me, SLOT_RIGHT_HAND) == tool) return 1;
            Item *held = items_in_slot(me, SLOT_RIGHT_HAND);
            ItemPlace from;
            if (held && items_detach(held, &from)) items_add_loose(me, held);
            if (items_detach(tool, &from))
            {
                if (!items_equip_at(me, tool, SLOT_RIGHT_HAND)) items_add_loose(me, tool); /* it can't hold it: it works with it in the pack */
            }
            return 1;
        }
        case ST_GOTO:
        {
            int at = approach(w, s->a);
            return at < 0 ? -1 : at;
        }
        case ST_WORK:
        {
            int station = s->a;
            cJSON *recipe = recipe_at(station, s->b), *in;
            if (!world_thing_there(station)) return -1;
            if (w->phase < 5)
            {
                if (station_busy(station, w->actor)) return -1;
                cJSON_ArrayForEach(in, get(recipe, "Inputs"))
                    if (num(in, "ObjectId", -1) >= 0 && items_count_any(me, CAT_OBJECT, num(in, "ObjectId", -1)) < (num(in, "Quantity", 1) > 1 ? num(in, "Quantity", 1) : 1)) return -1;
                cJSON_ArrayForEach(in, get(recipe, "Inputs"))
                {
                    int id = num(in, "ObjectId", -1), q = num(in, "Quantity", 1) > 1 ? num(in, "Quantity", 1) : 1;
                    Item *got[100];
                    int n = 0;
                    if (id >= 0 && items_take(me, CAT_OBJECT, id, q, got, 100, &n)) for (int k = 0; k < n; k++) items_free(got[k]);
                }
                if (station < 2048) stationUser[station] = w->actor + 1;
                world_thing_set_lit(station, 1); /* working: its working sprite */
                face(w, station);
                double secs = dbl(recipe, "WorkSeconds", 2.4);
                w->ms = (secs > 0.5 ? secs : 0.5) * 1000; w->beat = 0; w->phase = 5;
                return 0;
            }
            w->ms -= ms;
            if ((w->beat -= ms) <= 0 && w->ms > 0)
            {
                w->beat = 800; /* Exult's millstone: a turn, with its sound */
                if (heard(w->actor)) sound_play(num(recipe, "WorkSoundResourceId", -1));
                world_set_pose(w->actor, work_pose(w), -1);
            }
            if (w->ms > 0) return 0;
            if (station < 2048) stationUser[station] = 0;
            world_thing_set_lit(station, 0);
            world_set_pose(w->actor, "Idle", -1);
            int x, y, many = num(recipe, "OutputQuantity", 1);
            thing_cell(station, &x, &y);
            w->made = world_thing_put(CAT_OBJECT, num(recipe, "OutputObjectId", -1), many > 1 ? many : 1, x, y, world_thing_floor(station));
            if (w->made < 0) w->made = 0;
            if (w->made && owner_of(station) == me) world_thing_set_belongs(w->made, me); /* made at its own station: its own */
            events_object_event(station, 5, w->actor); /* By Made Something */
            return 1;
        }
        case ST_DELIVER_MADE:
        {
            int chest, x, y;
            if (!w->made || !world_thing_there(w->made) || !output_place(w, &chest, &x, &y) || needed_in_workshop(w, world_thing_object_id(w->made))) return 1; /* left on the station */
            int at = approach(w, w->made);
            if (at < 0) return 1;
            if (!at) return 0;
            if (w->phase < 2) { world_set_pose(w->actor, "Picking", -1); w->phase = 2; w->ms = 600; return 0; }
            if ((w->ms -= ms) > 0) return 0;
            int id = world_thing_object_id(w->made);
            pick_up(w, w->made);
            w->made = 0;
            if (w->produceCount < 16) w->produceIds[w->produceCount++] = id;
            s->kind = ST_DELIVER_CARRIED; w->phase = 0; w->target = 0; /* then to its output place */
            return 0;
        }
        case ST_HARVEST:
        {
            int plant = s->a;
            if (!world_thing_there(plant) || world_thing_type(plant) != OT_PLANT) return -1;
            if (w->phase < 5)
            {
                if (world_thing_stage(plant) < world_thing_stage_count(plant) - 1) return -1;
                face(w, plant);
                w->ms = 2400; w->beat = 0; w->phase = 5;
                return 0;
            }
            w->ms -= ms;
            if ((w->beat -= ms) <= 0 && w->ms > 0) { w->beat = 800; world_set_pose(w->actor, work_pose(w), -1); }
            if (w->ms > 0) return 0;
            world_set_pose(w->actor, "Idle", -1);
            int owner = owner_of(plant);
            w->produce = mechanisms_harvest(plant, w->actor);
            if (w->produce && owner == me) world_thing_set_belongs(w->produce, me); /* from its own field: its own */
            return 1;
        }
        case ST_PICK_PRODUCE:
        {
            if (!w->produce || !world_thing_there(w->produce)) return 1;
            int at = approach(w, w->produce);
            if (at < 0) return 1;
            if (!at) return 0;
            if (w->phase < 2) { world_set_pose(w->actor, "Picking", -1); w->phase = 2; w->ms = 600; return 0; }
            if ((w->ms -= ms) > 0) return 0;
            int id = world_thing_object_id(w->produce), known = 0;
            pick_up(w, w->produce);
            for (int i = 0; i < w->produceCount; i++) known |= w->produceIds[i] == id;
            if (!known && w->produceCount < 16) w->produceIds[w->produceCount++] = id;
            w->produce = 0;
            world_set_pose(w->actor, "Idle", -1);
            return 1;
        }
        case ST_DELIVER_CARRIED:
        {
            /* everything it carries of what it made or harvested: into the output chest, or onto the output spot */
            int chest, x, y;
            if (!output_place(w, &chest, &x, &y)) return 1;
            if (chest)
            {
                int at = approach(w, chest);
                if (at < 0) return -1;
                if (!at) return 0;
            }
            else
            {
                int ax, ay, af;
                world_actor_cell(w->actor, &ax, &ay, &af);
                if (abs(ax - x) > 1 || abs(ay - y) > 1)
                {
                    if (world_walking(w->actor)) return 0;
                    if (w->phase == 1) return -1;
                    if (!world_walk_to(w->actor, x, y, af, 1)) return -1;
                    w->phase = 1;
                    return 0;
                }
            }
            if (w->phase < 2) { world_set_pose(w->actor, "Picking", -1); w->phase = 2; w->ms = 600; return 0; }
            if ((w->ms -= ms) > 0) return 0;
            for (int k = 0; k < w->produceCount; k++)
            {
                int id = w->produceIds[k], count = items_count_any(me, CAT_OBJECT, id);
                if (count <= 0) continue;
                if (chest)
                {
                    Item *got[100], *box = chest_item(chest);
                    int n = 0;
                    if (items_take(me, CAT_OBJECT, id, count, got, 100, &n))
                        for (int g = 0; g < n; g++) if (!items_put_into(box, got[g], 0, 0, 0)) items_give_to(me, got[g]); /* no room: kept */
                }
                else put_down(w, id, count, x, y, world_thing_floor(chest) > 0 ? world_thing_floor(chest) : 1);
            }
            w->produceCount = 0;
            world_set_pose(w->actor, "Idle", -1);
            return 1;
        }
        case ST_SOW:
        {
            int soil = s->a, plantId = s->b, seed = s->c;
            if (w->phase < 5)
            {
                if (!world_thing_there(soil) || world_thing_object_id(soil) != num(get(object_def(plantId), "Plant"), "SoilObjectId", -2) || items_count_any(me, CAT_OBJECT, seed) < 1) return -1;
                face(w, soil);
                world_set_pose(w->actor, "Picking", -1);
                w->ms = 1500; w->phase = 5;
                return 0;
            }
            if ((w->ms -= ms) > 0) return 0;
            Item *got[4];
            int n = 0;
            if (!world_thing_there(soil) || !items_take(me, CAT_OBJECT, seed, 1, got, 4, &n)) return -1;
            for (int k = 0; k < n; k++) items_free(got[k]);
            int owner = world_thing_belongs_to(soil);
            world_thing_become(soil, plantId);
            if (owner >= 0) world_thing_set_belongs(soil, owner); /* the same field, the same owner */
            if (heard(w->actor)) sound_play(num(get(object_def(plantId), "Plant"), "HarvestSoundResourceId", -1));
            world_set_pose(w->actor, "Idle", -1);
            return 1;
        }
        case ST_READ:
        {
            /* Lab_schedule: the mage sits in a chair of its lab and reads a while */
            int chair = s->a;
            if (world_seated_on(w->actor) == chair)
            {
                if (w->phase < 6) { w->phase = 6; w->ms = 20000 + rnd(20000); }
                if ((w->ms -= ms) > 0) return 0;
                world_stand_up(w->actor);
                return 1;
            }
            if (world_walking(w->actor)) return 0;
            if (w->phase == 6) return 1; /* got up: done */
            if (++w->tries > 5) return -1;
            if (world_go_to_furniture(w->actor, 0, chair)) return 0;
            return -1;
        }
        case ST_PUT_BACK:
        {
            /* Tool_schedule::ending: a tool it took goes back into the chest, or onto the spot, it came from */
            Borrowed *b = &w->borrowed[s->a];
            if (items_count_any(me, CAT_OBJECT, b->objectId) < 1) return 1;
            if (b->chest && world_thing_there(b->chest))
            {
                int at = approach(w, b->chest);
                if (at < 0) return 1;
                if (!at) return 0;
            }
            else
            {
                int ax, ay, af;
                world_actor_cell(w->actor, &ax, &ay, &af);
                if (abs(ax - b->x) > 1 || abs(ay - b->y) > 1)
                {
                    if (world_walking(w->actor)) return 0;
                    if (w->phase == 1 || !world_walk_to(w->actor, b->x, b->y, af, 1)) { int f; world_actor_cell(w->actor, &ax, &ay, &f); b->x = ax; b->y = ay; }
                    else { w->phase = 1; return 0; }
                }
            }
            if (w->phase < 2) { world_set_pose(w->actor, "Picking", -1); w->phase = 2; w->ms = 600; return 0; }
            if ((w->ms -= ms) > 0) return 0;
            Item *got[4];
            int n = 0;
            if (items_take(me, CAT_OBJECT, b->objectId, 1, got, 4, &n))
                for (int k = 0; k < n; k++)
                {
                    if (b->chest && world_thing_there(b->chest) && items_put_into(chest_item(b->chest), got[k], 0, 0, 0)) continue;
                    int uid = world_thing_put(CAT_OBJECT, items_item_id(got[k]), items_quantity(got[k]), b->x, b->y, 1);
                    if (uid >= 0) items_park(uid, got[k]); else items_free(got[k]);
                }
            world_set_pose(w->actor, "Idle", -1);
            return 1;
        }
    }
    return -1;
}

/* ---------------- Starting a round ---------------- */

/* The first station in its area (its own first, then the nearest) with a recipe it can make now - later steps first (thread at the loom before cotton at the wheel). */
static int start_station_round(Worker *w)
{
    int stations[64], n = 0;
    for (int i = 0; i < world_thing_count() && n < 64; i++)
    {
        int uid = world_thing_uid_at(i);
        if (uid && world_thing_type(uid) == OT_STATION && in_area(w, uid) && mine_or_nobodys(w, uid) && !station_busy(uid, w->actor)) stations[n++] = uid;
    }
    for (int i = 1; i < n; i++)
        for (int j = i; j > 0; j--)
        {
            int a = stations[j - 1], b = stations[j];
            int ka = (owner_of(a) == me_id(w) ? 0 : 100000) + distance_to(w, a), kb = (owner_of(b) == me_id(w) ? 0 : 100000) + distance_to(w, b);
            if (kb >= ka) break;
            stations[j - 1] = b; stations[j] = a;
        }
    int madeHere[128], m = 0;
    for (int i = 0; i < n; i++)
    {
        cJSON *r;
        cJSON_ArrayForEach(r, get(get(object_def(world_thing_object_id(stations[i])), "Crafting"), "Recipes")) if (num(r, "OutputObjectId", -1) >= 0 && m < 128) madeHere[m++] = num(r, "OutputObjectId", -1);
    }
    for (int pass = 0; pass < 2; pass++)
        for (int i = 0; i < n; i++)
        {
            cJSON *r, *in;
            int index = -1;
            cJSON_ArrayForEach(r, get(get(object_def(world_thing_object_id(stations[i])), "Crafting"), "Recipes"))
            {
                index++;
                if (num(r, "OutputObjectId", -1) < 0) continue;
                int usesMadeHere = 0;
                cJSON_ArrayForEach(in, get(r, "Inputs")) for (int k = 0; k < m; k++) usesMadeHere |= num(in, "ObjectId", -1) == madeHere[k];
                if ((pass == 0) != usesMadeHere) continue;
                int ok = 1;
                cJSON_ArrayForEach(in, get(r, "Inputs"))
                    if (num(in, "ObjectId", -1) >= 0 && available(w, num(in, "ObjectId", -1)) < (num(in, "Quantity", 1) > 1 ? num(in, "Quantity", 1) : 1)) ok = 0;
                if (!ok || !tool_available(w, str(r, "ToolKind"))) continue;
                w->stepCount = 0;
                cJSON_ArrayForEach(in, get(r, "Inputs")) if (num(in, "ObjectId", -1) >= 0) add(w, ST_FETCH, num(in, "ObjectId", -1), num(in, "Quantity", 1) > 1 ? num(in, "Quantity", 1) : 1, 0, NULL);
                if (str(r, "ToolKind")[0]) { add(w, ST_GET_TOOL, 0, 0, 0, str(r, "ToolKind")); add(w, ST_READY_TOOL, 0, 0, 0, str(r, "ToolKind")); }
                add(w, ST_GOTO, stations[i], 0, 0, NULL);
                add(w, ST_WORK, stations[i], index, 0, NULL);
                add(w, ST_DELIVER_MADE, 0, 0, 0, NULL);
                begin_round(w);
                return 1;
            }
        }
    return 0;
}

static int field_claimed(int uid)
{
    for (int a = 0; a < 256; a++)
    {
        Worker *o = &workers[a];
        if (!o->on || o->step >= o->stepCount) continue;
        for (int k = o->step; k < o->stepCount; k++) if ((o->steps[k].kind == ST_HARVEST || o->steps[k].kind == ST_SOW) && o->steps[k].a == uid) return 1;
    }
    return 0;
}

/* Exult's Farmer_schedule: the nearest ripe plant (with its tool) harvested and picked up; carried off when its arms are full (8) or nothing is ripe; then empty soil sown. */
static int start_field_round(Worker *w)
{
    int carrying = 0, chest, ox, oy;
    for (int k = 0; k < w->produceCount; k++) carrying += items_count_any(me_id(w), CAT_OBJECT, w->produceIds[k]);
    int ripe = 0, best = 1 << 30;
    for (int i = 0; i < world_thing_count(); i++)
    {
        int uid = world_thing_uid_at(i);
        if (!uid || world_thing_type(uid) != OT_PLANT) continue;
        cJSON *p = get(object_def(world_thing_object_id(uid)), "Plant");
        if (num(p, "ProduceObjectId", -1) < 0 || world_thing_stage(uid) < world_thing_stage_count(uid) - 1) continue;
        if (!in_area(w, uid) || !mine_or_nobodys(w, uid) || field_claimed(uid) || !tool_available(w, str(p, "HarvestToolKind"))) continue;
        int d = distance_to(w, uid);
        if (d < best) { best = d; ripe = uid; }
    }
    if (carrying > 0 && (!ripe || carrying >= 8) && output_place(w, &chest, &ox, &oy))
    {
        w->stepCount = 0;
        add(w, ST_DELIVER_CARRIED, 0, 0, 0, NULL);
        begin_round(w);
        return 1;
    }
    if (ripe)
    {
        const char *kind = str(get(object_def(world_thing_object_id(ripe)), "Plant"), "HarvestToolKind");
        w->stepCount = 0;
        if (kind[0]) { add(w, ST_GET_TOOL, 0, 0, 0, kind); add(w, ST_READY_TOOL, 0, 0, 0, kind); }
        add(w, ST_GOTO, ripe, 0, 0, NULL);
        add(w, ST_HARVEST, ripe, 0, 0, NULL);
        add(w, ST_PICK_PRODUCE, 0, 0, 0, NULL);
        begin_round(w);
        return 1;
    }
    /* sowing: empty soil in its area it has seeds for */
    cJSON *o;
    for (int i = 0; i < world_thing_count(); i++)
    {
        int soil = world_thing_uid_at(i);
        if (!soil || world_thing_object_id(soil) < 0 || !in_area(w, soil) || !mine_or_nobodys(w, soil) || field_claimed(soil)) continue;
        cJSON_ArrayForEach(o, get(get(project(), "Objects"), "Objects"))
        {
            cJSON *p = get(o, "Plant");
            if (num(o, "Type", -1) != OT_PLANT || num(p, "SeedObjectId", -1) < 0 || num(p, "SoilObjectId", -2) != world_thing_object_id(soil)) continue;
            if (available(w, num(p, "SeedObjectId", -1)) < 1) continue;
            w->stepCount = 0;
            add(w, ST_FETCH, num(p, "SeedObjectId", -1), 1, 0, NULL);
            add(w, ST_GOTO, soil, 0, 0, NULL);
            add(w, ST_SOW, soil, num(o, "Id", -1), num(p, "SeedObjectId", -1), NULL);
            begin_round(w);
            return 1;
        }
    }
    return 0;
}

static int start_reading(Worker *w)
{
    int chair = 0, best = 1 << 30;
    for (int i = 0; i < world_thing_count(); i++)
    {
        int uid = world_thing_uid_at(i);
        if (!uid || world_thing_type(uid) != OT_CHAIR || !in_area(w, uid) || !mine_or_nobodys(w, uid)) continue;
        int key = (owner_of(uid) == me_id(w) ? 0 : 100000) + distance_to(w, uid);
        if (key < best) { best = key; chair = uid; }
    }
    if (!chair) return 0;
    w->stepCount = 0;
    add(w, ST_READ, chair, 0, 0, NULL);
    begin_round(w);
    if (rnd(3) == 0) world_bark(w->actor, "Now, where was I...", 2500);
    return 1;
}

/* ---------------- The schedule's side ---------------- */

/* A work schedule's now_what: the next round of work, or - nothing to do - a word, and a while later again. 1 = it's at work. */
int work_now_what(int actor, int activity, cJSON *entry, int destX, int destY)
{
    if (actor <= 0 || actor >= 256) return 0;
    Worker *w = &workers[actor];
    if (w->on) return 1;
    if (w->activity != activity) { w->saidIdle = 0; }
    w->actor = actor; w->activity = activity; w->entry = entry; w->destX = destX; w->destY = destY;
    if (w->waitMs > 0) return 0;
    int started = is_station_work(activity) ? start_station_round(w) : start_field_round(w);
    if (started) { w->saidIdle = 0; return 1; }
    if (!w->saidIdle)
    {
        w->saidIdle = 1;
        if (!cJSON_IsFalse(get(character_json(me_id(w)), "IsHuman"))) world_bark(actor, is_field_work(activity) ? "Nothing to harvest yet." : "Nothing to work with...", 2500);
    }
    if (activity == S_LAB && start_reading(w)) return 1; /* between brews: sits and reads */
    if (!world_seated(actor)) world_set_pose(actor, "Idle", -1);
    w->waitMs = 15000 + rnd(10000); /* look again a little later */
    return 0;
}

int work_busy(int actor) { return actor > 0 && actor < 256 && workers[actor].on; }

/* Its schedule changed (or a fight, or an event took it): the round stops; tools it borrowed go back. */
void work_schedule_changed(int actor, int newActivity)
{
    if (actor <= 0 || actor >= 256) return;
    Worker *w = &workers[actor];
    if (w->on && !w->tidying) { w->on = 0; world_walk_cancel(actor); }
    for (int i = 0; i < 2048; i++) if (stationUser[i] == actor + 1) { stationUser[i] = 0; if (world_thing_there(i)) world_thing_set_lit(i, 0); }
    if (work_is_work(newActivity) || !w->borrowedCount) { w->activity = newActivity; return; }
    /* Tool_schedule::ending */
    w->stepCount = 0;
    for (int k = 0; k < w->borrowedCount && k < 16; k++) add(w, ST_PUT_BACK, k, 0, 0, NULL);
    w->tidying = 1;
    w->activity = newActivity;
    begin_round(w);
}

void work_update(double ms)
{
    for (int a = 1; a < 256 && a < world_actor_count(); a++)
    {
        Worker *w = &workers[a];
        if (w->waitMs > 0) w->waitMs -= ms;
        if (!w->on) continue;
        if (world_actor_gone(a) || vitals_dead(world_actor_character(a)) || vitals_incapacitated(world_actor_character(a)) || combat_busy(a) || events_actor_busy(a) || events_actor_held(a))
        {
            /* a fight (or an event) comes first: this round is over */
            w->on = 0;
            for (int i = 0; i < 2048; i++) if (stationUser[i] == a + 1) { stationUser[i] = 0; if (world_thing_there(i)) world_thing_set_lit(i, 0); }
            w->waitMs = 4000 + rnd(3000);
            continue;
        }
        if (w->step >= w->stepCount)
        {
            w->on = 0;
            if (w->tidying) { w->tidying = 0; w->borrowedCount = 0; w->produceCount = 0; }
            if (!world_seated(a)) world_set_pose(a, "Idle", -1);
            w->waitMs = 600;
            if (w->activity == S_LAB && rnd(2) == 0) start_reading(w);
            continue;
        }
        int r = run_step(w, &w->steps[w->step], ms);
        if (r > 0) next_step(w);
        else if (r < 0)
        {
            w->on = 0;
            if (w->tidying) { w->tidying = 0; w->borrowedCount = 0; }
            world_walk_cancel(a);
            if (!world_seated(a)) world_set_pose(a, "Idle", -1);
            w->waitMs = 4000 + rnd(3000);
        }
    }
}

int work_station_in_use(int uid) { return uid > 0 && uid < 2048 && stationUser[uid] != 0; }

void work_reset(void) { memset(workers, 0, sizeof workers); memset(stationUser, 0, sizeof stationUser); }
