; Sandcastle RPG Maker - Apple IIgs player: music, as the Windows Ultima 6 engine (U6GameWindow.Ambient.cs,
; CombatMusic.cs, U6Talk.cs, ScriptGame.cs). Each kind on a DOC generator of its own, looping through the Sound Tool
; Set's nextWavePtr (the block pointing at itself): 4 the map's ambient sound, 5 the combat music, 6 an event's music,
; 7 the conversation music. The ambient sound is the part of the day's (Day & Night's hours) or the zone's (a Cave:
; none); combat music, a battle or a talk silence it. Combat music as Exult's: danger when a foe is near (checked once
; a second), battle when a fight starts (not within half a minute of the last), escaped when the foes are gone, run
; away when combat is left with a foe close, victory (battle over after a long one) when the last foe falls, near
; death below a quarter of the hit points with a foe about, death.

GEN_AMB  = 4
GEN_CUE  = 5
GEN_EVT  = 6
GEN_TALK = 7
CUE_DANGER = 0
CUE_BATTLE = 1
CUE_RUN    = 3
CUE_ESC    = 4
CUE_WIN    = 5
CUE_OVER   = 6
CUE_NEAR   = 7
CUE_DEATH  = 8

ParseMusic:
        lda [Ptr]
        sta AmbCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta AmbPtr
        lda Ptr+2
        sta AmbPtr+2
        lda AmbCount
        asl a
        asl a
        asl a
        jsr AddPtr
        lda [Ptr]
        sta ZnCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta ZnPtr
        lda Ptr+2
        sta ZnPtr+2
        lda ZnCount
        asl a
        sta MT
        asl a
        asl a
        asl a
        sec
        sbc MT                  ; 14 bytes each
        jsr AddPtr
        ldy #0                  ; the combat music: 9 x sound, loop
.c:     lda [Ptr],y
        sta CueTab,y
        iny
        iny
        cpy #36
        bcc .c
        lda #36
        jsr AddPtr
        lda [Ptr]
        sta TalkSong
        ldy #2
        lda [Ptr],y
        sta AfterRnd
        lda #4
        jsr AddPtr
        jmp ParseWeather        ; (weather.s)

; ---- generators ----

; Sound A on generator X, looping if Y, at volume MVol (0-255).
GenStart:
        stx PlayGen
        sty PlayLoop
        ldx MVol
        jsr PlaySound
        lda #$FFFF
        sta PlayGen
        rts

; (PlaySound's end for a music generator: its block copied to the generator's own, pointing at itself to loop.)
MusStart:
        lda PlayGen
        sec
        sbc #4
        asl a
        asl a
        sta MT
        asl a
        asl a
        clc
        adc MT                  ; x 20
        sta MBOff
        tax
        ldy #0
.c:     lda SndBlock,y
        sta MBlk,x
        inx
        inx
        iny
        iny
        cpy #20
        bcc .c
        ldx MBOff
        lda PlayGen             ; docBuffer: the generator's own 4 KB
        xba
        asl a
        asl a
        asl a
        asl a
        clc
        adc #$8000
        bne .db
        lda #$7000              ; (generator 8, the rain: below the others)
.db:    sta MBlk+8,x
        stz MBlk+12,x
        stz MBlk+14,x
        lda PlayLoop
        beq .nl
        txa
        clc
        adc #MBlk
        sta MBlk+12,x
        lda MyBank
        sta MBlk+14,x
.nl:    ldx PlayGen
        jsr StopGen
        lda PlayGen
        xba
        ora #$0001
        pha
        lda MyBank
        pha
        lda MBOff
        clc
        adc #MBlk
        pha
        tool $0E08              ; FFStartSound
        rts

; Stops generator X.
StopGen:
        lda #1
        cpx #0
        beq .m
.s:     asl a
        dex
        bne .s
.m:     pha
        tool $0F08              ; FFStopSound
        rts

; Carry set if generator X is playing.
GenPlaying:
        lda #1
        cpx #0
        beq .m
.s:     asl a
        dex
        bne .s
.m:     sta MT
        pha
        tool $1008              ; FFSoundStatus
        pla
        and MT
        beq .no
        sec
        rts
.no:    clc
        rts

; Generator X's volume to A (0-255), with Set Volume's share.
SetGenVol:
        stx PlayGen
        sta SndVolNow
        jsr VolScale
        lda SndVolNow
        and #$00FF
        pha
        lda PlayGen
        pha
        tool $0D08              ; SetSoundVolume
        lda #$FFFF
        sta PlayGen
        rts

