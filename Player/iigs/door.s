; Sandcastle RPG Maker - Apple IIgs player: doors beyond the plain swing, as the Windows Ultima 6 engine
; (U6GameWindow.Doors.cs - Martian Dreams' metal doors): a door with an opening animation (Object Editor > Door > How it
; opens: Animation) plays its frames as it opens and backwards (or its closing frames) as it shuts - who can pass changes
; at once, only the look takes its time; double doors open and shut together (the same door next to it); a door that
; opens by itself does when anyone comes within its cells and shuts once nobody has been near for its time; one set Not
; by hand (a sliding or laser door) isn't opened with Use - a lever, an event or itself does it.
; GAME.DAT (after the temperature): u16 doors, each 12 words: name, opening frames' first sprite, how many, closing
; frames' first, how many (0: the opening ones backwards), flags (1 with neighbours, 2 by itself, 4 not by hand), within
; cells, stays open turns, open sound, close sound, ticks a frame, 0.

ParseDoors:
        lda [Ptr]
        sta DrN
        lda #2
        jsr AddPtr
        lda DrN                 ; 24 bytes each
        asl a
        asl a
        asl a
        sta T4
        asl a
        clc
        adc T4
        sta T4
        ldy #0
.l:     cpy T4
        bcs .d
        lda [Ptr],y
        sta DrTab,y
        iny
        iny
        bra .l
.d:     lda T4
        jsr AddPtr
        jmp ParseJoin           ; (join.s)

; A = a name: carry set, X = its place in DrTab.
DoorKind:
        sta DrNm
        ldx #0
        ldy DrN
.l:     cpy #0
        beq .no
        lda DrTab,x
        cmp DrNm
        beq .yes
        txa
        clc
        adc #24
        tax
        dey
        bra .l
.yes:   sec
        rts
.no:    clc
        rts

; Use on the door at ItemPtr: carry set if it can't be opened by hand ("Not possible." told).
DoorNotByHand:
        ldy #8
        lda [ItemPtr],y
        jsr DoorKind
        bcc .no
        lda DrTab+10,x
        and #4
        beq .no
        lda #9
        jsr Format
        jsr PromptLine
        sec
        rts
.no:    clc
        rts

; The door at ItemPtr has just opened or shut (its flags and looks swapped): its sound, its motion, and (by hand) its
; double door. DrHand: 1 when opened by hand (its neighbours follow).
DoorMoved:
        ldy #8
        lda [ItemPtr],y
        jsr DoorKind
        bcs .k
        rts
.k:     stx DrX
        lda ItemPtr
        sta DrP
        lda ItemPtr+2
        sta DrP+2
        ldy #6                  ; opening (now open) or shutting
        lda [ItemPtr],y
        and #16
        sta DrOpen
        beq .cs
        lda DrTab+18,x
        bra .snd
.cs:    lda DrTab+20,x
.snd:   cmp #$FFFF
        beq .anim
        phx
        pha                     ; heard from where it is (moves.s)
        lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        pla
        jsr PlaySoundAt
        plx
.anim:  lda DrTab+4,x           ; frames to play
        bne .has
        brl .pair
.has:
        ldy #4                  ; its look when done
        lda [ItemPtr],y
        sta DrFinal
        lda DrOpen
        bne .fwd
        lda DrTab+8,x           ; shutting: its closing frames, else the opening ones backwards
        beq .back
        lda DrTab+6,x
        sta DrF
        lda DrTab+8,x
        sta DrC
        lda #1
        sta DrStep
        bra .play
.back:  lda DrTab+2,x
        clc
        adc DrTab+4,x
        dec a
        sta DrF
        lda DrTab+4,x
        sta DrC
        lda #$FFFF
        sta DrStep
        bra .play
.fwd:   lda DrTab+2,x
        sta DrF
        lda DrTab+4,x
        sta DrC
        lda #1
        sta DrStep
.play:  lda DrP
        sta ItemPtr
        lda DrP+2
        sta ItemPtr+2
        ldy #4
        lda DrF
        sta [ItemPtr],y
        jsr DrawView
        ldx DrX
        lda DrTab+22,x
        jsr DoorWait
        lda DrF
        clc
        adc DrStep
        sta DrF
        dec DrC
        bne .play
        lda DrP
        sta ItemPtr
        lda DrP+2
        sta ItemPtr+2
        ldy #4
        lda DrFinal
        sta [ItemPtr],y
.pair:  lda DrHand              ; by hand: its double door (the same door beside it, as it was) follows
        beq .o
        stz DrHand
        ldx DrX
        lda DrTab+10,x
        and #1
        beq .o
        jsr DoorPartner
.o:     lda DrP
        sta ItemPtr
        lda DrP+2
        sta ItemPtr+2
        rts

