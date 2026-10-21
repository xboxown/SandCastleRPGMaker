#!/bin/sh
# Builds "Game" for Amiga 68k: Kickstart 3.1 or later, an RTG card or a Vampire,
# a 68060 or 68080 with its FPU. Needs bebbo's amiga-gcc (in ~/amiga, or AMIGA_GCC=...).
set -e
cd "$(dirname "$0")"
GCC=${AMIGA_GCC:-$HOME/amiga/bin/m68k-amigaos-gcc}
mkdir -p build/amiga68k
# strtod -> sc_strtod (src/numbers.c): libnix's number reading uses FMOVECR, which a 68040/68060 can't run without 68060.library.
FLAGS="-noixemul -m68060 -mhard-float -O2 -fomit-frame-pointer -std=gnu99 -Wall -Wno-unused-function -Wno-format-truncation -Dstrtod=sc_strtod"
# The real main is src/amiga_start.c (no floating point: it checks the FPU is on before anything uses it); main.c's becomes game_main.
"$GCC" $FLAGS -Dmain=game_main -c src/main.c -o build/amiga68k/main.o
"$GCC" $FLAGS -o build/amiga68k/Game \
  src/amiga_start.c build/amiga68k/main.o src/world.c src/pictures.c src/font.c src/dialog.c src/events.c src/items.c src/script.c src/lighting.c src/schedules.c src/use.c src/sound.c src/weather.c src/conditions.c src/screen.c src/numbers.c src/platform_amiga.c src/platform_amiga_audio.c third_party/cJSON.c -lm
echo "built build/amiga68k/Game"
