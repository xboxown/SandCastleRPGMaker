---
name: build-number-bump
description: Always increment DEBUG-BUILD number in Editor/BuildInfo.cs with every change so the user can confirm which version they run
metadata:
  node_type: memory
  type: feedback
  originSessionId: 67092f50-21bd-4743-998e-b6d1e430773e
  modified: 2026-11-17T23:40:05.295Z
---

Increment `BuildInfo.CurrentBuild` (Editor/BuildInfo.cs, "DEBUG-BUILD-NNN") with every change delivered, and build into the normal output (not a scratch folder) when the editor isn't running.

**Why:** the user tested an old binary (the running editor locked bin/, so changes only compiled to a scratch folder) and couldn't tell; they asked for the number as proof of version.
**How to apply:** bump before building; tell the user the new number. If bin/ is locked by the running editor, say so explicitly — the change isn't in their app until they close it and rebuild. Also: always back up the whole project to backup/ before any change, and commit to git (use `git -c safe.directory=H:/SandcastleRpgMaker`). Related: [[mimic-exult-100]].

From 2026-11-17 (user's rule, after b560-b610 nearly got lost uncommitted): ANY change at all - minor, big, fix, doc -
is pushed to GitHub IMMEDIATELY once Windows and all the other platforms have it: bump + a line at the top of
BUILD-HISTORY.md + copy memory/*.md into the repo's claude-memory/ + commit "bN - ..." + `git push origin master` to the
user's PUBLIC repo (github.com/xboxown/SandCastleRPGMaker - public by the user's explicit choice; GitHub is their
off-site backup, they no longer burn CDs/DVDs). CLAUDE.md in the repo holds the rules for any PC.
