/*
 * Combat, ported from the Windows engine (GameTestWindow.Combat / Weapons /
 * Factions / CombatMusic / Levels, from Exult's combat.cc and actors.cc),
 * factions standing in for Exult's fixed alignments:
 *  - Combat_schedule::now_what: find an opponent, approach it (A*), gain
 *    Dexterity points each standard delay until 30, then strike; flee when
 *    below 3 HP (not Berserk); give up after more than 5 failures and go
 *    back to the schedule.
 *  - Opponents: anyone its faction sees in an "Attack on sight" band; anyone
 *    attacking someone it sees as "Ally" (the party defends the player).
 *    "Flee" makes it run (8-15 tiles off, a scream 2 in 3).
 *  - Hits: a 30-sided roll plus the attacker's Combat minus the defender's
 *    (lucky +3, protected +3); lying still, every blow lands. Damage
 *    1..Strength/3 plus 1..weapon points, less 1..armor; armor immune to the
 *    damage type: a clang and nothing. Powers: sleep, charm, curse, poison,
 *    paralyze, magebane.
 *  - Shooting and throwing: ammo by family, charges, a weapon thrown away
 *    (it lands where it flew); the shot flies and its hit is rolled when it
 *    gets there.
 *  - fight_back: the one hit turns on the attacker (same faction: no fight);
 *    the player bullying someone brings up to 3 of their side within 20
 *    tiles. The victim's faction thinks less of the attacker's.
 *  - The killing blow: the victim's experience goes to the killer (levels).
 *  - The player: combat mode (C); double-click someone to attack; struck
 *    whenever in reach; not Manual: picks a foe by its attack mode.
 *  - Combat music (System Settings > Combat), as Exult plays it.
 */
#include "combat.h"
#include "world.h"
#include "vitals.h"
#include "items.h"
#include "events.h"
#include "sound.h"
#include "schedules.h"
#include "platform.h"
#include "pictures.h"
#include "conditions.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define STD_DELAY 200
#define DEX_TO_ATTACK 30
#define OWNER_COMBAT 4

enum { F_ATTACK, F_IGNORE, F_ALLY, F_FLEE };
enum { M_NEAREST, M_WEAKEST, M_STRONGEST, M_BERSERK, M_PROTECT, M_DEFEND, M_FLANK, M_FLEE, M_RANDOM, M_MANUAL };
enum { K_STRENGTH = 1, K_DEXTERITY = 2, K_COMBAT = 4, K_MANA = 7, K_LEVEL = 8, K_EXP = 9, K_TRAINING = 10, K_NEXTEXP = 11 };
enum { D_NORMAL, D_FIRE, D_MAGIC, D_LIGHTNING, D_ETHEREAL, D_SONIC, D_FREEZING };
enum { P_SLEEP = 1, P_CHARM = 2, P_CURSE = 4, P_POISON = 8, P_PARALYZE = 16, P_MAGEBANE = 32, P_NODAMAGE = 128 };
enum { U_MELEE, U_POOR_THROWN, U_GOOD_THROWN, U_RANGED };
enum { A_NONE, A_AMMO, A_CHARGES, A_THROWS };
enum { TYPE_AMMO = 5, TYPE_PROJECTILE = 34, TYPE_WEAPON = 35, TYPE_ARMOR = 36 };
enum { TR_BY_HIT = 14 };

typedef struct Fighter
{
    int inCombat, target, fleeing, fleeFrom, fleeCount, failures, dexPoints, chasing, chaseX, chaseY, oppressor, hounding;
    double waitMs, poseBackMs;
} Fighter;

typedef struct Shot
{
    int used, attacker, target, attval, floor;
    const Sprite *sprite;
    double x, y, dx, dy, left, lift;
    cJSON *weapon, *ammo, *thrown;
} Shot;

static Fighter fighters[256];
static Shot shots[32];
static cJSON *project, *factionsJson, *statsJson, *settingsJson;
static int combatMode, playerTarget = -1, playerFinishesOff, playerDex, playerThing = -1;
static double playerStrikeMs, sightScanMs, nowMs;
static unsigned rng = 98765u;
static int rnd(int n) { rng = rng * 1103515245u + 12345u; return n > 0 ? (int)((rng >> 8) % (unsigned)n) : 0; }

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }

static cJSON *character_json(int characterId)
{
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project, "Characters"), "Characters")) if (num(c, "Id", -2) == characterId) return c;
    return NULL;
}

static int id_of(int actor) { return actor >= 0 ? world_actor_character(actor) : -1; }
static cJSON *json_of(int actor) { return character_json(id_of(actor)); }
static const char *name_of(int actor) { return str(json_of(actor), "Name"); }
static int dead(int actor) { return actor < 0 || world_actor_gone(actor) || vitals_dead(id_of(actor)); }
static int unconscious(int actor) { return vitals_unconscious(id_of(actor)); }
static int incapacitated(int actor) { return vitals_incapacitated(id_of(actor)); }
static int in_party(int actor) { return actor == 0 || items_in_party(id_of(actor)); }
static int faction_of(int actor) { return num(json_of(actor), "FactionId", -1); }
static int attack_mode(int actor) { return num(json_of(actor), "AttackMode", 0); }
static int hp_of(int actor, int fallback) { int hp; return vitals_hp(id_of(actor), &hp) ? hp : fallback; }

