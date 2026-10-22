/*
 * Health, food, poison and ailments for every character, as the Windows
 * engine has them (GameTestWindow.Vitals / Unconscious / Bodies / Verbose /
 * HitFlash, from Exult):
 *  - health is the Hits stat, Strength its maximum; below -(Strength / 3)
 *    is death, at 0 or below knocked out (lying, mending 1 in 6 every half
 *    minute, up again at 1 HP);
 *  - each game hour: 1 HP back (more asleep in a Sleep schedule) unless
 *    poisoned or starving, then 0..N food used; each game minute at food 0:
 *    a chance of losing health;
 *  - poison: first after 5 s, then 0..N HP every 10-20 s till it wears off;
 *    asleep 5-10 s; paralyzed, charmed, cursed 60-120 s; invisible and
 *    protected 60-80 s;
 *  - tile effects (Tile Properties > custom effects): on entering, staying
 *    (ramping if set) or leaving, unless a mitigating object is carried;
 *  - a hit: a red outline for 200 ms (the main character badly hurt: the
 *    screen flashes red); "Ouch!" now and then;
 *  - death: the character leaves a body (its Body pose) holding everything
 *    it carried where it fell; its By Death events. The main character's:
 *    the game is over (its By Death events decide what's said).
 * All of it stands still while the game is paused.
 */
#include "vitals.h"
#include "events.h"
#include "items.h"
#include "conditions.h"
#include "world.h"
#include "screen.h"
#include "schedules.h"
#include "combat.h"
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

enum { POISONED = 1, ASLEEP = 2, PARALYZED = 4, CHARMED = 8, CURSED = 16, INVISIBLE = 32, PROTECTED = 64 };
enum { K_STRENGTH = 1, K_HITS = 6 };
#define MAX_FOOD 31
#define MEND_CHECK_MS 30000

typedef struct Vital
{
    int characterId, food, ailments, dead, unconscious;
    double poisonLeft, poisonNext, sleepLeft, paralyzeLeft, charmLeft, curseLeft, invisibleLeft, protectedLeft, mendCheck, damageCarry;
    int cellX, cellY, hasCell;
    /* tile effects being felt: which, its ticks, its timer, on its tiles now, its recovery */
    struct { cJSON *effect; int ticks, onTile; double timer, recovery; } exposures[8]; int exposureCount;
} Vital;

static Vital *vitals; static int vitalCount;
static long lastMinute = -1;
static int lastHour = -1, gameOver;
static cJSON *statsJson, *tileProps;
static unsigned rng = 31337u;
static int rnd(int n) { rng = rng * 1103515245u + 12345u; return n > 0 ? (int)((rng >> 8) % (unsigned)n) : 0; }

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }

static cJSON *character_json(int characterId)
{
    cJSON *c, *list = get(get(get((cJSON *)world_json(), "Project"), "Characters"), "Characters");
    cJSON_ArrayForEach(c, list) if (num(c, "Id", -2) == characterId) return c;
    return NULL;
}

static cJSON *survival(int characterId) { return get(character_json(characterId), "Survival"); }
static int survival_num(int characterId, const char *k, int fallback) { cJSON *s = survival(characterId); return s ? num(s, k, fallback) : fallback; }
static int can_get_hungry(int characterId) { cJSON *s = survival(characterId); return !s || !cJSON_IsFalse(get(s, "CanGetHungry")); }

/* A stat by its kind: its value, and whether it has one. */
static int stat_of(int characterId, int kind, int *value)
{
    cJSON *s, *values = get(character_json(characterId), "StatValues");
    cJSON_ArrayForEach(s, statsJson)
        if (num(s, "Kind", 0) == kind)
        {
            char key[16];
            snprintf(key, sizeof key, "%d", num(s, "Id", -1));
            const char *v = cJSON_GetStringValue(get(values, key));
            if (!v || !*v) return 0;
            char *end;
            long n = strtol(v, &end, 10);
            if (*end) return 0;
            *value = (int)n;
            return 1;
        }
    return 0;
}

