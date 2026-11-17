---
name: plain-ascii-numbers
description: "Write plain numbers (3.5, not 3½) in replies - the user's terminal shows fraction signs like ½ as \"%\""
metadata:
  node_type: memory
  type: feedback
  originSessionId: b8505bdb-5df3-4357-bc14-0405a44b76f8
  modified: 2026-10-17T17:52:08.000Z
---

Use plain ASCII numbers in replies: "3.5 seconds", never "3½" or other Unicode fraction signs.

**Why:** 2026-10-17 the user's terminal rendered "3½" as "3%" and they asked what "3% second" meant.
**How to apply:** decimals or "x and a half" in words; avoid ½ ¼ ¾ and similar symbols in user-visible text.
