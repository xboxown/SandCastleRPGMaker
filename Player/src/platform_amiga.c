/*
 * The platform layer for Amiga 68k: AmigaOS 3.x with an RTG card, or
 * ApolloOS on a Vampire (not AROS). No custom chipset is used for the
 * picture: it is drawn through cybergraphics.library (Picasso96 provides it
 * too) - in a window on an RTG Workbench, or full screen on an RTG screen of
 * its own (F10 switches). The 320 x 200 frame is doubled to 640 x 400.
 * Keys and mouse through Intuition; time through timer.device.
 * Kickstart 3.1 (V40) at least: its libraries and devices are what's used.
 * Build: m68k-amigaos-gcc -noixemul -m68060 (runs on the 68080 too).
 *
 * MorphOS (PowerPC: Pegasos, PowerPC Macs, Efika) uses this same layer - it
 * has Intuition, cybergraphics.library, timer.device and keymap.library of
 * its own; only the 68k processor checks and chip memory are left out.
 * Build: ppc-morphos-gcc -noixemul (build_morphos.sh).
 */
#include "platform.h"
#include "crt.h"
#include <exec/exec.h>
#include <intuition/intuition.h>
#include <graphics/gfx.h>
#include <graphics/clip.h>
#include <graphics/layers.h>
#include <proto/layers.h>
#include <graphics/modeid.h>
#include <devices/timer.h>
#include <cybergraphx/cybergraphics.h>
#include <proto/exec.h>
#include <proto/intuition.h>
#include <proto/graphics.h>
#include <proto/cybergraphics.h>
#include <proto/timer.h>
#include <proto/dos.h>
#include <proto/keymap.h>
#include <devices/inputevent.h>
#include <devices/input.h>
#include <intuition/pointerclass.h>
struct Library *KeymapBase;
static int pendingText; /* a letter typed, given as EV_TEXT after its key */
static UWORD heldQualifier; /* the qualifier keys with the last input event */
int platform_ctrl_held(void) { return (heldQualifier & IEQUALIFIER_CONTROL) != 0; }
#include <string.h>
#include <stdio.h>
#include <stdlib.h>
#ifdef VAMPIRE
static int saga_start(void); /* SAGA's display taken over (below, with the frames) */
static void saga_stop(void);
#endif

#define KICKSTART_31 40 /* Kickstart 3.1: version 40 of its libraries */
#ifndef RECTFMT_RAW
#define RECTFMT_RAW 5UL /* the display's own pixel format (not in every NDK's header) */
#endif

/* How the 320 x 200 frame is shown: doubled (640 x 400, a window or a screen that size) or as it is (a 320 x 200 / 320 x 240
   full screen), at (offX, offY) - a 240-line screen has it in the middle. */
static int scale = 2, offX, offY;
#ifdef FRAME_HIRES
/* MorphOS: the picture itself is fine enough (frame_scale x 320 x 200), shown pixel for pixel. Chosen once, as the display first opens. */
int frame_scale = 1;
#define MOST_FRAME_SCALE 4
#define BIGGEST_SHOW 1
static int frameChosen;
#else
#define BIGGEST_SHOW 2
#endif
static int wantWindowScale = 2;
void platform_want_window_scale(int s) { wantWindowScale = s; }
/* System Settings > Game Window > CRT effect (crt.c): the lines darkened in place, on a copy of each frame. */
static int crtOn;
void platform_want_crt(int on) { crtOn = on; }
/* System Settings > Game Window > Fill the screen: MorphOS full screen widens the game's screen in game pixels to the
   Workbench's size (the Amigas: always 320 x 200). */
static int wantFill;
void platform_want_fill(int on) { wantFill = on; }
#ifdef VIEW_FILL
int screen_w = BASE_W, screen_h = BASE_H;
#endif
/* The display's own pixel format: each frame is made in it (doubled as it goes) and handed over with no conversion left
   to do (RECTFMT_RAW). rawOk: 1 it works, 0 it doesn't (the frame given as ARGB, converted by the card's software), -1 not tried. */
static ULONG pixFmt;
static int rawOk = -1;
void game_log(const char *line);

struct IntuitionBase *IntuitionBase;
struct GfxBase *GfxBase;
struct Library *CyberGfxBase;
#ifdef __MORPHOS__
struct Library *TimerBase; /* MorphOS declares it as a library */
#else
struct Device *TimerBase;
#endif

static struct Screen *screen;   /* our own RTG screen when full screen; NULL in a window on the Workbench */
static struct Window *window;
static struct timerequest *timerRequest;
static struct MsgPort *timerPort;
static struct EClockVal startClock;
static ULONG eclockRate;
static ULONG *row;              /* one row in the display's format */
static ULONG *scaled;           /* the frame doubled: 640 x 400, 0xAARRGGBB */
static int fullscreen;
static char title[128];
static char error_text[256];

const char *platform_error(void) { return error_text; }

static UWORD *blankPointer; /* Intuition's pointer, blanked: the game draws its own */
#define BLANK_POINTER_BYTES ((2 + 16 * 2 + 2) * 2) /* its control words, 16 lines of two words, the end */

/* The mouse kept inside the game: while its window is active the pointer can't leave it (moved back with an
   IECLASS_POINTERPOS input event). F9 lets it go (and takes it again); the game closing lets it go. */
static struct MsgPort *inputPort;
static struct IOStdReq *inputRequest;
static int inputOpen, mouseLocked = 1, windowActive = 1;

static void hide_pointer(void)
{
#ifdef __MORPHOS__
    SetWindowPointer(window, WA_PointerType, POINTERTYPE_INVISIBLE, TAG_DONE);
#else
    /* A whole 16 x 16 sprite with every pixel see-through (SetPointer, which every Amiga system has): a 1-line one was
       ignored by some RTG drivers (the Vampire's), which went on showing the Workbench's pointer over the game. */
    if (!blankPointer) blankPointer = (UWORD *)AllocMem(BLANK_POINTER_BYTES, MEMF_CHIP | MEMF_CLEAR); /* in chip memory */
    if (blankPointer) SetPointer(window, blankPointer, 16, 16, 0, 0);
#endif
}

