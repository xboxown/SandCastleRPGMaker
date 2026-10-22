#include "pictures.h"
#include "pack.h"
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define STB_IMAGE_IMPLEMENTATION
#define STBI_ONLY_PNG
#define STBI_ONLY_BMP
#define STBI_ONLY_GIF
#define STBI_ONLY_JPEG
#include "../third_party/stb_image.h"

static Sprite *sprites;
static int spriteCount;

static char *copy_text(const char *s)
{
    size_t n = strlen(s ? s : "");
    char *c = (char *)malloc(n + 1);
    memcpy(c, s ? s : "", n + 1);
    return c;
}

char *read_whole_file(const char *path, long *length)
{
    FILE *f = fopen(path, "rb");
    if (!f) return NULL;
    fseek(f, 0, SEEK_END);
    long n = ftell(f);
    fseek(f, 0, SEEK_SET);
    char *data = (char *)malloc((size_t)n + 1);
    if (data && fread(data, 1, (size_t)n, f) != (size_t)n) { free(data); data = NULL; }
    fclose(f);
    if (data) { data[n] = 0; if (length) *length = n; }
    return data;
}

static Archive *pictureArchive;

int sprites_load(const char *folder)
{
    char path[1100];
    snprintf(path, sizeof path, "%s/sprites.sdbx", folder);
    char *text = (char *)pack_read(path, NULL);
    if (!text) return 0;
    cJSON *root = cJSON_Parse(text);
    free(text);
    snprintf(path, sizeof path, "%s/pictures.sdbx", folder);
    pictureArchive = pack_archive_open(path);
    if (!root) return 0;
    cJSON *groups = cJSON_GetObjectItem(root, "Groups"), *group, *sprite, *frame;
    int total = 0;
    cJSON_ArrayForEach(group, groups) total += cJSON_GetArraySize(cJSON_GetObjectItem(group, "Sprites"));
    sprites = (Sprite *)calloc((size_t)(total > 0 ? total : 1), sizeof(Sprite));
    cJSON_ArrayForEach(group, groups)
    {
        const char *groupName = cJSON_GetStringValue(cJSON_GetObjectItem(group, "Name"));
        cJSON_ArrayForEach(sprite, cJSON_GetObjectItem(group, "Sprites"))
        {
            Sprite *s = &sprites[spriteCount++];
            s->group = copy_text(groupName);
            s->name = copy_text(cJSON_GetStringValue(cJSON_GetObjectItem(sprite, "Name")));
            s->loop = cJSON_IsTrue(cJSON_GetObjectItem(sprite, "Loop"));
            cJSON *nine = cJSON_GetObjectItem(sprite, "NineSlice");
            if (cJSON_IsObject(nine))
            {
                s->nineSlice = 1;
                s->sliceLeft = cJSON_GetObjectItem(nine, "Left")->valueint; s->sliceTop = cJSON_GetObjectItem(nine, "Top")->valueint;
                s->sliceRight = cJSON_GetObjectItem(nine, "Right")->valueint; s->sliceBottom = cJSON_GetObjectItem(nine, "Bottom")->valueint;
            }
            cJSON *frames = cJSON_GetObjectItem(sprite, "Frames");
            s->frameCount = cJSON_GetArraySize(frames);
            s->frames = (Frame *)calloc((size_t)(s->frameCount > 0 ? s->frameCount : 1), sizeof(Frame));
            int i = 0;
            cJSON_ArrayForEach(frame, frames)
            {
                s->frames[i].file = copy_text(cJSON_GetStringValue(cJSON_GetObjectItem(frame, "File")));
                s->frames[i].ms = cJSON_GetObjectItem(frame, "Ms")->valueint;
                cJSON *sound = cJSON_GetObjectItem(frame, "Sound");
                s->frames[i].sound = cJSON_IsNumber(sound) ? sound->valueint : -1;
                s->totalMs += s->frames[i].ms;
                i++;
            }
        }
    }
    cJSON_Delete(root);
    return 1;
}

const Sprite *sprite_find(const char *group, const char *name)
{
    if (!name || !*name) return NULL;
    for (int i = 0; i < spriteCount; i++)
        if ((!group || !*group || strcmp(sprites[i].group, group) == 0) && strcmp(sprites[i].name, name) == 0) return &sprites[i];
    for (int i = 0; i < spriteCount; i++)
        if (strcmp(sprites[i].name, name) == 0) return &sprites[i];
    return NULL;
}

