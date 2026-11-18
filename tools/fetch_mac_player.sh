#!/bin/bash
# The Mac player (Intel + Apple silicon) as GitHub's Apple silicon Mac built and tested it (.github/workflows/mac-apple-silicon.yml),
# fetched from the public "mac-player" release into Editor/Runtimes/Mac - Claude runs this after a player change has passed there.
set -e
cd "$(dirname "$0")/.."
mkdir -p Editor/Runtimes/Mac
curl -L --fail -s -o Editor/Runtimes/Mac/GameMac.zip.new https://github.com/xboxown/SandCastleRPGMaker/releases/download/mac-player/GameMac.zip
mv Editor/Runtimes/Mac/GameMac.zip.new Editor/Runtimes/Mac/GameMac.zip
ls -l Editor/Runtimes/Mac/GameMac.zip
