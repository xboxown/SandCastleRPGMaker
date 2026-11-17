/*
 * The PlayStation 2's own part of the player (built with PLATFORM_PS2, beside platform_sdl2.c):
 *  - start-up: the IOP's drivers (disc, memory card, USB keyboard and mouse, controller, sound);
 *  - the game's data on the disc: cdrom0:\DATA\, its files by short upper-case names (GAME.SDB...),
 *    the same rule the editor's ISO writer uses (Engine/Ps2Iso.cs: Ps2Name);
 *  - the saves on the memory card: mc0:/SANDCAST/<the game's folder>/;
 *  - the controller as the Windows game's keyboard and mouse (ps2_controller below).
 */
#ifdef PLATFORM_PS2
#include "platform.h"
#include <ctype.h>
#include <stdio.h>
#include <string.h>
#include <kernel.h>
#include <sifrpc.h>
#include <iopcontrol.h>
#include <loadfile.h>
#include <sbv_patches.h>
#include <ps2_filesystem_driver.h>
#include <ps2_joystick_driver.h>
#include <ps2_keyboard_driver.h>
#include <ps2_mouse_driver.h>
#include <ps2_audio_driver.h>
#undef fopen /* the real one, here */

static char dataRoot[64] = "cdrom0:\\DATA";

/* Drivers loaded, the data's place found (the disc - or, testing from a USB stick / host:, the folder the ELF is in). */
void ps2_boot(int argc, char **argv, char *folder, int folderSize)
{
    SifInitRpc(0);
    /* No reset of the I/O processor here: started from an ESR disc, a reset (to the console's own modules) loses ESR's
       disc reader - a real PS2 stopped on a purple screen (b567). */
    sbv_patch_enable_lmb();
    sbv_patch_disable_prefix_check();
    init_ps2_filesystem_driver();                 /* disc, memory card, USB storage */
    int pads = init_joystick_driver(true);        /* the controller (padman) */
    int keys = init_keyboard_driver(true);        /* a USB keyboard - Ultima 6 projects need one */
    int mice = init_mouse_driver(true);           /* a USB mouse */
    int sound = init_audio_driver();
    printf("Game: Drivers: controller %d, keyboard %d, mouse %d, sound %d (1 or 0: loaded)\n", pads, keys, mice, sound);
    const char *boot = argc > 0 && argv[0] ? argv[0] : "";
    if (strncmp(boot, "cdrom0:", 7) != 0 && boot[0])
    {
        /* Not from the disc (mass:/..., host:/...): the data beside the ELF, in DATA/. */
        char dir[256];
        snprintf(dir, sizeof dir, "%s", boot);
        char *slash = strrchr(dir, '/'), *back = strrchr(dir, '\\');
        if (back > slash) slash = back;
        if (slash) *slash = 0; else if (strchr(dir, ':')) strchr(dir, ':')[1] = 0;
        waitUntilDeviceIsReady(dir);
        snprintf(dataRoot, sizeof dataRoot, "%s/DATA", dir);
    }
    snprintf(folder, folderSize, "%s", dataRoot);
}

/* A file's name on the disc: upper case, 8 letters and a 3-letter ending (ISO 9660 level 1), ";1" on the disc. */
static void short_name(const char *name, char *out, int size)
{
    char base[16] = "", ext[8] = "";
    const char *dot = strrchr(name, '.');
    int b = 0, e = 0;
    for (const char *p = name; *p && p != dot && b < 8; p++) if (isalnum((unsigned char)*p) || *p == '_') base[b++] = (char)toupper((unsigned char)*p);
    if (dot) for (const char *p = dot + 1; *p && e < 3; p++) if (isalnum((unsigned char)*p) || *p == '_') ext[e++] = (char)toupper((unsigned char)*p);
    base[b] = 0; ext[e] = 0;
    snprintf(out, size, e ? "%s.%s" : "%s", base, ext);
}

/* ---------------- The memory cards: slot 1 only, and never a Free McBoot card ----------------
   The game sees only the card in slot 1 (mc0:) - slot 2 (mc1:), where Free McBoot's card usually is, doesn't exist for it.
   And if the card in slot 1 is a Free McBoot card (its folders on it), nothing is ever written to it: saving says
   "FMCB DETECTED." (error 113) - a damaged FMCB card can leave the console unable to start homebrew. */
