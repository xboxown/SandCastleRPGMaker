---
name: u8-keyboard
description: "b488-489: NOTHING hardcoded in U8 keys; Ultima 8 rebindable keys - System Settings > Ultima 8 > The keyboard, the picker's Ultima 8 tab (first U8-only commands), kept for good in keys.json beside the saves"
metadata:
  node_type: memory
  type: project
  originSessionId: 028bf686-2e51-45e1-be27-b25d2b064444
  modified: 2026-11-03T09:23:35.243Z
---

Requested 2026-11-01; the first attempt was lost when the user's window closed (nothing reached the disk), rebuilt as b488 on 2026-11-03.

- Engine/U8Keys.cs: actions (Up, Down, Left, Right, Run, Jump, Combat, Inventory, Stats, SaveLoad, Menu), Bindable whitelist (the party's 1-9, Escape, Alt, F6/F9/F10 can't be taken), swap logic, player file %LOCALAPPDATA%/SandcastleRpgMaker/Saves/<projectId>/keys.json holding only what differs from the maker's keys.
- Editor/U8/U8GameWindow.U8Keys.cs: Ask For Key, Set Key, Get Key Name, Reset Keys; %keyjump etc. on paperdolls; the Menu key (F1) opens Ultima8Settings.KeysDollDesignId. Held direction keys stay the arrows internally (WalkArrowFor translates).
- U8-only commands go in ScriptCommands with Tab = ScriptCommands.Ultima8Tab; the picker hides that tab in other styles. CommandList.docx: Tab "Ultima 8", Guide 23, test list.

Verified at runtime: ask, Escape, swap, fail, digit refused, Set Key, Get Key Name, reset, restart keeps keys, W walks while the arrow doesn't, U7 bundle still plays. NOT driven by me: paperdoll buttons with the real mouse, the System Settings tab, a rebound Run key (posted keys don't show in Keyboard.IsKeyDown).

Test trick: PostMessage clicks don't reach paperdoll parts (the pipeline reads the real button state); run the commands from the map's start event (Id 1) and post only keys. The u8game bundle builder and winkeys.ps1 live in the session scratchpads (copy from the newest one that has them).

b489 (user's rule, 2026-11-03: "for all KEYBOARD in Ultima 8 nothing is hardcoded"): every key the U8 engine reads is now a U8Keys action (Quit, Cancel, numbers + second keys, Accept, screen choices, pages, Erase, whole stack, Game Test keys), with groups World/Screens/Everywhere (keys clash only inside a group; Everywhere clashes with both), "Ctrl+K" style held keys, U8KeyCapture for reading a key. Shared NumberSliderControl keeps fixed keys, so SliderKeyFor translates. J stays the Jump default by the user's choice. Tested by me: rebinding Cancel/number/Accept, Alt alone, box closes on new key only, Set Key With, reset. NOT driven: real Ctrl/Shift/Alt held (posted keys; SetKeyboardState trick didn't take), so Ctrl+Q, Ctrl+K and rebound Run are the user's to check; Game Test keys need the editor's Game Test (bundle = shipped).

**How to apply:** never read a Key.X directly in Editor/U8; add an action and use KeyIs/TypingKeyIs/KeyHeld/KeyWords. new U8 keyboard actions: add to U8Keys.Actions and read with KeyIs in U8GameWindow_PreviewKeyDown. C port is on [[saturday-c-port-list]]. Related: [[u8-prefab-rule]], [[commandlist-progress]].
