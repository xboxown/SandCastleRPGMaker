; Sandcastle RPG Maker - Apple IIgs player: pets, as the Windows Ultima 6 engine (U6GameWindow.Pets.cs - Serpent Isle's
; and Ultima 8's): a creature that follows the Avatar without joining the party (its behaviour 3, combat.s calls PetFollow),
; talked to for orders. Through ACT (acts.s):
;  9 Call Character (a: 1 it follows after): from off the screen it is put at the view's edge on its side, then it walks
;    up to the Avatar.
; 10 Order On A Target (a: 0 attack, 1 fetch, 2 go to; c: the prompt's name): the verb cursor - the player picks someone
;    or something (Esc: no order) - it goes there and attacks it (blows till it is dead), takes it, or just goes.
; 11 / 12 Sniff And Lead to a character (a, b: its map and place) / to the nearest thing named a: it follows the scent,
;    the Avatar (and the party) led behind it - an event's commands wait, so the Avatar is walked after it - till it is
;    there or the scent is lost (the most 200 steps).

VERB_ORDER = 11
PET_FOLLOW = 3                  ; C_BEHAV: follows the Avatar

; combat.s's calm turn, behaviour 3: CP keeps near the Avatar (two cells). Carry set if it moved.
PetFollow:
        lda [CP]
        sec
        sbc PX
        jsr AbsX
        sta T5
        ldy #2
        lda [CP],y
        sec
        sbc PY
        jsr AbsY
        cmp T5
        bcs .m
        lda T5
.m:     cmp #3
        bcc .no
        jmp StepToward
.no:    clc
        rts

; ---- Call Character ----
AcCall:
        jsr WPIsAvatar
        bcc .c
        rts
.c:     jsr WPInParty
        bcc .c2
        rts
.c2:    jsr AcWhere
        ldx AcX
        ldy AcY
        jsr CellOnScreen
        bcs .walk
        lda AcX                 ; off the screen: at the view's edge, on its side
        sec
        sbc PX
        jsr ShortX
        jsr Sign
        sta T4
        lda AcY
        sec
        sbc PY
        jsr ShortY
        jsr Sign
        sta T5
        ora T4
        bne .e
        lda #1
        sta T4
.e:     lda HalfC
        jsr .times4
        clc
        adc PX
        sta AcNX
        lda T5
        sta T4
        lda HalfR
        jsr .times4
        clc
        adc PY
        tay
        ldx AcNX
        jsr WrapXY
        txa
        sta [WP]
        tya
        ldy #2
        sta [WP],y
.walk:  lda PX
        sta AcA
        lda PY
        sta AcB
        jsr AcBeside
        lda AcA0
        beq .o
        ldy #C_BEHAV            ; it follows from now on
        lda #PET_FOLLOW
        sta [WP],y
.o:     rts
; A x T4 (T4 -1, 0 or 1)
.times4: ldx T4
        beq .z
        cpx #1
        beq .r
        eor #$FFFF
        inc a
.r:     rts
.z:     lda #0
        rts

; ---- Order On A Target ----
AcOrder:
        jsr WPIsAvatar
        bcc .c
        rts
.c:     lda WP                  ; (WP: the pet, kept)
        sta PtP
        lda WP+2
        sta PtP+2
        lda AcC                 ; its prompt
        cmp #$FFFF
        beq .np
        asl a
        asl a
        tax
        lda NameTab,x
        sta TP
        lda NameTab+2,x
        sta TP+2
        lda TxtColor
        jsr SetColor
        jsr PrintWord
        jsr NewLine
.np:    lda PX                  ; the verb cursor, its own keys loop
        sta CurX
        lda PY
        sta CurY
        lda #VERB_ORDER
        sta Verb
        lda #$FFFF
        sta MoveIdx
        lda #4
        sta Mode
        stz PtPicked
        jsr DrawView
.l:     pha
        pea $0028               ; key down, auto-key
        pea #bank(EvRec)
        pea #EvRec
        tool $0A06              ; GetNextEvent
        pla
        beq .l
        lda EvMsg
        and #$007F
        sta Key
        cmp #$09                ; (no grid)
        beq .l
        jsr CursorKey           ; arrows, Enter / Space (PickCell -> OrderPicked), Esc
        lda Mode
        cmp #4
        beq .l
        lda PtP
        sta WP
        lda PtP+2
        sta WP+2
        lda PtPicked
        bne .go
        rts
.go:    lda PtX
        sta AcA
        lda PtY
        sta AcB
        lda AcA0                ; the order
        beq .atk
        cmp #1
        beq .fetch
        jmp AcBeside            ; go to
.fetch: jmp AcPickup
.atk:   lda PtKind              ; attack: a character
        cmp #2
        beq .a2
        rts
.a2:    jsr AcBeside
        lda #20
        sta PtBlows
.b:     lda PtP                 ; the pet strikes it
        sta AtkP
        lda PtP+2
        sta AtkP+2
        lda PtC
        sta DefP
        lda PtC+2
        sta DefP+2
        ldy #C_FLAGS
        lda [DefP],y
        and #4
        bne .o
        jsr Strike
        jsr DrawView
        lda #20
        jsr DoorWait
        dec PtBlows
        bne .b
.o:     lda PtP
        sta WP
        lda PtP+2
        sta WP+2
        rts

; PickCell, the order's verb: what is on the cursor's cell kept (Kind, ItemPtr), the loop ends (Mode 0).
OrderPicked:
        lda CurX
        sta PtX
        lda CurY
        sta PtY
        jsr WhatIsThere
        lda Kind
        sta PtKind
        lda ItemPtr
        sta PtC
        lda ItemPtr+2
        sta PtC+2
        lda #1
        sta PtPicked
        stz Mode
        rts

; ---- Sniff And Lead ----
; To a character: AcA, AcB its map and place (on this map).
AcSniffChr:
        lda AcA
        cmp CurMap
        bne .lost
        lda AcB
        cmp ChrCount
        bcs .lost
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        clc
        adc ChrPtr
        sta PtC
        lda ChrPtr+2
        adc #0
        sta PtC+2
        stz PtKind
        bra Lead
.lost:  rts
; To the nearest thing named AcA on this map (lying about, not carried).
AcSniffObj:
        lda AcA
        sta PtName
        lda #1
        sta PtKind
Lead:
        jsr WPIsAvatar
        bcc .fb2
        brl .o
.fb2:
        lda WP
        sta PtP
        lda WP+2
        sta PtP+2
        ldy #C_BEHAV            ; (not following while it leads)
        lda [WP],y
        sta PtBeh
        lda #0
        sta [WP],y
        lda #200
        sta PtBlows
.l:     jsr Scent               ; GX, GY: where the scent is now
        bcs .fb3
        brl .end
.fb3:
        lda PtP
        sta WP
        lda PtP+2
        sta WP+2
        jsr AcWhere
        lda AcX
        sec
        sbc GX
        jsr AbsX
        sta T4
        lda AcY
        sec
        sbc GY
        jsr AbsY
        cmp T4
        bcs .m
        lda T4
.m:     cmp #2
        bcc .end                ; there
        lda AcX                 ; where it was: the Avatar comes after
        sta PtX
        lda AcY
        sta PtY
        jsr AcStep
        bcc .end                ; blocked: it gives up
        lda GX
        pha
        lda GY
        pha
        lda #AvRec              ; the Avatar a step after it (if not next to it already)
        sta WP
        lda MyBank
        sta WP+2
        lda PX
        sec
        sbc PtX
        jsr AbsX
        sta T4
        lda PY
        sec
        sbc PtY
        jsr AbsY
        ora T4
        beq .st
        lda PtX
        sta GX
        lda PtY
        sta GY
        jsr AcWhere
        jsr AcStep
.st:    pla
        sta GY
        pla
        sta GX
        jsr DrawView
        lda #ACT_STEP_TICKS
        jsr DoorWait
        dec PtBlows
        beq .fb1
        brl .l
.fb1:
.end:   lda PtP
        sta WP
        lda PtP+2
        sta WP+2
        ldy #C_BEHAV
        lda PtBeh
        sta [WP],y
        ldy #8                  ; it faces the scent
.o:     rts

; Where the scent leads now (GX, GY); carry clear: nowhere.
Scent:
        lda PtKind
        bne .obj
        lda PtC
        sta Src
        lda PtC+2
        sta Src+2
        ldy #C_FLAGS            ; a character: alive
        lda [Src],y
        and #4
        beq .fb4
        brl .no
.fb4:
        lda [Src]
        sta GX
        ldy #2
        lda [Src],y
        sta GY
        sec
        rts
.obj:   lda PtP                 ; the nearest thing of that name lying on this map
        sta WP
        lda PtP+2
        sta WP+2
        jsr AcWhere
        lda #$FFFF
        sta PtBest
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta AcLeft
.l:     lda AcLeft
        beq .d
        ldy #8
        lda [ItemPtr],y
        cmp PtName
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .n
        lda [ItemPtr]
        sec
        sbc AcX
        jsr AbsX
        sta T4
        ldy #2
        lda [ItemPtr],y
        sec
        sbc AcY
        jsr AbsY
        clc
        adc T4
        cmp PtBest
        bcs .n
        sta PtBest
        lda [ItemPtr]
        sta GX
        ldy #2
        lda [ItemPtr],y
        sta GY
.n:     lda #16
        jsr NextItem
        dec AcLeft
        bra .l
.d:     lda PtBest
        cmp #$FFFF
        beq .no
        sec
        rts
.no:    clc
        rts

AcA0:     .dw 0                 ; (AcA as it came: the order / follows)
PtP:      .dd 0
PtC:      .dd 0
PtPicked: .dw 0
PtX:      .dw 0
PtY:      .dw 0
PtKind:   .dw 0
PtBlows:  .dw 0
PtName:   .dw 0
PtBeh:    .dw 0
PtBest:   .dw 0
