---
name: issues-odt-2026-11-03
description: Third issues.odt (edited 2026-11-02) - b490-498 did all but the C player's pixel-perfect frame; what's untested; Scenario Developer design
metadata:
  type: project
---

User said "do all of them" 2026-11-03, on U7 and U8 engines both (commits prefixed "U7:"; the guard lists those files, expected, until the user tags them approved).

Done: b490 Select Item tabs, Starvation kills (SystemSettings.StarvationKills, null = off U7, on U8), No Body, Immortal, only hurt by weapon/spell (_specialWound + DamageAllowed), Ghost movement. b491 chairs (NothingBetween, NearestCellWithClearWayToSeat), CantSit/OutdoorsOnly verbose. b492 Kite type, Music box. b493 stats controls (StatsDoll.ControlsDesignId, a per-character DollDesign laid over the window). b494 Scenario Developer. b495 Joining (Exult slime frames + rules, Joined Monsters group, JoiningGuideWindow). b497 U8 see-through on slot (U8Equip.cs), C pointer fix, fonts.sdbx (PlayerFontPack). b498 CommandList Guides 24-31.

Scenario Developer: Engine/ScenarioModel.cs in SystemSettings.Scenarios; ScenarioRules.Prepare applies it to COPIES at MainWindow level; scenario keys fire on key UP at window level (a window closed on key down loses the up, and WPF then flags the next press IsRepeat).

**NOT done - tell the user if asked:** "Amiga/MorphOS not pixel perfect". Cause found: the C player's frame is fixed 320x200 (SCREEN_W/H) then doubled, while WPF draws art at full screen resolution, so anything finer than 320x200 (gumps at 200/240 scale, book text, fine art) is thrown away. Fix = a higher-resolution frame, at least on MorphOS (SCREEN_W used across 19 files); needs the user's machines to judge. Proposed for a Saturday batch.

Test methods found: editor takes a .msrp argument and posted F5 works (scratchpad run_scn.ps1, project from u8game bundle + sdbx copied beside it), so Game Test can be driven. SANDCASTLE_PERF_LOG + SoakLog lines record joining/kite/no body/immortal.

Not driven by me: kite, music box, weapon/spell gates, chair, stats controls, U8 see-through, ghost (all need real mouse or fights); anything on real Amiga/MorphOS. Odd result: in one test F3 straight after closing the Script Data window sometimes didn't jump (background test process can't take focus; suspected, not proven).

Related: [[u8-prefab-rule]], [[saturday-c-port-list]], [[commandlist-progress]], [[player-still-to-do]].
