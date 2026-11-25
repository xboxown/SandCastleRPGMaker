; Sandcastle RPG Maker - Apple IIgs player: liquid containers (buckets, bottles), as the Windows Ultima 6 engine
; (U6GameWindow.Consumables.cs - Exult's bucket.uc). Use it, "On what?", the cursor:
;  the Avatar - drunk ("Ahhh, how refreshing.", the liquid's effects; "No, thank thee." if it can't be drunk);
;  someone who gives a liquid (a cow) - milked into it if empty and it holds that;
;  anyone else - doused and woken ("Hey, stop that!"), the spill left at their feet;
;  a source (a well, a trough) - filled ("You fill {0}.");  a lit light - put out, if the liquid puts out fires;
;  anywhere else - emptied ("You empty {0}."), the spill left there.
; What a container holds is its look: each liquid it can hold has a look of its own (the editor makes copies where two
; would be the same), so it is saved with the objects. Used up when poured out (if set): gone.
; Special kind 11: empty sprite, icon; pour sound, fill sound; used up (1); the spill (name, sprite, icon, flags,
; other); how many liquids (1), each: liquid (1), sprite, icon - 5 bytes.
; GAME.DAT (after the money): u16 liquids, each name, flags (1 puts out fires, 2 can't be drunk), spell ($FFFF none);
; u16 sources, each object name, liquid; u16 givers, each character name, liquid.

SP_LIQUID  = 11
VERB_POUR  = 12

W_LQEMPTY   = 104
W_LQFULL    = 105
W_LQDRUNK   = 106
W_LQNOTHANKS = 107
W_LQSTOP    = 108
W_LQFILL    = 109
W_LQDOUSE   = 110
W_LQSPILT   = 111
W_LQCANT    = 112

ParseLiquids:
        lda [Ptr]               ; the liquids: 6 bytes each
        sta LqN
        lda #2
        jsr AddPtr
        lda LqN
        asl a
        sta T4
        asl a
        clc
        adc T4
        sta T4
        ldy #0
.a:     cpy T4
        bcs .ad
        lda [Ptr],y
        sta LqLiq,y
        iny
        iny
        bra .a
.ad:    lda T4
        jsr AddPtr
        lda [Ptr]               ; the sources: 4 bytes each
        sta LqN+2
        lda #2
        jsr AddPtr
        lda LqN+2
        asl a
        asl a
        sta T4
        ldy #0
.b:     cpy T4
        bcs .bd
        lda [Ptr],y
        sta LqSrc,y
        iny
        iny
        bra .b
.bd:    lda T4
        jsr AddPtr
        lda [Ptr]               ; the givers: 4 bytes each
        sta LqN+4
        lda #2
        jsr AddPtr
        lda LqN+4
        asl a
        asl a
        sta T4
        ldy #0
.c:     cpy T4
        bcs .cd
        lda [Ptr],y
        sta LqGiv,y
        iny
        iny
        bra .c
.cd:    lda T4
        jsr AddPtr
        jmp ParseBreak          ; (break.s)

; Use on a liquid container at ItemPtr: "On what?" and the cursor.
LiquidPicked:
        lda ItemPtr
        sta LqRec
        lda ItemPtr+2
        sta LqRec+2
        lda PenX
        clc
        adc SpaceW
        sta PenX
        lda #W_ONWHAT
        jsr PrintSaid
        lda #VERB_POUR
        sta Verb
        lda PX
        sta CurX
        lda PY
        sta CurY
        lda #4
        sta Mode
        jsr DrawView
        sec
        rts

; PickCell, the pour's verb: on what is on the cursor's cell.
PourOn:
        jsr WhatIsThere
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        lda TxtColor
        jsr SetColor
        jsr PrintWord
        jsr NewLine
        lda Kind
        sta LqKind
        lda ItemPtr
        sta LqTgt
        lda ItemPtr+2
        sta LqTgt+2
        jsr LqSelf              ; ItemPtr = the container, SpRec its record, LqHas what it holds (0 nothing)
        lda LqKind
        cmp #1
        bne .k2
        jmp LqDrink
.k2:    cmp #2
        bne .k3
        jmp LqOnChar
.k3:    cmp #3
        bne .floor
        jsr LqOnThing
        bcc .floor
        rts
.floor: lda LqHas               ; the floor: spilt
        bne .sp
        jmp LqSayEmpty
.sp:    lda CurX
        sta LqX
        lda CurY
        sta LqY
        jsr LqPour
        lda #W_LQSPILT
        jmp LqSayName

; ItemPtr = LqRec, its record, LqHas.
LqSelf:
        lda LqRec
        sta ItemPtr
        lda LqRec+2
        sta ItemPtr+2
        jsr IsSpecial
        stz LqHas
        ldy #4
        lda [ItemPtr],y
        sta T4
        lda SpRec+20
        and #$00FF
        sta T5
        ldx #0
.l:     lda T5
        beq .o
        lda SpRec+22,x
        cmp T4
        beq .y
        inx
        inx
        inx
        inx
        inx
        dec T5
        bra .l
.y:     lda SpRec+21,x
        and #$00FF
        inc a
        sta LqHas
.o:     rts

; A = a liquid + 1 (0 empty): the container's look so.
LqSet:
        sta T4
        cmp #0
        bne .full
        lda SpRec+1
        ldx SpRec+3
        bra .put
.full:  lda SpRec+20
        and #$00FF
        sta T5
        ldx #0
.l:     lda T5
        beq .o
        lda SpRec+21,x
        and #$00FF
        inc a
        cmp T4
        beq .y
        inx
        inx
        inx
        inx
        inx
        dec T5
        bra .l
.y:     lda SpRec+24,x
        pha
        lda SpRec+22,x
        plx
.put:   ldy #4
        sta [ItemPtr],y
        txa
        ldy #12
        sta [ItemPtr],y
        lda T4
        sta LqHas
        jsr RefreshInventory
.o:     rts

; Poured out at LqX, LqY: the sound, empty (or gone), the spill there.
LqPour:
        lda SpRec+5
        cmp #$FFFF
        beq .q
        ldx #255
        jsr PlaySound
.q:     lda #0
        jsr LqSet
        lda SpRec+9
        and #$00FF
        beq .sp
        ldy #6                  ; used up: gone
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        jsr RefreshInventory
.sp:    lda SpRec+10             ; the spill
        cmp #$FFFF
        beq .o
        lda ObjPtr
        sta Src
        lda ObjPtr+2
        sta Src+2
        lda ObjCount
        sta T5
.l:     lda T5
        beq .o
        ldy #6
        lda [Src],y
        and #32
        beq .n
        ldy #8
        lda [Src],y
        cmp #$FFFF
        bne .n
        lda LqX
        sta [Src]
        ldy #2
        lda LqY
        sta [Src],y
        ldy #4
        lda SpRec+12
        sta [Src],y
        ldy #6
        lda SpRec+16
        and #$0FFF-64-32
        ora CurBits
        sta [Src],y
        ldy #8
        lda SpRec+10
        sta [Src],y
        ldy #10
        lda SpRec+18
        sta [Src],y
        ldy #12
        lda SpRec+14
        sta [Src],y
        ldy #14
        lda #$FFFF
        sta [Src],y
        rts
.n:     clc
        lda Src
        adc #16
        sta Src
        lda Src+2
        adc #0
        sta Src+2
        dec T5
        bra .l
.o:     rts

; A = a liquid + 1: carry set if the container holds it.
LqCanHold:
        sta T4
        lda SpRec+20
        and #$00FF
        sta T5
        ldx #0
.l:     lda T5
        beq .no
        lda SpRec+21,x
        and #$00FF
        inc a
        cmp T4
        beq .y
        inx
        inx
        inx
        inx
        inx
        dec T5
        bra .l
.y:     sec
        rts
.no:    clc
        rts

; A = a liquid + 1: filled with it (if empty and it holds it) - "You fill {0}.". Carry set.
LqFill:
        sta LqNew
        lda LqHas
        beq .e
        lda #W_LQFULL
        jsr LqSayName
        sec
        rts
.e:     lda LqNew
        jsr LqCanHold
        bcs .ok
        lda #W_LQCANT
        jsr LqSayName
        sec
        rts
.ok:    lda SpRec+5+2           ; the fill sound
        cmp #$FFFF
        beq .q
        ldx #255
        jsr PlaySound
.q:     lda LqNew
        jsr LqSet
        lda #W_LQFILL
        jsr LqSayName
        sec
        rts

; The Avatar drinks it.
LqDrink:
        lda LqHas
        bne .h
        jmp LqSayEmpty
.h:     dec a
        asl a
        sta T4
        asl a
        clc
        adc T4
        tax                     ; x 6
        lda LqLiq+2,x
        and #2
        beq .d
        lda #W_LQNOTHANKS
        jmp LqSay
.d:     lda LqLiq+4,x
        sta LqSpell
        lda #0
        jsr LqSet
        lda #W_LQDRUNK
        jsr PrintSaid
        lda LqSpell             ; its effects, on the Avatar
        cmp #$FFFF
        beq .o
        jsr SpellAt
        ldy #0
.cp:    lda [ItemPtr],y
        sta SpBuf,y
        iny
        iny
        cpy #64
        bcc .cp
        jsr AvatarWP
        jsr ApplySpell
.o:     jmp PromptLine

; On a character (LqTgt): a giver fills it, anyone else is doused.
LqOnChar:
        jsr LqWP
        ldy #C_NAME
        lda [WP],y
        ldx #4
        jsr LqLookup
        bcc .douse
        jmp LqFill
.douse: lda LqHas
        bne .h
        jmp LqSayEmpty
.h:     ldy #C_FLAGS            ; awake
        lda [WP],y
        and #$FFEF
        sta [WP],y
        lda [WP]
        sta LqX
        ldy #2
        lda [WP],y
        sta LqY
        jsr LqPour
        lda #W_LQSTOP
        jmp LqSay

; On a thing (LqTgt): a source fills it, a lit light is put out. Carry clear: neither (spilt there).
LqOnThing:
        jsr LqWP
        ldy #8
        lda [WP],y
        ldx #2
        jsr LqLookup
        bcc .light
        jmp LqFill
.light: ldy #10
        lda [WP],y
        jsr IsLight
        bcc .no
        ldy #10
        lda [WP],y
        and #$0100
        beq .no
        lda LqHas
        bne .h
        jsr LqSayEmpty
        sec
        rts
.h:     dec a
        asl a
        sta T4
        asl a
        clc
        adc T4
        tax
        lda LqLiq+2,x
        and #1
        beq .no
        ldy #10
        lda [WP],y
        and #$FEFF
        sta [WP],y
        lda [WP]
        sta LqX
        ldy #2
        lda [WP],y
        sta LqY
        lda SpRec+10             ; (no spill on a fire)
        pha
        lda #$FFFF
        sta SpRec+10
        jsr LqPour
        pla
        sta SpRec+10
        ldy #8
        lda [WP],y
        jsr NameNumber
        lda #W_LQDOUSE
        jsr Format
        jsr DrawView
        jsr PromptLine
        sec
        rts
.no:    clc
        rts

; A = a name, X = 2 the sources / 4 the givers: carry set, A = its liquid + 1.
LqLookup:
        sta T4
        lda LqN,x
        sta T5
        ldy #0
        cpx #2
        bne .g
.l:     lda T5
        beq .no
        lda LqSrc,y
        cmp T4
        beq .ys
        iny
        iny
        iny
        iny
        dec T5
        bra .l
.ys:    lda LqSrc+2,y
        inc a
        sec
        rts
.g:     lda T5
        beq .no
        lda LqGiv,y
        cmp T4
        beq .yg
        iny
        iny
        iny
        iny
        dec T5
        bra .g
.yg:    lda LqGiv+2,y
        inc a
        sec
        rts
.no:    clc
        rts

; WP = the target.
LqWP:
        lda LqTgt
        sta WP
        lda LqTgt+2
        sta WP+2
        rts

LqSayEmpty:
        lda #W_LQEMPTY
; A = a word with {0} = the container's name: said, the prompt.
LqSayName:
        pha
        lda LqRec               ; (ItemPtr may have moved: RefreshInventory)
        sta ItemPtr
        lda LqRec+2
        sta ItemPtr+2
        ldy #8
        lda [ItemPtr],y
        jsr NameNumber
        pla
        jsr Format
        jsr DrawView
        jmp PromptLine
; A = a word: said, the prompt.
LqSay:
        jsr PrintSaid
        jsr DrawView
        jmp PromptLine

LqRec:    .dd 0
LqTgt:    .dd 0
LqKind:   .dw 0
LqHas:    .dw 0
LqNew:    .dw 0
LqX:      .dw 0
LqY:      .dw 0
LqSpell:  .dw 0
LqN:      .dw 0,0,0             ; liquids, sources, givers
LqLiq:    .ds 6*16
LqSrc:    .ds 4*16
LqGiv:    .ds 4*8
