#include "blend.h"
#include "dialog.h"
#include "font.h"
#include "u6.h"
#include "u8.h"
#include "world.h"
#include "combat.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* DialogBoxMetrics: the box on the 320 x 200 screen. */
#define MARGIN 4
#define PADDING 4
#define MIN_HEIGHT 32
#define FACE_MAX 48
#define BOX_W (BASE_W - 2 * MARGIN)
/* Where the boxes go (Fill the screen widens the screen): Ultima VII's 320 x 200 in its middle, as U7's MessageLayer is; Ultima 8's
   the whole view, as U8GameWindow's MessageLayer is. */
#define AREA_X (u8_on() ? 0 : BASE_X)
#define AREA_Y (u8_on() ? 0 : BASE_Y)
#define AREA_H (u8_on() ? SCREEN_H : BASE_H)
#define LINES_PER_PAGE 4      /* Exult's NPC text box: four lines (Conversation::set_face_rect) */
#define REPLIES_PER_ROW 5
#define REPLY_ROWS 5
#define SLOT_MORE (-1)
#define SLOT_PREVIOUS (-2)

typedef struct Message
{
    char *title, *text;
    const Sprite *face; int faceRight, position, dim;
    int speakerPlus1; /* who speaks: their character id + 1 (0: none) */
    const Sprite *frame;
    const Font *wordsFont; /* a language not understood (NULL: the DialogFont) */
    int replyCount, replyIds[DIALOG_MAX_REPLIES];
    char *replies[DIALOG_MAX_REPLIES];
    void (*chosen)(void *, int); void *context;
    void (*afterRead)(void *); void *afterContext; /* not a box: Stop Conversation's "go on" */
} Message;

typedef struct Queue { Message **items; int count, cap; } Queue;

static Queue messages, pages;
static Message *current;
static int cursor = -1, lit = -1, replyPage;
static uint32_t shownAt, nowMs;
static int repliesApart; static const Sprite *replyFace;

/* The reply spots of the box drawn last (for the mouse): where each is, and what (a reply's index, More, Previous). */
static int spotCount, spotX[64], spotY[64], spotW[64], spotH[64], spotSlot[64], pageCount = 1;

void dialog_set_replies_apart(int apart, const Sprite *face) { repliesApart = apart; replyFace = face; }

static char *copy(const char *s) { s = s ? s : ""; size_t n = strlen(s) + 1; char *c = (char *)malloc(n); memcpy(c, s, n); return c; }

static void push(Queue *q, Message *m)
{
    if (q->count == q->cap) { q->cap = q->cap ? q->cap * 2 : 16; q->items = (Message **)realloc(q->items, sizeof(Message *) * (size_t)q->cap); }
    q->items[q->count++] = m;
}

static Message *pop(Queue *q)
{
    if (!q->count) return NULL;
    Message *m = q->items[0];
    memmove(q->items, q->items + 1, sizeof(Message *) * (size_t)--q->count);
    return m;
}

static void release(Message *m)
{
    if (!m) return;
    free(m->title); free(m->text);
    for (int i = 0; i < m->replyCount; i++) free(m->replies[i]);
    free(m);
}

static Message *clone(const Message *m, const char *text, int withReplies)
{
    Message *c = (Message *)calloc(1, sizeof(Message));
    *c = *m;
    c->title = copy(m->title); c->text = copy(text);
    if (!withReplies) { c->replyCount = 0; c->chosen = NULL; }
    for (int i = 0; i < c->replyCount; i++) c->replies[i] = copy(m->replies[i]);
    return c;
}

int dialog_showing(void) { return current || pages.count || messages.count; }

/* ---------------- Measuring ---------------- */

/* The DialogFont; while it has no letters (none imported yet), the In-game font - as the Windows game shows it. */
static const Font *font(void)
{
    const Font *f = font_get("DialogFont");
    return font_is_built_in(f) ? font_get("InGameFont") : f;
}

static void face_size(const Sprite *face, int *w, int *h)
{
    const Picture *p = face ? sprite_picture_at(face, 0) : NULL;
    if (!p || p->width <= 0 || p->height <= 0) { *w = *h = 0; return; }
    double s = fmin(1, fmin((double)FACE_MAX / p->width, (double)FACE_MAX / p->height));
    *w = (int)(p->width * s); *h = (int)(p->height * s);
}

static void inset_of(const Sprite *frame, int *l, int *t, int *r, int *b)
{
    if (frame && frame->nineSlice)
    {
        *l = frame->sliceLeft > PADDING ? frame->sliceLeft : PADDING; *t = frame->sliceTop > PADDING ? frame->sliceTop : PADDING;
        *r = frame->sliceRight > PADDING ? frame->sliceRight : PADDING; *b = frame->sliceBottom > PADDING ? frame->sliceBottom : PADDING;
    }
    else *l = *t = *r = *b = PADDING;
}

