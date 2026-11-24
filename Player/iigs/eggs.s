; Sandcastle RPG Maker - Apple IIgs player: spawn points (eggs, Map Editor > Spawn), as the Windows Ultima 6 engine
; (U6GameWindow.Spawns.cs; Exult's objs/egg.cc):
;  - looked at four times a second (every 15 ticks): who of the party is in each egg's area (its distance round it; the
;    footpads only its own square). Map entered: hatched as the map is entered (the first look); party / Avatar near:
;    when they come in; Avatar far: when it goes out; something put down: a thing dropped in its area; a button's
;    eggs: only by a Button egg. Nocturnal: only 21:00 - 04:59.
;  - hatching: marked hatched; a roll of 1-100 at most its chance does its kind's work; Once: then it's gone.
;    Auto-reset: once the party has left its area it can hatch again (a continuous music egg stops then).
;  - monster: its characters (absent copies in the map's list, flag 4) appear on a free cell by it (its own, then the
;    cells round it, 3 out), each by its chance, healed; they roam round it. Music, sound, usecode, teleport and
;    weather run their event commands (their code, Codes); weather clears after its minutes. Missile: its projectile
;    from it - in its direction (the first one there within its range is struck), or at the Avatar or a member within
;    its range - again every delay while the Avatar stays within range (party / Avatar near). Button: the button
;    eggs within its distance hatch. Path: only where teleports go.
; GAME.DAT (after the specials): the eggs (18 words each: map, x, y, kind, when, distance, chance, flags (1 nocturnal,
; 2 once, 4 auto-reset, 8 continuous), code, stop code, missile sprite, damage min, max, direction ($FFFF at the
; party), delay (ticks), range, weather minutes, -), then the monster eggs' characters (map, place, egg, chance).
; Each egg's state (EggSt: 1 hatched, 2 gone, 4 the Avatar in, 8 the party in, 16 launching) and weather minutes left
; are saved with the game (texts.s SCRIPTDATA).

E_SIZE = 36

ParseEggs:
        lda [Ptr]
        sta EggN
        lda #2
        jsr AddPtr
        lda EggN                ; 36 bytes each
        asl a
        asl a
        sta EgT
        asl a
        asl a
        asl a
        clc
        adc EgT
        sta EgT
        ldy #0
.l:     cpy EgT
        bcs .d
        lda [Ptr],y
        sta EggTab,y
        iny
        iny
        bra .l
.d:     lda EgT
        jsr AddPtr
        lda [Ptr]
        sta EggCN
        lda #2
        jsr AddPtr
        lda EggCN               ; 8 bytes each
        asl a
        asl a
        asl a
        sta EgT
        ldy #0
.c:     cpy EgT
        bcs .e
        lda [Ptr],y
        sta EggCTab,y
        iny
        iny
        bra .c
.e:     lda EgT
        jsr AddPtr
        jmp ParseBodies         ; (bodies.s)

; X = egg A's place in EggTab
EggOff:
        asl a
        asl a
        sta EgT2
        asl a
        asl a
        asl a
        clc
        adc EgT2
        tax
        rts

; On a map (NewMapEvents): its eggs looked at afresh - the map-entered ones hatch at the first look.
EggsArrive:
        stz EgStarted
        ldx #0
.l:     cpx #128
        bcs .o
        lda EggSt,x
        and #$FFF3
        sta EggSt,x
        inx
        inx
        bra .l
.o:     rts

; A thing put down on X, Y (DroppedEvents): the something-put-down eggs see it at their next look.
EggDropped:
        stx EgDX
        sty EgDY
        pha
        lda #1
        sta EgDrop
        pla
        rts

; Carry set if EgPX, EgPY is in egg X's area.
InEgg:
        lda EggTab+8,x
        cmp #4
        beq .z
        cmp #5
        beq .z
        lda EggTab+10,x
        bra .r
.z:     lda #0
.r:     sta EgR
EgWithin:
        lda EgPX
        sec
        sbc EggTab+2,x
        jsr Abs
        cmp EgR
        beq .y
        bcs .no
.y:     lda EgPY
        sec
        sbc EggTab+4,x
        jsr Abs
        cmp EgR
        beq .in
        bcs .no
.in:    sec
        rts
.no:    clc
        rts

; Carry set if EgPX, EgPY is within egg X's missile range.
EgInRange:
        lda EggTab+30,x
        sta EgR
        bra EgWithin

