#include "events.h"
#include "commands.h"
#include "dialog.h"
#include "world.h"
#include "items.h"
#include "script.h"
#include "lighting.h"
#include "schedules.h"
#include "sound.h"
#include "weather.h"
#include "conditions.h"
#include "screen.h"
#include <math.h>
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

double sc_strtod(const char *text, char **end);

/* ---------------- What the game holds ---------------- */

typedef struct Cmd { int type, indent; cJSON *params; } Cmd;
typedef struct List { Cmd *cmds; int count; } List;

typedef struct Page { List list; cJSON *cond; int fired, met; } Page;

/* Something with event pages: a map event, or a character (its Talk commands too). */
typedef struct Owner
{
    const char *name;
    int isMapEvent, x, y, floor;
    int characterId;
    int itemCategory, itemId;   /* a container's or book's own pages (By Pickup / Drop Item) */
    Page *pages; int pageCount;
    List talk;
    int eventId;                /* a map event's Id */
    cJSON *data;                /* the character's (or item's) own data */
} Owner;

static Owner *owners; static int ownerCount;

typedef struct VarDef { int id, type; const char *name; } VarDef;
static VarDef *varDefs; static int varDefCount;

static cJSON *playerCharacter; /* the main player's character (its stats) */
static cJSON *statsJson, *currenciesJson, *objectsJson;

/* ---------------- The game clock (GameClock: Exult's) ---------------- */

#define TICKS_PER_MINUTE 25   /* Exult: 25 ticks a game minute */
#define STD_DELAY_MS 200      /* a tick every standard delay (x the time rate): a game minute every 5 seconds */

static int clockDay, clockHour = 6, clockMinute, clockTicks, timeRate = 1;
static int ambientPlaying = -2; /* the map's ambient sound playing (-2: nothing decided yet) */
static int ambientDawn = -1, ambientDay = -1, ambientDusk = -1, ambientNight = -1;
static double dawnHour = 5, dayHour = 7, duskHour = 19, nightHour = 21;

/* The map's ambient sound (Edit > Map Ambient Sounds): the one for this part of the day loops, giving way to the next when its hour comes. */
static void update_ambient(void)
{
    double h = clockHour + clockMinute / 60.0;
    int sound = h >= dawnHour && h < dayHour ? ambientDawn : h >= dayHour && h < duskHour ? ambientDay : h >= duskHour && h < nightHour ? ambientDusk : ambientNight;
    if (sound == ambientPlaying) return;
    ambientPlaying = sound;
    sound_stop_owner(OWNER_AMBIENT);
    if (sound >= 0) sound_play_ex(sound, -1, 1, OWNER_AMBIENT, 1);
}
static double clockMs;

/* Returns how many hours went by (Exult re-schedules everyone on the hour). */
static int clock_advance(double ms)
{
    int hours = 0;
    clockMs += ms;
    while (clockMs >= STD_DELAY_MS)
    {
        clockMs -= STD_DELAY_MS;
        clockTicks += timeRate;
        clockMinute += clockTicks / TICKS_PER_MINUTE;
        clockTicks %= TICKS_PER_MINUTE;
        while (clockMinute >= 60) { clockMinute -= 60; if (++clockHour >= 24) { clockHour -= 24; clockDay++; } hours++; }
    }
    return hours;
}

/* Lights burn down by the game clock. */
static long burnedTo = -1;
static void burn_lights(void)
{
    long now = (long)clockDay * 1440 + clockHour * 60 + clockMinute;
    if (burnedTo < 0) burnedTo = now;
    if (now <= burnedTo) return;
    world_burn_lights((double)(now - burnedTo));
    items_burn_lights((double)(now - burnedTo));
    burnedTo = now;
}

/* Time passes at once (Set Time, resting). */
static int clock_advance_minutes(int minutes)
{
    int hours = 0;
    clockMinute += minutes > 0 ? minutes : 0;
    while (clockMinute >= 60) { clockMinute -= 60; if (++clockHour >= 24) { clockHour -= 24; clockDay++; } hours++; }
    return hours;
}

static long total_minutes(void) { return (long)clockDay * 1440 + clockHour * 60 + clockMinute; }

int events_hour(void) { return clockHour; }
int events_minute(void) { return clockMinute; }

/* Common Events (System Settings): a named list of commands, run from anywhere with Call Common Event. */
typedef struct Common { int id; const char *name; cJSON *parameters; List list; } Common;
static Common *commons; static int commonCount;

/* How many times each reply was picked (a reply's Cycle). */
typedef struct Picked { const Cmd *showText; int replyId, count; } Picked;
static Picked *picked; static int pickedCount, pickedCap;

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static double dbl(cJSON *o, const char *k) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valuedouble : 0; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }
static const char *param(const Cmd *c, const char *k) { return str(c->params, k); }

/* ---------------- Variables and switches (one store by name, as the engine's) ---------------- */

static double get_var(const char *name) { return script_get_number(name); }
static void set_var(const char *name, double value) { script_set_number(name, value); }

static int parse_number(const char *s, double *out)
{
    char *end;
    while (*s == ' ') s++;
    double v = sc_strtod(s, &end);
    if (end == s) return 0;
    while (*end == ' ') end++;
    if (*end) return 0;
    *out = v;
    return 1;
}

/* bool.TryParse: "True" / "False", any case. */
static int parse_bool(const char *s, int *out)
{
    char t[8]; int n = 0;
    while (*s == ' ') s++;
    for (; *s && n < 7; s++) t[n++] = (char)(*s >= 'A' && *s <= 'Z' ? *s + 32 : *s);
    while (n && t[n - 1] == ' ') n--;
    t[n] = 0;
    if (strcmp(t, "true") == 0) { *out = 1; return 1; }
    if (strcmp(t, "false") == 0) { *out = 0; return 1; }
    *out = 0;
    return 0;
}

/* ---------------- Reading the game ---------------- */

static List read_list(cJSON *array)
{
    List l = { NULL, 0 };
    int n = cJSON_GetArraySize(array);
    if (n <= 0) return l;
    l.cmds = (Cmd *)calloc((size_t)n, sizeof(Cmd));
    cJSON *c;
    cJSON_ArrayForEach(c, array)
    {
        Cmd *cmd = &l.cmds[l.count++];
        cmd->type = num(c, "Type", -1); cmd->indent = num(c, "IndentLevel", 0); cmd->params = get(c, "Parameters");
    }
    return l;
}

static void read_pages(Owner *o, cJSON *pages)
{
    o->pageCount = cJSON_GetArraySize(pages);
    o->pages = (Page *)calloc((size_t)(o->pageCount > 0 ? o->pageCount : 1), sizeof(Page));
    int i = 0;
    cJSON *p;
    cJSON_ArrayForEach(p, pages) { o->pages[i].cond = get(p, "Conditions"); o->pages[i].list = read_list(get(p, "Commands")); i++; }
}

void events_load(void)
{
    cJSON *root = (cJSON *)world_json(), *project = get(root, "Project"), *map = get(project, "Map");
    cJSON *events = get(map, "Events"), *characters = get(get(project, "Characters"), "Characters"), *e;
    cJSON *containers = get(get(project, "Containers"), "Containers"), *books = get(get(root, "Books"), "Books");
    owners = (Owner *)calloc((size_t)(cJSON_GetArraySize(events) + cJSON_GetArraySize(characters) + cJSON_GetArraySize(containers) + cJSON_GetArraySize(books) + 1), sizeof(Owner));
    cJSON_ArrayForEach(e, events)
    {
        Owner *o = &owners[ownerCount++];
        o->name = str(e, "Name"); o->isMapEvent = 1; o->eventId = num(e, "Id", 0); o->data = e;
        o->x = num(e, "X", 0); o->y = num(e, "Y", 0); o->floor = num(e, "Floor", 1);
        o->characterId = -1;
        read_pages(o, get(e, "EventPages"));
    }
    cJSON_ArrayForEach(e, characters)
    {
        Owner *o = &owners[ownerCount++];
        o->name = str(e, "Name"); o->characterId = num(e, "Id", -1); o->data = e;
        read_pages(o, get(e, "EventPages"));
        o->talk = read_list(get(e, "TalkCommands"));
        if (flag(e, "IsTestPlayer")) playerCharacter = e;
    }
    cJSON *objectList = get(get(project, "Objects"), "Objects");
    owners = (Owner *)realloc(owners, sizeof(Owner) * (size_t)(ownerCount + cJSON_GetArraySize(containers) + cJSON_GetArraySize(books) + cJSON_GetArraySize(objectList) + 1));
    cJSON_ArrayForEach(e, objectList)
    {
        if (!cJSON_GetArraySize(get(e, "EventPages"))) continue;
        Owner *o = &owners[ownerCount++];
        memset(o, 0, sizeof *o);
        o->name = str(e, "Name"); o->characterId = -1; o->data = e;
        o->itemCategory = CAT_OBJECT; o->itemId = num(e, "Id", -1);
        read_pages(o, get(e, "EventPages"));
    }
    for (int pass = 0; pass < 2; pass++)
        for (cJSON *source = pass == 0 ? containers : books; source; source = NULL)
        cJSON_ArrayForEach(e, source)
        {
            Owner *o = &owners[ownerCount++];
            memset(o, 0, sizeof *o);
            o->name = str(e, "Name"); o->characterId = -1;
            o->itemCategory = pass == 0 ? CAT_CONTAINER : CAT_BOOK; o->itemId = num(e, "Id", -1);
            read_pages(o, get(e, "EventPages"));
        }
    items_load();
    lighting_load();
    cJSON *common = get(get(project, "Settings"), "CommonEvents");
    commons = (Common *)calloc((size_t)cJSON_GetArraySize(common) + 1, sizeof(Common));
    cJSON_ArrayForEach(e, common) commons[commonCount++] = (Common){ num(e, "Id", -1), str(e, "Name"), get(e, "Parameters"), read_list(get(e, "Commands")) };
    statsJson = get(get(project, "Stats"), "Stats");
    cJSON *clock = get(get(project, "Settings"), "Clock");
    clockHour = num(clock, "StartHour", 6); if (clockHour < 0) clockHour = 0; if (clockHour > 23) clockHour = 23;
    timeRate = num(clock, "TimeRate", 1); if (timeRate < 1) timeRate = 1;
    cJSON *ambient = get(map, "Ambient"), *dn = get(get(project, "Settings"), "DayNight");
    ambientDawn = num(ambient, "DawnSoundId", -1); ambientDay = num(ambient, "DaySoundId", -1);
    ambientDusk = num(ambient, "DuskSoundId", -1); ambientNight = num(ambient, "NightSoundId", -1);
    dawnHour = num(dn, "DawnStartHour", 5); dayHour = num(dn, "DayStartHour", 7); duskHour = num(dn, "DuskStartHour", 19); nightHour = num(dn, "NightStartHour", 21);
    currenciesJson = get(get(project, "Currencies"), "Currencies");
    objectsJson = get(get(project, "Objects"), "Objects");
    cJSON *defs = get(get(project, "Variables"), "Variables");
    varDefs = (VarDef *)calloc((size_t)(cJSON_GetArraySize(defs) + 1), sizeof(VarDef));
    cJSON_ArrayForEach(e, defs) varDefs[varDefCount++] = (VarDef){ num(e, "Id", -1), num(e, "Type", 0), str(e, "Name") };
    /* System Settings > Conversations: the replies under the main character, as Exult. */
    cJSON *conversation = get(get(project, "Settings"), "Conversation");
    dialog_set_replies_apart(flag(conversation, "RepliesUnderMainCharacter"), world_actor_face(0));
}

/* ---------------- Conditions (BookEventConditionEvaluator) ---------------- */

enum { T_ALWAYS = 0, T_HAVING = 7, T_NOT_HAVING = 8, T_ENTER = 9, T_STAY = 10, T_EXIT = 11 };
enum { CMP_EQ, CMP_NE, CMP_GT, CMP_GE, CMP_LT, CMP_LE };

static int compare_numbers(double a, double b, int op)
{
    switch (op)
    {
        case CMP_EQ: return a == b; case CMP_NE: return a != b; case CMP_GT: return a > b;
        case CMP_GE: return a >= b; case CMP_LT: return a < b; case CMP_LE: return a <= b;
    }
    return 0;
}

/* What the engine's condition context holds for a variable: "True"/"False" for a Boolean, else the number as invariant text. */
static void variable_text(int id, char *out, size_t size)
{
    for (int i = 0; i < varDefCount; i++)
        if (varDefs[i].id == id)
        {
            double v = get_var(varDefs[i].name);
            if (varDefs[i].type == 4) snprintf(out, size, "%s", v != 0 ? "True" : "False");
            else if (v == (double)(long)v && v < 1e15 && v > -1e15) snprintf(out, size, "%ld", (long)v);
            else snprintf(out, size, "%.15g", v);
            return;
        }
    snprintf(out, size, "0");
}

static int one_variable_condition(cJSON *entry)
{
    char actual[64];
    variable_text(num(entry, "VariableId", -1), actual, sizeof actual);
    const char *wanted = str(entry, "Value");
    int op = num(entry, "Comparison", 0), mode = num(entry, "Mode", 0);
    if (mode == 1) /* String: ordinal */
    {
        int c = strcmp(actual, wanted);
        return compare_numbers(c, 0, op);
    }
    if (mode == 2) /* Boolean */
    {
        int a, b;
        parse_bool(actual, &a); parse_bool(wanted, &b);
        return op == CMP_NE ? a != b : a == b;
    }
    double a = 0, b = 0;
    if (!parse_number(actual, &a)) a = 0; /* "True" doesn't read as a number: 0, as double.TryParse */
    if (!parse_number(wanted, &b)) b = 0;
    return compare_numbers(a, b, op);
}

