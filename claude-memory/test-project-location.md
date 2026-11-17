---
name: test-project-location
description: "The user's test game project is I:\\test (test.msrp, project name \"test2\"); permission given 2026-10-16 to decrypt and inspect it read-only"
metadata:
  node_type: memory
  type: reference
  originSessionId: a45836a2-5a83-4011-a1e5-c213d53e4f6f
  modified: 2026-10-16T17:22:20.853Z
---

The user's test project lives at `I:\test` (test.msrp, objects.sdbx, shapes.sdbx, audios.sdbx). Permission to read it was given on 2026-10-16.

To inspect it, decrypt a copy into the scratchpad and never write back. The format (Engine/ProjectContainer.cs) is: "SRPG" magic, int version, 16-byte project GUID, int IV length + IV, int cipher length + cipher. It is AES-256-CBC with key = SHA256("SandcastleRpgMaker.ProjectKey.v1:" + guid.ToString()). PowerShell's .NET Aes is enough. The decrypted .msrp is JSON: Map.Objects, Map.UpperFloorLayers[].TileIndices (row-major, -1 = empty), Objects.Objects (definitions).

**How to apply:** when a game-test bug might come from map data, check the real map before changing engine code. On 2026-10-16 the "floor-2 candles visible and grabbable" report turned out to be candles with no floor-2 tiles under them, which is Exult-correct. Related: [[mimic-exult-100]].
