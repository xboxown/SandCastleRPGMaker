#!/bin/sh
# Builds "Game" for the Vampire (Apollo 68080) alone: its SAGA display taken over (two frames flipped at the vertical blank
# through SAGA's plane pointer), the frames made by AMMX (src/vampire_ammx.s, checked against C as it starts), its own 16-bit
# SAGA sound (no AHI, no Paula), 640 x 400. Won't start on another Amiga (Build > Amiga 68k (RTG) is for those).
# Needs bebbo's amiga-gcc and vasm (in ~/amiga, or AMIGA_GCC=... / AMIGA_VASM=...).
set -e
cd "$(dirname "$0")"
GCC=${AMIGA_GCC:-$HOME/amiga/bin/m68k-amigaos-gcc}
VASM=${AMIGA_VASM:-$HOME/amiga/bin/vasmm68k_mot}
OUT=build/vampire
mkdir -p $OUT
# -m68060, not -m68080: gcc's 68080 mode uses the 68080's extra A-line instructions, which the user's V4 Firebird core
# doesn't run (b613: guru 8000000A at once). The 68080 runs 68060 code at full speed; the Vampire's own parts stay (AMMX in
# vampire_ammx.s, checked as it starts; SAGA's display; its sound). strtod -> sc_strtod as in build_amiga68k.sh.
FLAGS="-noixemul -m68060 -mhard-float -O2 -fomit-frame-pointer -std=gnu99 -Wall -Wno-unused-function -Wno-format-truncation -Dstrtod=sc_strtod -DVAMPIRE -DFRAME_HIRES"
"$VASM" -m68080 -Fhunk -quiet -o $OUT/vampire_ammx.o src/vampire_ammx.s
"$GCC" $FLAGS -Dmain=game_main -c src/main.c -o $OUT/main.o
"$GCC" $FLAGS -o $OUT/Game \
  src/amiga_start.c $OUT/main.o src/world.c src/pictures.c src/font.c src/dialog.c src/events.c src/items.c src/script.c src/lighting.c src/schedules.c src/use.c src/sound.c src/weather.c src/conditions.c src/screen.c src/vitals.c src/combat.c src/errors.c src/gumps.c src/dolls.c src/save.c src/magic.c src/mechanisms.c src/vehicles.c src/video.c src/books.c src/prompt.c src/work.c src/traps.c src/eggs.c src/shop.c src/slider.c src/u6.c src/u8.c src/platform_video_none.c src/pack.c src/numbers.c src/crt.c src/platform_amiga.c src/platform_amiga_audio.c third_party/cJSON.c $OUT/vampire_ammx.o -lm
mkdir -p ../Editor/Runtimes/AmigaVampire && cp $OUT/Game ../Editor/Runtimes/AmigaVampire/Game
echo "built $OUT/Game (and Editor/Runtimes/AmigaVampire/Game)"
