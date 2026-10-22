/* Paperdolls made in the PaperDoll Editor, in play: objects' own, forms opened by events, the error box (GameTestWindow.Dolls / DollControls / ScriptDolls). */
#ifndef SANDCASTLE_DOLLS_H
#define SANDCASTLE_DOLLS_H
#include <stdint.h>
#include "items.h"
#include "pictures.h"
#include "../third_party/cJSON.h"

typedef struct DollState DollState;

void dolls_load(void);
cJSON *dolls_design(int id);
void dolls_free_state(void *state);                 /* an item's paperdoll, when the item goes */
void dolls_draw(uint32_t *pixels, uint32_t ms);
void dolls_update(double ms);

/* Opening: an object's (its own By Use first; 1 = it has a paperdoll), a form (token: who hears the answer), the error box. */
int dolls_use_item(Item *it, int uid);
int dolls_open_form(int designId, int modal, int fresh, int token, int title);
int dolls_error(int designId, int number, const char *words, const char *detail, int token);

/* The windows (drawn over the inventory windows; a modal one over everything). */
int dolls_window_count(void);
int dolls_modal_open(void);
void dolls_window_size(int i, int *w, int *h);
int dolls_window_x(int i);
int dolls_window_y(int i);
void dolls_move_window(int i, int x, int y);
int dolls_to_front(int i);
void dolls_close_window(int i);
int dolls_close_top(void);                          /* Escape: 0 when there's none to close */
DollState *dolls_window_state(int i);

/* The mouse: 0 nothing, 1 a part, 2 an item in a part, 3 a window's body. */
int dolls_hit(int x, int y, int *window, int *part, Item **item);
int dolls_click(int window, int part, int x, int y); /* 1 = acted on at once */
void dolls_double_click(int window, int part);
int dolls_part_acts_on_click(int window, int part);
const char *dolls_part_name(int window, int part);
int dolls_part_draggable(int window, int part);
const Sprite *dolls_part_look(int window, int part);
void dolls_part_dropped(int window, int dragged, int x, int y);
int dolls_drop(int window, int x, int y, int topLeftX, int topLeftY, int category, int itemId, Item *(*lift)(void), void (*put_back)(Item *), void (*landed)(Item *), const DollState *payloadDoll, const char *name);
int dolls_take_item(DollState *s, int part, Item *it);
void dolls_restore_item(DollState *s, int part, Item *it, int index);
void dolls_item_taken(DollState *s, int part, Item *it);
cJSON *dolls_fit_for(cJSON *slot, int category, int itemId);

/* Keys: typing in a text box, a modal paperdoll's Escape / Enter. 1 = taken. ch: a letter typed (0: a key). */
int dolls_key(int key, int ch);
int dolls_typing(void);
void dolls_stop_typing(void);

/* Commands. */
int dolls_paperdoll_command(DollState *s, const char *action, int partId, const char *partName, cJSON *params);
int dolls_script_command(const char *command, DollState *context, int designId, const char *partName, const char *value, double number, int valueIsText, double x, double y, double seconds, cJSON *params);
int dolls_close_command(const char *which, DollState *context, int designId);
int dolls_move_command(DollState *context, int designId, int move, double x, double y, int front);

#endif
