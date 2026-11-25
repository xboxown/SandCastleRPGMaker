; Sandcastle RPG Maker - Apple IIgs player: each party member's own things, as the Windows Ultima 6 engine (every member
; carries and wears their own; Move gives to another member - "To whom?"). A carried thing's +14: its owner - $FFFF the
; Avatar, $8000 + map x 64 + place (the character's own, a member or one who may join). The panel shows the shown
; member's things and gear (0-9); each member's weapon and armour come from what they wear.

W_TOWHOM = 68
W_GIVES  = 69

; CurOwner = the shown member's code (ViewIdx 0: the Avatar).
SetOwner:
        lda ViewIdx
        bne .m
        lda #$FFFF
        sta CurOwner
        rts
.m:     dec a
        jsr MemberAt
        jsr MPCode
        sta CurOwner
        rts

; A = the member at MP's owner code.
MPCode:
        ldy #P_MAP
        lda [MP],y
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ldy #P_IDX
        ora [MP],y
        ora #$8000
        rts

; Carry set if the carried thing at ItemPtr is the Avatar's or a member's.
PartyOwns:
        ldy #14
        lda [ItemPtr],y
        cmp #$FFFF
        beq .yes
        sta PO
        lda MP                  ; (MP kept)
        pha
        lda MP+2
        pha
        stz POK
.l:     lda POK
        cmp PartyN
        bcs .no
        jsr MemberAt
        jsr MPCode
        cmp PO
        beq .y2
        inc POK
        bra .l
.no:    pla
        sta MP+2
        pla
        sta MP
        ldy #6
        clc
        rts
.y2:    pla
        sta MP+2
        pla
        sta MP
.yes:   ldy #6
        sec
        rts

; Each member's weapon, reach and armour from what they wear.
MembersGear:
        stz POK
.l:     lda POK
        cmp PartyN
        bcs .d
        jsr MemberAt
        ldy #P_MAP              ; summoned: their own stats
        lda [MP],y
        cmp #$FFFF
        bne .own
        inc POK
        bra .l
.own:
        jsr MPCode
        sta GearOwner
        lda MP
        pha
        lda MP+2
        pha
        jsr GearOf
        pla
        sta MP+2
        pla
        sta MP
        ldy #C_WEAPON
        lda GWeapon
        sta [MP],y
        ldy #C_REACH
        lda GReach
        sta [MP],y
        ldy #C_ARMOUR
        lda GArmour
        sta [MP],y
        lda WP                  ; its worn magic's stats (worn.s)
        pha
        lda WP+2
        pha
        lda MP
        sta WP
        lda MP+2
        sta WP+2
        lda POK
        inc a
        jsr MagicApply
        pla
        sta WP+2
        pla
        sta WP
        inc POK
        bra .l
.d:     lda #$FFFF
        sta GearOwner
        rts

; Move's "Where?" with a carried thing: a digit - 0 the Avatar, 1-9 member n - gives it to them. Carry set if taken.
GiveDigit:
        cmp #'0'
        bcc .no
        cmp #'9'+1
        bcs .no
        ldx Verb
        cpx #VERB_MOVE
        bne .no
        ldx MoveIdx
        cpx #$FFFF
        beq .no
        sec
        sbc #'0'
        bne .mem
        stz Mode                ; 0: the Avatar
        jsr NewLine
        jmp ToPack
.mem:   cmp PartyN
        beq .ok
        bcs .no
.ok:    dec a
        jsr MemberAt
        stz Mode
        jsr NewLine
        jsr GiveToMP
        sec
        rts
.no:    clc
        rts

; Move onto a character (ChrRec): a member gets it; anyone else: Not possible.
GiveToChr:
        lda ChrRec+2
        cmp MyBank
        bne .np
        lda ChrRec
        sec
        sbc #PartyTab
        cmp #512
        bcs .np
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        jsr MemberAt
        jsr GiveToMP
        rts
.np:    lda #9
        jsr Format
        brl MoveDone

; The thing being moved: the member at MP's - "{1} gets {0}."
GiveToMP:
        jsr MPCode
        sta PO
        ldy #C_NAME             ; {1} their name
        lda [MP],y
        jsr NameNumber
        lda FmtName
        sta WhoName
        lda FmtName+2
        sta WhoName+2
        lda MoveIdx             ; too heavy for them? (moves.s)
        ldx PO
        jsr WeightCheck
        bcs .wok
        brl MoveDone
.wok:   jsr LeaveMap
        lda MoveIdx
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #$0EFF              ; carried, not worn, not in a bag
        ora #64
        sta [ItemPtr],y
        ldy #14
        lda PO
        sta [ItemPtr],y
        jsr CloseIfOpen
        jsr MoveName            ; {0} the thing
        lda #W_GIVES
        jsr Tell                ; (its own line; the turn's prompt follows - AfterTurn)
        lda #$FFFF
        sta MoveIdx
        stz Mode
        jsr DrawView
        jmp RefreshInventory

CurOwner: .dw $FFFF
PO:       .dw 0
POK:      .dw 0
