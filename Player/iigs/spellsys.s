; Sandcastle RPG Maker - Apple IIgs player: the spell system in use (Tools > Spell Editor; Engine/SpellSystem.cs), as the
; Windows Ultima 6 engine (U6GameWindow.SpellSystems.cs): 0 Ultima 6's spellbook and words (magic.s, as before), 1 The
; Savage Empire's fetish and ingredients (an ingredient Used while a fetish is carried by the party or lies next to the
; Avatar casts its spell), 2 events only (C runs a Common Event). After any spell a Common Event can run, the text
; variable SpellCast holding its name. Learn Spell (EXT 34) and Cast Spell (EXT 35) too; a spellbook that doesn't know all
; its spells (its count's bit 15) knows those learned (LearnTab, saved - the party's).
; GAME.DAT (after the breakables): style, on-cast code, after-cast code, the Cast line, the no-fetish line, uses mana,
; SpellCast's variable; u16 fetishes (names); u16 recipes, each (ingredient name, spell, used up).

ParseSpellSys:
        ldy #0
.h:     lda [Ptr],y             ; the seven words
        sta SsStyle,y
        iny
        iny
        cpy #14
        bcc .h
        lda #14
        jsr AddPtr
        lda [Ptr]               ; the fetishes (at most 8)
        sta SsT
        lda #2
        jsr AddPtr
        lda SsT
        cmp #9
        bcc .f
        lda #8
.f:     sta SsFetN
        asl a
        sta SsFetN2
        ldy #0
.fl:    cpy SsFetN2
        bcs .fd
        lda [Ptr],y
        sta SsFet,y
        iny
        iny
        bra .fl
.fd:    lda SsT
        asl a
        jsr AddPtr
        lda [Ptr]               ; the recipes (at most 16): 6 bytes each
        sta SsT
        lda #2
        jsr AddPtr
        lda SsT
        cmp #17
        bcc .r
        lda #16
.r:     sta SsRecN
        jsr Times6
        sta SsRecN6
        ldy #0
.rl:    cpy SsRecN6
        bcs .rd
        lda [Ptr],y
        sta SsRec,y
        iny
        iny
        bra .rl
.rd:    lda SsT
        jsr Times6
        jmp AddPtr

; A x 6
Times6:
        asl a
        sta SsT6
        asl a
        clc
        adc SsT6
        rts

; C in a system that isn't the spellbook's (magic.s StartCast, "Cast-" written): carry set if it was handled here.
SsStartCast:
        lda SsStyle
        bne .other
        clc
        rts
.other: jsr NewLine
        cmp #1
        bne .ev
        lda SsCastLine          ; "Use an ingredient with thy fetish."
        jsr BdSay
        jsr PromptLine
        sec
        rts
.ev:    lda SsOnCast            ; events only: its Common Event
        cmp #$FFFF
        beq .none
        tay
        lda #18
        ldx #$FFFE
        jsr QueueEvent
        jsr PromptLine
        jsr RunQueue
        sec
        rts
.none:  lda #W_NOSPELLS
        jsr PrintSaid
        jsr PromptLine
        sec
        rts

; A spell was cast (SpBuf): the system's After Cast event, SpellCast = its name.
SsAfterCast:
        lda SsAfter
        cmp #$FFFF
        beq .o
        lda SsVar
        cmp #$FFFF
        beq .q
        lda SpBuf               ; its name into the text variable
        jsr NameNumber
        lda FmtName
        sta GFar
        lda FmtName+2
        sta GFar+2
        jsr FarToTemp           ; (texts.s)
        pha
        lda SsVar
        asl a
        tax
        pla
        jsr StoreVar
.q:     ldy SsAfter
        lda #18
        ldx #$FFFE
        jsr QueueEvent          ; (run after this turn - combat.s AfterTurn)
.o:     rts

; Use on a carried thing (verbs.s, GridRec at ItemPtr): carry set if it was an ingredient of the fetish system.
FetishUse:
        lda SsStyle
        cmp #1
        beq .f
