; Sandcastle RPG Maker - Apple IIgs player: the mouse, as the Windows Ultima 6 engine (U6GameWindow.U6Levels.cs "The
; mouse": a click on the map looks, from any distance; a double click - someone: talk (in combat: attack), a thing: use).
; A verb's cursor up: a click on the map picks that square, on the inventory a cell; the inventory's cursor up: a cell,
; or the map (for the verbs that go there). The HUD's command buttons: a click is their verb's key. The title menu: a
; click picks the choice. "<more>", Press a key, Acknowledgments, the Disk Swap screen: a click goes on.
; QuickDraw's pointer is shown; it is hidden while the player draws straight into the screen (CurOff), back at the
; main loop (CurOn).

ParseButtons:
        lda [Ptr]               ; line of sight on (sight.s)
        sta SightOn
        lda #2
        jsr AddPtr
        jsr ParseWeapons        ; (missile.s)
        lda [Ptr]
        sta BtnCount
        lda #2
        jsr AddPtr
        ldy #0
.l:     cpy #160
        bcs .d
        lda [Ptr],y
        sta BtnTab,y
        iny
        iny
        bra .l
.d:     lda BtnCount
        asl a
        sta T4
        asl a
        asl a
        clc
        adc T4                  ; 10 bytes each
        jmp AddPtr

CurOff:
        lda CurHid
        bne .o
        inc CurHid
        tool $9004              ; HideCursor
.o:     rts

CurOn:
        lda CurHid
        beq .o
        stz CurHid
        tool $9104              ; ShowCursor
.o:     rts

; A click (EvRec): carry set and A = the key it stands for, or carry clear (done, or nothing).
MouseDown:
        lda EvRec+12
        sta MH
        lda EvRec+10
        sta MV
        lda Mode
        cmp #12
        bne .m1
        jmp TitleClick
.m1:    cmp #13                 ; Acknowledgments, Press a key, <more>: go on
        beq .any
        cmp #7
        beq .any
        cmp #10
        beq .any
        cmp #4
        bne .m2
        jmp CursorClick
.m2:    cmp #5
        bne .m3
        jmp GridClick
.m3:    cmp #0
        bne .no
        jmp WalkClick
.any:   lda #' '
        sec
        rts
.no:    clc
        rts

; The title menu: which choice's line.
TitleClick:
        stz TK
        lda #TITLE_Y-10
        sta TY0
.l:     lda TK
        jsr Offered
        bcc .n
        lda MV
        cmp TY0
        bcc .n2
        sec
        sbc TY0
        cmp #12
        bcs .n2
        lda TK
        sta TitlePick
        lda #$0D
        sec
        rts
.n2:    lda TY0
        clc
        adc #12
        sta TY0
.n:     inc TK
        lda TK
        cmp #4
        bcc .l
        clc
        rts

; Carry set if the click is on the view: ClickX, ClickY its square.
InView:
        lda MH
        sec
        sbc VX
        bmi .no
        lsr a
        lsr a
        lsr a
        lsr a
        cmp VCols
        bcs .no
        clc
        adc PX
        sec
        sbc HalfC
        sta ClickX
        lda MV
        sec
        sbc VY
        bmi .no
        lsr a
        lsr a
        lsr a
        lsr a
        cmp VRows
        bcs .no
        clc
        adc PY
        sec
        sbc HalfR
        sta ClickY
        sec
        rts
.no:    clc
        rts

; Carry set if the click is on one of the grid's cells (the inventory view): A = its place.
InGrid:
        lda PanelView
        bne .no
        lda GridCols
        beq .no
        ldx #0
.l:     txa
        lsr a
        cmp InvCount
        bcs .no
        lda MH
        sec
        sbc InvRX,x
        bmi .n
        cmp GridCell
        bcs .n
        lda MV
        sec
        sbc InvRY,x
        bmi .n
        cmp GridCell
        bcs .n
        txa
        lsr a
        sec
        rts
.n:     inx
        inx
        bra .l
.no:    clc
        rts

; A verb's cursor on the map: the square clicked, or (Look, Use, Move) a cell of the grid.
CursorClick:
        jsr InView
        bcc .grid
        lda ClickX
        sta CurX
        lda ClickY
        sta CurY
        lda #$0D
        sec
        rts
.grid:  jsr InGrid
        bcc .no
        pha
        jsr CursorToGrid
        pla
        ldx Mode
        cpx #5
        bne .no
        sta InvSel
        lda #$0D
        sec
        rts
.no:    clc
        rts

; The grid's cursor: a cell, or (not Drop's first pick) the map's square.
GridClick:
        jsr InGrid
        bcc .map
        sta InvSel
        lda #$0D
        sec
        rts
.map:   lda Verb
        cmp #VERB_DROP
        beq .no
        jsr InView
        bcc .no
        lda ClickX
        sta CurX
        lda ClickY
        sta CurY
        lda #4
        sta Mode
        jsr RefreshInventory
        lda #$0D
        sec
        rts
.no:    clc
        rts

; Walking: a command button; the map - Look (a double click on the same square: Talk / Attack / Use); the grid: Look.
WalkClick:
        jsr LiveClick           ; a live button (in a custom panel, or a HUD command's - hudlive.s)
        bcc .lc
        rts
.lc:    ldx #0                  ; the HUD's command buttons
.b:     txa
        cmp BtnCount
        bcs .nb
        asl a
        sta T4
        asl a
        asl a
        clc
        adc T4
        tay
        lda MH
        sec
        sbc BtnTab+2,y
        bmi .nx
        cmp BtnTab+6,y
        bcs .nx
        lda MV
        sec
        sbc BtnTab+4,y
        bmi .nx
        cmp BtnTab+8,y
        bcs .nx
        lda BtnTab,y            ; its verb's key
        sec
        rts
.nx:    inx
        bra .b
.nb:    jsr InView
        bcs .map
        jsr InGrid
        bcc .no
        pha                     ; the grid: Look at it
        lda #VERB_LOOK
        jsr StartVerb
        jsr CursorToGrid
        pla
        sta InvSel
        lda #$0D
        sec
        rts
.no:    clc
        rts
.map:   pha                     ; a double click: the same square within half a second
        pha
        tool $2503              ; GetTick
        pla
        sta T4
        pla
        lda T4
        sec
        sbc ClickTick
        sta T5
        lda T4
        sta ClickTick
        lda T5
        cmp #30
        bcs .single
        lda ClickX
        cmp LastCX
        bne .single
        lda ClickY
        cmp LastCY
        bne .single
        lda ClickX              ; what is there: someone - talk (in combat: attack), a thing - use
        sta CurX
        lda ClickY
        sta CurY
        jsr WhatIsThere
        lda Kind
        cmp #2
        bne .thing
        ldx #VERB_TALK
        lda CombatOn
        beq .verb
        ldx #VERB_ATTACK
        bra .verb
.thing: cmp #3
        bne .single
        ldx #VERB_USE
.verb:  txa
        jsr StartVerb
        bra .pick
.single: lda ClickX
        sta LastCX
        lda ClickY
        sta LastCY
        lda #VERB_LOOK          ; a click: Look at that square
        jsr StartVerb
.pick:  lda ClickX
        sta CurX
        lda ClickY
        sta CurY
        lda #$0D
        sec
        rts

CurHid:   .dw 0
MH:       .dw 0
MV:       .dw 0
ClickX:   .dw 0
ClickY:   .dw 0
LastCX:   .dw $FFFF
LastCY:   .dw $FFFF
ClickTick: .dw 0
BtnCount: .dw 0
BtnTab:   .ds 160
