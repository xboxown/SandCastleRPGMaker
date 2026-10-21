/*
 * Formulas and script data, as the Windows engine has them
 * (ScriptExpression, ScriptRuntime, ScriptData): numbers and texts in
 * variables, arrays, grids, self switches; formulas with + - * / %,
 * comparisons, and / or / not, texts in quotes, {names with spaces},
 * Items[i], Board[x, y], and the functions random(), round(), part(),
 * join()... plus the game's own (hp(Player), x(Self), items("Torch")...).
 */
#ifndef SANDCASTLE_SCRIPT_H
#define SANDCASTLE_SCRIPT_H

#include "../third_party/cJSON.h"

typedef struct ScriptList ScriptList;
typedef struct ScriptGrid ScriptGrid;

enum { V_NUM, V_TEXT, V_LIST, V_GRID };

/* A value: a number or a text (an array or grid only as a function's argument). Texts from formulas live till script_arena_reset. */
typedef struct Value { int kind; double n; const char *s; ScriptList *list; ScriptGrid *grid; } Value;

Value value_number(double n);
Value value_text(const char *s);            /* copied into the arena */
int value_try_number(Value v, double *out);
double value_number_of(Value v);
const char *value_text_of(Value v);         /* in the arena */
int value_truth(Value v);
int value_compare(Value a, Value b);

/* Evaluates a formula (empty = 0); 0 when it's wrong, with why in error. */
int script_eval(const char *formula, Value *out, char *error, int size);

/* Variables: a text that doesn't read as a number is kept as a text, anything else as a number (by name, as the engine). */
Value script_get(const char *name);
double script_get_number(const char *name);
void script_set(const char *name, Value v);
void script_set_number(const char *name, double v);

/* Self switches: "<owner>|A". */
int script_self_switch(const char *key);
void script_set_self_switch(const char *key, int on);

/* The data commands (Calculate, Text Operation, arrays, grids, Debug Log...); 0 when it isn't one of them. Errors go to Game.log. */
int script_data_command(int type, cJSON *params);

/* Frees the texts formulas made (after each command). */
void script_arena_reset(void);

/* "1, 2, max(a, b)" -> its formulas (commas in brackets and texts don't split); returns how many (up to max), copied into out (each size long). */
int script_split_list(const char *text, char out[][256], int max);

/* The game's own functions: 1 when there is one by that name (events.c). */
int events_game_function(const char *name, const Value *args, int count, Value *out);

#endif
