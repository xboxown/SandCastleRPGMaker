---
name: platform-ports
description: "2026-10-20 platform build requirements - Build dialog targets (Windows, Linux, Mac PPC/x86/ARM, MorphOS, AmigaOS 4, Amiga 68k RTG/Vampire), user's answers for Amiga, test machines, icons"
metadata:
  node_type: memory
  type: project
  originSessionId: a5ab90be-f68e-4703-813c-ded51d5a193c
  modified: 2026-10-22T19:35:31.243Z
---

User's decisions (2026-10-20):
- Build menu gets a platform choice (Windows - keep working - Linux, Mac PPC/x86/new chips, MorphOS, AmigaOS 4.x, Amiga 68k). REMOVE the platform choice from New Project.
- Amiga 68k: RTG or Vampire only - no OCS/ECS/AGA, hard drive only (no floppy). Min 68060, recommended 68080, FPU REQUIRED (build -m68060 hard float; player refuses without FPU). Kickstart 3.1 (V40) minimum - libraries/devices opened at version 40, older Kickstart gets a requester. OS: AmigaOS 3.x with RTG card AND ApolloOS (Vampire) - NOT AROS (never support AROS). Graphics: RTG (cybergraphics/Picasso96). Sound: AHI and also Paula. RAM: min 128 MB, may need up to 1 GB; the game checks free RAM and tells the player if it needs more. Runs both in a Workbench window and full screen (behaves like a window).
- MorphOS and Amiga builds: executable named "Game" with a 64x64 "Game.info" icon that looks like the splash screen.
- Testers: user has a Vampire (Firebird) Amiga, MorphOS machine, Mac OS X PPC, Ubuntu laptop; AmigaOS 4 via Discord testers.
- Plan agreed: Linux + modern Mac first, then MorphOS/PPC/Amiga (a C game player); PS2 later.
Progress: b392 Build platform picker (BuildPlatformWindow.cs), New Project asks no platform. Approach chosen: one portable C player (Player/ in repo: platform.h + platform_sdl2.c, world.c, pictures.c; cJSON + stb_image) for every non-Windows platform; Windows keeps .NET. Engine/PlayerPack.cs exports Game_Data (game.json with houses/vehicles built, sprites.json, pictures/). M1 done (commit 050164b): world drawn + Exult walking + camera; tested on Windows with w64devkit + SDL2 in scratchpad tools (--shot/--keys switches). b393: Build > Linux/Mac (preview) exports Game_Data. Next: M2 events/dialogs/doors/containers/books + wall draw order (Exult compare); M3 NPCs/schedules/combat/sound; then Amiga 68k layer; cross-compilers need WSL distro (none installed) or user's machines.
**How to apply:** I can't run these machines - every port needs the user's test reports. Related: [[candy-request-2026-10-20]], [[long-term-ultima-family]].

