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

XtJump:   .dw XtEquip,XtPose,XtAtkObj,XtAnim,XtPick,XtTakeC,XtFollow,XtBehav,XtBed,XtShake,XtFade,XtFlash,XtDayNight
          .dw XtCamera,XtCamFollow,XtLook,XtName,XtRes
          .dw XtCreate,XtDelObj,XtDelSelf,XtMoveObj,XtObjLook
          .dw XtCount,XtInside,XtCharges,XtEquipped,XtSetBox
          .dw XtAreaFind,XtAreaDel,XtAreaHurt,XtFindThing,XtThingInfo,XtCombine,XtLearn,XtCast,XtResetStat,XtStopSnd
          .dw XtLight,XtBookText,XtLog,XtKeys,XtHudFrame,XtPortrait,XtStyle,XtScene,XtHud
EXT_COUNT = 47

XtHud:    jmp XtHudCmd          ; EXT 46 HUD: the HUD's pieces and panels (hudlive.s)

XtScene:  jmp XtSceneCmd        ; EXT 45 SCENE: the Ultima 6 tab's scenes (scene.s)

XtStyle:  jmp XtStyleCmd        ; EXT 44 STYLE: Conversation Style (talkstyle.s)

XtHudFrame: jmp XtHudFrameCmd   ; EXT 42 HUDFRAME: Change HUD Frame (actkeys.s)
XtPortrait: jmp XtPortraitCmd   ; EXT 43 PORTRAIT: Change Portrait (actkeys.s)

XtKeys:   jmp XtKeysCmd         ; EXT 41 KEYS: Set Key, Ask For Key, Get Key Name, Reset Keys (actkeys.s)

XtLearn:  jmp XtLearnCmd        ; EXT 34 LEARN (spellsys.s)
XtCast:   jmp XtCastCmd         ; EXT 35 CAST (spellsys.s)

XtBed:    jmp XtBedCmd          ; EXT 8 BED: Sleep On Bed (bed.s)

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
        bcc .fb1
        brl .done
.fb1:
        lda AoName              ; from a variable (Find Thing): its own spot and name
        cmp #$FFFE
        bne .fd
        jsr ThingOfVar
        bcc .done
        lda AoX
        sta AcA
        lda AoY
        sta AcB
.fd:    jsr AoFind
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

; EXT 5 TAKEC who x y name count (12 bytes): Take From Container, as U6GameWindow.Actions.cs TakeFromContainerCommand:
; up to the container at x, y; locked: given up; then that many of the things of that name in it ($FFFF: everything)
; are carried by whoever took them - each one too heavy for them stays inside (moves.s WeightCheck).
XtTakeC:
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
        sta TcName
        iny
        iny
        lda [TalkPtr],y
        sta TcLeft
        jsr JOwner
        sta PkOwner
        jsr WhoRec
        bcc .fb1
        brl .done
.fb1:
        jsr TcBox
        bcs .fb2
        brl .done
.fb2:
        jsr AcBeside            ; up to it (acts.s)
        jsr TcBox
        bcs .fb3
        brl .done
.fb3:
        ldy #6
        lda [ItemPtr],y
        and #8                  ; locked
        bne .done
        jsr IndexOf
        sta TcBoxI
        stz TcI
.l:     lda TcI
        cmp ObjCount
        bcs .end
        jsr ObjAt               ; ItemPtr = object TcI
        ldy #6
        lda [ItemPtr],y
        and #256+32
        cmp #256
        bne .n
        ldy #14
        lda [ItemPtr],y
        cmp TcBoxI
        bne .n
        lda TcName
        cmp #$FFFF
        beq .take
        ldy #8
        lda [ItemPtr],y
        cmp TcName
        bne .n
.take:  lda TcI                 ; too heavy for them: it stays
        ldx PkOwner
        jsr WeightCheck
        bcc .n
        lda TcI
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #$0EFF              ; carried (not worn, not in a bag), theirs
        ora #64
        sta [ItemPtr],y
        ldy #14
        lda PkOwner
        sta [ItemPtr],y
        lda TcName
        cmp #$FFFF
        beq .n
        dec TcLeft
        beq .end
.n:     inc TcI
        bra .l
.end:   jsr RefreshInventory
        jsr Having
.done:  jsr DrawView
        lda #12
        jmp Next

; ItemPtr = the container at AoX, AoY (on this map, not gone): carry set.
TcBox:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta AoN
        beq .no
.l:     ldy #6
        lda [ItemPtr],y
        and #128+32+64+256
        cmp #128
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

