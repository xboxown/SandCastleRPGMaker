; Sandcastle RPG Maker - Apple IIgs player: map events' triggers, as the Windows Ultima 6 engine (U6GameWindow.xaml.cs
; TryFireEventPages: the first page whose trigger and conditions hold runs). An event: x, y, its triggers (a bit each),
; its code's length, its code - its pages (Engine/AppleIIgsTalk.cs CompileEvent): TRIG trigger next, ITEM name kind
; next, IFVs (its conditions), HIT, its commands, END. Triggers: 1 stepped on, 2 stood on (a turn there), 3 stepped
; off, 4 Use on its cell, 5 arriving on the map (Always), 6 having / not having an item (what is carried changed), 7 a
; thing dropped on it, 8 a thing picked up from it. Triggers wait in a queue; each event runs in turn (a talk-like
; question waits for the player; EndEvent goes on with the queue).

T_ENTER = 1
T_STAY  = 2
T_EXIT  = 3
T_USE   = 4
T_ARRIVE = 5
T_HAVE  = 6
T_DROP  = 7
T_PICK  = 8
ALL     = $FFFF

; A = trigger, X, Y = the cell (X = ALL: every event on the map).
QueueEvent:
        sta QTT
        stx QTX
        sty QTY
        lda QN
        cmp #8
        bcs .out
        asl a
        tax
        lda QTT
        sta QT,x
        lda QTX
        sta QX,x
        lda QTY
        sta QY,x
        inc QN
.out:   rts

; The Avatar stepped from OldX, OldY onto PX, PY.
SteppedEvents:
        ldx OldX
        ldy OldY
        lda #T_EXIT
        jsr QueueEvent
        ldx PX
        ldy PY
        lda #T_ENTER
        jsr QueueEvent
        lda PX
        sta StayX
        lda PY
        sta StayY
        lda #1                  ; this turn was a step: no Stay yet
        sta JustMoved
        jmp RunQueue

; A turn passed: still on the cell it was on: its Stay events.
StayEvents:
        lda JustMoved
        beq .st
        stz JustMoved
        rts
.st:    lda PX
        cmp StayX
        bne .new
        lda PY
        cmp StayY
        bne .new
        ldx PX
        ldy PY
        lda #T_STAY
        jmp QueueEvent
.new:   lda PX
        sta StayX
        lda PY
        sta StayY
        rts

; On a map (the game starting, Change Map): the queue of the map left forgotten, this one's Always events.
NewMapEvents:
        stz QN
        stz EvScan
ArriveEvents:
        ldx #ALL
        lda #T_ARRIVE
        jsr QueueEvent
        lda PX
        sta StayX
        lda PY
        sta StayY
        jmp RunQueue

; A thing (A = its name) picked up from X, Y.
PickedEvents:
        sta EvItem
        lda #T_PICK
        jsr QueueEvent
        bra Having
; A thing (A = its name) dropped on X, Y.
DroppedEvents:
        sta EvItem
        lda #T_DROP
        jsr QueueEvent
Having:
        ldx #ALL
        lda #T_HAVE
        jmp QueueEvent

; Use on the cursor's cell: carry set if an event there has a Use page (it runs instead of using the thing).
UseEvent:
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
        and #1<<T_USE
        beq .n
        ldx CurX
        ldy CurY
        lda #T_USE
        jsr QueueEvent
        jsr RunQueue
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

; Runs the queued triggers' events one after another; one waiting for the player comes back here when it ends.
RunQueue:
        lda EvRunning
        bne .out
        lda Mode
        bne .out
.next:  lda QN
        bne .scan
.out:   rts
.scan:  lda EvScan
        bne .w
        lda EvPtr
        sta EvWalk
        lda EvPtr+2
        sta EvWalk+2
.w:     lda EvScan
        cmp EvCount
        bcs .pop
        lda EvWalk
        sta ItemPtr
        lda EvWalk+2
        sta ItemPtr+2
        lda QX
        cmp #ALL
        beq .anyc
        lda [ItemPtr]
        cmp QX
        bne .skip
        ldy #2
        lda [ItemPtr],y
        cmp QY
        bne .skip
