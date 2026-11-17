---
name: edit-scripts-via-write
description: "In this Windows Git Bash setup, heredocs mangle backslash escapes - write Python edit scripts with the Write tool"
metadata:
  node_type: memory
  type: reference
  originSessionId: 67092f50-21bd-4743-998e-b6d1e430773e
  modified: 2026-10-02T23:33:33.446Z
---

Bash heredocs here (even with quoted 'EOF') turn `\\n` / `\"` inside embedded C# into real newlines or break the shell parse. Write multi-line Python edit scripts to the scratchpad with the Write tool, then run them with `python <path>`. Build while the editor runs: `dotnet build Editor/Editor.csproj -o <scratchpad>/buildout/` (bin is locked). Related: [[build-number-bump]]
