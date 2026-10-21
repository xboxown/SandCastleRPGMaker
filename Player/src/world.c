/*
 * The world, as the Windows engine has it (GameTestWindow), in C:
 *  - Units: map cells are tiles (32 x 32 pixels); things stand on 8-pixel
 *    units (Exult's tiles) and lifts (4 pixels each; a floor is 5 lifts).
 *  - Drawing: the ground's tiles, then everything else ordered by floor,
 *    then by how far toward the viewer (x + y) and its lift; a thing is drawn
 *    up and to the left by 4 pixels a lift (Exult's lift shift); pictures sit
 *    on their cell's bottom, centred, fitted into a box rounded up to 8 pixels.
 *  - Walking: Exult's step (Chunk_cache::is_blocked): each unit under the new
 *    footprint free at the lift or one above, or a floor at most one below;
 *    headroom for the whole walker; the ground itself walkable at lift 0.
 *  - Inside: with something solid over your head, everything from its
 *    height up isn't drawn (Exult's roofs).
 *  - The camera: a dead zone in the middle, following where the player is
 *    drawn, held inside the map.
 */
#include "world.h"
#include "pictures.h"
#include "font.h"
#include "platform.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

char *read_whole_file(const char *path, long *length);

#define UNIT 8
#define LIFT_SHIFT 4
#define LIFTS_PER_FLOOR 5
#define MAX_FLOORS 9
#define TILES_PER_SECOND 4.5
#define DEAD_HALF_W 60
#define DEAD_HALF_H 40

enum { T_JUNK, T_TILE, T_WALL, T_DOOR, T_LIGHT, T_AMMO, T_MONEY, T_CHAIR, T_BED, T_TRAP };
enum { DIR_N, DIR_NE, DIR_E, DIR_SE, DIR_S, DIR_SW, DIR_W, DIR_NW };
static const char *DIR_NAMES[8] = { "North", "NorthEast", "East", "SouthEast", "South", "SouthWest", "West", "NorthWest" };

typedef struct Link { const Sprite *sprite; } Link;

typedef struct ObjectDef
{
    int id, type, sizeX, sizeY, sizeZ, solid, solidOffX, solidOffY, stackable, elevates, floorTile;
    const Sprite *own, *open, *lit;
    const Sprite *facing[8], *turned[8];
    int doorStartsOpen, lightStartsLit, occludes;
    int doorStartsLocked, openOffX, openOffY;
    int brightness, gives;            /* a light: how bright, what it gives (Exult levels, the dusk look, daylight) */
} ObjectDef;

typedef struct Thing
{
    const ObjectDef *def;            /* an object; NULL for a placed book or container */
    const Sprite *sprite;            /* a book's or container's picture */
    int x, y, z, offX, offY, facing, open, lit;
    int sizeX, sizeY, sizeZ, solid, solidOffX, solidOffY, hangs;
    int uid, category, itemId, quantity, instanceId; /* what it is (ReferencedItemCategory: Book 3, Object 4, Container 5) */
    int body, gone;                                  /* its solid box (-1: none); picked up / taken away */
    int locked;                                      /* a door: locked (IsLockedRuntime) */
} Thing;

typedef struct Body { int left, top, width, depth, z, height; } Body;

#define MAX_POSES 48

typedef struct Pose { char name[48]; const Sprite *dirs[8]; } Pose;

typedef struct Actor
{
    double px, py; int lift, facing;
    int sizeX, sizeY, sizeZ;
    const Sprite *idle[8], *walking[8];
    int walkingNow;
    int characterId, body;          /* body: its solid box in bodies[] (-1 for the player) */
    int human;                      /* Exult: only the sentient open doors */
    const Sprite *face;
    Pose poses[MAX_POSES]; int poseCount;
    int shownPose;                  /* Change Pose: the pose kept (index into poses, -1 = idle / walking) */
    uint32_t poseSince;             /* when it began (its animation from the start) */
} Actor;

static char error_text[512], title[256], mapName[128];
static int mapW, mapH, tileW = 32, tileH = 32;
static int *floors[MAX_FLOORS + 1]; /* floors[1] = the ground; NULL = none painted */
static char sheetNames[8][128]; static int sheetCount;
static unsigned char *walkable; /* per ground cell */
static ObjectDef *defs; static int defCount;
static Thing *things; static int thingCount;
static Body *bodies; static int bodyCount;
static Actor player;
static Actor *npcs; static int npcCount;
static double cameraX, cameraY;
static int unitsX = 4, unitsY = 4;

static cJSON *gameJson;
void *world_json(void) { return gameJson; }
const char *world_error(void) { return error_text; }
const char *world_title(void) { return title; }

/* ---------------- Reading ---------------- */

static cJSON *get(cJSON *o, const char *k) { return cJSON_GetObjectItem(o, k); }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }
static const Sprite *link_of(cJSON *o) { return o ? sprite_find(str(o, "SpriteGroup"), str(o, "SpriteName")) : NULL; }
static int dir_of(const char *name) { for (int i = 0; i < 8; i++) if (strcmp(DIR_NAMES[i], name) == 0) return i; return DIR_S; }

static const ObjectDef *def_of(int id) { for (int i = 0; i < defCount; i++) if (defs[i].id == id) return &defs[i]; return NULL; }

static void read_objects(cJSON *project)
{
    cJSON *list = get(get(project, "Objects"), "Objects"), *o;
    defs = (ObjectDef *)calloc((size_t)cJSON_GetArraySize(list) + 1, sizeof(ObjectDef));
    cJSON_ArrayForEach(o, list)
    {
        ObjectDef *d = &defs[defCount++];
        d->id = num(o, "Id", -1);
        d->type = num(o, "Type", 0);
        d->sizeX = num(o, "SizeX", 1); d->sizeY = num(o, "SizeY", 1); d->sizeZ = num(o, "SizeZ", 1);
        d->solid = flag(o, "IsSolid");
        d->solidOffX = num(o, "SolidOffsetX", 0); d->solidOffY = num(o, "SolidOffsetY", 0);
        d->stackable = flag(o, "IsCellStackable"); d->elevates = flag(o, "AllowCellElevation");
        d->floorTile = flag(o, "IsFloorTile");
        d->occludes = flag(o, "Occludes");
        d->own = sprite_find(str(o, "SpriteGroup"), str(o, "SpriteName"));
        cJSON *door = get(o, "Door"), *light = get(o, "Light"), *furniture = get(get(o, "Furniture"), "Facings"), *turn = get(o, "TurnLooks"), *f;
        d->open = link_of(get(door, "OpenSprite")); d->doorStartsOpen = flag(door, "StartsOpen");
        d->doorStartsLocked = flag(door, "StartsLocked"); d->openOffX = num(door, "OpenOffsetX", 0); d->openOffY = num(door, "OpenOffsetY", 0);
        d->lit = link_of(get(light, "LitSprite")); d->lightStartsLit = flag(light, "StartsLit");
        d->brightness = num(light, "Brightness", 0); d->gives = num(light, "Gives", 0);
        cJSON_ArrayForEach(f, furniture) d->facing[dir_of(f->string)] = link_of(get(f, "Sprite"));
        cJSON_ArrayForEach(f, turn) d->turned[dir_of(f->string)] = link_of(f);
    }
}

/* ObjectLooks.SpriteFor: its look for its state and facing. */
static const Sprite *look_of(const Thing *t)
{
    const ObjectDef *d = t->def;
    if (!d) return t->sprite;
    const Sprite *own = d->turned[t->facing] ? d->turned[t->facing] : d->own;
    switch (d->type)
    {
        case T_DOOR: return t->open && d->open ? d->open : own;
        case T_LIGHT: return t->lit && d->lit ? d->lit : own;
        case T_CHAIR: case T_BED:
            if (d->facing[t->facing]) return d->facing[t->facing];
            if (d->facing[DIR_S]) return d->facing[DIR_S];
            for (int i = 0; i < 8; i++) if (d->facing[i]) return d->facing[i];
            return own;
        default: return own;
    }
}

static void thing_box(const Thing *t, Body *b)
{
    b->left = t->x * unitsX + (int)floor((unitsX - t->sizeX) / 2.0) + t->offX + t->solidOffX; /* FootprintAt */
    b->top = (t->y + 1) * unitsY - t->sizeY + t->offY + t->solidOffY;
    b->width = t->sizeX; b->depth = t->sizeY;
    b->z = t->z; b->height = t->sizeZ;
    if (t->def && t->def->type == T_TILE && t->def->solid && t->def->floorTile && t->z >= LIFTS_PER_FLOOR && t->z % LIFTS_PER_FLOOR == 0) { b->z = t->z - 1; b->height = 1; } /* an upper floor's floor tile: a slab */
}

static int nextUid = 1, thingCap, bodyCap;
static cJSON *containersJson, *booksJson;

static void add_thing_body(Thing *t)
{
    t->body = -1;
    int door = t->def && t->def->type == T_DOOR && t->def->solid;
    if ((!t->solid && !door) || t->hangs) return;
    t->body = bodyCount;
    if (door && !t->solid) { Body *b = &bodies[bodyCount++]; memset(b, 0, sizeof *b); b->z = -100000; return; } /* open: no box till it closes */
    thing_box(t, &bodies[bodyCount++]);
}

