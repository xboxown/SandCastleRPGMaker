/*
 * Paperdolls made in the PaperDoll Editor, in play (GameTestWindow.Dolls /
 * DollControls / ScriptDolls, DollLook, DollControls.cs): an object's own
 * window (Exult's object gumps), a form an event opens (Open Paperdoll),
 * the game's own (the error box). Every placed or carried copy of an object
 * keeps its paperdoll as it was left (on its item: it travels with it).
 *  - Each part shows the sprite of its look; one without a sprite takes
 *    its own size. Parts drawn in list order, later ones on top.
 *  - Button: Pressed for a moment, then its By Use. Switch: its next look.
 *    Lid: a double-click opens / shuts it; shut, what it covers is hidden
 *    or out of reach. Slot: only what it takes, in its fitted look, while it
 *    has room; Item area: anything, where it was dropped. Slider: a click
 *    along the track sets its value; Displays and bars follow. Close button.
 *  - Form controls: Yes / OK and No / Cancel answer whoever opened it;
 *    text boxes, check boxes, radio buttons, drop-downs, list boxes,
 *    progress bars - each value kept in its variable.
 *  - Modal: in the middle of the view, the only thing that can be clicked
 *    or typed in; Escape answers no, Enter presses Yes / OK.
 * Drawn at the design's own size, then scaled into the view as a
 * container's window is (200 / 240), so its words scale with it.
 */
#include "dolls.h"
#include "gumps.h"
#include "world.h"
#include "items.h"
#include "events.h"
#include "sound.h"
#include "font.h"
#include "script.h"
#include "platform.h"
#include "pictures.h"
#include "vitals.h"
#include "save.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
double sc_strtod(const char *text, char **end);
#include <ctype.h>

enum
{
    K_PICTURE, K_BUTTON, K_SWITCH, K_LID, K_SLOT, K_AREA, K_TEXT, K_SLIDER, K_DISPLAY, K_LIGHT, K_HOTSPOT, K_CLOSE,
    K_SAVELIST, K_SAVENAME, K_SAVESHOT, K_SAVEDETAILS, K_SAVEBUTTON, K_LOADBUTTON, K_DELETESAVE, K_CONFIRM, K_CANCEL,
    K_TEXTBOX, K_CHECKBOX, K_RADIO, K_DROPDOWN, K_LISTBOX, K_PROGRESS, K_NEWGAME, K_CONTINUE, K_LOADGAME, K_QUIT
};
enum { TR_USE = 2, TR_DROP = 3, TR_PICKUP = 6, TR_COMBINE_ITEM = 17, TR_COMBINE_PART = 18 };
#define BOX_H 240.0
#define PRESS_MS 160
#define OWNER_TITLE 5
#define ANSWER_QUIT -1
#define ANSWER_SAVE_OVER -2
#define ANSWER_DELETE -3
#define MAX_SAVES 200
#define MAX_SAVE_NAME 40

typedef struct PartState
{
    int look, visible, value, disabled, moved;
    double x, y;
    char *caption;                 /* Set Part Text (NULL: as designed) */
    char (*options)[96]; int optionCount, hasOptions; /* Fill List From Array */
    char text[256];                /* a text box's words */
    Item **items; int itemCount, itemCap;
} PartState;

struct DollState { int designId, partCount, soundOwner; cJSON *design; PartState *parts; };

typedef struct DollWin
{
    DollState *state;
    cJSON *design;
    int x, y, modal, title, answerToken, fresh;
    int typing, openList;                 /* the text box typed in, the drop-down open (part index, -1 none) */
    int pressPart; double pressMs; int pressAction; /* a button shown pressed; what happens after */
    int hasFill, errNumber; char errWords[200], errDetail[200];
    uint32_t *canvas; int cw, ch;
    int character;                         /* the character it was opened for (an inventory's button, with the inventory, a stats window): its parts' "own character" stats are theirs; -1 none */
    int hud, attached, linkedTo;           /* always on screen (a HUD); laid over a stats window; the inventory's character that brought it up (-1 none) */
    double closeInMs;                      /* the save / load progress: closes after this long (0: stays) */
    int lastValue[32], lostFrom[32];       /* what its stat-following parts last showed; icons just lost: what they had */
    uint32_t lostUntil[32];
    struct SaveScreen *save;               /* the Save/Load screen's own (System Settings > Save / Load) */
} DollWin;

/* The Save/Load screen (Exult's Newfile_gump): its saves, what's picked, the name being typed, a press waiting to be confirmed. */
typedef struct SaveScreen
{
    SaveInfo saves[MAX_SAVES]; int count;
    int picked, pickedSlot, scroll;
    char name[MAX_SAVE_NAME + 1];
    int confirmKind; char confirmFolder[1200]; double confirmMs;  /* pressed once: pressed again in time, it's done */
    int pendingSlot; char pendingName[MAX_SAVE_NAME + 1], pendingFolder[1200]; /* what a "Write over?" Yes goes ahead with */
    Picture *shot; char shotFolder[1200];
} SaveScreen;

