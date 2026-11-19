; Sandcastle RPG Maker - Apple IIgs player: vehicles, as the Windows Ultima 6 engine (U6GameWindow.Vehicles.cs,
; VehicleTravel.cs; Exult's Barge_object). A vehicle is its pieces (objects) over a footprint of cells; everything on it -
; loose things, characters, the members, the Avatar - moves with it. Use on its controls or driver's seat takes the
; controls (someone carrying its deed, if it needs one) or lets go. The arrows drive it: it turns first if it faces
; another way (quarter turns, every piece round its middle showing its look for the new way), then moves a cell at its
; speed. Land rolls over walkable ground, Sea sails on water, Air flies over anything and comes down only on clear
; dry ground. Wheels show their moving look while it rolls; its start, moving (looping, generator 9) and stop sounds.
; Drive Vehicle, Stop Driving, Set Vehicle Speed and Travel Vehicle (by itself, a side at a time) - talk op 37.

VMAX  = 16
VPMAX = 128
VSTATE = 7*VMAX*2               ; VL VT VW VD VQ VTk VFly: saved
GEN_VEH = 9

ParseVehicles:
        lda [Ptr]
        cmp #VMAX+1
        bcc .c
        lda #VMAX
.c:     sta VCount
        lda #2
        jsr AddPtr
        ldx #0                  ; each: 13 words
.v:     cpx VCount
        bcc .v1
        brl .vd
.v1:    txa
        asl a
        tax
        ldy #0
        lda [Ptr],y
        sta VMap,x
        ldy #2
        lda [Ptr],y
        sta VTpl,x
        ldy #4
        lda [Ptr],y
        sta VL,x
        ldy #6
        lda [Ptr],y
        sta VT,x
        ldy #8
        lda [Ptr],y
        sta VW,x
        ldy #10
        lda [Ptr],y
        sta VD,x
        ldy #12
        lda [Ptr],y
        sta VMove,x
        ldy #14
        lda [Ptr],y
        sta VTk,x
        sta VOwnTk,x
        ldy #16
        lda [Ptr],y
        sta VDeed,x
        ldy #18
        lda [Ptr],y
        sta VDTxt,x
        ldy #20
        lda [Ptr],y
        sta VS1,x
        ldy #22
        lda [Ptr],y
        sta VS2,x
        ldy #24
        lda [Ptr],y
        sta VS3,x
        stz VQ,x
        stz VFly,x
        lda #26
        jsr AddPtr
        txa
        lsr a
        inc a
        tax
        brl .v
.vd:    lda [Ptr]
        cmp #VPMAX+1
        bcc .c2
        lda #VPMAX
.c2:    sta VPCount
        lda #2
        jsr AddPtr
        ldx #0                  ; each piece: 8 words
.p:     cpx VPCount
        bcc .p1
        brl .pd
.p1:    txa
        asl a
        tax
        ldy #0
        lda [Ptr],y
        sta PVeh,x
        ldy #2
        lda [Ptr],y
        sta PObj,x
        ldy #4
        lda [Ptr],y
        sta PRole,x
        ldy #6
        lda [Ptr],y
        sta PS0,x
        ldy #8
        lda [Ptr],y
        sta PS1,x
        ldy #10
        lda [Ptr],y
        sta PS2,x
        ldy #12
        lda [Ptr],y
        sta PS3,x
        ldy #14
        lda [Ptr],y
        sta PMov,x
        lda #16
        jsr AddPtr
        txa
        lsr a
        inc a
        tax
        brl .p
.pd:    lda [Ptr]               ; the deeds' words
        sta VT0
        lda #2
        jsr AddPtr
        lda Ptr
        sta VStr
        lda Ptr+2
        sta VStr+2
        lda #$FFFF
        sta DrvV
        sta VAuto
        stz VMoving
        lda VT0
        jsr AddPtr
        jmp ParseLevels         ; (levels.s)

; ---- objects ----
; ItemPtr = object A's record.
ObjAddr:
        stz ItemPtr+2
        asl a
        rol ItemPtr+2
        asl a
        rol ItemPtr+2
        asl a
        rol ItemPtr+2
        asl a
        rol ItemPtr+2
        clc
        adc ObjPtr
        sta ItemPtr
        lda ItemPtr+2
        adc ObjPtr+2
        sta ItemPtr+2
        rts

; A = ItemPtr's object's number.
ObjIdx:
        lda ItemPtr
        sec
        sbc ObjPtr
        sta VT0
        lda ItemPtr+2
        sbc ObjPtr+2
        lsr a
        ror VT0
        lsr a
        ror VT0
        lsr a
        ror VT0
        lsr a
        ror VT0
        lda VT0
        rts

; ---- Use on a piece ----
; ObjRec used on: its vehicle's controls or driver's seat - the controls taken or let go (carry set); else carry clear.
VehUse:
        lda ObjRec
        sta ItemPtr
        lda ObjRec+2
        sta ItemPtr+2
        jsr ObjIdx
        sta VT0
        ldx #0
.l:     cpx VPCount
        bcs .no
        txa
        asl a
        tay
        lda PObj,y
        cmp VT0
        beq .got
        inx
        bra .l
.no:    clc
        rts
.got:   lda #$FFFF
        sta VSeat
        lda PRole,y
        cmp #1
        bne .nseat
        lda PObj,y              ; a driver's seat: the Avatar sits in it (aboard)
        sta VSeat
        bra .ctl
.nseat: cmp #2
        bne .no
.ctl:   lda PVeh,y
        sta VCur
        cmp DrvV
        bne .take
        jsr VehRelease          ; its controls again: let go
        sec
        rts
.take:  asl a
        tax
        lda VMap,x
        cmp CurMap
        bne .no
        lda VDeed,x             ; a deed needed: someone carries it
        cmp #$FFFF
        beq .ok
        jsr CountItem
        cmp #0
        bne .ok
        lda VCur
        asl a
        tax
        lda VDTxt,x
        jsr SayDeed
        jsr PromptLine
        sec
        rts
.ok:    lda VSeat
        cmp #$FFFF
        beq .tk
        jsr ObjAddr             ; sat in its seat
        lda [ItemPtr]
        sta PX
        ldy #2
        lda [ItemPtr],y
        sta PY
        jsr DrawView
.tk:    jsr TakeControls
        jsr PromptLine
        sec
        rts

; The controls of VCur: driven now (a flier goes up), its start sound.
TakeControls:
        lda VCur
        sta DrvV
        stz VMoving
        asl a
        tax
        lda VMove,x
        cmp #2
        bne .s
        lda #1
        sta VFly,x
.s:     lda VS1,x
        cmp #$FFFF
        beq .say
        ldx #255
        jsr PlaySound
.say:   lda #VTake
        jmp VSay

; Lets go of the controls (a flier comes down if it can; if not it stays up and the controls are kept).
VehRelease:
        lda DrvV
        cmp #$FFFF
        beq .o
        sta VCur
        asl a
        tax
        lda VFly,x
        beq .let
        jsr OkToLand
        bcs .down
        lda #VNoLand
        jsr VSay
        jmp PromptLine
.down:  lda VCur
        asl a
        tax
        stz VFly,x
.let:   jsr VStopMoving
        lda #$FFFF
        sta DrvV
        lda #VLet
        jsr VSay
        jmp PromptLine
.o:     rts

; Words of mine (a C string at A), after what is written.
VSay:
        sta SayP
        lda MyBank
        sta SayP+2
        lda PenX
        clc
        adc SpaceW
        sta PenX
        ldy #0
        jmp SayText
; The deed's words (A: their place in the vehicles' words).
SayDeed:
        clc
        adc VStr
        sta SayP
        lda VStr+2
        adc #0
        sta SayP+2
        lda PenX
        clc
        adc SpaceW
        sta PenX
        ldy #0
        jmp SayText

