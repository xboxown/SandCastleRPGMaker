---
name: issues-odt-2026-11-23
description: "Fourth issues.odt (2026-11-23): bugs + small features done in b665; PERMISSIONS.txt awaits the user's yes/no (filters, Event executed picker, Cast Spell check, runtime things, Quick Wall Plot, sprite generator)"
metadata:
  node_type: memory
  type: project
  originSessionId: e02e316e-a1c8-42c0-a7f7-69326088b5ec
  modified: 2026-11-24T09:41:46.948Z
---

Done in b665: armour to own slot (Character Editor), bucket reach behind walls, followed character's candle, upper-floor blood/bodies hidden, U7 events close paperdolls, AmigaOS 4 crash (stale doll press), Cave Darkness 0-1, U7 four directions (Exult dir/2), Add Command search. Fire sword = Object Editor > Weapon/Armor > Magic > Light (already existed).

Waiting on the user: H:\SandcastleRpgMaker\PERMISSIONS.txt - read the "Answer:" lines before doing any of: item picker filters (1a-1m), Event executed picker redesign, Cast Spell red/black + common-event names, Find Thing / From a variable commands, Quick Wall Plot (wall kit with 16 pieces + wall pencil), sprite generator (A parts-based composer first, B local AI later; asked for GPU model and warned about Ultima art copyright).

b666 (2026-11-24): Quick Wall Plot DONE (user GO) - Tools > Wall Kits (Editor/WallKitWindow.cs, Engine/WallKitModel.cs), Mode > Wall Plot (Editor/MainWindow.WallPlot.cs); editor-only, no player change. User has an NVIDIA RTX 3050 (sprite generator B); still waiting for GO A / A+B and the other PERMISSIONS answers.

PERMISSIONS answered 2026-11-24: b667 item picker filters (ItemPickFilter; new Object Editor 'Pressure plate' tick) + EventPickerWindow; b668 FormulaBox colours (var blue, array green, unknown spell red) + Cast Spell runs a Common Event of that name (C too); b669 Find Thing / Thing Info / 'From a variable' in MapThingField (handles: object InstanceId, items 1000000+id; C_FindThing 233, C_ThingInfo 234). 1i (levers) unanswered = unchanged; 1l kept. NEXT: sprite generator GO A AND B (user takes copyright responsibility): A = skeleton + mesh (parts on bones), U7 art as skeleton reference, user-editable reference bones, U6/U7/U8 libraries versioned, review window, 'all possible ways to control: controllers and text'; B = local AI 'integrated in your DLL' (RTX 3050). Gap noted: a Common-Event spell doesn't get Cast Spell's caster/target passed.

AmigaOS 4 black screen (QEMU pegasos2, virtio GPU): advice given - `setenv SDL_RENDER_DRIVER software` (or opengles2), 32-bit screen mode; not fixed in code.

**Why:** user's rule below the ATTENTION line: explain first, change only when asked.
**How to apply:** after the user answers PERMISSIONS.txt, do the YES items; then resume the IIgs audit ([[iigs-audit]]).
