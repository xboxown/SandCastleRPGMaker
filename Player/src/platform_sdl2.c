/*
 * The platform layer on SDL2: Windows (testing), Linux, Mac, MorphOS and
 * AmigaOS 4. A window that scales the 320 x 200 frame without blurring
 * (whole pixels, as the Windows engine's nearest-neighbour scaling).
 */
#include "platform.h"
#include "u6.h" /* the controller: Cross is Enter on Ultima 6's menu */
#include "crt.h"
#include <errno.h>
#include <stdlib.h>
#include <stdio.h>
#include <string.h>
#include <SDL.h>

static SDL_Window *window;
static SDL_Renderer *renderer;
static SDL_Texture *frame;
static char error_text[256];
/* The consoles (PS2, PS3): their controller is the keyboard and mouse; a stop screen on the TV for a message. */
#if defined(PLATFORM_PS2) || defined(PLATFORM_PS3)
#define PLATFORM_CONSOLE
#endif
#ifdef PLATFORM_PS2
#define console_pad_read ps2_pad_read
#define console_stop_screen ps2_stop_screen
#endif
#ifdef PLATFORM_CONSOLE
static void ps2_controller_open(void);
static int ps2_controller_poll(PlatformEvent *event);
static int ps2_controller_event(const SDL_Event *e, PlatformEvent *event);
#endif

const char *platform_error(void) { return error_text; }

#ifdef FRAME_HIRES
int frame_scale = 1;
#endif
static int wantScale = 2;
void platform_want_window_scale(int scale) { wantScale = scale; }
/* System Settings > Game Window > CRT effect (crt.c): the game drawn at 640 x 400 and shown as a 1280 x 800 picture with its
   scanlines (crtDouble) - on the PS2 (its picture can't be doubled) the lines darkened in place. */
static int crtOn, crtDouble;
void platform_want_crt(int on) { crtOn = on; }
static int wantFill;
void platform_want_fill(int on) { wantFill = on; }
#ifdef VIEW_FILL
int screen_w = BASE_W, screen_h = BASE_H;

/* Fill the screen (FillScreenView.cs): the biggest whole magnification of 320 x 200 that fits (outW x outH), the picture drawn
   that fine where it can be (at most 6 of its pixels to each of the game's; the rest a whole-number enlargement of the texture),
   and the game's screen widened in game pixels till it fills. Returns the texture's whole enlargement (0: not filled). */
static int choose_fill(int outW, int outH)
{
    int most = outW / BASE_W < outH / BASE_H ? outW / BASE_W : outH / BASE_H;
    if (most < 1) return 0;
    int k, total;
    if (crtDouble) { k = most / 4 > 1 ? most / 4 : 1; total = 4 * k; } /* the CRT picture: drawn at 2, doubled with its lines */
    else
    {
        frame_scale = most <= 6 ? most : 1;
        if (most > 6) for (int d = 6; d >= 1; d--) if (most % d == 0) { frame_scale = d; break; }
        k = most / frame_scale; total = most;
    }
    screen_w = outW / total > BASE_W ? outW / total : BASE_W;
    screen_h = outH / total > BASE_H ? outH / total : BASE_H;
    return k;
}
#endif

int platform_open(const char *title, int fullscreen)
{
#ifdef PLATFORM_PS2
    if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_TIMER | SDL_INIT_EVENTS) != 0) /* sound and the controller: platform_ps2.c's own */
#else
    if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_TIMER | SDL_INIT_EVENTS | SDL_INIT_AUDIO) != 0)
#endif
    {
        SDL_snprintf(error_text, sizeof error_text, "SDL couldn't start: %s", SDL_GetError());
        return 0;
    }
    SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, "nearest");
#ifdef PLATFORM_CONSOLE
    fullscreen = 1; /* a TV has no windows: a game set to play in a window filled only the top-left corner of the PS3's picture */
#endif
#ifdef FRAME_HIRES
    if (getenv("PLAYER_SCALE")) frame_scale = atoi(getenv("PLAYER_SCALE")); /* testing: the picture this fine */
    else frame_scale = wantScale;
    if (frame_scale < 1) frame_scale = 1; else if (frame_scale > 4) frame_scale = 4;
#ifdef PLATFORM_PS2
    frame_scale = 2; /* 640 x 400: the PS2's TV picture, 2 of its pixels to each of the game's */
