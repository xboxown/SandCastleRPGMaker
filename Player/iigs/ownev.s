; Sandcastle RPG Maker - Apple IIgs player: characters' and objects' own events, as the Windows Ultima 6 engine
; (FireCharacterEvent: By Hit, By Death, By Ailment Effect; an object's / container's pages on Use, Pickup, Drop; By Use
; Item with an item used on them). Their code is in the spells' Codes; CharEvTab: a character's map, place, code;
; ObjEvTab: a thing's name, code. They go through the events' queue (QX $FFFE: QY the code, QT the trigger).
; Use on a plain carried thing: "On what?" - a character, a thing or an event's square there with a page for it.

T_HIT  = 9
T_DEATH = 10
T_AIL  = 11
T_USED = 12
T_USEON = 13

ParseOwnEvents:
        lda [Ptr]
        sta CEvCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta CEvPtr
        lda Ptr+2
        sta CEvPtr+2
        lda CEvCount
        asl a
        sta T4
        asl a
        clc
        adc T4                  ; 6 bytes each
        jsr AddPtr
        lda [Ptr]
        sta OEvCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta OEvPtr
        lda Ptr+2
        sta OEvPtr+2
        lda OEvCount
        asl a
        asl a
        jsr AddPtr
        jmp ParseMusic          ; (music.s)

; TalkPtr = code A (Codes)
CodeAt:
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
        rts

; The queue's first entry is a character's or thing's own: its code runs (the entry gone).
RunOwn:
        lda QT
        sta CurTrig
        cmp #21                 ; a leveling event: its variables (levels.s)
        bcc .nl
        jsr LevelVarsNow
.nl:    lda QY
        pha
        ldx #0                  ; the entry off the queue
.sh:    cpx #14
        bcs .shd
        lda QT+2,x
        sta QT,x
        lda QX+2,x
        sta QX,x
        lda QY+2,x
        sta QY,x
        lda QA+2,x
        sta QA,x
        lda QB+2,x
        sta QB,x
        lda QC+2,x
        sta QC,x
        inx
        inx
        bra .sh
.shd:   dec QN
        pla
        jsr CodeAt
        stz EvHit
        lda #1
        sta EvRunning
        stz TPC
        lda #3
        sta Mode
        jmp RunTalk

; DefP's / WP's own event for trigger A (queued). Carry set if it has one.
FireDef:
        pha
        lda DefP
        sta WP
        lda DefP+2
        sta WP+2
        pla
FireWP:
        sta FTrig
        jsr WPIsAvatar
        bcs .no
        lda WP+2                ; a member: its map and place
        cmp MyBank
        bne .map
        lda WP
        sec
        sbc #PartyTab
        cmp #512
        bcs .map
        ldy #P_MAP
        lda [WP],y
        sta FMap
        ldy #P_IDX
        lda [WP],y
        sta FIdx
        bra .find
.map:   lda CurMap              ; a character of this map: its place
        sta FMap
        lda WP
        sec
        sbc ChrPtr
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        lsr a
        sta FIdx
.find:  lda CEvPtr
        sta Src
        lda CEvPtr+2
        sta Src+2
        ldx CEvCount
        beq .no
        ldy #0
.l:     lda [Src],y
        cmp FMap
        bne .n
        iny
        iny
        lda [Src],y
        dey
        dey
        cmp FIdx
        bne .n
        iny
        iny
        iny
        iny
        lda [Src],y
        tay
        lda FTrig
        ldx #$FFFE
        jsr QueueEvent
        sec
        rts
.n:     tya
        clc
        adc #6
        tay
        dex
        bne .l
.no:    clc
        rts

; Arriving on a map (ArriveEvents): each living character of it with its own events gets its "Always" pages run
; (U6GameWindow.AlwaysPages.cs - when play starts, and here each time the map is come to).
CharsArrive:
        lda CEvPtr
        sta Src
        lda CEvPtr+2
        sta Src+2
        ldx CEvCount
        beq .o
        ldy #0
.l:     lda [Src],y
        cmp CurMap
        bne .n
        phx
        phy
        iny
        iny
        lda [Src],y             ; its place: alive?
        cmp ChrCount
        bcs .skip
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        clc
        adc #C_FLAGS
        tay
        lda [ChrPtr],y
        and #4
        bne .skip
        ply
        phy
        iny
        iny
        iny
        iny
        lda [Src],y
        tay
        lda #T_ARRIVE
        ldx #$FFFE
        jsr QueueEvent
        lda CEvPtr              ; (Src again)
        sta Src
        lda CEvPtr+2
        sta Src+2
