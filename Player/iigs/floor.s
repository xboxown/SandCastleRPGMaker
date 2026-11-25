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
        lda TrN                 ; 24 bytes each
        asl a
        asl a
        asl a
        sta T4
        asl a
        clc
        adc T4
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
        adc #24
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
        jsr LeverTurn           ; pressed buttons spring back (lever.s)
        jsr TrapLines           ; shooting traps at whoever stands in their line of fire
        jsr DoorTurn            ; doors that open by themselves (door.s)
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
        lda TrTab+14,y          ; explosive: its blast; shooting: its shot (below)
        cmp #1
        bne .nx
        jmp TrapBlast
.nx:    cmp #2
        bne .gr
        jmp TrapShoot
.gr:    lda TrTab+2,y
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

; ---- explosive and shooting traps (U6GameWindow.Projectiles.cs) ----

; The explosive trap at ItemPtr (TrTab + FzT) blows up: everyone within its radius is hurt; then it is gone if set so.
TrapBlast:
        ldy FzT
        lda TrTab+2,y
        sta FzMin
        lda TrTab+4,y
        sta FzMax
        lda TrTab+16,y
        sta FzRad
        lda [ItemPtr]
        sta FzTX
        ldy #2
        lda [ItemPtr],y
        sta FzTY
        lda ItemPtr
        sta FzSvT
        lda ItemPtr+2
        sta FzSvT+2
        lda #AvRec              ; the one moved
        sta FzWho
        lda MyBank
        sta FzWho+2
        ldx PX
        ldy PY
        jsr .hit
        stz FzK                 ; the members
.m:     lda FzK
        cmp PartyN
        bcs .c
        jsr MemberAt
        lda MP
        sta FzWho
        lda MP+2
        sta FzWho+2
        lda [MP]
        tax
        ldy #2
        lda [MP],y
        tay
        jsr .hit
        inc FzK
        bra .m
.c:     lda ChrPtr              ; everyone else on the map
        sta FzWho
        lda ChrPtr+2
        sta FzWho+2
        lda ChrCount
        sta FzK
.cl:    lda FzK
        beq .done
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
        jsr .hit
.cn:    clc
        lda FzWho
        adc #64
        sta FzWho
        lda FzWho+2
        adc #0
        sta FzWho+2
        dec FzK
        bra .cl
.done:  lda FzSvT
        sta ItemPtr
        lda FzSvT+2
        sta ItemPtr+2
        ldy FzT                 ; removed after it goes off
        lda TrTab+22,y
        beq .stay
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        jsr FreeTrapCell
.stay:  jmp DrawView
.hit:   txa                     ; X, Y within the radius of the blast: FzWho hurt
        sec
        sbc FzTX
        jsr AbsX
        cmp FzRad
        beq .y
        bcs .far
.y:     tya
        sec
        sbc FzTY
        jsr AbsY
        cmp FzRad
        beq .h
        bcs .far
.h:     lda FzWho
        pha
        lda FzWho+2
        pha
        jsr HarmWho
        pla
        sta FzWho+2
        pla
        sta FzWho
.far:   rts

; A trap object gone from the map: its cell no longer blocked by it.
FreeTrapCell:
        lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        and #$FE
        sta [Ptr]
        rep #$20
        .a16
        rts

; The shooting trap at ItemPtr (TrTab + FzT) fires its projectile the way it faces (its +12: 0 south, 1 north, 2 east,
; 3 west) over its range: the first one in the way is hurt; anything solid stops it.
TrapShoot:
        jsr TrapLine            ; FzWho = whom it hits (carry set), FzEX, FzEY where the shot ends
        lda #0
        rol a
        sta FzGot
        ldy FzT                 ; its flight
        lda TrTab+18,y
        cmp #$FFFF
        beq .nofly
        sta MSprite
        lda [ItemPtr]
        sta ShootFrom
        ldy #2
        lda [ItemPtr],y
        sta ShootFrom+2
        lda FzEX
        sta ShootTo
        lda FzEY
        sta ShootTo+2
        lda AtkP
        pha
        lda AtkP+2
        pha
        lda DefP
        pha
        lda DefP+2
        pha
        lda #ShootFrom
        sta AtkP
        lda #ShootTo
        sta DefP
        lda MyBank
        sta AtkP+2
        sta DefP+2
        lda ItemPtr
        sta FzSvT
        lda ItemPtr+2
        sta FzSvT+2
        jsr Fly                 ; (missile.s)
        lda FzSvT
        sta ItemPtr
        lda FzSvT+2
        sta ItemPtr+2
        pla
        sta DefP+2
        pla
        sta DefP
        pla
        sta AtkP+2
        pla
        sta AtkP