#endif
#ifdef PLATFORM_PS3
    frame_scale = 4; /* 1280 x 800: 4 x 4 of the PS3's pixels to each of the game's (the finest the player draws), then filled to the TV */
#endif
#ifndef PLATFORM_PS2
    if (crtOn) frame_scale = 2; /* the CRT effect: drawn at 640 x 400, shown doubled with its scanlines (1280 x 800) */
#endif
#endif
#ifndef PLATFORM_PS2
    crtDouble = crtOn;
#endif
    int windowW = SCREEN_W * 3, windowH = SCREEN_H * 3, filled = 0;
#ifdef VIEW_FILL
    if (wantFill)
    {
        /* full screen: the desktop's size; in a window: the project's window size, exactly (System Settings > Game Window) */
        SDL_DisplayMode desk;
        int ws = wantScale < 1 ? 1 : wantScale;
        int outW = BASE_W * ws, outH = BASE_H * ws;
        if (fullscreen && SDL_GetDesktopDisplayMode(0, &desk) == 0) { outW = desk.w; outH = desk.h; }
        filled = choose_fill(outW, outH);
        if (filled && !fullscreen) { windowW = outW; windowH = outH; }
    }
#endif
    window = SDL_CreateWindow(title, SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED, windowW, windowH,
                              SDL_WINDOW_RESIZABLE | (fullscreen ? SDL_WINDOW_FULLSCREEN_DESKTOP : 0));
    if (!window) { SDL_snprintf(error_text, sizeof error_text, "No window: %s", SDL_GetError()); return 0; }
    renderer = SDL_CreateRenderer(window, -1, SDL_RENDERER_PRESENTVSYNC);
    if (!renderer) renderer = SDL_CreateRenderer(window, -1, 0);
    if (!renderer) { SDL_snprintf(error_text, sizeof error_text, "No renderer: %s", SDL_GetError()); return 0; }
    SDL_RenderSetLogicalSize(renderer, FRAME_W * (crtDouble ? 2 : 1), FRAME_H * (crtDouble ? 2 : 1));
    if (filled) SDL_RenderSetIntegerScale(renderer, SDL_TRUE); /* every pixel the same whole size: no uneven ones, no blur */
    SDL_ShowCursor(SDL_DISABLE); /* the game draws its own pointer */
#ifdef PLATFORM_PS2
    /* The PS2's graphics chip: its own colour order (ABGR), and its alpha's "opaque" is 128 - at the game's 255 it drew
       every colour twice as bright (dusk's orange came out bright yellow). So the picture goes over in the chip's order,
       alpha 128, never blended; and only whole-number sizes (each of the game's pixels exactly 2 x 2 on the TV). */
    SDL_RenderSetIntegerScale(renderer, SDL_TRUE);
    frame = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_ABGR8888, SDL_TEXTUREACCESS_STREAMING, FRAME_W, FRAME_H);
    if (frame) SDL_SetTextureBlendMode(frame, SDL_BLENDMODE_NONE);
    /* The chip's "as it is" for a picture's colour is 128 too: SDL passes its 255 straight through, and every colour came out
       twice as bright (measured in PCSX2: grass 92,87,43 shown as 183,173,85 - dawn bright yellow, light colours white). */
    if (frame) SDL_SetTextureColorMod(frame, 128, 128, 128);
#else
    /* The PS3: the 960 x 600 picture as big as the TV's height lets it, its shape kept (bars only at the sides of a wide TV), as on
       Windows full screen - not whole-number sizes only, which left it small in the middle of a 1080p picture. */
    /* the CRT picture scaled to the screen smoothly: nearest-pixel made its scanlines uneven stripes (800 lines onto 1080) */
    if (crtDouble) SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, "linear");
    frame = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_ARGB8888, SDL_TEXTUREACCESS_STREAMING, FRAME_W * (crtDouble ? 2 : 1), FRAME_H * (crtDouble ? 2 : 1));
#endif
    if (!frame) { SDL_snprintf(error_text, sizeof error_text, "No texture: %s", SDL_GetError()); return 0; }
#ifdef PLATFORM_CONSOLE
    ps2_controller_open();
#endif
    return 1;
}

void platform_close(void)
{
    if (frame) SDL_DestroyTexture(frame);
    if (renderer) SDL_DestroyRenderer(renderer);
    if (window) SDL_DestroyWindow(window);
    SDL_Quit();
}

