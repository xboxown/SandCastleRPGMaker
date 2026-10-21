/*
 * The platform layer on SDL2: Windows (testing), Linux, Mac, MorphOS and
 * AmigaOS 4. A window that scales the 320 x 200 frame without blurring
 * (whole pixels, as the Windows engine's nearest-neighbour scaling).
 */
#include "platform.h"
#include <SDL.h>

static SDL_Window *window;
static SDL_Renderer *renderer;
static SDL_Texture *frame;
static char error_text[256];

const char *platform_error(void) { return error_text; }

int platform_open(const char *title, int fullscreen)
{
    if (SDL_Init(SDL_INIT_VIDEO | SDL_INIT_TIMER | SDL_INIT_EVENTS) != 0)
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

void platform_message(const char *heading, const char *text) { SDL_ShowSimpleMessageBox(SDL_MESSAGEBOX_ERROR, heading, text, window); }
