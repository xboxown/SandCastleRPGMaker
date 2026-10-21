#include "font.h"
#include "pictures.h"
#include "platform.h"
#include <stdlib.h>
#include <string.h>

typedef struct Glyph { const Picture *picture; int left, width, advance; } Glyph;

struct Font
{
    char name[64];
    Glyph glyphs[95];
    int cellHeight, spaceWidth, builtIn;
};

static Font fonts[4];
static int fontCount;

/* The built-in font: 5 x 7 capitals, digits and signs (each row is a byte, the top bits used). */
static const unsigned char BUILT_IN[95][7] = {
    {0},{4,4,4,4,4,0,4},{10,10,0,0,0,0,0},{10,31,10,10,31,10,0},{4,15,20,14,5,30,4},{24,25,2,4,8,19,3},{8,20,20,8,21,18,13},{4,4,0,0,0,0,0},
    {2,4,8,8,8,4,2},{8,4,2,2,2,4,8},{0,4,21,14,21,4,0},{0,4,4,31,4,4,0},{0,0,0,0,4,4,8},{0,0,0,31,0,0,0},{0,0,0,0,0,0,4},{0,1,2,4,8,16,0},
    {14,17,19,21,25,17,14},{4,12,4,4,4,4,14},{14,17,1,2,4,8,31},{30,1,1,14,1,1,30},{2,6,10,18,31,2,2},{31,16,30,1,1,17,14},{6,8,16,30,17,17,14},{31,1,2,4,8,8,8},
    {14,17,17,14,17,17,14},{14,17,17,15,1,2,12},{0,0,4,0,0,4,0},{0,0,4,0,4,4,8},{2,4,8,16,8,4,2},{0,0,31,0,31,0,0},{8,4,2,1,2,4,8},{14,17,1,2,4,0,4},
    {14,17,23,21,23,16,14},{14,17,17,31,17,17,17},{30,17,17,30,17,17,30},{14,17,16,16,16,17,14},{30,17,17,17,17,17,30},{31,16,16,30,16,16,31},{31,16,16,30,16,16,16},{14,17,16,23,17,17,15},
    {17,17,17,31,17,17,17},{14,4,4,4,4,4,14},{7,2,2,2,2,18,12},{17,18,20,24,20,18,17},{16,16,16,16,16,16,31},{17,27,21,21,17,17,17},{17,25,21,19,17,17,17},{14,17,17,17,17,17,14},
    {30,17,17,30,16,16,16},{14,17,17,17,21,18,13},{30,17,17,30,20,18,17},{15,16,16,14,1,1,30},{31,4,4,4,4,4,4},{17,17,17,17,17,17,14},{17,17,17,17,17,10,4},{17,17,17,21,21,21,10},
    {17,17,10,4,10,17,17},{17,17,10,4,4,4,4},{31,1,2,4,8,16,31},{14,8,8,8,8,8,14},{0,16,8,4,2,1,0},{14,2,2,2,2,2,14},{4,10,17,0,0,0,0},{0,0,0,0,0,0,31},
    {8,4,0,0,0,0,0}
};

static Glyph builtInGlyph[95];
static Picture builtInPictures[95];

static void make_built_in(Font *f)
{
    f->builtIn = 1;
    f->cellHeight = 8;
    f->spaceWidth = 4;
    for (int i = 0; i < 95; i++)
    {
        int c = i + 32;
        int src = c >= 'a' && c <= 'z' ? c - 32 - 32 : (c > '`' ? 0 : i); /* lowercase as capitals; past the table: blank */
        Picture *p = &builtInPictures[i];
        if (!p->pixels)
        {
            p->width = 5; p->height = 7;
            p->pixels = (uint32_t *)calloc(35, 4);
            for (int y = 0; y < 7; y++)
                for (int x = 0; x < 5; x++)
                    if (src < 65 && (BUILT_IN[src][y] >> (4 - x)) & 1) p->pixels[y * 5 + x] = 0xFFFFFFFFu;
        }
        builtInGlyph[i] = (Glyph){ p, 0, 5, 6 };
        f->glyphs[i] = builtInGlyph[i];
    }
}