static int stat(int actor, int kind, int fallback)
{
    cJSON *s, *values = get(json_of(actor), "StatValues");
    cJSON_ArrayForEach(s, statsJson)
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

static int has_stat(int kind) { cJSON *s; cJSON_ArrayForEach(s, statsJson) if (num(s, "Kind", 0) == kind) return 1; return 0; }

static void set_stat(int actor, int kind, int value)
{
    cJSON *s, *values = get(json_of(actor), "StatValues");
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

/* ---------------- Factions (FactionDatabase) ---------------- */

static cJSON *faction(int id)
{
    cJSON *f;
    if (id < 0) return NULL;
    cJSON_ArrayForEach(f, get(factionsJson, "Factions")) if (num(f, "Id", -2) == id) return f;
    return NULL;
}

static int clamp100(int v) { return v < 0 ? 0 : v > 100 ? 100 : v; }

int combat_relation(int viewer, int target)
{
    cJSON *v = faction(viewer);
    int fallback = num(factionsJson, "DefaultRelation", 50);
    if (!v || !faction(target)) return fallback;
    char key[16];
    snprintf(key, sizeof key, "%d", target);
    cJSON *r = get(get(v, "Relations"), key);
    if (cJSON_IsNumber(r)) return clamp100(r->valueint);
    return viewer == target ? 100 : fallback;
}

void combat_set_relation(int viewer, int target, int value)
{
    cJSON *v = faction(viewer);
    if (!v || !faction(target)) return;
    cJSON *relations = get(v, "Relations");
    if (!relations) relations = cJSON_AddObjectToObject(v, "Relations");
    char key[16];
    snprintf(key, sizeof key, "%d", target);
    if (get(relations, key)) cJSON_ReplaceItemInObject(relations, key, cJSON_CreateNumber(clamp100(value)));
    else cJSON_AddNumberToObject(relations, key, clamp100(value));
}

/* BandFor: bands laid end to end from 0%; 100% belongs to the last. */
static int behavior_toward(int viewer, int target)
{
    int value = combat_relation(viewer, target), start = 0, last = F_IGNORE;
    cJSON *band, *bands = get(factionsJson, "Bands");
    if (!cJSON_GetArraySize(bands))
    {
        static const int widths[] = { 25, 15, 30, 30 }, kinds[] = { F_ATTACK, F_FLEE, F_IGNORE, F_ALLY };
        for (int i = 0; i < 4; i++) { if (value < start + widths[i]) return kinds[i]; start += widths[i]; }
        return F_ALLY;
    }
    cJSON_ArrayForEach(band, bands)
    {
        last = num(band, "Behavior", F_IGNORE);
        start += num(band, "WidthPercent", 10);
        if (value < start) return last;
    }
    return last;
}

enum { I_ATTACK, I_THEFT, I_KILL };
static void incident(int victimFaction, int offenderFaction, int kind)
{
    cJSON *v = faction(victimFaction);
    if (!v || !faction(offenderFaction)) return;
    int penalty = kind == I_ATTACK ? num(v, "AttackPenalty", 20) : kind == I_THEFT ? num(v, "TheftPenalty", 10) : num(v, "KillPenalty", 50);
    combat_set_relation(victimFaction, offenderFaction, combat_relation(victimFaction, offenderFaction) - penalty);
}

void combat_change_relation(int viewer, int target, const char *operation, int amount, int bothWays)
{
    for (int pass = 0; pass < (bothWays && viewer != target ? 2 : 1); pass++)
    {
        int from = pass ? target : viewer, to = pass ? viewer : target, current = combat_relation(from, to);
        combat_set_relation(from, to, !strcmp(operation, "Add") ? current + amount : !strcmp(operation, "Subtract") ? current - amount : amount);
    }
}

int combat_change_faction(const char *who, int factionId)
{
    cJSON *c = !strcmp(who, "Player") ? json_of(0) : character_json(atoi(who));
    if (!c) return 0;
    cJSON_ReplaceItemInObject(c, "FactionId", cJSON_CreateNumber(faction(factionId) ? factionId : -1));
    return 1;
}

static int same_side(int a, int b) { int fa = faction_of(a); return fa >= 0 && fa == faction_of(b); }
static int attitude(int viewer, int other) { return viewer == other ? F_ALLY : behavior_toward(faction_of(viewer), faction_of(other)); }

/* ---------------- Who's where ---------------- */

static double tiles_between(int a, int b)
{
    double ax, ay, bx, by;
    world_actor_pixel(a, &ax, &ay);
    world_actor_pixel(b, &bx, &by);
    return fmax(fabs(ax - bx), fabs(ay - by)) / 8;
}

/* Melee reach: neighbouring cells (diagonals too), within a few lifts. */
static int in_reach(int a, int b)
{
    int ax, ay, af, bx, by, bf;
    world_actor_cell(a, &ax, &ay, &af);
    world_actor_cell(b, &bx, &by, &bf);
    return abs(ax - bx) <= 1 && abs(ay - by) <= 1 && abs(world_actor_lift(a) - world_actor_lift(b)) <= 4;
}

/* Can see: within half a screen and nothing tall in between; the invisible are unseen by those not on their side. */
static int can_see(int viewer, int other)
{
    if (dead(viewer) || dead(other)) return 0;
    if (vitals_has(id_of(other), 32) && !same_side(viewer, other) && !(in_party(viewer) && in_party(other))) return 0;
    double vx, vy, ox, oy;
    world_actor_pixel(viewer, &vx, &vy);
    world_actor_pixel(other, &ox, &oy);
    if (fabs(vx - ox) > SCREEN_W / 2 || fabs(vy - oy) > SCREEN_H / 2) return 0;
    return world_line_clear(vx, vy, world_actor_lift(viewer), ox, oy);
}

static int facing_towards(int from, int to)
{
    double fx, fy, tx, ty;
    world_actor_pixel(from, &fx, &fy);
    world_actor_pixel(to, &tx, &ty);
    double dx = tx - fx, dy = ty - fy, a = atan2(dy, dx) * 180 / 3.14159265358979;
    /* 8 directions, N = 0 clockwise; east is 0 degrees, south 90 */
    int sector = (int)floor((a + 90 + 22.5 + 360) / 45) % 8;
    return sector;
}

static int target_of(int actor)
{
    if (actor == 0) return combatMode ? playerTarget : -1;
    return actor < 256 && fighters[actor].inCombat ? fighters[actor].target : -1;
}

/* ---------------- Weapons ---------------- */

static cJSON *weapon_settings(cJSON *def) { return get(def, "Weapon"); }

/* Actor::get_weapon: the weapon in hand (right, then left), else one wielded in its own slot; NULL: bare hands. */
static cJSON *wielded(int actor, Item **item) { return items_weapon(id_of(actor), item); }

/* combat.cc not_in_melee_range + range: whether this weapon shoots (or is thrown) at a foe this far. */
static int shoots(cJSON *weapon, int reach, double tiles)
{
    cJSON *w = weapon_settings(weapon);
    if (!w) return 0;
    int use = num(w, "Use", U_MELEE), range = num(w, "RangeTiles", 16);
    return (use == U_RANGED || ((use == U_POOR_THROWN || use == U_GOOD_THROWN) && !reach)) && tiles <= (range > 1 ? range : 1);
}

static int strike_ms(int swing) { return (swing == 0 ? 1 : swing == 3 ? 3 : 2) * STD_DELAY; }

/* The pose to strike or shoot with: the weapon's own, else the default - if it has art for it, else "Attacking Melee". */
static void strike_pose(int actor, cJSON *weapon, int shooting, int facing)
{
    cJSON *w = weapon_settings(weapon);
    const char *own = w ? str(w, shooting ? "ShootPose" : "AttackPose") : "";
    const char *name = own[0] ? own : shooting ? "Shooting" : "Attacking Melee";
    if (!world_set_pose(actor, name, facing) && !world_set_pose(actor, "Attacking Melee", facing)) world_set_pose(actor, "Idle", facing);
}

static void pose_back(int actor, double ms)
{
    if (actor == 0) fighters[0].poseBackMs = ms;
    else if (actor < 256) fighters[actor].poseBackMs = ms;
}

/* ---------------- Levels ---------------- */

static int level_steps(cJSON **levels)
{
    *levels = get(settingsJson, "Levels");
    return cJSON_GetArraySize(get(*levels, "Steps"));
}

static int experience_for(cJSON *levels, int level)
{
    cJSON *s;
    if (level <= 1) return 0;
    cJSON_ArrayForEach(s, get(levels, "Steps")) if (num(s, "Level", 0) == level) return num(s, "Experience", 0);
    /* Normalize: a missing step doubles the one before */
    int before = level == 2 ? 50 : experience_for(levels, level - 1);
    return before > 0x3FFFFFFF ? 0x7FFFFFFF : before * 2;
}

static int points_for(cJSON *levels, int level)
{
    cJSON *s;
    cJSON_ArrayForEach(s, get(levels, "Steps")) if (num(s, "Level", 0) == level) return num(s, "TrainingPoints", 0);
    return level > 2 ? points_for(levels, level - 1) : 3;
}

/* CheckLevel: its level from its experience - up only; each new level's training points; Next Exp kept up to date. */
static void check_level(int actor)
{
    cJSON *levels;
    level_steps(&levels);
    int maxLevel = num(levels, "MaxLevel", 8);
    if (maxLevel < 1) maxLevel = 1;
    if (maxLevel > 99) maxLevel = 99;
    int lock = num(json_of(actor), "LevelLock", 0), cap = lock > 0 && lock < maxLevel ? lock : maxLevel;
    int old = stat(actor, K_LEVEL, 1), exp = stat(actor, K_EXP, 0);
    if (old < 1) old = 1;
    int level = 1;
    while (level < cap && exp >= experience_for(levels, level + 1)) level++;
    if (level < old) level = old;
    if (level > old)
    {
        int points = 0;
        for (int l = old + 1; l <= level; l++) points += points_for(levels, l);
        set_stat(actor, K_LEVEL, level);
        set_stat(actor, K_TRAINING, stat(actor, K_TRAINING, 0) + points);
        if (in_party(actor))
        {
            char line[160];
            snprintf(line, sizeof line, "%s is now level %d.", name_of(actor), level);
            game_log(line);
        }
    }
    set_stat(actor, K_NEXTEXP, level < cap ? experience_for(levels, level + 1) : 0);
}

void combat_gain_experience(int actor, int amount)
{
    if (!has_stat(K_EXP) || actor < 0) return;
    long long total = (long long)stat(actor, K_EXP, 0) + amount;
    set_stat(actor, K_EXP, total > 0x7FFFFFFF ? 0x7FFFFFFF : (int)total);
    check_level(actor);
}

/* ---------------- Combat music ---------------- */

enum { CUE_NONE, CUE_DANGER, CUE_BATTLE, CUE_RUNAWAY, CUE_ESCAPED, CUE_VICTORY, CUE_BATTLEOVER, CUE_NEARDEATH, CUE_DEATH };
static int cue, startedBattle, nearDeath;
static double battleTimeMs = -30000, battleEndTimeMs, dangerCheckMs;

static cJSON *cue_settings(int which)
{
    cJSON *m = get(get(settingsJson, "Combat"), "Music");
    switch (which)
    {
        case CUE_DANGER: return get(m, "Danger");
        case CUE_BATTLE:
        {
            cJSON *a = get(m, "Battle"), *b = get(m, "Battle2");
            if (num(a, "MusicId", -1) < 0) return b;
            return num(b, "MusicId", -1) < 0 || rnd(2) == 0 ? a : b;
        }
        case CUE_RUNAWAY: return get(m, "RunAway");
        case CUE_ESCAPED: return get(m, "Escaped");
        case CUE_VICTORY: return get(m, "Victory");
        case CUE_BATTLEOVER: return get(m, "BattleOver");
        case CUE_NEARDEATH: return get(m, "NearDeath");
        case CUE_DEATH: return get(m, "Death");
    }
    return NULL;
}

static int music_playing(void)
{
    if (cue != CUE_NONE && !sound_owner_playing(OWNER_COMBAT)) cue = CUE_NONE;
    return cue != CUE_NONE;
}

static void play_cue(int which, cJSON *settings)
{
    if (!settings) settings = cue_settings(which);
    int id = num(settings, "MusicId", -1);
    if (id < 0) return;
    sound_stop_owner(OWNER_COMBAT);
    sound_stop_owner(OWNER_AMBIENT);
    cue = sound_play_ex(id, -1, flag(settings, "Loop"), OWNER_COMBAT, 1) ? which : CUE_NONE;
}

static void stop_music(void) { sound_stop_owner(OWNER_COMBAT); cue = CUE_NONE; }

/* Game_window::is_hostile_nearby: on screen, not dead, an enemy in combat. */
static int hostile_nearby(void)
{
    for (int a = 1; a < world_actor_count() && a < 256; a++)
    {
        Fighter *f = &fighters[a];
        if (dead(a) || in_party(a) || !f->inCombat || f->fleeing || f->target < 0) continue;
        int hostile = attitude(a, 0) == F_ATTACK || f->target == 0 || in_party(f->target);
        double x, y;
        world_actor_pixel(a, &x, &y);
        if (hostile && world_on_screen(x, y, 0)) return 1;
    }
    return 0;
}

int combat_silences_ambient(void) { return music_playing() || (combatMode && startedBattle && (playerTarget >= 0 || hostile_nearby())); }

static void update_music(double ms)
{
    int max = vitals_max_hp(id_of(0)), hp;
    int near = max > 0 && vitals_hp(id_of(0), &hp) && hp < max / 4 && !vitals_dead(id_of(0)) && (combatMode || hostile_nearby());
    if (near != nearDeath)
    {
        nearDeath = near;
        if (near) play_cue(CUE_NEARDEATH, NULL);
        else if (cue == CUE_NEARDEATH) stop_music();
    }
    if (nearDeath && cue == CUE_NEARDEATH && music_playing()) return;
    if (combatMode && !startedBattle && (playerTarget >= 0 || hostile_nearby()))
    {
        if (nowMs - battleTimeMs >= 30000) { play_cue(CUE_BATTLE, NULL); battleTimeMs = nowMs; battleEndTimeMs = nowMs - 1; }
        startedBattle = 1;
    }
    if ((dangerCheckMs -= ms) > 0) return;
    dangerCheckMs = 1000;
    if (combatMode) return;
    int hostile = hostile_nearby();
    if (hostile && !music_playing()) play_cue(CUE_DANGER, NULL);
    else if (!hostile && music_playing() && (cue == CUE_DANGER || cue == CUE_BATTLE)) play_cue(CUE_ESCAPED, NULL);
}

static void party_hit_music(int defender)
{
    if (in_party(defender) && cue == CUE_DANGER && music_playing())
    {
        cJSON *m = get(get(settingsJson, "Combat"), "Music");
        play_cue(CUE_BATTLE, num(get(m, "Battle"), "MusicId", -1) >= 0 ? get(m, "Battle") : get(m, "Battle2"));
    }
}

static void enemy_killed_music(int victim, int killer)
{
    if (killer < 0 || !in_party(killer) || in_party(victim) || attitude(victim, 0) != F_ATTACK || battleEndTimeMs >= battleTimeMs) return;
    for (int a = 1; a < world_actor_count() && a < 256; a++)
    {
        double x, y;
        world_actor_pixel(a, &x, &y);
        if (a == victim || dead(a) || !world_on_screen(x, y, 0) || fighters[a].fleeing || attack_mode(a) == M_FLEE) continue;
        if (attitude(a, 0) == F_ATTACK) return; /* still enemies */
    }
    battleEndTimeMs = nowMs;
    double seconds = (battleEndTimeMs - battleTimeMs) / 1000;
    play_cue(seconds > 15 && rnd(60) < seconds ? CUE_BATTLEOVER : CUE_VICTORY, NULL);
}

void combat_death_music(void) { nearDeath = 0; stop_music(); play_cue(CUE_DEATH, NULL); }

/* ---------------- NPC combat (Combat_schedule) ---------------- */

static void start_combat(int actor, int target)
{
    if (actor <= 0 || actor >= 256 || dead(actor) || dead(target) || target == actor) return;
    Fighter *f = &fighters[actor];
    if (!f->inCombat)
    {
        world_stand_up(actor); /* out of the chair or bed to fight */
        world_walk_cancel(actor);
        f->hounding = 0;
        f->inCombat = 1;
        f->failures = 0;
        f->dexPoints = 0;
        f->waitMs = STD_DELAY;
    }
    f->fleeing = 0;
    f->target = target;
}

static void end_combat(int actor)
{
    Fighter *f = &fighters[actor];
    f->inCombat = f->fleeing = f->chasing = 0;
    f->target = f->fleeFrom = -1;
    world_walk_cancel(actor);
    world_set_pose(actor, "Idle", -1);
    schedules_resume(actor); /* back to its normal schedule */
}

static int all_ally_target(int me, int foe)
{
    for (int a = 0; a < world_actor_count() && a < 256; a++)
        if (a != me && !dead(a) && same_side(a, me) && target_of(a) == foe) return 1;
    return 0;
}

/* PickFoe: by its attack mode. */
static int pick_foe(int me, const int *foes, int n)
{
    if (!n) return -1;
    int nearest = foes[0], weakest = foes[0], strongest = foes[0];
    for (int i = 1; i < n; i++)
    {
        if (hp_of(foes[i], 99) < hp_of(weakest, 99)) weakest = foes[i];
        if (hp_of(foes[i], 0) > hp_of(strongest, 0)) strongest = foes[i];
    }
    switch (attack_mode(me))
    {
        case M_WEAKEST: return weakest;
        case M_STRONGEST: return strongest;
        case M_RANDOM: return foes[rnd(n)];
        case M_BERSERK: return rnd(2) == 0 ? foes[rnd(n)] : nearest;
        case M_PROTECT:
        {
            /* find_protected_attacker: the nearest one attacking the party member under the halo */
            for (int i = 0; i < n; i++)
            {
                int t = target_of(foes[i]);
                if (t >= 0 && in_party(t) && flag(json_of(t), "CombatProtected")) return foes[i];
            }
            return foes[0];
        }
        case M_DEFEND: return stat(me, K_STRENGTH, 10) <= 2 * hp_of(me, 99) ? nearest : weakest;
        case M_FLANK:
            for (int i = 0; i < n; i++) if (all_ally_target(me, foes[i])) return foes[i];
            return nearest;
    }
    return nearest;
}

/* find_foe: those it would attack, or who attack it or its allies; the awake first, nearest first. */
static int find_foe(int me)
{
    int foes[256], n = 0, asleep[256], m = 0;
    for (int a = 0; a < world_actor_count() && a < 256; a++)
    {
        if (a == me || dead(a) || !can_see(me, a)) continue;
        int victim = target_of(a);
        if (attitude(me, a) == F_ATTACK || (victim >= 0 && (victim == me || attitude(me, victim) == F_ALLY) && !same_side(me, a)))
        {
            if (incapacitated(id_of(a)) || unconscious(a)) asleep[m++] = a; else foes[n++] = a;
        }
    }
    for (int i = 0; i < m; i++) foes[n++] = asleep[i];
    /* nearest first */
    for (int i = 1; i < n; i++)
        for (int j = i; j > 0 && tiles_between(me, foes[j]) < tiles_between(me, foes[j - 1]); j--) { int t = foes[j]; foes[j] = foes[j - 1]; foes[j - 1] = t; }
    return pick_foe(me, foes, n);
}

/* Combat_schedule::run_away: off 8-15 tiles in a random diagonal direction. */
static void run_away(int actor)
{
    Fighter *f = &fighters[actor];
    f->fleeCount++;
    int x, y, fl;
    world_actor_cell(actor, &x, &y, &fl);
    int cellsX = (int)ceil(8.0 * 8 / world_tile_w()), dx = rnd(2) * 2 - 1, dy = rnd(2) * 2 - 1;
    int tx = x + dx * (cellsX * (8 + rnd(8)) / 8), ty = y + dy * (cellsX * (8 + rnd(8)) / 8);
    if (tx < 0) tx = 0;
    if (ty < 0) ty = 0;
    if (tx >= world_map_w()) tx = world_map_w() - 1;
    if (ty >= world_map_h()) ty = world_map_h() - 1;
    if (!world_walk_to(actor, tx, ty, 0, 0)) f->waitMs = STD_DELAY + rnd(1000);
    if (f->fleeCount == 1 && rnd(3) > 0) vitals_bark(id_of(actor), 4); /* a scream, 2 in 3 */
}

static void strike(int attacker, int defender);
static int shoot(int attacker, int target, Item *weaponItem, cJSON *weapon);

static void combat_now_what(int actor)
{
    Fighter *f = &fighters[actor];
    if (f->fleeing)
    {
        if (f->fleeFrom < 0 || dead(f->fleeFrom) || !can_see(actor, f->fleeFrom) || f->fleeCount > 6) { f->fleeCount = 0; end_combat(actor); return; }
        run_away(actor);
        return;
    }
    if (f->target < 0 || dead(f->target)) f->target = find_foe(actor);
    if (f->target < 0)
    {
        if (++f->failures > 5) { end_combat(actor); return; }
        f->waitMs = 400;
        return;
    }
    int target = f->target, mode = attack_mode(actor);
    if (mode == M_FLEE || (mode != M_BERSERK && hp_of(actor, 99) < 3))
    {
        f->fleeing = 1;
        f->fleeFrom = target;
        run_away(actor);
        return;
    }
    if (f->dexPoints < DEX_TO_ATTACK)
    {
        int dex = stat(actor, K_DEXTERITY, 10);
        f->dexPoints += dex > 1 ? dex : 1;
        f->waitMs = STD_DELAY;
        return;
    }
    Item *held = NULL;
    cJSON *weapon = wielded(actor, &held);
    int reach = in_reach(actor, target);
    if (weapon && shoots(weapon, reach, tiles_between(actor, target)))
    {
        double sx, sy, tx, ty;
        world_actor_pixel(actor, &sx, &sy);
        world_actor_pixel(target, &tx, &ty);
        if (world_line_clear(sx, sy, world_actor_lift(actor), tx, ty))
        {
            strike_pose(actor, weapon, 1, facing_towards(actor, target));
            if (shoot(actor, target, held, weapon))
            {
                f->dexPoints -= DEX_TO_ATTACK;
                f->waitMs = strike_ms(num(weapon_settings(weapon), "ShootSwing", 0)) + STD_DELAY;
                pose_back(actor, strike_ms(num(weapon_settings(weapon), "ShootSwing", 0)));
                return;
            }
        }
    }
    if (!reach)
    {
        int tx, ty, tf;
        world_actor_cell(target, &tx, &ty, &tf);
        if (world_walk_to(actor, tx, ty, 0, 1))
        {
            f->failures = 0;
            f->chasing = 1;
            f->chaseX = tx; f->chaseY = ty;
        }
        else
        {
            if (++f->failures > 5) { end_combat(actor); return; }
            f->waitMs = 2 * STD_DELAY + rnd(500);
        }
        return;
    }
    f->dexPoints -= DEX_TO_ATTACK;
    strike_pose(actor, weapon, 0, facing_towards(actor, target));
    int swing = weapon ? strike_ms(num(weapon_settings(weapon), "Swing", 2)) : 2 * STD_DELAY;
    pose_back(actor, swing);
    strike(actor, target);
    f->waitMs = swing;
}

/* Approach_actor_action: close enough to strike, or the target moved more than 2 tiles from where it was - think again. */
static void chase_check(int actor)
{
    Fighter *f = &fighters[actor];
    if (!f->chasing) return;
    int stop = !f->inCombat || f->target < 0 || dead(f->target) || in_reach(actor, f->target) || !world_walking(actor);
    if (!stop)
    {
        int tx, ty, tf, cells = (int)ceil(2.0 * 8 / world_tile_w());
        world_actor_cell(f->target, &tx, &ty, &tf);
        stop = abs(tx - f->chaseX) > cells || abs(ty - f->chaseY) > cells;
    }
    if (!stop) return;
    f->chasing = 0;
    world_walk_cancel(actor);
    f->waitMs = 0;
}

/* Hound_schedule::now_what: keep about 3 tiles from the player, facing them; give up past 20. */
static void hound_now_what(int actor)
{
    Fighter *f = &fighters[actor];
    double distance = tiles_between(actor, 0);
    if (distance < 3 || in_reach(actor, 0)) { world_set_pose(actor, "Idle", facing_towards(actor, 0)); f->waitMs = STD_DELAY + 500 + rnd(1000); return; }
    if (distance > 20) { f->waitMs = STD_DELAY + 500 + rnd(1000); return; }
    int x, y, fl;
    world_actor_cell(0, &x, &y, &fl);
    if (!world_walk_to(actor, x, y, 0, 1)) f->waitMs = 2000 + rnd(3000);
    else f->waitMs = 3 * STD_DELAY * 2; /* a few steps, then look again */
}

/* Once a second, someone not already fighting looks around: enemies on sight, someone it fears, an ally under attack. */
static void scan_surroundings(int me)
{
    Fighter *f = &fighters[me];
    if (incapacitated(me) || f->inCombat || events_actor_busy(me) || events_actor_held(me)) return;
    int n = world_actor_count();
    for (int other = 0; other < n && other < 256; other++)
    {
        if (other == me || !can_see(me, other) || unconscious(other)) continue;
        int a = attitude(me, other);
        if (a == F_ATTACK) { start_combat(me, other); return; }
        if (a == F_FLEE && !f->fleeing)
        {
            f->fleeing = 1; f->fleeFrom = other; f->inCombat = 1; f->waitMs = 0;
            world_stand_up(me);
            world_walk_cancel(me);
            return;
        }
    }
    for (int other = 0; other < n && other < 256; other++)
    {
        if (other == me || dead(other)) continue;
        int victim = target_of(other);
        if (victim < 0 || dead(victim) || victim == me) continue;
        int defend = attitude(me, victim) == F_ALLY || (victim == 0 && in_party(me));
        if (defend && attitude(me, other) != F_ALLY && !same_side(me, other) && can_see(me, other)) { start_combat(me, other); return; }
    }
}

/* ---------------- A blow ---------------- */

/* Actor::roll_to_win: a 30-sided die; 0 always loses, 29 always wins, else roll + attacker - defender must reach 14. */
static int roll_to_win(int attacker, int defender)
{
    int roll = rnd(30);
    if (roll == 0) return 0;
    if (roll == 29) return 1;
    return roll + attacker - defender >= 14;
}

/* Knocked out, asleep or paralyzed (or asleep in its bed): every blow lands. */
static int motionless(int actor) { return incapacitated(actor) || schedules_sleeping(actor); }

static void attack_player(int victim, int includeSelf);

/* Actor::fight_back, with the faction's memory of it. */
static void fight_back(int defender, int attacker)
{
    if (same_side(defender, attacker) || dead(defender)) return;
    int firstBlow = 0;
    if (defender > 0 && defender < 256)
    {
        Fighter *f = &fighters[defender];
        firstBlow = f->oppressor != attacker + 1;
        f->oppressor = attacker + 1;
        if (!f->inCombat || f->target < 0 || f->fleeing) start_combat(defender, attacker);
    }
    if (firstBlow) incident(faction_of(defender), faction_of(attacker), I_ATTACK);
    if (attacker == 0 && !in_party(defender) && firstBlow) { vitals_bark(id_of(defender), 5); attack_player(defender, 0); }
}

/* Game_window::attack_avatar: up to 3 within 20 tiles, on the victim's side and able to see the player. */
static void attack_player(int victim, int includeSelf)
{
    int order[256], n = 0, helpers = 0;
    for (int a = 1; a < world_actor_count() && a < 256; a++) order[n++] = a;
    for (int i = 1; i < n; i++)
        for (int j = i; j > 0 && tiles_between(order[j], 0) < tiles_between(order[j - 1], 0); j--) { int t = order[j]; order[j] = order[j - 1]; order[j - 1] = t; }
    for (int i = 0; i < n; i++)
    {
        int who = order[i];
        if ((!includeSelf && who == victim) || in_party(who) || dead(who) || incapacitated(who) || tiles_between(who, 0) > 20) continue;
        int sympathetic = who == victim || same_side(who, victim) || attitude(who, victim) == F_ALLY;
        if (!sympathetic || !can_see(who, 0)) continue;
        start_combat(who, 0);
        if (++helpers >= 3) break;
    }
}

static void show_hit(int actor, const char *text)
{
    if (flag(get(settingsJson, "Combat"), "ShowHitNumbers")) world_bark(actor, text, 1000);
}

/* Actor::apply_damage: what a landed blow does after armor - 0 and a clang if the armor is immune. */
static int weapon_damage(int attacker, int defender, int points, int type)
{
    int str = stat(attacker, K_STRENGTH, 10) / 3, damage = 0;
    if (points >= 127) damage = 127;
    else
    {
        if (type != D_LIGHTNING && str > 0) damage += 1 + rnd(str);
        if (points > 0) damage += 1 + rnd(points);
    }
    cJSON *armor[16];
    int n = items_armor(id_of(defender), armor, 16), total = 0;
    for (int i = 0; i < n; i++)
    {
        cJSON *a = get(armor[i], "Armor"), *t;
        int p = num(a, "ArmorPoints", 0);
        total += p > 0 ? p : 0;
        cJSON_ArrayForEach(t, get(a, "Immunities"))
            if (cJSON_IsNumber(t) && t->valueint == type) { sound_play(num(a, "ImmuneSoundResourceId", -1)); return 0; } /* a metal clang */
    }
    if (points >= 127 || type == D_LIGHTNING || type == D_ETHEREAL || type == D_SONIC) total = 0;
    if (total > 0) damage -= 1 + rnd(total);
    if (damage <= 0 && incapacitated(defender)) damage = str > 0 ? 1 + rnd(str) : 0; /* the defenceless get hurt anyway */
    return damage > 0 ? damage : 0;
}

/* A blow (or a shot) that has landed: damage, then its powers. */
static void land_blow(int attacker, int defender, cJSON *weapon, cJSON *ammo)
{
    party_hit_music(defender);
    cJSON *w = weapon_settings(weapon);
    int points = w ? num(w, "Damage", 4) : 1, type = w ? num(w, "DamageType", D_NORMAL) : D_NORMAL, powers = w ? num(w, "Powers", 0) : 0;
    if (ammo)
    {
        cJSON *a = get(ammo, "Ammo");
        points += num(a, "DamageBonus", 0);
        if (flag(a, "Poisons")) powers |= P_POISON;
    }
    if (w) sound_play(num(w, "HitSoundResourceId", -1));
    int noDamage = (powers & P_NODAMAGE) != 0;
    int damage = noDamage || points == 0 ? 0 : weapon_damage(attacker, defender, points, type);
    char text[16];
    snprintf(text, sizeof text, "-%d", damage);
    show_hit(defender, text);
    if (damage <= 0 && !noDamage) return;
    if (damage > 0)
    {
        int willDie = hp_of(defender, 99) - damage <= 0;
        if (willDie) incident(faction_of(defender), faction_of(attacker), I_KILL);
        char cause[160];
        if (attacker == 0) snprintf(cause, sizeof cause, "slain by you");
        else snprintf(cause, sizeof cause, "slain by %s", name_of(attacker));
        vitals_damage(id_of(defender), damage, cause);
        if (willDie && vitals_dead(id_of(defender)))
        {
            if (attacker == 0) { char line[160]; snprintf(line, sizeof line, "You have slain %s.", name_of(defender)); game_log(line); }
            int worth = stat(defender, K_EXP, 0); /* the killing blow's experience */
            if (worth > 0 && attacker != defender) combat_gain_experience(attacker, worth);
            enemy_killed_music(defender, attacker);
        }
    }
    if (dead(defender)) return;
    int id = id_of(defender);
    if (powers & P_SLEEP) vitals_inflict(id, 2);
    if (powers & P_CHARM) vitals_inflict(id, 8);
    if (powers & P_CURSE) vitals_inflict(id, 16);
    if (powers & P_POISON) vitals_inflict(id, 1);
    if (powers & P_PARALYZE) vitals_inflict(id, 4);
    if (powers & P_MAGEBANE) set_stat(defender, K_MANA, 0);
    events_fire_character(id, TR_BY_HIT);
}

static void strike(int attacker, int defender)
{
    if (dead(defender)) return;
    cJSON *weapon = wielded(attacker, NULL), *w = weapon_settings(weapon);
    if (w) sound_play(num(w, "AttackSoundResourceId", -1));
    int attval = stat(attacker, K_COMBAT, 10) + (w && flag(w, "Lucky") ? 3 : 0);
    int defval = stat(defender, K_COMBAT, 10) + (vitals_has(id_of(defender), 64) ? 3 : 0);
    int hit = (w && flag(w, "AutoHit")) || motionless(defender) || roll_to_win(attval, defval);
    fight_back(defender, attacker);
    if (!hit) { show_hit(defender, "miss"); return; }
    land_blow(attacker, defender, weapon, NULL);
}

/* ---------------- Shooting and throwing ---------------- */

static cJSON *object_def(int id)
{
    cJSON *o;
    cJSON_ArrayForEach(o, get(get(project, "Objects"), "Objects")) if (num(o, "Id", -2) == id) return o;
    return NULL;
}

/* Game_object::attack_object at range: ammo, charges or the weapon itself used up; the shot flies. */
static int shoot(int attacker, int target, Item *weaponItem, cJSON *weapon)
{
    cJSON *w = weapon_settings(weapon), *ammo = NULL, *thrown = NULL;
    int id = id_of(attacker);
    switch (num(w, "Ammo", A_NONE))
    {
        case A_AMMO:
            ammo = items_use_ammo(id, str(w, "AmmoFamily"));
            if (!ammo)
            {
                if (attacker == 0) { char line[160]; snprintf(line, sizeof line, "Out of ammunition for \"%s\".", str(weapon, "Name")); game_log(line); }
                return 0;
            }
            break;
        case A_CHARGES:
            if (!items_use_charge(id, weaponItem, num(w, "Charges", 10), flag(w, "GoneWhenEmpty")))
            {
                if (attacker == 0) { char line[160]; snprintf(line, sizeof line, "\"%s\" has no charges left.", str(weapon, "Name")); game_log(line); }
                return 0;
            }
            break;
        case A_THROWS:
            if (!flag(w, "Returns")) { thrown = weapon; items_use_one(id, weaponItem); }
            break;
    }
    cJSON *projectile = object_def(num(w, "ProjectileObjectId", -1));
    cJSON *flies = projectile && num(projectile, "Type", -1) == TYPE_PROJECTILE ? projectile : ammo ? ammo : weapon;
    double tiles = tiles_between(attacker, target);
    int attval = stat(attacker, K_COMBAT, 10) + (flag(w, "Lucky") ? 3 : 0) + 6, use = num(w, "Use", U_MELEE);
    if (use == U_POOR_THROWN) attval -= (int)tiles;
    else if (use == U_GOOD_THROWN) attval -= (int)tiles / 2;
    sound_play(num(w, "AttackSoundResourceId", -1));
    Shot *s = NULL;
    for (int i = 0; i < 32 && !s; i++) if (!shots[i].used) s = &shots[i];
    if (!s) s = &shots[0];
    double ax, ay, tx, ty;
    world_actor_pixel(attacker, &ax, &ay);
    world_actor_pixel(target, &tx, &ty);
    int cx, cy;
    world_actor_cell(attacker, &cx, &cy, &s->floor);
    double length = sqrt((tx - ax) * (tx - ax) + (ty - ay) * (ty - ay));
    if (length < 1) length = 1;
    s->used = 1; s->attacker = attacker; s->target = target; s->attval = attval;
    s->sprite = sprite_find(str(flies, "SpriteGroup"), str(flies, "SpriteName"));
    s->x = ax; s->y = ay; s->dx = (tx - ax) / length; s->dy = (ty - ay) / length; s->left = length;
    s->lift = world_actor_lift(attacker) + 2;
    s->weapon = weapon; s->ammo = ammo; s->thrown = thrown;
    return 1;
}

/* A shot reaches where its target was: the hit is rolled now; a thrown weapon lands there. */
static void shot_arrives(Shot *s)
{
    int victim = s->target;
    double vx, vy;
    if (!dead(victim))
    {
        world_actor_pixel(victim, &vx, &vy);
        if (fabs(vx - s->x) > 12 || fabs(vy - s->y) > 12) victim = -1; /* it moved out of the way */
    }
    else victim = -1;
    if (victim >= 0)
    {
        cJSON *w = weapon_settings(s->weapon);
        int defval = stat(victim, K_COMBAT, 10) + (vitals_has(id_of(victim), 64) ? 3 : 0);
        fight_back(victim, s->attacker);
        if (!flag(w, "AutoHit") && !roll_to_win(s->attval, defval)) show_hit(victim, "miss");
        else land_blow(s->attacker, victim, s->weapon, s->ammo);
    }
    if (s->thrown)
    {
        int x = (int)floor(s->x / world_tile_w()), y = (int)floor(s->y / world_tile_h());
        world_thing_put(CAT_OBJECT, num(s->thrown, "Id", -1), 1, x, y, s->floor);
    }
    s->used = 0;
}

#define SHOT_SPEED 320.0 /* pixels a second */

static void update_shots(double ms)
{
    for (int i = 0; i < 32; i++)
    {
        Shot *s = &shots[i];
        if (!s->used) continue;
        double step = SHOT_SPEED * ms / 1000;
        if (step >= s->left) { s->x += s->dx * s->left; s->y += s->dy * s->left; shot_arrives(s); continue; }
        s->x += s->dx * step; s->y += s->dy * step; s->left -= step;
    }
}

void combat_draw(uint32_t *pixels, uint32_t ms)
{
    for (int i = 0; i < 32; i++)
        if (shots[i].used && shots[i].sprite) world_draw_sprite(pixels, shots[i].sprite, shots[i].x, shots[i].y, (int)shots[i].lift, ms);
}

/* ---------------- The player ---------------- */

int combat_mode(void) { return combatMode; }

void combat_toggle_mode(void)
{
    combatMode = !combatMode;
    if (!combatMode) { playerTarget = -1; playerThing = -1; }
    startedBattle = 0;
    if (!combatMode && !vitals_dead(id_of(0)))
        for (int a = 1; a < world_actor_count() && a < 256; a++)
        {
            if (dead(a) || in_party(a)) continue;
            int foe = attitude(a, 0) == F_ATTACK || (fighters[a].inCombat && (fighters[a].target == 0 || in_party(fighters[a].target)));
            if (foe && tiles_between(0, a) < 18 && can_see(0, a)) { play_cue(CUE_RUNAWAY, NULL); break; }
        }
    game_log(combatMode ? "Combat mode: double-click someone to attack them. C to stop." : "Combat mode off.");
}

void combat_player_target(int actor)
{
    if (actor <= 0 || dead(actor)) return;
    int finishing = unconscious(actor);
    char line[160];
    if (finishing && !wielded(0, NULL))
    {
        snprintf(line, sizeof line, "%s is unconscious - you need a weapon in hand to finish them off.", name_of(actor));
        game_log(line);
        return;
    }
    playerTarget = actor;
    playerThing = -1;
    playerFinishesOff = finishing;
    snprintf(line, sizeof line, finishing ? "Finishing off %s." : "Attacking %s.", name_of(actor));
    game_log(line);
}

static void update_fuses(double ms);

static void update_player(double ms)
{
    Fighter *me = &fighters[0];
    if (me->poseBackMs > 0 && (me->poseBackMs -= ms) <= 0 && !incapacitated(0) && !vitals_dead(id_of(0))) world_set_pose(0, "Idle", -1);
    if (combatMode && playerTarget < 0 && attack_mode(0) != M_MANUAL)
    {
        int foe = find_foe(0);
        if (foe >= 0 && !unconscious(foe)) playerTarget = foe;
    }
    if (combatMode && playerThing >= 0)
    {
        if (!world_thing_there(playerThing)) { playerThing = -1; return; }
        if (incapacitated(0) || (playerStrikeMs -= ms) > 0) return;
        playerStrikeMs = STD_DELAY;
        if (playerDex < DEX_TO_ATTACK) { int dex = stat(0, K_DEXTERITY, 10); playerDex += dex > 1 ? dex : 1; return; }
        int px, py, pf, tc, ti, tq, tx, ty, tin;
        world_actor_cell(0, &px, &py, &pf);
        world_thing_info(playerThing, &tc, &ti, &tq, &tx, &ty, &tin);
        if (abs(tx - px) > 1 || abs(ty - py) > 1 || abs(world_thing_z(playerThing) - world_actor_lift(0)) > 4) return;
        playerDex -= DEX_TO_ATTACK;
        world_face_cell(0, tx, ty, "Attacking Melee");
        pose_back(0, 2 * STD_DELAY);
        combat_strike_thing(playerThing, 0);
        return;
    }
    if (!combatMode || playerTarget < 0) return;
    if (dead(playerTarget) || (unconscious(playerTarget) && !(playerFinishesOff && wielded(0, NULL)))) { playerTarget = -1; playerFinishesOff = 0; return; }
    if (incapacitated(0)) return;
    if ((playerStrikeMs -= ms) > 0) return;
    playerStrikeMs = STD_DELAY;
    if (playerDex < DEX_TO_ATTACK) { int dex = stat(0, K_DEXTERITY, 10); playerDex += dex > 1 ? dex : 1; return; }
    Item *held = NULL;
    cJSON *weapon = wielded(0, &held);
    int reach = in_reach(0, playerTarget);
    if (weapon && shoots(weapon, reach, tiles_between(0, playerTarget)))
    {
        if (!shoot(0, playerTarget, held, weapon)) return;
        playerDex -= DEX_TO_ATTACK;
        strike_pose(0, weapon, 1, facing_towards(0, playerTarget));
        pose_back(0, strike_ms(num(weapon_settings(weapon), "ShootSwing", 0)));
        return;
    }
    if (!reach) return;
    playerDex -= DEX_TO_ATTACK;
    strike_pose(0, weapon, 0, facing_towards(0, playerTarget));
    pose_back(0, weapon ? strike_ms(num(weapon_settings(weapon), "Swing", 2)) : 2 * STD_DELAY);
    strike(0, playerTarget);
}

/* ---------------- Each frame ---------------- */

void combat_load(void)
{
    project = get((cJSON *)world_json(), "Project");
    factionsJson = get(project, "Factions");
    statsJson = get(get(project, "Stats"), "Stats");
    settingsJson = get(project, "Settings");
    for (int i = 0; i < 256; i++) { memset(&fighters[i], 0, sizeof fighters[i]); fighters[i].target = fighters[i].fleeFrom = -1; }
    combatMode = 0; playerTarget = -1;
}

int combat_busy(int actor) { return actor > 0 && actor < 256 && (fighters[actor].inCombat || fighters[actor].hounding); }

void combat_update(double ms)
{
    nowMs += ms;
    update_shots(ms);
    update_fuses(ms);
    if (vitals_game_over()) return;
    update_music(ms);
    update_player(ms);
    int n = world_actor_count();
    for (int a = 1; a < n && a < 256; a++)
    {
        Fighter *f = &fighters[a];
        if (f->poseBackMs > 0 && (f->poseBackMs -= ms) <= 0 && !world_walking(a) && !incapacitated(a)) world_set_pose(a, "Idle", -1);
        if (dead(a)) { f->inCombat = f->hounding = 0; continue; }
        if (!f->inCombat && !f->hounding) continue;
        if (incapacitated(a) || events_actor_busy(a) || events_actor_held(a)) continue;
        chase_check(a);
        if (world_walking(a)) continue;
        if ((f->waitMs -= ms) > 0) continue;
        f->waitMs = 0;
        if (f->inCombat) combat_now_what(a);
        else hound_now_what(a);
    }
    if ((sightScanMs -= ms) > 0) return;
    sightScanMs = 1000;
    for (int a = 1; a < n && a < 256; a++) if (!dead(a)) scan_surroundings(a);
}

/* ---------------- Commands ---------------- */

/* Attack: the player goes into combat mode at its target; anyone else fights it. */
int combat_attack(int attacker, int target)
{
    if (attacker < 0 || target < 0 || attacker == target || dead(attacker) || dead(target)) return 0;
    if (attacker == 0)
    {
        if (!combatMode) combat_toggle_mode();
        combat_player_target(target);
        return 1;
    }
    start_combat(attacker, target);
    return 1;
}

/* Game_window::theft: moving something not yours, seen by a sympathetic witness facing you within 12 tiles. */
static int theftWarnings, theftChunkX = -1, theftChunkY = -1;
void combat_theft(int ownerCharacterId, int factionId)
{
    double px, py;
    world_actor_pixel(0, &px, &py);
    int cx = (int)(px / 128), cy = (int)(py / 128);
    if (cx != theftChunkX || cy != theftChunkY) { theftChunkX = cx; theftChunkY = cy; theftWarnings = 0; }
    int witness = -1, closest = -1;
    int order[256], n = 0;
    for (int a = 1; a < world_actor_count() && a < 256; a++) order[n++] = a;
    for (int i = 1; i < n; i++)
        for (int j = i; j > 0 && tiles_between(order[j], 0) < tiles_between(order[j - 1], 0); j--) { int t = order[j]; order[j] = order[j - 1]; order[j - 1] = t; }
    for (int i = 0; i < n && witness < 0; i++)
    {
        int who = order[i], fw = faction_of(who);
        int sympathetic = id_of(who) == ownerCharacterId || (factionId >= 0 && (fw == factionId || behavior_toward(fw, factionId) == F_ALLY));
        if (in_party(who) || dead(who) || incapacitated(who) || !sympathetic || tiles_between(who, 0) > 12 || !can_see(who, 0)) continue;
        if (facing_towards(who, 0) == world_actor_facing(who) || fighters[who].hounding) witness = who;
        else if (closest < 0) closest = who;
    }
    if (witness < 0) { if (closest >= 0 && rnd(2) == 0) vitals_bark(id_of(closest), 7); return; }
    incident(factionId, faction_of(0), I_THEFT);
    world_set_pose(witness, "Idle", facing_towards(witness, 0));
    if (!fighters[witness].inCombat) { world_walk_cancel(witness); fighters[witness].hounding = 1; fighters[witness].waitMs = STD_DELAY; }
    theftWarnings++;
    cJSON *own = get(get(get(json_of(witness), "Verbose"), "Lines"), "TheftWarning");
    int warnings = cJSON_IsArray(own) ? cJSON_GetArraySize(own) : 3;
    if (theftWarnings <= warnings)
    {
        const char *line = cJSON_IsArray(own) ? cJSON_GetStringValue(cJSON_GetArrayItem(own, theftWarnings - 1)) : NULL;
        static const char *defaults[] = { "Do not touch that!", "Stop that, or else!", "Leave that be!" };
        world_bark(witness, line ? line : defaults[(theftWarnings - 1) % 3], 3000);
        return;
    }
    vitals_bark(id_of(witness), 5);
    attack_player(witness, 1);
}

/* ---------------- Things: struck, broken, blown up (ObjectCombat / Mechanisms) ---------------- */

enum { TYPE_DOOR = 3, TYPE_KEY = 10, TYPE_LOCKPICK = 11, TYPE_WINDOW = 12, TYPE_GLASS = 19, TYPE_EXPLOSIVE = 29 };

static cJSON *container_def(int id)
{
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project, "Containers"), "Containers")) if (num(c, "Id", -2) == id) return c;
    return NULL;
}

