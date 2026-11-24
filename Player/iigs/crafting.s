; Sandcastle RPG Maker - Apple IIgs player: plants, tools, crafting stations and musical instruments, as the Windows
; Ultima 6 engine (U6GameWindow.Crafting.cs; Exult's item usecode). An object's Other $D000 | n is special n (GAME.DAT):
;  - plants grow a stage every so many game hours (their stage in the object's Icon: low byte the stage, high the hours);
;    ripe, Use harvests them (by hand, or the tool they need used on them): the produce appears where the plant is, the
;    plant goes back a stage - or is taken whole, its soil left. Its own By Grown event (trigger 14).
;  - tools: Use, then On what? - a plant, a crafting station, or a thing with a By Tool Used event (15).
;  - crafting stations: the first recipe the party has the ingredients (and the tool) for: they are used up, the work
;    plays (its sound, the station's working look) and what it makes appears on the station (By Made Something, 16);
;    nothing to make: the station's hint.
;  - musical instruments: one of their tunes.

SP_PLANT = 1
SP_TOOL  = 2
SP_STATION = 3
SP_INSTR = 4
JOBS = 4

ParseSpecials:
        lda [Ptr]
        sta SpCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta SpOffs
        lda Ptr+2
        sta SpOffs+2
        lda SpCount
        asl a
        jsr AddPtr
        lda [Ptr]               ; the records' size
        sta T4
        lda #2
        jsr AddPtr
        lda Ptr
        sta SpBlob
        lda Ptr+2
        sta SpBlob+2
        stz JobN
        lda T4
        jsr AddPtr
        jmp ParseEggs           ; (eggs.s)

; Special A's record copied into SpRec (256 bytes at most).
CopySpec:
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
        lda [Src],y             ; its place among the records
        clc
        adc SpBlob
        sta Src
        lda SpBlob+2
        adc #0
        sta Src+2
        ldy #0
        sep #$20
        .a8
.c:     lda [Src],y
        sta SpRec,y
        iny
        cpy #255
        bcc .c
        rep #$20
        .a16
        pla
        sta Src+2
        pla
        sta Src
        rts

; A = the first word of special A's record (low: its kind; high: the next byte). SpRec untouched.
SpecHead:
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
        tax
        pla
        sta Src+2
        pla
        sta Src
        txa
        rts

; Carry set if the object at ItemPtr is special: SpRec its record, A its kind.
IsSpecial:
        ldy #10
        lda [ItemPtr],y
        and #$F000
        cmp #$D000
        bne .no
        lda [ItemPtr],y
        and #$0FFF
        cmp SpCount
        bcs .no
        jsr CopySpec
        lda SpRec
        and #$00FF
        sec
        rts
.no:    clc
        rts

; ---- Use on a thing on the map (verbs.s): a plant, a station, an instrument. Carry set if done. ----
SpecUse:
        jsr ObjFlags
        jsr IsSpecial
        bcc .no
        stz UKind               ; by hand
        cmp #SP_PLANT
        bne .s
        jmp UsePlant
.s:     cmp #SP_STATION
        bne .i
        jmp UseStation
.i:     cmp #SP_INSTR
        bne .w
        jmp PlayInstr
.w:     cmp #SP_USEABLE         ; a wand, a scroll, a potion (items.s)
        beq .it
        cmp #SP_POTION
        bne .no
.it:    jmp UseItem
.no:    clc
        rts

; Use on a carried thing (verbs.s): an instrument plays, a wand / scroll / potion is used. Carry set if done.
InvSpecial:
        lda GridRec
        sta ItemPtr
        lda GridRec+2
        sta ItemPtr+2
        jsr IsSpecial
        bcc .no
        cmp #SP_INSTR
        bne .w
        jmp PlayInstr
.w:     cmp #SP_USEABLE
        beq .it
        cmp #SP_POTION
        bne .no
.it:    jmp UseItem
.no:    clc
        rts

; A tool (UseRec) used on the thing at ItemPtr (ownev.s): a plant, a station, else its By Tool Used event. Carry set if done.
ToolOnThing:
        lda ItemPtr
        pha
        lda ItemPtr+2
        pha
        lda UseRec
        sta ItemPtr
        lda UseRec+2
        sta ItemPtr+2
        jsr IsSpecial
        bcc .notool
        cmp #SP_TOOL
        bne .notool
        lda SpRec+1             ; its kind, its sound
        and #$00FF
        sta UKind
        lda SpRec+2
        cmp #$FFFF
        beq .q
        ldx #255
        jsr PlaySound
.q:     pla
        sta ItemPtr+2
        pla
        sta ItemPtr
        jsr IsSpecial
        bcc .event
        cmp #SP_PLANT
        bne .st
        jmp UsePlant
.st:    cmp #SP_STATION
        bne .event
        jmp UseStation
.event: ldy #8                  ; By Tool Used On It
        lda [ItemPtr],y
        ldx #15
        jsr ObjEvQueue
        bcs .ran
        lda #CrNothing          ; "That does nothing."
        jsr SayMine
        jsr PromptLine
        sec
        rts
.ran:   jsr RunQueue
        sec
        rts
.notool: pla
        sta ItemPtr+2
        pla
        sta ItemPtr
        clc
        rts

; ---- plants ----
; Harvest the plant at ItemPtr (SpRec) with tool kind UKind (0 by hand). Carry set.
UsePlant:
        lda ItemPtr
        sta CrObj
        lda ItemPtr+2
        sta CrObj+2
        lda SpRec+12            ; no produce: nothing happens
        cmp #$FFFF
        bne .p
        lda #CrNothing
        jmp CrSayEnd
.p:     lda SpRec+24            ; the tool it needs
        and #$00FF
        beq .ripe
        cmp UKind
        beq .ripe
        lda #CrNeedTool         ; "Perhaps we could use a tool on that."
        jmp CrSayEnd
.ripe:  lda SpRec+25            ; only when ripe
        and #$00FF
        beq .go
        ldy #12
        lda [ItemPtr],y
        and #$00FF
        inc a
        sta T4
        lda SpRec+1
        and #$00FF
        cmp T4
        beq .go
        bcc .go
        lda #CrNotRipe          ; "It isn't ripe yet."
        jmp CrSayEnd
.go:    lda SpRec+28            ; its sound
        cmp #$FFFF
        beq .ns
        ldx #255
        jsr PlaySound
.ns:    lda [ItemPtr]           ; the produce where it is
        sta CrSpX
        ldy #2
        lda [ItemPtr],y
        sta CrSpY
        ldy #6
        lda [ItemPtr],y
        and #$F000
        sta SpBits
        lda SpRec+22
        sta CrQty
        sta T5
.mk:    lda T5
        beq .made
        ldx #12                 ; (the produce's item at SpRec+12)
        jsr SpawnRec
        dec T5
        bra .mk
.made:  lda CrObj
        sta ItemPtr
        lda CrObj+2
        sta ItemPtr+2
        lda SpRec+27            ; taken whole: gone, its soil left
        and #$00FF
        beq .back
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        lda SpRec+30
        cmp #$FFFF
        beq .told
        ldx #30
        jsr SpawnRec
        bra .told
.back:  lda SpRec+26            ; back a stage
        and #$00FF
        ldy #12
        sta [ItemPtr],y
        jsr PlantLook
.told:  lda SpRec+12            ; "You harvest 3 wheat."
        jsr NameNumber
        lda CrQty
        cmp #2
        bcc .one
        jsr QtyNum
        lda #CrHarvestN
        bra .say
.one:   lda #CrHarvest
.say:   jsr SayMine
        jsr MakeOccAgain
        jsr DrawView
        jsr PromptLine
        sec
        rts

; The plant at ItemPtr (SpRec): its sprite for its stage.
PlantLook:
        ldy #12
        lda [ItemPtr],y
        and #$00FF
        asl a
        tax
        lda SpRec+2,x
        ldy #4
        sta [ItemPtr],y
        rts

; Each game hour (schedule.s): every plant (every map) an hour on; a stage when its hours are up - By Grown.
GrowPlants:
        stz GrI
.l:     lda GrI
        cmp ObjCount
        bcc .one
        lda GrSeen              ; one grew here: the view again
        beq .r
        stz GrSeen
        lda Mode
        bne .r
        jsr DrawView
.r:     rts
.one:   jsr ObjAddr
        ldy #6
        lda [ItemPtr],y
        and #32+64+256
        bne .n
        jsr IsSpecial
        bcc .n
        cmp #SP_PLANT
        bne .n
        ldy #12
        lda [ItemPtr],y
        and #$00FF
        inc a
        sta T4
        lda SpRec+1
        and #$00FF
        cmp T4
        beq .n                  ; ripe: no more
        bcc .n
        lda [ItemPtr],y         ; an hour on
        clc
        adc #$0100
        sta [ItemPtr],y
        xba
        and #$00FF
        cmp SpRec+10
        bcc .n
        lda [ItemPtr],y         ; a stage on, the hours from 0
        and #$00FF
        inc a
        sta [ItemPtr],y
        jsr PlantLook
        ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .n
        lda #1
        sta GrSeen
        ldy #8                  ; its By Grown event
        lda [ItemPtr],y
        ldx #14
        jsr ObjEvQueue
.n:     inc GrI
        brl .l

; ---- crafting stations ----
; The station at ItemPtr (SpRec), with tool kind UKind: the first recipe the party can make. Carry set.
UseStation:
        lda ItemPtr
        sta CrObj
        lda ItemPtr+2
        sta CrObj+2
        jsr ObjIdx
        sta CrIdx
        ldx #0                  ; busy?
.b:     cpx JobN
        bcs .free
        lda JobObj,x
        cmp CrIdx
        bne .bn
        lda #CrBusy
        jmp CrSayEnd
.bn:    inx
        inx
        bra .b
.free:  lda SpRec+5             ; the recipes
        and #$00FF
        sta CrN
        ldx #6
.r:     lda CrN
        bne .try
        brl .none
.try:   stx CrAt
        lda SpRec,x             ; its tool kind
        and #$00FF
        cmp UKind
        bne .skip
        lda SpRec+1,x           ; its ingredients: all there?
        and #$00FF
        sta CrIn
        inx
        inx
.in:    lda CrIn
        beq .found
        lda SpRec,x
        phx
        jsr CountItem
        plx
        sta T4
        lda SpRec+2,x
        and #$00FF
        cmp T4
        beq .ok
        bcs .skip
.ok:    inx
        inx
        inx
        dec CrIn
        bra .in
.skip:  ldx CrAt                ; past this recipe
        jsr RecipeSize
        dec CrN
        bra .r
.found: stx CrOut               ; (its output there)
        ldx CrAt                ; the ingredients used up
        lda SpRec+1,x
        and #$00FF
        sta CrIn
        inx
        inx
.use:   lda CrIn
        beq .used
        lda SpRec,x
        sta EvName
        lda SpRec+2,x
        and #$00FF
        sta GCount
        phx
        jsr TakeItems
        plx
        inx
        inx
        inx
        dec CrIn
        bra .use
.used:  jsr RefreshInventory
        lda JobN                ; the work: a job (4 at once)
        cmp #JOBS*2
        bcc .job
        lda #CrBusy
        jmp CrSayEnd
.job:   tay
        lda CrIdx
        sta JobObj,y
        ldx CrOut
        lda SpRec+10,x          ; how many, the ticks, the sound
        and #$00FF
        sta JobQty,y
        lda SpRec+11,x
        sta JobTicks,y
        lda SpRec+13,x
        sta CrSnd
        phy
        pha
        pha
        tool $2503
        pla
        sta T4
        pla
        ply
        lda T4
        sta JobStart,y
        lda CrObj
        sta ItemPtr
        lda CrObj+2
        sta ItemPtr+2
        phy
        ldy #4
        lda [ItemPtr],y         ; its look while working
        ply
        sta JobSprite,y
        tya                     ; the output's item: JobItem + job x 5
        sta T4
        asl a
        asl a
        clc
        adc T4
        tay
        ldx CrOut
        lda SpRec,x
        sta JobItem,y
        lda SpRec+2,x
        sta JobItem+2,y
        lda SpRec+4,x
        sta JobItem+4,y
        lda SpRec+6,x
        sta JobItem+6,y
        lda SpRec+8,x
        sta JobItem+8,y
        inc JobN
        inc JobN
        lda SpRec+1             ; the working look
        cmp #$FFFF
        beq .snd
        ldy #4
        sta [ItemPtr],y
.snd:   lda CrSnd
        cmp #$FFFF
        beq .tell
        ldx #255
        jsr PlaySound
.tell:  ldy #8
        lda [ItemPtr],y
        jsr NameNumber
        lda #CrWork             ; "You set to work on the millstone..."
        jsr SayMine
        jsr DrawView
        jsr PromptLine
        sec
        rts
.none:  lda SpRec+3             ; nothing to make: its hint
        cmp #$FFFF
        beq .nh
        jsr NameNumber
        lda #CrHint
        jmp CrSayEnd
.nh:    lda #CrNoMake
        jmp CrSayEnd
; X: past the recipe at X (tool, count, count x 3, output 10, qty 1, ticks 2, sound 2)
RecipeSize:
        lda SpRec+1,x
        and #$00FF
        sta T4
        asl a
        clc
        adc T4
        clc
        adc #17
        stx T4
        clc
        adc T4
        tax
        rts

; Each pass of the main loop: a job's ticks up - its look back, what it made on it, By Made Something.
CraftRun:
        lda JobN
        bne .go
        rts
.go:    pha
        pha
        tool $2503
        pla
        sta CrNow
        pla
        ldx #0
.l:     cpx JobN
        bcc .t
        rts
.t:     lda CrNow
        sec
        sbc JobStart,x
        cmp JobTicks,x
        bcs .done
        inx
        inx
        bra .l
.done:  stx CrJ
        lda JobObj,x
        jsr ObjAddr
        ldx CrJ
        lda JobSprite,x         ; its own look again
        ldy #4
        sta [ItemPtr],y
        lda [ItemPtr]
        sta CrSpX
        ldy #2
        lda [ItemPtr],y
        sta CrSpY
        ldy #6
        lda [ItemPtr],y
        and #$F000
        sta SpBits
        lda JobQty,x
        sta CrQty
        sta T5
        txa                     ; its item into SpRec (SpawnRec reads it there)
        sta T4
        asl a
        asl a
        clc
        adc T4
        tax
        lda JobItem,x
        sta SpRec+200
        lda JobItem+2,x
        sta SpRec+202
        lda JobItem+4,x
        sta SpRec+204
        lda JobItem+6,x
        sta SpRec+206
        lda JobItem+8,x
        sta SpRec+208
.mk:    lda T5
        beq .made
        ldx #200
        jsr SpawnRec
        dec T5
        bra .mk
.made:  ldx CrJ
        lda JobObj,x
        jsr ObjAddr
        ldy #6                  ; told only on this map
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .gone
        ldy #8                  ; By Made Something
        lda [ItemPtr],y
        ldx #16
        jsr ObjEvQueue
        lda SpRec+200           ; "The millstone has made flour."
        jsr NameNumber
        lda Mode
        bne .q
        jsr NewLine
        lda CrQty
        cmp #2
        bcc .one
        jsr QtyNum
        lda #CrMadeN
        bra .say
.one:   lda #CrMade
.say:   jsr SayMine
        jsr PromptLine
.q:     jsr MakeOccAgain
        jsr DrawView
.gone:  ldx CrJ                 ; the job off the list
.sh:    lda JobObj+2,x
        sta JobObj,x
        lda JobStart+2,x
        sta JobStart,x
        lda JobTicks+2,x
        sta JobTicks,x
        lda JobQty+2,x
        sta JobQty,x
        lda JobSprite+2,x
        sta JobSprite,x
        txa
        sta T4
        asl a
        asl a
        clc
        adc T4
        tay
        lda JobItem+10,y
        sta JobItem,y
        lda JobItem+12,y
        sta JobItem+2,y
        lda JobItem+14,y
        sta JobItem+4,y
        lda JobItem+16,y
        sta JobItem+6,y
        lda JobItem+18,y
        sta JobItem+8,y
        inx
        inx
        cpx #JOBS*2-2
        bcc .sh
        dec JobN
        dec JobN
        jmp RunQueue

; ---- instruments ----
PlayInstr:
        lda SpRec+1             ; its tunes: one of them
        and #$00FF
        bne .t
        lda #CrNothing
        jmp CrSayEnd
.t:     jsr Random
        asl a
        tax
        lda SpRec+2,x
        ldx #255
        jsr PlaySound
        ldy #8
        lda [ItemPtr],y
        jsr NameNumber
        lda #CrPlay             ; "You play the lute."
        jmp CrSayEnd

; ---- making things ----
; A thing as the item at SpRec + X (name, sprite, icon, flags, other) at CrSpX, CrSpY on map SpBits: a spare object.
SpawnRec:
        stx SpK
        lda ItemPtr
        pha
        lda ItemPtr+2
        pha
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T4
        bne .bf1
        brl .done
.bf1:
.l:     ldy #6
        lda [ItemPtr],y
        and #32
        beq .n
        ldy #8
        lda [ItemPtr],y
        cmp #$FFFF
        bne .n
        ldx SpK
        lda SpIn                ; into a chest (work.s), or on the map there
        cmp #$FFFF
        beq .map
        lda #$FFFF
        sta [ItemPtr]
        ldy #2
        sta [ItemPtr],y
        lda SpRec+6,x
        and #$0FFF-64-256-32
        ora SpBits
        ora #256
        ldy #6
        sta [ItemPtr],y
        bra .rest
.map:   lda CrSpX
        sta [ItemPtr]
        ldy #2
        lda CrSpY
        sta [ItemPtr],y
        ldy #6
        lda SpRec+6,x
        and #$0FFF-64-256-32
        ora SpBits
        sta [ItemPtr],y
.rest:  ldy #4
        lda SpRec+2,x
        sta [ItemPtr],y
        ldy #8
        lda SpRec,x
        sta [ItemPtr],y
        ldy #10
        lda SpRec+8,x
        sta [ItemPtr],y
        ldy #12
        lda SpRec+4,x
        sta [ItemPtr],y
        ldy #14
        lda SpIn
        sta [ItemPtr],y
        bra .done
.n:     lda #16
        jsr NextItem
        dec T4
        beq .bf2
        brl .l
.bf2:
.done:  pla
        sta ItemPtr+2
        pla
        sta ItemPtr
        rts

; NumBuf = CrQty in figures.
QtyNum:
        ldx #0
        lda CrQty
        jsr Decimal
        sep #$20
        .a8
        lda #0
        sta StatBuf,x
        lda StatBuf
        sta NumBuf
        lda StatBuf+1
        sta NumBuf+1
        lda StatBuf+2
        sta NumBuf+2
        lda #0
        sta NumBuf+3
        rep #$20
        .a16
        rts

; My words at A, {0} the name (FmtName), {2} NumBuf: in the text area.
SayMine:
        sta SayP
        lda MyBank
        sta SayP+2
        jsr FillFmt
        lda #FmtBuf
        sta SayP
        lda MyBank
        sta SayP+2
        lda PenX
        clc
        adc SpaceW
        sta PenX
        ldy #0
        jmp SayText
CrSayEnd:
        jsr SayMine
        jsr PromptLine
        sec
        rts

CrNothing:  .cstr "Nothing happens."
CrNeedTool: .cstr "Perhaps we could use a tool on that."
CrNotRipe:  .cstr "It isn't ripe yet."
CrHarvest:  .cstr "You harvest {0}."
CrHarvestN: .cstr "You harvest {2} {0}."
CrBusy:     .cstr "Someone is working there."
CrWork:     .cstr "You set to work on {0}..."
CrHint:     .cstr "{0}"
CrNoMake:   .cstr "Thou hast not what it takes."
CrMade:     .cstr "Made: {0}."
CrMadeN:    .cstr "Made: {2} {0}."
CrPlay:     .cstr "You play {0}."
SpCount:  .dw 0
SpOffs:   .dd 0
SpBlob:   .dd 0
SpRec:    .ds 256
UKind:    .dw 0
SpIn:     .dw $FFFF
CrObj:    .dd 0
CrIdx:    .dw 0
CrN:      .dw 0
CrAt:     .dw 0
CrIn:     .dw 0
CrOut:    .dw 0
CrSnd:    .dw 0
CrQty:    .dw 0
CrNow:    .dw 0
CrJ:      .dw 0
CrSpX:      .dw 0
CrSpY:      .dw 0
SpBits:   .dw 0
SpK:      .dw 0
GrI:      .dw 0
GrSeen:   .dw 0
JobN:     .dw 0
JobObj:   .ds JOBS*2+2
JobStart: .ds JOBS*2+2
JobTicks: .ds JOBS*2+2
JobQty:   .ds JOBS*2+2
JobSprite: .ds JOBS*2+2
JobItem:  .ds JOBS*10+10
