; Sandcastle RPG Maker - Apple IIgs player: the HUD's live pieces and custom panels (U6GameWindow.HudPanels.cs; the user,
; 2026-11-26: serious power for the HUD, and loading from disk is fine). The editor bakes the HUD's picture without them:
;  - live pieces: every Label, every piece in a custom panel that does something (a button), every piece a HUD command names -
;    drawn by the player: shown or hidden, moved, another picture, (a Label) other words; a button's click is its key;
;  - custom panels (every Panel after the first): their picture (the panel with what is baked in it) is the file HUDP.n on
;    the disk, loaded when the panel opens (FindFile: its folder, its disk, the Disk Swap screen) and freed when it closes;
;    opened and closed by their key (not while the controls are off) or Show / Hide HUD Panel; Enable / Disable Panel Key.
; What the commands change is kept in saves (HlLvHide... HlPnSt in SCRIPTDATA). A panel over the Game View is drawn again
; after each blit of the view (BlitView -> LiveOverView).

LV_N = 24
PN_N = 8
LV_SIZE = 50                    ; kind, panel, x, y, w, h, sprite, key, colour (words), the words (32 bytes)

; GAME.DAT after the colours (actkeys.s ParseKeys): the panels (x, y, w, h, key, open at the start), the live pieces.
ParseLive:
        lda [Ptr]
        sta HlPnN
        lda #2
        jsr AddPtr
        lda HlPnN
        asl a
        sta HlLvT
        asl a
        clc
        adc HlLvT
        asl a
        sta HlLvT                 ; 12 bytes each
        ldy #0
.p:     cpy HlLvT
        bcs .pd
        lda [Ptr],y
        sta HlPnTab,y
        iny
        iny
        bra .p
.pd:    lda HlLvT
        jsr AddPtr
        lda [Ptr]
        sta HlLvN
        lda #2
        jsr AddPtr
        lda Ptr
        sta HlLvPtr
        lda Ptr+2
        sta HlLvPtr+2
        rts

; HlLvE = live piece A's record (from GAME.DAT); HlLvI2 = A.
HlLvLoad:
        sta HlLvI2
        asl a
        sta HlLvT
        asl a
        asl a
        asl a
        sta HlLvT2
        asl a
        clc
        adc HlLvT2
        clc
        adc HlLvT
        clc
        adc HlLvPtr
        sta Src
        lda HlLvPtr+2
        adc #0
        sta Src+2
        ldy #0
.c:     lda [Src],y
        sta HlLvE,y
        iny
        iny
        cpy #LV_SIZE
        bcc .c
        rts

; HlLvCX, HlLvCY = its place now (moved, else its own); RX RY RW RH its rectangle (x and width even).
HlLvPlace:
        lda HlLvI2
        asl a
        tax
        lda HlLvX,x
        beq .ox
        dec a
        bra .sx
.ox:    lda HlLvE+4
.sx:    sta HlLvCX
        lda HlLvY,x
        beq .oy
        dec a
        bra .sy
.oy:    lda HlLvE+6
.sy:    sta HlLvCY
        lda HlLvCX
        and #$FFFE
        sta RX
        lda HlLvCY
        sta RY
        lda HlLvE+8
        clc
        adc #3
        and #$FFFE
        sta RW
        lda HlLvE+10
        sta RH
        rts

; Panel A open now (carry set): Show / Hide HUD Panel's, its key's, else as ticked (Open at the start).
HlPnIsOpen:
        asl a
        tax
        lda HlPnSt,x
        beq .own
        cmp #1
        beq .y
        clc
        rts
.own:   txa
        sta HlLvT
        asl a
        clc
        adc HlLvT
        asl a                   ; (x2 -> x12)
        tax
        lda HlPnTab+10,x
        bne .y
        clc
        rts
.y:     sec
        rts