static void read_actor_poses(cJSON *character, cJSON *poses, Actor *a)
{
    int idleId = -1, walkingId = -1;
    cJSON *p;
    a->characterId = num(character, "Id", -1);
    a->human = !cJSON_IsFalse(get(character, "IsHuman"));
    a->face = sprite_find(str(character, "FaceSpriteGroup"), str(character, "FaceSpriteName"));
    if (!str(character, "FaceSpriteName")[0]) a->face = NULL;
    a->shownPose = -1;
    a->body = -1;
    /* Every pose it has a sprite for, by the pose's name (Change Pose, Play Pose). */
    cJSON *assigned;
    cJSON_ArrayForEach(assigned, get(character, "PoseSprites"))
    {
        if (a->poseCount >= MAX_POSES) break;
        const char *poseName = NULL;
        cJSON_ArrayForEach(p, get(poses, "Poses")) if (num(p, "Id", -2) == atoi(assigned->string)) poseName = str(p, "Name");
        if (!poseName) continue;
        Pose *pose = &a->poses[a->poseCount++];
        snprintf(pose->name, sizeof pose->name, "%s", poseName);
        const Sprite *south = NULL, *any = NULL;
        cJSON *d;
        cJSON_ArrayForEach(d, get(assigned, "Directions"))
        {
            const Sprite *sp = link_of(d);
            pose->dirs[dir_of(d->string)] = sp;
            if (dir_of(d->string) == DIR_S) south = sp;
            if (!any) any = sp;
        }
        for (int i = 0; i < 8; i++) if (!pose->dirs[i]) pose->dirs[i] = south ? south : any;
    }
    cJSON_ArrayForEach(p, get(poses, "Poses"))
    {
        if (strcmp(str(p, "Name"), "Idle") == 0) idleId = num(p, "Id", -1);
        if (strcmp(str(p, "Name"), "Walking") == 0) walkingId = num(p, "Id", -1);
    }
    char key[16];
    cJSON *sprites = get(character, "PoseSprites");
    for (int pass = 0; pass < 2; pass++)
    {
        snprintf(key, sizeof key, "%d", pass == 0 ? idleId : walkingId);
        cJSON *dirs = get(get(sprites, key), "Directions"), *d;
        const Sprite **into = pass == 0 ? a->idle : a->walking;
        const Sprite *south = NULL, *any = NULL;
        cJSON_ArrayForEach(d, dirs)
        {
            const Sprite *s = link_of(d);
            into[dir_of(d->string)] = s;
            if (dir_of(d->string) == DIR_S) south = s;
            if (!any) any = s;
        }
        for (int i = 0; i < 8; i++) if (!into[i]) into[i] = south ? south : any; /* PoseAssignment.Resolve */
    }
    /* ActorSizing: its own size, else from its idle picture (footprint = width / 16, height = what's left / 4). */
    a->sizeX = num(character, "SizeX", 0); a->sizeY = num(character, "SizeY", 0); a->sizeZ = num(character, "SizeZ", 0);
    if (a->sizeX <= 0 || a->sizeY <= 0 || a->sizeZ <= 0)
    {
        int fx = 1, fz = 4;
        const Picture *pic = a->idle[DIR_S] ? sprite_picture_at(a->idle[DIR_S], 0) : NULL;
        if (pic)
        {
            int w = (pic->width + UNIT - 1) / UNIT * UNIT, h = (pic->height + UNIT - 1) / UNIT * UNIT;
            fx = (int)fmax(1, floor(w / (2.0 * UNIT) + 0.5));
            fz = (int)fmax(1, floor((h - fx * UNIT) / 4.0 + 0.5));
        }
        if (a->sizeX <= 0) a->sizeX = fx;
        if (a->sizeY <= 0) a->sizeY = fx;
        if (a->sizeZ <= 0) a->sizeZ = fz;
    }
}

static int floor_of(int lift) { int f = lift / LIFTS_PER_FLOOR + 1; return f < 1 ? 1 : f > MAX_FLOORS ? MAX_FLOORS : f; }
static int base_lift(int floor) { return (floor - 1) * LIFTS_PER_FLOOR; }

/* ---------------- Exult's walking ---------------- */

static int overlaps(const Body *b, int l, int t, int w, int d) { return b->left < l + w && l < b->left + b->width && b->top < t + d && t < b->top + b->depth; }

/* The lift something standing on the footprint stands at: on the highest solid under it that starts below the next floor's slab. */
static int standing_lift(int l, int t, int w, int d, int floor)
{
    int ceilingFrom = base_lift(floor) + LIFTS_PER_FLOOR - 1, best = 0;
    for (int i = 0; i < bodyCount; i++)
        if (overlaps(&bodies[i], l, t, w, d) && bodies[i].z < ceilingFrom && bodies[i].z + bodies[i].height > best) best = bodies[i].z + bodies[i].height;
    return best;
}

static int is_clear(const Body **here, int n, int lift, int height)
{
    for (int i = 0; i < n; i++) if (here[i]->z < lift + height && lift < here[i]->z + here[i]->height) return 0;
    return 1;
}

static int floor_below(const Body **here, int n, int lift)
{
    int best = 0;
    for (int i = 0; i < n; i++) if (here[i]->z < lift) { int top = here[i]->z + here[i]->height; if (top > lift) top = lift; if (top > best) best = top; }
    return best;
}

static int step_unit(const Body **here, int n, int lift, int height, int *newLift)
{
    int l;
    for (l = lift; l <= lift + 1; l++) if (is_clear(here, n, l, height)) break;
    if (l > lift + 1)
    {
        l = floor_below(here, n, lift);
        if (l >= lift || !is_clear(here, n, l, height)) return 0;
    }
    if (l <= lift)
    {
        l = floor_below(here, n, lift);
        if (!is_clear(here, n, l, height)) return 0;
    }
    *newLift = l;
    return 1;
}

static int ground_walkable(int l, int t, int w, int d)
{
    for (int y = t; y < t + d; y++)
        for (int x = l; x < l + w; x++)
        {
            int cx = x / unitsX, cy = y / unitsY;
            if (cx < 0 || cy < 0 || cx >= mapW || cy >= mapH) return 0;
            if (!walkable[cy * mapW + cx]) return 0;
        }
    return 1;
}

/* TryStepActor for the player: the lift they'd stand at, or -1 if they can't. */
static int try_step(int l, int t, int w, int d, int lift, int height)
{
    if (l < 0 || t < 0 || l + w > mapW * unitsX || t + d > mapH * unitsY) return -1;
    static const Body *here[512], *solids[2048];
    int ns = 0;
    for (int i = 0; i < bodyCount && ns < 2048; i++) if (overlaps(&bodies[i], l, t, w, d)) solids[ns++] = &bodies[i];
    int highest = -1;
    for (int y = t; y < t + d; y++)
        for (int x = l; x < l + w; x++)
        {
            int n = 0;
            for (int i = 0; i < ns && n < 512; i++) if (overlaps(solids[i], x, y, 1, 1)) here[n++] = solids[i];
            int unitLift;
            if (!step_unit(here, n, lift, height, &unitLift)) return -1;
            if (unitLift > highest) highest = unitLift;
        }
    if (lift - highest > 1) return -1;
    if (!is_clear(solids, ns, highest, height)) return -1;
    if (highest == 0 && !ground_walkable(l, t, w, d)) return -1;
    return highest;
}

static void player_foot(double px, double py, int *l, int *t)
{
    double feetY = py + tileH / 2.0;
    *l = (int)floor((px - player.sizeX * UNIT / 2.0) / UNIT + 0.5);
    *t = (int)floor((feetY - player.sizeY * UNIT) / UNIT + 0.5);
}

/* ---------------- Loading ---------------- */

