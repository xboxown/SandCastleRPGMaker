---
name: winuae-test-harness
description: "How to run the Amiga 68k player in the user's WinUAE/AmiKit myself and read its timing - config, boot drive trick, safety rules"
metadata:
  node_type: memory
  type: reference
  originSessionId: a5ab90be-f68e-4703-813c-ded51d5a193c
  modified: 2026-10-22T20:56:53.232Z
---

User's emulator (permission given 2026-10-22): `D:\Program Files (x86)\AmiKit\WinUAE\winuae_32bit.exe`, config `Configurations\A500HardDrive-Backup.uae` (KS 3.2.1, 68060+FPU, uaegfx Z3 RTG, DH0 = `H:\Amiga\MyAmiga500BackedHardDrive.img`, 300 GB - can't be copied). User closes the emulator before I use it.

My setup (scratchpad make_uae.py, rebuild it if gone): copy of the config with use_gui=no, cachesize=0 (JIT off: about half the user's real speed), DH1 = Perf: (Game + Game_Data), DH2 = Boot: (bootpri 5) whose S/Startup-Sequence assigns SYS:/C:/S:/L:/LIBS:/DEVS:/FONTS:/LOCALE: to DH0 by full path (DH0:C/Assign), then `Run >NIL: Execute Boot:S/Later` and `Execute DH0:S/Startup-Sequence` (Run/Execute are resident in KS 3.2 - "DH0:C/Run" fails). Later: Wait 45 SECS, Stack 250000, CD Perf:, `Game --notitle --shot-at 60 --shot Perf:shot.bmp` (the game quits itself). Start with `-f test.uae`.

Safety: DH0 read-only hangs the boot (invisible write-protect requester) - keep rw; only kill WinUAE once the game has quit and the Amiga is idle. Screenshot only the WinUAE window (PrintWindow), never the whole desktop.

b445 numbers (emulated, JIT off, with sound): 5.6 fps -> 14-15 fps. Game.log "Timing:" lines: input/sound ~8 ms (Paula + MP3 task), world ~14, clouds ~11, windows (text box) ~14, present 18 (320x200 full screen) / 22 (640x400 window, 32-bit). Related: [[platform-ports]], [[player-still-to-do]].
