/*
 * The inventory windows and the mouse, as the Windows engine has them
 * (GameTestWindow.Interaction and the gumps, from Exult's exult.cc,
 * drag.cc, contain.cc and Paperdoll_gump):
 *  - one mouse pipeline: a press becomes a drag once the pointer moves more
 *    than 2 pixels; a release without that is a click, which identifies
 *    only after the 500 ms double-click window; a second click on the same
 *    thing in time uses it instead;
 *  - a character's paperdoll (its body, what's in each slot, weight), the
 *    loose things they carry (the backpack list), and container windows
 *    (the container's art, its contents where they were put, laid out in
 *    a row when they have no spot yet) - several open at once, the last
 *    clicked on top, dragged by their art, Escape closes the top one;
 *  - drag and drop between the map, the windows, the slots and the
 *    characters, by Exult's rules: what's under the drop point first
 *    (a container takes it), the closest free slot it fits, else the
 *    character's own add rules; reach, weight and volume checked; a
 *    refused drop leaves the thing where it was.
 */
#include "gumps.h"
#include "books.h"
#include "prompt.h"
#include "mechanisms.h"
#include "magic.h"
#include "world.h"
#include "items.h"
#include "events.h"
#include "use.h"
#include "sound.h"
#include "font.h"
#include "combat.h"
#include "vitals.h"
#include "dialog.h"
#include "platform.h"
#include "pictures.h"
#include "dolls.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define DOLL_W 320
#define DOLL_H 400
#define BOX_W 320
#define BOX_H 240
#define DOUBLE_CLICK_MS 500
#define DRAG_SLOP 2
#define SLOT_DROP_RADIUS 32
#define UNIT 8

enum { G_DOLL = 1, G_PACK, G_BOX, G_STATS };
enum { T_NONE, T_THING, T_ACTOR, T_SLOT, T_ROW, T_BOX_ITEM, T_GUMP, T_EXIT, T_DOLL_PART, T_DOLL_ITEM, T_DOLL_BODY, T_BUTTON };
enum { TR_USE = 2, TR_DROP = 3, TR_PICKUP = 6, TR_ENTER = 9 };

static const char *SLOT_NAMES[15] = { "Head", "Neck", "Body", "Arms", "Gloves", "Belt", "Back", "Legs", "Feet", "RightHand", "LeftHand", "Ring1", "Ring2", "Backpack", "Pouch" };

typedef struct Gump { int kind, x, y, characterId, uid; Item *box; cJSON *data; } Gump;
typedef struct Target { int kind, gump, slotKey, uid, actor; Item *item; int doll, part; } Target;
typedef struct Drag { int fromMap, uid, category, itemId, quantity; Item *item; DollState *dollState; int dollPart, dollIndex, partDoll, partDragged; } Drag;
static Drag drag;


static Gump gumps[24]; static int gumpCount;           /* in drawing order: the last is on top */
static Target pressed, lastClick, pendingIdentify;
static int movingDoll = -1;
static int pressing, dragging, movingGump = -1, pressX, pressY, mouseX, mouseY, grabX, grabY, moveDX, moveDY;
static double sinceClick = 1e9, identifyIn = -1;
static const Sprite *ghost;
typedef struct Label { char text[64]; int x, y; uint32_t until; } Label;
static Label labels[8];
static int pressedButtonGump = -1, pressedButton = -1, saveScreenWanted;
static void (*choosing)(void *context, const GumpsPick *pick);
static void *choosingContext;
static uint32_t nowMs;

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static double dbl(cJSON *o, const char *k, double fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valuedouble : fallback; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }
static cJSON *project(void) { return get((cJSON *)world_json(), "Project"); }

static cJSON *character_json(int characterId)
{
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Characters"), "Characters")) if (num(c, "Id", -2) == characterId) return c;
    return NULL;
}

/* A container's data; a body (its id below 0): System Settings' Body container. */
static cJSON *container_def(int id)
{
    if (id < 0) return get(get(project(), "Settings"), "BodyContainer");
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Containers"), "Containers")) if (num(c, "Id", -2) == id) return c;
    return NULL;
}

static const Picture *sprite_now(const Sprite *s) { if (s) sprite_frame_sound(s, nowMs); return s ? sprite_picture_at(s, nowMs) : NULL; }
static int quantize(int n) { return (n + UNIT - 1) / UNIT * UNIT; }

static const Sprite *item_look(const Item *it)
{
    int lit, spent;
    items_light(it, &lit, &spent);
    return world_item_look(items_category(it), items_item_id(it), lit, spent, items_is_locked(it));
}

/* Its size on the screen (as on the map: its picture rounded up to whole 8-pixel units). */
static void item_size(const Item *it, int *w, int *h)
{
    const Picture *p = sprite_now(item_look(it));
    *w = p ? quantize(p->width) : 32; *h = p ? quantize(p->height) : 32;
}

static void draw_item(uint32_t *pixels, const Item *it, int x, int y)
{
    const Picture *p = sprite_now(item_look(it));
    int w, h;
    item_size(it, &w, &h);
    if (p) world_blit(pixels, p, x, y, w, h);
}

/* Whether a point is on a solid pixel of a picture drawn into a box (as blit_box draws it). */
static int solid_at(const Picture *p, int bx, int by, int bw, int bh, int x, int y)
{
    if (!p || p->width <= 0 || p->height <= 0) return 0;
    double s = fmin((double)bw / p->width, (double)bh / p->height);
    int dw = (int)(p->width * s + 0.5), dh = (int)(p->height * s + 0.5), ox = bx + (bw - dw) / 2, oy = by + (bh - dh) / 2;
    if (x < ox || y < oy || x >= ox + dw || y >= oy + dh) return 0;
    return (p->pixels[(size_t)((y - oy) * p->height / dh) * (size_t)p->width + (size_t)((x - ox) * p->width / dw)] >> 24) > 0;
}

/* ---------------- The windows ---------------- */

static double scale_of(const Gump *g) { return g->kind == G_DOLL ? 200.0 / DOLL_H : g->kind == G_BOX ? 200.0 / BOX_H : 1; }
static double atof_safe(const char *s) { double sc_strtod(const char *text, char **end); return sc_strtod(s, NULL); }

static int pack_rows(const Gump *g) { Item *loose[64]; return items_loose(g->characterId, loose, 64); }

static void stats_size(const Gump *g, int *w, int *h);
static const Picture *button_look(const Gump *g, int b, int *x, int *y, int *w, int *h);
static const Picture *stats_close(const Gump *g, int *x, int *y, int *w, int *h);
static void draw_stats(uint32_t *pixels, const Gump *g);
static void press_button(int gi, int b);

static void size_of(const Gump *g, int *w, int *h)
{
    if (g->kind == G_STATS) { stats_size(g, w, h); return; }
    if (g->kind == G_DOLL) { *w = DOLL_W / 2; *h = DOLL_H / 2; return; }
    if (g->kind == G_BOX) { *w = (int)(BOX_W * scale_of(g) + 0.5); *h = 200; return; }
    int rows = pack_rows(g);
    *w = 130; *h = 12 + (rows ? rows : 1) * (font_line_height(font_get("InGameFont")) + 1);
}

static cJSON *paperdoll_of(int characterId) { return get(character_json(characterId), "Paperdoll"); }

/* A slot's spot on the paperdoll (its design's pixels). */
static int slot_spot(int characterId, int slotKey, double *x, double *y)
{
    cJSON *pd = paperdoll_of(characterId);
    *x = 140; *y = 10;
    if (slotKey < 100)
    {
        cJSON *pos = get(get(pd, "SlotPositions"), SLOT_NAMES[slotKey]);
        if (pos) { *x = dbl(pos, "X", 140); *y = dbl(pos, "Y", 10); }
        return 1;
    }
    cJSON *c;
    cJSON_ArrayForEach(c, get(pd, "CustomSlots")) if (100 + num(c, "Id", -1) == slotKey) { *x = dbl(c, "X", 140); *y = dbl(c, "Y", 10); return 1; }
    return 0;
}

/* Every slot on their paperdoll (built-in ones not turned off, then custom ones). */
static int slot_keys(int characterId, int *out)
{
    int n = 0;
    for (int s = 0; s < 15; s++) if (items_slot_shown(characterId, s)) out[n++] = s;
    cJSON *c;
    cJSON_ArrayForEach(c, get(paperdoll_of(characterId), "CustomSlots")) if (n < 64) out[n++] = 100 + num(c, "Id", -1);
    return n;
}

static int find_gump(int kind, int characterId, Item *box, int uid)
{
    for (int i = 0; i < gumpCount; i++)
        if (gumps[i].kind == kind && (kind == G_BOX ? (uid > 0 ? gumps[i].uid == uid : gumps[i].box == box) : gumps[i].characterId == characterId)) return i;
    return -1;
}

static int to_front(int i)
{
    Gump g = gumps[i];
    memmove(&gumps[i], &gumps[i + 1], sizeof(Gump) * (size_t)(gumpCount - i - 1));
    gumps[gumpCount - 1] = g;
    return gumpCount - 1;
}

