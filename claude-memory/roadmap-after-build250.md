---
name: roadmap-after-build250
description: "User's stated roadmap as of 2026-10-06 (build 250) - testing phase, then house/paperdoll editors, spellbook rework, event editor push, map editor floors"
metadata:
  node_type: memory
  type: project
  originSessionId: 9366c65b-5b69-4741-b437-296f25901c06
  modified: 2026-10-09T22:08:59.302Z
---

As of 2026-10-06 (build 250), the user is pausing for a day or more to gather sprites and test every object type, then report bugs/improvements (usually via issues.odt with screenshots).

Planned next, in the user's words/order:
- House Editor and Vehicle Editor (mini map editors producing one giant object of glued sub-objects).
- PaperDoll Editor (signs, plaques, radio-with-batteries style objects; item 26 in NewTASK.docx).
- Rework the Spellbook object type.
- Then ~89% of effort on the Event Editor: nearly every command needed, and per-editor/per-object-type conditions matching each type's nature.
- Map Editor: floors / multiple levels, placing objects at different floor heights, stacking, and free (non-grid-snapped) placement.
  User's picture of floors (2026-10-07): pick "Floor 1", plot it; click "Floor 2" and you plot on floor 2 while floor 1 shows below, transparent and darkened (out of focus).
  DONE 2026-10-08: build 269 zoom (100% = game full-screen scale), 270 floors 1-9 (5 lifts/storey, tileset layer per floor = solid slabs at lift 5n-1), 271 Free Placement mode (8px, game stacking rules, LiesUnder, per-placement container OwnContents via Ctrl+double-click). User chose: 8px steps, tileset per floor, own-copy contents, objects+books+containers. Awaiting test feedback.

DONE 2026-10-08 build 272: conversations live INSIDE the Show Text command (user: "we are not making a full dialog editor") - replies with conditions, each a WhenChoice branch (RPG Maker style), Label/Jump to Label work; dialog box look comes from the locked DialogFrame sprite group (no sprite = no box; no hardcoded blue). After 272 the user is spending ~a week building a small test game with all object types and floors.

2026-10-08 builds 273-285 (testing fixes): reply typing, branch insertion, main-player speaker, pinned replies + Stop Conversation + Test Conversation window, switch = Boolean variable, each floor stands at its own height (things on floor N never fall below it; editor draws lower floors 20px down-right per floor), tile objects drawn as flats, Hangs on the wall (per placed copy), deleted-object ghosts left out of the game, duplicate InstanceIds renumbered. Next: user reports the milk system (2026-10-09). Build 286 (2026-10-09): Free-mode drop loop tried lifts up to 40, and the floor clamp made any lift at a floor base "rest" -> dragged things jumped to higher floors (likely also the "corrupted project" invisible wall). Now limited to the picked floor; floor status lists things on other floors.

Parked ideas awaiting the user's go-ahead (proposed build 250): liquid "splashing applies its effects" (venom/acid), "gives liquid from its body" (dead snake), event condition/command for "carries a container full of X", editable Avatar milking line. Also parked: ladders taking the party along; Teleporter/Moongate (item 35).

**Why:** user wants Exult/Ultima VII construction-set completeness; testing comes before new features.
**How to apply:** when the user returns, expect test reports first; don't start the parked items unless asked. Related: [[mimic-exult-100]], [[master-object-mechanics]].
