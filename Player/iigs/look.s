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
.md:    lda Src                 ; the keys after them (actkeys.s)
        sta Ptr
        lda Src+2
        sta Ptr+2
        jmp ParseKeys

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
        lda DsHold              ; the view covered by a fade (fx.s) or a scene (scene.s): not now
        ora SceneOn
        beq .go
        rts
.go:    jsr CurOff
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
        jmp LiveOverView        ; what lies over the view drawn again (hudlive.s)

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

; The view's darkness, Ultima 6's (Engine/U6Lighting.cs - Nuvie's original lighting; the Windows Ultima 6 engine draws
; the same): by day none; at night and in a Cave every cell dark but for globes of light added up (at most 4): the
; Avatar's (the hour's level: night 4, dusk and dawn stepping it; a lit light in the party, worn magic light or the Light
; spell: 8) and each lit light on the map's (a candle-sized one 2, brighter 3). A cell's 0-4: black, three quarters, half,
; a quarter dark, lit - Ultima 6's shading as a 2 x 2 dither (ShMask).
DrawDark:
        lda CamOn               ; the camera elsewhere (ext.s): a cutscene - full light (the user, 2026-11-25)
        beq .dl
        rts
.dl:    jsr DarkLevel
        cmp #0
        bne .dark
        rts
.dark:  sta AvLvl
        stz AvLight
        lda LightLeft           ; the Light spell
        beq .c
        lda #1
        jsr MoreLight
.c:     jsr LightsCarried       ; a lit light in the party, worn magic light (AvLight > 0)
        lda AvLight
        beq .nl
        lda AvLvl
        cmp #8
        bcs .nl
        lda #8
        sta AvLvl
.nl:    jsr LightsOnMap
        jsr ViewCorner
        ldx #0                  ; no light yet
.z:     stz Shade,x
        inx
        inx
        cpx #256
        bcc .z
        jsr ViewEye             ; the Avatar's globe (or the one Camera Follows' - Windows LightEye)
        lda EyeX
        sec
        sbc ViewX0
        jsr ShortX
        sta GlX
        lda EyeY
        sec
        sbc ViewY0
        jsr ShortY
        sta GlY
        lda AvLvl
        jsr AddGlobe
        stz ShI                 ; each light's
.lt:    lda ShI
        lsr a
        cmp LCount
        bcs .draw
        ldx ShI
        lda LX,x
        sec
        sbc ViewX0
        jsr ShortX
        sta GlX
        ldx ShI
        lda LY,x
        sec
        sbc ViewY0
        jsr ShortY
        sta GlY
        ldx ShI
        lda LR,x                ; reach 1-2 (a candle): 2, more: 3
        cmp #3
        lda #2
        bcc .lv
        inc a
.lv:    jsr AddGlobe
        inc ShI
        inc ShI
        bra .lt
.draw:  stz Row
        stz ShRow
.row:   stz Col
.col:   lda ShRow
        clc
        adc Col
        tay
        lda Shade,y
        and #$00FF
        cmp #4
        bcc .fb4
        brl .next
.fb4:
        asl a                   ; its masks: even lines, odd lines
        asl a
        tay
        lda ShMask,y
        sta ShEven
        lda ShMask+2,y
        sta ShOdd
        lda Row                 ; the cell: 16 lines of 8 bytes
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
        lda #8
        sta T5
.blk:   lda >VIEWBUF,x
        and ShEven
        sta >VIEWBUF,x
        lda >VIEWBUF+2,x
        and ShEven
        sta >VIEWBUF+2,x
        lda >VIEWBUF+4,x
        and ShEven
        sta >VIEWBUF+4,x
        lda >VIEWBUF+6,x
        and ShEven
        sta >VIEWBUF+6,x
        lda >VIEWBUF+160,x
        and ShOdd
        sta >VIEWBUF+160,x
        lda >VIEWBUF+162,x
        and ShOdd
        sta >VIEWBUF+162,x
        lda >VIEWBUF+164,x
        and ShOdd
        sta >VIEWBUF+164,x
        lda >VIEWBUF+166,x
        and ShOdd
        sta >VIEWBUF+166,x
        txa
        clc
        adc #320
        tax
        dec T5
        bne .blk
.next:  inc Col
        lda Col
        cmp VCols
        bcs .fb1
        brl .col
.fb1:
        lda ShRow
        clc
        adc VCols
        sta ShRow
        inc Row
        lda Row
        cmp VRows
        bcs .fb2
        brl .row
.fb2:
        rts

