; Sandcastle RPG Maker - Apple IIgs player: how things move and what can be carried, as the Windows Ultima 6 engine:
; maps that wrap around (U6GameWindow.Wrap.cs - walk off the east edge, come back on the west; everyone crosses the
; seam, distances the short way round), characters stepping aside (MoveAside.cs, Exult's Actor::move_aside: a
; character in the way steps to a free cell at right angles, else a diagonal, else the two swap places; the Avatar
; steps aside for a walker too, out of combat), land, sea and air characters (Movement.cs: sea ones only on water,
; fliers over walls and solid things, never through characters), and the carry limit (Weight.cs, Exult's
; Check_weight: a member or the Avatar never carries more than 2 x Strength - "Too heavy!").
; GAME.DAT (after the bodies): u16 wrapping maps, their numbers; u16 weights, each 6 bytes: a name, its weight in
; tenths, 1 = a bag that weighs only itself (a bag of holding).

W_TOOHEAVY = 84

ParseMoves:
        lda [Ptr]
        sta WrapN
        lda #2
        jsr AddPtr
        ldy #0
.wl:    tya
        lsr a
        cmp WrapN
        bcs .wd
        lda [Ptr],y
        sta WrapMaps,y
        iny
        iny
        bra .wl
.wd:    lda WrapN
        asl a
        jsr AddPtr
        lda [Ptr]
        sta MvWtN
        lda #2
        jsr AddPtr
        lda MvWtN                 ; 6 bytes each
        asl a
        sta T4
        asl a
        clc
        adc T4
        sta T4
        ldy #0
.tl:    cpy T4
        bcs .td
        lda [Ptr],y
        sta MvWtTab,y
        iny
        iny
        bra .tl
.td:    lda T4
        jsr AddPtr
        lda [Ptr]               ; the Avatar's look as sprites (solo mode draws it as a member - solo.s)
        sta AvRec+4
        lda #2
        jsr AddPtr
        jsr SetMapWrap
        jmp ParseFloor          ; traps, lights (floor.s)

; ---- maps that wrap around ----

; MapWrap = 1 if the map the Avatar is on (CurMap) wraps around.
SetMapWrap:
        stz MapWrap
        ldx #0
.l:     txa
        lsr a
        cmp WrapN
        bcs .o
        lda WrapMaps,x
        cmp CurMap
        beq .y
        inx
        inx
        bra .l
.y:     lda #1
        sta MapWrap
.o:     rts

; X, Y brought back onto a map that wraps around (past an edge: on the other side). A is lost.
WrapXY:
        lda MapWrap
        beq .o
        txa
        bpl .xp
        clc
        adc MapW
        tax
        bra .y
.xp:    cmp MapW
        bcc .y
        sec
        sbc MapW
        tax
.y:     tya
        bpl .yp
        clc
        adc MapH
        tay
        rts
.yp:    cmp MapH
        bcc .o
        sec
        sbc MapH
        tay
.o:     rts

