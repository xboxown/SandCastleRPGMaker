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
#include <math.h>
#include "pictures.h"
#include "sound.h"
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
    int cold, heat; /* how cold and how hot it is, 0-63 (Exult's temperature) */
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
    { "Mmmm!", "Delicious.", "*munch munch*", "Tasty!" },
    { "Where is my food?", "I am famished - bring me something!", "Food, please!", "Is anyone serving here?", "More food!" },
    { "Hey! That was my food!", "Who took my supper?", "Thief! My food!" },
    { "What wouldst thou like?", "Hungry, art thou?", "May I bring thee something?", "What shall it be?", "Thou lookest hungry." },
    { "Here thou art.", "Enjoy thy meal." },
    { "I cannot get there.", "Something is in the way." },
    { "Not in here!", "I need open sky for that." },
    { "It is chilly here.", "Brrr!", "It is cold!" },
    { "It is very cold!", "I am freezing!" },
    { "I cannot feel my fingers!", "I have frostbite!", "This cold is killing me!" },
    { "I am frozen!", "I... cannot... move..." },
    { "I am warming up.", "That is better." },
    { "It is warm here.", "Phew!", "It is hot!" },
    { "It is very hot!", "I am sweating!" },
    { "I feel faint!", "This heat is killing me!" },
    { "I am burning up!", "I... cannot... breathe..." },
    { "I am cooling down.", "That is better." },
};
static const char *KIND_NAMES[] = { "Hungry", "VeryHungry", "Starving", "Hurt", "Fleeing", "CallForHelp", "TheftWarning", "HeardNoise", "WrongKey", "LockpickFailed", "Uneasy", "Munching", "WantsFood", "FoodStolen", "WaiterAsks", "WaiterServes", "CantSit", "OutdoorsOnly", "FeelingCold", "VeryCold", "Frostbite", "Frozen", "WarmingUp", "FeelingHot", "VeryHot", "Heatstroke", "Scorched", "CoolingDown" };

