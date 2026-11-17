---
name: master-object-mechanics
description: "User thinks of Character/Container/Object as \"master\" types; every new chest or NPC is a prefab copy, so mechanics go on the master and each map placement is its own instance"
metadata:
  node_type: memory
  type: user
  originSessionId: 9366c65b-5b69-4741-b437-296f25901c06
  modified: 2026-10-04T20:45:46.942Z
---

The user explains the engine in prefab terms: Character, Container (and Object) are master objects holding all mechanics; every chest, door-like container or NPC they author is just an instance/prefab of that master. "Do this to the Character Editor" means: it must apply to every character made from it, now and in future.

Each placement on the map is its own runtime instance with its own state (health, inventory, lock, ailments, fights). Build 232 fixed a bug where two placements of one character shared one record (killing one "killed" both).

**Why:** they want any mechanic (locks, HP, factions, combat) to work for every container/character they create, not per-asset special cases.
**How to apply:** put new behavior on the master type's data model + runtime (ContainerData, CharacterDefinition, WorldObject), never keyed to a specific named asset; keep per-instance state on the placed instance, never on the shared definition. Related: [[mimic-exult-100]].
