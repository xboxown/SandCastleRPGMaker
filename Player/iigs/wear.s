; Sandcastle RPG Maker - Apple IIgs player: wearing weapons and armour, as the Windows Ultima 6 engine's body slots
; (HUD Editor > Body Slot pieces; U6GameWindow.U6Verbs.cs Readies / Unreadies). A worn thing is carried (64) with its
; slot (EquipmentSlot + 1) in the flags' top 4 bits, drawn in that slot's piece; the grid's cursor reaches it after the
; pack's things. Use on a weapon or armour in the pack: readied in its slot (what was there goes back to the pack) -
; "{1} readies {0}."; Use on a worn one: "{1} puts away {0}.". A weapon's or armour's Other: 1 weapon / 2 armour (bits
; 14-15), its slot (10-13), reach 1/2/4/8 (8-9), damage or armour points (0-7). The Avatar's weapon damage and reach:
; the worn weapon's (none: bare hands); its armour: all worn armour's points.

W_READIES = 51
W_UNREADIES = 52

; GAME.DAT: the body slots (after the party's most).
ParseSlots:
        lda [Ptr]
        sta SlotCount
        lda #2
        jsr AddPtr
        ldy #0
        ldx #0
.l:     cpx SlotCount
        bcs .d
        lda [Ptr],y
        sta SlotTab,y
        iny
        iny
        lda [Ptr],y
        sta SlotTab,y
        iny
        iny
        lda [Ptr],y
        sta SlotTab,y
        iny
        iny
        inx
        bra .l
.d:     tya
        jsr AddPtr
        jmp ParseSpells         ; (magic.s)

; ItemPtr = the thing worn in slot A (carry set), else carry clear.
WornIn:
        xba
        asl a
        asl a
        asl a
        asl a
        and #$F000
        ora #64
        sta WSlot
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta WN
        beq .no
.l:     ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp WSlot
        bne .nw
        ldy #14                 ; the shown member's (owner.s)
        lda [ItemPtr],y
        cmp CurOwner
        beq .yes
.nw:
        lda #16
        jsr NextItem
        dec WN
        bne .l
.no:    clc
        rts
.yes:   sec
        rts

; After the pack's things in the grid: each slot's worn thing, drawn in its place, listed for the cursor.
ShowWorn:
        lda OpenBag
        cmp #$FFFF
        bne .out
        lda #1
        sta WornMode
        stz WK
.l:     lda WK
        cmp SlotCount
        bcs .d
        asl a
        sta T4
        asl a
        clc
        adc T4                  ; 6 bytes each
        tax
        lda SlotTab+2,x         ; the slot as the HUD shows it empty
        sta RX
        sta CellX
        lda SlotTab+4,x
        sta RY
        sta CellY
        lda GridCell
        sta RW
        sta RH
        lda SlotTab,x
        pha
        jsr RestoreRect
        pla
        jsr WornIn
        bcc .n
        jsr ShowOne
.n:     inc WK
        bra .l
.d:     stz WornMode
.out:   rts

; The portrait view: the worn things listed too (not drawn).
CountWorn:
        lda OpenBag
        cmp #$FFFF
        bne .out
        stz WK
.l:     lda WK
        cmp SlotCount
        bcs .out
        asl a
        sta T4
        asl a
        clc
        adc T4
        tax
        lda SlotTab,x
        jsr WornIn
        bcc .n
        jsr CountOne
.n:     inc WK
        bra .l
.out:   rts

; Use on a weapon or armour in the grid (GridRec; its name written).
WearUse:
        jsr NewLine
        stz Mode
        lda GridRec
        sta Src
        lda GridRec+2
        sta Src+2
        ldy #6
        lda [Src],y
        and #$F000
        beq .ready
        lda [Src],y             ; worn: back to the pack
        and #$0FFF
        sta [Src],y
        lda #W_UNREADIES
        bra .said
.ready: ldy #10                 ; into its slot: what is there goes back to the pack
        lda [Src],y
        xba
        lsr a
        lsr a
        and #$000F
        bne .slot
        jsr RefreshInventory
        lda #9                  ; Not possible.
        jsr Format
        jmp PromptLine
.slot:  sta WK
        jsr WornIn
        bcc .free
        ldy #6
        lda [ItemPtr],y
        and #$0FFF
        sta [ItemPtr],y
.free:  lda WK
        xba
        asl a
        asl a
        asl a
        asl a
        and #$F000
        ldy #6
        ora [Src],y
        sta [Src],y
        lda #W_READIES
.said:  pha
        lda Src                 ; {0} the thing
        sta ItemPtr
        lda Src+2
        sta ItemPtr+2
        ldy #8
        lda [ItemPtr],y
        jsr NameNumber
        stz InvSel
        jsr RefreshInventory
        pla
        jsr Format
        jsr HpChanged
        jmp PromptLine

; The Avatar's weapon damage, reach and armour from what it wears.
RecalcGear:
        lda #$FFFF
        sta GearOwner
        jsr GearOf
        lda GWeapon
        sta AvRec+C_WEAPON
        lda GReach
        sta AvRec+C_REACH
        lda GArmour
        sta AvRec+C_ARMOUR
        lda WP                  ; its worn magic's stats (worn.s)
        pha
        lda WP+2
        pha
        lda #AvRec
        sta WP
        lda MyBank
        sta WP+2
        lda #0
        jsr MagicApply
        pla
        sta WP+2
        pla
        sta WP
        jmp MembersGear         ; (owner.s)

; GWeapon, GReach, GArmour: what GearOwner wears.
GearOf:
        jsr MagicClear          ; worn magic (worn.s)
        stz GWeapon
        lda #1
        sta GReach
        stz GArmour
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta WN
        beq .d
.l:     ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp #$1000+64
        bcc .n
        and #$0FFF
        cmp #64
        bne .n
        ldy #14
        lda [ItemPtr],y
        cmp GearOwner
        bne .n
        jsr WornMagic           ; its magic counted (worn.s)
        ldy #10
        lda [ItemPtr],y
        cmp #$FFFF
        beq .n
        sta T4
        and #$C000
        cmp #$4000
        bne .arm
        lda T4                  ; a weapon: its damage, its reach
        and #$00FF
        sta GWeapon
        lda T4
        xba
        and #$0003
        tax
        lda ReachTab,x
        sta GReach
        bra .n
.arm:   cmp #$8000
        bne .n
        lda T4
        and #$00FF
        clc
        adc GArmour
        sta GArmour
.n:     lda #16
        jsr NextItem
        dec WN
        bne .l
.d:     jmp MagicGear           ; the magic's defense and reach (worn.s)

SlotCount: .dw 0
SlotTab:  .ds 96
WornMode: .dw 0
WSlot:    .dw 0
WN:       .dw 0
WK:       .dw 0
GWeapon:  .dw 0
GearOwner: .dw $FFFF
GReach:   .dw 1
GArmour:  .dw 0
ReachTab: .dw 1,2,4,8
InvRX:    .ds 128
InvRY:    .ds 128
