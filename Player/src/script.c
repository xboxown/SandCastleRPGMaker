#include "script.h"
#include "world.h"
#include "commands.h"
#include <ctype.h>
#include <math.h>
#include <setjmp.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

double sc_strtod(const char *text, char **end);
void game_log(const char *line);

/* ---------------- The arena: texts made while working a formula out ---------------- */

typedef struct Block { struct Block *next; size_t used, size; char data[1]; } Block;
static Block *arena;

static void *arena_alloc(size_t n)
{
    n = (n + 7) & ~(size_t)7;
    if (!arena || arena->used + n > arena->size)
    {
        size_t size = n > 65536 ? n : 65536;
        Block *b = (Block *)malloc(sizeof(Block) + size);
        b->next = arena; b->used = 0; b->size = size;
        arena = b;
    }
    void *p = arena->data + arena->used;
    arena->used += n;
    return p;
}

void script_arena_reset(void)
{
    while (arena && arena->next) { Block *next = arena->next; free(arena); arena = next; }
    if (arena) arena->used = 0;
}

static char *arena_copy(const char *s, size_t n)
{
    char *c = (char *)arena_alloc(n + 1);
    memcpy(c, s, n);
    c[n] = 0;
    return c;
}

static char *owned_copy(const char *s) { size_t n = strlen(s) + 1; char *c = (char *)malloc(n); memcpy(c, s, n); return c; }

/* ---------------- Values (ScriptValues) ---------------- */

struct ScriptList { Value *items; int count, cap; };
struct ScriptGrid { int cols, rows; Value *cells; };

Value value_number(double n) { Value v = { V_NUM, n, NULL, NULL, NULL }; return v; }
Value value_text(const char *s) { Value v = { V_TEXT, 0, arena_copy(s, strlen(s)), NULL, NULL }; return v; }
static Value text_in_place(const char *s) { Value v = { V_TEXT, 0, s, NULL, NULL }; return v; }

/* double.TryParse, invariant: spaces round it, a sign, a point, an exponent. */
static int text_number(const char *s, double *out)
{
    while (isspace((unsigned char)*s)) s++;
    if (!*s) return 0;
    char *end;
    double v = sc_strtod(s, &end);
    if (end == s) return 0;
    while (isspace((unsigned char)*end)) end++;
    if (*end) return 0;
    *out = v;
    return 1;
}

int value_try_number(Value v, double *out)
{
    if (v.kind == V_NUM) { *out = v.n; return 1; }
    if (v.kind == V_TEXT) return text_number(v.s, out);
    *out = 0;
    return 0;
}

double value_number_of(Value v) { double n; return value_try_number(v, &n) ? n : 0; }

/* ScriptValues.Text: a whole number without a point, else up to 6 decimals. */
static void number_text(double d, char *out, size_t size)
{
    if (d == floor(d) && fabs(d) < 1e15) { snprintf(out, size, "%lld", (long long)d); return; }
    snprintf(out, size, "%.6f", d);
    char *p = out + strlen(out) - 1;
    while (p > out && *p == '0') *p-- = 0;
    if (p > out && *p == '.') *p = 0;
}

const char *value_text_of(Value v)
{
    if (v.kind == V_TEXT) return v.s;
    if (v.kind == V_NUM) { char t[64]; number_text(v.n, t, sizeof t); return arena_copy(t, strlen(t)); }
    return "";
}

static int equal_ignore_case(const char *a, const char *b)
{
    for (; *a && *b; a++, b++) if (toupper((unsigned char)*a) != toupper((unsigned char)*b)) return 0;
    return *a == *b;
}

int value_truth(Value v)
{
    if (v.kind == V_NUM) return v.n != 0;
    if (v.kind == V_TEXT) return v.s[0] && strcmp(v.s, "0") != 0 && !equal_ignore_case(v.s, "false");
    return 0;
}

static int compare_ignore_case(const char *a, const char *b)
{
    for (;; a++, b++)
    {
        int x = toupper((unsigned char)*a), y = toupper((unsigned char)*b);
        if (x != y || !x) return x < y ? -1 : x > y ? 1 : 0;
    }
}

/* Numbers compare as numbers (a text that reads as one too); otherwise as texts, ignoring case. */
int value_compare(Value a, Value b)
{
    double x, y;
    if (value_try_number(a, &x) && value_try_number(b, &y)) return x < y ? -1 : x > y ? 1 : 0;
    return compare_ignore_case(value_text_of(a), value_text_of(b));
}

static int same(Value a, Value b) { return value_compare(a, b) == 0; }

/* A value kept (in a variable, an array): its text owned. */
static Value keep(Value v)
{
    if (v.kind == V_TEXT) { v.s = owned_copy(v.s); return v; }
    if (v.kind != V_NUM) return value_number(0);
    return v;
}

static void unkeep(Value *v) { if (v->kind == V_TEXT) free((char *)v->s); v->kind = V_NUM; v->n = 0; }

/* ---------------- Script data ---------------- */

typedef struct Named { char *name; void *data; double number; } Named;
typedef struct Table { Named *items; int count, cap; int ignoreCase; } Table;

static Table numbers = { 0 }, texts = { NULL, 0, 0, 1 }, lists = { NULL, 0, 0, 1 }, grids = { NULL, 0, 0, 1 }, selfSwitches = { NULL, 0, 0, 1 };

static Named *find(Table *t, const char *name)
{
    for (int i = 0; i < t->count; i++)
        if (t->ignoreCase ? equal_ignore_case(t->items[i].name, name) : strcmp(t->items[i].name, name) == 0) return &t->items[i];
    return NULL;
}

static Named *find_or_add(Table *t, const char *name)
{
    Named *n = find(t, name);
    if (n) return n;
    if (t->count == t->cap) { t->cap = t->cap ? t->cap * 2 : 32; t->items = (Named *)realloc(t->items, sizeof(Named) * (size_t)t->cap); }
    n = &t->items[t->count++];
    n->name = owned_copy(name); n->data = NULL; n->number = 0;
    return n;
}

static void drop(Table *t, const char *name)
{
    Named *n = find(t, name);
    if (!n) return;
    free(n->name);
    *n = t->items[--t->count];
}

