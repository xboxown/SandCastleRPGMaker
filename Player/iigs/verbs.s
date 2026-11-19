; Sandcastle RPG Maker - Apple IIgs player: Ultima 6's verbs (as the Windows Ultima 6 engine, U6GameWindow.U6Verbs.cs).
; L Look, G Get, U Use, T Talk: "Look-" and a cursor on the Avatar; the arrows move it (as far as the view), Enter or
; Space picks the cell, Esc gives up. Look: "You see {0}." Get: something that can be taken, next to the Avatar - "{1}
; gets {0}." Use: a door next to the Avatar opens or shuts ("Locked." if it is); a container spills what is in it
; ("Searching here, you find ..."). Talk: the one there talks (talk.s). Move: a thing next to the Avatar, "Where?", one
; square on from it - or into a container there, or onto the Avatar (carried). Tab: the grid's cursor instead (a carried
; thing; Use on a bag opens it in the grid, Use on it again shuts it) and back.

VERB_LOOK = 1
VERB_GET  = 2
VERB_USE  = 3
VERB_TALK = 4
VERB_MOVE = 6
VERB_ATTACK = 7
VERB_KEY  = 8
VERB_CAST = 9
VERB_USEON = 10

; A = the verb: its word and "-", the cursor on the Avatar.
StartVerb:
        pha
        lda #$FFFF              ; nothing picked for Move yet
        sta MoveIdx
        pla
        sta Verb
        cmp #VERB_LOOK          ; the others take a turn
        beq .free
        cmp #VERB_TALK
        beq .free
        lda #1
        sta TurnUsed
.free:  lda Verb
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
.v4:    cmp #VERB_MOVE
        bne .v4a
        lda #18
        bra .say
.v4a:   cmp #VERB_ATTACK
        bne .v5
        lda #25
        bra .say
.v5:    lda #4
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
        cmp #$09
        bne .k0
        jmp CursorToGrid
.k0:    cmp #$1B
        bne .k1
        lda #$FFFF
        sta MoveIdx
        stz Mode                ; given up
        jsr DrawView
        jmp PromptLine
.k1:    cmp #$0D
        beq .pick
        cmp #' '
        beq .pick
        jsr GiveDigit           ; Move: a member's number gives it to them (owner.s)
        bcc .ndig
        rts
.ndig:  lda Key
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
        beq .party
        ldy #6                  ; dead: not there
        lda [ItemPtr],y
        and #4
        bne .cn
        lda [ItemPtr]
        cmp CurX
        bne .cn
        ldy #2
        lda [ItemPtr],y
        cmp CurY
        bne .cn
        lda #2
        sta Kind
        lda ItemPtr             ; (kept: Attack strikes it)
        sta ChrRec
        lda ItemPtr+2
        sta ChrRec+2
        ldy #14
        lda [ItemPtr],y
        jmp NameNumber
.cn:    lda #64
        jsr NextItem
        dec Item
        bra .c
.party: ldx CurX                ; a party member there?
        ldy CurY
        jsr PartyAt
        bcc .objs
        lda #2
        sta Kind
        lda ItemPtr
        sta ChrRec
        lda ItemPtr+2
        sta ChrRec+2
        ldy #14
        lda [ItemPtr],y
        jmp NameNumber
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
        and #$F000+96+256       ; carried, gone, inside something or on another map
        cmp CurBits
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
        bne .mv
        jmp DropHere            ; Drop's "Where?" (inventory.s)
.mv:    cmp #VERB_USEON
        bne .mv0
        jmp UseOnCell           ; (ownev.s)
.mv0:   cmp #VERB_KEY
        bne .mv1
        jmp KeyOn               ; (keys.s)
.mv1:   cmp #VERB_CAST
        bne .mv2
        jmp CastAt              ; (magic.s)
.mv2:   cmp #VERB_MOVE
        bne .what
        lda MoveIdx
        cmp #$FFFF
        bne .second
        jmp MapFirst
.second: jmp MapTarget
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
        cmp #VERB_ATTACK
        bne .nat
        jmp AttackPicked        ; (combat.s)
.nat:   lda Verb
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
        jsr ObjFlags            ; money: into the Avatar's gold (read.s)
        ldy #10
        lda [ItemPtr],y
        cmp #$FFFE
        bne .notgold
        jmp GetGold
