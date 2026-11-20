; Sandcastle RPG Maker - Apple IIgs player: formulas (Engine/ScriptExpression.cs CompileIIgs makes their stack code).
; Every value has a kind (tag): 0 a number (whole, -32768..32767), 1 a text (its slot, texts.s; 0 = ""), 2 an array,
; 3 a grid. As Windows (ScriptExpression): + adds two numbers (a text that reads as one too), else joins as text;
; - * / % numbers; comparisons numbers, else texts without case; a text is true if not empty.
; 1 n push, 2 v a variable, 3 + 4 - 5 * 6 / 7 %, 8 negate, 9 = 10 != 11 < 12 > 13 <= 14 >=, 15 and 16 or 17 not,
; 18 random(a, b) 19 chance(n) 20 min 21 max 22 abs 23 clamp(x, a, b), 24 the Avatar's stat (a byte: its word),
; 25 the hour, 26 has a thing named (u16), 27 the minute, 28 a vehicle travelling (u16), 29 a text (u8 length, its
; letters), 30 an array (u8), 31 a grid (u8), 32 array[i], 33 grid[x, y], 34 a function (u8 which, u8 how many values),
; 35 the game's (u8 which, u16 map, u16 place of whom: $FFFF the Avatar; stat: u8 its word; items: u16 a name); 0 the end.

; CALC variable formula
OpCalc:
        iny
        lda [TalkPtr],y
        asl a
        sta CalcVar
        iny
        iny
        jsr EvalExpr
        sty TPC
        ldx CalcVar
        jsr StoreVar            ; (texts.s)
        jmp RunTalk

; IFX formula place: on if true, else to the place
OpIfx:
        iny
        jsr EvalExpr
        ldx PTag
        jsr Truth
        bne .t
        lda [TalkPtr],y
        sta TPC
        jmp RunTalk
.t:     iny
        iny
        sty TPC
        jmp RunTalk

; The formula at [TalkPtr],y: A = its value, PTag its kind, Y past its end.
EvalExpr:
        stz ExSP
        stz PTag                ; (a number pushed is a number)
.l:     lda [TalkPtr],y
        and #$00FF
        iny
        cmp #0
        bne .op
        jsr Pop                 ; the value, its kind in PTag
        pha
        lda QTag
        sta PTag
        pla
        rts
.op:    cmp #36
        bcs .l
        asl a
        tax
        jsr (ExJump,x)
        bra .l

ExJump:   .dw ExEnd,ExPush,ExVar,ExAdd,ExSub,ExMul,ExDiv,ExMod,ExNeg,ExEq,ExNe,ExLt,ExGt,ExLe,ExGe,ExAnd,ExOr,ExNot
          .dw ExRand,ExChance,ExMin,ExMax,ExAbs,ExClamp,ExStat,ExHour,ExHave,ExMinute,ExVBusy
          .dw ExText,ExList,ExGrid,ExIndex1,ExIndex2,ExFn,ExGame
ExEnd:  rts

; the stack (32 values): A, its kind PTag
Push:
        ldx ExSP
        cpx #64
        bcs .o
        sta ExStack,x
        lda PTag
        sta ExTags,x
        inx
        inx
        stx ExSP
.o:     stz PTag
        rts
Pop:
        ldx ExSP
        beq .z
        dex
        dex
        stx ExSP
        lda ExTags,x
        sta QTag
        lda ExStack,x
        rts
.z:     stz QTag
        lda #0
        rts
; B (ExB, BTag) = the top, A (ATag) = the one under it (both popped)
Pop2:
        jsr Pop
        sta ExB
        lda QTag
        sta BTag
        jsr Pop
        sta ExA
        lda QTag
        sta ATag
        lda ExA
        rts
; the two as numbers: A and ExB
Nums2:
        jsr Pop2
        lda ExB
        ldx BTag
        jsr ValNum
        sta ExB
        lda ExA
        ldx ATag
        jmp ValNum