.anyc:  lda QT
        asl a
        tax
        lda BitTab,x
        ldy #4
        and [ItemPtr],y
        beq .skip
        jsr Advance             ; (the next one's place kept)
        lda ItemPtr             ; this one runs
        clc
        adc #8
        sta TalkPtr
        lda ItemPtr+2
        adc #0
        sta TalkPtr+2
        lda QT
        sta CurTrig
        stz EvHit
        lda #1
        sta EvRunning
        stz TPC
        lda #3
        sta Mode
        jmp RunTalk
.skip:  jsr Advance
        bra .w
.pop:   ldx #0                  ; this trigger done: the next one
.sh:    cpx #14
        bcs .shd
        lda QT+2,x
        sta QT,x
        lda QX+2,x
        sta QX,x
        lda QY+2,x
        sta QY,x
        inx
        inx
        bra .sh
.shd:   dec QN
        stz EvScan
        brl .next

; EvWalk past the event at ItemPtr; EvScan + 1.
Advance:
        ldy #6
        lda [ItemPtr],y
        clc
        adc #8
        clc
        adc ItemPtr
        sta EvWalk
        lda ItemPtr+2
        adc #0
        sta EvWalk+2
        inc EvScan
        rts

; An event's code is over: the prompt if it did something, then the queue goes on.
EndEvent:
        lda IntroOn             ; the Introduction over: the title menu again (title.s)
        beq .ev
        stz IntroOn
        stz EvRunning
        jmp ShowTitle
.ev:    stz EvRunning
        stz Mode
        lda EvHit
        beq .q
        jsr PromptLine
.q:     jmp RunQueue

; TRIG / ITEM / HIT in an event's code (Y on the op, A the op).
EventOp:
        cmp #9
        bne .item
        iny                     ; TRIG trigger next
        lda [TalkPtr],y
        and #$00FF
        cmp CurTrig
        beq .tok
        iny
        lda [TalkPtr],y
        sta TPC
        jmp RunTalk
.tok:   lda TPC
        clc
        adc #4
        sta TPC
        jmp RunTalk
.item:  cmp #10
        bne .hit
        iny                     ; ITEM name kind next
        lda [TalkPtr],y
        sta EvName
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta EvKind
        iny
        lda [TalkPtr],y
        sta EvNext
        lda EvKind
        cmp #2
        bne .carried
        lda EvItem              ; the thing dropped / picked up is it
        cmp EvName
        beq .ipass
        bra .ifail
.carried:
        jsr HasItem
        lda #0
        rol a                   ; 1: carried
        eor EvKind              ; kind 1 (not carried): the other way
        bne .ipass
.ifail: lda EvNext
        sta TPC
        jmp RunTalk
.ipass: lda TPC
        clc
        adc #6
        sta TPC
        jmp RunTalk
.hit:   lda #1                  ; HIT: this page runs - its words on a new line
        sta EvHit
        jsr NewLine
        inc TPC
        jmp RunTalk

; Carry set if the Avatar carries a thing named EvName (in the pack, or in a bag carried).
HasItem:
        lda ObjPtr
        sta ItemPtr
        lda ObjPtr+2
        sta ItemPtr+2
        lda ObjCount
        sta T4
        beq .no
.l:     ldy #8
        lda [ItemPtr],y
        cmp EvName
        bne .n
        ldy #6
        lda [ItemPtr],y
        and #32
        bne .n
        lda [ItemPtr],y
        and #64+256
        cmp #64
        bne .ins
        jsr PartyOwns           ; carried by the party (owner.s)
        bcs .yes
        bra .n
.ins:   and #256
        beq .n
        ldy #14                 ; inside: is its container carried?
        lda [ItemPtr],y
        sta T5
        lda ItemPtr
        pha
        lda ItemPtr+2
        pha
        lda T5
        jsr ObjAt
        ldy #6
        lda [ItemPtr],y
        tax
        pla
        sta ItemPtr+2
        pla
        sta ItemPtr
        txa
        and #64
        bne .yes
.n:     lda #16
        jsr NextItem
        dec T4
        bne .l
.no:    clc
        rts
.yes:   sec
        rts

QN:       .dw 0
QT:       .ds 16
QX:       .ds 16
QY:       .ds 16
QTT:      .dw 0
QTX:      .dw 0
QTY:      .dw 0
EvScan:   .dw 0
EvWalk:   .dd 0
EvRunning: .dw 0
EvHit:    .dw 0
CurTrig:  .dw 0
EvItem:   .dw $FFFF
EvName:   .dw 0
EvKind:   .dw 0
EvNext:   .dw 0
JustMoved: .dw 0
StayX:    .dw 0
StayY:    .dw 0
BitTab:   .dw 1,2,4,8,16,32,64,128,256
