; Sandcastle RPG Maker - Apple IIgs player: combat, as the Windows Ultima 6 engine (U6GameWindow.Combat.cs, Weapons.cs,
; U6Combat.cs - Exult's rules). A character is 64 bytes: x, y, sprite, flags (1 solid, 2 hostile, 4 dead), facing, talk,
; portrait, name, then hit points, most, strength, dexterity, intelligence, combat, magic, mana, most mana, level,
; experience, armour, weapon damage, reach, behaviour (0 stays, 1 wanders), home x, y. The Avatar has the same (AvRec).
; A Attack (the cursor, a character within reach), B combat mode on / off, Space pass. Each step or verb is a turn: then
; the hostile come on and strike; out of combat mode the world also goes on by itself every half second.
; A blow: a 30-sided die, 0 misses, 29 hits, else roll + attacker's combat - defender's must reach 14; damage 1..str/3
; + 1..weapon (bare hands 1) - 1..armour. "Rat attacks Avatar." "Missed!" "Rat lightly wounded." "Rat killed!"

C_FLAGS  = 6
C_NAME   = 14
C_HP     = 16
C_MAXHP  = 18
C_STR    = 20
C_DEX    = 22
C_INT    = 24
C_COMBAT = 26
C_MAGIC  = 28
C_MANA   = 30
C_MAXMANA = 32
C_LEVEL  = 34
C_EXP    = 36
C_ARMOUR = 38
C_WEAPON = 40
C_REACH  = 42
C_BEHAV  = 44
C_HOMEX  = 46
C_HOMEY  = 48

W_ATTACKS = 26
W_MISSED  = 27
W_BARELY  = 28
W_KILLED  = 32
W_BEGIN   = 33
W_BREAK   = 34
W_PASS    = 35
W_PRESSKEY = 37

; GAME.DAT after map 0's events: the Avatar's 24 numbers, then the levels (on, hits a level, most hits, count, needs).
ParseAvatar:
        ldy #0
.c:     lda [Ptr],y
        sta AvRec+16,y
        iny
        iny
        cpy #48
        bcc .c
        lda #48
        jsr AddPtr
        lda [Ptr]
        sta LvlOn
        ldy #2
        lda [Ptr],y
        sta LvlHits
        ldy #4
        lda [Ptr],y
        sta LvlMost
        ldy #6
        lda [Ptr],y
        sta LvlCount
        lda #8
        jsr AddPtr
        ldy #0
.n:     cpy #32
        bcs .nd
        lda [Ptr],y
        sta LvlNeeds,y
        iny
        iny
        bra .n
.nd:    lda LvlCount
        asl a
        jsr AddPtr
        lda [Ptr]               ; map 0's schedules, the hour the game starts
        sta SchCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta SchPtr
        lda Ptr+2
        sta SchPtr+2
        lda SchCount
        asl a
        sta T4
        asl a
        asl a
        clc
        adc T4                  ; 10 bytes each
        jsr AddPtr
        lda [Ptr]
        sta Hour
        ldy #2
        lda [Ptr],y
        sta PartyMax
        lda #4
        jsr AddPtr
        jsr ParseSlots          ; the HUD's body slots (wear.s)
        lda PX
        sta AvRec
        lda PY
        sta AvRec+2
        lda #1
        sta AvRec+C_FLAGS
        lda AvRec+C_REACH
        bne .r
        lda #1
        sta AvRec+C_REACH
.r:     pha                     ; the dice: from the clock
        pha
        tool $2503              ; GetTick
        pla
        ora #1
        sta Seed
        pla
        rts

; A = a random number 0 .. A-1 (xorshift)
Random:
        sta RndN
        lda Seed
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        eor Seed
        sta Seed
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        eor Seed
        sta Seed
        xba
        and #$FF00
        eor Seed
        sta Seed
        sta MulA
        lda RndN
        sta MB0
        jsr Mul16
        lda MulR+2
        rts

; ---- the Avatar's verbs and keys ----

; Attack picked a cell (verbs.s, after "Attack-name"): a character within reach is struck.
AttackPicked:
        lda Kind
        cmp #2
        beq .who
        lda #9                  ; Not possible.
        jsr Format
        jmp PromptLine
.who:   lda CurX
        sec
        sbc PX
        jsr Abs
        sta T4
        lda CurY
        sec
        sbc PY
        jsr Abs
        cmp T4
        bcc .d
        sta T4
.d:     lda T4
        cmp AvRec+C_REACH
        beq .near
        bcc .near
        lda #10                 ; Out of range!
        jsr Format
        jmp PromptLine
.near:  lda CombatOn            ; Attack starts combat (as Windows)
        bne .on
        lda #1
        sta CombatOn
.on:    jsr AvatarAtk
        lda ChrRec
        sta DefP
        lda ChrRec+2
        sta DefP+2
        stz WTLead
        jsr Strike
        lda #1
        sta TurnUsed
        rts                     ; AfterTurn: the others' turn, then the prompt

; AtkP = the Avatar
AvatarAtk:
        lda #AvRec
        sta AtkP
        lda MyBank
        sta AtkP+2
        lda PX
        sta AvRec
        lda PY
        sta AvRec+2
        rts

; B: combat mode on / off.
ToggleCombat:
        lda CombatOn
        eor #1
        sta CombatOn
        beq .off
        lda #W_BEGIN
        bra .say
.off:   lda #W_BREAK
.say:   jsr PrintSaid
        jmp PromptLine

; Space: "Pass", a turn.
PassTurn:
        lda #W_PASS
        jsr PrintSaid
        jsr PromptLine
        lda #1
        sta TurnUsed
        jmp AfterTurn

; After a key: if it took a turn, the others have theirs.
AfterTurn:
        lda TurnUsed
        bne .t
        jmp RunQueue            ; events waiting (events.s)
.t:     lda Mode                ; a verb still asking: not yet (the turn kept for when it is answered)
        beq .m
        jmp RunQueue
.m:     stz TurnUsed
        lda LightLeft           ; a Light spell burns down a turn
        beq .lt
        dec LightLeft
.lt:    jsr StayEvents          ; still on the same cell: its Stay events (events.s)
        lda Told                ; the Avatar's own blow told: the others' words follow it at once
        beq .lead
        stz WTLead
        bra .go
.lead:  lda #1
        sta WTLead
.go:    jsr WorldTurn
        lda Told
        beq .out
        stz Told
        lda Mode
        cmp #7
        beq .out
        lda #Prompt             ; a fresh line: the prompt
        jsr PrintMine
.out:   jmp RunQueue

; Nothing pressed: out of combat mode, a turn every half second.
IdleTurn:
        jsr ClockRun            ; the clock (schedule.s)
        jsr AilRun              ; ailments' times, poison (ailments.s)
        jsr AnimRun             ; animated tiles (look.s)
        lda Mode
        bne .out
        lda CombatOn
        bne .out
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta T4
        pla
        lda T4
        sec
        sbc LastTick
        cmp #30
        bcc .out
        lda T4
        sta LastTick
        lda #1
        sta WTLead
        stz Told
        jsr WorldTurn
        lda Told
        beq .out
        stz Told
        lda Mode
        cmp #7
        beq .out
        lda #Prompt
        jsr PrintMine
.out:   rts

; The Avatar dead: a key loads the saved game, else the game ends.
DeadKey:
        jmp ShowTitle           ; the title menu: Journey Onward (the saved game) or a new one (title.s)

; ---- the world's turn ----

; Every character on this map: the hostile strike the Avatar if it is in reach, else come on (within 8); those who wander
; take a step now and then.
WorldTurn:
        lda AvDead
        beq .alive
        rts
.alive: lda PX
        sta AvRec
        lda PY
        sta AvRec+2
        stz Moved
        lda ChrPtr
        sta CP
        lda ChrPtr+2
        sta CP+2
        lda ChrCount
        sta TurnN
.l:     lda TurnN
        bne .one
        brl .done
.one:   lda TimeStop            ; time stopped (spells2.s): they stand
        beq .ts
        brl .n
.ts:    ldy #C_FLAGS
        lda [CP],y
        and #4
        beq .alv
        brl .n
.alv:   lda [CP],y
        and #16                 ; asleep (a spell, its bed): does nothing
        beq .awk
        brl .n
.awk:   lda CP                  ; afraid: runs from the Avatar (spells2.s)
        ldx CP+2
        jsr AilsOf
        and #A_FEAR
        beq .naf
        jsr RunAway
        bcc .afn
        inc Moved
.afn:   brl .n
.naf:   ldy #C_FLAGS
        lda [CP],y
        and #2
        beq .calm
        jsr NearestFoe          ; the Avatar or the nearest member: DefP, distance T4 (party.s)
        ldy #C_REACH
        lda [CP],y
        bne .rc
        lda #1
.rc:    cmp T4
        bcc .chase              ; reach < distance
        lda CP                  ; it strikes
        sta AtkP
        lda CP+2
        sta AtkP+2
        jsr Strike
        lda AvDead
        beq .n
        brl .done
.chase: lda T4
        cmp #9
        bcs .n
        jsr StepToward
        bcc .n
        inc Moved
        bra .n
.calm:  ldy #C_BEHAV
        lda [CP],y
        cmp #2
        bne .nsch
        jsr ScheduleStep        ; its schedule (schedule.s)
        bcc .n
        inc Moved
        bra .n
.nsch:  cmp #1
        bne .n
        ldy #C_HOMEX
        lda [CP],y
        sta GX
        ldy #C_HOMEY
        lda [CP],y
        sta GY
        lda #4                  ; wanders: a step one turn in four, near home
        jsr Random
        bne .n
        lda #4
        jsr Random
        asl a
        tax
        lda DirX,x
        sta SDX
        lda DirY,x
        sta SDY
        lda #4
        jsr NearSpot
        bcc .n
        jsr TryCharStep
        bcc .n
        inc Moved
.n:     lda CP
        clc
        adc #64
        sta CP
        lda CP+2
        adc #0
        sta CP+2
        dec TurnN
        brl .l
.done:  lda AvDead
        bne .dd
        jsr PartyTurn           ; the members strike foes in reach (party.s)
.dd:    jsr PruneParty
        lda Moved
        beq .out
        jsr DrawView
.out:   rts

; A = the distance (the larger axis) from CP to the Avatar.
DistCP:
        lda [CP]
        sec
        sbc PX
        jsr Abs
        sta T5
        ldy #2
        lda [CP],y
        sec
        sbc PY
        jsr Abs
        cmp T5
        bcs .d
        lda T5
.d:     rts

; Carry set if CP's step SDX, SDY keeps it within A of GX, GY.
NearSpot:
        inc a
        sta Radius
        lda [CP]
        clc
        adc SDX
        sec
        sbc GX
        jsr Abs
        cmp Radius
        bcs .no
        ldy #2
        lda [CP],y
        clc
        adc SDY
        sec
        sbc GY
        jsr Abs
        cmp Radius
        bcs .no
        sec
        rts
.no:    clc
        rts

; CP steps toward the Avatar: straight at it, else along one axis, else round. Carry set if it moved.
StepToward:
        lda PX
        sta GX
        lda PY
        sta GY
; ... toward GX, GY
StepToGoal:
        lda GX
        sec
        sbc [CP]
        jsr Sign
        sta SDX
        sta TDX
        ldy #2
        lda GY
        sec
        sbc [CP],y
        jsr Sign
        sta SDY
        sta TDY
        jsr TryCharStep
        bcs .out
        lda TDX
        beq .no
        lda TDY
        beq .no
        lda TDX
        sta SDX
        stz SDY
        jsr TryCharStep
        bcs .out
        stz SDX
        lda TDY
        sta SDY
        jmp TryCharStep
.no:    lda TDY                 ; in line with it and blocked: round, one diagonal or the other
        bne .vert
        lda TDX
        sta SDX
        lda #1
        sta SDY
        jsr TryCharStep
        bcs .out
        lda #$FFFF
        sta SDY
        jmp TryCharStep
.vert:  lda TDY
        sta SDY
        lda #1
        sta SDX
        jsr TryCharStep
        bcs .out
        lda #$FFFF
        sta SDX
        jmp TryCharStep
.out:   rts

; A = -1, 0 or 1 by A's sign
Sign:
        cmp #0
        beq .z
        bmi .m
        lda #1
.z:     rts
.m:     lda #$FFFF
        rts

; CP steps by SDX, SDY if the cell is ground that can be walked, free, not the Avatar's. Carry set if it did.
TryCharStep:
        bra .go
.nj:    brl .no
.go:
        lda SDX
        ora SDY
        beq .nj
        lda [CP]
        clc
        adc SDX
        sta SNX
        ldy #2
        lda [CP],y
        clc
        adc SDY
        sta SNY
        lda SNX
        cmp PX
        bne .nav
        lda SNY
        cmp PY
        beq .nj
.nav:   ldx SNX
        ldy SNY
        jsr MapTile
        cmp #$FFFF
        beq .nj
        tay
        lda [FlagsPtr],y
        and #$0001
        beq .nj
        ldx SNX
        ldy SNY
        jsr OccAt
        bne .nj
        lda [CP]                ; off its cell
        tax
        ldy #2
        lda [CP],y
        tay
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        and #$FD
        sta [Ptr]
        rep #$20
        .a16
        lda SNX                 ; onto the new one
        sta [CP]
        ldy #2
        lda SNY
        sta [CP],y
        ldx SNX
        ldy SNY
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        ora #2
        sta [Ptr]
        rep #$20
        .a16
        lda SDX                 ; facing the way it went: 0 south, 1 north, 2 east, 3 west
        beq .vert
        bmi .w
        lda #2
        bra .f
.w:     lda #3
        bra .f
.vert:  lda SDY
        bmi .nth
        lda #0
        bra .f
.nth:   lda #1
.f:     ldy #8
        sta [CP],y
        sec
        rts
.no:    clc
        rts

; ---- a blow ----

; AtkP strikes DefP: told, the die, the damage, the wound told, death.
Strike:
        jsr Shoot               ; from afar: ammo, the missile's flight (missile.s); carry set: can't
        bcc .go
        rts
.go:    jsr AtkName
        jsr DefName
        lda #W_ATTACKS          ; {1} attacks {0}.
        jsr Tell
        lda #30                 ; Actor::roll_to_win
        jsr Random
        cmp #0
        beq .miss
        cmp #29
        beq .hit
        clc
        ldy #C_COMBAT
        adc [AtkP],y
        sec
        sbc [DefP],y
        sta RollV
        jsr BlowAils            ; protected: +3 defence; cursed: -3 (ailments.s)
        lda RollV
        sec
        sbc #14
        bpl .hit
.miss:  lda #W_MISSED
        jmp Tell
.hit:   stz Dmg                 ; 1..strength/3, 1..weapon (bare hands 1), less 1..armour
        ldy #C_STR
        lda [AtkP],y
        ldx #0
.d3:    cmp #3
        bcc .s
        sbc #3
        inx
        bra .d3
.s:     txa
        beq .w
        jsr Random
        inc a
        sta Dmg
.w:     ldy #C_WEAPON
        lda [AtkP],y
        bne .wp
        lda #1
.wp:    jsr Random
        inc a
        clc
        adc Dmg
        sta Dmg
        ldy #C_ARMOUR
        lda [DefP],y
        beq .arm
        jsr Random
        inc a
        sta T4
        lda Dmg
        sec
        sbc T4
        bpl .pos
        lda #0
.pos:   sta Dmg
.arm:   jsr FightBack
        lda Dmg
        bne .hurt
        rts                     ; no damage: nothing more told (as Windows)
.hurt:  ldy #C_HP
        lda [DefP],y
        sec
        sbc Dmg
        bpl .hp
        lda #0
.hp:    sta [DefP],y
        jsr TellWound
        ldy #C_HP
        lda [DefP],y
        bne .alive
        jmp Dies
.alive: jmp HpChanged

; The one struck turns on whoever struck it (the Avatar: it is hostile now).
FightBack:
        jsr DefIsAvatar
        bcs .out
        ldy #C_FLAGS            ; awake now, and hostile
        lda [DefP],y
        and #$FFEF
        ora #2
        sta [DefP],y
.out:   rts

; How badly DefP is hurt, by what is left: killed, critically, heavily, lightly, barely.
TellWound:
        ldy #C_HP
        lda [DefP],y
        bne .some
        lda #W_KILLED
        jmp Tell
.some:  asl a
        asl a
        sta T4                  ; 4 x hp
        ldy #C_MAXHP
        lda [DefP],y
        sta T5                  ; most
        asl a
        clc
        adc T5                  ; 3 x most
        cmp T4
        beq .barely
        bcc .barely
        lda T5
        asl a                   ; 2 x most
        cmp T4
        beq .lightly
        bcc .lightly
        lda T5
        cmp T4
        beq .heavily
        bcc .heavily
        lda #W_BARELY+3         ; Critical
        jmp Tell
.barely: lda #W_BARELY
        jmp Tell
.lightly: lda #W_BARELY+1
        jmp Tell
.heavily: lda #W_BARELY+2
        jmp Tell

; DefP is dead: off the map (its cell free); the Avatar who killed it gets its experience. The Avatar dead: Press a key.
Dies:
        jsr Withstands          ; withstand death: healed instead (spells2.s)
        bcc .dies
        rts
.dies:  jsr DefIsAvatar
        bcc .npc
        lda #1
        sta AvDead
        lda #W_PRESSKEY
        jsr PrintSaid
        lda #7
        sta Mode
        rts
.npc:   ldy #C_FLAGS
        lda [DefP],y
        ora #4
        sta [DefP],y
        lda [DefP]
        tax
        ldy #2
        lda [DefP],y
        tay
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        and #$F9                ; neither a character's nor a member's
        sta [Ptr]
        rep #$20
        .a16
        lda #1
        sta Moved
        ldy #C_EXP              ; Exult: the killer gets the victim's experience
        lda [DefP],y
        clc
        adc [AtkP],y
        bcc .e
        lda #$FFFF
.e:     sta [AtkP],y
        lda AtkP
        cmp #AvRec
        bne .out
        jsr CheckLevel
.out:   jmp DrawView

; Carry set if DefP is the Avatar.
DefIsAvatar:
        lda DefP
        cmp #AvRec
        bne .no
        lda DefP+2
        cmp MyBank
        bne .no
        sec
        rts
.no:    clc
        rts

; FmtName = the name of the one at DefP; WhoName = the one at AtkP.
DefName:
        jsr DefIsAvatar
        bcc .c
        ldx AvatarW
        lda WordTab,x
        sta FmtName
        lda WordTab+2,x
        sta FmtName+2
        rts
.c:     ldy #C_NAME
        lda [DefP],y
        jmp NameNumber
AtkName:
        lda AtkP
        cmp #AvRec
        bne .c
        ldx AvatarW
        lda WordTab,x
        sta WhoName
        lda WordTab+2,x
        sta WhoName+2
        rts
.c:     ldy #C_NAME
        lda [AtkP],y
        jsr NameNumber
        lda FmtName
        sta WhoName
        lda FmtName+2
        sta WhoName+2
        rts

; A = a word, told on a line of its own ({0} FmtName, {1} WhoName); the first of a turn starts a new line if asked.
Tell:
        pha
        lda WTLead
        beq .go
        stz WTLead
        jsr NewLine
.go:    lda #1
        sta Told
        sta UseWho
        pla
        jsr Format
        stz UseWho
        jmp NewLine


AvRec:    .ds 64
AvDead:   .dw 0
CombatOn: .dw 0
TurnUsed: .dw 0
Told:     .dw 0
WTLead:   .dw 0
UseWho:   .dw 0
WhoName:  .dd 0
ChrRec:   .dd 0
Moved:    .dw 0
TurnN:    .dw 0
LastTick: .dw 0
Seed:     .dw 1
RndN:     .dw 0
Dmg:      .dw 0
RollV:    .dw 0
GX:       .dw 0
GY:       .dw 0
Radius:   .dw 0
SDX:      .dw 0
SDY:      .dw 0
TDX:      .dw 0
TDY:      .dw 0
SNX:      .dw 0
SNY:      .dw 0
LvlOn:    .dw 0
LvlHits:  .dw 30
LvlMost:  .dw 240
LvlCount: .dw 0
LvlNeeds: .ds 32
DirX:     .dw 0,0,1,$FFFF
DirY:     .dw 1,$FFFF,0,0
