#include "items.h"
#include "books.h"
#include "events.h"
#include "world.h"
#include "sound.h"
#include "conditions.h"
#include "dolls.h"
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>

#define UNPLACED -100000.0
#define MAX_QUANTITY 100 /* ObjectTypes.MaxQuantity: a stack holds up to 100 */

/* EquipmentSlot, in the editor's order. */
enum { S_HEAD, S_NECK, S_BODY, S_ARMS, S_GLOVES, S_BELT, S_BACK, S_LEGS, S_FEET, S_RIGHT, S_LEFT, S_RING1, S_RING2, S_BACKPACK, S_POUCH, SLOT_COUNT };
static const char *SLOT_NAMES[SLOT_COUNT] = { "Head", "Neck", "Body", "Arms", "Gloves", "Belt", "Back", "Legs", "Feet", "RightHand", "LeftHand", "Ring1", "Ring2", "Backpack", "Pouch" };

/* ObjectType numbers used here. */
enum { OT_AMMO = 5, OT_MONEY = 6, OT_LOCKPICK = 11, OT_WEAPON = 35, OT_ARMOR = 36 };
/* Trigger numbers (BookTriggerConditionBased). */
enum { TR_DROP = 3, TR_PICKUP = 6, TR_HAVING = 7, TR_NOT_HAVING = 8, TR_EQUIP = 12, TR_UNEQUIP = 13 };

struct Item
{
    int instanceId, category, itemId, quantity;
    int locked;              /* -1 as its kind says, else 0 / 1 (IsLockedRuntime) */
    int lit, spent; double burnLeft; /* a light: lit, burnt out, game minutes left (-1: not started) */
    int keys[32], keyCount, keysSet; /* a keyring: the keys on it (keysSet: its list is its own, not its Starting Keys) */
    int charges;                     /* a weapon with charges: how many left (-1: not used yet) */
    double cx, cy;                   /* its spot in a container's window (-100000: none yet) */
    int autoPlaced;                  /* laid out in the row by the engine, not dropped at a spot */
    void *doll;                      /* its paperdoll as it was left (PaperDoll Editor), NULL: never opened */
    char **learned; int learnedCount; /* a spellbook: the spells this copy has learned (LearnedSpellsRuntime) */
    int liquid, liquidSet;            /* a liquid container: what it holds (a liquid Food's id, -1 empty); liquidSet: its own, not as it starts */
    int facing;                       /* the way it faces when put down (FacingRuntime; 4 = south) */
    Item **contents; int count, cap;
};

/* A slot: a built-in one (0..14) or a character's custom slot (100 + its id). */
typedef struct Slot { int key, instanceId; } Slot;

typedef struct Carrier
{
    int characterId, inParty, isPlayer, strength;
    Item **items; int count, cap;            /* everything straight on them (in a slot) */
    Slot slots[64]; int slotCount;
    int disabled[SLOT_COUNT];
    int customIds[32]; char customNames[32][48]; int customCount;
    int nextInstanceId;
} Carrier;

static Carrier *carriers; static int carrierCount;
static cJSON *objectsJson, *containersJson, *booksJson;
static char refusal[200];

const char *items_refusal(void) { return refusal; }

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static double dbl(cJSON *o, const char *k) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valuedouble : 0; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }

static cJSON *def_of(int category, int itemId)
{
    cJSON *list = category == CAT_OBJECT ? objectsJson : category == CAT_CONTAINER ? containersJson : category == CAT_BOOK ? booksJson : NULL, *d;
    cJSON_ArrayForEach(d, list) if (num(d, "Id", -2) == itemId) return d;
    return NULL;
}

const char *items_name(int category, int itemId)
{
    if (category == CAT_BOOK && books_setting(itemId, "Name")) return books_setting(itemId, "Name");
    cJSON *d = def_of(category, itemId);
    return d ? str(d, "Name") : category == CAT_CONTAINER ? "container" : category == CAT_BOOK ? "book" : "object";
}

int items_category(const Item *item) { return item->category; }
int items_item_id(const Item *item) { return item->itemId; }
int items_quantity(const Item *item) { return item->quantity; }

/* ---------------- Items ---------------- */

static void add_to(Item ***list, int *count, int *cap, Item *item)
{
    if (*count == *cap) { *cap = *cap ? *cap * 2 : 8; *list = (Item **)realloc(*list, sizeof(Item *) * (size_t)*cap); }
    (*list)[(*count)++] = item;
}

static void remove_from(Item **list, int *count, const Item *item)
{
    for (int i = 0; i < *count; i++)
        if (list[i] == item) { memmove(&list[i], &list[i + 1], sizeof(Item *) * (size_t)(*count - i - 1)); (*count)--; return; }
}

static Item *new_item(int category, int itemId, int quantity)
{
    Item *it = (Item *)calloc(1, sizeof(Item));
    it->category = category; it->itemId = itemId; it->quantity = quantity > 0 ? quantity : 1; it->locked = -1; it->burnLeft = -1; it->charges = -1; it->cx = it->cy = UNPLACED; it->facing = 4;
    return it;
}

void items_free(Item *item)
{
    if (!item) return;
    for (int i = 0; i < item->count; i++) items_free(item->contents[i]);
    free(item->contents);
    if (item->doll) dolls_free_state(item->doll);
    for (int i = 0; i < item->learnedCount; i++) free(item->learned[i]);
    free(item->learned);
    free(item);
}

void items_learn_spell(Item *it, const char *name);

/* An inventory entry from the game (InventoryItemInstance), with what's inside. */
static Item *read_item(cJSON *j)
{
    Item *it = new_item(num(j, "Category", CAT_OBJECT), num(j, "ItemId", -1), num(j, "Quantity", 1));
    it->instanceId = num(j, "InstanceId", 0);
    if (cJSON_IsNumber(get(j, "ContainerX")) && cJSON_IsNumber(get(j, "ContainerY"))) { it->cx = get(j, "ContainerX")->valuedouble; it->cy = get(j, "ContainerY")->valuedouble; }
    cJSON *locked = get(j, "IsLockedRuntime"), *c;
    if (cJSON_IsBool(locked)) it->locked = cJSON_IsTrue(locked);
    it->lit = cJSON_IsTrue(get(j, "IsLitRuntime")); it->spent = cJSON_IsTrue(get(j, "IsSpentRuntime"));
    cJSON *key;
    cJSON_ArrayForEach(key, get(j, "KeyIdsRuntime")) if (it->keyCount < 32 && cJSON_IsNumber(key)) it->keys[it->keyCount++] = key->valueint;
    if (cJSON_IsArray(get(j, "KeyIdsRuntime"))) it->keysSet = 1;
    it->burnLeft = cJSON_IsNumber(get(j, "BurnLeftRuntime")) ? get(j, "BurnLeftRuntime")->valuedouble : -1;
    it->charges = num(j, "ChargesRuntime", -1);
    it->facing = num(j, "FacingRuntime", 4);
    if (cJSON_IsNumber(get(j, "LiquidIdRuntime"))) { it->liquid = num(j, "LiquidIdRuntime", -1); it->liquidSet = 1; }
    cJSON *spell;
    cJSON_ArrayForEach(spell, get(j, "LearnedSpellsRuntime")) if (cJSON_IsString(spell)) items_learn_spell(it, spell->valuestring);
    if (cJSON_IsObject(get(j, "DollRuntime"))) it->doll = dolls_state_from_json(get(j, "DollRuntime")); /* its paperdoll as it was left */
    cJSON_ArrayForEach(c, get(j, "Contents")) add_to(&it->contents, &it->count, &it->cap, read_item(c));
    return it;
}

/* A fresh one: a container comes with its Starting Contents (SeedStartingContents). */
Item *items_make(int category, int itemId, int quantity)
{
    Item *it = new_item(category, itemId, quantity);
    if (category == CAT_CONTAINER)
    {
        cJSON *seed;
        cJSON_ArrayForEach(seed, get(def_of(CAT_CONTAINER, itemId), "StartingContents")) add_to(&it->contents, &it->count, &it->cap, read_item(seed));
    }
    return it;
}

static int is_bundle(int type) { return type == OT_AMMO || type == OT_MONEY || type == OT_LOCKPICK; }

/* CanCombine: a bundle (ammo, money, lockpicks) or a Stackable object. */
static int can_combine(int category, int itemId)
{
    cJSON *d = category == CAT_OBJECT ? def_of(CAT_OBJECT, itemId) : NULL;
    return d && (is_bundle(num(d, "Type", 0)) || flag(d, "IsStackable"));
}

/* ItemWeights.WeightOf: its Weight times how many, plus what's inside (unless a bag of holding). */
static double weight_of(const Item *it)
{
    cJSON *d = def_of(it->category, it->itemId);
    double w = dbl(d, "Weight") * (it->quantity > 0 ? it->quantity : 1);
    if (it->category == CAT_CONTAINER && !flag(d, "ExtradimensionalStorage"))
        for (int i = 0; i < it->count; i++) w += weight_of(it->contents[i]);
    return w;
}

/* ResolveContentsVolume: its Volume times how many. */
static double volume_of(const Item *it) { return dbl(def_of(it->category, it->itemId), "Volume") * it->quantity; }