/* ObjectTypes.CanBeBroken: plants, food, keys, money, walls, stairs... never. */
static int type_can_break(int type)
{
    static const int never[] = { 13, 17, 18, 31, TYPE_KEY, TYPE_LOCKPICK, 14, 5, 6, 1, 2, 24, 23, 15, 16, 20, 34 };
    for (size_t i = 0; i < sizeof never / sizeof never[0]; i++) if (never[i] == type) return 0;
    return 1;
}

static int object_breakable(cJSON *def)
{
    int type = num(def, "Type", 0);
    if (num(def, "HitPoints", 0) <= 0 || flag(def, "CannotBeDestroyed") || !type_can_break(type)) return 0;
    if (type == TYPE_DOOR && (num(get(def, "Door"), "RequiredKeyItemId", -1) >= 0 || flag(get(def, "Door"), "MagicallyLocked"))) return 0;
    return 1;
}

/* Whose it is (its own Belongs To in play, else Belong To / Faction), unless it's the party's or nobody's: 1 = someone else's. */
static int owned_by_other(cJSON *def, int belongsTo, int *owner, int *factionId)
{
    int ownerId = belongsTo >= 0 ? belongsTo : num(def, "BelongToId", -1);
    *owner = -1; *factionId = -1;
    if (ownerId >= 0)
    {
        int actor = world_actor_by_character(ownerId);
        if (actor == 0 || (actor > 0 && in_party(actor)) || items_in_party(ownerId)) return 0;
        *owner = ownerId;
        *factionId = num(character_json(ownerId), "FactionId", -1);
        if (*factionId < 0 && belongsTo < 0) *factionId = num(def, "FactionId", -1);
        return 1;
    }
    int f = num(def, "FactionId", -1);
    if (f < 0 || f == faction_of(0)) return 0;
    *factionId = f;
    return 1;
}

