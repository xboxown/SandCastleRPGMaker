; Sandcastle RPG Maker - Apple IIgs player: the HUD's live pieces, as the Windows Ultima 6 engine (U6GameWindow.U6Hud.cs
; RefreshHudClock): each game minute the date-and-wind words (Britannia's calendar: 12 months of 28 days, from 161;
; "Wind:" the wind - turning every 3 hours, C when calm), the compass's frame by the wind (8: N NE E SE S SW W NW) and the
; sun-and-moons' frame by the time of day. Custom buttons: a click runs the button's event (its code: key $100 + n).

ParseHud:
        lda [Ptr]
        sta DialN
        lda #2
        jsr AddPtr
        ldy #0
.d:     cpy #8*14
        bcs .dd
        lda [Ptr],y
        sta DialTab,y
        iny
        iny
        bra .d
.dd:    lda DialN               ; 14 bytes each
        asl a
        sta T4
        asl a
        asl a
        asl a
        sec
        sbc T4
        jsr AddPtr
        lda [Ptr]
        sta DateN
        lda #2
        jsr AddPtr
        ldy #0
.t:     cpy #4*6
        bcs .td
        lda [Ptr],y
        sta DateTab,y
        iny
        iny
        bra .t
.td:    lda DateN               ; 6 bytes each
        asl a
        sta T4
        asl a
        clc
        adc T4
        jsr AddPtr
        jmp ParseSpecials       ; (crafting.s)

; The game's screen drawn anew: every live piece again.
HudForce:
        ldx #0
.l:     lda #$FFFF
        sta DialLast,x
        inx
        inx
        cpx #16
        bcc .l
        lda #$FFFF
        sta DateLast
        jmp HudClock

; Each game minute: the dials' frames, the date words - drawn when they change.
HudClock:
        lda Mode
        cmp #12
        bcc .go
        rts
.go:    jsr Wind                ; WindI: 0-7, 8 calm
        ldx #0
.l:     txa
        lsr a
        cmp DialN
        bcc .one
        brl .date
.one:   stx HX
        txa                     ; its record: x 7
        asl a
        asl a
        asl a
        sec
        sbc HX
        tay
        sty HY
        lda DialTab+12,y        ; frames
        sta HF
        lda DialTab,y
        bne .sun
        lda WindI               ; the compass: the wind's frame (calm: the first)
        cmp #8
        bcc .w
        lda #0
.w:     sta MulA
        lda HF
        sta MB0
        jsr Mul16
        lda MulR
        lsr a
        lsr a
        lsr a
        bra .f
.sun:   lda Hour                ; the sun and moons: the time of day's
        asl a
        asl a
        asl a
        asl a
        sta T4
        asl a
        asl a
        sec
        sbc T4                  ; x 60
        clc
        adc Minute
        sta MulA
        lda HF
        sta MB0
        jsr Mul16
        lda MulR
        sta ExA
        lda MulR+2
        bne .big
        lda #1440
        sta ExB
        lda ExA
        jsr SDiv
        lda ExQ
        bra .f
.big:   lda #0
.f:     cmp HF
        bcc .fr
        lda HF
        dec a
.fr:    ldx HX
        cmp DialLast,x
        beq .n
        sta DialLast,x
        ldy HY
        clc
        adc DialTab+10,y
        sta SprId
        lda DialTab+2,y
        sta RX
        sta CellX
        lda DialTab+4,y
        sta RY
        sta CellY
        lda DialTab+6,y
        sta RW
        lda DialTab+8,y
        sta RH
        jsr RestoreRect
        jsr DrawPic
.n:     ldx HX
        inx
        inx
        brl .l
.date:  lda DateN
        bne .dt
        rts
.dt:    lda Day                 ; changed? (the minute is enough: the wind turns by the hour)
        xba
        eor Hour
        cmp DateLast
        bne .new
        rts
.new:   sta DateLast
        jsr DateWords
        stz HX
.dl:    lda HX
        cmp DateN
        bcc .d1
        rts
.d1:    asl a                   ; x 6
        sta T4
        asl a
        clc
        adc T4
        tay
        lda DateTab,y
        sta RX
        lda DateTab+2,y
        sta RY
        lda #320
        sec
        sbc RX
        cmp #160
        bcc .wd
        lda #160
