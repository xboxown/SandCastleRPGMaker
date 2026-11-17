---
name: player-still-to-do
description: "KEYWORD \"still to do\" - the C player (Player/) work left after b422; resume from this list when the user says \"still to do\""
metadata:
  node_type: memory
  type: project
  originSessionId: 4c4865e7-5d58-487c-9aef-047dc28694f3
  modified: 2026-10-22T12:18:58.993Z
---

When the user says **"still to do"**, resume this list for the C player (Player/src, all platforms incl. Amiga 68k). State after b422 (2026-10-21): paperdolls/forms, Title Screen, Save/Load (save + load) done and tested on Windows.

Still to do, in this order unless the user picks:
1. DONE b423: New Game during play and Return To Title (main.c new_game / rebuild).
2. DONE b424: magic.c - spells, Exult spellbook gump, potions, food, useable objects, Learn/Cast Spell (also from a spellbook paperdoll), mages in combat, Time Stop, summon (world_add_actor), Resurrect. DONE b425 (mechanisms.c: keys, lockpicks, keychains, windows, levers, stairs/designed staircases, ladders, sextant, map, clock, instruments) and b426 (plants, sowing, tools, stations, bed/bedroll sleep slider + rest, liquids). Test hook U<instanceId> double-clicks a placed thing.
3. DONE b427: vehicles.c (drive, turn, autopilot, flying). Test hook J<n/e/s/w/x> holds a direction.
4. DONE b428 (+b429 Windows fix: each world keeps its own Event:/Object: fired pages).
5. DONE b430: video.c + videos.sdbx in the pack; Windows test build decodes with Media Foundation; Amiga/Linux/Mac have NO decoder yet (video skipped) - needs the user's decision (e.g. CDXL for Amiga).
6. DONE b431: books.c; Editor/PlayerBookText.cs converts book XAML to paragraphs at Build time. Test hook B<bookId>.
7. DONE b432: buttons, stats paperdoll, Z/I party cycling. Test hook Z.
8. DONE b433-b441 (2026-10-22, overnight port): prompts, game over, Ctrl+Q, combine, display names (b433-434); work schedules (b435); meals/inn/waiter/When-rules/house doors (b436); traps.c: tiles Step On/Stand On/Step Off, ground/explosive/shooting traps, fires burn, projectiles, By Item Dropped On It, status outlines, invisible shimmer, bleeding/puddles (b437); move aside/swap, sea + air characters (b438); sprite mouse pointer + pack exports sprite OriginX/Y (b439); map wrap-around (b440); upper-floor paint order, NPCs breaking doors (b441). Audit of all GameTestWindow partials vs C: no known gaps left. Test hooks: Q now logs hp too; V x;y moves the mouse.
   Runtime-untested in C (no test data): sea creatures (needs Water tiles), fliers/swimmers crossing a wrap seam (refused at the seam).
8b. DONE b445 (2026-10-22, user's Amiga test report): gump pause, held keys let go on pause/cinema (stair block + upstairs cutscene walk fixed), Escape never quits, Save/Load/OpenSaveLoad commands, While/WaitUntil game conditions, item-trigger sub-conditions, full-screen setting, Amiga speed work (see [[winuae-test-harness]]), invisible Intuition pointer + mouse kept in window (F9). Waiting on the user's real-Amiga/MorphOS test of b445.
9. The Amiga Vampire build (SAGA/AMMX, its own sound system) - after the user's speed test. See [[platform-ports]].
10. Video on Amiga/Linux/Mac: no decoder yet - user to choose (e.g. CDXL on Amiga).
Weapon drawn in the hand: not in the Windows engine either ([[deferred-merges-and-weapons]]).
Scratchpad note: tools/SDL2-2.32.10 got deleted once; re-extract tools/sdl2.zip if the Windows build can't find SDL.h.

Also keep CommandList.docx updated for new features ([[commandlist-progress]]).