.no:    clc
        rts
.f:     ldy #8                  ; its recipe
        lda [ItemPtr],y
        sta SsName
        ldx #0
.l:     cpx SsRecN6
        bcs .no
        lda SsRec,x
        cmp SsName
        beq .got
        txa
        clc
        adc #6
        tax
        bra .l
.got:   lda SsRec+2,x
        sta SsSpell
        lda SsRec+4,x
        sta SsUsed
        stz Mode
        jsr NewLine
        jsr SsFetishNear
        bcs .near
        lda SsNoFet             ; no fetish
        jsr BdSay
        jsr PromptLine
        sec
        rts
.near:  lda SsSpell             ; the spell into SpBuf
        jsr SpellAt
        ldy #0
.cp:    lda [ItemPtr],y
        sta SpBuf,y
        iny
        iny
        cpy #64
        bcc .cp
        lda SsMana
        beq .free
        lda AvRec+C_LEVEL       ; its circle and mana, if this system asks them
        cmp SpBuf+12
        bcs .lv
        lda #W_LEVELLOW
        bra .refuse
.lv:    lda AvRec+C_MANA
        cmp SpBuf+14
        bcs .mp
        lda #W_NOMANA
        bra .refuse
.mp:    sec
        sbc SpBuf+14
        sta AvRec+C_MANA
.free:  lda SsUsed              ; the ingredient used up
        beq .keep
        lda GridRec
        sta Src
        lda GridRec+2
        sta Src+2
        ldy #6
        lda [Src],y
        ora #32
        sta [Src],y
.keep:  jsr RefreshInventory
        lda #1
        sta TurnUsed
        stz ItemCast
        jsr CastSpBuf
        sec
        rts
.refuse: jsr PrintSaid
        jsr PromptLine
        sec
        rts

; Carry set if a fetish is carried by the party, or lies within a cell of the Avatar.
SsFetishNear:
        stz CtI
.l:     lda CtI
        cmp ObjCount
        bcs .no
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        and #32+256
        bne .n
        ldy #8                  ; one of the fetishes?
        lda [ItemPtr],y
        ldx #0
.f:     cpx SsFetN2
        bcs .n
        cmp SsFet,x
        beq .fet
        inx
        inx
        bra .f
.fet:   ldy #6
        lda [ItemPtr],y
        and #64
        beq .map
        jsr PartyOwns           ; carried: the party's (owner.s)
        bcs .y
        bra .n
.map:   lda [ItemPtr],y
        and #$F000
        cmp CurBits
        bne .n
        lda [ItemPtr]
        sec
        sbc PX
        jsr AbsX
        cmp #2
        bcs .n
        ldy #2
        lda [ItemPtr],y
        sec
        sbc PY
        jsr AbsY
        cmp #2
        bcc .y
.n:     inc CtI
        bra .l
.no:    clc
        rts
.y:     sec
        rts

; The spell in SpBuf, paid for: its words said, the After Cast event, then its target asked for ("On whom?") or it works.
CastSpBuf:
        jsr SsAfterCast
        lda SpBuf+2             ; its words said
        jsr NameNumber
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        lda TxtColor
        jsr SetColor
        jsr PrintWord
        jsr NewLine
        lda SpBuf+16            ; a target: "On whom?"
        beq .self
        lda #W_ONWHOM
        jsr PrintSaid
        lda #VERB_CAST
        sta Verb
        lda PX
        sta CurX
        lda PY
        sta CurY
        lda #4
        sta Mode
        jmp DrawView
.self:  lda PX
        sta CastX
        lda PY
        sta CastY
        jsr HasDamage           ; no target: damage hits the foes round the Avatar, anything else the caster
        bcc .me
        jsr AreaDamage
        jsr HpChanged
        jmp SpellPrompt
.me:    jsr AvatarWP
        jsr ApplySpell
        jmp SpellPrompt

