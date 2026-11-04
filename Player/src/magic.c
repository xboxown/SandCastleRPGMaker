/*
 * Magic, and the things used up that share its effects, as the Windows
 * engine has them (GameTestWindow.Effects / Spells / Magic / Consumables,
 * from Exult's usecode and spellbook.cc):
 *  - Effects: heal (up to Strength), mana (up to Magic), the ailments and
 *    their cures, protection, invisibility, light (the night lit as by many
 *    lights), damage, nourish, custom effects (event commands); and the
 *    spells' own - charm, fear, dance, clone, reveal invisible; on the thing
 *    clicked (magic unlock and lock, telekinesis, damage); on the spot
 *    (teleport, Mark and Recall, summon, create items, fields, douse and
 *    ignite lights, reveal and destroy traps, time stop, tremor, locate,
 *    resurrect).
 *  - Potions and food: double-click, then click who drinks or eats (Exult's
 *    consumeEdible: "No, thank thee." when full; its barks by food level).
 *    Useable objects: their effects, charges, By Use events.
 *  - Spells (can_do_spell / do_spell): mana, level for the circle, one of
 *    each reagent; the words said, the sound, a target clicked when it needs
 *    one; delivered straight there or as a missile; touching one, or all
 *    within reach (friends, foes or everyone); its animation over each.
 *  - Spellbooks: Exult's Spellbook_gump - two pages, a circle a page, each
 *    spell's icon, name and how many times it can be cast now; a click marks
 *    it, a double-click casts it; only those who can cast magic may read.
 *  - Mages in a fight cast a third of their turns: a heal when badly hurt,
 *    else a harmful (or summoning) spell at their foe.
 */
#include "magic.h"
#include "world.h"
#include "items.h"
#include "events.h"
#include "vitals.h"
#include "combat.h"
#include "gumps.h"
#include "sound.h"
#include "font.h"
#include "screen.h"
#include "lighting.h"
#include "schedules.h"
#include "use.h"
#include "platform.h"
#include "pictures.h"
#include "dolls.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <ctype.h>

enum
{
    E_HEAL, E_MANA, E_CURE_POISON, E_POISON, E_SLEEP, E_AWAKEN, E_PARALYZE, E_CURE_PARALYSIS, E_CURSE, E_REMOVE_CURSE, E_PROTECTION, E_INVISIBILITY,
    E_LIGHT, E_DAMAGE, E_NOURISH, E_CUSTOM, E_TELEPORT, E_MARK, E_RECALL, E_SUMMON, E_CREATE_ITEM, E_CREATE_FIELD, E_UNLOCK, E_LOCK, E_DOUSE, E_IGNITE,
    E_REVEAL_TRAPS, E_DESTROY_TRAPS, E_TELEKINESIS, E_CHARM, E_FEAR, E_DANCE, E_RESURRECT, E_CLONE, E_REVEAL_INVISIBLE, E_TIME_STOP, E_TREMOR, E_LOCATE,
    E_DESTROY_FIELD, E_COMMON_EVENT, E_LEVITATE, E_TRANSLATE
};
enum { A_POISONED = 1, A_ASLEEP = 2, A_PARALYZED = 4, A_CHARMED = 8, A_CURSED = 16, A_INVISIBLE = 32, A_PROTECTED = 64 };
enum { K_STRENGTH = 1, K_MAGIC = 5, K_HITS = 6, K_MANA = 7, K_LEVEL = 8, K_EXP = 9 };
enum { OT_DOOR = 3, OT_LIGHT = 4, OT_TRAP = 9, OT_FOOD = 17, OT_POTION = 18, OT_SPELLBOOK = 20, OT_USEABLE = 21 };
enum { TR_USE = 2 };
#define FOOD_PECKISH 5
#define FOOD_WELLFED 10
#define FOOD_STUFFED 20
#define FOOD_FULL 25
#define COPY_STEP 1000000

static uint32_t rng = 2463534242u;
static int rnd(int n) { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return n > 0 ? (int)(((uint64_t)rng * (uint32_t)n) >> 32) : 0; } /* xorshift: no pattern when several share it in turn */

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static double dbl(cJSON *o, const char *k, double fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valuedouble : fallback; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }
static cJSON *project(void) { return get((cJSON *)world_json(), "Project"); }

static cJSON *object_def(int id)
{
    cJSON *o;
    cJSON_ArrayForEach(o, get(get(project(), "Objects"), "Objects")) if (num(o, "Id", -2) == id) return o;
    return NULL;
}

static cJSON *character_json(int characterId)
{
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Characters"), "Characters")) if (num(c, "Id", -2) == characterId) return c;
    return NULL;
}

static int id_of(int actor) { return actor >= 0 ? world_actor_character(actor) : -1; }
static int dead(int actor) { return actor < 0 || world_actor_gone(actor) || vitals_dead(id_of(actor)); }
static int in_party(int actor) { return actor == 0 || items_in_party(id_of(actor)); }
static const char *name_of(int actor) { return world_actor_name(actor); }

static int stat_of(int actor, int kind, int fallback)
{
    cJSON *s, *values = get(character_json(id_of(actor)), "StatValues");
    cJSON_ArrayForEach(s, get(get(project(), "Stats"), "Stats"))
        if (num(s, "Kind", 0) == kind)
        {
            char key[16];
            snprintf(key, sizeof key, "%d", num(s, "Id", -1));
            const char *v = cJSON_GetStringValue(get(values, key));
            char *end;
            if (!v || !*v) return fallback;
            long n = strtol(v, &end, 10);
            return *end ? fallback : (int)n;
        }
    return fallback;
}

static void set_stat_of(int actor, int kind, int value)
{
    cJSON *s, *values = get(character_json(id_of(actor)), "StatValues");
    if (!values) return;
    char text[16];
    snprintf(text, sizeof text, "%d", value);
    cJSON_ArrayForEach(s, get(get(project(), "Stats"), "Stats"))
        if (num(s, "Kind", 0) == kind)
        {
            char key[16];
            snprintf(key, sizeof key, "%d", num(s, "Id", -1));
            if (get(values, key)) cJSON_ReplaceItemInObject(values, key, cJSON_CreateString(text)); else cJSON_AddStringToObject(values, key, text);
        }
}

static void bark(int actor, const char *text, int ms)
{
    cJSON *c = character_json(id_of(actor));
    if (actor >= 0 && !cJSON_IsFalse(get(c, "IsHuman"))) world_bark(actor, text, ms);
}

static int roll(cJSON *effect)
{
    int a = num(effect, "Min", 1), b = num(effect, "Max", 1), lo = a < b ? a : b, hi = a < b ? b : a;
    return lo + rnd(hi - lo + 1);
}

static const Sprite *link_sprite(cJSON *link) { return flag(link, "IsSet") ? sprite_find(str(link, "SpriteGroup"), str(link, "SpriteName")) : NULL; }

static void cell_of(int actor, int *x, int *y, int *fl) { world_actor_cell(actor, x, y, fl); }

/* ---------------- Timers: marks, summoned helpers, fields, fear, time stop ---------------- */

typedef struct Mark { int actor, x, y, floor; } Mark;
static Mark marks[32]; static int markCount;
typedef struct Timed { int id; double ms; } Timed;
static Timed summoned[32], fields[64], fears[32]; static int summonedCount, fieldCount, fearCount;
static double timeStopMs;
static long lastMinute = -1;

static double translateMs; /* Translate: every language reads plainly a while */

static cJSON *character_of(int actor)
{
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Characters"), "Characters")) if (num(c, "Id", -2) == id_of(actor)) return c;
    return NULL;
}

static int knows_language(int actor, int languageId)
{
    cJSON *k;
    cJSON_ArrayForEach(k, get(character_of(actor), "KnownLanguages")) if (cJSON_IsNumber(k) && k->valueint == languageId) return 1;
    return 0;
}

int magic_understands(int languageId)
{
    if (languageId < 0 || translateMs > 0) return 1;
    for (int a = 0; a < world_actor_count(); a++)
        if ((a == 0 || (in_party(a) && !world_actor_gone(a))) && knows_language(a, languageId)) return 1;
    return 0;
}

const char *magic_language_font(int languageId)
{
    if (magic_understands(languageId)) return NULL;
    cJSON *l;
    cJSON_ArrayForEach(l, get(get(project(), "Settings"), "Languages"))
        if (num(l, "Id", -2) == languageId) { const char *f = cJSON_GetStringValue(get(l, "FontSpriteName")); return f && f[0] ? f : NULL; }
    return NULL;
}

