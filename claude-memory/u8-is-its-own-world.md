---
name: u8-is-its-own-world
description: "User's rule 2026-11-05 - focus only on U8 now; U8 and U7 have different rules; real water, real falls, lava for every character; nothing new goes into U7"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 6db52882-69e2-4bac-8341-a1d83cc1d49d
  modified: 2026-11-05T18:50:05.675Z
---

Said 2026-11-05: "this is exclusive U8 now... U7 is a different world, different rules and laws. We are focusing now only in u8."

U8's world rules, for ANY character (character is a prefab, never player-only):
- Real falls: falling from a real height hurts hard and can kill. U7's fall is fake.
- Real water: any character falling in drowns and disappears, never to be seen again. Thrown objects splash and are deleted forever at RUNTIME (never touch editor data). Larger objects make larger splashes, with a splash sound and animation.
- Lava tiles burn characters.

**Why:** the user keeps the two games apart; experience, levels and everything else follow each game's own rules.
**How to apply:** new work goes in Editor/U8 and U8-only editor/tab guards. Don't add features to GameTestWindow unless the user asks by name. Don't ask "U7 too?" Related: [[u8-prefab-rule]], [[u8-exclusive-features]].
