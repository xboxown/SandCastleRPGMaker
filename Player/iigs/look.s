; Sandcastle RPG Maker - Apple IIgs player: the world's look. Night and light, as the Ultima 6 engine shows it on the
; IIgs: with Day / Night on (System Settings), from dusk the view is dark beyond the light - at dusk and dawn 6 squares
; round the Avatar, at night 2 - and lit lights (Object Editor > Light Source: its Other $F000, lit $100, how far it
; lights 1-7) light their own squares round them: on the map, carried (the Avatar's), and the Light spell (6, for its
; turns). Use lights / puts out a light. Animated tiles (Tile Properties' animation) cycle their frames. Roofs (Other
; $FFFD) are drawn over everything, but not while the Avatar is under one. Upper floors are maps of their own: the
; editor makes stairs and ladders their events.

ParseLook:
        lda [Ptr]
        sta DNOn
        ldy #2
        lda [Ptr],y
        sta DNDawn
        ldy #4
        lda [Ptr],y
        sta DNDay
        ldy #6
        lda [Ptr],y
        sta DNDusk
        ldy #8
        lda [Ptr],y
        sta DNNight
        lda #10
        jsr AddPtr
        jsr ParseTitle          ; the title menu's picture, words, introduction (title.s)
        lda [Ptr]
        sta AnimCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta AnimPtr
        lda Ptr+2
        sta AnimPtr+2
        ldx #0                  ; every tile itself to begin with
.r:     txa
        sta RemapTab,x
        lsr RemapTab,x
        inx
        inx
        cpx #2048
        bcc .r
        lda AnimPtr             ; mark the animated tiles
        sta Src
        lda AnimPtr+2
        sta Src+2
        ldx AnimCount
        beq .md
.mk:    ldy #2
        lda [Src],y
        cmp #1024
        bcs .mn
        phx
        tax
        sep #$20
        .a8
        lda #1
        sta AnimMark,x
        rep #$20
        .a16
        plx
.mn:    lda Src
        clc
        adc #10
        sta Src
        dex
        bne .mk
.md:    rts

; A = a tile from MapTile (kept): an animated one notes AnimVisible.
NoteAnim:
        pha
        cmp #1024
        bcs .o
        tax
        lda AnimMark,x
        and #$00FF
        beq .o
        sta AnimVisible
.o:     pla
        rts

; The view (VIEWBUF, bank $01) copied onto the screen, a row at a time; the pointer hidden meanwhile.
BlitView:
        jsr CurOff
        lda VRows               ; lines: rows x 16
        asl a
        asl a
        asl a
        asl a
        sta T5
        lda ViewOff             ; offset of the view's top left in the screen
        sta T4
.l:     lda T4
        clc
        adc #$2000
        tay
        clc
        adc ShakeSrc            ; a shaking view: copied from a little way off (fx.s)
        tax
        lda VCols               ; bytes: columns x 8
        asl a
        asl a
        asl a
        dec a
        phb
        mvn $01,$E1
        plb
        lda T4
        clc
        adc #160
        sta T4
        dec T5
        bne .l
        rts

; Bank $01's screen-sized memory ($012000) asked of the Memory Manager (QuickDraw may hold it as its shadow - then it is
; free for this too), and Super Hi-Res shadowing off, so drawing there doesn't show until it is copied.
ViewBuffer:
        pha
        pha
        pea $0000
        pea $8000
        lda UserId
        pha
        pea $C002               ; locked, fixed, at this address
        pea $0001
        pea $2000
        tool $0902              ; NewHandle (whether it is had or not, the place is used)
        pla
        pla
        sep #$20
        .a8
        lda >$E0C035
        sta Shadow
        ora #$08                ; no Super Hi-Res shadowing
        sta >$E0C035
        rep #$20
        .a16
        rts

Shadow:   .dw 0

