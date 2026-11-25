; Sandcastle RPG Maker - Apple IIgs player: breaking things, as the Windows Ultima 6 engine (U6GameWindow.ObjectCombat.cs
; StrikeThing, .Objects.cs StrikeObject, .Mechanisms.cs BreakObject - Exult's reduce_health): an object marked breakable
; (hit points, not "Cannot be destroyed", a door with no key and not magically locked) or a destructible container takes
; blows - the striker's Strength / 3: 1 + a roll of it, + 1; its Defense takes 1 + a roll of it off ("No damage."); not
; breakable: "No effect.". At 0 hit points: its destroyed sound, gone (a container's things fall out where it stood),
; "{0} breaks apart.". Attack on an object (combat.s AttackPicked) and the event command Attack Object (ext.s) strike.
; GAME.DAT (after the liquids): u16 kinds, each (name, hit points, defense, destroyed sound, flags 1 container) - 10 bytes.
; The blows so far: BrHp (saved) - up to 16 objects (object number, hit points left).

W_NOEFFECT = 113
W_NODAMAGE = 114
W_BREAKS   = 115

ParseBreak:
        lda [Ptr]
        cmp #33
        bcc .n
        lda #32
.n:     sta BrKN
        lda #2
        jsr AddPtr
        lda BrKN
        asl a
        sta T4
        asl a
        asl a
        clc
        adc T4                  ; x 10
        sta T4
        ldy #0
.l:     cpy T4
        bcs .d
        lda [Ptr],y
        sta BrKinds,y
        iny
        iny
        bra .l
.d:     lda T4
        jmp AddPtr

; ItemPtr = an object: carry set, BrK = its kind's place (x 10) if it can be broken.
BrKindOf:
        ldy #8
        lda [ItemPtr],y
        sta BrT
        ldx #0
        ldy BrKN
.l:     cpy #0
        beq .no
        lda BrKinds,x
        cmp BrT
        beq .y
        txa
        clc
        adc #10
        tax
        dey
        bra .l
.y:     stx BrK
        sec
        rts
.no:    clc
        rts

; A = the striker's Strength: the blow's damage (Windows StrikeThing: Strength / 3 > 0: 1 + a roll of it; + 1).
BlowOf:
        jsr Div3
        cmp #0
        beq .z
        jsr Random
        inc a
.z:     inc a
        rts
; A / 3
Div3:
        ldx #0
.l:     cmp #3
        bcc .d
        sec
        sbc #3
        inx
        bra .l
.d:     txa
        rts

; A blow of A at the object at ItemPtr: what happens written. Carry set: it broke.
BreakHit:
        sta BrDmg
        lda ItemPtr
        sta BrRec
        lda ItemPtr+2
        sta BrRec+2
        ldy #6
        lda [ItemPtr],y
        and #32+64+256          ; gone, carried, inside something
        bne .noeff
        jsr BrKindOf
        bcs .k
.noeff: lda #W_NOEFFECT
        bra .say
.k:     ldx BrK                 ; its defense
        lda BrKinds+4,x
        beq .d
        jsr Random
        inc a
        sta BrT
        lda BrDmg
        sec
        sbc BrT
        sta BrDmg
.d:     lda BrDmg
        beq .nod
        bpl .hit
.nod:   lda #W_NODAMAGE
.say:   pha
        jsr BrName
        pla
        jsr Format
        clc
        rts
.hit:   jsr IndexOf             ; its hit points so far
        sta BrIdx
        jsr BrSlot
        lda BrHp+2,x
        sec
        sbc BrDmg
        beq .broke
        bmi .broke
        sta BrHp+2,x
        clc
        rts
.broke: lda #$FFFF              ; its place free again
        sta BrHp,x
        ldx BrK
        lda BrKinds+6,x         ; the destroyed sound
        cmp #$FFFF
        beq .q
        ldx #255
        jsr PlaySound
.q:     jsr BrName
        lda [ItemPtr]           ; where it stood
        sta BrX
        ldy #2
        lda [ItemPtr],y
        sta BrY
        ldy #6                  ; gone
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        and #1
        beq .ns
        ldx BrX                 ; no longer in the way (the cell map: verbs.s FreeCell)
        ldy BrY
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        and #$FE
        sta [Ptr]
        rep #$20
        .a16
.ns:    ldx BrK
        lda BrKinds+8,x
        and #1
        beq .said
        jsr BrSpill
.said:  lda #W_BREAKS
        jsr Format
        jsr DrawView
        sec
        rts

; FmtName = the struck thing's name.
BrName:
        lda BrRec
        sta ItemPtr
        lda BrRec+2
        sta ItemPtr+2
        ldy #8
        lda [ItemPtr],y
        jmp NameNumber

; X = BrIdx's place in BrHp (a new one, full hit points, if it has none).
BrSlot:
        ldx #0
.l:     lda BrHp,x
        cmp BrIdx
        beq .y
        inx
        inx
        inx
        inx
        cpx #64
        bcc .l
        ldx #0                  ; none: a free place (else the first)
.f:     lda BrHp,x
        cmp #$FFFF
        beq .new
        inx
        inx
        inx
        inx
        cpx #64
        bcc .f
        ldx #0
.new:   lda BrIdx
        sta BrHp,x
        phx
        ldx BrK
        lda BrKinds+2,x
        plx
        sta BrHp+2,x
.y:     rts

; A broken container's things fall out where it stood.
BrSpill:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta BrN
        beq .d
.l:     ldy #6
        lda [ItemPtr],y
        and #256+32
        cmp #256
        bne .n
        ldy #14
        lda [ItemPtr],y
        cmp BrIdx
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #$FEFF              ; out
        sta [ItemPtr],y
        lda BrX
        sta [ItemPtr]
        ldy #2
        lda BrY
        sta [ItemPtr],y
.n:     lda #16
        jsr NextItem
        dec BrN
        bne .l
.d:     jmp BrName

; Attack picked an object (combat.s): within reach, the Avatar's blow (a turn).
AttackThing:
        lda AvRec+C_STR
        jsr BlowOf
        jsr BreakHit
        jsr DrawView
        jsr PromptLine
        lda #1
        sta TurnUsed
        rts

BrKN:     .dw 0
BrKinds:  .ds 320
BrT:      .dw 0
BrK:      .dw 0
BrDmg:    .dw 0
BrIdx:    .dw 0
BrRec:    .dw 0,0
BrX:      .dw 0
BrY:      .dw 0
BrN:      .dw 0
