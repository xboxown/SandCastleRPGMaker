/*
 * What characters carry, as the Windows engine keeps it (InventoryModel,
 * GameTestWindow.Interaction/Weight/Objects): each character's things with
 * the slots they're in (hands, backpack, pouch, worn...), containers with
 * what's inside, and Exult's rules for taking something (Actor::add,
 * drag.cc Check_weight): 2 x Strength for the party, ammo to its slot, a
 * container to its Location slot or a hand, anything else into the backpack
 * or pouch if it fits, else a free hand.
 */
#ifndef SANDCASTLE_ITEMS_H
#define SANDCASTLE_ITEMS_H

enum { CAT_BOOK = 3, CAT_OBJECT = 4, CAT_CONTAINER = 5 };

typedef struct Item Item;

void items_load(void);

/* Anyone in the party (the main character too) carries one - in a slot, a bag, a bag in a bag. */
int items_party_has(int category, int itemId);
/* This character carries one, anywhere on them. */
int items_carries(int characterId, int category, int itemId);

/* A loose thing, not anyone's yet: a fresh one (a container with its Starting Contents). */
Item *items_make(int category, int itemId, int quantity);
void items_free(Item *item);
int items_category(const Item *item);
int items_item_id(const Item *item);
int items_quantity(const Item *item);

/* Actor::add as for a drop on them (weight, ammo slot, container slot, backpack / pouch, hands): 1 = they have it now (the item is theirs), 0 = refused (why in items_refusal). */
int items_give_to(int characterId, Item *item);
const char *items_refusal(void);

/* TakeFromCarrier: this many out of everything they carry (at any depth); 0 when they haven't that many (nothing taken). The things taken go into out (up to max). */
int items_take(int characterId, int category, int itemId, int quantity, Item **out, int max, int *taken);

/* Gifts made from nothing (MakeGifts): a stackable thing as one stack, else one each. */
int items_make_gifts(int category, int itemId, int quantity, Item **out, int max);

/* Whether it can be lifted at all: an object marked not Portable, a container marked Too Heavy (Can Be Picked off). */
int items_can_lift(int category, int itemId);

/* The party: the main character and whoever has joined. */
int items_in_party(int characterId);

/* A container (or anything) put down on the map keeps what it is; picked up again, it comes back as it was. */
void items_park(int uid, Item *item);
Item *items_unpark(int uid, int category, int itemId, int quantity);
void items_repark(int uid, Item *item);

/* How many of it they carry / the party carries (stacks counted), and the party's money in the first currency's units. */
int items_count(int characterId, int category, int itemId);
int items_party_count(int category, int itemId);
#include "../third_party/cJSON.h"
double items_party_money(cJSON *objects, cJSON *currencies);

/* Lights: their state carried with them; lit ones in hands (for the dark); burning down. */
void items_light_state(const Item *it, int *lit, int *spent, double *burnLeft);
void items_set_light_state(Item *it, int lit, int spent, double burnLeft);
int items_hand_lights(int characterId, int *brightness, int *gives);
void items_burn_lights(double minutes);

/* Game conditions: equipped, carried but not worn, how many (category -1: anything). */
int items_equipped(int characterId, int category, int itemId);
int items_carried_not_worn(int characterId, int category, int itemId);
int items_count_any(int characterId, int category, int itemId);
int items_party_count_any(int category, int itemId);

void items_set_in_party(int characterId, int join);
int items_count_in(const Item *container, int category, int itemId);
void items_empty(Item *container);
void items_put_in(Item *container, Item *it);
void items_set_locked(Item *it, int locked);
int items_locked(const Item *it, int byDefault);
int items_carries_key(int characterId, int keyId);
int items_lockpick(int characterId);
int items_equip(int characterId, int category, int itemId, Item **dropped, int *droppedCount);
int items_unequip(int characterId, int category, int itemId, Item **dropped);
int items_take_from(Item *container, int characterId, int category, int itemId, int quantity);

/* Its name, for the log. */
const char *items_name(int category, int itemId);

#endif
