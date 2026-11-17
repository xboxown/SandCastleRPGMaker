/* PlayStation 2 builds only (build_ps2.sh puts it before every file with -include): the game's data read from the disc
   by its short upper-case names (platform_ps2.c: ps2_fopen); the saves on the memory card. */
#ifndef SANDCASTLE_PS2_FILES_H
#define SANDCASTLE_PS2_FILES_H
#ifdef PLATFORM_PS2
#include <stdio.h>
FILE *ps2_fopen(const char *path, const char *mode);
void ps2_boot(int argc, char **argv, char *folder, int folderSize);
const char *ps2_saves_folder(void);
#define fopen ps2_fopen
#endif
#endif