double script_get_number(const char *name) { Named *n = find(&numbers, name); return n ? n->number : 0; }
void script_set_number(const char *name, double v) { find_or_add(&numbers, name)->number = v; }

Value script_get(const char *name)
{
    Named *t = find(&texts, name);
    if (t) return text_in_place((const char *)t->data);
    return value_number(script_get_number(name));
}

void script_set(const char *name, Value v)
{
    double n;
    if (v.kind == V_TEXT && !text_number(v.s, &n))
    {
        Named *t = find_or_add(&texts, name);
        free(t->data);
        t->data = owned_copy(v.s);
        return;
    }
    Named *t = find(&texts, name);
    if (t) { free(t->data); drop(&texts, name); }
    script_set_number(name, value_number_of(v));
}

int script_self_switch(const char *key) { return find(&selfSwitches, key) != NULL; }
void script_set_self_switch(const char *key, int on) { if (on) find_or_add(&selfSwitches, key); else drop(&selfSwitches, key); }

/* ---------------- Errors ---------------- */

static jmp_buf failed;
static char why[200];

static void fail(const char *fmt, const char *a, double n)
{
    if (a) snprintf(why, sizeof why, fmt, a, n);
    else snprintf(why, sizeof why, fmt, n);
    longjmp(failed, 1);
}

static ScriptList *list_of(const char *name)
{
    Named *n = find(&lists, name);
    if (!n) fail("there is no array called \"%s\" (Create Array first)", name, 0);
    return (ScriptList *)n->data;
}

static ScriptGrid *grid_of(const char *name)
{
    Named *n = find(&grids, name);
    if (!n) fail("there is no grid called \"%s\" (Create Grid first)", name, 0);
    return (ScriptGrid *)n->data;
}

static Value *cell(ScriptGrid *g, int x, int y)
{
    if (x < 0 || y < 0 || x >= g->cols || y >= g->rows)
    {
        snprintf(why, sizeof why, "cell (%d, %d) is outside the grid (%d x %d)", x, y, g->cols, g->rows);
        longjmp(failed, 1);
    }
    return &g->cells[y * g->cols + x];
}

/* ---------------- The formula tree ---------------- */

enum { N_LIT, N_NAME, N_INDEX, N_NEG, N_NOT, N_BIN, N_CALL };

typedef struct Node
{
    int kind;
    Value literal;            /* its text owned */
    char *name;               /* N_NAME, N_INDEX, N_CALL (lower case for calls) */
    char op[4];
    struct Node *a, *b;       /* N_INDEX: a = first, b = second (or NULL) */
    struct Node **args; int argCount;
} Node;

static Value eval(const Node *n);

static void need(const Node *n, int count)
{
    if (n->argCount < count)
    {
        snprintf(why, sizeof why, "%s() needs %d value%s", n->name, count, count == 1 ? "" : "s");
        longjmp(failed, 1);
    }
}

static unsigned rngState = 2463534242u;
static unsigned next_random(void) { rngState ^= rngState << 13; rngState ^= rngState >> 17; rngState ^= rngState << 5; return rngState; }
static double random_unit(void) { return (next_random() & 0xFFFFFF) / 16777216.0; }

/* Every number among the values (an array's or grid's too). */
static int numbers_of(const Value *a, int count, double *out, int max)
{
    int n = 0;
    for (int i = 0; i < count; i++)
    {
        if (a[i].kind == V_LIST) for (int k = 0; k < a[i].list->count && n < max; k++) out[n++] = value_number_of(a[i].list->items[k]);
        else if (a[i].kind == V_GRID) for (int k = 0; k < a[i].grid->cols * a[i].grid->rows && n < max; k++) out[n++] = value_number_of(a[i].grid->cells[k]);
        else if (n < max) out[n++] = value_number_of(a[i]);
    }
    return n;
}

static const char *find_ignore_case(const char *hay, const char *needle)
{
    size_t n = strlen(needle);
    if (!n) return hay;
    for (const char *p = hay; *p; p++)
    {
        size_t k = 0;
        while (k < n && p[k] && toupper((unsigned char)p[k]) == toupper((unsigned char)needle[k])) k++;
        if (k == n) return p;
    }
    return NULL;
}

static int index_of(Value hay, Value needle)
{
    if (hay.kind == V_LIST) { for (int i = 0; i < hay.list->count; i++) if (same(hay.list->items[i], needle)) return i; return -1; }
    if (hay.kind == V_GRID) { for (int i = 0; i < hay.grid->cols * hay.grid->rows; i++) if (same(hay.grid->cells[i], needle)) return i; return -1; }
    const char *h = value_text_of(hay), *at = find_ignore_case(h, value_text_of(needle));
    return at ? (int)(at - h) : -1;
}

/* replace(t, a, b): every a in t (ignoring case) becomes b. */
static const char *replace_all(const char *t, const char *a, const char *b)
{
    size_t na = strlen(a), nb = strlen(b), cap = strlen(t) + 1, len = 0;
    if (!na) return t;
    char *out = (char *)malloc(cap);
    for (const char *p = t; *p;)
    {
        const char *hit = find_ignore_case(p, a);
        if (hit != p) { if (len + 2 > cap) { cap *= 2; out = (char *)realloc(out, cap); } out[len++] = *p++; continue; }
        if (len + nb + 1 > cap) { cap = (len + nb + 1) * 2; out = (char *)realloc(out, cap); }
        memcpy(out + len, b, nb); len += nb; p += na;
    }
    out[len] = 0;
    char *result = arena_copy(out, len);
    free(out);
    return result;
}

static const char *upper_or_lower(const char *t, int upper)
{
    char *c = arena_copy(t, strlen(t));
    for (char *p = c; *p; p++) *p = (char)(upper ? toupper((unsigned char)*p) : tolower((unsigned char)*p));
    return c;
}

static const char *trimmed(const char *t)
{
    while (isspace((unsigned char)*t)) t++;
    size_t n = strlen(t);
    while (n && isspace((unsigned char)t[n - 1])) n--;
    return arena_copy(t, n);
}

