/* The inventory windows and the mouse: paperdolls, backpack lists, container windows, identify, use, drag and drop (GameTestWindow.Interaction). */
#ifndef SANDCASTLE_GUMPS_H
#define SANDCASTLE_GUMPS_H
#include <stdint.h>

int gumps_mouse_down(int x, int y);
void gumps_mouse_move(int x, int y);
void gumps_mouse_up(int x, int y);
void gumps_update(double ms);
void gumps_draw(uint32_t *pixels, uint32_t ms);
int gumps_open(void);                         /* how many windows are open */
int gumps_close_top(void);                    /* Escape: 0 when none was open */
void gumps_open_paperdoll(int characterId);   /* I, or double-clicking the avatar */
void gumps_open_map_container(int uid);
/* For the paperdolls (dolls.c). */
#include "items.h"
int gumps_dragging_item(const Item *it);
int gumps_dragging_doll_part(const void *state, int part);
const struct DollState *gumps_drag_doll_state(void);
int gumps_drag_doll_part(void);
int gumps_use_up_combined(void);
void gumps_label(const char *text);
void gumps_reset(void);
/* Choosing a target (Get_click): the next press picks someone (actor), something on the map (uid) or carried (item), or a spot. */
typedef struct GumpsPick { int actor, uid, cellX, cellY, floor, ground; Item *item; } GumpsPick;
void gumps_choose_target(const char *prompt, void (*chosen)(void *context, const GumpsPick *pick), void *context);
int gumps_choosing(void);
void gumps_cancel_choosing(void);
int gumps_turn_into(const void *state, int objectId);

#endif