; Live piece HlLvI2 (loaded) shown now (carry set): not hidden, and its panel (if in one) open and loaded.
HlLvShown:
        lda HlLvI2
        asl a
        tax
        lda HlLvHide,x
        bne .n
        lda HlLvE+2
        beq .y
        dec a
        sta HlLvT3
        jsr HlPnIsOpen
        bcc .n
        lda HlLvT3
        asl a
        asl a
        tax
        lda HlPnAddr,x
        ora HlPnAddr+2,x
        beq .n
.y:     sec
        rts
.n:     clc
        rts

; Live piece A drawn, if shown: its picture now, or (a Label) its words now.
HlLvDraw:
        jsr HlLvLoad
        jsr HlLvShown
        bcs .go
        rts
.go:    jsr HlLvPlace
        jsr HlLvOverCheck
        lda HlLvE
        cmp #2
        beq .label
        lda HlLvI2
        asl a
        tax
        lda HlLvSpr,x
        beq .own
        dec a
        bra .s
.own:   lda HlLvE+12
.s:     cmp #$FFFF
        beq .o
        sta SprId
        lda HlLvCX
        and #$FFFE
        sta CellX
        lda HlLvCY
        sta CellY
        jsr DrawPic
.o:     rts
.label: jsr HlLvWords             ; HlLvBuf = its words now
        lda HlLvCX
        sta SqLnX
        lda HlLvCY
        sta SqLnY
        lda HlLvE+8
        sta SqLnW
        lda HlLvE+16
        sta SqLnCol
        lda #HlLvBuf
        jmp SceneLine           ; (scene.s)

; HlLvBuf = live piece HlLvI2's words: Set HUD Piece Text's (HlLvTxt), else its own.
HlLvWords:
        ldx #0
.f:     lda HlLvTxtOwn,x
        dec a
        cmp HlLvI2
        beq .set
        inx
        inx
        cpx #16
        bcc .f
        ldx #0                  ; its own
        sep #$20
        .a8
.o:     lda HlLvE+18,x
        sta HlLvBuf,x
        beq .od
        inx
        cpx #31
        bcc .o
        lda #0
        sta HlLvBuf,x
.od:    rep #$20
        .a16
        rts
.set:   txa                     ; slot x/2: HlLvTxt + slot * 32
        asl a
        asl a
        asl a
        asl a
        tax
        ldy #0
        sep #$20
        .a8
.s:     lda HlLvTxt,x
        sta HlLvBuf,y
        beq .sd
        inx
        iny
        cpy #31
        bcc .s
        lda #0
        sta HlLvBuf,y
.sd:    rep #$20
        .a16
        rts

; RX RY RW RH over the Game View: HlLvOver set (LiveOverView draws again after each blit of the view).
HlLvOverCheck:
        lda RX
        clc
        adc RW
        cmp VX
        bcc .n
        beq .n
        lda VCols
        asl a
        asl a
        asl a
        asl a
        clc
        adc VX
        cmp RX
        bcc .n
        beq .n
        lda RY
        clc
        adc RH
        cmp VY
        bcc .n
        beq .n
        lda VRows
        asl a
        asl a
        asl a
        asl a
        clc
        adc VY
        cmp RY
        bcc .n
        beq .n
        lda #1
        sta HlLvOver
.n:     rts

; Every live piece on the HUD itself, then each open panel (loaded the first time) with its own pieces.
LiveAll:
        lda HlLvN
        ora HlPnN
        bne .go
        rts
.go:    stz HlLvOver
        stz HlLvI
.m:     lda HlLvI
        cmp HlLvN
        bcs .p
        jsr HlLvLoad
        lda HlLvE+2
        bne .mn
        lda HlLvI
        jsr HlLvDraw
.mn:    inc HlLvI
        bra .m
.p:     stz HlPnI
.q:     lda HlPnI
        cmp HlPnN
        bcs .d
        jsr HlPnIsOpen
        bcc .c
        lda HlPnI
        jsr HlPnEnsure
        bcs .nx
        lda HlPnI
        jsr PanelBlit
        stz HlLvI                 ; its pieces