static int add_gump(Gump g)
{
    if (gumpCount >= 24) return -1;
    int w, h;
    size_of(&g, &w, &h);
    if (g.x + w > SCREEN_W) g.x = SCREEN_W - w;
    if (g.x < 0) g.x = 0;
    gumps[gumpCount++] = g;
    return gumpCount - 1;
}

/* OpenPaperdoll: its own window, or the one already open brought to the front. */
void gumps_open_paperdoll(int characterId)
{
    int i = find_gump(G_DOLL, characterId, NULL, 0);
    if (i >= 0) { to_front(i); return; }
    sound_play(num(paperdoll_of(characterId), "OpenSoundResourceId", -1));
    int dolls = 0;
    for (int k = 0; k < gumpCount; k++) dolls += gumps[k].kind == G_DOLL;
    add_gump((Gump){ G_DOLL, 10 + dolls * 18, 0, characterId, 0, NULL, NULL });
}

static void open_pack(int characterId)
{
    int i = find_gump(G_PACK, characterId, NULL, 0);
    if (i >= 0) { to_front(i); return; }
    int doll = find_gump(G_DOLL, characterId, NULL, 0);
    add_gump((Gump){ G_PACK, doll >= 0 ? gumps[doll].x + 162 : 100, 20, characterId, 0, NULL, NULL });
}

static void open_box(Item *box, int uid)
{
    int i = find_gump(G_BOX, -1, box, uid);
    if (i >= 0) { to_front(i); return; }
    cJSON *data = container_def(items_item_id(box));
    if (!data) return;
    if (items_is_locked(box))
    {
        sound_play(num(data, "LockedRattleSoundResourceId", -1));
        if (uid > 0) world_bark_thing(uid, "Locked", 1000); else game_log("Locked.");
        return;
    }
    /* its own event pages first (a trapped chest): the window opens when none ran */
    if (items_item_id(box) >= 0 && events_fire_item(5, items_item_id(box), TR_USE)) return;
    sound_play(num(data, "OpenSoundResourceId", -1));
    int boxes = 0;
    for (int k = 0; k < gumpCount; k++) boxes += gumps[k].kind == G_BOX;
    add_gump((Gump){ G_BOX, 20 + boxes * 18, 0, -1, uid, box, data });
}

static void close_gump(int i)
{
    Gump g = gumps[i];
    memmove(&gumps[i], &gumps[i + 1], sizeof(Gump) * (size_t)(gumpCount - i - 1));
    gumpCount--;
    if (g.kind == G_BOX)
    {
        sound_play(num(g.data, "CloseSoundResourceId", -1));
        if (items_item_id(g.box) >= 0) events_fire_item(5, items_item_id(g.box), TR_ENTER); /* after it's closed */
    }
    if (g.kind == G_DOLL) { int p = find_gump(G_PACK, g.characterId, NULL, 0); if (p >= 0) close_gump(p); }
}

int gumps_open(void) { return gumpCount; }
int gumps_close_top(void) { if (books_escape()) return 1; if (mechanisms_map_click()) return 1; if (magic_book_open()) { magic_close_book(); return 1; } if (choosing) { choosing = NULL; game_log("Never mind."); return 1; } if (dolls_close_top()) return 1; if (dolls_modal_open()) return 1; if (!gumpCount) return 0; close_gump(gumpCount - 1); return 1; }

/* A map container double-clicked: its window (a never-opened one with its starting contents). */
void gumps_open_map_container(int uid)
{
    int category, itemId, quantity, x, y, instance;
    world_thing_info(uid, &category, &itemId, &quantity, &x, &y, &instance);
    if (category != CAT_CONTAINER) return;
    open_box(items_parked(uid, category, itemId, quantity), uid);
}

/* ---------------- Drawing ---------------- */

static void fill(uint32_t *pixels, int x, int y, int w, int h, uint32_t c)
{
    uint32_t a = c >> 24;
    for (int j = y < 0 ? 0 : y; j < y + h && j < SCREEN_H; j++)
        for (int i = x < 0 ? 0 : x; i < x + w && i < SCREEN_W; i++)
        {
            uint32_t *d = &pixels[j * SCREEN_W + i];
            uint32_t r = (((c >> 16) & 255) * a + ((*d >> 16) & 255) * (255 - a)) / 255, gg = (((c >> 8) & 255) * a + ((*d >> 8) & 255) * (255 - a)) / 255, b = ((c & 255) * a + (*d & 255) * (255 - a)) / 255;
            *d = 0xFF000000u | (r << 16) | (gg << 8) | b;
        }
}

/* A readout (Paperdoll > Expand: weight, labels): its text at its spot, aligned left / centre / right. */
static void draw_readout(uint32_t *pixels, const Gump *g, cJSON *r, const char *text)
{
    if (!flag(r, "Enabled")) return;
    const char *fontName = str(r, "FontName");
    const Font *f = font_get(fontName[0] ? fontName : "InGameFont");
    double s = scale_of(g);
    int scale = num(r, "Scale", 1) > 1 ? num(r, "Scale", 1) : 1;
    int w = font_text_width(f, text) * scale, x = g->x + (int)(dbl(r, "X", 0) * s), y = g->y + (int)(dbl(r, "Y", 0) * s);
    int align = num(r, "Align", 0);
    if (align == 1) x -= w / 2; else if (align == 2) x -= w;
    const char *tint = str(r, "Tint");
    uint32_t colour = tint[0] == '#' && strlen(tint) >= 7 ? 0xFF000000u | (uint32_t)strtoul(tint + (strlen(tint) >= 9 ? 3 : 1), NULL, 16) : 0;
    font_draw_into(f, pixels, SCREEN_W, SCREEN_H, x, y, text, colour, scale, 0);
}

static int being_dragged(const Item *it) { return dragging && pressed.item == it; }
int gumps_dragging_item(const Item *it) { return being_dragged(it); }
int gumps_dragging_doll_part(const void *state, int part) { return dragging && drag.partDragged && drag.dollState == state && drag.dollPart == part; }
const DollState *gumps_drag_doll_state(void) { return dragging && !drag.partDragged ? drag.dollState : NULL; }
int gumps_drag_doll_part(void) { return dragging && drag.dollState && !drag.partDragged ? drag.dollPart : -1; }

static void draw_doll(uint32_t *pixels, const Gump *g)
{
    cJSON *pd = paperdoll_of(g->characterId);
    double s = scale_of(g);
    const Picture *body = sprite_now(sprite_find(str(pd, "BodySpriteGroup"), str(pd, "BodySpriteName")));
    if (body) world_blit(pixels, body, g->x, g->y, DOLL_W / 2, DOLL_H / 2);
    else fill(pixels, g->x, g->y, DOLL_W / 2, DOLL_H / 2, 0xEE1A1A1Au);
    int keys[64], n = slot_keys(g->characterId, keys);
    for (int i = 0; i < n; i++)
    {
        Item *it = items_in_slot(g->characterId, keys[i]);
        double x, y;
        if (!it || being_dragged(it) || !slot_spot(g->characterId, keys[i], &x, &y)) continue;
        draw_item(pixels, it, g->x + (int)(x * s), g->y + (int)(y * s));
    }
    double carried, max;
    items_weights(g->characterId, &carried, &max);
    char text[32];
    snprintf(text, sizeof text, "%.0f", carried); draw_readout(pixels, g, get(pd, "WeightCurrent"), text);
    snprintf(text, sizeof text, "%.0f", max); draw_readout(pixels, g, get(pd, "WeightMax"), text);
    cJSON *label;
    cJSON_ArrayForEach(label, get(pd, "Labels"))
    {
        if (num(label, "Kind", 0) == 0) { draw_readout(pixels, g, label, str(label, "Text")); continue; }
        cJSON *var;
        cJSON_ArrayForEach(var, get(get(project(), "Variables"), "Variables"))
            if (num(var, "Id", -2) == num(label, "VariableId", -1)) { snprintf(text, sizeof text, "%g", events_variable(str(var, "Name"))); draw_readout(pixels, g, label, text); }
    }
    const Picture *exit = sprite_now(sprite_find(str(pd, "ExitButtonSpriteGroup"), str(pd, "ExitButtonSpriteName")));
    if (exit) world_blit(pixels, exit, g->x + (int)((DOLL_W - 28) * s), g->y + (int)(4 * s), (int)(24 * s), (int)(24 * s));
    for (int b = 0; b < 5; b++)
    {
        int bx, by, bw, bh;
        const Picture *pic = button_look(g, b, &bx, &by, &bw, &bh);
        if (pic) world_blit(pixels, pic, bx, by, bw, bh);
    }
}