; CurX, CurY (the cursor's cell) brought back onto the map.
WrapCur:
        ldx CurX
        ldy CurY
        jsr WrapXY
        stx CurX
        sty CurY
        rts

; A = |A| across (a difference of x), the short way round on a map that wraps around. X and Y kept.
AbsX:
        jsr Abs
        sta MvWTv
        lda MapWrap
        beq .o
        lda MapW
        lsr a
        cmp MvWTv
        bcs .o
        lda MapW
        sec
        sbc MvWTv
        rts
.o:     lda MvWTv
        rts

; A = |A| up or down (a difference of y), the short way round.
AbsY:
        jsr Abs
        sta MvWTv
        lda MapWrap
        beq .o
        lda MapH
        lsr a
        cmp MvWTv
        bcs .o
        lda MapH
        sec
        sbc MvWTv
        rts
.o:     lda MvWTv
        rts

; A = a difference of x (signed): the short way round (signed), as Exult's delta_wrap_check. X and Y kept.
ShortX:
        sta MvWTv
        lda MapWrap
        beq .o
        lda MapW
        bra ShortAny
.o:     lda MvWTv
        rts

; A = a difference of y (signed): the short way round.
ShortY:
        sta MvWTv
        lda MapWrap
        beq .o
        lda MapH
        bra ShortAny
.o:     lda MvWTv
        rts

; MvWTv = a signed difference, A = the map's size that way: MvWTv brought within half the size either way.
ShortAny:
        sta MvWSize
        lsr a
        sta MvWHalf
        lda MvWTv
        bmi .neg
        cmp MvWHalf
        bcc .o
        beq .o
        sec
        sbc MvWSize
        rts
.neg:   eor #$FFFF
        inc a
        cmp MvWHalf
        bcc .o
        beq .o
        lda MvWTv
        clc
        adc MvWSize
        rts
.o:     lda MvWTv
        rts

; ---- land, sea and air ----

; A = the tile CP would step onto: carry set if it can be there - land: ground that can be walked; sea: water; air:
; anything. OccMask: what in a cell stops it (7: solid things, characters, members; a flier: 6, over solid things).
GroundFor:
        sta MvGTile
        lda #7
        sta OccMask
        ldy #C_FLAGS
        lda [CP],y
        and #$3000
        cmp #$2000
        beq .air
        sta MvGMove
        ldy MvGTile
        lda [FlagsPtr],y
        ldx MvGMove
        beq .land
        and #2                  ; sea: water only
        bne .yes
        clc
        rts
.land:  and #1
        bne .yes
        clc
        rts
.air:   lda #6
        sta OccMask
.yes:   sec
        rts

; ---- stepping aside ----

; X, Y: the character standing there on this map (not dead or absent) - carry set, WP = its record.
FindChrAt:
        stx MvFX
        sty MvFY
        lda ChrPtr
        sta WP
        lda ChrPtr+2
        sta WP+2
        lda ChrCount
        sta MvFN
.l:     lda MvFN
        beq .no
        ldy #C_FLAGS
        lda [WP],y
        and #4+8
        bne .n
        lda [WP]
        cmp MvFX
        bne .n
        ldy #2
        lda [WP],y
        cmp MvFY
        beq .yes
.n:     clc
        lda WP
        adc #64
        sta WP
        lda WP+2
        adc #0
        sta WP+2
        dec MvFN
        bra .l
.yes:   sec
        rts
.no:    clc
        rts

; Carry set if the character at WP may be asked to step aside: not fighting (hostile), dead, absent, asleep, knocked
; out, paralyzed - nor one that never moves (behaviour 0 stays where it is put: Movement Stationary).
CanBeAsked:
        ldy #C_FLAGS
        lda [WP],y
        and #2+4+8+16+32
        bne .no
        lda WP
        ldx WP+2
        jsr AilsOf              ; (ailments.s)
        and #A_SLEEP+A_PARA
        bne .no
        sec
        rts
.no:    clc
        rts

; CP tries the cells beside it, at right angles to the heading MvADX, MvADY first (one side, then the other), then the
; diagonals - Exult's order. Carry set if it stepped. (Asiding: those steps don't ask anyone else to move.)
TryAside:
        inc Asiding
        lda MvADY                 ; (-dy, dx)
        eor #$FFFF
        inc a
        sta SDX
        lda MvADX
        sta SDY
        jsr TryCharStep
        bcs .yes
        lda MvADY                 ; (dy, -dx)
        sta SDX
        lda MvADX
        eor #$FFFF
        inc a
        sta SDY
        jsr TryCharStep
        bcs .yes
        ldx #0
.d:     lda Diagonals,x
        sta SDX
        lda Diagonals+2,x
        sta SDY
        phx
        jsr TryCharStep
        plx
        bcs .yes
        inx
        inx
        inx
        inx
        cpx #16
        bcc .d
        dec Asiding
        clc
        rts
.yes:   dec Asiding
        sec
        rts

Diagonals:
        .dw 1,1, 1,$FFFF, $FFFF,1, $FFFF,$FFFF