.pp:    lda HlLvI
        cmp HlLvN
        bcs .nx
        jsr HlLvLoad
        lda HlLvE+2
        dec a
        cmp HlPnI
        bne .pn
        lda HlLvI
        jsr HlLvDraw
.pn:    inc HlLvI
        bra .pp
.c:     lda HlPnI
        jsr HlPnFree
.nx:    inc HlPnI
        bra .q
.d:     rts

; After a blit of the Game View (look.s BlitView): what lies over it drawn again.
LiveOverView:
        lda HlLvOver
        beq .o
        jmp LiveAll
.o:     rts

; Panel A's picture loaded (HUDP.n) if it isn't. Carry set: not to be had.
HlPnEnsure:
        sta HlPnJ
        asl a
        asl a
        tax
        lda HlPnAddr,x
        ora HlPnAddr+2,x
        beq .load
        clc
        rts
.load:  lda #$5548              ; "HUDP." + its number
        sta FileName
        lda #$5044
        sta FileName+2
        lda HlPnJ
        clc
        adc #'1'
        and #$00FF
        xba
        ora #'.'
        sta FileName+4
        stz FileName+6
        jsr FindFile
        bcc .got
        rts
.got:   lda HlPnJ
        asl a
        asl a
        tax
        lda LoadAddr
        sta HlPnAddr,x
        lda LoadAddr+2
        sta HlPnAddr+2,x
        lda LoadHandle
        sta HlPnHandle,x
        lda LoadHandle+2
        sta HlPnHandle+2,x
        clc
        rts

; Panel A's picture freed (closed).
HlPnFree:
        asl a
        asl a
        tax
        lda HlPnAddr,x
        ora HlPnAddr+2,x
        beq .o
        stz HlPnAddr,x
        stz HlPnAddr+2,x
        lda HlPnHandle+2,x
        pha
        lda HlPnHandle,x
        pha
        tool $1002              ; DisposeHandle
.o:     rts

; Panel A's picture onto the screen at its place.
PanelBlit:
        sta HlPnJ
        jsr CurOff
        lda HlPnJ
        asl a
        asl a
        tax
        lda HlPnAddr,x
        sta RSrc
        lda HlPnAddr+2,x
        sta RSrc+2
        jsr HlPnRect
        jsr HlLvOverCheck
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
.b:     lda [RSrc],y
        sta [RScr],y
        iny
        dex
        bne .b
        rep #$20
        .a16
        lda RW
        lsr a
        clc
        adc RSrc
        sta RSrc
        lda RSrc+2
        adc #0
        sta RSrc+2
        inc RY
        dec RH
        bne .row
        rts

; RX RY RW RH = panel HlPnJ's rectangle.
HlPnRect:
        lda HlPnJ
        asl a
        sta HlLvT
        asl a
        clc
        adc HlLvT
        asl a
        tax
        lda HlPnTab,x
        sta RX
        lda HlPnTab+2,x
        sta RY
        lda HlPnTab+4,x
        sta RW
        lda HlPnTab+6,x
        sta RH
        rts

; Something changed: its old place (HlLvOR*) back as the HUD has it, the view and the panel again, then the live pieces.
HlLvChanged:
        lda HlLvORX
        sta RX
        lda HlLvORY
        sta RY
        lda HlLvORW
        sta RW
        lda HlLvORH
        sta RH
        jsr RestoreRect
        jsr DrawView
        jmp DrawPanel           ; (inventory.s: then LiveAll)

HlLvKeepRect:
        lda RX
        sta HlLvORX
        lda RY
        sta HlLvORY
        lda RW
        sta HlLvORW
        lda RH
        sta HlLvORH
        rts

