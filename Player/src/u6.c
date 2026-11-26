/*
 * The Ultima 6 engine's screen (Editor/U6/U6GameWindow.U6Hud.cs): the HUD Editor's frame and pieces over the world, which is
 * seen through the Game View only. Drawn as the Windows engine layers it: the frame behind (covering everything outside the
 * Game View), then the pieces in the HUD's list order, then the panel's contents for the shown member, then the pictures
 * ticked In front.
 */
#include "u6.h"
#include "platform.h"
#include "blend.h"
#include "font.h"
#include "pictures.h"
#include "world.h"
#include "items.h"
#include "events.h"
#include "gumps.h"
#include "use.h"
#include "combat.h"
#include "vitals.h"
#include "traps.h"
#include "dialog.h"
#include "books.h"
#include "sound.h"
#include "magic.h"
#include "schedules.h"
#include "script.h"
#include "commands.h"
#include "save.h"
#include "video.h"
#include "../third_party/cJSON.h"
#include <ctype.h>
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* the verbs' part (below): the keys read once, the cursors drawn with the HUD, who the panel shows */
static void load_keys(void);
static int heldCount; /* the walking keys held (below) */
static int controlled, soloMode; /* solo mode (below): the actor the keys move (0 the avatar) */
static int controlled_actor(void);
static void start_solo(int index);
static void end_solo(void);
static void combat_over_take_avatar(void);
static void take_control(int actor);
static void follow_all(int on_);
static void draw_cursors(int over, uint32_t ms);
static int member_shown(void);
static int party_list(int *ids, int max);
static int looked = -1, partyView;
/* talking (below): who - their face and name in the panel while the talk goes on */
static int talking; static const Sprite *talkerFace; static char talkerName[64];
static int u6_talk_more_waiting(void);
static void talk_tick(uint32_t ms);
static int carried_items(int member, Item **out, int max);
static void carried_grid(cJSON *piece, int member, int *columns, int *rows, int *cell, cJSON **empty);
static void u6_reset_play(void);
static void (*askAnswer)(char letter); /* a key asked for (below) */
static uint32_t *hudCache, hudSig; static int hudCacheable = -1; /* the HUD kept drawn (u6_draw, at the end) */
static const Sprite *frameOverride; /* Change HUD Frame: the frame in play in place of the HUD Editor's (NULL: its own) */
/* Conversation Style (U6GameWindow.U6TalkStyle): NULL - Ultima 6's; the words' own box while a talk goes on */
static cJSON *talkStyle; static int talkBoxOpen, replyPick;
static char boxLines[40][192]; static int boxLineCount;
static const char *style(const char *key);
static int words_in_box(void);
static int typing_replies(void);
static int picking_replies(void);
static int reply_list_lines(char out[][192], int max);
static void draw_talk_box(uint32_t ms);
static void close_talk_box(void);
static int scene_key(const PlatformEvent *e);
static int scene_click(int x, int y);
static void end_scene(void);
static int title_key(const PlatformEvent *e);
static int title_click(int x, int y);
static void quick_save_or_load(int save);
static void quick_save_or_load_now(int save);
static void outline(int x, int y, int w, int h, uint32_t c);
static char *wrapped_text(const char *text, int width, const char *fontName);
static cJSON *u6settings(void);
static int input_key(const PlatformEvent *e);
static int input_text(int ch);
static int input_click(void);
/* combat turn by turn (below) */
static double turnRunMs; static int turnIsAttack, turnStepFromX = -99999, turnStepFromY;
static void pass_turn_if_fighting(int attack);
static int turn_by_turn(void);
static const char *mode_word(int characterId);
static int combat_mode_clicked(int x, int y);
static void init_modes(void);
static int turn_step(int dx, int dy);
static int turn_pass(void);
static void rest(void);
/* casting (below) */
static int casting, aimCaster = -1; static cJSON *aimSpell;
static const char *word(const char *id);
const char *u6_word(const char *id) { return word(id); }

static void start_cast(void);
static int cast_key(const PlatformEvent *e);
static int draw_cast_panel(cJSON *panel);
static void cast_on_cell(int cx, int cy, int actor, int uid, const char *name);

enum { P_PICTURE, P_GAMEVIEW, P_TEXTAREA, P_PANEL, P_BODYSLOT, P_CARRIED, P_PORTRAIT, P_STATS, P_MEMBERNAME, P_COMMAND, P_CUSTOM,
       P_COMPASS, P_SUNMOON, P_DATE, P_COMBATMODE, P_LABEL }; /* HudPieceKind, in its order (numbers in game.json) */
enum { VIEW_ALWAYS, VIEW_INVENTORY, VIEW_PORTRAIT }; /* HudPanelView */
enum { TR_USE = 2 };                                  /* BookTriggerConditionBased.ByUseItem */
static const char *VERBS[] = { "Attack", "Cast", "Talk", "Look", "Get", "Drop", "Move", "Use", "Rest", "Combat" }; /* HudVerb */

static int on, hasHud;
static cJSON *hud, *viewPiece, *textPiece;
static int screenW = BASE_W, screenH = BASE_H, vx, vy, vw = BASE_W, vh = BASE_H; /* Ultima 6 is never widened (Fill the screen) */
static int panelView = VIEW_INVENTORY;
#define MAX_LINES 120
static char lines[MAX_LINES][192]; static int lineCount;
static char prompt[96];

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int d) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : d; }
static const char *str(cJSON *o, const char *k) { cJSON *v = get(o, k); return cJSON_IsString(v) ? v->valuestring : ""; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static cJSON *project(void) { return get((cJSON *)world_json(), "Project"); }
static const char *style(const char *key) { return talkStyle ? str(talkStyle, key) : ""; }
static int words_in_box(void) { return talkStyle && !strcmp(style("Words"), "In a box of their own (below)") && talkBoxOpen; }
static int typing_replies(void) { return !talkStyle || strcmp(style("Answers"), "Picked from a list (arrows, 1-9, a click)"); }

int u6_on(void) { return on; }
int u6_hud(void) { return hasHud; }
int u6_in_view(int x, int y) { return !hasHud || (x >= vx && y >= vy && x < vx + vw && y < vy + vh); }

static cJSON *first(int kind)
{
    cJSON *p;
    cJSON_ArrayForEach(p, get(hud, "Pieces")) if (num(p, "Kind", -1) == kind) return p;
    return NULL;
}

/* ---- The HUD in play (U6GameWindow.HudPanels.cs): pieces shown / hidden, moved, another picture, a Label's words; custom panels
   (every Panel after the first) closed unless StartsOpen, opened and closed by their key or Show / Hide HUD Panel, their key
   switched off and on; a piece In a custom panel only while it is open. Kept in saves (U6HudPieces). ---- */
typedef struct HudState { int id, shown, keyOff, hasX, x, hasY, y; char group[64], name[64]; char *text; cJSON *now; } HudState;
#define HUD_STATES 64
static HudState hudStates[HUD_STATES]; static int hudStateCount, hudVersion;

static HudState *hud_state(int id, int make)
{
    for (int i = 0; i < hudStateCount; i++) if (hudStates[i].id == id) return &hudStates[i];
    if (!make || hudStateCount == HUD_STATES) return NULL;
    HudState *s = &hudStates[hudStateCount++];
    memset(s, 0, sizeof *s); s->id = id; s->shown = -1;
    return s;
}
static void hud_states_clear(void)
{
    for (int i = 0; i < hudStateCount; i++) { free(hudStates[i].text); cJSON_Delete(hudStates[i].now); }
    hudStateCount = 0; hudVersion++;
}
static cJSON *piece_by_id(int id) { cJSON *p; cJSON_ArrayForEach(p, get(hud, "Pieces")) if (num(p, "Id", -1) == id) return p; return NULL; }
static int is_custom_panel(cJSON *p) { return num(p, "Kind", -1) == P_PANEL && p != first(P_PANEL); }
static int own_shown(cJSON *p) { HudState *s = hud_state(num(p, "Id", -1), 0); return s && s->shown >= 0 ? s->shown : !is_custom_panel(p) || flag(p, "StartsOpen"); }
static int piece_shown(cJSON *p)
{
    if (!own_shown(p)) return 0;
    int pid = num(p, "PanelId", 0);
    if (pid > 0 && num(p, "Kind", -1) != P_PANEL) { cJSON *panel = piece_by_id(pid); return panel && is_custom_panel(panel) && own_shown(panel); }
    return 1;
}
static cJSON *piece_now(cJSON *p) { HudState *s = hud_state(num(p, "Id", -1), 0); return s && s->now ? s->now : p; }
/* The changed copy made again (the HUD Editor's piece stays as made). */
static void hud_state_rebuild(HudState *s)
{
    cJSON_Delete(s->now); s->now = NULL; hudVersion++;
    cJSON *p = piece_by_id(s->id);
    if (!p || !(s->hasX || s->hasY || s->name[0] || s->text)) return;
    s->now = cJSON_Duplicate(p, 1);
    if (s->hasX) { cJSON_DeleteItemFromObject(s->now, "X"); cJSON_AddNumberToObject(s->now, "X", s->x); }
    if (s->hasY) { cJSON_DeleteItemFromObject(s->now, "Y"); cJSON_AddNumberToObject(s->now, "Y", s->y); }
    if (s->name[0]) { cJSON_DeleteItemFromObject(s->now, "Sprite"); cJSON *l = cJSON_AddObjectToObject(s->now, "Sprite"); cJSON_AddStringToObject(l, "SpriteGroup", s->group); cJSON_AddStringToObject(l, "SpriteName", s->name); }
    if (s->text) { cJSON_DeleteItemFromObject(s->now, "Text"); cJSON_AddStringToObject(s->now, "Text", s->text); }
}
static cJSON *piece_named(const char *name)
{
    char want[96]; size_t k = 0;
    while (*name == ' ') name++;
    for (; *name && k < sizeof want - 1; name++) want[k++] = (char)tolower((unsigned char)*name);
    while (k && want[k - 1] == ' ') k--;
    want[k] = 0;
    cJSON *p;
    cJSON_ArrayForEach(p, get(hud, "Pieces"))
    {
        const char *n = str(p, "Name"); char have[96]; size_t j = 0;
        while (*n == ' ') n++;
        for (; *n && j < sizeof have - 1; n++) have[j++] = (char)tolower((unsigned char)*n);
        while (j && have[j - 1] == ' ') j--;
        have[j] = 0;
        if (!strcmp(have, want)) return p;
    }
    return NULL;
}

void u6_load(void)
{
    cJSON *p = project();
    on = !strcmp(str(get(p, "Manifest"), "Style"), "Ultima6");
    if (!on) return;
    hud = get(get(get(p, "Settings"), "Ultima6"), "Hud");
    hud_states_clear();
    viewPiece = first(P_GAMEVIEW);
    textPiece = first(P_TEXTAREA);
    hasHud = viewPiece != NULL;
    if (hasHud)
    {
        /* U6GameWindow.InitU6Hud: the screen the HUD's size (this player's screen is 320 x 200), the world in the Game View */
        screenW = num(hud, "Width", 320); screenH = num(hud, "Height", 200);
        vx = num(viewPiece, "X", 0); vy = num(viewPiece, "Y", 0);
        vw = num(viewPiece, "Width", 16); vh = num(viewPiece, "Height", 16);
        if (vw < 16) vw = 16;
        if (vh < 16) vh = 16;
    }
    world_set_view(vx, vy, vw, vh); /* no HUD yet: the whole screen, the avatar in the middle (no dead zone in Ultima 6) */
    load_keys();
    platform_game_takes_function_keys(hasHud); /* F9 a member, F10 the party - the Amiga's own uses of them give way */
    u6_reset_play();
    hudCacheable = -1; hudSig = 0; /* a new HUD: composed afresh */
    u6_init_levels();
    init_modes();
}

/* ---------------- Drawing helpers ---------------- */

static uint32_t *screen;

static const Sprite *sprite_of(cJSON *link)
{
    const char *name = str(link, "SpriteName");
    return name[0] ? sprite_find(str(link, "SpriteGroup"), name) : NULL;
}

/* A picture stretched over a rectangle (Stretch.Fill), blended by its alpha; nothing drawn inside the hole (the Game View). */
static void blit_fill(const Picture *p, int x, int y, int w, int h, int hole)
{
    if (!p || p->width <= 0 || p->height <= 0 || w <= 0 || h <= 0) return;
    x *= frame_scale; y *= frame_scale; w *= frame_scale; h *= frame_scale;
    int hx0 = vx * frame_scale, hy0 = vy * frame_scale, hx1 = (vx + vw) * frame_scale, hy1 = (vy + vh) * frame_scale;
    int i0 = x < 0 ? -x : 0, i1 = x + w > FRAME_W ? FRAME_W - x : w;
    int j0 = y < 0 ? -y : 0, j1 = y + h > FRAME_H ? FRAME_H - y : h;
    if (i0 >= i1 || j0 >= j1) return;
    uint32_t stepX = ((uint32_t)p->width << 16) / (uint32_t)w, stepY = ((uint32_t)p->height << 16) / (uint32_t)h;
    for (int j = j0; j < j1; j++)
    {
        int sy = y + j;
        int holeRow = hole && sy >= hy0 && sy < hy1;
        const uint32_t *row = p->pixels + (size_t)(((uint32_t)j * stepY) >> 16) * (size_t)p->width;
        uint32_t *d = &screen[sy * FRAME_W + x];
        uint32_t at = (uint32_t)i0 * stepX;
        for (int i = i0; i < i1; i++, at += stepX)
        {
            if (holeRow && x + i >= hx0 && x + i < hx1) continue;
            uint32_t c = row[at >> 16], a = c >> 24;
            if (a == 0) continue;
            d[i] = a == 255 ? c : blend_px(d[i], c, a);
        }
    }
}

static void fill_rect(int x, int y, int w, int h, uint32_t c) { frame_fill(screen, x, y, w, h, c); }

static uint32_t colour_of(const char *hex)
{
    if (!hex || hex[0] != '#' || strlen(hex) < 7) return 0;
    return 0xFF000000u | ((uint32_t)strtoul(hex + 1, NULL, 16) & 0xFFFFFFu);
}

static Font *font_of(cJSON *piece) { const char *n = str(piece, "FontName"); return font_get(n[0] ? n : "InGameFont"); }
static int line_height(Font *f) { int h = font_line_height(f) + 1; return h < 6 ? 6 : h; }

/* Lines of words in a piece's rectangle, its font and colour, cut at its bottom. */
static void words_at(cJSON *piece, const char *text)
{
    Font *f = font_of(piece);
    int x = num(piece, "X", 0), y = num(piece, "Y", 0), h = num(piece, "Height", 16), lh = line_height(f);
    uint32_t c = colour_of(str(piece, "TextColor"));
    char line[192];
    for (const char *t = text; *t && y + lh <= num(piece, "Y", 0) + h + lh - 1; )
    {
        size_t n = strcspn(t, "\n");
        snprintf(line, sizeof line, "%.*s", (int)n, t);
        font_draw(f, screen, x, y, line, c);
        y += lh; t += n; if (*t == '\n') t++;
    }
}

/* ---------------- The panel: one member, inventory or portrait ---------------- */

static cJSON *character_json(int id)
{
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Characters"), "Characters")) if (num(c, "Id", -2) == id) return c;
    return NULL;
}

static const Sprite *item_sprite(const Item *it)
{
    int lit, spent;
    items_light(it, &lit, &spent);
    return world_item_look(items_category(it), items_item_id(it), lit, spent, items_is_locked(it), items_quantity(it), items_liquid(it, -1));
}

static void draw_sprite_fit(const Sprite *s, int x, int y, int w, int h, uint32_t ms) { if (s) world_blit(screen, sprite_picture_at(s, ms), x, y, w, h); }

static void draw_panel_piece(cJSON *piece, int kind, int member, cJSON *mj, uint32_t ms)
{
    int x = num(piece, "X", 0), y = num(piece, "Y", 0), w = num(piece, "Width", 16), h = num(piece, "Height", 16);
    switch (kind)
    {
        case P_MEMBERNAME: words_at(piece, str(mj, "Name")); break;
        case P_COMBATMODE: words_at(piece, mode_word(member)); break;
        case P_PORTRAIT:
            if (str(mj, "FaceSpriteName")[0]) draw_sprite_fit(sprite_find(str(mj, "FaceSpriteGroup"), str(mj, "FaceSpriteName")), x, y, w, h, ms);
            break;
        case P_STATS:
        {
            /* "Name value" a line: the stats picked (none: every stat), in the Stat Editor's order */
            char text[1024] = "";
            cJSON *s, *ids = get(piece, "StatIds");
            int any = cJSON_GetArraySize(ids) > 0;
            cJSON_ArrayForEach(s, get(get(project(), "Stats"), "Stats"))
            {
                int id = num(s, "Id", -1), picked = !any;
                cJSON *i;
                if (any) cJSON_ArrayForEach(i, ids) if (cJSON_IsNumber(i) && i->valueint == id) picked = 1;
                if (!picked) continue;
                char key[16]; snprintf(key, sizeof key, "%d", id);
                size_t at = strlen(text);
                snprintf(text + at, sizeof text - at, "%s%s %s", at ? "\n" : "", str(s, "Name"), str(get(mj, "StatValues"), key));
            }
            words_at(piece, text);
            break;
        }
        case P_BODYSLOT:
        {
            const Item *worn = items_in_slot(member, num(piece, "Slot", 0));
            const Sprite *s = worn ? item_sprite(worn) : sprite_of(get(piece, "Sprite"));
            draw_sprite_fit(s, x, y, w, h, ms);
            break;
        }
        case P_CARRIED:
        {
            int columns, rows, cell; cJSON *emptyLink;
            carried_grid(piece, member, &columns, &rows, &cell, &emptyLink);
            const Sprite *empty = sprite_of(emptyLink);
            if (empty) for (int i = 0; i < columns * rows; i++) blit_fill(sprite_picture_at(empty, 0), x + i % columns * cell, y + i / columns * cell, cell, cell, 0);
            Item *carried[96];
            int n = carried_items(member, carried, 96);
            for (int i = 0; i < n && i < columns * rows; i++) draw_sprite_fit(item_sprite(carried[i]), x + i % columns * cell, y + i / columns * cell, cell, cell, ms);
            break;
        }
    }
}

/* ---------------- Bags (U6GameWindow.U6Things) ---------------- */

static Item *openBag; /* the bag open in the panel: one the shown member carries */

static int member_carries(int member, Item *it)
{
    int cid = -1, slot = -1; Item *holder = NULL;
    return it && items_where(it, &cid, &holder, &slot) == 1 && cid == member;
}

/* The panel's carried cells: the open bag (itself first - Use it again to go back), else what's carried loose and the pack's contents. */
static int carried_items(int member, Item **out, int max)
{
    int n = 0;
    if (openBag && member_carries(member, openBag))
    {
        out[n++] = openBag;
        Item **inside; int count = items_contents(openBag, &inside);
        for (int i = 0; i < count && n < max; i++) out[n++] = inside[i];
        return n;
    }
    openBag = NULL;
    n = items_loose(member, out, max);
    Item *pack = items_in_slot(member, 13); /* EquipmentSlot.Backpack */
    if (pack && items_category(pack) == 5)
    {
        Item **inside; int count = items_contents(pack, &inside);
        for (int i = 0; i < count && n < max; i++) out[n++] = inside[i];
    }
    return n;
}

static cJSON *container_json(int id)
{
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Containers"), "Containers")) if (num(c, "Id", -2) == id) return c;
    return NULL;
}

/* The Carried Items grid as shown now: an open bag's own (Container Editor: Across, Down, Cell size, empty cell) at the HUD's
   Carried Items X and Y - else the HUD's own. */
static void carried_grid(cJSON *piece, int member, int *columns, int *rows, int *cell, cJSON **empty)
{
    *columns = num(piece, "Columns", 4); *rows = num(piece, "Rows", 3); *cell = num(piece, "CellSize", 16); *empty = get(piece, "Sprite");
    if (openBag && member_carries(member, openBag))
    {
        cJSON *d = container_json(items_item_id(openBag));
        if (num(d, "U6Columns", 0) > 0 && num(d, "U6Rows", 0) > 0)
        {
            *columns = num(d, "U6Columns", 0); *rows = num(d, "U6Rows", 0);
            if (num(d, "U6CellSize", 0) > 0) *cell = num(d, "U6CellSize", 0);
            if (str(get(d, "U6EmptyCell"), "SpriteName")[0]) *empty = get(d, "U6EmptyCell");
        }
    }
    if (*cell < 4) *cell = 4;
}

static int view_of(int kind) { return kind == P_BODYSLOT || kind == P_CARRIED ? VIEW_INVENTORY : kind == P_PORTRAIT || kind == P_STATS ? VIEW_PORTRAIT : VIEW_ALWAYS; }
static int is_panel_content(int kind) { return kind == P_BODYSLOT || kind == P_CARRIED || kind == P_PORTRAIT || kind == P_STATS || kind == P_MEMBERNAME || kind == P_COMBATMODE; }

/* ---------------- The text area and the date ---------------- */

static const char *WIND[] = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };

/* U6GameWindow.HudWind: turns now and then through the day; "C" while calm. */
static int wind_index(void)
{
    int hourOfGame = events_day() * 24 + events_hour();
    uint32_t h = (uint32_t)(hourOfGame / 3) * 2654435761u;
    int roll = (int)(h >> 24) % 10;
    return roll == 0 ? -1 : roll % 8;
}

static void add_line(const char *text, int length)
{
    if (lineCount == MAX_LINES) { memmove(lines[0], lines[1], sizeof lines[0] * (MAX_LINES - 1)); lineCount--; }
    snprintf(lines[lineCount++], sizeof lines[0], "%.*s", length, text);
}

/* Case-blind comparisons (C99 has none). */
static int lower_c(int c) { return c >= 'A' && c <= 'Z' ? c + 32 : c; }
static int same_n(const char *a, const char *b, size_t n) { for (size_t i = 0; i < n; i++) { if (lower_c((unsigned char)a[i]) != lower_c((unsigned char)b[i])) return 1; if (!a[i]) return 0; } return 0; }
static int same_word(const char *a, const char *b) { while (*a && *b && lower_c((unsigned char)*a) == lower_c((unsigned char)*b)) { a++; b++; } return *a || *b; }

/* ---- Colours in the text area (U6GameWindow.TextColours.cs): [COLOR=name]...[/COLOR] or [COLOR=#RRGGBB] - marks \3 colour \5 ...
   \4 that take no room; a line wrapped inside a colour carries it on to the next. ---- */
static void colour_marks(const char *in, char *out, size_t size)
{
    size_t k = 0;
    while (*in && k + 1 < size)
    {
        if (*in == '[' && (in[1] == 'C' || in[1] == 'c') && !same_n(in, "[COLOR=", 7) && strchr(in + 7, ']'))
        {
            const char *end = strchr(in + 7, ']');
            if (k + (size_t)(end - in) + 2 >= size) break;
            out[k++] = '\3';
            for (const char *c = in + 7; c < end; c++) out[k++] = *c;
            out[k++] = '\5';
            in = end + 1;
            continue;
        }
        if (*in == '[' && !same_n(in, "[/COLOR]", 8)) { out[k++] = '\4'; in += 8; continue; }
        out[k++] = *in++;
    }
    out[k] = 0;
}

static char colourOpen[40]; /* the colour a wrapped line ended in ("": none) */
/* A wrapped line: the colour still open from the last opened again, closed at its end if still open. */
static void colour_line(const char *in, char *out, size_t size)
{
    char open[40]; snprintf(open, sizeof open, "%s", colourOpen);
    if (open[0]) snprintf(out, size, "\3%s\5%s", open, in); else snprintf(out, size, "%s", in);
    for (const char *p = in; *p; p++)
    {
        if (*p == '\3') { const char *e = strchr(p, '\5'); size_t n = e ? (size_t)(e - p - 1) : 0; if (n >= sizeof colourOpen) n = sizeof colourOpen - 1; memcpy(colourOpen, p + 1, n); colourOpen[n] = 0; }
        else if (*p == '\4') colourOpen[0] = 0;
    }
    if (colourOpen[0]) { size_t l = strlen(out); if (l + 1 < size) { out[l] = '\4'; out[l + 1] = 0; } }
}

/* A colour's name (or #RRGGBB) as a colour; 0: not one. */
static uint32_t named_colour(const char *name)
{
    static const struct { const char *n; uint32_t c; } NAMES[] = {
        { "red", 0xFF0000 }, { "green", 0x008000 }, { "lime", 0x00FF00 }, { "blue", 0x0000FF }, { "yellow", 0xFFFF00 }, { "white", 0xFFFFFF },
        { "black", 0x000000 }, { "gold", 0xFFD700 }, { "orange", 0xFFA500 }, { "purple", 0x800080 }, { "gray", 0x808080 }, { "grey", 0x808080 },
        { "cyan", 0x00FFFF }, { "aqua", 0x00FFFF }, { "magenta", 0xFF00FF }, { "fuchsia", 0xFF00FF }, { "brown", 0xA52A2A }, { "pink", 0xFFC0CB },
        { "silver", 0xC0C0C0 }, { "maroon", 0x800000 }, { "navy", 0x000080 }, { "olive", 0x808000 }, { "teal", 0x008080 }, { "violet", 0xEE82EE } };
    while (*name == ' ') name++;
    if (*name == '#') return colour_of(name);
    for (size_t i = 0; i < sizeof NAMES / sizeof NAMES[0]; i++) if (!same_word(name, NAMES[i].n)) return 0xFF000000u | NAMES[i].c;
    return 0;
}

/* The width words take on the screen - a keyword's marks (\1 \2) and colour marks take none. */
static int words_width(Font *f, const char *text)
{
    char plain[400]; size_t n = 0;
    for (const char *p = text; *p && n < sizeof plain - 1; p++)
    {
        if (*p == '\3') { const char *e = strchr(p, '\5'); if (e) { p = e; continue; } }
        if (*p != '\1' && *p != '\2' && *p != '\4' && *p != '\5') plain[n++] = *p;
    }
    plain[n] = 0;
    return n ? font_text_width(f, plain) : 0;
}

/* U6GameWindow.HudWrap: words wrapped to the width, word by word (a word longer than the line stays whole on its own line).
   Each line out through add(line); how many. */
static int wrap(Font *f, const char *text, int width, void (*add)(const char *line))
{
    char line[400] = "", candidate[400];
    int count = 0;
    const char *p = text;
    for (;;)
    {
        size_t n = strcspn(p, " ");
        char wordText[300]; snprintf(wordText, sizeof wordText, "%.*s", (int)n, p);
        snprintf(candidate, sizeof candidate, "%s%s%s", line, line[0] ? " " : "", wordText);
        if (!line[0] || words_width(f, candidate) <= width) snprintf(line, sizeof line, "%s", candidate);
        else { add(line); count++; snprintf(line, sizeof line, "%s", wordText); }
        p += n;
        if (!*p) break;
        p++;
    }
    add(line); count++;
    return count;
}

