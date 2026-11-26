; Sandcastle RPG Maker - Apple IIgs player: saving and loading the game (Ctrl+S, Ctrl+R) and the quit question (Ctrl+Q).
; SAVE.DAT, in the game's own folder ("9:"; on floppies, disk 1): "SCSV", its version, the map, where the Avatar is
; and faces, the open bag, how many objects, maps, then every object (16 bytes each: where it is, carried, open...),
; the variables (VarTab), and for each map whether it was visited and its characters.

SAVE_VERSION = 15               ; (b727: Change Stat's log, ext.s Reset Stat)

; Ctrl+S
SaveGame:
        lda SaveOff             ; Allow / Forbid Saving (cmds.s OpGame)
        beq .may
        jsr NewLine
        lda #W_NOSAVING
        jsr PrintSaid
        jmp PromptLine
.may:   jsr SoloOff             ; the Avatar moved again first (solo.s)
        lda #SaveName
        jsr LocalPath           ; PathBuf = "9:SAVE.DAT"
        gsos $2002,DestroyParms ; the old one gone (none: no matter)
        gsos $2001,CreateParms
        bcc .k1
        brl .fail
.k1:
        gsos $2010,OpenWParms
        bcc .k2
        brl .fail
.k2:
        lda OpenWRef
        sta WriteRef
        sta CloseRef
        lda CurMap              ; the header
        sta SvHead+6
        lda PX
        sta SvHead+8
        lda PY
        sta SvHead+10
        lda Facing
        sta SvHead+12
        lda OpenBag
        sta SvHead+14
        lda ObjCount
        sta SvHead+16
        lda MapCount
        sta SvHead+18
        lda #SvHead
        ldx MyBank
        ldy #20
        jsr WriteBlock
        bcc .k3
        brl .close
.k3:
        lda ObjCount            ; the objects
        asl a
        asl a
        asl a
        asl a
        tay
        lda ObjPtr
        ldx ObjPtr+2
        jsr WriteBlock
        bcc .k4
        brl .close
.k4:
        lda #VarTab             ; the variables
        ldx MyBank
        ldy #512
        jsr WriteBlock
        bcc .k0
        brl .close
.k0:    lda PX                  ; the Avatar: its stats
        sta AvRec
        lda PY
        sta AvRec+2
        lda #AvRec
        ldx MyBank
        ldy #64
        jsr WriteBlock
        bcc .kc
        brl .close
.kc:    lda #Day                ; the clock
        ldx MyBank
        ldy #6
        jsr WriteBlock
        bcc .kp
        brl .close
.kp:    lda #PartyN             ; the party
        ldx MyBank
        ldy #2+512
        jsr WriteBlock
        bcc .k5
        brl .close
.k5:
        lda #VL                 ; the vehicles: where, how big, which way, speed, flying (vehicles.s)
        ldx MyBank
        ldy #VSTATE
        jsr WriteBlock
        bcc .k5v
        brl .close
.k5v:   lda #SCRIPTDATA         ; texts, arrays, grids (texts.s)
        ldx MyBank
        ldy #SCRIPTEND-SCRIPTDATA
        jsr WriteBlock
        bcc .k5t
        brl .close
.k5t:
        stz SvMap               ; every map: visited, its characters
.m:     lda SvMap
        cmp MapCount
        bcs .done
        jsr MapOff
        lda MapTab+MT_IN,x
        sta SvRec
        lda MapTab+MT_CC,x
        sta SvRec+2
        lda #SvRec
        ldx MyBank
        ldy #4
        jsr WriteBlock
        bcc .k6
        brl .close
.k6:
        lda SvRec
        beq .nm
        lda SvMap
        jsr MapOff
        lda MapTab+MT_CC,x
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        tay
        lda MapTab+MT_CP+2,x
        pha
        lda MapTab+MT_CP,x
        plx
        jsr WriteBlock
        bcc .k7
        brl .close
.k7:
.nm:    inc SvMap
        bra .m
.done:  gsos $2014,CloseParms
        bcc .k8
        brl .fail
.k8:
        jsr NewLine
        lda #22                 ; Game saved.
        jsr PrintSaid
        jmp PromptLine
.close: gsos $2014,CloseParms
.fail:  jsr NewLine
        lda #9                  ; Not possible.
        jsr Format
        jmp PromptLine

; Writes Y bytes from X:A (bank, address) to the file open. Carry set: it couldn't.
WriteBlock:
        sta WriteBuf
        stx WriteBuf+2
        sty WriteReq
        stz WriteReq+2
        cpy #0
        beq .none
        gsos $2013,WriteParms
        rts
.none:  clc
        rts

; Ctrl+R: the saved game back.
LoadGame:
        stz LoadedOk
        lda #SaveName
        jsr LocalPath
        jsr LoadFile
        bcc .got
.none:  jsr NewLine
        lda #24                 ; No saved game.
        jsr PrintSaid
        jmp PromptLine
.got:   lda LoadHandle          ; kept: loading a map's file uses LoadFile too
        sta SvHandle
        lda LoadHandle+2
        sta SvHandle+2
        lda LoadAddr
        sta SvPtr
        lda LoadAddr+2
        sta SvPtr+2
        lda [SvPtr]             ; "SCSV", this version, this game (as many objects and maps)
        cmp #$4353
        bne .bad
        ldy #2
        lda [SvPtr],y
        cmp #$5653
        bne .bad
        ldy #4
        lda [SvPtr],y
        cmp #SAVE_VERSION
        bne .bad
        ldy #16
        lda [SvPtr],y
        cmp ObjCount
        bne .bad
        ldy #18
        lda [SvPtr],y
        cmp MapCount
        beq .good
.bad:   jsr FreeSave
        bra .none
.good:  ldy #6
        lda [SvPtr],y
        sta GoMap
        ldy #8
        lda [SvPtr],y
        sta GoX
        ldy #10
        lda [SvPtr],y
        sta GoY
        ldy #12
        lda [SvPtr],y
        sta Facing
        ldy #14
        lda [SvPtr],y
        sta OpenBag
        lda #20
        jsr SvSkip
        lda ObjPtr              ; the objects
        sta Dst
        lda ObjPtr+2
        sta Dst+2
        lda ObjCount
        asl a
        asl a
        asl a
        asl a
        jsr SvCopy
        lda #VarTab             ; the variables
        sta Dst
        lda MyBank
        sta Dst+2
        lda #512
        jsr SvCopy
        lda #AvRec              ; the Avatar's stats
        sta Dst
        lda MyBank
        sta Dst+2
        lda #64
        jsr SvCopy
        lda #Day                ; the clock
        sta Dst
        lda MyBank
        sta Dst+2
        lda #6
        jsr SvCopy
        lda #PartyN             ; the party
        sta Dst
        lda MyBank
        sta Dst+2
        lda #2+512
        jsr SvCopy
        lda #VL                 ; the vehicles
        sta Dst
        lda MyBank
        sta Dst+2
        lda #VSTATE
        jsr SvCopy
        lda #SCRIPTDATA         ; texts, arrays, grids
        sta Dst
        lda MyBank
        sta Dst+2
        lda #SCRIPTEND-SCRIPTDATA
        jsr SvCopy
        lda #$FFFF
        sta DrvV
        sta VAuto
        stz ViewIdx
        stz SvMap               ; the maps' characters (a map visited: loaded again if need be)
.m:     lda SvMap
        cmp MapCount
        bcs .done
        lda [SvPtr]
        sta SvRec
        ldy #2
        lda [SvPtr],y
        sta SvRec+2
        lda #4
        jsr SvSkip
        lda SvRec
        beq .nm
        lda SvMap
        jsr MapOff
        lda MapTab+MT_IN,x
        bne .in
        lda SvMap
        jsr LoadMap
        bcs .skip               ; its disk not given: its characters as they start
.in:    lda SvMap
        jsr MapOff
        lda MapTab+MT_CC,x
        cmp SvRec+2
        bne .skip
        lda MapTab+MT_CP,x
        sta Dst
        lda MapTab+MT_CP+2,x
        sta Dst+2
        lda SvRec+2
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        jsr SvCopy
        bra .nm
.skip:  lda SvRec+2
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        jsr SvSkip
.nm:    inc SvMap
        bra .m
.done:  jsr FreeSave
        lda #1
        sta LoadedOk
        stz AvDead
        stz Mode
        lda #$FFFF              ; that map, as if arriving: its cells, the cell map again
        sta CurMap
        jsr SetMapWrap          ; (moves.s)
        sta WMap                ; (its weather again too - weather.s)
        lda GoMap
        jsr ChangeMap
        jsr AvNameWord          ; the Avatar's name and look as they were (ext.s)
        jsr LkAvatar
        jsr Redraw
        jsr NewLine
        lda #23                 ; Game loaded.
        jsr PrintSaid
        jmp PromptLine

; SvPtr += A
SvSkip:
        clc
        adc SvPtr
        sta SvPtr
        lda SvPtr+2
        adc #0
        sta SvPtr+2
        rts

; A bytes (even) from SvPtr to Dst; SvPtr past them.
SvCopy:
        sta T4
        ldy #0
        cpy T4
        beq .d
.c:     lda [SvPtr],y
        sta [Dst],y
        iny
        iny
        cpy T4
        bcc .c
.d:     lda T4
        jmp SvSkip

FreeSave:
        lda SvHandle+2
        pha
        lda SvHandle
        pha
        tool $1002              ; DisposeHandle
        rts

; Esc, q, Ctrl+Q: "Quit the game? (Y/N)"
AskQuit:
        jsr NewLine
        lda #21
        jsr PrintSaid
        lda #6
        sta Mode
        rts

; The answer: Y quits, anything else goes on.
QuitKey:
        ora #$20
        cmp #'y'
        bne .no
        jmp Quit
.no:    lda QuitFromTitle       ; asked at the title menu: back to it
        beq .game
        stz QuitFromTitle
        jmp ShowTitle
.game:  stz Mode
        jmp PromptLine

SvMap:    .dw 0
LoadedOk: .dw 0
SvRec:    .dw 0,0
SvHandle: .dd 0
SvHead:   .dw $4353,$5653        ; "SCSV"
          .dw SAVE_VERSION
          .ds 14
SaveName: .cstr "SAVE.DAT"
DestroyParms:
        .dw 1
        .dd PathBuf
CreateParms:
        .dw 4
        .dd PathBuf
        .dw $00C3               ; access: read, write, rename, destroy
        .dw $0006               ; a binary file
        .dd 0
OpenWParms:
        .dw 3
OpenWRef:
        .dw 0
        .dd PathBuf
        .dw 2                   ; to write
WriteParms:
        .dw 4
WriteRef:
        .dw 0
WriteBuf:
        .dd 0
WriteReq:
        .dd 0
        .dd 0
