; Sandcastle RPG Maker - Apple IIgs player: what lies on the floor, as the Windows Ultima 6 engine
; (U6GameWindow.ObjectsTick.cs, Objects.cs BurnLights):
;  - tiles with events (Object Editor > Tile): stepping onto one runs its By Step On, standing on it By Stand On each
;    turn, stepping off By Step Off - the Avatar's steps;
;  - traps (Object Editor > Trap, ground): stepping onto one sets it off - its own By Step On, its sound, damage (and
;    poisoned / asleep / paralyzed), a hidden one shows itself; a one-shot one is spent, others re-arm after their time.
;    The Avatar and every character walking;
;  - fires (a Light Source that burns when lit): whoever stands in a lit one is burnt each turn;
;  - lights burn down by the game's minutes (Burn Minutes): spent, they go out for good ("{0} has burnt out.").
; GAME.DAT (after the Avatar's sprite): u16 traps, each 8 words: name, damage min, max, ailment bit, flags (1 hidden,
; 2 one-shot), re-arm turns, sound ($FFFF), 0; u16 lights, each 4 words: name, burn minutes (0 never), fire damage
; min, max (max 0: not a fire). Kept by name; each sprung trap's state in TrSt (not saved, as ailments).

W_TRAPOFF  = 89
W_BURNTOUT = 90

ParseFloor:
        lda [Ptr]
        sta TrN
        lda #2
        jsr AddPtr
        lda TrN                 ; 16 bytes each
        asl a
        asl a
        asl a
        asl a
        sta T4
        ldy #0
.t:     cpy T4
        bcs .td
        lda [Ptr],y
        sta TrTab,y
        iny
        iny
        bra .t
.td:    lda T4
        jsr AddPtr
        lda [Ptr]
        sta LtN
        lda #2
        jsr AddPtr
        lda LtN                 ; 8 bytes each
        asl a
        asl a
        asl a
        sta T4
        ldy #0
.l:     cpy T4
        bcs .ld
        lda [Ptr],y
        sta LtTab,y
        iny
        iny
        bra .l
.ld:    lda T4
        jsr AddPtr
        stz TrSN
        stz BnN
        jmp ParseBlood          ; a death's blood (below)

; A = a name: carry set and X = its place in TrTab if it is a trap.
TrapNamed:
        sta FzNm
        ldx #0
        ldy TrN
.l:     cpy #0
        beq .no
        lda TrTab,x
        cmp FzNm
        beq .yes
        txa
        clc
        adc #16
        tax
        dey
        bra .l
.yes:   sec
        rts
.no:    clc
        rts

; A = a name: carry set and X = its place in LtTab if it is a light with burn minutes or a fire.
LightNamed:
        sta FzNm
        ldx #0
        ldy LtN
.l:     cpy #0
        beq .no
        lda LtTab,x
        cmp FzNm
        beq .yes
        txa
        clc
        adc #8
        tax
        dey
        bra .l
.yes:   sec
        rts
.no:    clc
        rts

; A = an object's number: carry set and X = its place in TrSt (4 words: object, sprung, re-arm turns, 0).
TrapState:
        sta FzObj
        ldx #0
        ldy TrSN
.l:     cpy #0
        beq .no
        lda TrSt,x
        cmp FzObj
        beq .yes
        txa
        clc
        adc #8
        tax
        dey
        bra .l
.yes:   sec
        rts
.no:    clc
        rts

; DrawObjects: carry set if the object at ItemPtr is a hidden trap not yet sprung (not drawn).
HiddenTrap:
        lda TrN
        beq .no
        ldy #8
        lda [ItemPtr],y
        jsr TrapNamed
        bcc .no
        lda TrTab+8,x           ; hidden?
        and #1
        beq .no
        jsr IndexOf
        jsr TrapState
        bcc .yes
        lda TrSt+2,x
        bne .no
.yes:   sec
        rts
.no:    clc
        rts

; ---- stepping ----

; The Avatar stepped from OldX, OldY onto PX, PY (SteppedEvents): tiles' Step Off / Step On, traps, fires.
FloorAvatarStep:
        lda #AvRec
        sta FzWho
        lda MyBank
        sta FzWho+2
        lda #T_EXIT
        sta FzTrig
        ldx OldX
        ldy OldY
        jsr FloorTiles
        lda #T_ENTER
        sta FzTrig
        ldx PX
        ldy PY
        jsr FloorTiles
        ldx PX
        ldy PY
        jmp FloorHarm

; TryCharStep: CP stepped onto SNX, SNY - traps and fires there (ItemPtr and WP kept).
CharFloorStep:
        lda TrN
        ora LtN
        bne .go
        rts
