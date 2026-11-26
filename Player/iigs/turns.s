; Sandcastle RPG Maker - Apple IIgs player: Ultima 6's turns in a fight, as Nuvie (actor.lua actor_update_all / advance_time,
; player.cpp): whenever a fight is on - combat mode, or within 8 of the Avatar someone hostile, afraid or fighting (fight.s)
; - the world waits for the Avatar (no half-second turns) and each of the Avatar's turns every character gets its Dexterity
; in points and acts once for each Avatar's Dexterity of them (the rest kept): the same Dexterity one step or blow a turn,
; twice it two, half of it one turn in two. A runner ("Aiiieee!") takes its step and waits like anyone else. The user,
; 2026-11-26: "it is a turn based system".

TU_MAX = 128                    ; characters on a map with points

; Carry set: a fight is on.
FightOn:
        lda CombatOn
        beq .look
        sec
        rts
.look:  lda ChrPtr
        sta Dst
        lda ChrPtr+2
        sta Dst+2
        stz TuI
.l:     lda TuI
        cmp ChrCount
        bcc .one
        clc
        rts
.one:   ldy #C_FLAGS
        lda [Dst],y
        and #4
        bne .n                  ; dead
        lda [Dst]               ; within 8 of the Avatar
        sec
        sbc PX
        jsr ShortX
        jsr AbsX
        cmp #9
        bcs .n
        ldy #2
        lda [Dst],y
        sec
        sbc PY
        jsr ShortY
        jsr AbsY
        cmp #9
        bcs .n
        ldy #C_FLAGS            ; hostile
        lda [Dst],y
        and #2
        bne .y
        lda Dst                 ; afraid
        ldx Dst+2
        jsr AilsOf
        and #A_FEAR
        bne .y
        lda CurMap              ; fighting someone (fight.s)
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ora TuI
        ora #$8000
        sta TuOwn
        ldx #0
.f:     lda FightTab,x
        cmp TuOwn
        beq .y
        inx
        inx
        inx
        inx
        cpx #FT_N*4
        bcc .f
.n:     lda Dst
        clc
        adc #64
        sta Dst
        lda Dst+2
        adc #0
        sta Dst+2
        inc TuI
        brl .l
.y:     sec
        rts

; After the Avatar's turn (AfterTurn): the world's. In a fight, by points (Nuvie): each character its Dexterity, then the
; world goes round as often as anyone still has an Avatar's Dexterity of them (at most 4 times).
TurnWorld:
        jsr FightOn
        bcs .fight
        stz TuOn
        jmp WorldTurn
.fight: lda #1
        sta TuOn
        lda AvRec+C_DEX         ; a turn's worth: the Avatar's Dexterity
        bne .ad
        lda #1
.ad:    sta TuDex
        lda ChrPtr              ; everyone's points
        sta Dst
        lda ChrPtr+2
        sta Dst+2
        ldx #0
.p:     txa
        lsr a
        cmp ChrCount
        bcs .pd
        cmp #TU_MAX
        bcs .pd
        ldy #C_DEX
        lda [Dst],y
        bne .dx
        lda #1
.dx:    sta TuT
        clc
        adc TuPts,x
        sta TuPts,x
        lda TuT                 ; kept no more than its own and a turn's worth (none hoarded)
        clc
        adc TuDex
        cmp TuPts,x
        bcs .ok
        sta TuPts,x
.ok:    lda Dst
        clc
        adc #64
        sta Dst
        lda Dst+2
        adc #0
        sta Dst+2
        inx
        inx
        bra .p
.pd:    stz TuPass
.r:     stz TuActed
        jsr WorldTurn
        inc TuPass
        lda TuActed
        beq .d
        lda TuPass
        cmp #4
        bcc .r
.d:     stz TuPass
        stz TuOn
        rts

; WorldTurn, each character (CP; TurnN counting down): in a fight, its turn only with a turn's worth of points (taken).
; Carry set: it has its turn.
TuMay:
        lda TuOn
        bne .f
        sec
        rts
.f:     lda ChrCount            ; its place: ChrCount - TurnN
        sec
        sbc TurnN
        cmp #TU_MAX
        bcs .y
        asl a
        tax
        lda TuPts,x
        cmp TuDex
        bcc .no
        sec
        sbc TuDex
        sta TuPts,x
        inc TuActed
.y:     sec
        rts
.no:    clc
        rts

TuOn:     .dw 0
TuDex:    .dw 1
TuPass:   .dw 0
TuActed:  .dw 0
TuI:      .dw 0
TuT:      .dw 0
TuOwn:    .dw 0
TuPts:    .ds TU_MAX*2