/* The pointer put at (x, y) in the window's own pixels. */
static void warp_mouse(int x, int y)
{
    if (!inputOpen || !window) return;
    struct InputEvent ie;
    memset(&ie, 0, sizeof ie);
    ie.ie_Class = IECLASS_POINTERPOS;
    ie.ie_X = x + window->LeftEdge + (screen ? offX : window->BorderLeft);
    ie.ie_Y = y + window->TopEdge + (screen ? offY : window->BorderTop);
    inputRequest->io_Command = IND_WRITEEVENT;
    inputRequest->io_Data = (APTR)&ie;
    inputRequest->io_Length = sizeof ie;
    DoIO((struct IORequest *)inputRequest);
}

static int open_display(void)
{
    scale = 2; offX = offY = 0;
    int sw = SCREEN_W * 2, sh = SCREEN_H * 2;
#ifdef FRAME_HIRES
    /* As fine as the display allows: the Workbench's own screen mode when full screen, the project's window size in a window. */
    scale = 1;
    int deskW = 1024, deskH = 768;
    struct Screen *desk = LockPubScreen(NULL);
    if (desk) { deskW = desk->Width; deskH = desk->Height; UnlockPubScreen(NULL, desk); }
    if (!frameChosen)
    {
        int fit = deskW / BASE_W < deskH / BASE_H ? deskW / BASE_W : deskH / BASE_H;
        int inWindow = (deskH - 40) / BASE_H < deskW / BASE_W ? (deskH - 40) / BASE_H : deskW / BASE_W; /* room for its title bar */
        frame_scale = fullscreen ? fit : (wantWindowScale < inWindow ? wantWindowScale : inWindow);
        if (frame_scale > MOST_FRAME_SCALE) frame_scale = MOST_FRAME_SCALE;
        if (frame_scale < 1) frame_scale = 1;
#ifdef VIEW_FILL
        if (wantFill && fullscreen) /* Fill the screen: the same whole scale, the game's screen the Workbench's size in game pixels */
        {
            screen_w = deskW / frame_scale > BASE_W ? deskW / frame_scale : BASE_W;
            screen_h = deskH / frame_scale > BASE_H ? deskH / frame_scale : BASE_H;
        }
#endif
        frameChosen = 1;
    }
    sw = FRAME_W; sh = FRAME_H;
#ifdef VAMPIRE
    /* The Vampire: always a screen of its own, SAGA's 16-bit 5-6-5, the game drawn at 640 x 400 (on 640 x 480: in the middle) */
    frame_scale = 2; sw = FRAME_W; sh = FRAME_H;
    {
        static const int TRY[][2] = { { 640, 400 }, { 640, 480 } };
        for (int t = 0; t < 2 && !screen; t++)
        {
            ULONG mode = BestCModeIDTags(CYBRBIDTG_NominalWidth, TRY[t][0], CYBRBIDTG_NominalHeight, TRY[t][1], CYBRBIDTG_Depth, 16, TAG_DONE);
            if (mode == INVALID_ID || !IsCyberModeID(mode)) continue;
            if ((int)GetCyberIDAttr(CYBRIDATTR_WIDTH, mode) != TRY[t][0] || (int)GetCyberIDAttr(CYBRIDATTR_HEIGHT, mode) != TRY[t][1]) continue;
            screen = OpenScreenTags(NULL, SA_DisplayID, mode, SA_Width, TRY[t][0], SA_Height, TRY[t][1], SA_Depth, 16,
                                    SA_Title, (ULONG)title, SA_ShowTitle, FALSE, SA_Quiet, TRUE, SA_Type, CUSTOMSCREEN, TAG_DONE);
            if (screen) { sw = TRY[t][0]; sh = TRY[t][1]; offX = (sw - FRAME_W) / 2; offY = (sh - FRAME_H) / 2; fullscreen = 1; }
        }
    }
    if (!screen) { snprintf(error_text, sizeof error_text, "No 640 x 400 or 640 x 480 16-bit screen: this build needs a Vampire with its SAGA driver (P96)."); return 0; }
#endif
    if (!screen && fullscreen && deskW >= FRAME_W && deskH >= FRAME_H)
    {
        ULONG mode = BestCModeIDTags(CYBRBIDTG_NominalWidth, deskW, CYBRBIDTG_NominalHeight, deskH, CYBRBIDTG_Depth, 32, TAG_DONE);
        if (mode == INVALID_ID) mode = BestCModeIDTags(CYBRBIDTG_NominalWidth, deskW, CYBRBIDTG_NominalHeight, deskH, CYBRBIDTG_Depth, 16, TAG_DONE);
        if (mode != INVALID_ID && IsCyberModeID(mode))
            screen = OpenScreenTags(NULL, SA_DisplayID, mode, SA_Width, deskW, SA_Height, deskH, SA_Depth, GetCyberIDAttr(CYBRIDATTR_DEPTH, mode),
                                    SA_Title, (ULONG)title, SA_ShowTitle, FALSE, SA_Quiet, TRUE, SA_Type, CUSTOMSCREEN, TAG_DONE);
        if (screen) { sw = deskW; sh = deskH; offX = (deskW - FRAME_W) / 2; offY = (deskH - FRAME_H) / 2; }
    }
    if (!screen) fullscreen = 0;
#elif defined(VAMPIRE)
    /* The Vampire (b615): the game drawn at 320 x 200 as on the Amiga 68k - a quarter of the pixels of 640 x 400, which ran at
       8-9 fps on a V4 Firebird - and doubled on its way to SAGA's own 16-bit 640 x 400 screen (640 x 480: in the middle). */
    {
        static const int TRY[][2] = { { 640, 400 }, { 640, 480 } };
        for (int t = 0; t < 2 && !screen; t++)
        {
            ULONG mode = BestCModeIDTags(CYBRBIDTG_NominalWidth, TRY[t][0], CYBRBIDTG_NominalHeight, TRY[t][1], CYBRBIDTG_Depth, 16, TAG_DONE);
            if (mode == INVALID_ID || !IsCyberModeID(mode)) continue;
            if ((int)GetCyberIDAttr(CYBRIDATTR_WIDTH, mode) != TRY[t][0] || (int)GetCyberIDAttr(CYBRIDATTR_HEIGHT, mode) != TRY[t][1]) continue;
            screen = OpenScreenTags(NULL, SA_DisplayID, mode, SA_Width, TRY[t][0], SA_Height, TRY[t][1], SA_Depth, 16,
                                    SA_Title, (ULONG)title, SA_ShowTitle, FALSE, SA_Quiet, TRUE, SA_Type, CUSTOMSCREEN, TAG_DONE);
            if (screen) { sw = TRY[t][0]; sh = TRY[t][1]; scale = 2; offX = (sw - SCREEN_W * 2) / 2; offY = (sh - SCREEN_H * 2) / 2; fullscreen = 1; }
        }
    }
    if (!screen) { snprintf(error_text, sizeof error_text, "No 640 x 400 or 640 x 480 16-bit screen: this build needs a Vampire with its SAGA driver (P96)."); return 0; }
#else
    if (fullscreen)
    {
        /* a 320 x 200 or 320 x 240 RTG mode: the frame needs no doubling (a quarter of the pixels to hand over); else 640 x 400 / 480 */
        static const int TRY[][3] = { { 320, 200, 1 }, { 320, 240, 1 }, { 640, 400, 2 }, { 640, 480, 2 } };
        for (int t = 0; t < 4 && !screen; t++)
        {
            ULONG mode = BestCModeIDTags(CYBRBIDTG_NominalWidth, TRY[t][0], CYBRBIDTG_NominalHeight, TRY[t][1], CYBRBIDTG_Depth, 16, TAG_DONE);
            if (mode == INVALID_ID || !IsCyberModeID(mode)) continue;
            if ((int)GetCyberIDAttr(CYBRIDATTR_WIDTH, mode) != TRY[t][0] || (int)GetCyberIDAttr(CYBRIDATTR_HEIGHT, mode) != TRY[t][1]) continue; /* only that size */
            screen = OpenScreenTags(NULL, SA_DisplayID, mode, SA_Width, TRY[t][0], SA_Height, TRY[t][1], SA_Depth, 16,
                                    SA_Title, (ULONG)title, SA_ShowTitle, FALSE, SA_Quiet, TRUE, SA_Type, CUSTOMSCREEN, TAG_DONE);
            if (screen) { sw = TRY[t][0]; sh = TRY[t][1]; scale = TRY[t][2]; offX = (sw - SCREEN_W * scale) / 2; offY = (sh - SCREEN_H * scale) / 2; }
        }
        if (!screen) fullscreen = 0; /* no RTG mode that size: a window on the Workbench instead */
    }
#endif
    if (screen)
        window = OpenWindowTags(NULL, WA_CustomScreen, (ULONG)screen, WA_Left, 0, WA_Top, 0, WA_Width, sw, WA_Height, sh,
                                WA_Borderless, TRUE, WA_Backdrop, TRUE, WA_Activate, TRUE, WA_RMBTrap, TRUE, WA_ReportMouse, TRUE,
                                WA_IDCMP, IDCMP_RAWKEY | IDCMP_MOUSEBUTTONS | IDCMP_MOUSEMOVE | IDCMP_ACTIVEWINDOW | IDCMP_INACTIVEWINDOW, TAG_DONE);
    else
        window = OpenWindowTags(NULL, WA_Title, (ULONG)title, WA_InnerWidth, sw, WA_InnerHeight, sh,
                                WA_DragBar, TRUE, WA_DepthGadget, TRUE, WA_CloseGadget, TRUE, WA_Activate, TRUE, WA_RMBTrap, TRUE, WA_ReportMouse, TRUE,
                                WA_IDCMP, IDCMP_CLOSEWINDOW | IDCMP_RAWKEY | IDCMP_MOUSEBUTTONS | IDCMP_MOUSEMOVE | IDCMP_ACTIVEWINDOW | IDCMP_INACTIVEWINDOW, TAG_DONE);
    if (!window) { snprintf(error_text, sizeof error_text, "The game's window couldn't open (an RTG Workbench in 15, 16, 24 or 32 bits is needed)."); return 0; }
    /* the game draws its own pointer: Intuition's is made invisible over the window */
    hide_pointer();
    pixFmt = GetCyberMapAttr(window->RPort->BitMap, CYBRMATTR_PIXFMT);
    rawOk = -1;
    if (screen) { ScreenToFront(screen); ActivateWindow(window); FillPixelArray(window->RPort, 0, 0, window->Width, window->Height, 0x000000); } /* in front, and black round the frame (a 240-line screen) */
    { char line[120]; snprintf(line, sizeof line, "Display: %s, %d x %d, pixel format %lu.", screen ? "full screen" : "a window", FRAME_W * scale, FRAME_H * scale, (unsigned long)pixFmt); game_log(line); }
    windowActive = 1;
#ifdef VAMPIRE
    saga_start(); /* SAGA's display taken over (else drawn through the system) */
#endif
    return 1;
}

