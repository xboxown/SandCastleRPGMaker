; Sandcastle RPG Maker - the Apple IIgs player (Ultima 6 Apple IIgs mode).
; A GS/OS S16 application in 65816 assembly (assembled by asm816.py). GS/OS and an accelerator required.
;
; Version 2 (b620): objects and characters on the map too (solid ones and characters stop the Avatar).
; Version 1: starts under GS/OS, checks for an accelerator (an Apple IIgs at its own 2.8 MHz gets an alert and the game
; quits), loads GAME.DAT from its own folder, sets the Super Hi-Res palette, draws the world (10 x 10 tiles of 16 x 16)
; with the Avatar in the middle, walks with the arrow keys (walls stop the Avatar), Q or Esc quits back to the desktop.
; GAME.DAT's layout: Engine/AppleIIgs.cs (AppleIIgsGamePack).

; ---- direct page (the ~Direct segment the loader gives us) ----
UserId    = $00
SSRef     = $02   ; 4: StartUpTools' reference
Handle    = $06   ; 4: GAME.DAT's memory
DataPtr   = $0A   ; 4
TilesPtr  = $0E   ; 4
FlagsPtr  = $12   ; 4
MapPtr    = $16   ; 4
AvatarPtr = $1A   ; 4
Src       = $1E   ; 4
Scr       = $22   ; 4
Ptr       = $26   ; 4
MapW      = $2A
MapH      = $2C
PX        = $2E
PY        = $30
Facing    = $32
T0        = $34
T1        = $36
T2        = $38
T3        = $3A
Count     = $3C
Row       = $3E
Col       = $40
MulA      = $42
MB0       = $44
MB1       = $46
MulR      = $48   ; 4
Speed     = $4C
Bright    = $4E
TileCount = $50
NX        = $52
NY        = $54
ViewOff   = $56
SprTab    = $58   ; 4
SprBank   = $5C   ; 4
ObjPtr    = $60   ; 4
ChrPtr    = $64   ; 4
OccPtr    = $68   ; 4: one byte a cell - 1 a solid object, 2 a character
OccHandle = $6C   ; 4
ObjCount  = $70
ChrCount  = $72
SprId     = $74
SX        = $76
SY        = $78
SW        = $7A
SH        = $7C
BPR       = $7E
C0        = $80
C1        = $82
R0        = $84
R1        = $86
Item      = $88
ItemPtr   = $8A   ; 4
ViewX0    = $8E
ViewY0    = $90
CellX     = $92
CellY     = $94

SPEED_NEEDED = 560        ; loop counts in one screen refresh: a 2.8 MHz IIgs makes about 370
VIEW_X    = 80            ; where the world shows: 160 x 160 in the middle of 320 x 200
VIEW_Y    = 20
SCREEN    = $E12000       ; Super Hi-Res pixels
SCB       = $E19D00
PALETTE   = $E19E00

start:
        phk
        plb
        rep #$30
        .a16
        .i16
        sta UserId
        tool $0201              ; TLStartUp
        pha
        tool $0202              ; MMStartUp
        pla
        sta UserId
        jsr MakeMaskTable
        jsr SpeedCheck          ; carry set: accelerated
        php
        ; the tools: QuickDraw II, the Event Manager, the Window Manager (for the alert)...
        pha
        pha
        lda UserId
        pha
        pea 0
        pea #bank(StartStop)
        pea #StartStop
        tool $1801              ; StartUpTools
        pla
        sta SSRef
        pla
        sta SSRef+2
        bcc .tools
        plp
        jmp FailQuit
.tools:
        plp
        bcs .fast
        lda #AlertSlow
        ldx #bank(AlertSlow)
        jsr Alert
        jmp Quit
.fast:
        jsr LoadData
        bcc .loaded
        lda #AlertNoData
        ldx #bank(AlertNoData)
        jsr Alert
        jmp Quit
.loaded:
        jsr ParseData
        bcc .parsed
        lda #AlertBadData
        ldx #bank(AlertBadData)
        jsr Alert
        jmp Quit
.parsed:
        jsr MakeOcc
        bcc .occ
        lda #AlertNoMemory
        ldx #bank(AlertNoMemory)
        jsr Alert
        jmp Quit
