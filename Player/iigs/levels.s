; Sandcastle RPG Maker - Apple IIgs player: Ultima 6's levels, as the Windows engine (U6GameWindow.U6Levels.cs, Nuvie):
; experience from kills earns a level; with Rise by themselves it is taken at once, else it is only earned - the
; leveling system's "When someone earns a level" events say so (once a level) and a shrine (Meditate At Shrine), Rise A
; Level or a dream on Rest raises it: a level's hit points back (never past level x hits per level, nor the most), the
; points (never past the stat limit), told in the text area. Rest's events: when the party wakes, and once for each
; member who has earned a level. Their variables: LevelWhoId, LevelNow, LevelEarned, LevelRose, RestHours; {LevelWho},
; {RestGuard} in words. Scene Text / Scene Choice in the text area; words with {formulas}.

W_GAINS     = 79
W_NOTREADY  = 80
W_STRENGTH  = 81
W_DEXTERITY = 82
W_INTEL     = 83

ParseLevels:
        lda [Ptr]
        sta RiseBy
        ldy #2
        lda [Ptr],y
        sta StatCap
        ldx #0                  ; the 5 variables
        ldy #4
.v:     lda [Ptr],y
        sta LvVars,x
        iny
        iny
        inx
        inx
        cpx #10
        bcc .v
        lda [Ptr],y             ; the Avatar's id
        sta AvId
        tya
        clc
        adc #2
        jsr AddPtr
        lda [Ptr]               ; the events: trigger, code
        sta LvEvN
        lda #2
        jsr AddPtr
        ldx #0
        ldy #0
.e:     cpx LvEvN
        bcs .ed
        phx
        txa
        asl a
        tax
        lda [Ptr],y
        sta LvEvT,x
        iny
        iny
        lda [Ptr],y
        sta LvEvC,x
        iny
        iny
        plx
        inx
        bra .e
.ed:    tya
        jsr AddPtr
        lda [Ptr]               ; characters' ids by name
        sta CIdN
        lda #2
        jsr AddPtr
        lda Ptr
        sta CIdPtr
        lda Ptr+2
        sta CIdPtr+2
        lda CIdN
        asl a
        asl a
        jsr AddPtr
        stz NotN
        jmp ParseScript         ; (texts.s)

; Carry set if WP is the Avatar or a member.
WPInParty:
        jsr WPIsAvatar
        bcs .o
        lda WP+2
        cmp MyBank
        bne .no
        lda WP
        sec
        sbc #PartyTab
        cmp #512
        bcs .no
        sec
.o:     rts
.no:    clc
        rts

; Carry set if WP has earned a level (below the highest, more experience than its step).
HasEarned:
        ldy #C_LEVEL
        lda [WP],y
        beq .no
        cmp LvlCount
        beq .ok
        bcs .no
.ok:    dec a
        asl a
        tax
        ldy #C_EXP
        lda [WP],y
        cmp LvlNeeds,x
        beq .no
        bcc .no
        sec
        rts
.no:    clc
        rts

; Experience gained by WP: a level taken at once (Rise by themselves), or only noticed - When someone earns a level.
ExpGained:
        lda LvlOn
        bne .on
        jmp HpChanged
.on:    lda RiseBy
        beq .earned
.rise:  jsr HasEarned
        bcc .done
        stz RStr
        stz RDex
        stz RInt
        lda LvlHits
        sta RHits
        lda #1
        sta RTell
        jsr RiseOne
        ldy #C_LEVEL            ; When someone earns a level: risen
        lda [WP],y
        sta LEarn
        lda #1
        sta LRose
        lda #21
        jsr QueueLevel
        bra .rise
.earned: jsr HasEarned
        bcc .done
        jsr Noticed
        sta T4
        ldy #C_LEVEL
        lda [WP],y
        inc a
        cmp T4
        beq .done
        bcc .done
        sta LEarn               ; noticed now: once a level
        jsr SetNoticed
        stz LRose
        lda #21
        jsr QueueLevel
.done:  jmp HpChanged

; A = the level noticed for WP (0 none yet).
Noticed:
        ldx #0
.l:     cpx NotN
        bcs .none
        lda NotLo,x
        cmp WP
        bne .n
        lda NotHi,x
        cmp WP+2
        bne .n
        lda NotLv,x
        rts
.n:     inx
        inx
        bra .l
.none:  lda #0
        rts
; WP's noticed level = A.
SetNoticed:
        sta T5
        ldx #0
.l:     cpx NotN
        bcs .new
        lda NotLo,x
        cmp WP
        bne .n
        lda NotHi,x
        cmp WP+2
        beq .set
.n:     inx
        inx
        bra .l
.new:   cpx #32
        bcs .o
        lda WP
        sta NotLo,x
        lda WP+2
        sta NotHi,x
        inc NotN
        inc NotN