/* A file there: opened for reading (stat() on the memory card says yes even for folders that aren't there). */
static int exists(const char *path) { FILE *f = fopen(path, "rb"); if (f) fclose(f); return f != NULL; }
int ps2_fmcb_on_card(void)
{
    static const char *SIGNS[] = { "mc0:/SYS-CONF/FREEMCB.CNF", "mc0:/SYS-CONF/FMCB_CFG.ELF", "mc0:/SYS-CONF/USBD.IRX",
                                   "mc0:/BEXEC-SYSTEM/osdmain.elf", "mc0:/BAEXEC-SYSTEM/osdmain.elf", "mc0:/BIEXEC-SYSTEM/osdmain.elf",
                                   "mc0:/BCEXEC-SYSTEM/osdmain.elf", "mc0:/BEEXEC-SYSTEM/osdmain.elf", "mc0:/BOOT/BOOT.ELF",
                                   "mc0:/FORTUNA/icon.icn", "mc0:/OPENTUNA/icon.icn" };
    for (size_t i = 0; i < sizeof SIGNS / sizeof SIGNS[0]; i++)
        if (exists(SIGNS[i])) { printf("Game: Memory card: Free McBoot found (%s) - not written to\n", SIGNS[i]); return 1; }
    return 0;
}
/* A path the game may write: on the card in slot 1, and that card not Free McBoot's. */
int ps2_may_write(const char *path)
{
    if (!strncmp(path, "mc1:", 4)) return 0;
    if (!strncmp(path, "mc0:", 4) && ps2_fmcb_on_card()) return 0;
    return 1;
}

/* fopen for the player on the PS2: a file of the game's data -> its place on the disc; anything else (the memory card) as it is. */
FILE *ps2_fopen(const char *path, const char *mode)
{
    if (!strncmp(path, "mc1:", 4)) return NULL; /* slot 2: never seen */
    if (strpbrk(mode, "wa+") && !ps2_may_write(path)) return NULL;
    if (strncmp(path, dataRoot, strlen(dataRoot)) == 0)
    {
        const char *name = path + strlen(dataRoot);
        while (*name == '/' || *name == '\\') name++;
        char shortName[16], real[160];
        short_name(name, shortName, sizeof shortName);
        if (strncmp(dataRoot, "cdrom0:", 7) == 0) snprintf(real, sizeof real, "%s\\%s;1", dataRoot, shortName);
        else snprintf(real, sizeof real, "%s/%s", dataRoot, shortName);
        return fopen(real, mode);
    }
    return fopen(path, mode);
}

/* The saves: on the memory card in slot 1, in the game's own folder. */
const char *ps2_saves_folder(void) { return "mc0:/SANDCAST"; }

long ps2_free_memory_mb(void) { return 32; }

/* ---------------- Sound: the PS2's own sound tasks (not SDL's) ----------------
   The EE runs the highest-priority task that is ready, and never shares time between the rest - with SDL's sound task
   the game's own task kept the processor, and the sound chip replayed its last piece over and over. So:
   - the mixer: above the game, wakes when the sound chip has room, mixes 1024 frames (23 ms) and goes back to sleep;
     it never reads the disc;
   - the reader (sound.c's sound_decode_ahead): between the two, reads and decodes what's playing ahead of the mixer
     into each sound's own buffer (a second and a half of music), waiting on the disc while the game runs. */
#include <audsrv.h>
#define MIX_FRAMES 1024
static AudioFill ps2Fill;
static int audioLock = -1, decoderLock = -1, decoderWake = -1;
static int gamePriority = -1; /* the game's own task's priority (the mixer 2 above it, the reader 1 above) */
static int16_t mixBuffer[MIX_FRAMES * 2] __attribute__((aligned(64)));
static unsigned char mixerStack[0x8000] __attribute__((aligned(16))), readerStack[0x10000] __attribute__((aligned(16)));
extern void *_gp;
int sound_decode_ahead(int fresh);

static int make_sema(int start, int most) { ee_sema_t s; memset(&s, 0, sizeof s); s.init_count = start; s.max_count = most; return CreateSema(&s); }
static int start_task(void (*run)(void *), void *stack, int size, int priority)
{
    ee_thread_t t; memset(&t, 0, sizeof t);
    t.func = (void *)run; t.stack = stack; t.stack_size = size; t.gp_reg = &_gp; t.initial_priority = priority;
    int id = CreateThread(&t);
    if (id >= 0) StartThread(id, NULL);
    return id;
}

static void mixer_task(void *unused)
{
    (void)unused;
    for (;;)
    {
        audsrv_wait_audio(sizeof mixBuffer);   /* asleep until the sound chip has room */
        WaitSema(audioLock);
        ps2Fill(mixBuffer, MIX_FRAMES);
        SignalSema(audioLock);
        audsrv_play_audio((const char *)mixBuffer, sizeof mixBuffer);
        SignalSema(decoderWake);               /* the reader: top the buffers up */
    }
}

static void reader_task(void *unused)
{
    (void)unused;
    for (;;)
    {
        if (sound_decode_ahead(1)) while (sound_decode_ahead(0)) { } /* until every sound has 1.5 s ahead (and a little more) */
        WaitSema(decoderWake);
    }
}

