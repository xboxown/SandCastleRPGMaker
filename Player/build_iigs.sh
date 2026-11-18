#!/bin/bash
# The Apple IIgs player (Ultima 6 Apple IIgs mode): 65816 assembly, assembled by our own asm816.py (only Python needed -
# runs on Windows, Linux or a Mac). Output: Editor/Runtimes/AppleIIgs/SANDCASTLE, a GS/OS S16 application.
# Test it headless first: python Player/iigs/sim816.py SANDCASTLE <folder with GAME.DAT> out.png --keys "RRDDL"
set -e
cd "$(dirname "$0")"
mkdir -p ../Editor/Runtimes/AppleIIgs
PY=$(command -v python3 || command -v python)
"$PY" iigs/asm816.py iigs/main.s -o ../Editor/Runtimes/AppleIIgs/SANDCASTLE -l iigs/main.lst
echo "built Editor/Runtimes/AppleIIgs/SANDCASTLE"
