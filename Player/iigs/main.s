; Sandcastle RPG Maker - the Apple IIgs player (Ultima 6 Apple IIgs mode).
; A GS/OS S16 application in 65816 assembly (assembled by asm816.py). GS/OS and an accelerator required.
;
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
        cmp #1
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
        lda #2
        jsr AddPtr
        lda Ptr
        sta AvatarPtr
        lda Ptr+2
        sta AvatarPtr+2
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
        jsr DrawAvatar
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
AlertBadData:
        .cstr "42/GAME.DAT was made for another version of this player. Build the game again with the editor./^Quit"
HelpText:
        .cstr "Arrows: walk     Q: quit"
TitleBuf:
        .ds 64
MaskTab:
        .ds 256
