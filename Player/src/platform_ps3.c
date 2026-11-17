/*
 * The PlayStation 3's own part of the player (homebrew under HEN or a custom firmware, built with PSL1GHT): everything else is
 * platform_sdl2.c, as on every SDL machine.
 *  - The game is installed by its PKG (Package Manager > Install Package Files; a PKG installed from the hard drive's packages
 *    folder is deleted once installed): /dev_hdd0/game/<title id>/USRDIR holds EBOOT.BIN and Game_Data; the saves go beside
 *    them in USRDIR/Saves (save.c's own rule).
 *  - The controller in port 1 is the keyboard and mouse, as on the PS2 (platform_sdl2.c: the same buttons do the same).
 *  - The PS button's Quit Game: the game ends at once.
 */
#ifdef PLATFORM_PS3
#include "platform.h"
#include "font.h"
#include <io/pad.h>
#include <sysutil/sysutil.h>
#include <sys/thread.h>
#include <sys/systime.h>
#include <sys/process.h>
#include <rsx/rsx.h>
#include <rsx/gcm_sys.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

/* The game runs on a thread of its own with a 1 MB stack: the PS3's main thread has far less than a computer's 8 MB (the
   process is left as PSL1GHT starts it). main.c's main is game_main here (build_ps3.sh), as on the Amiga. */
int game_main(int argc, char **argv);
static int gameArgc, gameResult;
static char **gameArgv;
static void game_thread(void *arg) { (void)arg; gameResult = game_main(gameArgc, gameArgv); sysThreadExit(0); }

#ifdef SANDCASTLE_NETLOG
/* Testing only (build_ps3.sh's NETLOG=1; off in a finished build): with /dev_hdd0/tmp/sandcastle_netlog.txt holding a computer's
   address ("192.168.1.20 5555"), each line of the game's log goes to it at once over the network (UDP), so a game that freezes
   has already said how far it got. No such file: nothing is sent. */
#include <net/net.h>
#include <sys/socket.h>
#include <netinet/in.h>
#include <arpa/inet.h>
static int netlogSocket = -1;
static struct sockaddr_in netlogTo;
static void netlog_open(void)
{
    char ip[64] = ""; int port = 5555;
    FILE *f = fopen("/dev_hdd0/tmp/sandcastle_netlog.txt", "r");
    if (!f) return;
    int got = fscanf(f, "%63s %d", ip, &port);
    fclose(f);
    if (got < 1 || netInitialize() != 0) return;
    netlogSocket = socket(AF_INET, SOCK_DGRAM, 0);
    memset(&netlogTo, 0, sizeof netlogTo);
    netlogTo.sin_family = AF_INET;
    netlogTo.sin_port = htons((unsigned short)port);
    netlogTo.sin_addr.s_addr = inet_addr(ip);
}
void ps3_netlog(const char *line)
{
    if (netlogSocket < 0) return;
    char text[1200];
    int n = snprintf(text, sizeof text, "%u %s\n", (unsigned)platform_ticks(), line);
    if (n > (int)sizeof text - 1) n = (int)sizeof text - 1;
    sendto(netlogSocket, text, (size_t)n, 0, (const struct sockaddr *)&netlogTo, sizeof netlogTo);
}
/* The watchdog: once a second, how far the game is (main.c's frame and its last finished part, events.c's and script.c's last
   command), from a thread of its own - so a game frozen in a loop or a fault still says where it stopped. */
