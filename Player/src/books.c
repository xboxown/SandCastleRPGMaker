/*
 * Reading books, as the Windows engine does (ShowBookReadingView): the
 * book's paperdoll artwork, and on it each text frame showing this page's
 * text - written in the book's font (its Text Font, else the game's), each
 * run in its own size, colour and boldness, each paragraph aligned as
 * written; sprites placed in the text are drawn there. A click anywhere
 * turns the page (its page-flip sound), Escape or the last page finishes
 * it (its close sound, then By After Read Item). The page text comes from
 * the editor's pages (converted to plain paragraphs when the game is
 * built); a frame whose text doesn't fit at the screen's size goes on over
 * an extra turn of the same page. Change Book Content swaps one frame's text
 * on one page, Change Book Settings a book's name and the like - both only
 * in play (and in saves).
 */
#include "books.h"
#include "world.h"
#include "events.h"
#include "sound.h"
#include "font.h"
#include "magic.h"
#include "pictures.h"
#include "platform.h"
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

void game_log(const char *line);

#define DESIGN_W 700.0   /* the 700 x 400 canvas every frame was drawn on (Book Editor) */
#define DESIGN_H 400.0
#define TR_AFTER_READ 5
#define CAT_BOOK 3

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static double dbl(cJSON *o, const char *k, double fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valuedouble : fallback; }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }

static cJSON *book_json(int id)
{
    cJSON *b;
    cJSON_ArrayForEach(b, get(get((cJSON *)world_json(), "Books"), "Books")) if (num(b, "Id", -2) == id) return b;
    return NULL;
}

/* ---------------- What changed in play ---------------- */

static cJSON *overrides;    /* { "book:page:frame": paragraphs (text) } */
static cJSON *settings;     /* { "book:Field": value } */
static cJSON *readBooks;    /* [ book ids opened to read ] */

static cJSON *own(cJSON **o, int array) { if (!*o) *o = array ? cJSON_CreateArray() : cJSON_CreateObject(); return *o; }

void books_change_content(int bookId, int page, int frame, const char *paragraphs)
{
    char key[48];
    snprintf(key, sizeof key, "%d:%d:%d", bookId, page, frame);
    cJSON_DeleteItemFromObject(own(&overrides, 0), key);
    cJSON_AddStringToObject(overrides, key, paragraphs ? paragraphs : "[]");
}

void books_change_setting(int bookId, const char *field, const char *value)
{
    char key[80];
    snprintf(key, sizeof key, "%d:%s", bookId, field);
    cJSON_DeleteItemFromObject(own(&settings, 0), key);
    cJSON_AddStringToObject(settings, key, value ? value : "");
}

const char *books_setting(int bookId, const char *field)
{
    char key[80];
    snprintf(key, sizeof key, "%d:%s", bookId, field);
    return cJSON_GetStringValue(get(settings, key));
}

const char *books_name(int bookId)
{
    const char *n = books_setting(bookId, "Name");
    return n ? n : str(book_json(bookId), "Name");
}

int books_has_been_read(int bookId)
{
    cJSON *r;
    cJSON_ArrayForEach(r, readBooks) if (cJSON_IsNumber(r) && r->valueint == bookId) return 1;
    return 0;
}

void books_save(cJSON *runtime)
{
    cJSON *b = cJSON_CreateObject();
    if (overrides) cJSON_AddItemToObject(b, "Content", cJSON_Duplicate(overrides, 1));
    if (settings) cJSON_AddItemToObject(b, "Settings", cJSON_Duplicate(settings, 1));
    if (readBooks) cJSON_AddItemToObject(b, "Read", cJSON_Duplicate(readBooks, 1));
    cJSON_AddItemToObject(runtime, "Books", b);
}

void books_restore(cJSON *runtime)
{
    books_reset();
    cJSON *b = get(runtime, "Books");
    if (get(b, "Content")) overrides = cJSON_Duplicate(get(b, "Content"), 1);
    if (get(b, "Settings")) settings = cJSON_Duplicate(get(b, "Settings"), 1);
    if (get(b, "Read")) readBooks = cJSON_Duplicate(get(b, "Read"), 1);
}

/* ---------------- Laying a frame's text out ---------------- */

