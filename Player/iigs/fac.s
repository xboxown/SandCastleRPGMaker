; Sandcastle RPG Maker - Apple IIgs player: factions, as the Windows Ultima 6 engine (Change Faction, Change Faction
; Relationship - Engine/FactionModel.cs): who attacks the Avatar is whose faction thinks badly enough of the Avatar's
; (its relationship, 0-100, falls in a band that attacks). Change Faction puts a character in another faction; Change
; Faction Relationship sets / adds / takes away from how a faction thinks of the Avatar's - everyone of it on the map
; turns hostile or friendly as their band says.
; GAME.DAT (after the hands): u16 the Avatar's faction, u16 bands, each (its end 0-100, 1 attacks); u16 factions, each
; id, its relationship to the Avatar's; u16 characters, each name, faction.
; Talk op 46 FAC: who (4 bytes), what (0 change faction, 1 relationship), a (faction / viewer), b (op 0 set 1 add 2 take
; away), c (value) - 12 bytes.

ParseFac:
        lda [Ptr]
        sta FcAv
        lda #2
        jsr AddPtr
        lda [Ptr]
        sta FcBN
        lda #2
        jsr AddPtr
        lda FcBN
        asl a
        asl a
        sta T4
        ldy #0
.b:     cpy T4
        bcs .bd
        lda [Ptr],y
        sta FcBands,y
        iny
        iny
        bra .b
.bd:    lda T4
        jsr AddPtr
        lda [Ptr]
        sta FcFN
        lda #2
        jsr AddPtr
        lda FcFN
        asl a
        asl a
        sta T4
        ldy #0
.f:     cpy T4
        bcs .fd
        lda [Ptr],y
        sta FcFac,y
        iny
        iny
        bra .f
.fd:    lda T4
        jsr AddPtr
        lda [Ptr]
        sta FcCN
        lda #2
        jsr AddPtr
        lda FcCN
        asl a
        asl a
        sta T4
        ldy #0
.c:     cpy T4
        bcs .cd
        lda [Ptr],y
        sta FcChr,y
        iny
        iny
        bra .c
.cd:    lda T4
        jsr AddPtr
        jmp ParseMoney          ; (money.s)

OpFac:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta FcWhat
        iny
        lda [TalkPtr],y
        sta FcA
        iny
        iny
        lda [TalkPtr],y
        sta FcB
        iny
        iny
        lda [TalkPtr],y
        sta FcC
        lda FcWhat
        bne .rel
        jsr WhoRec              ; Change Faction: its name now of faction FcA
        bcs .done
        jsr WPIsAvatar
        bcs .done
        ldy #C_NAME
        lda [WP],y
        sta FcNm
        jsr CharFacSet
        lda FcA
        sta FcF
        jsr FacRefresh
        bra .done
.rel:   lda FcA                 ; a relationship towards the Avatar's faction
        jsr FacPlace
        bcc .done
        lda FcFac+2,x
        ldy FcB
        beq .set
        cpy #1
        beq .add
        sec
        sbc FcC
        bra .clamp
.add:   clc
        adc FcC
        bra .clamp
.set:   lda FcC
.clamp: bpl .c0
        lda #0
.c0:    cmp #101
        bcc .c1
        lda #100
.c1:    sta FcFac+2,x
        lda FcA
        sta FcF
        stz FcNm                ; (everyone of that faction)
        jsr FacRefresh
.done:  jsr DrawView
        lda #12
        jmp Next

; A = a faction's id: carry set, X = its place in FcFac.
FacPlace:
        sta FcT
        ldx #0
        ldy FcFN
.l:     cpy #0
        beq .no
        lda FcFac,x
        cmp FcT
        beq .y
        inx
        inx
        inx
        inx
        dey
        bra .l
.y:     sec
        rts
.no:    clc
        rts

; Name FcNm's faction set to FcA (in FcChr, a new entry if it had none).
CharFacSet:
        ldx #0
        ldy FcCN
.l:     cpy #0
        beq .new
        lda FcChr,x
        cmp FcNm
        beq .set
        inx
        inx
        inx
        inx
        dey
        bra .l
.new:   lda FcCN
        cmp #64
        bcs .o
        inc FcCN
        lda FcNm
        sta FcChr,x
.set:   lda FcA
        sta FcChr+2,x
.o:     rts

; A = a name: carry set, A = its faction.
FacOfName:
        sta FcT
        ldx #0
        ldy FcCN
.l:     cpy #0
        beq .no
        lda FcChr,x
        cmp FcT
        beq .y
        inx
        inx
        inx
        inx
        dey
        bra .l
.y:     lda FcChr+2,x
        sec
        rts
.no:    clc
        rts

; Everyone on this map of faction FcF (FcNm: only that name; 0 everyone of it): hostile if its relationship's band attacks.
FacRefresh:
        lda FcF
        jsr FacPlace
        bcs .fb1
        brl .o
.fb1:
        lda FcFac+2,x           ; its band
        sta FcT
        stz FcHostile
        ldx #0
        ldy FcBN
.b:     cpy #0
        beq .bd
        lda FcT
        cmp FcBands,x
        bcc .got
        inx
        inx
        inx
        inx
        dey
        bra .b
.got:   lda FcBands+2,x
        sta FcHostile
.bd:    lda ChrPtr
        sta WP
        lda ChrPtr+2
        sta WP+2
        lda ChrCount
        sta FcLeft
.l:     lda FcLeft
        beq .o
        ldy #C_FLAGS
        lda [WP],y
        and #4
        bne .n
        ldy #C_NAME
        lda [WP],y
        ldx FcNm
        beq .any
        cmp FcNm
        bne .n
.any:   jsr FacOfName
        bcc .n
        cmp FcF
        bne .n
        ldy #C_FLAGS
        lda [WP],y
        and #$FFFD
        ldx FcHostile
        beq .s
        ora #2
.s:     sta [WP],y
.n:     clc
        lda WP
        adc #64
        sta WP
        lda WP+2
        adc #0
        sta WP+2
        dec FcLeft
        bra .l
.o:     rts

FcAv:     .dw 0
FcBN:     .dw 0
FcBands:  .ds 4*8
FcFN:     .dw 0
FcFac:    .ds 4*32
FcCN:     .dw 0
FcChr:    .ds 4*64
FcWhat:   .dw 0
FcA:      .dw 0
FcB:      .dw 0
FcC:      .dw 0
FcNm:     .dw 0
FcF:      .dw 0
FcT:      .dw 0
FcHostile: .dw 0
FcLeft:   .dw 0
