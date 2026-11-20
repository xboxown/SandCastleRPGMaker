; Sandcastle RPG Maker - Apple IIgs player: the work schedules, as the Windows Ultima 6 engine (U6GameWindow.Work.cs,
; Exult's schedule.cc). Nothing is faked - a worker does what the player would, within 8 squares of its schedule's spot:
;  - Bake, Sew, Blacksmith, Lab (activity 6): a crafting station there, a recipe whose ingredients it carries or the
;    chests there hold (and its tool, if one is needed, with it or in them): it walks to the station, takes them, works
;    (the station's working look, as long as the recipe says), and what it made goes into a chest there - or stays on
;    the station (By Made Something).
;  - Farm, Miner (activity 7): a ripe plant there - walked to, harvested (its tool if it needs one), the produce into a
;    chest there or left where the plant was; soil with a seed in the chests (or on it) is sown: the soil becomes the plant.
; A worker's job: WSlot (by its record): what it is doing (0 looking, 1 walking to it, 2 working), its target, the time.

WSLOTS = 16
AREA   = 8

; CP's turn at its work (schedule.s: at or near its spot GX, GY; Act 6 or 7). Carry set if it moved.
WorkTurn:
        lda GX
        sta WSpotX
        lda GY
        sta WSpotY
        jsr WorkSlot            ; X: its slot
        stx WS
        lda WPhase,x
        cmp #1
        beq .walk
        cmp #2
        bne .look
        brl .work
.look:  lda #4                  ; now and then: something to do?
        jsr Random
        beq .find
        clc
        rts
.find:  lda Act
        cmp #7
        beq .field
        jsr FindStation         ; a station and a recipe it can make
        bcc .none
        bra .got
.field: jsr FindPlant           ; a ripe plant, or soil to sow
        bcc .none
.got:   ldx WS
        lda WFound
        sta WTarget,x
        lda WRecipe
        sta WRec,x
        lda #1
        sta WPhase,x
.none:  clc
        rts
.walk:  lda WTarget,x           ; next to it: the work; else a step there
        jsr ObjAddr
        ldy #6
        lda [ItemPtr],y
        and #32
        beq .there
        ldx WS                  ; (gone: something else)
        stz WPhase,x
        clc
        rts
.there: lda [ItemPtr]
        sta GX
        ldy #2
        lda [ItemPtr],y
        sta GY
        jsr DistToGoal
        cmp #2
        bcc .arrive
        jmp StepToGoal
.arrive: lda Act
        cmp #7
        bne .station
        ldx WS
        stz WPhase,x
        jsr FieldWork           ; harvest or sow it
        clc
        rts
.station:
        jsr StartWork
        clc
        rts
.work:  dec WTimer,x            ; working: done when its time is up
        beq .done
        clc
        rts
.done:  stz WPhase,x
        jsr FinishWork
        clc
        rts

; Carry set if CP is at work (on its way, or working): its work goes on wherever it is.
WorkBusy:
        jsr WorkSlot
        lda WPhase,x
        beq .no
        sec
        rts
.no:    clc
        rts

; X = CP's slot (a free one taken).
WorkSlot:
        ldx #0
.l:     cpx #WSLOTS*2
        bcs .new
        lda WLo,x
        cmp CP
        bne .n
        lda WHi,x
        cmp CP+2
        beq .o
.n:     inx
        inx
        bra .l
.new:   ldx #0                  ; a free one
.f:     cpx #WSLOTS*2
        bcs .full
        lda WHi,x
        beq .take
        inx
        inx
        bra .f
.full:  ldx #0
.take:  lda CP
        sta WLo,x
        lda CP+2
        sta WHi,x
        stz WPhase,x
.o:     rts

; A = how far CP is from GX, GY (the larger of across and down).
DistToGoal:
        lda [CP]
        sec
        sbc GX
        jsr WAbs
        sta T5
        ldy #2
        lda [CP],y
        sec
        sbc GY
        jsr WAbs
        cmp T5
        bcs .o
        lda T5
.o:     rts
WAbs:   cmp #$8000
        bcc .o
        eor #$FFFF
        inc a
.o:     rts

; Carry set if the object at ItemPtr is on this map (not carried, not in anything) within AREA of the spot.
InArea:
        ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .no
        lda [ItemPtr]
        sec
        sbc WSpotX
        jsr WAbs
        cmp #AREA+1
        bcs .no
        ldy #2
        lda [ItemPtr],y
        sec
        sbc WSpotY
        jsr WAbs
        cmp #AREA+1
        bcs .no
        sec
        rts
.no:    clc
        rts

; Carry set if object A is some worker's target already.
Claimed:
        sta T4
        ldx #0
.l:     cpx #WSLOTS*2
        bcs .no
        lda WHi,x
        beq .n
        lda WPhase,x
        beq .n
        lda WTarget,x
        cmp T4
        beq .yes
.n:     inx
        inx
        bra .l
.no:    clc
        rts
.yes:   sec
        rts

; A = CP's owner code ($8000 | map << 6 | its place on the map).
WorkerCode:
        lda CP
        sec
        sbc ChrPtr
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        sta T4
        lda CurMap
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ora T4
        ora #$8000
        rts

; A = how many things named A the worker can have: what it carries, what the chests in its area hold.
Avail:
        sta WName
        jsr WorkerCode
        sta WCode
        stz WkWCount
        stz WkWI
.l:     lda WkWI
        cmp ObjCount
        bcs .o
        jsr ObjAddr
        jsr Usable
        bcc .n
        inc WkWCount
.n:     inc WkWI
        bra .l
.o:     lda WkWCount
        rts
; Carry set if the object at ItemPtr is a WName the worker can use: carried by it, or in a chest in its area.
Usable:
        ldy #8
        lda [ItemPtr],y
        cmp WName
        bne .no
        ldy #6
        lda [ItemPtr],y
        and #32+64+256
        cmp #64
        bne .in
        ldy #14                 ; carried: by it
        lda [ItemPtr],y
        cmp WCode
        bne .no
        sec
        rts
.in:    cmp #256                ; in something: a chest in its area
        bne .no
        ldy #14
        lda [ItemPtr],y
        pha
        lda ItemPtr
        sta WKeep
        lda ItemPtr+2
        sta WKeep+2
        pla
        jsr ObjAddr
        jsr InArea
        php
        lda WKeep
        sta ItemPtr
        lda WKeep+2
        sta ItemPtr+2
        plp
        rts
.no:    clc
        rts
; WkWCount things named WName the worker can use taken (gone).
TakeAvail:
        jsr WorkerCode
        sta WCode
        stz WkWI
.l:     lda WkWCount
        beq .o
        lda WkWI
        cmp ObjCount
        bcs .o
        jsr ObjAddr
        jsr Usable
        bcc .n
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        ldy #8                  ; (no name: a spare again)
        lda #$FFFF
        sta [ItemPtr],y
        dec WkWCount
.n:     inc WkWI
        bra .l
.o:     rts

; Carry set if the worker can use a tool of kind A (0: none needed).
HasTool:
        cmp #0
        bne .k
        sec
        rts
.k:     sta WkWKind
        jsr WorkerCode
        sta WCode
        stz WkWI
.l:     lda WkWI
        cmp ObjCount
        bcs .no
        jsr ObjAddr
        ldy #10
        lda [ItemPtr],y
        and #$F000
        cmp #$D000
        bne .n
        ldy #8                  ; usable where it is? (its name, as Usable checks)
        lda [ItemPtr],y
        sta WName
        jsr Usable
        bcc .n
        ldy #10                 ; a tool of that kind? (its record's head: SpRec untouched)
        lda [ItemPtr],y
        and #$0FFF
        cmp SpCount
        bcs .n
        jsr SpecHead
        sta T4
        and #$00FF
        cmp #SP_TOOL
        bne .n
        lda T4
        xba
        and #$00FF
        cmp WkWKind
        beq .yes
.n:     inc WkWI
        bra .l
.no:    clc
        rts
.yes:   sec
        rts

; ---- stations ----
; Carry set: WFound a free station in the area, WRecipe (its offset in the record) one the worker can make.
FindStation:
        stz WJ
.l:     lda WJ
        cmp ObjCount
        bcc .one
        clc
        rts
.one:   jsr ObjAddr
        jsr InArea
        bcc .n
        jsr IsSpecial
        bcc .n
        cmp #SP_STATION
        bne .n
        lda WJ
        jsr Claimed
        bcs .n
        lda WJ                  ; the player's job on it?
        jsr PlayerJob
        bcs .n
        jsr PickRecipe
        bcc .n
        lda WJ
        sta WFound
        sec
        rts
.n:     inc WJ
        bra .l

; Carry set if the player has a job on station A.
PlayerJob:
        ldx #0
.l:     cpx JobN
        bcs .no
        cmp JobObj,x
        beq .yes
        inx
        inx
        bra .l
.no:    clc
        rts
.yes:   sec
        rts

; Carry set: WRecipe = the first recipe in SpRec (a station) the worker has the tool and the ingredients for.
PickRecipe:
        lda SpRec+5
        and #$00FF
        sta WkWN
        ldx #6
.r:     lda WkWN
        bne .try
        clc
        rts
.try:   stx WRecipe
        lda SpRec,x
        and #$00FF
        phx
        jsr HasTool
        plx
        bcc .skip
        lda SpRec+1,x
        and #$00FF
        sta WIn
        inx
        inx
.in:    lda WIn
        beq .ok
        phx
        lda SpRec,x
        jsr Avail
        plx
        sta T4
        lda SpRec+2,x
        and #$00FF
        cmp T4
        beq .y
        bcs .skip
.y:     inx
        inx
        inx
        dec WIn
        bra .in
.ok:    sec
        rts
.skip:  ldx WRecipe
        jsr RecipeSize
        dec WkWN
        bra .r

; At the station: the ingredients taken, its working look, the time it takes.
StartWork:
        ldx WS
        lda WTarget,x
        jsr ObjAddr
        jsr IsSpecial
        bcs .bf3
        brl .no
.bf3:
        ldx WS
        lda WRec,x
        sta WRecipe
        tax                     ; still there?
        lda SpRec+1,x
        and #$00FF
        sta WIn
        inx
        inx
.chk:   lda WIn
        beq .take
        phx
        lda SpRec,x
        jsr Avail
        plx
        sta T4
        lda SpRec+2,x
        and #$00FF
        cmp T4
        beq .c2
        bcc .bf4
        brl .no
.bf4:
.c2:    inx
        inx
        inx
        dec WIn
        bra .chk
.take:  ldx WRecipe
        lda SpRec+1,x
        and #$00FF
        sta WIn
        inx
        inx
.t:     lda WIn
        beq .go
        phx
        lda SpRec,x
        sta WName
        lda SpRec+2,x
        and #$00FF
        sta WkWCount
        jsr TakeAvail
        plx
        inx
        inx
        inx
        dec WIn
        bra .t
.go:    stx WkWOut                ; (its output)
        ldx WS
        lda WTarget,x
        jsr ObjAddr
        ldy #4
        lda [ItemPtr],y
        ldx WS
        sta WSprite,x
        lda SpRec+1             ; the working look, the sound
        cmp #$FFFF
        beq .s
        ldy #4
        sta [ItemPtr],y
.s:     ldx WkWOut
        lda SpRec+13,x
        cmp #$FFFF
        beq .tm
        ldx #255
        jsr PlaySound
.tm:    ldx WkWOut                ; its time: a turn each half second
        lda SpRec+11,x
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        bne .t1
        inc a
.t1:    ldx WS
        sta WTimer,x
        lda #2
        sta WPhase,x
        jmp DrawView
.no:    ldx WS
        stz WPhase,x
        rts

; Done: its look again; what it made into a chest in the area, else on it; By Made Something.
FinishWork:
        ldx WS
        lda WTarget,x
        jsr ObjAddr
        ldx WS
        lda WSprite,x
        ldy #4
        sta [ItemPtr],y
        jsr IsSpecial
        bcc .o
        lda [ItemPtr]
        sta CrSpX
        ldy #2
        lda [ItemPtr],y
        sta CrSpY
        ldy #6
        lda [ItemPtr],y
        and #$F000
        sta SpBits
        ldx WS
        lda WRec,x
        tax
        lda SpRec+1,x           ; past its ingredients: the output
        and #$00FF
        sta T4
        asl a
        clc
        adc T4
        stx T4
        clc
        adc T4
        clc
        adc #2
        sta WkWOut
        tax
        lda SpRec+10,x
        and #$00FF
        sta WkWN
        jsr AreaChest           ; where it goes
        sta SpIn
.mk:    lda WkWN
        beq .made
        ldx WkWOut
        jsr SpawnRec
        dec WkWN
        bra .mk
.made:  lda #$FFFF
        sta SpIn
        ldx WS                  ; By Made Something
        lda WTarget,x
        jsr ObjAddr
        ldy #8
        lda [ItemPtr],y
        ldx #16
        jsr ObjEvQueue
        jsr MakeOccAgain
        jsr DrawView
.o:     rts

; A = a chest in the worker's area (its number), or $FFFF.
AreaChest:
        stz WJ
.l:     lda WJ
        cmp ObjCount
        bcs .none
        jsr ObjAddr
        ldy #6
        lda [ItemPtr],y
        and #128+8
        cmp #128                ; a container, not locked
        bne .n
        jsr InArea
        bcc .n
        lda WJ
        rts
.n:     inc WJ
        bra .l
.none:  lda #$FFFF
        rts

; ---- the fields ----
; Carry set: WFound a ripe plant in the area (WRecipe 0) - or soil to sow with a seed at hand (WRecipe its plant + 1).
FindPlant:
        stz WJ
.l:     lda WJ
        cmp ObjCount
        bcc .one
        clc
        rts
.one:   jsr ObjAddr
        jsr InArea
        bcc .n
        lda WJ
        jsr Claimed
        bcs .n
        jsr IsSpecial
        bcc .soil
        cmp #SP_PLANT
        bne .n
        ldy #12                 ; ripe?
        lda [ItemPtr],y
        and #$00FF
        inc a
        sta T4
        lda SpRec+1
        and #$00FF
        cmp T4
        bne .n
        lda SpRec+24            ; its tool
        and #$00FF
        jsr HasTool
        bcc .n
        lda WJ
        jsr ObjAddr
        stz WRecipe
        bra .yes
.soil:  jsr SoilPlant           ; soil a plant grows from, its seed at hand
        bcc .n
        inc a
        sta WRecipe
.yes:   lda WJ
        sta WFound
        sec
        rts
.n:     inc WJ
        bra .l

; The object at ItemPtr is soil: carry set, A = the plant (special) sown in it, if a seed for it is at hand.
SoilPlant:
        ldy #8
        lda [ItemPtr],y
        sta WSoil
        lda ItemPtr
        sta WKeep2
        lda ItemPtr+2
        sta WKeep2+2
        stz WkWK
.l:     lda WkWK
        cmp SpCount
        bcs .no
        jsr CopySpec
        lda SpRec
        and #$00FF
        cmp #SP_PLANT
        bne .n
        lda SpRec+30            ; its soil's name
        cmp WSoil
        bne .n
        lda SpRec+40            ; its seed: at hand?
        cmp #$FFFF
        beq .n
        jsr Avail
        beq .n
        lda WKeep2
        sta ItemPtr
        lda WKeep2+2
        sta ItemPtr+2
        lda WkWK
        sec
        rts
.n:     inc WkWK
        bra .l
.no:    lda WKeep2
        sta ItemPtr
        lda WKeep2+2
        sta ItemPtr+2
        clc
        rts

; Next to the plant (or soil): harvest it, or sow it.
FieldWork:
        ldx WS
        lda WTarget,x
        sta WFound
        jsr ObjAddr
        ldx WS
        lda WRec,x
        beq .harvest
        dec a                   ; sow: a seed used, the soil becomes the plant (its first stage)
        sta WkWK
        jsr CopySpec
        lda SpRec+40
        sta WName
        lda #1
        sta WkWCount
        jsr TakeAvail
        lda WFound
        jsr ObjAddr
        ldy #8
        lda SpRec+42
        sta [ItemPtr],y
        ldy #10
        lda WkWK
        ora #$D000
        sta [ItemPtr],y
        ldy #12
        lda #0
        sta [ItemPtr],y
        lda SpRec+2
        ldy #4
        sta [ItemPtr],y
        jmp DrawView
.harvest:
        jsr IsSpecial
        bcs .bf5
        brl .o
.bf5:
        lda SpRec+12
        cmp #$FFFF
        bne .bf6
        brl .o
.bf6:
        lda SpRec+28
        cmp #$FFFF
        beq .ns
        ldx #255
        jsr PlaySound
.ns:    lda [ItemPtr]
        sta CrSpX
        ldy #2
        lda [ItemPtr],y
        sta CrSpY
        ldy #6
        lda [ItemPtr],y
        and #$F000
        sta SpBits
        jsr AreaChest           ; the produce: into a chest there, else where it grew
        sta SpIn
        lda SpRec+22
        sta WkWN
.mk:    lda WkWN
        beq .made
        ldx #12
        jsr SpawnRec
        dec WkWN
        bra .mk
.made:  lda #$FFFF
        sta SpIn
        lda WFound
        jsr ObjAddr
        lda SpRec+27            ; taken whole: its soil left
        and #$00FF
        beq .back
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        lda SpRec+30
        cmp #$FFFF
        beq .d
        ldx #30
        jsr SpawnRec
        bra .d
.back:  lda SpRec+26
        and #$00FF
        ldy #12
        sta [ItemPtr],y
        jsr PlantLook
.d:     jsr MakeOccAgain
        jmp DrawView
.o:     rts

WSpotX:   .dw 0
WSpotY:   .dw 0
WS:       .dw 0
WFound:   .dw 0
WRecipe:  .dw 0
WName:    .dw 0
WCode:    .dw 0
WkWCount:   .dw 0
WkWI:       .dw 0
WJ:       .dw 0
WkWK:       .dw 0
WkWN:       .dw 0
WIn:      .dw 0
WkWKind:    .dw 0
WkWOut:     .dw 0
WSoil:    .dw 0
WKeep:    .dd 0
WKeep2:   .dd 0
WLo:      .ds WSLOTS*2
WHi:      .ds WSLOTS*2
WPhase:   .ds WSLOTS*2
WTarget:  .ds WSLOTS*2
WRec:     .ds WSLOTS*2
WTimer:   .ds WSLOTS*2
WSprite:  .ds WSLOTS*2
