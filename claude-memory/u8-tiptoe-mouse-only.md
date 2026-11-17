---
name: u8-tiptoe-mouse-only
description: "User's decision 2026-11-04 - Ultima 8 tiptoe (step) stays mouse-only; do not add a keyboard action for it"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 6db52882-69e2-4bac-8341-a1d83cc1d49d
  modified: 2026-11-05T05:54:03.248Z
---

Tiptoe (the Step move: right mouse held with the pointer close to the Avatar) stays mouse-exclusive in the U8 engine. Do not add a keyboard action for it.

**Why:** the user decided this on 2026-11-04 when I offered a held Tiptoe key.
**How to apply:** leave it out of U8Keys.Actions and don't offer it again. Related: [[u8-keyboard]].