static void close_display(void)
{
#ifdef VAMPIRE
    saga_stop();
#endif
    if (window) { ClearPointer(window); CloseWindow(window); window = NULL; }
    if (screen) { CloseScreen(screen); screen = NULL; }
}

int platform_open(const char *gameTitle, int wantFullscreen)
{
    snprintf(title, sizeof title, "%s", gameTitle);
    IntuitionBase = (struct IntuitionBase *)OpenLibrary((CONST_STRPTR)"intuition.library", KICKSTART_31);
    GfxBase = (struct GfxBase *)OpenLibrary((CONST_STRPTR)"graphics.library", KICKSTART_31);
    CyberGfxBase = OpenLibrary((CONST_STRPTR)"cybergraphics.library", 41);
    KeymapBase = OpenLibrary((CONST_STRPTR)"keymap.library", 37); /* the letters typed, by the user's keymap */
    if (!IntuitionBase || !GfxBase) { snprintf(error_text, sizeof error_text, "Kickstart 3.1 or later is needed."); return 0; }
#ifdef VAMPIRE
    if (!(SysBase->AttnFlags & (1u << 10))) /* AFB_68080: this build is the Vampire's alone (its AMMX and SAGA) */
    { snprintf(error_text, sizeof error_text, "This game is built for the Vampire (Apollo 68080) only. On any other Amiga use its \"Amiga 68k (RTG)\" build."); return 0; }
#endif
    if (!CyberGfxBase) { snprintf(error_text, sizeof error_text, "cybergraphics.library 41 or later is needed: an RTG graphics card (Picasso96 or CyberGraphX) or a Vampire."); return 0; }
    timerPort = CreateMsgPort();
    timerRequest = timerPort ? (struct timerequest *)CreateIORequest(timerPort, sizeof(struct timerequest)) : NULL;
    if (!timerRequest || OpenDevice((CONST_STRPTR)TIMERNAME, UNIT_ECLOCK, (struct IORequest *)timerRequest, 0) != 0) { snprintf(error_text, sizeof error_text, "timer.device couldn't open."); return 0; }
    TimerBase = (void *)timerRequest->tr_node.io_Device;
    eclockRate = ReadEClock(&startClock);
    fullscreen = wantFullscreen;
    inputPort = CreateMsgPort();
    inputRequest = inputPort ? (struct IOStdReq *)CreateIORequest(inputPort, sizeof(struct IOStdReq)) : NULL;
    inputOpen = inputRequest && OpenDevice((CONST_STRPTR)"input.device", 0, (struct IORequest *)inputRequest, 0) == 0;
    if (!open_display()) return 0;
    scaled = (ULONG *)AllocVec(FRAME_W * BIGGEST_SHOW * FRAME_H * BIGGEST_SHOW * 4, MEMF_ANY);
    row = (ULONG *)AllocVec(FRAME_W * BIGGEST_SHOW * 4, MEMF_ANY);
    if (!scaled || !row) { snprintf(error_text, sizeof error_text, "Not enough memory for the picture."); return 0; }
    return 1;
}

