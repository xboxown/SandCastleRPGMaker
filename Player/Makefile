# The Sandcastle player for Linux and Mac (SDL2).
#   Ubuntu:  sudo apt install build-essential libsdl2-dev   then   make
#   Mac:     brew install sdl2                               then   make
# Run:  ./SandcastlePlayer <the game's data folder>   (default: Game_Data beside it)
CC ?= cc
CFLAGS ?= -std=c99 -O2 -Wall -Wno-unused-function
SRC = src/main.c src/world.c src/pictures.c src/font.c src/dialog.c src/events.c src/items.c src/script.c src/lighting.c src/schedules.c src/use.c src/sound.c src/weather.c src/conditions.c src/screen.c src/vitals.c src/combat.c src/errors.c src/gumps.c src/dolls.c src/save.c src/magic.c src/mechanisms.c src/pack.c src/numbers.c src/platform_sdl2.c third_party/cJSON.c

SandcastlePlayer: $(SRC) src/*.h
	$(CC) $(CFLAGS) $(shell sdl2-config --cflags) -o $@ $(SRC) $(shell sdl2-config --libs) -lm

clean:
	rm -f SandcastlePlayer