static void set_stat_of(int characterId, int kind, int value)
{
    cJSON *s, *values = get(character_json(characterId), "StatValues");
    if (!values) return;
    char text[16];
    snprintf(text, sizeof text, "%d", value);
    cJSON_ArrayForEach(s, statsJson)
        if (num(s, "Kind", 0) == kind)
        {
            char key[16];
            snprintf(key, sizeof key, "%d", num(s, "Id", -1));
            if (get(values, key)) cJSON_ReplaceItemInObject(values, key, cJSON_CreateString(text));
            else cJSON_AddStringToObject(values, key, text);
        }
}

int vitals_hp(int characterId, int *hp) { return stat_of(characterId, K_HITS, hp); }
int vitals_max_hp(int characterId) { int v = 0; return stat_of(characterId, K_STRENGTH, &v) ? v : 0; }
void vitals_set_hp(int characterId, int hp) { set_stat_of(characterId, K_HITS, hp); }

static Vital *vital_of(int characterId)
{
    for (int i = 0; i < vitalCount; i++) if (vitals[i].characterId == characterId) return &vitals[i];
    return NULL;
}

static int actor_of_character(int characterId) { return world_actor_by_character(characterId); }

/* ---------------- Speaking up (Character Editor > Verbose) ---------------- */

static const char *const DEFAULT_LINES[][9] = {
    { "I could use a little food.", "I am a bit hungry.", "When do we eat?" },
    { "I am very hungry.", "I must eat now.", "I must have food." },
    { "I'm famished!", "I'm starving!", "I need food!" },
    { "Ouch!", "Ooof!", "Argh!", "Eeow!" },
    { "Aiiieeee!", "Mercy!", "Pity me!", "Spare me!", "Let me be!", "I surrender!", "I give up!", "Please, no more!" },
    { "Help!", "Save me!", "Guards!  Guards!", "To my side!", "Defend me!", "Protect me!" },
    { "Do not touch that!", "Stop that, or else!", "Leave that be!" },
    { "Did you hear something?", "What was that noise?" },
    { "The key doesn't fit.", "Maybe it is another key." },
    { "The lockpick broke!", "Blast, it snapped!" },
    { "I do not like this.", "Something is not right here.", "I feel uneasy..." },
};
static const char *KIND_NAMES[] = { "Hungry", "VeryHungry", "Starving", "Hurt", "Fleeing", "CallForHelp", "TheftWarning", "HeardNoise", "WrongKey", "LockpickFailed", "Uneasy" };

/* Bark: one line of its own for the situation, over its head (only the human speak; never the dead or the knocked out). */
int vitals_bark(int characterId, int kind)
{
    cJSON *ch = character_json(characterId);
    Vital *v = vital_of(characterId);
    if (!ch || cJSON_IsFalse(get(ch, "IsHuman")) || (v && (v->dead || v->unconscious)) || kind < 0 || kind > 10) return 0;
    int actor = actor_of_character(characterId);
    if (actor < 0) return 0;
    cJSON *own = get(get(get(ch, "Verbose"), "Lines"), KIND_NAMES[kind]);
    if (!own) { char key[8]; snprintf(key, sizeof key, "%d", kind); own = get(get(get(ch, "Verbose"), "Lines"), key); }
    const char *line = NULL;
    if (cJSON_IsArray(own))
    {
        int n = cJSON_GetArraySize(own);
        if (!n) return 0;
        line = cJSON_GetStringValue(cJSON_GetArrayItem(own, kind == 4 && n > 1 && rnd(4) > 0 ? 0 : rnd(n)));
    }
    else
    {
        int n = 0;
        while (n < 9 && DEFAULT_LINES[kind][n]) n++;
        if (!n) return 0;
        line = DEFAULT_LINES[kind][kind == 4 && n > 1 && rnd(4) > 0 ? 0 : rnd(n)];
    }
    if (!line) return 0;
    world_bark(actor, line, 3000);
    return 1;
}

/* ---------------- Starting ---------------- */