; ---- driving ----
FacQ:     .dw 0,2,3,1           ; the arrows' facings (south, north, east, west) as quarters from south, clockwise

; An arrow: A the facing - a step, or driving.
StepOrDrive:
        ldx DrvV
        bpl VehKey
        jmp TryMove

; An arrow while driving: A = the facing, T2, T3 the way. Turn first if it faces another way, else a cell on.
VehKey:
        asl a
        tax
        lda FacQ,x
        sta VWant
        lda T2
        sta VDX
        lda T3
        sta VDY
        lda #1
        sta TurnUsed
        lda DrvV
        sta VCur
        asl a
        tax
        lda VMap,x
        cmp CurMap
        beq .here
        lda #$FFFF              ; (another map: not driving)
        sta DrvV
        rts
.here:  pha                     ; its speed: a cell every so many ticks
        pha
        tool $2503
        pla
        sta VNow
        pla
        lda VNow
        sec
        sbc VLastT
        ldx VCur
        cpx #0
        txa
        asl a
        tax
        lda VNow
        sec
        sbc VLastT
        cmp VTk,x
        bcs .go
        rts
.go:    lda VNow
        sta VLastT
        lda VQ,x
        cmp VWant
        beq .step
        lda VWant               ; quarters to turn clockwise
        sec
        sbc VQ,x
        and #3
        sta VQs
        jsr VehTurn
        bcs .o
        lda #VNoTurn
        jsr VSay
        jmp PromptLine
