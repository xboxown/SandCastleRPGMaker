; Sandcastle RPG Maker - Apple IIgs player: the text area and talk (Ultima 6's way, as the Windows Ultima 6 engine).
; Talk: T, then a direction: the one there talks - their portrait and name in the panel, their words in the text area
; (the words to ask about in the keyword colour), "you say:" and a typed word; a reply is picked by its first four
; letters; nothing typed is "bye"; Esc ends the talk. The talk's code: Engine/AppleIIgsTalk.cs.

LINE_H = 10

; ---- the text area ----
; Ready to write at the top of the text area.
TextInit:
        lda TextX
        clc
        adc #2
        sta PenX
        lda TextY
        clc
        adc #9
        sta PenY
        pha                     ; the width of a space
        pea ' '
        tool $A804              ; CharWidth
        pla
        sta SpaceW
        rts

; A = a colour (0-15, $FFFF: the brightest): the pen's colour for words.
SetColor:
        cmp #$FFFF
        bne .c
        lda Bright
.c:     pha
        tool $A004              ; SetForeColor
        rts

; The next line; at the bottom the text area scrolls up a line.
NewLine:
        lda TextX
        clc
        adc #2
        sta PenX
        lda PenY
        clc
        adc #LINE_H
        sta PenY
        lda TextY
        clc
        adc TextH
        sec
        sbc #2
        cmp PenY
        bcs .ok
        jsr ScrollText
        lda PenY
        sec
        sbc #LINE_H
        sta PenY
.ok:    rts

; The text area's pixels up by a line; the last line cleared (the HUD's own picture back there).
ScrollText:
        lda TextY
        sta RY
        lda TextH
        sec
        sbc #LINE_H
        sta RH                  ; rows to move
        beq .clear
        bmi .clear
.row:   lda RY                  ; dst = $2000 + RY * 160 + TextX / 2
        jsr RowOffset
        sta T4
        lda RY
        clc
        adc #LINE_H
        jsr RowOffset           ; src: the row a line below
        tax
        ldy T4
        lda TextW
        lsr a
        dec a
        phb
        mvn $E1,$E1
        plb
        inc RY
        dec RH
        bne .row
.clear: lda TextX
        sta RX
        lda TextY
        clc
        adc TextH
        sec
        sbc #LINE_H
        sta RY
        lda TextW
        sta RW
        lda #LINE_H
        sta RH
        jmp RestoreRect

; A = a screen row -> A = $2000 + row * 160 + TextX / 2
RowOffset:
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
        lda TextX
        lsr a
        clc
        adc T5
        clc
        adc #$2000
        rts

; The HUD's own picture back in RX, RY, RW, RH (x even): from its background, or black without a HUD.
RestoreRect:
        lda RW
        beq .done
        lda RH
        beq .done
.row:   lda RY                  ; offset = RY * 160 + RX / 2
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
        sta T5
        clc
        adc #$2000
        sta RScr
        lda #$00E1
        sta RScr+2
        lda HasHud
        beq .black
        lda HudPtr
        clc
        adc T5
        sta RSrc
        lda HudPtr+2
        adc #0
        sta RSrc+2
        lda RW
        lsr a
        tax                     ; bytes
        ldy #0
        sep #$20
        .a8
.b:     lda [RSrc],y
        sta [RScr],y
        iny
        dex
        bne .b
        rep #$20
        .a16
        bra .next
.black: lda RW
        lsr a
        tax
        ldy #0
        sep #$20
        .a8
        lda #0
.z:     sta [RScr],y
        iny
        dex
        bne .z
        rep #$20
        .a16
.next:  inc RY
        dec RH
        bne .row
.done:  rts

