/* Mixing colours without dividing (a 68k divide costs tens of cycles a pixel): alpha 0..255 taken as 0..256,
   red and blue mixed together in one multiply, green in another. Within one step of the exact "/ 255" result. */
#ifndef SANDCASTLE_BLEND_H
#define SANDCASTLE_BLEND_H
#include <stdint.h>

/* c over d, c seen a/255 (the result opaque) */
static inline uint32_t blend_px(uint32_t d, uint32_t c, uint32_t a)
{
    uint32_t a1 = a + (a >> 7), k = 256 - a1;
    uint32_t rb = (((c & 0xFF00FFu) * a1 + (d & 0xFF00FFu) * k) >> 8) & 0xFF00FFu;
    uint32_t g = (((c & 0xFF00u) * a1 + (d & 0xFF00u) * k) >> 8) & 0xFF00u;
    return 0xFF000000u | rb | g;
}

/* x / 255 for 0 <= x <= 65535, exactly */
static inline uint32_t div255(uint32_t x) { return (x + 1 + (x >> 8)) >> 8; }

#endif
