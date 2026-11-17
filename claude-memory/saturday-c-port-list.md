---
name: saturday-c-port-list
description: "What the C player (Amiga 68k / MorphOS) still needs, done in the user's Saturday batch - U7 fixes after b471 and the whole U8 engine"
metadata:
  node_type: memory
  type: project
  originSessionId: a5ab90be-f68e-4703-813c-ded51d5a193c
  modified: 2026-11-05T18:15:37.317Z
---

The user batches Amiga/MorphOS (C player, Player/src) catch-up work (said 2026-10-27).

State after b503 (2026-11-03): U7 mechanics through b499 are ported to C. Still different from Windows:
- b486 player walks in 8px steps: DONE in C at b513 (find_player_path in world.c), for the main character's scripted walks; NPCs keep cell-to-cell as Windows.
- b514 fixed the user's MorphOS reports: Idle frames, stacked spawned NPCs, lone starter item in a body, bucket on fire. Target picking in C now falls back to the picture's whole box.
- b515 (Windows, not in C): bursting on death pieces and the weight condition.
- b511 (Windows U7+U8, not in C yet): damage types with character responses, getting back up, Feign/Withstand, Changeling, area and ritual commands, 5 new conditions, Character killed finding a body-left NPC.
- No full audit of C-only behaviour was done; F9 mouse let-go is Amiga-only on purpose (kept).
- U8: the whole Ultima 8 engine has no C port (b472-b479, b488-489 keyboard, see-through slot); the Build dialog offers only Windows for U8.
- 68k speed: nothing new squeezed; not measured since b445. WinUAE harness (make_uae.py) is gone from the scratchpads.

**Why:** the C player must stay a clone of the Windows engine.
**How to apply:** start from this list and git log since the last C commit; port, build (WSL: Player/build_morphos.sh, build_amiga68k.sh; check objdump for fmovecr), commit Editor/Runtimes too. Related: [[u8-prefab-rule]], [[all-platforms-together]], [[morphos-hires]].

Decided 2026-11-04: rat reach stays as it is on all platforms, by the user's order (they think the rat may have been behind a wall). Don't change melee reach or bring it up again unless they ask.
- b521 Crusader-in-U8 (control switching, party limit, climbing party, rolls/kneel, crosshair) - part of the U8 engine port.
- b530 (U7+U8, Windows): inventory close button over an empty slot now clicks (CloseButtonTag); Container Editor optional close button (ContainerData.HasCloseButton, CloseButtonSprite*, X/Y, OwnSize) - port both to C gumps.c.
- b533 (U7+U8): stats paperdoll Ailment effect element (StatsDollElement.Effect: ailment / Hungry / Stat:id above 0) - port to C stats doll.
- b535 (U7+U8): hit/status outlines placed over the drawn frame (Uniform fit in quantized box) and updated after moves - check the C player's outline placement.

- b542 (2026-11-06): inventory paperdoll disk button opens Paperdoll.DiskDollDesignId (else Save/Load setting) - the C player has no inventory disk button at all yet.
- b543 (2026-11-06): DollDesign.Modal (as opened / always / never) - C player should honour it too.
- b558 (2026-11-07, all engines): DollPart.ShowOnObject / ObjectX / ObjectY / ObjectStepX/Y + DollSlotFit.ObjectSprite - a slot's items drawn on its object in the world (Editor/ObjectSlotPictures.cs), each clickable (look names it, double click uses it); port the drawing + hit test to the C player.
- b559 (2026-11-07, all engines): Light Source command (EventCommandType.LightSource), Paperdoll action "OpenOwn", By Burnt Out (ByStay) fired for carried lights too and run with the object's own paperdoll context (FireWithOwnDoll) - port to C.