static int variable_conditions(cJSON *entries)
{
    int n = cJSON_GetArraySize(entries);
    if (n == 0) return 1;
    int result = one_variable_condition(cJSON_GetArrayItem(entries, 0));
    for (int i = 1; i < n; i++)
    {
        int next = one_variable_condition(cJSON_GetArrayItem(entries, i));
        result = num(cJSON_GetArrayItem(entries, i - 1), "NextOperator", 0) == 0 ? result && next : result || next;
    }
    return result;
}

static int player_stat(int statId)
{
    char key[16];
    snprintf(key, sizeof key, "%d", statId);
    const char *text = str(get(playerCharacter, "StatValues"), key);
    double v;
    return text[0] && parse_number(text, &v) && v == (double)(int)v ? (int)v : 0;
}

static int one_stat(cJSON *entry)
{
    if (!flag(entry, "IsActive")) return 1;
    return compare_numbers(player_stat(num(entry, "StatId", 0)), num(entry, "Value", 0), num(entry, "Comparison", 0));
}

static int warned[256];
static uint32_t screenNow;
static const char *evaluatingName = ""; /* whose event's conditions are being looked at ("this event's page N") */
static int lastResult = 1;             /* whether the last command that can fail worked */

static void log_once(int key, const char *text)
{
    if (key < 0 || key >= 256 || warned[key]) return;
    warned[key] = 1;
    game_log(text);
}

/* trigger: the page's own trigger met (TriggerActuallyOccurred); just: what happened. */
static int sub_condition(cJSON *sub, int just)
{
    int raw;
    switch (num(sub, "Type", 0))
    {
        case 0: raw = one_variable_condition(get(sub, "InACondition")); break;
        case 1: raw = 0 == flag(sub, "EventCompletedExpectedState"); break;
        case 2: raw = one_stat(get(sub, "StatCondition")); break;
        case 3: raw = flag(get(sub, "CombineItemSlot1"), "IsSelf") && flag(get(sub, "CombineItemSlot2"), "IsSelf"); break;
        case 4: raw = 0; break;                    /* no item dropped on it */
        case 5: raw = 1; break;                    /* the distance the engine assumes: 0 <= 32 */
        case 6: raw = 1; break;                    /* Always */
        case 8: raw = just == 2; break;            /* By Use Item */
        case 15: raw = conditions_test(get(sub, "Game"), evaluatingName); break;
        default: raw = 0; break;                   /* the other item triggers: they don't happen in the player yet */
    }
    return flag(sub, "Invert") ? !raw : raw;
}

static int complex_conditions(cJSON *groups, int just)
{
    cJSON *g;
    cJSON_ArrayForEach(g, groups)
    {
        cJSON *subs = get(g, "SubConditions");
        int n = cJSON_GetArraySize(subs);
        if (n == 0) continue;
        int result = sub_condition(cJSON_GetArrayItem(subs, 0), just);
        for (int i = 1; i < n; i++)
        {
            int next = sub_condition(cJSON_GetArrayItem(subs, i), just);
            switch (num(cJSON_GetArrayItem(subs, i - 1), "NextOperator", 0))
            {
                case 1: result = result || next; break;
                case 2: result = result ^ next; break;
                default: result = result && next; break;
            }
        }
        if (!result) return 0;
    }
    return 1;
}

static int conditions_met(cJSON *c, int just, int timesSoFar, int triggerOverride)
{
    int result = 1, trigger = num(c, "TriggerConditionBased", 0);
    if (trigger != T_ALWAYS) result &= triggerOverride >= 0 ? triggerOverride : trigger == just;
    if (flag(c, "ConditionInACondition")) result &= variable_conditions(get(c, "VariableConditions"));
    if (flag(c, "ConditionEventTriggerCompleted")) result &= 0 == flag(c, "EventCompletedExpectedState");
    if (flag(c, "ConditionStatCondition")) { cJSON *s; cJSON_ArrayForEach(s, get(c, "StatConditions")) result &= one_stat(s); }
    if (flag(c, "ConditionCombineItem")) result &= flag(get(c, "CombineItemSlot1"), "IsSelf") && flag(get(c, "CombineItemSlot2"), "IsSelf");
    if (flag(c, "ConditionDropToSelf")) result &= 0;
    if (flag(c, "ConditionDistance")) result &= 0 <= dbl(c, "DistanceValue");
    if (flag(c, "ConditionComplex")) result &= complex_conditions(get(c, "ComplexConditions"), just);
    switch (num(c, "CycleCondition", 0))
    {
        case 0: result &= timesSoFar == 0; break;
        case 1: result &= timesSoFar < num(c, "CycleSpecificTimes", 0); break;
        case 2: /* Until Condition Met: it keeps going while its Until conditions aren't all met yet */
            if (cJSON_GetArraySize(get(c, "UntilConditions")) > 0) result &= !conditions_all(get(c, "UntilConditions"), evaluatingName);
            break;
        default: break;
    }
    return result;
}

/* ---------------- Running commands ---------------- */

enum { W_NONE, W_TIMER, W_CHOICE, W_READ, W_WALK, W_ACTION, W_UNTIL, W_YIELD, W_SCREEN };
enum { A_PICKUP = 1, A_DROP, A_SIT, A_POSE };

typedef struct Loop { const Cmd *opener; int count, total; } Loop;

/* A command list being run - the event's own, or a Common Event it called (Root: the event's own). */
typedef struct CallFrame
{
    const List *list;
    int pc;
    int returns[16][2], returnCount;
    Loop loops[16]; int loopCount;
} CallFrame;

#define MAX_CALL_DEPTH 40

typedef struct Run
{
    int used;
    const List *list;
    int pc;
    Loop loops[16]; int loopCount;
    CallFrame *callers; int depth;       /* the lists that called Common Events, innermost last */
    char owner[48];                  /* whose event it is (self switches): "event:3", "char:2", "global" */
    int ownerUid;                    /* an object's placed copy whose event it is (Sleep On Bed "Under") */
    double screenMs, screenTotal; int cameraBack; double camFromX, camFromY, camToX, camToY; int camera; /* Fade Screen / Move Camera running */
    const Cmd *untilCmd; double untilWaited, untilTimeout; /* Wait Until */
    int activator, byStepping;   /* whoever set it off (an actor; -1 = the player), and whether by walking over it */
    int returns[16][2], returnCount;
    int waiting; double timerMs;
    int walker, goalX, goalY, goalFloor, tries, best, beside; /* Walk To Location; beside: up to a thing */
    long waitFromMinute; int waitMinutes;  /* Wait in game minutes / hours: from when, how long */
    int act, stage, thing, actX, actY, category, itemId, quantity, atSpot; double stageMs; /* Pickup / Drop Item */
    int cinema;
    /* a Show Text with replies waiting: where it is, where its branches end */
    int choiceAt, afterBranches, choicePinned[DIALOG_MAX_REPLIES], choiceIds[DIALOG_MAX_REPLIES], choiceCount;
} Run;

#define MAX_RUNS 32
static Run runs[MAX_RUNS];

/* Characters an event moved, whose event is still going: they stay where it left them (no schedule) till it's over. */
static int heldBy[256]; /* per actor: the run's number + 1, 0 = free */

static void hold(Run *r, int actor) { if (actor > 0 && actor < 256) heldBy[actor] = (int)(r - runs) + 1; }

/* ReleaseHeldBy: everyone the run held goes back to their schedule. */
static void release_held(Run *r)
{
    for (int a = 1; a < 256; a++) if (heldBy[a] == (int)(r - runs) + 1) { heldBy[a] = 0; schedules_let_go(a); }
}

int events_actor_held(int actor) { return actor > 0 && actor < 256 && heldBy[actor]; }

/* Carrying out an event's command (walking there, picking up): its schedule waits. */
int events_actor_busy(int actor)
{
    for (int i = 0; i < MAX_RUNS; i++) if (runs[i].used && (runs[i].waiting == W_WALK || runs[i].waiting == W_ACTION) && runs[i].walker == actor) return 1;
    return 0;
}

int events_cinema(void)
{
    for (int i = 0; i < MAX_RUNS; i++) if (runs[i].used && runs[i].cinema) return 1;
    return 0;
}

static void continue_run(Run *r);

static int find_else_or_end(const List *l, int at)
{
    int indent = l->cmds[at].indent;
    for (int j = at + 1; j < l->count; j++)
        if (l->cmds[j].indent == indent && (l->cmds[j].type == C_Else || l->cmds[j].type == C_EndBranch)) return j;
    return l->count - 1;
}

static int find_end_branch(const List *l, int at)
{
    int indent = l->cmds[at].indent;
    for (int j = at + 1; j < l->count; j++)
        if (l->cmds[j].indent == indent && l->cmds[j].type == C_EndBranch) return j;
    return l->count - 1;
}

static int find_end_choices(const List *l, int showText)
{
    int indent = l->cmds[showText].indent;
    if (showText + 1 >= l->count || l->cmds[showText + 1].type != C_WhenChoice || l->cmds[showText + 1].indent != indent) return -1;
    for (int j = showText + 1; j < l->count; j++)
    {
        if (l->cmds[j].indent < indent) return -1;
        if (l->cmds[j].indent == indent && l->cmds[j].type == C_EndChoices) return j;
    }
    return -1;
}

static int find_when(const List *l, int showText, int id)
{
    int end = find_end_choices(l, showText);
    if (end < 0) return -1;
    char key[16];
    snprintf(key, sizeof key, "%d", id);
    for (int j = showText + 1; j < end; j++)
        if (l->cmds[j].indent == l->cmds[showText].indent && l->cmds[j].type == C_WhenChoice && strcmp(param(&l->cmds[j], "ChoiceId"), key) == 0) return j;
    return -1;
}

static int find_branch_end(const List *l, int when)
{
    for (int j = when + 1; j < l->count; j++)
        if (l->cmds[j].indent == l->cmds[when].indent && (l->cmds[j].type == C_WhenChoice || l->cmds[j].type == C_EndChoices)) return j;
    return l->count;
}

static int find_end_choices_from(const List *l, int from)
{
    for (int j = from + 1; j < l->count; j++)
        if (l->cmds[j].indent == l->cmds[from].indent && l->cmds[j].type == C_EndChoices) return j;
    return l->count - 1;
}

static int branch_true(const Cmd *c)
{
    if (c->type == C_ConditionalBranchSwitch)
    {
        int expected;
        parse_bool(param(c, "Value"), &expected);
        return (get_var(param(c, "SwitchName")) != 0) == expected;
    }
    if (c->type == C_AdvancedIf)
    {
        cJSON *condition = cJSON_Parse(param(c, "Condition"));
        int result = conditions_test(condition, evaluatingName);
        cJSON_Delete(condition);
        return result;
    }
    double actual = get_var(param(c, "VariableName")), target = 0;
    parse_number(param(c, "Value"), &target);
    const char *op = param(c, "Comparison");
    while (*op == ' ') op++;
    if (strncmp(op, "==", 2) == 0) return actual == target;
    if (strncmp(op, ">=", 2) == 0) return actual >= target;
    if (strncmp(op, "<=", 2) == 0) return actual <= target;
    if (strncmp(op, "!=", 2) == 0) return actual != target;
    if (op[0] == '>') return actual > target;
    if (op[0] == '<') return actual < target;
    return actual == target;
}

/* "Player", "Activator" (whoever set it off; the player when nobody), or a character's id: the actor (-1 when not here). */
static int actor_for(const Run *r, const char *who)
{
    if (strcmp(who, "Player") == 0) return 0;
    if (!who[0] || strcmp(who, "Activator") == 0) return r->activator >= 0 ? r->activator : 0;
    return world_actor_by_character(atoi(who));
}

/* A Show Text's box (BuildMessage); 0 when it isn't shown (no words and no replies, narration for a character walking past, a speaker out of sight). */
static int build_box(const Run *r, const Cmd *c, DialogBox *box, int withReplies)
{
    memset(box, 0, sizeof *box);
    box->text = param(c, "Text");
    box->title = param(c, "Title");
    if (!box->text[0] && !box->title[0] && !withReplies) return 0;
    const char *key = param(c, "SpeakerId");
    int speaker = -1, onMap = -1;
    if (!key[0]) { if (r->byStepping) return 0; }
    else if (strcmp(key, "Self") == 0) { speaker = r->activator >= 0 ? r->activator : 0; onMap = r->activator; }
    else if (strcmp(key, "Player") == 0 || atoi(key) == world_actor_character(0)) speaker = 0;
    else
    {
        speaker = onMap = world_actor_by_character(atoi(key));
        if (speaker < 0) { game_log("A speaker isn't on this map - their line isn't shown."); return 0; }
    }
    if (onMap > 0 && !world_actor_in_sight(onMap)) return 0; /* not in sight: their line isn't shown */
    box->face = speaker >= 0 ? world_actor_face(speaker) : NULL;
    const char *frame = "", *bg = param(c, "Background");
    int dim = 0;
    if (get(c->params, "Frame")) { frame = param(c, "Frame"); dim = strcmp(param(c, "Dim"), "True") == 0; }
    else if (strcmp(bg, "Dim") == 0) { frame = "None"; dim = 1; }
    else if (strcmp(bg, "Transparent") == 0) frame = "None";
    box->frame = sprite_dialog_frame(frame);
    box->dim = dim;
    const char *pos = param(c, "Position");
    box->position = strcmp(pos, "Top") == 0 ? 0 : strcmp(pos, "Middle") == 0 ? 1 : 2;
    box->faceRight = strcmp(param(c, "FaceSide"), "Right") == 0;
    return 1;
}

