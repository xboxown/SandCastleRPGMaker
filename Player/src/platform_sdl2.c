/*
 * The platform layer on SDL2: Windows (testing), Linux, Mac, MorphOS and
 * AmigaOS 4. A window that scales the 320 x 200 frame without blurring
 * (whole pixels, as the Windows engine's nearest-neighbour scaling).
 */
#include "platform.h"
#include <errno.h>
#include <SDL.h>

static SDL_Window *window;
static SDL_Renderer *renderer;
static SDL_Texture *frame;
static char error_text[256];

const char *platform_error(void) { return error_text; }

int platform_open(const char *title, int fullscreen)
{
    if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_TIMER | SDL_INIT_EVENTS | SDL_INIT_AUDIO) != 0)
    {
        SDL_snprintf(error_text, sizeof error_text, "SDL couldn't start: %s", SDL_GetError());
        return 0;
    }
    SDL_SetHint(SDL_HINT_RENDER_SCALE_QUALITY, "nearest");
    window = SDL_CreateWindow(title, SDL_WINDOWPOS_CENTERED, SDL_WINDOWPOS_CENTERED, SCREEN_W * 3, SCREEN_H * 3,
                              SDL_WINDOW_RESIZABLE | (fullscreen ? SDL_WINDOW_FULLSCREEN_DESKTOP : 0));
    if (!window) { SDL_snprintf(error_text, sizeof error_text, "No window: %s", SDL_GetError()); return 0; }
    renderer = SDL_CreateRenderer(window, -1, SDL_RENDERER_PRESENTVSYNC);
    if (!renderer) renderer = SDL_CreateRenderer(window, -1, 0);
    if (!renderer) { SDL_snprintf(error_text, sizeof error_text, "No renderer: %s", SDL_GetError()); return 0; }
    SDL_RenderSetLogicalSize(renderer, SCREEN_W, SCREEN_H);
    frame = SDL_CreateTexture(renderer, SDL_PIXELFORMAT_ARGB8888, SDL_TEXTUREACCESS_STREAMING, SCREEN_W, SCREEN_H);
    if (!frame) { SDL_snprintf(error_text, sizeof error_text, "No texture: %s", SDL_GetError()); return 0; }
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

int platform_poll(PlatformEvent *event)
{
    SDL_Event e;
    while (SDL_PollEvent(&e))
    {
        event->type = EV_NONE;
        switch (e.type)
        {
            case SDL_QUIT: event->type = EV_QUIT; return 1;
            case SDL_KEYDOWN:
                if (e.key.repeat) break;
                event->type = EV_KEY_DOWN; event->key = key_of(e.key.keysym.sym); return 1;
            case SDL_KEYUP:
                event->type = EV_KEY_UP; event->key = key_of(e.key.keysym.sym); return 1;
            case SDL_MOUSEBUTTONDOWN:
            case SDL_MOUSEBUTTONUP:
                event->type = e.type == SDL_MOUSEBUTTONDOWN ? EV_MOUSE_DOWN : EV_MOUSE_UP;
                event->button = e.button.button == SDL_BUTTON_RIGHT ? 2 : 1;
                event->mouseX = e.button.x; event->mouseY = e.button.y; /* logical size: already in game pixels */
                return 1;
            case SDL_TEXTINPUT:
                if ((unsigned char)e.text.text[0] < 32 || (unsigned char)e.text.text[0] > 126) break; /* letters typed (a paperdoll's text box) */
                event->type = EV_TEXT; event->ch = e.text.text[0]; return 1;
            case SDL_MOUSEMOTION:
                event->type = EV_MOUSE_MOVE; event->mouseX = e.motion.x; event->mouseY = e.motion.y; return 1;
            default: break;
        }
    }
    return 0;
}

void platform_present(const uint32_t *pixels)
{
    SDL_UpdateTexture(frame, NULL, pixels, SCREEN_W * 4);
    SDL_SetRenderDrawColor(renderer, 0, 0, 0, 255);
    SDL_RenderClear(renderer);
    SDL_RenderCopy(renderer, frame, NULL, NULL);
    SDL_RenderPresent(renderer);
}

uint32_t platform_ticks(void) { return SDL_GetTicks(); }
void platform_sleep(uint32_t ms) { SDL_Delay(ms); }

void platform_toggle_fullscreen(void)
{
    Uint32 flags = SDL_GetWindowFlags(window);
    SDL_SetWindowFullscreen(window, (flags & SDL_WINDOW_FULLSCREEN_DESKTOP) ? 0 : SDL_WINDOW_FULLSCREEN_DESKTOP);
}

long platform_free_memory_mb(void) { return SDL_GetSystemRAM(); }

int platform_can_run(void) { return 1; }

void platform_message(const char *heading, const char *text) { SDL_ShowSimpleMessageBox(SDL_MESSAGEBOX_ERROR, heading, text, window); }

/* ---------------- Sound ---------------- */

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

/* ---------------- Folders (the saves) ---------------- */
#ifdef _WIN32
#include <direct.h>
int platform_make_dir(const char *path) { return _mkdir(path) == 0 || errno == EEXIST; }
int platform_remove_dir(const char *path) { return _rmdir(path) == 0; }
#else
#include <sys/stat.h>
#include <unistd.h>
int platform_make_dir(const char *path) { return mkdir(path, 0755) == 0 || errno == EEXIST; }
int platform_remove_dir(const char *path) { return rmdir(path) == 0; }
#endif