.notgold:
        jsr ObjFlags            ; carried now: off the map (no longer in the way), in the grid
        ora #64
        sta [ItemPtr],y
        ldy #14                 ; the Avatar's
        lda #$FFFF
        sta [ItemPtr],y
        jsr FreeCell
        jsr ObjFlags            ; picked up from here; what is carried changed (events.s)
        ldy #8
        lda [ItemPtr],y
        pha
        ldx CurX
        ldy CurY
        jsr PickedEvents
        pla                     ; its own By Pickup event (ownev.s)
        ldx #8
        jsr ObjEvQueue
        lda #8                  ; {1} gets {0}.
        jsr Format
        jsr DrawView
        jsr RefreshInventory
        jmp PromptLine
.use:   cmp #VERB_USE
        beq .usego
        brl .talk
.usego:
        jsr UseEvent            ; an event there with a Use page: it, not the thing (events.s)
        bcc .nuse
        rts
.nuse:  lda Kind
        cmp #2                  ; someone: its own Use event (ownev.s)
        bne .nchr
        jmp UseChar
.nchr:  cmp #3
        beq .isth
        brl .notpossible
.isth:  jsr NextToCursor          ; (things are used from beside them)
        bcs .nearev
        brl .range
.nearev: jsr ObjFlags           ; a thing with its own Use event (ownev.s)
        ldy #8
        lda [ItemPtr],y
        ldx #12
        jsr ObjEvent
        bcc .noev
        rts
.noev:  lda Kind
        jsr ObjFlags
        and #128                ; a container: what is in it spills out
        beq .rd
        jmp UseContainer
.rd:    jsr ObjFlags            ; a light: lit / put out (look.s)
        ldy #10
        lda [ItemPtr],y
        jsr IsLight
        bcc .rd2
        jsr NextToCursor
        bcs .lt
        brl .range
.lt:    jmp ToggleLight
.rd2:   jsr ObjFlags            ; a book or sign: read (read.s)
        ldy #10
        lda [ItemPtr],y
        jsr IsText
        bcc .door
        jsr NextToCursor
        bcs .readit
        brl .range
.readit: jsr ObjFlags
        ldy #10
        lda [ItemPtr],y
        and #$03FF
        jmp ReadText
.np2:   brl .notpossible
.rg2:   brl .range
.door:  jsr ObjFlags
        and #4                  ; a door
        beq .np2
        jsr NextToCursor
        bcc .rg2
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
        lda FarUse              ; Telekinesis: from afar (spells2.s)
        beq .near
        sec
        rts
.near:  lda CurX
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
        jsr FillFmt
        lda #FmtBuf
        sta SayP
        lda MyBank
        sta SayP+2
        ldy #0
        jmp SayText

; FmtBuf = the C string at SayP with {0} = FmtName and {1} = the Avatar's name.
FillFmt:
        ldx #0                  ; FmtBuf <- the word, the names put in
        ldy #0
.c:     lda [SayP],y
        and #$00FF
        bne .ch
        brl .end
.ch:    cmp #'{'
        bne .put
        iny
        lda [SayP],y
        and #$00FF
        iny
        iny                     ; past "0}"
        phy
        cmp #'1'
        beq .avatar
        cmp #'2'
        beq .number
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        bra .name
.number:
        lda #NumBuf             ; {2}: a number written in NumBuf
        sta TP
        lda MyBank
        sta TP+2
        bra .name
.avatar:
        phx
        lda UseWho              ; {1}: who does it (a blow), else the Avatar
        beq .av
        lda WhoName
        sta TP
        lda WhoName+2
        sta TP+2
        plx
        bra .name
.av:    ldx AvatarW
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
        brl .c
.put:   cpx #120
        bcs .skip
        sep #$20
        .a8
        sta FmtBuf,x
        rep #$20
        .a16
        inx
.skip:  iny
        brl .c
.end:   sep #$20
        .a8
        lda #0
        sta FmtBuf,x
        rep #$20
        .a16
        rts

; ---------------- Move and containers (as U6GameWindow.U6Verbs.cs / U6Things.cs) ----------------

; ItemPtr = object number A
ObjAt:
        asl a
        asl a
        asl a
        asl a
        clc
        adc ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        adc #0
        sta ItemPtr+2
        rts

; A = the number of the object at ItemPtr
IndexOf:
        lda ItemPtr
        sec
        sbc ObjPtr
        lsr a
        lsr a
        lsr a
        lsr a
        rts

