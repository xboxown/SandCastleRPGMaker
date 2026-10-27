/*
 * Ultima VII's number slider, as the Windows engine draws it (Editor/NumberSliderControl.cs; Exult's Slider_gump.cc,
 * Slider_widget.cc), its look System Settings > Slider:
 *  - Classic: a background; at the slider position a left arrow, a track of TrackWidth pixels with a diamond on it, and a
 *    right arrow; the number right-aligned ending at NumberX; a check mark. Without sprites, a plain stand-in.
 *  - Custom: a background and pieces placed on it - buttons (frame 2 while pressed, a sound each, minus and plus repeat while
 *    held), the number (a digit sprite, frames 1-10 = 0-9 and an 11th a blank, or the game font), a track with its thumb,
 *    decorations; a sound per last digit, or the change sound.
 * The arrows step by Step, the diamond can be dragged or the track clicked (snapping to Step); Left and Right step, Enter
 * accepts, Escape cancels. Shown in the middle of the screen with its question above it (ShowNumberSlider).
 */
#include "slider.h"
#include "pictures.h"
#include "font.h"
#include "platform.h"
#include "sound.h"
#include "world.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <string.h>

enum { MAX_VALUE = 999 };
enum { P_DECREASE, P_INCREASE, P_ACCEPT, P_CANCEL, P_NUMBER, P_TRACK, P_DECORATION };
enum { HIT_NONE, HIT_LEFT, HIT_RIGHT, HIT_TRACK, HIT_DIAMOND, HIT_CHECK, HIT_PIECE };

static struct
{
    int open, min, max, step, value;
    char question[256];
    void (*done)(int, int, void *);
    void *context;
    int dragging, pressed;          /* the custom button held down (-1: none) */
    int repeatDir; uint32_t repeatAt;
    int x, y, w, h;                 /* the slider's place on the screen (its question above) */
} s;

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static const char *str(cJSON *o, const char *k) { const char *x = cJSON_GetStringValue(get(o, k)); return x ? x : ""; }
static cJSON *settings(void) { return get(get(get((cJSON *)world_json(), "Project"), "Settings"), "Slider"); }
static int custom(void) { return num(settings(), "Style", 0) == 1; }
static cJSON *design(void) { return get(settings(), "Custom"); }

/* A sprite link's sprite (frames), or NULL when it isn't set. */
static const Sprite *link_sprite(cJSON *link) { return str(link, "SpriteName")[0] ? sprite_find(str(link, "SpriteGroup"), str(link, "SpriteName")) : NULL; }
static const Picture *frame_of(const Sprite *sp, int frame) { return sp && frame < sp->frameCount ? frame_picture(&sp->frames[frame]) : NULL; }
static const Picture *link_picture(cJSON *link) { return frame_of(link_sprite(link), 0); }

static int clampi(int v, int lo, int hi) { return v < lo ? lo : v > hi ? hi : v; }

// ---------- drawing ----------

static uint32_t *px;

static void blend(int x, int y, uint32_t c)
{
    if (x < 0 || y < 0 || x >= SCREEN_W || y >= SCREEN_H) return;
    uint32_t a = c >> 24, *d = &px[y * SCREEN_W + x];
    if (!a) return;
    if (a == 255) { *d = c; return; }
    uint32_t r = (((c >> 16) & 255) * a + ((*d >> 16) & 255) * (255 - a)) / 255, g = (((c >> 8) & 255) * a + ((*d >> 8) & 255) * (255 - a)) / 255, b = ((c & 255) * a + (*d & 255) * (255 - a)) / 255;
    *d = 0xFF000000u | (r << 16) | (g << 8) | b;
}

static void picture(const Picture *p, int x, int y)
{
    if (!p) return;
    for (int j = 0; j < p->height; j++) for (int i = 0; i < p->width; i++) blend(x + i, y + j, p->pixels[j * p->width + i]);
}

static void rect(int x, int y, int w, int h, uint32_t c) { for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) blend(x + i, y + j, c); }
static void frame_rect(int x, int y, int w, int h, uint32_t c) { rect(x, y, w, 1, c); rect(x, y + h - 1, w, 1, c); rect(x, y, 1, h, c); rect(x + w - 1, y, 1, h, c); }

/* The stand-in arrow (a triangle) and diamond. */
static void arrow(int x, int y, int w, int h, int left)
{
    for (int j = 0; j < h; j++)
    {
        double half = h / 2.0, reach = w * (1 - fabs(j + 0.5 - half) / half);
        for (int i = 0; i < w; i++) { int k = left ? w - 1 - i : i; if (k + 0.5 <= reach) blend(x + i, y + j, 0xFFE8D8A8u); }
    }
}

