; Sandcastle RPG Maker - Apple IIgs player: shooting and throwing, as the Windows Ultima 6 engine (Exult's ranged weapons):
; a strike on someone further than a square flies its missile across the view first - the weapon's projectile, else its
; ammo, else the thrown weapon. The Avatar and members: the worn weapon's entry (Weapons): ammo of its family is taken
; from the party's things (none: Not possible.); a weapon thrown itself lands by the target (a stranger's after that).
; Characters: their missile in their word 23 (+62).

ParseWeapons:
        lda [Ptr]
        sta WpnCount
        lda #2
        jsr AddPtr
        ldy #0
.l:     cpy #64*8
        bcs .d
        tya
        lsr a
        lsr a
        lsr a
        cmp WpnCount
        bcs .d
        lda [Ptr],y
        sta WpnTab,y
        iny
        iny
        bra .l
.d:     lda WpnCount
        asl a
        asl a
        asl a
        jmp AddPtr

; Before a blow (AtkP strikes DefP): from afar, the missile; carry set if it can't be done (no ammo).
Shoot:
        lda [AtkP]              ; how far
        sec
        sbc [DefP]
        jsr Abs
        sta T4
        ldy #2
        lda [AtkP],y
        sec
        sbc [DefP],y
        jsr Abs
        cmp T4
        bcs .m
        lda T4
.m:     cmp #2
        bcs .far
        clc
        rts
.far:   lda #$FFFF
        sta MSprite
        stz MUses
        jsr AtkWeapon           ; the Avatar's or a member's: its worn weapon's entry
        bcc .chr
        lda MUses
        cmp #3                  ; charges: one used (items.s); none left - no shot
        bne .n3
        jsr UseCharge
        bcc .fly
        lda #9
        jsr Tell
        sec
        rts
.n3:    cmp #1
        bne .fly
        jsr TakeAmmo            ; one of its family's ammo
        bcc .fly
        lda #9                  ; Not possible. (no ammo)
        jsr Tell
        sec
        rts
.chr:   lda AtkP                ; a character: its own missile (word 23)
        cmp #AvRec
        beq .fly
        ldy #62
        lda [AtkP],y
        sta MSprite
.fly:   lda MSprite
        cmp #$FFFF
        beq .done
        jsr Fly
.done:  lda MUses               ; thrown itself: it lands by the target
        cmp #2
        bne .ok
        jsr ThrownLands
.ok:    clc
        rts

; The worn weapon of AtkP (the Avatar or a member): its entry - MSprite, MUses, MFamily, MWeapon (the object). Carry set if found.
AtkWeapon:
        bra .go
.nj:    brl .no
.go:
        lda AtkP
        ldx AtkP+2
        cpx MyBank
        bne .nj
        cmp #AvRec
        bne .mem
        lda #$FFFF
        bra .own
.mem:   sec                     ; a member: its owner code
        sbc #PartyTab
        cmp #512
        bcs .nj
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        jsr MemberAt
        jsr MPCode
.own:   sta MOwner
        lda ObjPtr              ; its worn weapon (Other: 1 weapon)
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T5
        beq .nj
.l:     ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp #$1000+64
        bcc .n
        ldy #14
        lda [ItemPtr],y
        cmp MOwner
        bne .n
        ldy #10
        lda [ItemPtr],y
        cmp #$FFFF
        beq .n
        and #$C000
        cmp #$4000
        bne .n
        lda ItemPtr
        sta MWeapon
        lda ItemPtr+2
        sta MWeapon+2
        ldy #8                  ; its name: in Weapons?
        lda [ItemPtr],y
        sta T4
        ldx #0
.w:     txa
        lsr a
        lsr a
        lsr a
        cmp WpnCount
        bcc .wk
        clc
        rts
.wk:
        lda WpnTab,x
        cmp T4
        beq .got
        txa
        clc
        adc #8
        tax
        bra .w
.got:   lda WpnTab+2,x
        sta MUses
        lda WpnTab+4,x
        sta MFamily
        lda WpnTab+6,x
        sta MSprite
        sec
        rts
.n:     lda #16
        jsr NextItem
        dec T5
        bne .l
.no:    clc
        rts

; One ammo of family MFamily taken from the party's things ($C800 + family in Other). Carry set if there was none.
TakeAmmo:
        lda MFamily
        ora #$C800
        sta T4
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T5
        beq .none
.l:     ldy #10
        lda [ItemPtr],y
        cmp T4
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #96+256
        cmp #64
        bne .n
        jsr PartyOwns
        bcc .n
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        jsr RefreshInventory
        clc
        rts
.n:     lda #16
        jsr NextItem
        dec T5
        bne .l
.none:  sec
        rts

; The thrown weapon (MWeapon) lands on the target's square: on this map, no one's.
ThrownLands:
        lda MWeapon
        sta ItemPtr
        lda MWeapon+2
        sta ItemPtr+2
        lda [DefP]
        sta [ItemPtr]
        ldy #2
        lda [DefP],y
        sta [ItemPtr],y
        ldy #14
        lda #$FFFF
        sta [ItemPtr],y
        ldy #6
        lda [ItemPtr],y
        and #$0EBF
        ora CurBits
        sta [ItemPtr],y
        jmp RefreshInventory

; The missile (MSprite) flies from AtkP to DefP, a square at a time (the view drawn, it over it, put on the screen).
Fly:
        lda [AtkP]
        sta FlX
        ldy #2
        lda [AtkP],y
        sta FlY
        lda #1
        sta NoBlit
.l:     lda [DefP]              ; a step toward the target
        sec
        sbc FlX
        jsr Sign
        clc
        adc FlX
        sta FlX
        ldy #2
        lda [DefP],y
        sec
        sbc FlY
        jsr Sign
        clc
        adc FlY
        sta FlY
        lda FlX                 ; there: done
        cmp [DefP]
        bne .draw
        ldy #2
        lda FlY
        cmp [DefP],y
        beq .end
.draw:  jsr DrawView            ; (not put on the screen: NoBlit)
        jsr ViewCorner
        ldx FlX
        ldy FlY
        jsr CellOnScreen
        bcc .shown
        lda MSprite
        sta SprId
        jsr DrawSprite
.shown: jsr BlitView
        pha                     ; a moment: 3 ticks
        pha
        tool $2503
        pla
        sta FlT
        pla
.w:     pha
        pha
        tool $2503
        pla
        sta T4
        pla
        lda T4
        sec
        sbc FlT
        cmp #3
        bcc .w
        bra .l
.end:   stz NoBlit
        jmp DrawView

NoBlit:   .dw 0
FlX:      .dw 0
FlY:      .dw 0
FlT:      .dw 0
MSprite:  .dw $FFFF
MUses:    .dw 0
MFamily:  .dw 0
MOwner:   .dw 0
MWeapon:  .dd 0
WpnCount: .dw 0
WpnTab:   .ds 64*8
