---
name: all-platforms-together
description: "From b446 every engine change goes into the Windows engine AND the C player (Amiga 68k + MorphOS) together; the C player is a port/clone of Windows, never a new engine"
metadata:
  node_type: memory
  type: feedback
  originSessionId: a5ab90be-f68e-4703-813c-ded51d5a193c
  modified: 2026-10-22T21:41:26.797Z
---

From 2026-10-22 (b446): any update or change to game behaviour is made in the Windows engine (Editor/GameTestWindow*.cs) AND the C player (Player/src: Amiga 68k and MorphOS share it), then both Amiga binaries rebuilt (build_amiga68k.sh, build_morphos.sh refresh Editor/Runtimes) and committed.

**Why:** the user said "from here and going on... any update or changes we do in the windows edition, morphos and Amiga68k need to be updated as well", and earlier "we are porting it, not making a new engine from scratch, so it should be a clone of windows in all mechanics and behavior" (e.g. Escape quitting in C while Windows only closes the top window was a bug).

**How to apply:** when changing a mechanic, find its C counterpart and port the same rule; runtime-test the C side with the Windows test build's --press hooks (P join party, N place character, M teleport, U use, J hold direction, Q log) and in WinUAE ([[winuae-test-harness]]). Amiga speed optimisation is finished (user happy with b445). Related: [[player-still-to-do]], [[platform-ports]], [[mimic-exult-100]].
