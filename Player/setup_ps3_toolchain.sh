#!/bin/bash
# Builds the PlayStation 3 toolchain (github.com/ps3dev/ps3toolchain) into ~/ps3dev - once; it takes an hour or more.
# Steps: 1 binutils PPU, 2 gcc/newlib PPU, 4 symlinks, 5 binutils SPU, 6 gcc/newlib SPU, 8 PSL1GHT, 9 ps3libraries (SDL2...).
# The debuggers (3, 7) are left out. Needs (Ubuntu, as root once): libelf-dev libssl-dev libtool libtool-bin python-is-python3
# python-dev-is-python3 python3-setuptools clang subversion pkg-config bzip2 wget patch libncurses-dev texinfo bison flex
# libgmp-dev libmpfr-dev libmpc-dev zlib1g-dev cmake pv.
# The log: ~/ps3toolchain.log. Then: Player/build_ps3.sh.
export PS3DEV=$HOME/ps3dev
export PSL1GHT=$PS3DEV
export PATH=$PATH:$PS3DEV/bin:$PS3DEV/ppu/bin:$PS3DEV/spu/bin
export MAKEFLAGS=-j$(nproc)
mkdir -p "$PS3DEV"
[ -d ~/ps3toolchain ] || git clone --depth 1 https://github.com/ps3dev/ps3toolchain.git ~/ps3toolchain
cd ~/ps3toolchain
./toolchain.sh ${@:-1 2 4 5 6 8 9} > ~/ps3toolchain.log 2>&1 # a step number or two to redo just those (setup_ps3_toolchain.sh 9)
status=$?
echo "toolchain exit $status" >> ~/ps3toolchain.log
# SDL2's threads get the stack they ask for (its PS3 port gave all 16 KB - the game's sound overflowed it).
[ -d ~/ps3toolchain/build/ps3libraries/build/sdl2_psl1ght ] && bash "$(dirname "$0")/ps3_patch_sdl2.sh" >> ~/ps3toolchain.log 2>&1
tail -5 ~/ps3toolchain.log
exit $status