; The Avatar steps onto NX, NY where a character stands: it steps aside, else they swap places. Carry set if the
; cell is free now.
AsideForAvatar:
        jsr SaveWalk
        ldx NX
        ldy NY
        jsr FindChrAt
        bcc .no
        jsr CanBeAsked
        bcc .no
        lda WP
        sta CP
        lda WP+2
        sta CP+2
        lda NX                  ; the Avatar's heading
        sec
        sbc PX
        jsr ShortX
        sta MvADX
        lda NY
        sec
        sbc PY
        jsr ShortY
        sta MvADY
        jsr TryAside
        bcs .yes
        lda [CP]                ; nowhere to go: onto the Avatar's cell (the Avatar onto its)
        tax
        ldy #2
        lda [CP],y
        tay
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        and #$FD
        sta [Ptr]
        rep #$20
        .a16
        lda PX
        sta [CP]
        ldy #2
        lda PY
        sta [CP],y
        ldx PX
        ldy PY
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        ora #2
        sta [Ptr]
        rep #$20
        .a16
.yes:   jsr RestoreWalk
        sec
        rts
.no:    jsr RestoreWalk
        clc
        rts

; CP (heading SDX, SDY) finds a character on SNX, SNY: it is asked to step aside, else they swap places.
; A = 0 (carry clear) it stays in the way; 1 it stepped aside (the cell is free); 2 they swapped (CP is there now).
AsideForChar:
        lda Asiding
        beq .go
        lda #0
        clc
        rts
.go:    lda WP
        sta MvSvWP
        lda WP+2
        sta MvSvWP+2
        ldx SNX
        ldy SNY
        jsr FindChrAt
        bcc .nf
        jsr CanBeAsked
        bcs .ok
.nf:    brl .no
.ok:    lda CP                  ; the walker and its step, kept
        sta MvWkCP
        lda CP+2
        sta MvWkCP+2
        lda SDX
        sta MvADX
        sta MvWkDX
        lda SDY
        sta MvADY
        sta MvWkDY
        lda SNX
        sta MvWkNX
        lda SNY
        sta MvWkNY
        lda WP
        sta CP
        lda WP+2
        sta CP+2
        jsr TryAside
        lda #0
        rol a
        sta MvAsideOk
        lda MvWkCP                ; the walker again
        sta CP
        lda MvWkCP+2
        sta CP+2
        lda MvWkDX
        sta SDX
        lda MvWkDY
        sta SDY
        lda MvWkNX
        sta SNX
        lda MvWkNY
        sta SNY
        lda MvAsideOk
        bne .moved
        lda [CP]                ; swap: the one in the way onto the walker's cell, the walker onto its
        sta [WP]
        ldy #2
        lda [CP],y
        sta [WP],y
        lda SNX
        sta [CP]
        lda SNY
        sta [CP],y
        jsr RestoreWP
        lda #2
        sec
        rts
.moved: jsr RestoreWP
        lda #1
        sec
        rts
.no:    jsr RestoreWP
        lda #0
        clc
        rts

RestoreWP:
        lda MvSvWP
        sta WP
        lda MvSvWP+2
        sta WP+2
        rts

; CP walks onto the Avatar's cell: out of combat the Avatar steps aside (right angles to CP's heading SDX, SDY, then
; the diagonals), else they swap places. Carry set if the Avatar moved.
AvatarAside:
        bra .go
.n1:    clc
        rts
