#include "events.h"
#include "commands.h"
#include "dialog.h"
#include "world.h"
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
    Page *pages; int pageCount;
    List talk;
} Owner;

static Owner *owners; static int ownerCount;

typedef struct Var { char *name; double value; } Var;
static Var *vars; static int varCount, varCap;

typedef struct VarDef { int id, type; const char *name; } VarDef;
static VarDef *varDefs; static int varDefCount;

static cJSON *playerCharacter; /* the main player's character (its stats) */

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

static double get_var(const char *name)
{
    for (int i = 0; i < varCount; i++) if (strcmp(vars[i].name, name) == 0) return vars[i].value;
    return 0;
}

static void set_var(const char *name, double value)
{
    for (int i = 0; i < varCount; i++) if (strcmp(vars[i].name, name) == 0) { vars[i].value = value; return; }
    if (varCount == varCap) { varCap = varCap ? varCap * 2 : 64; vars = (Var *)realloc(vars, sizeof(Var) * (size_t)varCap); }
    size_t n = strlen(name) + 1;
    vars[varCount].name = (char *)malloc(n); memcpy(vars[varCount].name, name, n);
    vars[varCount++].value = value;
}

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
    owners = (Owner *)calloc((size_t)(cJSON_GetArraySize(events) + cJSON_GetArraySize(characters) + 1), sizeof(Owner));
    cJSON_ArrayForEach(e, events)
    {
        Owner *o = &owners[ownerCount++];
        o->name = str(e, "Name"); o->isMapEvent = 1;
        o->x = num(e, "X", 0); o->y = num(e, "Y", 0); o->floor = num(e, "Floor", 1);
        o->characterId = -1;
        read_pages(o, get(e, "EventPages"));
    }
    cJSON_ArrayForEach(e, characters)
    {
        Owner *o = &owners[ownerCount++];
        o->name = str(e, "Name"); o->characterId = num(e, "Id", -1);
        read_pages(o, get(e, "EventPages"));
        o->talk = read_list(get(e, "TalkCommands"));
        if (flag(e, "IsTestPlayer")) playerCharacter = e;
    }
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
        case 15: log_once(200, "A Complex Condition uses a Game Condition - not in the player yet (taken as false)."); raw = 0; break;
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
        case 2:
            if (cJSON_GetArraySize(get(c, "UntilConditions")) > 0) log_once(201, "A page cycles Until Condition Met - its game conditions aren't in the player yet (it keeps going).");
            break;
        default: break;
    }
    return result;
}

/* ---------------- Running commands ---------------- */

enum { W_NONE, W_TIMER, W_CHOICE, W_READ, W_WALK };

typedef struct Run
{
    int used;
    const List *list;
    int pc;
    int activator, byStepping;   /* whoever set it off (an actor; -1 = the player), and whether by walking over it */
    int returns[16][2], returnCount;
    int waiting; double timerMs;
    int walker, goalX, goalY, goalFloor, tries, best; /* Walk To Location */
    int cinema;
    /* a Show Text with replies waiting: where it is, where its branches end */
    int choiceAt, afterBranches, choicePinned[DIALOG_MAX_REPLIES], choiceIds[DIALOG_MAX_REPLIES], choiceCount;
} Run;

#define MAX_RUNS 32
static Run runs[MAX_RUNS];

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
    if (c->type == C_AdvancedIf) { log_once(202, "Advance IF isn't in the player yet (taken as false)."); return 0; }
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

