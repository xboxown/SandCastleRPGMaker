---
name: paperdoll-forms-request
description: "2026-10-18 request list after Save/Load worked (b339) - prompt paperdolls, form controls, hard-coded sounds, lockpick group, object folders, ambient sounds"
metadata:
  node_type: memory
  type: project
  originSessionId: ccab2481-db64-4e63-88c6-12485a653bda
  modified: 2026-10-18T18:22:00.637Z
---

User request 2026-10-18 (after b339, save/load confirmed working). Items:
1. PaperDoll Editor: Confirm (Yes) + Cancel (No) parts, hard-coded for saving. Overwriting a slot opens an author-made confirm paperdoll ("Are you sure?" Text + Yes/No). Yes saves into the picked slot; No closes that paperdoll, back to the one behind.
2. A "saved" message paperdoll option (Text + an OK = Cancel button relabelled).
3. Load button: show its Pressed look first, then load.
4. Buttons that can be a sprite OR text, with a click event condition.
5. Treat the editor like a Visual Studio form designer: button, label, textbox, dropdown, checkbox, radio, list... controls.
6. Sounds hard-coded from System Settings: every click on a button/textbox/slot = drop sound; every paperdoll open = paperdoll open sound; none set = silent; not changeable per paperdoll.
7. Events can open another paperdoll from the editor's list, modal or not; several open at once.
8. Sprite Editor: the Lockpick group is a built-in group like Tiles/Fonts - colour it red, no delete/rename.
9. Object Editor folders: a folder holding any object turns purple and can't be deleted until empty.
10. Sound settings: per-map ambient loops for dawn/morning/night (birds, crickets). One map = the whole world (not JRPG).

Status: b340 = items 3, 8, 9. b341 = items 1, 2, 4-7, 10 (Editor/DollControls.cs, GameTestWindow.DollControls.cs, OpenPaperdoll command, SaveLoadSettings.ConfirmDollDesignId/SavedDollDesignId, MapData.Ambient + GameTestWindow.Ambient.cs). All built, none play-tested by the user yet.

**Why:** making the PaperDoll Editor "the best and most powerful tool", like VS forms.
**How to apply:** track which items are done in builds after b339; related [[title-save-video-plan]], [[paperdoll-editor-design]].