; A globe of level A at the view's cell GlX, GlY (Nuvie's TileGlobe, GlobeData): added into Shade (each cell at most 4).
AddGlobe:
        cmp #1
        bcs .a
        rts
.a:     cmp #11
        bcc .b
        lda #10
.b:     sta GlL
        dec a
        asl a
        tax
        lda GlobeOff,x
        sta GlP
        lda GlL                 ; half-width: level - 1, at most 5
        cmp #6
        bcc .r
        lda #6
.r:     dec a
        sta GlR
        asl a
        inc a
        sta GlS
        stz GlJ
.j:     lda GlJ
        cmp GlS
        bcc .j1
        rts
.j1:    lda GlY                 ; its row in the view
        clc
        adc GlJ
        sec
        sbc GlR
        bmi .nj
        cmp VRows
        bcs .nj
        sta T5                  ; x VCols
        stz GlRow
.m:     lda T5
        beq .md
        lda GlRow
        clc
        adc VCols
        sta GlRow
        dec T5
        bra .m
.md:    stz GlI
.i:     lda GlI
        cmp GlS
        bcs .nj
        lda GlX
        clc
        adc GlI
        sec
        sbc GlR
        bmi .ni
        cmp VCols
        bcs .ni
        clc
        adc GlRow
        sta GlC
        lda GlP
        clc
        adc GlI
        tax
        lda GlobeData,x
        and #$00FF
        beq .ni
        clc
        ldx GlC
        sep #$20
        .a8
        adc Shade,x
        cmp #5
        bcc .s
        lda #4
.s:     sta Shade,x
        rep #$20
        .a16
.ni:    inc GlI
        bra .i
.nj:    lda GlP
        clc
        adc GlS
        sta GlP
        inc GlJ
        brl .j

; A = the Avatar's globe for now: 0 by day (no darkness); night 4; from dusk's start to night's 10 down to 5 in six
; steps, from dawn's start to day's 5 up to 10; a Cave always 4; Change Day/Night's for the rest of its hour.
DarkLevel:
        lda WCave               ; a Cave: dark, day or night (weather.s keeps WCave)
        beq .nc
        lda #4
        rts
.nc:    lda DNOvH               ; Change Day/Night (ext.s): till the hour is over
        cmp Hour
        bne .no
        lda DNOvL
        rts
.no:    lda #$FFFF
        sta DNOvH
        lda DNOn
        bne .on
        lda #0
        rts
.on:    lda Hour                ; now, in minutes
        jsr Min60
        clc
        adc Minute
        sta DkNow
        lda DNDawn
        jsr Min60
        sta DkDawn
        lda DNDay
        jsr Min60
        cmp DkDawn
        beq .d1
        bcs .d2
.d1:    lda DkDawn
        inc a
.d2:    sta DkDay
        lda DNDusk
        jsr Min60
        sta DkDusk
        lda DNNight
        jsr Min60
        cmp DkDusk
        beq .n1
        bcs .n2
.n1:    lda DkDusk
        inc a
.n2:    sta DkNight
        lda DkNow               ; day
        cmp DkDay
        bcc .ndy
        cmp DkDusk
        bcs .ndy
        lda #0
        rts
.ndy:   cmp DkDusk              ; dusk: 10 - step
        bcc .ndu
        cmp DkNight
        bcs .ndu
        sec
        sbc DkDusk
        ldx DkNight
        ldy DkDusk
        jsr DkStep
        eor #$FFFF
        sec
        adc #10
        rts
.ndu:   cmp DkDawn              ; dawn: 5 + step
        bcc .night
        cmp DkDay
        bcs .night
        sec
        sbc DkDawn
        ldx DkDay
        ldy DkDawn
        jsr DkStep
        clc
        adc #5
        rts
.night: lda #4
        rts