; The object at ItemPtr: its name (FmtName) written in the text colour.
PrintThing:
        ldy #8
        lda [ItemPtr],y
        jsr NameNumber
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        lda TxtColor
        jsr SetColor
        jmp PrintWord

; FmtName = the name of the thing being moved
MoveName:
        lda MoveIdx
        jsr ObjAt
        ldy #8
        lda [ItemPtr],y
        jmp NameNumber

; Move is over: the cursors gone, the map and the grid as they are now.
MoveDone:
        lda #$FFFF
        sta MoveIdx
        stz Mode
        jsr DrawView
        jsr RefreshInventory
        jmp PromptLine

; Tab with the map's cursor up: the grid's cursor instead (Look, Use, Move).
CursorToGrid:
        lda Verb
        cmp #VERB_LOOK
        beq .ok
        cmp #VERB_USE
        beq .ok
        cmp #VERB_MOVE
        bne .out
.ok:    lda GridCols
        beq .out
        lda PanelView           ; the inventory view for it
        beq .inv
        stz PanelView
        jsr DrawPanel
.inv:   stz InvSel
        lda #5
        sta Mode
        jsr DrawView            ; the map's cursor gone
        jmp RefreshInventory
.out:   rts

; Move's first pick on the map: a thing that can be taken, next to the Avatar - "Where?".
MapFirst:
        jsr WhatIsThere
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        lda TxtColor
        jsr SetColor
        jsr PrintWord
        lda Kind
        cmp #3
        bne .np
        jsr ObjFlags
        and #2
        beq .np
        jsr NextToCursor
        bcc .far
        lda ObjRec
        sta ItemPtr
        lda ObjRec+2
        sta ItemPtr+2
        jsr IndexOf
        sta MoveIdx
        lda PenX
        clc
        adc SpaceW
        sta PenX
        lda #16                 ; Where?
        jsr PrintSaid
        lda #4
        sta Mode
        jmp DrawView
.np:    jsr NewLine
        lda #9
        jsr Format
        jmp PromptLine
.far:   jsr NewLine
        lda #10
        jsr Format
        jmp PromptLine

; Move's second pick on the map: one square on from where the thing is (a carried thing: within 5 of the Avatar).
MapTarget:
        jsr NewLine
        lda MoveIdx
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #64+256
        bne .carried
        lda [ItemPtr]
        sec
        sbc CurX
        jsr Abs
        cmp #2
        bcs .far
        ldy #2
        lda [ItemPtr],y
        sec
        sbc CurY
        jsr Abs
        cmp #2
        bcs .far
        bra .near
.carried:
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
.near:  jsr WhatIsThere
        lda Kind
        cmp #1
        bne .k2
        brl ToPack              ; onto the Avatar: carried
.k2:    cmp #2
        bne .k3
        jmp GiveToChr           ; a party member: theirs (owner.s)
.k3:
        cmp #3
        bne .ground
        jsr ObjFlags
        and #128
        beq .ground
        jsr IndexOf
        cmp MoveIdx
        beq .ground
        brl IntoContainer       ; into the container there
.far:   lda #10
        jsr Format
        brl MoveDone
.np:    lda #9
        jsr Format
        brl MoveDone
.ground:
        ldx CurX                ; ground that can be walked, nothing solid and no one there
        ldy CurY
        jsr MapTile
        cmp #$FFFF
        beq .np
        tay
        lda [FlagsPtr],y
        and #$0001
        beq .np
        ldx CurX
        ldy CurY
        jsr OccAt
        bne .np
        lda MoveIdx
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #1
        beq .put
        lda CurX                ; something solid: not onto the Avatar
        cmp PX
        bne .put
        lda CurY
        cmp PY
        beq .np
.put:   ldy #6
        lda [ItemPtr],y
        and #64+256
        sta WasIn
        jsr LeaveMap
        lda MoveIdx
        jsr ObjAt
        lda CurX
        sta [ItemPtr]
        ldy #2
        lda CurY
        sta [ItemPtr],y
        ldy #6
        lda [ItemPtr],y
        and #$0EBF              ; on this map: neither carried nor inside
        ora CurBits
        sta [ItemPtr],y
        and #1
        beq .free
        jsr TakeCell