.occ:
        tool $9004              ; HideCursor
        jsr SetupScreen
        jsr DrawView
MainLoop:
        pha
        pea $0028               ; key down, auto-key
        pea #bank(EvRec)
        pea #EvRec
        tool $0A06              ; GetNextEvent
        pla
        beq MainLoop
        lda EvMsg
        and #$007F
        cmp #$1B
        beq .quit
        cmp #'q'
        beq .quit
        cmp #'Q'
        beq .quit
        cmp #$0B                ; up arrow
        bne .n1
        lda #0
        sta T2
        lda #$FFFF
        sta T3
        lda #1
        jsr TryMove
        bra MainLoop
.n1:    cmp #$0A                ; down arrow
        bne .n2
        lda #0
        sta T2
        lda #1
        sta T3
        lda #0
        jsr TryMove
        bra MainLoop
.n2:    cmp #$15                ; right arrow
        bne .n3
        lda #1
        sta T2
        lda #0
        sta T3
        lda #2
        jsr TryMove
        bra MainLoop
.n3:    cmp #$08                ; left arrow
        bne MainLoop
        lda #$FFFF
        sta T2
        lda #0
        sta T3
        lda #3
        jsr TryMove
        brl MainLoop
.quit:
        jmp Quit

; ---- leaving ----
Quit:
        rep #$30
        lda Handle
        ora Handle+2
        beq .nohandle
        lda Handle+2
        pha
        lda Handle
        pha
        tool $1002              ; DisposeHandle
.nohandle:
        lda OccHandle
        ora OccHandle+2
        beq .noocc
        lda OccHandle+2
        pha
        lda OccHandle
        pha
        tool $1002
.noocc:
        pea 0
        lda SSRef+2
        pha
        lda SSRef
        pha
        tool $1901              ; ShutDownTools
FailQuit:
        rep #$30
        lda UserId
        pha
        tool $0302              ; MMShutDown
        tool $0301              ; TLShutDown
        gsos $2029,QuitParms    ; back to the desktop
        brk $00

; ---- the accelerator ----
; Counts loops of the same work through one screen refresh (60 a second): an accelerated IIgs makes far more.
SpeedCheck:
        php
        sei
        sep #$20
        .a8
        lda >$E0C036            ; the IIgs's own speed: fast (2.8 MHz), never the 1 MHz setting
        ora #$80
        sta >$E0C036
.w0:    lda >$E0C019
        bmi .w0
.w1:    lda >$E0C019
        bpl .w1
        ldx #0
.l1:    ldy #20
.i1:    dey
        bne .i1
        inx
        beq .over
        lda >$E0C019
        bmi .l1
.l2:    ldy #20
.i2:    dey
        bne .i2
        inx
        beq .over
        lda >$E0C019
        bpl .l2
        rep #$20
        .a16
        stx Speed
        cpx #SPEED_NEEDED
        bcs .yes
        plp
        clc
        rts
.over:  rep #$20
        .a16
        ldx #$FFFF
        stx Speed
.yes:   plp
        sec
        rts

; ---- an alert: A = the text's address, X = its bank (a C string: "42/words/^Quit") ----
Alert:
        sta T0
        stx T1
        tool $9104              ; ShowCursor (the alert needs the pointer)
        pha
        pea 0
        pea 0
        pea 0
        lda T1
        pha
        lda T0
        pha
        tool $590E              ; AlertWindow
        pla
        rts

; ---- GAME.DAT ----
LoadData:
        gsos $2010,OpenParms
        bcc .open
        rts
.open:
        lda OpenRef
        sta EofRef
        sta ReadRef
        sta CloseRef
        gsos $2019,EofParms
        bcs .close
        pha
        pha
        lda EofLen+2
        pha
        lda EofLen
        pha
        lda UserId
        pha
        pea $C000               ; locked, fixed
        pea 0
        pea 0
        tool $0902              ; NewHandle
        pla
        sta Handle
        pla
        sta Handle+2
        bcs .close
        ldy #2
        lda [Handle],y
        sta DataPtr+2
        sta ReadBuf+2
        lda [Handle]
        sta DataPtr
        sta ReadBuf
        lda EofLen
        sta ReadReq
        lda EofLen+2
        sta ReadReq+2
        gsos $2012,ReadParms