; A = minutes into a span from Y to X: the step 0-5 (A x 6 / the span's length, at most 5).
DkStep:
        sta T4
        asl a                   ; x 6
        clc
        adc T4
        asl a
        sta T4
        stx T5
        tya
        eor #$FFFF
        sec
        adc T5
        sta T5                  ; the span's length
        ldx #0
.l:     lda T4
        cmp T5
        bcc .d
        sbc T5
        sta T4
        inx
        cpx #5
        bcc .l
.d:     txa
        rts

; A x 60
Min60:
        asl a
        asl a
        sta T4                  ; x 4
        asl a
        asl a
        asl a
        asl a                   ; x 64
        sec
        sbc T4
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
AvLvl:    .dw 0
DNOvH:    .dw $FFFF             ; Change Day/Night: its hour ($FFFF none) and level (ext.s)
DNOvL:    .dw 0
DkNow:    .dw 0
DkDawn:   .dw 0
DkDay:    .dw 0
DkDusk:   .dw 0
DkNight:  .dw 0
Shade:    .ds 256               ; each view cell's light 0-4 (DrawDark)
ShI:      .dw 0
ShRow:    .dw 0
ShEven:   .dw 0
ShOdd:    .dw 0
ShMask:   .dw $0000,$0000,$0000,$F0F0,$0F0F,$F0F0,$0F0F,$FFFF ; light 0-3: even lines, odd lines (a 2 x 2 dither)
GlX:      .dw 0
GlY:      .dw 0
GlL:      .dw 0
GlP:      .dw 0
GlR:      .dw 0
GlS:      .dw 0
GlJ:      .dw 0
GlI:      .dw 0
GlRow:    .dw 0
GlC:      .dw 0
GlobeOff: .dw 0,1,10,35,84,165,286,407,528,649
GlobeData:
          .db 1,1,1,1,1,2,1,1,1,1,0,1,1,1,0,1,2,2,2,1,1,2,3,2,1,1,2,2,2,1,0,1,1,1,0,0,0,1,1,1
          .db 0,0,0,1,2,2,2,1,0,1,2,3,3,3,2,1,1,2,3,4,3,2,1,1,2,3,3,3,2,1,0,1,2,2,2,1,0,0,0,1
          .db 1,1,0,0,0,0,0,1,1,1,0,0,0,0,1,1,2,2,2,1,1,0,0,1,2,3,3,3,2,1,0,1,2,3,4,4,4,3,2,1
          .db 1,2,3,4,4,4,3,2,1,1,2,3,4,4,4,3,2,1,0,1,2,3,3,3,2,1,0,0,1,1,2,2,2,1,1,0,0,0,0,1
          .db 1,1,0,0,0,0,0,0,0,1,1,1,0,0,0,0,0,0,1,1,2,2,2,1,1,0,0,0,1,2,2,3,3,3,2,2,1,0,0,1
          .db 2,3,4,4,4,3,2,1,0,1,2,3,4,4,4,4,4,3,2,1,1,2,3,4,4,4,4,4,3,2,1,1,2,3,4,4,4,4,4,3
          .db 2,1,0,1,2,3,4,4,4,3,2,1,0,0,1,2,2,3,3,3,2,2,1,0,0,0,1,1,2,2,2,1,1,0,0,0,0,0,0,1
          .db 1,1,0,0,0,0,0,0,1,1,2,2,2,1,1,0,0,0,1,2,2,3,3,3,2,2,1,0,1,2,3,3,4,4,4,3,3,2,1,1
          .db 2,3,4,4,4,4,4,3,2,1,2,3,4,4,4,4,4,4,4,3,2,2,3,4,4,4,4,4,4,4,3,2,2,3,4,4,4,4,4,4
          .db 4,3,2,1,2,3,4,4,4,4,4,3,2,1,1,2,3,3,4,4,4,3,3,2,1,0,1,2,2,3,3,3,2,2,1,0,0,0,1,1
          .db 2,2,2,1,1,0,0,2,2,2,2,3,3,3,2,2,2,2,2,2,3,3,4,4,4,3,3,2,2,2,3,4,4,4,4,4,4,4,3,2
          .db 2,3,4,4,4,4,4,4,4,3,2,3,4,4,4,4,4,4,4,4,4,3,3,4,4,4,4,4,4,4,4,4,3,3,4,4,4,4,4,4
          .db 4,4,4,3,2,3,4,4,4,4,4,4,4,3,2,2,3,4,4,4,4,4,4,4,3,2,2,2,3,3,4,4,4,3,3,2,2,2,2,2
          .db 2,3,3,3,2,2,2,2,3,3,3,3,4,4,4,3,3,3,3,3,3,4,4,4,4,4,4,4,3,3,3,4,4,4,4,4,4,4,4,4
          .db 3,3,4,4,4,4,4,4,4,4,4,3,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4
          .db 4,4,4,4,4,3,4,4,4,4,4,4,4,4,4,3,3,4,4,4,4,4,4,4,4,4,3,3,3,4,4,4,4,4,4,4,3,3,3,3
          .db 3,3,4,4,4,3,3,3,3,3,3,3,4,4,4,4,4,3,3,3,3,4,4,4,4,4,4,4,4,4,3,3,4,4,4,4,4,4,4,4
          .db 4,3,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4
          .db 4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,4,3,4,4,4,4,4,4,4,4,4,3,3,4,4,4,4,4,4,4,4,4,3,3
          .db 3,3,4,4,4,4,4,3,3,3
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
