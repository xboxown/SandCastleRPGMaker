#!/bin/bash
# Builds "Game" for AmigaOS 4.1 (PowerPC: X1000, X5000, A1222, Sam440/460, Pegasos II, AmigaOne) through SDL2, as the
# Linux, Mac and PS3 players do (platform_sdl2.c). Needs the AmigaOS 4 cross compiler (GCC 11, adtools) with the OS4 SDK and
# SDL2 - walkero's amigagccondocker image (os4-gcc11), unpacked without Docker in ~/os4img (OS4_ROOT=... to change it).
# Run in WSL Ubuntu:   bash build_amigaos4.sh
set -e
cd "$(dirname "$0")"
OS4=${OS4_ROOT:-$HOME/os4img/opt/ppc-amigaos}
GCC=$OS4/bin/ppc-amigaos-gcc
SDK=$OS4/ppc-amigaos/SDK
OUT=build/amigaos4
mkdir -p $OUT
echo "Compiler: $($GCC --version | head -1)"
if [ "$1" = "defs" ]; then echo | "$GCC" -mcrt=newlib -dM -E - | grep -i -E "amiga|powerpc|__ppc__"; exit 0; fi
if [ "$1" = "specs" ]; then "$GCC" -mcrt=newlib -dumpspecs | grep -A3 -E "^\*(startfile|endfile|link|lib|startfile_newlib|endfile_newlib)"; exit 0; fi
SRC=""
for f in main world pictures font dialog events items script lighting schedules use sound weather conditions screen vitals combat errors gumps dolls save magic mechanisms vehicles video books prompt work traps eggs shop slider u6 u8 platform_video_none pack numbers crt platform_sdl2; do SRC="$SRC src/$f.c"; done
# newlib (OS4's standard C library), SDL2 from the SDK's local libraries, static so nothing extra is needed on the Amiga
# but SDL2's own system parts. FRAME_HIRES: the picture as fine as the display allows, as on MorphOS.
# The compiler expects to live in /opt/ppc-amigaos and finds the SDK (C library, OS headers, startup code) through its
# "base_sdk" setting; here it's elsewhere, so that one setting is given again in a specs file.
printf '*base_sdk:\n%s/\n\n' "$SDK" > $OUT/sdk.specs
"$GCC" -specs=$OUT/sdk.specs -mcrt=newlib -O2 -std=gnu99 -DFRAME_HIRES -DPLATFORM_AMIGAOS4 \
  -Wall -Wno-unused-function -Wno-format-truncation -Wno-unused-variable -Wno-unused-but-set-variable \
  -I$SDK/local/newlib/include -I$SDK/local/newlib/include/SDL2 -L$SDK/local/newlib/lib \
  -o $OUT/Game $SRC third_party/cJSON.c \
  -lSDL2 -lpthread -lm -athread=native
ls -l $OUT/Game
# The editor ships it: Build > AmigaOS 4 copies it into the game's drawer.
mkdir -p ../Editor/Runtimes/AmigaOS4 && cp $OUT/Game ../Editor/Runtimes/AmigaOS4/Game
echo "built $OUT/Game (and Editor/Runtimes/AmigaOS4/Game)"
