; Sandcastle RPG Maker - Apple IIgs player: sounds, and the files on other disks.
; A sound plays through GS/OS's Sound Tool Set (FFStartSound: free-form synthesis, the sample streamed from memory
; through the Ensoniq DOC). Sound n is the file SND.n, loaded the first time it plays and kept: from the game's own folder
; ("9:"), else from its disk (DISKS.DAT: ":DISK:SND.n"), else the Disk Swap screen asks for that disk until it is in.

; ---- reading GAME.DAT's sounds and Disk Swap screen (after the talks) ----
ParseSounds:
        lda TalkTotal           ; past the talks
        sta MulR
        lda TalkTotal+2
        sta MulR+2
        jsr AddPtrLong
        lda [Ptr]
        sta SndCount
        lda #2
        jsr AddPtr
        ldx #0
.s:     cpx SndCount
        bcs .sdone
        cpx #256
        bcs .sdone
        stx T4                  ; its number
        lda [Ptr]               ; past the name
        and #$00FF
        inc a
        jsr AddPtr
        lda T4                  ; (the rate is not needed: the frequency offset is)
        asl a
        asl a
        tax
        ldy #2
        lda [Ptr],y             ; the length
        sta SndLen,x
        ldy #4
        lda [Ptr],y
        sta SndLen+2,x
        stz SndAddr,x
        stz SndAddr+2,x
        lda T4
        asl a
        tax
        ldy #6
        lda [Ptr],y             ; the frequency offset
        sta SndFreq,x
        ldy #8
        lda [Ptr],y
        and #$00FF              ; the volume
        sta SndVol,x
        lda #9
        jsr AddPtr
        ldx T4
        inx
        bra .s
.sdone: ; the Disk Swap screen
        lda [Ptr]
        sta SwapHasPic
        lda #2
        jsr AddPtr
        lda SwapHasPic
        beq .nopic
        lda Ptr
        sta SwapPic
        lda Ptr+2
        sta SwapPic+2
        lda #32000
        jsr AddPtr
.nopic: lda [Ptr]
        sta SwapFrames
        ldy #2
        lda [Ptr],y
        sta SwapAX
        ldy #4
        lda [Ptr],y
        sta SwapAY
        lda #6
        jsr AddPtr
        lda Ptr
        sta SwapFrameTab
        lda Ptr+2
        sta SwapFrameTab+2
        lda SwapFrames
        asl a
        jsr AddPtr
        lda [Ptr]
        sta SwapTX
        ldy #2
        lda [Ptr],y
        sta SwapTY
        ldy #4
        lda [Ptr],y
        sta SwapTC
        lda #6
        jsr AddPtr
        lda Ptr                 ; the words: a length, then a C string
        clc
        adc #1
        sta SwapMsg
        lda Ptr+2
        adc #0
        sta SwapMsg+2
        lda [Ptr]
        and #$00FF
        clc
        adc #2
        jsr AddPtr
        lda [Ptr]
        sta SwapSnd
        lda #2
        jsr AddPtr
        jmp ParseMaps           ; the maps, map 0's events (maps.s)

; ---- DISKS.DAT ----
; Read once at the start, if the game came on floppies: the disks' names, which file is on which.
LoadDisks:
        stz DiskCount
        stz FileCount
        lda #DisksName
        jsr LocalPath
        jsr LoadFile
        bcc .got
        rts
.got:   lda LoadAddr
        sta Ptr
        lda LoadAddr+2
        sta Ptr+2
        lda [Ptr]
        cmp #$4353              ; "SC"
        beq .ok
        rts
.ok:    lda #4
        jsr AddPtr
        lda [Ptr]
        sta Count
        lda #2
        jsr AddPtr
        ldx #0
.d:     lda Count
        beq .files
        cpx #32*8
        bcs .dskip
        lda Ptr                 ; its ProDOS name
        clc
        adc #1
        sta DiskTab,x
        lda Ptr+2
        adc #0
        sta DiskTab+2,x
.dskip: jsr SkipStr
        cpx #32*8
        bcs .dskip2
        lda Ptr                 ; the name shown
        clc
        adc #1
        sta DiskTab+4,x
        lda Ptr+2
        adc #0
        sta DiskTab+6,x
        inc DiskCount
.dskip2:
        jsr SkipStr
        txa
        clc
        adc #8
        tax
        dec Count
        bra .d
.files: lda [Ptr]
        sta FileCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta FileTab
        lda Ptr+2
        sta FileTab+2
        rts