static void diamond(int x, int y, int w, int h)
{
    for (int j = 0; j < h; j++) for (int i = 0; i < w; i++)
        if (fabs(i + 0.5 - w / 2.0) / (w / 2.0) + fabs(j + 0.5 - h / 2.0) / (h / 2.0) <= 1.0) blend(x + i, y + j, 0xFFF0C030u);
}

static void check_mark(int x, int y, uint32_t c) { for (int i = 0; i < 3; i++) { rect(x + i, y + 3 + i, 1, 2, c); } for (int i = 0; i < 5; i++) rect(x + 3 + i, y + 4 - i, 1, 2, c); }
static void cross(int x, int y, uint32_t c) { for (int i = 0; i < 6; i++) { rect(x + i, y + i, 1, 1, c); rect(x + 5 - i, y + i, 1, 1, c); } }

// ---------- the Classic layout ----------

typedef struct { int leftW, leftH, diamondW, diamondH, leftX, y, xmin, xmax, rightX; } Classic;

static Classic classic(void)
{
    cJSON *st = settings();
    const Picture *l = link_picture(get(st, "LeftArrow")), *d = link_picture(get(st, "Diamond"));
    Classic c;
    c.leftW = l ? l->width : 7; c.leftH = l ? l->height : 9;
    c.diamondW = d ? d->width : 7; c.diamondH = d ? d->height : 7;
    c.leftX = num(st, "SliderX", 24); c.y = num(st, "SliderY", 6);
    c.xmin = c.leftX + c.leftW + 1;
    c.xmax = c.xmin + num(st, "TrackWidth", 64) - c.diamondW;
    c.rightX = c.leftX + c.leftW + num(st, "TrackWidth", 64) + 1;
    return c;
}

static void size_of(int *w, int *h)
{
    cJSON *st = settings();
    if (!custom())
    {
        const Picture *bg = link_picture(get(st, "Background"));
        int a = num(st, "NumberX", 128) + 8, b = num(st, "SliderX", 24) + num(st, "TrackWidth", 64) + 30, c = num(st, "CheckY", 18) + 12, d = num(st, "SliderY", 6) + 20;
        *w = bg ? bg->width : a > b ? a : b;
        *h = bg ? bg->height : c > d ? c : d;
        return;
    }
    const Picture *bg = link_picture(get(design(), "Background"));
    if (bg) { *w = bg->width; *h = bg->height; return; }
    int width = 0, height = 0;
    cJSON *piece;
    cJSON_ArrayForEach(piece, get(design(), "Pieces"))
    {
        const Picture *art = link_picture(get(piece, "Sprite"));
        int kind = num(piece, "Kind", P_DECORATION);
        int pw = kind == P_TRACK ? num(piece, "Width", 64) : art ? art->width : 14, ph = art ? art->height : 12;
        if (kind == P_NUMBER) { int digits = num(piece, "DigitCount", 3); pw = pw * (digits > 1 ? digits : 1); if (pw < 18) pw = 18; }
        if (num(piece, "X", 0) + pw + 4 > width) width = num(piece, "X", 0) + pw + 4;
        if (num(piece, "Y", 0) + ph + 4 > height) height = num(piece, "Y", 0) + ph + 4;
    }
    *w = width > 20 ? width : 20;
    *h = height > 12 ? height : 12;
}

/* The slider in the middle of the screen, its question 3 pixels above it (ShowNumberSlider's panel). */
static void place(void)
{
    const Font *f = font_get("InGameFont");
    size_of(&s.w, &s.h);
    int qh = s.question[0] ? font_line_height(f) + 3 : 0;
    s.x = (SCREEN_W - s.w) / 2;
    s.y = (SCREEN_H - (s.h + qh)) / 2 + qh;
}

// ---------- the value ----------

static void changed(void)
{
    cJSON *st = settings();
    if (!custom()) { sound_play(num(st, "ClickSoundResourceId", -1)); return; }
    cJSON *digits = get(design(), "DigitSoundIds");
    int digit = s.value % 10, sound = digit < cJSON_GetArraySize(digits) && cJSON_IsNumber(cJSON_GetArrayItem(digits, digit)) ? cJSON_GetArrayItem(digits, digit)->valueint : -1;
    sound_play(sound >= 0 ? sound : num(design(), "ChangeSoundResourceId", -1));
}

static void set_value(int value)
{
    value = clampi(value, s.min, s.max);
    if (value == s.value) return;
    s.value = value;
    changed();
}

static void step_by(int direction) { set_value(s.value + direction * s.step); }