; A = ticks: waits that long (the frames' pace).
DoorWait:
        sta DrW
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta DrT0
        pla
.w:     pha
        pha
        tool $2503
        pla
        sec
        sbc DrT0
        sta DrT1
        pla
        lda DrT1
        cmp DrW
        bcc .w
        rts

; The door beside DrP (left, right, above, below) of the same name, not yet as DrP is now: it moves too.
DoorPartner:
        lda DrP
        sta ItemPtr
        lda DrP+2
        sta ItemPtr+2
        lda [ItemPtr]
        sta DrPX
        ldy #2
        lda [ItemPtr],y
        sta DrPY
        ldy #8
        lda [ItemPtr],y
        sta DrNm2
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta DrLeft
.l:     lda DrLeft
        beq .o
        ldy #6
        lda [ItemPtr],y
        and #$F000+96+256+4
        sec
        sbc CurBits
        cmp #4                  ; a door on this map, on the ground
        bne .n
        ldy #8
        lda [ItemPtr],y
        cmp DrNm2
        bne .n
        lda [ItemPtr]           ; next to it (one cell away, not itself)
        sec
        sbc DrPX
        jsr Abs
        sta T4
        ldy #2
        lda [ItemPtr],y
        sec
        sbc DrPY
        jsr Abs
        clc
        adc T4
        cmp #1
        bne .n
        ldy #6                  ; not yet as the first is
        lda [ItemPtr],y
        and #16
        cmp DrOpen
        beq .n
        lda [ItemPtr],y         ; (a locked one stays)
        and #8
        bne .n
        jsr SwapDoor            ; (lever.s)
        jmp DoorMoved
.n:     lda #16
        jsr NextItem
        dec DrLeft
        bra .l
.o:     rts

; Each turn: doors that open by themselves - anyone within their cells: open; nobody near for their time: shut.
DoorTurn:
        lda DrN
        bne .go
        rts
.go:    lda ObjPtr
        sta DrQ
        lda ObjPtr+2
        sta DrQ+2
        stz DrI
.l:     lda DrI
        cmp ObjCount
        bcc .one
        rts
.one:   lda DrQ
        sta ItemPtr
        lda DrQ+2
        sta ItemPtr+2
        ldy #6
        lda [ItemPtr],y
        and #$F000+96+256+4+8
        sec
        sbc CurBits
        cmp #4                  ; an unlocked door on this map
        bne .n
        ldy #8
        lda [ItemPtr],y
        jsr DoorKind
        bcc .n
        lda DrTab+10,x
        and #2
        beq .n
        stx DrX
        jsr AnyoneNear
        ldy #6
        lda [ItemPtr],y
        and #16
        bne .isopen
        lda DrNear              ; shut: someone near opens it
        beq .n
        jsr SwapDoor
        jsr DoorMoved
        jsr IdleSet
        bra .n
.isopen:
        lda DrNear              ; open: someone near keeps it so
        beq .idle
        jsr IdleSet
        bra .n
.idle:  jsr IdleTick            ; nobody near: its time runs; then it shuts (nobody on its cell)
        bcc .n
        lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        jsr OccAt
        and #6
        bne .n
        lda [ItemPtr]
        cmp PX
        bne .shut
        ldy #2
        lda [ItemPtr],y
        cmp PY
        beq .n
.shut:  jsr SwapDoor
        jsr DoorMoved
.n:     clc
        lda DrQ
        adc #16
        sta DrQ
        lda DrQ+2
        adc #0
        sta DrQ+2
        inc DrI
        brl .l

; DrNear = 1 if the Avatar or anyone (a member, a character) is within the door's cells (DrTab + DrX).
AnyoneNear:
        stz DrNear
        ldx DrX
        lda DrTab+12,x
        inc a
        sta DrR
        lda [ItemPtr]
        sta DrPX
        ldy #2
        lda [ItemPtr],y
        sta DrPY
        ldx PX
        ldy PY
        jsr .near
        bcs .yes
        ldx DrX
        lda DrPX                ; any cell round it with a character or a member
        sec
        sbc DrTab+12,x
        sta DrCX
.cx:    lda DrPY
        ldx DrX
        sec
        sbc DrTab+12,x
        sta DrCY
.cy:    ldx DrCX
        ldy DrCY
        jsr OccAt
        and #6
        bne .yes
        lda DrCY
        sec
        sbc DrPY
        ldx DrX
        cmp DrTab+12,x
        beq .nx
        inc DrCY
        bra .cy
.nx:    lda DrCX
        sec
        sbc DrPX
        cmp DrTab+12,x
        beq .no
        inc DrCX
        bra .cx
.yes:   lda #1
        sta DrNear
.no:    rts
.near:  txa                     ; X, Y within DrR - 1 of the door: carry set
        sec
        sbc DrPX
        jsr AbsX
        cmp DrR
        bcs .f
        tya
        sec
        sbc DrPY
        jsr AbsY
        cmp DrR
        bcs .f
        sec
        rts
.f:     clc
        rts

; The door's idle turns (DrIdle: object, turns), by its number DrI: set to its stays-open time.
IdleSet:
        jsr IdleFind
        ldx DrX2
        lda DrI
        sta DrIdle,x
        ldx DrX
        lda DrTab+14,x
        ldx DrX2
        sta DrIdle+2,x
        rts
; One idle turn: carry set when its time is up.
IdleTick:
        jsr IdleFind
        ldx DrX2
        lda DrIdle,x
        cmp DrI
        bne .up                 ; (not known: shut now)
        lda DrIdle+2,x
        beq .up
        dec DrIdle+2,x
        clc
        rts
.up:    sec
        rts
IdleFind:
        ldx #0
.l:     cpx #16*4
        bcs .first
        lda DrIdle,x
        cmp DrI
        beq .f
        inx
        inx
        inx
        inx
        bra .l
.first: lda DrRing              ; not there: the next place round
        clc
        adc #4
        and #$003F
        sta DrRing
        tax
.f:     stx DrX2
        rts

DrN:      .dw 0
DrTab:    .ds 24*24
DrNm:     .dw 0
DrNm2:    .dw 0
DrX:      .dw 0
DrX2:     .dw 0
DrP:      .dd 0
DrQ:      .dd 0
DrOpen:   .dw 0
DrHand:   .dw 0
DrFinal:  .dw 0
DrF:      .dw 0
DrC:      .dw 0
DrStep:   .dw 0
DrW:      .dw 0
DrT0:     .dw 0
DrT1:     .dw 0
DrPX:     .dw 0
DrPY:     .dw 0
DrLeft:   .dw 0
DrI:      .dw 0
DrNear:   .dw 0
DrR:      .dw 0
DrCX:     .dw 0
DrCY:     .dw 0
DrIdle:   .ds 16*4
DrRing:   .dw 0
