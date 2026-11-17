---
name: worlds-maps-tab
description: "b358-359 multiple worlds - one map = one world (portal to dark world); Maps tab, Change Map World choice, world states in saves"
metadata:
  node_type: memory
  type: project
  originSessionId: 4c4865e7-5d58-487c-9aef-047dc28694f3
  modified: 2026-10-19T05:35:32.410Z
---

User's model (2026-10-18): one map = one WORLD, not a JRPG room. A one-world story needs one map; a portal to a dark world is a second map.

b358: Maps tab in the editor (MainWindow.Maps.cs) - add/rename/resize/make starting/delete; payload.Map = starting world, payload.Worlds = the others; MapData.Id. Toolbar Tile W/H, Map W/H, New/Save/Load Map removed (tile size comes from the tileset).
b359: Change Map has a World field; another world = new GameTestWindow via ReplacedBy (GameTestWindow.Worlds.cs), left world kept in GameSaveData.WorldStates. Commands after a world change don't run.

**Why:** user asked for it in Issue2.docx; they must not lose their project (old projects become world #1).
**How to apply:** never hardcode "the one map"; anything per-map lives on MapData. See [[long-term-ultima-family]].
