; Sandcastle RPG Maker - Apple IIgs player: damage types and getting back up, as the Windows Ultima 6 engine
; (U6GameWindow.Toughness.cs - the user wanted them for Ultima 6 too, 2026-11-24): a blow's damage type is its striker's
; worn weapon's (Object Editor > Weapon > Damage type; none: normal); the one struck may be immune to it, take half, or
; take it adjusted (Character Editor > Health > Damage types). A character set to get back up rises from its body after
; its time - with what the body held - unless what killed it was a type that stops it.
; GAME.DAT (after the worn magic): u16 weapons, each name, type; u16 responses, each name, type, kind (1 immune, 2 half,
; 3 adjust), amount; u16 risers, each name, least turns, most turns, the types that stop it (a bit each).

ParseTough:
        lda [Ptr]
        sta TwN
        lda #2
        jsr AddPtr
        lda TwN                 ; 4 bytes each
        asl a
        asl a
        sta T4
        ldy #0
.w:     cpy T4
        bcs .wd
        lda [Ptr],y
        sta TwTab,y
        iny
        iny
        bra .w
.wd:    lda T4
        jsr AddPtr
        lda [Ptr]
        sta TrsN
        lda #2
        jsr AddPtr
        lda TrsN                ; 8 bytes each
        asl a
        asl a
        asl a
        sta T4
        ldy #0
.r:     cpy T4
        bcs .rd
        lda [Ptr],y
        sta TrsTab,y
        iny
        iny
        bra .r
.rd:    lda T4
        jsr AddPtr
        lda [Ptr]
        sta TbN
        lda #2
        jsr AddPtr
        lda TbN                 ; 8 bytes each
        asl a
        asl a
        asl a
        sta T4
        ldy #0
.b:     cpy T4
        bcs .bd
        lda [Ptr],y
        sta TbTab,y
        iny
        iny
        bra .b
.bd:    lda T4
        jsr AddPtr
        stz RsN
        rts

; Strike, before the damage lands: HitType = the striker's (AtkP) weapon's type; Dmg as the one struck (DefP) takes it.
BlowType:
        stz HitType
        lda TwN
        bne .fb1
        brl .resp
.fb1:
        lda AtkP                ; the striker's owner code
        jsr RecIsAvatar         ; (solo.s)
        bcc .nav
        lda #$FFFF
        bra .code
.nav:   lda AtkP+2
        cmp MyBank
        bne .chr
        lda MP                  ; a member: its slot's code (MP kept)
        pha
        lda MP+2
        pha
        lda AtkP
        sta MP
        lda AtkP+2
        sta MP+2
        jsr MPCode              ; (owner.s)
        tax
        pla
        sta MP+2
        pla
        sta MP
        txa
        bra .code
.chr:   lda DefP                ; a map character: as DefCode works it out (bodies.s)
        pha
        lda DefP+2
        pha
        lda AtkP
        sta DefP
        lda AtkP+2
        sta DefP+2
        jsr DefCode
        tax
        pla
        sta DefP+2
        pla
        sta DefP
        txa
.code:  sta TwCode
        lda ObjPtr              ; its worn weapon with a type
        sta TwP
        lda ObjPtr+2
        sta TwP+2
        lda ObjCount
        sta TwLeft
.l:     lda TwLeft
        beq .resp
        lda TwP
        sta Ptr
        lda TwP+2
        sta Ptr+2
        ldy #6
        lda [Ptr],y
        and #$F000+96+256
        cmp #$1000+64           ; carried and in a slot
        bcc .n
        and #$0FFF
        cmp #64
        bne .n
        ldy #14
        lda [Ptr],y
        cmp TwCode
        bne .n
        ldy #8
        lda [Ptr],y
        jsr WeaponType
        bcc .n
        sta HitType
        bra .resp
.n:     clc
        lda TwP
        adc #16
        sta TwP
        lda TwP+2
        adc #0
        sta TwP+2
        dec TwLeft
        bra .l
.resp:  ldy #C_NAME             ; the one struck: its rule for that type
        lda [DefP],y
        jmp Respond

; A = a name: carry set and A = its damage type if it is a weapon with one.
WeaponType:
        sta TwNm
        ldx #0
        ldy TwN
.l:     cpy #0
        beq .no
        lda TwTab,x
        cmp TwNm
        beq .yes
        inx
        inx
        inx
        inx
        dey
        bra .l
.yes:   lda TwTab+2,x
        sec
        rts
.no:    clc
        rts

; A = the name of the one struck: Dmg as its rule for HitType makes it (immune 0, half, adjusted).
Respond:
        sta TwNm
        ldx #0
        ldy TrsN
.l:     cpy #0
        beq .o
        lda TrsTab,x
        cmp TwNm
        bne .n
        lda TrsTab+2,x
        cmp HitType
        beq .f
.n:     txa
        clc
        adc #8
        tax
        dey
        bra .l