static int is_two_handed(const Item *it) { return it->category != CAT_CONTAINER && flag(def_of(it->category, it->itemId), "IsTwoHanded"); }

static int holds(Item **list, int count, const Item *target)
{
    for (int i = 0; i < count; i++) if (list[i] == target || holds(list[i]->contents, list[i]->count, target)) return 1;
    return 0;
}

/* ---------------- Characters ---------------- */

static Carrier *carrier_of(int characterId)
{
    for (int i = 0; i < carrierCount; i++) if (carriers[i].characterId == characterId) return &carriers[i];
    return NULL;
}

int items_in_party(int characterId) { Carrier *c = carrier_of(characterId); return c && (c->isPlayer || c->inParty); }

static int slot_key_of(const char *name) { for (int i = 0; i < SLOT_COUNT; i++) if (strcmp(SLOT_NAMES[i], name) == 0) return i; return -1; }

void items_load(void)
{
    cJSON *root = (cJSON *)world_json(), *project = get(root, "Project");
    objectsJson = get(get(project, "Objects"), "Objects");
    containersJson = get(get(project, "Containers"), "Containers");
    booksJson = get(get(root, "Books"), "Books");
    int strengthId = -1;
    cJSON *s, *c, *e;
    cJSON_ArrayForEach(s, get(get(project, "Stats"), "Stats")) if (num(s, "Kind", 0) == 1) strengthId = num(s, "Id", -1);
    cJSON *characters = get(get(project, "Characters"), "Characters");
    carriers = (Carrier *)calloc((size_t)cJSON_GetArraySize(characters) + 1, sizeof(Carrier));
    cJSON_ArrayForEach(c, characters)
    {
        Carrier *k = &carriers[carrierCount++];
        k->characterId = num(c, "Id", -1);
        k->inParty = flag(c, "IsInParty"); k->isPlayer = flag(c, "IsTestPlayer");
        char key[16];
        snprintf(key, sizeof key, "%d", strengthId);
        const char *st = str(get(c, "StatValues"), key);
        k->strength = st[0] ? atoi(st) : 10; /* Stat(owner, Strength, 10) */
        int top = 0;
        cJSON_ArrayForEach(e, get(c, "Inventory"))
        {
            Item *it = read_item(e);
            add_to(&k->items, &k->count, &k->cap, it);
            if (it->instanceId > top) top = it->instanceId;
        }
        k->nextInstanceId = top + 1;
        cJSON_ArrayForEach(e, get(c, "EquippedSlots"))
        {
            int slot = slot_key_of(e->string);
            if (slot >= 0 && k->slotCount < 64) k->slots[k->slotCount++] = (Slot){ slot, e->valueint };
        }
        cJSON_ArrayForEach(e, get(c, "CustomEquippedSlots"))
            if (k->slotCount < 64) k->slots[k->slotCount++] = (Slot){ 100 + atoi(e->string), e->valueint };
        cJSON *paperdoll = get(c, "Paperdoll");
        cJSON_ArrayForEach(e, get(paperdoll, "DisabledSlots")) if (e->valueint >= 0 && e->valueint < SLOT_COUNT) k->disabled[e->valueint] = 1;
        cJSON_ArrayForEach(e, get(paperdoll, "CustomSlots"))
            if (k->customCount < 32) { k->customIds[k->customCount] = num(e, "Id", -1); snprintf(k->customNames[k->customCount++], 48, "%s", str(e, "Name")); }
    }
}

static int custom_slot(const Carrier *k, const char *name)
{
    for (int i = 0; i < k->customCount; i++) if (strcmp(k->customNames[i], name) == 0) return 100 + k->customIds[i];
    return -1;
}

static Item *in_slot(const Carrier *k, int key)
{
    for (int i = 0; i < k->slotCount; i++)
        if (k->slots[i].key == key)
            for (int j = 0; j < k->count; j++) if (k->items[j]->instanceId == k->slots[i].instanceId) return k->items[j];
    return NULL;
}

/* IsSlotFree: shown on their paperdoll, and nothing in it. */
static int slot_free(const Carrier *k, int key)
{
    if (key < SLOT_COUNT && k->disabled[key]) return 0;
    for (int i = 0; i < k->slotCount; i++) if (k->slots[i].key == key) return 0;
    return 1;
}

static int left_hand_locked(const Carrier *k) { Item *r = in_slot(k, S_RIGHT); return r && is_two_handed(r); }

/* EquipInSlot: theirs now, in that slot - By Equip on them, By Having An Item for the map. */
static void equip(Carrier *k, Item *it, int key)
{
    it->instanceId = k->nextInstanceId++;
    add_to(&k->items, &k->count, &k->cap, it);
    if (k->slotCount < 64) k->slots[k->slotCount++] = (Slot){ key, it->instanceId };
    conditions_record_person_item("equip", k->characterId, it->category, it->itemId);
    events_fire_character(k->characterId, TR_EQUIP);
    events_fire_map(TR_HAVING);
}

static void received(Carrier *k) { (void)k; events_fire_map(TR_HAVING); } /* FinishCharacterReceive */

/* RemoveFromCharacter: out of their hands / slots (By UnEquip when it was in one). */
static void remove_from_character(Carrier *k, Item *it)
{
    remove_from(k->items, &k->count, it);
    int was = 0;
    for (int i = 0; i < k->slotCount; i++)
        if (k->slots[i].instanceId == it->instanceId) { k->slots[i] = k->slots[--k->slotCount]; i--; was = 1; }
    if (was) { conditions_record_person_item("unequip", k->characterId, it->category, it->itemId); events_fire_character(k->characterId, TR_UNEQUIP); }
}

static double carried_weight(const Carrier *k)
{
    double w = 0;
    for (int i = 0; i < k->count; i++) w += weight_of(k->items[i]);
    return w;
}

/* Joins a stack of the same thing in these contents if it stays within 100 (TryCombineIntoContents). */
static int combine_into(Item **list, int count, const Item *it)
{
    if (it->category != CAT_OBJECT || !can_combine(it->category, it->itemId)) return 0;
    for (int i = 0; i < count; i++)
        if (list[i]->category == it->category && list[i]->itemId == it->itemId && list[i]->quantity + it->quantity <= MAX_QUANTITY)
        {
            list[i]->quantity += it->quantity;
            return 1;
        }
    return 0;
}

/* CanContainerAccept: not a body, not locked, not into itself, and room for it. */
static int container_accepts(const Item *bag, const Item *it)
{
    cJSON *d = def_of(CAT_CONTAINER, bag->itemId);
    if (!d || flag(d, "IsBodyContainer")) return 0;
    if (bag->locked >= 0 ? bag->locked : flag(d, "IsLocked")) return 0;
    if (bag == it || holds(it->contents, it->count, bag)) return 0;
    double used = 0;
    for (int i = 0; i < bag->count; i++) used += volume_of(bag->contents[i]);
    return used + volume_of(it) <= dbl(d, "MaxVolume");
}

/* DropOnCharacter for a loose thing (picked up, given). */
int items_give_to(int characterId, Item *it)
{
    Carrier *k = carrier_of(characterId);
    refusal[0] = 0;
    if (!k) { snprintf(refusal, sizeof refusal, "They can't carry anything."); return 0; }
    /* drag.cc Check_weight: the party can carry 2 x Strength. */
    if (k->isPlayer || k->inParty)
    {
        double carried = carried_weight(k), adding = weight_of(it), max = 2.0 * k->strength;
        if (carried + adding > max)
        {
            snprintf(refusal, sizeof refusal, "Too heavy: \"%s\" weighs %.2f, and they already carry %.2f of the %.2f they can.", items_name(it->category, it->itemId), adding, carried, max);
            return 0;
        }
    }
    cJSON *d = def_of(it->category, it->itemId);
    /* Ammo goes to its own slot, joining a stack of the same ammo there. */
    if (it->category == CAT_OBJECT && num(d, "Type", 0) == OT_AMMO)
    {
        cJSON *ammo = get(d, "Ammo"), *pref = get(ammo, "PreferredEquipSlot");
        int key = cJSON_IsNumber(pref) ? pref->valueint : str(ammo, "PreferredCustomSlotName")[0] ? custom_slot(k, str(ammo, "PreferredCustomSlotName")) : -1;
        if (key >= 0)
        {
            Item *held = in_slot(k, key);
            if (held && held->category == it->category && held->itemId == it->itemId && held->quantity + it->quantity <= MAX_QUANTITY)
            {
                held->quantity += it->quantity;
                items_free(it);
                received(k);
                return 1;
            }
            if (slot_free(k, key)) { equip(k, it, key); return 1; }
        }
    }
    if (it->category == CAT_CONTAINER)
    {
        /* Its own Location slot, then the right hand, then the left - never into a bag. */
        cJSON *pref = get(d, "PreferredEquipSlot");
        int key = cJSON_IsNumber(pref) ? pref->valueint : str(d, "PreferredCustomSlotName")[0] ? custom_slot(k, str(d, "PreferredCustomSlotName")) : -1;
        if (key >= 0 && slot_free(k, key)) { equip(k, it, key); return 1; }
        if (slot_free(k, S_RIGHT)) { equip(k, it, S_RIGHT); return 1; }
        if (slot_free(k, S_LEFT) && !left_hand_locked(k)) { equip(k, it, S_LEFT); return 1; }
        snprintf(refusal, sizeof refusal, "Unable to pick it up - its own slot and both hands are full.");
        return 0;
    }
    /* Into the backpack, then the pouch, if it fits. */
    static const int BAGS[2] = { S_BACKPACK, S_POUCH };
    for (int b = 0; b < 2; b++)
    {
        Item *bag = in_slot(k, BAGS[b]);
        if (!bag || bag->category != CAT_CONTAINER || bag == it || !container_accepts(bag, it)) continue;
        if (combine_into(bag->contents, bag->count, it)) { items_free(it); received(k); return 1; }
        add_to(&bag->contents, &bag->count, &bag->cap, it);
        received(k);
        return 1;
    }
    /* A free hand (a two-handed thing: the right, with the left free too). */
    int two = is_two_handed(it);
    if (slot_free(k, S_RIGHT) && (!two || slot_free(k, S_LEFT))) { equip(k, it, S_RIGHT); return 1; }
    if (!two && !left_hand_locked(k) && slot_free(k, S_LEFT)) { equip(k, it, S_LEFT); return 1; }
    snprintf(refusal, sizeof refusal, "Unable to pick up - it doesn't fit in the Backpack or Pouch container, and both hands are full.");
    return 0;
}