static enum PlatformKey key_of(SDL_Keycode k)
{
    switch (k)
    {
        case SDLK_LEFT: return KEY_LEFT;
        case SDLK_RIGHT: return KEY_RIGHT;
        case SDLK_UP: return KEY_UP;
        case SDLK_DOWN: return KEY_DOWN;
        case SDLK_ESCAPE: return KEY_ESCAPE;
        case SDLK_RETURN: return KEY_ENTER;
        case SDLK_SPACE: return KEY_SPACE;
        case SDLK_F11: return KEY_F11;
        case SDLK_KP_ENTER: return KEY_ENTER;
        case SDLK_TAB: return KEY_TAB;
        case SDLK_c: return KEY_C;
        case SDLK_i: return KEY_I;
        case SDLK_z: return KEY_Z;
        case SDLK_s: return KEY_S;
        case SDLK_BACKSPACE: return KEY_BACKSPACE;
        case SDLK_PAGEUP: return KEY_PAGEUP;
        case SDLK_PAGEDOWN: return KEY_PAGEDOWN;
        case SDLK_1: case SDLK_KP_1: return KEY_1;
        case SDLK_2: case SDLK_KP_2: return KEY_2;
        case SDLK_3: case SDLK_KP_3: return KEY_3;
        case SDLK_4: case SDLK_KP_4: return KEY_4;
        case SDLK_5: case SDLK_KP_5: return KEY_5;
        case SDLK_6: case SDLK_KP_6: return KEY_6;
        case SDLK_7: case SDLK_KP_7: return KEY_7;
        case SDLK_8: case SDLK_KP_8: return KEY_8;
        case SDLK_9: case SDLK_KP_9: return KEY_9;
        default: return KEY_OTHER;
    }
}

void platform_game_takes_function_keys(int on) { (void)on; } /* SDL machines: F1-F10 are the game's already (F11 full screen) */

/* A key's name as the editor's key lists have it (Engine/U8Keys.cs Bindable); "" for one none of them is. */
static void key_name(SDL_Keycode k, char *out)
{
    out[0] = 0;
    if (k >= SDLK_a && k <= SDLK_z) { out[0] = (char)('A' + (k - SDLK_a)); out[1] = 0; return; }
    if (k >= SDLK_0 && k <= SDLK_9) { snprintf(out, 16, "D%d", (int)(k - SDLK_0)); return; }
    if (k >= SDLK_KP_1 && k <= SDLK_KP_9) { snprintf(out, 16, "NumPad%d", (int)(k - SDLK_KP_1) + 1); return; }
    if (k >= SDLK_F1 && k <= SDLK_F12) { snprintf(out, 16, "F%d", (int)(k - SDLK_F1) + 1); return; }
    const char *n = "";
    switch (k)
    {
        case SDLK_KP_0: n = "NumPad0"; break;
        case SDLK_UP: n = "Up"; break; case SDLK_DOWN: n = "Down"; break; case SDLK_LEFT: n = "Left"; break; case SDLK_RIGHT: n = "Right"; break;
        case SDLK_SPACE: n = "Space"; break; case SDLK_RETURN: case SDLK_KP_ENTER: n = "Enter"; break; case SDLK_TAB: n = "Tab"; break;
        case SDLK_BACKSPACE: n = "Back"; break; case SDLK_ESCAPE: n = "Escape"; break; case SDLK_INSERT: n = "Insert"; break;
        case SDLK_DELETE: n = "Delete"; break; case SDLK_HOME: n = "Home"; break; case SDLK_END: n = "End"; break;
        case SDLK_PAGEUP: n = "PageUp"; break; case SDLK_PAGEDOWN: n = "PageDown"; break;
        case SDLK_LSHIFT: n = "LeftShift"; break; case SDLK_RSHIFT: n = "RightShift"; break; case SDLK_LCTRL: n = "LeftCtrl"; break;
        case SDLK_RCTRL: n = "RightCtrl"; break; case SDLK_LALT: n = "LeftAlt"; break; case SDLK_RALT: n = "RightAlt"; break;
        case SDLK_CAPSLOCK: n = "CapsLock"; break; case SDLK_NUMLOCKCLEAR: n = "NumLock"; break; case SDLK_SCROLLLOCK: n = "Scroll"; break; case SDLK_PAUSE: n = "Pause"; break;
        case SDLK_BACKQUOTE: n = "OemTilde"; break; case SDLK_MINUS: n = "OemMinus"; break; case SDLK_EQUALS: n = "OemPlus"; break;
        case SDLK_LEFTBRACKET: n = "OemOpenBrackets"; break; case SDLK_RIGHTBRACKET: n = "OemCloseBrackets"; break; case SDLK_SEMICOLON: n = "OemSemicolon"; break;
        case SDLK_QUOTE: n = "OemQuotes"; break; case SDLK_COMMA: n = "OemComma"; break; case SDLK_PERIOD: n = "OemPeriod"; break;
        case SDLK_SLASH: n = "OemQuestion"; break; case SDLK_BACKSLASH: n = "OemPipe"; break;
        case SDLK_KP_PLUS: n = "Add"; break; case SDLK_KP_MINUS: n = "Subtract"; break; case SDLK_KP_MULTIPLY: n = "Multiply"; break;
        case SDLK_KP_DIVIDE: n = "Divide"; break; case SDLK_KP_PERIOD: n = "Decimal"; break;
        default: break;
    }
    snprintf(out, 16, "%s", n);
}