typedef struct Piece { char text[160]; int scale, bold, x; uint32_t colour; const Font *font; } Piece;   /* a run's words on one line */
typedef struct Line { Piece pieces[12]; int count, height, gap, width, align; const Sprite *sprite; int spriteW, spriteH; } Line; /* gap: room after it (a paragraph's end) */
typedef struct Laid { Line *lines; int count, cap; } Laid;

static void add_line(Laid *l, const Line *line)
{
    if (l->count == l->cap) { l->cap = l->cap * 2 + 16; l->lines = (Line *)realloc(l->lines, sizeof(Line) * (size_t)l->cap); }
    l->lines[l->count++] = *line;
}

static uint32_t colour_of(const char *hex)
{
    if (!hex || hex[0] != '#' || strlen(hex) < 7) return 0xFF000000u;
    return 0xFF000000u | (uint32_t)strtoul(hex + 1, NULL, 16);
}

/* UTF-8 to the font's ASCII: invisible marks dropped, curly quotes and dashes made plain. */
static void ascii_of(const char *in, char *out, size_t size)
{
    size_t n = 0;
    for (const unsigned char *p = (const unsigned char *)in; *p && n + 4 < size;)
    {
        unsigned c = *p;
        if (c < 0x80) { out[n++] = (char)c; p++; continue; }
        unsigned cp = 0; int extra = c >= 0xF0 ? 3 : c >= 0xE0 ? 2 : c >= 0xC0 ? 1 : 0;
        cp = c & (extra == 3 ? 0x07 : extra == 2 ? 0x0F : 0x1F);
        p++;
        for (int k = 0; k < extra && *p; k++, p++) cp = (cp << 6) | (*p & 0x3F);
        if (cp == 0x200B || cp == 0xFEFF || cp == 0x200C || cp == 0x200D) continue;
        if (cp == 0x2018 || cp == 0x2019) out[n++] = '\'';
        else if (cp == 0x201C || cp == 0x201D) out[n++] = '"';
        else if (cp == 0x2013 || cp == 0x2014) out[n++] = '-';
        else if (cp == 0x2026) { out[n++] = '.'; out[n++] = '.'; out[n++] = '.'; }
        else if (cp == 0xA0) out[n++] = ' ';
        else out[n++] = '?';
    }
    out[n] = 0;
}

static int text_width(const Font *f, const char *t, int scale) { return font_text_width(f, t) * scale; }

/* Each paragraph wrapped into the frame's width: words placed whole (a word wider than the frame broken where it runs out). */
static int layRunFonts; /* the book has no font of its own: each run in the font it was written in (fonts.sdbx) */

