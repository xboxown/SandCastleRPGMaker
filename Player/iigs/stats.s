; Sandcastle RPG Maker - Apple IIgs player: stats that change, as the Windows Ultima 6 engine (U6GameWindow.U6Levels.cs,
; Vitals.cs). The portrait view's stats show the Avatar's numbers as they are now (the editor marks a built-in stat's
; value "~" and a letter: A hit points, C strength... Z the next level's experience). Experience over the Leveling
; Editor's step for its level: a level (hit points a level more, up to the most), "Avatar is now level 2!".
; R Rest: not in combat mode, not with foes near; "How many hours?" 0-9: the time goes on, hit points and mana back
; (an eighth of the most an hour).

W_NEWLEVEL = 40
W_REST     = 41
W_NOTCOMBAT = 42
W_FOESNEAR = 43
W_HOURS    = 44
W_OTHERSNEAR = 72
W_WHOGUARDS = 73
W_NOONE    = 74
W_MEALTIME = 75
W_HASFOOD  = 76
W_NOFOOD   = 77
W_STANDS   = 78

; The panel's stats again if they show (hit points, experience, a level changed).
HpChanged:
        lda PanW
        beq .out
        lda PanelView
        beq .out
        jsr DrawPanel
.out:   rts

; The portrait view's stats: each line written with its live value.
DrawLiveStats:
        jsr ViewPtr             ; whose (party.s)
        lda StatPtr
        sta ItemPtr
        lda StatPtr+2
        sta ItemPtr+2
        lda StatCount
        sta Item
.l:     lda Item
        bne .one
        rts
.one:   ldy #4                  ; its colour
        lda [ItemPtr],y
        cmp #$FFFF
        bne .col
        lda Bright
.col:   pha
        tool $A004              ; SetForeColor
        lda [ItemPtr]
        pha
        ldy #2
        lda [ItemPtr],y
        clc
        adc #8
        pha
        tool $3A04              ; MoveTo
        ldy #7                  ; the words into StatBuf, the values put in
        ldx #0
.c:     lda [ItemPtr],y
        and #$00FF
        beq .end
        cmp #'~'
        bne .put
        iny
        lda [ItemPtr],y
        and #$00FF
        iny
        phy
        sec
        sbc #'A'
        phx
        phy
        jsr LiveValue
        ply
        plx
        jsr Decimal             ; into StatBuf at X
        ply
        bra .c
.put:   sep #$20
        .a8
        sta StatBuf,x
        rep #$20
        .a16
        inx
        iny
        cpx #60
        bcc .c
.end:   sep #$20
        .a8
        lda #0
        sta StatBuf,x
        rep #$20
        .a16
        pea #bank(StatBuf)
        pea #StatBuf
        tool $A604              ; DrawCString
        ldy #6
        lda [ItemPtr],y
        and #$00FF
        clc
        adc #8
        jsr NextItem
        dec Item
        brl .l

; A = a stat's letter number (0 hit points ... 25 the next level's experience) -> A = the Avatar's value now.
LiveValue:
        cmp #25
        beq .next
        cmp #24
        bcs .z
        asl a
        clc
        adc #16
        tay
        lda [ViewP],y
        rts
.z:     lda #0
        rts
.next:  lda LvlOn
        beq .z
        ldy #C_LEVEL
        lda [ViewP],y
        beq .z
        cmp LvlCount
        beq .n
        bcs .z
.n:     dec a
        asl a
        tax
        lda LvlNeeds,x
        inc a
        rts

; A (unsigned) written in figures into StatBuf from X on; X after them.
Decimal:
        sta DecN
        stz DecGo
        ldy #0
.p:     lda DecPow,y
        sta DecP
        lda #0
        sta DecD
.s:     lda DecN
        cmp DecP
        bcc .w
        sbc DecP
        sta DecN
        inc DecD
        bra .s
.w:     lda DecD
        ora DecGo
        bne .digit
        cpy #8                  ; the last figure always
        bne .skip
