/*
 * The things that work, as the Windows engine has them (GameTestWindow.Locks,
 * Travel, Mechanisms, Crafting), ported from Exult's item usecode:
 *  - Keys and lockpicks (keyring.uc KeyInternal / UseKeyOnChest /
 *    UseKeyOnDoor): double-click, click a door or a container. The right key
 *    unlocks it, or locks it (a door is closed first; "The key is inside the
 *    chest!"); the wrong one doesn't fit. A lockpick: Dexterity against the
 *    lock's difficulty (50% at equal, 5% a point, 5..95%); a failure breaks
 *    the pick. Magic locks take no key or pick.
 *  - Keychains: click yourself - every key the party carries goes on it
 *    ("Added 3 keys"); click a key - onto the ring; a door or a container -
 *    it works as whichever key fits ("We don't have that key.").
 *  - Windows' shutters; levers, switches and buttons working what they're
 *    linked to; ladders to their destination.
 *  - Sextant (Exult's coordinates), map (its picture till a click, a cross
 *    for the party with a sextant outdoors), clock (pocketwatch.uc; a sundial
 *    only outdoors by day), musical instruments.
 */
#include "mechanisms.h"
#include "work.h"
#include "world.h"
#include "items.h"
#include "events.h"
#include "vitals.h"
#include "combat.h"
#include "gumps.h"
#include "sound.h"
#include "conditions.h"
#include "font.h"
#include "magic.h"
#include "platform.h"
#include "pictures.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

enum
{
    OT_DOOR = 3, OT_LIGHT = 4, OT_KEY = 10, OT_LOCKPICK = 11, OT_WINDOW = 12, OT_KEYCHAIN = 14, OT_SEXTANT = 15, OT_MAP = 16,
    OT_LEVER = 22, OT_STAIRS = 23, OT_CLOCK = 25, OT_INSTRUMENT = 28, OT_EXPLOSIVE = 29,
    OT_PLANT = 13, OT_FOOD = 17, OT_TOOL = 26, OT_STATION = 27, OT_BEDROLL = 30, OT_LIQUID = 33
};
enum { TR_TRADE = 1, TR_USE = 2, TR_AFTER_READ = 5, TR_ENTER = 9, TR_STAY = 10, TR_EXIT = 11 };
enum { K_DEXTERITY = 2, K_MAGIC = 5, K_MANA = 7 };
enum { BARK_WRONG_KEY = 8, BARK_LOCKPICK_FAILED = 9 };

static uint32_t rng = 1597334677u;
static int rnd(int n) { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return n > 0 ? (int)(((uint64_t)rng * (uint32_t)n) >> 32) : 0; }

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static double dbl(cJSON *o, const char *k, double fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valuedouble : fallback; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }
static cJSON *project(void) { return get((cJSON *)world_json(), "Project"); }
static const Sprite *link_sprite(cJSON *link) { return flag(link, "IsSet") ? sprite_find(str(link, "SpriteGroup"), str(link, "SpriteName")) : NULL; }

static cJSON *object_def(int id)
{
    cJSON *o;
    cJSON_ArrayForEach(o, get(get(project(), "Objects"), "Objects")) if (num(o, "Id", -2) == id) return o;
    return NULL;
}

static cJSON *container_def(int id)
{
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Containers"), "Containers")) if (num(c, "Id", -2) == id) return c;
    return NULL;
}

static cJSON *character_json(int characterId)
{
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Characters"), "Characters")) if (num(c, "Id", -2) == characterId) return c;
    return NULL;
}

static int stat_of(int actor, int kind, int fallback)
{
    cJSON *s, *values = get(character_json(world_actor_character(actor)), "StatValues");
    cJSON_ArrayForEach(s, get(get(project(), "Stats"), "Stats"))
        if (num(s, "Kind", 0) == kind)
        {
            char key[16];
            snprintf(key, sizeof key, "%d", num(s, "Id", -1));
            const char *v = cJSON_GetStringValue(get(values, key));
            return v && *v ? atoi(v) : fallback;
        }
    return fallback;
}

static void say(const char *line) { game_log(line); }
static int in_party(int actor) { return actor == 0 || items_in_party(world_actor_character(actor)); }
static int alive(int actor) { return actor >= 0 && !world_actor_gone(actor) && !vitals_dead(world_actor_character(actor)); }
static int human(int actor) { return !cJSON_IsFalse(get(character_json(world_actor_character(actor)), "IsHuman")); }

/* randomPartyBark / PartySay: a random member of the party who can speak (the player included). */
static int random_speaker(void)
{
    int who[64], n = 0;
    for (int a = 0; a < world_actor_count() && n < 64; a++) if (alive(a) && in_party(a) && human(a)) who[n++] = a;
    return n ? who[rnd(n)] : -1;
}
static void party_say(const char *text) { int a = random_speaker(); if (a >= 0) world_bark(a, text, 2500); }
static void party_bark(int kind) { int a = random_speaker(); if (a >= 0) vitals_bark(world_actor_character(a), kind); }

/* ---------------- The thing being used: on the map (uid) or carried (item) ---------------- */

typedef struct Used { cJSON *def; int uid; Item *item; int holder; } Used;
static Used using;
static int harvestBy = -1; /* who harvests (a worker), or -1: the player */
static int slider_click(int mx, int my);
static void draw_slider(uint32_t *p);
static void draw_sleep(uint32_t *p);
static void use_station(int uid, cJSON *obj, const char *toolKind);
static void use_plant(int uid, cJSON *obj, const char *toolKind);
typedef struct Milking { int on, beats, animal, liquid; double ms; Used u; } Milking;
static Milking milking;
static long lastMinute = -1;
typedef struct Job { int uid; cJSON *obj, *recipe; double left, nextBeat; } Job;
static Job jobs[16]; static int jobCount;
typedef struct Sleep { int phase, hours, bedroll; double ms; } Sleep; /* phase 1: falling dark, 2: rising */
static Sleep sleep;
typedef struct Slider { int open, bed, value, min, max, step, minHours; char question[128], cancel[96]; } Slider;
static Slider slider;

static int still_there(const Used *u) { return u->uid > 0 ? world_thing_there(u->uid) : items_alive(u->item); }
static int fire_use(const Used *u) { return u->uid > 0 ? events_object_event(u->uid, TR_USE, -1) : events_fire_item(CAT_OBJECT, num(u->def, "Id", -1), TR_USE); }
static int user_of(const Used *u) { int a = u->holder >= 0 ? world_actor_by_character(u->holder) : -1; return alive(a) ? a : 0; }

/* Words over it: over the thing on the map, else over whoever carries it. */
static void say_over(const Used *u, const char *text, int ms)
{
    if (u->uid > 0) world_bark_thing(u->uid, text, ms);
    else world_bark(user_of(u), text, ms);
}

static void use_up_one(const Used *u)
{
    if (u->uid > 0)
    {
        if (world_thing_quantity(u->uid) > 1) { world_thing_add_quantity(u->uid, -1); return; }
        int c, i, q, x, y, n;
        world_thing_info(u->uid, &c, &i, &q, &x, &y, &n);
        items_free(items_unpark(u->uid, c, i, q));
        world_thing_remove(u->uid);
        return;
    }
    items_use_up_one(u->item);
}

/* ---------------- Locks ---------------- */

/* A container a click picked - on the map or carried - with its Container Editor entry (bodies don't count). */
typedef struct Box { Item *item; int uid; cJSON *data; } Box;

static int box_of(const GumpsPick *pick, Box *box)
{
    memset(box, 0, sizeof *box);
    if (pick->uid > 0)
    {
        int c, i, q, x, y, n;
        world_thing_info(pick->uid, &c, &i, &q, &x, &y, &n);
        if (c != CAT_CONTAINER) return 0;
        box->uid = pick->uid; box->item = items_parked(pick->uid, c, i, q); box->data = container_def(i);
    }
    else if (pick->item && items_category(pick->item) == CAT_CONTAINER) { box->item = pick->item; box->data = container_def(items_item_id(pick->item)); }
    return box->data && !flag(box->data, "IsBodyContainer");
}

static int door_of(const GumpsPick *pick) { return pick->uid > 0 && world_thing_type(pick->uid) == OT_DOOR ? pick->uid : 0; }

static int unlock_sound(cJSON *d) { return num(d, "UnlockSoundResourceId", -1) >= 0 ? num(d, "UnlockSoundResourceId", -1) : num(d, "LockSoundResourceId", -1); }
static int lock_sound(cJSON *d) { return num(d, "LockSoundResourceId", -1) >= 0 ? num(d, "LockSoundResourceId", -1) : num(d, "UnlockSoundResourceId", -1); }

static void say_over_box(const Box *b, const char *text) { if (b->uid > 0) world_bark_thing(b->uid, text, 1500); else gumps_label(text); }

static void wrong_key(cJSON *keyDef, const char *what, cJSON *lockData)
{
    char line[200];
    snprintf(line, sizeof line, "\"%s\" doesn't fit \"%s\".", str(keyDef, "Name"), what);
    say(line);
    sound_play(num(lockData, "WrongKeySoundResourceId", -1));
    party_bark(BARK_WRONG_KEY);
}

