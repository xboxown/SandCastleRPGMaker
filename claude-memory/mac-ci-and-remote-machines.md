---
name: mac-ci-and-remote-machines
description: "Apple silicon is built+tested by GitHub Actions (macos-latest, free, public repo); user's PPC 10.5 + Intel Mac + Ubuntu laptop reached over SSH per \"mac instruction.txt\""
metadata:
  node_type: memory
  type: project
  originSessionId: e02e316e-a1c8-42c0-a7f7-69326088b5ec
  modified: 2026-11-18T00:43:54.037Z
---

- The user has NO Apple silicon Mac. `.github/workflows/mac-apple-silicon.yml` builds Player/build_mac.sh on GitHub's
  macos-latest (arm64; macos-14 was retired - "configuration not supported") at every push touching Player/, checks arm64 in
  lipo, starts it with no game, then plays the test game 8 s and saves a screenshot. First green run 2026-11-17 (44209866720,
  macOS 26.7.2). Artifacts GameMac (GameMac.zip, universal x86_64+arm64) and AppleSiliconShot - downloading needs a GitHub
  login; results are readable without login via the check-run annotations API (::notice lines).
- The workflow publishes the tested player as the public release "mac-player" (GameMac.zip + shot.bmp, no login needed);
  tools/fetch_mac_player.sh puts it in Editor/Runtimes/Mac. The user: Apple silicon is Claude's alone - they never handle its files.
- Test game: Player/testgame/Game_Data = the user's Peter demo (public with permission: their demos are already on their
  website; they said Claude may use their games).
- LESSON: Player/.gitattributes had `* text eol=lf` and corrupted .sdbx on commit; binary exceptions added there and a root
  .gitattributes. Verify binary files with `git hash-object --no-filters` vs the index after adding new binary types.
- "mac instruction.txt" (repo root + H:\Sand Castle RPG Maker) tells the user how to set up: PowerPC Mac OS X 10.5.8 (Xcode
  3.1.4, Remote Login, no sleep, auto-login), Intel Mac (Command Line Tools), Ubuntu laptop (openssh-server). Machines ready
  ~2026-11-18; user brings user names + IPs (this PC is 192.168.1.71). Claude makes an SSH key; user installs it with a
  `! ssh-copy-id` command typing the password themselves. Leopard's SSH needs -o HostKeyAlgorithms=+ssh-rsa (use an RSA key).
- PowerPC player is unbuilt (Build dialog greys it): needs checking what SDL/compilers work on 10.5.

Related: [[all-platforms-together]], [[plan-from-nov-10]], [[platform-ports]].