void ps2_decoder_lock(void) { if (decoderLock >= 0) WaitSema(decoderLock); }
void ps2_decoder_unlock(void) { if (decoderLock >= 0) SignalSema(decoderLock); }
void ps2_decoder_wake(void) { if (decoderWake >= 0) SignalSema(decoderWake); }

const char *platform_audio_name(void) { return "PS2 audsrv"; }
void platform_audio_route(int which) { (void)which; }

int platform_audio_open(int rate, AudioFill fill)
{
    /* 48,000 Hz: the sound chip's own rate (b578). At any other rate audsrv changes the rate itself, on the I/O processor -
       the same small processor that reads the disc and the controller. The mixer here changes each sound's rate anyway. */
    (void)rate; rate = 48000;
    struct audsrv_fmt_t format;
    format.freq = rate; format.bits = 16; format.channels = 2;
    if (audsrv_set_format(&format) != 0) { printf("Game: Sound chip: format refused (%s)\n", audsrv_get_error_string()); return 0; }
    audsrv_set_volume(MAX_VOLUME);
    ps2Fill = fill;
    audioLock = make_sema(1, 1); decoderLock = make_sema(1, 1); decoderWake = make_sema(0, 1);
    ee_thread_status_t me; memset(&me, 0, sizeof me);
    ReferThreadStatus(GetThreadId(), &me);
    int game = me.current_priority;
    if (game < 3) { game = 3; ChangeThreadPriority(GetThreadId(), game); } /* room above the game for the two */
    printf("Game: Sound tasks: game at priority %d, mixer %d, reader %d\n", game, game - 2, game - 1);
    if (start_task(reader_task, readerStack, sizeof readerStack, game - 1) < 0 || start_task(mixer_task, mixerStack, sizeof mixerStack, game - 2) < 0) return 0;
    gamePriority = game;
    return rate;
}

void platform_audio_close(void) { audsrv_stop_audio(); }
/* The game's hold on the sound (b576): while it holds it, it runs above the reader - so the reader can never keep it
   from letting go while the mixer waits (a real PS2: the mixer stuck behind the game, the sound chip replaying its
   last piece over and over). Only the game's own task calls these. */
void platform_audio_lock(void)
{
    if (audioLock < 0) return;
    if (gamePriority >= 0) ChangeThreadPriority(GetThreadId(), gamePriority - 2);
    WaitSema(audioLock);
}
void platform_audio_unlock(void)
{
    if (audioLock < 0) return;
    SignalSema(audioLock);
    if (gamePriority >= 0) ChangeThreadPriority(GetThreadId(), gamePriority);
}

/* ---------------- The controller in port 1, read straight from the PS2's pad library ----------------
   Not through SDL: SDL loads the controller driver a second time (we load it in ps2_boot), fails, and then sees no
   controller at all ("0 controllers seen" - b568, nothing answered). libpad is how PS2 games read theirs. */
#include <libpad.h>
#include <timer.h>
static char padArea[256] __attribute__((aligned(64)));
static int padOpened;
static unsigned padAskedAt; /* when the sticks were last asked for (ms), 0 never */

static unsigned pad_ms(void) { unsigned t = (unsigned)(GetTimerSystemTime() / (kBUSCLK / 1000)); return t ? t : 1; }

/* The buttons held (PAD_CROSS... set when held) and the sticks (0..255, 128 the middle); 0 while there's no controller,
   or while what it sends can't be trusted (b576: a real PS2's controller, found again, sent "every button held" for a moment -
   Triangle = the inventory opened by itself - and came back with its sticks off). */