volatile int ps3dbg_stage = -1, ps3dbg_frame, ps3dbg_event = -1, ps3dbg_script = -1, ps3dbg_sound, ps3dbg_fills, ps3dbg_infill;
volatile int ps3dbg_mixstep, ps3dbg_mixiter, ps3dbg_mixv[8]; /* sound.c's mixer: 1 decoding, 2 closing; its loop count; the voice's numbers */
static void watchdog(void *arg)
{
    (void)arg;
    static const char *PARTS[] = { "input", "events", "update", "world", "light", "weather", "barks", "windows", "present" };
    static const char *STEPS[] = { "reading keys/pad/mouse", "after reading keys", "sound update", "screen update", "video update" }; /* main.c's DBG_STEP 9..13 */
    int lastFrame = -1, still = 0;
    for (;;)
    {
        sysUsleep(1000000);
        int frame = ps3dbg_frame, stage = ps3dbg_stage;
        still = frame == lastFrame ? still + 1 : 0;
        lastFrame = frame;
        char line[320];
        /* the graphics chip: how full its command buffer is (bytes from its start), and how far the chip has read (get) of what it
           was given (put); flip status 0 = the last flip done */
        long used = -1, size = -1;
        if (gGcmContext && gGcmContext->begin) { used = (long)((char *)gGcmContext->current - (char *)gGcmContext->begin); size = (long)((char *)gGcmContext->end - (char *)gGcmContext->begin); }
        gcmControlRegister *ctrl = gcmGetControlRegister();
        char where[96];
        if (stage >= 9 && stage <= 13) snprintf(where, sizeof where, "in %s", STEPS[stage - 9]);
        else snprintf(where, sizeof where, "last part done %s (next: %s)", stage >= 0 && stage < 9 ? PARTS[stage] : "-", stage >= 0 && stage < 9 ? PARTS[(stage + 1) % 9] : "-");
        snprintf(line, sizeof line, "WATCH frame %d%s, %s, event command %d, script command %d | sound step %d, mixes %d, in mix %d | rsx buffer %ld of %ld, put %08X get %08X ref %08X, flip status %d",
                 frame, still ? " STUCK" : "", where, ps3dbg_event, ps3dbg_script, ps3dbg_sound, ps3dbg_fills, ps3dbg_infill,
                 used, size, ctrl ? (unsigned)ctrl->put : 0, ctrl ? (unsigned)ctrl->get : 0, ctrl ? (unsigned)ctrl->ref : 0, (int)gcmGetFlipStatus());
        ps3_netlog(line);
        if (ps3dbg_infill)
        {
            snprintf(line, sizeof line, "WATCH mixer: step %d (1 decoding, 2 closing), loop count %d, frac %d step %d at %d buffered %d rate %d kind %d, frame %d of %d",
                     ps3dbg_mixstep, ps3dbg_mixiter, ps3dbg_mixv[0], ps3dbg_mixv[1], ps3dbg_mixv[2], ps3dbg_mixv[3], ps3dbg_mixv[4], ps3dbg_mixv[5], ps3dbg_mixv[6], ps3dbg_mixv[7]);
            ps3_netlog(line);
        }
        if (still >= 8) { ps3_netlog("WATCH: stuck 8 seconds - the game ends (back to the XMB)"); sysProcessExit(1); } /* testing: no power button needed */
    }
}
static void watchdog_start(void)
{
    if (netlogSocket < 0) return;
    sys_ppu_thread_t t;
    sysThreadCreate(&t, watchdog, NULL, 1000, 0x10000, 0, "watchdog");
}
#else
static void watchdog_start(void) { }
static void netlog_open(void) { }
void ps3_netlog(const char *line) { (void)line; }
#endif

static void boot_mark(const char *line)
{
    FILE *f = fopen("/dev_hdd0/tmp/sandcastle_boot.txt", "a");
    if (f) { fprintf(f, "%s\n", line); fclose(f); }
    ps3_netlog(line);
}

int main(int argc, char **argv)
{
    remove("/dev_hdd0/tmp/sandcastle_boot.txt");
    netlog_open();
    watchdog_start();
    boot_mark("PS3: started");
    gameArgc = argc; gameArgv = argv;
    sys_ppu_thread_t thread;
    if (sysThreadCreate(&thread, game_thread, NULL, 1001, 0x100000, THREAD_JOINABLE, "game") != 0)
    {
        boot_mark("PS3: no game thread - run as it is");
        return game_main(argc, argv);
    }
    boot_mark("PS3: game thread started (1 MB stack)");
    u64 done;
    sysThreadJoin(thread, &done);
    return gameResult;
}

static int quitAsked;

/* The XMB's word: Quit Game from the PS button's menu. */
static void sysutil_callback(u64 status, u64 param, void *user)
{
    (void)param; (void)user;
    if (status == SYSUTIL_EXIT_GAME) { quitAsked = 1; exit(0); }
}

/* Where the game is: beside the EBOOT (argv[0] is ".../USRDIR/EBOOT.BIN"). */
void ps3_boot(int argc, char **argv, char *folder, int folderSize)
{
    sysUtilRegisterCallback(SYSUTIL_EVENT_SLOT0, sysutil_callback, NULL);
    ioPadInit(7);
    const char *self = argc > 0 && argv && argv[0] ? argv[0] : "";
    const char *slash = strrchr(self, '/');
    if (slash) snprintf(folder, (size_t)folderSize, "%.*s/Game_Data", (int)(slash - self), self);
    else snprintf(folder, (size_t)folderSize, "Game_Data");
}

