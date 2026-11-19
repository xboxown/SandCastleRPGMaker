; Sandcastle RPG Maker - Apple IIgs player: the weather, as the Windows Ultima 6 engine (U6GameWindow.Weather.cs,
; Exult's weather eggs): each map's own (Edit > Map Weather) when the Avatar arrives; rain slanting with the wind, snow
; drifting, hail, storms (heavier rain, lightning flashing the screen - the palette white a moment - and thunder after
; it); cloud shadows drifting with the wind; the overcast greying the colours (Day & Night's overcast strength); the rain
; sound looping on generator 8. Not drawn under a roof; in a Cave nothing at all, not even heard. Each hour it may turn
; (clear, or the map's own at a new strength). Change Weather (talk op 36). Drawn into the view before it is shown.

W_CLEAR = 0
W_RAIN  = 1
W_SNOW  = 2
W_HAIL  = 3
W_STORM = 4
GEN_RAIN = 8
WMAXP   = 64
WMAXC   = 8

ParseWeather:
        lda [Ptr]
        sta WCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta WTab
        lda Ptr+2
        sta WTab+2
        lda WCount
        asl a
        asl a
        sta WT
        asl a
        asl a
        clc
        adc WT                  ; 20 bytes each
        jsr AddPtr
        lda [Ptr]
        sta RainCol
        ldy #2
        lda [Ptr],y
        sta IceCol
        ldy #4
        lda [Ptr],y
        sta ShadeCol
        ldy #6
        lda [Ptr],y
        sta Over16
        lda #8
        jsr AddPtr
        jmp ParseVehicles       ; (vehicles.s)

; ---- the map's weather, on arrival ----
WeatherStart:
        lda CurMap
        sta WMap
        lda #W_CLEAR
        sta WKind
        stz WClouds
        stz WChanges
        lda #$FFFF
        sta WRainS
        sta WThunS
        lda CurMap
        cmp WCount
        bcs .b
        asl a                   ; x 20
        asl a
        sta WT
        asl a
        asl a
        clc
        adc WT
        tay
        ldx #0
        lda Src
        pha
        lda Src+2
        pha
        lda WTab
        sta Src
        lda WTab+2
        sta Src+2
.c:     lda [Src],y
        sta WKind,x
        iny
        iny
        inx
        inx
        cpx #20
        bcc .c
        pla
        sta Src+2
        pla
        sta Src
        lda WKind
        sta WBase
.b:     jsr InCave              ; (arriving in a Cave: not even the rain heard)
        sta WCave
        jmp WeatherBuild

; The drops and clouds for the weather now; the lightning's time; the overcast; the rain sound.
WeatherBuild:
        stz WDrops
        stz WCN
        lda WKind               ; how many drops (a quarter of Windows', the view being smaller)
        bne .some
        brl .clouds
.some:
        ldx #30
        ldy #140
        cmp #W_STORM
        bne .r
        ldx #80
        ldy #200
.r:     cmp #W_SNOW
        bne .h
        ldx #25
        ldy #110
.h:     cmp #W_HAIL
        bne .n
        ldx #20
        ldy #90
.n:     stx WT
        sty ExB
        lda WInt
        jsr SMul
        sta ExA
        lda #100
        sta ExB
        lda ExA
        jsr SDiv
        lda ExQ
        clc
        adc WT
        lsr a
        lsr a
        cmp #WMAXP
        bcc .p
        lda #WMAXP
.p:     sta WDrops
        ldx #0                  ; each: somewhere in the view, its speed
.d:     cpx WDrops
        bcc .d1
        brl .clouds
.d1:
        stx WI
        jsr ViewWH
        lda WVW
        jsr Random
        ldx WI
        sta WPX,x
        lda WVH
        jsr Random
        ldx WI
        sta WPY,x
        jsr DropSpeed
        lda WI
        inc a
        inc a
        tax
        bra .d
.clouds:
        lda WClouds             ; cloud shadows: their cover / 8 (1-14), + 3 in rain or a storm - half of that here
        beq .nc
        lda WCover
        lsr a
        lsr a
        lsr a
        bne .c1
        inc a
.c1:    cmp #15
        bcc .c2
        lda #14
.c2:    bra .cs
.nc:    lda #0
.cs:    ldx WKind
        cpx #W_RAIN
        beq .c3
        cpx #W_STORM
        bne .c4
.c3:    clc
        adc #3
.c4:    inc a
        lsr a
        cmp #WMAXC+1
        bcc .c5
        lda #WMAXC
.c5:    asl a
        sta WCN
        ldx #0
.cl:    cpx WCN
        bcs .cd
        stx WI
        jsr ViewWH
        lda WVW
        clc
        adc #80
        jsr Random
        sec
        sbc #40
        ldx WI
        sta WCX,x
        lda WVH
        clc
        adc #40
        jsr Random
        sec
        sbc #20
        ldx WI
        sta WCY,x
        inx
        inx
        bra .cl
.cd:    lda #180                ; a storm's first lightning: 3 to 9 seconds
        jsr Random
        clc
        adc #180
        sta WLight
        stz WActive
        lda WKind
        ora WClouds
        beq .g
        lda #1
        sta WActive
.g:     stz WFlash              ; (a flash under way: over)
        lda #$FFFF
        sta WGloomNow           ; the overcast again
        jsr Gloom
        jmp RainSound

; X = a drop's place (x 2): its speed for the kind and wind.
DropSpeed:
        lda WKind
        cmp #W_SNOW
        bne .nsnow
        lda WWindX
        sta WVX,x
        lda #2
        sta WVY,x
        rts
.nsnow: cmp #W_HAIL
        bne .rain
        lda WWindX
        asl a
        sta WVX,x
        lda #3
        jsr Random
        ldx WI
        clc
        adc #8
        sta WVY,x
        rts
.rain:  lda WWindX              ; rain 3 a step sideways in the wind, a storm 6
        asl a
        clc
        adc WWindX
        ldy WKind
        cpy #W_STORM
        bne .rv
        asl a
.rv:    sta WVX,x
        lda #4
        jsr Random
        ldx WI
        clc
        adc #10
        ldy WKind
        cpy #W_STORM
        bne .rs
        clc
        adc #3
.rs:    sta WVY,x
        rts

; WVW, WVH: the view's size in pixels.
ViewWH:
        lda VCols
        asl a
        asl a
        asl a
        asl a
        sta WVW
        lda VRows
        asl a
        asl a
        asl a
        asl a
        sta WVH
        rts

; ---- each pass of the main loop ----
WeatherRun:
        lda Mode
        cmp #12
        bcc .go
        rts
.go:    lda CurMap
        cmp WMap
        beq .same
        jsr WeatherStart        ; a new map: its own weather
.same:  jsr FlashRun
        jsr ThunderRun
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta WNow
        pla
        lda WNow
        sec
        sbc WTick
        cmp #8                  ; 7.5 times a second
        bcs .t
        rts
.t:     sta WEl                 ; the ticks since the last time
        lda WNow
        sta WTick
        jsr InCave
        sta WCave
        cmp WCaveWas            ; into or out of a Cave: the view again (its darkness)
        beq .cv
        sta WCaveWas
        lda Mode
        bne .cv
        jsr DrawView
.cv:    jsr Gloom               ; (a Cave: none)
        jsr RainSound
        lda WActive
        bne .a
        rts
.a:     jsr ViewWH
        jsr MoveDrops
        jsr MoveClouds
        lda WKind               ; a storm: lightning now and then
        cmp #W_STORM
        bne .draw
        lda WLight              ; (counted in ticks, as Windows counts its milliseconds)
        sec
        sbc WEl
        sta WLight
        bpl .draw
        jsr Lightning
.draw:  lda Mode                ; the view again, with the weather (walking only: nothing else is over it then)
        bne .o
        lda WCave
        bne .o
        lda WIndoor
        beq .rd
        jsr UnderRoof           ; under a roof: drawn once more after leaving it
        bcs .o
.rd:    jmp DrawView
.o:     rts

MoveDrops:
        ldx #0
.l:     cpx WDrops
        bcs .o
        lda WPX,x
        clc
        adc WVX,x
        sta WPX,x
        lda WKind               ; snow sways
        cmp #W_SNOW
        bne .ns
        stx WI
        lda #3
        jsr Random
        ldx WI
        dec a
        clc
        adc WPX,x
        sta WPX,x
.ns:    lda WPY,x
        clc
        adc WVY,x
        sta WPY,x
        cmp WVH
        bcc .x                  ; (above the top: negative - unsigned, large; below 32768 fine)
        bmi .x
        sec                     ; off the bottom: back at the top, somewhere else
        sbc WVH
        sec
        sbc #6
        sta WPY,x
        stx WI
        lda WVW
        jsr Random
        ldx WI
        sta WPX,x
.x:     lda WPX,x               ; round the sides
        bmi .neg
        cmp WVW
        bcc .n
        sec
        sbc WVW
        sta WPX,x
        bra .n
.neg:   clc
        adc WVW
        sta WPX,x
.n:     inx
        inx
        bra .l
.o:     rts

MoveClouds:
        lda WWindX              ; no wind: east, slowly
        ora WWindY
        bne .w
        lda #1
        sta WDX
        stz WDY
        bra .g
.w:     lda WWindX
        sta WDX
        lda WWindY
        sta WDY
.g:     ldx #0
.l:     cpx WCN
        bcs .o
        stx WI
        lda WCX,x
        clc
        adc WDX
        sta WCX,x
        jsr SignedVsW           ; past the right: back on the left
        ldx WI
        lda WCY,x
        clc
        adc WDY
        sta WCY,x
        jsr SignedVsH
        ldx WI
        inx
        inx
        bra .l
.o:     rts
SignedVsW:
        lda WCX,x
        bmi .left
        sec
        sbc WVW
        bmi .o
        cmp #20
        bcc .o
        lda #40                 ; off the right
        jsr Random
        clc
        adc #84
        eor #$FFFF
        inc a
        ldx WI
        sta WCX,x
        rts
.left:  cmp #-84
        bcs .o
        lda #40
        jsr Random
        clc
        adc WVW
        ldx WI
        sta WCX,x
.o:     rts
SignedVsH:
        lda WCY,x
        bmi .up
        sec
        sbc WVH
        bmi .o
        cmp #16
        bcc .o
        lda #20
        jsr Random
        clc
        adc #48
        eor #$FFFF
        inc a
        ldx WI
        sta WCY,x
        rts
.up:    cmp #-48
        bcs .o
        lda #20
        jsr Random
        clc
        adc WVH
        ldx WI
        sta WCY,x
.o:     rts

; ---- drawn into the view (DrawView, before it is shown) ----
DrawWeather:
        lda WActive
        bne .a
        rts
.a:     stz WIndoor
        lda WCave
        bne .in
        lda ItemPtr
        pha
        lda ItemPtr+2
        pha
        jsr UnderRoof
        pla
        sta ItemPtr+2
        pla
        sta ItemPtr
        bcc .out
.in:    lda #1
        sta WIndoor
        rts
.out:   jsr ViewWH
        jsr DrawClouds
        ldx #0                  ; the drops
.l:     cpx WDrops
        bcs .o
        stx WI
        lda WKind
        cmp #W_RAIN
        beq .streak
        cmp #W_STORM
        beq .streak
        lda IceCol              ; snow a dot, hail two
        sta WCol
        lda WPX,x
        sta WX
        lda WPY,x
        sta WY
        jsr Plot
        ldx WI
        lda WKind
        cmp #W_HAIL
        bne .n
        inc WX
        jsr Plot
        bra .n
.streak:
        lda RainCol             ; rain: a streak of 3, slanted with the wind
        sta WCol
        lda WPX,x
        sta WX
        lda WPY,x
        sta WY
        lda WVX,x
        beq .s0
        bmi .sm
        lda #1
        bra .s0
.sm:    lda #$FFFF
.s0:    sta WSX
        ldy #3
.sl:    phy
        jsr Plot
        ply
        lda WX
        clc
        adc WSX
        sta WX
        inc WY
        dey
        bne .sl
.n:     ldx WI
        inx
        inx
        bra .l
.o:     rts

; The pixel WX, WY of the view in colour WCol (outside it: nothing).
Plot:
        lda WX
        cmp WVW
        bcs .o                  ; (negative: large)
        lda WY
        cmp WVH
        bcs .o
        asl a                   ; y x 160 + x / 2 + the view's offset
        asl a
        asl a
        asl a
        asl a
        sta WT
        asl a
        asl a
        clc
        adc WT
        clc
        adc ViewOff
        sta WT
        lda WX
        lsr a
        clc
        adc WT
        tax
        lda WCol
        and #$000F
        sta WT
        lda WX
        and #1
        bne .odd
        lda WT                  ; even: the high nibble
        asl a
        asl a
        asl a
        asl a
        sta WT
        sep #$20
        .a8
        lda >VIEWBUF,x
        and #$0F
        ora WT
        sta >VIEWBUF,x
        rep #$20
        .a16
.o:     rts
.odd:   sep #$20
        .a8
        lda >VIEWBUF,x
        and #$F0
        ora WT
        sta >VIEWBUF,x
        rep #$20
        .a16
        rts

; Cloud shadows: ellipses 64 x 32, a quarter of their pixels the darkest colour (a dither), drifting.
DrawClouds:
        lda ShadeCol
        and #$000F
        sta WSh
        asl a
        asl a
        asl a
        asl a
        sta WShH
        ldx #0
.c:     cpx WCN
        bcs .o
        stx WCI
        stz WR
.r:     lda WCI
        tax
        lda WCY,x               ; this row on the screen?
        clc
        adc WR
        bmi .nr
        cmp WVH
        bcs .nr
        sta WY
        ldx WR                  ; its half width (bytes), round the middle
        lda CloudHW,x
        and #$00FF
        sta WT2
        ldx WCI
        lda WCX,x               ; the middle byte: (x + 32) / 2
        clc
        adc #32
        cmp #$8000
        ror a
        sec
        sbc WT2
        sta WB0                 ; first byte
        lda WT2
        asl a
        clc
        adc WB0
        sta WB1                 ; past the last
        jsr ShadeRow
.nr:    inc WR
        lda WR
        cmp #32
        bcc .r
        ldx WCI
        inx
        inx
        bra .c
.o:     rts

; Bytes WB0..WB1 of view row WY: every other row, one pixel of each byte in the shadow (a quarter of them, the
; pixel alternating by the row) - as light as Windows' shadow.
ShadeRow:
        lda WY
        and #1
        beq .row
        rts
.row:   lda WB0
        bpl .a
        lda #0
.a:     sta WB0
        lda WVW
        lsr a
        cmp WB1
        bcs .b
        sta WB1
.b:     lda WB0
        cmp WB1
        bcs .o
        lda WY
        asl a
        asl a
        asl a
        asl a
        asl a
        sta WT
        asl a
        asl a
        clc
        adc WT
        clc
        adc ViewOff
        clc
        adc WB0
        tax
        lda WB1
        sec
        sbc WB0
        tay
        lda WY
        and #2
        bne .lo
        sep #$20
        .a8
.lh:    lda >VIEWBUF,x
        and #$0F
        ora WShH
        sta >VIEWBUF,x
        inx
        dey
        bne .lh
        rep #$20
        .a16
.o:     rts
.lo:    sep #$20
        .a8
.ll:    lda >VIEWBUF,x
        and #$F0
        ora WSh
        sta >VIEWBUF,x
        inx
        dey
        bne .ll
        rep #$20
        .a16
        rts

CloudHW:  .db 4,7,9,10,11,12,13,14,14,15,15,15,16,16,16,16,16,16,16,16,15,15,15,14,14,13,12,11,10,9,7,4

; ---- lightning and thunder ----
Lightning:
        lda #150                ; the next: 4 s + up to 9 s x (1.5 - intensity / 100)
        sec
        sbc WInt
        sta ExB
        lda #540
        jsr Random
        jsr SMul
        sta ExA
        lda #100
        sta ExB
        lda ExA
        jsr SDiv
        lda ExQ
        clc
        adc #240
        sta WLight
        lda WCave
        bne .o
        lda WThunS              ; thunder 0.3 to 1.8 seconds after
        cmp #$FFFF
        beq .f
        lda #90
        jsr Random
        clc
        adc #18
        clc
        adc WNow
        sta WThunAt
        lda #1
        sta WThunOn
.f:     jsr UnderRoof           ; the flash: not under a roof
        bcs .o
        lda #1
        sta WFlash
        lda WNow
        sta WFlashT
        lda #14                 ; nearly white
        jmp FlashTint
.o:     rts

; The flash's steps: white, the colours, paler white, the colours.
FlashRun:
        lda WFlash
        bne .go
        rts
.go:    pha
        pha
        tool $2503
        pla
        sta WT
        pla
        lda WT
        sec
        sbc WFlashT
        ldx WFlash
        cpx #1
        bne .s2
        cmp #3
        bcc .o
        inc WFlash
        bra .gl
.s2:    cpx #2
        bne .s3
        cmp #7
        bcc .o
        inc WFlash
        lda #10
        jmp FlashTint
.s3:    cmp #10
        bcc .o
        stz WFlash
.gl:    lda #$FFFF
        sta WGloomNow
        jmp Gloom2
.o:     rts

ThunderRun:
        lda WThunOn
        beq .o
        pha
        pha
        tool $2503
        pla
        sta WT
        pla
        lda WT
        sec
        sbc WThunAt
        bmi .o
        stz WThunOn
        lda WCave
        bne .o
        lda WThunS
        ldx #255
        jmp PlaySound
.o:     rts

; ---- the colours ----
; The overcast: the weather's (intensity if not clear, + half the cloud cover if clouds) x the overcast strength,
; the colours that much towards a dark grey. None in a Cave.
Gloom:
        lda WFlash
        beq Gloom2
        rts
Gloom2:                         ; (the flash's own steps: whatever the flash)
        lda WCave
        bne .z
        lda WKind
        beq .k
        lda WInt
.k:     sta WT
        lda WClouds
        beq .c
        lda WCover
        lsr a
        clc
        adc WT
        sta WT
.c:     lda WT
        cmp #101
        bcc .m
        lda #100
.m:     sta ExB
        lda Over16
        jsr SMul
        sta ExA
        lda #100
        sta ExB
        lda ExA
        jsr SDiv
        lda ExQ
        bra .s
.z:     lda #0
.s:     cmp WGloomNow
        beq .o
        sta WGloomNow
        sta WG
        lda #2
        sta WGR
        sta WGG
        lda #3
        sta WGB
        jmp Tint
.o:     rts

; The flash: A sixteenths towards white.
FlashTint:
        sta WG
        lda #15
        sta WGR
        sta WGG
        sta WGB
; Palette 0 = the game's colours WG sixteenths towards WGR, WGG, WGB.
Tint:
        ldx #0
        ldy #8
.l:     lda [DataPtr],y
        sta WC
        xba
        and #$000F              ; red
        ldx WGR
        jsr Mix
        xba
        sta WOut
        lda WC
        lsr a
        lsr a
        lsr a
        lsr a
        and #$000F              ; green
        ldx WGG
        jsr Mix
        asl a
        asl a
        asl a
        asl a
        ora WOut
        sta WOut
        lda WC
        and #$000F              ; blue
        ldx WGB
        jsr Mix
        ora WOut
        phx
        tyx
        sta >PALETTE-8,x
        plx
        iny
        iny
        cpy #40
        bcc .l
        rts
; (A x (16 - WG) + X x WG) / 16
Mix:
        sta WM
        stx WM2
        lda #16
        sec
        sbc WG
        sta WM3
        lda #0
        ldx WM3
.a:     beq .b
        clc
        adc WM
        dex
        bra .a
.b:     ldx WG
.c:     beq .d
        clc
        adc WM2
        dex
        bra .c
.d:     lsr a
        lsr a
        lsr a
        lsr a
        rts

; ---- the rain sound (generator 8): rain or a storm with a sound, not in a Cave ----
RainSound:
        lda #$FFFF
        ldx WCave
        bne .w
        ldx WKind
        cpx #W_RAIN
        beq .r
        cpx #W_STORM
        bne .w
.r:     lda WRainS
.w:     cmp WSndNow
        beq .o
        sta WSndNow
        ldx #GEN_RAIN
        jsr StopGen
        lda WSndNow
        cmp #$FFFF
        beq .o
        lda #255
        sta MVol
        lda WSndNow
        ldx #GEN_RAIN
        ldy #1
        jmp GenStart
.o:     rts

; A = 1 if a Cave zone holds the Avatar.
InCave:
        lda ZnCount
        beq .no
        sta WT
        lda Src
        pha
        lda Src+2
        pha
        lda ZnPtr
        sta Src
        lda ZnPtr+2
        sta Src+2
        ldy #0
.l:     lda [Src],y
        cmp CurMap
        bne .n
        phy
        iny
        iny
        lda PX
        sec
        sbc [Src],y
        bcc .np
        iny
        iny
        iny
        iny
        cmp [Src],y
        bcs .np
        dey
        dey
        lda PY
        sec
        sbc [Src],y
        bcc .np
        iny
        iny
        iny
        iny
        cmp [Src],y
        bcs .np
        iny
        iny
        iny
        iny
        lda [Src],y
        beq .np
        ply
        pla
        sta Src+2
        pla
        sta Src
        lda #1
        rts
.np:    ply
.n:     tya
        clc
        adc #14
        tay
        dec WT
        bne .l
        pla
        sta Src+2
        pla
        sta Src
.no:    lda #0
        rts

; ---- by the hour: it may turn (clear, or the map's own at 20 to 100) ----
WeatherHour:
        lda WChanges
        beq .o
        lda #100
        jsr Random
        cmp WChance
        bcs .o
        lda WKind
        bne .clear
        lda WBase
        bne .k
        lda #W_RAIN
.k:     sta WKind
        lda #81
        jsr Random
        clc
        adc #20
        sta WInt
        jmp WeatherBuild
.clear: stz WKind
        jmp WeatherBuild
.o:     rts

; WEATHER kind intensity clouds
OpWeather:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta WKind
        iny
        lda [TalkPtr],y
        and #$00FF
        sta WInt
        iny
        lda [TalkPtr],y
        and #$00FF
        cmp #2
        bcs .k
        sta WClouds
.k:     jsr WeatherBuild
        lda Mode                ; (the talk or event is over the view: it is drawn again when it ends)
        lda #4
        jmp Next

; The title: none, the colours as they are.
WeatherOff:
        ldx #GEN_RAIN
        jsr StopGen
        lda #$FFFF
        sta WSndNow
        sta WMap
        stz WActive
        stz WFlash
        stz WThunOn
        rts

WCount:   .dw 0
WTab:     .dd 0
RainCol:  .dw 15
IceCol:   .dw 15
ShadeCol: .dw 0
Over16:   .dw 4
WMap:     .dw $FFFF
; the weather now (as a map's record: 10 words)
WKind:    .dw 0
WInt:     .dw 50
WClouds:  .dw 0
WCover:   .dw 0
WWindX:   .dw 0
WWindY:   .dw 0
WChanges: .dw 0
WChance:  .dw 0
WRainS:   .dw $FFFF
WThunS:   .dw $FFFF
WBase:    .dw 0
WActive:  .dw 0
WCave:    .dw 0
WCaveWas: .dw 0
WIndoor:  .dw 0
WNow:     .dw 0
WTick:    .dw 0
WEl:      .dw 0
WLight:   .dw 0
WFlash:   .dw 0
WFlashT:  .dw 0
WThunOn:  .dw 0
WThunAt:  .dw 0
WSndNow:  .dw $FFFF
WGloomNow: .dw $FFFF
WDrops:       .dw 0
WCN:      .dw 0
WI:       .dw 0
WCI:      .dw 0
WR:       .dw 0
WT:       .dw 0
WT2:      .dw 0
WX:       .dw 0
WY:       .dw 0
WSX:      .dw 0
WDX:      .dw 0
WDY:      .dw 0
WVW:      .dw 0
WVH:      .dw 0
WB0:      .dw 0
WB1:      .dw 0
WCol:     .dw 0
WSh:      .dw 0
WShH:     .dw 0
WG:       .dw 0
WGR:      .dw 0
WGG:      .dw 0
WGB:      .dw 0
WC:       .dw 0
WOut:     .dw 0
WM:       .dw 0
WM2:      .dw 0
WM3:      .dw 0
WPX:      .ds WMAXP*2
WPY:      .ds WMAXP*2
WVX:      .ds WMAXP*2
WVY:      .ds WMAXP*2
WCX:      .ds WMAXC*2
WCY:      .ds WMAXC*2
