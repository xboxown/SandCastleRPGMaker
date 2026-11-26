; Sandcastle RPG Maker - Apple IIgs player: path finding (list item 36), as the Windows Ultima 6 engine's FindPath (Exult's
; A* on map cells, 4 ways; a path to the reachable cell nearest the goal when the goal can't be had - a leg of a long walk).
; Here: a breadth-first search (the same shortest ways, steps all cost one) over a 64 x 64 window round the walker and its
; goal, only when stepping straight at the goal doesn't work. The way found is kept (8 walkers at a time, 64 steps each) and
; followed step by step - searched again when something new is in the way, or once its end is reached. What stops a way:
; ground the walker can't be on (land, sea, air - moves.s GroundFor) and solid things; characters don't (they step aside or
; swap, as when walking). Its work memory (12 KB) comes from the Memory Manager the first time (SvPtr points at it while
; it works - SvPtr is only a saved game's while one is read).

PA_W = 64                       ; the window, cells across and down
PA_D = 0                        ; the work memory: each cell's way in (0 not reached, 1 +x, 2 -x, 3 +y, 4 -y, 5 the start, 6 shut)
PA_Q = 4096                     ; the cells to look at (words); after the search, the way back
PA_S = 12288                    ; the ways kept: 8 x (CP 4, goal x y 4, length, at 4, the steps 64)
PA_SLOT = 76
PA_SIZE = PA_S+8*PA_SLOT

; SvPtr = the work memory (made the first time). Carry set: none to be had. A new map: the ways kept forgotten.
PaMem:
        lda PaHandle
        ora PaHandle+2
        bne .have
        pha
        pha
        pea 0
        pea PA_SIZE
        lda UserId
        pha
        pea $C010               ; locked, fixed, not across a bank
        pea 0
        pea 0
        tool $0902              ; NewHandle
        pla
        sta PaHandle
        pla
        sta PaHandle+2
        bcc .got
        stz PaHandle
        stz PaHandle+2
        sec
        rts
.got:   lda PaHandle
        sta SvPtr
        lda PaHandle+2
        sta SvPtr+2
        ldy #2
        lda [SvPtr],y
        sta PaAddr+2
        lda [SvPtr]
        sta PaAddr
        lda #$FFFF
        sta PaMap
.have:  lda PaAddr
        sta SvPtr
        lda PaAddr+2
        sta SvPtr+2
        lda CurMap
        cmp PaMap
        beq .ok
        sta PaMap
        ldy #PA_S               ; another map: no ways kept
.z:     lda #0
        sta [SvPtr],y
        tya
        clc
        adc #PA_SLOT
        tay
        cpy #PA_S+8*PA_SLOT
        bcc .z
.ok:    clc
        rts

; Y = the way kept for CP toward (about) GX, GY - carry set. (Its first word is CP's address; 0: free.)
PaSlot:
        ldy #PA_S
.l:     lda [SvPtr],y
        cmp CP
        bne .n
        iny
        iny
        lda [SvPtr],y
        dey
        dey
        cmp CP+2
        bne .n
        sty PaY
        iny                     ; the goal within 3 of the one it was found for (a pet's Avatar moves)
        iny
        iny
        iny
        lda [SvPtr],y
        sec
        sbc GX
        jsr AbsX
        sta PaT
        iny
        iny
        lda [SvPtr],y
        sec
        sbc GY
        jsr AbsY
        clc
        adc PaT
        ldy PaY
        cmp #4
        bcs .n
        sec
        rts
.n:     tya
        clc
        adc #PA_SLOT
        tay
        cpy #PA_S+8*PA_SLOT
        bcc .l
        clc
        rts

; CP along its way kept toward GX, GY, if it has one: carry set if it stepped. A step that can't be made: the way forgotten.
PathFollow:
        jsr PaMem
        bcc .m
        clc
        rts
.m:     jsr PaSlot
        bcs .have
        rts
.have:  sty PaY
        tya
        clc
        adc #10
        tay
        lda [SvPtr],y           ; at
        dey
        dey
        cmp [SvPtr],y           ; length
        bcs .forget
        sta PaAt
        lda PaY                 ; its step: slot + 12 + at
        clc
        adc #12
        clc
        adc PaAt
        tay
        lda [SvPtr],y
        and #$00FF
        jsr PaDir
        jsr TryCharStep
        bcc .forget0
        jsr PaMem               ; (SvPtr again)
        lda PaY
        clc
        adc #10
        tay
        lda PaAt
        inc a
        sta [SvPtr],y
        sec
        rts
.forget0:
        jsr PaMem
.forget:
        ldy PaY
        lda #0
        sta [SvPtr],y
        iny
        iny
        sta [SvPtr],y
        clc
        rts

; SDX, SDY = way A (1 +x, 2 -x, 3 +y, 4 -y).
PaDir:
        stz SDX
        stz SDY
        cmp #1
        bne .d2
        lda #1
        sta SDX
        rts
.d2:     cmp #2
        bne .d3
        lda #$FFFF
        sta SDX
        rts
.d3:     cmp #3
        bne .d4
        lda #1
        sta SDY
        rts
.d4:     lda #$FFFF
        sta SDY
        rts

; Stepping straight at GX, GY didn't work: a way round searched, kept, and its first step taken. Carry set if it stepped.
PathStep:
        jsr PaMem
        bcc .m
        clc                     ; (no memory: nothing moved)
        rts
.m:     jsr PaSearch
        bcs .way
        clc
        rts
.way:   jmp PathFollow

; The search from CP to GX, GY; the way (to the goal, else to the reachable cell nearest it) kept in a slot. Carry set: a way
; of at least one step.
PaSearch:
        lda [CP]
        sta PaSX
        ldy #2
        lda [CP],y
        sta PaSY
        lda GX                  ; the goal from here (the short way round a map that wraps around)
        sec
        sbc PaSX
        jsr ShortX
        sta PaDX
        lda GY
        sec
        sbc PaSY
        jsr ShortY
        sta PaDY
        lda PaDX                ; the window: the start at 32 - half the way (at most 30 off the middle)
        jsr PaHalf
        sta PaT
        lda #32
        sec
        sbc PaT
        sta PaWSX
        lda PaDY
        jsr PaHalf
        sta PaT
        lda #32
        sec
        sbc PaT
        sta PaWSY
        lda #1                  ; the goal in the window (kept inside it; then not the goal itself - PaReal 0)
        sta PaReal
        lda PaWSX
        clc
        adc PaDX
        sta PaT
        jsr PaClamp
        sta PaWGX
        cmp PaT
        beq .gx
        stz PaReal
.gx:    lda PaWSY
        clc
        adc PaDY
        sta PaT
        jsr PaClamp
        sta PaWGY
        cmp PaT
        beq .gy
        stz PaReal
.gy:
        lda PaSX                ; the window's first column and row on the map
        sec
        sbc PaWSX
        sta PaOX
        lda PaSY
        sec
        sbc PaWSY
        sta PaOY
        ldy #PA_D               ; nothing reached yet
        lda #0
.z:     sta [SvPtr],y
        iny
        iny
        cpy #PA_D+PA_W*PA_W
        bcc .z
        lda PaWSY               ; the start
        xba
        lsr a
        lsr a                   ; (y * 64)
        clc
        adc PaWSX
        sta PaBest
        tay
        sep #$20
        .a8
        lda #5
        sta [SvPtr],y
        rep #$20
        .a16
        ldy #PA_Q
        lda PaBest
        sta [SvPtr],y
        stz PaHead
        lda #1
        sta PaTail
        lda #$7FFF
        sta PaBestH
        lda #PA_W*PA_W
        sta PaLeft
.loop:  lda PaHead
        cmp PaTail
        bcc .more
        brl .done
.more:  dec PaLeft
        bne .go
        brl .done
.go:    asl a
        clc
        adc #PA_Q
        tay
        lda [SvPtr],y
        sta PaCur
        inc PaHead
        and #PA_W-1
        sta PaCX
        lda PaCur
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        sta PaCY
        lda PaWGX               ; nearer the goal than any so far?
        sec
        sbc PaCX
        jsr AbsX
        sta PaT
        lda PaWGY
        sec
        sbc PaCY
        jsr AbsY
        clc
        adc PaT
        cmp PaBestH
        bcs .nb
        sta PaBestH
        lda PaCur
        sta PaBest
        lda PaBestH
        bne .nb
        brl .done               ; the goal
.nb:    lda #1                  ; its 4 neighbours
        sta PaWay
.nbr:   lda PaWay
        cmp #5
        bcc .try
        brl .loop
.try:   lda PaCX
        sta PaNX
        lda PaCY
        sta PaNY
        lda PaWay
        cmp #1
        bne .w2
        inc PaNX
        bra .wd
.w2:    cmp #2
        bne .w3
        dec PaNX
        bra .wd
.w3:    cmp #3
        bne .w4
        inc PaNY
        bra .wd
.w4:    dec PaNY
.wd:    lda PaNX
        cmp #PA_W
        bcs .next               ; (below 0: big)
        lda PaNY
        cmp #PA_W
        bcs .next
        xba
        lsr a
        lsr a
        clc
        adc PaNX
        sta PaNI
        tay
        lda [SvPtr],y
        and #$00FF
        bne .next               ; reached already, or shut
        lda PaReal              ; the goal itself: open (someone may stand there)
        beq .chk
        lda PaNX
        cmp PaWGX
        bne .chk
        lda PaNY
        cmp PaWGY
        beq .open
.chk:   lda PaOX
        clc
        adc PaNX
        tax
        lda PaOY
        clc
        adc PaNY
        tay
        jsr PaOpen
        php
        jsr PaMem
        plp
        bcs .open
        ldy PaNI
        sep #$20
        .a8
        lda #6
        sta [SvPtr],y
        rep #$20
        .a16
        bra .next
.open:  ldy PaNI
        sep #$20
        .a8
        lda PaWay
        sta [SvPtr],y
        rep #$20
        .a16
        lda PaTail
        cmp #PA_W*PA_W
        bcs .next
        asl a
        clc
        adc #PA_Q
        tay
        lda PaNI
        sta [SvPtr],y
        inc PaTail
.next:  inc PaWay
        brl .nbr
.done:  lda PaBestH             ; nowhere nearer than the start: no way
        cmp #$7FFF
        bne .back
        clc
        rts
.back:  stz PaLen               ; the way back from the best cell to the start, into the cells' list
        lda PaBest
        sta PaCur
.b:     ldy PaCur
        lda [SvPtr],y
        and #$00FF
        cmp #5
        beq .rev
        sta PaWay
        lda PaLen
        clc
        adc #PA_Q
        tay
        sep #$20
        .a8
        lda PaWay
        sta [SvPtr],y
        rep #$20
        .a16
        inc PaLen
        lda PaWay               ; to the cell it came from
        cmp #1
        bne .b2
        dec PaCur
        bra .b
.b2:    cmp #2
        bne .b3
        inc PaCur
        bra .b
.b3:    cmp #3
        bne .b4
        lda PaCur
        sec
        sbc #PA_W
        sta PaCur
        bra .b
.b4:    lda PaCur
        clc
        adc #PA_W
        sta PaCur
        bra .b
.rev:   lda PaLen
        bne .some
        clc
        rts
.some:  jsr PaSlotFor           ; Y = CP's slot (its own, else the next)
        sty PaY
        lda CP
        sta [SvPtr],y
        iny
        iny
        lda CP+2
        sta [SvPtr],y
        iny
        iny
        lda GX
        sta [SvPtr],y
        iny
        iny
        lda GY
        sta [SvPtr],y
        iny
        iny
        lda PaLen
        cmp #65
        bcc .l
        lda #64
.l:     sta [SvPtr],y           ; length (the first 64 steps)
        sta PaN
        iny
        iny
        lda #0
        sta [SvPtr],y           ; at
        stz PaK                 ; step k = the way back's (length - 1 - k)
.c:     lda PaK
        cmp PaN
        bcs .cd
        lda PaLen
        dec a
        sec
        sbc PaK
        clc
        adc #PA_Q
        tay
        lda [SvPtr],y
        and #$00FF
        sta PaWay
        lda PaY
        clc
        adc #12
        clc
        adc PaK
        tay
        sep #$20
        .a8
        lda PaWay
        sta [SvPtr],y
        rep #$20
        .a16
        inc PaK
        bra .c
.cd:    sec
        rts

; Y = CP's slot if it has one, else the next in turn.
PaSlotFor:
        ldy #PA_S
.l:     lda [SvPtr],y
        cmp CP
        bne .n
        iny
        iny
        lda [SvPtr],y
        dey
        dey
        cmp CP+2
        beq .o
.n:     tya
        clc
        adc #PA_SLOT
        tay
        cpy #PA_S+8*PA_SLOT
        bcc .l
        lda PaNext
        inc a
        and #7
        sta PaNext
        sta MulA
        lda #PA_SLOT
        sta MB0
        jsr Mul16
        lda MulR
        clc
        adc #PA_S
        tay
.o:     rts

; X, Y = a map cell: carry set if CP can be there by the ground and nothing solid is on it.
PaOpen:
        stx PaMX
        sty PaMY
        jsr MapTile
        cmp #$FFFF
        beq .no
        jsr GroundFor
        bcc .no
        ldx PaMX
        ldy PaMY
        jsr OccAt
        and OccMask
        and #1
        bne .no
        sec
        rts
.no:    clc
        rts

; A (signed) halved, at most 30 either way.
PaHalf:
        cmp #$8000
        bcc .p
        eor #$FFFF
        inc a
        lsr a
        cmp #31
        bcc .n
        lda #30
.n:     eor #$FFFF
        inc a
        rts
.p:     lsr a
        cmp #31
        bcc .o
        lda #30
.o:     rts

; A (signed) kept within the window: 0-63.
PaClamp:
        cmp #$8000
        bcc .p
        lda #0
        rts
.p:     cmp #PA_W
        bcc .o
        lda #PA_W-1
.o:     rts

PaHandle: .dd 0
PaAddr:   .dd 0
PaMap:    .dw 0
PaNext:   .dw 0
PaY:      .dw 0
PaT:      .dw 0
PaAt:     .dw 0
PaSX:     .dw 0
PaSY:     .dw 0
PaDX:     .dw 0
PaDY:     .dw 0
PaWSX:    .dw 0
PaWSY:    .dw 0
PaWGX:    .dw 0
PaWGY:    .dw 0
PaOX:     .dw 0
PaOY:     .dw 0
PaBest:   .dw 0
PaBestH:  .dw 0
PaHead:   .dw 0
PaTail:   .dw 0
PaLeft:   .dw 0
PaCur:    .dw 0
PaCX:     .dw 0
PaCY:     .dw 0
PaNX:     .dw 0
PaNY:     .dw 0
PaNI:     .dw 0
PaWay:    .dw 0
PaLen:    .dw 0
PaN:      .dw 0
PaK:      .dw 0
PaMX:     .dw 0
PaMY:     .dw 0
PaReal:   .dw 0