/* Whether blows can break it at all: an object marked breakable (or a keg that goes off), or a destructible container (not a body, not one with its own key). */
int combat_can_break(int uid)
{
    int tc, ti, tq, tx, ty, tin;
    world_thing_info(uid, &tc, &ti, &tq, &tx, &ty, &tin);
    if (tc == CAT_OBJECT)
    {
        cJSON *def = object_def(ti);
        return def && (object_breakable(def) || (num(def, "Type", 0) == TYPE_EXPLOSIVE && flag(get(def, "Explosive"), "ExplodesWhenStruck")));
    }
    cJSON *data = tc == CAT_CONTAINER ? container_def(ti) : NULL;
    return data && flag(data, "Destructible") && !flag(data, "IsBodyContainer") && num(data, "RequiredKeyItemId", -1) < 0;
}

static int struckOwned[64], struckCount;
static int first_strike(int uid)
{
    for (int i = 0; i < struckCount; i++) if (struckOwned[i] == uid) return 0;
    if (struckCount < 64) struckOwned[struckCount++] = uid;
    return 1;
}

static void explode(int uid, cJSON *def, int byPlayer);

/* The blow lands on an object (Game_object::reduce_health). */
static void strike_object(int uid, cJSON *def, int damage, int striker, int byPlayer)
{
    int type = num(def, "Type", 0);
    if (type == TYPE_EXPLOSIVE && flag(get(def, "Explosive"), "ExplodesWhenStruck")) { explode(uid, def, striker <= 0 && byPlayer); return; }
    if ((type == TYPE_WINDOW && world_thing_spent(uid)) || !object_breakable(def)) { world_bark_thing(uid, "no effect", 1000); return; }
    int defense = num(def, "Defense", 0);
    if (defense > 0) damage -= 1 + rnd(defense);
    if (damage <= 0) { world_bark_thing(uid, "no damage", 1000); return; }
    int hp = world_thing_hp(uid, num(def, "HitPoints", 1)) - damage;
    world_thing_set_hp(uid, hp);
    if (flag(get(settingsJson, "Combat"), "ShowHitNumbers")) { char t[16]; snprintf(t, sizeof t, "-%d", damage); world_bark_thing(uid, t, 1000); }
    int owner, factionId, crime = striker <= 0 && byPlayer && owned_by_other(def, world_thing_belongs_to(uid), &owner, &factionId);
    if (crime && first_strike(uid)) combat_theft(owner, factionId);
    if (hp > 0) return;
    /* broken: gone from the world - or a window with a Broken picture, broken where it is */
    if (uid == playerThing) playerThing = -1;
    sound_play(num(def, "DestroyedSoundResourceId", -1));
    events_object_event(uid, 4 /* By Destroy Item */, striker > 0 ? striker : -1);
    cJSON *broken = get(get(def, "Window"), "BrokenSprite");
    char line[160];
    if (type == TYPE_WINDOW && flag(broken, "IsSet"))
    {
        world_thing_break_glass(uid, sprite_find(str(broken, "SpriteGroup"), str(broken, "SpriteName")));
        snprintf(line, sizeof line, "The glass of \"%s\" breaks!", str(def, "Name"));
    }
    else
    {
        int tc, ti, tq, tx, ty, tin, fl = world_thing_floor(uid);
        world_thing_info(uid, &tc, &ti, &tq, &tx, &ty, &tin);
        world_thing_remove(uid);
        int shards = num(get(def, "Glass"), "ShardsObjectId", -1);
        if (type == TYPE_GLASS && shards >= 0) world_thing_put(CAT_OBJECT, shards, 1, tx, ty, fl);
        if (type == TYPE_GLASS) snprintf(line, sizeof line, "\"%s\" shatters!", str(def, "Name"));
        else if (striker > 0) snprintf(line, sizeof line, "%s breaks \"%s\" down!", name_of(striker), str(def, "Name"));
        else snprintf(line, sizeof line, "\"%s\" breaks apart.", str(def, "Name"));
    }
    game_log(line);
    if (crime) combat_theft(owner, factionId);
}

