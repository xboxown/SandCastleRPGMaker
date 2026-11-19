; Sandcastle RPG Maker - Apple IIgs player: more event and talk commands, as the Windows Ultima 6 engine's (Engine/
; AppleIIgsTalk.cs has their bytes). Whom a command is about: the Avatar, a party member, or a character of a map.

; The op in A (12-24), Y on it.
MoreOps:
        sec
        sbc #12
        asl a
        tax
        jmp (OpJump,x)

OpJump:   .dw OpGive,OpTake,OpHp,OpStat,OpWait,OpPlace,OpKill,OpSched,OpTime,OpDoor,OpPic,OpErase,OpGame,OpPay,OpSell,OpMore,OpAil,OpCalc,OpIfx,OpHaggle,OpPayV,OpMusic,OpVolume,OpResume,OpWeather

; TPC += A, on with the code.
Next:
        clc
        adc TPC
        sta TPC
        jmp RunTalk

; Y on the op: JMap, JIdx from the 4 bytes after it.
ReadWho:
        iny
        lda [TalkPtr],y
        sta JMap
        iny
        iny
        lda [TalkPtr],y
        sta JIdx
        iny
        iny
        rts

; WP = whom JMap, JIdx names: the Avatar, a member, a map's character (that map loaded). Carry set: not to be had.
WhoRec:
        lda JMap
        cmp #$FFFF
        bne .c
        lda PX
        sta AvRec
        lda PY
        sta AvRec+2
        lda #AvRec
        sta WP
        lda MyBank
        sta WP+2
        clc
        rts
.c:     jsr FindMember
        bcc .map
        lda MP
        sta WP
        lda MP+2
        sta WP+2
        clc
        rts
.map:   jsr JoinRec
        bcs .no
        lda Src
        sta WP
        lda Src+2
        sta WP+2
        clc
.no:    rts

; Carry set if WP is the Avatar.
WPIsAvatar:
        lda WP
        cmp #AvRec
        bne .no
        lda WP+2
        cmp MyBank
        bne .no
        sec
        rts
.no:    clc
        rts

; GIVE name sprite icon flags: a spare object (gone, no name) becomes the thing, carried.
OpGive:
        iny
        lda [TalkPtr],y
        sta GName
        iny
        iny
        lda [TalkPtr],y
        sta GSprite
        iny
        iny
        lda [TalkPtr],y
        sta GIcon
        iny
        iny
        lda [TalkPtr],y
        sta GFlags
        iny
        iny
        lda [TalkPtr],y
        sta GOther
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T4
        beq .done
.l:     ldy #6
        lda [ItemPtr],y
        and #32
        beq .n
        ldy #8
        lda [ItemPtr],y
        cmp #$FFFF
        bne .n
        lda #$FFFF              ; here: carried
        sta [ItemPtr]
        ldy #2
        sta [ItemPtr],y
        ldy #4
        lda GSprite
        sta [ItemPtr],y
        ldy #6
        lda GFlags
        ora #64
        sta [ItemPtr],y
        ldy #8
        lda GName
        sta [ItemPtr],y
        ldy #10
        lda GOther
        sta [ItemPtr],y
        ldy #12
        lda GIcon
        sta [ItemPtr],y
        ldy #14
        lda #$FFFF
        sta [ItemPtr],y
        jsr RefreshInventory
        jsr Having
        bra .done
.n:     lda #16
        jsr NextItem
        dec T4
        bne .l
.done:  lda #11
        jmp Next

; TAKE name count: that many of the Avatar's things of that name are gone.
OpTake:
        iny
        lda [TalkPtr],y
        sta EvName
        iny
        iny
        lda [TalkPtr],y
        sta GCount
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T4
        beq .done
.l:     lda GCount
        beq .done
        ldy #8
        lda [ItemPtr],y
        cmp EvName
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #96+256
        cmp #64
        bne .n
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        dec GCount
.n:     lda #16
        jsr NextItem
        dec T4
        bne .l
.done:  jsr RefreshInventory
        jsr Having
        lda #5
        jmp Next

; HP who op amount cankill
OpHp:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta GOp
        iny
        lda [TalkPtr],y
        sta GVal
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta GKill
        jsr WhoRec
        bcs .skip
        ldy #C_HP
        lda GOp
        jsr Apply               ; A = the new value (signed)
        bmi .low
        cmp #1
        bcs .clamp
.low:   lda GKill
        bne .dead
        lda #1