.free:  jsr CloseIfOpen
        lda WasIn
        beq .quiet
        jsr MoveName            ; {1} drops {0}.
        lda #17
        jsr Format
.quiet: brl MoveDone

; The grid's pick for Look, Use, Move (InvKey: GridHas, GridRec).
GridVerb:
        lda Verb
        cmp #VERB_MOVE
        bne .first
        lda MoveIdx
        cmp #$FFFF
        beq .first
        jsr NewLine
        brl GridTarget
.first: lda GridHas
        bne .has
        stz Mode
        jsr RefreshInventory
        lda #5                  ; nothing
        jsr PrintSaid
        jsr NewLine
        lda #9
        jsr Format
        jmp PromptLine
.has:   lda GridRec
        sta ItemPtr
        lda GridRec+2
        sta ItemPtr+2
        jsr PrintThing
        lda Verb
        cmp #VERB_MOVE
        bne .look
        lda GridRec             ; Move: "Where?" - the map's cursor (Tab: the grid's)
        sta ItemPtr
        lda GridRec+2
        sta ItemPtr+2
        jsr IndexOf
        sta MoveIdx
        lda PenX
        clc
        adc SpaceW
        sta PenX
        lda #16
        jsr PrintSaid
        lda PX
        sta CurX
        lda PY
        sta CurY
        lda #4
        sta Mode
        jsr RefreshInventory
        jmp DrawView
.look:  stz Mode
        lda Verb
        cmp #VERB_LOOK
        bne .use
        jsr NewLine
        jsr RefreshInventory
        lda #7                  ; You see {0}.
        jsr Format
        jmp PromptLine
.use:   lda GridRec
        sta ItemPtr
        lda GridRec+2
        sta ItemPtr+2
        ldy #6
        lda [ItemPtr],y
        and #512+1024           ; a key or a lockpick: "On what?" (keys.s)
        beq .nkey
        jmp KeyPicked
.nkey:  lda [ItemPtr],y
        and #128
        bne .bag
        ldy #10                 ; a weapon or armour: readied / put away (wear.s)
        lda [ItemPtr],y
        cmp #$FFFF
        beq .bag
        and #$C000
        beq .bag
        lda [ItemPtr],y         ; food: eaten (ailments.s)
        jsr IsFood
        bcc .nfood
        stz Mode
        jsr NewLine
        jsr RefreshInventory
        jmp EatIt
.nfood: lda [ItemPtr],y         ; a light carried: lit / put out (look.s)
        jsr IsLight
        bcc .ntl
        stz Mode
        jmp ToggleLight
.ntl:   lda [ItemPtr],y         ; a book: read it (read.s)
        jsr IsText
        bcc .wear
        and #$03FF
        jsr NewLine
        stz Mode
        jsr RefreshInventory
        jmp ReadText
.wear:  jmp WearUse
.bag:   ldy #6
        lda [ItemPtr],y
        and #128                ; a bag: open (or shut) in the grid
        bne .isbag
        jmp StartUseOn          ; anything else: used on something - "On what?" (ownev.s)
.isbag:
        lda [ItemPtr],y
        and #8
        bne .locked
        jsr IndexOf
        cmp OpenBag
        bne .open
        lda #$FFFF
.open:  sta OpenBag
        stz InvSel
        jsr RefreshInventory
        jmp PromptLine
.locked:
        jsr NewLine
        jsr RefreshInventory
        lda #14
        jsr Format
        jmp PromptLine
.np:    jsr NewLine
        jsr RefreshInventory
        lda #9
        jsr Format
        jmp PromptLine

; Move's second pick in the grid: into the open bag (out of it if it is in it already), into a bag picked, else carried.
GridTarget:
        lda OpenBag
        cmp #$FFFF
        beq .closed
        lda MoveIdx
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #256
        beq .bag
        ldy #14
        lda [ItemPtr],y
        cmp OpenBag
        beq .pack               ; in the open bag: out of it
.bag:   lda OpenBag
        brl IntoContainer
.closed:
        lda GridHas
        beq .pack
        lda GridRec
        sta ItemPtr
        lda GridRec+2
        sta ItemPtr+2
        ldy #6
        lda [ItemPtr],y
        and #128
        beq .pack
        jsr IndexOf
        cmp MoveIdx
        beq .pack
        brl IntoContainer
.pack:  brl ToPack