/* Bark: one line of its own for the situation, over its head (only the human speak; never the dead or the knocked out). */
int vitals_bark(int characterId, int kind)
{
    cJSON *ch = character_json(characterId);
    Vital *v = vital_of(characterId);
    if (!ch || cJSON_IsFalse(get(ch, "IsHuman")) || (v && (v->dead || v->unconscious)) || kind < 0 || kind > 27) return 0;
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
static void knock_out(int characterId);

static void die(int characterId, const char *cause)
{
    Vital *v = vital_of(characterId);
    if (!v || v->dead) return;
    if (flag(character_json(characterId), "Immortal")) { knock_out(characterId); return; }
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
    Item *belongings = items_death_drops(characterId, items_strip(characterId), rnd); /* not human: what may be found in it */
    int x, y, f;
    world_actor_cell(actor, &x, &y, &f);
    if (flag(get(character_json(characterId), "Body"), "NoBody"))
    {
        /* Exult's has_no_body: what it carried falls on the ground, each on the nearest free spot within 5 tiles */
        int radius = (5 * 8 + world_tile_w() / 2) / world_tile_w(), xs[256], ys[256], n = 0;
        if (radius < 1) radius = 1;
        for (int ring = 0; ring <= radius; ring++)
        {
            int first = n;
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                    if ((abs(dx) == ring || abs(dy) == ring) && n < 256 && world_can_stand(actor, x + dx, y + dy, f)) { xs[n] = x + dx; ys[n] = y + dy; n++; }
            for (int i = n - 1; i > first; i--) { int j = first + rnd(i - first + 1), tx = xs[i], ty = ys[i]; xs[i] = xs[j]; ys[i] = ys[j]; xs[j] = tx; ys[j] = ty; }
        }
        if (belongings) items_spill_on(belongings, xs, ys, n, x, y, f);
        world_actor_remove(actor);
        return;
    }
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

/* A weapon or spell it needs is doing this damage (or an event is). */
static int specialWound;

int vitals_immortal(int characterId) { return flag(character_json(characterId), "Immortal"); }

/* Only a certain weapon or spell hurts it: nothing else does, not traps, fire or hunger. Immortal: these don't apply. */
static int needs_special_wound(int characterId)
{
    cJSON *ch = character_json(characterId);
    return !flag(ch, "Immortal") && (flag(ch, "RequiresWeapon") || flag(ch, "RequiresSpell"));
}

int vitals_wounds_it(int characterId, int weaponId, const char *spell)
{
    if (!needs_special_wound(characterId)) return 1;
    cJSON *ch = character_json(characterId);
    if (flag(ch, "RequiresWeapon") && weaponId >= 0 && weaponId == num(ch, "RequiredWeaponId", -1)) return 1;
    const char *needed = cJSON_GetStringValue(get(ch, "RequiredSpellName"));
    return flag(ch, "RequiresSpell") && spell && needed && strcmp(spell, needed) == 0;
}

void vitals_damage_allowed(int characterId, int amount, const char *cause)
{
    specialWound = 1;
    vitals_damage(characterId, amount, cause);
    specialWound = 0;
}

/* Splitting when hit (Exult's Actor::reduce_health; GameTestWindow.Joining MaybeSplit): the new one comes on the next update. */
static int hitType, pendingSplits[64], pendingSplitCount;
void vitals_hit_type(int type) { hitType = type; }

static void maybe_split(int characterId, int actor)
{
    cJSON *joining = get(character_json(characterId), "Joining"), *kind;
    if (!joining || num(joining, "Growth", 0) == 0 || actor <= 0 || items_in_party(characterId)) return;
    cJSON_ArrayForEach(kind, get(joining, "NoSplitFrom")) if (kind->valueint == hitType) return;
    if (rnd(2) != 0) return;
    if (pendingSplitCount < 64) pendingSplits[pendingSplitCount++] = actor;
}

void vitals_damage(int characterId, int amount, const char *cause)
{
    int hp;
    if (amount <= 0 || vitals_dead(characterId) || !vitals_hp(characterId, &hp)) return;
    if (needs_special_wound(characterId) && !specialWound) return; /* only its weapon or spell hurts it (Character Editor > Health) */
    int actor = actor_of_character(characterId), max = vitals_max_hp(characterId);
    /* Exult's hit: badly hurt, the main character's screen flashes red; anyone else a red outline */
    if (actor == 0 && max > 0 && (amount >= max / 3 || hp < max / 4)) screen_flash(0xE00000, 0.15);
    else if (actor >= 0) world_hit_outline(actor);
    int half = max / 2;
    if (hp >= half && hp - amount < half && hp - amount > 0 && rnd(2) == 0) vitals_bark(characterId, 3); /* "Ouch!" */
    hp -= amount;
    if (vitals_immortal(characterId) && hp < 0) hp = 0; /* knocked out at worst */
    vitals_set_hp(characterId, hp);
    if (hp > 0) maybe_split(characterId, actor);
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

/* mend_wounds: at 50 or more of cold or heat, wounds don't mend. */
static int too_cold_or_hot_to_mend(const Vital *v)
{
    cJSON *s = survival(v->characterId);
    return (v->cold >= 50 && !flag(s, "ColdImmune")) || (v->heat >= 50 && !flag(s, "HeatImmune"));
}

static void mend(Vital *v)
{
    int hp, max = vitals_max_hp(v->characterId);
    int starving = can_get_hungry(v->characterId) && v->food <= survival_num(v->characterId, "HungryAtOrBelow", 9);
    if ((v->ailments & POISONED) || !vitals_hp(v->characterId, &hp) || (starving && hp > 0) || too_cold_or_hot_to_mend(v)) return;
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

/* System Settings > Combat: starving goes on hurting someone knocked out by it, until they die (off: Ultima 7's way). */
static int starvation_kills(void) { return flag(get(get((cJSON *)world_json(), "Project"), "Settings"), "StarvationKills"); }

static void hunger_minute(Vital *v)
{
    int starvesOn = starvation_kills() && v->unconscious && !v->dead; /* it goes on losing health where it lies */
    if (!can_get_hungry(v->characterId) || (vitals_incapacitated(v->characterId) && !starvesOn)) return;
    if (v->food > survival_num(v->characterId, "HungryAtOrBelow", 9)) return;
    if (v->food <= 0 && rnd(100) < survival_num(v->characterId, "StarvingChancePercent", 40))
        vitals_damage(v->characterId, survival_num(v->characterId, "StarvingDamage", 1), "starvation");
}

/* ---------------- Cold and Hot zones (GameTestWindow.Temperature, Exult's check_temperature) ---------------- */

static cJSON *object_json(int objectId)
{
    cJSON *o;
    cJSON_ArrayForEach(o, get(get(get((cJSON *)world_json(), "Project"), "Objects"), "Objects"))
        if (num(o, "Id", -2) == objectId) return o;
    return NULL;
}

/* figure_warmth: -75, and what's worn on the head, back, feet, body, hands and legs, and in any custom slot. */
static int worn_protection(int characterId, const char *field)
{
    static const int SLOTS[] = { 0 /* Head */, 6 /* Back */, 8 /* Feet */, 2 /* Body */, 4 /* Gloves */, 7 /* Legs */ };
    int total = -75;
    for (unsigned i = 0; i < sizeof SLOTS / sizeof SLOTS[0]; i++)
    {
        Item *it = items_in_slot(characterId, SLOTS[i]);
        cJSON *obj = it && items_category(it) == CAT_OBJECT ? object_json(items_item_id(it)) : NULL;
        if (obj) total += num(get(obj, "Armor"), field, 0);
    }
    cJSON *custom;
    cJSON_ArrayForEach(custom, get(get(character_json(characterId), "Paperdoll"), "CustomSlots"))
    {
        Item *it = items_in_slot(characterId, 100 + num(custom, "Id", 0));
        cJSON *obj = it && items_category(it) == CAT_OBJECT ? object_json(items_item_id(it)) : NULL;
        if (obj) total += num(get(obj, "Armor"), field, 0);
    }
    return total;
}

/* check_temperature: one minute of it. The new temperature. Kinds: low, low + 1, low + 2 (frostbite), low + 3 (frozen), low + 4 (easing). */
static int temperature_step(int characterId, int temperature, int inside, int protection, int low, const char *cause)
{
    int says = rnd(3) == 0;
    if (!inside || protection >= 100)
    {
        if (temperature == 0) return 0;
        int fall = inside ? 1 + (protection - 100) / 10 : 5;
        temperature -= fall < temperature ? fall : temperature;
        if (says) vitals_bark(characterId, low + 4);
        return temperature;
    }
    temperature += 1 + (100 - protection) / 20;
    if (temperature > 63) temperature = 63;
    if (!says) return temperature;
    switch (temperature / 10)
    {
        case 0: case 1: vitals_bark(characterId, low); break;
        case 2: vitals_bark(characterId, low + 1); break;
        case 3: case 4: vitals_bark(characterId, low + 2); break;
        case 5: vitals_bark(characterId, low + 2); vitals_damage(characterId, 1, cause); break;
        default: vitals_bark(characterId, low + 3); vitals_damage(characterId, 1 + rnd(3), cause); break;
    }
    return temperature;
}

static void temperature_minute(Vital *v, int actor)
{
    int x, y, f;
    cJSON *s = survival(v->characterId);
    world_actor_cell(actor, &x, &y, &f);
    v->cold = temperature_step(v->characterId, v->cold, !flag(s, "ColdImmune") && events_zone_here("Cold", x, y, f), worn_protection(v->characterId, "Warmth"), 18, "the cold");
    if (v->dead) return;
    v->heat = temperature_step(v->characterId, v->heat, !flag(s, "HeatImmune") && events_zone_here("Hot", x, y, f), worn_protection(v->characterId, "Cooling"), 23, "the heat");
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

/* ---------------- Monsters that join (GameTestWindow.Joining; Character Editor > Health > Joining) ---------------- */

int magic_appear(cJSON *kind, int beside, int cx, int cy);

typedef struct JoinWait { int a, b; double ms; } JoinWait;
static JoinWait *joinWaits;
static int joinWaitCount, joinWaitCap;
static double joinCheckMs;

static int same_kind_alive(int actor, int kindId, int floorThere)
{
    int id = world_actor_character(actor), x, y, f;
    if (world_actor_gone(actor) || vitals_dead(id) || world_character_source(id) != kindId) return 0;
    world_actor_cell(actor, &x, &y, &f);
    return f == floorThere;
}

static cJSON *character_of_kind(int kindId) { return character_json(kindId); }

/* update_frames: bit 0 wobbles as it moves; a neighbour two tiles to the north sets 2, east 4, south 8, west 16. */
static void joined_look(int actor, cJSON *look)
{
    const Sprite *sprite = sprite_find(cJSON_GetStringValue(get(look, "SpriteGroup")), cJSON_GetStringValue(get(look, "SpriteName")));
    if (!sprite || !sprite->frameCount) return;
    int id = world_actor_character(actor), kind = world_character_source(id), mask = 0, x, y, f;
    double px, py;
    world_actor_cell(actor, &x, &y, &f);
    world_actor_pixel(actor, &px, &py);
    /* how far apart neighbours stand: Exult's two tiles, or the next square on a map with bigger squares */
    double reachX = world_tile_w() > 16 ? world_tile_w() : 16, reachY = world_tile_h() > 16 ? world_tile_h() : 16, slackX = reachX / 2, slackY = reachY / 2;
    for (int o = 1; o < world_actor_count(); o++)
    {
        if (o == actor || !same_kind_alive(o, kind, f)) continue;
        double ox, oy;
        world_actor_pixel(o, &ox, &oy);
        double dx = world_wrap_dx(ox - px), dy = world_wrap_dy(oy - py);
        if (fabs(dx) < slackX && fabs(dy + reachY) < slackY) mask |= 2;
        else if (fabs(dy) < slackY && fabs(dx - reachX) < slackX) mask |= 4;
        else if (fabs(dx) < slackX && fabs(dy - reachY) < slackY) mask |= 8;
        else if (fabs(dy) < slackY && fabs(dx + reachX) < slackX) mask |= 16;
    }
    int wobble = world_walking(actor) ? rnd(2) : 0, frame = mask | wobble;
    int shown = sprite->frameCount >= 32 ? frame : sprite->frameCount >= 16 ? mask >> 1 : (wobble < sprite->frameCount ? wobble : sprite->frameCount - 1);
    world_actor_joined(actor, sprite, shown);
}

static void try_join(int actor, cJSON *rule)
{
    int id = world_actor_character(actor), kind = world_character_source(id), x, y, f;
    cJSON *result = character_of_kind(num(rule, "ResultCharacterId", -1));
    if (!result || items_in_party(id)) return;
    if (!flag(rule, "Fuses") && num(get(character_json(id), "Joining"), "Growth", 0) == 1) return; /* Ultima 7's only grow by being hit */
    int partner = num(rule, "PartnerCharacterId", -1);
    if (partner < 0) partner = kind;
    double reach = (num(rule, "ReachTiles", 3) > 1 ? num(rule, "ReachTiles", 3) : 1) * 8.0, px, py;
    if (reach < world_tile_w()) reach = world_tile_w(); /* the next square always counts */
    if (reach < world_tile_h()) reach = world_tile_h();
    world_actor_cell(actor, &x, &y, &f);
    world_actor_pixel(actor, &px, &py);
    for (int o = 1; o < world_actor_count(); o++)
    {
        if (o == actor || !same_kind_alive(o, partner, f) || items_in_party(world_actor_character(o))) continue;
        double ox, oy;
        world_actor_pixel(o, &ox, &oy);
        double dx = fabs(world_wrap_dx(ox - px)), dy = fabs(world_wrap_dy(oy - py));
        if ((dx > dy ? dx : dy) > reach) continue;
        int waiting = 0;
        for (int w = 0; w < joinWaitCount; w++) if ((joinWaits[w].a == actor && joinWaits[w].b == o) || (joinWaits[w].a == o && joinWaits[w].b == actor)) waiting = 1;
        if (waiting) continue;
        if (joinWaitCount >= joinWaitCap)
        {
            int cap = joinWaitCap ? joinWaitCap * 2 : 64;
            JoinWait *more = (JoinWait *)realloc(joinWaits, sizeof(JoinWait) * (size_t)cap);
            if (!more) return; /* no memory: no new one either */
            joinWaits = more; joinWaitCap = cap;
        }
        joinWaits[joinWaitCount++] = (JoinWait){ actor, o, (num(rule, "CooldownSeconds", 10) > 1 ? num(rule, "CooldownSeconds", 10) : 1) * 1000.0 };
        int most = num(rule, "MaxOnMap", 0);
        if (most > 0)
        {
            int on = 0;
            for (int k = 1; k < world_actor_count(); k++) on += same_kind_alive(k, num(result, "Id", -1), f);
            if (on >= most) continue;
        }
        if (rnd(100) >= num(rule, "ChancePercent", 25)) continue;
        sound_play(num(rule, "SoundResourceId", -1));
        int fuses = flag(rule, "Fuses");
        Item *carried[2] = { NULL, NULL };
        if (fuses)
        {
            int ids[2] = { id, world_actor_character(o) }, actors[2] = { actor, o };
            for (int k = 0; k < 2; k++)
            {
                carried[k] = items_strip(ids[k]);
                Vital *v = vital_of(ids[k]);
                if (v) v->dead = 1;
                world_walk_cancel(actors[k]);
                world_actor_joined(actors[k], NULL, 0);
                world_actor_remove(actors[k]);
            }
        }
        int born = magic_appear(result, actor, x, y);
        if (fuses)
            for (int k = 0; k < 2; k++)
            {
                if (!carried[k]) continue;
                if (born > 0) { Item *out[64]; int n = items_take_all(carried[k], out, 64); for (int i = 0; i < n; i++) items_add_loose(world_actor_character(born), out[i]); }
                items_free(carried[k]);
            }
        return;
    }
}

static void update_joining(double ms)
{
    for (int i = 0; i < pendingSplitCount; i++)
    {
        int actor = pendingSplits[i], id = actor < world_actor_count() ? world_actor_character(actor) : -1, x, y, f;
        if (id < 0 || world_actor_gone(actor) || vitals_dead(id)) continue;
        world_actor_cell(actor, &x, &y, &f);
        magic_appear(character_of_kind(world_character_source(id)), actor, x, y);
    }
    pendingSplitCount = 0;
    for (int w = 0; w < joinWaitCount; w++) if ((joinWaits[w].ms -= ms) <= 0) joinWaits[w--] = joinWaits[--joinWaitCount];
    if ((joinCheckMs += ms) < 200) return;
    joinCheckMs = 0;
    for (int a = 1; a < world_actor_count(); a++)
    {
        if (world_actor_gone(a) || vitals_dead(world_actor_character(a))) continue;
        cJSON *joining = get(character_json(world_actor_character(a)), "Joining"), *rule;
        if (!joining) continue;
        cJSON *look = get(joining, "JoinedSprite");
        const char *name = cJSON_GetStringValue(get(look, "SpriteName"));
        if (name && *name) joined_look(a, look);
        cJSON_ArrayForEach(rule, get(joining, "Rules")) if (!world_actor_gone(a)) try_join(a, rule);
    }
}

void vitals_update(double ms)
{
    if (gameOver) return;
    update_joining(ms);
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
            if (!vitals[i].dead && actor >= 0 && !world_actor_gone(actor)) temperature_minute(&vitals[i], actor);
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
        cJSON_AddNumberToObject(e, "CharacterId", v->characterId); cJSON_AddNumberToObject(e, "Food", v->food); cJSON_AddNumberToObject(e, "Cold", v->cold); cJSON_AddNumberToObject(e, "Heat", v->heat); cJSON_AddNumberToObject(e, "Ailments", v->ailments);
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
        v->cold = num(e, "Cold", 0); v->heat = num(e, "Heat", 0);
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

/* A character made in play (Summon): its own vitals, at full health. */
void vitals_add(int characterId)
{
    vitals = (Vital *)realloc(vitals, sizeof(Vital) * (size_t)(vitalCount + 1));
    Vital *v = &vitals[vitalCount++];
    memset(v, 0, sizeof *v);
    v->characterId = characterId;
    v->food = MAX_FOOD;
    vitals_heal_fully(characterId);
}

/* ResurrectDead: it lives again where its body lies, with what's still in it; the body is gone. */
int vitals_resurrect(int characterId)
{
    Vital *v = vital_of(characterId);
    int actor = world_actor_by_character(characterId), uid = world_body_of(characterId);
    if (!v || !v->dead || actor <= 0 || !uid) return 0;
    int c, i, q, x, y, n, fl = world_thing_floor(uid);
    world_thing_info(uid, &c, &i, &q, &x, &y, &n);
    Item *body = items_unpark(uid, c, i, q), *back[256];
    int count = items_take_all(body, back, 256);
    for (int k = 0; k < count; k++) items_add_loose(characterId, back[k]);
    items_free(body);
    world_thing_remove(uid);
    v->dead = 0; v->unconscious = 0;
    world_actor_place(actor, x, y, fl);
    world_set_pose(actor, "Idle", -1);
    vitals_heal_fully(characterId);
    char line[160];
    snprintf(line, sizeof line, "%s lives again!", cJSON_GetStringValue(get(character_json(characterId), "Name")) ? cJSON_GetStringValue(get(character_json(characterId), "Name")) : "Someone");
    game_log(line);
    return 1;
}

/* The main character lives again (Resurrect in its By Death event): the game is no longer over - standing, healed, the death music gone. */
int vitals_resurrect_player(void)
{
    int id = world_actor_character(0);
    Vital *v = vital_of(id);
    if (!v || !v->dead) return 0;
    v->dead = 0; v->unconscious = 0;
    gameOver = 0;
    vitals_heal_fully(id);
    world_set_pose(0, "Idle", -1);
    combat_stop_cue(); /* the death music ends */
    return 1;
}

int vitals_can_get_hungry(int characterId) { return can_get_hungry(characterId); }
