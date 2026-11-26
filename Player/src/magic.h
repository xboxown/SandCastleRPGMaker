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
int magic_spell_exists(const char *name); /* any spellbook in the project has it */
int magic_cast_named(int actor, const char *name, int other, void *doll, int byActivator);
/* The spell system in use (Tools > Spell Editor): its style (0 spellbook, 1 fetish, 2 events only), the Cast key's Common
   Event, a Common Event run by name, an ingredient Used (1: it was one - fetish style). */
int magic_system_style(void);
const char *magic_system_on_cast(void);
int magic_run_common_named(const char *name, int actor);
int magic_fetish_use(int characterId, int objectId); /* doll: the paperdoll the event runs from (a spellbook's own) */
int magic_combat_spell(int actor, int target);               /* a mage's turn in a fight: 1 = it cast */
int magic_frozen(int actor);                                 /* Time Stop */
int magic_understands(int languageId);                       /* no language, Translate on, or someone in the party knows it */
const char *magic_language_font(int languageId);             /* the font while not understood, or NULL */
void magic_learn_language(int actor, int languageId, int learn);
void magic_update(double ms);
void magic_reset(void);
void magic_set_mana(int actor, int mana);

/* Ultima 6's Cast (u6.c): the readied books' spells (-1: none readied); the checks and the cost (NULL: cast, else a Words id); the spell delivered. */
int magic_u6_spells(int actor, cJSON **spells, int max);
const char *magic_u6_spend(int actor, cJSON *spell);
void magic_u6_deliver(int caster, cJSON *spell, int who, int thing, int cx, int cy);

/* The open spellbook (Exult's Spellbook_gump): over everything, it takes every click. */
int magic_book_open(void);
void magic_close_book(void);
void magic_draw_book(uint32_t *pixels, uint32_t ms);
int magic_book_click(int x, int y, uint32_t now);

#endif
