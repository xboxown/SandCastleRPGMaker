; Sandcastle RPG Maker - Apple IIgs player: the clock and the characters' schedules, as the Windows Ultima 6 engine
; (U6GameWindow.Schedules.cs, Exult's Game_clock and schedules). A game minute every 5 seconds (300 ticks of 60); every
; 3 hours (Exult's periods: midnight, 3 AM, 6 AM...) each scheduled character takes the activity of the latest entry of
; its schedule at or before now (none today: yesterday's last) and walks to its spot: 0 stand, 1 wander, 2 sleep,
; 3 pace east-west, 4 pace north-south, 5 loiter (work activities). Its record: activity +50, spot +52 +54.
; A schedule entry (10 bytes): the character's place in the map's list, the period, the activity, x, y.

S_ACT = 50
S_TX  = 52
S_TY  = 54
S_KEY = 56

; Called as the main loop waits: the clock goes on.
ClockRun:
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta T4
        pla
        lda T4
        sec
        sbc ClockTick
        cmp #300
        bcc .out
        lda T4
        sta ClockTick
        inc Minute
        jsr BurnMinute          ; lights burn down (floor.s)
        jsr EggMinute           ; weather eggs' minutes (eggs.s)
        jsr HudClock            ; the HUD's date, compass, sun and moons (hud.s)
        lda Minute
        cmp #60
        bcc .out
        stz Minute
        jsr Starving            ; a minute: the starving lose a hit point now and then (ailments.s)
        inc Hour
        jsr WeatherHour         ; (weather.s)
        jsr GrowPlants          ; (crafting.s)
        jsr EatHour             ; an hour: food used (ailments.s)
        lda Hour
        cmp #24
        bcc .h
        stz Hour
        inc Day
.h:     lda Hour                ; a new period every 3 hours
        ldx #0
.d:     cmp #3
        bcc .r
        sbc #3
        inx
        bra .d
.r:     cmp #0
        bne .out
        jmp ApplySchedules
.out:   rts

; Every scheduled character on this map: the activity and spot for now.
ApplySchedules:
        bra .go
.oj:    brl .out
.go:
        lda Hour                ; the period now
        ldx #0
.d:     cmp #3
        bcc .r
        sbc #3
        inx
        bra .d
.r:     stx Period
        lda ChrPtr              ; nothing picked yet
        sta CP
        lda ChrPtr+2
        sta CP+2
        ldx ChrCount
        beq .oj
.c:     ldy #S_KEY
        lda #$FFFF
        sta [CP],y
        jsr NextCP
        dex
        bne .c
        lda SchCount
        beq .oj
        sta T5
        lda SchPtr
        sta ItemPtr
        lda SchPtr+2
        sta ItemPtr+2
.e:     lda [ItemPtr]           ; its character
        cmp ChrCount
        bcs .n
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        clc
        adc ChrPtr
        sta CP
        lda ChrPtr+2
        adc #0
        sta CP+2
        ldy #2                  ; today's (at or before now) outrank yesterday's
        lda [ItemPtr],y
        cmp Period
        beq .today
        bcs .key
.today: clc
        adc #8
.key:   sta T4
        ldy #S_KEY
        lda [CP],y
        cmp #$FFFF
        beq .take
        cmp T4
        beq .take
        bcs .n
.take:  lda T4
        sta [CP],y
        ldy #4
        lda [ItemPtr],y
        ldy #S_ACT
        sta [CP],y
        ldy #6
        lda [ItemPtr],y
        ldy #S_TX
        sta [CP],y
        ldy #8
        lda [ItemPtr],y
        ldy #S_TY
        sta [CP],y
        ldy #C_FLAGS            ; awake again (sleep sets it at its bed)
        lda [CP],y
        and #$FFEF
        sta [CP],y
.n:     lda #10
        jsr NextItem
        dec T5
        bne .e
.out:   rts

; CP += 64
NextCP:
        lda CP
        clc
        adc #64
        sta CP
        lda CP+2
        adc #0
        sta CP+2
        rts

; CP's turn by its schedule: to its spot, then its activity there. Carry set if it moved.
ScheduleStep:
        ldy #S_KEY
        lda [CP],y
        cmp #$FFFF
        bne .some
        clc                     ; no entry: stays
        rts
.some:  ldy #S_TX
        lda [CP],y
        sta GX
        ldy #S_TY
        lda [CP],y
        sta GY
        ldy #S_ACT
        lda [CP],y
        sta Act
        cmp #6                  ; at work, on its way or working: its work (work.s)
        bcc .nw
        jsr WorkBusy
        bcc .nw
        jmp WorkTurn
.nw:    stz Radius
        lda Act
        cmp #1
        beq .wide
        cmp #5
        bcc .dist
.wide:  lda #3
        sta Radius
.dist:  lda [CP]                ; how far from its spot
        sec
        sbc GX
        jsr AbsX
        sta T5
        ldy #2
        lda [CP],y
        sec
        sbc GY
        jsr AbsY
        cmp T5
        bcs .m
        lda T5
.m:     cmp Radius
        beq .there
        bcc .there
        jmp StepToGoal          ; on its way
.there: lda Act
        cmp #6                  ; work: crafting, the fields (work.s)
        bcc .t2
        jmp WorkTurn
.t2:    cmp #2
        bne .nsl
        ldy #C_FLAGS            ; asleep
        lda [CP],y
        ora #16
        sta [CP],y
        clc
        rts
.nsl:   cmp #1
        beq .wan
        cmp #5
        beq .wan
        cmp #3
        beq .pace
        cmp #4
        beq .pace
        clc                     ; stands
        rts
.wan:   lda #3                  ; wanders / loiters: a step now and then, within 3 of the spot
        jsr Random
        bne .no
        lda #4
        jsr Random
        asl a
        tax
        lda DirX,x
        sta SDX
        lda DirY,x
        sta SDY
        lda #3
        jsr NearSpot
        bcc .no
        jmp TryCharStep
.pace:  stz SDX                 ; paces: on the way it faces, within 3; blocked or too far: turns round
        stz SDY
        ldy #8
        lda [CP],y
        tax
        lda Act
        cmp #3
        bne .vp
        lda #1
        cpx #3
        bne .hp
        lda #$FFFF
.hp:    sta SDX
        bra .go
.vp:    lda #1
        cpx #1
        bne .vv
        lda #$FFFF
.vv:    sta SDY
.go:    lda #3
        jsr NearSpot
        bcc .turn
        jsr TryCharStep
        bcs .yes
.turn:  lda SDX
        eor #$FFFF
        inc a
        sta SDX
        lda SDY
        eor #$FFFF
        inc a
        sta SDY
        lda #3
        jsr NearSpot
        bcc .no
        jmp TryCharStep
.yes:   rts
.no:    clc
        rts

Day:      .dw 0
Hour:     .dw 6
Minute:   .dw 0
ClockTick: .dw 0
Period:   .dw 0
Act:      .dw 0
SchPtr:   .dd 0
SchCount: .dw 0
