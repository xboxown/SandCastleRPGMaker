; Sandcastle RPG Maker - Apple IIgs player: magic, as the Windows Ultima 6 engine (U6GameWindow.U6Cast.cs, after Nuvie):
; C Cast - a spellbook readied in a hand ("No spellbook is readied."); the syllables' first letters typed (A An, B Bet,
; C Corp ... I In, L Lor: "In Lor" is I L), shown as words; Enter casts. In Ultima 6's order: in the book ("That spell
; is not in thy spellbook!"), the level ("Your level is not high enough."), the magic points ("Not enough magic
; points."), the reagents ("No Reagents."). Its name is said; a spell that needs a target asks "On whom?". Effects on
; the IIgs: Heal, Restore Mana, Damage (without a target: the foes within its radius), Sleep / Paralyze, Awaken / Cure
; Paralysis, Light (light.s). A spell (48 bytes): name, words, letters (8), circle, mana, target, 4 reagents (name,
; how many), 2 effects (kind, min, max), radius.

W_CAST     = 53
W_NOBOOK   = 54
W_NOSPELLS = 55
W_NOTINBOOK = 56
W_LEVELLOW = 57
W_NOMANA   = 58
W_NOREAGENTS = 59
W_ONWHOM   = 60
W_HEALED   = 61

E_HEAL   = 0
E_MANA   = 1
E_SLEEP  = 4
E_AWAKEN = 5
E_PARALYZE = 6
E_CUREPAR = 7
E_LIGHT  = 12
E_DAMAGE = 13

ParseSpells:
        lda [Ptr]
        sta SpellCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta SpellPtr
        lda Ptr+2
        sta SpellPtr+2
        lda SpellCount          ; 64 bytes each
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        jsr AddPtr
        lda [Ptr]
        sta BookCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta BookPtr
        lda Ptr+2
        sta BookPtr+2
        ldx BookCount           ; past them: the texts (read.s)
.b:     cpx #0
        beq .bd
        lda [Ptr]
        asl a
        clc
        adc #2
        jsr AddPtr
        dex
        bra .b
.bd:    jsr ParseSpellThings    ; what spells make and summon (spells2.s)
        jmp ParseTexts

; C: "Cast-", the readied spellbook, the syllables asked for.
StartCast:
        lda #W_CAST
        jsr PrintSaid
        lda #Dash
        jsr PrintMine
        lda ObjPtr              ; a spellbook worn (in a hand)
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T4
        beq .none
.l:     ldy #6
        lda [ItemPtr],y
        and #$F000+96+256
        cmp #$1000+64
        bcc .n
        ldy #10
        lda [ItemPtr],y
        cmp #$FFFF
        beq .n
        and #$C000
        cmp #$C000
        beq .book
.n:     lda #16
        jsr NextItem
        dec T4
        bne .l
.none:  jsr NewLine
        lda #W_NOBOOK
        jsr PrintSaid
        jmp PromptLine
.book:  lda [ItemPtr],y
        and #$00FF
        sta CastBook
        jsr BookList
        lda [Src]
        bne .some
        jsr NewLine
        lda #W_NOSPELLS
        jsr PrintSaid
        jmp PromptLine
.some:  stz CastLen
        stz CastBuf
        lda #9
        sta Mode
        rts

; Src = book CastBook's list (its count, then its spells' places).
BookList:
        lda BookPtr
        sta Src
        lda BookPtr+2
        sta Src+2
        ldx CastBook
.w:     cpx #0
        beq .d
        lda [Src]
        asl a
        clc
        adc #2
        clc
        adc Src
        sta Src
        lda Src+2
        adc #0
        sta Src+2
        dex
        bra .w
.d:     rts

; A key while the syllables are typed.
CastKey:
        cmp #$1B
        bne .k1
        stz Mode
        jmp PromptLine
.k1:    cmp #$0D
        bne .k2
        jmp CastIt
