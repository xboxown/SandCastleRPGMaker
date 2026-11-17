---
name: fire-and-pouring
description: "b371 user-approved non-Exult rules - Light Source fires that burn while lit (bucket douses), pouring liquids between containers / from a liquid Food; b509 PutOutLight and Delete Object Self"
metadata:
  node_type: memory
  type: project
  originSessionId: 4c4865e7-5d58-487c-9aef-047dc28694f3
  modified: 2026-11-04T22:03:09.533Z
---

Build 371 (2026-10-19), asked for by the user for their nightmare-universe gate puzzle (5 fires, a bucket, 4 water bottles in a wardrobe):

- LightSettings.BurnsWhenLit / BurnDamageMin/Max / SolidOnlyWhileLit: a lit fire burns whoever stands in it every second (GameTestWindow.ObjectsTick.cs Burn); a bucket of a PutsOutFires liquid douses it (existing UseLiquid light branch).
- Pouring (PourBetween in GameTestWindow.Consumables.cs): full container <-> empty container, or a liquid Food (bottle of water) <-> empty container (the Food is used up). LiquidContainerSettings.UsedUpWhenPouredOut.

Build 509 (2026-11-04): the user insists going out belongs to the light, not the bucket. PutOutLight (U7, U8) / use_put_out_light (C) is the one way a placed light goes out: double-click, bucket, lever, Douse spell all call it, and it runs the light's own By After Put Out (trigger ByEnter). New command Delete Object Self (enum 178, appended) removes the placed copy whose event it is; carried: does nothing. A page with only a Comment takes over the double-click (Comment already existed). Igniting by lever or spell still does NOT run By After Lit.

**Why:** pouring is not in Exult - an addition the user explicitly approved on top of the Ultima way.
**How to apply:** keep these as optional settings so default behaviour stays Exult's. New ways of putting a light out must call PutOutLight, not set IsLitRuntime. Related: [[mimic-exult-100]].
