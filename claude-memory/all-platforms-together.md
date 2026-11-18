---
name: all-platforms-together
description: "EVERY change goes into Windows AND every platform's player (Amiga 68k, Vampire, MorphOS, Ubuntu, Mac, PS2, PS3) - all rebuilt, none left outdated - then pushed to GitHub"
metadata:
  node_type: memory
  type: feedback
  originSessionId: a5ab90be-f68e-4703-813c-ded51d5a193c
  modified: 2026-11-18T00:30:46.756Z
---

From 2026-10-22 (b446), widened 2026-11-17: any update or change to game behaviour is made in the Windows engine AND the C
player (Player/src, shared by every non-Windows platform), then EVERY platform's player is rebuilt into Editor/Runtimes:
- Amiga 68k: build_amiga68k.sh - MorphOS: build_morphos.sh - Vampire: build_vampire.sh (all in WSL)
- Ubuntu: build_linux.sh - PS2: build_ps2.sh - PS3: build_ps3.sh (WSL Ubuntu)
- Mac: build_mac.sh runs only ON A MAC (the user's) -> GameMac.zip into Editor/Runtimes/Mac; when Claude can't build it,
  say so plainly so the Mac player is never silently outdated.
Then the build is installed and everything pushed to GitHub (see [[build-number-bump]]).

**Why:** the user, 2026-11-17: "PS3 is fine for now... any new changes I do in the editor PS3 need to be updated like the
rest... all have to be updated... so there will be no platform outdated.. all get updated with github in every new change".
Earlier (b446): "we are porting it, not making a new engine from scratch, so it should be a clone of windows in all mechanics
and behavior".

**How to apply:** a change isn't done until every platform's player has it. Find the mechanic's C counterpart, port the same
rule, rebuild all players, report any platform that couldn't be rebuilt. Runtime-test the C side with the Windows test build
(build_windows_test.sh, --press hooks) and in WinUAE ([[winuae-test-harness]]). Ports to finish: Mac and Ubuntu after the
Vampire test ([[plan-from-nov-10]]). Related: [[platform-ports]], [[mimic-exult-100]].