/* keyring.uc UseKeyOnDoor: the right key locks or unlocks it (an open door is closed first); a magic lock takes no key. */
static void key_on_door(cJSON *key, int door)
{
    cJSON *obj = object_def(world_thing_object_id(door)), *d = get(obj, "Door");
    char line[200];
    if (flag(d, "MagicallyLocked") || num(d, "RequiredKeyItemId", -1) != num(key, "Id", -2)) { wrong_key(key, str(obj, "Name"), d); return; }
    int open, locked;
    world_door_state(door, &open, &locked);
    if (locked)
    {
        world_door_set_locked(door, 0);
        sound_play(unlock_sound(d));
        world_bark_thing(door, "Unlocked", 1500);
        snprintf(line, sizeof line, "You unlock \"%s\".", str(obj, "Name"));
        say(line);
        conditions_record_item("unlockkey", CAT_OBJECT, num(obj, "Id", -1));
        events_object_event(door, TR_STAY, -1);
        return;
    }
    if (open && !world_door_close(door)) return; /* can't lock it open */
    world_door_set_locked(door, 1);
    sound_play(lock_sound(d));
    world_bark_thing(door, "Locked", 1500);
    snprintf(line, sizeof line, "You lock \"%s\".", str(obj, "Name"));
    say(line);
}

/* keyring.uc UseKeyOnChest. keyItem: the key itself when carried (it can't lock the chest it's in). */
static void key_on_box(cJSON *key, Item *keyItem, const Box *b)
{
    char line[200];
    int fits = num(b->data, "RequiredKeyCategory", -1) == CAT_OBJECT && num(b->data, "RequiredKeyItemId", -1) == num(key, "Id", -2);
    if (!fits) { wrong_key(key, str(b->data, "Name"), b->data); return; }
    if (items_is_locked(b->item))
    {
        sound_play(unlock_sound(b->data));
        items_set_locked(b->item, 0);
        say_over_box(b, "Unlocked");
        conditions_record_item("unlockkey", CAT_CONTAINER, num(b->data, "Id", -1));
        snprintf(line, sizeof line, "You unlock \"%s\".", str(b->data, "Name"));
        say(line);
        return;
    }
    if (keyItem && items_holds(b->item, keyItem)) { snprintf(line, sizeof line, "The key is inside the %s!", str(b->data, "Name")); world_bark(0, line, 3000); return; }
    gumps_close_container(b->item); /* its windows close, and those of every container inside it */
    sound_play(lock_sound(b->data));
    items_set_locked(b->item, 1);
    say_over_box(b, "Locked");
    snprintf(line, sizeof line, "You lock \"%s\".", str(b->data, "Name"));
    say(line);
}

static int pick_chance(int difficulty) { int c = (stat_of(0, K_DEXTERITY, 10) - difficulty) * 5 + 50; return c < 5 ? 5 : c > 95 ? 95 : c; }

static void lockpick_on_door(const Used *u, int door)
{
    cJSON *obj = object_def(world_thing_object_id(door)), *d = get(obj, "Door");
    char line[240];
    int open, locked;
    world_door_state(door, &open, &locked);
    if (!locked) { snprintf(line, sizeof line, "\"%s\" isn't locked.", str(obj, "Name")); say(line); return; }
    if (flag(d, "MagicallyLocked")) { snprintf(line, sizeof line, "\"%s\" is magically locked - no lockpick opens it.", str(obj, "Name")); say(line); return; }
    int chance = pick_chance(num(d, "PickLockDifficulty", 0));
    if (rnd(100) < chance)
    {
        world_door_set_locked(door, 0);
        sound_play(unlock_sound(d));
        world_bark_thing(door, "Unlocked", 1500);
        snprintf(line, sizeof line, "You pick the lock on \"%s\". (%d%% chance)", str(obj, "Name"), chance);
        say(line);
        conditions_record_item("picklocked", CAT_OBJECT, num(obj, "Id", -1));
        events_object_event(door, TR_STAY, -1);
        return;
    }
    use_up_one(u);
    sound_play(num(d, "LockpickBrokeSoundResourceId", -1));
    vitals_bark(world_actor_character(0), BARK_LOCKPICK_FAILED);
    snprintf(line, sizeof line, "You fail to pick the lock on \"%s\", and \"%s\" breaks. (%d%% chance)", str(obj, "Name"), str(u->def, "Name"), chance);
    say(line);
    events_object_event(door, TR_EXIT, -1);
}

static void lockpick_on_box(const Used *u, const Box *b)
{
    char line[240];
    if (!items_is_locked(b->item)) { snprintf(line, sizeof line, "\"%s\" isn't locked.", str(b->data, "Name")); say(line); return; }
    int chance = pick_chance(num(b->data, "PickLockDifficulty", 0));
    if (rnd(100) < chance)
    {
        items_set_locked(b->item, 0);
        sound_play(unlock_sound(b->data));
        say_over_box(b, "Unlocked");
        conditions_record_item("picklocked", CAT_CONTAINER, num(b->data, "Id", -1));
        snprintf(line, sizeof line, "You pick the lock on \"%s\". (%d%% chance)", str(b->data, "Name"), chance);
        say(line);
        return;
    }
    use_up_one(u);
    sound_play(num(b->data, "LockpickBrokeSoundResourceId", -1));
    vitals_bark(world_actor_character(0), BARK_LOCKPICK_FAILED);
    snprintf(line, sizeof line, "You fail to pick the lock on \"%s\", and \"%s\" breaks. (%d%% chance)", str(b->data, "Name"), str(u->def, "Name"), chance);
    say(line);
}

static void lock_tool_target(void *context, const GumpsPick *pick)
{
    Used *u = (Used *)context;
    if (!still_there(u)) return;
    int key = num(u->def, "Type", -1) == OT_KEY, door = door_of(pick);
    Box b;
    if (door) { if (key) key_on_door(u->def, door); else lockpick_on_door(u, door); return; }
    if (!box_of(pick, &b)) return; /* "The target cannot be unlocked": nothing happens */
    if (key) key_on_box(u->def, u->item, &b); else lockpick_on_box(u, &b);
}

/* ---------------- Keychains (keyring.uc) ---------------- */

/* Its keys: the item's own list (a placed keychain has its item too), started from its Starting Keys. */
static Item *ring_of(const Used *u)
{
    Item *ring = u->uid > 0 ? items_parked(u->uid, CAT_OBJECT, num(u->def, "Id", -1), 1) : u->item;
    if (ring && !items_keys_set(ring))
    {
        cJSON *k;
        cJSON_ArrayForEach(k, get(get(u->def, "Keychain"), "StartingKeyIds")) if (cJSON_IsNumber(k)) items_add_key(ring, k->valueint);
        items_mark_keys_set(ring);
    }
    return ring;
}

static void show_ring(const Used *u, Item *ring) { if (u->uid > 0) { int n = items_key_count(ring); world_thing_set_frame(u->uid, n < 4 ? n : 4); } } /* set_keyring_frame */

/* Keyring_data.add_party_keys: every key the party carries goes on the ring. */
static void add_party_keys(const Used *u)
{
    Item *ring = ring_of(u), *all[256];
    int added = 0;
    char line[160];
    for (int a = 0; a < world_actor_count(); a++)
    {
        if (!alive(a) || !in_party(a)) continue;
        int n = items_carried_objects(world_actor_character(a), all, 256);
        for (int i = 0; i < n; i++)
        {
            if (all[i] == u->item || num(object_def(items_item_id(all[i])), "Type", -1) != OT_KEY) continue;
            ItemPlace from;
            if (!items_detach(all[i], &from)) continue;
            items_add_key(ring, items_item_id(all[i]));
            added += items_quantity(all[i]) > 1 ? items_quantity(all[i]) : 1;
            items_detach_done(&from, all[i]);
            items_free(all[i]);
        }
    }
    sound_play(num(get(u->def, "Keychain"), "AddKeySoundResourceId", -1));
    show_ring(u, ring);
    snprintf(line, sizeof line, "Added %d keys", added);
    party_say(line);
    snprintf(line, sizeof line, "\"%s\" now holds %d key%s.", str(u->def, "Name"), items_key_count(ring), items_key_count(ring) == 1 ? "" : "s");
    say(line);
}

