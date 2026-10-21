/*
 * The platform layer for Amiga 68k: AmigaOS 3.x with an RTG card, or
 * ApolloOS on a Vampire (not AROS). No custom chipset is used for the
 * picture: it is drawn through cybergraphics.library (Picasso96 provides it
 * too) - in a window on an RTG Workbench, or full screen on an RTG screen of
 * its own (F10 switches). The 320 x 200 frame is doubled to 640 x 400.
 * Keys and mouse through Intuition; time through timer.device.
 * Kickstart 3.1 (V40) at least: its libraries and devices are what's used.
 * Build: m68k-amigaos-gcc -noixemul -m68060 (runs on the 68080 too).
 */
#include "platform.h"
#include <exec/exec.h>
#include <intuition/intuition.h>
#include <graphics/gfx.h>
#include <graphics/modeid.h>
#include <devices/timer.h>
#include <cybergraphx/cybergraphics.h>
#include <proto/exec.h>
#include <proto/intuition.h>
#include <proto/graphics.h>
#include <proto/cybergraphics.h>
#include <proto/timer.h>
#include <proto/dos.h>
#include <string.h>
#include <stdio.h>

#define SCALE 2
#define KICKSTART_31 40 /* Kickstart 3.1: version 40 of its libraries */

struct IntuitionBase *IntuitionBase;
struct GfxBase *GfxBase;
struct Library *CyberGfxBase;
struct Device *TimerBase;

static struct Screen *screen;   /* our own RTG screen when full screen; NULL in a window on the Workbench */
static struct Window *window;
static struct timerequest *timerRequest;
static struct MsgPort *timerPort;
static struct EClockVal startClock;
static ULONG eclockRate;
static ULONG *scaled;           /* the frame doubled: 640 x 400, 0xAARRGGBB */
static int fullscreen;
static char title[128];
static char error_text[256];

const char *platform_error(void) { return error_text; }

static int open_display(void)
{
    if (fullscreen)
    {
        ULONG mode = BestCModeIDTags(CYBRBIDTG_NominalWidth, SCREEN_W * SCALE, CYBRBIDTG_NominalHeight, SCREEN_H * SCALE, CYBRBIDTG_Depth, 16, TAG_DONE);
        if (mode != INVALID_ID)
            screen = OpenScreenTags(NULL, SA_DisplayID, mode, SA_Width, SCREEN_W * SCALE, SA_Height, SCREEN_H * SCALE, SA_Depth, 16,
                                    SA_Title, (ULONG)title, SA_ShowTitle, FALSE, SA_Quiet, TRUE, SA_Type, CUSTOMSCREEN, TAG_DONE);
        if (!screen) fullscreen = 0; /* no RTG mode that size: a window on the Workbench instead */
    }
    if (screen)
        window = OpenWindowTags(NULL, WA_CustomScreen, (ULONG)screen, WA_Left, 0, WA_Top, 0, WA_Width, SCREEN_W * SCALE, WA_Height, SCREEN_H * SCALE,
                                WA_Borderless, TRUE, WA_Backdrop, TRUE, WA_Activate, TRUE, WA_RMBTrap, TRUE, WA_ReportMouse, TRUE,
                                WA_IDCMP, IDCMP_RAWKEY | IDCMP_MOUSEBUTTONS | IDCMP_MOUSEMOVE, TAG_DONE);
    else
        window = OpenWindowTags(NULL, WA_Title, (ULONG)title, WA_InnerWidth, SCREEN_W * SCALE, WA_InnerHeight, SCREEN_H * SCALE,
                                WA_DragBar, TRUE, WA_DepthGadget, TRUE, WA_CloseGadget, TRUE, WA_Activate, TRUE, WA_RMBTrap, TRUE, WA_ReportMouse, TRUE,
                                WA_IDCMP, IDCMP_CLOSEWINDOW | IDCMP_RAWKEY | IDCMP_MOUSEBUTTONS | IDCMP_MOUSEMOVE, TAG_DONE);
    if (!window) { snprintf(error_text, sizeof error_text, "The game's window couldn't open (an RTG Workbench in 15, 16, 24 or 32 bits is needed)."); return 0; }
    return 1;
}

