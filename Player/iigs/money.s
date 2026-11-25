; Sandcastle RPG Maker - Apple IIgs player: more than one currency and Exchange Currency, as the Windows Ultima 6 engine
; (U6GameWindow.Money.cs). The main currency (its Money objects') is the Avatar's gold (AvRec+GOLD) as before; each other
; currency has a purse of its own (Purse, saved with the game). Money picked up goes into its currency's purse.
; GAME.DAT (after the factions): u16 money kinds, each (its name, its purse 0-7); then 7 words: purses 1-7 at the start.
; Talk op 47 XCHG: from purse (1), to purse (1), how many (word; $FFFF all), the rate (word: 8.8 fixed, the cut taken
; off already) - 7 bytes.

ParseMoney:
        lda [Ptr]
        sta MoKN
        lda #2
        jsr AddPtr
        lda MoKN
        asl a
        asl a
        sta T4
        ldy #0
.k:     cpy T4
        bcs .kd
        lda [Ptr],y
        sta MoKinds,y
        iny
        iny
        bra .k
.kd:    lda T4
        jsr AddPtr
        ldy #0
.p:     lda [Ptr],y
        sta Purse+2,y
        iny
        iny
        cpy #14
        bcc .p
        lda #14
        jsr AddPtr
        jmp ParseLiquids        ; (liquid.s)

; A = a money thing's name: X = its purse x 2 (0: the main currency, the Avatar's gold).
PurseOf:
        sta MoT
        ldx #0
        ldy MoKN
.l:     cpy #0
        beq .main
        lda MoKinds,x
        cmp MoT
        beq .y
        inx
        inx
        inx
        inx
        dey
        bra .l
.y:     lda MoKinds+2,x
        and #7
        asl a
        tax
        rts
.main:  ldx #0
        rts

; A = so much money of the thing at ItemPtr (its name): added to its purse.
AddMoney:
        sta MoN
        ldy #8
        lda [ItemPtr],y
        jsr PurseOf
        cpx #0
        bne .p
        lda AvRec+GOLD
        clc
        adc MoN
        bcc .g
        lda #$FFFF
.g:     sta AvRec+GOLD
        rts
.p:     lda Purse,x
        clc
        adc MoN
        bcc .h
        lda #$FFFF
.h:     sta Purse,x
        rts

; X = a purse x 2: A = what is in it.
PurseGet:
        cpx #0
        bne .p
        lda AvRec+GOLD
        rts
.p:     lda Purse,x
        rts
; X = a purse x 2, A = what it holds now.
PurseSet:
        cpx #0
        bne .p
        sta AvRec+GOLD
        rts
.p:     sta Purse,x
        rts

; All the money (Gold (all money)): every purse, the most 65535.
AllMoney:
        lda AvRec+GOLD
        ldx #2
.l:     clc
        adc Purse,x
        bcc .n
        lda #$FFFF
        rts
.n:     inx
        inx
        cpx #16
        bcc .l
        rts

OpXchg:
        iny
        lda [TalkPtr],y
        and #$00FF
        asl a
        sta MoFrom
        iny
        lda [TalkPtr],y
        and #$00FF
        asl a
        sta MoTo
        iny
        lda [TalkPtr],y
        sta MoN
        iny
        iny
        lda [TalkPtr],y
        sta MB0                 ; the rate
        ldx MoFrom              ; give: as many as asked for, or all there is
        jsr PurseGet
        sta MoHave
        lda MoN
        cmp MoHave
        bcc .g
        lda MoHave
.g:     sta MoN
        beq .done
        lda MoHave
        sec
        sbc MoN
        ldx MoFrom
        jsr PurseSet
        lda MoN                 ; get: given x rate (8.8)
        sta MulA
        jsr Mul16
        lda MulR+2
        and #$FF00
        bne .big
        lda MulR+1
        bra .add
.big:   lda #$FFFF
.add:   sta MoN
        ldx MoTo
        jsr PurseGet
        clc
        adc MoN
        bcc .s
        lda #$FFFF
.s:     ldx MoTo
        jsr PurseSet
        jsr HpChanged
.done:  lda #7
        jmp Next

MoKN:     .dw 0
MoKinds:  .ds 4*16
MoT:      .dw 0
MoN:      .dw 0
MoFrom:   .dw 0
MoTo:     .dw 0
MoHave:   .dw 0
