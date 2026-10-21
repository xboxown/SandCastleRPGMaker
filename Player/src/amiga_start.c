/*
 * The Amiga's real main: checks the machine before any floating point runs.
 * The game's own main (main.c, renamed game_main for the Amiga) saves FPU
 * registers as it starts - on a 68060 whose FPU isn't switched on (no
 * 68060.library) or a machine with none, that alone crashes with #8000000B.
 * So this file uses no floating point at all; it writes the first lines of
 * Game.log and shows a requester if the game can't run.
 */
#include <exec/exec.h>
#include <intuition/intuition.h>
#include <dos/dos.h>
#include <proto/exec.h>
#include <proto/intuition.h>
#include <proto/dos.h>
#include <string.h>

int game_main(int argc, char **argv);

static void start_note(const char *text, int fresh)
{
    BPTR f = Open((CONST_STRPTR)"Game.log", fresh ? MODE_NEWFILE : MODE_OLDFILE);
    if (!f) return;
    Seek(f, 0, OFFSET_END);
    Write(f, (APTR)text, strlen(text));
    Write(f, (APTR)"\n", 1);
    Close(f);
}

static void start_message(const char *text)
{
    struct Library *intuition = OpenLibrary((CONST_STRPTR)"intuition.library", 36);
    if (!intuition) return;
    struct Library *saved = (struct Library *)IntuitionBase;
    IntuitionBase = (struct IntuitionBase *)intuition;
    struct EasyStruct es = { sizeof(struct EasyStruct), 0, (UBYTE *)"This Amiga can't run the game", (UBYTE *)text, (UBYTE *)"OK" };
    EasyRequestArgs(NULL, &es, NULL, NULL);
    IntuitionBase = (struct IntuitionBase *)saved;
    CloseLibrary(intuition);
}

static int refuse(const char *text)
{
    start_note(text, 0);
    start_message(text);
    return 20;
}

int main(int argc, char **argv)
{
    start_note("Started (checking the machine).", 1);
    UWORD flags = SysBase->AttnFlags;
    char line[96];
    strcpy(line, "Kickstart V");
    {
        char n[8]; int v = SysBase->LibNode.lib_Version, i = 0; char r[8];
        do { r[i++] = (char)('0' + v % 10); v /= 10; } while (v && i < 7);
        int j = 0; while (i) n[j++] = r[--i]; n[j] = 0;
        strcat(line, n);
    }
    strcat(line, (flags & (1 << 10)) ? ", 68080" : (flags & (1 << 7)) ? ", 68060" : (flags & AFF_68040) ? ", 68040" : ", older CPU");
    strcat(line, (flags & (AFF_68881 | AFF_68882 | AFF_FPU40)) ? ", FPU on." : ", no FPU.");
    start_note(line, 0);
    if (SysBase->LibNode.lib_Version < 40)
        return refuse("This game needs Kickstart 3.1 or later - this Amiga has an older one.");
    if (!(flags & AFF_68040))
        return refuse("This game needs a 68060 or a 68080 (Vampire) - this Amiga's processor is older.");
    if (!(flags & (AFF_68881 | AFF_68882 | AFF_FPU40)))
        return refuse("This game needs its floating-point unit (FPU), and it isn't switched on.\n"
                      "On a 68060, install 68060.library in LIBS: (it turns the FPU on).\n"
                      "On a Vampire, use a core with an FPU.");
    return game_main(argc, argv);
}
