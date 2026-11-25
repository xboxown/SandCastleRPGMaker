---
name: iigs-audit
description: "2026-11-19 audit of the Windows U6 engine (Editor/U6, 111 files) vs the Apple IIgs player - what is still missing, in work order"
metadata:
  node_type: memory
  type: project
  originSessionId: e02e316e-a1c8-42c0-a7f7-69326088b5ec
  modified: 2026-11-20T02:27:49.270Z
---

Goal (user, 2026-11-19): the Ultima 6 Apple IIgs mode must match the Windows U6 engine 100% (not original U6), then only
testing; when 100% done, IMMEDIATELY read issues.odt (H:\SandcastleRpgMaker\issues.odt, updated 2026-11-19) and do it.

Done on the IIgs through b661: maps/floors/stairs, houses, vehicles, combat (turn-based, ranged, charges), ailments, food,
sight, spells (all effects), wands/scrolls/potions, formulas/texts/arrays/grids, leveling (U6 system), rest, talk/shops/
haggle, own events, music, weather, crafting/work, HUD dials/date/custom buttons, save/load, title, mouse.

PAUSED 2026-11-19 after b662 (user: if usage limits are near, pause the IIgs and do issues.odt first; skip IIgs testing).
Done since: b659 crafting/work, b660 wands/potions/charges, b661 HUD dials/date/custom buttons, b662 knocked out + bodies.
Eggs (item 2) half done and PARKED: editor side saved in H:\_sandcastle_test_temp\eggs_AppleIIgs.cs / eggs_MainWindow.AppleIIgs.cs
(and the script iigs_b663_cs.py); design: Eggs list (18 words each), EggChars absent copies, codes trigger 18; player eggs.s
not written yet.
EGGS DONE in b664 (eggs.s; GAME.DAT 42, SAVE 7; harness IIGS_EGGS / IIGS_MISSILE in iigspack).
RESUMED 2026-11-24 (user: "spend the rest of your energy 100% on apple //gs until 100% completed"). ITEM 3 DONE b673 (moves.s; GAME.DAT 43): wrap (WrapXY in MapTile/OccAt, AbsX/AbsY/ShortX/ShortY, CellOnScreen, PickCell), MoveAside (AsideForAvatar/AsideForChar/AvatarAside), sea/air (char flags $1000/$2000, GroundFor, OccMask), weight by name table (WeightCheck in Get/ToPack/GiveToMP/Into). Harness IIGS_MOVES. Sim tips: keys start "!~" (disks in, title Enter); (3,5) on map 0 is a Change Map event; simwatch.py/simprof.py in _sandcastle_test_temp. ITEM 4 DONE b674 (solo.s; GAME.DAT 44): SoloSwap swaps AvRec<->slot (not +58/+60) + PX/PY + owners $FFFF<->slot code; AvRec+4 = avatar sprite; mode word 20 (+56), NextCommander after each turn; Option+digit = view only. ITEM 5 PART 1 DONE b675 (floor.s; GAME.DAT 45): traps/lights by NAME tables, tile events via ObjEvQueue, BurnMinute in ClockRun. Data segment 54 KB of 64 - watch it. b676 no barks; b677 U6 death blood (all platforms); b678 U7 commands hidden in U6 (ScriptCommands.NotInUltima6); b679 levers (lever.s, GAME.DAT 47; object numbers through newPlace!). SKIPPED as U7-only (user agreed): shooting/exploding traps, door motion, temperature, NPC meals, blood/hitflash looks, hearing, kite, joining, cinema. b680 sleep/sit looks (+4/+5, flags $4000, Sit = act $80) + combat modes by NAME (ModeTab; +56 is S_KEY!). b681 hunger/meals/outlines gone in U6. b682 temp.s, b683 door.s. IIgs CODE SEGMENT 63 KB / DATA 57 KB of 64: split code into a 2nd segment (JSL/RTL) next. b684 2nd code segment (.code2), b685 wall-kit faces. User 2026-11-24: IIgs ~80% done; ALSO WANTS joining (slimes split/join, Martian Dreams + U6) and hearing (sound by distance) on the IIgs - Windows/C have both. NEXT: shooting/exploding traps, cinema mode, joining, hearing on the IIgs; then magic items (U6 rings/amulets worn effects), party combat leftovers, scene pictures, talk style. Ubuntu root build pending for b677.
Missing or partial on the IIgs (from the file summaries), rough order to do:
1. (done b662) Bodies + Unconscious
2. Spawns / eggs (monster, music, sound, weather, usecode, missile eggs) - Spawns.cs
3. Weight (carry limit), Wrap (map wrap-around), MoveAside, Movement (sea / air characters)
4. U6CombatModes (member modes), U6Solo (1-9 solo mode)
5. ObjectsTick (lights burn down; tiles / traps step events), Projectiles (traps), Mechanisms (levers linked, windows,
   shutters, glass), Doors (motion animations)
6. Beds / bedrolls (sleep hours), Furniture (sit), Meals (NPC hunger, inns), Temperature (cold / hot zones)
7. Toughness (damage types, get up, feign death), MagicItems (worn bonuses, immunities), Weapons in hand (drawing)
8. AlwaysPages (character Always pages), Actions (character action chains), Pets, Combine (item onto object event),
   Consumables liquids (bucket), food "who eats", Travel (keychain, sextant, map, clock), Money (exchange currency),
   Factions commands
9. U6Scene (scene pictures), U6Create (type text, pick picture...), U6TalkStyle, Cinema mode, Verbose (barks),
   Overlay (map view), Kite, Joining (slimes), Blood, HitFlash / StatusOutline, Hearing, Fonts, Pointer sprite, WallSets
Hardware-left-out (Check notes): paperdolls (Dolls, DollControls, DollLinks, PaperdollButtons, ScriptDolls, DiskProgress,
save screen), Video, Scenarios (Game Test only). Engine-internal, no IIgs equivalent needed: Perf, Physics, Reach, Sweep,
Walls order, UpperFloorOrder, Pause.

How to apply: work down the list one build each (event commands where possible), sim816-test, release.py; keep this note
and apple-iigs-modes.md updated; when the list is empty -> issues.odt. See [[apple-iigs-modes]].
