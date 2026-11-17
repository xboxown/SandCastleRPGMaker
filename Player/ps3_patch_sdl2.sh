#!/bin/bash
# The PS3's SDL2 (ps3libraries' sdl2_psl1ght, built by setup_ps3_toolchain.sh) gives every thread a 16 KB stack, whatever SDL
# asks for - its audio thread asks for 64 KB, and the game's mixer with the WAV decoder overflowed 16 KB (the game froze on a
# door's sound). This makes it use the size asked for (64 KB when none is), then rebuilds and reinstalls SDL2. Run once after
# setup_ps3_toolchain.sh; running it again does no harm.
set -e
export PS3DEV=${PS3DEV:-$HOME/ps3dev}
export PSL1GHT=$PS3DEV
export PATH=$PS3DEV/bin:$PS3DEV/ppu/bin:$PS3DEV/spu/bin:$PATH
cd ~/ps3toolchain/build/ps3libraries/build/sdl2_psl1ght
F=src/thread/psl1ght/SDL_systhread.c
if grep -q "size_t stack_size = 0x4000;" $F; then
  sed -i 's|size_t stack_size = 0x4000;|size_t stack_size = thread->stacksize > 0x4000 ? thread->stacksize : 0x10000; /* Sandcastle: the size asked for (was always 16 KB) */|' $F
  echo "patched $F"
else
  echo "already patched"
fi
grep -n "size_t stack_size" $F
# The picture scaled to the TV smoothly when its texture asks for it (SDL_HINT_RENDER_SCALE_QUALITY "linear") - the game's
# CRT effect: nearest-pixel made its scanlines uneven stripes (800 lines onto 1080). Always nearest before.
R=src/render/psl1ght/SDL_PSL1GHTrender.c
if grep -q "scale.interp = GCM_TRANSFER_INTERPOLATOR_NEAREST;" $R; then
  sed -i 's|scale.interp = GCM_TRANSFER_INTERPOLATOR_NEAREST;|scale.interp = texture->scaleMode == SDL_ScaleModeNearest ? GCM_TRANSFER_INTERPOLATOR_NEAREST : GCM_TRANSFER_INTERPOLATOR_LINEAR; /* Sandcastle: smooth when asked */|' $R
  echo "patched $R"
else
  echo "renderer already patched"
fi
grep -n "scale.interp" $R
make -j"$(nproc)" > ~/sdl2_rebuild.log 2>&1 && make install >> ~/sdl2_rebuild.log 2>&1
echo "SDL2 rebuilt and installed (log: ~/sdl2_rebuild.log)"
ls -l $PS3DEV/portlibs/ppu/lib/libSDL2.a
