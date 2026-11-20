; Sandcastle RPG Maker - Apple IIgs player: the party, as the Windows Ultima 6 engine (U6GameWindow.Joining.cs, party
; members following the main character). Join / Leave Party (talk op 8) moves a character's 64 bytes into PartyTab
; (its map's record marked gone, 4+8) and back. Members walk in a line behind the Avatar (each to the cell the one
; before left), come along to other maps, strike foes within reach on each turn; foes strike the nearest of the party.
; A member's cell: 4 in the cell map (the Avatar walks into it - they swap round as it goes on; foes are stopped).
; 0-9: the portrait view shows the Avatar (0) or member n - their portrait, name and stats. A member's +58: the map it
; came from, +60: its place in that map's list.

P_MAP = 58
P_IDX = 60
W_PARTYFULL = 45

; X, Y: carry set if a member stands there (ItemPtr = it).
PartyAt:
        stx PAX
        sty PAY
        lda #PartyTab
        sta ItemPtr
        lda MyBank
        sta ItemPtr+2
        ldx PartyN
        beq .no
.l:     lda [ItemPtr]
        cmp PAX
        bne .n
        ldy #2
        lda [ItemPtr],y
        cmp PAY
        bne .n
        sec
        rts
.n:     lda ItemPtr
        clc
        adc #64
        sta ItemPtr
        dex
        bne .l
.no:    clc
        rts

; MP = member A
MemberAt:
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        clc
        adc #PartyTab
        sta MP
        lda MyBank
        sta MP+2
        rts

; Sets (A = 4) or clears the member bit of MP's cell.
OccMember:
        sta OccBit
        lda [MP]
        tax
        ldy #2
        lda [MP],y
        tay
        jsr OccAt
        sep #$20
        .a8
        lda OccBit
        beq .clr
        lda [Ptr]
        ora #4
        sta [Ptr]
        bra .d
.clr:   lda [Ptr]
        and #$FB
        sta [Ptr]
.d:     rep #$20
        .a16
        rts

; The cell map: every member's cell.
OccParty:
        stz PK
.l:     lda PK
        cmp PartyN
        bcs .d
        jsr MemberAt
        lda #4
        jsr OccMember
        inc PK
        bra .l
.d:     rts

; The Avatar stepped from OldX, OldY: each member to the cell the one before it left.
FollowParty:
        lda OldX
        sta FX
        lda OldY
        sta FY
        stz PK
.l:     lda PK
        cmp PartyN
        bcs .d
        jsr MemberAt
        lda [MP]                ; already there (it was standing still behind)
        cmp FX
        bne .go
        ldy #2
        lda [MP],y
        cmp FY
        beq .d
.go:    lda #0
        jsr OccMember
        lda [MP]
        sta T4
        ldy #2
        lda [MP],y
        sta T5
        lda FX                  ; facing the way it goes
        sec
        sbc T4
        sta SDX
        lda FY
        sec
        sbc T5
        sta SDY
        jsr FaceMP
        lda FX
        sta [MP]
        ldy #2
        lda FY
        sta [MP],y
        lda T4
        sta FX
        lda T5
        sta FY
        lda #4
        jsr OccMember
        inc PK
        bra .l
.d:     rts

; MP faces SDX, SDY: 0 south, 1 north, 2 east, 3 west.
FaceMP:
        lda SDX
        beq .v
        bmi .w
        lda #2
        bra .f
.w:     lda #3
        bra .f
.v:     lda SDY
        bmi .n
        lda #0
        bra .f
.n:     lda #1
.f:     ldy #8
        sta [MP],y
        rts

; After going to a map (or a spot): every member on a free cell round the Avatar.
PlaceParty:
        stz PK
.l:     lda PK
        cmp PartyN
        bcc .one
        rts
.one:   jsr MemberAt
        lda #0                  ; off its old cell
        jsr OccMember
        ldx #0
.r:     lda PX                  ; the rings round the Avatar
        clc
        adc RingX,x
        sta FX
        lda PY
        clc
        adc RingY,x
        sta FY
        phx
        ldx FX
        ldy FY
        jsr MapTile
        cmp #$FFFF
        beq .no
        tay
        lda [FlagsPtr],y
        and #$0001
        beq .no
        ldx FX
        ldy FY
        jsr OccAt
        bne .no
        plx
        bra .put
