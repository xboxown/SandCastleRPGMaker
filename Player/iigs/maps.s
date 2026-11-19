; Sandcastle RPG Maker - Apple IIgs player: the maps (the Maps tab's worlds) and their events.
; Map 0 is in GAME.DAT; map n (1-15) is the file MAP.n - "SCMP", width, height, start x, y, the cells, the characters
; (16 bytes each), the events - loaded the first time the game goes there (from its disk: the Disk Swap screen may ask)
; and kept as it is after. The objects of every map are in GAME.DAT, their flags' top 4 bits saying which map.
; An event (x, y, the code's length, the code) runs when the Avatar steps onto it: the talk code (talk.s, RunTalk) -
; Show Text, variables, Play Sound, Change Map.

; MapTab: 32 bytes a map
MT_W   = 0
MT_H   = 2
MT_SX  = 4
MT_SY  = 6
MT_MAP = 8
MT_CC  = 12
MT_CP  = 14
MT_EC  = 18
MT_EP  = 20
MT_IN  = 24
MT_SC  = 26                     ; the schedules: where (4), how many (2)
MT_SN  = 30

; GAME.DAT after the Disk Swap screen: how many maps, map 0's events. Map 0 as ParseData found it.
ParseMaps:
        lda [Ptr]
        sta MapCount
        cmp #17
        bcc .n
        lda #16
        sta MapCount
.n:     lda #2
        jsr AddPtr
        lda [Ptr]
        sta EvCount
        lda #2
        jsr AddPtr
        lda Ptr
        sta EvPtr
        lda Ptr+2
        sta EvPtr+2
        lda EvCount             ; past them: the Avatar's stats, the levels (combat.s)
        sta Item
.ev:    lda Item
        beq .evd
        ldy #6
        lda [Ptr],y
        clc
        adc #8
        jsr AddPtr
        dec Item
        bra .ev
.evd:   jsr ParseAvatar
        ldx #0                  ; map 0: as it is now
        lda MapW
        sta MapTab+MT_W,x
        lda MapH
        sta MapTab+MT_H,x
        lda PX
        sta MapTab+MT_SX,x
        lda PY
        sta MapTab+MT_SY,x
        jsr KeepMap
        lda #1
        sta MapTab+MT_IN
        rts

; MapTab (X = its place) <- the map's cells, characters, events now in use.
KeepMap:
        lda MapPtr
        sta MapTab+MT_MAP,x
        lda MapPtr+2
        sta MapTab+MT_MAP+2,x
        lda ChrCount
        sta MapTab+MT_CC,x
        lda ChrPtr
        sta MapTab+MT_CP,x
        lda ChrPtr+2
        sta MapTab+MT_CP+2,x
        lda EvCount
        sta MapTab+MT_EC,x
        lda EvPtr
        sta MapTab+MT_EP,x
        lda EvPtr+2
        sta MapTab+MT_EP+2,x
        lda SchPtr
        sta MapTab+MT_SC,x
        lda SchPtr+2
        sta MapTab+MT_SC+2,x
        lda SchCount
        sta MapTab+MT_SN,x
        rts

; X = map A's place in MapTab
MapOff:
        asl a
        asl a
        asl a
        asl a
        asl a
        tax
        rts

; Goes to map GoMap ($FFFF: this one) at GoX, GoY ($FFFF: its start). Carry set: it couldn't (its file not to be had).
ChangeMap:
        lda GoMap
        cmp #$FFFF
        bne .other
        lda CurMap
        sta GoMap
.other: lda GoMap
        cmp MapCount
        bcc .ok
        sec
        rts
.ok:    cmp CurMap
        bne .go
        brl .place
.go:    jsr MapOff
        lda MapTab+MT_IN,x
        bne .loaded
        lda GoMap
        jsr LoadMap
        bcc .loaded
        rts
.loaded:
        lda GoMap               ; that map's cells, characters, events
        sta CurMap
        jsr MapOff
        lda MapTab+MT_W,x
        sta MapW
        lda MapTab+MT_H,x
        sta MapH
        lda MapTab+MT_MAP,x
        sta MapPtr
        lda MapTab+MT_MAP+2,x
        sta MapPtr+2
        lda MapTab+MT_CC,x
        sta ChrCount
        lda MapTab+MT_CP,x
        sta ChrPtr
        lda MapTab+MT_CP+2,x
        sta ChrPtr+2
        lda MapTab+MT_EC,x
        sta EvCount
        lda MapTab+MT_EP,x
        sta EvPtr
        lda MapTab+MT_EP+2,x
        sta EvPtr+2
        lda MapTab+MT_SC,x
        sta SchPtr
        lda MapTab+MT_SC+2,x
        sta SchPtr+2
        lda MapTab+MT_SN,x
        sta SchCount
        lda CurMap              ; its objects: flags' top 4 bits
        asl a
        asl a
        asl a
        asl a
        xba
        and #$FF00
        sta CurBits
        ora #1
        sta CurBits1
        lda OccHandle+2         ; the cell map again, for this map
        pha
        lda OccHandle
        pha
        tool $1002              ; DisposeHandle
        jsr MakeOcc
        jsr ApplySchedules      ; there: everyone to this period's activity
.place: lda GoX                 ; where: the spot, else the map's start
        cmp #$FFFF
        bne .spot
        lda CurMap
        jsr MapOff
        lda MapTab+MT_SX,x
        sta PX
        lda MapTab+MT_SY,x
        sta PY
        jsr PlaceParty          ; the members round the Avatar (party.s)
        clc
        rts
.spot:  lda GoX
        sta PX
        lda GoY
        sta PY
        jsr PlaceParty
        clc
        rts

; Loads MAP.A into its MapTab place. Carry set: not to be had (or not a map file).
LoadMap:
        sta WantMap
        jsr MapName
        jsr FindFile
        bcc .got
        rts
.got:   lda LoadAddr
        sta Ptr
        lda LoadAddr+2
        sta Ptr+2
        lda [Ptr]
        cmp #$4353              ; "SC"
        bne .bad
        ldy #2
        lda [Ptr],y
        cmp #$504D              ; "MP"
        beq .good
.bad:   sec
        rts
.good:  lda #4
        jsr AddPtr
        lda WantMap
        jsr MapOff
        lda [Ptr]
        sta MapTab+MT_W,x
        sta MulA
        ldy #2
        lda [Ptr],y
        sta MapTab+MT_H,x
        asl a
        sta MB0
        ldy #4
        lda [Ptr],y
        sta MapTab+MT_SX,x
        ldy #6
        lda [Ptr],y
        sta MapTab+MT_SY,x
        lda #8
        jsr AddPtr
        lda Ptr
        sta MapTab+MT_MAP,x
        lda Ptr+2
        sta MapTab+MT_MAP+2,x
        jsr Mul16               ; past the cells: width x height x 2
        jsr AddPtrLong
        lda WantMap
        jsr MapOff
        lda [Ptr]
        sta MapTab+MT_CC,x
        lda #2
        jsr AddPtr
        lda Ptr
        sta MapTab+MT_CP,x
        lda Ptr+2
        sta MapTab+MT_CP+2,x
        lda MapTab+MT_CC,x      ; 64 bytes each
        asl a
        asl a
        asl a
        asl a
        asl a
        asl a
        jsr AddPtr
        lda WantMap
        jsr MapOff
        lda [Ptr]
        sta MapTab+MT_EC,x
        lda #2
        jsr AddPtr
        lda Ptr
        sta MapTab+MT_EP,x
        lda Ptr+2
        sta MapTab+MT_EP+2,x
        lda MapTab+MT_EC,x      ; past the events: the schedules
        sta Item
.ev:    lda Item
        beq .evd
        ldy #6
        lda [Ptr],y
        clc
        adc #8
        jsr AddPtr
        dec Item
        bra .ev
.evd:   lda WantMap
        jsr MapOff
        lda [Ptr]
        sta MapTab+MT_SN,x
        lda #2
        jsr AddPtr
        lda Ptr
        sta MapTab+MT_SC,x
        lda Ptr+2
        sta MapTab+MT_SC+2,x
        lda #1
        sta MapTab+MT_IN,x
        clc
        rts

WantMap:  .dw 0
