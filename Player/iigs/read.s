; Sandcastle RPG Maker - Apple IIgs player: books and signs read, money, shops - as the Windows Ultima 6 engine
; (U6GameWindow.U6Things.cs U6ReadBook: the words in the text area a screenful at a time, "<more>" between, Escape
; stops; U6GameWindow.Shop.cs made into keyword talk by the editor). A book's or sign's Other: 3 (bits 14-15), slot 0,
; its text's place (bits 0-9). Money on the map: Other $FFFE, Icon its worth - Get puts it into the Avatar's gold
; (its stat word 23). PAY / SELL in a shop's talk pay and earn it.

W_MORE    = 62
W_NOTHINGWRITTEN = 63
GOLD      = 16+46               ; AvRec: the gold (stat word 23)

ParseTexts:
        lda [Ptr]
        sta TextCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta TextPtr
        lda Ptr+2
        sta TextPtr+2
        rts

; Carry set if Other (A) is a book's or sign's text.
IsText:
        sta T4
        cmp #$FFFE
        bcs .no
        and #$C000
        cmp #$C000
        bne .no
        lda T4
        and #$3C00              ; slot 0: a book to read, not a spellbook
        bne .no
        lda T4
        sec
        rts
.no:    lda T4
        clc
        rts

; Text A read: its code run as a talk (no one talking); nothing in it: "There is nothing written."
ReadText:
        cmp TextCount
        bcs .none
        tax
        lda TextPtr
        sta TalkPtr
        lda TextPtr+2
        sta TalkPtr+2
.w:     cpx #0
        beq .got
        lda [TalkPtr]           ; past one: its length, its code
        clc
        adc #2
        clc
        adc TalkPtr
        sta TalkPtr
        lda TalkPtr+2
        adc #0
        sta TalkPtr+2
        dex
        bra .w
.got:   lda TalkPtr             ; its code after the length
        clc
        adc #2
        sta TalkPtr
        lda TalkPtr+2
        adc #0
        sta TalkPtr+2
        lda [TalkPtr]
        and #$00FF
        beq .none               ; END at once: no words
        stz TPC
        lda #1
        sta EvRunning
        sta EvHit
        lda #3
        sta Mode
        jmp RunTalk
.none:  lda #W_NOTHINGWRITTEN
        jsr PrintSaid
        jmp PromptLine

; Get on money: the Avatar's gold, the coins gone from the map.
GetGold:
        ldy #12
        lda [ItemPtr],y
        clc
        adc AvRec+GOLD
        bcc .g
        lda #$FFFF
.g:     sta AvRec+GOLD
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        jsr FreeCell
        lda #8                  ; {1} gets {0}.
        jsr Format
        jsr DrawView
        jsr HpChanged
        jmp PromptLine

; PAY price else: the Avatar's gold, or the else place.
OpPay:
        iny
        lda [TalkPtr],y
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

; SELL name price else: one such thing from the Avatar for the price, or the else place.
OpSell:
        iny
        lda [TalkPtr],y
        sta GName
        iny
        iny
        lda [TalkPtr],y
        sta GVal
        iny
        iny
        lda [TalkPtr],y
        sta GKill
        lda GName
        jsr CountItem
        cmp #0
        bne .has
        lda GKill
        sta TPC
        jmp RunTalk
.has:   lda GName
        sta EvName
        lda #1
        sta GCount
        jsr TakeItems
        lda AvRec+GOLD
        clc
        adc GVal
        bcc .g
        lda #$FFFF
.g:     sta AvRec+GOLD
        jsr RefreshInventory
        jsr HpChanged
        lda #7
        jmp Next

; MORE: "<more>", a key goes on.
OpMore:
        jsr NewLine
        lda #W_MORE
        jsr PrintSaid
        lda #10
        sta Mode
        rts

; The key at <more>: Escape stops, any other goes on.
MoreKey:
        cmp #$1B
        bne .on
        lda #3
        sta Mode
        jmp EndTalk
.on:    jsr NewLine
        lda #3
        sta Mode
        inc TPC
        jmp RunTalk

TextCount: .dw 0
TextPtr:  .dd 0
