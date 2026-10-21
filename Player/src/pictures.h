/*
 * Sprites and their pictures (the pack's sprites.json and pictures/):
 * a sprite is a list of frames, each a picture with a time; a tile is frame
 * N of its tileset sprite. Pictures are decoded when first drawn and kept.
 */
#ifndef SANDCASTLE_PICTURES_H
#define SANDCASTLE_PICTURES_H

#include <stdint.h>

typedef struct Picture
{
    int width, height;
    uint32_t *pixels; /* 0xAARRGGBB */
} Picture;

typedef struct Frame
{
    char *file;
    int ms;
    int sound;        /* a sound played when this frame comes round (-1: none) */
    Picture *picture; /* decoded on first use */
} Frame;

typedef struct Sprite
{
    char *group, *name;
    int loop;
    int nineSlice, sliceLeft, sliceTop, sliceRight, sliceBottom; /* a dialog frame's borders that don't stretch */
    int frameCount, totalMs;
    Frame *frames;
} Sprite;

/* Reads <folder>/sprites.json; 0 on failure. */
int sprites_load(const char *folder);

/* A sprite by its group and name (else the first of that name in any group, as the engine finds them). */
const Sprite *sprite_find(const char *group, const char *name);

/* A dialog frame by name: "None" = none (NULL), a name in the DialogFrame group, else its first sprite (NULL if the group is empty). */
const Sprite *sprite_dialog_frame(const char *name);

/* A frame's picture (decoded on first use; NULL if missing). */
const Picture *frame_picture(const Frame *frame);

/* Which frame shows at a time (ms) (as sprite_picture_at picks it). */
int sprite_frame_at(const Sprite *sprite, uint32_t ms);

/* The picture to show at a time (ms) for an animation; its first frame when not looping and past the end. */
const Picture *sprite_picture_at(const Sprite *sprite, uint32_t ms);

#endif