int world_load(const char *folder)
{
    if (!sprites_load(folder)) { snprintf(error_text, sizeof error_text, "Couldn't read %s/sprites.json.", folder); return 0; }
    char path[1100];
    snprintf(path, sizeof path, "%s/game.json", folder);
    char *text = read_whole_file(path, NULL);
    if (!text) { snprintf(error_text, sizeof error_text, "Couldn't read %s.", path); return 0; }
    cJSON *root = cJSON_Parse(text);
    free(text);
    if (!root) { snprintf(error_text, sizeof error_text, "game.json is damaged."); return 0; }
    cJSON *project = get(root, "Project"), *map = get(project, "Map");
    snprintf(title, sizeof title, "%s", str(get(project, "Manifest"), "ProjectName"));
    snprintf(mapName, sizeof mapName, "%s", str(get(project, "Map"), "MapName"));
    if (!map) { snprintf(error_text, sizeof error_text, "The game has no map."); return 0; }
    read_objects(project);

    mapW = num(map, "Width", 1); mapH = num(map, "Height", 1);
    cJSON *tileset = get(map, "Tileset"), *e;
    tileW = num(tileset, "TileWidth", 32); tileH = num(tileset, "TileHeight", 32);
    unitsX = tileW / UNIT > 0 ? tileW / UNIT : 1; unitsY = tileH / UNIT > 0 ? tileH / UNIT : 1;
    snprintf(sheetNames[0], sizeof sheetNames[0], "%s", str(tileset, "TilesetSpriteName"));
    sheetCount = 1;
    cJSON_ArrayForEach(e, get(tileset, "ExtraSheetNames")) if (sheetCount < 8) snprintf(sheetNames[sheetCount++], sizeof sheetNames[0], "%s", e->valuestring);
    int first = 1;
    cJSON *layer;
    cJSON_ArrayForEach(layer, get(map, "Layers"))
    {
        int floor = first ? 1 : num(layer, "Floor", 1);
        first = 0;
        if (floor < 1 || floor > MAX_FLOORS || floors[floor]) continue;
        floors[floor] = (int *)malloc(sizeof(int) * (size_t)(mapW * mapH));
        int i = 0;
        cJSON_ArrayForEach(e, get(layer, "TileIndices")) if (i < mapW * mapH) floors[floor][i++] = e->valueint;
        for (; i < mapW * mapH; i++) floors[floor][i] = -1;
    }
    /* The ground's walkable cells (Tile Properties). */
    walkable = (unsigned char *)malloc((size_t)(mapW * mapH));
    cJSON *byKey = get(get(project, "TileProperties"), "ByTileKey");
    for (int i = 0; i < mapW * mapH; i++)
    {
        walkable[i] = 1;
        int tile = floors[1] ? floors[1][i] : -1;
        if (tile < 0) continue;
        char key[200];
        int slot = tile / 100000;
        snprintf(key, sizeof key, "%s:%d", slot < sheetCount ? sheetNames[slot] : "", tile % 100000);
        cJSON *props = get(byKey, key);
        if (props && !flag(props, "Walkable")) walkable[i] = 0;
    }

    /* Things: objects on the map, placed books and containers. */
    cJSON *objects = get(map, "Objects"), *items = get(map, "Items"), *o;
    int capacity = cJSON_GetArraySize(objects) + cJSON_GetArraySize(items) + 1;
    thingCap = capacity + 64;
    bodyCap = capacity + 64 + mapW * mapH * MAX_FLOORS + cJSON_GetArraySize(get(map, "Characters"));
    things = (Thing *)calloc((size_t)thingCap, sizeof(Thing));
    bodies = (Body *)calloc((size_t)bodyCap, sizeof(Body));
    cJSON_ArrayForEach(o, objects)
    {
        if (num(o, "ContainerInstanceId", -1) >= 0) continue;
        const ObjectDef *d = def_of(num(o, "ObjectId", -1));
        if (!d) continue;
        Thing *t = &things[thingCount++];
        t->def = d;
        t->uid = nextUid++; t->category = 4; t->itemId = d->id; t->instanceId = num(o, "InstanceId", 0);
        t->quantity = num(o, "Quantity", 1) > 0 ? num(o, "Quantity", 1) : 1;
        t->x = num(o, "X", 0); t->y = num(o, "Y", 0); t->z = num(o, "Z", 0);
        t->offX = num(o, "OffsetX", 0); t->offY = num(o, "OffsetY", 0); t->facing = num(o, "Facing", DIR_S) & 7;
        cJSON *so = get(o, "StartsOpen"), *sl = get(o, "StartsLit"), *sk = get(o, "StartsLocked"), *lr = get(o, "IsLockedRuntime");
        t->locked = d->type == T_DOOR && (cJSON_IsBool(lr) ? cJSON_IsTrue(lr) : cJSON_IsBool(sk) ? cJSON_IsTrue(sk) : d->doorStartsLocked);
        t->open = d->type == T_DOOR && (cJSON_IsBool(so) ? cJSON_IsTrue(so) : d->doorStartsOpen);
        t->lit = d->type == T_LIGHT && (cJSON_IsBool(sl) ? cJSON_IsTrue(sl) : d->lightStartsLit);
        int turned = (d->turned[0] || d->turned[2] || d->turned[4] || d->turned[6]) && (t->facing == DIR_E || t->facing == DIR_W);
        t->sizeX = turned ? d->sizeY : d->sizeX; t->sizeY = turned ? d->sizeX : d->sizeY;
        int flat = d->type == T_TRAP || (d->type == T_TILE && !d->solid);
        t->sizeZ = flat ? 0 : d->sizeZ;
        t->solid = d->solid && !(d->type == T_DOOR && t->open);
        t->solidOffX = turned ? d->solidOffY : d->solidOffX; t->solidOffY = turned ? d->solidOffX : d->solidOffY;
        t->hangs = flag(o, "HangsOnWall");
        if (t->sizeX < 1) t->sizeX = 1;
        if (t->sizeY < 1) t->sizeY = 1;
        add_thing_body(t);
    }
    cJSON *containers = get(get(project, "Containers"), "Containers"), *books = get(get(root, "Books"), "Books");
    containersJson = containers; booksJson = books;
    cJSON_ArrayForEach(o, items)
    {
        int category = num(o, "Category", 0), id = num(o, "ItemId", -1);
        cJSON *data = NULL, *c;
        if (category != 5 && category != 3) continue; /* placed containers and books (ReferencedItemCategory) */
        cJSON *source = category == 5 ? containers : books;
        cJSON_ArrayForEach(c, source) if (num(c, "Id", -2) == id) data = c;
        if (!data) continue;
        Thing *t = &things[thingCount++];
        t->uid = nextUid++; t->category = category; t->itemId = id; t->quantity = 1; t->instanceId = num(o, "InstanceId", 0);
        t->sprite = sprite_find(str(data, "SpriteGroup"), str(data, "SpriteName"));
        t->x = num(o, "X", 0); t->y = num(o, "Y", 0); t->z = num(o, "Z", 0); t->offX = num(o, "OffsetX", 0); t->offY = num(o, "OffsetY", 0);
        t->sizeX = num(data, "SizeX", 1); t->sizeY = num(data, "SizeY", 1); t->sizeZ = num(data, "SizeZ", 1);
        t->solid = flag(data, "IsSolid"); t->solidOffX = num(data, "SolidOffsetX", 0); t->solidOffY = num(data, "SolidOffsetY", 0);
        t->hangs = flag(o, "HangsOnWall");
        add_thing_body(t);
    }
    /* Upper floors' painted tiles: slabs one lift thick under their floor's height (BuildFloorSlabs). */
    for (int f = 2; f <= MAX_FLOORS; f++)
        if (floors[f])
            for (int y = 0; y < mapH; y++)
                for (int x = 0; x < mapW; x++)
                    if (floors[f][y * mapW + x] >= 0)
                    {
                        Body *b = &bodies[bodyCount++];
                        b->left = x * unitsX; b->top = y * unitsY; b->width = unitsX; b->depth = unitsY;
                        b->z = base_lift(f) - 1; b->height = 1;
                    }

    /* The main player (the character marked as the test player) at the Start Position; the others where they're placed. */
    cJSON *characters = get(get(project, "Characters"), "Characters"), *poses = get(project, "Poses"), *ch;
    cJSON_ArrayForEach(ch, characters)
        if (flag(ch, "IsTestPlayer")) read_actor_poses(ch, poses, &player);
    int sx = num(map, "StartX", -1), sy = num(map, "StartY", -1), sf = num(map, "StartFloor", 1);
    if (sx < 0 || sy < 0) { sx = mapW / 2; sy = mapH / 2; }
    player.px = sx * tileW + tileW / 2.0; player.py = sy * tileH + tileH / 2.0; player.facing = DIR_S;
    int l, t;
    player_foot(player.px, player.py, &l, &t);
    player.lift = standing_lift(l, t, player.sizeX, player.sizeY, sf);
    cJSON *placed = get(map, "Characters");
    npcs = (Actor *)calloc((size_t)cJSON_GetArraySize(placed) + 1, sizeof(Actor));
    cJSON_ArrayForEach(o, placed)
    {
        if (cJSON_IsTrue(get(o, "Dead"))) continue;
        cJSON *who = NULL;
        cJSON_ArrayForEach(ch, characters) if (num(ch, "Id", -2) == num(o, "CharacterId", -1)) { who = ch; break; }
        if (!who || flag(who, "IsTestPlayer")) continue;
        Actor *a = &npcs[npcCount++];
        read_actor_poses(who, poses, a);
        a->px = num(o, "X", 0) * tileW + tileW / 2.0; a->py = num(o, "Y", 0) * tileH + tileH / 2.0;
        a->facing = num(o, "Facing", DIR_S) & 7;
        double feetY = a->py + tileH / 2.0;
        int al = (int)floor((a->px - a->sizeX * UNIT / 2.0) / UNIT + 0.5), at = (int)floor((feetY - a->sizeY * UNIT) / UNIT + 0.5);
        a->lift = standing_lift(al, at, a->sizeX, a->sizeY, num(o, "Floor", 1));
        a->body = bodyCount;
        Body *b = &bodies[bodyCount++]; /* solid, like Exult's actors */
        b->left = al; b->top = at; b->width = a->sizeX; b->depth = a->sizeY; b->z = a->lift; b->height = a->sizeZ;
    }
    gameJson = root; /* kept: the events run from it */
    cameraX = player.px - SCREEN_W / 2.0; cameraY = player.py - SCREEN_H / 2.0;
    return 1;
}

/* ---------------- Each frame ---------------- */

static int keysHeld;
int world_keys_held(void) { return keysHeld; }
static Actor *actor_of(int i);
void world_walks_update(double seconds);
void world_walk_cancel(int actor);
int world_walking(int actor);

