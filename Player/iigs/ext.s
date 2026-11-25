; Sandcastle RPG Maker - Apple IIgs player: talk op 48 EXT - more event commands, each a sub-command (the byte after the
; op), so the first segment's OpJump needn't grow for each one (Engine/AppleIIgsTalk.cs: Ext = 48, the subs in ExtOp).
; Each sub is called with Y on its sub byte (ReadWho reads the who after it, as after an op) and ends with
; "lda #its length (op byte included); jmp Next".

; The op in A (48), Y on it.
OpExt:
        iny
        lda [TalkPtr],y
        and #$00FF
        cmp #EXT_COUNT
        bcs .bad
        asl a
        tax
        jmp (XtJump,x)
.bad:   lda #2                  ; (an older player's unknown sub: its length isn't known - the code ends here)
        jmp EndTalk

XtJump:   .dw XtEquip,XtPose,XtAtkObj,XtAnim,XtPick
EXT_COUNT = 5

; EXT 0 EQUIP who name on (9 bytes): Equip Item (on 1) / Unequip Item (on 0) - as the Windows Ultima 6 engine's
; EquipItemCommand / UnequipItemCommand: one of the thing not worn yet is readied in its own slot (what is there goes
; back into the pack); Unequip puts the worn one back into the pack. Only weapons and armour have slots on the IIgs.
XtEquip:
        jsr ReadWho
        lda [TalkPtr],y
        sta EqName
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta EqOn
        jsr JOwner
        sta EqOwner
        stz EqPtr+2             ; (none found yet)
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta EqN
        beq .look
.l:     ldy #6                  ; carried (not gone, not in a bag), the name, the owner
        lda [ItemPtr],y
        and #96+256
        cmp #64
        bne .n
        ldy #8
        lda [ItemPtr],y
        cmp EqName
        bne .n
        ldy #14
        lda [ItemPtr],y
        cmp EqOwner
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #$F000
        beq .free
        lda EqOn                ; a worn one: Equip - there already; Unequip - back into the pack
        beq .off
        jmp .done
.off:   lda [ItemPtr],y
        and #$0FFF
        sta [ItemPtr],y
        jmp .gear
.free:  lda EqPtr+2             ; the first one not worn
        bne .n
        lda ItemPtr
        sta EqPtr
        lda ItemPtr+2
        sta EqPtr+2
.n:     lda #16
        jsr NextItem
        dec EqN
        bne .l
.look:  lda EqOn
        beq .done               ; Unequip: none worn
        lda EqPtr+2
        beq .done               ; Equip: hasn't got it
        lda EqPtr
        sta Src
        lda EqPtr+2
        sta Src+2
        ldy #10                 ; a weapon (1) or armour (2), and its slot
        lda [Src],y
        cmp #$FFFF
        beq .done
        and #$C000
        beq .done
        lda [Src],y
        xba
        lsr a
        lsr a
        and #$000F
        beq .done
        sta EqSlot
        lda CurOwner            ; what is in that slot (its owner's) back into the pack
        pha
        lda EqOwner
        sta CurOwner
        lda EqSlot
        jsr WornIn
        pla
        sta CurOwner
        bcc .put
        ldy #6
        lda [ItemPtr],y
        and #$0FFF
        sta [ItemPtr],y
.put:   lda EqSlot
        xba
        asl a
        asl a
        asl a
        asl a
        and #$F000
        ldy #6
        ora [Src],y
        sta [Src],y
.gear:  jsr RecalcGear          ; everyone's weapon and armour from what they wear (wear.s)
        jsr RefreshInventory
        jsr HpChanged
.done:  lda #9
        jmp Next

; EXT 1 POSE who n ticks, then n frames (sprite word, ticks byte) - 10 + 3n bytes: Play Pose, as U6GameWindow.Actions.cs
; PlayPoseCommand: the pose's frames (the editor's, fitted) shown in place of its look, frame after frame, for so many
; ticks (0: once through), the game clock going on; then its own look again. The event waits for it, as on Windows.
XtPose:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta PoN
        iny
        lda [TalkPtr],y
        sta PoLeft
        iny
        iny
        sty PoFr                ; the frames' place
        lda PoN
        asl a
        clc
        adc PoN
        clc
        adc #10
        sta PoLen
        jsr WhoRec
        bcc .fb1
        brl .done
