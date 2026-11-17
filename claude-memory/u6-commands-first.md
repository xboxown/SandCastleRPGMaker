---
name: u6-commands-first
description: User rule 2026-11-06 - new Ultima 6 features go in as event commands (Ultima 6 tab) unless truly impossible; editor UI only as a last resort
metadata:
  type: feedback
---

From 2026-11-06: anything extra for Ultima 6 mode is added as NEW EVENT EDITOR COMMANDS on the Ultima 6 tab (U6 only), not wired into editors or the engine. Only if something really can't be done with commands is it integrated into an editor. Also: nothing wired one way (an RPG maker) - templates are fine as examples the maker can delete.

**Why:** the user said "anything extra now goes to the command event editor for u6 only new commands... most can be done in event editor itself... unless you really think it is not possible then you integrate into the editor itself" (after asking for portraits that change in play), and earlier "NOTHING SHOULD BE WIRED in one way...as this is an rpg maker" (leveling).
**How to apply:** for a U6 request, first design it as one or more Ultima 6 tab commands (Engine/ScriptCommands.cs Ultima6Tab, EventCommandType, handler in Editor/U6/*.cs); say why when an editor change is needed instead. Related: [[u6-mode-requirements]], [[u6-demo-focus]].