static const char *part_of(const char *s, int start, int count)
{
    int len = (int)strlen(s);
    if (start < 0) start = 0;
    if (start > len) start = len;
    if (count < 0) count = 0;
    if (count > len - start) count = len - start;
    return arena_copy(s + start, (size_t)count);
}

static double clampd(double v, double lo, double hi) { return v < lo ? lo : v > hi ? hi : v; }

static double round_away(double v, int digits)
{
    double scale = pow(10, digits), x = v * scale;
    double r = x < 0 ? -floor(-x + 0.5) : floor(x + 0.5);
    return r / scale;
}

static Value call(const Node *n)
{
    const char *f = n->name;
    if (strcmp(f, "if") == 0) { need(n, 3); return value_truth(eval(n->args[0])) ? eval(n->args[1]) : eval(n->args[2]); }
    Value a[16];
    int count = n->argCount < 16 ? n->argCount : 16;
    for (int i = 0; i < count; i++) a[i] = eval(n->args[i]);
#define N(i) value_number_of(a[i])
#define T(i) value_text_of(a[i])
    if (strcmp(f, "random") == 0) { need(n, 2); int lo = (int)fmin(N(0), N(1)), hi = (int)fmax(N(0), N(1)); return value_number(lo + (int)(next_random() % (unsigned)(hi - lo + 1))); }
    if (strcmp(f, "chance") == 0) { need(n, 1); return value_number(random_unit() * 100 < N(0) ? 1 : 0); }
    if (strcmp(f, "min") == 0 || strcmp(f, "max") == 0 || strcmp(f, "sum") == 0 || strcmp(f, "average") == 0)
    {
        static double nums[100000];
        int k = numbers_of(a, count, nums, 100000);
        double r = 0;
        if (f[1] == 'i') { r = k ? nums[0] : 0; for (int i = 1; i < k; i++) if (nums[i] < r) r = nums[i]; }
        else if (f[1] == 'a') { r = k ? nums[0] : 0; for (int i = 1; i < k; i++) if (nums[i] > r) r = nums[i]; }
        else { for (int i = 0; i < k; i++) r += nums[i]; if (f[0] == 'a') r = k ? r / k : 0; }
        return value_number(r);
    }
    if (strcmp(f, "abs") == 0) { need(n, 1); return value_number(fabs(N(0))); }
    if (strcmp(f, "round") == 0) { need(n, 1); return value_number(count >= 2 ? round_away(N(0), (int)clampd(N(1), 0, 10)) : round_away(N(0), 0)); }
    if (strcmp(f, "floor") == 0) { need(n, 1); return value_number(floor(N(0))); }
    if (strcmp(f, "ceil") == 0) { need(n, 1); return value_number(ceil(N(0))); }
    if (strcmp(f, "sqrt") == 0) { need(n, 1); return value_number(sqrt(fmax(0, N(0)))); }
    if (strcmp(f, "pow") == 0) { need(n, 2); return value_number(pow(N(0), N(1))); }
    if (strcmp(f, "clamp") == 0) { need(n, 3); return value_number(clampd(N(0), fmin(N(1), N(2)), fmax(N(1), N(2)))); }
    if (strcmp(f, "length") == 0 || strcmp(f, "count") == 0)
    {
        need(n, 1);
        if (a[0].kind == V_LIST) return value_number(a[0].list->count);
        if (a[0].kind == V_GRID) return value_number(a[0].grid->cols * a[0].grid->rows);
        return value_number((double)strlen(T(0)));
    }
    if (strcmp(f, "rows") == 0) { need(n, 1); if (a[0].kind != V_GRID) fail("rows() needs a grid%s", "", 0); return value_number(a[0].grid->rows); }
    if (strcmp(f, "columns") == 0 || strcmp(f, "cols") == 0) { need(n, 1); if (a[0].kind != V_GRID) fail("columns() needs a grid%s", "", 0); return value_number(a[0].grid->cols); }
    if (strcmp(f, "contains") == 0) { need(n, 2); return value_number(index_of(a[0], a[1]) >= 0 ? 1 : 0); }
    if (strcmp(f, "indexof") == 0) { need(n, 2); return value_number(index_of(a[0], a[1])); }
    if (strcmp(f, "upper") == 0) { need(n, 1); return text_in_place(upper_or_lower(T(0), 1)); }
    if (strcmp(f, "lower") == 0) { need(n, 1); return text_in_place(upper_or_lower(T(0), 0)); }
    if (strcmp(f, "trim") == 0) { need(n, 1); return text_in_place(trimmed(T(0))); }
    if (strcmp(f, "part") == 0 || strcmp(f, "sub") == 0)
    {
        need(n, 2);
        const char *s = T(0);
        int len = (int)strlen(s), start = (int)clampd(N(1), 0, len);
        return text_in_place(part_of(s, start, count >= 3 ? (int)N(2) : len - start));
    }
    if (strcmp(f, "left") == 0) { need(n, 2); const char *s = T(0); return text_in_place(part_of(s, 0, (int)clampd(N(1), 0, (double)strlen(s)))); }
    if (strcmp(f, "right") == 0) { need(n, 2); const char *s = T(0); int len = (int)strlen(s), k = (int)clampd(N(1), 0, len); return text_in_place(part_of(s, len - k, k)); }
    if (strcmp(f, "replace") == 0) { need(n, 3); return text_in_place(replace_all(T(0), T(1), T(2))); }
    if (strcmp(f, "join") == 0)
    {
        need(n, 1);
        if (a[0].kind != V_LIST) return text_in_place(T(0));
        const char *sep = count >= 2 ? T(1) : ", ";
        size_t total = 1;
        for (int i = 0; i < a[0].list->count; i++) total += strlen(value_text_of(a[0].list->items[i])) + strlen(sep);
        char *out = (char *)arena_alloc(total);
        out[0] = 0;
        for (int i = 0; i < a[0].list->count; i++) { if (i) strcat(out, sep); strcat(out, value_text_of(a[0].list->items[i])); }
        return text_in_place(out);
    }
    if (strcmp(f, "text") == 0) { need(n, 1); return text_in_place(T(0)); }
    if (strcmp(f, "number") == 0) { need(n, 1); return value_number(N(0)); }
#undef N
#undef T
    Value result;
    if (events_game_function(f, a, count, &result)) return result;
    fail("there is no function called %s()", f, 0);
    return value_number(0);
}