void magic_learn_language(int actor, int languageId, int learn)
{
    cJSON *c = character_of(actor);
    if (!c) return;
    cJSON *known = get(c, "KnownLanguages");
    if (!cJSON_IsArray(known)) { cJSON_DeleteItemFromObject(c, "KnownLanguages"); known = cJSON_AddArrayToObject(c, "KnownLanguages"); }
    for (int i = cJSON_GetArraySize(known) - 1; i >= 0; i--)
        if (cJSON_GetArrayItem(known, i)->valueint == languageId) { if (learn) return; cJSON_DeleteItemFromArray(known, i); }
    if (learn) cJSON_AddItemToArray(known, cJSON_CreateNumber(languageId));
}

int magic_frozen(int actor) { return timeStopMs > 0 && actor > 0 && !in_party(actor); } /* Time Stop: everyone but the party stands still */

static void timed_add(Timed *list, int *count, int max, int id, double ms)
{
    for (int i = 0; i < *count; i++) if (list[i].id == id) { if (ms > list[i].ms) list[i].ms = ms; return; }
    if (*count < max) list[(*count)++] = (Timed){ id, ms };
}

/* ---------------- Effects on someone ---------------- */

static void spot_effect(cJSON *effect, int caster, int cx, int cy, int radius, int who);
static void character_spell(cJSON *effect, int target, int by);
static void summon(cJSON *kind, int caster, int cx, int cy, int count, double seconds);

static int is_spot(int k) { return k == E_TELEPORT || k == E_MARK || k == E_RECALL || k == E_SUMMON || k == E_CREATE_ITEM || k == E_CREATE_FIELD || k == E_DOUSE || k == E_IGNITE || k == E_REVEAL_TRAPS || k == E_DESTROY_TRAPS || k == E_TIME_STOP || k == E_TREMOR || k == E_LOCATE || k == E_DESTROY_FIELD; }
static int is_thing(int k) { return k == E_UNLOCK || k == E_LOCK || k == E_TELEKINESIS; }
static int is_harmful(int k) { return k == E_DAMAGE || k == E_POISON || k == E_SLEEP || k == E_PARALYZE || k == E_CURSE || k == E_CHARM || k == E_FEAR || k == E_DANCE; }

/* ApplyEffects: on whoever it's used on; by: whoever cast it (a kill gives them the experience; spot effects happen where the target is). */
void magic_apply_effects(cJSON *effects, int target, const char *cause, int by)
{
    cJSON *e;
    cJSON_ArrayForEach(e, effects)
    {
        if (dead(target)) return;
        int id = id_of(target), amount = roll(e), kind = num(e, "Kind", 0);
        if (is_spot(kind)) { int x, y, f; cell_of(target, &x, &y, &f); spot_effect(e, by >= 0 ? by : target, x, y, 0, target); continue; }
        switch (kind)
        {
            case E_HEAL:
            {
                int hp, max = vitals_max_hp(id);
                if (!vitals_hp(id, &hp)) break;
                vitals_set_hp(id, max > 0 && hp + amount > max ? max : hp + amount);
                if (target == 0) { char line[64]; vitals_hp(id, &hp); snprintf(line, sizeof line, "You feel better (%d HP).", hp); game_log(line); }
                break;
            }
            case E_MANA:
            {
                int mana = stat_of(target, K_MANA, 0), max = stat_of(target, K_MAGIC, 0x7FFFFFFF);
                set_stat_of(target, K_MANA, mana + amount > max ? max : mana + amount);
                break;
            }
            case E_CURE_POISON: vitals_cure(id, A_POISONED); break;
            case E_POISON: vitals_inflict(id, A_POISONED); break;
            case E_SLEEP: vitals_inflict(id, A_ASLEEP); break;
            case E_AWAKEN: vitals_cure(id, A_ASLEEP); break;
            case E_PARALYZE: vitals_inflict(id, A_PARALYZED); break;
            case E_CURE_PARALYSIS: vitals_cure(id, A_PARALYZED); break;
            case E_CURSE: vitals_inflict(id, A_CURSED); break;
            case E_REMOVE_CURSE: vitals_cure(id, A_CURSED); break;
            case E_PROTECTION: vitals_inflict(id, A_PROTECTED); break;
            case E_INVISIBILITY: vitals_inflict(id, A_INVISIBLE); break;
            case E_LIGHT: lighting_special(amount); break;
            case E_DAMAGE:
                if (amount > 0 && vitals_wounds_it(id, -1, by >= 0 ? cause : NULL)) vitals_damage_allowed(id, amount, cause); /* cause: the spell's name */
                if (by >= 0 && by != target && vitals_dead(id)) { int worth = stat_of(target, K_EXP, 0); if (worth > 0) combat_gain_experience(by, worth); } /* a spell's kill */
                break;
            case E_CHARM: case E_FEAR: case E_DANCE: case E_REVEAL_INVISIBLE: case E_CLONE: character_spell(e, target, by); break;
            case E_CUSTOM:
            {
                cJSON *custom;
                cJSON_ArrayForEach(custom, get(get(project(), "Objects"), "CustomEffects"))
                    if (num(custom, "Id", -2) == num(e, "CustomEffectId", -1)) events_run_commands(get(custom, "Commands"), target > 0 ? target : -1);
                break;
            }
            case E_NOURISH: vitals_change_food(id, "Add", amount); break;
            case E_TRANSLATE: { double ms = (cJSON_IsNumber(get(e, "Seconds")) ? get(e, "Seconds")->valuedouble : 60) * 1000; if (ms > translateMs) translateMs = ms; break; }
            case E_LEVITATE: world_levitate(target, cJSON_IsNumber(get(e, "Seconds")) ? get(e, "Seconds")->valuedouble : 60); break;
            case E_COMMON_EVENT: /* its Common Event runs on them ("Whoever set it off") */
            {
                cJSON *common;
                cJSON_ArrayForEach(common, get(get(project(), "Settings"), "CommonEvents"))
                    if (num(common, "Id", -2) == num(e, "CommonEventId", -1)) events_run_commands(get(common, "Commands"), target > 0 ? target : -1);
                break;
            }
        }
    }
}

/* Damage effects on a placed object (a fireball scroll): struck for that much. */
static void effects_on_thing(cJSON *effects, int uid)
{
    cJSON *e;
    cJSON_ArrayForEach(e, effects) if (num(e, "Kind", 0) == E_DAMAGE && world_thing_there(uid)) combat_strike_object(uid, roll(e));
}

static void character_spell(cJSON *effect, int target, int by)
{
    double seconds = dbl(effect, "Seconds", 60);
    if (seconds < 1) seconds = 1;
    int id = id_of(target);
    switch (num(effect, "Kind", 0))
    {
        case E_CHARM: vitals_inflict(id, A_CHARMED); break; /* Exult's charm: fights for the other side a while */
        case E_FEAR:
            if (target <= 0 || by < 0) break;
            combat_flee(target, by);
            timed_add(fears, &fearCount, 32, target, seconds * 1000);
            bark(target, "Aaagh!", 2000);
            break;
        case E_DANCE:
            if (target <= 0) break;
            schedules_change_behavior(target, "Dance");
            schedules_until(target, events_total_minutes() + (long)(seconds / 5 > 1 ? floor(seconds / 5 + 0.5) : 1)); /* a game minute is 5 seconds */
            break;
        case E_REVEAL_INVISIBLE: vitals_cure(id, A_INVISIBLE); break;
        case E_CLONE: { int x, y, f; cell_of(target, &x, &y, &f); summon(character_json(id), by >= 0 ? by : target, x, y, 1, seconds); break; }
    }
}

/* ---------------- On the thing clicked ---------------- */

static void thing_spell(cJSON *effect, int uid)
{
    int c, i, q, x, y, n;
    world_thing_info(uid, &c, &i, &q, &x, &y, &n);
    switch (num(effect, "Kind", 0))
    {
        case E_UNLOCK:
            if (world_thing_type(uid) == OT_DOOR)
            {
                int open, locked;
                world_door_state(uid, &open, &locked);
                if (locked) { world_door_set_locked(uid, 0); sound_play(num(get(object_def(i), "Door"), "UnlockSoundResourceId", -1)); game_log("It is unlocked."); }
            }
            else if (c == CAT_CONTAINER)
            {
                Item *box = items_parked(uid, c, i, q);
                if (items_is_locked(box)) { items_set_locked(box, 0); game_log("It is unlocked."); }
            }
            break;
        case E_LOCK:
            if (world_thing_type(uid) == OT_DOOR)
            {
                int open, locked;
                world_door_state(uid, &open, &locked);
                if (!open) { world_door_set_locked(uid, 1); sound_play(num(get(object_def(i), "Door"), "LockSoundResourceId", -1)); game_log("It is locked."); }
            }
            else if (c == CAT_CONTAINER) { items_set_locked(items_parked(uid, c, i, q), 1); game_log("It is locked."); }
            break;
        case E_TELEKINESIS: if (c == CAT_OBJECT) use_thing(uid); break; /* used from where the caster stands */
    }
}