.go:
        lda CombatOn
        bne .n1
        lda Asiding
        bne .n1
        ldy #C_FLAGS            ; a foe never pushes the Avatar aside
        lda [CP],y
        and #2
        bne .n1
        lda CP+2                ; nor does a member following (the party's records)
        cmp MyBank
        bne .nm
        lda CP
        cmp #PartyTab
        bcc .nm
        cmp #PartyTab+512
        bcc .n1
.nm:
        lda AvRec+C_FLAGS
        and #16+32
        bne .n1
        lda #AvRec
        ldx MyBank
        jsr AilsOf
        and #A_SLEEP+A_PARA
        bne .n1
        lda SDY
        eor #$FFFF
        inc a
        sta MvADX2
        lda SDX
        sta MvADY2
        jsr AvTry
        bcs .yes
        lda SDY
        sta MvADX2
        lda SDX
        eor #$FFFF
        inc a
        sta MvADY2
        jsr AvTry
        bcs .yes
        ldx #0
.d:     lda Diagonals,x
        sta MvADX2
        lda Diagonals+2,x
        sta MvADY2
        phx
        jsr AvTry
        plx
        bcs .yes
        inx
        inx
        inx
        inx
        cpx #16
        bcc .d
        lda [CP]                ; swap: the Avatar onto the walker's cell (the walker then steps onto the Avatar's)
        sta PX
        ldy #2
        lda [CP],y
        sta PY
.yes:   lda #1
        sta Moved
        sec
        rts
.no:    clc
        rts

; The Avatar onto PX + MvADX2, PY + MvADY2 if it is ground that can be walked and free. Carry set if it went.
AvTry:
        lda PX
        clc
        adc MvADX2
        tax
        lda PY
        clc
        adc MvADY2
        tay
        jsr WrapXY
        stx MvAvNX
        sty MvAvNY
        jsr MapTile
        cmp #$FFFF
        beq .no
        tay
        lda [FlagsPtr],y
        and #1
        beq .no
        ldx MvAvNX
        ldy MvAvNY
        jsr OccAt
        and #7
        bne .no
        lda MvAvNX
        sta PX
        lda MvAvNY
        sta PY
        sec
        rts
.no:    clc
        rts

SaveWalk:
        lda CP
        sta MvSvCP
        lda CP+2
        sta MvSvCP+2
        lda WP
        sta MvSvWP2
        lda WP+2
        sta MvSvWP2+2
        rts

RestoreWalk:
        lda MvSvCP
        sta CP
        lda MvSvCP+2
        sta CP+2
        lda MvSvWP2
        sta WP
        lda MvSvWP2+2
        sta WP+2
        rts

; ---- the carry limit ----

; A = a thing's number, X = the owner code that would carry it ($FFFF the Avatar, else a member's): carry set if they
; can - everything they then carry (the thing with what is in it) no more than 2 x their Strength. Too much: "Too
; heavy!" told, carry clear. One who isn't the Avatar or a member is never weighed. ItemPtr and MP are lost / kept.
WeightCheck:
        sta MvWIdx
        stx MvWOwner
        lda MP
        sta MvSvMP
        lda MP+2
        sta MvSvMP+2
        jsr OwnerStrength
        bcc .ok
        asl a                   ; 2 x Strength, in tenths: x 20
        asl a
        sta T4
        asl a
        asl a
        clc
        adc T4                  ; x 16 + x 4
        sta MvWMax
        stz MvWSum
        stz MvWAdd
        stz MvWI
.l:     lda MvWI
        cmp ObjCount
        bcs .done
        jsr WeighOne
        inc MvWI
        bra .l
.done:  lda MvWSum
        clc
        adc MvWAdd
        bcs .heavy
        cmp MvWMax
        beq .ok
        bcs .heavy
.ok:    jsr .mp
        sec
        rts
.heavy: jsr .mp
        lda #W_TOOHEAVY
        jsr Format
        clc
        rts
.mp:    lda MvSvMP
        sta MP
        lda MvSvMP+2
        sta MP+2
        rts

; A = the strength of the owner MvWOwner - carry clear: not one who is weighed.
OwnerStrength:
        lda MvWOwner
        cmp #$FFFF
        bne .m
        lda AvRec+C_STR
        sec
        rts
.m:     stz MvWK
.l:     lda MvWK
        cmp PartyN
        bcs .no
        jsr MemberAt            ; (party.s)
        jsr MPCode              ; (owner.s)
        cmp MvWOwner
        beq .f
        inc MvWK
        bra .l
.f:     ldy #C_STR
        lda [MP],y
        sec
        rts
.no:    clc
        rts

