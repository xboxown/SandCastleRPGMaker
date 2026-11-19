; Sandcastle RPG Maker - the Apple IIgs player (Ultima 6 Apple IIgs mode).
; A GS/OS S16 application in 65816 assembly (assembled by asm816.py). GS/OS and an accelerator required.
;
; Version 16 (b635): map events' triggers and pages (events.s): stepped on / stood on / stepped off, Use on it,
;   arriving (Always), having or not having an item, a thing dropped on it / picked up from it; variable conditions.
; Version 15 (b634): the party (party.s): Join / Leave Party, members follow, fight, go along to other maps,
;   0-9 show a member in the portrait view; saved.
; Version 14 (b633): stats that change (stats.s): the portrait view's stats live, levels from experience, R Rest.
; Version 13 (b632): the clock and schedules (schedule.s): a game minute every 5 seconds (Exult), each character's
;   activity for the 3-hour period (stand, wander, sleep, pace, loiter) at its spot; asleep: "No response."
; Version 12 (b631): combat (combat.s): characters 64 bytes with their stats, the Avatar's stats, A Attack, B combat
;   mode, Space Pass; Ultima 6's to-hit and damage; foes chase and strike; death; experience for a kill.
; Version 11 (b630): save and load (save.s: Ctrl+S, Ctrl+R; SAVE.DAT), the quit question (Ctrl+Q, Esc, q).
; Version 10 (b629): more maps (maps.s): map 0 in GAME.DAT, the others each their own file MAP.n, loaded when the game
;   goes there (and kept as they are); the objects of all of them in GAME.DAT (flags' top 4 bits: the map); map events
;   stepped on run their code (the talk code), Change Map among it.
; Version 9 (b628): Move (M: push a thing one square, put it in a container, give it to the Avatar), containers (Use on a
;   chest spills what is in it; a carried bag opens in the grid), Tab between the map's cursor and the grid's.
; Version 8 (b627): sounds (sound.s: Play Sound in talks, through the Sound Tool Set), files found on other disks
;   (DISKS.DAT) and the Disk Swap screen asking for them.
; Version 7 (b625): the panel's two views (*): the inventory, or the portrait and the stats.
; Version 6 (b624): the inventory (inventory.s): the HUD's Carried Items grid, Get into it, Drop out of it.
; Version 5 (b623): the verbs (verbs.s): Look, Get, Use (doors), Talk with Ultima 6's cursor.
; Version 4 (b622): talk (talk.s): T and a direction; portrait, name, words with keywords, "you say:", replies.
; Version 3 (b621): the HUD (HUD Editor): its background, the Game View where and as big as it is, the name; the
;   text area's prompt.
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
VX        = $96   ; the Game View on the screen: left (even), top, columns, rows of 16 x 16
VY        = $98
VCols     = $9A
VRows     = $9C
HalfC     = $9E   ; the Avatar's cell in the view
HalfR     = $A0
ClipX0    = $A2   ; the view's edges, + 64 (sprites may start left of or above the screen)
ClipX1    = $A4
ClipY0    = $A6
ClipY1    = $A8
HudPtr    = $AA   ; 4
HasHud    = $AE
LblCount  = $B0
LblPtr    = $B2   ; 4
TextX     = $B6
TextY     = $B8
TalkPtr   = $BA   ; 4: the talk's code
TP        = $BE   ; 4: words to write
TalkTab   = $C2   ; 4
TalkBank  = $C6   ; 4
RSrc      = $CA   ; 4
RScr      = $CE   ; 4
T4        = $D2
T5        = $D4
SayP      = $D6   ; 4: words being written
TNamePtr  = $DA   ; 4: the tiles' names
Dst       = $DE   ; 4: copying to
SvPtr     = $E2   ; 4: a saved game being read
AtkP      = $E6   ; 4: who strikes (combat.s)
DefP      = $EA   ; 4: who is struck
CP        = $EE   ; 4: the character whose turn it is
ViewP     = $F2   ; 4: whose stats the portrait view shows (the Avatar or a member)
MP        = $F6   ; 4: a party member

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
        sep #$20
        .a8
        phk
        pla
        sta MyBank
        stz MyBank+1
        rep #$20
        .a16
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
        jsr ApplySchedules      ; everyone to their period's activity (schedule.s)
        jsr LoadDisks           ; a game on floppies: which file is on which disk
        jsr PreloadSwapSound
        tool $9004              ; HideCursor
        jsr SetupScreen
        jsr DrawPanel
        jsr DrawView
        jsr ArriveEvents        ; the first map's Always events (events.s)
MainLoop:
        pha
        pea $0028               ; key down, auto-key
        pea #bank(EvRec)
        pea #EvRec
        tool $0A06              ; GetNextEvent
        pla
        bne .ev
        jsr IdleTurn            ; nothing pressed: the world goes on (combat.s)
        bra MainLoop
.ev:
        lda EvMsg
        and #$007F
        sta Key
        lda Mode
        beq .walk
        cmp #4
        beq .cur
        cmp #5
        beq .inv
        cmp #6
        bne .not6
        lda Key                 ; the quit question
        jsr QuitKey
        brl MainLoop
.not6:  cmp #7
        bne .not7
        jsr DeadKey             ; the Avatar dead: a key (combat.s)
        brl MainLoop
.not7:  cmp #8
        bne .not8
        lda Key                 ; Rest's hours (stats.s)
        jsr RestKey
        brl MainLoop
.not8:  cmp #2
        bne MainLoop            ; the talk is running
        lda Key
        jsr TalkKey
        bra MainLoop
.cur:   lda Key                 ; the verb's cursor
        jsr CursorKey
        jsr AfterTurn
        brl MainLoop
.inv:   lda Key                 ; the grid's cursor (Drop)
        jsr InvKey
        jsr AfterTurn
        brl MainLoop
.walk:  lda Key
        cmp #$1B
        beq .qj
        cmp #'q'
        beq .qj
        cmp #'Q'
        bne .nq
.qj:    brl .quit
.nq:
        cmp #'*'
        bne .notstar
        jsr ToggleView
        brl MainLoop
.notstar:
        cmp #$13                ; Ctrl+S: save
        bne .nots
        jsr SaveGame
        brl MainLoop
.nots:  cmp #$12                ; Ctrl+R: load
        bne .notr
        jsr LoadGame
        brl MainLoop
.notr:  cmp #$11                ; Ctrl+Q: quit?
        bne .notq
        brl .quit
.notq:  cmp #' '                ; Space: pass a turn
        bne .notsp
        jsr PassTurn
        brl MainLoop
.notsp:  ora #$20                ; the verbs' letters, either case
        ldx #VERB_TALK
        cmp #'t'
        beq .verb
        ldx #VERB_LOOK
        cmp #'l'
        beq .verb
        ldx #VERB_GET
        cmp #'g'
        beq .verb
        ldx #VERB_USE
        cmp #'u'
        beq .verb
        ldx #VERB_MOVE
        cmp #'m'
        beq .verb
        ldx #VERB_ATTACK
        cmp #'a'
        beq .verb
        cmp #'b'
        bne .notb
        jsr ToggleCombat
        brl MainLoop
.notb:  lda Key                 ; 0-9 (as typed): whose portrait view
        cmp #'0'
        bcc .notd
        cmp #'9'+1
        bcs .notd
        jsr ViewMember
        brl MainLoop
.notd:  lda Key
        ora #$20
        cmp #'r'
        bne .notrr
        jsr StartRest
        brl MainLoop
.notrr:
        cmp #'d'
        beq .drop
        lda Key
        jsr ArrowDelta
        bcc .none
        jsr TryMove
        jsr AfterTurn
.none:  brl MainLoop
.verb:  txa
        jsr StartVerb
        brl MainLoop
.drop:  jsr StartDrop
        brl MainLoop
.quit:  jsr AskQuit
        brl MainLoop

; A = a key: carry set if an arrow - T2 = dx, T3 = dy, A = the facing (0 south, 1 north, 2 east, 3 west).
ArrowDelta:
        stz T2
        stz T3
        cmp #$0B                ; up
        bne .n1
        dec T3
        lda #1
        sec
        rts
.n1:    cmp #$0A                ; down
        bne .n2
        inc T3
        lda #0
        sec
        rts
.n2:    cmp #$15                ; right
        bne .n3
        inc T2
        lda #2
        sec
        rts
.n3:    cmp #$08                ; left
        bne .no
        dec T2
        lda #3
        sec
        rts
.no:    clc
        rts

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
        cmp #16
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
        lda Ptr                 ; the tiles' names
        sta TNamePtr
        lda Ptr+2
        sta TNamePtr+2
        lda TileCount
        asl a
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
        lda ObjCount            ; 16 bytes each
        asl a
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
        lda ChrCount            ; 64 bytes each
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        jsr AddPtr
        ; the HUD: its background (32000 bytes) if there is one, the Game View, the text area, the words
        lda [Ptr]
        sta HasHud
        lda #2
        jsr AddPtr
        lda HasHud
        beq .nohud
        lda Ptr
        sta HudPtr
        lda Ptr+2
        sta HudPtr+2
        lda #32000
        jsr AddPtr
.nohud: lda [Ptr]
        sta VX
        ldy #2
        lda [Ptr],y
        sta VY
        ldy #4
        lda [Ptr],y
        sta VCols
        ldy #6
        lda [Ptr],y
        sta VRows
        ldy #8
        lda [Ptr],y
        sta TextX
        ldy #10
        lda [Ptr],y
        sta TextY
        ldy #12
        lda [Ptr],y
        sta TextW
        ldy #14
        lda [Ptr],y
        sta TextH
        lda #16
        jsr AddPtr
        lda [Ptr]
        sta LblCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta LblPtr
        lda Ptr+2
        sta LblPtr+2
        jsr ViewSetup
        jsr ParseTalk
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
        pea 0
        tool $A204              ; SetBackColor
        lda HasHud
        beq .plain
        jmp DrawHud
.plain: lda Bright              ; the frame round the world and the words: the brightest colour
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
        jsr TextInit
        lda #Prompt
        jmp PrintMine

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
.row:   stz Col
.col:   lda PX
        clc
        adc Col
        sec
        sbc HalfC
        tax
        lda PY
        clc
        adc Row
        sec
        sbc HalfR
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
        cmp VCols
        beq .nextrow
        jmp .col
.nextrow:
        inc Row
        lda Row
        cmp VRows
        beq .rowsdone
        jmp .row
.rowsdone:
        jsr DrawObjects
        jsr DrawCharacters
        jsr DrawAvatar
        lda Mode
        cmp #4
        bne .nocur
        jmp DrawCursor
.nocur: rts

; After the HUD's words: the portrait and name places, the colours, the words, the variables, the talks.
ParseTalk:
        lda LblCount            ; past the labels: 8 + length each
        sta Item
.lb:    lda Item
        beq .lbdone
        ldy #6
        lda [Ptr],y
        and #$00FF
        clc
        adc #8
        jsr AddPtr
        dec Item
        bra .lb
.lbdone:
        ldx #0
        ldy #0
.rect:  lda [Ptr],y
        sta PortX,x
        iny
        iny
        inx
        inx
        cpx #30                 ; portrait x y w h, name x y w h, text colour, keyword colour, the grid x y columns rows cell
        bne .rect
        lda #30
        jsr AddPtr
        lda GridCols            ; how many cells it shows
        sta MulA
        lda GridRows
        sta MB0
        jsr Mul16
        lda MulR
        cmp #64
        bcc .max
        lda #64
.max:   sta InvMax
        ldx #0                  ; the panel
        ldy #0
.pan:   lda [Ptr],y
        sta PanX,x
        iny
        iny
        inx
        inx
        cpx #8
        bne .pan
        lda #8
        jsr AddPtr
        lda PanW
        beq .nopan
        lda Ptr                 ; its portrait view's pixels
        sta PanPix
        lda Ptr+2
        sta PanPix+2
        lda PanW
        lsr a
        sta MulA
        lda PanH
        sta MB0
        jsr Mul16
        jsr AddPtrLong
.nopan: lda [Ptr]               ; its words (the stats)
        sta StatCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta StatPtr
        lda Ptr+2
        sta StatPtr+2
        lda StatCount
        sta Item
.sk:    lda Item
        beq .skd
        ldy #6
        lda [Ptr],y
        and #$00FF
        clc
        adc #8
        jsr AddPtr
        dec Item
        bra .sk
.skd:
        lda [Ptr]               ; the words
        sta Count
        lda #2
        jsr AddPtr
        ldx #0
.w:     lda Count
        beq .wdone
        cpx #256                ; room for 64
        bcs .wdone
        lda Ptr
        clc
        adc #1
        sta WordTab,x
        lda Ptr+2
        adc #0
        sta WordTab+2,x
        lda [Ptr]
        and #$00FF
        clc
        adc #2
        jsr AddPtr
        stx AvatarW             ; the last word is the Avatar's name
        inx
        inx
        inx
        inx
        dec Count
        bra .w
.wdone: lda [Ptr]               ; the names
        sta Count
        lda #2
        jsr AddPtr
        ldx #0
.nm:    lda Count
        beq .nmdone
        cpx #2048
        bcs .nmskip
        lda Ptr
        clc
        adc #1
        sta NameTab,x
        lda Ptr+2
        adc #0
        sta NameTab+2,x
.nmskip:
        lda [Ptr]
        and #$00FF
        clc
        adc #2
        jsr AddPtr
        inx
        inx
        inx
        inx
        dec Count
        bra .nm
.nmdone:
        lda [Ptr]               ; the variables' starting values
        sta Count
        lda #2
        jsr AddPtr
        ldx #0
        ldy #0
.v:     lda Count
        beq .vdone
        cpx #512
        bcs .vskip
        lda [Ptr],y
        sta VarTab,x
.vskip: iny
        iny
        inx
        inx
        dec Count
        bra .v
.vdone: tya
        jsr AddPtr
        lda [Ptr]               ; the talks
        sta MulA
        lda #4
        sta MB0
        jsr Mul16
        lda #2
        jsr AddPtr
        lda Ptr
        sta TalkTab
        lda Ptr+2
        sta TalkTab+2
        jsr AddPtrLong
        lda [Ptr]               ; all the talks' bytes (to step past them)
        sta TalkTotal
        ldy #2
        lda [Ptr],y
        sta TalkTotal+2
        lda #4
        jsr AddPtr
        lda Ptr
        sta TalkBank
        lda Ptr+2
        sta TalkBank+2
        jmp ParseSounds

; ---- the Game View ----
; From VX, VY, VCols, VRows: the Avatar's cell, the view's first byte on the screen, its edges (+ 64).
ViewSetup:
        lda VCols
        lsr a
        sta HalfC
        lda VRows
        lsr a
        sta HalfR
        lda VY                  ; ViewOff = VY * 160 + VX / 2
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
        lda VX
        lsr a
        clc
        adc T0
        sta ViewOff
        lda VX
        clc
        adc #64
        sta ClipX0
        lda VCols
        asl a
        asl a
        asl a
        asl a
        clc
        adc ClipX0
        sta ClipX1
        lda VY
        clc
        adc #64
        sta ClipY0
        lda VRows
        asl a
        asl a
        asl a
        asl a
        clc
        adc ClipY0
        sta ClipY1
        rts

; The HUD's background over the whole screen, then its words.
DrawHud:
        lda HudPtr
        sta Src
        lda HudPtr+2
        sta Src+2
        ldx #0
        ldy #0
.c:     lda [Src],y
        sta >SCREEN,x
        iny
        iny
        inx
        inx
        cpx #32000
        bne .c
        jsr DrawLabels
        jsr TextInit
        lda #Prompt
        jmp PrintMine

; The HUD's words (the member's name...).
DrawLabels:
        lda LblPtr
        sta ItemPtr
        lda LblPtr+2
        sta ItemPtr+2
        lda LblCount
        sta Item
        bra LabelList