static void set_from(double x, double xmin, double xmax)
{
    double t = xmax <= xmin ? 0 : (x - xmin) / (xmax - xmin);
    if (t < 0) t = 0;
    if (t > 1) t = 1;
    int raw = s.min + (int)floor(t * (s.max - s.min) + 0.5);
    set_value(s.min + (int)floor((raw - s.min) / (double)s.step + 0.5) * s.step);
}

static void finish(int ok)
{
    if (!s.open) return;
    s.open = 0;
    s.pressed = -1; s.repeatDir = 0; s.dragging = 0;
    if (s.done) s.done(ok, s.value, s.context);
}

void slider_open(const char *question, int min, int max, int step, int value, void (*done)(int ok, int value, void *context), void *context)
{
    memset(&s, 0, sizeof s);
    s.open = 1;
    s.min = clampi(min < max ? min : max, 0, MAX_VALUE);
    s.max = clampi(min < max ? max : min, 0, MAX_VALUE);
    s.step = step > 1 ? step : 1;
    s.value = clampi(value, s.min, s.max);
    s.pressed = -1;
    s.done = done; s.context = context;
    const char *q = question ? question : "";
    while (*q == ' ' || *q == '\t' || *q == '\n' || *q == '\r') q++;
    snprintf(s.question, sizeof s.question, "%s", q);
    for (size_t n = strlen(s.question); n > 0 && (s.question[n - 1] == ' ' || s.question[n - 1] == '\n' || s.question[n - 1] == '\r'); n--) s.question[n - 1] = 0;
}

int slider_open_now(void) { return s.open; }
void slider_reset(void) { memset(&s, 0, sizeof s); s.pressed = -1; }

int slider_key(int key)
{
    if (!s.open) return 0;
    if (key == KEY_LEFT) step_by(-1);
    else if (key == KEY_RIGHT) step_by(+1);
    else if (key == KEY_ENTER) finish(1);
    else if (key == KEY_ESCAPE) finish(0);
    return 1;
}

// ---------- the custom pieces ----------

static int custom_thumb(cJSON *track, int *tw, int *th)
{
    const Picture *art = link_picture(get(track, "Sprite"));
    *tw = art ? art->width : 6; *th = art ? art->height : 8;
    return 1;
}

static void custom_piece_size(cJSON *piece, int *w, int *h)
{
    const Picture *art = link_picture(get(piece, "Sprite"));
    *w = art ? art->width : 12; *h = art ? art->height : 11;
}

/* Which piece is under the point, the topmost first (-1: none); kinds that take clicks only. */
static int custom_hit(int mx, int my)
{
    cJSON *pieces = get(design(), "Pieces");
    for (int i = cJSON_GetArraySize(pieces) - 1; i >= 0; i--)
    {
        cJSON *p = cJSON_GetArrayItem(pieces, i);
        int kind = num(p, "Kind", P_DECORATION), x = s.x + num(p, "X", 0), y = s.y + num(p, "Y", 0), w, h;
        if (kind <= P_CANCEL) custom_piece_size(p, &w, &h);
        else if (kind == P_TRACK) { int tw, th; custom_thumb(p, &tw, &th); w = num(p, "Width", 64) > 1 ? num(p, "Width", 64) : 1; h = th; }
        else continue;
        if (mx >= x && mx < x + w && my >= y && my < y + h) return i;
    }
    return -1;
}

static void custom_track_at(cJSON *track, int mx)
{
    int tw, th;
    custom_thumb(track, &tw, &th);
    double span = num(track, "Width", 64) - tw;
    if (span < 1) span = 1;
    double x = mx - s.x - tw / 2.0;
    set_from(x, num(track, "X", 0), num(track, "X", 0) + span);
}

static cJSON *custom_track(void)
{
    cJSON *p;
    cJSON_ArrayForEach(p, get(design(), "Pieces")) if (num(p, "Kind", P_DECORATION) == P_TRACK) return p;
    return NULL;
}