; EXT 6 FOLLOW who leader stop (11 bytes): Follow Character, as U6GameWindow.ScriptWorld.cs FollowCommand /
; UpdateFollowers: a map's character (not the Avatar, not a member) walks after its leader (anyone - the Avatar too),
; keeping within 2 cells (combat.s's calm turn: FlStep); stop 1: it stops following. Up to 8 at once; a new map: none.
XtFollow:
        jsr ReadWho
        lda [TalkPtr],y
        sta FlLM
        iny
        iny
        lda [TalkPtr],y
        sta FlLI
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta FlStop
        jsr WhoRec              ; the follower
        bcs .done
        jsr WPIsAvatar
        bcs .done
        jsr WPInParty
        bcs .done
        lda WP
        sta FlF
        lda WP+2
        sta FlF+2
        jsr FlFind              ; X = its place (or a free one)
        lda FlStop
        beq .lead
        cpx #$FFFF
        beq .done
        lda FlTab,x
        cmp FlF
        bne .done
        lda FlTab+2,x
        cmp FlF+2
        bne .done
        stz FlTab+2,x           ; it stops following
        bra .done
.lead:  cpx #$FFFF
        beq .done               ; eight following already
        stx FoX
        lda FlLM
        sta JMap
        lda FlLI
        sta JIdx
        jsr WhoRec              ; the leader
        bcs .done
        lda WP
        cmp FlF
        bne .ok
        lda WP+2
        cmp FlF+2
        beq .done               ; (not itself)
.ok:    ldx FoX
        lda FlF
        sta FlTab,x
        lda FlF+2
        sta FlTab+2,x
        lda WP
        sta FlTab+4,x
        lda WP+2
        sta FlTab+6,x
.done:  lda #11
        jmp Next

; X = FlF's place in FlTab, else a free place, else $FFFF.
FlFind:
        ldx #0
.l:     lda FlTab+2,x
        beq .n
        lda FlTab,x
        cmp FlF
        bne .n
        lda FlTab+2,x
        cmp FlF+2
        beq .y
.n:     txa
        clc
        adc #8
        tax
        cpx #64
        bcc .l
        ldx #0
.f:     lda FlTab+2,x
        beq .y
        txa
        clc
        adc #8
        tax
        cpx #64
        bcc .f
        ldx #$FFFF
.y:     rts

; CP's turn (combat.s, calm): carry set if it is following someone (FlMv 1: it stepped).
FlStep:
        stz FlMv
        ldx #0
.l:     lda FlTab+2,x
        beq .n
        cmp CP+2
        bne .n
        lda FlTab,x
        cmp CP
        beq .y
.n:     txa
        clc
        adc #8
        tax
        cpx #64
        bcc .l
        clc
        rts
.y:     lda FlTab+4,x           ; the leader: where it is (the Avatar: PX, PY)
        sta Src
        lda FlTab+6,x
        sta Src+2
        cmp MyBank
        bne .ch
        lda Src
        cmp #AvRec
        bne .ch
        lda PX
        sta GX
        lda PY
        sta GY
        bra .go
.ch:    ldy #C_FLAGS            ; a dead leader: no more following
        lda [Src],y
        and #4
        beq .alive
        stz FlTab+2,x
        clc
        rts
.alive: lda [Src]
        sta GX
        ldy #2
        lda [Src],y
        sta GY
.go:    lda GX                  ; within 2 cells: no step
        sec
        sbc [CP]
        jsr ShortX
        jsr AbsX
        sta T5
        ldy #2
        lda GY
        sec
        sbc [CP],y
        jsr ShortY
        jsr AbsY
        cmp T5
        bcs .m
        lda T5
.m:     cmp #3
        bcc .stay
        jsr StepToGoal          ; (combat.s)
        bcc .stay
        inc FlMv
.stay:  sec
        rts

; A new map: no one follows.
FlClear:
        ldx #0
.l:     stz FlTab+2,x
        txa
        clc
        adc #8
        tax
        cpx #64
        bcc .l
        rts

FlTab:    .ds 64
FlF:      .dw 0,0
FlLM:     .dw 0
FlLI:     .dw 0
FlStop:   .dw 0
FoX:      .dw 0
FlMv:     .dw 0

; EXT 7 BEHAV who activity (7 bytes): Change Behavior, as U6GameWindow.Talk.cs ChangeBehaviorCommand: a map's character
; (not the Avatar, not a member) now does that activity (schedule.s's: 0 stand, 1 wander, 2 sleep, 3 / 4 pace, 5 loiter,
; 6 / 7 work, $80 sit) where it stands, its schedule left off ($0800 in its flags: ApplySchedules passes it by) - its own
; behaviour kept in S_KEY; $FF: back to its schedule (and its own behaviour) now.
XtBehav:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta BhAct
        jsr WhoRec
        bcc .fb1
        brl .done
.fb1:
        jsr WPIsAvatar
        bcc .fb2
        brl .done
.fb2:
        jsr WPInParty
        bcc .fb3
        brl .done
.fb3:
        lda BhAct
        cmp #$FF
        beq .resume
        ldy #C_FLAGS
        lda [WP],y
        and #$0800
        bne .held
        ldy #C_BEHAV            ; its own behaviour kept (in S_KEY: never $FFFF while held)
        lda [WP],y
        ldy #S_KEY
        sta [WP],y
        ldy #C_FLAGS
        lda [WP],y
        ora #$0800
        sta [WP],y
.held:  ldy #C_BEHAV
        lda #2                  ; scheduled: this activity, here
        sta [WP],y
        lda BhAct
        ldy #S_ACT
        sta [WP],y
        lda [WP]
        ldy #S_TX
        sta [WP],y
        ldy #2
        lda [WP],y
        ldy #S_TY
        sta [WP],y
        ldy #C_FLAGS            ; awake
        lda [WP],y
        and #$FFEF
        sta [WP],y
        bra .done
.resume:
        ldy #C_FLAGS
        lda [WP],y
        and #$0800
        beq .done
        lda [WP],y
        and #$F7FF
        sta [WP],y
        ldy #S_KEY
        lda [WP],y
        ldy #C_BEHAV
        sta [WP],y
        jsr ApplySchedules      ; its schedule's activity for now (schedule.s)
.done:  jsr DrawView
        lda #7
        jmp Next

BhAct:    .dw 0

; EXT 10 FADE out colour ticks (6 bytes): Fade Screen - out (1): the view dissolves into the colour over so many ticks and
; stays covered; in (0): a covered view dissolves back (not covered: nothing). The event waits (fx.s DsRun).
XtFade:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta XfOut
        iny
        lda [TalkPtr],y
        and #$000F
        sta DsCol
        asl a
        asl a
        asl a
        asl a
        tsb DsCol
        iny
        lda [TalkPtr],y
        lsr a                   ; a step: a sixteenth
        lsr a
        lsr a
        lsr a
        bne .p
        inc a
.p:     sta DsPer
        lda XfOut
        beq .in
        lda #1
        sta DsMode
        sta DsHold
        bra .go
.in:    lda DsHold
        beq .done
        lda #2
        sta DsMode
.go:    stz DsK
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta DsTick
        pla
.w:     jsr ClockRun
        jsr DsRun
        lda DsMode
        bne .w
.done:  lda #6
        jmp Next

; EXT 11 FLASH colour ticks (5 bytes): Flash Screen - the view all the colour at once, then back to itself over so many
; ticks while the game goes on (a covered view: no flash).
XtFlash:
        iny
        lda [TalkPtr],y
        and #$000F
        sta XfCol
        iny
        lda [TalkPtr],y
        sta XfT
        lda DsHold
        bne .done
        lda XfCol
        sta DsCol
        asl a
        asl a
        asl a
        asl a
        tsb DsCol
        lda #1
        sta DsMode
        sta DsHold
        jsr DsAll
        lda #2
        sta DsMode
        stz DsK
        lda XfT
        lsr a
        lsr a
        lsr a
        lsr a
        beq .fb1
        brl .p
.fb1:
        inc a
.p:     sta DsPer
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta DsTick
        pla
.done:  lda #5
        jmp Next

; EXT 12 DAYNIGHT level (3 bytes): Change Day/Night - the darkness this level (0 day, 7 dusk, 4 night: look.s DarkLevel)
; till the hour is over, as U6GameWindow.Lighting.cs SetLevelUntilNextHour.
XtDayNight:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta DNOvL
        lda Hour
        sta DNOvH
        jsr DrawView
        lda #3
        jmp Next

; EXT 13 CAMERA back x y ticks (9 bytes): Move Camera, as U6GameWindow.ScriptGame.cs MoveCameraCommand - the view glides
; a cell at a time to x, y (back 1: to the Avatar, then follows it again) over so many ticks; the event waits.
XtCamera:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta CmBack
        iny
        lda [TalkPtr],y
        sta CmTX
        iny
        iny
        lda [TalkPtr],y
        sta CmTY
        iny
        iny
        lda [TalkPtr],y
        sta CmT
        jsr ViewCenter          ; from where the view is now
        lda CvX
        sta CamX
        lda CvY
        sta CamY
        lda #1
        sta CamOn
        lda CmBack
        beq .to
        lda PX
        sta CmTX
        lda PY
        sta CmTY
.to:    lda CmTX                ; the steps: the longer way
        sec
        sbc CamX
        jsr AbsX
        sta CmN
        lda CmTY
        sec
        sbc CamY
        jsr AbsY
        cmp CmN
        bcc .n
        sta CmN
.n:     lda CmN
        beq .there
        lda CmT                 ; each step's ticks
        ldx #0
.dv:    cmp CmN
        bcc .dd
        sec
        sbc CmN
        inx
        bra .dv
.dd:    stx CmPer
.step:  lda CmTX
        sec
        sbc CamX
        jsr Sign
        clc
        adc CamX
        sta CamX
        lda CmTY
        sec
        sbc CamY
        jsr Sign
        clc
        adc CamY
        sta CamY
        jsr DrawView
        lda CmPer
        beq .nw
        jsr XtWait
.nw:    dec CmN
        bne .step
.there: lda CmBack
        beq .hold
        stz CamOn               ; back: the view follows the Avatar again
        bra .done
.hold:  lda CmTX
        sta CamX
        lda CmTY
        sta CamY
.done:  jsr DrawView
        lda #9
        jmp Next

; EXT 14 CAMFOLLOW who (6 bytes): Camera Follows - the view follows that character (the Avatar: back to normal).
XtCamFollow:
        jsr ReadWho
        jsr WhoRec
        bcs .done
        stz CamOn
        jsr WPIsAvatar
        bcs .done
        lda WP
        sta CamRec
        lda WP+2
        sta CamRec+2
        lda #2
        sta CamOn
.done:  jsr DrawView
        lda #6
        jmp Next

; EXT 15 LOOK who own sprite poselooks (10 bytes): Change Look, as U6GameWindow.ScriptWorld.cs ChangeLookCommand - the
; character looks like another (that one's four facings, the editor's - and its sleeping / sitting looks if poselooks 1);
; own 1: its own look again. Its own look is kept in LkTab (saved: by its owner code) the first time it changes.
XtLook:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta LkOwn
        iny
        lda [TalkPtr],y
        sta LkSpr
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta LkPose
        jsr JOwner
        sta LkCode
        jsr WhoRec
        bcs .done
        jsr LkFind              ; X = its entry (carry set), else a free one / $FFFF
        lda LkOwn
        beq .new
        bcc .done               ; never changed
        lda LkTab+2,x           ; its own look back
        ldy #4
        sta [WP],y
        lda LkTab+4,x
        sta LkPose
        stz LkTab,x
        lda LkSpr
        bra .flag
.new:   bcs .set                ; changed already: its own is kept
        cpx #$FFFF
        beq .done               ; (sixteen changed already)
        lda LkCode
        sta LkTab,x
        ldy #4
        lda [WP],y
        sta LkTab+2,x
        ldy #C_FLAGS
        lda [WP],y
        and #$4000
        sta LkTab+4,x
.set:   lda LkSpr
        ldy #4
        sta [WP],y
        lda LkPose
        beq .flag
        lda #$4000
        sta LkPose
.flag:  jsr WPIsAvatar          ; (the Avatar: drawn as a sprite while changed - main.s DrawAvatar)
        bcs .av
        ldy #C_FLAGS
        lda [WP],y
        and #$BFFF
        ora LkPose
        sta [WP],y
        bra .done
.av:    jsr LkAvatar
.done:  jsr DrawView
        lda #10
        jmp Next

; LkAv 1 if the Avatar's look is changed (in LkTab).
LkAvatar:
        lda #$FFFF
        sta LkCode
        stz LkAv
        jsr LkFind
        bcc .o
        inc LkAv
.o:     rts

; X = LkCode's entry in LkTab (carry set); else carry clear and X a free entry ($FFFF none).
LkFind:
        ldx #0
.l:     lda LkTab,x
        cmp LkCode
        beq .y
        txa
        clc
        adc #6
        tax
        cpx #16*6
        bcc .l
        ldx #0
.f:     lda LkTab,x             ; (0: free - the saved area starts empty)
        beq .fr
        txa
        clc
        adc #6
        tax
        cpx #16*6
        bcc .f
        ldx #$FFFF
.fr:    clc
        rts
.y:     sec
        rts

; EXT 16 NAME who name (8 bytes): Change Name - its name (the Avatar's: {1} in words too).
XtName:
        jsr ReadWho
        lda [TalkPtr],y
        sta LkSpr
        jsr WhoRec
        bcs .done
        lda LkSpr
        ldy #C_NAME
        sta [WP],y
        jsr WPIsAvatar
        bcc .done
        jsr AvNameWord
.done:  jsr HpChanged
        lda #8
        jmp Next

; The Avatar's word ({1}, AvatarW) = its name now (AvRec's C_NAME): after Change Name, after a load.
AvNameWord:
        lda AvRec+C_NAME
        cmp #$FFFF
        beq .o
        asl a
        asl a
        tay
        ldx AvatarW
        lda NameTab,y
        sta WordTab,x
        lda NameTab+2,y
        sta WordTab+2,x
.o:     rts

; EXT 17 RES who resurrect where x y hp (14 bytes): Resurrect / Heal Fully, as U6GameWindow.ScriptWorld.cs ResurrectCommand.
; Heal fully (resurrect 0): all its hit points, no ailments, up again if knocked out. Resurrect (1): a dead map character
; (not the Avatar, not a member) lives again where its body lies (where 0) or at x, y - its body gone, what the body held
; its own again - with all its hit points ($FFFF) or hp (at most all).
XtRes:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta RsMode
        iny
        lda [TalkPtr],y
        and #$00FF
        sta RsWhere
        iny
        lda [TalkPtr],y
        sta RsX
        iny
        iny
        lda [TalkPtr],y
        sta RsY
        iny
        iny
        lda [TalkPtr],y
        sta RsHp
        jsr JOwner
        sta RsCode
        jsr WhoRec
        bcc .got
        brl .done
.got:   lda RsMode
        bne .res
        ldy #C_FLAGS            ; heal fully: not the dead
        lda [WP],y
        and #4
        beq .heal
        brl .done
.heal:  ldy #C_MAXHP
        lda [WP],y
        ldy #C_HP
        sta [WP],y
        lda WP                  ; no ailments (ailments.s)
        ldx WP+2
        jsr AilFind
        bcc .na
        lda #0
        sta AilTab+4,y
.na:    jsr UpCheck             ; up again (bodies.s)
        jsr HpChanged
        brl .done
.res:   jsr WPIsAvatar          ; (the Avatar's death ends the game on the IIgs; a member's leaves the party)
        bcc .r1
        brl .done
.r1:    jsr WPInParty
        bcc .r2
        brl .done
.r2:    ldy #C_FLAGS
        lda [WP],y
        and #4
        bne .dead
        brl .done
.dead:  lda #$FFFF              ; its body: the body container where it fell
        sta RsBody
        lda [WP]
        sta AoX
        ldy #2
        lda [WP],y
        sta AoY
        jsr RsFindBody
        bcc .nb
        jsr IndexOf
        sta RsBody
        ldy #6                  ; the body gone
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        lda RsWhere
        bne .nb
        lda [ItemPtr]           ; up where it lies
        sta RsX
        ldy #2
        lda [ItemPtr],y
        sta RsY
.nb:    lda RsWhere             ; where: its body (none: where it fell), or the spot
        bne .at
        lda RsBody
        cmp #$FFFF
        bne .at
        lda AoX
        sta RsX
        lda AoY
        sta RsY
.at:    lda RsX
        sta [WP]
        ldy #2
        lda RsY
        sta [WP],y
        ldy #C_FLAGS            ; alive, awake
        lda [WP],y
        and #$FFFF-4-32-16
        sta [WP],y
        ldy #C_MAXHP            ; its hit points
        lda [WP],y
        sta T4
        lda RsHp
        cmp T4
        bcs .fb1
        brl .hp
.fb1:
        lda T4
.hp:    cmp #1
        bcs .hp1
        lda #1
.hp1:   ldy #C_HP
        sta [WP],y
        lda RsBody              ; what the body held: its own again
        cmp #$FFFF
        beq .nt
        stz TcI
.l:     lda TcI
        cmp ObjCount
        bcs .nt
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #256+32
        cmp #256
        bne .n
        ldy #14
        lda [ItemPtr],y
        cmp RsBody
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #$0FFF-256
        ora #64
        sta [ItemPtr],y
        ldy #14
        lda RsCode
        sta [ItemPtr],y
.n:     inc TcI
        bra .l
.nt:    jsr MakeOccAgain        ; (spells2.s)
.done:  jsr DrawView
        lda #14
        jmp Next

; ItemPtr = a body (BodyNm's container) at AoX, AoY on this map, not gone: carry set.
RsFindBody:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta AoN
        beq .no
.l:     ldy #6
        lda [ItemPtr],y
        and #$F000+32+64+256+128
        sec
        sbc CurBits
        cmp #128
        bne .n
        ldy #8
        lda [ItemPtr],y
        cmp BodyNm
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

; ---- objects (U6GameWindow.ScriptWorld.cs: Create Object, Delete Object, Delete Object Self, Move Object, Change Object Look) ----

; EXT 18 CREATE name sprite icon flags other x y count (17 bytes): that many of the thing on this map at x, y (spare slots:
; gone with no name, as Give Item's).
XtCreate:
        iny
        ldx #0
.r:     lda [TalkPtr],y         ; name, sprite, icon, flags, other, x, y
        sta CoName,x
        iny
        iny
        inx
        inx
        cpx #14
        bcc .r
        lda [TalkPtr],y
        and #$00FF
        sta CoN
        beq .done
.one:   lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta AoN
        beq .done
.l:     ldy #6
        lda [ItemPtr],y
        and #32
        beq .n
        ldy #8
        lda [ItemPtr],y
        cmp #$FFFF
        bne .n
        lda CoX                 ; this slot: the thing, on this map
        sta [ItemPtr]
        ldy #2
        lda CoY
        sta [ItemPtr],y
        ldy #4
        lda CoSpr
        sta [ItemPtr],y
        ldy #6
        lda CoFlags
        and #$0FFF-32-64-256
        ora CurBits
        sta [ItemPtr],y
        ldy #8
        lda CoName
        sta [ItemPtr],y
        ldy #10
        lda CoOther
        sta [ItemPtr],y
        ldy #12
        lda CoIcon
        sta [ItemPtr],y
        ldy #14
        lda #$FFFF
        sta [ItemPtr],y
        dec CoN
        bne .one
        bra .done
.n:     lda #16
        jsr NextItem
        dec AoN
        bne .l
.done:  jsr MakeOccAgain        ; (spells2.s)
        jsr DrawView
        lda #17
        jmp Next

; EXT 19 DELOBJ x y name (8 bytes): the thing of that name at x, y gone for good.
XtDelObj:
        jsr XtThing
        bcc .done
        jsr XtGone
.done:  lda #8
        jmp Next

; EXT 20 DELSELF (2 bytes): the object whose own event this is (the last to queue one - ownev.s SelfRec) gone, unless carried.
XtDelSelf:
        lda SelfRec+2
        beq .done
        lda SelfRec
        sta ItemPtr
        lda SelfRec+2
        sta ItemPtr+2
        ldy #6
        lda [ItemPtr],y
        and #64+256
        bne .done
        jsr XtGone
.done:  lda #2
        jmp Next

; The thing at ItemPtr gone: off its cell, the view again.
XtGone:
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        jsr MakeOccAgain
        jmp DrawView

; Y on the sub byte: AoX, AoY, AoName from x y name; ItemPtr = that thing on this map (carry set).
XtThing:
        iny
        lda [TalkPtr],y
        sta AoX
        iny
        iny
        lda [TalkPtr],y
        sta AoY
        iny
        iny
        lda [TalkPtr],y
        sta AoName
        iny
        iny
        sty XtY
        lda AoName
        cmp #$FFFE
        beq ThingOfVar
        jmp AoFind

; A thing from a variable (Find Thing's handle: its object number, in variable AoX): ItemPtr on it, carry set if it is on
; this map (not gone, carried or inside); AoX, AoY, AoName then its own.
ThingOfVar:
        lda AoX
        asl a
        tax
        lda VarTab,x
        bmi .no
        cmp ObjCount
        bcs .no
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #$F000+32+64+256
        cmp CurBits
        bne .no
        lda [ItemPtr]
        sta AoX
        ldy #2
        lda [ItemPtr],y
        sta AoY
        ldy #8
        lda [ItemPtr],y
        sta AoName
        sec
        rts
.no:    clc
        rts

; EXT 21 MOVEOBJ x y name tox toy (12 bytes): the thing moved to another cell.
XtMoveObj:
        jsr XtThing
        bcc .done
        ldy XtY
        lda [TalkPtr],y
        sta [ItemPtr]
        iny
        iny
        lda [TalkPtr],y
        ldy #2
        sta [ItemPtr],y
        jsr MakeOccAgain
        jsr DrawView
.done:  lda #12
        jmp Next

; EXT 22 OBJLOOK x y name own sprite (11 bytes): Change Object Look - the thing shown with that sprite; own 1: its own again
; (kept in OlTab, saved: its number + 1 (0 free), its own sprite).
XtObjLook:
        jsr XtThing
        bcs .got
        brl .done
.got:   ldy XtY
        lda [TalkPtr],y
        and #$00FF
        sta LkOwn
        iny
        lda [TalkPtr],y
        sta LkSpr
        jsr IndexOf
        inc a
        sta LkCode
        ldx #0                  ; its entry
.f:     lda OlTab,x
        cmp LkCode
        beq .has
        inx
        inx
        inx
        inx
        cpx #64
        bcc .f
        lda LkOwn               ; none: back to its own - nothing to do
        bne .done
        ldx #0
.fr:    lda OlTab,x
        beq .new
        inx
        inx
        inx
        inx
        cpx #64
        bcc .fr
        bra .done
.new:   lda LkCode
        sta OlTab,x
        ldy #4
        lda [ItemPtr],y
        sta OlTab+2,x
        bra .set
.has:   lda LkOwn
        beq .set
        lda OlTab+2,x
        ldy #4
        sta [ItemPtr],y
        stz OlTab,x
        bra .drawn
.set:   lda LkSpr
        ldy #4
        sta [ItemPtr],y
.drawn: jsr DrawView
.done:  lda #11
        jmp Next

; ---- counting what is carried (U6GameWindow: Count Items, Items Inside, Item Charges, Get Equipped Item) ----

; Object CtI (ItemPtr on it): CtOwn = who carries it at the top (through the bags holding it: their owner code; $FFFE on
; the map), carry set if CtBox (an object's number) holds it at some level. ItemPtr is left on object CtI.
CtHolder:
        stz CtIn
        lda CtI
        sta CtJ
        ldx #10                 ; (bags in bags: at most 10 deep)
.up:    lda CtJ
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #256
        beq .top
        ldy #14
        lda [ItemPtr],y
        sta CtJ
        cmp CtBox
        bne .nb
        inc CtIn
.nb:    dex
        bne .up
.top:   ldy #6
        lda [ItemPtr],y
        and #32+64
        cmp #64
        beq .car
        lda #$FFFE
        bra .s
.car:   ldy #14
        lda [ItemPtr],y
.s:     sta CtOwn
        lda CtI
        jsr ObjAt
        lda CtIn
        cmp #1
        rts

; EXT 23 COUNT var name mode who x y (15 bytes): Count Items - how many things of that name (each object one) the one
; (mode 0: carried, in their bags too), the whole party (1) or a container on the map at x, y (2) has: into the variable.
XtCount:
        iny
        lda [TalkPtr],y
        asl a
        sta CtVar
        iny
        iny
        lda [TalkPtr],y
        sta CtName
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta CtMode
        jsr ReadWho
        lda [TalkPtr],y
        sta AoX
        iny
        iny
        lda [TalkPtr],y
        sta AoY
        jsr JOwner
        sta CtCode
        lda #$FFFF
        sta CtBox
        lda CtMode
        cmp #2
        bne .go
        jsr TcBox               ; the container (ext.s)
        bcs .bx
        stz CtN
        bra .put
.bx:    jsr IndexOf
        sta CtBox
.go:    stz CtN
        stz CtI
.l:     lda CtI
        cmp ObjCount
        bcs .put
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #32
        bne .n
        ldy #8
        lda [ItemPtr],y
        cmp CtName
        bne .n
        jsr CtHolder
        lda CtMode
        cmp #2
        bne .nc
        lda CtIn                ; in the container
        beq .n
        bra .yes
.nc:    lda CtOwn
        cmp #$FFFE
        beq .n
        ldx CtMode
        bne .party
        cmp CtCode              ; the one's
        bne .n
        bra .yes
.party: jsr PartyOwnsCode       ; the party's
        bcc .n
.yes:   inc CtN
.n:     inc CtI
        bra .l
.put:   lda CtN
        ldx CtVar
        stz PTag
        jsr StoreVar
        lda #15
        jmp Next

; Carry set if owner code A is the Avatar's or a member's.
PartyOwnsCode:
        cmp #$FFFF
        beq .y
        sta CtJ
        lda MP
        pha
        lda MP+2
        pha
        stz CtK
.l:     lda CtK
        cmp PartyN
        bcs .no
        jsr MemberAt
        jsr MPCode
        cmp CtJ
        beq .y2
        inc CtK
        bra .l
.no:    pla
        sta MP+2
        pla
        sta MP
        clc
        rts
.y2:    pla
        sta MP+2
        pla
        sta MP
.y:     sec
        rts

; EXT 24 INSIDE who bag name var remove (13 bytes): Items Inside - how many things of that name ($FFFF: anything) are in
; a bag of that name the one carries: into the variable; remove 1: they are taken away.
XtInside:
        jsr ReadWho
        lda [TalkPtr],y
        sta InBag
        iny
        iny
        lda [TalkPtr],y
        sta CtName
        iny
        iny
        lda [TalkPtr],y
        asl a
        sta CtVar
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta InRemove
        jsr JOwner
        sta CtCode
        stz CtN
        stz InI                 ; each bag of that name they carry
.b:     lda InI
        cmp ObjCount
        bcs .put
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #32
        bne .nb
        ldy #8
        lda [ItemPtr],y
        cmp InBag
        bne .nb
        lda InI
        sta CtI
        jsr CtHolder
        lda CtOwn
        cmp CtCode
        bne .nb
        lda InI                 ; what is in it (at any level)
        sta CtBox
        stz CtI
.l:     lda CtI
        cmp ObjCount
        bcs .nb
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #32+256
        cmp #256
        bne .n
        lda CtName
        cmp #$FFFF
        beq .any
        ldy #8
        lda [ItemPtr],y
        cmp CtName
        bne .n
.any:   jsr CtHolder
        bcc .n
        inc CtN
        lda InRemove
        beq .n
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
.n:     inc CtI
        bra .l
.nb:    inc InI
        brl .b
.put:   jsr RefreshInventory
        lda CtN
        ldx CtVar
        stz PTag
        jsr StoreVar
        lda #13
        jmp Next

; EXT 25 CHARGES who name op var amountvar (13 bytes): Item Charges - the first thing of that name the one carries (bags
; too): op 0 read, 1 set, 2 add (the amount from amountvar); its charges then into var ($FFFF: none). Charges are the
; IIgs's own count (items.s ChgObj / ChgLeft), the thing's own number of them till first used.
XtCharges:
        jsr ReadWho
        lda [TalkPtr],y
        sta CtName
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta ChOp
        iny
        lda [TalkPtr],y
        sta CtVar
        iny
        iny
        lda [TalkPtr],y
        asl a
        tax
        lda VarTab,x
        sta ChAmt
        jsr JOwner
        sta CtCode
        lda #$FFFF
        sta CtBox
        stz CtI
.l:     lda CtI
        cmp ObjCount
        bcc .one
        brl .done
.one:   jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #32
        bne .n
        ldy #8
        lda [ItemPtr],y
        cmp CtName
        bne .n
        jsr CtHolder
        lda CtOwn
        cmp CtCode
        beq .got
.n:     inc CtI
        bra .l
.got:   stz ChDef               ; its own number of charges (a wand's: its special's)
        jsr IsSpecial           ; (crafting.s)
        bcc .nd
        lda SpRec+3
        and #$00FF
        sta ChDef
.nd:    lda CtI
        ldx ChDef
        jsr ChargeGet           ; (items.s)
        sta ChNow
        lda ChOp
        beq .rd
        cmp #1
        beq .cset
        lda ChNow
        clc
        adc ChAmt
        bpl .cst
        lda #0
        bra .cst
.cset:  lda ChAmt
        bpl .cst
        lda #0
.cst:   sta ChNow
        tax
        lda CtI
        jsr ChargeSet
.rd:    lda CtVar
        cmp #$FFFF
        beq .done
        asl a
        tax
        lda ChNow
        stz PTag
        jsr StoreVar
.done:  lda #13
        jmp Next

; EXT 26 EQUIPPED who slot var namevar n (name, id) x n (12 + 4n bytes): Get Equipped Item - what the one wears in that slot
; (the IIgs's: EquipmentSlot + 1): its Object Editor id (from the list after - the editor's ids by name; -1 nothing) into
; var, its name into namevar ($FFFF: none).
XtEquipped:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta EqSlot
        iny
        lda [TalkPtr],y
        asl a
        sta CtVar
        iny
        iny
        lda [TalkPtr],y
        sta EqNameVar
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta EqIdN
        iny
        sty EqIds
        lda EqIdN
        asl a
        asl a
        clc
        adc #12
        sta EqLen
        lda #$FFFF
        sta EqFound
        jsr JOwner
        sta EqOwner
        lda CurOwner            ; what is in that slot (its owner's - wear.s)
        pha
        lda EqOwner
        sta CurOwner
        lda EqSlot
        jsr WornIn
        pla
        sta CurOwner
        bcc .none
        ldy #8
        lda [ItemPtr],y
        sta EqFound
        ldx EqIdN               ; its id by name
        ldy EqIds
.f:     cpx #0
        beq .none2
        lda [TalkPtr],y
        cmp EqFound
        beq .id
        iny
        iny
        iny
        iny
        dex
        bra .f
.id:    iny
        iny
        lda [TalkPtr],y
        bra .put
.none:  stz EqFound
        dec EqFound
.none2: lda #$FFFF
.put:   ldx CtVar
        stz PTag
        jsr StoreVar
        lda EqNameVar           ; its name
        cmp #$FFFF
        beq .done
        lda EqFound
        cmp #$FFFF
        beq .empty
        jsr NameNumber
        lda FmtName
        sta GFar
        lda FmtName+2
        sta GFar+2
        jsr FarToTemp           ; (texts.s: PTag 1)
        bra .nm
.empty: lda #0
        ldx #1
        stx PTag
.nm:    pha
        lda EqNameVar
        asl a
        tax
        pla
        jsr StoreVar
.done:  lda EqLen
        jmp Next

; EXT 27 SETBOX x y boxname empty name sprite icon flags other count (20 bytes): Set Container Contents, as
; U6GameWindow.ScriptWorld.cs SetContainerContentsCommand - the container of that name at x, y: emptied first if asked
; (everything in it, at any depth, gone); then that many of the thing ($FFFF name: none) put in it (spare slots).
XtSetBox:
        jsr XtThing             ; (x, y, the container's name: ItemPtr on it)
        bcs .got
        brl .done
.got:   jsr IndexOf
        sta CtBox
        ldy XtY
        lda [TalkPtr],y
        and #$00FF
        sta SbEmpty
        iny
        ldx #0
.r:     lda [TalkPtr],y         ; name, sprite, icon, flags, other
        sta CoName,x
        iny
        iny
        inx
        inx
        cpx #10
        bcc .r
        lda [TalkPtr],y
        and #$00FF
        sta CoN
        lda SbEmpty
        beq .put
        stz CtI                 ; empty it: all in it gone
.e:     lda CtI
        cmp ObjCount
        bcs .put
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #32+256
        cmp #256
        bne .en
        jsr CtHolder
        bcc .en
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
.en:    inc CtI
        bra .e
.put:   lda CoName
        cmp #$FFFF
        beq .done
.one:   lda CoN
        beq .done
        stz CtI                 ; a spare slot
.s:     lda CtI
        cmp ObjCount
        bcs .done
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #32
        beq .sn
        ldy #8
        lda [ItemPtr],y
        cmp #$FFFF
        bne .sn
        lda #$FFFF              ; in the container
        sta [ItemPtr]
        ldy #2
        sta [ItemPtr],y
        ldy #4
        lda CoSpr
        sta [ItemPtr],y
        ldy #6
        lda CoFlags
        and #$0FFF-32-64
        ora #256
        sta [ItemPtr],y
        ldy #8
        lda CoName
        sta [ItemPtr],y
        ldy #10
        lda CoOther
        sta [ItemPtr],y
        ldy #12
        lda CoIcon
        sta [ItemPtr],y
        ldy #14
        lda CtBox
        sta [ItemPtr],y
        dec CoN
        bra .one
.sn:    inc CtI
        bra .s
.done:  lda #20
        jmp Next

SbEmpty:  .dw 0

; ---- areas (U6GameWindow.Toughness.cs: Find Objects In Area, Delete Objects In Area, Hurt In Area) ----

; Y on: mode (1: 0 who, 1 a spot, 2 this object), who (4), x, y, cells var: ArX, ArY the centre, ArR the cells (carry
; set: there is one). Y after.
ArCentre:
        lda [TalkPtr],y
        and #$00FF
        sta ArMode
        jsr ReadWho
        lda [TalkPtr],y
        sta ArX
        iny
        iny
        lda [TalkPtr],y
        sta ArY
        iny
        iny
        lda [TalkPtr],y
        asl a
        tax
        lda VarTab,x
        bpl .r
        lda #0
.r:     sta ArR
        iny
        iny
        sty ArYs
        lda ArMode
        cmp #1
        beq .yes
        cmp #2
        bne .who
        lda SelfRec+2           ; this object: where it lies (ownev.s SelfRec)
        beq .no
        lda SelfRec
        sta Src
        lda SelfRec+2
        sta Src+2
        lda [Src]
        sta ArX
        ldy #2
        lda [Src],y
        sta ArY
        bra .yes
.who:   jsr WhoRec
        bcs .no
        jsr WPIsAvatar
        bcc .w
        lda PX
        sta ArX
        lda PY
        sta ArY
        bra .yes
.w:     lda [WP]
        sta ArX
        ldy #2
        lda [WP],y
        sta ArY
.yes:   ldy ArYs
        sec
        rts
.no:    ldy ArYs
        clc
        rts

; Carry set if cell X, Y is within ArR of the centre.
ArNear:
        txa
        sec
        sbc ArX
        jsr AbsX
        cmp ArR
        beq .x
        bcs .no
.x:     tya
        sec
        sbc ArY
        jsr AbsY
        cmp ArR
        beq .y
        bcs .no
.y:     sec
        rts
.no:    clc
        rts

; Each thing on this map in the area, of the name ArName ($FFFF any), lit only if ArLit: ArDo (0 count, 1 gone - at most
; ArMost, 0 all) - ArN how many.
ArThings:
        stz ArN
        stz CtI
.l:     lda CtI
        cmp ObjCount
        bcc .one
        rts
.one:   jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #$F000+32+64+256
        cmp CurBits
        bne .n
        lda ArName
        cmp #$FFFF
        beq .nm
        ldy #8
        cmp [ItemPtr],y
        bne .n
.nm:    lda ArLit
        beq .lt
        ldy #10
        lda [ItemPtr],y
        jsr IsLight             ; (look.s)
        bcc .n
        and #$0100
        beq .n
.lt:    lda [ItemPtr]
        tax
        ldy #2
        lda [ItemPtr],y
        tay
        jsr ArNear
        bcc .n
        inc ArN
        lda ArDo
        beq .n
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        lda ArMost
        beq .n
        cmp ArN
        bne .n
        rts
.n:     inc CtI
        bra .l

; EXT 28 AREAFIND var name lit mode who x y cellsvar (18 bytes): Find Objects In Area - how many things around into the variable.
XtAreaFind:
        iny
        lda [TalkPtr],y
        asl a
        sta CtVar
        iny
        iny
        lda [TalkPtr],y
        sta ArName
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta ArLit
        iny
        jsr ArCentre
        stz ArN
        bcc .put
        stz ArDo
        jsr ArThings
.put:   lda ArN
        ldx CtVar
        stz PTag
        jsr StoreVar
        lda #18
        jmp Next

; EXT 29 AREADEL name lit mostvar mode who x y cellsvar (18 bytes): Delete Objects In Area - those things gone for good.
XtAreaDel:
        iny
        lda [TalkPtr],y
        sta ArName
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta ArLit
        iny
        lda [TalkPtr],y
        asl a
        tax
        lda VarTab,x
        bpl .m
        lda #0
.m:     sta ArMost
        iny
        iny
        jsr ArCentre
        bcc .done
        lda #1
        sta ArDo
        jsr ArThings
        jsr MakeOccAgain
        jsr DrawView
.done:  lda #18
        jmp Next

; EXT 30 AREAHURT amountvar type affects mode who x y cellsvar (17 bytes): Hurt In Area - everyone around but Who takes the
; damage (its type's rule - tough.s Respond), as poison's wound (ailments.s Hurt); affects 1: Who's enemies (hostile ones
; if Who is the Avatar or a member, else the rest), 2: only those with a rule for the type.
XtAreaHurt:
        iny
        lda [TalkPtr],y
        asl a
        tax
        lda VarTab,x
        sta ArAmt
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta ArType
        iny
        lda [TalkPtr],y
        and #$00FF
        sta ArAff
        iny
        jsr ArCentre
        bcs .go
        brl .done
.go:    lda WP                  ; Who (never hurt)
        sta ArBy
        lda WP+2
        sta ArBy+2
        lda ArMode
        beq .bk
        stz ArBy+2              ; (a spot, this object: no Who)
.bk:    stz ArByParty
        lda ArBy+2
        beq .p0
        jsr WPIsAvatar
        bcs .p1
        jsr WPInParty
        bcc .p0
.p1:    inc ArByParty
.p0:    lda ArX                 ; the Avatar
        sta T4
        lda #AvRec
        sta WP
        lda MyBank
        sta WP+2
        lda PX
        sta AvRec
        lda PY
        sta AvRec+2
        jsr ArOne
        stz ArK                 ; the members
.m:     lda ArK
        cmp PartyN
        bcs .c
        jsr MemberAt
        lda MP
        sta WP
        lda MP+2
        sta WP+2
        jsr ArOne
        inc ArK
        bra .m
.c:     lda ChrPtr              ; the map's characters
        sta ArCP
        lda ChrPtr+2
        sta ArCP+2
        lda ChrCount
        sta ArK
.cl:    lda ArK
        beq .d
        lda ArCP
        sta WP
        lda ArCP+2
        sta WP+2
        jsr ArOne
        lda ArCP
        clc
        adc #64
        sta ArCP
        bcc .cn
        inc ArCP+2
.cn:    dec ArK
        bra .cl
.d:     jsr DrawView
.done:  lda #17
        jmp Next

; WP: struck if in the area, alive, not Who, and as Affects says.
ArOne:
        lda WP
        cmp ArBy
        bne .nb
        lda WP+2
        cmp ArBy+2
        bne .nb
        rts
.nb:    ldy #C_FLAGS
        lda [WP],y
        and #4
        beq .alive
        rts
.alive: lda [WP]
        tax
        ldy #2
        lda [WP],y
        tay
        jsr ArNear
        bcs .in
        rts
.in:    lda ArAff
        beq .hit
        cmp #2
        beq .rule
        jsr WPIsAvatar          ; Who's enemies
        bcs .party
        jsr WPInParty
        bcs .party
        ldy #C_FLAGS
        lda [WP],y
        and #2
        beq .friend
        lda ArByParty           ; a hostile one: an enemy of the party's side
        bne .hit
        rts
.friend: lda ArByParty          ; a friendly one: an enemy of a hostile Who
        beq .hit
        rts
.party: lda ArByParty
        beq .hit
        rts
.rule:  ldy #C_NAME             ; only those with a rule for this type
        lda [WP],y
        jsr HasRule
        bcs .hit
        rts
.hit:   lda ArType
        sta HitType
        lda ArAmt
        sta Dmg
        ldy #C_NAME
        lda [WP],y
        jsr Respond             ; (tough.s)
        lda Dmg
        beq .o
        jsr Hurt                ; (ailments.s)
.o:     rts

; Carry set if the name A has a rule for HitType (tough.s TrsTab).
HasRule:
        sta TwNm
        ldx #0
        ldy TrsN
.l:     cpy #0
        beq .no
        lda TrsTab,x
        cmp TwNm
        bne .n
        lda TrsTab+2,x
        cmp ArType
        beq .y
.n:     txa
        clc
        adc #8
        tax
        dey
        bra .l
.no:    clc
        rts
.y:     sec
        rts

; EXT 31 FINDTHING mode who x y name var (15 bytes): Find Thing, as U6GameWindow.FindThing.cs - its handle (the object's
; number; -1 none) into the variable. Mode 0: the body of a dead character (bodies.s BodyNm where it fell); 1: the nearest
; of the thing to someone; 2: the nearest to the spot; 3: the thing at the spot (the one on top: the last there).
XtFindThing:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta FtMode
        jsr ReadWho
        lda [TalkPtr],y
        sta AoX
        iny
        iny
        lda [TalkPtr],y
        sta AoY
        iny
        iny
        lda [TalkPtr],y
        sta FtName
        iny
        iny
        lda [TalkPtr],y
        asl a
        sta CtVar
        lda #$FFFF
        sta FtFound
        lda FtMode
        cmp #2
        bcs .spot
        jsr WhoRec              ; someone: where they are
        bcc .fb2
        brl .put
.fb2:
        jsr WPIsAvatar
        bcc .w
        lda PX
        sta AoX
        lda PY
        sta AoY
        bra .wd
.w:     lda [WP]
        sta AoX
        ldy #2
        lda [WP],y
        sta AoY
.wd:    lda FtMode
        bne .near
        ldy #C_FLAGS            ; the body: only of the dead
        lda [WP],y
        and #4
        bne .fb3
        brl .put
.fb3:
        jsr RsFindBody
        bcs .fb4
        brl .put
.fb4:
        jsr IndexOf
        sta FtFound
        brl .put
.spot:  cmp #3
        beq .top
.near:  lda #$FFFF              ; the nearest of that name on this map
        sta FtBest
        stz CtI
.nl:    lda CtI
        cmp ObjCount
        bcc .fb6
        brl .put
.fb6:
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #$F000+32+64+256
        cmp CurBits
        bne .nn
        ldy #8
        lda [ItemPtr],y
        cmp FtName
        bne .nn
        lda [ItemPtr]
        sec
        sbc AoX
        jsr AbsX
        sta T5
        ldy #2
        lda [ItemPtr],y
        sec
        sbc AoY
        jsr AbsY
        cmp T5
        bcs .mx
        lda T5
.mx:    cmp FtBest
        bcs .nn
        sta FtBest
        lda CtI
        sta FtFound
.nn:    inc CtI
        bra .nl
.top:   stz CtI                 ; the last thing at the spot
.tl:    lda CtI
        cmp ObjCount
        bcs .put
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #$F000+32+64+256
        cmp CurBits
        bne .tn
        lda [ItemPtr]
        cmp AoX
        bne .tn
        ldy #2
        lda [ItemPtr],y
        cmp AoY
        bne .tn
        lda CtI
        sta FtFound
.tn:    inc CtI
        bra .tl
.put:   lda FtFound
        ldx CtVar
        stz PTag
        jsr StoreVar
        lda #15
        jmp Next

; EXT 32 THINGINFO handlevar xvar yvar floorvar namevar (12 bytes): Thing Info - where the thing is and its name ($FFFF:
; not wanted); -1 and an empty name when it isn't on the map any more.
XtThingInfo:
        iny
        lda [TalkPtr],y
        sta AoX                 ; (the handle's variable)
        iny
        iny
        sty FtY
        jsr ThingOfVar
        bcs .on
        lda #$FFFF
        sta AoX
        sta AoY
        sta FtFloor
        stz FtFound             ; (no name)
        bra .w
.on:    lda #1
        sta FtFloor
        lda #1
        sta FtFound
.w:     ldy FtY                 ; x, y, floor
        lda [TalkPtr],y
        ldx AoX
        jsr FtStore
        iny
        iny
        lda [TalkPtr],y
        ldx AoY
        jsr FtStore
        iny
        iny
        lda [TalkPtr],y
        ldx FtFloor
        jsr FtStore
        iny
        iny
        lda [TalkPtr],y         ; its name
        cmp #$FFFF
        beq .done
        asl a
        sta CtVar
        lda FtFound
        beq .empty
        lda AoName
        jsr NameNumber
        lda FmtName
        sta GFar
        lda FmtName+2
        sta GFar+2
        jsr FarToTemp           ; (texts.s: PTag 1)
        bra .nm
.empty: lda #0
        ldx #1
        stx PTag
.nm:    ldx CtVar
        jsr StoreVar
.done:  lda #12
        jmp Next

; Variable A ($FFFF: none) = the number X (Y kept).
FtStore:
        cmp #$FFFF
        beq .o
        phy
        asl a
        sta CtVar
        txa
        ldx CtVar
        stz PTag
        jsr StoreVar
        ply
.o:     rts

; EXT 33 COMBINE who name1 n1 name2 n2 made(name sprite icon flags other) n3 missing (28 bytes): Combine Items, as
; U6GameWindow.MagicItems.cs CombineItemsCommand - the one has n1 of the first and n2 of the second (anywhere they carry
; them; the same thing: n1 + n2): they are used up and n3 (at most 8) of the made thing are theirs; else the missing
; line ($FFFF none) is said and nothing happens.
XtCombine:
        jsr ReadWho
        ldx #0
.r:     lda [TalkPtr],y         ; name1, n1, name2, n2, then the made thing (name sprite icon flags other), n3, missing
        sta CbName1,x
        iny
        iny
        inx
        inx
        cpx #22
        bcc .r
        jsr JOwner
        sta CtCode
        lda CbName1             ; enough of each?
        sta CtName
        jsr CbCount
        sta CbHave1
        lda CbName2
        sta CtName
        jsr CbCount
        sta CbHave2
        lda CbName1
        cmp CbName2
        bne .two
        lda CbN1
        clc
        adc CbN2
        cmp CbHave1
        beq .ok
        bcc .ok
        brl .miss
.two:   lda CbHave1
        cmp CbN1
        bcs .fb2
        brl .miss
.fb2:
        lda CbHave2
        cmp CbN2
        bcs .fb3
        brl .miss
.fb3:
.ok:    lda CbName1             ; used up
        sta CtName
        lda CbN1
        jsr CbTake
        lda CbName2
        sta CtName
        lda CbN2
        jsr CbTake
        lda CbN3                ; the made thing: theirs
        cmp #9
        bcc .g
        lda #8
.g:     sta CoN
        beq .done
        stz CtI
.s:     lda CtI
        cmp ObjCount
        bcs .done
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #32
        beq .sn
        ldy #8
        lda [ItemPtr],y
        cmp #$FFFF
        bne .sn
        lda #$FFFF
        sta [ItemPtr]
        ldy #2
        sta [ItemPtr],y
        ldy #4
        lda CbMSpr
        sta [ItemPtr],y
        ldy #6
        lda CbMFlags
        and #$0FFF-32-256
        ora #64
        sta [ItemPtr],y
        ldy #8
        lda CbMName
        sta [ItemPtr],y
        ldy #10
        lda CbMOther
        sta [ItemPtr],y
        ldy #12
        lda CbMIcon
        sta [ItemPtr],y
        ldy #14
        lda CtCode
        sta [ItemPtr],y
        dec CoN
        beq .done
.sn:    inc CtI
        bra .s
.miss:  lda CbMiss              ; what is missing: said
        cmp #$FFFF
        beq .end
        jsr NameNumber
        lda #W_ECHO
        jsr Format
        bra .end
.done:  jsr RecalcGear
        jsr RefreshInventory
        jsr Having              ; (By Not Having An Item: events.s)
.end:   lda #28
        jmp Next

; A = how many things named CtName CtCode carries (in their bags too).
CbCount:
        stz CtN
        lda #$FFFF
        sta CtBox
        stz CtI
.l:     lda CtI
        cmp ObjCount
        bcs .d
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #32
        bne .n
        ldy #8
        lda [ItemPtr],y
        cmp CtName
        bne .n
        jsr CtHolder
        lda CtOwn
        cmp CtCode
        bne .n
        inc CtN
.n:     inc CtI
        bra .l
.d:     lda CtN
        rts

; A things named CtName that CtCode carries: gone.
CbTake:
        sta CtN
        stz CtI
.l:     lda CtN
        beq .d
        lda CtI
        cmp ObjCount
        bcs .d
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #32
        bne .n
        ldy #8
        lda [ItemPtr],y
        cmp CtName
        bne .n
        jsr CtHolder
        lda CtOwn
        cmp CtCode
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #$0FFF              ; (not worn any more)
        ora #32
        sta [ItemPtr],y
        dec CtN
.n:     inc CtI
        bra .l
.d:     rts

CbName1:  .dw 0
CbN1:     .dw 0
CbName2:  .dw 0
CbN2:     .dw 0
CbMName:  .dw 0
CbMSpr:   .dw 0
CbMIcon:  .dw 0
CbMFlags: .dw 0
CbMOther: .dw 0
CbN3:     .dw 0
CbMiss:   .dw 0
CbHave1:  .dw 0
CbHave2:  .dw 0

; ---- Reset Stat (U6GameWindow.EventCommands.cs ResetStatCommand): Change Stat's changes taken back, newest first ----

; Change Stat moved stat word GWord of JMap, JIdx by A: logged (StLog, saved: owner code, word, by how much - 16 at most,
; the oldest dropped).
StLogAdd:
        sta StBy
        cmp #0
        beq .o
        lda StLogN
        cmp #16
        bcc .room
        ldx #0                  ; full: the oldest out
.sh:    lda StLog+6,x
        sta StLog,x
        inx
        inx
        cpx #15*6
        bcc .sh
        dec StLogN
.room:  lda StLogN
        asl a
        sta T4
        asl a
        clc
        adc T4
        tax
        jsr JOwner
        sta StLog,x
        lda GWord
        sta StLog+2,x
        lda StBy
        sta StLog+4,x
        inc StLogN
.o:     rts

; EXT 36 RESETSTAT who word (7 bytes): every logged change of that stat ($FF: all) of the one taken back, newest first.
XtResetStat:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta StWord
        jsr WhoRec
        bcs .done
        jsr JOwner
        sta StCode
        lda StLogN
        sta StI
.l:     lda StI
        beq .d
        dec StI
        lda StI
        asl a
        sta T4
        asl a
        clc
        adc T4
        tax
        lda StLog,x
        cmp StCode
        bne .l
        lda StWord
        cmp #$FF
        beq .m
        cmp StLog+2,x
        bne .l
.m:     lda StLog+2,x           ; its stat back by what it moved
        asl a
        clc
        adc #16
        tay
        lda [WP],y
        sec
        sbc StLog+4,x
        bpl .p
        lda #0
.p:     sta [WP],y
.sh:    lda StLog+6,x           ; off the log
        sta StLog,x
        inx
        inx
        cpx #15*6
        bcc .sh
        dec StLogN
        bra .l
.d:     jsr HpChanged
.done:  lda #7
        jmp Next

; EXT 37 STOPSND sound (4 bytes): Stop Sound - the sound effects' generators playing that sound ($FFFF: every one) stopped
; (sound.s plays them on four in turn; the music's own generator plays on).
XtStopSnd:
        iny
        lda [TalkPtr],y
        sta StBy
        stz T4                  ; the generators' bits
        ldx #0
.l:     lda StBy
        cmp #$FFFF
        beq .hit
        cmp GenSnd,x
        bne .n
.hit:   lda #$FFFF
        sta GenSnd,x
        txa
        lsr a
        tay
        lda #1
.sh:    cpy #0
        beq .set
        asl a
        dey
        bra .sh
.set:   tsb T4
.n:     inx
        inx
        cpx #8
        bcc .l
        lda T4
        beq .done
        pha
        tool $0F08              ; FFStopSound
.done:  lda #4
        jmp Next

GenSnd:   .dw $FFFF,$FFFF,$FFFF,$FFFF

; EXT 38 LIGHT which x y name do var minutesvar (14 bytes): Light Source, as U6GameWindow.LightCommand.cs - this object
; (which 0: ownev.s SelfRec) or the thing at x, y (name; $FFFE: from a variable) - do 0 light it, 1 put it out, 2 switch,
; 3 burn it out (spent), 4 make it fresh, 5 set its minutes left (minutesvar), 6 read its minutes left, 7 read lit (1 / 0),
; 8 read burnt out (1 / 0) - into var. A light's Other: $0100 lit, $0200 spent (look.s IsLight); minutes left: floor.s BnTab.
XtLight:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta LsWhich
        iny
        lda [TalkPtr],y
        sta AoX
        iny
        iny
        lda [TalkPtr],y
        sta AoY
        iny
        iny
        lda [TalkPtr],y
        sta AoName
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta LsDo
        iny
        lda [TalkPtr],y
        sta LsVar
        iny
        iny
        lda [TalkPtr],y
        asl a
        tax
        lda VarTab,x
        sta LsMin
        lda LsWhich             ; which light
        bne .thing
        lda SelfRec+2
        bne .self
        brl .done
.self:  lda SelfRec
        sta ItemPtr
        lda SelfRec+2
        sta ItemPtr+2
        bra .got
.thing: lda AoName
        cmp #$FFFE
        bne .find
        jsr ThingOfVar
        bra .chk
.find:  jsr AoFind
.chk:   bcs .got
        brl .done
.got:   ldy #10                 ; a light?
        lda [ItemPtr],y
        jsr IsLight
        bcs .lt
        brl .done
.lt:    jsr IndexOf
        sta FzIdx
        ldy #8                  ; its full time (floor.s LtTab: 0 never burns out)
        lda [ItemPtr],y
        jsr LightNamed
        stz FzMin
        bcc .nf
        lda LtTab+2,x
        sta FzMin
.nf:    ldy #10
        lda [ItemPtr],y
        sta LsOther
        lda LsDo
        cmp #2                  ; switch: lit -> out, else lit
        bne .d0
        lda LsOther
        and #$0100
        beq .on
        bra .off
.d0:    cmp #0
        bne .d1
.on:    lda LsOther             ; light it (not a spent one)
        and #$0200
        beq .fb1
        brl .put
.fb1:
        lda LsOther
        ora #$0100
        sta LsOther
        brl .put
.d1:    cmp #1
        bne .d3
.off:   lda LsOther
        and #$FEFF
        sta LsOther
        brl .put
.d3:    cmp #3
        bne .d4
        lda LsOther             ; burnt out
        and #$FEFF
        ora #$0200
        sta LsOther
        bra .put
.d4:    cmp #4
        bne .d5
        lda LsOther             ; fresh: can be lit, its full time
        and #$FDFF
        sta LsOther
        jsr BurnLeft
        bcc .put
        lda FzMin
        sta BnTab+2,x
        bra .put
.d5:    cmp #5
        bne .rd
        jsr BurnLeft            ; its minutes left
        bcc .put
        lda LsMin
        bpl .m
        lda #0
.m:     sta BnTab+2,x
        bra .put
.rd:    cmp #6                  ; reads
        bne .r7
        jsr BurnLeft
        lda FzMin
        bcc .rv
        lda BnTab+2,x
        bra .rv
.r7:    cmp #7
        bne .r8
        lda LsOther
        and #$0100
        beq .rv
        lda #1
        bra .rv
.r8:    lda LsOther
        and #$0200
        beq .rv
        lda #1
.rv:    ldx LsVar
        cpx #$FFFF
        beq .done
        pha
        txa
        asl a
        tax
        pla
        stz PTag
        jsr StoreVar
        bra .done
.put:   lda LsOther
        ldy #10
        sta [ItemPtr],y
        jsr RefreshInventory
        jsr DrawView
.done:  lda #14
        jmp Next

; EXT 39 BOOKTEXT self old new (7 bytes): Change Self / External Book Content in an Ultima 6 project - the book's words from
; now on are text new (compiled by the editor): self 1 - the book whose event runs (ownev.s SelfRec), else every book that
; reads text old. A book's Other: $C000 + its text (read.s IsText); saved with the objects.
XtBookText:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta LsWhich
        iny
        lda [TalkPtr],y
        sta LsMin               ; (old)
        iny
        iny
        lda [TalkPtr],y
        sta LsOther             ; (new)
        lda LsWhich
        beq .all
        lda SelfRec+2
        beq .done
        lda SelfRec
        sta ItemPtr
        lda SelfRec+2
        sta ItemPtr+2
        jsr .set
        bra .done
.all:   stz CtI
.l:     lda CtI
        cmp ObjCount
        bcs .done
        jsr ObjAt
        ldy #10
        lda [ItemPtr],y
        jsr IsText
        bcc .n
        and #$03FF
        cmp LsMin
        bne .n
        jsr .set
.n:     inc CtI
        bra .l
.done:  lda #7
        jmp Next
.set:   ldy #10                 ; a book to read: its new words
        lda [ItemPtr],y
        jsr IsText
        bcc .o
        and #$FC00
        ora LsOther
        sta [ItemPtr],y
.o:     rts

; EXT 40 LOG mode (3 bytes) - list item 29 (the user, 2026-11-25): Debug Log, Pause Here and Show Error on the IIgs.
; mode 0: a line begins - what SAYF prints next goes into LogBuf (talk.s SayText, NewLine), not on the screen;
; mode 1: the line into LOG.TXT in the game's folder (made if it isn't there - a text file - and added at its end);
; mode 2: "Log error generated." in the text area.
XtLog:
        iny
        lda [TalkPtr],y
        and #$00FF
        beq .begin
        cmp #1
        beq .write
        jsr NewLine
        lda #W_LOGERR
        jsr PrintSaid
        bra .done
.begin: lda #1
        sta LogOn
        stz LogLen
        bra .done
.write: stz LogOn
        jsr LogWrite
.done:  lda #3
        jmp Next

; LogBuf (LogLen bytes) and a return into LOG.TXT. Any trouble (no disk room, locked): nothing more.
LogWrite:
        ldx LogLen
        sep #$20
        .a8
        lda #$0D
        sta LogBuf,x
        rep #$20
        .a16
        inc LogLen
        lda #LogName
        jsr LocalPath           ; PathBuf = "9:LOG.TXT"
        gsos $2001,LogCreate    ; made if it isn't there (there already: no matter)
        gsos $2010,OpenWParms
        bcs .x
        lda OpenWRef
        sta LogMarkRef
        sta WriteRef
        sta CloseRef
        gsos $2016,LogMark      ; at its end
        lda #LogBuf
        ldx MyBank
        ldy LogLen
        jsr WriteBlock
        gsos $2014,CloseParms
.x:     rts

; talk.s SayText while a line is being logged: the words added to LogBuf (marks left out, a new line a space).
LogText:
.l:     lda [SayP],y
        and #$00FF
        beq .e
        iny
        cmp #3                  ; a colour and its number
        bne .n3
        iny
        bra .l
.n3:    cmp #6
        bcc .l                  ; (keyword and colour marks)
        cmp #$0A
        bne .add
        lda #' '
.add:   ldx LogLen
        cpx #250
        bcs .l
        sep #$20
        .a8
        sta LogBuf,x
        rep #$20
        .a16
        inc LogLen
        bra .l
.e:     rts

W_LOGERR = 120
LogOn:    .dw 0
LogLen:   .dw 0
LogBuf:   .ds 256
LogName:  .cstr "LOG.TXT"
LogCreate:
        .dw 4
        .dd PathBuf
        .dw $00C3
        .dw $0004               ; a text file
        .dd 0
LogMark:
        .dw 3
LogMarkRef:
        .dw 0
        .dw 1                   ; from the end
        .dd 0

LsWhich:  .dw 0
LsDo:     .dw 0
LsVar:    .dw 0
LsMin:    .dw 0
LsOther:  .dw 0

StOld:    .dw 0
StBy:     .dw 0
StWord:   .dw 0
StCode:   .dw 0
StI:      .dw 0

FtMode:   .dw 0
FtName:   .dw 0
FtFound:  .dw 0
FtBest:   .dw 0
FtY:      .dw 0
FtFloor:  .dw 0

ArMode:   .dw 0
ArX:      .dw 0
ArY:      .dw 0
ArR:      .dw 0
ArYs:     .dw 0
ArName:   .dw 0
ArLit:    .dw 0
ArDo:     .dw 0
ArMost:   .dw 0
ArN:      .dw 0
ArAmt:    .dw 0
ArType:   .dw 0
ArAff:    .dw 0
ArBy:     .dw 0,0
ArByParty: .dw 0
ArK:      .dw 0
ArCP:     .dw 0,0

CtVar:    .dw 0
CtName:   .dw 0
CtMode:   .dw 0
CtCode:   .dw 0
CtBox:    .dw 0
CtN:      .dw 0
CtI:      .dw 0
CtJ:      .dw 0
CtK:      .dw 0
CtIn:     .dw 0
CtOwn:    .dw 0
InBag:    .dw 0
InRemove: .dw 0
InI:      .dw 0
ChOp:     .dw 0
ChAmt:    .dw 0
ChDef:    .dw 0
ChNow:    .dw 0
EqNameVar: .dw 0
EqIdN:    .dw 0
EqIds:    .dw 0
EqLen:    .dw 0
EqFound:  .dw 0

CoName:   .dw 0
CoSpr:    .dw 0
CoIcon:   .dw 0
CoFlags:  .dw 0
CoOther:  .dw 0
CoX:      .dw 0
CoY:      .dw 0
CoN:      .dw 0
XtY:      .dw 0

RsMode:   .dw 0
RsWhere:  .dw 0
RsX:      .dw 0
RsY:      .dw 0
RsHp:     .dw 0
RsCode:   .dw 0
RsBody:   .dw 0

LkOwn:    .dw 0
LkSpr:    .dw 0
LkPose:   .dw 0
LkCode:   .dw 0
LkAv:     .dw 0

CmBack:   .dw 0
CmTX:     .dw 0
CmTY:     .dw 0
CmT:      .dw 0
CmN:      .dw 0
CmPer:    .dw 0

XfOut:    .dw 0
XfCol:    .dw 0
XfT:      .dw 0

; EXT 9 SHAKE pixels ticks wait sound (8 bytes): Shake Screen / Earthquake (fx.s ShakeRun) - the view shaken so many
; pixels each way for so many ticks, its sound once as it starts; wait 1: the event waits till it is over.
XtShake:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta ShAmp
        iny
        lda [TalkPtr],y
        sta ShLeft
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta XaWait
        iny
        lda [TalkPtr],y
        cmp #$FFFF
        beq .q
        ldx #255
        jsr PlaySound
.q:     pha
        pha
        tool $2503              ; GetTick
        pla
        sta ShTick
        pla
        lda XaWait
        beq .done
.w:     jsr ClockRun
        jsr ShakeRun
        lda ShLeft
        bne .w
.done:  lda #8
        jmp Next

TcName:   .dw 0
TcLeft:   .dw 0
TcBoxI:   .dw 0
TcI:      .dw 0

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
