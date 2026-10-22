/* Reading books (ShowBookReadingView), Change Book Content / Change Book Settings. */
#ifndef SANDCASTLE_BOOKS_H
#define SANDCASTLE_BOOKS_H
#include <stdint.h>
#include "../third_party/cJSON.h"

int books_read(int bookId);           /* its reading view: 0 if it has nothing to show */
int books_open(void);
int books_click(void);                /* a click while it's open: the next page (1 = taken) */
int books_escape(void);               /* Escape: finished */
void books_draw(uint32_t *pixels, uint32_t ms);

void books_change_content(int bookId, int page, int frame, const char *paragraphs);
void books_change_setting(int bookId, const char *field, const char *value);
const char *books_setting(int bookId, const char *field);   /* NULL: no change */
const char *books_name(int bookId);
int books_has_been_read(int bookId);

void books_save(cJSON *runtime);
void books_restore(cJSON *runtime);
void books_reset(void);

#endif
