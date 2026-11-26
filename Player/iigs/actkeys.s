; Sandcastle RPG Maker - Apple IIgs player: Ultima 6's keys, every one changeable (list item 30; U6GameWindow.U6Keys.cs,
; Engine/U6Keys.cs). main.s reads its own fixed keys (ActCanon: Ultima 6's reference card); a key pressed while walking is
; first turned into the fixed key of the action it is now (MapKey) - and a fixed key whose action moved to another key does
; nothing. The keys: the maker's (System Settings > Ultima 6, in GAME.DAT) under what the player changed in play (Ask For
; Key, Set Key, Reset Keys) - in KEYS.DAT beside the game, kept for good, through new games and loads, as Windows' keys.json.

ACT_N = 29              ; Up Down Left Right, Attack Cast Talk Look Get Drop Move Use Rest Combat, the portrait, Solo 1-9,
                        ; party mode, save, load, quit, cancel (AppleIIgsTalk.IIgsKeyActions)
ACT_CANCEL = 28

; GAME.DAT's keys (look.s, after the animated tiles; Ptr on them): how many, then each action's key (the maker's).
ParseKeys:
        ldx #0                  ; Ultima 6's to begin with
.c0:    lda ActCanon,x
        sta ActMaker,x
        inx
        inx
        cpx #ACT_N*2
        bcc .c0
        lda [Ptr]
        cmp #ACT_N+1
        bcc .c
        lda #ACT_N
.c:     asl a
        sta AkT
        ldy #2
        ldx #0
.l:     cpx AkT
        bcs .d
        lda [Ptr],y
        beq .z                  ; (none on the IIgs's keyboard: Ultima 6's)
        sta ActMaker,x
.z:     iny
        iny
        inx
        inx
        bra .l
.d:     jmp KeysEff

; At the start: what the player changed (KEYS.DAT), if anything.
KeysLoad:
        ldx #0
.z:     stz ActOver,x
        inx
        inx
        cpx #ACT_N*2
        bcc .z
        lda #KeysName
        jsr LocalPath
        jsr LoadFile
        bcs .e
        lda LoadAddr
        sta Src
        lda LoadAddr+2
        sta Src+2
        lda [Src]
        cmp #$594B              ; "KY", as many actions
        bne .f
        ldy #2
        lda [Src],y
        cmp #ACT_N
        bne .f
        ldy #4
        ldx #0
.l:     lda [Src],y
        sta ActOver,x
        iny
        iny
        inx
        inx
        cpx #ACT_N*2
        bcc .l
.f:     lda LoadHandle+2
        pha
        lda LoadHandle
        pha
        tool $1002              ; DisposeHandle
.e:     jmp KeysEff

; Each action's key now: the player's, else the maker's.
KeysEff:
        ldx #0
.l:     lda ActOver,x
        bne .o
        lda ActMaker,x
.o:     sta ActKey,x
        inx
        inx
        cpx #ACT_N*2
        bcc .l
        rts

; KEYS.DAT written again (no room on the disk: the keys still hold till the game ends).
KeysSave:
        lda #KeysName
        jsr LocalPath
        gsos $2002,DestroyParms
        gsos $2001,CreateParms
        bcs .x
        gsos $2010,OpenWParms
        bcs .x
        lda OpenWRef
        sta WriteRef
        sta CloseRef
        lda #KeysHead
        ldx MyBank
        ldy #4+ACT_N*2
        jsr WriteBlock
        gsos $2014,CloseParms
.x:     rts

; A = a key pressed while walking: the fixed key main.s knows for its action (0: a fixed key whose action has moved away).
MapKey:
        sta AkOrig
        cmp #'A'
        bcc .k
        cmp #'Z'+1
        bcs .k
        ora #$20                ; letters either case
.k:     sta AkK
        ldx #0
.l:     lda ActKey,x
        cmp AkK
        beq .hit
        inx
        inx
        cpx #ACT_N*2
        bcc .l
        ldx #0
.m:     lda ActCanon,x
        cmp AkK
        beq .zero
        inx
        inx
        cpx #ACT_N*2
        bcc .m
        lda AkOrig              ; no action's: as it is
        rts
.zero:  lda #0
        rts
.hit:   lda ActCanon,x
        rts

; Action AkAct (x2) gets key AkArg: another action holding it takes the old key (AkSwap 1) or nothing changes (0).
SetAct:
        ldx AkAct
        lda ActKey,x
        sta AkOld
        cmp AkArg
        beq .o
        ldx #0
.l:     cpx AkAct
        beq .n
        lda ActKey,x
        cmp AkArg
        bne .n
        lda AkSwap
        beq .o
        lda AkOld
        sta ActOver,x
.n:     inx
        inx
        cpx #ACT_N*2
        bcc .l
        ldx AkAct
        lda AkArg
        sta ActOver,x
        jsr KeysEff
        jsr KeysSave
.o:     rts

; EXT 41 KEYS mode action arg swap (7 bytes): mode 0 Set Key (arg: the key), 1 Ask For Key (the next key pressed; the
; Cancel key: no change), 2 Get Key Name (arg: the variable), 3 Reset Keys (action 255: every key).
XtKeysCmd:
        iny
        lda [TalkPtr],y
        and #$00FF
        sta AkMode
        iny
        lda [TalkPtr],y
        and #$00FF
        asl a
        sta AkAct
        iny
        lda [TalkPtr],y
        sta AkArg
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta AkSwap
        lda AkMode
        beq .set
        cmp #1
        beq .ask
        cmp #2
        beq .name
        lda AkAct               ; Reset Keys
        cmp #255*2
        bne .one
        ldx #0
.z:     stz ActOver,x
        inx
        inx
        cpx #ACT_N*2
        bcc .z
        jsr KeysEff
        jsr KeysSave
        bra .done
.one:   tax
        lda ActMaker,x
        sta AkArg
        lda #1
        sta AkSwap
.set:   jsr SetAct
        bra .done
.ask:   jsr NewLine
        lda #W_PRESSKEY
        jsr PrintSaid
.w:     pha
        pea $0028               ; key down, auto-key
        pea #bank(EvRec)
        pea #EvRec
        tool $0A06              ; GetNextEvent
        pla
        beq .w
        lda EvMsg
        and #$007F
        cmp #'A'
        bcc .k
        cmp #'Z'+1
        bcs .k
        ora #$20
.k:     sta AkArg
        lda AkAct
        cmp #ACT_CANCEL*2
        beq .set
        lda AkArg
        cmp ActKey+ACT_CANCEL*2
        beq .done               ; Cancel: no change
        bra .set
.name:  ldx AkAct
        lda ActKey,x
        jsr KeyName
        lda #AkName
        jsr MineToTemp
        sta SVal
        lda #1
        sta PTag
        lda AkArg
        asl a
        tax
        lda SVal
        jsr StoreVar
.done:  lda #7
        jmp Next

; A = a key: its name in AkName, as Windows' (U8Keys.Display): "T", "Up Arrow", "Ctrl+S", "Escape"...
KeyName:
        sta AkK
        ldx #0
.l:     lda KnTab,x
        beq .plain
        cmp AkK
        beq .f
        inx
        inx
        inx
        inx
        bra .l
.f:     lda KnTab+2,x
        sta Src
        lda MyBank
        sta Src+2
        ldy #0
        sep #$20
        .a8
.c:     lda [Src],y
        sta AkName,y
        beq .cd
        iny
        bra .c
.cd:    rep #$20
        .a16
        rts
.plain: ldy #0
        sep #$20
        .a8
        lda AkK
        cmp #' '
        bcs .p
        ldx #0                  ; a Ctrl key
.cl:    lda KnCtrl,x
        beq .cl2
        sta AkName,y
        iny
        inx
        bra .cl
.cl2:   lda AkK
        ora #$40
.p:     cmp #'a'
        bcc .u
        cmp #'z'+1
        bcs .u
        and #$DF
.u:     sta AkName,y
        lda #0
        sta AkName+1,y
        rep #$20
        .a16
        rts

ActCanon: .dw $0B,$0A,$08,$15,'a','c','t','l','g','d','m','u','r','b','*'
          .dw '1','2','3','4','5','6','7','8','9','0',$13,$12,$11,$1B
ActMaker: .ds ACT_N*2
ActKey:   .ds ACT_N*2
KeysHead: .dw $594B,ACT_N
ActOver:  .ds ACT_N*2           ; (written after KeysHead: KEYS.DAT)
KeysName: .cstr "KEYS.DAT"
AkT:      .dw 0
AkK:      .dw 0
AkOrig:   .dw 0
AkOld:    .dw 0
AkMode:   .dw 0
AkAct:    .dw 0
AkArg:    .dw 0
AkSwap:   .dw 0
AkName:   .ds 24
KnCtrl:   .cstr "Ctrl+"
KnTab:    .dw $0B,KnUp,$0A,KnDown,$08,KnLeft,$15,KnRight,$1B,KnEsc,$0D,KnEnter,$20,KnSpace,$09,KnTab2,$7F,KnBack,0
KnUp:     .cstr "Up Arrow"
KnDown:   .cstr "Down Arrow"
KnLeft:   .cstr "Left Arrow"
KnRight:  .cstr "Right Arrow"
KnEsc:    .cstr "Escape"
KnEnter:  .cstr "Enter"
KnSpace:  .cstr "Space"
KnTab2:   .cstr "Tab"
KnBack:   .cstr "Backspace"
