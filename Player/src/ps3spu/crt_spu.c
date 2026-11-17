/*
 * The PS3's SPEs making the CRT picture (crt.c's crt_double, the same arithmetic): each SPE thread is given a band of the
 * 640 x 400 picture's rows, brings each row in from main memory, makes its two doubled rows (the second CRT_KEEP darker,
 * each pixel's second copy blended with the pixel to its right) and sends them straight into the texture in the graphics
 * chip's memory. It waits for the next frame's signal; a signal of 0xFFFFFFFF ends it. Built by build_ps3.sh (spu-gcc).
 */
#include <spu_intrinsics.h>
#include <spu_mfcio.h>
#include <stdint.h>
#include "crt_job.h"

extern void spu_thread_exit(uint32_t);

#define CRT_KEEP 141 /* crt.h: 45% darker */
#define MAX_W 1024   /* the widest row it takes (640 for the PS3's CRT picture) */

static CrtJob job __attribute__((aligned(128)));
static uint32_t in[2][MAX_W] __attribute__((aligned(128)));
static uint32_t bright[2][2 * MAX_W] __attribute__((aligned(128)));
static uint32_t dark[2][2 * MAX_W] __attribute__((aligned(128)));

static inline uint32_t dim(uint32_t c)
{
    return 0xFF000000u | ((((c & 0xFF00FFu) * CRT_KEEP) >> 8) & 0xFF00FFu) | ((((c & 0xFF00u) * CRT_KEEP) >> 8) & 0xFF00u);
}
static inline uint32_t half_way(uint32_t a, uint32_t b)
{
    return 0xFF000000u | (((a & 0xFEFEFEu) >> 1) + ((b & 0xFEFEFEu) >> 1) + (a & b & 0x010101u));
}
static void wait_tag(int tag) { mfc_write_tag_mask(1u << tag); spu_mfcstat(MFC_TAG_UPDATE_ALL); }

/* Up to 16 KB a DMA: a row in pieces. */
static void get_big(void *ls, uint64_t ea, uint32_t bytes, int tag)
{
    for (uint32_t at = 0; at < bytes; at += 16384) { uint32_t n = bytes - at > 16384 ? 16384 : bytes - at; mfc_get((char *)ls + at, ea + at, n, tag, 0, 0); }
}
static void put_big(void *ls, uint64_t ea, uint32_t bytes, int tag)
{
    for (uint32_t at = 0; at < bytes; at += 16384) { uint32_t n = bytes - at > 16384 ? 16384 : bytes - at; mfc_put((char *)ls + at, ea + at, n, tag, 0, 0); }
}

int main(uint64_t jobEa, uint64_t arg2, uint64_t arg3, uint64_t arg4)
{
    (void)arg2; (void)arg3; (void)arg4;
    for (;;)
    {
        uint32_t frame = spu_read_signal1(); /* the next frame - or the end */
        if (frame == 0xFFFFFFFFu) break;
        mfc_get(&job, jobEa, sizeof job, 0, 0, 0);
        wait_tag(0);
        uint32_t w = job.w;
        if (w > MAX_W) w = MAX_W;
        uint32_t rowBytes = w * 4, outBytes = 2 * w * 4;
        int b = 0;
        if (job.y0 < job.y1) get_big(in[0], job.inEa + (uint64_t)job.y0 * job.inPitch, rowBytes, 1);
        for (uint32_t y = job.y0; y < job.y1; y++, b ^= 1)
        {
            wait_tag(1 + b); /* this row in (and this buffer's last rows out) */
            if (y + 1 < job.y1) get_big(in[b ^ 1], job.inEa + (uint64_t)(y + 1) * job.inPitch, rowBytes, 1 + (b ^ 1)); /* the next row on its way */
            const uint32_t *row = in[b];
            uint32_t *br = bright[b], *dk = dark[b];
            uint32_t x = 0;
            for (; x + 1 < w; x++)
            {
                uint32_t c = row[x], m = half_way(c, row[x + 1]);
                br[2 * x] = c; br[2 * x + 1] = m;
                dk[2 * x] = dim(c); dk[2 * x + 1] = dim(m);
            }
            br[2 * x] = br[2 * x + 1] = row[x];
            dk[2 * x] = dk[2 * x + 1] = dim(row[x]);
            uint64_t out = job.outEa + (uint64_t)(2 * y) * job.outPitch;
            put_big(br, out, outBytes, 1 + b);
            put_big(dk, out + job.outPitch, outBytes, 1 + b);
        }
        wait_tag(1); wait_tag(2); /* every row out */
        job.done = frame;
        mfc_putf(&job.done, jobEa + ((uint32_t)&job.done - (uint32_t)&job), 4, 0, 0, 0); /* after the rows */
        wait_tag(0);
    }
    spu_thread_exit(0);
    return 0;
}