; The portrait view's words (the stats).
DrawStats:
        jmp DrawLiveStats       ; (stats.s)

; Item words from ItemPtr: x, y, colour, length, the words, 0.
LabelList:
.l:     lda Item
        beq .text
        ldy #4                  ; its colour ($FFFF: the brightest)
        lda [ItemPtr],y
        cmp #$FFFF
        bne .col
        lda Bright
.col:   pha
        tool $A004              ; SetForeColor
        lda [ItemPtr]
        pha
        ldy #2
        lda [ItemPtr],y
        clc
        adc #8                  ; the top to the baseline
        pha
        tool $3A04              ; MoveTo
        lda ItemPtr+2           ; the words: 7 bytes in, a C string
        pha
        lda ItemPtr
        clc
        adc #7
        pha
        tool $A604              ; DrawCString
        ldy #6
        lda [ItemPtr],y
        and #$00FF
        clc
        adc #8                  ; x, y, colour, length, the words, 0
        jsr NextItem
        dec Item
        bra .l
.text:  rts

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
        and #$F000+96+256+1
        cmp CurBits1            ; solid, on this map
        bne .on
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
.on:    lda #16
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
        ldy #6                  ; dead: not there
        lda [ItemPtr],y
        and #4
        bne .cn
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
.cn:    lda #64
        jsr NextItem
        dec Item
        bra .c