.no:    plx
        inx
        inx
        cpx #48
        bcc .r
        lda PX                  ; nowhere free: on the Avatar's cell
        sta FX
        lda PY
        sta FY
.put:   jsr MemberAt
        lda FX
        sta [MP]
        ldy #2
        lda FY
        sta [MP],y
        lda #4
        jsr OccMember
        inc PK
        bra .l

; Draws the members in the view.
DrawParty:
        jsr ViewCorner
        stz PK
.l:     lda PK
        cmp PartyN
        bcs .d
        jsr MemberAt
        lda [MP]
        tax
        ldy #2
        lda [MP],y
        tay
        jsr CellOnScreen
        bcc .n
        ldy #C_FLAGS            ; knocked out: its body look (bodies.s)
        lda [MP],y
        and #32
        beq .up
        ldy #C_NAME
        lda [MP],y
        jsr BodySprite
        cmp #$FFFF
        bne .bs
.up:    ldy #8
        lda [MP],y
        ldy #4
        clc
        adc [MP],y
.bs:    sta SprId
        jsr DrawSprite
.n:     inc PK
        bra .l
.d:     rts

; CP (a hostile one): DefP = whom it goes for - the Avatar or a member nearer; T4 = how far.
NearestFoe:
        lda #AvRec
        sta DefP
        lda MyBank
        sta DefP+2
        jsr DistCP
        sta T4
        lda DefP                ; invisible: not seen (ailments.s)
        ldx DefP+2
        jsr AilsOf
        and #32
        beq .seen
        lda #99
        sta T4
.seen:  lda AvDown                ; knocked out: those awake first (bodies.s)
        beq .s2
        lda #99
        sta T4
.s2:    stz PK
.l:     lda PK
        cmp PartyN
        bcs .d
        jsr MemberAt
        lda [CP]
        sec
        sbc [MP]
        jsr Abs
        sta T5
        ldy #2
        lda [CP],y
        sec
        sbc [MP],y
        jsr Abs
        cmp T5
        bcc .m
        sta T5
.m:     ldy #C_FLAGS            ; a knocked-out member: those awake first
        lda [MP],y
        and #32
        bne .n
        lda MP                  ; an invisible member: not seen
        ldx MP+2
        jsr AilsOf
        and #32
        bne .n
        lda T5
        cmp T4
        bcs .n
        sta T4
        lda MP
        sta DefP
        lda MP+2
        sta DefP+2
.n:     inc PK
        bra .l
.d:     lda T4                  ; no one awake: the knocked-out Avatar after all
        cmp #99
        bne .r
        lda AvDown
        beq .r
        lda #AvRec
        sta DefP
        lda MyBank
        sta DefP+2
        jsr DistCP
        sta T4
.r:     rts

; Each member strikes a hostile character within its reach.
PartyTurn:
        stz PK
.l:     lda PK
        cmp PartyN
        bcc .one
        rts
.one:   jsr MemberAt
        ldy #C_FLAGS
        lda [MP],y
        and #4+32               ; dead or knocked out: no blow
        bne .n
        lda ChrPtr
        sta CP
        lda ChrPtr+2
        sta CP+2
        lda ChrCount
        sta TurnN
.c:     lda TurnN
        beq .n
        ldy #C_FLAGS
        lda [CP],y
        and #2+4+32             ; (a knocked-out foe: left alone)
        cmp #2
        bne .cn
        lda [CP]                ; how far from the member
        sec
        sbc [MP]
        jsr Abs
        sta T5
        ldy #2
        lda [CP],y
        sec
        sbc [MP],y
        jsr Abs
        cmp T5
        bcc .m
        sta T5
.m:     ldy #C_REACH
        lda [MP],y
        bne .r
        lda #1
.r:     cmp T5
        bcc .cn
        lda MP                  ; it strikes
        sta AtkP
        lda MP+2
        sta AtkP+2
        lda CP
        sta DefP
        lda CP+2
        sta DefP+2
        jsr Strike
        bra .n
.cn:    jsr NextCP
        dec TurnN
        bra .c
.n:     inc PK
        bra .l

; Members killed leave the party.
PruneParty:
        stz PK
.l:     lda PK
        cmp PartyN
        bcc .one
        rts
.one:   jsr MemberAt
        ldy #C_FLAGS
        lda [MP],y
        and #4
        beq .n
        lda #0
        jsr OccMember
        lda PK
        jsr DropMember
        bra .l
