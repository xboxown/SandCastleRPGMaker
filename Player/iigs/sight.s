; Sandcastle RPG Maker - Apple IIgs player: line of sight, as the Windows Ultima 6 engine (U6GameWindow.U6Sight.cs, Nuvie's
; boundary fill): from the Avatar's square sight spreads (up, down, left, right) to every square it can pass through,
; within the view and one more round it; a square that stops sight is seen but not seen through. Walls at the corners of
; what is seen show too; walls', doors' and windows' squares always show. Everything else in the view is black.
; Sight is stopped by a cell's mark (bit 15: a wall or something that Occludes), a closed door, and ground that can't be
; walked on and isn't water.

SG_W = 40                       ; the fill's grid: up to 40 x 40 squares (the view and a square round it)

; The cell's sight marks: A = bits 14-15 of its tile number (0 off the map).
CellMarks:
        cpx MapW
        bcs .off
        cpy MapH
        bcs .off
        jsr MapTile             ; (Ptr on it)
        lda [Ptr]
        and #$C000
        rts
.off:   lda #0
        rts

; Carry set if square X, Y stops sight.
StopsSight:
        stx SX0
        sty SY0
        jsr CellMarks
        and #$8000
        bne .yes
        ldx SX0
        ldy SY0
        jsr MapTile
        cmp #$FFFF
        beq .yes
        tay
        lda [FlagsPtr],y
        and #$0003              ; walkable or water: sight goes on
        beq .yes
        jsr ClosedDoorAt
        rts
.yes:   sec
        rts

; Carry set if a closed door stands at SX0, SY0 (DoorList, made for this drawing).
ClosedDoorAt:
        ldx #0
.l:     cpx DoorN
        bcs .no
        lda DoorX,x
        cmp SX0
        bne .n
        lda DoorY,x
        cmp SY0
        beq .yes
.n:     inx
        inx
        bra .l
.no:    clc
        rts
.yes:   sec
        rts

; The closed doors on this map near the Avatar (32 at most).
FindDoors:
        stz DoorN
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T4
        beq .d
.l:     ldy #6
        lda [ItemPtr],y
        and #$F000+96+256+4+16
        sec
        sbc CurBits
        cmp #4                  ; a door on this map, shut
        bne .n
        lda [ItemPtr]
        sec
        sbc PX
        jsr AbsX
        cmp #20
        bcs .n
        ldy #2
        lda [ItemPtr],y
        sec
        sbc PY
        jsr AbsY
        cmp #20
        bcs .n
        lda DoorN
        cmp #64
        bcs .d
        tax
        lda [ItemPtr]
        sta DoorX,x
        ldy #2
        lda [ItemPtr],y
        sta DoorY,x
        inc DoorN
        inc DoorN
.n:     lda #16
        jsr NextItem
        dec T4
        bne .l
.d:     rts

; The fill and the black (after everything is drawn, before the darkness).
DrawSight:
        lda SightOn
        bne .on
        rts
.on:    jsr FindDoors
        lda VCols               ; the grid: the view and a square round it
        clc
        adc #2
        cmp #SG_W
        bcc .w
        lda #SG_W
.w:     sta SGW
        lda VRows
        clc
        adc #2
        cmp #SG_W
        bcc .h
        lda #SG_W
.h:     sta SGH
        lda PX                  ; its top left on the map
        sec
        sbc HalfC
        dec a
        sta SGX
        lda PY
        sec
        sbc HalfR
        dec a
        sta SGY
        ldx #0                  ; nothing seen yet
        lda #0
.z:     sta Seen,x
        inx
        inx
        cpx #SG_W*SG_W
        bcc .z
        lda HalfC               ; the Avatar's square: seen, the fill's start
        inc a
        sta GQX
        lda HalfR
        inc a
        sta GQY
        jsr SgIndex
        tax
        sep #$20
        .a8
        lda #1
        sta Seen,x
        rep #$20
        .a16
        stz GHead
        lda #2
        sta GTail
        lda GQX
        sta FQ
        lda GQY
        sta FQ+1
.q:     lda GHead               ; the queue of squares to look from
        cmp GTail
        bcc .take
        brl .corners