static Value eval(const Node *n)
{
    switch (n->kind)
    {
        case N_LIT: return n->literal;
        case N_NAME:
        {
            Named *t = find(&texts, n->name);
            if (t) return text_in_place((const char *)t->data);
            Named *l = find(&lists, n->name);
            if (l) { Value v = { V_LIST, 0, NULL, (ScriptList *)l->data, NULL }; return v; }
            Named *g = find(&grids, n->name);
            if (g) { Value v = { V_GRID, 0, NULL, NULL, (ScriptGrid *)g->data }; return v; }
            return value_number(script_get_number(n->name));
        }
        case N_INDEX:
        {
            if (!n->b)
            {
                ScriptList *l = list_of(n->name);
                int i = (int)value_number_of(eval(n->a));
                if (i < 0 || i >= l->count)
                {
                    snprintf(why, sizeof why, "%s[%d] is outside the array (it has %d, from 0 to %d)", n->name, i, l->count, l->count - 1);
                    longjmp(failed, 1);
                }
                return l->items[i];
            }
            ScriptGrid *g = grid_of(n->name);
            int x = (int)value_number_of(eval(n->a)), y = (int)value_number_of(eval(n->b));
            return *cell(g, x, y);
        }
        case N_NEG: return value_number(-value_number_of(eval(n->a)));
        case N_NOT: return value_number(value_truth(eval(n->a)) ? 0 : 1);
        case N_CALL: return call(n);
        case N_BIN:
        {
            const char *op = n->op;
            if (strcmp(op, "and") == 0) return value_number(value_truth(eval(n->a)) && value_truth(eval(n->b)) ? 1 : 0);
            if (strcmp(op, "or") == 0) return value_number(value_truth(eval(n->a)) || value_truth(eval(n->b)) ? 1 : 0);
            Value x = eval(n->a), y = eval(n->b);
            double p, q;
            switch (op[0])
            {
                case '+':
                    /* two numbers (a typed "12" too) add up; anything else joins as text */
                    if (value_try_number(x, &p) && value_try_number(y, &q)) return value_number(p + q);
                    {
                        const char *s = value_text_of(x), *t = value_text_of(y);
                        size_t ns = strlen(s), nt = strlen(t);
                        char *c = (char *)arena_alloc(ns + nt + 1);
                        memcpy(c, s, ns); memcpy(c + ns, t, nt + 1);
                        return text_in_place(c);
                    }
                case '-': return value_number(value_number_of(x) - value_number_of(y));
                case '*': return value_number(value_number_of(x) * value_number_of(y));
                case '/': { double d = value_number_of(y); if (d == 0) fail("division by zero%s", "", 0); return value_number(value_number_of(x) / d); }
                case '%':
                {
                    double d = value_number_of(y);
                    if (d == 0) fail("remainder of a division by zero%s", "", 0);
                    double r = fmod(value_number_of(x), d);
                    return value_number(r < 0 ? r + fabs(d) : r);
                }
                case '=': return value_number(value_compare(x, y) == 0);
                case '!': return value_number(value_compare(x, y) != 0);
                case '<': return value_number(op[1] == '=' ? value_compare(x, y) <= 0 : value_compare(x, y) < 0);
                case '>': return value_number(op[1] == '=' ? value_compare(x, y) >= 0 : value_compare(x, y) > 0);
            }
            fail("unknown operator %s", op, 0);
        }
    }
    return value_number(0);
}

/* ---------------- Reading a formula ---------------- */

enum { T_NUMBER, T_TEXT, T_NAME, T_OP, T_END };
typedef struct Token { int kind, braced; char *text; } Token;

static Token *toks; static int tokCount, tokCap, at;

static void add_token(int kind, const char *s, size_t n, int braced)
{
    if (tokCount == tokCap) { tokCap = tokCap ? tokCap * 2 : 64; toks = (Token *)realloc(toks, sizeof(Token) * (size_t)tokCap); }
    toks[tokCount].kind = kind; toks[tokCount].braced = braced;
    toks[tokCount].text = (char *)malloc(n + 1);
    memcpy(toks[tokCount].text, s, n); toks[tokCount].text[n] = 0;
    tokCount++;
}

static int is_op(const char *op) { return toks[at].kind == T_OP && strcmp(toks[at].text, op) == 0; }
static int is_word(const char *w) { return toks[at].kind == T_NAME && !toks[at].braced && equal_ignore_case(toks[at].text, w); }

static Node *node(int kind) { Node *n = (Node *)calloc(1, sizeof(Node)); n->kind = kind; return n; }
static Node *binary(const char *op, Node *a, Node *b) { Node *n = node(N_BIN); snprintf(n->op, sizeof n->op, "%s", op); n->a = a; n->b = b; return n; }

static Node *parse_or(void);

static void expect(const char *op)
{
    if (!is_op(op)) fail("a \"%s\" is missing", op, 0);
    at++;
}

static Node *primary(void)
{
    Token *t = &toks[at];
    if (t->kind == T_NUMBER) { at++; Node *n = node(N_LIT); n->literal = value_number(sc_strtod(t->text, NULL)); return n; }
    if (t->kind == T_TEXT) { at++; Node *n = node(N_LIT); n->literal.kind = V_TEXT; n->literal.s = owned_copy(t->text); return n; }
    if (t->kind == T_OP && strcmp(t->text, "(") == 0)
    {
        at++;
        Node *inner = parse_or();
        expect(")");
        return inner;
    }
    if (t->kind == T_NAME)
    {
        int braced = t->braced;
        at++;
        if (!braced && equal_ignore_case(t->text, "true")) { Node *n = node(N_LIT); n->literal = value_number(1); return n; }
        if (!braced && equal_ignore_case(t->text, "false")) { Node *n = node(N_LIT); n->literal = value_number(0); return n; }
        if (!braced && is_op("("))
        {
            at++;
            Node *n = node(N_CALL);
            n->name = owned_copy(t->text);
            for (char *p = n->name; *p; p++) *p = (char)tolower((unsigned char)*p);
            Node *args[64];
            if (!is_op(")"))
            {
                args[n->argCount++] = parse_or();
                while (is_op(",")) { at++; if (n->argCount < 64) args[n->argCount++] = parse_or(); }
            }
            expect(")");
            n->args = (Node **)malloc(sizeof(Node *) * (size_t)(n->argCount ? n->argCount : 1));
            memcpy(n->args, args, sizeof(Node *) * (size_t)n->argCount);
            return n;
        }
        if (is_op("["))
        {
            at++;
            Node *n = node(N_INDEX);
            n->name = owned_copy(t->text);
            n->a = parse_or();
            if (is_op(",")) { at++; n->b = parse_or(); }
            expect("]");
            return n;
        }
        Node *n = node(N_NAME);
        n->name = owned_copy(t->text);
        return n;
    }
    if (t->kind == T_END) fail("the formula ends too soon%s", "", 0);
    fail("didn't expect \"%s\" there", t->text, 0);
    return NULL;
}