static void keychain_target(void *context, const GumpsPick *pick)
{
    Used *u = (Used *)context;
    if (!still_there(u)) return;
    if (pick->actor == 0) { add_party_keys(u); return; }
    if (pick->actor > 0) return;
    Item *ring = ring_of(u);
    char line[200];
    /* a key: onto the ring (gone from where it was) */
    int keyId = -1;
    if (pick->uid > 0 && world_thing_type(pick->uid) == OT_KEY) keyId = world_thing_object_id(pick->uid);
    else if (pick->item && items_category(pick->item) == CAT_OBJECT && num(object_def(items_item_id(pick->item)), "Type", -1) == OT_KEY) keyId = items_item_id(pick->item);
    if (keyId >= 0)
    {
        if (pick->uid > 0)
        {
            int c, i, q, n, x, y;
            world_thing_info(pick->uid, &c, &i, &q, &x, &y, &n);
            if (!world_reachable(x, y, world_thing_z(pick->uid))) { say("You can't reach that from here."); return; }
            items_free(items_unpark(pick->uid, c, i, q));
            world_thing_remove(pick->uid);
        }
        else { ItemPlace from; if (items_detach(pick->item, &from)) { items_detach_done(&from, pick->item); items_free(pick->item); } }
        items_add_key(ring, keyId);
        sound_play(num(get(u->def, "Keychain"), "AddKeySoundResourceId", -1));
        show_ring(u, ring);
        snprintf(line, sizeof line, "\"%s\" is now on \"%s\".", str(object_def(keyId), "Name"), str(u->def, "Name"));
        say(line);
        return;
    }
    static const char *barks[] = { "We don't have that key.", "It's not on the keyring." };
    int door = door_of(pick);
    Box b;
    if (door)
    {
        cJSON *d = get(object_def(world_thing_object_id(door)), "Door");
        int need = num(d, "RequiredKeyItemId", -1);
        if (flag(d, "MagicallyLocked") || !items_has_key(ring, need) || !object_def(need)) { party_say(barks[rnd(2)]); return; }
        sound_play(num(get(u->def, "Keychain"), "AddKeySoundResourceId", -1));
        key_on_door(object_def(need), door);
        return;
    }
    if (box_of(pick, &b))
    {
        int need = num(b.data, "RequiredKeyItemId", -1);
        if (num(b.data, "RequiredKeyCategory", -1) != CAT_OBJECT || !items_has_key(ring, need) || !object_def(need)) { party_say(barks[rnd(2)]); return; }
        sound_play(num(get(u->def, "Keychain"), "AddKeySoundResourceId", -1));
        key_on_box(object_def(need), u->item, &b);
    }
}

/* ---------------- Windows, levers, ladders ---------------- */

static void use_window(int uid, cJSON *obj)
{
    if (events_object_event(uid, TR_USE, -1)) return;
    cJSON *w = get(obj, "Window");
    char line[160];
    if (!flag(w, "HasShutters") || world_thing_spent(uid)) { snprintf(line, sizeof line, "\"%s\" - nothing happens.", str(obj, "Name")); say(line); return; }
    int open = !world_thing_open(uid);
    world_thing_set_open(uid, open);
    sound_play(num(w, open ? "OpenSoundResourceId" : "CloseSoundResourceId", -1));
    snprintf(line, sizeof line, open ? "You open the shutters of \"%s\"." : "You close the shutters of \"%s\".", str(obj, "Name"));
    say(line);
}

typedef struct Button { int uid; double ms; } Button;
static Button buttons[32]; static int buttonCount;

static void flip_lever(int uid, cJSON *obj, int depth);

/* Everything the lever is linked to (Map Editor) changes over; linked levers flip too - a loop of them stops after a few. */
static void work_links(int lever, int depth)
{
    if (depth > 4) return;
    cJSON *id;
    cJSON_ArrayForEach(id, get(world_thing_entry(lever), "LinkedInstanceIds"))
    {
        int target = cJSON_IsNumber(id) ? world_thing_by_instance(id->valueint) : 0;
        cJSON *obj = target ? object_def(world_thing_object_id(target)) : NULL;
        if (!obj) continue;
        switch (num(obj, "Type", -1))
        {
            case OT_DOOR:
            {
                int open, locked;
                world_door_state(target, &open, &locked);
                if (open) { if (world_door_close(target)) sound_play(num(get(obj, "Door"), "CloseSoundResourceId", -1)); }
                else { world_door_set_locked(target, 0); world_door_open(target); sound_play(num(get(obj, "Door"), "OpenSoundResourceId", -1)); } /* a locked one is unlocked, as a portcullis winch */
                break;
            }
            case OT_LIGHT:
            {
                if (world_light_spent(target)) break;
                int lit = !world_light_lit(target);
                world_light_set(target, lit);
                if (lit) world_light_lit_now(target);
                sound_play(num(get(obj, "Light"), lit ? "LitSoundResourceId" : "OffSoundResourceId", -1));
                break;
            }
            case OT_WINDOW:
                if (!flag(get(obj, "Window"), "HasShutters") || world_thing_spent(target)) break;
                world_thing_set_open(target, !world_thing_open(target));
                sound_play(num(get(obj, "Window"), world_thing_open(target) ? "OpenSoundResourceId" : "CloseSoundResourceId", -1));
                break;
            case OT_EXPLOSIVE: combat_force_fuse(target); break;
            case OT_LEVER: if (num(get(obj, "Lever"), "Kind", 0) != 2) flip_lever(target, obj, depth + 1); break;
            default: events_object_event(target, TR_USE, -1); break;
        }
    }
}

static void flip_lever(int uid, cJSON *obj, int depth)
{
    int on = !world_thing_open(uid);
    world_thing_set_open(uid, on);
    sound_play(num(get(obj, "Lever"), on ? "OnSoundResourceId" : "OffSoundResourceId", -1));
    events_object_event(uid, on ? TR_AFTER_READ : TR_ENTER, -1);
    work_links(uid, depth);
}

static void pull_lever(int uid, cJSON *obj)
{
    if (events_object_event(uid, TR_USE, -1)) return;
    cJSON *l = get(obj, "Lever");
    char line[160];
    if (num(l, "Kind", 0) == 2)
    {
        /* a button: pressed, it springs back after a while */
        if (world_thing_open(uid) || buttonCount >= 32) return;
        world_thing_set_open(uid, 1);
        double reset = dbl(l, "ButtonResetSeconds", 2) * 1000;
        buttons[buttonCount++] = (Button){ uid, reset > 100 ? reset : 100 };
        sound_play(num(l, "OnSoundResourceId", -1));
        snprintf(line, sizeof line, "You press \"%s\".", str(obj, "Name"));
        say(line);
        events_object_event(uid, TR_AFTER_READ, -1);
        work_links(uid, 0);
        return;
    }
    flip_lever(uid, obj, 0);
    snprintf(line, sizeof line, world_thing_open(uid) ? "You pull \"%s\"." : "You push \"%s\" back.", str(obj, "Name"));
    say(line);
}

static void use_stairs(int uid, cJSON *obj)
{
    if (events_object_event(uid, TR_USE, -1)) return;
    cJSON *s = get(obj, "Stairs"), *entry = world_thing_entry(uid);
    char line[200];
    if (num(s, "Kind", 0) != 1) { snprintf(line, sizeof line, "Walk up \"%s\" a step at a time.", str(obj, "Name")); say(line); return; }
    int dx = num(entry, "DestinationX", -1), dy = num(entry, "DestinationY", -1);
    if (dx < 0 || dy < 0 || dx >= world_map_w() || dy >= world_map_h()) { snprintf(line, sizeof line, "\"%s\" leads nowhere.", str(obj, "Name")); say(line); return; }
    if (vitals_incapacitated(world_actor_character(0))) return;
    sound_play(num(s, "ClimbSoundResourceId", -1));
    world_actor_place_lift(0, dx, dy, num(entry, "DestinationLift", 0)); /* arrives at the floor at (or just under) its lift */
    events_player_moved();
    snprintf(line, sizeof line, "You climb \"%s\".", str(obj, "Name"));
    say(line);
}

/* ---------------- Sextant, map, clock, instruments ---------------- */

static void use_sextant(const Used *u)
{
    if (fire_use(u)) return;
    char line[160];
    if (world_indoors()) { snprintf(line, sizeof line, "\"%s\" can't see the sky from in here.", str(u->def, "Name")); say(line); return; }
    cJSON *s = get(u->def, "Sextant");
    int ox = num(s, "OriginCellX", -1) >= 0 ? num(s, "OriginCellX", -1) : world_map_w() / 2, oy = num(s, "OriginCellY", -1) >= 0 ? num(s, "OriginCellY", -1) : world_map_h() / 2;
    double originX = (ox + 0.5) * world_tile_w() / 8.0, originY = (oy + 0.5) * world_tile_h() / 8.0;
    int perDegree = num(s, "TilesPerDegree", 10) > 1 ? num(s, "TilesPerDegree", 10) : 1, tx, ty;
    world_player_units(&tx, &ty);
    int lat = (int)((ty - originY) / perDegree), lon = (int)((tx - originX) / perDegree); /* truncated toward 0, as C does */
    snprintf(line, sizeof line, "%d %s, %d %s", abs(lat), lat < 0 ? "North" : "South", abs(lon), lon < 0 ? "West" : "East");
    say_over(u, line, 4000);
}

static void use_clock(const Used *u)
{
    if (fire_use(u)) return;
    int hour = events_hour(), minute = events_minute();
    char line[64];
    if (flag(get(u->def, "Clock"), "Sundial") && (world_indoors() || hour < 6 || hour >= 18)) { snprintf(line, sizeof line, "\"%s\" casts no shadow.", str(u->def, "Name")); say(line); return; }
    snprintf(line, sizeof line, " %d:%02d%s", hour % 12 == 0 ? 12 : hour % 12, minute, hour < 12 ? "am" : "pm"); /* pocketwatch.uc */
    say_over(u, line, 2500);
}

static double poseBackMs;