static Picked *picked_of(const Cmd *showText, int id)
{
    for (int i = 0; i < pickedCount; i++) if (picked[i].showText == showText && picked[i].replyId == id) return &picked[i];
    if (pickedCount == pickedCap) { pickedCap = pickedCap ? pickedCap * 2 : 32; picked = (Picked *)realloc(picked, sizeof(Picked) * (size_t)pickedCap); }
    picked[pickedCount] = (Picked){ showText, id, 0 };
    return &picked[pickedCount++];
}

static void end_run(Run *r) { r->used = 0; r->cinema = 0; free(r->callers); r->callers = NULL; r->depth = 0; release_held(r); }

/* ---------------- Formulas in commands ---------------- */

/* A command's formula box: its value (a box set to "Words": the words as typed). Errors go to Game.log; the value is then 0. */
static Value formula(const Cmd *c, const char *key)
{
    char mode[64];
    snprintf(mode, sizeof mode, "%sMode", key);
    if (strcmp(param(c, mode), "Words") == 0) return value_text(param(c, key));
    Value v;
    char error[200];
    if (!script_eval(param(c, key), &v, error, sizeof error))
    {
        char line[320];
        snprintf(line, sizeof line, "Script error in %s: %s", c->type >= 0 && c->type < C_COUNT ? COMMAND_NAMES[c->type] : "a formula", error);
        game_log(line);
        return value_number(0);
    }
    return v;
}

static double formula_number(const Cmd *c, const char *key) { return value_number_of(formula(c, key)); }

static Value value_of_text(const char *f)
{
    Value v;
    char error[200];
    if (!script_eval(f, &v, error, sizeof error))
    {
        char line[320];
        snprintf(line, sizeof line, "Script error in Formula: %s", error);
        game_log(line);
        return value_number(0);
    }
    return v;
}

/* While / Wait Until: its formula, or a game condition (Advance IF's - not in the player yet). */
static int condition_of(const Cmd *c)
{
    if (strcmp(param(c, "Mode"), "Game") == 0) { log_once(205, "A While / Wait Until on a game condition isn't in the player yet (taken as false)."); return 0; }
    return value_truth(formula(c, "Formula"));
}

/* Sets a variable if a name was given (an optional counter left empty is skipped). */
static void set_named(const char *name, Value v)
{
    const char *t = name;
    while (*t == ' ') t++;
    if (*t) script_set(name, v);
}

static void reply_chosen(void *context, int id)
{
    Run *r = (Run *)context;
    if (!r->used || r->waiting != W_CHOICE) return;
    const List *l = r->list;
    picked_of(&l->cmds[r->choiceAt], id)->count++;
    r->waiting = W_NONE;
    int when = find_when(l, r->choiceAt, id);
    if (when < 0) { r->pc = r->afterBranches; continue_run(r); return; }
    for (int i = 0; i < r->choiceCount; i++)
        if (r->choiceIds[i] == id && r->choicePinned[i] && r->returnCount < 16)
        {
            /* Pinned: when its branch runs out, back to the same box. */
            r->returns[r->returnCount][0] = find_branch_end(l, when);
            r->returns[r->returnCount++][1] = r->choiceAt;
        }
    r->pc = when + 1;
    continue_run(r);
}

static void read_done(void *context)
{
    Run *r = (Run *)context;
    if (!r->used || r->waiting != W_READ) return;
    r->waiting = W_NONE;
    continue_run(r);
}

/* A Show Text with replies: 1 when it waits for the player's pick. */
static int ask_choice(Run *r, int at)
{
    const List *l = r->list;
    const Cmd *c = &l->cmds[at];
    int end = find_end_choices(l, at);
    r->afterBranches = end >= 0 ? end + 1 : at + 1;
    cJSON *choices = cJSON_Parse(param(c, "Choices")), *ch;
    DialogBox box;
    int n = 0;
    r->choiceCount = 0;
    const char *texts[DIALOG_MAX_REPLIES];
    cJSON_ArrayForEach(ch, choices)
    {
        if (n >= DIALOG_MAX_REPLIES) break;
        int id = num(ch, "Id", 0);
        cJSON *cond = get(ch, "Condition");
        if (cJSON_IsObject(cond))
        {
            int t = num(cond, "TriggerConditionBased", 0);
            cJSON *ref = get(cond, "TriggerItemReference");
            int has = cJSON_IsObject(ref) && num(ref, "ItemId", -1) >= 0 && items_party_has(num(ref, "Category", -1), num(ref, "ItemId", -1));
            int override = t == T_HAVING ? has : t == T_NOT_HAVING ? !has : -1;
            if (!conditions_met(cond, T_ALWAYS, picked_of(c, id)->count, override)) continue;
        }
        texts[n] = str(ch, "Text");
        r->choiceIds[n] = id;
        r->choicePinned[n] = flag(ch, "Pinned");
        n++;
    }
    r->choiceCount = n;
    if (n == 0)
    {
        /* nothing to answer: just the box */
        if (build_box(r, c, &box, 0)) dialog_queue(&box);
        cJSON_Delete(choices);
        return 0;
    }
    if (!build_box(r, c, &box, 1)) { cJSON_Delete(choices); return 0; } /* the speaker isn't in sight: no conversation */
    box.replyCount = n;
    for (int i = 0; i < n; i++) { box.replyIds[i] = r->choiceIds[i]; box.replies[i] = texts[i]; }
    box.chosen = reply_chosen;
    box.context = r;
    r->choiceAt = at;
    r->waiting = W_CHOICE;
    dialog_queue(&box); /* copies the words: the JSON can go */
    cJSON_Delete(choices);
    return 1;
}

/* "Object:61:27" / "Item:Container:6": the parts. */
static int sscanf_like(const char *thing, char *kind, char *cat, int *a1, int *a2)
{
    const char *p = strchr(thing, ':');
    if (!p) return 0;
    snprintf(kind, 16, "%.*s", (int)(p - thing), thing);
    const char *q = strchr(p + 1, ':');
    if (!q) return 0;
    snprintf(cat, 16, "%.*s", (int)(q - p - 1), p + 1);
    *a1 = atoi(p + 1); *a2 = atoi(q + 1);
    return 1;
}

static int category_of(const char *name)
{
    return strcmp(name, "Object") == 0 ? CAT_OBJECT : strcmp(name, "Container") == 0 ? CAT_CONTAINER : strcmp(name, "Book") == 0 ? CAT_BOOK : -1;
}

static void start_walk(Run *r, int actor, int x, int y, int beside)
{
    r->walker = actor; r->goalX = x; r->goalY = y; r->goalFloor = 0; r->beside = beside;
    r->tries = 0; r->best = 1 << 30; r->timerMs = 0;
    r->stage = 0;
}

static void cancel_actions_of(Run *self, int actor);

/* BeginScriptAction: whatever they were doing for an earlier command is cancelled. */
static void begin_action(Run *r, int actor, int kind)
{
    cancel_actions_of(r, actor);
    r->act = kind; r->walker = actor; r->stage = 0; r->stageMs = -1;
    r->waiting = W_ACTION;
}

/* SayCommandLine: a line of a command said by someone - their own line for it if the command has one - in a box, or over their head when they're out of sight. */
static void say_line(Run *r, const Cmd *c, const char *lineKey, int speakerActor)
{
    char key[96];
    int character = speakerActor >= 0 ? world_actor_character(speakerActor) : -1;
    if (speakerActor == 0) snprintf(key, sizeof key, "Line_%s_Player", lineKey);
    else snprintf(key, sizeof key, "Line_%s_%d", lineKey, character);
    const char *text = speakerActor >= 0 && get(c->params, key) ? param(c, key) : param(c, lineKey);
    const char *t = text;
    while (*t == ' ' || *t == '\t' || *t == '\n') t++;
    if (!*t) return;
    char id[16];
    snprintf(id, sizeof id, "%d", character);
    cJSON *params = cJSON_CreateObject();
    cJSON_AddStringToObject(params, "Text", text);
    cJSON_AddStringToObject(params, "SpeakerId", speakerActor < 0 ? "" : speakerActor == 0 ? "Player" : id);
    Cmd line = { C_ShowText, 0, params };
    DialogBox box;
    int byStepping = r->byStepping;
    r->byStepping = 0;
    if (build_box(r, &line, &box, 0)) dialog_queue(&box);
    else if (speakerActor >= 0) world_bark(speakerActor, text, 3000); /* no box for them: the words over their head */
    r->byStepping = byStepping;
    cJSON_Delete(params);
}

/* PutDownAt: at someone's feet (or a cell), resting on whatever is there, on their floor. */
static void put_down(Item *item, int atActor, int x, int y)
{
    int ax, ay, f;
    world_actor_cell(atActor, &ax, &ay, &f);
    if (x < 0) { x = ax; y = ay; }
    int uid = world_thing_put(items_category(item), items_item_id(item), items_quantity(item), x, y, f);
    if (uid >= 0) { int lit, spent; double burn; items_light_state(item, &lit, &spent, &burn); world_set_light_state(uid, lit, spent, burn); items_park(uid, item); }
    else items_free(item);
}

/* Give Item: from the giver (what they carry, if they must have it) or out of nowhere, to the receiver - the party helps carry; nobody can: at the receiver's feet. */
static void give_item(Run *r, const Cmd *c)
{
    int giver = actor_for(r, param(c, "Giver")), receiver = actor_for(r, param(c, "Receiver"));
    int category = category_of(param(c, "Category")), itemId = atoi(param(c, "ItemId"));
    if (giver < 0 || receiver < 0 || giver == receiver || category < 0) return;
    int quantity = atoi(param(c, "Quantity"));
    if (quantity < 1) quantity = 1;
    if (quantity > 100) quantity = 100;
    Item *gifts[100];
    int n = 0;
    if (strcmp(param(c, "MustHave"), "True") == 0)
    {
        if (!items_take(world_actor_character(giver), category, itemId, quantity, gifts, 100, &n)) { lastResult = 0; say_line(r, c, "NotHaveText", giver); return; }
    }
    else n = items_make_gifts(category, itemId, quantity, gifts, 100);
    int receiverId = world_actor_character(receiver);
    int partyHelps = items_in_party(receiverId);
    lastResult = 1;
    int saidCant = 0, saidNobody = 0, spoken[64], spokenCount = 0;
    for (int g = 0; g < n; g++)
    {
        if (items_give_to(receiverId, gifts[g])) continue;
        int taken = 0;
        if (partyHelps)
            for (int a = 0; a < world_actor_count() && !taken; a++)
            {
                int id = world_actor_character(a);
                if (a == receiver || !items_in_party(id) || !items_give_to(id, gifts[g])) continue;
                int said = 0;
                for (int k = 0; k < spokenCount; k++) if (spoken[k] == a) said = 1;
                if (!said && spokenCount < 64) { spoken[spokenCount++] = a; say_line(r, c, "PartyTryText", a); } /* "I shall carry it." */
                taken = 1;
            }
        if (taken) continue;
        if (partyHelps && !saidNobody) { say_line(r, c, "NobodyText", giver); saidNobody = 1; }
        if (!partyHelps && !saidCant) { say_line(r, c, "CantCarryText", giver); saidCant = 1; }
        put_down(gifts[g], receiver, -1, -1);
    }
}

/* "Player", "Self" / "Activator", a character's id, or its name (ScriptCharacter). */
static int script_actor(const Run *r, Value who)
{
    const char *t = value_text_of(who);
    while (*t == ' ') t++;
    if (!*t || !strcmp(t, "Self") || !strcmp(t, "self") || !strcmp(t, "Activator")) return r && r->activator >= 0 ? r->activator : 0;
    if (!strcmp(t, "Player") || !strcmp(t, "player")) return 0;
    double id;
    if (value_try_number(who, &id)) return world_actor_by_character((int)id);
    for (int i = 0; i < ownerCount; i++)
        if (!owners[i].isMapEvent && owners[i].itemCategory == 0)
        {
            const char *a = owners[i].name, *b = t;
            while (*a && *b && ((*a | 32) == (*b | 32))) { a++; b++; }
            if (!*a && !*b) return world_actor_by_character(owners[i].characterId);
        }
    return -1;
}

static cJSON *character_data(int characterId)
{
    for (int i = 0; i < ownerCount; i++) if (!owners[i].isMapEvent && owners[i].itemCategory == 0 && owners[i].characterId == characterId) return owners[i].data;
    return NULL;
}

/* A stat by its kind (Hits 6, Strength 1...), or by name; NULL when none. */
static const char *stat_value(int characterId, int kind, const char *name)
{
    cJSON *s, *values = get(character_data(characterId), "StatValues");
    cJSON_ArrayForEach(s, statsJson)
    {
        int match = name ? (strlen(name) && !strcmp(str(s, "Name"), name)) : num(s, "Kind", 0) == kind;
        if (name && !match)
        {
            const char *a = str(s, "Name"), *b = name;
            while (*a && *b && ((*a | 32) == (*b | 32))) { a++; b++; }
            match = !*a && !*b;
        }
        if (!match) continue;
        char key[16];
        snprintf(key, sizeof key, "%d", num(s, "Id", -1));
        cJSON *v = get(values, key);
        return cJSON_IsString(v) ? v->valuestring : NULL;
    }
    return NULL;
}

static Run *functionRun; /* whose event a formula is worked out for */

