---
name: combat-music
description: "b346 combat music (System Settings > Combat) - Exult rules researched, what's not Exult, ambient vs weather rule; not play-tested yet"
metadata:
  node_type: memory
  type: project
  originSessionId: 4c4865e7-5d58-487c-9aef-047dc28694f3
  modified: 2026-10-18T21:01:55.952Z
---

2026-10-18, b346 (request interrupted once by a Windows crash, redone): combat music in Editor/GameTestWindow.CombatMusic.cs, settings CombatSettings.Music (CombatMusicCue = MusicId + Loop).

Exult facts (combat.cc): every combat track plays ONCE (start_music_combat continuous=false); Danger = not in combat mode + hostile fighting on screen (checked ~1s); Battle = CSAttacked1/2 random, max once per 30 s; a hit on the party during Danger switches to Battle; Run Away = combat off with foe within 18 tiles; Hidden Danger ("Escaped") = hostiles gone during Danger/Battle; Victory/Battle Over only when the player or party kills an evil monster and none is left nearby (hard if >15 s and rand(60)<secs). CSAvatar_died exists but is never played.

User's own (not Exult, loop by default): Near death (<1/4 HP with enemy around), Death (loops until Save/Load opens or a load). Ambient (birds) silent during combat music or battle; weather (rain/thunder) keeps playing. Dead: double-click body opens Save/Load; Save button hidden.

**Why:** user wanted the five cues + death music mimicking Exult's looping rules.
**How to apply:** not play-tested yet; check here first if the user reports combat music issues. Related [[mimic-exult-100]].