.go:    lda ItemPtr
        sta FzSvI
        lda ItemPtr+2
        sta FzSvI+2
        lda WP
        sta FzSvW
        lda WP+2
        sta FzSvW+2
        jsr FloorCharStep
        lda FzSvI
        sta ItemPtr
        lda FzSvI+2
        sta ItemPtr+2
        lda FzSvW
        sta WP
        lda FzSvW+2
        sta WP+2
        rts

FloorCharStep:
        lda CP
        sta FzWho
        lda CP+2
        sta FzWho+2
        ldx SNX
        ldy SNY
        jmp FloorHarm

; Each turn: traps re-arm, the Avatar's Stand On tiles, everyone standing in a lit fire is burnt.
FloorTurn:
        ldx #0                  ; re-arming
        ldy TrSN
.r:     cpy #0
        beq .rd
        lda TrSt+4,x
        beq .rn
        dec TrSt+4,x
.rn:    txa
        clc
        adc #8
        tax
        dey
        bra .r
.rd:    lda #AvRec
        sta FzWho
        lda MyBank
        sta FzWho+2
        lda JustMoved           ; (this turn's step burnt already)
        bne .ch
        lda #T_STAY
        sta FzTrig
        ldx PX
        ldy PY
        jsr FloorTiles
        lda #1
        sta FzFire
        ldx PX
        ldy PY
        jsr FloorHarm
        stz FzFire
.ch:    lda LtN                 ; the characters standing in fires
        beq .o
        lda #1
        sta FzFire
        lda ChrPtr
        sta FzWho
        lda ChrPtr+2
        sta FzWho+2
        lda ChrCount
        sta FzC
.c:     lda FzC
        beq .cd
        lda FzWho
        sta WP
        lda FzWho+2
        sta WP+2
        ldy #C_FLAGS
        lda [WP],y
        and #4+8
        bne .cn
        lda [WP]
        tax
        ldy #2
        lda [WP],y
        tay
        jsr FloorHarm
.cn:    lda FzWho
        clc
        adc #64
        sta FzWho
        lda FzWho+2
        adc #0
        sta FzWho+2
        dec FzC
        bra .c
.cd:    stz FzFire
.o:     rts

; The tiles with events at X, Y: their own pages, trigger FzTrig.
FloorTiles:
        stx FzCX
        sty FzCY
        lda OEvCount            ; (ownev.s: things with their own events)
        beq .o
        jsr FloorFirst
.l:     jsr FloorNext
        bcc .o
        ldy #8
        lda [ItemPtr],y
        jsr TrapNamed           ; (a trap's own By Step On: when it goes off)
        bcs .l
        ldy #8
        lda [ItemPtr],y
        ldx FzTrig
        jsr ObjEvQueue
        bra .l
.o:     rts

; Traps and lit fires at X, Y hurt FzWho (FzFire set: only fires - standing still).
FloorHarm:
        stx FzCX
        sty FzCY
        lda TrN
        ora LtN
        beq .o
        jsr FloorFirst
.l:     jsr FloorNext
        bcc .o
        lda FzFire
        bne .fire
        ldy #8
        lda [ItemPtr],y
        jsr TrapNamed
        bcc .fire
        jsr Spring
        bra .l
.fire:  ldy #10                 ; a lit fire
        lda [ItemPtr],y
        jsr IsLight             ; (look.s)
        bcc .l
        and #$0100
        beq .l
        ldy #8
        lda [ItemPtr],y
        jsr LightNamed
        bcc .l
        lda LtTab+6,x
        beq .l
        lda LtTab+4,x
        sta FzMin
        lda LtTab+6,x
        sta FzMax
        jsr HarmWho
        bra .l
.o:     rts

; Walks the objects: FloorFirst, then FloorNext gives each on this map at FzCX, FzCY (carry set; ItemPtr; FzIdx its
; number), carry clear at the end.
FloorFirst:
        lda ObjPtr
        sta FzP
        lda ObjPtr+2
        sta FzP+2
        lda ObjCount
        sta FzLeft
        stz FzIdx
        dec FzIdx
        rts
FloorNext:
.l:     lda FzLeft
        bne .go
        clc
        rts
.go:    dec FzLeft
        inc FzIdx
        lda FzP
        sta ItemPtr
        lda FzP+2
        sta ItemPtr+2
        clc
        lda FzP
        adc #16
        sta FzP
        lda FzP+2
        adc #0
        sta FzP+2
        ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .l
        lda [ItemPtr]
        cmp FzCX
        bne .l
        ldy #2
        lda [ItemPtr],y
        cmp FzCY
        bne .l
        sec
        rts

; The trap at ItemPtr (TrTab + X) goes off under FzWho.
Spring:
        bra .go
.o0:    rts
.go:    stx FzT
        lda FzIdx
        jsr TrapState
        bcs .have
        lda TrSN                ; its state: new (32 at most)
        cmp #32
        bcs .o0
        asl a
        asl a
        asl a
        tax
        lda FzIdx
        sta TrSt,x
        stz TrSt+2,x
        stz TrSt+4,x
        inc TrSN
.have:  stx FzS
        ldy FzT
        lda TrSt+2,x            ; one-shot and spent: nothing
        beq .arm
        lda TrTab+8,y
        and #2
        bne .o0
.arm:   lda TrSt+4,x            ; not re-armed yet
        beq .fire
        rts
.fire:
        lda #1
        sta TrSt+2,x            ; sprung (a hidden one shows itself)
        lda TrTab+10,y          ; re-arms after its turns
        sta TrSt+4,x
        lda TrTab+12,y          ; its sound
        cmp #$FFFF
        beq .ns
        ldx #255
        jsr PlaySound
.ns:    ldy #8                  ; "Spike trap goes off!"
        lda [ItemPtr],y
        jsr NameNumber
        lda #1
        sta WTLead
        lda #W_TRAPOFF
        jsr Tell
        ldy #8                  ; its own By Step On
        lda [ItemPtr],y
        ldx #T_ENTER
        jsr ObjEvQueue
        ldy FzT
        lda TrTab+2,y
        sta FzMin
        lda TrTab+4,y
        sta FzMax
        lda TrTab+6,y
        sta FzAil
        jsr HarmWho
        lda FzAil               ; and its ailment, if still alive
        beq .o
        lda FzWho
        sta WP
        lda FzWho+2
        sta WP+2
        ldy #C_FLAGS
        lda [WP],y
        and #4
        bne .o
        lda FzAil
        jsr AilOn               ; (ailments.s)
.o:     jsr DrawView
        rts

; FzWho takes FzMin..FzMax damage (Hurt: told as a wound, it can kill).
HarmWho:
        lda FzMax
        beq .o
        cmp FzMin
        bcs .k
        lda FzMin
.k:     sec
        sbc FzMin
        inc a
        jsr Random
        clc
        adc FzMin
        beq .o
        pha
        lda FzWho
        sta WP
        lda FzWho+2
        sta WP+2
        pla
        jmp Hurt                ; (ailments.s)
.o:     rts

; ---- lights burning down ----

; A game minute (ClockRun): every lit light with burn minutes burns a minute; at 0 it is spent - out for good.
BurnMinute:
        lda LtN
        bne .go
        rts
.go:    lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        stz FzIdx
.l:     lda FzIdx
        cmp ObjCount
        bcc .one
        rts
.nx:    brl .n
.one:   ldy #6
        lda [ItemPtr],y
        and #32
        bne .nx
        ldy #10
        lda [ItemPtr],y
        jsr IsLight
        bcc .nx
        and #$0100
        beq .nx
        ldy #8
        lda [ItemPtr],y
        jsr LightNamed
        bcc .nx
        lda LtTab+2,x           ; burn minutes (0: never)
        beq .nx
        sta FzMin
        jsr BurnLeft            ; X = its place in BnTab (made with its full time)
        bcc .nx
        dec BnTab+2,x
        bne .nx
        ldy #10                 ; spent: out, and never a light again
        lda [ItemPtr],y
        and #$FEFF
        ora #$0200
        sta [ItemPtr],y
        lda ItemPtr             ; (kept: the words and the drawing use it)
        sta FzSvI
        lda ItemPtr+2
        sta FzSvI+2
        ldy #8                  ; "Torch has burnt out." (the Avatar's), its By Burnt Out
        lda [ItemPtr],y
        pha
        jsr NameNumber
        ldy #6
        lda [ItemPtr],y
        and #64
        beq .ev
        ldy #14
        lda [ItemPtr],y
        cmp #$FFFF
        bne .ev
        lda #1
        sta WTLead
        lda #W_BURNTOUT
        jsr Tell
        jsr RefreshInventory
.ev:    pla
        ldx #T_STAY
        jsr ObjEvQueue
        jsr DrawView
        lda FzSvI
        sta ItemPtr
        lda FzSvI+2
        sta ItemPtr+2
.n:     lda #16
        jsr NextItem
        inc FzIdx
        brl .l

; X = the place in BnTab of object FzIdx (2 words: object, minutes left); a new one gets FzMin. Carry clear: no room.
BurnLeft:
        ldx #0
        ldy BnN
.l:     cpy #0
        beq .new
        lda BnTab,x
        cmp FzIdx
        beq .yes
        inx
        inx
        inx
        inx
        dey
        bra .l
.new:   lda BnN
        cmp #64
        bcs .no
        lda FzIdx
        sta BnTab,x
        lda FzMin
        sta BnTab+2,x
        inc BnN
.yes:   sec
        rts
.no:    clc
        rts

TrN:      .dw 0
TrTab:    .ds 16*64
LtN:      .dw 0
LtTab:    .ds 8*64
TrSN:     .dw 0
TrSt:     .ds 8*32
BnN:      .dw 0
BnTab:    .ds 4*64
FzNm:     .dw 0
FzObj:    .dw 0
FzWho:    .dd 0
FzTrig:   .dw 0
FzFire:   .dw 0
FzCX:      .dw 0
FzCY:      .dw 0
FzC:      .dw 0
FzP:      .dd 0
FzLeft:   .dw 0
FzIdx:    .dw 0
FzT:      .dw 0
FzS:      .dw 0
FzMin:    .dw 0
FzMax:    .dw 0
FzAil:    .dw 0
FzSvI:    .dd 0
FzSvW:    .dd 0

; ---- a death's blood (Ultima 6 / Savage Empire / Martian Dreams) ----
; Where someone dies: blood under the body - a random frame of the Blood folder's sprite. Look names it ("blood"); it
; can't be taken or moved; once out of view it is gone. GAME.DAT (after the lights): u16 its first sprite ($FFFF none),
; frames, its name. BlTab: 16 of x, y, map bits, frame.

ParseBlood:
        lda [Ptr]
        sta BlFirst
        ldy #2
        lda [Ptr],y
        sta BlFrames
        ldy #4
        lda [Ptr],y
        sta BlName
        stz BlN
        lda #6
        jmp AddPtr

; Dies: blood where DefP fell.
DeathBlood:
        lda BlFirst
        cmp #$FFFF
        beq .o
        lda BlFrames
        beq .o
        jsr DefIsAvatar
        bcc .chr
        lda PX
        sta BlX
        lda PY
        sta BlY
        bra .put
.chr:   lda [DefP]
        sta BlX
        ldy #2
        lda [DefP],y
        sta BlY
.put:   lda BlN                 ; full: the first goes
        cmp #16
        bcc .room
        ldx #0
.sh:    lda BlTab+8,x
        sta BlTab,x
        inx
        inx
        cpx #15*8
        bcc .sh
        dec BlN
.room:  lda BlN
        asl a
        asl a
        asl a
        tax
        lda BlX
        sta BlTab,x
        lda BlY
        sta BlTab+2,x
        lda CurBits
        sta BlTab+4,x
        phx
        lda BlFrames
        jsr Random
        plx
        sta BlTab+6,x
        inc BlN
.o:     rts

; DrawView, before the objects: the blood in view; what is out of view is gone.
DrawBlood:
        lda BlN
        beq .o
        jsr ViewCorner
        ldx #0
.l:     txa
        lsr a
        lsr a
        lsr a
        cmp BlN
        bcs .o
        lda BlTab+4,x
        cmp CurBits
        bne .gone
        phx
        lda BlTab+2,x
        tay
        lda BlTab,x
        tax
        jsr CellOnScreen
        plx
        bcc .gone
        lda BlTab+6,x
        clc
        adc BlFirst
        sta SprId
        phx
        jsr DrawSprite
        plx
        txa
        clc
        adc #8
        tax
        bra .l
.gone:  phx                     ; out of view (or another map): gone
.m:     lda BlTab+8,x
        sta BlTab,x
        inx
        inx
        cpx #15*8
        bcc .m
        plx
        dec BlN
        bra .l
.o:     rts

; Look at CurX, CurY with nothing else there: carry set (A = its name) if blood lies there.
BloodHere:
        ldx #0
.l:     txa
        lsr a
        lsr a
        lsr a
        cmp BlN
        bcs .no
        lda BlTab,x
        cmp CurX
        bne .n
        lda BlTab+2,x
        cmp CurY
        bne .n
        lda BlTab+4,x
        cmp CurBits
        bne .n
        lda BlName
        sec
        rts
.n:     txa
        clc
        adc #8
        tax
        bra .l
.no:    clc
        rts

BlFirst:  .dw $FFFF
BlFrames: .dw 0
BlName:   .dw $FFFF
BlN:      .dw 0
BlX:      .dw 0
BlY:      .dw 0
BlTab:    .ds 16*8+8