.skip:  ply
        plx
.n:     tya
        clc
        adc #6
        tay
        dex
        bne .l
.o:     rts

; A = a thing's name, X = a trigger: its own event queued. Carry set if it has one.
ObjEvQueue:
        sta FName
        stx FTrig
        lda OEvPtr
        sta Src
        lda OEvPtr+2
        sta Src+2
        ldx OEvCount
        beq .no
        ldy #0
.l:     lda [Src],y
        cmp FName
        beq .got
        iny
        iny
        iny
        iny
        dex
        bne .l
.no:    clc
        rts
.got:   iny
        iny
        lda [Src],y
        tay
        lda FTrig
        ldx #$FFFE
        jsr QueueEvent
        sec
        rts

; Use on a thing with its own Use event (A its name, X the trigger): queued and run. Carry set if so.
ObjEvent:
        jsr ObjEvQueue
        bcc .o
        jsr RunQueue
        sec
.o:     rts

; Use on someone (ChrRec): its own Use event, else Not possible.
UseChar:
        lda ChrRec
        sta WP
        lda ChrRec+2
        sta WP+2
        lda #T_USED
        jsr FireWP
        bcc .np
        jmp RunQueue
.np:    lda #9
        jsr Format
        jmp PromptLine

; Use on a plain carried thing (GridRec; its name written): "On what?" and the map's cursor.
StartUseOn:
        lda GridRec
        sta UseRec
        lda GridRec+2
        sta UseRec+2
        lda PenX
        clc
        adc SpaceW
        sta PenX
        lda #W_ONWHAT
        jsr PrintSaid
        lda #VERB_USEON
        sta Verb
        lda PX
        sta CurX
        lda PY
        sta CurY
        lda #4
        sta Mode
        jsr RefreshInventory
        jmp DrawView

; The square picked for the thing: someone's, a thing's or the square's own page for it - else Not possible.
UseOnCell:
        lda UseRec
        sta ItemPtr
        lda UseRec+2
        sta ItemPtr+2
        ldy #8
        lda [ItemPtr],y
        sta EvItem              ; (pages waiting for this item: ITEM kind 2)
        jsr WhatIsThere
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        lda TxtColor
        jsr SetColor
        jsr PrintWord
        jsr NewLine
        stz UseHit
        lda Kind
        cmp #2
        bne .thing
        lda ChrRec
        sta WP
        lda ChrRec+2
        sta WP+2
        lda #T_USEON
        jsr FireWP
        bcc .sq
        inc UseHit
        bra .sq
.thing: cmp #3
        bne .sq
        jsr ObjFlags            ; a tool on a plant, a station, a thing (crafting.s)
        jsr ToolOnThing
        bcc .tev
        rts
.tev:   jsr ObjFlags
        ldy #8
        lda [ItemPtr],y
        ldx #T_USEON
        jsr ObjEvQueue
        bcc .sq
        inc UseHit
.sq:    jsr EventHere           ; a map event on the square with an item page
        bcc .run
        ldx CurX
        ldy CurY
        lda #T_USEON
        jsr QueueEvent
        inc UseHit
.run:   lda UseHit
        bne .go
        lda #9
        jsr Format
        jmp PromptLine
.go:    jmp RunQueue

; Carry set if a map event on the cursor's square has a page for an item used on it (bit 13).
EventHere:
        lda EvCount
        beq .no
        sta T4
        lda EvPtr
        sta ItemPtr
        lda EvPtr+2
        sta ItemPtr+2
.l:     lda [ItemPtr]
        cmp CurX
        bne .n
        ldy #2
        lda [ItemPtr],y
        cmp CurY
        bne .n
        ldy #4
        lda [ItemPtr],y
        and #8192
        beq .n
        sec
        rts
.n:     ldy #6
        lda [ItemPtr],y
        clc
        adc #8
        jsr NextItem
        dec T4
        bne .l
.no:    clc
        rts

CEvCount: .dw 0
CEvPtr:   .dd 0
OEvCount: .dw 0
OEvPtr:   .dd 0
FTrig:    .dw 0
FMap:     .dw 0
FIdx:     .dw 0
FName:    .dw 0
UseRec:   .dd 0
UseHit:   .dw 0