.close:
        php
        gsos $2014,CloseParms
        plp
        rts

; Finds every part of GAME.DAT. Carry set: it isn't a Sandcastle game file of a version this player knows.
ParseData:
        lda [DataPtr]
        cmp #$4353              ; "SC"
        bne .bad
        ldy #2
        lda [DataPtr],y
        cmp #$5347              ; "GS"
        bne .bad
        ldy #4
        lda [DataPtr],y
        cmp #2
        beq .known
.bad:   sec
        rts
.known:
        ; the title: offset 40, a length byte and the text -> TitleBuf as a C string
        ldy #40
        lda [DataPtr],y
        and #$00FF
        sta Count
        ldx #0
.t:     cpx Count
        beq .tend
        iny
        sep #$20
        .a8
        lda [DataPtr],y
        sta TitleBuf,x
        rep #$20
        .a16
        inx
        bra .t
.tend:  sep #$20
        .a8
        lda #0
        sta TitleBuf,x
        rep #$20
        .a16
        ; the tiles: count, then count x 128 bytes, then count flag bytes
        lda Count
        clc
        adc #41
        jsr AtData              ; Ptr = DataPtr + A
        lda [Ptr]
        sta TileCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta TilesPtr
        lda Ptr+2
        sta TilesPtr+2
        lda TileCount           ; Ptr += count * 128
        sta MulA
        lda #128
        sta MB0
        jsr Mul16
        jsr AddPtrLong
        lda Ptr
        sta FlagsPtr
        lda Ptr+2
        sta FlagsPtr+2
        lda TileCount
        jsr AddPtr
        ; the map
        lda [Ptr]
        sta MapW
        ldy #2
        lda [Ptr],y
        sta MapH
        ldy #4
        lda [Ptr],y
        sta PX
        ldy #6
        lda [Ptr],y
        sta PY
        lda #8
        jsr AddPtr
        lda Ptr
        sta MapPtr
        lda Ptr+2
        sta MapPtr+2
        lda MapW
        sta MulA
        lda MapH
        asl a
        sta MB0
        jsr Mul16               ; width x height x 2
        jsr AddPtrLong
        ; the Avatar: frame count, then 128 bytes a frame (south, north, east, west)
        lda [Ptr]
        sta T0
        lda #2
        jsr AddPtr
        lda Ptr
        sta AvatarPtr
        lda Ptr+2
        sta AvatarPtr+2
        lda T0
        sta MulA
        lda #128
        sta MB0
        jsr Mul16
        jsr AddPtrLong
        ; the sprites: count, offsets, their size, the sprites
        lda [Ptr]
        sta MulA
        lda #4
        sta MB0
        jsr Mul16               ; count * 4
        lda #2
        jsr AddPtr
        lda Ptr
        sta SprTab
        lda Ptr+2
        sta SprTab+2
        jsr AddPtrLong
        lda [Ptr]
        sta MulR
        ldy #2
        lda [Ptr],y
        sta MulR+2
        lda #4
        jsr AddPtr
        lda Ptr
        sta SprBank
        lda Ptr+2
        sta SprBank+2
        jsr AddPtrLong
        ; the objects: 8 bytes each
        lda [Ptr]
        sta ObjCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta ObjPtr
        lda Ptr+2
        sta ObjPtr+2
        lda ObjCount
        asl a
        asl a
        asl a
        jsr AddPtr
        ; the characters: 10 bytes each
        lda [Ptr]
        sta ChrCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta ChrPtr
        lda Ptr+2
        sta ChrPtr+2
        stz Facing
        clc
        rts

; Ptr = DataPtr + A
AtData:
        clc
        adc DataPtr
        sta Ptr
        lda DataPtr+2
        adc #0
        sta Ptr+2
        rts

; Ptr += A
AddPtr:
        clc
        adc Ptr
        sta Ptr
        lda Ptr+2
        adc #0
        sta Ptr+2
        rts

; Ptr += MulR (32 bits)
AddPtrLong:
        clc
        lda Ptr
        adc MulR
        sta Ptr
        lda Ptr+2
        adc MulR+2
        sta Ptr+2
        rts

; MulR = MulA x MB0 (32-bit result; MulA and MB0 are used up)
Mul16:
        stz MulR
        stz MulR+2
        stz MB1
        ldx #16