; SndVolNow x Set Volume's music (a music generator) or sounds share / 100.
VolScale:
        lda PlayGen
        bmi .s
        lda MusVol
        bra .m
.s:     lda SndPct
.m:     cmp #100
        bcs .o
        sta ExB
        lda SndVolNow
        and #$00FF
        jsr SMul
        pha
        lda #100
        sta ExB
        pla
        jsr SDiv
        lda ExQ
        sta SndVolNow
.o:     rts

; ---- each pass of the main loop ----
MusicRun:
        lda Mode
        cmp #12
        bcc .go
        rts                     ; the title: none
.go:    jsr FadeRun
        pha
        pha
        tool $2503              ; GetTick
        pla
        sta MNow
        pla
        lda MNow
        sec
        sbc MusTick
        cmp #30
        bcs .t
        rts
.t:     lda MNow
        sta MusTick
        jsr NearNow             ; near death (not Exult)
        cmp NearD
        beq .nd
        sta NearD
        cmp #0
        beq .rec
        lda #CUE_NEAR
        jsr PlayCue
        bra .nd
.rec:   lda CueNow
        cmp #CUE_NEAR
        bne .nd
        jsr StopCue
.nd:    lda NearD
        beq .b
        lda CueNow
        cmp #CUE_NEAR
        bne .b
        jsr CuePlaying
        bcs .amb
.b:     lda CombatOn
        beq .dg
        jsr StartBattle
.dg:    lda MNow                ; danger, once a second
        sec
        sbc DangT
        cmp #60
        bcc .amb
        lda MNow
        sta DangT
        lda CombatOn
        bne .amb
        jsr FoeNear
        bcc .nof
        jsr CuePlaying
        bcs .amb
        lda #CUE_DANGER
        jsr PlayCue
        bra .amb
.nof:   jsr CuePlaying
        bcc .amb
        lda CueNow
        cmp #CUE_ESC
        bcs .amb
        cmp #CUE_RUN
        beq .amb
        lda #CUE_ESC            ; danger or battle music, the foes gone: escaped
        jsr PlayCue
.amb:   jmp Ambient

; A = 1 if the Avatar is below a quarter of its hit points with a foe about.
NearNow:
        lda AvDead
        bne .no
        lda AvRec+18            ; max hp / 4
        lsr a
        lsr a
        sta MT
        lda AvRec+16
        cmp MT
        bcs .no
        lda CombatOn
        bne .yes
        jsr FoeNear
        bcc .no
.yes:   lda #1
        rts
.no:    lda #0
        rts

; Exult's start_battle: battle music if half a minute has gone since the last.
StartBattle:
        lda Started
        bne .o
        jsr FoeNear
        bcc .o
        lda BattleEver
        beq .play
        lda MNow
        sec
        sbc BattleT
        cmp #1800
        bcc .st
.play:  lda #CUE_BATTLE
        jsr PlayCue
        lda MNow
        sta BattleT
        lda #1
        sta BattleEver
        sta Raging
.st:    lda #1
        sta Started
.o:     rts

; Combat mode turned on or off: off with a foe close by - run away.
CombatChanged:
        stz Started
        lda CombatOn
        bne .o
        lda AvDead
        bne .o
        jsr FoeNear
        bcc .o
        lda #CUE_RUN
        jsr PlayCue
.o:     rts

; A blow landed on the Avatar or a member during the danger music: the battle music (Battle first).
PartyHitMus:
        lda DefP+2
        cmp MyBank
        bne .o
        lda DefP
        cmp #AvRec
        beq .p
        sec
        sbc #PartyTab
        cmp #512
        bcs .o
.p:     lda CueNow
        cmp #CUE_DANGER
        bne .o
        jsr CuePlaying
        bcc .o
        lda #CUE_BATTLE
        ldx CueTab+4
        cpx #$FFFF
        bne .x
        lda #2
.x:     jmp PlayCueX
.o:     rts

; DefP (a foe) killed by the Avatar or a member: none left near - victory, or after a long fight now and then battle over.
KilledMus:
        lda AtkP+2
        cmp MyBank
        bne .o
        lda AtkP
        cmp #AvRec
        beq .k
        sec
        sbc #PartyTab
        cmp #512
        bcs .o