/* The game's formula functions (GameTestWindow.GameFunction). */
int events_game_function(const char *name, const Value *args, int count, Value *out)
{
    Run *r = functionRun;
    int who = script_actor(r, count > 0 ? args[0] : value_text("Self"));
    int id = who >= 0 ? world_actor_character(who) : -1;
    if (!strcmp(name, "hp") || !strcmp(name, "maxhp"))
    {
        const char *v = who >= 0 ? stat_value(id, !strcmp(name, "hp") ? 6 : 1, NULL) : NULL;
        *out = value_number(v ? atoi(v) : 0);
        return 1;
    }
    if (!strcmp(name, "stat"))
    {
        const char *v = who >= 0 && count >= 2 ? stat_value(id, 0, value_text_of(args[1])) : NULL;
        double n;
        *out = !v ? value_number(0) : (value_try_number(value_text(v), &n) ? value_number(n) : value_text(v));
        return 1;
    }
    if (!strcmp(name, "x") || !strcmp(name, "y") || !strcmp(name, "floorof"))
    {
        int x = -1, y = -1, f = 1;
        if (who >= 0) world_actor_cell(who, &x, &y, &f);
        *out = value_number(name[0] == 'x' ? x : name[0] == 'y' ? y : f);
        return 1;
    }
    if (!strcmp(name, "name")) { cJSON *d = character_data(id); *out = value_text(d ? str(d, "Name") : ""); return 1; }
    if (!strcmp(name, "isdead")) { *out = value_number(0); return 1; } /* nobody dies in the player yet */
    if (!strcmp(name, "inparty")) { *out = value_number(who >= 0 && items_in_party(id)); return 1; }
    if (!strcmp(name, "partysize"))
    {
        int n = 0;
        for (int a = 0; a < world_actor_count(); a++) if (items_in_party(world_actor_character(a))) n++;
        *out = value_number(n);
        return 1;
    }
    if (!strcmp(name, "facing")) { static const char *F[8] = { "North", "NorthEast", "East", "SouthEast", "South", "SouthWest", "West", "NorthWest" }; int f = who >= 0 ? world_actor_facing(who) : 4; *out = value_text(F[f & 7]); return 1; }
    if (!strcmp(name, "gold")) { *out = value_number(items_party_money(objectsJson, currenciesJson)); return 1; }
    if (!strcmp(name, "items"))
    {
        int n = 0;
        cJSON *o;
        if (count >= 1)
            cJSON_ArrayForEach(o, objectsJson)
            {
                const char *a = str(o, "Name"), *b = value_text_of(args[0]);
                while (*a && *b && ((*a | 32) == (*b | 32))) { a++; b++; }
                if (!*a && !*b) n += items_party_count(CAT_OBJECT, num(o, "Id", -1));
            }
        *out = value_number(n);
        return 1;
    }
    if (!strcmp(name, "self"))
    {
        char key[64];
        snprintf(key, sizeof key, "%s|%c", r ? r->owner : "global", count > 0 ? (char)(value_text_of(args[0])[0] & ~32) : 'A');
        *out = value_number(script_self_switch(key));
        return 1;
    }
    if (!strcmp(name, "hour")) { *out = value_number(clockHour); return 1; }
    if (!strcmp(name, "minute")) { *out = value_number(clockMinute); return 1; }
    if (!strcmp(name, "day")) { *out = value_number(clockDay); return 1; }
    if (!strcmp(name, "combat") || !strcmp(name, "paperdollopen")) { *out = value_number(0); return 1; }
    return 0;
}

/* ThingOf: the thing a command picked ("Object:<instance>:<object>" or "Item:<Category>:<id>", near X, Y); -1 if it isn't there. */
static int thing_of(const Cmd *c)
{
    char kind[16], cat[16];
    int a1, a2, x = atoi(param(c, "X")), y = atoi(param(c, "Y"));
    if (!sscanf_like(param(c, "Thing"), kind, cat, &a1, &a2)) return -1;
    if (!strcmp(kind, "Object")) return world_find_object(a1, a2, x, y);
    return world_find_item(category_of(cat), a2, x, y);
}

/* A stat of a character, set (by its kind). */
static void set_stat(int characterId, int kind, const char *value)
{
    cJSON *data = character_data(characterId), *s, *values = get(data, "StatValues");
    if (!values) return;
    cJSON_ArrayForEach(s, statsJson)
        if (num(s, "Kind", 0) == kind)
        {
            char key[16];
            snprintf(key, sizeof key, "%d", num(s, "Id", -1));
            if (get(values, key)) cJSON_ReplaceItemInObject(values, key, cJSON_CreateString(value));
            else cJSON_AddStringToObject(values, key, value);
        }
}

/* Get Game Data: one thing about the game into a variable. */
static void get_game_data(Run *r, const Cmd *c)
{
    int who = actor_for(r, param(c, "Who"));
    char whoKey[16];
    snprintf(whoKey, sizeof whoKey, "%d", who >= 0 ? world_actor_character(who) : -1);
    if (who == 0) snprintf(whoKey, sizeof whoKey, "Player");
    Value args[2] = { value_text(whoKey), value_text(param(c, "StatName")) }, v;
    const char *what = param(c, "What");
    functionRun = r;
    int ok = 1;
    if (!strcmp(what, "X (cell)")) events_game_function("x", args, 1, &v);
    else if (!strcmp(what, "Y (cell)")) events_game_function("y", args, 1, &v);
    else if (!strcmp(what, "Floor")) events_game_function("floorof", args, 1, &v);
    else if (!strcmp(what, "HP")) events_game_function("hp", args, 1, &v);
    else if (!strcmp(what, "Max HP")) events_game_function("maxhp", args, 1, &v);
    else if (!strcmp(what, "Stat")) events_game_function("stat", args, 2, &v);
    else if (!strcmp(what, "Gold (all money)")) events_game_function("gold", args, 0, &v);
    else if (!strcmp(what, "Item count")) v = value_number(who >= 0 && category_of(param(c, "Category")) >= 0 ? items_count(world_actor_character(who), category_of(param(c, "Category")), atoi(param(c, "ItemId"))) : 0);
    else if (!strcmp(what, "Name")) events_game_function("name", args, 1, &v);
    else if (!strcmp(what, "Is dead")) events_game_function("isdead", args, 1, &v);
    else if (!strcmp(what, "Is in party")) events_game_function("inparty", args, 1, &v);
    else if (!strcmp(what, "Party size")) events_game_function("partysize", args, 0, &v);
    else if (!strcmp(what, "Hour")) v = value_number(clockHour);
    else if (!strcmp(what, "Minute")) v = value_number(clockMinute);
    else if (!strcmp(what, "Day")) v = value_number(clockDay);
    else if (!strcmp(what, "Map name")) v = value_text(world_map_name());
    else if (!strcmp(what, "Leader's name")) { args[0] = value_text("Player"); events_game_function("name", args, 1, &v); }
    else if (!strcmp(what, "Facing")) events_game_function("facing", args, 1, &v);
    else ok = 0;
    if (ok) set_named(param(c, "Variable"), v);
}

