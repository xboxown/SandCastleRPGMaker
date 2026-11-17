---
name: wall-designer
description: b364 Wall Designer - wall sets (window/door in a wall) glued like stairs, one object in Map Editor, split into real pieces in play
metadata:
  type: project
---
b364 (2026-10-19): user couldn't put windows in walls (no way to stack on a wall top in the Map Editor). Built Wall Designer (Editor/WallDesignerWindow.cs, Engine/WallDesignModel.cs, Editor/WallLook.cs): a WallDesign has poses per WallRun (WestEast / NorthSouth = placed copy's Facing South / East); pieces = any ObjectId + OffsetX/Y (8px units) + Lift (0-4). Wall object links via WorldObject.Wall.DesignId. Map Editor draws the composite (anchored on the cell; designer keeps pieces above the red baseline). Game splits sets into real ObjectInstances (WallSetPiece=true: SettleAll/SetZ keep their lift) in the GameTestWindow ctor (GameTestWindow.WallSets.cs); saves hold the pieces. Set's StartsOpen/Locked go to door pieces only.

**Why:** Exult/U7 windows are separate shapes at a lift between wall pieces; editing convenience is the glued set.
**How to apply:** half walls = Wall objects with lower SizeZ. Awaiting user test.

b504 (2026-11-03): the split lives in Engine WallSets.SplitInto; PlayerPack.Export runs it on every map, so C never sees a set (sets were missing on Amiga/MorphOS before; a save made before b504 still holds the unsplit set).