.done:  jsr OccParty            ; the members' cells (party.s)
        clc
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
        ldy #6                  ; carried, gone, inside something or on another map: not here
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .n
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
.n:     lda #16
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
        ldy #6                  ; dead: not there
        lda [ItemPtr],y
        and #4
        bne .n
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
.n:     lda #64
        jsr NextItem
        dec Item
        bra .l
.end:   jmp DrawParty           ; the members too (party.s)

; ViewX0, ViewY0: the map cell at the view's top left.
ViewCorner:
        lda PX
        sec
        sbc HalfC
        sta ViewX0
        lda PY
        sec
        sbc HalfR
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
        sta T3
        lda VCols
        inc a
        inc a
        cmp T3                  ; columns -1 to VCols
        beq .no
        bcc .no
        lda T3
        asl a
        asl a
        asl a
        asl a
        clc
        adc ClipX0
        sec
        sbc #16
        sta CellX               ; + 64, as the clip edges
        tya
        sec
        sbc ViewY0
        sta T3
        lda VRows
        inc a
        cmp T3                  ; rows 0 to VRows
        beq .no
        bcc .no
        lda T3
        asl a
        asl a
        asl a
        asl a
        clc
        adc ClipY0
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
        cmp ClipX1
        bcc .x1
        rts
.x1:    cmp ClipX0
        bcs .x2
        lda ClipX0
        sec
        sbc SX
        lsr a
        sta C0
