---
name: commands-only-from-now
description: "From 2026-11-07, every new feature in every engine (U6, U7, U8) is an event command plus a CommandList.docx update; editor UI only if a command truly can't do it"
metadata:
  node_type: memory
  type: feedback
  originSessionId: 6db52882-69e2-4bac-8341-a1d83cc1d49d
  modified: 2026-11-07T21:56:26.841Z
---

From 2026-11-07 (after b559), the user said: "can we go from here and going on with what we have..anything new only updates the command list". The rule: the engine's tools stay as they are, and anything new comes as an event command (in the Add Command picker or on an engine tab), documented in CommandList.docx (a guide or tutorial). Their example: the flashlight (b559) needed only a Light Source command, a Paperdoll action and an engine-side fix; no editor change.

**Why:** they see the engine as complete enough. Paperdolls on objects, slots shown on the object (b558) and events plus commands cover "thousands of different things" (a test-tube rack, the serpent jawbone, a flashlight, a teddy bear with a missing eye). They want new needs met the same way, without the editors growing.

**How to apply:**
- A new request: make it a command first, U6-only ones on the Ultima 6 tab. Change an editor or object-type interface only when a command really can't do it, and say why.
- Bug fixes still go where the bug is.
- Always add or update CommandList.docx.
- Every new command also goes on the Saturday C port list.

Related: [[u6-commands-first]], [[saturday-c-port-list]], [[all-platforms-together]].
