/* One SPE's share of a frame's CRT picture (platform_ps3.c gives it, crt_spu.c does it). 80 bytes, 16-aligned, as DMA wants. */
#ifndef SANDCASTLE_CRT_JOB_H
#define SANDCASTLE_CRT_JOB_H
#include <stdint.h>
typedef struct CrtJob
{
    uint64_t inEa, outEa;          /* the 640 x 400 picture (main memory), the texture (the graphics chip's memory) */
    uint32_t w, inPitch, outPitch; /* pixels a row in; bytes a row in and out */
    uint32_t y0, y1;               /* its rows [y0, y1) */
    uint32_t done;                 /* the frame it last finished (it writes it, last) */
    uint32_t pad[8];
} __attribute__((aligned(16))) CrtJob;
#endif