; A = a key while walking (before the keys' actions): a custom panel's key (not switched off) opens or closes it.
; Carry set: taken. The controls off (Cinema Mode, Forbid Controls, a scene): nothing.
PanelKey:
        sta HlLvT3
        cmp #'A'
        bcc .k
        cmp #'Z'+1
        bcs .k
        ora #$20
        sta HlLvT3
.k:     jsr NoControls
        bcs .no
        stz HlPnJ
.l:     lda HlPnJ
        cmp HlPnN
        bcs .no
        asl a
        tax
        lda HlPnKeyOff,x
        bne .n
        txa
        sta HlLvT
        asl a
        clc
        adc HlLvT
        asl a
        tax
        lda HlPnTab+8,x
        beq .n
        cmp HlLvT3
        bne .n
        lda #2                  ; toggled
        ldx HlPnJ
        jsr HlPnSet
        sec
        rts
.n:     inc HlPnJ
        bra .l
.no:    clc
        rts

; Panel X: A 0 open, 1 closed, 2 toggled - drawn again.
HlPnSet:
        stx HlPnK
        sta HlLvT3
        txa
        jsr HlPnIsOpen
        lda #0
        rol a
        sta HlLvT                 ; open now
        lda HlLvT3
        cmp #2
        bne .v
        lda HlLvT                 ; toggled: the other
        beq .op
        bra .cl
.v:     cmp #0
        bne .cl
.op:    lda #1
        bra .st
.cl:    lda #2
.st:    pha
        lda HlPnK
        sta HlPnJ
        jsr HlPnRect
        jsr HlLvKeepRect
        lda HlPnK
        asl a
        tax
        pla
        sta HlPnSt,x
        jmp HlLvChanged

; EXT 46 HUD mode index ... (hudlive.s): 0 SHOW i v (5 bytes; v 0 shown, 1 hidden, 2 toggled), 1 PICTURE i sprite (6;
; $FFFF its own), 2 TEXT i (4; LogBuf's words), 3 MOVE i back x-formula y-formula, 4 PANEL p v (5; 0 open, 1 closed,
; 2 toggled), 5 KEY p on (5).
XtHudCmd:
        stz LogOn
        iny
        lda [TalkPtr],y
        and #$00FF
        sta HlLvMode
        iny
        lda [TalkPtr],y
        and #$00FF
        sta HlLvIdx
        iny
        sty HlLvY0
        lda HlLvMode
        cmp #4
        bcc .piece
        bne .key
        lda [TalkPtr],y         ; PANEL p v
        and #$00FF
        ldx HlLvIdx
        cpx HlPnN
        bcs .p5
        jsr HlPnSet
.p5:    lda #5
        jmp Next
.key:   lda [TalkPtr],y         ; KEY p on
        and #$00FF
        eor #1
        pha
        lda HlLvIdx
        asl a
        tax
        pla
        cpx #PN_N*2
        bcs .k5
        sta HlPnKeyOff,x
.k5:    lda #5
        jmp Next
.piece: lda HlLvIdx               ; its old place first
        cmp HlLvN
        bcc .ok
        brl .skip
.ok:    jsr HlLvLoad
        jsr HlLvPlace
        jsr HlLvKeepRect
        lda HlLvIdx
        asl a
        tax
        ldy HlLvY0
        lda HlLvMode
        bne .m1
        lda [TalkPtr],y         ; SHOW
        and #$00FF
        cmp #2
        bne .sv
        lda HlLvHide,x
        eor #1
        bra .sh
.sv:    cmp #1
        beq .sh
        lda #0
.sh:    sta HlLvHide,x
        jsr HlLvChanged
        lda #5
        jmp Next
.m1:    cmp #1
        bne .m2
        lda [TalkPtr],y         ; PICTURE
        inc a                   ; ($FFFF -> 0: its own)
        sta HlLvSpr,x
        jsr HlLvChanged
        lda #6
        jmp Next
.m2:    cmp #2
        bne .m3
        jsr HlLvSetText           ; TEXT
        jsr HlLvChanged
        lda #4
        jmp Next
.m3:    lda [TalkPtr],y         ; MOVE back x y
        and #$00FF
        sta HlLvT3
        iny
        jsr EvalExpr
        cmp #$8000
        bcc .x0
        lda #0
.x0:    inc a
        sta HlLvNX
        jsr EvalExpr
        cmp #$8000
        bcc .y0
        lda #0
.y0:    inc a
        sta HlLvNY
        sty HlLvY0
        lda HlLvIdx
        asl a
        tax
        lda HlLvT3
        beq .mv
        stz HlLvX,x
        stz HlLvY,x
        bra .md
.mv:    lda HlLvNX
        sta HlLvX,x
        lda HlLvNY
        sta HlLvY,x
.md:    jsr HlLvChanged
        ldy HlLvY0
        sty TPC
        jmp RunTalk
.skip:  lda HlLvMode              ; (no such piece: its length)
        cmp #3
        beq .sm
        asl a
        tax
        lda HlLvLen,x
        jmp Next
.sm:    ldy HlLvY0
        iny
        jsr EvalExpr
        jsr EvalExpr
        sty TPC
        jmp RunTalk

; Set HUD Piece Text: LogBuf's words (at most 31) are live piece HlLvIdx's from now on (a slot of HlLvTxt, saved).
HlLvSetText:
        ldx #0
.f:     lda HlLvTxtOwn,x
        dec a
        cmp HlLvIdx
        beq .got
        inx
        inx
        cpx #16
        bcc .f
        ldx #0
.e:     lda HlLvTxtOwn,x
        beq .got
        inx
        inx
        cpx #16
        bcc .e
        rts                     ; (all 8 used)
.got:   lda HlLvIdx
        inc a
        sta HlLvTxtOwn,x
        txa
        asl a
        asl a
        asl a
        asl a
        tax
        ldy #0
        sep #$20
        .a8
.c:     cpy LogLen
        bcs .ce
        cpy #31
        bcs .ce
        lda LogBuf,y
        sta HlLvTxt,x
        inx
        iny
        bra .c
.ce:    lda #0
        sta HlLvTxt,x
        rep #$20
        .a16
        rts

; A click while walking: a shown live button under it - carry set, A its key.
LiveClick:
        stz HlLvI
.l:     lda HlLvI
        cmp HlLvN
        bcs .no
        jsr HlLvLoad
        lda HlLvE+14
        beq .n
        jsr HlLvShown
        bcc .n
        jsr HlLvPlace
        lda MH
        sec
        sbc RX
        bmi .n
        cmp RW
        bcs .n
        lda MV
        sec
        sbc RY
        bmi .n
        cmp RH
        bcs .n
        lda HlLvE+14
        sec
        rts
.n:     inc HlLvI
        bra .l
.no:    clc
        rts

HlLvLen:    .dw 5,6,4,0
HlPnN:      .dw 0
HlPnTab:    .ds PN_N*12
HlPnAddr:   .ds PN_N*4
HlPnHandle: .ds PN_N*4
HlLvN:      .dw 0
HlLvPtr:    .dd 0
HlLvE:      .ds LV_SIZE
HlLvBuf:    .ds 32
HlLvT:      .dw 0
HlLvT2:     .dw 0
HlLvT3:     .dw 0
HlLvI:      .dw 0
HlLvI2:     .dw 0
HlPnI:      .dw 0
HlPnJ:      .dw 0
HlPnK:      .dw 0
HlLvCX:     .dw 0
HlLvCY:     .dw 0
HlLvOver:   .dw 0
HlLvORX:    .dw 0
HlLvORY:    .dw 0
HlLvORW:    .dw 0
HlLvORH:    .dw 0
HlLvMode:   .dw 0
HlLvIdx:    .dw 0
HlLvY0:     .dw 0
HlLvNX:     .dw 0
HlLvNY:     .dw 0