static void key_event_names(PlatformEvent *event, const SDL_KeyboardEvent *k)
{
    key_name(k->keysym.sym, event->keyName);
    event->keyMods = (k->keysym.mod & KMOD_CTRL ? KEYMOD_CTRL : 0) | (k->keysym.mod & KMOD_SHIFT ? KEYMOD_SHIFT : 0) | (k->keysym.mod & KMOD_ALT ? KEYMOD_ALT : 0);
    event->keyRepeat = k->repeat != 0;
}

int platform_poll(PlatformEvent *event)
{
    SDL_Event e;
#ifdef PLATFORM_CONSOLE
    if (ps2_controller_poll(event)) return 1; /* the controller's pointer and queued presses */
#endif
    while (SDL_PollEvent(&e))
    {
        event->type = EV_NONE;
#ifdef PLATFORM_CONSOLE
        if (ps2_controller_event(&e, event)) return 1;
#endif
        switch (e.type)
        {
            case SDL_QUIT: event->type = EV_QUIT; return 1;
            case SDL_KEYDOWN:
                event->type = EV_KEY_DOWN; event->key = key_of(e.key.keysym.sym);
                if (e.key.keysym.sym == SDLK_q && (e.key.keysym.mod & KMOD_CTRL)) event->key = KEY_CTRL_Q; /* Ctrl+Q: the Quit prompt */
                key_event_names(event, &e.key); /* a held key's repeats come too, marked (only Ultima 6's cursor uses them; main.c drops the rest) */
                return 1;
            case SDL_KEYUP:
                event->type = EV_KEY_UP; event->key = key_of(e.key.keysym.sym); key_event_names(event, &e.key); return 1;
            case SDL_MOUSEBUTTONDOWN:
            case SDL_MOUSEBUTTONUP:
                event->type = e.type == SDL_MOUSEBUTTONDOWN ? EV_MOUSE_DOWN : EV_MOUSE_UP;
                event->button = e.button.button == SDL_BUTTON_RIGHT ? 2 : 1;
                event->mouseX = e.button.x / (frame_scale * (crtDouble ? 2 : 1)); event->mouseY = e.button.y / (frame_scale * (crtDouble ? 2 : 1)); /* logical size: in the picture's pixels */
                return 1;
            case SDL_TEXTINPUT:
                if ((unsigned char)e.text.text[0] < 32 || (unsigned char)e.text.text[0] > 126) break; /* letters typed (a paperdoll's text box) */
                event->type = EV_TEXT; event->ch = e.text.text[0]; return 1;
            case SDL_MOUSEMOTION:
                event->type = EV_MOUSE_MOVE; event->mouseX = e.motion.x / (frame_scale * (crtDouble ? 2 : 1)); event->mouseY = e.motion.y / (frame_scale * (crtDouble ? 2 : 1)); return 1;
            default: break;
        }
    }
    return 0;
}