; Carry set if a member (awake or not, not dead) is in egg X's area.
MembersInEgg:
        lda PartyN
        sta EgK
        ldy #0
.l:     lda EgK
        beq .no
        lda PartyTab+6,y
        and #4
        bne .n
        lda PartyTab,y
        sta EgPX
        lda PartyTab+2,y
        sta EgPY
        phy
        jsr InEgg
        ply
        bcs .yes
.n:     tya
        clc
        adc #64
        tay
        dec EgK
        bra .l
.no:    clc
        rts
.yes:   sec
        rts

; Four times a second (IdleTurn): the launchers, then every egg of this map looked at.
EggRun:
        lda EggN
        beq .out
        lda Mode
        bne .out
        lda AvDead
        bne .out
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta EgT
        pla
        lda EgT
        sec
        sbc EgTick
        cmp #15
        bcc .out
        lda EgT
        sta EgTick
        jsr EggLaunchers
        stz EgI
.l:     lda EgI
        cmp EggN
        bcs .done
        jsr EggLook
        inc EgI
        bra .l
.done:  lda #1
        sta EgStarted
        stz EgDrop
        lda EgQueued            ; their commands: the events' queue (events.s)
        beq .out
        stz EgQueued
        jmp RunQueue
.out:   rts

; Egg EgI looked at: who is in its area now, and what that sets off.
EggLook:
        lda EgI
        jsr EggOff
        stx EgO
        lda EggTab,x
        cmp CurMap
        beq .here
        rts
.here:  lda EgI
        asl a
        tay
        lda EggSt,y
        and #2
        beq .live
        rts
.live:  stz EgNow
        lda PX
        sta EgPX
        lda PY
        sta EgPY
        jsr InEgg
        bcc .a
        lda #4+8                ; the Avatar is of the party too
        tsb EgNow
        bra .p
.a:     jsr MembersInEgg
        bcc .p
        lda #8
        tsb EgNow
.p:     lda EgI
        asl a
        tay
        lda EggSt,y
        sta EgWas
        and #$FFF3
        ora EgNow
        sta EggSt,y
        stz EgRise
        stz EgFall
        ldx EgO
        lda EggTab+8,x
        sta EgC
        bne .crit
        lda EgStarted           ; map entered: at the first look
        bne .out
        lda EgWas
        and #1
        bne .out
        jmp EggHatch
.crit:  cmp #6
        bne .near
        lda EgDrop              ; something put down in its area
        beq .out
        lda EgDX
        sta EgPX
        lda EgDY
        sta EgPY
        jsr InEgg
        bcc .out
        lda #1
        sta EgRise
        bra .fire
.near:  ldy #8
        cmp #1
        beq .m
        cmp #5
        beq .m
        ldy #4
        cmp #2
        beq .m
        cmp #3
        beq .m
        cmp #4
        beq .m
.out:   rts
.m:     sty EgM
        lda EgWas
        eor #$FFFF
        and EgNow
        and EgM
        sta EgRise
        lda EgNow
        eor #$FFFF
        and EgWas
        and EgM
        sta EgFall
        lda EgC
        cmp #3                  ; Avatar far: going out sets it off, coming in arms it again
        bne .fire
        lda EgRise
        ldx EgFall
        stx EgRise
        sta EgFall
.fire:  lda EgRise
        beq .left
        lda EgI
        asl a
        tay
        lda EggSt,y
        and #1
        bne .left
        jsr EggHatch
.left:  lda EgFall
        beq .o2
        lda EgI
        asl a
        tay
        lda EggSt,y
        and #1
        beq .o2
        lda EgI
        jsr EggOff
        stx EgO
        lda EggTab+14,x
        and #4
        beq .o2
        jmp EggUnhatch
.o2:    rts

; Egg EgI hatches (Egg_object::hatch).
EggHatch:
        lda EgI
        jsr EggOff
        stx EgO
        lda EgI
        asl a
        tay
        lda EggSt,y
        and #2
        bne .out
        lda EggTab+14,x         ; nocturnal: only 21:00 - 04:59
        and #1
        beq .day
        lda Hour
        cmp #21
        bcs .day
        cmp #5
        bcs .out
.day:   lda EggSt,y
        ora #1
        sta EggSt,y
        lda EggTab+14,x
        and #2
        beq .r
        lda EggSt,y
        ora #2
        sta EggSt,y
.r:     lda #100
        jsr Random
        inc a
        ldx EgO
        cmp EggTab+12,x
        beq .go
        bcs .out