/* One blow at a thing (Game_object::attacked): it always hits. Only the main character's blows are a crime. */
void combat_strike_thing(int uid, int striker)
{
    if (striker < 0 || !world_thing_there(uid)) return;
    int str3 = stat(striker, K_STRENGTH, 10) / 3, damage = (str3 > 0 ? 1 + rnd(str3) : 0) + 1; /* + 1: bare hands */
    int tc, ti, tq, tx, ty, tin;
    world_thing_info(uid, &tc, &ti, &tq, &tx, &ty, &tin);
    if (tc == CAT_OBJECT)
    {
        cJSON *def = object_def(ti);
        if (def) strike_object(uid, def, damage, striker == 0 ? -1 : striker, striker == 0);
        return;
    }
    cJSON *data = tc == CAT_CONTAINER ? container_def(ti) : NULL;
    if (!data || !flag(data, "Destructible") || flag(data, "IsBodyContainer") || num(data, "RequiredKeyItemId", -1) >= 0) { world_bark_thing(uid, "no effect", 1000); return; }
    int defense = num(data, "Defense", 0);
    if (defense > 0) damage -= 1 + rnd(defense);
    if (damage <= 0) { world_bark_thing(uid, "no damage", 1000); return; }
    int hp = world_thing_hp(uid, num(data, "HitPoints", 1) > 1 ? num(data, "HitPoints", 1) : 1) - damage;
    world_thing_set_hp(uid, hp);
    if (flag(get(settingsJson, "Combat"), "ShowHitNumbers")) { char t[16]; snprintf(t, sizeof t, "-%d", damage); world_bark_thing(uid, t, 1000); }
    int owner, factionId, crime = striker == 0 && owned_by_other(data, world_thing_belongs_to(uid), &owner, &factionId);
    if (crime && first_strike(uid)) combat_theft(owner, factionId);
    if (hp > 0) return;
    /* it breaks: its sound, everything inside falls out where it stood */
    if (uid == playerThing) playerThing = -1;
    sound_play(num(data, "DestructibleSoundResourceId", -1));
    int fl = world_thing_floor(uid);
    Item *box = items_unpark(uid, tc, ti, tq);
    int spilled = box ? items_count_in(box, -1, -1) : 0;
    world_thing_remove(uid);
    items_spill(box, tx, ty, fl);
    char line[160];
    if (spilled > 0) snprintf(line, sizeof line, "\"%s\" breaks apart - %d thing%s fall%s out.", str(data, "Name"), spilled, spilled == 1 ? "" : "s", spilled == 1 ? "s" : "");
    else snprintf(line, sizeof line, "\"%s\" breaks apart.", str(data, "Name"));
    game_log(line);
    if (crime) combat_theft(owner, factionId);
}