.step:  jsr VehStep
.o:     rts

; Carry set if (x, y) = VCX, VCY is inside VCur's footprint now.
InFoot:
        lda VCur
        asl a
        tax
        lda VCX
        sec
        sbc VL,x
        cmp VW,x
        bcs .no
        lda VCY
        sec
        sbc VT,x
        cmp VD,x
        bcs .no
        sec
        rts
.no:    clc
        rts

; Carry set if VCur can cover the footprint NL, NT, NW, ND: its ground suits it (not while flying), and nothing that
; isn't aboard (a solid thing, someone) is in the cells it comes onto.
VehCheck:
        lda VCur
        asl a
        tax
        lda VFly,x
        beq .g
        sec                     ; flying: over everything
        rts
.g:     lda VMove,x
        sta VKind
        lda NT
        sta VCY
.r:     lda NL
        sta VCX
.c:     ldx VCX
        ldy VCY
        jsr MapTile
        cmp #$FFFF
        beq .no
        tay
        lda [FlagsPtr],y
        and #$0003
        ldx VKind
        cpx #1
        beq .sea
        cmp #1                  ; land: walkable, not water
        bne .no
        bra .t
.sea:   and #2                  ; sea: water
        beq .no
.t:     jsr InFoot              ; outside where it is: nothing in the way
        bcs .n
        ldx VCX
        ldy VCY
        jsr OccAt
        and #7
        bne .no
.n:     inc VCX
        lda VCX
        sec
        sbc NL
        cmp NW
        bcc .c
        inc VCY
        lda VCY
        sec
        sbc NT
        cmp ND
        bcc .r
        sec
        rts
.no:    clc
        rts

; A cell on (VDX, VDY): everything aboard with it. Carry clear: blocked.
VehStep:
        lda VCur
        asl a
        tax
        lda VL,x
        clc
        adc VDX
        sta NL
        lda VT,x
        clc
        adc VDY
        sta NT
        lda VW,x
        sta NW
        lda VD,x
        sta ND
        jsr VehCheck
        bcs .ok
        rts
.ok:    stz VTurnQ              ; (moved, not turned)
        jsr MoveAboard
        lda VCur
        asl a
        tax
        lda NL
        sta VL,x
        lda NT
        sta VT,x
        jsr Rolling
        jsr MakeOccAgain
        jsr DrawView
        sec
        rts

; Quarter turns VQs clockwise round its middle, if there's room for the new footprint. Carry clear: no room.
VehTurn:
        lda VCur
        asl a
        tax
        lda VL,x                ; the middle, doubled
        asl a
        clc
        adc VW,x
        sta VC2X
        lda VT,x
        asl a
        clc
        adc VD,x
        sta VC2Y
        lda VQs
        and #1
        beq .same
        lda VD,x
        sta NW
        lda VW,x
        sta ND
        bra .lt