; Ptr past a string: a length, the letters, 0.
SkipStr:
        lda [Ptr]
        and #$00FF
        clc
        adc #2
        jmp AddPtr

; FileName (a C string) -> A = the disk it is on (1 = the first), 0 if DISKS.DAT doesn't say.
DiskOf:
        lda FileTab
        sta Src
        lda FileTab+2
        sta Src+2
        lda FileCount
        sta Count
.f:     lda Count
        beq .none
        ldy #0                  ; compare its name (from Src + 1) with FileName
        ldx #0
.c:     iny
        lda [Src],y
        and #$00FF
        sta T4
        lda FileName,x
        and #$00FF
        cmp T4
        bne .next
        inx
        cmp #0
        bne .c
        iny                     ; the same: its disk after the 0
        lda [Src],y
        and #$00FF
        rts
.next:  lda [Src]               ; the next: length + 3
        and #$00FF
        clc
        adc #3
        clc
        adc Src
        sta Src
        lda Src+2
        adc #0
        sta Src+2
        dec Count
        bra .f
.none:  lda #0
        rts

; ---- paths and loading ----
; PathBuf = "9:" + the C string at A (in this program).
LocalPath:
        sta T4
        sep #$20
        .a8
        lda #'9'
        sta PathBuf+2
        lda #':'
        sta PathBuf+3
        rep #$20
        .a16
        ldx #2
        bra AppendName

; PathBuf = ":" + disk A's ProDOS name + ":" + FileName.
DiskPath:
        dec a
        asl a
        asl a
        asl a
        tax
        lda DiskTab,x
        sta Src
        lda DiskTab+2,x
        sta Src+2
        sep #$20
        .a8
        lda #':'
        sta PathBuf+2
        rep #$20
        .a16
        ldx #1
        ldy #0
.v:     lda [Src],y
        and #$00FF
        beq .vend
        sep #$20
        .a8
        sta PathBuf+2,x
        rep #$20
        .a16
        inx
        iny
        cpx #40
        bcc .v
.vend:  sep #$20
        .a8
        lda #':'
        sta PathBuf+2,x
        rep #$20
        .a16
        inx
        lda #FileName
        sta T4
        ; on into AppendName

; PathBuf + 2 + X onwards = the C string at T4 (in this program); PathBuf's length set.
AppendName:
        ldy T4
.n:     lda 0,y
        and #$00FF
        beq .end
        sep #$20
        .a8
        sta PathBuf+2,x
        rep #$20
        .a16
        inx
        iny
        cpx #60
        bcc .n
.end:   stx PathBuf
        rts

; Loads the file PathBuf names into memory of its own (and 256 zero bytes after it): LoadAddr, LoadSize. Carry set: not there.
LoadFile:
        lda #PathBuf
        sta OpenPath
        lda MyBank
        sta OpenPath+2
        gsos $2010,OpenParms
        bcc .open
        rts
.open:  lda OpenRef
        sta EofRef
        sta ReadRef
        sta CloseRef
        gsos $2019,EofParms
        bcc .eof
        brl .close
.eof:   lda EofLen
        sta LoadSize
        lda EofLen+2
        sta LoadSize+2
        pha
        pha
        lda EofLen+2
        pha
        lda EofLen
        clc
        adc #256
        pha
        bcc .nc
        lda 3,s                 ; (the high word + 1)
        inc a
        sta 3,s
.nc:    lda UserId
        pha
        pea $C000
        pea 0
        pea 0
        tool $0902              ; NewHandle
        pla
        sta LoadHandle
        pla
        sta LoadHandle+2
        bcc .mem
        brl .close
.mem:   lda LoadHandle          ; where it is: the handle's pointer
        sta Src
        lda LoadHandle+2
        sta Src+2
        ldy #2
        lda [Src],y
        sta LoadAddr+2
        sta ReadBuf+2
        lda [Src]
        sta LoadAddr
        sta ReadBuf
        lda EofLen
        sta ReadReq
        lda EofLen+2
        sta ReadReq+2
        gsos $2012,ReadParms
        php
        lda LoadAddr            ; 256 zero bytes after it (a sound stops at a 0)
        clc
        adc LoadSize
        sta Src
        lda LoadAddr+2
        adc LoadSize+2
        sta Src+2
        ldy #0
        lda #0
.z:     sta [Src],y
        iny
        iny
        cpy #256
        bne .z
        plp
.close: php
        gsos $2014,CloseParms
        plp
        rts