/* A command that isn't control flow; 0 = it waits (the run stops here till it's done). */
static int execute(Run *r, const Cmd *c)
{
    char text[160];
    switch (c->type)
    {
        case C_ShowText:
        {
            DialogBox box;
            if (build_box(r, c, &box, 0)) dialog_queue(&box);
            return 1;
        }
        case C_SetSwitch:
        {
            const char *name = param(c, "SwitchName");
            int value;
            parse_bool(param(c, "Value"), &value);
            if (name[0]) set_var(name, value ? 1 : 0);
            return 1;
        }
        case C_SetVariable:
        {
            const char *name = param(c, "VariableName"), *op = param(c, "Operation");
            double amount;
            if (!name[0] || !parse_number(param(c, "Value"), &amount)) return 1;
            double now = get_var(name), result = amount;
            if (strcmp(op, "Add") == 0) result = now + amount;
            else if (strcmp(op, "Subtract") == 0) result = now - amount;
            else if (strcmp(op, "Multiply") == 0) result = now * amount;
            else if (strcmp(op, "Divide") == 0) result = amount != 0 ? now / amount : now;
            else if (strcmp(op, "Modulo") == 0) result = amount != 0 ? now - amount * (double)(long)(now / amount) : now;
            set_var(name, result);
            return 1;
        }
        case C_Wait:
        {
            const char *unit = param(c, "Unit"), *amount = param(c, "Duration");
            if (!amount[0]) amount = param(c, "Seconds");
            double v = 1;
            if (!parse_number(amount, &v)) v = 1;
            if (v < 0) v = 0;
            r->waiting = W_TIMER;
            r->timerMs = v * 1000;
            r->waitMinutes = 0;
            if (!strcmp(unit, "Minutes") || !strcmp(unit, "Hours"))
            {
                /* Lasting: by the game clock */
                r->waitFromMinute = total_minutes();
                r->waitMinutes = (int)(strcmp(unit, "Hours") == 0 ? v * 60 : v);
                if (r->waitMinutes < 1) r->waitMinutes = 1;
                r->timerMs = 1e30;
            }
            return 0;
        }
        case C_WalkToLocation: case C_WalkToFloorLocation:
        {
            int actor = actor_for(r, param(c, "Who"));
            if (actor < 0) return 1;
            r->walker = actor; r->goalX = atoi(param(c, "X")); r->goalY = atoi(param(c, "Y"));
            r->goalFloor = 0;
            if (c->type == C_WalkToFloorLocation) { int f = atoi(param(c, "Floor")); r->goalFloor = f < 1 ? 1 : f > 9 ? 9 : f; }
            r->tries = 0; r->best = 1 << 30; r->timerMs = 0; r->beside = 0;
            cancel_actions_of(r, actor);
            r->waiting = W_WALK;
            return 0;
        }
        case C_PickupItem:
        {
            /* "Object:<instance>:<object>" or "Item:<Category>:<id>", near (X, Y) - ThingOf. */
            int actor = actor_for(r, param(c, "Who"));
            const char *thing = param(c, "Thing");
            int x = atoi(param(c, "X")), y = atoi(param(c, "Y")), uid = -1, a1 = 0, a2 = 0;
            char kind[16] = "", cat[16] = "";
            if (sscanf_like(thing, kind, cat, &a1, &a2))
            {
                if (strcmp(kind, "Object") == 0) uid = world_find_object(a1, a2, x, y);
                else uid = world_find_item(strcmp(cat, "Container") == 0 ? CAT_CONTAINER : strcmp(cat, "Book") == 0 ? CAT_BOOK : CAT_OBJECT, a2, x, y);
            }
            if (actor < 0 || uid < 0) { game_log(actor < 0 ? "Pickup Item: that character isn't here." : "Pickup Item: the thing isn't there any more."); return 1; }
            int tx, ty, tc, ti, tq, tin;
            world_thing_info(uid, &tc, &ti, &tq, &tx, &ty, &tin);
            begin_action(r, actor, A_PICKUP);
            r->thing = uid; r->actX = tx; r->actY = ty;
            start_walk(r, actor, tx, ty, 1);
            return 0;
        }
        case C_DropItem:
        {
            int actor = actor_for(r, param(c, "Who")), category = category_of(param(c, "Category")), itemId = atoi(param(c, "ItemId"));
            if (actor < 0 || category < 0) return 1;
            if (!items_carries(world_actor_character(actor), category, itemId)) { game_log("Drop Item: they haven't got it to drop."); return 1; }
            begin_action(r, actor, A_DROP);
            r->category = category; r->itemId = itemId;
            r->quantity = atoi(param(c, "Quantity")) > 0 ? atoi(param(c, "Quantity")) : 1;
            r->atSpot = strcmp(param(c, "AtSpot"), "True") == 0;
            if (r->atSpot)
            {
                r->actX = atoi(param(c, "X")); r->actY = atoi(param(c, "Y"));
                start_walk(r, actor, r->actX, r->actY, 1);
            }
            else { r->stage = 1; r->stageMs = -1; }
            return 0;
        }
        case C_GiveItem:
            give_item(r, c);
            return 1;
        case C_ShowBark:
        {
            int actor = actor_for(r, param(c, "Who"));
            const char *words = value_text_of(formula(c, "Value"));
            double seconds = formula_number(c, "Seconds");
            if (actor >= 0) world_bark(actor, words, (int)((seconds > 0.5 ? seconds : 0.5) * 1000));
            return 1;
        }
        case C_ChangeDayNight: lighting_period(param(c, "Period")); return 1;
        case C_JoinParty:
        {
            int actor = actor_for(r, param(c, "Who"));
            if (actor <= 0 || world_actor_gone(actor)) { lastResult = 0; return 1; }
            int join = strcmp(param(c, "Value"), "Leave the party") != 0, id = world_actor_character(actor);
            if (items_in_party(id) == join) return 1;
            items_set_in_party(id, join);
            schedules_join(actor, join);
            return 1;
        }
        case C_ChangeSchedule:
        {
            int actor = actor_for(r, param(c, "Who"));
            if (actor <= 0) return 1;
            const char *a = param(c, "Activity");
            const char *behavior = !strcmp(a, "Pace east-west") ? "HorizPace" : !strcmp(a, "Pace north-south") ? "VertPace" : !strcmp(a, "Eat at inn") ? "EatAtInn" : !strcmp(a, "Back to its schedule") ? "Resume" : a;
            if (actor < 256) heldBy[actor] = 0;
            schedules_change_behavior(actor, behavior);
            double hours = formula_number(c, "Hours");
            schedules_until(actor, hours > 0 && strcmp(behavior, "Resume") ? total_minutes() + (long)(hours * 60 + 0.5) : 0);
            return 1;
        }
        case C_PlaceCharacter:
        {
            const char *who = param(c, "Who");
            int actor = actor_for(r, who);
            if (actor < 0 && who[0]) for (int a = 1; a < world_actor_count(); a++) if (world_actor_character(a) == atoi(who)) { actor = a; break; }
            if (actor < 0) { lastResult = 0; return 1; }
            if (!strcmp(param(c, "Value"), "Remove it from the map")) { world_actor_remove(actor); schedules_follow(actor, -1); return 1; }
            if (!param(c, "X")[0] || !param(c, "Y")[0]) return 1;
            int wasGone = world_actor_gone(actor);
            world_actor_place(actor, atoi(param(c, "X")), atoi(param(c, "Y")), param(c, "Floor")[0] ? atoi(param(c, "Floor")) : 1);
            if (wasGone && !items_in_party(world_actor_character(actor))) schedules_change_behavior(actor, "Resume");
            return 1;
        }
        case C_TurnToFace:
        {
            int actor = actor_for(r, param(c, "Who"));
            if (actor < 0) return 1;
            const char *t = param(c, "Towards");
            int facing = !strcmp(t, "North") ? 0 : !strcmp(t, "South") ? 4 : !strcmp(t, "East") ? 2 : !strcmp(t, "West") ? 6 : -1;
            int other = !strcmp(t, "The main player") ? 0 : !strcmp(t, "Whoever set it off") ? (r->activator >= 0 ? r->activator : 0) : !strcmp(t, "Another character") ? actor_for(r, param(c, "Other")) : -1;
            if (facing < 0 && other >= 0)
            {
                double fx, fy, ox, oy;
                world_actor_pixel(actor, &fx, &fy); world_actor_pixel(other, &ox, &oy);
                facing = fabs(ox - fx) > fabs(oy - fy) ? (ox > fx ? 2 : 6) : (oy > fy ? 4 : 0);
            }
            if (facing >= 0) world_set_pose(actor, "Idle", facing);
            return 1;
        }
        case C_FollowCharacter:
        {
            int actor = actor_for(r, param(c, "Who"));
            if (actor <= 0) return 1;
            if (!strcmp(param(c, "Value"), "Stop following")) { schedules_follow(actor, -1); return 1; }
            int leader = actor_for(r, param(c, "Other"));
            if (leader >= 0 && leader != actor) schedules_follow(actor, leader);
            return 1;
        }
        case C_ChangeName:
        {
            int actor = actor_for(r, param(c, "Who"));
            cJSON *data = actor >= 0 ? character_data(world_actor_character(actor)) : NULL;
            if (data) cJSON_ReplaceItemInObject(data, "Name", cJSON_CreateString(value_text_of(formula(c, "Value"))));
            return 1;
        }
        case C_ChangeLook:
        {
            int actor = actor_for(r, param(c, "Who"));
            if (actor < 0) return 1;
            if (!strcmp(param(c, "Own"), "True")) world_change_look(actor, -1);
            else { int other = actor_for(r, param(c, "Other")); if (other >= 0 && other != actor) world_change_look(actor, other); }
            return 1;
        }
        case C_SetAttackMode:
        {
            int actor = actor_for(r, param(c, "Who"));
            cJSON *data = actor >= 0 ? character_data(world_actor_character(actor)) : NULL;
            if (data && param(c, "Mode")[0]) cJSON_ReplaceItemInObject(data, "AttackModeRuntime", cJSON_CreateString(param(c, "Mode")));
            return 1;
        }
        case C_Resurrect:
        {
            int actor = actor_for(r, param(c, "Who"));
            if (strcmp(param(c, "Value"), "Resurrect") != 0)
            {
                /* HealFully: hit points back to the most (Strength) */
                cJSON *data = actor >= 0 ? character_data(world_actor_character(actor)) : NULL;
                const char *strength = data ? stat_value(world_actor_character(actor), 1, NULL) : NULL;
                if (data && strength) set_stat(world_actor_character(actor), 6, strength);
                return 1;
            }
            lastResult = 0; /* nobody has died: nothing to bring back */
            return 1;
        }
        case C_PlayPose:
        {
            int actor = actor_for(r, param(c, "Who"));
            if (actor < 0) return 1;
            const char *pose = param(c, "Pose");
            if (!world_set_pose(actor, pose, world_direction_of(param(c, "Facing")))) { lastResult = 0; return 1; }
            if (strcmp(pose, "Sit") && strcmp(pose, "Sleeping") && world_seated(actor)) { world_stand_up(actor); world_set_pose(actor, pose, world_direction_of(param(c, "Facing"))); }
            begin_action(r, actor, A_POSE);
            double amount = 1;
            if (!parse_number(param(c, "Duration"), &amount) || amount < 0.01) amount = amount < 0.01 ? 0.01 : 1;
            const char *unit = param(c, "Unit");
            r->stage = 0; r->waitMinutes = 0;
            if (!strcmp(unit, "Seconds")) r->stageMs = amount * 1000;
            else if (!strcmp(unit, "Minutes") || !strcmp(unit, "Hours")) { r->waitFromMinute = total_minutes(); r->waitMinutes = (int)(strcmp(unit, "Hours") ? amount : amount * 60); r->stageMs = 1e30; }
            else r->stageMs = world_pose_length(actor, pose); /* until its animation has played once */
            return 0;
        }
        case C_CreateObject:
        {
            int category = category_of(param(c, "Category")), itemId = atoi(param(c, "ItemId"));
            if (category < 0 || itemId < 0 || !param(c, "X")[0]) return 1;
            int quantity = (int)formula_number(c, "Quantity");
            Item *gifts[100];
            int n = items_make_gifts(category, itemId, quantity > 0 ? quantity : 1, gifts, 100);
            int f = param(c, "Floor")[0] ? atoi(param(c, "Floor")) : 1;
            for (int i = 0; i < n; i++)
            {
                int uid = world_thing_put(items_category(gifts[i]), items_item_id(gifts[i]), items_quantity(gifts[i]), atoi(param(c, "X")), atoi(param(c, "Y")), f);
                if (uid >= 0) items_park(uid, gifts[i]); else items_free(gifts[i]);
            }
            return 1;
        }
        case C_DeleteObject: { int uid = thing_of(c); if (uid >= 0) { items_free(items_unpark(uid, -1, -1, 1)); world_thing_remove(uid); } else lastResult = 0; return 1; }
        case C_MoveObject:
        {
            int uid = thing_of(c);
            if (uid < 0 || !param(c, "ToX")[0]) { lastResult = 0; return 1; }
            world_thing_move(uid, atoi(param(c, "ToX")), atoi(param(c, "ToY")), param(c, "ToFloor")[0] ? atoi(param(c, "ToFloor")) : 1);
            return 1;
        }
        case C_ChangeObjectLook:
        {
            int uid = thing_of(c);
            if (uid < 0 || world_thing_object_id(uid) < 0) { lastResult = 0; return 1; }
            world_thing_look(uid, !strcmp(param(c, "Own"), "True") ? NULL : sprite_find(param(c, "SpriteGroup"), param(c, "SpriteName")));
            return 1;
        }
        case C_SetDoorState:
        {
            int uid = thing_of(c);
            if (uid < 0) { lastResult = 0; return 1; }
            if (world_thing_type(uid) == 3) world_door_set(uid, param(c, "Value"));
            else
            {
                /* a container on the map: its lock */
                int tc, ti, tq, tx, ty, tin;
                world_thing_info(uid, &tc, &ti, &tq, &tx, &ty, &tin);
                if (tc == CAT_CONTAINER && (!strcmp(param(c, "Value"), "Locked") || !strcmp(param(c, "Value"), "Unlocked")))
                {
                    Item *box = items_unpark(uid, tc, ti, tq);
                    items_set_locked(box, !strcmp(param(c, "Value"), "Locked"));
                    items_park(uid, box);
                }
            }
            return 1;
        }
        case C_CountItems:
        {
            int category = category_of(param(c, "Category")), itemId = atoi(param(c, "ItemId")), count = 0;
            if (category < 0) { lastResult = 0; return 1; }
            const char *where = param(c, "Where");
            if (!strcmp(where, "The whole party")) count = items_party_count_any(category, itemId);
            else if (!strcmp(where, "A container on the map"))
            {
                int uid = thing_of(c), tc, ti, tq, tx, ty, tin;
                if (uid < 0) { lastResult = 0; return 1; }
                world_thing_info(uid, &tc, &ti, &tq, &tx, &ty, &tin);
                Item *box = items_unpark(uid, tc, ti, tq);
                count = items_count_in(box, category, itemId);
                items_park(uid, box);
            }
            else { int actor = actor_for(r, param(c, "Who")); if (actor < 0) { lastResult = 0; return 1; } count = items_count_any(world_actor_character(actor), category, itemId); }
            set_named(param(c, "Variable"), value_number(count));
            return 1;
        }
        case C_SetContainerContents:
        {
            int uid = thing_of(c), tc, ti, tq, tx, ty, tin;
            if (uid < 0) { lastResult = 0; return 1; }
            world_thing_info(uid, &tc, &ti, &tq, &tx, &ty, &tin);
            if (tc != CAT_CONTAINER) { lastResult = 0; return 1; }
            Item *box = items_unpark(uid, tc, ti, tq);
            if (!strcmp(param(c, "Empty"), "True")) items_empty(box);
            int category = category_of(param(c, "Category")), itemId = atoi(param(c, "ItemId"));
            if (category >= 0 && itemId >= 0)
            {
                Item *gifts[100];
                int q = (int)formula_number(c, "Quantity"), n = items_make_gifts(category, itemId, q > 0 ? q : 1, gifts, 100);
                for (int i = 0; i < n; i++) items_put_in(box, gifts[i]);
            }
            items_park(uid, box);
            return 1;
        }
        case C_ChangeTile:
        {
            if (!param(c, "X")[0] || !param(c, "Y")[0]) return 1;
            world_set_tile(atoi(param(c, "X")), atoi(param(c, "Y")), param(c, "Floor")[0] ? atoi(param(c, "Floor")) : 1, (int)formula_number(c, "TileIndex"));
            return 1;
        }
        case C_ShakeScreen:
        {
            int intensity = param(c, "Intensity")[0] ? atoi(param(c, "Intensity")) : 5;
            double seconds = 1;
            if (!parse_number(param(c, "DurationSeconds"), &seconds)) seconds = 1;
            screen_shake(intensity, seconds);
            return 1;
        }
        case C_FlashScreen: screen_flash(screen_parse_colour(param(c, "Color"), 0xFFFFFF), formula_number(c, "Seconds")); return 1;
        case C_FadeScreen:
        {
            double seconds = formula_number(c, "Seconds");
            double total = screen_fade(screen_parse_colour(param(c, "Color"), 0), strcmp(param(c, "Value"), "In") != 0, seconds > 0 ? seconds : 0);
            if (total <= 0) return 1;
            r->camera = 0; r->screenMs = 0; r->screenTotal = total;
            r->waiting = W_SCREEN; /* the event waits till the fade is done */
            return 0;
        }
        case C_MoveCamera:
        {
            int back = !strcmp(param(c, "Value"), "Back to the player");
            double fx, fy, tx, ty;
            world_camera_center(&fx, &fy);
            if (back) { int px, py, f; world_actor_cell(0, &px, &py, &f); world_actor_pixel(0, &tx, &ty); }
            else { if (!param(c, "X")[0] || !param(c, "Y")[0]) return 1; tx = atoi(param(c, "X")) * world_tile_w() + world_tile_w() / 2.0; ty = atoi(param(c, "Y")) * world_tile_h() + world_tile_h() / 2.0; }
            world_camera_follow(0);
            double seconds = formula_number(c, "Seconds");
            if (seconds <= 0) { world_camera_hold(!back, tx, ty); return 1; }
            r->camera = 1; r->cameraBack = back; r->camFromX = fx; r->camFromY = fy; r->camToX = tx; r->camToY = ty;
            r->screenMs = 0; r->screenTotal = seconds * 1000;
            r->waiting = W_SCREEN;
            return 0;
        }
        case C_CameraFollow:
        {
            int actor = actor_for(r, param(c, "Who"));
            world_camera_follow(actor > 0 ? actor : 0);
            return 1;
        }
        case C_ShowPicture:
        {
            const Sprite *s = sprite_find(param(c, "SpriteGroup"), param(c, "SpriteName"));
            if (!s) return 1;
            screen_show_picture((int)formula_number(c, "Number"), s, formula_number(c, "X"), formula_number(c, "Y"), formula_number(c, "Opacity") / 100.0, screenNow);
            return 1;
        }
        case C_ErasePicture: screen_erase_picture((int)formula_number(c, "Number")); return 1;
        case C_ChangeWeather:
        {
            static const char *KINDS[] = { "Clear", "Rain", "Snow", "Hail", "Storm" };
            int k = -1;
            for (int i = 0; i < 5; i++) if (!strcmp(param(c, "Kind"), KINDS[i])) k = i;
            if (k < 0) return 1;
            const char *cl = param(c, "Clouds");
            weather_change(k, param(c, "Intensity")[0] ? atoi(param(c, "Intensity")) : 50, !strcmp(cl, "True") ? 1 : !strcmp(cl, "False") ? 0 : -1);
            return 1;
        }
        case C_PlaySound:
        {
            double volume = -1;
            if (parse_number(param(c, "Volume"), &volume)) volume = volume < 0 ? 0 : volume > 1 ? 1 : volume;
            else volume = -1;
            sound_play_ex(atoi(param(c, "SoundResourceId")), volume, !strcmp(param(c, "Loop"), "True"), OWNER_NONE, 1);
            return 1;
        }
        case C_StopSound:
            if (!strcmp(param(c, "SoundResourceId"), "All")) sound_stop_all();
            else sound_stop_id(atoi(param(c, "SoundResourceId")));
            return 1;
        case C_PlayMusic:
        {
            double volume = -1;
            if (!parse_number(param(c, "Volume"), &volume)) volume = -1;
            sound_stop_owner(OWNER_MUSIC); /* one piece of event music at a time */
            sound_play_ex(atoi(param(c, "MusicResourceId")), volume, !strcmp(param(c, "Loop"), "True"), OWNER_MUSIC, 1);
            return 1;
        }
        case C_StopMusic:
        {
            double s = formula_number(c, "Seconds");
            sound_fade(OWNER_MUSIC, s > 0 ? s : 0, 0);
            return 1;
        }
        case C_FadeMusicIn:
        {
            int id = atoi(param(c, "SoundId"));
            double s = formula_number(c, "Seconds");
            sound_stop_owner(OWNER_MUSIC);
            if (sound_play_ex(id, -1, !strcmp(param(c, "Loop"), "True"), OWNER_MUSIC, 0)) sound_fade(OWNER_MUSIC, s > 0.05 ? s : 0.05, 1);
            return 1;
        }
        case C_SetVolume: sound_set_volumes(formula_number(c, "Music") / 100.0, formula_number(c, "Sounds") / 100.0); return 1;
        case C_ResumeMapMusic: sound_stop_owner(OWNER_MUSIC); ambientPlaying = -2; return 1;
        case C_Stand:
        {
            int actor = actor_for(r, param(c, "Who"));
            if (actor >= 0) world_stand_up(actor);
            return 1;
        }
        case C_SleepOnBed:
        {
            /* "Under": the bed the running event's object is (or lies on, a cover); else the nearest */
            int bed = 0;
            if (strcmp(param(c, "Which"), "Nearest") != 0 && r->ownerUid)
            {
                bed = world_thing_type(r->ownerUid) == 8 ? r->ownerUid : world_bed_under_cover(r->ownerUid);
            }
            if (!bed) bed = world_nearest_bed();
            if (!bed) { game_log("Sleep On Bed: there's no bed."); return 1; }
            world_player_sit(bed);
            return 1;
        }
        case C_Sit:
        {
            int actor = actor_for(r, param(c, "Who")), x = atoi(param(c, "X")), y = atoi(param(c, "Y")), a1, a2;
            char kind[16], cat[16];
            int uid = sscanf_like(param(c, "Thing"), kind, cat, &a1, &a2) && !strcmp(kind, "Object") ? world_find_object(a1, a2, x, y) : -1;
            if (actor < 0) return 1;
            if (uid < 0 || world_thing_type(uid) != 7) { game_log("Sit: that chair isn't there."); return 1; }
            begin_action(r, actor, A_SIT);
            r->thing = uid; r->tries = 0; r->stageMs = 0;
            return 0;
        }
        case C_ChangeBehavior:
        {
            const char *who = param(c, "CharacterId");
            int actor = actor_for(r, who[0] ? who : "Activator");
            if (actor > 0 && actor < 256) heldBy[actor] = 0; /* told what to do now: no longer waiting for its event to end */
            if (actor > 0) schedules_change_behavior(actor, param(c, "Behavior"));
            return 1;
        }
        case C_ApplyPaletteColor:
        {
            int percent = atoi(param(c, "OpacityPercent"));
            if (!param(c, "OpacityPercent")[0]) percent = 40;
            lighting_palette(param(c, "Hex"), percent / 100.0);
            return 1;
        }
        case C_SetTime:
        {
            int amount = (int)floor(formula_number(c, "Hour") + 0.5), minutes;
            const char *how = param(c, "Value");
            if (!strcmp(how, "Advance by minutes")) minutes = amount > 0 ? amount : 0;
            else if (!strcmp(how, "Advance by hours")) minutes = (amount > 0 ? amount : 0) * 60;
            else
            {
                int h = amount < 0 ? 0 : amount > 23 ? 23 : amount, m = (int)formula_number(c, "Minute");
                m = m < 0 ? 0 : m > 59 ? 59 : m;
                minutes = (((h * 60 + m) - (clockHour * 60 + clockMinute)) % 1440 + 1440) % 1440; /* forward to that time of day */
            }
            if (minutes > 0) clock_advance_minutes(minutes);
            return 1;
        }
        case C_ControlSelfSwitch:
        {
            char key[64], letter[8];
            const char *l = param(c, "Letter");
            while (*l == ' ') l++;
            snprintf(letter, sizeof letter, "%c", *l ? (char)((*l >= 'a' && *l <= 'z') ? *l - 32 : *l) : 'A');
            snprintf(key, sizeof key, "%s|%s", r->owner, letter);
            script_set_self_switch(key, strcmp(param(c, "Value"), "Off") != 0);
            return 1;
        }
        case C_GetGameData:
            get_game_data(r, c);
            return 1;
        case C_RemoveItem:
        {
            int category = category_of(param(c, "Category")), itemId = atoi(param(c, "ItemId"));
            int wanted = (int)formula_number(c, "Quantity");
            if (category < 0) return 1;
            if (wanted < 1) wanted = 1;
            int left = wanted;
            Item *taken[100];
            for (int a = 0; a < world_actor_count() && left > 0; a++)
            {
                int id = world_actor_character(a);
                if (strcmp(param(c, "FromParty"), "True") == 0 ? !items_in_party(id) : a != actor_for(r, param(c, "Who"))) continue;
                while (left > 0)
                {
                    int have = items_count(id, category, itemId);
                    if (have <= 0) break;
                    int n, take = have < left ? have : left;
                    if (!items_take(id, category, itemId, take, taken, 100, &n)) break;
                    for (int k = 0; k < n; k++) items_free(taken[k]);
                    left -= take;
                }
            }
            return 1;
        }
        case C_Teleport:
        {
            int actor = actor_for(r, param(c, "Who"));
            if (actor >= 0) world_teleport(actor, atoi(param(c, "X")), atoi(param(c, "Y")));
            return 1;
        }
        case C_ChangePose:
        {
            int actor = actor_for(r, param(c, "Who"));
            if (actor >= 0 && !world_set_pose(actor, param(c, "Pose"), world_direction_of(param(c, "Facing"))))
            {
                snprintf(text, sizeof text, "Change Pose: there's no pose \"%s\".", param(c, "Pose"));
                game_log(text);
            }
            return 1;
        }
        default:
            if (c->type >= 0 && c->type < C_COUNT)
            {
                snprintf(text, sizeof text, "\"%s\" isn't in the player yet - skipped.", COMMAND_NAMES[c->type]);
                log_once(c->type, text);
            }
            return 1;
    }
}

