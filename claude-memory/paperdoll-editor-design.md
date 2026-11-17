---
name: paperdoll-editor-design
description: "User's agreed design for the PaperDoll Editor (2026-10-15) - canvas + toolbox of part types, sprites from Sprite Editor, per-part events"
metadata:
  node_type: memory
  type: project
  originSessionId: 244c6ae5-df6f-4517-b109-8b366c7ee76d
  modified: 2026-10-15T16:25:58.344Z
---

Requested 2026-10-09, design agreed 2026-10-15 (after build 288 Staircase Designer). Order of the user's three-editor request: Staircase Designer (done, build 288) -> PaperDoll Editor -> House Editor zones (named zones like Pub/Hospital covering a house; character schedules go there, e.g. "go to pub if hungry").

PaperDoll Editor = canvas + toolbox of part types (Picture, Button, Switch, Lid/Door/Hatch, Slot/Socket accepting listed items, Item area, Text, Dial/Slider, Display, Light, Hotspot, Close). Examples (not hardcoded): radio/cassette player (eject opens door, drag cassette into tray, close, play -> cassette spins, audio keeps playing after the paperdoll closes), coffin, sign/plaque, robot body with random flashing LEDs, hardware with missing parts that becomes a different item when parts are fitted.

- NO pixel/paint tools: every part's look, animation frames AND sound come from the Sprite Editor.
- Double-click a part in the editor -> pop-up Event Editor for it.
- Triggers: double-click, combine a world object with a paperdoll part, combine one paperdoll part with another.
- Any object type can open a paperdoll (setting on the master object, see [[master-object-mechanics]]); per placed copy state persists.
- Two passes: 1) editor + toolbox + linking + playing in game, 2) event conditions/commands per part.

Status: build 289 (2026-10-15) = pass 1 done (editor, toolbox, Object Editor "Opens paperdoll", in-game window, slots/areas drag in/out, part event window with By Use / By Item Put In / By Item Taken Out). Build 290 (2026-10-15) = pass 2 done: triggers ByCombineItem / ByCombinePart (DollTriggers, hidden outside part windows), BookEventConditions.CombinedPartId, DollPart.Draggable, Paperdoll event command (DollCommandWindow; actions incl. ToVariable, owned/looping sounds via SoundPlayback.TryPlayFor/StopFor, TurnInto). Awaiting user test. Build 291: Zones done (Map Editor Mode > Zones, MapData.Zones with Purpose; ScheduleEntry.ZoneId/ZonePurpose; CharacterDefinition.ScheduleRules "When..." hungry/injured/variable; Survival.InnZonePurpose). All three requested editors done; the House/Vehicle Editor itself (glued mini-map objects, roadmap) is NOT built yet.

**Why:** user wants imagination to be the only limit; Exult-gump style.
**How to apply:** if a session is cut off mid-build, resume from this design; check git log for the last PaperDoll build.