static int text_width(const Sprite *face, const Sprite *frame)
{
    int fw, fh, l, t, r, b;
    face_size(face, &fw, &fh);
    inset_of(frame, &l, &t, &r, &b);
    int w = BOX_W - l - r - (face && fw ? fw + PADDING : 0);
    return w < 16 ? 16 : w;
}

static const Font *pagingFont;
static int lines_of(const char *s, int width) { return font_line_count(pagingFont ? pagingFont : font(), s, width); }

/* DialogBoxBuilder.Pages: four lines a page; a page that can't hold the rest ends after its last word ending in . ? ! , or "
   (else after its fourth line); a word starting with * begins a new page. */
static int make_pages(const char *whole, int width, char **out, int max)
{
    int n = 0;
    size_t len = strlen(whole);
    char *text = (char *)malloc(len + 1), *work = (char *)malloc(len + 1);
    /* "\r\n" -> "\n" */
    size_t k = 0;
    for (size_t i = 0; i < len; i++) if (!(whole[i] == '\r' && whole[i + 1] == '\n')) text[k++] = whole[i];
    text[k] = 0;
    int blank = 1;
    for (char *c = text; *c; c++) if (*c != ' ' && *c != '\t' && *c != '\n' && *c != '\r') blank = 0;
    if (blank) { out[n++] = copy(whole); free(text); free(work); return n; }
    /* Split where a word starts with '*' (the '*' itself goes): Regex.Split(text, @"(?<=^|\s)\*"). */
    char *part = text;
    while (part && n < max)
    {
        char *next = NULL;
        for (char *c = part; *c; c++)
            if (*c == '*' && (c == text || c[-1] == ' ' || c[-1] == '\t' || c[-1] == '\n' || c[-1] == '\r')) { next = c; break; }
        if (next) *next = 0;
        /* rest = part, trimmed */
        char *rest = part;
        while (*rest == ' ' || *rest == '\t' || *rest == '\n' || *rest == '\r') rest++;
        size_t rl = strlen(rest);
        while (rl && (rest[rl - 1] == ' ' || rest[rl - 1] == '\t' || rest[rl - 1] == '\n' || rest[rl - 1] == '\r')) rest[--rl] = 0;
        while (*rest && n < max)
        {
            if (lines_of(rest, width) <= LINES_PER_PAGE) { out[n++] = copy(rest); break; }
            int fits = -1, punct = -1;
            rl = strlen(rest);
            for (size_t e = 1; e <= rl; e++)
            {
                int wordEnd = !(rest[e - 1] == ' ' || rest[e - 1] == '\t' || rest[e - 1] == '\n') && (e == rl || rest[e] == ' ' || rest[e] == '\t' || rest[e] == '\n');
                if (!wordEnd) continue;
                memcpy(work, rest, e); work[e] = 0;
                if (lines_of(work, width) > LINES_PER_PAGE) break;
                fits = (int)e;
                char last = rest[e - 1];
                if (last == '.' || last == '?' || last == '!' || last == ',' || last == '"') punct = (int)e;
            }
            int cut = punct > 0 ? punct : fits;
            if (cut <= 0) { out[n++] = copy(rest); break; } /* one word longer than a whole page */
            memcpy(work, rest, (size_t)cut); work[cut] = 0;
            size_t wl = strlen(work);
            while (wl && (work[wl - 1] == ' ' || work[wl - 1] == '\t' || work[wl - 1] == '\n')) work[--wl] = 0;
            out[n++] = copy(work);
            rest += cut;
            while (*rest == ' ' || *rest == '\t' || *rest == '\n') rest++;
        }
        part = next ? next + 1 : NULL;
    }
    if (n == 0) out[n++] = copy("");
    free(text); free(work);
    return n;
}

/* ---------------- Queueing and going on ---------------- */

static void show_next(void);

void dialog_queue(const DialogBox *box)
{
    Message *m = (Message *)calloc(1, sizeof(Message));
    m->title = copy(box->title); m->text = copy(box->text);
    m->face = box->face; m->faceRight = box->faceRight; m->position = box->position; m->dim = box->dim; m->frame = box->frame;
    m->speakerPlus1 = box->speakerPlus1;
    const Sprite *words = box->wordsFont && box->wordsFont[0] ? sprite_find("Fonts", box->wordsFont) : NULL;
    m->wordsFont = words && words->frameCount > 0 ? font_get(box->wordsFont) : NULL; /* SpriteFont.For: no letters - the DialogFont */
    m->replyCount = box->replyCount > DIALOG_MAX_REPLIES ? DIALOG_MAX_REPLIES : box->replyCount;
    for (int i = 0; i < m->replyCount; i++) { m->replyIds[i] = box->replyIds[i]; m->replies[i] = copy(box->replies[i]); }
    m->chosen = box->chosen; m->context = box->context;
    push(&messages, m);
    if (!current) show_next();
}

void dialog_after_read(void (*goOn)(void *), void *context)
{
    Message *m = (Message *)calloc(1, sizeof(Message));
    m->title = copy(""); m->text = copy("");
    m->afterRead = goOn; m->afterContext = context;
    push(&messages, m);
}