.l:     lsr MulA
        bcc .s
        clc
        lda MulR
        adc MB0
        sta MulR
        lda MulR+2
        adc MB1
        sta MulR+2
.s:     asl MB0
        rol MB1
        dex
        bne .l
        rts

; ---- the screen ----
SetupScreen:
        ldx #0                  ; the palette: palette 0 of Super Hi-Res
        ldy #8
.pal:   lda [DataPtr],y
        sta >PALETTE,x
        iny
        iny
        inx
        inx
        cpx #32
        bne .pal
        lda #0                  ; every line: palette 0, 320 mode
        ldx #0
.scb:   sta >SCB,x
        inx
        inx
        cpx #200
        bne .scb
        ldx #0                  ; clear the screen
.clr:   sta >SCREEN,x
        inx
        inx
        cpx #32000
        bne .clr
        jsr FindBright
        lda Bright              ; the frame round the world and the words: the brightest colour
        pha
        tool $3704              ; SetSolidPenPat
        pea #bank(ViewRect)
        pea #ViewRect
        tool $5404              ; FrameRect
        lda Bright
        pha
        tool $A004              ; SetForeColor
        pea 0
        tool $A204              ; SetBackColor
        pea 8
        pea 12
        tool $3A04              ; MoveTo
        pea #bank(TitleBuf)
        pea #TitleBuf
        tool $A604              ; DrawCString
        pea 8
        pea 196
        tool $3A04
        pea #bank(HelpText)
        pea #HelpText
        tool $A604
        rts

; The palette colour with the most light (red + green + blue).
FindBright:
        stz Bright
        stz T1
        ldx #0
        ldy #8
.l:     lda [DataPtr],y
        and #$000F
        sta T0
        lda [DataPtr],y
        lsr a
        lsr a
        lsr a
        lsr a
        and #$000F
        clc
        adc T0
        sta T0
        lda [DataPtr],y
        xba
        and #$000F
        clc
        adc T0
        cmp T1
        bcc .n
        sta T1
        stx Bright
.n:     iny
        iny
        inx
        cpx #16
        bne .l
        rts

; The world around the Avatar: 10 x 10 tiles, the Avatar in the middle.
DrawView:
        stz Row
        lda #VIEW_Y*160+VIEW_X/2
        sta ViewOff
.row:   stz Col
.col:   lda PX
        clc
        adc Col
        sec
        sbc #5
        tax
        lda PY
        clc
        adc Row
        sec
        sbc #5
        tay
        jsr MapTile
        pha
        lda Row                 ; screen offset: ViewOff + row * 2560 (16 lines of 160) + col * 8
        xba
        asl a                   ; row * 512
        sta T0
        asl a
        asl a                   ; row * 2048
        clc
        adc T0
        clc
        adc ViewOff
        sta T0
        lda Col
        asl a
        asl a
        asl a
        clc
        adc T0
        tax
        pla
        jsr DrawTile
        inc Col
        lda Col
        cmp #10
        bne .col
        inc Row
        lda Row
        cmp #10
        bne .row
        jsr DrawObjects
        jsr DrawCharacters
        jsr DrawAvatar
        rts

; ---- objects and characters ----
; Which cells are taken: OccPtr (map width x height bytes), 1 = a solid object, 2 = a character. Carry set: no memory.
MakeOcc:
        lda MapW
        sta MulA
        lda MapH
        sta MB0
        jsr Mul16
        lda MulR                ; + 2: whole words to clear
        clc
        adc #2
        sta T0
        lda MulR+2
        adc #0
        sta T1
        pha
        pha
        lda T1
        pha
        lda T0
        pha
        lda UserId
        pha
        pea $C000
        pea 0
        pea 0
        tool $0902              ; NewHandle
        pla
        sta OccHandle
        pla
        sta OccHandle+2
        bcc .got
        rts
.got:   ldy #2
        lda [OccHandle],y
        sta OccPtr+2
        lda [OccHandle]
        sta OccPtr
        lda T0                  ; clear: (size + 2) / 2 words
        lsr T1
        ror a
        tax
        ldy #0
        lda #0
