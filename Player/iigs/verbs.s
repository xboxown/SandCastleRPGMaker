; Sandcastle RPG Maker - Apple IIgs player: Ultima 6's verbs (as the Windows Ultima 6 engine, U6GameWindow.U6Verbs.cs).
; L Look, G Get, U Use, T Talk: "Look-" and a cursor on the Avatar; the arrows move it (as far as the view), Enter or
; Space picks the cell, Esc gives up. Look: "You see {0}." Get: something that can be taken, next to the Avatar - "{1}
; gets {0}." Use: a door next to the Avatar opens or shuts ("Locked." if it is). Talk: the one there talks (talk.s).

VERB_LOOK = 1
VERB_GET  = 2
VERB_USE  = 3
VERB_TALK = 4

; A = the verb: its word and "-", the cursor on the Avatar.
StartVerb:
        sta Verb
        cmp #VERB_LOOK
        bne .v2
        lda #11
        bra .say
.v2:    cmp #VERB_GET
        bne .v3
        lda #12
        bra .say
.v3:    cmp #VERB_USE
        bne .v4
        lda #13
        bra .say
.v4:    lda #4
.say:   jsr PrintSaid
        lda #Dash
        jsr PrintMine
        lda PX
        sta CurX
        lda PY
        sta CurY
        lda #4
        sta Mode
        jmp DrawView

; The cursor: a frame round its cell in the keyword colour.
DrawCursor:
        lda CurX
        sec
        sbc PX
        clc
        adc HalfC
        asl a
        asl a
        asl a
        asl a
        clc
        adc VX
        sta CurRect+2           ; left
        clc
        adc #16
        sta CurRect+6           ; right
        lda CurY
        sec
        sbc PY
        clc
        adc HalfR
        asl a
        asl a
        asl a
        asl a
        clc
        adc VY
        sta CurRect             ; top
        clc
        adc #16
        sta CurRect+4           ; bottom
        lda KeyColor
        cmp #$FFFF
        bne .c
        lda Bright
.c:     pha
        tool $3704              ; SetSolidPenPat
        pea #bank(CurRect)
        pea #CurRect
        tool $5404              ; FrameRect
        rts

; A key while the cursor is up.
CursorKey:
        cmp #$1B
        bne .k1
        stz Mode                ; given up
        jsr DrawView
        jmp PromptLine
.k1:    cmp #$0D
        beq .pick
        cmp #' '
        beq .pick
        jsr ArrowDelta
        bcc .out
        lda CurX                ; moved, as far as the view
        clc
        adc T2
        sta T4
        sec
        sbc PX
        jsr Abs
        cmp HalfC
        beq .x
        bcs .y
.x:     lda T4
        sta CurX
.y:     lda CurY
        clc
        adc T3
        sta T4
        sec
        sbc PY
        jsr Abs
        cmp HalfR
        beq .yy
        bcs .draw
.yy:    lda T4
        sta CurY
.draw:  jmp DrawView
.out:   rts
.pick:  stz Mode
        jsr DrawView            ; the cursor gone
        jmp PickCell

; A = |A| (signed)
Abs:
        cmp #$8000
        bcc .p
        eor #$FFFF
        inc a
.p:     rts

; What is on the cursor's cell: Kind (0 the ground, 1 the Avatar, 2 a character - ItemPtr, 3 an object - ObjRec),
; FmtName its name.
WhatIsThere:
        lda CurX
        cmp PX
        bne .chars
        lda CurY
        cmp PY
        bne .chars
        lda #1
        sta Kind
        ldx AvatarW             ; the Avatar's name
        lda WordTab,x
        sta FmtName
        lda WordTab+2,x
        sta FmtName+2
        rts
.chars: lda ChrPtr
        sta ItemPtr
        lda ChrPtr+2
        sta ItemPtr+2
        lda ChrCount
        sta Item
.c:     lda Item
        beq .objs
        lda [ItemPtr]
        cmp CurX
        bne .cn
        ldy #2
        lda [ItemPtr],y
        cmp CurY
        bne .cn
        lda #2
        sta Kind
        ldy #14
        lda [ItemPtr],y
        jmp NameNumber
.cn:    lda #16
        jsr NextItem
        dec Item
        bra .c
.objs:  stz Kind                ; the topmost: the last one there
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta Item
.o:     lda Item
        beq .found
        ldy #6
        lda [ItemPtr],y
        and #96                 ; carried or gone
        bne .on
        lda [ItemPtr]
        cmp CurX
        bne .on
        ldy #2
        lda [ItemPtr],y
        cmp CurY
        bne .on
        lda #3
        sta Kind
        lda ItemPtr
        sta ObjRec
        lda ItemPtr+2
        sta ObjRec+2
.on:    lda #16
        jsr NextItem
        dec Item
        bra .o
.found: lda Kind
        beq .ground
        lda ObjRec              ; the object's name
        sta ItemPtr
        lda ObjRec+2
        sta ItemPtr+2
        ldy #8
        lda [ItemPtr],y
        jmp NameNumber
.ground:
        ldx CurX                ; the tile's own name, or "nothing"
        ldy CurY
        jsr MapTile
        cmp #$FFFF
        beq .nothing
        asl a
        tay
        lda [TNamePtr],y
        jmp NameNumber
.nothing:
        lda #$FFFF
        ; on into NameNumber