void vitals_load(void)
{
    cJSON *project = get((cJSON *)world_json(), "Project"), *c;
    statsJson = get(get(project, "Stats"), "Stats");
    tileProps = get(get(project, "TileProperties"), "ByTileKey");
    cJSON *list = get(get(project, "Characters"), "Characters");
    free(vitals); vitalCount = 0; gameOver = 0; lastHour = -1;
    vitals = (Vital *)calloc((size_t)cJSON_GetArraySize(list) + 1, sizeof(Vital));
    cJSON_ArrayForEach(c, list)
    {
        Vital *v = &vitals[vitalCount++];
        v->characterId = num(c, "Id", -1);
        int start = survival_num(v->characterId, "StartingFood", 24);
        v->food = start < 0 ? 0 : start > MAX_FOOD ? MAX_FOOD : start;
        /* no health set at all: full health (its Strength), or 10 */
        int hp;
        if (!stat_of(v->characterId, K_HITS, &hp)) { int max = vitals_max_hp(v->characterId); set_stat_of(v->characterId, K_HITS, max > 0 ? max : 10); }
    }
    lastMinute = events_total_minutes();
    lastHour = events_hour();
}

int vitals_dead(int characterId) { Vital *v = vital_of(characterId); return v && v->dead; }
int vitals_unconscious(int characterId) { Vital *v = vital_of(characterId); return v && v->unconscious && !v->dead; }
int vitals_food(int characterId) { Vital *v = vital_of(characterId); return v ? v->food : 0; }
int vitals_game_over(void) { return gameOver; }

/* Can't move or act: dead, knocked out, asleep or paralyzed. */
int vitals_incapacitated(int characterId)
{
    Vital *v = vital_of(characterId);
    return v && (v->dead || v->unconscious || (v->ailments & (ASLEEP | PARALYZED)));
}

int vitals_has(int characterId, int ailment) { Vital *v = vital_of(characterId); return v && (v->ailments & ailment); }

/* ---------------- Being hurt, knocked out, dying ---------------- */

static void stop_acting(int characterId, int asleep)
{
    int actor = actor_of_character(characterId);
    if (actor < 0) return;
    world_walk_cancel(actor);
    if (!world_set_pose(actor, asleep ? "Sleeping" : "Idle", -1)) world_set_pose(actor, "Idle", -1);
}

static void resume_acting(int characterId)
{
    int actor = actor_of_character(characterId);
    if (actor >= 0) world_set_pose(actor, "Idle", -1);
}

/* Actor::die: out of the world, a body holding all it carried where it fell; its By Death events. */
static void die(int characterId, const char *cause)
{
    Vital *v = vital_of(characterId);
    if (!v || v->dead) return;
    char key[32];
    snprintf(key, sizeof key, "killed:%d", world_character_source(characterId)); conditions_record(key); /* Character killed */
    conditions_record("killed:*");
    int actor = actor_of_character(characterId);
    if (actor >= 0) world_stand_up(actor);
    v->dead = 1;
    v->ailments = 0;
    if (actor == 0)
    {
        /* the main character: no built-in message - its own By Death events decide; from then on the player can do nothing */
        if (!world_set_pose(0, "Body", -1)) world_set_pose(0, "Idle", -1);
        gameOver = 1;
        combat_death_music();
        char line[128];
        snprintf(line, sizeof line, "You have died (%s).", cause);
        game_log(line);
        events_fire_character(characterId, 16 /* By Death */);
        return;
    }
    events_fire_character(characterId, 16); /* its own By Death events, while it's still where it fell */
    if (actor < 0) return;
    world_walk_cancel(actor);
    /* the body: a container like it (its Body pose), holding everything it carried */
    Item *belongings = items_strip(characterId);
    int x, y, f;
    world_actor_cell(actor, &x, &y, &f);
    int uid = world_put_body(actor, x, y, f);
    if (uid >= 0 && belongings) items_park(uid, belongings);
    else if (belongings) items_free(belongings);
    world_actor_remove(actor);
    char line[128];
    snprintf(line, sizeof line, "%s has died (%s).", cJSON_GetStringValue(get(character_json(characterId), "Name")), cause);
    game_log(line);
}

/* reduce_health at 0 HP or below and not dying: knocked out - lying, the attackers looking elsewhere. */
static void knock_out(int characterId)
{
    Vital *v = vital_of(characterId);
    if (!v || v->unconscious) return;
    int actor = actor_of_character(characterId);
    if (actor >= 0) { world_stand_up(actor); world_walk_cancel(actor); }
    v->unconscious = 1;
    v->mendCheck = MEND_CHECK_MS;
    if (actor >= 0 && !world_set_pose(actor, "Unconscious", -1) && !world_set_pose(actor, "Sleeping", -1)) world_set_pose(actor, "Idle", -1);
    events_knocked_out(characterId);
}