static void end_run(Run *r) { r->used = 0; r->cinema = 0; }

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
            int override = t == T_HAVING ? 0 : t == T_NOT_HAVING ? 1 : -1; /* nobody carries anything in the player yet */
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
            if (unit[0] && strcmp(unit, "Seconds") != 0) { log_once(203, "Wait in game minutes or hours isn't in the player yet (the clock comes later) - not waited."); return 1; }
            r->waiting = W_TIMER;
            r->timerMs = v * 1000;
            return 0;
        }
        case C_WalkToLocation: case C_WalkToFloorLocation:
        {
            int actor = actor_for(r, param(c, "Who"));
            if (actor < 0) return 1;
            r->walker = actor; r->goalX = atoi(param(c, "X")); r->goalY = atoi(param(c, "Y"));
            r->goalFloor = 0;
            if (c->type == C_WalkToFloorLocation) { int f = atoi(param(c, "Floor")); r->goalFloor = f < 1 ? 1 : f > 9 ? 9 : f; }
            r->tries = 0; r->best = 1 << 30; r->timerMs = 0;
            r->waiting = W_WALK;
            return 0;
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
    if (floorSteps == 0 && (dx + dy == 0 || (dx + dy == 1 && !world_can_stand(a, r->goalX, r->goalY, r->goalFloor)))) { *ok = 1; return 0; }
    if ((r->timerMs -= ms) > 0) return 1;
    if (a == 0)
    {
        if (++r->tries > 5) { *ok = 0; return 0; }
        if (!world_walk_to(0, r->goalX, r->goalY, r->goalFloor)) r->timerMs = 700; /* no way just now: again shortly */
        return 1;
    }
    int distance = dx + dy + floorSteps;
    if (distance < r->best) { r->best = distance; r->tries = 0; }
    else if (++r->tries >= 5) { *ok = 0; return 0; }
    if (!world_walk_to(a, r->goalX, r->goalY, r->goalFloor)) r->timerMs = 500 + 500 * r->tries; /* someone in the way: shortly again */
    return 1;
}

/* EventCommandInterpreter.Run: on from r->pc till the list ends or something waits. */
static void continue_run(Run *r)
{
    const List *l = r->list;
    int guard = 0;
    while (r->used && r->waiting == W_NONE)
    {
        if (r->pc >= l->count || ++guard > 100000) { end_run(r); return; }
        /* The end of a pinned reply's branch: back to its Show Text. */
        int pin = -1;
        for (int k = r->returnCount - 1; k >= 0; k--) if (r->returns[k][0] == r->pc) { pin = k; break; }
        if (pin >= 0) { r->pc = r->returns[pin][1]; r->returnCount = pin; continue; }

        const Cmd *c = &l->cmds[r->pc];
        switch (c->type)
        {
            case C_End: case C_ReturnFromCommon: end_run(r); return;
            case C_ConditionalBranchSwitch: case C_ConditionalBranchVariable: case C_AdvancedIf:
                r->pc = branch_true(c) ? r->pc + 1 : find_else_or_end(l, r->pc) + 1;
                continue;
            case C_Else: r->pc = find_end_branch(l, r->pc) + 1; continue;
            case C_EndBranch: case C_Label: case C_Comment: case C_EndChoices: r->pc++; continue;
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
                if (!execute(r, c)) return;
                continue;
        }
    }
}

static void start_run(const List *list, int activator, int byStepping)
{
    if (!list->count) return;
    for (int i = 0; i < MAX_RUNS; i++)
        if (!runs[i].used)
        {
            Run *r = &runs[i];
            memset(r, 0, sizeof *r);
            r->used = 1; r->list = list; r->activator = activator; r->byStepping = byStepping;
            continue_run(r);
            return;
        }
    game_log("More than 32 events at once - one didn't run.");
}

/* TryFireEventPages: the first page whose conditions are met runs. */
static int fire_pages(Owner *o, int just, int activator, int byStepping)
{
    for (int i = 0; i < o->pageCount; i++)
    {
        Page *p = &o->pages[i];
        cJSON *c = p->cond;
        /* By Enter / Stay / Exit "stepped on by": the player is the one stepping here. */
        const char *by = str(c, "SteppedOnBy");
        int trigger = num(c, "TriggerConditionBased", 0);
        if (by[0] && o->isMapEvent && (just == T_ENTER || just == T_STAY || just == T_EXIT) && trigger == just)
        {
            if (strcmp(by, "Player") != 0 && strcmp(by, "Party") != 0 && atoi(by) != world_actor_character(0)) continue;
        }
        if (!conditions_met(c, just, p->fired, -1)) continue;
        p->fired++;
        if (p->list.count > 0) { start_run(&p->list, activator, byStepping); return 1; }
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
    if (paused) return;
    /* Waits run while the game does. */
    for (int i = 0; i < MAX_RUNS; i++)
    {
        Run *r = &runs[i];
        if (r->used && r->waiting == W_TIMER && (r->timerMs -= ms) <= 0) { r->waiting = W_NONE; continue_run(r); }
        int ok;
        if (r->used && r->waiting == W_WALK && !walk_tick(r, ms, &ok))
        {
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
                int met = conditions_met(p->cond, T_ALWAYS, p->fired, -1), was = p->met;
                p->met = met;
                if (!met || was) continue;
                p->fired++;
                start_run(&p->list, actor, 0);
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