.o:     rts
.f:     lda TrsTab+4,x
        cmp #1
        bne .half
        stz Dmg                 ; immune
        rts
.half:  cmp #2
        bne .adj
        lda Dmg
        inc a
        lsr a
        sta Dmg
        rts
.adj:   lda Dmg
        clc
        adc TrsTab+6,x
        bpl .a
        lda #0
.a:     sta Dmg
        rts

; Dies (a character, its body made - BIdx): one that gets back up starts its time, unless HitType stops it.
MaybeRise:
        lda TbN
        beq .o
        lda BIdx
        cmp #$FFFF
        beq .o                  ; no body to rise from
        ldy #C_NAME
        lda [DefP],y
        sta TwNm
        ldx #0
        ldy TbN
.l:     cpy #0
        beq .o
        lda TbTab,x
        cmp TwNm
        beq .f
        txa
        clc
        adc #8
        tax
        dey
        bra .l
.o:     rts
.f:     lda HitType             ; killed by a type that stops it: no
        phx
        tax
        lda #1
.sh:    cpx #0
        beq .m
        asl a
        dex
        bra .sh
.m:     plx
        and TbTab+6,x
        bne .o
        lda TbTab+4,x           ; its time: least .. most turns
        sec
        sbc TbTab+2,x
        inc a
        phx
        jsr Random
        plx
        clc
        adc TbTab+2,x
        inc a
        sta TwT
        lda RsN
        cmp #16
        bcs .o
        asl a
        asl a
        asl a
        tax
        lda DefP
        sta RsTab,x
        lda DefP+2
        sta RsTab+2,x
        lda BIdx
        sta RsTab+4,x
        lda TwT
        sta RsTab+6,x
        inc RsN
        rts

; Each turn: those whose time is up rise from their body (if it still lies on the map), with what it held.
RiseTurn:
        ldx #0
.l:     txa
        lsr a
        lsr a
        lsr a
        cmp RsN
        bcc .one
        rts
.one:   dec RsTab+6,x
        beq .rise
        txa
        clc
        adc #8
        tax
        bra .l
.rise:  stx TwX
        lda RsTab,x
        sta WP
        lda RsTab+2,x
        sta WP+2
        lda RsTab+4,x
        sta TwBody
        jsr ObjAt               ; the body: still on the map?
        ldy #6
        lda [ItemPtr],y
        and #32+64+256+$F000
        cmp CurBits
        beq .fb2
        brl .drop
.fb2:
        lda [ItemPtr]           ; up where its body lies
        sta [WP]
        ldy #2
        lda [ItemPtr],y
        sta [WP],y
        ldy #6
        lda [ItemPtr],y
        ora #32                 ; the body gone
        sta [ItemPtr],y
        ldy #C_FLAGS
        lda [WP],y
        and #$FFFB-32
        sta [WP],y
        ldy #C_MAXHP
        lda [WP],y
        ldy #C_HP
        sta [WP],y
        lda DefP                ; what the body held: its own again
        pha
        lda DefP+2
        pha
        lda WP
        sta DefP
        lda WP+2
        sta DefP+2
        jsr DefCode
        sta TwCode
        pla
        sta DefP+2
        pla
        sta DefP
        lda ObjPtr
        sta TwP
        lda ObjPtr+2
        sta TwP+2
        lda ObjCount
        sta TwLeft
        stz TwI
.c:     lda TwLeft
        beq .cd
        lda TwP
        sta Ptr
        lda TwP+2
        sta Ptr+2
        ldy #6
        lda [Ptr],y
        and #256+32
        cmp #256
        bne .cn
        ldy #14
        lda [Ptr],y
        cmp TwBody
        bne .cn
        ldy #6
        lda [Ptr],y
        and #$0FFF-256
        ora #64
        sta [Ptr],y
        ldy #14
        lda TwCode
        sta [Ptr],y
.cn:    clc
        lda TwP
        adc #16
        sta TwP
        lda TwP+2
        adc #0
        sta TwP+2
        dec TwLeft
        bra .c
.cd:    jsr MakeOccAgain        ; (spells2.s)
        jsr DrawView
.drop:  ldx TwX                 ; off the list
.sh:    lda RsTab+8,x
        sta RsTab,x
        inx
        inx
        cpx #15*8
        bcc .sh
        dec RsN
        ldx TwX
        brl .l

HitType:  .dw 0
TwN:      .dw 0
TwTab:    .ds 4*32
TrsN:     .dw 0
TrsTab:   .ds 8*48
TbN:      .dw 0
TbTab:    .ds 8*16
RsN:      .dw 0
RsTab:    .ds 8*17
TwNm:     .dw 0
TwCode:   .dw 0
TwP:      .dd 0
TwLeft:   .dw 0
TwT:      .dw 0
TwX:      .dw 0
TwBody:   .dw 0
TwI:      .dw 0