/* ---------------- Having, taking ---------------- */

static int count_in(Item **list, int count, int category, int itemId)
{
    int n = 0;
    for (int i = 0; i < count; i++)
    {
        if (list[i]->category == category && list[i]->itemId == itemId) n += list[i]->quantity > 0 ? list[i]->quantity : 1;
        n += count_in(list[i]->contents, list[i]->count, category, itemId);
    }
    return n;
}

int items_carries(int characterId, int category, int itemId)
{
    Carrier *k = carrier_of(characterId);
    return k && count_in(k->items, k->count, category, itemId) > 0;
}

int items_party_has(int category, int itemId)
{
    for (int i = 0; i < carrierCount; i++)
        if ((carriers[i].isPlayer || carriers[i].inParty) && count_in(carriers[i].items, carriers[i].count, category, itemId) > 0) return 1;
    return 0;
}

/* Where each one is: the item and the list holding it (the carrier's own list = in a slot). */
typedef struct Found { Item *item; Item ***list; int *count; } Found;

static void find_all(Item ***list, int *count, int category, int itemId, Found *out, int *n, int max)
{
    for (int i = 0; i < *count; i++)
    {
        Item *it = (*list)[i];
        if (it->category == category && it->itemId == itemId && *n < max) out[(*n)++] = (Found){ it, list, count };
        find_all(&it->contents, &it->count, category, itemId, out, n, max);
    }
}

int items_take(int characterId, int category, int itemId, int quantity, Item **out, int max, int *taken)
{
    *taken = 0;
    Carrier *k = carrier_of(characterId);
    if (!k) return 0;
    if (count_in(k->items, k->count, category, itemId) < quantity) return 0;
    Found found[256];
    int n = 0;
    find_all(&k->items, &k->count, category, itemId, found, &n, 256);
    int left = quantity;
    for (int i = 0; i < n && left > 0 && *taken < max; i++)
    {
        Item *it = found[i].item;
        int have = it->quantity > 0 ? it->quantity : 1;
        if (have > left)
        {
            it->quantity = have - left; /* the stack shrinks; the rest is given */
            out[(*taken)++] = new_item(category, itemId, left);
            left = 0;
            break;
        }
        if (found[i].list == &k->items) remove_from_character(k, it); /* out of its hand / slot too */
        else remove_from(*found[i].list, found[i].count, it);
        out[(*taken)++] = it; /* the very thing, as it is */
        left -= have;
    }
    events_fire_map(TR_NOT_HAVING);
    return 1;
}

int items_make_gifts(int category, int itemId, int quantity, Item **out, int max)
{
    if (can_combine(category, itemId)) { out[0] = items_make(category, itemId, quantity); return 1; }
    int n = 0;
    for (int i = 0; i < quantity && n < max; i++) out[n++] = items_make(category, itemId, 1);
    return n;
}

int items_can_lift(int category, int itemId)
{
    cJSON *d = def_of(category, itemId);
    if (category == CAT_OBJECT) { cJSON *p = get(d, "IsPortable"); return !cJSON_IsBool(p) || cJSON_IsTrue(p); }
    if (category == CAT_CONTAINER) { cJSON *p = get(d, "CanBePicked"); return !cJSON_IsBool(p) || cJSON_IsTrue(p); }
    return 1;
}

/* ---------------- Containers put down on the map keep what's in them ---------------- */

typedef struct Parked { int uid; Item *item; } Parked;
static Parked *parked; static int parkedCount, parkedCap;

void items_park(int uid, Item *item)
{
    if (parkedCount == parkedCap) { parkedCap = parkedCap ? parkedCap * 2 : 16; parked = (Parked *)realloc(parked, sizeof(Parked) * (size_t)parkedCap); }
    parked[parkedCount++] = (Parked){ uid, item };
    if (item && item->category == CAT_OBJECT) world_thing_set_facing(uid, item->facing); /* put down the way it faced (FacingRuntime) */
}

/* The thing on the map with that uid as it was put down - or, never carried, a fresh one (a container with its Starting Contents). */
Item *items_unpark(int uid, int category, int itemId, int quantity)
{
    for (int i = 0; i < parkedCount; i++)
        if (parked[i].uid == uid) { Item *it = parked[i].item; parked[i] = parked[--parkedCount]; it->quantity = quantity; it->facing = world_thing_facing(uid); return it; }
    /* never carried: SeedContentsOf - its own contents if the Map Editor gave it some, else its Starting Contents */
    cJSON *own = (cJSON *)world_thing_own_contents(uid), *c, *placed = world_thing_placed(uid);
    Item *it;
    if (category != CAT_CONTAINER || !own) it = items_make(category, itemId, quantity);
    else
    {
        it = new_item(category, itemId, quantity);
        cJSON_ArrayForEach(c, own) add_to(&it->contents, &it->count, &it->cap, read_item(c));
    }
    cJSON *locked = get(placed, "IsLockedRuntime");
    if (category == CAT_CONTAINER && cJSON_IsBool(locked)) it->locked = cJSON_IsTrue(locked);
    if (cJSON_IsObject(get(placed, "DollRuntime"))) it->doll = dolls_state_from_json(get(placed, "DollRuntime"));
    cJSON *placedKey;
    cJSON_ArrayForEach(placedKey, get(placed, "KeyIdsRuntime")) if (it->keyCount < 32 && cJSON_IsNumber(placedKey)) it->keys[it->keyCount++] = placedKey->valueint;
    if (cJSON_IsArray(get(placed, "KeyIdsRuntime"))) it->keysSet = 1;
    it->charges = num(placed, "ChargesRuntime", -1);
    it->facing = world_thing_facing(uid); /* picked up: the way it faced goes with it */
    return it;
}

/* Put back where it was found (a pick-up that was refused). */
void items_repark(int uid, Item *item) { items_park(uid, item); }

int items_count(int characterId, int category, int itemId)
{
    Carrier *k = carrier_of(characterId);
    return k ? count_in(k->items, k->count, category, itemId) : 0;
}

int items_party_count(int category, int itemId)
{
    int n = 0;
    for (int i = 0; i < carrierCount; i++)
        if (carriers[i].isPlayer || carriers[i].inParty) n += count_in(carriers[i].items, carriers[i].count, category, itemId);
    return n;
}

/* PartyMoney: every coin the party carries, a Money object's count times its currency's worth. */
static double money_in(Item **list, int count, cJSON *currencies)
{
    double total = 0;
    for (int i = 0; i < count; i++)
    {
        cJSON *d = list[i]->category == CAT_OBJECT ? def_of(CAT_OBJECT, list[i]->itemId) : NULL;
        if (d && num(d, "Type", 0) == OT_MONEY)
        {
            double worth = 1;
            cJSON *c;
            cJSON_ArrayForEach(c, currencies) if (num(c, "Id", -2) == num(get(d, "Money"), "CurrencyId", -1)) worth = dbl(c, "Value");
            total += (list[i]->quantity > 0 ? list[i]->quantity : 1) * worth;
        }
        total += money_in(list[i]->contents, list[i]->count, currencies);
    }
    return total;
}

double items_party_money(cJSON *objects, cJSON *currencies)
{
    (void)objects;
    double total = 0;
    for (int i = 0; i < carrierCount; i++)
        if (carriers[i].isPlayer || carriers[i].inParty) total += money_in(carriers[i].items, carriers[i].count, currencies);
    return total;
}

/* A light's state, carried with it. */
void items_light_state(const Item *it, int *lit, int *spent, double *burnLeft) { *lit = it->lit; *spent = it->spent; *burnLeft = it->burnLeft; }
void items_set_light_state(Item *it, int lit, int spent, double burnLeft) { it->lit = lit; it->spent = spent; it->burnLeft = burnLeft; }