void vitals_damage(int characterId, int amount, const char *cause)
{
    int hp;
    if (amount <= 0 || vitals_dead(characterId) || !vitals_hp(characterId, &hp)) return;
    int actor = actor_of_character(characterId), max = vitals_max_hp(characterId);
    /* Exult's hit: badly hurt, the main character's screen flashes red; anyone else a red outline */
    if (actor == 0 && max > 0 && (amount >= max / 3 || hp < max / 4)) screen_flash(0xE00000, 0.15);
    else if (actor >= 0) world_hit_outline(actor);
    int half = max / 2;
    if (hp >= half && hp - amount < half && hp - amount > 0 && rnd(2) == 0) vitals_bark(characterId, 3); /* "Ouch!" */
    hp -= amount;
    vitals_set_hp(characterId, hp);
    if (hp < -(max / 3)) die(characterId, cause);
    else if (hp <= 0) knock_out(characterId);
}

void vitals_kill(int characterId, const char *cause) { die(characterId, cause); }

/* ---------------- Ailments ---------------- */

int vitals_inflict(int characterId, int ailment)
{
    Vital *v = vital_of(characterId);
    if (!v || v->dead) return 0;
    cJSON *s = survival(characterId);
    if (ailment != INVISIBLE && ailment != PROTECTED && (num(s, "Immunities", 0) & ailment)) return 0;
    v->ailments |= ailment;
    switch (ailment)
    {
        case POISONED:
        {
            int lo = num(s, "PoisonCureMinSeconds", 60), hi = num(s, "PoisonCureMaxSeconds", 180);
            if (lo < 1) lo = 1;
            if (hi < lo) hi = lo;
            v->poisonLeft = (lo + rnd(hi - lo + 1)) * 1000.0;
            v->poisonNext = 5000;
            break;
        }
        case ASLEEP: v->sleepLeft = 5000 + rnd(5000); break;
        case PARALYZED: v->paralyzeLeft = 60000 + rnd(60000); break;
        case CHARMED: v->charmLeft = 60000 + rnd(60000); break;
        case CURSED: v->curseLeft = 60000 + rnd(60000); break;
        case INVISIBLE: v->invisibleLeft = 60000 + rnd(20000); break;
        case PROTECTED: v->protectedLeft = 60000 + rnd(20000); break;
    }
    if (ailment == ASLEEP || ailment == PARALYZED) stop_acting(characterId, ailment == ASLEEP);
    if (ailment != INVISIBLE && ailment != PROTECTED) events_fire_character(characterId, 15 /* By Ailment Effect */);
    return 1;
}

void vitals_cure(int characterId, int ailment)
{
    Vital *v = vital_of(characterId);
    if (!v || !(v->ailments & ailment)) return;
    v->ailments &= ~ailment;
    if ((ailment == ASLEEP || ailment == PARALYZED) && !vitals_incapacitated(characterId)) resume_acting(characterId);
}

int vitals_ailment_of(const char *name)
{
    static const char *NAMES[] = { "Poisoned", "Asleep", "Paralyzed", "Charmed", "Cursed", "Invisible", "Protected" };
    for (int i = 0; i < 7; i++) if (!strcmp(NAMES[i], name)) return 1 << i;
    return 0;
}

/* Change Food Level. */
void vitals_change_food(int characterId, const char *operation, int amount)
{
    Vital *v = vital_of(characterId);
    if (!v || v->dead || !can_get_hungry(characterId)) return;
    int food = !strcmp(operation, "Set") ? amount : !strcmp(operation, "Subtract") ? v->food - amount : v->food + amount;
    v->food = food < 0 ? 0 : food > MAX_FOOD ? MAX_FOOD : food;
}

/* HealFully: health back to the most, every ailment gone, up again if knocked out. */
void vitals_heal_fully(int characterId)
{
    Vital *v = vital_of(characterId);
    if (!v) return;
    vitals_set_hp(characterId, vitals_max_hp(characterId));
    for (int a = 1; a <= PROTECTED; a <<= 1) vitals_cure(characterId, a);
    if (v->unconscious) { v->unconscious = 0; resume_acting(characterId); }
}

/* ---------------- Each frame ---------------- */