static void use_instrument(const Used *u)
{
    if (fire_use(u)) return;
    cJSON *ins = get(u->def, "Instrument"), *songs = get(ins, "SongSoundIds");
    char line[160];
    int n = cJSON_GetArraySize(songs);
    if (!n) { snprintf(line, sizeof line, "\"%s\" - nothing happens (no tunes set).", str(u->def, "Name")); say(line); return; }
    sound_play(cJSON_GetArrayItem(songs, rnd(n))->valueint);
    int player = user_of(u);
    world_bark(player, "~ ~ ~", 3000);
    if (player == 0 && str(ins, "PlayingPose")[0] && world_set_pose(0, str(ins, "PlayingPose"), -1)) poseBackMs = 3000;
    snprintf(line, sizeof line, "You play \"%s\".", str(u->def, "Name"));
    say(line);
}

/* The map open (display_map): its picture over the game till a click. */
typedef struct MapView { int open; const Picture *map, *doll; int markX, markY, mark; } MapView;
static MapView view;
static int mapBoxes[8]; /* where the paperdoll and the map go: x, y, w, h each */

static void use_map(const Used *u)
{
    if (fire_use(u)) return;
    cJSON *m = get(u->def, "MapPicture");
    char line[160];
    const Picture *map = sprite_picture_at(link_sprite(get(m, "Picture")), 0), *doll = sprite_picture_at(link_sprite(get(m, "PaperdollSprite")), 0);
    if (!map) { snprintf(line, sizeof line, "\"%s\" has no picture (Object Editor > Map).", str(u->def, "Name")); say(line); return; }
    memset(&view, 0, sizeof view);
    view.open = 1; view.map = map; view.doll = doll;
    /* the whole view fits the screen, keeping its shape, in whole steps where it can */
    double fullW = doll ? doll->width : map->width, fullH = doll ? doll->height : map->height, scale = fmin(SCREEN_W / fullW, SCREEN_H / fullH);
    if (scale >= 1) scale = floor(scale);
    int w = (int)(fullW * scale), h = (int)(fullH * scale), x0 = (SCREEN_W - w) / 2, y0 = (SCREEN_H - h) / 2;
    double mx = x0, my = y0, mw = w, mh = h;
    if (doll)
    {
        int automatic = num(m, "PictureWidth", 0) <= 0 || num(m, "PictureHeight", 0) <= 0;
        mx = x0 + (automatic ? fullW * 0.1 : num(m, "PictureLeft", 0)) * scale; my = y0 + (automatic ? fullH * 0.1 : num(m, "PictureTop", 0)) * scale;
        mw = (automatic ? fullW * 0.8 : num(m, "PictureWidth", 0)) * scale; mh = (automatic ? fullH * 0.8 : num(m, "PictureHeight", 0)) * scale;
    }
    /* Paint_map: a cross where the party is - with a sextant in the party, outdoors (unless the map says otherwise), on the area it shows */
    int sextant = 0;
    Item *all[256];
    for (int a = 0; a < world_actor_count() && !sextant; a++)
    {
        if (!alive(a) || !in_party(a)) continue;
        int n = items_carried_objects(world_actor_character(a), all, 256);
        for (int i = 0; i < n; i++) sextant |= num(object_def(items_item_id(all[i])), "Type", -1) == OT_SEXTANT;
    }
    int whole = num(m, "AreaLeft", -1) < 0, tw = world_tile_w(), th = world_tile_h();
    double areaLeft = whole ? 0 : num(m, "AreaLeft", 0) * tw, areaTop = whole ? 0 : num(m, "AreaTop", 0) * th;
    double areaW = whole ? world_map_w() * tw : (fmax(num(m, "AreaLeft", 0), num(m, "AreaRight", 0)) - num(m, "AreaLeft", 0) + 1) * tw;
    double areaH = whole ? world_map_h() * th : (fmax(num(m, "AreaTop", 0), num(m, "AreaBottom", 0)) - num(m, "AreaTop", 0) + 1) * th;
    double px, py;
    world_player_pixel(&px, &py);
    double fx = (px - areaLeft) / fmax(1, areaW), fy = (py - areaTop) / fmax(1, areaH);
    say("Click to put the map away.");
    if (!cJSON_IsFalse(get(m, "ShowLocation")) && (cJSON_IsFalse(get(m, "OutdoorsOnly")) || !world_indoors()) && (cJSON_IsFalse(get(m, "LocationNeedsSextant")) || sextant))
    {
        if (fx >= 0 && fx <= 1 && fy >= 0 && fy <= 1) { view.mark = 1; view.markX = (int)(mx + fx * mw); view.markY = (int)(my + fy * mh); }
        else say("You can't find where you are on this map. (Click to put it away.)");
    }
    cJSON *sounds = get(get(project(), "Settings"), "Sounds");
    sound_play(!cJSON_IsFalse(get(sounds, "PaperdollOpenSoundEnabled")) && num(sounds, "PaperdollOpenSoundId", -1) >= 0 ? num(sounds, "PaperdollOpenSoundId", -1) : num(m, "OpenSoundResourceId", -1)); /* a map is a paperdoll too */
    mapBoxes[0] = x0; mapBoxes[1] = y0; mapBoxes[2] = w; mapBoxes[3] = h;
    mapBoxes[4] = (int)mx; mapBoxes[5] = (int)my; mapBoxes[6] = (int)mw; mapBoxes[7] = (int)mh;
}

int mechanisms_map_open(void) { return view.open; }
int mechanisms_map_click(void) { if (!view.open) return 0; view.open = 0; return 1; }
int mechanisms_click(int x, int y) { if (sleep.phase) return 1; if (slider_click(x, y)) return 1; return mechanisms_map_click(); }

static void draw_map(uint32_t *pixels);

void mechanisms_draw(uint32_t *pixels)
{
    draw_map(pixels);
    if (slider.open) draw_slider(pixels);
    draw_sleep(pixels);
}

static void draw_map(uint32_t *pixels)
{
    if (!view.open) return;
    if (view.doll) world_blit(pixels, view.doll, mapBoxes[0], mapBoxes[1], mapBoxes[2], mapBoxes[3]);
    world_blit(pixels, view.map, mapBoxes[4], mapBoxes[5], mapBoxes[6], mapBoxes[7]);
    if (!view.mark) return;
    for (int k = -2; k <= 2; k++)
    {
        int x = view.markX + k, y = view.markY + k;
        if (x >= 0 && x < SCREEN_W && view.markY >= 0 && view.markY < SCREEN_H) pixels[view.markY * SCREEN_W + x] = 0xFFFF0000u;
        if (y >= 0 && y < SCREEN_H && view.markX >= 0 && view.markX < SCREEN_W) pixels[y * SCREEN_W + view.markX] = 0xFFFF0000u;
    }
}

/* ---------------- Plants (GameTestWindow.Crafting) ---------------- */


/* A plant harvested: its sound, back to an earlier stage, taken whole, or back to the soil it was sown in; what it gives appears where it stood. */
static int harvest(int uid, cJSON *obj)
{
    cJSON *p = get(obj, "Plant");
    if (harvestBy < 0 || combat_tiles_between(harvestBy, 0) <= 24) sound_play(num(p, "HarvestSoundResourceId", -1));
    int c, i, q, x, y, n, fl = world_thing_floor(uid), last = world_thing_stage_count(uid) - 1;
    world_thing_info(uid, &c, &i, &q, &x, &y, &n);
    int soil = num(p, "SoilObjectId", -1);
    if (flag(p, "RemovedWhenHarvested") && soil >= 0 && object_def(soil)) world_thing_become(uid, soil); /* the soil it was sown in, ready to sow again */
    else if (flag(p, "RemovedWhenHarvested")) { items_free(items_unpark(uid, c, i, q)); world_thing_remove(uid); }
    else { int st = num(p, "StageAfterHarvest", 0); world_thing_set_stage(uid, st < 0 ? 0 : st > last ? (last < 0 ? 0 : last) : st); }
    int produce = num(p, "ProduceObjectId", -1), many = num(p, "ProduceQuantity", 1), made = 0;
    if (object_def(produce)) made = world_thing_put(CAT_OBJECT, produce, many > 1 ? many : 1, x, y, fl);
    if (world_thing_there(uid) && world_thing_object_id(uid) == num(obj, "Id", -2)) events_object_event(uid, TR_AFTER_READ, harvestBy); /* By Harvested */
    return made > 0 ? made : 0;
}

/* A worker's harvest (Farm, Miner): its sound only near the player. The produce's uid (0: none). */
int mechanisms_harvest(int uid, int by)
{
    cJSON *obj = object_def(world_thing_object_id(uid));
    if (!obj) return 0;
    harvestBy = by;
    int made = harvest(uid, obj);
    harvestBy = -1;
    return made;
}