.set:   lda T5
        sta NotLv,x
.o:     rts

; The leveling events with trigger A, queued for WP (LEarn, LRose).
QueueLevel:
        sta LTrig
        ldx #0
.l:     cpx LvEvN
        bcs .o
        phx
        txa
        asl a
        tax
        lda LvEvT,x
        cmp LTrig
        bne .n
        lda LvEvC,x
        tay
        lda WP
        sta QNA
        lda WP+2
        sta QNC
        lda LRose
        beq .r
        lda #$8000
.r:     ora LEarn
        sta QNB
        lda LTrig
        ldx #$FFFE
        jsr QueueEvent
.n:     plx
        inx
        bra .l
.o:     rts

; A leveling event about to run: its variables from the queue's first entry.
LevelVarsNow:
        lda QA
        sta WP
        lda QC
        sta WP+2
        jsr WPName
        lda WhoName
        sta LvName
        lda WhoName+2
        sta LvName+2
        jsr WhoId               ; LevelWhoId
        ldx #0
        jsr SetLvVar
        ldy #C_LEVEL            ; LevelNow
        lda [WP],y
        ldx #2
        jsr SetLvVar
        lda QB                  ; LevelEarned, LevelRose
        and #$7FFF
        ldx #4
        jsr SetLvVar
        lda QB
        rol a
        lda #0
        rol a
        ldx #6
        jmp SetLvVar
; The variable LvVars,x = A (if the game has it).
SetLvVar:
        pha
        lda LvVars,x
        cmp #$FFFF
        beq .no
        asl a
        tax
        pla
        jsr SetVarNum
        rts
.no:    pla
        rts

; A = WP's character id (the Avatar's, or by its name).
WhoId:
        jsr WPIsAvatar
        bcc .c
        lda AvId
        rts
.c:     ldy #C_NAME
        lda [WP],y
        sta T4
        lda Src
        pha
        lda Src+2
        pha
        lda CIdPtr
        sta Src
        lda CIdPtr+2
        sta Src+2
        ldx CIdN
        ldy #0
.l:     cpx #0
        beq .none
        lda [Src],y
        cmp T4
        beq .got
        iny
        iny
        iny
        iny
        dex
        bra .l
.got:   iny
        iny
        lda [Src],y
        bra .o
.none:  lda #0
.o:     tax
        pla
        sta Src+2
        pla
        sta Src
        txa
        rts

; One level up for WP: RStr, RDex, RInt points (never past the stat limit), RHits back, told if RTell.
RiseOne:
        ldy #C_LEVEL
        lda [WP],y
        inc a
        sta T4
        lda LvlCount
        inc a
        cmp T4
        bcs .l
        sta T4
.l:     lda T4
        sta [WP],y
        stz GainN
        lda RStr
        ldy #C_STR
        ldx #W_STRENGTH
        jsr RaiseStat
        lda RDex
        ldy #C_DEX
        ldx #W_DEXTERITY
        jsr RaiseStat
        lda RInt
        ldy #C_INT
        ldx #W_INTEL
        jsr RaiseStat
        ldy #C_LEVEL            ; the most hit points: level x hits a level, never past the most
        lda [WP],y
        sta MulA
        lda LvlHits
        sta MB0
        jsr Mul16
        lda MulR
        cmp LvlMost
        bcc .m
        lda LvlMost
.m:     ldy #C_MAXHP
        sta [WP],y
        lda RHits
        bpl .h
        lda #0
.h:     ldy #C_HP
        clc
        adc [WP],y
        ldy #C_MAXHP
        cmp [WP],y
        bcc .hp
        lda [WP],y
.hp:    ldy #C_HP
        sta [WP],y
        ldy #C_LEVEL
        lda [WP],y
        jsr SetNoticedMax
        lda RTell
        beq .o
        jsr WPName              ; "Avatar is now level 2!"
        ldx #0
        ldy #C_LEVEL
        lda [WP],y
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
        lda #W_NEWLEVEL
        jsr Tell
        lda GainN               ; "Avatar gains 3 strength."
        beq .o
        jsr JoinGains
        jsr WPName
        lda #GainBuf
        sta FmtName
        lda MyBank
        sta FmtName+2
        lda #W_GAINS
        jsr Tell
.o:     rts

; Noticed = the larger of it and A.
SetNoticedMax:
        sta T5
        jsr Noticed
        cmp T5
        bcs .o
        lda T5
        jmp SetNoticed
.o:     rts

; WP's stat at Y up by A (signed; between 0 and the stat limit); a gain "n word" (word X) kept if it moved.
RaiseStat:
        cmp #0
        bne .go
        rts
.go:    sta RBy
        stx RWord
        sty RSY
        lda [WP],y
        sta RWas
        clc
        adc RBy
        bpl .p
        lda #0