.fb1:
        lda PoN
        bne .fb2
        brl .done
.fb2:
        lda PoLeft              ; once through: all the frames' ticks
        bne .go
        ldx PoN
        ldy PoFr
.sum:   iny
        iny
        lda [TalkPtr],y
        and #$00FF
        clc
        adc PoLeft
        sta PoLeft
        iny
        dex
        bne .sum
.go:    lda WP
        sta PoseRec
        lda WP+2
        sta PoseRec+2
        stz PoK
.f:     lda PoK                 ; this frame
        asl a
        clc
        adc PoK
        clc
        adc PoFr
        tay
        lda [TalkPtr],y
        sta PoseSpr
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta PoT
        jsr DrawView
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta PoTick
        pla
.w:     jsr ClockRun            ; (schedule.s)
        pha
        pha
        tool $2503
        pla
        sta T4
        pla
        lda T4
        sec
        sbc PoTick
        cmp PoT
        bcs .next
        cmp PoLeft
        bcc .w
.next:  cmp PoLeft              ; time used
        bcs .end
        lda PoLeft
        sec
        sbc T4
        clc
        adc PoTick
        sta PoLeft
        inc PoK
        lda PoK
        cmp PoN
        bcc .f
        stz PoK
        bra .f
.end:   stz PoseRec+2           ; its own look again
        jsr DrawView
.done:  lda PoLen
        jmp Next

; A = a record's address, X = its bank: carry set and A = the pose's sprite while Play Pose shows it.
PosedLook:
        cpx PoseRec+2
        bne .no
        cmp PoseRec
        bne .no
        lda PoseSpr
        sec
        rts
.no:    clc
        rts

PoseRec:  .dw 0,0
PoseSpr:  .dw 0
PoN:      .dw 0
PoK:      .dw 0
PoT:      .dw 0
PoLeft:   .dw 0
PoTick:   .dw 0
PoFr:     .dw 0
PoLen:    .dw 0

; EXT 2 ATKOBJ who x y name (11 bytes): Attack Object, as U6GameWindow.Actions.cs AttackObjectCommand: up to the thing
; (the object of that name at x, y), then blows (half a second apart, the clock going on) till it breaks (break.s) - a
; minute of blows and it still stands: given up. One that can't be broken: "No effect.".
XtAtkObj:
        jsr ReadWho
        lda [TalkPtr],y
        sta AcA
        sta AoX
        iny
        iny
        lda [TalkPtr],y
        sta AcB
        sta AoY
        iny
        iny
        lda [TalkPtr],y
        sta AoName
        jsr WhoRec
        bcs .done
        jsr AoFind
        bcc .done               ; not there
        jsr BrKindOf
        bcs .walk
        jsr BreakHit            ; (writes "No effect.")
        bra .done