.go:    lda EggTab+6,x
        bne .nm
        jmp EggMonsters
.nm:    cmp #4
        bne .nmi
        jmp EggMissile
.nmi:   cmp #8
        bne .nb
        jmp EggButton
.nb:    cmp #7
        beq .out                ; a path: only where teleports go
        cmp #6
        bne .code
        lda EgI                 ; weather: cleared after its minutes
        asl a
        tay
        lda EggTab+32,x
        sta EggWM,y
.code:  lda EggTab+16,x
        jmp EggCode
.out:   rts

; Exult's unhatch: egg EgI (EgO) can hatch again; a continuous music egg stops.
EggUnhatch:
        lda EgI
        asl a
        tay
        lda EggSt,y
        and #$FFEE
        sta EggSt,y
        ldx EgO
        lda EggTab+18,x
; Code A ($FFFF none) into the events' queue (run at the end of the look).
EggCode:
        cmp #$FFFF
        beq .out
        tay
        lda #18
        ldx #$FFFE
        jsr QueueEvent
        lda #1
        sta EgQueued
.out:   rts

; A monster egg: its characters appear by it.
EggMonsters:
        stz EgJ
        stz EgMade
.l:     lda EgJ
        cmp EggCN
        bcc .t
        brl .done
.t:     asl a
        asl a
        asl a
        tax
        lda EggCTab,x
        cmp CurMap
        bne .n
        lda EggCTab+4,x
        cmp EgI
        bne .n
        lda EggCTab+2,x
        cmp ChrCount
        bcs .n
        sta EgIdx
        lda EggCTab+6,x
        sta EgPct
        lda EgIdx               ; its record
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        clc
        adc ChrPtr
        sta Src
        lda ChrPtr+2
        adc #0
        sta Src+2
        ldy #C_FLAGS
        lda [Src],y
        and #4
        beq .n                  ; about already
        lda #100
        jsr Random
        cmp EgPct
        bcs .n
        jsr EggSpot
        bcc .n
        lda EgPX
        sta [Src]
        ldy #2
        lda EgPY
        sta [Src],y
        ldy #C_FLAGS            ; about, awake, healed
        lda [Src],y
        and #$FFC3
        sta [Src],y
        ldy #C_MAXHP
        lda [Src],y
        ldy #C_HP
        sta [Src],y
        ldx EgPX
        ldy EgPY
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        ora #2
        sta [Ptr]
        rep #$20
        .a16
        inc EgMade
.n:     inc EgJ
        brl .l
.done:  lda EgMade
        beq .out
        lda #1
        sta Moved
        jsr DrawView
.out:   rts

; A free cell by egg EgO (its own, then the rings round it, 3 out) -> EgPX, EgPY; carry clear if none.
EggSpot:
        stz EgRing
.r:     lda EgRing
        cmp #4
        bcc .go
        clc
        rts
.go:    eor #$FFFF
        inc a
        sta EgSY
.yl:    lda EgRing
        eor #$FFFF
        inc a
        sta EgSX
.xl:    lda EgSX                ; on this ring
        jsr Abs
        sta EgT
        lda EgSY
        jsr Abs
        cmp EgT
        bcs .m
        lda EgT
.m:     cmp EgRing
        bne .nx
        ldx EgO
        lda EggTab+2,x
        clc
        adc EgSX
        sta EgPX
        lda EggTab+4,x
        clc
        adc EgSY
        sta EgPY
        jsr EgFree
        bcc .nx
        sec
        rts
.nx:    lda EgSX
        cmp EgRing
        beq .ny
        inc EgSX
        bra .xl
.ny:    lda EgSY
        cmp EgRing
        beq .nr
        inc EgSY
        bra .yl
.nr:    inc EgRing
        bra .r