void platform_present(const uint32_t *pixels)
{
#ifdef PLATFORM_PS2
    if (crtOn) /* the CRT effect on a copy (the game's own picture is drawn over again, not afresh, by some screens) */
    {
        static uint32_t *crt;
        if (!crt) crt = (uint32_t *)malloc(sizeof(uint32_t) * FRAME_PIXELS);
        if (crt) { memcpy(crt, pixels, sizeof(uint32_t) * FRAME_PIXELS); crt_in_place(crt, FRAME_W, FRAME_H, frame_scale); pixels = crt; }
    }
    void *to; int pitch;
    if (SDL_LockTexture(frame, NULL, &to, &pitch) == 0)
    {
        for (int y = 0; y < FRAME_H; y++)
        {
            const uint32_t *in = pixels + (size_t)y * FRAME_W;
            uint32_t *out = (uint32_t *)((uint8_t *)to + (size_t)y * pitch);
            for (int x = 0; x < FRAME_W; x++)
            {
                uint32_t p = in[x]; /* 0xAARRGGBB -> the chip's 0xAABBGGRR, alpha 128 */
                out[x] = 0x80000000u | (p & 0xFF00u) | (p >> 16 & 0xFFu) | (p & 0xFFu) << 16;
            }
        }
        SDL_UnlockTexture(frame);
    }
#else
    if (crtDouble) /* the CRT picture made straight into the texture: twice the size, with its scanlines (crt.c) */
    {
        void *to; int pitch;
        if (SDL_LockTexture(frame, NULL, &to, &pitch) == 0)
        {
#ifdef PLATFORM_PS3
            int ps3_crt_spu(const uint32_t *in, int w, int h, uint32_t *out, int outPitch);
            if (!ps3_crt_spu(pixels, FRAME_W, FRAME_H, (uint32_t *)to, pitch / 4)) /* on the SPEs (platform_ps3.c); else here */
#endif
            crt_double(pixels, FRAME_W, FRAME_H, (uint32_t *)to, pitch / 4, 0, FRAME_H);
            SDL_UnlockTexture(frame);
        }
    }
    else SDL_UpdateTexture(frame, NULL, pixels, FRAME_W * 4);
#endif
#ifndef PLATFORM_PS3
    SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
    SDL_RenderClear(renderer);
#endif
    /* (The PS3: no clear - its SDL fills the whole 1080p screen with the processor, 8 MB a frame, under a picture that covers all
       but the bars at the sides; they start black and nothing draws there.) */
    SDL_RenderCopy(renderer, frame, NULL, NULL);
    SDL_RenderPresent(renderer);
#ifdef PLATFORM_PS3
    void ps3_rsx_rewind(void);
    ps3_rsx_rewind(); /* the graphics chip's commands back to the buffer's start (platform_ps3.c) */
#endif
}

#ifdef SANDCASTLE_INSTALLED
static char installedName[128] = "Game";
void platform_installed_data(const char *argv0, char *folder, int size)
{
    const char *slash = strrchr(argv0, '/');
    snprintf(installedName, sizeof installedName, "%s", slash ? slash + 1 : argv0[0] ? argv0 : "Game"); /* the program's name: the game's */
    char *base = SDL_GetBasePath(); /* where the program is - in a Mac .app its Contents/Resources */
    if (base) { snprintf(folder, (size_t)size, "%sGame_Data", base); SDL_free(base); }
    else if (slash) snprintf(folder, (size_t)size, "%.*s/Game_Data", (int)(slash - argv0), argv0); /* (no /proc to ask): the folder it was started from - the menu gives its whole path */
}
int platform_installed_saves(char *folder, int size)
{
    char *pref = SDL_GetPrefPath("Sandcastle RPG Maker", installedName); /* made if it isn't there */
    if (!pref) return 0;
    snprintf(folder, (size_t)size, "%sSaves", pref);
    SDL_free(pref);
    return 1;
}
#endif

uint32_t platform_ticks(void) { return SDL_GetTicks(); }
int platform_ctrl_held(void) { return (SDL_GetModState() & KMOD_CTRL) != 0; }
void platform_warp_mouse(int x, int y) { (void)x; (void)y; } /* testing only: the Windows test leaves the desktop's mouse alone */
double platform_ms_precise(void) { static Uint64 start; if (!start) start = SDL_GetPerformanceCounter(); return (double)(SDL_GetPerformanceCounter() - start) * 1000.0 / (double)SDL_GetPerformanceFrequency(); }
void platform_sleep(uint32_t ms) { SDL_Delay(ms); }

void platform_toggle_fullscreen(void)
{
    Uint32 flags = SDL_GetWindowFlags(window);
    SDL_SetWindowFullscreen(window, (flags & SDL_WINDOW_FULLSCREEN_DESKTOP) ? 0 : SDL_WINDOW_FULLSCREEN_DESKTOP);
}

