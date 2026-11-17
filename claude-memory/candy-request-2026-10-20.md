---
name: candy-request-2026-10-20
description: Request.docx "candy" list (2026-10-20) - toolbar icons, splash, House Editor, Vehicle Editor, multi-platform Build, PS2 later; progress per item
metadata:
  type: project
---

Order from Request.docx (repo root):
1. Toolbar icons after Zoom with tooltips - DONE b386 (Editor/ToolIcons.cs vector icons, MainWindow.Toolbar.cs).
2. Splash screen - DONE b387 (SplashWindow.cs): user wants a STILL anime-style cartoon, colourful, not animated; own STA thread; Splash.png beside the exe overrides.
3. House Editor - DONE b388 (user chose: linked, all floors, own copy per plot, no turning). Engine/HouseModel.cs (HouseTemplate in ManifestPayload.Houses, MapData.Houses PlacedHouse, HouseBuilder.BuildInto called in GameTestWindow ctor after CloneMapData); MainWindow.Houses.cs (Houses tab last, edit = _map switched to house.Layout, Mode > Houses plot with ghost, right-click edit/remove). Editing happens in the main editor (not a separate window). Verified at runtime with the user's house as a template.
4. Vehicle Editor - user chose: Exult quarter turns, ALL four controls (driver seat, helm/lever, dashboard paperdoll, event commands), autopilot with walking aboard, deed optional per vehicle. Stages: 1 DONE b389 (Engine/VehicleModel.cs; HouseTemplate.Vehicle; VehicleState in MapData.VehicleStates; ObjectInstance.VehicleStateId; WorldObject.VehicleRole/TurnLooks/MovingLook; GameTestWindow.Vehicles.cs gather/step/turn; Vehicles tab + VehicleSettingsWindow; ObjectEditorWindow.Vehicle.cs; vehicles stamp objects only, built facing South). Verified with a test cart (probe worktree + SANDCASTLE_TEST_DRIVE keys). Stage 2 DONE b390 (GameTestWindow.VehicleTravel.cs: DriveVehicle/StopDriving/TravelVehicle commands, ScriptFieldKind.Vehicle, autopilot one turn per leg, Air takeoff to VehicleFlyLift=50 and okay_to_land; gravity skips vehicle pieces; camera follows drawn (lifted) position). Stage 3 DONE b391 (VehicleSettings.DashboardDollId opened from Controls / on sitting; SetVehicleSpeed + VehicleState.Speed). CommandList.docx guides 13-16 added (b47628d) via scratchpad patch_guides3.py.
5. Build dialog with platform choices: Windows (keep working), Linux, Mac PPC/x86/ARM, MorphOS; user has MorphOS + Mac PPC to test. Not started.
6. PS2 later.
Related: [[roadmap-after-build250]], [[commandlist-progress]].