.n:     inc PK
        bra .l

; Member A out of PartyTab (the ones after it move up).
DropMember:
        sta T4
        jsr MemberAt
.m:     lda T4
        inc a
        cmp PartyN
        bcs .d
        ldy #0
.c:     phy
        tya
        clc
        adc #64
        tay
        lda [MP],y
        ply
        sta [MP],y
        iny
        iny
        cpy #64
        bcc .c
        lda MP
        clc
        adc #64
        sta MP
        inc T4
        bra .m
.d:     dec PartyN
        stz ViewIdx
        jmp AilForget           ; their ailments' places moved (ailments.s)

; PARTY in the code: map, character ($FFFF: the one talked to), join or leave.
TalkParty:
        iny
        lda [TalkPtr],y
        sta JMap
        iny
        iny
        lda [TalkPtr],y
        sta JIdx
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta JJoin
        lda TPC
        clc
        adc #6
        sta TPC
        lda JMap
        cmp #$FFFF
        bne .some
        lda TalkRec+2           ; the one talked to: a member already?
        cmp MyBank
        bne .onmap
        lda TalkRec
        sec
        sbc #PartyTab
        cmp #512
        bcs .onmap
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        sta PK
        jsr MemberAt
        ldy #P_MAP
        lda [MP],y
        sta JMap
        ldy #P_IDX
        lda [MP],y
        sta JIdx
        bra .some
.onmap: lda CurMap
        sta JMap
        lda TalkRec
        sec
        sbc ChrPtr
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        sta JIdx
.some:  jsr FindMember          ; carry set: a member (PK)
        lda JJoin
        beq .leave
        bcs .done               ; in the party already
        jsr Join
        bra .done
.leave: bcc .done
        jsr Leave
.done:  jsr DrawView
        jmp RunTalk

; Carry set if the character JMap, JIdx is a member (PK its place).
FindMember:
        stz PK
.l:     lda PK
        cmp PartyN
        bcs .no
        jsr MemberAt
        ldy #P_MAP
        lda [MP],y
        cmp JMap
        bne .n
        ldy #P_IDX
        lda [MP],y
        cmp JIdx
        bne .n
        sec
        rts
.n:     inc PK
        bra .l
.no:    clc
        rts

; Src = the map record of JMap, JIdx (that map loaded). Carry set: not to be had.
JoinRec:
        lda JMap
        cmp MapCount
        bcs .no
        jsr MapOff
        lda MapTab+MT_IN,x
        beq .no
        lda JIdx
        cmp MapTab+MT_CC,x
        bcs .no
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        clc
        adc MapTab+MT_CP,x
        sta Src
        lda MapTab+MT_CP+2,x
        adc #0
        sta Src+2
        clc
        rts
.no:    sec
        rts