void world_update(int dx, int dy, double seconds)
{
    keysHeld = dx || dy;
    if (dx || dy) world_walk_cancel(0); /* a direction key takes over from a walk an event began */
    if (world_walking(0)) { /* walked by the event (world_walks_update) */ }
    else if (dx == 0 && dy == 0) { player.walkingNow = 0; }
    else
    {
        player.facing = dx < 0 ? DIR_W : dx > 0 ? DIR_E : dy < 0 ? DIR_N : DIR_S;
        player.walkingNow = 1;
        player.shownPose = -1; /* walking on: the pose an event gave is over */
        double distance = TILES_PER_SECOND * tileW * seconds;
        double nx = player.px + dx * distance, ny = player.py + dy * distance;
        int l0, t0, l1, t1;
        player_foot(player.px, player.py, &l0, &t0);
        player_foot(nx, ny, &l1, &t1);
        if (l1 == l0 && t1 == t0) { player.px = nx; player.py = ny; }
        else
        {
            int lift = try_step(l1, t1, player.sizeX, player.sizeY, player.lift, player.sizeZ);
            if (lift >= 0) { player.px = nx; player.py = ny; player.lift = lift; }
        }
    }
    world_walks_update(seconds);
    /* The camera: a dead zone round the middle, following where the player is drawn (up and left by its lift). */
    double fx = player.px - player.lift * LIFT_SHIFT, fy = player.py - player.lift * LIFT_SHIFT;
    double rx = fx - cameraX, ry = fy - cameraY;
    if (rx < SCREEN_W / 2.0 - DEAD_HALF_W) cameraX -= SCREEN_W / 2.0 - DEAD_HALF_W - rx;
    else if (rx > SCREEN_W / 2.0 + DEAD_HALF_W) cameraX += rx - (SCREEN_W / 2.0 + DEAD_HALF_W);
    if (ry < SCREEN_H / 2.0 - DEAD_HALF_H) cameraY -= SCREEN_H / 2.0 - DEAD_HALF_H - ry;
    else if (ry > SCREEN_H / 2.0 + DEAD_HALF_H) cameraY += ry - (SCREEN_H / 2.0 + DEAD_HALF_H);
    double worldW = mapW * tileW, worldH = mapH * tileH;
    cameraX = worldW <= SCREEN_W ? (worldW - SCREEN_W) / 2 : fmin(fmax(cameraX, 0), worldW - SCREEN_W);
    cameraY = worldH <= SCREEN_H ? (worldH - SCREEN_H) / 2 : fmin(fmax(cameraY, 0), worldH - SCREEN_H);
}

/* ---------------- Drawing ---------------- */

static uint32_t *screen;

/* A picture fitted (keeping its shape, centred, nearest pixel) into a box at (x, y). */
static void blit_box(const Picture *p, int x, int y, int bw, int bh)
{
    if (!p || p->width <= 0 || p->height <= 0) return;
    double s = fmin((double)bw / p->width, (double)bh / p->height);
    int dw = (int)(p->width * s + 0.5), dh = (int)(p->height * s + 0.5);
    int ox = x + (bw - dw) / 2, oy = y + (bh - dh) / 2;
    for (int j = 0; j < dh; j++)
    {
        int sy = oy + j;
        if (sy < 0 || sy >= SCREEN_H) continue;
        const uint32_t *row = p->pixels + (size_t)(j * p->height / dh) * (size_t)p->width;
        for (int i = 0; i < dw; i++)
        {
            int sx = ox + i;
            if (sx < 0 || sx >= SCREEN_W) continue;
            uint32_t c = row[i * p->width / dw], a = c >> 24;
            if (a == 0) continue;
            uint32_t *d = &screen[sy * SCREEN_W + sx];
            if (a == 255) { *d = c; continue; }
            uint32_t r = (((c >> 16) & 255) * a + ((*d >> 16) & 255) * (255 - a)) / 255;
            uint32_t g = (((c >> 8) & 255) * a + ((*d >> 8) & 255) * (255 - a)) / 255;
            uint32_t b = ((c & 255) * a + (*d & 255) * (255 - a)) / 255;
            *d = 0xFF000000u | (r << 16) | (g << 8) | b;
        }
    }
}

static const Picture *tile_picture(int tile, uint32_t ms)
{
    if (tile < 0) return NULL;
    int slot = tile / 100000, local = tile % 100000;
    if (slot >= sheetCount) return NULL;
    const Sprite *sheet = sprite_find("", sheetNames[slot]);
    if (!sheet || local >= sheet->frameCount) return NULL;
    (void)ms;
    return frame_picture(&sheet->frames[local]);
}

/* What's drawn: its picture where it goes, and (for Exult's compare) its box - x and y in 8-pixel units, z in lifts. */
typedef struct Drawn
{
    double key; const Picture *picture; int x, y, w, h;
    int hasBox, wall, occludes, hangs, xl, xr, yf, yn, zb, zt, flat;
    int actor; /* the character drawn (world_actor_at), -1 for anything else */
} Drawn;

static int by_key(const void *a, const void *b)
{
    const Drawn *p = (const Drawn *)a, *q = (const Drawn *)b;
    return p->key < q->key ? -1 : p->key > q->key ? 1 : 0;
}

/* objs.cc Compare_ranges */
static void compare_ranges(int from1, int to1, int from2, int to2, int *cmp, int *overlap)
{
    if (to1 < from2) { *overlap = 0; *cmp = -1; }
    else if (to2 < from1) { *overlap = 0; *cmp = 1; }
    else { *overlap = 1; *cmp = from1 < from2 ? -1 : from1 > from2 ? 1 : to1 < to2 ? 1 : to1 > to2 ? -1 : 0; }
}

/* objs.cc Game_object::compare: -1 if a is drawn before b, 1 after, 0 either way. */
static int exult_compare(const Drawn *a, const Drawn *b)
{
    if (a->x >= b->x + b->w || b->x >= a->x + a->w || a->y >= b->y + b->h || b->y >= a->y + a->h) return 0;
    int xcmp, xover, ycmp, yover, zcmp, zover;
    compare_ranges(a->xl, a->xr, b->xl, b->xr, &xcmp, &xover);
    compare_ranges(a->yf, a->yn, b->yf, b->yn, &ycmp, &yover);
    compare_ranges(a->zb, a->zt, b->zb, b->zt, &zcmp, &zover);
    if (xcmp == 0 && ycmp == 0 && zcmp == 0) return a->w < b->w && a->h < b->h ? -1 : a->w > b->w && a->h > b->h ? 1 : 0;
    if (xover && yover && zover) { if (a->flat) return b->flat ? 0 : -1; if (b->flat) return 1; }
    if (xcmp >= 0 && ycmp >= 0 && zcmp >= 0) return 1;
    if (xcmp <= 0 && ycmp <= 0 && zcmp <= 0) return -1;
    if (yover)
    {
        if (xover) return zcmp;
        if (zover) return xcmp;
        if (zcmp == 0) return xcmp;
        if (xcmp == zcmp) return xcmp;
        if (a->zt / 5 < b->zb / 5 && b->occludes) return -1;
        if (b->zt / 5 < a->zb / 5 && a->occludes) return 1;
        return 0;
    }
    if (xover) { if (zover || zcmp == 0) return ycmp; return ycmp == zcmp ? ycmp : 0; }
    if (xcmp == -1) { if (ycmp == -1) return zover || zcmp <= 0 ? -1 : 0; }
    else if (ycmp == 1) { if (zover || zcmp >= 0) return 1; if (a->zt / 5 < b->zb / 5) return -1; return 0; }
    return 0;
}

static void set_box(Drawn *d, int left, int top, int width, int depth, int lift, int height)
{
    d->hasBox = 1;
    d->xl = left; d->xr = left + width - 1; d->yf = top; d->yn = top + depth - 1;
    d->zb = height == 0 ? lift - 1 : lift; d->zt = lift + (height > 1 ? height : 1) - 1; d->flat = height == 0;
}

static Drawn *drawn; static int drawnCap, drawnCount;
static uint32_t lastMs;

static Drawn *add_drawn(int floorBand, double depth, const Picture *pic, int x, int y, int w, int h, int *count)
{
    if (!pic) return NULL;
    if (x + w < 0 || y + h < 0 || x > SCREEN_W || y > SCREEN_H) return NULL;
    if (*count >= drawnCap) { drawnCap = drawnCap ? drawnCap * 2 : 1024; drawn = (Drawn *)realloc(drawn, sizeof(Drawn) * (size_t)drawnCap); }
    Drawn *d = &drawn[(*count)++];
    memset(d, 0, sizeof *d);
    d->key = floorBand * 10000000.0 + depth; /* the floor's band first, then nearer the viewer later (ComputeDepthZIndex) */
    d->picture = pic; d->x = x; d->y = y; d->w = w; d->h = h;
    d->actor = -1;
    return d;
}

/* Exult's roofs: something solid over the player's head - everything from its height up isn't drawn. */
static int ceiling_over_player(void)
{
    int l, t, ceiling = 1 << 30;
    player_foot(player.px, player.py, &l, &t);
    for (int i = 0; i < bodyCount; i++)
        if (overlaps(&bodies[i], l, t, player.sizeX, player.sizeY) && bodies[i].z >= player.lift + player.sizeZ && bodies[i].z < ceiling) ceiling = bodies[i].z;
    return ceiling;
}

