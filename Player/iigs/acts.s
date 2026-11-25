; Sandcastle RPG Maker - Apple IIgs player: what an event has a character do, as the Windows Ultima 6 engine's action
; chains (U6GameWindow.Actions.cs - Exult's set_action_sequence): Walk To Location, Turn To Face, Pickup Item, Drop Item,
; Open Door, Sit, Stand, Attack (the Avatar), Change Pose (Sleeping). On the IIgs an event's commands run one after the
; other, so each is carried out on the spot, the view drawn as it goes: a walk steps till it is there (or blocked three
; times), a pickup or a door walks up to it first.
; Talk op 45 ACT: who (map, place: 4 bytes), what (1), a, b, c (words) - 12 bytes.

ACT_STEP_TICKS = 6

OpAct:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta AcWhat
        iny
        lda [TalkPtr],y
        sta AcA
        iny
        iny
        lda [TalkPtr],y
        sta AcB
        iny
        iny
        lda [TalkPtr],y
        sta AcC
        jsr WhoRec              ; WP = whom it is about
        bcs .done
        lda AcWhat
        asl a
        tax
        jsr (AcJump,x)
.done:  jsr DrawView
        lda #12
        jmp Next

AcJump:   .dw AcWalk,AcFace,AcPickup,AcDrop,AcDoor,AcSit,AcStand,AcAttack,AcSleep

; ---- walking ----

; Walk To Location: to AcA, AcB.
AcWalk:
        stz AcNear
        bra AcGo
; Up to AcA, AcB (next to it).
AcBeside:
        lda #1
        sta AcNear
AcGo:   lda AcA
        sta GX
        lda AcB
        sta GY
        stz AcStuck
        lda #80
        sta AcLeft
.l:     jsr AcWhere             ; there (or beside it)?
        lda AcX
        sec
        sbc GX
        jsr AbsX
        sta T4
        lda AcY
        sec
        sbc GY
        jsr AbsY
        cmp T4
        bcs .m
        lda T4
.m:     cmp AcNear
        beq .o
        bcc .o
        dec AcLeft
        beq .o
        jsr AcStep
        bcs .moved
        inc AcStuck
        lda AcStuck
        cmp #3
        bcs .o
        bra .l
.moved: stz AcStuck
        jsr DrawView
        lda #ACT_STEP_TICKS
        jsr DoorWait            ; (door.s: waits so many ticks)
        bra .l
.o:     rts

; AcX, AcY: where WP is (the Avatar: PX, PY).
AcWhere:
        jsr WPIsAvatar
        bcc .r
        lda PX
        sta AcX
        lda PY
        sta AcY
        rts
.r:     lda [WP]
        sta AcX
        ldy #2
        lda [WP],y
        sta AcY
        rts

; One step of WP toward GX, GY - the Avatar, a member or anyone else, each by its own rules. Carry set if it moved.
AcStep:
        lda GX
        sec
        sbc AcX
        jsr ShortX
        jsr Sign
        sta SDX
        lda GY
        sec
        sbc AcY
        jsr ShortY
        jsr Sign
        sta SDY
        jsr WPIsAvatar
        bcs .av
        jsr WPInParty           ; (levels.s)
        bcc .chr
        lda WP                  ; a member (MemberStep - solo.s)
        sta MP
        lda WP+2
        sta MP+2
        jmp MemberStep
.chr:   lda WP                  ; anyone else (StepToGoal - combat.s)
        sta CP
        lda WP+2
        sta CP+2
        jmp StepToGoal
.av:    jsr .try                ; the Avatar: straight, else one axis
        bcs .ok
        lda SDY
        pha
        stz SDY
        jsr .try
        pla
        bcs .ok
        sta SDY
        stz SDX
        jsr .try
        bcc .no
.ok:    sec
        rts
.no:    clc
        rts
.try:   lda SDX
        ora SDY
        beq .f
        lda PX
        clc
        adc SDX
        tax
        lda PY
        clc
        adc SDY
        tay
        jsr WrapXY
        stx AcNX
        sty AcNY
        jsr MapTile
        cmp #$FFFF
        beq .f
        tay
        lda [FlagsPtr],y
        and #1
        beq .f
        ldx AcNX
        ldy AcNY
        jsr OccAt
        and #3
        bne .f
        lda PX                  ; there; the party follows
        sta OldX
        lda PY
        sta OldY
        lda AcNX
        sta PX
        lda AcNY
        sta PY
        jsr FollowParty
        sec
        rts
.f:     clc
        rts

; ---- the rest ----

; Turn To Face: AcA 0 south, 1 north, 2 east, 3 west; $FFFF towards the Avatar.
AcFace:
        lda AcA
        cmp #$FFFF
        bne .set
        jsr AcWhere
        lda PX
        sec
        sbc AcX
        jsr ShortX
        sta T4
        lda PY
        sec
        sbc AcY
        jsr ShortY
        sta T5
        lda T4                  ; the larger way
        jsr Abs
        sta AcX
        lda T5
        jsr Abs
        cmp AcX
        bcs .ud
        lda T4
        bmi .w
        lda #2
        bra .set
.w:     lda #3
        bra .set
.ud:    lda T5
        bmi .n
        lda #0
        bra .set
.n:     lda #1
.set:   and #3
        sta AcA
        jsr WPIsAvatar
        bcc .rec
        lda AcA
        sta Facing
        rts
.rec:   ldy #8
        lda AcA
        sta [WP],y
        rts

; WP's owner code (the Avatar $FFFF, a member its slot's, anyone else its map's).
AcCode:
        jsr WPIsAvatar
        bcc .m
        lda #$FFFF
        rts
.m:     lda DefP
        pha
        lda DefP+2
        pha
        lda WP
        sta DefP
        lda WP+2
        sta DefP+2
        jsr DefCode             ; (bodies.s)
        tax
        pla
        sta DefP+2
        pla
        sta DefP
        txa
        rts

; Pickup Item: walks up to AcA, AcB and takes the thing on top there that can be taken.
AcPickup:
        jsr AcBeside
        jsr AcCode
        sta AcCodeV
        stz AcFound
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta AcLeft
.l:     lda AcLeft
        beq .d
        ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .n
        lda [ItemPtr]
        cmp AcA
        bne .n
        ldy #2
        lda [ItemPtr],y
        cmp AcB
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #2
        beq .n
        lda ItemPtr             ; the last one there: on top
        sta AcP
        lda ItemPtr+2
        sta AcP+2
        lda #1
        sta AcFound
.n:     lda #16
        jsr NextItem
        dec AcLeft
        bra .l
.d:     lda AcFound
        beq .o
        lda AcP
        sta ItemPtr
        lda AcP+2
        sta ItemPtr+2
        ldy #6                  ; solid: its cell free now
        lda [ItemPtr],y
        and #1
        beq .t
        jsr FreeTrapCell        ; (floor.s)
.t:     ldy #6                  ; carried, its owner's
        lda [ItemPtr],y
        and #$0FFF
        ora #64
        sta [ItemPtr],y
        ldy #14
        lda AcCodeV
        sta [ItemPtr],y
        jsr RefreshInventory
.o:     rts

; Drop Item: the first thing named AcA that WP carries is put down where WP stands.
AcDrop:
        jsr AcCode
        sta AcCodeV
        jsr AcWhere
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta AcLeft
.l:     lda AcLeft
        beq .o
        ldy #6
        lda [ItemPtr],y
        and #96+256
        cmp #64
        bne .n
        ldy #14
        lda [ItemPtr],y
        cmp AcCodeV
        bne .n
        ldy #8
        lda [ItemPtr],y
        cmp AcA
        bne .n
        lda AcX                 ; down at its feet, on this map
        sta [ItemPtr]
        ldy #2
        lda AcY
        sta [ItemPtr],y
        ldy #6
        lda [ItemPtr],y
        and #$0FFF-64
        ora CurBits
        sta [ItemPtr],y
        jmp RefreshInventory
.n:     lda #16
        jsr NextItem
        dec AcLeft
        bra .l
.o:     rts

; Open Door: walks up to AcA, AcB and opens the door there (shut and not locked).
AcDoor:
        jsr AcBeside
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta AcLeft
.l:     lda AcLeft
        beq .o
        ldy #6
        lda [ItemPtr],y
        and #$F000+96+256+4+8+16
        sec
        sbc CurBits
        cmp #4                  ; a door here, shut, not locked
        bne .n
        lda [ItemPtr]
        cmp AcA
        bne .n
        ldy #2
        lda [ItemPtr],y
        cmp AcB
        bne .n
        jsr SwapDoor            ; (lever.s)
        jmp DoorMoved           ; (door.s)
.n:     lda #16
        jsr NextItem
        dec AcLeft
        bra .l
.o:     rts

; Sit: walks to AcA, AcB ($FFFF: where it is) and sits there (drawn sitting - a character's schedule word).
AcSit:
        lda AcA
        cmp #$FFFF
        beq .here
        jsr AcWalk
.here:  jsr WPIsAvatar
        bcs .o
        jsr WPInParty
        bcs .o
        jsr AcWhere
        ldy #50
        lda [WP],y
        ora #$0080
        sta [WP],y
        ldy #52
        lda AcX
        sta [WP],y
        ldy #54
        lda AcY
        sta [WP],y
.o:     rts

; Stand: up again.
AcStand:
        jsr WPIsAvatar
        bcs .o
        jsr WPInParty
        bcs .o
        ldy #50
        lda [WP],y
        and #$FF7F
        sta [WP],y
        ldy #C_FLAGS            ; (and awake)
        lda [WP],y
        and #$FFEF
        sta [WP],y
.o:     rts

; Attack: it turns on the Avatar (hostile now).
AcAttack:
        jsr WPIsAvatar
        bcs .o
        jsr WPInParty
        bcs .o
        ldy #C_FLAGS
        lda [WP],y
        ora #2
        and #$FFEF
        sta [WP],y
.o:     rts

; Change Pose Sleeping (AcA 1) / any other pose (0): asleep or awake.
AcSleep:
        jsr WPIsAvatar
        bcs .o
        ldy #C_FLAGS
        lda [WP],y
        and #$FFEF
        ldx AcA
        beq .s
        ora #16
.s:     sta [WP],y
.o:     rts

AcWhat:   .dw 0
AcA:      .dw 0
AcB:      .dw 0
AcC:      .dw 0
AcNear:   .dw 0
AcStuck:  .dw 0
AcLeft:   .dw 0
AcX:      .dw 0
AcY:      .dw 0
AcNX:     .dw 0
AcNY:     .dw 0
AcFound:  .dw 0
AcCodeV:  .dw 0
AcP:      .dd 0