; Carry set if EgPX, EgPY is on the map with no one there (as Windows' FreeSpawnCell).
EgFree:
        lda EgPX
        cmp PX
        bne .t
        lda EgPY
        cmp PY
        beq .no
.t:     ldx EgPX
        ldy EgPY
        jsr MapTile
        cmp #$FFFF
        beq .no
        ldx EgPX
        ldy EgPY
        jsr OccAt
        and #6                  ; a character's or a member's
        bne .no
        sec
        rts
.no:    clc
        rts

; A missile egg hatched: it shoots; party / Avatar near ones go on while the Avatar stays within range.
EggMissile:
        jsr EggShoot
        ldx EgO
        lda EggTab+8,x
        cmp #1
        beq .l
        cmp #2
        bne .out
.l:     lda EgI
        asl a
        tay
        lda EggSt,y
        ora #16
        sta EggSt,y
        lda EggTab+28,x
        sta EggLT,y
.out:   rts

; The launching eggs: out of range they stop; else every delay a shot.
EggLaunchers:
        stz EgI
.l:     lda EgI
        cmp EggN
        bcc .t
        rts
.t:     asl a
        tay
        lda EggSt,y
        and #16
        beq .n
        lda EgI
        jsr EggOff
        stx EgO
        lda EggSt,y
        and #2
        bne .stop
        lda EggTab,x
        cmp CurMap
        bne .stop
        lda PX
        sta EgPX
        lda PY
        sta EgPY
        jsr EgInRange
        bcc .stop
        lda EggLT,y
        sec
        sbc #15
        sta EggLT,y
        beq .fire
        bpl .n
.fire:  lda EggTab+28,x
        sta EggLT,y
        phy
        jsr EggShoot
        ply
        bra .n
.stop:  lda EggSt,y
        and #$FFEF
        sta EggSt,y
.n:     inc EgI
        bra .l

; Egg EgO's projectile: in its direction (the first one there is struck), or at one of the party within its range.
EggShoot:
        lda CurX
        pha
        lda CurY
        pha
        jsr EggShot
        pla
        sta CurY
        pla
        sta CurX
        rts
EggShot:
        ldx EgO
        lda EggTab+20,x
        cmp #$FFFF
        bne .s
        rts
.s:     sta MSprite
        lda EggTab+2,x
        sta EggRec
        lda EggTab+4,x
        sta EggRec+2
        lda EggTab+26,x
        cmp #$FFFF
        bne .bf1
        brl .aim
.bf1:
        asl a                   ; along its direction, up to its range
        tay
        lda EgDXT,y
        sta EgSX
        lda EgDYT,y
        sta EgSY
        lda EggTab+30,x
        sta EgK
        lda EggRec
        sta CurX
        lda EggRec+2
        sta CurY
.st:    lda CurX
        clc
        adc EgSX
        sta CurX
        lda CurY
        clc
        adc EgSY
        sta CurY
        jsr WhatIsThere
        lda Kind
        cmp #1
        beq .hitA
        cmp #2
        beq .hitC
        dec EgK
        bne .st
        lda CurX                ; no one: it flies to the end
        sta EggTgt
        lda CurY
        sta EggTgt+2
        lda #EggTgt
        sta DefP
        lda MyBank
        sta DefP+2
        jmp EggFly
.hitA:  lda PX
        sta AvRec
        lda PY
        sta AvRec+2
        lda #AvRec
        sta DefP
        lda MyBank
        sta DefP+2
        bra .hit
.hitC:  lda ChrRec
        sta DefP
        lda ChrRec+2
        sta DefP+2
.hit:   jsr EggFly
        jmp EggDamage
.aim:   stz EgL                 ; the Avatar and the members within range
        lda PX
        sta EgPX
        lda PY
        sta EgPY
        jsr EgInRange
        bcc .m0
        lda #AvRec
        sta EgList
        lda #2
        sta EgL
.m0:    lda PartyN
        sta EgK
        ldy #0
.ml:    lda EgK
        beq .pick
        lda PartyTab+6,y
        and #4
        bne .mn
        lda PartyTab,y
        sta EgPX
        lda PartyTab+2,y
        sta EgPY
        ldx EgO
        jsr EgInRange
        bcc .mn
        tya
        clc
        adc #PartyTab
        ldx EgL
        sta EgList,x
        inx
        inx
        stx EgL
.mn:    tya
        clc
        adc #64
        tay
        dec EgK
        bra .ml
.pick:  lda EgL
        bne .pk
        rts
.pk:    lsr a
        jsr Random
        asl a
        tax
        lda EgList,x
        sta DefP
        lda MyBank
        sta DefP+2
        lda DefP
        cmp #AvRec
        bne .h2
        lda PX
        sta AvRec
        lda PY
        sta AvRec+2
.h2:    jsr EggFly
        jmp EggDamage

; The missile flies from the egg to DefP (missile.s).
EggFly:
        lda #EggRec
        sta AtkP
        lda MyBank
        sta AtkP+2
        jmp Fly

; DefP struck by egg EgO's projectile: its damage (min..max).
EggDamage:
        ldx EgO
        lda EggTab+24,x
        sec
        sbc EggTab+22,x
        inc a
        jsr Random
        ldx EgO
        clc
        adc EggTab+22,x
        sta Dmg
        bne .hurt
        rts
.hurt:  lda #EggRec
        sta AtkP
        lda MyBank
        sta AtkP+2
        jsr DefName
        ldy #C_HP
        lda [DefP],y
        sec
        sbc Dmg
        sta [DefP],y
        jsr PartyHitMus         ; (music.s)
        lda #9                  ; its own By Hit event (ownev.s)
        jsr FireDef
        jsr DefState            ; told: wounded, knocked out, killed (bodies.s)
        bcc .alive
        jmp Dies
.alive: jmp HpChanged

; A button egg: the button eggs within its distance on this map hatch (not hatched yet, or auto-reset).
EggButton:
        lda EgI
        sta EgBI
        lda EggTab,x
        sta EgBM
        lda EggTab+2,x
        sta EgBX
        lda EggTab+4,x
        sta EgBY
        lda EggTab+10,x
        sta EgBD
        stz EgJ
.l:     lda EgJ
        cmp EggN
        bcc .t
        brl .out
.t:     cmp EgBI
        bne .bf2
        brl .n
.bf2:
        jsr EggOff
        lda EggTab+8,x
        cmp #7
        bne .n
        lda EggTab,x
        cmp EgBM
        bne .n
        lda EggTab+2,x
        sec
        sbc EgBX
        jsr Abs
        cmp EgBD
        beq .a
        bcs .n
.a:     lda EggTab+4,x
        sec
        sbc EgBY
        jsr Abs
        cmp EgBD
        beq .b
        bcs .n
.b:     lda EgJ
        asl a
        tay
        lda EggSt,y
        and #1
        beq .h
        lda EggTab+14,x
        and #4
        beq .n
.h:     lda EgBI                ; (a button may set off another: what this one needs kept)
        pha
        lda EgBM
        pha
        lda EgBX
        pha
        lda EgBY
        pha
        lda EgBD
        pha
        lda EgJ
        pha
        sta EgI
        jsr EggHatch
        pla
        sta EgJ
        pla
        sta EgBD
        pla
        sta EgBY
        pla
        sta EgBX
        pla
        sta EgBM
        pla
        sta EgBI
.n:     inc EgJ
        brl .l
.out:   lda EgBI
        sta EgI
        jsr EggOff
        stx EgO
        rts

; A minute passed (ClockRun): weather eggs' minutes; run out, the weather clears.
EggMinute:
        ldy #0
        stz EgK
.l:     lda EgK
        cmp EggN
        bcs .out
        lda EggWM,y
        beq .n
        dec a
        sta EggWM,y
        bne .n
        stz WKind
        stz WInt
        phy
        jsr WeatherBuild        ; (weather.s)
        ply
.n:     iny
        iny
        inc EgK
        bra .l
.out:   rts

EgDXT:    .dw 0,1,1,1,0,$FFFF,$FFFF,$FFFF       ; N NE E SE S SW W NW
EgDYT:    .dw $FFFF,$FFFF,0,1,1,1,0,$FFFF
EggN:     .dw 0
EggCN:    .dw 0
EggTab:   .ds 64*E_SIZE
EggCTab:  .ds 128*8
EggLT:    .ds 128
EggRec:   .ds 64                ; where a missile comes from
EggTgt:   .ds 64                ; where a missile that struck no one ends
EgList:   .ds 20
EgStarted: .dw 0
EgQueued: .dw 0
EgDrop:   .dw 0
EgDX:     .dw 0
EgDY:     .dw 0
EgTick:   .dw 0
EgT:      .dw 0
EgT2:     .dw 0
EgI:      .dw 0
EgO:      .dw 0
EgJ:      .dw 0
EgK:      .dw 0
EgL:      .dw 0
EgR:      .dw 0
EgM:      .dw 0
EgC:      .dw 0
EgNow:    .dw 0
EgWas:    .dw 0
EgRise:   .dw 0
EgFall:   .dw 0
EgPX:     .dw 0
EgPY:     .dw 0
EgSX:     .dw 0
EgSY:     .dw 0
EgRing:   .dw 0
EgIdx:    .dw 0
EgPct:    .dw 0
EgMade:   .dw 0
EgBI:     .dw 0
EgBM:     .dw 0
EgBX:     .dw 0
EgBY:     .dw 0
EgBD:     .dw 0