/* Double-clicked (toolKind NULL) or a tool used on it: harvested if it's ripe and the right tool is used. */
static void use_plant(int uid, cJSON *obj, const char *toolKind)
{
    if (!toolKind && events_object_event(uid, TR_USE, -1)) return;
    cJSON *p = get(obj, "Plant");
    char line[200], needs[64];
    if (num(p, "ProduceObjectId", -1) < 0) { snprintf(line, sizeof line, "\"%s\" - nothing happens.", str(obj, "Name")); say(line); return; }
    snprintf(needs, sizeof needs, "%s", str(p, "HarvestToolKind"));
    for (char *k = needs; *k; k++) if (*k >= 'A' && *k <= 'Z') *k = (char)(*k + 32);
    if (needs[0])
    {
        int same = toolKind != NULL;
        for (size_t k = 0; same && (needs[k] || toolKind[k]); k++) { char a = needs[k], b = toolKind[k]; if (b >= 'A' && b <= 'Z') b = (char)(b + 32); same = a == b; }
        if (!same)
        {
            snprintf(line, sizeof line, "Perhaps we could use a %s on that.", needs);
            party_say(line);
            snprintf(line, sizeof line, "You need a %s to harvest \"%s\".", needs, str(obj, "Name"));
            say(line);
            return;
        }
    }
    if (!cJSON_IsFalse(get(p, "OnlyWhenRipe")) && world_thing_stage(uid) < world_thing_stage_count(uid) - 1) { snprintf(line, sizeof line, "\"%s\" isn't ripe yet.", str(obj, "Name")); say(line); return; }
    combat_owned_theft(uid); /* someone else's crop */
    harvest(uid, obj);
    int many = num(p, "ProduceQuantity", 1);
    char count[16] = "";
    if (many > 1) snprintf(count, sizeof count, "%d ", many);
    snprintf(line, sizeof line, "You harvest %s\"%s\".", count, object_def(num(p, "ProduceObjectId", -1)) ? str(object_def(num(p, "ProduceObjectId", -1)), "Name") : "something");
    say(line);
}

/* Each game minute's growth: a stage gained every Hours Per Stage, till ripe (By Grown). */
static void grow_plants(double minutes)
{
    int plants[1024], n = world_things_of_type(OT_PLANT, plants, 1024);
    for (int i = 0; i < n; i++)
    {
        cJSON *obj = object_def(world_thing_object_id(plants[i]));
        double perStage = dbl(get(obj, "Plant"), "HoursPerStage", 24) * 60;
        int last = world_thing_stage_count(plants[i]) - 1, stage = world_thing_stage(plants[i]), grew = 0;
        if (perStage <= 0 || stage >= last) continue;
        double grown = world_thing_grow(plants[i], minutes);
        while (grown >= perStage && stage < last) { grown -= perStage; stage++; grew = 1; }
        if (!grew) continue;
        world_thing_set_stage(plants[i], stage);
        world_thing_set_grow(plants[i], grown);
        events_object_event(plants[i], TR_STAY, -1);
    }
}

/* A seed dropped on its soil (Plant: Sown from / in): the soil becomes the plant, at its first stage. */
int mechanisms_drop_on(int uid, int category, int itemId)
{
    if (category != CAT_OBJECT || world_thing_object_id(uid) < 0) return 0;
    int soil = world_thing_object_id(uid);
    cJSON *o, *sown = NULL;
    cJSON_ArrayForEach(o, get(get(project(), "Objects"), "Objects"))
        if (num(o, "Type", -1) == OT_PLANT && num(get(o, "Plant"), "SeedObjectId", -1) == itemId && num(get(o, "Plant"), "SoilObjectId", -1) == soil && itemId >= 0) { sown = o; break; }
    if (!sown) return 0;
    int c, i, q, x, y, n;
    world_thing_info(uid, &c, &i, &q, &x, &y, &n);
    if (!world_reachable(x, y, world_thing_z(uid))) { say("You can't reach that from here."); return 1; }
    combat_owned_theft(uid); /* someone else's field */
    world_thing_become(uid, num(sown, "Id", -1));
    sound_play(num(get(sown, "Plant"), "HarvestSoundResourceId", -1));
    gumps_use_up_combined();
    char line[160];
    snprintf(line, sizeof line, "You sow \"%s\".", str(sown, "Name"));
    say(line);
    return 1;
}

/* ---------------- Tools and crafting stations ---------------- */

static void at_work(double ms) { if (world_set_pose(0, "Attacking Melee", -1)) poseBackMs = ms; } /* the player's work frames for a moment */

static void tool_target(void *context, const GumpsPick *pick)
{
    Used *u = (Used *)context;
    if (!still_there(u) || pick->uid <= 0 || world_thing_object_id(pick->uid) < 0) return;
    cJSON *obj = object_def(world_thing_object_id(pick->uid));
    const char *kind = str(get(u->def, "Tool"), "ToolKind");
    sound_play(num(get(u->def, "Tool"), "UseSoundResourceId", -1));
    at_work(600);
    if (num(obj, "Type", -1) == OT_PLANT && str(get(obj, "Plant"), "HarvestToolKind")[0]) { use_plant(pick->uid, obj, kind); return; }
    if (num(obj, "Type", -1) == OT_STATION) { use_station(pick->uid, obj, kind); return; }
    if (!events_object_event(pick->uid, TR_TRADE, -1)) /* By Tool Used On It */
    {
        char line[200];
        snprintf(line, sizeof line, "Using \"%s\" on \"%s\" does nothing.", str(u->def, "Name"), str(obj, "Name"));
        say(line);
    }
}

static void use_tool(const Used *u)
{
    if (fire_use(u)) return;
    char prompt[160];
    snprintf(prompt, sizeof prompt, "Use \"%s\" on what?", str(u->def, "Name"));
    gumps_choose_target(prompt, tool_target, &using);
}

/* What the party carries of an object, all told. */
static int party_count(int objectId)
{
    int n = 0;
    for (int a = 0; a < world_actor_count(); a++) if (alive(a) && in_party(a)) n += items_count_any(world_actor_character(a), CAT_OBJECT, objectId);
    return n;
}

static void party_take(int objectId, int quantity)
{
    for (int a = 0; a < world_actor_count() && quantity > 0; a++)
    {
        if (!alive(a) || !in_party(a)) continue;
        int have = items_count_any(world_actor_character(a), CAT_OBJECT, objectId), take = have < quantity ? have : quantity;
        if (take <= 0) continue;
        Item *got[100];
        int n = 0;
        if (items_take(world_actor_character(a), CAT_OBJECT, objectId, take, got, 100, &n)) for (int k = 0; k < n; k++) items_free(got[k]);
        quantity -= take;
    }
}

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

static int has_ingredients(cJSON *recipe)
{
    cJSON *in, *other;
    cJSON_ArrayForEach(in, get(recipe, "Inputs"))
    {
        int id = num(in, "ObjectId", -1), need = 0;
        if (id < 0) continue;
        cJSON_ArrayForEach(other, get(recipe, "Inputs")) if (num(other, "ObjectId", -1) == id) need += num(other, "Quantity", 1) > 1 ? num(other, "Quantity", 1) : 1;
        if (party_count(id) < need) return 0;
    }
    return 1;
}


/* Double-clicked (toolKind NULL) or a tool used on it: the first recipe the party can make (millstone: createFlour). */
static void use_station(int uid, cJSON *obj, const char *toolKind)
{
    if (!toolKind && events_object_event(uid, TR_USE, -1)) return;
    char line[300];
    for (int i = 0; i < jobCount; i++) if (jobs[i].uid == uid) { snprintf(line, sizeof line, "\"%s\" is busy.", str(obj, "Name")); say(line); return; }
    if (work_station_in_use(uid)) { snprintf(line, sizeof line, "\"%s\" is busy.", str(obj, "Name")); say(line); return; } /* someone is working at it */
    cJSON *r, *first = NULL, *recipe = NULL;
    cJSON_ArrayForEach(r, get(get(obj, "Crafting"), "Recipes"))
    {
        if (!same_kind(str(r, "ToolKind"), toolKind ? toolKind : "") || num(r, "OutputObjectId", -1) < 0) continue;
        if (!first) first = r;
        if (!recipe && has_ingredients(r)) recipe = r;
    }
    if (!recipe)
    {
        const char *hint = str(get(obj, "Crafting"), "Hint");
        if (hint[0]) party_say(hint);
        if (!first) snprintf(line, sizeof line, "Nothing to make on \"%s\" that way.", str(obj, "Name"));
        else
        {
            int at = snprintf(line, sizeof line, "You don't have what it takes: ");
            cJSON *in;
            int any = 0;
            cJSON_ArrayForEach(in, get(first, "Inputs"))
            {
                if (num(in, "ObjectId", -1) < 0 || at >= (int)sizeof line - 40) continue;
                int qn = num(in, "Quantity", 1);
                at += snprintf(line + at, sizeof line - (size_t)at, qn > 1 ? "%s%d %s" : "%s%.0d%s", any ? ", " : "", qn > 1 ? qn : 0, str(object_def(num(in, "ObjectId", -1)), "Name"));
                any = 1;
            }
            snprintf(line + at, sizeof line - (size_t)at, ".");
        }
        say(line);
        return;
    }
    cJSON *in;
    cJSON_ArrayForEach(in, get(recipe, "Inputs")) if (num(in, "ObjectId", -1) >= 0) party_take(num(in, "ObjectId", -1), num(in, "Quantity", 1) > 1 ? num(in, "Quantity", 1) : 1);
    if (jobCount >= 16) return;
    world_thing_set_lit(uid, 1); /* working: its working sprite */
    sound_play(num(recipe, "WorkSoundResourceId", -1));
    jobs[jobCount++] = (Job){ uid, obj, recipe, dbl(recipe, "WorkSeconds", 2.4) * 1000, 800 };
    at_work(400);
    snprintf(line, sizeof line, "You set to work on \"%s\"...", str(obj, "Name"));
    say(line);
}