.z:     sta [OccPtr],y
        iny
        iny
        dex
        bne .z
        ; solid objects
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta Item
.o:     lda Item
        beq .chars
        ldy #6
        lda [ItemPtr],y
        and #$0001
        beq .on
        lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        ora #1
        sta [Ptr]
        rep #$20
        .a16
.on:    lda #8
        jsr NextItem
        dec Item
        bra .o
.chars: lda ChrPtr
        sta ItemPtr
        lda ChrPtr+2
        sta ItemPtr+2
        lda ChrCount
        sta Item
.c:     lda Item
        beq .done
        lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        ora #2
        sta [Ptr]
        rep #$20
        .a16
        lda #10
        jsr NextItem
        dec Item
        bra .c
.done:  clc
        rts

; ItemPtr += A
NextItem:
        clc
        adc ItemPtr
        sta ItemPtr
        lda ItemPtr+2
        adc #0
        sta ItemPtr+2
        rts

; Ptr = the cell X, Y of OccPtr; A = what is there (0: free)
OccAt:
        stx T2
        sty MulA
        lda MapW
        sta MB0
        jsr Mul16
        clc
        lda MulR
        adc T2
        sta MulR
        lda MulR+2
        adc #0
        sta MulR+2
        clc
        lda OccPtr
        adc MulR
        sta Ptr
        lda OccPtr+2
        adc MulR+2
        sta Ptr+2
        lda [Ptr]
        and #$00FF
        rts

; Every object in or next to the view, top to bottom.
DrawObjects:
        jsr ViewCorner
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta Item
.l:     lda Item
        beq .end
        lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        jsr CellOnScreen
        bcc .n
        ldy #4
        lda [ItemPtr],y
        sta SprId
        jsr DrawSprite
.n:     lda #8
        jsr NextItem
        dec Item
        bra .l
.end:   rts

DrawCharacters:
        jsr ViewCorner
        lda ChrPtr
        sta ItemPtr
        lda ChrPtr+2
        sta ItemPtr+2
        lda ChrCount
        sta Item
.l:     lda Item
        beq .end
        lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        jsr CellOnScreen
        bcc .n
        ldy #8
        lda [ItemPtr],y         ; its facing
        ldy #4
        clc
        adc [ItemPtr],y         ; + its first (south) sprite
        sta SprId
        jsr DrawSprite
.n:     lda #10
        jsr NextItem
        dec Item
        bra .l
.end:   rts

; ViewX0, ViewY0: the map cell at the view's top left.
ViewCorner:
        lda PX
        sec
        sbc #5
        sta ViewX0
        lda PY
        sec
        sbc #5
        sta ViewY0
        rts

; Cell X, Y: carry set if a sprite standing there can show (columns -1 to 10, rows 0 to 10 of the view);
; CellX, CellY = the cell's top left on the screen.
CellOnScreen:
        txa
        sec
        sbc ViewX0
        clc
        adc #1
        cmp #12
        bcs .no
        asl a
        asl a
        asl a
        asl a
        clc
        adc #VIEW_X-16
        sta CellX
        tya
        sec
        sbc ViewY0
        cmp #11
        bcs .no
        asl a
        asl a
        asl a
        asl a
        clc
        adc #VIEW_Y
        sta CellY
        sec
        rts
.no:    clc
        rts

; Draws sprite SprId standing on the cell at CellX, CellY (its bottom middle), clipped to the view; colour 0 see-through.
DrawSprite:
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
        sta SW
        ldy #2
        lda [Src],y
        sta SH
        lda #4
        clc
        adc Src
        sta Src
        lda Src+2
        adc #0
        sta Src+2
        lda SW
        lsr a
        sta BPR
        lsr a                   ; SX = CellX + 8 - width / 2
        sta T0
        lda CellX
        clc
        adc #8
        sec
        sbc T0
        sta SX
        lda CellY               ; SY = CellY + 16 - height
        clc
        adc #16
        sec
        sbc SH
        sta SY
        ; the columns (bytes) that show
        stz C0
        lda SX
        cmp #VIEW_X+160
        bcc .x1
        rts
.x1:    cmp #VIEW_X
        bcs .x2
        lda #VIEW_X
        sec
        sbc SX
        lsr a
        sta C0
.x2:    lda #VIEW_X+160
        sec
        sbc SX
        lsr a
        cmp BPR
        bcc .x3
        lda BPR
