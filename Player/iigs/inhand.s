; Sandcastle RPG Maker - Apple IIgs player: weapons drawn in the hand, as the Windows Ultima 6 engine (Exult's
; Actor::paint_weapon; Sprite Editor > Weapon in hand, each character frame's hand point, each weapon frame's grip): who
; holds a weapon (in a hand slot) is drawn with it - its Held picture (facing east / west the editor's mirrored copy) set
; with its grip on the hand point of the look shown; behind the body where that frame says so. (Swings: not yet.)
; GAME.DAT (after the toughness): u16 hands, each a character look's sprite, the hand's x, y from its standing point
; (bottom middle), 1 behind; u16 weapons, each name, held sprite, mirrored sprite, grip x, y from its standing point.

ParseHands:
        lda [Ptr]
        sta HhN
        lda #2
        jsr AddPtr
        lda HhN                 ; 8 bytes each
        asl a
        asl a
        asl a
        sta T4
        ldy #0
.h:     cpy T4
        bcs .hd
        lda [Ptr],y
        sta HhTab,y
        iny
        iny
        bra .h
.hd:    lda T4
        jsr AddPtr
        lda [Ptr]
        sta HwN
        lda #2
        jsr AddPtr
        lda HwN                 ; 10 bytes each
        asl a
        sta T4
        asl a
        asl a
        clc
        adc T4
        sta T4
        ldy #0
.w:     cpy T4
        bcs .wd
        lda [Ptr],y
        sta HwTab,y
        iny
        iny
        bra .w
.wd:    lda T4
        jsr AddPtr
        jmp ParseFac            ; (fac.s)

; Before / after a character is drawn: A = the look drawn (its sprite), X = its owner code, CellX, CellY its place on the
; screen (DrawSprite's). HandFind: carry set if it holds a weapon with a held look - HwSpr, HwDX, HwDY ready, HwBack 1 if
; behind. Then HandDraw draws it.
HandFind:
        sta HhLook
        stx HhOwner
        lda HhN
        beq .no
        lda HwN
        beq .no
        ldx #0                  ; its look's hand point
        ldy HhN
.l:     cpy #0
        beq .no
        lda HhTab,x
        cmp HhLook
        beq .f
        txa
        clc
        adc #8
        tax
        dey
        bra .l
.no:    clc
        rts
.f:     lda HhTab+2,x
        sta HhX
        lda HhTab+4,x
        sta HhY
        lda HhTab+6,x
        sta HwBack
        lda ObjPtr              ; what it holds: a weapon in a hand slot (10, 11)
        sta HhP
        lda ObjPtr+2
        sta HhP+2
        lda ObjCount
        sta HhLeft
.o:     lda HhLeft
        beq .no
        lda HhP
        sta Ptr
        lda HhP+2
        sta Ptr+2
        ldy #6
        lda [Ptr],y
        and #$F000+96+256
        cmp #$A000+64
        beq .slot
        cmp #$B000+64
        bne .on
.slot:  ldy #14
        lda [Ptr],y
        cmp HhOwner
        bne .on
        ldy #8                  ; a weapon with a held look
        lda [Ptr],y
        jsr HeldLook
        bcs .got
.on:    clc
        lda HhP
        adc #16
        sta HhP
        lda HhP+2
        adc #0
        sta HhP+2
        dec HhLeft
        bra .o
.got:   sec
        rts

; A = a name: carry set - HwSpr (held, or mirrored facing east / west by HhFace), HwDX, HwDY (its standing point moved
; so its grip sits on the hand) - if it is a weapon with a held look.
HeldLook:
        sta HhNm
        ldx #0
        ldy HwN
.l:     cpy #0
        beq .no
        lda HwTab,x
        cmp HhNm
        beq .f
        txa
        clc
        adc #10
        tax
        dey
        bra .l
.no:    clc
        rts
.f:     lda HhFace              ; facing east / west: the mirrored look
        cmp #2
        bcc .ns
        lda HwTab+4,x
        bra .s
.ns:    lda HwTab+2,x
.s:     sta HwSpr
        lda HhX                 ; hand - grip, from the standing points
        sec
        sbc HwTab+6,x
        sta HwDX
        lda HhY
        sec
        sbc HwTab+8,x
        sta HwDY
        sec
        rts

; Draws the weapon found (HwSpr) at CellX + HwDX, CellY + HwDY (CellX, CellY kept).
HandDraw:
        lda CellX
        pha
        lda CellY
        pha
        lda SprId
        pha
        lda CellX
        clc
        adc HwDX
        sta CellX
        lda CellY
        clc
        adc HwDY
        sta CellY
        lda HwSpr
        sta SprId
        jsr DrawSprite
        pla
        sta SprId
        pla
        sta CellY
        pla
        sta CellX
        rts

; A character drawn: A = its look, X = its owner code, HhFace its facing - its weapon drawn behind it now (HandBefore)
; or over it after (HandAfter).

; DrawCharacters: the map character at ItemPtr, its look SprId.
CharHand:
        stz HwPend
        ldy #8
        lda [ItemPtr],y
        sta HhFace
        lda ItemPtr             ; its owner code: $8000 + map x 64 + its place
        sec
        sbc ChrPtr
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        sta HhIdx
        lda CurMap
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ora HhIdx
        ora #$8000
        tax
        lda SprId
        jmp HandBefore

; DrawParty: the member at MP, its look SprId.
MemberHand:
        stz HwPend
        ldy #8
        lda [MP],y
        sta HhFace
        jsr MPCode              ; (owner.s)
        tax
        lda SprId
        jmp HandBefore

; DrawView, before DrawAvatar: the one moved (its look: AvRec+4 + Facing; the Avatar's are sprites too).
AvatarHand:
        stz HwPend
        ldx PX
        ldy PY
        jsr CellOnScreen
        bcc .o
        lda Facing
        sta HhFace
        lda AvRec+4
        clc
        adc Facing
        ldx #$FFFF
        jmp HandBefore
.o:     rts
HandBefore:
        jsr HandFind
        bcc .no
        lda HwBack
        beq .later
        jsr HandDraw
        stz HwPend
        sec
        rts
.later: lda #1
        sta HwPend
.no:    clc
        rts
HandAfter:
        lda HwPend
        beq .o
        stz HwPend
        jmp HandDraw
.o:     rts

HhN:      .dw 0
HhTab:    .ds 8*128
HwN:      .dw 0
HwTab:    .ds 10*32
HhLook:   .dw 0
HhOwner:  .dw 0
HhX:      .dw 0
HhY:      .dw 0
HhP:      .dd 0
HhLeft:   .dw 0
HhNm:     .dw 0
HhFace:   .dw 0
HwSpr:    .dw 0
HwDX:     .dw 0
HwDY:     .dw 0
HwBack:   .dw 0
HwPend:   .dw 0
HhIdx:    .dw 0