/* ---------------- On the spot ---------------- */

/* Teleport and Recall: the caster (the party with the main character) there at once. */
static void move_caster(int caster, int x, int y, int floorOf)
{
    if (x < 0 || y < 0 || x >= world_map_w() || y >= world_map_h()) return;
    world_actor_place(caster, x, y, floorOf);
    if (caster != 0) return;
    for (int a = 1; a < world_actor_count(); a++) if (!world_actor_gone(a) && in_party(a)) world_actor_place(a, x, y, floorOf); /* the party comes along */
}

static void summon(cJSON *kind, int caster, int cx, int cy, int count, double seconds)
{
    if (!kind) return;
    int x0, y0, fl;
    cell_of(caster, &x0, &y0, &fl);
    cJSON *list = get(get(project(), "Characters"), "Characters");
    int base = num(kind, "Id", -1) % COPY_STEP;
    for (int n = 0; n < count; n++)
    {
        /* a free spot round the cell */
        int sx = -1, sy = -1;
        for (int ring = 1; ring <= 3 && sx < 0; ring++)
            for (int dy = -ring; dy <= ring && sx < 0; dy++)
                for (int dx = -ring; dx <= ring && sx < 0; dx++)
                {
                    int x = cx + dx, y = cy + dy, taken = 0;
                    if (x < 0 || y < 0 || x >= world_map_w() || y >= world_map_h()) continue;
                    for (int a = 0; a < world_actor_count() && !taken; a++) { int ax, ay, af; if (world_actor_gone(a)) continue; world_actor_cell(a, &ax, &ay, &af); taken = ax == x && ay == y; }
                    if (!taken && world_can_stand(0, x, y, fl)) { sx = x; sy = y; }
                }
        if (sx < 0) break;
        /* its own copy: same kind, the caster's side, no schedule */
        int copies = 1;
        cJSON *c;
        cJSON_ArrayForEach(c, list) if (num(c, "Id", -1) % COPY_STEP == base && num(c, "Id", -1) / COPY_STEP >= copies) copies = num(c, "Id", -1) / COPY_STEP + 1;
        cJSON *copy = cJSON_Duplicate(kind, 1);
        cJSON_ReplaceItemInObject(copy, "Id", cJSON_CreateNumber(base + COPY_STEP * copies));
        cJSON_ReplaceItemInObject(copy, "FactionId", cJSON_CreateNumber(num(character_json(id_of(caster)), "FactionId", -1)));
        if (get(copy, "IsInParty")) cJSON_ReplaceItemInObject(copy, "IsInParty", cJSON_CreateFalse());
        if (get(copy, "IsTestPlayer")) cJSON_ReplaceItemInObject(copy, "IsTestPlayer", cJSON_CreateFalse());
        if (get(copy, "Schedules")) cJSON_ReplaceItemInObject(copy, "Schedules", cJSON_CreateArray());
        if (get(copy, "Inventory")) cJSON_ReplaceItemInObject(copy, "Inventory", cJSON_CreateArray());
        cJSON_AddItemToArray(list, copy);
        items_add_carrier(copy);
        vitals_add(num(copy, "Id", -1));
        int actor = world_add_actor(copy, sx, sy, fl);
        if (actor > 0) timed_add(summoned, &summonedCount, 32, actor, seconds * 1000);
    }
}

static void spot_effect(cJSON *effect, int caster, int cx, int cy, int radius, int who)
{
    (void)who;
    int cells = (int)ceil(8.0 * (radius > 1 ? radius : 1) / world_tile_w()), near = radius > 0 ? cells : 0;
    double seconds = dbl(effect, "Seconds", 60);
    if (seconds < 1) seconds = 1;
    int amount = roll(effect), x0, y0, fl;
    cell_of(caster, &x0, &y0, &fl);
    char line[160];
    switch (num(effect, "Kind", 0))
    {
        case E_TELEPORT: move_caster(caster, cx, cy, fl); break;
        case E_MARK:
        {
            int m = -1;
            for (int i = 0; i < markCount; i++) if (marks[i].actor == caster) m = i;
            if (m < 0 && markCount < 32) m = markCount++;
            if (m >= 0) marks[m] = (Mark){ caster, x0, y0, fl };
            game_log("This place is marked.");
            break;
        }
        case E_RECALL:
        {
            int found = 0;
            for (int i = 0; i < markCount; i++) if (marks[i].actor == caster) { move_caster(caster, marks[i].x, marks[i].y, marks[i].floor); found = 1; }
            if (!found) game_log("Nothing has been marked.");
            break;
        }
        case E_SUMMON: summon(character_json(num(effect, "ThingId", -1)), caster, cx, cy, amount > 1 ? amount : 1, seconds); break;
        case E_CREATE_ITEM:
        {
            if (!object_def(num(effect, "ThingId", -1))) break;
            Item *gifts[100];
            int n = items_make_gifts(CAT_OBJECT, num(effect, "ThingId", -1), amount > 1 ? amount : 1, gifts, 100);
            for (int i = 0; i < n; i++)
                if (!items_give_to(id_of(caster), gifts[i]))
                {
                    int uid = world_thing_put(CAT_OBJECT, items_item_id(gifts[i]), items_quantity(gifts[i]), x0, y0, fl);
                    if (uid >= 0) items_park(uid, gifts[i]); else items_free(gifts[i]);
                }
            break;
        }
        case E_CREATE_FIELD:
            if (!object_def(num(effect, "ThingId", -1))) break;
            for (int y = cy - near; y <= cy + near; y++)
                for (int x = cx - near; x <= cx + near; x++)
                {
                    if (x < 0 || y < 0 || x >= world_map_w() || y >= world_map_h()) continue;
                    int uid = world_thing_put(CAT_OBJECT, num(effect, "ThingId", -1), 1, x, y, fl);
                    if (uid >= 0) timed_add(fields, &fieldCount, 64, uid, seconds * 1000);
                }
            break;
        case E_DOUSE: case E_IGNITE:
        {
            int lit = num(effect, "Kind", 0) == E_IGNITE, around[512];
            int n = world_things_near(cx * world_tile_w() + world_tile_w() / 2.0, cy * world_tile_h() + world_tile_h() / 2.0, (cells > 0 ? cells : 1) * world_tile_w(), around, 512);
            for (int i = 0; i < n; i++)
                if (world_thing_type(around[i]) == OT_LIGHT && !world_light_spent(around[i]) && world_light_lit(around[i]) != lit)
                {
                    world_light_set(around[i], lit);
                    if (lit) world_light_lit_now(around[i]);
                }
            break;
        }
        case E_REVEAL_TRAPS: case E_DESTROY_TRAPS:
        {
            int reach = cells > (int)ceil(64.0 / world_tile_w()) ? cells : (int)ceil(64.0 / world_tile_w()), around[512];
            int n = world_things_near(cx * world_tile_w() + world_tile_w() / 2.0, cy * world_tile_h() + world_tile_h() / 2.0, reach * world_tile_w(), around, 512);
            for (int i = 0; i < n; i++)
                if (world_thing_type(around[i]) == OT_TRAP) { if (num(effect, "Kind", 0) == E_DESTROY_TRAPS) world_thing_remove(around[i]); }
            break;
        }
        case E_DESTROY_FIELD:
        {
            /* Dispel Field: what Create Field made within reach goes - and, with an object chosen, any of it there */
            int around[512], thing = num(effect, "ThingId", -1);
            int n = world_things_near(cx * world_tile_w() + world_tile_w() / 2.0, cy * world_tile_h() + world_tile_h() / 2.0, (near > 0 ? near : 1) * world_tile_w(), around, 512);
            for (int i = 0; i < n; i++)
            {
                int made = -1;
                for (int k = 0; k < fieldCount; k++) if (fields[k].id == around[i]) made = k;
                int category, itemId, q, tx, ty, inst;
                world_thing_info(around[i], &category, &itemId, &q, &tx, &ty, &inst);
                if (made < 0 && !(thing >= 0 && category == CAT_OBJECT && itemId == thing)) continue;
                if (made >= 0) fields[made] = fields[--fieldCount];
                world_thing_remove(around[i]);
            }
            break;
        }
        case E_TIME_STOP: if (seconds * 1000 > timeStopMs) timeStopMs = seconds * 1000; game_log("Time stands still."); break;
        case E_TREMOR: screen_shake(amount < 2 ? 2 : amount > 20 ? 20 : amount, 2); break;
        case E_LOCATE: snprintf(line, sizeof line, "%s stands at %d, %d.", name_of(caster), x0, y0); game_log(line); break;
    }
}