static void close_display(void)
{
    if (window) { CloseWindow(window); window = NULL; }
    if (screen) { CloseScreen(screen); screen = NULL; }
}

int platform_open(const char *gameTitle, int wantFullscreen)
{
    snprintf(title, sizeof title, "%s", gameTitle);
    IntuitionBase = (struct IntuitionBase *)OpenLibrary((CONST_STRPTR)"intuition.library", KICKSTART_31);
    GfxBase = (struct GfxBase *)OpenLibrary((CONST_STRPTR)"graphics.library", KICKSTART_31);
    CyberGfxBase = OpenLibrary((CONST_STRPTR)"cybergraphics.library", 41);
    if (!IntuitionBase || !GfxBase) { snprintf(error_text, sizeof error_text, "Kickstart 3.1 or later is needed."); return 0; }
    if (!CyberGfxBase) { snprintf(error_text, sizeof error_text, "cybergraphics.library 41 or later is needed: an RTG graphics card (Picasso96 or CyberGraphX) or a Vampire."); return 0; }
    timerPort = CreateMsgPort();
    timerRequest = timerPort ? (struct timerequest *)CreateIORequest(timerPort, sizeof(struct timerequest)) : NULL;
    if (!timerRequest || OpenDevice((CONST_STRPTR)TIMERNAME, UNIT_ECLOCK, (struct IORequest *)timerRequest, 0) != 0) { snprintf(error_text, sizeof error_text, "timer.device couldn't open."); return 0; }
    TimerBase = timerRequest->tr_node.io_Device;
    eclockRate = ReadEClock(&startClock);
    scaled = (ULONG *)AllocVec(SCREEN_W * SCALE * SCREEN_H * SCALE * 4, MEMF_ANY);
    if (!scaled) { snprintf(error_text, sizeof error_text, "Not enough memory for the picture."); return 0; }
    fullscreen = wantFullscreen;
    return open_display();
}

void platform_close(void)
{
    close_display();
    if (scaled) FreeVec(scaled);
    if (TimerBase) CloseDevice((struct IORequest *)timerRequest);
    if (timerRequest) DeleteIORequest((struct IORequest *)timerRequest);
    if (timerPort) DeleteMsgPort(timerPort);
    if (CyberGfxBase) CloseLibrary(CyberGfxBase);
    if (GfxBase) CloseLibrary((struct Library *)GfxBase);
    if (IntuitionBase) CloseLibrary((struct Library *)IntuitionBase);
}

static enum PlatformKey key_of(UWORD code)
{
    switch (code & 0x7F)
    {
        case 0x4F: return KEY_LEFT;
        case 0x4E: return KEY_RIGHT;
        case 0x4C: return KEY_UP;
        case 0x4D: return KEY_DOWN;
        case 0x45: return KEY_ESCAPE;
        case 0x44: case 0x43: return KEY_ENTER;
        case 0x40: return KEY_SPACE;
        case 0x59: return KEY_F11; /* F10: window <-> full screen (Amiga keyboards have no F11) */
        default: return KEY_OTHER;
    }
}

