---
name: mimic-exult-100
description: "User wants Game Test mechanics to mimic Exult 100% (verified from Exult source), including NPC rules while gumps pause the game"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 67092f50-21bd-4743-998e-b6d1e430773e
  modified: 2026-10-16T17:22:22.862Z
---

Default for every game mechanic: mimic Exult exactly, verified against real Exult source (github.com/exult/exult), not guessed. Cite the source function in code comments; when something can't be verified, say so.

Decided rules (2026-10-01):
- Walking: rise/drop max 1 lift (4px) per step; no jumping down ("keep it pure exult").
- Reach: Exult is_grabable port (Editor/GameTestWindow.Reach.cs).
- Gump pause (Editor/GameTestWindow.Pause.cs, IsGamePaused): when building NPCs — NPCs freeze, no double-click attack, party member double-click opens their inventory, **non-party NPCs can still be talked to while paused** (user explicitly: "don't block it, mimic Exult 100%"), using items on NPCs works.

- When a reported bug is really Exult-correct behavior caused by how the map is built, explain it and change nothing. The user (2026-10-16): "thank you for not changing anything we are not breaking exult rule...that breeds disaster". Offer any deviation only as an optional setting, off by default.

**Why:** user's stated goal is an Exult-faithful construction set; deviations caused bugs and confusion before.
**How to apply:** research the Exult source first, report any difference between the user's description and the source, then implement per source unless the user chooses otherwise. Related: [[build-number-bump]].