/* The lit lights in a character's hands: how bright, what they give. */
int items_hand_lights(int characterId, int *brightness, int *gives)
{
    Carrier *k = carrier_of(characterId);
    int n = 0;
    if (!k) return 0;
    static const int HANDS[2] = { S_RIGHT, S_LEFT };
    for (int h = 0; h < 2; h++)
    {
        Item *it = in_slot(k, HANDS[h]);
        cJSON *d = it && it->lit && it->category == CAT_OBJECT ? def_of(CAT_OBJECT, it->itemId) : NULL;
        if (!d || num(d, "Type", 0) != 4) continue;
        brightness[n] = num(get(d, "Light"), "Brightness", 0); gives[n] = num(get(d, "Light"), "Gives", 0); n++;
    }
    return n;
}

/* BurnLights for carried lights: burnt out, it's spent (its sound). */
static void burn_in(Item **list, int count, double minutes)
{
    for (int i = 0; i < count; i++)
    {
        Item *it = list[i];
        if (it->lit && it->burnLeft >= 0)
        {
            cJSON *d = def_of(it->category, it->itemId);
            it->burnLeft -= minutes;
            if (it->burnLeft <= 0) { it->lit = 0; it->spent = 1; sound_play(num(get(d, "Light"), "SpentSoundResourceId", -1)); }
        }
        burn_in(it->contents, it->count, minutes);
    }
}

void items_burn_lights(double minutes)
{
    if (minutes <= 0) return;
    for (int i = 0; i < carrierCount; i++) burn_in(carriers[i].items, carriers[i].count, minutes);
}

static int is_item(const Item *it, int category, int itemId) { return category < 0 || (it->category == category && it->itemId == itemId); }

static int is_worn(const Carrier *k, const Item *it)
{
    for (int i = 0; i < k->slotCount; i++) if (k->slots[i].instanceId == it->instanceId) return 1;
    return 0;
}

/* Equipped: in one of their slots (category -1: anything). */
int items_equipped(int characterId, int category, int itemId)
{
    Carrier *k = carrier_of(characterId);
    if (!k) return 0;
    for (int i = 0; i < k->count; i++) if (is_worn(k, k->items[i]) && is_item(k->items[i], category, itemId)) return 1;
    return 0;
}

static int carried_in(Item **list, int count, int category, int itemId, const Carrier *k, int top)
{
    for (int i = 0; i < count; i++)
    {
        if (!(top && is_worn(k, list[i])) && is_item(list[i], category, itemId)) return 1;
        if (carried_in(list[i]->contents, list[i]->count, category, itemId, k, 0)) return 1;
    }
    return 0;
}

/* Carried, not worn (inside a bag counts as carried). */
int items_carried_not_worn(int characterId, int category, int itemId)
{
    Carrier *k = carrier_of(characterId);
    return k && carried_in(k->items, k->count, category, itemId, k, 1);
}

static int count_any(Item **list, int count, int category, int itemId)
{
    int n = 0;
    for (int i = 0; i < count; i++)
    {
        if (is_item(list[i], category, itemId)) n += list[i]->quantity > 0 ? list[i]->quantity : 1;
        n += count_any(list[i]->contents, list[i]->count, category, itemId);
    }
    return n;
}

int items_count_any(int characterId, int category, int itemId) { Carrier *k = carrier_of(characterId); return k ? count_any(k->items, k->count, category, itemId) : 0; }

int items_party_count_any(int category, int itemId)
{
    int n = 0;
    for (int i = 0; i < carrierCount; i++) if (carriers[i].isPlayer || carriers[i].inParty) n += count_any(carriers[i].items, carriers[i].count, category, itemId);
    return n;
}

void items_set_in_party(int characterId, int join) { Carrier *k = carrier_of(characterId); if (k) k->inParty = join; }

/* A container on the map: what's in it now (taken from the thing's parked contents; put back with items_park). */
int items_count_in(const Item *container, int category, int itemId) { return container ? count_any(container->contents, container->count, category, itemId) : 0; }

void items_empty(Item *container)
{
    for (int i = 0; i < container->count; i++) items_free(container->contents[i]);
    container->count = 0;
}

void items_put_in(Item *container, Item *it) { add_to(&container->contents, &container->count, &container->cap, it); }

void items_set_locked(Item *it, int locked) { if (it) it->locked = locked; }

/* CarriesKeyFor: their key for it, loose or on a keyring they carry (its keys: KeyIdsRuntime). */
static int has_key_in(Item **list, int count, int keyId)
{
    for (int i = 0; i < count; i++)
    {
        if (list[i]->category == CAT_OBJECT && list[i]->itemId == keyId) return 1;
        for (int k = 0; k < list[i]->keyCount; k++) if (list[i]->keys[k] == keyId) return 1;
        if (has_key_in(list[i]->contents, list[i]->count, keyId)) return 1;
    }
    return 0;
}

int items_carries_key(int characterId, int keyId)
{
    Carrier *k = carrier_of(characterId);
    return keyId >= 0 && k && has_key_in(k->items, k->count, keyId);
}

/* A lockpick they carry (its object id), or -1. */
static int lockpick_in(Item **list, int count)
{
    for (int i = 0; i < count; i++)
    {
        cJSON *d = list[i]->category == CAT_OBJECT ? def_of(CAT_OBJECT, list[i]->itemId) : NULL;
        if (d && num(d, "Type", 0) == OT_LOCKPICK) return list[i]->itemId;
        int inner = lockpick_in(list[i]->contents, list[i]->count);
        if (inner >= 0) return inner;
    }
    return -1;
}

int items_lockpick(int characterId) { Carrier *k = carrier_of(characterId); return k ? lockpick_in(k->items, k->count) : -1; }

/* SlotToReady: its own slot (weapon, armor, ammo, a container's Location), else the right hand. */
static int slot_to_ready(Carrier *k, Item *it)
{
    cJSON *d = def_of(it->category, it->itemId), *pref = NULL, *section = NULL;
    int key = -1;
    if (it->category == CAT_CONTAINER) section = d;
    else if (it->category == CAT_OBJECT)
    {
        int type = num(d, "Type", 0);
        section = type == OT_AMMO ? get(d, "Ammo") : type == OT_WEAPON ? get(d, "Weapon") : type == 36 ? get(d, "Armor") : NULL;
    }
    if (section)
    {
        pref = get(section, it->category == CAT_CONTAINER || (it->category == CAT_OBJECT && num(d, "Type", 0) == OT_AMMO) ? "PreferredEquipSlot" : "EquipSlot");
        const char *custom = str(section, it->category == CAT_CONTAINER || num(d, "Type", 0) == OT_AMMO ? "PreferredCustomSlotName" : "CustomSlotName");
        key = cJSON_IsNumber(pref) ? pref->valueint : custom[0] ? custom_slot(k, custom) : -1;
    }
    if (key >= 0 && key < SLOT_COUNT && k->disabled[key]) key = -1; /* a slot turned off isn't there */
    return key >= 0 ? key : S_RIGHT;
}

/* TryStowInBag: off a slot into the Backpack, then the Pouch, if it fits; 0 = no bag has room. */
static int stow_in_bag(Carrier *k, int fromSlot, Item *it)
{
    static const int BAGS[2] = { S_BACKPACK, S_POUCH };
    for (int b = 0; b < 2; b++)
    {
        if (BAGS[b] == fromSlot) continue;
        Item *bag = in_slot(k, BAGS[b]);
        if (!bag || bag->category != CAT_CONTAINER || bag == it || !container_accepts(bag, it)) continue;
        remove_from_character(k, it);
        if (combine_into(bag->contents, bag->count, it)) items_free(it);
        else add_to(&bag->contents, &bag->count, &bag->cap, it);
        return 1;
    }
    return 0;
}

static int slot_of_item(const Carrier *k, const Item *it)
{
    for (int i = 0; i < k->slotCount; i++) if (k->slots[i].instanceId == it->instanceId) return k->slots[i].key;
    return -1;
}

/* StowOrDrop: off a slot into a bag, else out of their hands - returned to be put down at their feet. */
static Item *stow_or_drop(Carrier *k, int slot, Item *it)
{
    if (stow_in_bag(k, slot, it)) return NULL;
    remove_from_character(k, it);
    return it;
}

