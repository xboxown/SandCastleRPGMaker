#!/bin/bash
# Builds EBOOT.elf for the PlayStation 3 (homebrew under HEN or a custom firmware) with PSL1GHT (github.com/ps3dev/ps3toolchain:
# its toolchain.sh with steps 1 2 4 5 6 8 9 - the compilers, PSL1GHT and ps3libraries, SDL2 among them).
# PS3DEV points at it (default ~/ps3dev). The editor signs it for each game (Engine/Ps3Package.cs: EBOOT.BIN in the PKG).
set -e
cd "$(dirname "$0")"
export PS3DEV=${PS3DEV:-$HOME/ps3dev}
export PSL1GHT=$PS3DEV
export PATH=$PS3DEV/bin:$PS3DEV/ppu/bin:$PATH
GCC=ppu-gcc
OUT=build/ps3
mkdir -p $OUT/obj
PORTLIBS=$PS3DEV/portlibs/ppu
# The same player as every other machine: platform_sdl2.c (SDL2 for PSL1GHT) with the PS3's own start-up and controller (platform_ps3.c).
# The PS3 is big-endian, as MorphOS: the player reads its data the same on both. FRAME_HIRES: drawn 960 x 600 for the 720p TV.
# NETLOG=1 (testing, the default for now; NETLOG=0 for a finished build): the log over the network (platform_ps3.c).
NETLOG=${NETLOG:-1}
NETFLAG=""; [ "$NETLOG" = 1 ] && NETFLAG="-DSANDCASTLE_NETLOG"
# The SPEs' program (the CRT effect, src/ps3spu/crt_spu.c): spu-gcc, stripped, then into $OUT/crt_spu_bin.h as bytes the main
# program loads (sysSpuImageImport).
export PATH=$PS3DEV/spu/bin:$PATH
spu-gcc -O3 -mdual-nops -fmodulo-sched -Wall -std=gnu99 -Isrc/ps3spu -I$PS3DEV/spu/include -o $OUT/crt_spu.elf src/ps3spu/crt_spu.c -L$PS3DEV/spu/lib -lsputhread
spu-strip $OUT/crt_spu.elf
( cd $OUT && xxd -i crt_spu.elf | sed 's/^unsigned char crt_spu_elf\[\]/unsigned char crt_spu_bin[] __attribute__((aligned(128)))/; s/crt_spu_elf_len/crt_spu_bin_len/' > crt_spu_bin.h )
FLAGS="$NETFLAG -DPLATFORM_PS3 -DFRAME_HIRES -mcpu=cell -mhard-float -fmodulo-sched -ffunction-sections -fdata-sections -std=gnu99 -O2 -Wall \
  -Wno-unused-function -Wno-format-truncation -Wno-unused-variable -Wno-unused-but-set-variable -Wno-misleading-indentation \
  -I$PSL1GHT/ppu/include -I$PORTLIBS/include -I$PORTLIBS/include/SDL2 -Isrc -I$OUT"
SRC="src/main.c src/world.c src/pictures.c src/font.c src/dialog.c src/events.c src/items.c src/script.c src/lighting.c src/schedules.c src/use.c src/sound.c src/weather.c src/conditions.c src/screen.c src/vitals.c src/combat.c src/errors.c src/gumps.c src/dolls.c src/save.c src/magic.c src/mechanisms.c src/vehicles.c src/video.c src/books.c src/prompt.c src/work.c src/traps.c src/eggs.c src/shop.c src/slider.c src/u6.c src/u8.c src/platform_video_none.c src/pack.c src/numbers.c src/crt.c src/platform_sdl2.c src/platform_ps3.c third_party/cJSON.c"
# main.c's main is game_main: platform_ps3.c's main runs it on a thread with a bigger stack.
compile() { f=$1; extra=""; [ "$(basename "$f")" = main.c ] && extra="-Dmain=game_main"; $GCC $FLAGS $extra -c "$f" -o "$OUT/obj/$(basename "$f" .c).o"; }
export -f compile; export GCC FLAGS OUT
printf '%s\n' $SRC | xargs -P "$(nproc)" -I{} bash -c 'compile {}'
OBJS=""
for f in $SRC; do OBJS="$OBJS $OUT/obj/$(basename "$f" .c).o"; done
$GCC -mcpu=cell -Wl,--gc-sections -L$PSL1GHT/ppu/lib -L$PORTLIBS/lib -o $OUT/EBOOT_dbg.elf $OBJS \
  -lSDL2 -lrsx -lgcm_sys -lio -lsysutil -lnet -lrt -llv2 -laudio -lsysmodule -lm
# EBOOT_dbg.elf keeps the names; EBOOT.elf is it stripped and sprx-linked (PSL1GHT's ppu_rules), ready to be signed.
ppu-strip $OUT/EBOOT_dbg.elf -o $OUT/EBOOT.elf
sprxlinker $OUT/EBOOT.elf
# The editor ships it: Build > PlayStation 3 signs it as each game's EBOOT.BIN and puts it in the PKG.
mkdir -p ../Editor/Runtimes/PS3 && cp $OUT/EBOOT.elf ../Editor/Runtimes/PS3/EBOOT.elf
# For checking the editor's signing against PSL1GHT's own tools (testing only): its make_self_npdrm, sfo and pkg.
if [ "$1" = "pkg" ]; then
  CID=UP0001-SNDC00000_00-0000000000000000
  rm -rf $OUT/pkg && mkdir -p $OUT/pkg/USRDIR
  make_self_npdrm $OUT/EBOOT.elf $OUT/pkg/USRDIR/EBOOT.BIN $CID >/dev/null
  sfo --title "Sandcastle Test" --appid SNDC00000 -f $PS3DEV/bin/sfo.xml $OUT/pkg/PARAM.SFO
  cp $PS3DEV/bin/ICON0.PNG $OUT/pkg/ICON0.PNG 2>/dev/null || true
  pkg --contentid $CID $OUT/pkg/ $OUT/test.pkg >/dev/null && cp $OUT/test.pkg $OUT/test.gnpdrm.pkg && package_finalize $OUT/test.gnpdrm.pkg
  echo "built $OUT/test.pkg and $OUT/test.gnpdrm.pkg (PSL1GHT's own, to compare)"
fi
echo "built $OUT/EBOOT.elf (and Editor/Runtimes/PS3/EBOOT.elf)"