/* ---------------- Sleeping: beds and bedrolls (GameTestWindow.Travel / Beds) ---------------- */


static int bedroll_hours(cJSON *obj)
{
    cJSON *b = get(obj, "Bedroll");
    int hours = num(b, "SleepHours", 8);
    if (flag(b, "WakeAtDawn")) { int until = (6 - events_hour() + 24) % 24; hours = until > 0 ? until : 24; }
    return hours < 1 ? 1 : hours > 24 ? 24 : hours;
}

/* advanceTimeDueToRest for the party, then the clock moves on. */
static void rest(int hours)
{
    for (int a = 0; a < world_actor_count(); a++)
    {
        if (!alive(a) || !in_party(a)) continue;
        int id = world_actor_character(a);
        if (vitals_food(id) < 10 && vitals_can_get_hungry(id)) continue; /* "The party member gets nothing from rest if he is starving" */
        for (int bit = 1; bit <= 64; bit <<= 1) vitals_cure(id, bit);
        int hp, max = vitals_max_hp(id);
        if (vitals_hp(id, &hp) && max > 0) { int back = hp + (int)ceil(max * hours / 8.0); vitals_set_hp(id, back > max ? max : back); }
        int magic = stat_of(a, K_MAGIC, 0);
        if (magic > 0) { int mana = stat_of(a, K_MANA, 0) + (int)ceil(magic * hours / 8.0); magic_set_mana(a, mana > magic ? magic : mana); }
        vitals_change_food(id, "Subtract", hours); /* set_npc_prop(FOODLEVEL, -sleep_time) */
    }
    events_advance_minutes(hours * 60);
    grow_plants(hours * 60.0);
    lastMinute = events_total_minutes();
}

static void sleep_hours(int hours, int bedroll) { sleep = (Sleep){ 1, hours, bedroll, 0 }; }

static void cancel_sleep(void)
{
    if (slider.cancel[0]) world_bark(0, slider.cancel, 2500);
    if (world_seated(0)) world_stand_up(0);
    if (slider.bed && world_thing_type(slider.bed) == OT_BEDROLL) world_thing_set_open(slider.bed, 0);
    world_set_pose(0, "Idle", -1);
    say("You decide not to sleep.");
}

/* AskHowLongToSleep: the bed's own range, from 0 ("don't sleep"). */
static void ask_sleep(int bed, cJSON *obj)
{
    cJSON *f = get(obj, "Furniture");
    if (cJSON_IsFalse(get(f, "AsksSleepHours")))
    {
        if (num(obj, "Type", -1) == OT_BEDROLL && !combat_foes_in_sight()) sleep_hours(bedroll_hours(obj), bed); /* its own hours, or until dawn */
        return;
    }
    memset(&slider, 0, sizeof slider);
    slider.open = 1; slider.bed = bed;
    slider.max = num(f, "SleepMaxHours", 12); if (slider.max < 1) slider.max = 1; if (slider.max > 99) slider.max = 99;
    slider.minHours = num(f, "SleepMinHours", 1); if (slider.minHours < 1) slider.minHours = 1; if (slider.minHours > slider.max) slider.minHours = slider.max;
    slider.step = num(f, "SleepStepHours", 1) > 1 ? num(f, "SleepStepHours", 1) : 1;
    slider.value = num(f, "SleepDefaultHours", 8); if (slider.value < slider.minHours) slider.value = slider.minHours; if (slider.value > slider.max) slider.value = slider.max;
    snprintf(slider.question, sizeof slider.question, "%s", get(f, "SleepQuestion") ? str(f, "SleepQuestion") : "How many hours wilt thou sleep?");
    snprintf(slider.cancel, sizeof slider.cancel, "%s", get(f, "CancelSleepLine") ? str(f, "CancelSleepLine") : "Perhaps later.");
}

static void slider_done(int ok)
{
    slider.open = 0;
    if (!ok || slider.value <= 0) { cancel_sleep(); return; }
    if (combat_foes_in_sight()) { world_bark(0, "We cannot rest with foes about!", 2500); say("You can't sleep with enemies in sight."); return; }
    sleep_hours(slider.value > slider.minHours ? slider.value : slider.minHours, world_thing_type(slider.bed) == OT_BEDROLL ? slider.bed : 0); /* wakes still lying - move to get up */
}

void mechanisms_lay_down(int uid)
{
    cJSON *obj = object_def(world_thing_object_id(uid));
    if (obj) ask_sleep(uid, obj);
}

int mechanisms_pausing(void) { return slider.open || sleep.phase; }

int mechanisms_key(int key)
{
    if (sleep.phase) return 1;
    if (!slider.open) return 0;
    if (key == KEY_LEFT || key == KEY_DOWN) { slider.value -= slider.step; if (slider.value < 0) slider.value = 0; }
    else if (key == KEY_RIGHT || key == KEY_UP) { slider.value += slider.step; if (slider.value > slider.max) slider.value = slider.max; }
    else if (key == KEY_ENTER || key == KEY_SPACE) slider_done(1);
    else if (key == KEY_ESCAPE) slider_done(0);
    return 1;
}

/* The slider's box: the question, "<  8 hours  >", OK and Cancel. */
static void slider_box(int *x, int *y, int *w, int *h)
{
    int need = font_text_width(font_get("InGameFont"), slider.question) + 16; /* as wide as its question, within the screen */
    *w = need < 220 ? 220 : need > SCREEN_W - 8 ? SCREEN_W - 8 : need; *h = 62; *x = (SCREEN_W - *w) / 2; *y = (SCREEN_H - *h) / 2;
}

static int slider_click(int mx, int my)
{
    if (!slider.open) return 0;
    int x, y, w, h;
    slider_box(&x, &y, &w, &h);
    if (my >= y + 22 && my < y + 36)
    {
        if (mx >= x + 8 && mx < x + 30) mechanisms_key(KEY_LEFT);
        else if (mx >= x + w - 30 && mx < x + w - 8) mechanisms_key(KEY_RIGHT);
        else if (mx >= x + 30 && mx < x + w - 30) { slider.value = (int)((mx - x - 30) * (double)slider.max / (w - 60) + 0.5); slider.value -= slider.value % slider.step; }
    }
    else if (my >= y + 42 && my < y + 56)
    {
        if (mx >= x + w / 2 - 70 && mx < x + w / 2 - 10) slider_done(1);
        else if (mx >= x + w / 2 + 10 && mx < x + w / 2 + 70) slider_done(0);
    }
    return 1;
}

static void fill_rect(uint32_t *p, int x, int y, int w, int h, uint32_t c)
{
    for (int j = y < 0 ? 0 : y; j < y + h && j < SCREEN_H; j++) for (int i = x < 0 ? 0 : x; i < x + w && i < SCREEN_W; i++) p[j * SCREEN_W + i] = c;
}

static void draw_slider(uint32_t *p)
{
    int x, y, w, h;
    slider_box(&x, &y, &w, &h);
    const Font *f = font_get("InGameFont");
    fill_rect(p, x, y, w, h, 0xFF2E1808u); fill_rect(p, x + 2, y + 2, w - 4, h - 4, 0xFFF1E4C2u);
    const uint32_t ink = 0xFF3B2410u;
    font_draw(f, p, x + (w - font_text_width(f, slider.question)) / 2, y + 6, slider.question, ink);
    fill_rect(p, x + 30, y + 28, w - 60, 2, ink);
    int knob = x + 30 + (slider.max > 0 ? (w - 60) * slider.value / slider.max : 0);
    fill_rect(p, knob - 2, y + 23, 5, 12, 0xFF8B0000u);
    font_draw(f, p, x + 14, y + 23, "<", ink);
    font_draw(f, p, x + w - 20, y + 23, ">", ink);
    char value[32];
    snprintf(value, sizeof value, slider.value == 1 ? "%d hour" : "%d hours", slider.value);
    font_draw(f, p, knob - font_text_width(f, value) / 2, y + 36, value, ink);
    fill_rect(p, x + w / 2 - 70, y + 46, 60, 1, ink); fill_rect(p, x + w / 2 + 10, y + 46, 60, 1, ink);
    font_draw(f, p, x + w / 2 - 40 - font_text_width(f, "OK") / 2, y + 48, "OK", ink);
    font_draw(f, p, x + w / 2 + 40 - font_text_width(f, "Cancel") / 2, y + 48, "Cancel", ink);
}