/* Equip Equipment: readies one they carry (not readied yet) in its slot; what's in the way comes off into the pack, or out (dropped[]). */
int items_equip(int characterId, int category, int itemId, Item **dropped, int *droppedCount)
{
    Carrier *k = carrier_of(characterId);
    *droppedCount = 0;
    if (!k) return 0;
    Found found[256];
    int n = 0;
    find_all(&k->items, &k->count, category, itemId, found, &n, 256);
    int pick = -1;
    for (int i = 0; i < n && pick < 0; i++) if (!(found[i].list == &k->items && slot_of_item(k, found[i].item) >= 0)) pick = i;
    if (pick < 0) pick = n > 0 ? 0 : -1;
    if (pick < 0) return 0;
    Item *it = found[pick].item;
    int slot = slot_to_ready(k, it);
    if (found[pick].list == &k->items && slot_of_item(k, it) == slot) return 1; /* already there */
    int clear[2] = { slot, -1 };
    if (is_two_handed(it) && slot == S_RIGHT) clear[1] = S_LEFT;
    if (slot == S_LEFT && left_hand_locked(k)) clear[1] = S_RIGHT;
    for (int c = 0; c < 2; c++)
    {
        if (clear[c] < 0) continue;
        Item *occupant = in_slot(k, clear[c]);
        if (occupant && occupant != it) { Item *out = stow_or_drop(k, clear[c], occupant); if (out) dropped[(*droppedCount)++] = out; }
    }
    /* out of where it was, into the slot */
    if (found[pick].list == &k->items) remove_from_character(k, it);
    else remove_from(*found[pick].list, found[pick].count, it);
    equip(k, it, slot);
    return 1;
}

/* Unequip Equipment: off its slot into the pack, else out (returned to be put down). 0 if not worn or held. */
int items_unequip(int characterId, int category, int itemId, Item **dropped)
{
    Carrier *k = carrier_of(characterId);
    *dropped = NULL;
    if (!k) return 0;
    for (int i = 0; i < k->count; i++)
    {
        Item *it = k->items[i];
        int slot = slot_of_item(k, it);
        if (slot < 0 || it->category != category || it->itemId != itemId) continue;
        *dropped = stow_or_drop(k, slot, it);
        return 1;
    }
    return 0;
}

/* Pick Item From Container: out of a container's contents into their hands / pack (each by Actor::add); how many were taken. */
int items_take_from(Item *container, int characterId, int category, int itemId, int quantity)
{
    int any = category < 0 || itemId < 0, taken = 0, left = quantity;
    for (int i = 0; i < container->count; i++)
    {
        Item *it = container->contents[i];
        if (!any && (it->category != category || it->itemId != itemId)) continue;
        if (!any && left <= 0) break;
        int have = it->quantity > 0 ? it->quantity : 1;
        if (!any && have > left)
        {
            Item *part = new_item(it->category, it->itemId, left);
            it->quantity = have - left;
            if (items_give_to(characterId, part)) { taken += left; left = 0; }
            else { it->quantity = have; items_free(part); }
            continue;
        }
        remove_from(container->contents, &container->count, it);
        if (!items_give_to(characterId, it)) { add_to(&container->contents, &container->count, &container->cap, it); /* too heavy / no room: it stays */ continue; }
        i--;
        taken += have; left -= have;
    }
    return taken;
}

int items_locked(const Item *it, int byDefault) { return it->locked >= 0 ? it->locked : byDefault; }

/* Actor::die: everything it carried - worn or in the pack - out of it, into one container (the body's). NULL if it carried nothing. */
Item *items_strip(int characterId)
{
    Carrier *k = carrier_of(characterId);
    if (!k || !k->count) return NULL;
    Item *body = new_item(CAT_CONTAINER, -1, 1);
    for (int i = 0; i < k->count; i++) add_to(&body->contents, &body->count, &body->cap, k->items[i]);
    k->count = 0; k->slotCount = 0;
    return body;
}

/* Not human: what may be found in its body ("DeathDrops"), each by its chance - into the body's belongings (made if it had none). */
Item *items_death_drops(int characterId, Item *body, int (*roll)(int))
{
    cJSON *c = NULL, *d;
    cJSON_ArrayForEach(d, get(get(get((cJSON *)world_json(), "Project"), "Characters"), "Characters")) if (num(d, "Id", -2) == characterId) c = d;
    if (!c || !cJSON_IsFalse(get(c, "IsHuman"))) return body;
    cJSON_ArrayForEach(d, get(c, "DeathDrops"))
    {
        int objectId = num(d, "ObjectId", -1), quantity = num(d, "Quantity", 1);
        if (!def_of(CAT_OBJECT, objectId) || roll(100) >= num(d, "Percent", 50)) continue;
        Item *gifts[100];
        int n = items_make_gifts(CAT_OBJECT, objectId, quantity > 1 ? quantity : 1, gifts, 100);
        if (!body) body = new_item(CAT_CONTAINER, -1, 1);
        for (int i = 0; i < n; i++) add_to(&body->contents, &body->count, &body->cap, gifts[i]);
    }
    return body;
}

/* ---------------- Weapons, armor, ammo (combat) ---------------- */

static int object_type(const Item *it) { return it->category == CAT_OBJECT ? num(def_of(CAT_OBJECT, it->itemId), "Type", -1) : -1; }

/* Actor::get_weapon: the weapon in hand (right, then left), else one worn in its own slot. NULL: bare hands. */
cJSON *items_weapon(int characterId, Item **item)
{
    Carrier *k = carrier_of(characterId);
    if (item) *item = NULL;
    if (!k) return NULL;
    Item *r = in_slot(k, S_RIGHT), *l = in_slot(k, S_LEFT), *found = NULL;
    if (r && object_type(r) == 35) found = r;
    else if (l && object_type(l) == 35) found = l;
    else for (int i = 0; i < k->count && !found; i++) if (is_worn(k, k->items[i]) && object_type(k->items[i]) == 35) found = k->items[i];
    if (!found) return NULL;
    if (item) *item = found;
    return def_of(CAT_OBJECT, found->itemId);
}

/* Armor worn anywhere (a shield in a hand counts, as in Exult). */
int items_armor(int characterId, cJSON **out, int max)
{
    Carrier *k = carrier_of(characterId);
    int n = 0;
    for (int i = 0; k && i < k->count && n < max; i++)
        if (is_worn(k, k->items[i]) && object_type(k->items[i]) == 36) out[n++] = def_of(CAT_OBJECT, k->items[i]->itemId);
    return n;
}

static int same_family(const char *a, const char *b)
{
    while (*a == ' ') a++;
    while (*b == ' ') b++;
    size_t la = strlen(a), lb = strlen(b);
    while (la && a[la - 1] == ' ') la--;
    while (lb && b[lb - 1] == ' ') lb--;
    if (la != lb) return 0;
    for (size_t i = 0; i < la; i++) if (tolower((unsigned char)a[i]) != tolower((unsigned char)b[i])) return 0;
    return 1;
}

static int is_ammo_of(const Item *it, const char *family)
{
    if (object_type(it) != 5) return 0;
    return same_family(str(get(def_of(CAT_OBJECT, it->itemId), "Ammo"), "Family"), family);
}

static Item *ammo_inside(Item *container, const char *family, Item **holder)
{
    for (int i = 0; i < container->count; i++)
    {
        if (is_ammo_of(container->contents[i], family)) { *holder = container; return container->contents[i]; }
        if (container->contents[i]->count)
        {
            Item *deeper = ammo_inside(container->contents[i], family, holder);
            if (deeper) return deeper;
        }
    }
    return NULL;
}

/* One of a family used up - in a slot first (a quiver), then the pack, then inside what's carried. Its definition, or NULL when there's none. */
cJSON *items_use_ammo(int characterId, const char *family)
{
    Carrier *k = carrier_of(characterId);
    if (!k) return NULL;
    Item *found = NULL, *holder = NULL;
    for (int i = 0; i < k->count && !found; i++) if (is_worn(k, k->items[i]) && is_ammo_of(k->items[i], family)) found = k->items[i];
    for (int i = 0; i < k->count && !found; i++) if (is_ammo_of(k->items[i], family)) found = k->items[i];
    for (int i = 0; i < k->count && !found; i++) if (k->items[i]->count) found = ammo_inside(k->items[i], family, &holder);
    if (!found) return NULL;
    cJSON *def = def_of(CAT_OBJECT, found->itemId);
    if (found->quantity > 1) found->quantity--;
    else if (holder) { remove_from(holder->contents, &holder->count, found); items_free(found); }
    else { remove_from_character(k, found); items_free(found); }
    return def;
}

/* A charge used: 0 when there's none left. Empty and marked so, the weapon is gone. */
int items_use_charge(int characterId, Item *weapon, int charges, int goneWhenEmpty)
{
    if (!weapon) return 0;
    if (weapon->charges < 0) weapon->charges = charges > 0 ? charges : 0;
    if (weapon->charges <= 0) return 0;
    weapon->charges--;
    if (weapon->charges <= 0 && goneWhenEmpty) { Carrier *k = carrier_of(characterId); if (k) { remove_from_character(k, weapon); items_free(weapon); } }
    return 1;
}

/* One of a stack (or the thing itself) out of their hands: thrown. */
void items_use_one(int characterId, Item *it)
{
    Carrier *k = carrier_of(characterId);
    if (!k || !it) return;
    if (it->quantity > 1) { it->quantity--; return; }
    remove_from_character(k, it);
    items_free(it);
}

/* extract_contents: everything in it put down where it stood (each keeping what it is); the empty shell freed. */
void items_spill(Item *box, int x, int y, int onFloor)
{
    if (!box) return;
    for (int i = 0; i < box->count; i++)
    {
        Item *it = box->contents[i];
        int uid = world_thing_put(it->category, it->itemId, it->quantity, x, y, onFloor);
        if (uid >= 0) items_park(uid, it);
        else items_free(it);
    }
    box->count = 0;
    items_free(box);
}

