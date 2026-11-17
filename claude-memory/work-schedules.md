---
name: work-schedules
description: "b373-375 real work schedules (Bake/Sew/Blacksmith/Lab/Farm/Miner), per-copy Belongs To, sowing - user wants \"everything real, not fake\""
metadata:
  node_type: memory
  type: project
  originSessionId: 4c4865e7-5d58-487c-9aef-047dc28694f3
  modified: 2026-10-20T04:39:46.593Z
---

2026-10-19, builds 373-375 (user: "I want everything real..not fake" - a real life simulation engine):
- b373: ObjectInstance/PlacedItemInstance/InventoryItemInstance.BelongsToId - per placed copy (Map Editor right-click > Belongs to), carried with the item; theft uses it.
- b374: Editor/GameTestWindow.Work.cs - ScheduleType Farm=6, Miner=8, Blacksmith=13, Bake=18, Sew=19, Lab=21 (Exult numbers). Station workers pick a recipe, fetch ingredients from own/nobody's chests and tables in the work area (zone, else WorkAreaTiles round spot), fetch + ready the tool (hands full -> bag -> nearest table -> feet), work, deliver to ScheduleEntry.OutputX/Y; tidy up (tools back) when the schedule changes. Farm/Miner harvest ripe plants and sow (PlantSettings.SeedObjectId/SoilObjectId). Lab sits and reads between brews.
- b375: in-between products (inputs of another recipe in the area) stay in the workshop; later-step recipes first.
- Guide 9 in CommandList.docx documents all of it (scratchpad make_guides.py + patch_guides.py regenerate from backup/CommandList_2026-10-19_before-guides.docx).

**Why:** user is testing all of it the next day (2026-10-20); expect bug reports from real play.
**How to apply:** none of b371-375 was played in-game by me; when reports come, read Work.cs first. Waiter still serves from its menu, not stock. Related: [[mimic-exult-100]], [[fire-and-pouring]], [[master-object-mechanics]].