static void use_bedroll(const Used *u)
{
    if (fire_use(u)) return;
    if (vitals_incapacitated(world_actor_character(0))) return;
    if (combat_foes_in_sight()) { world_bark(0, "We cannot rest with foes about!", 2500); say("You can't sleep with enemies in sight."); return; }
    char line[200];
    if (u->uid <= 0)
    {
        /* in a pack it's only a bundle: put it down first, then sleep on it as on a bed */
        world_bark(0, "I should put it down first.", 2500);
        snprintf(line, sizeof line, "Put \"%s\" down on the ground, then double-click it to sleep on it.", str(u->def, "Name"));
        say(line);
        return;
    }
    world_thing_set_open(u->uid, 1); /* laid out */
    if (world_player_sit(u->uid)) return; /* its sleeping place: walk there and lie down, as on a bed (then the hours) */
    world_set_pose(0, "Sleeping", -1); /* no sleeping place: lies where it stands */
    ask_sleep(u->uid, u->def);
    if (!slider.open && !sleep.phase) { world_thing_set_open(u->uid, 0); world_set_pose(0, "Idle", -1); }
}

/* ---------------- Liquid containers (bucket.uc, milking.uc; pouring is the user's rule) ---------------- */

static cJSON *liquid_food(int foodId) { cJSON *o = foodId >= 0 ? object_def(foodId) : NULL; return num(o, "Type", -1) == OT_FOOD ? o : NULL; }
static int can_hold(cJSON *container, int foodId) { cJSON *l; cJSON_ArrayForEach(l, get(get(container, "Liquid"), "Liquids")) if (num(l, "FoodId", -2) == foodId) return 1; return 0; }
static int liquid_of(const Used *u) { return u->uid > 0 ? world_thing_liquid(u->uid) : items_liquid(u->item, num(get(u->def, "Liquid"), "StartsFullOfId", -1)); }
static void set_liquid(const Used *u, int liquid) { if (u->uid > 0) world_thing_set_liquid(u->uid, liquid); else items_set_liquid(u->item, liquid); }

static void lower(char *s) { for (; *s; s++) if (*s >= 'A' && *s <= 'Z') *s = (char)(*s + 32); }

static void avatar_say(const char *fmt, const char *name) { char line[160]; snprintf(line, sizeof line, fmt, name); world_bark(0, line, 2500); }

/* The other thing as a used thing (a liquid on its own, another container). */
static int other_used(const GumpsPick *pick, Used *o)
{
    memset(o, 0, sizeof *o);
    o->holder = -1;
    if (pick->uid > 0 && world_thing_object_id(pick->uid) >= 0) { o->uid = pick->uid; o->def = object_def(world_thing_object_id(pick->uid)); }
    else if (pick->item && items_category(pick->item) == CAT_OBJECT) { o->item = pick->item; o->def = object_def(items_item_id(pick->item)); o->holder = items_carrier_of(pick->item); }
    return o->def != NULL;
}

/* PourBetween: into an empty container from a full one (or a liquid on its own, a bottle of water). 0: the other isn't a liquid. */
static int pour_between(const Used *container, const Used *other)
{
    char name[96], line[240];
    snprintf(name, sizeof name, "%s", str(container->def, "Name")); lower(name);
    if (num(other->def, "Type", -1) == OT_FOOD && flag(get(other->def, "Food"), "IsLiquid"))
    {
        if (liquid_of(container) >= 0) { avatar_say("The %s is full.", name); return 1; }
        if (!can_hold(container->def, num(other->def, "Id", -1))) { snprintf(line, sizeof line, "\"%s\" can't hold that.", str(container->def, "Name")); say(line); return 1; }
        sound_play(num(get(container->def, "Liquid"), "FillSoundResourceId", -1));
        set_liquid(container, num(other->def, "Id", -1));
        use_up_one(other);
        snprintf(line, sizeof line, "You pour \"%s\" into \"%s\".", str(other->def, "Name"), str(container->def, "Name"));
        say(line);
        return 1;
    }
    if (num(other->def, "Type", -1) != OT_LIQUID) return 0;
    int full = liquid_of(container) >= 0, otherFull = liquid_of(other) >= 0;
    if (!full && !otherFull) { avatar_say("The %s is empty.", name); return 1; }
    if (full && otherFull) { char on[96]; snprintf(on, sizeof on, "%s", str(other->def, "Name")); lower(on); avatar_say("The %s is full.", on); return 1; }
    const Used *from = full ? container : other, *into = full ? other : container;
    int liquid = liquid_of(from);
    if (!can_hold(into->def, liquid)) { snprintf(line, sizeof line, "\"%s\" can't hold that.", str(into->def, "Name")); say(line); return 1; }
    sound_play(num(get(from->def, "Liquid"), "PourSoundResourceId", -1));
    sound_play(num(get(into->def, "Liquid"), "FillSoundResourceId", -1));
    set_liquid(into, liquid);
    if (flag(get(from->def, "Liquid"), "UsedUpWhenPouredOut")) use_up_one(from); else set_liquid(from, -1);
    snprintf(line, sizeof line, "You pour \"%s\" into \"%s\".", str(from->def, "Name"), str(into->def, "Name"));
    say(line);
    return 1;
}


static void spill_at(const Used *u, int x, int y, int fl)
{
    int spill = num(get(u->def, "Liquid"), "SpillObjectId", -1);
    if (object_def(spill)) world_thing_put(CAT_OBJECT, spill, 1, x, y, fl);
}

static void liquid_target(void *context, const GumpsPick *pick)
{
    Used *u = (Used *)context;
    if (!still_there(u)) return;
    char name[96], line[240];
    snprintf(name, sizeof name, "%s", str(u->def, "Name")); lower(name);
    Used other;
    if (pick->actor < 0 && other_used(pick, &other) && !(other.uid > 0 && other.uid == u->uid) && !(other.item && other.item == u->item) && pour_between(u, &other)) return;
    int liquid = liquid_of(u), empty = !liquid_food(liquid);
    cJSON *food = get(liquid_food(liquid), "Food"), *settings = get(u->def, "Liquid");
    /* yourself: drunk ("Ahhh, how refreshing.") */
    if (pick->actor == 0)
    {
        if (empty) { avatar_say("The %s is empty.", name); return; }
        if (flag(food, "Undrinkable")) { world_bark(0, "No, thank thee.", 2500); return; }
        world_bark(0, "Ahhh, how refreshing.", 2500);
        set_liquid(u, -1);
        if (num(food, "Nutrition", 0) > 0) vitals_change_food(world_actor_character(0), "Add", num(food, "Nutrition", 0));
        magic_apply_effects(get(food, "Effects"), 0, str(liquid_food(liquid), "Name"), -1);
        return;
    }
    if (pick->actor > 0)
    {
        cJSON *npc = character_json(world_actor_character(pick->actor));
        int gives = num(npc, "GivesLiquidFoodId", -1);
        if (gives >= 0 && liquid_food(gives) && alive(pick->actor))
        {
            /* milked into an empty container (milking.uc) */
            if (!empty) { avatar_say("The %s is full.", name); return; }
            if (!can_hold(u->def, gives)) { snprintf(line, sizeof line, "\"%s\" can't hold that.", str(u->def, "Name")); say(line); return; }
            if (rnd(10) < 3) world_bark(0, "Hold still...", 2000);
            world_walk_cancel(pick->actor);
            milking = (Milking){ 1, 0, pick->actor, gives, 800, *u };
            sound_play(num(npc, "GivesLiquidSoundResourceId", -1));
            at_work(400);
            snprintf(line, sizeof line, "You milk %s...", world_actor_name(pick->actor));
            say(line);
            return;
        }
        /* anyone else: doused and woken (useBucketOnNPC) */
        if (empty) { avatar_say("The %s is empty.", name); return; }
        vitals_cure(world_actor_character(pick->actor), 2);
        if (human(pick->actor)) world_bark(pick->actor, "Hey, stop that!", 2500);
        sound_play(num(settings, "PourSoundResourceId", -1));
        set_liquid(u, -1);
        int x, y, fl;
        world_actor_cell(pick->actor, &x, &y, &fl);
        spill_at(u, x, y, fl);
        return;
    }
    if (pick->uid > 0 && pick->uid != u->uid && world_thing_object_id(pick->uid) >= 0)
    {
        cJSON *obj = object_def(world_thing_object_id(pick->uid));
        cJSON *source = liquid_food(num(obj, "LiquidSourceId", -1));
        if (source)
        {
            /* a source (a well, a trough): filled (useBucketOnWell) */
            if (!empty) { avatar_say("The %s is full.", name); return; }
            if (!can_hold(u->def, num(source, "Id", -1))) { snprintf(line, sizeof line, "\"%s\" can't hold that.", str(u->def, "Name")); say(line); return; }
            events_object_event(pick->uid, TR_TRADE, -1); /* the well's own script first */
            sound_play(num(settings, "FillSoundResourceId", -1));
            set_liquid(u, num(source, "Id", -1));
            char sn[96]; snprintf(sn, sizeof sn, "%s", str(source, "Name")); lower(sn);
            snprintf(line, sizeof line, "You fill \"%s\" with %s.", str(u->def, "Name"), sn);
            say(line);
            return;
        }
        if (num(obj, "Type", -1) == OT_LIGHT && world_light_lit(pick->uid))
        {
            /* useBucketOnDousable */
            if (empty) { avatar_say("The %s is empty.", name); return; }
            if (!flag(food, "PutsOutFires")) return;
            world_light_set(pick->uid, 0);
            sound_play(num(get(obj, "Light"), "OffSoundResourceId", -1));
            sound_play(num(settings, "PourSoundResourceId", -1));
            set_liquid(u, -1);
            snprintf(line, sizeof line, "You douse \"%s\".", str(obj, "Name"));
            say(line);
            return;
        }
        if (num(obj, "Type", -1) == OT_EXPLOSIVE && !empty && flag(food, "PutsOutFires") && combat_douse_fuse(pick->uid))
        {
            set_liquid(u, -1);
            snprintf(line, sizeof line, "You douse the fuse of \"%s\".", str(obj, "Name"));
            say(line);
            return;
        }
    }
    /* anything else - the floor: spilt (useBucketOnGround) */
    if (empty) { avatar_say("The %s is empty.", name); return; }
    sound_play(num(settings, "PourSoundResourceId", -1));
    set_liquid(u, -1);
    spill_at(u, pick->cellX, pick->cellY, pick->floor);
    snprintf(line, sizeof line, "You empty \"%s\".", str(u->def, "Name"));
    say(line);
}