/* ---------------- The inventory windows (gumps.c) ---------------- */

Item *items_in_slot(int characterId, int slotKey) { Carrier *k = carrier_of(characterId); return k ? in_slot(k, slotKey) : NULL; }

/* A slot is there on their paperdoll (a built-in one not turned off; a custom one they have). */
int items_slot_shown(int characterId, int slotKey)
{
    Carrier *k = carrier_of(characterId);
    if (!k) return 0;
    if (slotKey < SLOT_COUNT) return slotKey >= 0 && !k->disabled[slotKey];
    for (int i = 0; i < k->customCount; i++) if (100 + k->customIds[i] == slotKey) return 1;
    return 0;
}

/* What they carry outside every slot (the backpack list). */
int items_loose(int characterId, Item **out, int max)
{
    Carrier *k = carrier_of(characterId);
    int n = 0;
    for (int i = 0; k && i < k->count && n < max; i++) if (!is_worn(k, k->items[i])) out[n++] = k->items[i];
    return n;
}

int items_contents(Item *container, Item ***list) { *list = container->contents; return container->count; }
void items_spot(const Item *it, double *x, double *y) { *x = it->cx; *y = it->cy; }
void items_set_spot(Item *it, double x, double y, int autoPlaced) { it->cx = x; it->cy = y; it->autoPlaced = autoPlaced; }
int items_unplaced(const Item *it) { return it->cx <= UNPLACED || it->cy <= UNPLACED; }
int items_auto_placed(const Item *it) { return it->autoPlaced; }
int items_is_locked(const Item *it) { return it->category == CAT_CONTAINER && items_locked(it, flag(def_of(CAT_CONTAINER, it->itemId), "IsLocked")); }
void items_light(const Item *it, int *lit, int *spent) { *lit = it->lit; *spent = it->spent; }
int items_stack_at_top(Item *container, Item *it)
{
    for (int i = 0; i < container->count; i++)
        if (container->contents[i] == it) { memmove(&container->contents[i], &container->contents[i + 1], sizeof(Item *) * (size_t)(container->count - i - 1)); container->contents[container->count - 1] = it; return 1; }
    return 0;
}

/* Where something is: in a slot / loose on a character (holder NULL), or inside a container. */
static int find_in(Item **list, int count, const Item *it, Item *parent, Item **holder)
{
    for (int i = 0; i < count; i++)
    {
        if (list[i] == it) { *holder = parent; return 1; }
        if (find_in(list[i]->contents, list[i]->count, it, list[i], holder)) return 1;
    }
    return 0;
}

int items_where(const Item *it, int *characterId, Item **holder, int *slotKey)
{
    *characterId = -1; *holder = NULL; *slotKey = -1;
    for (int c = 0; c < carrierCount; c++)
    {
        Carrier *k = &carriers[c];
        Item *h = NULL;
        if (!find_in(k->items, k->count, it, NULL, &h)) continue;
        *characterId = k->characterId; *holder = h;
        if (!h) *slotKey = slot_of_item(k, it);
        return 1;
    }
    for (int p = 0; p < parkedCount; p++)
    {
        Item *h = NULL;
        if (parked[p].item == it) return 2; /* lying on the map */
        if (find_in(parked[p].item->contents, parked[p].item->count, it, parked[p].item, &h)) { *holder = h; return 3; } /* in a container on the map */
    }
    return 0;
}

/* The character carrying it, at any depth (-1: nobody). */
int items_carrier_of(const Item *it) { int id, slot; Item *h; return items_where(it, &id, &h, &slot) == 1 ? id : -1; }

/* Taken out of wherever it is, quietly (no events yet): what's needed to put it back. */
int items_detach(Item *it, ItemPlace *from)
{
    memset(from, 0, sizeof *from);
    from->slotKey = -1;
    int where = items_where(it, &from->characterId, &from->holder, &from->slotKey);
    if (!where || where == 2) return 0;
    if (from->holder)
    {
        for (int i = 0; i < from->holder->count; i++) if (from->holder->contents[i] == it) from->index = i;
        remove_from(from->holder->contents, &from->holder->count, it);
        return 1;
    }
    Carrier *k = carrier_of(from->characterId);
    for (int i = 0; i < k->count; i++) if (k->items[i] == it) from->index = i;
    remove_from(k->items, &k->count, it);
    for (int i = 0; i < k->slotCount; i++) if (k->slots[i].instanceId == it->instanceId) { k->slots[i] = k->slots[--k->slotCount]; i--; }
    return 1;
}

int items_place_of(const Item *it, ItemPlace *place)
{
    memset(place, 0, sizeof *place);
    place->slotKey = -1;
    int where = items_where(it, &place->characterId, &place->holder, &place->slotKey);
    if (!where || where == 2) return 0;
    if (place->holder) { for (int i = 0; i < place->holder->count; i++) if (place->holder->contents[i] == it) place->index = i; return 1; }
    Carrier *k = carrier_of(place->characterId);
    if (!k) return 0;
    for (int i = 0; i < k->count; i++) if (k->items[i] == it) place->index = i;
    return 1;
}

Item *items_copy_stack(const Item *it, int quantity)
{
    Item *c = new_item(it->category, it->itemId, quantity);
    c->instanceId = it->instanceId;
    c->locked = it->locked; c->lit = it->lit; c->spent = it->spent; c->burnLeft = it->burnLeft;
    memcpy(c->keys, it->keys, sizeof c->keys); c->keyCount = it->keyCount; c->keysSet = it->keysSet;
    c->charges = it->charges; c->cx = it->cx; c->cy = it->cy; c->autoPlaced = it->autoPlaced;
    c->liquid = it->liquid; c->liquidSet = it->liquidSet; c->facing = it->facing;
    for (int i = 0; i < it->learnedCount; i++) items_learn_spell(c, it->learned[i]);
    return c;
}

void items_set_quantity(Item *it, int quantity) { if (it) it->quantity = quantity > 0 ? quantity : 1; }

void items_put_back_copy(const ItemPlace *place, Item *copy)
{
    if (place->holder)
    {
        int top = 0;
        for (int i = 0; i < place->holder->count; i++) if (place->holder->contents[i]->instanceId > top) top = place->holder->contents[i]->instanceId;
        copy->instanceId = top + 1;
    }
    else { Carrier *k = carrier_of(place->characterId); if (!k) { items_free(copy); return; } copy->instanceId = k->nextInstanceId++; }
    items_restore(place, copy);
}

/* Put back exactly where it was (the drop was refused). */
void items_restore(const ItemPlace *from, Item *it)
{
    if (from->holder)
    {
        Item *h = from->holder;
        add_to(&h->contents, &h->count, &h->cap, it);
        memmove(&h->contents[from->index + 1], &h->contents[from->index], sizeof(Item *) * (size_t)(h->count - 1 - from->index));
        h->contents[from->index] = it;
        return;
    }
    Carrier *k = carrier_of(from->characterId);
    if (!k) return;
    add_to(&k->items, &k->count, &k->cap, it);
    if (from->slotKey >= 0 && k->slotCount < 64) k->slots[k->slotCount++] = (Slot){ from->slotKey, it->instanceId };
}

/* The move went through: what leaving its place means (RemoveFromCharacter: By UnEquip; no longer having it). */
void items_detach_done(const ItemPlace *from, Item *it)
{
    if (from->holder || from->characterId < 0) return;
    if (from->slotKey >= 0) { conditions_record_person_item("unequip", from->characterId, it->category, it->itemId); events_fire_character(from->characterId, TR_UNEQUIP); }
    events_fire_map(TR_NOT_HAVING);
}

/* drag.cc Check_weight: the party can carry 2 x Strength (what's being moved is already off them). */
int items_check_weight(int characterId, const Item *it)
{
    Carrier *k = carrier_of(characterId);
    refusal[0] = 0;
    if (!k || !(k->isPlayer || k->inParty)) return 1;
    double carried = carried_weight(k), adding = weight_of(it), max = 2.0 * k->strength;
    if (carried + adding <= max) return 1;
    snprintf(refusal, sizeof refusal, "Too heavy: \"%s\" weighs %.2f, and they already carry %.2f of the %.2f they can.", items_name(it->category, it->itemId), adding, carried, max);
    return 0;
}

/* The slot its own kind names (a weapon's or armor's EquipSlot, a container's Location, ammo's slot); -1 none. */
static int own_slot(const Carrier *k, const Item *it)
{
    cJSON *d = def_of(it->category, it->itemId), *pref = NULL;
    const char *custom = "";
    if (it->category == CAT_CONTAINER) { pref = get(d, "PreferredEquipSlot"); custom = str(d, "PreferredCustomSlotName"); }
    else if (it->category == CAT_OBJECT)
    {
        int type = num(d, "Type", 0);
        cJSON *settings = type == OT_WEAPON ? get(d, "Weapon") : type == OT_ARMOR ? get(d, "Armor") : type == OT_AMMO ? get(d, "Ammo") : NULL;
        if (!settings) return -1;
        pref = get(settings, type == OT_AMMO ? "PreferredEquipSlot" : "EquipSlot");
        custom = str(settings, type == OT_AMMO ? "PreferredCustomSlotName" : "CustomSlotName");
    }
    if (cJSON_IsNumber(pref)) return pref->valueint;
    return custom[0] ? custom_slot(k, custom) : -1;
}