.k2:    and #$00DF              ; a letter: upper case
        cmp #'A'
        bcc .out
        cmp #'Z'+1
        bcs .out
        ldx CastLen
        cpx #7
        bcs .out
        sta CastCh
        sep #$20
        .a8
        sta CastBuf,x
        lda #0
        sta CastBuf+1,x
        rep #$20
        .a16
        inc CastLen
        lda CastCh
        sec                     ; its syllable, shown
        sbc #'A'
        asl a
        tax
        lda SylTab,x
        jsr PrintMine
        lda PenX
        clc
        adc SpaceW
        sta PenX
.out:   rts

; Enter: the spell typed, checked, cast.
CastIt:
        stz ItemCast            ; (a spell from the book: no item's)
        jsr NewLine
        stz Mode
        lda #1
        sta TurnUsed
        jsr BookList            ; in the book?
        lda [Src]
        sta T5
        ldy #2
.f:     lda T5
        bne .chk
        lda #W_NOTINBOOK
        brl .refuse
.chk:   lda [Src],y
        phy
        jsr SpellAt             ; ItemPtr = that spell
        jsr SameLetters
        ply
        bcs .found
        iny
        iny
        dec T5
        bra .f
.found: ldy #0                  ; the spell into SpBuf
.cp:    lda [ItemPtr],y
        sta SpBuf,y
        iny
        iny
        cpy #64
        bcc .cp
        lda AvRec+C_LEVEL       ; its circle: the caster's level at least
        cmp SpBuf+12
        bcs .lv
        lda #W_LEVELLOW
        brl .refuse
.lv:    lda AvRec+C_MANA
        cmp SpBuf+14
        bcs .mp
        lda #W_NOMANA
        brl .refuse
.mp:    ldx #0                  ; the reagents
.rg:    lda SpBuf+18,x
        cmp #$FFFF
        beq .rn
        phx
        jsr CountItem
        plx
        cmp SpBuf+20,x
        bcs .rn
        lda #W_NOREAGENTS
        brl .refuse
.rn:    inx
        inx
        inx
        inx
        cpx #16
        bcc .rg
        lda AvRec+C_MANA        ; paid: magic points, reagents
        sec
        sbc SpBuf+14
        sta AvRec+C_MANA
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
        jsr RefreshInventory
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
.refuse: jsr PrintSaid
        jmp PromptLine

; After a spell: words told end their own line (AfterTurn writes the prompt), else the prompt now.
SpellPrompt:
        lda Told
        bne .q
        jmp PromptLine
.q:     rts

; ItemPtr = spell A
SpellAt:
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a                   ; x 64
        clc
        adc SpellPtr
        sta ItemPtr
        lda SpellPtr+2
        adc #0
        sta ItemPtr+2
        rts

; Carry set if the spell at ItemPtr has the letters typed.
SameLetters:
        ldy #4
        ldx #0
.c:     lda [ItemPtr],y
        and #$00FF
        sta T4
        lda CastBuf,x
        and #$00FF
        cmp T4
        bne .no
        cmp #0
        beq .yes
        iny
        inx
        cpx #8
        bcc .c
.yes:   sec
        rts
.no:    clc
        rts

; WP = the Avatar
AvatarWP:
        lda PX
        sta AvRec
        lda PY
        sta AvRec+2
        lda #AvRec
        sta WP
        lda MyBank
        sta WP+2
        rts

; The target picked ("On whom?"): the Avatar, a member or a character there.
CastAt:
        lda CurX
        sta CastX
        lda CurY
        sta CastY
        jsr WhatIsThere
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        lda TxtColor
        jsr SetColor
        jsr PrintWord
        jsr NewLine
        lda Kind
        cmp #1
        bne .c2
        jsr AvatarWP
        bra .go
.c2:    cmp #2
        beq .chr
        jsr CellSpell           ; a spell on a square (teleport, a field, a door...): on it (spells2.s)
        bcc .np
        jsr AvatarWP
        bra .go
.np:    jsr ItemCastOff         ; (a wand's / potion's target refused: not spent - items.s)
        lda #9                  ; Not possible.
        jsr Format
        jmp PromptLine
.chr:   lda ChrRec
        sta WP
        lda ChrRec+2
        sta WP+2
.go:    jsr ApplySpell
        jsr ItemCastDone        ; a wand, a scroll, a potion: spent (items.s)
        jmp SpellPrompt

; Carry set if the spell (SpBuf) does damage.
HasDamage:
        lda SpBuf+34
        cmp #E_DAMAGE
        beq .y
        lda SpBuf+44
        cmp #E_DAMAGE
        beq .y
        clc
        rts
.y:     sec
        rts

; Damage spells without a target: every hostile character within the radius (3 if none) of the Avatar.
AreaDamage:
        lda SpBuf+54
        bne .r
        lda #3
.r:     sta ARad
        lda ChrPtr
        sta CP
        lda ChrPtr+2
        sta CP+2
        lda ChrCount
        sta ACount
.l:     lda ACount
        beq .d
        ldy #C_FLAGS
        lda [CP],y
        and #2+4
        cmp #2
        bne .n
        jsr DistCP
        cmp ARad
        beq .hit
        bcs .n
.hit:   lda CP
        sta WP
        lda CP+2
        sta WP+2
        lda CP                  ; (CP kept round it)
        pha
        lda CP+2
        pha
        jsr ApplySpell
        pla
        sta CP+2
        pla
        sta CP
.n:     jsr NextCP
        dec ACount
        bra .l
.d:     rts

; The spell's effects (SpBuf) on WP.
ApplySpell:
        ldx #0
.e:     phx
        lda SpBuf+34,x
        sta EKind
        lda SpBuf+36,x
        sta EMin
        lda SpBuf+38,x
        sta EMax
        lda SpBuf+40,x
        sta EThing
        lda SpBuf+42,x
        sta ESecs
        jsr Effect
        plx
        txa
        clc
        adc #10
        tax
        cpx #20
        bcc .e
        jsr UpCheck             ; healed: up again (bodies.s)
        jmp HpChanged

; One effect: EKind, EMin..EMax on WP.
Effect:
        lda EKind
        cmp #$FFFF
        bne .some
        rts
.some:  lda EMax                ; how much: EMin..EMax
        sec
        sbc EMin
        inc a
        jsr Random
        clc
        adc EMin
        sta EAmt
        lda EKind
        cmp #E_HEAL
        bne .k1
        ldy #C_HP
        lda [WP],y
        clc
        adc EAmt
        ldy #C_MAXHP
        cmp [WP],y
        bcc .h
        lda [WP],y
.h:     ldy #C_HP
        sta [WP],y
        jsr WPName
        lda #W_HEALED           ; {1} has healed.
        jmp Tell
.k1:    cmp #E_MANA
        bne .k2
        ldy #C_MANA
        lda [WP],y
        clc
        adc EAmt
        ldy #C_MAXMANA
        cmp [WP],y
        bcc .m
        lda [WP],y
.m:     ldy #C_MANA
        sta [WP],y
        rts
.k2:    cmp #E_DAMAGE
        bne .k3
        lda #AvRec              ; the Avatar's spell: a blow without the die
        sta AtkP
        lda MyBank
        sta AtkP+2
        lda WP
        sta DefP
        lda WP+2
        sta DefP+2
        jsr DefName
        jsr AtkName
        ldy #C_HP
        lda [DefP],y
        sec
        sbc EAmt
        bpl .hp
        lda #0
.hp:    sta [DefP],y
        jsr FightBack
        jsr TellWound
        ldy #C_HP
        lda [DefP],y
        bne .alive
        jmp Dies
.alive: rts
.k3:    cmp #E_SLEEP
        beq .zz
        cmp #E_PARALYZE
        bne .k4
.zz:    jsr WPIsAvatar
        bcs .out
        ldy #C_FLAGS
        lda [WP],y
        ora #16
        sta [WP],y
        rts
.k4:    cmp #E_AWAKEN
        beq .wk
        cmp #E_CUREPAR
        bne .k5
.wk:    ldy #C_FLAGS
        lda [WP],y
        and #$FFEF
        sta [WP],y
        rts
.k5:    jsr AilEffect           ; poison, curse, protection, invisibility, nourish (ailments.s)
        bcc .k6
        rts
.k6:    lda EKind
        cmp #E_LIGHT
        beq .lt
        jmp MoreEffect          ; (spells2.s)
.lt:
        lda EAmt                ; light: as many turns x 20
        asl a
        asl a
        sta T4
        asl a
        asl a
        clc
        adc T4
        sta LightLeft
        jmp DrawView
.out:   rts

; WhoName = WP's name ({1}).
WPName:
        jsr WPIsAvatar
        bcc .c
        ldx AvatarW
        lda WordTab,x
        sta WhoName
        lda WordTab+2,x
        sta WhoName+2
        rts
.c:     ldy #C_NAME
        lda [WP],y
        jsr NameNumber
        lda FmtName
        sta WhoName
        lda FmtName+2
        sta WhoName+2
        rts

; A = how many things named A the Avatar carries (in the pack or worn).
CountItem:
        sta EvName
        stz CCount
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T4
        beq .d
.l:     ldy #8
        lda [ItemPtr],y
        cmp EvName
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #96+256
        cmp #64
        bne .n
        jsr PartyOwns           ; (owner.s)
        bcc .n
        inc CCount
.n:     lda #16
        jsr NextItem
        dec T4
        bne .l
.d:     lda CCount
        rts

; GCount things named EvName taken from the Avatar (gone).
TakeItems:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T4
        beq .d
.l:     lda GCount
        beq .d
        ldy #8
        lda [ItemPtr],y
        cmp EvName
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #96+256
        cmp #64
        bne .n
        jsr PartyOwns
        bcc .n
        ldy #6
        lda [ItemPtr],y
        ora #32
        sta [ItemPtr],y
        dec GCount
.n:     lda #16
        jsr NextItem
        dec T4
        bne .l
.d:     rts

SpellCount: .dw 0
SpellPtr: .dd 0
BookCount: .dw 0
BookPtr:  .dd 0
CastBook: .dw 0
CastLen:  .dw 0
CastCh:   .dw 0
CastBuf:  .ds 10
SpBuf:    .ds 64
EThing:   .dw 0
ESecs:    .dw 0
CastX:    .dw 0
CastY:    .dw 0
EKind:    .dw 0
EMin:     .dw 0
EMax:     .dw 0
EAmt:     .dw 0
ARad:     .dw 0
ACount:   .dw 0
CCount:   .dw 0
LightLeft: .dw 0
SylTab:   .dw SyA,SyB,SyC,SyD,SyE,SyF,SyG,SyH,SyI,SyJ,SyK,SyL,SyM,SyN,SyO,SyP,SyQ,SyR,SyS,SyT,SyU,SyV,SyW,SyX,SyY,SyZ
SyA: .cstr "An"
SyB: .cstr "Bet"
SyC: .cstr "Corp"
SyD: .cstr "Des"
SyE: .cstr "Ex"
SyF: .cstr "Flam"
SyG: .cstr "Grav"
SyH: .cstr "Hur"
SyI: .cstr "In"
SyJ: .cstr "Jux"
SyK: .cstr "Kal"
SyL: .cstr "Lor"
SyM: .cstr "Mani"
SyN: .cstr "Nox"
SyO: .cstr "Ort"
SyP: .cstr "Por"
SyQ: .cstr "Quas"
SyR: .cstr "Rel"
SyS: .cstr "Sanct"
SyT: .cstr "Tym"
SyU: .cstr "Uus"
SyV: .cstr "Vas"
SyW: .cstr "Wis"
SyX: .cstr "Xen"
SyY: .cstr "Ylem"
SyZ: .cstr "Zu"