/* ---------------- Spells: on their way, and where they land ---------------- */

typedef struct Cast { int used, caster, who, thing, cx, cy; cJSON *spell; } Cast;
static Cast casts[16];

static int spell_touches(cJSON *spell, int caster, int who)
{
    int affects = num(spell, "Affects", 0);
    return affects == 1 ? !combat_friends(who, caster) : affects == 2 ? combat_friends(who, caster) : 1;
}

static void animation_at(cJSON *spell, double px, double py)
{
    const Sprite *s = link_sprite(get(spell, "Animation"));
    if (s) world_effect_once(s, (int)floor(px / world_tile_w()), (int)floor(py / world_tile_h()));
}

static void spell_arrives(int caster, cJSON *spell, int who, int thing, int cx, int cy)
{
    int touched[256], n = 0, radius = num(spell, "RadiusTiles", 0);
    if (radius > 0)
    {
        double px, py;
        int fl, tx, ty;
        if (who >= 0 && !dead(who)) { world_actor_pixel(who, &px, &py); world_actor_cell(who, &tx, &ty, &fl); }
        else { px = cx * world_tile_w() + world_tile_w() / 2.0; py = cy * world_tile_h() + world_tile_h() / 2.0; world_actor_cell(caster, &tx, &ty, &fl); }
        for (int a = 0; a < world_actor_count() && n < 256; a++)
        {
            int ax, ay, af;
            double qx, qy;
            if (dead(a)) continue;
            world_actor_cell(a, &ax, &ay, &af);
            world_actor_pixel(a, &qx, &qy);
            if (af != fl || fmax(fabs(qx - px), fabs(qy - py)) / 8 > radius) continue;
            if (spell_touches(spell, caster, a)) touched[n++] = a;
        }
    }
    else if (who >= 0 && !dead(who)) touched[n++] = who;
    /* the effects on characters */
    cJSON *onCharacters = cJSON_CreateArray(), *e;
    int harmful = 0;
    cJSON_ArrayForEach(e, get(spell, "Effects"))
    {
        int k = num(e, "Kind", 0);
        if (!is_spot(k) && !is_thing(k)) { cJSON_AddItemReferenceToArray(onCharacters, e); harmful |= is_harmful(k); }
    }
    for (int i = 0; i < n; i++)
    {
        double px, py;
        world_actor_pixel(touched[i], &px, &py);
        animation_at(spell, px, py);
        magic_apply_effects(onCharacters, touched[i], str(spell, "Name"), caster);
        if (touched[i] != caster && !combat_friends(touched[i], caster) && harmful && !dead(touched[i])) combat_fight_back(touched[i], caster); /* a harmful spell is an attack */
    }
    cJSON_Delete(onCharacters);
    if (!n) animation_at(spell, cx * world_tile_w() + world_tile_w() / 2.0, cy * world_tile_h() + world_tile_h() / 2.0);
    /* Resurrect: the body clicked, or every body within its reach */
    cJSON_ArrayForEach(e, get(spell, "Effects"))
        if (num(e, "Kind", 0) == E_RESURRECT)
            for (int a = 1; a < world_actor_count(); a++)
            {
                int id = id_of(a), body = world_body_of(id);
                if (!body || !vitals_dead(id)) continue;
                int c, i2, q, bx, by, bn;
                world_thing_info(body, &c, &i2, &q, &bx, &by, &bn);
                int reach = (int)ceil(8.0 * radius / world_tile_w());
                if (body == thing || (radius > 0 && abs(bx - cx) <= reach && abs(by - cy) <= reach)) vitals_resurrect(id);
            }
    /* the thing clicked */
    if (thing > 0 && world_thing_there(thing))
    {
        cJSON_ArrayForEach(e, get(spell, "Effects")) if (is_thing(num(e, "Kind", 0))) thing_spell(e, thing);
        if (world_thing_there(thing) && world_thing_object_id(thing) >= 0) effects_on_thing(get(spell, "Effects"), thing);
    }
    /* the spot: once for the spell */
    cJSON_ArrayForEach(e, get(spell, "Effects")) if (is_spot(num(e, "Kind", 0))) spot_effect(e, caster, cx, cy, radius, who);
    char line[160];
    if (n == 1) snprintf(line, sizeof line, "%s on %s.", str(spell, "Name"), name_of(touched[0]));
    else if (n > 1) snprintf(line, sizeof line, "%s touches %d.", str(spell, "Name"), n);
    else if (caster == 0) snprintf(line, sizeof line, "You cast %s.", str(spell, "Name"));
    else snprintf(line, sizeof line, "%s casts %s.", name_of(caster), str(spell, "Name"));
    game_log(line);
}

static void missile_landed(void *context, int victim, double x, double y)
{
    Cast *c = (Cast *)context;
    if (!c->used) return;
    c->used = 0;
    int lx = (int)floor(x / world_tile_w()), ly = (int)floor(y / world_tile_h()), reached = abs(lx - c->cx) <= 1 && abs(ly - c->cy) <= 1;
    int vx = lx, vy = ly, fl;
    if (victim >= 0) world_actor_cell(victim, &vx, &vy, &fl);
    spell_arrives(c->caster, c->spell, victim, victim < 0 && reached ? c->thing : 0, vx, vy);
}

/* DeliverSpell: as a missile flying from the caster (a Projectile object), or straight there. */
static void deliver(int caster, cJSON *spell, int who, int thing, int cx, int cy)
{
    int ox, oy, of;
    cell_of(caster, &ox, &oy, &of);
    int elsewhere = who != caster || thing > 0 || cx != ox || cy != oy;
    cJSON *missile = object_def(num(spell, "MissileObjectId", -1));
    if (flag(spell, "NeedsTarget") && elsewhere && missile && num(missile, "Type", -1) == 34)
    {
        Cast *c = NULL;
        for (int i = 0; i < 16 && !c; i++) if (!casts[i].used) c = &casts[i];
        double tx = cx * world_tile_w() + world_tile_w() / 2.0, ty = cy * world_tile_h() + world_tile_h() / 2.0;
        if (who >= 0 && !dead(who)) world_actor_pixel(who, &tx, &ty);
        if (c)
        {
            *c = (Cast){ 1, caster, who, thing, cx, cy, spell };
            if (combat_launch(sprite_find(str(missile, "SpriteGroup"), str(missile, "SpriteName")), caster, who, tx, ty, missile_landed, c)) return;
            c->used = 0;
        }
    }
    spell_arrives(caster, spell, who, thing, cx, cy);
}

/* ---------------- Casting (can_do_spell, do_spell) ---------------- */

static int circle_level(int circle)
{
    if (circle <= 8) return circle;
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Objects"), "CustomCircles")) if (num(c, "Number", -1) == circle) return num(c, "LevelNeeded", circle);
    return circle;
}

static const char *circle_name(int circle, char *buf, size_t size)
{
    static const char *names[] = { "Linear", "First Circle", "Second Circle", "Third Circle", "Fourth Circle", "Fifth Circle", "Sixth Circle", "Seventh Circle", "Eighth Circle" };
    if (circle >= 0 && circle <= 8) return names[circle];
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Objects"), "CustomCircles")) if (num(c, "Number", -1) == circle) { snprintf(buf, size, "%s", str(c, "Name")); return buf; }
    snprintf(buf, size, "Circle %d", circle);
    return buf;
}

static int mana_cost(cJSON *spell) { int m = num(spell, "ManaCost", -1); return m >= 0 ? m : (num(spell, "Circle", 1) > 0 ? num(spell, "Circle", 1) : 0); }

/* Its reagents: the list with quantities, else the old one id each. Returns how many (ids and quantities into the arrays). */
static int reagents_of(cJSON *spell, int *ids, int *quantities, int max)
{
    int n = 0;
    cJSON *r;
    if (cJSON_GetArraySize(get(spell, "Reagents")) > 0)
    {
        cJSON_ArrayForEach(r, get(spell, "Reagents")) if (num(r, "ObjectId", -1) >= 0 && n < max) { ids[n] = num(r, "ObjectId", -1); quantities[n] = num(r, "Quantity", 1) > 1 ? num(r, "Quantity", 1) : 1; n++; }
        return n;
    }
    cJSON_ArrayForEach(r, get(spell, "ReagentObjectIds"))
    {
        int dup = 0;
        for (int i = 0; i < n; i++) dup |= ids[i] == r->valueint;
        if (!dup && n < max && cJSON_IsNumber(r)) { ids[n] = r->valueint; quantities[n] = 1; n++; }
    }
    return n;
}

