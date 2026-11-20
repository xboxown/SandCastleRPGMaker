; Sandcastle RPG Maker - Apple IIgs player: wands, scrolls and potions, and weapons with charges, as the Windows Ultima 6
; engine (U6GameWindow.Consumables.cs, Weapons.cs). A useable object (special 5) or a potion (6) has a spell of its own
; (its effects): Use - on the user, or On whom? (a scroll that asks for a target; every potion: who drinks it). A useable
; with charges counts them down (ChgObj / ChgLeft, saved with the game): used up it is gone, or shows its used-up look and
; does nothing more. A potion is gone, leaving what it leaves (an empty bottle) in its place. A weapon that fires charges
; (Weapons: uses 3) counts them down too - none left: no shot; gone when empty if it says so.

SP_USEABLE = 5
SP_POTION  = 6
CHARGES    = 64

; Use on a wand, scroll or potion at ItemPtr (SpRec, A its kind). Carry set (done).
UseItem:
        sta UType
        jsr ObjIdx
        sta UObj
        lda UType
        cmp #SP_USEABLE
        bne .pot
        lda SpRec+3             ; charges: none left - nothing happens
        beq .ok
        lda UObj
        ldx SpRec+3
        jsr ChargeGet
        cmp #0
        bne .ok
        lda #CrNothing
        jmp CrSayEnd
.ok:    lda SpRec+9             ; its sound
        bra .snd
.pot:   lda SpRec+3
.snd:   cmp #$FFFF
        beq .spell
        ldx #255
        jsr PlaySound
.spell: lda SpRec+1             ; its spell into SpBuf
        jsr SpellAt
        ldy #0
.cp:    lda [ItemPtr],y
        sta SpBuf,y
        iny
        iny
        cpy #64
        bcc .cp
        lda UObj
        jsr ObjAddr
        lda UType               ; a target? (a potion: who drinks it)
        cmp #SP_POTION
        beq .target
        lda SpRec+6
        and #$00FF
        beq .self
.target: lda UObj
        inc a
        sta ItemCast
        lda PenX
        clc
        adc SpaceW
        sta PenX
        lda #W_ONWHOM
        jsr PrintSaid
        lda #VERB_CAST
        sta Verb
        lda PX
        sta CurX
        lda PY
        sta CurY
        lda #4
        sta Mode
        jsr DrawView
        sec
        rts
.self:  jsr NewLine
        jsr AvatarWP
        jsr ApplySpell
        jsr ItemSpent
        jsr SpellPrompt
        sec
        rts

; After a cast's target (magic.s CastAt): the wand, scroll or potion it came from, spent.
ItemCastDone:
        lda ItemCast
        beq .o
        dec a
        sta UObj
        stz ItemCast
        jsr ObjAddr
        jsr IsSpecial
        bcc .o
        sta UType
        jmp ItemSpent
.o:     rts
; (the target refused: nothing spent)
ItemCastOff:
        stz ItemCast
        rts

; Object UObj (SpRec, UType): a charge used (none left: gone, or its used-up look); a potion: what it leaves.
ItemSpent:
        lda UObj
        jsr ObjAddr
        lda UType
        cmp #SP_POTION
        beq .potion
        lda SpRec+3             ; charges
        beq .o
        lda UObj
        ldx SpRec+3
        jsr ChargeGet
        dec a
        bpl .k
        lda #0
.k:     pha
        lda UObj
        plx
        jsr ChargeSet
        cpx #0
        bne .o
        lda SpRec+5             ; used up: gone, or its used-up look
        and #$00FF
        beq .look
        jmp Vanish
.look:  lda SpRec+7
        cmp #$FFFF
        beq .o
        ldy #4
        sta [ItemPtr],y
        ldy #12
        sta [ItemPtr],y
        jsr RefreshInventory
.o:     rts
.potion: lda SpRec+5            ; drunk: what it leaves in its place, else gone
        cmp #$FFFF
        bne .left
        jmp Vanish
.left:  ldy #8
        sta [ItemPtr],y
        ldy #4
        lda SpRec+7
        sta [ItemPtr],y
        ldy #12
        lda SpRec+9
        sta [ItemPtr],y
        ldy #6                  ; where it is kept; what it is from the new one
        lda [ItemPtr],y
        and #$F000+64+256
        sta T4
        lda SpRec+11
        and #$0FFF-64-256-32
        ora T4
        sta [ItemPtr],y
        ldy #10
        lda SpRec+13
        sta [ItemPtr],y
        jmp RefreshInventory
; The object at ItemPtr gone (a spare again).
Vanish:
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        ldy #8
        lda #$FFFF
        sta [ItemPtr],y
        jsr RefreshInventory
        jmp DrawView

; A = the charges left of object A (X: as many as a new one has, if not counted yet).
ChargeGet:
        sta UC
        stx UDef
        ldx #0
.l:     cpx ChgN
        bcs .new
        lda ChgObj,x
        cmp UC
        beq .got
        inx
        inx
        bra .l
.got:   lda ChgLeft,x
        rts
.new:   lda UDef
        rts
; Object A's charges left = X.
ChargeSet:
        sta UC
        stx UDef
        ldx #0
.l:     cpx ChgN
        bcs .new
        lda ChgObj,x
        cmp UC
        beq .set
        inx
        inx
        bra .l
.new:   cpx #CHARGES*2
        bcs .o
        lda UC
        sta ChgObj,x
        inc ChgN
        inc ChgN
.set:   lda UDef
        sta ChgLeft,x
.o:     ldx UDef
        rts

; A weapon firing charges (missile.s): one used. Carry set if it had none left.
UseCharge:
        lda MWeapon
        sta ItemPtr
        lda MWeapon+2
        sta ItemPtr+2
        jsr ObjIdx
        sta UObj
        lda MFamily
        and #$7FFF
        tax
        lda UObj
        jsr ChargeGet
        cmp #0
        bne .ok
        sec
        rts
.ok:    dec a
        tax
        lda UObj
        jsr ChargeSet
        cpx #0
        bne .c
        lda MFamily             ; the last one: gone, if it says so
        bpl .c
        lda MWeapon
        sta ItemPtr
        lda MWeapon+2
        sta ItemPtr+2
        jsr Vanish
.c:     clc
        rts

UType:    .dw 0
UObj:     .dw 0
UC:       .dw 0
UDef:     .dw 0
ItemCast: .dw 0