; Carry set if spell A has been learned (LearnTab: its place + 1).
SsLearned:
        inc a
        ldx #0
.l:     cmp LearnTab,x
        beq .y
        inx
        inx
        cpx #64
        bcc .l
        clc
        rts
.y:     sec
        rts

; EXT 34 LEARN spell (4 bytes): Learn Spell - the party knows it now (a spellbook that knows only what is learned).
XtLearnCmd:
        iny
        lda [TalkPtr],y
        sta SsSpell
        jsr SsLearned
        bcs .done
        ldx #0
.f:     lda LearnTab,x
        beq .put
        inx
        inx
        cpx #64
        bcc .f
        bra .done
.put:   lda SsSpell
        inc a
        sta LearnTab,x
.done:  lda #4
        jmp Next

; EXT 35 CAST spell who (8 bytes): Cast Spell by the Avatar - Ultima 6's checks and costs (the spellbook system), or the
; system's own (its mana if it asks); its words said; on who ($FFFF: on itself, or the foes round it for damage).
XtCastCmd:
        iny
        lda [TalkPtr],y
        sta SsSpell
        jsr ReadWho
        lda SsSpell
        jsr SpellAt
        ldy #0
.cp:    lda [ItemPtr],y
        sta SpBuf,y
        iny
        iny
        cpy #64
        bcc .cp
        lda SsStyle
        bne .sys
        lda AvRec+C_LEVEL       ; the spellbook's: level, mana, reagents
        cmp SpBuf+12
        bcs .fb1
        brl .done
.fb1:
        lda AvRec+C_MANA
        cmp SpBuf+14
        bcs .fb2
        brl .done
.fb2:
        ldx #0
.rg:    lda SpBuf+18,x
        cmp #$FFFF
        beq .rn
        phx
        jsr CountItem
        plx
        cmp SpBuf+20,x
        bcs .fb3
        brl .done
.fb3:
.rn:    inx
        inx
        inx
        inx
        cpx #16
        bcc .rg
        ldx #0
.tk:    lda SpBuf+18,x
        cmp #$FFFF
        beq .tn
        phx
        sta EvName
        lda SpBuf+20,x
        sta GCount
        jsr TakeItems
        plx
.tn:    inx
        inx
        inx
        inx
        cpx #16
        bcc .tk
        bra .pay
.sys:   lda SsMana
        beq .go
        lda AvRec+C_MANA
        cmp SpBuf+14
        bcc .done
.pay:   lda AvRec+C_MANA
        sec
        sbc SpBuf+14
        sta AvRec+C_MANA
.go:    stz ItemCast
        jsr SsAfterCast
        lda SpBuf+2             ; its words
        jsr NameNumber
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        lda TxtColor
        jsr SetColor
        jsr PrintWord
        jsr NewLine
        lda JMap                ; on whom
        cmp #$FFFF
        bne .who
        lda PX
        sta CastX
        lda PY
        sta CastY
        jsr HasDamage
        bcc .me
        jsr AreaDamage
        bra .fin
.me:    jsr AvatarWP
        jsr ApplySpell
        bra .fin
.who:   jsr WhoRec
        bcs .done
        jsr ApplySpell
.fin:   jsr RefreshInventory
        jsr HpChanged
.done:  lda #8
        jmp Next

SsStyle:  .dw 0                 ; (the seven from GAME.DAT, in this order)
SsOnCast: .dw $FFFF
SsAfter:  .dw $FFFF
SsCastLine: .dw $FFFF
SsNoFet:  .dw $FFFF
SsMana:   .dw 0
SsVar:    .dw $FFFF
SsFetN:   .dw 0
SsFetN2:  .dw 0
SsFet:    .ds 16
SsRecN:   .dw 0
SsRecN6:  .dw 0
SsRec:    .ds 6*16
SsName:   .dw 0
SsSpell:  .dw 0
SsUsed:   .dw 0
SsT:      .dw 0
SsT6:     .dw 0
