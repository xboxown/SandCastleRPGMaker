---
name: commandlist-progress
description: "Progress through the user's CommandList.docx (event commands to build) - what's done in which build, what's next"
metadata:
  node_type: memory
  type: project
  originSessionId: b8505bdb-5df3-4357-bc14-0405a44b76f8
  modified: 2026-10-18T23:26:51.057Z
---

H:\SandcastleRpgMaker\CommandList.docx (2026-10-16) lists existing commands to tweak and new event commands. The user (2026-10-16) calls events + commands "OUR LITERAL PROGRAMMING language" and expects 40-50 commands in the end - more lists will come.

- b315 (part 1): one Show Text button, Play Music removed, Play Sound loop + volume, Stop Sound, Advance IF tabs named.
- b316 (part 2): Give Item, Change Stat (temporary), Reset Stat, Train, Kill Character. Reset Stat confirmed by user: undo only Change Stat additions, keep training/level gains.
- b317 (part 3): every remaining CommandList command. Timed ones wait via Engine IActionCommandHost (context.GameActions); NPC schedule pauses while scripted (Editor/GameTestWindow.Actions.cs, ScriptAction + Chain steps). Instant ones in context.GameHandlers. New Advance IF condition "Last command succeeded". Picker has an "Actions" tab. Not play-tested by me.

- b319-321: map picker rebuilt (Editor/ScheduleDialogs.cs MapCellPickerWindow + MainWindow.MapPicture.cs): editor 100% zoom, floors 1-9, objects clickable. A character's Always pages now auto-run (GameTestWindow.AlwaysPages.cs: at start + on conditions turning true, within Cycle) - the user expected Always to run by itself (Peter's Pickup Item on an Always page never fired before).

- b324: NPCs stand 3.5 s (StopPauseMs) whenever they stop walking, so they can be clicked (not in combat/scripted).
- b325: Cinema Mode / End Cinema Mode pair (Engine ICinemaHost, Editor/GameTestWindow.Cinema.cs): locks keys + world clicks, boxes still work; ends at End Cinema, event end, or 1 s with nothing of the event alive.
- b323: Wait command enabled (seconds / game minutes / game hours, no Who; _scriptWaits list ticked in UpdateScriptActions).
- b333: Walk To Floor Location (new, enum appended at END - enums serialize as ints). User wants Walk To Location kept as "current floor" and floor-specific walking as a separate command. FindPath/FindPlayerPath take goalFloor (A* keyed by X,Y,floor). b334: with goalFloor, A* takes each cell's lift from the step-by-step sweep (CanWalkBetween out lift) - a single CanStandAt jump treats stairs as cliffs. User play-tested b334 OK (walked down stairs in a cinema); Give Item confirmed working too.

How to add a command: enum in Engine/EventModel.cs, window in Editor (FactionCommandWindowBase / ActionCommandWindowBase), entry in CommandWindowRegistry, picker button Tag="EnumName" Click="Registered_Click", game handler registered in InitializeEventCommands / InitializeActionCommands.

2026-10-18: the full "remaining commands" list (13 groups, ~95 commands, each with a test box) is at the end of H:\SandcastleRpgMaker\Issue2.docx: finish 7 greyed-out picker buttons, variables/text, 1D arrays, 2D grids, loops (While/Repeat/For Each/Break/Select Case/Wait Until/Common Event), paperdoll commands (user insists on these), characters, objects, screen/camera/time, music, game (save/load by command), debug, and C# .dll add-on commands (user's idea; GameMaker can't make .NET dlls). Suggested build order = group order. Nothing built yet; the user will test every command.

**Why:** the user is building the full event command set before making a real test game.
**How to apply:** wait for the user's next command list; one build per batch, backup + commit each. Related: [[deferred-merges-and-weapons]], [[mimic-exult-100]].