; Thing MvWI: counted into MvWAdd if it is the thing being taken or in it, into MvWSum if the owner carries it (at any
; depth) - not if it is inside a bag that weighs only itself, nor if it is gone.
WeighOne:
        lda MvWI
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #32
        beq .go
        rts
.go:    stz MvWPay
        stz MvWBag
        lda MvWI
        sta MvWCur
        lda #12
        sta MvWDep
.up:    lda MvWCur
        cmp MvWIdx
        bne .np
        lda #1
        sta MvWPay
.np:    lda MvWCur
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        sta MvWFl
        and #256
        beq .top
        ldy #14                 ; inside a container: up to it
        lda [ItemPtr],y
        sta MvWCur
        jsr ObjAt
        ldy #8
        lda [ItemPtr],y
        jsr NameWt
        lda MvWtBag
        beq .nb
        sta MvWBag
.nb:    dec MvWDep
        bne .up
        rts
.top:   lda MvWBag
        bne .out
        lda MvWPay
        bne .pay
        lda MvWFl
        and #64
        beq .out
        ldy #14
        lda [ItemPtr],y
        cmp MvWOwner
        bne .out
        jsr WtOfWI
        clc
        adc MvWSum
        bcc .s
        lda #$FFFF
.s:     sta MvWSum
.out:   rts
.pay:   jsr WtOfWI
        clc
        adc MvWAdd
        bcc .a
        lda #$FFFF
.a:     sta MvWAdd
        rts

WtOfWI:
        lda MvWI
        jsr ObjAt
        ldy #8
        lda [ItemPtr],y
        jmp NameWt

; A = a name: A = its weight in tenths (0: not known), MvWtBag = 1 if it is a bag that weighs only itself.
NameWt:
        sta MvWName
        stz MvWtBag
        ldx #0
        ldy MvWtN
.l:     cpy #0
        beq .none
        lda MvWtTab,x
        cmp MvWName
        beq .f
        txa
        clc
        adc #6
        tax
        dey
        bra .l
.f:     lda MvWtTab+4,x
        sta MvWtBag
        lda MvWtTab+2,x
        rts
.none:  lda #0
        rts

; A = the owner code of whoever carries container A (at any depth); $FFFE: it is on the map.
CarrierOf:
        sta MvWCur
        lda #12
        sta MvWDep
.l:     lda MvWCur
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        sta MvWFl
        ldy #14
        lda [ItemPtr],y
        sta MvWCur
        lda MvWFl
        and #256
        beq .top
        dec MvWDep
        bne .l
.top:   lda MvWFl
        and #64
        beq .map
        lda MvWCur
        rts
.map:   lda #$FFFE
        rts

MapWrap:  .dw 0
WrapN:    .dw 0
WrapMaps: .ds 64
MvWTv:       .dw 0
MvWSize:    .dw 0
MvWHalf:    .dw 0
MvGTile:    .dw 0
MvGMove:    .dw 0
OccMask:  .dw 7
MvFX:       .dw 0
MvFY:       .dw 0
MvFN:       .dw 0
MvADX:      .dw 0
MvADY:      .dw 0
MvADX2:     .dw 0
MvADY2:     .dw 0
MvAvNX:     .dw 0
MvAvNY:     .dw 0
Asiding:  .dw 0
MvAsideOk:  .dw 0
MvSvWP:     .dd 0
MvSvWP2:    .dd 0
MvSvCP:     .dd 0
MvWkCP:     .dd 0
MvWkDX:     .dw 0
MvWkDY:     .dw 0
MvWkNX:     .dw 0
MvWkNY:     .dw 0
MvWtN:      .dw 0
MvWtTab:    .ds 6*256
MvWIdx:     .dw 0
MvWOwner:   .dw 0
MvWMax:     .dw 0
MvWSum:     .dw 0
MvWAdd:     .dw 0
MvWI:       .dw 0
MvWK:       .dw 0
MvWCur:     .dw 0
MvWDep:     .dw 0
MvWFl:      .dw 0
MvWPay:     .dw 0
MvWBag:     .dw 0
MvWName:    .dw 0
MvWtBag:    .dw 0
MvSvMP:     .dd 0
