#!/bin/sh
# Ultima 7 guard: the Ultima 7 engine (Editor/GameTestWindow*, Player/) is finished and frozen at tag u7-final-b471.
# Run before every Ultima 8 commit:  sh tools/u7-guard.sh
# It lists any Ultima 7 engine file changed since the tag (staged or not). Nothing listed: Ultima 7 is untouched.
# A U7 change the user asked for (or the Saturday Amiga/MorphOS work) is committed with a message starting "U7:".
cd "$(dirname "$0")/.." || exit 1
changed=$(git -c safe.directory="$(pwd -W 2>/dev/null || pwd)" diff --name-only u7-final-b471 -- 'Editor/GameTestWindow*' Player/src Player/*.sh)
if [ -n "$changed" ]; then
  echo "Ultima 7 engine files changed since u7-final-b471:"
  echo "$changed" | sed 's/^/  /'
  exit 1
fi
echo "Ultima 7 engine untouched since u7-final-b471."