.x3:    sta C1
        cmp C0
        beq .out
        bcc .out
        ; the rows that show
        stz R0
        lda SY
        cmp #VIEW_Y+160
        bcs .out
        cmp #VIEW_Y
        bcs .y2
        lda #VIEW_Y
        sec
        sbc SY
        sta R0
.y2:    lda #VIEW_Y+160
        sec
        sbc SY
        cmp SH
        bcc .y3
        lda SH
.y3:    sta R1
        cmp R0
        beq .out
        bcs .draw
.out:   rts
.draw:  ldx R0                  ; Src += R0 rows
.skip:  cpx #0
        beq .scr
        lda Src
        clc
        adc BPR
        sta Src
        lda Src+2
        adc #0
        sta Src+2
        dex
        bra .skip
.scr:   lda SY                  ; Scr = $E12000 + (SY + R0) * 160 + SX / 2
        clc
        adc R0
        asl a
        asl a
        asl a
        asl a
        asl a                   ; * 32
        sta T0
        asl a
        asl a                   ; * 128
        clc
        adc T0
        sta T0
        lda SX
        lsr a
        clc
        adc T0
        clc
        adc #$2000
        sta Scr
        lda #$00E1
        sta Scr+2
        lda R1
        sec
        sbc R0
        sta Count
.row:   lda #0                  ; B = 0 for TAX
        sep #$20
        .a8
        ldy C0
.b:     lda [Src],y
        beq .nb
        tax
        lda [Scr],y
        and MaskTab,x
        ora [Src],y
        sta [Scr],y
.nb:    iny
        cpy C1
        bne .b
        rep #$20
        .a16
        lda Src
        clc
        adc BPR
        sta Src
        lda Src+2
        adc #0
        sta Src+2
        lda Scr
        clc
        adc #160
        sta Scr
        dec Count
        bne .row
        rts

; A = the tile at X, Y on the map; $FFFF off the map.
MapTile:
        cpx MapW
        bcs .off                ; (negative numbers are big: off too)
        cpy MapH
        bcs .off
        stx T2
        sty MulA
        lda MapW
        sta MB0
        jsr Mul16               ; y * width
        clc
        lda MulR
        adc T2
        sta MulR
        lda MulR+2
        adc #0
        sta MulR+2
        asl MulR                ; * 2: a tile number is a word
        rol MulR+2
        clc
        lda MapPtr
        adc MulR
        sta Ptr
        lda MapPtr+2
        adc MulR+2
        sta Ptr+2
        lda [Ptr]
        rts
.off:   lda #$FFFF
        rts

; Draws tile A (16 x 16) at screen offset X; $FFFF: black.
DrawTile:
        cmp #$FFFF
        beq .blank
        sta T0                  ; Src = TilesPtr + tile * 128
        stz T1
        ldy #7
.sh:    asl T0
        rol T1
        dey
        bne .sh
        clc
        lda TilesPtr
        adc T0
        sta Src
        lda TilesPtr+2
        adc T1
        sta Src+2
        lda #16
        sta Count
        ldy #0
.r:     lda [Src],y
        sta >SCREEN,x
        iny
        iny
        lda [Src],y
        sta >SCREEN+2,x
        iny
        iny
        lda [Src],y
        sta >SCREEN+4,x
        iny
        iny
        lda [Src],y
        sta >SCREEN+6,x
        iny
        iny
        txa
        clc
        adc #160
        tax
        dec Count
        bne .r
        rts
.blank: lda #16
        sta Count
        lda #0
.b:     sta >SCREEN,x
        sta >SCREEN+2,x
        sta >SCREEN+4,x
        sta >SCREEN+6,x
        txa
        clc
        adc #160
        tax
        lda #0
        dec Count
        bne .b
        rts

; The Avatar in the middle of the view (cell 5, 5), colour 0 see-through.
DrawAvatar:
        lda Facing              ; Src = AvatarPtr + facing * 128
        xba
        lsr a
        clc
        adc AvatarPtr
        sta Src
        lda AvatarPtr+2
        adc #0
        sta Src+2
        lda #$2000+VIEW_Y*160+VIEW_X/2+5*16*160+5*8
        sta Scr
        lda #$00E1
        sta Scr+2
        lda #16
        sta Count
