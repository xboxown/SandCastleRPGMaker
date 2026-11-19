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
        jsr LiveValue
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
        tax
        lda AvRec+16,x
        rts
.z:     lda #0
        rts
.next:  lda LvlOn
        beq .z
        lda AvRec+C_LEVEL
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
        bcc .ask
        lda #W_FOESNEAR
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

; Rest's hours: a figure 0-9 (Esc: none).
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
        lda AvRec+C_MAXHP       ; an eighth of the most an hour
        lsr a
        lsr a
        lsr a
        bne .p
        lda #1
.p:     sta T4
.hr:    lda AvRec+C_HP
        clc
        adc T4
        cmp AvRec+C_MAXHP
        bcc .hp
        lda AvRec+C_MAXHP
.hp:    sta AvRec+C_HP
        lda AvRec+C_MANA
        cmp AvRec+C_MAXMANA
        bcs .mn
        inc a
        sta AvRec+C_MANA
.mn:    inc Hour
        lda Hour
        cmp #24
        bcc .nd
        stz Hour
        inc Day
.nd:    dec RestH
        bne .hr
        jsr ApplySchedules
        jsr HpChanged
.stop:  stz Mode
        jmp PromptLine
.out:   rts

StatBuf:  .ds 72
DecN:     .dw 0
DecP:     .dw 0
DecD:     .dw 0
DecGo:    .dw 0
DecPow:   .dw 10000,1000,100,10,1
RestCh:   .dw 0
RestH:    .dw 0
