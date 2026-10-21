#!/bin/sh
# Builds the player for Windows (testing only - Windows games use the .NET engine) with w64devkit and SDL2's MinGW package.
# Usage: build_windows_test.sh <w64devkit folder> <SDL2 x86_64-w64-mingw32 folder>
set -e
cd "$(dirname "$0")"
"$1/bin/gcc" -std=c99 -O2 -Wall -Wno-unused-function -o build/SandcastlePlayer.exe src/main.c src/world.c src/pictures.c src/font.c src/dialog.c src/events.c src/items.c src/script.c src/lighting.c src/schedules.c src/use.c src/sound.c src/weather.c src/conditions.c src/screen.c src/vitals.c src/combat.c src/errors.c src/gumps.c src/pack.c third_party/aes.c src/numbers.c src/platform_sdl2.c third_party/cJSON.c \
  -I"$2/include/SDL2" -L"$2/lib" -lmingw32 -lSDL2main -lSDL2 -lm -mwindows
cp "$2/bin/SDL2.dll" build/