static void add_whole(const char *line) { add_line(line, (int)strlen(line)); }
static void add_coloured(const char *line) { char c[400]; colour_line(line, c, sizeof c); add_line(c, (int)strlen(c)); }

/* The conversation's own box (Conversation Style: the words in a box): where its words go, its font - the face beside them when it goes there. */
static int face_room(void);
static void box_words_rect(int *x, int *y, int *w, int *h)
{
    int room = face_room();
    *x = num(talkStyle, "BoxX", 8) + 4 + room; *y = num(talkStyle, "BoxY", 120) + 4;
    *w = num(talkStyle, "BoxWidth", 304) - 8 - room; if (*w < 16) *w = 16;
    *h = num(talkStyle, "BoxHeight", 72) - 8; if (*h < 8) *h = 8;
}
static Font *box_font(void) { return font_get(style("Font")[0] ? style("Font") : "InGameFont"); }
static void add_box_line(const char *line)
{
    if (boxLineCount == 40) { memmove(boxLines[0], boxLines[1], sizeof boxLines[0] * 39); boxLineCount--; }
    snprintf(boxLines[boxLineCount++], sizeof boxLines[0], "%s", line);
}

int u6_say(const char *text)
{
    if (!textPiece) return 0;
    if (words_in_box())
    {
        int x, y, w, h; box_words_rect(&x, &y, &w, &h);
        for (const char *p = text; *p; )
        {
            size_t n = strcspn(p, "\n");
            char para[600]; snprintf(para, sizeof para, "%.*s", (int)n, p);
            wrap(box_font(), para, w, add_box_line);
            p += n; if (*p == '\n') p++;
        }
        return 1;
    }
    Font *f = font_of(textPiece);
    int width = num(textPiece, "Width", 16);
    const char *start = text;
    while (*start)
    {
        /* each paragraph wrapped to the area's width, word by word (U6GameWindow.HudWrap) */
        size_t n = strcspn(start, "\n");
        char para[600];
        snprintf(para, sizeof para, "%.*s", (int)n, start);
        size_t k = 0; for (size_t i = 0; para[i]; i++) if (para[i] != '\r') para[k++] = para[i];
        para[k] = 0;
        char marked[600]; colour_marks(para, marked, sizeof marked); /* [COLOR=...] */
        colourOpen[0] = 0;
        wrap(f, marked, width, add_coloured);
        start += n; if (*start == '\n') start++;
    }
    int keep = num(textPiece, "Height", 16) / line_height(f) * 4; /* a few screens of history */
    if (keep < 1) keep = 1;
    if (lineCount > keep) { memmove(lines[0], lines[lineCount - keep], sizeof lines[0] * (size_t)keep); lineCount = keep; }
    return 1;
}

void u6_prompt(const char *text) { snprintf(prompt, sizeof prompt, "%s", text ? text : ""); }

/* The keywords' colour: System Settings > Ultima 6 (Conversation Style's own comes with step 2's style command). */
static uint32_t keyword_colour(void)
{
    if (style("KeywordColor")[0]) { char hex[16]; snprintf(hex, sizeof hex, "%s%s", style("KeywordColor")[0] == '#' ? "" : "#", style("KeywordColor")); if (colour_of(hex)) return colour_of(hex); }
    uint32_t c = colour_of(str(get(get(project(), "Settings"), "Ultima6"), "KeywordColor"));
    return c ? c : 0xFFFFD800u;
}

/* A line with keywords (between \1 and \2) drawn in the keyword colour, words in a colour of their own (\3 colour \5 ... \4)
   in theirs, the rest in the piece's. */
static void draw_marked(Font *f, int x, int y, const char *line, uint32_t colour)
{
    if (!strchr(line, '\1') && !strchr(line, '\3')) { font_draw(f, screen, x, y, line, colour); return; }
    uint32_t key = keyword_colour(), own = 0;
    int inKey = 0;
    char part[300];
    const char *p = line;
    while (*p)
    {
        size_t n = strcspn(p, "\1\2\3\4");
        if (n)
        {
            snprintf(part, sizeof part, "%.*s", (int)n, p);
            font_draw(f, screen, x, y, part, inKey ? key : own ? own : colour);
            x += font_text_width(f, part);
        }
        p += n;
        if (*p == '\1') { inKey = 1; p++; }
        else if (*p == '\2') { inKey = 0; p++; }
        else if (*p == '\3')
        {
            const char *e = strchr(p, '\5');
            char name[40]; size_t m = e ? (size_t)(e - p - 1) : 0; if (m >= sizeof name) m = sizeof name - 1;
            memcpy(name, p + 1, m); name[m] = 0;
            own = named_colour(name);
            p = e ? e + 1 : p + 1;
        }
        else if (*p == '\4') { own = 0; p++; }
    }
}

static void draw_text_area(cJSON *piece, uint32_t ms)
{
    Font *f = font_of(piece);
    int x = num(piece, "X", 0), y = num(piece, "Y", 0), w = num(piece, "Width", 16), h = num(piece, "Height", 16), lh = line_height(f);
    uint32_t c = colour_of(str(piece, "TextColor"));
    const Sprite *cursorSprite = sprite_of(get(piece, "Sprite"));
    int cursor = prompt[0] != 0 || u6_talk_more_waiting();
    /* the lines, then the line being typed (with "_" when the area has no cursor sprite); the last that fit */
    const char *shown[MAX_LINES + 40]; char typed[4][192]; static char listed[32][192]; int count = 0;
    for (int i = 0; i < lineCount; i++) shown[count++] = lines[i];
    if (words_in_box()) cursor = 0; /* the conversation's box has the prompt */
    else { int n = reply_list_lines(listed, 32); for (int i = 0; i < n; i++) shown[count++] = listed[i]; } /* picking from a list: the replies */
    if (cursor)
    {
        char t[200]; snprintf(t, sizeof t, "%s%s", prompt, cursorSprite ? "" : "_");
        const char *starts[4]; int lengths[4];
        int n = font_wrap(f, t, w, 4, starts, lengths);
        for (int i = 0; i < n; i++) { snprintf(typed[i], sizeof typed[i], "%.*s", lengths[i], starts[i]); shown[count++] = typed[i]; }
    }
    int fit = h / lh; if (fit < 1) fit = 1;
    int from = count > fit ? count - fit : 0, line = 0;
    for (int i = from; i < count; i++, line++) draw_marked(f, x, y + line * lh, shown[i], c);
    /* the cursor sprite after the last words: animated if it has frames, else blinking every half second */
    if (cursor && cursorSprite && count > 0 && (cursorSprite->frameCount > 1 || ms / 500 % 2 == 0))
    {
        const Picture *p = sprite_picture_at(cursorSprite, ms);
        int lastW = words_width(f, shown[count - 1]);
        if (p) blit_fill(p, x + lastW + (shown[count - 1][0] ? 1 : 0), y + (line - 1) * lh, p->width, p->height, 0);
    }
}

static void draw_date(cJSON *piece)
{
    int day = events_day(), w = wind_index();
    char text[64];
    snprintf(text, sizeof text, "%d-%d-%04d Wind:%s", day / 28 % 12 + 1, day % 28 + 1, 161 + day / 336, w < 0 ? "C" : WIND[w]); /* Britannia: 12 months of 28 days, from 161 */
    words_at(piece, text);
}

static void draw_dial(cJSON *piece, int kind)
{
    const Sprite *s = sprite_of(get(piece, "Sprite"));
    if (!s || s->frameCount <= 0) return;
    int frames = s->frameCount, frame;
    if (kind == P_COMPASS) { int w = wind_index(); frame = w < 0 ? 0 : w * frames / 8; }
    else frame = (events_hour() * 60 + events_minute()) * frames / (24 * 60);
    if (frame < 0) frame = 0;
    if (frame >= frames) frame = frames - 1;
    blit_fill(frame_picture(&s->frames[frame]), num(piece, "X", 0), num(piece, "Y", 0), num(piece, "Width", 16), num(piece, "Height", 16), 0);
}

/* ---------------- The whole HUD ---------------- */

static void hud_compose(uint32_t ms);
static void hud_compose(uint32_t ms)
{
    uint32_t *pixels = screen;
    (void)pixels;
    /* the frame, behind: everything outside the Game View (the world's pixels there are covered) */
    fill_rect(0, 0, SCREEN_W, vy, 0xFF000000); fill_rect(0, vy + vh, SCREEN_W, SCREEN_H - vy - vh, 0xFF000000);
    fill_rect(0, vy, vx, vh, 0xFF000000); fill_rect(vx + vw, vy, SCREEN_W - vx - vw, vh, 0xFF000000);
    const Sprite *frameSprite = frameOverride ? frameOverride : sprite_of(get(hud, "Frame")); /* Change HUD Frame's, else the HUD Editor's */
    if (frameSprite) blit_fill(sprite_picture_at(frameSprite, ms), 0, 0, screenW, screenH, 1);
    cJSON *piece, *raw;
    /* the pieces in the HUD's order (the In front pictures later, over the panel) - shown, as the HUD commands left them */
    cJSON_ArrayForEach(raw, get(hud, "Pieces"))
    {
        if (!piece_shown(raw)) continue;
        piece = piece_now(raw);
        int kind = num(piece, "Kind", -1), x = num(piece, "X", 0), y = num(piece, "Y", 0), w = num(piece, "Width", 16), h = num(piece, "Height", 16);
        switch (kind)
        {
            case P_PICTURE: case P_PANEL:
                if (kind == P_PICTURE && flag(piece, "InFront")) break;
                { const Sprite *s = sprite_of(get(piece, "Sprite")); if (s) blit_fill(sprite_picture_at(s, ms), x, y, w, h, 0); }
                break;
            case P_COMMAND: case P_CUSTOM:
            {
                const Sprite *s = sprite_of(get(piece, "Sprite"));
                if (s) { blit_fill(sprite_picture_at(s, ms), x, y, w, h, 0); break; }
                /* no sprite yet: its verb's first letter (a custom button's name's) in a box, so it can be seen and clicked */
                fill_rect(x, y, w, h, 0xFF808080); fill_rect(x + 1, y + 1, w - 2, h - 2, 0xFF000000);
                int verb = num(piece, "Verb", 0);
                char letter[2] = { kind == P_COMMAND ? VERBS[verb >= 0 && verb < 10 ? verb : 0][0] : str(piece, "Name")[0], 0 };
                if (letter[0]) font_draw(font_of(piece), screen, x + 2, y + 1, letter, 0);
                break;
            }
            case P_COMPASS: case P_SUNMOON: draw_dial(piece, kind); break;
            case P_TEXTAREA: draw_text_area(piece, ms); break;
            case P_DATE: draw_date(piece); break;
            case P_LABEL: words_at(piece, str(piece, "Text")); break;
        }
    }
    /* the panel (RefreshHudPanel): someone looked at - their portrait and name; F10 - the party; else the shown member in the shown view */
    int member = member_shown();
    cJSON *mj = character_json(member), *panel = first(P_PANEL);
    /* (the talk's end: u6_draw, every frame) */
    if (talking && (talkerFace || talkerName[0]) && (!talkStyle || !strcmp(style("Portrait"), "In the HUD's Portrait (Ultima 6)")))
    {
        /* the one talking: their face in the Portrait (else filling the panel) and their name */
        int portrait = 0;
        cJSON_ArrayForEach(piece, get(hud, "Pieces"))
        {
            int kind = num(piece, "Kind", -1);
            if (kind == P_PORTRAIT) { portrait = 1; draw_sprite_fit(talkerFace, num(piece, "X", 0), num(piece, "Y", 0), num(piece, "Width", 16), num(piece, "Height", 16), ms); }
            else if (kind == P_MEMBERNAME) words_at(piece, talkerName);
        }
        if (!portrait && panel) draw_sprite_fit(talkerFace, num(panel, "X", 0), num(panel, "Y", 0), num(panel, "Width", 16), num(panel, "Height", 16), ms);
    }
    else if (looked >= 0 && character_json(looked))
    {
        cJSON *lj = character_json(looked);
        int portrait = 0;
        cJSON_ArrayForEach(piece, get(hud, "Pieces"))
        {
            int kind = num(piece, "Kind", -1);
            if (kind == P_PORTRAIT) { portrait = 1; draw_panel_piece(piece, kind, looked, lj, ms); }
            else if (kind == P_MEMBERNAME) draw_panel_piece(piece, kind, looked, lj, ms);
        }
        if (!portrait && panel && str(lj, "FaceSpriteName")[0])
            draw_sprite_fit(sprite_find(str(lj, "FaceSpriteGroup"), str(lj, "FaceSpriteName")), num(panel, "X", 0), num(panel, "Y", 0), num(panel, "Width", 16), num(panel, "Height", 16), ms);
    }
    else if (casting && panel && draw_cast_panel(panel)) { } /* Cast: the book */
    else if (partyView && panel)
    {
        char text[600] = ""; int ids[16], n = party_list(ids, 16);
        for (int i = 0; i < n; i++) { size_t at = strlen(text); snprintf(text + at, sizeof text - at, "%s%d %s", i ? "\n" : "", i + 1, str(character_json(ids[i]), "Name")); }
        cJSON *p = cJSON_CreateObject(); /* a piece in the panel's place, the plain font */
        cJSON_AddNumberToObject(p, "X", num(panel, "X", 0)); cJSON_AddNumberToObject(p, "Y", num(panel, "Y", 0));
        cJSON_AddNumberToObject(p, "Width", num(panel, "Width", 16)); cJSON_AddNumberToObject(p, "Height", num(panel, "Height", 16));
        words_at(p, text);
        cJSON_Delete(p);
    }
    else if (mj)
        cJSON_ArrayForEach(piece, get(hud, "Pieces"))
        {
            int kind = num(piece, "Kind", -1);
            if (!is_panel_content(kind) || !piece_shown(piece)) continue;
            int v = view_of(kind);
            if (v != VIEW_ALWAYS && v != panelView) continue;
            draw_panel_piece(piece_now(piece), kind, member, mj, ms);
        }
    /* In front pictures, over the panel's pieces */
    cJSON_ArrayForEach(piece, get(hud, "Pieces"))
        if (num(piece, "Kind", -1) == P_PICTURE && flag(piece, "InFront") && piece_shown(piece))
        {
            cJSON *pn = piece_now(piece);
            const Sprite *s = sprite_of(get(pn, "Sprite"));
            if (s) blit_fill(sprite_picture_at(s, ms), num(pn, "X", 0), num(pn, "Y", 0), num(pn, "Width", 16), num(pn, "Height", 16), 0);
        }
    draw_cursors(1, ms); /* the inventory cursor, over the panel */
}

/* ======================= The verbs, the keys, the cursor (U6GameWindow.U6Verbs.cs) ======================= */

/* ---- Ultima 6's words (Engine/U6Keys.cs U6Words), under the maker's (System Settings > Ultima 6 > Words) ---- */
static const char *WORDS[][2] = {
    { "Attack", "Attack" }, { "Cast", "Cast" }, { "Talk", "Talk" }, { "Look", "Look" }, { "Get", "Get" }, { "Drop", "Drop" },
    { "Move", "Move" }, { "Use", "Use" }, { "Rest", "Rest" }, { "Combat", "Combat" }, { "YouSee", "You see {0}." }, { "Nothing", "nothing" }, { "Blood", "blood" },
    { "OutOfRange", "Out of range!" }, { "NotPossible", "Not possible." }, { "WhatPlace", "Where?" }, { "ToWhom", "To whom?" },
    { "Gives", "{1} gets {0}." }, { "Readies", "{1} readies {0}." }, { "Unreadies", "{1} puts away {0}." }, { "Gets", "{1} gets {0}." },
    { "Drops", "{1} drops {0}." }, { "Solo", "Solo mode: {1}." }, { "PartyMode", "Party mode." }, { "NotInSolo", "Not in solo mode." },
    { "TalkSelf", "Talking to yourself?" }, { "NoResponse", "No response." }, { "FunnyNoResponse", "Funny, no response." },
    { "CombatBegin", "Begin combat!" }, { "CombatBreakOff", "Break off combat!" }, { "SaysLine", "{1}: \"{0}\"" },
    { "ErrorLine", "Error {1}: {0}" }, { "QuitAsk", "Quit the game? (Y/N) " },
    { "Saved", "Game saved." }, { "Loaded", "Game loaded." }, { "NoSaving", "Saving isn't allowed here." }, { "NoSavedGame", "There is no saved game." },
    { "YouSay", "you say: " }, { "Bye", "bye" }, { "NoHelp", "\"I cannot help thee with that.\"" }, { "PickReply", "Choose: " },
    { "Searching", "Searching here, you find {0}." }, { "SearchNothing", "Searching here, you find nothing." }, { "Locked", "Locked." },
    { "NothingWritten", "There is nothing written." }, { "More", "<more>" },
    { "Attacks", "{1} attacks {0}." }, { "Missed", "Missed!" }, { "Barely", "{0} barely wounded." }, { "Lightly", "{0} lightly wounded." },
    { "Heavily", "{0} heavily wounded." }, { "Critical", "{0} critically wounded!" }, { "KnockedOut", "{0} is knocked out!" }, { "Killed", "{0} killed!" },
    { "BarelyBy", "{0} barely damaged by {1}." }, { "LightlyBy", "{0} lightly damaged by {1}." }, { "HeavilyBy", "{0} heavily damaged by {1}." },
    { "CriticalBy", "{0} critically damaged by {1}!" }, { "KnockedOutBy", "{0} is knocked out by {1}!" }, { "KilledBy", "{0} killed by {1}!" },
    { "PartyFull", "The party is full: {1} doesn't join." },
    { "TurnOf", "{1}:" }, { "Pass", "Pass" },
    { "NotWhileCombat", "-Not while in Combat!" }, { "FoesNear", "-Not while foes are near!" }, { "OthersNear", "-Not while others are near!" },
    { "RestNotSolo", "-Not in solo mode!" }, { "HowManyHours", "How many hours? " }, { "WhoGuards", "Who will guard? " }, { "NoOne", "none" },
    { "Mealtime", "Mealtime!" }, { "HasFood", "{1} has food." }, { "NoFood", "{1} has no food." },
    { "StandsGuard", "{1} stands guard while the party rests." }, { "Healed", "{1} has healed." },
    { "NoSpellbook", "No spellbook is readied." }, { "NoSpells", "No spells in the spellbook." }, { "NotInBook", "That spell is not in thy spellbook!" },
    { "LevelTooLow", "Your level is not high enough." }, { "NoMagicPoints", "Not enough magic points." }, { "NoReagents", "No Reagents." }, { "OnWhom", "On whom? " },
    { "ModeCommand", "COMMAND" }, { "ModeFront", "FRONT" }, { "ModeRear", "REAR" }, { "ModeFlank", "FLANK" }, { "ModeBerserk", "BERSERK" },
    { "ModeRetreat", "RETREAT" }, { "ModeAssault", "ASSAULT" },
    { "PointsLeft", "Points left:" }, { "PressAKey", "Press a key..." },
    { "TitleIntro", "Introduction" }, { "TitleCreate", "Create New Character" }, { "TitleAcks", "Acknowledgments" }, { "TitleJourney", "Journey Onward" },
    { "NewLevel", "{1} is now level {0}!" }, { "Gains", "{1} gains {0}." }, { "NotReady", "{1} is not ready." },
    { "Strength", "strength" }, { "Dexterity", "dexterity" }, { "Intelligence", "intelligence" },
};

static const char *word(const char *id)
{
    cJSON *w = get(get(get(get(project(), "Settings"), "Ultima6"), "Words"), id);
    if (cJSON_IsString(w) && w->valuestring[0]) return w->valuestring;
    for (size_t i = 0; i < sizeof WORDS / sizeof WORDS[0]; i++) if (!strcmp(WORDS[i][0], id)) return WORDS[i][1];
    return id;
}

/* W(id, thing, who): {0} the thing, {1} whoever. */
static const char *wordf(const char *id, const char *thing, const char *who)
{
    static char out[4][256]; static int turn;
    char *o = out[turn++ & 3];
    const char *w = word(id);
    size_t n = 0;
    for (const char *p = w; *p && n < 250; p++)
    {
        if (p[0] == '{' && (p[1] == '0' || p[1] == '1') && p[2] == '}')
        {
            const char *with = p[1] == '0' ? (thing ? thing : "") : (who ? who : "");
            n += (size_t)snprintf(o + n, 256 - n, "%s", with);
            if (n > 250) n = 250;
            p += 2;
        }
        else o[n++] = *p;
    }
    o[n] = 0;
    return o;
}

/* ---- Notes: the engine's own words, through the maker's rules (U6Notes.Rewrite) ---- */

/* "*" stands for any words (case ignored); the words each * took into caps. */
static int glob(const char *pat, const char *s, char caps[][128], int *capCount)
{
    if (!*pat) return !*s;
    if (*pat == '*')
    {
        int at = *capCount;
        for (const char *t = s; ; t++)
        {
            /* shortest first (as the rule's lazy (.*?)) */
            if (at < 8) { snprintf(caps[at], 128, "%.*s", (int)(t - s), s); *capCount = at + 1; }
            if (glob(pat + 1, t, caps, capCount)) return 1;
            *capCount = at;
            if (!*t) return 0;
        }
    }
    if (!*s || tolower((unsigned char)*pat) != tolower((unsigned char)*s)) return 0;
    return glob(pat + 1, s + 1, caps, capCount);
}

static int heardOn; static char heard[8][192]; static int heardCount;

void u6_note(const char *text)
{
    if (!on || !text || !text[0]) return;
    cJSON *u6 = get(get(project(), "Settings"), "Ultima6"), *rule;
    char said[400];
    const char *say = text;
    cJSON_ArrayForEach(rule, get(u6, "NoteRules"))
    {
        char when[256], caps[8][128]; int capCount = 0;
        snprintf(when, sizeof when, "%s", str(rule, "When"));
        char *w = when; while (*w == ' ') w++;
        size_t len = strlen(w); while (len && w[len - 1] == ' ') w[--len] = 0;
        if (!w[0] || !glob(w, text, caps, &capCount)) continue;
        /* {1}, {2}... the words each * stood for */
        const char *s = str(rule, "Say");
        size_t n = 0;
        for (const char *p = s; *p && n < sizeof said - 1; p++)
        {
            if (p[0] == '{' && p[1] >= '1' && p[1] <= '8' && p[2] == '}' && p[1] - '1' < capCount) { n += (size_t)snprintf(said + n, sizeof said - n, "%s", caps[p[1] - '1']); if (n >= sizeof said) n = sizeof said - 1; p += 2; }
            else said[n++] = *p;
        }
        said[n] = 0;
        int blank = 1; for (const char *p = said; *p; p++) if (*p != ' ') blank = 0;
        if (blank) return; /* the rule says nothing */
        say = said;
        goto tell;
    }
    if (!(cJSON_IsBool(get(u6, "ShowOtherNotes")) ? cJSON_IsTrue(get(u6, "ShowOtherNotes")) : 1)) return;
tell:
    if (heardOn) { if (heardCount < 8) snprintf(heard[heardCount++], sizeof heard[0], "%s", say); return; } /* kept: told after the verb's line */
    u6_say(say);
}

/* ---- The keys (Engine/U6Keys.cs: Ultima 6's, under the maker's) ---- */
static const char *KEYS[][2] = {
    { "Up", "Up" }, { "Down", "Down" }, { "Left", "Left" }, { "Right", "Right" }, { "UpLeft", "NumPad7" }, { "UpRight", "NumPad9" },
    { "DownLeft", "NumPad1" }, { "DownRight", "NumPad3" }, { "Attack", "A" }, { "Cast", "C" }, { "Talk", "T" }, { "Look", "L" }, { "Get", "G" },
    { "Drop", "D" }, { "Move", "M" }, { "Use", "U" }, { "Rest", "R" }, { "Combat", "B" }, { "ToggleCursor", "Tab" }, { "TogglePortrait", "Multiply" },
    { "Member1", "F1" }, { "Member2", "F2" }, { "Member3", "F3" }, { "Member4", "F4" }, { "Member5", "F5" }, { "Member6", "F6" },
    { "Member7", "F7" }, { "Member8", "F8" }, { "Member9", "F9" }, { "PartyView", "F10" },
    { "Solo1", "D1" }, { "Solo2", "D2" }, { "Solo3", "D3" }, { "Solo4", "D4" }, { "Solo5", "D5" }, { "Solo6", "D6" }, { "Solo7", "D7" },
    { "Solo8", "D8" }, { "Solo9", "D9" }, { "PartyMode", "D0" }, { "Save", "Ctrl+S" }, { "Load", "Ctrl+R" }, { "TestNextPeriod", "F11" },
    { "TestScriptState", "F12" }, { "Quit", "Ctrl+Q" }, { "Cancel", "Escape" }, { "Accept", "Enter" }, { "Accept2", "Space" }, { "Erase", "Back" },
};
#define KEY_COUNT ((int)(sizeof KEYS / sizeof KEYS[0]))
static char keyOf[KEY_COUNT][24];

/* Each action's name (Engine/U6Keys.cs: what the Set Key / Ask For Key commands name it by) and group (clashes: World, Screens, Everywhere). */
static const char *KEY_NAMES[] = {
    "Walk north / cursor up", "Walk south / cursor down", "Walk west / cursor left", "Walk east / cursor right", "Walk north-west", "Walk north-east",
    "Walk south-west", "Walk south-east", "Attack", "Cast", "Talk", "Look", "Get", "Drop", "Move (push, between members, into containers)", "Use", "Rest",
    "Begin / break off combat", "Cursor to the inventory and back", "Portrait / inventory",
    "Show member 1", "Show member 2", "Show member 3", "Show member 4", "Show member 5", "Show member 6", "Show member 7", "Show member 8", "Show member 9", "Show the party",
    "Solo: member 1", "Solo: member 2", "Solo: member 3", "Solo: member 4", "Solo: member 5", "Solo: member 6", "Solo: member 7", "Solo: member 8", "Solo: member 9",
    "Party mode (the party follows again)", "Save the game", "Load a game", "Game Test: next schedule period", "Game Test: script data", "Quit prompt",
    "Cancel", "Pick / Enter", "Pick (second key)", "Erase a typed letter",
};
enum { KG_WORLD, KG_SCREENS, KG_EVERYWHERE };
static int key_is(const PlatformEvent *e, const char *action);
static int key_group(int i) { const char *id = KEYS[i][0]; return !strcmp(id, "Quit") || !strcmp(id, "Cancel") || !strcmp(id, "Accept") ? KG_EVERYWHERE : !strcmp(id, "Erase") ? KG_SCREENS : KG_WORLD; }
static int key_index(const char *id) { for (int i = 0; i < KEY_COUNT; i++) if (!strcmp(KEYS[i][0], id)) return i; return -1; }
static int key_by_name(const char *name) { for (int i = 0; i < KEY_COUNT; i++) if (!strcmp(KEY_NAMES[i], name)) return i; return -1; }

/* The player's own keys, beside the game's saves (U6Keys.Save: only those that differ from the maker's), through new games and loads. */
static void keys_file(char *path, size_t size) { snprintf(path, size, "%s/keys.json", save_folder() ? save_folder() : "."); }

