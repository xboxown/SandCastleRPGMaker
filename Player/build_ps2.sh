#!/bin/bash
# Builds GAME.ELF for the PlayStation 2 (homebrew: PCSX2, or a PS2 running FreeMcBoot / OPL) with the ps2dev toolchain.
# Needs ps2dev (github.com/ps2dev/ps2dev releases) unpacked; PS2DEV points at it (default ~/ps2dev-tc/ps2dev).
#   build_ps2.sh        the player the editor ships (copied to Editor/Runtimes/PS2)
#   build_ps2.sh diag   a testing one in build/ps2diag: in PCSX2 with the host file system on, it saves frames as
#                       pictures beside the emulator (host:frameNNNN.bmp); never shipped
set -e
cd "$(dirname "$0")"
export PS2DEV=${PS2DEV:-$HOME/ps2dev-tc/ps2dev}
export PS2SDK=$PS2DEV/ps2sdk
export PATH=$PS2DEV/bin:$PS2DEV/ee/bin:$PS2DEV/iop/bin:$PS2DEV/dvp/bin:$PS2SDK/bin:$PATH
GCC=mips64r5900el-ps2-elf-gcc
OUT=build/ps2
EXTRA=""
if [ "$1" = "diag" ]; then OUT=build/ps2diag; EXTRA="-DPS2_DIAG_SHOTS"; fi
# build_ps2.sh diag hires: the testing one drawn at 640 x 400 (FRAME_HIRES, 2 x) - to measure what it costs
if [ "$2" = "hires" ]; then OUT=build/ps2hires; EXTRA="$EXTRA -DFRAME_HIRES"; fi
mkdir -p $OUT/obj
# The same player as every other machine; platform_sdl2.c with the PS2's own start-up, disc, memory card and controller (platform_ps2.c).
FLAGS="-include src/ps2_files.h -DPLATFORM_PS2 -D_EE -G0 -std=gnu99 -g -Wall -Wno-unused-function -Wno-format-truncation -Wno-unused-variable -Wno-unused-but-set-variable -Wno-misleading-indentation $EXTRA \
  -I$PS2SDK/ee/include -I$PS2SDK/common/include -I$PS2SDK/ports/include -I$PS2SDK/ports/include/SDL2 -I$PS2DEV/gsKit/include"
SRC="src/main.c src/world.c src/pictures.c src/font.c src/dialog.c src/events.c src/items.c src/script.c src/lighting.c src/schedules.c src/use.c src/sound.c src/weather.c src/conditions.c src/screen.c src/vitals.c src/combat.c src/errors.c src/gumps.c src/dolls.c src/save.c src/magic.c src/mechanisms.c src/vehicles.c src/video.c src/books.c src/prompt.c src/work.c src/traps.c src/eggs.c src/shop.c src/slider.c src/u6.c src/u8.c src/platform_video_none.c src/pack.c src/numbers.c src/crt.c src/platform_sdl2.c src/platform_ps2.c third_party/cJSON.c"
# Each file compiled at the same time as the others (one per processor).
compile() {
  f=$1; o=$OUT/obj/$(basename "$f" .c).o
  # The r5900 compiler gives up on a few very big functions at -O2 ("maximum number of generated reload insns"): those at -O1.
  if ! $GCC $FLAGS -O2 -c "$f" -o "$o" 2>$o.err; then
    if ! grep -q "reload insns" $o.err; then cat $o.err; return 1; fi
    echo "  $f: -O1 (the compiler's limit at -O2)"
    $GCC $FLAGS -O1 -c "$f" -o "$o" 2>/dev/null || { echo "  $f: -O0"; $GCC $FLAGS -O0 -c "$f" -o "$o"; }
  fi
}
export -f compile; export GCC FLAGS OUT
printf '%s\n' $SRC | xargs -P "$(nproc)" -I{} bash -c 'compile {}'
OBJS=""
for f in $SRC; do OBJS="$OBJS $OUT/obj/$(basename "$f" .c).o"; done
# malloc / calloc / realloc / memset wrapped (platform_ps2.c): running out of the PS2's 32 MB is told, never a silent crash.
$GCC -Wl,--wrap=malloc,--wrap=calloc,--wrap=realloc,--wrap=memset -T$PS2SDK/ee/startup/linkfile -L$PS2SDK/ee/lib -L$PS2SDK/ports/lib -L$PS2DEV/gsKit/lib -o $OUT/GAME_dbg.ELF $OBJS \
  -lSDL2main -lSDL2 -lm -lpatches -lgskit -ldmakit -lps2_drivers -lpad -laudsrv -lcdvd -lmc -lkbd -lmouse -lpoweroff -lfileXio -lpatches -ldebug
# GAME_dbg.ELF keeps the names (mips64r5900el-ps2-elf-addr2line -f -e GAME_dbg.ELF <address from PCSX2's log>); GAME.ELF is it stripped.
mips64r5900el-ps2-elf-strip -o $OUT/GAME.ELF $OUT/GAME_dbg.ELF
if [ "$1" = "diag" ]; then echo "built $OUT/GAME.ELF (testing only)"; exit 0; fi
# The editor ships it: Build > PlayStation 2 puts it on the game's disc (ISO).
mkdir -p ../Editor/Runtimes/PS2 && cp $OUT/GAME.ELF ../Editor/Runtimes/PS2/GAME.ELF
echo "built $OUT/GAME.ELF (and Editor/Runtimes/PS2/GAME.ELF)"