/* A dialog frame by name (SpriteDatabase.FindDialogFrame): "None" = none, else that sprite of the group, else the group's first. */
const Sprite *sprite_dialog_frame(const char *name)
{
    if (name && strcmp(name, "None") == 0) return NULL;
    const Sprite *first = NULL;
    for (int i = 0; i < spriteCount; i++)
    {
        if (strcmp(sprites[i].group, "DialogFrame") != 0) continue;
        if (!first) first = &sprites[i];
        if (name && strcmp(sprites[i].name, name) == 0) return &sprites[i];
    }
    return first;
}

const Picture *frame_picture(const Frame *frame)
{
    Frame *f = (Frame *)frame;
    if (f->picture || !f->file || !*f->file) return f->picture;
    long size;
    unsigned char *file = pack_archive_read(pictureArchive, f->file, &size);
    if (!file) return NULL;
    int w, h, n;
    unsigned char *rgba = stbi_load_from_memory(file, (int)size, &w, &h, &n, 4);
    free(file);
    if (!rgba) return NULL;
    Picture *p = (Picture *)malloc(sizeof(Picture));
    p->width = w;
    p->height = h;
    p->pixels = (uint32_t *)malloc((size_t)w * (size_t)h * 4);
    for (int i = 0; i < w * h; i++)
        p->pixels[i] = ((uint32_t)rgba[i * 4 + 3] << 24) | ((uint32_t)rgba[i * 4] << 16) | ((uint32_t)rgba[i * 4 + 1] << 8) | rgba[i * 4 + 2];
    stbi_image_free(rgba);
    f->picture = p;
    return p;
}

const Picture *sprite_picture_at(const Sprite *sprite, uint32_t ms)
{
    if (!sprite || sprite->frameCount == 0) return NULL;
    if (sprite->frameCount == 1 || sprite->totalMs <= 0) return frame_picture(&sprite->frames[0]);
    uint32_t t = ms % (uint32_t)sprite->totalMs;
    for (int i = 0; i < sprite->frameCount; i++)
    {
        if (t < (uint32_t)sprite->frames[i].ms) return frame_picture(&sprite->frames[i]);
        t -= (uint32_t)sprite->frames[i].ms;
    }
    return frame_picture(&sprite->frames[0]);
}

int sprite_frame_at(const Sprite *sprite, uint32_t ms)
{
    if (!sprite || sprite->frameCount <= 1 || sprite->totalMs <= 0) return 0;
    uint32_t t = ms % (uint32_t)sprite->totalMs;
    for (int i = 0; i < sprite->frameCount; i++)
    {
        if (t < (uint32_t)sprite->frames[i].ms) return i;
        t -= (uint32_t)sprite->frames[i].ms;
    }
    return 0;
}

/* A picture from a file on disk (a save's screenshot); NULL if it can't be read. Freed with picture_free. */
Picture *picture_from_file(const char *path)
{
    FILE *f = fopen(path, "rb");
    if (!f) return NULL;
    fseek(f, 0, SEEK_END);
    long size = ftell(f);
    fseek(f, 0, SEEK_SET);
    unsigned char *file = (unsigned char *)malloc((size_t)(size > 0 ? size : 1));
    int ok = file && fread(file, 1, (size_t)size, f) == (size_t)size;
    fclose(f);
    if (!ok) { free(file); return NULL; }
    int w, h, n;
    unsigned char *rgba = stbi_load_from_memory(file, (int)size, &w, &h, &n, 4);
    free(file);
    if (!rgba) return NULL;
    Picture *p = (Picture *)malloc(sizeof(Picture));
    p->width = w; p->height = h;
    p->pixels = (uint32_t *)malloc((size_t)w * (size_t)h * 4);
    for (int i = 0; i < w * h; i++) p->pixels[i] = ((uint32_t)rgba[i * 4 + 3] << 24) | ((uint32_t)rgba[i * 4] << 16) | ((uint32_t)rgba[i * 4 + 1] << 8) | rgba[i * 4 + 2];
    stbi_image_free(rgba);
    return p;
}

void picture_free(Picture *p) { if (p) { free(p->pixels); free(p); } }
