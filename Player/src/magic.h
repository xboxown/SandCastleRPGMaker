/* Magic and things used up: effects, potions, food, useable objects, spells, spellbooks (GameTestWindow.Effects / Spells / Magic / Consumables). */
#ifndef SANDCASTLE_MAGIC_H
#define SANDCASTLE_MAGIC_H
#include <stdint.h>
#include "items.h"
#include "../third_party/cJSON.h"

void magic_apply_effects(cJSON *effects, int target, const char *cause, int by);
void magic_cast(int caster, cJSON *spell, int castOn);      /* castOn -1: itself, or a target clicked */
int magic_use_map(int uid);                                  /* a potion, food, useable object or spellbook double-clicked: 1 = handled */
int magic_use_carried(Item *it);
int magic_learn_spell(int actor, const char *name);
int magic_cast_named(int actor, const char *name, int other, void *doll, int byActivator); /* doll: the paperdoll the event runs from (a spellbook's own) */
int magic_combat_spell(int actor, int target);               /* a mage's turn in a fight: 1 = it cast */
int magic_frozen(int actor);                                 /* Time Stop */
void magic_update(double ms);
void magic_reset(void);

/* The open spellbook (Exult's Spellbook_gump): over everything, it takes every click. */
int magic_book_open(void);
void magic_close_book(void);
void magic_draw_book(uint32_t *pixels, uint32_t ms);
int magic_book_click(int x, int y, uint32_t now);

#endif