.x2:    lda ClipX1
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
        cmp ClipY1
        bcs .out
        cmp ClipY0
        bcs .y2
        lda ClipY0
        sec
        sbc SY
        sta R0
.y2:    lda ClipY1
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
.scr:   lda SY                  ; Scr = $E12000 + (SY - 64 + R0) * 160 + (SX - 64) / 2
        clc
        adc R0
        sec
        sbc #64
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
        sec
        sbc #64
        cmp #$8000              ; halved keeping its sign: it may start left of the screen
        ror a
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
        ; on into BlitRows

; Count rows from Src (BPR bytes a row) to Scr (160 a row), bytes C0 to C1 of each; colour 0 see-through.
BlitRows:
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
        lda HalfR               ; Scr = $E12000 + ViewOff + HalfR * 2560 + HalfC * 8
        xba
        asl a
        sta T0
        asl a
        asl a
        clc
        adc T0
        sta T0
        lda HalfC
        asl a
        asl a
        asl a
        clc
        adc T0
        clc
        adc ViewOff
        clc
        adc #$2000
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
        ldx NX                  ; a solid object or a character there stops it too (not a member: they follow)
        ldy NY
        jsr OccAt
        and #3
        bne .stay
        lda PX
        sta OldX
        lda PY
        sta OldY
        lda NX
        sta PX
        lda NY
        sta PY
        jsr FollowParty         ; (party.s)
        lda #1                  ; a step: a turn
        sta TurnUsed
        jsr DrawView
        jmp SteppedEvents       ; off the old cell, onto the new one (events.s)