/* Game_window::double_clicked in combat mode on a thing: attacked - except a body or an unlocked container (used as normal). 1 = attacked. */
int combat_player_thing(int uid)
{
    if (!combatMode) return 0;
    int tc, ti, tq, tx, ty, tin;
    world_thing_info(uid, &tc, &ti, &tq, &tx, &ty, &tin);
    if (tc == CAT_CONTAINER)
    {
        cJSON *data = container_def(ti);
        if (ti < 0 || !data || flag(data, "IsBodyContainer")) return 0;
        Item *box = items_unpark(uid, tc, ti, tq);
        int locked = items_locked(box, flag(data, "IsLocked"));
        items_park(uid, box);
        if (!locked) return 0;
    }
    playerTarget = -1;
    playerThing = uid;
    char line[160];
    snprintf(line, sizeof line, "Attacking %s.", items_name(tc, ti));
    game_log(line);
    return 1;
}

/* ---- Explosives ---- */

typedef struct Fuse { int uid, byPlayer; double left; } Fuse;
static Fuse fuses[32]; static int fuseCount;

static int fuse_of(int uid) { for (int i = 0; i < fuseCount; i++) if (fuses[i].uid == uid) return i; return -1; }

static void set_fuse(int uid, double seconds, int byPlayer)
{
    if (fuse_of(uid) >= 0 || fuseCount >= 32) return; /* already burning */
    fuses[fuseCount++] = (Fuse){ uid, byPlayer, seconds };
}