.digit: sta DecGo
        lda DecD
        clc
        adc #'0'
        sep #$20
        .a8
        sta StatBuf,x
        rep #$20
        .a16
        inx
.skip:  iny
        iny
        cpy #10
        bcc .p
        rts

; The Avatar's experience: levels earned (Ultima 6: more than the step for its level).
CheckLevel:
        lda #AvRec              ; the Avatar's experience: as levels.s's ExpGained
        sta WP
        lda MyBank
        sta WP+2
        jmp ExpGained
OldCheckLevel:
        lda LvlOn
        bne .again
        jmp HpChanged
.again: lda AvRec+C_LEVEL
        cmp LvlCount
        beq .ok
        bcs .done
.ok:    dec a
        asl a
        tax
        lda AvRec+C_EXP
        cmp LvlNeeds,x
        beq .done
        bcc .done
        inc AvRec+C_LEVEL       ; a level: the most hit points it allows, that many hit points back
        lda AvRec+C_LEVEL
        sta MulA
        lda LvlHits
        sta MB0
        jsr Mul16
        lda MulR
        cmp LvlMost
        bcc .m
        lda LvlMost
.m:     sta AvRec+C_MAXHP
        lda AvRec+C_HP
        clc
        adc LvlHits
        cmp AvRec+C_MAXHP
        bcc .h
        lda AvRec+C_MAXHP
.h:     sta AvRec+C_HP
        ldx #0                  ; "Avatar is now level 2!"
        lda AvRec+C_LEVEL
        jsr Decimal
        sep #$20
        .a8
        lda #0
        sta StatBuf,x
        rep #$20
        .a16
        lda #StatBuf
        sta FmtName
        lda MyBank
        sta FmtName+2
        lda #W_NEWLEVEL
        jsr Tell
        bra .again
.done:  jmp HpChanged

; R: Rest.
StartRest:
        lda #W_REST
        jsr PrintSaid
        jsr NewLine
        lda CombatOn
        beq .nc
        lda #W_NOTCOMBAT
        jsr PrintSaid
        jmp PromptLine
.nc:    jsr FoesNear
        bcc .others
        lda #W_FOESNEAR
        jsr PrintSaid
        jmp PromptLine
.others: jsr OthersNear         ; someone not in the party within 5
        bcc .ask
        lda #W_OTHERSNEAR
        jsr PrintSaid
        jmp PromptLine
.ask:   lda #W_HOURS
        jsr PrintSaid
        lda #8
        sta Mode
        rts

; Carry set if a hostile character is within 8 of the Avatar.
FoesNear:
        lda ChrPtr
        sta CP
        lda ChrPtr+2
        sta CP+2
        lda ChrCount
        sta TurnN
.l:     lda TurnN
        beq .no
        ldy #C_FLAGS
        lda [CP],y
        and #2+4
        cmp #2
        bne .n
        jsr DistCP
        cmp #9
        bcc .yes
.n:     jsr NextCP
        dec TurnN
        bra .l
.no:    clc
        rts
.yes:   sec
        rts

; Carry set if anyone living (not in the party) is within 5 of the Avatar.
OthersNear:
        lda ChrPtr
        sta CP
        lda ChrPtr+2
        sta CP+2
        lda ChrCount
        sta TurnN
.l:     lda TurnN
        beq .no
        ldy #C_FLAGS
        lda [CP],y
        and #4+8
        bne .n
        jsr DistCP
        cmp #6
        bcc .yes
.n:     jsr NextCP
        dec TurnN
        bra .l
.no:    clc
        rts
.yes:   sec
        rts

; Rest's hours: a figure 0-9 (Esc: none). With a party: who guards?
RestKey:
        cmp #$1B
        beq .stop
        cmp #'0'
        bcc .out
        cmp #'9'+1
        bcs .out
        pha
        sta RestCh
        lda #RestCh
        jsr PrintMine           ; the figure typed
        pla
        sec
        sbc #'0'
        sta RestH
        beq .stop
        lda #$FFFF
        sta RGuard
        lda PartyN
        beq .alone
        jsr NewLine
        lda #W_WHOGUARDS
        jsr PrintSaid
        lda #15
        sta Mode
        rts