static void frame_bounds(cJSON *data, double *x, double *y, double *w, double *h)
{
    cJSON *p, *points = get(get(data, "Frame"), "Points");
    double l = 1e9, t = 1e9, r = -1e9, b = -1e9;
    cJSON_ArrayForEach(p, points) { double px = dbl(p, "X", 0), py = dbl(p, "Y", 0); l = fmin(l, px); t = fmin(t, py); r = fmax(r, px); b = fmax(b, py); }
    if (l > r) { l = 60; t = 45; r = 260; b = 195; }
    *x = l; *y = t; *w = r - l; *h = b - t;
}

/* Exult Gump::paint for a container: anything without a spot is laid out in a row from the area's top-left (the row carrying on after those already laid out). */
static void lay_out(const Gump *g)
{
    Item **contents;
    int n = items_contents(g->box, &contents);
    double s = scale_of(g);
    if (n > 1)
    {
        double x0, y0, x1, y1;
        items_spot(contents[0], &x0, &y0);
        int same = 1;
        for (int i = 1; i < n && same; i++) { items_spot(contents[i], &x1, &y1); same = x1 == x0 && y1 == y0; }
        if (same) for (int i = 0; i < n; i++) items_set_spot(contents[i], -100000, -100000, 0);
    }
    double ax, ay, aw, ah;
    frame_bounds(g->data, &ax, &ay, &aw, &ah);
    double curX = 0, curY = 0;
    int loop = 0;
    for (int i = 0; i < n; i++)
    {
        int unplaced = items_unplaced(contents[i]);
        if (!unplaced && !items_auto_placed(contents[i])) continue;
        int iw, ih;
        item_size(contents[i], &iw, &ih);
        double w = iw / s, h = ih / s;
        if (unplaced) items_set_spot(contents[i], ax + fmin(curX + w, aw) - w, ay + fmin(curY + h, ah) - h, 1);
        curX += w - 1;
        if (curX >= aw) { curY += 8; curX = 0; if (curY >= ah) curY = 2 * (++loop); }
    }
}

static void draw_box(uint32_t *pixels, const Gump *g)
{
    double s = scale_of(g);
    const Picture *body = sprite_now(sprite_find(str(g->data, "PaperDollSpriteGroup"), str(g->data, "PaperDollSpriteName")));
    int w, h;
    size_of(g, &w, &h);
    if (body) world_blit(pixels, body, g->x, g->y, w, h);
    else fill(pixels, g->x, g->y, w, h, 0xEE1A1A1Au);
    lay_out(g);
    Item **contents;
    int n = items_contents(g->box, &contents);
    for (int i = 0; i < n; i++)
    {
        if (being_dragged(contents[i])) continue;
        double x, y;
        items_spot(contents[i], &x, &y);
        draw_item(pixels, contents[i], g->x + (int)(x * s), g->y + (int)(y * s));
    }
}

static void item_label(const Item *it, char *out, size_t size)
{
    int q = items_quantity(it);
    if (q > 1) snprintf(out, size, "%s x%d", items_name(items_category(it), items_item_id(it)), q);
    else snprintf(out, size, "%s", items_name(items_category(it), items_item_id(it)));
}

static void draw_pack(uint32_t *pixels, const Gump *g)
{
    int w, h;
    size_of(g, &w, &h);
    fill(pixels, g->x, g->y, w, h, 0xEE1A1A1Au);
    const Font *f = font_get("InGameFont");
    Item *loose[64];
    int n = items_loose(g->characterId, loose, 64), lh = font_line_height(f) + 1;
    for (int i = 0; i < n; i++)
    {
        if (being_dragged(loose[i])) continue;
        char text[96];
        item_label(loose[i], text, sizeof text);
        font_draw(f, pixels, g->x + 6, g->y + 6 + i * lh, text, 0);
    }
}

void gumps_draw(uint32_t *pixels, uint32_t ms)
{
    nowMs = ms;
    for (int i = 0; i < gumpCount; i++)
    {
        if (gumps[i].kind == G_DOLL) draw_doll(pixels, &gumps[i]);
        else if (gumps[i].kind == G_STATS) draw_stats(pixels, &gumps[i]);
        else if (gumps[i].kind == G_BOX) draw_box(pixels, &gumps[i]);
        else draw_pack(pixels, &gumps[i]);
    }
    dolls_draw(pixels, ms); /* PaperDoll Editor paperdolls, over the inventory windows */
    const Font *f = font_get("InGameFont");
    for (int i = 0; i < 8; i++) if (labels[i].until > ms && labels[i].text[0]) font_draw(f, pixels, labels[i].x, labels[i].y, labels[i].text, 0);
    if (dragging && ghost)
    {
        const Picture *p = sprite_now(ghost);
        if (p) world_blit(pixels, p, mouseX - grabX, mouseY - grabY, quantize(p->width), quantize(p->height));
    }
    magic_draw_book(pixels, ms); /* the open spellbook, over every window */
    books_draw(pixels, ms); /* a book being read */
    mechanisms_draw(pixels); /* a map being looked at */
    prompt_draw(pixels, ms); /* a question being answered */
}

static void show_label(const char *text, int x, int y)
{
    int slot = 0;
    for (int i = 0; i < 8; i++) if (labels[i].until <= nowMs) { slot = i; break; }
    const Font *f = font_get("InGameFont");
    int w = font_text_width(f, text);
    x -= w / 2;
    if (x < 0) x = 0;
    if (x + w > SCREEN_W) x = SCREEN_W - w;
    if (y < 0) y = 0;
    snprintf(labels[slot].text, sizeof labels[slot].text, "%s", text);
    labels[slot].x = x; labels[slot].y = y; labels[slot].until = nowMs + 2000;
}

/* ---------------- What the mouse is on ---------------- */

static Target hit(int x, int y)
{
    Target t = { T_NONE, -1, -1, 0, -1, NULL, -1, -1 };
    {
        int w, p;
        Item *it;
        int on = dolls_hit(x, y, &w, &p, &it);
        if (on) { t.kind = on == 1 ? T_DOLL_PART : on == 2 ? T_DOLL_ITEM : T_DOLL_BODY; t.doll = w; t.part = p; t.item = it; return t; }
        if (dolls_modal_open()) return t; /* a modal paperdoll: nothing else can be clicked */
    }
    for (int gi = gumpCount - 1; gi >= 0; gi--)
    {
        Gump *g = &gumps[gi];
        int w, h;
        size_of(g, &w, &h);
        if (x < g->x || y < g->y || x >= g->x + w || y >= g->y + h) continue;
        double s = scale_of(g);
        t.gump = gi;
        if (g->kind == G_PACK)
        {
            Item *loose[64];
            int n = items_loose(g->characterId, loose, 64), row = (y - g->y - 6) / (font_line_height(font_get("InGameFont")) + 1);
            if (y >= g->y + 6 && row >= 0 && row < n) { t.kind = T_ROW; t.item = loose[row]; return t; }
            t.kind = T_GUMP;
            return t;
        }
        if (g->kind == G_BOX)
        {
            Item **contents;
            int n = items_contents(g->box, &contents);
            for (int i = n - 1; i >= 0; i--)
            {
                double ix, iy;
                int iw, ih;
                items_spot(contents[i], &ix, &iy);
                item_size(contents[i], &iw, &ih);
                if (solid_at(sprite_now(item_look(contents[i])), g->x + (int)(ix * s), g->y + (int)(iy * s), iw, ih, x, y)) { t.kind = T_BOX_ITEM; t.item = contents[i]; return t; }
            }
            const Picture *body = sprite_now(sprite_find(str(g->data, "PaperDollSpriteGroup"), str(g->data, "PaperDollSpriteName")));
            if (!body || solid_at(body, g->x, g->y, w, h, x, y)) { t.kind = T_GUMP; return t; }
            t.gump = -1;
            continue; /* see-through: what's behind */
        }
        /* a paperdoll: the exit button, a slot's thing, an empty slot under the body art, the body */
        if (g->kind == G_STATS)
        {
            int cx, cy, cw, ch;
            const Picture *close = stats_close(g, &cx, &cy, &cw, &ch);
            if (close && x >= cx && y >= cy && x < cx + cw && y < cy + ch) { t.kind = T_EXIT; return t; }
            t.kind = T_GUMP;
            return t;
        }
        for (int b = 4; b >= 0; b--)
        {
            int bx, by, bw, bh;
            const Picture *pic = button_look(g, b, &bx, &by, &bw, &bh);
            if (pic && solid_at(pic, bx, by, bw, bh, x, y)) { t.kind = T_BUTTON; t.slotKey = b; return t; }
        }
        cJSON *pd = paperdoll_of(g->characterId);
        int ex = g->x + (int)((DOLL_W - 28) * s), ey = g->y + (int)(4 * s);
        if (x >= ex && y >= ey && x < ex + (int)(24 * s) && y < ey + (int)(24 * s)) { t.kind = T_EXIT; return t; }
        int keys[64], n = slot_keys(g->characterId, keys);
        for (int i = n - 1; i >= 0; i--)
        {
            Item *it = items_in_slot(g->characterId, keys[i]);
            double sx, sy;
            if (!it || !slot_spot(g->characterId, keys[i], &sx, &sy)) continue;
            int iw, ih;
            item_size(it, &iw, &ih);
            if (solid_at(sprite_now(item_look(it)), g->x + (int)(sx * s), g->y + (int)(sy * s), iw, ih, x, y)) { t.kind = T_SLOT; t.slotKey = keys[i]; t.item = it; return t; }
        }
        const Picture *body = sprite_now(sprite_find(str(pd, "BodySpriteGroup"), str(pd, "BodySpriteName")));
        if (body && !solid_at(body, g->x, g->y, w, h, x, y)) { t.gump = -1; continue; }
        double dx = (x - g->x) / s, dy = (y - g->y) / s;
        for (int i = 0; i < n; i++)
        {
            double sx, sy;
            if (keys[i] >= 100 || items_in_slot(g->characterId, keys[i]) || !slot_spot(g->characterId, keys[i], &sx, &sy)) continue;
            if (dx >= sx - 4 && dx < sx + 36 && dy >= sy - 4 && dy < sy + 36) { t.kind = T_SLOT; t.slotKey = keys[i]; return t; }
        }
        t.kind = T_GUMP;
        return t;
    }
    int actor, uid;
    world_hit(x, y, &actor, &uid);
    if (actor >= 0) { t.kind = T_ACTOR; t.actor = actor; return t; }
    if (uid) { t.kind = T_THING; t.uid = uid; }
    return t;
}

