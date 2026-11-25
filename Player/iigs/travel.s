; Sandcastle RPG Maker - Apple IIgs player: things a traveller carries, as the Windows Ultima 6 engine
; (U6GameWindow.Travel.cs, from Exult). Special kinds (crafting.s's records):
;  7 Sextant (origin x, y; the map's units a cell; units a degree): Use - "3 North, 12 East." ((cell - origin) x units /
;    units a degree, toward 0); under a roof "{0} can't see the sky from in here.".
;  8 Clock (sundial 0/1): Use - " 3:05pm"; a sundial under a roof or by night: "{0} casts no shadow.".
;  9 Map (its picture's sprite; show where 0/1, outdoors only 0/1, needs a sextant 0/1; the area it shows: left, top,
;    right, bottom - left $FFFF the whole map): Use - its picture over the view, a red cross where the party is (if it
;    shows that, and a sextant is carried when it needs one), till a key or a click.
; 10 Keychain: Use, "On what?" - on the Avatar: every key the party carries goes onto it ("3 keys added to the
;    keyring."); on a door or chest: it works as whichever key on it fits, else "It's not on the keyring.". The ring
;    (RingKeys, saved with the game) holds the keys' codes.

SP_SEXTANT = 7
SP_CLOCK   = 8
SP_MAP     = 9
SP_KEYRING = 10

W_SKYHIDDEN = 95
W_NOSHADOW  = 96
W_NORTH     = 97
W_SOUTH     = 98
W_WEST      = 99
W_EAST      = 100
W_KEYSADDED = 101
W_NOKEYS    = 102
W_NOTONRING = 103

; Use on a carried / lying travel thing at ItemPtr (SpRec its record, A its kind). Carry set if done.
TravelUse:
        cmp #SP_SEXTANT
        bne .c
        jmp UseSextant
.c:     cmp #SP_CLOCK
        bne .m
        jmp UseClock
.m:     cmp #SP_MAP
        bne .k
        jmp UseMap
.k:     cmp #SP_KEYRING
        bne .no
        lda ItemPtr             ; as a key: "On what?" (keys.s)
        sta GridRec
        lda ItemPtr+2
        sta GridRec+2
        jsr KeyPicked
        sec
        rts
.no:    clc
        rts

; FmtName = the thing at ItemPtr's name.
TrName:
        ldy #8
        lda [ItemPtr],y
        jmp NameNumber          ; (verbs.s)

; ---- Sextant ----
UseSextant:
        jsr NewLine
        jsr UnderRoof
        bcc .sky
        jsr TrName
        lda #W_SKYHIDDEN
        jsr Format
        jmp .end
.sky:   lda SpRec+3             ; latitude: from the origin's row ($FFFF: the map's middle)
        cmp #$FFFF
        bne .oy
        lda MapH
        lsr a
.oy:    sta TrT
        lda PY
        sec
        sbc TrT
        jsr ShortY
        jsr Degrees
        ldx #W_NORTH
        bcs .ns
        ldx #W_SOUTH
.ns:    jsr TrSay
        lda SpRec+1             ; longitude
        cmp #$FFFF
        bne .ox
        lda MapW
        lsr a
.ox:    sta TrT
        lda PX
        sec
        sbc TrT
        jsr ShortX
        jsr Degrees
        ldx #W_WEST
        bcs .we
        ldx #W_EAST
.we:    jsr TrSay
.end:   jsr PromptLine
        sec
        rts

; A = cells from the origin (signed): A = |cells x units / units a degree| (toward 0), carry set if it was negative.
Degrees:
        stz TrNeg
        bpl .p
        eor #$FFFF
        inc a
        inc TrNeg
.p:     sta MulA
        lda SpRec+5
        and #$00FF
        sta MB0
        jsr Mul16
        lda SpRec+6             ; units a degree (at least 1)
        and #$00FF
        bne .d
        inc a
.d:     sta TrPer
        stz TrQ
        lda MulR
.l:     cmp TrPer
        bcc .o
        sec
        sbc TrPer
        inc TrQ
        bra .l
.o:     lda TrNeg
        lsr a                   ; carry = negative
        lda TrQ
        rts

; A = a number, X = the word ("{0} North,"): said.
TrSay:
        phx
        ldx #0
        jsr Decimal             ; into StatBuf (stats.s)
        sep #$20
        .a8
        lda #0
        sta StatBuf,x
        rep #$20
        .a16
        lda #StatBuf
        sta FmtName
        lda MyBank
        sta FmtName+2
        pla
        jmp Format

; ---- Clock ----
UseClock:
        jsr NewLine
        lda SpRec+1             ; a sundial: under a roof or by night, no shadow
        and #$00FF
        beq .tell
        jsr UnderRoof
        bcs .none
        lda Hour
        cmp #6
        bcc .none
        cmp #18
        bcc .tell
.none:  jsr TrName
        lda #W_NOSHADOW
        jsr Format
        bra .end
.tell:  sep #$20                ; " 3:05pm"
        .a8
        lda #' '
        sta StatBuf
        rep #$20
        .a16
        lda Hour
.h:     cmp #12
        bcc .h2
        sbc #12
        bra .h
.h2:    cmp #0
        bne .h3
        lda #12
.h3:    ldx #1
        jsr Decimal
        sep #$20
        .a8
        lda #':'
        sta StatBuf,x
        inx
        rep #$20
        .a16
        lda Minute
        cmp #10
        bcs .mm
        sep #$20
        .a8
        lda #'0'
        sta StatBuf,x
        inx
        rep #$20
        .a16
        lda Minute
.mm:    jsr Decimal
        sep #$20
        .a8
        lda #'a'
        ldy Hour
        cpy #12
        bcc .ap
        lda #'p'
.ap:    sta StatBuf,x
        lda #'m'
        sta StatBuf+1,x
        lda #0
        sta StatBuf+2,x
        rep #$20
        .a16
        lda #StatBuf
        sta SayP
        lda MyBank
        sta SayP+2
        ldy #0
        jsr SayText
.end:   jsr PromptLine
        sec
        rts

; ---- Map ----
UseMap:
        jsr ViewWH              ; (weather.s)
        jsr DrawView
        lda SpRec+1             ; its picture, in the middle of the view
        sta SprId
        jsr SpriteWH            ; TrW, TrH
        lda WVW
        lsr a
        clc
        adc ClipX0
        sec
        sbc #8
        sta CellX
        lda WVH
        sec
        sbc TrH
        lsr a
        sta TrY0                ; (its top in the view)
        clc
        adc TrH
        clc
        adc ClipY0
        sec
        sbc #16
        sta CellY
        lda WVW
        sec
        sbc TrW
        lsr a
        sta TrX0
        jsr DrawSprite
        jsr MapCross
        jsr BlitView
.l:     pha                     ; till a key or a click
        pea $000A
        pea #bank(EvRec)
        pea #EvRec
        tool $0A06
        pla
        beq .l
        jsr DrawView
        jsr PromptLine
        sec
        rts

; TrW, TrH: sprite SprId's size.
SpriteWH:
        lda SprId
        asl a
        asl a
        tay
        lda [SprTab],y
        sta T0
        iny
        iny
        lda [SprTab],y
        sta T1
        clc
        lda SprBank
        adc T0
        sta Src
        lda SprBank+2
        adc T1
        sta Src+2
        lda [Src]
        sta TrW
        ldy #2
        lda [Src],y
        sta TrH
        rts

; The cross where the party is, if the map shows it.
MapCross:
        lda SpRec+3
        and #$00FF
        bne .sh
        rts
.sh:    lda SpRec+4             ; outdoors only
        and #$00FF
        beq .od
        jsr UnderRoof
        bcc .od
        rts
.od:    lda SpRec+5             ; needs a sextant
        and #$00FF
        beq .se
        jsr HasSextant
        bcs .se
        rts
.se:    lda SpRec+6             ; the area: the whole map, or its cells
        cmp #$FFFF
        bne .area
        stz TrAX
        stz TrAY
        lda MapW
        sta TrAW
        lda MapH
        sta TrAH
        bra .in
.area:  sta TrAX
        lda SpRec+8
        sta TrAY
        lda SpRec+10
        sec
        sbc TrAX
        inc a
        sta TrAW
        lda SpRec+12
        sec
        sbc TrAY
        inc a
        sta TrAH
.in:    lda PX                  ; inside it?
        sec
        sbc TrAX
        bmi .o
        cmp TrAW
        bcs .o
        sta MulA                ; x = left + (PX - ax) x width / area width
        lda TrW
        sta MB0
        jsr Mul16
        lda TrAW
        jsr TrDiv
        clc
        adc TrX0
        sta TrCX
        lda PY
        sec
        sbc TrAY
        bmi .o
        cmp TrAH
        bcs .o
        sta MulA
        lda TrH
        sta MB0
        jsr Mul16
        lda TrAH
        jsr TrDiv
        clc
        adc TrY0
        sta TrCY
        lda SpRec+14            ; the palette's reddest colour
        sta WCol
        lda #$FFFE              ; a cross, 5 pixels
        sta TrI
.c:     lda TrCX
        clc
        adc TrI
        sta WX
        lda TrCY
        sta WY
        jsr Plot
        lda TrCX
        sta WX
        lda TrCY
        clc
        adc TrI
        sta WY
        jsr Plot
        inc TrI
        lda TrI
        cmp #3
        bne .c
.o:     rts

; A = a divisor: A = MulR / it (16 bits; repeated subtraction).
TrDiv:
        sta TrPer
        stz TrQ
        lda MulR
.l:     cmp TrPer
        bcc .o
        sec
        sbc TrPer
        inc TrQ
        bra .l
.o:     lda TrQ
        rts

; Carry set if the party carries a sextant.
HasSextant:
        lda ItemPtr
        pha
        lda ItemPtr+2
        pha
        lda SpRec+0             ; (SpRec kept: IsSpecial copies over it)
        pha
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta TrI
        stz TrQ
.l:     lda TrI
        beq .d
        ldy #6
        lda [ItemPtr],y
        and #96+256
        cmp #64
        bne .n
        jsr PartyOwns
        bcc .n
        ldy #10
        lda [ItemPtr],y
        and #$F000
        cmp #$D000
        bne .n
        lda [ItemPtr],y
        and #$0FFF
        sta TrT
        jsr SpKindOf
        cmp #SP_SEXTANT
        bne .n
        inc TrQ
.n:     lda #16
        jsr NextItem
        dec TrI
        bne .l
.d:     pla
        sta SpRec+0
        pla
        sta ItemPtr+2
        pla
        sta ItemPtr
        lda TrQ
        cmp #1
        rts

; A = a special's number: A = its kind (without copying its record).
SpKindOf:
        cmp SpCount
        bcs .no
        asl a
        tay
        lda Src
        pha
        lda Src+2
        pha
        lda SpOffs
        sta Src
        lda SpOffs+2
        sta Src+2
        lda [Src],y
        clc
        adc SpBlob
        sta Src
        lda SpBlob+2
        adc #0
        sta Src+2
        lda [Src]
        and #$00FF
        tax
        pla
        sta Src+2
        pla
        sta Src
        txa
        rts
.no:    lda #0
        rts

; ---- Keychain ----
; KeyOn's key (KeyRec) a keychain? Carry set.
IsRing:
        lda KeyRec
        sta Src
        lda KeyRec+2
        sta Src+2
        ldy #10
        lda [Src],y
        and #$F000
        cmp #$D000
        bne .no
        lda [Src],y
        and #$0FFF
        jsr SpKindOf
        cmp #SP_KEYRING
        bne .no
        sec
        rts
.no:    clc
        rts

; The keychain on the Avatar: every key the party carries onto it.
RingAdd:
        stz TrQ
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta TrI
.l:     lda TrI
        beq .d
        ldy #6
        lda [ItemPtr],y
        and #96+256+512
        cmp #64+512
        bne .n
        jsr PartyOwns
        bcc .n
        lda RingN
        cmp #32
        bcs .d
        asl a
        tax
        ldy #10
        lda [ItemPtr],y
        sta RingKeys,x
        inc RingN
        inc TrQ
        ldy #6                  ; the key gone onto the ring
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
.n:     lda #16
        jsr NextItem
        dec TrI
        bne .l
.d:     jsr RefreshInventory
        lda TrQ
        bne .some
        lda #W_NOKEYS
        jsr PrintSaid
        jmp PromptLine
.some:  ldx #W_KEYSADDED
        jsr TrSay
        jmp PromptLine

; The keychain on the door / chest at ItemPtr: carry set if a key on it fits (A = that code), else "It's not on the
; keyring." said.
RingFits:
        jsr LockCode
        sta TrT
        ldx #0
.l:     txa
        lsr a
        cmp RingN
        bcs .no
        lda RingKeys,x
        cmp TrT
        beq .y
        inx
        inx
        bra .l
.y:     lda TrT
        sec
        rts
.no:    lda #W_NOTONRING
        jsr PrintSaid
        clc
        rts

TrNeg:    .dw 0
TrPer:    .dw 0
TrQ:      .dw 0
TrW:      .dw 0
TrH:      .dw 0
TrX0:     .dw 0
TrY0:     .dw 0
TrAX:     .dw 0
TrAY:     .dw 0
TrAW:     .dw 0
TrAH:     .dw 0
TrCX:     .dw 0
TrCY:     .dw 0
TrI:      .dw 0
TrT:      .dw 0