static void maker_key(int i, char *out, size_t size)
{
    cJSON *k = get(get(get(get(project(), "Settings"), "Ultima6"), "Keys"), KEYS[i][0]);
    snprintf(out, size, "%s", cJSON_IsString(k) && k->valuestring[0] ? k->valuestring : KEYS[i][1]);
}

static void load_keys(void)
{
    for (int i = 0; i < KEY_COUNT; i++) maker_key(i, keyOf[i], sizeof keyOf[i]);
    char path[1200]; keys_file(path, sizeof path);
    long n = 0; char *text = NULL;
    FILE *f = fopen(path, "rb");
    if (f) { fseek(f, 0, SEEK_END); n = ftell(f); fseek(f, 0, SEEK_SET); text = n > 0 ? (char *)malloc((size_t)n + 1) : NULL; if (text) { n = (long)fread(text, 1, (size_t)n, f); text[n] = 0; } fclose(f); }
    cJSON *own = text ? cJSON_Parse(text) : NULL, *k;
    free(text);
    cJSON_ArrayForEach(k, own) { int i = key_index(k->string); if (i >= 0 && cJSON_IsString(k) && k->valuestring[0]) snprintf(keyOf[i], sizeof keyOf[i], "%s", k->valuestring); }
    cJSON_Delete(own);
}

static int save_keys(void)
{
    cJSON *changed = cJSON_CreateObject();
    int any = 0;
    for (int i = 0; i < KEY_COUNT; i++) { char m[24]; maker_key(i, m, sizeof m); if (strcmp(m, keyOf[i])) { cJSON_AddStringToObject(changed, KEYS[i][0], keyOf[i]); any = 1; } }
    char path[1200]; keys_file(path, sizeof path);
    int ok = 1;
    if (!any) remove(path);
    else
    {
        if (save_folder()) platform_make_dir(save_folder());
        char *text = cJSON_PrintUnformatted(changed);
        FILE *f = text ? fopen(path, "wb") : NULL;
        if (f) { ok = fwrite(text, 1, strlen(text), f) == strlen(text); fclose(f); } else ok = 0;
        free(text);
    }
    cJSON_Delete(changed);
    heldCount = 0; /* a walking key changed under the finger mustn't walk on by itself */
    return ok;
}

/* U6Keys.Assign: the action gets the key; another action that clashes (same group, or Everywhere) takes this one's old key (swap) or nothing changes. */
static int clash(int a, int b) { return key_group(a) == KG_EVERYWHERE || key_group(b) == KG_EVERYWHERE || key_group(a) == key_group(b); }
static int assign_key(int id, const char *key, int swap)
{
    char old[24]; snprintf(old, sizeof old, "%s", keyOf[id]);
    if (!strcmp(old, key)) return 1;
    int holders[KEY_COUNT], n = 0;
    for (int i = 0; i < KEY_COUNT; i++) if (i != id && !strcmp(keyOf[i], key) && clash(i, id)) holders[n++] = i;
    if (n > 0 && !swap) return 0;
    for (int h = 0; h < n; h++)
        for (int o = 0; o < KEY_COUNT; o++)
            if (o != id && o != holders[h] && !strcmp(keyOf[o], old) && clash(o, holders[h])) return 0;
    for (int h = 0; h < n; h++) snprintf(keyOf[holders[h]], sizeof keyOf[0], "%s", old);
    snprintf(keyOf[id], sizeof keyOf[id], "%s", key);
    return save_keys();
}

/* A key's words (U8Keys.Display): "Up Arrow", "Keypad 7", "Ctrl+S". */
static void key_words(const char *key, char *out, size_t size)
{
    static const char *W[][2] = { { "Up", "Up Arrow" }, { "Down", "Down Arrow" }, { "Left", "Left Arrow" }, { "Right", "Right Arrow" }, { "Back", "Backspace" },
        { "PageUp", "Page Up" }, { "PageDown", "Page Down" }, { "LeftShift", "Left Shift" }, { "RightShift", "Right Shift" }, { "LeftCtrl", "Left Ctrl" },
        { "RightCtrl", "Right Ctrl" }, { "LeftAlt", "Left Alt" }, { "RightAlt", "Right Alt" }, { "CapsLock", "Caps Lock" }, { "NumLock", "Num Lock" },
        { "Scroll", "Scroll Lock" }, { "OemTilde", "`" }, { "OemMinus", "-" }, { "OemPlus", "=" }, { "OemOpenBrackets", "[" }, { "OemCloseBrackets", "]" },
        { "OemSemicolon", ";" }, { "OemQuotes", "'" }, { "OemComma", "," }, { "OemPeriod", "." }, { "OemQuestion", "/" }, { "OemPipe", "\\" },
        { "Add", "Keypad +" }, { "Subtract", "Keypad -" }, { "Multiply", "Keypad *" }, { "Divide", "Keypad /" }, { "Decimal", "Keypad ." } };
    const char *name = strrchr(key, '+'); name = name && name[1] ? name + 1 : key;
    int held = (int)(name - key);
    char one[24];
    if (strlen(name) == 2 && name[0] == 'D' && isdigit((unsigned char)name[1])) snprintf(one, sizeof one, "%c", name[1]);
    else if (strlen(name) == 7 && !strncmp(name, "NumPad", 6)) snprintf(one, sizeof one, "Keypad %c", name[6]);
    else
    {
        snprintf(one, sizeof one, "%s", name);
        for (size_t i = 0; i < sizeof W / sizeof W[0]; i++) if (!strcmp(W[i][0], name)) snprintf(one, sizeof one, "%s", W[i][1]);
    }
    snprintf(out, size, "%.*s%s", held, key, one);
}

/* The key with these words (Set Key's list): every key the game reads, its words compared. */
static int key_from_words(const char *words, char *out, size_t size)
{
    static const char *OTHERS[] = { "Up", "Down", "Left", "Right", "Space", "Enter", "Tab", "Back", "Escape", "Insert", "Delete", "Home", "End", "PageUp", "PageDown",
        "LeftShift", "RightShift", "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt", "CapsLock", "NumLock", "Scroll", "Pause", "OemTilde", "OemMinus", "OemPlus",
        "OemOpenBrackets", "OemCloseBrackets", "OemSemicolon", "OemQuotes", "OemComma", "OemPeriod", "OemQuestion", "OemPipe", "Add", "Subtract", "Multiply", "Divide", "Decimal" };
    char name[24], w[48];
    for (int pass = 0; pass < 5; pass++)
    {
        int count = pass == 0 ? 26 : pass == 1 ? 10 : pass == 2 ? 10 : pass == 3 ? 12 : (int)(sizeof OTHERS / sizeof OTHERS[0]);
        for (int i = 0; i < count; i++)
        {
            if (pass == 0) snprintf(name, sizeof name, "%c", 'A' + i);
            else if (pass == 1) snprintf(name, sizeof name, "D%d", i);
            else if (pass == 2) snprintf(name, sizeof name, "NumPad%d", i);
            else if (pass == 3) snprintf(name, sizeof name, "F%d", i + 1);
            else snprintf(name, sizeof name, "%s", OTHERS[i]);
            key_words(name, w, sizeof w);
            if (!strcmp(w, words)) { snprintf(out, size, "%s", name); return 1; }
        }
    }
    return 0;
}

/* Ask For Key: the next key pressed (the text area asks for it) becomes the action's; the Cancel key - no change, false. */
static int keyAskAction = -1, keyAskSwap, keyAskToken;
static int key_ask_took(const PlatformEvent *e)
{
    if (keyAskAction < 0) return 0;
    if (e->type != EV_KEY_DOWN || e->keyRepeat || !e->keyName[0]) return 1;
    int t = keyAskToken, id = keyAskAction;
    if (strcmp(KEYS[id][0], "Cancel") && key_is(e, "Cancel")) { keyAskAction = -1; keyAskToken = 0; events_doll_answered(t, 0); return 1; }
    int modifierAlone = strstr(e->keyName, "Shift") || strstr(e->keyName, "Ctrl") || strstr(e->keyName, "Alt");
    char key[32];
    if (!modifierAlone && (e->keyMods & (KEYMOD_CTRL | KEYMOD_SHIFT | KEYMOD_ALT)))
        snprintf(key, sizeof key, "%s%s", e->keyMods & KEYMOD_CTRL ? "Ctrl+" : e->keyMods & KEYMOD_ALT ? "Alt+" : "Shift+", e->keyName);
    else snprintf(key, sizeof key, "%s", e->keyName);
    keyAskAction = -1; keyAskToken = 0;
    int ok = assign_key(id, key, keyAskSwap);
    char w[48]; key_words(key, w, sizeof w); u6_say(w);
    events_doll_answered(t, ok);
    return 1;
}

static int key_command(int type, cJSON *params, int token)
{
    int id = key_by_name(str(params, "Action"));
    switch (type)
    {
        case C_U6AskKey:
            if (id < 0) return -1;
            if (keyAskAction >= 0 && keyAskToken) events_doll_answered(keyAskToken, 0);
            keyAskAction = id; keyAskSwap = strcmp(str(params, "IfUsed"), "Fail") != 0; keyAskToken = token;
            u6_say(word("PressAKey"));
            return 0;
        case C_U6SetKey:
        {
            char key[24], name[40];
            if (id < 0 || !key_from_words(str(params, "Key"), key, sizeof key)) return -1;
            const char *with = str(params, "With");
            if (!strcmp(with, "Ctrl") || !strcmp(with, "Shift") || !strcmp(with, "Alt")) snprintf(name, sizeof name, "%s+%s", with, key);
            else snprintf(name, sizeof name, "%s", key);
            return assign_key(id, name, strcmp(str(params, "IfUsed"), "Fail") != 0) ? 1 : -1;
        }
        case C_U6GetKeyName:
        {
            if (id < 0) return -1;
            char w[48]; key_words(keyOf[id], w, sizeof w);
            script_set(str(params, "Variable"), value_text(w));
            return 1;
        }
        case C_U6ResetKeys:
            if (!strcmp(str(params, "Action"), "Every key")) { for (int i = 0; i < KEY_COUNT; i++) maker_key(i, keyOf[i], sizeof keyOf[i]); return save_keys() ? 1 : -1; }
            if (id < 0) return -1;
            { char m[24]; maker_key(id, m, sizeof m); return assign_key(id, m, 1) ? 1 : -1; }
    }
    return 1;
}

/* Is this event the key named ("Ctrl+P": Ctrl held; a key alone: neither Ctrl nor Alt held)? */
static int key_name_is(const PlatformEvent *e, const char *k)
{
    if (!e->keyName[0] || !k[0]) return 0;
    const char *plus; int mods = 0;
    while ((plus = strchr(k, '+')) != NULL)
    {
        if (!strncmp(k, "Ctrl", 4)) mods |= KEYMOD_CTRL; else if (!strncmp(k, "Shift", 5)) mods |= KEYMOD_SHIFT; else if (!strncmp(k, "Alt", 3)) mods |= KEYMOD_ALT;
        k = plus + 1;
    }
    if (strcmp(k, e->keyName)) return 0;
    return (e->keyMods & (KEYMOD_CTRL | KEYMOD_ALT)) == (mods & (KEYMOD_CTRL | KEYMOD_ALT)) && (!(mods & KEYMOD_SHIFT) || (e->keyMods & KEYMOD_SHIFT));
}

/* A custom panel's key (HUD Editor; not switched off): it opens or closes. 1 taken. */
static int panel_key(const PlatformEvent *e)
{
    cJSON *p;
    cJSON_ArrayForEach(p, get(hud, "Pieces"))
    {
        if (!is_custom_panel(p) || !str(p, "Key")[0]) continue;
        HudState *s = hud_state(num(p, "Id", -1), 0);
        if (s && s->keyOff) continue;
        if (!key_name_is(e, str(p, "Key"))) continue;
        int open = own_shown(p);
        s = hud_state(num(p, "Id", -1), 1);
        if (s) { s->shown = !open; hudVersion++; }
        return 1;
    }
    return 0;
}

/* Is this event the action's key ("Ctrl+S": Ctrl held; a key alone: neither Ctrl nor Alt held)? */
static int key_is(const PlatformEvent *e, const char *action)
{
    if (!e->keyName[0]) return 0;
    for (int i = 0; i < KEY_COUNT; i++)
    {
        if (strcmp(KEYS[i][0], action)) continue;
        const char *k = keyOf[i], *plus;
        int mods = 0;
        while ((plus = strchr(k, '+')) != NULL)
        {
            if (!strncmp(k, "Ctrl", 4)) mods |= KEYMOD_CTRL; else if (!strncmp(k, "Shift", 5)) mods |= KEYMOD_SHIFT; else if (!strncmp(k, "Alt", 3)) mods |= KEYMOD_ALT;
            k = plus + 1;
        }
        if (strcmp(k, e->keyName)) return 0;
        return (e->keyMods & (KEYMOD_CTRL | KEYMOD_ALT)) == (mods & (KEYMOD_CTRL | KEYMOD_ALT)) && (!(mods & KEYMOD_SHIFT) || (e->keyMods & KEYMOD_SHIFT));
    }
    return 0;
}

/* The walking keys this key is (an arrow; a diagonal is two): dx, dy; 0 none. */
static int walk_of(const PlatformEvent *e, int *dx, int *dy)
{
    static const struct { const char *a; int x, y; } W[] = { { "Up", 0, -1 }, { "Down", 0, 1 }, { "Left", -1, 0 }, { "Right", 1, 0 },
                                                             { "UpLeft", -1, -1 }, { "UpRight", 1, -1 }, { "DownLeft", -1, 1 }, { "DownRight", 1, 1 } };
    for (int i = 0; i < 8; i++) if (key_is(e, W[i].a)) { *dx = W[i].x; *dy = W[i].y; return 1; }
    return 0;
}

/* ---- State ---- */
enum { FOCUS_MAP, FOCUS_PANEL };
static int verb = -1, focus = FOCUS_MAP, mapCurX, mapCurY, panelCursor, hudMember = -1;
static Item *pickedItem; static int pickedOwner = -1, pickedThing; /* Drop / Move: what was picked first */
typedef struct HeldKey { int dx, dy, order; } HeldKey;
static HeldKey held[8]; static int heldCount, heldOrder;

static int player_char(void) { return world_actor_character(0); }
static const char *char_name(int id) { return str(character_json(id), "Name"); }

/* GetPartyList: the avatar, then the members in the project's order. */
static int party_list(int *ids, int max)
{
    int n = 0, me = player_char();
    if (me >= 0 && n < max) ids[n++] = me;
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Characters"), "Characters"))
    {
        int id = num(c, "Id", -1);
        if (id != me && items_in_party(id) && n < max) ids[n++] = id;
    }
    return n;
}

static int member_shown(void) { return hudMember >= 0 && (hudMember == player_char() || items_in_party(hudMember)) ? hudMember : player_char(); }

/* ---- The panel's cells: the body slots, then the carried cells ---- */
typedef struct Cell { int x, y, w, h, slot, carried; } Cell;
static int panel_cells(Cell *cells, int max)
{
    int n = 0;
    if (panelView != VIEW_INVENTORY) return 0;
    cJSON *p;
    cJSON_ArrayForEach(p, get(hud, "Pieces"))
        if (num(p, "Kind", -1) == P_BODYSLOT && n < max) cells[n++] = (Cell){ num(p, "X", 0), num(p, "Y", 0), num(p, "Width", 16), num(p, "Height", 16), num(p, "Slot", 0), -1 };
    cJSON_ArrayForEach(p, get(hud, "Pieces"))
        if (num(p, "Kind", -1) == P_CARRIED)
        {
            int columns, rows, cell; cJSON *emptyLink;
            carried_grid(p, member_shown(), &columns, &rows, &cell, &emptyLink);
            for (int i = 0; i < columns * rows && n < max; i++) cells[n++] = (Cell){ num(p, "X", 0) + i % columns * cell, num(p, "Y", 0) + i / columns * cell, cell, cell, -1, i };
        }
    return n;
}

static Item *item_in_cell(int member, const Cell *c)
{
    if (c->slot >= 0) return items_in_slot(member, c->slot);
    Item *carried[96];
    int n = carried_items(member, carried, 96);
    return c->carried < n ? carried[c->carried] : NULL;
}

static int first_filled_cell(void)
{
    Cell cells[96]; int n = panel_cells(cells, 96), member = member_shown();
    for (int i = 0; i < n; i++) if (cells[i].slot < 0 && item_in_cell(member, &cells[i])) return i;
    for (int i = 0; i < n; i++) if (item_in_cell(member, &cells[i])) return i;
    return -1;
}

/* ---- Verbs ---- */

static void set_focus(int f) { focus = f; if (f == FOCUS_PANEL) partyView = 0; }

static void end_verb(void) { verb = -1; prompt[0] = 0; }

static void start_verb(int v)
{
    looked = -1;
    if (verb >= 0) { u6_say(wordf("Nothing", "", "")); }
    if (v == 9) /* Combat: begin / break off */
    {
        end_verb();
        combat_toggle_mode();
        if (!combat_mode()) combat_over_take_avatar(); /* a Command member was being moved on its turn: the avatar again */
        u6_say(word(combat_mode() ? "CombatBegin" : "CombatBreakOff"));
        return;
    }
    if (v == 8) { end_verb(); rest(); return; } /* no target: the party camps */
    if (v == 1) { end_verb(); start_cast(); return; } /* Cast: the book in the panel (U6Cast) */
    pickedItem = NULL; pickedOwner = -1; pickedThing = 0;
    verb = v;
    snprintf(prompt, sizeof prompt, "%s-", word(VERBS[v]));
    int mx, my, mf; world_player_cell(&mx, &my, &mf);
    mapCurX = mx; mapCurY = my;
    int inPanel = v == 5 || focus == FOCUS_PANEL; /* Drop starts in the inventory */
    if (inPanel) { int f = first_filled_cell(); if (f >= 0) panelCursor = f; }
    set_focus(inPanel ? FOCUS_PANEL : FOCUS_MAP);
}

static void cancel_verb(void)
{
    if (verb >= 0) { char line[200]; snprintf(line, sizeof line, "%s%s", prompt, word("Nothing")); u6_say(line); }
    casting = 0; aimSpell = NULL; aimCaster = -1;
    pickedItem = NULL; pickedThing = 0; pickedOwner = -1;
    end_verb();
    set_focus(FOCUS_MAP);
}

/* The verb's line finished ("Look-wall"), then what happened. */
static void finish(const char *target, const char *told1, const char *told2)
{
    int second = (pickedItem || pickedThing) && verb >= 0;
    char line[256];
    if (second) snprintf(line, sizeof line, "%s-%s", word(VERBS[verb]), target);
    else snprintf(line, sizeof line, "%s%s", prompt, target);
    u6_say(line);
    if (told1 && told1[0]) u6_say(told1);
    if (told2 && told2[0]) u6_say(told2);
    pickedItem = NULL; pickedThing = 0; pickedOwner = -1;
    end_verb();
    set_focus(FOCUS_MAP);
    pass_turn_if_fighting(0); /* turn-by-turn combat: the verb took the turn */
}

/* HudAct: the engine acts; its refusals kept and told after the verb's line (none: Not possible). */
static void act_begin(void) { heardOn = 1; heardCount = 0; }
static void act_end(const char *target, int done, const char *saidIfDone)
{
    heardOn = 0;
    if (done) { finish(target, saidIfDone, NULL); return; }
    if (heardCount == 0) { finish(target, word("NotPossible"), NULL); return; }
    char keep[8][192]; int n = heardCount;
    memcpy(keep, heard, sizeof keep);
    finish(target, keep[0], NULL);
    for (int i = 1; i < n; i++) u6_say(keep[i]);
}

/* What's on a map cell, the topmost: the avatar, a character, then a thing. */
static void thing_on_cell(int cx, int cy, int *actor, int *uid)
{
    *actor = -1; *uid = 0;
    int px, py, pf; world_player_cell(&px, &py, &pf);
    if (cx == px && cy == py) { *actor = 0; return; }
    double ox, oy; world_camera_origin(&ox, &oy);
    int sx = (int)((cx + 0.5) * world_tile_w() - ox), sy = (int)((cy + 0.5) * world_tile_h() - oy);
    if (!u6_in_view(sx, sy)) return;
    world_hit(sx, sy, actor, uid);
    if (*actor >= 0) *uid = 0;
}

static const char *thing_name_of(int actor, int uid, char *buf, size_t size)
{
    if (actor >= 0) { snprintf(buf, size, "%s", char_name(world_actor_character(actor))); return buf; }
    if (uid) return gumps_thing_name(uid, buf, size);
    snprintf(buf, size, "%s", word("Nothing"));
    return buf;
}

/* The ground's name: its tile's (Tile Properties' terrain label). */
static int tile_name(int x, int y, char *buf, size_t size)
{
    char key[200];
    if (!world_tile_key(x, y, 1, key, sizeof key)) return 0;
    const char *label = str(get(get(get(project(), "TileProperties"), "ByTileKey"), key), "TerrainLabel");
    if (!label[0]) return 0;
    snprintf(buf, size, "%s", label);
    return 1;
}

static int next_to(int actor, int cx, int cy, int reach)
{
    int x, y, f; world_actor_cell(actor, &x, &y, &f);
    int dx = abs(x - cx), dy = abs(y - cy);
    return (dx > dy ? dx : dy) <= reach;
}

static void say_look(const char *name)
{
    char line[256];
    snprintf(line, sizeof line, "%s-%s", word("Look"), name); u6_say(line);
    u6_say(wordf("YouSee", name, ""));
}

/* Put a carried thing down on a map cell (U6GameWindow PutDownAt): there, on the floor the avatar is on. */
static int put_down(Item *it, int owner, int cx, int cy)
{
    (void)owner;
    int category = items_category(it), itemId = items_item_id(it), quantity = items_quantity(it);
    int px, py, pf; world_player_cell(&px, &py, &pf);
    if (cx < 0 || cy < 0 || cx >= world_map_w() || cy >= world_map_h()) { cx = px; cy = py; }
    ItemPlace from;
    if (!items_detach(it, &from)) return 0;
    int uid = world_thing_put(category, itemId, quantity, cx, cy, pf);
    if (uid < 0) { items_restore(&from, it); return 0; }
    items_park(uid, it);
    items_detach_done(&from, category, itemId);
    traps_dropped(uid, category, itemId); /* By Item Dropped On It */
    return 1;
}

/* Move into the open bag (PutInOpenBag): taken from where it is and put in - one thing to a cell. */
static int put_in_open_bag(Item *it)
{
    if (!openBag || it == openBag) return 0;
    int category = items_category(it), itemId = items_item_id(it);
    ItemPlace from;
    if (!items_detach(it, &from)) return 0;
    if (!items_put_into(openBag, it, 0, 0, 0)) { u6_note(items_refusal()); items_restore(&from, it); return 0; }
    items_detach_done(&from, category, itemId);
    return 1;
}

/* The sound something opens with: the paperdoll open sound when System Settings has it on, else its own (PlayOpenSound). */
static void open_sound(int own)
{
    cJSON *sounds = get(get(project(), "Settings"), "Sounds");
    sound_play(!cJSON_IsFalse(get(sounds, "PaperdollOpenSoundEnabled")) && num(sounds, "PaperdollOpenSoundId", -1) >= 0 ? num(sounds, "PaperdollOpenSoundId", -1) : own);
}

/* Use on a chest or a body on the map (U6Search / U6SearchBody): locked - its rattle; its own By Use events first; else what it
   holds spills out on its square: "Searching here, you find a dagger, 3 arrows." */
static void search(int uid)
{
    int c, id, q, x, y, n;
    world_thing_info(uid, &c, &id, &q, &x, &y, &n);
    Item *box = items_parked(uid, c, id, q);
    cJSON *d = id <= -1000 || id < 0 ? get(get(project(), "Settings"), "BodyContainer") : container_json(id);
    if (box && items_is_locked(box)) { sound_play(num(d, "LockedRattleSoundResourceId", -1)); u6_say(word("Locked")); return; }
    if (id >= 0 && events_fire_item(5, id, TR_USE)) return; /* its own events say what opening it does */
    open_sound(num(d, "OpenSoundResourceId", -1));
    Item **inside;
    if (!box || items_contents(box, &inside) == 0) { u6_say(word("SearchNothing")); return; }
    char names[400] = "";
    int guard = 0;
    while (items_contents(box, &inside) > 0 && guard++ < 200)
    {
        Item *it = inside[0];
        size_t at = strlen(names);
        if (items_quantity(it) > 1) snprintf(names + at, sizeof names - at, "%s%d %s", at ? ", " : "", items_quantity(it), items_name(items_category(it), items_item_id(it)));
        else snprintf(names + at, sizeof names - at, "%s%s", at ? ", " : "", items_name(items_category(it), items_item_id(it)));
        if (!put_down(it, -1, x, y)) break;
    }
    u6_say(wordf("Searching", names, ""));
}