.same:  lda VW,x
        sta NW
        lda VD,x
        sta ND
.lt:    lda VC2X
        sec
        sbc NW
        cmp #$8000
        ror a
        sta NL
        lda VC2Y
        sec
        sbc ND
        cmp #$8000
        ror a
        sta NT
        jsr VehCheck
        bcs .ok
        rts
.ok:    lda VQs
        sta VTurnQ
        jsr MoveAboard
        lda VCur
        asl a
        tax
        lda NL
        sta VL,x
        lda NT
        sta VT,x
        lda NW
        sta VW,x
        lda ND
        sta VD,x
        lda VQ,x
        clc
        adc VQs
        and #3
        sta VQ,x
        jsr PieceLooks
        jsr MakeOccAgain
        jsr DrawView
        sec
        rts

; Moves (VTurnQ 0: by VDX, VDY) or turns (VTurnQ quarters) everything in the footprint: things, characters, members,
; the Avatar.
MoveAboard:
        lda ObjCount            ; the things on this map (not carried, not in something)
        sta VN
        stz VI
.ol:    lda VI
        cmp VN
        bcs .chars
        jsr ObjAddr
        ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .on
        lda [ItemPtr]
        sta VCX
        ldy #2
        lda [ItemPtr],y
        sta VCY
        jsr InFoot
        bcc .on
        jsr NewPlace
        lda VCX
        sta [ItemPtr]
        ldy #2
        lda VCY
        sta [ItemPtr],y
.on:    inc VI
        bra .ol
.chars: lda ChrPtr              ; the characters (not the dead)
        sta ItemPtr
        lda ChrPtr+2
        sta ItemPtr+2
        lda ChrCount
        sta VN
.cl:    lda VN
        beq .mem
        ldy #6
        lda [ItemPtr],y
        and #4
        bne .cn
        lda [ItemPtr]
        sta VCX
        ldy #2
        lda [ItemPtr],y
        sta VCY
        jsr InFoot
        bcc .cn
        jsr NewPlace
        lda VCX
        sta [ItemPtr]
        ldy #2
        lda VCY
        sta [ItemPtr],y
.cn:    lda #64
        jsr NextItem
        dec VN
        bra .cl
.mem:   stz PK                  ; the members
.ml:    lda PK
        cmp PartyN
        bcs .av
        jsr MemberAt
        lda [MP]
        sta VCX
        ldy #2
        lda [MP],y
        sta VCY
        jsr InFoot
        bcc .mn
        jsr NewPlace
        lda VCX
        sta [MP]
        ldy #2
        lda VCY
        sta [MP],y
.mn:    inc PK
        bra .ml
.av:    lda PX                  ; the Avatar
        sta VCX
        lda PY
        sta VCY
        jsr InFoot
        bcc .o
        jsr NewPlace
        lda VCX
        sta PX
        lda VCY
        sta PY
.o:     rts

; VCX, VCY moved or turned (round the doubled middle VC2X, VC2Y into the footprint NL, NT, NW, ND).
NewPlace:
        lda VTurnQ
        bne .turn
        lda VCX
        clc
        adc VDX
        sta VCX
        lda VCY
        clc
        adc VDY
        sta VCY
        rts
.turn:  lda VCX                 ; relative to the middle, doubled
        asl a
        inc a
        sec
        sbc VC2X
        sta VRX
        lda VCY
        asl a
        inc a
        sec
        sbc VC2Y
        sta VRY
        lda VTurnQ
        cmp #1
        bne .q2
        lda VRY                 ; a quarter right: (x, y) -> (-y, x)
        eor #$FFFF
        inc a
        ldx VRX
        bra .set
.q2:    cmp #2
        bne .q3
        lda VRX
        eor #$FFFF
        inc a
        pha
        lda VRY
        eor #$FFFF
        inc a
        tax
        pla
        bra .set
.q3:    lda VRX                 ; a quarter left: (x, y) -> (y, -x)
        eor #$FFFF
        inc a
        tax
        lda VRY