static void mend(Vital *v)
{
    int hp, max = vitals_max_hp(v->characterId);
    int starving = can_get_hungry(v->characterId) && v->food <= survival_num(v->characterId, "HungryAtOrBelow", 9);
    if ((v->ailments & POISONED) || !vitals_hp(v->characterId, &hp) || (starving && hp > 0)) return;
    if (max <= 0 || hp >= max) return;
    int actor = actor_of_character(v->characterId);
    int sleeping = actor > 0 && schedules_sleeping(actor);
    hp += max >= 3 && !starving && sleeping ? 1 + rnd(max / 3) : 1;
    vitals_set_hp(v->characterId, hp > max ? max : hp);
}

static void use_food(Vital *v)
{
    if (!can_get_hungry(v->characterId)) return;
    int most = survival_num(v->characterId, "FoodUsedPerHourMax", 3);
    v->food -= rnd((most > 0 ? most : 0) + 1);
    if (v->food < 0) v->food = 0;
    /* say_hunger_message */
    int hungry = survival_num(v->characterId, "HungryAtOrBelow", 9);
    if (v->food > hungry) return;
    if (v->food <= 0) { if (rnd(4) > 0) vitals_bark(v->characterId, 2); }
    else if (v->food <= 4) { if (rnd(3) > 0) vitals_bark(v->characterId, 1); }
    else if (rnd(2) > 0) vitals_bark(v->characterId, 0);
}

static void hunger_minute(Vital *v)
{
    if (!can_get_hungry(v->characterId) || vitals_incapacitated(v->characterId)) return;
    if (v->food > survival_num(v->characterId, "HungryAtOrBelow", 9)) return;
    if (v->food <= 0 && rnd(100) < survival_num(v->characterId, "StarvingChancePercent", 40))
        vitals_damage(v->characterId, survival_num(v->characterId, "StarvingDamage", 1), "starvation");
}

static void update_ailments(Vital *v, double ms)
{
    if (!v->ailments) return;
    if (v->ailments & POISONED)
    {
        v->poisonLeft -= ms;
        if ((v->poisonNext -= ms) <= 0)
        {
            cJSON *s = survival(v->characterId);
            if (!cJSON_IsFalse(get(s, "PoisonWearsOff")) && v->poisonLeft <= 0) vitals_cure(v->characterId, POISONED);
            else
            {
                int most = num(s, "PoisonDamageMax", 2);
                vitals_damage(v->characterId, rnd((most > 0 ? most : 0) + 1), "poison");
                v->poisonNext = 10000 + rnd(10000);
            }
        }
    }
    int hp = 1;
    vitals_hp(v->characterId, &hp);
    if ((v->ailments & ASLEEP) && (v->sleepLeft -= ms) <= 0 && hp >= 1) vitals_cure(v->characterId, ASLEEP);
    if ((v->ailments & PARALYZED) && (v->paralyzeLeft -= ms) <= 0) vitals_cure(v->characterId, PARALYZED);
    if ((v->ailments & CHARMED) && (v->charmLeft -= ms) <= 0) vitals_cure(v->characterId, CHARMED);
    if ((v->ailments & CURSED) && (v->curseLeft -= ms) <= 0) vitals_cure(v->characterId, CURSED);
    if ((v->ailments & INVISIBLE) && (v->invisibleLeft -= ms) <= 0) vitals_cure(v->characterId, INVISIBLE);
    if ((v->ailments & PROTECTED) && (v->protectedLeft -= ms) <= 0) vitals_cure(v->characterId, PROTECTED);
}

static void update_unconscious(Vital *v, double ms)
{
    if (!v->unconscious || v->dead) return;
    int hp = 1;
    vitals_hp(v->characterId, &hp);
    if (hp <= 0 && (v->mendCheck -= ms) <= 0)
    {
        v->mendCheck = MEND_CHECK_MS;
        if (rnd(6) == 0) mend(v); /* about 1 HP every 3 minutes */
        vitals_hp(v->characterId, &hp);
    }
    if (hp < 1) return;
    v->unconscious = 0;
    if (!vitals_incapacitated(v->characterId)) resume_acting(v->characterId);
}

