The Sandcastle player - the C engine that runs games built in Sandcastle RPG Maker
on the platforms the Windows engine doesn't: Linux, Mac (Intel, Apple chips, PowerPC),
MorphOS, AmigaOS 4 and Amiga 68k (RTG or Vampire).

src/platform.h      what each machine provides (screen, keys, mouse, time, memory)
src/platform_sdl2.c that, on SDL2 (Linux, Mac, MorphOS, AmigaOS 4; Windows for testing)
src/world.c         the world: drawing it and walking in it, as the Windows engine does
src/pictures.c      sprites and their pictures
src/main.c          the program

The game it plays is a folder written by the editor's Build (Engine/PlayerPack.cs):
game.json, sprites.json and pictures/.

Milestone 1 (now): the world drawn, walking by Exult's rules, the camera.
Next: events and dialogs, doors, containers, books; characters and schedules;
combat; sound and music; then the Amiga 68k layer (RTG, AHI or Paula).

Testing switches: --shot file.bmp (saves the frame and quits), --keys "R:1.5,U:0.5".
