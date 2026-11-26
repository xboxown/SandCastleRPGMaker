; Sandcastle RPG Maker - Apple IIgs player: the title menu, as the Windows Ultima 6 engine (U6GameWindow.U6Title.cs,
; Nuvie's original menu): over the title picture (System Settings > Ultima 6 > Title; none: black) - Introduction (the
; intro commands; only if there are some), Create New Character (a new game), Acknowledgments (the maker's words),
; Journey Onward (the saved game; only if there is one). Up / Down and Enter. The game starts here; the Avatar's death
; comes back here.

TitleInPlay: .dw 0
W_TINTRO   = 64
W_TCREATE  = 65
W_TACKS    = 66
W_TJOURNEY = 67
TITLE_X    = 96
TITLE_Y    = 136

ParseTitle:
        lda [Ptr]
        sta TitleHasPic
        lda #2
        jsr AddPtr
        lda TitleHasPic
        beq .np
        lda Ptr
        sta TitlePic
        lda Ptr+2
        sta TitlePic+2
        lda #32000
        jsr AddPtr
.np:    lda Ptr                 ; the Acknowledgments: a length, a C string
        clc
        adc #1
        sta AcksPtr
        lda Ptr+2
        adc #0
        sta AcksPtr+2
        lda [Ptr]
        and #$00FF
        clc
        adc #2
        jsr AddPtr
        lda [Ptr]               ; the Introduction's code
        sta IntroLen
        lda #2
        jsr AddPtr
        lda Ptr
        sta IntroPtr
        lda Ptr+2
        sta IntroPtr+2
        lda IntroLen
        jsr AddPtr
        jmp ParseButtons        ; the HUD's command buttons (mouse.s)

; The title menu (Mode 12).
ShowTitle:
        jsr MusicOff            ; (music.s)
        lda #12
        sta Mode
        stz IntroOn
        jsr SaveThere
        lda #1                  ; Journey Onward if there is a saved game, else Create New Character
        ldx HasSave
        beq .p
        lda #3
.p:     sta TitlePick
        jmp DrawTitle

; HasSave = 1 if SAVE.DAT is in the game's folder.
SaveThere:
        stz HasSave
        lda #SaveName
        jsr LocalPath
        gsos $2006,InfoParms
        bcs .no
        lda #1
        sta HasSave
.no:    rts

; Carry set if choice A is on the menu.
Offered:
        cmp #0
        bne .c3
        lda IntroLen
        beq .no
        sec
        rts
.c3:    cmp #3
        bne .yes
        lda HasSave
        beq .no
.yes:   sec
        rts
.no:    clc
        rts

; The picture, then the choices (the picked one bright).
DrawTitle:
        jsr CurOff              ; the pointer hidden while drawing (mouse.s)
        lda TitleHasPic
        beq .black
        lda TitlePic
        sta Src
        lda TitlePic+2
        sta Src+2
        ldx #0
        ldy #0
.cp:    lda [Src],y
        sta >SCREEN,x
        iny
        iny
        inx
        inx
        cpx #32000
        bne .cp
        bra .menu
.black: ldx #0
        lda #0
.bk:    sta >SCREEN,x
        inx
        inx
        cpx #32000
        bne .bk
.menu:  stz TK
        lda #TITLE_Y
        sta TY0
.l:     lda TK
        jsr Offered
        bcc .n
        lda TK
        cmp TitlePick
        bne .plain
        lda KeyColor
        bra .col
.plain: lda TxtColor
.col:   jsr SetColor
        lda #TITLE_X
        pha
        lda TY0
        pha
        tool $3A04              ; MoveTo
        lda TK
        clc
        adc #W_TINTRO
        asl a
        asl a
        tax
        lda WordTab+2,x
        pha
        lda WordTab,x
        pha
        tool $A604              ; DrawCString
        lda TY0
        clc
        adc #12
        sta TY0
.n:     inc TK
        lda TK
        cmp #4
        bcc .l
        rts

; A key at the title menu (Mode 12) or its Acknowledgments (Mode 13).
TitleKey:
        ldx Mode
        cpx #13
        bne .menu
        jmp ShowTitle           ; the Acknowledgments: any key back
.menu:  cmp #$1B                ; Escape, q, Ctrl+Q: quit? (brought up in play by an event: Escape - back to the game)
        bne .nesc
        lda TitleInPlay
        beq .q
        stz TitleInPlay
        lda #$FFFE              ; the map's music chosen again (music.s MusicRun)
        sta AmbNow
        jsr GameScreen
        jmp PromptLine
.nesc:  cmp #$1B
        beq .q
        cmp #$11
        beq .q
        ora #$20
        cmp #'q'
        bne .k
.q:     lda #1
        sta QuitFromTitle
        lda #6
        sta Mode
        rts
.k:     lda Key
        cmp #$0B                ; up
        beq .up
        cmp #$0A                ; down
        beq .down
        cmp #$0D
        beq .pick
        cmp #' '
        beq .pick
        rts
.up:    lda TitlePick
.u:     dec a
        bmi .out
        pha
        jsr Offered
        pla
        bcc .u
        bra .set
.down:  lda TitlePick
.d:     inc a
        cmp #4
        bcs .out
        pha
        jsr Offered
        pla
        bcc .d
.set:   sta TitlePick
        jmp DrawTitle
.out:   rts
.pick:  lda TitlePick
        bne .p1
        jmp Introduction
.p1:    cmp #1
        bne .p2
        jmp NewGame
.p2:    cmp #2
        bne .p3
        jmp ShowAcks
.p3:    jsr NewGame             ; Journey Onward: the game, then the saved one
        jmp LoadGame

; Acknowledgments: the maker's words on black (any key back).
ShowAcks:
        ldx #0
        lda #0
.bk:    sta >SCREEN,x
        inx
        inx
        cpx #32000
        bne .bk
        lda TxtColor
        jsr SetColor
        lda #16
        pha
        lda #90
        pha
        tool $3A04
        lda AcksPtr+2
        pha
        lda AcksPtr
        pha
        tool $A604
        lda #13
        sta Mode
        rts

; The Introduction: the game's screen, the intro commands run (as an event); at their end the menu again.
Introduction:
        jsr GameScreen
        lda IntroPtr
        sta TalkPtr
        lda IntroPtr+2
        sta TalkPtr+2
        stz TPC
        lda #1
        sta IntroOn
        sta EvRunning
        sta EvHit
        lda #3
        sta Mode
        jmp RunTalk

; Create New Character: the game from its start (GAME.DAT read again if a game was played), the first map's events.
NewGame:
        lda Played
        beq .first
        jsr Restart
.first: lda #1
        sta Played
        jsr GameScreen
        jmp ArriveEvents

; The game's screen.
GameScreen:
        stz Mode
        jsr SetupScreen
        jsr DrawPanel
        jsr HudForce            ; (hud.s)
        jmp DrawView

; A fresh game: GAME.DAT again, everything as it starts.
Restart:
        lda Handle+2
        pha
        lda Handle
        pha
        tool $1002              ; DisposeHandle
        lda OccHandle+2
        pha
        lda OccHandle
        pha
        tool $1002
        lda #DataName           ; GAME.DAT again
        sta OpenPath
        lda MyBank
        sta OpenPath+2
        jsr LoadData
        jsr ParseData
        ldx #32                 ; the other maps: from their files again
.m:     cpx #512
        bcs .md
        stz MapTab+MT_IN,x
        txa
        clc
        adc #32
        tax
        bra .m
.md:    stz CurMap
        stz CurBits
        lda #1
        sta CurBits1
        jsr MakeOcc
        stz PartyN
        stz ViewIdx
        stz PanelView
        lda #$FFFF
        sta OpenBag
        sta MoveIdx
        stz AvDead
        stz CombatOn
        stz QN
        stz EvScan
        stz EvRunning
        stz LightLeft
        stz Day
        stz Minute
        stz TurnUsed
        stz Told
        stz InvSel
        jmp ApplySchedules

TitlePick: .dw 0
TitleHasPic: .dw 0
TitlePic: .dd 0
AcksPtr:  .dd 0
IntroLen: .dw 0
IntroPtr: .dd 0
IntroOn:  .dw 0
HasSave:  .dw 0
Played:   .dw 0
QuitFromTitle: .dw 0
TK:       .dw 0
TY0:      .dw 0
InfoParms:
        .dw 2
        .dd PathBuf
        .dw 0