static void show_next(void)
{
    for (;;)
    {
        int isPage = pages.count > 0;
        Message *m = isPage ? pop(&pages) : pop(&messages);
        release(current);
        current = NULL;
        if (!m) return;
        if (m->afterRead)
        {
            /* Stop Conversation: the boxes before it are read - the commands after it go on. */
            void (*goOn)(void *) = m->afterRead; void *ctx = m->afterContext;
            release(m);
            goOn(ctx);
            if (current) return;
            continue;
        }
        if (!isPage && u6_talking())
        {
            /* Ultima 6 (U6GameWindow.U6Talk ShowU6Message): no box - the words in the text area, the portrait in the panel */
            current = m;
            cursor = lit = -1; replyPage = 0; shownAt = nowMs;
            u6_talk_show(m->speakerPlus1 - 1, m->text, m->face, (const char *const *)m->replies, m->replyCount);
            return;
        }
        if (!isPage)
        {
            /* Too long for one box: four lines at a time, the replies with the last page. */
            char *parts[256];
            pagingFont = m->wordsFont;
            int n = make_pages(m->text, text_width(m->face, m->frame), parts, 256);
            pagingFont = NULL;
            if (n > 1)
            {
                for (int i = 1; i < n; i++) push(&pages, clone(m, parts[i], i == n - 1));
                Message *first = clone(m, parts[0], 0);
                release(m);
                m = first;
            }
            else
            {
                free(m->text);
                m->text = copy(parts[0]);
            }
            for (int i = 0; i < n; i++) free(parts[i]);
        }
        current = m;
        cursor = lit = -1;
        replyPage = 0;
        shownAt = nowMs;
        return;
    }
}

static void advance(void)
{
    if (current && current->replyCount) return; /* a reply has to be picked */
    show_next();
}

static void pick_reply(int index)
{
    if (!current || index < 0 || index >= current->replyCount || !current->chosen) return;
    Message *m = current;
    current = NULL; /* the box goes; the reply's branch may put up the next one */
    int id = m->replyIds[index];
    void (*chosen)(void *, int) = m->chosen; void *ctx = m->context;
    release(m);
    chosen(ctx, id);
    if (!current) show_next();
}

static void pick_slot(int spot)
{
    if (spot < 0 || spot >= spotCount) return;
    int slot = spotSlot[spot];
    if (slot == SLOT_MORE) { replyPage++; cursor = lit = -1; }
    else if (slot == SLOT_PREVIOUS) { replyPage--; cursor = lit = -1; }
    else pick_reply(slot);
}

static int spot_at(int x, int y)
{
    for (int i = 0; i < spotCount; i++)
        if (x >= spotX[i] && y >= spotY[i] && x < spotX[i] + spotW[i] && y < spotY[i] + spotH[i]) return i;
    return -1;
}

/* Ultima 6's talk (u6.c asks): go on past a page without replies; the reply picked by its index. */
void dialog_go_on(void) { advance(); }
void dialog_pick_reply(int index) { pick_reply(index); }
int dialog_reply_count(void) { return current ? current->replyCount : 0; }
const char *dialog_reply_text(int index) { return current && index >= 0 && index < current->replyCount ? current->replies[index] : ""; }
int dialog_speaker(void) { return current ? current->speakerPlus1 - 1 : -1; }

void dialog_key(enum PlatformKey key)
{
    if (!current) return;
    if (!current->replyCount || spotCount == 0)
    {
        if (key == KEY_ENTER || key == KEY_SPACE || key == KEY_ESCAPE) advance();
        return;
    }
    if (key >= KEY_1 && key <= KEY_9)
    {
        /* 1-9: this page's replies, counted without More / Previous. */
        int wanted = key - KEY_1, seen = 0;
        for (int i = 0; i < spotCount; i++)
            if (spotSlot[i] >= 0 && seen++ == wanted) { pick_slot(i); return; }
        return;
    }
    switch (key)
    {
        case KEY_UP: case KEY_LEFT: cursor = cursor <= 0 ? spotCount - 1 : cursor - 1; lit = cursor; break;
        case KEY_DOWN: case KEY_RIGHT: case KEY_TAB: cursor = (cursor + 1) % spotCount; lit = cursor; break;
        case KEY_PAGEDOWN: for (int i = 0; i < spotCount; i++) if (spotSlot[i] == SLOT_MORE) { pick_slot(i); break; } break;
        case KEY_PAGEUP: for (int i = 0; i < spotCount; i++) if (spotSlot[i] == SLOT_PREVIOUS) { pick_slot(i); break; } break;
        case KEY_ENTER: case KEY_SPACE: if (cursor >= 0) pick_slot(cursor); break;
        default: break;
    }
}

void dialog_mouse_move(int x, int y)
{
    if (!current || !current->replyCount) return;
    int i = spot_at(x, y);
    if (i >= 0) cursor = i;
    lit = i >= 0 ? i : cursor;
}

