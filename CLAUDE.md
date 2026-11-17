# Sandcastle RPG Maker - rules for Claude

Read this first, every session, on any PC.

## Where things stand
- The current build is `CurrentBuild` in `Editor/BuildInfo.cs`. Read it and `git log -3` before any feature or fix -
  never work from an older build's assumptions.
- `BUILD-HISTORY.md` has one line per build (newest first); older detail is in `git log`.

## Every build (no exceptions)
1. Bump `DEBUG-BUILD-N` in `Editor/BuildInfo.cs` by one.
2. Add a line at the top of `BUILD-HISTORY.md`: `- **bN** (date) - what changed, which engines/platforms`.
3. Commit with the message `bN - <what changed>` and push to GitHub (`git push origin master`).
   Never let builds pile up uncommitted - 50 builds (b560-b610) were nearly lost that way in November 2026.
4. New commands are written up in `CommandList.docx`.

## Standing decisions
- New features (all engines) are event commands only; change an editor only if a command truly can't do it, and say why first.
- Plan from 2026-11-10: ports only (PS2, PS3, Ubuntu, Mac, MorphOS, Amiga 68k, Vampire - U6, U7 and U8 engines) until 100%;
  then Ultima 5; Ultima Underworld last.
- Ports must be identical to Windows. Fix differences on the port side; never change Windows to match a port.
- Every change goes into Windows and the C player (`Player/src`) together.
- Consoles use the controller; Ultima 6 games need a USB keyboard and mouse on every console. PS2 ISO max 8 GB.
- Mechanics follow Exult / Pentagram / Nuvie sources (U7 / U8 / U6); verify there before inventing.
- Write numbers as plain ASCII (3.5, not a fraction sign).

## Building
- Editor: .NET 8 WPF (`Editor/`, `Engine/`).
- C player: `Player/` - PS2/PS3/Linux toolchains live in WSL Ubuntu on the main PC (`~/ps2dev-tc/ps2dev` for the PS2);
  run the `Player/build_*.sh` scripts through `wsl.exe -d Ubuntu`. Built players go in `Editor/Runtimes/<platform>/`.
- Write helper scripts to files instead of shell heredocs (heredocs mangle backslashes).