static int same_target(const Target *a, const Target *b)
{
    return a->kind == b->kind && a->gump == b->gump && a->slotKey == b->slotKey && a->uid == b->uid && a->actor == b->actor && a->item == b->item && a->doll == b->doll && a->part == b->part;
}

/* ---------------- Click and double click ---------------- */

static const char *thing_name(int uid, char *buf, size_t size)
{
    int category, itemId, quantity, x, y, instance;
    world_thing_info(uid, &category, &itemId, &quantity, &x, &y, &instance);
    if (category == CAT_CONTAINER && itemId <= -1000)
    {
        snprintf(buf, size, "Body of %s", str(character_json(-1000 - itemId), "Name"));
        return buf;
    }
    if (quantity > 1) snprintf(buf, size, "%d %s", quantity, items_name(category, itemId));
    else snprintf(buf, size, "%s", items_name(category, itemId));
    return buf;
}

static void identify(const Target *t)
{
    char name[96];
    switch (t->kind)
    {
        case T_THING: if (world_thing_there(t->uid)) world_bark_thing(t->uid, thing_name(t->uid, name, sizeof name), 2000); break;
        case T_ACTOR:
        {
            const char *n = str(character_json(world_actor_character(t->actor)), "Name");
            world_bark(t->actor, t->actor == 0 && !n[0] ? "Yourself" : n, 2000);
            break;
        }
        case T_DOLL_PART:
            if (t->doll < dolls_window_count() && dolls_part_name(t->doll, t->part)[0]) { show_label(dolls_part_name(t->doll, t->part), mouseX, mouseY - 12); game_log(dolls_part_name(t->doll, t->part)); }
            break;
        case T_DOLL_ITEM:
            if (t->item && t->doll < dolls_window_count()) { item_label(t->item, name, sizeof name); show_label(name, mouseX, mouseY - 12); game_log(name); }
            break;
        case T_SLOT: case T_ROW: case T_BOX_ITEM:
            if (t->item && items_alive(t->item) && t->gump < gumpCount)
            {
                item_label(t->item, name, sizeof name);
                show_label(name, mouseX, mouseY - 12);
                game_log(name);
            }
            break;
    }
}

/* Double-clicking something carried: a container opens; a book or an object - its own By Use events, a light lit or put out. */
static void use_carried(Item *it)
{
    int category = items_category(it), id = items_item_id(it);
    if (category == CAT_CONTAINER) { open_box(it, 0); return; }
    if (category == CAT_OBJECT && dolls_use_item(it, 0)) return; /* its paperdoll (its own By Use first) */
    if (category == CAT_OBJECT && mechanisms_use_carried(it)) return; /* a key, lockpick, keychain, sextant, map, clock, instrument */
    if (category == CAT_OBJECT && magic_use_carried(it)) return; /* a potion, food, a useable object, a spellbook */
    if (events_fire_item(category, id, TR_USE)) return;
    if (category == CAT_OBJECT && items_use_light(it)) return;
    if (category == CAT_BOOK) books_read(id); /* no By Use page of its own: it's read */
}

static void use_target(const Target *t)
{
    if (vitals_incapacitated(world_actor_character(0))) { game_log("You are unconscious."); return; }
    switch (t->kind)
    {
        case T_ACTOR:
            if (t->actor == 0) gumps_open_paperdoll(world_actor_character(0)); /* Exult: double-clicking the avatar opens their inventory */
            else use_actor(t->actor);
            break;
        case T_THING:
        {
            int category, itemId, quantity, x, y, instance;
            world_thing_info(t->uid, &category, &itemId, &quantity, &x, &y, &instance);
            if (!world_reachable(x, y, world_thing_z(t->uid))) { game_log("You can't reach that from here."); return; } /* Game_window::double_clicked: blocked */
            use_thing(t->uid);
            break;
        }
        case T_SLOT:
        {
            Gump *g = &gumps[t->gump];
            if (t->item) use_carried(t->item);
            else if (t->slotKey == 13) open_pack(g->characterId); /* Exult: an empty backpack slot opens their own general contents */
            break;
        }
        case T_ROW: case T_BOX_ITEM: if (t->item && items_alive(t->item)) use_carried(t->item); break;
        case T_DOLL_PART: if (t->doll < dolls_window_count()) dolls_double_click(t->doll, t->part); break;
        case T_DOLL_ITEM: if (t->item) use_carried(t->item); break;
    }
}

static void click_released(const Target *t)
{
    if (t->kind == T_EXIT) { close_gump(t->gump); return; }
    if (t->kind == T_BUTTON)
    {
        Target now = hit(mouseX, mouseY);
        if (now.kind == T_BUTTON && now.gump == t->gump && now.slotKey == t->slotKey) press_button(t->gump, t->slotKey);
        lastClick.kind = T_NONE;
        return;
    }
    if (t->kind == T_DOLL_PART && t->doll < dolls_window_count() && dolls_part_acts_on_click(t->doll, t->part))
    {
        /* buttons, switches, sliders, controls act at once; a second click in time is a double click too (a save loads) */
        int twice = lastClick.kind != T_NONE && same_target(&lastClick, t) && sinceClick < DOUBLE_CLICK_MS;
        dolls_click(t->doll, t->part, mouseX, mouseY);
        identifyIn = -1;
        if (twice) { lastClick.kind = T_NONE; if (t->doll < dolls_window_count()) dolls_double_click(t->doll, t->part); }
        else { lastClick = *t; sinceClick = 0; }
        return;
    }
    if (t->kind == T_DOLL_BODY) { lastClick.kind = T_NONE; return; }
    if (t->kind == T_GUMP || t->kind == T_NONE) { lastClick.kind = T_NONE; return; }
    if (lastClick.kind != T_NONE && same_target(&lastClick, t) && sinceClick < DOUBLE_CLICK_MS)
    {
        lastClick.kind = T_NONE;
        identifyIn = -1;
        use_target(t);
        return;
    }
    lastClick = *t;
    sinceClick = 0;
    pendingIdentify = *t;
    identifyIn = DOUBLE_CLICK_MS; /* Exult: identify waits out the double-click window */
}

/* ---------------- Dragging ---------------- */


