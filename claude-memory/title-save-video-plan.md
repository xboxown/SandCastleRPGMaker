---
name: title-save-video-plan
description: "Agreed 2026-10-17: Play Video command, Save/Load paperdoll, Title Screen editor, Return To Title command - design + build order"
metadata:
  node_type: memory
  type: project
  originSessionId: ccab2481-db64-4e63-88c6-12485a653bda
  modified: 2026-10-18T04:44:01.296Z
---

Agreed 2026-10-17 (after b334). Build order: 1) Play Video command, 2) Save/Load, 3) Title Screen editor, 4) Return To Title command. One build each, user tests between.

- Video: H.264 .mp4, AV1, H.265 (HEVC with an editor warning that players may need Microsoft's ~$1 HEVC extension). Use Windows' own decoder (WPF MediaElement) - never bundle FFmpeg/VLC (licensing falls on the user then).
- Save/Load: built from PaperDoll Editor parts (new part kinds: save list, save/load/delete buttons, name box, screenshot...). Opens with the paperdoll sound, pauses the game. Unlimited saves, scrolling. Author places, sizes and styles every part.
- Title Screen: a full editor. Built-in New Game / Continue / Load / Quit plus any number of custom buttons/objects; what a button does is programmed in the Event Editor (user: "Powerful this event editor").
- The project already has a Build Game command that makes a shippable standalone game (the user corrected me - don't say it's missing).

Status: b335 = step 1 done (Tools > Video Library, Engine/VideoModel.cs VideoProbe + VideoStore videos.sdbx streamed AES, ManifestPayload.Videos, GameBundle bundles it optionally, Play Video command in Editor/GameTestWindow.Video.cs). Not play-tested by the user yet.
b336 = step 2 done: Save/Load paperdoll (DollPartKind SaveList..DeleteSaveButton appended; SystemSettings.SaveLoad.DollDesignId; S key; Editor/GameTestWindow.SaveLoad.cs; Engine/GameSaveModel.cs GameSaveStore in %LocalAppData%/SandcastleRpgMaker/Saves/<projectId>/). Load = new GameTestWindow from the save + ApplySave, old closes, ReplacedBy (MainWindow loops ShowDialog / RunStandalone). Not saved: mid-script state, temporary Change Stat, blood, doll-part fired counts. b337: user wanted Exult's fixed slots - the SaveName kind is now shown as "Save slot" (DollPart.SaveSlot number, DollPart.SlotNumber; SaveSlotInfo.Slot); each slot holds its own save, Save list still available. Next: Title Screen editor (title Load should reuse GameSaveStore + LoadGame path).

**Why:** turning the construction set into something that ships a real game (alpha release on the user's website).
**How to apply:** resume from here after a cut-off; check git log for the last of these builds. Related: [[paperdoll-editor-design]], [[commandlist-progress]].