.clamp: ldy #C_MAXHP
        cmp [WP],y
        bcc .set
        lda [WP],y
.set:   ldy #C_HP
        sta [WP],y
        jsr HpChanged
.skip:  lda #9
        jmp Next
.dead:  lda #0
        ldy #C_HP
        sta [WP],y
        jmp KillWP

; A = the new value of WP's word at Y by GOp (0 add, 1 subtract, 2 set) and GVal.
Apply:
        cmp #2
        beq .set
        cmp #1
        beq .sub
        lda [WP],y
        clc
        adc GVal
        rts
.sub:   lda [WP],y
        sec
        sbc GVal
        rts
.set:   lda GVal
        rts

; STAT who word op value
OpStat:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta GWord
        iny
        lda [TalkPtr],y
        and #$00FF
        sta GOp
        iny
        lda [TalkPtr],y
        sta GVal
        jsr WhoRec
        bcs .skip
        lda GWord
        asl a
        clc
        adc #16
        tay
        lda GOp
        jsr Apply
        bpl .ok
        lda #0
.ok:    sta [WP],y
        lda GWord
        cmp #10                 ; experience: a level?
        bne .h
        jsr WPIsAvatar
        bcc .h
        jsr CheckLevel
.h:     jsr HpChanged
.skip:  lda #9
        jmp Next

; WAIT ticks
OpWait:
        iny
        lda [TalkPtr],y
        sta GVal
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta GTick
        pla
.w:     pha
        pha
        tool $2503
        pla
        sta T4
        pla
        lda T4
        sec
        sbc GTick
        cmp GVal
        bcc .w
        lda #3
        jmp Next

; PLACE who place x y: a map's character put at x, y or taken off its map.
OpPlace:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta GOp
        iny
        lda [TalkPtr],y
        sta GX
        iny
        iny
        lda [TalkPtr],y
        sta GY
        jsr FindMember          ; a member: not this way
        bcs .skip
        jsr WhoRec
        bcs .skip
        jsr WPIsAvatar
        bcs .skip
        jsr WPOffCell           ; off its cell (this map)
        lda GOp
        bne .put
        ldy #C_FLAGS            ; removed: gone
        lda [WP],y
        ora #4+8
        sta [WP],y
        bra .drawn
.put:   ldy #C_FLAGS
        lda [WP],y
        and #$FFF3
        sta [WP],y
        lda GX
        sta [WP]
        ldy #2
        lda GY
        sta [WP],y
        lda JMap
        cmp CurMap
        bne .drawn
        ldx GX
        ldy GY
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        ora #2
        sta [Ptr]
        rep #$20
        .a16
.drawn: jsr DrawView
.skip:  lda #10
        jmp Next

