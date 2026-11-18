# Sandcastle RPG Maker - rules for Claude

Read this first, every session, on any PC.

## Where things stand
- The current build is `CurrentBuild` in `Editor/BuildInfo.cs`. Read it and `git log -3` before any feature or fix -
  never work from an older build's assumptions.
- `BUILD-HISTORY.md` has one line per build (newest first); older detail is in `git log`.

## Every change goes to GitHub at once (no exceptions)
ANY change - a feature, a fix big or small, a doc, a note - is pushed to GitHub immediately, as soon as Windows and all the
other platforms have it. GitHub (public, by the user's choice) is the user's only off-site backup.
1. Bump `DEBUG-BUILD-N` in `Editor/BuildInfo.cs` by one (for code changes).
2. Add a line at the top of `BUILD-HISTORY.md`: `- **bN** (date) - what changed, which engines/platforms`.
3. If Claude's memory changed, copy it in: `cp "C:/Users/Fahed Al Daye/.claude/projects/H--SandcastleRpgMaker/memory/"*.md claude-memory/`
   (`claude-memory/` is the GitHub copy of those notes; on a new PC copy it back the other way).
4. `git add -A`, commit `bN - <what changed>`, `git push origin master`, and check the push went through.
   Never let changes pile up unpushed - 50 builds (b560-b610) were nearly lost that way in November 2026.
5. New commands are written up in `CommandList.docx`.

## Standing decisions
- New features (all engines) are event commands only; change an editor only if a command truly can't do it, and say why first.
- Plan from 2026-11-10: ports only (U6, U7 and U8 engines on every platform) until 100%. As of 2026-11-17 Amiga 68k is done,
  the Vampire awaits the user's test, and Mac, Ubuntu and PS2 are left.
- Then Ultima Underworld: a real 3D game editor with the heart of Ultima Underworld (any 3D game - Silent Hill-like,
  Doom/Quake-like, first-person horror) with tools that make an Underworld game easy. Ultima 5 comes last.
- To do (2026-11-17, order not set yet): "Ultima 5 Apple IIgs" and "Ultima 6 Apple IIgs" project modes - own low-level
  65816 player under GS/OS (required), accelerator required (error message if none), the editor enforces the IIgs limits
  (colours, sprites, sizes, map size, sound). Tested in KEGS. See claude-memory/apple-iigs-modes.md.
- Ports must be identical to Windows. Fix differences on the port side; never change Windows to match a port.
- Every change goes into Windows and the C player (`Player/src`) together, and EVERY platform's player is rebuilt -
  Amiga 68k, Vampire, MorphOS, AmigaOS 4, Ubuntu, PS2, PS3 (`Player/build_*.sh` in WSL) and Mac. No platform may be left outdated.
- Mac (Intel + Apple silicon): the user has no Apple silicon Mac - it's Claude's alone, on GitHub. A push touching `Player/`
  runs `.github/workflows/mac-apple-silicon.yml` (build, start, play the test game on Apple silicon, screenshot); when it
  passes it publishes the release `mac-player`. Then `bash tools/fetch_mac_player.sh` puts it in `Editor/Runtimes/Mac`,
  commit, install. Check the run's result before saying the Mac is updated. The user's Intel and PowerPC Macs are reached
  over SSH (`mac instruction.txt`).
- Consoles use the controller; Ultima 6 games need a USB keyboard and mouse on every console. PS2 ISO max 8 GB.
- Mechanics follow Exult / Pentagram / Nuvie sources (U7 / U8 / U6); verify there before inventing.
- Write numbers as plain ASCII (3.5, not a fraction sign).

## Building
- Editor: .NET 8 WPF (`Editor/`, `Engine/`).
- C player: `Player/` - PS2/PS3/Linux toolchains live in WSL Ubuntu on the main PC (`~/ps2dev-tc/ps2dev` for the PS2);
  run the `Player/build_*.sh` scripts through `wsl.exe -d Ubuntu`. Built players go in `Editor/Runtimes/<platform>/`.
- AmigaOS 4: `Player/build_amigaos4.sh` uses walkero's amigagccondocker os4-gcc11 image unpacked in WSL `~/os4img`
  (no Docker; the user allowed running it 2026-11-17). Its compiler wants /opt/ppc-amigaos: the script gives `base_sdk` in a specs file.
- Write helper scripts to files instead of shell heredocs (heredocs mangle backslashes).