static void pick_map_cell(int cx, int cy, int givenActor, int givenUid)
{
    if (verb < 0) return;
    int actor = givenActor, uid = givenUid;
    if (actor < 0 && !uid) thing_on_cell(cx, cy, &actor, &uid);
    char name[128];
    thing_name_of(actor, uid, name, sizeof name);
    int me = player_char(), who = member_shown();
    switch (verb)
    {
        case 3: /* Look */
            if (actor < 0 && !uid && world_death_blood_at(cx, cy)) snprintf(name, sizeof name, "%s", word("Blood")); /* a death's blood */
            else if (actor < 0 && !uid) tile_name(cx, cy, name, sizeof name);
            finish(name, wordf("YouSee", name, ""), NULL);
            if (actor > 0) looked = world_actor_character(actor);
            break;
        case 1: /* Cast: the spell's target */
            cast_on_cell(cx, cy, actor, uid, name);
            break;
        case 4: /* Get */
            if (actor >= 0 || !uid) { finish(name, word("NotPossible"), NULL); break; }
            if (!next_to(controlled_actor(), cx, cy, 1)) { finish(name, word("OutOfRange"), NULL); break; }
            act_begin(); { int ok = gumps_u6_get(who, uid); act_end(name, ok, wordf("Gets", name, char_name(who))); }
            break;
        case 7: /* Use */
            if (actor >= 0 || !uid) { finish(name, word("NotPossible"), NULL); break; }
            if (!next_to(controlled_actor(), cx, cy, 1)) { finish(name, word("OutOfRange"), NULL); break; }
            { char line[200]; snprintf(line, sizeof line, "%s%s", prompt, name); u6_say(line); }
            end_verb(); set_focus(FOCUS_MAP);
            { int c, i, q, tx, ty, n; world_thing_info(uid, &c, &i, &q, &tx, &ty, &n); if (c == 5) { search(uid); pass_turn_if_fighting(0); break; } } /* a chest, a body: searched - it spills out */
            use_thing(uid);
            break;
        case 2: /* Talk */
            if (actor == 0) { finish(name, word("TalkSelf"), NULL); break; }
            if (actor < 0) { finish(name, word("NotPossible"), NULL); break; }
            if (soloMode) { finish(name, word("NotInSolo"), NULL); break; } /* only the avatar talks */
            {
                int cid = world_actor_character(actor);
                if (vitals_dead(cid) || vitals_incapacitated(cid)) { finish(name, word("NoResponse"), NULL); break; }
                if (cJSON_GetArraySize(get(character_json(cid), "TalkCommands")) == 0) { finish(name, word("FunnyNoResponse"), NULL); break; }
                char line[200]; snprintf(line, sizeof line, "%s%s", prompt, name); u6_say(line);
                end_verb(); set_focus(FOCUS_MAP);
                events_talk(actor);
            }
            break;
        case 0: /* Attack */
            if (actor > 0 && !items_in_party(world_actor_character(actor)))
            {
                if (!combat_mode()) { combat_toggle_mode(); u6_say(word("CombatBegin")); }
                combat_attack(0, actor);
                finish(name, NULL, NULL);
                if (turn_by_turn()) { turnRunMs = 0; pass_turn_if_fighting(1); } /* the turn: long enough for a blow */
            }
            else finish(name, word("NotPossible"), NULL);
            break;
        case 5: /* Drop: where */
        case 6: /* Move */
            if (pickedItem)
            {
                if (!next_to(controlled_actor(), cx, cy, 5)) { finish(word("WhatPlace"), word("OutOfRange"), NULL); break; } /* MapWindow::can_drop_or_move_obj: within 5 */
                char itemName[128]; snprintf(itemName, sizeof itemName, "%s", items_name(items_category(pickedItem), items_item_id(pickedItem)));
                act_begin(); { int ok = put_down(pickedItem, pickedOwner, cx, cy); act_end(itemName, ok, wordf("Drops", itemName, char_name(pickedOwner))); }
                break;
            }
            if (verb == 5) { set_focus(FOCUS_PANEL); break; }
            if (!pickedThing)
            {
                if (actor >= 0 || !uid) { finish(name, word("NotPossible"), NULL); break; }
                if (!next_to(controlled_actor(), cx, cy, 1)) { finish(name, word("OutOfRange"), NULL); break; }
                pickedThing = uid;
                snprintf(prompt, sizeof prompt, "%s-%s %s", word("Move"), name, word("WhatPlace"));
                break;
            }
            {
                int moving = pickedThing, c, i, q, tx, ty, n;
                char movedName[128]; gumps_thing_name(moving, movedName, sizeof movedName);
                world_thing_info(moving, &c, &i, &q, &tx, &ty, &n);
                if (abs(tx - cx) > 1 || abs(ty - cy) > 1) { finish(movedName, word("OutOfRange"), NULL); break; } /* Events::pushTo: one square on */
                act_begin();
                int ok = 0;
                if (gumps_u6_get(me, moving))
                {
                    Item *loose[64]; int k = items_loose(me, loose, 64);
                    ok = k > 0 && put_down(loose[k - 1], me, cx, cy);
                }
                act_end(movedName, ok, "");
            }
            break;
        default: finish(name, word("NotPossible"), NULL); break;
    }
    (void)me;
}

static void pick_panel_cell(const Cell *cell)
{
    if (verb < 0) return;
    int member = member_shown();
    Item *it = item_in_cell(member, cell);
    char name[128];
    snprintf(name, sizeof name, "%s", it ? items_name(items_category(it), items_item_id(it)) : word("Nothing"));
    if (verb == 6 && pickedItem) /* Move's second pick: a slot, the carried things, another member */
    {
        char itemName[128]; snprintf(itemName, sizeof itemName, "%s", items_name(items_category(pickedItem), items_item_id(pickedItem)));
        act_begin();
        if (member != pickedOwner) { int ok = gumps_u6_give(pickedItem, member); act_end(itemName, ok, wordf("Gives", itemName, char_name(member))); return; }
        if (cell->slot < 0 && openBag) { int ok = put_in_open_bag(pickedItem); act_end(itemName, ok, wordf("Gets", itemName, char_name(member))); return; } /* into the open bag */
        if (cell->slot >= 0) { int ok = gumps_u6_ready(pickedItem, member, cell->slot); act_end(itemName, ok, wordf("Readies", itemName, char_name(member))); return; }
        int ok = gumps_u6_give(pickedItem, member); act_end(itemName, ok, wordf("Unreadies", itemName, char_name(member)));
        return;
    }
    switch (verb)
    {
        case 3: finish(name, wordf("YouSee", name, ""), NULL); break;
        case 7:
            if (it && items_category(it) == 5) /* a bag: opens in the panel (Use the open one again: closed) */
            {
                openBag = it == openBag ? NULL : it;
                finish(name, NULL, NULL);
                break;
            }
            if (!it) { finish(name, word("NotPossible"), NULL); break; }
            { char line[200]; snprintf(line, sizeof line, "%s%s", prompt, name); u6_say(line); }
            end_verb(); set_focus(FOCUS_MAP);
            if (items_category(it) == CAT_OBJECT && magic_fetish_use(member, items_item_id(it))) break; /* an ingredient with a fetish (magic.c) */
            gumps_u6_use_item(it);
            break;
        case 5:
            if (!it) { finish(name, word("NotPossible"), NULL); break; }
            pickedItem = it; pickedOwner = member;
            snprintf(prompt, sizeof prompt, "%s-%s %s", word("Drop"), name, word("WhatPlace"));
            { int x, y, f; world_player_cell(&x, &y, &f); mapCurX = x; mapCurY = y; }
            set_focus(FOCUS_MAP);
            break;
        case 6:
            if (!it) { finish(name, word("NotPossible"), NULL); break; }
            pickedItem = it; pickedOwner = member;
            snprintf(prompt, sizeof prompt, "%s-%s %s", word("Move"), name, word("ToWhom"));
            break;
        default: finish(name, word("NotPossible"), NULL); break;
    }
}

static void pick_under_cursor(void)
{
    if (focus == FOCUS_PANEL)
    {
        Cell cells[96]; int n = panel_cells(cells, 96);
        if (partyView || n == 0) return;
        if (panelCursor >= n) panelCursor = n - 1;
        pick_panel_cell(&cells[panelCursor < 0 ? 0 : panelCursor]);
    }
    else pick_map_cell(mapCurX, mapCurY, -1, 0);
}

static void move_cursor(int dx, int dy)
{
    if (focus == FOCUS_MAP)
    {
        int px, py, pf; world_player_cell(&px, &py, &pf);
        int rx = vw / world_tile_w() / 2, ry = vh / world_tile_h() / 2;
        mapCurX += dx; mapCurY += dy;
        if (mapCurX < px - rx) mapCurX = px - rx;
        if (mapCurX > px + rx) mapCurX = px + rx;
        if (mapCurY < py - ry) mapCurY = py - ry;
        if (mapCurY > py + ry) mapCurY = py + ry;
        return;
    }
    /* the nearest cell that way, by their middles */
    Cell cells[96]; int n = panel_cells(cells, 96);
    if (n == 0) return;
    if (panelCursor >= n) panelCursor = n - 1;
    const Cell *from = &cells[panelCursor < 0 ? 0 : panelCursor];
    double mx = from->x + from->w / 2.0, my = from->y + from->h / 2.0, bestScore = 1e18;
    int best = -1;
    for (int i = 0; i < n; i++)
    {
        double ox = cells[i].x + cells[i].w / 2.0 - mx, oy = cells[i].y + cells[i].h / 2.0 - my;
        double along = ox * dx + oy * dy;
        if (along <= 0.5) continue;
        double across = dx != 0 ? (oy < 0 ? -oy : oy) : (ox < 0 ? -ox : ox);
        double score = along + across * 2;
        if (score < bestScore) { bestScore = score; best = i; }
    }
    if (best >= 0) panelCursor = best;
}

static void show_member(int index)
{
    looked = -1;
    int ids[16]; int n = party_list(ids, 16);
    if (index >= n) return;
    hudMember = ids[index];
    partyView = 0;
}

/* A character's inventory or stats asked for (gumps.c - Ultima 6 has no paperdolls): a party member shown in the HUD's panel. */
void u6_show_character(int characterId)
{
    int ids[16]; int n = party_list(ids, 16);
    for (int i = 0; i < n; i++) if (ids[i] == characterId) { show_member(i); return; }
}

static void toggle_view(void)
{
    panelView = panelView == VIEW_INVENTORY ? VIEW_PORTRAIT : VIEW_INVENTORY;
    partyView = 0;
    if (panelView == VIEW_PORTRAIT && focus == FOCUS_PANEL) focus = FOCUS_MAP;
}

/* ---- Keys ---- */

static int star_key(const PlatformEvent *e) { return !strcmp(e->keyName, "D8") && (e->keyMods & KEYMOD_SHIFT) && !strcmp(keyOf[19], "Multiply"); }

static int verb_key(const PlatformEvent *e)
{
    for (int v = 0; v < 10; v++) if (key_is(e, VERBS[v])) return v;
    return -1;
}

static int numbered_key(const PlatformEvent *e, const char *prefix)
{
    char action[16];
    for (int i = 1; i <= 9; i++) { snprintf(action, sizeof action, "%s%d", prefix, i); if (key_is(e, action)) return i - 1; }
    return -1;
}

static void held_add(int dx, int dy) { for (int i = 0; i < heldCount; i++) if (held[i].dx == dx && held[i].dy == dy) { held[i].order = ++heldOrder; return; } if (heldCount < 8) held[heldCount++] = (HeldKey){ dx, dy, ++heldOrder }; }
static void held_remove(int dx, int dy) { for (int i = 0; i < heldCount; i++) if (held[i].dx == dx && held[i].dy == dy) { held[i] = held[--heldCount]; return; } }

void u6_walk(int *dx, int *dy)
{
    *dx = *dy = 0;
    int best = 0;
    for (int i = 0; i < heldCount; i++) if (held[i].order > best) { best = held[i].order; *dx = held[i].dx; *dy = held[i].dy; }
}

static int letter_ask_took(const PlatformEvent *e);

int u6_key(const PlatformEvent *e)
{
    if (!on) return 0;
    if (key_ask_took(e)) return 1; /* Ask For Key: the next key is the action's */
    if (input_key(e)) return 1; /* Type Text, Pick Picture, Distribute Points */
    if (scene_key(e)) return 1; /* a scene waiting for a key or a choice */
    if (title_key(e)) return 1; /* the title menu */
    if (!hasHud) return 0;
    if (letter_ask_took(e)) return 1; /* a key asked for (a book's <more>, Quit's Y/N, Rest's hours) */
    if (casting) { if (e->type == EV_KEY_DOWN) cast_key(e); return 1; } /* the book is open */
    int dx = 0, dy = 0, arrow = walk_of(e, &dx, &dy);
    if (e->type == EV_KEY_UP)
    {
        if (!arrow) return 0;
        if (turnStepFromX != -99999) return 1; /* turn by turn: the step goes on to the next cell, the key let go or not */
        held_remove(dx, dy);
        return 1;
    }
    if (verb >= 0 || focus == FOCUS_PANEL)
    {
        if (e->keyRepeat && !arrow) return 1;
        if (key_is(e, "Cancel")) { cancel_verb(); return 1; }
        if (key_is(e, "ToggleCursor"))
        {
            if (focus == FOCUS_MAP) { int f = first_filled_cell(); if (f >= 0) panelCursor = f; }
            set_focus(focus == FOCUS_MAP ? FOCUS_PANEL : FOCUS_MAP);
            return 1;
        }
        int m = numbered_key(e, "Member");
        if (m >= 0) { show_member(m); return 1; }
        if (key_is(e, "TogglePortrait") || star_key(e)) { toggle_view(); return 1; }
        if (arrow) { move_cursor(dx, dy); return 1; }
        if (key_is(e, "Accept") || key_is(e, "Accept2")) { pick_under_cursor(); return 1; }
        int v = verb < 0 ? verb_key(e) : -1;
        if (v >= 0) start_verb(v);
        return 1;
    }
    if (e->keyRepeat && !arrow) return 1;
    if (panel_key(e)) return 1; /* a custom panel's key */
    int v = verb_key(e);
    if (v >= 0) { start_verb(v); return 1; }
    if (key_is(e, "ToggleCursor")) { int f = first_filled_cell(); if (f >= 0) panelCursor = f; set_focus(FOCUS_PANEL); return 1; }
    if (key_is(e, "TogglePortrait") || star_key(e)) { toggle_view(); return 1; }
    int m = numbered_key(e, "Member");
    if (m >= 0) { show_member(m); return 1; }
    if (key_is(e, "PartyView")) { partyView = 1; return 1; }
    { int s = numbered_key(e, "Solo"); if (s >= 0) { start_solo(s); return 1; } }
    if (key_is(e, "PartyMode")) { end_solo(); return 1; }
    if (key_is(e, "Save")) { quick_save_or_load(1); return 1; }
    if (key_is(e, "Load")) { quick_save_or_load(0); return 1; }
    if (key_is(e, "Quit")) return 0; /* the engine's Quit prompt (Ctrl+Q) */
    if (!arrow && verb < 0 && key_is(e, "Accept2") && turn_pass()) return 1; /* Space in turn-by-turn combat: pass */
    if (arrow)
    {
        if (e->keyRepeat) return 1;
        if (turn_step(dx, dy)) return 1; /* turn-by-turn combat: one cell, one round */
        held_add(dx, dy);
        return 1;
    }
    if (key_is(e, "Cancel") || !strcmp(e->keyName, "Escape") || !strcmp(e->keyName, "F11")) return 0; /* Escape closes what's open; F11 full screen */
    return 1; /* anything else that isn't Ultima 6's does nothing */
}

/* ---- Clicks ---- */

static int buttonRunning;
static void button_finished(void) { buttonRunning = 0; }
static int inside(cJSON *p, int x, int y) { int px = num(p, "X", 0), py = num(p, "Y", 0); return x >= px && y >= py && x < px + num(p, "Width", 16) && y < py + num(p, "Height", 16); }

int u6_mouse_down(int x, int y)
{
    if (!on) return 0;
    if (input_click()) return 1; /* a picture picked */
    if (scene_click(x, y)) return 1; /* a scene: its choice, or going on */
    if (title_click(x, y)) return 1; /* the title menu */
    if (!hasHud) return 0;
    cJSON *p;
    /* the command buttons */
    cJSON_ArrayForEach(p, get(hud, "Pieces"))
    {
        int kind = num(p, "Kind", -1);
        if ((kind != P_COMMAND && kind != P_CUSTOM) || !piece_shown(p) || !inside(piece_now(p), x, y)) continue;
        if (kind == P_COMMAND) { start_verb(num(p, "Verb", 0)); return 1; }
        /* a custom button (U6GameWindow HudButtonClicked): its event once - again once it has finished */
        cJSON *commands = get(cJSON_GetArrayItem(get(p, "EventPages"), 0), "Commands");
        if (!buttonRunning && cJSON_GetArraySize(commands) > 0) { buttonRunning = 1; events_run_commands_then(commands, 0, button_finished); }
        return 1;
    }
    if (verb < 0 && combat_mode_clicked(x, y)) return 1; /* a member's combat mode, round the list */
    /* the member's name or portrait: portrait / inventory */
    if (verb < 0)
        cJSON_ArrayForEach(p, get(hud, "Pieces"))
        {
            int kind = num(p, "Kind", -1);
            if ((kind == P_MEMBERNAME || kind == P_PORTRAIT) && (view_of(kind) == VIEW_ALWAYS || view_of(kind) == panelView) && inside(p, x, y)) { toggle_view(); return 1; }
        }
    /* the panel: a slot or a carried thing (Look when no verb), a member's name in the party view */
    cJSON *panel = first(P_PANEL);
    if (panel && inside(panel, x, y))
    {
        if (partyView)
        {
            int line = (y - num(panel, "Y", 0)) / line_height(font_get("InGameFont"));
            int ids[16]; if (line >= 0 && line < party_list(ids, 16)) show_member(line);
            return 1;
        }
        Cell cells[96]; int n = panel_cells(cells, 96);
        for (int i = 0; i < n; i++)
            if (x >= cells[i].x && y >= cells[i].y && x < cells[i].x + cells[i].w && y < cells[i].y + cells[i].h)
            {
                panelCursor = i; focus = FOCUS_PANEL;
                if (verb < 0) start_verb(3);
                pick_panel_cell(&cells[i]);
                return 1;
            }
        return 0;
    }
    if (!u6_in_view(x, y)) return 1; /* the HUD's frame: nothing */
    double ox, oy; world_camera_origin(&ox, &oy);
    int cx = (int)((x + ox) / world_tile_w()), cy = (int)((y + oy) / world_tile_h());
    if (verb >= 0) { pick_map_cell(cx, cy, -1, 0); return 1; }
    /* a click on bare ground (nothing there): Look names the tile - the engine's own clicks see only things */
    int actor, uid; world_hit(x, y, &actor, &uid);
    if (actor < 0 && !uid)
    {
        char ground[128];
        if (world_death_blood_at(cx, cy)) snprintf(ground, sizeof ground, "%s", word("Blood")); /* a death's blood */
        else if (!tile_name(cx, cy, ground, sizeof ground)) snprintf(ground, sizeof ground, "%s", word("Nothing"));
        say_look(ground);
    }
    return 0; /* the engine's own clicks (things: look, use; walking) */
}

void u6_click_look(int actor, int uid)
{
    if (verb >= 0) return;
    char name[128];
    thing_name_of(actor, uid, name, sizeof name);
    say_look(name);
    looked = actor > 0 ? world_actor_character(actor) : -1;
}

void u6_multi_use(int actor, int uid)
{
    if (verb >= 0 || vitals_incapacitated(player_char())) return;
    if (actor == 0) return;
    int cx, cy, f;
    if (actor > 0)
    {
        world_actor_cell(actor, &cx, &cy, &f);
        int cid = world_actor_character(actor);
        int foe = combat_mode() && !items_in_party(cid) && !vitals_dead(cid);
        verb = foe ? 0 : 2; snprintf(prompt, sizeof prompt, "%s-", word(VERBS[verb]));
        pick_map_cell(cx, cy, actor, 0);
        return;
    }
    if (!uid) return;
    int c, i, q, n; world_thing_info(uid, &c, &i, &q, &cx, &cy, &n);
    verb = combat_mode() ? 0 : 7; snprintf(prompt, sizeof prompt, "%s-", word(VERBS[verb]));
    pick_map_cell(cx, cy, -1, uid);
}

int u6_can_drag(int cellX, int cellY)
{
    if (!on) return 1;
    int px, py, pf; world_player_cell(&px, &py, &pf);
    return abs(cellX - px) <= 1 && abs(cellY - py) <= 1;
}

/* ---- The cursors, drawn under the HUD (the map's) and over it (the panel's) ---- */
static void outline(int x, int y, int w, int h, uint32_t c)
{
    fill_rect(x, y, w, 1, c); fill_rect(x, y + h - 1, w, 1, c); fill_rect(x, y, 1, h, c); fill_rect(x + w - 1, y, 1, h, c);
}

static void draw_cursors(int over, uint32_t ms)
{
    if (!over && verb >= 0 && focus == FOCUS_MAP)
    {
        double ox, oy; world_camera_origin(&ox, &oy);
        int tw = world_tile_w(), th = world_tile_h(), sx = (int)(mapCurX * tw - ox), sy = (int)(mapCurY * th - oy);
        cJSON *link = get(get(get(get(project(), "Settings"), "MousePointers"), "U8Arrows"), "U6.Crosshair");
        const Sprite *cross = sprite_of(link);
        const Picture *p = cross ? sprite_picture_at(cross, ms) : NULL;
        if (p) blit_fill(p, sx + (tw - p->width) / 2, sy + (th - p->height) / 2, p->width, p->height, 0);
        else outline(sx, sy, tw, th, 0xFFFFFF00);
    }
    if (over && focus == FOCUS_PANEL && !partyView)
    {
        Cell cells[96]; int n = panel_cells(cells, 96);
        if (n == 0) return;
        if (panelCursor >= n) panelCursor = n - 1;
        const Cell *c = &cells[panelCursor < 0 ? 0 : panelCursor];
        outline(c->x, c->y, c->w, c->h, 0xFFFFFF00);
    }
}

/* ======================= Talking (U6GameWindow.U6Talk.cs) ======================= */

static char talkTyped[48];
#define MORE_MAX 64
static char talkMore[MORE_MAX][192]; static int moreCount, moreAt, moreFit = 4;
static uint32_t talkEndAt;

int u6_talking(void) { return hasHud && textPiece != NULL; }
static int u6_talk_more_waiting(void) { return moreAt < moreCount; }

/* Each word of a keyword (a reply's text) marked where it stands whole in the words, case ignored (MarkKeywords), longest first. */
static void mark_keywords(const char *text, const char *const *replies, int count, char *out, size_t size)
{
    snprintf(out, size, "%s", text);
    int order[64]; int n = count < 64 ? count : 64;
    for (int i = 0; i < n; i++) order[i] = i;
    for (int i = 0; i < n; i++) for (int j = i + 1; j < n; j++) if (strlen(replies[order[j]]) > strlen(replies[order[i]])) { int t = order[i]; order[i] = order[j]; order[j] = t; }
    char *work = (char *)malloc(size);
    if (!work) return;
    for (int r = 0; r < n; r++)
    {
        const char *k = replies[order[r]];
        size_t kl = strlen(k);
        if (!kl) continue;
        size_t at = 0;
        for (;;)
        {
            size_t len = strlen(out), found = (size_t)-1;
            for (size_t i = at; i + kl <= len; i++)
            {
                size_t j = 0;
                while (j < kl && tolower((unsigned char)out[i + j]) == tolower((unsigned char)k[j])) j++;
                if (j == kl) { found = i; break; }
            }
            if (found == (size_t)-1) break;
            int startOk = found == 0 || !isalnum((unsigned char)out[found - 1]);
            int endOk = found + kl >= len || !isalnum((unsigned char)out[found + kl]);
            if (!startOk || !endOk || (found > 0 && out[found - 1] == '\1')) { at = found + kl; continue; }
            size_t w = (size_t)snprintf(work, size, "%.*s", (int)found, out);
            const char *s = out + found, *end = out + found + kl;
            while (s < end && w + 4 < size)
            {
                size_t wl = strcspn(s, " ");
                if (s + wl > end) wl = (size_t)(end - s);
                w += (size_t)snprintf(work + w, size - w, "\1%.*s\2", (int)wl, s);
                s += wl;
                if (s < end && *s == ' ') { work[w++] = ' '; s++; }
            }
            size_t markedEnd = w;
            if (w < size) snprintf(work + w, size - w, "%s", end);
            snprintf(out, size, "%s", work);
            at = markedEnd;
        }
    }
    free(work);
}

static void more_add(const char *line) { if (moreCount < MORE_MAX) snprintf(talkMore[moreCount++], sizeof talkMore[0], "%s", line); }

static void talk_prompt(void)
{
    if (u6_talk_more_waiting()) { prompt[0] = 0; return; }
    snprintf(prompt, sizeof prompt, "%s", dialog_reply_count() ? word(typing_replies() ? "YouSay" : "PickReply") : "");
}

/* Picking the answer from a list (Conversation Style): the replies listed while asked. */
static int picking_replies(void) { return talkStyle && strcmp(style("Answers"), "Typed keywords (Ultima 6)") && dialog_showing() && dialog_reply_count() > 0 && !u6_talk_more_waiting(); }

/* The replies listed (pick from a list): "1. Name" ..., the one picked marked; how many lines. */
static int reply_list_lines(char out[][192], int max)
{
    if (!picking_replies()) return 0;
    int n = dialog_reply_count();
    for (int i = 0; i < n && i < max; i++) snprintf(out[i], 192, "%s%d. %s", i == replyPick ? ">" : " ", i + 1, dialog_reply_text(i));
    return n < max ? n : max;
}

static int face_room(void)
{
    if (strcmp(style("Portrait"), "Inside the words' box") || !talkerFace) return 0;
    const Picture *p = sprite_picture_at(talkerFace, 0);
    return p ? p->width + 6 : 0;
}

/* The conversation's box over the screen: its colour or picture, the portrait if it goes there, the words, the replies, the prompt. */
static void draw_talk_box(uint32_t ms)
{
    if (!words_in_box()) return;
    int bx = num(talkStyle, "BoxX", 8), by = num(talkStyle, "BoxY", 120), bw = num(talkStyle, "BoxWidth", 304), bh = num(talkStyle, "BoxHeight", 72);
    char hex[16]; snprintf(hex, sizeof hex, "%s%s", style("BoxColor")[0] == '#' ? "" : "#", style("BoxColor"));
    uint32_t bg = style("BoxColor")[0] ? colour_of(hex) : 0;
    fill_rect(bx, by, bw, bh, bg ? bg : 0xFF000000u);
    const Sprite *picture = sprite_of(get(talkStyle, "BoxSprite"));
    if (picture) blit_fill(sprite_picture_at(picture, ms), bx, by, bw, bh, 0);
    if (face_room() && talkerFace) { const Picture *p = sprite_picture_at(talkerFace, ms); if (p) draw_sprite_fit(talkerFace, bx + 4, by + 4, p->width, p->height < bh - 8 ? p->height : bh - 8, ms); }
    int x, y, w, h; box_words_rect(&x, &y, &w, &h);
    Font *f = box_font(); int lh = line_height(f);
    char tc[16]; snprintf(tc, sizeof tc, "%s%s", style("TextColor")[0] == '#' ? "" : "#", style("TextColor"));
    uint32_t colour = style("TextColor")[0] ? colour_of(tc) : 0;
    static char shown[64][192]; int count = 0;
    for (int i = 0; i < boxLineCount && count < 64; i++) snprintf(shown[count++], 192, "%s", boxLines[i]);
    count += reply_list_lines(shown + count, 64 - count);
    if ((prompt[0] || u6_talk_more_waiting()) && count < 64) snprintf(shown[count++], 192, "%s_", prompt);
    int fit = h / lh; if (fit < 1) fit = 1;
    for (int i = count > fit ? count - fit : 0, line = 0; i < count; i++, line++) draw_marked(f, x, y + line * lh, shown[i], colour);
}

static void close_talk_box(void) { talkBoxOpen = 0; boxLineCount = 0; }