void dialog_click(int x, int y)
{
    if (!current) return;
    if (u6_talking()) return; /* Ultima 6: no clicking through a talk - its keys (u6.c) */
    if (current->replyCount) { pick_slot(spot_at(x, y)); return; } /* away from the replies: nothing */
    advance();
}

/* ---------------- Drawing ---------------- */

static uint32_t *screen;

/* Part of a picture stretched into a rectangle (nearest pixel), blended by its alpha. */
static void blit_part(const Picture *p, int sx, int sy, int sw, int sh, int dx, int dy, int dw, int dh)
{
    if (!p || sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0) return;
    dx *= frame_scale; dy *= frame_scale; dw *= frame_scale; dh *= frame_scale;
    /* clipped once; the picture stepped through in 16.16 (no dividing a pixel) */
    int i0 = dx < 0 ? -dx : 0, i1 = dx + dw > FRAME_W ? FRAME_W - dx : dw;
    int j0 = dy < 0 ? -dy : 0, j1 = dy + dh > FRAME_H ? FRAME_H - dy : dh;
    if (i0 >= i1 || j0 >= j1) return;
    uint32_t stepX = ((uint32_t)sw << 16) / (uint32_t)dw, stepY = ((uint32_t)sh << 16) / (uint32_t)dh;
    for (int j = j0; j < j1; j++)
    {
        const uint32_t *row = p->pixels + (size_t)(sy + (int)(((uint32_t)j * stepY) >> 16)) * (size_t)p->width + sx;
        uint32_t *d = &screen[(dy + j) * FRAME_W + dx];
        uint32_t at = (uint32_t)i0 * stepX;
        for (int i = i0; i < i1; i++, at += stepX)
        {
            uint32_t c = row[at >> 16], a = c >> 24;
            if (a == 0) continue;
            d[i] = a == 255 ? c : blend_px(d[i], c, a);
        }
    }
}

static void shade(int x, int y, int w, int h, uint32_t rgb, uint32_t a)
{
    FRAME_CLIP(x, y, w, h, x0, y0, x1, y1);
    for (int j = y0; j < y1; j++)
        for (int i = x0; i < x1; i++)
        {
            uint32_t *d = &screen[j * FRAME_W + i];
            *d = blend_px(*d, rgb, a);
        }
}

/* DialogFrameVisual: Nine Slice corners as drawn, edges and middle stretched; without it, the whole picture stretched. */
static void draw_frame(const Sprite *frame, int x, int y, int w, int h)
{
    const Picture *p = sprite_picture_at(frame, nowMs - shownAt);
    if (!p) return;
    if (!frame->nineSlice) { blit_part(p, 0, 0, p->width, p->height, x, y, w, h); return; }
    int left = frame->sliceLeft < w / 2 ? frame->sliceLeft : w / 2, right = frame->sliceRight < w / 2 ? frame->sliceRight : w / 2;
    int top = frame->sliceTop < h / 2 ? frame->sliceTop : h / 2, bottom = frame->sliceBottom < h / 2 ? frame->sliceBottom : h / 2;
    int pw = p->width, ph = p->height;
#define CLAMP(v, lo, hi) ((v) < (lo) ? (lo) : (v) > (hi) ? (hi) : (v))
    int l = CLAMP(frame->sliceLeft, 0, pw), r = CLAMP(frame->sliceRight, 0, pw - l), t = CLAMP(frame->sliceTop, 0, ph), b = CLAMP(frame->sliceBottom, 0, ph - t);
    int sx[3] = { 0, l, pw - r }, sw[3] = { l, pw - l - r, r }, sy[3] = { 0, t, ph - b }, sh[3] = { t, ph - t - b, b };
    int dx[3] = { x, x + left, x + w - right }, dw[3] = { left, w - left - right, right }, dy[3] = { y, y + top, y + h - bottom }, dh[3] = { top, h - top - bottom, bottom };
    for (int row = 0; row < 3; row++)
        for (int col = 0; col < 3; col++)
            blit_part(p, sx[col], sy[row], sw[col], sh[row], dx[col], dy[row], dw[col], dh[row]);
}

typedef struct Piece { const char *text; int x, y, w, h, spot; } Piece;

typedef struct Plan
{
    const char *title, *text; const Sprite *face, *frame; int faceRight;
    int faceW, faceH, textW, height;
    Piece pieces[80]; int pieceCount;
    int withReplies;
} Plan;

static const char *slot_text(int slot) { return slot == SLOT_MORE ? "More" : slot == SLOT_PREVIOUS ? "Previous" : current->replies[slot]; }

static int natural_width(const char *s, int maxW) { int w = font_block_width(font(), s, maxW); return w < maxW ? w : maxW; }

/* LayoutReplies: left to right, a new row when the next won't fit (at most five to a row, five rows), More / Previous when they don't all fit.
   Fills the page asked for into slots/xs/rows; returns how many pages there are. */
