/* The CRT effect (crt.h). Whole numbers only - the PS2's and the Amiga's processors do floating point slowly. */
#include "crt.h"
#include <stddef.h>

static inline uint32_t dim(uint32_t c)
{
    return 0xFF000000u | ((((c & 0xFF00FFu) * CRT_KEEP) >> 8) & 0xFF00FFu) | ((((c & 0xFF00u) * CRT_KEEP) >> 8) & 0xFF00u);
}
static inline uint32_t half_way(uint32_t a, uint32_t b) /* the average of two colours, per channel */
{
    return 0xFF000000u | (((a & 0xFEFEFEu) >> 1) + ((b & 0xFEFEFEu) >> 1) + (a & b & 0x010101u));
}

void crt_double(const uint32_t *in, int w, int h, uint32_t *out, int outPitch, int y0, int y1)
{
    if (y0 < 0) y0 = 0;
    if (y1 > h) y1 = h;
    for (int y = y0; y < y1; y++)
    {
        const uint32_t *row = in + (size_t)y * (size_t)w;
        uint32_t *bright = out + (size_t)(2 * y) * (size_t)outPitch, *dark = bright + outPitch;
        int x = 0;
        for (; x < w - 1; x++)
        {
            uint32_t c = row[x], m = half_way(c, row[x + 1]);
            bright[2 * x] = c; bright[2 * x + 1] = m;
            dark[2 * x] = dim(c); dark[2 * x + 1] = dim(m);
        }
        bright[2 * x] = bright[2 * x + 1] = row[x];
        dark[2 * x] = dark[2 * x + 1] = dim(row[x]);
    }
}

void crt_in_place(uint32_t *pixels, int w, int h, int scale)
{
    if (scale < 1) scale = 1;
    int half = scale / 2; /* rows of each game pixel kept bright; the rest darkened (scale 1: every other row) */
    for (int y = 0; y < h; y++)
    {
        uint32_t *row = pixels + (size_t)y * (size_t)w;
        int darken = scale == 1 ? (y & 1) : (y % scale) >= (half ? half : 1);
        uint32_t last = row[0];
        for (int x = 0; x < w; x++)
        {
            uint32_t c = row[x], s = half_way(c, last); /* softened with the pixel to its left */
            last = c;
            row[x] = darken ? dim(s) : s;
        }
    }
}