void u6_talk_show(int speaker, const char *text, const Sprite *face, const char *const *replies, int replyCount)
{
    talkEndAt = 0;
    if (speaker >= 0 && speaker != player_char()) /* someone else speaks: their face and name in the panel */
    {
        talking = 1; talkerFace = face;
        snprintf(talkerName, sizeof talkerName, "%s", char_name(speaker));
    }
    talkTyped[0] = 0;
    moreCount = moreAt = 0;
    prompt[0] = 0;
    replyPick = 0;
    if (talkStyle && !strcmp(style("Words"), "In a box of their own (below)") && !talkBoxOpen) { talkBoxOpen = 1; boxLineCount = 0; } /* the words in a box of their own */
    if (talkStyle && cJSON_IsFalse(get(talkStyle, "MarkKeywords"))) replyCount = 0; /* keywords not marked */
    /* the words wrapped to the text area; more than it holds: a screenful, then the cursor waits for a key (Ultima 6's scroll) */
    static char trimmed[2000], marked[4000];
    const char *t = text ? text : "";
    while (*t == ' ' || *t == '\n' || *t == '\r') t++;
    snprintf(trimmed, sizeof trimmed, "%s", t);
    size_t len = strlen(trimmed);
    while (len && (trimmed[len - 1] == ' ' || trimmed[len - 1] == '\n' || trimmed[len - 1] == '\r')) trimmed[--len] = 0;
    if (len)
    {
        mark_keywords(trimmed, replies, replyCount, marked, sizeof marked);
        Font *f = words_in_box() ? box_font() : font_of(textPiece);
        int bx, by, bw, bh; if (words_in_box()) box_words_rect(&bx, &by, &bw, &bh); else { bw = num(textPiece, "Width", 16); bh = num(textPiece, "Height", 16); }
        moreFit = bh / line_height(f) - 1;
        if (moreFit < 1) moreFit = 1;
        const char *p = marked;
        while (*p)
        {
            size_t n = strcspn(p, "\n");
            char para[1200];
            snprintf(para, sizeof para, "%.*s", (int)n, p);
            size_t k = 0; for (size_t i = 0; para[i]; i++) if (para[i] != '\r') para[k++] = para[i];
            para[k] = 0;
            wrap(f, para, bw, more_add);
            p += n; if (*p == '\n') p++;
        }
        for (int i = 0; i < moreFit && moreAt < moreCount; i++) u6_say(talkMore[moreAt++]);
    }
    talk_prompt();
}

/* The talk is over once no words have come for a moment and nothing is asked: the panel goes back to the member. */
static void talk_tick(uint32_t ms)
{
    if (!talking) return;
    if (dialog_showing()) { talkEndAt = 0; return; }
    if (!talkEndAt) { talkEndAt = ms + 300; return; }
    if (ms < talkEndAt) return;
    talking = 0; talkerFace = NULL; talkerName[0] = 0; talkTyped[0] = 0; prompt[0] = 0; talkEndAt = 0;
    close_talk_box();
}

/* Picking from the list: Up / Down, 1-9, Enter. 1 when the key was the list's. */
static int reply_pick_key(const PlatformEvent *e)
{
    if (!picking_replies()) return 0;
    int n = dialog_reply_count(), pick = -1;
    const char *k = e->keyName;
    if (k[0] == 'D' && k[1] >= '1' && k[1] <= '9' && !k[2] && !(typing_replies() && talkTyped[0])) pick = k[1] - '1';
    else if (!strncmp(k, "NumPad", 6) && k[6] >= '1' && k[6] <= '9') pick = k[6] - '1';
    else if (!strcmp(k, "Up")) { replyPick = (replyPick + n - 1) % n; return 1; }
    else if (!strcmp(k, "Down")) { replyPick = (replyPick + 1) % n; return 1; }
    else if (!strcmp(k, "Enter") && !(typing_replies() && talkTyped[0])) pick = replyPick;
    if (pick < 0 || pick >= n) return !typing_replies(); /* list only: nothing else */
    char line[200]; snprintf(line, sizeof line, "%s%s", word("YouSay"), dialog_reply_text(pick)); u6_say(line);
    talkTyped[0] = 0; prompt[0] = 0; replyPick = 0;
    dialog_pick_reply(pick);
    return 1;
}

/* A click on a listed reply (in the text area or the box). 1 taken. */
static int reply_clicked(int x, int y)
{
    if (!picking_replies()) return 0;
    int tx, ty, tw, th; Font *f;
    if (words_in_box()) { box_words_rect(&tx, &ty, &tw, &th); f = box_font(); }
    else { tx = num(textPiece, "X", 0); ty = num(textPiece, "Y", 0); tw = num(textPiece, "Width", 16); th = num(textPiece, "Height", 16); f = font_of(textPiece); }
    if (x < tx || y < ty || x >= tx + tw || y >= ty + th) return 0;
    int lh = line_height(f), fit = th / lh, n = dialog_reply_count();
    int history = words_in_box() ? boxLineCount : lineCount, promptLines = prompt[0] ? 1 : 0;
    int total = history + n + promptLines, first = total > fit ? total - fit : 0;
    int clicked = first + (y - ty) / lh - history;
    if (clicked < 0 || clicked >= n) return 0;
    replyPick = clicked;
    PlatformEvent enter; memset(&enter, 0, sizeof enter); enter.type = EV_KEY_DOWN; snprintf(enter.keyName, sizeof enter.keyName, "Enter");
    talkTyped[0] = 0;
    reply_pick_key(&enter);
    return 1;
}

/* Ultima 6 compares the first four letters: "jobs" asks about "job", "nam" doesn't ask about "name". */
static int keyword_matches(const char *keyword, const char *typed)
{
    while (*keyword == ' ') keyword++;
    while (*typed == ' ') typed++;
    size_t kl = strlen(keyword); while (kl && keyword[kl - 1] == ' ') kl--;
    size_t tl = strlen(typed); while (tl && typed[tl - 1] == ' ') tl--;
    if (!kl) return 0;
    size_t n = kl < 4 ? kl : 4;
    if (tl < n) return 0;
    for (size_t i = 0; i < n; i++) if (tolower((unsigned char)keyword[i]) != tolower((unsigned char)typed[i])) return 0;
    return 1;
}

static void answer_typed(void)
{
    char typed[48]; snprintf(typed, sizeof typed, "%s", talkTyped);
    char *t = typed; while (*t == ' ') t++;
    size_t tl = strlen(t); while (tl && t[tl - 1] == ' ') t[--tl] = 0;
    char line[200]; snprintf(line, sizeof line, "%s%s", word("YouSay"), t); u6_say(line);
    talkTyped[0] = 0;
    snprintf(prompt, sizeof prompt, "%s", word("YouSay"));
    char asked[48]; snprintf(asked, sizeof asked, "%s", t[0] ? t : word("Bye"));
    int n = dialog_reply_count();
    for (int i = 0; i < n; i++)
        if (keyword_matches(dialog_reply_text(i), asked)) { prompt[0] = 0; dialog_pick_reply(i); return; }
    u6_say(wordf("NoHelp", asked, dialog_speaker() >= 0 ? char_name(dialog_speaker()) : ""));
}

int u6_talk_key(const PlatformEvent *e)
{
    if (!u6_talking() || !dialog_showing() || e->type != EV_KEY_DOWN) return 0;
    if (u6_talk_more_waiting())
    {
        for (int i = 0; i < moreFit && moreAt < moreCount; i++) u6_say(talkMore[moreAt++]); /* the next screenful */
        talk_prompt();
        return 1;
    }
    if (reply_pick_key(e)) return 1; /* Conversation Style: picked from a list */
    if (!dialog_reply_count())
    {
        if (key_is(e, "Accept") || key_is(e, "Accept2") || key_is(e, "Cancel")) dialog_go_on();
        return 1;
    }
    if (key_is(e, "Erase"))
    {
        size_t n = strlen(talkTyped); if (n) talkTyped[n - 1] = 0;
        snprintf(prompt, sizeof prompt, "%s%s", word("YouSay"), talkTyped);
        return 1;
    }
    if (key_is(e, "Accept")) { answer_typed(); return 1; }
    if (key_is(e, "Cancel")) { talkTyped[0] = 0; answer_typed(); return 1; } /* Escape: as nothing typed - bye */
    return 1; /* the letters come as text (u6_talk_text) */
}

int u6_talk_text(int ch)
{
    if (input_text(ch)) return 1; /* Scene Type Text */
    if (!u6_talking() || !dialog_showing()) return 0;
    if (u6_talk_more_waiting() || !dialog_reply_count() || !typing_replies()) return 1;
    if (ch < 32 || ch > 126 || strlen(talkTyped) >= 40) return 1;
    if (ch == ' ' && !talkTyped[0]) return 1; /* the Space that went on to this page */
    size_t n = strlen(talkTyped); talkTyped[n] = (char)ch; talkTyped[n + 1] = 0;
    snprintf(prompt, sizeof prompt, "%s%s", word("YouSay"), talkTyped);
    return 1;
}

/* ======================= Combat's words and the hit's spark (U6GameWindow.U6Combat / HitFlash) ======================= */

/* In view: the avatar and the party always; anyone else while they're in the Game View. */
static int in_view(int actor, int characterId)
{
    if (actor == 0 || characterId == player_char() || items_in_party(characterId)) return 1;
    return actor >= 0 && world_actor_in_sight(actor);
}

void u6_hit_spark(int actor)
{
    static const Sprite *spark; static int looked;
    if (!looked) { spark = sprite_first_in_group("Hit Spark"); looked = 1; } /* SpriteDatabase.HitSparkGroupName */
    if (!spark) return;
    int x, y, f; world_actor_cell(actor, &x, &y, &f);
    world_effect_once(spark, x, y);
}

/* "Rat lightly wounded." - or, hurt by anything not a blow, "Andrew lightly damaged by poison." */
void u6_tell_wound(int characterId, int actor, int hpLeft, int maxHp, int dying, const char *cause)
{
    if (!textPiece || !in_view(actor, characterId)) return;
    double left = hpLeft / (double)(maxHp > 0 ? maxHp : 1);
    const char *id = dying ? "Killed" : hpLeft <= 0 ? "KnockedOut" : left >= 0.75 ? "Barely" : left >= 0.5 ? "Lightly" : left >= 0.25 ? "Heavily" : "Critical";
    int blow = !cause || !cause[0] || !strncmp(cause, "slain by", 8);
    char byId[24]; snprintf(byId, sizeof byId, "%sBy", id);
    u6_say(blow ? wordf(id, char_name(characterId), "") : wordf(byId, char_name(characterId), cause));
}

void u6_tell_attack(int attacker, int defender, int hit)
{
    int a = world_actor_character(attacker), d = world_actor_character(defender);
    if (!textPiece || (!in_view(attacker, a) && !in_view(defender, d))) return;
    u6_say(wordf("Attacks", char_name(d), char_name(a)));
    if (!hit) u6_say(word("Missed"));
}

/* A character's own line (Character Editor > Verbose) in the text area: "Iolo: "Ouch!"" - 1 told (0: the engine barks it). */
int u6_says_line(int actor, int characterId, const char *line)
{
    if (!textPiece) return 0;
    if (in_view(actor, characterId)) u6_say(wordf("SaysLine", line, char_name(characterId)));
    return 1;
}

/* Any error, in the text area - its words editable (Words: Any error) - no window (U6GameWindow.Errors). 0: no text area. */
int u6_error(int number, const char *words)
{
    if (!on || !textPiece) return 0;
    char n[16]; snprintf(n, sizeof n, "%d", number);
    u6_say(wordf("ErrorLine", words, n));
    return 1;
}

/* Ultima 6's food (System Settings > Ultima 6 > Food): off - no hunger, no starving, no healing by the hour. */
int u6_food(void) { return on && !cJSON_IsTrue(get(get(get(project(), "Settings"), "Ultima6"), "UseUltima7Food")); }

/* Join Party with the party full (System Settings > Ultima 6: the most in it, the main character with them): refused, told. 1 refused. */
int u6_party_full(int characterId)
{
    if (!on) return 0;
    cJSON *size = get(get(get(project(), "Settings"), "Ultima6"), "PartySize");
    int most = cJSON_IsNumber(size) ? size->valueint : 9;
    if (most > 9) most = 9;
    if (most < 1) most = 1;
    int ids[16], n = party_list(ids, 16), alive = 0;
    for (int i = 0; i < n; i++) if (!vitals_dead(ids[i])) alive++;
    if (alive < most) return 0;
    u6_say(wordf("PartyFull", "", char_name(characterId)));
    return 1;
}

/* ======================= Solo mode (U6GameWindow.U6Solo; Nuvie Player::set_actor) ======================= */
/* 1-9 puts that member under the player's keys; the avatar and everyone else stay where they are (no one follows). 0 gives the
   avatar back and the party follows it again. The member walks, gets, uses, fights and casts; only the avatar talks. Here the
   avatar stays the world's actor 0 and the member is steered (a square at a time, the camera on it) - the same play. */

static int controlled_actor(void) { return controlled > 0 && !world_actor_gone(controlled) ? controlled : 0; }

static void follow_all(int on_)
{
    int ids[16], n = party_list(ids, 16);
    for (int i = 0; i < n; i++)
    {
        int a = world_actor_by_character(ids[i]);
        if (a > 0 && !vitals_dead(ids[i])) schedules_follow(a, on_ ? 0 : -1);
    }
}

static void take_control(int actor)
{
    controlled = actor;
    world_camera_follow(actor);
    heldCount = 0;
}

static void start_solo(int index)
{
    int ids[16], n = party_list(ids, 16);
    if (index >= n) return;
    int member = ids[index], actor = member == player_char() ? 0 : world_actor_by_character(member);
    if (vitals_dead(member) || vitals_incapacitated(member) || actor < 0) { u6_say(word("NotPossible")); return; }
    soloMode = 1;
    follow_all(0); /* the party stays where it is */
    take_control(actor);
    hudMember = member;
    u6_say(wordf("Solo", "", char_name(member)));
}

static void end_solo(void)
{
    take_control(0); /* the avatar is moved again */
    soloMode = 0;
    hudMember = -1;
    follow_all(1);
    u6_say(word("PartyMode"));
}

/* Each frame: the controlled member steered by the walking keys (1: the keys were its - the avatar stands); the one controlled dying: the avatar back. */
int u6_solo_step(int dx, int dy)
{
    if (!on || controlled <= 0) return 0;
    int member = world_actor_character(controlled);
    if (world_actor_gone(controlled) || vitals_dead(member)) { if (soloMode) end_solo(); else take_control(0); return 0; }
    if ((dx || dy) && !world_walking(controlled))
    {
        int x, y, f; world_actor_cell(controlled, &x, &y, &f);
        world_walk_to(controlled, x + dx, y + dy, f, 0);
    }
    return 1;
}

/* ======================= Combat modes (U6GameWindow.U6CombatModes; Nuvie combat_mode_tbl) ======================= */

static const char *MODES[7] = { "Command", "Front", "Rear", "Flank", "Berserk", "Retreat", "Assault" };

/* A character's mode: the avatar Command; a member its own, Front till set. */
static const char *mode_of(int characterId)
{
    if (characterId == player_char()) return "Command";
    const char *m = str(character_json(characterId), "U6CombatMode");
    for (int i = 0; i < 7; i++) if (!strcmp(m, MODES[i])) return MODES[i];
    return "Front";
}

/* The fighting itself is the engine's, by the matching behaviour (AttackMode: Nearest 0, Strongest 2, Berserk 3, Defend 5, Flank 6, Flee 7). */
static void set_mode(int characterId, const char *mode)
{
    cJSON *ch = character_json(characterId);
    if (!ch) return;
    int attack = !strcmp(mode, "Rear") ? 5 : !strcmp(mode, "Flank") ? 6 : !strcmp(mode, "Berserk") ? 3 : !strcmp(mode, "Retreat") ? 7 : !strcmp(mode, "Assault") ? 2 : 0;
    cJSON_DeleteItemFromObject(ch, "U6CombatMode"); cJSON_AddStringToObject(ch, "U6CombatMode", mode);
    cJSON_DeleteItemFromObject(ch, "AttackMode"); cJSON_AddNumberToObject(ch, "AttackMode", attack);
}

static void init_modes(void) { int ids[16], n = party_list(ids, 16); for (int i = 0; i < n; i++) set_mode(ids[i], mode_of(ids[i])); }

/* "COMMAND", as the words have it. */
static const char *mode_word(int characterId) { char id[24]; snprintf(id, sizeof id, "Mode%s", mode_of(characterId)); return word(id); }

/* A click on the panel's combat mode: the next mode (not the avatar's). 1 taken. */
static int combat_mode_clicked(int x, int y)
{
    cJSON *piece = first(P_COMBATMODE);
    if (!piece) return 0;
    int px = num(piece, "X", 0), py = num(piece, "Y", 0);
    if (x < px || y < py || x >= px + num(piece, "Width", 16) || y >= py + num(piece, "Height", 16)) return 0;
    int member = member_shown();
    if (member == player_char()) return 1; /* the avatar is always Command */
    const char *now = mode_of(member);
    int i = 0; while (i < 7 && strcmp(MODES[i], now)) i++;
    set_mode(member, MODES[(i + 1) % 7]);
    return 1;
}

/* ======================= Combat turn by turn (U6GameWindow.U6Combat) ======================= */
/* Outside combat mode the world is live; the combat key makes it turn by turn: the world waits for the avatar - a step, a verb or
   Pass (Space) plays one round (the party and the foes act in it), then "Andrew:" asks for the next. */

#define ROUND_MS 700
#define ATTACK_ROUND_MS 1100
#define STEP_ROUND_MS 600

static int turn_by_turn(void) { return hasHud && combat_mode(); }

static void ask_turn(void)
{
    if (!turn_by_turn() || verb >= 0) return;
    snprintf(prompt, sizeof prompt, "%s", wordf("TurnOf", "", char_name(world_actor_character(controlled_actor()))));
}

static void start_round(double ms, int attack)
{
    if (!turn_by_turn()) return;
    turnRunMs = ms; turnIsAttack = attack;
    prompt[0] = 0;
}

/* Turn by turn: a Command member waits for the player's turn rather than fighting by itself (WaitsForCommand). */
int u6_waits_for_command(int actor)
{
    if (!on || !turn_by_turn() || soloMode || actor <= 0 || actor == controlled) return 0;
    int id = world_actor_character(actor);
    return items_in_party(id) && !strcmp(mode_of(id), "Command");
}

/* The round is over: the next Command member (in party order, round again) is the one moved (NextCommander). */
static void next_commander(void)
{
    if (!turn_by_turn() || soloMode) return;
    int ids[16], n = party_list(ids, 16), commanders[16], c = 0;
    for (int i = 0; i < n; i++)
    {
        int a = ids[i] == player_char() ? 0 : world_actor_by_character(ids[i]);
        if (a < 0 || vitals_dead(ids[i]) || vitals_incapacitated(ids[i]) || strcmp(mode_of(ids[i]), "Command")) continue;
        commanders[c++] = a;
    }
    if (c == 0) return;
    int now = controlled_actor(), at = 0;
    for (int i = 0; i < c; i++) if (commanders[i] == now) at = i;
    int next = c == 1 ? commanders[0] : commanders[(at + 1) % c];
    if (next != now) { take_control(next); hudMember = next > 0 ? world_actor_character(next) : -1; }
}

static void end_round(void)
{
    turnRunMs = 0; turnIsAttack = 0; heldCount = 0; turnStepFromX = -99999;
    next_commander(); /* the next Command member's turn (U6CombatModes) */
    ask_turn();
}

/* Combat over: the avatar is moved again (unless in solo mode) and the party follows it (CombatOverTakeAvatar). */
static void combat_over_take_avatar(void)
{
    if (soloMode || controlled <= 0) return;
    take_control(0);
    hudMember = -1;
    follow_all(1);
}

/* Each frame: 1 while the world waits for the avatar's turn (nothing in it moves). */
int u6_turn_frozen(double ms)
{
    if (!turn_by_turn()) { turnRunMs = 0; turnStepFromX = -99999; return 0; }
    if (turnRunMs <= 0) { if (!prompt[0] && verb < 0 && !askAnswer && !dialog_showing()) ask_turn(); return 1; }
    if (turnStepFromX != -99999)
    {
        int px, py, pf; world_player_cell(&px, &py, &pf);
        if (px != turnStepFromX || py != turnStepFromY) { heldCount = 0; turnStepFromX = -99999; } /* one cell a step */
    }
    turnRunMs -= ms;
    if (turnRunMs <= 0) end_round();
    return 0;
}

/* The avatar may strike only on its Attack turn (Ultima 6's Command mode). */
int u6_player_may_strike(void) { return !turn_by_turn() || turnIsAttack; }

static int turn_step(int dx, int dy)
{
    if (!turn_by_turn()) return 0;
    if (turnRunMs > 0) return 1; /* the round is still being played */
    held_add(dx, dy);
    int pf; world_player_cell(&turnStepFromX, &turnStepFromY, &pf);
    start_round(STEP_ROUND_MS, 0);
    return 1;
}

static void pass_turn_if_fighting(int attack) { if (turn_by_turn() && turnRunMs <= 0) start_round(attack ? ATTACK_ROUND_MS : ROUND_MS, attack); }

static int turn_pass(void)
{
    if (!turn_by_turn() || turnRunMs > 0) return turn_by_turn();
    u6_say(word("Pass"));
    start_round(ROUND_MS, 0);
    return 1;
}

/* ======================= The pointer (U6GameWindow.Pointer U6PointerKey; Nuvie MapWindow::update_mouse_cursor) ======================= */

/* The arrow over the HUD and the view's edge; the crosshairs on the avatar or while a verb waits for its target; else an arrow from
   the avatar toward the mouse. While a verb aims at the map, the map's cursor follows the mouse and no pointer is drawn over it.
   Drawn here: 1 (0 - no HUD: the engine's own pointer). */
int u6_pointer(uint32_t *pixels, int x, int y, uint32_t ms)
{
    if (!hasHud) return 0;
    static const char *DIRS[8] = { "North", "NorthEast", "East", "SouthEast", "South", "SouthWest", "West", "NorthWest" };
    char key[24] = "U6.Arrow";
    int inView = x >= vx + 1 && y >= vy + 1 && x < vx + vw - 1 && y < vy + vh - 1;
    if (inView && gumps_dragged_map_thing() == 0)
    {
        double ox, oy; world_camera_origin(&ox, &oy);
        int cx = (int)floor((x + ox) / world_tile_w()), cy = (int)floor((y + oy) / world_tile_h());
        int px, py, pf; world_player_cell(&px, &py, &pf);
        if (verb >= 0 || (cx == px && cy == py)) snprintf(key, sizeof key, "U6.Crosshair");
        else
        {
            int sector = ((int)floor(atan2((double)(cx - px), (double)-(cy - py)) / (3.14159265358979323846 / 4) + 0.5) % 8 + 8) % 8; /* 0 north, clockwise */
            snprintf(key, sizeof key, "U6.%s", DIRS[sector]);
        }
        if (verb >= 0 && focus == FOCUS_MAP) { mapCurX = cx; mapCurY = cy; return 1; } /* aiming: one cross - the map cursor */
    }
    screen = pixels;
    const Sprite *s = sprite_of(get(get(get(get(project(), "Settings"), "MousePointers"), "U8Arrows"), key));
    const Picture *p = s ? sprite_picture_at(s, ms) : NULL;
    if (p) blit_fill(p, x - s->originX, y - s->originY, p->width, p->height, 0);
    else fill_rect(x, y, 16, 16, 0xFF000000u); /* not drawn yet: a black square, never another pointer */
    return 1;
}

/* A game built afresh (started, loaded): nothing of the last one's play is held - a verb, a bag, a talk, the text area. */
static void u6_reset_play(void)
{
    verb = -1; focus = FOCUS_MAP; hudMember = -1; partyView = 0; looked = -1; panelView = VIEW_INVENTORY;
    openBag = NULL; pickedItem = NULL; pickedOwner = -1; pickedThing = 0; heldCount = 0;
    talking = 0; talkerFace = NULL; talkerName[0] = 0; talkTyped[0] = 0; moreCount = moreAt = 0;
    askAnswer = NULL; prompt[0] = 0; lineCount = 0; heardOn = 0;
    turnRunMs = 0; turnIsAttack = 0; turnStepFromX = -99999;
    controlled = 0; soloMode = 0;
    casting = 0; aimSpell = NULL; aimCaster = -1;
    frameOverride = NULL; end_scene();
    if (talkStyle) { cJSON_Delete(talkStyle); talkStyle = NULL; }
    close_talk_box(); replyPick = 0;
}

/* A click while a talk is up: a listed reply picked (Conversation Style). 1 taken. */
int u6_talk_click(int x, int y) { return on && hasHud && reply_clicked(x, y); }

/* A save keeps the frame in play (GameSave.U6HudFrame) and the conversations' style (U6TalkStyle); a load puts them back. */
void u6_save(cJSON *runtime)
{
    if (!on) return;
    if (frameOverride)
    {
        cJSON *f = cJSON_AddObjectToObject(runtime, "U6HudFrame");
        cJSON_AddStringToObject(f, "SpriteGroup", frameOverride->group);
        cJSON_AddStringToObject(f, "SpriteName", frameOverride->name);
    }
    if (talkStyle) cJSON_AddItemToObject(runtime, "U6TalkStyle", cJSON_Duplicate(talkStyle, 1));
    if (hudStateCount)
    {
        cJSON *list = cJSON_AddArrayToObject(runtime, "U6HudPieces");
        for (int i = 0; i < hudStateCount; i++)
        {
            HudState *s = &hudStates[i]; cJSON *o = cJSON_CreateObject();
            cJSON_AddNumberToObject(o, "Id", s->id);
            if (s->shown >= 0) cJSON_AddBoolToObject(o, "Shown", s->shown);
            if (s->hasX) cJSON_AddNumberToObject(o, "X", s->x);
            if (s->hasY) cJSON_AddNumberToObject(o, "Y", s->y);
            if (s->name[0]) { cJSON *l = cJSON_AddObjectToObject(o, "Sprite"); cJSON_AddStringToObject(l, "SpriteGroup", s->group); cJSON_AddStringToObject(l, "SpriteName", s->name); }
            if (s->text) cJSON_AddStringToObject(o, "Text", s->text);
            cJSON_AddBoolToObject(o, "KeyOff", s->keyOff);
            cJSON_AddItemToArray(list, o);
        }
    }
}

void u6_restore(cJSON *runtime)
{
    if (!on) return;
    cJSON *f = get(runtime, "U6HudFrame");
    frameOverride = f && str(f, "SpriteName")[0] ? sprite_find(str(f, "SpriteGroup"), str(f, "SpriteName")) : NULL;
    if (talkStyle) { cJSON_Delete(talkStyle); talkStyle = NULL; }
    if (cJSON_IsObject(get(runtime, "U6TalkStyle"))) talkStyle = cJSON_Duplicate(get(runtime, "U6TalkStyle"), 1);
    hud_states_clear(); /* the HUD's pieces as they were (GameSave.U6HudPieces) */
    cJSON *o;
    cJSON_ArrayForEach(o, get(runtime, "U6HudPieces"))
    {
        HudState *s = hud_state(num(o, "Id", -1), 1);
        if (!s) break;
        cJSON *v = get(o, "Shown"); if (cJSON_IsBool(v)) s->shown = cJSON_IsTrue(v);
        if (cJSON_IsNumber(get(o, "X"))) { s->hasX = 1; s->x = num(o, "X", 0); }
        if (cJSON_IsNumber(get(o, "Y"))) { s->hasY = 1; s->y = num(o, "Y", 0); }
        cJSON *l = get(o, "Sprite");
        if (cJSON_IsObject(l) && str(l, "SpriteName")[0]) { snprintf(s->group, sizeof s->group, "%s", str(l, "SpriteGroup")); snprintf(s->name, sizeof s->name, "%s", str(l, "SpriteName")); }
        if (cJSON_IsString(get(o, "Text"))) s->text = strdup(str(o, "Text"));
        s->keyOff = cJSON_IsTrue(get(o, "KeyOff"));
        hud_state_rebuild(s);
    }
}