/* The graphics chip's command buffer, back to its start after each picture once it has used 8 KB: left to the system it moves on
   segment by segment and, at the end of SDL's 64 KB (the 256th picture), out past it - the flip there never happened and the game
   froze. After SDL's present the chip has done all it was given (get == put), so a jump to the start is safe: written where the
   buffer is now, put set to the start, then wait for the chip to take the jump. The system's own wrap is never reached. */
void ps3_rsx_rewind(void)
{
    static u32 *start;
    static u32 startOffset;
    gcmContextData *c = gGcmContext;
    if (!c || !c->begin) return;
    if (!start) { start = c->begin; if (rsxAddressToOffset(start, &startOffset) != 0) { start = NULL; return; } }
    if ((char *)c->current - (char *)start < 8192 || c->current < start || c->current >= c->end) return;
    gcmControlRegister *ctrl = gcmGetControlRegister();
    rsxSetJumpCommand(c, startOffset);
    __asm__ __volatile__("sync" ::: "memory");
    ctrl->put = startOffset;
    for (int i = 0; ctrl->get != startOffset && i < 200000; i++) sysUsleep(10);
    c->current = start;
}

/* ---------------- The CRT effect on the SPEs (System Settings > Game Window > CRT effect) ----------------
   Six SPE threads (ps3spu/crt_spu.c, built into crt_spu_bin.h by build_ps3.sh) wait for each frame; each makes a band of the
   doubled CRT picture straight into the texture. The main processor only hands out the bands and waits for them. If the SPEs
   can't be started, 0 is returned and crt.c does it on the main processor as before. */
#include <sys/spu.h>
#include <malloc.h>
#include <stdint.h>
#include "ps3spu/crt_job.h"
#include "crt_spu_bin.h"
#define CRT_SPES 6
static int spuState; /* 0 not tried, 1 running, -1 not available */
static sysSpuImage spuImage;
static u32 spuGroup, spuThread[CRT_SPES], spuFrame;
static CrtJob *spuJobs;

static int crt_spu_start(void)
{
    sysSpuThreadAttribute attr = { "crt", 4, SPU_THREAD_ATTR_NONE };
    sysSpuThreadGroupAttribute group = { 9, "crtgroup", 0, { 0 } };
    sysSpuThreadArgument arg[CRT_SPES];
    spuJobs = (CrtJob *)memalign(128, sizeof(CrtJob) * CRT_SPES);
    if (!spuJobs) return 0;
    memset(spuJobs, 0, sizeof(CrtJob) * CRT_SPES);
    sysSpuInitialize(CRT_SPES, 0); /* (an error here - already done - is fine: the group below says if they're there) */
    if (sysSpuImageImport(&spuImage, crt_spu_bin, 0) != 0) { ps3_netlog("CRT: SPE program not loaded - the main processor does it"); return 0; }
    if (sysSpuThreadGroupCreate(&spuGroup, CRT_SPES, 100, &group) != 0) { ps3_netlog("CRT: no SPE group - the main processor does it"); return 0; }
    for (int i = 0; i < CRT_SPES; i++)
    {
        memset(&arg[i], 0, sizeof arg[i]);
        arg[i].arg0 = (u64)(uintptr_t)&spuJobs[i];
        if (sysSpuThreadInitialize(&spuThread[i], spuGroup, i, &spuImage, &attr, &arg[i]) != 0) { ps3_netlog("CRT: an SPE thread failed - the main processor does it"); return 0; }
        sysSpuThreadSetConfiguration(spuThread[i], SPU_SIGNAL1_OVERWRITE | SPU_SIGNAL2_OVERWRITE);
    }
    if (sysSpuThreadGroupStart(spuGroup) != 0) { ps3_netlog("CRT: the SPE group didn't start - the main processor does it"); return 0; }
    ps3_netlog("CRT: on the SPEs (6)");
    return 1;
}

/* The doubled CRT picture of in (w x h, main memory) into out (the texture: 2w x 2h, outPitch pixels a row) by the SPEs:
   1 when done, 0 when they can't (the caller does it itself). */
