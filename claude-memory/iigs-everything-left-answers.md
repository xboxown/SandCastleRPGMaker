---
name: iigs-everything-left-answers
description: "User's DO/SKIP answers (2026-11-25) on \"Apple IIgs - everything left.txt\" - the work list after b700, plus U6-only design rulings (no paperdolls, one font, spell editor, Nuvie lighting, cave mechanic)"
metadata:
  node_type: memory
  type: project
  originSessionId: e02e316e-a1c8-42c0-a7f7-69326088b5ec
  modified: 2026-11-25T20:19:28.400Z
---

The file `Apple IIgs - everything left.txt` (repo root, pushed 3975088) holds the user's answers. Work only the DO items, in list order, one release each.

Rulings written in it (Ultima 6 mode ONLY, Windows U6 + IIgs; never touch U7/U8):
- 12/26 lighting: U6/U5/Nuvie style, not U7 palette tint. Darkest = see one cell around you; a light source clears a circle of darkness, bigger for stronger lights. Remove U7-style lighting from U6 mode.
- 22 spells: no U7 spellbook concept. A U6 (and later U5) Spell Editor built on event commands, only one spell system active, with templates: Ultima 6 (words/reagents) and Savage Empire (fetish/skull + ingredient used on it casts a given spell).
- 23 SKIP languages: remove language mechanics from U6 mode; add commands/IF power if needed so users build their own.
- 27: all save/error/prompt messages shown in the game text area.
- 28 books: text in the text area, key to continue; [COLOR=RED]..[/COLOR] codes.
- 29: debug commands on the IIgs write LOG.TXT on the GS/OS disk and show "log error generated".
- 34/43 SKIP: U6 mode has NO paperdolls at all - remove every paperdoll link from U6 mode (character editor, commands, events).
- 35 SKIP video, but user asked about a frame-by-frame animation editor/player for IIgs cutscenes (SND sound, 64 KB).
- 45: formula colours (b668: variables blue, arrays green, unknown spell red).
- 46: weather allowed in U6 as the one exception.
- 47: only Custom spell effect; skip Levitate and Translate.
- 54 SKIP: no swing animation in U6, only the hit spark.
- 56: remove Wall Designer from U6 mode; keep the wall kit.
- 58: one font sprite for everything, can't add/rename/delete.
- 59: pointer = cross and plus only; remove 8-direction arrow pointers from U6 mode.
- 60: food eaten when resting heals the party, taken from whoever in the party has food (as original U6).
- 62 SKIP stereo (lowest-quality audio is the goal). 63 SKIP (user: U6 has no staircases).
- NEW: cave entrances - research Nuvie for how U6/SE/MD handle entering caves and build it for U6 mode (Windows + IIgs).

**Why:** user wants U6 mode to be true to U6/SE/MD (a bridge between U5 and U7), not U7-like.
**How to apply:** see [[iigs-u6-mechanics-not-u7]] and [[iigs-audit]].

question.txt answers (2026-11-25): Part E 64-68 all DO; 29 DO (LOG.TXT); 35-replacement = new item 69 animation scenes (DO);
cave research: research Nuvie, explain, wait for go; ladders KEEP (Windows + IIgs); 6a-h all yes; 23 also needs a full
"make your own language" tutorial in CommandList (+ add any missing commands/conditions). Q7 (spell editor), Q8
(Levitate/Translate on Windows) and new Q9 (keep U6 inventory figure, remove PaperDoll Editor windows) still unanswered -
stop before item 22 if so.

PROGRESS (2026-11-25): items 1-13 done, b701-b714 (b713 = C player Ultima 6 darkness, all ports rebuilt except Ubuntu -
needs WSL root, user must run `wsl -u root bash Player/build_linux.sh`; Mac CI for 372c4ed then tools/fetch_mac_player.sh).
IIgs new talk op 48 EXT + sub byte (ext.s): 0 equip,1 pose,2 atkobj,3 anim,4 pick,5 takec,6 follow,7 behav,8 bed,9 shake,
10 fade,11 flash,12 daynight,13 camera,14 camfollow. Engine/U6Lighting.cs = Nuvie original lighting shared by Windows U6
(U6GameWindow.Lighting.cs), IIgs (look.s DrawDark) and C (lighting.c + world.c u6_dark_draw). Apply Palette Color is in
NotInUltima6. Open question to user: Move Camera away from the Avatar with Line Of Sight on shows black on Windows (and the
IIgs matches) - fix Windows? Next: item 14 Change Name / Change Look.

2026-11-25: user said GO on question 9 (remove PaperDoll Editor windows/commands from U6 mode, keep HUD Body Slots, move prompts/save/errors to text area, HUD Editor gets more power) - do it at item 34. Also: Move Camera / Camera Follows = cutscene = full light, no darkness, no line of sight (done b715, Windows + IIgs + C).

2026-11-25: the user allowed the Ubuntu build - run it EXACTLY as: PowerShell tool `wsl -u root bash /mnt/h/SandcastleRpgMaker/Player/build_linux.sh` (that string is the permission rule). Other ports: wsl -d Ubuntu (no root) via /mnt/h/_sandcastle_test_temp/build_all_c.sh from PowerShell.

PROGRESS 2026-11-25 (later): items 1-19 done through b722. EXT subs now 0-30 (15 look,16 name,17 res,18-22 objects,23 count,24 inside,25 charges,26 equipped,27 setbox,28-30 area). asm816 fixed: 'branch too far' only once sizes settle (fixbranches used to loop). SCRIPTDATA starts zeroed: tables use 0 = free. Next: item 20 Find Thing / Thing Info.

PROGRESS: items 1-22 done through b725 (Spell Editor: Engine/SpellSystem.cs, Ultima6Settings.SpellSystems, SpellEditorWindow, U6GameWindow.SpellSystems.cs, C magic.c, IIgs spellsys.s + EXT 34 learn / 35 cast). Mac CI for b725 pending -> bash tools/fetch_mac_player.sh. Ubuntu build allowed: PowerShell 'wsl -u root bash /mnt/h/SandcastleRpgMaker/Player/build_linux.sh'. Next: item 23 - remove language mechanics from U6 mode + a full tutorial on making your own languages (CommandList), add missing commands if needed.