.r:     lda #0                  ; B = 0: TAX below takes the byte alone
        sep #$20
        .a8
        ldy #0
.b:     lda [Src],y
        beq .next
        tax
        lda [Scr],y
        and MaskTab,x
        ora [Src],y
        sta [Scr],y
.next:  iny
        cpy #8
        bne .b
        rep #$20
        .a16
        lda Src
        clc
        adc #8
        sta Src
        lda Src+2
        adc #0
        sta Src+2
        lda Scr
        clc
        adc #160
        sta Scr
        dec Count
        bne .r
        rts

; MaskTab[b]: which halves of a screen byte stay under sprite byte b (a 0 half is see-through).
MakeMaskTable:
        sep #$20
        .a8
        ldx #0
.l:     txa
        and #$F0
        beq .hz
        lda #0
        bra .lo
.hz:    lda #$F0
.lo:    sta T0
        txa
        and #$0F
        beq .lz
        lda #0
        bra .st
.lz:    lda #$0F
.st:    ora T0
        sta MaskTab,x
        inx
        cpx #256
        bne .l
        rep #$20
        .a16
        rts

; Walks one step: T2 = dx, T3 = dy, A = the facing (0 south, 1 north, 2 east, 3 west). Walls and the map's edge stop it.
TryMove:
        sta Facing
        lda PX
        clc
        adc T2
        sta NX
        lda PY
        clc
        adc T3
        sta NY
        ldx NX
        ldy NY
        jsr MapTile
        cmp #$FFFF
        beq .stay
        tay                     ; its flags: bit 0 walkable
        lda [FlagsPtr],y
        and #$0001
        beq .stay
        ldx NX                  ; a solid object or a character there stops it too
        ldy NY
        jsr OccAt
        bne .stay
        lda NX
        sta PX
        lda NY
        sta PY
.stay:  jsr DrawView
        rts

; ---- data ----
StartStop:
        .dw 0                   ; flags
        .dw $0000               ; 320 mode
        .dw 0                   ; no resource file
        .dd 0                   ; direct page: StartUpTools gets it
        .dw 10                  ; tools:
        .dw $03,$0000           ; Miscellaneous Tool Set
        .dw $04,$0000           ; QuickDraw II
        .dw $06,$0000           ; Event Manager
        .dw $0E,$0000           ; Window Manager
        .dw $10,$0000           ; Control Manager
        .dw $0F,$0000           ; Menu Manager
        .dw $12,$0000           ; QuickDraw II Auxiliary
        .dw $14,$0000           ; LineEdit
        .dw $15,$0000           ; Dialog Manager
        .dw $16,$0000           ; Scrap Manager

OpenParms:
        .dw 2
OpenRef:
        .dw 0
        .dd DataName
EofParms:
        .dw 2
EofRef:
        .dw 0
EofLen:
        .dd 0
ReadParms:
        .dw 4
ReadRef:
        .dw 0
ReadBuf:
        .dd 0
ReadReq:
        .dd 0
ReadGot:
        .dd 0
CloseParms:
        .dw 1
CloseRef:
        .dw 0
QuitParms:
        .dw 0

DataName:
        .gsstr "9:GAME.DAT"

ViewRect:                       ; top, left, bottom, right
        .dw VIEW_Y-2, VIEW_X-2, VIEW_Y+162, VIEW_X+162

EvRec:
        .dw 0                   ; what
EvMsg:
        .dd 0                   ; message
        .dd 0                   ; when
        .dd 0                   ; where
        .dw 0                   ; modifiers

AlertSlow:
        .cstr "42/This game needs an Apple IIgs with an accelerator card (a TransWarp GS, a ZipGS or another). This Apple IIgs runs at its own speed./^Quit"
AlertNoData:
        .cstr "42/GAME.DAT, the game itself, is missing: keep it in the same folder as this program./^Quit"
AlertNoMemory:
        .cstr "42/There is not enough memory for the game./^Quit"
AlertBadData:
        .cstr "42/GAME.DAT was made for another version of this player. Build the game again with the editor./^Quit"
HelpText:
        .cstr "Arrows: walk     Q: quit"
TitleBuf:
        .ds 64
MaskTab:
        .ds 256