.set:   stx VRY
        clc
        adc VC2X
        dec a
        cmp #$8000
        ror a
        sta VCX
        lda VRY
        clc
        adc VC2Y
        dec a
        cmp #$8000
        ror a
        sta VCY
        lda VCX                 ; kept inside the new footprint
        sec
        sbc NL
        bpl .x1
        lda NL
        sta VCX
        bra .y
.x1:    cmp NW
        bcc .y
        lda NL
        clc
        adc NW
        dec a
        sta VCX
.y:     lda VCY
        sec
        sbc NT
        bpl .y1
        lda NT
        sta VCY
        rts
.y1:    cmp ND
        bcc .o
        lda NT
        clc
        adc ND
        dec a
        sta VCY
.o:     rts

; VCur's pieces: their looks for the way it faces now (wheels rolling: their moving look).
PieceLooks:
        lda VCur
        asl a
        tax
        lda VQ,x
        asl a
        sta VQ2
        ldx #0
.l:     cpx VPCount
        bcs .o
        stx VI
        txa
        asl a
        tay
        lda PVeh,y
        cmp VCur
        bne .n
        lda PObj,y
        phy
        jsr ObjAddr
        ply
        ldx VQ2                 ; its look that way
        cpx #2
        bcc .s0
        beq .s1
        cpx #6
        bcc .s2
        lda PS3,y
        bra .w
.s0:    lda PS0,y
        bra .w
.s1:    lda PS1,y
        bra .w
.s2:    lda PS2,y
.w:     ldx VMoving
        beq .put
        ldx PRole,y
        cpx #3
        bne .put
        ldx PMov,y
        cpx #$FFFF
        beq .put
        txa
.put:   ldy #4
        sta [ItemPtr],y
.n:     ldx VI
        inx
        bra .l
.o:     rts

; Rolling: the wheels' moving look and the moving sound (looping).
Rolling:
        lda VMoving
        bne .o
        lda #1
        sta VMoving
        jsr PieceLooks
        lda VCur
        sta VSndV
        asl a
        tax
        lda VS2,x
        cmp #$FFFF
        beq .o
        ldx #255
        stx MVol
        ldx #GEN_VEH
        ldy #1
        jmp GenStart
.o:     rts

; Stopped: the wheels still, the moving sound off, the stop sound.
VStopMoving:
        lda VMoving
        beq .o
        stz VMoving
        jsr PieceLooks
        ldx #GEN_VEH
        jsr StopGen
        lda VCur
        asl a
        tax
        lda VS3,x
        cmp #$FFFF
        beq .o
        ldx #255
        jmp PlaySound
.o:     rts

; Carry set if the flier VCur can come down: every cell under it ground that can be walked on, not water.
OkToLand:
        lda VCur
        asl a
        tax
        lda VL,x
        sta NL
        lda VT,x
        sta NT
        lda VW,x
        sta NW
        lda VD,x
        sta ND
        lda NT
        sta VCY
.r:     lda NL
        sta VCX
.c:     ldx VCX
        ldy VCY
        jsr MapTile
        cmp #$FFFF
        beq .no
        tay
        lda [FlagsPtr],y
        and #3
        cmp #1
        bne .no
        inc VCX
        lda VCX
        sec
        sbc NL
        cmp NW
        bcc .c
        inc VCY
        lda VCY
        sec
        sbc NT
        cmp ND
        bcc .r
        sec
        rts
.no:    clc
        rts

; ---- each pass of the main loop: stopped rolling, travelling by itself ----
VehicleRun:
        lda Mode
        cmp #12
        bcc .go
        rts
.go:    pha
        pha
        tool $2503
        pla
        sta VNow
        pla
        lda VMoving             ; half a second without a cell: stopped
        beq .auto
        lda VNow
        sec
        sbc VLastT
        cmp #30
        bcc .auto
        lda VSndV
        sta VCur
        jsr VStopMoving
        lda Mode
        bne .auto
        jsr DrawView
.auto:  lda VAuto
        cmp #$FFFF
        bne .a
        rts
