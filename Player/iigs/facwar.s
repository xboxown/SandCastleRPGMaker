; Sandcastle RPG Maker - Apple IIgs player: factions at war with each other (list item 39), as the Windows Ultima 6 engine's
; ScanSurroundings (U6GameWindow.Combat.cs; Engine/FactionModel.cs BehaviorToward): a character looks round and attacks
; someone in sight (within 6) whose faction its own thinks badly enough of (the relationship's band attacks) - the fight is
; item 37's (fight.s). How one faction thinks of another: Change Faction Relationship's (FwOv, saved), else the Faction
; Editor's (GAME.DAT, FwTab), else the default. Factions and the Avatar's: fac.s, as before. A few characters near the
; Avatar look round each turn (not all at once - the IIgs's time).

FW_N = 64
FW_OV = 16

; GAME.DAT after the live pieces (hudlive.s ParseLive): the default relationship, then (viewer, target, value) pairs.
ParseFacWar:
        lda [Ptr]
        sta FwDef
        ldy #2
        lda [Ptr],y
        cmp #FW_N+1
        bcc .n
        lda #FW_N
.n:     sta FwN
        asl a
        sta FwT
        asl a
        clc
        adc FwT
        sta FwT                 ; 6 bytes each
        ldx #0
        ldy #4
.c:     cpx FwT
        bcs .d
        lda [Ptr],y
        sta FwTab,x
        iny
        iny
        inx
        inx
        bra .c
.d:     rts

; FwV = how faction FwA thinks of faction FwB (0-100).
FwRel:
        lda FwA
        cmp FwB
        bne .o
        lda #100
        sta FwV
        rts
.o:     ldx #0                  ; changed in play
.v:     lda FwOv,x
        cmp FwA
        bne .vn
        lda FwOv+2,x
        cmp FwB
        bne .vn
        lda FwOv+4,x
        sta FwV
        rts
.vn:    inx
        inx
        inx
        inx
        inx
        inx
        cpx #FW_OV*6
        bcc .v
        ldx #0                  ; the Faction Editor's
.t:     cpx FwT
        bcs .def
        lda FwTab,x
        cmp FwA
        bne .tn
        lda FwTab+2,x
        cmp FwB
        bne .tn
        lda FwTab+4,x
        sta FwV
        rts
.tn:    inx
        inx
        inx
        inx
        inx
        inx
        bra .t
.def:   lda FwDef
        sta FwV
        rts

; Carry set: FwV's band attacks (fac.s's bands).
FwAttacks:
        ldx #0
        ldy FcBN
.b:     cpy #0
        beq .no
        lda FwV
        cmp FcBands,x
        bcc .got
        inx
        inx
        inx
        inx
        dey
        bra .b
.got:   lda FcBands+2,x
        beq .no
        sec
        rts
.no:    clc
        rts

; WorldTurn, CP not fighting: now and then (a few each turn, near the Avatar) it looks round for an enemy faction's member.
FwScan:
        lda FcBN
        beq .o0
        lda FwTick              ; one turn in four each, the characters in turn
        clc
        adc TurnN
        and #3
        bne .o0
        ldy #C_NAME             ; its faction
        lda [CP],y
        jsr FacOfName
        bcc .o0
        sta FwA
        lda [CP]                ; near the Avatar (within 10)
        sec
        sbc PX
        jsr ShortX
        jsr AbsX
        cmp #11
        bcs .o0
        ldy #2
        lda [CP],y
        sec
        sbc PY
        jsr ShortY
        jsr AbsY
        cmp #11
        bcc .go
.o0:    rts
.go:    lda ChrPtr
        sta Dst
        lda ChrPtr+2
        sta Dst+2
        stz FwI
.l:     lda FwI
        cmp ChrCount
        bcc .one
        rts
.one:   lda Dst
        cmp CP
        bne .ch
        lda Dst+2
        cmp CP+2
        beq .n                  ; itself
.ch:    ldy #C_FLAGS
        lda [Dst],y
        and #4+16+32            ; dead, asleep, knocked out: left alone
        bne .n
        lda [Dst]               ; in sight: within 6
        sec
        sbc [CP]
        jsr ShortX
        jsr AbsX
        cmp #7
        bcs .n
        ldy #2
        lda [Dst],y
        sec
        sbc [CP],y
        jsr ShortY
        jsr AbsY
        cmp #7
        bcs .n
        ldy #C_NAME
        lda [Dst],y
        jsr FacOfName
        bcc .n
        sta FwB
        jsr FwRel
        jsr FwAttacks
        bcc .n
        lda CP                  ; an enemy: it attacks it (fight.s)
        sec
        sbc ChrPtr
        jsr FwOwner
        sta FtA
        lda FwI
        jsr FwOwner2
        sta FtT
        jmp FtPut
.n:     lda Dst
        clc
        adc #64
        sta Dst
        lda Dst+2
        adc #0
        sta Dst+2
        inc FwI
        brl .l

; A = a record's offset in the map's list (x64): its owner code. FwOwner2: A = its place.
FwOwner:
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
FwOwner2:
        sta FwT2
        lda CurMap
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ora FwT2
        ora #$8000
        rts

; EXT 49 FACREL viewer target op value (10 bytes): Change Faction Relationship between two factions (not the Avatar's -
; fac.s): op 0 set, 1 add, 2 take away; kept 0-100 (FwOv, saved).
XtFacRelCmd:
        iny
        lda [TalkPtr],y
        sta FwA
        iny
        iny
        lda [TalkPtr],y
        sta FwB
        iny
        iny
        lda [TalkPtr],y
        sta FwOp
        iny
        iny
        lda [TalkPtr],y
        sta FwC
        jsr FwRel               ; now
        lda FwOp
        beq .set
        cmp #1
        beq .add
        lda FwV
        sec
        sbc FwC
        bra .cl
.add:   lda FwV
        clc
        adc FwC
        bra .cl
.set:   lda FwC
.cl:    cmp #$8000
        bcc .c0
        lda #0
.c0:    cmp #101
        bcc .c1
        lda #100
.c1:    sta FwV
        ldx #0                  ; its own entry, else a free one
.f:     lda FwOv,x
        cmp FwA
        bne .fn
        lda FwOv+2,x
        cmp FwB
        beq .put
.fn:    inx
        inx
        inx
        inx
        inx
        inx
        cpx #FW_OV*6
        bcc .f
        ldx #0
.e:     lda FwOv,x
        beq .put
        inx
        inx
        inx
        inx
        inx
        inx
        cpx #FW_OV*6
        bcc .e
        bra .d
.put:   lda FwA
        sta FwOv,x
        lda FwB
        sta FwOv+2,x
        lda FwV
        sta FwOv+4,x
.d:     lda #10
        jmp Next

FwDef:    .dw 50
FwN:      .dw 0
FwT:      .dw 0
FwT2:     .dw 0
FwTab:    .ds FW_N*6
FwA:      .dw 0
FwB:      .dw 0
FwC:      .dw 0
FwV:      .dw 0
FwOp:     .dw 0
FwI:      .dw 0
FwTick:   .dw 0