static void lay_out(const Font *bookFont, cJSON *paragraphs, int width, double s, Laid *out)
{
    const Font *f = bookFont;
    cJSON *para;
    int lineH = font_line_height(f);
    cJSON_ArrayForEach(para, paragraphs)
    {
        cJSON *sprite = get(para, "Sprite");
        if (sprite)
        {
            Line line;
            memset(&line, 0, sizeof line);
            line.sprite = sprite_find(str(sprite, "SpriteGroup"), str(sprite, "SpriteName"));
            const Picture *pic = sprite_picture_at(line.sprite, 0);
            line.spriteW = pic ? (int)(pic->width * s + 0.5) : 0; line.spriteH = pic ? (int)(pic->height * s + 0.5) : 0;
            if (line.spriteW > width && line.spriteW > 0) { line.spriteH = line.spriteH * width / line.spriteW; line.spriteW = width; }
            line.height = line.spriteH + lineH / 2;
            add_line(out, &line);
            continue;
        }
        const char *align = str(para, "Align");
        int alignment = !strcmp(align, "Center") ? 1 : !strcmp(align, "Right") ? 2 : 0;
        Line line;
        memset(&line, 0, sizeof line);
        line.align = alignment;
        int any = 0;
        cJSON *run;
        cJSON_ArrayForEach(run, get(para, "Runs"))
        {
            char text[2048];
            ascii_of(str(run, "Text"), text, sizeof text);
            double size = dbl(run, "Size", 12);
            int scale = (int)(size / 12.0 + 0.5);
            if (scale < 1) scale = 1;
            f = bookFont;
            if (layRunFonts && str(run, "Font")[0]) { f = font_get(str(run, "Font")); scale = 1; } /* drawn at its own size */
            uint32_t colour = colour_of(str(run, "Color"));
            int bold = cJSON_IsTrue(get(run, "Bold"));
            for (const char *p = text; *p;)
            {
                if (*p == '\n') { if (!line.height) line.height = lineH * scale; add_line(out, &line); memset(&line, 0, sizeof line); line.align = alignment; p++; continue; }
                /* the next word, with the spaces before it */
                const char *start = p;
                while (*p == ' ' || *p == '\t') p++;
                while (*p && *p != ' ' && *p != '\t' && *p != '\n') p++;
                char word[160];
                int n = (int)(p - start) < 159 ? (int)(p - start) : 159;
                memcpy(word, start, (size_t)n); word[n] = 0;
                const char *visible = word;
                if (line.width == 0) while (*visible == ' ' || *visible == '\t') visible++; /* no spaces starting a line */
                int w = text_width(f, visible, scale) + bold;
                if (line.width > 0 && line.width + w > width)
                {
                    add_line(out, &line);
                    memset(&line, 0, sizeof line); line.align = alignment;
                    visible = word;
                    while (*visible == ' ' || *visible == '\t') visible++;
                    w = text_width(f, visible, scale) + bold;
                }
                if (!*visible) continue;
                Piece *last = line.count ? &line.pieces[line.count - 1] : NULL;
                if (last && last->font == f && last->scale == scale && last->bold == bold && last->colour == colour && strlen(last->text) + strlen(visible) < sizeof last->text)
                    strcat(last->text, visible);
                else if (line.count < 12)
                {
                    Piece *pc = &line.pieces[line.count++];
                    snprintf(pc->text, sizeof pc->text, "%s", visible);
                    pc->scale = scale; pc->bold = bold; pc->colour = colour; pc->x = line.width; pc->font = f;
                }
                line.width += w;
                if (font_line_height(f) * scale > line.height) line.height = font_line_height(f) * scale;
                any = 1;
            }
        }
        if (!line.height) line.height = lineH;
        add_line(out, &line);
        (void)any;
        out->lines[out->count - 1].gap = (int)(10 * s + 0.5) > 1 ? (int)(10 * s + 0.5) : 1; /* ParagraphSpacing */
    }
}

/* ---------------- The open book ---------------- */

typedef struct TextFrame { int x, y, w, h; Laid laid; int *turnStart, turns; } TextFrame; /* its box on screen, its lines, where each turn of the page starts */
typedef struct Open { int on, id, page, turn, turns, pageCount; cJSON *book; const Font *font; TextFrame frames[16]; int frameCount; int bx, by, bw, bh; double s; } Open;
static Open open_;

static void free_frames(void)
{
    for (int i = 0; i < open_.frameCount; i++) { free(open_.frames[i].laid.lines); free(open_.frames[i].turnStart); }
    memset(open_.frames, 0, sizeof open_.frames);
    open_.frameCount = 0;
}