int ps3_crt_spu(const uint32_t *in, int w, int h, uint32_t *out, int outPitch)
{
    if (spuState == 0) spuState = crt_spu_start() ? 1 : -1;
    if (spuState < 0 || w > 1024 || ((uintptr_t)in & 15) || ((uintptr_t)out & 15)) return 0;
    spuFrame = spuFrame + 1 == 0xFFFFFFFFu ? 1 : spuFrame + 1;
    for (int i = 0; i < CRT_SPES; i++)
    {
        CrtJob *j = &spuJobs[i];
        j->inEa = (u64)(uintptr_t)in; j->outEa = (u64)(uintptr_t)out;
        j->w = (uint32_t)w; j->inPitch = (uint32_t)w * 4; j->outPitch = (uint32_t)outPitch * 4;
        j->y0 = (uint32_t)(h * i / CRT_SPES); j->y1 = (uint32_t)(h * (i + 1) / CRT_SPES);
    }
    __asm__ __volatile__("sync" ::: "memory"); /* the jobs written before the SPEs are woken */
    for (int i = 0; i < CRT_SPES; i++) sysSpuThreadWriteSignal(spuThread[i], 0, spuFrame);
    for (int i = 0; i < CRT_SPES; i++)
        for (int spin = 0; ((volatile CrtJob *)&spuJobs[i])->done != spuFrame; spin++)
        {
            if (spin > 200000) { ps3_netlog("CRT: the SPEs didn't answer - the main processor does it from now on"); spuState = -1; return 0; } /* ~2 s */
            sysUsleep(10);
        }
    __asm__ __volatile__("sync" ::: "memory");
    return 1;
}

/* The controller in port 1: its buttons as the PS2's bits (platform_sdl2.c), its sticks 0..255. 0: none connected. */
int console_pad_read(unsigned *buttons, int *lx, int *ly, int *rx, int *ry)
{
    static padData held;
    static int known;
    padInfo info;
    sysUtilCheckCallback(); /* the XMB's words (Quit Game) - every frame, as the controller is read */
    if (ioPadGetInfo(&info) != 0 || !info.status[0]) return 0;
    padData now;
    memset(&now, 0, sizeof now);
    if (ioPadGetData(0, &now) == 0 && now.len > 0) { held = now; known = 1; } /* nothing new: as it was */
    if (!known) { *buttons = 0; *lx = *ly = *rx = *ry = 128; return 1; }
    unsigned b = 0;
    if (held.BTN_SELECT) b |= 0x0001; if (held.BTN_L3) b |= 0x0002; if (held.BTN_R3) b |= 0x0004; if (held.BTN_START) b |= 0x0008;
    if (held.BTN_UP) b |= 0x0010; if (held.BTN_RIGHT) b |= 0x0020; if (held.BTN_DOWN) b |= 0x0040; if (held.BTN_LEFT) b |= 0x0080;
    if (held.BTN_L2) b |= 0x0100; if (held.BTN_R2) b |= 0x0200; if (held.BTN_L1) b |= 0x0400; if (held.BTN_R1) b |= 0x0800;
    if (held.BTN_TRIANGLE) b |= 0x1000; if (held.BTN_CIRCLE) b |= 0x2000; if (held.BTN_CROSS) b |= 0x4000; if (held.BTN_SQUARE) b |= 0x8000;
    *buttons = b;
    *lx = held.ANA_L_H; *ly = held.ANA_L_V; *rx = held.ANA_R_H; *ry = held.ANA_R_V;
    return 1;
}

/* No message boxes on a TV: the reason on the screen (the game's built-in letters), till the PS button's Quit Game. */
void console_stop_screen(const char *heading, const char *text)
{
    FILE *log = fopen("/dev_hdd0/tmp/sandcastle_stop.txt", "w");
    if (log) { fprintf(log, "%s\n%s\n", heading, text); fclose(log); }
    uint32_t *pixels = malloc(sizeof(uint32_t) * FRAME_PIXELS); /* the frame's size is set as the player starts */
    if (!pixels) for (;;) { sysUtilCheckCallback(); platform_sleep(50); }
    const Font *f = font_get("");
    for (;;)
    {
        for (int i = 0; i < FRAME_PIXELS; i++) pixels[i] = 0xFF001040u; /* dark blue */
        font_draw(f, pixels, 8, 12, heading, 0xFFFFFFFFu);
        font_draw_block(f, pixels, 8, 32, text, SCREEN_W - 16, 0xFFFFFFFFu);
        font_draw(f, pixels, 8, SCREEN_H - 16, "Quit with the PS button's Quit Game.", 0xFFC0C0C0u);
        platform_present(pixels);
        sysUtilCheckCallback();
        platform_sleep(50);
    }
}
#endif