void world_draw(uint32_t *pixels, uint32_t ms)
{
    screen = pixels;
    for (int i = 0; i < SCREEN_W * SCREEN_H; i++) pixels[i] = 0xFF000000u;
    int cx = (int)floor(cameraX), cy = (int)floor(cameraY);
    int ceiling = ceiling_over_player();

    /* The ground. */
    int x0 = cx / tileW - 1, y0 = cy / tileH - 1, x1 = (cx + SCREEN_W) / tileW + 1, y1 = (cy + SCREEN_H) / tileH + 1;
    for (int y = y0 < 0 ? 0 : y0; y <= y1 && y < mapH; y++)
        for (int x = x0 < 0 ? 0 : x0; x <= x1 && x < mapW; x++)
            if (floors[1]) blit_box(tile_picture(floors[1][y * mapW + x], ms), x * tileW - cx, y * tileH - cy, tileW, tileH);

    int count = 0;
    /* Upper floors' tiles, at their floor's height. */
    for (int f = 2; f <= MAX_FLOORS; f++)
    {
        if (!floors[f] || base_lift(f) - 1 >= ceiling) continue;
        int lift = base_lift(f);
        for (int y = y0 < 0 ? 0 : y0; y <= y1 + 4 && y < mapH; y++)
            for (int x = x0 < 0 ? 0 : x0; x <= x1 + 4 && x < mapW; x++)
            {
                int tile = floors[f][y * mapW + x];
                if (tile < 0) continue;
                add_drawn(f - 1, -5000000.0 + (x + y) * 100.0, tile_picture(tile, ms), x * tileW - lift * LIFT_SHIFT - cx, y * tileH - lift * LIFT_SHIFT - cy, tileW, tileH, &count); /* a floor: before everything standing on it */
            }
    }
    /* Things: bottom-centred on their cell, in a box rounded up to 8 pixels, up and left by their lift. */
    for (int i = 0; i < thingCount; i++)
    {
        const Thing *t = &things[i];
        if (t->gone || t->z >= ceiling) continue;
        const Picture *pic = sprite_picture_at(look_of(t), ms);
        if (!pic) continue;
        int qw = (pic->width + UNIT - 1) / UNIT * UNIT, qh = (pic->height + UNIT - 1) / UNIT * UNIT;
        int left = t->x * tileW + (tileW - qw) / 2 + t->offX * UNIT - t->z * LIFT_SHIFT;
        int top = (t->y + 1) * tileH - qh + t->offY * UNIT - t->z * LIFT_SHIFT;
        if (t->def && t->def->type == T_DOOR && t->open) { left += t->def->openOffX; top += t->def->openOffY; } /* ShiftOpenDoor: hanging from its hinge */
        int flat = t->def && (t->def->type == T_TRAP || (t->def->type == T_TILE && !t->def->solid));
        Drawn *d = add_drawn(floor_of(t->z) - 1, (t->x + t->y + 1) * 100.0 + t->z - (flat ? 1000 : 0), pic, left - cx, top - cy, qw, qh, &count);
        if (!d) continue;
        Body b;
        thing_box(t, &b);
        set_box(d, b.left, b.top, b.width, b.depth, b.z, flat ? 0 : b.height);
        d->wall = t->def && (t->def->type == T_WALL || t->def->type == T_DOOR || t->def->type == 12 /* Window */);
        d->occludes = t->def && t->def->occludes;
        d->hangs = t->hangs;
    }
    /* Characters and the player. */
    for (int i = -1; i < npcCount; i++)
    {
        const Actor *a = i < 0 ? &player : &npcs[i];
        if (i >= 0 && a->lift >= ceiling) continue;
        const Sprite *s = a->walkingNow && a->walking[a->facing] ? a->walking[a->facing] : a->idle[a->facing];
        uint32_t at = a->walkingNow ? ms : 0;
        if (a->shownPose >= 0 && a->poses[a->shownPose].dirs[a->facing]) { s = a->poses[a->shownPose].dirs[a->facing]; at = ms - a->poseSince; }
        const Picture *pic = sprite_picture_at(s, at);
        if (!pic) continue;
        int qw = (pic->width + UNIT - 1) / UNIT * UNIT, qh = (pic->height + UNIT - 1) / UNIT * UNIT;
        int left = (int)floor(a->px - qw / 2.0) - a->lift * LIFT_SHIFT, top = (int)floor(a->py + tileH / 2.0 - qh) - a->lift * LIFT_SHIFT;
        Drawn *d = add_drawn(floor_of(a->lift) - 1, (a->px / tileW + a->py / tileH) * 100.0 + a->lift, pic, left - cx, top - cy, qw, qh, &count);
        if (!d) continue;
        double feetY = a->py + tileH / 2.0;
        set_box(d, (int)floor((a->px - a->sizeX * UNIT / 2.0) / UNIT + 0.5), (int)floor((feetY - a->sizeY * UNIT) / UNIT + 0.5), a->sizeX, a->sizeY, a->lift, a->sizeZ);
        d->actor = i + 1;
    }
    /* Whatever rests on something is drawn after it (Exult: greater in z). */
    for (int pass = 0; pass < 2; pass++)
        for (int i = 0; i < count; i++)
        {
            Drawn *d = &drawn[i];
            if (!d->hasBox || d->wall) continue;
            for (int j = 0; j < count; j++)
            {
                const Drawn *u = &drawn[j];
                if (j == i || !u->hasBox || u->zt + 1 > d->zb || u->xr < d->xl || d->xr < u->xl || u->yn < d->yf || d->yn < u->yf) continue;
                if (u->key + 0.5 > d->key) d->key = u->key + 0.5;
            }
        }
    /* Walls, doors and windows: whatever is near them is drawn before or after them as Exult decides (OrderAmongWalls); hung on a wall: over it. */
    for (int i = 0; i < count; i++)
    {
        Drawn *d = &drawn[i];
        if (!d->hasBox || d->wall) continue;
        double lowest = 1e30, key = d->key;
        for (int j = 0; j < count; j++)
        {
            const Drawn *w = &drawn[j];
            if (!w->wall) continue;
            if (d->hangs)
            {
                if (!(d->x >= w->x + w->w || w->x >= d->x + d->w || d->y >= w->y + w->h || w->y >= d->y + d->h) && w->key + 0.25 > key) key = w->key + 0.25;
                continue;
            }
            int cmp = exult_compare(d, w);
            if (cmp > 0 && w->key + 0.25 > key) key = w->key + 0.25;
            else if (cmp < 0 && w->key - 0.25 < lowest) lowest = w->key - 0.25;
        }
        d->key = key < lowest ? key : lowest;
    }
    qsort(drawn, (size_t)count, sizeof(Drawn), by_key);
    for (int i = 0; i < count; i++) blit_box(drawn[i].picture, drawn[i].x, drawn[i].y, drawn[i].w, drawn[i].h);
    drawnCount = count;
    lastMs = ms;
}

/* ---------------- The characters, for events ---------------- */

static Actor *actor_of(int i) { return i == 0 ? &player : i > 0 && i <= npcCount ? &npcs[i - 1] : NULL; }

int world_actor_count(void) { return npcCount + 1; }

int world_actor_by_character(int characterId)
{
    if (player.characterId == characterId) return 0;
    for (int i = 0; i < npcCount; i++) if (npcs[i].characterId == characterId) return i + 1;
    return -1;
}

int world_actor_character(int actor) { const Actor *a = actor_of(actor); return a ? a->characterId : -1; }

const Sprite *world_actor_face(int actor) { const Actor *a = actor_of(actor); return a ? a->face : NULL; }

/* On the visible screen itself (GameTestWindow.IsInSight). */
int world_actor_in_sight(int actor)
{
    const Actor *a = actor_of(actor);
    return a && a->px >= cameraX && a->px <= cameraX + SCREEN_W && a->py >= cameraY && a->py <= cameraY + SCREEN_H;
}

/* The character whose picture is under a point of the screen, topmost first (as a click hits it); -1 for none. */
int world_actor_at(int sx, int sy)
{
    for (int i = drawnCount - 1; i >= 0; i--)
    {
        const Drawn *d = &drawn[i];
        if (sx < d->x || sy < d->y || sx >= d->x + d->w || sy >= d->y + d->h) continue;
        const Picture *p = d->picture;
        double s = fmin((double)d->w / p->width, (double)d->h / p->height);
        int dw = (int)(p->width * s + 0.5), dh = (int)(p->height * s + 0.5);
        int ox = d->x + (d->w - dw) / 2, oy = d->y + (d->h - dh) / 2;
        if (sx < ox || sy < oy || sx >= ox + dw || sy >= oy + dh) continue;
        uint32_t c = p->pixels[(size_t)((sy - oy) * p->height / dh) * (size_t)p->width + (size_t)((sx - ox) * p->width / dw)];
        if ((c >> 24) == 0) continue; /* see-through: what's behind is hit */
        return d->actor;
    }
    return -1;
}

int world_direction_of(const char *name) { return name && name[0] ? dir_of(name) : -1; }

/* Change Pose: the pose kept (and facing, -1 = as it is); 0 when it has no such pose. "Idle" / "Walking" go back to the usual. */
int world_set_pose(int actor, const char *pose, int facing)
{
    Actor *a = actor_of(actor);
    if (!a) return 0;
    if (facing >= 0) a->facing = facing & 7;
    if (strcmp(pose, "Idle") == 0) { a->shownPose = -1; return 1; }
    for (int i = 0; i < a->poseCount; i++)
        if (strcmp(a->poses[i].name, pose) == 0) { a->shownPose = i; a->poseSince = lastMs; return 1; }
    return 0;
}