/* This page's text in each frame, laid out; how many turns it takes for the longest to be read. */
static void build_page(void)
{
    free_frames();
    cJSON *frames = get(open_.book, "Frames"), *page = cJSON_GetArrayItem(get(open_.book, "Pages"), open_.page), *f;
    open_.turns = 1;
    Laid carry;
    memset(&carry, 0, sizeof carry);
    cJSON_ArrayForEach(f, frames)
    {
        if (open_.frameCount >= 16) break;
        TextFrame *fr = &open_.frames[open_.frameCount++];
        double minX = 1e9, minY = 1e9, maxX = -1e9, maxY = -1e9;
        cJSON *pt;
        cJSON_ArrayForEach(pt, get(f, "Points"))
        {
            double x = dbl(pt, "X", 0), y = dbl(pt, "Y", 0);
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }
        if (maxX < minX) continue;
        fr->x = open_.bx + (int)(minX * open_.s); fr->y = open_.by + (int)(minY * open_.s);
        fr->w = (int)((maxX - minX) * open_.s); fr->h = (int)((maxY - minY) * open_.s);
        char key[48];
        snprintf(key, sizeof key, "%d:%d:%d", open_.id, open_.page, num(f, "Id", -1));
        const char *text = cJSON_GetStringValue(get(overrides, key));
        if (!text) { char fid[16]; snprintf(fid, sizeof fid, "%d", num(f, "Id", -1)); text = cJSON_GetStringValue(get(get(page, "FrameTexts"), fid)); }
        cJSON *paragraphs = text ? cJSON_Parse(text) : NULL;
        int own = paragraphs && cJSON_GetArraySize(paragraphs) > 0;
        if (!own && carry.count)
        {
            /* no text of its own: what didn't fit in the frame before goes on here */
            fr->laid = carry;
            memset(&carry, 0, sizeof carry);
        }
        else if (paragraphs) lay_out(open_.font, paragraphs, fr->w, open_.s, &fr->laid);
        cJSON_Delete(paragraphs);
        if (carry.count) { free(carry.lines); memset(&carry, 0, sizeof carry); } /* the frame had its own: the rest goes over extra turns of the frame before */
        /* what doesn't fit in this frame goes on into the next one if that has none of its own */
        cJSON *next = f->next;
        char nid[16];
        if (next) snprintf(nid, sizeof nid, "%d", num(next, "Id", -1));
        char nkey[48];
        if (next) snprintf(nkey, sizeof nkey, "%d:%d:%d", open_.id, open_.page, num(next, "Id", -1));
        const char *nextText = next ? cJSON_GetStringValue(get(overrides, nkey)) : NULL;
        if (next && !nextText) nextText = cJSON_GetStringValue(get(get(page, "FrameTexts"), nid));
        cJSON *np = nextText ? cJSON_Parse(nextText) : NULL;
        int nextEmpty = next && (!np || cJSON_GetArraySize(np) == 0);
        cJSON_Delete(np);
        if (nextEmpty)
        {
            int used = 0, fit = 0;
            while (fit < fr->laid.count && (used == 0 || used + fr->laid.lines[fit].height <= fr->h)) { used += fr->laid.lines[fit].height + fr->laid.lines[fit].gap; fit++; }
            for (int k = fit; k < fr->laid.count; k++) add_line(&carry, &fr->laid.lines[k]);
            fr->laid.count = fit;
        }
        /* the turns: as many lines as fit the frame each time */
        fr->turnStart = (int *)calloc((size_t)fr->laid.count + 2, sizeof(int));
        fr->turns = 1;
        int used = 0;
        for (int i = 0; i < fr->laid.count; i++)
        {
            int h = fr->laid.lines[i].height;
            if (used > 0 && used + h > fr->h) { fr->turnStart[fr->turns++] = i; used = 0; }
            used += h + fr->laid.lines[i].gap;
        }
        fr->turnStart[fr->turns] = fr->laid.count;
        if (fr->turns > open_.turns) open_.turns = fr->turns;
    }
    free(carry.lines);
    open_.turn = 0;
}

/* Use a book (no By Use page took it): its reading view. 0 if it has no pages or frames, or no artwork. */
int books_read(int bookId)
{
    cJSON *book = book_json(bookId);
    char line[200];
    if (!book) return 0;
    int pages = cJSON_GetArraySize(get(book, "Pages")), frames = cJSON_GetArraySize(get(book, "Frames"));
    if (!pages || !frames) { snprintf(line, sizeof line, "\"%s\" has no pages or text frames set up yet.", books_name(bookId)); game_log(line); return 0; }
    const Sprite *doll = sprite_find(str(book, "PaperDollSpriteGroup"), str(book, "PaperDollSpriteName"));
    if (!doll) { snprintf(line, sizeof line, "\"%s\" has no paper doll artwork assigned yet.", books_name(bookId)); game_log(line); return 0; }
    if (!books_has_been_read(bookId)) cJSON_AddItemToArray(own(&readBooks, 1), cJSON_CreateNumber(bookId)); /* HasBeenRead: the moment it's opened */
    free_frames();
    memset(&open_, 0, sizeof open_);
    open_.on = 1; open_.id = bookId; open_.book = book; open_.pageCount = pages;
    const char *language = magic_language_font(num(book, "LanguageId", -1)); /* in a language not understood: its font */
    open_.font = font_get(language ? language : str(book, "TextFontSpriteName")[0] ? str(book, "TextFontSpriteName") : "InGameFont");
    layRunFonts = !language && !str(book, "TextFontSpriteName")[0];
    open_.s = SCREEN_W / DESIGN_W < SCREEN_H / DESIGN_H ? SCREEN_W / DESIGN_W : SCREEN_H / DESIGN_H;
    open_.bw = (int)(DESIGN_W * open_.s); open_.bh = (int)(DESIGN_H * open_.s);
    open_.bx = (SCREEN_W - open_.bw) / 2; open_.by = (SCREEN_H - open_.bh) / 2;
    build_page();
    cJSON *sounds = get(get(get((cJSON *)world_json(), "Project"), "Settings"), "Sounds");
    sound_play(!cJSON_IsFalse(get(sounds, "PaperdollOpenSoundEnabled")) && num(sounds, "PaperdollOpenSoundId", -1) >= 0 ? num(sounds, "PaperdollOpenSoundId", -1) : num(book, "OpenSoundResourceId", -1));
    snprintf(line, sizeof line, "You open \"%s\".", books_name(bookId));
    game_log(line);
    return 1;
}