static int start_drag(const Target *t)
{
    if (vitals_incapacitated(world_actor_character(0)) || vitals_game_over()) return 0;
    memset(&drag, 0, sizeof drag);
    if (t->kind == T_THING)
    {
        int x, y, instance;
        world_thing_info(t->uid, &drag.category, &drag.itemId, &drag.quantity, &x, &y, &instance);
        if (!items_can_lift(drag.category, drag.itemId) || drag.itemId <= -1000) { game_log("That's too heavy to move."); return 0; }
        if (!world_reachable(x, y, world_thing_z(t->uid))) { game_log("You can't reach that from here."); return 0; }
        drag.fromMap = 1; drag.uid = t->uid;
        ghost = world_thing_sprite(t->uid);
    }
    else if (t->kind == T_DOLL_ITEM && t->item)
    {
        drag.item = t->item; drag.dollState = dolls_window_state(t->doll); drag.dollPart = t->part;
        drag.category = items_category(t->item); drag.itemId = items_item_id(t->item); drag.quantity = items_quantity(t->item);
        ghost = item_look(t->item);
    }
    else if (t->kind == T_DOLL_PART && dolls_part_draggable(t->doll, t->part) && dolls_part_look(t->doll, t->part))
    {
        /* a part dragged onto another (a key part onto a keyhole): it goes back when let go */
        drag.partDragged = 1; drag.partDoll = t->doll; drag.dollPart = t->part; drag.dollState = dolls_window_state(t->doll);
        ghost = dolls_part_look(t->doll, t->part);
        const Picture *gp = sprite_now(ghost);
        grabX = gp ? gp->width / 2 : 8; grabY = gp ? gp->height / 2 : 8;
        identifyIn = -1; lastClick.kind = T_NONE;
        return 1;
    }
    else if ((t->kind == T_SLOT || t->kind == T_ROW || t->kind == T_BOX_ITEM) && t->item)
    {
        if (t->kind == T_BOX_ITEM && gumps[t->gump].uid > 0)
        {
            int c, i, q, x, y, n;
            world_thing_info(gumps[t->gump].uid, &c, &i, &q, &x, &y, &n);
            if (!world_reachable(x, y, world_thing_z(gumps[t->gump].uid))) { game_log("That's out of reach."); return 0; }
        }
        drag.item = t->item;
        drag.category = items_category(t->item); drag.itemId = items_item_id(t->item); drag.quantity = items_quantity(t->item);
        ghost = item_look(t->item);
    }
    else return 0;
    const Picture *p = sprite_now(ghost);
    grabX = p ? quantize(p->width) / 2 : 8; grabY = p ? quantize(p->height) / 2 : 8;
    identifyIn = -1;
    lastClick.kind = T_NONE;
    events_fire_item(drag.category, drag.itemId, TR_PICKUP);
    return 1;
}

/* The dragged thing in hand: off its map spot (kept parked till it lands) or out of where it was carried. */
static Item *lift(ItemPlace *from)
{
    if (drag.dollState) { drag.dollIndex = dolls_take_item(drag.dollState, drag.dollPart, drag.item); return drag.dollIndex >= 0 ? drag.item : NULL; }
    if (drag.fromMap) return items_unpark(drag.uid, drag.category, drag.itemId, world_thing_quantity(drag.uid));
    return items_detach(drag.item, from) ? drag.item : NULL;
}

static void put_back(Item *it, const ItemPlace *from)
{
    if (drag.dollState) { dolls_restore_item(drag.dollState, drag.dollPart, it, drag.dollIndex); return; }
    if (drag.fromMap) items_repark(drag.uid, it);
    else items_restore(from, it);
}

static void landed(Item *it, const ItemPlace *from)
{
    if (drag.dollState) { dolls_item_taken(drag.dollState, drag.dollPart, drag.item); return; } /* taken out of a paperdoll's slot: By Taken Out */
    if (drag.fromMap) world_thing_remove(drag.uid);
    else items_detach_done(from, it);
}

static void refused(const char *why) { if (why && why[0]) game_log(why); }

static int navigate_sound(int characterId) { sound_play(num(paperdoll_of(characterId), "NavigateSoundResourceId", -1)); return 1; }

/* DropOnCharacter: the character's own add rules. */
static int drop_on_character(int characterId)
{
    ItemPlace from;
    Item *it = lift(&from);
    if (!it) return 0;
    char name[96];
    snprintf(name, sizeof name, "%s", items_name(drag.category, drag.itemId));
    if (!items_give_to(characterId, it)) { refused(items_refusal()); put_back(it, &from); return 0; }
    landed(NULL, &from);
    navigate_sound(characterId);
    char line[140];
    snprintf(line, sizeof line, "Picked up \"%s\".", name);
    game_log(line);
    return 1;
}

static int drop_into(Item *bag, int atSpot, double x, double y)
{
    if (bag == drag.item) return 0;
    ItemPlace from;
    Item *it = lift(&from);
    if (!it) return 0;
    char name[96];
    snprintf(name, sizeof name, "%s", items_name(drag.category, drag.itemId));
    if (!items_put_into(bag, it, atSpot, x, y)) { refused(items_refusal()); put_back(it, &from); return 0; }
    landed(NULL, &from);
    cJSON *data = container_def(items_item_id(bag));
    sound_play(num(data, "NavigateSoundResourceId", -1));
    char line[200];
    snprintf(line, sizeof line, "You put \"%s\" into \"%s\".", name, str(data, "Name"));
    game_log(line);
    return 1;
}

static int drop_on_box(int gi, const Target *under)
{
    Gump *g = &gumps[gi];
    if (g->uid > 0)
    {
        int c, i, q, x, y, n;
        world_thing_info(g->uid, &c, &i, &q, &x, &y, &n);
        if (!world_reachable(x, y, world_thing_z(g->uid))) { game_log("That's out of reach."); return 0; }
    }
    double s = scale_of(g), fx = (mouseX - grabX - g->x) / s, fy = (mouseY - grabY - g->y) / s;
    double ax, ay, aw, ah;
    frame_bounds(g->data, &ax, &ay, &aw, &ah);
    fx = fmin(fmax(fx, ax), ax + fmax(0, aw - 32)); fy = fmin(fmax(fy, ay), ay + fmax(0, ah - 32));
    /* a container icon under the drop point takes it first */
    if (under->kind == T_BOX_ITEM && under->item && under->item != drag.item && items_category(under->item) == CAT_CONTAINER) return drop_into(under->item, 0, 0, 0);
    if (!drag.fromMap && drag.item)
    {
        int ch, slot;
        Item *holder;
        if (items_where(drag.item, &ch, &holder, &slot) && holder == g->box)
        {
            if (flag(g->data, "IsBodyContainer")) { game_log("Things can be taken out of a body, but not put back in."); return 0; }
            items_set_spot(drag.item, fx, fy, 0); /* moved within the window: on top of the pile */
            items_stack_at_top(g->box, drag.item);
            sound_play(num(g->data, "NavigateSoundResourceId", -1));
            return 1;
        }
    }
    return drop_into(g->box, 1, fx, fy);
}

/* Paperdoll_gump::add: a container in the slot under the drop point takes it; else the closest free slot it fits; else the character's add rules. */
static int drop_on_doll(int gi, const Target *under)
{
    Gump *g = &gumps[gi];
    if (under->kind == T_SLOT && under->item && under->item != drag.item && items_category(under->item) == CAT_CONTAINER) return drop_into(under->item, 0, 0, 0);
    double s = scale_of(g), px = (mouseX - g->x) / s, py = (mouseY - g->y) / s, best = SLOT_DROP_RADIUS * SLOT_DROP_RADIUS;
    int keys[64], n = slot_keys(g->characterId, keys), slot = -1;
    for (int i = 0; i < n; i++)
    {
        double sx, sy;
        Item *in = items_in_slot(g->characterId, keys[i]);
        if ((in && in != drag.item) || !slot_spot(g->characterId, keys[i], &sx, &sy)) continue;
        double cx = keys[i] >= 100 ? sx + 30 : sx + 16, cy = keys[i] >= 100 ? sy + 11 : sy + 16, d = (px - cx) * (px - cx) + (py - cy) * (py - cy);
        if (d < best) { best = d; slot = keys[i]; }
    }
    if (slot >= 0)
    {
        ItemPlace from;
        if (!drag.fromMap && items_in_slot(g->characterId, slot) == drag.item) return 0; /* back where it came from */
        Item *it = lift(&from);
        if (!it) return 0;
        if (items_fits_slot(g->characterId, it, slot) && items_equip_at(g->characterId, it, slot))
        {
            landed(NULL, &from);
            navigate_sound(g->characterId);
            return 1;
        }
        const char *why = items_refusal();
        if (why[0]) game_log(why);
        put_back(it, &from);
    }
    return drop_on_character(g->characterId);
}