static cJSON *designs;
static DollWin *wins[16]; static int winCount;   /* drawing order: the last on top */
static DollState *forms[64]; static int formIds[64], formCount;
static int nextSoundOwner = 1000;
static uint32_t nowMs;
static void title_button(int wi, int kind);
void dolls_open_save_screen(void);
void dolls_save_answer(int token, int yes);
static int save_part_clicked(struct DollWin *w, int i, int dy, int ph);
static SaveInfo *picked_save(struct SaveScreen *s);
static void load_pressed(struct DollWin *w);
typedef struct Slide { DollState *state; int part; double fx, fy, tx, ty, ms, total; } Slide;
static Slide slides[16]; static int slideCount;

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }
static cJSON *project(void) { return get((cJSON *)world_json(), "Project"); }
static cJSON *sounds_settings(void) { return get(get(project(), "Settings"), "Sounds"); }
static int clampi(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

void dolls_load(void) { designs = get(get(project(), "Objects"), "DollDesigns"); }

cJSON *dolls_design(int id)
{
    cJSON *d;
    if (id < 0) return NULL;
    cJSON_ArrayForEach(d, designs) if (num(d, "Id", -2) == id) return d;
    return NULL;
}

static cJSON *parts_of(cJSON *design) { return get(design, "Parts"); }
static cJSON *part_at(cJSON *design, int i) { return cJSON_GetArrayItem(parts_of(design), i); }
static int part_count(cJSON *design) { return cJSON_GetArraySize(parts_of(design)); }
static int kind_of(cJSON *part) { return num(part, "Kind", 0); }

static int index_of_id(cJSON *design, int id)
{
    int i = 0;
    cJSON *p;
    cJSON_ArrayForEach(p, parts_of(design)) { if (num(p, "Id", -2) == id) return i; i++; }
    return -1;
}

static int index_of_name(cJSON *design, const char *name)
{
    char want[96];
    snprintf(want, sizeof want, "%s", name);
    char *e = want + strlen(want);
    while (e > want && isspace((unsigned char)e[-1])) *--e = 0;
    const char *w = want;
    while (isspace((unsigned char)*w)) w++;
    int i = 0;
    cJSON *p;
    cJSON_ArrayForEach(p, parts_of(design))
    {
        const char *n = str(p, "Name");
        size_t k = 0;
        while (n[k] && w[k] && tolower((unsigned char)n[k]) == tolower((unsigned char)w[k])) k++;
        if (!n[k] && !w[k]) return i;
        i++;
    }
    return -1;
}

static int is_button_kind(int k) { return k == K_BUTTON || k == K_CLOSE || k == K_SAVEBUTTON || k == K_LOADBUTTON || k == K_DELETESAVE || k == K_CONFIRM || k == K_CANCEL || (k >= K_NEWGAME && k <= K_QUIT); }
static int is_control_kind(int k) { return is_button_kind(k) || k == K_TEXTBOX || k == K_CHECKBOX || k == K_RADIO || k == K_DROPDOWN || k == K_LISTBOX || k == K_PROGRESS; }
static int has_variable(int k) { return k == K_TEXTBOX || k == K_CHECKBOX || k == K_RADIO || k == K_DROPDOWN || k == K_LISTBOX || k == K_PROGRESS || k == K_SLIDER || k == K_DISPLAY; }
static int is_area_kind(int k) { return k == K_SLOT || k == K_AREA || k == K_HOTSPOT || k == K_TEXT || k == K_TEXTBOX || k == K_DROPDOWN || k == K_LISTBOX || k == K_PROGRESS || k == K_SAVELIST || k == K_SAVENAME || k == K_SAVESHOT || k == K_SAVEDETAILS; }

static int has_pages(cJSON *part, int trigger)
{
    cJSON *p;
    cJSON_ArrayForEach(p, get(part, "EventPages"))
        if (num(get(p, "Conditions"), "TriggerConditionBased", 0) == trigger && cJSON_GetArraySize(get(p, "Commands")) > 0) return 1;
    return 0;
}

static int any_commands(cJSON *part)
{
    cJSON *p;
    cJSON_ArrayForEach(p, get(part, "EventPages")) if (cJSON_GetArraySize(get(p, "Commands")) > 0) return 1;
    return 0;
}

static int clickable(cJSON *part)
{
    int k = kind_of(part);
    return k == K_BUTTON || k == K_SWITCH || k == K_LID || k == K_SLOT || k == K_AREA || k == K_SLIDER || k == K_HOTSPOT || k == K_CLOSE
        || (k >= K_SAVELIST && k <= K_DELETESAVE && k != K_SAVESHOT && k != K_SAVEDETAILS) || k == K_CONFIRM || k == K_CANCEL
        || k == K_TEXTBOX || k == K_CHECKBOX || k == K_RADIO || k == K_DROPDOWN || k == K_LISTBOX || (k >= K_NEWGAME && k <= K_QUIT)
        || any_commands(part);
}

/* ---------------- State ---------------- */

static void init_part(DollState *s, int i)
{
    cJSON *part = part_at(s->design, i);
    PartState *r = &s->parts[i];
    memset(r, 0, sizeof *r);
    int k = kind_of(part), min = num(part, "Min", 0), max = num(part, "Max", 10);
    r->look = num(part, "StartLook", 0);
    r->visible = !cJSON_IsFalse(get(part, "StartsVisible"));
    r->value = k == K_SLIDER || k == K_DISPLAY || k == K_PROGRESS ? clampi(num(part, "StartValue", 0), min, max > min ? max : min) : num(part, "StartValue", 0);
    if (k == K_TEXTBOX) snprintf(r->text, sizeof r->text, "%s", str(part, "Text"));
    if (k == K_CHECKBOX || k == K_RADIO) r->look = r->value != 0;
    cJSON *start;
    cJSON_ArrayForEach(start, get(part, "StartsWith"))
    {
        if (num(start, "ItemId", -1) < 0) continue;
        Item *it = items_make(num(start, "Category", 4), num(start, "ItemId", -1), 1);
        if (r->itemCount == r->itemCap) { r->itemCap = r->itemCap ? r->itemCap * 2 : 4; r->items = (Item **)realloc(r->items, sizeof(Item *) * (size_t)r->itemCap); }
        r->items[r->itemCount++] = it;
    }
}

static DollState *new_state(cJSON *design)
{
    DollState *s = (DollState *)calloc(1, sizeof(DollState));
    s->design = design;
    s->designId = num(design, "Id", -1);
    s->partCount = part_count(design);
    s->parts = (PartState *)calloc((size_t)s->partCount + 1, sizeof(PartState));
    s->soundOwner = nextSoundOwner++;
    for (int i = 0; i < s->partCount; i++) init_part(s, i);
    return s;
}

void dolls_free_state(void *p)
{
    DollState *s = (DollState *)p;
    if (!s) return;
    for (int w = winCount - 1; w >= 0; w--) if (wins[w]->state == s) dolls_close_window(w);
    for (int i = 0; i < s->partCount; i++)
    {
        PartState *r = &s->parts[i];
        free(r->caption); free(r->options);
        for (int k = 0; k < r->itemCount; k++) items_free(r->items[k]);
        free(r->items);
    }
    free(s->parts);
    free(s);
}

/* The paperdoll of this copy: made from the design the first time (a design swapped in the Object Editor starts fresh). */
static DollState *state_of_item(Item *it, cJSON *design)
{
    DollState *s = (DollState *)items_doll(it);
    if (s && s->designId != num(design, "Id", -1)) { dolls_free_state(s); s = NULL; }
    if (!s) { s = new_state(design); items_set_doll(it, s); }
    return s;
}

/* A form's own state (opened by an event or the game): one per design, kept as it was left unless fresh. */
static DollState *form_state(cJSON *design, int fresh)
{
    int id = num(design, "Id", -1);
    for (int i = 0; i < formCount; i++)
        if (formIds[i] == id)
        {
            if (fresh) { dolls_free_state(forms[i]); forms[i] = new_state(design); }
            return forms[i];
        }
    if (formCount >= 64) return new_state(design);
    formIds[formCount] = id;
    return forms[formCount++] = new_state(design);
}

/* ---------------- Variables ---------------- */

static void write_variable(cJSON *part, PartState *r)
{
    const char *name = str(part, "VariableName");
    int k = kind_of(part);
    if (!name[0] || !has_variable(k)) return;
    if (k == K_TEXTBOX)
    {
        char *end;
        double v = sc_strtod(r->text, &end);
        if (r->text[0] && !*end) events_set_variable(name, v);
        else if (flag(part, "NumbersOnly")) events_set_variable(name, 0);
        return;
    }
    events_set_variable(name, r->value);
}

static void read_variables(DollWin *w)
{
    for (int i = 0; i < w->state->partCount; i++)
    {
        cJSON *part = part_at(w->design, i);
        int k = kind_of(part);
        const char *name = str(part, "VariableName");
        if (!has_variable(k) || !name[0]) continue;
        PartState *r = &w->state->parts[i];
        if (!script_has_variable(name)) { write_variable(part, r); continue; }
        double v = events_variable(name);
        int min = num(part, "Min", 0), max = num(part, "Max", 10);
        if (k == K_CHECKBOX || k == K_RADIO) r->value = r->look = v != 0;
        else if (k == K_TEXTBOX) { if (flag(part, "NumbersOnly")) snprintf(r->text, sizeof r->text, "%g", v); }
        else if (k == K_SLIDER || k == K_DISPLAY || k == K_PROGRESS) r->value = clampi((int)floor(v + 0.5), min, max > min ? max : min);
        else r->value = (int)floor(v + 0.5);
    }
}

/* ---------------- Looks and sizes ---------------- */

static const Sprite *look_sprite(cJSON *part, int look)
{
    cJSON *l = cJSON_GetArrayItem(get(part, "Looks"), look), *s = get(l, "Sprite");
    if (!l || !flag(s, "IsSet")) return NULL;
    return sprite_find(str(s, "SpriteGroup"), str(s, "SpriteName"));
}

static const Picture *frame0(const Sprite *s) { return s && s->frameCount ? frame_picture(&s->frames[0]) : NULL; }
static const Picture *now_picture(const Sprite *s) { if (s) sprite_frame_sound(s, nowMs); return s ? sprite_picture_at(s, nowMs) : NULL; }

static const Font *font_of(cJSON *part) { const char *n = str(part, "FontName"); return font_get(n[0] ? n : "InGameFont"); }
static int scale_of_text(cJSON *part) { int s = num(part, "TextScale", 1); return s < 1 ? 1 : s; }

static uint32_t tint_of(cJSON *part)
{
    const char *c = str(part, "TextColor");
    if (c[0] != '#') return 0;
    unsigned long v = strtoul(c + 1, NULL, 16);
    if (strlen(c + 1) == 8) v &= 0xFFFFFF; /* #AARRGGBB: the colour */
    return 0xFF000000u | (uint32_t)v;
}

static int row_height(cJSON *part) { return font_line_height(font_of(part)) * scale_of_text(part) + 2; }

static const char *text_of(const DollWin *w, cJSON *part, const PartState *r, char *buf, size_t size)
{
    const char *t = r->caption && kind_of(part) != K_TEXTBOX ? r->caption : str(part, "Text");
    if (!w->hasFill || !strchr(t, '%')) return t;
    /* the error box: %errnum, %errdescrp, %errdetail */
    size_t n = 0;
    for (const char *p = t; *p && n + 1 < size;)
    {
        const char *put = NULL;
        char number[16];
        if (!strncmp(p, "%errnum", 7)) { snprintf(number, sizeof number, "%d", w->errNumber); put = number; p += 7; }
        else if (!strncmp(p, "%errdescrp", 10)) { put = w->errWords; p += 10; }
        else if (!strncmp(p, "%errdetail", 10)) { put = w->errDetail; p += 10; }
        if (put) { while (*put && n + 1 < size) buf[n++] = *put++; }
        else buf[n++] = *p++;
    }
    buf[n] = 0;
    return buf;
}

static int option_count(cJSON *part, const PartState *r) { return r->hasOptions ? r->optionCount : cJSON_GetArraySize(get(part, "Options")); }
static const char *option_at(cJSON *part, const PartState *r, int i)
{
    if (r->hasOptions) return i >= 0 && i < r->optionCount ? r->options[i] : "";
    const char *s = cJSON_GetStringValue(cJSON_GetArrayItem(get(part, "Options"), i));
    return s ? s : "";
}

static void words_size(cJSON *part, const char *text, int *w, int *h)
{
    int s = scale_of_text(part);
    *w = text[0] ? font_text_width(font_of(part), text) * s : 0;
    *h = font_line_height(font_of(part)) * s;
}

static void box_size(cJSON *part, int look, int *w, int *h)
{
    const Picture *p = frame0(look_sprite(part, look));
    if (!p) p = frame0(look_sprite(part, 0));
    *w = p ? p->width : 10; *h = p ? p->height : 10;
}

/* DollLook.SizeOf: its look's sprite, else its own Width x Height (areas always their own size). */
static void size_of_part(cJSON *part, int look, int *w, int *h)
{
    int k = kind_of(part);
    if (k == K_CHECKBOX || k == K_RADIO)
    {
        int bw, bh, tw, th;
        box_size(part, look, &bw, &bh);
        words_size(part, str(part, "Text"), &tw, &th);
        *w = bw + (tw ? tw + 4 : 0); *h = bh > row_height(part) ? bh : row_height(part);
        return;
    }
    int ow = num(part, "Width", 32), oh = num(part, "Height", 32);
    if (ow < 4) ow = 4;
    if (oh < 4) oh = 4;
    if (is_area_kind(k)) { *w = ow; *h = oh; return; }
    const Picture *p = frame0(look_sprite(part, k == K_SLIDER ? 0 : look));
    *w = p ? p->width : ow; *h = p ? p->height : oh;
}

/* The closed lid over a part, if any. */
static cJSON *closed_lid_over(const DollState *s, cJSON *part)
{
    int id = num(part, "Id", -2), i = 0;
    cJSON *lid, *c;
    cJSON_ArrayForEach(lid, parts_of(s->design))
    {
        if (kind_of(lid) == K_LID && s->parts[i].look == 0 && s->parts[i].visible)
            cJSON_ArrayForEach(c, get(lid, "Covers")) if (cJSON_IsNumber(c) && c->valueint == id) return lid;
        i++;
    }
    return NULL;
}

/* ---------------- Drawing (into the window's own picture) ---------------- */

static uint32_t *cv; static int cvw, cvh;

static void cv_fill(int x, int y, int w, int h, uint32_t c)
{
    uint32_t a = c >> 24;
    for (int j = y < 0 ? 0 : y; j < y + h && j < cvh; j++)
        for (int i = x < 0 ? 0 : x; i < x + w && i < cvw; i++)
        {
            uint32_t *d = &cv[j * cvw + i];
            if (a == 255) { *d = c; continue; }
            uint32_t da = *d >> 24, r = (((c >> 16) & 255) * a + ((*d >> 16) & 255) * (255 - a)) / 255, g = (((c >> 8) & 255) * a + ((*d >> 8) & 255) * (255 - a)) / 255, b = ((c & 255) * a + (*d & 255) * (255 - a)) / 255;
            uint32_t na = a + da * (255 - a) / 255;
            *d = (na << 24) | (r << 16) | (g << 8) | b;
        }
}

static void cv_frame(int x, int y, int w, int h, uint32_t face, uint32_t edge)
{
    cv_fill(x, y, w, h, face);
    cv_fill(x, y, w, 1, edge); cv_fill(x, y + h - 1, w, 1, edge); cv_fill(x, y, 1, h, edge); cv_fill(x + w - 1, y, 1, h, edge);
}

/* A picture into the canvas, stretched to w x h (nearest), only inside the clip. */
static void cv_blit(const Picture *p, int x, int y, int w, int h, int cx0, int cy0, int cx1, int cy1, int fade)
{
    if (!p || w <= 0 || h <= 0) return;
    for (int j = 0; j < h; j++)
    {
        int py = y + j;
        if (py < 0 || py >= cvh || py < cy0 || py >= cy1) continue;
        const uint32_t *row = p->pixels + (size_t)(j * p->height / h) * (size_t)p->width;
        for (int i = 0; i < w; i++)
        {
            int px = x + i;
            if (px < 0 || px >= cvw || px < cx0 || px >= cx1) continue;
            uint32_t c = row[i * p->width / w], a = c >> 24;
            if (fade) a = a * 115 / 255;
            if (!a) continue;
            uint32_t *d = &cv[py * cvw + px];
            if (a == 255) { *d = c; continue; }
            uint32_t da = *d >> 24, r = (((c >> 16) & 255) * a + ((*d >> 16) & 255) * (255 - a)) / 255, g = (((c >> 8) & 255) * a + ((*d >> 8) & 255) * (255 - a)) / 255, b = ((c & 255) * a + (*d & 255) * (255 - a)) / 255;
            *d = ((a + da * (255 - a) / 255) << 24) | (r << 16) | (g << 8) | b;
        }
    }
}

static void cv_picture(const Picture *p, int x, int y, int fade) { if (p) cv_blit(p, x, y, p->width, p->height, 0, 0, cvw, cvh, fade); }

static cJSON *character_of_id(int id)
{
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Characters"), "Characters")) if (num(c, "Id", -2) == id) return c;
    return NULL;
}

/* The save / load progress, 0 to 1 (a part following it: DiskProgressStat). */
static double diskProgress;

/* A part following a stat (Progress bar, Display): its value and the top of its range now. 0: it follows none. */
static int doll_stat(const DollWin *win, cJSON *part, double *value, double *max)
{
    int statId = num(part, "StatId", -1);
    if (statId == -2) { *value = diskProgress * 100; *max = 100; return 1; }
    if (statId < 0) return 0;
    int who = num(part, "StatOwner", 0) == 1 && win->character >= 0 ? win->character : world_actor_character(0);
    cJSON *values = get(character_of_id(who), "StatValues");
    char key[16];
    snprintf(key, sizeof key, "%d", statId);
    const char *v = cJSON_GetStringValue(get(values, key));
    *value = v && *v ? sc_strtod(v, NULL) : 0;
    int maxStat = num(part, "MaxStatId", -1);
    if (maxStat >= 0) { snprintf(key, sizeof key, "%d", maxStat); v = cJSON_GetStringValue(get(values, key)); *max = v && *v ? sc_strtod(v, NULL) : 0; }
    else *max = num(part, "Max", 10);
    return 1;
}

static uint32_t colour_of_hex(const char *hex, uint32_t fallback)
{
    if (!hex || hex[0] != '#') return fallback;
    size_t n = strlen(hex);
    if (n == 7) return 0xFF000000u | (uint32_t)strtoul(hex + 1, NULL, 16);
    if (n == 9) return (uint32_t)strtoul(hex + 1, NULL, 16);
    return fallback;
}

/* A skin fitted to a bar: as drawn, stretched to its size, or tiled across it. */
static void skin_blit(const Picture *p, int fit, int x, int y, int w, int h, int cx0, int cy0, int cx1, int cy1, int fade)
{
    if (!p) return;
    if (fit == 1) { cv_blit(p, x, y, w, h, cx0, cy0, cx1, cy1, fade); return; }
    if (fit == 2)
    {
        for (int ty = y; ty < y + h; ty += p->height)
            for (int tx = x; tx < x + w; tx += p->width)
                cv_blit(p, tx, ty, p->width, p->height, cx0 > x ? cx0 : x, cy0 > y ? cy0 : y, cx1 < x + w ? cx1 : x + w, cy1 < y + h ? cy1 : y + h, fade);
        return;
    }
    cv_blit(p, x, y, p->width, p->height, cx0, cy0, cx1, cy1, fade);
}

/* Icons (hearts): one per IconValue of the range, IconsPerRow to a row; its sprite's frames from full to empty. Beats: the last holding something pulses while low. Flashes: those just lost blink. */
static void draw_icons(DollWin *win, cJSON *part, int index, int x, int y, int value, int range, const Sprite *icon, int fade)
{
    int per = num(part, "IconValue", 2), perRow = num(part, "IconsPerRow", 10), gap = num(part, "IconGap", 1);
    per = per > 0 ? per : 1; perRow = perRow > 0 ? perRow : 1; gap = gap > 0 ? gap : 0;
    int count = range <= 0 ? 0 : (range + per - 1) / per;
    if (count > 200) count = 200;
    const Picture *first = icon && icon->frameCount ? frame_picture(&icon->frames[0]) : NULL;
    int iw = first ? first->width : 8, ih = first ? first->height : 8, frames = icon ? icon->frameCount : 0;
    if (index < 32)
    {
        if (win->lastValue[index] > value && win->lastValue[index] != 0 && flag(part, "IconsFlashOnLoss")) { win->lostFrom[index] = win->lastValue[index]; win->lostUntil[index] = nowMs + 450; }
        win->lastValue[index] = value;
    }
    int beatBelow = num(part, "IconsBeatBelow", 0), beating = beatBelow > 0 && range > 0 && value > 0 && value * 100.0 / range <= beatBelow;
    int lastHolding = (value + per - 1) / per - 1;
    uint32_t fillc = colour_of_hex(str(part, "FillColor"), 0xFFDC143Cu), backc = colour_of_hex(str(part, "EmptyColor"), 0xFF696969u);
    for (int i = 0; i < count; i++)
    {
        double inIt = (value - i * per) / (double)per;
        inIt = inIt < 0 ? 0 : inIt > 1 ? 1 : inIt;
        int px = x + i % perRow * (iw + gap), py = y + i / perRow * (ih + gap), grow = 0;
        if (beating && i == lastHolding) { int t = (int)(nowMs % 440); grow = (t < 220 ? t : 440 - t) * iw / 880; }
        if (index < 32 && nowMs < win->lostUntil[index] && (win->lostUntil[index] - nowMs) / 150 % 2 == 0)
        {
            double before = (win->lostFrom[index] - i * per) / (double)per;
            before = before < 0 ? 0 : before > 1 ? 1 : before;
            if (before > inIt) inIt = before;
        }
        if (frames > 0)
        {
            if (frames == 1 && inIt <= 0) continue;
            int frame = frames == 1 ? 0 : (int)floor((1 - inIt) * (frames - 1) + 0.5);
            cv_blit(frame_picture(&icon->frames[frame]), px - grow, py - grow, iw + 2 * grow, ih + 2 * grow, 0, 0, cvw, cvh, fade);
        }
        else
        {
            cv_fill(px, py, iw, ih, backc);
            if (inIt > 0) cv_fill(px, py, (int)(iw * inIt), ih, fillc);
        }
    }
}

static int cv_words(cJSON *part, const char *text, int x, int y, int wrap)
{
    if (!text[0]) return 0;
    return font_draw_into(font_of(part), cv, cvw, cvh, x, y, text, tint_of(part), scale_of_text(part), wrap);
}

#define PLAIN_FACE 0xFF3A3A42u
#define PLAIN_PRESSED 0xFF26262Cu
#define PLAIN_BOX 0xFF1C1C20u
#define PLAIN_EDGE 0xFF8A8A94u
#define PLAIN_TYPING 0xFFE0B840u
#define PLAIN_PICKED 0x5AFFFFFFu
#define PLAIN_FILL 0xFF4CA850u

static void draw_control(DollWin *win, cJSON *part, PartState *r, int x, int y, int fade)
{
    int k = kind_of(part), w, h;
    size_of_part(part, r->look, &w, &h);
    char buf[300];
    const char *caption = text_of(win, part, r, buf, sizeof buf);
    int tw, th;
    if (is_button_kind(k))
    {
        int pressed = r->look == 1;
        const Sprite *s = look_sprite(part, r->look);
        if (!s) s = look_sprite(part, 0);
        if (s) cv_picture(now_picture(s), x, y, fade); else cv_frame(x, y, w, h, pressed ? PLAIN_PRESSED : PLAIN_FACE, PLAIN_EDGE);
        words_size(part, caption, &tw, &th);
        cv_words(part, caption, x + (w - tw > 0 ? (w - tw) / 2 : 0) + pressed, y + (h - th > 0 ? (h - th) / 2 : 0) + pressed, 0);
        return;
    }
    switch (k)
    {
        case K_TEXTBOX:
        {
            int typing = win->typing == (int)(r - win->state->parts);
            const Sprite *s = typing ? (look_sprite(part, 1) ? look_sprite(part, 1) : look_sprite(part, 0)) : look_sprite(part, 0);
            if (s) cv_picture(now_picture(s), x, y, fade); else cv_frame(x, y, w, h, PLAIN_BOX, typing ? PLAIN_TYPING : PLAIN_EDGE);
            char t[260];
            snprintf(t, sizeof t, "%s%s", r->text, typing ? "_" : "");
            words_size(part, t, &tw, &th);
            /* clipped to the box */
            uint32_t *keep = cv; int kw = cvw, kh = cvh; (void)keep; (void)kw; (void)kh;
            cv_words(part, t, x + 3, y + (h - th > 0 ? (h - th) / 2 : 0), 0);
            break;
        }
        case K_CHECKBOX: case K_RADIO:
        {
            int bw, bh;
            box_size(part, r->look, &bw, &bh);
            int top = (h - bh) / 2 > 0 ? (h - bh) / 2 : 0;
            const Sprite *s = look_sprite(part, r->look);
            if (!s) s = look_sprite(part, 0);
            if (s) cv_picture(now_picture(s), x, y + top, fade);
            else
            {
                cv_frame(x, y + top, bw, bh, PLAIN_BOX, PLAIN_EDGE);
                if (r->look == 1) cv_fill(x + 2, y + top + 2, bw - 4, bh - 4, 0xFFFFFFFFu);
            }
            words_size(part, caption, &tw, &th);
            cv_words(part, caption, x + bw + 4, y + (h - th > 0 ? (h - th) / 2 : 0), 0);
            break;
        }
        case K_DROPDOWN:
        {
            const Sprite *s = look_sprite(part, 0);
            if (s) cv_picture(now_picture(s), x, y, fade);
            else
            {
                cv_frame(x, y, w, h, PLAIN_BOX, PLAIN_EDGE);
                int a = h - 6 > 4 ? h - 6 : 4; /* the arrow */
                for (int j = 0; j < a / 2; j++) cv_fill(x + w - a - 2 + j, y + 3 + j, a - 2 * j, 1, PLAIN_EDGE);
            }
            const char *picked = r->value >= 1 && r->value <= option_count(part, r) ? option_at(part, r, r->value - 1) : "";
            words_size(part, picked, &tw, &th);
            cv_words(part, picked, x + 3, y + (h - th > 0 ? (h - th) / 2 : 0), 0);
            if (win->openList == (int)(r - win->state->parts))
            {
                int row = row_height(part), n = option_count(part, r), lh = (n > 0 ? n : 1) * row;
                const Sprite *behind = look_sprite(part, 1);
                if (behind) cv_blit(now_picture(behind), x, y + h, w, lh, 0, 0, cvw, cvh, 0); else cv_frame(x, y + h, w, lh, PLAIN_BOX, PLAIN_EDGE);
                for (int i = 0; i < n; i++)
                {
                    if (i + 1 == r->value) { const Sprite *m = look_sprite(part, 2); if (m) cv_blit(now_picture(m), x, y + h + i * row, w, row, 0, 0, cvw, cvh, 0); else cv_fill(x, y + h + i * row, w, row, PLAIN_PICKED); }
                    cv_words(part, option_at(part, r, i), x + 3, y + h + i * row + 1, 0);
                }
            }
            break;
        }
        case K_LISTBOX:
        {
            const Sprite *s = look_sprite(part, 0);
            if (s) cv_picture(now_picture(s), x, y, fade); else cv_frame(x, y, w, h, PLAIN_BOX, PLAIN_EDGE);
            int row = row_height(part), rows = h / row > 1 ? h / row : 1, n = option_count(part, r);
            int first = (r->value < n ? r->value : n) - rows; if (first < 0) first = 0;
            for (int i = first; i < n && i < first + rows; i++)
            {
                int yy = y + (i - first) * row;
                if (i + 1 == r->value) { const Sprite *m = look_sprite(part, 1); if (m) cv_blit(now_picture(m), x, yy, w, row, 0, 0, cvw, cvh, 0); else cv_fill(x, yy, w, row, PLAIN_PICKED); }
                cv_words(part, option_at(part, r, i), x + 3, yy + 1, 0);
            }
            break;
        }
        case K_PROGRESS:
        {
            int value = r->value, shows = num(part, "ShowsPartId", -1);
            if (shows >= 0) { int si = index_of_id(win->design, shows); if (si >= 0) value = win->state->parts[si].value; }
            int min = num(part, "Min", 0), max = num(part, "Max", 10);
            double statValue, statMax;
            if (doll_stat(win, part, &statValue, &statMax)) { value = (int)floor(statValue + 0.5); max = (int)floor(statMax + 0.5); }
            double t = (value - min) / (double)(max - min > 1 ? max - min : 1);
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            const Sprite *empty = look_sprite(part, 0), *full = look_sprite(part, 1);
            int style = num(part, "BarStyle", 0), fit = num(part, "SkinFit", 0);
            uint32_t fillc = colour_of_hex(str(part, "FillColor"), PLAIN_FILL), backc = colour_of_hex(str(part, "EmptyColor"), PLAIN_BOX);
            if (style == 2) { draw_icons(win, part, index_of_id(win->design, num(part, "Id", -1)), x, y, value - min, max - min, empty, fade); break; }
            if (style == 1)
            {
                /* a pie, filled clockwise from the top: the empty look, and the full one as far round as the value reaches */
                const Picture *ep = empty ? now_picture(empty) : NULL, *fp = full ? now_picture(full) : NULL;
                double rx = w / 2.0, ry = h / 2.0;
                for (int j = 0; j < h; j++)
                    for (int k = 0; k < w; k++)
                    {
                        double dx = (k + 0.5 - rx) / rx, dy = (j + 0.5 - ry) / ry;
                        if (dx * dx + dy * dy > 1) continue;
                        double angle = atan2(dx, -dy);
                        if (angle < 0) angle += 2 * 3.14159265358979;
                        const Picture *from = angle <= t * 2 * 3.14159265358979 ? fp : ep;
                        uint32_t c = from ? from->pixels[(size_t)(j * from->height / h) * (size_t)from->width + (size_t)(k * from->width / w)] : (from == fp && angle <= t * 2 * 3.14159265358979 ? fillc : backc);
                        if ((c >> 24) && x + k >= 0 && y + j >= 0 && x + k < cvw && y + j < cvh) cv[(y + j) * cvw + x + k] = c;
                    }
                break;
            }
            if (empty) skin_blit(now_picture(empty), fit, x, y, w, h, 0, 0, cvw, cvh, fade); else cv_frame(x, y, w, h, backc, PLAIN_EDGE);
            /* only as much of the full bar as the value reaches: left to right (or right to left), or Upright bottom up (or top down) */
            int vertical = flag(part, "Vertical"), reverse = flag(part, "ReverseFill");
            int cx0 = !vertical && reverse ? x + (int)(w * (1 - t)) : x, cy0 = vertical && !reverse ? y + (int)(h * (1 - t)) : y;
            int cx1 = !vertical && !reverse ? x + (int)(w * t) : x + w, cy1 = vertical && reverse ? y + (int)(h * t) : y + h;
            if (full) skin_blit(now_picture(full), fit, x, y, w, h, cx0, cy0, cx1, cy1, fade);
            else cv_fill(cx0, cy0, cx1 - cx0, cy1 - cy0, fillc);
            break;
        }
    }
}

static void draw_items(DollWin *win, cJSON *part, PartState *r, int px, int py)
{
    double counter = BOX_H / 200.0; /* drawn at the size they have on the map */
    int rowX = 0;
    for (int i = 0; i < r->itemCount; i++)
    {
        Item *it = r->items[i];
        if (gumps_dragging_item(it)) continue;
        cJSON *fit = dolls_fit_for(part, items_category(it), items_item_id(it));
        cJSON *fs = get(fit, "FittedSprite");
        if (fit && flag(fs, "IsSet"))
        {
            const Sprite *s = sprite_find(str(fs, "SpriteGroup"), str(fs, "SpriteName"));
            cv_picture(now_picture(s), px + num(fit, "OffsetX", 0), py + num(fit, "OffsetY", 0), 0);
            continue;
        }
        int lit, spent;
        items_light(it, &lit, &spent);
        const Picture *p = now_picture(world_item_look(items_category(it), items_item_id(it), lit, spent, items_is_locked(it), items_quantity(it), items_liquid(it, -1)));
        int w = p ? (int)(((p->width + 7) / 8 * 8) * counter) : 32, h = p ? (int)(((p->height + 7) / 8 * 8) * counter) : 32;
        double ix, iy;
        if (kind_of(part) == K_SLOT) { int sw, sh; size_of_part(part, 0, &sw, &sh); ix = (sw - w) / 2.0; iy = (sh - h) / 2.0; }
        else
        {
            items_spot(it, &ix, &iy);
            if (items_unplaced(it)) { ix = rowX; iy = 0; items_set_spot(it, ix, iy, 1); }
            if (ix + w + 2 > rowX) rowX = (int)(ix + w + 2);
        }
        if (p) cv_blit(p, px + (int)ix, py + (int)iy, w, h, 0, 0, cvw, cvh, 0);
    }
}

static void draw_part(DollWin *win, cJSON *d, cJSON *part, PartState *r, int i, int x, int y, int fade);
static void draw_save_part(DollWin *win, cJSON *part, int i, int x, int y, int w, int h, int fade);

static void render(DollWin *win)
{
    cJSON *d = win->design;
    int w = num(d, "Width", 320), h = num(d, "Height", 240);
    if (w < 8) w = 8;
    if (h < 8) h = 8;
    if (!win->canvas || win->cw != w || win->ch != h) { free(win->canvas); win->canvas = (uint32_t *)malloc(sizeof(uint32_t) * (size_t)(w * h)); win->cw = w; win->ch = h; }
    cv = win->canvas; cvw = w; cvh = h;
    memset(cv, 0, sizeof(uint32_t) * (size_t)(w * h));
    for (int i = 0; i < win->state->partCount; i++)
    {
        cJSON *part = part_at(d, i);
        PartState *r = &win->state->parts[i];
        int k = kind_of(part);
        if (!r->visible) continue;
        cJSON *lid = closed_lid_over(win->state, part);
        if (lid && !cJSON_IsFalse(get(lid, "HidesWhatItCovers"))) continue;
        if (k == K_SAVEBUTTON && vitals_game_over()) continue;
        int x = r->moved ? (int)r->x : num(part, "X", 0), y = r->moved ? (int)r->y : num(part, "Y", 0), fade = 0;
        if (gumps_dragging_doll_part(win->state, i)) continue;
        /* disabled (Enable / Disable Part): drawn on a layer of its own, then laid on at 45% - greyed */
        static uint32_t *layer; static int layerSize;
        uint32_t *under = cv;
        if (r->disabled)
        {
            if (layerSize < w * h) { free(layer); layer = (uint32_t *)malloc(sizeof(uint32_t) * (size_t)(w * h)); layerSize = w * h; }
            memset(layer, 0, sizeof(uint32_t) * (size_t)(w * h));
            cv = layer;
        }
        draw_part(win, d, part, r, i, x, y, fade);
        if (r->disabled)
        {
            cv = under;
            for (int p = 0; p < w * h; p++)
            {
                uint32_t c = layer[p], a = (c >> 24) * 115 / 255;
                if (!a) continue;
                uint32_t *dst = &cv[p], da = *dst >> 24;
                uint32_t rr = (((c >> 16) & 255) * a + ((*dst >> 16) & 255) * (255 - a)) / 255, gg = (((c >> 8) & 255) * a + ((*dst >> 8) & 255) * (255 - a)) / 255, bb = ((c & 255) * a + (*dst & 255) * (255 - a)) / 255;
                *dst = ((a + da * (255 - a) / 255) << 24) | (rr << 16) | (gg << 8) | bb;
            }
        }
    }
    /* an open drop-down over everything */
    if (win->openList >= 0 && win->openList < win->state->partCount)
    {
        cJSON *part = part_at(d, win->openList);
        PartState *r = &win->state->parts[win->openList];
        draw_control(win, part, r, r->moved ? (int)r->x : num(part, "X", 0), r->moved ? (int)r->y : num(part, "Y", 0), 0);
    }
}

static void draw_part(DollWin *win, cJSON *d, cJSON *part, PartState *r, int i, int x, int y, int fade)
{
    int k = kind_of(part);
    {
        if (is_control_kind(k)) { if (win->openList != i) draw_control(win, part, r, x, y, fade); return; }
        int pw, ph;
        size_of_part(part, r->look, &pw, &ph);
        char buf[300];
        switch (k)
        {
            case K_HOTSPOT: break;
            case K_TEXT: cv_words(part, text_of(win, part, r, buf, sizeof buf), x, y, pw > 8 ? pw : 8); break;
            case K_SLIDER:
            {
                const Sprite *track = look_sprite(part, 0), *knob = look_sprite(part, 1);
                if (track) cv_picture(now_picture(track), x, y, fade);
                const Picture *kp = frame0(knob);
                int kw = kp ? kp->width : (pw < 8 ? pw : 8), kh = kp ? kp->height : ph;
                int min = num(part, "Min", 0), max = num(part, "Max", 10);
                double t = (r->value - min) / (double)(max - min > 1 ? max - min : 1);
                t = t < 0 ? 0 : t > 1 ? 1 : t;
                int kx = flag(part, "Vertical") ? (pw - kw) / 2 : (int)((pw - kw) * t), ky = flag(part, "Vertical") ? (int)((ph - kh) * (1 - t)) : (ph - kh) / 2;
                if (knob) cv_picture(now_picture(knob), x + kx, y + ky, fade); else cv_fill(x + kx, y + ky, kw, kh, 0xFFC0C0C0u);
                break;
            }
            case K_DISPLAY:
            {
                double statValue, statMax;
                if (doll_stat(win, part, &statValue, &statMax))
                {
                    /* following a stat: its frames from empty (frame 1) to full (the last), by how full its range is */
                    const Sprite *frames = look_sprite(part, 0);
                    int count = frames && frames->frameCount > 0 ? frames->frameCount : 1, low = num(part, "Min", 0);
                    double fraction = (statValue - low) / (statMax - low > 1 ? statMax - low : 1);
                    fraction = fraction < 0 ? 0 : fraction > 1 ? 1 : fraction;
                    if (frames && frames->frameCount) cv_picture(frame_picture(&frames->frames[(int)floor(fraction * (count - 1) + 0.5)]), x, y, fade);
                    else { char t[16]; snprintf(t, sizeof t, "%d", (int)statValue); font_draw_into(font_get("InGameFont"), cv, cvw, cvh, x, y, t, 0xFFFFFFFFu, 1, 0); }
                    break;
                }
                int shows = num(part, "ShowsPartId", -1), si = shows >= 0 ? index_of_id(d, shows) : -1;
                int value = si >= 0 ? win->state->parts[si].value : r->value;
                int min = si >= 0 ? num(part_at(d, si), "Min", 0) : num(part, "Min", 0);
                const Sprite *s = look_sprite(part, 0);
                if (s && s->frameCount)
                {
                    int f = value - min;
                    f = f < 0 ? 0 : f >= s->frameCount ? s->frameCount - 1 : f;
                    cv_picture(frame_picture(&s->frames[f]), x, y, fade);
                }
                else { char t[16]; snprintf(t, sizeof t, "%d", value); font_draw_into(font_get("InGameFont"), cv, cvw, cvh, x, y, t, 0xFFFFFFFFu, 1, 0); }
                break;
            }
            case K_SAVELIST: case K_SAVENAME: case K_SAVESHOT: case K_SAVEDETAILS:
                draw_save_part(win, part, i, x, y, pw, ph, fade);
                break;
            case K_SLOT: case K_AREA:
            {
                const Sprite *s = look_sprite(part, 0);
                if (s) cv_picture(now_picture(s), x, y, fade);
                draw_items(win, part, r, x, y);
                break;
            }
            default:
            {
                const Sprite *s = look_sprite(part, r->look);
                if (!s) s = look_sprite(part, 0);
                if (s) cv_picture(now_picture(s), x, y, fade);
            }
        }
    }
}

/* A window's size on screen: as a container's (200 / 240); the Title Screen fills the view. */
static double dolls_scale(const DollWin *w)
{
    if (w->attached) return 1; /* at the stats window's own scale: its parts land where they were put */
    if (!w->title) return 200.0 / BOX_H;
    double sx = SCREEN_W / (double)(num(w->design, "Width", 320) > 1 ? num(w->design, "Width", 320) : 1), sy = SCREEN_H / (double)(num(w->design, "Height", 240) > 1 ? num(w->design, "Height", 240) : 1);
    return sx < sy ? sx : sy;
}

void dolls_window_size(int i, int *w, int *h)
{
    DollWin *win = wins[i];
    double s = dolls_scale(win);
    *w = (int)(num(win->design, "Width", 320) * s + 0.5); *h = (int)(num(win->design, "Height", 240) * s + 0.5);
}

void dolls_draw(uint32_t *pixels, uint32_t ms)
{
    nowMs = ms;
    for (int i = 0; i < winCount; i++)
    {
        DollWin *w = wins[i];
        if (w->hud) continue; /* under every window: dolls_draw_hud */
        render(w);
        Picture p = { w->cw, w->ch, w->canvas };
        int sw, sh;
        dolls_window_size(i, &sw, &sh);
        world_blit(pixels, &p, w->x, w->y, sw, sh);
    }
}

static DollWin *open_window(DollState *state, cJSON *design, int modal, int title);
static cJSON *save_settings(void);
/* The always-on-screen paperdolls: under the windows, over the world. */
void dolls_draw_hud(uint32_t *pixels, uint32_t ms)
{
    nowMs = ms;
    for (int i = 0; i < winCount; i++)
    {
        DollWin *w = wins[i];
        if (!w->hud) continue;
        render(w);
        Picture p = { w->cw, w->ch, w->canvas };
        int sw, sh;
        dolls_window_size(i, &sw, &sh);
        world_blit(pixels, &p, w->x, w->y, sw, sh);
    }
}

/* Windows that pause the game: not the HUD's, nor the controls of a stats window (the window itself does). */
int dolls_pausing(void)
{
    for (int i = 0; i < winCount; i++) if (!wins[i]->hud && !wins[i]->attached) return 1;
    return 0;
}

/* KeepHudDolls: opened when missing, kept to their corner, closed when no longer wanted. */
static void keep_hud_dolls(void)
{
    int shown = !dolls_title_open() && !vitals_game_over();
    cJSON *design;
    cJSON_ArrayForEach(design, designs)
    {
        int id = num(design, "Id", -1), at = -1;
        for (int i = 0; i < winCount; i++) if (wins[i]->hud && wins[i]->design == design) at = i;
        if (!flag(design, "OnScreen") || !shown) { if (at >= 0) dolls_close_window(at); continue; }
        if (at < 0)
        {
            if (winCount >= 16) continue;
            DollWin *w = open_window(form_state(design, 0), design, 0, 1); /* quietly: no paperdoll-open sound */
            if (!w) continue;
            w->title = 0; w->hud = 1; w->character = world_actor_character(0);
            for (int i = 0; i < winCount; i++) if (wins[i] == w) at = i;
        }
        (void)id;
        DollWin *w = wins[at];
        int sw, sh, corner = num(design, "ScreenCorner", 0);
        double sx = (cJSON_IsNumber(get(design, "ScreenX")) ? get(design, "ScreenX")->valuedouble : 0) * 200.0 / BOX_H, sy = (cJSON_IsNumber(get(design, "ScreenY")) ? get(design, "ScreenY")->valuedouble : 0) * 200.0 / BOX_H;
        dolls_window_size(at, &sw, &sh);
        w->x = corner == 1 || corner == 3 ? SCREEN_W - sw - (int)sx : (int)sx;
        w->y = corner == 2 || corner == 3 ? SCREEN_H - sh - (int)sy : (int)sy;
    }
}

/* A paperdoll an inventory brings up (Other paperdolls): beside it, for its character. */
void dolls_open_linked(int characterId, int designId, int x, int y)
{
    cJSON *design = dolls_design(designId);
    if (!design) return;
    for (int i = 0; i < winCount; i++) if (wins[i]->design == design && wins[i]->linkedTo == characterId) return;
    DollWin *w = open_window(form_state(design, 0), design, 0, 1); /* quietly */
    if (!w) return;
    w->title = 0; w->character = characterId; w->linkedTo = characterId; w->x = x; w->y = y;
}

int dolls_linked_open(int characterId, int designId)
{
    cJSON *design = dolls_design(designId);
    for (int i = 0; i < winCount; i++) if (design && wins[i]->design == design && wins[i]->linkedTo == characterId) return 1;
    return 0;
}

void dolls_close_linked(int characterId, int designId)
{
    for (int i = winCount - 1; i >= 0; i--)
        if (wins[i]->linkedTo == characterId && (designId < 0 || num(wins[i]->design, "Id", -2) == designId)) dolls_close_window(i);
}

/* A stats window's controls, laid over it (Stats paperdoll > Controls): they move and close with it. */
void dolls_attach_to_stats(int designId, int characterId, int x, int y)
{
    cJSON *design = dolls_design(designId);
    if (!design) return;
    DollWin *w = open_window(form_state(design, 0), design, 0, 1); /* quietly */
    if (!w) return;
    w->title = 0; w->attached = 1; w->character = characterId; w->x = x; w->y = y;
}

void dolls_place_attached(int designId, int x, int y)
{
    for (int i = 0; i < winCount; i++) if (wins[i]->attached && num(wins[i]->design, "Id", -2) == designId) { wins[i]->x = x; wins[i]->y = y; }
}

void dolls_close_attached(int designId)
{
    for (int i = winCount - 1; i >= 0; i--) if (wins[i]->attached && num(wins[i]->design, "Id", -2) == designId) dolls_close_window(i);
}

/* ---------------- The save / load progress paperdoll (GameTestWindow.DiskProgress) ---------------- */

void main_show_now(void);
static uint32_t diskOpened;
static DollWin *diskWin;

static int disk_design_id(void) { return num(save_settings(), "ProgressDollDesignId", -1); }

void dolls_disk_begin(void)
{
    diskProgress = 0;
    diskWin = NULL;
    cJSON *design = dolls_design(disk_design_id());
    if (!design) return;
    diskWin = open_window(form_state(design, 1), design, 1, 0);
    diskOpened = platform_ticks();
    dolls_disk_report(0);
}

/* How far it is (0 to 1): drawn now, before the next step. */
void dolls_disk_report(double fraction)
{
    diskProgress = fraction < 0 ? 0 : fraction > 1 ? 1 : fraction;
    if (diskWin) main_show_now();
}

/* Done: full, kept the rest of its time, then closed (a failed one: closed now). */
void dolls_disk_end(int failed)
{
    if (!diskWin) return;
    int at = -1;
    for (int i = 0; i < winCount; i++) if (wins[i] == diskWin) at = i;
    if (at >= 0 && !failed)
    {
        diskProgress = 1;
        main_show_now();
        double left = num(save_settings(), "ProgressShownAtLeastMs", 600) - (double)(platform_ticks() - diskOpened);
        if (left > 0) { diskWin->closeInMs = left; diskWin = NULL; return; }
    }
    if (at >= 0) dolls_close_window(at);
    diskWin = NULL;
}

/* ---------------- Opening and closing ---------------- */

int dolls_window_count(void) { return winCount; }
int dolls_modal_open(void) { for (int i = 0; i < winCount; i++) if (wins[i]->modal) return 1; return 0; }
int dolls_window_x(int i) { return wins[i]->x; }
int dolls_window_y(int i) { return wins[i]->y; }
void dolls_move_window(int i, int x, int y) { if (!wins[i]->title) { wins[i]->x = x; wins[i]->y = y; } }
int dolls_to_front(int i)
{
    if (wins[winCount - 1]->modal && !wins[i]->modal) return i; /* a modal one stays on top */
    DollWin *w = wins[i];
    memmove(&wins[i], &wins[i + 1], sizeof(DollWin *) * (size_t)(winCount - i - 1));
    wins[winCount - 1] = w;
    return winCount - 1;
}

static DollWin *open_window(DollState *state, cJSON *design, int modal, int title)
{
    for (int i = 0; i < winCount; i++) if (wins[i]->state == state) { int top = dolls_to_front(i); return wins[top]; }
    if (winCount >= 16) return NULL;
    if (!title)
    {
        cJSON *s = sounds_settings();
        if (!cJSON_IsFalse(get(s, "PaperdollOpenSoundEnabled"))) sound_play(num(s, "PaperdollOpenSoundId", -1));
    }
    DollWin *w = (DollWin *)calloc(1, sizeof(DollWin));
    w->state = state; w->design = design; w->modal = modal; w->title = title;
    w->typing = w->openList = w->pressPart = -1;
    w->character = -1; w->linkedTo = -1;
    int sw, sh;
    wins[winCount++] = w;
    dolls_window_size(winCount - 1, &sw, &sh);
    if (modal || title) { w->x = (SCREEN_W - sw) / 2; w->y = (SCREEN_H - sh) / 2; }
    else { int open = winCount - 1 + gumps_open(); w->x = 20 + open * 18; w->y = open * 8; if (w->x + sw > SCREEN_W) w->x = SCREEN_W - sw; if (w->y + sh > SCREEN_H) w->y = SCREEN_H - sh; if (w->x < 0) w->x = 0; if (w->y < 0) w->y = 0; }
    read_variables(w);
    return w;
}

void dolls_close_window(int i)
{
    DollWin *w = wins[i];
    memmove(&wins[i], &wins[i + 1], sizeof(DollWin *) * (size_t)(winCount - i - 1));
    winCount--;
    sound_play(num(w->design, "CloseSoundResourceId", -1));
    if (w->title) sound_stop_owner(OWNER_TITLE); /* the game begins */
    if (w->answerToken > 0) events_doll_answered(w->answerToken, 0); /* an unanswered question is a no */
    if (w->save) { picture_free(w->save->shot); free(w->save); }
    free(w->canvas);
    free(w);
}

int dolls_close_top(void)
{
    int top = winCount - 1;
    while (top >= 0 && (wins[top]->hud || wins[top]->attached)) top--; /* out of Escape's reach: a HUD; a stats window's controls go with the window */
    if (top < 0) return 0;
    DollWin *w = wins[top];
    if (w->title) return 0;
    if (w->typing >= 0) { dolls_stop_typing(); return 1; }
    dolls_close_window(top);
    return 1;
}

/* An object that opens a paperdoll (Object Editor) - this copy's. Its own By Use first (with the paperdoll as "this" for a Paperdoll command); else it opens. 1 = it has one. */
int dolls_use_item(Item *it, int uid)
{
    if (items_category(it) != CAT_OBJECT) return 0;
    cJSON *obj = NULL, *o;
    cJSON_ArrayForEach(o, get(get(project(), "Objects"), "Objects")) if (num(o, "Id", -2) == items_item_id(it)) obj = o;
    cJSON *design = dolls_design(num(obj, "DollDesignId", -1));
    if (!design) return 0;
    DollState *s = state_of_item(it, design);
    events_set_doll_context(s);
    int ran = uid > 0 ? events_object_event(uid, TR_USE, -1) : events_fire_item(CAT_OBJECT, items_item_id(it), TR_USE);
    events_set_doll_context(NULL);
    if (ran) return 1;
    open_window(s, design, 0, 0);
    char line[160];
    snprintf(line, sizeof line, "You open \"%s\".", str(obj, "Name"));
    game_log(line);
    return 1;
}

/* A paperdoll that isn't an object's (Open Paperdoll, the game's own): modal or not; token: who hears the answer. */
int dolls_open_form(int designId, int modal, int fresh, int token, int title)
{
    cJSON *design = dolls_design(designId);
    if (!design) return 0;
    DollState *s = form_state(design, fresh);
    DollWin *w = open_window(s, design, modal, title);
    if (!w) return 0;
    if (token) { int before = w->answerToken; w->answerToken = token; if (before > 0) events_doll_answered(before, 0); } /* opened again while asking: the first asker hears no */
    if (modal && !w->modal) w->modal = 1;
    return 1;
}

/* The error box (System Settings > Errors): fresh, modal, its words in %errnum, %errdescrp, %errdetail. 0 without one. */
int dolls_error(int designId, int number, const char *words, const char *detail, int token)
{
    for (int i = 0; i < winCount; i++)
        if (num(wins[i]->design, "Id", -1) == designId && wins[i]->hasFill) /* one at a time: the newest shown */
        {
            wins[i]->errNumber = number;
            snprintf(wins[i]->errWords, sizeof wins[i]->errWords, "%s", words);
            snprintf(wins[i]->errDetail, sizeof wins[i]->errDetail, "%s", detail);
            if (token) events_doll_answered(token, 0); /* the one asking now goes on */
            return 1;
        }
    if (!dolls_open_form(designId, 1, 1, token, 0)) return 0;
    DollWin *w = wins[winCount - 1];
    w->hasFill = 1; w->errNumber = number;
    snprintf(w->errWords, sizeof w->errWords, "%s", words);
    snprintf(w->errDetail, sizeof w->errDetail, "%s", detail);
    return 1;
}

static void answer(int wi, int yes)
{
    DollWin *w = wins[wi];
    int token = w->answerToken;
    w->answerToken = 0;
    dolls_close_window(wi);
    if (token == ANSWER_QUIT && yes) events_request_quit(); /* the Quit prompt */
    else if (token == ANSWER_SAVE_OVER || token == ANSWER_DELETE) dolls_save_answer(token, yes);
    else if (token > 0) events_doll_answered(token, yes);
}

/* A Quit button (or Ctrl+Q): the Quit prompt (System Settings > Save / Load) asks first; Yes quits. */
static void ask_to_quit(void);
void dolls_ask_to_quit(void) { ask_to_quit(); }

static void ask_to_quit(void)
{
    int prompt = num(get(get(project(), "Settings"), "Quit"), "DollDesignId", -1);
    if (dolls_design(prompt) && dolls_open_form(prompt, 1, 1, 0, 0)) { wins[winCount - 1]->answerToken = ANSWER_QUIT; return; }
    events_request_quit();
}

/* New Game, Continue, Load Game, Quit - on the Title Screen or any paperdoll. */
static void title_button(int wi, int kind)
{
    switch (kind)
    {
        case K_NEWGAME:
            if (wins[wi]->title) dolls_close_window(wi); /* the game begins */
            else events_request_restart(0); /* during play: a fresh game */
            break;
        case K_CONTINUE:
        {
            /* the newest save */
            static SaveInfo newest[1];
            if (save_list(newest, 1) < 1) { game_log("There's no saved game to continue."); break; }
            dolls_request_load(newest[0].folder, newest[0].name);
            break;
        }
        case K_LOADGAME: dolls_open_save_screen(); break; /* to load (no Save there at the title) */
        case K_QUIT: ask_to_quit(); break;
    }
}

int dolls_title_open(void) { for (int i = 0; i < winCount; i++) if (wins[i]->title) return 1; return 0; }

/* The Title Screen (System Settings > Title Screen): filling the view before play, with its music; Continue greyed with no save. 0 = the game has none. */
int dolls_open_title(void)
{
    cJSON *title = get(get(project(), "Settings"), "Title");
    int id = num(title, "DollDesignId", -1);
    if (!dolls_design(id) || !dolls_open_form(id, 1, 1, 0, 1)) return 0;
    DollWin *w = wins[winCount - 1];
    static SaveInfo any[1];
    int saved = save_list(any, 1) > 0;
    for (int i = 0; i < w->state->partCount; i++) if (kind_of(part_at(w->design, i)) == K_CONTINUE) w->state->parts[i].disabled = !saved; /* greyed with nothing to continue */
    if (num(title, "MusicId", -1) >= 0) sound_play_ex(num(title, "MusicId", -1), -1, !cJSON_IsFalse(get(title, "MusicLoops")), OWNER_TITLE, 1);
    return 1;
}

/* ---------------- What's under the mouse ---------------- */

/* In a window: 0 nothing (see-through), 1 a part (clickable), 2 an item in a part, 3 its body. */
int dolls_hit(int x, int y, int *window, int *part, Item **item)
{
    *window = -1; *part = -1; *item = NULL;
    for (int wi = winCount - 1; wi >= 0; wi--)
    {
        DollWin *w = wins[wi];
        int sw, sh;
        dolls_window_size(wi, &sw, &sh);
        if (!w->modal && dolls_modal_open()) continue;
        if (x < w->x || y < w->y || x >= w->x + sw || y >= w->y + sh) { if (w->modal) { *window = wi; return 3; } continue; }
        double s = dolls_scale(w);
        int dx = (int)((x - w->x) / s), dy = (int)((y - w->y) / s);
        *window = wi;
        /* an open list first */
        if (w->openList >= 0)
        {
            cJSON *lp = part_at(w->design, w->openList);
            PartState *r = &w->state->parts[w->openList];
            int px = r->moved ? (int)r->x : num(lp, "X", 0), py = r->moved ? (int)r->y : num(lp, "Y", 0), pw, ph;
            size_of_part(lp, 0, &pw, &ph);
            if (dx >= px && dx < px + pw && dy >= py && dy < py + ph + option_count(lp, r) * row_height(lp)) { *part = w->openList; return 1; }
        }
        int body = 0;
        for (int i = w->state->partCount - 1; i >= 0; i--)
        {
            cJSON *p = part_at(w->design, i);
            PartState *r = &w->state->parts[i];
            if (!r->visible) continue;
            cJSON *lid = closed_lid_over(w->state, p);
            if (lid && !cJSON_IsFalse(get(lid, "HidesWhatItCovers"))) continue;
            int px = r->moved ? (int)r->x : num(p, "X", 0), py = r->moved ? (int)r->y : num(p, "Y", 0), pw, ph;
            size_of_part(p, r->look, &pw, &ph);
            if (dx < px || dy < py || dx >= px + pw || dy >= py + ph) continue;
            int k = kind_of(p);
            int reachable = !lid && !r->disabled;
            if (reachable && (k == K_SLOT || k == K_AREA))
                for (int n = r->itemCount - 1; n >= 0; n--) { *part = i; *item = r->items[n]; if (k == K_SLOT || 1) return 2; }
            if (reachable && clickable(p)) { *part = i; return 1; }
            /* art: solid pixels are its body */
            const Sprite *sp = look_sprite(p, r->look);
            if (!sp) sp = look_sprite(p, 0);
            const Picture *pic = now_picture(sp);
            if (pic) { int ix = (dx - px) * pic->width / (pw ? pw : 1), iy = (dy - py) * pic->height / (ph ? ph : 1); if ((pic->pixels[iy * pic->width + ix] >> 24) > 0) body = 1; }
            else if (k != K_HOTSPOT && k != K_PICTURE && k != K_LIGHT) body = 1;
        }
        if (body || w->modal) return 3;
        *window = -1;
    }
    return 0;
}

/* ---------------- Events ---------------- */

static int fire(DollState *s, int part, int trigger, int itemCategory, int itemId, int combinedPart)
{
    cJSON *p = part_at(s->design, part);
    if (!cJSON_GetArraySize(get(p, "EventPages"))) return 0;
    return events_fire_doll(get(p, "EventPages"), trigger, s, part, itemCategory, itemId, combinedPart);
}

/* ---------------- Clicks ---------------- */

static int find_win(DollState *s) { for (int i = 0; i < winCount; i++) if (wins[i]->state == s) return i; return -1; }

static void control_changed(DollWin *w, int i, const char *what)
{
    cJSON *part = part_at(w->design, i);
    write_variable(part, &w->state->parts[i]);
    if (!fire(w->state, i, TR_USE, 0, 0, -1))
    {
        char line[200];
        snprintf(line, sizeof line, "\"%s\" %s.", str(part, "Name"), what);
        game_log(line);
    }
}

void dolls_stop_typing(void)
{
    for (int wi = 0; wi < winCount; wi++)
        if (wins[wi]->typing >= 0)
        {
            int i = wins[wi]->typing;
            wins[wi]->typing = -1;
            control_changed(wins[wi], i, "typed in");
            return;
        }
}

int dolls_typing(void) { for (int i = 0; i < winCount; i++) if (wins[i]->typing >= 0) return 1; return 0; }

static void play_click(cJSON *part)
{
    int k = kind_of(part);
    if (k == K_SLOT || k == K_AREA || k == K_LID || k == K_PICTURE || k == K_LIGHT || k == K_DISPLAY || k == K_TEXT || k == K_SAVESHOT || k == K_SAVEDETAILS || k == K_PROGRESS) return;
    sound_play(num(sounds_settings(), "NavigateSoundId", -1));
}

/* A single click on a part: buttons, switches, sliders, controls act at once (as Exult's gump buttons). 1 = handled. */
int dolls_click(int wi, int i, int x, int y)
{
    DollWin *w = wins[wi];
    cJSON *part = part_at(w->design, i);
    PartState *r = &w->state->parts[i];
    int k = kind_of(part);
    play_click(part);
    if (r->disabled) return 1;
    if (w->typing >= 0 && w->typing != i) dolls_stop_typing();
    {
        double sc = dolls_scale(w);
        int ppx = r->moved ? (int)r->x : num(part, "X", 0), ppy = r->moved ? (int)r->y : num(part, "Y", 0), sw, sh;
        size_of_part(part, 0, &sw, &sh);
        (void)ppx;
        if (save_part_clicked(w, i, (int)((y - w->y) / sc) - ppy, sh)) return 1; /* the Save/Load screen's own parts */
    }
    if (w->openList >= 0 && w->openList != i) w->openList = -1;
    double s = dolls_scale(w);
    int px = r->moved ? (int)r->x : num(part, "X", 0), py = r->moved ? (int)r->y : num(part, "Y", 0);
    int dx = (int)((x - w->x) / s) - px, dy = (int)((y - w->y) / s) - py, pw, ph;
    size_of_part(part, 0, &pw, &ph);
    char line[200];
    switch (k)
    {
        case K_BUTTON:
            r->look = 1; w->pressPart = i; w->pressMs = PRESS_MS; w->pressAction = 0;
            if (!fire(w->state, i, TR_USE, 0, 0, -1)) { snprintf(line, sizeof line, "You press \"%s\".", str(part, "Name")); game_log(line); }
            return 1;
        case K_CONFIRM: case K_CANCEL: case K_NEWGAME: case K_CONTINUE: case K_LOADGAME: case K_QUIT:
            r->look = 1; w->pressPart = i; w->pressMs = PRESS_MS; w->pressAction = k; /* after the press shows: its event, then what it does */
            return 1;
        case K_SWITCH:
        {
            int n = cJSON_GetArraySize(get(part, "Looks"));
            if (n > 0) r->look = (r->look + 1) % n;
            if (!fire(w->state, i, TR_USE, 0, 0, -1)) { snprintf(line, sizeof line, "\"%s\": %s", str(part, "Name"), str(cJSON_GetArrayItem(get(part, "Looks"), r->look), "Name")); game_log(line); }
            return 1;
        }
        case K_SLIDER:
        {
            int min = num(part, "Min", 0), max = num(part, "Max", 10);
            double t = flag(part, "Vertical") ? 1 - dy / (double)(ph > 1 ? ph : 1) : dx / (double)(pw > 1 ? pw : 1);
            t = t < 0 ? 0 : t > 1 ? 1 : t;
            int value = min + (int)floor(t * (max - min) + 0.5);
            if (value == r->value) return 1;
            r->value = value;
            write_variable(part, r);
            if (!fire(w->state, i, TR_USE, 0, 0, -1)) { snprintf(line, sizeof line, "\"%s\": %d", str(part, "Name"), value); game_log(line); }
            return 1;
        }
        case K_CLOSE:
        {
            DollState *st = w->state;
            fire(st, i, TR_USE, 0, 0, -1);
            int now = find_win(st);
            if (now >= 0) dolls_close_window(now);
            return 1;
        }
        case K_TEXTBOX: w->typing = i; return 1;
        case K_CHECKBOX: r->value = r->look = r->value ? 0 : 1; control_changed(w, i, r->value ? "ticked" : "unticked"); return 1;
        case K_RADIO:
        {
            if (r->value == 1) return 1;
            const char *group = str(part, "RadioGroup");
            for (int o = 0; o < w->state->partCount; o++)
            {
                cJSON *other = part_at(w->design, o);
                if (o == i || kind_of(other) != K_RADIO || strcmp(str(other, "RadioGroup"), group) || !w->state->parts[o].value) continue;
                w->state->parts[o].value = w->state->parts[o].look = 0;
                write_variable(other, &w->state->parts[o]);
            }
            r->value = r->look = 1;
            control_changed(w, i, "picked");
            return 1;
        }
        case K_DROPDOWN:
        {
            if (w->openList != i) { w->openList = i; return 1; }
            w->openList = -1;
            if (dy >= ph)
            {
                int row = (dy - ph) / row_height(part);
                if (row >= 0 && row < option_count(part, r) && row + 1 != r->value) { r->value = row + 1; snprintf(line, sizeof line, "\"%s\" picked", option_at(part, r, row)); control_changed(w, i, line); }
            }
            return 1;
        }
        case K_LISTBOX:
        {
            int rowH = row_height(part), rows = ph / rowH > 1 ? ph / rowH : 1, n = option_count(part, r);
            int first = (r->value < n ? r->value : n) - rows; if (first < 0) first = 0;
            int row = first + dy / rowH;
            if (row >= 0 && row < n && row + 1 != r->value) { r->value = row + 1; snprintf(line, sizeof line, "\"%s\" picked", option_at(part, r, row)); control_changed(w, i, line); }
            return 1;
        }
    }
    return 0;
}

/* A double click: a lid opens or shuts; anything else runs its By Use. */
void dolls_double_click(int wi, int i)
{
    DollWin *w = wins[wi];
    cJSON *part = part_at(w->design, i);
    PartState *r = &w->state->parts[i];
    char line[200];
    if (kind_of(part) == K_LID)
    {
        r->look = r->look == 0 ? 1 : 0;
        if (!fire(w->state, i, TR_USE, 0, 0, -1)) { snprintf(line, sizeof line, "You %s \"%s\".", r->look == 1 ? "open" : "close", str(part, "Name")); game_log(line); }
        return;
    }
    if ((kind_of(part) == K_SAVELIST || kind_of(part) == K_SAVENAME) && w->save) { if (picked_save(w->save)) load_pressed(w); return; } /* double-clicking a save loads it */
    if (is_control_kind(kind_of(part)) || kind_of(part) == K_SWITCH || kind_of(part) == K_SLIDER) return; /* acted on at the click */
    if (!fire(w->state, i, TR_USE, 0, 0, -1)) { snprintf(line, sizeof line, "\"%s\" - nothing happens.", str(part, "Name")); game_log(line); }
}

/* A single click on a part not acted on at once: its name. */
const char *dolls_part_name(int wi, int i) { return str(part_at(wins[wi]->design, i), "Name"); }
int dolls_part_acts_on_click(int wi, int i)
{
    int k = kind_of(part_at(wins[wi]->design, i));
    return is_control_kind(k) || k == K_SWITCH || k == K_SLIDER || k == K_CLOSE || k == K_SAVELIST || k == K_SAVENAME;
}

/* Keys: typing in a text box; on a modal one Escape answers no, Enter presses Yes / OK. 1 = taken. */
static int save_key(int key, int ch);

int dolls_key(int key, int ch)
{
    for (int wi = winCount - 1; wi >= 0; wi--)
    {
        DollWin *w = wins[wi];
        if (w->typing >= 0)
        {
            cJSON *part = part_at(w->design, w->typing);
            PartState *r = &w->state->parts[w->typing];
            if (ch)
            {
                size_t n = strlen(r->text);
                int most = num(part, "MaxLength", 20);
                if ((int)n >= (most > 1 ? most : 1) || n + 1 >= sizeof r->text) return 1;
                if (flag(part, "NumbersOnly") && !(isdigit(ch) || (ch == '-' && n == 0))) return 1;
                r->text[n] = (char)ch; r->text[n + 1] = 0;
                return 1;
            }
            if (key == KEY_BACKSPACE) { size_t n = strlen(r->text); if (n) r->text[n - 1] = 0; return 1; }
            if (key == KEY_ENTER || key == KEY_TAB || key == KEY_ESCAPE) { dolls_stop_typing(); return 1; }
            return 1; /* nothing else while typing */
        }
        if (!w->modal) { if (w->save && save_key(key, ch)) return 1; continue; }
        if (ch) return 1;
        if (key == KEY_ESCAPE) { if (!w->title) answer(wi, 0); return 1; }
        if (key == KEY_ENTER)
            for (int i = 0; i < w->state->partCount; i++)
                if (kind_of(part_at(w->design, i)) == K_CONFIRM && w->state->parts[i].visible) { dolls_click(wi, i, w->x, w->y); return 1; }
        return 1; /* nothing reaches the game */
    }
    return 0;
}

/* ---------------- Each frame: pressed buttons, sliding parts ---------------- */

void dolls_update(double ms)
{
    static double hudCheckMs;
    if ((hudCheckMs += ms) >= 250) { hudCheckMs = 0; keep_hud_dolls(); }
    for (int wi = winCount - 1; wi >= 0; wi--)
        if (wins[wi]->closeInMs > 0 && (wins[wi]->closeInMs -= ms) <= 0) dolls_close_window(wi);
    for (int wi = 0; wi < winCount; wi++) if (wins[wi]->save && wins[wi]->save->confirmMs > 0) wins[wi]->save->confirmMs -= ms;
    for (int wi = 0; wi < winCount; wi++)
    {
        DollWin *w = wins[wi];
        if (w->pressPart < 0 || (w->pressMs -= ms) > 0) continue;
        int i = w->pressPart, action = w->pressAction;
        w->pressPart = -1;
        w->state->parts[i].look = 0;
        if (!action) continue;
        DollState *s = w->state;
        fire(s, i, TR_USE, 0, 0, -1);
        int now = find_win(s);
        if (now < 0) break;
        if (action == K_CONFIRM || action == K_CANCEL) { answer(now, action == K_CONFIRM); break; }
        if (action == K_LOADBUTTON) { if (wins[now]->save) load_pressed(wins[now]); break; }
        title_button(now, action); /* New Game, Continue, Load Game, Quit */
        break;
    }
    for (int i = 0; i < slideCount; i++)
    {
        Slide *sl = &slides[i];
        sl->ms = sl->ms + ms > sl->total ? sl->total : sl->ms + ms;
        double t = sl->total <= 0 ? 1 : sl->ms / sl->total;
        PartState *r = &sl->state->parts[sl->part];
        r->moved = 1; r->x = sl->fx + (sl->tx - sl->fx) * t; r->y = sl->fy + (sl->ty - sl->fy) * t;
        if (t >= 1) { slides[i--] = slides[--slideCount]; }
    }
}

/* ---------------- Items in slots and areas ---------------- */

cJSON *dolls_fit_for(cJSON *slot, int category, int itemId)
{
    static cJSON *any;
    if (kind_of(slot) != K_SLOT) return NULL;
    cJSON *accepts = get(slot, "Accepts"), *a;
    if (!cJSON_GetArraySize(accepts)) { if (!any) any = cJSON_CreateObject(); return any; } /* takes anything, in its own look */
    cJSON_ArrayForEach(a, accepts) if (num(get(a, "Item"), "Category", -1) == category && num(get(a, "Item"), "ItemId", -2) == itemId) return a;
    return NULL;
}

/* The front-most part under a point that's shown and in reach (kinds: a mask of 1 << kind; 0 any). */
static int part_at_point(DollWin *w, int dx, int dy, unsigned long kinds, int excluding, int needsTrigger)
{
    for (int i = w->state->partCount - 1; i >= 0; i--)
    {
        cJSON *p = part_at(w->design, i);
        PartState *r = &w->state->parts[i];
        if (i == excluding || !r->visible || closed_lid_over(w->state, p)) continue;
        if (kinds && !(kinds & (1ul << kind_of(p)))) continue;
        if (needsTrigger && !has_pages(p, needsTrigger)) continue;
        int px = r->moved ? (int)r->x : num(p, "X", 0), py = r->moved ? (int)r->y : num(p, "Y", 0), pw, ph;
        size_of_part(p, r->look, &pw, &ph);
        if (dx >= px && dy >= py && dx < px + pw && dy < py + ph) return i;
    }
    return -1;
}

/* An item dropped on a window: a part programmed for it (By Combined With An Item) takes the say first - 2: it ran (the item stays where it was unless used up);
   else into the slot or area under it - 1: it's in (the item is the paperdoll's now); 0: refused. */
int dolls_drop(int wi, int x, int y, int topLeftX, int topLeftY, int category, int itemId, Item *(*lift)(void), void (*put_back)(Item *), void (*landed)(Item *), const DollState *payloadDoll, const char *name)
{
    DollWin *w = wins[wi];
    double s = dolls_scale(w);
    int dx = (int)((x - w->x) / s), dy = (int)((y - w->y) / s), tx = (int)((topLeftX - w->x) / s), ty = (int)((topLeftY - w->y) / s);
    char line[200];
    int combineOn = part_at_point(w, dx, dy, 0, -1, TR_COMBINE_ITEM);
    if (combineOn >= 0 && !(gumps_drag_doll_state() == w->state && gumps_drag_doll_part() == combineOn))
    {
        int ran = fire(w->state, combineOn, TR_COMBINE_ITEM, category, itemId, -1);
        if (ran) return 2;
    }
    int i = part_at_point(w, dx, dy, (1ul << K_SLOT) | (1ul << K_AREA), -1, 0);
    if (i < 0) { snprintf(line, sizeof line, "\"%s\" doesn't go there.", name); gumps_refuse(P_WONT_FIT, line); return 0; }
    cJSON *part = part_at(w->design, i);
    PartState *r = &w->state->parts[i];
    if (payloadDoll == w->state) { snprintf(line, sizeof line, "\"%s\" can't go inside itself.", name); gumps_refuse(P_RED_X, line); return 0; }
    if (gumps_drag_doll_state() == w->state && gumps_drag_doll_part() == i)
    {
        if (kind_of(part) != K_AREA) return 0; /* back where it came from */
    }
    else
    {
        if (kind_of(part) == K_SLOT && !dolls_fit_for(part, category, itemId)) { snprintf(line, sizeof line, "\"%s\" doesn't fit in \"%s\".", name, str(part, "Name")); gumps_refuse(P_WONT_FIT, line); return 0; }
        int cap = num(part, "Capacity", 1);
        if (r->itemCount >= (cap > 1 ? cap : 1)) { snprintf(line, sizeof line, "\"%s\" is full.", str(part, "Name")); gumps_refuse(P_WONT_FIT, line); return 0; }
    }
    Item *it = lift();
    if (!it) return 0;
    if (kind_of(part) == K_AREA)
    {
        int px = r->moved ? (int)r->x : num(part, "X", 0), py = r->moved ? (int)r->y : num(part, "Y", 0), aw = num(part, "Width", 32), ah = num(part, "Height", 32);
        int ix = tx - px, iy = ty - py;
        ix = ix < 0 ? 0 : ix > (aw - 8 > 0 ? aw - 8 : 0) ? (aw - 8 > 0 ? aw - 8 : 0) : ix;
        iy = iy < 0 ? 0 : iy > (ah - 8 > 0 ? ah - 8 : 0) ? (ah - 8 > 0 ? ah - 8 : 0) : iy;
        items_set_spot(it, ix, iy, 0);
    }
    if (r->itemCount == r->itemCap) { r->itemCap = r->itemCap ? r->itemCap * 2 : 4; r->items = (Item **)realloc(r->items, sizeof(Item *) * (size_t)r->itemCap); }
    r->items[r->itemCount++] = it;
    landed(it);
    (void)put_back;
    sound_play(num(sounds_settings(), "NavigateSoundId", -1));
    snprintf(line, sizeof line, "You put \"%s\" into \"%s\".", name, str(part, "Name"));
    game_log(line);
    fire(w->state, i, TR_DROP, category, itemId, -1);
    return 1;
}


/* An item taken out of a part (a drag from it): out of the list; where it was, to put it back. */
int dolls_take_item(DollState *s, int part, Item *it)
{
    PartState *r = &s->parts[part];
    for (int i = 0; i < r->itemCount; i++)
        if (r->items[i] == it) { memmove(&r->items[i], &r->items[i + 1], sizeof(Item *) * (size_t)(r->itemCount - i - 1)); r->itemCount--; return i; }
    return -1;
}

void dolls_restore_item(DollState *s, int part, Item *it, int index)
{
    PartState *r = &s->parts[part];
    if (r->itemCount == r->itemCap) { r->itemCap = r->itemCap ? r->itemCap * 2 : 4; r->items = (Item **)realloc(r->items, sizeof(Item *) * (size_t)r->itemCap); }
    if (index < 0 || index > r->itemCount) index = r->itemCount;
    memmove(&r->items[index + 1], &r->items[index], sizeof(Item *) * (size_t)(r->itemCount - index));
    r->items[index] = it;
    r->itemCount++;
}

void dolls_item_taken(DollState *s, int part, Item *it) { fire(s, part, TR_PICKUP, items_category(it), items_item_id(it), -1); }
DollState *dolls_window_state(int wi) { return wins[wi]->state; }
int dolls_part_draggable(int wi, int i) { return flag(part_at(wins[wi]->design, i), "Draggable") && !closed_lid_over(wins[wi]->state, part_at(wins[wi]->design, i)); }
const Sprite *dolls_part_look(int wi, int i) { PartState *r = &wins[wi]->state->parts[i]; const Sprite *s = look_sprite(part_at(wins[wi]->design, i), r->look); return s ? s : look_sprite(part_at(wins[wi]->design, i), 0); }
int dolls_is_reachable_window(int wi) { (void)wi; return 1; }
DollState *dolls_state_of(void *item) { return (DollState *)items_doll((Item *)item); }

/* A part let go over another of the same paperdoll: that part's By Combined With A Part. The dragged part goes back. */
void dolls_part_dropped(int wi, int dragged, int x, int y)
{
    DollWin *w = wins[wi];
    double s = dolls_scale(w);
    int t = part_at_point(w, (int)((x - w->x) / s), (int)((y - w->y) / s), 0, dragged, 0);
    if (t < 0) return;
    if (!fire(w->state, t, TR_COMBINE_PART, 0, 0, num(part_at(w->design, dragged), "Id", -1)))
    {
        char line[200];
        snprintf(line, sizeof line, "\"%s\" and \"%s\" - nothing happens.", str(part_at(w->design, dragged), "Name"), str(part_at(w->design, t), "Name"));
        game_log(line);
    }
}

/* ---------------- Commands (Paperdoll; the paperdoll script commands) ---------------- */

/* The paperdoll a command means: this one (the event's), or the first open one of that design. */
static DollWin *win_for(DollState *context, int designId)
{
    if (designId >= 0) { for (int i = 0; i < winCount; i++) if (num(wins[i]->design, "Id", -1) == designId) return wins[i]; return NULL; }
    if (!context) return NULL;
    for (int i = 0; i < winCount; i++) if (wins[i]->state == context) return wins[i];
    return NULL;
}

/* The Paperdoll command: on the paperdoll of the event that's running (open or not). 1 = done. */
int dolls_paperdoll_command(DollState *s, const char *action, int partId, const char *partName, cJSON *params)
{
    char line[200];
    if (!s) { game_log("Paperdoll command: no paperdoll here (it runs from a paperdoll part's events, or an object's that opens one)."); return 0; }
    int i = partId >= 0 ? index_of_id(s->design, partId) : -1;
    int needsPart = strcmp(action, "UseUpCombined") && strcmp(action, "PlaySound") && strcmp(action, "StopSounds") && strcmp(action, "Close") && strcmp(action, "TurnInto");
    if (needsPart && i < 0) { snprintf(line, sizeof line, "Paperdoll command: \"%s\" has no part \"%s\".", str(s->design, "Name"), partName); game_log(line); return 0; }
    cJSON *part = i >= 0 ? part_at(s->design, i) : NULL;
    PartState *r = i >= 0 ? &s->parts[i] : NULL;
    int value = atoi(str(params, "Value")), min = num(part, "Min", 0), max = num(part, "Max", 10);
    if (!strcmp(action, "SetLook")) { int n = cJSON_GetArraySize(get(part, "Looks")); r->look = clampi(atoi(str(params, "Look")), 0, n > 1 ? n - 1 : 0); }
    else if (!strcmp(action, "NextLook")) { int n = cJSON_GetArraySize(get(part, "Looks")); if (n > 0) r->look = (r->look + 1) % n; }
    else if (!strcmp(action, "Show")) r->visible = 1;
    else if (!strcmp(action, "Hide")) r->visible = 0;
    else if (!strcmp(action, "SetValue")) r->value = clampi(value, min, max > min ? max : min);
    else if (!strcmp(action, "AddValue")) r->value = clampi(r->value + value, min, max > min ? max : min);
    else if (!strcmp(action, "OpenLid")) r->look = 1;
    else if (!strcmp(action, "CloseLid")) r->look = 0;
    else if (!strcmp(action, "PutItem"))
    {
        static const char *CATS[] = { "", "", "", "Book", "Object", "Container" };
        int category = -1;
        for (int c = 3; c <= 5; c++) if (!strcmp(str(params, "ItemCategory"), CATS[c])) category = c;
        if (category < 0 || !str(params, "ItemId")[0]) return 0;
        int cap = num(part, "Capacity", 1);
        if (r->itemCount >= (cap > 1 ? cap : 1)) { snprintf(line, sizeof line, "\"%s\" is full.", str(part, "Name")); gumps_refuse(P_WONT_FIT, line); return 0; }
        dolls_restore_item(s, i, items_make(category, atoi(str(params, "ItemId")), 1), -1);
    }
    else if (!strcmp(action, "UseUpItems")) { for (int k = 0; k < r->itemCount; k++) items_free(r->items[k]); r->itemCount = 0; }
    else if (!strcmp(action, "UseUpCombined")) return gumps_use_up_combined();
    else if (!strcmp(action, "ToVariable"))
    {
        const char *name = str(params, "VariableName"), *what = str(params, "State");
        if (!name[0]) return 0;
        events_set_variable(name, !strcmp(what, "Look") ? r->look + 1 : !strcmp(what, "Value") ? r->value : !strcmp(what, "Items") ? r->itemCount : r->visible);
    }
    else if (!strcmp(action, "PlaySound"))
    {
        if (!str(params, "SoundResourceId")[0]) return 0;
        sound_play_ex(atoi(str(params, "SoundResourceId")), -1, !strcmp(str(params, "Loop"), "True"), s->soundOwner, 1); /* this copy's: it plays on after the window closes */
    }
    else if (!strcmp(action, "StopSounds")) sound_stop_owner(s->soundOwner);
    else if (!strcmp(action, "Close")) { int w; while ((w = find_win(s)) >= 0) dolls_close_window(w); return 1; }
    else if (!strcmp(action, "TurnInto"))
    {
        int w;
        while ((w = find_win(s)) >= 0) dolls_close_window(w);
        sound_stop_owner(s->soundOwner);
        return gumps_turn_into(s, atoi(str(params, "ObjectId")));
    }
    else return 0;
    if (part) write_variable(part, r);
    return 1;
}

/* The paperdoll script commands: 1 = done. */
int dolls_script_command(const char *command, DollState *context, int designId, const char *partName, const char *value, double number, int valueIsText, double x, double y, double seconds, cJSON *params)
{
    DollWin *w = win_for(context, designId);
    char line[200];
    if (!w) { snprintf(line, sizeof line, "%s: that paperdoll isn't open.", command); game_log(line); return 0; }
    int i = index_of_name(w->design, partName);
    if (i < 0) { snprintf(line, sizeof line, "%s: \"%s\" has no part \"%s\".", command, str(w->design, "Name"), partName); game_log(line); return 0; }
    cJSON *part = part_at(w->design, i);
    PartState *r = &w->state->parts[i];
    int k = kind_of(part), n = (int)floor(number + 0.5);
    if (!strcmp(command, "ShowHidePart")) r->visible = !strcmp(value, "Hidden") ? 0 : !strcmp(value, "Toggled") ? !r->visible : 1;
    else if (!strcmp(command, "SetPartLook"))
    {
        int looks = cJSON_GetArraySize(get(part, "Looks")), index = -1;
        if (valueIsText) { int li = 0; cJSON *l; cJSON_ArrayForEach(l, get(part, "Looks")) { const char *ln = str(l, "Name"); size_t a = 0; while (ln[a] && value[a] && tolower((unsigned char)ln[a]) == tolower((unsigned char)value[a])) a++; if (!ln[a] && !value[a]) { index = li; break; } li++; } }
        else index = n - 1;
        if (index < 0 || index >= (looks > 1 ? looks : 1)) return 0;
        r->look = index;
    }
    else if (!strcmp(command, "SetPartText"))
    {
        if (k == K_TEXTBOX) snprintf(r->text, sizeof r->text, "%s", value);
        else { free(r->caption); r->caption = strdup(value); }
    }
    else if (!strcmp(command, "SetPartValue"))
    {
        switch (k)
        {
            case K_TEXTBOX: snprintf(r->text, sizeof r->text, "%s", value); break;
            case K_CHECKBOX: r->value = r->look = n != 0; break;
            case K_RADIO:
                r->value = r->look = n != 0;
                if (r->value)
                    for (int o = 0; o < w->state->partCount; o++)
                    {
                        cJSON *other = part_at(w->design, o);
                        if (o == i || kind_of(other) != K_RADIO || strcmp(str(other, "RadioGroup"), str(part, "RadioGroup"))) continue;
                        w->state->parts[o].value = w->state->parts[o].look = 0;
                        write_variable(other, &w->state->parts[o]);
                    }
                break;
            case K_DROPDOWN: case K_LISTBOX: r->value = clampi(n, 0, option_count(part, r)); break;
            default: { int min = num(part, "Min", 0), max = num(part, "Max", 10); r->value = max > min ? clampi(n, min, max) : n; }
        }
    }
    else if (!strcmp(command, "GetPartValue"))
    {
        const char *var = str(params, "Variable");
        if (k == K_TEXTBOX) script_set(var, value_text(r->text)); else events_set_variable(var, r->value);
        return 1;
    }
    else if (!strcmp(command, "FillListFromArray"))
    {
        char (*texts)[96] = (char (*)[96])malloc(96 * 256);
        int count = script_array_texts(str(params, "Array"), texts, 256);
        if (count < 0) { free(texts); return 0; }
        free(r->options);
        r->options = texts; r->optionCount = count; r->hasOptions = 1;
        r->value = clampi(r->value, 0, count);
    }
    else if (!strcmp(command, "GetPickedChoice"))
    {
        events_set_variable(str(params, "Variable"), r->value);
        if (str(params, "Variable2")[0]) script_set(str(params, "Variable2"), value_text(r->value >= 1 && r->value <= option_count(part, r) ? option_at(part, r, r->value - 1) : ""));
        return 1;
    }
    else if (!strcmp(command, "EnablePart")) r->disabled = !strcmp(value, "Disabled");
    else if (!strcmp(command, "MovePart"))
    {
        for (int sI = 0; sI < slideCount; sI++) if (slides[sI].state == w->state && slides[sI].part == i) slides[sI--] = slides[--slideCount];
        if (seconds <= 0) { r->moved = 1; r->x = x; r->y = y; }
        else if (slideCount < 16) slides[slideCount++] = (Slide){ w->state, i, r->moved ? r->x : num(part, "X", 0), r->moved ? r->y : num(part, "Y", 0), x, y, 0, seconds * 1000 };
    }
    else if (!strcmp(command, "PutItemInSlot"))
    {
        if (k != K_SLOT && k != K_AREA) return 0;
        static const char *CATS[] = { "", "", "", "Book", "Object", "Container" };
        int category = -1;
        for (int c = 3; c <= 5; c++) if (!strcmp(str(params, "Category"), CATS[c])) category = c;
        int itemId = atoi(str(params, "ItemId"));
        if (category < 0 || itemId < 0 || !str(params, "ItemId")[0]) return 0;
        int count = n > 1 ? n : 1, cap = num(part, "Capacity", 1), room = (cap > 1 ? cap : 1) - r->itemCount;
        if (room <= 0) return 0;
        if (items_stackable(category, itemId)) dolls_restore_item(w->state, i, items_make(category, itemId, count), -1);
        else for (int c = 0; c < count && c < room; c++) dolls_restore_item(w->state, i, items_make(category, itemId, 1), -1);
    }
    else if (!strcmp(command, "TakeItemFromSlot"))
    {
        int toPlayer = !strcmp(str(params, "To"), "Go to the main player");
        for (int c = 0; c < r->itemCount; c++)
        {
            Item *it = r->items[c];
            if (toPlayer && !items_give_to(world_actor_character(0), it))
            {
                int px, py, pf;
                world_actor_cell(0, &px, &py, &pf);
                int uid = world_thing_put(items_category(it), items_item_id(it), items_quantity(it), px, py, pf);
                if (uid >= 0) items_park(uid, it); else items_free(it);
            }
            else if (!toPlayer) items_free(it);
        }
        r->itemCount = 0;
    }
    else if (!strcmp(command, "FocusTextBox")) { if (k != K_TEXTBOX) return 0; w->typing = i; return 1; }
    else return 0;
    write_variable(part, r);
    return 1;
}

int dolls_close_command(const char *which, DollState *context, int designId)
{
    int closed = 0;
    for (int i = winCount - 1; i >= 0; i--)
    {
        int match = !strcmp(which, "Every paperdoll") ? !wins[i]->title : !strcmp(which, "One paperdoll") ? num(wins[i]->design, "Id", -1) == designId : wins[i]->state == context;
        if (match) { dolls_close_window(i); closed++; }
    }
    return closed > 0;
}

int dolls_move_command(DollState *context, int designId, int move, double x, double y, int front)
{
    DollWin *w = win_for(context, designId);
    if (!w) return 0;
    if (move) { w->x = (int)x; w->y = (int)y; }
    if (front) for (int i = 0; i < winCount; i++) if (wins[i] == w) { dolls_to_front(i); break; }
    return 1;
}

/* ---------------- Saving (DollState: DesignId, Parts by id) ---------------- */

cJSON *dolls_state_json(void *p)
{
    DollState *s = (DollState *)p;
    cJSON *j = cJSON_CreateObject(), *parts = cJSON_CreateObject();
    cJSON_AddNumberToObject(j, "DesignId", s->designId);
    for (int i = 0; i < s->partCount; i++)
    {
        PartState *r = &s->parts[i];
        cJSON *e = cJSON_CreateObject(), *its = cJSON_CreateArray();
        cJSON_AddNumberToObject(e, "Look", r->look);
        cJSON_AddBoolToObject(e, "Visible", r->visible);
        cJSON_AddNumberToObject(e, "Value", r->value);
        cJSON_AddBoolToObject(e, "Disabled", r->disabled);
        if (r->moved) { cJSON_AddNumberToObject(e, "X", r->x); cJSON_AddNumberToObject(e, "Y", r->y); }
        if (r->caption) cJSON_AddStringToObject(e, "Caption", r->caption);
        if (r->hasOptions) { cJSON *o = cJSON_CreateArray(); for (int k = 0; k < r->optionCount; k++) cJSON_AddItemToArray(o, cJSON_CreateString(r->options[k])); cJSON_AddItemToObject(e, "Options", o); }
        cJSON_AddStringToObject(e, "Text", r->text);
        for (int k = 0; k < r->itemCount; k++) cJSON_AddItemToArray(its, items_item_json(r->items[k]));
        cJSON_AddItemToObject(e, "Items", its);
        char key[16];
        snprintf(key, sizeof key, "%d", num(part_at(s->design, i), "Id", i));
        cJSON_AddItemToObject(parts, key, e);
    }
    cJSON_AddItemToObject(j, "Parts", parts);
    return j;
}

/* A saved paperdoll (or one from a Windows save's DollRuntime): parts it no longer has are dropped, new ones start as designed. */
void *dolls_state_from_json(cJSON *j)
{
    if (!designs) dolls_load();
    cJSON *design = dolls_design(num(j, "DesignId", -1));
    if (!design) return NULL;
    DollState *s = new_state(design);
    for (int i = 0; i < s->partCount; i++)
    {
        char key[16];
        snprintf(key, sizeof key, "%d", num(part_at(design, i), "Id", i));
        cJSON *e = get(get(j, "Parts"), key), *x;
        if (!e) continue;
        PartState *r = &s->parts[i];
        for (int k = 0; k < r->itemCount; k++) items_free(r->items[k]);
        r->itemCount = 0;
        r->look = num(e, "Look", r->look);
        r->visible = !cJSON_IsFalse(get(e, "Visible"));
        r->value = num(e, "Value", r->value);
        r->disabled = flag(e, "Disabled");
        if (cJSON_IsNumber(get(e, "X")) && cJSON_IsNumber(get(e, "Y"))) { r->moved = 1; r->x = get(e, "X")->valuedouble; r->y = get(e, "Y")->valuedouble; }
        if (cJSON_IsString(get(e, "Caption"))) r->caption = strdup(str(e, "Caption"));
        if (cJSON_IsArray(get(e, "Options")))
        {
            int n = cJSON_GetArraySize(get(e, "Options")), k = 0;
            r->options = (char (*)[96])calloc((size_t)(n + 1), 96);
            cJSON_ArrayForEach(x, get(e, "Options")) snprintf(r->options[k++], 96, "%s", cJSON_GetStringValue(x) ? cJSON_GetStringValue(x) : "");
            r->optionCount = n; r->hasOptions = 1;
        }
        snprintf(r->text, sizeof r->text, "%s", str(e, "Text"));
        cJSON_ArrayForEach(x, get(e, "Items")) dolls_restore_item(s, i, items_read_json(x), -1);
    }
    return s;
}

/* ---------------- The Save/Load screen (System Settings > Save / Load) ---------------- */

static cJSON *save_settings(void) { return get(get(project(), "Settings"), "SaveLoad"); }

/* The saved game a Save slot holds: its own number, or its place among the paperdoll's Save slots. */
static int slot_number(cJSON *design, int partIndex)
{
    cJSON *part = part_at(design, partIndex);
    if (num(part, "SaveSlot", 0) > 0) return num(part, "SaveSlot", 0);
    int n = 0;
    for (int i = 0; i <= partIndex; i++) if (kind_of(part_at(design, i)) == K_SAVENAME) n++;
    return n;
}

static int has_kind(cJSON *design, int kind) { cJSON *p; cJSON_ArrayForEach(p, parts_of(design)) if (kind_of(p) == kind) return 1; return 0; }
static int slot_count(cJSON *design) { int n = 0; cJSON *p; cJSON_ArrayForEach(p, parts_of(design)) if (kind_of(p) == K_SAVENAME) n++; return n; }
static SaveInfo *in_slot(SaveScreen *s, int slot) { for (int i = 0; i < s->count; i++) if (s->saves[i].slot == slot) return &s->saves[i]; return NULL; }
static SaveInfo *picked_save(SaveScreen *s) { return s->pickedSlot > 0 ? in_slot(s, s->pickedSlot) : s->picked >= 0 && s->picked < s->count ? &s->saves[s->picked] : NULL; }

static void pick_slot(SaveScreen *s, int slot)
{
    s->pickedSlot = slot;
    SaveInfo *save = in_slot(s, slot);
    snprintf(s->name, sizeof s->name, "%s", save ? save->name : "");
    s->picked = save ? (int)(save - s->saves) : -1;
    s->confirmKind = 0;
}

static void pick_row(DollWin *w, int row)
{
    SaveScreen *s = w->save;
    s->picked = row;
    int slot = s->saves[row].slot, found = 0;
    for (int i = 0; i < w->state->partCount; i++) if (kind_of(part_at(w->design, i)) == K_SAVENAME && slot_number(w->design, i) == slot) found = 1;
    s->pickedSlot = slot > 0 && found ? slot : 0;
    snprintf(s->name, sizeof s->name, "%s", s->saves[row].name);
    s->confirmKind = 0;
}

static void refresh_saves(SaveScreen *s) { s->count = save_list(s->saves, MAX_SAVES); }

static DollWin *save_window(int *index)
{
    for (int i = winCount - 1; i >= 0; i--) if (wins[i]->save) { if (index) *index = i; return wins[i]; }
    return NULL;
}

int dolls_save_screen_open(void) { return save_window(NULL) != NULL; }

/* The S key (Exult's File gump): the Save/Load screen opens - as designed, every time. */
void dolls_open_save_screen(void)
{
    int wi;
    if (save_window(&wi)) { dolls_to_front(wi); return; }
    cJSON *design = dolls_design(num(save_settings(), "DollDesignId", -1));
    if (!design) { game_error(101, ""); return; }
    DollWin *w = open_window(form_state(design, 1), design, 0, 0);
    if (!w) return;
    w->save = (SaveScreen *)calloc(1, sizeof(SaveScreen));
    w->save->picked = -1;
    refresh_saves(w->save);
    for (int i = 0; i < w->state->partCount; i++) if (kind_of(part_at(design, i)) == K_SAVEBUTTON && dolls_title_open()) w->state->parts[i].visible = 0; /* at the title: load only */
    game_log(slot_count(design) ? "Click a slot: type a name and Save, or Load what it holds." : w->save->count ? "Pick a save, or type a new name." : "No saved games yet - type a name and press Save.");
}

static void save_text(cJSON *part, const char *text, int x, int y, int faint)
{
    if (!text[0]) return;
    uint32_t tint = tint_of(part);
    if (faint) tint = 0xFF999999u;
    font_draw_into(font_of(part), cv, cvw, cvh, x, y, text, tint, scale_of_text(part), 0);
}

static void draw_save_part(DollWin *win, cJSON *part, int i, int x, int y, int w, int h, int fade)
{
    const Sprite *look = look_sprite(part, 0);
    int k = kind_of(part), row = row_height(part);
    SaveScreen *s = win->save;
    if (look && k != K_SAVESHOT) cv_blit(now_picture(look), x, y, w, h, x, y, x + w, y + h, fade);
    if (!s) return;
    switch (k)
    {
        case K_SAVELIST:
        {
            int rows = h / row > 1 ? h / row : 1;
            if (s->scroll > s->count - rows) s->scroll = s->count - rows;
            if (s->scroll < 0) s->scroll = 0;
            if (!s->count) { save_text(part, "(no saved games)", x + 2, y + 1, 1); break; }
            for (int n = s->scroll; n < s->count && n < s->scroll + rows; n++)
            {
                int yy = y + (n - s->scroll) * row;
                if (n == s->picked) { const Sprite *m = look_sprite(part, 1); if (m) cv_blit(now_picture(m), x, yy, w, row, x, y, x + w, y + h, 0); else cv_fill(x, yy, w, row, PLAIN_PICKED); }
                save_text(part, s->saves[n].name, x + 2, yy + 1, 0);
            }
            break;
        }
        case K_SAVENAME:
        {
            int slot = slot_number(win->design, i), picked = s->pickedSlot == slot;
            if (picked) { const Sprite *m = look_sprite(part, 1); if (m) cv_blit(now_picture(m), x, y, w, h, x, y, x + w, y + h, 0); else cv_fill(x, y, w, h, PLAIN_PICKED); }
            else if (!look) { cv_fill(x, y, w, 1, 0x46FFFFFFu); cv_fill(x, y + h - 1, w, 1, 0x46FFFFFFu); cv_fill(x, y, 1, h, 0x46FFFFFFu); cv_fill(x + w - 1, y, 1, h, 0x46FFFFFFu); } /* an empty slot can still be found */
            char text[64];
            SaveInfo *save = in_slot(s, slot);
            if (picked) snprintf(text, sizeof text, "%s_", s->name); else snprintf(text, sizeof text, "%s", save ? save->name : ""); /* an empty slot is blank, as in Exult */
            save_text(part, text, x + 2, y + (h - row > 0 ? (h - row) / 2 : 0) + 1, 0);
            break;
        }
        case K_SAVESHOT:
        {
            SaveInfo *save = picked_save(s);
            if (save)
            {
                if (strcmp(s->shotFolder, save->folder))
                {
                    char path[1300];
                    picture_free(s->shot);
                    s->shot = picture_from_file(save_screenshot_path(save->folder, path, sizeof path));
                    snprintf(s->shotFolder, sizeof s->shotFolder, "%s", save->folder);
                }
                if (s->shot)
                {
                    double sc = fmin((double)w / s->shot->width, (double)h / s->shot->height);
                    int dw = (int)(s->shot->width * sc), dh = (int)(s->shot->height * sc);
                    cv_blit(s->shot, x + (w - dw) / 2, y + (h - dh) / 2, dw, dh, x, y, x + w, y + h, 0);
                }
            }
            if (look) cv_blit(now_picture(look), x, y, w, h, x, y, x + w, y + h, fade); /* its frame, over the picture */
            break;
        }
        case K_SAVEDETAILS:
        {
            SaveInfo *save = picked_save(s);
            if (!save) break;
            char when[48];
            snprintf(when, sizeof when, "Saved %s", save->savedAt);
            const char *lines[3] = { save->name, save->gameTime, when };
            int yy = y + 1;
            for (int n = 0; n < 3; n++) if (lines[n][0]) { save_text(part, lines[n], x + 2, yy, 0); yy += row; }
            break;
        }
    }
}

/* Tells the player on the screen itself and in Game.log. */
static void say(const char *text) { game_log(text); } /* the Windows engine's status bar - not shown in a built game */

static void save_error(int number, const char *detail) { game_error_shown(number, detail, 0); }

static void write_save(DollWin *w, int slot, const char *name, const char *over)
{
    const char *busy = events_save_blocker();
    if (busy) { save_error(102, busy); return; }
    char error[300], line[120];
    dolls_disk_begin(); /* the saving progress paperdoll, if there is one */
    dolls_disk_report(0.15);
    if (!save_write(name, slot, over, error, sizeof error)) { dolls_disk_end(1); save_error(106, error); return; }
    dolls_disk_end(0);
    snprintf(line, sizeof line, "Game saved: \"%s\".", name);
    int savedId = num(save_settings(), "SavedDollDesignId", -1);
    if (dolls_design(savedId)) { game_log(line); dolls_open_form(savedId, 1, 1, 0, 0); } /* the maker's own "Game saved" paperdoll */
    else say(line);
    /* Exult's Newfile_gump::save: the screen stays open, nothing picked any more */
    refresh_saves(w->save);
    w->save->pickedSlot = 0; w->save->picked = -1; w->save->name[0] = 0; w->save->confirmKind = 0;
}

/* Writing over or deleting a save asks first: the "Are you sure?" paperdoll, else the same button pressed again. 1 = go ahead now. */
static int ask_first(DollWin *w, int kind, const char *folder, const char *question, int token)
{
    int prompt = num(save_settings(), "ConfirmDollDesignId", -1);
    SaveScreen *s = w->save;
    if (dolls_design(prompt)) { game_log(question); if (dolls_open_form(prompt, 1, 1, 0, 0)) wins[winCount - 1]->answerToken = token; return 0; }
    if (s->confirmKind == kind && !strcmp(s->confirmFolder, folder) && s->confirmMs > 0) { s->confirmKind = 0; return 1; }
    s->confirmKind = kind; snprintf(s->confirmFolder, sizeof s->confirmFolder, "%s", folder); s->confirmMs = 4000;
    say(question);
    return 0;
}

static void save_pressed(DollWin *w)
{
    SaveScreen *s = w->save;
    int slot = s->pickedSlot;
    if (slot == 0 && slot_count(w->design) > 0 && !has_kind(w->design, K_SAVELIST)) { save_error(103, ""); return; }
    char name[MAX_SAVE_NAME + 1];
    const char *a = s->name;
    while (*a == ' ') a++;
    snprintf(name, sizeof name, "%s", a);
    size_t n = strlen(name);
    while (n && name[n - 1] == ' ') name[--n] = 0;
    if (!n) { save_error(111, ""); return; }
    const char *busy = events_save_blocker();
    if (busy) { save_error(102, busy); return; }
    SaveInfo *existing = NULL;
    if (slot > 0) existing = in_slot(s, slot); /* a slot is written over whatever its save was called */
    else for (int i = 0; i < s->count && !existing; i++) if (s->saves[i].slot == 0 && !strcmp(s->saves[i].name, name)) existing = &s->saves[i];
    if (!existing) { write_save(w, slot, name, NULL); return; }
    char question[160];
    snprintf(question, sizeof question, "Write over \"%s\"? Press Save again.", existing->name);
    s->pendingSlot = slot; snprintf(s->pendingName, sizeof s->pendingName, "%s", name); snprintf(s->pendingFolder, sizeof s->pendingFolder, "%s", existing->folder);
    if (ask_first(w, K_SAVEBUTTON, existing->folder, question, ANSWER_SAVE_OVER)) write_save(w, slot, name, existing->folder);
}

static void delete_pressed(DollWin *w)
{
    SaveScreen *s = w->save;
    SaveInfo *save = picked_save(s);
    if (!save) { save_error(s->pickedSlot > 0 ? 105 : 104, ""); return; }
    char question[160];
    snprintf(question, sizeof question, "Delete \"%s\"? Press Delete again.", save->name);
    snprintf(s->pendingFolder, sizeof s->pendingFolder, "%s", save->folder);
    if (ask_first(w, K_DELETESAVE, save->folder, question, ANSWER_DELETE)) dolls_save_answer(ANSWER_DELETE, 1);
}

static void load_pressed(DollWin *w)
{
    SaveScreen *s = w->save;
    SaveInfo *save = picked_save(s);
    if (!save) { save_error(s->pickedSlot > 0 ? 105 : 104, ""); return; }
    dolls_request_load(save->folder, save->name);
}

/* An answer to the screen's own questions (write over, delete). */
void dolls_save_answer(int token, int yes)
{
    DollWin *w = save_window(NULL);
    if (!w || !yes) return;
    SaveScreen *s = w->save;
    if (token == ANSWER_SAVE_OVER) write_save(w, s->pendingSlot, s->pendingName, s->pendingFolder);
    else if (token == ANSWER_DELETE)
    {
        char name[64] = "";
        for (int i = 0; i < s->count; i++) if (!strcmp(s->saves[i].folder, s->pendingFolder)) snprintf(name, sizeof name, "%s", s->saves[i].name);
        save_delete(s->pendingFolder);
        refresh_saves(s);
        if (s->pickedSlot > 0) pick_slot(s, s->pickedSlot); /* the slot stays picked, empty now */
        else { if (s->picked >= s->count) s->picked = s->count - 1; SaveInfo *p = picked_save(s); snprintf(s->name, sizeof s->name, "%s", p ? p->name : ""); }
        char line[120];
        snprintf(line, sizeof line, "Deleted \"%s\".", name);
        game_log(line);
    }
}

/* A click on one of the screen's own parts. 1 = handled. */
static int save_part_clicked(DollWin *w, int i, int dy, int ph)
{
    cJSON *part = part_at(w->design, i);
    int k = kind_of(part);
    if (k < K_SAVELIST || k > K_DELETESAVE) return 0;
    if (!w->save) { char line[160]; snprintf(line, sizeof line, "\"%s\" works on the Save/Load screen (System Settings > Save / Load).", str(part, "Name")); game_log(line); return 1; }
    SaveScreen *s = w->save;
    PartState *r = &w->state->parts[i];
    switch (k)
    {
        case K_SAVELIST:
        {
            int row = s->scroll + dy / row_height(part);
            (void)ph;
            if (row >= 0 && row < s->count) pick_row(w, row);
            return 1;
        }
        case K_SAVENAME:
        {
            int slot = slot_number(w->design, i);
            if (s->pickedSlot != slot) pick_slot(s, slot);
            SaveInfo *save = in_slot(s, slot);
            char line[160];
            if (save) snprintf(line, sizeof line, "Slot %d: \"%s\" - Load it, or type a new name and Save over it.", slot, save->name);
            else snprintf(line, sizeof line, "Slot %d is empty - type a name and press Save.", slot);
            game_log(line);
            return 1;
        }
        case K_SAVEBUTTON: r->look = 1; w->pressPart = i; w->pressMs = PRESS_MS; w->pressAction = 0; fire(w->state, i, TR_USE, 0, 0, -1); save_pressed(w); return 1;
        case K_LOADBUTTON: r->look = 1; w->pressPart = i; w->pressMs = PRESS_MS; w->pressAction = K_LOADBUTTON; return 1; /* its Pressed look shows first, then the game loads */
        case K_DELETESAVE: r->look = 1; w->pressPart = i; w->pressMs = PRESS_MS; w->pressAction = 0; fire(w->state, i, TR_USE, 0, 0, -1); delete_pressed(w); return 1;
    }
    return 1;
}

/* Keys on the Save/Load screen: the name typed, Backspace, Enter saves, Up / Down pick. 1 = taken. */
static int save_key(int key, int ch)
{
    DollWin *w = save_window(NULL);
    if (!w || dolls_modal_open()) return 0;
    SaveScreen *s = w->save;
    if (ch)
    {
        if (s->pickedSlot == 0 && slot_count(w->design) > 0 && !has_kind(w->design, K_SAVELIST)) return 1; /* no slot picked: the letters go nowhere (Exult) */
        size_t n = strlen(s->name);
        if (n < MAX_SAVE_NAME && !strchr("\\/:*?\"<>|", ch)) { s->name[n] = (char)ch; s->name[n + 1] = 0; }
        s->confirmKind = 0;
        return 1;
    }
    switch (key)
    {
        case KEY_BACKSPACE: { size_t n = strlen(s->name); if (n) s->name[n - 1] = 0; s->confirmKind = 0; return 1; }
        case KEY_ENTER: save_pressed(w); return 1;
        case KEY_UP: case KEY_DOWN:
        {
            int step = key == KEY_UP ? -1 : 1, slots = slot_count(w->design);
            if (slots > 0 && (s->pickedSlot > 0 || !has_kind(w->design, K_SAVELIST)))
            {
                int numbers[64], n = 0, at = -1;
                for (int i = 0; i < w->state->partCount && n < 64; i++) if (kind_of(part_at(w->design, i)) == K_SAVENAME) numbers[n++] = slot_number(w->design, i);
                for (int i = 0; i < n; i++) if (numbers[i] == s->pickedSlot) at = i;
                at = at < 0 ? 0 : at + step < 0 ? 0 : at + step >= n ? n - 1 : at + step;
                pick_slot(s, numbers[at]);
                return 1;
            }
            if (!s->count) return 1;
            int row = s->picked + step;
            pick_row(w, row < 0 ? 0 : row >= s->count ? s->count - 1 : row);
            if (s->picked < s->scroll) s->scroll = s->picked;
            return 1;
        }
        case KEY_S: case KEY_C: case KEY_I: case KEY_SPACE: case KEY_1: case KEY_2: case KEY_3: case KEY_4: case KEY_5: case KEY_6: case KEY_7: case KEY_8: case KEY_9: case KEY_OTHER:
            return 1; /* letters arrive as text */
    }
    return 0;
}

/* The mouse wheel over the Save list: it scrolls. */
void dolls_save_scroll(int step) { DollWin *w = save_window(NULL); if (w) w->save->scroll += step; }

/* A new game: every paperdoll window closed (no answers heard), forms as designed, the designs read again. */
void dolls_reset(void)
{
    while (winCount) { DollWin *w = wins[--winCount]; if (w->save) { picture_free(w->save->shot); free(w->save); } free(w->canvas); free(w); }
    for (int i = 0; i < formCount; i++) dolls_free_state(forms[i]);
    formCount = 0; slideCount = 0;
    designs = NULL;
}

int dolls_design_exists(int id) { return dolls_design(id) != NULL; }