.p:     cmp StatCap
        bcc .s
        lda StatCap
.s:     ldy RSY
        sta [WP],y
        sec
        sbc RWas
        beq .o
        bpl .pos
        eor #$FFFF              ; (a loss: its size)
        inc a
.pos:   ldx GainN               ; this gain: the figure, a space, the stat's word
        sta GainV,x
        lda RWord
        sta GainW,x
        inx
        inx
        stx GainN
.o:     rts

; GainBuf = the gains: "3 strength", "1 strength and 1 dexterity", "1 strength, 1 dexterity, 1 intelligence".
JoinGains:
        stz GLen
        ldx #0
.l:     cpx GainN
        bcs .end
        cpx #0
        beq .first
        lda GainN               ; two: " and ", more: ", "
        cmp #4
        bne .comma
        lda #AndTxt
        bra .sep
.comma: lda #CommaTxt
.sep:   phx
        jsr AppendMine
        plx
.first: phx
        lda GainV,x
        ldx #0
        jsr Decimal
        sep #$20
        .a8
        lda #' '
        sta StatBuf,x
        lda #0
        sta StatBuf+1,x
        rep #$20
        .a16
        lda #StatBuf
        jsr AppendMine
        plx
        phx
        lda GainW,x
        jsr AppendWord
        plx
        inx
        inx
        bra .l
.end:   rts

; GainBuf += my C string at A.
AppendMine:
        tay
        ldx GLen
        sep #$20
        .a8
.l:     lda 0,y
        beq .d
        cpx #70
        bcs .d
        sta GainBuf,x
        inx
        iny
        bra .l
.d:     lda #0
        sta GainBuf,x
        rep #$20
        .a16
        stx GLen
        rts
; GainBuf += word A.
AppendWord:
        asl a
        asl a
        tax
        lda Src
        pha
        lda Src+2
        pha
        lda WordTab,x
        sta Src
        lda WordTab+2,x
        sta Src+2
        ldx GLen
        ldy #0
        sep #$20
        .a8
.l:     lda [Src],y
        beq .d
        cpx #70
        bcs .d
        sta GainBuf,x
        inx
        iny
        bra .l
.d:     lda #0
        sta GainBuf,x
        rep #$20
        .a16
        stx GLen
        pla
        sta Src+2
        pla
        sta Src
        rts

; ---- Rest's leveling events ----
RestLevelEvents:
        lda RestH               ; RestHours
        ldx #8
        jsr SetLvVar
        lda #AvRec              ; When the party wakes from Rest (about the Avatar)
        sta WP
        lda MyBank
        sta WP+2
        stz LEarn
        stz LRose
        lda #22
        jsr QueueLevel
        stz RSlot               ; once for each member (not the guard) who has earned a level
.l:     lda RSlot
        cmp PartyN
        beq .m
        bcs .o
.m:     cmp RGuard
        beq .n
        jsr SlotWP
        jsr HasEarned
        bcc .n
        ldy #C_LEVEL
        lda [WP],y
        inc a
        sta LEarn
        stz LRose
        lda #23
        jsr QueueLevel
.n:     inc RSlot
        bra .l
.o:     rts

; ---- talk ops ----
; SAYF parts: words, a formula's value, a name; then a new line.
OpSayF:
        iny
.l:     lda [TalkPtr],y
        and #$00FF
        iny
        cmp #1
        beq .txt
        cmp #2
        beq .num
        cmp #3
        beq .name
        sty TPC                 ; the end
        jsr NewLine
        jmp RunTalk
.txt:   sty SfY
        lda TalkPtr
        sta SayP
        lda TalkPtr+2
        sta SayP+2
        jsr SayText
        ldy SfY                 ; past it
.sk:    lda [TalkPtr],y
        iny
        and #$00FF
        bne .sk
        bra .l
.num:   jsr EvalExpr
        sty SfY
        ldx PTag                ; a text: as it is
        cpx #1
        bne .nn
        jsr TxtAddr
        txa
        clc
        adc #TextHeap
        sta SayP
        lda MyBank
        sta SayP+2
        brl .say
.nn:    ldx #0
        cmp #$8000              ; below 0: a minus
        bcc .pos
        eor #$FFFF
        inc a
        pha
        sep #$20
        .a8
        lda #'-'
        sta StatBuf
        rep #$20
        .a16
        pla
        ldx #1
.pos:   jsr Decimal
        sep #$20
        .a8
        lda #0
        sta StatBuf,x
        rep #$20
        .a16
        lda #StatBuf
        sta SayP
        lda MyBank
        sta SayP+2
        bra .say
.name:  lda [TalkPtr],y
        and #$00FF
        iny
        sty SfY
        cmp #1
        bne .g
        lda LvName
        sta SayP
        lda LvName+2
        sta SayP+2
        bra .say
