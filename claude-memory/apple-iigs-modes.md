---
name: apple-iigs-modes
description: "TO DO after Underworld 3D (user, 2026-11-17) - two new project modes \"Ultima 5 Apple IIgs\" and \"Ultima 6 Apple IIgs\": own low-level GS/OS player, accelerator REQUIRED, editor enforces hardware limits; test in KEGS"
metadata:
  node_type: memory
  type: project
  originSessionId: e02e316e-a1c8-42c0-a7f7-69326088b5ec
  modified: 2026-11-18T04:28:40.221Z
---

The user, 2026-11-17: add two NEW project modes in the same editor, Ultima 5 Apple IIgs mode and Ultima 6 Apple IIgs mode,
each its own mode (alongside the normal U5 / U6 modes).

Requirements (user's words, as given):
- Same editor, but choosing the mode when making a project switches on the IIgs restrictions: number of sprites, colours
  (Super Hi-Res 320x200, 16 colours per line, 16 palettes), sprite/picture sizes, map size, sound size (Ensoniq DOC 64 KB) -
  "and you name it": every limit the real hardware needs, enforced by the editor.
- Its OWN player, written in a very low-level language (65816 assembly, maybe ORCA/C or Calypsi C for glue) - NOT the shared
  C player in Player/src.
- Runs under GS/OS and REQUIRES GS/OS (use the GS/OS toolbox/libraries as needed).
- ACCELERATOR REQUIRED (TransWarp GS, ZipGS or similar): at start the player checks for one; if none, it shows an error
  message inside GS/OS saying an accelerator is required, and quits. Applies to both modes.
- Test with KEGS (emulator) on the main PC.
- Build output: a hard-disk image (.2mg / .hdv / .po; ProDOS or HFS inside - the user said "HDF ... HF format", meaning HFS).

**Why:** the user's what-if became a wish: U5 shipped on the 8-bit Apple II; U6 never did, so a U6 IIgs mode is new.
**How to apply:** ORDER (user, 2026-11-17): 1. finish all ports, 2. finalize and complete Underworld 3D,
3. Ultima 6 Apple IIgs mode, 4. Ultima 5, 5. Ultima 5 Apple IIgs mode. See [[plan-from-nov-10]]. Related: [[platform-ports]], [[u6-mode-requirements]].