/* can_do_spell: NULL if it can be cast now, else why not. */
static const char *why_cannot(int caster, cJSON *spell)
{
    static char why[160];
    int id = id_of(caster);
    if (vitals_incapacitated(id)) return "The caster can't act.";
    if (vitals_has(id, A_CURSED)) return "The caster is cursed.";
    int mana = stat_of(caster, K_MANA, 0), cost = mana_cost(spell);
    if (mana < cost) { snprintf(why, sizeof why, "Not enough mana (%d of %d).", mana, cost); return why; }
    int level = stat_of(caster, K_LEVEL, 0x7FFFFFFF), needed = circle_level(num(spell, "Circle", 1));
    if (level < needed) { snprintf(why, sizeof why, "Needs level %d (has %d).", needed, level); return why; }
    int ids[16], qs[16], n = reagents_of(spell, ids, qs, 16);
    for (int i = 0; i < n; i++)
    {
        int have = items_count_any(id, CAT_OBJECT, ids[i]);
        if (have < qs[i]) { snprintf(why, sizeof why, "Needs %d %s (has %d).", qs[i], items_name(CAT_OBJECT, ids[i]), have); return why; }
    }
    return NULL;
}

static int casts_left(int caster, cJSON *spell)
{
    int cost = mana_cost(spell), left = cost > 0 ? stat_of(caster, K_MANA, 0) / cost : 99;
    int ids[16], qs[16], n = reagents_of(spell, ids, qs, 16);
    for (int i = 0; i < n; i++) { int c = items_count_any(id_of(caster), CAT_OBJECT, ids[i]) / qs[i]; if (c < left) left = c; }
    return left < 0 ? 0 : left > 99 ? 99 : left;
}

typedef struct Pending { int caster; cJSON *spell; } Pending;
static Pending pending;

static void target_chosen(void *context, const GumpsPick *pick)
{
    Pending *p = (Pending *)context;
    int who = pick->actor, thing = pick->uid, cx = pick->cellX, cy = pick->cellY, fl;
    if (who >= 0) world_actor_cell(who, &cx, &cy, &fl);
    else if (thing > 0) { int c, i, q, n; world_thing_info(thing, &c, &i, &q, &cx, &cy, &n); }
    deliver(p->caster, p->spell, who, thing, cx, cy);
}

/* do_spell: the mana spent and the reagents used up; its words and sound; then on whom (castOn given, itself, or clicked). */
void magic_cast(int caster, cJSON *spell, int castOn)
{
    const char *why = why_cannot(caster, spell);
    if (why) { game_log(why); return; }
    int id = id_of(caster);
    set_stat_of(caster, K_MANA, stat_of(caster, K_MANA, 0) - mana_cost(spell));
    int ids[16], qs[16], n = reagents_of(spell, ids, qs, 16);
    for (int i = 0; i < n; i++)
    {
        Item *used[100]; int got = 0;
        if (items_take(id, CAT_OBJECT, ids[i], qs[i], used, 100, &got)) for (int k = 0; k < got; k++) items_free(used[k]);
    }
    char words[128];
    snprintf(words, sizeof words, "%s", str(spell, "Words"));
    char *w = words;
    while (isspace((unsigned char)*w)) w++;
    if (*w) bark(caster, w, 2500); /* begin_casting: the words of power */
    world_sound_from_actor(num(spell, "CastSoundResourceId", -1), caster);
    int x, y, fl;
    cell_of(caster, &x, &y, &fl);
    if (castOn >= 0 && flag(spell, "NeedsTarget")) { int tx, ty, tf; world_actor_cell(castOn, &tx, &ty, &tf); deliver(caster, spell, castOn, 0, tx, ty); return; }
    if (!flag(spell, "NeedsTarget") || !in_party(caster)) { deliver(caster, spell, caster, 0, x, y); return; }
    pending = (Pending){ caster, spell };
    char prompt[96];
    snprintf(prompt, sizeof prompt, "Cast %s on whom?", str(spell, "Name"));
    gumps_choose_target(prompt, target_chosen, &pending);
}

/* ---------------- Spellbooks ---------------- */

/* A spellbook's spells this copy knows: all of them, or (its type starting empty) those learned. */
static int knows(cJSON *book, Item *copy, cJSON *spell)
{
    if (!cJSON_IsFalse(get(get(book, "Spellbook"), "StartsWithAllSpells"))) return 1;
    return copy && items_knows_spell(copy, str(spell, "Name"));
}

/* Not everyone can read one: Can cast magic, and on the book's own list when it has one. */
static int refused(cJSON *book, int reader)
{
    cJSON *c = character_json(id_of(reader)), *allowed = get(get(book, "Spellbook"), "AllowedCharacterIds"), *a;
    char line[160];
    if (!flag(c, "CanCastMagic")) { snprintf(line, sizeof line, "%s can't make sense of the runes in \"%s\".", name_of(reader), str(book, "Name")); game_log(line); bark(reader, "I cannot read this.", 2500); return 1; }
    if (cJSON_GetArraySize(allowed))
    {
        int ok = 0;
        cJSON_ArrayForEach(a, allowed) ok |= cJSON_IsNumber(a) && a->valueint == world_character_source(id_of(reader));
        if (!ok) { snprintf(line, sizeof line, "\"%s\" isn't %s's to read.", str(book, "Name"), name_of(reader)); game_log(line); bark(reader, "I cannot read this.", 2500); return 1; }
    }
    return 0;
}

/* The book open now (Exult's Spellbook_gump): its pages - a circle each, eight spells a page - the spread shown, its reader. */
typedef struct Book { int open, reader, spread, pageCount; cJSON *def; Item *copy; cJSON *pageSpells[64][8]; int pageCircle[64], pageCount8[64]; char marked[64]; uint32_t lastClick; int lastSpell; } Book;
static Book book;
static char bookmarks[64][2][64]; static int bookmarkCount; /* each copy's bookmark: by its address */

static const char *bookmark_of(const void *copy)
{
    char key[32];
    snprintf(key, sizeof key, "%p", copy);
    for (int i = 0; i < bookmarkCount; i++) if (!strcmp(bookmarks[i][0], key)) return bookmarks[i][1];
    return "";
}

static void set_bookmark(const void *copy, const char *spell)
{
    char key[32];
    snprintf(key, sizeof key, "%p", copy);
    for (int i = 0; i < bookmarkCount; i++) if (!strcmp(bookmarks[i][0], key)) { snprintf(bookmarks[i][1], 64, "%s", spell); return; }
    if (bookmarkCount < 64) { snprintf(bookmarks[bookmarkCount][0], 64, "%s", key); snprintf(bookmarks[bookmarkCount++][1], 64, "%s", spell); }
}

static void open_book(cJSON *def, Item *copy, int reader)
{
    if (refused(def, reader)) return;
    memset(&book, 0, sizeof book);
    book.open = 1; book.def = def; book.copy = copy; book.reader = reader; book.lastSpell = -1;
    /* its known spells by circle */
    cJSON *spells[256];
    int n = 0;
    cJSON *s;
    cJSON_ArrayForEach(s, get(get(def, "Spellbook"), "Spells")) if (n < 256 && knows(def, copy, s)) spells[n++] = s;
    for (int i = 1; i < n; i++) for (int j = i; j > 0 && num(spells[j], "Circle", 1) < num(spells[j - 1], "Circle", 1); j--) { cJSON *t = spells[j]; spells[j] = spells[j - 1]; spells[j - 1] = t; }
    for (int i = 0; i < n && book.pageCount < 64;)
    {
        int circle = num(spells[i], "Circle", 1), page = book.pageCount++;
        book.pageCircle[page] = circle;
        while (i < n && num(spells[i], "Circle", 1) == circle && book.pageCount8[page] < 8) book.pageSpells[page][book.pageCount8[page]++] = spells[i++];
    }
    if (!book.pageCount) { book.pageCount = 1; book.pageCircle[0] = -1; }
    /* it opens at the bookmark */
    snprintf(book.marked, sizeof book.marked, "%s", bookmark_of(copy ? (void *)copy : (void *)def));
    for (int p = 0; p < book.pageCount; p++) for (int k = 0; k < book.pageCount8[p]; k++) if (!strcmp(str(book.pageSpells[p][k], "Name"), book.marked)) book.spread = p / 2;
    cJSON *sounds = get(get(project(), "Settings"), "Sounds");
    if (!cJSON_IsFalse(get(sounds, "PaperdollOpenSoundEnabled"))) sound_play(num(sounds, "PaperdollOpenSoundId", -1)); /* a spellbook is a paperdoll too */
}

