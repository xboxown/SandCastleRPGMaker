#!/bin/bash
# Builds the Linux player (64-bit, Ubuntu 24.04 and newer) in the Ubuntu 24.04 system setup_linux_build.sh made (/srv/noble),
# installed-mode (its data where the .deb puts it, its saves in the player's own ~/.local/share), into
# Editor/Runtimes/Linux/Game - Build > Linux puts it in each game's .deb. Run as root: wsl -u root bash build_linux.sh
set -e
cd "$(dirname "$0")"
ROOT=/srv/noble
[ -x $ROOT/usr/bin/gcc ] || { echo "Run setup_linux_build.sh first (as root)."; exit 1; }
rm -rf $ROOT/build/player && mkdir -p $ROOT/build/player
cp -r src third_party $ROOT/build/player/
SRC=""
for f in main world pictures font dialog events items script lighting schedules use sound weather conditions screen vitals combat errors gumps dolls save magic mechanisms vehicles video books prompt work traps eggs shop slider u6 u8 platform_video_none pack numbers crt platform_sdl2; do SRC="$SRC src/$f.c"; done
chroot $ROOT bash -c "cd /build/player && gcc -std=gnu99 -O2 -DSANDCASTLE_INSTALLED -DFRAME_HIRES -Wall -Wno-unused-function -Wno-format-truncation \
  -Wno-unused-variable -Wno-unused-but-set-variable -Wno-misleading-indentation \$(pkg-config --cflags sdl2) -o Game $SRC third_party/cJSON.c \
  \$(pkg-config --libs sdl2) -lm -Wl,--as-needed && strip Game"
mkdir -p ../Editor/Runtimes/Linux
cp $ROOT/build/player/Game ../Editor/Runtimes/Linux/Game
chmod 755 ../Editor/Runtimes/Linux/Game
echo "newest C library it needs: $(objdump -T ../Editor/Runtimes/Linux/Game | grep -o 'GLIBC_[0-9.]*' | sort -V | tail -1) (Ubuntu 24.04 has 2.39)"
echo "libraries: $(objdump -p ../Editor/Runtimes/Linux/Game | awk '/NEEDED/ {print $2}' | tr '\n' ' ')"
echo "built Editor/Runtimes/Linux/Game"