int slider_mouse_down(int mx, int my)
{
    if (!s.open) return 0;
    place();
    if (custom())
    {
        int i = custom_hit(mx, my);
        if (i < 0) return 1;
        cJSON *p = cJSON_GetArrayItem(get(design(), "Pieces"), i);
        int kind = num(p, "Kind", P_DECORATION);
        if (kind == P_TRACK)
        {
            int tw, th;
            custom_thumb(p, &tw, &th);
            double frac = s.max == s.min ? 0 : (s.value - s.min) / (double)(s.max - s.min);
            int thumbX = s.x + num(p, "X", 0) + (int)(frac * (num(p, "Width", 64) - tw > 0 ? num(p, "Width", 64) - tw : 0));
            if (mx >= thumbX && mx < thumbX + tw) s.dragging = 1; /* on the thumb: dragged */
            else custom_track_at(p, mx);                         /* on the track: the value there */
            return 1;
        }
        s.pressed = i;
        sound_play(num(p, "SoundResourceId", -1));
        if (kind == P_DECREASE || kind == P_INCREASE)
        {
            s.repeatDir = kind == P_DECREASE ? -1 : 1;
            step_by(s.repeatDir);
            s.repeatAt = platform_ticks() + 400; /* held down: after a moment it keeps going */
        }
        else if (kind == P_ACCEPT) finish(1);
        else finish(0);
        return 1;
    }
    Classic c = classic();
    cJSON *st = settings();
    int x = mx - s.x, y = my - s.y;
    const Picture *check = link_picture(get(st, "CheckMark")), *right = link_picture(get(st, "RightArrow"));
    int cw = check ? check->width : 9, ch = check ? check->height : 9;
    double t = s.max == s.min ? 0 : (s.value - s.min) / (double)(s.max - s.min);
    int dx = c.xmin + (int)(t * (c.xmax - c.xmin)), dy = c.y + (c.leftH - c.diamondH) / 2;
    int trackH = c.leftH > c.diamondH ? c.leftH : c.diamondH, rw = right ? right->width : c.leftW, rh = right ? right->height : c.leftH;
    /* the topmost first: the check mark, the diamond, the track, the arrows */
    if (x >= num(st, "CheckX", 22) && x < num(st, "CheckX", 22) + cw && y >= num(st, "CheckY", 18) && y < num(st, "CheckY", 18) + ch) finish(1);
    else if (x >= dx && x < dx + c.diamondW && y >= dy && y < dy + c.diamondH) s.dragging = 1;
    else if (x >= c.xmin && x < c.xmin + num(st, "TrackWidth", 64) && y >= c.y && y < c.y + trackH) set_from(x - c.diamondW / 2.0, c.xmin, c.xmax);
    else if (x >= c.rightX && x < c.rightX + rw && y >= c.y && y < c.y + rh) step_by(+1);
    else if (x >= c.leftX && x < c.leftX + c.leftW && y >= c.y && y < c.y + c.leftH) step_by(-1);
    return 1;
}

void slider_mouse_move(int mx, int my)
{
    (void)my;
    if (!s.open || !s.dragging) return;
    place();
    if (custom()) { cJSON *track = custom_track(); if (track) custom_track_at(track, mx); return; }
    Classic c = classic();
    set_from(mx - s.x - c.diamondW / 2.0, c.xmin, c.xmax);
}

void slider_mouse_up(int mx, int my)
{
    (void)mx; (void)my;
    s.dragging = 0;
    s.pressed = -1;
    s.repeatDir = 0;
}

