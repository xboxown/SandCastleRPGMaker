; The Vampire build's AMMX routines (the 68080's 64-bit SIMD: four pixels an instruction). Assembled by build_vampire.sh with
; vasm -m68080. Each is checked against platform_amiga.c's C version as the game starts (vampire_selftest): used only if it
; gives exactly the same pixels, else the C version is (and Game.log says so).
;
; C: void ammx_rgb565_row(const uint32_t *src, uint16_t *dst, unsigned long count)  - count a multiple of 4
;    the game's 0xAARRGGBB pixels into SAGA's R5|G6|B5 (big-endian), four at a time (PACK3216)

        machine ac68080
        section code,code

        xdef    _ammx_rgb565_row
_ammx_rgb565_row:
        move.l  4(sp),a0                ; src
        move.l  8(sp),a1                ; dst
        move.l  12(sp),d0               ; count
        move.l  d2,-(sp)                ; d2 is the caller's (only its low 32 bits matter to it)
        lsr.l   #2,d0                   ; four pixels a pass
        beq.s   .done
.loop:  load    (a0)+,d1                ; pixels 0, 1
        load    (a0)+,d2                ; pixels 2, 3
        pack3216 d1,d2,d1               ; the four as 5-6-5
        store   d1,(a1)+
        subq.l  #1,d0
        bne.s   .loop
.done:  move.l  (sp)+,d2
        rts

; C: void ammx_copy_row(const void *src, void *dst, unsigned long bytes)  - bytes a multiple of 8: 64-bit loads and stores
        xdef    _ammx_copy_row
_ammx_copy_row:
        move.l  4(sp),a0
        move.l  8(sp),a1
        move.l  12(sp),d0
        lsr.l   #3,d0
        beq.s   .cdone
.cloop: load    (a0)+,d1
        store   d1,(a1)+
        subq.l  #1,d0
        bne.s   .cloop
.cdone: rts
