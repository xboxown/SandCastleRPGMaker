#!/bin/bash
# The Linux player is built in an Ubuntu 24.04 (noble) system of its own (/srv/noble): built there, it runs on Ubuntu 24.04
# and everything newer (built on a newer Ubuntu it wouldn't start on 24.04 - its C library is too old for it).
# Run once, as root: wsl -u root bash setup_linux_build.sh. Ubuntu's own debootstrap, Ubuntu's own archive.
set -e
ROOT=/srv/noble
apt-get install -y debootstrap > /dev/null
if [ ! -x $ROOT/usr/bin/gcc ]; then
  [ -d $ROOT/usr ] || debootstrap --variant=minbase noble $ROOT http://archive.ubuntu.com/ubuntu/
  echo "deb http://archive.ubuntu.com/ubuntu noble main universe" > $ROOT/etc/apt/sources.list
  echo "deb http://archive.ubuntu.com/ubuntu noble-updates main universe" >> $ROOT/etc/apt/sources.list
  chroot $ROOT apt-get update > /dev/null
  chroot $ROOT apt-get install -y --no-install-recommends gcc libc6-dev libsdl2-dev pkg-config > /dev/null
fi
chroot $ROOT gcc --version | head -1
chroot $ROOT ldd --version | head -1
chroot $ROOT pkg-config --modversion sdl2