int books_open(void) { return open_.on; }

/* Its close sound, the view gone, then By After Read Item. */
static void finish(void)
{
    if (!open_.on) return;
    int id = open_.id;
    sound_play(num(open_.book, "CloseSoundResourceId", -1));
    free_frames();
    open_.on = 0;
    char line[200];
    snprintf(line, sizeof line, "You finish reading \"%s\".", books_name(id));
    game_log(line);
    events_fire_item(CAT_BOOK, id, TR_AFTER_READ);
}

/* A click anywhere: the next turn of the page, the next page, or the end. 1 = taken (it takes every click). */
int books_click(void)
{
    if (!open_.on) return 0;
    if (open_.turn + 1 < open_.turns) { open_.turn++; sound_play(num(open_.book, "PageFlipSoundResourceId", -1)); return 1; }
    if (open_.page + 1 >= open_.pageCount) { finish(); return 1; }
    open_.page++;
    sound_play(num(open_.book, "PageFlipSoundResourceId", -1));
    build_page();
    return 1;
}

int books_escape(void) { if (!open_.on) return 0; finish(); return 1; }

void books_draw(uint32_t *pixels, uint32_t ms)
{
    if (!open_.on) return;
    const Sprite *doll = sprite_find(str(open_.book, "PaperDollSpriteGroup"), str(open_.book, "PaperDollSpriteName"));
    const Picture *pic = sprite_picture_at(doll, ms);
    if (pic) world_blit(pixels, pic, open_.bx, open_.by, open_.bw, open_.bh);
    for (int i = 0; i < open_.frameCount; i++)
    {
        TextFrame *fr = &open_.frames[i];
        if (open_.turn >= fr->turns) continue;
        int y = fr->y;
        for (int k = fr->turnStart[open_.turn]; k < fr->turnStart[open_.turn + 1]; k++)
        {
            Line *line = &fr->laid.lines[k];
            if (line->sprite)
            {
                const Picture *sp = sprite_picture_at(line->sprite, ms);
                if (sp && y + line->spriteH <= fr->y + fr->h + 1) world_blit(pixels, sp, fr->x, y, line->spriteW, line->spriteH);
                y += line->height + line->gap;
                continue;
            }
            int offset = line->align == 1 ? (fr->w - line->width) / 2 : line->align == 2 ? fr->w - line->width : 0;
            for (int p = 0; p < line->count; p++)
            {
                Piece *pc = &line->pieces[p];
                const Font *drawIn = pc->font ? pc->font : open_.font;
                int px = fr->x + offset + pc->x, py = y + line->height - font_line_height(drawIn) * pc->scale; /* their feet on one line (the tallest sets it) */
                font_draw_frame(drawIn, pixels, px, py, pc->text, pc->colour, pc->scale, 0);
                if (pc->bold) font_draw_frame(drawIn, pixels, px + 1, py, pc->text, pc->colour, pc->scale, 0);
            }
            y += line->height + line->gap;
        }
    }
}

void books_reset(void)
{
    free_frames();
    open_.on = 0;
    cJSON_Delete(overrides); cJSON_Delete(settings); cJSON_Delete(readBooks);
    overrides = settings = readBooks = NULL;
}