#ifdef PLATFORM_PS2
long platform_free_memory_mb(void) { return 32; }
#elif defined(PLATFORM_PS3)
long platform_free_memory_mb(void) { return 256; } /* the PS3's main memory */
#else
long platform_free_memory_mb(void) { return SDL_GetSystemRAM(); }
#endif

int platform_can_run(void) { return 1; }

#ifdef PLATFORM_CONSOLE
void console_stop_screen(const char *heading, const char *text);
void platform_message(const char *heading, const char *text) { console_stop_screen(heading, text); } /* no message boxes on a TV: the reason on the screen, and it waits (platform_ps2.c) */
#else
void platform_message(const char *heading, const char *text) { SDL_ShowSimpleMessageBox(SDL_MESSAGEBOX_ERROR, heading, text, window); }
#endif

/* ---------------- Sound ---------------- */
#ifndef PLATFORM_PS2 /* the PS2's: platform_ps2.c */
static SDL_AudioDeviceID audioDevice;
static AudioFill audioFill;

static void audio_callback(void *user, Uint8 *stream, int bytes)
{
    (void)user;
    audioFill((int16_t *)stream, bytes / 4);
}

const char *platform_audio_name(void) { return "SDL"; }
void platform_audio_route(int which) { (void)which; }

int platform_audio_open(int rate, AudioFill fill)
{
    SDL_AudioSpec want, have;
    SDL_zero(want);
    want.freq = rate; want.format = AUDIO_S16SYS; want.channels = 2; want.samples = 1024; want.callback = audio_callback;
    audioFill = fill;
    audioDevice = SDL_OpenAudioDevice(NULL, 0, &want, &have, 0);
    if (!audioDevice) return 0;
    SDL_PauseAudioDevice(audioDevice, 0);
    return have.freq > 0 ? have.freq : rate;
}

void platform_audio_close(void) { if (audioDevice) { SDL_CloseAudioDevice(audioDevice); audioDevice = 0; } }

void platform_audio_lock(void) { if (audioDevice) SDL_LockAudioDevice(audioDevice); }
void platform_audio_unlock(void) { if (audioDevice) SDL_UnlockAudioDevice(audioDevice); }
#endif

/* ---------------- Folders (the saves) ---------------- */
#ifdef PLATFORM_PS2
/* The memory card: mkdir / rmdir through the PS2's own file system (mc0:). */
#include <sys/stat.h>
#include <unistd.h>
int ps2_may_write(const char *path); /* platform_ps2.c: slot 1 only, never a Free McBoot card */
int platform_make_dir(const char *path) { if (!ps2_may_write(path)) return 0; return mkdir(path, 0777) == 0 || errno == EEXIST; }
int platform_remove_dir(const char *path) { if (!ps2_may_write(path)) return 0; return rmdir(path) == 0; }
#elif defined(_WIN32)
#include <direct.h>
int platform_make_dir(const char *path) { return _mkdir(path) == 0 || errno == EEXIST; }
int platform_remove_dir(const char *path) { return _rmdir(path) == 0; }
#else
#include <sys/stat.h>
#include <unistd.h>
int platform_make_dir(const char *path) { return mkdir(path, 0755) == 0 || errno == EEXIST; }
int platform_remove_dir(const char *path) { return rmdir(path) == 0; }
#endif

#ifdef PLATFORM_CONSOLE
#ifdef PLATFORM_PS2
#include <libpad.h>
#else
/* The PS3: the same buttons, the PS2's bits for them (platform_ps3.c gives them so) */
#define PAD_SELECT 0x0001
#define PAD_L3 0x0002
#define PAD_R3 0x0004
#define PAD_START 0x0008
#define PAD_UP 0x0010
#define PAD_RIGHT 0x0020
#define PAD_DOWN 0x0040
#define PAD_LEFT 0x0080
#define PAD_L2 0x0100
#define PAD_R2 0x0200
#define PAD_L1 0x0400
#define PAD_R1 0x0800
#define PAD_TRIANGLE 0x1000
#define PAD_CIRCLE 0x2000
#define PAD_CROSS 0x4000
#define PAD_SQUARE 0x8000
#endif
/* ---------------- The PS2 controller as the Windows game's keyboard and mouse ----------------
 * D-pad / left stick: the arrow keys (walking).   Right stick: the mouse pointer.
 * Cross: the left button (click, drag).            Circle: the right button (held: walk towards the pointer).
 * Square: a double click (use).                    Triangle: I (inventory).
 * Start: Escape.  Select: S (Save / Load).  R1: C (combat).  L1: Z (stats) - held while the pointer moves: faster.  L2 / R2: Page Up / Down.  L3: Space.  R3: Tab.
 */