.alone: lda #W_NOONE            ; no guard: {RestGuard} none
        asl a
        asl a
        tax
        lda WordTab,x
        sta RGName
        lda WordTab+2,x
        sta RGName+2
        jmp DoRest
.stop:  stz Mode
        jmp PromptLine
.out:   rts

; Who guards: 0 no one, 1 the Avatar, 2... the members.
GuardKey:
        cmp #$1B
        beq .none
        cmp #'0'
        bcc .out
        sta RestCh
        sec
        sbc #'0'
        sta T4
        lda PartyN
        inc a
        cmp T4
        bcc .out                ; more than there are
        lda #RestCh
        jsr PrintMine
        lda T4
        beq .none
        dec a
        sta RGuard              ; its place: 0 the Avatar
        jsr SlotWP
        jsr WPName
        lda WhoName
        sta TP
        lda WhoName+2
        sta TP+2
        bra .say
.none:  lda #$FFFF
        sta RGuard
        lda #W_NOONE
        asl a
        asl a
        tax
        lda WordTab,x
        sta TP
        lda WordTab+2,x
        sta TP+2
.say:   lda TP                  ; (the guard's name, {RestGuard} - levels.s)
        sta RGName
        lda TP+2
        sta RGName+2
        lda PenX
        clc
        adc SpaceW
        sta PenX
        lda TxtColor
        jsr SetColor
        jsr PrintWord
        jmp DoRest
.out:   rts

; WP = the party's member A (0 the Avatar, 1... PartyTab's).
SlotWP:
        cmp #0
        bne .m
        lda #AvRec
        sta WP
        lda MyBank
        sta WP+2
        rts
.m:     dec a
        jsr MemberAt
        lda MP
        sta WP
        lda MP+2
        sta WP+2
        rts

