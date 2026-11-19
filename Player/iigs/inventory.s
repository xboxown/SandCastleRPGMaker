; Sandcastle RPG Maker - Apple IIgs player: the inventory (the HUD's Carried Items grid) and Drop.
; What the Avatar carries is in the objects list, marked carried (64) and off the map. Drop: D, a cursor in the grid
; (the arrows, Enter), "Where?" and the map's cursor: within 5 of the Avatar, on ground that can be walked, free.
; A bag open (Use on it): the grid shows the bag first, then what is in it (inside, 256, In = the bag).

VERB_DROP = 5

; The grid again: its cells as the HUD has them, a carried thing's icon in each, in order.
RefreshInventory:
        jsr RecalcGear          ; what is worn: the Avatar's weapon and armour (wear.s)
        lda PanelView           ; the portrait view: counted, not drawn
        bne .count
        lda GridCols
        bne .grid
.count: jmp CountCarried
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
        lda OpenBag             ; a bag open: it first
        cmp #$FFFF
        beq .list
        jsr ObjAt
        jsr ShowOne
.list:  lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta Item
.l:     lda Item
        bne .one
        brl .done
.one:   jsr Listed
        bcc .n
        jsr ShowOne
.n:     lda #16
        jsr NextItem
        dec Item
        brl .l
.done:  jsr ShowWorn            ; what is worn, in its slot (wear.s)
        lda Mode
        cmp #5
        bne .out
        jmp DrawInvCursor
.out:   rts

; Carry set if the object at ItemPtr is in the grid: what is in the open bag, else what is carried.
Listed:
        lda OpenBag
        cmp #$FFFF
        beq .pack
        ldy #6
        lda [ItemPtr],y
        and #256+32
        cmp #256
        bne .no
        ldy #14
        lda [ItemPtr],y
        cmp OpenBag
        bne .no
        sec
        rts
.pack:  ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp #64                 ; carried, not gone, not in a bag, not worn
        bne .no
        sec
        rts
.no:    clc
        rts

; The object at ItemPtr in the grid's next cell (its icon), and in InvList.
ShowOne:
        lda WornMode            ; (a worn thing: its slot, not the grid's room)
        bne .room
        lda InvCount            ; full grid: no more shown
        cmp InvMax
        bcs .out
.room:  lda InvCount
        cmp #64
        bcs .out
        asl a
        asl a
        tax
        lda ItemPtr
        sta InvList,x
        lda ItemPtr+2
        sta InvList+2,x
        lda InvCount            ; its cell (the cursor's place)
        asl a
        tax
        lda CellX
        sta InvRX,x
        lda CellY
        sta InvRY,x
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
.next:  lda WornMode            ; a worn thing: in its slot, the grid's place unchanged
        bne .n
        lda CellX               ; the next cell: right, then the next row
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
.n:
.out:   rts

; No grid: how many things are carried (InvList filled all the same).
CountCarried:
        stz InvCount
        lda OpenBag
        cmp #$FFFF
        beq .list
        jsr ObjAt
        jsr CountOne
.list:  lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta Item
.l:     lda Item
        beq .done
        jsr Listed
        bcc .n
        jsr CountOne
.n:     lda #16
        jsr NextItem
        dec Item
        bra .l
.done:  jmp CountWorn          ; (wear.s)
CountOne:
        lda InvCount
        cmp #64
        bcs .out
        asl a
        asl a
        tax
        lda ItemPtr
        sta InvList,x
        lda ItemPtr+2
        sta InvList+2,x
        inc InvCount
.out:   rts

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
        lda InvSel              ; its cell, as RefreshInventory put it
        cmp InvCount
        bcc .some
        lda GridX               ; nothing there: the first cell
        sta CurRect+2
        lda GridY
        sta CurRect
        bra .wh
.some:  asl a
        tax
        lda InvRX,x
        sta CurRect+2
        lda InvRY,x
        sta CurRect
.wh:    lda CurRect+2
        clc
        adc GridCell
        sta CurRect+6
        lda CurRect
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

; *: the panel's other view.
ToggleView:
        lda PanW
        beq .out
        lda PanelView
        eor #1
        sta PanelView
        jmp DrawPanel
.out:   rts

; The panel as its view is: the inventory (the HUD's own picture, the words, the grid) or the portrait view (its
; picture with the member's portrait, the words, the stats).
DrawPanel:
        lda PanW
        bne .some
        jmp RefreshInventory
.some:  lda PanelView
        bne .portrait
        lda PanX
        sta RX
        lda PanY
        sta RY
        lda PanW
        sta RW
        lda PanH
        sta RH
        jsr RestoreRect
        jsr DrawLabels
        jmp RefreshInventory
.portrait:
        lda PanY                ; its pixels, row by row
        sta RY
        lda PanH
        sta RH
        lda PanPix
        sta RSrc
        lda PanPix+2
        sta RSrc+2
.row:   lda RY
        asl a
        asl a
        asl a
        asl a
        asl a
        sta T5
        asl a
        asl a
        clc
        adc T5
        sta T5
        lda PanX
        lsr a
        clc
        adc T5
        clc
        adc #$2000
        sta RScr
        lda #$00E1
        sta RScr+2
        lda PanW
        lsr a
        tax
        ldy #0
        sep #$20
        .a8
.b:     lda [RSrc],y
        sta [RScr],y
        iny
        dex
        bne .b
        rep #$20
        .a16
        lda PanW
        lsr a
        clc
        adc RSrc
        sta RSrc
        lda RSrc+2
        adc #0
        sta RSrc+2
        inc RY
        dec RH
        bne .row
        jsr DrawLabels
        jsr MemberOver          ; a member shown: its portrait and name (party.s)
        jsr DrawStats
        jmp RefreshInventory

; D: "Drop-" and the grid's cursor on the first thing carried.
StartDrop:
        lda PanelView           ; the inventory view for it
        beq .inv
        stz PanelView
        jsr DrawPanel
.inv:
        lda #VERB_DROP
        sta Verb
        lda #$FFFF
        sta MoveIdx
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
        bne .k0
        lda #$FFFF
        sta MoveIdx
        stz Mode
        jsr RefreshInventory
        jmp PromptLine
.k0:    cmp #$09                ; Tab: the map's cursor (not for Drop's first pick)
        bne .k1
        lda Verb
        cmp #VERB_DROP
        beq .out
        lda PX
        sta CurX
        lda PY
        sta CurY
        lda #4
        sta Mode
        jsr RefreshInventory
        jmp DrawView
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
.pick:  stz GridHas              ; what is in the picked cell
        lda InvSel
        cmp InvCount
        bcs .none
        asl a
        asl a
        tax
        lda InvList,x
        sta GridRec
        lda InvList+2,x
        sta GridRec+2
        lda #1
        sta GridHas
.none:  lda Verb
        cmp #VERB_DROP
        beq .drop
        jmp GridVerb            ; Look, Use, Move (verbs.s)
.drop:  lda GridHas
        beq .out
        lda GridRec             ; the thing: its name, "Where?", the map's cursor
        sta DropRec
        lda GridRec+2
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
        bra .go
.fr:    brl .far
.nn:    brl .no
.go:
        lda CurX
        sec
        sbc PX
        jsr Abs
        cmp #6
        bcs .fr
        lda CurY
        sec
        sbc PY
        jsr Abs
        cmp #6
        bcs .fr
        ldx CurX
        ldy CurY
        jsr MapTile
        cmp #$FFFF
        beq .nn
        tay
        lda [FlagsPtr],y
        and #$0001
        beq .nn
        ldx CurX
        ldy CurY
        jsr OccAt
        bne .nn
        lda DropRec             ; the open bag shut if it goes
        sta ItemPtr
        lda DropRec+2
        sta ItemPtr+2
        jsr IndexOf
        sta MoveIdx
        jsr CloseIfOpen
        lda #$FFFF
        sta MoveIdx
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
        and #$0EBF              ; carried no more, in no bag, on this map
        ora CurBits
        sta [ItemPtr],y
        and #1
        beq .free
        jsr TakeCell
.free:  ldy #8
        lda [ItemPtr],y
        pha
        ldx CurX                ; dropped here; what is carried changed (events.s)
        ldy CurY
        jsr DroppedEvents
        pla
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
