; Sandcastle RPG Maker - Apple IIgs player: haggling, as Serpent Isle's (Engine/ShopModel.cs ShopHaggle.Offer): an offer at
; or above his asking price - he takes his asking price; below his lowest - refused; else he meets the player half way
; (rounded up), never below his lowest, and once that is within one of the offer he takes the offer. Each refusal or
; counter is a try; when they are used up he names his last price (yes / no in the talk).

; HAGGLE var asking lowest tries accepted gaveup declined, then 5 C strings.
OpHaggle:
        iny
        lda [TalkPtr],y
        asl a
        sta HgVar
        iny
        iny
        lda [TalkPtr],y
        sta HgAsk
        iny
        iny
        lda [TalkPtr],y
        sta HgLow
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta HgTries
        iny
        lda [TalkPtr],y
        sta HgAcc
        iny
        iny
        lda [TalkPtr],y
        sta HgGive
        iny
        iny
        lda [TalkPtr],y
        sta HgDecl
        iny
        iny
        sty HgText              ; the strings
        stz HgUsed
; Asks for an offer: the question, the number typed (Mode 14).
HgAsk2:
        lda #0
        jsr HgSay
        stz HgIn
        stz HgLen
        lda #14
        sta Mode
        rts

; Says haggling string A (0 question .. 4 gives up), {0} = HgPrice.
HgSay:
        tax
        ldy HgText
.s:     cpx #0                  ; to the string
        beq .got
.c:     lda [TalkPtr],y
        and #$00FF
        iny
        cmp #0
        bne .c
        dex
        bra .s
.got:   tya                     ; SayP = it
        clc
        adc TalkPtr
        sta SayP
        lda TalkPtr+2
        adc #0
        sta SayP+2
        ldx #0                  ; the price in figures (FmtName)
        lda HgPrice
        jsr Decimal
        sep #$20
        .a8
        lda #0
        sta StatBuf,x
        rep #$20
        .a16
        lda #StatBuf
        sta FmtName
        lda MyBank
        sta FmtName+2
        jsr FillFmt
        lda #FmtBuf
        sta SayP
        lda MyBank
        sta SayP+2
        ldy #0
        jsr SayText
        jmp NewLine

; A key while the offer is typed: figures, Delete, Enter; Escape declines.
HaggleKey:
        cmp #$1B
        bne .k1
        lda HgDecl
        bra .go
.k1:    cmp #$0D
        beq .offer
        cmp #$7F
        beq .del
        cmp #8
        beq .del
        cmp #'0'
        bcc .o
        cmp #'9'+1
        bcs .o
        ldx HgLen
        cpx #5
        bcs .o
        sta HgCh
        sec
        sbc #'0'
        sta T4
        lda HgIn                ; x 10 + the figure
        asl a
        sta T5
        asl a
        asl a
        clc
        adc T5
        clc
        adc T4
        sta HgIn
        inc HgLen
        lda #HgCh
        jsr PrintMine
.o:     rts
.del:   lda HgLen
        beq .o
        dec HgLen
        lda HgIn                ; / 10
        sta ExA
        lda #10
        sta ExB
        lda ExA
        jsr SDiv
        lda ExQ
        sta HgIn
        rts
.go:    sta TPC
        lda #3
        sta Mode
        jsr NewLine
        jmp RunTalk
.offer: jsr NewLine
        lda #3
        sta Mode
        lda HgIn                ; at or above his asking price: his asking price
        cmp HgAsk
        bcc .under
        lda HgAsk
        bra .accept
.under: lda HgUsed              ; the last try?
        inc a
        cmp HgTries
        lda #0
        rol a
        sta HgLast
        lda HgIn                ; below his lowest: refused
        cmp HgLow
        bcs .mid
        lda HgAsk
        sta HgPrice
        lda HgLast
        bne .giveup
        lda #1                  ; too low
        jsr HgSay
        inc HgUsed
        jmp HgAsk2
.mid:   lda HgAsk               ; half way, rounded up, not below his lowest
        clc
        adc HgIn
        inc a
        lsr a
        cmp HgLow
        bcs .c
        lda HgLow
.c:     sta HgPrice
        lda HgIn
        inc a
        cmp HgPrice
        bcc .counter
        lda HgIn                ; within one: the offer
.accept: sta HgPrice
        ldx HgVar
        sta VarTab,x
        lda #3
        jsr HgSay
        lda HgAcc
        sta TPC
        jmp RunTalk
.counter:
        lda HgLast
        bne .giveup
        lda #2
        jsr HgSay
        lda HgPrice
        sta HgAsk
        inc HgUsed
        jmp HgAsk2
.giveup: lda HgPrice            ; his last price: yes or no
        ldx HgVar
        sta VarTab,x
        lda #4
        jsr HgSay
        lda HgGive
        sta TPC
        jmp RunTalk

; PAYV var else: the variable's price paid in gold, or the else place.
OpPayV:
        iny
        lda [TalkPtr],y
        asl a
        tax
        lda VarTab,x
        sta GVal
        iny
        iny
        lda [TalkPtr],y
        sta GKill
        lda AvRec+GOLD
        cmp GVal
        bcs .ok
        lda GKill
        sta TPC
        jmp RunTalk
.ok:    sec
        sbc GVal
        sta AvRec+GOLD
        jsr HpChanged
        lda #5
        jmp Next

HgVar:    .dw 0
HgAsk:    .dw 0
HgLow:    .dw 0
HgTries:  .dw 0
HgUsed:   .dw 0
HgLast:   .dw 0
HgAcc:    .dw 0
HgGive:   .dw 0
HgDecl:   .dw 0
HgText:   .dw 0
HgIn:     .dw 0
HgLen:    .dw 0
HgCh:     .dw 0
HgPrice:  .dw 0