Font *font_get(const char *name)
{
    for (int i = 0; i < fontCount; i++) if (strcmp(fonts[i].name, name) == 0) return &fonts[i];
    if (fontCount >= 4) return &fonts[0];
    Font *f = &fonts[fontCount++];
    memset(f, 0, sizeof *f);
    strncpy(f->name, name, sizeof f->name - 1);
    const Sprite *sprite = sprite_find("Fonts", name);
    int inkTotal = 0, inkCount = 0, maxCell = 0, any = 0;
    for (int i = 0; sprite && i < sprite->frameCount && i < 95; i++)
    {
        const Picture *p = frame_picture(&sprite->frames[i]);
        if (!p) continue;
        any = 1;
        if (p->height > f->cellHeight) f->cellHeight = p->height;
        if (p->width > maxCell) maxCell = p->width;
        int left = p->width, right = -1, transparent = 0;
        for (int y = 0; y < p->height; y++)
            for (int x = 0; x < p->width; x++)
            {
                if ((p->pixels[y * p->width + x] >> 24) == 0) { transparent = 1; continue; }
                if (x < left) left = x;
                if (x > right) right = x;
            }
        int width = right < 0 ? 0 : right - left + 1;
        if (right < 0) left = 0;
        if (!transparent) { left = 0; width = p->width; }
        f->glyphs[i] = (Glyph){ p, left, width, width + (transparent && width > 0 ? 1 : 0) };
        if (width > 0 && transparent && i != 0) { inkTotal += width; inkCount++; }
    }
    if (!any || f->cellHeight == 0) { make_built_in(f); return f; }
    f->spaceWidth = f->glyphs[0].width > 0 ? f->glyphs[0].advance
        : (int)((inkCount ? (double)inkTotal / inkCount : maxCell / 2.0) * 0.6 + 0.5) + 1;
    if (f->spaceWidth < 2) f->spaceWidth = 2;
    return f;
}

static const Glyph *glyph_of(const Font *f, char c)
{
    if (c < 32 || c > 126) c = '?';
    const Glyph *g = &f->glyphs[c - 32];
    if (g->picture) return g;
    if (c >= 'a' && c <= 'z' && f->glyphs[c - 32 - 32].picture) return &f->glyphs[c - 32 - 32]; /* a sheet with capitals only */
    if (c >= 'A' && c <= 'Z' && f->glyphs[c + 32 - 32].picture) return &f->glyphs[c + 32 - 32];
    return f->glyphs['?' - 32].picture ? &f->glyphs['?' - 32] : NULL;
}

static int advance_of(const Font *f, char c)
{
    if (c == ' ') return f->spaceWidth;
    if (c == '\t') return f->spaceWidth * 4;
    const Glyph *g = glyph_of(f, c);
    return g ? g->advance : f->spaceWidth;
}

int font_line_height(const Font *f) { return f->cellHeight + 1; }

int font_text_width(const Font *f, const char *text)
{
    int w = 0;
    for (; *text && *text != '\n'; text++) w += advance_of(f, *text);
    return w;
}

void font_draw(const Font *f, uint32_t *pixels, int x, int y, const char *text, uint32_t colour)
{
    for (; *text && *text != '\n'; text++)
    {
        const Glyph *g = *text == ' ' || *text == '\t' ? NULL : glyph_of(f, *text);
        if (g && g->picture)
        {
            const Picture *p = g->picture;
            for (int j = 0; j < p->height; j++)
            {
                int sy = y + j + (f->cellHeight - p->height);
                if (sy < 0 || sy >= SCREEN_H) continue;
                for (int i = 0; i < g->width; i++)
                {
                    int sx = x + i;
                    if (sx < 0 || sx >= SCREEN_W) continue;
                    uint32_t c = p->pixels[j * p->width + g->left + i];
                    uint32_t a = c >> 24;
                    if (a < 128) continue;
                    pixels[sy * SCREEN_W + sx] = colour ? (colour | 0xFF000000u) : (c | 0xFF000000u);
                }
            }
        }
        x += advance_of(f, *text);
    }
}

int font_wrap(const Font *f, const char *text, int width, int max, const char **starts, int *lengths)
{
    int lines = 0;
    const char *line = text, *p = text;
    int lineWidth = 0;
    const char *lastBreak = NULL;
    while (*p && lines < max)
    {
        if (*p == '\n') { starts[lines] = line; lengths[lines++] = (int)(p - line); line = ++p; lineWidth = 0; lastBreak = NULL; continue; }
        if (*p == ' ') lastBreak = p;
        lineWidth += advance_of(f, *p);
        if (lineWidth > width && p > line)
        {
            const char *end = lastBreak && lastBreak > line ? lastBreak : p;
            starts[lines] = line; lengths[lines++] = (int)(end - line);
            line = end; while (*line == ' ') line++;
            p = line; lineWidth = 0; lastBreak = NULL;
            continue;
        }
        p++;
    }
    if (lines < max && p > line) { starts[lines] = line; lengths[lines++] = (int)(p - line); }
    return lines;
}