/* PlayerWalkStep / NpcWalkStep: a leg at a time; done when there (or beside it when someone's on it), given up after 5 tries that get no nearer. 1 = still going. */
static int walk_tick(Run *r, double ms, int *ok)
{
    int a = r->walker, x, y, f;
    if (a == 0 && world_keys_held()) { world_walk_cancel(0); *ok = 0; return 0; } /* the player took over */
    if (world_walking(a)) return 1;
    world_actor_cell(a, &x, &y, &f);
    int floorSteps = r->goalFloor ? abs(f - r->goalFloor) * 5 : 0;
    int dx = abs(x - r->goalX), dy = abs(y - r->goalY);
    if (floorSteps == 0 && r->beside && (dx > dy ? dx : dy) <= 1) { *ok = 1; return 0; } /* next to it */
    if (floorSteps == 0 && !r->beside && (dx + dy == 0 || (dx + dy == 1 && !world_can_stand(a, r->goalX, r->goalY, r->goalFloor)))) { *ok = 1; return 0; }
    if ((r->timerMs -= ms) > 0) return 1;
    if (a == 0)
    {
        if (++r->tries > 5) { *ok = 0; return 0; }
        if (!world_walk_to(0, r->goalX, r->goalY, r->goalFloor, r->beside)) r->timerMs = 700; /* no way just now: again shortly */
        return 1;
    }
    int distance = dx + dy + floorSteps;
    if (distance < r->best) { r->best = distance; r->tries = 0; }
    else if (++r->tries >= 5) { *ok = 0; return 0; }
    if (!world_walk_to(a, r->goalX, r->goalY, r->goalFloor, r->beside)) r->timerMs = 500 + 500 * r->tries; /* someone in the way: shortly again */
    return 1;
}

static void cancel_actions_of(Run *self, int actor)
{
    for (int i = 0; i < MAX_RUNS; i++)
    {
        Run *o = &runs[i];
        if (o == self || !o->used || o->walker != actor || (o->waiting != W_ACTION && o->waiting != W_WALK)) continue;
        world_walk_cancel(actor);
        o->waiting = W_NONE; /* Done(false): its commands go on */
        o->act = 0;
        continue_run(o);
    }
}

/* PoseOnce: the pose played once through (at least 0.3 s), then standing again. 1 = still playing. */
static int pose_once(Run *r, int actor, double ms)
{
    if (r->stageMs < 0)
    {
        world_set_pose(actor, "Picking", -1);
        int length = world_pose_length(actor, "Picking");
        r->stageMs = length > 300 ? length : 300;
        return 1;
    }
    if ((r->stageMs -= ms) > 0) return 1;
    world_set_pose(actor, "Idle", -1);
    return 0;
}

/* Pickup Item / Drop Item: walk there, the Picking pose, then the deed. 1 = still going; ok = how it went. */
static int action_tick(Run *r, double ms, int *ok)
{
    int a = r->walker;
    if (r->act == A_POSE)
    {
        /* Play Pose: shown for its time (or till its animation has played once), then standing again */
        if (r->waitMinutes > 0 ? total_minutes() - r->waitFromMinute < r->waitMinutes : (r->stageMs -= ms) > 0) return 1;
        r->waitMinutes = 0;
        if (!world_seated(a)) world_set_pose(a, "Idle", -1);
        *ok = 1;
        return 0;
    }
    if (r->act == A_SIT)
    {
        /* SitCommand: head for the chair (round the table), sit; 5 tries */
        if (world_seated(a)) { *ok = world_seated_on(a) == r->thing; return 0; }
        if (a == 0)
        {
            if (world_keys_held()) { *ok = 0; return 0; }
            if (!r->tries) { r->tries = 1; if (!world_player_sit(r->thing)) { *ok = 0; return 0; } return 1; }
            if (world_player_sitting_down() || world_walking(0)) return 1;
            *ok = 0;
            return 0;
        }
        if (world_walking(a)) return 1;
        if (world_arrive_furniture(a) > 0) return 1;
        if ((r->stageMs -= ms) > 0) return 1;
        if (++r->tries > 5) { game_log("Sit: they can't get a seat."); *ok = 0; return 0; }
        world_stand_up(a);
        if (!world_go_to_furniture(a, 0, r->thing)) r->stageMs = 700;
        return 1;
    }
    if (r->stage == 0)
    {
        if (r->act == A_PICKUP && !world_thing_there(r->thing)) { *ok = 0; return 0; } /* gone meanwhile */
        int walked;
        if (walk_tick(r, ms, &walked)) return 1;
        if (!walked) { char t[128]; snprintf(t, sizeof t, "%s: no way to (%d, %d) for them.", r->act == A_PICKUP ? "Pickup Item" : "Drop Item", r->goalX, r->goalY); game_log(t); *ok = 0; return 0; }
        world_face_cell(a, r->actX, r->actY, "Idle");
        r->stage = 1; r->stageMs = -1;
        return 1;
    }
    if (r->stage == 1) { if (pose_once(r, a, ms)) return 1; r->stage = 2; }
    int who = world_actor_character(a);
    if (r->act == A_PICKUP)
    {
        /* PickUp: Dragging_info::start's rules, then Actor::add as for a drop on them. */
        if (!world_thing_there(r->thing)) { game_log("Pickup Item: it isn't there any more."); *ok = 0; return 0; }
        int category, itemId, quantity, x, y, instance;
        world_thing_info(r->thing, &category, &itemId, &quantity, &x, &y, &instance);
        if (!items_can_lift(category, itemId)) { game_log("Pickup Item: that's too heavy to move."); *ok = 0; return 0; }
        Item *item = items_unpark(r->thing, category, itemId, quantity);
        { int lit, spent; double burn; world_light_state(r->thing, &lit, &spent, &burn); items_set_light_state(item, lit, spent, burn); } /* a torch's flame goes with it */
        if (!items_give_to(who, item))
        {
            game_log(items_refusal());
            items_repark(r->thing, item);
            *ok = 0;
            return 0;
        }
        world_thing_remove(r->thing);
        events_fire_item(category, itemId, 6); /* By Pickup Item */
        *ok = 1;
        return 0;
    }
    /* Drop Item: out of what they carry, down at the spot (or their feet). */
    Item *items[100];
    int n;
    if (!items_take(who, r->category, r->itemId, r->quantity, items, 100, &n)) { game_log("Drop Item: they haven't got that many to drop."); *ok = 0; return 0; }
    for (int i = 0; i < n; i++) put_down(items[i], a, r->atSpot ? r->actX : -1, r->atSpot ? r->actY : -1);
    events_fire_item(r->category, r->itemId, 3); /* By Drop Item */
    *ok = 1;
    return 0;
}