/* ======================= Levels (Engine U6Leveling, U6GameWindow.U6Levels) ======================= */

enum { BK_STRENGTH = 1, BK_DEXTERITY = 2, BK_INTELLIGENCE = 3, BK_HITS = 6, BK_LEVEL = 8, BK_EXP = 9, BK_NEXTEXP = 11 }; /* BuiltInStatKind */

static cJSON *u6settings(void) { return get(get(project(), "Settings"), "Ultima6"); }
int u6_levels_on(void) { return on && !cJSON_IsTrue(get(u6settings(), "UseUltima7Leveling")); }

/* The Leveling Editor's system in use (none: levels are only what events set) - U6Leveling.Table's numbers from it. */
static cJSON *level_system(void)
{
    const char *active = str(u6settings(), "ActiveLevelSystem");
    cJSON *s;
    cJSON_ArrayForEach(s, get(u6settings(), "LevelSystems")) if (!strcmp(str(s, "Name"), active)) return s;
    return NULL;
}
static int max_level(void) { cJSON *s = level_system(); return (s ? cJSON_GetArraySize(get(s, "LevelExperience")) : 7) + 1; }
static int needs(int level)
{
    cJSON *s = level_system();
    static const int DEFAULT[] = { 100, 200, 400, 800, 1600, 3200, 6400 };
    if (level < 1 || level >= max_level()) return 0x7FFFFFFF;
    if (!s) return DEFAULT[level - 1];
    cJSON *e = cJSON_GetArrayItem(get(s, "LevelExperience"), level - 1);
    return cJSON_IsNumber(e) ? e->valueint : 0x7FFFFFFF;
}
static int hits_per_level(void) { cJSON *s = level_system(); int h = s ? num(s, "HitsPerLevel", 30) : 30; return h < 0 ? 0 : h; }
static int max_hits(int level)
{
    cJSON *s = level_system();
    int most = s ? num(s, "MostHits", 255) : 255, per = hits_per_level(), top = max_level();
    if (most < 1) most = 1;
    if (per < 1) per = 1;
    if (level < 1) level = 1;
    if (level > top) level = top;
    return level * per < most ? level * per : most;
}
static int has_earned(int level, int exp) { return level >= 1 && level < max_level() && exp > needs(level); }
static int next_exp(int level) { if (level < 1) level = 1; return level < max_level() ? needs(level) + 1 : 0; }

/* A character's built-in stat (Stat Editor: the stat of that kind), as its StatValues keep it. */
static cJSON *stat_of_kind(int kind) { cJSON *s; cJSON_ArrayForEach(s, get(get(project(), "Stats"), "Stats")) if (num(s, "Kind", -1) == kind) return s; return NULL; }
static int stat(int characterId, int kind, int fallback)
{
    cJSON *s = stat_of_kind(kind);
    if (!s) return fallback;
    char key[16]; snprintf(key, sizeof key, "%d", num(s, "Id", -1));
    const char *v = str(get(character_json(characterId), "StatValues"), key);
    return v[0] ? atoi(v) : fallback;
}
static void set_stat(int characterId, int kind, int value)
{
    cJSON *s = stat_of_kind(kind), *ch = character_json(characterId);
    if (!s || !ch) return;
    cJSON *values = get(ch, "StatValues");
    if (!values) values = cJSON_AddObjectToObject(ch, "StatValues");
    char key[16], text[16]; snprintf(key, sizeof key, "%d", num(s, "Id", -1)); snprintf(text, sizeof text, "%d", value);
    cJSON_DeleteItemFromObject(values, key);
    cJSON_AddStringToObject(values, key, text);
}

/* Ultima 6's most hit points: 30 a level (U6Actor::get_maxhp) - -1: not Ultima 6's levels (the engine's own). */
int u6_max_hp(int characterId)
{
    if (!u6_levels_on()) return -1;
    return max_hits(stat(characterId, BK_LEVEL, 1));
}

/* At the start (Nuvie's actor init: level = (hp + 29) / 30): more hit points than the level allows is that much higher a level. */
void u6_init_levels(void)
{
    if (!u6_levels_on()) return;
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Characters"), "Characters"))
    {
        int id = num(c, "Id", -1), top = max_level(), level = stat(id, BK_LEVEL, 1);
        if (level < 1) level = 1;
        if (level > top) level = top;
        int hp = stat(id, BK_HITS, -1);
        if (hp > max_hits(level)) { level = (hp + 29) / 30; if (level < 1) level = 1; if (level > top) level = top; }
        if (stat_of_kind(BK_LEVEL)) set_stat(id, BK_LEVEL, level);
        if (stat_of_kind(BK_NEXTEXP)) set_stat(id, BK_NEXTEXP, next_exp(level));
    }
}

/* One level up: the stats' points (never past the system's limit), hit points back (never past the new most), told if asked. */
static void rise_level(int characterId, int str_, int dex, int intel, int hitsBack, int tell)
{
    int top = max_level(), level = stat(characterId, BK_LEVEL, 1) + 1;
    if (level > top) level = top;
    set_stat(characterId, BK_LEVEL, level);
    set_stat(characterId, BK_NEXTEXP, next_exp(level));
    cJSON *s = level_system();
    int cap = s ? num(s, "StatCap", 30) : 30;
    if (cap < 1) cap = 1;
    char gains[160] = ""; int gainCount = 0;
    const int kinds[3] = { BK_STRENGTH, BK_DEXTERITY, BK_INTELLIGENCE }, by[3] = { str_, dex, intel };
    const char *names[3] = { "Strength", "Dexterity", "Intelligence" };
    char parts[3][48]; int partCount = 0;
    for (int k = 0; k < 3; k++)
    {
        if (!by[k]) continue;
        int was = stat(characterId, kinds[k], 0), now = was + by[k];
        if (now < 0) now = 0;
        if (now > cap) now = cap;
        set_stat(characterId, kinds[k], now);
        if (now != was) snprintf(parts[partCount++], sizeof parts[0], "%d %s", now - was, word(names[k]));
    }
    for (int k = 0; k < partCount; k++)
    {
        size_t at = strlen(gains);
        snprintf(gains + at, sizeof gains - at, "%s%s", k == 0 ? "" : partCount == 2 ? " and " : ", ", parts[k]);
        gainCount++;
    }
    int hp = stat(characterId, BK_HITS, -1);
    if (hp >= 0) { int most = max_hits(level), want = hp + (hitsBack > 0 ? hitsBack : 0); set_stat(characterId, BK_HITS, want < most ? want : most); }
    cJSON *ch = character_json(characterId);
    if (ch && num(ch, "U6LevelNoticed", 0) < level) { cJSON_DeleteItemFromObject(ch, "U6LevelNoticed"); cJSON_AddNumberToObject(ch, "U6LevelNoticed", level); }
    if (!tell) return;
    char lv[16]; snprintf(lv, sizeof lv, "%d", level);
    u6_say(wordf("NewLevel", lv, char_name(characterId)));
    if (gainCount) u6_say(wordf("Gains", gains, char_name(characterId)));
}

/* The variables a leveling event reads: LevelWho, LevelWhoId, LevelNow, LevelEarned, LevelRose. */
static void set_level_variables(int who, int earned, int rose)
{
    script_set("LevelWho", value_text(char_name(who)));
    script_set_number("LevelWhoId", who);
    script_set_number("LevelNow", stat(who, BK_LEVEL, 1));
    script_set_number("LevelEarned", earned);
    script_set_number("LevelRose", rose ? 1 : 0);
}

static int actor_of_char(int characterId) { int a = characterId >= 0 ? world_actor_by_character(characterId) : -1; return a >= 0 ? a : 0; }

/* The leveling system's events with this trigger, each run with the one it is about as its Self. */
static void level_events(const char *trigger, int who, int earned, int rose)
{
    cJSON *s = level_system(), *e;
    if (!s) return;
    cJSON_ArrayForEach(e, get(s, "Events"))
    {
        if (strcmp(str(e, "Trigger"), trigger) || cJSON_GetArraySize(get(e, "Commands")) == 0) continue;
        set_level_variables(who, earned, rose);
        events_run_commands(get(e, "Commands"), actor_of_char(who));
    }
}
#define WHEN_EARNED "When someone earns a level"
#define WHEN_RESTED "When the party wakes from Rest"
#define WHEN_RESTED_EACH "When the party wakes from Rest - once for each member who has earned a level"

/* Experience gained (U6ExperienceGained): a level earned - taken at once (Rise by themselves) or noticed once - Next Exp kept. 1 handled. */
int u6_experience_gained(int characterId)
{
    if (!u6_levels_on()) return 0;
    int level = stat(characterId, BK_LEVEL, 1), exp = stat(characterId, BK_EXP, 0), top = max_level();
    if (level < 1) level = 1;
    cJSON *ch = character_json(characterId), *s = level_system();
    int lock = num(ch, "LevelLock", 0), cap = lock > 0 && lock < top ? lock : top;
    if (s && cJSON_IsTrue(get(s, "RiseByThemselves")))
        while (level < cap && has_earned(level, exp))
        {
            rise_level(characterId, 0, 0, 0, hits_per_level(), 1);
            level++;
            level_events(WHEN_EARNED, characterId, level, 1);
        }
    else if (level < cap && has_earned(level, exp) && num(ch, "U6LevelNoticed", 0) < level + 1)
    {
        cJSON_DeleteItemFromObject(ch, "U6LevelNoticed"); cJSON_AddNumberToObject(ch, "U6LevelNoticed", level + 1);
        level_events(WHEN_EARNED, characterId, level + 1, 0); /* the system's "When someone earns a level" events */
    }
    if (stat_of_kind(BK_NEXTEXP)) set_stat(characterId, BK_NEXTEXP, next_exp(level));
    return 1;
}

/* ======================= A key asked for (U6GameWindow.U6Levels AskLetter) ======================= */

static char askAllowed[16]; static void (*askAnswer)(char letter);

/* The question in the line being typed; only the allowed keys answer (Escape: "0" when allowed; Space and Enter: ' '). */
static void ask_letter(const char *question, const char *allowed, void (*answer)(char))
{
    snprintf(prompt, sizeof prompt, "%s", question);
    snprintf(askAllowed, sizeof askAllowed, "%s", allowed);
    askAnswer = answer;
}

/* A key while a letter is asked: 1 taken (every key is, while it waits). */
static int letter_ask_took(const PlatformEvent *e)
{
    if (!askAnswer || e->type != EV_KEY_DOWN) return askAnswer != NULL;
    if (e->keyRepeat) return 1;
    const char *k = e->keyName;
    char c = 0;
    if (k[0] == 'D' && k[1] >= '0' && k[1] <= '9' && !k[2]) c = k[1];
    else if (!strncmp(k, "NumPad", 6) && k[6] >= '0' && k[6] <= '9') c = k[6];
    else if (k[0] >= 'A' && k[0] <= 'Z' && !k[1]) c = k[0];
    else if (!strcmp(k, "Escape") && strchr(askAllowed, '0')) c = '0';
    else if (!strcmp(k, "Space") || !strcmp(k, "Enter")) c = ' ';
    if (!c || !strchr(askAllowed, c)) return 1;
    void (*answer)(char) = askAnswer;
    askAnswer = NULL;
    char line[200];
    if (c != ' ') { snprintf(line, sizeof line, "%s%c", prompt, c); u6_say(line); }
    prompt[0] = 0;
    answer(c);
    return 1;
}

/* Ctrl+Q (U6GameWindow.Title AskToQuit): with a text area, asked there - Y quits, N or Escape doesn't. 0: the engine's own prompt. */
static void quit_answer(char key) { if (key == 'Y') events_request_quit(); }
int u6_ask_to_quit(void)
{
    if (!hasHud || !textPiece) return 0;
    if (askAnswer) return 1;
    ask_letter(word("QuitAsk"), "YN0", quit_answer);
    return 1;
}

/* ======================= Cast (U6GameWindow.U6Cast; Nuvie Magic start_new_spell, cast) ======================= */

static const char *SYLLABLES[26] = { "An", "Bet", "Corp", "Des", "Ex", "Flam", "Grav", "Hur", "In", "Jux", "Kal", "Lor", "Mani",
                                     "Nox", "Ort", "Por", "Quas", "Rel", "Sanct", "Tym", "Uus", "Vas", "Wis", "Xen", "Ylem", "Zu" };
static cJSON *castSpells[128]; static int castCount, castCircles[32], castCircleCount, castPage, castPicked, castCaster;
static char castTyped[8];

static int circle_of(cJSON *spell) { return num(spell, "Circle", 1); }

static const char *circle_name(int circle)
{
    static const char *N[9] = { "Linear", "First Circle", "Second Circle", "Third Circle", "Fourth Circle", "Fifth Circle", "Sixth Circle", "Seventh Circle", "Eighth Circle" };
    if (circle >= 0 && circle <= 8) return N[circle];
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Objects"), "CustomCircles")) if (num(c, "Number", -1) == circle) return str(c, "Name");
    static char name[24]; snprintf(name, sizeof name, "Circle %d", circle);
    return name;
}

static int cast_page(cJSON **out, int max)
{
    int n = 0;
    if (castCircleCount == 0) return 0;
    for (int i = 0; i < castCount && n < max; i++) if (circle_of(castSpells[i]) == castCircles[castPage]) out[n++] = castSpells[i];
    return n;
}

static void start_cast(void)
{
    int member = member_shown(), actor = world_actor_by_character(member);
    char verbLine[64]; snprintf(verbLine, sizeof verbLine, "%s-", word("Cast"));
    int style = magic_system_style(); /* another spell system (Tools > Spell Editor - magic.c) */
    if (style == 1) { u6_say(verbLine); u6_say("Use an ingredient with thy fetish."); return; }
    if (style == 2) { u6_say(verbLine); if (!magic_run_common_named(magic_system_on_cast(), actor)) u6_say(word("NoSpells")); return; }
    int n = actor >= 0 ? magic_u6_spells(actor, castSpells, 128) : -1;
    if (n < 0) { u6_say(verbLine); u6_say(word("NoSpellbook")); return; }
    if (n == 0) { u6_say(verbLine); u6_say(word("NoSpells")); return; }
    /* by circle (OrderBy: stable) */
    for (int i = 1; i < n; i++) { cJSON *s = castSpells[i]; int j = i - 1; while (j >= 0 && circle_of(castSpells[j]) > circle_of(s)) { castSpells[j + 1] = castSpells[j]; j--; } castSpells[j + 1] = s; }
    castCount = n; castCircleCount = 0;
    for (int i = 0; i < n; i++) { int c = circle_of(castSpells[i]), dup = 0; for (int k = 0; k < castCircleCount; k++) dup |= castCircles[k] == c; if (!dup && castCircleCount < 32) castCircles[castCircleCount++] = c; }
    castPage = castPicked = 0; castTyped[0] = 0; castCaster = actor;
    casting = 1;
    verb = 1;
    snprintf(prompt, sizeof prompt, "%s", verbLine);
}

static int draw_cast_panel(cJSON *panel)
{
    cJSON *page[64]; int n = cast_page(page, 64);
    char text[1200];
    int at = snprintf(text, sizeof text, "%s  (%d/%d)", castCircleCount ? circle_name(castCircles[castPage]) : "", castPage + 1, castCircleCount > 0 ? castCircleCount : 1);
    for (int i = 0; i < n && at < (int)sizeof text - 64; i++) at += snprintf(text + at, sizeof text - (size_t)at, "\n%s%s", i == castPicked && !castTyped[0] ? "> " : "  ", str(page[i], "Name"));
    cJSON *p = cJSON_CreateObject();
    cJSON_AddNumberToObject(p, "X", num(panel, "X", 0)); cJSON_AddNumberToObject(p, "Y", num(panel, "Y", 0));
    cJSON_AddNumberToObject(p, "Width", num(panel, "Width", 16)); cJSON_AddNumberToObject(p, "Height", num(panel, "Height", 16));
    words_at(p, text);
    cJSON_Delete(p);
    return 1;
}

static void show_typed(void)
{
    int at = snprintf(prompt, sizeof prompt, "%s-", word("Cast"));
    for (int i = 0; castTyped[i] && at < (int)sizeof prompt - 8; i++) at += snprintf(prompt + at, sizeof prompt - (size_t)at, "%s%s", i ? " " : "", SYLLABLES[castTyped[i] - 'A']);
}

/* A spell's syllables as typed letters ("In Lor" -> "IL"). */
static void invocation(cJSON *spell, char *out, size_t size)
{
    size_t n = 0;
    const char *w = str(spell, "Words");
    for (const char *p = w; *p && n < size - 1; p++) if ((p == w || p[-1] == ' ') && *p != ' ') out[n++] = (char)toupper((unsigned char)*p);
    out[n] = 0;
}

static void end_casting(void) { casting = 0; }

static void choose_spell(void)
{
    cJSON *spell = NULL, *page[64];
    int n = cast_page(page, 64);
    if (castTyped[0]) { char inv[16]; for (int i = 0; i < castCount && !spell; i++) { invocation(castSpells[i], inv, sizeof inv); if (!strcmp(inv, castTyped)) spell = castSpells[i]; } }
    else if (castPicked < n) spell = page[castPicked];
    char line[200]; snprintf(line, sizeof line, "%s", prompt);
    int typed = castTyped[0] != 0, caster = castCaster;
    end_casting(); end_verb(); set_focus(FOCUS_MAP);
    if (!spell) { u6_say(line); u6_say(word("NotInBook")); return; }
    if (typed) { u6_say(line); char nm[96]; snprintf(nm, sizeof nm, "(%s)", str(spell, "Name")); u6_say(nm); }
    else
    {
        snprintf(line, sizeof line, "%s-%s", word("Cast"), str(spell, "Name")); u6_say(line);
        char w[120]; snprintf(w, sizeof w, "%s", str(spell, "Words"));
        char *s = w; while (*s == ' ') s++;
        size_t l = strlen(s); while (l && s[l - 1] == ' ') s[--l] = 0;
        if (*s) { snprintf(line, sizeof line, "\"%s\"", s); u6_say(line); }
    }
    const char *why = magic_u6_spend(caster, spell);
    if (why) { u6_say(word(why)); return; }
    if (!cJSON_IsTrue(get(spell, "NeedsTarget")))
    {
        int x, y, f; world_actor_cell(caster, &x, &y, &f);
        magic_u6_deliver(caster, spell, caster, 0, x, y);
        pass_turn_if_fighting(0);
        return;
    }
    /* its target: the map cursor (it starts on the caster) or a click; Enter there */
    aimSpell = spell; aimCaster = caster;
    verb = 1;
    snprintf(prompt, sizeof prompt, "%s", word("OnWhom"));
    int x, y, f; world_actor_cell(caster, &x, &y, &f); mapCurX = x; mapCurY = y;
    set_focus(FOCUS_MAP);
}

static int cast_key(const PlatformEvent *e)
{
    if (key_is(e, "Cancel") || !strcmp(e->keyName, "Escape")) { end_casting(); cancel_verb(); return 1; }
    if (e->keyRepeat && strcmp(e->keyName, "Up") && strcmp(e->keyName, "Down") && strcmp(e->keyName, "Back")) return 1;
    if (!strcmp(e->keyName, "Enter")) { choose_spell(); return 1; }
    if (!strcmp(e->keyName, "Back")) { size_t l = strlen(castTyped); if (l) castTyped[l - 1] = 0; show_typed(); return 1; }
    if (e->keyName[0] >= 'A' && e->keyName[0] <= 'Z' && !e->keyName[1])
    {
        size_t l = strlen(castTyped);
        if (l < 4) { castTyped[l] = e->keyName[0]; castTyped[l + 1] = 0; }
        show_typed();
        return 1;
    }
    cJSON *page[64]; int count = cast_page(page, 64);
    if (!strcmp(e->keyName, "Up") && count > 0) castPicked = (castPicked + count - 1) % count;
    else if (!strcmp(e->keyName, "Down") && count > 0) castPicked = (castPicked + 1) % count;
    else if (!strcmp(e->keyName, "Left") && castCircleCount > 0) { castPage = (castPage + castCircleCount - 1) % castCircleCount; castPicked = 0; }
    else if (!strcmp(e->keyName, "Right") && castCircleCount > 0) { castPage = (castPage + 1) % castCircleCount; castPicked = 0; }
    return 1;
}

/* The target picked: the one there, the thing there, or the spot. */
static void cast_on_cell(int cx, int cy, int actor, int uid, const char *name)
{
    if (!aimSpell) { finish(name, word("NotPossible"), NULL); return; }
    cJSON *spell = aimSpell; int caster = aimCaster;
    aimSpell = NULL; aimCaster = -1;
    finish(name, NULL, NULL);
    magic_u6_deliver(caster, spell, actor, actor >= 0 ? 0 : uid, cx, cy);
}

/* ======================= Rest (U6GameWindow.U6Levels U6Rest; Nuvie Events::rest, Party::can_rest, TimedRest) ======================= */

static long restHealsAfter;
static int restHours, restParty[16], restPartyCount, restEach[16], restEachCount, restEachAt;
static void rest_each_next(void);
static void set_level_variables(int who, int earned, int rose);
static int actor_of_char(int characterId);

static void sleep_for(int hours, int guard)
{
    /* Mealtime: each eats one food, their own or anyone's in the party */
    u6_say(word("Mealtime"));
    int fed[16] = { 0 };
    for (int i = 0; i < restPartyCount; i++)
    {
        int member = restParty[i], ate = items_eat_one_food(member);
        for (int j = 0; !ate && j < restPartyCount; j++) if (j != i) ate = items_eat_one_food(restParty[j]);
        fed[i] = ate;
        u6_say(wordf(ate ? "HasFood" : "NoFood", "", char_name(member)));
    }
    if (guard >= 0) u6_say(wordf("StandsGuard", "", char_name(guard)));
    events_advance_minutes(hours * 60);
    /* waking: those asleep and fed heal - once every 12 hours */
    long now = events_total_minutes();
    if (now >= restHealsAfter)
    {
        for (int i = 0; i < restPartyCount; i++)
        {
            int member = restParty[i], hp;
            if (member == guard || !fed[i] || !vitals_hp(member, &hp)) continue;
            int most = vitals_max_hp(member), diff = most - hp;
            if (diff <= 0) continue;
            if (diff == 1) diff = 2;
            int half = diff / 2 > 0 ? diff / 2 : 1;
            int healed = hp + rand() % half + diff / 2;
            vitals_set_hp(member, healed < most ? healed : most);
            u6_say(wordf("Healed", "", char_name(member)));
        }
        restHealsAfter = now + 12 * 60;
    }
    /* the leveling system's rest events: once, then once for each member who has earned a level (a dream, anything) */
    cJSON *s = u6_levels_on() ? level_system() : NULL;
    if (!s) return;
    script_set_number("RestHours", hours);
    script_set("RestGuard", value_text(guard >= 0 ? char_name(guard) : ""));
    cJSON *e;
    cJSON_ArrayForEach(e, get(s, "Events"))
        if (!strcmp(str(e, "Trigger"), WHEN_RESTED) && cJSON_GetArraySize(get(e, "Commands")) > 0) events_run_commands(get(e, "Commands"), 0);
    restEachCount = restEachAt = 0;
    for (int i = 0; i < restPartyCount; i++)
        if (restParty[i] != guard && has_earned(stat(restParty[i], BK_LEVEL, 1), stat(restParty[i], BK_EXP, 0))) restEach[restEachCount++] = restParty[i];
    rest_each_next();
}

/* The "once for each member" rest events, one member after the other (each run waits for the one before). */
static void rest_each_next(void)
{
    cJSON *s = level_system(), *e;
    if (!s || restEachAt >= restEachCount) return;
    int member = restEach[restEachAt++];
    static cJSON *all, *builtFrom; /* every such event's commands, one list - made once per leveling system (the runner keeps lists by their address) */
    if (builtFrom != s)
    {
        all = cJSON_CreateArray(); builtFrom = s;
        cJSON_ArrayForEach(e, get(s, "Events"))
            if (!strcmp(str(e, "Trigger"), WHEN_RESTED_EACH)) { cJSON *c; cJSON_ArrayForEach(c, get(e, "Commands")) cJSON_AddItemToArray(all, cJSON_Duplicate(c, 1)); }
    }
    if (cJSON_GetArraySize(all) == 0) return;
    set_level_variables(member, stat(member, BK_LEVEL, 1) + 1, 0);
    events_run_commands_then(all, actor_of_char(member), rest_each_next);
}

static void rest_guard(char key)
{
    int g = key - '0', guard = g >= 1 && g <= restPartyCount ? restParty[g - 1] : -1;
    u6_say(guard >= 0 ? char_name(guard) : word("NoOne"));
    sleep_for(restHours, guard);
}

static void rest_hours(char key)
{
    restHours = key - '0';
    if (restHours == 0) return;
    int ids[16], n = party_list(ids, 16);
    restPartyCount = 0;
    for (int i = 0; i < n; i++) if (!vitals_dead(ids[i])) restParty[restPartyCount++] = ids[i];
    if (restPartyCount <= 1) { sleep_for(restHours, -1); return; }
    char allowed[12]; int k = 0;
    for (int i = 0; i <= restPartyCount && i < 10; i++) allowed[k++] = (char)('0' + i);
    allowed[k] = 0;
    ask_letter(word("WhoGuards"), allowed, rest_guard);
}

/* Rest: not in combat, with foes or others near (within 5, not in the party), or in solo mode; then the hours and the guard. */
static void rest(void)
{
    const char *refused = NULL;
    if (combat_mode()) refused = word("NotWhileCombat");
    else if (combat_hostile_nearby()) refused = word("FoesNear");
    else
    {
        int px, py, pf; world_player_cell(&px, &py, &pf);
        for (int a = 1; a < world_actor_count() && !refused; a++)
        {
            int id = world_actor_character(a), x, y, f;
            if (id < 0 || items_in_party(id) || vitals_dead(id) || world_actor_gone(a)) continue;
            world_actor_cell(a, &x, &y, &f);
            if (f == pf && abs(x - px) <= 5 && abs(y - py) <= 5) refused = word("OthersNear");
        }
    }
    char line[160];
    if (!refused && soloMode) refused = word("RestNotSolo");
    if (refused) { snprintf(line, sizeof line, "%s%s", word("Rest"), refused); u6_say(line); return; }
    u6_say(word("Rest"));
    ask_letter(word("HowManyHours"), "0123456789", rest_hours);
}

/* ======================= Books (U6GameWindow.U6Things U6ReadBook) ======================= */

static char (*bookLines)[192]; static int bookLineCount, bookLineCap, bookAt, bookPerScreen;