.k:     ldy #C_FLAGS
        lda [DefP],y
        and #2
        beq .o
        lda Raging
        beq .o
        jsr FoeNear
        bcs .o
        stz Raging
        pha
        pha
        tool $2503
        pla
        sta MNow
        pla
        lda MNow                ; the fight's seconds
        sec
        sbc BattleT
        sta ExA
        lda #60
        sta ExB
        lda ExA
        jsr SDiv
        lda ExQ
        sta MT
        cmp #16
        bcc .win
        lda #60
        jsr Random
        cmp MT
        bcs .win
        lda #CUE_OVER
        jmp PlayCue
.win:   lda #CUE_WIN
        jmp PlayCue
.o:     rts

; The Avatar died: the death music.
DeathMus:
        stz NearD
        jsr StopCue
        lda #CUE_DEATH
        jmp PlayCue

; Combat cue A (Battle: Battle or Battle 2, either if both): the ambient silenced, its sound on its generator.
PlayCue:
        cmp #CUE_BATTLE
        bne PlayCueX
        lda CueTab+4
        cmp #$FFFF
        beq .b2
        lda CueTab+8
        cmp #$FFFF
        beq .b1
        lda #2
        jsr Random
        cmp #0
        beq .b1
.b2:    lda #2
        bra PlayCueX
.b1:    lda #CUE_BATTLE
PlayCueX:
        sta MCue
        jsr SilenceAmb
        ldx #GEN_CUE
        jsr StopGen
        lda MCue
        asl a
        asl a
        tax
        lda CueTab,x
        cmp #$FFFF
        beq .none
        pha
        lda CueTab+2,x
        tay
        lda #255
        sta MVol
        pla
        ldx #GEN_CUE
        jsr GenStart
        lda MCue
        sta CueNow
        rts
.none:  lda #$FFFF
        sta CueNow
        rts

StopCue:
        ldx #GEN_CUE
        jsr StopGen
        lda #$FFFF
        sta CueNow
        rts

; Carry set if a combat cue plays (a cue that ended: none).
CuePlaying:
        lda CueNow
        cmp #$FFFF
        beq .no
        ldx #GEN_CUE
        jsr GenPlaying
        bcs .yes
        lda #$FFFF
        sta CueNow
.no:    clc
.yes:   rts

; Carry set if a living hostile character is within 9 squares of the Avatar (its own walk: Src borrowed).
FoeNear:
        lda Src
        pha
        lda Src+2
        pha
        lda ChrPtr
        sta Src
        lda ChrPtr+2
        sta Src+2
        lda ChrCount
        sta MN
.l:     lda MN
        beq .no
        ldy #C_FLAGS
        lda [Src],y
        and #2+4
        cmp #2
        bne .n
        lda [Src]               ; |dx| and |dy| below 9
        sec
        sbc PX
        bpl .x
        eor #$FFFF
        inc a
.x:     cmp #9
        bcs .n
        ldy #2
        lda [Src],y
        sec
        sbc PY
        bpl .y
        eor #$FFFF
        inc a
.y:     cmp #9
        bcc .yes
.n:     lda Src
        clc
        adc #64
        sta Src
        lda Src+2
        adc #0
        sta Src+2
        dec MN
        bra .l
.no:    pla
        sta Src+2
        pla
        sta Src
        clc
        rts
.yes:   pla
        sta Src+2
        pla
        sta Src
        sec
        rts

; ---- the map's ambient sound ----
SilenceAmb:
        lda AmbNow
        cmp #$FFFE
        beq .o
        ldx #GEN_AMB
        jsr StopGen
        lda #$FFFE
        sta AmbNow
.o:     rts

Ambient:
        lda TalkOn
        bne .sil
        jsr CuePlaying
        bcs .sil
        lda CombatOn
        beq .want
        lda Started
        beq .want
        jsr FoeNear
        bcc .want
.sil:   jmp SilenceAmb
.want:  jsr MapSong             ; the part of the day's
        sta MSong
        jsr ZoneSong            ; a zone's of its own (a Cave: none)
        lda MHold               ; after a talk: a random song of the map's, until the hour's own changes
        beq .cmp
        lda MHoldDue
        cmp #$FFFE
        bne .hd
        lda MSong
        sta MHoldDue
.hd:    lda MSong
        cmp MHoldDue
        bne .unhold
        lda MHoldRnd
        sta MSong
        bra .cmp
.unhold: stz MHold
.cmp:   lda MSong
        cmp AmbNow
        beq .o
        sta AmbNow
        ldx #GEN_AMB
        jsr StopGen
        lda AmbNow
        cmp #$FFFF
        beq .o
        lda #255
        sta MVol
        lda AmbNow
        ldx #GEN_AMB
        ldy #1
        jmp GenStart
.o:     rts

