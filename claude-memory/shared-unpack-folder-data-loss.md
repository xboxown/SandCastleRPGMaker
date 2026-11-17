---
name: shared-unpack-folder-data-loss
description: "2026-10-27 the user's sprite pictures were lost - test runs of built games/editors on the user's project wiped the editor's unpack folder; rules for testing on the user's project"
metadata:
  node_type: memory
  type: feedback
  originSessionId: a5ab90be-f68e-4703-813c-ded51d5a193c
  modified: 2026-10-27T20:11:00.861Z
---

2026-10-27: 427 of 670 frame pictures vanished from I:\test\shapes.sdbx (saved 09:37). Cause: before b463 every load emptied %TEMP%\SandcastleRpgMaker\<projectId>\sprites (and \audio), shared by every process opening that project; my wingame test bundles (built Windows games from a copy of I:\test, same project id) wiped the folder while the user's editor had the project open, and the user's next save silently dropped every missing file. b463 fixed it (per-process session_<pid>_<start> folders; save takes missing bytes from the source .sdbx; warning). Repaired file built from scratchpad wingame/run/win1/WinGame_Data/shapes.sdbx (Oct 26): scratchpad repaired2/shapes.sdbx, 329 sprites, 0 missing - installed in I:	est on 2026-10-27 13:23 at the user's request (verified: 0 missing); the damaged one is kept as I:	est\shapes.sdbx.damaged.

**Why:** a test run on a copy of the user's project is not isolated if it shares the project id - it can destroy the user's live work.
**How to apply:** never run an editor or built game on a copy of the user's project while it may be open, unless the build has b463+ unpack isolation; give test copies their own project id when possible; never overwrite files in I:\test without asking. Related: [[test-project-location]], [[scenario-harness]].