.stay:  lda #1
        sta TurnUsed
        jsr DrawView
        rts

        .include "talk.s"
        .include "verbs.s"
        .include "inventory.s"
        .include "sound.s"
        .include "maps.s"
        .include "save.s"
        .include "combat.s"
        .include "schedule.s"
        .include "stats.s"
        .include "party.s"
        .include "events.s"

; The Disk Swap screen's sound, loaded now if it is here (it can't come from a disk that isn't in).
PreloadSwapSound:
        lda SwapSnd
        cmp SndCount
        bcs .out
        sta SndNow
        jsr SoundName
        lda #FileName
        jsr LocalPath
        jsr LoadFile
        bcs .out
        lda SndNow
        asl a
        asl a
        tax
        lda LoadAddr
        sta SndAddr,x
        lda LoadAddr+2
        sta SndAddr+2,x
.out:   rts

; ---- data ----
StartStop:
        .dw 0                   ; flags
        .dw $0000               ; 320 mode
        .dw 0                   ; no resource file
        .dd 0                   ; direct page: StartUpTools gets it
        .dw 11                  ; tools:
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
        .dw $08,$0000           ; Sound Tool Set

OpenParms:
        .dw 2
OpenRef:
        .dw 0
OpenPath:
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
Prompt:
        .cstr ">"
