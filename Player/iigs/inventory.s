; Sandcastle RPG Maker - Apple IIgs player: the inventory (the HUD's Carried Items grid) and Drop.
; What the Avatar carries is in the objects list, marked carried (64) and off the map. Drop: D, a cursor in the grid
; (the arrows, Enter), "Where?" and the map's cursor: within 5 of the Avatar, on ground that can be walked, free.

VERB_DROP = 5

; The grid again: its cells as the HUD has them, a carried thing's icon in each, in order.
RefreshInventory:
        lda GridCols
        bne .grid
        stz InvCount            ; no grid: still counted, for Drop
        jmp CountCarried
.grid:  lda GridX
        sta RX
        lda GridY
        sta RY
        lda GridCols
        jsr TimesCell
        sta RW
        lda GridRows
        jsr TimesCell
        sta RH
        jsr RestoreRect
        stz InvCount
        stz InvCol
        lda GridX
        sta CellX
        lda GridY
        sta CellY
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta Item
.l:     lda Item
        bne .one
        brl .done
.one:   ldy #6
        lda [ItemPtr],y
        and #96
        cmp #64                 ; carried, not gone
        bne .n
        lda InvCount            ; full grid: no more shown
        cmp InvMax
        bcs .n
        asl a
        asl a
        tax
        lda ItemPtr
        sta InvList,x
        lda ItemPtr+2
        sta InvList+2,x
        inc InvCount
        ldy #12                 ; its icon
        lda [ItemPtr],y
        cmp #$FFFF
        beq .next
        sta SprId
        lda ItemPtr             ; (kept round DrawPic)
        pha
        lda ItemPtr+2
        pha
        lda Item
        pha
        jsr DrawPic
        pla
        sta Item
        pla
        sta ItemPtr+2
        pla
        sta ItemPtr
.next:  lda CellX               ; the next cell: right, then the next row
        clc
        adc GridCell
        sta CellX
        inc InvCol
        lda InvCol
        cmp GridCols
        bne .n
        stz InvCol
        lda GridX
        sta CellX
        lda CellY
        clc
        adc GridCell
        sta CellY
.n:     lda #16
        jsr NextItem
        dec Item
        brl .l
.done:  lda Mode
        cmp #5
        bne .out
        jmp DrawInvCursor
.out:   rts

; No grid: how many things are carried (InvList filled all the same).
CountCarried:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta Item
.l:     lda Item
        beq .done
        ldy #6
        lda [ItemPtr],y
        and #96
        cmp #64
        bne .n
        lda InvCount
        cmp #64
        bcs .n
        asl a
        asl a
        tax
        lda ItemPtr
        sta InvList,x
        lda ItemPtr+2
        sta InvList+2,x
        inc InvCount
.n:     lda #16
        jsr NextItem
        dec Item
        bra .l
.done:  rts

; A = A * GridCell
TimesCell:
        sta MulA
        lda GridCell
        sta MB0
        jsr Mul16
        lda MulR
        rts

; The grid's cursor: a frame round the picked cell.
DrawInvCursor:
        lda GridCols
        beq .out
        lda InvSel              ; its column and row
        sta T4
        stz T5
.d:     lda T4
        cmp GridCols
        bcc .got
        sec
        sbc GridCols
        sta T4
        inc T5
        bra .d
.got:   lda T4
        jsr TimesCell
        clc
        adc GridX
        sta CurRect+2
        clc
        adc GridCell
        sta CurRect+6
        lda T5
        jsr TimesCell
        clc
        adc GridY
        sta CurRect
        clc
        adc GridCell
        sta CurRect+4
        lda KeyColor
        cmp #$FFFF
        bne .c
        lda Bright
.c:     pha
        tool $3704              ; SetSolidPenPat
        pea #bank(CurRect)
        pea #CurRect
        tool $5404              ; FrameRect
.out:   rts

; D: "Drop-" and the grid's cursor on the first thing carried.
StartDrop:
        lda #15
        jsr PrintSaid
        lda #Dash
        jsr PrintMine
        jsr RefreshInventory
        lda InvCount
        bne .some
        lda #5                  ; nothing
        jsr PrintSaid
        jmp PromptLine
.some:  stz InvSel
        lda #5
        sta Mode
        jmp RefreshInventory

; A key while the grid's cursor is up.
InvKey:
        cmp #$1B
        bne .k1
        stz Mode
        jsr RefreshInventory
        jmp PromptLine
.k1:    cmp #$0D
        beq .pick
        cmp #' '
        beq .pick
        jsr ArrowDelta
        bcc .out
        lda T3                  ; up / down: a row; left / right: a cell
        beq .side
        lda GridCols
        bne .rows
        lda #1
.rows:  ldx T3
        cpx #1
        beq .add
        eor #$FFFF
        inc a
        bra .add
.side:  lda T2
.add:   clc
        adc InvSel
        cmp InvCount            ; (negative is big: refused too)
        bcs .out
        sta InvSel
        jmp RefreshInventory
.out:   rts
.pick:  lda InvSel              ; the thing: its name, "Where?", the map's cursor
        asl a
        asl a
        tax
        lda InvList,x
        sta DropRec
        lda InvList+2,x
        sta DropRec+2
        lda DropRec
        sta ItemPtr
        lda DropRec+2
        sta ItemPtr+2
        ldy #8
        lda [ItemPtr],y
        jsr NameNumber
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        lda TxtColor
        jsr SetColor
        jsr PrintWord
        lda PenX
        clc
        adc SpaceW
        sta PenX
        lda #16                 ; Where?
        jsr PrintSaid
        lda #VERB_DROP
        sta Verb
        lda PX
        sta CurX
        lda PY
        sta CurY
        lda #4
        sta Mode
        jsr RefreshInventory
        jmp DrawView

; The place picked for Drop: within 5, walkable ground, nothing solid and no one there.
DropHere:
        jsr NewLine
        lda CurX
        sec
        sbc PX
        jsr Abs
        cmp #6
        bcs .far
        lda CurY
        sec
        sbc PY
        jsr Abs
        cmp #6
        bcs .far
        ldx CurX
        ldy CurY
        jsr MapTile
        cmp #$FFFF
        beq .no
        tay
        lda [FlagsPtr],y
        and #$0001
        beq .no
        ldx CurX
        ldy CurY
        jsr OccAt
        bne .no
        lda DropRec             ; on the map again, here
        sta ItemPtr
        lda DropRec+2
        sta ItemPtr+2
        lda CurX
        sta [ItemPtr]
        ldy #2
        lda CurY
        sta [ItemPtr],y
        ldy #6
        lda [ItemPtr],y
        and #$FFBF              ; carried no more
        sta [ItemPtr],y
        and #1
        beq .free
        jsr TakeCell
.free:  ldy #8
        lda [ItemPtr],y
        jsr NameNumber
        lda #17                 ; {1} drops {0}.
        jsr Format
        jsr DrawView
        jsr RefreshInventory
        jmp PromptLine
.far:   lda #10
        jsr Format
        jmp PromptLine
.no:    lda #9
        jsr Format
        jmp PromptLine
