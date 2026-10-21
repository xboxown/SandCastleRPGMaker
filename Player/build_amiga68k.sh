#!/bin/sh
# Builds "Game" for Amiga 68k: Kickstart 3.1 or later, an RTG card or a Vampire,
# a 68060 or 68080 with its FPU. Needs bebbo's amiga-gcc (in ~/amiga, or AMIGA_GCC=...).
set -e
cd "$(dirname "$0")"
GCC=${AMIGA_GCC:-$HOME/amiga/bin/m68k-amigaos-gcc}
mkdir -p build/amiga68k
# strtod -> sc_strtod (src/numbers.c): libnix's number reading uses FMOVECR, which a 68040/68060 can't run without 68060.library.
"$GCC" -noixemul -m68060 -mhard-float -O2 -fomit-frame-pointer -std=gnu99 -Wall -Wno-unused-function -Wno-format-truncation -Dstrtod=sc_strtod \
  -o build/amiga68k/Game \
  src/main.c src/world.c src/pictures.c src/font.c src/numbers.c src/platform_amiga.c third_party/cJSON.c -lm
echo "built build/amiga68k/Game"