void platform_close(void)
{
    close_display();
    if (scaled) FreeVec(scaled);
    if (row) FreeVec(row);
    scaled = row = NULL;
    if (blankPointer) { FreeMem(blankPointer, BLANK_POINTER_BYTES); blankPointer = NULL; }
    if (inputOpen) CloseDevice((struct IORequest *)inputRequest);
    if (inputRequest) DeleteIORequest((struct IORequest *)inputRequest);
    if (inputPort) DeleteMsgPort(inputPort);
    inputOpen = 0; inputRequest = NULL; inputPort = NULL;
    if (TimerBase) CloseDevice((struct IORequest *)timerRequest);
    if (timerRequest) DeleteIORequest((struct IORequest *)timerRequest);
    if (timerPort) DeleteMsgPort(timerPort);
    if (CyberGfxBase) CloseLibrary(CyberGfxBase);
    if (KeymapBase) { CloseLibrary(KeymapBase); KeymapBase = NULL; }
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
        case 0x59: case 0x4B: return KEY_F11; /* F11 (a PC keyboard on MorphOS); F10 too - Amiga keyboards have no F11 */
        case 0x48: return KEY_PAGEUP;   /* Page Up / Page Down (PC keyboards on MorphOS): a talk's Previous / More, as on Windows (b579) */
        case 0x49: return KEY_PAGEDOWN;
        case 0x42: return KEY_TAB;
        case 0x33: return KEY_C;
        case 0x17: return KEY_I;
        case 0x31: return KEY_Z;
        case 0x21: return KEY_S;
        case 0x41: return KEY_BACKSPACE;
        case 0x01: case 0x1D: return KEY_1; /* the row of numbers, and the keypad's */
        case 0x02: case 0x1E: return KEY_2;
        case 0x03: case 0x1F: return KEY_3;
        case 0x04: case 0x2D: return KEY_4;
        case 0x05: case 0x2E: return KEY_5;
        case 0x06: case 0x2F: return KEY_6;
        case 0x07: case 0x3D: return KEY_7;
        case 0x08: case 0x3E: return KEY_8;
        case 0x09: case 0x3F: return KEY_9;
        default: return KEY_OTHER;
    }
}

static int lastMoveX = -1, lastMoveY = -1;
static int gameTakesFKeys;
void platform_game_takes_function_keys(int on) { gameTakesFKeys = on; }

/* A raw key's name as the editor's key lists have it (Engine/U8Keys.cs Bindable); "" for one none of them is. */
static void key_name(UWORD code, char *out)
{
    static const char *ROW1 = "1234567890", *QROW = "QWERTYUIOP", *AROW = "ASDFGHJKL", *ZROW = "ZXCVBNM";
    int c = code & 0x7F;
    const char *n = "";
    out[0] = 0;
    if (c >= 0x01 && c <= 0x0A) { snprintf(out, 16, "D%c", ROW1[c - 0x01]); return; }
    if (c >= 0x10 && c <= 0x19) { out[0] = QROW[c - 0x10]; out[1] = 0; return; }
    if (c >= 0x20 && c <= 0x28) { out[0] = AROW[c - 0x20]; out[1] = 0; return; }
    if (c >= 0x31 && c <= 0x37) { out[0] = ZROW[c - 0x31]; out[1] = 0; return; }
    if (c >= 0x50 && c <= 0x59) { snprintf(out, 16, "F%d", c - 0x50 + 1); return; }
    switch (c)
    {
        case 0x0F: n = "NumPad0"; break; case 0x1D: n = "NumPad1"; break; case 0x1E: n = "NumPad2"; break; case 0x1F: n = "NumPad3"; break;
        case 0x2D: n = "NumPad4"; break; case 0x2E: n = "NumPad5"; break; case 0x2F: n = "NumPad6"; break; case 0x3D: n = "NumPad7"; break;
        case 0x3E: n = "NumPad8"; break; case 0x3F: n = "NumPad9"; break; case 0x3C: n = "Decimal"; break; case 0x4A: n = "Subtract"; break;
        case 0x5C: n = "Divide"; break; case 0x5D: n = "Multiply"; break; case 0x5E: n = "Add"; break;
        case 0x00: n = "OemTilde"; break; case 0x0B: n = "OemMinus"; break; case 0x0C: n = "OemPlus"; break; case 0x0D: n = "OemPipe"; break;
        case 0x1A: n = "OemOpenBrackets"; break; case 0x1B: n = "OemCloseBrackets"; break; case 0x29: n = "OemSemicolon"; break; case 0x2A: n = "OemQuotes"; break;
        case 0x38: n = "OemComma"; break; case 0x39: n = "OemPeriod"; break; case 0x3A: n = "OemQuestion"; break;
        case 0x40: n = "Space"; break; case 0x41: n = "Back"; break; case 0x42: n = "Tab"; break; case 0x43: case 0x44: n = "Enter"; break;
        case 0x45: n = "Escape"; break; case 0x46: n = "Delete"; break; case 0x4C: n = "Up"; break; case 0x4D: n = "Down"; break;
        case 0x4E: n = "Right"; break; case 0x4F: n = "Left"; break; case 0x60: n = "LeftShift"; break; case 0x61: n = "RightShift"; break;
        case 0x62: n = "CapsLock"; break; case 0x63: n = "LeftCtrl"; break; case 0x64: n = "LeftAlt"; break; case 0x65: n = "RightAlt"; break;
        case 0x4B: n = "F11"; break; case 0x6F: n = "F12"; break; case 0x47: n = "Insert"; break; case 0x48: n = "PageUp"; break;
        case 0x49: n = "PageDown"; break; case 0x70: n = "Home"; break; case 0x71: n = "End"; break;
        default: break;
    }
    snprintf(out, 16, "%s", n);
}