; The animated tiles' next frame every 20 ticks (3 a second): the view drawn again.
AnimRun:
        lda AnimCount
        beq .out
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta T4
        pla
        lda T4
        sec
        sbc AnimTick
        cmp #20
        bcc .out
        lda T4
        sta AnimTick
        inc AnimFrame
        lda AnimPtr
        sta Src
        lda AnimPtr+2
        sta Src+2
        ldx AnimCount
.l:     lda [Src]               ; its frames
        sta T4
        lda AnimFrame
.m:     cmp T4
        bcc .f
        sec
        sbc T4
        bra .m
.f:     asl a
        inc a
        inc a
        tay
        lda [Src],y             ; that frame's tile
        pha
        ldy #2
        lda [Src],y             ; the tile it stands for
        asl a
        tay
        pla
        phx
        tyx
        sta RemapTab,x
        plx
        lda Src
        clc
        adc #10
        sta Src
        lda Src+2
        adc #0
        sta Src+2
        dex
        bne .l
        lda AnimVisible         ; none in sight: nothing to draw
        beq .out
        lda Mode                ; (not over a question's cursor or a screen of its own)
        cmp #7
        bcs .out
        jmp DrawView
.out:   rts

; Carry set if Other (A) is a light source's.
IsLight:
        sta ILt                 ; (its own: callers count with T4)
        cmp #$FFFC
        bcs .no
        and #$F000
        cmp #$F000
        bne .no
        lda ILt
        and #$0E00
        bne .no
        lda ILt
        sec
        rts
.no:    lda ILt
        clc
        rts
ILt:    .dw 0

; Use on a light (ObjRec on the map, or GridRec carried): lit <-> out.
ToggleLight:
        ldy #10
        lda [ItemPtr],y
        eor #$0100
        sta [ItemPtr],y
        jsr NewLine
        jsr RefreshInventory
        jsr DrawView
        jmp PromptLine

; ---- darkness ----

; The view's cells outside every light: black.
DrawDark:
        lda WCave               ; a Cave: dark, day or night - only lights lift it (weather.s keeps WCave)
        beq .nc
        lda #1
        sta AvLight
        bra .night
.nc:    lda DNOn
        bne .on
        rts
.on:    lda Hour                ; day: no darkness
        cmp DNDay
        bcc .notday
        cmp DNDusk
        bcs .notday
        rts
.notday:
        lda #2                  ; night 2, dusk and dawn 6
        sta AvLight
        lda Hour
        cmp DNDawn
        bcc .night
        cmp DNNight
        bcs .night
        lda #6
        sta AvLight
.night: lda LightLeft           ; the Light spell
        beq .carry
        lda #6
        jsr MoreLight
.carry: jsr LightsCarried
        jsr LightsOnMap
        jsr ViewCorner
        stz Row
.row:   stz Col
.col:   lda ViewX0
        clc
        adc Col
        sta DX
        lda ViewY0
        clc
        adc Row
        sta DY
        jsr Lit
        bcs .next
        lda Row                 ; this cell black: 16 lines of 8 bytes
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
.blk:   lda #0
        sta >VIEWBUF,x
        sta >VIEWBUF+2,x
        sta >VIEWBUF+4,x
        sta >VIEWBUF+6,x
        txa
        clc
        adc #160
        tax
        dec T5
        bne .blk
.next:  inc Col
        lda Col
        cmp VCols
        bcc .col
        inc Row
        lda Row
        cmp VRows
        bcc .row
        rts

; AvLight = the larger of it and A
MoreLight:
        cmp AvLight
        bcc .o
        sta AvLight
.o:     rts

; The Avatar's carried lit lights: its light the larger.
LightsCarried:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T4
        beq .d
.l:     ldy #6
        lda [ItemPtr],y
        and #96+256
        cmp #64
        bne .n
        ldy #10
        lda [ItemPtr],y
        jsr IsLight
        bcc .n
        and #$0100
        beq .n
        lda [ItemPtr],y
        and #$000F
        inc a
        jsr MoreLight
.n:     lda #16
        jsr NextItem
        dec T4
        bne .l
.d:     jmp MagicLights         ; worn magic that lights (worn.s)

; The lit lights on this map (16 at most): their cells and reach.
LightsOnMap:
        stz LCount
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T4
        beq .d
.l:     ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .n
        ldy #10
        lda [ItemPtr],y
        jsr IsLight
        bcc .n
        and #$0100
        beq .n
        lda LCount
        cmp #16
        bcs .d
        asl a
        tax
        lda [ItemPtr]
        sta LX,x
        ldy #2
        lda [ItemPtr],y
        sta LY,x
        ldy #10
        lda [ItemPtr],y
        and #$000F
        sta LR,x
        inc LCount
.n:     lda #16
        jsr NextItem
        dec T4
        bne .l
.d:     rts

; Carry set if the cell DX, DY is lit: within AvLight of the Avatar, or of a lit light within its reach.
Lit:
        lda DX
        sec
        sbc PX
        jsr AbsX
        sta T4
        lda DY
        sec
        sbc PY
        jsr AbsY
        cmp T4
        bcc .a
        sta T4
.a:     lda T4
        cmp AvLight
        beq .yes
        bcc .yes
        ldx #0
.l:     txa
        lsr a
        cmp LCount
        bcs .no
        lda DX
        sec
        sbc LX,x
        jsr AbsX
        sta T4
        lda DY
        sec
        sbc LY,x
        jsr AbsY
        cmp T4
        bcc .b
        sta T4
.b:     lda T4
        cmp LR,x
        beq .yes
        bcc .yes
        inx
        inx
        bra .l
.no:    clc
        rts
.yes:   sec
        rts

; ---- roofs ----

; Roofs over everything, unless the Avatar is under one.
DrawRoofs:
        jsr UnderRoof
        bcc .draw
        rts
.draw:  jsr ViewCorner
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta RN
        beq .d
.l:     ldy #10
        lda [ItemPtr],y
        cmp #$FFFD
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .n
        lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        jsr CellOnScreen
        bcc .n
        ldy #4
        lda [ItemPtr],y
        sta SprId
        lda ItemPtr
        pha
        lda ItemPtr+2
        pha
        jsr DrawSprite
        pla
        sta ItemPtr+2
        pla
        sta ItemPtr
.n:     lda #16
        jsr NextItem
        dec RN
        bne .l
.d:     rts

; Carry set if a roof on this map covers the Avatar's cell (its picture's cells, up and left of where it stands).
UnderRoof:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta RN
        beq .no
