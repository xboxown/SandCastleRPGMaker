; Sandcastle RPG Maker - Apple IIgs player: a character attacking someone other than the Avatar (list item 37), as the
; Windows Ultima 6 engine's Attack command (U6GameWindow.Combat.cs AttackCommand / StartCombat / FtFightBack): the attacker
; goes after its target till it is dead - a blow when in reach (Strike), else a step toward it (StepToGoal, path.s); below
; 3 hit points it runs from it; the one struck fights back (a character of the map). Who fights whom: FightTab (8 pairs of
; owner codes - ext.s JOwner - saved). The Avatar as the target: talk op ACT 7 as before.

FT_N = 8

; EXT 47 FIGHT attacker target (10 bytes).
XtFightCmd:
        jsr ReadWho             ; the attacker
        jsr JOwner
        sta FtA
        lda [TalkPtr],y         ; the target
        sta JMap
        iny
        iny
        lda [TalkPtr],y
        sta JIdx
        jsr JOwner
        sta FtT
        lda FtA
        cmp FtT
        beq .d
        jsr FtPut
.d:     lda #10
        jmp Next

; FtA fights FtT from now on (its own pair, else a free one).
FtPut:
        ldx #0
.f:     lda FightTab,x
        cmp FtA
        beq .set
        inx
        inx
        inx
        inx
        cpx #FT_N*4
        bcc .f
        ldx #0
.e:     lda FightTab,x
        beq .set
        inx
        inx
        inx
        inx
        cpx #FT_N*4
        bcc .e
        rts
.set:   lda FtA
        sta FightTab,x
        lda FtT
        sta FightTab+2,x
        rts

; WorldTurn: CP (a character of this map) fighting someone? Its turn: a blow, a step toward it, or running. Carry set: it
; had its turn (Moved counted if it stepped).
FightTurn:
        lda CP                  ; its owner code: $8000 | map << 6 | its place
        sec
        sbc ChrPtr
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        sta FtI
        lda CurMap
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ora FtI
        ora #$8000
        sta FtMe
        ldx #0
.f:     lda FightTab,x
        cmp FtMe
        beq .got
        inx
        inx
        inx
        inx
        cpx #FT_N*4
        bcc .f
        clc
        rts
.got:   stx FtX
        lda FightTab+2,x        ; its target's record
        sta FtT
        cmp #$FFFF
        bne .chr
        sta JMap
        bra .rec
.chr:   and #$003F
        sta JIdx
        lda FtT
        and #$7FFF
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        sta JMap
.rec:   lda CP                  ; (WhoRec may load the map's records: CP kept)
        sta FtCP
        lda CP+2
        sta FtCP+2
        jsr WhoRec
        lda FtCP
        sta CP
        lda FtCP+2
        sta CP+2
        bcc .fb1
        brl .drop
.fb1:
        lda FtT
        cmp #$FFFF
        bne .alive
        lda AvDead
        beq .fb2
        brl .drop
.fb2:
        bra .on
.alive: ldy #C_FLAGS            ; dead, or gone: it stops
        lda [WP],y
        and #4
        beq .fb3
        brl .drop
.fb3:
.on:    ldy #C_HP               ; badly hurt: it runs from it
        lda [CP],y
        cmp #$8000
        bcs .run
        cmp #3
        bcs .fight
.run:   lda [CP]
        asl a
        sec
        sbc [WP]
        sta GX
        ldy #2
        lda [CP],y
        asl a
        sec
        sbc [WP],y
        sta GY
        jsr StepToGoal
        bcc .t
        inc Moved
.t:     sec
        rts
.fight: lda [WP]                ; how far (the longer way, across a seam the short way)
        sec
        sbc [CP]
        jsr ShortX
        jsr AbsX
        sta FtD
        ldy #2
        lda [WP],y
        sec
        sbc [CP],y
        jsr ShortY
        jsr AbsY
        cmp FtD
        bcc .dd
        sta FtD
.dd:    ldy #C_REACH
        lda [CP],y
        bne .rc
        lda #1
.rc:    cmp FtD
        bcs .strike
        lda [WP]                ; out of reach: a step toward it (round what is in the way)
        sta GX
        ldy #2
        lda [WP],y
        sta GY
        jsr StepToGoal
        bcc .t2
        inc Moved
.t2:    sec
        rts
.strike:
        lda CP
        sta AtkP
        lda CP+2
        sta AtkP+2
        lda WP
        sta DefP
        lda WP+2
        sta DefP+2
        lda #1
        sta FtStriking
        jsr Strike
        stz FtStriking
        lda FtCP                ; (CP again)
        sta CP
        lda FtCP+2
        sta CP+2
        jsr FtFightBack
        sec
        rts
.drop:  ldx FtX
        stz FightTab,x
        stz FightTab+2,x
        ldx #0                  ; and the gone one's own fight (it fought back)
.dl:    lda FightTab,x
        cmp FtT
        bne .dn
        stz FightTab,x
        stz FightTab+2,x
.dn:    inx
        inx
        inx
        inx
        cpx #FT_N*4
        bcc .dl
        clc
        rts

; The one struck (DefP), a character of this map still alive and not fighting already: it fights back (Actor::fight_back).
FtFightBack:
        lda DefP+2
        cmp ChrPtr+2
        bne .o
        lda DefP
        sec
        sbc ChrPtr
        bcc .o
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        cmp ChrCount
        bcs .o
        sta FtI
        ldy #C_FLAGS
        lda [DefP],y
        and #4+2                ; dead, or hostile (it fights the Avatar already)
        bne .o
        lda CurMap
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ora FtI
        ora #$8000
        sta FtA
        ldx #0                  ; fighting someone already: it goes on with that
.f:     lda FightTab,x
        cmp FtA
        beq .o
        inx
        inx
        inx
        inx
        cpx #FT_N*4
        bcc .f
        lda FtMe
        sta FtT
        jmp FtPut
.o:     rts

FtStriking: .dw 0
FtA:      .dw 0
FtT:      .dw 0
FtI:      .dw 0
FtX:      .dw 0
FtD:      .dw 0
FtMe:     .dw 0
FtCP:     .dd 0
