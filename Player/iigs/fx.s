; Sandcastle RPG Maker - Apple IIgs player: animations played over the view by Play Animation (ext.s EXT 3), as
; U6GameWindow.Actions.cs PlayAnimationCommand: a sprite's frames (at most 8, each its own ticks) at a cell or over a
; character (following it), drawn over everything, for so many ticks; up to 4 at once. Each slot (48 bytes):
; +0 on, +2 frames, +4 frame now, +6 tick it began, +8 ticks left, +10 0 at a cell / 1 over a character, +12 x,
; +14 y, +16 the character's record (long), +20 the frame's ticks, +24 (sprite, ticks) x 8.

FX_SLOTS = 4
FX_SIZE  = 48

; X = a free slot's place (carry set), else carry clear.
FxFree:
        ldx #0
.l:     lda FxTab,x
        beq .y
        txa
        clc
        adc #FX_SIZE
        tax
        cpx #FX_SLOTS*FX_SIZE
        bcc .l
        clc
        rts
.y:     sec
        rts

; Each slot's next frame when its time comes; one whose time is over ends. Any change: the view drawn again.
FxRun:
        stz FxChg
        ldx #0
.l:     lda FxTab,x
        beq .n
        phx
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta FxNow
        pla
        plx
        lda FxNow
        sec
        sbc FxTab+6,x
        cmp FxTab+20,x
        bcc .n
        sta FxT
        lda FxNow
        sta FxTab+6,x
        inc FxChg
        lda FxTab+8,x           ; time used
        sec
        sbc FxT
        bcc .end
        beq .end
        sta FxTab+8,x
        lda FxTab+4,x           ; the next frame
        inc a
        cmp FxTab+2,x
        bcc .k
        lda #0
.k:     sta FxTab+4,x
        jsr FxFrameTicks
        bra .n
.end:   stz FxTab,x
.n:     txa
        clc
        adc #FX_SIZE
        tax
        cpx #FX_SLOTS*FX_SIZE
        bcc .l
        lda FxChg
        beq .out
        lda Mode                ; (not over a question's cursor or a screen of its own)
        cmp #7
        bcs .out
        jmp DrawView
.out:   rts

; X = a slot: +20 = its frame's ticks.
FxFrameTicks:
        lda FxTab+4,x
        asl a
        asl a
        sta FxT
        txa
        clc
        adc FxT
        tay
        lda FxTab+26,y
        sta FxTab+20,x
        rts

; Over everything in the view (DrawView): each slot's frame.
DrawFx:
        ldx #0
.l:     lda FxTab,x
        beq .n
        stx FxX
        lda FxTab+4,x           ; its sprite
        asl a
        asl a
        sta FxT
        txa
        clc
        adc FxT
        tay
        lda FxTab+24,y
        sta SprId
        lda FxTab+10,x
        beq .cell
        lda FxTab+16,x          ; over a character: where it is now (the Avatar: PX, PY)
        sta Src
        lda FxTab+18,x
        sta Src+2
        cmp MyBank
        bne .ch
        lda Src
        cmp #AvRec
        bne .ch
        ldx PX
        ldy PY
        bra .at
.ch:    lda [Src]
        tax
        ldy #2
        lda [Src],y
        tay
        bra .at
.cell:  lda FxTab+14,x
        tay
        lda FxTab+12,x
        tax
.at:    jsr CellOnScreen
        bcc .nx
        jsr DrawSprite
.nx:    ldx FxX
.n:     txa
        clc
        adc #FX_SIZE
        tax
        cpx #FX_SLOTS*FX_SIZE
        bcc .l
        rts

; Leaving a map: its animations end.
FxClear:
        ldx #0
.l:     stz FxTab,x
        txa
        clc
        adc #FX_SIZE
        tax
        cpx #FX_SLOTS*FX_SIZE
        bcc .l
        rts

; ---- the view shaken (Shake Screen, Earthquake: U6GameWindow PlayRealShakeEffect - up to Amp pixels each way) ----

; ShLeft ticks of shaking, ShAmp pixels: a new offset every 6 ticks (BlitView copies the view from that far off).
ShakeRun:
        lda ShLeft
        beq .out
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta FxNow
        pla
        lda FxNow
        sec
        sbc ShTick
        cmp #6
        bcc .out
        sta FxT
        lda FxNow
        sta ShTick
        lda ShLeft
        sec
        sbc FxT
        bcc .end
        beq .end
        sta ShLeft
        lda ShAmp               ; across: -Amp..Amp pixels, 2 a byte
        asl a
        inc a
        jsr Random
        sec
        sbc ShAmp
        cmp #$8000
        ror a
        sta ShakeSrc
        lda ShAmp               ; down: -Amp..Amp lines, 160 bytes each
        asl a
        inc a
        jsr Random
        sec
        sbc ShAmp
        sta MulA
        lda #160
        sta MB0
        jsr Mul16
        lda MulR
        clc
        adc ShakeSrc
        sta ShakeSrc
        jmp BlitView
.end:   stz ShLeft
        stz ShakeSrc
        jmp BlitView
.out:   rts

ShakeSrc: .dw 0
ShLeft:   .dw 0
ShAmp:    .dw 0
ShTick:   .dw 0

FxTab:    .ds FX_SLOTS*FX_SIZE
FxChg:    .dw 0
FxNow:    .dw 0
FxT:      .dw 0
FxX:      .dw 0