static double pointerX = SCREEN_W / 2, pointerY = SCREEN_H / 2;
static int stickRX, stickRY, stickLX, stickLY;
static int heldArrow[4]; /* left, right, up, down - from the left stick */
static PlatformEvent queue[32];
static int queued, queueAt;
static uint32_t lastMove;

static void push(PlatformEvent e) { if (queued < 32) queue[queued++] = e; }
static PlatformEvent at_pointer(int type, int button) { PlatformEvent e; memset(&e, 0, sizeof e); e.type = type; e.button = button; e.mouseX = (int)pointerX; e.mouseY = (int)pointerY; return e; }
/* A key pressed on the controller, with its name as the keyboard's (Ultima 6 and 8 find their keys by name). */
static const char *pad_key_name(enum PlatformKey k)
{
    switch (k)
    {
        case KEY_LEFT: return "Left"; case KEY_RIGHT: return "Right"; case KEY_UP: return "Up"; case KEY_DOWN: return "Down";
        case KEY_ESCAPE: return "Escape"; case KEY_ENTER: return "Enter"; case KEY_SPACE: return "Space"; case KEY_TAB: return "Tab";
        case KEY_PAGEUP: return "PageUp"; case KEY_PAGEDOWN: return "PageDown"; case KEY_C: return "C"; case KEY_I: return "I";
        case KEY_S: return "S"; case KEY_Z: return "Z"; default: return "";
    }
}
static PlatformEvent key_event(int type, enum PlatformKey key) { PlatformEvent e; memset(&e, 0, sizeof e); e.type = type; e.key = key; snprintf(e.keyName, sizeof e.keyName, "%s", pad_key_name(key)); return e; }

static void ps2_controller_open(void) { lastMove = SDL_GetTicks(); }
int console_pad_read(unsigned *buttons, int *lx, int *ly, int *rx, int *ry); /* platform_ps2.c / platform_ps3.c: the controller in port 1 (0..255 sticks) */

/* The buttons, and what each is on the keyboard (the mouse's: Cross, Circle, Square - below). */
static const struct { unsigned bit; enum PlatformKey key; } BUTTON_KEYS[] = {
    { PAD_LEFT, KEY_LEFT }, { PAD_RIGHT, KEY_RIGHT }, { PAD_UP, KEY_UP }, { PAD_DOWN, KEY_DOWN },
    { PAD_TRIANGLE, KEY_I }, { PAD_START, KEY_ESCAPE }, { PAD_SELECT, KEY_S }, { PAD_R1, KEY_C },
    { PAD_L2, KEY_PAGEUP }, { PAD_R2, KEY_PAGEDOWN }, { PAD_L3, KEY_SPACE }, { PAD_R3, KEY_TAB },
};

static int l1Held, l1Sped; /* L1 held; and used to speed the pointer up meanwhile (then it isn't Z when let go) */