void slider_draw(uint32_t *pixels, uint32_t ms)
{
    (void)ms;
    if (!s.open) return;
    px = pixels;
    if (s.repeatDir && platform_ticks() >= s.repeatAt) { step_by(s.repeatDir); s.repeatAt = platform_ticks() + 110; }
    place();
    const Font *f = font_get("InGameFont");
    if (s.question[0]) font_draw(f, pixels, (SCREEN_W - font_text_width(f, s.question)) / 2, s.y - 3 - font_line_height(f), s.question, 0xFFFFFFFFu);
    char number[16];
    if (!custom())
    {
        cJSON *st = settings();
        Classic c = classic();
        const Picture *bg = link_picture(get(st, "Background")), *l = link_picture(get(st, "LeftArrow")), *r = link_picture(get(st, "RightArrow"));
        const Picture *d = link_picture(get(st, "Diamond")), *check = link_picture(get(st, "CheckMark"));
        if (bg) picture(bg, s.x, s.y);
        else { rect(s.x, s.y, s.w, s.h, 0xFF6B4A2Bu); frame_rect(s.x, s.y, s.w, s.h, 0xFF2E1E10u); }
        if (!l && !r) rect(s.x + c.xmin, s.y + c.y + c.leftH / 2, num(st, "TrackWidth", 64), 1, 0xFF000000u); /* the stand-in's track line */
        if (l) picture(l, s.x + c.leftX, s.y + c.y); else arrow(s.x + c.leftX, s.y + c.y, c.leftW, c.leftH, 1);
        if (r) picture(r, s.x + c.rightX, s.y + c.y); else arrow(s.x + c.rightX, s.y + c.y, c.leftW, c.leftH, 0);
        double t = s.max == s.min ? 0 : (s.value - s.min) / (double)(s.max - s.min);
        int dx = s.x + c.xmin + (int)(t * (c.xmax - c.xmin)), dy = s.y + c.y + (c.leftH - c.diamondH) / 2;
        if (d) picture(d, dx, dy); else diamond(dx, dy, c.diamondW, c.diamondH);
        if (check) picture(check, s.x + num(st, "CheckX", 22), s.y + num(st, "CheckY", 18)); else check_mark(s.x + num(st, "CheckX", 22), s.y + num(st, "CheckY", 18), 0xFF32CD32u);
        snprintf(number, sizeof number, "%d", s.value);
        font_draw(f, pixels, s.x + num(st, "NumberX", 128) - font_text_width(f, number), s.y + num(st, "NumberY", 7), number, 0xFFFFFFFFu); /* right-aligned, ending at NumberX */
        return;
    }
    const Picture *bg = link_picture(get(design(), "Background"));
    if (bg) picture(bg, s.x, s.y);
    else { rect(s.x, s.y, s.w, s.h, 0xFF30343Au); frame_rect(s.x, s.y, s.w, s.h, 0xFF000000u); }
    cJSON *pieces = get(design(), "Pieces"), *numberPiece = NULL;
    for (int i = 0; i < cJSON_GetArraySize(pieces); i++)
    {
        cJSON *p = cJSON_GetArrayItem(pieces, i);
        int kind = num(p, "Kind", P_DECORATION), x = s.x + num(p, "X", 0), y = s.y + num(p, "Y", 0);
        const Sprite *sp = link_sprite(get(p, "Sprite"));
        if (kind <= P_CANCEL)
        {
            const Picture *look = s.pressed == i && frame_of(sp, 1) ? frame_of(sp, 1) : frame_of(sp, 0);
            if (look) { picture(look, x, y); continue; }
            rect(x, y, 12, 11, 0xFF55606Au); frame_rect(x, y, 12, 11, 0xFF000000u);
            if (kind == P_DECREASE) rect(x + 3, y + 5, 6, 1, 0xFFFFFFFFu);
            else if (kind == P_INCREASE) { rect(x + 3, y + 5, 6, 1, 0xFFFFFFFFu); rect(x + 6, y + 2, 1, 7, 0xFFFFFFFFu); }
            else if (kind == P_ACCEPT) check_mark(x + 2, y + 2, 0xFFFFFFFFu);
            else cross(x + 3, y + 2, 0xFFFFFFFFu);
        }
        else if (kind == P_NUMBER) numberPiece = p;
        else if (kind == P_TRACK)
        {
            int tw, th;
            custom_thumb(p, &tw, &th);
            const Picture *art = frame_of(sp, 0);
            if (!art) rect(x, y + th / 2, num(p, "Width", 64) > 1 ? num(p, "Width", 64) : 1, 1, 0xFFD3D3D3u);
            double t = s.max == s.min ? 0 : (s.value - s.min) / (double)(s.max - s.min);
            int span = num(p, "Width", 64) - tw > 0 ? num(p, "Width", 64) - tw : 0, thumbX = x + (int)(t * span);
            if (art) picture(art, thumbX, y); else diamond(thumbX, y, tw, th);
        }
        else if (frame_of(sp, 0)) picture(frame_of(sp, 0), x, y);
    }
    if (numberPiece) /* the number last, on top (Windows re-adds it at each change) */
    {
        cJSON *p = numberPiece;
        const Sprite *sp = link_sprite(get(p, "Sprite"));
        int count = num(p, "DigitCount", 3);
        char digits[32];
        snprintf(number, sizeof number, "%d", s.value);
        int n = (int)strlen(number), pad = count > n ? count - n : 0;
        int zeros = cJSON_IsFalse(get(p, "LeadingZeros")) ? 0 : 1;
        memset(digits, zeros ? '0' : ' ', (size_t)pad);
        snprintf(digits + pad, sizeof digits - (size_t)pad, "%s", number);
        int x = s.x + num(p, "X", 0), y = s.y + num(p, "Y", 0);
        if (sp && sp->frameCount >= 10)
        {
            for (const char *c = digits; *c; c++)
            {
                int frame = *c == ' ' ? 10 : *c - '0';
                const Picture *pic = frame_of(sp, frame), *zero = frame_of(sp, 0);
                int w = pic ? pic->width : zero ? zero->width : 6;
                if (pic) picture(pic, x, y);
                x += w + num(p, "DigitSpacing", 1);
            }
        }
        else font_draw(f, pixels, x, y, digits, 0xFFFFFFFFu);
    }
}
