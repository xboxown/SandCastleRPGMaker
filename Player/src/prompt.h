/* Input Number, Input Text, Select Item: a box asking for a number, some words, or one of the things carried. */
#ifndef SANDCASTLE_PROMPT_H
#define SANDCASTLE_PROMPT_H
#include <stdint.h>

enum { PROMPT_NUMBER, PROMPT_TEXT, PROMPT_LIST };

void prompt_ask(int kind, const char *question, const char *start, int longest, void (*done)(int ok, const char *text, int id, void *context), void *context);
void prompt_add_row(const char *name, int id);   /* a list's rows, after prompt_ask */
int prompt_open(void);                           /* the game waits while it's up */
int prompt_key(int key, int ch);                 /* a key (or a typed letter, key 0): 1 = taken */
int prompt_click(int x, int y, uint32_t now);    /* 1 = taken (it takes every click) */
void prompt_draw(uint32_t *pixels, uint32_t ms);
void prompt_reset(void);

#endif