/* Teleport: to a cell (its middle), standing on the floor it was on. */
int world_teleport(int actor, int x, int y)
{
    Actor *a = actor_of(actor);
    if (!a || x < 0 || y < 0 || x >= mapW || y >= mapH) return 0;
    int onFloor = floor_of(a->lift);
    a->px = x * tileW + tileW / 2.0; a->py = y * tileH + tileH / 2.0;
    double feetY = a->py + tileH / 2.0;
    int l = (int)floor((a->px - a->sizeX * UNIT / 2.0) / UNIT + 0.5), t = (int)floor((feetY - a->sizeY * UNIT) / UNIT + 0.5);
    if (a->body >= 0) bodies[a->body].z = -1000; /* not in its own way */
    a->lift = standing_lift(l, t, a->sizeX, a->sizeY, onFloor);
    if (a->body >= 0) { Body *b = &bodies[a->body]; b->left = l; b->top = t; b->z = a->lift; }
    if (actor == 0) { cameraX = a->px - SCREEN_W / 2.0; cameraY = a->py - SCREEN_H / 2.0; world_update(0, 0, 0); }
    return 1;
}

/* The cell the player stands in, and the floor. */
void world_player_cell(int *x, int *y, int *onFloor)
{
    *x = (int)floor(player.px / tileW); *y = (int)floor(player.py / tileH); *onFloor = floor_of(player.lift);
}

/* Stop and turn to the player for a conversation (GameTestWindow.TalkTo: Idle, facing the player). */
void world_turn_to_player(int actor)
{
    Actor *a = actor_of(actor);
    if (!a || actor == 0) return;
    double dx = player.px - a->px, dy = player.py - a->py;
    int facing = fabs(dx) > fabs(dy) ? (dx > 0 ? DIR_E : DIR_W) : (dy > 0 ? DIR_S : DIR_N);
    world_set_pose(actor, "Idle", facing);
}

/* ---------------- Walking somewhere (Walk To Location): Exult's A* on map cells ---------------- */

/* The footprint (8-pixel units) of someone whose middle is at (px, py). */
static void foot_at(const Actor *a, double px, double py, int *l, int *t)
{
    double feetY = py + tileH / 2.0;
    *l = (int)floor((px - a->sizeX * UNIT / 2.0) / UNIT + 0.5);
    *t = (int)floor((feetY - a->sizeY * UNIT) / UNIT + 0.5);
}

static double cell_cx(int x) { return x * tileW + tileW / 2.0; }
static double cell_cy(int y) { return y * tileH + tileH / 2.0; }

/* While working out a walker's way, its own body isn't in its way. */
static int lift_own_body(Actor *a) { if (a->body < 0) return 0; int z = bodies[a->body].z; bodies[a->body].z = -100000; return z; }
static void put_own_body(Actor *a, int z) { if (a->body >= 0) bodies[a->body].z = z; }

/* CanSweep: from one point to another the way walkers go (along X, then Y), half a unit at a time, each new footprint by the walking rules. */
static int can_sweep(const Actor *a, double fx, double fy, double tx, double ty, int fromLift, int *lift)
{
    *lift = fromLift;
    double x = fx, y = fy, step = UNIT / 2.0;
    int ll, lt;
    foot_at(a, x, y, &ll, &lt);
    for (int pass = 0; pass < 2; pass++)
        for (;;)
        {
            double remaining = pass == 0 ? tx - x : ty - y;
            if (fabs(remaining) < 0.01) break;
            double move = (remaining > 0 ? 1 : -1) * fmin(step, fabs(remaining));
            if (pass == 0) x += move; else y += move;
            int l, t;
            foot_at(a, x, y, &l, &t);
            if (l == ll && t == lt) continue;
            int nl = try_step(l, t, a->sizeX, a->sizeY, *lift, a->sizeZ);
            if (nl < 0) return 0;
            *lift = nl; ll = l; lt = t;
        }
    return 1;
}

static int can_stand_at(const Actor *a, int cx, int cy, int fromLift, int *lift)
{
    int l, t;
    foot_at(a, cell_cx(cx), cell_cy(cy), &l, &t);
    *lift = try_step(l, t, a->sizeX, a->sizeY, fromLift, a->sizeZ);
    return *lift >= 0;
}

typedef struct Node { int x, y, f, lift, cost, from, open; } Node;
static int find_path_core(Actor *a, int gx, int gy, int goalFloor, int allowPartial, int besideOnly, int *pathX, int *pathY, int max);

static int heuristic(int ax, int ay, int bx, int by) { return abs(ax - bx) + abs(ay - by); }

/* FindPath: from where the walker is to the goal cell (on a floor when asked: 0 = whatever floor), or beside it when it's taken.
   Fills path (cells) and returns its length; -1 when there's no way (allowPartial: the reachable cell nearest the goal instead). */
static void doors_passable(const Actor *a, int passable)
{
    if (!a->human) return;
    for (int i = 0; i < thingCount; i++)
    {
        Thing *t = &things[i];
        if (t->gone || !t->def || t->def->type != T_DOOR || t->open || t->locked || t->body < 0) continue;
        if (passable) bodies[t->body].z -= 1000000; else bodies[t->body].z += 1000000;
    }
}

static int find_path(Actor *a, int gx, int gy, int goalFloor, int allowPartial, int besideOnly, int *pathX, int *pathY, int max)
{
    doors_passable(a, 1); /* WorldBodiesForPathing: closed unlocked doors will be opened on the way */
    int length = find_path_core(a, gx, gy, goalFloor, allowPartial, besideOnly, pathX, pathY, max);
    doors_passable(a, 0);
    return length;
}

static int find_path_core(Actor *a, int gx, int gy, int goalFloor, int allowPartial, int besideOnly, int *pathX, int *pathY, int max)
{
    int own = lift_own_body(a);
    int sx = (int)floor(a->px / tileW), sy = (int)floor(a->py / tileH);
    int startF = goalFloor ? floor_of(a->lift) : 0;
    if (sx == gx && sy == gy && startF == goalFloor && !besideOnly) { put_own_body(a, own); return 0; }
    int goalLift, goalFree;
    if (besideOnly) goalFree = 0; /* heading for a thing: end beside it, never on it */
    else if (goalFloor) goalFree = can_stand_at(a, gx, gy, base_lift(goalFloor), &goalLift) && floor_of(goalLift) == goalFloor;
    else
    {
        /* anyLift: clear on the highest floor under it */
        int l, t;
        foot_at(a, cell_cx(gx), cell_cy(gy), &l, &t);
        goalFree = try_step(l, t, a->sizeX, a->sizeY, standing_lift(l, t, a->sizeX, a->sizeY, MAX_FLOORS), a->sizeZ) >= 0;
    }
    /* Nodes: a cell on a floor (floor 0 when none is asked for). */
    int floors = goalFloor ? MAX_FLOORS + 1 : 1, cells = mapW * mapH * floors;
    int *index = (int *)malloc(sizeof(int) * (size_t)cells);
    for (int i = 0; i < cells; i++) index[i] = -1;
    int cap = 6100, count = 0;
    Node *nodes = (Node *)malloc(sizeof(Node) * (size_t)cap);
#define KEY(x, y, f) (((f) * mapH + (y)) * mapW + (x))
#define H(n) (heuristic((n).x, (n).y, gx, gy) + (goalFloor ? abs((n).f - goalFloor) * LIFTS_PER_FLOOR : 0))
    nodes[count] = (Node){ sx, sy, startF, a->lift, 0, -1, 1 };
    index[KEY(sx, sy, startF)] = count++;
    int nearest = 0, nearestH = H(nodes[0]), found = -1, expansions = 0;
    while (expansions++ < 6000)
    {
        /* the open node with the least cost + estimate */
        int best = -1, bestScore = 1 << 30;
        for (int i = 0; i < count; i++)
            if (nodes[i].open) { int s = nodes[i].cost + H(nodes[i]); if (s < bestScore) { bestScore = s; best = i; } }
        if (best < 0) break;
        Node cur = nodes[best];
        nodes[best].open = 0;
        int h = H(cur);
        if (h < nearestH) { nearest = best; nearestH = h; }
        if (cur.f == goalFloor && ((cur.x == gx && cur.y == gy && goalFree) || (!goalFree && heuristic(cur.x, cur.y, gx, gy) == 1 && (best != 0 || besideOnly)))) { found = best; break; }
        static const int DX[4] = { 1, -1, 0, 0 }, DY[4] = { 0, 0, 1, -1 };
        for (int d = 0; d < 4; d++)
        {
            int nx = cur.x + DX[d], ny = cur.y + DY[d], lift;
            if (nx < 0 || ny < 0 || nx >= mapW || ny >= mapH) continue;
            if (!goalFloor && index[KEY(nx, ny, 0)] >= 0 && nodes[index[KEY(nx, ny, 0)]].cost <= cur.cost + 1) continue;
            double fx = best == 0 ? a->px : cell_cx(cur.x), fy = best == 0 ? a->py : cell_cy(cur.y);
            if (!goalFloor && !can_stand_at(a, nx, ny, cur.lift, &lift)) continue;
            if (!can_sweep(a, fx, fy, cell_cx(nx), cell_cy(ny), cur.lift, goalFloor ? &lift : &(int){ 0 })) continue;
            int nf = goalFloor ? floor_of(lift) : 0, k = KEY(nx, ny, nf), cost = cur.cost + 1;
            if (index[k] >= 0 && nodes[index[k]].cost <= cost) continue;
            if (index[k] < 0)
            {
                if (count == cap) { cap *= 2; nodes = (Node *)realloc(nodes, sizeof(Node) * (size_t)cap); }
                index[k] = count++;
            }
            nodes[index[k]] = (Node){ nx, ny, nf, lift, cost, best, 1 };
        }
    }
    int end = found >= 0 ? found : allowPartial && nearest != 0 ? nearest : -1, length = -1;
    if (end >= 0)
    {
        length = 0;
        for (int n = end; n > 0 && nodes[n].from >= 0; n = nodes[n].from) length++;
        if (length > max) length = max;
        int i = length;
        for (int n = end; n > 0 && nodes[n].from >= 0 && i > 0; n = nodes[n].from) { i--; pathX[i] = nodes[n].x; pathY[i] = nodes[n].y; }
    }
#undef KEY
#undef H
    free(index); free(nodes);
    put_own_body(a, own);
    return length;
}

