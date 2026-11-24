---
name: iigs-u6-mechanics-not-u7
description: Ultima 6 Apple IIgs mode must match the Windows U6 engine AND original Ultima 6 / Savage Empire mechanics - never copy Ultima 7-only things (barks) into it
metadata:
  node_type: memory
  type: feedback
  originSessionId: e02e316e-a1c8-42c0-a7f7-69326088b5ec
  modified: 2026-11-24T21:40:26.766Z
---

User, 2026-11-24: "ultima 6 on apple //gs must match ultima 6 on windows mode but should also match the mechanics of
ultima 6/savage empire - there is no such thing as bark, don't copy 100% of ultima 7 there."

**Why:** the Windows U6 engine was copied from the U7 engine and still carries U7-only features (barks...); the IIgs
must be true Ultima 6 / Savage Empire (Nuvie as the reference), not U7.
**How to apply:** before porting any Windows U6 feature to the IIgs, check it exists in Ultima 6 / Savage Empire
(Nuvie source); if it's U7-only, leave it out and tell the user. b676 removed Show Bark from the IIgs; the audit item
"Verbose (barks)" is dropped. Whether Windows U6 should also lose U7-only things: ask the user, don't assume.
Related: [[iigs-audit]], [[apple-iigs-modes]], [[u6-mode-requirements]].

User decisions 2026-11-24 (later the same day) - Ultima 6 mode, all platforms incl. IIgs:
- KEEP / BRING to the IIgs: cinema mode (a cutscene tool, not a U7 thing), temperature zones (Martian Dreams oxygen, cold /
  heat with protective gear, as U7's), door motion animations (Martian Dreams metal doors), shooting and exploding traps.
- REMOVED in U6: hunger, starving, NPC meals (b681; food only heals the party at Rest); Ultima 7 bleeding / blood looks
  (b677), hit flash (already the Hit Spark), status outlines (b681).
- Kite: not important (skip). Party joining exists already everywhere (the user asked; "Joining" in the audit list meant
  Exult's slimes merging - explain, don't change). Hearing = positional sound volume / panning (explained).
