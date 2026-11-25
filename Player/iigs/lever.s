; Sandcastle RPG Maker - Apple IIgs player: levers, switches and buttons, as the Windows Ultima 6 engine
; (U6GameWindow.Mechanisms.cs - Ultima 6's levers and switches working doors and portcullises): Use flips one (a button
; springs back after its time), its sound, and everything it is linked to (Map Editor > Link) changes over: a door opens
; or shuts (a locked one is unlocked, as a portcullis winch), a light goes on or out, a linked lever flips too, anything
; else gets its By Use event. Its object's Other is $FFFB; GAME.DAT (after the blood): u16 levers, each 16 words: object,
; kind (0 lever, 1 switch, 2 button), its other look, on sound, off sound, reset turns, on, links (0-4), 4 links, 0 x 4
; (word 12: a pressed button's turns left).

ParseLevers:
        lda [Ptr]
        sta LvN
        lda #2
        jsr AddPtr
        lda LvN                 ; 32 bytes each
        asl a
        asl a
        asl a
        asl a
        asl a
        sta T4
        ldy #0
.l:     cpy T4
        bcs .d
        lda [Ptr],y
        sta LvTab,y
        iny
        iny
        bra .l
.d:     lda T4
        jsr AddPtr
        jmp ParseModes          ; the characters' combat modes (solo.s)

; A = an object's number: carry set, X = its place in LvTab, if it is a lever.
LeverOf:
        sta LvObj
        ldx #0
        ldy LvN
.l:     cpy #0
        beq .no
        lda LvTab,x
        cmp LvObj
        beq .yes
        txa
        clc
        adc #32
        tax
        dey
        bra .l
.yes:   sec
        rts
.no:    clc
        rts

; Use on the thing at ItemPtr (beside the Avatar): carry set if it was a lever, switch or button (pulled).
LeverUse:
        ldy #10
        lda [ItemPtr],y
        cmp #$FFFB
        beq .go
        clc
        rts
.go:    jsr IndexOf
        jsr LeverOf
        bcs .have
        clc
        rts
.have:  lda LvTab+2,x           ; a button: pressed (if it isn't still), springs back after its time
        cmp #2
        bne .flip
        lda LvTab+12,x
        bne .done
        lda LvTab+10,x
        bne .t
        lda #1
.t:     sta LvTab+24,x
        stz LvDepth
        jsr FlipLever
        bra .done
.flip:  stz LvDepth
        jsr FlipLever
.done:  jsr DrawView
        jsr PromptLine
        sec
        rts

; The lever at LvTab + X changes over: its look, on / off, its sound, then what it is linked to.
FlipLever:
        stx LvX
        lda LvTab,x
        jsr ObjAt
        ldy #4                  ; its look <-> its other look
        lda [ItemPtr],y
        pha
        ldx LvX
        lda LvTab+4,x
        sta [ItemPtr],y
        pla
        sta LvTab+4,x
        lda LvTab+12,x
        eor #1
        sta LvTab+12,x
        beq .off
        lda LvTab+6,x
        bra .snd
.off:   lda LvTab+8,x
.snd:   cmp #$FFFF
        beq .links
        pha                     ; heard from where it is (moves.s)
        lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        pla
        jsr PlaySoundAt
.links: lda LvDepth             ; a loop of levers stops after a few
        cmp #5
        bcs .o
        inc LvDepth
        ldx LvX
        lda LvTab+14,x
        sta LvLinks
        stz LvK
.k:     lda LvK
        cmp LvLinks
        bcs .kd
        cmp #4
        bcs .kd
        asl a
        clc
        adc LvX
        tax
        lda LvTab+16,x
        jsr WorkLink
        inc LvK
        bra .k
.kd:    dec LvDepth
.o:     rts

; Linked thing A changes over.
WorkLink:
        pha
        lda LvX                 ; (kept across a nested lever)
        pha
        lda LvK
        pha
        lda LvLinks
        pha
        lda 7,s                 ; the thing
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #4                  ; a door: open <-> shut, unlocked
        beq .nd
        lda [ItemPtr],y
        and #$FFF7
        sta [ItemPtr],y
        jsr SwapDoor
        jsr DoorMoved           ; its sound and motion (door.s)
        bra .out
.nd:    ldy #10                 ; a light: on <-> out
        lda [ItemPtr],y
        cmp #$FFFB
        beq .lever
        jsr IsLight
        bcc .ev
        eor #$0100
        sta [ItemPtr],y
        bra .out
.lever: lda 7,s                 ; a linked lever flips too
        jsr LeverOf
        bcc .out
        jsr FlipLever
        bra .out
.ev:    ldy #8                  ; anything else: its By Use event
        lda [ItemPtr],y
        ldx #12
        jsr ObjEvQueue
.out:   pla
        sta LvLinks
        pla
        sta LvK
        pla
        sta LvX
        pla
        rts

; The door at ItemPtr: open <-> shut - its looks swap, it stops walkers only shut (its cell in the cell map).
SwapDoor:
        ldy #6
        lda [ItemPtr],y
        eor #16+1
        sta [ItemPtr],y
        ldy #4
        lda [ItemPtr],y
        pha
        ldy #10
        lda [ItemPtr],y
        ldy #4
        sta [ItemPtr],y
        pla
        ldy #10
        sta [ItemPtr],y
        lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        jsr OccAt
        ldy #6
        lda [ItemPtr],y
        and #1
        sep #$20
        .a8
        beq .free
        lda [Ptr]
        ora #1
        sta [Ptr]
        bra .d
.free:  lda [Ptr]
        and #$FE
        sta [Ptr]
.d:     rep #$20
        .a16
        rts

; Each turn: pressed buttons spring back when their time is up.
LeverTurn:
        ldx #0
        ldy LvN
.l:     cpy #0
        beq .o
        lda LvTab+24,x
        beq .n
        dec LvTab+24,x
        bne .n
        lda LvTab+12,x          ; up again: its look and sound, no links
        beq .n
        phy
        stx LvX
        lda #9
        sta LvDepth             ; (no links on the way back)
        jsr FlipLever
        ldx LvX
        ply
        stz LvDepth
.n:     txa
        clc
        adc #32
        tax
        dey
        bra .l
.o:     rts

LvN:      .dw 0
LvTab:    .ds 32*32
LvObj:    .dw 0
LvX:      .dw 0
LvK:      .dw 0
LvLinks:  .dw 0
LvDepth:  .dw 0