/* LightFuse: double-clicked (Lit By Double-Click) - its fuse burns, then the blast. */
int combat_light_fuse(int uid)
{
    cJSON *def = object_def(world_thing_object_id(uid)), *e = get(def, "Explosive");
    if (!def || num(def, "Type", 0) != TYPE_EXPLOSIVE) return 0;
    char line[160];
    if (cJSON_IsFalse(get(e, "LitByDoubleClick"))) { snprintf(line, sizeof line, "\"%s\" - nothing happens.", str(def, "Name")); game_log(line); return 1; }
    if (fuse_of(uid) >= 0) return 1;
    double seconds = get(e, "FuseSeconds") ? get(e, "FuseSeconds")->valuedouble : 3;
    set_fuse(uid, seconds > 0.05 ? seconds : 0.05, 1);
    conditions_record_item("ignited", CAT_OBJECT, num(def, "Id", -1));
    sound_play(num(e, "FuseSoundResourceId", -1));
    cJSON *lit = get(e, "LitSprite");
    if (flag(lit, "IsSet")) world_thing_look(uid, sprite_find(str(lit, "SpriteGroup"), str(lit, "SpriteName")));
    snprintf(line, sizeof line, "You light the fuse of \"%s\"!", str(def, "Name"));
    game_log(line);
    return 1;
}

