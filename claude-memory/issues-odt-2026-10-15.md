---
name: issues-odt-2026-10-15
description: "Progress through the user's issues.odt (2026-10-15) - which items are done in which build, what remains"
metadata:
  node_type: memory
  type: project
  originSessionId: 244c6ae5-df6f-4517-b109-8b366c7ee76d
  modified: 2026-10-15T16:36:37.518Z
---

User asked (2026-10-15) to finish everything in H:\sandcastlerpgmaker\issues.odt before testing builds 288-291 (stairs, paperdoll, zones). Screenshots extract from issues.odt media/ (zip).

Done in build 292: path sweep between cells (GameTestWindow.Sweep.cs), sit/lie from beside seat (no teleport), bedroll = bed you carry (Furniture panel, refuses in pack), sleep slider from 0 = cancel + CancelSleepLine, cover double-click -> bed, Sleep On Bed command, keyring takes clicked key, no walking while dragging, lights not lit in containers/put out, carried liquid names, well By Tool Used On It fires before fill, container contents Quantity button.

Done b293: Settings>Sounds Drop-in-a-window (navigate) + optional global Paperdoll open sound. Done b294: multiple tilesheets per map (TileSheets.Block slots, ExtraSheetNames), currency exchange rates + Exchange Currency command. Done b295: Object Editor folders (ObjectDatabase.Folders, WorldObject.FolderId), Map Editor Objects tab tree + filter + search + animated + Shift-hover sound. Done b296: TrapKind Ground/Shooting/Explosive, ObjectType.Projectile (GameTestWindow.Projectiles.cs). Done b297: weather (GameTestWindow.Weather.cs, MapData.Weather, Edit>Map Weather, Change Weather cmd) + clock day/night (Lighting.cs SkyNow, Settings>Day & Night, LightSettings.Gives). Done b298: per-type trigger lists, By Item Combined To It (GameTestWindow.Combine.cs), By Hitting Target. Done b299: GameConditionModel (33 kinds), history (GameTestWindow.Conditions.cs), Advance IF, Until conditions, Complex Game Condition.

ALL issues.odt items done as of b299 (2026-10-15); user to test builds 288-299.
Deferred by choice: moving Books/Containers into object types + removing Mode > Item (user said only if it doesn't break the project - it would, explain); bucket/well Exult pick-up-then-drop semantics.

**Why:** user wants all issues done before testing.
**How to apply:** continue from the first remaining item; one build per batch, back up + commit each.

Issue2.docx (stairs, 2026-10-15): b300 walk-down fix, b301 landing tiles, b302 landings drawn/hidden with their floor (game + Map Editor) and step pictures forced to real height (4px/lift; "Match pictures to heights"). User's own staircase had 5-unit-deep steps with 20px picture raise per step (fake height) - must press Match pictures to heights. If still blocked, ask for the status-bar "Blocked:" line.