Dash:
        .cstr "-"
ByeWord:
        .db "bye",0
Key:      .dw 0
Mode:     .dw 0                 ; 0 walking, 1 Talk- which way, 2 typing a word, 3 the talk running
MyBank:   .dw 0
PenX:     .dw 0
PenY:     .dw 0
SpaceW:   .dw 0
RX:       .dw 0
RY:       .dw 0
RW:       .dw 0
RH:       .dw 0
TxtAt:    .dw 0
WordKey:  .dw 0
WordEnd:  .dw 0
TPC:      .dw 0
SayLen:   .dw 0
PWWidth:  .dw 0
PWEnd:    .dw 0
AskPC:    .dw 0
InLen:    .dw 0
TalkPortrait: .dw 0
NamePtr:  .dd 0
TextW:    .dw 0
TextH:    .dw 0
PortX:    .dw 0                 ; portrait x y w h, name x y w h, text colour, keyword colour (as GAME.DAT has them)
PortY:    .dw 0
PortW:    .dw 0
PortH:    .dw 0
NameX:    .dw 0
NameY:    .dw 0
NameW:    .dw 0
NameH:    .dw 0
TxtColor: .dw 0
KeyColor: .dw 0
GridX:    .dw 0                 ; the carried things' grid (as GAME.DAT has it, right after KeyColor)
GridY:    .dw 0
GridCols: .dw 0
GridRows: .dw 0
GridCell: .dw 0
InvMax:   .dw 0
InvCount: .dw 0
InvCol:   .dw 0
InvSel:   .dw 0
InvList:  .ds 256
DropRec:  .dd 0
PanX:     .dw 0                 ; the panel (as GAME.DAT has it)
PanY:     .dw 0
PanW:     .dw 0
PanH:     .dw 0
PanPix:   .dd 0
StatPtr:  .dd 0
StatCount: .dw 0
PanelView: .dw 0                ; 0 the inventory, 1 the portrait and the stats
CurMap:   .dw 0                 ; the map the Avatar is on
CurBits:  .dw 0                 ;   its number in an object's flags (the top 4 bits)
CurBits1: .dw 1                 ;   and solid
MapCount: .dw 1
MapTab:   .ds 512               ; 16 maps, 32 bytes each: width, height, start x, y, cells, characters, events, loaded
EvCount:  .dw 0
EvPtr:    .dd 0
GoMap:    .dw 0
GoX:      .dw 0
GoY:      .dw 0
OpenBag:  .dw $FFFF             ; the bag open in the grid (its object number; $FFFF none)
MoveIdx:  .dw $FFFF             ; Move: the thing picked first ($FFFF: none yet)
Into:     .dw 0
GridHas:  .dw 0                 ; the grid's pick: something there, and what
GridRec:  .dd 0
WasIn:    .dw 0
SpX:      .dw 0
SpY:      .dw 0
ListLen:  .dw 0
ListBuf:  .ds 104               ; what spilled out of a container: names, ", " between
NumBuf:   .ds 4                 ; the disk's number in figures (the Disk Swap screen's {0})
TalkTotal: .dd 0
SndCount: .dw 0
SndNow:   .dw 0
SndVolNow: .dw 0
Gen:      .dw 0
SndLen:   .ds 1024              ; 256 sounds: their lengths (4 bytes)
SndAddr:  .ds 1024              ;   where they are once loaded (0: not yet)
SndFreq:  .ds 512               ;   the Sound Tool Set's frequency offset
SndVol:   .ds 512               ;   their volume
SndBlock: .ds 18                ; FFStartSound's parameters: waveStart, waveSize, freqOffset, docBuffer, bufferSize, nextWavePtr, volSetting
SwapHasPic: .dw 0
SwapPic:  .dd 0
SwapFrames: .dw 0
SwapFrameTab: .dd 0
SwapAX:   .dw 0
SwapAY:   .dw 0
SwapTX:   .dw 0
SwapTY:   .dw 0
SwapTC:   .dw 0
SwapMsg:  .dd 0
SwapSnd:  .dw 0
SwapFrame: .dw 0
SwapTick: .dw 0
WantDisk: .dw 0
DiskCount: .dw 0
DiskTab:  .ds 256               ; 32 disks: their ProDOS name, their name shown (4 bytes each)
FileCount: .dw 0
FileTab:  .dd 0
FileName: .ds 20
PathBuf:  .ds 66                ; a GS/OS path: its length, then its letters
LoadHandle: .dd 0
LoadAddr: .dd 0
LoadSize: .dd 0
DisksName:
        .cstr "DISKS.DAT"
AvatarW:  .dw 0
WordTab:  .ds 256                ; up to 64 words
NameTab:  .ds 2048
Verb:     .dw 0
Kind:     .dw 0
CurX:     .dw 0
CurY:     .dw 0
CurRect:  .dw 0,0,0,0
ObjRec:   .dd 0
FmtName:  .dd 0
FmtBuf:   .ds 128
InBuf:    .ds 34
WBuf:     .ds 64
VarTab:   .ds 512
HelpText:
        .cstr "Arrows: walk     Q: quit"
TitleBuf:
        .ds 64
MaskTab:
        .ds 256