static void book_line(const char *line)
{
    if (bookLineCount == bookLineCap) { bookLineCap = bookLineCap ? bookLineCap * 2 : 64; bookLines = (char (*)[192])realloc(bookLines, sizeof *bookLines * (size_t)bookLineCap); }
    if (bookLines) snprintf(bookLines[bookLineCount++], sizeof bookLines[0], "%s", line);
}
static void book_line_coloured(const char *line) { char c[400]; colour_line(line, c, sizeof c); book_line(c); }
static void book_paragraph(const char *text)
{
    char marked[1200]; colour_marks(text, marked, sizeof marked); /* [COLOR=...] kept across its lines */
    colourOpen[0] = 0;
    wrap(font_of(textPiece), marked, num(textPiece, "Width", 16), book_line_coloured);
}

static void book_more(char key);
static void book_show(void)
{
    for (int i = 0; i < bookPerScreen && bookAt < bookLineCount; i++) u6_say(bookLines[bookAt++]);
    if (bookAt < bookLineCount) ask_letter(word("More"), " 0", book_more); /* any key goes on, Escape stops */
}
static void book_more(char key) { if (key == ' ') book_show(); }

int u6_read_book(int bookId)
{
    if (!textPiece) return 0;
    bookLineCount = 0; bookAt = 0;
    books_paragraphs(bookId, book_paragraph);
    sound_play(books_open_sound(bookId));
    if (bookLineCount == 0) { u6_say(word("NothingWritten")); return 1; }
    bookPerScreen = num(textPiece, "Height", 16) / line_height(font_of(textPiece)) - 1;
    if (bookPerScreen < 2) bookPerScreen = 2;
    book_show();
    return 1;
}

/* ======================= Scenes (U6GameWindow.U6Scene): a black screen, pictures, words, choices ======================= */

typedef struct ScenePart { char name[40]; const Sprite *sprite; int x, y, w, h, fit; char *text; char font[48]; uint32_t colour; } ScenePart;
#define SCENE_MAX 64
static int sceneOn, sceneX, sceneY, sceneW, sceneH; static uint32_t sceneColour;
static ScenePart sceneParts[SCENE_MAX]; static int scenePartCount;
static int sceneKeyToken;                                              /* Scene Wait For Key: its token (0 none) */
static int choiceToken; static char choiceVar[64], choiceKeys[16]; static int choiceCount, choiceX, choiceY, choiceLineH; /* Scene Choice */
static int askChoiceToken; static char askChoiceVar[64], askChoiceLetters[16];   /* a choice asked in the text area (no scene) */

int u6_scene_on(void) { return sceneOn; }

static void scene_remove(const char *name)
{
    for (int i = 0; i < scenePartCount; )
        if (!name[0] || !strcmp(sceneParts[i].name, name)) { free(sceneParts[i].text); sceneParts[i] = sceneParts[--scenePartCount]; }
        else i++;
}

static ScenePart *scene_add(const char *name)
{
    if (scenePartCount >= SCENE_MAX) return NULL;
    ScenePart *p = &sceneParts[scenePartCount++];
    memset(p, 0, sizeof *p);
    snprintf(p->name, sizeof p->name, "%s", name);
    return p;
}

static void begin_scene(int gameViewOnly, const char *colour)
{
    scene_remove("");
    sceneOn = 1;
    if (gameViewOnly && hasHud) { sceneX = vx; sceneY = vy; sceneW = vw; sceneH = vh; }
    else { sceneX = 0; sceneY = 0; sceneW = screenW < SCREEN_W ? screenW : SCREEN_W; sceneH = screenH < SCREEN_H ? screenH : SCREEN_H; }
    char hex[16]; snprintf(hex, sizeof hex, "%s%s", colour[0] == '#' ? "" : "#", colour[0] ? colour : "000000");
    sceneColour = colour_of(hex); if (!sceneColour) sceneColour = 0xFF000000u;
    heldCount = 0;
}

static void end_scene(void)
{
    scene_remove("");
    sceneOn = 0; sceneKeyToken = 0; choiceToken = 0;
}

/* Words with {formula} filled in (a variable's value - any formula in braces). */
static void scene_words(const char *text, char *out, size_t size)
{
    size_t n = 0;
    for (const char *p = text; *p && n < size - 1; )
    {
        const char *close = *p == '{' ? strchr(p + 1, '}') : NULL;
        if (close && !memchr(p + 1, '{', (size_t)(close - p - 1)))
        {
            char f[200]; snprintf(f, sizeof f, "%.*s", (int)(close - p - 1), p + 1);
            Value v; char err[120];
            if (script_eval(f, &v, err, sizeof err)) { n += (size_t)snprintf(out + n, size - n, "%s", value_text_of(v)); if (n >= size) n = size - 1; p = close + 1; continue; }
        }
        out[n++] = *p++;
    }
    out[n] = 0;
}

void u6_draw_scene(uint32_t *pixels, uint32_t ms)
{
    if (!sceneOn) return;
    screen = pixels;
    fill_rect(sceneX, sceneY, sceneW, sceneH, sceneColour);
    for (int i = 0; i < scenePartCount; i++)
    {
        ScenePart *p = &sceneParts[i];
        if (p->sprite)
        {
            const Picture *pic = sprite_picture_at(p->sprite, ms);
            if (p->fit) draw_sprite_fit(p->sprite, sceneX + p->x, sceneY + p->y, p->w, p->h, ms);
            else if (pic) blit_fill(pic, sceneX + p->x, sceneY + p->y, p->w, p->h, 0);
        }
        else if (p->text)
        {
            cJSON *piece = cJSON_CreateObject();
            cJSON_AddNumberToObject(piece, "X", sceneX + p->x); cJSON_AddNumberToObject(piece, "Y", sceneY + p->y);
            cJSON_AddNumberToObject(piece, "Width", p->w); cJSON_AddNumberToObject(piece, "Height", sceneH);
            cJSON_AddStringToObject(piece, "FontName", p->font);
            if (p->colour) { char hex[12]; snprintf(hex, sizeof hex, "#%06X", (unsigned)(p->colour & 0xFFFFFF)); cJSON_AddStringToObject(piece, "TextColor", hex); }
            words_at(piece, p->text);
            cJSON_Delete(piece);
        }
    }
}

/* Lines wrapped to a width, joined by '\n' (into a new string). */
static char *wrapped_text(const char *text, int width, const char *fontName)
{
    static char *acc; static size_t accLen, accCap;
    accLen = 0;
    if (!acc) { accCap = 2048; acc = (char *)malloc(accCap); }
    Font *f = font_get(fontName && fontName[0] ? fontName : "InGameFont");
    char para[1200];
    for (const char *p = text; ; )
    {
        size_t n = strcspn(p, "\n");
        snprintf(para, sizeof para, "%.*s", (int)n, p);
        size_t k = 0; for (size_t i = 0; para[i]; i++) if (para[i] != '\r') para[k++] = para[i];
        para[k] = 0;
        /* each line through wrap(): appended with '\n' */
        char lines[32][200]; int count = 0;
        const char *s = para;
        char line[400] = "", cand[400];
        for (;;)
        {
            size_t wl = strcspn(s, " ");
            char w[300]; snprintf(w, sizeof w, "%.*s", (int)wl, s);
            snprintf(cand, sizeof cand, "%s%s%s", line, line[0] ? " " : "", w);
            if (!line[0] || words_width(f, cand) <= width) snprintf(line, sizeof line, "%s", cand);
            else { if (count < 32) snprintf(lines[count++], sizeof lines[0], "%s", line); snprintf(line, sizeof line, "%s", w); }
            s += wl; if (!*s) break; s++;
        }
        if (count < 32) snprintf(lines[count++], sizeof lines[0], "%s", line);
        for (int i = 0; i < count; i++)
        {
            size_t need = accLen + strlen(lines[i]) + 2;
            if (need > accCap) { accCap = need * 2; acc = (char *)realloc(acc, accCap); }
            accLen += (size_t)sprintf(acc + accLen, "%s%s", accLen ? "\n" : "", lines[i]);
        }
        p += n; if (!*p) break; p++;
    }
    char *copy = (char *)malloc(accLen + 1);
    memcpy(copy, acc, accLen); copy[accLen] = 0;
    return copy;
}

static void answer_scene_choice(int index)
{
    if (!choiceToken || index < 0 || index >= choiceCount) return;
    int token = choiceToken; choiceToken = 0;
    scene_remove("choice");
    script_set_number(choiceVar, index + 1);
    events_doll_answered(token, 1);
}

static void ask_choice_answer(char key)
{
    int pick = key >= '1' && key <= '9' ? key - '1' : (int)(strchr(askChoiceLetters, key) ? strchr(askChoiceLetters, key) - askChoiceLetters : -1);
    script_set_number(askChoiceVar, pick + 1);
    int token = askChoiceToken; askChoiceToken = 0;
    events_doll_answered(token, 1);
}

/* Keys while a scene waits: any key goes on; a choice takes its letter or number. 1 taken. */
static int scene_key(const PlatformEvent *e)
{
    if (!sceneOn) return 0;
    if (e->type != EV_KEY_DOWN || e->keyRepeat) return 1;
    const char *k = e->keyName;
    if (choiceToken)
    {
        if (k[0] == 'D' && k[1] >= '1' && k[1] <= '9' && !k[2]) answer_scene_choice(k[1] - '1');
        else if (!strncmp(k, "NumPad", 6) && k[6] >= '1' && k[6] <= '9') answer_scene_choice(k[6] - '1');
        else if (k[0] >= 'A' && k[0] <= 'Z' && !k[1]) { const char *at = strchr(choiceKeys, k[0]); if (at) answer_scene_choice((int)(at - choiceKeys)); }
        return 1;
    }
    if (sceneKeyToken) { int t = sceneKeyToken; sceneKeyToken = 0; events_doll_answered(t, 1); }
    return 1;
}

static int scene_click(int x, int y)
{
    if (!sceneOn) return 0;
    if (choiceToken)
    {
        int i = (int)floor((double)(y - sceneY - choiceY) / (choiceLineH > 0 ? choiceLineH : 1));
        if (x - sceneX >= choiceX && i >= 0 && i < choiceCount) answer_scene_choice(i);
        return 1;
    }
    if (sceneKeyToken) { int t = sceneKeyToken; sceneKeyToken = 0; events_doll_answered(t, 1); }
    return 1;
}

static int choices_of(const char *text, char out[][128], int max)
{
    char words[1200]; scene_words(text, words, sizeof words);
    int n = 0;
    for (const char *p = words; *p && n < max; )
    {
        size_t l = strcspn(p, "\n");
        char line[128]; snprintf(line, sizeof line, "%.*s", (int)l, p);
        char *s = line; while (*s == ' ' || *s == '\r') s++;
        size_t sl = strlen(s); while (sl && (s[sl - 1] == ' ' || s[sl - 1] == '\r')) s[--sl] = 0;
        if (*s) snprintf(out[n++], 128, "%s", s);
        p += l; if (*p) p++;
    }
    return n;
}

/* ======================= The title menu (U6GameWindow.U6Title; Nuvie's original menu) ======================= */
/* Over the title picture: Introduction, Create New Character, Acknowledgments, Journey Onward (the saved game; greyed when none).
   Up / Down and Enter, or a click. Ultima 6 has no Save/Load screen: wherever the Ultima 7 engine would open it, this comes up. */

#define OWNER_U6_TITLE 5 /* the title's music (dolls.c's OWNER_TITLE) */
static int titleOn, titlePick, titleAcks, titleInPlay;
static const char *TITLE_CHOICES[4] = { "TitleIntro", "TitleCreate", "TitleAcks", "TitleJourney" };

static cJSON *title_settings(void) { return get(get(project(), "Settings"), "Title"); }
void dolls_request_load(const char *folder, const char *name); /* main.c: a save loaded at the frame's end */
static int has_saved_game(void) { static SaveInfo s; return save_list(&s, 1) > 0; }
static int intro_video(void) { int v = num(title_settings(), "IntroVideoId", -1); return v >= 0 && video_exists(v); }
static int has_intro(void) { return cJSON_GetArraySize(get(hud, "IntroCommands")) > 0 || intro_video(); }

int u6_title_on(void) { return titleOn; }

static void open_title(int inPlay)
{
    titleOn = 1; titleInPlay = inPlay; titleAcks = 0;
    titlePick = has_saved_game() ? 3 : 1;
    int music = num(title_settings(), "MusicId", -1);
    if (music >= 0) sound_play_ex(music, -1, !cJSON_IsFalse(get(title_settings(), "MusicLoops")), OWNER_U6_TITLE, 1);
}

static void close_title(void) { titleOn = 0; sound_stop_owner(OWNER_U6_TITLE); }

static void intro_video_done(int ok, void *context) { (void)ok; (void)context; open_title(titleInPlay); }

/* The game's start: the intro video, then the menu. */
void u6_show_title(void)
{
    titleInPlay = 0;
    if (intro_video()) { titleOn = 1; video_play(num(title_settings(), "IntroVideoId", -1), 1, 1.0, intro_video_done, NULL); }
    else open_title(0);
}

/* Wherever the Save/Load screen would open (S, the command, a dead avatar double-clicked): the menu, in play. */
int u6_open_save_screen(void)
{
    if (!on || !hasHud) return 0;
    if (!titleOn) open_title(1);
    return 1;
}

static void intro_finished(void) { end_scene(); if (titleOn) open_title(titleInPlay); }
static void create_finished(void) { end_scene(); close_title(); }

static void title_choose(int choice)
{
    switch (choice)
    {
        case 0:
            if (cJSON_GetArraySize(get(hud, "IntroCommands")) > 0) { sound_stop_owner(OWNER_U6_TITLE); events_run_commands_then(get(hud, "IntroCommands"), 0, intro_finished); }
            else if (intro_video()) { sound_stop_owner(OWNER_U6_TITLE); video_play(num(title_settings(), "IntroVideoId", -1), 1, 1.0, intro_video_done, NULL); }
            break;
        case 1: /* a new game: at the start, the game begins (its Create New Character scene first); in play, a fresh one */
            if (titleInPlay) { close_title(); events_request_restart(0); }
            else if (cJSON_GetArraySize(get(hud, "CreateCommands")) > 0) { sound_stop_owner(OWNER_U6_TITLE); events_run_commands_then(get(hud, "CreateCommands"), 0, create_finished); }
            else close_title();
            break;
        case 2: titleAcks = 1; break;
        case 3:
            if (has_saved_game())
            {
                static SaveInfo newest;
                save_list(&newest, 1);
                close_title();
                dolls_request_load(newest.folder, newest.name);
            }
            break;
    }
}

static int title_key(const PlatformEvent *e)
{
    if (!titleOn) return 0;
    if (video_playing() || u6_scene_on()) return 0;
    if (e->type != EV_KEY_DOWN) return 1;
    const char *k = e->keyName;
    if (e->keyRepeat && strcmp(k, "Up") && strcmp(k, "Down")) return 1;
    if (titleAcks) { titleAcks = 0; return 1; }
    if (!strcmp(k, "Up")) titlePick = (titlePick + 3) % 4;
    else if (!strcmp(k, "Down")) titlePick = (titlePick + 1) % 4;
    else if (!strcmp(k, "Enter") || !strcmp(k, "Space")) title_choose(titlePick);
    else if (!strcmp(k, "Escape") && titleInPlay && !vitals_game_over()) close_title();
    return 1;
}

static int title_line_h(void) { return line_height(font_get("InGameFont")); }

static int title_click(int x, int y)
{
    (void)x;
    if (!titleOn || video_playing() || u6_scene_on()) return titleOn;
    if (titleAcks) { titleAcks = 0; return 1; }
    int lh = title_line_h(), top = screenH - 5 * (lh + 2);
    int i = (int)floor((double)(y - top + 1) / (lh + 2));
    if (i < 0 || i >= 4) return 1;
    titlePick = i;
    title_choose(i);
    return 1;
}

static void shade_rect(int x, int y, int w, int h, uint32_t rgb, uint32_t alpha)
{
    FRAME_CLIP(x, y, w, h, x0, y0, x1, y1);
    for (int j = y0; j < y1; j++) for (int i = x0; i < x1; i++) screen[j * FRAME_W + i] = blend_px(screen[j * FRAME_W + i], 0xFF000000u | rgb, alpha);
}

void u6_draw_title(uint32_t *pixels, uint32_t ms)
{
    if (!titleOn || video_playing()) return;
    screen = pixels;
    fill_rect(0, 0, SCREEN_W, SCREEN_H, 0xFF000000u);
    const Sprite *picture = sprite_of(get(u6settings(), "TitleSprite"));
    if (picture) draw_sprite_fit(picture, 0, 0, screenW, screenH, ms);
    Font *f = font_get("InGameFont");
    int lh = title_line_h();
    if (titleAcks)
    {
        cJSON *page = cJSON_CreateObject();
        cJSON_AddNumberToObject(page, "X", 16); cJSON_AddNumberToObject(page, "Y", 16);
        cJSON_AddNumberToObject(page, "Width", screenW - 32); cJSON_AddNumberToObject(page, "Height", screenH - 32);
        char *text = wrapped_text(str(u6settings(), "Acknowledgments"), screenW - 32, "");
        words_at(page, text);
        free(text); cJSON_Delete(page);
        return;
    }
    int width = 0;
    for (int i = 0; i < 4; i++) { int w = font_text_width(f, word(TITLE_CHOICES[i])); if (w > width) width = w; }
    width += 8;
    int left = (screenW - width) / 2, top = screenH - 5 * (lh + 2);
    for (int i = 0; i < 4; i++)
    {
        int y = top + i * (lh + 2);
        int enabled = i == 0 ? has_intro() : i == 3 ? has_saved_game() : 1;
        if (i == titlePick) { shade_rect(left, y - 1, width, lh + 2, 0x603010, 160); outline(left, y - 1, width, lh + 2, 0xFFFFD700u); }
        const char *w = word(TITLE_CHOICES[i]);
        int tx = left + (width - font_text_width(f, w)) / 2;
        font_draw(f, screen, tx, y, w, 0);
        if (!enabled) shade_rect(tx, y, font_text_width(f, w), lh, 0x000000, 153); /* greyed (opacity 0.4) */
    }
}

/* ---- Ultima 6's one saved game: Ctrl+S saves it, Ctrl+R restores it (QuickSaveOrLoad: Save Game / Load Game, slot 1) ---- */
static void quick_save_or_load(int save)
{
    if (save && (soloMode || controlled > 0)) end_solo(); /* saved in party mode, the avatar moved */
    quick_save_or_load_now(save);
}
static void quick_save_or_load_now(int save)
{
    static cJSON *saveCmd, *loadCmd;
    cJSON **cmd = save ? &saveCmd : &loadCmd;
    if (!*cmd)
    {
        *cmd = cJSON_CreateArray();
        cJSON *c = cJSON_CreateObject(), *p = cJSON_CreateObject();
        cJSON_AddNumberToObject(c, "Type", save ? C_SaveGame : C_LoadGame);
        cJSON_AddStringToObject(p, "Slot", "1");
        cJSON_AddStringToObject(p, "Name", "\"Game\"");
        cJSON_AddItemToObject(c, "Parameters", p);
        cJSON_AddItemToArray(*cmd, c);
    }
    cJSON *p = get(cJSON_GetArrayItem(*cmd, 0), "Parameters");
    char name[96]; snprintf(name, sizeof name, "\"%s\"", char_name(player_char())[0] ? char_name(player_char()) : "Game");
    cJSON_ReplaceItemInObject(p, "Name", cJSON_CreateString(name));
    events_run_commands(*cmd, 0);
}

/* ======================= Making a character (U6GameWindow.U6Create) ======================= */
/* Scene Type Text, Scene Pick Picture, Scene Distribute Points: on the scene, or in the text area's prompt with no scene up. */

const char *u6_formula_text(const char *key); /* events.c: the running command's formula, as words */
enum { IN_NONE, IN_TYPE, IN_PICK, IN_POINTS };
static int inputMode, inputToken, inputX, inputY;
static char inputVar[64], inputPrompt[200], inputTyped[64]; static int inputLongest;
static const Sprite *pickPictures[128]; static int pickCount, pickAt, pickW, pickH, pickPortrait, pickWho; static char pickGroup[64];
static cJSON *pointStats[16]; static int pointValues[16], pointCount, pointRow, pointLeft, pointLeast, pointMost, pointSpendAll, pointWho; static char pointLeftVar[64];

/* Words shown on the scene under a name (replaced each time), or in the text area's prompt with no scene. */
static void scene_show(const char *name, int x, int y, const char *text)
{
    if (!sceneOn) { snprintf(prompt, sizeof prompt, "%s", text); return; }
    scene_remove(name);
    ScenePart *p = scene_add(name);
    if (!p) return;
    p->x = x; p->y = y; p->w = sceneW - x > 8 ? sceneW - x : 8;
    size_t l = strlen(text); p->text = (char *)malloc(l + 1); memcpy(p->text, text, l + 1);
}

static void input_done(const char *name)
{
    inputMode = IN_NONE;
    if (!sceneOn) prompt[0] = 0;
    else scene_remove(name);
}

static void answer_input(void) { int t = inputToken; inputToken = 0; events_doll_answered(t, 1); }

static void show_typing(void) { char t[300]; snprintf(t, sizeof t, "%s%s_", inputPrompt, inputTyped); scene_show("typing", inputX, inputY, t); }

static void show_pick(void)
{
    const Sprite *s = pickPictures[pickAt];
    char t[300];
    if (sceneOn)
    {
        const Picture *first = sprite_picture_at(s, 0);
        int pw = pickW > 0 ? pickW : first ? first->width : 16, ph = pickH > 0 ? pickH : first ? first->height : 16;
        scene_remove("picking");
        ScenePart *p = scene_add("picking");
        if (p) { p->sprite = s; p->x = inputX; p->y = inputY; p->w = pw; p->h = ph; p->fit = 1; }
        snprintf(t, sizeof t, "%s< %d / %d >", inputPrompt, pickAt + 1, pickCount);
        scene_show("picking words", inputX, inputY + ph + 2, t);
    }
    else { snprintf(t, sizeof t, "%s< %s %d / %d >", inputPrompt, s->name, pickAt + 1, pickCount); scene_show("picking words", inputX, inputY, t); }
}

static void pick_done(void)
{
    input_done("picking words");
    scene_remove("picking");
    script_set_number(inputVar, pickAt + 1);
    cJSON *who = character_json(pickWho);
    if (pickPortrait && who)
    {
        if (!str(who, "U6OwnFace")[0]) { char own[140]; snprintf(own, sizeof own, "%s/%s", str(who, "FaceSpriteGroup"), str(who, "FaceSpriteName")); cJSON_DeleteItemFromObject(who, "U6OwnFace"); cJSON_AddStringToObject(who, "U6OwnFace", own); }
        cJSON_DeleteItemFromObject(who, "FaceSpriteGroup"); cJSON_AddStringToObject(who, "FaceSpriteGroup", pickGroup);
        cJSON_DeleteItemFromObject(who, "FaceSpriteName"); cJSON_AddStringToObject(who, "FaceSpriteName", pickPictures[pickAt]->name);
    }
    answer_input();
}

static void show_points(void)
{
    char t[1200]; int at = 0;
    const char *sep = sceneOn ? "\n" : "  ";
    if (inputPrompt[0]) at += snprintf(t + at, sizeof t - (size_t)at, "%s", inputPrompt);
    for (int i = 0; i < pointCount && at < (int)sizeof t - 80; i++) at += snprintf(t + at, sizeof t - (size_t)at, "%s%s %s: %d", at ? sep : "", i == pointRow ? ">" : " ", str(pointStats[i], "Name"), pointValues[i]);
    snprintf(t + at, sizeof t - (size_t)at, "%s%s %d", at ? sep : "", word("PointsLeft"), pointLeft);
    scene_show("points", inputX, inputY, t);
}

/* Keys while a making command waits (with a scene up or not). 1 taken. */
static int input_key(const PlatformEvent *e)
{
    if (!inputMode) return 0;
    if (e->type != EV_KEY_DOWN) return 1;
    const char *k = e->keyName;
    int repeatOk = !strcmp(k, "Back") || !strcmp(k, "Left") || !strcmp(k, "Right") || !strcmp(k, "Up") || !strcmp(k, "Down");
    if (e->keyRepeat && !repeatOk) return 1;
    if (inputMode == IN_TYPE)
    {
        if (!strcmp(k, "Enter"))
        {
            char *t = inputTyped; while (*t == ' ') t++;
            if (!*t) return 1;
            size_t l = strlen(t); while (l && t[l - 1] == ' ') t[--l] = 0;
            int hadScene = sceneOn;
            input_done("typing");
            if (!hadScene) { char line[300]; snprintf(line, sizeof line, "%s%s", inputPrompt, inputTyped); u6_say(line); }
            script_set(inputVar, value_text(t));
            answer_input();
            return 1;
        }
        if (!strcmp(k, "Back")) { size_t l = strlen(inputTyped); if (l) inputTyped[l - 1] = 0; show_typing(); }
        return 1; /* the letters come as text (u6_talk_text) */
    }
    if (inputMode == IN_PICK)
    {
        if (!strcmp(k, "Left")) { pickAt = (pickAt + pickCount - 1) % pickCount; show_pick(); }
        else if (!strcmp(k, "Right")) { pickAt = (pickAt + 1) % pickCount; show_pick(); }
        else if (!strcmp(k, "Enter") || !strcmp(k, "Space")) pick_done();
        return 1;
    }
    /* points */
    if (!strcmp(k, "Up")) pointRow = (pointRow + pointCount - 1) % pointCount;
    else if (!strcmp(k, "Down")) pointRow = (pointRow + 1) % pointCount;
    else if ((!strcmp(k, "Right") || !strcmp(k, "OemPlus") || !strcmp(k, "Add")) && pointLeft > 0 && pointValues[pointRow] < pointMost) { pointValues[pointRow]++; pointLeft--; }
    else if ((!strcmp(k, "Left") || !strcmp(k, "OemMinus") || !strcmp(k, "Subtract")) && pointValues[pointRow] > pointLeast) { pointValues[pointRow]--; pointLeft++; }
    else if (!strcmp(k, "Enter") && (!pointSpendAll || pointLeft == 0))
    {
        input_done("points");
        cJSON *who = character_json(pointWho), *values = get(who, "StatValues");
        if (who && !values) values = cJSON_AddObjectToObject(who, "StatValues");
        for (int i = 0; i < pointCount && values; i++)
        {
            char key[16], text[16]; snprintf(key, sizeof key, "%d", num(pointStats[i], "Id", -1)); snprintf(text, sizeof text, "%d", pointValues[i]);
            cJSON_DeleteItemFromObject(values, key); cJSON_AddStringToObject(values, key, text);
        }
        if (pointLeftVar[0]) script_set_number(pointLeftVar, pointLeft);
        answer_input();
        return 1;
    }
    show_points();
    return 1;
}

static int input_text(int ch)
{
    if (inputMode != IN_TYPE) return 0;
    size_t l = strlen(inputTyped);
    if (ch >= 32 && ch <= 126 && (int)l < inputLongest) { inputTyped[l] = (char)ch; inputTyped[l + 1] = 0; }
    show_typing();
    return 1;
}

static int input_click(void) { if (inputMode == IN_PICK) { pick_done(); return 1; } return inputMode != IN_NONE; }