int platform_poll(PlatformEvent *event)
{
    struct IntuiMessage *m;
    if (pendingText) { event->type = EV_TEXT; event->ch = pendingText; pendingText = 0; return 1; }
    while (window && (m = (struct IntuiMessage *)GetMsg(window->UserPort)))
    {
        ULONG cls = m->Class;
        UWORD code = m->Code, qualifier = m->Qualifier;
        heldQualifier = qualifier; /* Ctrl held (platform_ctrl_held) */
        if (cls == IDCMP_RAWKEY && !(code & IECODE_UP_PREFIX) && KeymapBase)
        {
            /* the letter this key makes, by the keymap (shift, dead keys) */
            struct InputEvent ie;
            memset(&ie, 0, sizeof ie);
            ie.ie_Class = IECLASS_RAWKEY; ie.ie_Code = code; ie.ie_Qualifier = m->Qualifier;
            ie.ie_EventAddress = m->IAddress ? (APTR)*((ULONG *)m->IAddress) : NULL;
            char buf[4];
            if (MapRawKey(&ie, (STRPTR)buf, 4, NULL) == 1 && (unsigned char)buf[0] >= 32 && (unsigned char)buf[0] <= 126) pendingText = buf[0];
        }
        int mx = m->MouseX - (screen ? offX : window->BorderLeft), my = m->MouseY - (screen ? offY : window->BorderTop);
        ReplyMsg((struct Message *)m);
        event->type = EV_NONE;
        event->mouseX = mx / (scale * frame_scale);
        event->mouseY = my / (scale * frame_scale);
        switch (cls)
        {
            case IDCMP_CLOSEWINDOW: event->type = EV_QUIT; return 1;
            case IDCMP_ACTIVEWINDOW: windowActive = 1; hide_pointer(); break; /* again each time: some systems give the window the default pointer back */
            case IDCMP_INACTIVEWINDOW: windowActive = 0; break; /* Amiga-tabbed away: the mouse is free */
            case IDCMP_RAWKEY:
                if ((code & 0x7F) >= 0x68) break; /* not a key */
                if ((code & 0x7F) == 0x58 && !gameTakesFKeys) { if (!(code & IECODE_UP_PREFIX)) mouseLocked = !mouseLocked; break; } /* F9: the mouse let go / kept in */
                event->type = (code & IECODE_UP_PREFIX) ? EV_KEY_UP : EV_KEY_DOWN;
                event->key = key_of(code);
                if (gameTakesFKeys && (code & 0x7F) == 0x59) event->key = KEY_OTHER; /* F10: Ultima 6's party view, not full screen */
                key_name(code, event->keyName);
                event->keyMods = (qualifier & IEQUALIFIER_CONTROL ? KEYMOD_CTRL : 0) | (qualifier & (IEQUALIFIER_LSHIFT | IEQUALIFIER_RSHIFT) ? KEYMOD_SHIFT : 0) | (qualifier & (IEQUALIFIER_LALT | IEQUALIFIER_RALT) ? KEYMOD_ALT : 0);
                event->keyRepeat = (qualifier & IEQUALIFIER_REPEAT) != 0; /* a held key's auto-repeat: walking ignores it (as the Windows engine) - main.c drops it but for Ultima 6's cursor */
                if ((code & 0x7F) == 0x10 && (qualifier & IEQUALIFIER_CONTROL)) event->key = KEY_CTRL_Q; /* Ctrl+Q: the Quit prompt */
                return 1;
            case IDCMP_MOUSEBUTTONS:
                if (code == SELECTDOWN || code == MENUDOWN) { event->type = EV_MOUSE_DOWN; event->button = code == MENUDOWN ? 2 : 1; return 1; }
                if (code == SELECTUP || code == MENUUP) { event->type = EV_MOUSE_UP; event->button = code == MENUUP ? 2 : 1; return 1; }
                break;
            case IDCMP_MOUSEMOVE:
                if (mouseLocked && windowActive)
                {
                    int w = FRAME_W * scale, h = FRAME_H * scale;
                    int cx = mx < 0 ? 0 : mx >= w ? w - 1 : mx, cy = my < 0 ? 0 : my >= h ? h - 1 : my;
                    if (cx != mx || cy != my) { warp_mouse(cx, cy); mx = cx; my = cy; event->mouseX = mx / (scale * frame_scale); event->mouseY = my / (scale * frame_scale); }
                }
                lastMoveX = event->mouseX; lastMoveY = event->mouseY;
                event->type = EV_MOUSE_MOVE; return 1;
        }
    }
    /* Where the mouse is now, from the window itself: if a move's message never came (a heavy frame, MorphOS's window), the pointer still follows. */
    if (window && windowActive)
    {
        int x = (window->MouseX - (screen ? offX : window->BorderLeft)) / (scale * frame_scale), y = (window->MouseY - (screen ? offY : window->BorderTop)) / (scale * frame_scale);
        if (x != lastMoveX || y != lastMoveY)
        {
            lastMoveX = x; lastMoveY = y;
            event->type = EV_MOUSE_MOVE; event->mouseX = x; event->mouseY = y;
            return 1;
        }
    }
    return 0;
}

/* One ARGB pixel in the display's own format: 16 bits (in the low half), or 32. */
static inline ULONG native_px(ULONG c, ULONG fmt)
{
    ULONG v;
    switch (fmt)
    {
        case PIXFMT_RGB16: return ((c >> 8) & 0xF800) | ((c >> 5) & 0x07E0) | ((c >> 3) & 0x001F);
        case PIXFMT_RGB16PC: v = ((c >> 8) & 0xF800) | ((c >> 5) & 0x07E0) | ((c >> 3) & 0x001F); return ((v >> 8) | (v << 8)) & 0xFFFF;
        case PIXFMT_BGR16PC: v = ((c << 8) & 0xF800) | ((c >> 5) & 0x07E0) | ((c >> 19) & 0x001F); return ((v >> 8) | (v << 8)) & 0xFFFF;
        case PIXFMT_RGB15: return ((c >> 9) & 0x7C00) | ((c >> 6) & 0x03E0) | ((c >> 3) & 0x001F);
        case PIXFMT_RGB15PC: v = ((c >> 9) & 0x7C00) | ((c >> 6) & 0x03E0) | ((c >> 3) & 0x001F); return ((v >> 8) | (v << 8)) & 0xFFFF;
        case PIXFMT_BGRA32: return ((c & 0xFF) << 24) | ((c & 0xFF00) << 8) | ((c >> 8) & 0xFF00) | 0xFF;
        case PIXFMT_RGBA32: return (c << 8) | 0xFF;
        default: return c; /* ARGB32 */
    }
}

