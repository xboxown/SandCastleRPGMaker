#!/bin/bash
# Builds the Mac player - one program for Intel and Apple silicon Macs (macOS 10.13 and newer) - on a Mac with Apple's
# Command Line Tools. Run it from Terminal in this folder (the Player folder copied to the Mac):
#     bash build_mac.sh
# It makes build/mac/Game.app (the player with SDL2 inside it) and build/mac/GameMac.zip. Copy GameMac.zip back to the PC,
# into H:\SandcastleRpgMaker\Editor\Runtimes\Mac\ - the editor's Build > Mac puts each game's data into a copy of it.
set -e
cd "$(dirname "$0")"
OUT=build/mac
SDL_VERSION=2.30.9
MIN_MAC=10.13

# ---- Apple's Command Line Tools (the C compiler) ----
if ! xcode-select -p >/dev/null 2>&1; then
  echo "Apple's Command Line Tools aren't installed. A window opens to install them: click Install, wait for it to finish,"
  echo "then run this script again."
  xcode-select --install || true
  exit 1
fi
echo "Compiler: $(clang --version | head -1)"

# ---- SDL2, Apple's universal framework from libsdl.org's release ----
mkdir -p $OUT
if [ ! -d $OUT/SDL2.framework ]; then
  echo "Downloading SDL2 $SDL_VERSION..."
  curl -L --fail -o $OUT/SDL2.dmg "https://github.com/libsdl-org/SDL/releases/download/release-$SDL_VERSION/SDL2-$SDL_VERSION.dmg"
  MOUNT=$(mktemp -d)
  hdiutil attach $OUT/SDL2.dmg -nobrowse -readonly -mountpoint "$MOUNT" >/dev/null
  cp -R "$MOUNT/SDL2.framework" $OUT/
  hdiutil detach "$MOUNT" >/dev/null
  rm -f $OUT/SDL2.dmg
fi

# ---- The player: Intel and Apple silicon in one, if the tools can make both ----
ARCHS="-arch x86_64 -arch arm64"
echo 'int main(void){return 0;}' > $OUT/archtest.c
if ! clang -arch arm64 -mmacosx-version-min=11.0 -o $OUT/archtest $OUT/archtest.c 2>/dev/null; then
  echo "These Command Line Tools can't build for Apple silicon (they're too old): building for Intel Macs only."
  ARCHS="-arch x86_64"
fi
rm -f $OUT/archtest $OUT/archtest.c
SRC=""
for f in main world pictures font dialog events items script lighting schedules use sound weather conditions screen vitals combat errors gumps dolls save magic mechanisms vehicles video books prompt work traps eggs shop slider u6 u8 platform_video_none pack numbers crt platform_sdl2; do SRC="$SRC src/$f.c"; done
echo "Compiling ($ARCHS)..."
clang $ARCHS -mmacosx-version-min=$MIN_MAC -std=gnu99 -O2 -DSANDCASTLE_INSTALLED -DFRAME_HIRES \
  -Wno-unused-function -Wno-format-truncation -Wno-unused-variable -Wno-unused-but-set-variable -Wno-misleading-indentation \
  -Wno-unknown-warning-option -F$OUT -I$OUT/SDL2.framework/Headers \
  -o $OUT/Game $SRC third_party/cJSON.c -F$OUT -framework SDL2 -Wl,-rpath,@executable_path/../Frameworks -lm

# ---- Game.app: the program, SDL2 inside it, Contents/Resources for the game's data (the editor fills it) ----
APP=$OUT/Game.app
rm -rf $APP
mkdir -p $APP/Contents/MacOS $APP/Contents/Frameworks $APP/Contents/Resources
mv $OUT/Game $APP/Contents/MacOS/Game
cp -R $OUT/SDL2.framework $APP/Contents/Frameworks/
cat > $APP/Contents/Info.plist <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>Game</string>
  <key>CFBundleIdentifier</key><string>com.sandcastle.game</string>
  <key>CFBundleName</key><string>Game</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>10.13</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST
# Signed ad hoc (no Apple account needed): Apple silicon Macs run only signed programs.
codesign --force --deep --sign - $APP
echo "Checking it starts (no game data yet - it should say it can't find the game, then close)..."
( $APP/Contents/MacOS/Game window 2>&1 & PID=$!; sleep 4; kill $PID 2>/dev/null ) | head -3 || true
lipo -info $APP/Contents/MacOS/Game
# Zipped with its links kept (SDL2.framework needs them), to take back to the PC.
( cd $OUT && rm -f GameMac.zip && ditto -c -k --keepParent Game.app GameMac.zip )
echo
echo "Done: $OUT/GameMac.zip - copy it to the PC, into H:\\SandcastleRpgMaker\\Editor\\Runtimes\\Mac\\"
