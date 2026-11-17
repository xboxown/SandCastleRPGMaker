---
name: morphos-hires
description: b500-503 - MorphOS draws a finer picture (FRAME_HIRES, frame_scale up to 4); 68k stays 320x200 by the user's order; how to test C with no Amiga (WSL gcc + SDL dummy, --shot)
metadata:
  type: project
---

User's order 2026-11-03: MorphOS must be 100% pixel perfect; Amiga 68k stays low-res and speed comes first ("leave Amiga68k none pixel perfect").

Player/src/platform.h: SCREEN_W/H stay the game's 320x200 logic; FRAME_W/H = times frame_scale (a variable with -DFRAME_HIRES on MorphOS, the constant 1 on 68k, so no slowdown). Anything writing pixels goes through frame_fill / frame_dot / FRAME_CLIP or multiplies by frame_scale. platform_amiga.c picks the scale once: full screen = Workbench mode size, window = System Settings WindowScale, cap 4. Fonts: PlayerFontPack writes "name@2/3/4" for MorphOS, Font.density in font.c.

Gotcha: never call a macro HIRES - the Amiga NDK's graphics/view.h defines it (it silently switched the 68k build on).

Test without any Amiga: WSL has gcc and SDL2. Build with platform_sdl2.c (-DFRAME_HIRES optional), run SDL_VIDEODRIVER=dummy PLAYER_SCALE=3 player --notitle --window nosound --shot x.bmp --shot-at N --press "1:U9100" (hooks in main.c). Scratchpad: wsl_build.sh, wsl_shot.sh, pack/ (exports I:/test as a pack; env CTEST adds zones, kite, door). Cross compilers: /gg/bin/ppc-morphos-gcc and ~/amiga/bin/m68k-amigaos-gcc inside WSL.

Not proven on real hardware: nothing of b497-503 has run on MorphOS or an Amiga (platform_amiga.c's new display code is compile-tested only). Related: [[saturday-c-port-list]], [[platform-ports]].

Gotcha: WSL's /tmp is wiped when the wsl.exe call ends, so build and shoot in the SAME call or the shot silently keeps the old .bmp.