/* The custom effects of the tiles under a cell (every floor's layer). */
static int effects_at(int x, int y, cJSON **out, int max)
{
    int n = 0;
    for (int f = 1; f <= 9 && n < max; f++)
    {
        char key[200];
        if (!world_tile_key(x, y, f, key, sizeof key)) continue;
        cJSON *e;
        cJSON_ArrayForEach(e, get(get(tileProps, key), "CustomEffects")) if (n < max) out[n++] = e;
    }
    return n;
}

static int carries_any(int characterId, cJSON *ids)
{
    cJSON *id;
    cJSON_ArrayForEach(id, ids) if (cJSON_IsNumber(id) && items_count_any(characterId, 4, id->valueint) > 0) return 1;
    return 0;
}

static void apply_tile_effect(Vital *v, cJSON *effect, int ticks)
{
    if (carries_any(v->characterId, get(effect, "MitigatingObjectIds"))) return;
    double damage = (get(effect, "DamagePerTick") ? get(effect, "DamagePerTick")->valuedouble : 0)
                  + (flag(effect, "RampingDamage") && get(effect, "RampIncreasePerTick") ? get(effect, "RampIncreasePerTick")->valuedouble * ticks : 0);
    if (damage <= 0) return;
    v->damageCarry += damage;
    int whole = (int)v->damageCarry;
    v->damageCarry -= whole;
    const char *name = cJSON_GetStringValue(get(effect, "Name"));
    vitals_damage(v->characterId, whole, name ? name : "a tile");
}

static void update_tile_effects(Vital *v, double ms)
{
    int actor = actor_of_character(v->characterId);
    if (actor < 0 || world_actor_gone(actor)) return;
    int x, y, f;
    world_actor_cell(actor, &x, &y, &f);
    cJSON *here[16], *before[16];
    int n = effects_at(x, y, here, 16);
    int moved = !v->hasCell || v->cellX != x || v->cellY != y;
    if (moved && v->hasCell)
    {
        int m = effects_at(v->cellX, v->cellY, before, 16);
        for (int i = 0; i < m; i++)
        {
            int still = 0;
            for (int k = 0; k < n; k++) if (here[k] == before[i]) still = 1;
            if (still) continue;
            if (num(before[i], "Trigger", 0) == 2 /* OnExit */) apply_tile_effect(v, before[i], 0);
            for (int e = 0; e < v->exposureCount; e++)
                if (v->exposures[e].effect == before[i]) { v->exposures[e].onTile = 0; v->exposures[e].recovery = (get(before[i], "RecoverySeconds") ? get(before[i], "RecoverySeconds")->valuedouble : 0) * 1000; }
        }
    }
    v->cellX = x; v->cellY = y; v->hasCell = 1;
    for (int i = 0; i < n; i++)
    {
        int e = -1;
        for (int k = 0; k < v->exposureCount; k++) if (v->exposures[k].effect == here[i]) e = k;
        if (e < 0 && v->exposureCount < 8) { e = v->exposureCount++; memset(&v->exposures[e], 0, sizeof v->exposures[e]); v->exposures[e].effect = here[i]; }
        if (e < 0) continue;
        int entering = !v->exposures[e].onTile;
        v->exposures[e].onTile = 1;
        int trigger = num(here[i], "Trigger", 0);
        if (entering && trigger == 0 /* OnEnter */) apply_tile_effect(v, here[i], 0);
        if (trigger != 1 /* OnStay */) continue;
        v->exposures[e].timer += ms;
        double interval = (get(here[i], "TickIntervalSeconds") ? get(here[i], "TickIntervalSeconds")->valuedouble : 1) * 1000;
        if (interval < 50) interval = 50;
        while (v->exposures[e].timer >= interval && !v->dead)
        {
            v->exposures[e].timer -= interval;
            apply_tile_effect(v, here[i], v->exposures[e].ticks++);
        }
    }
    for (int k = 0; k < v->exposureCount; k++)
        if (!v->exposures[k].onTile && (v->exposures[k].recovery -= ms) <= 0) { v->exposures[k] = v->exposures[--v->exposureCount]; k--; }
}