Amiga gotcha: libnix strtod/scanf use FMOVECR (Line-F #8000000B on 040/060 without 68060.library). Build uses -Dstrtod=sc_strtod (src/numbers.c); check objdump for fmovecr after each build. Game.log diary beside Game shows start-up steps.

2026-10-20: first Amiga 68k run WORKED in AmiKit (OS 3.2, 68060 emulation) after user installed 68060.library (MMULib from Aminet). amiga_start.c checks FPU before any FPU code.

2026-10-20 USER PERMISSION: for Amiga speed, bang the hardware hard - may marry it to the hardware (Vampire SAGA direct chunky, AMMX, Paula, etc.), crashes on other hardware acceptable. Do after the user reports the Vampire speed test.

2026-10-20 DECISION (supersedes the hardware-banging note above): TWO Amiga builds. (1) Amiga 68k RTG build unchanged: OS3.x/ApolloOS, KS3.1+, 060/080+FPU, P96/CGX, AHI+Paula, 128MB, testable in WinUAE/AmiKit - NO hardware banging. (2) Separate "Amiga Vampire" build: bangs SAGA directly, AMMX, sound to hardware, refuses to run on non-Vampire. MorphOS + AmigaOS 4: plain SDL2 like PC.

b394 (2026-10-20): player has events.c (interpreter, triggers, conditions), dialog.c (DialogBoxBuilder port), Walk To with A*. Not yet: formulas/loops/Common Events, items/inventory, NPC schedules, sound, clock (Wait minutes), save/load. Test hooks: --press "t:E|1-9|T<charId>|M<x>;<y>|W<switch>" --shot f.bmp --shot-at t. Pack exporter: scratchpad packexport (reads I:	est).

2026-10-21 USER: "everything not yet, do them too" - player backlog to finish after schedules: weather (rain/clouds + gloom), lights carried in hand, clock display, Map-Editor container OwnContents, full SettleWorld, sounds/music (SDL + AHI/Paula), Advance IF / game conditions, remaining world commands (Play Pose, Sit/Stand, Open Door, Picklock, Sleep On Bed, Join Party, followers, Change Schedule, Teleport done...), furniture (chairs/beds), combat, vitals/hunger, save/load, title screen, paperdolls, Vampire build.

2026-10-21 USER RULE: player Game_Data must be encrypted like Windows (sdbx), never plain JSON/pictures. Done b406: game.sdbx, sprites.sdbx, pictures.sdbx + sounds.sdbx (SRPK archives, per-file containers). Player: src/pack.c (SHA-256 key from file id + tiny-AES-c AES256). Progress b398-406: schedules, furniture, use, sound (SDL+AHI/Paula), weather, lights burning, settle, conditions, screen cmds.

b409-410 (2026-10-21): player vitals.c (HP/food/ailments/knock-out/death bodies) and combat.c (factions, Combat_schedule, player combat mode C, weapons/armor/ammo/shots, theft, EXP/levels, combat music). Extra placements of one character = own copies (runtime id = id + 1000000*n, world_character_source). Test hooks added: K = combat mode, A<charId> = attack. Still to do: Attack Object (object HP/breaking), spells, Input/Select commands, ChangeMap/worlds, PlayVideo, ShowError, save/load + title + game over, paperdoll/container/book UI, meals/waiter/work, zones, Vampire build.
b411-413 (2026-10-21): Attack Object/explosives (combat.c), small commands b412 (Change/Reset Stat, Train, Exchange Currency, grids, Show Error via errors.c), b413 gumps.c = inventory windows + mouse pipeline (paperdoll, backpack list, container windows, drag/drop). Test hooks: I, D/V/R x;y (press/move/release), X (Escape). Next: PaperDoll Editor forms (Dolls.cs, DollControls, ScriptDolls, OpenPaperdoll command), paperdoll buttons, books reading, save/load+title, spells, vehicles, ChangeMap, PlayVideo.
b414 (2026-10-21): user reported Amiga freeze (log stopped at "Window open. Running.", reboot needed). Fixed: sound task priority 10 -> 0 with Delay(1) when behind; sounds streamed (pack_archive_stream, decrypt per block) instead of whole-file decrypt; own table AES-256 replaced tiny-AES. Untested on Amiga yet; MP3 decode speed on a real 060 still unknown.

2026-10-21 USER DECISION (loves it): Amiga 68k RTG build plays sound through PAULA (audio.device, 8-bit, the crackly white-noise sound is wanted) by DEFAULT - never AHI unless an "ahi" file/argument. Vampire build uses its own new sound system. b414-418 fixed the AmiKit freeze: AHI path froze the system (exact cause unproven; sound task stack 16->128 KB, priority 0, streamed decryption, table AES also done). Game.log notes each start-up step; nosound/paula/ahi files or arguments beside Game.
b419-422 (2026-10-21): dolls.c (PaperDoll Editor paperdolls, forms, Open Paperdoll, error box, Paperdoll + part script commands, typing via EV_TEXT), Title Screen (fills view, music, New Game/Continue/Quit), save.c + Save/Load screen (S; Saves/ beside the game: info.json, game.sav AES-encrypted with the game key - C now encrypts too, Windows tools read it - shot.png), loading rebuilds every module from the save JSON (Runtime object holds clock/vitals/script/etc). Test hooks: --notitle, F (S key), Lname (typing). Still to do: Return To Title / New Game in play, spells, vehicles, ChangeMap/worlds, PlayVideo, books reading, paperdoll buttons (combat/heart/disk/halo), stats paperdoll, Vampire build.

b443 (2026-10-22): Build > Amiga 68k is ENABLED and makes a full drawer named after the game: Game (shipped prebuilt in Editor/Runtimes/Amiga68k/Game, copied to the editor output by Editor.csproj; build_amiga68k.sh refreshes it - commit it after player changes), Game.info (SplashScene.IconPixels(64) + AmigaIcon.DiskObject), Game_Data. Other platforms still write Game_Data only. Editor Build not yet tried by the user on a real Amiga.
b444 (2026-10-22, committed 754ec8e just before a power outage): MorphOS build - the Amiga platform layer (platform_amiga.c/_audio.c) compiled for MorphOS (no 68k/FPU checks, no chip mem, AHI 44.1 kHz, sound process as PPC code). build_morphos.sh uses MorphOS SDK 3.20 cross GCC 11.5 in /gg, refreshes Editor/Runtimes/MorphOS/Game; Build > MorphOS enabled, makes the drawer like Amiga 68k. Not yet tested by the user on their MorphOS machine. Not plain SDL2 as first planned.
