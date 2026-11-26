; Sandcastle RPG Maker - Apple IIgs player: Conversation Style (list item 32; U6GameWindow.U6TalkStyle.cs) - how talk looks
; and works from now on, kept in saves (TsOn... in SCRIPTDATA; all 0: Ultima 6's): answers typed (Ultima 6), picked from a
; list (1-9; Enter the first) or either; the keywords coloured or not, in any colour; the portrait in the HUD's Portrait,
; inside the words' box, or none; the words in the HUD's text area or in a box of their own over the screen - its place,
; size, colour, picture and the words' colour. The IIgs has one font (the user's ruling): the style's font is not used.

; EXT 44 STYLE on answers mark portrait words keycolour bx by bw bh boxcolour boxpicture textcolour (23 bytes).
XtStyleCmd:
        iny
        ldx #0
.b:     lda [TalkPtr],y         ; five bytes
        and #$00FF
        sta TsOn,x
        iny
        inx
        inx
        cpx #10
        bcc .b
.w:     lda [TalkPtr],y         ; eight words
        sta TsOn,x
        iny
        iny
        inx
        inx
        cpx #26
        bcc .w
        lda #23
        jmp Next

; SayText: a keyword's colour now (A: the word's own colour - kept when the style marks no keywords).
KeyColNow:
        ldy TsOn
        beq .k
        ldy TsMark
        beq .o
        ldy TsKey
        cpy #$FFFF
        beq .k
        tya
        rts
.k:     lda KeyColor
.o:     rts

; ShowTalker: carry set - the HUD's Portrait isn't the talker's (the style puts it in the box, or nowhere).
PortNotHud:
        lda TsOn
        beq .no
        lda TsPort
        beq .no
        sec
        rts
.no:    clc
        rts

; Words to say in a talk (RunTalk SAY / ASK): the style's box opened first, if it has one (Y kept).
BoxCheck:
        lda TsTalk
        beq .o
        lda TsOn
        beq .o
        lda TsWords
        beq .o
        lda BoxOn
        bne .o
        phy
        jsr OpenBox
        ply
.o:     rts

; The words' box over the screen: its colour, its picture, the portrait inside if it goes there; the text area is the box's
; inside till the talk ends (CloseBox).
OpenBox:
        lda #1
        sta BoxOn
        lda TextX
        sta SvTX
        lda TextY
        sta SvTY
        lda TextW
        sta SvTW
        lda TextH
        sta SvTH
        lda TxtColor
        sta SvTC
        lda PenX
        sta SvPX
        lda PenY
        sta SvPY
        jsr BoxRect
        lda TsBCol
        jsr FillRect
        lda TsBPic
        cmp #$FFFF
        beq .np
        sta SprId
        lda TsBX
        and #$FFFE
        sta CellX
        lda TsBY
        sta CellY
        jsr DrawPic
.np:    stz BoxFace
        lda TsPort              ; the talker's portrait inside, on the left
        cmp #1
        bne .w
        lda TalkPortrait
        cmp #$FFFF
        beq .w
        sta SprId
        lda TsBX
        clc
        adc #4
        and #$FFFE
        sta CellX
        lda TsBY
        clc
        adc #4
        sta CellY
        jsr DrawPic
        lda PortW
        clc
        adc #6
        sta BoxFace
.w:     lda TsBX                ; the words inside
        clc
        adc #4
        clc
        adc BoxFace
        and #$FFFE
        sta TextX
        lda TsBY
        clc
        adc #2
        sta TextY
        lda TsBW
        sec
        sbc #8
        sec
        sbc BoxFace
        and #$FFFE
        cmp #16
        bcs .ww
        lda #16
.ww:    sta TextW
        lda TsBH
        sec
        sbc #4
        cmp #LINE_H+2
        bcs .hh
        lda #LINE_H+2
.hh:    sta TextH
        lda TsTCol
        cmp #$FFFF
        beq .tc
        sta TxtColor
.tc:    jmp TextInit

; The talk is over: the box gone (the HUD and the Game View back), the text area as it was.
CloseBox:
        stz TsTalk
        lda BoxOn
        beq .o
        stz BoxOn
        lda SvTX
        sta TextX
        lda SvTY
        sta TextY
        lda SvTW
        sta TextW
        lda SvTH
        sta TextH
        lda SvTC
        sta TxtColor
        lda SvPX
        sta PenX
        lda SvPY
        sta PenY
        jsr BoxRect
        jsr RestoreRect
        jmp DrawView
.o:     rts

; RX RY RW RH = the box (x and width even, on the screen).
BoxRect:
        lda TsBX
        and #$FFFE
        sta RX
        lda TsBY
        sta RY
        lda TsBW
        and #$FFFE
        sta RW
        lda TsBH
        sta RH
        rts

