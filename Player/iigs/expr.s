; Sandcastle RPG Maker - Apple IIgs player: formulas (Engine/ScriptExpression.cs CompileIIgs makes their stack code):
; 1 n push, 2 v a variable, 3 + 4 - 5 * 6 / 7 %, 8 negate, 9 = 10 != 11 < 12 > 13 <= 14 >=, 15 and 16 or 17 not,
; 18 random(a, b) 19 chance(n) 20 min 21 max 22 abs 23 clamp(x, a, b), 24 the Avatar's stat (a byte: its word),
; 25 the hour, 26 has a thing named (u16), 27 the minute; 0 the end. Whole numbers, -32768..32767.

; CALC variable formula
OpCalc:
        iny
        lda [TalkPtr],y
        asl a
        sta CalcVar
        iny
        iny
        jsr EvalExpr
        ldx CalcVar
        sta VarTab,x
        sty TPC
        jmp RunTalk

; IFX formula place: on if true, else to the place
OpIfx:
        iny
        jsr EvalExpr
        cmp #0
        bne .t
        lda [TalkPtr],y
        sta TPC
        jmp RunTalk
.t:     iny
        iny
        sty TPC
        jmp RunTalk

; The formula at [TalkPtr],y: A = its value, Y past its end.
EvalExpr:
        stz ExSP
.l:     lda [TalkPtr],y
        and #$00FF
        iny
        cmp #0
        bne .op
        jsr Pop
        rts
.op:    cmp #28
        bcs .l
        asl a
        tax
        jsr (ExJump,x)
        bra .l

ExJump:   .dw ExEnd,ExPush,ExVar,ExAdd,ExSub,ExMul,ExDiv,ExMod,ExNeg,ExEq,ExNe,ExLt,ExGt,ExLe,ExGe,ExAnd,ExOr,ExNot
          .dw ExRand,ExChance,ExMin,ExMax,ExAbs,ExClamp,ExStat,ExHour,ExHave,ExMinute
ExEnd:  rts

; the stack (32 numbers)
Push:
        ldx ExSP
        cpx #64
        bcs .o
        sta ExStack,x
        inx
        inx
        stx ExSP
.o:     rts
Pop:
        ldx ExSP
        beq .z
        dex
        dex
        stx ExSP
        lda ExStack,x
        rts
.z:     lda #0
        rts
; B = the top, A = the one under it (both popped)
Pop2:
        jsr Pop
        sta ExB
        jmp Pop

ExPush: lda [TalkPtr],y
        iny
        iny
        jmp Push
ExVar:  lda [TalkPtr],y
        iny
        iny
        asl a
        tax
        lda VarTab,x
        jmp Push
ExAdd:  jsr Pop2
        clc
        adc ExB
        jmp Push
ExSub:  jsr Pop2
        sec
        sbc ExB
        jmp Push
ExMul:  jsr Pop2
        jsr SMul
        jmp Push
ExDiv:  jsr Pop2
        jsr SDiv
        lda ExQ
        jmp Push
ExMod:  jsr Pop2
        jsr SDiv
        lda ExR                 ; (as Windows: never below 0)
        bpl .p
        lda ExB
        bpl .bp
        eor #$FFFF
        inc a
.bp:    clc
        adc ExR
.p:     jmp Push
ExNeg:  jsr Pop
        eor #$FFFF
        inc a
        jmp Push
ExEq:   jsr Pop2
        cmp ExB
        beq True
        bra False
ExNe:   jsr Pop2
        cmp ExB
        bne True
        bra False
ExLt:   jsr Pop2
        jsr SCmp
        bmi True
        bra False
ExGt:   jsr Pop2
        jsr SCmp
        beq False
        bpl True
        bra False
ExLe:   jsr Pop2
        jsr SCmp
        beq True
        bmi True
        bra False
ExGe:   jsr Pop2
        jsr SCmp
        bpl True
        bra False
True:   lda #1
        jmp Push
False:  lda #0
        jmp Push
ExAnd:  jsr Pop2
        cmp #0
        beq False
        lda ExB
        beq False
        bra True