; A = this map's song for the hour now ($FFFF none); MSongs its four.
MapSong:                         ; (AmbPtr through Src, Src kept)
        lda Src
        pha
        lda Src+2
        pha
        lda AmbPtr
        sta Src
        lda AmbPtr+2
        sta Src+2
        jsr MapSong0
        tax
        pla
        sta Src+2
        pla
        sta Src
        txa
        rts
MapSong0:
        lda CurMap
        cmp AmbCount
        bcc .m
        lda #$FFFF
        rts
.m:     asl a
        asl a
        asl a
        tay
        ldx #0
.c:     lda [Src],y
        sta MSongs,x
        iny
        iny
        inx
        inx
        cpx #8
        bcc .c
        ldx #6                  ; night
        lda Hour
        cmp DNDawn
        bcc .p
        ldx #0                  ; dawn
        cmp DNDay
        bcc .p
        ldx #2                  ; day
        cmp DNDusk
        bcc .p
        ldx #4                  ; dusk
        cmp DNNight
        bcc .p
        ldx #6
.p:     lda MSongs,x
        rts

; MSong = the first zone of this map holding the Avatar's (sound or none), if any.
ZoneSong:                         ; (ZnPtr through Src, Src kept)
        lda Src
        pha
        lda Src+2
        pha
        lda ZnPtr
        sta Src
        lda ZnPtr+2
        sta Src+2
        jsr ZoneSong0
        tax
        pla
        sta Src+2
        pla
        sta Src
        txa
        rts
ZoneSong0:
        lda ZnCount
        beq .o
        sta MN
        ldy #0
.l:     lda [Src],y
        cmp CurMap
        bne .n
        lda PX
        sec
        iny
        iny
        sbc [Src],y           ; x
        bcc .n2
        iny
        iny
        iny
        iny
        cmp [Src],y           ; < w
        bcs .n6
        lda PY
        sec
        dey
        dey
        sbc [Src],y           ; y
        bcc .n4
        iny
        iny
        iny
        iny
        cmp [Src],y           ; < h
        bcs .n8
        iny
        iny
        lda [Src],y
        sta MSong
.o:     rts
.n2:    dey
        dey
        bra .n
.n4:    dey
        dey
        dey
        dey
        bra .n
.n6:    dey
        dey
        dey
        dey
        dey
        dey
        bra .n
.n8:    tya
        sec
        sbc #8
        tay
.n:     tya
        clc
        adc #14
        tay
        dec MN
        bne .l
        rts

; ---- the conversation music ----
TalkMusicOn:
        lda TalkOn
        bne .o
        lda TalkSong
        cmp #$FFFF
        beq .o
        lda #1
        sta TalkOn
        jsr SilenceAmb
        lda #255
        sta MVol
        lda TalkSong
        ldx #GEN_TALK
        ldy #1
        jmp GenStart
.o:     rts

TalkMusicOff:
        lda TalkOn
        beq .o
        stz TalkOn
        ldx #GEN_TALK
        jsr StopGen
        lda #$FFFE
        sta AmbNow
        lda AfterRnd            ; after the talk: a random one of the map's songs
        beq .o
        jsr MapSong
        ldx #0
        stz MN
.c:     lda MSongs,x            ; the songs there are (repeats count once more: near enough Distinct)
        cmp #$FFFF
        beq .n
        ldy MN
        sta MPick,y
        iny
        iny
        sty MN
.n:     inx
        inx
        cpx #8
        bcc .c
        lda MN
        beq .o
        lsr a
        jsr Random
        asl a
        tax
        lda MPick,x
        sta MHoldRnd
        lda #$FFFE
        sta MHoldDue
        lda #1
        sta MHold
.o:     rts

; Everything stops (the title).
MusicOff:
        ldx #GEN_AMB
        jsr StopGen
        jsr StopCue
        ldx #GEN_EVT
        jsr StopGen
        ldx #GEN_TALK
        jsr StopGen
        lda #$FFFE
        sta AmbNow
        lda #$FFFF
        sta EvtNow
        stz TalkOn
        stz FadeDir
        stz NearD
        stz Started
        stz Raging
        stz MHold
        jmp WeatherOff          ; (weather.s)

; ---- events' music ----
; MUSIC sound loop volume fade
OpMusic:
        iny
        lda [TalkPtr],y
        sta MSong
        iny
        iny
        lda [TalkPtr],y
        and #$00FF
        sta PlayLoop2
        iny
        lda [TalkPtr],y
        and #$00FF
        sta MVol2
        iny
        lda [TalkPtr],y
        sta MFade
        stz FadeDir
        lda MSong
        cmp #$FFFF
        bne .play
        lda MFade               ; stop: at once, or faded out
        beq .stop
        lda #2
        bra .fade