static int layout_replies(int maxW, int gap, int page, int *slots, int *xs, int *rows, int *count)
{
    int n = current->replyCount, pagesSoFar = 0, next = 0;
    int moreW = natural_width("More", maxW), prevW = natural_width("Previous", maxW);
    *count = 0;
    if (n == 0) return 1;
    do
    {
        int bestCount = 0, best[64], bx[64], br[64], taken = 0;
        for (int take = 1; next + take <= n; take++)
        {
            int spots[64], widths[64], m = 0;
            if (pagesSoFar > 0) { spots[m] = SLOT_PREVIOUS; widths[m++] = prevW; }
            for (int i = next; i < next + take && m < 62; i++) { spots[m] = i; widths[m++] = natural_width(current->replies[i], maxW); }
            if (next + take < n) { spots[m] = SLOT_MORE; widths[m++] = moreW; }
            int x = 0, row = 0, inRow = 0, ok = 1, px[64], pr[64];
            for (int i = 0; i < m; i++)
            {
                if (inRow > 0 && (inRow >= REPLIES_PER_ROW || x + widths[i] > maxW)) { row++; x = 0; inRow = 0; }
                if (row >= REPLY_ROWS) { ok = 0; break; }
                px[i] = x; pr[i] = row;
                x += widths[i] + gap;
                inRow++;
            }
            if (!ok) break;
            bestCount = m; taken = take;
            for (int i = 0; i < m; i++) { best[i] = spots[i]; bx[i] = px[i]; br[i] = pr[i]; }
        }
        if (bestCount == 0)
        {
            /* never stuck: one reply a page */
            int m = 0;
            if (pagesSoFar > 0) best[m++] = SLOT_PREVIOUS;
            best[m++] = next;
            if (next + 1 < n) best[m++] = SLOT_MORE;
            for (int i = 0; i < m; i++) { bx[i] = 0; br[i] = i; }
            bestCount = m; taken = 1;
        }
        if (pagesSoFar == page) { for (int i = 0; i < bestCount; i++) { slots[i] = best[i]; xs[i] = bx[i]; rows[i] = br[i]; } *count = bestCount; }
        pagesSoFar++;
        next += taken;
    }
    while (next < n);
    return pagesSoFar;
}

static void measure(Plan *plan, int maxHeight)
{
    int l, t, r, b;
    inset_of(plan->frame, &l, &t, &r, &b);
    face_size(plan->face, &plan->faceW, &plan->faceH);
    plan->textW = text_width(plan->face, plan->frame);
    const Font *f = font();
    int cell = font_line_height(f), y = 0;
    plan->pieceCount = 0;
    if (plan->title && plan->title[0])
    {
        int h = font_line_count(f, plan->title, plan->textW) * cell;
        plan->pieces[plan->pieceCount++] = (Piece){ plan->title, 0, y, plan->textW, h, -1 };
        y += h + 3;
    }
    if (plan->text && plan->text[0])
    {
        const Font *wf = current->wordsFont ? current->wordsFont : f;
        int h = font_line_count(wf, plan->text, plan->textW) * font_line_height(wf);
        plan->pieces[plan->pieceCount++] = (Piece){ plan->text, 0, y, plan->textW, h, -1 };
        y += h;
    }
    if (plan->withReplies && current->replyCount > 0)
    {
        if (y > 0) y += 4;
        int gap = 2 * font_space_width(f) > 6 ? 2 * font_space_width(f) : 6;
        int slots[64], xs[64], rows[64], count;
        pageCount = layout_replies(plan->textW, gap, replyPage, slots, xs, rows, &count);
        if (replyPage >= pageCount) { replyPage = pageCount - 1; layout_replies(plan->textW, gap, replyPage, slots, xs, rows, &count); }
        if (replyPage < 0) { replyPage = 0; layout_replies(plan->textW, gap, 0, slots, xs, rows, &count); }
        int rowsUsed = 0;
        for (int i = 0; i < count; i++) if (rows[i] + 1 > rowsUsed) rowsUsed = rows[i] + 1;
        spotCount = 0;
        for (int row = 0; row < rowsUsed; row++)
        {
            int rowH = 0;
            for (int i = 0; i < count; i++)
            {
                if (rows[i] != row || plan->pieceCount >= 80) continue;
                const char *s = slot_text(slots[i]);
                int w = natural_width(s, plan->textW), h = font_line_count(f, s, plan->textW) * cell;
                plan->pieces[plan->pieceCount++] = (Piece){ s, xs[i], y, w, h, spotCount };
                spotSlot[spotCount++] = slots[i];
                if (h > rowH) rowH = h;
            }
            y += rowH + 2;
        }
    }
    int inner = plan->faceH > y ? plan->faceH : y;
    int h = inner + t + b;
    if (h < MIN_HEIGHT) h = MIN_HEIGHT;
    if (h > maxHeight) h = maxHeight;
    plan->height = h;
}