/* "STR" or "Strength": a stat by its name, else by its built-in kind's name. */
static cJSON *stat_named(const char *name)
{
    static const char *KINDS[12] = { "None", "Strength", "Dexterity", "Intelligence", "Combat", "Magic", "Hits", "Mana", "Level", "Exp", "TrainingPoints", "NextExp" };
    cJSON *s;
    cJSON_ArrayForEach(s, get(get(project(), "Stats"), "Stats")) { const char *n = str(s, "Name"); size_t i = 0; while (n[i] && name[i] && tolower((unsigned char)n[i]) == tolower((unsigned char)name[i])) i++; if (!n[i] && !name[i]) return s; }
    cJSON_ArrayForEach(s, get(get(project(), "Stats"), "Stats"))
    {
        int kind = num(s, "Kind", 0);
        if (kind <= 0 || kind > 11) continue;
        const char *n = KINDS[kind]; size_t i = 0; while (n[i] && name[i] && tolower((unsigned char)n[i]) == tolower((unsigned char)name[i])) i++;
        if (!n[i] && !name[i]) return s;
    }
    return NULL;
}

static int making_command(int type, cJSON *params, int who, int token, double (*formula)(const char *key))
{
    char words[600];
    switch (type)
    {
        case C_U6SceneTypeText:
        {
            if (u6_talking() && dialog_showing() && !dialog_reply_count() && !u6_talk_more_waiting()) dialog_go_on(); /* the line before it counts as read */
            scene_words(str(params, "Prompt"), inputPrompt, sizeof inputPrompt);
            int longest = (int)formula("Letters"); inputLongest = longest < 1 ? 1 : longest > 60 ? 60 : longest;
            snprintf(inputTyped, sizeof inputTyped, "%s", u6_formula_text("Start"));
            if ((int)strlen(inputTyped) > inputLongest) inputTyped[inputLongest] = 0;
            inputX = (int)formula("X"); inputY = (int)formula("Y");
            snprintf(inputVar, sizeof inputVar, "%s", str(params, "Variable"));
            inputMode = IN_TYPE; inputToken = token;
            show_typing();
            return 0;
        }
        case C_U6ScenePickPicture:
        {
            const char *group = str(params, "Group");
            while (*group == ' ') group++;
            snprintf(pickGroup, sizeof pickGroup, "%s", group);
            size_t gl = strlen(pickGroup); while (gl && pickGroup[gl - 1] == ' ') pickGroup[--gl] = 0;
            pickCount = 0;
            for (int i = 0; pickCount < 128; i++) { const Sprite *s = sprite_in_group(pickGroup, i); if (!s) break; if (s->frameCount > 0) pickPictures[pickCount++] = s; }
            if (pickCount == 0) return -1;
            inputX = (int)formula("X"); inputY = (int)formula("Y"); pickW = (int)formula("Width"); pickH = (int)formula("Height");
            pickAt = (int)formula("Start") - 1; if (pickAt < 0) pickAt = 0; if (pickAt >= pickCount) pickAt = pickCount - 1;
            scene_words(str(params, "Prompt"), inputPrompt, sizeof inputPrompt);
            snprintf(inputVar, sizeof inputVar, "%s", str(params, "Variable"));
            pickPortrait = !strcmp(str(params, "Portrait"), "True"); pickWho = who;
            inputMode = IN_PICK; inputToken = token;
            show_pick();
            return 0;
        }
        case C_U6SetGender:
        {
            cJSON *ch = character_json(who);
            if (!ch) return -1;
            const char *how = str(params, "Gender");
            int female = !strcmp(how, "Female") || (!strncmp(how, "By", 2) && (int)script_get_number(str(params, "Variable")) == 2);
            cJSON_DeleteItemFromObject(ch, "Gender"); cJSON_AddNumberToObject(ch, "Gender", female ? 1 : 0); /* CharacterGender Male 0, Female 1 */
            return 1;
        }
        case C_U6SceneDistributePoints:
        {
            cJSON *ch = character_json(who);
            if (!ch) return -1;
            pointCount = 0;
            snprintf(words, sizeof words, "%s", str(params, "Stats"));
            for (char *line = strtok(words, "\n"); line && pointCount < 16; line = strtok(NULL, "\n"))
            {
                while (*line == ' ' || *line == '\r') line++;
                size_t l = strlen(line); while (l && (line[l - 1] == ' ' || line[l - 1] == '\r')) line[--l] = 0;
                cJSON *s = *line ? stat_named(line) : NULL;
                if (s) pointStats[pointCount++] = s;
            }
            if (pointCount == 0) return -1;
            pointLeft = (int)formula("Points"); if (pointLeft < 0) pointLeft = 0;
            pointLeast = (int)formula("Least"); pointMost = (int)formula("Most"); if (pointMost <= 0) pointMost = 99;
            pointSpendAll = strcmp(str(params, "SpendAll"), "False") != 0;
            for (int i = 0; i < pointCount; i++)
            {
                char key[16]; snprintf(key, sizeof key, "%d", num(pointStats[i], "Id", -1));
                const char *v = str(get(ch, "StatValues"), key);
                int value = v[0] ? atoi(v) : pointLeast;
                pointValues[i] = value > pointLeast ? value : pointLeast;
            }
            pointRow = 0; pointWho = who;
            inputX = (int)formula("X"); inputY = (int)formula("Y");
            scene_words(str(params, "Prompt"), inputPrompt, sizeof inputPrompt);
            snprintf(pointLeftVar, sizeof pointLeftVar, "%s", str(params, "LeftVariable"));
            inputMode = IN_POINTS; inputToken = token;
            show_points();
            return 0;
        }
    }
    return 1;
}

/* ======================= The Ultima 6 tab's commands ======================= */

static int run_level_event(const char *name, int who);

int u6_command(int type, cJSON *params, int whoCharacter, int activator, int token, double (*formula)(const char *key))
{
    char text[1200];
    switch (type)
    {
        case C_U6SceneBegin: begin_scene(!strcmp(str(params, "Area"), "The Game View"), str(params, "Colour")); return 1;
        case C_U6SceneEnd: end_scene(); return 1;
        case C_U6SceneRemove: if (sceneOn) scene_remove(str(params, "Name")); return 1;
        case C_U6ScenePicture:
        {
            const Sprite *s = str(params, "SpriteName")[0] ? sprite_find(str(params, "SpriteGroup"), str(params, "SpriteName")) : NULL;
            if (!sceneOn || !s || s->frameCount <= 0) return -1;
            const Picture *first = sprite_picture_at(s, 0);
            int w = (int)formula("Width"), h = (int)formula("Height"), x = (int)formula("X"), y = (int)formula("Y");
            int fill = !strcmp(str(params, "Fill"), "True");
            if (fill) { x = 0; y = 0; w = sceneW; h = sceneH; }
            if (w <= 0) w = first ? first->width : 16;
            if (h <= 0) h = first ? first->height : 16;
            if (str(params, "Name")[0]) scene_remove(str(params, "Name")); /* the same name again: the picture changes */
            ScenePart *p = scene_add(str(params, "Name"));
            if (p) { p->sprite = s; p->x = x; p->y = y; p->w = w; p->h = h; p->fit = fill; }
            return 1;
        }
        case C_U6SceneText:
        {
            scene_words(str(params, "Text"), text, sizeof text);
            if (!strcmp(str(params, "Where"), "In the text area") || !sceneOn) { u6_say(text); return 1; }
            int x = (int)formula("X"), y = (int)formula("Y"), width = (int)formula("Width");
            if (width <= 0) width = sceneW - x;
            if (str(params, "Name")[0]) scene_remove(str(params, "Name"));
            ScenePart *p = scene_add(str(params, "Name"));
            if (!p) return 1;
            p->x = x; p->y = y; p->w = width;
            snprintf(p->font, sizeof p->font, "%s", str(params, "Font"));
            const char *col = str(params, "Colour");
            if (col[0]) { char hex[16]; snprintf(hex, sizeof hex, "%s%s", col[0] == '#' ? "" : "#", col); p->colour = colour_of(hex); }
            p->text = wrapped_text(text, width, p->font);
            return 1;
        }
        case C_U6SceneWaitKey:
            if (!sceneOn) return -1;
            if (str(params, "Prompt")[0]) u6_say(str(params, "Prompt"));
            sceneKeyToken = token;
            return 0;
        case C_U6SceneChoice:
        {
            char choices[9][128];
            int n = choices_of(str(params, "Choices"), choices, 9);
            if (n == 0) return -1;
            char question[600]; scene_words(str(params, "Question"), question, sizeof question);
            char letters[16]; for (int i = 0; i < n; i++) letters[i] = (char)toupper((unsigned char)choices[i][0]); letters[n] = 0;
            if (!sceneOn)
            {
                /* no scene up: asked in the text area - the answers numbered; their number or first letter answers */
                if (question[0]) u6_say(question);
                for (int i = 0; i < n; i++) { char line[160]; snprintf(line, sizeof line, "%d. %s", i + 1, choices[i]); u6_say(line); }
                char allowed[24]; int k = 0;
                for (int i = 0; i < n; i++) allowed[k++] = (char)('1' + i);
                for (int i = 0; i < n; i++) allowed[k++] = letters[i];
                allowed[k] = 0;
                snprintf(askChoiceVar, sizeof askChoiceVar, "%s", str(params, "Variable"));
                snprintf(askChoiceLetters, sizeof askChoiceLetters, "%s", letters);
                askChoiceToken = token;
                ask_letter("> ", allowed, ask_choice_answer);
                return 0;
            }
            int x = (int)formula("X"), y = (int)formula("Y");
            int lineH = line_height(font_get("InGameFont"));
            char all[1600] = "";
            int questionLines = 0;
            if (question[0]) { char *q = wrapped_text(question, sceneW - x, ""); snprintf(all, sizeof all, "%s", q); for (char *c = q; *c; c++) if (*c == '\n') questionLines++; questionLines++; free(q); }
            for (int i = 0; i < n; i++) { size_t at = strlen(all); snprintf(all + at, sizeof all - at, "%s%s", at ? "\n" : "", choices[i]); }
            scene_remove("choice");
            ScenePart *p = scene_add("choice");
            if (p) { p->x = x; p->y = y; p->w = sceneW - x; size_t l = strlen(all); p->text = (char *)malloc(l + 1); memcpy(p->text, all, l + 1); }
            snprintf(choiceKeys, sizeof choiceKeys, "%s", letters);
            snprintf(choiceVar, sizeof choiceVar, "%s", str(params, "Variable"));
            choiceCount = n; choiceX = x; choiceY = y + questionLines * lineH; choiceLineH = lineH;
            choiceToken = token;
            return 0;
        }
        case C_U6SetCombatMode:
        {
            const char *mode = str(params, "Mode");
            int ok = 0; for (int i = 0; i < 7; i++) ok |= !strcmp(mode, MODES[i]);
            if (whoCharacter < 0 || !ok) return -1;
            if (whoCharacter == player_char() && strcmp(mode, "Command")) return -1; /* the avatar is always in Command */
            set_mode(whoCharacter, mode);
            return 1;
        }
        case C_U6ChangePortrait:
        {
            cJSON *who = character_json(whoCharacter);
            if (!who) return -1;
            if (!strcmp(str(params, "Own"), "True"))
            {
                const char *own = str(who, "U6OwnFace");
                if (!own[0]) return 1; /* never changed */
                char group[64], name[64]; const char *slash = strchr(own, '/');
                snprintf(group, sizeof group, "%.*s", slash ? (int)(slash - own) : (int)strlen(own), own);
                snprintf(name, sizeof name, "%s", slash ? slash + 1 : "");
                cJSON_DeleteItemFromObject(who, "FaceSpriteGroup"); cJSON_AddStringToObject(who, "FaceSpriteGroup", group);
                cJSON_DeleteItemFromObject(who, "FaceSpriteName"); cJSON_AddStringToObject(who, "FaceSpriteName", name);
                cJSON_DeleteItemFromObject(who, "U6OwnFace"); cJSON_AddStringToObject(who, "U6OwnFace", "");
                return 1;
            }
            if (!str(params, "SpriteName")[0]) return -1;
            if (!str(who, "U6OwnFace")[0]) { char own[140]; snprintf(own, sizeof own, "%s/%s", str(who, "FaceSpriteGroup"), str(who, "FaceSpriteName")); cJSON_DeleteItemFromObject(who, "U6OwnFace"); cJSON_AddStringToObject(who, "U6OwnFace", own); }
            cJSON_DeleteItemFromObject(who, "FaceSpriteGroup"); cJSON_AddStringToObject(who, "FaceSpriteGroup", str(params, "SpriteGroup"));
            cJSON_DeleteItemFromObject(who, "FaceSpriteName"); cJSON_AddStringToObject(who, "FaceSpriteName", str(params, "SpriteName"));
            return 1;
        }
        case C_U6ShowHidePiece: case C_U6ShowHidePanel:
        {
            cJSON *p = piece_named(str(params, "Piece"));
            if (!p || (type == C_U6ShowHidePanel && !is_custom_panel(p))) return -1;
            int now = own_shown(p); const char *v = str(params, "Value");
            HudState *s = hud_state(num(p, "Id", -1), 1);
            if (!s) return -1;
            s->shown = !strcmp(v, "Hidden") || !strcmp(v, "Closed") ? 0 : !strcmp(v, "Toggled") ? !now : 1;
            hudVersion++;
            return 1;
        }
        case C_U6SetPiecePicture:
        {
            cJSON *p = piece_named(str(params, "Piece")); HudState *s = p ? hud_state(num(p, "Id", -1), 1) : NULL;
            if (!s) return -1;
            if (!strcmp(str(params, "Own"), "True") || !str(params, "SpriteName")[0]) s->group[0] = s->name[0] = 0;
            else { snprintf(s->group, sizeof s->group, "%s", str(params, "SpriteGroup")); snprintf(s->name, sizeof s->name, "%s", str(params, "SpriteName")); }
            hud_state_rebuild(s);
            return 1;
        }
        case C_U6SetPieceText:
        {
            cJSON *p = piece_named(str(params, "Piece")); HudState *s = p ? hud_state(num(p, "Id", -1), 1) : NULL;
            if (!s) return -1;
            char words[600]; scene_words(str(params, "Text"), words, sizeof words); /* {formulas} worked out now */
            free(s->text); s->text = strdup(words);
            hud_state_rebuild(s);
            return 1;
        }
        case C_U6MovePiece:
        {
            cJSON *p = piece_named(str(params, "Piece")); HudState *s = p ? hud_state(num(p, "Id", -1), 1) : NULL;
            if (!s) return -1;
            if (!strcmp(str(params, "Back"), "True")) s->hasX = s->hasY = 0;
            else { s->hasX = s->hasY = 1; s->x = (int)floor(formula("X") + 0.5); s->y = (int)floor(formula("Y") + 0.5); }
            hud_state_rebuild(s);
            return 1;
        }
        case C_U6EnablePanelKey: case C_U6DisablePanelKey:
        {
            cJSON *p = piece_named(str(params, "Piece"));
            if (!p || !is_custom_panel(p)) return -1;
            HudState *s = hud_state(num(p, "Id", -1), 1);
            if (!s) return -1;
            s->keyOff = type == C_U6DisablePanelKey;
            return 1;
        }
        case C_U6ChangeHudFrame:
            frameOverride = str(params, "SpriteName")[0] ? sprite_find(str(params, "SpriteGroup"), str(params, "SpriteName")) : NULL;
            return 1;
        case C_U6Meditate:
        {
            if (!u6_levels_on()) return -1;
            static const struct { const char *virtue; int s, d, i; } SHRINES[8] = { { "Honesty", 0, 0, 3 }, { "Compassion", 0, 3, 0 }, { "Valor", 3, 0, 0 }, { "Justice", 0, 1, 1 },
                                                                                    { "Sacrifice", 1, 1, 0 }, { "Honor", 1, 0, 1 }, { "Spirituality", 1, 1, 1 }, { "Humility", 0, 0, 0 } };
            int s = (int)formula("Str"), d = (int)formula("Dex"), in = (int)formula("Int");
            for (int k = 0; k < 8; k++) if (!strcmp(SHRINES[k].virtue, str(params, "Virtue"))) { s = SHRINES[k].s; d = SHRINES[k].d; in = SHRINES[k].i; }
            int ids[16], n = party_list(ids, 16), any = 0;
            for (int k = 0; k < n; k++)
            {
                if (vitals_dead(ids[k]) || !has_earned(stat(ids[k], BK_LEVEL, 1), stat(ids[k], BK_EXP, 0))) continue;
                rise_level(ids[k], s, d, in, hits_per_level(), 1);
                any = 1;
            }
            if (!any) u6_say(wordf("NotReady", "", char_name(player_char())));
            return any ? 1 : -1;
        }
        case C_U6RiseLevel:
        {
            if (!u6_levels_on()) return -1;
            int targets[16], n = 0;
            if (!strcmp(str(params, "Party"), "True")) { int ids[16], m = party_list(ids, 16); for (int k = 0; k < m; k++) if (!vitals_dead(ids[k])) targets[n++] = ids[k]; }
            else if (whoCharacter >= 0) targets[n++] = whoCharacter;
            int onlyIfEarned = strcmp(str(params, "OnlyIfEarned"), "False") != 0, tell = strcmp(str(params, "Tell"), "False") != 0;
            const char *hitsText = str(params, "Hits"); while (*hitsText == ' ') hitsText++;
            int hits = hitsText[0] ? (int)formula("Hits") : hits_per_level(), any = 0;
            for (int k = 0; k < n; k++)
            {
                int level = stat(targets[k], BK_LEVEL, 1), top = max_level(), lock = num(character_json(targets[k]), "LevelLock", 0), cap = lock > 0 && lock < top ? lock : top;
                if (level >= cap || (onlyIfEarned && !has_earned(level, stat(targets[k], BK_EXP, 0)))) continue;
                rise_level(targets[k], (int)formula("Str"), (int)formula("Dex"), (int)formula("Int"), hits, tell);
                any = 1;
            }
            if (!any && str(params, "NotReady")[0]) { scene_words(str(params, "NotReady"), text, sizeof text); u6_say(text); }
            return any ? 1 : -1;
        }
        case C_U6RunLevelEvent:
            return run_level_event(str(params, "Name"), activator) ? 1 : -1;
        case C_U6SceneTypeText: case C_U6ScenePickPicture: case C_U6SetGender: case C_U6SceneDistributePoints:
            return making_command(type, params, whoCharacter, token, formula);
        case C_U6AskKey: case C_U6SetKey: case C_U6GetKeyName: case C_U6ResetKeys:
            return key_command(type, params, token);
        case C_U6ConversationStyle:
        {
            if (talkStyle) { cJSON_Delete(talkStyle); talkStyle = NULL; }
            if (!strcmp(str(params, "Set"), "Back to Ultima 6's")) return 1;
            #define OR(key, otherwise) (str(params, key)[0] ? str(params, key) : otherwise)
            #define N(key, otherwise) (str(params, key)[0] ? (int)formula(key) : otherwise)
            talkStyle = cJSON_CreateObject();
            cJSON_AddStringToObject(talkStyle, "Answers", OR("Answers", "Typed keywords (Ultima 6)"));
            cJSON_AddBoolToObject(talkStyle, "MarkKeywords", strcmp(str(params, "MarkKeywords"), "False") != 0);
            cJSON_AddStringToObject(talkStyle, "KeywordColor", str(params, "KeywordColour"));
            cJSON_AddStringToObject(talkStyle, "Portrait", OR("Portrait", "In the HUD's Portrait (Ultima 6)"));
            cJSON_AddStringToObject(talkStyle, "Words", OR("Words", "In the HUD's text area (Ultima 6)"));
            cJSON_AddNumberToObject(talkStyle, "BoxX", N("BoxX", 8)); cJSON_AddNumberToObject(talkStyle, "BoxY", N("BoxY", 120));
            cJSON_AddNumberToObject(talkStyle, "BoxWidth", N("BoxWidth", 304)); cJSON_AddNumberToObject(talkStyle, "BoxHeight", N("BoxHeight", 72));
            cJSON_AddStringToObject(talkStyle, "BoxColor", str(params, "BoxColour"));
            cJSON *sprite = cJSON_AddObjectToObject(talkStyle, "BoxSprite");
            cJSON_AddStringToObject(sprite, "SpriteGroup", str(params, "SpriteGroup")); cJSON_AddStringToObject(sprite, "SpriteName", str(params, "SpriteName"));
            cJSON_AddStringToObject(talkStyle, "Font", str(params, "Font"));
            cJSON_AddStringToObject(talkStyle, "TextColor", str(params, "TextColour"));
            #undef OR
            #undef N
            return 1;
        }
        default:
            return 1; /* the rest of the Ultima 6 tab's commands: next (keys, conversation style, character creation) */
    }
}

/* Run Leveling Event: the system's event by its name, its commands run (with the one it is about as its Self). */
static int run_level_event(const char *name, int who)
{
    cJSON *s = level_system(), *e;
    if (!s) return 0;
    char want[80]; snprintf(want, sizeof want, "%s", name);
    char *w = want; while (*w == ' ') w++;
    size_t wl = strlen(w); while (wl && w[wl - 1] == ' ') w[--wl] = 0;
    cJSON_ArrayForEach(e, get(s, "Events"))
    {
        const char *n = str(e, "Name");
        size_t i = 0; while (n[i] && w[i] && tolower((unsigned char)n[i]) == tolower((unsigned char)w[i])) i++;
        if (n[i] || w[i]) continue;
        int actor = who >= 0 ? world_actor_by_character(who) : 0;
        events_run_commands(get(e, "Commands"), actor >= 0 ? actor : 0);
        return 1;
    }
    return 0;
}

/* ---- The HUD kept drawn (the Amiga 68k's speed: Ultima 6 must never struggle) ----
   Most frames nothing on the HUD changes: it is composed once into its own picture and only copied round the Game View;
   composed again only when what it shows changes (its signature: the text, the prompt, the panel, the clock, a blink, an
   animated sprite's frame). A HUD with a piece over the Game View is simply drawn each frame. */

static uint32_t fnv(uint32_t h, const void *data, size_t n) { const unsigned char *p = (const unsigned char *)data; while (n--) { h ^= *p++; h *= 16777619u; } return h; }
static uint32_t fnv_str(uint32_t h, const char *s) { return fnv(h, s ? s : "", s ? strlen(s) + 1 : 1); }
static uint32_t fnv_int(uint32_t h, int v) { return fnv(h, &v, sizeof v); }

static int hud_over_view(void)
{
    cJSON *p;
    cJSON_ArrayForEach(p, get(hud, "Pieces"))
    {
        int kind = num(p, "Kind", -1), x = num(p, "X", 0), y = num(p, "Y", 0), w = num(p, "Width", 16), h = num(p, "Height", 16);
        if (kind == P_GAMEVIEW) continue;
        if (x < vx + vw && x + w > vx && y < vy + vh && y + h > vy) return 1;
    }
    return 0;
}

static uint32_t sprite_sig(uint32_t h, const Sprite *s, uint32_t ms) { return s && s->frameCount > 1 ? fnv_int(h, sprite_frame_at(s, ms)) : h; }

/* Everything the HUD shows, hashed. */
static uint32_t hud_signature(uint32_t ms)
{
    uint32_t h = 2166136261u;
    h = fnv_str(h, prompt); h = fnv_int(h, lineCount);
    for (int i = lineCount > 12 ? lineCount - 12 : 0; i < lineCount; i++) h = fnv_str(h, lines[i]);
    int state[] = { verb, focus, panelCursor, hudMember, panelView, partyView, looked, talking, casting, castPage, castPicked, events_day(), events_hour(), events_minute(),
                    (int)(ms / 500 % 2), moreAt, moreCount, replyPick, combat_mode(), openBag != NULL };
    h = fnv(h, state, sizeof state);
    h = fnv_int(h, hudVersion); /* the HUD commands' changes */
    h = fnv_str(h, talkerName); h = fnv(h, &talkerFace, sizeof talkerFace); h = fnv(h, &frameOverride, sizeof frameOverride); h = fnv_str(h, castTyped);
    /* the shown member's things and stats */
    int member = member_shown();
    cJSON *mj = character_json(member);
    h = fnv_str(h, str(mj, "Name")); h = fnv_str(h, str(mj, "FaceSpriteName")); h = fnv_str(h, str(mj, "U6CombatMode"));
    cJSON *v; cJSON_ArrayForEach(v, get(mj, "StatValues")) h = fnv_str(h, cJSON_IsString(v) ? v->valuestring : "");
    Item *carried[96]; int n = carried_items(member, carried, 96);
    for (int i = 0; i < n; i++) { h = fnv(h, &carried[i], sizeof carried[i]); h = fnv_int(h, items_quantity(carried[i])); }
    for (int slot = 0; slot < 15; slot++) { Item *it = items_in_slot(member, slot); h = fnv(h, &it, sizeof it); }
    /* animated pieces: their frame now */
    const Sprite *frameSprite = frameOverride ? frameOverride : sprite_of(get(hud, "Frame"));
    h = sprite_sig(h, frameSprite, ms);
    cJSON *p; cJSON_ArrayForEach(p, get(hud, "Pieces")) h = sprite_sig(h, sprite_of(get(p, "Sprite")), ms);
    if (looked >= 0) h = fnv_int(h, looked);
    return h;
}



void u6_draw(uint32_t *pixels, uint32_t ms)
{
    if (!hasHud) return;
    screen = pixels;
    talk_tick(ms); /* a talk over: the panel back to the member */
    draw_cursors(0, ms); /* a verb's target cell on the map (under the frame) */
    if (hudCacheable < 0) hudCacheable = !hud_over_view();
    if (hudCacheable)
    {
        if (!hudCache) { hudCache = (uint32_t *)malloc(sizeof(uint32_t) * FRAME_PIXELS); hudSig = 0; }
        if (hudCache)
        {
            uint32_t sig = hud_signature(ms);
            if (sig != hudSig || !hudSig) { screen = hudCache; hud_compose(ms); hudSig = sig ? sig : 1; screen = pixels; }
            /* the HUD's rows round the Game View, copied */
            int x0 = vx * frame_scale, x1 = (vx + vw) * frame_scale, y0 = vy * frame_scale, y1 = (vy + vh) * frame_scale;
            for (int y = 0; y < FRAME_H; y++)
            {
                uint32_t *d = pixels + (size_t)y * FRAME_W; const uint32_t *s = hudCache + (size_t)y * FRAME_W;
                if (y < y0 || y >= y1) { memcpy(d, s, sizeof(uint32_t) * FRAME_W); continue; }
                if (x0 > 0) memcpy(d, s, sizeof(uint32_t) * (size_t)x0);
                if (x1 < FRAME_W) memcpy(d + x1, s + x1, sizeof(uint32_t) * (size_t)(FRAME_W - x1));
            }
            draw_talk_box(ms); /* Conversation Style: the words in a box of their own (it may lie over the view) */
            return;
        }
    }
    hud_compose(ms);
    draw_talk_box(ms);
}