; The rest itself (U6GameWindow.U6Levels.cs Sleep, Nuvie's TimedRest): mealtime - each eats one food, theirs or
; anyone's in the party; the guard; the hours go by; those fed (not the guard) heal - once every 12 hours.
DoRest:
        stz Mode
        jsr NewLine
        lda #W_MEALTIME
        jsr PrintSaid
        jsr NewLine
        stz RFed
        stz RSlot
.eat:   lda RSlot
        cmp PartyN
        beq .e1
        bcs .guard
.e1:    jsr EatOne
        bcc .hungry
        lda RSlot               ; (BitTab: words)
        asl a
        tax
        lda BitTab,x
        tsb RFed
        lda RSlot
        jsr SlotWP
        jsr WPName
        lda #W_HASFOOD
        jsr Tell
        bra .next
.hungry: lda RSlot
        jsr SlotWP
        jsr WPName
        lda #W_NOFOOD
        jsr Tell
.next:  inc RSlot
        bra .eat
.guard: lda RGuard
        cmp #$FFFF
        beq .time
        jsr SlotWP
        jsr WPName
        lda #W_STANDS
        jsr Tell
.time:  lda RestH               ; the hours go by
        sta T5
.hr:    inc Hour
        lda Hour
        cmp #24
        bcc .nd
        stz Hour
        inc Day
.nd:    jsr EatHour             ; (ailments.s)
        dec T5
        bne .hr
        jsr ApplySchedules
        lda Day                 ; now in hours: healing once every 12
        asl a
        sta T5
        asl a
        clc
        adc T5
        asl a
        asl a
        clc
        adc Hour
        sta T5
        cmp RHealAt
        bcc .done
        clc
        adc #12
        sta RHealAt
        stz RSlot
.heal:  lda RSlot
        cmp PartyN
        beq .h1
        bcs .done
.h1:    cmp RGuard
        beq .hn
        asl a
        tax
        lda BitTab,x
        and RFed
        beq .hn
        lda RSlot
        jsr SlotWP
        ldy #C_MAXHP            ; the missing hit points: half of them and up to half again
        lda [WP],y
        ldy #C_HP
        sec
        sbc [WP],y
        beq .hn
        bmi .hn
        cmp #1
        bne .d
        inc a
.d:     lsr a
        sta T4
        bne .r
        lda #1
.r:     jsr Random
        clc
        adc T4
        ldy #C_HP
        clc
        adc [WP],y
        ldy #C_MAXHP
        cmp [WP],y
        bcc .set
        lda [WP],y
.set:   ldy #C_HP
        sta [WP],y
        jsr WPName
        lda #W_HEALED
        jsr Tell
.hn:    inc RSlot
        bra .heal
.done:  jsr RestLevelEvents      ; the leveling system's rest events (levels.s)
        jsr HpChanged
        jsr DrawView
        jsr PromptLine
        jmp RunQueue

; Carry set if member RSlot ate one food: theirs first, then anyone's in the party (in their bags too).
EatOne:
        lda RSlot
        jsr OwnerCode
        sta ROwn
        stz RPass
.pass:  stz RI
.l:     lda RI
        cmp ObjCount
        bcc .l1
        brl .np
.l1:    jsr ObjAddr
        ldy #6
        lda [ItemPtr],y
        and #32+64+256
        cmp #64
        beq .carried
        cmp #256
        bne .n
        lda RPass               ; in a bag: the second time round, if the bag is the party's
        beq .n
        ldy #10
        lda [ItemPtr],y
        and #$FC00
        cmp #$C400
        bne .n
        lda ItemPtr
        pha
        lda ItemPtr+2
        pha
        ldy #14
        lda [ItemPtr],y
        jsr ObjAddr
        ldy #6
        lda [ItemPtr],y
        and #32+64
        cmp #64
        bne .nb
        ldy #14
        lda [ItemPtr],y
        jsr PartyCode
        bcc .nb
        pla
        sta ItemPtr+2
        pla
        sta ItemPtr
        bra .eat
.nb:    pla
        sta ItemPtr+2
        pla
        sta ItemPtr
        bra .n
.carried:
        ldy #10                 ; food?
        lda [ItemPtr],y
        and #$FC00
        cmp #$C400
        bne .n
        ldy #14
        lda [ItemPtr],y
        ldx RPass
        bne .any
        cmp ROwn
        beq .eat
        bra .n
.any:   jsr PartyCode
        bcs .eat
.n:     inc RI
        brl .l
.np:    lda RPass
        bne .no
        inc RPass
        brl .pass
.no:    clc
        rts
.eat:   ldy #6                  ; eaten: gone
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        jsr RefreshInventory
        sec
        rts

; A = the owner code of the party's member A ($FFFF the Avatar, $8000 | map << 6 | place a member).
OwnerCode:
        cmp #0
        bne .m
        lda #$FFFF
        rts
.m:     dec a
        jsr MemberAt
        ldy #P_MAP
        lda [MP],y
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ldy #P_IDX
        ora [MP],y
        ora #$8000
        rts

; Carry set if owner code A is the party's.
PartyCode:
        cmp #$FFFF
        beq .yes
        sta RCode
        stz PK
.l:     lda PK
        cmp PartyN
        bcs .no
        inc a
        jsr OwnerCode
        cmp RCode
        beq .yes
        inc PK
        bra .l
.no:    clc
        rts
.yes:   sec
        rts

StatBuf:  .ds 72
DecN:     .dw 0
DecP:     .dw 0
DecD:     .dw 0
DecGo:    .dw 0
DecPow:   .dw 10000,1000,100,10,1
RestCh:   .dw 0
RestH:    .dw 0
RGuard:   .dw $FFFF
RFed:     .dw 0
RSlot:    .dw 0
RHealAt:  .dw 0
ROwn:     .dw 0
RPass:    .dw 0
RI:       .dw 0
RCode:    .dw 0