.nofly: lda FzGot
        beq .o
        ldy FzT
        lda TrTab+2,y
        sta FzMin
        lda TrTab+4,y
        sta FzMax
        jmp HarmWho
.o:     jmp DrawView

; Along the trap's line (ItemPtr, TrTab + FzT; ItemPtr kept): carry set and FzWho = the first one (the Avatar, a member,
; anyone) within its range; FzEX, FzEY where the shot ends (them, or the last open cell before something solid).
TrapLine:
        lda ItemPtr
        sta FzSvL
        lda ItemPtr+2
        sta FzSvL+2
        ldy #12                 ; its facing: the step
        lda [ItemPtr],y
        and #3
        asl a
        tax
        lda TrDX,x
        sta FzDX
        lda TrDY,x
        sta FzDY
        lda [ItemPtr]
        sta FzEX
        ldy #2
        lda [ItemPtr],y
        sta FzEY
        ldy FzT
        lda TrTab+16,y
        sta FzK
.l:     lda FzK
        bne .step
        brl .stop
.step:  dec FzK
        lda FzEX
        clc
        adc FzDX
        tax
        lda FzEY
        clc
        adc FzDY
        tay
        jsr WrapXY
        stx FzNX
        sty FzNY
        cpx PX                  ; the one moved
        bne .nav
        cpy PY
        bne .nav
        lda #AvRec
        sta FzWho
        lda MyBank
        sta FzWho+2
        brl .got
.nav:   jsr OccAt
        sta FzOcc
        and #4                  ; a member
        beq .nm
        ldx FzNX
        ldy FzNY
        jsr PartyAt             ; (party.s: ItemPtr = it)
        bcc .nm
        lda ItemPtr
        sta FzWho
        lda ItemPtr+2
        sta FzWho+2
        brl .got
.nm:    lda FzOcc
        and #2                  ; anyone else
        beq .nc
        lda WP
        pha
        lda WP+2
        pha
        ldx FzNX
        ldy FzNY
        jsr FindChrAt           ; (moves.s: WP = it)
        bcc .nf
        lda WP
        sta FzWho
        lda WP+2
        sta FzWho+2
        pla
        sta WP+2
        pla
        sta WP
        bra .got
.nf:    pla
        sta WP+2
        pla
        sta WP
.nc:    lda FzOcc               ; something solid: it stops before it
        and #1
        bne .stop
        ldx FzNX
        ldy FzNY
        jsr MapTile
        cmp #$FFFF
        beq .stop
        tay
        lda [FlagsPtr],y
        and #3                  ; ground or water: on it goes
        beq .stop
        lda FzNX
        sta FzEX
        lda FzNY
        sta FzEY
        brl .l
.stop:  jsr .back
        clc
        rts
.got:   lda FzNX
        sta FzEX
        lda FzNY
        sta FzEY
        jsr .back
        sec
        rts
.back:  lda FzSvL
        sta ItemPtr
        lda FzSvL+2
        sta ItemPtr+2
        rts

; Each turn: shooting traps that fire at whoever stands in their line of fire (Trap > Triggered by: line of fire).
TrapLines:
        lda TrN
        bne .go
        rts
.go:    jsr FloorFirst
.l:     jsr FloorNextAll
        bcc .o
        ldy #8
        lda [ItemPtr],y
        jsr TrapNamed
        bcc .l
        lda TrTab+14,x
        cmp #2
        bne .l
        lda TrTab+20,x
        beq .l
        stx FzT
        lda ItemPtr
        sta FzSv3
        lda ItemPtr+2
        sta FzSv3+2
        jsr TrapLine
        bcc .back
        ldx FzT
        jsr Spring              ; its state, sound, then its shot
.back:  lda FzSv3
        sta ItemPtr
        lda FzSv3+2
        sta ItemPtr+2
        bra .l
.o:     rts

; After FloorFirst: every object on this map (not carried, gone or inside) - carry set, ItemPtr, FzIdx.
FloorNextAll:
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
        sec
        rts

TrDX:     .dw 0, 0, 1, $FFFF
TrDY:     .dw 1, $FFFF, 0, 0
ShootFrom: .dw 0, 0
ShootTo:  .dw 0, 0

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
TrTab:    .ds 24*32
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
FzSvT:    .dd 0
FzSvL:    .dd 0
FzSv3:    .dd 0
FzRad:    .dw 0
FzTX:     .dw 0
FzTY:     .dw 0
FzK:      .dw 0
FzGot:    .dw 0
FzEX:     .dw 0
FzEY:     .dw 0
FzNX:     .dw 0
FzNY:     .dw 0
FzDX:     .dw 0
FzDY:     .dw 0
FzOcc:    .dw 0
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
        jsr AddPtr
        jmp ParseLevers         ; (lever.s)

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