.wd:    sta RW
        lda #10
        sta RH
        phy
        jsr RestoreRect
        ply
        lda DateTab+4,y         ; its colour ($FFFF: the brightest)
        cmp #$FFFF
        bne .c
        lda Bright
.c:     pha
        tool $A004              ; SetForeColor
        lda RX
        pha
        lda RY
        clc
        adc #8
        pha
        tool $3A04              ; MoveTo
        pea #bank(DateBuf)
        pea #DateBuf
        tool $A604              ; DrawCString
        inc HX
        bra .dl

; DateBuf = "M-D-YYYY Wind:X"
DateWords:
        lda Day                 ; the month: day / 28 mod 12 + 1
        sta ExA
        lda #28
        sta ExB
        lda ExA
        jsr SDiv
        lda ExR
        inc a
        sta HDay
        lda ExQ
        sta ExA
        lda #12
        sta ExB
        lda ExA
        jsr SDiv
        lda ExR
        inc a
        sta HMonth
        lda Day                 ; the year: 161 + day / 336
        sta ExA
        lda #336
        sta ExB
        lda ExA
        jsr SDiv
        lda ExQ
        clc
        adc #161
        sta HYear
        ldx #0
        lda HMonth
        jsr Decimal
        jsr Dash1
        lda HDay
        jsr Decimal
        jsr Dash1
        lda HYear               ; four figures
        cmp #1000
        bcs .y
        jsr Zero1
        lda HYear
        cmp #100
        bcs .y
        jsr Zero1
        lda HYear
        cmp #10
        bcs .y
        jsr Zero1
.y:     lda HYear
        jsr Decimal
        ldy #0                  ; " Wind:"
.w:     lda WindWord,y
        and #$00FF
        beq .wn
        jsr Put1
        iny
        bra .w
.wn:    lda WindI
        asl a
        tay
        lda WindNames,y
        and #$00FF
        jsr Put1
        lda WindNames+1,y
        and #$00FF
        beq .e
        jsr Put1
.e:     lda #0
        jsr Put1
        ldx #0                  ; StatBuf to DateBuf
.c:     lda StatBuf,x
        sta DateBuf,x
        inx
        inx
        cpx #24
        bcc .c
        rts
Dash1:  lda #'-'
Put1:   sep #$20
        .a8
        sta StatBuf,x
        rep #$20
        .a16
        inx
        rts
Zero1:  lda #'0'
        bra Put1

; WindI = the wind now (Windows' HudWind): ((day x 24 + hour) / 3 x 2654435761) >> 24, mod 10: 0 calm (8), else that mod 8.
Wind:
        lda Day
        asl a
        asl a
        asl a
        sta T4
        asl a
        clc
        adc T4                  ; x 24
        clc
        adc Hour
        sta ExA
        lda #3
        sta ExB
        lda ExA
        jsr SDiv
        lda ExQ
        sta HQ
        sta MulA                ; HQ x $9E3779B1, its top byte: (HQ x $79B1) + (HQ x $9E37) << 16
        lda #$79B1
        sta MB0
        jsr Mul16
        lda MulR+2
        sta T4
        lda HQ
        sta MulA
        lda #$9E37
        sta MB0
        jsr Mul16
        lda MulR
        clc
        adc T4
        xba
        and #$00FF
        sta ExA
        lda #10
        sta ExB
        lda ExA
        jsr SDiv
        lda ExR
        bne .w
        lda #8
        sta WindI
        rts
.w:     and #7
        sta WindI
        rts

; A HUD custom button clicked (A = $100 + its code): its event runs.
HudButton:
        sec
        sbc #$100
        tay
        lda #17
        ldx #$FFFE
        jsr QueueEvent
        jmp RunQueue

WindWord: .cstr " Wind:"
WindNames: .db "N",0,"NE","E",0,"SE","S",0,"SW","W",0,"NW","C",0
DialN:    .dw 0
DialTab:  .ds 8*14
DialLast: .ds 16
DateN:    .dw 0
DateTab:  .ds 4*6
DateLast: .dw 0
DateBuf:  .ds 24
HX:       .dw 0
HY:       .dw 0
HF:       .dw 0
HQ:       .dw 0
HDay:     .dw 0
HMonth:   .dw 0
HYear:    .dw 0
WindI:    .dw 0