; WP (a map's character, on this map and there) off its cell.
WPOffCell:
        lda JMap
        cmp CurMap
        bne .out
        ldy #C_FLAGS
        lda [WP],y
        and #4
        bne .out
        lda [WP]
        tax
        ldy #2
        lda [WP],y
        tay
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        and #$FD
        sta [Ptr]
        rep #$20
        .a16
.out:   rts

; KILL who
OpKill:
        jsr ReadWho
        jsr WhoRec
        bcs .skip
        jmp KillWP
.skip:  lda #5
        jmp Next

; WP dies (the Avatar: Press a key...; the talk or event is over).
KillWP:
        jsr WPIsAvatar
        bcc .other
        lda #0
        sta AvRec+C_HP
        lda #1
        sta AvDead
        jsr NewLine
        lda #W_PRESSKEY
        jsr PrintSaid
        stz EvRunning
        stz QN
        lda #7
        sta Mode
        rts
.other: jsr WPOffCell
        ldy #C_FLAGS
        lda [WP],y
        ora #4
        sta [WP],y
        jsr PruneParty
        jsr DrawView
        ldy TPC
        lda [TalkPtr],y
        and #$00FF
        cmp #14
        beq .hp
        lda #5
        jmp Next
.hp:    lda #9
        jmp Next

; SCHED who activity ($FF: back to its schedule)
OpSched:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta GOp
        jsr WhoRec
        bcs .skip
        jsr WPIsAvatar
        bcs .skip
        lda GOp
        cmp #$FF
        bne .act
        jsr ApplySchedules
        bra .skip
.act:   ldy #S_ACT
        sta [WP],y
        ldy #S_TX               ; right where it is
        lda [WP]
        sta [WP],y
        ldy #2
        lda [WP],y
        ldy #S_TY
        sta [WP],y
        ldy #S_KEY
        lda #0
        sta [WP],y
        ldy #C_BEHAV
        lda #2
        sta [WP],y
.skip:  lda #6
        jmp Next

; TIME mode hour minute
OpTime:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta GOp
        iny
        lda [TalkPtr],y
        sta GVal
        iny
        iny
        lda [TalkPtr],y
        sta GWord
        lda GOp
        bne .adv
        lda GVal
        cmp #24
        bcc .h
        lda #0
.h:     sta Hour
        lda GWord
        sta Minute
        bra .done
.adv:   cmp #2
        beq .hours
.min:   lda GVal                ; minutes
        beq .done
        dec GVal
        jsr AddMinute
        bra .min
.hours: lda GVal
        beq .done
        dec GVal
        lda #60
        sta GWord
.hm:    jsr AddMinute
        dec GWord
        bne .hm
        bra .hours
.done:  jsr ApplySchedules
        lda #6
        jmp Next

AddMinute:
        inc Minute
        lda Minute
        cmp #60
        bcc .out
        stz Minute
        inc Hour
        lda Hour
        cmp #24
        bcc .out
        stz Hour
        inc Day
.out:   rts

; DOOR x y value (0 locked, 1 unlocked, 2 closed, 3 open): the door there on this map.
OpDoor:
        iny
        lda [TalkPtr],y
        sta GX
        iny
        iny
        lda [TalkPtr],y
        sta GY
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta GOp
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T4
        bne .l
        brl .done
.l:     ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .n
        lda [ItemPtr],y
        and #4
        beq .n
        lda [ItemPtr]
        cmp GX
        bne .n
        ldy #2
        lda [ItemPtr],y
        cmp GY
        beq .door
.n:     lda #16
        jsr NextItem
        dec T4
        bne .l
        brl .done
.door:  ldy #6
        lda GOp
        bne .g1
        lda [ItemPtr],y         ; locked: shut first
        and #16
        beq .lk
        jsr ToggleDoor
.lk:    ldy #6
        lda [ItemPtr],y
        ora #8
        sta [ItemPtr],y
        bra .drawn
.g1:    cmp #1
        bne .g2
        lda [ItemPtr],y
        and #$FFF7
        sta [ItemPtr],y
        bra .drawn
.g2:    cmp #2
        bne .g3
        lda [ItemPtr],y
        and #16
        beq .drawn
        jsr ToggleDoor
        bra .drawn
.g3:    lda [ItemPtr],y         ; open (and unlocked)
        and #$FFF7
        sta [ItemPtr],y
        and #16
        bne .drawn
        jsr ToggleDoor
.drawn: jsr DrawView
.done:  lda #6
        jmp Next

; The door at ItemPtr open <-> shut: its looks swap, its cell stops walkers only shut.
ToggleDoor:
        ldy #6
        lda [ItemPtr],y
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
        lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        jsr OccAt
        ldy #6
        lda [ItemPtr],y
        and #1
        sep #$20
        .a8
        sta T4
        lda [Ptr]
        and #$FE
        ora T4
        sta [Ptr]
        rep #$20
        .a16
        rts

; PIC sprite x y: drawn over the game.
OpPic:
        iny
        lda [TalkPtr],y
        sta SprId
        iny
        iny
        lda [TalkPtr],y
        sta CellX
        iny
        iny
        lda [TalkPtr],y
        sta CellY
        jsr DrawPic
        lda #7
        jmp Next

; ERASE: the game drawn again.
OpErase:
        jsr Redraw
        lda #1
        jmp Next

; GAME 0 over / 1 quit / 2 save / 3 load
OpGame:
        iny
        lda [TalkPtr],y
        and #$00FF
        bne .g1
        lda #AvRec              ; game over: as the Avatar's death
        sta WP
        lda MyBank
        sta WP+2
        jmp KillWP
.g1:    cmp #1
        bne .g2
        jmp Quit
.g2:    cmp #2
        bne .g3
        jsr SaveGame
        lda #2
        jmp Next
.g3:    stz EvRunning           ; load: the talk or event is over
        stz QN
        stz Mode
        jmp LoadGame

GName:    .dw 0
GSprite:  .dw 0
GIcon:    .dw 0
GFlags:   .dw 0
GOther:   .dw 0
GCount:   .dw 0
GOp:      .dw 0
GVal:     .dw 0
GKill:    .dw 0
GWord:    .dw 0
GTick:    .dw 0
