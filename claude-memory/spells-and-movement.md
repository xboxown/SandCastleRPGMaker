---
name: spells-and-movement
description: "b376-379 spellbook rework (Exult gump, paperdoll option, who can read), spell kinds, mages in combat, Land/Sea/Air characters"
metadata:
  node_type: memory
  type: project
  originSessionId: 4c4865e7-5d58-487c-9aef-047dc28694f3
  modified: 2026-10-20T05:35:15.253Z
---

2026-10-19, builds 376-379 (user chose: Exult-style book + optional own paperdoll design; "both" for who can read):
- b376 Magic.cs: Exult Spellbook_gump (pages per circle, 8 spells/page, icons, casts left, click = bookmark, double-click = cast, corners turn). SpellbookSettings.AllowedCharacterIds/OpenBookSprite/BookmarkSprite; CharacterDefinition.CanCastMagic (default true). A spellbook with an Object Editor paperdoll opens that instead; Cast Spell in its part events uses the book being read (_spellbookReader).
- b377 GameTestWindow.Spells.cs: SpellDefinition.MissileObjectId/RadiusTiles/Affects/Animation; MagicEffectKind appended (Teleport, Mark, Recall, Summon, CreateItem, CreateField, MagicUnlock/Lock, Douse/IgniteLights, Reveal/DestroyTraps, Telekinesis, Charm, Fear, Dance, Resurrect, Clone, RevealInvisible, TimeStop, Tremor, Locate); MagicEffect.ThingId/Seconds. Spell kills AwardKill.
- b378: TryCombatSpell - casters heal below half HP or cast harmful/summon spells (1/3 of turns).
- b379 GameTestWindow.Movement.cs: CharacterDefinition.Movement (Land/Sea/Air), TileTypeProperties.IsWater. Fliers: free lift bands per cell, A* over (cell, band), cruise lift = floor 10 (lift 45), dive to foe's floor in combat, fall when incapacitated; projectiles interpolate lift to the aimed target. Movement applies to NPCs only (the player walks).

**Why:** user wants a "real life simulation"; untested in play when written.
**How to apply:** expect test reports; fliers' falls land on whatever is below (roof), not strictly floor 1. Related: [[work-schedules]], [[mimic-exult-100]], [[levels-experience]].