; the top as a number
Num1:
        jsr Pop
        ldx QTag
        jmp ValNum

; Z clear (A not 0) if the value A (kind X) is true.
Truth:
        cpx #1
        bne .n
        jsr TxtLen              ; a text: not empty
.n:     cmp #0
        rts

ExPush: lda [TalkPtr],y
        iny
        iny
        jmp Push
ExVar:  lda [TalkPtr],y
        iny
        iny
        asl a
        tax
        lda VarTag,x
        sta PTag
        lda VarTab,x
        jmp Push
ExAdd:  jsr Pop2                ; two numbers (or texts that read as numbers): added; else joined
        lda ATag
        ora BTag
        beq .num
        lda ExA
        ldx ATag
        jsr IsNum
        bcc .join
        lda ExB
        ldx BTag
        jsr IsNum
        bcc .join
        lda ExB
        ldx BTag
        jsr ValNum
        sta ExB
        lda ExA
        ldx ATag
        jsr ValNum
.num:   lda ExA
        clc
        adc ExB
        jmp Push
.join:  jsr JoinAB              ; (texts.s)
        ldx #1
        stx PTag
        jmp Push
ExSub:  jsr Nums2
        sec
        sbc ExB
        jmp Push
ExMul:  jsr Nums2
        jsr SMul
        jmp Push
ExDiv:  jsr Nums2
        jsr SDiv
        lda ExQ
        jmp Push
ExMod:  jsr Nums2
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
ExNeg:  jsr Num1
        eor #$FFFF
        inc a
        jmp Push
ExEq:   jsr Pop2
        jsr CmpAB
        beq True
        bra False
ExNe:   jsr Pop2
        jsr CmpAB
        bne True
        bra False
ExLt:   jsr Pop2
        jsr CmpAB
        bmi True
        bra False
ExGt:   jsr Pop2
        jsr CmpAB
        beq False
        bpl True
        bra False
ExLe:   jsr Pop2
        jsr CmpAB
        beq True
        bmi True
        bra False
ExGe:   jsr Pop2
        jsr CmpAB
        bpl True
        bra False
True:   lda #1
        jmp Push
False:  lda #0
        jmp Push
ExAnd:  jsr Pop2
        lda ExA
        ldx ATag
        jsr Truth
        beq False
        lda ExB
        ldx BTag
        jsr Truth
        beq False
        bra True
ExOr:   jsr Pop2
        lda ExA
        ldx ATag
        jsr Truth
        bne True
        lda ExB
        ldx BTag
        jsr Truth
        bne True
        bra False
ExNot:  jsr Pop
        ldx QTag
        jsr Truth
        beq True
        bra False
ExRand: jsr Nums2               ; random(a, b): a..b
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
ExChance: jsr Num1              ; chance(n): n in 100
        sta ExA
        lda #100
        phy
        jsr Random
        ply
        cmp ExA
        bcs .bf1
        jmp True
.bf1:
        brl False
ExMin:  jsr Nums2
        jsr SCmp
        bmi .a
        lda ExB
        jmp Push
.a:     lda ExA
        jmp Push
ExMax:  jsr Nums2
        jsr SCmp
        bpl .a
        lda ExB
        jmp Push
.a:     lda ExA
        jmp Push
ExAbs:  jsr Num1
        bpl .p
        eor #$FFFF
        inc a
.p:     jmp Push
ExClamp: jsr Num1               ; clamp(x, a, b)
        sta ExHi
        jsr Num1
        sta ExLo
        jsr Num1
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
ExTags:   .ds 64
PTag:     .dw 0
QTag:     .dw 0
ATag:     .dw 0
BTag:     .dw 0
ExA:      .dw 0
ExB:      .dw 0
ExQ:      .dw 0
ExR:      .dw 0
ExSg:     .dw 0
ExDv:     .dw 0
ExLo:     .dw 0
ExHi:     .dw 0
