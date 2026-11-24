; Sandcastle RPG Maker - Apple IIgs player: texts, arrays and grids (Windows' ScriptRuntime): the variables hold a number
; or a text (VarTag 1: VarTab is the text's slot); a text lives in a slot of 47 letters (slot 0 is always "", 1-16 are
; for the moment a formula is worked out, 17-127 are kept - a variable's, an array element's, a grid cell's own).
; Arrays: 16 of up to 64 values; grids: 8, their cells from a pool of 2048 (packed together again when it is full).
; All of it is saved with the game (SCRIPTDATA..SCRIPTEND).

SLOT    = 48
TEMPS   = 16
FIRSTKEEP = 17
SLOTS   = 128
LISTMAX = 64
GRIDCELL0 = 1024                ; a grid's cells come after the arrays' (16 x 64)
CELLS   = 3072

; From GAME.DAT (after the leveling): the variables that start as texts, the maps' names.
ParseScript:
        ldx #0                  ; everything empty
.z:     stz SCRIPTDATA,x
        inx
        inx
        cpx #SCRIPTEND-SCRIPTDATA
        bcc .z
        lda #GRIDCELL0
        sta GTop
        lda [Ptr]
        sta T4
        lda #2
        jsr AddPtr
.t:     lda T4
        beq .td
        lda [Ptr]               ; a text variable: "" (slot 0)
        asl a
        tax
        lda #1
        sta VarTag,x
        stz VarTab,x
        lda #2
        jsr AddPtr
        dec T4
        bra .t
.td:    lda [Ptr]
        sta MapNameN
        lda #2
        jsr AddPtr
        ldy #0
.m:     tya
        lsr a
        cmp MapNameN
        bcs .md
        lda [Ptr],y
        tyx
        sta MapNameTab,x
        iny
        iny
        bra .m
.md:    lda MapNameN
        asl a
        jsr AddPtr
        jmp ParseHud            ; (hud.s)

; ---- slots ----
; X = slot A's place in TextHeap.
TxtAddr:
        asl a
        asl a
        asl a
        asl a
        sta TA
        asl a
        clc
        adc TA
        tax
        rts

; A = a new slot for a moment (emptied).
NewTemp:
        lda TempNext
        inc TempNext
        and #TEMPS-1
        inc a
        pha
        jsr TxtAddr
        stz TextHeap,x
        pla
        rts

; A = a slot kept (0: none left).
Alloc:
        ldx #FIRSTKEEP*2
.l:     cpx #SLOTS*2
        bcs .none
        lda FreeMap,x
        beq .got
        inx
        inx
        bra .l
.got:   lda #1
        sta FreeMap,x
        txa
        lsr a
        pha
        jsr TxtAddr
        stz TextHeap,x
        pla
        rts
.none:  lda #0
        rts

; Slot A no longer kept.
Free:
        cmp #FIRSTKEEP
        bcc .o
        cmp #SLOTS
        bcs .o
        asl a
        tax
        stz FreeMap,x
.o:     rts

; A = how many letters slot A has (Y kept).
TxtLen:
        phy
        jsr TxtAddr
        ldy #0
.l:     lda TextHeap,x
        and #$00FF
        beq .d
        inx
        iny
        bra .l
.d:     tya
        ply
        rts

; Slot CSrc copied into slot CDst (Y kept).
CopyTxt:
        phy
        lda CSrc
        cmp CDst
        beq .o
        jsr TxtAddr
        stx CA
        lda CDst
        jsr TxtAddr
        txy
        ldx CA
        lda #SLOT-1
        sta CN
        sep #$20
        .a8
.l:     lda TextHeap,x
        sta TextHeap,y
        beq .d
        inx
        iny
        dec CN
        bne .l
        lda #0
        sta TextHeap,y
.d:     rep #$20
        .a16
.o:     ply
        rts

; Slot CDst += slot CSrc (at most 47 letters; Y kept).
AppendTxt:
        phy
        lda CDst
        jsr TxtLen
        sta CN
        lda CDst
        jsr TxtAddr
        txa
        clc
        adc CN
        tay                     ; the end of CDst
        lda CSrc
        jsr TxtAddr
        sep #$20
        .a8
.l:     lda CN
        cmp #SLOT-1
        bcs .d
        lda TextHeap,x
        beq .d
        sta TextHeap,y
        inx
        iny
        inc CN
        bra .l
.d:     lda #0
        sta TextHeap,y
        rep #$20
        .a16
        ply
        rts

; A = a slot for a moment with the number A written (Y kept).
NumToText:
        phy
        pha
        jsr NewTemp
        sta NTId
        pla
        ldx #0
        cmp #$8000
        bcc .p
        eor #$FFFF
        inc a
        pha
        sep #$20
        .a8
        lda #'-'
        sta StatBuf
        rep #$20
        .a16
        pla
        ldx #1
.p:     jsr Decimal
        sep #$20
        .a8
        lda #0
        sta StatBuf,x
        rep #$20
        .a16
        lda NTId
        jsr TxtAddr
        ldy #0
        sep #$20
        .a8
.c:     lda StatBuf,y
        sta TextHeap,x
        beq .d
        inx
        iny
        bra .c
.d:     rep #$20
        .a16
        lda NTId
        ply
        rts

; Carry set if slot A reads as a whole number: A = it (Y kept).
ParseTxt:
        phy
        jsr TxtAddr
        stz PNeg
        stz PVal
        stz PDig
.sp:    lda TextHeap,x          ; (spaces first)
        and #$00FF
        cmp #' '
        bne .s
        inx
        bra .sp
.s:     cmp #'-'
        bne .d
        inc PNeg
        inx
.d:     lda TextHeap,x
        and #$00FF
        beq .end
        cmp #' '
        beq .trail
        sec
        sbc #'0'
        cmp #10
        bcs .no
        sta T5
        lda PVal                ; x 10 + the figure
        asl a
        sta PT
        asl a
        asl a
        clc
        adc PT
        clc
        adc T5
        sta PVal
        inc PDig
        inx
        bra .d
.trail: inx                     ; spaces after: fine
        lda TextHeap,x
        and #$00FF
        beq .end
        cmp #' '
        beq .trail
        bra .no
.end:   lda PDig
        beq .no
        lda PVal
        ldx PNeg
        beq .y
        eor #$FFFF
        inc a
.y:     ply
        sec
        rts
.no:    lda #0
        ply
        clc
        rts

; Carry set if the value A (kind X) is a number (a text that reads as one too).
IsNum:
        cpx #0
        beq .y
        cpx #1
        bne .n
        jmp ParseTxt
.y:     sec
        rts
.n:     clc
        rts
; A = the value A (kind X) as a number (a text that isn't one: 0; an array: its length).
ValNum:
        cpx #0
        beq .o
        cpx #1
        bne .l
        jsr ParseTxt
        bcs .o
        lda #0
.o:     rts
.l:     cpx #2
        bne .z
        asl a
        tax
        lda ListN,x
        rts
.z:     lda #0
        rts
; A = the value A (kind X) as a text (its slot).
ValText:
        cpx #1
        beq .o
        cpx #0
        bne .z
        jmp NumToText
.z:     lda #0
.o:     rts

; A = a slot for a moment: ExA (ATag) and ExB (BTag) joined (Y kept).
JoinAB:
        lda ExB
        ldx BTag
        jsr ValText
        sta JB
        lda ExA
        ldx ATag
        jsr ValText
        sta CSrc
        jsr NewTemp
        sta CDst
        jsr CopyTxt
        lda JB
        sta CSrc
        jsr AppendTxt
        lda CDst
        rts

; ExA (ATag) against ExB (BTag): numbers as numbers, else texts without case. N set if less, Z if equal (Y kept).
CmpAB:
        phy
        lda ExA
        ldx ATag
        jsr IsNum
        bcc .txt
        sta CmpX
        lda ExB
        ldx BTag
        jsr IsNum
        bcc .txt
        sta ExB
        lda CmpX
        jsr SCmp
        beq .eq
        bmi .lt
        bra .gt
.txt:   lda ExA
        ldx ATag
        jsr ValText
        jsr TxtAddr
        stx CA
        lda ExB
        ldx BTag
        jsr ValText
        jsr TxtAddr
        txy
        ldx CA
.l:     lda TextHeap,x
        and #$00FF
        jsr Upper
        sta CmpX
        lda TextHeap,y
        and #$00FF
        jsr Upper
        cmp CmpX
        bne .ne
        cmp #0
        beq .eq
        inx
        iny
        bra .l
.ne:    bcc .gt                 ; (A's letter is the bigger)
.lt:    ply
        lda #$FFFF
        rts
.eq:    ply
        lda #0
        rts
.gt:    ply
        lda #1
        rts

; A's letter in capitals.
Upper:
        cmp #'a'
        bcc .o
        cmp #'z'+1
        bcs .o
        and #$DF
.o:     rts

; ---- variables and cells ----
; Variable X (its number x 2) = A, of kind PTag: a text is copied into the variable's own slot.
StoreVar:
        sta SVal
        stx SVx
        lda PTag
        cmp #1
        beq .text
        jsr FreeVarText
        ldx SVx
        lda SVal
        sta VarTab,x
        stz VarTag,x
        rts
.text:  ldx SVx
        lda VarTag,x
        cmp #1
        bne .new
        lda VarTab,x
        cmp #FIRSTKEEP
        bcs .have
.new:   lda SVal
        beq .empty
        jsr Alloc
.have:  sta CDst
        lda SVal
        sta CSrc
        beq .empty2
        jsr CopyTxt
        ldx SVx
        lda CDst
        sta VarTab,x
        lda #1
        sta VarTag,x
        rts
.empty2: lda CDst
        jsr TxtAddr
        stz TextHeap,x
        ldx SVx
        lda CDst
        sta VarTab,x
        lda #1
        sta VarTag,x
        rts
.empty: ldx SVx
        stz VarTab,x
        lda #1
        sta VarTag,x
        rts
FreeVarText:
        ldx SVx
        lda VarTag,x
        cmp #1
        bne .o
        lda VarTab,x
        jmp Free
.o:     rts

; Variable X = the number A (Set Variable, the game's own variables; X and Y kept).
SetVarNum:
        phy
        phx
        pha
        stx SVx
        jsr FreeVarText
        pla
        plx
        sta VarTab,x
        stz VarTag,x
        ply
        rts

; Cell X (its number x 2) = A, of kind PTag (a text: the cell's own slot).
StoreCell:
        sta SVal
        stx SVx
        lda PTag
        cmp #1
        beq .text
        jsr FreeCellText
        ldx SVx
        lda SVal
        sta CellV,x
        lda PTag
        sta CellT,x
        rts
.text:  ldx SVx
        lda CellT,x
        cmp #1
        bne .new
        lda CellV,x
        cmp #FIRSTKEEP
        bcs .have
.new:   lda SVal
        beq .empty
        jsr Alloc
.have:  sta CDst
        lda SVal
        sta CSrc
        jsr CopyTxt
        ldx SVx
        lda CDst
        sta CellV,x
        lda #1
        sta CellT,x
        rts
.empty: jsr FreeCellText
        ldx SVx
        stz CellV,x
        lda #1
        sta CellT,x
        rts
FreeCellText:
        ldx SVx
        lda CellT,x
        cmp #1
        bne .o
        lda CellV,x
        jmp Free
.o:     rts

; ---- expression ops ----
ExText: lda [TalkPtr],y         ; a text: into a slot for the moment
        and #$00FF
        sta CN
        iny
        jsr NewTemp
        sta NTId
        jsr TxtAddr
.l:     lda CN
        beq .d
        lda [TalkPtr],y
        sep #$20
        .a8
        sta TextHeap,x
        rep #$20
        .a16
        inx
        iny
        dec CN
        bra .l
.d:     sep #$20
        .a8
        lda #0
        sta TextHeap,x
        rep #$20
        .a16
        lda #1
        sta PTag
        lda NTId
        jmp Push
ExList: lda [TalkPtr],y
        and #$00FF
        iny
        ldx #2
        stx PTag
        jmp Push
ExGrid: lda [TalkPtr],y
        and #$00FF
        iny
        ldx #3
        stx PTag
        jmp Push
; array[i]
ExIndex1:
        jsr Num1
        sta IxI
        jsr Pop
        sta IxL
        jsr ListCell
        bcc .z
        lda CellT,x
        sta PTag
        lda CellV,x
        jmp Push
.z:     lda #0
        jmp Push
; grid[x, y]
ExIndex2:
        jsr Num1
        sta IxY
        jsr Num1
        sta IxX
        jsr Pop
        sta IxL
        jsr TxGridCell
        bcc .z
        lda CellT,x
        sta PTag
        lda CellV,x
        jmp Push
.z:     lda #0
        jmp Push

; X = array IxL's element IxI (x 2); carry clear if outside it.
ListCell:
        lda IxL
        cmp #16
        bcs .no
        asl a
        tax
        lda IxI
        cmp ListN,x
        bcs .no
        lda IxL
        xba
        lsr a                   ; x 128 (64 cells of 2)
        sta T5
        lda IxI
        asl a
        clc
        adc T5
        tax
        sec
        rts
.no:    clc
        rts
; X = grid IxL's cell IxX, IxY (x 2); carry clear if outside it.
TxGridCell:
        lda IxL
        cmp #8
        bcs .no
        asl a
        tax
        lda IxX
        cmp GCols,x
        bcs .no
        lda IxY
        cmp GRows,x
        bcs .no
        lda GCols,x
        sta MulA
        lda IxY
        sta MB0
        phx
        phy
        jsr Mul16
        ply
        plx
        lda MulR
        clc
        adc IxX
        clc
        adc GBase,x
        asl a
        tax
        sec
        rts
.no:    clc
        rts

; ---- functions: op 34 which, how many ----
ExFn:   lda [TalkPtr],y
        and #$00FF
        sta FnNo
        iny
        lda [TalkPtr],y
        and #$00FF
        sta FnN
        iny
        sty FnY
        lda FnN                 ; the values, last first
        asl a
        tax
.a:     cpx #0
        beq .go
        dex
        dex
        phx
        jsr Pop
        plx
        sta ArgV,x
        lda QTag
        sta ArgT,x
        bra .a
.go:    lda FnNo
        cmp #22
        bcs .z
        asl a
        tax
        jsr (FnJump,x)
        ldy FnY
        jmp Push
.z:     ldy FnY
        lda #0
        jmp Push

FnJump:   .dw FnZero,FnLength,FnRows,FnCols,FnContains,FnIndexOf,FnUpper,FnLower,FnTrim,FnPart,FnLeft,FnRight
          .dw FnReplace,FnJoin,FnText,FnNumber,FnSum,FnAverage,FnSqrt,FnPow,FnMinL,FnMaxL
FnZero: lda #0
        rts
; (A, its kind) = argument X (0, 2, 4)
Arg:    lda ArgT,x
        sta PTag
        lda ArgV,x
        rts
ArgNum: lda ArgT,x
        pha
        lda ArgV,x
        plx
        jmp ValNum
ArgText: lda ArgT,x
        pha
        lda ArgV,x
        plx
        jmp ValText

FnLength:
        ldx #0
        jsr Arg
        ldx PTag
        cpx #2
        bne .g
        asl a
        tax
        lda ListN,x
        bra .n
.g:     cpx #3
        bne .t
        asl a
        tax
        lda GCols,x
        sta MulA
        lda GRows,x
        sta MB0
        jsr Mul16
        lda MulR
        bra .n
.t:     ldx #0
        jsr ArgText
        jsr TxtLen
.n:     stz PTag
        rts
FnRows: ldx #0
        jsr Arg
        ldx #GRows-GCols
        bra FnGr
FnCols: ldx #0
        jsr Arg
        ldx #0
FnGr:   stx T5
        ldx PTag
        cpx #3
        bne .z
        asl a
        clc
        adc T5
        tax
        lda GCols,x
        stz PTag
        rts
.z:     stz PTag
        lda #0
        rts
FnContains:
        jsr FindArg
        cmp #$8000
        lda #0
        bcs .n
        inc a
.n:     stz PTag
        rts
FnIndexOf:
        jsr FindArg
        stz PTag
        rts
; A = where argument 1 is in argument 0 (an array: its index; a text: the letter's place; -1 none).
FindArg:
        ldx #0
        jsr Arg
        ldx PTag
        cpx #2
        bne .t
        sta IxL                 ; an array: the first element equal
        ldx #2
        jsr Arg
        sta FV
        lda PTag
        sta FT
        jmp ListFind
.t:     ldx #0                  ; a text: where the other starts (without case)
        jsr ArgText
        sta FA
        ldx #2
        jsr ArgText
        sta FB
        jmp TxtFind
; A = the place of slot FB in slot FA, from letter FFrom (-1: not there; without case).
TxtFind:
        stz FFrom
TxtFindFrom:
        lda FB
        jsr TxtLen
        sta FBL
        lda FA
        jsr TxtLen
        sta FAL
        lda FFrom
        sta FI
.l:     lda FI
        clc
        adc FBL
        cmp FAL
        beq .try
        bcs .none
.try:   lda FA
        jsr TxtAddr
        txa
        clc
        adc FI
        sta FPa
        lda FB
        jsr TxtAddr
        stx FPb
        ldy #0
.c:     cpy FBL
        bcs .yes
        ldx FPa
        phy
        tya
        clc
        adc FPa
        tax
        lda TextHeap,x
        and #$00FF
        jsr Upper
        sta CmpX
        ply
        phy
        tya
        clc
        adc FPb
        tax
        lda TextHeap,x
        and #$00FF
        jsr Upper
        ply
        cmp CmpX
        bne .n
        iny
        bra .c
.n:     inc FI
        bra .l
.yes:   lda FI
        rts
.none:  lda #$FFFF
        rts
; A = the index of the first element of array IxL equal to FV (FT) (-1 none).
ListFind:
        stz IxI
.l:     jsr ListCell
        bcc .none
        lda CellV,x
        sta ExA
        lda CellT,x
        sta ATag
        lda FV
        sta ExB
        lda FT
        sta BTag
        jsr CmpAB
        beq .got
        inc IxI
        bra .l
.got:   lda IxI
        rts
.none:  lda #$FFFF
        rts

; upper / lower / trim: a copy, changed.
FnUpper: jsr CopyArg0
.l:     lda TextHeap,x
        and #$00FF
        beq FnTextOut
        jsr Upper
        sep #$20
        .a8
        sta TextHeap,x
        rep #$20
        .a16
        inx
        bra .l
FnLower: jsr CopyArg0
.l:     lda TextHeap,x
        and #$00FF
        beq FnTextOut
        cmp #'A'
        bcc .n
        cmp #'Z'+1
        bcs .n
        ora #$20
        sep #$20
        .a8
        sta TextHeap,x
        rep #$20
        .a16
.n:     inx
        bra .l
FnTextOut:
        lda CDst
        ldx #1
        stx PTag
        rts
; CDst = a slot for the moment holding argument 0 as a text; X its place.
CopyArg0:
        ldx #0
        jsr ArgText
        sta CSrc
        jsr NewTemp
        sta CDst
        jsr CopyTxt
        lda CDst
        jmp TxtAddr
FnTrim: ldx #0                  ; the spaces off both ends
        jsr ArgText
        sta FA
        jsr TxtLen
        sta FAL
        lda FA
        jsr TxtAddr
        stx FPa
        stz FI                  ; first not a space
.f:     lda FI
        cmp FAL
        bcs .sub
        clc
        adc FPa
        tax
        lda TextHeap,x
        and #$00FF
        cmp #' '
        bne .e
        inc FI
        bra .f
.e:     lda FAL                 ; last not a space
.b:     cmp FI
        beq .sub
        dec a
        clc
        adc FPa
        tax
        lda TextHeap,x
        and #$00FF
        cmp #' '
        bne .end
        dec FAL
        lda FAL
        bra .b
.end:
.sub:   lda FAL
        sec
        sbc FI
        bpl .k
        lda #0
.k:     sta FN2
        lda FI
        sta FFrom
        jmp SubText
; A = a slot for the moment: FN2 letters of slot FA from letter FFrom.
SubText:
        jsr NewTemp
        sta CDst
        jsr TxtAddr
        txy
        lda FA
        jsr TxtAddr
        txa
        clc
        adc FFrom
        tax
.l:     lda FN2
        beq .d
        lda TextHeap,x
        and #$00FF
        beq .d
        sep #$20
        .a8
        sta TextHeap,y
        rep #$20
        .a16
        inx
        iny
        dec FN2
        bra .l
.d:     sep #$20
        .a8
        lda #0
        sta TextHeap,y
        rep #$20
        .a16
        jmp FnTextOut
; part(s, start, count)
FnPart: jsr TextAndLen
        ldx #2
        jsr ArgNum
        jsr ClampLen
        sta FFrom
        lda FAL
        sec
        sbc FFrom
        sta FN2
        lda FnN
        cmp #3
        bcc .all
        ldx #4
        jsr ArgNum
        cmp #$8000
        bcc .c
        lda #0
.c:     cmp FN2
        bcs .all
        sta FN2
.all:   jmp SubText
; left(s, n)
FnLeft: jsr TextAndLen
        ldx #2
        jsr ArgNum
        jsr ClampLen
        sta FN2
        stz FFrom
        jmp SubText
; right(s, n)
FnRight: jsr TextAndLen
        ldx #2
        jsr ArgNum
        jsr ClampLen
        sta FN2
        lda FAL
        sec
        sbc FN2
        sta FFrom
        jmp SubText
TextAndLen:
        ldx #0
        jsr ArgText
        sta FA
        jsr TxtLen
        sta FAL
        rts
; A between 0 and FAL.
ClampLen:
        cmp #$8000
        bcc .p
        lda #0
.p:     cmp FAL
        bcc .o
        lda FAL
.o:     rts
; replace(s, a, b): every a (without case) made b.
FnReplace:
        ldx #0
        jsr ArgText
        sta FA
        ldx #2
        jsr ArgText
        sta FB
        ldx #4
        jsr ArgText
        sta TxFR
        lda FB
        jsr TxtLen
        bne .go
        lda FA                  ; nothing to look for: as it is
        ldx #1
        stx PTag
        rts
.go:    jsr NewTemp
        sta RDst
        stz FFrom
.l:     jsr TxtFindFrom         ; the next one
        sta RHit
        cmp #$FFFF
        beq .rest
        lda RHit                ; the part before it, then b
        sec
        sbc FFrom
        sta FN2
        jsr AppendPart
        lda TxFR
        sta CSrc
        lda RDst
        sta CDst
        jsr AppendTxt
        lda RHit
        clc
        adc FBL
        sta FFrom
        bra .l
.rest:  lda #SLOT
        sta FN2
        jsr AppendPart
        lda RDst
        ldx #1
        stx PTag
        rts
; RDst += FN2 letters of FA from FFrom.
AppendPart:
        lda FFrom
        pha
        lda FB
        pha
        lda FBL
        pha
        jsr SubText             ; (a slot for the moment)
        sta CSrc
        lda RDst
        sta CDst
        jsr AppendTxt
        pla
        sta FBL
        pla
        sta FB
        pla
        sta FFrom
        rts
; join(array, between): the elements as texts, ", " between (or the second value).
FnJoin: ldx #0
        jsr Arg
        ldx PTag
        cpx #2
        beq .l
        ldx #0
        jsr ArgText
        ldx #1
        stx PTag
        rts
.l:     sta IxL
        lda FnN
        cmp #2
        bcc .def
        ldx #2
        jsr ArgText
        bra .sep
.def:   lda #Comma2
        jsr MineToTemp
.sep:   sta JSep
        jsr NewTemp
        sta JDst
        stz IxI
.e:     jsr ListCell
        bcc .d
        lda IxI
        beq .first
        lda JSep
        sta CSrc
        lda JDst
        sta CDst
        jsr AppendTxt
.first: jsr ListCell
        lda CellT,x
        pha
        lda CellV,x
        plx
        jsr ValText
        sta CSrc
        lda JDst
        sta CDst
        jsr AppendTxt
        inc IxI
        bra .e
.d:     lda JDst
        ldx #1
        stx PTag
        rts
; A = a slot for the moment with my C string A.
MineToTemp:
        sta T5
        jsr NewTemp
        sta NTId
        jsr TxtAddr
        ldy T5
        sep #$20
        .a8
.l:     lda 0,y
        sta TextHeap,x
        beq .d
        inx
        iny
        bra .l
.d:     rep #$20
        .a16
        lda NTId
        rts
FnText: ldx #0
        jsr ArgText
        ldx #1
        stx PTag
        rts
FnNumber:
        ldx #0
        jsr ArgNum
        stz PTag
        rts
; sum / average: of an array, else of the values
FnSum:  jsr SumArgs
        lda SumV
        stz PTag
        rts
FnAverage:
        jsr SumArgs
        lda TxSumN
        beq .z
        sta ExB
        lda SumV
        jsr SDiv
        lda ExQ
        stz PTag
        rts
.z:     lda #0
        stz PTag
        rts
SumArgs:
        stz SumV
        stz TxSumN
        ldx #0
        jsr Arg
        ldx PTag
        cpx #2
        bne .vals
        sta IxL
        stz IxI
.e:     jsr ListCell
        bcc .o
        lda CellT,x
        pha
        lda CellV,x
        plx
        jsr ValNum
        clc
        adc SumV
        sta SumV
        inc TxSumN
        inc IxI
        bra .e
.vals:  ldx #0
.v:     txa
        lsr a
        cmp FnN
        bcs .o
        phx
        jsr ArgNum
        clc
        adc SumV
        sta SumV
        inc TxSumN
        plx
        inx
        inx
        bra .v
.o:     rts
; min / max of an array
FnMinL: lda #0
        bra MinMax
FnMaxL: lda #1
MinMax: sta MMx
        ldx #0
        jsr Arg
        sta IxL
        stz IxI
        stz MMv
        stz MMany
.e:     jsr ListCell
        bcc .o
        lda CellT,x
        pha
        lda CellV,x
        plx
        jsr ValNum
        ldx MMany
        beq .take
        sta ExA
        lda MMv
        sta ExB
        lda ExA
        jsr SCmp
        php
        lda MMx
        bne .mx
        plp
        bpl .n
        bra .tk
.mx:    plp
        bmi .n
        beq .n
.tk:    lda ExA
.take:  sta MMv
        lda #1
        sta MMany
.n:     inc IxI
        bra .e
.o:     lda MMv
        stz PTag
        rts
; sqrt (whole), pow
FnSqrt: ldx #0
        jsr ArgNum
        cmp #$8000
        bcc .p
        lda #0
.p:     sta FA
        ldx #0
.l:     inx                     ; the largest x with x x x <= n
        cpx #182
        bcs .d
        stx MulA
        stx MB0
        jsr Mul16
        lda MulR+2
        bne .d
        lda MulR
        cmp FA
        beq .l
        bcc .l
.d:     dex
        txa
        stz PTag
        rts
FnPow:  ldx #2
        jsr ArgNum
        sta FN2
        ldx #0
        jsr ArgNum
        sta FA
        lda #1
        sta TxFR
.l:     lda FN2
        beq .d
        bmi .z
        lda TxFR
        sta ExB
        lda FA
        jsr SMul
        sta TxFR
        dec FN2
        bra .l
.z:     lda #0
        sta TxFR
.d:     lda TxFR
        stz PTag
        rts

; ---- the game's: op 35 which, of whom ----
ExGame: lda [TalkPtr],y
        and #$00FF
        sta FnNo
        iny
        lda [TalkPtr],y
        sta JMap
        iny
        iny
        lda [TalkPtr],y
        sta JIdx
        iny
        iny
        stz GExtra
        lda FnNo
        cmp #15
        bne .n15
        lda [TalkPtr],y
        and #$00FF
        sta GExtra
        iny
.n15:   cmp #16
        bne .n16
        lda [TalkPtr],y
        sta GExtra
        iny
        iny
.n16:   sty FnY
        jsr WhoRec              ; WP = whom
        bcc .got
        lda #0
        ldy FnY
        jmp Push
.got:   stz PTag                ; (a number, unless it says a text)
        lda FnNo
        cmp #20
        bcs .z
        asl a
        tax
        jsr (GameJump,x)
        ldy FnY
        jmp Push
.z:     ldy FnY
        lda #0
        jmp Push

GameJump: .dw FnZero,GHp,GMaxHp,TxGX,TxGY,GGold,GFood,GHour,GMinute,GDay,GParty,GDead,GInParty,TxGName,GFacing,GStat,GItems,GFloor,GCombat,GMapName
GHp:    ldy #C_HP
        lda [WP],y
        rts
GMaxHp: ldy #C_MAXHP
        lda [WP],y
        rts
TxGX:     jsr WPIsAvatar
        bcc .c
        lda PX
        rts
.c:     lda [WP]
        rts
TxGY:     jsr WPIsAvatar
        bcc .c
        lda PY
        rts
.c:     ldy #2
        lda [WP],y
        rts
GGold:  jsr WPIsAvatar
        bcc .z
        lda AvRec+GOLD
        rts
.z:     lda #0
        rts
GFood:  ldy #FOOD
        lda [WP],y
        cmp #$FFFF
        bne .o
        lda #0
.o:     rts
GHour:  lda Hour
        rts
GMinute: lda Minute
        rts
GDay:   lda Day
        rts
GParty: lda PartyN
        inc a
        rts
GDead:  jsr WPIsAvatar
        bcc .c
        lda AvDead
        bra .b
.c:     ldy #C_FLAGS
        lda [WP],y
        and #4
.b:     beq .o
        lda #1
.o:     rts
GInParty:
        jsr WPInParty
        lda #0
        rol a
        rts
TxGName:  jsr WPName              ; a text: their name
        lda WhoName
        sta GFar
        lda WhoName+2
        sta GFar+2
        jmp FarToTemp
GFacing: jsr WPIsAvatar
        bcc .c
        lda Facing
        rts
.c:     ldy #8
        lda [WP],y
        rts
GStat:  lda GExtra
        asl a
        clc
        adc #16
        tay
        lda [WP],y
        rts
GItems: lda GExtra
        jmp CountItem
GFloor: lda #1
        rts
GCombat: lda CombatOn
        rts
GMapName:
        lda CurMap
        cmp MapNameN
        bcs .z
        asl a
        tax
        lda MapNameTab,x
        jsr NameNumber
        lda FmtName
        sta GFar
        lda FmtName+2
        sta GFar+2
        jmp FarToTemp
.z:     lda #0
        ldx #1
        stx PTag
        rts
; A = a slot for the moment holding the C string at GFar (a text).
FarToTemp:
        jsr NewTemp
        sta NTId
        jsr TxtAddr
        lda Src
        pha
        lda Src+2
        pha
        lda GFar
        sta Src
        lda GFar+2
        sta Src+2
        ldy #0
        sep #$20
        .a8
.l:     lda [Src],y
        sta TextHeap,x
        beq .d
        cpy #SLOT-2
        bcs .end
        inx
        iny
        bra .l
.end:   lda #0
        sta TextHeap+1,x
.d:     rep #$20
        .a16
        pla
        sta Src+2
        pla
        sta Src
        lda #1
        sta PTag
        lda NTId
        rts

; ---- talk ops ----
; INPUT kind variable longest words: the player types it (Enter: done; Delete: a letter back; Escape: nothing).
OpInput:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta InKind
        iny
        lda [TalkPtr],y
        asl a
        sta InVar
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta InMax
        iny
        sty InY
        lda TalkPtr
        sta SayP
        lda TalkPtr+2
        sta SayP+2
        lda [TalkPtr],y         ; the words above (if any)
        and #$00FF
        beq .nw
        jsr SayText
        jsr NewLine
.nw:    ldy InY
.sk:    lda [TalkPtr],y
        iny
        and #$00FF
        bne .sk
        sty TPC
        sty InPC                ; (where the talk goes on: kept while waiting)
        stz TxInLen
        sep #$20
        .a8
        lda #0
        sta InText
        rep #$20
        .a16
        lda #Prompt
        jsr PrintMine
        lda #17
        sta Mode
        rts

InputKey:
        cmp #$0D
        bne .bf3
        brl .done
.bf3:
        cmp #$1B
        beq .none
        cmp #$7F
        beq .del
        cmp #8
        beq .del
        cmp #' '
        bcc .o
        cmp #$7F
        bcs .o
        ldx InKind              ; a number: figures (a minus first)
        beq .ok
        cmp #'-'
        bne .fig
        ldx TxInLen
        bne .o
        bra .ok
.fig:   cmp #'0'
        bcc .o
        cmp #'9'+1
        bcs .o
.ok:    ldx TxInLen
        cpx InMax
        bcs .o
        sep #$20
        .a8
        sta InText,x
        sta InCh
        lda #0
        sta InText+1,x
        sta InCh+1
        rep #$20
        .a16
        inc TxInLen
        lda #InCh
        jmp PrintMine
.del:   lda TxInLen
        beq .o
        dec TxInLen
        ldx TxInLen
        sep #$20
        .a8
        lda #0
        sta InText,x
        rep #$20
        .a16
        lda PenX                ; the letter off the screen: back over it (the text area shows the line again at Enter)
        sec
        sbc #6
        sta PenX
.o:     rts
.none:  stz TxInLen
        sep #$20
        .a8
        lda #0
        sta InText
        rep #$20
        .a16
.done:  jsr NewLine
        lda #InText             ; into a slot for the moment
        jsr MineToTemp
        sta SVal
        ldx InKind
        beq .txt
        jsr ParseTxt            ; a number (nothing typed: 0)
        bcs .n
        lda #0
.n:     ldx InVar
        jsr SetVarNum
        bra .back
.txt:   lda #1
        sta PTag
        lda SVal
        ldx InVar
        jsr StoreVar
.back:  lda #3
        sta Mode
        lda InPC
        sta TPC
        jmp RunTalk

; LIST what array ...
OpList: iny
        lda [TalkPtr],y
        and #$00FF
        sta LWhat
        iny
        lda [TalkPtr],y
        and #$00FF
        sta IxL
        iny
        lda LWhat
        cmp #14
        bcs .d
        asl a
        tax
        jsr (ListJump,x)
.d:     sty TPC
        jmp RunTalk

ListJump: .dw LCreate,LSet,LGet,LAdd,LInsert,LRemoveAt,LRemoveEq,LLength,LFind,LReorder,LFill,LClear,LCopy,LStat
; (each: Y at its values, left past them)
LCreate: jsr EvalExpr           ; how many
        jsr NumPos
        cmp #LISTMAX+1
        bcc .c
        lda #LISTMAX
.c:     sta LN2
        jsr EvalExpr            ; each starts as
        sta FV
        lda PTag
        sta FT
        sty TxLY
        jsr ListFreeAll
        lda IxL
        asl a
        tax
        lda LN2
        sta ListN,x
        jsr FillList
        ldy TxLY
        rts
LSet:   jsr EvalExpr
        sta IxI
        jsr EvalExpr
        sty TxLY
        pha
        jsr ListCell
        pla
        bcc .o
        jsr StoreCell
.o:     ldy TxLY
        rts
LGet:   jsr EvalExpr
        sta IxI
        lda [TalkPtr],y
        asl a
        sta LVar
        iny
        iny
        sty TxLY
        jsr ListCell
        bcs .g
        stz PTag
        lda #0
        bra .s
.g:     lda CellT,x
        sta PTag
        lda CellV,x
.s:     ldx LVar
        jsr StoreVar
        ldy TxLY
        rts
LAdd:   jsr EvalExpr
        sty TxLY
        pha
        lda IxL
        asl a
        tax
        lda ListN,x
        cmp #LISTMAX
        bcs .full
        sta IxI
        inc a
        sta ListN,x
        jsr ListCell
        pla
        jsr StoreCellNew
        ldy TxLY
        rts
.full:  pla
        ldy TxLY
        rts
LInsert: jsr EvalExpr
        jsr NumPos
        sta LIns
        jsr EvalExpr
        sty TxLY
        pha
        lda PTag
        pha
        lda IxL
        asl a
        tax
        lda ListN,x
        cmp #LISTMAX
        bcs .full
        cmp LIns
        bcs .ok
        sta LIns                ; past the end: at the end
.ok:    sta IxI                 ; each from the end, one on
        inc a
        sta ListN,x
.mv:    lda IxI
        cmp LIns
        beq .put
        beq .put
        dec IxI
        jsr ListCell
        lda CellV,x
        sta CellV+2,x
        lda CellT,x
        sta CellT+2,x
        bra .mv
.put:   jsr ListCell
        pla
        sta PTag
        pla
        jsr StoreCellNew
        ldy TxLY
        rts
.full:  pla
        pla
        ldy TxLY
        rts
LRemoveAt:
        jsr EvalExpr
        sta IxI
        sty TxLY
        jsr RemoveAtI
        ldy TxLY
        rts
LRemoveEq:
        jsr EvalExpr
        sta FV
        lda PTag
        sta FT
        sty TxLY
        jsr ListFind
        cmp #$FFFF
        beq .o
        sta IxI
        jsr RemoveAtI
.o:     ldy TxLY
        rts
; element IxI out, the rest one back.
RemoveAtI:
        jsr ListCell
        bcc .o
        stx SVx
        jsr FreeCellText
        lda IxL
        asl a
        tax
        lda ListN,x
        dec a
        sta ListN,x
        sta LN2
.l:     lda IxI
        cmp LN2
        bcs .last
        jsr ListCellRaw
        lda CellV+2,x
        sta CellV,x
        lda CellT+2,x
        sta CellT,x
        inc IxI
        bra .l
.last:  lda LN2                 ; the old last place: empty (its text is the one before it now)
        sta IxI
        jsr ListCellRaw
        stz CellV,x
        stz CellT,x
.o:     rts
LLength: lda IxL
        asl a
        tax
        lda ListN,x
        jmp StoreNumVarY
LFind:  jsr EvalExpr
        sta FV
        lda PTag
        sta FT
        sty TxLY
        jsr ListFind
        ldy TxLY
        jmp StoreNumVarY
LReorder:
        lda [TalkPtr],y
        and #$00FF
        sta LOrder
        iny
        sty TxLY
        lda IxL
        asl a
        tax
        lda ListN,x
        sta LN2
        lda LOrder
        cmp #2
        beq .shuffle
        cmp #3
        beq .rev
        jsr SortList            ; small to big, or big to small
        ldy TxLY
        rts
.shuffle: lda LN2               ; each from the end: with one before it
.sl:    cmp #2
        bcc .d
        dec a
        sta IxI
        inc a
        jsr Random
        sta IxJ
        jsr SwapIJ
        lda IxI
        bra .sl
.rev:   stz IxI
        lda LN2
        dec a
        sta IxJ
.rl:    lda IxI
        cmp IxJ
        bcs .d
        bmi .d
        jsr SwapIJ
        inc IxI
        dec IxJ
        bra .rl
.d:     ldy TxLY
        rts
; elements IxI and IxJ change places
SwapIJ:
        lda IxI
        pha
        jsr ListCellRaw
        stx SwX
        lda IxJ
        sta IxI
        jsr ListCellRaw
        ldy SwX
        lda CellV,x
        pha
        lda CellV,y
        sta CellV,x
        pla
        sta CellV,y
        lda CellT,x
        pha
        lda CellT,y
        sta CellT,x
        pla
        sta CellT,y
        pla
        sta IxI
        rts
; X = element IxI's place (x 2), whatever the length.
ListCellRaw:
        lda IxL
        xba
        lsr a
        sta T5
        lda IxI
        asl a
        clc
        adc T5
        tax
        rts
; sorted: small to big (LOrder 0) or big to small (1) - as Windows compares
SortList:
        lda LN2
        cmp #2
        bcc .o
.pass:  stz LSw
        stz IxI
.l:     lda IxI
        inc a
        cmp LN2
        bcs .end
        sta IxJ
        jsr ListCellRaw
        lda CellV,x
        sta ExA
        lda CellT,x
        sta ATag
        lda CellV+2,x
        sta ExB
        lda CellT+2,x
        sta BTag
        jsr CmpAB
        ldx LOrder
        bne .big
        cmp #1                  ; small to big: out of order if the first is bigger
        bne .n
        bra .sw
.big:   cmp #$FFFF
        bne .n
.sw:    jsr SwapIJ
        inc LSw
.n:     inc IxI
        bra .l
.end:   lda LSw
        bne .pass
.o:     rts
LFill:  jsr EvalExpr
        sta FV
        lda PTag
        sta FT
        sty TxLY
        jsr FillList
        ldy TxLY
        rts
; every element of array IxL = FV (FT)
FillList:
        stz IxI
.l:     jsr ListCell
        bcc .o
        lda FT
        sta PTag
        lda FV
        jsr StoreCell
        inc IxI
        bra .l
.o:     rts
LClear: sty TxLY
        jsr ListFreeAll
        lda IxL
        asl a
        tax
        stz ListN,x
        ldy TxLY
        rts
; the texts of array IxL's elements given back; all made 0
ListFreeAll:
        lda IxL
        asl a
        tax
        lda #LISTMAX
        sta ListN,x
        stz IxI
.l:     jsr ListCell
        bcc .o
        stx SVx
        jsr FreeCellText
        ldx SVx
        stz CellV,x
        stz CellT,x
        inc IxI
        bra .l
.o:     lda IxL
        asl a
        tax
        stz ListN,x
        rts
LCopy:  lda [TalkPtr],y         ; from
        and #$00FF
        sta LFrom
        iny
        sty TxLY
        jsr ListFreeAll
        lda LFrom
        asl a
        tax
        lda ListN,x
        sta LN2
        lda IxL
        asl a
        tax
        lda LN2
        sta ListN,x
        stz IxI
.l:     lda IxI
        cmp LN2
        bcs .o
        lda IxL                 ; the element from the other
        pha
        lda LFrom
        sta IxL
        jsr ListCellRaw
        pla
        sta IxL
        lda CellT,x
        sta PTag
        lda CellV,x
        pha
        jsr ListCellRaw
        pla
        jsr StoreCellNew
        inc IxI
        bra .l
.o:     ldy TxLY
        rts
LStat:  lda [TalkPtr],y         ; sum, smallest, biggest, average
        and #$00FF
        sta LOrder
        iny
        lda IxL
        sta ArgV
        lda #2
        sta ArgT
        lda #1
        sta FnN
        phy
        lda LOrder
        bne .s1
        jsr FnSum
        bra .put
.s1:    cmp #1
        bne .s2
        jsr FnMinL
        bra .put
.s2:    cmp #2
        bne .s3
        jsr FnMaxL
        bra .put
.s3:    jsr FnAverage
.put:   ply
        jmp StoreNumVarY
; cell X (empty: no text of its own yet) = A (PTag)
StoreCellNew:
        pha
        stz CellT,x
        stz CellV,x
        pla
        jmp StoreCell
; A at least 0
NumPos: ldx PTag
        jsr ValNum
        cmp #$8000
        bcc .o
        lda #0
.o:     rts
; the variable at Y (u16) = the number A; Y past it
StoreNumVarY:
        pha
        lda [TalkPtr],y
        asl a
        tax
        iny
        iny
        pla
        jmp SetVarNum

; GRID what grid ...
OpGrid: iny
        lda [TalkPtr],y
        and #$00FF
        sta LWhat
        iny
        lda [TalkPtr],y
        and #$00FF
        sta IxL
        iny
        lda LWhat
        cmp #11
        bcs .d
        asl a
        tax
        jsr (GridJump,x)
.d:     sty TPC
        jmp RunTalk

GridJump: .dw GCreate,GSet,GGet,GFill,GSize,GResize,GFind,GLineToArr,GArrToLine,GFromMap,GToMap
GCreate: jsr EvalExpr
        jsr NumPos
        sta GNC
        jsr EvalExpr
        jsr NumPos
        sta GNR
        jsr EvalExpr
        sta FV
        lda PTag
        sta FT
        sty TxLY
        jsr GridFreeAll
        jsr GridMake            ; a new block of GNC x GNR
        bcc .o
        jsr GridFillAll
.o:     ldy TxLY
        rts
GSet:   jsr EvalExpr
        sta IxX
        jsr EvalExpr
        sta IxY
        jsr EvalExpr
        sty TxLY
        pha
        jsr TxGridCell
        pla
        bcc .o
        jsr StoreCell
.o:     ldy TxLY
        rts
GGet:   jsr EvalExpr
        sta IxX
        jsr EvalExpr
        sta IxY
        lda [TalkPtr],y
        asl a
        sta LVar
        iny
        iny
        sty TxLY
        jsr TxGridCell
        bcs .g
        stz PTag
        lda #0
        bra .s
.g:     lda CellT,x
        sta PTag
        lda CellV,x
.s:     ldx LVar
        jsr StoreVar
        ldy TxLY
        rts
GFill:  jsr EvalExpr            ; the value; the whole grid or an area
        sta FV
        lda PTag
        sta FT
        lda [TalkPtr],y
        and #$00FF
        iny
        sta GArea
        stz GAX
        stz GAY
        lda #$7FFF
        sta GAW
        sta GAH
        lda GArea
        beq .all
        jsr EvalExpr
        sta GAX
        jsr EvalExpr
        sta GAY
        jsr EvalExpr
        sta GAW
        jsr EvalExpr
        sta GAH
.all:   sty TxLY
        lda GAY
        sta IxY
.r:     lda IxY
        sec
        sbc GAY
        cmp GAH
        bcs .o
        lda GAX
        sta IxX
.c:     lda IxX
        sec
        sbc GAX
        cmp GAW
        bcs .nr
        jsr TxGridCell
        bcc .nr
        lda FT
        sta PTag
        lda FV
        jsr StoreCell
        inc IxX
        bra .c
.nr:    inc IxY
        lda IxY
        cmp #200
        bcc .r
.o:     ldy TxLY
        rts
GSize:  lda IxL
        asl a
        tax
        lda GCols,x
        sta GNC
        lda GRows,x
        sta GNR
        lda [TalkPtr],y
        cmp #$FFFF
        beq .a
        asl a
        tax
        lda GNC
        jsr SetVarNum
.a:     iny
        iny
        lda [TalkPtr],y
        cmp #$FFFF
        beq .b
        asl a
        tax
        lda GNR
        jsr SetVarNum
.b:     iny
        iny
        rts
GResize: jsr EvalExpr           ; the cells kept where both have them, new ones the fill
        jsr NumPos
        sta GNC
        jsr EvalExpr
        jsr NumPos
        sta GNR
        jsr EvalExpr
        sta FV
        lda PTag
        sta FT
        sty TxLY
        lda IxL
        asl a
        tax
        lda GCols,x
        sta GOC
        lda GRows,x
        sta GOR
        lda GBase,x
        sta GOB
        lda GNC                 ; no room after the pool's end: packed first (with this one)
        sta MulA
        lda GNR
        sta MB0
        jsr Mul16
        lda MulR
        clc
        adc GTop
        cmp #CELLS+1
        bcc .room
        jsr GridPack
        lda IxL
        asl a
        tax
        lda GBase,x
        sta GOB
.room:  lda IxL
        asl a
        tax
        stz GCols,x             ; (out of the pool while a new block is found)
        stz GRows,x
        jsr GridMake
        bcs .moved
        lda IxL                 ; no room: as it was
        asl a
        tax
        lda GOC
        sta GCols,x
        lda GOR
        sta GRows,x
        lda GOB
        sta GBase,x
        ldy TxLY
        rts
.moved: lda IxL
        asl a
        tax
        lda GBase,x
        sta GNB
        stz IxY
.r:     lda IxY
        cmp GOR
        bcs .nf
        stz IxX
.c:     lda IxX
        cmp GOC
        bcs .nr
        jsr OldCell             ; X = the old cell
        lda IxX
        cmp GNC
        bcs .drop
        lda IxY
        cmp GNR
        bcs .drop
        phx
        jsr TxGridCell            ; Y = the new cell
        txy
        plx
        lda CellV,x
        sta CellV,y
        lda CellT,x
        sta CellT,y
        lda #$FFFF              ; (moved: kept, marked)
        sta CellT,x
        bra .n
.drop:  stx SVx
        jsr FreeCellText
.n:     inc IxX
        bra .c
.nr:    inc IxY
        bra .r
.nf:    stz IxY                 ; the new cells: the fill
.r2:    lda IxY
        cmp GNR
        bcs .o
        stz IxX
.c2:    lda IxX
        cmp GNC
        bcs .nr2
        lda IxX                 ; kept where both had a cell
        cmp GOC
        bcs .f
        lda IxY
        cmp GOR
        bcs .f
        bra .k
.f:     jsr TxGridCell
        stz CellT,x
        stz CellV,x
        lda FT
        sta PTag
        lda FV
        jsr StoreCell
.k:     inc IxX
        bra .c2
.nr2:   inc IxY
        bra .r2
.o:     ldy TxLY
        rts
; X = the old block's cell IxX, IxY (x 2)
OldCell:
        lda GOC
        sta MulA
        lda IxY
        sta MB0
        jsr Mul16
        lda MulR
        clc
        adc IxX
        clc
        adc GOB
        asl a
        tax
        rts
GFind:  jsr EvalExpr
        sta FV
        lda PTag
        sta FT
        sty TxLY
        stz IxY
        lda #$FFFF
        sta GFX
        sta GFY
.r:     lda IxL
        asl a
        tax
        lda IxY
        cmp GRows,x
        bcs .o
        stz IxX
.c:     jsr TxGridCell
        bcc .nr
        lda CellV,x
        sta ExA
        lda CellT,x
        sta ATag
        lda FV
        sta ExB
        lda FT
        sta BTag
        jsr CmpAB
        bne .n
        lda IxX
        sta GFX
        lda IxY
        sta GFY
        bra .o
.n:     inc IxX
        bra .c
.nr:    inc IxY
        bra .r
.o:     ldy TxLY
        lda [TalkPtr],y
        cmp #$FFFF
        beq .a
        asl a
        tax
        lda GFX
        jsr SetVarNum
.a:     iny
        iny
        lda [TalkPtr],y
        cmp #$FFFF
        beq .b
        asl a
        tax
        lda GFY
        jsr SetVarNum
.b:     iny
        iny
        rts
GLineToArr:                     ; a row (or column) into an array
        lda [TalkPtr],y
        and #$00FF
        sta GLine
        iny
        jsr EvalExpr
        sta GLIdx
        lda [TalkPtr],y
        and #$00FF
        sta LFrom               ; (the array)
        iny
        sty TxLY
        lda IxL
        sta GG
        lda LFrom
        sta IxL
        jsr ListFreeAll
        lda GG
        sta IxL
        stz GK
.l:     jsr LineCell            ; X = the grid's cell GK of the line
        bcc .d
        lda CellT,x
        sta PTag
        lda CellV,x
        pha
        lda PTag
        pha
        lda LFrom
        sta IxL
        asl a
        tax
        lda GK
        cmp #LISTMAX
        bcs .full
        sta IxI
        inc a
        sta ListN,x
        jsr ListCell
        pla
        sta PTag
        pla
        jsr StoreCellNew
        lda GG
        sta IxL
        inc GK
        bra .l
.full:  pla
        pla
        lda GG
        sta IxL
.d:     ldy TxLY
        rts
GArrToLine:                     ; an array into a row (or column)
        lda [TalkPtr],y
        and #$00FF
        sta GLine
        iny
        jsr EvalExpr
        sta GLIdx
        lda [TalkPtr],y
        and #$00FF
        sta LFrom
        iny
        sty TxLY
        lda IxL
        sta GG
        stz GK
.l:     lda LFrom
        sta IxL
        lda GK
        sta IxI
        jsr ListCell
        bcc .d
        lda CellT,x
        sta PTag
        lda CellV,x
        pha
        lda PTag
        pha
        lda GG
        sta IxL
        jsr LineCell
        bcc .end
        pla
        sta PTag
        pla
        jsr StoreCell
        inc GK
        bra .l
.end:   pla
        pla
.d:     lda GG
        sta IxL
        ldy TxLY
        rts
; X = cell GK along line GLIdx (GLine 0: a row - x = GK; 1: a column - y = GK)
LineCell:
        lda GLine
        bne .col
        lda GK
        sta IxX
        lda GLIdx
        sta IxY
        jmp TxGridCell
.col:   lda GLIdx
        sta IxX
        lda GK
        sta IxY
        jmp TxGridCell
GFromMap:                       ; the map's tiles of an area into the grid (made that size)
        jsr EvalExpr
        sta GAX
        jsr EvalExpr
        sta GAY
        jsr EvalExpr
        jsr NumPos
        sta GNC
        jsr EvalExpr
        jsr NumPos
        sta GNR
        sty TxLY
        jsr GridFreeAll
        jsr GridMake
        bcc .o
        stz IxY
.r:     lda IxY
        cmp GNR
        bcs .o
        stz IxX
.c:     lda IxX
        cmp GNC
        bcs .nr
        lda GAX
        clc
        adc IxX
        tax
        lda GAY
        clc
        adc IxY
        tay
        jsr MapTile
        pha
        jsr TxGridCell
        pla
        sta CellV,x
        stz CellT,x
        inc IxX
        bra .c
.nr:    inc IxY
        bra .r
.o:     ldy TxLY
        rts
GToMap: jsr EvalExpr            ; the grid's numbers as the map's tiles there
        sta GAX
        jsr EvalExpr
        sta GAY
        sty TxLY
        stz IxY
.r:     lda IxL
        asl a
        tax
        lda IxY
        cmp GRows,x
        bcs .o
        stz IxX
.c:     jsr TxGridCell
        bcc .nr
        lda CellV,x
        and #$3FFF
        sta GV
        lda GAX
        clc
        adc IxX
        tax
        lda GAY
        clc
        adc IxY
        tay
        jsr MapTile             ; (Ptr: the cell)
        cmp #$FFFF
        beq .n
        lda [Ptr]
        and #$C000
        ora GV
        sta [Ptr]
.n:     inc IxX
        bra .c
.nr:    inc IxY
        bra .r
.o:     jsr DrawView
        ldy TxLY
        rts

; grid IxL's cells' texts given back; it has none
GridFreeAll:
        lda IxL
        asl a
        tax
        lda GCols,x
        sta MulA
        lda GRows,x
        sta MB0
        jsr Mul16
        lda IxL
        asl a
        tax
        lda GBase,x
        asl a
        sta T5
        lda MulR
        asl a
        clc
        adc T5
        sta GEnd
        ldx T5
.l:     cpx GEnd
        bcs .o
        stx SVx
        jsr FreeCellText
        ldx SVx
        inx
        inx
        bra .l
.o:     lda IxL
        asl a
        tax
        stz GCols,x
        stz GRows,x
        rts
; Grid IxL: a block of GNC x GNR cells from the pool (packed first if need be). Carry clear: no room (it has none).
GridMake:
        lda GNC
        sta MulA
        lda GNR
        sta MB0
        jsr Mul16
        lda MulR+2
        bne .no
        lda MulR
        sta GNeed
        clc
        adc GTop
        cmp #CELLS+1
        bcc .ok
        jsr GridPack            ; the others packed together at the start
        lda GTop
        clc
        adc GNeed
        cmp #CELLS+1
        bcs .no
.ok:    lda IxL
        asl a
        tax
        lda GTop
        sta GBase,x
        clc
        adc GNeed
        sta GTop
        lda GNC
        sta GCols,x
        lda GNR
        sta GRows,x
        lda GBase,x             ; its cells: 0
        asl a
        tax
        lda GNeed
        beq .d
.z:     stz CellV,x
        stz CellT,x
        inx
        inx
        dec a
        bne .z
.d:     sec
        rts
.no:    clc
        rts
; The grids' blocks moved down together (lowest first), GTop after the last.
GridPack:
        lda #GRIDCELL0
        sta GPNext
        stz GPDone
.again: lda #$FFFF              ; the lowest not yet moved
        sta GPBest
        ldx #0
.f:     cpx #16
        bcs .got
        lda GPDone
        and BitTab,x
        bne .nf
        lda GCols,x
        beq .nf
        lda GRows,x
        beq .nf
        lda GBase,x
        cmp GPBest
        bcs .nf
        sta GPBest
        stx GPWho
.nf:    inx
        inx
        bra .f
.got:   lda GPBest
        cmp #$FFFF
        beq .end
        ldx GPWho
        lda BitTab,x
        tsb GPDone
        lda GCols,x
        sta MulA
        lda GRows,x
        sta MB0
        jsr Mul16
        lda MulR
        sta GPN
        ldx GPWho
        lda GBase,x
        asl a
        tax
        lda GPNext
        asl a
        tay
        lda GPN
        beq .mv
.c:     lda CellV,x
        sta CellV,y
        lda CellT,x
        sta CellT,y
        inx
        inx
        iny
        iny
        dec GPN
        bne .c
.mv:    ldx GPWho
        lda GPNext
        sta GBase,x
        lda GCols,x
        sta MulA
        lda GRows,x
        sta MB0
        jsr Mul16
        lda GPNext
        clc
        adc MulR
        sta GPNext
        brl .again
.end:   lda GPNext
        sta GTop
        rts
; every cell of grid IxL = FV (FT)
GridFillAll:
        stz IxY
.r:     lda IxY
        cmp GNR
        bcs .o
        stz IxX
.c:     lda IxX
        cmp GNC
        bcs .nr
        jsr TxGridCell
        lda FT
        sta PTag
        lda FV
        jsr StoreCell
        inc IxX
        bra .c
.nr:    inc IxY
        bra .r
.o:     rts

Comma2:   .cstr ", "
TA:       .dw 0
TempNext: .dw 0
CSrc:     .dw 0
CDst:     .dw 0
CA:       .dw 0
CN:       .dw 0
NTId:     .dw 0
PNeg:     .dw 0
PVal:     .dw 0
PDig:     .dw 0
PT:       .dw 0
JB:       .dw 0
CmpX:     .dw 0
SVal:     .dw 0
SVx:      .dw 0
IxI:      .dw 0
IxJ:      .dw 0
IxL:      .dw 0
IxX:      .dw 0
IxY:      .dw 0
FnNo:     .dw 0
FnN:      .dw 0
FnY:      .dw 0
ArgV:     .ds 6
ArgT:     .ds 6
FV:       .dw 0
FT:       .dw 0
FA:       .dw 0
FB:       .dw 0
TxFR:       .dw 0
FAL:      .dw 0
FBL:      .dw 0
FI:       .dw 0
FN2:      .dw 0
FFrom:    .dw 0
FPa:      .dw 0
FPb:      .dw 0
RDst:     .dw 0
RHit:     .dw 0
JSep:     .dw 0
JDst:     .dw 0
SumV:     .dw 0
TxSumN:     .dw 0
MMx:      .dw 0
MMv:      .dw 0
MMany:    .dw 0
GExtra:   .dw 0
GFar:     .dd 0
InKind:   .dw 0
InPC:     .dw 0
InY:      .dw 0
InVar:    .dw 0
InMax:    .dw 0
TxInLen:    .dw 0
InText:   .ds 48
InCh:     .dw 0
LWhat:    .dw 0
TxLY:       .dw 0
LN2:      .dw 0
LVar:     .dw 0
LIns:     .dw 0
LOrder:   .dw 0
LSw:      .dw 0
LFrom:    .dw 0
GNC:      .dw 0
GNR:      .dw 0
GNB:      .dw 0
GOC:      .dw 0
GOR:      .dw 0
GOB:      .dw 0
GNeed:    .dw 0
GEnd:     .dw 0
GArea:    .dw 0
GAX:      .dw 0
GAY:      .dw 0
GAW:      .dw 0
GAH:      .dw 0
GFX:      .dw 0
GFY:      .dw 0
GLine:    .dw 0
GLIdx:    .dw 0
GG:       .dw 0
GK:       .dw 0
GV:       .dw 0
GPNext:   .dw 0
GPDone:   .dw 0
GPBest:   .dw 0
GPWho:    .dw 0
GPN:      .dw 0
SwX:      .dw 0
MapNameN: .dw 0
MapNameTab: .ds 32
; saved with the game
SCRIPTDATA:
VarTag:   .ds 512
TextHeap: .ds SLOT*SLOTS
FreeMap:  .ds SLOTS*2
ListN:    .ds 32
GCols:    .ds 16
GRows:    .ds 16
GBase:    .ds 16
GTop:     .dw 0
CellV:    .ds CELLS*2
CellT:    .ds CELLS*2
AvDown:   .dw 0                 ; (bodies.s: the Avatar knocked out - saved)
ChgN:     .dw 0                 ; (items.s: the charges left of wands and weapons)
ChgObj:   .ds 128
ChgLeft:  .ds 128
EggSt:    .ds 128               ; (eggs.s: each egg's state and weather minutes left - saved)
EggWM:    .ds 128
SCRIPTEND: .dw 0