int ps2_pad_read(unsigned *buttons, int *lx, int *ly, int *rx, int *ry)
{
    if (!padOpened)
    {
        static unsigned lastTry;
        unsigned now = pad_ms();
        if (lastTry && now - lastTry < 1000) return 0;
        lastTry = now;
        if (padPortOpen(0, 0, padArea) == 0) return 0;
        padOpened = 1;
        printf("Game: Controller: port 1 opened\n");
    }
    /* STABLE, or FINDCTP1 (read fine, the controller's modes not asked yet - PS2SDK's own pad sample reads in both); never
       while it's being found or changing mode (FINDPAD, EXECCMD, ERROR). b578: b.ok is NOT a "the read is good" flag - it's
       the controller's first answer byte (0xFF on a real PS2): b577 refused every read for it and the controller was dead. */
    int state = padGetState(0, 0);
    if (state != PAD_STATE_STABLE && state != PAD_STATE_FINDCTP1) return 0;
    struct padButtonStatus b;
    if (padRead(0, 0, &b) == 0) return 0;
    int analog = (b.mode >> 4) == 7;
    /* The sticks on (a DualShock 2), locked so the Analog button can't turn them off - and asked again whenever they're found
       off (a controller found again starts with them off), once a second at most, and only once the last ask has finished. */
    if (!analog)
    {
        unsigned now = pad_ms();
        if ((!padAskedAt || now - padAskedAt >= 1000) && padGetReqState(0, 0) != PAD_RSTAT_BUSY && padInfoMode(0, 0, PAD_MODETABLE, -1) > 0)
        {
            padAskedAt = now;
            padSetMainMode(0, 0, PAD_MMODE_DUALSHOCK, PAD_MMODE_LOCK);
            printf("Game: Controller: sticks asked on\n");
        }
    }
    unsigned held = 0xFFFFu ^ b.btns; /* the PS2 sends 0 for a button held */
    if (held == 0xFFFFu) return 0;     /* every button at once: not a hand - a controller between modes */
    *buttons = held;
    *lx = analog ? b.ljoy_h : 128; *ly = analog ? b.ljoy_v : 128;
    *rx = analog ? b.rjoy_h : 128; *ry = analog ? b.rjoy_v : 128;
    return 1;
}

/* Microseconds since start-up (the EE's bus-clock timer). */
#include <timer.h>
unsigned long ps2_micros(void) { return (unsigned long)(GetTimerSystemTime() / (kBUSCLK / 1000000)); }

/* The PS2 has 32 MB: running out is told (PCSX2's log says who asked, and for how much) - never a silent write to nothing.
   build_ps2.sh links these in place of malloc / calloc / realloc / memset (-Wl,--wrap=...). */
#include <stdlib.h>
#include <malloc.h>
#include <debug.h>
/* What the game has taken from the heap so far, in KB (with the program itself, 32 MB in all). */
long ps2_memory_used_kb(void) { struct mallinfo m = mallinfo(); return (long)(m.uordblks / 1024); }
static int ranOut; /* memory ran out at some point: what a stop then says */

/* The game can't go on: the reason on the TV (PS2SDK's text screen), and it waits there - never a black screen. */
void ps2_stop_screen(const char *heading, const char *text)
{
    printf("Game: STOPPED - %s: %s\n", heading, text);
    init_scr();
    scr_setbgcolor(0x401000); /* dark blue */
    scr_setfontcolor(0xffffff);
    scr_clear();
    scr_setXY(2, 3);
    scr_printf("  %s\n\n", heading);
    if (ranOut)
        scr_printf("  This game needs more memory than the PlayStation 2 has\n  (32 MB in all, about 24 MB of it for the game).\n\n"
                   "  In the editor, make the game smaller - fewer or smaller\n  maps, sprites, pictures and sounds - and build it again.\n\n");
    for (const char *line = text; *line; )
    {
        int n = (int)strcspn(line, "\n");
        if (n > 58) n = 58; /* the screen's width */
        scr_printf("  %.*s\n", n, line);
        line += n;
        if (*line == '\n') line++;
    }
    scr_printf("\n\n  Switch the PlayStation 2 off, or press its Reset button.\n");
    for (;;) SleepThread();
}

void *__real_malloc(size_t n); void *__real_calloc(size_t n, size_t size); void *__real_realloc(void *p, size_t n); void *__real_memset(void *d, int c, size_t n);
static void out_of_memory(const char *what, size_t n, void *caller) { ranOut = 1; printf("Game: OUT OF MEMORY - %s of %lu bytes, asked at %p\n", what, (unsigned long)n, caller); }
void *__wrap_malloc(size_t n) { void *p = __real_malloc(n); if (!p && n) out_of_memory("malloc", n, __builtin_return_address(0)); return p; }
void *__wrap_calloc(size_t n, size_t size) { void *p = __real_calloc(n, size); if (!p && n && size) out_of_memory("calloc", n * size, __builtin_return_address(0)); return p; }
void *__wrap_realloc(void *old, size_t n) { void *p = __real_realloc(old, n); if (!p && n) out_of_memory("realloc", n, __builtin_return_address(0)); return p; }
void *__wrap_memset(void *d, int c, size_t n)
{
    if (!d && n)
    {
        /* memory that was never had, about to be written: on a real PS2 that is a crash - stopped here with the reason instead */
        char where[80];
        printf("Game: memset of %lu bytes on NULL, at %p\n", (unsigned long)n, __builtin_return_address(0));
        snprintf(where, sizeof where, "(%lu bytes at %p)", (unsigned long)n, __builtin_return_address(0));
        ranOut = 1;
        ps2_stop_screen("Out of memory", where);
    }
    return __real_memset(d, c, n);
}
#endif