static int fmt_bytes(ULONG fmt)
{
    switch (fmt)
    {
        case PIXFMT_RGB16: case PIXFMT_RGB16PC: case PIXFMT_BGR16PC: case PIXFMT_RGB15: case PIXFMT_RGB15PC: return 2;
        case PIXFMT_ARGB32: case PIXFMT_BGRA32: case PIXFMT_RGBA32: return 4;
        default: return 0; /* 8 or 24 bits, or another: given as ARGB */
    }
}

/* One row of the frame in the display's own format (doubled across when scale is 2): bytes 2 or 4. */
static void native_row(const uint32_t *src, void *out, int bytes)
{
    const ULONG fmt = pixFmt;
    if (bytes == 2)
    {
        if (scale == 2)
        {
            ULONG *a = (ULONG *)out;
            if (fmt == PIXFMT_RGB16) /* Vampire SAGA, many cards: big-endian 5-6-5 */
                for (int x = 0; x < FRAME_W; x++) { ULONG c = src[x], v = ((c >> 8) & 0xF800) | ((c >> 5) & 0x07E0) | ((c >> 3) & 0x001F); a[x] = (v << 16) | v; }
            else if (fmt == PIXFMT_RGB16PC) /* most Picasso96 cards: little-endian 5-6-5 */
                for (int x = 0; x < FRAME_W; x++) { ULONG c = src[x], v = ((c >> 8) & 0xF800) | ((c >> 5) & 0x07E0) | ((c >> 3) & 0x001F); v = ((v >> 8) | (v << 8)) & 0xFFFF; a[x] = (v << 16) | v; }
            else
                for (int x = 0; x < FRAME_W; x++) { ULONG v = native_px(src[x], fmt); a[x] = (v << 16) | v; }
        }
        else
        {
            UWORD *a = (UWORD *)out;
            if (fmt == PIXFMT_RGB16)
                for (int x = 0; x < FRAME_W; x++) { ULONG c = src[x]; a[x] = (UWORD)(((c >> 8) & 0xF800) | ((c >> 5) & 0x07E0) | ((c >> 3) & 0x001F)); }
            else if (fmt == PIXFMT_RGB16PC)
                for (int x = 0; x < FRAME_W; x++) { ULONG c = src[x], v = ((c >> 8) & 0xF800) | ((c >> 5) & 0x07E0) | ((c >> 3) & 0x001F); a[x] = (UWORD)((v >> 8) | (v << 8)); }
            else
                for (int x = 0; x < FRAME_W; x++) a[x] = (UWORD)native_px(src[x], fmt);
        }
        return;
    }
    ULONG *a = (ULONG *)out;
    if (scale == 2)
    {
        if (fmt == PIXFMT_ARGB32) for (int x = 0; x < FRAME_W; x++) { ULONG c = src[x]; a[2 * x] = a[2 * x + 1] = c; }
        else if (fmt == PIXFMT_BGRA32) for (int x = 0; x < FRAME_W; x++) { ULONG c = src[x]; c = ((c & 0xFF) << 24) | ((c & 0xFF00) << 8) | ((c >> 8) & 0xFF00) | 0xFF; a[2 * x] = a[2 * x + 1] = c; }
        else for (int x = 0; x < FRAME_W; x++) { ULONG c = native_px(src[x], fmt); a[2 * x] = a[2 * x + 1] = c; }
    }
    else if (fmt == PIXFMT_ARGB32) memcpy(a, src, FRAME_W * 4);
    else for (int x = 0; x < FRAME_W; x++) a[x] = native_px(src[x], fmt);
}

/* The frame made in the display's format (doubled when scale is 2) into "scaled"; its bytes a row. */
static int make_native(const uint32_t *pixels, int bytes)
{
    const int rowBytes = FRAME_W * scale * bytes;
    UBYTE *out = (UBYTE *)scaled;
    for (int y = 0; y < FRAME_H; y++)
    {
        UBYTE *a = out + (size_t)(y * scale) * rowBytes;
        native_row(pixels + y * FRAME_W, a, bytes);
        if (scale == 2) memcpy(a + rowBytes, a, rowBytes);
    }
    return rowBytes;
}

#ifdef VAMPIRE
/* ---------------- The Vampire: SAGA's own display, the 68080's AMMX ----------------
   The screen is opened through the Vampire's SAGA driver (its timing right for the monitor); then the game takes it over:
   two frames of its own, made straight in SAGA's 5-6-5 by AMMX (four pixels an instruction), shown by writing SAGA's plane
   pointer ($DFF1EC) at the vertical blank - no copying by the system, no tearing. The frames go in chip memory first (the V4's
   extra chip memory - the display's own), else anywhere. The system's own picture pointer is put back when the game ends. */
#define SAGA_PLANEPTR (*(volatile ULONG *)0xDFF1EC)
void ammx_rgb565_row(const uint32_t *src, uint16_t *dst, unsigned long count); /* vampire_ammx.s */
void ammx_copy_row(const void *src, void *dst, unsigned long bytes);
static UBYTE *sagaMem[2], *sagaBuf[2];
static ULONG sagaOrig, sagaPerRow, sagaRows;
static int sagaOn, sagaBack, ammxOk = -1;

static void c_rgb565_row(const uint32_t *src, uint16_t *dst, int n)
{
    for (int x = 0; x < n; x++) { ULONG c = src[x]; dst[x] = (UWORD)(((c >> 8) & 0xF800) | ((c >> 5) & 0x07E0) | ((c >> 3) & 0x001F)); }
}

/* Each AMMX routine against its C version on a test row: used only if they agree to the last pixel. */
static void vampire_selftest(void)
{
    static uint32_t in[64];
    static uint16_t a[64], b[64];
    static UBYTE c1[64], c2[64];
    for (int i = 0; i < 64; i++) in[i] = 0xFF000000u | ((ULONG)i * 0x9E3779B1u >> 8);
    c_rgb565_row(in, a, 64);
    ammx_rgb565_row(in, b, 64);
    int same = memcmp(a, b, sizeof a) == 0;
    for (int i = 0; i < 64; i++) c1[i] = (UBYTE)(i * 7 + 3);
    ammx_copy_row(c1, c2, 64);
    int copied = memcmp(c1, c2, 64) == 0;
    ammxOk = same && copied;
    char line[160];
    if (ammxOk) snprintf(line, sizeof line, "Vampire: AMMX checked - the frames made by it (four pixels an instruction).");
    else snprintf(line, sizeof line, "Vampire: AMMX gave other pixels (5-6-5 %s, copy %s; first %04X, should be %04X) - the C versions are used.",
                  same ? "ok" : "wrong", copied ? "ok" : "wrong", (unsigned)b[1], (unsigned)a[1]);
    game_log(line);
}