static int drop_on_map(void)
{
    if (mouseX < 0 || mouseY < 0 || mouseX >= SCREEN_W || mouseY >= SCREEN_H) { game_log("You can't drop that there."); return 0; }
    int actor, uid;
    world_hit(mouseX, mouseY, &actor, &uid);
    /* onto the avatar, or a party member standing there: theirs */
    if (actor == 0) return drop_on_character(world_actor_character(0));
    if (actor > 0 && items_in_party(world_actor_character(actor)) && !vitals_dead(world_actor_character(actor))) return drop_on_character(world_actor_character(actor));
    int x, y, fl;
    world_screen_cell(mouseX, mouseY, &x, &y, &fl); /* the ghost's middle */
    if (x < 0 || y < 0 || x >= world_map_w() || y >= world_map_h()) { game_log("You can't drop that there."); return 0; }
    int px, py, pf;
    world_actor_cell(0, &px, &py, &pf);
    if (!world_reachable(x, y, world_actor_lift(0))) { game_log("You can't reach that from here."); return 0; }
    if (drag.fromMap && !world_thing_there(drag.uid)) return 0;
    /* a stack of the same stackable thing there grows */
    int stack = drag.category == CAT_OBJECT ? world_thing_at_cell(CAT_OBJECT, drag.itemId, x, y, drag.fromMap ? drag.uid : 0) : 0;
    if (stack && items_stackable(drag.category, drag.itemId) && world_thing_quantity(stack) + drag.quantity <= 100)
    {
        ItemPlace from;
        Item *it = lift(&from);
        if (!it) return 0;
        landed(it, &from);
        world_thing_add_quantity(stack, items_quantity(it));
        items_free(it);
        sound_play(num(get(get(project(), "Settings"), "Sounds"), "DropOnMapSoundId", -1));
        events_fire_item(drag.category, drag.itemId, TR_DROP);
        return 1;
    }
    /* a closed container there (one that doesn't take things on top) takes it */
    int box = world_thing_at_cell(CAT_CONTAINER, -1, x, y, drag.fromMap ? drag.uid : 0);
    if (box)
    {
        int c, i, q, bx, by, n;
        world_thing_info(box, &c, &i, &q, &bx, &by, &n);
        cJSON *data = container_def(i);
        if (data && !flag(data, "AllowElevation")) return drop_into(items_parked(box, c, i, q), 0, 0, 0);
    }
    if (drag.fromMap)
    {
        world_thing_drop_at(drag.uid, mouseX - grabX, mouseY - grabY, fl);
    }
    else
    {
        ItemPlace from;
        Item *it = lift(&from);
        if (!it) return 0;
        int newUid = world_thing_put(items_category(it), items_item_id(it), items_quantity(it), x, y, fl);
        if (newUid < 0) { put_back(it, &from); game_log("You can't drop that there."); return 0; }
        items_park(newUid, it);
        world_thing_drop_at(newUid, mouseX - grabX, mouseY - grabY, fl); /* exactly where it was shown */
        landed(it, &from);
    }
    sound_play(num(get(get(project(), "Settings"), "Sounds"), "DropOnMapSoundId", -1));
    events_fire_item(drag.category, drag.itemId, TR_DROP);
    return 1;
}

/* For a paperdoll's drop: the dragged thing lifted, put back, landed. */
static ItemPlace dollFrom;
static Item *doll_lift(void) { return lift(&dollFrom); }
static void doll_put_back(Item *it) { put_back(it, &dollFrom); }
static void doll_landed(Item *it) { landed(it, &dollFrom); }
static int dropping;

/* Paperdoll > Use up the item just combined: one of the dragged thing, while its drop is going on. */
int gumps_use_up_combined(void)
{
    if (!dropping) return 0;
    if (drag.dollState) { Item *it = drag.item; if (items_quantity(it) > 1) { items_use_up_one(it); return 1; } int i = dolls_take_item(drag.dollState, drag.dollPart, it); if (i >= 0) items_free(it); return 1; }
    if (drag.fromMap)
    {
        if (world_thing_quantity(drag.uid) > 1) world_thing_add_quantity(drag.uid, -1);
        else { items_free(items_unpark(drag.uid, drag.category, drag.itemId, 1)); world_thing_remove(drag.uid); }
        return 1;
    }
    items_use_up_one(drag.item);
    return 1;
}

static void drop(void)
{
    Target under = hit(mouseX, mouseY);
    if (drag.partDragged) { if (under.kind == T_DOLL_PART || under.kind == T_DOLL_ITEM || under.kind == T_DOLL_BODY) { if (under.doll == drag.partDoll) dolls_part_dropped(drag.partDoll, drag.dollPart, mouseX, mouseY); } return; }
    if (under.kind == T_DOLL_PART || under.kind == T_DOLL_ITEM || under.kind == T_DOLL_BODY)
    {
        char name[96];
        snprintf(name, sizeof name, "%s", items_name(drag.category, drag.itemId));
        const DollState *payloadDoll = drag.item ? (const DollState *)items_doll(drag.item) : NULL;
        dropping = 1;
        dolls_drop(under.doll, mouseX, mouseY, mouseX - grabX, mouseY - grabY, drag.category, drag.itemId, doll_lift, doll_put_back, doll_landed, payloadDoll, name);
        dropping = 0;
        return;
    }
    if (under.gump >= 0 && under.kind != T_THING && under.kind != T_ACTOR)
    {
        Gump *g = &gumps[under.gump];
        if (g->kind == G_BOX) drop_on_box(under.gump, &under);
        else if (g->kind == G_DOLL) drop_on_doll(under.gump, &under);
        else
        {
            int ch, slot;
            Item *holder;
            if (!drag.fromMap && items_where(drag.item, &ch, &holder, &slot) == 1 && ch == g->characterId && !holder && slot < 0) return; /* back onto its own list */
            drop_on_character(g->characterId);
        }
        return;
    }
    if (under.kind == T_THING && !drag.dollState)
    {
        dropping = 1;
        int sown = mechanisms_drop_on(under.uid, drag.category, drag.itemId);
        dropping = 0;
        if (sown) return;
    }
    drop_on_map();
}

/* ---------------- The mouse ---------------- */

static int pick_target(int x, int y);

int gumps_mouse_down(int x, int y)
{
    mouseX = x; mouseY = y;
    if (events_cinema()) return 1;
    if (prompt_click(x, y, platform_ticks())) return 1; /* a question being answered */
    if (books_click()) return 1; /* a book being read: the next page */
    if (mechanisms_click(x, y)) return 1; /* the sleep slider, a map being looked at, the party asleep */
    if (pick_target(x, y)) return 1; /* a target being chosen: this press picks it */
    if (magic_book_click(x, y, platform_ticks())) return 1; /* the open spellbook takes every click */
    pressed = hit(x, y);
    pressing = 1; dragging = 0; pressX = x; pressY = y;
    if (pressed.kind == T_BUTTON) { pressedButtonGump = pressed.gump; pressedButton = pressed.slotKey; }
    if (pressed.doll >= 0) { int top = dolls_to_front(pressed.doll); pressed.doll = top; }
    if (pressed.kind == T_DOLL_BODY) { movingDoll = pressed.doll; moveDX = x - dolls_window_x(movingDoll); moveDY = y - dolls_window_y(movingDoll); pressing = 0; return 1; }
    if (pressed.kind == T_GUMP) { movingGump = to_front(pressed.gump); moveDX = x - gumps[movingGump].x; moveDY = y - gumps[movingGump].y; pressing = 0; return 1; }
    if (pressed.gump >= 0) { int top = to_front(pressed.gump); if (pressed.kind != T_EXIT) pressed.gump = top; else pressed.gump = top; }
    return 1;
}

void gumps_mouse_move(int x, int y)
{
    mouseX = x; mouseY = y;
    if (movingGump >= 0 && movingGump < gumpCount) { gumps[movingGump].x = x - moveDX; gumps[movingGump].y = y - moveDY; return; }
    if (movingDoll >= 0 && movingDoll < dolls_window_count()) { dolls_move_window(movingDoll, x - moveDX, y - moveDY); return; }
    if (!pressing || dragging) return;
    if (abs(x - pressX) <= DRAG_SLOP && abs(y - pressY) <= DRAG_SLOP) return; /* Exult: wait for greater motion */
    if (start_drag(&pressed)) dragging = 1;
    else pressing = 0;
}

void gumps_mouse_up(int x, int y)
{
    mouseX = x; mouseY = y;
    if (movingGump >= 0) { movingGump = -1; return; }
    if (movingDoll >= 0) { movingDoll = -1; return; }
    if (!pressing) return;
    pressing = 0;
    if (dragging) { dragging = 0; drop(); ghost = NULL; memset(&drag, 0, sizeof drag); return; }
    click_released(&pressed);
}

/* The rest of a frame: identify after the double-click window; windows whose thing is gone close. */
void gumps_update(double ms)
{
    sinceClick += ms;
    dolls_update(ms);
    if (identifyIn >= 0 && (identifyIn -= ms) < 0) { identifyIn = -1; identify(&pendingIdentify); }
    for (int i = gumpCount - 1; i >= 0; i--)
    {
        Gump *g = &gumps[i];
        int gone = g->kind == G_BOX ? (g->uid > 0 ? !world_thing_there(g->uid) : !items_alive(g->box)) : vitals_dead(g->characterId) || world_actor_by_character(g->characterId) < 0;
        if (gone) close_gump(i);
    }
}

/* Paperdoll > Turn into: the object whose paperdoll this is becomes another, where it is (on the map or carried). */
int gumps_turn_into(const void *state, int objectId)
{
    int uid;
    Item *it = items_find_doll(state, &uid);
    if (!it || objectId < 0) return 0;
    char line[200];
    snprintf(line, sizeof line, "\"%s\" becomes \"%s\".", items_name(items_category(it), items_item_id(it)), items_name(CAT_OBJECT, objectId));
    if (uid > 0)
    {
        int c, i, q, x, y, n, fl = world_thing_floor(uid);
        world_thing_info(uid, &c, &i, &q, &x, &y, &n);
        Item *own = items_unpark(uid, c, i, q);
        world_thing_remove(uid);
        items_become(own, objectId);
        int newUid = world_thing_put(CAT_OBJECT, objectId, q, x, y, fl);
        if (newUid >= 0) items_park(newUid, own); else items_free(own);
    }
    else items_become(it, objectId);
    game_log(line);
    return 1;
}