/* ---------------- Loops and Common Events (ScriptRunState) ---------------- */

static int closer_of(int type) { return type == C_While ? C_EndWhile : type == C_RepeatTimes ? C_EndRepeat : type == C_ForEach ? C_EndForEach : type == C_SelectCase ? C_EndSelect : -1; }
static int opener_of(int type) { return type == C_EndWhile ? C_While : type == C_EndRepeat ? C_RepeatTimes : type == C_EndForEach ? C_ForEach : type == C_EndSelect ? C_SelectCase : -1; }
static int is_loop_opener(int type) { return type == C_While || type == C_RepeatTimes || type == C_ForEach; }

/* An opener's End line (same indent, after it); the list's end when it's missing. */
static int find_closer(const List *l, int open)
{
    int closer = closer_of(l->cmds[open].type), indent = l->cmds[open].indent;
    for (int j = open + 1; j < l->count; j++) if (l->cmds[j].indent == indent && l->cmds[j].type == closer) return j;
    return l->count - 1;
}

/* An End line's opener (same indent, before it), or -1. */
static int find_opener(const List *l, int close)
{
    int opener = opener_of(l->cmds[close].type), indent = l->cmds[close].indent;
    for (int j = close - 1; j >= 0; j--) if (l->cmds[j].indent == indent && l->cmds[j].type == opener) return j;
    return -1;
}

/* The loop a line is inside (the nearest While / Repeat / For Each whose End is after it), or -1. */
static int enclosing_loop(const List *l, int index)
{
    int indent = l->cmds[index].indent;
    for (int j = index - 1; j >= 0; j--)
    {
        if (l->cmds[j].indent >= indent || !is_loop_opener(l->cmds[j].type)) continue;
        if (find_closer(l, j) > index) return j;
        indent = l->cmds[j].indent;
    }
    return -1;
}

static Loop *loop_of(Run *r, const Cmd *opener)
{
    for (int i = 0; i < r->loopCount; i++) if (r->loops[i].opener == opener) return &r->loops[i];
    if (r->loopCount >= 16) return &r->loops[15];
    Loop *l = &r->loops[r->loopCount++];
    l->opener = opener; l->count = l->total = 0;
    return l;
}

static void clear_loop(Run *r, const Cmd *opener)
{
    for (int i = 0; i < r->loopCount; i++) if (r->loops[i].opener == opener) { r->loops[i] = r->loops[--r->loopCount]; return; }
}

/* For Each: element (or cell) number loop->count into its variables; 0 once past the last. */
static int for_each_step(const Cmd *c, Loop *loop)
{
    const char *name = param(c, "Array");
    char formula[200];
    snprintf(formula, sizeof formula, "{%s}", name);
    Value all = value_of_text(formula);
    if (strcmp(param(c, "Of"), "Cell of a grid") == 0)
    {
        if (all.kind != V_GRID) { char t[200]; snprintf(t, sizeof t, "Script error in For Each: there is no grid called \"%s\"", name); game_log(t); return 0; }
        Value cols = value_of_text(strcat(strcpy(formula, "columns({"), name) ? strcat(formula, "})") : formula);
        Value total = value_of_text(strcat(strcat(strcpy(formula, "length({"), name), "})"));
        int w = (int)value_number_of(cols) > 0 ? (int)value_number_of(cols) : 1;
        if (loop->count >= (int)value_number_of(total)) return 0;
        char at[256];
        snprintf(at, sizeof at, "{%s}[%d, %d]", name, loop->count % w, loop->count / w);
        set_named(param(c, "Variable"), value_of_text(at));
        set_named(param(c, "Index"), value_number(loop->count % w));
        set_named(param(c, "Index2"), value_number(loop->count / w));
        return 1;
    }
    if (all.kind != V_LIST) { char t[200]; snprintf(t, sizeof t, "Script error in For Each: there is no array called \"%s\"", name); game_log(t); return 0; }
    Value total = value_of_text(strcat(strcat(strcpy(formula, "length({"), name), "})"));
    if (loop->count >= (int)value_number_of(total)) return 0;
    char at[256];
    snprintf(at, sizeof at, "{%s}[%d]", name, loop->count);
    set_named(param(c, "Variable"), value_of_text(at));
    set_named(param(c, "Index"), value_number(loop->count));
    return 1;
}

/* Into a Common Event: the caller's place kept, the common list from its start. */
static void call_common(Run *r, const Common *common)
{
    if (!r->callers) r->callers = (CallFrame *)calloc(MAX_CALL_DEPTH + 1, sizeof(CallFrame));
    CallFrame *f = &r->callers[r->depth++];
    f->list = r->list; f->pc = r->pc;
    memcpy(f->returns, r->returns, sizeof f->returns); f->returnCount = r->returnCount;
    memcpy(f->loops, r->loops, sizeof f->loops); f->loopCount = r->loopCount;
    r->list = &common->list; r->pc = 0; r->returnCount = 0; r->loopCount = 0;
}

/* Out of a Common Event (its last line, End or Return): back to the caller - 0 when this was the event's own list (the run is over). */
static int return_from(Run *r)
{
    if (r->depth == 0) return 0;
    CallFrame *f = &r->callers[--r->depth];
    r->list = f->list; r->pc = f->pc;
    memcpy(r->returns, f->returns, sizeof r->returns); r->returnCount = f->returnCount;
    memcpy(r->loops, f->loops, sizeof r->loops); r->loopCount = f->loopCount;
    return 1;
}

/* EventCommandInterpreter.Run: on from r->pc till the list ends or something waits (2000 steps a frame at most: a long loop goes on next frame). */
static void continue_run(Run *r)
{
    int guard = 0;
    functionRun = r;
    while (r->used && r->waiting == W_NONE)
    {
        const List *l = r->list;
        if (++guard > 2000) { r->waiting = W_YIELD; return; }
        if (r->pc >= l->count) { if (return_from(r)) continue; end_run(r); return; }
        /* The end of a pinned reply's branch: back to its Show Text. */
        int pin = -1;
        for (int k = r->returnCount - 1; k >= 0; k--) if (r->returns[k][0] == r->pc) { pin = k; break; }
        if (pin >= 0) { r->pc = r->returns[pin][1]; r->returnCount = pin; continue; }

        const Cmd *c = &l->cmds[r->pc];
        switch (c->type)
        {
            case C_End: case C_ReturnFromCommon:
                if (c->type == C_ReturnFromCommon)
                {
                    const char *v = param(c, "Value");
                    while (*v == ' ') v++;
                    if (*v) script_set("Result", formula(c, "Value"));
                }
                script_arena_reset();
                if (return_from(r)) continue;
                end_run(r);
                return;

            /* ---- Loops ---- */
            case C_While:
            {
                int go = condition_of(c);
                script_arena_reset();
                r->pc = go ? r->pc + 1 : find_closer(l, r->pc) + 1;
                continue;
            }
            case C_EndWhile:
                r->pc = find_opener(l, r->pc); /* back to its While, which checks again */
                if (r->pc < 0) r->pc = l->count;
                continue;
            case C_RepeatTimes:
            {
                int total = (int)floor(formula_number(c, "Count"));
                script_arena_reset();
                Loop *loop = loop_of(r, c);
                loop->count = 1; loop->total = total;
                if (total < 1) { clear_loop(r, c); r->pc = find_closer(l, r->pc) + 1; continue; }
                set_named(param(c, "Variable"), value_number(1));
                r->pc++;
                continue;
            }
            case C_EndRepeat:
            {
                int open = find_opener(l, r->pc);
                if (open < 0) { r->pc++; continue; }
                Loop *loop = loop_of(r, &l->cmds[open]);
                if (++loop->count <= loop->total) { set_named(param(&l->cmds[open], "Variable"), value_number(loop->count)); r->pc = open + 1; }
                else { clear_loop(r, &l->cmds[open]); r->pc++; }
                continue;
            }
            case C_ForEach:
            {
                Loop *loop = loop_of(r, c);
                loop->count = 0;
                if (for_each_step(c, loop)) r->pc++;
                else { clear_loop(r, c); r->pc = find_closer(l, r->pc) + 1; }
                script_arena_reset();
                continue;
            }
            case C_EndForEach:
            {
                int open = find_opener(l, r->pc);
                if (open < 0) { r->pc++; continue; }
                Loop *loop = loop_of(r, &l->cmds[open]);
                loop->count++;
                if (for_each_step(&l->cmds[open], loop)) r->pc = open + 1;
                else { clear_loop(r, &l->cmds[open]); r->pc++; }
                script_arena_reset();
                continue;
            }
            case C_BreakLoop:
            {
                int open = enclosing_loop(l, r->pc);
                if (open < 0) { r->pc++; continue; }
                clear_loop(r, &l->cmds[open]);
                r->pc = find_closer(l, open) + 1;
                continue;
            }
            case C_NextLoop:
            {
                int open = enclosing_loop(l, r->pc);
                r->pc = open < 0 ? r->pc + 1 : find_closer(l, open); /* its End moves the loop on */
                continue;
            }

            /* ---- Select Case ---- */
            case C_SelectCase:
            {
                Value value = formula(c, "Formula");
                if (value.kind == V_TEXT) value = value_text(value.s);
                int indent = c->indent, target = -1, otherwise = -1, end = l->count - 1;
                for (int j = r->pc + 1; j < l->count; j++)
                {
                    if (l->cmds[j].indent != indent) continue;
                    int t = l->cmds[j].type;
                    if (t == C_EndSelect) { end = j; break; }
                    if (t == C_CaseElse && otherwise < 0) otherwise = j + 1;
                    if (t == C_Case && target < 0)
                    {
                        static char parts[64][256];
                        int n = script_split_list(param(&l->cmds[j], "Formula"), parts, 64);
                        for (int k = 0; k < n && target < 0; k++) if (value_compare(value_of_text(parts[k]), value) == 0) target = j + 1;
                    }
                }
                script_arena_reset();
                r->pc = target >= 0 ? target : otherwise >= 0 ? otherwise : end + 1;
                continue;
            }
            case C_Case: case C_CaseElse:
            {
                /* the end of the Case that ran: on past End Select */
                int end = -1;
                for (int j = r->pc + 1; j < l->count && end < 0; j++) if (l->cmds[j].indent == c->indent && l->cmds[j].type == C_EndSelect) end = j;
                r->pc = end < 0 ? l->count : end + 1;
                continue;
            }
            case C_EndSelect: r->pc++; continue;

            case C_WaitUntil:
            {
                int met = condition_of(c);
                script_arena_reset();
                r->pc++;
                if (met) continue;
                r->untilCmd = c; r->untilWaited = 0;
                r->untilTimeout = 0;
                parse_number(param(c, "Timeout"), &r->untilTimeout);
                if (r->untilTimeout < 0) r->untilTimeout = 0;
                r->waiting = W_UNTIL;
                return;
            }

            case C_CallCommonEvent:
            {
                int id = atoi(param(c, "CommonEventId"));
                const Common *common = NULL;
                for (int k = 0; k < commonCount; k++) if (commons[k].id == id) common = &commons[k];
                r->pc++;
                if (!common) { game_log("Call Common Event: it isn't there any more - going on."); continue; }
                if (r->depth >= MAX_CALL_DEPTH)
                {
                    char t[200];
                    snprintf(t, sizeof t, "Script error in Call Common Event: \"%s\" calls Common Events more than %d deep (one calling itself?)", common->name, MAX_CALL_DEPTH);
                    game_log(t);
                    continue;
                }
                static char parts[64][256];
                int n = script_split_list(param(c, "Args"), parts, 64);
                Value values[64];
                for (int k = 0; k < n; k++) { values[k] = value_of_text(parts[k]); }
                int k = 0;
                cJSON *p;
                cJSON_ArrayForEach(p, common->parameters) { if (k < n && cJSON_IsString(p)) set_named(p->valuestring, values[k]); k++; }
                script_arena_reset();
                call_common(r, common);
                continue;
            }

            case C_ConditionalBranchSwitch: case C_ConditionalBranchVariable: case C_AdvancedIf:
                r->pc = branch_true(c) ? r->pc + 1 : find_else_or_end(l, r->pc) + 1;
                continue;
            case C_Else: r->pc = find_end_branch(l, r->pc) + 1; continue;
            case C_EndBranch: case C_Label: case C_EndChoices: r->pc++; continue;
            case C_GoToLabel:
            {
                const char *name = param(c, "LabelName");
                int target = -1;
                for (int j = 0; j < l->count && target < 0; j++)
                    if (l->cmds[j].type == C_Label)
                    {
                        const char *a = param(&l->cmds[j], "LabelName"), *b = name;
                        while (*a && *b && (*a | 32) == (*b | 32)) { a++; b++; }
                        if (!*a && !*b) target = j;
                    }
                r->pc = target >= 0 ? target + 1 : r->pc + 1;
                continue;
            }
            case C_CinemaMode: r->cinema = 1; r->pc++; continue;
            case C_EndCinemaMode: r->cinema = 0; r->pc++; continue;
            case C_WhenChoice: r->pc = find_end_choices_from(l, r->pc) + 1; continue;
            case C_StopConversation:
                r->returnCount = 0;
                r->pc++;
                if (dialog_showing()) { r->waiting = W_READ; dialog_after_read(read_done, r); return; }
                continue;
            case C_ShowText:
                if (get(c->params, "Choices") && str(c->params, "Choices")[0])
                {
                    int at = r->pc;
                    if (ask_choice(r, at)) return;
                    r->pc = r->afterBranches;
                    continue;
                }
                /* fall through */
            default:
                r->pc++;
                functionRun = r;
                if (script_data_command(c->type, c->params)) continue; /* variables, texts, arrays, grids, the debug log */
                {
                    int goesOn = execute(r, c);
                    script_arena_reset();
                    if (!goesOn) return;
                }
                continue;
        }
    }
}

