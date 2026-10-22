/*
 * Double-clicking in the world, as the Windows engine does it
 * (GameTestWindow HandleObjectClick, UseDoor, UseLight, TalkTo):
 *  - a character: talking (its Talk commands);
 *  - a thing: its own event pages first (By Use), then what its type does -
 *    a door opens or closes (locked: "Locked"), a light is lit or put out,
 *    a chair is sat on, a bed lain on (a bed's cover means its bed);
 *    anything else: nothing happens.
 */
#include "use.h"
#include "mechanisms.h"
#include "magic.h"
#include "events.h"
#include "combat.h"
#include "gumps.h"
#include "dolls.h"
#include "items.h"
#include "world.h"
#include "sound.h"
#include "../third_party/cJSON.h"
#include <string.h>
#include <stdio.h>

enum { OT_DOOR = 3, OT_LIGHT = 4, OT_CHAIR = 7, OT_BED = 8 };
enum { TR_USE = 2, TR_AFTER_READ = 5, TR_ENTER = 9 };

static cJSON *objectDef(int objectId)
{
    cJSON *list = cJSON_GetObjectItem(cJSON_GetObjectItem(cJSON_GetObjectItem((cJSON *)world_json(), "Project"), "Objects"), "Objects"), *o;
    cJSON_ArrayForEach(o, list) { cJSON *id = cJSON_GetObjectItem(o, "Id"); if (cJSON_IsNumber(id) && id->valueint == objectId) return o; }
    return NULL;
}

static int sound_of(cJSON *section, const char *key) { cJSON *v = cJSON_GetObjectItem(section, key); return cJSON_IsNumber(v) ? v->valueint : -1; }

/* Whether any of its pages has commands (a cover with events of its own isn't just its bed). */
static int has_commands(cJSON *def)
{
    cJSON *p;
    cJSON_ArrayForEach(p, cJSON_GetObjectItem(def, "EventPages")) if (cJSON_GetArraySize(cJSON_GetObjectItem(p, "Commands")) > 0) return 1;
    return 0;
}

static void use_door(int uid, cJSON *def)
{
    if (events_object_event(uid, TR_USE, -1)) return;
    int open, locked;
    world_door_state(uid, &open, &locked);
    cJSON *door = cJSON_GetObjectItem(def, "Door");
    if (!open && locked)
    {
        sound_play(sound_of(door, "LockedRattleSoundResourceId"));
        world_bark_thing(uid, "Locked", 1000);
        return;
    }
    if (open)
    {
        if (!world_door_close(uid)) return; /* someone in the doorway */
        sound_play(sound_of(door, "CloseSoundResourceId"));
        events_object_event(uid, TR_ENTER, -1);
        return;
    }
    world_door_open(uid);
    sound_play(sound_of(door, "OpenSoundResourceId"));
    events_object_event(uid, TR_AFTER_READ, -1);
}

static void use_light(int uid, cJSON *def)
{
    if (events_object_event(uid, TR_USE, -1)) return;
    if (world_light_spent(uid)) return; /* burnt out: it can't be lit */
    int lit = !world_light_lit(uid);
    world_light_set(uid, lit);
    if (lit) world_light_lit_now(uid);
    cJSON *light = cJSON_GetObjectItem(def, "Light");
    sound_play(sound_of(light, lit ? "LitSoundResourceId" : "OffSoundResourceId"));
    events_object_event(uid, lit ? TR_AFTER_READ : TR_ENTER, -1);
}

void use_at(int sx, int sy)
{
    int actor, uid;
    world_hit(sx, sy, &actor, &uid);
    if (actor > 0) { use_actor(actor); return; }
    if (uid) use_thing(uid);
}

/* Exult: in combat mode, double-clicking someone attacks them (never a party member); otherwise it talks to them. */
void use_actor(int actor)
{
    if (combat_mode() && !items_in_party(world_actor_character(actor))) combat_player_target(actor);
    else events_talk(actor);
}

void use_thing(int uid)
{
    if (combat_player_thing(uid)) return; /* combat mode: attacked (bodies and unlocked containers open as normal) */
    int objectId = world_thing_object_id(uid);
    if (objectId < 0)
    {
        int category, itemId, quantity, x, y, instance;
        world_thing_info(uid, &category, &itemId, &quantity, &x, &y, &instance);
        if (category == 5) { gumps_open_map_container(uid); return; } /* its window */
        if (!events_fire_item(category, itemId, TR_USE)) events_log_once(311, "Reading books isn't in the player yet.");
        return;
    }
    cJSON *def = objectDef(objectId);
    if (!def) return;
    cJSON *dollId = cJSON_GetObjectItem(def, "DollDesignId");
    if (cJSON_IsNumber(dollId) && dollId->valueint >= 0)
    {
        int category, itemId, quantity, x, y, instance;
        world_thing_info(uid, &category, &itemId, &quantity, &x, &y, &instance);
        if (dolls_use_item(items_parked(uid, category, itemId, quantity), uid)) return; /* its paperdoll (its own By Use first) */
    }
    /* a bed's cover lies over the bed: double-clicking it means the bed */
    int bed = world_bed_under_cover(uid);
    if (bed && !has_commands(def)) { world_player_sit(bed); return; }
    if (mechanisms_use_map(uid)) return; /* a window, lever, ladder, key, lockpick, keychain, sextant, map, clock, instrument */
    if (magic_use_map(uid)) return; /* a potion, food, a useable object, a spellbook */
    switch (world_thing_type(uid))
    {
        case OT_DOOR: use_door(uid, def); return;
        case 29: if (!events_object_event(uid, TR_USE, -1)) combat_light_fuse(uid); return; /* an explosive: its fuse */
        case OT_LIGHT: use_light(uid, def); return;
        case OT_CHAIR: case OT_BED: if (!events_object_event(uid, TR_USE, -1)) world_player_sit(uid); return;
    }
    events_object_event(uid, TR_USE, -1); /* no usecode: nothing happens */
}
