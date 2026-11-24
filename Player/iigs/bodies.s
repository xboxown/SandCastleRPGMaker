; Sandcastle RPG Maker - Apple IIgs player: knocked out and dead bodies, as the Windows Ultima 6 engine
; (U6GameWindow.Unconscious.cs, Bodies.cs; Exult's reduce_health / die):
;  - a blow taking someone to 0 hit points or below knocks them out (flag 32; the Avatar: AvDown): lying in their Body
;    pose, doing nothing; below -(Strength / 3) they are dead. Foes go for those awake first, the knocked out only when
;    none are left. Every half minute a 1 in 6 chance to mend a point; at 1 or more they get up (healing of any kind too).
;    The Avatar knocked out can't act - the world goes on (in combat too) until it gets up, or dies.
;  - the dead leave a body where they fell (a container - picked up unless too heavy) holding all they carried; one
;    that leaves no body drops it all where it fell.

; From GAME.DAT (after the specials).
ParseBodies:
        lda [Ptr]
        sta AvBody
        ldy #2
        lda [Ptr],y
        sta BodyNm
        ldy #4
        lda [Ptr],y
        cmp #129
        bcc .c
        lda #128
.c:     sta BodyN
        lda #6
        jsr AddPtr
        lda BodyN               ; 6 bytes each
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
        sta BodyTab,y
        iny
        iny
        bra .l
.d:     lda T4
        jsr AddPtr
        jmp ParseMoves          ; wrapping maps, weights (moves.s)

; A = the body picture of a character named A ($FFFF none); X its flags (1 no body, 2 too heavy).
BodySprite:
        sta BN
        ldy #0
        ldx BodyN
.l:     cpx #0
        beq .none
        lda BodyTab,y
        cmp BN
        beq .got
        iny
        iny
        iny
        iny
        iny
        iny
        dex
        bra .l
.got:   lda BodyTab+4,y
        tax
        lda BodyTab+2,y
        rts
.none:  ldx #0
        lda #$FFFF
        rts

; After a blow on DefP (its hit points set, signed): knocked out, or dead (carry set: Dies), or just hurt.
DefState:
        ldy #C_HP
        lda [DefP],y
        beq .low
        bmi .low
        jsr TellWound           ; still standing
        clc
        rts
.low:   sta BHp                 ; dead below -(Strength / 3)
        ldy #C_STR
        lda [DefP],y
        sta ExA
        lda #3
        sta ExB
        lda ExA
        jsr SDiv
        lda ExQ
        eor #$FFFF
        inc a
        sta BLim
        lda BHp
        sec
        sbc BLim
        bvc .v
        eor #$8000
.v:     bpl .out                ; at or above the limit: knocked out
        ldy #C_HP               ; dead
        lda #0
        sta [DefP],y
        lda #W_KILLED
        jsr Tell
        sec
        rts
.out:   jsr DefIsAvatar
        bcc .chr
        lda AvDown
        bne .told
        lda #1
        sta AvDown
        bra .say
.chr:   ldy #C_FLAGS
        lda [DefP],y
        and #32
        bne .told
        lda [DefP],y
        ora #32
        sta [DefP],y
.say:   lda #W_KNOCKED          ; "Rat is knocked out!"
        jsr Tell
.told:  jsr DrawView
        clc
        rts

; WP healed (a spell, a potion...): back on its feet if it has 1 hit point or more.
UpCheck:
        ldy #C_HP
        lda [WP],y
        beq .o
        bmi .o
        jsr WPIsAvatar
        bcc .c
        stz AvDown
        rts
.c:     ldy #C_FLAGS
        lda [WP],y
        and #$FFFF-32
        sta [WP],y
.o:     rts

; Each pass of the main loop: every half minute the knocked out (this map, the party, the Avatar) may mend a point.
MendRun:
        pha
        pha
        tool $2503
        pla
        sta BNow
        pla
        lda BNow
        sec
        sbc MendT
        cmp #1800
        bcs .go
        rts
.go:    lda BNow
        sta MendT
        lda AvDown              ; the Avatar
        beq .chars
        lda #AvRec
        sta WP
        lda MyBank
        sta WP+2
        jsr Mend1
.chars: lda ChrPtr
        sta WP
        lda ChrPtr+2
        sta WP+2
        lda ChrCount
        sta BN
.l:     lda BN
        beq .mem
        ldy #C_FLAGS
        lda [WP],y
        and #4+32
        cmp #32
        bne .n
        jsr Mend1
.n:     lda WP
        clc
        adc #64
        sta WP
        lda WP+2
        adc #0
        sta WP+2
        dec BN
        bra .l
.mem:   stz BK
.m:     lda BK
        cmp PartyN
        bcs .d
        jsr MemberAt
        lda MP
        sta WP
        lda MP+2
        sta WP+2
        ldy #C_FLAGS
        lda [WP],y
        and #4+32
        cmp #32
        bne .mn
        jsr Mend1
.mn:    inc BK
        bra .m
.d:     lda Mode
        bne .o
        jsr DrawView
        jsr HpChanged
.o:     rts
; WP: 1 in 6 a point back (healed already: up at once).
Mend1:
        ldy #C_HP
        lda [WP],y
        beq .roll
        bpl .up
.roll:  lda #6
        jsr Random
        bne .o
        ldy #C_HP
        lda [WP],y
        inc a
        sta [WP],y
.up:    jmp UpCheck
.o:     rts

; DefP has died (not the Avatar): its body where it fell, holding all it carried - or that on the ground.
MakeBody:
        lda [DefP]
        sta CrSpX
        ldy #2
        lda [DefP],y
        sta CrSpY
        jsr DefCode             ; its things' owner code
        sta BCode
        ldy #C_NAME
        lda [DefP],y
        sta BName
        jsr BodySprite
        stx BFlags
        cmp #$FFFF
        bne .s
        ldy #4                  ; no Body pose: its own look
        lda [DefP],y
.s:     sta BSpr
        lda CurBits
        sta SpBits
        lda #$FFFF
        sta SpIn
        lda BFlags              ; no body: its things on the ground there
        and #1
        beq .body
        lda #$FFFF
        sta BIdx
        bra .things
.body:  lda BodyNm              ; the body: a container (picked up unless too heavy)
        sta SpRec+220
        lda BSpr
        sta SpRec+222
        sta SpRec+224
        lda BFlags
        and #2
        eor #2
        ora #128
        sta SpRec+226
        lda #$FFFF
        sta SpRec+228
        ldx #220
        jsr SpawnRec
        jsr LastSpawn           ; BIdx: the body's number
.things: stz BI
.l:     lda BI
        cmp ObjCount
        bcs .done
        jsr ObjAddr
        ldy #6
        lda [ItemPtr],y
        and #32+64
        cmp #64
        bne .n
        ldy #14
        lda [ItemPtr],y
        cmp BCode
        bne .n
        lda BIdx
        cmp #$FFFF
        beq .ground
        ldy #6                  ; into the body
        lda [ItemPtr],y
        and #$0FFF-64
        ora #256
        sta [ItemPtr],y
        lda #$FFFF
        sta [ItemPtr]
        ldy #2
        sta [ItemPtr],y
        ldy #14
        lda BIdx
        sta [ItemPtr],y
        bra .n
.ground: ldy #6                 ; on the ground where it fell
        lda [ItemPtr],y
        and #$0FFF-64
        ora CurBits
        sta [ItemPtr],y
        lda CrSpX
        sta [ItemPtr]
        ldy #2
        lda CrSpY
        sta [ItemPtr],y
        ldy #14
        lda #$FFFF
        sta [ItemPtr],y
.n:     inc BI
        bra .l
.done:  jmp MakeOccAgain

; A = DefP's owner code: a character of this map $8000 | map << 6 | its place; a member its own map and place.
DefCode:
        lda DefP+2
        cmp MyBank
        bne .map
        lda DefP
        sec
        sbc #PartyTab
        cmp #512
        bcs .map
        ldy #P_MAP
        lda [DefP],y
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ldy #P_IDX
        ora [DefP],y
        ora #$8000
        rts
.map:   lda DefP
        sec
        sbc ChrPtr
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        sta BI
        lda CurMap
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ora BI
        ora #$8000
        rts

; BIdx = the number of the object SpawnRec just made (the last named BodyNm at CrSpX, CrSpY on this map).
LastSpawn:
        lda #$FFFF
        sta BIdx
        stz BI
.l:     lda BI
        cmp ObjCount
        bcs .o
        jsr ObjAddr
        ldy #8
        lda [ItemPtr],y
        cmp BodyNm
        bne .n
        lda [ItemPtr]
        cmp CrSpX
        bne .n
        ldy #2
        lda [ItemPtr],y
        cmp CrSpY
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #32+64+256
        bne .n
        lda BI
        sta BIdx
.n:     inc BI
        bra .l
.o:     rts

W_KNOCKED = 38
AvBody:   .dw $FFFF
BodyNm:   .dw $FFFF
BodyN:    .dw 0
BodyTab:  .ds 128*6
BN:       .dw 0
BK:       .dw 0
BI:       .dw 0
BHp:      .dw 0
BLim:     .dw 0
BNow:     .dw 0
MendT:    .dw 0
BCode:    .dw 0
BName:    .dw 0
BFlags:   .dw 0
BSpr:     .dw 0
BIdx:     .dw 0
