---
name: game-errors
description: b362 game error system - numbered errors, error paperdoll with %errnum/%errdescrp/%errdetail, Show Error command
metadata:
  type: project
---
b362 (2026-10-19): Engine/GameErrors.cs holds the engine's numbers (1xx save/load, 2xx disk, 3xx data, 900 bug) and default words; System Settings > Errors lets the maker edit words, add own numbers, pick the error paperdoll. GameTestWindow.Errors.cs GameError(number, detail) opens it modal. Before data loads (missing sdbx) a plain box shows the number + maker's words read from the msrp.

**Why:** user wants every error (busy cutscene, disk full, corrupt save, fatal start) in their own paperdoll.
**How to apply:** new failures the player can hit should call GameError with a number (add a constant + default words), not just StatusText.