void vitals_update(double ms)
{
    if (gameOver) return;
    int hour = events_hour();
    int hourChanged = hour != lastHour;
    lastHour = hour;
    for (int i = 0; i < vitalCount; i++)
    {
        Vital *v = &vitals[i];
        int actor = actor_of_character(v->characterId);
        if (v->dead || actor < 0 || world_actor_gone(actor)) continue;
        if (hourChanged) { mend(v); use_food(v); }
    }
    long minute = events_total_minutes();
    if (minute != lastMinute)
    {
        lastMinute = minute;
        for (int i = 0; i < vitalCount; i++)
        {
            int actor = actor_of_character(vitals[i].characterId);
            if (!vitals[i].dead && actor >= 0 && !world_actor_gone(actor)) hunger_minute(&vitals[i]);
        }
    }
    for (int i = 0; i < vitalCount; i++)
    {
        Vital *v = &vitals[i];
        int actor = actor_of_character(v->characterId);
        if (v->dead || actor < 0 || world_actor_gone(actor)) continue;
        update_unconscious(v, ms);
        update_ailments(v, ms);
        update_tile_effects(v, ms);
    }
}

/* Game Over (the command): from now on the player can do nothing. */
void vitals_set_game_over(void) { gameOver = 1; }

/* ---------------- Saving (SavedVitals) ---------------- */

void vitals_save(cJSON *runtime)
{
    cJSON *all = cJSON_CreateArray();
    for (int i = 0; i < vitalCount; i++)
    {
        Vital *v = &vitals[i];
        cJSON *e = cJSON_CreateObject();
        cJSON_AddNumberToObject(e, "CharacterId", v->characterId); cJSON_AddNumberToObject(e, "Food", v->food); cJSON_AddNumberToObject(e, "Ailments", v->ailments);
        cJSON_AddNumberToObject(e, "PoisonRemainingMs", v->poisonLeft); cJSON_AddNumberToObject(e, "SleepRemainingMs", v->sleepLeft);
        cJSON_AddNumberToObject(e, "ParalyzeRemainingMs", v->paralyzeLeft); cJSON_AddNumberToObject(e, "CharmRemainingMs", v->charmLeft);
        cJSON_AddNumberToObject(e, "CurseRemainingMs", v->curseLeft); cJSON_AddNumberToObject(e, "InvisibleRemainingMs", v->invisibleLeft);
        cJSON_AddNumberToObject(e, "ProtectedRemainingMs", v->protectedLeft);
        cJSON_AddBoolToObject(e, "Dead", v->dead); cJSON_AddBoolToObject(e, "Unconscious", v->unconscious);
        cJSON_AddItemToArray(all, e);
    }
    cJSON_AddItemToObject(runtime, "Vitals", all);
    cJSON_AddBoolToObject(runtime, "GameOver", gameOver);
}

void vitals_restore(cJSON *runtime)
{
    cJSON *e;
    cJSON_ArrayForEach(e, get(runtime, "Vitals"))
    {
        Vital *v = vital_of(num(e, "CharacterId", -2));
        if (!v) continue;
        v->food = num(e, "Food", v->food); v->ailments = num(e, "Ailments", 0);
        v->poisonLeft = get(e, "PoisonRemainingMs") ? get(e, "PoisonRemainingMs")->valuedouble : 0; v->poisonNext = 5000;
        v->sleepLeft = get(e, "SleepRemainingMs") ? get(e, "SleepRemainingMs")->valuedouble : 0;
        v->paralyzeLeft = get(e, "ParalyzeRemainingMs") ? get(e, "ParalyzeRemainingMs")->valuedouble : 0;
        v->charmLeft = get(e, "CharmRemainingMs") ? get(e, "CharmRemainingMs")->valuedouble : 0;
        v->curseLeft = get(e, "CurseRemainingMs") ? get(e, "CurseRemainingMs")->valuedouble : 0;
        v->invisibleLeft = get(e, "InvisibleRemainingMs") ? get(e, "InvisibleRemainingMs")->valuedouble : 0;
        v->protectedLeft = get(e, "ProtectedRemainingMs") ? get(e, "ProtectedRemainingMs")->valuedouble : 0;
        v->dead = flag(e, "Dead"); v->unconscious = flag(e, "Unconscious");
        int actor = world_actor_by_character(v->characterId);
        if (actor >= 0 && v->unconscious && !v->dead && !world_set_pose(actor, "Unconscious", -1)) world_set_pose(actor, "Sleeping", -1);
    }
    gameOver = flag(runtime, "GameOver");
}