; The thing being moved goes into container A (not locked, not into itself or what it holds).
IntoContainer:
        sta Into
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #8
        beq .ok
        lda #14                 ; Locked.
        jsr Format
        brl MoveDone
.ok:    lda MoveIdx
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #256
        beq .notin
        ldy #14
        lda [ItemPtr],y
        cmp Into
        beq .np                 ; in it already
.notin: lda Into                ; the container, what holds it, ... : never the thing itself
        sta T5
        ldx #16
.up:    lda T5
        cmp MoveIdx
        beq .np
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #256
        beq .fine
        ldy #14
        lda [ItemPtr],y
        sta T5
        dex
        bne .up
.fine:  jsr LeaveMap
        lda MoveIdx
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #$FFBF
        ora #256
        sta [ItemPtr],y
        ldy #14
        lda Into
        sta [ItemPtr],y
        jsr CloseIfOpen
        brl MoveDone
.np:    lda #9
        jsr Format
        brl MoveDone

; Onto the Avatar: carried ("{1} gets {0}.").
ToPack:
        lda MoveIdx
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #64+256
        cmp #64
        beq .np                 ; carried already
        jsr LeaveMap
        lda MoveIdx
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #$0EFF              ; carried (not worn, not in a bag), the Avatar's
        ora #64
        sta [ItemPtr],y
        ldy #14
        lda #$FFFF
        sta [ItemPtr],y
        jsr MoveName
        lda #8
        jsr Format
        brl MoveDone
.np:    lda #9
        jsr Format
        brl MoveDone

; The thing being moved off its cell on the map (if it is on the map and solid).
LeaveMap:
        lda MoveIdx
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #96+256+1
        cmp #1
        bne .out
        lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        and #$FE
        sta [Ptr]
        rep #$20
        .a16
.out:   rts

; The open bag shut if the thing being moved is it or holds it.
CloseIfOpen:
        lda OpenBag
        ldx #16
.up:    cmp #$FFFF
        beq .out
        cmp MoveIdx
        beq .close
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #256
        beq .out
        ldy #14
        lda [ItemPtr],y
        dex
        bne .up
        bra .out
.close: lda #$FFFF
        sta OpenBag
.out:   rts

; Use on a container next to the Avatar: everything in it spills out onto its cell (Ultima 6's Search).
UseContainer:
        jsr NextToCursor
        bcs .near
        lda #10
        jsr Format
        jmp PromptLine
.near:  jsr ObjFlags
        and #8
        beq .open
        lda #14                 ; Locked.
        jsr Format
        jmp PromptLine
.open:  jsr ObjFlags
        jsr IndexOf
        sta Into
        lda [ItemPtr]
        sta SpX
        ldy #2
        lda [ItemPtr],y
        sta SpY
        stz ListLen
        sep #$20
        .a8
        stz ListBuf
        rep #$20
        .a16
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
        and #256+32
        cmp #256
        bne .n
        ldy #14
        lda [ItemPtr],y
        cmp Into
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #$0EBF              ; out, on this map
        ora CurBits
        sta [ItemPtr],y
        lda SpX
        sta [ItemPtr]
        ldy #2
        lda SpY
        sta [ItemPtr],y
        ldy #6
        lda [ItemPtr],y
        and #1
        beq .nm
        jsr TakeCell            ; (the cursor is on the container's cell)
.nm:    ldy #8
        lda [ItemPtr],y
        jsr NameNumber
        jsr AddToList
.n:     lda #16
        jsr NextItem
        dec Item
        brl .l
.done:  lda ListLen
        bne .some
        lda #20                 ; Searching here, you find nothing.
        jsr Format
        bra .shown
.some:  lda #ListBuf
        sta FmtName
        lda MyBank
        sta FmtName+2
        lda #19                 ; Searching here, you find {0}.
        jsr Format
.shown: jsr DrawView
        jmp PromptLine

; FmtName's name added to ListBuf (", " between), up to 100 letters.
AddToList:
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        ldx ListLen
        beq .first
        cpx #96
        bcs .full
        lda #$202C              ; ", "
        sta ListBuf,x
        inx
        inx
.first: ldy #0
.c:     lda [TP],y
        and #$00FF
        beq .end
        cpx #100
        bcs .end
        sta ListBuf,x           ; (its 0 after it too)
        inx
        iny
        bra .c
.end:   stx ListLen
        lda #0
        sta ListBuf,x
.full:  rts