int platform_poll(PlatformEvent *event)
{
    struct IntuiMessage *m;
    while (window && (m = (struct IntuiMessage *)GetMsg(window->UserPort)))
    {
        ULONG cls = m->Class;
        UWORD code = m->Code;
        int mx = m->MouseX - (screen ? 0 : window->BorderLeft), my = m->MouseY - (screen ? 0 : window->BorderTop);
        ReplyMsg((struct Message *)m);
        event->type = EV_NONE;
        event->mouseX = mx / SCALE;
        event->mouseY = my / SCALE;
        switch (cls)
        {
            case IDCMP_CLOSEWINDOW: event->type = EV_QUIT; return 1;
            case IDCMP_RAWKEY:
                if ((code & 0x7F) >= 0x68) break; /* not a key */
                event->type = (code & IECODE_UP_PREFIX) ? EV_KEY_UP : EV_KEY_DOWN;
                event->key = key_of(code);
                return 1;
            case IDCMP_MOUSEBUTTONS:
                if (code == SELECTDOWN || code == MENUDOWN) { event->type = EV_MOUSE_DOWN; event->button = code == MENUDOWN ? 2 : 1; return 1; }
                if (code == SELECTUP || code == MENUUP) { event->type = EV_MOUSE_UP; event->button = code == MENUUP ? 2 : 1; return 1; }
                break;
            case IDCMP_MOUSEMOVE: event->type = EV_MOUSE_MOVE; return 1;
        }
    }
    return 0;
}

void platform_present(const uint32_t *pixels)
{
    if (!window) return;
    /* Doubled, a pixel to a 2 x 2 block (no blurring, as the other platforms). */
    const int w = SCREEN_W * SCALE;
    for (int y = 0; y < SCREEN_H; y++)
    {
        ULONG *a = scaled + (y * SCALE) * w, *b = a + w;
        const uint32_t *src = pixels + y * SCREEN_W;
        for (int x = 0; x < SCREEN_W; x++) { ULONG c = src[x]; a[2 * x] = a[2 * x + 1] = c; }
        memcpy(b, a, w * 4);
    }
    WaitTOF();
    WritePixelArray(scaled, 0, 0, w * 4, window->RPort, screen ? 0 : window->BorderLeft, screen ? 0 : window->BorderTop, w, SCREEN_H * SCALE, RECTFMT_ARGB);
}

uint32_t platform_ticks(void)
{
    struct EClockVal now;
    ReadEClock(&now);
    unsigned long long a = ((unsigned long long)startClock.ev_hi << 32) | startClock.ev_lo;
    unsigned long long b = ((unsigned long long)now.ev_hi << 32) | now.ev_lo;
    return (uint32_t)((b - a) * 1000ULL / (eclockRate ? eclockRate : 1));
}

void platform_sleep(uint32_t ms) { if (ms >= 20) Delay(ms / 20); }

void platform_toggle_fullscreen(void)
{
    close_display();
    fullscreen = !fullscreen;
    if (!open_display()) { fullscreen = !fullscreen; open_display(); }
}

/* A 68060 (or a Vampire's 68080) with its FPU: the game is built for it and uses floating point throughout. */
int platform_can_run(void)
{
    if (SysBase->LibNode.lib_Version < KICKSTART_31) { snprintf(error_text, sizeof error_text, "This game needs Kickstart 3.1 or later - this Amiga has an older one."); return 0; }
    UWORD flags = SysBase->AttnFlags;
    if (!(flags & AFF_68040)) { snprintf(error_text, sizeof error_text, "This game needs a 68060 or a 68080 (Vampire) - this Amiga's processor is older."); return 0; }
    if (!(flags & (AFF_68881 | AFF_FPU40))) { snprintf(error_text, sizeof error_text, "This game needs a floating-point unit (FPU): a full 68060, or a Vampire with an FPU core. This processor has none."); return 0; }
    return 1;
}

long platform_free_memory_mb(void) { return (long)(AvailMem(MEMF_ANY | MEMF_LARGEST) / (1024 * 1024)); }

/* The Amiga's own requester, for a game that can't start (not enough RAM, no RTG). */
void platform_message(const char *heading, const char *text)
{
    struct EasyStruct es = { sizeof(struct EasyStruct), 0, (UBYTE *)heading, (UBYTE *)text, (UBYTE *)"OK" };
    if (!IntuitionBase) IntuitionBase = (struct IntuitionBase *)OpenLibrary((CONST_STRPTR)"intuition.library", 36); /* any that can show a requester - even to say the Kickstart is too old */
    if (IntuitionBase) EasyRequestArgs(NULL, &es, NULL, NULL);
}
