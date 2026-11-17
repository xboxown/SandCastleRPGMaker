---
name: peter-cutscene-findings
description: 2026-10-20 after-rats cutscene findings; b384 fixed: event holds (characters stay till their event ends), paths through the player, step along a narrow stair
metadata:
  type: project
---

Investigated first (user: "don't change code"), then the user said yes to the fixes - done in b384 (commit fb22c4e):
- ScriptRunState.Parent/Root, EventCommandInterpreter.CurrentRun + IEventRunHost.RunEnded; GameTestWindow _heldByEvent (UpdateNpc skips schedule, dormancy exempt, ScheduleNpcs defers the hour, Change Behavior lets go).
- FindPath plans with _planningThroughPlayer; MovePlayerAside: aside, then along/back a narrow stair (PlacePlayer), then swap if the walker fits.
Original findings:
1. A character an event just walked somewhere resumes its schedule when the walk ends; off-screen, the dormant rule (Perf.cs, WalkingToSchedule -> Teleport to npc.Dest) sends it straight back to its schedule spot while the cutscene is still running.
2. Show Text whose speaker isn't on screen is skipped ("isn't in sight - their line isn't shown").
3. Path planning treats the player as a wall: the player on the one staircase -> Peter "can't find a way" after ~8 s.
Content issues in I:\test: player Walk To Location without floor; floor-2 holes (13,7) (15,8) (16,8); well at (24,8) 6 lifts tall pokes through the hallway.
Content advice given: Change Behavior Peter -> Stand right after his walk; Walk To Floor Location for the player. Related: [[scenario-harness]], [[work-schedules]].