; FileName from its own folder, from its disk, or - its disk asked for - when it is in. Carry set: not to be had.
FindFile:
        lda #FileName
        jsr LocalPath
        jsr LoadFile
        bcs .disk
        rts
.disk:  jsr DiskOf
        sta WantDisk
        bne .try
        sec                     ; nowhere to look
        rts
.try:   lda WantDisk
        jsr DiskPath
        jsr LoadFile
        bcs .ask
        rts
.ask:   jmp SwapFor

; ---- sounds ----
; Plays sound A at volume X (0-255).
PlaySound:
        cmp SndCount
        bcc .ok
        rts
.ok:    sta SndNow
        stx SndVolNow
        jsr VolScale            ; Set Volume's share (music.s)
        lda SndNow
        asl a
        asl a
        tax
        lda SndAddr,x
        ora SndAddr+2,x
        bne .loaded
        jsr SoundName           ; FileName = SND.n
        jsr FindFile
        bcc .got
        rts                     ; not to be had: no sound
.got:   lda SndNow
        asl a
        asl a
        tax
        lda LoadAddr
        sta SndAddr,x
        lda LoadAddr+2
        sta SndAddr+2,x
.loaded:
        lda SndNow
        asl a
        asl a
        tax
        lda SndAddr,x
        sta SndBlock            ; waveStart
        lda SndAddr+2,x
        sta SndBlock+2
        lda SndLen,x            ; waveSize: its pages, the last one partly (a page of zeros after it ends it)
        and #$00FF
        beq .whole
        lda SndLen+1,x
        inc a
        bra .size
.whole: lda SndLen+1,x
.size:  sta SndBlock+4
        lda SndNow
        asl a
        tax
        lda SndFreq,x
        sta SndBlock+6          ; freqOffset
        lda Gen                 ; each generator its own 4 KB of the DOC's memory, from $8000
        xba
        asl a
        asl a
        asl a
        asl a
        clc
        adc #$8000
        sta SndBlock+8          ; docBuffer
        lda #4
        sta SndBlock+10         ; bufferSize: 4 KB
        stz SndBlock+12         ; nextWavePtr: none
        stz SndBlock+14
        lda SndVolNow
        and #$00FF
        sta SndBlock+16         ; volSetting
        lda PlayGen             ; music: a generator of its own, maybe looping (music.s)
        bmi .rot
        jmp MusStart
.rot:   lda #1                  ; stop what this generator plays
        ldx Gen
        beq .m
.sh:    asl a
        dex
        bne .sh
.m:     pha
        tool $0F08              ; FFStopSound
        lda Gen                 ; generator in the high byte, free-form synthesis (1) in the low
        xba
        ora #$0001
        pha
        pea #bank(SndBlock)
        pea #SndBlock
        tool $0E08              ; FFStartSound
        lda Gen                 ; which sound it plays (Stop Sound - ext.s)
        asl a
        tax
        lda SndNow
        sta GenSnd,x
        lda Gen                 ; the next sound on the next generator (four in turn)
        inc a
        and #3
        sta Gen
        rts

; FileName = "SND." and SndNow + 1 in figures.
SoundName:
        lda #$4E53              ; "SN"
        sta FileName
        lda #$2E44              ; "D."
        sta FileName+2
        lda SndNow
        inc a
        bra NumName

; FileName = "MAP." and WantMap in figures.
MapName:
        lda #$414D              ; "MA"
        sta FileName
        lda #$2E50              ; "P."
        sta FileName+2
        lda WantMap
; FileName + 4 = A in figures.
NumName:
        ldx #4
        sta T4
        stz T5                  ; a figure written yet
        ldy #100
        jsr .digit
        ldy #10
        jsr .digit
        lda T4
        clc
        adc #'0'
        sep #$20
        .a8
        sta FileName,x
        lda #0
        sta FileName+1,x
        rep #$20
        .a16
        rts
.digit: sty T3
        lda #0
        sta T2
.d:     lda T4
        cmp T3
        bcc .w
        sec
        sbc T3
        sta T4
        inc T2
        bra .d
.w:     lda T2
        ora T5
        beq .skip               ; no leading zeros
        sta T5
        lda T2
        clc
        adc #'0'
        sep #$20
        .a8
        sta FileName,x
        rep #$20
        .a16
        inx
.skip:  rts