; JMap, JIdx joins (the party's most permitting).
Join:
        jsr JoinRec
        bcs .out
        ldy #C_FLAGS
        lda [Src],y
        and #4
        bne .out                ; dead or gone
        lda PartyN              ; the most counts the Avatar (at most 8 members)
        inc a
        cmp PartyMax
        bcs .full               ; "The party is full: Iolo doesn't join."
.room:  lda PartyN
        jsr MemberAt
        lda MP
        sta Dst
        lda MP+2
        sta Dst+2
        jsr Copy64
        ldy #C_FLAGS            ; not hostile, not asleep in the party
        lda [MP],y
        and #$FFED
        sta [MP],y
        ldy #P_MAP
        lda JMap
        sta [MP],y
        ldy #P_IDX
        lda JIdx
        sta [MP],y
        ldy #50                 ; its food (its schedule's place, unused in the party)
        lda #24
        sta [MP],y
        ldy #C_FLAGS            ; its map's record: gone (in the party)
        lda [Src],y
        ora #4+8
        sta [Src],y
        inc PartyN
        lda JMap
        cmp CurMap
        bne .out
        lda [Src]               ; its cell: a member's now
        tax
        ldy #2
        lda [Src],y
        tay
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        and #$FD
        ora #4
        sta [Ptr]
        rep #$20
        .a16
.out:   rts
.full:  ldy #C_NAME
        lda [Src],y
        jsr NameNumber
        lda FmtName
        sta WhoName
        lda FmtName+2
        sta WhoName+2
        lda #W_PARTYFULL
        jsr Tell
        rts

; Member PK (JMap, JIdx) leaves: back into its map's record - here where it stands, or (another map) at its home.
Leave:
        lda PK
        jsr MemberAt
        lda #0
        jsr OccMember
        jsr JoinRec
        bcs .drop
        lda MP
        sta T4
        lda Src                 ; Src <-> Dst for the copy back
        sta Dst
        lda Src+2
        sta Dst+2
        lda MP
        sta Src
        lda MP+2
        sta Src+2
        jsr Copy64
        ldy #C_FLAGS
        lda [Dst],y
        and #$FFF3
        sta [Dst],y
        lda JMap
        cmp CurMap
        beq .here
        ldy #C_HOMEX
        lda [Dst],y
        sta [Dst]
        ldy #C_HOMEY
        lda [Dst],y
        ldy #2
        sta [Dst],y
        bra .drop
.here:  lda [Dst]
        tax
        ldy #2
        lda [Dst],y
        tay
        jsr OccAt
        sep #$20
        .a8
        lda [Ptr]
        ora #2
        sta [Ptr]
        rep #$20
        .a16
.drop:  lda PK
        jmp DropMember

; 64 bytes Src -> Dst
Copy64:
        ldy #0
.c:     lda [Src],y
        sta [Dst],y
        iny
        iny
        cpy #64
        bcc .c
        rts

; 0-9: the portrait view shows the Avatar or member n.
ViewMember:
        sec
        sbc #'0'
        beq .set
        cmp PartyN
        beq .set
        bcs .out
.set:   sta ViewIdx
        lda PanW
        beq .out
        lda #1
        sta PanelView
        jmp DrawPanel
.out:   rts

; ViewP = whose stats show.
ViewPtr:
        lda ViewIdx
        beq .av
        dec a
        jsr MemberAt
        lda MP
        sta ViewP
        lda MP+2
        sta ViewP+2
        rts
.av:    lda PX
        sta AvRec
        lda PY
        sta AvRec+2
        lda #AvRec
        sta ViewP
        lda MyBank
        sta ViewP+2
        rts

; A member shown in the portrait view: its portrait over the Avatar's, its name.
MemberOver:
        lda ViewIdx
        bne .m
        rts
.m:     dec a
        jsr MemberAt
        lda PortW
        beq .name
        lda PortX
        sta RX
        lda PortY
        sta RY
        lda PortW
        sta RW
        lda PortH
        sta RH
        jsr RestoreRect
        ldy #12
        lda [MP],y
        cmp #$FFFF
        beq .name
        sta SprId
        lda PortX
        sta CellX
        lda PortY
        sta CellY
        jsr DrawPic
.name:  lda NameW
        beq .d
        lda ViewIdx
        dec a
        jsr MemberAt
        lda NameX
        sta RX
        lda NameY
        sta RY
        lda NameW
        sta RW
        lda NameH
        sta RH
        jsr RestoreRect
        ldy #C_NAME
        lda [MP],y
        jsr NameNumber
        lda TxtColor
        jsr SetColor
        lda NameX
        pha
        lda NameY
        clc
        adc #8
        pha
        tool $3A04              ; MoveTo
        lda FmtName+2
        pha
        lda FmtName
        pha
        tool $A604              ; DrawCString
.d:     rts

PartyN:   .dw 0
PartyTab: .ds 512               ; 8 members, 64 bytes each
PartyMax: .dw 9
ViewIdx:  .dw 0
TalkRec:  .dd 0
OldX:     .dw 0
OldY:     .dw 0
FX:       .dw 0
FY:       .dw 0
PK:       .dw 0
PAX:      .dw 0
PAY:      .dw 0
OccBit:   .dw 0
JMap:     .dw 0
JIdx:     .dw 0
JJoin:    .dw 0
RingX:    .dw 0,$FFFF,1,0,$FFFF,1,$FFFF,1,0,$FFFE,2,0,$FFFE,2,$FFFE,2,$FFFE,2,$FFFF,1,$FFFF,1,$FFFE,2
RingY:    .dw 1,0,0,$FFFF,1,1,$FFFF,$FFFF,2,0,0,$FFFE,1,1,$FFFF,$FFFF,2,2,2,2,$FFFE,$FFFE,$FFFE,$FFFE
