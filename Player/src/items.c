#include "items.h"
#include "events.h"
#include "world.h"
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

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
    it->category = category; it->itemId = itemId; it->quantity = quantity > 0 ? quantity : 1; it->locked = -1;
    return it;
}

void items_free(Item *item)
{
    if (!item) return;
    for (int i = 0; i < item->count; i++) items_free(item->contents[i]);
    free(item->contents);
    free(item);
}

/* An inventory entry from the game (InventoryItemInstance), with what's inside. */
static Item *read_item(cJSON *j)
{
    Item *it = new_item(num(j, "Category", CAT_OBJECT), num(j, "ItemId", -1), num(j, "Quantity", 1));
    it->instanceId = num(j, "InstanceId", 0);
    cJSON *locked = get(j, "IsLockedRuntime"), *c;
    if (cJSON_IsBool(locked)) it->locked = cJSON_IsTrue(locked);
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
    if (was) events_fire_character(k->characterId, TR_UNEQUIP);
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
}

/* The thing on the map with that uid as it was put down - or, never carried, a fresh one (a container with its Starting Contents). */
Item *items_unpark(int uid, int category, int itemId, int quantity)
{
    for (int i = 0; i < parkedCount; i++)
        if (parked[i].uid == uid) { Item *it = parked[i].item; parked[i] = parked[--parkedCount]; it->quantity = quantity; return it; }
    return items_make(category, itemId, quantity);
}

/* Put back where it was found (a pick-up that was refused). */
void items_repark(int uid, Item *item) { items_park(uid, item); }