/* Words over the windows for a moment (the Save/Load screen tells the player). */
void gumps_label(const char *text) { show_label(text, SCREEN_W / 2, SCREEN_H - 24); }

/* A new game: every window closed, nothing in hand. */
void gumps_reset(void) { gumpCount = 0; memset(&drag, 0, sizeof drag); pressing = dragging = 0; movingGump = movingDoll = -1; lastClick.kind = pendingIdentify.kind = T_NONE; identifyIn = -1; memset(labels, 0, sizeof labels); }

/* ---------------- Choosing a target (Get_click: "Who drinks it?", "Cast on whom?") ---------------- */


void gumps_choose_target(const char *prompt, void (*chosen)(void *context, const GumpsPick *pick), void *context)
{
    identifyIn = -1;
    choosing = chosen; choosingContext = context;
    char line[200];
    snprintf(line, sizeof line, "%s (Escape to cancel)", prompt);
    game_log(line);
    gumps_label(prompt);
}

int gumps_choosing(void) { return choosing != NULL; }
void gumps_cancel_choosing(void) { choosing = NULL; }

/* The next press while choosing picks: someone, something (on the map or carried), or the spot. */
static int pick_target(int x, int y)
{
    if (!choosing) return 0;
    void (*chosen)(void *, const GumpsPick *) = choosing;
    choosing = NULL;
    Target t = hit(x, y);
    GumpsPick p;
    memset(&p, 0, sizeof p);
    p.actor = -1;
    world_screen_cell(x, y, &p.cellX, &p.cellY, &p.floor);
    if (t.kind == T_ACTOR) p.actor = t.actor;
    else if (t.kind == T_THING) p.uid = t.uid;
    else if ((t.kind == T_SLOT || t.kind == T_ROW) && t.gump >= 0 && t.gump < gumpCount)
    {
        if (t.item) p.item = t.item; else p.actor = world_actor_by_character(gumps[t.gump].characterId); /* a paperdoll's body: its character */
        if (!t.item && p.actor < 0) p.actor = -1;
    }
    else if (t.kind == T_GUMP && t.gump >= 0 && t.gump < gumpCount && gumps[t.gump].kind == G_DOLL) p.actor = world_actor_by_character(gumps[t.gump].characterId);
    else if (t.kind == T_BOX_ITEM || t.kind == T_DOLL_ITEM) p.item = t.item;
    if (p.actor < 0 && !p.uid && !p.item && t.kind == T_NONE) p.ground = 1;
    chosen(choosingContext, &p);
    return 1;
}

/* Locked with a key: its windows close, and those of every container inside it. */
void gumps_close_container(const Item *box)
{
    for (int i = gumpCount - 1; i >= 0; i--)
        if (gumps[i].box && (gumps[i].box == box || items_holds(box, gumps[i].box))) close_gump(i);
}

/* ---------------- The paperdoll's buttons and the stats paperdoll (GameTestWindow.PaperdollButtons, misc_buttons.cc, Stats_gump.cc) ---------------- */

static const char *BUTTONS[5] = { "CombatButton", "CombatModeButton", "StatsButton", "DiskButton", "HaloButton" };

static cJSON *character_of(int characterId)
{
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Characters"), "Characters")) if (num(c, "Id", -2) == characterId) return c;
    return NULL;
}

/* Its picture now (the frame for its state) and where it is; NULL when it isn't on this paperdoll. */
static const Picture *button_look(const Gump *g, int b, int *x, int *y, int *w, int *h)
{
    if (g->kind != G_DOLL) return NULL;
    int player = world_actor_character(0) == g->characterId;
    if ((b == 0 || b == 3) && !player) return NULL; /* combat and disk: the main character's only */
    cJSON *button = get(paperdoll_of(g->characterId), BUTTONS[b]), *c = character_of(g->characterId);
    if (!flag(button, "Enabled")) return NULL;
    const Sprite *sprite = sprite_find(str(get(button, "Sprite"), "SpriteGroup"), str(get(button, "Sprite"), "SpriteName"));
    if (!sprite || !sprite->frameCount) return NULL;
    int frame = b == 0 ? combat_mode() != 0 : b == 1 ? num(c, "AttackMode", 0) : b == 4 ? flag(c, "CombatProtected") : 0;
    if ((b == 2 || b == 3) && dragging == 0 && pressing && pressedButton == b && &gumps[pressedButtonGump] == g) frame = 1; /* heart and disk: pressed while held */
    if (frame >= sprite->frameCount) frame = sprite->frameCount - 1;
    const Picture *p = frame_picture(&sprite->frames[frame < 0 ? 0 : frame]);
    if (!p) return NULL;
    double s = scale_of(g);
    *x = g->x + (int)(dbl(button, "X", 0) * s); *y = g->y + (int)(dbl(button, "Y", 0) * s);
    *w = (int)(p->width * s + 0.5); *h = (int)(p->height * s + 0.5);
    return p;
}

static void open_stats(int characterId);

static void press_button(int gi, int b)
{
    if (gi < 0 || gi >= gumpCount) return;
    int id = gumps[gi].characterId, player = world_actor_character(0) == id;
    cJSON *button = get(paperdoll_of(id), BUTTONS[b]), *c = character_of(id);
    char line[160];
    sound_play(num(button, "ClickSoundResourceId", -1));
    switch (b)
    {
        case 0: combat_toggle_mode(); break;
        case 1:
        {
            /* Combat_mode_button::activate: the next mode; only the player gets the last one (Manual) */
            static const char *NAMES[] = { "Nearest", "Weakest", "Strongest", "Berserk", "Protect", "Defend", "Flank", "Flee", "Random", "Manual" };
            int mode = (num(c, "AttackMode", 0) + 1) % (player ? 10 : 9);
            if (get(c, "AttackMode")) cJSON_ReplaceItemInObject(c, "AttackMode", cJSON_CreateNumber(mode)); else cJSON_AddNumberToObject(c, "AttackMode", mode);
            snprintf(line, sizeof line, "%s now fights: %s.", cJSON_GetStringValue(get(c, "Name")) ? cJSON_GetStringValue(get(c, "Name")) : "", NAMES[mode]);
            game_log(line);
            break;
        }
        case 2: open_stats(id); break;
        case 3: saveScreenWanted = 1; break; /* the save/load screen (the view now is its picture) */
        case 4:
        {
            /* Halo_button::activate: toggled; on, nobody else in the party is protected */
            int on = !flag(c, "CombatProtected");
            if (get(c, "CombatProtected")) cJSON_ReplaceItemInObject(c, "CombatProtected", cJSON_CreateBool(on)); else cJSON_AddBoolToObject(c, "CombatProtected", on);
            if (on)
                for (int a = 0; a < world_actor_count(); a++)
                {
                    int other = world_actor_character(a);
                    cJSON *oc = character_of(other);
                    if (other == id || !oc || (a != 0 && !items_in_party(other))) continue;
                    if (get(oc, "CombatProtected")) cJSON_ReplaceItemInObject(oc, "CombatProtected", cJSON_CreateFalse());
                }
            snprintf(line, sizeof line, on ? "The party protects %s." : "%s is no longer protected.", cJSON_GetStringValue(get(c, "Name")) ? cJSON_GetStringValue(get(c, "Name")) : "");
            game_log(line);
            break;
        }
    }
}

int gumps_save_screen_wanted(void) { int w = saveScreenWanted; saveScreenWanted = 0; return w; }

static cJSON *stats_doll(int characterId) { return get(paperdoll_of(characterId), "StatsDoll"); }

static const Picture *link_picture(cJSON *link, int frame)
{
    const Sprite *s = sprite_find(str(link, "SpriteGroup"), str(link, "SpriteName"));
    if (!s || !s->frameCount) return NULL;
    return frame_picture(&s->frames[frame < s->frameCount ? frame : s->frameCount - 1]);
}

static void stats_size(const Gump *g, int *w, int *h)
{
    const Picture *bg = link_picture(get(stats_doll(g->characterId), "Background"), 0);
    *w = bg ? bg->width : 160; *h = bg ? bg->height : 120;
}

static const Picture *stats_close(const Gump *g, int *x, int *y, int *w, int *h)
{
    cJSON *doll = stats_doll(g->characterId);
    int held = pressing && pressed.kind == T_EXIT && &gumps[pressed.gump] == g;
    const Picture *p = link_picture(get(doll, "CloseButton"), held ? 1 : 0);
    if (!p) return NULL;
    *x = g->x + (int)dbl(doll, "CloseX", 140); *y = g->y + (int)dbl(doll, "CloseY", 4); *w = p->width; *h = p->height;
    return p;
}