/* Our own two frames for the screen opened, the system's pointer kept to put back. 0: not taken over (the RTG path is used). */
static int saga_start(void)
{
    if (!screen || pixFmt != PIXFMT_RGB16) { game_log("Vampire: the screen isn't SAGA's 16-bit 5-6-5 - drawn through the system instead."); return 0; }
    ULONG base = 0, perRow = 0;
    APTR h = LockBitMapTags(screen->RastPort.BitMap, LBMI_BASEADDRESS, (ULONG)&base, LBMI_BYTESPERROW, (ULONG)&perRow, TAG_DONE);
    if (!h) return 0;
    UnLockBitMap(h);
    if (!base || !perRow) return 0;
    sagaRows = screen->Height;
    ULONG size = perRow * sagaRows;
    const char *where = "chip";
    for (int i = 0; i < 2; i++)
    {
        sagaMem[i] = (UBYTE *)AllocVec(size + 64, MEMF_CHIP | MEMF_CLEAR); /* the V4's extra chip memory */
        if (!sagaMem[i]) { sagaMem[i] = (UBYTE *)AllocVec(size + 64, MEMF_ANY | MEMF_CLEAR); where = "fast"; }
        if (!sagaMem[i]) { if (i) FreeVec(sagaMem[0]); sagaMem[0] = NULL; return 0; }
        sagaBuf[i] = (UBYTE *)(((ULONG)sagaMem[i] + 63) & ~63UL); /* 64-byte aligned for the display's fetches */
    }
    sagaOrig = base; sagaPerRow = perRow; sagaBack = 0; sagaOn = 1;
    if (ammxOk < 0) vampire_selftest();
    char line[160];
    snprintf(line, sizeof line, "Vampire: SAGA's display taken over - two frames of %lu bytes in %s memory, flipped at the vertical blank.", (unsigned long)size, where);
    game_log(line);
    return 1;
}

/* SAGA's own hardware pointer (the Firebird/V4 core's 16 x 16, three colours) is drawn by the core over any picture, so the
   game's frame can't cover it - it showed a hand on top of the game's own pointer. Its picture is SAGA's 16 lines at $DFF800
   (one long each: two bitplanes; AROS's SAGA driver, arch/m68k-amiga/hidd/sagagfx, writes it there): all made see-through,
   every frame while the game's window is the active one, as the system's driver redraws it. Only the picture - its position
   ($DFF1D0/$DFF1D2) stays the system's, so nothing of it is left to put back but the picture, and the system does that itself:
   another window made active draws that window's pointer, and closing the game sets the system's pointer again
   (close_display's ClearPointer, after saga_stop has stopped the blanking), which the driver draws back into SAGA. */
#define SAGA_SPRITE_LINES ((volatile ULONG *)0xDFF800)
static void saga_hide_pointer(void)
{
    if (IntuitionBase->ActiveWindow != window) return; /* the Workbench's pointer is the system's while it has the mouse */
    for (int i = 0; i < 16; i++) SAGA_SPRITE_LINES[i] = 0;
}

/* The frame made straight into the back buffer (5-6-5), then shown at the next vertical blank. */
static void saga_present(const uint32_t *pixels)
{
    UBYTE *to = sagaBuf[sagaBack] + (size_t)offY * sagaPerRow + (size_t)offX * 2;
#ifdef FRAME_HIRES
    for (int y = 0; y < FRAME_H; y++, to += sagaPerRow)
    {
        if (ammxOk > 0 && (FRAME_W & 3) == 0) ammx_rgb565_row(pixels + (size_t)y * FRAME_W, (uint16_t *)to, FRAME_W);
        else c_rgb565_row(pixels + (size_t)y * FRAME_W, (uint16_t *)to, FRAME_W);
    }
#else
    /* 320 x 200 doubled (b615): each row made 5-6-5 by AMMX, each pixel written twice in one long, the row copied below it */
    static uint16_t half[FRAME_W] __attribute__((aligned(16)));
    for (int y = 0; y < FRAME_H; y++, to += sagaPerRow * 2)
    {
        if (ammxOk > 0) ammx_rgb565_row(pixels + (size_t)y * FRAME_W, half, FRAME_W);
        else c_rgb565_row(pixels + (size_t)y * FRAME_W, half, FRAME_W);
        ULONG *wide = (ULONG *)to;
        for (int x = 0; x < FRAME_W; x++) { ULONG c = half[x]; wide[x] = (c << 16) | c; }
        if (ammxOk > 0) ammx_copy_row(to, to + sagaPerRow, FRAME_W * 4);
        else memcpy(to + sagaPerRow, to, FRAME_W * 4);
    }
#endif
    WaitTOF();
    SAGA_PLANEPTR = (ULONG)sagaBuf[sagaBack];
    saga_hide_pointer();
    sagaBack ^= 1;
}

static void saga_stop(void)
{
    if (!sagaOn) return;
    WaitTOF();
    SAGA_PLANEPTR = sagaOrig; /* the system's own picture back before its screen closes */
    WaitTOF();
    for (int i = 0; i < 2; i++) { FreeVec(sagaMem[i]); sagaMem[i] = sagaBuf[i] = NULL; }
    sagaOn = 0;
}
#endif

/* Straight into the display's memory (LockBitMap), each row made in fast memory first and copied there (twice when doubled):
   on our own screen always; in a window only while nothing covers any of it (its layer one whole clip rectangle). 0: not now. */
