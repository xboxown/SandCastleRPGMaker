/* System Settings > Game Window > CRT effect (Editor/CrtLook.cs on Windows): scanlines and a softened picture, as an old TV. */
#ifndef SANDCASTLE_CRT_H
#define SANDCASTLE_CRT_H
#include <stdint.h>

/* How much darker every other line is (out of 256: 45% darker), as Windows (CrtLook.ScanlineDarkness). */
#define CRT_KEEP 141

/* The picture twice as big (w x h in, 2w x 2h out): each row twice - the second CRT_KEEP darker - and each pixel twice, the
   second blended with the pixel to its right (the beam's softness). out: 4 w h pixels. Rows [y0, y1) of the picture in. */
void crt_double(const uint32_t *in, int w, int h, uint32_t *out, int outPitch, int y0, int y1);

/* Where the picture can't be doubled (the PS2, the Amiga): in place, the lower half of each game pixel's rows darkened
   (scale: the picture's pixels to a game pixel) - and softened along each row. */
void crt_in_place(uint32_t *pixels, int w, int h, int scale);

#endif