; ---- the Disk Swap screen ----
; Asks for disk WantDisk until FileName can be loaded from it (a key: look again; Esc: give up - carry set).
SwapFor:
        jsr CurOff              ; the pointer hidden while drawing (mouse.s)
        lda SwapHasPic          ; the screen: its picture (or black)
        beq .black
        lda SwapPic
        sta Src
        lda SwapPic+2
        sta Src+2
        ldx #0
        ldy #0
.cp:    lda [Src],y
        sta >SCREEN,x
        iny
        iny
        inx
        inx
        cpx #32000
        bne .cp
        bra .words
.black: ldx #0
        lda #0
.bk:    sta >SCREEN,x
        inx
        inx
        cpx #32000
        bne .bk
.words: lda WantDisk            ; its number in figures (the words' {2})
        ldx #0
        cmp #10
        bcc .units
        ldy #0
.tens:  cmp #10
        bcc .tdone
        sbc #10
        iny
        bra .tens
.tdone: pha
        tya
        clc
        adc #'0'
        sta NumBuf              ; (and a 0 after it)
        inx
        pla
.units: clc
        adc #'0'
        sta NumBuf,x
        lda WantDisk            ; the words, the disk's name for {0}
        dec a
        asl a
        asl a
        asl a
        tax
        lda DiskTab+4,x
        sta FmtName
        lda DiskTab+6,x
        sta FmtName+2
        lda SwapMsg
        sta SayP
        lda SwapMsg+2
        sta SayP+2
        jsr FillFmt
        lda SwapTC
        jsr SetColor
        lda SwapTX
        pha
        lda SwapTY
        clc
        adc #8
        pha
        tool $3A04              ; MoveTo
        pea #bank(FmtBuf)
        pea #FmtBuf
        tool $A604              ; DrawCString
        lda SwapSnd             ; its sound, if it is here already
        cmp SndCount
        bcs .wait
        asl a
        asl a
        tax
        lda SndAddr,x
        ora SndAddr+2,x
        beq .wait
        lda SwapSnd
        ldx #255
        jsr PlaySound
.wait:  stz SwapFrame
        stz SwapTick
.loop:  jsr SwapAnimate
        pha
        pea $002A
        pea #bank(EvRec)
        pea #EvRec
        tool $0A06              ; GetNextEvent
        pla
        beq .loop
        lda EvMsg
        and #$007F
        cmp #$1B
        beq .giveup
        lda WantDisk            ; a key: is the disk in now?
        jsr DiskPath
        jsr LoadFile
        bcs .loop
        jsr Redraw
        clc
        rts
.giveup:
        jsr Redraw
        sec
        rts

; The game's screen again after the Disk Swap screen.
Redraw:
        jsr SetupScreen
        jsr DrawPanel
        jsr HudForce            ; (hud.s)
        jmp DrawView

; The Disk Swap screen's animation: the next frame every 8 ticks (60 a second).
SwapAnimate:
        lda SwapFrames
        bne .some
        rts
.some:  pha
        pha
        tool $2503              ; GetTick
        pla
        sta T4
        pla
        lda T4
        sec
        sbc SwapTick
        cmp #8
        bcs .next
        lda SwapTick
        bne .out
.next:  lda T4
        sta SwapTick
        lda SwapFrame           ; this frame's sprite
        asl a
        tay
        lda SwapFrameTab
        sta Src
        lda SwapFrameTab+2
        sta Src+2
        lda [Src],y
        sta SprId
        lda SwapAX              ; its place, back as the picture is there (or black) under it
        sta RX
        sta CellX
        lda SwapAY
        sta RY
        sta CellY
        jsr SpriteSize
        lda T4
        sta RW
        lda T5
        sta RH
        lda HudPtr
        pha
        lda HudPtr+2
        pha
        lda HasHud
        pha
        lda SwapPic
        sta HudPtr
        lda SwapPic+2
        sta HudPtr+2
        lda SwapHasPic
        sta HasHud
        jsr RestoreRect
        pla
        sta HasHud
        pla
        sta HudPtr+2
        pla
        sta HudPtr
        jsr DrawPic
        lda SwapFrame
        inc a
        cmp SwapFrames
        bcc .f
        lda #0
.f:     sta SwapFrame
.out:   rts

; T4, T5 = sprite SprId's width and height.
SpriteSize:
        lda SprId
        asl a
        asl a
        tay
        lda [SprTab],y
        sta T4
        iny
        iny
        lda [SprTab],y
        sta T5
        clc
        lda SprBank
        adc T4
        sta Src
        lda SprBank+2
        adc T5
        sta Src+2
        lda [Src]
        sta T4
        ldy #2
        lda [Src],y
        sta T5
        rts
