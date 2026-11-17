---
name: map-wrap-around
description: b382-383 - MapData.WrapsAround (user says "warp" = same map wraps, not another map): everyone crosses the seam; missiles end off-screen after crossing
metadata:
  type: project
---

2026-10-20, build 382. User asked for "warp / non-warp" maps; this means wrap-around edges (Ultima V/VI style).
- MapData.WrapsAround; Maps tab right-click "Wraps around the edges" (checkable), Add Map dialog checkbox, row shows ", wraps".
- GameTestWindow.Wrap.cs: WrapPixel (player crosses, camera shifted the same), SplitAtSeam/TryStepAcrossSeam in TryStepActor, ShowAcrossTheSeam = 3 Rectangles with VisualBrush(GameCanvas) (the brush includes CameraTransform, so viewbox = screen rect + map shift). Streaming, ground chunks, IsOnScreen, IsInSight, dormancy consider wrap shifts.

**Why:** the user wants the choice per map; untested in play when written.
- b383: Heuristic/CellsApart/WrappedDelta/NearestImage/WrapCell helpers; FindPath/FindPlayerPath/FindFlyPath step over the seam (raw stepTo for the walk checks, wrapped cell for the node); MoveAlongPath wraps; followers, InReach, TilesBetween, CanSee + line of sight, ChaseCheck, FacingTowards, Work, Spells radius, Blast, aiming all use the short way. A projectile that crosses gets Crossed=true and ends once off the screen (user's rule).

**How to apply:** limits the user accepted: things seen across the seam can't be clicked until the player crosses; Reach.cs (use reach) doesn't wrap; bodies straddling the seam don't block things on the other side; a map smaller than the 320x200 view wraps movement but doesn't draw copies. Related: [[worlds-maps-tab]].