; A = a name's number ($FFFF: "nothing") -> FmtName
NameNumber:
        cmp #$FFFF
        bne .n
        ldx #5*4
        lda WordTab,x
        sta FmtName
        lda WordTab+2,x
        sta FmtName+2
        rts
.n:     asl a
        asl a
        tax
        lda NameTab,x
        sta FmtName
        lda NameTab+2,x
        sta FmtName+2
        rts

; The cursor's cell picked: the verb on what is there.
PickCell:
        lda Verb
        cmp #VERB_DROP
        bne .what
        jmp DropHere            ; Drop's "Where?" (inventory.s)
.what:  jsr WhatIsThere
        lda Verb
        cmp #VERB_TALK
        bne .named
        lda Kind
        cmp #2
        bne .named
        lda CurX                ; a character: the talk (talk.s writes the name)
        sta NX
        lda CurY
        sta NY
        jmp TalkAt
.named: lda FmtName             ; "Look-" and the name
        sta TP
        lda FmtName+2
        sta TP+2
        lda TxtColor
        jsr SetColor
        jsr PrintWord
        jsr NewLine
        lda Verb
        cmp #VERB_LOOK
        bne .get
        lda #7                  ; You see {0}.
        jsr Format
        jmp PromptLine
.np:    brl .notpossible        ; (near jumps for the branches below)
.rg:    brl .range
.get:   cmp #VERB_GET
        bne .use
        lda Kind
        cmp #3
        bne .np
        jsr ObjFlags
        and #2                  ; can be taken
        beq .np
        jsr NextToCursor
        bcc .rg
        jsr ObjFlags            ; carried now: off the map (no longer in the way), in the grid
        ora #64
        sta [ItemPtr],y
        jsr FreeCell
        lda #8                  ; {1} gets {0}.
        jsr Format
        jsr DrawView
        jsr RefreshInventory
        jmp PromptLine
.use:   cmp #VERB_USE
        bne .talk
        lda Kind
        cmp #3
        bne .np
        jsr ObjFlags
        and #4                  ; a door
        beq .np
        jsr NextToCursor
        bcc .rg
        jsr ObjFlags
        and #8
        beq .open
        lda #14                 ; Locked.
        jsr Format
        jmp PromptLine
.open:  jsr ObjFlags            ; open <-> shut: its looks swap, it stops walkers only shut
        eor #16+1
        sta [ItemPtr],y
        ldy #4
        lda [ItemPtr],y
        pha
        ldy #10
        lda [ItemPtr],y
        ldy #4
        sta [ItemPtr],y
        pla
        ldy #10
        sta [ItemPtr],y
        jsr FreeCell
        jsr ObjFlags
        and #1
        beq .drawn
        jsr TakeCell
.drawn: jsr DrawView
        jmp PromptLine
.talk:  lda Kind                ; Talk at no one
        cmp #1
        bne .notpossible
        lda #6                  ; talking to yourself?
        jsr Format
        jmp PromptLine
.notpossible:
        lda #9
        jsr Format
        jmp PromptLine
.range: lda #10
        jsr Format
        jmp PromptLine

; ItemPtr = the picked object (ObjRec); A = its flags, Y = their place in it.
ObjFlags:
        lda ObjRec
        sta ItemPtr
        lda ObjRec+2
        sta ItemPtr+2
        ldy #6
        lda [ItemPtr],y
        rts

; Carry set if the cursor's cell is next to the Avatar (or its own).
NextToCursor:
        lda CurX
        sec
        sbc PX
        jsr Abs
        cmp #2
        bcs .no
        lda CurY
        sec
        sbc PY
        jsr Abs
        cmp #2
        bcs .no
        sec
        rts
.no:    clc
        rts

; The cursor's cell no longer has a solid object / has one again (the cell map, MakeOcc).
FreeCell:
        ldx CurX
        ldy CurY
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        and #$FE
        sta [Ptr]
        rep #$20
        .a16
        rts
TakeCell:
        ldx CurX
        ldy CurY
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        ora #1
        sta [Ptr]
        rep #$20
        .a16
        rts

; A = one of GAME.DAT's words, written with {0} = FmtName and {1} = the Avatar's name, wrapped.
Format:
        asl a
        asl a
        tax
        lda WordTab,x
        sta SayP
        lda WordTab+2,x
        sta SayP+2
        ldx #0                  ; FmtBuf <- the word, the names put in
        ldy #0
.c:     lda [SayP],y
        and #$00FF
        beq .end
        cmp #'{'
        bne .put
        iny
        lda [SayP],y
        and #$00FF
        iny
        iny                     ; past "0}"
        phy
        cmp #'1'
        beq .avatar
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        bra .name
.avatar:
        phx
        ldx AvatarW
        lda WordTab,x
        sta TP
        lda WordTab+2,x
        sta TP+2
        plx
.name:  ldy #0
.nc:    lda [TP],y
        and #$00FF
        beq .named
        cpx #120
        bcs .named
        sep #$20
        .a8
        sta FmtBuf,x
        rep #$20
        .a16
        inx
        iny
        bra .nc
.named: ply
        bra .c
.put:   cpx #120
        bcs .skip
        sep #$20
        .a8
        sta FmtBuf,x
        rep #$20
        .a16
        inx
.skip:  iny
        bra .c
.end:   sep #$20
        .a8
        lda #0
        sta FmtBuf,x
        rep #$20
        .a16
        lda #FmtBuf
        sta SayP
        lda MyBank
        sta SayP+2
        ldy #0
        jmp SayText