static void place(const Plan *plan, int boxTop)
{
    int l, t, r, b;
    inset_of(plan->frame, &l, &t, &r, &b);
    if (plan->frame) draw_frame(plan->frame, AREA_X + MARGIN, boxTop, BOX_W, plan->height);
    if (plan->face && plan->faceW)
    {
        const Picture *p = sprite_picture_at(plan->face, 0);
        int fx = AREA_X + (plan->faceRight ? MARGIN + BOX_W - r - plan->faceW : MARGIN + l);
        blit_part(p, 0, 0, p->width, p->height, fx, boxTop + t, plan->faceW, plan->faceH);
    }
    int left = AREA_X + MARGIN + l + (plan->faceRight || !plan->faceW ? 0 : plan->faceW + PADDING), top = boxTop + t;
    const Font *f = font();
    for (int i = 0; i < plan->pieceCount; i++)
    {
        const Piece *pc = &plan->pieces[i];
        if (pc->spot >= 0)
        {
            /* The reply's words: what the mouse hits, lit up under the pointer (or the keys' cursor). */
            int s = pc->spot;
            spotX[s] = left + pc->x - 2; spotY[s] = top + pc->y - 1; spotW[s] = pc->w + 4; spotH[s] = pc->h + 2;
            if (s == lit) shade(spotX[s], spotY[s], spotW[s], spotH[s], 0xFFFFFF, 70);
        }
        if (top + pc->y > boxTop + plan->height) continue; /* past the bottom of a full-screen box */
        font_draw_block(pc->text == plan->text && current->wordsFont ? current->wordsFont : f, screen, left + pc->x, top + pc->y, pc->text, plan->textW, 0);
    }
}

static int top_for(int position, int height)
{
    return AREA_Y + (position == 0 ? MARGIN : position == 1 ? (AREA_H - height) / 2 : AREA_H - MARGIN - height);
}

/* ---------------- Ultima 8's conversations (U8GameWindow.U8Talk: Pentagram's BarkGump and AskGump) ----------------
   What a character says floats over its head - wrapped to BarkWidth, a page at a time; a page with no answers goes on by itself
   after its time (its length at the talk speed) or on a click - and the player's answers are a row of words over the Avatar,
   rows of at most AskWidth, 4 apart. Narration (no speaker on the map) keeps the box. The clicks, the hover and the keys are the
   box's own: the drawing fills the same reply spots. */

static int talkPoser = -1; /* who shows the Talk pose now (an actor) */
static const Message *talkShown; /* the page the Talk pose went with */