static Node *unary_minus(void)
{
    if (is_op("-")) { at++; Node *n = node(N_NEG); n->a = unary_minus(); return n; }
    if (is_op("+")) { at++; return unary_minus(); }
    return primary();
}

static Node *mul(void)
{
    Node *a = unary_minus();
    while (is_op("*") || is_op("/") || is_op("%")) { char op[4]; snprintf(op, sizeof op, "%s", toks[at].text); at++; a = binary(op, a, unary_minus()); }
    return a;
}

static Node *add(void)
{
    Node *a = mul();
    while (is_op("+") || is_op("-")) { char op[4]; snprintf(op, sizeof op, "%s", toks[at].text); at++; a = binary(op, a, mul()); }
    return a;
}

static Node *compare(void)
{
    Node *a = add();
    const char *t = toks[at].text;
    if (toks[at].kind == T_OP && (!strcmp(t, "=") || !strcmp(t, "==") || !strcmp(t, "!=") || !strcmp(t, "<>") || !strcmp(t, "<") || !strcmp(t, ">") || !strcmp(t, "<=") || !strcmp(t, ">=")))
    {
        const char *op = !strcmp(t, "==") ? "=" : !strcmp(t, "<>") ? "!=" : t;
        char keep[4];
        snprintf(keep, sizeof keep, "%s", op);
        at++;
        return binary(keep, a, add());
    }
    return a;
}

static Node *parse_not(void)
{
    if (is_word("not") || is_op("!")) { at++; Node *n = node(N_NOT); n->a = parse_not(); return n; }
    return compare();
}

static Node *parse_and(void)
{
    Node *a = parse_not();
    while (is_word("and") || is_op("&&")) { at++; a = binary("and", a, parse_not()); }
    return a;
}

static Node *parse_or(void)
{
    Node *a = parse_and();
    while (is_word("or") || is_op("||")) { at++; a = binary("or", a, parse_and()); }
    return a;
}

static Node *parse(const char *f)
{
    for (int i = 0; i < tokCount; i++) free(toks[i].text);
    tokCount = 0; at = 0;
    size_t i = 0, len = strlen(f);
    while (i < len)
    {
        char c = f[i];
        if (isspace((unsigned char)c)) { i++; continue; }
        if (isdigit((unsigned char)c) || (c == '.' && i + 1 < len && isdigit((unsigned char)f[i + 1])))
        {
            size_t start = i;
            while (i < len && (isdigit((unsigned char)f[i]) || f[i] == '.')) i++;
            add_token(T_NUMBER, f + start, i - start, 0);
            continue;
        }
        if (c == '"')
        {
            char *buf = (char *)malloc(len + 1);
            size_t n = 0;
            i++;
            for (;;)
            {
                if (i >= len) { free(buf); fail("a text is missing its closing \"%s", "", 0); }
                if (f[i] == '"')
                {
                    if (i + 1 < len && f[i + 1] == '"') { buf[n++] = '"'; i += 2; continue; }
                    i++;
                    break;
                }
                buf[n++] = f[i++];
            }
            add_token(T_TEXT, buf, n, 0);
            free(buf);
            continue;
        }
        if (c == '{')
        {
            const char *close = strchr(f + i + 1, '}');
            if (!close) fail("a name is missing its closing }%s", "", 0);
            const char *s = f + i + 1, *e = close;
            while (s < e && isspace((unsigned char)*s)) s++;
            while (e > s && isspace((unsigned char)e[-1])) e--;
            add_token(T_NAME, s, (size_t)(e - s), 1);
            i = (size_t)(close - f) + 1;
            continue;
        }
        if (isalpha((unsigned char)c) || c == '_' || (unsigned char)c >= 0x80)
        {
            size_t start = i;
            while (i < len && (isalnum((unsigned char)f[i]) || f[i] == '_' || (unsigned char)f[i] >= 0x80)) i++;
            add_token(T_NAME, f + start, i - start, 0);
            continue;
        }
        if (i + 1 < len)
        {
            char two[3] = { c, f[i + 1], 0 };
            if (!strcmp(two, "==") || !strcmp(two, "!=") || !strcmp(two, "<>") || !strcmp(two, "<=") || !strcmp(two, ">=") || !strcmp(two, "&&") || !strcmp(two, "||"))
            { add_token(T_OP, two, 2, 0); i += 2; continue; }
        }
        if (strchr("+-*/%()[],=<>!", c)) { add_token(T_OP, &c, 1, 0); i++; continue; }
        char one[2] = { c, 0 };
        fail("didn't expect \"%s\" there", one, 0);
    }
    add_token(T_END, "", 0, 0);
    Node *n = parse_or();
    if (toks[at].kind != T_END) fail("didn't expect \"%s\" there", toks[at].text, 0);
    return n;
}

/* Parsed once and kept (a loop doesn't read its formula again every turn). */
typedef struct Cached { char *formula; Node *node; char *error; struct Cached *next; } Cached;
static Cached *cache[1024];

static unsigned hash(const char *s) { unsigned h = 2166136261u; for (; *s; s++) h = (h ^ (unsigned char)*s) * 16777619u; return h; }

static int eval_guarded(const char *formula, Value *out, char *error, int size);

