---
name: scenario-harness
description: How to runtime-test the user's events without touching the engine - scenario builder makes game bundles from a modified COPY of I:\test\test.msrp and logs with SANDCASTLE_PERF_LOG
metadata:
  type: reference
---

2026-10-20: scratchpad scen/ (Program.cs + run.sh) builds a game bundle per scenario (start position, Peter's spot/floor, start hour, wait, flags killrats/ratsnear/chores), title screen off; run.sh plays it N seconds with SANDCASTLE_PERF_LOG (soak mode: boxes auto-advance, scripted characters logged each second, status text logged). SCEN_ENGINE=<folder> uses a throwaway engine build (git worktree outside the repo, removed after) with one extra log line per event command ("CMD ...").
Flag saveload: after the kills, Save Game to slot 97 then Load Game - saves land in the user's real save folder (%LOCALAPPDATA%/SandcastleRpgMaker/Saves/<projectId>): list it before, delete only the new slot-97 folder after. b385 fixed dead characters coming back on load (PlacedCharacter.Dead/BodyInstanceId/SavedCharacterIndex).
Gotcha: a covered/minimized game window drops to ~9 fps and runs slow - long waits overrun the run time.

Findings from the "after the rats" cutscene (Andrew's page 1) are in [[peter-cutscene-findings]]. Related: [[test-project-location]].