.stop:  ldx #GEN_EVT
        jsr StopGen
        lda #$FFFF
        sta EvtNow
        bra .n
.play:  sta EvtNow
        lda MVol2
        sta EvtVol
        sta MVol
        lda MFade
        beq .st
        stz MVol                ; faded in from silence
.st:    lda MSong
        ldx #GEN_EVT
        ldy PlayLoop2
        jsr GenStart
        lda MFade
        beq .n
        lda #1
.fade:  sta FadeDir
        lda MFade
        sta FadeLen
        stz FadeStep
        pha
        pha
        tool $2503
        pla
        sta FadeT0
        pla
.n:     lda #7
        jmp Next

; The event music's fade: 16 steps.
FadeRun:
        lda FadeDir
        bne .go
        rts
.go:    pha
        pha
        tool $2503
        pla
        sta MT
        pla
        lda MT
        sec
        sbc FadeT0
        cmp FadeLen
        bcc .part
        lda FadeDir             ; done
        stz FadeDir
        cmp #2
        beq .end
        lda EvtVol
        ldx #GEN_EVT
        jmp SetGenVol
.end:   ldx #GEN_EVT
        jsr StopGen
        lda #$FFFF
        sta EvtNow
        rts
.part:  sta ExA                 ; elapsed / (len / 16 + 1): 0..15
        lda FadeLen
        lsr a
        lsr a
        lsr a
        lsr a
        inc a
        sta ExB
        lda ExA
        jsr SDiv
        lda ExQ
        cmp FadeStep
        beq .o
        sta FadeStep
        ldx FadeDir
        cpx #1
        beq .in
        eor #$FFFF
        clc
        adc #16
.in:    sta ExB
        lda EvtVol
        jsr SMul
        lsr a
        lsr a
        lsr a
        lsr a
        ldx #GEN_EVT
        jmp SetGenVol
.o:     rts

; VOLUME music sounds (formulas, 0-100)
OpVolume:
        iny
        jsr EvalExpr
        jsr Pct
        sta MusVol
        jsr EvalExpr
        jsr Pct
        sta SndPct
        sty TPC
        lda EvtNow              ; what plays now: its new volume
        cmp #$FFFF
        beq .a
        lda EvtVol
        ldx #GEN_EVT
        jsr SetGenVol
.a:     ldx #GEN_AMB
        lda #255
        jsr SetGenVol
        ldx #GEN_CUE
        lda #255
        jsr SetGenVol
        ldx #GEN_TALK
        lda #255
        jsr SetGenVol
        ldy TPC
        sty TPC
        jmp RunTalk
Pct:    cmp #$8000
        bcc .p
        lda #0
.p:     cmp #101
        bcc .o
        lda #100
.o:     rts

; RESUME: the event and combat music stop, the map's own sound again.
OpResume:
        ldx #GEN_EVT
        jsr StopGen
        lda #$FFFF
        sta EvtNow
        stz FadeDir
        jsr StopCue
        lda #$FFFE
        sta AmbNow
        lda #1
        jmp Next

PlayGen:  .dw $FFFF
PlayLoop: .dw 0
PlayLoop2: .dw 0
MVol:     .dw 255
MVol2:    .dw 0
MusVol:   .dw 100
SndPct:   .dw 100
MBOff:    .dw 0
MBlk:     .ds 100
MT:       .dw 0
MN:       .dw 0
MNow:     .dw 0
MusTick:  .dw 0
DangT:    .dw 0
MCue:     .dw 0
CueNow:   .dw $FFFF
CueTab:   .ds 36
NearD:    .dw 0
Started:  .dw 0
Raging:   .dw 0
BattleEver: .dw 0
BattleT:  .dw 0
AmbCount: .dw 0
AmbPtr:   .dd 0
ZnCount:  .dw 0
ZnPtr:    .dd 0
AmbNow:   .dw $FFFE
MSong:    .dw 0
MSongs:   .ds 8
MPick:    .ds 8
MHold:    .dw 0
MHoldDue: .dw 0
MHoldRnd: .dw 0
TalkSong: .dw $FFFF
AfterRnd: .dw 0
TalkOn:   .dw 0
EvtNow:   .dw $FFFF
EvtVol:   .dw 255
FadeDir:  .dw 0
FadeT0:   .dw 0
FadeLen:  .dw 0
FadeStep: .dw 0
MFade:    .dw 0
