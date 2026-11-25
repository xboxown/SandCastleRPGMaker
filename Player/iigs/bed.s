; Sandcastle RPG Maker - Apple IIgs player: beds and bedrolls, as the Windows Ultima 6 engine (U6GameWindow.Beds.cs,
; .Travel.cs UseBedroll / AskHowLongToSleep / SleepTheHoursAway): Use on a bed (or a bedroll laid on the ground) walks the
; Avatar up to it; with foes about: "We cannot rest with foes about!"; a bed that asks: its question, 0-9 hours typed
; (0 or Esc: its cancel line, no sleep; else at least its fewest, at most its most); a bedroll that doesn't ask: its own
; hours (or till dawn). Then the rest itself (stats.s DoRest: mealtime, the hours, healing) with no guard. A bedroll
; in the pack: "I should put it down first.". Sleep On Bed (EXT 8): the same with the bed nearest the Avatar.
; Special kind 12 (a bed): +1 flags (1 asks the hours, 2 a bedroll, 4 till dawn), +2 its question ($FFFF: "How many
; hours?"), +4 fewest, +5 most, +6 own hours, +7 its cancel line ($FFFF none).

SP_BED     = 12
W_PUTDOWN  = 116
W_FOESREST = 117
W_ECHO     = 118

; Use on a bed or bedroll at ItemPtr (SpRec its record). Carry set (done).
BedUse:
        ldy #6
        lda [ItemPtr],y
        and #64                 ; carried: only a bedroll, and it must be put down
        beq .laid
        lda #W_PUTDOWN
        jsr PrintSaid
        jmp BdEnd
.laid:  lda [ItemPtr]           ; up to it
        sta AcA
        sta AcA0
        ldy #2
        lda [ItemPtr],y
        sta AcB
        lda #AvRec
        sta WP
        lda MyBank
        sta WP+2
        lda PX
        sta AvRec
        lda PY
        sta AvRec+2
        jsr AcBeside            ; (acts.s)
        jsr DrawView
        jsr FoesNear            ; (stats.s)
        bcc .calm
        lda #W_FOESREST
        jsr PrintSaid
        jmp BdEnd
.calm:  lda SpRec+1
        and #1
        beq .own
        lda SpRec+2             ; its question
        cmp #$FFFF
        bne .q
        lda #W_HOURS
        jsr PrintSaid
        bra .ask
.q:     jsr BdSay
.ask:   jsr BdHours             ; A = 0-9
        cmp #0
        bne .h
        lda SpRec+7             ; no sleep: its cancel line
        cmp #$FFFF
        beq .nl
        pha
        jsr NewLine
        pla
        jsr BdSay
.nl:    jmp BdEnd
.h:     sta RestH
        lda SpRec+4             ; at least its fewest, at most its most
        and #$00FF
        cmp RestH
        bcc .mx
        sta RestH
.mx:    lda SpRec+5
        and #$00FF
        beq .go
        cmp RestH
        bcs .go
        sta RestH
        bra .go
.own:   lda SpRec+1             ; a bedroll that doesn't ask: its hours, or till dawn
        and #4
        beq .oh
        lda #30                 ; (6 - hour + 24) mod 24, 0: 24
        sec
        sbc Hour
        cmp #24
        bcc .dw
        sbc #24
.dw:    cmp #0
        bne .set
        lda #24
        bra .set
.oh:    lda SpRec+6
        and #$00FF
        bne .set
        lda #8
.set:   sta RestH
.go:    lda #$FFFF              ; no guard
        sta RGuard
        lda #W_NOONE
        asl a
        asl a
        tax
        lda WordTab,x
        sta RGName
        lda WordTab+2,x
        sta RGName+2
        jsr NewLine
        jsr DoRest              ; (stats.s)
        sec
        rts

; Done: the prompt (not in an event's command).
BdEnd:  lda BdEvent
        bne .e
        jsr PromptLine
.e:     sec
        rts

; A = a text's name number: written (wrapped).
BdSay:
        jsr NameNumber
        lda #W_ECHO
        jmp Format

; A = the hours typed: 0-9 (Esc: 0).
BdHours:
.l:     pha
        pea $0028               ; key down, auto-key
        pea #bank(EvRec)
        pea #EvRec
        tool $0A06              ; GetNextEvent
        pla
        beq .l
        lda EvMsg
        and #$007F
        cmp #$1B
        beq .esc
        cmp #'0'
        bcc .l
        cmp #'9'+1
        bcs .l
        sta RestCh
        sec
        sbc #'0'
        pha
        lda #RestCh
        jsr PrintMine           ; the figure typed
        pla
        rts
.esc:   lda #0
        rts

; EXT 8 BED (2 bytes): Sleep On Bed - the bed or bedroll on this map nearest the Avatar.
XtBedCmd:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta BdN
        lda #$FFFF
        sta BdBest
        stz BdAt+2
.l:     lda BdN
        beq .found
        ldy #6
        lda [ItemPtr],y
        and #32+64+256
        bne .n
        lda [ItemPtr],y
        and #$F000
        cmp CurBits
        bne .n
        jsr IsSpecial           ; (crafting.s)
        bcc .n
        cmp #SP_BED
        bne .n
        lda [ItemPtr]           ; how far
        sec
        sbc PX
        jsr AbsX
        sta T5
        ldy #2
        lda [ItemPtr],y
        sec
        sbc PY
        jsr AbsY
        clc
        adc T5
        cmp BdBest
        bcs .n
        sta BdBest
        lda ItemPtr
        sta BdAt
        lda ItemPtr+2
        sta BdAt+2
.n:     lda #16
        jsr NextItem
        dec BdN
        bra .l
.found: lda BdAt+2
        beq .done
        lda BdAt
        sta ItemPtr
        lda BdAt+2
        sta ItemPtr+2
        jsr IsSpecial
        lda #1
        sta BdEvent
        jsr BedUse
        stz BdEvent
.done:  lda #2
        jmp Next

BdEvent:  .dw 0
BdN:      .dw 0
BdBest:   .dw 0
BdAt:     .dw 0,0
