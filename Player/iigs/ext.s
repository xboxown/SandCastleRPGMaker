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

XtJump:   .dw XtEquip,XtPose,XtAtkObj
EXT_COUNT = 3

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
        lda JMap                ; the owner code: $FFFF the Avatar, $8000 + map x 64 + place (owner.s)
        cmp #$FFFF
        beq .own
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        ora JIdx
        ora #$8000
.own:   sta EqOwner
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

AoName:   .dw 0
AoX:      .dw 0
AoY:      .dw 0
AoWho:    .dw 0,0
AoLeft:   .dw 0
AoTick:   .dw 0
AoN:      .dw 0

EqName:   .dw 0
EqOn:     .dw 0
EqOwner:  .dw 0
EqPtr:    .dw 0,0
EqN:      .dw 0
EqSlot:   .dw 0
