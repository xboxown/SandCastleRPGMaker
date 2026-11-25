; Sandcastle RPG Maker - Apple IIgs player: monsters that join, as the Windows Ultima 6 engine (U6GameWindow.Joining.cs -
; Ultima 6's and Martian Dreams' slimes, Exult's Slime_actor): a monster set to split (Character Editor > Health > Joining)
; that survives a hit, 1 chance in 2, makes another of its kind beside it - one of its map's spares (absent copies the
; editor adds, flags 4 + $8000); and its joined look: a frame by which sides have one of its kind next to it (2 north, 4
; east, 8 south, 16 west) and 1 a wobble. GAME.DAT (after the doors): u16 kinds, each name, flags (1 splits), its joined
; look's first sprite of 32 ($FFFF none).

ParseJoin:
        lda [Ptr]
        sta JnN
        lda #2
        jsr AddPtr
        lda JnN                 ; 6 bytes each
        asl a
        sta T4
        asl a
        clc
        adc T4
        sta T4
        ldy #0
.l:     cpy T4
        bcs .d
        lda [Ptr],y
        sta JnTab,y
        iny
        iny
        bra .l
.d:     lda T4
        jmp AddPtr

; A = a name: carry set, X = its place in JnTab.
JoinKind:
        sta JnNm
        ldx #0
        ldy JnN
.l:     cpy #0
        beq .no
        lda JnTab,x
        cmp JnNm
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

; DrawCharacters: the character at ItemPtr - carry set and A = its joined look's sprite (by its neighbours of its kind).
JoinedLook:
        lda JnN
        bne .go
        clc
        rts
.go:    ldy #C_NAME
        lda [ItemPtr],y
        jsr JoinKind
        bcs .fb1
        brl .no
.fb1:
        lda JnTab+4,x
        cmp #$FFFF
        bne .fb2
        brl .no
.fb2:
        sta JnFirst
        ldy #C_NAME
        lda [ItemPtr],y
        sta JnNm
        lda [ItemPtr]
        sta JnX
        ldy #2
        lda [ItemPtr],y
        sta JnY
        lda JnX                 ; a wobble as it goes
        clc
        adc JnY
        and #1
        sta JnMask
        lda ItemPtr
        sta JnP
        lda ItemPtr+2
        sta JnP+2
        lda ChrPtr
        sta JnQ
        lda ChrPtr+2
        sta JnQ+2
        lda ChrCount
        sta JnLeft
.l:     lda JnLeft
        bne .fb3
        brl .d
.fb3:
        lda JnQ
        sta ItemPtr
        lda JnQ+2
        sta ItemPtr+2
        ldy #C_FLAGS
        lda [ItemPtr],y
        and #4
        bne .n
        ldy #C_NAME
        lda [ItemPtr],y
        cmp JnNm
        bne .n
        lda [ItemPtr]           ; beside it: which side
        sec
        sbc JnX
        sta JnDX
        ldy #2
        lda [ItemPtr],y
        sec
        sbc JnY
        sta JnDY
        lda JnDX
        bne .sx
        lda JnDY
        cmp #$FFFF
        bne .s1
        lda #2                  ; north
        bra .set
.s1:    cmp #1
        bne .n
        lda #8                  ; south
        bra .set
.sx:    lda JnDY
        bne .n
        lda JnDX
        cmp #1
        bne .w
        lda #4                  ; east
        bra .set
.w:     cmp #$FFFF
        bne .n
        lda #16                 ; west
.set:   ora JnMask
        sta JnMask
.n:     clc
        lda JnQ
        adc #64
        sta JnQ
        lda JnQ+2
        adc #0
        sta JnQ+2
        dec JnLeft
        brl .l
.d:     lda JnP
        sta ItemPtr
        lda JnP+2
        sta ItemPtr+2
        lda JnFirst
        clc
        adc JnMask
        sec
        rts
.no:    clc
        rts

; A blow DefP survived: a monster that splits, 1 chance in 2, has another of its kind appear beside it (a spare of its
; map's: absent, flags 4 + $8000). Never the Avatar or a member.
MaybeSplit:
        lda JnN
        bne .fb5
        brl .o
.fb5:
        lda DefP
        jsr RecIsAvatar         ; (solo.s)
        bcs .o
        lda DefP+2
        cmp MyBank
        beq .o                  ; (a member: the party's records are in this bank)
        ldy #C_NAME
        lda [DefP],y
        jsr JoinKind
        bcc .o
        lda JnTab+2,x
        and #1
        beq .o
        lda #2
        jsr Random
        cmp #0
        bne .o
        ldy #C_NAME
        lda [DefP],y
        sta JnNm
        lda ChrPtr              ; a spare of its kind
        sta JnQ
        lda ChrPtr+2
        sta JnQ+2
        lda ChrCount
        sta JnLeft
.l:     lda JnLeft
        beq .o
        lda JnQ
        sta WP
        lda JnQ+2
        sta WP+2
        ldy #C_FLAGS
        lda [WP],y
        and #$8000+4
        cmp #$8000+4
        bne .n
        ldy #C_NAME
        lda [WP],y
        cmp JnNm
        beq .spare
.n:     clc
        lda JnQ
        adc #64
        sta JnQ
        lda JnQ+2
        adc #0
        sta JnQ+2
        dec JnLeft
        bra .l
.o:     rts
.spare: stz JnK                 ; a free cell round the one hit
.r:     lda JnK
        cmp #8
        bcs .o
        asl a
        tax
        lda [DefP]
        clc
        adc JnRX,x
        sta JnX
        ldy #2
        lda [DefP],y
        clc
        adc JnRY,x
        sta JnY
        inc JnK
        lda JnX
        cmp PX
        bne .t
        lda JnY
        cmp PY
        beq .r
.t:     ldx JnX
        ldy JnY
        jsr MapTile
        cmp #$FFFF
        beq .r
        tay
        lda [FlagsPtr],y
        and #1
        beq .r
        ldx JnX
        ldy JnY
        jsr OccAt
        and #7
        bne .r
        sep #$20                ; there now: a character's cell
        .a8
        lda [Ptr]
        ora #2
        sta [Ptr]
        rep #$20
        .a16
        lda JnX
        sta [WP]
        ldy #2
        lda JnY
        sta [WP],y
        ldy #C_FLAGS
        lda [WP],y
        and #$7FFB
        sta [WP],y
        lda #1
        sta Moved
        rts

JnN:      .dw 0
JnTab:    .ds 6*16
JnNm:     .dw 0
JnFirst:  .dw 0
JnX:      .dw 0
JnY:      .dw 0
JnDX:     .dw 0
JnDY:     .dw 0
JnMask:   .dw 0
JnP:      .dd 0
JnQ:      .dd 0
JnLeft:   .dw 0
JnK:      .dw 0
JnRX:     .dw 1, $FFFF, 0, 0, 1, 1, $FFFF, $FFFF
JnRY:     .dw 0, 0, 1, $FFFF, 1, $FFFF, 1, $FFFF
