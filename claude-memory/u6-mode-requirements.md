---
name: u6-mode-requirements
description: "The user's Ultima 6 mode spec (issues.odt 2026-11-05 + chat) - HUD Editor replaces PaperDoll Editor, keyword talk, turn combat on key, Amiga 68k pixel perfect 22 fps + IFF audio; U6 ONLY"
metadata:
  node_type: memory
  type: project
  originSessionId: 6db52882-69e2-4bac-8341-a1d83cc1d49d
  modified: 2026-11-06T01:26:05.090Z
---

Started 2026-11-05. U6 mode covers Ultima 6, Savage Empire, Martian Dreams (Nuvie = the reference, as Exult/Pentagram). EVERYTHING here is Ultima 6 projects only - never U7 or U8.

Spec (issues.odt + chat):
- PaperDoll Editor replaced by a HUD Editor. Frame art imported from Sprite Editor. Pieces placed with position and size: game view, text area (messages + typed input), body slots, carried-items area, the panel where the inventory shows AND, in the same place, portrait + stats.
- Command buttons (Attack, Talk, Look, Get, Drop, Move, Use, Rest...) are pieces, each with its own sprite (animated or not), add/remove freely; a compass piece; sun/moon bar, date/wind, combat mode are optional pieces.
- Custom button piece: one click runs its event; trigger fixed "Always" (top), cycle fixed "Only Once" (bottom), not changeable; Only Once resets when the event finishes, so it can be clicked again. Own sprite.
- One HUD per project; an Ultima 6-only event command changes the HUD frame.
- Party: one member shown at a time, picked with 1-9; party limit 9, shown in settings but locked.
- Keyboard like U6: verb key then Tab between map and inventory panel, arrows over slots, move items slot/container/party member (trade).
- Conversation: NPC portrait + text, all typed keywords (no clicking); keywords in the shown text coloured (colour settable). Optional conversation music; on leaving, map music restarts or a random one (Nuvie: conversations_stop_music, musicStop then musicPlay picks from the current group).
- Character Editor's inventory editor hidden in U6. "Disable Z" = the Z KEY (stats window) does nothing in U6 games (user confirmed 2026-11-05; heights stay).
- Combat: live (real-time) normally; the combat key enters turn-by-turn mode; every action reported in the text area. U6 itself: B = Begin/Break off combat, C = Cast; modes Command/Front/Rear/Flank/Berserk/Retreat/Assault; F1-F8 show a member, F10 party, 1-8 solo, 0 party, * portrait/inventory toggle.
- Amiga 68k: U6 must be pixel perfect and at least 22 fps; choice of Amiga IFF (8SVX) audio files, U6 only.

Build order planned: b524 style + engine copy (done, 5b373a9); b525 HUD Editor (Editor/HudEditorWindow.cs, model Engine/Ultima6Model.cs in SystemSettings.Ultima6.Hud), System Settings > Ultima 6, Character Editor tab = Starting Inventory (done, 6ef92fc; screenshots via scratchpad shots6); b526 HUD in play (46a867c); b527 U6 keys + verbs + panel + loose carrying (43d9e6e; test t527_make.py/t527_run.ps1 posts keys, logs u6say:); b528 typed keyword talk (4ae0b6e); b529 turn combat on B + blows told + Change HUD Frame (bd8feda). Steps 1-6 done 2026-11-05.
b536-538 (2026-11-06): b536 Map Editor draws in the game's order (MainWindow.DrawOrder.cs; U7/U6 view). b537 U6 mouse (click = Look any distance, double = multiuse from 1 square, chests searched/spilled, drag only adjacent), U6 levels (U6Leveling: 8 levels, 30 hp/level, shrine Meditate At Shrine or dream on Rest; Ultima 7 leveling locked behind typing USE ULTIMA 7 LEVELING), Rest. b538 Cast (book in panel, syllables), solo swap (U6GameWindow.U6Solo.cs, ported from U8 SwapControlTo), combat modes + Command members' turns, U6 title menu replaces paperdoll title/Save-Load, CommandList Guide 34. All the old not-done list is DONE. Tested in-process with scratchpad shots14/shots15 (reflection on U6GameWindow). Remaining loose ends: the U7 'Light on Andrew.' style spell notes still reach the text area; custom button click and compass art never run by me; talk is any distance (U6) though the user said 'one square' for interacting.

User's answers 2026-11-05 (after b525):
- Keys: Ultima 6's keys by default, changeable in System Settings, plus U6-only event commands to change keys in the game (like U8's Ask For Key / Set Key).
- Members: exactly as Ultima 6 (F1-F9 show a member, 1-9 solo, 0 party, F10 party, * portrait/inventory) - NOT 1-9 to show.
- Messages, errors, prompts: in the HUD's text area, as U6 ("You see wall", "Chicken attacks rats"); the words replaceable in a settings tab of verbs/words.
- Order: (1) HUD in play (2) inventory panel + Tab + Move/trade (3) keyword talk (4) turn combat + text reports (5) Z key off (6) custom buttons/compass/sun-moon/Change HUD Frame. Amiga/MorphOS only once U6 is 100%; then perfect ALL ports; then Ultima 5 and Underworld last.

**Why:** the user wants U6 "complete" next, estimated ~2 weeks.
**How to apply:** new U6 work in Editor/U6 and ProjectSession.IsUltima6 guards only. Related: [[u8-prefab-rule]], [[long-term-ultima-family]], [[u8-is-its-own-world]].

b539-541 (2026-11-06): b539 Text area notes (rules rewrite/remove any engine note). b540 no floors/roofs in U6, line-of-sight black (U6Sight.cs), bags in panel / books in text area / bodies searched, scenes (U6Scene.cs: Begin Scene ... End Scene; HUD Editor > Introduction / Create New Character scenes). b541 user's rule: NOTHING WIRED - Tools > Leveling Editor (LevelingEditorWindow.cs): leveling systems (table + events of commands; triggers earned / after rest / after rest each / called), one in use, templates U6 shrines + SE/MD dreams (deletable); Rise A Level, Run Leveling Event. Talk stays U6 (any distance on screen) - user confirmed.
Still not done: CommandList guide for b539-542 (Guide 35); Amiga U6 player 0%.

2026-11-26 (user, b742): Ultima 6 fights are TURN BASED as Nuvie - whenever a fight is on (combat mode B, or anyone
fighting/hostile/fleeing within 8 of the avatar), the world waits for the avatar's turn; everyone moves by Nuvie's movement
points (Dexterity vs the avatar's: same dex = one step/blow a turn; a fleeing "Aiiieee" NPC steps once and waits). No fight:
the world is live. Windows U6Combat.cs FightOn/StartRound, C u6.c fight_on, IIgs turns.s.