#define MAX_PATH 512
typedef struct Walk { int active, count, at; int x[MAX_PATH], y[MAX_PATH]; int doorToClose, blocked; } Walk;
static Walk *walks; /* one per actor */

static Walk *walk_of(int actor)
{
    if (!walks) walks = (Walk *)calloc((size_t)npcCount + 1, sizeof(Walk));
    return actor >= 0 && actor <= npcCount ? &walks[actor] : NULL;
}

/* Starts a walk there (the player: first to the middle of the cell they stand in); 0 when there's no way just now. */
int world_walk_to(int actor, int x, int y, int goalFloor, int beside)
{
    Actor *a = actor_of(actor);
    Walk *w = walk_of(actor);
    if (!a || !w) return 0;
    int n = find_path(a, x, y, goalFloor, actor != 0, beside, w->x + 1, w->y + 1, MAX_PATH - 1);
    if (n < 0) return 0;
    w->x[0] = (int)floor(a->px / tileW); w->y[0] = (int)floor(a->py / tileH);
    w->count = n + 1; w->at = 0; w->active = 1;
    a->shownPose = -1;
    return 1;
}

int world_walking(int actor) { Walk *w = walk_of(actor); return w && w->active; }

void world_walk_cancel(int actor) { Walk *w = walk_of(actor); Actor *a = actor_of(actor); if (w) w->active = 0; if (a) a->walkingNow = 0; }

void world_actor_cell(int actor, int *x, int *y, int *onFloor)
{
    Actor *a = actor_of(actor);
    if (!a) { *x = *y = -1; *onFloor = 1; return; }
    *x = (int)floor(a->px / tileW); *y = (int)floor(a->py / tileH); *onFloor = floor_of(a->lift);
}

/* Whether someone could stand on the cell (on that floor; 0 = any). */
int world_can_stand(int actor, int x, int y, int onFloor)
{
    Actor *a = actor_of(actor);
    if (!a || x < 0 || y < 0 || x >= mapW || y >= mapH) return 0;
    int own = lift_own_body(a), lift, ok;
    if (onFloor) ok = can_stand_at(a, x, y, base_lift(onFloor), &lift) && floor_of(lift) == onFloor;
    else ok = can_stand_at(a, x, y, a->lift, &lift);
    put_own_body(a, own);
    return ok;
}

static Thing *thing_by_uid(int uid);

static void door_set_open(Thing *t, int open)
{
    t->open = open;
    t->solid = t->def->solid && !open;
    if (t->body < 0) return;
    if (open) { bodies[t->body].width = bodies[t->body].depth = 0; bodies[t->body].z = -100000; }
    else thing_box(t, &bodies[t->body]);
}

/* NpcOpensDoor: a human walking into a closed, unlocked door opens it (and will close it behind). */
static int open_door_in_way(Actor *a, Walk *w, int l, int t)
{
    if (!a->human || a == &player) return 0;
    for (int i = 0; i < thingCount; i++)
    {
        Thing *d = &things[i];
        if (d->gone || !d->def || d->def->type != T_DOOR || d->open || d->locked || d->body < 0) continue;
        if (!overlaps(&bodies[d->body], l, t, a->sizeX, a->sizeY)) continue;
        door_set_open(d, 1);
        w->doorToClose = d->uid;
        return 1;
    }
    return 0;
}

/* CloseDoorsBehindNpcs: once clear of the doorway (a tile all round), the door is closed again - unless someone is in it. */
static void close_doors_behind(void)
{
    for (int i = 0; i <= npcCount; i++)
    {
        Walk *w = walk_of(i);
        Actor *a = actor_of(i);
        if (!w || !a || !w->doorToClose) continue;
        Thing *d = thing_by_uid(w->doorToClose);
        if (!d || !d->open) { w->doorToClose = 0; continue; }
        Body b;
        thing_box(d, &b);
        int l, t;
        foot_at(a, a->px, a->py, &l, &t);
        Body doorway = { b.left - 1, b.top - 1, b.width + 2, b.depth + 2, 0, 0 };
        if (overlaps(&doorway, l, t, a->sizeX, a->sizeY)) continue;
        int blocked = 0;
        for (int k = 0; k <= npcCount && !blocked; k++)
        {
            Actor *o = actor_of(k);
            int ol, ot;
            foot_at(o, o->px, o->py, &ol, &ot);
            if (overlaps(&b, ol, ot, o->sizeX, o->sizeY)) blocked = 1;
        }
        if (blocked) continue; /* someone in the doorway: next frame */
        door_set_open(d, 0);
        w->doorToClose = 0;
    }
}

/* One frame of a walk: toward the next cell's middle, one axis at a time, never past it; blocked: it stops. */
static void step_walk(int actor, double seconds)
{
    Actor *a = actor_of(actor);
    Walk *w = walk_of(actor);
    if (!a || !w || !w->active) return;
    double budget = TILES_PER_SECOND * tileW * seconds;
    while (budget > 0.0001)
    {
        if (w->at >= w->count) { w->active = 0; a->walkingNow = 0; return; }
        double rx = cell_cx(w->x[w->at]) - a->px, ry = cell_cy(w->y[w->at]) - a->py;
        if (fabs(rx) < 0.01 && fabs(ry) < 0.01) { a->px = cell_cx(w->x[w->at]); a->py = cell_cy(w->y[w->at]); w->at++; continue; }
        int alongX = fabs(rx) > 0.01;
        double remaining = alongX ? rx : ry, move = fmin(fabs(remaining), budget);
        a->facing = alongX ? (remaining > 0 ? DIR_E : DIR_W) : (remaining > 0 ? DIR_S : DIR_N);
        double nx = a->px + (alongX ? (remaining > 0 ? move : -move) : 0), ny = a->py + (alongX ? 0 : (remaining > 0 ? move : -move));
        int l0, t0, l1, t1;
        foot_at(a, a->px, a->py, &l0, &t0);
        foot_at(a, nx, ny, &l1, &t1);
        if (l1 != l0 || t1 != t0)
        {
            int own = lift_own_body(a);
            int lift = try_step(l1, t1, a->sizeX, a->sizeY, a->lift, a->sizeZ);
            put_own_body(a, own);
            if (lift < 0 && open_door_in_way(a, w, l1, t1)) return; /* a closed, unlocked door: opened, on next frame */
            if (lift < 0) { w->active = 0; w->blocked = 1; a->walkingNow = 0; return; } /* something in the way: stop, think again */
            a->lift = lift;
            if (a->body >= 0) { bodies[a->body].left = l1; bodies[a->body].top = t1; bodies[a->body].z = lift; }
        }
        a->px = nx; a->py = ny;
        a->walkingNow = 1;
        budget -= move;
    }
}

/* Each frame: everyone walking somewhere takes their step (the player's own keys cancel theirs). */
void world_walks_update(double seconds)
{
    for (int i = 0; i <= npcCount; i++) step_walk(i, seconds);
    close_doors_behind();
}

/* ---------------- Things on the map, for items (Pickup / Drop / Give Item) ---------------- */

static Thing *thing_by_uid(int uid)
{
    for (int i = 0; i < thingCount; i++) if (things[i].uid == uid && !things[i].gone) return &things[i];
    return NULL;
}

static int cells_apart(int ax, int ay, int bx, int by) { int dx = abs(ax - bx), dy = abs(ay - by); return dx > dy ? dx : dy; }

/* ThingOf: an object - the very one if it's still there, else the same kind of object within 2 cells of where it was. */
int world_find_object(int instanceId, int objectId, int x, int y)
{
    int best = -1, bestApart = 3;
    for (int i = 0; i < thingCount; i++)
    {
        const Thing *t = &things[i];
        if (t->gone || t->category != 4 || t->itemId != objectId) continue;
        if (t->instanceId == instanceId) return t->uid;
        int apart = cells_apart(t->x, t->y, x, y);
        if (apart <= 2 && apart < bestApart) { bestApart = apart; best = t->uid; }
    }
    return best;
}

/* ThingOf: a book or container of that kind within 2 cells of where it was, the nearest. */
int world_find_item(int category, int itemId, int x, int y)
{
    int best = -1, bestApart = 3;
    for (int i = 0; i < thingCount; i++)
    {
        const Thing *t = &things[i];
        if (t->gone || t->category != category || t->itemId != itemId) continue;
        int apart = cells_apart(t->x, t->y, x, y);
        if (apart <= 2 && apart < bestApart) { bestApart = apart; best = t->uid; }
    }
    return best;
}