; A line of the text area cleared (ScrollText, TalkKey's erase): the box's colour in a box, else the HUD's picture.
ClearText:
        lda BoxOn
        beq .hud
        lda TsBCol
        jmp FillRect
.hud:   jmp RestoreRect

; RX RY RW RH (x and width even) in colour A (0-15).
FillRect:
        and #$000F
        sta FillB
        asl a
        asl a
        asl a
        asl a
        ora FillB
        sta FillB
        jsr CurOff
        lda RW
        beq .d
        lda RH
        beq .d
.row:   lda RY
        asl a
        asl a
        asl a
        asl a
        asl a
        sta T5
        asl a
        asl a
        clc
        adc T5
        sta T5
        lda RX
        lsr a
        clc
        adc T5
        clc
        adc #$2000
        sta RScr
        lda #$00E1
        sta RScr+2
        lda RW
        lsr a
        tax
        ldy #0
        sep #$20
        .a8
        lda FillB
.b:     sta [RScr],y
        iny
        dex
        bne .b
        rep #$20
        .a16
        inc RY
        dec RH
        bne .row
.d:     rts

; RunTalk ASK with the style's list: each reply's word, "1. name"..., a line each (at most 9).
ListReplies:
        lda TsOn
        bne .fb1
        brl .o
.fb1:
        lda TsAns
        beq .o
        ldy AskPC
        iny
        lda [TalkPtr],y
        and #$00FF
        cmp #10
        bcc .c
        lda #9
.c:     sta LrN
        iny
        sty LrY
        stz LrI
.l:     lda LrI
        cmp LrN
        bcs .o
        clc
        adc #'1'
        sep #$20
        .a8
        sta LrBuf
        lda #'.'
        sta LrBuf+1
        lda #' '
        sta LrBuf+2
        rep #$20
        .a16
        ldy LrY
        lda [TalkPtr],y
        and #$00FF
        sta LrLen
        iny
        ldx #3
.cp:    lda LrLen
        beq .cd
        cpx #30
        bcs .sk
        lda [TalkPtr],y
        sep #$20
        .a8
        sta LrBuf,x
        rep #$20
        .a16
        inx
.sk:    iny
        dec LrLen
        bra .cp
.cd:    sep #$20
        .a8
        lda #0
        sta LrBuf,x
        rep #$20
        .a16
        iny                     ; past its place
        iny
        sty LrY
        lda #LrBuf
        jsr PrintMine
        jsr NewLine
        inc LrI
        bra .l
.o:     rts

; TalkKey first: the style's list - a digit picks that reply (Both: only with nothing typed), list only: Enter picks the
; first, other letters nothing. Carry set: the key was taken - A 1: a reply picked (TalkKey: Submit).
ListKey:
        ldx TsOn
        beq .no
        ldx TsAns
        beq .no
        cmp #'1'
        bcc .nd
        cmp #'9'+1
        bcs .nd
        ldx InLen
        bne .no
        sec
        sbc #'1'
        jsr PickNth
        sec
        rts
.nd:    ldx TsAns
        cpx #1
        bne .no
        cmp #$1B
        beq .no
        cmp #$0D
        bne .took
        ldx InLen
        bne .no
        lda #0
        jsr PickNth
        sec
        rts
.took:  lda #0
        sec
        rts
.no:    clc
        rts

; A = a reply's number (0...): its word as if typed and written; A 1 (0: no such reply).
PickNth:
        sta LrI
        ldy AskPC
        iny
        lda [TalkPtr],y
        and #$00FF
        cmp LrI
        beq .o
        bcc .o
        iny
.f:     lda LrI
        beq .got
        lda [TalkPtr],y         ; past one: its length byte, its word, its place
        and #$00FF
        clc
        adc #3
        sta LrLen
        tya
        clc
        adc LrLen
        tay
        dec LrI
        bra .f
.got:   lda [TalkPtr],y
        and #$00FF
        cmp #30
        bcc .l
        lda #30
.l:     sta InLen
        iny
        ldx #0
.cp:    cpx InLen
        bcs .e
        lda [TalkPtr],y
        sep #$20
        .a8
        sta InBuf,x
        rep #$20
        .a16
        iny
        inx
        bra .cp
.e:     sep #$20
        .a8
        lda #0
        sta InBuf,x
        rep #$20
        .a16
        lda #InBuf
        jsr PrintMine
        lda #1                  ; (TalkKey: Submit takes the talk on from here)
        rts
.o:     lda #0
        rts

TsTalk:   .dw 0
BoxOn:    .dw 0
BoxFace:  .dw 0
SvTX:     .dw 0
SvTY:     .dw 0
SvTW:     .dw 0
SvTH:     .dw 0
SvTC:     .dw 0
SvPX:     .dw 0
SvPY:     .dw 0
FillB:    .dw 0
LrN:      .dw 0
LrI:      .dw 0
LrY:      .dw 0
LrLen:    .dw 0
LrBuf:    .ds 34