/* The blast: everyone in reach hurt, breakable things struck, other explosives set off. */
static void explode(int uid, cJSON *def, int byPlayer)
{
    if (!world_thing_there(uid)) return;
    cJSON *e = get(def, "Explosive");
    int f = fuse_of(uid);
    if (f >= 0) fuses[f] = fuses[--fuseCount];
    double cx, cy;
    world_thing_center(uid, &cx, &cy);
    int tc, ti, tq, tx, ty, tin, owner, factionId;
    world_thing_info(uid, &tc, &ti, &tq, &tx, &ty, &tin);
    events_object_event(uid, 4 /* By Destroy Item */, -1);
    int crime = byPlayer && owned_by_other(def, world_thing_belongs_to(uid), &owner, &factionId);
    cJSON *blast = get(e, "ExplosionSprite");
    if (flag(blast, "IsSet")) world_effect_once(sprite_find(str(blast, "SpriteGroup"), str(blast, "SpriteName")), tx, ty);
    sound_play(num(e, "ExplosionSoundResourceId", -1));
    world_thing_remove(uid);
    double reach = num(e, "RadiusTiles", 5) * 8.0;
    int lo = num(e, "DamageMin", 8), hi = num(e, "DamageMax", 20);
    for (int a = 0; a < world_actor_count() && a < 256; a++)
    {
        if (dead(a)) continue;
        double px, py;
        world_actor_pixel(a, &px, &py);
        if (fmax(fabs(px - cx), fabs(py - cy)) > reach) continue;
        vitals_damage(id_of(a), lo + rnd((hi - lo > 0 ? hi - lo : 0) + 1), "an explosion");
        if (byPlayer && a != 0 && !dead(a)) fight_back(a, 0);
    }
    int near[256];
    int n = world_things_near(cx, cy, reach, near, 256);
    for (int i = 0; i < n; i++)
    {
        cJSON *other = object_def(world_thing_object_id(near[i]));
        if (!other || !world_thing_there(near[i])) continue;
        if (num(other, "Type", 0) == TYPE_EXPLOSIVE) { set_fuse(near[i], 0.25, byPlayer); continue; }
        if (object_breakable(other)) strike_object(near[i], other, lo + rnd((hi - lo > 0 ? hi - lo : 0) + 1), -1, byPlayer);
    }
    char line[160];
    snprintf(line, sizeof line, "\"%s\" explodes!", str(def, "Name"));
    game_log(line);
    if (crime) combat_theft(owner, factionId);
}

static void update_fuses(double ms)
{
    for (int i = 0; i < fuseCount; i++)
    {
        if ((fuses[i].left -= ms / 1000) > 0) continue;
        Fuse f = fuses[i];
        fuses[i] = fuses[--fuseCount];
        i--;
        cJSON *def = object_def(world_thing_object_id(f.uid));
        if (def && num(def, "Type", 0) == TYPE_EXPLOSIVE) explode(f.uid, def, f.byPlayer);
    }
}

/* ---- Attack Object (the event command): blows at a fight's pace till it's gone; a minute and it still stands: given up ---- */

typedef struct ThingAttack { int uid, dex, x, y; double elapsed, untilTick; } ThingAttack;
static ThingAttack thingAttacks[256];

void combat_attack_thing_begin(int actor, int uid)
{
    if (actor < 0 || actor >= 256) return;
    ThingAttack *t = &thingAttacks[actor];
    int tc, ti, tq, tin;
    memset(t, 0, sizeof *t);
    t->uid = uid;
    world_thing_info(uid, &tc, &ti, &tq, &t->x, &t->y, &tin);
}

/* 1 still at it, 0 it's broken, -1 given up. */
int combat_attack_thing_tick(int actor, double ms)
{
    ThingAttack *t = &thingAttacks[actor];
    if (!world_thing_there(t->uid) || world_thing_spent(t->uid)) { world_set_pose(actor, "Idle", -1); return 0; }
    t->elapsed += ms;
    if (t->elapsed > 60000)
    {
        char line[200];
        int tc, ti, tq, tx, ty, tin;
        world_thing_info(t->uid, &tc, &ti, &tq, &tx, &ty, &tin);
        snprintf(line, sizeof line, "%s gives up on \"%s\" - a minute of blows and it still stands.", name_of(actor), items_name(tc, ti));
        game_log(line);
        world_set_pose(actor, "Idle", -1);
        return -1;
    }
    if ((t->untilTick -= ms) > 0) return 1;
    t->untilTick = STD_DELAY;
    if (t->dex < DEX_TO_ATTACK) { int dex = stat(actor, K_DEXTERITY, 10); t->dex += dex > 1 ? dex : 1; return 1; }
    t->dex -= DEX_TO_ATTACK;
    world_face_cell(actor, t->x, t->y, "Attacking Melee");
    pose_back(actor, 2 * STD_DELAY);
    combat_strike_thing(t->uid, actor);
    return 1;
}

void combat_save(cJSON *runtime) { cJSON_AddBoolToObject(runtime, "CombatMode", combatMode); }
void combat_restore(cJSON *runtime) { combatMode = cJSON_IsTrue(cJSON_GetObjectItem(runtime, "CombatMode")); }