/* script_eval may be called while a command is being worked out (its own error jump kept). */
int script_eval(const char *formula, Value *out, char *error, int size)
{
    jmp_buf outer;
    memcpy(outer, failed, sizeof(jmp_buf));
    int ok = eval_guarded(formula, out, error, size);
    memcpy(failed, outer, sizeof(jmp_buf));
    return ok;
}

static int eval_guarded(const char *formula, Value *out, char *error, int size)
{
    const char *f = formula ? formula : "";
    const char *p = f;
    while (isspace((unsigned char)*p)) p++;
    if (!*p) { *out = value_number(0); return 1; }
    unsigned h = hash(f) & 1023;
    Cached *c = cache[h];
    while (c && strcmp(c->formula, f) != 0) c = c->next;
    if (!c)
    {
        c = (Cached *)calloc(1, sizeof(Cached));
        c->formula = owned_copy(f);
        c->next = cache[h];
        cache[h] = c;
        if (setjmp(failed)) c->error = owned_copy(why);
        else c->node = parse(f);
    }
    if (c->error) { snprintf(error, (size_t)size, "%s", c->error); return 0; }
    if (setjmp(failed)) { snprintf(error, (size_t)size, "%s", why); return 0; }
    *out = eval(c->node);
    return 1;
}

int script_split_list(const char *text, char out[][256], int max)
{
    int n = 0, depth = 0, inText = 0;
    const char *start = text;
    for (const char *p = text;; p++)
    {
        char c = *p;
        if (c == '"') inText = !inText;
        else if (!inText && c && strchr("([{", c)) depth++;
        else if (!inText && c && strchr(")]}", c)) depth--;
        if (!c || (!inText && depth == 0 && (c == ',' || c == '\n')))
        {
            const char *s = start, *e = p;
            while (s < e && isspace((unsigned char)*s)) s++;
            while (e > s && isspace((unsigned char)e[-1])) e--;
            if (e > s && n < max) { snprintf(out[n], 256, "%.*s", (int)(e - s), s); n++; }
            start = p + 1;
            if (!c) break;
        }
    }
    return n;
}

/* ---------------- The data commands (ScriptRuntime.Execute) ---------------- */

static cJSON *P;
static int commandType;

static const char *param(const char *k) { const char *s = cJSON_GetStringValue(cJSON_GetObjectItem(P, k)); return s ? s : ""; }

static Value E(const char *k)
{
    Value v;
    char error[200];
    if (!script_eval(param(k), &v, error, sizeof error)) { snprintf(why, sizeof why, "%s", error); longjmp(failed, 1); }
    return v;
}

static int I(const char *k) { return (int)floor(value_number_of(E(k))); }

static const char *name_of(const char *k)
{
    const char *s = trimmed(param(k));
    if (!*s)
    {
        snprintf(why, sizeof why, "no %s name was given", k);
        for (char *p = why; *p; p++) *p = (char)tolower((unsigned char)*p);
        longjmp(failed, 1);
    }
    return s;
}

static void set_var(const char *k, Value v)
{
    const char *name = param(k);
    const char *t = name;
    while (isspace((unsigned char)*t)) t++;
    if (!*t) fail("no variable was picked%s", "", 0);
    script_set(name, v);
}

static ScriptList *new_list(const char *name)
{
    Named *n = find_or_add(&lists, name);
    ScriptList *l = (ScriptList *)n->data;
    if (l) { for (int i = 0; i < l->count; i++) unkeep(&l->items[i]); l->count = 0; }
    else n->data = l = (ScriptList *)calloc(1, sizeof(ScriptList));
    return l;
}

static void list_add(ScriptList *l, Value v, int index)
{
    if (l->count == l->cap) { l->cap = l->cap ? l->cap * 2 : 8; l->items = (Value *)realloc(l->items, sizeof(Value) * (size_t)l->cap); }
    if (index < 0 || index > l->count) index = l->count;
    memmove(&l->items[index + 1], &l->items[index], sizeof(Value) * (size_t)(l->count - index));
    l->items[index] = keep(v);
    l->count++;
}

static void list_remove(ScriptList *l, int i)
{
    unkeep(&l->items[i]);
    memmove(&l->items[i], &l->items[i + 1], sizeof(Value) * (size_t)(l->count - i - 1));
    l->count--;
}

static void put(Value *slot, Value v) { Value k = keep(v); unkeep(slot); *slot = k; }

static ScriptGrid *new_grid(const char *name, int cols, int rows, Value fill)
{
    Named *n = find_or_add(&grids, name);
    ScriptGrid *g = (ScriptGrid *)n->data;
    if (g) { for (int i = 0; i < g->cols * g->rows; i++) unkeep(&g->cells[i]); free(g->cells); }
    else n->data = g = (ScriptGrid *)calloc(1, sizeof(ScriptGrid));
    g->cols = cols < 0 ? 0 : cols; g->rows = rows < 0 ? 0 : rows;
    g->cells = (Value *)calloc((size_t)(g->cols * g->rows + 1), sizeof(Value));
    for (int i = 0; i < g->cols * g->rows; i++) g->cells[i] = keep(fill);
    return g;
}

static int compare_values(const void *a, const void *b) { return value_compare(*(const Value *)a, *(const Value *)b); }
static int compare_values_down(const void *a, const void *b) { return value_compare(*(const Value *)b, *(const Value *)a); }