/* Actor::fits_in_spot: a hand holds anything; any other slot only what belongs there. */
int items_fits_slot(int characterId, const Item *it, int slotKey)
{
    Carrier *k = carrier_of(characterId);
    if (!k) return 0;
    if (slotKey == S_RIGHT || slotKey == S_LEFT) return 1;
    return own_slot(k, it) == slotKey;
}

int items_slot_free(int characterId, int slotKey)
{
    Carrier *k = carrier_of(characterId);
    return k && items_slot_shown(characterId, slotKey) && slot_free(k, slotKey);
}

/* TryEquipInSlot: into that very slot, by the hand rules - the left hand is unavailable while the right holds a two-handed thing. */
int items_equip_at(int characterId, Item *it, int slotKey)
{
    Carrier *k = carrier_of(characterId);
    refusal[0] = 0;
    if (!k) return 0;
    if (!items_check_weight(characterId, it)) return 0;
    if (!items_fits_slot(characterId, it, slotKey)) { snprintf(refusal, sizeof refusal, "\"%s\" doesn't go there - only in a hand, or its own slot.", items_name(it->category, it->itemId)); return 0; }
    int two = is_two_handed(it);
    if (slotKey == S_LEFT && left_hand_locked(k)) { snprintf(refusal, sizeof refusal, "Left hand is unavailable - the right hand is holding a two-handed weapon."); return 0; }
    if (slotKey == S_LEFT && two) { snprintf(refusal, sizeof refusal, "This needs both hands - put it in the right hand."); return 0; }
    if (slotKey == S_RIGHT && two && !slot_free(k, S_LEFT)) { snprintf(refusal, sizeof refusal, "This needs both hands - unequip the left hand first."); return 0; }
    if (!items_slot_free(characterId, slotKey)) return 0;
    equip(k, it, slotKey);
    return 1;
}

/* Into a container (TryPutIntoContainer): not a body, not locked, not into itself, room for it; a stack of the same joins; at a spot or none yet. 1 = it's in (or joined a stack: freed). */
int items_put_into(Item *bag, Item *it, int atSpot, double x, double y)
{
    cJSON *d = def_of(CAT_CONTAINER, bag->itemId);
    refusal[0] = 0;
    const char *name = items_name(it->category, it->itemId);
    if (!d) return 0;
    if (flag(d, "IsBodyContainer")) { snprintf(refusal, sizeof refusal, "You can't put things into \"%s\" - it's a body, not a real container.", str(d, "Name")); return 0; }
    if (items_is_locked(bag)) { snprintf(refusal, sizeof refusal, "\"%s\" is locked.", str(d, "Name")); return 0; }
    if (bag == it || holds(it->contents, it->count, bag)) { snprintf(refusal, sizeof refusal, "\"%s\" can't go inside itself.", name); return 0; }
    double used = 0;
    for (int i = 0; i < bag->count; i++) used += volume_of(bag->contents[i]);
    if (used + volume_of(it) > dbl(d, "MaxVolume"))
    {
        snprintf(refusal, sizeof refusal, "\"%s\" won't fit in \"%s\" - %.2f + %.2f would exceed the max volume of %.2f.", name, str(d, "Name"), used, volume_of(it), dbl(d, "MaxVolume"));
        return 0;
    }
    int carrier = items_carrier_of(bag);
    if (carrier >= 0 && !items_check_weight(carrier, it)) return 0;
    if (combine_into(bag->contents, bag->count, it)) { items_free(it); return 1; }
    if (it->lit && it->category == CAT_OBJECT) it->lit = 0; /* a torch put away goes out */
    if (atSpot) { it->cx = x; it->cy = y; it->autoPlaced = 0; } else { it->cx = it->cy = UNPLACED; it->autoPlaced = 0; }
    add_to(&bag->contents, &bag->count, &bag->cap, it);
    return 1;
}

/* The thing parked under a map uid, left where it is (a container open on the map); made the first time. */
Item *items_parked(int uid, int category, int itemId, int quantity)
{
    for (int i = 0; i < parkedCount; i++) if (parked[i].uid == uid) return parked[i].item;
    Item *it = items_unpark(uid, category, itemId, quantity);
    items_park(uid, it);
    return it;
}

/* Still somewhere in the game (carried, or on the map). */
int items_alive(const Item *it) { int id, slot; Item *h; return items_where(it, &id, &h, &slot) != 0; }

/* A carried object used (double-clicked in a window): a light lit or put out. 1 = something happened. */
int items_use_light(Item *it)
{
    cJSON *d = it->category == CAT_OBJECT ? def_of(CAT_OBJECT, it->itemId) : NULL;
    if (!d || num(d, "Type", 0) != 4 || it->spent) return 0;
    it->lit = !it->lit;
    cJSON *light = get(d, "Light");
    if (it->lit && it->burnLeft < 0 && num(light, "BurnMinutes", 0) > 0) it->burnLeft = num(light, "BurnMinutes", 0);
    sound_play(num(light, it->lit ? "LitSoundResourceId" : "OffSoundResourceId", -1));
    return 1;
}

/* The carry weight and the most they can (2 x Strength). */
void items_weights(int characterId, double *carried, double *max)
{
    Carrier *k = carrier_of(characterId);
    *carried = k ? carried_weight(k) : 0;
    *max = k ? 2.0 * k->strength : 0;
}

/* CanCombine, for a stack on the map. */
int items_stackable(int category, int itemId) { return can_combine(category, itemId); }

void *items_doll(const Item *it) { return it->doll; }
void items_set_doll(Item *it, void *doll) { it->doll = doll; }

/* The map thing a parked item is (0: it isn't on the map). */
int items_parked_uid(const Item *it) { for (int i = 0; i < parkedCount; i++) if (parked[i].item == it) return parked[i].uid; return 0; }

/* Turned into another object where it is (Paperdoll > Turn into): its paperdoll starts again. */
void items_become(Item *it, int itemId) { it->itemId = itemId; if (it->doll) { dolls_free_state(it->doll); it->doll = NULL; } }

/* One of it used up (a stack one less; the last one gone), wherever it is carried. */
void items_use_up_one(Item *it)
{
    if (it->quantity > 1) { it->quantity--; return; }
    ItemPlace from;
    if (items_detach(it, &from)) { items_detach_done(&from, it); items_free(it); }
}

/* The item whose paperdoll this is (carried or parked on the map), and its map uid (0 when carried). */
static Item *with_doll(Item **list, int count, const void *doll)
{
    for (int i = 0; i < count; i++)
    {
        if (list[i]->doll == doll) return list[i];
        Item *inner = with_doll(list[i]->contents, list[i]->count, doll);
        if (inner) return inner;
    }
    return NULL;
}

Item *items_find_doll(const void *doll, int *uid)
{
    *uid = 0;
    for (int c = 0; c < carrierCount; c++) { Item *it = with_doll(carriers[c].items, carriers[c].count, doll); if (it) return it; }
    for (int p = 0; p < parkedCount; p++)
    {
        if (parked[p].item->doll == doll) { *uid = parked[p].uid; return parked[p].item; }
        Item *it = with_doll(parked[p].item->contents, parked[p].item->count, doll);
        if (it) return it;
    }
    return NULL;
}

/* ---------------- Saving: what's carried, as InventoryItemInstance ---------------- */

cJSON *items_item_json(const Item *it)
{
    cJSON *j = cJSON_CreateObject(), *contents = cJSON_CreateArray();
    cJSON_AddNumberToObject(j, "InstanceId", it->instanceId);
    cJSON_AddNumberToObject(j, "Category", it->category);
    cJSON_AddNumberToObject(j, "ItemId", it->itemId);
    cJSON_AddNumberToObject(j, "Quantity", it->quantity);
    if (it->cx > UNPLACED && it->cy > UNPLACED) { cJSON_AddNumberToObject(j, "ContainerX", it->cx); cJSON_AddNumberToObject(j, "ContainerY", it->cy); }
    for (int i = 0; i < it->count; i++) cJSON_AddItemToArray(contents, items_item_json(it->contents[i]));
    cJSON_AddItemToObject(j, "Contents", contents);
    if (it->lit) cJSON_AddTrueToObject(j, "IsLitRuntime");
    if (it->spent) cJSON_AddTrueToObject(j, "IsSpentRuntime");
    cJSON_AddNumberToObject(j, "BurnLeftRuntime", it->burnLeft);
    if (it->locked >= 0) cJSON_AddBoolToObject(j, "IsLockedRuntime", it->locked);
    if (it->keyCount || it->keysSet) { cJSON *keys = cJSON_CreateArray(); for (int i = 0; i < it->keyCount; i++) cJSON_AddItemToArray(keys, cJSON_CreateNumber(it->keys[i])); cJSON_AddItemToObject(j, "KeyIdsRuntime", keys); }
    cJSON_AddNumberToObject(j, "ChargesRuntime", it->charges);
    cJSON_AddNumberToObject(j, "FacingRuntime", it->facing);
    if (it->liquidSet) cJSON_AddNumberToObject(j, "LiquidIdRuntime", it->liquid);
    if (it->learnedCount) { cJSON *spells = cJSON_CreateArray(); for (int i = 0; i < it->learnedCount; i++) cJSON_AddItemToArray(spells, cJSON_CreateString(it->learned[i])); cJSON_AddItemToObject(j, "LearnedSpellsRuntime", spells); }
    if (it->doll) cJSON_AddItemToObject(j, "DollRuntime", dolls_state_json(it->doll));
    return j;
}

