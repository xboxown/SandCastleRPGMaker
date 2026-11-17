---
name: current-build-b610
description: "LATEST BUILD is b610 (checkpoint commit 8c90808, 2026-11-17); what b560-b610 contain; always check BuildInfo.cs before working"
metadata:
  node_type: memory
  type: project
  originSessionId: e02e316e-a1c8-42c0-a7f7-69326088b5ec
  modified: 2026-11-17T23:26:23.683Z
---

As of 2026-11-17 the latest build is **DEBUG-BUILD-610** (Editor/BuildInfo.cs), committed as checkpoint **8c90808**.
The conversation that made b560-b610 (Nov 10-17) is NOT saved on this PC (transcript 6db52882 stops at Nov 7), so
everything known about it comes from the code. Always read Editor/BuildInfo.cs + `git log -3` first; never work from an
older build's assumptions. Next build is b611.

What b560-b610 added (from the code):
- Build dialog platforms: Windows, PlayStation 2, PlayStation 3, Ubuntu 24.04+ (.deb), Mac modern (preview), MorphOS,
  Amiga 68k RTG, Amiga Vampire. Not ready yet: Mac PowerPC and AmigaOS 4. U6 and U8 games build for every player platform.
- PS2: Editor/MainWindow.Ps2Build.cs + Engine/Ps2Iso.cs give an ISO with the ESR patch (Runtimes/PS2/GAME.ELF,
  esr_dvd_video.bin), max 8 GB, PS2 controller; U6 needs a USB keyboard and mouse. Sound is IMA ADPCM via audsrv (b576-578),
  libpad controller (b568/b577). Built in WSL Ubuntu with ~/ps2dev-tc/ps2dev and Player/build_ps2.sh. The r5900 GCC
  struct-with-double crash is worked around in script.h (a union under PLATFORM_PS2).
- PS3: MainWindow.Ps3Build.cs + Engine/Ps3Package.cs make a .pkg (HEN/CFW), PSL1GHT, Runtimes/PS3/EBOOT.elf, CRT effect on SPEs
  (Player/src/ps3spu), build_ps3.sh / setup_ps3_toolchain.sh / ps3_patch_sdl2.sh.
- Ubuntu: MainWindow.LinuxBuild.cs + Engine/LinuxDeb.cs; build_linux.sh, setup_linux_build.sh, test_linux_deb.sh.
- Vampire: build_vampire.sh, vampire_ammx.s, Runtimes/AmigaVampire/Game (see [[vampire-build-decision]]).
- C player now has the U6 engine (u6.c: HUD Editor frame) and the U8 engine (u8.c: keys/actions), plus crt.c.
- Windows: CRT effect (CrtLook.cs) and Fill the screen (FillScreenView.cs) in System Settings > Game Window;
  U6 debug notes (Engine/U6DebugNotes.cs, System Settings > Debugging); BookEditorWindow.U6.cs, ContainerEditorWindow.U6.cs.
- Uncommitted user files left alone: Log.txt (latest bug table), Fixitforme.txt, Request.docx (old request).
- WSL Ubuntu on this PC is online and holds the PS2 toolchain. The user's separate Hyper-V Ubuntu VM had no internet;
  they gave up on it on 2026-11-17 and are moving to a laptop.

**Why:** the user's top priority: never act on an old build on top of a new one.
**How to apply:** start from b610 code; follow [[plan-from-nov-10]] (ports to 100%, [[all-platforms-together]]) and
[[commands-only-from-now]]; bump the build number per [[build-number-bump]].