ExOr:   jsr Pop2
        cmp #0
        bne True
        lda ExB
        bne True
        bra False
ExNot:  jsr Pop
        cmp #0
        beq True
        bra False
ExRand: jsr Pop2                ; random(a, b): a..b
        sta ExA
        lda ExB
        sec
        sbc ExA
        bpl .o
        eor #$FFFF              ; (the other way round)
        inc a
        pha
        lda ExB
        sta ExA
        pla
.o:     inc a
        phy
        jsr Random
        ply
        clc
        adc ExA
        jmp Push
ExChance: jsr Pop               ; chance(n): n in 100
        sta ExA
        lda #100
        phy
        jsr Random
        ply
        cmp ExA
        bcc True
        bra False
ExMin:  jsr Pop2
        jsr SCmp
        bmi .a
        lda ExB
        jmp Push
.a:     lda ExA
        jmp Push
ExMax:  jsr Pop2
        jsr SCmp
        bpl .a
        lda ExB
        jmp Push
.a:     lda ExA
        jmp Push
ExAbs:  jsr Pop
        bpl .p
        eor #$FFFF
        inc a
.p:     jmp Push
ExClamp: jsr Pop                ; clamp(x, a, b)
        sta ExHi
        jsr Pop
        sta ExLo
        jsr Pop
        sta ExA
        sta ExB
        lda ExLo
        jsr SCmpTo              ; x < a: a
        bpl .hi
        lda ExLo
        jmp Push
.hi:    lda ExHi
        jsr SCmpTo
        bmi .x
        beq .x
        lda ExHi
        jmp Push
.x:     lda ExA
        jmp Push
ExStat: lda [TalkPtr],y
        and #$00FF
        iny
        asl a
        tax
        lda AvRec+16,x
        jmp Push
ExHour: lda Hour
        jmp Push
ExMinute: lda Minute
        jmp Push
ExHave: lda [TalkPtr],y
        iny
        iny
        phy
        jsr CountItem
        ply
        cmp #0
        bne .t
        jmp False
.t:     jmp True

; A against ExB, signed: N set if A < B, Z if equal (A kept in ExA).
SCmp:
        sta ExA
        sec
        sbc ExB
        bvc .o
        eor #$8000
.o:     rts
; ExA against A (ExB = A): as SCmp, x = ExA.
SCmpTo:
        sta ExB
        lda ExA
        jmp SCmp

; A x ExB (signed, low 16 bits)
SMul:
        sta MulA
        lda ExB
        sta MB0
        phy
        jsr Mul16
        ply
        lda MulR
        rts

; A / ExB, signed: ExQ, ExR (dividing by 0: 0, 0).
SDiv:
        sta ExA
        stz ExQ
        stz ExR
        lda ExB
        beq .o
        stz ExSg
        lda ExA
        bpl .ap
        eor #$FFFF
        inc a
        sta ExA
        lda #1
        sta ExSg
.ap:    lda ExB
        bpl .bp
        eor #$FFFF
        inc a
        sta ExDv
        lda ExSg
        eor #2
        sta ExSg
        bra .go
.bp:    sta ExDv
.go:    phx
        ldx #16                 ; ExA / ExDv, unsigned
        lda #0
.l:     asl ExA
        rol a
        cmp ExDv
        bcc .n
        sbc ExDv
        inc ExA
.n:     dex
        bne .l
        plx
        sta ExR
        lda ExA
        sta ExQ
        lda ExSg                ; the signs: quotient negative if they differ, remainder as the dividend
        and #1
        beq .r
        lda ExR
        eor #$FFFF
        inc a
        sta ExR
.r:     lda ExSg
        beq .o
        cmp #3
        beq .o
        lda ExQ
        eor #$FFFF
        inc a
        sta ExQ
.o:     rts

CalcVar:  .dw 0
ExSP:     .dw 0
ExStack:  .ds 64
ExA:      .dw 0
ExB:      .dw 0
ExQ:      .dw 0
ExR:      .dw 0
ExSg:     .dw 0
ExDv:     .dw 0
ExLo:     .dw 0
ExHi:     .dw 0