int world_thing_there(int uid) { return thing_by_uid(uid) != NULL; }

void world_thing_info(int uid, int *category, int *itemId, int *quantity, int *x, int *y, int *instanceId)
{
    const Thing *t = thing_by_uid(uid);
    *category = t ? t->category : -1; *itemId = t ? t->itemId : -1; *quantity = t ? t->quantity : 0;
    *x = t ? t->x : -1; *y = t ? t->y : -1; *instanceId = t ? t->instanceId : 0;
}

/* SettleWorld for what rested on a thing taken away: it comes down onto whatever is under it now (on its floor). */
static void settle_over(int left, int top, int width, int depth, int topLift)
{
    for (int i = 0; i < thingCount; i++)
    {
        Thing *t = &things[i];
        if (t->gone || t->hangs || (t->def && (t->def->type == T_WALL || t->def->type == T_DOOR || t->def->type == T_TILE || t->def->type == 12 || t->def->type == T_TRAP))) continue;
        Body b;
        thing_box(t, &b);
        if (t->z != topLift || !overlaps(&b, left, top, width, depth)) continue;
        if (t->body >= 0) bodies[t->body].z = -100000;
        int lift = standing_lift(b.left, b.top, b.width, b.depth, floor_of(t->z));
        if (t->body >= 0) bodies[t->body].z = lift;
        if (lift < t->z)
        {
            int was = t->z;
            t->z = lift;
            settle_over(b.left, b.top, b.width, b.depth, was + b.height);
        }
    }
}

/* Picked up: off the map (what rested on it comes down). */
void world_thing_remove(int uid)
{
    Thing *t = thing_by_uid(uid);
    if (!t) return;
    t->gone = 1;
    Body b;
    thing_box(t, &b);
    if (t->body >= 0) { bodies[t->body].width = bodies[t->body].depth = 0; bodies[t->body].z = -100000; }
    settle_over(b.left, b.top, b.width, b.depth, b.z + b.height);
}

/* Put down at a cell (PutDownAt): resting on whatever is there, on that floor. Returns its uid (-1 if it can't be shown). */
int world_thing_put(int category, int itemId, int quantity, int x, int y, int onFloor)
{
    if (thingCount >= thingCap) { thingCap *= 2; things = (Thing *)realloc(things, sizeof(Thing) * (size_t)thingCap); }
    if (bodyCount + 1 >= bodyCap) { bodyCap *= 2; bodies = (Body *)realloc(bodies, sizeof(Body) * (size_t)bodyCap); }
    Thing *t = &things[thingCount];
    memset(t, 0, sizeof *t);
    t->uid = nextUid++; t->category = category; t->itemId = itemId; t->quantity = quantity > 0 ? quantity : 1;
    t->x = x; t->y = y; t->facing = DIR_S;
    if (category == 4)
    {
        const ObjectDef *d = def_of(itemId);
        if (!d) return -1;
        t->def = d;
        t->sizeX = d->sizeX; t->sizeY = d->sizeY;
        int flat = d->type == T_TRAP || (d->type == T_TILE && !d->solid);
        t->sizeZ = flat ? 0 : d->sizeZ;
        t->solid = d->solid; t->solidOffX = d->solidOffX; t->solidOffY = d->solidOffY;
        t->open = d->type == T_DOOR && d->doorStartsOpen; t->lit = d->type == T_LIGHT && d->lightStartsLit;
    }
    else
    {
        cJSON *data = NULL, *c;
        cJSON *source = category == 5 ? containersJson : booksJson;
        cJSON_ArrayForEach(c, source) if (num(c, "Id", -2) == itemId) data = c;
        if (!data) return -1;
        t->sprite = sprite_find(str(data, "SpriteGroup"), str(data, "SpriteName"));
        t->sizeX = num(data, "SizeX", 1); t->sizeY = num(data, "SizeY", 1); t->sizeZ = num(data, "SizeZ", 1);
        t->solid = flag(data, "IsSolid"); t->solidOffX = num(data, "SolidOffsetX", 0); t->solidOffY = num(data, "SolidOffsetY", 0);
    }
    if (t->sizeX < 1) t->sizeX = 1;
    if (t->sizeY < 1) t->sizeY = 1;
    Body b;
    thing_box(t, &b);
    t->z = standing_lift(b.left, b.top, b.width, b.depth, onFloor);
    thingCount++;
    add_thing_body(t);
    return t->uid;
}

/* How long a pose plays once as they face now (PoseLengthMs): its frames' times, 500 when it has none. */
int world_pose_length(int actor, const char *pose)
{
    Actor *a = actor_of(actor);
    if (!a) return 500;
    for (int i = 0; i < a->poseCount; i++)
        if (strcmp(a->poses[i].name, pose) == 0 && a->poses[i].dirs[a->facing]) return a->poses[i].dirs[a->facing]->totalMs > 0 ? a->poses[i].dirs[a->facing]->totalMs : 500;
    return 500;
}

/* FaceCell: turned toward a cell, in a pose. */
void world_face_cell(int actor, int x, int y, const char *pose)
{
    Actor *a = actor_of(actor);
    if (!a) return;
    double dx = x * tileW + tileW / 2.0 - a->px, dy = y * tileH + tileH / 2.0 - a->py;
    world_set_pose(actor, pose, fabs(dx) > fabs(dy) ? (dx > 0 ? DIR_E : DIR_W) : (dy > 0 ? DIR_S : DIR_N));
}

/* ---------------- Words over someone's head (Show Bark, ShowFloatingText) ---------------- */

typedef struct Bark { int actor; char text[160]; uint32_t until; } Bark;
static Bark barks[16];

void world_bark(int actor, const char *text, int ms)
{
    int slot = 0;
    for (int i = 0; i < 16; i++) if (barks[i].until <= lastMs) { slot = i; break; }
    barks[slot].actor = actor;
    snprintf(barks[slot].text, sizeof barks[slot].text, "%s", text);
    barks[slot].until = lastMs + (uint32_t)(ms > 0 ? ms : 1000);
}

/* Drawn over everything: centred over the head, 2 pixels above the picture (GameTextLabel in the InGameFont). */
void world_draw_barks(uint32_t *pixels)
{
    const Font *f = font_get("InGameFont");
    for (int i = 0; i < 16; i++)
    {
        if (barks[i].until <= lastMs || !barks[i].text[0]) continue;
        const Actor *a = actor_of(barks[i].actor);
        if (!a) continue;
        const Picture *pic = sprite_picture_at(a->idle[a->facing], 0);
        int h = pic ? (pic->height + UNIT - 1) / UNIT * UNIT : tileH;
        int w = font_text_width(f, barks[i].text);
        int x = (int)floor(a->px - cameraX - w / 2.0), y = (int)floor(a->py + tileH / 2.0 - h - font_line_height(f) - 2 - a->lift * LIFT_SHIFT - cameraY);
        font_draw(f, pixels, x, y, barks[i].text, 0);
    }
}

const char *world_map_name(void) { return mapName; }
int world_actor_facing(int actor) { Actor *a = actor_of(actor); return a ? a->facing : DIR_S; }

/* LightAround: Get_light_strength summed over the lit lights on screen (6 tiles round it too), and the strongest kind among them. */
int world_light_strength(int *gives)
{
    int total = 0;
    *gives = 0;
    double margin = 6 * UNIT;
    for (int i = 0; i < thingCount; i++)
    {
        const Thing *t = &things[i];
        if (t->gone || !t->lit || !t->def || t->def->type != T_LIGHT) continue;
        double x = t->x * tileW + tileW / 2.0, y = t->y * tileH + tileH / 2.0;
        if (x < cameraX - margin || x > cameraX + SCREEN_W + margin || y < cameraY - margin || y > cameraY + SCREEN_H + margin) continue;
        int dx = (int)fabs(floor((x - player.px) / UNIT + 0.5)), dy = (int)fabs(floor((y - player.py) / UNIT + 0.5));
        int strength = 75 - 2 * dx - 3 * dy;
        if (strength <= 0) continue;
        total += strength * t->def->brightness;
        if (strength * t->def->brightness > 0 && t->def->gives > *gives) *gives = t->def->gives;
    }
    return total;
}

void world_actor_pixel(int actor, double *px, double *py) { Actor *a = actor_of(actor); *px = a ? a->px : 0; *py = a ? a->py : 0; }
int world_tile_w(void) { return tileW; }
int world_tile_h(void) { return tileH; }
int world_map_w(void) { return mapW; }
int world_map_h(void) { return mapH; }
/* Whether its last walk was stopped by something in the way (asked once: the answer is cleared). */
int world_walk_blocked(int actor) { Walk *w = walk_of(actor); int b = w && w->blocked; if (w) w->blocked = 0; return b; }
/* On the screen, enlarged by margin pixels all round. */
int world_on_screen(double px, double py, double margin)
{
    return px >= cameraX - margin && px <= cameraX + SCREEN_W + margin && py >= cameraY - margin && py <= cameraY + SCREEN_H + margin;
}
void world_view_center(double *x, double *y) { *x = cameraX + SCREEN_W / 2.0; *y = cameraY + SCREEN_H / 2.0; }
