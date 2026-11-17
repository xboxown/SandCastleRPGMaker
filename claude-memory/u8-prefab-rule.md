---
name: u8-prefab-rule
description: Ultima 8 = a project Style in the same editor + a NEW separate U8 engine copied from U7 and modified; U7 engine never touched; Amiga/MorphOS catch-up on Saturdays
metadata:
  node_type: memory
  type: feedback
  originSessionId: a5ab90be-f68e-4703-813c-ded51d5a193c
  modified: 2026-10-28T02:37:30.506Z
---

Agreed 2026-10-27 (after b471), user's words: "I am scared you will damage ultima 7 engine".

- File > New Project: name, location, **Style: Ultima 7 / Ultima 8**. Ultima 7 = everything built so far, unchanged.
- ONE editor, shared, unchanged for U7. U8 projects get MORE: extra editor features, extra event commands, U8 sprite style - shown only when the project's Style is Ultima 8.
- A NEW, separate U8 game engine. I may copy up to 100% of the U7 engine into it and modify the copy; the U7 engine files are never touched for U8.
- Rendering/behaviour matches Ultima 8, referenced from **Pentagram** (U8's Exult). Exception: party members can join, as in U7.
- After each U8 build, check a U7 project still builds and plays as before.
- Amiga 68k / MorphOS: the user wants every U7 engine update there too, but does the C-player work in a batch **on Saturdays** ("this does take long time") - U8 first on weekdays, Amiga/MorphOS 100% on Saturday (U7 changes and the U8 engine's C port).

Foundation built b472 (U8-1): git tag u7-final-b471 = frozen U7. ProjectManifest.Style ("Ultima7"/"Ultima8", missing = U7); New Project has a Style box; Editor/U8/U8GameWindow*.cs (+ .xaml) = the U8 engine, a renamed copy of GameTestWindow (ScriptStateWindow -> U8ScriptStateWindow); MainWindow.U8.cs launches it for Game Test and built games; Build dialog: U8 projects Windows-only until the C port. Run `sh tools/u7-guard.sh` before every U8 commit (lists U7 engine files changed since the tag) - installing a git commit-msg hook was blocked by the permission system, so it is a manual check. Shared Editor/Engine helper classes are used by both engines: when U8 needs one changed, copy it into Editor/U8 first rather than editing the shared one.

U8 engine progress (2026-10-27): b473 view (Editor/U8/U8ProjectionCanvas.cs: map (x,y) -> (x-y+OriginX, (x+y)/2), placements Thing/Character/Cell/Center/Screen, square map cell = sheet tile H, ground chunks as diamonds, camera/culling/clicks projected); b474 no window pauses (books too, by the user's rule - Pentagram's ReadableGump does pause); b475 movement (U8Movement.cs: screen-relative 8 dirs, Shift run, right mouse walk step/walk/run by Pentagram thresholds, J / both buttons jump, climb 2-9 lifts, fall damage 80/104 units with 8 units per lift, PoseDatabase.EnsureUltima8Poses); b476 combat (U8Combat.cs: manual swing/kick/block, Pentagram to-hit/damage/armour; WeaponSettings/ArmorSettings U8 fields); b477 paperdoll stats + SystemSettings.Ultima8 (Engine/Ultima8Model.cs, System Settings tab only for U8); b478 conversations (U8Talk.cs: barks over the speaker, answers over the Avatar, no pause); b479 magic (spell School, Spellbook U8Focus + charges, Cast pose). Not done yet: the editor's Map Editor drawn in the U8 diamond view (shared editor code - needs care), the U8 engine's C port (Saturday), real U8-style art to test with. Test tools: scratchpad u8game (U8 bundle from I:	est with a NEW project id), winkeys.ps1 (PostMessage keys/clicks to the game window only - never keybd_event, it types into whatever window is in front).

**Why:** U7 is "100% complete" to the user; U8 work must not break or drift it.
**How to apply:** before editing a file for U8, make sure it is U8-only code (the copy) or a Style-guarded addition in the shared editor. Related: [[long-term-ultima-family]], [[all-platforms-together]].
