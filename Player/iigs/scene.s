; Sandcastle RPG Maker - Apple IIgs player: the Ultima 6 tab's scenes (list item 33; U6GameWindow.U6Scene.cs, U6Create.cs) -
; Begin Scene (the whole screen or the Game View in a colour; the world waits), Scene Picture, Scene Text (on the scene, or in
; the text area), Scene Wait For Key, Scene Remove, End Scene, and making a character: Scene Type Text, Scene Pick Picture,
; Set Gender, Scene Distribute Points. A scene's pictures and words are parts (at most 16, by name): replacing or removing
; one draws the scene again. Words to show (a prompt, Scene Text's) come caught in LogBuf (ext.s EXT 40 LOG mode 0: what SAY
; / SAYF print goes there) - {formulas} worked out. A picture shows its first frame. One font (the user's ruling).

SC_N = 16
W_POINTSLEFT = 121

; EXT 45 SCENE mode ... (scene.s): 0 begin, 1 picture, 2 text, 3 wait key, 4 remove, 5 end, 6 type, 7 pick picture,
; 8 gender, 9 points.
XtSceneCmd:
        stz LogOn               ; (words caught for it: in LogBuf)
        iny
        lda [TalkPtr],y
        and #$00FF
        iny
        cmp #0
        bne .m1
        jmp ScBegin
.m1:     cmp #1
        bne .m2
        jmp ScPic
.m2:     cmp #2
        bne .m3
        jmp ScText
.m3:     cmp #3
        bne .m4
        jmp ScWait
.m4:     cmp #4
        bne .m5
        jmp ScRemove
.m5:     cmp #5
        bne .m6
        jmp ScEnd
.m6:     cmp #6
        bne .m7
        jmp ScType
.m7:     cmp #7
        bne .m8
        jmp ScPick
.m8:     cmp #8
        bne .m9
        jmp ScGender
.m9:     jmp ScPoints

; A colour word: a slot (bit 15: [COLOR] colours, talk.s ColTab) as the palette's.
ColOf:
        cmp #$FFFF
        beq .o
        bit #$8000
        beq .o
        jmp ColSlot
.o:     rts

; 0 BEGIN area colour (6 bytes): area 0 the whole screen, 1 the Game View.
ScBegin:
        lda [TalkPtr],y
        and #$00FF
        sta SqScnArea
        iny
        lda [TalkPtr],y
        jsr ColOf
        sta SqScnCol
        lda SqScnArea
        bne .v
        stz SqScnX
        stz SqScnY
        lda #320
        sta SqScnW
        lda #200
        sta SqScnH
        bra .r
.v:     lda VX
        and #$FFFE
        sta SqScnX
        lda VY
        sta SqScnY
        lda VCols
        asl a
        asl a
        asl a
        asl a
        sta SqScnW
        lda VRows
        asl a
        asl a
        asl a
        asl a
        sta SqScnH
.r:     ldx #0
.z:     stz SqScPtKind,x
        inx
        inx
        cpx #SC_N*2
        bcc .z
        lda #1
        sta SceneOn
        jsr SceneDraw
        lda #6
        jmp Next

; 1 PICTURE pic x y name (11 bytes).
ScPic:
        lda [TalkPtr],y
        sta SqScA
        iny
        iny
        lda [TalkPtr],y
        sta SqScB
        iny
        iny
        lda [TalkPtr],y
        sta SqScC
        iny
        iny
        lda [TalkPtr],y
        ldx SceneOn
        beq .o
        jsr PartSlot
        bcs .o
        lda #1
        sta SqScPtKind,x
        lda SqScName
        sta SqScPtName,x
        lda SqScA
        sta SqPtPic,x
        lda SqScB
        sta SqScPtX,x
        lda SqScC
        sta SqScPtY,x
        jsr SceneDraw
.o:     lda #11
        jmp Next

; 2 TEXT x y w colour name where (14 bytes): LogBuf's words on the scene (where 0, a scene up), else in the text area.
ScText:
        lda [TalkPtr],y
        sta SqScB
        iny
        iny
        lda [TalkPtr],y
        sta SqScC
        iny
        iny
        lda [TalkPtr],y
        sta SqScD
        iny
        iny
        lda [TalkPtr],y
        jsr ColOf
        sta SqScA
        iny
        iny
        lda [TalkPtr],y
        sta SqScE
        iny
        lda [TalkPtr],y
        and #$00FF
        sta SqScF
        jsr LogEnd0
        lda SqScF
        bne .area
        lda SceneOn
        beq .area
        lda SqScE
        jsr PartSlot
        bcs .o
        lda #2
        sta SqScPtKind,x
        lda SqScName
        sta SqScPtName,x
        lda SqScB
        sta SqScPtX,x
        lda SqScC
        sta SqScPtY,x
        lda SqScD
        bne .w
        lda SqScnW                  ; 0: to the scene's edge
        sec
        sbc SqScB
.w:     sta SqPtW,x
        lda SqScA
        sta SqPtCol,x
        txa
        lsr a
        jsr PartText
        jsr CopyLog
        jsr SceneDraw
        bra .o
.area:  lda LogLen
        beq .o
        lda #LogBuf
        sta SayP
        lda MyBank
        sta SayP+2
        ldy #0
        jsr SayText
        jsr NewLine
.o:     lda #14
        jmp Next

; 3 WAITKEY (3 bytes): any key or a click.
ScWait:
.w:     pha
        pea $000A               ; mouse down, key down
        pea #bank(EvRec)
        pea #EvRec
        tool $0A06              ; GetNextEvent
        pla
        beq .w
        lda #3
        jmp Next

; 4 REMOVE name (5 bytes): $FFFF everything (the colour stays).
ScRemove:
        lda [TalkPtr],y
        sta SqScName
        ldx #0
.l:     lda SqScPtKind,x
        beq .n
        lda SqScName
        cmp #$FFFF
        beq .k
        cmp SqScPtName,x
        bne .n
.k:     stz SqScPtKind,x
.n:     inx
        inx
        cpx #SC_N*2
        bcc .l
        jsr SceneDraw
        lda #5
        jmp Next

; 5 END (3 bytes): the scene goes, the HUD and the world come back.
ScEnd:
        lda SceneOn
        beq .o
        stz SceneOn
        jsr Redraw
.o:     lda #3
        jmp Next

; 6 TYPE var letters x y start-formula: the player types (on the scene at x, y; else on the text area's line) - Backspace
; takes a letter back, Enter keeps them (none: nothing) in the variable. LogBuf: the words before.
ScType:
        lda [TalkPtr],y
        asl a
        sta SqTyVar
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        cmp #61
        bcc .m
        lda #60
.m:     sta SqTyMax
        iny
        lda [TalkPtr],y
        sta SqTyX
        iny
        iny
        lda [TalkPtr],y
        sta SqTyY
        iny
        iny
        jsr EvalExpr            ; what it starts as
        sty SqTyEnd
        stz SqTyLen
        ldx PTag
        cpx #1
        bne .np
        jsr TxtAddr
        ldy #0
        sep #$20
        .a8
.cp:    lda TextHeap,x
        beq .ce
        cpy SqTyMax
        bcs .ce
        sta SqTyBuf,y
        inx
        iny
        bra .cp
.ce:    rep #$20
        .a16
        sty SqTyLen
.np:    jsr LogEnd0
        lda #1
        sta SqTyCur
.loop:  jsr TyShow
.k:     pha
        pea $0028               ; key down, auto-key
        pea #bank(EvRec)
        pea #EvRec
        tool $0A06
        pla
        beq .k
        lda EvMsg
        and #$007F
        cmp #$0D
        bne .n1
        lda SqTyLen
        beq .k
        bra .done
.n1:    cmp #$7F
        beq .del
        cmp #$08
        beq .del
        cmp #' '
        bcc .loop
        cmp #$7F
        bcs .loop
        ldx SqTyLen
        cpx SqTyMax
        bcs .loop
        sep #$20
        .a8
        sta SqTyBuf,x
        rep #$20
        .a16
        inc SqTyLen
        bra .loop
.del:   lda SqTyLen
        beq .loop
        dec SqTyLen
        bra .loop
.done:  stz SqTyCur
        lda SceneOn
        beq .ta
        jsr TyEndBuf
        jsr SceneDraw           ; (the typing gone)
        bra .st
.ta:    jsr TyShow              ; the line as typed, then the next
        jsr NewLine
.st:    lda #SqTyBuf
        jsr MineToTemp
        sta SVal
        lda #1
        sta PTag
        lda SVal
        ldx SqTyVar
        jsr StoreVar
        ldy SqTyEnd
        sty TPC
        jmp RunTalk

TyEndBuf:
        ldx SqTyLen
        sep #$20
        .a8
        lda #0
        sta SqTyBuf,x
        rep #$20
        .a16
        rts

; The typing line: the words before, what is typed, the cursor.
TyShow:
        jsr TyEndBuf
        jsr LbReset
        lda #LogBuf
        jsr LbAddStr
        lda #SqTyBuf
        jsr LbAddStr
        lda SqTyCur
        beq .nc
        lda #SqStrCur
        jsr LbAddStr
.nc:    lda SceneOn
        beq .ta
        jsr SceneDraw
        lda SqTyX
        clc
        adc SqScnX
        sta SqLnX
        lda SqTyY
        clc
        adc SqScnY
        sta SqLnY
        lda SqScnW
        sec
        sbc SqTyX
        sta SqLnW
        bra .go
.ta:    jsr TaLine
.go:    lda #$FFFF
        sta SqLnCol
        lda #SqLnBuf
        jmp SceneLine

; The text area's line the pen is on: cleared, SqLnX SqLnY SqLnW on it.
TaLine:
        lda TextX
        sta RX
        lda PenY
        sec
        sbc #8
        sta RY
        sta SqLnY
        lda TextW
        sta RW
        lda #LINE_H
        sta RH
        jsr ClearText
        lda TextX
        clc
        adc #2
        sta SqLnX
        lda TextW
        sec
        sbc #4
        sta SqLnW
        rts

; 7 PICK var x y n start whomap whoidx portrait, n x (picture, portrait): the pictures one at a time at x, y (on the scene,
; else on the screen) - Left / Right to look, Enter, Space or a click picks: its number (1...) into the variable, and with
; portrait 1 it is Who's portrait at once. LogBuf: the words under it.
ScPick:
        lda [TalkPtr],y
        asl a
        sta SqTyVar
        iny
        iny
        lda [TalkPtr],y
        sta SqTyX
        iny
        iny
        lda [TalkPtr],y
        sta SqTyY
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta SqScPkN
        iny
        lda [TalkPtr],y
        and #$00FF
        sta SqPkAt
        iny
        lda [TalkPtr],y
        sta SqPkMap
        iny
        iny
        lda [TalkPtr],y
        sta SqPkIdx
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta SqPkPort
        iny
        sty SqPkList
        lda SqScPkN
        asl a
        asl a
        clc
        adc SqPkList
        sta SqTyEnd
        lda SqScPkN
        bne .some
        brl .end
.some:  lda SqPkAt
        beq .a0
        dec a
        cmp SqScPkN
        bcc .a1
.a0:    lda #0
.a1:    sta SqPkAt
        jsr LogEnd0
.loop:  jsr PkShow
.k:     pha
        pea $002A               ; mouse down, key down, auto-key
        pea #bank(EvRec)
        pea #EvRec
        tool $0A06
        pla
        beq .k
        lda EvRec
        cmp #1
        beq .pick
        lda EvMsg
        and #$007F
        cmp #$08
        bne .r
        lda SqPkAt
        bne .dl
        lda SqScPkN
.dl:    dec a
        sta SqPkAt
        bra .loop
.r:     cmp #$15
        bne .e
        lda SqPkAt
        inc a
        cmp SqScPkN
        bcc .rs
        lda #0
.rs:    sta SqPkAt
        bra .loop
.e:     cmp #$0D
        beq .pick
        cmp #' '
        bne .k
.pick:  lda SqPkAt
        inc a
        ldx SqTyVar
        jsr SetVarNum
        lda SqPkPort
        beq .cl
        lda SqPkAt
        asl a
        asl a
        clc
        adc SqPkList
        tay
        iny
        iny
        lda [TalkPtr],y
        sta SqPkPor
        lda SqPkMap
        sta JMap
        lda SqPkIdx
        sta JIdx
        jsr WhoRec
        bcs .cl
        lda SqPkPor
        ldx JMap
        cpx #$FFFF
        bne .s
        inc a                   ; (the Avatar's: + 1)
.s:     ldy #12
        sta [WP],y
.cl:    lda SceneOn
        beq .rd
        jsr SceneDraw
        bra .end
.rd:    jsr Redraw
.end:   ldy SqTyEnd
        sty TPC
        jmp RunTalk

; The picture now and "words < n / N >" under it.
PkShow:
        lda SqPkAt
        asl a
        asl a
        clc
        adc SqPkList
        tay
        lda [TalkPtr],y
        sta SprId
        lda SceneOn
        beq .ns
        jsr SceneDraw
        lda SqTyX
        clc
        adc SqScnX
        and #$FFFE
        sta CellX
        lda SqTyY
        clc
        adc SqScnY
        sta CellY
        jsr SpriteSize
        bra .dr
.ns:    lda SqTyX
        and #$FFFE
        sta CellX
        sta RX
        lda SqTyY
        sta CellY
        sta RY
        jsr SpriteSize
        lda T4
        sta RW
        lda T5
        sta RH
        jsr RestoreRect
.dr:    lda T5
        sta SqPkH
        jsr DrawPic
        jsr LbReset
        lda #LogBuf
        jsr LbAddStr
        lda #SqStrLt
        jsr LbAddStr
        lda SqPkAt
        inc a
        jsr LbAddNum
        lda #SqStrSl
        jsr LbAddStr
        lda SqScPkN
        jsr LbAddNum
        lda #SqStrGt
        jsr LbAddStr
        lda CellX
        sta SqLnX
        lda CellY
        clc
        adc SqPkH
        clc
        adc #2
        sta SqLnY
        lda #320
        sec
        sbc SqLnX
        sta SqLnW
        lda SceneOn
        bne .w
        lda SqLnX                 ; (no scene: the line's place back first)
        sta RX
        lda SqLnY
        sta RY
        lda SqLnW
        and #$FFFE
        sta RW
        lda #LINE_H
        sta RH
        jsr RestoreRect
.w:     lda #$FFFF
        sta SqLnCol
        lda #SqLnBuf
        jmp SceneLine

; 8 GENDER whomap whoidx how var (10 bytes): how 1 male, 2 female, 0 by the variable (2 female). Kept in GenTab (saved) by
; the owner code (ext.s JOwner) for the Gender condition.
ScGender:
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
        sta SqScA
        iny
        lda [TalkPtr],y
        sta SqScB
        lda SqScA
        bne .h
        lda SqScB
        asl a
        tax
        lda VarTab,x
        cmp #2
        beq .f
        lda #1
        bra .h
.f:     lda #2
.h:     sta SqScC
        jsr JOwner
        sta SqScD
        ldx #0
.f1:    lda GenTab,x
        cmp SqScD
        beq .put
        inx
        inx
        inx
        inx
        cpx #64
        bcc .f1
        ldx #0
.f2:    lda GenTab,x
        beq .put
        inx
        inx
        inx
        inx
        cpx #64
        bcc .f2
        bra .o
.put:   lda SqScD
        sta GenTab,x
        lda SqScC
        sta GenTab+2,x
.o:     lda #10
        jmp Next

; 9 POINTS whomap whoidx least most spend x y leftvar n, n x (stat, name 0), points-formula: the points spread over Who's
; stats - Up / Down a stat, Right (+) gives a point, Left (-) takes one back, each between least and most (0: 99); Enter
; done (spend 1: only with none left). The points left into leftvar ($FFFF none). LogBuf: the title.
ScPoints:
        lda [TalkPtr],y
        sta JMap
        iny
        iny
        lda [TalkPtr],y
        sta JIdx
        iny
        iny
        lda [TalkPtr],y
        sta SqPtLeast
        iny
        iny
        lda [TalkPtr],y
        bne .mo
        lda #99
.mo:    sta SqPtMost
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta SqPtSpend
        iny
        lda [TalkPtr],y
        sta SqTyX
        iny
        iny
        lda [TalkPtr],y
        sta SqTyY
        iny
        iny
        lda [TalkPtr],y
        sta SqPtLeftV
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        cmp #9
        bcc .n
        lda #8
.n:     sta SqPtN
        iny
        ldx #0                  ; each stat: its number, where its name is
.st:    cpx SqPtN
        bcs .ex
        txa
        asl a
        sta SqScX
        lda [TalkPtr],y
        and #$00FF
        phx
        ldx SqScX
        sta SqPtCode,x
        iny
        tya
        sta SqPtNameY,x
        plx
.sn:    lda [TalkPtr],y
        iny
        and #$00FF
        bne .sn
        inx
        bra .st
.ex:    jsr EvalExpr            ; the points
        sty SqTyEnd
        cmp #$8000
        bcc .pp
        lda #0
.pp:    sta SqPtPts
        jsr LogEnd0
        jsr WhoRec
        bcc .who
        brl .end
.who:   ldx #0                  ; the values: Who's, at least Least
.v:     cpx SqPtN
        bcs .vd
        txa
        asl a
        tax
        lda SqPtCode,x
        asl a
        clc
        adc #16
        tay
        lda [WP],y
        cmp SqPtLeast
        bpl .vk
        lda SqPtLeast
.vk:    sta SqPtVal,x
        txa
        lsr a
        tax
        inx
        bra .v
.vd:    stz SqPtRow
.loop:  jsr PtShow
.k:     pha
        pea $0028
        pea #bank(EvRec)
        pea #EvRec
        tool $0A06
        pla
        beq .k
        lda SqPtRow
        asl a
        tax
        lda EvMsg
        and #$007F
        cmp #$0B
        bne .dn
        lda SqPtRow
        bne .u1
        lda SqPtN
.u1:    dec a
        sta SqPtRow
        bra .loop
.dn:    cmp #$0A
        bne .rt
        lda SqPtRow
        inc a
        cmp SqPtN
        bcc .d1
        lda #0
.d1:    sta SqPtRow
        bra .loop
.rt:    cmp #$15
        beq .give
        cmp #'+'
        beq .give
        cmp #'='
        bne .lf
.give:  lda SqPtPts
        beq .loop
        lda SqPtVal,x
        cmp SqPtMost
        bpl .loop
        inc SqPtVal,x
        dec SqPtPts
        bra .loop
.lf:    cmp #$08
        beq .take
        cmp #'-'
        bne .en
.take:  lda SqPtVal,x
        cmp SqPtLeast
        beq .l2
        bmi .l2
        dec SqPtVal,x
        inc SqPtPts
.l2:    brl .loop
.en:    cmp #$0D
        bne .l2
        lda SqPtSpend
        beq .done
        lda SqPtPts
        bne .l2
.done:  jsr WhoRec              ; Who's stats from now on
        bcs .lv
        ldx #0
.w:     cpx SqPtN
        bcs .lv
        txa
        asl a
        tax
        lda SqPtCode,x
        asl a
        clc
        adc #16
        tay
        lda SqPtVal,x
        sta [WP],y
        txa
        lsr a
        tax
        inx
        bra .w
.lv:    lda SqPtLeftV
        cmp #$FFFF
        beq .cl
        asl a
        tax
        lda SqPtPts
        jsr SetVarNum
.cl:    lda SceneOn
        beq .rd
        jsr SceneDraw
        bra .dp
.rd:    jsr Redraw
.dp:    jsr DrawPanel
.end:   ldy SqTyEnd
        sty TPC
        jmp RunTalk

; The title, "> Name: value" a line each, "Points left: n".
PtShow:
        lda SceneOn
        beq .ns
        jsr SceneDraw
        lda SqTyX
        clc
        adc SqScnX
        sta SqLnX
        lda SqTyY
        clc
        adc SqScnY
        sta SqLnY
        lda SqScnX
        clc
        adc SqScnW
        sec
        sbc SqLnX
        sta SqLnW
        bra .t
.ns:    lda SqTyX                 ; no scene: on the screen, its place back first
        and #$FFFE
        sta RX
        sta SqLnX
        lda SqTyY
        sta RY
        sta SqLnY
        lda #320
        sec
        sbc RX
        sta RW
        sta SqLnW
        lda SqPtN
        clc
        adc #2
        asl a
        sta T4
        asl a
        asl a
        clc
        adc T4
        sta RH                  ; (n + 2) lines
        jsr RestoreRect
.t:     lda #$FFFF
        sta SqLnCol
        lda LogLen
        beq .rows
        lda #LogBuf
        jsr SceneLine
        lda SqLnNext
        sta SqLnY
.rows:  stz SqPtI
.r:     lda SqPtI
        cmp SqPtN
        bcs .left
        jsr LbReset
        lda #SqStrSp
        ldx SqPtI
        cpx SqPtRow
        bne .m
        lda #SqStrMark
.m:     jsr LbAddStr
        lda SqPtI
        asl a
        tax
        ldy SqPtNameY,x
        jsr LbAddTalk
        lda #SqStrColon
        jsr LbAddStr
        lda SqPtI
        asl a
        tax
        lda SqPtVal,x
        jsr LbAddNum
        lda #SqLnBuf
        jsr SceneLine
        lda SqLnNext
        sta SqLnY
        inc SqPtI
        bra .r
.left:  jsr LbReset
        lda #W_POINTSLEFT
        jsr LbAddWord
        lda #SqStrSp1
        jsr LbAddStr
        lda SqPtPts
        jsr LbAddNum
        lda #SqLnBuf
        jmp SceneLine

; ---- the scene ----
; Its colour, then each part.
SceneDraw:
        lda SceneOn
        bne .go
        rts
.go:    lda SqScnX
        sta RX
        lda SqScnY
        sta RY
        lda SqScnW
        sta RW
        lda SqScnH
        sta RH
        lda SqScnCol
        jsr FillRect
        stz SqScI
.l:     lda SqScI
        cmp #SC_N
        bcs .d
        asl a
        tax
        lda SqScPtKind,x
        beq .n
        cmp #1
        bne .t
        lda SqPtPic,x
        sta SprId
        lda SqScPtX,x
        clc
        adc SqScnX
        and #$FFFE
        sta CellX
        lda SqScPtY,x
        clc
        adc SqScnY
        sta CellY
        jsr DrawPic
        bra .n
.t:     lda SqScPtX,x
        clc
        adc SqScnX
        sta SqLnX
        lda SqScPtY,x
        clc
        adc SqScnY
        sta SqLnY
        lda SqPtW,x
        sta SqLnW
        lda SqPtCol,x
        sta SqLnCol
        lda SqScI
        jsr PartText
        jsr SceneLine
.n:     inc SqScI
        bra .l
.d:     rts

; A = a part's name: X = its place (x2) - the one with that name, else a free one. Carry set: none free. SqScName = the name.
PartSlot:
        sta SqScName
        cmp #$FFFF
        beq .free
        ldx #0
.f:     lda SqScPtKind,x
        beq .fn
        lda SqScPtName,x
        cmp SqScName
        beq .ok
.fn:    inx
        inx
        cpx #SC_N*2
        bcc .f
.free:  ldx #0
.e:     lda SqScPtKind,x
        beq .ok
        inx
        inx
        cpx #SC_N*2
        bcc .e
        sec
        rts
.ok:    clc
        rts

; A = a part: A = its words' place (96 bytes each).
PartText:
        asl a
        asl a
        asl a
        asl a
        asl a
        sta SqScT
        asl a
        clc
        adc SqScT
        clc
        adc #SqPtText
        rts

; LogBuf ended (a C string).
LogEnd0:
        ldx LogLen
        sep #$20
        .a8
        lda #0
        sta LogBuf,x
        rep #$20
        .a16
        rts

; LogBuf (at most 95 letters) into A (a part's words).
CopyLog:
        sta Src
        lda MyBank
        sta Src+2
        ldy #0
        sep #$20
        .a8
.l:     cpy LogLen
        bcs .e
        cpy #95
        bcs .e
        lda LogBuf,y
        sta [Src],y
        iny
        bra .l
.e:     lda #0
        sta [Src],y
        rep #$20
        .a16
        rts

; Words (A: a C string) at SqLnX, SqLnY (their top), wrapped to SqLnW, in SqLnCol ($FFFF: the text's); SqLnNext = the next line's top.
; The text area's places, pen and colour are borrowed and given back.
SceneLine:
        sta SqLnStr
        lda TextX
        sta SqScTX
        lda TextY
        sta SqScTY
        lda TextW
        sta SqScTW
        lda TextH
        sta SqScTH
        lda PenX
        sta SqScPX
        lda PenY
        sta SqScPY
        lda TxtColor
        sta SqScTC
        lda SqLnX
        sec
        sbc #2
        sta TextX
        lda SqLnY
        dec a
        sta TextY
        lda SqLnW
        cmp #16
        bcs .w
        lda #16
.w:     sta TextW
        lda #200
        sec
        sbc TextY
        sta TextH
        lda SqLnCol
        cmp #$FFFF
        beq .c
        sta TxtColor
.c:     jsr TextInit
        lda SqLnStr
        sta SayP
        lda MyBank
        sta SayP+2
        ldy #0
        jsr SayText
        lda PenY
        clc
        adc #2
        sta SqLnNext
        lda SqScTX
        sta TextX
        lda SqScTY
        sta TextY
        lda SqScTW
        sta TextW
        lda SqScTH
        sta TextH
        lda SqScPX
        sta PenX
        lda SqScPY
        sta PenY
        lda SqScTC
        sta TxtColor
        rts

; ---- SqLnBuf, a line put together ----
LbReset:
        stz SqLbLen
        sep #$20
        .a8
        lda #0
        sta SqLnBuf
        rep #$20
        .a16
        rts

; A = a C string (the data bank's) added.
LbAddStr:
        sta Src
        lda MyBank
        sta Src+2
LbAddLong:
        ldy #0
        ldx SqLbLen
        sep #$20
        .a8
.l:     lda [Src],y
        beq .e
        cpx #110
        bcs .e
        sta SqLnBuf,x
        inx
        iny
        bra .l
.e:     lda #0
        sta SqLnBuf,x
        rep #$20
        .a16
        stx SqLbLen
        rts

; Y = a C string's place in the talk's code: added.
LbAddTalk:
        tya
        clc
        adc TalkPtr
        sta Src
        lda TalkPtr+2
        adc #0
        sta Src+2
        bra LbAddLong

; A = one of GAME.DAT's words: added.
LbAddWord:
        asl a
        asl a
        tax
        lda WordTab,x
        sta Src
        lda WordTab+2,x
        sta Src+2
        bra LbAddLong

; A = a number: added in figures.
LbAddNum:
        ldx #0
        jsr Decimal
        sep #$20
        .a8
        lda #0
        sta StatBuf,x
        rep #$20
        .a16
        lda #StatBuf
        bra LbAddStr

SceneOn:  .dw 0
SqScnArea:    .dw 0
SqScnX:       .dw 0
SqScnY:       .dw 0
SqScnW:       .dw 0
SqScnH:       .dw 0
SqScnCol:     .dw 0
SqScPtKind:   .ds SC_N*2
SqScPtName:   .ds SC_N*2
SqScPtX:      .ds SC_N*2
SqScPtY:      .ds SC_N*2
SqPtW:      .ds SC_N*2
SqPtCol:    .ds SC_N*2
SqPtPic:    .ds SC_N*2
SqPtText:   .ds SC_N*96
SqScA:      .dw 0
SqScB:      .dw 0
SqScC:      .dw 0
SqScD:      .dw 0
SqScE:      .dw 0
SqScF:      .dw 0
SqScI:      .dw 0
SqScT:      .dw 0
SqScX:      .dw 0
SqScName:   .dw 0
SqScTX:     .dw 0
SqScTY:     .dw 0
SqScTW:     .dw 0
SqScTH:     .dw 0
SqScPX:     .dw 0
SqScPY:     .dw 0
SqScTC:     .dw 0
SqLnX:      .dw 0
SqLnY:      .dw 0
SqLnW:      .dw 0
SqLnCol:    .dw 0
SqLnStr:    .dw 0
SqLnNext:   .dw 0
SqLbLen:    .dw 0
SqLnBuf:    .ds 112
SqTyVar:    .dw 0
SqTyMax:    .dw 0
SqTyX:      .dw 0
SqTyY:      .dw 0
SqTyEnd:    .dw 0
SqTyLen:    .dw 0
SqTyCur:    .dw 0
SqTyBuf:    .ds 64
SqScPkN:      .dw 0
SqPkAt:     .dw 0
SqPkMap:    .dw 0
SqPkIdx:    .dw 0
SqPkPort:   .dw 0
SqPkList:   .dw 0
SqPkPor:    .dw 0
SqPkH:      .dw 0
SqPtLeast:  .dw 0
SqPtMost:   .dw 0
SqPtSpend:  .dw 0
SqPtLeftV:  .dw 0
SqPtN:      .dw 0
SqPtPts:    .dw 0
SqPtRow:    .dw 0
SqPtI:      .dw 0
SqPtCode:   .ds 16
SqPtNameY:  .ds 16
SqPtVal:    .ds 16
SqStrCur:   .cstr "_"
SqStrLt:    .cstr " < "
SqStrSl:    .cstr " / "
SqStrGt:    .cstr " >"
SqStrSp:    .cstr "  "
SqStrSp1:   .cstr " "
SqStrMark:  .cstr "> "
SqStrColon: .cstr ": "
