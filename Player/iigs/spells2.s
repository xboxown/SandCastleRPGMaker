; Sandcastle RPG Maker - Apple IIgs player: the rest of the spell effects, as the Windows Ultima 6 engine
; (U6GameWindow.Spells.cs): Teleport (the spot), Mark / Recall, Summon (allies that join the party for their seconds),
; Clone (a copy of the one it's cast on), Create Item, Create Field (on the spot and its reach, for its seconds) / Dispel
; Field, Magic Unlock / Lock, Douse / Ignite lights within reach, Telekinesis (the thing used from afar), Resurrect
; (the dead on the spot), Reveal (the invisible seen), Time Stop (everyone but the party stands), Locate, a Common
; Event, Tremor (its damage is Damage's). Traps aren't on the Apple IIgs; Levitate and Translate do nothing here.

W_LOCATE = 71

ParseSpellThings:
        lda [Ptr]               ; what spells make: 10 bytes each
        sta SItemCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta SItemPtr
        lda Ptr+2
        sta SItemPtr+2
        lda SItemCount
        asl a
        sta T4
        asl a
        asl a
        clc
        adc T4
        jsr AddPtr
        lda [Ptr]               ; what they summon: 64 bytes each
        sta SumCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta SumPtr
        lda Ptr+2
        sta SumPtr+2
        lda SumCount
        xba
        lsr a
        lsr a
        jsr AddPtr
        lda [Ptr]               ; common events' code
        sta SCodeCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta SCodePtr
        lda Ptr+2
        sta SCodePtr+2
        ldx SCodeCount
.c:     cpx #0
        beq .d
        lda [Ptr]
        clc
        adc #2
        jsr AddPtr
        dex
        bra .c
.d:     rts

; Carry set if the spell (SpBuf) works on a square: teleport, a field, a door, lights, telekinesis, resurrect, dispel.
CellSpell:
        ldx #0
.l:     lda SpBuf+34,x
        ldy #0
.k:     cmp CellKinds,y
        beq .yes
        iny
        iny
        cpy #18
        bcc .k
        txa
        clc
        adc #10
        tax
        cpx #20
        bcc .l
        clc
        rts
.yes:   sec
        rts

; The other effects (EKind on WP, the spot CastX, CastY).
MoreEffect:
        lda EKind
        sec
        sbc #16
        bcc .o
        cmp #28
        bcs .o
        asl a
        tax
        jmp (EffJump,x)
.o:     rts

EffJump:  .dw ETeleport,EMark,ERecall,ESummon,ECreate,EField,EUnlock,ELock,EDouse,EIgnite,ENone,ENone,ETele
          .dw ENone,ENone,ENone,EResurrect,EClone,EReveal,ETimeStop,ENone,ELocate,EDispel,ECommon,ENone,ENone,ENone,ENone
ENone:  rts

ETeleport:
        lda #$FFFF
        sta GoMap
        lda CastX
        sta GoX
        lda CastY
        sta GoY
        jsr ChangeMap
        jmp DrawView

EMark:
        lda CurMap
        sta MarkMap
        lda PX
        sta MarkX
        lda PY
        sta MarkY
        lda #1
        sta MarkSet
        rts

ERecall:
        lda MarkSet
        beq .o
        lda CurMap
        sta FR                  ; (the map before)
        lda MarkMap
        sta GoMap
        lda MarkX
        sta GoX
        lda MarkY
        sta GoY
        jsr ChangeMap
        lda CurMap
        cmp FR
        beq .same
        jsr NewMapEvents        ; another map: its arrival
.same:  jmp DrawView
.o:     rts

; Summon: EMin..EMax of the kind (EThing) join the party for ESecs (their P_MAP $FFFF, their time +62 in half seconds).
ESummon:
        lda EThing
        cmp SumCount
        bcs .o
        xba
        lsr a
        lsr a                   ; x 64
        clc
        adc SumPtr
        sta Src
        lda SumPtr+2
        adc #0
        sta Src+2
        lda EAmt                ; how many (at least 1)
        bne .n
        lda #1
.n:     sta SumN
.l:     lda SumN
        beq .d
        jsr JoinCopy
        bcc .d
        dec SumN
        bra .l
.d:     jsr PlaceParty
        jmp DrawView
.o:     rts

; Clone: a copy of the one it's cast on joins for ESecs.
EClone:
        jsr WPIsAvatar
        bcs .o
        lda WP
        sta Src
        lda WP+2
        sta Src+2
        jsr JoinCopy
        jsr PlaceParty
        jmp DrawView
.o:     rts

; The 64 bytes at Src into the party, summoned (carry clear: no room).
JoinCopy:
        lda PartyN
        inc a
        cmp PartyMax
        bcs .no
        cmp #9
        bcs .no
        lda PartyN
        jsr MemberAt
        lda MP
        sta Dst
        lda MP+2
        sta Dst+2
        jsr Copy64
        ldy #C_FLAGS
        lda [MP],y
        and #$FFE9              ; not hostile, not asleep, not dead
        ora #1
        sta [MP],y
        ldy #P_MAP
        lda #$FFFF
        sta [MP],y
        ldy #P_IDX
        sta [MP],y
        lda ESecs
        bne .s
        lda #60
.s:     asl a
        ldy #62
        sta [MP],y
        lda PX
        sta [MP]
        ldy #2
        lda PY
        sta [MP],y
        inc PartyN
        sec
        rts
.no:    clc
        rts

; Create Item: EAmt (at least 1) of the item (EThing) given to the Avatar.
ECreate:
        jsr ItemAt
        bcc .o
        lda EAmt
        bne .n
        lda #1
.n:     sta SumN
.l:     lda SumN
        beq .d
        jsr SpareObject
        bcc .d
        jsr FillSpare
        ldy #6
        lda GFlags
        ora #64
        sta [ItemPtr],y
        ldy #14
        lda #$FFFF
        sta [ItemPtr],y
        dec SumN
        bra .l
.d:     jmp RefreshInventory
.o:     rts

; Create Field: the item on the spot and every square of the spell's reach, for ESecs.
EField:
        jsr ItemAt
        bcc .o
        lda SpBuf+54
        sta FR
        lda CastY
        sec
        sbc FR
        sta FY0
.y:     lda CastX
        sec
        sbc FR
        sta FX0
.x:     jsr SpareObject
        bcc .d
        jsr FillSpare
        lda FX0
        sta [ItemPtr]
        ldy #2
        lda FY0
        sta [ItemPtr],y
        ldy #6
        lda GFlags
        and #$0FFF
        ora CurBits
        sta [ItemPtr],y
        ldy #14
        lda #$FFFF
        sta [ItemPtr],y
        jsr AddField
        inc FX0
        lda CastX
        clc
        adc FR
        cmp FX0
        bcs .x
        inc FY0
        lda CastY
        clc
        adc FR
        cmp FY0
        bcs .y
.d:     jsr MakeOccAgain
        jmp DrawView
.o:     rts

; The item EThing's 10 bytes into GName.. (carry set if there is one).
ItemAt:
        lda EThing
        cmp SItemCount
        bcs .no
        asl a
        sta T4
        asl a
        asl a
        clc
        adc T4
        tay
        lda SItemPtr
        sta Src
        lda SItemPtr+2
        sta Src+2
        lda [Src],y
        sta GName
        iny
        iny
        lda [Src],y
        sta GSprite
        iny
        iny
        lda [Src],y
        sta GIcon
        iny
        iny
        lda [Src],y
        sta GFlags
        iny
        iny
        lda [Src],y
        sta GOther
        sec
        rts
.no:    clc
        rts

; ItemPtr = a spare object (gone, no name). Carry clear: none left.
SpareObject:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T5
        beq .no
.l:     ldy #6
        lda [ItemPtr],y
        and #32
        beq .n
        ldy #8
        lda [ItemPtr],y
        cmp #$FFFF
        bne .n
        sec
        rts
.n:     lda #16
        jsr NextItem
        dec T5
        bne .l
.no:    clc
        rts

; The spare at ItemPtr becomes the item (GName...).
FillSpare:
        ldy #4
        lda GSprite
        sta [ItemPtr],y
        ldy #8
        lda GName
        sta [ItemPtr],y
        ldy #10
        lda GOther
        sta [ItemPtr],y
        ldy #12
        lda GIcon
        sta [ItemPtr],y
        rts

; The field at ItemPtr kept for ESecs (32 at most).
AddField:
        ldx #0
.l:     cpx #64
        bcs .o
        lda FieldT,x
        beq .put
        inx
        inx
        bra .l
.put:   jsr IndexOf
        sta FieldI,x
        lda ESecs
        bne .s
        lda #30
.s:     asl a
        sta FieldT,x
.o:     rts

; A field gone: its object back to a spare (gone, no name).
FieldGone:
        lda FieldI,x
        phx
        jsr ObjAt
        plx
        ldy #6
        lda #32
        sta [ItemPtr],y
        ldy #8
        lda #$FFFF
        sta [ItemPtr],y
        stz FieldT,x
        rts

; Dispel Field: the fields within reach of the spot.
EDispel:
        ldx #0
.l:     cpx #64
        bcs .d
        lda FieldT,x
        beq .n
        lda FieldI,x
        phx
        jsr ObjAt
        plx
        lda [ItemPtr]
        sec
        sbc CastX
        jsr Abs
        cmp SpBuf+54
        beq .y
        bcs .n
.y:     ldy #2
        lda [ItemPtr],y
        sec
        sbc CastY
        jsr Abs
        cmp SpBuf+54
        beq .g
        bcs .n
.g:     jsr FieldGone
.n:     inx
        inx
        bra .l
.d:     jsr MakeOccAgain
        jmp DrawView

; The cell map made again (things appeared or went).
MakeOccAgain:
        lda OccHandle+2
        pha
        lda OccHandle
        pha
        tool $1002              ; DisposeHandle
        jmp MakeOcc

; A door or chest on the spot: unlocked (magically locked ones too) / locked.
EUnlock:
        jsr ThingOnSpot
        bcc .o
        ldy #6
        lda [ItemPtr],y
        and #$F7F7              ; not locked, not magically locked
        sta [ItemPtr],y
.o:     rts
ELock:
        jsr ThingOnSpot
        bcc .o
        ldy #6
        lda [ItemPtr],y
        and #16                 ; (an open door can't be locked)
        bne .o
        lda [ItemPtr],y
        ora #8
        sta [ItemPtr],y
.o:     rts

; ItemPtr = a door or container on the spot (this map). Carry set if found.
ThingOnSpot:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T5
        beq .no
.l:     ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .n
        lda [ItemPtr],y
        and #4+128
        beq .n
        lda [ItemPtr]
        cmp CastX
        bne .n
        ldy #2
        lda [ItemPtr],y
        cmp CastY
        bne .n
        sec
        rts
.n:     lda #16
        jsr NextItem
        dec T5
        bne .l
.no:    clc
        rts

; Douse / Ignite: the lights within reach (3 if none set) of the spot.
EDouse:
        stz LitTo
        bra Lights
EIgnite:
        lda #$0100
        sta LitTo
Lights:
        lda SpBuf+54
        bne .r
        lda #3
.r:     sta FR
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T5
        beq .d
.l:     ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp CurBits
        bne .n
        ldy #10
        lda [ItemPtr],y
        jsr IsLight
        bcc .n
        lda [ItemPtr]
        sec
        sbc CastX
        jsr Abs
        cmp FR
        beq .y
        bcs .n
.y:     ldy #2
        lda [ItemPtr],y
        sec
        sbc CastY
        jsr Abs
        cmp FR
        beq .g
        bcs .n
.g:     ldy #10
        lda [ItemPtr],y
        and #$FEFF
        ora LitTo
        sta [ItemPtr],y
.n:     lda #16
        jsr NextItem
        dec T5
        bne .l
.d:     jmp DrawView

; Telekinesis: the thing on the spot used, as from beside it.
ETele:
        lda CastX
        sta CurX
        lda CastY
        sta CurY
        lda #1
        sta FarUse
        lda #VERB_USE
        sta Verb
        jsr PickCell
        stz FarUse
        rts

; Resurrect: a dead character on the spot lives again (its most hit points).
EResurrect:
        lda ChrPtr
        sta CP
        lda ChrPtr+2
        sta CP+2
        lda ChrCount
        sta T5
        beq .o
.l:     ldy #C_FLAGS
        lda [CP],y
        and #4+8
        cmp #4
        bne .n
        lda [CP]
        cmp CastX
        bne .n
        ldy #2
        lda [CP],y
        cmp CastY
        bne .n
        ldy #C_FLAGS
        lda [CP],y
        and #$FFFB
        sta [CP],y
        ldy #C_MAXHP
        lda [CP],y
        ldy #C_HP
        sta [CP],y
        jsr MakeOccAgain
        jmp DrawView
.n:     jsr NextCP
        dec T5
        bne .l
.o:     rts

; Reveal: no one invisible any more.
EReveal:
        ldy #0
.l:     cpy #24*16
        bcs .d
        lda AilTab+4,y
        and #$FFDF
        sta AilTab+4,y
        tya
        clc
        adc #24
        tay
        bra .l
.d:     jmp DrawView

ETimeStop:
        lda ESecs
        bne .s
        lda #20
.s:     asl a
        sta TimeStop
        rts

; Locate: "You are at x, y."
ELocate:
        ldx #0
        lda PX
        jsr Decimal
        sep #$20
        .a8
        lda #','
        sta StatBuf,x
        inx
        lda #' '
        sta StatBuf,x
        inx
        rep #$20
        .a16
        lda PY
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
        lda #W_LOCATE
        jmp Tell

; A Common Event: its code run (as an event).
ECommon:
        lda EThing
        cmp SCodeCount
        bcs .o
        tax
        lda SCodePtr
        sta TalkPtr
        lda SCodePtr+2
        sta TalkPtr+2
.w:     cpx #0
        beq .got
        lda [TalkPtr]
        clc
        adc #2
        clc
        adc TalkPtr
        sta TalkPtr
        lda TalkPtr+2
        adc #0
        sta TalkPtr+2
        dex
        bra .w
.got:   lda TalkPtr
        clc
        adc #2
        sta TalkPtr
        lda TalkPtr+2
        adc #0
        sta TalkPtr+2
        stz TPC
        lda #1
        sta EvRunning
        sta EvHit
        lda #3
        sta Mode
        jmp RunTalk
.o:     rts

; Withstand death: DefP about to die - healed instead, once. Carry set if so.
Withstands:
        lda DefP
        ldx DefP+2
        jsr AilsOf
        and #A_WD
        beq .no
        lda DefP
        sta WP
        lda DefP+2
        sta WP+2
        lda #A_WD
        jsr AilOff
        ldy #C_MAXHP
        lda [DefP],y
        ldy #C_HP
        sta [DefP],y
        jsr HpChanged
        sec
        rts
.no:    clc
        rts

; CP runs from the Avatar a step. Carry set if it moved.
RunAway:
        lda [CP]                ; the goal: the other way from the Avatar
        asl a
        sec
        sbc PX
        sta GX
        ldy #2
        lda [CP],y
        asl a
        sec
        sbc PY
        sta GY
        jmp StepToGoal

; Every half second: time stop, summoned allies' and fields' times.
SpellTimes:
        lda TimeStop
        beq .sm
        dec TimeStop
.sm:    stz POK                 ; summoned members: gone when their time is up
.l:     lda POK
        cmp PartyN
        bcs .f
        jsr MemberAt
        ldy #P_MAP
        lda [MP],y
        cmp #$FFFF
        bne .n
        ldy #62
        lda [MP],y
        dec a
        sta [MP],y
        bne .n
        lda #0
        jsr OccMember
        lda POK
        jsr DropMember
        jsr DrawView
        bra .l
.n:     inc POK
        bra .l
.f:     ldx #0                  ; fields
.fl:    cpx #64
        bcs .d
        lda FieldT,x
        beq .fn
        dec a
        sta FieldT,x
        bne .fn
        jsr FieldGone
        phx
        jsr MakeOccAgain
        jsr DrawView
        plx
.fn:    inx
        inx
        bra .fl
.d:     rts

CellKinds: .dw 16,21,22,23,24,25,28,32,38
SItemCount: .dw 0
SItemPtr: .dd 0
SumCount: .dw 0
SumPtr:   .dd 0
SCodeCount: .dw 0
SCodePtr: .dd 0
SumN:     .dw 0
FR:       .dw 0
FX0:      .dw 0
FY0:      .dw 0
LitTo:    .dw 0
FarUse:   .dw 0
TimeStop: .dw 0
MarkSet:  .dw 0
MarkMap:  .dw 0
MarkX:    .dw 0
MarkY:    .dw 0
FieldI:   .ds 64
FieldT:   .ds 64
