#include "dialog.h"
#include "font.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* DialogBoxMetrics: the box on the 320 x 200 screen. */
#define MARGIN 4
#define PADDING 4
#define MIN_HEIGHT 32
#define FACE_MAX 48
#define BOX_W (SCREEN_W - 2 * MARGIN)
#define LINES_PER_PAGE 4      /* Exult's NPC text box: four lines (Conversation::set_face_rect) */
#define REPLIES_PER_ROW 5
#define REPLY_ROWS 5
#define SLOT_MORE (-1)
#define SLOT_PREVIOUS (-2)

typedef struct Message
{
    char *title, *text;
    const Sprite *face; int faceRight, position, dim;
    const Sprite *frame;
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

static const Font *font(void) { return font_get("DialogFont"); }

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

static int lines_of(const char *s, int width) { return font_line_count(font(), s, width); }

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
        if (!isPage)
        {
            /* Too long for one box: four lines at a time, the replies with the last page. */
            char *parts[256];
            int n = make_pages(m->text, text_width(m->face, m->frame), parts, 256);
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
    if (current->replyCount) { pick_slot(spot_at(x, y)); return; } /* away from the replies: nothing */
    advance();
}

/* ---------------- Drawing ---------------- */

static uint32_t *screen;

/* Part of a picture stretched into a rectangle (nearest pixel), blended by its alpha. */
static void blit_part(const Picture *p, int sx, int sy, int sw, int sh, int dx, int dy, int dw, int dh)
{
    if (!p || sw <= 0 || sh <= 0 || dw <= 0 || dh <= 0) return;
    for (int j = 0; j < dh; j++)
    {
        int y = dy + j;
        if (y < 0 || y >= SCREEN_H) continue;
        const uint32_t *row = p->pixels + (size_t)(sy + j * sh / dh) * (size_t)p->width;
        for (int i = 0; i < dw; i++)
        {
            int x = dx + i;
            if (x < 0 || x >= SCREEN_W) continue;
            uint32_t c = row[sx + i * sw / dw], a = c >> 24;
            if (a == 0) continue;
            uint32_t *d = &screen[y * SCREEN_W + x];
            if (a == 255) { *d = c; continue; }
            uint32_t r = (((c >> 16) & 255) * a + ((*d >> 16) & 255) * (255 - a)) / 255;
            uint32_t g = (((c >> 8) & 255) * a + ((*d >> 8) & 255) * (255 - a)) / 255;
            uint32_t b = ((c & 255) * a + (*d & 255) * (255 - a)) / 255;
            *d = 0xFF000000u | (r << 16) | (g << 8) | b;
        }
    }
}

static void shade(int x, int y, int w, int h, uint32_t rgb, uint32_t a)
{
    for (int j = y < 0 ? 0 : y; j < y + h && j < SCREEN_H; j++)
        for (int i = x < 0 ? 0 : x; i < x + w && i < SCREEN_W; i++)
        {
            uint32_t *d = &screen[j * SCREEN_W + i];
            uint32_t r = (((rgb >> 16) & 255) * a + ((*d >> 16) & 255) * (255 - a)) / 255;
            uint32_t g = (((rgb >> 8) & 255) * a + ((*d >> 8) & 255) * (255 - a)) / 255;
            uint32_t b = ((rgb & 255) * a + (*d & 255) * (255 - a)) / 255;
            *d = 0xFF000000u | (r << 16) | (g << 8) | b;
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
        int h = font_line_count(f, plan->text, plan->textW) * cell;
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
    if (plan->frame) draw_frame(plan->frame, MARGIN, boxTop, BOX_W, plan->height);
    if (plan->face && plan->faceW)
    {
        const Picture *p = sprite_picture_at(plan->face, 0);
        int fx = plan->faceRight ? MARGIN + BOX_W - r - plan->faceW : MARGIN + l;
        blit_part(p, 0, 0, p->width, p->height, fx, boxTop + t, plan->faceW, plan->faceH);
    }
    int left = MARGIN + l + (plan->faceRight || !plan->faceW ? 0 : plan->faceW + PADDING), top = boxTop + t;
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
        font_draw_block(f, screen, left + pc->x, top + pc->y, pc->text, plan->textW, 0);
    }
}

static int top_for(int position, int height)
{
    return position == 0 ? MARGIN : position == 1 ? (SCREEN_H - height) / 2 : SCREEN_H - MARGIN - height;
}

void dialog_draw(uint32_t *pixels, uint32_t ms)
{
    nowMs = ms;
    if (!current) { spotCount = 0; return; }
    screen = pixels;
    const Message *m = current;
    if (m->dim) shade(0, 0, SCREEN_W, SCREEN_H, 0, 128);
    static Plan words, replies;
    spotCount = 0;
    if (!repliesApart || m->replyCount == 0)
    {
        words = (Plan){ m->title, m->text, m->face, m->frame, m->faceRight };
        words.withReplies = 1;
        measure(&words, SCREEN_H - 2 * MARGIN);
        place(&words, top_for(m->position, words.height));
        return;
    }
    /* As Exult's conversation: the speaker's box, and under it the main character's face with the replies. */
    int speaks = (m->title && m->title[0]) || (m->text && m->text[0]);
    replies = (Plan){ "", "", replyFace, m->frame, m->faceRight };
    replies.withReplies = 1;
    measure(&replies, SCREEN_H - 2 * MARGIN);
    if (!speaks) { place(&replies, top_for(m->position, replies.height)); return; }
    int keep = spotCount, keepSlots[64];
    memcpy(keepSlots, spotSlot, sizeof keepSlots);
    words = (Plan){ m->title, m->text, m->face, m->frame, m->faceRight };
    int room = SCREEN_H - 3 * MARGIN - replies.height;
    measure(&words, room > MIN_HEIGHT ? room : MIN_HEIGHT);
    spotCount = keep;
    memcpy(spotSlot, keepSlots, sizeof keepSlots);
    int top = top_for(m->position, words.height + MARGIN + replies.height);
    place(&words, top);
    place(&replies, top + words.height + MARGIN);
}

/* A new game: no box up, none waiting. */
void dialog_reset(void) { messages.count = 0; pages.count = 0; current = NULL; cursor = lit = -1; replyPage = 0; }
