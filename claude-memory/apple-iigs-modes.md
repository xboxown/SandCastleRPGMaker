---
name: apple-iigs-modes
description: "Ultima 6 Apple IIgs mode STARTED b618 (user pulled it forward 2026-11-18); U5 Apple IIgs later. Own 65816 player (Player/iigs, our asm816.py + sim816.py), GS/OS + accelerator required, editor enforces limits, KEGS test via Game > Apple IIgs Emulator"
metadata:
  node_type: memory
  type: project
  originSessionId: e02e316e-a1c8-42c0-a7f7-69326088b5ec
  modified: 2026-11-18T20:50:29.073Z
---

The user, 2026-11-17: two NEW project modes, "Ultima 5 Apple IIgs" and "Ultima 6 Apple IIgs", same editor with the IIgs's
limits enforced (sprites, colours, sizes, map size, HUD sprite sizes...); own very low-level player; GS/OS REQUIRED;
accelerator REQUIRED (alert inside GS/OS if none); tested in KEGS.

2026-11-18 the user pulled Ultima 6 Apple IIgs mode forward ("while I am busy... do the Ultima 6 apple //gs mode... GO"), with:
- Settings: the emulator's path, the GS/OS boot .hdv/.2mg; the game's test.2mg mounted as the SECOND hard disk; KEGS boots,
  the user double-clicks the game's partition and the program.
- The editor is LOCKED (modal) while the emulator runs, until it is closed.
- The editor checks the emulator supports an accelerator; if not, an error asking for a different emulator.
- No video at all (Video Library / Play Video removed). Audio: ONLY SND (22 kHz or lower, mono). The Sound Mixer is REPLACED
  by Convert Audio in this mode only: pick files, Convert, they fill the Sound Editor as snd1, snd2...; rename there; the
  editor plays them.
- Mimic the Windows Ultima 6 engine within the IIgs's limits and environment.

State (b618): editor side done (MainWindow.AppleIIgs.cs, ConvertAudioWindow.cs, Engine/AppleIIgs.cs: limits, ProDOS/.2mg
writer, SHR palette/packing, GAME.DAT format). Player v1 (Player/iigs/main.s): GS/OS start, StartUpTools, speed-loop
accelerator check (count per VBL frame, 560 = needed; stock ~370) -> AlertWindow, GAME.DAT, palette, 10x10 view, Avatar
walks, Q quits. asm816.py is OUR assembler (building downloaded Merlin32 was denied by the classifier - don't retry).
sim816.py runs the player headless (stubbed toolbox/GS/OS) and writes a PNG - use it before every push. KEGS 1.38 is in
H:\_sandcastle_test_temp\kegs (not run by me: no ROM/GS/OS here). Not yet tested in KEGS by the user.
Next for the IIgs player, following the Windows U6 engine: NPCs + objects on the map, HUD (from the HUD Editor), talk,
combat, sound (Ensoniq DOC from SOUNDS.DAT), events/commands, more maps (Change Map).

**Why:** the user's wish; U6 never shipped on the IIgs.
**How to apply:** ORDER now: ports (Mac/Ubuntu/PS2 left) and this IIgs mode in between as the user asks; then Underworld 3D,
Ultima 5, Ultima 5 Apple IIgs mode. See [[plan-from-nov-10]], [[platform-ports]], [[u6-mode-requirements]].