static void use_liquid(const Used *u)
{
    if (fire_use(u)) return;
    char prompt[160];
    snprintf(prompt, sizeof prompt, "Use \"%s\" on what?", str(u->def, "Name"));
    gumps_choose_target(prompt, liquid_target, &using);
}

/* ---------------- Each frame: growth, work, milking, sleep ---------------- */

static void mech_work_update(double ms)
{
    long minute = events_total_minutes();
    if (lastMinute < 0) lastMinute = minute;
    if (minute > lastMinute) { grow_plants((double)(minute - lastMinute)); lastMinute = minute; }
    char line[240];
    for (int i = 0; i < jobCount; i++)
    {
        Job *j = &jobs[i];
        j->left -= ms;
        if ((j->nextBeat -= ms) <= 0 && j->left > 0) { j->nextBeat = 800; sound_play(num(j->recipe, "WorkSoundResourceId", -1)); at_work(400); } /* the millstone's turns, each with its sound */
        if (j->left > 0) continue;
        Job done = *j;
        jobs[i--] = jobs[--jobCount];
        if (!world_thing_there(done.uid)) continue;
        world_thing_set_lit(done.uid, 0);
        int c, id, q, x, y, n, many = num(done.recipe, "OutputQuantity", 1);
        world_thing_info(done.uid, &c, &id, &q, &x, &y, &n);
        world_thing_put(CAT_OBJECT, num(done.recipe, "OutputObjectId", -1), many > 1 ? many : 1, x, y, world_thing_floor(done.uid)); /* on the station (createFlour) */
        char count[16] = "";
        if (many > 1) snprintf(count, sizeof count, "%d ", many);
        snprintf(line, sizeof line, "\"%s\" has made %s\"%s\".", str(done.obj, "Name"), count, str(object_def(num(done.recipe, "OutputObjectId", -1)), "Name"));
        say(line);
        events_object_event(done.uid, TR_AFTER_READ, -1); /* By Made Something */
    }
    if (milking.on && (milking.ms -= ms) <= 0)
    {
        milking.ms = 800;
        cJSON *npc = character_json(world_actor_character(milking.animal));
        world_walk_cancel(milking.animal); /* it stands still to be milked */
        if (++milking.beats <= 3) { sound_play(num(npc, "GivesLiquidSoundResourceId", -1)); at_work(400); }
        else
        {
            milking.on = 0;
            if (still_there(&milking.u) && liquid_of(&milking.u) < 0)
            {
                set_liquid(&milking.u, milking.liquid);
                world_bark(0, "That's a good girl!", 2500);
                if (str(npc, "GivesLiquidBark")[0] && alive(milking.animal)) world_bark(milking.animal, str(npc, "GivesLiquidBark"), 2500);
                if (rnd(3) == 0)
                {
                    int others[64], n = 0;
                    for (int a = 1; a < world_actor_count() && n < 64; a++) if (alive(a) && in_party(a) && human(a) && !vitals_incapacitated(world_actor_character(a))) others[n++] = a;
                    if (n) world_bark(others[rnd(n)], "Don't we have a quest?", 3000);
                }
                char ln[96], lq[96];
                snprintf(lq, sizeof lq, "%s", str(liquid_food(milking.liquid), "Name")); lower(lq);
                snprintf(ln, sizeof ln, "\"%s\" is full of %s.", str(milking.u.def, "Name"), lq);
                say(ln);
            }
        }
    }
}

/* The party sleeps: the screen falls dark (1.2 s), the hours pass at once, it rises again. Runs while the world waits. */
static void sleep_update(double ms)
{
    if (!sleep.phase) return;
    sleep.ms += ms;
    if (sleep.ms < 1200) return;
    sleep.ms = 0;
    if (sleep.phase == 1) { rest(sleep.hours); sleep.phase = 2; return; }
    sleep.phase = 0;
    if (sleep.bedroll && world_thing_there(sleep.bedroll) && !world_seated(0)) { world_thing_set_open(sleep.bedroll, 0); world_set_pose(0, "Idle", -1); }
    char line[96];
    snprintf(line, sizeof line, "You wake, rested (%d hour%s have passed).", sleep.hours, sleep.hours == 1 ? "" : "s");
    say(line);
}

void mechanisms_frame(double ms) { sleep_update(ms); }

static void draw_sleep(uint32_t *p)
{
    if (!sleep.phase) return;
    double t = sleep.ms / 1200.0;
    int a = (int)((sleep.phase == 1 ? t : 1 - t) * 255);
    if (a <= 0) return;
    for (int i = 0; i < SCREEN_W * SCREEN_H; i++)
    {
        uint32_t c = p[i];
        uint32_t r = ((c >> 16) & 255) * (255 - (uint32_t)a) / 255, g = ((c >> 8) & 255) * (255 - (uint32_t)a) / 255, b = (c & 255) * (255 - (uint32_t)a) / 255;
        p[i] = 0xFF000000u | (r << 16) | (g << 8) | b;
    }
}

/* ---------------- Using them ---------------- */

static void use_tool(const Used *u);
static void use_bedroll(const Used *u);
static void use_liquid(const Used *u);

static int use(Used u)
{
    using = u;
    char prompt[160];
    switch (num(u.def, "Type", -1))
    {
        case OT_KEY: case OT_LOCKPICK:
            if (fire_use(&u)) return 1;
            snprintf(prompt, sizeof prompt, num(u.def, "Type", -1) == OT_KEY ? "Use \"%s\" on what?" : "Pick which lock with \"%s\"?", str(u.def, "Name"));
            gumps_choose_target(prompt, lock_tool_target, &using);
            return 1;
        case OT_KEYCHAIN:
            if (fire_use(&u)) return 1;
            snprintf(prompt, sizeof prompt, "Use \"%s\" on what?", str(u.def, "Name"));
            gumps_choose_target(prompt, keychain_target, &using);
            return 1;
        case OT_SEXTANT: use_sextant(&u); return 1;
        case OT_MAP: use_map(&u); return 1;
        case OT_CLOCK: use_clock(&u); return 1;
        case OT_INSTRUMENT: use_instrument(&u); return 1;
        case OT_TOOL: use_tool(&using); return 1;
        case OT_BEDROLL: use_bedroll(&using); return 1;
        case OT_LIQUID: use_liquid(&using); return 1;
    }
    return 0;
}

int mechanisms_use_map(int uid)
{
    cJSON *def = object_def(world_thing_object_id(uid));
    if (!def) return 0;
    switch (num(def, "Type", -1))
    {
        case OT_WINDOW: use_window(uid, def); return 1;
        case OT_LEVER: pull_lever(uid, def); return 1;
        case OT_STAIRS: use_stairs(uid, def); return 1;
        case OT_PLANT: use_plant(uid, def, NULL); return 1;
        case OT_STATION: use_station(uid, def, NULL); return 1;
    }
    return use((Used){ def, uid, NULL, -1 });
}

int mechanisms_use_carried(Item *it)
{
    if (items_category(it) != CAT_OBJECT) return 0;
    cJSON *def = object_def(items_item_id(it));
    return def ? use((Used){ def, 0, it, items_carrier_of(it) }) : 0;
}

void mechanisms_update(double ms)
{
    for (int i = 0; i < buttonCount; i++)
    {
        if ((buttons[i].ms -= ms) > 0) continue;
        int uid = buttons[i].uid;
        buttons[i--] = buttons[--buttonCount];
        if (!world_thing_there(uid)) continue;
        world_thing_set_open(uid, 0); /* springs back */
        cJSON *obj = object_def(world_thing_object_id(uid));
        sound_play(num(get(obj, "Lever"), "OffSoundResourceId", -1));
        events_object_event(uid, TR_ENTER, -1);
    }
    if (poseBackMs > 0 && (poseBackMs -= ms) <= 0) world_set_pose(0, "Idle", -1);
    mech_work_update(ms);
}

void mechanisms_reset(void) { buttonCount = 0; poseBackMs = 0; view.open = 0; jobCount = 0; milking.on = 0; slider.open = 0; sleep.phase = 0; lastMinute = -1; }

int mechanisms_station_busy(int uid) { for (int i = 0; i < jobCount; i++) if (jobs[i].uid == uid) return 1; return 0; }
