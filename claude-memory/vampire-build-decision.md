---
name: vampire-build-decision
description: 2026-11-04 - the Vampire build only happens if it gains 10 fps; user reports Firebird Game.log Timing lines on 2026-11-05; 68k stays 320x200
metadata:
  node_type: memory
  type: project
  originSessionId: 70ac2fa8-139c-4715-a70d-e64d078ee493
  modified: 2026-11-05T03:43:52.096Z
---

2026-11-04: the user will cancel the separate Amiga Vampire build altogether unless it is worth at least 10 fps. They will run today's Amiga 68k build (b510) on their Firebird and bring the results on 2026-11-05.

That run measures today's 68k build on the Vampire, not the Vampire build's gain. Work out the gain from the Game.log `Timing:` lines: only the pixel work (world, clouds, windows, present) can get faster with AMMX or direct SAGA. The 25-35 fps (today's build) and 40-55 fps (Vampire build, AMMX doubling) figures I gave are estimates; the doubling is a guess, and the baseline assumes WinUAE JIT off = 68060 at 100 MHz (unverified).

Later 2026-11-04: the user accepts Amiga 68k as the inferior target ("cannot squeeze lemon from a rock"); MorphOS and AmigaOS 4.x are the best Amiga-world targets. Don't push 68k speed or finer-picture work unprompted. At that date Editor/Runtimes held only Amiga68k and MorphOS, so AmigaOS 4 had no runtime yet.

**Why:** the user's rule: the extra build must pay for itself in fps. Pixel perfect on 68k stays dropped ("forget").
**How to apply:** when results arrive, give a plain go or cancel against the 10 fps bar, stating what is measured and what is estimated. If cancelled, take item 9 off [[player-still-to-do]]. Related: [[platform-ports]], [[winuae-test-harness]], [[morphos-hires]].

b611 (2026-11-17): the hand pointer on top of the game was SAGA's HARDWARE pointer (the core's 16x16 sprite: lines at
$DFF800, colours $DFF3A2-6, X $DFF1D0, Y $DFF1D2 - from AROS arch/m68k-amiga/hidd/sagagfx). b612: the game blanks only its picture (no parking - position stays the system's, so nothing to restore)
every frame while its window is active (platform_amiga.c saga_hide_pointer). Waiting for the user's Firebird test.

b614 (2026-11-17): NEVER compile with gcc -m68080 - it emits the 68080's A-line instructions and the user's V4 Firebird
core crashed at once (guru 8000000A). Use -m68060 (+ AMMX only in vampire_ammx.s). Check with objdump for 0xAxxx opcodes.
b615 (2026-11-17): b614 ran but at 8-9 fps (FRAME_HIRES = 640x400 drawn in C). Vampire now has no FRAME_HIRES: 320x200 drawn,
doubled in saga_present (AMMX 565 row + long writes + ammx_copy_row); editor fonts hires=false for Vampire. Awaiting fps result
vs the Amiga 68k build on the same Firebird (the user's 10 fps bar).