static int write_direct(const uint32_t *pixels, int bytes)
{
    struct Layer *layer = window->WLayer;
    int x0, y0;
    if (!screen)
    {
        LockLayer(0, layer);
        struct ClipRect *cr = layer->ClipRect;
        x0 = layer->bounds.MinX + window->BorderLeft; y0 = layer->bounds.MinY + window->BorderTop;
#ifdef __MORPHOS__
#define CR_OBSCURED(c) ((c)->lobs != NULL)
#else
#define CR_OBSCURED(c) ((c)->obscured != 0) /* the 3.x NDK's name for it */
#endif
        int whole = cr && !cr->Next && !CR_OBSCURED(cr) && cr->bounds.MinX <= x0 && cr->bounds.MinY <= y0
                    && cr->bounds.MaxX >= x0 + FRAME_W * scale - 1 && cr->bounds.MaxY >= y0 + FRAME_H * scale - 1;
        if (!whole) { UnlockLayer(layer); return 0; }
    }
    else { x0 = offX; y0 = offY; }
    ULONG base = 0, perRow = 0;
    APTR handle = LockBitMapTags(window->RPort->BitMap, LBMI_BASEADDRESS, (ULONG)&base, LBMI_BYTESPERROW, (ULONG)&perRow, TAG_DONE);
    if (!handle || !base)
    {
        if (handle) UnLockBitMap(handle);
        if (!screen) UnlockLayer(layer);
        return 0;
    }
    const int rowBytes = FRAME_W * scale * bytes;
    UBYTE *to = (UBYTE *)base + (size_t)y0 * perRow + (size_t)x0 * bytes;
    for (int y = 0; y < FRAME_H; y++)
    {
        native_row(pixels + y * FRAME_W, row, bytes);
        memcpy(to, row, rowBytes); to += perRow;
        if (scale == 2) { memcpy(to, row, rowBytes); to += perRow; }
    }
    UnLockBitMap(handle);
    if (!screen) UnlockLayer(layer);
    return 1;
}

/* Does this display take RECTFMT_RAW? A pixel written that way and read back (once a display): else the frame is given as ARGB. */
static int raw_works(int bytes)
{
    int x0 = screen ? offX : window->BorderLeft, y0 = screen ? offY : window->BorderTop;
    ULONG test = 0xFF8040C0, back = 0, word[1];
    if (bytes == 2) ((UWORD *)word)[0] = (UWORD)native_px(test, pixFmt);
    else word[0] = native_px(test, pixFmt);
    WritePixelArray(word, 0, 0, bytes, window->RPort, x0, y0, 1, 1, RECTFMT_RAW);
    ReadPixelArray(&back, 0, 0, 4, window->RPort, x0, y0, 1, 1, RECTFMT_ARGB);
    int dr = (int)((back >> 16) & 255) - 0x80, dg = (int)((back >> 8) & 255) - 0x40, db = (int)(back & 255) - 0xC0;
    int ok = dr > -12 && dr < 12 && dg > -12 && dg < 12 && db > -12 && db < 12;
    char line[120];
    snprintf(line, sizeof line, ok ? "Display: frames handed over in its own format." : "Display: its own format can't be written (read back %08lX) - frames given as ARGB.", (unsigned long)back);
    game_log(line);
    return ok;
}

void platform_present(const uint32_t *pixels)
{
    if (!window) return;
    if (crtOn) /* the CRT effect on a copy (some screens draw over the last frame rather than afresh) */
    {
        static uint32_t *crt;
        if (!crt) crt = (uint32_t *)malloc(sizeof(uint32_t) * FRAME_PIXELS);
        if (crt) { memcpy(crt, pixels, sizeof(uint32_t) * FRAME_PIXELS); crt_in_place(crt, FRAME_W, FRAME_H, frame_scale); pixels = crt; }
    }
#ifdef VAMPIRE
    if (sagaOn) { saga_present(pixels); return; } /* SAGA's own display (above) */
#endif
    const int w = FRAME_W * scale, h = FRAME_H * scale;
    int x0 = screen ? offX : window->BorderLeft, y0 = screen ? offY : window->BorderTop;
    int bytes = fmt_bytes(pixFmt);
    if (bytes && write_direct(pixels, bytes)) { static int said; if (!said) { said = 1; game_log("Display: frames written straight into its memory."); } return; }
    if (bytes && rawOk < 0) rawOk = raw_works(bytes);
    if (bytes && rawOk)
    {
        int rowBytes = make_native(pixels, bytes);
        WritePixelArray(scaled, 0, 0, rowBytes, window->RPort, x0, y0, w, h, RECTFMT_RAW);
        return;
    }
    /* any other display: the frame as ARGB (doubled when it must be), converted by the card's software */
    if (scale == 1) { WritePixelArray((APTR)pixels, 0, 0, FRAME_W * 4, window->RPort, x0, y0, w, h, RECTFMT_ARGB); return; }
    for (int y = 0; y < FRAME_H; y++)
    {
        ULONG *a = scaled + (y * 2) * w, *b = a + w;
        const uint32_t *src = pixels + y * FRAME_W;
        for (int x = 0; x < FRAME_W; x++) { ULONG c = src[x]; a[2 * x] = a[2 * x + 1] = c; }
        memcpy(b, a, w * 4);
    }
    WritePixelArray(scaled, 0, 0, w * 4, window->RPort, x0, y0, w, h, RECTFMT_ARGB);
}

uint32_t platform_ticks(void)
{
    struct EClockVal now;
    ReadEClock(&now);
    unsigned long long a = ((unsigned long long)startClock.ev_hi << 32) | startClock.ev_lo;
    unsigned long long b = ((unsigned long long)now.ev_hi << 32) | now.ev_lo;
    return (uint32_t)((b - a) * 1000ULL / (eclockRate ? eclockRate : 1));
}

void platform_warp_mouse(int x, int y) { warp_mouse(x * scale, y * scale); }

double platform_ms_precise(void)
{
    struct EClockVal now;
    ReadEClock(&now);
    unsigned long long a = ((unsigned long long)startClock.ev_hi << 32) | startClock.ev_lo;
    unsigned long long b = ((unsigned long long)now.ev_hi << 32) | now.ev_lo;
    return (double)(b - a) * 1000.0 / (double)(eclockRate ? eclockRate : 1);
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
#ifdef __MORPHOS__
    return 1; /* every MorphOS machine is a PowerPC with its FPU */
#endif
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

/* ---------------- Folders (the saves) ---------------- */
int platform_make_dir(const char *path)
{
    BPTR lock = Lock((CONST_STRPTR)path, ACCESS_READ);
    if (lock) { UnLock(lock); return 1; } /* there already */
    lock = CreateDir((CONST_STRPTR)path);
    if (!lock) return 0;
    UnLock(lock);
    return 1;
}

int platform_remove_dir(const char *path) { return DeleteFile((CONST_STRPTR)path) != 0; }