static void start_run_as(const List *list, int activator, int byStepping, const char *owner);

static void start_run(const List *list, int activator, int byStepping) { start_run_as(list, activator, byStepping, NULL); }

static void start_run_as(const List *list, int activator, int byStepping, const char *owner)
{
    if (!list->count) return;
    for (int i = 0; i < MAX_RUNS; i++)
        if (!runs[i].used)
        {
            Run *r = &runs[i];
            memset(r, 0, sizeof *r);
            r->used = 1; r->list = list; r->activator = activator; r->byStepping = byStepping;
            if (owner) snprintf(r->owner, sizeof r->owner, "%s", owner);
            else if (activator > 0) snprintf(r->owner, sizeof r->owner, "char:%d", world_actor_character(activator));
            else snprintf(r->owner, sizeof r->owner, "global");
            continue_run(r);
            return;
        }
    game_log("More than 32 events at once - one didn't run.");
}

/* How many times each page fired, for each placed copy of an object (every copy is its own). */
typedef struct CopyCount { int uid, page, count; } CopyCount;
static CopyCount *copyCounts; static int copyCountN, copyCountCap;

static int *count_for(int uid, int page)
{
    for (int i = 0; i < copyCountN; i++) if (copyCounts[i].uid == uid && copyCounts[i].page == page) return &copyCounts[i].count;
    if (copyCountN == copyCountCap) { copyCountCap = copyCountCap ? copyCountCap * 2 : 64; copyCounts = (CopyCount *)realloc(copyCounts, sizeof(CopyCount) * (size_t)copyCountCap); }
    copyCounts[copyCountN] = (CopyCount){ uid, page, 0 };
    return &copyCounts[copyCountN++].count;
}

static int fireCopy; /* the placed copy an object's pages fire for (0: not an object's) */

static int fire_pages_core(Owner *o, int just, int activator, int byStepping);

/* TryFireEventPages: the first page whose conditions are met runs. */
static int fire_pages(Owner *o, int just, int activator, int byStepping)
{
    const char *previousName = evaluatingName;
    evaluatingName = o->name;
    int ran = fire_pages_core(o, just, activator, byStepping);
    evaluatingName = previousName;
    return ran;
}

static int fire_pages_core(Owner *o, int just, int activator, int byStepping)
{
    for (int i = 0; i < o->pageCount; i++)
    {
        Page *p = &o->pages[i];
        int *fired = fireCopy ? count_for(fireCopy, i) : &p->fired;
        cJSON *c = p->cond;
        /* By Enter / Stay / Exit "stepped on by": the player is the one stepping here. */
        const char *by = str(c, "SteppedOnBy");
        int trigger = num(c, "TriggerConditionBased", 0);
        if (by[0] && o->isMapEvent && (just == T_ENTER || just == T_STAY || just == T_EXIT) && trigger == just)
        {
            if (strcmp(by, "Player") != 0 && strcmp(by, "Party") != 0 && atoi(by) != world_actor_character(0)) continue;
        }
        if (!conditions_met(c, just, *fired, -1)) continue;
        (*fired)++;
        if (p->list.count > 0)
        {
            char record[160];
            snprintf(record, sizeof record, "event:%s", o->name); conditions_record(record);              /* Event executed */
            snprintf(record, sizeof record, "eventpage:%s:%d", o->name, i + 1); conditions_record(record); /* and its page number */
            char key[48];
            if (o->isMapEvent) snprintf(key, sizeof key, "event:%d", o->eventId);
            else if (fireCopy) snprintf(key, sizeof key, "obj:%d", world_thing_instance(fireCopy));
            else if (o->itemCategory) snprintf(key, sizeof key, "itemdef:%d:%d", o->itemCategory, o->itemId);
            else snprintf(key, sizeof key, "char:%d", o->characterId);
            start_run_as(&p->list, activator, byStepping, key);
            return 1;
        }
    }
    return 0;
}

/* ---------------- Triggers ---------------- */

static Owner *eventUnder;
static int lastX = -1, lastY = -1, started;
static double stayMs = 1000, alwaysMs = 0;

static Owner *event_at(int x, int y, int floor)
{
    for (int i = 0; i < ownerCount; i++)
        if (owners[i].isMapEvent && owners[i].x == x && owners[i].y == y && owners[i].floor == floor) return &owners[i];
    return NULL;
}

void events_player_moved(void)
{
    int x, y, floor;
    world_player_cell(&x, &y, &floor);
    if (!started) { started = 1; lastX = x; lastY = y; eventUnder = event_at(x, y, floor); return; } /* arriving doesn't set it off */
    if (x == lastX && y == lastY) return;
    lastX = x; lastY = y;
    Owner *leaving = eventUnder;
    if (leaving && (leaving->x != x || leaving->y != y)) fire_pages(leaving, T_EXIT, -1, 0);
    eventUnder = event_at(x, y, floor);
    if (eventUnder) fire_pages(eventUnder, T_ENTER, -1, 0);
}

void events_update(double ms, int paused)
{
    /* By Stay: each second while the player stands on it (a timer of its own - it runs while a box is up too). */
    if ((stayMs -= ms) <= 0)
    {
        stayMs += 1000;
        if (eventUnder) fire_pages(eventUnder, T_STAY, -1, 0);
    }
    /* Every frame, paused or not: long loops go on, Wait Untils are checked, fades and camera moves go on. */
    screenNow += (uint32_t)ms;
    for (int i = 0; i < MAX_RUNS; i++)
    {
        Run *r = &runs[i];
        if (r->used && r->waiting == W_SCREEN)
        {
            r->screenMs += ms;
            double t = r->screenMs >= r->screenTotal ? 1 : r->screenMs / r->screenTotal;
            if (r->camera)
            {
                double e = t * t * (3 - 2 * t); /* ease in and out */
                world_camera_hold(1, r->camFromX + (r->camToX - r->camFromX) * e, r->camFromY + (r->camToY - r->camFromY) * e);
                if (t >= 1 && r->cameraBack) world_camera_hold(0, 0, 0);
            }
            if (t >= 1) { r->waiting = W_NONE; continue_run(r); }
        }
        if (r->used && r->waiting == W_YIELD) { r->waiting = W_NONE; continue_run(r); }
        if (r->used && r->waiting == W_UNTIL)
        {
            r->untilWaited += ms;
            int timedOut = r->untilTimeout > 0 && r->untilWaited >= r->untilTimeout * 1000;
            functionRun = r;
            int met = !timedOut && condition_of(r->untilCmd);
            script_arena_reset();
            if (timedOut || met) { r->waiting = W_NONE; continue_run(r); }
        }
    }
    if (paused) return;
    clock_advance(ms); /* the clock runs while the game does */
    burn_lights();
    lighting_update(ms);
    update_ambient();
    /* Waits run while the game does. */
    for (int i = 0; i < MAX_RUNS; i++)
    {
        Run *r = &runs[i];
        if (r->used && r->waiting == W_TIMER && r->waitMinutes > 0 && total_minutes() - r->waitFromMinute >= r->waitMinutes) { r->waiting = W_NONE; r->waitMinutes = 0; continue_run(r); }
        if (r->used && r->waiting == W_TIMER && r->waitMinutes == 0 && (r->timerMs -= ms) <= 0) { r->waiting = W_NONE; continue_run(r); }
        int ok;
        if (r->used && r->waiting == W_ACTION && !action_tick(r, ms, &ok))
        {
            lastResult = ok;
            hold(r, r->walker);
            r->waiting = W_NONE;
            r->act = 0;
            continue_run(r);
        }
        if (r->used && r->waiting == W_WALK && !walk_tick(r, ms, &ok))
        {
            lastResult = ok;
            hold(r, r->walker);
            if (!ok) { char t[128]; snprintf(t, sizeof t, "Someone can't find a way to (%d, %d).", r->goalX, r->goalY); game_log(t); }
            r->waiting = W_NONE;
            continue_run(r);
        }
    }
    /* A character's Always pages: when their conditions come true (checked twice a second). */
    if ((alwaysMs -= ms) <= 0)
    {
        alwaysMs = 500;
        for (int i = 0; i < ownerCount; i++)
        {
            Owner *o = &owners[i];
            if (o->isMapEvent) continue;
            int actor = world_actor_by_character(o->characterId);
            if (actor < 0) continue;
            for (int k = 0; k < o->pageCount; k++)
            {
                Page *p = &o->pages[k];
                if (num(p->cond, "TriggerConditionBased", 0) != T_ALWAYS || p->list.count == 0) continue;
                if (dialog_showing()) return; /* a box went up: the rest wait for the next check */
                evaluatingName = o->name;
                int met = conditions_met(p->cond, T_ALWAYS, p->fired, -1), was = p->met;
                evaluatingName = "";
                p->met = met;
                if (!met || was) continue;
                p->fired++;
                char record[160];
                snprintf(record, sizeof record, "event:%s", o->name); conditions_record(record);
                snprintf(record, sizeof record, "eventpage:%s:%d", o->name, k + 1); conditions_record(record);
                char key[48];
                snprintf(key, sizeof key, "char:%d", o->characterId);
                start_run_as(&p->list, actor, 0, key);
            }
        }
    }
}

void events_talk(int actor)
{
    int id = world_actor_character(actor);
    for (int i = 0; i < ownerCount; i++)
    {
        Owner *o = &owners[i];
        if (o->isMapEvent || o->characterId != id) continue;
        if (!o->talk.count) return; /* nothing to say */
        world_turn_to_player(actor);
        start_run(&o->talk, actor, 0);
        return;
    }
}

/* Testing (--press W...): a switch or variable set from outside. */
void events_set_variable(const char *name, double value) { set_var(name, value); }

/* A character's own pages for something that happened to them (By Equip, By UnEquip...): Self = them. */
void events_fire_character(int characterId, int trigger)
{
    for (int i = 0; i < ownerCount; i++)
        if (!owners[i].isMapEvent && owners[i].itemCategory == 0 && owners[i].characterId == characterId)
        {
            int actor = world_actor_by_character(characterId);
            fire_pages(&owners[i], trigger, actor > 0 ? actor : -1, 0);
            return;
        }
}

/* FireGlobalInventoryStateTrigger: every map event gets its chance (By Having / Not Having An Item). */
void events_fire_map(int trigger)
{
    for (int i = 0; i < ownerCount; i++) if (owners[i].isMapEvent) fire_pages(&owners[i], trigger, -1, 0);
}

/* FireDragTrigger: a container's or book's own pages (By Pickup / Drop Item). */
void events_fire_item(int category, int itemId, int trigger)
{
    for (int i = 0; i < ownerCount; i++)
        if (owners[i].itemCategory == category && owners[i].itemId == itemId) { fire_pages(&owners[i], trigger, -1, 0); return; }
}

void events_log_once(int key, const char *text) { log_once(key, text); }

/* A Custom schedule's commands, run as that character's own (Self = them). */
typedef struct CustomList { cJSON *source; List list; } CustomList;
static CustomList customs[64]; static int customCount;

void events_run_commands(cJSON *commands, int actor)
{
    if (!cJSON_GetArraySize(commands)) return;
    const List *l = NULL;
    for (int i = 0; i < customCount; i++) if (customs[i].source == commands) l = &customs[i].list;
    if (!l && customCount < 64) { customs[customCount].source = commands; customs[customCount].list = read_list(commands); l = &customs[customCount++].list; }
    if (!l) return;
    char key[48];
    snprintf(key, sizeof key, "char:%d", world_actor_character(actor));
    start_run_as(l, actor, 0, key);
}

/* An object's own pages for something that happened to one placed copy of it (By Use, By Enter when someone sits...). 1 if a page with commands ran. */
int events_object_event(int uid, int trigger, int activator)
{
    int objectId = world_thing_object_id(uid);
    /* RecordObjectHappening: destroyed, used; a light lit, put out, burnt out */
    if (trigger == 4) conditions_record_item("destroyed", 4, objectId);
    if (trigger == 2) conditions_record_item("used", 4, objectId);
    if (world_thing_type(uid) == 4)
    {
        if (trigger == 5) conditions_record_item("lit", 4, objectId);
        if (trigger == 9) conditions_record_item("putout", 4, objectId);
        if (trigger == 10) conditions_record_item("burnedout", 4, objectId);
    }
    for (int i = 0; i < ownerCount; i++)
    {
        Owner *o = &owners[i];
        if (o->itemCategory != CAT_OBJECT || o->itemId != objectId) continue;
        int before = 0;
        for (int k = 0; k < MAX_RUNS; k++) before += runs[k].used;
        fireCopy = uid;
        int ran = fire_pages(o, trigger, activator > 0 ? activator : -1, activator > 0);
        fireCopy = 0;
        if (ran)
            for (int k = 0; k < MAX_RUNS; k++) if (runs[k].used && !strncmp(runs[k].owner, "obj:", 4)) runs[k].ownerUid = runs[k].ownerUid ? runs[k].ownerUid : uid;
        (void)before;
        return ran;
    }
    return 0;
}

void world_furniture_event(int uid, int trigger, int actor) { events_object_event(uid, trigger, actor); }

long events_total_minutes(void) { return total_minutes(); }
int events_day(void) { return clockDay; }
double events_variable(const char *name) { return get_var(name); }
int events_last_result(void) { return lastResult; }