; Writes the C string at TP as one piece (a word, "you say: "): on the next line if it doesn't fit on this one.
PrintWord:
        pha
        lda TP+2
        pha
        lda TP
        pha
        tool $AA04              ; CStringWidth
        pla
        sta PWWidth             ; (not T4/T5: a new line's scroll uses them)
        lda PenX
        clc
        adc PWWidth
        sta PWEnd
        lda TextX
        clc
        adc TextW
        sec
        sbc #2
        cmp PWEnd
        bcs .fit
        lda TextX               ; too wide: a new line, unless this one is still empty
        clc
        adc #2
        cmp PenX
        beq .fit
        jsr NewLine
.fit:   lda PenX
        pha
        lda PenY
        pha
        tool $3A04              ; MoveTo
        lda TP+2
        pha
        lda TP
        pha
        tool $A604              ; DrawCString
        lda PenX
        clc
        adc PWWidth
        sta PenX
        rts

; A = a word's address in this program (a C string): written in the text colour.
PrintMine:
        sta TP
        lda MyBank
        sta TP+2
        lda TxtColor
        jsr SetColor
        jmp PrintWord

; A = which of GAME.DAT's words (0 you say, 1 bye, 2 no help, 3 funny no response, 4 talk, 5 nothing, 6 talking to yourself)
PrintSaid:
        asl a
        asl a
        tax
        lda WordTab,x
        sta TP
        sta SayP
        lda WordTab+2,x
        sta TP+2
        sta SayP+2
        cpx #0                  ; "you say: " stays one piece, with what is typed after it
        beq .whole
        ldy #0
        jmp SayText             ; Ultima 6's other words wrap, word by word
.whole: lda TxtColor
        jsr SetColor
        jmp PrintWord

; The prompt on a new line.
PromptLine:
        jsr NewLine
        lda #Prompt
        jmp PrintMine

; Writes words: the text at SayP + Y (a C string; \1 word \2 a keyword, \n a new line), word by word.
SayText:
        sty TxtAt
.word:  ldx #0
        stz WordKey
.ch:    ldy TxtAt
        lda [SayP],y
        and #$00FF
        beq .last
        cmp #$0A
        beq .last
        cmp #' '
        beq .last
        iny
        sty TxtAt
        cmp #1
        bne .n1
        lda #1
        sta WordKey
        bra .ch
.n1:    cmp #2                  ; a keyword's end: written now, in its colour (what follows - "." - is not)
        bne .n2
        dec TxtAt               ; .sep steps over it
        bra .last
.n2:    cpx #60
        bcs .ch
        sep #$20
        .a8
        sta WBuf,x
        rep #$20
        .a16
        inx
        bra .ch
.last:  sta WordEnd             ; what ended the word: 0, a new line or a space
        sep #$20
        .a8
        lda #0
        sta WBuf,x
        rep #$20
        .a16
        cpx #0
        beq .sep
        lda TxtColor
        ldy WordKey
        beq .col
        lda KeyColor
.col:   jsr SetColor
        lda #WBuf
        sta TP
        lda MyBank
        sta TP+2
        jsr PrintWord
.sep:   lda WordEnd
        beq .done
        ldy TxtAt
        iny
        sty TxtAt
        cmp #2
        bne .nl
        brl .word
.nl:    cmp #$0A
        bne .space
        jsr NewLine
        brl .word
.space: lda PenX
        clc
        adc SpaceW
        sta PenX
        brl .word
.done:  rts

; ---- talking ----
; T: "Talk-", then a direction.
TalkVerb:
        lda #4
        jsr PrintSaid
        lda #Dash
        jsr PrintMine
        lda #1
        sta Mode
        rts

; Talk at NX, NY (the verb's cursor, verbs.s).
TalkAt:
        stz Mode
        ; who is there?
        lda ChrPtr
        sta ItemPtr
        lda ChrPtr+2
        sta ItemPtr+2
        lda ChrCount
        sta Item
.l:     lda Item
        beq .nobody
        lda [ItemPtr]
        cmp NX
        bne .n
        ldy #2
        lda [ItemPtr],y
        cmp NY
        beq .found
.n:     lda #16
        jsr NextItem
        dec Item
        bra .l
.nobody:
        lda #5                  ; nothing
        jsr PrintSaid
        jmp PromptLine
.found: ldy #12
        lda [ItemPtr],y
        sta TalkPortrait
        ldy #10
        lda [ItemPtr],y
        cmp #$FFFF
        bne .talks
        ldy #14                 ; Talk-Name, then: funny, no response
        lda [ItemPtr],y
        jsr NameNumber
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        lda TxtColor
        jsr SetColor
        jsr PrintWord
        jsr NewLine
        lda #3
        jsr PrintSaid
        jmp PromptLine
.talks: asl a                   ; TalkPtr = TalkBank + TalkTab[talk]
        asl a
        tay
        lda [TalkTab],y
        sta T4
        iny
        iny
        lda [TalkTab],y
        sta T5
        clc
        lda TalkBank
        adc T4
        sta TalkPtr
        lda TalkBank+2
        adc T5
        sta TalkPtr+2
        lda TalkPtr             ; their name: a C string after its length
        clc
        adc #1
        sta NamePtr
        lda TalkPtr+2
        adc #0
        sta NamePtr+2
        lda NamePtr
        sta TP
        lda NamePtr+2
        sta TP+2
        lda TxtColor
        jsr SetColor
        jsr PrintWord           ; Talk-Name
        jsr NewLine
        lda [TalkPtr]           ; the code after the name
        and #$00FF
        clc
        adc #2
        clc
        adc TalkPtr
        sta TalkPtr
        lda TalkPtr+2
        adc #0
        sta TalkPtr+2
        jsr ShowTalker
        stz TPC
        lda #3
        sta Mode
        jmp RunTalk

; The talker's portrait and name in the panel.
ShowTalker:
        lda PortW
        beq .name
        lda PortX
        sta RX
        lda PortY
        sta RY
        lda PortW
        sta RW
        lda PortH
        sta RH
        jsr RestoreRect
        lda TalkPortrait
        cmp #$FFFF
        beq .name
        sta SprId
        lda PortX
        sta CellX
        lda PortY
        sta CellY
        jsr DrawPic
.name:  lda NameW
        beq .done
        lda NameX
        sta RX
        lda NameY
        sta RY
        lda NameW
        sta RW
        lda NameH
        sta RH
        jsr RestoreRect
        lda TxtColor
        jsr SetColor
        lda NameX
        pha
        lda NameY
        clc
        adc #8
        pha
        tool $3A04
        lda NamePtr+2
        pha
        lda NamePtr
        pha
        tool $A604
.done:  rts

; The talk is over: the panel as it was, the prompt.
EndTalk:
        lda PortW
        beq .n
        lda PortX
        sta RX
        lda PortY
        sta RY
        lda PortW
        sta RW
        lda PortH
        sta RH
        jsr RestoreRect
.n:     lda NameW
        beq .l
        lda NameX
        sta RX
        lda NameY
        sta RY
        lda NameW
        sta RW
        lda NameH
        sta RH
        jsr RestoreRect
.l:     jsr DrawPanel           ; the panel as its view is (inventory.s)
        stz Mode
        jmp PromptLine

; Runs the talk's code from TPC until it asks or ends.
RunTalk:
        ldy TPC
        lda [TalkPtr],y
        and #$00FF
        cmp #1
        beq .say
        cmp #2
        beq .ask
        cmp #3
        beq .jump
        cmp #4
        bne .n4
        brl .setv
.n4:    cmp #5
        bne .n5
        brl .ifv
.n5:    cmp #6
        beq .play
        cmp #7
        bne .end
        jmp TalkMap
.play:
        iny                     ; PLAY: a sound, its volume
        lda [TalkPtr],y
        pha
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        tax
        pla
        jsr PlaySound
        lda TPC
        clc
        adc #4
        sta TPC
        brl RunTalk
.end:   jmp EndTalk
.say:   iny
        lda [TalkPtr],y
        sta SayLen              ; its length
        iny
        iny
        lda TalkPtr
        sta SayP
        lda TalkPtr+2
        sta SayP+2
        jsr SayText
        jsr NewLine
        lda TPC
        clc
        adc SayLen
        clc
        adc #4
        sta TPC
        brl RunTalk
.ask:   sty AskPC
        lda #0
        jsr PrintSaid           ; you say:
        stz InLen
        lda #2
        sta Mode
        rts
.jump:  iny
        lda [TalkPtr],y
        sta TPC
        brl RunTalk
.setv:  iny
        lda [TalkPtr],y         ; the variable
        asl a
        tax
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta T4                  ; 0 set, 1 add, 2 subtract
        iny
        lda [TalkPtr],y
        ldy T4
        beq .set
        cpy #1
        bne .sub
        clc
        adc VarTab,x
        bra .set
.sub:   eor #$FFFF
        sec
        adc VarTab,x
.set:   sta VarTab,x
        lda TPC
        clc
        adc #6
        sta TPC
        brl RunTalk
.ifv:   iny
        lda [TalkPtr],y
        asl a
        tax
        lda VarTab,x
        sta T4                  ; the variable's value
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta T5                  ; the test
        iny
        lda [TalkPtr],y
        sta T2                  ; the value
        jsr Compare             ; T3: 1 equal, 2 less, 0 more
        ldx T5
        lda T3
        cpx #0
        beq .eq
        cpx #1
        beq .ne
        cpx #2
        beq .gt
        cpx #3
        beq .lt
        cpx #4
        beq .ge
        cmp #0                  ; <=: equal or less
        bne .yes
        bra .no
.eq:    cmp #1
        beq .yes
        bra .no
.ne:    cmp #1
        bne .yes
        bra .no
.gt:    cmp #0
        beq .yes
        bra .no
.lt:    cmp #2
        beq .yes
        bra .no
.ge:    cmp #2
        bne .yes
.no:    ldy TPC
        tya
        clc
        adc #6
        tay
        lda [TalkPtr],y
        sta TPC
        brl RunTalk
.yes:   lda TPC
        clc
        adc #8
        sta TPC
        brl RunTalk

; MAP in the code (Y on it): to that map and spot (Change Map).
TalkMap:
        iny                     ; MAP: a map, x, y
        lda [TalkPtr],y
        sta GoMap
        iny
        iny
        lda [TalkPtr],y
        sta GoX
        iny
        iny
        lda [TalkPtr],y
        sta GoY
        lda TPC
        clc
        adc #7
        sta TPC
        lda GoMap
        cmp #$FFFF
        beq .same
        cmp CurMap
        beq .same
        jsr ChangeMap           ; another map: what comes after doesn't run (as Windows)
        jsr DrawView
        jmp EndTalk
.same:  jsr ChangeMap
        jsr DrawView
        jmp RunTalk

; T3 = how T4 stands to T2 (signed): 1 equal, 2 less, 0 more.
Compare:
        lda T4
        cmp T2
        bne .ne
        lda #1
        sta T3
        rts
.ne:    lda T4
        sec
        sbc T2
        bvc .nv
        eor #$8000
.nv:    bmi .less
        stz T3
        rts
.less:  lda #2
        sta T3
        rts

; A key while the player types the word to say.
TalkKey:
        cmp #$1B
        bne .k1
        jsr NewLine
        jmp EndTalk
.k1:    cmp #$0D
        bne .k2
        jmp Submit
.k2:    cmp #$7F
        beq .del
        cmp #$08
        beq .del
        cmp #' '
        bcc .out
        cmp #$7F
        bcs .out
        ldx InLen
        cpx #30
        bcs .out
        cmp #'A'
        bcc .st
        cmp #'Z'+1
        bcs .st
        ora #$20                ; lower case, as the keywords
.st:    sep #$20
        .a8
        sta InBuf,x
        rep #$20
        .a16
        inc InLen
        and #$00FF
        pha                     ; the letter, kept on the stack
        lda TxtColor
        jsr SetColor
        lda PenX
        pha
        lda PenY
        pha
        tool $3A04              ; MoveTo
        lda 1,s
        pha
        tool $A404              ; DrawChar
        pha                     ; CharWidth's result
        lda 3,s
        pha
        tool $A804              ; CharWidth
        pla
        clc
        adc PenX
        sta PenX
        pla                     ; the letter off the stack
.out:   rts
.del:   lda InLen
        beq .out
        dec InLen
        lda TextX               ; the line again: "you say: " and what is left
        sta RX
        lda PenY
        sec
        sbc #8
        sta RY
        lda TextW
        sta RW
        lda #LINE_H
        sta RH
        jsr RestoreRect
        lda TextX
        clc
        adc #2
        sta PenX
        lda #0
        jsr PrintSaid
        ldx InLen
        sep #$20
        .a8
        lda #0
        sta InBuf,x
        rep #$20
        .a16
        lda InLen
        beq .out
        lda #InBuf
        jmp PrintMine

; Enter: the typed word picks a reply by its first four letters; nothing typed is "bye".
Submit:
        jsr NewLine
        lda InLen
        bne .typed
        ldx #0                  ; "bye"
.bye:   lda ByeWord,x
        sta InBuf,x
        inx
        inx
        cpx #4
        bne .bye
        lda #3
        sta InLen
.typed: ldy AskPC
        iny
        lda [TalkPtr],y
        and #$00FF
        sta Count               ; how many replies
        iny
        sty T2                  ; the first reply
.r:     lda Count
        beq .none
        ldy T2
        lda [TalkPtr],y
        and #$00FF
        sta T4                  ; its word's length
        iny
        sty T3                  ; its word
        cmp #4                  ; letters to match: at most 4
        bcc .m
        lda #4
.m:     sta T5
        cmp #0
        beq .skip
        cmp InLen
        beq .cmp
        bcs .skip               ; typed shorter than that
.cmp:   ldx #0
        ldy T3
.c:     lda [TalkPtr],y
        and #$00FF
        sta Col
        lda InBuf,x
        and #$00FF
        cmp Col
        bne .skip
        iny
        inx
        cpx T5
        bne .c
        lda T3                  ; picked: its place
        clc
        adc T4
        tay
        lda [TalkPtr],y
        sta TPC
        lda #3
        sta Mode
        jmp RunTalk
.skip:  lda T2                  ; the next reply: length byte + word + place
        clc
        adc T4
        clc
        adc #3
        sta T2
        dec Count
        bra .r
.none:  ldx #0                  ; no reply: "bye" ends the talk, anything else "cannot help"
.nb:    lda InBuf,x
        and #$00FF
        sta Col
        lda ByeWord,x
        and #$00FF
        cmp Col
        bne .help
        inx
        cpx #3
        bne .nb
        lda InLen
        cmp #3
        bne .help
        jmp EndTalk
.help:  lda #2
        jsr PrintSaid
        jsr NewLine
        lda #0
        jsr PrintSaid
        stz InLen
        rts

; Draws sprite SprId as it is (a portrait) at CellX, CellY (x even), colour 0 see-through.
DrawPic:
        lda SprId
        asl a
        asl a
        tay
        lda [SprTab],y
        sta T0
        iny
        iny
        lda [SprTab],y
        sta T1
        clc
        lda SprBank
        adc T0
        sta Src
        lda SprBank+2
        adc T1
        sta Src+2
        lda [Src]
        lsr a
        sta BPR
        sta C1
        stz C0
        ldy #2
        lda [Src],y
        sta Count
        lda #4
        clc
        adc Src
        sta Src
        lda Src+2
        adc #0
        sta Src+2
        lda CellY
        asl a
        asl a
        asl a
        asl a
        asl a
        sta T0
        asl a
        asl a
        clc
        adc T0
        sta T0
        lda CellX
        lsr a
        clc
        adc T0
        clc
        adc #$2000
        sta Scr
        lda #$00E1
        sta Scr+2
        jmp BlitRows