.take:  tax
        lda FQ,x
        and #$00FF
        sta GQX
        lda FQ+1,x
        and #$00FF
        sta GQY
        inc GHead
        inc GHead
        lda GQX                  ; a square that stops sight (not the Avatar's): seen, not seen through
        cmp HalfC
        bne .chk
        lda GQY
        cmp HalfR
        beq .spread
.chk:   jsr SgStops
        bcs .q
.spread: ldx #0
.dir:   lda GQX
        clc
        adc SgDX,x
        sta GNX
        lda GQY
        clc
        adc SgDY,x
        sta GNY
        lda GNX
        cmp SGW
        bcs .nd
        lda GNY
        cmp SGH
        bcs .nd
        phx
        lda GNX
        sta T4
        lda GNY
        jsr SgIdxOf
        tax
        lda Seen,x
        and #$00FF
        bne .been
        sep #$20
        .a8
        lda #1
        sta Seen,x
        rep #$20
        .a16
        ldx GTail               ; on the queue
        cpx #SG_W*SG_W*2
        bcs .been
        lda GNX
        sep #$20
        .a8
        sta FQ,x
        lda GNY
        sta FQ+1,x
        rep #$20
        .a16
        inc GTail
        inc GTail
.been:  plx
.nd:    inx
        inx
        cpx #8
        bcc .dir
        brl .q
.corners:                       ; walls at the corners of what is seen show too
        stz GQY
.cy:    stz GQX
.cx:    lda GQX
        sta T4
        lda GQY
        jsr SgIdxOf
        tax
        lda Seen,x
        and #$00FF
        cmp #1
        bne .cn
        jsr SgStops
        bcs .cn
        ldx #8
.dg:    lda GQX
        clc
        adc SgDX,x
        sta GNX
        lda GQY
        clc
        adc SgDY,x
        sta GNY
        lda GNX
        cmp SGW
        bcs .dn
        lda GNY
        cmp SGH
        bcs .dn
        phx
        lda GQX
        pha
        lda GQY
        pha
        lda GNX
        sta GQX
        lda GNY
        sta GQY
        jsr SgStops
        pla
        sta GQY
        pla
        sta GQX
        bcc .dp
        lda GNX
        sta T4
        lda GNY
        jsr SgIdxOf
        tax
        sep #$20
        .a8
        lda Seen,x
        bne .dk
        lda #2                  ; (2: a corner wall - not spread from)
        sta Seen,x
.dk:    rep #$20
        .a16
.dp:    plx
.dn:    inx
        inx
        cpx #16
        bcc .dg
.cn:    inc GQX
        lda GQX
        cmp SGW
        bcs .cyn
        brl .cx
.cyn:
        inc GQY
        lda GQY
        cmp SGH
        bcs .black
        brl .cy
.black: stz Row                 ; the view's squares not seen (and not walls): black
.row:   stz Col
.col:   lda Col
        inc a
        sta T4
        lda Row
        inc a
        jsr SgIdxOf
        tax
        lda Seen,x
        and #$00FF
        bne .next
        lda PX                  ; a wall's, door's or window's square always shows
        sec
        sbc HalfC
        clc
        adc Col
        tax
        lda PY
        sec
        sbc HalfR
        clc
        adc Row
        tay
        jsr CellMarks
        and #$4000
        bne .next
        jsr BlackCell
.next:  inc Col
        lda Col
        cmp VCols
        bcc .col
        inc Row
        lda Row
        cmp VRows
        bcc .row
        rts

; Carry set if the fill's square GQX, GQY stops sight.
SgStops:
        lda SGX
        clc
        adc GQX
        tax
        lda SGY
        clc
        adc GQY
        tay
        jmp StopsSight

; A = the fill's index of square GQX, GQY (row x SG_W + column).
SgIndex:
        lda GQX
        sta T4
        lda GQY
; A = row A, column T4.
SgIdxOf:
        sta MulA
        lda #SG_W
        sta MB0
        phx
        jsr Mul16
        plx
        lda MulR
        clc
        adc T4
        rts

; The view's square Col, Row: black (in VIEWBUF).
BlackCell:
        lda Row
        xba
        asl a
        sta T0
        asl a
        asl a
        clc
        adc T0
        clc
        adc ViewOff
        sta T0
        lda Col
        asl a
        asl a
        asl a
        clc
        adc T0
        tax
        lda #16
        sta T5
.b:     lda #0
        sta >VIEWBUF,x
        sta >VIEWBUF+2,x
        sta >VIEWBUF+4,x
        sta >VIEWBUF+6,x
        txa
        clc
        adc #160
        tax
        dec T5
        bne .b
        rts

SightOn:  .dw 1
SGW:      .dw 0
SGH:      .dw 0
SGX:      .dw 0
SGY:      .dw 0
SX0:      .dw 0
SY0:      .dw 0
GQX:       .dw 0
GQY:       .dw 0
GNX:      .dw 0
GNY:      .dw 0
GHead:    .dw 0
GTail:    .dw 0
DoorN:    .dw 0
DoorX:    .ds 64
DoorY:    .ds 64
SgDX:     .dw 1,$FFFF,0,0,1,1,$FFFF,$FFFF
SgDY:     .dw 0,0,1,$FFFF,1,$FFFF,1,$FFFF
Seen:     .ds SG_W*SG_W+2
FQ:       .ds SG_W*SG_W*2+4
