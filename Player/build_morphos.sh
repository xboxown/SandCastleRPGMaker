#!/bin/sh
# Builds "Game" for MorphOS 3.x (PowerPC: Pegasos, PowerPC Macs, Efika).
# Needs the MorphOS cross SDK in /gg (setup-cross-sdk.sh from morphos-team.net).
set -e
cd "$(dirname "$0")"
GCC=${MORPHOS_GCC:-/gg/bin/ppc-morphos-gcc}
mkdir -p build/morphos
FLAGS="-noixemul -O2 -std=gnu99 -Wall -Wno-unused-function -Wno-format-truncation -Dstrtod=sc_strtod"
"$GCC" $FLAGS -o build/morphos/Game \
  src/main.c src/world.c src/pictures.c src/font.c src/dialog.c src/events.c src/items.c src/script.c src/lighting.c src/schedules.c src/use.c src/sound.c src/weather.c src/conditions.c src/screen.c src/vitals.c src/combat.c src/errors.c src/gumps.c src/dolls.c src/save.c src/magic.c src/mechanisms.c src/vehicles.c src/video.c src/books.c src/prompt.c src/work.c src/traps.c src/eggs.c src/shop.c src/slider.c src/platform_video_none.c src/pack.c src/numbers.c src/platform_amiga.c src/platform_amiga_audio.c third_party/cJSON.c -lm
# The editor ships it: Build > MorphOS copies it into the game's drawer.
mkdir -p ../Editor/Runtimes/MorphOS && cp build/morphos/Game ../Editor/Runtimes/MorphOS/Game
echo "built build/morphos/Game (and Editor/Runtimes/MorphOS/Game)"
