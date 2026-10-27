/* Ultima VII's number slider (Editor/NumberSliderControl.cs, GameTestWindow.ShowNumberSlider): System Settings > Slider. */
#ifndef SANDCASTLE_SLIDER_H
#define SANDCASTLE_SLIDER_H

#include <stdint.h>

/* The slider over the game with its question above it; done(1, value) when accepted (check mark or Enter), done(0) on Escape / Cancel. */
void slider_open(const char *question, int min, int max, int step, int value, void (*done)(int ok, int value, void *context), void *context);
int slider_open_now(void);                 /* up: the game waits */
int slider_key(int key);                   /* Left / Right step, Enter accepts, Escape cancels: 1 = taken (any key while it's up) */
int slider_mouse_down(int x, int y);       /* 1 = taken (any click while it's up) */
void slider_mouse_move(int x, int y);
void slider_mouse_up(int x, int y);
void slider_draw(uint32_t *pixels, uint32_t ms);
void slider_reset(void);

#endif
