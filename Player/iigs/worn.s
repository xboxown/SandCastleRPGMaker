; Sandcastle RPG Maker - Apple IIgs player: magic worn or held, as the Windows Ultima 6 engine (U6GameWindow.MagicItems.cs;
; Object Editor > Weapon / Armor > Magic - Ultima 6's rings, amulets, magic armour and the glass sword's reach): while
; worn or held, an item's defense adds to the wearer's armour, its reach lengthens a hand-to-hand blow, its light lights
; round the wearer (never burning out), and its strength, dexterity and intelligence bonuses are added - taken back
; exactly as it comes off. GAME.DAT (after the joiners): u16 items, each 8 words: name, reach cells (0: none), defense,
; light radius (0: none), strength, dexterity, intelligence (signed), 0.

ParseWorn:
        lda [Ptr]
        sta MgN
        lda #2
        jsr AddPtr
        lda MgN                 ; 16 bytes each
        asl a
        asl a
        asl a
        asl a
        sta T4
        ldy #0
.l:     cpy T4
        bcs .d
        lda [Ptr],y
        sta MgTab,y
        iny
        iny
        bra .l
.d:     lda T4
        jsr AddPtr
        jmp ParseTough          ; (tough.s)

; GearOf (wear.s), before it: nothing magic counted yet.
MagicClear:
        stz MgReach
        stz MgDef
        stz MgStr
        stz MgDex
        stz MgInt
        stz MgLight
        rts

; GearOf: the worn thing at ItemPtr - its magic counted (ItemPtr kept).
WornMagic:
        lda MgN
        bne .go
        rts
.go:    ldy #8
        lda [ItemPtr],y
        sta MgNm
        ldx #0
        ldy MgN
.l:     cpy #0
        beq .o
        lda MgTab,x
        cmp MgNm
        beq .f
        txa
        clc
        adc #16
        tax
        dey
        bra .l
.o:     rts
.f:     lda MgTab+2,x           ; the longest reach
        cmp MgReach
        bcc .r
        sta MgReach
.r:     lda MgTab+6,x           ; the brightest light
        cmp MgLight
        bcc .li
        sta MgLight
.li:    lda MgTab+4,x
        clc
        adc MgDef
        sta MgDef
        lda MgTab+8,x
        clc
        adc MgStr
        sta MgStr
        lda MgTab+10,x
        clc
        adc MgDex
        sta MgDex
        lda MgTab+12,x
        clc
        adc MgInt
        sta MgInt
        rts

; GearOf, at its end: the magic's defense and reach into GArmour, GReach.
MagicGear:
        lda MgDef
        clc
        adc GArmour
        bpl .a
        lda #0
.a:     sta GArmour
        lda MgReach
        cmp GReach
        bcc .o
        beq .o
        sta GReach
.o:     rts

; The record at WP (its place A: 0 the one moved, 1-8 a member slot + 1) gets the stat bonuses of what it wears now -
; what was added before taken back first. Its light remembered (LightsCarried).
MagicApply:
        asl a
        sta MgK
        asl a
        clc
        adc MgK                 ; x 6
        tax
        stx MgK
        ldy #C_STR
        lda MgStr
        sec
        sbc MgDone,x
        clc
        adc [WP],y
        sta [WP],y
        lda MgStr
        sta MgDone,x
        ldy #C_DEX
        lda MgDex
        sec
        sbc MgDone+2,x
        clc
        adc [WP],y
        sta [WP],y
        lda MgDex
        sta MgDone+2,x
        ldy #C_INT
        lda MgInt
        sec
        sbc MgDone+4,x
        clc
        adc [WP],y
        sta [WP],y
        lda MgInt
        sta MgDone+4,x
        ldx MgK                 ; its worn light
        lda MgLight
        sta MgLights,x
        rts

; Solo mode's swap (solo.s): what the one moved and slot A (1-8) had added change round with them.
MagicSwap:
        sta MgK
        asl a
        clc
        adc MgK
        asl a                   ; x 6
        tax
        ldy #0
.l:     lda MgDone,x
        pha
        lda MgDone,y
        sta MgDone,x
        pla
        sta MgDone,y
        lda MgLights,x
        pha
        lda MgLights,y
        sta MgLights,x
        pla
        sta MgLights,y
        inx
        inx
        iny
        iny
        cpy #6
        bcc .l
        rts

; LightsCarried (look.s): the party's worn magic lights.
MagicLights:
        ldx #0
.l:     lda MgLights,x
        beq .n
        phx
        inc a
        jsr MoreLight
        plx
.n:     txa
        clc
        adc #6
        tax
        cpx #9*6
        bcc .l
        rts

MgN:      .dw 0
MgTab:    .ds 16*32
MgNm:     .dw 0
MgK:      .dw 0
MgReach:  .dw 0
MgDef:    .dw 0
MgStr:    .dw 0
MgDex:    .dw 0
MgInt:    .dw 0
MgLight:  .dw 0
MgDone:   .ds 9*6
MgLights: .ds 9*6