int magic_book_open(void) { return book.open; }
void magic_close_book(void) { book.open = 0; }

static void book_box(int *x, int *y, int *w, int *h)
{
    const Picture *bg = sprite_picture_at(link_sprite(get(get(book.def, "Spellbook"), "OpenBookSprite")), 0);
    *w = bg ? bg->width : 296; *h = bg ? bg->height : 184;
    if (*w > SCREEN_W) *w = SCREEN_W;
    if (*h > SCREEN_H) *h = SCREEN_H;
    *x = (SCREEN_W - *w) / 2; *y = (SCREEN_H - *h) / 2;
}

static void fill(uint32_t *p, int x, int y, int w, int h, uint32_t c)
{
    frame_fill(p, x, y, w, h, c);
}

static void cell_rect(int side, int i, int *cx, int *cy, int *cw, int *ch)
{
    int bx, by, bw, bh;
    book_box(&bx, &by, &bw, &bh);
    int pageLeft = bx + (side == 0 ? bw * 7 / 100 : bw * 53 / 100), pageTop = by + bh * 8 / 100, pageWidth = bw * 40 / 100, pageHeight = bh * 80 / 100;
    *cw = pageWidth / 2; *ch = (pageHeight - 14) / 4;
    *cx = pageLeft + (i / 4) * *cw; *cy = pageTop + 14 + (i % 4) * *ch;
}

void magic_draw_book(uint32_t *pixels, uint32_t ms)
{
    if (!book.open) return;
    int bx, by, bw, bh;
    book_box(&bx, &by, &bw, &bh);
    cJSON *sb = get(book.def, "Spellbook");
    const Picture *bg = sprite_picture_at(link_sprite(get(sb, "OpenBookSprite")), ms);
    const uint32_t ink = 0xFF3B2410u, faint = 0xFF8B5A2Bu;
    if (bg) world_blit(pixels, bg, bx, by, bw, bh);
    else
    {
        /* a plain book: a leather cover, two parchment pages and the spine */
        fill(pixels, bx, by, bw, bh, 0xFF2E1808u); fill(pixels, bx + 2, by + 2, bw - 4, bh - 4, 0xFF5A3418u);
        fill(pixels, bx + 8, by + 8, bw / 2 - 9, bh - 16, 0xFFF1E4C2u); fill(pixels, bx + bw / 2 + 1, by + 8, bw / 2 - 9, bh - 16, 0xFFF1E4C2u);
        fill(pixels, bx + bw / 2 - 1, by + 8, 2, bh - 16, 0xFF8A6A3Cu);
    }
    const Font *f = font_get("InGameFont");
    for (int side = 0; side < 2; side++)
    {
        int page = book.spread * 2 + side;
        if (page >= book.pageCount) continue;
        int circle = book.pageCircle[page], continued = page > 0 && book.pageCircle[page - 1] == circle;
        char title[64], buf[48];
        if (circle < 0) snprintf(title, sizeof title, "(no spells)");
        else snprintf(title, sizeof title, "%s%s", circle_name(circle, buf, sizeof buf), continued ? " (cont.)" : "");
        int pageLeft = bx + (side == 0 ? bw * 7 / 100 : bw * 53 / 100), pageWidth = bw * 40 / 100;
        font_draw(f, pixels, pageLeft + (pageWidth - font_text_width(f, title)) / 2, by + bh * 8 / 100, title, ink);
        for (int i = 0; i < book.pageCount8[page]; i++)
        {
            cJSON *spell = book.pageSpells[page][i];
            int cx, cy, cw, ch;
            cell_rect(side, i, &cx, &cy, &cw, &ch);
            const char *why = why_cannot(book.reader, spell);
            const Picture *icon = sprite_picture_at(link_sprite(get(spell, "Icon")), ms);
            int is = ch - 4 < 24 ? ch - 4 : 24, tx = cx + is + 4; /* the icon, its name and casts beside it */
            if (icon) world_blit(pixels, icon, cx, cy + 1, is, is);
            else { fill(pixels, cx, cy + 1, is, 1, ink); fill(pixels, cx, cy + is, is, 1, ink); fill(pixels, cx, cy + 1, 1, is, ink); fill(pixels, cx + is - 1, cy + 1, 1, is, ink); }
            char name[48];
            snprintf(name, sizeof name, "%s", str(spell, "Name"));
            int room = cx + (i < 4 && i + 4 >= book.pageCount8[page] ? 2 * cw : cw) - tx - 2; /* the cell to its right empty: the name may run into it */
            while (strlen(name) > 1 && font_text_width(f, name) > room) name[strlen(name) - 1] = 0;
            font_draw(f, pixels, tx, cy + 1, name, why ? faint : ink);
            char count[16];
            if (why) snprintf(count, sizeof count, "-"); else snprintf(count, sizeof count, "x%d", casts_left(book.reader, spell));
            font_draw(f, pixels, tx, cy + 1 + font_line_height(f), count, faint);
            if (!strcmp(book.marked, str(spell, "Name")))
            {
                const Picture *mark = sprite_picture_at(link_sprite(get(sb, "BookmarkSprite")), ms);
                int ms2 = ch - 4 < 24 ? ch - 4 : 24;
                if (mark) world_blit(pixels, mark, cx + ms2 - 6, cy - 4, mark->width, mark->height); else fill(pixels, cx + ms2 - 6, cy - 4, 3, 10, 0xFF8B0000u);
            }
        }
    }
    /* who is reading, and their mana */
    char reader[96];
    snprintf(reader, sizeof reader, "%s: mana %d of %d, level %d", name_of(book.reader), stat_of(book.reader, K_MANA, 0), stat_of(book.reader, K_MAGIC, 0), stat_of(book.reader, K_LEVEL, 0));
    font_draw(f, pixels, bx + (bw - font_text_width(f, reader)) / 2, by + bh * 89 / 100, reader, faint);
    if (book.spread > 0) font_draw(f, pixels, bx + bw * 7 / 100, by + bh * 84 / 100, "<", ink);
    if (book.spread * 2 + 2 < book.pageCount) font_draw(f, pixels, bx + bw * 89 / 100, by + bh * 84 / 100, ">", ink);
    font_draw(f, pixels, bx + bw * 92 / 100, by + bh * 5 / 100, "x", ink);
}

/* A click on the open book: a spell marked (a second click in time casts it, closing the book), the corners turn the pages, x closes. 1 = taken (it takes every click). */
int magic_book_click(int x, int y, uint32_t now)
{
    if (!book.open) return 0;
    int bx, by, bw, bh;
    book_box(&bx, &by, &bw, &bh);
    if (x >= bx + bw * 92 / 100 - 2 && x < bx + bw * 92 / 100 + 8 && y >= by + bh * 5 / 100 - 2 && y < by + bh * 5 / 100 + 10) { book.open = 0; return 1; }
    int cornerY = by + bh * 84 / 100;
    if (y >= cornerY - 2 && y < cornerY + 10)
    {
        if (book.spread > 0 && x >= bx + bw * 7 / 100 - 3 && x < bx + bw * 7 / 100 + 9) { book.spread--; sound_play(num(get(book.def, "Spellbook"), "PageTurnSoundResourceId", -1)); return 1; }
        if (book.spread * 2 + 2 < book.pageCount && x >= bx + bw * 89 / 100 - 3 && x < bx + bw * 89 / 100 + 9) { book.spread++; sound_play(num(get(book.def, "Spellbook"), "PageTurnSoundResourceId", -1)); return 1; }
    }
    for (int side = 0; side < 2; side++)
    {
        int page = book.spread * 2 + side;
        if (page >= book.pageCount) continue;
        for (int i = 0; i < book.pageCount8[page]; i++)
        {
            int cx, cy, cw, ch;
            cell_rect(side, i, &cx, &cy, &cw, &ch);
            if (x < cx || y < cy || x >= cx + cw || y >= cy + ch) continue;
            cJSON *spell = book.pageSpells[page][i];
            int index = page * 8 + i, twice = book.lastSpell == index && now - book.lastClick < 500;
            book.lastSpell = index; book.lastClick = now;
            snprintf(book.marked, sizeof book.marked, "%s", str(spell, "Name"));
            set_bookmark(book.copy ? (void *)book.copy : (void *)book.def, book.marked); /* a click: the bookmark */
            if (!twice) return 1;
            const char *why = why_cannot(book.reader, spell);
            if (why) { game_log(why); return 1; }
            book.open = 0; /* Exult: casting closes the book */
            magic_cast(book.reader, spell, -1);
            return 1;
        }
    }
    return 1;
}

