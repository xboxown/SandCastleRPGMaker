---
name: deferred-merges-and-weapons
description: Container/Book editors merge into Object types only after the editor is 100% complete (new project); Weapon/Armor types built b308, weapon-in-hand drawing still open
metadata:
  type: project
---

2026-10-15: user confirmed the Container Editor and Book Editor must NOT be merged into object types yet - merge only after the editor is 100% complete and they start a new project (merging now would break their project).

Build 308 (2026-10-15): Weapon and Armor object types (Exult Weapon_info/Armor_info): own or custom slot, damage/type/powers, melee/thrown/ranged, ammo family / charges (magic staff) / thrown itself, Projectile object that flies (fireball), strike style + poses, armor points + immunities. Not done yet: drawing the weapon in the character's hand (Exult wpninf weapon offsets), NPCs readying the best weapon, auto-equip on pickup. Next big focus: the user's Event Editor command list document.

**Why:** user is about to write the event command list and then build a full game to test performance.
**How to apply:** don't merge Container/Book editors until told; offer weapon-in-hand drawing when weapons get tested. Related: [[issues-odt-2026-10-15]], [[long-term-ultima-family]].
