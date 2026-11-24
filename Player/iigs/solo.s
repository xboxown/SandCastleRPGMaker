; Sandcastle RPG Maker - Apple IIgs player: solo mode and the members' combat modes, as the Windows Ultima 6 engine
; (U6GameWindow.U6Solo.cs, U6CombatModes.cs - Nuvie's Player::set_actor, actor.lua's worktypes).
; Solo: 1-9 puts that member under the keys - the two swap places as Windows does: their records (all but the party's
; map / place words) and what they carry change round, the one left stands where it was, no one follows. 0: party mode
; (the Avatar back, the party follows). Only the Avatar talks or rests ("Not in solo mode."). Saving, a map change or
; the controlled member's death give the Avatar back first. Option + a digit: only the portrait view (Windows' F1-F9).
; Combat modes (word 20, +56): 0 FRONT (the nearest foe), 1 REAR (holds back, strikes what comes near), 2 FLANK, 3
; BERSERK (on at the foe, never running), 4 RETREAT (away from foes), 5 ASSAULT (the strongest foe), 6 COMMAND - in
; combat mode each Command member in turn is moved by the player (the round's commander), as Windows' NextCommander.

W_SOLO      = 85
W_PARTYMODE = 86
W_NOTSOLO   = 87
W_RESTSOLO  = 88
C_MODE      = 56
MODE_REAR   = 1
MODE_RETREAT = 4
MODE_ASSAULT = 5
MODE_COMMAND = 6

; A = a digit key (as typed) with the event's modifiers: 1-9 solo on that member, 0 party mode; with Option only the
; portrait view.
SoloKey:
        sta SoKey
        lda EvRec+14            ; Option held: the portrait view only (Windows' F1-F9)
        and #$0800
        beq .solo
        lda SoKey
        jmp ViewMember          ; (party.s)
.solo:  lda SoKey
        sec
        sbc #'0'
        bne .member
        lda SoloOn              ; 0: party mode
        ora SoloCtl
        beq .view0
        stz SoloOn
        jsr SoloBack
        lda #W_PARTYMODE
        jsr Format
        jsr DrawView
        jmp PromptLine
.view0: lda SoKey
        jmp ViewMember
.member:
        cmp PartyN
        beq .ok
        bcc .ok
        rts
.ok:    sta SoN
        dec a
        jsr MemberAt
        ldy #C_FLAGS            ; dead or knocked out: not possible
        lda [MP],y
        and #4+32
        beq .go
        lda #9
        jsr Format
        jmp PromptLine
.go:    jsr SoloBack            ; the Avatar first, then that member
        lda SoN
        jsr SoloTo
        lda #1
        sta SoloOn
        ldy #C_NAME             ; "Solo mode: Dupre."
        lda AvRec+C_NAME
        jsr NameNumber
        lda FmtName
        sta WhoName
        lda FmtName+2
        sta WhoName+2
        lda #1                  ; {1}: the member's name
        sta UseWho
        lda #W_SOLO
        jsr Format
        stz UseWho
        lda SoN
        sta ViewIdx
        jsr DrawView
        jmp PromptLine

; Member n (1-9, PartyTab's n-1) becomes the one moved; the Avatar is left standing in its place in the party's list.
SoloTo:
        sta SoloCtl
        dec a
        jsr MemberAt
        jmp SoloSwap

; The Avatar moved again (if a member is).
SoloBack:
        lda SoloCtl
        beq .o
        dec a
        jsr MemberAt
        jsr SoloSwap
        stz SoloCtl
.o:     rts

; The one moved (AvRec, PX, PY, Facing, AvDown) and the record at MP change round, with what each carries.
SoloSwap:
        lda #0                  ; the slot's cell: no longer a member's (it becomes the one moved)
        jsr OccMember
        lda PX                  ; the one moved, as a record
        sta AvRec
        lda PY
        sta AvRec+2
        lda Facing
        sta AvRec+8
        lda AvRec+C_FLAGS
        and #$FFDF
        ldx AvDown
        beq .up
        ora #32
.up:    sta AvRec+C_FLAGS
        ldy #0                  ; their 64 bytes change round - not the party's map and place (+58, +60)
.x:     cpy #P_MAP
        beq .skip
        cpy #P_IDX
        beq .skip
        lda [MP],y
        tax
        lda AvRec,y
        sta [MP],y
        txa
        sta AvRec,y
.skip:  iny
        iny
        cpy #64
        bcc .x
        lda AvRec
        sta PX
        lda AvRec+2
        sta PY
        lda AvRec+8
        and #3
        sta Facing
        stz AvDown
        lda AvRec+C_FLAGS
        and #32
        beq .d
        lda #1
        sta AvDown
.d:     lda AvRec+C_FLAGS       ; the one moved is never "knocked out" by its flags (AvDown says it)
        and #$FFDF
        sta AvRec+C_FLAGS
        lda #4                  ; the one left: a member's cell
        jsr OccMember
        jsr MPCode              ; what they carry: the one moved's ($FFFF) <-> the slot's
        sta SoCode
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta SoI
.o:     lda SoI
        beq .od
        ldy #6
        lda [ItemPtr],y
        and #64+256             ; carried (owner) - a thing in a bag keeps its bag as +14
        cmp #64
        bne .on
        ldy #14
        lda [ItemPtr],y
        cmp #$FFFF
        bne .o2
        lda SoCode
        sta [ItemPtr],y
        bra .on
.o2:    cmp SoCode
        bne .on
        lda #$FFFF
        sta [ItemPtr],y
.on:    lda #16
        jsr NextItem
        dec SoI
        bra .o
.od:    jmp RefreshInventory

; Carry set (and "Not in solo mode." told) if a member is being moved - talking is the Avatar's.
NotSolo:
        lda SoloCtl
        bne .yes
        clc
        rts
.yes:   lda #W_NOTSOLO
        jsr Format
        jsr PromptLine
        sec
        rts

; Rest: "-Not in solo mode!" (carry set) while a member is moved.
NotSoloRest:
        lda SoloCtl
        bne .yes
        clc
        rts
.yes:   lda #W_RESTSOLO
        jsr Format
        jsr PromptLine
        sec
        rts

; Before a save or a map change: the Avatar moved again, solo mode off.
SoloOff:
        stz SoloOn
        jmp SoloBack

; Dies: the one moved is a member - the Avatar back first; DefP = the member (it dies as a member does).
SoloDies:
        lda SoloCtl
        dec a
        pha
        jsr SoloBack
        stz SoloOn
        pla
        jsr MemberAt
        lda MP
        sta DefP
        lda MP+2
        sta DefP+2
        rts

; ---- combat modes ----

; After each turn in combat mode: the next Command member (in the party's order, the Avatar among them) is moved.
; Out of combat (or in solo mode): nothing; combat over - the Avatar again.
NextCommander:
        lda SoloOn
        bne .o
        lda CombatOn
        bne .c
        jmp SoloBack            ; combat over: the Avatar moved again, the party follows
.c:     lda SoloCtl             ; from the one moved now, round the party (0 = the Avatar)
        sta SoK
        ldx PartyN
        inx
        stx SoLeft
.l:     dec SoLeft
        beq .o
        inc SoK
        lda SoK
        cmp PartyN
        beq .k
        bcc .k
        stz SoK                 ; past the last member: the Avatar
        lda SoloCtl
        beq .o                  ; the Avatar is moved already
        jmp SoloBack
.k:     lda SoK
        cmp SoloCtl
        beq .o
        dec a
        jsr MemberAt
        lda SoloCtl             ; (the slot holding the Avatar holds the Avatar's mode: Command)
        beq .m
        cmp SoK
        beq .o
.m:     ldy #C_FLAGS
        lda [MP],y
        and #4+32
        bne .l
        ldy #C_MODE
        lda [MP],y
        cmp #MODE_COMMAND
        bne .l
        jsr SoloBack
        lda SoK
        jmp SoloTo
.o:     rts

; In combat mode, before a member strikes: MP moves by its mode - FRONT, FLANK, BERSERK, ASSAULT step toward their foe
; (the nearest; ASSAULT the strongest), REAR holds, RETREAT steps away from the nearest foe. Not a Command member (the
; player moves it), nor out of combat mode (they follow).
MemberMoves:
        bra .go
.o0:    rts
.go:    lda CombatOn
        beq .o0
        ldy #C_MODE
        lda [MP],y
        cmp #MODE_COMMAND
        beq .o0
        cmp #MODE_REAR
        beq .o0
        sta SoMode
        lda CP                  ; (PartyTurn's own CP, kept)
        sta SoCP
        lda CP+2
        sta SoCP+2
        jsr FoeFor              ; CP = its foe (carry clear: none in sight)
        bcs .f
        jmp .back
.f:
        lda [CP]                ; how far
        sec
        sbc [MP]
        jsr ShortX
        sta SoDX
        ldy #2
        lda [CP],y
        sec
        sbc [MP],y
        jsr ShortY
        sta SoDY
        lda SoMode
        cmp #MODE_RETREAT
        beq .away
        lda SoDX                ; already in reach: no step
        jsr Abs
        sta T5
        lda SoDY
        jsr Abs
        cmp T5
        bcs .mx
        lda T5
.mx:    ldy #C_REACH
        cmp [MP],y
        beq .o
        bcc .o
        cmp #2
        bcc .o
        lda SoDX
        jsr Sign
        sta SDX
        lda SoDY
        jsr Sign
        sta SDY
        jsr MemberStep
        jmp .back
.away:  lda SoDX
        jsr Sign
        eor #$FFFF
        inc a
        sta SDX
        lda SoDY
        jsr Sign
        eor #$FFFF
        inc a
        sta SDY
        jsr MemberStep
.back:
.o:     lda SoCP
        sta CP
        lda SoCP+2
        sta CP+2
        rts

; CP = the foe MP goes for: a hostile character (awake, on this map) within 8 - the nearest, or for ASSAULT the one
; with the most hit points. Carry clear: none.
FoeFor:
        stz SoFound
        lda #$7FFF
        sta SoBest
        lda ChrPtr
        sta CP
        lda ChrPtr+2
        sta CP+2
        lda ChrCount
        sta SoI
.l:     lda SoI
        beq .d
        ldy #C_FLAGS
        lda [CP],y
        and #2+4+8+32
        cmp #2
        bne .n
        lda [CP]
        sec
        sbc [MP]
        jsr AbsX
        sta T5
        ldy #2
        lda [CP],y
        sec
        sbc [MP],y
        jsr AbsY
        cmp T5
        bcc .m
        sta T5
.m:     lda T5
        cmp #9
        bcs .n
        lda SoMode
        cmp #MODE_ASSAULT
        bne .near
        ldy #C_HP               ; the strongest: the most hit points (the lowest "best")
        lda #$7FFF
        sec
        sbc [CP],y
        sta T5
.near:  lda T5
        cmp SoBest
        bcs .n
        sta SoBest
        lda CP
        sta SoFP
        lda CP+2
        sta SoFP+2
        lda #1
        sta SoFound
.n:     lda CP
        clc
        adc #64
        sta CP
        lda CP+2
        adc #0
        sta CP+2
        dec SoI
        bra .l
.d:     lda SoFound
        beq .no
        lda SoFP
        sta CP
        lda SoFP+2
        sta CP+2
        sec
        rts
.no:    clc
        rts

; MP steps by SDX, SDY if that is ground that can be walked, free and not the one moved's cell; else one axis only.
MemberStep:
        jsr .try
        bcs .o
        lda SDY
        pha
        stz SDY
        jsr .try
        pla
        bcs .o
        sta SDY
        stz SDX
        jsr .try
.o:     rts
.try:   lda SDX
        ora SDY
        beq .no
        lda [MP]
        clc
        adc SDX
        tax
        ldy #2
        lda [MP],y
        clc
        adc SDY
        tay
        jsr WrapXY
        stx SoNX
        sty SoNY
        cpx PX
        bne .nav
        cpy PY
        beq .no
.nav:   jsr MapTile
        cmp #$FFFF
        beq .no
        tay
        lda [FlagsPtr],y
        and #1
        beq .no
        ldx SoNX
        ldy SoNY
        jsr OccAt
        and #7
        bne .no
        lda #0
        jsr OccMember
        lda SoNX
        sta [MP]
        ldy #2
        lda SoNY
        sta [MP],y
        lda #4
        jsr OccMember
        lda #1
        sta Moved
        sec
        rts
.no:    clc
        rts

; A = a name: A = its combat mode (ModeTab; 0 FRONT when not set).
ModeOfName:
        sta SoNm
        ldx #0
.l:     txa
        lsr a
        lsr a
        cmp ModeN
        bcs .no
        lda ModeTab,x
        cmp SoNm
        beq .yes
        inx
        inx
        inx
        inx
        bra .l
.yes:   lda ModeTab+2,x
        rts
.no:    lda #0
        rts

; Set Combat Mode (STAT word 20) on WP: its name's mode in ModeTab; a member's record too. GVal the mode.
SetModeOp:
        ldy #C_NAME
        lda [WP],y
        sta SoNm
        ldx #0
.l:     txa
        lsr a
        lsr a
        cmp ModeN
        bcs .new
        lda ModeTab,x
        cmp SoNm
        beq .set
        inx
        inx
        inx
        inx
        bra .l
.new:   lda ModeN
        cmp #64
        bcs .rec
        inc ModeN
        lda SoNm
        sta ModeTab,x
.set:   lda GVal
        sta ModeTab+2,x
.rec:   jsr WPInParty           ; a member: its record now (the Avatar stays Command)
        bcc .o
        lda WP
        cmp #AvRec
        beq .o
        lda GVal
        ldy #C_MODE
        sta [WP],y
.o:     rts

; GAME.DAT (after the levers): u16 modes, each name, mode.
ParseModes:
        lda [Ptr]
        sta ModeN
        lda #2
        jsr AddPtr
        lda ModeN
        asl a
        asl a
        sta T4
        ldy #0
.l:     cpy T4
        bcs .d
        lda [Ptr],y
        sta ModeTab,y
        iny
        iny
        bra .l
.d:     lda T4
        jmp AddPtr

ModeN:    .dw 0
ModeTab:  .ds 4*64
SoNm:     .dw 0
SoloOn:   .dw 0                 ; solo mode (1-9) on
SoloCtl:  .dw 0                 ; the member moved (1-9; its slot holds the Avatar), 0 the Avatar
SoKey:    .dw 0
SoN:      .dw 0
SoK:      .dw 0
SoLeft:   .dw 0
SoCode:   .dw 0
SoI:      .dw 0
SoMode:   .dw 0
SoDX:     .dw 0
SoDY:     .dw 0
SoNX:     .dw 0
SoNY:     .dw 0
SoBest:   .dw 0
SoFound:  .dw 0
SoFP:     .dd 0
SoCP:     .dd 0