static cJSON *u8_talk_settings(void) { return cJSON_GetObjectItem(cJSON_GetObjectItem(cJSON_GetObjectItem((cJSON *)world_json(), "Project"), "Settings"), "Ultima8"); }
static int u8_setting(const char *k, int fallback) { cJSON *v = cJSON_GetObjectItem(u8_talk_settings(), k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static const char *u8_setting_text(const char *k) { const char *s = cJSON_GetStringValue(cJSON_GetObjectItem(u8_talk_settings(), k)); return s ? s : ""; }

static cJSON *speaker_json(int characterId)
{
    cJSON *c;
    cJSON_ArrayForEach(c, cJSON_GetObjectItem(cJSON_GetObjectItem(cJSON_GetObjectItem((cJSON *)world_json(), "Project"), "Characters"), "Characters"))
        if (cJSON_IsNumber(cJSON_GetObjectItem(c, "Id")) && cJSON_GetObjectItem(c, "Id")->valueint == characterId) return c;
    return NULL;
}

/* Its own bark font and colour (Character Editor > Verbose); none set: the game's font as it is. */
static const Font *bark_font(int characterId)
{
    const char *name = cJSON_GetStringValue(cJSON_GetObjectItem(speaker_json(characterId), "BarkFont"));
    return name && name[0] ? font_get(name) : font_get("InGameFont");
}
static uint32_t bark_colour(int characterId)
{
    const char *s = cJSON_GetStringValue(cJSON_GetObjectItem(speaker_json(characterId), "BarkColor"));
    if (!s || s[0] != '#') return 0;
    unsigned long v = strtoul(s + 1, NULL, 16);
    size_t n = strlen(s + 1);
    return n == 6 ? (uint32_t)(0xFF000000u | v) : n == 8 ? (uint32_t)v : 0;
}

/* WrapGameText: lines no wider than this ('*' or a new line starts one). */
static int wrap_bark(const Font *f, const char *text, int width, char lines[][160], int max)
{
    int n = 0;
    char line[160] = "", word[160];
    for (const char *p = text; ; p++)
    {
        if (*p == '*' || *p == '\n' || *p == 0)
        {
            if (line[0] && n < max) snprintf(lines[n++], 160, "%s", line);
            line[0] = 0;
            if (!*p) break;
            continue;
        }
        if (*p == ' ') continue;
        size_t k = 0;
        while (p[k] && p[k] != ' ' && p[k] != '*' && p[k] != '\n' && k < sizeof word - 1) { word[k] = p[k]; k++; }
        word[k] = 0;
        p += k - 1;
        char candidate[320];
        snprintf(candidate, sizeof candidate, line[0] ? "%s %s" : "%s%s", line, word);
        if (font_text_width(f, candidate) > width && line[0]) { if (n < max) snprintf(lines[n++], 160, "%s", line); snprintf(line, sizeof line, "%s", word); }
        else snprintf(line, sizeof line, "%s", candidate);
    }
    if (!n) lines[n++][0] = 0;
    return n;
}

static void end_talk_pose(void)
{
    if (talkPoser < 0) return;
    if (world_actor_pose_is(talkPoser, "Talk")) world_set_pose(talkPoser, talkPoser == 0 && combat_mode() && world_has_pose(0, "Combat Stand") ? "Combat Stand" : "Idle", -1);
    talkPoser = -1; talkShown = NULL;
}

/* A portrait (its Face, frames playing - a talking head - at most 64 pixels tall). */
static void draw_portrait(int characterId, int x, int bottom, int *w)
{
    cJSON *c = speaker_json(characterId);
    const char *g = cJSON_GetStringValue(cJSON_GetObjectItem(c, "FaceSpriteGroup")), *n = cJSON_GetStringValue(cJSON_GetObjectItem(c, "FaceSpriteName"));
    const Sprite *s = n && n[0] ? sprite_find(g ? g : "", n) : NULL;
    const Picture *p = s ? sprite_picture_at(s, nowMs) : NULL;
    *w = 0;
    if (!p) return;
    int qw = (p->width + 7) / 8 * 8, qh = (p->height + 7) / 8 * 8;
    double scale = qh > 64 ? 64.0 / qh : 1;
    int pw = (int)(qw * scale), ph = (int)(qh * scale);
    world_blit(screen, p, x, bottom - ph, pw, ph);
    *w = pw;
}

static int portrait_width(int characterId)
{
    cJSON *c = speaker_json(characterId);
    const char *g = cJSON_GetStringValue(cJSON_GetObjectItem(c, "FaceSpriteGroup")), *n = cJSON_GetStringValue(cJSON_GetObjectItem(c, "FaceSpriteName"));
    const Sprite *s = n && n[0] ? sprite_find(g ? g : "", n) : NULL;
    const Picture *p = s ? sprite_picture_at(s, 0) : NULL;
    if (!p) return 0;
    int qw = (p->width + 7) / 8 * 8, qh = (p->height + 7) / 8 * 8;
    return (int)(qw * (qh > 64 ? 64.0 / qh : 1));
}

/* TryDrawU8Message: the page up now drawn Ultima 8's way; 0: drawn as a box (nobody on the map to float over). */
static int draw_u8_talk(void)
{
    const Message *m = current;
    int speakerId = m->speakerPlus1 - 1, speaker = speakerId >= 0 ? world_actor_by_character(speakerId) : -1, sx, sy, sw, sh;
    if (speaker < 0 || !world_actor_drawn_rect(speaker, &sx, &sy, &sw, &sh)) { end_talk_pose(); return 0; }
    if (talkShown != m)
    {
        end_talk_pose();
        talkShown = m;
        if (world_has_pose(speaker, "Talk")) { world_set_pose(speaker, "Talk", -1); talkPoser = speaker; } /* its Talk pose while it speaks */
    }
    const char *portrait = u8_setting_text("TalkPortrait");
    /* the words, over the speaker's head - in its own colour and font */
    const Font *bf = bark_font(speakerId);
    uint32_t colour = bark_colour(speakerId);
    static char lines[16][160];
    int n = wrap_bark(bf, m->text, u8_setting("BarkWidth", 194), lines, 16), lh = font_line_height(bf), ww = 0;
    for (int i = 0; i < n; i++) { int w = font_text_width(bf, lines[i]); if (w > ww) ww = w; }
    int beside = !strcmp(portrait, "Beside the words") ? portrait_width(speakerId) : 0;
    int blockW = ww + (beside ? beside + 4 : 0), blockH = n * lh;
    int bx = sx + sw / 2 - blockW / 2, by = sy - blockH - 2;
    if (bx > SCREEN_W - blockW) bx = SCREEN_W - blockW;
    if (bx < 0) bx = 0;
    if (by > SCREEN_H - blockH) by = SCREEN_H - blockH;
    if (by < 0) by = 0;
    if (beside) { int pw; draw_portrait(speakerId, bx, by + blockH, &pw); }
    int wx = bx + (beside ? beside + 4 : 0);
    for (int i = 0; i < n; i++) font_draw(bf, screen, wx + (ww - font_text_width(bf, lines[i])) / 2, by + i * lh, lines[i], colour);
    if (!strcmp(portrait, "In a corner of the screen"))
    {
        int who = m->replyCount ? world_actor_character(0) : speakerId, pw, ph = 0;
        cJSON *c = speaker_json(who);
        const char *g = cJSON_GetStringValue(cJSON_GetObjectItem(c, "FaceSpriteGroup")), *nm = cJSON_GetStringValue(cJSON_GetObjectItem(c, "FaceSpriteName"));
        const Sprite *s = nm && nm[0] ? sprite_find(g ? g : "", nm) : NULL;
        const Picture *p = s ? sprite_picture_at(s, 0) : NULL;
        if (p) { int qh = (p->height + 7) / 8 * 8; ph = qh > 64 ? 64 : qh; }
        draw_portrait(who, 6, SCREEN_H - 6, &pw);
        (void)ph;
    }
    spotCount = 0;
    if (m->replyCount)
    {
        /* the answers, over the Avatar (AskGump): rows of at most AskWidth, 4 apart */
        int ax, ay, aw, ah, playerId = world_actor_character(0);
        if (!world_actor_drawn_rect(0, &ax, &ay, &aw, &ah)) { ax = SCREEN_W / 2; ay = SCREEN_H / 2; aw = 0; }
        const Font *pf = bark_font(playerId);
        uint32_t pc = bark_colour(playerId);
        int askW = u8_setting("AskWidth", 160), plh = font_line_height(pf);
        int rowOf[DIALOG_MAX_REPLIES], rowCount = 1, rowWidth = 0;
        for (int i = 0; i < m->replyCount; i++)
        {
            int w = font_text_width(pf, m->replies[i]);
            if (rowWidth + w > askW && rowWidth > 0) { rowCount++; rowWidth = 0; }
            rowOf[i] = rowCount - 1;
            rowWidth += w + 4;
        }
        int top = ay - rowCount * plh - 2;
        if (speaker == 0) top -= blockH; /* the Avatar's own words above */
        for (int r = 0; r < rowCount; r++, top += plh)
        {
            int width = 0, count = 0;
            for (int i = 0; i < m->replyCount; i++) if (rowOf[i] == r) { width += font_text_width(pf, m->replies[i]); count++; }
            width += 4 * (count - 1);
            int x = ax + aw / 2 - width / 2;
            for (int i = 0; i < m->replyCount && spotCount < 64; i++)
            {
                if (rowOf[i] != r) continue;
                int w = font_text_width(pf, m->replies[i]), px = x < 0 ? 0 : x > SCREEN_W - w ? SCREEN_W - w : x, py = top < 0 ? 0 : top;
                int s = spotCount++;
                spotX[s] = px; spotY[s] = py; spotW[s] = w; spotH[s] = plh; spotSlot[s] = i;
                if (s == lit) shade(px - 1, py, w + 2, plh, 0xFFFFFF, 110);
                font_draw(pf, screen, px, py, m->replies[i], pc);
                x += w + 4;
            }
        }
    }
    else
    {
        /* no answers: it goes on by itself after its time, as a bark (or on a click) */
        int ms = (int)strlen(m->text) * u8_setting("BarkMsPerChar", 60), least = u8_setting("BarkMinMs", 1500);
        if (nowMs - shownAt >= (uint32_t)(ms > least ? ms : least)) advance();
    }
    return 1;
}

void dialog_draw(uint32_t *pixels, uint32_t ms)
{
    nowMs = ms;
    if (!current) { spotCount = 0; if (u8_on()) end_talk_pose(); return; } /* (Ultima 8: the talk is over - its Talk pose ends) */
    if (u6_talking()) { spotCount = 0; return; } /* Ultima 6: the text area shows it (u6.c) */
    screen = pixels;
    if (u8_on() && draw_u8_talk()) return; /* Ultima 8: over the speaker's head, the answers over the Avatar */
    const Message *m = current;
    if (m->dim) shade(0, 0, SCREEN_W, SCREEN_H, 0, 128);
    static Plan words, replies;
    spotCount = 0;
    if (!repliesApart || m->replyCount == 0)
    {
        words = (Plan){ m->title, m->text, m->face, m->frame, m->faceRight };
        words.withReplies = 1;
        measure(&words, AREA_H - 2 * MARGIN);
        place(&words, top_for(m->position, words.height));
        return;
    }
    /* As Exult's conversation: the speaker's box, and under it the main character's face with the replies. */
    int speaks = (m->title && m->title[0]) || (m->text && m->text[0]);
    replies = (Plan){ "", "", replyFace, m->frame, m->faceRight };
    replies.withReplies = 1;
    measure(&replies, AREA_H - 2 * MARGIN);
    if (!speaks) { place(&replies, top_for(m->position, replies.height)); return; }
    int keep = spotCount, keepSlots[64];
    memcpy(keepSlots, spotSlot, sizeof keepSlots);
    words = (Plan){ m->title, m->text, m->face, m->frame, m->faceRight };
    int room = AREA_H - 3 * MARGIN - replies.height;
    measure(&words, room > MIN_HEIGHT ? room : MIN_HEIGHT);
    spotCount = keep;
    memcpy(spotSlot, keepSlots, sizeof keepSlots);
    int top = top_for(m->position, words.height + MARGIN + replies.height);
    place(&words, top);
    place(&replies, top + words.height + MARGIN);
}

/* A new game: no box up, none waiting. */
void dialog_reset(void) { messages.count = 0; pages.count = 0; current = NULL; cursor = lit = -1; replyPage = 0; }
