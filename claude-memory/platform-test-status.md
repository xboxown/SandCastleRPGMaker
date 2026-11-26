---
name: platform-test-status
description: "User's real-hardware test results per platform - U6 confirmed on AmigaOS 4 (2026-11-26); PS2 ethernet adapter now in hand; Mac series left"
metadata:
  node_type: memory
  type: project
  originSessionId: e02e316e-a1c8-42c0-a7f7-69326088b5ec
  modified: 2026-11-24T21:21:11.775Z
---

User, 2026-11-24: AmigaOS 4.x and Ubuntu tested - "work perfect" and up to date with Windows. Amiga 68k, Vampire and PS3
also completed. Ultima 7 is perfect on all platforms. Left: PS2 (the user needs an ethernet adapter first) and the Mac
series. The user is now testing Ultima 8 and Ultima 6 on Ubuntu and AmigaOS 4.x - expect bug reports from those.

**Why:** tells which ports are trusted and which still need the user's hardware.
**How to apply:** don't re-verify the confirmed ports; treat U6/U8 reports from Ubuntu/AmigaOS 4 as the next port work.
Meanwhile keep working on the Apple IIgs audit ([[iigs-audit]]). See [[platform-ports]].

2026-11-24 evening: the user confirmed b6xx on AmigaOS 4.x and Ubuntu "perfect"; U6 and U8 on both get tested tomorrow
(2026-11-25) - keep them marked done in the per-platform table. MorphOS, PS2 (Ethernet adapter), Mac Intel/PPC: no U6/U8
report yet; Mac Apple silicon passes GitHub CI.

2026-11-26: the user confirmed Ultima 6 works on AmigaOS 4. The user now HAS the PS2 ethernet adapter - PS2 testing
can start (the user drives it; PS2 port fixes come from their reports).