/* The controller read once more: what changed since, queued as the game's own presses and clicks. */
static void ps2_controller_read(void)
{
    static unsigned held;
    unsigned buttons; int lx, ly, rx, ry;
    if (!console_pad_read(&buttons, &lx, &ly, &rx, &ry)) return;
    unsigned changed = buttons ^ held;
    held = buttons;
#ifdef PS2_DIAG_SHOTS
    { static int lastRX = 128, lastRY = 128;
      if (changed || abs(rx - lastRX) > 40 || abs(ry - lastRY) > 40) { printf("Game: Pad: buttons %04X, left %d,%d right %d,%d\n", buttons, lx, ly, rx, ry); lastRX = rx; lastRY = ry; } }
#endif
    /* Cross: the left button - but on Ultima 6's menu and scenes, Enter (they choose with the keyboard: the highlighted line,
       moved with the arrows, Enter takes it; a click only where the pointer is). Let go: as it was pressed. */
    static int crossIsEnter;
    if (changed & PAD_CROSS)
    {
        if (buttons & PAD_CROSS) crossIsEnter = u6_on() && (u6_title_on() || u6_scene_on());
        if (crossIsEnter) push(key_event(buttons & PAD_CROSS ? EV_KEY_DOWN : EV_KEY_UP, KEY_ENTER));
        else push(at_pointer(buttons & PAD_CROSS ? EV_MOUSE_DOWN : EV_MOUSE_UP, 1));
    }
    if (changed & PAD_CIRCLE) push(at_pointer(buttons & PAD_CIRCLE ? EV_MOUSE_DOWN : EV_MOUSE_UP, 2)); /* the right button */
    if (changed & buttons & PAD_SQUARE) /* a double click */
    { push(at_pointer(EV_MOUSE_DOWN, 1)); push(at_pointer(EV_MOUSE_UP, 1)); push(at_pointer(EV_MOUSE_DOWN, 1)); push(at_pointer(EV_MOUSE_UP, 1)); }
    for (size_t i = 0; i < sizeof BUTTON_KEYS / sizeof BUTTON_KEYS[0]; i++)
        if (changed & BUTTON_KEYS[i].bit) push(key_event(buttons & BUTTON_KEYS[i].bit ? EV_KEY_DOWN : EV_KEY_UP, BUTTON_KEYS[i].key));
    /* L1: Z (stats) when pressed on its own - held while the pointer moves, the pointer goes faster instead */
    if (changed & PAD_L1)
    {
        if (buttons & PAD_L1) { l1Held = 1; l1Sped = 0; }
        else { l1Held = 0; if (!l1Sped) { push(key_event(EV_KEY_DOWN, KEY_Z)); push(key_event(EV_KEY_UP, KEY_Z)); } }
    }
    /* the sticks: 0..255 from the controller, as SDL's -32768..32767 */
    stickLX = (lx - 128) * 256; stickLY = (ly - 128) * 256; stickRX = (rx - 128) * 256; stickRY = (ry - 128) * 256;
    /* The left stick: the arrow keys, pushed past a third of the way. */
    int want[4] = { stickLX < -11000, stickLX > 11000, stickLY < -11000, stickLY > 11000 };
    static const enum PlatformKey keys[4] = { KEY_LEFT, KEY_RIGHT, KEY_UP, KEY_DOWN };
    for (int i = 0; i < 4; i++)
        if (want[i] != heldArrow[i]) { heldArrow[i] = want[i]; push(key_event(want[i] ? EV_KEY_DOWN : EV_KEY_UP, keys[i])); }
}

static int ps2_controller_event(const SDL_Event *e, PlatformEvent *event) { (void)e; (void)event; return 0; } /* the controller: not SDL's */

/* Queued presses first; then the pointer moved by the right stick: faster the further it's pushed, and the longer - gently at
   first (to land it exactly), quickly once it's been held a moment (across the screen in about a second); L1 held: faster still. */
static int ps2_controller_poll(PlatformEvent *event)
{
    if (queueAt >= queued) { queueAt = queued = 0; ps2_controller_read(); } /* read again only once all it gave is taken */
    if (queueAt < queued) { *event = queue[queueAt++]; if (queueAt == queued) queueAt = queued = 0; return 1; }
    uint32_t now = SDL_GetTicks();
    double seconds = (now - lastMove) / 1000.0;
    lastMove = now;
    static double pushedFor;
    int rx = abs(stickRX) > 6000 ? stickRX : 0, ry = abs(stickRY) > 6000 ? stickRY : 0;
    if (!rx && !ry) { pushedFor = 0; return 0; }
    if (seconds > 0.1) seconds = 0.1; /* a long frame doesn't throw it across the screen */
    pushedFor += seconds;
    double ramp = pushedFor >= 0.5 ? 1 : pushedFor / 0.5;
    double speed = 60 + 240 * ramp * ramp; /* game pixels a second at full tilt: 60, rising to 300 */
    if (l1Held) { speed *= 2; l1Sped = 1; }
    double nx = pointerX + rx / 32767.0 * speed * seconds, ny = pointerY + ry / 32767.0 * speed * seconds;
    if (nx < 0) nx = 0;
    if (ny < 0) ny = 0;
    if (nx > SCREEN_W - 1) nx = SCREEN_W - 1;
    if (ny > SCREEN_H - 1) ny = SCREEN_H - 1;
    int moved = (int)nx != (int)pointerX || (int)ny != (int)pointerY;
    pointerX = nx; pointerY = ny;
    if (!moved) return 0;
    *event = at_pointer(EV_MOUSE_MOVE, 0);
    return 1;
}
#endif