.walk:  lda WP                  ; (AcBeside may move WP's things about: kept)
        sta AoWho
        lda WP+2
        sta AoWho+2
        jsr AcBeside            ; up to it (acts.s)
        lda #120
        sta AoLeft
.blow:  lda AoWho
        sta WP
        lda AoWho+2
        sta WP+2
        jsr AoFind
        bcc .done               ; broken
        ldy #C_STR
        lda [WP],y
        jsr BlowOf
        jsr BreakHit
        bcs .done
        jsr DrawView
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta AoTick
        pla
.w:     jsr ClockRun
        pha
        pha
        tool $2503
        pla
        sec
        sbc AoTick
        sta T4
        pla
        lda T4
        cmp #30
        bcc .w
        dec AoLeft
        bne .blow
.done:  jsr DrawView
        lda #11
        jmp Next

; ItemPtr = the object named AoName at AoX, AoY (on this map, not gone, not carried): carry set.
AoFind:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta AoN
        beq .no
.l:     ldy #6
        lda [ItemPtr],y
        and #32+64+256
        bne .n
        lda [ItemPtr],y
        and #$F000
        cmp CurBits
        bne .n
        ldy #8
        lda [ItemPtr],y
        cmp AoName
        bne .n
        lda [ItemPtr]
        cmp AoX
        bne .n
        ldy #2
        lda [ItemPtr],y
        cmp AoY
        beq .y
.n:     lda #16
        jsr NextItem
        dec AoN
        bne .l
.no:    clc
        rts
.y:     sec
        rts

; EXT 3 ANIM where a b wait n ticks, then n frames (sprite word, ticks byte) - 13 + 3n bytes: Play Animation (fx.s):
; where 0 at the cell a, b; 1 over the character a, b (map, place - following it). The event waits for it unless wait 0.
XtAnim:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta XaWhere
        iny
        lda [TalkPtr],y
        sta XaA
        iny
        iny
        lda [TalkPtr],y
        sta XaB
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta XaWait
        iny
        lda [TalkPtr],y
        and #$00FF
        sta XaN
        iny
        lda [TalkPtr],y
        sta XaTicks
        iny
        iny
        sty XaFr
        lda XaN
        asl a
        clc
        adc XaN
        clc
        adc #13
        sta XaLen
        lda XaN
        bne .fb1
        brl .done
.fb1:
        cmp #9
        bcc .fb2
        brl .done
.fb2:
        lda XaWhere             ; over a character: its record
        beq .slot
        lda XaA
        sta JMap
        lda XaB
        sta JIdx
        jsr WhoRec
        bcc .fb3
        brl .done
.fb3:
.slot:  jsr FxFree
        bcs .fb4               ; four playing already
        brl .done
.fb4:
        stx XaSlot
        lda #1
        sta FxTab,x
        lda XaN
        sta FxTab+2,x
        stz FxTab+4,x
        lda XaTicks
        sta FxTab+8,x
        lda XaWhere
        sta FxTab+10,x
        lda XaA
        sta FxTab+12,x
        lda XaB
        sta FxTab+14,x
        lda WP
        sta FxTab+16,x
        lda WP+2
        sta FxTab+18,x
        txa                     ; its frames
        clc
        adc #24
        sta XaD
        ldx XaN
        ldy XaFr
.f:     lda [TalkPtr],y
        phx
        ldx XaD
        sta FxTab,x
        plx
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        phx
        ldx XaD
        sta FxTab+2,x
        inx
        inx
        inx
        inx
        stx XaD
        plx
        iny
        dex
        bne .f
        ldx XaSlot
        jsr FxFrameTicks
        pha
        pha
        tool $2503              ; GetTick
        pla
        ldx XaSlot
        sta FxTab+6,x
        pla
        jsr DrawView
        lda XaWait
        beq .done
.w:     jsr ClockRun            ; till it is over
        jsr FxRun
        ldx XaSlot
        lda FxTab,x
        bne .w
        jsr DrawView
.done:  lda XaLen
        jmp Next

XaWhere:  .dw 0
XaA:      .dw 0
XaB:      .dw 0
XaWait:   .dw 0
XaN:      .dw 0
XaTicks:  .dw 0
XaFr:     .dw 0
XaLen:    .dw 0
XaSlot:   .dw 0
XaD:      .dw 0

AoName:   .dw 0
AoX:      .dw 0
AoY:      .dw 0
AoWho:    .dw 0,0
AoLeft:   .dw 0
AoTick:   .dw 0
AoN:      .dw 0

; A = the owner code of JMap, JIdx: $FFFF the Avatar, $8000 + map x 64 + place (owner.s).
JOwner:
        lda JMap
        cmp #$FFFF
        beq .o
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ora JIdx
        ora #$8000
.o:     rts

; EXT 4 PICK who x y (8 bytes): Picklock Door, as U6GameWindow.Actions.cs PicklockDoorCommand / PickTheLock: up to the
; door at x, y, then its lockpicks one after another (keys.s's roll: Dexterity against the door's difficulty, 5..95%)
; till it is unlocked ("Unlocked.") - each that fails breaks; none left, or magically locked: given up.
XtPick:
        jsr ReadWho
        lda [TalkPtr],y
        sta AcA
        sta AoX
        iny
        iny
        lda [TalkPtr],y
        sta AcB
        sta AoY
        jsr JOwner
        sta PkOwner
        jsr WhoRec
        bcc .fb2
        brl .done
.fb2:
        jsr PkDoor
        bcs .fb3
        brl .done
.fb3:
        lda WP
        sta AoWho
        lda WP+2
        sta AoWho+2
        jsr AcBeside            ; up to it (acts.s)
.try:   lda AoWho
        sta WP
        lda AoWho+2
        sta WP+2
        jsr PkDoor
        bcc .done
        ldy #6
        lda [ItemPtr],y
        and #8                  ; unlocked already
        beq .done
        lda [ItemPtr],y
        and #2048               ; magically locked: no lockpick opens it
        bne .done
        jsr PkPick              ; a lockpick of theirs
        bcc .done
        ldy #C_DEX              ; the chance
        lda [WP],y
        sec
        ldy #12
        sbc [ItemPtr],y
        sta T5
        asl a
        asl a
        clc
        adc T5
        clc
        adc #50
        bmi .lo
        cmp #5
        bcs .hi
.lo:    lda #5
.hi:    cmp #96
        bcc .c
        lda #95
.c:     sta PkChance
        lda #100
        jsr Random
        cmp PkChance
        bcs .broke
        ldy #6
        lda [ItemPtr],y
        and #$FFF7
        sta [ItemPtr],y
        lda #W_UNLOCKED
        jsr PrintSaid
        bra .done
.broke: ldy #6                  ; the pick breaks
        lda [Src],y
        ora #32
        sta [Src],y
        jsr RefreshInventory
        lda #W_PICKBROKE
        jsr PrintSaid
        jsr DrawView
        lda #90                 ; a try every 1.5 seconds
        jsr XtWait
        brl .try
.done:  jsr DrawView
        lda #8
        jmp Next

; ItemPtr = the door at AoX, AoY (on this map, not gone): carry set.
PkDoor:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta AoN
        beq .no
.l:     ldy #6
        lda [ItemPtr],y
        and #4+32+64+256
        cmp #4
        bne .n
        lda [ItemPtr],y
        and #$F000
        cmp CurBits
        bne .n
        lda [ItemPtr]
        cmp AoX
        bne .n
        ldy #2
        lda [ItemPtr],y
        cmp AoY
        beq .y
.n:     lda #16
        jsr NextItem
        dec AoN
        bne .l
.no:    clc
        rts
.y:     sec
        rts

; Src = a lockpick PkOwner carries: carry set.
PkPick:
        lda ObjPtr
        sta Src
        lda ObjPtr+2
        sta Src+2
        lda ObjCount
        sta AoN
        beq .no
.l:     ldy #6
        lda [Src],y
        and #96+256+1024
        cmp #64+1024
        bne .n
        ldy #14
        lda [Src],y
        cmp PkOwner
        beq .y
.n:     lda Src
        clc
        adc #16
        sta Src
        bcc .nc
        inc Src+2
.nc:    dec AoN
        bne .l
.no:    clc
        rts
.y:     sec
        rts

; A = ticks: waited, the clock going on.
XtWait:
        sta XwN
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta XwT
        pla
.w:     jsr ClockRun
        pha
        pha
        tool $2503
        pla
        sec
        sbc XwT
        sta T4
        pla
        lda T4
        cmp XwN
        bcc .w
        rts

PkOwner:  .dw 0
PkChance: .dw 0
XwN:      .dw 0
XwT:      .dw 0

EqName:   .dw 0
EqOn:     .dw 0
EqOwner:  .dw 0
EqPtr:    .dw 0,0
EqN:      .dw 0
EqSlot:   .dw 0
