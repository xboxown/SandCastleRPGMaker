/*
 * The game's fonts (Sprite Editor > Fonts: InGameFont, DialogFont) as the
 * Windows engine draws them (SpriteFont.cs): frame N is the character
 * 32 + N; a glyph with transparent edges is trimmed to its ink with 1 pixel
 * before the next (proportional, like Exult's fonts); a solid cell is drawn
 * whole. A game with no font drawn yet uses a plain built-in one.
 */
#ifndef SANDCASTLE_FONT_H
#define SANDCASTLE_FONT_H

#include <stdint.h>

typedef struct Font Font;

/* A font by its sprite's name in the Fonts group ("InGameFont", "DialogFont"); never NULL (the built-in one if it has no glyphs). */
Font *font_get(const char *name);

int font_line_height(const Font *font);
int font_text_width(const Font *font, const char *text);

/* Draws text at (x, y) into a 320 x 200 frame; colour 0 keeps the glyphs' own colours, else they're tinted. */
void font_draw(const Font *font, uint32_t *pixels, int x, int y, const char *text, uint32_t colour);

/* Word-wrapped into lines no wider than width ('\n' breaks a line); returns how many lines; their starts and lengths go into starts/lengths (up to max). */
int font_wrap(const Font *font, const char *text, int width, int max, const char **starts, int *lengths);

#endif
