#!/bin/sh
# Ultima 7 guard: the Ultima 7 engine (Editor/GameTestWindow*, Player/) is frozen at its newest u7-* tag
# (u7-final-b471, then a tag after each Ultima 7 change the user asked for: u7-approved-b481...).
# Run before every Ultima 8 commit:  sh tools/u7-guard.sh
# It lists any Ultima 7 engine file changed since the tag (staged or not). Nothing listed: Ultima 7 is untouched.
# A U7 change the user asked for (or the Saturday Amiga/MorphOS work) is committed with a message starting "U7:".
cd "$(dirname "$0")/.." || exit 1
g="git -c safe.directory=$(pwd -W 2>/dev/null || pwd)"
base=$($g tag --list 'u7-*' --sort=-creatordate | head -1)
changed=$($g diff --name-only "$base" -- 'Editor/GameTestWindow*' Player/src Player/*.sh)
if [ -n "$changed" ]; then
  echo "Ultima 7 engine files changed since $base:"
  echo "$changed" | sed 's/^/  /'
  exit 1
fi
echo "Ultima 7 engine untouched since $base."