.a:     sta VCur
        asl a
        tax
        lda VMap,x
        cmp CurMap
        beq .m
        lda #$FFFF              ; (another map: it stops)
        sta VAuto
        rts
.m:     lda VNow
        sec
        sbc VLastT
        cmp VTk,x
        bcs .s
        rts
.s:     lda VNow
        sta VLastT
        lda VW,x                ; its middle
        lsr a
        clc
        adc VL,x
        sta VCX
        lda VD,x
        lsr a
        clc
        adc VT,x
        sta VCY
        stz VDX
        stz VDY
        lda VTX                 ; the longer way first, a side at a time
        sec
        sbc VCX
        sta VRX
        jsr AbsA
        sta VT0
        lda VTY
        sec
        sbc VCY
        sta VRY
        jsr AbsA
        ora VT0
        bne .on
        lda #$FFFF              ; there: a flier comes down if it can
        sta VAuto
        lda VFly,x
        beq .arr
        cpx #0
        lda DrvV
        cmp VCur
        beq .arr
        jsr OkToLand
        bcc .arr
        lda VCur
        asl a
        tax
        stz VFly,x
.arr:   jmp VStopMoving
.on:    lda VRY
        jsr AbsA
        cmp VT0
        bcs .ydir
        lda VRX
        jsr SignA
        sta VDX
        bra .dir
.ydir:  lda VRY
        jsr SignA
        sta VDY
.dir:   lda VDY                 ; the way: south 0, west 1, north 2, east 3
        beq .h
        bmi .n
        lda #0
        bra .w
.n:     lda #2
        bra .w
.h:     lda VDX
        bmi .wst
        lda #3
        bra .w
.wst:   lda #1
.w:     sta VWant
        lda VCur
        asl a
        tax
        lda VQ,x
        cmp VWant
        beq .step
        lda VWant
        sec
        sbc VQ,x
        and #3
        sta VQs
        jsr VehTurn
        bcs .o
        bra .stop
.step:  jsr VehStep
        bcs .o
.stop:  lda #$FFFF              ; blocked: it stops
        sta VAuto
        jmp VStopMoving
.o:     rts

AbsA:   cmp #$8000
        bcc .o
        eor #$FFFF
        inc a
.o:     rts
SignA:  cmp #0
        beq .o
        bmi .m
        lda #1
        rts
.m:     lda #$FFFF
.o:     rts

; VCur = the copy of vehicle (template) A on this map nearest the Avatar; carry clear if none.
NearestVeh:
        sta VT0
        lda #$FFFF
        sta VCur
        sta VBest
        ldx #0
.l:     cpx VCount
        bcs .d
        txa
        asl a
        tay
        lda VTpl,y
        cmp VT0
        bne .n
        lda VMap,y
        cmp CurMap
        bne .n
        lda VL,y
        sec
        sbc PX
        jsr AbsA
        sta VRX
        lda VT,y
        sec
        sbc PY
        jsr AbsA
        clc
        adc VRX
        cmp VBest
        bcs .n
        sta VBest
        stx VCur
.n:     inx
        bra .l
.d:     lda VCur
        cmp #$FFFF
        beq .no
        sec
        rts
.no:    clc
        rts

; VEHICLE sub template ...
OpVeh:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta VSub
        iny
        lda [TalkPtr],y
        sta VTplW
        iny
        iny
        sty TPC
        lda VSub
        cmp #1
        bne .n1
        jsr VehReleaseQuiet     ; stop driving
        ldy TPC
        sty TPC
        jmp RunTalk
.n1:    lda VTplW
        jsr NearestVeh
        bcs .got
        ldy TPC                 ; none here: past it
        lda VSub
        cmp #2
        bne .n3
        jsr EvalExpr
        sty TPC
        jmp RunTalk
.n3:    cmp #3
        bne .nx
        iny
        iny
        iny
        iny
.nx:    sty TPC
        jmp RunTalk
.got:   lda VSub
        bne .sp
        jsr TakeControlsQuiet   ; drive
        jmp RunTalk
