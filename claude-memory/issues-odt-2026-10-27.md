---
name: issues-odt-2026-10-27
description: "Progress through the user's second issues.odt (late Oct 2026, after b447) - done per build, what remains, and the checkmark + history blog it asks for"
metadata:
  node_type: memory
  type: project
  originSessionId: a5ab90be-f68e-4703-813c-ded51d5a193c
  modified: 2026-10-27T14:24:45.021Z
---

H:\SandcastleRpgMaker\issues.odt was rewritten by the user (new list, read after b447). It asks: mark each done task with a green checkmark, and write a history blog at the bottom (bug fixes + added features, all platforms, with build numbers). The user may have it open (~$issues.odt lock file) - edit a copy, or ask before overwriting.

Done (all in Windows + Amiga 68k + MorphOS):
- b448: Amiga bug list A/B (messages to log only, fonts, draw order, reach, camera, in-cell door spots -> Peter leaves the house).
- b449: sound distance (Exult falloff 24 tiles, height 8, balance) - GameTestWindow.Hearing.cs, C sound_play_at.
- b450: Stationary movement, Teleport map/floor/spot, day/night swatch double-click palette, Autosave tab, Cave zones with ambient.
- b451: Sprite Editor Look column, Object Editor preview, house/vehicle list previews, stats in columns (custom green).
- b452: level curve per character (Character Editor > Level tab, graph colour), Earthquake command.
- b453: spells Destroy Field, Common Event, Levitate (3 floors, ceiling-limited), Translate + Languages (System Settings > Languages, font per language, Learn / Forget Language, Show Text + Book language).
- b454: non-human: no paperdoll (red X), can't be given things, DeathDrops (object, qty, percent).
- b455: chairs facing N/W drawn over the sitter (Exult Sit_actor_action offsets + compare) - answers the chair overlay question, no per-pose checkbox.
- b456: Tile Properties "Tile under it" (ground tile drawn under see-through tiles).
- b457: Facing of things in container contents (right-click); C keeps carried FacingRuntime.
- b458: Sprite Editor folders (identity, counts, purple/orange, cut/copy/paste/rename/delete, SpriteReferences keeps uses), SpriteBrowserWindow "Expand...", filter checks removed.
- b459: Format Painter (FormatPainterWindow) in Object/Character/inventory+stats paperdoll/PaperDoll editors.

Remaining (in order): Spawn mode, SI paperdoll avatar, weapon-in-hand opinion/summary (Exult wihh offsets per actor frame), Undo everywhere, CommandList.docx: add Earthquake + Learn/Forget Language, editor CPU/RAM, big .msrp robustness, report Amiga/MorphOS extras (F9 mouse release, mouse lock, timing diary, test hooks), then checkmarks + history blog.

**Why:** the user's standing instruction "also do the tasks in the issue.odt file" after the cross-floor fix.
**How to apply:** continue from the first remaining item, one build per batch, commit each; all platforms together ([[player-still-to-do]], [[mimic-exult-100]]).
