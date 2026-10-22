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
#include "world.h"
#include "items.h"
#include "events.h"
#include "vitals.h"
#include "combat.h"
#include "gumps.h"
#include "sound.h"
#include "conditions.h"
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
    OT_LEVER = 22, OT_STAIRS = 23, OT_CLOCK = 25, OT_INSTRUMENT = 28, OT_EXPLOSIVE = 29
};
enum { TR_USE = 2, TR_AFTER_READ = 5, TR_ENTER = 9, TR_STAY = 10, TR_EXIT = 11 };
enum { K_DEXTERITY = 2 };
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

void mechanisms_draw(uint32_t *pixels)
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

/* ---------------- Using them ---------------- */

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
}

void mechanisms_reset(void) { buttonCount = 0; poseBackMs = 0; view.open = 0; }