.g:     lda RGName
        sta SayP
        lda RGName+2
        sta SayP+2
.say:   ldy #0
        jsr SayText
        ldy SfY
        brl .l

; RISE who flags str dex int hits notready
OpRise:
        jsr ReadWho
        lda [TalkPtr],y
        and #$00FF
        sta RFlags
        iny
        jsr EvalExpr
        sta RStr
        jsr EvalExpr
        sta RDex
        jsr EvalExpr
        sta RInt
        jsr EvalExpr
        sta RHits
        sty RNotAt
.sk:    lda [TalkPtr],y
        iny
        and #$00FF
        bne .sk
        sty TPC
        lda RFlags
        and #8
        beq .h
        lda LvlHits
        sta RHits
.h:     lda RFlags
        and #4
        sta RTell
        stz RAny
        lda RFlags
        and #1
        beq .one
        stz RSlot               ; every member alive
.pl:    lda RSlot
        cmp PartyN
        beq .pm
        bcs .after
.pm:    jsr SlotWP
        ldy #C_FLAGS
        lda RSlot
        bne .alive
        lda AvDead
        bne .pn
        bra .try
.alive: lda [WP],y
        and #4
        bne .pn
.try:   jsr TryRise
.pn:    inc RSlot
        bra .pl
.one:   jsr WhoRec
        bcs .after
        jsr TryRise
.after: lda RAny
        bne .done
        lda RFlags              ; no one ready
        and #16
        beq .own
        lda #AvRec
        sta WP
        lda MyBank
        sta WP+2
        jsr WPName
        lda #W_NOTREADY
        jsr Tell
        bra .done
.own:   ldy RNotAt
        lda [TalkPtr],y
        and #$00FF
        beq .done
        lda TalkPtr
        sta SayP
        lda TalkPtr+2
        sta SayP+2
        jsr SayText
        jsr NewLine
.done:  jsr HpChanged
        jmp RunTalk

; WP up a level if it may (not at the highest; earned, if asked).
TryRise:
        ldy #C_LEVEL
        lda [WP],y
        cmp LvlCount
        beq .ok
        bcs .no
.ok:    lda RFlags
        and #2
        beq .go
        jsr HasEarned
        bcc .no
.go:    jsr RiseOne
        lda #1
        sta RAny
.no:    rts

; PICK variable count letters: the answer's number or first letter.
OpPick:
        iny
        lda [TalkPtr],y
        asl a
        sta PkVar
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta PkN
        iny
        sty PkLet
        tya
        clc
        adc PkN
        sta PkPC
        lda #Prompt
        jsr PrintMine
        lda #16
        sta Mode
        rts

PickKey:
        sta PkCh
        cmp #'1'
        bcc .let
        sec
        sbc #'0'
        cmp PkN
        beq .num
        bcs .let
.num:   sta PkNum
        bra .got
.let:   lda PkCh                ; a letter: its first letter (either case)
        cmp #'a'
        bcc .u
        cmp #'z'+1
        bcs .u
        and #$DF
.u:     sta T4
        ldy PkLet
        ldx #0
.l:     cpx PkN
        bcs .o
        lda [TalkPtr],y
        and #$00FF
        cmp T4
        beq .hit
        iny
        inx
        bra .l
.hit:   inx
        stx PkNum
.got:   lda #PkCh
        jsr PrintMine
        jsr NewLine
        lda PkNum
        ldx PkVar
        jsr SetVarNum
        lda #3
        sta Mode
        lda PkPC
        sta TPC
        jmp RunTalk
.o:     rts

AndTxt:   .cstr " and "
CommaTxt: .cstr ", "
RiseBy:   .dw 1
StatCap:  .dw 30
LvVars:   .ds 10
AvId:     .dw 0
LvEvN:    .dw 0
LvEvT:    .ds 64
LvEvC:    .ds 64
CIdN:     .dw 0
CIdPtr:   .dd 0
NotN:     .dw 0
NotLo:    .ds 32
NotHi:    .ds 32
NotLv:    .ds 32
LTrig:    .dw 0
LEarn:    .dw 0
LRose:    .dw 0
LvName:   .dd 0
RGName:   .dd 0
RStr:     .dw 0
RDex:     .dw 0
RInt:     .dw 0
RHits:    .dw 0
RTell:    .dw 0
RFlags:   .dw 0
RAny:     .dw 0
RNotAt:   .dw 0
RBy:      .dw 0
RWord:    .dw 0
RSY:       .dw 0
RWas:     .dw 0
GainN:    .dw 0
GainV:    .ds 8
GainW:    .ds 8
GainBuf:  .ds 72
GLen:     .dw 0
SfY:      .dw 0
PkVar:    .dw 0
PkN:      .dw 0
PkLet:    .dw 0
PkPC:     .dw 0
PkCh:     .dw 0
PkNum:    .dw 0