/* ---------------- Things used up: potions, food, useable objects, spellbooks ---------------- */

/* A thing being used: placed on the map (uid) or carried (item). */
typedef struct Used { cJSON *def; int uid; Item *item; int holder; } Used;
static Used using;
static int bookReader = -1; /* who opened a spellbook last (its paperdoll's "whoever set it off") */

static int still_there(const Used *u) { return u->uid > 0 ? world_thing_there(u->uid) : items_alive(u->item); }
static int user_of(const Used *u) { int a = u->holder >= 0 ? world_actor_by_character(u->holder) : -1; return a >= 0 && !dead(a) ? a : 0; }

static int fire_use(const Used *u) { return u->uid > 0 ? events_object_event(u->uid, TR_USE, -1) : events_fire_item(CAT_OBJECT, num(u->def, "Id", -1), TR_USE); }

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

/* One used up, leaving something in its place if set (an empty bottle): a single one becomes it where it is; one of a stack leaves it at the user's feet. */
static void replace_with_leftover(const Used *u, int leftover)
{
    int single = (u->uid > 0 ? world_thing_quantity(u->uid) : items_quantity(u->item)) <= 1;
    if (leftover >= 0 && single && object_def(leftover))
    {
        if (u->uid > 0)
        {
            int c, i, q, x, y, n, fl = world_thing_floor(u->uid);
            world_thing_info(u->uid, &c, &i, &q, &x, &y, &n);
            items_free(items_unpark(u->uid, c, i, q));
            world_thing_remove(u->uid);
            world_thing_put(CAT_OBJECT, leftover, 1, x, y, fl);
        }
        else items_become(u->item, leftover);
        return;
    }
    int x, y, fl;
    if (u->uid > 0) { int c, i, q, n; world_thing_info(u->uid, &c, &i, &q, &x, &y, &n); fl = world_thing_floor(u->uid); }
    else world_actor_cell(0, &x, &y, &fl);
    use_up_one(u);
    if (leftover >= 0 && object_def(leftover)) world_thing_put(CAT_OBJECT, leftover, 1, x, y, fl);
}

/* consumeEdible: only a party member awake, alive and not paralyzed eats; full ("No, thank thee."), else its barks by food before and after. */
static void eat(const Used *u, int eater)
{
    int id = id_of(eater);
    cJSON *food = get(u->def, "Food");
    if (!in_party(eater) || dead(eater) || vitals_has(id, A_ASLEEP) || vitals_has(id, A_PARALYZED)) return;
    if (flag(food, "IsLiquid") && flag(food, "Undrinkable")) { bark(eater, "No, thank thee.", 2500); return; }
    int level = vitals_food(id), nutrition = num(food, "Nutrition", 0), after = level + nutrition;
    if (level >= FOOD_FULL) { bark(eater, "No, thank thee.", 2500); return; }
    if (u->uid > 0) combat_owned_theft(u->uid); /* stealItem: eaten in sight of its owner's side */
    sound_play(num(food, "EatSoundResourceId", -1));
    use_up_one(u);
    int r = rnd(10);
    const char *line = "";
    if (level >= FOOD_STUFFED) { if (r > 4) line = "I'll soon be plump."; }
    else if (level >= FOOD_PECKISH) line = after >= FOOD_FULL && r < 3 ? "Belch" : "Ahh, very tasty.";
    else if (after >= FOOD_STUFFED) line = r > 5 ? "That hit the spot!" : "Burp";
    else if (after >= FOOD_WELLFED) line = "Ah yes, much better.";
    else if (after >= FOOD_PECKISH) line = r > 5 || eater == 0 ? "I am still hungry." : "May I have some more?";
    else line = r > 5 ? "More!" : "I must have more!";
    if (line[0]) bark(eater, line, 2500);
    vitals_change_food(id, "Set", after);
    char cause[96];
    snprintf(cause, sizeof cause, "\"%s\"", str(u->def, "Name"));
    magic_apply_effects(get(food, "Effects"), eater, cause, -1);
}

static void food_target(void *context, const GumpsPick *pick)
{
    Used *u = (Used *)context;
    if (!still_there(u) || pick->actor < 0) return;
    eat(u, pick->actor);
}

static void potion_target(void *context, const GumpsPick *pick)
{
    Used *u = (Used *)context;
    if (!still_there(u) || pick->actor < 0 || dead(pick->actor)) return;
    cJSON *potion = get(u->def, "Potion");
    if (u->uid > 0) combat_owned_theft(u->uid);
    sound_play(num(potion, "DrinkSoundResourceId", -1));
    char cause[96], line[160];
    snprintf(cause, sizeof cause, "\"%s\"", str(u->def, "Name"));
    snprintf(line, sizeof line, pick->actor == 0 ? "You drink %s." : "%s drinks.", pick->actor == 0 ? cause : name_of(pick->actor));
    game_log(line);
    cJSON *effects = cJSON_Duplicate(get(potion, "Effects"), 1);
    replace_with_leftover(u, num(potion, "EmptyObjectId", -1));
    magic_apply_effects(effects, pick->actor, cause, -1);
    cJSON_Delete(effects);
}

static int charges_of(const Used *u, cJSON *settings)
{
    Item *it = u->uid > 0 ? items_parked(u->uid, CAT_OBJECT, num(u->def, "Id", -1), 1) : u->item;
    int left = items_charges(it);
    return left < 0 ? num(settings, "Charges", 0) : left;
}

static void finish_use(const Used *u)
{
    cJSON *settings = get(u->def, "Useable");
    sound_play(num(settings, "UseSoundResourceId", -1));
    int fired = fire_use(u);
    if (!fired && !cJSON_GetArraySize(get(settings, "Effects"))) { char line[96]; snprintf(line, sizeof line, "\"%s\" - nothing happens.", str(u->def, "Name")); game_log(line); }
    if (num(settings, "Charges", 0) <= 0) return;
    int left = charges_of(u, settings) - 1;
    Item *it = u->uid > 0 ? items_parked(u->uid, CAT_OBJECT, num(u->def, "Id", -1), 1) : u->item;
    items_set_charges(it, left);
    if (left > 0) return;
    if (!cJSON_IsFalse(get(settings, "RemovedWhenUsedUp"))) use_up_one(u);
    else if (u->uid > 0) world_thing_break_glass(u->uid, link_sprite(get(settings, "UsedUpSprite"))); /* spent, its Used Up look */
    else items_set_spent(u->item, 1);
}

static void useable_target(void *context, const GumpsPick *pick)
{
    Used *u = (Used *)context;
    if (!still_there(u)) return;
    cJSON *settings = get(u->def, "Useable");
    char cause[96];
    snprintf(cause, sizeof cause, "\"%s\"", str(u->def, "Name"));
    if (pick->actor >= 0) magic_apply_effects(get(settings, "Effects"), pick->actor, cause, -1);
    else if (pick->uid > 0 && world_thing_object_id(pick->uid) >= 0) effects_on_thing(get(settings, "Effects"), pick->uid);
    else return;
    finish_use(u);
}

static int use(Used u)
{
    using = u;
    char prompt[96];
    switch (num(u.def, "Type", -1))
    {
        case OT_FOOD:
            if (fire_use(&u)) return 1;
            snprintf(prompt, sizeof prompt, "Who eats \"%s\"?", str(u.def, "Name"));
            gumps_choose_target(prompt, food_target, &using);
            return 1;
        case OT_POTION:
            if (fire_use(&u)) return 1;
            snprintf(prompt, sizeof prompt, "Who drinks \"%s\"?", str(u.def, "Name"));
            gumps_choose_target(prompt, potion_target, &using);
            return 1;
        case OT_SPELLBOOK:
            bookReader = user_of(&u);
            if (fire_use(&u)) return 1;
            open_book(u.def, u.uid > 0 ? items_parked(u.uid, CAT_OBJECT, num(u.def, "Id", -1), 1) : u.item, user_of(&u));
            return 1;
        case OT_USEABLE:
        {
            cJSON *settings = get(u.def, "Useable");
            if (num(settings, "Charges", 0) > 0 && (charges_of(&u, settings) <= 0 || (u.uid > 0 ? world_thing_spent(u.uid) : items_spent(u.item))))
            {
                char line[96];
                snprintf(line, sizeof line, "\"%s\" - nothing happens.", str(u.def, "Name"));
                game_log(line);
                return 1;
            }
            if (flag(settings, "AsksForTarget") && cJSON_GetArraySize(get(settings, "Effects")))
            {
                snprintf(prompt, sizeof prompt, "Use \"%s\" on what?", str(u.def, "Name"));
                gumps_choose_target(prompt, useable_target, &using);
                return 1;
            }
            char cause[96];
            snprintf(cause, sizeof cause, "\"%s\"", str(u.def, "Name"));
            magic_apply_effects(get(settings, "Effects"), user_of(&u), cause, -1);
            finish_use(&u);
            return 1;
        }
    }
    return 0;
}

