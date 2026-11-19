; Sandcastle RPG Maker - Apple IIgs player: ailments and hunger, as the Windows Ultima 6 engine (U6GameWindow.Vitals.cs,
; from Exult's timers): poisoned - 0..2 hit points every 10-20 s from 5 s on, wearing off after 60-180 s; asleep 5-10 s;
; paralyzed, charmed, cursed 60-120 s; invisible, protected 60-80 s. Asleep / paralyzed don't act; charmed foes don't
; fight; cursed: -3 to hit; protected: +3 to defence; invisible: foes don't see them. Hunger: the Avatar and members
; use 0..3 food an hour (their word 17, +50); at 0 a 2-in-5 chance a minute of a hit point lost. Use eats food.
; The ailing are kept in AilTab (24 bytes: who (4), bits, then their times in half seconds: poison, poison's next,
; asleep, paralyzed, charmed, cursed, invisible, protected); not saved.

A_POISON = 1
A_SLEEP  = 2
A_PARA   = 4
A_CHARM  = 8
A_CURSE  = 16
A_INVIS  = 32
A_PROT   = 64
A_WASFOE = 128                  ; charmed: it was hostile
A_FEAR   = 256                  ; runs from the Avatar (its time +22)
A_WD     = 512                  ; withstand death: the next killing blow heals instead
W_EATS   = 70
FOOD     = 50

; A, X = a record's address: A = its ailments' bits (0 none).
AilsOf:
        jsr AilFind
        bcc .none
        lda AilTab+4,y
        rts
.none:  lda #0
        rts

; A, X = a record's address: carry set, Y = its place in AilTab.
AilFind:
        sta AFlo
        stx AFhi
        ldy #0
.l:     cpy #24*16
        bcs .no
        lda AilTab+4,y
        beq .n
        lda AilTab,y
        cmp AFlo
        bne .n
        lda AilTab+2,y
        cmp AFhi
        beq .yes
.n:     tya
        clc
        adc #24
        tay
        bra .l
.no:    clc
        rts
.yes:   sec
        rts

; WP gets ailment A (a bit): its time, its effect. Carry clear if no room.
AilOn:
        sta ABit
        lda WP
        ldx WP+2
        jsr AilFind
        bcs .have
        ldy #0                  ; a free place
.f:     cpy #24*16
        bcs .full
        lda AilTab+4,y
        beq .take
        tya
        clc
        adc #24
        tay
        bra .f
.full:  clc
        rts
.take:  lda WP
        sta AilTab,y
        lda WP+2
        sta AilTab+2,y
        lda #0
        sta AilTab+4,y
.have:  sty AY
        lda ABit
        ora AilTab+4,y
        sta AilTab+4,y
        lda ABit                ; its time
        cmp #A_POISON
        bne .s
        lda #240                ; 120..360 half seconds, the first hurt in 10
        ldx #240
        jsr Between
        ldy AY
        sta AilTab+6,y
        lda #10
        sta AilTab+8,y
        bra .eff
.s:     cmp #A_FEAR
        bne .s2
        lda ESecs               ; its seconds (else 30)
        bne .fs
        lda #30
.fs:    asl a
        ldy AY
        sta AilTab+22,y
        brl .eff
.s2:    cmp #A_WD
        bne .s3
        brl .eff
.s3:    cmp #A_SLEEP
        bne .p
        lda #10
        ldx #10
        jsr Between
        ldy AY
        sta AilTab+10,y
        bra .eff
.p:     ldx #12                 ; paralyzed, charmed, cursed: 120..240; invisible, protected: 120..160
        cmp #A_PARA
        beq .long
        ldx #14
        cmp #A_CHARM
        beq .long
        ldx #16
        cmp #A_CURSE
        beq .long
        ldx #18
        cmp #A_INVIS
        beq .short
        ldx #20
.short: phx
        lda #120
        ldx #40
        bra .set
.long:  phx
        lda #120
        ldx #120
.set:   jsr Between
        plx
        stx T4
        pha
        lda AY
        clc
        adc T4
        tay
        pla
        sta AilTab,y
.eff:   lda ABit                ; asleep / paralyzed: still; charmed: not hostile now
        and #A_SLEEP+A_PARA
        beq .ch
        jsr WPIsAvatar
        bcs .ch
        ldy #C_FLAGS
        lda [WP],y
        ora #16
        sta [WP],y
.ch:    lda ABit
        cmp #A_CHARM
        bne .ok
        jsr WPIsAvatar
        bcs .ok
        ldy #C_FLAGS
        lda [WP],y
        and #2
        beq .ok
        lda [WP],y
        and #$FFFD
        sta [WP],y
        ldy AY
        lda AilTab+4,y
        ora #A_WASFOE
        sta AilTab+4,y
.ok:    sec
        rts

; A = A + 0..X-1 (random)
Between:
        sta T5
        txa
        jsr Random
        clc
        adc T5
        rts

; WP loses ailment A.
AilOff:
        sta ABit
        lda WP
        ldx WP+2
        jsr AilFind
        bcc .out
        sty AY
        lda ABit
        eor #$FFFF
        and AilTab+4,y
        sta AilTab+4,y
        jmp Undo
.out:   rts

; The effects of ABit ending for the one at AilTab,AY (its record WP): awake, hostile again if it was.
Undo:
        ldy AY
        lda AilTab,y
        sta WP
        lda AilTab+2,y
        sta WP+2
        lda ABit
        and #A_SLEEP+A_PARA
        beq .ch
        lda AilTab+4,y          ; still the other one: still
        and #A_SLEEP+A_PARA
        bne .ch
        jsr WPIsAvatar
        bcs .ch
        ldy #C_FLAGS
        lda [WP],y
        and #$FFEF
        sta [WP],y
.ch:    ldy AY
        lda ABit
        cmp #A_CHARM
        bne .out
        lda AilTab+4,y
        and #A_WASFOE
        beq .out
        lda AilTab+4,y
        and #$FF7F
        sta AilTab+4,y
        jsr WPIsAvatar
        bcs .out
        ldy #C_FLAGS
        lda [WP],y
        ora #2
        sta [WP],y
.out:   ldy AY                  ; nothing left: the place free
        lda AilTab+4,y
        and #$7F
        bne .r
        lda #0
        sta AilTab+4,y
.r:     rts

; Every half second (30 ticks): the ailments' times, poison's hurts.
AilRun:
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta T4
        pla
        lda T4
        sec
        sbc AilTick
        cmp #30
        bcs .go
        rts
.go:    lda T4
        sta AilTick
        jsr SpellTimes          ; time stop, summons, fields (spells2.s)
        stz AY
.l:     ldy AY
        cpy #24*16
        bcc .one
        rts
.one:   lda AilTab+4,y
        bne .some
        brl .n
.some:  lda AilTab,y
        sta WP
        lda AilTab+2,y
        sta WP+2
        lda AilTab+4,y          ; poisoned: the next hurt
        and #A_POISON
        beq .tm
        lda AilTab+8,y
        dec a
        sta AilTab+8,y
        bne .tm
        lda #20                 ; again in 10-20 s
        ldx #20
        jsr Between
        ldy AY
        sta AilTab+8,y
        lda #3
        jsr Random
        beq .tm
        jsr Hurt
.tm:    ldx #2                  ; the times: poison 6, asleep 10, paralyzed 12, charmed 14, cursed 16, invisible 18, protected 20
        lda #A_POISON
        sta ABit
.t:     ldy AY
        lda AilTab+4,y
        and ABit
        beq .tn
        lda TimeOf,x
        clc
        adc AY
        tay
        lda AilTab,y
        dec a
        sta AilTab,y
        bne .tn
        phx
        ldy AY
        lda ABit
        eor #$FFFF
        and AilTab+4,y
        sta AilTab+4,y
        jsr Undo
        plx
.tn:    asl ABit
        inx
        inx
        lda ABit
        cmp #A_WASFOE           ; (not a time)
        beq .tn
        cpx #20
        bcc .t
.n:     lda AY
        clc
        adc #24
        sta AY
        brl .l

; Poison hurts WP: A hit points (1-2), told as a wound; it can kill.
Hurt:
        sta EAmt
        lda WP
        sta DefP
        sta AtkP
        lda WP+2
        sta DefP+2
        sta AtkP+2
        jsr DefName
        ldy #C_HP
        lda [DefP],y
        sec
        sbc EAmt
        bpl .h
        lda #0
.h:     sta [DefP],y
        lda #1
        sta WTLead
        stz Told
        jsr TellWound
        ldy #C_HP
        lda [DefP],y
        bne .alive
        jsr Dies
.alive: jsr HpChanged
        lda Told
        beq .o
        stz Told
        lda Mode
        cmp #7
        beq .o
        lda #Prompt
        jsr PrintMine
.o:     rts

; The blow (RollV = roll + attacker's combat - defender's): protected defender -3, cursed attacker -3.
BlowAils:
        lda DefP
        ldx DefP+2
        jsr AilsOf
        and #A_PROT
        beq .c
        lda RollV
        sec
        sbc #3
        sta RollV
.c:     lda AtkP
        ldx AtkP+2
        jsr AilsOf
        and #A_CURSE
        beq .o
        lda RollV
        sec
        sbc #3
        sta RollV
.o:     rts

; A spell's effect (EKind on WP): carry set if it was an ailment's or food's.
AilEffect:
        lda EKind
        cmp #3                  ; Poison
        bne .k2
        lda #A_POISON
        jsr AilOn
        sec
        rts
.k2:    cmp #2                  ; Cure Poison
        bne .k8
        lda #A_POISON
        jsr AilOff
        sec
        rts
.k8:    cmp #8                  ; Curse
        bne .k9
        lda #A_CURSE
        jsr AilOn
        sec
        rts
.k9:    cmp #9                  ; Remove Curse
        bne .k10
        lda #A_CURSE
        jsr AilOff
        sec
        rts
.k10:   cmp #10                 ; Protection
        bne .k11
        lda #A_PROT
        jsr AilOn
        sec
        rts
.k11:   cmp #11                 ; Invisibility
        bne .k14
        lda #A_INVIS
        jsr AilOn
        jsr DrawView
        sec
        rts
.k14:   cmp #14                 ; Nourish
        bne .k4
        ldy #FOOD
        lda [WP],y
        cmp #$FFFF
        beq .y
        clc
        adc EAmt
        cmp #32
        bcc .f
        lda #31
.f:     sta [WP],y
.y:     sec
        rts
.k4:    cmp #4                  ; Sleep and Paralyze: their times too (the bits set by magic.s)
        bne .k6
        lda #A_SLEEP
        jsr AilOn
        sec
        rts
.k6:    cmp #6
        bne .k5
        lda #A_PARA
        jsr AilOn
        sec
        rts
.k5:    cmp #5                  ; Awaken, Cure Paralysis
        bne .k7
        lda #A_SLEEP
        jsr AilOff
        sec
        rts
.k7:    cmp #29                 ; Charm
        bne .k30
        lda #A_CHARM
        jsr AilOn
        sec
        rts
.k30:   cmp #30                 ; Fear
        bne .k31
        lda #A_FEAR
        jsr AilOn
        sec
        rts
.k31:   cmp #31                 ; Dance: still, a while
        bne .k42
        lda #A_PARA
        jsr AilOn
        sec
        rts
.k42:   cmp #42                 ; Feign Death: as unseen
        bne .k43
        lda #A_INVIS
        jsr AilOn
        sec
        rts
.k43:   cmp #43                 ; Withstand Death
        bne .k7b
        lda #A_WD
        jsr AilOn
        sec
        rts
.k7b:   cmp #7
        bne .no
        lda #A_PARA
        jsr AilOff
        sec
        rts
.no:    clc
        rts

; AIL who bit on/off (Change State, Modify Ailments).
OpAil:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta GWord
        iny
        lda [TalkPtr],y
        and #$00FF
        sta GOp
        jsr WhoRec
        bcs .skip
        lda GWord
        ldx GOp
        beq .off
        jsr AilOn
        bra .skip
.off:   jsr AilOff
.skip:  jsr DrawView
        lda #7
        jmp Next

; A member left the party (PartyTab moved up): ailments of members are forgotten.
AilForget:
        ldy #0
.l:     cpy #24*16
        bcs .o
        lda AilTab+2,y
        cmp MyBank
        bne .n
        lda AilTab,y
        sec
        sbc #PartyTab
        cmp #512
        bcs .n
        lda #0
        sta AilTab+4,y
.n:     tya
        clc
        adc #24
        tay
        bra .l
.o:     rts

; ---- hunger ----

; An hour: the Avatar and each member use 0..3 food.
EatHour:
        lda #AvRec
        sta WP
        lda MyBank
        sta WP+2
        jsr UseFood
        stz PK
.l:     lda PK
        cmp PartyN
        bcs .o
        jsr MemberAt
        lda MP
        sta WP
        lda MP+2
        sta WP+2
        jsr UseFood
        inc PK
        bra .l
.o:     rts
UseFood:
        ldy #FOOD
        lda [WP],y
        cmp #$FFFF              ; never hungry
        beq .o
        sta T5
        lda #4
        jsr Random
        sta T4
        lda T5
        sec
        sbc T4
        bpl .f
        lda #0
.f:     ldy #FOOD
        sta [WP],y
.o:     rts

; A minute: the starving (food 0) lose a hit point, 2 times in 5.
Starving:
        lda #AvRec
        sta WP
        lda MyBank
        sta WP+2
        jsr Starve
        stz PK
.l:     lda PK
        cmp PartyN
        bcs .o
        jsr MemberAt
        lda MP
        sta WP
        lda MP+2
        sta WP+2
        jsr Starve
        inc PK
        bra .l
.o:     rts
Starve:
        ldy #FOOD
        lda [WP],y
        bne .o
        lda #5
        jsr Random
        cmp #2
        bcs .o
        lda #1
        jmp Hurt
.o:     rts

; Carry set if Other (A) is food's ($C400 + nutrition).
IsFood:
        sta T4
        and #$FC00
        cmp #$C400
        bne .no
        lda T4
        sec
        rts
.no:    lda T4
        clc
        rts

; Use on food in the grid (GridRec; the shown member eats): fed, the food gone - "{1} eats {0}."
EatIt:
        lda GridRec
        sta ItemPtr
        lda GridRec+2
        sta ItemPtr+2
        ldy #10
        lda [ItemPtr],y
        and #$00FF
        sta EAmt
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        ldy #8
        lda [ItemPtr],y
        jsr NameNumber
        jsr ViewPtr             ; the shown one (party.s)
        lda ViewP
        sta WP
        lda ViewP+2
        sta WP+2
        ldy #FOOD
        lda [WP],y
        cmp #$FFFF
        beq .t
        clc
        adc EAmt
        cmp #32
        bcc .f
        lda #31
.f:     sta [WP],y
.t:     jsr WPName
        jsr RefreshInventory
        lda #W_EATS
        jsr Tell
        lda #1
        sta TurnUsed
        rts

AilTab:   .ds 24*16+2
AilTick:  .dw 0
AFlo:     .dw 0
AFhi:     .dw 0
ABit:     .dw 0
AY:       .dw 0
TimeOf:   .dw 0,6,10,12,14,16,18,20,0,22
