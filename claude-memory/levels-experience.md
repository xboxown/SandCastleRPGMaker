---
name: levels-experience
description: "b369 experience/levels design - killer gets victim's EXP stat, RPG Maker-style level curve in System Settings > Levels, per-character Level Lock"
metadata:
  node_type: memory
  type: project
  originSessionId: 4c4865e7-5d58-487c-9aef-047dc28694f3
  modified: 2026-10-20T01:04:38.799Z
---

Build 369 (2026-10-19): the user's design for experience and levels.

- Whoever lands the killing blow (LandBlow in GameTestWindow.Weapons.cs) gets the dead character's own EXP stat - any character, hostile or not; monsters killing each other gain too (all characters are one prefab).
- Level curve: SystemSettings.Levels (Engine/LevelModel.cs) - MaxLevel (1-99) plus per-level Experience needed and TrainingPoints given. Editor: Editor/LevelCurveEditor.cs, bars draggable with the mouse + a table to type into, Generate from a formula, "Exult's own" defaults (max 8, level 2 at 100, doubling, 3 points/level - from Exult's actors.cc set_property(exp)).
- CharacterDefinition.LevelLock (0 = game's max), in Character Editor under Stats.
- GameTestWindow.Levels.cs: AwardKill / GainExperience / CheckLevel; Change Stat on EXP also levels up.

**Why:** the user wants RPG Maker-style curves but Ultima rules; AI characters don't go to trainers yet - commands for that may come later.
**How to apply:** spells/explosions/traps don't award experience yet (only weapon blows and shots); extend AwardKill if asked. Related: [[mimic-exult-100]], [[master-object-mechanics]].