/* Z (the player's) or the heart (anyone's): opens it - or closes it when it's open. */
static void open_stats(int characterId)
{
    int i = find_gump(G_STATS, characterId, NULL, 0);
    if (i >= 0) { close_gump(i); return; }
    cJSON *doll = stats_doll(characterId), *c = character_of(characterId);
    char line[200];
    if (!link_picture(get(doll, "Background"), 0) && !cJSON_GetArraySize(get(doll, "Elements")))
    {
        snprintf(line, sizeof line, "%s has no stats paperdoll yet.", cJSON_GetStringValue(get(c, "Name")) ? cJSON_GetStringValue(get(c, "Name")) : "They");
        game_log(line);
        return;
    }
    cJSON *sounds = get(get(project(), "Settings"), "Sounds");
    int own = num(doll, "OpenSoundResourceId", -1) >= 0 ? num(doll, "OpenSoundResourceId", -1) : num(paperdoll_of(characterId), "OpenSoundResourceId", -1);
    sound_play(!cJSON_IsFalse(get(sounds, "PaperdollOpenSoundEnabled")) && num(sounds, "PaperdollOpenSoundId", -1) >= 0 ? num(sounds, "PaperdollOpenSoundId", -1) : own);
    int open = 0;
    for (int k = 0; k < gumpCount; k++) open += gumps[k].kind == G_STATS;
    add_gump((Gump){ G_STATS, 60 + open * 24, 40 + open * 20, characterId, 0, NULL, NULL });
}

static double stat_number(cJSON *c, int statId)
{
    char key[16];
    snprintf(key, sizeof key, "%d", statId);
    const char *v = cJSON_GetStringValue(get(get(c, "StatValues"), key));
    return v && *v ? atof_safe(v) : 0;
}

static uint32_t hex_colour(const char *hex, uint32_t fallback)
{
    if (!hex || hex[0] != '#') return fallback;
    size_t n = strlen(hex);
    if (n == 9) return (uint32_t)strtoul(hex + 1, NULL, 16);           /* #AARRGGBB */
    if (n == 7) return 0xFF000000u | (uint32_t)strtoul(hex + 1, NULL, 16);
    return fallback;
}

/* PaperdollReadouts.Gauge: a bar, a pie filled clockwise from the top, or a sprite's frame (first empty .. last full). */
static void draw_gauge(uint32_t *pixels, int x, int y, cJSON *g, int kind, double fraction)
{
    if (fraction < 0) fraction = 0;
    if (fraction > 1) fraction = 1;
    int w = (int)dbl(g, "Width", 80), h = (int)dbl(g, "Height", 10);
    uint32_t back = hex_colour(str(g, "BackColor"), 0xFF402818u), fillc = hex_colour(str(g, "FillColor"), 0xFFC8A040u);
    if (kind == 2)
    {
        const Sprite *s = sprite_find(str(get(g, "Sprite"), "SpriteGroup"), str(get(g, "Sprite"), "SpriteName"));
        if (!s || !s->frameCount) return;
        int frame = (int)(fraction * s->frameCount);
        if (frame >= s->frameCount) frame = s->frameCount - 1;
        const Picture *p = frame_picture(&s->frames[frame]);
        if (p) world_blit(pixels, p, x, y, p->width, p->height);
        return;
    }
    if (kind == 1)
    {
        int size = w < h ? w : h;
        if (size < 4) size = 4;
        double r = size / 2.0;
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                double dx = i + 0.5 - r, dy = j + 0.5 - r;
                if (dx * dx + dy * dy > r * r) continue;
                double angle = atan2(dx, -dy); /* clockwise from the top */
                if (angle < 0) angle += 2 * 3.14159265358979;
                int px = x + i, py = y + j;
                if (px < 0 || py < 0 || px >= SCREEN_W || py >= SCREEN_H) continue;
                uint32_t c = angle <= fraction * 2 * 3.14159265358979 ? fillc : back;
                if (c >> 24) pixels[py * SCREEN_W + px] = c | 0xFF000000u;
            }
        return;
    }
    if (w < 2) w = 2;
    if (h < 2) h = 2;
    if (back >> 24) fill(pixels, x, y, w, h, back | 0xFF000000u);
    fill(pixels, x, y, (int)(w * fraction), h, fillc | 0xFF000000u);
}

/* Stats_gump::paint: the background, then every element with the character's values now. */
static void draw_stats(uint32_t *pixels, const Gump *g)
{
    cJSON *doll = stats_doll(g->characterId), *c = character_of(g->characterId), *e;
    int w, h;
    stats_size(g, &w, &h);
    const Picture *bg = link_picture(get(doll, "Background"), 0);
    if (bg) world_blit(pixels, bg, g->x, g->y, w, h); else fill(pixels, g->x, g->y, w, h, 0xE6281A10u);
    cJSON *survival = get(c, "Survival");
    int food = vitals_food(g->characterId), hungry = !cJSON_IsFalse(get(survival, "CanGetHungry")) && food <= num(survival, "HungryAtOrBelow", 9);
    Gump at = *g;
    at.kind = G_STATS; /* its elements at its own pixels (scale 1) */
    cJSON_ArrayForEach(e, get(doll, "Elements"))
    {
        int kind = num(e, "Kind", 0), showAs = num(e, "ShowAs", 0);
        cJSON *style = get(e, "Style"), *gauge = get(e, "Gauge");
        int ex = g->x + (int)dbl(style, "X", 0), ey = g->y + (int)dbl(style, "Y", 0);
        char text[64];
        switch (kind)
        {
            case 0: draw_readout(pixels, &at, style, str(e, "Text")); break;
            case 1: draw_readout(pixels, &at, style, cJSON_GetStringValue(get(c, "Name")) ? cJSON_GetStringValue(get(c, "Name")) : ""); break;
            case 2:
            {
                char key[16];
                snprintf(key, sizeof key, "%d", num(e, "StatId", -1));
                const char *v = cJSON_GetStringValue(get(get(c, "StatValues"), key));
                if (showAs == 0) { snprintf(text, sizeof text, "%s", v && *v ? v : "0"); draw_readout(pixels, &at, style, text); break; }
                double value = stat_number(c, num(e, "StatId", -1)), max = num(e, "MaxStatId", -1) >= 0 ? stat_number(c, num(e, "MaxStatId", -1)) : num(e, "Max", 30), min = num(e, "Min", 0);
                draw_gauge(pixels, ex, ey, gauge, showAs == 3 ? 2 : showAs == 2 ? 1 : 0, max - min <= 0 ? 0 : (value - min) / (max - min));
                break;
            }
            case 3: if (vitals_has(g->characterId, num(e, "Ailment", 1))) draw_gauge(pixels, ex, ey, gauge, 2, 0); break;
            default:
                if (cJSON_IsFalse(get(e, "OnlyWhenHungry"))) draw_gauge(pixels, ex, ey, gauge, showAs == 3 ? 2 : showAs == 2 ? 1 : 0, food / 31.0);
                else if (hungry) draw_gauge(pixels, ex, ey, gauge, 2, 0);
                break;
        }
    }
    int cx, cy, cw, ch;
    const Picture *close = stats_close(g, &cx, &cy, &cw, &ch);
    if (close) world_blit(pixels, close, cx, cy, cw, ch);
}

/* Exult's Z (ActionStats): the first in the party (the main character first) with a stats paperdoll whose isn't open yet opens; all open: the next to the front. */
void gumps_next_stats(void)
{
    int party[64], n = 0;
    for (int a = 0; a < world_actor_count() && n < 64; a++)
    {
        int id = world_actor_character(a);
        cJSON *doll = stats_doll(id);
        if ((a == 0 || items_in_party(id)) && !vitals_dead(id) && (link_picture(get(doll, "Background"), 0) || cJSON_GetArraySize(get(doll, "Elements")))) party[n++] = id;
    }
    if (!n) { open_stats(world_actor_character(0)); return; } /* says there's none yet */
    for (int i = 0; i < n; i++) if (find_gump(G_STATS, party[i], NULL, 0) < 0) { open_stats(party[i]); return; }
    static int next;
    next = (next + 1) % n;
    to_front(find_gump(G_STATS, party[next], NULL, 0));
}

/* Exult's I (ActionInventory): the same for the party's inventories. */
void gumps_next_inventory(void)
{
    int party[64], n = 0;
    for (int a = 0; a < world_actor_count() && n < 64; a++)
    {
        int id = world_actor_character(a);
        if ((a == 0 || items_in_party(id)) && !vitals_dead(id)) party[n++] = id;
    }
    for (int i = 0; i < n; i++) if (find_gump(G_DOLL, party[i], NULL, 0) < 0) { gumps_open_paperdoll(party[i]); return; }
    static int next;
    if (n) { next = (next + 1) % n; gumps_open_paperdoll(party[next]); }
}

/* Exult's gump mode (gumps pause the game): an inventory, a container, a stats paperdoll, a paperdoll of the PaperDoll Editor,
   the spellbook, a map being looked at, a target being chosen - the world waits. (Dialog boxes, books, videos and the rest pause it on their own.) */
int gumps_pausing(void) { return gumpCount > 0 || dolls_window_count() > 0 || choosing != NULL || magic_book_open() || mechanisms_map_open(); }