int magic_use_map(int uid)
{
    int objectId = world_thing_object_id(uid);
    cJSON *def = object_def(objectId);
    return def ? use((Used){ def, uid, NULL, -1 }) : 0;
}

int magic_use_carried(Item *it)
{
    if (items_category(it) != CAT_OBJECT) return 0;
    cJSON *def = object_def(items_item_id(it));
    return def ? use((Used){ def, 0, it, items_carrier_of(it) }) : 0;
}

/* ---------------- Learn Spell, Cast Spell ---------------- */

/* The spellbooks a character carries: each its item and its definition. */
static int spellbooks_of(int characterId, Item **items, cJSON **defs, int max)
{
    Item *all[256];
    int n = items_carried_objects(characterId, all, 256), m = 0;
    for (int i = 0; i < n && m < max; i++)
    {
        cJSON *d = object_def(items_item_id(all[i]));
        if (num(d, "Type", -1) == OT_SPELLBOOK) { items[m] = all[i]; defs[m++] = d; }
    }
    return m;
}

static cJSON *spell_named(cJSON *book, const char *name)
{
    cJSON *s;
    cJSON_ArrayForEach(s, get(get(book, "Spellbook"), "Spells"))
    {
        const char *n = str(s, "Name");
        size_t k = 0;
        while (n[k] && name[k] && tolower((unsigned char)n[k]) == tolower((unsigned char)name[k])) k++;
        if (!n[k] && !name[k]) return s;
    }
    return NULL;
}

int magic_learn_spell(int actor, const char *name)
{
    Item *items[16]; cJSON *defs[16];
    int n = spellbooks_of(id_of(actor), items, defs, 16);
    char line[160];
    for (int i = 0; i < n; i++)
    {
        cJSON *spell = spell_named(defs[i], name);
        if (!spell) continue;
        items_learn_spell(items[i], str(spell, "Name"));
        snprintf(line, sizeof line, "%s learns %s.", name_of(actor), str(spell, "Name"));
        game_log(line);
        return 1;
    }
    snprintf(line, sizeof line, "Learn Spell: %s carries no spellbook with the spell \"%s\".", name_of(actor), name);
    game_log(line);
    return 0;
}

int magic_cast_named(int actor, const char *name, int other, void *doll, int byActivator)
{
    Item *items[16]; cJSON *defs[16];
    int n = spellbooks_of(id_of(actor), items, defs, 16);
    char line[160];
    for (int i = 0; i < n; i++)
    {
        cJSON *spell = spell_named(defs[i], name);
        if (!spell || !knows(defs[i], items[i], spell)) continue;
        const char *why = why_cannot(actor, spell);
        if (why) { game_log(why); return 0; }
        magic_cast(actor, spell, other >= 0 ? other : flag(spell, "NeedsTarget") ? actor : -1);
        return 1;
    }
    /* From a spellbook's own paperdoll (a book designed in the PaperDoll Editor): the book being read, even lying on a table */
    int uid;
    Item *copy = doll ? items_find_doll(doll, &uid) : NULL;
    cJSON *def = copy ? object_def(items_item_id(copy)) : NULL, *spell = num(def, "Type", -1) == OT_SPELLBOOK ? spell_named(def, name) : NULL;
    if (spell && knows(def, copy, spell))
    {
        if (byActivator && bookReader >= 0 && !dead(bookReader)) actor = bookReader;
        const char *why = why_cannot(actor, spell);
        if (why) { game_log(why); return 0; }
        dolls_close_command("This paperdoll", (DollState *)doll, -1); /* Exult: casting closes the book */
        magic_cast(actor, spell, other);
        return 1;
    }
    snprintf(line, sizeof line, "Cast Spell: %s has no spellbook that knows \"%s\".", name_of(actor), name);
    game_log(line);
    return 0;
}

/* ---------------- Mages in a fight ---------------- */

/* A character who can cast magic, with a spellbook it may read, casts a third of its turns: a heal when below half its health, else a harmful (or summoning) spell at its foe in sight within 16 tiles. */
int magic_combat_spell(int actor, int target)
{
    cJSON *me = character_json(id_of(actor));
    if (!flag(me, "CanCastMagic") || rnd(3) != 0) return 0;
    Item *items[16]; cJSON *defs[16];
    int books = spellbooks_of(id_of(actor), items, defs, 16);
    cJSON *known[128];
    int n = 0;
    for (int b = 0; b < books; b++)
    {
        cJSON *allowed = get(get(defs[b], "Spellbook"), "AllowedCharacterIds"), *a, *s;
        if (cJSON_GetArraySize(allowed)) { int ok = 0; cJSON_ArrayForEach(a, allowed) ok |= cJSON_IsNumber(a) && a->valueint == world_character_source(id_of(actor)); if (!ok) continue; }
        cJSON_ArrayForEach(s, get(get(defs[b], "Spellbook"), "Spells")) if (n < 128 && knows(defs[b], items[b], s) && !why_cannot(actor, s)) known[n++] = s;
    }
    if (!n) return 0;
    int hp, max = vitals_max_hp(id_of(actor));
    if (max > 0 && vitals_hp(id_of(actor), &hp) && hp * 2 < max)
        for (int i = 0; i < n; i++)
        {
            cJSON *e;
            cJSON_ArrayForEach(e, get(known[i], "Effects"))
                if (num(e, "Kind", 0) == E_HEAL) { world_set_pose(actor, "Attacking Melee", -1); magic_cast(actor, known[i], flag(known[i], "NeedsTarget") ? actor : -1); return 1; }
        }
    if (combat_tiles_between(actor, target) > 16 || !combat_line_clear(actor, target)) return 0;
    cJSON *attacks[128];
    int m = 0;
    for (int i = 0; i < n; i++)
    {
        cJSON *e;
        int harm = 0, summons = 0;
        cJSON_ArrayForEach(e, get(known[i], "Effects")) { harm |= is_harmful(num(e, "Kind", 0)); summons |= num(e, "Kind", 0) == E_SUMMON; }
        if ((flag(known[i], "NeedsTarget") && harm) || summons) attacks[m++] = known[i];
    }
    if (!m) return 0;
    cJSON *spell = attacks[rnd(m)];
    world_set_pose(actor, "Attacking Melee", -1);
    magic_cast(actor, spell, flag(spell, "NeedsTarget") ? target : -1);
    return 1;
}

/* ---------------- Each frame ---------------- */

void magic_update(double ms)
{
    if (timeStopMs > 0) { timeStopMs -= ms; if (timeStopMs < 0) timeStopMs = 0; }
    world_levitation_update(ms);
    if (translateMs > 0) { translateMs -= ms; if (translateMs < 0) translateMs = 0; }
    long minute = events_total_minutes();
    if (lastMinute < 0) lastMinute = minute;
    if (minute > lastMinute) { lighting_minutes_passed((double)(minute - lastMinute)); lastMinute = minute; }
    for (int i = 0; i < summonedCount; i++)
        if ((summoned[i].ms -= ms) <= 0 || dead(summoned[i].id)) { world_actor_remove(summoned[i].id); summoned[i--] = summoned[--summonedCount]; } /* the summoned leave */
    for (int i = 0; i < fieldCount; i++)
        if ((fields[i].ms -= ms) <= 0) { if (world_thing_there(fields[i].id)) world_thing_remove(fields[i].id); fields[i--] = fields[--fieldCount]; } /* fields fade */
    for (int i = 0; i < fearCount; i++)
        if ((fears[i].ms -= ms) <= 0) { combat_calm(fears[i].id); fears[i--] = fears[--fearCount]; } /* fear wears off */
}

void magic_reset(void)
{
    markCount = summonedCount = fieldCount = fearCount = 0;
    timeStopMs = 0; translateMs = 0; lastMinute = -1;
    book.open = 0; bookmarkCount = 0; bookReader = -1;
    memset(casts, 0, sizeof casts);
}

/* Mana set (resting brings it back). */
void magic_set_mana(int actor, int mana) { set_stat_of(actor, K_MANA, mana); }
