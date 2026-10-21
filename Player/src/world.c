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
} ObjectDef;

typedef struct Thing
{
    const ObjectDef *def;            /* an object; NULL for a placed book or container */
    const Sprite *sprite;            /* a book's or container's picture */
    int x, y, z, offX, offY, facing, open, lit;
    int sizeX, sizeY, sizeZ, solid, solidOffX, solidOffY, hangs;
} Thing;

typedef struct Body { int left, top, width, depth, z, height; } Body;

typedef struct Actor
{
    double px, py; int lift, facing;
    int sizeX, sizeY, sizeZ;
    const Sprite *idle[8], *walking[8];
    int walkingNow;
} Actor;

static char error_text[512], title[256];
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
        d->lit = link_of(get(light, "LitSprite")); d->lightStartsLit = flag(light, "StartsLit");
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

static void add_thing_body(const Thing *t)
{
    if (!t->solid || t->hangs) return;
    thing_box(t, &bodies[bodyCount++]);
}

static void read_actor_poses(cJSON *character, cJSON *poses, Actor *a)
{
    int idleId = -1, walkingId = -1;
    cJSON *p;
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
    things = (Thing *)calloc((size_t)capacity, sizeof(Thing));
    bodies = (Body *)calloc((size_t)capacity + (size_t)(mapW * mapH * MAX_FLOORS), sizeof(Body));
    cJSON_ArrayForEach(o, objects)
    {
        if (num(o, "ContainerInstanceId", -1) >= 0) continue;
        const ObjectDef *d = def_of(num(o, "ObjectId", -1));
        if (!d) continue;
        Thing *t = &things[thingCount++];
        t->def = d;
        t->x = num(o, "X", 0); t->y = num(o, "Y", 0); t->z = num(o, "Z", 0);
        t->offX = num(o, "OffsetX", 0); t->offY = num(o, "OffsetY", 0); t->facing = num(o, "Facing", DIR_S) & 7;
        cJSON *so = get(o, "StartsOpen"), *sl = get(o, "StartsLit");
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
    cJSON_ArrayForEach(o, items)
    {
        int category = num(o, "Category", 0), id = num(o, "ItemId", -1);
        cJSON *data = NULL, *c;
        if (category != 5 && category != 3) continue; /* placed containers and books (ReferencedItemCategory) */
        cJSON *source = category == 5 ? containers : books;
        cJSON_ArrayForEach(c, source) if (num(c, "Id", -2) == id) data = c;
        if (!data) continue;
        Thing *t = &things[thingCount++];
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
        Body *b = &bodies[bodyCount++]; /* solid, like Exult's actors */
        b->left = al; b->top = at; b->width = a->sizeX; b->depth = a->sizeY; b->z = a->lift; b->height = a->sizeZ;
    }
    cJSON_Delete(root);
    cameraX = player.px - SCREEN_W / 2.0; cameraY = player.py - SCREEN_H / 2.0;
    return 1;
}

/* ---------------- Each frame ---------------- */

void world_update(int dx, int dy, double seconds)
{
    if (dx == 0 && dy == 0) { player.walkingNow = 0; }
    else
    {
        player.facing = dx < 0 ? DIR_W : dx > 0 ? DIR_E : dy < 0 ? DIR_N : DIR_S;
        player.walkingNow = 1;
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

static Drawn *drawn; static int drawnCap;

static Drawn *add_drawn(int floorBand, double depth, const Picture *pic, int x, int y, int w, int h, int *count)
{
    if (!pic) return NULL;
    if (x + w < 0 || y + h < 0 || x > SCREEN_W || y > SCREEN_H) return NULL;
    if (*count >= drawnCap) { drawnCap = drawnCap ? drawnCap * 2 : 1024; drawn = (Drawn *)realloc(drawn, sizeof(Drawn) * (size_t)drawnCap); }
    Drawn *d = &drawn[(*count)++];
    memset(d, 0, sizeof *d);
    d->key = floorBand * 10000000.0 + depth; /* the floor's band first, then nearer the viewer later (ComputeDepthZIndex) */
    d->picture = pic; d->x = x; d->y = y; d->w = w; d->h = h;
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
        if (t->z >= ceiling) continue;
        const Picture *pic = sprite_picture_at(look_of(t), ms);
        if (!pic) continue;
        int qw = (pic->width + UNIT - 1) / UNIT * UNIT, qh = (pic->height + UNIT - 1) / UNIT * UNIT;
        int left = t->x * tileW + (tileW - qw) / 2 + t->offX * UNIT - t->z * LIFT_SHIFT;
        int top = (t->y + 1) * tileH - qh + t->offY * UNIT - t->z * LIFT_SHIFT;
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
        const Picture *pic = sprite_picture_at(s, a->walkingNow ? ms : 0);
        if (!pic) continue;
        int qw = (pic->width + UNIT - 1) / UNIT * UNIT, qh = (pic->height + UNIT - 1) / UNIT * UNIT;
        int left = (int)floor(a->px - qw / 2.0) - a->lift * LIFT_SHIFT, top = (int)floor(a->py + tileH / 2.0 - qh) - a->lift * LIFT_SHIFT;
        Drawn *d = add_drawn(floor_of(a->lift) - 1, (a->px / tileW + a->py / tileH) * 100.0 + a->lift, pic, left - cx, top - cy, qw, qh, &count);
        if (!d) continue;
        double feetY = a->py + tileH / 2.0;
        set_box(d, (int)floor((a->px - a->sizeX * UNIT / 2.0) / UNIT + 0.5), (int)floor((feetY - a->sizeY * UNIT) / UNIT + 0.5), a->sizeX, a->sizeY, a->lift, a->sizeZ);
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
}
