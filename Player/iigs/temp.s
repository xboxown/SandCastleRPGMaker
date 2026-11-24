; Sandcastle RPG Maker - Apple IIgs player: cold and hot zones, as the Windows Ultima 6 engine (U6GameWindow.Temperature.cs,
; Exult's Actor::check_temperature) - Martian Dreams' thin air, Serpent Isle's frost. Once a game minute, for the Avatar
; and each member: in a Cold zone (Map Editor > Zones), with worn warmth under 100 (-75 plus each worn thing's Warmth on
; the head, back, feet, body, hands and legs), it gets colder by 1 + (100 - warmth) / 20, up to 63; with 100 or more, or
; out of it, it warms up (5 a minute out of it). A third of the time it says so, and from 50 it is hurt: 1, from 60 1-3.
; Heat the same with Cooling. Immune ones (Character Editor > Health) never feel it.
; GAME.DAT (after the modes): u16 zones, each map, x, y, width, height, kind (1 cold, 2 hot); u16 gear, each name,
; warmth, cooling; u16 immune, each name, flags (1 cold, 2 heat).

W_COLD     = 91
W_FREEZING = 92
W_HOT      = 93
W_BURNING  = 94

ParseTemp:
        lda [Ptr]
        sta TzN
        lda #2
        jsr AddPtr
        lda TzN                 ; 12 bytes each
        asl a
        asl a
        sta T4
        asl a
        clc
        adc T4
        sta T4
        ldy #0
.z:     cpy T4
        bcs .zd
        lda [Ptr],y
        sta TzTab,y
        iny
        iny
        bra .z
.zd:    lda T4
        jsr AddPtr
        lda [Ptr]
        sta TgN
        lda #2
        jsr AddPtr
        lda TgN                 ; 6 bytes each
        asl a
        sta T4
        asl a
        clc
        adc T4
        sta T4
        ldy #0
.g:     cpy T4
        bcs .gd
        lda [Ptr],y
        sta TgTab,y
        iny
        iny
        bra .g
.gd:    lda T4
        jsr AddPtr
        lda [Ptr]
        sta TiN
        lda #2
        jsr AddPtr
        lda TiN                 ; 4 bytes each
        asl a
        asl a
        sta T4
        ldy #0
.i:     cpy T4
        bcs .id
        lda [Ptr],y
        sta TiTab,y
        iny
        iny
        bra .i
.id:    lda T4
        jsr AddPtr
        jmp ParseDoors          ; (door.s)

; A game minute (ClockRun): everyone in the party feels the cold and the heat.
TempMinute:
        lda TzN
        bne .go
        rts
.go:    stz TmK
.l:     lda TmK
        cmp PartyN
        beq .one
        bcc .one
        rts
.one:   lda TmK                 ; who: the one moved (0), else member TmK - 1
        bne .mem
        lda #AvRec
        sta WP
        lda MyBank
        sta WP+2
        lda PX
        sta TmX
        lda PY
        sta TmY
        lda #$FFFF
        sta TmOwner
        bra .who
.mem:   dec a
        jsr MemberAt
        lda MP
        sta WP
        lda MP+2
        sta WP+2
        lda [MP]
        sta TmX
        ldy #2
        lda [MP],y
        sta TmY
        jsr MPCode
        sta TmOwner
.who:   ldy #C_FLAGS            ; dead or knocked out: not now
        lda [WP],y
        and #4+32
        bne .n
        ldy #C_NAME
        lda [WP],y
        jsr Immunity
        sta TmImm
        lda TmK                 ; this one's place in TmC / TmH
        asl a
        sta TmI
        lda #1                  ; the cold
        sta TmKind
        jsr TempStep
        ldy #C_FLAGS
        lda [WP],y
        and #4
        bne .n
        lda #2                  ; the heat
        sta TmKind
        jsr TempStep
.n:     inc TmK
        brl .l

; A = a name: its immunities (1 cold, 2 heat).
Immunity:
        sta TmNm
        ldx #0
        ldy TiN
.l:     cpy #0
        beq .no
        lda TiTab,x
        cmp TmNm
        beq .yes
        inx
        inx
        inx
        inx
        dey
        bra .l
.yes:   lda TiTab+2,x
        rts
.no:    lda #0
        rts

; Carry set if TmX, TmY on this map lies in a zone of kind TmKind.
InZone:
        ldx #0
        ldy TzN
.l:     cpy #0
        beq .no
        lda TzTab+10,x
        cmp TmKind
        bne .n
        lda TzTab,x
        cmp CurMap
        bne .n
        lda TmX
        sec
        sbc TzTab+2,x
        bmi .n
        cmp TzTab+6,x
        bcs .n
        lda TmY
        sec
        sbc TzTab+4,x
        bmi .n
        cmp TzTab+8,x
        bcs .n
        sec
        rts
.n:     txa
        clc
        adc #12
        tax
        dey
        bra .l
.no:    clc
        rts

; Worn protection of TmOwner's things against TmKind (warmth 1 / cooling 2): -75 + each worn thing's on the head, back,
; feet, body, hands and legs.
Protection:
        lda #$FFB5              ; -75
        sta TmProt
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta TmLeft
.l:     lda TmLeft
        beq .d
        ldy #6
        lda [ItemPtr],y
        and #64+256+32
        cmp #64
        bne .n
        ldy #14
        lda [ItemPtr],y
        cmp TmOwner
        bne .n
        ldy #6                  ; worn in one of those slots (EquipmentSlot + 1 in the top 4 bits)
        lda [ItemPtr],y
        xba
        lsr a
        lsr a
        lsr a
        lsr a
        and #$000F
        cmp #1                  ; head
        beq .w
        cmp #3                  ; body
        beq .w
        cmp #5                  ; gloves
        beq .w
        cmp #7                  ; back
        beq .w
        cmp #8                  ; legs
        beq .w
        cmp #9                  ; feet
        bne .n
.w:     ldy #8
        lda [ItemPtr],y
        jsr TempGearOf
        bcc .n
        lda TmKind
        cmp #1
        beq .warm
        lda TgTab+4,x
        bra .add
.warm:  lda TgTab+2,x
.add:   clc
        adc TmProt
        sta TmProt
.n:     lda #16
        jsr NextItem
        dec TmLeft
        bra .l
.d:     rts

; A = a name: carry set, X = its place in TgTab, if it is worn gear against the cold or heat.
TempGearOf:
        sta TmNm
        ldx #0
        ldy TgN
.l:     cpy #0
        beq .no
        lda TgTab,x
        cmp TmNm
        beq .yes
        inx
        inx
        inx
        inx
        inx
        inx
        dey
        bra .l
.yes:   sec
        rts
.no:    clc
        rts

; One minute of TmKind for WP (its temperature at TmC / TmH + TmI).
TempStep:
        ldx TmI
        lda TmKind
        cmp #1
        beq .c
        lda TmH,x
        bra .t
.c:     lda TmC,x
.t:     sta TmT
        bra .chk
.ez:    brl .ease
.chk:
        lda TmImm               ; immune: never feels it
        and TmKind
        bne .ez
        jsr InZone
        bcc .ez
        jsr Protection
        lda TmProt
        bmi .colder
        cmp #100
        bcs .ez
.colder:
        lda #100                ; 1 + (100 - protection) / 20
        sec
        sbc TmProt
        lsr a
        lsr a
        sta T4
        lda #5
        sta ExB
        lda T4
        sta ExA
        jsr SDiv
        lda ExQ
        inc a
        clc
        adc TmT
        cmp #64
        bcc .k
        lda #63
.k:     sta TmT
        jsr .store
        lda #3                  ; a third of the time it says so; from 50 it hurts
        jsr Random
        cmp #0
        bne .o
        lda WP                  ; its name (the Avatar's word for the Avatar - solo.s)
        jsr RecName
        lda FmtName
        sta WhoName
        lda FmtName+2
        sta WhoName+2
        lda TmT
        cmp #30
        bcs .bad
        lda TmKind
        cmp #1
        beq .cw
        lda #W_HOT
        bra .say
.cw:    lda #W_COLD
        bra .say
.bad:   lda TmKind
        cmp #1
        beq .cb
        lda #W_BURNING
        bra .say
.cb:    lda #W_FREEZING
.say:   pha
        lda #1
        sta WTLead
        pla
        jsr Tell
        lda TmT
        cmp #50
        bcc .o
        cmp #60
        bcs .worse
        lda #1
        jmp Hurt
.worse: lda #3
        jsr Random
        inc a
        jmp Hurt
.ease:  lda TmT                 ; warming up / cooling down
        beq .o
        sec
        sbc #5
        bpl .e
        lda #0
.e:     sta TmT
        jmp .store
.o:     rts
.store: ldx TmI
        lda TmKind
        cmp #1
        beq .sc
        lda TmT
        sta TmH,x
        rts
.sc:    lda TmT
        sta TmC,x
        rts

TzN:      .dw 0
TzTab:    .ds 12*32
TgN:      .dw 0
TgTab:    .ds 6*64
TiN:      .dw 0
TiTab:    .ds 4*32
TmC:      .ds 2*10
TmH:      .ds 2*10
TmK:      .dw 0
TmI:      .dw 0
TmX:      .dw 0
TmY:      .dw 0
TmOwner:  .dw 0
TmImm:    .dw 0
TmKind:   .dw 0
TmProt:   .dw 0
TmLeft:   .dw 0
TmT:      .dw 0
TmNm:     .dw 0
