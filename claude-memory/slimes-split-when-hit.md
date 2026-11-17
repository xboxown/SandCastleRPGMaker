---
name: slimes-split-when-hit
description: "b506 - Exult's split-when-hit (Growth choice Ultima 7/Our engine/Both), Slime Trail group, optional puddle damage, no limit, on all engines; what's proven and limits"
metadata:
  node_type: memory
  type: project
  originSessionId: 70ac2fa8-139c-4715-a70d-e64d078ee493
  modified: 2026-11-04T18:55:53.289Z
---

User's order 2026-11-04: slimes as Ultima 7, growing forever if not killed fast; "what makes a new slime" a 3-way choice; new ones stay in saves (NOT temporary, unlike Exult); a trail like blood in a red locked Sprite Editor group; a choice whether the trail hurts.

b506: JoiningSettings.Growth (OurEngine=0 default so old monsters don't change, Ultima7=1, Both=2), NoSplitFrom, TrailSpriteName, TrailHurts, TrailDamage; JoinRule.MaxOnMap default now 0. Splits are queued and done next frame because Damage can run inside a foreach over _mapCharacters. Own kind is never hurt by its trail, else slimes would split themselves.

Non-obvious: the user's map squares are 32 px. Neighbour spacing is now max(16, square size) and "near" always includes the next square; before this, on their map slimes never looked joined and rarely multiplied.

C limits: a monster's copies stop at 2146 (id = base + 1000000 x copy, a bigger one overflows int); puddles 128 on 68k, 512 on MorphOS.

Proven by real runs on a copy of test.msrp (C via WSL, Windows U7 and U8): hits split, multiplies rules idle under Ultima7, count unchanged across save+load, puddles hurt others and never slimes. NOT driven: the new Character/Sprite Editor controls by hand; a real Amiga or MorphOS machine.

Test harness: scratchpad slimepack/ (session 70ac2fa8), a rat in its own faction fights slimes, an Always page saves and loads. Windows test saves land in %LOCALAPPDATA%/SandcastleRpgMaker/Saves/<new id>: delete after.

**How to apply:** Guide 25 in CommandList.docx is the user-facing text; keep it in step. Related: [[mimic-exult-100]], [[all-platforms-together]], [[commandlist-progress]].
