#!/bin/bash
# Tests a game's .deb in the Ubuntu 24.04 system (/srv/noble) as Ubuntu would: dpkg reads it, apt installs it (and what it
# needs), the installed game starts from its menu entry's command, saves where it should, and it uninstalls. As root:
#     wsl -u root bash test_linux_deb.sh /mnt/h/ClaudeTemp/linux/test2.deb
set -e
DEB=$1
ROOT=/srv/noble
cp "$DEB" $ROOT/tmp/test.deb
mountpoint -q $ROOT/proc || mount -t proc proc $ROOT/proc # as on a real Ubuntu (the player asks /proc where it is)
echo "== the package"
chroot $ROOT dpkg-deb --info /tmp/test.deb | sed -n '/Package:/,$p'
echo "== a few of its files"
chroot $ROOT dpkg-deb -c /tmp/test.deb | awk '{print $1, $6}' | head -12
echo "== installed by apt (what it needs comes with it)"
chroot $ROOT apt-get install -y /tmp/test.deb 2>&1 | grep -E "^(Setting up|Unpacking|The following|  )" | head -12
PKG=$(chroot $ROOT dpkg-deb -f /tmp/test.deb Package)
echo "== its menu entry"
chroot $ROOT cat /usr/share/applications/$PKG.desktop
EXEC=$(chroot $ROOT sed -n 's/^Exec=//p' /usr/share/applications/$PKG.desktop)
echo "== started as the menu starts it, by a user (not root), for 12 seconds, saving once"
chroot $ROOT bash -c "id player >/dev/null 2>&1 || useradd -m player"
chroot $ROOT su player -c "cd ~ && SDL_VIDEODRIVER=dummy SDL_AUDIODRIVER=dummy timeout 15 $EXEC nosound window --notitle --press '6:Y' --shot-at 12 --shot /tmp/debshot.bmp > /tmp/debrun.txt 2>&1; echo exit \$?"
echo "its own folder in the player's home (saves go in it): $(chroot $ROOT su player -c "ls -d ~/.local/share/'Sandcastle RPG Maker'/$PKG 2>&1")"
echo "screenshot: $(ls -l $ROOT/tmp/debshot.bmp 2>&1 | awk '{print $5}') bytes"
echo "== removed"
chroot $ROOT apt-get remove -y $PKG 2>&1 | grep -E "^Removing" ; chroot $ROOT ls /opt