static void put_into(cJSON *o, const char *k, cJSON *v) { if (cJSON_GetObjectItem(o, k)) cJSON_ReplaceItemInObject(o, k, v); else cJSON_AddItemToObject(o, k, v); }

/* Every character's belongings into the save's characters: Inventory, EquippedSlots, CustomEquippedSlots, IsInParty. */
void items_save(cJSON *characters)
{
    cJSON *c;
    cJSON_ArrayForEach(c, characters)
    {
        Carrier *k = carrier_of(num(c, "Id", -2));
        if (!k) continue;
        cJSON *inventory = cJSON_CreateArray(), *slots = cJSON_CreateObject(), *custom = cJSON_CreateObject();
        for (int i = 0; i < k->count; i++) cJSON_AddItemToArray(inventory, items_item_json(k->items[i]));
        for (int i = 0; i < k->slotCount; i++)
        {
            char key[16];
            if (k->slots[i].key < SLOT_COUNT) cJSON_AddNumberToObject(slots, SLOT_NAMES[k->slots[i].key], k->slots[i].instanceId);
            else { snprintf(key, sizeof key, "%d", k->slots[i].key - 100); cJSON_AddNumberToObject(custom, key, k->slots[i].instanceId); }
        }
        put_into(c, "Inventory", inventory);
        put_into(c, "EquippedSlots", slots);
        put_into(c, "CustomEquippedSlots", custom);
        put_into(c, "IsInParty", cJSON_CreateBool(k->inParty));
    }
}

/* A thing on the map: what a container holds (and its lock), a carried object's own state, its paperdoll - into its map entry. */
cJSON *items_save_thing(int uid, cJSON *entry)
{
    for (int p = 0; p < parkedCount; p++)
    {
        if (parked[p].uid != uid) continue;
        Item *it = parked[p].item;
        if (it->category == CAT_CONTAINER)
        {
            cJSON *contents = cJSON_CreateArray();
            for (int i = 0; i < it->count; i++) cJSON_AddItemToArray(contents, items_item_json(it->contents[i]));
            put_into(entry, "OwnContents", contents);
            if (it->locked >= 0) put_into(entry, "IsLockedRuntime", cJSON_CreateBool(it->locked));
        }
        if (it->charges >= 0) put_into(entry, "ChargesRuntime", cJSON_CreateNumber(it->charges));
        if (it->keysSet) { cJSON *keys = cJSON_CreateArray(); for (int i = 0; i < it->keyCount; i++) cJSON_AddItemToArray(keys, cJSON_CreateNumber(it->keys[i])); put_into(entry, "KeyIdsRuntime", keys); } /* a keychain lying on the map */
        if (it->doll) put_into(entry, "DollRuntime", dolls_state_json(it->doll));
        if (it->category == CAT_CONTAINER && it->itemId <= -1000) put_into(entry, "BodyOf", cJSON_CreateNumber(-1000 - it->itemId));
        break;
    }
    return entry;
}

/* An InventoryItemInstance read (a saved paperdoll's items). */
Item *items_read_json(cJSON *j) { return read_item(j); }

/* A new game: every carried thing and everything parked on the map forgotten. */
void items_reset(void)
{
    for (int c = 0; c < carrierCount; c++) { for (int i = 0; i < carriers[c].count; i++) items_free(carriers[c].items[i]); free(carriers[c].items); }
    free(carriers); carriers = NULL; carrierCount = 0;
    for (int p = 0; p < parkedCount; p++) items_free(parked[p].item);
    parkedCount = 0;
}

/* A body's belongings from a save (its OwnContents), as one container. */
Item *items_body_from_json(cJSON *contents)
{
    Item *box = new_item(CAT_CONTAINER, -1, 1);
    cJSON *c;
    cJSON_ArrayForEach(c, contents) add_to(&box->contents, &box->count, &box->cap, read_item(c));
    return box;
}

/* Loose on a character (in no slot): what a body held goes back to its owner. */
void items_add_loose(int characterId, Item *it)
{
    Carrier *k = carrier_of(characterId);
    if (!k) { items_free(it); return; }
    it->instanceId = k->nextInstanceId++;
    add_to(&k->items, &k->count, &k->cap, it);
}

/* Every item a container holds, taken out of it (the shell left empty). */
int items_take_all(Item *container, Item **out, int max)
{
    int n = 0;
    for (int i = 0; i < container->count && n < max; i++) out[n++] = container->contents[i];
    container->count = 0;
    return n;
}

/* A character made in play (Summon): its own carrier. */
void items_add_carrier(cJSON *character)
{
    carriers = (Carrier *)realloc(carriers, sizeof(Carrier) * (size_t)(carrierCount + 1));
    Carrier *k = &carriers[carrierCount++];
    memset(k, 0, sizeof *k);
    k->characterId = num(character, "Id", -1);
    k->strength = 10;
    k->nextInstanceId = 1;
}

/* A spellbook copy's learned spells (Learn Spell; LearnedSpellsRuntime), by name, any case. */
int items_knows_spell(const Item *it, const char *name)
{
    if (!it) return 0;
    for (int i = 0; i < it->learnedCount; i++)
    {
        const char *a = it->learned[i], *b = name;
        while (*a && *b && tolower((unsigned char)*a) == tolower((unsigned char)*b)) { a++; b++; }
        if (!*a && !*b) return 1;
    }
    return 0;
}

void items_learn_spell(Item *it, const char *name)
{
    if (!it || !name || items_knows_spell(it, name)) return;
    it->learned = (char **)realloc(it->learned, sizeof(char *) * (size_t)(it->learnedCount + 1));
    size_t n = strlen(name) + 1;
    it->learned[it->learnedCount] = (char *)malloc(n);
    memcpy(it->learned[it->learnedCount++], name, n);
}

/* A useable object's charges left (-1: not used yet), and spent (used up but kept). */
int items_charges(const Item *it) { return it ? it->charges : -1; }
void items_set_charges(Item *it, int charges) { if (it) it->charges = charges; }
int items_spent(const Item *it) { return it && it->spent; }
void items_set_spent(Item *it, int spent) { if (it) it->spent = spent; }

static void collect_objects(Item *it, Item **out, int *n, int max)
{
    if (*n < max && it->category == CAT_OBJECT) out[(*n)++] = it;
    for (int i = 0; i < it->count; i++) collect_objects(it->contents[i], out, n, max);
}

/* Every object a character carries, anywhere on them (in bags too). */
int items_carried_objects(int characterId, Item **out, int max)
{
    Carrier *k = carrier_of(characterId);
    int n = 0;
    if (k) for (int i = 0; i < k->count; i++) collect_objects(k->items[i], out, &n, max);
    return n;
}

/* A keychain's keys (KeyIdsRuntime). */
int items_keys_set(const Item *it) { return it && it->keysSet; }
void items_mark_keys_set(Item *it) { if (it) it->keysSet = 1; }
int items_key_count(const Item *it) { return it ? it->keyCount : 0; }
int items_has_key(const Item *it, int keyId) { if (!it || keyId < 0) return 0; for (int i = 0; i < it->keyCount; i++) if (it->keys[i] == keyId) return 1; return 0; }
void items_add_key(Item *it, int keyId) { if (it && keyId >= 0 && !items_has_key(it, keyId) && it->keyCount < 32) it->keys[it->keyCount++] = keyId; if (it) it->keysSet = 1; }

/* Whether this is somewhere inside the container (at any depth). */
int items_holds(const Item *container, const Item *it)
{
    if (!container || !it) return 0;
    for (int i = 0; i < container->count; i++) if (container->contents[i] == it || items_holds(container->contents[i], it)) return 1;
    return 0;
}

/* A carried liquid container's liquid: its own once set, else as it starts (fallback). */
int items_liquid(const Item *it, int fallback) { return it && it->liquidSet ? it->liquid : fallback; }
void items_set_liquid(Item *it, int liquid) { if (it) { it->liquid = liquid; it->liquidSet = 1; } }

static void collect_all(Item *it, Item **out, int *n, int max)
{
    if (*n < max) out[(*n)++] = it;
    for (int i = 0; i < it->count; i++) collect_all(it->contents[i], out, n, max);
}

/* Everything a character carries, at any depth, in order (Select Item's list). */
int items_carried_all(int characterId, Item **out, int max)
{
    Carrier *k = carrier_of(characterId);
    int n = 0;
    if (k) for (int i = 0; i < k->count; i++) collect_all(k->items[i], out, &n, max);
    return n;
}
