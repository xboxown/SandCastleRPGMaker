/*
 * Input Number, Input Text and Select Item (GameTestWindow.Script AskInput /
 * SelectItemCommand): a box in the middle of the screen with the prompt and
 * a line to type in (digits and '-' only for a number, as long as allowed),
 * or a list of what the main character carries to pick from. Enter: OK,
 * Escape: cancel; a list row is picked by a click, a double click picks it
 * at once. The game waits while it's up.
 */
#include "prompt.h"
#include "font.h"
#include "platform.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

typedef struct Prompt
{
    int open, kind, longest, chosen, top, count;
    char question[256], text[201];
    char names[128][64]; int ids[128];
    void (*done)(int ok, const char *text, int id, void *context); void *context;
    uint32_t lastClick; int lastRow;
} Prompt;
static Prompt p;

int prompt_open(void) { return p.open; }

static void finish(int ok)
{
    if (!p.open) return;
    p.open = 0;
    int picked = ok && (p.kind != PROMPT_LIST || (p.chosen >= 0 && p.chosen < p.count));
    if (p.done) p.done(picked, p.kind == PROMPT_LIST ? (picked ? p.names[p.chosen] : "") : p.text, p.kind == PROMPT_LIST && picked ? p.ids[p.chosen] : -1, p.context);
}

void prompt_ask(int kind, const char *question, const char *start, int longest, void (*done)(int ok, const char *text, int id, void *context), void *context)
{
    finish(0);
    memset(&p, 0, sizeof p);
    p.open = 1; p.kind = kind; p.done = done; p.context = context; p.lastRow = -1;
    p.longest = longest < 1 ? 1 : longest > 200 ? 200 : longest;
    snprintf(p.question, sizeof p.question, "%s", question ? question : "");
    snprintf(p.text, (size_t)p.longest + 1, "%s", start ? start : "");
}

void prompt_add_row(const char *name, int id)
{
    if (!p.open || p.count >= 128) return;
    snprintf(p.names[p.count], sizeof p.names[0], "%s", name);
    p.ids[p.count++] = id;
}

int prompt_key(int key, int ch)
{
    if (!p.open) return 0;
    if (key == KEY_ENTER) { finish(1); return 1; }
    if (key == KEY_ESCAPE) { finish(0); return 1; }
    if (p.kind == PROMPT_LIST)
    {
        if (key == KEY_UP && p.chosen > 0) p.chosen--;
        if (key == KEY_DOWN && p.chosen + 1 < p.count) p.chosen++;
        if (p.chosen < p.top) p.top = p.chosen;
        if (p.chosen >= p.top + 8) p.top = p.chosen - 7;
        return 1;
    }
    size_t n = strlen(p.text);
    if (key == KEY_BACKSPACE && n > 0) p.text[n - 1] = 0;
    if (ch >= 32 && ch < 127 && (int)n < p.longest)
    {
        if (p.kind == PROMPT_NUMBER && !(ch >= '0' && ch <= '9') && ch != '-') return 1; /* digits and '-' only */
        p.text[n] = (char)ch; p.text[n + 1] = 0;
    }
    return 1;
}

static void box(int *x, int *y, int *w, int *h)
{
    const Font *f = font_get("InGameFont");
    int lh = font_line_height(f);
    *w = 220;
    int qw = font_text_width(f, p.question) + 20;
    if (qw > *w) *w = qw > SCREEN_W - 8 ? SCREEN_W - 8 : qw;
    int rows = p.kind == PROMPT_LIST ? (p.count < 8 ? (p.count ? p.count : 1) : 8) : 1;
    *h = 10 + (p.question[0] ? lh + 4 : 0) + rows * (lh + 2) + 6 + lh + 6;
    *x = (SCREEN_W - *w) / 2; *y = (SCREEN_H - *h) / 2;
}

static void fill(uint32_t *px, int x, int y, int w, int h, uint32_t c)
{
    for (int j = y < 0 ? 0 : y; j < y + h && j < SCREEN_H; j++) for (int i = x < 0 ? 0 : x; i < x + w && i < SCREEN_W; i++) px[j * SCREEN_W + i] = c;
}

int prompt_click(int mx, int my, uint32_t now)
{
    if (!p.open) return 0;
    if (p.kind != PROMPT_LIST) return 1;
    int x, y, w, h;
    box(&x, &y, &w, &h);
    const Font *f = font_get("InGameFont");
    int lh = font_line_height(f), top = y + 8 + (p.question[0] ? lh + 4 : 0);
    if (mx >= x + 8 && mx < x + w - 8 && my >= top)
    {
        int row = p.top + (my - top) / (lh + 2);
        if (row < p.count && row - p.top < 8)
        {
            int twice = row == p.lastRow && now - p.lastClick < 500;
            p.chosen = row; p.lastRow = row; p.lastClick = now;
            if (twice) finish(1);
        }
    }
    return 1;
}

void prompt_draw(uint32_t *px, uint32_t ms)
{
    if (!p.open) return;
    int x, y, w, h;
    box(&x, &y, &w, &h);
    const Font *f = font_get("InGameFont");
    int lh = font_line_height(f);
    fill(px, x, y, w, h, 0xFFDAA520u);                      /* goldenrod border */
    fill(px, x + 2, y + 2, w - 4, h - 4, 0xFF14100Cu);
    int cy = y + 8;
    if (p.question[0]) { font_draw(f, px, x + 10, cy, p.question, 0xFFFFFFFFu); cy += lh + 4; }
    if (p.kind == PROMPT_LIST)
    {
        if (!p.count) { font_draw(f, px, x + 10, cy, "(nothing carried)", 0xFFB0B0B0u); cy += lh + 2; }
        for (int i = p.top; i < p.count && i < p.top + 8; i++)
        {
            if (i == p.chosen) fill(px, x + 8, cy - 1, w - 16, lh + 2, 0xFF3A2E10u);
            font_draw(f, px, x + 10, cy, p.names[i], i == p.chosen ? 0xFFFFE080u : 0xFFFFFFFFu);
            cy += lh + 2;
        }
    }
    else
    {
        fill(px, x + 8, cy - 1, w - 16, lh + 2, 0xFF2A2420u);
        char shown[210];
        snprintf(shown, sizeof shown, "%s%s", p.text, (ms / 500) % 2 ? "_" : ""); /* the caret blinking */
        font_draw(f, px, x + 10, cy, shown, 0xFFFFFFFFu);
        cy += lh + 2;
    }
    font_draw(f, px, x + 10, cy + 6, "Enter: OK    Escape: cancel", 0xFFB0B0B0u);
}

void prompt_reset(void) { p.open = 0; }