.sp:    cmp #2
        bne .tr
        ldy TPC                 ; speed: cells a second (0: its own)
        jsr EvalExpr
        sty TPC
        cmp #0
        beq .own
        bmi .own
        sta ExB
        lda #60
        jsr SDiv
        lda ExQ
        bne .t
        inc a
.t:     pha
        lda VCur
        asl a
        tax
        pla
        sta VTk,x
        jmp RunTalk
.own:   lda VCur
        asl a
        tax
        lda VOwnTk,x
        sta VTk,x
        jmp RunTalk
.tr:    ldy TPC                 ; travel to x, y
        lda [TalkPtr],y
        sta VTX
        iny
        iny
        lda [TalkPtr],y
        sta VTY
        iny
        iny
        sty TPC
        lda VCur
        sta VAuto
        asl a
        tax
        lda VMove,x
        cmp #2
        bne .rt
        lda #1
        sta VFly,x
.rt:    jmp RunTalk

TakeControlsQuiet:
        lda VCur
        sta DrvV
        asl a
        tax
        lda VMove,x
        cmp #2
        bne .o
        lda #1
        sta VFly,x
.o:     rts
VehReleaseQuiet:
        lda DrvV
        cmp #$FFFF
        beq .o
        sta VCur
        asl a
        tax
        lda VFly,x
        beq .let
        jsr OkToLand
        bcc .let                ; (nowhere to land: it stays up)
        lda VCur
        asl a
        tax
        stz VFly,x
.let:   jsr VStopMoving
        lda #$FFFF
        sta DrvV
.o:     rts

; Expression op 28 u16: vehicle (template) travelling by itself.
ExVBusy:
        lda [TalkPtr],y
        iny
        iny
        sta VT0
        lda VAuto
        cmp #$FFFF
        beq .f
        asl a
        tax
        lda VTpl,x
        cmp VT0
        bne .f
        jmp True
.f:     jmp False

VTake:    .cstr "Thou hast the controls."
VLet:     .cstr "Thou dost let go of the controls."
VNoLand:  .cstr "There is nowhere to land here."
VNoTurn:  .cstr "There is no room to turn."

VSeat:    .dw 0
VCount:   .dw 0
VPCount:  .dw 0
VStr:     .dd 0
DrvV:     .dw $FFFF
VAuto:    .dw $FFFF
VCur:     .dw 0
VSndV:    .dw 0
VMoving:  .dw 0
VLastT:   .dw 0
VNow:     .dw 0
VWant:    .dw 0
VQs:      .dw 0
VQ2:      .dw 0
VTurnQ:   .dw 0
VDX:      .dw 0
VDY:      .dw 0
VCX:      .dw 0
VCY:      .dw 0
VC2X:     .dw 0
VC2Y:     .dw 0
VRX:      .dw 0
VRY:      .dw 0
VTX:      .dw 0
VTY:      .dw 0
VKind:    .dw 0
VBest:    .dw 0
VSub:     .dw 0
VTplW:    .dw 0
VT0:      .dw 0
VN:       .dw 0
VI:       .dw 0
NL:       .dw 0
NT:       .dw 0
NW:       .dw 0
ND:       .dw 0
; saved (VSTATE bytes, in this order)
VL:       .ds VMAX*2
VT:       .ds VMAX*2
VW:       .ds VMAX*2
VD:       .ds VMAX*2
VQ:       .ds VMAX*2
VTk:      .ds VMAX*2
VFly:     .ds VMAX*2
; from GAME.DAT
VMap:     .ds VMAX*2
VTpl:     .ds VMAX*2
VMove:    .ds VMAX*2
VOwnTk:   .ds VMAX*2
VDeed:    .ds VMAX*2
VDTxt:    .ds VMAX*2
VS1:      .ds VMAX*2
VS2:      .ds VMAX*2
VS3:      .ds VMAX*2
PVeh:     .ds VPMAX*2
PObj:     .ds VPMAX*2
PRole:    .ds VPMAX*2
PS0:      .ds VPMAX*2
PS1:      .ds VPMAX*2
PS2:      .ds VPMAX*2
PS3:      .ds VPMAX*2
PMov:     .ds VPMAX*2
