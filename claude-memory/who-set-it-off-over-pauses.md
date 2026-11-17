---
name: who-set-it-off-over-pauses
description: b510 - Whoever set it off must survive every pause in an event (KeepingWhoSetItOff); C player now has characters stepping on map events; test events with their real commands
metadata:
  node_type: memory
  type: project
  originSessionId: 70ac2fa8-139c-4715-a70d-e64d078ee493
  modified: 2026-11-05T00:13:33.856Z
---

Build 510 (2026-11-04): Peter walked into the gate and the player died, because timed commands (Play Animation, Wait, Walk To...) resumed with the activator cleared, and a cleared activator means the main character. BookEventPreviewExecutionContext.TryBegin now wraps its continuation with OnKeepWhoSetItOff (KeepingWhoSetItOff in both Windows engines). Reply branches and Stop Conversation use it too, so they keep _firingOwner as well; those two paths compile but were not run.

C player: map events were player-only. events.c actors_stepping now fires By Enter/Exit/Stay for characters, and "Stepped on by" checks the real stepper. It polls cells each frame, treating a jump of more than 2 cells as a Teleport (no event).

**Why:** my b508 gate test replaced the user's 4 animations with a bare Change HP, so it passed while the real event failed.
**How to apply:** any new way for an event to pause must go through KeepingWhoSetItOff. When testing the user's event, keep its commands as they wrote them. Don't call the C player "fine" from reading one part: drive it. Related: [[scenario-harness]], [[peter-cutscene-findings]], [[all-platforms-together]].
