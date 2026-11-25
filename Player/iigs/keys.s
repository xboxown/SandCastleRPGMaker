; Sandcastle RPG Maker - Apple IIgs player: keys and lockpicks, as the Windows Ultima 6 engine (U6GameWindow.Locks.cs,
; from Exult's key usecode): Use a key (U, Tab, the key), "On what?", the cursor on a door or chest next to the Avatar.
; The key that fits it (the door's / chest's Required Key) locks or unlocks it ("Unlocked." / "Locked."); another one:
; "The key doesn't fit.". A lockpick on a locked one: Dexterity against its Pick-Lock Difficulty - 50% at equal, 5% a
; point either way, 5..95% - "Unlocked.", or the pick breaks. Magically locked: neither works. A key: flag 512, its code
; at +10. A lockpick: flag 1024. A door: difficulty +12, the code that fits +14. A container: the code that fits +10.

W_ONWHAT   = 46
W_UNLOCKED = 47
W_NOFIT    = 48
W_PICKBROKE = 49

; The key or lockpick picked in the grid (GridRec; its name written): "On what?" and the map's cursor.
KeyPicked:
        lda GridRec
        sta KeyRec
        lda GridRec+2
        sta KeyRec+2
        lda PenX
        clc
        adc SpaceW
        sta PenX
        lda #W_ONWHAT
        jsr PrintSaid
        lda #VERB_KEY
        sta Verb
        lda PX
        sta CurX
        lda PY
        sta CurY
        lda #4
        sta Mode
        jsr RefreshInventory
        jmp DrawView

; The cell picked for the key or lockpick.
KeyOn:
        jsr WhatIsThere
        lda FmtName
        sta TP
        lda FmtName+2
        sta TP+2
        lda TxtColor
        jsr SetColor
        jsr PrintWord
        jsr NewLine
        lda Kind                ; a keychain on the Avatar: the party's keys onto it (travel.s)
        cmp #1
        bne .nav
        jsr IsRing
        bcc .nav
        jmp RingAdd
.nav:   lda Kind
        cmp #3
        bne .np
        jsr ObjFlags
        and #4+128
        beq .np
        jsr NextToCursor
        bcs .near
        lda #10                 ; Out of range!
        jsr Format
        jmp PromptLine
.np:    lda #9                  ; Not possible.
        jsr Format
        jmp PromptLine
.near:  jsr ObjFlags
        and #2048               ; magically locked
        bne .np
        jsr IsRing              ; a keychain: whichever key on it fits (travel.s)
        bcc .nring
        jsr RingFits
        bcs .fits
        jmp PromptLine
.nring:
        lda KeyRec
        sta Src
        lda KeyRec+2
        sta Src+2
        ldy #6
        lda [Src],y
        and #512
        beq .pick
        jsr LockCode            ; a key: does it fit?
        ldy #10
        cmp [Src],y
        beq .fits
        lda #W_NOFIT
        jsr PrintSaid
        jmp PromptLine
.fits:  jsr ObjFlags
        and #8
        beq .lock
        jsr ObjFlags            ; locked -> unlocked
        and #$FFF7
        sta [ItemPtr],y
        lda #W_UNLOCKED
        jsr PrintSaid
        jmp PromptLine
.lock:  jsr ObjFlags            ; an open door can't be locked
        and #4
        beq .lk
        jsr ObjFlags
        and #16
        bne .np
.lk:    jsr ObjFlags
        ora #8
        sta [ItemPtr],y
        lda #14                 ; Locked.
        jsr PrintSaid
        jmp PromptLine
.pick:  jsr ObjFlags            ; a lockpick: only on a locked one
        and #8
        bne .fb1
        brl .np
.fb1:
        jsr ObjFlags
        and #4
        beq .cdiff
        ldy #12                 ; a door's difficulty
        lda [ItemPtr],y
        bra .diff
.cdiff: lda #10                 ; a chest's: 10
.diff:  sta T5
        lda AvRec+C_DEX         ; 50% + 5% a point of Dexterity over it, 5..95%
        sec
        sbc T5
        bmi .less
        cmp #10
        bcc .m
        lda #9
.m:     sta T5
        asl a
        asl a
        clc
        adc T5
        clc
        adc #50
        bra .chance
.less:  eor #$FFFF
        inc a
        cmp #10
        bcc .m2
        lda #9
.m2:    sta T5
        asl a
        asl a
        clc
        adc T5
        sta T5
        lda #50
        sec
        sbc T5
.chance: sta T4
        lda #100
        jsr Random
        cmp T4
        bcs .broke
        jsr ObjFlags
        and #$FFF7
        sta [ItemPtr],y
        lda #W_UNLOCKED
        jsr PrintSaid
        jmp PromptLine
.broke: ldy #6                  ; the pick is gone
        lda [Src],y
        ora #32
        sta [Src],y
        jsr RefreshInventory
        lda #W_PICKBROKE
        jsr PrintSaid
        jmp PromptLine

; A = the key code that fits the door or container at ItemPtr (ObjRec).
LockCode:
        jsr ObjFlags
        and #4
        beq .c
        ldy #14
        lda [ItemPtr],y
        rts
.c:     ldy #10
        lda [ItemPtr],y
        rts

KeyRec:   .dd 0