static int clampi(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

static void execute(void)
{
    switch (commandType)
    {
        case C_Calculate: set_var("Variable", E("Formula")); break;
        case C_TextOperation:
        {
            const char *op = param("Operation");
            const char *a = value_text_of(E("A"));
            Value b = E("B"), c = E("C");
            const char *bt = value_text_of(b);
            Value result = text_in_place(a);
            if (!strcmp(op, "Join")) { size_t na = strlen(a), nb = strlen(bt); char *j = (char *)arena_alloc(na + nb + 1); memcpy(j, a, na); memcpy(j + na, bt, nb + 1); result = text_in_place(j); }
            else if (!strcmp(op, "Length")) result = value_number((double)strlen(a));
            else if (!strcmp(op, "Part")) result = text_in_place(part_of(a, (int)value_number_of(b), trimmed(param("C"))[0] ? (int)value_number_of(c) : 1 << 30));
            else if (!strcmp(op, "Upper case")) result = text_in_place(upper_or_lower(a, 1));
            else if (!strcmp(op, "Lower case")) result = text_in_place(upper_or_lower(a, 0));
            else if (!strcmp(op, "Trim")) result = text_in_place(trimmed(a));
            else if (!strcmp(op, "Replace")) result = text_in_place(bt[0] ? replace_all(a, bt, value_text_of(c)) : a);
            else if (!strcmp(op, "Contains")) result = value_number(find_ignore_case(a, bt) ? 1 : 0);
            else if (!strcmp(op, "Find")) { const char *hit = find_ignore_case(a, bt); result = value_number(hit ? (double)(hit - a) : -1); }
            else if (!strcmp(op, "Number to text")) result = text_in_place(value_text_of(E("A")));
            else if (!strcmp(op, "Text to number")) result = value_number(value_number_of(text_in_place(a)));
            /* a text that reads as a number stays a text when one was asked for */
            if (!strcmp(op, "Join") || !strcmp(op, "Part") || !strcmp(op, "Upper case") || !strcmp(op, "Lower case") || !strcmp(op, "Trim") || !strcmp(op, "Replace") || !strcmp(op, "Number to text"))
            {
                Named *t = find_or_add(&texts, name_of("Variable"));
                free(t->data);
                t->data = owned_copy(value_text_of(result));
            }
            else set_var("Variable", result);
            break;
        }
        case C_SwapVariables:
        {
            Value a = script_get(name_of("A")), b = script_get(name_of("B"));
            if (a.kind == V_TEXT) a = value_text(a.s);
            if (b.kind == V_TEXT) b = value_text(b.s);
            set_var("A", b);
            set_var("B", a);
            break;
        }
        case C_CreateArray:
        {
            int size = clampi(I("Size"), 0, 100000);
            Value fill = E("Fill");
            ScriptList *l = new_list(name_of("Array"));
            for (int i = 0; i < size; i++) list_add(l, fill, -1);
            break;
        }
        case C_SetElement: case C_GetElement:
        {
            ScriptList *l = list_of(name_of("Array"));
            int i = I("Index");
            if (i < 0 || i >= l->count) { snprintf(why, sizeof why, "%s[%d] is outside the array (it has %d)", param("Array"), i, l->count); longjmp(failed, 1); }
            if (commandType == C_SetElement) put(&l->items[i], E("Value"));
            else set_var("Variable", l->items[i]);
            break;
        }
        case C_AddToArray: case C_InsertAt:
        {
            const char *name = name_of("Array");
            Named *n = find(&lists, name);
            ScriptList *l = n ? (ScriptList *)n->data : new_list(name);
            if (commandType == C_AddToArray) list_add(l, E("Value"), -1);
            else { int i = clampi(I("Index"), 0, l->count); list_add(l, E("Value"), i); }
            break;
        }
        case C_RemoveAt:
        {
            ScriptList *l = list_of(name_of("Array"));
            if (!strcmp(param("By"), "First equal to"))
            {
                Value v = E("Index");
                for (int i = 0; i < l->count; i++) if (same(l->items[i], v)) { list_remove(l, i); break; }
            }
            else { int i = I("Index"); if (i >= 0 && i < l->count) list_remove(l, i); }
            break;
        }
        case C_ArrayLength: set_var("Variable", value_number(list_of(name_of("Array"))->count)); break;
        case C_FindInArray:
        {
            Value v = E("Value");
            ScriptList *l = list_of(name_of("Array"));
            int found = -1;
            for (int i = 0; i < l->count && found < 0; i++) if (same(l->items[i], v)) found = i;
            set_var("Variable", value_number(found));
            break;
        }
        case C_ReorderArray:
        {
            ScriptList *l = list_of(name_of("Array"));
            const char *order = param("Order");
            if (!strcmp(order, "Big to small")) qsort(l->items, (size_t)l->count, sizeof(Value), compare_values_down);
            else if (!strcmp(order, "Shuffle")) for (int n = l->count - 1; n > 0; n--) { int k = (int)(next_random() % (unsigned)(n + 1)); Value t = l->items[n]; l->items[n] = l->items[k]; l->items[k] = t; }
            else if (!strcmp(order, "Reverse")) for (int i = 0, j = l->count - 1; i < j; i++, j--) { Value t = l->items[i]; l->items[i] = l->items[j]; l->items[j] = t; }
            else qsort(l->items, (size_t)l->count, sizeof(Value), compare_values);
            break;
        }
        case C_FillArray:
        {
            ScriptList *l = list_of(name_of("Array"));
            Value v = E("Value");
            for (int i = 0; i < l->count; i++) put(&l->items[i], v);
            break;
        }
        case C_ClearArray:
        {
            const char *name = name_of("Array");
            Named *n = find(&lists, name);
            if (n) { ScriptList *l = (ScriptList *)n->data; while (l->count) list_remove(l, l->count - 1); }
            else new_list(name);
            break;
        }
        case C_CopyArray:
        {
            ScriptList *from = list_of(name_of("From"));
            Value copy[1];
            (void)copy;
            ScriptList *to = new_list(name_of("Array"));
            if (to != from) for (int i = 0; i < from->count; i++) list_add(to, from->items[i], -1);
            break;
        }
        case C_ArrayStatistic:
        {
            ScriptList *l = list_of(name_of("Array"));
            const char *what = param("What");
            double r = 0;
            for (int i = 0; i < l->count; i++)
            {
                double v = value_number_of(l->items[i]);
                if (!strcmp(what, "Smallest")) r = i == 0 || v < r ? v : r;
                else if (!strcmp(what, "Biggest")) r = i == 0 || v > r ? v : r;
                else r += v;
            }
            if (!strcmp(what, "Average") && l->count) r /= l->count;
            set_var("Variable", value_number(r));
            break;
        }
        case C_GridFromMap:
        {
            /* the tiles of a map area into a grid (-1 = none) */
            int x0 = atoi(param("X")), y0 = atoi(param("Y")), fl = param("Floor")[0] ? atoi(param("Floor")) : 1;
            ScriptGrid *g = new_grid(name_of("Grid"), clampi(I("Width"), 1, 2000), clampi(I("Height"), 1, 2000), value_number(-1));
            for (int y = 0; y < g->rows; y++)
                for (int x = 0; x < g->cols; x++) *cell(g, x, y) = value_number(world_get_tile(x0 + x, y0 + y, fl));
            break;
        }
        case C_GridToMap:
        {
            /* a grid back onto the map: each cell a tile (below 0: left as it is) */
            int x0 = atoi(param("X")), y0 = atoi(param("Y")), fl = param("Floor")[0] ? atoi(param("Floor")) : 1;
            ScriptGrid *g = grid_of(name_of("Grid"));
            for (int y = 0; y < g->rows; y++)
                for (int x = 0; x < g->cols; x++)
                {
                    int tile = (int)value_number_of(*cell(g, x, y));
                    if (tile >= 0) world_set_tile(x0 + x, y0 + y, fl, tile);
                }
            break;
        }
        case C_CreateGrid: new_grid(name_of("Grid"), clampi(I("Columns"), 0, 2000), clampi(I("Rows"), 0, 2000), E("Fill")); break;
        case C_SetCell: { ScriptGrid *g = grid_of(name_of("Grid")); int x = I("X"), y = I("Y"); put(cell(g, x, y), E("Value")); break; }
        case C_GetCell: { ScriptGrid *g = grid_of(name_of("Grid")); int x = I("X"), y = I("Y"); set_var("Variable", *cell(g, x, y)); break; }
        case C_FillGrid:
        {
            ScriptGrid *g = grid_of(name_of("Grid"));
            Value v = E("Value");
            if (!strcmp(param("Area"), "True"))
            {
                int x0 = I("X"), y0 = I("Y"), w = I("Width"), h = I("Height");
                for (int y = y0 > 0 ? y0 : 0; y < g->rows && y < y0 + h; y++)
                    for (int x = x0 > 0 ? x0 : 0; x < g->cols && x < x0 + w; x++) put(&g->cells[y * g->cols + x], v);
            }
            else for (int i = 0; i < g->cols * g->rows; i++) put(&g->cells[i], v);
            break;
        }
        case C_GridSize:
        {
            ScriptGrid *g = grid_of(name_of("Grid"));
            set_var("Variable", value_number(g->cols));
            if (param("Variable2")[0]) set_var("Variable2", value_number(g->rows));
            break;
        }
        case C_ResizeGrid:
        {
            ScriptGrid *g = grid_of(name_of("Grid"));
            int cols = clampi(I("Columns"), 0, 2000), rows = clampi(I("Rows"), 0, 2000);
            Value fill = E("Fill");
            Value *cells = (Value *)calloc((size_t)(cols * rows + 1), sizeof(Value));
            for (int i = 0; i < cols * rows; i++) cells[i] = keep(fill);
            for (int y = 0; y < g->rows; y++)
                for (int x = 0; x < g->cols; x++)
                {
                    if (x < cols && y < rows) { unkeep(&cells[y * cols + x]); cells[y * cols + x] = g->cells[y * g->cols + x]; }
                    else unkeep(&g->cells[y * g->cols + x]);
                }
            free(g->cells);
            g->cells = cells; g->cols = cols; g->rows = rows;
            break;
        }
        case C_FindInGrid:
        {
            ScriptGrid *g = grid_of(name_of("Grid"));
            Value v = E("Value");
            int found = -1, w = g->cols > 1 ? g->cols : 1;
            for (int i = 0; i < g->cols * g->rows && found < 0; i++) if (same(g->cells[i], v)) found = i;
            set_var("Variable", value_number(found < 0 ? -1 : found % w));
            if (param("Variable2")[0]) set_var("Variable2", value_number(found < 0 ? -1 : found / w));
            break;
        }
        case C_GridLineToArray:
        {
            ScriptGrid *g = grid_of(name_of("Grid"));
            int line = I("Index"), row = strcmp(param("Line"), "Column") != 0;
            Value tmp[4096];
            int n = 0;
            if (row) for (int x = 0; x < g->cols && n < 4096; x++) tmp[n++] = *cell(g, x, line);
            else for (int y = 0; y < g->rows && n < 4096; y++) tmp[n++] = *cell(g, line, y);
            for (int i = 0; i < n; i++) if (tmp[i].kind == V_TEXT) tmp[i] = value_text(tmp[i].s);
            ScriptList *l = new_list(name_of("Array"));
            for (int i = 0; i < n; i++) list_add(l, tmp[i], -1);
            break;
        }
        case C_ArrayToGridLine:
        {
            ScriptGrid *g = grid_of(name_of("Grid"));
            ScriptList *l = list_of(name_of("Array"));
            int line = I("Index"), row = strcmp(param("Line"), "Column") != 0;
            for (int n = 0; n < l->count && n < (row ? g->cols : g->rows); n++) put(row ? cell(g, n, line) : cell(g, line, n), l->items[n]);
            break;
        }
        case C_DebugLog:
        {
            char line[300];
            snprintf(line, sizeof line, "Debug: %s", value_text_of(E("Value")));
            game_log(line);
            break;
        }
        case C_Comment: break;
    }
}

int script_data_command(int type, cJSON *params)
{
    switch (type)
    {
        case C_Calculate: case C_TextOperation: case C_SwapVariables: case C_CreateArray: case C_SetElement: case C_GetElement: case C_AddToArray:
        case C_InsertAt: case C_RemoveAt: case C_ArrayLength: case C_FindInArray: case C_ReorderArray: case C_FillArray: case C_ClearArray:
        case C_CopyArray: case C_ArrayStatistic: case C_CreateGrid: case C_SetCell: case C_GetCell: case C_FillGrid: case C_GridSize:
        case C_ResizeGrid: case C_FindInGrid: case C_GridLineToArray: case C_ArrayToGridLine: case C_DebugLog: case C_Comment: case C_GridFromMap: case C_GridToMap:
            break;
        default: return 0;
    }
    P = params; commandType = type;
    if (setjmp(failed))
    {
        char line[300];
        snprintf(line, sizeof line, "Script error in %s: %s", COMMAND_NAMES[type], why);
        game_log(line);
    }
    else execute();
    script_arena_reset();
    return 1;
}