.l:     ldy #10
        lda [ItemPtr],y
        cmp #$FFFD
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .n
        ldy #4
        lda [ItemPtr],y
        sta SprId
        lda ItemPtr
        pha
        lda ItemPtr+2
        pha
        jsr SpriteSize          ; T4 width, T5 height
        pla
        sta ItemPtr+2
        pla
        sta ItemPtr
        lda [ItemPtr]           ; right: its x; left: x - (width / 16 - 1)
        cmp PX
        bcc .n
        lsr T4
        lsr T4
        lsr T4
        lsr T4
        lda [ItemPtr]
        sec
        sbc T4
        cmp PX
        bpl .n                  ; left edge - 1 >= PX: not over it
        ldy #2
        lda [ItemPtr],y
        cmp PY
        bcc .n
        lsr T5
        lsr T5
        lsr T5
        lsr T5
        lda [ItemPtr],y
        sec
        sbc T5
        cmp PY
        bpl .n
        sec
        rts
.n:     lda #16
        jsr NextItem
        dec RN
        bne .l
.no:    clc
        rts

DNOn:     .dw 0
DNDawn:   .dw 5
DNDay:    .dw 7
DNDusk:   .dw 19
DNNight:  .dw 21
AvLight:  .dw 0
DX:       .dw 0
DY:       .dw 0
LCount:   .dw 0
LX:       .ds 32
LY:       .ds 32
LR:       .ds 32
RN:       .dw 0
AnimCount: .dw 0
AnimVisible: .dw 0
AnimMark: .ds 1024              ; 1: an animated tile
AnimPtr:  .dd 0
AnimTick: .dw 0
AnimFrame: .dw 0
RemapTab: .ds 2048
