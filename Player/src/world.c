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
#include "blend.h"
#include "gumps.h"
#include "lighting.h"
#include "world.h"
#include "vitals.h"
#include "magic.h"
#include "combat.h"
#include "mechanisms.h"
#include "pictures.h"
#include "font.h"
#include "pack.h"
#include "sound.h"
#include "items.h"
#include "schedules.h"
#include "platform.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

char *read_whole_file(const char *path, long *length);
void game_log(const char *line);

#define UNIT 8
#define COPY_STEP 1000000
#define LIFT_SHIFT 4
#define LIFTS_PER_FLOOR 5
#define MAX_FLOORS 9
#define FLY_LIFT (MAX_FLOORS * LIFTS_PER_FLOOR) /* "floor 10": above every floor, out of reach of any blow */
#define SKY_LIFT (FLY_LIFT + 20)
#define TILES_PER_SECOND 4.5
#define DEAD_HALF_W 60
#define DEAD_HALF_H 40

enum { T_JUNK, T_TILE, T_WALL, T_DOOR, T_LIGHT, T_AMMO, T_MONEY, T_CHAIR, T_BED, T_TRAP, T_KEY, T_LOCKPICK, T_WINDOW, T_PLANT, T_KEYCHAIN,
       T_SEXTANT, T_MAP, T_FOOD, T_POTION, T_GLASS, T_SPELLBOOK, T_USEABLE, T_LEVER, T_STAIRS, T_ROOF, T_CLOCK, T_TOOL, T_STATION,
       T_INSTRUMENT, T_EXPLOSIVE, T_BEDROLL, T_REAGENT, T_TABLE, T_LIQUID };
enum { T_KITE = 37 }; /* ObjectType.Kite */
enum { DIR_N, DIR_NE, DIR_E, DIR_SE, DIR_S, DIR_SW, DIR_W, DIR_NW };
static const char *DIR_NAMES[8] = { "North", "NorthEast", "East", "SouthEast", "South", "SouthWest", "West", "NorthWest" };

typedef struct Link { const Sprite *sprite; } Link;

typedef struct ObjectDef
{
    int id, type, sizeX, sizeY, sizeZ, solid, solidOffX, solidOffY, stackable, elevates, floorTile, portable;
    const Sprite *own, *open, *lit, *spent, *musicOpen;
    const Sprite *facing[8], *turned[8], *back[8]; /* back: a chair's back per facing, drawn over its sitter (FurnitureFacing.BackSprite) */
    int doorStartsOpen, lightStartsLit, occludes;
    int doorStartsLocked, openOffX, openOffY;
    /* How a door opens (DoorOpening: 0 swing, 1 animation, 2 slide, 3 fade), by itself, with its double, by hand or not. */
    int doorOpening, slideX, slideY, slideHidden, motionMs, notByHand, withNeighbours, opensByItself, opensWithin, staysOpenMs, doorOpenSound, doorCloseSound;
    const Sprite *openingSprite, *closingSprite;
    int brightness, gives;            /* a light: how bright, what it gives (Exult levels, the dusk look, daylight) */
    int burnMinutes, spentSound;      /* how long it burns (0: for ever), and the sound when it's spent */
    int spotCount[8]; double spotX[8][8], spotY[8][8]; /* a chair's seats / a bed's sleeping places, per facing (picture pixels) */
    int coverId;                      /* a bed's cover object (-1: none) */
    const Sprite *locked, *bundle[8]; /* a door's locked look; ammo / money / lockpicks by how many (ObjectTypes.BundleLevel) */
    const Sprite *winOpen, *winBroken; int winShutters, winStartsOpen, winBlocks; /* a window: shutters open, glass broken; closed shutters block sight */
    const Sprite *stages[16]; int stageCount;                                    /* a plant's stages */
    const Sprite *leverOn; int leverKind, leverStartsOn;                          /* a lever (0), switch (1), button (2) */
    const Sprite *working, *laidOut, *usedUp;                                     /* a crafting station at work; a bedroll laid out; a useable object used up */
    int liquidFood[8]; const Sprite *liquidLook[8]; int liquidCount, liquidStart; /* a liquid container: its look per liquid; what it starts full of */
    int stairsKind, risesToward; cJSON *design;                                   /* stairs (0) or a ladder (1); a designed staircase (Staircase Designer) */
    int trapHidden;                                                               /* a hidden trap: unseen till sprung */
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
    int flying;                                      /* a kite up in the air: it stays at its height (mechanisms.c) */
    int fixedFrame;                                  /* a frame shown instead of the animation (frame + 1; 0 = animate) - a bed's cover */
    int spent; double burnLeft;                      /* a light burnt out; the game minutes it has left (-1: not started) */
    cJSON *placed;                                   /* its Map Editor entry (a container's own contents) */
    const Sprite *lookOverride;                      /* Change Object Look: another sprite (NULL: its own) */
    int seq;                                         /* when last put down (0: when made, its uid) - ties in drawing order go to the later */
    int elevates, liesUnder, wallSetPiece, vehicle, homeFloor, staysPut; /* gravity: rests on top / holds things up; put underneath; fixed pieces; the floor it's on */
    int hp, hpSet;                                   /* hit points left in play (Attack Object) */
    int spawned;                                     /* made at the start (a bed's cover): not saved, made again */
    int bodyN;                                       /* how many boxes it has from body on (stairs: a box a step) */
    int owner;                                       /* who it belongs to now, set in play (+1; 0: as placed) */
    int sprung;                                      /* a trap that has gone off (TrapSprungRuntime): a hidden one shows from then on */
    int stage; double grow;                          /* a plant: its stage, the game minutes grown towards the next */
    int liquid;                                      /* a liquid container: what it holds (a liquid Food's id, -1: empty) */
    cJSON *steps;                                    /* a designed staircase: its steps for its facing */
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
    const Sprite *joinedSprite; int joinedFrame; /* a monster joining up: this frame instead of its poses (vitals.c) */
    int movement;                   /* Character Editor > Moves on: 0 land, 1 sea (water only), 2 air (flies), 3 stationary */
    int flyPrefer;                  /* a flier's height on its way: floor 10 in the open, or the floor it's headed for */
    double levitateMs; int levitateLift, groundMovement; /* Levitate (a spell): how long still, at what height, how it moved before */
    double climbMs;                 /* a flier climbing or dropping a lift at a time */
    double bashMs;                  /* breaking a door down: till its next blow */
    const Sprite *face;
    Pose poses[MAX_POSES]; int poseCount;
    int shownPose;                  /* Change Pose: the pose kept (index into poses, -1 = idle / walking) */
    int gone;                       /* off the map (Place Character: removed, or not placed yet) */
    uint32_t poseSince;             /* when it began (its animation from the start) */
} Actor;

static char error_text[512], title[256], mapName[128];
static int mapW, mapH, tileW = 32, tileH = 32;
static int wraps; /* Maps tab: "Wraps around the edges" (Ultima V / VI): off the east edge and back on the west */
static int *floors[MAX_FLOORS + 1]; /* floors[1] = the ground; NULL = none painted */
static char sheetNames[8][128]; static int sheetCount;
static unsigned char *walkable; /* per ground cell */
static int *underTile;          /* per ground cell: the tile drawn under its tile (Tile Properties "UnderTileIndex"), -1 none */
static unsigned char *water;    /* per ground cell: Is Water (ships sail on it) */
static ObjectDef *defs; static int defCount;
static Thing *things; static int thingCount;
static Body *bodies; static int bodyCount;
static Actor player;
static Actor *npcs; static int npcCount;
static double cameraX, cameraY;
static int unitsX = 4, unitsY = 4;

/* ---------------- A map that wraps around (GameTestWindow.Wrap) ---------------- */

static double short_way(double d, double size)
{
    if (size <= 0) return d;
    d = fmod(d, size);
    if (d > size / 2) d -= size; else if (d < -size / 2) d += size;
    return d;
}
double world_wrap_dx(double dx) { return wraps ? short_way(dx, mapW * tileW) : dx; }
double world_wrap_dy(double dy) { return wraps ? short_way(dy, mapH * tileH) : dy; }
int world_wrap_cells_x(int d) { return wraps ? (int)short_way(d, mapW) : d; }
int world_wrap_cells_y(int d) { return wraps ? (int)short_way(d, mapH) : d; }
int world_wraps(void) { return wraps; }

static void foot_at(const struct Actor *a, double px, double py, int *l, int *t);

/* Someone over the seam: back onto the map on the other side (the player takes the camera with them). */
static void wrap_actor(struct Actor *a, int isPlayer)
{
    if (!wraps) return;
    double w = mapW * tileW, h = mapH * tileH, dx = 0, dy = 0;
    if (a->px < 0) dx = w; else if (a->px >= w) dx = -w;
    if (a->py < 0) dy = h; else if (a->py >= h) dy = -h;
    if (dx == 0 && dy == 0) return;
    a->px += dx; a->py += dy;
    if (isPlayer) { cameraX += dx; cameraY += dy; }
    if (a->body >= 0) { int l, t; foot_at(a, a->px, a->py, &l, &t); bodies[a->body].left = l; bodies[a->body].top = t; }
}

static void camera_update(void);
static int fly_room(int l, int t, int sx, int sy, int lift, int h);
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
        d->portable = flag(o, "IsPortable"); /* Reach: something portable doesn't block an arm's line (IsMovable) */
        d->occludes = flag(o, "Occludes");
        d->own = sprite_find(str(o, "SpriteGroup"), str(o, "SpriteName"));
        if (flag(get(o, "Instrument"), "MusicBox")) d->musicOpen = link_of(get(get(o, "Instrument"), "OpenSprite")); /* a music box, open */
        cJSON *door = get(o, "Door"), *light = get(o, "Light"), *furniture = get(get(o, "Furniture"), "Facings"), *turn = get(o, "TurnLooks"), *f;
        d->open = link_of(get(door, "OpenSprite")); d->doorStartsOpen = flag(door, "StartsOpen");
        d->doorStartsLocked = flag(door, "StartsLocked"); d->openOffX = num(door, "OpenOffsetX", 0); d->openOffY = num(door, "OpenOffsetY", 0);
        d->doorOpening = num(door, "Opening", 0); d->slideX = num(door, "SlideX", 0); d->slideY = num(door, "SlideY", 0);
        d->slideHidden = !cJSON_IsFalse(get(door, "SlideHidden")); d->motionMs = num(door, "MotionMs", 600); d->notByHand = flag(door, "NotByHand");
        d->withNeighbours = flag(door, "OpensWithNeighbours"); d->opensByItself = flag(door, "OpensByItself");
        d->opensWithin = num(door, "OpensWithinCells", 1); d->staysOpenMs = num(door, "StaysOpenMs", 1500);
        d->doorOpenSound = num(door, "OpenSoundResourceId", -1); d->doorCloseSound = num(door, "CloseSoundResourceId", -1);
        d->openingSprite = link_of(get(door, "OpeningSprite")); d->closingSprite = link_of(get(door, "ClosingSprite"));
        d->lit = link_of(get(light, "LitSprite")); d->lightStartsLit = flag(light, "StartsLit");
        d->brightness = num(light, "Brightness", 0); d->gives = num(light, "Gives", 0);
        d->spent = link_of(get(light, "SpentSprite")); d->burnMinutes = num(light, "BurnMinutes", 0); d->spentSound = num(light, "SpentSoundResourceId", -1);
        cJSON_ArrayForEach(f, furniture)
        {
            int dir = dir_of(f->string);
            d->facing[dir] = link_of(get(f, "Sprite"));
            d->back[dir] = get(get(f, "BackSprite"), "SpriteName") && str(get(f, "BackSprite"), "SpriteName")[0] ? link_of(get(f, "BackSprite")) : NULL;
            cJSON *spot;
            cJSON_ArrayForEach(spot, get(f, "Spots"))
                if (d->spotCount[dir] < 8) { d->spotX[dir][d->spotCount[dir]] = get(spot, "X") ? get(spot, "X")->valuedouble : 0; d->spotY[dir][d->spotCount[dir]++] = get(spot, "Y") ? get(spot, "Y")->valuedouble : 0; }
        }
        d->coverId = num(get(o, "Furniture"), "CoverObjectId", -1);
        d->locked = link_of(get(door, "LockedSprite"));
        cJSON *lv;
        int li = 0;
        cJSON_ArrayForEach(lv, get(get(o, "Bundle"), "Levels")) if (li < 8) d->bundle[li++] = link_of(lv);
        cJSON *win = get(o, "Window"), *lever = get(o, "Lever"), *liquid = get(o, "Liquid"), *stairs = get(o, "Stairs");
        d->winOpen = link_of(get(win, "OpenSprite")); d->winBroken = link_of(get(win, "BrokenSprite"));
        d->winShutters = flag(win, "HasShutters"); d->winStartsOpen = flag(win, "StartsOpen"); d->winBlocks = !cJSON_IsFalse(get(win, "ClosedBlocksSight"));
        cJSON_ArrayForEach(lv, get(get(o, "Plant"), "Stages")) if (d->stageCount < 16) d->stages[d->stageCount++] = link_of(lv);
        d->leverOn = link_of(get(lever, "OnSprite")); d->leverKind = num(lever, "Kind", 0); d->leverStartsOn = flag(lever, "StartsOn");
        d->working = link_of(get(get(o, "Crafting"), "WorkingSprite")); d->laidOut = link_of(get(get(o, "Bedroll"), "LaidOutSprite")); d->usedUp = link_of(get(get(o, "Useable"), "UsedUpSprite"));
        cJSON_ArrayForEach(lv, get(liquid, "Liquids")) if (d->liquidCount < 8) { d->liquidFood[d->liquidCount] = num(lv, "FoodId", -1); d->liquidLook[d->liquidCount++] = link_of(get(lv, "Sprite")); }
        d->liquidStart = num(liquid, "StartsFullOfId", -1);
        d->stairsKind = num(stairs, "Kind", 0); d->risesToward = num(stairs, "RisesToward", 0);
        d->trapHidden = flag(get(o, "Trap"), "Hidden");
        if (d->type == T_STAIRS && d->stairsKind == 0 && num(stairs, "DesignId", -1) >= 0)
        {
            cJSON *design;
            cJSON_ArrayForEach(design, get(get(project, "Objects"), "StairDesigns")) if (num(design, "Id", -2) == num(stairs, "DesignId", -1)) d->design = design;
        }
        cJSON_ArrayForEach(f, turn) d->turned[dir_of(f->string)] = link_of(f);
    }
}

/* ObjectLooks.SpriteFor: its look for its state and facing. */
static const Sprite *look_of(const Thing *t)
{
    if (t->lookOverride) return t->lookOverride;
    const ObjectDef *d = t->def;
    if (!d) return t->sprite;
    const Sprite *own = d->turned[t->facing] ? d->turned[t->facing] : d->own;
    switch (d->type)
    {
        case T_DOOR:
            if (t->open && d->doorOpening == 2) return own; /* slid away: the door itself, moved */
            return t->open ? (d->open ? d->open : own) : t->locked && d->locked ? d->locked : own;
        case T_LIGHT: return t->spent ? (d->spent ? d->spent : own) : t->lit && d->lit ? d->lit : own;
        case T_INSTRUMENT: return t->lit && d->musicOpen ? d->musicOpen : own; /* a music box, open */
        case T_AMMO: case T_MONEY: case T_LOCKPICK:
        {
            int q = t->quantity > 1 ? t->quantity : 1, level = q > 12 ? 7 : q > 6 ? 6 : q - 1;
            for (; level >= 0; level--) if (d->bundle[level]) return d->bundle[level];
            return own;
        }
        case T_WINDOW: return t->spent ? (d->winBroken ? d->winBroken : own) : t->open && d->winShutters && d->winOpen ? d->winOpen : own;
        case T_PLANT: return d->stageCount && d->stages[t->stage < 0 ? 0 : t->stage >= d->stageCount ? d->stageCount - 1 : t->stage] ? d->stages[t->stage < 0 ? 0 : t->stage >= d->stageCount ? d->stageCount - 1 : t->stage] : own;
        case T_LEVER: return t->open && d->leverOn ? d->leverOn : own;
        case T_STATION: return t->lit && d->working ? d->working : own;
        case T_BEDROLL:
            if (!t->open) return own;
            if (d->facing[t->facing]) return d->facing[t->facing];
            return d->laidOut ? d->laidOut : own;
        case T_USEABLE: return t->spent && d->usedUp ? d->usedUp : own;
        case T_LIQUID:
            for (int i = 0; i < d->liquidCount; i++) if (d->liquidFood[i] == t->liquid && t->liquid >= 0) return d->liquidLook[i] ? d->liquidLook[i] : own;
            return own;
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

static int nextUid = 1, thingCap, bodyCap, slabStart, slabEnd;
static void settle_all(void);
static void doors_update(double ms);
static void door_set_open(Thing *t, int open);
static void spawn_bed_covers(void);
static cJSON *containersJson, *booksJson;

static void stair_bodies(Thing *t, Body *out, int max, int *n);

static void ensure_bodies(int more)
{
    if (bodyCount + more < bodyCap) return;
    while (bodyCount + more >= bodyCap) bodyCap = bodyCap * 2 + 16;
    bodies = (Body *)realloc(bodies, sizeof(Body) * (size_t)bodyCap);
}

static void add_thing_body(Thing *t)
{
    t->body = -1;
    t->bodyN = 1;
    if (t->def && t->def->type == T_STAIRS && t->solid && !t->hangs && !t->staysPut)
    {
        Body steps[64];
        int n = 0;
        stair_bodies(t, steps, 64, &n);
        if (n > 0)
        {
            ensure_bodies(n);
            t->body = bodyCount; t->bodyN = n;
            for (int i = 0; i < n; i++) bodies[bodyCount++] = steps[i];
            return;
        }
    }
    int door = t->def && t->def->type == T_DOOR && t->def->solid;
    if ((!t->solid && !door) || t->hangs || t->staysPut) return; /* hung on the wall, or staying where it's put (the same): in nobody's way */
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
    a->movement = num(character, "Movement", 0);
    a->flyPrefer = FLY_LIFT;
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
/* Reach's walk (world_reachable): only the boxes round the player are looked at for each of its many steps. */
static int stepWindowOn, stepWindowL, stepWindowT, stepWindowR, stepWindowB, *stepWindow, stepWindowN, stepWindowCap;

static int try_step(int l, int t, int w, int d, int lift, int height)
{
    int W = mapW * unitsX, H = mapH * unitsY;
    if (wraps && (l < 0 || t < 0 || l + w > W || t + d > H) && w <= W && d <= H)
    {
        /* TryStepAcrossSeam: over the seam of a map that wraps around, each piece on either side must take it; it stands at the highest */
        if (l < -W || t < -H || l > 2 * W || t > 2 * H) return -1;
        int xs[2][2], ys[2][2], nx = 0, ny = 0, highest = -1;
        if (l < 0) { xs[nx][0] = l + W; xs[nx++][1] = W; xs[nx][0] = 0; xs[nx++][1] = l + w; }
        else if (l + w > W) { xs[nx][0] = l; xs[nx++][1] = W; xs[nx][0] = 0; xs[nx++][1] = l + w - W; }
        else { xs[nx][0] = l; xs[nx++][1] = l + w; }
        if (t < 0) { ys[ny][0] = t + H; ys[ny++][1] = H; ys[ny][0] = 0; ys[ny++][1] = t + d; }
        else if (t + d > H) { ys[ny][0] = t; ys[ny++][1] = H; ys[ny][0] = 0; ys[ny++][1] = t + d - H; }
        else { ys[ny][0] = t; ys[ny++][1] = t + d; }
        for (int i = 0; i < nx; i++)
            for (int k = 0; k < ny; k++)
            {
                if (xs[i][1] <= xs[i][0] || ys[k][1] <= ys[k][0]) continue;
                int part = try_step(xs[i][0], ys[k][0], xs[i][1] - xs[i][0], ys[k][1] - ys[k][0], lift, height);
                if (part < 0) return -1;
                if (part > highest) highest = part;
            }
        return highest;
    }
    if (l < 0 || t < 0 || l + w > mapW * unitsX || t + d > mapH * unitsY) return -1;
    static const Body *here[512], *solids[2048];
    int ns = 0;
    if (stepWindowOn && l >= stepWindowL && t >= stepWindowT && l + w <= stepWindowR && t + d <= stepWindowB)
    { for (int k = 0; k < stepWindowN && ns < 2048; k++) if (overlaps(&bodies[stepWindow[k]], l, t, w, d)) solids[ns++] = &bodies[stepWindow[k]]; }
    else for (int i = 0; i < bodyCount && ns < 2048; i++) if (overlaps(&bodies[i], l, t, w, d)) solids[ns++] = &bodies[i];
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

int world_build(cJSON *root);

int world_load(const char *folder)
{
    if (!sprites_load(folder)) { snprintf(error_text, sizeof error_text, "Couldn't read %s/sprites.sdbx (missing, damaged, or not this game's).", folder); return 0; }
    char path[1100];
    snprintf(path, sizeof path, "%s/game.sdbx", folder);
    char *text = (char *)pack_read(path, NULL);
    if (!text) { snprintf(error_text, sizeof error_text, "Couldn't read %s (missing, damaged, or not this game's).", path); return 0; }
    cJSON *root = cJSON_Parse(text);
    free(text);
    if (!root) { snprintf(error_text, sizeof error_text, "game.sdbx is damaged."); return 0; }
    return world_build(root);
}

/* The world from a game's data (game.sdbx's, or a save's). */
/* ---------------- Spawn points (GameTestWindow.Spawns.cs) ---------------- */

enum { S_LOITER_TYPE = 11, S_WANDER_TYPE = 12 }; /* ScheduleType.Loiter / Wander */
static unsigned spawnRng = 2166136261u;
static int spawn_rnd(int n) { spawnRng = spawnRng * 1103515245u + 12345u; return n > 0 ? (int)((spawnRng >> 8) % (unsigned)n) : 0; }

/* As Ultima VII's monster eggs: the first time the map is built, each of a point's characters has its chance for each of how
   many - a copy of its own (id: the character's + 1000000 x (500 + n), clear of the copies of characters placed more than once)
   by the point, its timetable the point's all day: Loiter within its reach, or, free, Wander unbounded. */
static Actor *actor_of(int i);

/* Whether a cell is taken: by a placement on the map (while the map is built), or by someone on it (in play). */
static int spawn_cell_taken(cJSON *placed, int x, int y, int onFloor)
{
    if (placed)
    {
        cJSON *p;
        cJSON_ArrayForEach(p, placed) if (!flag(p, "Dead") && num(p, "X", -1) == x && num(p, "Y", -1) == y && num(p, "Floor", 1) == onFloor) return 1;
        return 0;
    }
    for (int a = 0; a <= npcCount; a++)
    {
        Actor *who = actor_of(a);
        if (!who || who->gone) continue;
        if ((int)floor(who->px / tileW) == x && (int)floor(who->py / tileH) == y && floor_of(who->lift < 0 ? 0 : who->lift) == onFloor) return 1;
    }
    return 0;
}

/* SpawnCopies: a monster egg's characters, each by its chance for each of how many, a copy of its own on a free cell by the egg
   (its own first, then the rings round it), its timetable the egg's all day. placed: the map's placements (as the map is built,
   a placement each); NULL: in play (each put on the map at once). */
static void spawn_copies(cJSON *spawn, cJSON *characters, cJSON *placed)
{
    int sx = num(spawn, "X", 0), sy = num(spawn, "Y", 0), sf = num(spawn, "Floor", 1), isFree = flag(spawn, "Free");
    int reach = num(spawn, "WanderTiles", 12);
    cJSON *entry;
    cJSON_ArrayForEach(entry, get(spawn, "Entries"))
    {
        cJSON *kind = NULL, *ch;
        cJSON_ArrayForEach(ch, characters) if (num(ch, "Id", -2) == num(entry, "CharacterId", -1)) { kind = ch; break; }
        if (!kind || flag(kind, "IsTestPlayer")) continue;
        int count = num(entry, "Count", 1);
        for (int k = 0; k < (count > 1 ? count : 1); k++)
        {
            if (spawn_rnd(100) >= num(entry, "Percent", 100)) continue;
            int at = -1, ax = 0, ay = 0;
            for (int ring = 0; ring <= 3 && at < 0; ring++)
                for (int dy = -ring; dy <= ring && at < 0; dy++)
                    for (int dx = -ring; dx <= ring && at < 0; dx++)
                    {
                        if ((abs(dx) > abs(dy) ? abs(dx) : abs(dy)) != ring) continue;
                        int x = sx + dx, y = sy + dy;
                        if (x < 0 || y < 0 || x >= mapW || y >= mapH) continue;
                        if (!spawn_cell_taken(placed, x, y, sf)) { at = 1; ax = x; ay = y; }
                    }
            if (at < 0) break;
            int base = num(kind, "Id", -1) % COPY_STEP, copies = 500;
            cJSON_ArrayForEach(ch, characters) if (num(ch, "Id", -1) % COPY_STEP == base && num(ch, "Id", -1) / COPY_STEP >= copies) copies = num(ch, "Id", -1) / COPY_STEP + 1;
            cJSON *copy = cJSON_Duplicate(kind, 1);
            cJSON_ReplaceItemInObject(copy, "Id", cJSON_CreateNumber(base + COPY_STEP * copies));
            if (get(copy, "IsInParty")) cJSON_ReplaceItemInObject(copy, "IsInParty", cJSON_CreateFalse());
            cJSON *schedule = cJSON_CreateArray();
            for (int period = 0; period < 8; period++)
            {
                cJSON *e = cJSON_CreateObject();
                cJSON_AddNumberToObject(e, "Period", period);
                cJSON_AddNumberToObject(e, "Type", isFree ? S_WANDER_TYPE : S_LOITER_TYPE); /* ScheduleType Wander / Loiter */
                cJSON_AddNumberToObject(e, "X", sx); cJSON_AddNumberToObject(e, "Y", sy);
                cJSON_AddNumberToObject(e, "WorkAreaTiles", isFree ? 0 : (reach > 1 ? reach : 1) * (tileW / 8 > 1 ? (int)floor(tileW / 8.0 + 0.5) : 1)); /* map squares as Exult tiles */
                cJSON_AddItemToArray(schedule, e);
            }
            cJSON_DeleteItemFromObject(copy, "Schedules");
            cJSON_AddItemToObject(copy, "Schedules", schedule);
            cJSON_AddItemToArray(characters, copy);
            if (!placed)
            {
                /* in play: on the map now, with its things, health and timetable (BringOntoMap) */
                items_add_carrier(copy);
                vitals_add(base + COPY_STEP * copies);
                int actor = world_add_actor(copy, ax, ay, sf);
                if (actor > 0) schedules_add(actor);
                continue;
            }
            int top = 0;
            cJSON *p;
            cJSON_ArrayForEach(p, placed) if (num(p, "Id", 0) > top) top = num(p, "Id", 0);
            cJSON *placement = cJSON_CreateObject();
            cJSON_AddNumberToObject(placement, "Id", top + 1);
            cJSON_AddNumberToObject(placement, "CharacterId", base + COPY_STEP * copies);
            cJSON_AddNumberToObject(placement, "X", ax); cJSON_AddNumberToObject(placement, "Y", ay);
            cJSON_AddNumberToObject(placement, "Floor", sf);
            cJSON_AddNumberToObject(placement, "Facing", DIR_S);
            cJSON_AddItemToArray(placed, placement);
        }
    }
}

static void set_flag(cJSON *o, const char *k, int v) { cJSON_DeleteItemFromObject(o, k); cJSON_AddItemToObject(o, k, cJSON_CreateBool(v)); }

/* HatchSpawnPoints: as the map is built, the monster eggs set off as it's entered (CachedIn) and not hatched - their characters among
   the placements. A nocturnal one waits for the eggs' first look (eggs.c), which knows the hour. */
static void hatch_spawn_points(cJSON *map, cJSON *characters)
{
    cJSON *spawn, *placed = get(map, "Characters");
    if (!cJSON_IsArray(placed)) { cJSON_DeleteItemFromObject(map, "Characters"); placed = cJSON_AddArrayToObject(map, "Characters"); }
    cJSON_ArrayForEach(spawn, get(map, "SpawnPoints"))
    {
        if (num(spawn, "Kind", 0) != 0 || num(spawn, "Criteria", 0) != 0 || flag(spawn, "Nocturnal") || flag(spawn, "HatchedRuntime") || flag(spawn, "GoneRuntime")) continue;
        set_flag(spawn, "HatchedRuntime", 1);
        if (flag(spawn, "Once")) set_flag(spawn, "GoneRuntime", 1);
        if (spawn_rnd(100) + 1 > num(spawn, "Probability", 100)) continue;
        spawn_copies(spawn, characters, placed);
    }
}

/* A monster egg hatching in play (eggs.c): its characters on the map at once. */
void world_spawn_now(void *spawn)
{
    spawn_copies((cJSON *)spawn, get(get(get(gameJson, "Project"), "Characters"), "Characters"), NULL);
}

int world_floor_of_lift(int lift) { return floor_of(lift < 0 ? 0 : lift); }

int world_build(cJSON *root)
{
    cJSON *project = get(root, "Project"), *map = get(project, "Map");
    snprintf(title, sizeof title, "%s", str(get(project, "Manifest"), "ProjectName"));
    snprintf(mapName, sizeof mapName, "%s", str(get(project, "Map"), "MapName"));
    if (!map) { snprintf(error_text, sizeof error_text, "The game has no map."); return 0; }
    read_objects(project);

    mapW = num(map, "Width", 1); mapH = num(map, "Height", 1);
    wraps = flag(map, "WrapsAround");
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
    water = (unsigned char *)calloc((size_t)(mapW * mapH), 1);
    underTile = (int *)malloc(sizeof(int) * (size_t)(mapW * mapH));
    cJSON *byKey = get(get(project, "TileProperties"), "ByTileKey");
    for (int i = 0; i < mapW * mapH; i++)
    {
        walkable[i] = 1;
        underTile[i] = -1;
        int tile = floors[1] ? floors[1][i] : -1;
        if (tile < 0) continue;
        char key[200];
        int slot = tile / 100000;
        snprintf(key, sizeof key, "%s:%d", slot < sheetCount ? sheetNames[slot] : "", tile % 100000);
        cJSON *props = get(byKey, key);
        if (props && !flag(props, "Walkable")) walkable[i] = 0;
        if (props && flag(props, "IsWater")) water[i] = 1;
        if (props && num(props, "UnderTileIndex", -1) >= 0) underTile[i] = slot * 100000 + num(props, "UnderTileIndex", -1);
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
        t->placed = o; /* its Map Editor entry (a save writes it back) */
        t->uid = nextUid++; t->category = 4; t->itemId = d->id; t->instanceId = num(o, "InstanceId", 0);
        if (cJSON_IsNumber(get(o, "HitPointsRuntime")) && num(o, "HitPointsRuntime", -1) >= 0) { t->hp = num(o, "HitPointsRuntime", 0); t->hpSet = 1; }
        t->quantity = num(o, "Quantity", 1) > 0 ? num(o, "Quantity", 1) : 1;
        t->x = num(o, "X", 0); t->y = num(o, "Y", 0); t->z = num(o, "Z", 0);
        t->offX = num(o, "OffsetX", 0); t->offY = num(o, "OffsetY", 0); t->facing = num(o, "Facing", DIR_S) & 7;
        cJSON *so = get(o, "StartsOpen"), *sl = get(o, "StartsLit"), *sk = get(o, "StartsLocked"), *lr = get(o, "IsLockedRuntime");
        t->locked = d->type == T_DOOR && (cJSON_IsBool(lr) ? cJSON_IsTrue(lr) : cJSON_IsBool(sk) ? cJSON_IsTrue(sk) : d->doorStartsLocked);
        t->open = d->type == T_DOOR && (cJSON_IsBool(so) ? cJSON_IsTrue(so) : d->doorStartsOpen);
        cJSON *litNow = get(o, "IsLitRuntime");
        t->lit = d->type == T_LIGHT && (cJSON_IsBool(litNow) ? cJSON_IsTrue(litNow) : cJSON_IsBool(sl) ? cJSON_IsTrue(sl) : d->lightStartsLit);
        t->spent = flag(o, "IsSpentRuntime");
        t->sprung = flag(o, "TrapSprungRuntime");
        cJSON *openNow = get(o, "IsOpenRuntime");
        if (d->type == T_WINDOW) t->open = d->winShutters && (cJSON_IsBool(openNow) ? cJSON_IsTrue(openNow) : cJSON_IsBool(so) ? cJSON_IsTrue(so) : d->winStartsOpen);
        if (d->type == T_LEVER) t->open = d->leverKind != 2 && (cJSON_IsBool(openNow) ? cJSON_IsTrue(openNow) : cJSON_IsBool(so) ? cJSON_IsTrue(so) : d->leverStartsOn);
        if (d->type == T_BEDROLL) t->open = cJSON_IsTrue(openNow);
        t->stage = num(o, "StartStage", 0);
        if (t->stage >= d->stageCount) t->stage = d->stageCount > 0 ? d->stageCount - 1 : 0;
        if (t->stage < 0) t->stage = 0;
        t->grow = cJSON_IsNumber(get(o, "GrowMinutesRuntime")) ? get(o, "GrowMinutesRuntime")->valuedouble : 0;
        cJSON *full = get(o, "LiquidIdRuntime");
        if (!cJSON_IsNumber(full)) full = get(o, "StartsFullOfId");
        t->liquid = d->type != T_LIQUID ? -1 : cJSON_IsNumber(full) ? full->valueint : d->liquidStart;
        if (d->type == T_KEYCHAIN || d->type == T_PLANT) t->fixedFrame = 0;
        if (d->type == T_KEYCHAIN && cJSON_IsNumber(get(o, "FrameRuntime")) && num(o, "FrameRuntime", 0) > 0) t->fixedFrame = num(o, "FrameRuntime", 0) + 1;
        if (d->design)
        {
            /* StairDesign.Resolve: its own pose for the way it faces, else the first with steps */
            cJSON *pose, *first = NULL;
            cJSON_ArrayForEach(pose, get(d->design, "Poses"))
                if (cJSON_GetArraySize(get(pose, "Steps")) > 0) { if (!first) first = pose; if (num(pose, "Facing", -1) == t->facing && !t->steps) t->steps = get(pose, "Steps"); }
            if (!t->steps && first) t->steps = get(first, "Steps");
        }
        t->burnLeft = get(o, "BurnLeftRuntime") && cJSON_IsNumber(get(o, "BurnLeftRuntime")) ? get(o, "BurnLeftRuntime")->valuedouble : -1;
        int turned = (d->turned[0] || d->turned[2] || d->turned[4] || d->turned[6]) && (t->facing == DIR_E || t->facing == DIR_W);
        t->sizeX = turned ? d->sizeY : d->sizeX; t->sizeY = turned ? d->sizeX : d->sizeY;
        int flat = d->type == T_TRAP || (d->type == T_TILE && !d->solid);
        t->sizeZ = flat ? 0 : d->sizeZ;
        t->solid = d->solid && !(d->type == T_DOOR && t->open);
        t->solidOffX = turned ? d->solidOffY : d->solidOffX; t->solidOffY = turned ? d->solidOffX : d->solidOffY;
        t->hangs = flag(o, "HangsOnWall"); t->staysPut = flag(o, "StaysPut");
        t->elevates = d->elevates; t->liesUnder = flag(o, "LiesUnder"); t->wallSetPiece = flag(o, "WallSetPiece"); t->vehicle = num(o, "VehicleStateId", 0) > 0 ? num(o, "VehicleStateId", 0) : 0;
        t->homeFloor = floor_of(t->z < 0 ? 0 : t->z);
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
        t->placed = o;
        t->sprite = sprite_find(str(data, "SpriteGroup"), str(data, "SpriteName"));
        t->x = num(o, "X", 0); t->y = num(o, "Y", 0); t->z = num(o, "Z", 0); t->offX = num(o, "OffsetX", 0); t->offY = num(o, "OffsetY", 0);
        t->sizeX = num(data, "SizeX", 1); t->sizeY = num(data, "SizeY", 1); t->sizeZ = num(data, "SizeZ", 1);
        t->solid = flag(data, "IsSolid"); t->solidOffX = num(data, "SolidOffsetX", 0); t->solidOffY = num(data, "SolidOffsetY", 0);
        t->hangs = flag(o, "HangsOnWall"); t->staysPut = flag(o, "StaysPut");
        t->elevates = flag(data, "AllowElevation"); t->liesUnder = flag(o, "LiesUnder");
        t->homeFloor = floor_of(t->z < 0 ? 0 : t->z);
        add_thing_body(t);
    }
    /* Upper floors' painted tiles: slabs one lift thick under their floor's height (BuildFloorSlabs). */
    slabStart = bodyCount;
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

    slabEnd = bodyCount;
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
    hatch_spawn_points(map, characters); /* spawn points not hatched yet: their characters' chances */
    cJSON *placed = get(map, "Characters");
    npcs = (Actor *)calloc((size_t)(cJSON_GetArraySize(placed) + cJSON_GetArraySize(characters) + 1), sizeof(Actor));
    cJSON_ArrayForEach(o, placed)
    {
        if (cJSON_IsTrue(get(o, "Dead"))) continue;
        cJSON *who = NULL;
        cJSON_ArrayForEach(ch, characters) if (num(ch, "Id", -2) == num(o, "CharacterId", -1)) { who = ch; break; }
        if (!who || flag(who, "IsTestPlayer")) continue;
        /* The same character placed more than once: every copy after the first is its own instance (a prefab) - its own
           health, belongings and fights. Its runtime id: the character's + 1000000 x n, so lookups by id still find the first. */
        int copies = 0;
        for (int i = 0; i < npcCount; i++) if (npcs[i].characterId % COPY_STEP == num(who, "Id", -2)) copies++;
        if (copies && num(who, "Id", -2) < COPY_STEP)
        {
            cJSON *copy = cJSON_Duplicate(who, 1);
            cJSON_ReplaceItemInObject(copy, "Id", cJSON_CreateNumber(num(who, "Id", -2) + COPY_STEP * copies));
            cJSON_AddItemToArray(characters, copy);
            who = copy;
        }
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
    /* Everyone else (never placed on this map): off it, till Place Character brings them in. */
    cJSON_ArrayForEach(ch, characters)
    {
        if (flag(ch, "IsTestPlayer")) continue;
        int placedAlready = 0;
        for (int i = 0; i < npcCount; i++) if (npcs[i].characterId == num(ch, "Id", -2)) placedAlready = 1;
        if (placedAlready) continue;
        Actor *a = &npcs[npcCount++];
        read_actor_poses(ch, poses, a);
        a->gone = 1;
        a->body = bodyCount;
        Body *b = &bodies[bodyCount++];
        memset(b, 0, sizeof *b); b->z = -100000;
    }
    gameJson = root; /* kept: the events run from it */
    settle_all(); /* every authored thing comes to rest: stacks form in placement order */
    spawn_bed_covers();
    cameraX = cameraY = 0; camera_update(); /* from the map's corner, as the Windows engine's camera starts */
    return 1;
}

/* ---------------- Each frame ---------------- */

static int keysHeld;
static int cameraFollow, cameraHeld; static double holdX, holdY;
static void player_sit_update(double seconds);
int world_seated(int actor);
void world_stand_up(int actor);
void world_cancel_player_sit(void);
int world_keys_held(void) { return keysHeld; }
void world_keys_let_go(void) { keysHeld = 0; }
static Actor *actor_of(int i);
void world_walks_update(double seconds);
void world_walk_cancel(int actor);
int world_walking(int actor);

static int ask_aside(int walker, int l, int t, int dx, int dy);

void world_update(int dx, int dy, double seconds)
{
    if (seconds > 0) keysHeld = dx || dy;
    if (dx || dy) { if (world_seated(0)) world_stand_up(0); world_cancel_player_sit(); } /* moving gets up again */
    player_sit_update(seconds);
    if (seconds > 0) doors_update(seconds * 1000);
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
            int lift = player.levitateMs > 0 ? fly_room(l1, t1, player.sizeX, player.sizeY, player.lift, player.sizeZ) /* floating: room at this height */
                                             : try_step(l1, t1, player.sizeX, player.sizeY, player.lift, player.sizeZ);
            if (lift >= 0) { player.px = nx; player.py = ny; player.lift = lift; wrap_actor(&player, 1); }
            else ask_aside(0, l1, t1, dx, dy); /* Actor::is_really_blocked: a character in the way is asked to move aside */
        }
    }
    world_walks_update(seconds);
    camera_update();
}

/* The camera (GameTestWindow.UpdateCamera): a dead zone round the middle, following where the player (or whoever it follows) is
   drawn (up and left by its lift); kept inside the map. It is never centred: a new game's starts at the map's corner. */
static void camera_update(void)
{
    const Actor *followed = cameraFollow > 0 ? actor_of(cameraFollow) : NULL;
    if (!followed) followed = &player;
    double fx = followed->px - followed->lift * LIFT_SHIFT, fy = followed->py - followed->lift * LIFT_SHIFT;
    double rx = fx - cameraX, ry = fy - cameraY;
    if (rx < SCREEN_W / 2.0 - DEAD_HALF_W) cameraX -= SCREEN_W / 2.0 - DEAD_HALF_W - rx;
    else if (rx > SCREEN_W / 2.0 + DEAD_HALF_W) cameraX += rx - (SCREEN_W / 2.0 + DEAD_HALF_W);
    if (ry < SCREEN_H / 2.0 - DEAD_HALF_H) cameraY -= SCREEN_H / 2.0 - DEAD_HALF_H - ry;
    else if (ry > SCREEN_H / 2.0 + DEAD_HALF_H) cameraY += ry - (SCREEN_H / 2.0 + DEAD_HALF_H);
    if (cameraHeld) { cameraX = holdX - SCREEN_W / 2.0; cameraY = holdY - SCREEN_H / 2.0; } /* Move Camera: centred on its spot */
    double worldW = mapW * tileW, worldH = mapH * tileH;
    if (!(wraps && worldW > SCREEN_W)) cameraX = worldW <= SCREEN_W ? (worldW - SCREEN_W) / 2 : fmin(fmax(cameraX, 0), worldW - SCREEN_W);
    if (!(wraps && worldH > SCREEN_H)) cameraY = worldH <= SCREEN_H ? (worldH - SCREEN_H) / 2 : fmin(fmax(cameraY, 0), worldH - SCREEN_H);
}

/* ---------------- Drawing ---------------- */

static uint32_t *screen;

typedef struct Puddle { int ux, uy, lift, frame, mirrored; const Sprite *blood; } Puddle;
static Puddle puddles[128]; static int puddleCount;
typedef struct Bleeding { int actor, lastHp; double left, next; } Bleeding;
static Bleeding bleeding[32]; static int bleedingCount;
static void draw_status_outlines(uint32_t *pixels);
static int blitClip, blitClipL, blitClipT, blitClipR, blitClipB; /* the next blit shows only this box, in the game's pixels (a sliding door, into its wall) */
static int blitOpacity = 255, blitMirror; /* the next blits: how see-through (invisible in the party), mirrored (blood) */

/* The sky's light (lighting_shade), drawn into the world as it's drawn: each picture's darkened copy, made again only when
   the light changes (shadeGen); the same as the Windows engine's overlay over the finished map, without a pass over it. */
static int shadeOn, blitShading;
static uint32_t shadeKeep, shadeRB, shadeG, shadeGen = 1;
static inline uint32_t shade_px(uint32_t c)
{
    return (c & 0xFF000000u) | ((((c & 0xFF00FFu) * shadeKeep + shadeRB) >> 8) & 0xFF00FFu) | ((((c & 0xFF00u) * shadeKeep + shadeG) >> 8) & 0xFF00u);
}
static void shade_update(void)
{
    uint32_t keep = 256, rb = 0, g = 0;
    int on = lighting_shade(&keep, &rb, &g);
    if (on != shadeOn || keep != shadeKeep || rb != shadeRB || g != shadeG) { if (++shadeGen == 0) shadeGen = 1; }
    shadeOn = on; shadeKeep = keep; shadeRB = rb; shadeG = g;
}
static const uint32_t *shaded_pixels(const Picture *p)
{
    Picture *q = (Picture *)p;
    if (q->shaded && q->shadeGen == shadeGen) return q->shaded;
    size_t n = (size_t)p->width * (size_t)p->height;
    if (!q->shaded) q->shaded = (uint32_t *)malloc(n * 4);
    if (!q->shaded) return p->pixels;
    for (size_t i = 0; i < n; i++) q->shaded[i] = shade_px(p->pixels[i]);
    q->shadeGen = shadeGen;
    return q->shaded;
}
uint32_t world_shade_colour(uint32_t c) { return shadeOn ? shade_px(c) : c; }

/* A picture fitted (keeping its shape, centred, nearest pixel) into a box at (x, y). */
static void blit_box(const Picture *p, int x, int y, int bw, int bh)
{
    if (!p || p->width <= 0 || p->height <= 0) return;
    /* in the picture's own pixels from here: art finer than 320 x 200 keeps all of them on a HIRES machine */
    x *= frame_scale; y *= frame_scale; bw *= frame_scale; bh *= frame_scale;
    double s = fmin((double)bw / p->width, (double)bh / p->height);
    int dw = (int)(p->width * s + 0.5), dh = (int)(p->height * s + 0.5);
    int ox = x + (bw - dw) / 2, oy = y + (bh - dh) / 2;
    /* clipped to the screen once: the columns and rows of it that show */
    int i0 = ox < 0 ? -ox : 0, i1 = ox + dw > FRAME_W ? FRAME_W - ox : dw;
    int j0 = oy < 0 ? -oy : 0, j1 = oy + dh > FRAME_H ? FRAME_H - oy : dh;
    if (blitClip)
    {
        int cl = blitClipL * frame_scale - ox, cr = blitClipR * frame_scale - ox, ct = blitClipT * frame_scale - oy, cb = blitClipB * frame_scale - oy;
        if (i0 < cl) i0 = cl;
        if (i1 > cr) i1 = cr;
        if (j0 < ct) j0 = ct;
        if (j1 > cb) j1 = cb;
    }
    if (i0 >= i1 || j0 >= j1) return;
    int same = dw == p->width && dh == p->height;
    const uint32_t *src = blitShading && shadeOn ? shaded_pixels(p) : p->pixels;
    if (same && p->opaque && !blitMirror && blitOpacity >= 255)
    {
        for (int j = j0; j < j1; j++) memcpy(&screen[(oy + j) * FRAME_W + ox + i0], src + (size_t)j * (size_t)p->width + i0, sizeof(uint32_t) * (size_t)(i1 - i0)); /* a whole tile: its rows as they are */
        return;
    }
    uint32_t stepX = ((uint32_t)p->width << 16) / (uint32_t)dw, stepY = ((uint32_t)p->height << 16) / (uint32_t)dh;
    uint32_t op = (uint32_t)blitOpacity, op1 = op + (op >> 7);
    const int wholeRows = p->opaque && op >= 255; /* nothing under it shows: a row repeated is the row before, copied */
    int lastRow = -1;
    for (int j = j0; j < j1; j++)
    {
        int sj = same ? j : (int)(((uint32_t)j * stepY) >> 16);
        if (sj >= p->height) sj = p->height - 1;
        const uint32_t *row = src + (size_t)sj * (size_t)p->width;
        uint32_t *d = &screen[(oy + j) * FRAME_W + ox];
        if (wholeRows && sj == lastRow) { memcpy(d + i0, d - FRAME_W + i0, sizeof(uint32_t) * (size_t)(i1 - i0)); continue; }
        lastRow = sj;
        for (int i = i0; i < i1; i++)
        {
            int ci = blitMirror ? dw - 1 - i : i;
            uint32_t c = row[same ? ci : (int)(((uint32_t)ci * stepX) >> 16)], a = c >> 24;
            if (op < 255) a = (a * op1) >> 8;
            if (a == 0) continue;
            d[i] = a >= 255 ? c : blend_px(d[i], c, a);
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
    int clip, cl, ct, cr, cb; /* shown only in this box (a sliding door hidden past where it stands) */
    int opacity, mirror; /* 0: opaque, else 1..254 (an invisible party member: a shimmer); a mirrored picture */
    int thing; /* the thing drawn (its uid), 0 for anything else */
    int band;  /* its floor's band (floor - 1) */
    int slab, sl, sr, sf, sn; /* an upper floor's tiles (UpperFloorOrder): its tile box in 8px units */
    int order; /* at the same depth, the later drawn over (the Windows engine: the order its visuals were added) */
} Drawn;

static int by_key(const void *a, const void *b)
{
    const Drawn *p = (const Drawn *)a, *q = (const Drawn *)b;
    if (p->key != q->key) return p->key < q->key ? -1 : 1;
    return p->order < q->order ? -1 : p->order > q->order ? 1 : 0; /* never left to chance: qsort isn't stable, and a tie flickered */
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

/* SpriteAnimator's frame sounds: a frame that has a sound plays it each time it comes round (key: a thing's uid, or -(actor + 2)). */
typedef struct FrameSeen { int key; const Sprite *sprite; int frame; } FrameSeen;
static FrameSeen seen[1024]; static int seenCount;

/* GameTestWindow.Hearing (Exult audio/Audio.cc get_2d_position_for_tile, AudioChannel::calculate2DVolume): heard from the
   middle of the view, the distance on the screen in 8px tiles (a lift up is half a tile up and left); past 24 tiles not at
   all; nearer, distance = tiles squared * 256 / 24^2 (+ 8 a lift the view is up), volume (256 - distance) / 256; balance
   (2 dx - lifts) * 32 / 5, the other side turned down. 0: too far. */
static Thing *thing_by_uid(int uid);
int world_hearing_at(double drawnX, double drawnY, double *volume, double *balance)
{
    double dx = world_wrap_dx(drawnX - (cameraX + SCREEN_W / 2.0)), dy = world_wrap_dy(drawnY - (cameraY + SCREEN_H / 2.0));
    double hx = dx / (UNIT / 2.0), hy = dy / (UNIT / 2.0);
    double squared = (hx * hx + hy * hy) / 4;
    if (squared > 24.0 * 24.0) return 0;
    double distance = squared * 256 / (24.0 * 24.0);
    const Actor *ear = cameraFollow > 0 ? actor_of(cameraFollow) : NULL;
    int lift = ear ? ear->lift : player.lift;
    if (lift > 0) { distance += lift * 8; if (distance > 256) distance = 256; }
    *volume = (256 - distance) / 256;
    double b = hx * 32 / 5 / 256;
    *balance = b < -1 ? -1 : b > 1 ? 1 : b;
    return 1;
}

/* A sound from a character (the main character's own: as it is) or a thing on the map, heard from where it's drawn. */
void world_sound_from_actor(int resourceId, int actor)
{
    if (resourceId < 0) return;
    const Actor *a = actor_of(actor);
    if (!a || actor == 0) { sound_play(resourceId); return; }
    double v, b;
    if (world_hearing_at(a->px - a->lift * LIFT_SHIFT, a->py - a->lift * LIFT_SHIFT, &v, &b)) sound_play_at(resourceId, v, b);
}

void world_sound_at_thing(int resourceId, int uid)
{
    if (resourceId < 0) return;
    Thing *t = thing_by_uid(uid);
    if (!t) { sound_play(resourceId); return; }
    double px = t->x * tileW + tileW / 2.0 + t->offX * UNIT - t->z * LIFT_SHIFT, py = t->y * tileH + tileH / 2.0 + t->offY * UNIT - t->z * LIFT_SHIFT, v, b;
    if (world_hearing_at(px, py, &v, &b)) sound_play_at(resourceId, v, b);
}

/* A sound from a point on the map (map pixels, at a lift): a shot in the air. */
void world_sound_at_point(int resourceId, double px, double py, int lift)
{
    double v, b;
    if (resourceId >= 0 && world_hearing_at(px - lift * LIFT_SHIFT, py - lift * LIFT_SHIFT, &v, &b)) sound_play_at(resourceId, v, b);
}

/* Heard from here at all (within 24 tiles)? */
int world_actor_heard(int actor)
{
    const Actor *a = actor_of(actor);
    double v, b;
    return a && (actor == 0 || world_hearing_at(a->px - a->lift * LIFT_SHIFT, a->py - a->lift * LIFT_SHIFT, &v, &b));
}

static void frame_sound(int key, const Sprite *s, uint32_t ms, double cx, double cy)
{
    if (!s || s->frameCount <= 1) return;
    int f = sprite_frame_at(s, ms), slot = -1;
    for (int i = 0; i < seenCount; i++) if (seen[i].key == key) { slot = i; break; }
    if (slot < 0) { if (seenCount >= 1024) return; slot = seenCount++; seen[slot] = (FrameSeen){ key, s, -1 }; }
    if (seen[slot].sprite == s && seen[slot].frame == f) return;
    int changed = seen[slot].sprite == s;
    seen[slot].sprite = s; seen[slot].frame = f;
    double v, b;
    if (changed && s->frames[f].sound >= 0 && world_hearing_at(cx, cy, &v, &b)) sound_play_at(s->frames[f].sound, v, b); /* from where it's drawn */
}
static int seated_draw_order(int count);
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
    d->actor = -1; d->band = floorBand; d->order = *count;
    return d;
}

/* Exult's roofs: something solid over the player's head - everything from its height up isn't drawn. */
static int ceilingIsFloor; /* the lowest ceiling over the player is the floor above (its slab), not a roof */

static int ceiling_over_player(void)
{
    int l, t, ceiling = 1 << 30;
    player_foot(player.px, player.py, &l, &t);
    for (int i = 0; i < bodyCount; i++)
        if (overlaps(&bodies[i], l, t, player.sizeX, player.sizeY) && bodies[i].z >= player.lift + player.sizeZ && bodies[i].z < ceiling) ceiling = bodies[i].z;
    /* IsFloorSlabAt: a painted upper floor's slab, or a Floor tile up there (one lift thick, just under its floor's height) */
    ceilingIsFloor = 0;
    for (int i = 0; i < bodyCount && ceiling < (1 << 30); i++)
        if (bodies[i].z == ceiling && overlaps(&bodies[i], l, t, player.sizeX, player.sizeY)
            && ((i >= slabStart && i < slabEnd) || (bodies[i].height == 1 && (bodies[i].z + 1) % LIFTS_PER_FLOOR == 0 && bodies[i].z + 1 >= base_lift(2)))) ceilingIsFloor = 1;
    return ceiling;
}

/* skip_above (UpdateRoofs): under a roof, what is at its lift or above isn't drawn - at the roof's own lift only solid things (and Floor tiles) go.
   Under the floor above, its slab's own lift is still this floor (a book on a tall table): only from that floor's height up goes. */
static int hidden_above(int z, int solid, int ceiling)
{
    if (ceiling >= (1 << 30)) return 0;
    if (ceilingIsFloor) return z >= ceiling + 1;
    return z > ceiling || (z == ceiling && solid);
}

static void draw_world_once(uint32_t *pixels, uint32_t ms);

/* The world - and on a map that wraps around, the land beyond an edge drawn in the gap, one map-width (or height) along. */
void world_draw(uint32_t *pixels, uint32_t ms)
{
    shade_update();
    double w = mapW * tileW, h = mapH * tileH, cx = floor(cameraX), cy = floor(cameraY);
    int ax0 = 0, ax1 = 0, ay0 = 0, ay1 = 0; double shiftX = 0, shiftY = 0;
    if (wraps && w > SCREEN_W) { if (cx < 0) { ax1 = (int)-cx; shiftX = w; } else if (cx + SCREEN_W > w) { ax0 = (int)(w - cx); ax1 = SCREEN_W; shiftX = -w; } }
    if (wraps && h > SCREEN_H) { if (cy < 0) { ay1 = (int)-cy; shiftY = h; } else if (cy + SCREEN_H > h) { ay0 = (int)(h - cy); ay1 = SCREEN_H; shiftY = -h; } }
    if (ax1 <= ax0 && ay1 <= ay0) { draw_world_once(pixels, ms); return; }
    static uint32_t *across; /* the gaps, drawn first (the world's own drawing last: what's clicked and outlined is its) */
    if (!across) across = (uint32_t *)malloc(sizeof(uint32_t) * FRAME_PIXELS);
    static uint32_t *scratch;
    if (!scratch) scratch = (uint32_t *)malloc(sizeof(uint32_t) * FRAME_PIXELS);
    if (!across || !scratch) { draw_world_once(pixels, ms); return; }
    double keepX = cameraX, keepY = cameraY;
    struct { int x0, x1, y0, y1; double sx, sy; } gaps[3] = {
        { ax0, ax1, 0, SCREEN_H, shiftX, 0 },          /* across */
        { 0, SCREEN_W, ay0, ay1, 0, shiftY },          /* up or down */
        { ax0, ax1, ay0, ay1, shiftX, shiftY } };      /* the corner */
    for (int g = 0; g < 3; g++)
    {
        if (gaps[g].x1 <= gaps[g].x0 || gaps[g].y1 <= gaps[g].y0) continue;
        cameraX = keepX + gaps[g].sx; cameraY = keepY + gaps[g].sy;
        draw_world_once(scratch, ms);
        for (int y = gaps[g].y0 * frame_scale; y < gaps[g].y1 * frame_scale; y++) memcpy(across + y * FRAME_W + gaps[g].x0 * frame_scale, scratch + y * FRAME_W + gaps[g].x0 * frame_scale, sizeof(uint32_t) * (size_t)((gaps[g].x1 - gaps[g].x0) * frame_scale));
    }
    cameraX = keepX; cameraY = keepY;
    draw_world_once(pixels, ms);
    for (int g = 0; g < 3; g++)
        for (int y = gaps[g].y0 * frame_scale; y < gaps[g].y1 * frame_scale; y++)
            if (gaps[g].x1 > gaps[g].x0) memcpy(pixels + y * FRAME_W + gaps[g].x0 * frame_scale, across + y * FRAME_W + gaps[g].x0 * frame_scale, sizeof(uint32_t) * (size_t)((gaps[g].x1 - gaps[g].x0) * frame_scale));
    screen = pixels;
}

/* A picture with x and y swapped (Exult's reflected frames), kept once made. */
static const Picture *swapped_picture(const Picture *p)
{
    static struct { const Picture *from; Picture *to; } made[64];
    static int madeCount;
    for (int i = 0; i < madeCount; i++) if (made[i].from == p) return made[i].to;
    if (madeCount >= 64) return p;
    Picture *q = (Picture *)calloc(1, sizeof(Picture));
    q->width = p->height; q->height = p->width; q->opaque = p->opaque;
    q->pixels = (uint32_t *)malloc((size_t)q->width * (size_t)q->height * 4);
    for (int y = 0; y < p->height; y++)
        for (int x = 0; x < p->width; x++) q->pixels[(size_t)x * (size_t)q->width + y] = p->pixels[(size_t)y * (size_t)p->width + x];
    made[madeCount].from = p; made[madeCount].to = q; madeCount++;
    return q;
}

/* Exult's Actor::paint_weapon (GameTestWindow.WeaponInHand): the frame a character shows gives the hand and which weapon frame; the weapon frame is drawn with its grip on the hand, over the body or behind it. */
static void draw_weapon_in_hand(const Actor *a, const Sprite *body, uint32_t at, const Picture *bodyPic, int x, int y, int w, int h, int band, double depth, int *count)
{
    if (a->characterId < 0 || vitals_dead(a->characterId)) return;
    cJSON *weapon = items_weapon(a->characterId, NULL), *link = get(get(weapon, "Weapon"), "InHandSprite");
    const Sprite *held = weapon && link ? link_of(link) : NULL;
    if (!held || !held->frameCount || !body || !body->frameCount) return;
    int index = sprite_frame_at(body, at);
    if (index < 0 || index >= body->frameCount) return;
    const Frame *frame = &body->frames[index];
    if (frame->handX < 0 || frame->handY < 0) return; /* no weapon in this frame */
    cJSON *settings = get(get(get(gameJson, "Project"), "Settings"), "WeaponInHand"), *wset = get(weapon, "Weapon");
    const char *pose = a->shownPose >= 0 ? a->poses[a->shownPose].name : "";
    int attacking = pose[0] && (!strcmp(pose, str(wset, "AttackPose")) || !strcmp(pose, str(wset, "ShootPose")) || !strcmp(pose, "Attacking Melee") || !strcmp(pose, "Shooting") || !strcmp(pose, "Attack"));
    static const int WIN_DIR[8] = { 0, 1, 2, 3, 4, 5, 6, 7 }; /* SpriteDirection: north, clockwise */
    int weaponFrame, mirrored = 0;
    if (!flag(settings, "Smooth"))
    {
        mirrored = a->facing == DIR_E || a->facing == DIR_W;
        if (frame->weaponFrame >= 0) weaponFrame = frame->weaponFrame;
        else if (!attacking) weaponFrame = 0;
        else { int phase = index * 3 / (body->frameCount > 0 ? body->frameCount : 1); weaponFrame = 3 - (phase < 2 ? phase : 2); }
    }
    else
    {
        int swing = num(settings, "SwingFrames", 3);
        if (swing < 1) swing = 1; else if (swing > 8) swing = 8;
        int first = WIN_DIR[a->facing & 7] * (1 + swing);
        if (frame->weaponFrame >= 0) weaponFrame = frame->weaponFrame;
        else if (!attacking) weaponFrame = first;
        else { int k = index * swing / (body->frameCount > 0 ? body->frameCount : 1); weaponFrame = first + 1 + (k < swing - 1 ? k : swing - 1); }
    }
    if (weaponFrame < 0) weaponFrame = 0;
    if (weaponFrame >= held->frameCount) weaponFrame = held->frameCount - 1;
    const Frame *hf = &held->frames[weaponFrame];
    const Picture *wp = frame_picture(hf);
    if (!wp) return;
    double scale = fmin((double)w / bodyPic->width, (double)h / bodyPic->height);
    double handX = x + (w - bodyPic->width * scale) / 2 + frame->handX * scale, handY = y + (h - bodyPic->height * scale) / 2 + frame->handY * scale;
    double gripX = (hf->gripX >= 0 ? hf->gripX : wp->width / 2.0) * scale, gripY = (hf->gripY >= 0 ? hf->gripY : wp->height / 2.0) * scale;
    const Picture *shown = mirrored ? swapped_picture(wp) : wp;
    if (mirrored) { double t = gripX; gripX = gripY; gripY = t; }
    Drawn *d = add_drawn(band, depth + (frame->weaponBehind ? -0.25 : 0.25), shown, (int)floor(handX - gripX + 0.5), (int)floor(handY - gripY + 0.5), (int)(shown->width * scale + 0.5), (int)(shown->height * scale + 0.5), count);
    if (d) d->order = 1 << 30;
}

/* Doors moving (GameTestWindow.Doors): their look takes its time; whether they're open changes at once, as Exult's. */
typedef struct DoorMove { int uid, opening, backwards; const Sprite *sprite; double durationMs, elapsedMs; } DoorMove;
static DoorMove doorMoves[16];
static int doorMoveCount;

static const DoorMove *door_move_of(int uid)
{
    for (int i = 0; i < doorMoveCount; i++) if (doorMoves[i].uid == uid) return &doorMoves[i];
    return NULL;
}

static void door_move_start(const Thing *t, int opening)
{
    const ObjectDef *d = t->def;
    for (int i = 0; i < doorMoveCount; i++) if (doorMoves[i].uid == t->uid) doorMoves[i--] = doorMoves[--doorMoveCount];
    if (d->doorOpening == 0 || doorMoveCount >= 16) return;
    const Sprite *sprite = NULL;
    int backwards = 0;
    if (d->doorOpening == 1)
    {
        if (!opening && d->closingSprite && d->closingSprite->frameCount) sprite = d->closingSprite;
        else if (d->openingSprite && d->openingSprite->frameCount) { sprite = d->openingSprite; backwards = !opening; }
    }
    else if (d->own && d->own->frameCount) sprite = d->own;
    if (!sprite) return;
    double ms = 0;
    if (d->doorOpening == 1) for (int f = 0; f < sprite->frameCount; f++) ms += sprite->frames[f].ms;
    else ms = d->motionMs > 50 ? d->motionMs : 50;
    doorMoves[doorMoveCount++] = (DoorMove){ t->uid, opening, backwards, sprite, ms > 1 ? ms : 1, 0 };
}

static double selfOpeningCheckMs;
static struct { int uid; double awayMs; } doorsAlone[64];
static int doorsAloneCount;

/* Each frame: moving doors a step on; those that open by themselves, four times a second. */
static void doors_update(double ms)
{
    for (int i = 0; i < doorMoveCount; i++) if ((doorMoves[i].elapsedMs += ms) >= doorMoves[i].durationMs) doorMoves[i--] = doorMoves[--doorMoveCount];
    if ((selfOpeningCheckMs += ms) < 250) return;
    double step = selfOpeningCheckMs;
    selfOpeningCheckMs = 0;
    for (int i = 0; i < thingCount; i++)
    {
        Thing *t = &things[i];
        if (t->gone || !t->def || t->def->type != T_DOOR || !t->def->opensByItself) continue;
        int near = 0, floor = floor_of(t->z), reach = t->def->opensWithin > 0 ? t->def->opensWithin : 0;
        for (int k = 0; k <= npcCount && !near; k++)
        {
            int ax, ay, af;
            if (world_actor_gone(k) || vitals_dead(world_actor_character(k))) continue;
            world_actor_cell(k, &ax, &ay, &af);
            near = af == floor && abs(ax - t->x) <= reach && abs(ay - t->y) <= reach;
        }
        int slot = -1;
        for (int k = 0; k < doorsAloneCount; k++) if (doorsAlone[k].uid == t->uid) slot = k;
        if (near)
        {
            if (slot >= 0) doorsAlone[slot] = doorsAlone[--doorsAloneCount];
            if (!t->open && !t->locked) { door_set_open(t, 1); world_sound_at_thing(t->def->doorOpenSound, t->uid); }
            continue;
        }
        if (!t->open) continue;
        if (slot < 0) { if (doorsAloneCount >= 64) continue; slot = doorsAloneCount++; doorsAlone[slot].uid = t->uid; doorsAlone[slot].awayMs = 0; }
        if ((doorsAlone[slot].awayMs += step) >= t->def->staysOpenMs && world_door_close(t->uid))
        {
            world_sound_at_thing(t->def->doorCloseSound, t->uid);
            doorsAlone[slot] = doorsAlone[--doorsAloneCount];
        }
    }
}

static void draw_world_once(uint32_t *pixels, uint32_t ms)
{
    screen = pixels;
    uint32_t black = world_shade_colour(0xFF000000u);
    for (int i = 0; i < FRAME_PIXELS; i++) pixels[i] = black;
    blitShading = 1;
    int cx = (int)floor(cameraX), cy = (int)floor(cameraY);
    int ceiling = ceiling_over_player();
    int draggedUid = gumps_dragged_map_thing();

    /* The ground. */
    int x0 = cx / tileW - 1, y0 = cy / tileH - 1, x1 = (cx + SCREEN_W) / tileW + 1, y1 = (cy + SCREEN_H) / tileH + 1;
    for (int y = y0 < 0 ? 0 : y0; y <= y1 && y < mapH; y++)
        for (int x = x0 < 0 ? 0 : x0; x <= x1 && x < mapW; x++)
            if (floors[1])
            {
                if (underTile[y * mapW + x] >= 0) blit_box(tile_picture(underTile[y * mapW + x], ms), x * tileW - cx, y * tileH - cy, tileW, tileH); /* its tile under it */
                blit_box(tile_picture(floors[1][y * mapW + x], ms), x * tileW - cx, y * tileH - cy, tileW, tileH);
            }

    int count = 0;
    /* Upper floors' tiles, at their floor's height. */
    for (int f = 2; f <= MAX_FLOORS; f++)
    {
        if (!floors[f] || (ceiling < (1 << 30) && base_lift(f) >= ceiling + ceilingIsFloor)) continue; /* the floors overhead go */
        int lift = base_lift(f);
        for (int y = y0 < 0 ? 0 : y0; y <= y1 + 4 && y < mapH; y++)
            for (int x = x0 < 0 ? 0 : x0; x <= x1 + 4 && x < mapW; x++)
            {
                int tile = floors[f][y * mapW + x];
                if (tile < 0) continue;
                Drawn *d = add_drawn(f - 1, -5000000.0 + (x + y) * 100.0, tile_picture(tile, ms), x * tileW - lift * LIFT_SHIFT - cx, y * tileH - lift * LIFT_SHIFT - cy, tileW, tileH, &count); /* a floor: before everything standing on it */
                if (d) { d->slab = 1; d->sl = x * unitsX; d->sr = (x + 1) * unitsX - 1; d->sf = y * unitsY; d->sn = (y + 1) * unitsY - 1; }
            }
    }
    /* Things: bottom-centred on their cell, in a box rounded up to 8 pixels, up and left by their lift. */
    for (int i = 0; i < thingCount; i++)
    {
        const Thing *t = &things[i];
        if (t->gone || hidden_above(t->z, t->solid || (t->def && t->def->type == T_TILE && t->def->floorTile), ceiling)) continue;
        if (t->uid == draggedUid) continue; /* being dragged: only the one under the pointer is seen */
        if (t->def && t->def->type == T_TRAP && t->def->trapHidden && !t->sprung) continue; /* a hidden trap: unseen till it goes off */
        if (t->steps)
        {
            cJSON *step;
            int index = 0;
            cJSON_ArrayForEach(step, t->steps)
            {
                const Sprite *ss = link_of(get(step, "Sprite"));
                int frame = num(step, "Frame", 0), landing = flag(step, "IsLanding");
                const Picture *sp = landing || frame > 0 ? (ss && ss->frameCount ? frame_picture(&ss->frames[frame < ss->frameCount ? frame : ss->frameCount - 1]) : NULL) : sprite_picture_at(ss, ms);
                index++;
                if (!sp) continue;
                int sx = t->x * tileW + t->offX * UNIT + num(step, "PictureX", 0) - t->z * LIFT_SHIFT, sy = t->y * tileH + t->offY * UNIT + num(step, "PictureY", 0) - t->z * LIFT_SHIFT;
                int al = t->x * unitsX + t->offX + num(step, "FootX", 0), at = t->y * unitsY + t->offY + num(step, "FootY", 0);
                int sw = num(step, "SizeX", 1) > 1 ? num(step, "SizeX", 1) : 1, sd = num(step, "SizeY", 1) > 1 ? num(step, "SizeY", 1) : 1, topLift = num(step, "TopLift", 1);
                if (landing)
                {
                    int slab = t->z + (topLift - 1 > 0 ? topLift - 1 : 0);
                    if (ceiling < (1 << 30) && slab + 1 >= ceiling + ceilingIsFloor) continue;
                    Drawn *ld = add_drawn(floor_of(t->z + topLift) - 1, -5000000.0 + (t->x + t->y) * 100.0 + index * 0.01, sp, sx - cx, sy - cy, sp->width, sp->height, &count); /* with that floor's tiles */
                    if (ld) { ld->slab = 1; ld->sl = al; ld->sr = al + sw - 1; ld->sf = at; ld->sn = at + sd - 1; }
                    continue;
                }
                Drawn *d = add_drawn(floor_of(t->z) - 1, (t->x + t->y + 1) * 100.0 + t->z + index * 0.01, sp, sx - cx, sy - cy, sp->width, sp->height, &count);
                if (!d) continue;
                set_box(d, al, at, sw, sd, t->z, topLift > 0 ? topLift : 1);
                d->thing = t->uid; d->order = t->seq ? t->seq : t->uid;
            }
            continue;
        }
        const Sprite *ls = look_of(t);
        const Picture *pic = t->fixedFrame && ls && t->fixedFrame - 1 < ls->frameCount ? frame_picture(&ls->frames[t->fixedFrame - 1]) : sprite_picture_at(ls, ms);
        /* How a door looks as it opens and closes (DoorMotion): an animation's frame, the closed door sliding or fading */
        const DoorMove *doorMove = t->def && t->def->type == T_DOOR ? door_move_of(t->uid) : NULL;
        double doorOpenAmount = t->def && t->def->type == T_DOOR && t->open ? 1 : 0;
        const Picture *underFade = NULL;
        if (doorMove)
        {
            double along = doorMove->durationMs > 0 ? doorMove->elapsedMs / doorMove->durationMs : 1;
            along = along > 1 ? 1 : along;
            double eased = along < 0.5 ? 2 * along * along : 1 - pow(-2 * along + 2, 2) / 2;
            doorOpenAmount = doorMove->opening ? eased : 1 - eased;
            if (t->def->doorOpening == 1)
            {
                int index = doorMove->sprite->frameCount - 1;
                double sum = 0;
                for (int f = 0; f < doorMove->sprite->frameCount; f++) { sum += doorMove->sprite->frames[f].ms; if (doorMove->elapsedMs < sum) { index = f; break; } }
                pic = frame_picture(&doorMove->sprite->frames[doorMove->backwards ? doorMove->sprite->frameCount - 1 - index : index]);
            }
            else
            {
                if (t->def->doorOpening == 3 && doorMove->opening && t->def->open) underFade = sprite_picture_at(t->def->open, ms); /* opening onto its open sprite, which shows under it */
                pic = doorMove->sprite->frameCount ? frame_picture(&doorMove->sprite->frames[0]) : pic;
            }
        }
        if (t->def && t->def->type == T_DOOR && t->open && !doorMove && t->def->doorOpening == 3 && !t->def->open) continue; /* faded away, with nothing left */
        if (!pic) { if (!t->fixedFrame) frame_sound(t->uid, ls, ms, t->x * tileW + tileW / 2.0, t->y * tileH + tileH / 2.0); continue; }
        int qw = (pic->width + UNIT - 1) / UNIT * UNIT, qh = (pic->height + UNIT - 1) / UNIT * UNIT;
        int left = t->x * tileW + (tileW - qw) / 2 + t->offX * UNIT - t->z * LIFT_SHIFT;
        int top = (t->y + 1) * tileH - qh + t->offY * UNIT - t->z * LIFT_SHIFT;
        if (!t->fixedFrame) frame_sound(t->uid, ls, ms, left + qw / 2.0, top + qh / 2.0); /* its frame sounds from its picture's middle */
        int doorClipOn = 0, doorClipL = 0, doorClipT = 0, doorOpacity = 0;
        if (t->def && t->def->type == T_DOOR)
        {
            int kind = t->def->doorOpening;
            if (t->open && !doorMove && kind < 2) { left += t->def->openOffX; top += t->def->openOffY; } /* ShiftOpenDoor: hanging from its hinge */
            if (kind == 2 && doorOpenAmount > 0)
            {
                /* slid by that much, still hidden past where the closed door stands when it slides into the wall */
                doorClipOn = t->def->slideHidden; doorClipL = left - cx; doorClipT = top - cy;
                left += (int)floor(t->def->slideX * doorOpenAmount + 0.5); top += (int)floor(t->def->slideY * doorOpenAmount + 0.5);
            }
            if (kind == 3 && doorMove)
            {
                doorOpacity = (int)((1 - doorOpenAmount) * 255);
                if (doorOpacity < 1) doorOpacity = 1;
                if (underFade) { Drawn *under = add_drawn(floor_of(t->z) - 1, (t->x + t->y + 1) * 100.0 + t->z - 0.01, underFade, left - cx, top - cy, qw, qh, &count); if (under) { under->thing = t->uid; under->wall = 1; } }
            }
        }
        int flat = t->def && (t->def->type == T_TRAP || (t->def->type == T_TILE && !t->def->solid));
        Drawn *d = add_drawn(floor_of(t->z) - 1, (t->x + t->y + 1) * 100.0 + t->z - (flat ? 1000 : 0), pic, left - cx, top - cy, qw, qh, &count);
        if (!d) continue;
        if (doorClipOn) { d->clip = 1; d->cl = doorClipL; d->ct = doorClipT; d->cr = doorClipL + qw; d->cb = doorClipT + qh; }
        if (doorOpacity) d->opacity = doorOpacity;
        Body b;
        thing_box(t, &b);
        set_box(d, b.left, b.top, b.width, b.depth, b.z, flat ? 0 : b.height);
        d->wall = t->def && (t->def->type == T_WALL || t->def->type == T_DOOR || t->def->type == 12 /* Window */);
        d->occludes = t->def && t->def->occludes;
        d->hangs = t->hangs || t->staysPut; /* staying where it's put: drawn as a hung thing */
        d->thing = t->uid; d->order = t->seq ? t->seq : t->uid;
        if (t->def && t->def->type == T_TILE && (t->def->solid || t->def->floorTile) && t->z >= base_lift(2) && t->z == base_lift(floor_of(t->z)))
        { d->slab = 1; d->sl = b.left; d->sr = b.left + b.width - 1; d->sf = b.top; d->sn = b.top + b.depth - 1; } /* a tile object that is part of an upper floor */
    }
    /* Blood: puddles on the ground, under everything standing there. */
    for (int i = 0; i < puddleCount; i++)
    {
        Puddle *pd = &puddles[i];
        const Picture *pic = pd->blood && pd->frame < pd->blood->frameCount ? frame_picture(&pd->blood->frames[pd->frame]) : NULL;
        if (!pic || hidden_above(pd->lift, 0, ceiling)) continue;
        double px = pd->ux * 8 + 4.0, py = pd->uy * 8 + 4.0;
        Drawn *d = add_drawn(floor_of(pd->lift) - 1, (px / tileW + py / tileH) * 100.0 + pd->lift - 50, pic, (int)(px - pic->width / 2.0) - pd->lift * LIFT_SHIFT - cx, (int)(py - pic->height / 2.0) - pd->lift * LIFT_SHIFT - cy, pic->width, pic->height, &count);
        if (d) d->mirror = pd->mirrored;
    }
    /* Characters and the player. */
    for (int i = -1; i < npcCount; i++)
    {
        const Actor *a = i < 0 ? &player : &npcs[i];
        if (i >= 0 && (a->gone || (ceiling < (1 << 30) && a->lift >= ceiling + ceilingIsFloor))) continue;
        int invisible = vitals_has(a->characterId, 32), shimmer = invisible && (i < 0 || items_in_party(a->characterId));
        if (invisible && !shimmer) continue; /* ShowInvisibility: unseen */
        const Sprite *s = a->walkingNow && a->walking[a->facing] ? a->walking[a->facing] : a->idle[a->facing];
        uint32_t at = a->walkingNow ? ms : 0;
        if (a->shownPose >= 0 && a->poses[a->shownPose].dirs[a->facing]) { s = a->poses[a->shownPose].dirs[a->facing]; at = ms - a->poseSince; }
        const Picture *pic = sprite_picture_at(s, at);
        if (a->joinedSprite && a->joinedFrame < a->joinedSprite->frameCount) pic = frame_picture(&a->joinedSprite->frames[a->joinedFrame]);
        if (!pic) continue;
        int qw = (pic->width + UNIT - 1) / UNIT * UNIT, qh = (pic->height + UNIT - 1) / UNIT * UNIT;
        int left = (int)floor(a->px - qw / 2.0) - a->lift * LIFT_SHIFT, top = (int)floor(a->py + tileH / 2.0 - qh) - a->lift * LIFT_SHIFT;
        if (i < 0) frame_sound(-(i + 2), s, at, cameraX + SCREEN_W / 2.0, cameraY + SCREEN_H / 2.0); /* the main character's own: as it is */
        else frame_sound(-(i + 2), s, at, left + qw / 2.0, top + qh / 2.0);
        Drawn *d = add_drawn(floor_of(a->lift) - 1, (a->px / tileW + a->py / tileH) * 100.0 + a->lift, pic, left - cx, top - cy, qw, qh, &count);
        if (!d) continue;
        if (shimmer) d->opacity = 102; /* 40% */
        double feetY = a->py + tileH / 2.0;
        set_box(d, (int)floor((a->px - a->sizeX * UNIT / 2.0) / UNIT + 0.5), (int)floor((feetY - a->sizeY * UNIT) / UNIT + 0.5), a->sizeX, a->sizeY, a->lift, a->sizeZ);
        d->actor = i + 1; d->order = 1 << 30;
        if (!a->joinedSprite) draw_weapon_in_hand(a, s, at, pic, left - cx, top - cy, qw, qh, floor_of(a->lift) - 1, d->key - (floor_of(a->lift) - 1) * 10000000.0, &count);
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
    /* UpperFloorOrder: an upper floor's tiles are a thin slab - something on a lower floor wholly in front of them (south or east, not under)
       is nearer the viewer and drawn over them (objs.cc compare); what is under or behind them stays covered. */
    for (int i = 0; i < count; i++)
    {
        Drawn *d = &drawn[i];
        if (!d->hasBox || d->slab) continue;
        int liftTo = -1;
        for (int j = 0; j < count; j++)
        {
            const Drawn *s = &drawn[j];
            if (!s->slab || s->band <= d->band || d->x >= s->x + s->w || s->x >= d->x + d->w || d->y >= s->y + s->h || s->y >= d->y + d->h) continue;
            int south = d->yf > s->sn, east = d->xl > s->sr, north = d->yn < s->sf, west = d->xr < s->sl;
            if ((south || east) && !north && !west) { if (s->band > liftTo) liftTo = s->band; }
            else if (!(south && west) && !(north && east)) { liftTo = -2; break; } /* under it or behind it: the floor above covers it */
        }
        if (liftTo >= 0) d->key = liftTo * 10000000.0 - 2500000.0 + (d->key - d->band * 10000000.0) * 0.1; /* over that floor's slabs, under all that stands on it */
    }
    count = seated_draw_order(count);
    qsort(drawn, (size_t)count, sizeof(Drawn), by_key);
    for (int i = 0; i < count; i++)
    {
        blitOpacity = drawn[i].opacity ? drawn[i].opacity : 255; blitMirror = drawn[i].mirror;
        blitClip = drawn[i].clip; blitClipL = drawn[i].cl; blitClipT = drawn[i].ct; blitClipR = drawn[i].cr; blitClipB = drawn[i].cb;
        blit_box(drawn[i].picture, drawn[i].x, drawn[i].y, drawn[i].w, drawn[i].h);
        blitClip = 0;
    }
    blitOpacity = 255; blitMirror = 0;
    blitShading = 0;
    draw_status_outlines(pixels);
    drawnCount = count;
    lastMs = ms;
}
void world_camera_origin(double *x, double *y) { *x = floor(cameraX); *y = floor(cameraY); }
void world_draw_stats(int *thingsOnMap, int *drawnLast) { *thingsOnMap = thingCount; *drawnLast = drawnCount; }

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
    if (actor == 0) world_update(0, 0, 0); /* the camera follows by its dead zone (GameTestWindow.UpdateCamera), not centred */
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

/* ---------------- Land, sea and air (GameTestWindow.Movement) ---------------- */

static Actor *actor_of(int i);
void world_walk_cancel(int actor);

/* The free bands of lifts over a footprint: [low, high) with nothing solid in them, ground to sky. */
static int free_bands(int l, int t, int w, int d, int *lows, int *highs, int max)
{
    static const Body *solids[2048];
    int ns = 0, n = 0, cursor = 0;
    for (int i = 0; i < bodyCount && ns < 2048; i++) if (overlaps(&bodies[i], l, t, w, d) && bodies[i].height > 0) solids[ns++] = &bodies[i];
    for (int i = 1; i < ns; i++) for (int k = i; k > 0 && solids[k]->z < solids[k - 1]->z; k--) { const Body *x = solids[k]; solids[k] = solids[k - 1]; solids[k - 1] = x; }
    for (int i = 0; i < ns; i++)
    {
        if (solids[i]->z > cursor && n < max) { lows[n] = cursor; highs[n] = solids[i]->z; n++; }
        if (solids[i]->z + solids[i]->height > cursor) cursor = solids[i]->z + solids[i]->height;
    }
    if (cursor < SKY_LIFT && n < max) { lows[n] = cursor; highs[n] = SKY_LIFT; n++; }
    return n;
}

/* A swimmer's step: every unit under it Water (Tile Properties), room for its height - it never leaves the water. */
static int sea_step(const Actor *a, int l, int t, int lift)
{
    if (l < 0 || t < 0 || l + a->sizeX > mapW * unitsX || t + a->sizeY > mapH * unitsY || !water) return -1;
    for (int y = t; y < t + a->sizeY; y++)
        for (int x = l; x < l + a->sizeX; x++) if (!water[(y / unitsY) * mapW + x / unitsX]) return -1;
    static const Body *solids[1024];
    int ns = 0;
    for (int i = 0; i < bodyCount && ns < 1024; i++) if (overlaps(&bodies[i], l, t, a->sizeX, a->sizeY)) solids[ns++] = &bodies[i];
    return is_clear(solids, ns, lift, a->sizeZ) ? lift : -1;
}

/* A flier's step: from its band over the old footprint to a band over the new one where they overlap by its height,
   entering as high as the overlap lets it (floor 10 under the open sky, or down at a floor it's headed for). */
static int air_step(const Actor *a, int fl, int ft, int l, int t, int lift)
{
    if (l < 0 || t < 0 || l + a->sizeX > mapW * unitsX || t + a->sizeY > mapH * unitsY) return -1;
    int lows[64], highs[64], n = free_bands(fl, ft, a->sizeX, a->sizeY, lows, highs, 64), low = -1, high = -1;
    for (int i = 0; i < n; i++) if (lift >= lows[i] && lift < highs[i]) { low = lows[i]; high = highs[i]; }
    if (low < 0) return -1;
    int nlows[64], nhighs[64], nn = free_bands(l, t, a->sizeX, a->sizeY, nlows, nhighs, 64), best = -1;
    for (int i = 0; i < nn; i++)
    {
        int lo = low > nlows[i] ? low : nlows[i], hi = high < nhighs[i] ? high : nhighs[i];
        if (hi - lo < a->sizeZ) continue; /* no room to get across */
        int at = a->flyPrefer < lo ? lo : a->flyPrefer > hi - a->sizeZ ? hi - a->sizeZ : a->flyPrefer;
        if (best < 0 || abs(at - a->flyPrefer) < abs(best - a->flyPrefer)) best = at;
    }
    return best;
}

/* TryFlyStep: room for its whole height at this lift on the new footprint (nothing under it needed) - the lift, or -1. */
static int fly_room(int l, int t, int sx, int sy, int lift, int h)
{
    if (l < 0 || t < 0 || l + sx > mapW * unitsX || t + sy > mapH * unitsY) return -1;
    int lows[64], highs[64], n = free_bands(l, t, sx, sy, lows, highs, 64);
    for (int i = 0; i < n; i++) if (lift >= lows[i] && lift + h <= highs[i]) return lift;
    return -1;
}

/* Levitate: up by 3 floors from where they stand - under a ceiling only till the head touches it - walking in the air for a while. */
void world_levitate(int actor, double seconds)
{
    Actor *a = actor_of(actor);
    if (!a || a->gone || seconds <= 0) return;
    int l, t;
    foot_at(a, a->px, a->py, &l, &t);
    int own = actor > 0 ? lift_own_body(a) : -1;
    int lows[64], highs[64], n = free_bands(l, t, a->sizeX, a->sizeY, lows, highs, 64), top = a->lift;
    if (actor > 0) put_own_body(a, own);
    for (int i = 0; i < n; i++) if (a->lift >= lows[i] && a->lift < highs[i]) top = highs[i] - a->sizeZ > a->lift ? highs[i] - a->sizeZ : a->lift;
    int to = a->lift + 3 * LIFTS_PER_FLOOR < top ? a->lift + 3 * LIFTS_PER_FLOOR : top;
    if (a->levitateMs <= 0) a->groundMovement = a->movement;
    a->levitateMs = seconds * 1000;
    a->levitateLift = to;
    a->lift = to;
    if (actor > 0) { world_walk_cancel(actor); a->movement = 2; a->flyPrefer = to; if (a->body >= 0) bodies[a->body].z = to; }
    else camera_update();
}

/* Each frame: the time runs out - down they fall onto whatever is under them. */
void world_levitation_update(double ms)
{
    for (int i = 0; i <= npcCount; i++)
    {
        Actor *a = actor_of(i);
        if (!a || a->levitateMs <= 0 || (a->levitateMs -= ms) > 0) continue;
        a->levitateMs = 0;
        int l, t;
        foot_at(a, a->px, a->py, &l, &t);
        static const Body *under[1024];
        int n = 0;
        for (int k = 0; k < bodyCount && n < 1024; k++) if (k != a->body && overlaps(&bodies[k], l, t, a->sizeX, a->sizeY)) under[n++] = &bodies[k];
        int ground = floor_below(under, n, a->lift);
        if (i > 0) { world_walk_cancel(i); a->movement = a->groundMovement; }
        if (ground < a->lift) a->lift = ground;
        if (a->body >= 0) bodies[a->body].z = a->lift;
        if (i == 0) camera_update();
    }
}

/* Exult's MOVE_ETHEREAL: walls and everything else are nothing to it, only the map's edge. */
static int ghost_step(const Actor *a, int l, int t, int lift)
{
    if (wraps) return lift;
    return l >= 0 && t >= 0 && l + a->sizeX <= mapW * unitsX && t + a->sizeY <= mapH * unitsY ? lift : -1;
}

/* A character's step onto a new footprint by how it moves: the lift it'd be at, or -1. */
static int mover_step(const Actor *a, int fl, int ft, int l, int t, int lift)
{
    if (a->movement == 1) return sea_step(a, l, t, lift);
    if (a->movement == 2) return air_step(a, fl, ft, l, t, lift);
    if (a->movement == 4) return ghost_step(a, l, t, lift);
    return try_step(l, t, a->sizeX, a->sizeY, lift, a->sizeZ);
}

/* A flier that can't fly (knocked out, asleep, paralyzed, killed) falls onto whatever is under it. */
static void fall_if_grounded(int actor)
{
    Actor *a = actor_of(actor);
    if (!a || a->gone || a->movement != 2 || a->lift <= 0) return;
    int hp = 1;
    vitals_hp(a->characterId, &hp);
    if (!vitals_dead(a->characterId) && hp > 0 && !vitals_has(a->characterId, 2 | 4)) return;
    int l, t;
    foot_at(a, a->px, a->py, &l, &t);
    static const Body *under[1024];
    int n = 0;
    for (int i = 0; i < bodyCount && n < 1024; i++) if (i != a->body && overlaps(&bodies[i], l, t, a->sizeX, a->sizeY)) under[n++] = &bodies[i];
    int ground = floor_below(under, n, a->lift);
    if (ground >= a->lift) return;
    world_walk_cancel(actor);
    a->lift = ground;
    if (a->body >= 0) bodies[a->body].z = ground;
}

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
            int nl = mover_step(a, ll, lt, l, t, *lift);
            if (nl < 0) return 0;
            *lift = nl; ll = l; lt = t;
        }
    return 1;
}

static int door_by_hand_early(const ObjectDef *d) { return d->doorOpening != 2 && !d->notByHand; }

static int can_stand_at(const Actor *a, int cx, int cy, int fromLift, int *lift)
{
    int l, t;
    foot_at(a, cell_cx(cx), cell_cy(cy), &l, &t);
    if (a->movement == 2)
    {
        /* a flier: any free band with room over the cell - the one nearest the height it's at */
        int lows[64], highs[64], n = free_bands(l, t, a->sizeX, a->sizeY, lows, highs, 64), best = -1;
        for (int i = 0; i < n; i++)
            if (highs[i] - lows[i] >= a->sizeZ)
            {
                int at = fromLift < lows[i] ? lows[i] : fromLift > highs[i] - a->sizeZ ? highs[i] - a->sizeZ : fromLift;
                if (best < 0 || abs(at - fromLift) < abs(best - fromLift)) best = at;
            }
        *lift = best;
        return best >= 0;
    }
    *lift = a->movement == 1 ? sea_step(a, l, t, fromLift) : a->movement == 4 ? ghost_step(a, l, t, fromLift) : try_step(l, t, a->sizeX, a->sizeY, fromLift, a->sizeZ);
    return *lift >= 0;
}

typedef struct Node { int x, y, f, lift, cost, from, open; double px, py; } Node; /* px, py: where in its cell the walker stands */
static int find_path_core(Actor *a, int gx, int gy, int goalFloor, int allowPartial, int besideOnly, int *pathX, int *pathY, signed char *offX, signed char *offY, int max);

static int heuristic(int ax, int ay, int bx, int by) { return abs(world_wrap_cells_x(ax - bx)) + abs(world_wrap_cells_y(ay - by)); }

static int door_sound(const Thing *d, const char *key)
{
    cJSON *o;
    cJSON_ArrayForEach(o, get(get(get(gameJson, "Project"), "Objects"), "Objects")) if (num(o, "Id", -2) == d->itemId) return num(get(o, "Door"), key, -1);
    return -1;
}
static int door_unlock_sound(const Thing *d) { int s = door_sound(d, "UnlockSoundResourceId"); return s >= 0 ? s : door_sound(d, "LockSoundResourceId"); }
static int door_lock_sound(const Thing *d) { int s = door_sound(d, "LockSoundResourceId"); return s >= 0 ? s : door_sound(d, "UnlockSoundResourceId"); }
static int actor_under_roof(const Actor *a);

static int (*holdsKey)(int actor, int doorUid); /* LocksHouseDoor: the key to its own door, carried (schedules.c) */
static int index_of_actor(const Actor *a);

static int (*breaksDoor)(int actor, int doorUid, int locked); /* WouldBreakDown (combat.c) */
static void (*bashDoor)(int actor, int doorUid);               /* a blow at it (combat.c) */

static void doors_passable(const Actor *a, int passable)
{
    for (int i = 0; i < thingCount; i++)
    {
        Thing *t = &things[i];
        if (t->gone || !t->def || t->def->type != T_DOOR || t->open || t->body < 0 || !door_by_hand_early(t->def)) continue;
        int breaks = a != &player && breaksDoor && breaksDoor(index_of_actor(a), t->uid, t->locked); /* it breaks it down on the way */
        if (!breaks && !a->human) continue; /* only the sentient open doors */
        if (!breaks && t->locked && !(holdsKey && a != &player && holdsKey(index_of_actor(a), t->uid))) continue;
        if (passable) bodies[t->body].z -= 1000000; else bodies[t->body].z += 1000000;
    }
}

/* FindPath: from where the walker is to the goal cell (on a floor when asked: 0 = whatever floor), or beside it when it's taken.
   Fills path (cells) and returns its length; -1 when there's no way (allowPartial: the reachable cell nearest the goal instead). */
static int find_path(Actor *a, int gx, int gy, int goalFloor, int allowPartial, int besideOnly, int *pathX, int *pathY, signed char *offX, signed char *offY, int max)
{
    doors_passable(a, 1); /* WorldBodiesForPathing: closed unlocked doors will be opened on the way */
    int length = find_path_core(a, gx, gy, goalFloor, allowPartial, besideOnly, pathX, pathY, offX, offY, max);
    doors_passable(a, 0);
    return length;
}

/* SpotsInCell: offsets from a cell's middle where a walker may stand in it - the middle, then each 8px step off it within the
   cell, nearest first. Walls built in 8px steps can leave the way between two cells off their middles (a doorway between
   wall pieces): then the nearest spot that can be stood on and walked to is used (FindPathAStar's pointAt). */
static int spotCount; static signed char spotX[64], spotY[64];
static void make_spots(void)
{
    spotCount = 0;
    int rx = tileW / UNIT / 2, ry = tileH / UNIT / 2;
    for (int j = -ry; j <= ry; j++)
        for (int i = -rx; i <= rx; i++)
        {
            int x = i * UNIT, y = j * UNIT;
            if (abs(x) * 2 >= tileW || abs(y) * 2 >= tileH || spotCount >= 64) continue;
            /* sorted as it goes: by |x| + |y|, then by |y| */
            int k = spotCount++;
            while (k > 0 && (abs(spotX[k - 1]) + abs(spotY[k - 1]) > abs(x) + abs(y) || (abs(spotX[k - 1]) + abs(spotY[k - 1]) == abs(x) + abs(y) && abs(spotY[k - 1]) > abs(y))))
            { spotX[k] = spotX[k - 1]; spotY[k] = spotY[k - 1]; k--; }
            spotX[k] = (signed char)x; spotY[k] = (signed char)y;
        }
}

static int find_path_core(Actor *a, int gx, int gy, int goalFloor, int allowPartial, int besideOnly, int *pathX, int *pathY, signed char *offX, signed char *offY, int max)
{
    if (!spotCount) make_spots();
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
    nodes[count] = (Node){ sx, sy, startF, a->lift, 0, -1, 1, a->px, a->py };
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
            double toX = cell_cx(cur.x) + DX[d] * tileW, toY = cell_cy(cur.y) + DY[d] * tileH; /* the next cell's middle, over the seam if it's there */
            if (wraps) { nx = (nx + mapW) % mapW; ny = (ny + mapH) % mapH; }
            if (nx < 0 || ny < 0 || nx >= mapW || ny >= mapH) continue;
            if (!goalFloor && index[KEY(nx, ny, 0)] >= 0 && nodes[index[KEY(nx, ny, 0)]].cost <= cur.cost + 1) continue;
            double fx = cur.px, fy = cur.py; /* from its spot in this cell */
            int spotOx = 0, spotOy = 0;
            if (a->movement)
            {
                int fl, ft, l, t;
                foot_at(a, fx, fy, &fl, &ft);
                foot_at(a, cell_cx(nx), cell_cy(ny), &l, &t);
                if ((lift = mover_step(a, fl, ft, l, t, cur.lift)) < 0) continue;
            }
            else
            {
                /* to its spot in the next: the next cell's middle first, else its other 8px spots, nearest first */
                int found1 = 0;
                for (int sp = 0; sp < spotCount && !found1; sp++)
                {
                    double tx = toX + spotX[sp], ty = toY + spotY[sp];
                    if (!goalFloor)
                    {
                        int l, t;
                        foot_at(a, tx, ty, &l, &t);
                        if ((lift = try_step(l, t, a->sizeX, a->sizeY, cur.lift, a->sizeZ)) < 0) continue;
                    }
                    if (!can_sweep(a, fx, fy, tx, ty, cur.lift, goalFloor ? &lift : &(int){ 0 })) continue;
                    spotOx = spotX[sp]; spotOy = spotY[sp]; found1 = 1;
                }
                if (!found1) continue;
            }
            int nf = goalFloor ? floor_of(lift) : 0, k = KEY(nx, ny, nf), cost = cur.cost + 1;
            if (index[k] >= 0 && nodes[index[k]].cost <= cost) continue;
            if (index[k] < 0)
            {
                if (count == cap) { cap *= 2; nodes = (Node *)realloc(nodes, sizeof(Node) * (size_t)cap); }
                index[k] = count++;
            }
            nodes[index[k]] = (Node){ nx, ny, nf, lift, cost, best, 1, cell_cx(nx) + spotOx, cell_cy(ny) + spotOy };
        }
    }
    int end = found >= 0 ? found : allowPartial && nearest != 0 ? nearest : -1, length = -1;
    if (end >= 0)
    {
        length = 0;
        for (int n = end; n > 0 && nodes[n].from >= 0; n = nodes[n].from) length++;
        if (length > max) length = max;
        int i = length;
        for (int n = end; n > 0 && nodes[n].from >= 0 && i > 0; n = nodes[n].from)
        {
            i--; pathX[i] = nodes[n].x; pathY[i] = nodes[n].y;
            if (offX) { offX[i] = (signed char)(nodes[n].px - cell_cx(nodes[n].x)); offY[i] = (signed char)(nodes[n].py - cell_cy(nodes[n].y)); }
        }
    }
#undef KEY
#undef H
    free(index); free(nodes);
    put_own_body(a, own);
    return length;
}

#define MAX_PATH 512
typedef struct Walk { int active, count, at; int x[MAX_PATH], y[MAX_PATH]; signed char ox[MAX_PATH], oy[MAX_PATH]; int doorToClose, blocked, lockBehind; } Walk; /* ox, oy: its spot off the cell's middle */
static Walk *walks; /* one per actor */

static Walk *walk_of(int actor)
{
    if (!walks) walks = (Walk *)calloc((size_t)(npcCount + 1), sizeof(Walk));
    return actor >= 0 && actor <= npcCount ? &walks[actor] : NULL;
}

/* Starts a walk there (the player: first to the middle of the cell they stand in); 0 when there's no way just now. */
int world_actor_stationary(int actor) { Actor *a = actor_of(actor); return a && a->movement == 3; }

int world_walk_to(int actor, int x, int y, int goalFloor, int beside)
{
    Actor *a = actor_of(actor);
    Walk *w = walk_of(actor);
    if (!a || !w) return 0;
    if (a->movement == 3) return 0; /* Stationary: it never moves (Character Editor > Moves on) */
    a->flyPrefer = goalFloor ? base_lift(goalFloor) : a->levitateMs > 0 ? a->levitateLift : FLY_LIFT; /* levitating: at its own height */
    int n = find_path(a, x, y, goalFloor, actor != 0, beside, w->x + 1, w->y + 1, w->ox + 1, w->oy + 1, MAX_PATH - 1);
    if (n < 0) return 0;
    w->x[0] = (int)floor(a->px / tileW); w->y[0] = (int)floor(a->py / tileH);
    w->ox[0] = (signed char)(a->px - cell_cx(w->x[0])); w->oy[0] = (signed char)(a->py - cell_cy(w->y[0])); /* from where it stands */
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
    static int movingPartners;
    t->open = open;
    t->solid = t->def->solid && !open;
    door_move_start(t, open); /* its look takes its time */
    if (t->body >= 0)
    {
        if (open) { bodies[t->body].width = bodies[t->body].depth = 0; bodies[t->body].z = -100000; }
        else thing_box(t, &bodies[t->body]);
    }
    if (!t->def->withNeighbours || movingPartners) return;
    /* a double door: the double doors right next to it (on its floor) follow */
    movingPartners = 1;
    for (int i = 0; i < thingCount; i++)
    {
        Thing *o = &things[i];
        if (o == t || o->gone || !o->def || o->def->type != T_DOOR || !o->def->withNeighbours || floor_of(o->z) != floor_of(t->z) || abs(o->x - t->x) > 1 || abs(o->y - t->y) > 1) continue;
        if (open && !o->open && !o->locked) door_set_open(o, 1);
        else if (!open && o->open) world_door_close(o->uid);
    }
    movingPartners = 0;
}

/* Opened by hand (a double-click, someone walking through)? Never a sliding door, nor one set Not by hand (a laser door's button). */
static int door_by_hand(const ObjectDef *d) { return d->doorOpening != 2 && !d->notByHand; }

int world_door_by_hand(int uid)
{
    Thing *t = thing_by_uid(uid);
    return !t || !t->def || t->def->type != T_DOOR || door_by_hand(t->def);
}

/* NpcOpensDoor: a human walking into a closed, unlocked door opens it (and will close it behind). */
static int open_door_in_way(Actor *a, Walk *w, int l, int t)
{
    if (!a->human || a == &player) return 0;
    for (int i = 0; i < thingCount; i++)
    {
        Thing *d = &things[i];
        if (d->gone || !d->def || d->def->type != T_DOOR || d->open || d->body < 0 || !door_by_hand(d->def)) continue;
        if (!overlaps(&bodies[d->body], l, t, a->sizeX, a->sizeY)) continue;
        if (!d->locked && breaksDoor && breaksDoor(index_of_actor(a), d->uid, 0)) return 0; /* it'd rather break it down */
        if (d->locked)
        {
            /* its own door, key in hand: unlocked to get through (and locked again behind it once outside) */
            if (!holdsKey || !holdsKey(index_of_actor(a), d->uid)) continue;
            d->locked = 0;
            world_sound_at_thing(door_unlock_sound(d), d->uid);
            w->lockBehind = 1;
        }
        door_set_open(d, 1);
        w->doorToClose = d->uid;
        return 1;
    }
    return 0;
}

/* NpcBashesDoor: a walker blocked by a door it would break down hits it - a blow every two standard delays - until it breaks. */
static int bash_door_in_way(int actor, Actor *a, int l, int t, double seconds)
{
    if (a == &player || !breaksDoor || !bashDoor) return 0;
    for (int i = 0; i < thingCount; i++)
    {
        Thing *d = &things[i];
        if (d->gone || !d->def || d->def->type != T_DOOR || d->open || d->body < 0 || !door_by_hand(d->def) || !overlaps(&bodies[d->body], l, t, a->sizeX, a->sizeY)) continue;
        if (!breaksDoor(actor, d->uid, d->locked)) return 0;
        a->walkingNow = 0;
        if ((a->bashMs -= seconds * 1000) > 0) return 1;
        a->bashMs = 400;
        bashDoor(actor, d->uid);
        return 1;
    }
    return 0;
}

void world_set_door_breaker(int (*breaks)(int actor, int doorUid, int locked), void (*bash)(int actor, int doorUid)) { breaksDoor = breaks; bashDoor = bash; }

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
        /* LockBehindIfLeaving: its own door, and it's now outside (nothing over its head) */
        if (holdsKey && holdsKey(i, d->uid) && !actor_under_roof(a)) { d->locked = 1; world_sound_at_thing(door_lock_sound(d), d->uid); }
        w->lockBehind = 0;
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
        double gx = cell_cx(w->x[w->at]) + w->ox[w->at], gy = cell_cy(w->y[w->at]) + w->oy[w->at]; /* its spot in that cell */
        double rx = world_wrap_dx(gx - a->px), ry = world_wrap_dy(gy - a->py);
        if (fabs(rx) < 0.01 && fabs(ry) < 0.01) { a->px = gx; a->py = gy; w->at++; continue; }
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
            int lift = mover_step(a, l0, t0, l1, t1, a->lift);
            put_own_body(a, own);
            if (a->movement == 2 && lift >= 0 && lift != a->lift)
            {
                /* ClimbToward: up or down a lift every 25 ms, inside its own cell, then the step */
                a->climbMs += seconds * 1000;
                while (a->climbMs >= 25 && a->lift != lift) { a->climbMs -= 25; a->lift += lift > a->lift ? 1 : -1; }
                if (a->body >= 0) bodies[a->body].z = a->lift;
                a->walkingNow = 1;
                if (a->lift != lift) return;
                a->climbMs = 0;
            }
            if (lift < 0 && open_door_in_way(a, w, l1, t1)) return; /* a closed, unlocked door: opened, on next frame */
            if (lift < 0 && bash_door_in_way(actor, a, l1, t1, seconds)) return; /* a door of a side it's hostile to: broken down */
            if (lift < 0 && ask_aside(actor, l1, t1, alongX ? (remaining > 0 ? 1 : -1) : 0, alongX ? 0 : (remaining > 0 ? 1 : -1))) return; /* someone in the way moved aside (or swapped): on next frame */
            if (lift < 0) { w->active = 0; w->blocked = 1; a->walkingNow = 0; return; } /* something in the way: stop, think again */
            a->lift = lift;
            if (a->body >= 0) { bodies[a->body].left = l1; bodies[a->body].top = t1; bodies[a->body].z = lift; }
        }
        a->px = nx; a->py = ny;
        wrap_actor(a, actor == 0);
        a->walkingNow = 1;
        budget -= move;
    }
}

/* Each frame: everyone walking somewhere takes their step (the player's own keys cancel theirs). */
void world_walks_update(double seconds)
{
    for (int i = 0; i <= npcCount; i++) if (!magic_frozen(i)) step_walk(i, seconds); /* Time Stop: everyone but the party stands still */
    for (int i = 1; i <= npcCount; i++) fall_if_grounded(i);
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

/* Picked up: off the map (what rested on it comes down). */
void world_thing_remove(int uid)
{
    Thing *t = thing_by_uid(uid);
    if (!t) return;
    t->gone = 1;
    Body b;
    thing_box(t, &b);
    if (t->body >= 0) for (int k = 0; k < (t->bodyN > 0 ? t->bodyN : 1); k++) { bodies[t->body + k].width = bodies[t->body + k].depth = 0; bodies[t->body + k].z = -100000; }
    (void)b;
    settle_all(); /* whatever rested on it falls */
}

/* Put down at a cell (PutDownAt): resting on whatever is there, on that floor. Returns its uid (-1 if it can't be shown). */
int world_thing_put(int category, int itemId, int quantity, int x, int y, int onFloor)
{
    if (thingCount >= thingCap) { thingCap *= 2; things = (Thing *)realloc(things, sizeof(Thing) * (size_t)thingCap); }
    if (bodyCount + 1 >= bodyCap) { bodyCap *= 2; bodies = (Body *)realloc(bodies, sizeof(Body) * (size_t)bodyCap); }
    Thing *t = &things[thingCount];
    memset(t, 0, sizeof *t);
    t->uid = nextUid++; t->category = category; t->itemId = itemId; t->quantity = quantity > 0 ? quantity : 1;
    t->x = x; t->y = y; t->facing = DIR_S; t->burnLeft = -1;
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
        t->liquid = d->type == T_LIQUID ? d->liquidStart : -1;
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
    t->homeFloor = onFloor;
    if (category == 4) t->elevates = t->def->elevates;
    else { cJSON *data = NULL, *c; cJSON *source = category == 5 ? containersJson : booksJson; cJSON_ArrayForEach(c, source) if (num(c, "Id", -2) == itemId) data = c; t->elevates = flag(data, "AllowElevation"); }
    thingCount++;
    add_thing_body(t);
    settle_all();
    return t->uid;
}

void world_thing_spot(int uid, int *x, int *y, int *offX, int *offY, int *z)
{
    const Thing *t = thing_by_uid(uid);
    *x = t ? t->x : -1; *y = t ? t->y : -1; *offX = t ? t->offX : 0; *offY = t ? t->offY : 0; *z = t ? t->z : 0;
}

int world_thing_copy_at(int fromUid, int quantity, int x, int y, int offX, int offY, int z)
{
    int i = 0;
    while (i < thingCount && things[i].uid != fromUid) i++; /* gone too: it may have been picked up */
    if (i >= thingCount) return -1;
    int category = things[i].category, itemId = things[i].itemId;
    int uid = world_thing_put(category, itemId, quantity, x, y, floor_of(z < 0 ? 0 : z));
    if (uid < 0) return -1;
    Thing *from = &things[i], *t = thing_by_uid(uid); /* after the put: the array may have moved */
    t->offX = offX; t->offY = offY; t->z = z; t->homeFloor = floor_of(z < 0 ? 0 : z);
    t->facing = from->facing; t->lit = from->lit; t->spent = from->spent; t->burnLeft = from->burnLeft; t->liquid = from->liquid;
    t->hangs = from->hangs; t->staysPut = from->staysPut; t->liesUnder = from->liesUnder;
    if (t->body >= 0) thing_box(t, &bodies[t->body]);
    settle_all();
    return uid;
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
    int slot = -1;
    for (int i = 0; i < 16 && slot < 0; i++) if (barks[i].until > lastMs && barks[i].actor == actor) slot = i; /* Exult's add_text: their earlier words go */
    for (int i = 0; i < 16 && slot < 0; i++) if (barks[i].until <= lastMs) slot = i;
    if (slot < 0) slot = 0;
    barks[slot].actor = actor;
    snprintf(barks[slot].text, sizeof barks[slot].text, "%s", text);
    barks[slot].until = lastMs + (uint32_t)(ms > 0 ? ms : 1000);
}

static void draw_thing_barks(uint32_t *pixels);

/* Drawn over everything: centred over the head, 2 pixels above the picture (GameTextLabel in the InGameFont). */
static void draw_effects(uint32_t *pixels);

static void draw_hit_outline(uint32_t *pixels);

void world_draw_barks(uint32_t *pixels)
{
    draw_hit_outline(pixels);
    draw_effects(pixels);
    draw_thing_barks(pixels);
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
    /* every lit light a party member holds in a hand */
    for (int a = 0; a <= npcCount; a++)
    {
        Actor *who = actor_of(a);
        if (!items_in_party(who->characterId)) continue;
        int dx = (int)fabs(floor((who->px - player.px) / UNIT + 0.5)), dy = (int)fabs(floor((who->py - player.py) / UNIT + 0.5));
        int strength = 75 - 2 * dx - 3 * dy;
        if (strength <= 0) continue;
        int brightness[2], lightGives[2], n = items_hand_lights(who->characterId, brightness, lightGives);
        for (int k = 0; k < n; k++)
        {
            total += strength * brightness[k];
            if (strength * brightness[k] > 0 && lightGives[k] > *gives) *gives = lightGives[k];
        }
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

/* ---------------- Chairs and beds (GameTestWindow.Furniture, from Exult's Sit / Sleep schedules) ---------------- */

typedef struct Seat { int uid, spot, bed; double fromX, fromY; int fromLift; } Seat;
static Seat *seats;       /* per actor: uid 0 = not seated */
static Seat *heading;     /* per actor: the chair or bed it's walking to */

static void seats_ready(void)
{
    if (!seats) { seats = (Seat *)calloc((size_t)npcCount + 2, sizeof(Seat)); heading = (Seat *)calloc((size_t)npcCount + 2, sizeof(Seat)); }
}

/* ResolveDirection: the facing whose look and spots are used - its own, else South, else any set. */
static int furniture_facing(const ObjectDef *d, int facing)
{
    if (d->facing[facing]) return facing;
    if (d->facing[DIR_S]) return DIR_S;
    for (int i = 0; i < 8; i++) if (d->facing[i]) return i;
    return -1;
}

static int spot_count(const Thing *t)
{
    int f = furniture_facing(t->def, t->facing);
    if (f < 0) return 0;
    return t->def->spotCount[f] > 0 ? t->def->spotCount[f] : 1; /* none set: one in the middle of its picture */
}

/* Where someone in this spot has their feet (world pixels, without the lift shift), and the lift. */
static int spot_position(const Thing *t, int spot, double *x, double *y, int *lift)
{
    const Picture *pic = sprite_picture_at(look_of(t), 0);
    if (!pic || spot < 0 || spot >= spot_count(t)) return 0;
    int f = furniture_facing(t->def, t->facing);
    double sx = pic->width / 2.0, sy = pic->height;
    if (t->def->spotCount[f] > 0) { sx = t->def->spotX[f][spot]; sy = t->def->spotY[f][spot]; }
    int qw = (pic->width + UNIT - 1) / UNIT * UNIT, qh = (pic->height + UNIT - 1) / UNIT * UNIT;
    double left = t->x * tileW + (tileW - qw) / 2 + t->offX * UNIT, top = (t->y + 1) * tileH - qh + t->offY * UNIT;
    double scale = fmin((double)qw / pic->width, (double)qh / pic->height);
    double insetX = (qw - pic->width * scale) / 2, insetY = (qh - pic->height * scale) / 2;
    *x = left + insetX + sx * scale; *y = top + insetY + sy * scale; *lift = t->z;
    return 1;
}

static int spot_taken(int uid, int spot, int except)
{
    for (int a = 0; a <= npcCount; a++)
        if (a != except && ((seats[a].uid == uid && seats[a].spot == spot) || (heading[a].uid == uid && heading[a].spot == spot))) return 1;
    return 0;
}

static int nearest_free_spot(const Thing *t, double x, double y, int who)
{
    int best = -1;
    double bestDistance = 1e30;
    for (int i = 0; i < spot_count(t); i++)
    {
        double px, py; int lift;
        if (spot_taken(t->uid, i, who) || !spot_position(t, i, &px, &py, &lift)) continue;
        double d = fabs(px - x) + fabs(py - y);
        if (d < bestDistance) { bestDistance = d; best = i; }
    }
    return best;
}

static Thing *cover_of(const Thing *bed)
{
    if (!bed->def || bed->def->coverId < 0) return NULL;
    for (int i = 0; i < thingCount; i++)
    {
        Thing *c = &things[i];
        if (!c->gone && c->category == 4 && c->itemId == bed->def->coverId && c->x == bed->x && c->y == bed->y) return c;
    }
    return NULL;
}

/* Exult Sleep_schedule: the cover turned down over a sleeper (its second frame), or the bed made again (its first). */
static void set_cover(const Thing *bed, int turnedDown) { Thing *c = cover_of(bed); if (c) c->fixedFrame = turnedDown ? 2 : 1; }

static int someone_on(int uid) { for (int a = 0; a <= npcCount; a++) if (seats[a].uid == uid) return 1; return 0; }

/* Sits or lies them down on the spot. */
static void take_seat(int actor, Thing *t, int spot)
{
    Actor *a = actor_of(actor);
    double x, y; int lift;
    if (!a || !spot_position(t, spot, &x, &y, &lift)) return;
    int bed = t->def->type == T_BED || t->def->type == T_BEDROLL;
    seats[actor] = (Seat){ t->uid, spot, bed, a->px, a->py, a->lift };
    a->px = x; a->py = y - tileH / 2.0; a->lift = lift;
    if (a->body >= 0) bodies[a->body].z = -100000; /* in the chair: not in anyone's way as a standing body */
    int f = furniture_facing(t->def, t->facing);
    world_set_pose(actor, bed ? "Sleeping" : "Sit", f >= 0 ? f : t->facing);
    if (bed) set_cover(t, 1);
    world_furniture_event(t->uid, 9, actor); /* By Enter on the chair or bed */
    if (actor == 0 && bed) mechanisms_lay_down(t->uid); /* the slider: how many hours (Exult's rest) */
}

/* Gets up: back where they stood, standing; a bed is made again. */
void world_stand_up(int actor)
{
    seats_ready();
    heading[actor].uid = 0;
    Seat s = seats[actor];
    if (!s.uid) return;
    seats[actor].uid = 0;
    Thing *t = thing_by_uid(s.uid);
    if (t && s.bed && !someone_on(s.uid)) set_cover(t, 0);
    Actor *a = actor_of(actor);
    a->px = s.fromX; a->py = s.fromY; a->lift = s.fromLift;
    if (a->body >= 0)
    {
        int l, tp;
        foot_at(a, a->px, a->py, &l, &tp);
        bodies[a->body].left = l; bodies[a->body].top = tp; bodies[a->body].z = a->lift;
    }
    world_set_pose(actor, "Idle", -1);
    if (t) world_furniture_event(t->uid, 11, actor); /* By Exit */
}

int world_seated(int actor) { seats_ready(); return seats[actor].uid != 0; }
int world_seated_on(int actor) { seats_ready(); return seats[actor].uid; }
int world_in_bed(int actor) { seats_ready(); return seats[actor].uid != 0 && seats[actor].bed; }

/* Sit_schedule / Sleep_schedule: the nearest free chair (any distance) or bed (within 24 tiles) it can walk to - heading there. 0 if none. */
static int go_to_furniture(int actor, int bed, int onlyUid, int maxTiles);
int world_go_to_furniture(int actor, int bed, int onlyUid) { return go_to_furniture(actor, bed, onlyUid, 0); }
/* A party member sitting with the main character: only chairs within so many tiles (Exult find_closest: 24). */
int world_go_to_chair_within(int actor, int tiles) { return go_to_furniture(actor, 0, 0, tiles); }

static int go_to_furniture(int actor, int bed, int onlyUid, int maxTiles)
{
    seats_ready();
    Actor *a = actor_of(actor);
    if (!a || !a->human) return 0; /* a rat doesn't take a chair or a bed */
    int cx = (int)floor(a->px / tileW), cy = (int)floor(a->py / tileH);
    int range = maxTiles > 0 ? (int)floor(maxTiles * 8.0 / tileW + 0.5) : bed ? (int)floor(24 * 8.0 / tileW + 0.5) : 1 << 30;
    /* the candidates, nearest first */
    static int uids[2048], dists[2048];
    int n = 0;
    for (int i = 0; i < thingCount && n < 2048; i++)
    {
        Thing *t = &things[i];
        if (t->gone || !t->def || t->def->type != (bed ? T_BED : T_CHAIR) || (onlyUid && t->uid != onlyUid)) continue;
        int ax = abs(t->x - cx), ay = abs(t->y - cy);
        if ((ax > ay ? ax : ay) > range) continue;
        int d = ax + ay, k = n++;
        while (k > 0 && dists[k - 1] > d) { uids[k] = uids[k - 1]; dists[k] = dists[k - 1]; k--; }
        uids[k] = t->uid; dists[k] = d;
    }
    for (int i = 0; i < n; i++)
    {
        Thing *t = thing_by_uid(uids[i]);
        int spot = nearest_free_spot(t, a->px, a->py, actor);
        double px, py; int lift;
        if (spot < 0 || !spot_position(t, spot, &px, &py, &lift)) continue;
        int gx = (int)floor(px / tileW), gy = (int)floor((py - 1) / tileH);
        heading[actor] = (Seat){ t->uid, spot, bed, 0, 0, 0 };
        if (abs(cx - gx) + abs(cy - gy) <= 1) { world_arrive_furniture(actor); return 1; }
        if (world_walk_to(actor, gx, gy, 0, 1)) return 1;
        heading[actor].uid = 0; /* not reachable: the next one */
    }
    return 0;
}

/* Reached the chair or bed it was heading for: sits or lies down (if the spot is still free). 0 when it wasn't heading for one. */
int world_arrive_furniture(int actor)
{
    seats_ready();
    Seat h = heading[actor];
    if (!h.uid) return 0;
    heading[actor].uid = 0;
    Thing *t = thing_by_uid(h.uid);
    double x, y; int lift;
    if (!t || spot_taken(h.uid, h.spot, actor) || !spot_position(t, h.spot, &x, &y, &lift)) return -1; /* taken meanwhile: look again shortly */
    take_seat(actor, t, h.spot);
    return 1;
}

/* ---- The main character: double-clicking a chair or bed - walk up, look at it, turn its way, sit (Exult's sit_down) ---- */

typedef struct PendingSit { int uid, phase; double waitMs, targetX, targetY; } PendingSit;
static PendingSit pendingSit; /* phase 0 = none, 4 = walking there, 3 = last steps, 1 = looking, 2 = turned */

int vitals_bark(int characterId, int kind);

/* Told it can't be done: the character's own line (Character Editor > Verbose). */
static void cant_reach_seat(void)
{
    game_log("You can't get to it from here.");
    vitals_bark(world_actor_character(0), 16 /* CantSit */);
}

/* The way from this point up to the seat crosses nothing solid but the seat's own furniture: no sitting down through a table. */
static int nothing_between(double fromX, double fromY, int uid)
{
    double dx = pendingSit.targetX - fromX, dy = pendingSit.targetY - fromY, most = fmax(fabs(dx), fabs(dy));
    int steps = (int)ceil(most / 4.0), mine[1] = { uid }, me[1] = { 0 };
    for (int i = 0; i <= steps; i++)
    {
        double t = steps ? i / (double)steps : 1;
        int l, tp;
        player_foot(fromX + dx * t, fromY + dy * t, &l, &tp);
        if (world_blocked(l, tp, player.sizeX, player.sizeY, player.lift + 1, player.lift + player.sizeZ, mine, 1, me, 1)) return 0;
    }
    return 1;
}

int world_player_sit(int uid)
{
    seats_ready();
    Thing *t = thing_by_uid(uid);
    if (!t || !t->def || (t->def->type != T_CHAIR && t->def->type != T_BED && t->def->type != T_BEDROLL)) return 0;
    if (seats[0].uid) world_stand_up(0);
    int spot = nearest_free_spot(t, player.px, player.py + tileH / 2.0, 0);
    double x, y; int lift;
    if (spot < 0 || !spot_position(t, spot, &x, &y, &lift)) return 0;
    int gx = (int)floor(x / tileW), gy = (int)floor((y - 1) / tileH), px = (int)floor(player.px / tileW), py = (int)floor(player.py / tileH);
    pendingSit = (PendingSit){ uid, 4, 0, x, y - tileH / 2.0 };
    if (abs(px - gx) + abs(py - gy) <= 1 && nothing_between(player.px, player.py, uid)) { pendingSit.phase = 3; return 1; }
    /* The cell round the seat nearest the player that goes straight on to the seat: walked round whatever is in the way (a table). */
    int cellX[8], cellY[8], cellD[8], cells = 0;
    for (int dy = -1; dy <= 1; dy++)
        for (int dx = -1; dx <= 1; dx++)
        {
            if (!dx && !dy) continue;
            int cx = gx + dx, cy = gy + dy;
            if (cx < 0 || cy < 0 || cx >= mapW || cy >= mapH || !world_can_stand(0, cx, cy, floor_of(player.lift)) || !nothing_between(cell_cx(cx), cell_cy(cy), uid)) continue;
            int d = abs(cx - px) + abs(cy - py), at = cells++;
            while (at > 0 && cellD[at - 1] > d) { cellX[at] = cellX[at - 1]; cellY[at] = cellY[at - 1]; cellD[at] = cellD[at - 1]; at--; }
            cellX[at] = cx; cellY[at] = cy; cellD[at] = d;
        }
    for (int i = 0; i < cells; i++) if (world_walk_to(0, cellX[i], cellY[i], 0, 0)) return 1;
    pendingSit.phase = 0;
    cant_reach_seat();
    return 0;
}

int world_player_sitting_down(void) { return pendingSit.phase != 0; }

static void player_sit_update(double seconds)
{
    if (!pendingSit.phase) return;
    Thing *t = thing_by_uid(pendingSit.uid);
    if (!t) { pendingSit.phase = 0; return; }
    if (pendingSit.phase == 4) { if (!world_walking(0)) pendingSit.phase = 3; return; }
    if (pendingSit.phase == 3)
    {
        /* the last steps: toward the seat, one axis at a time; there (or stopped by the chair) - look at it */
        double rx = pendingSit.targetX - player.px, ry = pendingSit.targetY - player.py;
        int alongX = fabs(rx) > 0.5;
        if (!alongX && fabs(ry) <= 0.5) goto look;
        double remaining = fabs(alongX ? rx : ry), step = fmin(TILES_PER_SECOND * tileW * seconds, remaining);
        if (step <= 0) return;
        double nx = player.px + (alongX ? (rx > 0 ? step : -step) : 0), ny = player.py + (alongX ? 0 : (ry > 0 ? step : -step));
        int l0, t0, l1, t1;
        player_foot(player.px, player.py, &l0, &t0);
        player_foot(nx, ny, &l1, &t1);
        int lift = l1 == l0 && t1 == t0 ? player.lift : try_step(l1, t1, player.sizeX, player.sizeY, player.lift, player.sizeZ);
        if (lift >= 0) { player.px = nx; player.py = ny; player.lift = lift; player.facing = alongX ? (rx > 0 ? DIR_E : DIR_W) : (ry > 0 ? DIR_S : DIR_N); player.walkingNow = 1; return; }
        if (alongX && fabs(ry) > 0.5) { pendingSit.targetX = player.px; return; }
    look:
        player.walkingNow = 0;
        {
            const Picture *pic = sprite_picture_at(look_of(t), 0);
            double cx = t->x * tileW + tileW / 2.0, cy = (t->y + 1) * tileH;
            (void)pic;
            double dx = cx - player.px, dy = cy - (player.py + tileH / 2.0);
            player.facing = fabs(dx) > fabs(dy) ? (dx > 0 ? DIR_E : DIR_W) : (dy > 0 ? DIR_S : DIR_N);
        }
        player.shownPose = -1;
        pendingSit.phase = 1; pendingSit.waitMs = 1000;
        return;
    }
    if ((pendingSit.waitMs -= seconds * 1000) > 0) return;
    if (pendingSit.phase == 1)
    {
        int f = furniture_facing(t->def, t->facing);
        player.facing = f >= 0 ? f : t->facing; /* turn the way the chair faces */
        pendingSit.phase = 2; pendingSit.waitMs = 300;
        return;
    }
    pendingSit.phase = 0;
    /* not actually beside it (the way was blocked): no jumping onto it from across a table */
    if (fabs(player.px - pendingSit.targetX) > tileW * 1.5 || fabs(player.py - pendingSit.targetY) > tileH * 1.5 || !nothing_between(player.px, player.py, pendingSit.uid)) { cant_reach_seat(); return; }
    int spot = nearest_free_spot(t, player.px, player.py + tileH / 2.0, 0);
    if (spot < 0) return;
    take_seat(0, t, spot);
}

/* At the start of play: a bed with a cover gets it laid on it (its own object). */
static void spawn_bed_covers(void)
{
    int count = thingCount;
    for (int i = 0; i < count; i++)
    {
        Thing *bed = &things[i];
        if (bed->gone || !bed->def || bed->def->type != T_BED || bed->def->coverId < 0 || !def_of(bed->def->coverId) || cover_of(bed)) continue;
        int bx = bed->x, by = bed->y, bz = bed->z, ox = bed->offX, oy = bed->offY, facing = bed->facing;
        int uid = world_thing_put(4, things[i].def->coverId, 1, bx, by, floor_of(bz));
        Thing *c = thing_by_uid(uid);
        if (c) { c->z = bz; c->offX = ox; c->offY = oy; c->facing = facing; c->fixedFrame = 1; c->spawned = 1; }
    }
}

void world_cancel_player_sit(void) { pendingSit.phase = 0; }

/* Someone sitting or lying is drawn over their chair or bed, and a bed's cover over its sleeper (or over the empty bed). */
/* The chair (or bed) under its sitter whichever way it faces; a chair's back (if it has one) laid over the chair, over them. Returns the new count. */
static int seated_draw_order(int count)
{
    seats_ready();
    int total = count;
    for (int i = 0; i < count; i++)
    {
        Drawn *d = &drawn[i];
        if (d->actor < 0 || d->actor > npcCount || !seats[d->actor].uid) continue;
        Thing *f = thing_by_uid(seats[d->actor].uid);
        int chair = -1;
        for (int j = 0; j < count; j++)
            if (drawn[j].thing == seats[d->actor].uid)
            {
                if (drawn[j].key + 0.5 > d->key) d->key = drawn[j].key + 0.5;
                chair = j;
            }
        int fi = f && f->def && !seats[d->actor].bed ? furniture_facing(f->def, f->facing) : -1;
        const Picture *backPic = fi >= 0 && f->def->back[fi] ? sprite_picture_at(f->def->back[fi], lastMs) : NULL;
        if (chair >= 0 && backPic)
        {
            Drawn copy = drawn[chair];
            double key = drawn[i].key + 0.25;
            Drawn *b = add_drawn(copy.band, 0, backPic, copy.x, copy.y, copy.w, copy.h, &total); /* may move the array: d is not used after this */
            if (b) { int order = b->order; *b = copy; b->picture = backPic; b->key = key; b->order = order; }
            d = &drawn[i];
        }
        Thing *c = f && seats[d->actor].bed ? cover_of(f) : NULL;
        if (c) for (int j = 0; j < count; j++) if (drawn[j].thing == c->uid) drawn[j].key = d->key + 0.25;
    }
    for (int i = 0; i < count; i++)
    {
        Thing *bed = drawn[i].thing ? thing_by_uid(drawn[i].thing) : NULL;
        if (!bed || !bed->def || bed->def->type != T_BED || someone_on(bed->uid)) continue;
        Thing *c = cover_of(bed);
        if (c) for (int j = 0; j < count; j++) if (drawn[j].thing == c->uid && drawn[j].key < drawn[i].key + 0.5) drawn[j].key = drawn[i].key + 0.5;
    }
    return total;
}

/* What a click lands on: the topmost character (actor) or thing (uid) whose picture is under the point. */
void world_hit(int sx, int sy, int *actor, int *uid)
{
    *actor = -1; *uid = 0;
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
        if ((c >> 24) == 0) continue;
        if (d->actor >= 0) { *actor = d->actor; return; }
        if (d->thing) { *uid = d->thing; return; }
        return; /* the ground or a floor: nothing to use */
    }
}

/* What kind of thing it is (ObjectType for objects; -1 for books and containers). */
int world_thing_type(int uid) { Thing *t = thing_by_uid(uid); return t && t->def ? t->def->type : -1; }

/* ---------------- Using things on the map (doors, lights, covers) ---------------- */

int world_thing_object_id(int uid) { Thing *t = thing_by_uid(uid); return t && t->category == 4 ? t->itemId : -1; }
int world_thing_instance(int uid) { Thing *t = thing_by_uid(uid); return t ? t->instanceId : 0; }

void world_door_state(int uid, int *open, int *locked)
{
    Thing *t = thing_by_uid(uid);
    *open = t && t->open; *locked = t && t->locked;
}

void world_door_open(int uid) { Thing *t = thing_by_uid(uid); if (t && t->def && t->def->type == T_DOOR && !t->open) door_set_open(t, 1); }

/* TryCloseDoor: closes it - unless someone is standing in the doorway. */
int world_door_close(int uid)
{
    Thing *t = thing_by_uid(uid);
    if (!t || !t->def || t->def->type != T_DOOR || !t->open) return 1;
    Body b;
    thing_box(t, &b);
    for (int k = 0; k <= npcCount; k++)
    {
        Actor *o = actor_of(k);
        int l, tp;
        foot_at(o, o->px, o->py, &l, &tp);
        if (overlaps(&b, l, tp, o->sizeX, o->sizeY)) return 0;
    }
    door_set_open(t, 0);
    return 1;
}

void world_door_set_locked(int uid, int locked) { Thing *t = thing_by_uid(uid); if (t) t->locked = locked; }

int world_light_lit(int uid) { Thing *t = thing_by_uid(uid); return t && t->lit; }
void world_light_set(int uid, int lit) { Thing *t = thing_by_uid(uid); if (t) t->lit = lit; }

/* The bed a cover lies on, or 0. */
int world_bed_under_cover(int uid)
{
    Thing *c = thing_by_uid(uid);
    if (!c) return 0;
    for (int i = 0; i < thingCount; i++)
    {
        Thing *b = &things[i];
        if (b != c && !b->gone && b->def && b->def->type == T_BED && b->def->coverId == c->itemId && cover_of(b) == c) return b->uid;
    }
    return 0;
}

/* The nearest bed to the player (Sleep On Bed "Nearest"), or 0. */
int world_nearest_bed(void)
{
    int best = 0, bestD = 1 << 30, px = (int)floor(player.px / tileW), py = (int)floor(player.py / tileH);
    for (int i = 0; i < thingCount; i++)
    {
        Thing *b = &things[i];
        if (b->gone || !b->def || b->def->type != T_BED) continue;
        int d = abs(b->x - px) + abs(b->y - py);
        if (d < bestD) { bestD = d; best = b->uid; }
    }
    return best;
}

/* Words over a thing for a while ("Locked"). */
typedef struct ThingBark { int uid; char text[64]; uint32_t until; } ThingBark;
static ThingBark thingBarks[8];

void world_bark_thing(int uid, const char *text, int ms)
{
    int slot = 0;
    for (int i = 0; i < 8; i++) if (thingBarks[i].until <= lastMs) { slot = i; break; }
    thingBarks[slot].uid = uid;
    snprintf(thingBarks[slot].text, sizeof thingBarks[slot].text, "%s", text);
    thingBarks[slot].until = lastMs + (uint32_t)(ms > 0 ? ms : 1000);
}

static void draw_thing_barks(uint32_t *pixels)
{
    const Font *f = font_get("InGameFont");
    for (int i = 0; i < 8; i++)
    {
        if (thingBarks[i].until <= lastMs) continue;
        Thing *t = thing_by_uid(thingBarks[i].uid);
        if (!t) continue;
        const Picture *pic = sprite_picture_at(look_of(t), 0);
        int qw = pic ? (pic->width + UNIT - 1) / UNIT * UNIT : tileW, qh = pic ? (pic->height + UNIT - 1) / UNIT * UNIT : tileH;
        int left = t->x * tileW + (tileW - qw) / 2 + t->offX * UNIT - t->z * LIFT_SHIFT, top = (t->y + 1) * tileH - qh + t->offY * UNIT - t->z * LIFT_SHIFT;
        int w = font_text_width(f, thingBarks[i].text);
        font_draw(f, pixels, (int)floor(left + qw / 2.0 - w / 2.0 - cameraX), (int)floor(top - font_line_height(f) - 2 - cameraY), thingBarks[i].text, 0);
    }
}

/* Under a roof (something solid over the player's head): weather isn't drawn. */
int world_indoors(void) { return ceiling_over_player() < (1 << 30); }

/* A light's state, to carry it with the light (picked up, put down). */
void world_light_state(int uid, int *lit, int *spent, double *burnLeft)
{
    Thing *t = thing_by_uid(uid);
    *lit = t && t->lit; *spent = t && t->spent; *burnLeft = t ? t->burnLeft : -1;
}

void world_set_light_state(int uid, int lit, int spent, double burnLeft)
{
    Thing *t = thing_by_uid(uid);
    if (!t) return;
    t->lit = lit; t->spent = spent; t->burnLeft = burnLeft;
}

int world_light_spent(int uid) { Thing *t = thing_by_uid(uid); return t && t->spent; }

/* Lit: it starts burning (its Burn Minutes) if it hasn't yet. */
void world_light_lit_now(int uid)
{
    Thing *t = thing_by_uid(uid);
    if (t && t->def && t->def->burnMinutes > 0 && t->burnLeft < 0) t->burnLeft = t->def->burnMinutes;
}

/* BurnLights for the map: every lit light burns down with the game clock; burnt out, it's spent (its sound, its By Stay event). */
void world_burn_lights(double minutes)
{
    if (minutes <= 0) return;
    for (int i = 0; i < thingCount; i++)
    {
        Thing *t = &things[i];
        if (t->gone || !t->lit || t->burnLeft < 0 || !t->def || t->def->type != T_LIGHT) continue;
        t->burnLeft -= minutes;
        if (t->burnLeft > 0) continue;
        t->lit = 0; t->spent = 1;
        world_sound_at_thing(t->def->spentSound, t->uid);
        world_furniture_event(t->uid, 10, -1); /* By Stay */
    }
}

/* A placed container's own contents (Map Editor: Ctrl + double-click), or NULL. */
void *world_thing_own_contents(int uid)
{
    Thing *t = thing_by_uid(uid);
    cJSON *own = t && t->placed ? get(t->placed, "OwnContents") : NULL;
    if (cJSON_IsArray(own)) return own;
    /* opened in the editor's play test before: its live contents */
    cJSON *live = t && t->placed && num(t->placed, "InstanceId", 0) != 0 ? get(t->placed, "Contents") : NULL;
    return cJSON_IsArray(live) ? live : NULL;
}

/* ---------------- Gravity (SettleAll, Map_chunk::gravity) ---------------- */

typedef struct SettleBody { int left, top, w, d, z, h, elevates, surface, floorOnly, liesUnder, hangs, fixed, index; Thing *t; } SettleBody;

static int by_lift(const void *a, const void *b)
{
    const SettleBody *p = (const SettleBody *)a, *q = (const SettleBody *)b;
    return p->z != q->z ? (p->z < q->z ? -1 : 1) : (p->index < q->index ? -1 : p->index > q->index ? 1 : 0);
}

static int sb_overlaps(const SettleBody *b, const SettleBody *f) { return b->left < f->left + f->w && f->left < b->left + b->w && b->top < f->top + f->d && f->top < b->top + b->d; }

/* BelowCeilingOf: a support starting below the next floor's slab of the floor at this lift. */
static int below_ceiling(const SettleBody *support, int lift) { return support->z < base_lift(floor_of(lift < 0 ? 0 : lift)) + LIFTS_PER_FLOOR - 1; }

static void settle_all_things(void);

/* Gravity for all - but a kite up in the air stays there: nothing holds it but the wind. */
static void settle_all(void)
{
    settle_all_things();
    for (int i = 0; i < thingCount; i++) if (things[i].flying && !things[i].gone) things[i].z = things[i].flying - 1;
}

static void settle_all_things(void)
{
    int n = 0, cap = thingCount + (slabEnd - slabStart) + 1;
    SettleBody *all = (SettleBody *)calloc((size_t)cap, sizeof(SettleBody)), *settled = (SettleBody *)calloc((size_t)cap, sizeof(SettleBody));
    for (int i = slabStart; i < slabEnd; i++)
    {
        /* an upper floor's painted tiles: a slab, a floor */
        Body *b = &bodies[i];
        all[n] = (SettleBody){ b->left, b->top, b->width, b->depth, b->z, b->height, 1, 1, 1, 0, 0, 1, n, NULL };
        n++;
    }
    for (int i = 0; i < thingCount; i++)
    {
        Thing *t = &things[i];
        if (t->gone) continue;
        Body b;
        thing_box(t, &b);
        int type = t->def ? t->def->type : -1;
        int slab = t->def && type == T_TILE && t->def->solid && t->def->floorTile && t->z >= LIFTS_PER_FLOOR && t->z % LIFTS_PER_FLOOR == 0;
        SettleBody *s = &all[n];
        *s = (SettleBody){ b.left, b.top, b.width, b.depth, b.z, b.height, t->elevates || slab, slab || type == 32 /* Table */ || (type == T_TILE && t->def->solid),
                           slab || (type == T_TILE && t->def->solid), t->liesUnder, t->hangs || t->staysPut, 0, n, t };
        /* a tile has no gravity (a floor, a roof); hung on the wall, a wall set's piece, a vehicle's piece: they stay where they are */
        s->fixed = type == T_TILE || t->hangs || t->staysPut || t->wallSetPiece || t->vehicle; /* staying where it's put: at its height, like a hung thing */
        n++;
    }
    qsort(all, (size_t)n, sizeof(SettleBody), by_lift);
    int count = 0;
    for (int i = 0; i < n; i++)
    {
        SettleBody body = all[i];
        if (body.fixed || !body.t) { settled[count++] = body; continue; }
        int floorBase = base_lift(body.t->homeFloor), z = 0;
        for (int k = 0; k < count; k++)
        {
            const SettleBody *b = &settled[k];
            /* never held up by the floor above, nor by something put underneath or hung on the wall, nor by a floor below */
            if (!below_ceiling(b, body.z) || b->liesUnder || b->hangs || b->z < floorBase - 1 || !sb_overlaps(b, &body)) continue;
            int top = b->z + b->h;
            if (body.liesUnder) { if (b->floorOnly && top > z) z = top; }       /* put underneath: on the floor itself, under the table */
            else if (body.elevates) { if (b->elevates && top > z) z = top; }    /* on top of the highest elevating thing */
            else if (b->surface && top > z) z = top;                              /* lying flat: on a surface - a table top is a floor */
        }
        if (z < floorBase) z = floorBase; /* never below the floor it was put on */
        Thing *t = body.t;
        if (z != t->z)
        {
            t->z = z;
            if (t->body >= 0 && bodies[t->body].width > 0) { Body nb; thing_box(t, &nb); bodies[t->body] = nb; }
        }
        body.z = t->body >= 0 ? z : z;
        if (t->def && t->def->type == T_TILE && t->def->solid && t->def->floorTile && t->z >= LIFTS_PER_FLOOR && t->z % LIFTS_PER_FLOOR == 0) body.z = t->z - 1;
        settled[count++] = body;
    }
    free(all); free(settled);
    /* ApplyGravityToPlayer: the player comes down onto what's under them */
    int l, tp;
    player_foot(player.px, player.py, &l, &tp);
    const Body *here[512];
    int hn = 0;
    for (int i = 0; i < bodyCount && hn < 512; i++) if (overlaps(&bodies[i], l, tp, player.sizeX, player.sizeY)) here[hn++] = &bodies[i];
    int below = floor_below(here, hn, player.lift);
    if (below < player.lift && player.levitateMs <= 0) player.lift = below; /* floating (Levitate): nothing pulls them down till it ends */
}

/* Any placed lever (of that object, -1: any) on - or off. */
int world_lever_on(int objectId, int on)
{
    for (int i = 0; i < thingCount; i++)
    {
        Thing *t = &things[i];
        if (t->gone || !t->def || t->def->type != 22 || (objectId >= 0 && t->itemId != objectId)) continue;
        if ((t->open != 0) == (on != 0)) return 1;
    }
    return 0;
}

/* Any door (of that object, -1: any) open - or closed. */
int world_door_open_any(int objectId, int open)
{
    for (int i = 0; i < thingCount; i++)
    {
        Thing *t = &things[i];
        if (t->gone || !t->def || t->def->type != T_DOOR || (objectId >= 0 && t->itemId != objectId)) continue;
        if ((t->open != 0) == (open != 0)) return 1;
    }
    return 0;
}

/* Move Camera: the world spot the view is centred on (held = 0: it follows as usual again). */
void world_camera_hold(int held, double x, double y) { cameraHeld = held; holdX = x; holdY = y; world_update(0, 0, 0); }
void world_camera_center(double *x, double *y)
{
    if (cameraHeld) { *x = holdX; *y = holdY; return; }
    *x = cameraX + SCREEN_W / 2.0; *y = cameraY + SCREEN_H / 2.0;
}
/* Camera Follows: someone else (0 = the player again). */
void world_camera_follow(int actor) { cameraFollow = actor; cameraHeld = 0; }

/* ---------------- Characters and things, for the event commands ---------------- */

int world_actor_gone(int actor) { Actor *a = actor_of(actor); return !a || a->gone; }

/* Place Character "Remove it from the map". */
void world_actor_remove(int actor)
{
    Actor *a = actor_of(actor);
    if (!a || actor == 0) return;
    world_walk_cancel(actor);
    a->gone = 1;
    if (a->body >= 0) { bodies[a->body].width = bodies[a->body].depth = 0; bodies[a->body].z = -100000; }
}

/* Place Character at a cell of a floor: there (brought onto the map if it was off it), standing on that floor. */
void world_actor_place(int actor, int x, int y, int onFloor)
{
    Actor *a = actor_of(actor);
    if (!a || x < 0 || y < 0 || x >= mapW || y >= mapH) return;
    world_walk_cancel(actor);
    a->gone = 0;
    a->px = x * tileW + tileW / 2.0; a->py = y * tileH + tileH / 2.0;
    int l, t;
    foot_at(a, a->px, a->py, &l, &t);
    if (a->body >= 0) bodies[a->body].z = -100000;
    a->lift = standing_lift(l, t, a->sizeX, a->sizeY, onFloor);
    if (a->body >= 0) { Body *b = &bodies[a->body]; b->left = l; b->top = t; b->width = a->sizeX; b->depth = a->sizeY; b->z = a->lift; b->height = a->sizeZ; }
    if (actor == 0) world_update(0, 0, 0); /* the camera follows by its dead zone (GameTestWindow.UpdateCamera), not centred */
}

/* Change Look: someone else's look (its poses), or back to its own. */
typedef struct SavedLook { int actor; const Sprite *idle[8], *walking[8]; Pose poses[MAX_POSES]; int poseCount; } SavedLook;
static SavedLook savedLooks[16]; static int savedLookCount;

void world_change_look(int actor, int other)
{
    Actor *a = actor_of(actor), *o = other >= 0 ? actor_of(other) : NULL;
    if (!a) return;
    int slot = -1;
    for (int i = 0; i < savedLookCount; i++) if (savedLooks[i].actor == actor) slot = i;
    if (!o)
    {
        if (slot < 0) return; /* already its own */
        SavedLook *s = &savedLooks[slot];
        memcpy(a->idle, s->idle, sizeof a->idle); memcpy(a->walking, s->walking, sizeof a->walking);
        memcpy(a->poses, s->poses, sizeof a->poses); a->poseCount = s->poseCount;
        savedLooks[slot] = savedLooks[--savedLookCount];
    }
    else
    {
        if (slot < 0 && savedLookCount < 16)
        {
            SavedLook *s = &savedLooks[savedLookCount++];
            s->actor = actor;
            memcpy(s->idle, a->idle, sizeof a->idle); memcpy(s->walking, a->walking, sizeof a->walking);
            memcpy(s->poses, a->poses, sizeof a->poses); s->poseCount = a->poseCount;
        }
        memcpy(a->idle, o->idle, sizeof a->idle); memcpy(a->walking, o->walking, sizeof a->walking);
        memcpy(a->poses, o->poses, sizeof a->poses); a->poseCount = o->poseCount;
    }
    a->shownPose = -1;
}

/* Change Object Look (NULL: its own again). */
void world_thing_look(int uid, const Sprite *sprite) { Thing *t = thing_by_uid(uid); if (t) t->lookOverride = sprite; }

/* Move Object: to a cell of a floor, resting on whatever is there. */
void world_thing_move(int uid, int x, int y, int onFloor)
{
    Thing *t = thing_by_uid(uid);
    if (!t || x < 0 || y < 0 || x >= mapW || y >= mapH) return;
    t->x = x; t->y = y; t->offX = t->offY = 0;
    t->seq = nextUid++; /* put down again: over what it ties with, as the Windows engine re-adds its visual */
    /* put down by hand: no longer tucked underneath anything, hung on the wall, nor staying put (DropOnMap) */
    int wasHung = t->hangs || t->staysPut;
    t->liesUnder = 0; t->hangs = 0; t->staysPut = 0;
    if (wasHung && t->solid && t->body < 0) { ensure_bodies(1); t->body = bodyCount; t->bodyN = 1; thing_box(t, &bodies[bodyCount++]); } /* off the wall: in the way again */
    Body b;
    thing_box(t, &b);
    if (t->body >= 0) bodies[t->body].z = -100000;
    t->z = standing_lift(b.left, b.top, b.width, b.depth, onFloor);
    t->homeFloor = onFloor;
    if (t->body >= 0) { if (bodies[t->body].width || t->solid) thing_box(t, &bodies[t->body]); }
    settle_all();
}

/* Set Door State: Locked (shut), Unlocked, Closed, Open. */
void world_door_set(int uid, const char *state)
{
    Thing *t = thing_by_uid(uid);
    if (!t || !t->def || t->def->type != T_DOOR) return;
    if (!strcmp(state, "Locked")) { if (t->open) door_set_open(t, 0); t->locked = 1; }
    else if (!strcmp(state, "Unlocked")) t->locked = 0;
    else if (!strcmp(state, "Closed")) { if (t->open) door_set_open(t, 0); }
    else if (!strcmp(state, "Open")) { t->locked = 0; if (!t->open) door_set_open(t, 1); }
}

/* Change Tile: a cell's tile on a floor (-1: none). The ground's walkability follows its tile; an upper floor's tile is a slab to stand on. */
int world_set_tile(int x, int y, int onFloor, int tile)
{
    if (x < 0 || y < 0 || x >= mapW || y >= mapH) return 0;
    if (onFloor < 1) onFloor = 1;
    if (onFloor > MAX_FLOORS) onFloor = MAX_FLOORS;
    if (!floors[onFloor])
    {
        floors[onFloor] = (int *)malloc(sizeof(int) * (size_t)(mapW * mapH));
        for (int i = 0; i < mapW * mapH; i++) floors[onFloor][i] = -1;
    }
    int was = floors[onFloor][y * mapW + x];
    floors[onFloor][y * mapW + x] = tile;
    if (onFloor == 1)
    {
        cJSON *byKey = get(get(get(gameJson, "Project"), "TileProperties"), "ByTileKey");
        walkable[y * mapW + x] = 1;
        underTile[y * mapW + x] = -1;
        if (tile >= 0)
        {
            char key[200];
            int slot = tile / 100000;
            snprintf(key, sizeof key, "%s:%d", slot < sheetCount ? sheetNames[slot] : "", tile % 100000);
            cJSON *props = get(byKey, key);
            if (props && !flag(props, "Walkable")) walkable[y * mapW + x] = 0;
            water[y * mapW + x] = props && flag(props, "IsWater");
            if (props && num(props, "UnderTileIndex", -1) >= 0) underTile[y * mapW + x] = slot * 100000 + num(props, "UnderTileIndex", -1);
        }
        else water[y * mapW + x] = 0;
        return 1;
    }
    /* an upper floor's slab: added, or taken away */
    int left = x * unitsX, top = y * unitsY, z = base_lift(onFloor) - 1;
    for (int i = slabStart; i < slabEnd; i++)
        if (bodies[i].left == left && bodies[i].top == top && bodies[i].z == z)
        {
            if (tile < 0) { bodies[i].width = bodies[i].depth = 0; }
            else { bodies[i].width = unitsX; bodies[i].depth = unitsY; }
            return 1;
        }
    if (tile >= 0 && was < 0)
    {
        if (bodyCount + 1 >= bodyCap) { bodyCap *= 2; bodies = (Body *)realloc(bodies, sizeof(Body) * (size_t)bodyCap); }
        Body *b = &bodies[bodyCount++];
        b->left = left; b->top = top; b->width = unitsX; b->depth = unitsY; b->z = z; b->height = 1;
    }
    return 1;
}

/* ---------------- Play Animation: a sprite played at a spot or over someone ---------------- */

typedef struct Effect { int used, follows; const Sprite *sprite; double x, y; int lift; uint32_t since, until; } Effect;
static Effect effects[16];

int world_effect_start(const Sprite *sprite, int follows, int cellX, int cellY)
{
    for (int i = 0; i < 16; i++)
        if (!effects[i].used)
        {
            Effect *e = &effects[i];
            e->used = 1; e->sprite = sprite; e->follows = follows; e->since = lastMs; e->until = 0;
            if (follows < 0)
            {
                e->x = cellX * tileW + tileW / 2.0; e->y = cellY * tileH + tileH / 2.0;
                e->lift = standing_lift(cellX * unitsX, cellY * unitsY, unitsX, unitsY, floor_of(player.lift)); /* on whatever is there, on the player's floor */
            }
            return i;
        }
    return -1;
}

void world_effect_stop(int id) { if (id >= 0 && id < 16) effects[id].used = 0; }

/* Over the world (just under the words over heads): bottom-centre on the feet line, up-left by its lift. */
static void draw_effects(uint32_t *pixels)
{
    for (int i = 0; i < 16; i++)
    {
        Effect *e = &effects[i];
        if (!e->used) continue;
        if (e->until && lastMs >= e->until) { e->used = 0; continue; } /* played once */
        double x = e->x, y = e->y;
        int lift = e->lift;
        if (e->follows >= 0) { const Actor *a = actor_of(e->follows); if (!a) continue; x = a->px; y = a->py; lift = a->lift; }
        const Picture *pic = sprite_picture_at(e->sprite, lastMs - e->since);
        if (!pic) continue;
        int qw = (pic->width + UNIT - 1) / UNIT * UNIT, qh = (pic->height + UNIT - 1) / UNIT * UNIT;
        screen = pixels;
        blit_box(pic, (int)floor(x - qw / 2.0 - lift * LIFT_SHIFT - cameraX), (int)floor(y + tileH / 2.0 - qh - lift * LIFT_SHIFT - cameraY), qw, qh);
    }
}

/* ---------------- Hits, bodies, tiles (for vitals) ---------------- */

static int outlineActor = -1; static uint32_t outlineUntil;

/* Exult's hit: a red outline round the character's frame for 200 ms. */
void world_hit_outline(int actor) { outlineActor = actor; outlineUntil = lastMs + 200; }

/* The outline, drawn round the frame the character shows now (every see-through pixel touching a solid one). */
static void draw_hit_outline(uint32_t *pixels)
{
    if (outlineActor < 0 || lastMs >= outlineUntil) return;
    for (int i = 0; i < drawnCount; i++)
    {
        const Drawn *d = &drawn[i];
        if (d->actor != outlineActor) continue;
        const Picture *p = d->picture;
        double s = fmin((double)d->w / p->width, (double)d->h / p->height);
        int dw = (int)(p->width * s + 0.5), dh = (int)(p->height * s + 0.5), ox = d->x + (d->w - dw) / 2, oy = d->y + (d->h - dh) / 2;
#define SOLID(x, y) ((x) >= 0 && (y) >= 0 && (x) < dw && (y) < dh && (p->pixels[(size_t)((y) * p->height / dh) * (size_t)p->width + (size_t)((x) * p->width / dw)] >> 24) > 127)
        for (int y = -1; y <= dh; y++)
            for (int x = -1; x <= dw; x++)
            {
                if (SOLID(x, y) || !(SOLID(x - 1, y) || SOLID(x + 1, y) || SOLID(x, y - 1) || SOLID(x, y + 1))) continue;
                int sx = ox + x, sy = oy + y;
                frame_dot(pixels, sx, sy, 0xFFE01010u);
            }
#undef SOLID
    }
}

/* A body where someone fell: a container on the map looking like them (their Body pose), not solid, walked over. Its uid. */
int world_put_body(int actor, int x, int y, int onFloor)
{
    Actor *a = actor_of(actor);
    if (!a) return -1;
    if (thingCount >= thingCap) { thingCap *= 2; things = (Thing *)realloc(things, sizeof(Thing) * (size_t)thingCap); }
    Thing *t = &things[thingCount];
    memset(t, 0, sizeof *t);
    t->uid = nextUid++; t->category = 5; t->itemId = -1000 - a->characterId; t->quantity = 1; t->burnLeft = -1;
    t->x = x; t->y = y; t->facing = a->facing;
    const Sprite *body = NULL;
    for (int i = 0; i < a->poseCount; i++) if (!strcmp(a->poses[i].name, "Body")) body = a->poses[i].dirs[a->facing];
    t->sprite = body ? body : a->idle[a->facing];
    t->sizeX = a->sizeX; t->sizeY = a->sizeY; t->sizeZ = 1;
    t->homeFloor = onFloor;
    Body b;
    thing_box(t, &b);
    t->z = standing_lift(b.left, b.top, b.width, b.depth, onFloor);
    t->body = -1;
    thingCount++;
    return t->uid;
}

/* The key of the tile a floor shows at a cell ("sheet:local"), for Tile Properties; 0 if none. */
int world_tile_key(int x, int y, int onFloor, char *key, size_t size)
{
    if (onFloor < 1 || onFloor > MAX_FLOORS || !floors[onFloor] || x < 0 || y < 0 || x >= mapW || y >= mapH) return 0;
    int tile = floors[onFloor][y * mapW + x];
    if (tile < 0) return 0;
    int slot = tile / 100000;
    snprintf(key, size, "%s:%d", slot < sheetCount ? sheetNames[slot] : "", tile % 100000);
    return 1;
}

/* ---------------- For combat ---------------- */

int world_actor_lift(int actor) { Actor *a = actor_of(actor); return a ? a->lift : 0; }

/*
 * HasLineOfSight: nothing solid taller than eye level (3 lifts above the
 * feet) on the straight line between two feet positions. Characters and
 * windows don't block sight. Only the solids in the line's box are tried.
 */
int world_line_clear(double fx, double fy, int fromLift, double tx, double ty)
{
    static const Body *solids[512];
    int n = 0;
    int left = (int)floor(fmin(fx, tx) / UNIT) - 1, right = (int)ceil(fmax(fx, tx) / UNIT) + 1;
    int top = (int)floor((fmin(fy, ty) + tileH / 2.0) / UNIT) - 2, bottom = (int)ceil((fmax(fy, ty) + tileH / 2.0) / UNIT) + 2;
    for (int i = 0; i < thingCount && n < 512; i++)
    {
        const Thing *t = &things[i];
        if (t->gone || t->body < 0) continue;
        if (t->def && t->def->type == T_WINDOW && (!t->def->winShutters || !t->def->winBlocks || t->open || t->spent)) continue; /* LetsSightThrough */
        const Body *b = &bodies[t->body];
        if (b->z + b->height <= fromLift + 3 || b->z > fromLift + 3) continue;
        if (b->left >= right || b->left + b->width <= left || b->top >= bottom || b->top + b->depth <= top) continue;
        solids[n++] = b;
    }
    if (!n) return 1;
    double length = fmax(fabs(tx - fx), fabs(ty - fy));
    int steps = (int)(length / 4);
    if (steps < 1) steps = 1;
    for (int i = 1; i < steps; i++)
    {
        double x = fx + (tx - fx) * i / steps, y = fy + (ty - fy) * i / steps + tileH / 2.0 - UNIT / 2.0;
        int ux = (int)floor(x / UNIT), uy = (int)floor(y / UNIT);
        for (int k = 0; k < n; k++)
            if (ux >= solids[k]->left && ux < solids[k]->left + solids[k]->width && uy >= solids[k]->top && uy < solids[k]->top + solids[k]->depth) return 0;
    }
    return 1;
}

/* A sprite at a map position and lift (a shot in the air). */
void world_draw_sprite(uint32_t *pixels, const Sprite *sprite, double x, double y, int lift, uint32_t ms)
{
    const Picture *pic = sprite_picture_at(sprite, ms);
    if (!pic) return;
    int qw = (pic->width + UNIT - 1) / UNIT * UNIT, qh = (pic->height + UNIT - 1) / UNIT * UNIT;
    screen = pixels;
    blit_box(pic, (int)floor(x - qw / 2.0 - lift * LIFT_SHIFT - cameraX), (int)floor(y + tileH / 2.0 - qh - lift * LIFT_SHIFT - cameraY), qw, qh);
}

/* The project's id of a character (a copy placed again: the character it's a copy of). */
int world_character_source(int characterId) { return characterId >= 0 ? characterId % COPY_STEP : characterId; }

/* ---------------- Things struck and broken (Attack Object) ---------------- */

/* Its hit points in play, starting from its own (Game_object::reduce_health). */
int world_thing_hp(int uid, int initial)
{
    Thing *t = thing_by_uid(uid);
    if (!t) return 0;
    if (!t->hpSet) { t->hp = initial; t->hpSet = 1; }
    return t->hp;
}

void world_thing_set_hp(int uid, int hp) { Thing *t = thing_by_uid(uid); if (t) { t->hp = hp; t->hpSet = 1; } }
int world_thing_z(int uid) { Thing *t = thing_by_uid(uid); return t ? t->z : 0; }
int world_thing_floor(int uid) { Thing *t = thing_by_uid(uid); return t ? floor_of(t->z) : 1; }
int world_thing_spent(int uid) { Thing *t = thing_by_uid(uid); return t && t->spent; }
int world_thing_belongs_to(int uid) { Thing *t = thing_by_uid(uid); return !t ? -1 : t->owner ? t->owner - 1 : num(t->placed, "BelongsToId", -1); }
void world_thing_set_belongs(int uid, int characterId) { Thing *t = thing_by_uid(uid); if (t) t->owner = characterId + 1; }

/* A window's glass broken where it is: its Broken picture from now on. */
void world_thing_break_glass(int uid, const Sprite *broken) { Thing *t = thing_by_uid(uid); if (t) { t->spent = 1; if (broken) t->lookOverride = broken; } }

void world_thing_center(int uid, double *px, double *py)
{
    Thing *t = thing_by_uid(uid);
    *px = t ? t->x * tileW + tileW / 2.0 : 0; *py = t ? t->y * tileH + tileH / 2.0 : 0;
}

/* Things on the map (not gone) whose cell middle is within a distance (pixels, the larger axis) of a point. */
int world_things_near(double px, double py, double reach, int *out, int max)
{
    int n = 0;
    for (int i = 0; i < thingCount && n < max; i++)
    {
        const Thing *t = &things[i];
        if (t->gone) continue;
        double x = t->x * tileW + tileW / 2.0, y = t->y * tileH + tileH / 2.0;
        if (fmax(fabs(x - px), fabs(y - py)) <= reach) out[n++] = t->uid;
    }
    return n;
}

/* A sprite played once at a cell (a blast), then gone. */
void world_effect_once(const Sprite *sprite, int cellX, int cellY)
{
    if (!sprite) return;
    int id = world_effect_start(sprite, -1, cellX, cellY);
    if (id >= 0) effects[id].until = lastMs + (uint32_t)(sprite->totalMs > 0 ? sprite->totalMs : 500);
}

/* The tile a floor shows at a cell (-1: none), as Change Tile sets it. */
int world_get_tile(int x, int y, int onFloor)
{
    if (x < 0 || y < 0 || x >= mapW || y >= mapH || onFloor < 1 || onFloor > MAX_FLOORS || !floors[onFloor]) return -1;
    return floors[onFloor][y * mapW + x];
}

/* ---------------- For the inventory windows (gumps.c) ---------------- */

/* A sprite scaled into a box on the screen (kept in proportion, centred). */
void world_blit(uint32_t *pixels, const Picture *picture, int x, int y, int w, int h)
{
    screen = pixels;
    blit_box(picture, x, y, w, h);
}

/* How a carried thing looks: an object's own picture (lit, spent), a book's or container's (a locked container: its Locked picture). */
/* A carried thing's look (GameTestWindow.StateOf(InventoryItemInstance) + ObjectLooks.SpriteFor): as a placed one in the same
   state - lit or spent, how many (a bundle's picture), what it holds. */
const Sprite *world_item_look(int category, int itemId, int lit, int spent, int locked, int quantity, int liquid)
{
    if (category == 4)
    {
        const ObjectDef *d = def_of(itemId);
        if (!d) return NULL;
        Thing as;
        memset(&as, 0, sizeof as);
        as.def = d; as.lit = lit; as.spent = spent; as.quantity = quantity > 1 ? quantity : 1; as.liquid = liquid; as.facing = DIR_S;
        return look_of(&as);
    }
    cJSON *list = category == 5 ? get(get(get(gameJson, "Project"), "Containers"), "Containers") : get(get(gameJson, "Books"), "Books"), *o;
    cJSON_ArrayForEach(o, list)
        if (num(o, "Id", -2) == itemId)
        {
            const Sprite *lockedLook = locked && category == 5 ? sprite_find(str(o, "LockedSpriteGroup"), str(o, "LockedSpriteName")) : NULL;
            return lockedLook ? lockedLook : sprite_find(str(o, "SpriteGroup"), str(o, "SpriteName"));
        }
    return NULL;
}

/* A thing on the map as it looks now. */
const Sprite *world_thing_sprite(int uid) { Thing *t = thing_by_uid(uid); return t ? look_of(t) : NULL; }

/* The map cell under a point on the screen, on the player's floor. */
void world_screen_cell(int sx, int sy, int *x, int *y, int *onFloor)
{
    *x = (int)floor((sx + cameraX) / tileW); *y = (int)floor((sy + cameraY) / tileH); *onFloor = floor_of(player.lift);
}

/* ---------------- Reach (GameTestWindow.Reach: Exult's Fast_pathfinder_client::is_grabable, paths.cc) ----------------
   1. Within 1 tile (8px unit) of the player, sizes counted (Game_object::distance): reachable.
   2. Else, for margins 1..5: a short A* walk by the player's footprint (a step rises or drops at most 1 lift, 8 ways, no
      cutting corners, at most 2x the distance (8..64 steps)) till the footprint touches the target box grown by the margin
      and is within 5 lifts of it.
   3. From there a straight 3D line (Zombie) to the target's nearest edge must not pass through anything solid that can't be
      moved. Walls, doors and windows reach up to the roof; you reach over a table. */

#define MAX_GRAB_DIST 5
typedef struct T3 { int x, y, z; } T3;
typedef struct Vol { int x, y, z, w, d, h; } Vol;
typedef struct ReachSolid { Body b; int table, movable; } ReachSolid;
static ReachSolid *reachSolids; static int reachSolidCount, reachSolidCap;

static int vol_has(const Vol *v, T3 t) { return t.x >= v->x && t.x < v->x + v->w && t.y >= v->y && t.y < v->y + v->d && t.z >= v->z && t.z < v->z + v->h; }
static T3 anchor_of(const Vol *v) { T3 t = { v->x + v->w - 1, v->y + v->d - 1, v->z }; return t; }
static int imax(int a, int b) { return a > b ? a : b; }
static int tile_distance(T3 a, T3 b) { return imax(imax(abs(b.x - a.x), abs(b.y - a.y)), abs(b.z - a.z)); }

static void add_reach_solid(const Body *b, int table, int movable)
{
    if (b->z <= -100000 || b->width <= 0 || b->depth <= 0) return;
    if (reachSolidCount >= reachSolidCap) { reachSolidCap = reachSolidCap * 2 + 64; reachSolids = (ReachSolid *)realloc(reachSolids, sizeof(ReachSolid) * (size_t)reachSolidCap); }
    ReachSolid *r = &reachSolids[reachSolidCount++];
    r->b = *b; r->table = table; r->movable = movable;
}

/* ReachQuery's solids: the things' boxes (walls, doors and windows to the roof), the upper floors' slabs, the characters. */
static void reach_solids(void)
{
    reachSolidCount = 0;
    for (int i = 0; i < thingCount; i++)
    {
        const Thing *t = &things[i];
        if (t->gone || t->body < 0) continue;
        int type = t->def ? t->def->type : -1;
        int wallLike = type == T_WALL || type == T_DOOR || type == T_WINDOW;
        for (int k = 0; k < t->bodyN; k++)
        {
            Body b = bodies[t->body + k];
            if (wallLike && b.height < 64) b.height = 64;
            add_reach_solid(&b, type == T_TABLE, t->def ? t->def->portable : 1); /* IsMovable: an object if Portable; books and containers can always be moved */
        }
    }
    for (int i = slabStart; i < slabEnd; i++) add_reach_solid(&bodies[i], 0, 0); /* a floor overhead: not reached through */
    for (int i = 0; i < npcCount; i++) if (!npcs[i].gone && npcs[i].body >= 0) add_reach_solid(&bodies[npcs[i].body], 0, 1);
}

/* BlockingAt / ReachBlockerAt: the solid filling this tile and lift (forReach: not a table). -1: none. */
static int reach_blocker(T3 t, int forReach)
{
    for (int i = 0; i < reachSolidCount; i++)
    {
        const Body *b = &reachSolids[i].b;
        if (t.x >= b->left && t.x < b->left + b->width && t.y >= b->top && t.y < b->top + b->depth && t.z >= b->z && t.z < b->z + b->height)
        {
            if (forReach && reachSolids[i].table) continue;
            return i;
        }
    }
    return -1;
}

static int reach_at_goal(T3 tile, T3 goal, int bl, int bt, int bw, int bd, int aw, int ad)
{
    int dz = tile.z - goal.z;
    if (dz > 5 || dz < -5) return 0;
    int left = tile.x - aw + 1, top = tile.y - ad + 1;
    return left < bl + bw && bl < left + aw && top < bt + bd && bt < top + ad;
}

/* StepTo: the player's footprint (lower-right at this tile), 1 lift tall, rising or dropping at most 1 lift; -1 blocked.
   Remembered per tile for this question (each is asked several times: the diagonals' corners). */
#define STEP_MEMO 8192
typedef struct StepMemo { int x, y, z, lift, used; } StepMemo;
static StepMemo stepMemo[STEP_MEMO];
static int reach_step(int x, int y, int z, int aw, int ad)
{
    unsigned h = ((unsigned)(x * 73856093) ^ (unsigned)(y * 19349663) ^ (unsigned)(z * 83492791)) & (STEP_MEMO - 1);
    for (int probe = 0; probe < 16; probe++, h = (h + 1) & (STEP_MEMO - 1))
    {
        StepMemo *m = &stepMemo[h];
        if (m->used && m->x == x && m->y == y && m->z == z) return m->lift;
        if (!m->used) { m->used = 1; m->x = x; m->y = y; m->z = z; m->lift = try_step(x - aw + 1, y - ad + 1, aw, ad, z, 1); return m->lift; }
    }
    return try_step(x - aw + 1, y - ad + 1, aw, ad, z, 1);
}

/* path.cc Find_path (A*) with Fast_pathfinder_client's costs and limits: the steps (not the start) into out; -1 no path.
   The open list a binary heap ordered by total, then by when added (the Windows engine's priority queue). */
#define REACH_NODES 8192
typedef struct ReachNode { T3 at; int cost, parent, closed; } ReachNode;
typedef struct HeapEntry { int total; long order; int node; } HeapEntry;
static ReachNode reachNodes[REACH_NODES];
static HeapEntry reachHeap[REACH_NODES * 4];
static int reachHash[16384];
static unsigned reach_key(T3 t) { return ((unsigned)(t.x * 73856093) ^ (unsigned)(t.y * 19349663) ^ (unsigned)(t.z * 83492791)) & 16383u; }
static int reach_find(T3 t)
{
    for (unsigned h = reach_key(t);; h = (h + 1) & 16383u)
    {
        int i = reachHash[h];
        if (i < 0) return -1;
        if (reachNodes[i].at.x == t.x && reachNodes[i].at.y == t.y && reachNodes[i].at.z == t.z) return i;
    }
}
static int reach_add(T3 t, int *count)
{
    if (*count >= REACH_NODES) return -1;
    unsigned h = reach_key(t);
    while (reachHash[h] >= 0) h = (h + 1) & 16383u;
    reachHash[h] = *count;
    reachNodes[*count].at = t;
    return (*count)++;
}
static int heap_less(const HeapEntry *a, const HeapEntry *b) { return a->total < b->total || (a->total == b->total && a->order < b->order); }
static void heap_push(int *n, HeapEntry e)
{
    if (*n >= REACH_NODES * 4) return;
    int i = (*n)++;
    while (i > 0) { int p = (i - 1) / 2; if (!heap_less(&e, &reachHeap[p])) break; reachHeap[i] = reachHeap[p]; i = p; }
    reachHeap[i] = e;
}
static HeapEntry heap_pop(int *n)
{
    HeapEntry top = reachHeap[0], last = reachHeap[--(*n)];
    int i = 0;
    for (;;)
    {
        int c = 2 * i + 1;
        if (c >= *n) break;
        if (c + 1 < *n && heap_less(&reachHeap[c + 1], &reachHeap[c])) c++;
        if (!heap_less(&reachHeap[c], &last)) break;
        reachHeap[i] = reachHeap[c]; i = c;
    }
    if (*n > 0) reachHeap[i] = last;
    return top;
}

static int reach_path(T3 start, T3 goal, int bl, int bt, int bw, int bd, int aw, int ad, T3 *out, int max)
{
    int estimate0 = tile_distance(start, goal);
    int maxCost = 2 * estimate0; if (maxCost < 8) maxCost = 8; if (maxCost > 64) maxCost = 64; /* "Don't try too hard." */
    for (int i = 0; i < 16384; i++) reachHash[i] = -1;
    int count = 0, heapN = 0; long order = 0;
    int s = reach_add(start, &count);
    reachNodes[s].cost = 0; reachNodes[s].parent = -1; reachNodes[s].closed = 0;
    heap_push(&heapN, (HeapEntry){ estimate0, order++, s });
    while (heapN > 0)
    {
        HeapEntry e = heap_pop(&heapN);
        int cur = e.node;
        if (reachNodes[cur].closed) continue; /* a stale entry */
        reachNodes[cur].closed = 1;
        T3 c = reachNodes[cur].at;
        if (reach_at_goal(c, goal, bl, bt, bw, bd, aw, ad))
        {
            int n = 0;
            for (int k = cur; reachNodes[k].parent >= 0; k = reachNodes[k].parent) n++;
            if (n > max) return -1;
            int k = cur;
            for (int i = n - 1; i >= 0; i--) { out[i] = reachNodes[k].at; k = reachNodes[k].parent; }
            return n;
        }
        int costHere = reachNodes[cur].cost;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (!dx && !dy) continue;
                int lift = reach_step(c.x + dx, c.y + dy, c.z, aw, ad);
                if (lift < 0) continue;
                if (dx && dy && (reach_step(c.x + dx, c.y, c.z, aw, ad) < 0 || reach_step(c.x, c.y + dy, c.z, aw, ad) < 0)) continue; /* no cutting a corner */
                T3 next = { c.x + dx, c.y + dy, lift };
                int newCost = costHere + 1, estimate = tile_distance(next, goal);
                if (newCost + estimate >= maxCost) continue; /* too far away */
                int k = reach_find(next);
                if (k >= 0 && reachNodes[k].cost <= newCost) continue;
                if (k < 0 && (k = reach_add(next, &count)) < 0) continue;
                reachNodes[k].cost = newCost; reachNodes[k].parent = cur; reachNodes[k].closed = 0;
                heap_push(&heapN, (HeapEntry){ newCost + estimate, order++, k });
            }
    }
    return -1;
}

/* paths.cc Get_closest_edge */
static void closest_edge(const Vol *from, const Vol *to, T3 *p1, T3 *p2)
{
    if (p2->x < p1->x) p1->x = from->x; else p2->x = to->x;
    if (p2->y < p1->y) p1->y = from->y; else p2->y = to->y;
    if (p2->z < p1->z) p2->z += to->h - 1;
    p1->z += from->h - 1;
}

/* Game_object::distance with both sizes counted, then Tile_coord::distance */
static int exult_distance(const Vol *a, const Vol *b)
{
    T3 t1 = anchor_of(a), t2 = anchor_of(b);
    int x1 = t1.x, y1 = t1.y, z1 = t1.z, x2 = t2.x, y2 = t2.y, z2 = t2.z;
    if (x2 > x1) x2 -= b->w - 1; else if (x2 < x1) x1 -= a->w - 1;
    if (y2 > y1) y2 -= b->d - 1; else if (y2 < y1) y1 -= a->d - 1;
    int dz = z1 - z2;
    if (dz < 0) { if (z1 + a->h > z2) z1 = z2; else z1 += a->h; }
    else if (dz > 0) { if (z2 + b->h > z1) z2 = z1; else z2 += b->h; }
    return imax(imax(abs(x2 - x1), abs(y2 - y1)), abs(z2 - z1));
}

/* Zombie.cc: a straight line through 3D tiles (not the start), stopping below ground. Returns how many. */
static int zombie_line(T3 from, T3 to, T3 *out, int max)
{
    int d[3] = { to.x - from.x, to.y - from.y, to.z - from.z };
    if (!d[0] && !d[1] && !d[2]) return 0;
    int a[3] = { abs(d[0]), abs(d[1]), abs(d[2]) }, dir[3] = { d[0] >= 0 ? 1 : -1, d[1] >= 0 ? 1 : -1, d[2] >= 0 ? 1 : -1 };
    int cur[3] = { from.x, from.y, from.z };
    int major, m1, m2;
    if (a[2] >= a[0] && a[2] >= a[1]) { major = 2; m1 = 0; m2 = 1; }
    else if (a[1] >= a[0] && a[1] >= a[2]) { major = 1; m1 = 0; m2 = 2; }
    else { major = 0; m1 = 1; m2 = 2; }
    int sum1 = 0, sum2 = 0, n = 0;
    for (int remaining = a[major]; remaining > 0 && n < max; remaining--)
    {
        sum1 += a[m1]; sum2 += a[m2];
        int step1 = sum1 / a[major], step2 = sum2 / a[major];
        sum1 %= a[major]; sum2 %= a[major];
        cur[major] += dir[major]; cur[m1] += dir[m1] * step1; cur[m2] += dir[m2] * step2;
        if (cur[2] < 0) break;
        out[n].x = cur[0]; out[n].y = cur[1]; out[n].z = cur[2]; n++;
    }
    return n;
}

/* Staying where it's put (IsReachable's stuckIn): what it's stuck in or on - solids over its footprint - doesn't stand in the way. */
static const Vol *reachStuckIn;

/* Fast_pathfinder_client::is_grabable_internal */
static int grabable_internal(const Vol *fromVol, T3 targetTile, const Vol *toVol, int bl, int bt, int bw, int bd)
{
    T3 fromTile = anchor_of(fromVol), src = fromTile, dst = targetTile;
    closest_edge(fromVol, toVol, &src, &dst);
    src = fromTile; src.z = fromVol->z; /* the walk starts from where the player really stands */
    static T3 path[128];
    int n = reach_path(src, dst, bl, bt, bw, bd, fromVol->w, fromVol->d, path, 128);
    if (n < 0) return 0;
    T3 t = fromTile;
    for (int i = 0; i < n; i++)
    {
        t = path[i];
        if (!(t.x == fromTile.x && t.y == fromTile.y && t.z == fromTile.z) && !reach_at_goal(t, dst, bl, bt, bw, bd, fromVol->w, fromVol->d) && reach_blocker(t, 0) >= 0) return 0; /* blocked */
    }
    if (!reach_at_goal(t, dst, bl, bt, bw, bd, fromVol->w, fromVol->d)) return 0;
    Vol moved = *fromVol;
    moved.x += t.x - src.x; moved.y += t.y - src.y; moved.z += t.z - src.z;
    dst = targetTile;
    closest_edge(&moved, toVol, &t, &dst);
    static T3 line[256];
    int m = zombie_line(t, dst, line, 256);
    if (m == 0) return 0; /* "if (zpath.get_num_steps() == 0) return false;" */
    for (int i = 0; i < m; i++)
    {
        if (vol_has(toVol, line[i]) || vol_has(fromVol, line[i]) || vol_has(&moved, line[i])) continue;
        int b = reach_blocker(line[i], 1);
        if (b >= 0 && !reachSolids[b].movable)
        {
            const Body *o = &reachSolids[b].b;
            const Vol *s = reachStuckIn;
            if (!(s && o->left < s->x + s->w && s->x < o->left + o->width && o->top < s->y + s->d && s->y < o->top + o->depth)) return 0;
        }
    }
    return 1;
}

static int grabable(Vol target, int isSpot)
{
    int l, t;
    player_foot(player.px, player.py, &l, &t);
    Vol from = { l, t, player.lift, player.sizeX, player.sizeY, player.sizeZ };
    Vol near = target;
    if (isSpot) { near.w = 1; near.d = 1; near.h = 0; }
    if (exult_distance(&from, &near) <= 1) return 1;
    reach_solids();
    memset(stepMemo, 0, sizeof stepMemo);
    /* the walk can't go further than 64 tiles: only the boxes round here are looked at for each step */
    stepWindowOn = 1; stepWindowL = from.x - 70; stepWindowT = from.y - 70; stepWindowR = from.x + 70; stepWindowB = from.y + 70; stepWindowN = 0;
    for (int i = 0; i < bodyCount; i++)
        if (bodies[i].left < stepWindowR && bodies[i].left + bodies[i].width > stepWindowL && bodies[i].top < stepWindowB && bodies[i].top + bodies[i].depth > stepWindowT)
        { if (stepWindowN >= stepWindowCap) { stepWindowCap = stepWindowCap * 2 + 256; stepWindow = (int *)realloc(stepWindow, sizeof(int) * (size_t)stepWindowCap); } stepWindow[stepWindowN++] = i; }
    int found = 0;
    for (int margin = 1; margin <= MAX_GRAB_DIST; margin++)
    {
        int bl = target.x - margin, bt = target.y - margin;
        int bw = isSpot ? 2 * margin : target.w + 2 * margin, bd = isSpot ? 2 * margin : target.d + 2 * margin;
        if (grabable_internal(&from, anchor_of(&target), &target, bl, bt, bw, bd)) { found = 1; break; }
    }
    stepWindowOn = 0;
    return found;
}

/* Is this thing on the map within the player's reach (its own box, solid or not)? */
/* A kite in the air: the lift it's at (flying), or back to normal (-1). */
void world_thing_set_flying(int uid, int lift)
{
    Thing *t = thing_by_uid(uid);
    if (!t) return;
    t->flying = lift >= 0 ? lift + 1 : 0;
    if (lift >= 0) t->z = lift;
    else settle_all();
}

void world_actor_joined(int actor, const Sprite *sprite, int frame)
{
    Actor *a = actor_of(actor);
    if (a) { a->joinedSprite = sprite; a->joinedFrame = frame; }
}

int world_reachable_thing(int uid)
{
    Thing *t = thing_by_uid(uid);
    if (!t) return 0;
    if (t->flying && abs(t->z - player.lift) >= LIFTS_PER_FLOOR) return 0; /* a kite up in the air: not from the ground */
    Body b;
    thing_box(t, &b);
    Vol v = { b.left, b.top, b.z, b.width > 0 ? b.width : 1, b.depth > 0 ? b.depth : 1, b.height > 0 ? b.height : 1 };
    reachStuckIn = t->staysPut ? &v : NULL;
    int reached = grabable(v, 0);
    reachStuckIn = NULL;
    return reached;
}

/* Is this spot (the middle of a cell, at a lift) within the player's reach (IsSpotReachable)? */
int world_reachable(int x, int y, int z)
{
    Vol v = { x * unitsX + unitsX / 2, y * unitsY + unitsY / 2, z, 1, 1, 1 };
    return grabable(v, 1);
}

/* A stack on the map grows (a stackable thing dropped on it). */
void world_thing_add_quantity(int uid, int n) { Thing *t = thing_by_uid(uid); if (t) t->quantity += n; }
int world_thing_quantity(int uid) { Thing *t = thing_by_uid(uid); return t ? t->quantity : 0; }

/* The thing of that kind on exactly that cell (not gone), or 0. */
int world_thing_at_cell(int category, int itemId, int x, int y, int except)
{
    for (int i = thingCount - 1; i >= 0; i--)
    {
        const Thing *t = &things[i];
        if (!t->gone && t->uid != except && t->category == category && (itemId < 0 || t->itemId == itemId) && t->x == x && t->y == y) return t->uid;
    }
    return 0;
}

/* Let go of by hand (drag.cc drop_on_map): its picture's top-left on the screen - the cell under its middle, and the 8-pixel offset that keeps it exactly where it was shown. */
void world_thing_drop_at(int uid, int sx, int sy, int onFloor)
{
    Thing *t = thing_by_uid(uid);
    if (!t) return;
    const Picture *p = sprite_picture_at(look_of(t), 0);
    int qw = p ? (p->width + UNIT - 1) / UNIT * UNIT : tileW, qh = p ? (p->height + UNIT - 1) / UNIT * UNIT : tileH;
    double left = sx + cameraX, top = sy + cameraY;
    int cx = (int)floor((left + qw / 2.0) / tileW), cy = (int)floor((top + qh / 2.0) / tileH);
    cx = cx < 0 ? 0 : cx >= mapW ? mapW - 1 : cx; cy = cy < 0 ? 0 : cy >= mapH ? mapH - 1 : cy;
    double defaultLeft = cx * tileW + (tileW - qw) / 2.0, defaultTop = (cy + 1) * tileH - qh;
    world_thing_move(uid, cx, cy, onFloor);
    t = thing_by_uid(uid);
    t->offX = (int)floor((left - defaultLeft) / UNIT + 0.5); t->offY = (int)floor((top - defaultTop) / UNIT + 0.5);
    Body b;
    thing_box(t, &b);
    if (t->body >= 0) bodies[t->body].z = -100000;
    t->z = standing_lift(b.left, b.top, b.width, b.depth, onFloor);
    if (t->body >= 0) { if (bodies[t->body].width || t->solid) thing_box(t, &bodies[t->body]); }
    settle_all();
}

/* ---------------- Saving (GameSaveData.Map): the map as it is now ---------------- */

static void set_num(cJSON *o, const char *k, double v) { if (get(o, k)) cJSON_ReplaceItemInObject(o, k, cJSON_CreateNumber(v)); else cJSON_AddNumberToObject(o, k, v); }
static void set_bool(cJSON *o, const char *k, int v) { if (get(o, k)) cJSON_ReplaceItemInObject(o, k, cJSON_CreateBool(v)); else cJSON_AddBoolToObject(o, k, v); }
static void set_item(cJSON *o, const char *k, cJSON *v) { if (get(o, k)) cJSON_ReplaceItemInObject(o, k, v); else cJSON_AddItemToObject(o, k, v); }

/* A thing's entry in the save's map: its Map Editor entry with where it is and what state it's in now. extra: what it holds (items.c / dolls.c). */
static cJSON *thing_json(const Thing *t, int instanceId, cJSON *(*extra)(int uid, cJSON *entry))
{
    cJSON *e = t->placed ? cJSON_Duplicate(t->placed, 1) : cJSON_CreateObject();
    set_num(e, "InstanceId", instanceId);
    if (t->category == 4)
    {
        set_num(e, "ObjectId", t->itemId);
        set_num(e, "Quantity", t->quantity);
        set_num(e, "ContainerInstanceId", -1);
        set_bool(e, "StartsOpen", t->open);
        if (t->def && t->def->type == T_DOOR) set_bool(e, "IsLockedRuntime", t->locked);
        if (t->def && t->def->type == T_LIGHT) { set_bool(e, "IsLitRuntime", t->lit); set_bool(e, "StartsLit", t->lit); }
        set_bool(e, "IsSpentRuntime", t->spent);
        if (t->def && t->def->type == T_TRAP) set_bool(e, "TrapSprungRuntime", t->sprung);
        if (t->def && (t->def->type == T_WINDOW || t->def->type == T_LEVER || t->def->type == T_BEDROLL)) set_bool(e, "IsOpenRuntime", t->open);
        if (t->def && t->def->type == T_PLANT) { set_num(e, "StartStage", t->stage); set_num(e, "FrameRuntime", t->stage); set_num(e, "GrowMinutesRuntime", t->grow); }
        if (t->def && t->def->type == T_LIQUID) { set_num(e, "LiquidIdRuntime", t->liquid); set_num(e, "StartsFullOfId", t->liquid); }
        if (t->def && t->def->type == T_KEYCHAIN) set_num(e, "FrameRuntime", t->fixedFrame > 0 ? t->fixedFrame - 1 : 0);
        set_num(e, "BurnLeftRuntime", t->burnLeft);
        set_num(e, "HitPointsRuntime", t->hpSet ? t->hp : -1);
    }
    else { set_num(e, "Category", t->category); set_num(e, "ItemId", t->itemId); }
    set_num(e, "X", t->x); set_num(e, "Y", t->y); set_num(e, "Z", t->z);
    set_num(e, "OffsetX", t->offX); set_num(e, "OffsetY", t->offY); set_num(e, "Facing", t->facing);
    set_bool(e, "HangsOnWall", t->hangs); set_bool(e, "StaysPut", t->staysPut); set_bool(e, "LiesUnder", t->liesUnder);
    if (t->owner) set_num(e, "BelongsToId", t->owner - 1);
    if (extra) extra(t->uid, e);
    return e;
}

/* The map now: its tiles (Change Tile), every thing where it is (picked up: gone; put down: there), every character where it stands
   (the dead lie in their bodies), and the main character's spot as the Start Position. runtime: the exact spot, facing, lift. */
void world_save(cJSON *map, cJSON *runtime, cJSON *(*extra)(int uid, cJSON *entry))
{
    /* tiles */
    int first = 1;
    cJSON *layer;
    cJSON_ArrayForEach(layer, get(map, "Layers"))
    {
        int f = first ? 1 : num(layer, "Floor", 1);
        first = 0;
        if (f < 1 || f > MAX_FLOORS || !floors[f]) continue;
        cJSON *tiles = cJSON_CreateArray();
        for (int i = 0; i < mapW * mapH; i++) cJSON_AddItemToArray(tiles, cJSON_CreateNumber(floors[f][i]));
        set_item(layer, "TileIndices", tiles);
    }
    /* things */
    int top = 0;
    for (int i = 0; i < thingCount; i++) if (things[i].instanceId > top) top = things[i].instanceId;
    cJSON *objects = cJSON_CreateArray(), *items = cJSON_CreateArray(), *o;
    cJSON_ArrayForEach(o, get(map, "Objects")) if (num(o, "ContainerInstanceId", -1) >= 0) cJSON_AddItemToArray(objects, cJSON_Duplicate(o, 1)); /* inside a container: as it was */
    for (int i = 0; i < thingCount; i++)
    {
        const Thing *t = &things[i];
        if (t->spawned) continue;
        if (t->gone) continue;
        int id = t->instanceId > 0 ? t->instanceId : ++top;
        cJSON_AddItemToArray(t->category == 4 ? objects : items, thing_json(t, id, extra));
    }
    set_item(map, "Objects", objects);
    set_item(map, "Items", items);
    /* characters */
    cJSON *placed = cJSON_CreateArray();
    int placementId = 1;
    for (int i = 0; i < npcCount; i++)
    {
        const Actor *a = &npcs[i];
        if (a->gone) continue;
        cJSON *p = cJSON_CreateObject();
        cJSON_AddNumberToObject(p, "Id", placementId++);
        cJSON_AddNumberToObject(p, "CharacterId", a->characterId);
        cJSON_AddNumberToObject(p, "X", (int)floor(a->px / tileW)); cJSON_AddNumberToObject(p, "Y", (int)floor(a->py / tileH));
        cJSON_AddNumberToObject(p, "Floor", floor_of(a->lift < 0 ? 0 : a->lift));
        cJSON_AddNumberToObject(p, "Facing", a->facing);
        cJSON_AddItemToArray(placed, p);
    }
    set_item(map, "Characters", placed);
    /* the main character */
    set_num(map, "StartX", (int)floor(player.px / tileW)); set_num(map, "StartY", (int)floor(player.py / tileH));
    set_num(map, "StartFloor", floor_of(player.lift < 0 ? 0 : player.lift));
    cJSON_AddNumberToObject(runtime, "PlayerPixelX", player.px); cJSON_AddNumberToObject(runtime, "PlayerPixelY", player.py);
    cJSON_AddNumberToObject(runtime, "PlayerLift", player.lift); cJSON_AddNumberToObject(runtime, "PlayerFacing", player.facing);
}

/* A save's exact spot for the main character (the Start Position only knows the cell). */
void world_restore_player(cJSON *runtime)
{
    if (!cJSON_IsNumber(get(runtime, "PlayerPixelX"))) return;
    player.px = get(runtime, "PlayerPixelX")->valuedouble; player.py = get(runtime, "PlayerPixelY")->valuedouble;
    player.lift = num(runtime, "PlayerLift", player.lift); player.facing = num(runtime, "PlayerFacing", DIR_S) & 7;
    cameraX = cameraY = 0; camera_update(); /* a loaded game: a fresh window in the Windows engine, its camera from the corner */
}

cJSON *world_thing_placed(int uid) { Thing *t = thing_by_uid(uid); return t ? t->placed : NULL; }
int world_thing_count(void) { return thingCount; }
int world_thing_uid_at(int i) { return i >= 0 && i < thingCount && !things[i].gone ? things[i].uid : 0; }

const char *world_actor_name(int actor)
{
    cJSON *c;
    int id = world_actor_character(actor);
    cJSON_ArrayForEach(c, get(get(get(gameJson, "Project"), "Characters"), "Characters")) if (num(c, "Id", -2) == id) return str(c, "Name");
    return "Someone";
}

/* A new game (or a save loaded, as a new game is built from the project): everything of the last one forgotten. */
void world_reset(void)
{
    for (int f = 0; f <= MAX_FLOORS; f++) { free(floors[f]); floors[f] = NULL; }
    free(walkable); free(water); free(underTile); water = NULL; walkable = NULL; underTile = NULL;
    puddleCount = 0; bleedingCount = 0;
    free(defs); defs = NULL; defCount = 0;
    free(things); things = NULL; thingCount = 0;
    free(bodies); bodies = NULL; bodyCount = 0;
    memset(&player, 0, sizeof player);
    free(npcs); npcs = NULL; npcCount = 0;
    nextUid = 1; thingCap = bodyCap = slabStart = slabEnd = 0;
    drawnCount = 0; seenCount = 0;
    free(walks); walks = NULL;
    memset(barks, 0, sizeof barks);
    free(seats); seats = NULL; free(heading); heading = NULL;
    memset(&pendingSit, 0, sizeof pendingSit);
    memset(thingBarks, 0, sizeof thingBarks);
    savedLookCount = 0;
    memset(effects, 0, sizeof effects);
    outlineActor = -1;
    cameraFollow = 0; cameraHeld = 0; keysHeld = 0;
    sheetCount = 0;
    gameJson = NULL;
}


/* ---------------- Characters added in play (Summon, Clone) and bodies ---------------- */

/* A character brought onto the map now: its own actor, standing at a cell of a floor. Its index (-1: couldn't). */
int world_add_actor(cJSON *character, int x, int y, int onFloor)
{
    if (!character || x < 0 || y < 0 || x >= mapW || y >= mapH) return -1;
    npcs = (Actor *)realloc(npcs, sizeof(Actor) * (size_t)(npcCount + 1));
    Actor *a = &npcs[npcCount];
    memset(a, 0, sizeof *a);
    read_actor_poses(character, get(get(gameJson, "Project"), "Poses"), a);
    a->facing = DIR_S;
    a->body = -1;
    if (bodyCount + 1 >= bodyCap) { bodyCap = bodyCap * 2 + 16; bodies = (Body *)realloc(bodies, sizeof(Body) * (size_t)bodyCap); }
    a->body = bodyCount;
    Body *b = &bodies[bodyCount++];
    memset(b, 0, sizeof *b); b->z = -100000;
    npcCount++;
    if (walks) { walks = (Walk *)realloc(walks, sizeof(Walk) * (size_t)(npcCount + 1)); memset(&walks[npcCount], 0, sizeof(Walk)); }
    if (seats)
    {
        seats = (Seat *)realloc(seats, sizeof(Seat) * (size_t)(npcCount + 2)); heading = (Seat *)realloc(heading, sizeof(Seat) * (size_t)(npcCount + 2));
        memset(&seats[npcCount], 0, sizeof(Seat) * 2); memset(&heading[npcCount], 0, sizeof(Seat) * 2);
    }
    a->gone = 1;
    world_actor_place(npcCount, x, y, onFloor);
    return npcCount;
}

/* The body a dead character lies in (0: none). */
int world_body_of(int characterId)
{
    for (int i = 0; i < thingCount; i++) if (!things[i].gone && things[i].category == 5 && things[i].itemId == -1000 - characterId) return things[i].uid;
    return 0;
}

/* ---------------- Stairs: a box a step (GameTestWindow.Mechanisms ObjectBodies) ---------------- */

static void stair_bodies(Thing *t, Body *out, int max, int *n)
{
    Body body;
    thing_box(t, &body);
    *n = 0;
    if (t->steps)
    {
        /* a designed staircase: every step its own piece, from the staircase's foot up to its top lift */
        int ax = t->x * unitsX + t->offX, ay = t->y * unitsY + t->offY;
        cJSON *step;
        cJSON_ArrayForEach(step, t->steps)
        {
            if (*n >= max) break;
            Body b = body;
            int top = num(step, "TopLift", 1);
            b.left = ax + num(step, "FootX", 0); b.top = ay + num(step, "FootY", 0);
            b.width = num(step, "SizeX", 1) > 1 ? num(step, "SizeX", 1) : 1; b.depth = num(step, "SizeY", 1) > 1 ? num(step, "SizeY", 1) : 1;
            if (flag(step, "IsLanding")) { b.z = body.z + (top - 1 > 0 ? top - 1 : 0); b.height = 1; } /* a landing: a floor slab, solid only in its top lift */
            else b.height = top > 1 ? top : 1;
            out[(*n)++] = b;
        }
        return;
    }
    if (t->def->stairsKind != 0 || body.height <= 1) return; /* a ladder, or too low: one box */
    int steps = body.height, alongY = t->def->risesToward == 0 || t->def->risesToward == 4, length = alongY ? body.depth : body.width;
    for (int i = 0; i < steps && *n < max; i++)
    {
        int from = length * i / steps, to = length * (i + 1) / steps;
        if (to <= from) continue; /* too short for its height: a step is missing */
        int start = t->def->risesToward == 0 ? body.top + body.depth - to : t->def->risesToward == 4 ? body.top + from
                  : t->def->risesToward == 6 ? body.left + body.width - to : body.left + from;
        Body b = body;
        if (alongY) { b.top = start; b.depth = to - from; } else { b.left = start; b.width = to - from; }
        b.height = i + 1;
        out[(*n)++] = b;
    }
}

/* ---------------- The state of things that work (GameTestWindow.Types) ---------------- */

int world_thing_open(int uid) { Thing *t = thing_by_uid(uid); return t && t->open; }
void world_thing_set_open(int uid, int open) { Thing *t = thing_by_uid(uid); if (t) t->open = open; }
int world_thing_lit(int uid) { Thing *t = thing_by_uid(uid); return t && t->lit; }
void world_thing_set_lit(int uid, int lit) { Thing *t = thing_by_uid(uid); if (t) t->lit = lit; }
void world_thing_set_spent(int uid, int spent) { Thing *t = thing_by_uid(uid); if (t) t->spent = spent; }
int world_thing_stage(int uid) { Thing *t = thing_by_uid(uid); return t ? t->stage : 0; }
int world_thing_stage_count(int uid) { Thing *t = thing_by_uid(uid); return t && t->def ? t->def->stageCount : 0; }
void world_thing_set_stage(int uid, int stage) { Thing *t = thing_by_uid(uid); if (t) { t->stage = stage; t->grow = 0; } }
double world_thing_grow(int uid, double minutes) { Thing *t = thing_by_uid(uid); if (!t) return 0; t->grow += minutes; return t->grow; }
void world_thing_set_grow(int uid, double minutes) { Thing *t = thing_by_uid(uid); if (t) t->grow = minutes; }
int world_thing_liquid(int uid) { Thing *t = thing_by_uid(uid); return t ? t->liquid : -1; }
void world_thing_set_liquid(int uid, int liquid) { Thing *t = thing_by_uid(uid); if (t) t->liquid = liquid; }
void world_thing_set_frame(int uid, int frame) { Thing *t = thing_by_uid(uid); if (t) t->fixedFrame = frame >= 0 ? frame + 1 : 0; }
cJSON *world_thing_entry(int uid) { Thing *t = thing_by_uid(uid); return t ? t->placed : NULL; }
int world_thing_by_instance(int instanceId)
{
    for (int i = 0; i < thingCount; i++) if (!things[i].gone && things[i].category == 4 && things[i].instanceId == instanceId) return things[i].uid;
    return 0;
}

/* Every thing of a type on the map now (uids). */
int world_things_of_type(int type, int *out, int max)
{
    int n = 0;
    for (int i = 0; i < thingCount && n < max; i++) if (!things[i].gone && things[i].def && things[i].def->type == type) out[n++] = things[i].uid;
    return n;
}

/* A ladder's destination: there, on the floor at (or just under) its lift. */
void world_actor_place_lift(int actor, int x, int y, int lift)
{
    Actor *a = actor_of(actor);
    if (!a || x < 0 || y < 0 || x >= mapW || y >= mapH) return;
    world_actor_place(actor, x, y, floor_of(lift < 0 ? 0 : lift));
    int l, t;
    foot_at(a, a->px, a->py, &l, &t);
    const Body *here[512];
    int n = 0;
    for (int i = 0; i < bodyCount && n < 512; i++) if (overlaps(&bodies[i], l, t, a->sizeX, a->sizeY) && (actor == 0 || i != a->body)) here[n++] = &bodies[i];
    a->lift = floor_below(here, n, lift + 1);
    if (a->body >= 0) bodies[a->body].z = a->lift;
    if (actor == 0) world_update(0, 0, 0); /* the camera follows by its dead zone (GameTestWindow.UpdateCamera), not centred */
}

/* The player's spot in 8-pixel units (a sextant's reading). */
void world_player_units(int *ux, int *uy) { *ux = (int)floor(player.px / UNIT); *uy = (int)floor(player.py / UNIT); }
void world_player_pixel(double *px, double *py) { *px = player.px; *py = player.py; }

/* BecomeObject: a placed object becomes another where it is, starting as that one starts (a plant at its first stage). */
void world_thing_become(int uid, int objectId)
{
    Thing *t = thing_by_uid(uid);
    const ObjectDef *d = def_of(objectId);
    if (!t || !d || t->category != 4) return;
    t->def = d; t->itemId = objectId;
    t->stage = 0; t->grow = 0; t->spent = 0; t->lookOverride = NULL; t->fixedFrame = 0;
    t->open = (d->type == T_DOOR && d->doorStartsOpen) || (d->type == T_WINDOW && d->winShutters && d->winStartsOpen) || (d->type == T_LEVER && d->leverKind != 2 && d->leverStartsOn);
    t->locked = d->type == T_DOOR && d->doorStartsLocked && !d->doorStartsOpen;
    t->lit = d->type == T_LIGHT && d->lightStartsLit;
    t->liquid = d->type == T_LIQUID ? d->liquidStart : -1;
    t->hpSet = 0;
    t->sizeX = d->sizeX > 0 ? d->sizeX : 1; t->sizeY = d->sizeY > 0 ? d->sizeY : 1;
    t->sizeZ = d->type == T_TRAP || (d->type == T_TILE && !d->solid) ? 0 : d->sizeZ;
    t->solid = d->solid; t->solidOffX = d->solidOffX; t->solidOffY = d->solidOffY;
    if (t->body >= 0) { if (t->solid) thing_box(t, &bodies[t->body]); else { bodies[t->body].width = bodies[t->body].depth = 0; bodies[t->body].z = -100000; } }
    else if (t->solid && !t->hangs && !t->staysPut) { ensure_bodies(1); t->body = bodyCount; t->bodyN = 1; thing_box(t, &bodies[bodyCount++]); }
    settle_all();
}

/* ---------------- Vehicles' needs (Barge_object: gather, step, turn, lift) ---------------- */

/* The ground cell: a tile there, walkable, water. */
void world_ground(int cx, int cy, int *tile, int *walk, int *wet)
{
    *tile = 0; *walk = 0; *wet = 0;
    if (cx < 0 || cy < 0 || cx >= mapW || cy >= mapH) return;
    *tile = floors[1] && floors[1][cy * mapW + cx] >= 0;
    *walk = walkable[cy * mapW + cx];
    *wet = water && water[cy * mapW + cx];
}

int world_units_x(void) { return unitsX; }
int world_units_y(void) { return unitsY; }
int world_thing_vehicle(int uid) { Thing *t = thing_by_uid(uid); return t && !t->gone ? t->vehicle : 0; }

/* Its box in 8px units and lifts (its footprint as placed, whether solid or not). */
void world_thing_box(int uid, int *l, int *tp, int *w, int *d, int *z, int *h)
{
    Thing *t = thing_by_uid(uid);
    Body b = { 0, 0, 0, 0, 0, 0 };
    if (t) thing_box(t, &b);
    *l = b.left; *tp = b.top; *w = b.width; *d = b.depth; *z = b.z; *h = b.height;
}

int world_thing_solid(int uid) { Thing *t = thing_by_uid(uid); return t && t->body >= 0 && bodies[t->body].width > 0; }
int world_thing_kind(int uid) { Thing *t = thing_by_uid(uid); return t && t->def ? t->def->type : -1; }

/* Every thing on the map now whose box overlaps this area (uids). */
int world_things_in(int l, int tp, int w, int d, int *out, int max)
{
    int n = 0;
    for (int i = 0; i < thingCount && n < max; i++)
    {
        Thing *t = &things[i];
        if (t->gone) continue;
        Body b;
        thing_box(t, &b);
        if (overlaps(&b, l, tp, w, d) || (t->bodyN > 1 && t->body >= 0 && overlaps(&bodies[t->body], l, tp, w, d))) out[n++] = t->uid;
    }
    return n;
}

/* A character's footprint (8px units) and lift and height. */
void world_actor_foot(int actor, int *l, int *tp, int *w, int *d, int *lift, int *h)
{
    Actor *a = actor_of(actor);
    *l = *tp = *w = *d = *lift = *h = 0;
    if (!a || a->gone) return;
    foot_at(a, a->px, a->py, l, tp);
    *w = a->sizeX; *d = a->sizeY; *lift = a->lift; *h = a->sizeZ;
}

/* Something solid in this unit area between these lifts that isn't one of the given things or characters: its name (NULL: clear). */
const char *world_blocked(int l, int tp, int w, int d, int zFrom, int zTo, const int *uids, int nu, const int *actors, int na)
{
    static char name[96];
    for (int i = 0; i < thingCount; i++)
    {
        Thing *t = &things[i];
        if (t->gone || t->body < 0) continue;
        int mine = 0;
        for (int k = 0; k < nu && !mine; k++) mine = uids[k] == t->uid;
        if (mine) continue;
        for (int k = 0; k < (t->bodyN > 0 ? t->bodyN : 1); k++)
        {
            const Body *b = &bodies[t->body + k];
            if (b->width <= 0 || !overlaps(b, l, tp, w, d)) continue;
            if (b->z + b->height <= zFrom || b->z >= zTo) continue; /* under its floor, or over its top (a bridge) */
            snprintf(name, sizeof name, "%s", items_name(t->category, t->itemId));
            return name;
        }
    }
    for (int a = 0; a <= npcCount; a++)
    {
        int mine = 0;
        for (int k = 0; k < na && !mine; k++) mine = actors[k] == a;
        if (mine) continue;
        int al, at, aw, ad, lift, h;
        world_actor_foot(a, &al, &at, &aw, &ad, &lift, &h);
        if (!aw || al >= l + w || l >= al + aw || at >= tp + d || tp >= at + ad) continue;
        if (lift + h <= zFrom || lift >= zTo) continue;
        if (a == 0) return "you";
        snprintf(name, sizeof name, "%s", world_actor_name(a));
        return name;
    }
    return NULL;
}

static void refresh_bodies(Thing *t)
{
    if (t->body < 0) return;
    if (t->bodyN > 1)
    {
        Body steps[64];
        int n = 0;
        stair_bodies(t, steps, 64, &n);
        for (int k = 0; k < n && k < t->bodyN; k++) bodies[t->body + k] = steps[k];
        return;
    }
    if (bodies[t->body].width > 0 || t->solid) thing_box(t, &bodies[t->body]);
}

/* Moved by whole 8px units (its cell and the offset within it) and lifts. */
void world_shift_thing(int uid, int dx, int dy, int dz)
{
    Thing *t = thing_by_uid(uid);
    if (!t) return;
    int ux = t->x * unitsX + t->offX + dx, uy = t->y * unitsY + t->offY + dy;
    t->x = (int)floor(ux / (double)unitsX); t->y = (int)floor(uy / (double)unitsY);
    t->offX = ux - t->x * unitsX; t->offY = uy - t->y * unitsY;
    t->z += dz;
    t->homeFloor = floor_of(t->z < 0 ? 0 : t->z);
    refresh_bodies(t);
}

/* Turned a quarter (or more) about a point: facing on, its width and depth swapped if it has turned looks, its footprint's middle put at (cx, cy) units. */
void world_turn_thing(int uid, int quarters, double cx, double cy)
{
    Thing *t = thing_by_uid(uid);
    if (!t) return;
    if (t->def)
    {
        const ObjectDef *d = t->def;
        t->facing = (t->facing + quarters * 2) & 7;
        int turned = (d->turned[0] || d->turned[2] || d->turned[4] || d->turned[6]) && (t->facing == DIR_E || t->facing == DIR_W);
        t->sizeX = turned ? d->sizeY : d->sizeX; t->sizeY = turned ? d->sizeX : d->sizeY;
        if (t->sizeX < 1) t->sizeX = 1;
        if (t->sizeY < 1) t->sizeY = 1;
        t->solidOffX = turned ? d->solidOffY : d->solidOffX; t->solidOffY = turned ? d->solidOffX : d->solidOffY;
    }
    int left = (int)floor(cx - t->sizeX / 2.0 + 0.5), top = (int)floor(cy - t->sizeY / 2.0 + 0.5);
    int ux = left - (int)floor((unitsX - t->sizeX) / 2.0) - t->solidOffX, uy = top + t->sizeY - t->solidOffY - unitsY; /* FootprintAt the other way round */
    t->x = (int)floor(ux / (double)unitsX); t->y = (int)floor(uy / (double)unitsY);
    t->offX = ux - t->x * unitsX; t->offY = uy - t->y * unitsY;
    refresh_bodies(t);
}

/* A character moved with what it stands on (pixels and lifts): where it would get up to goes along. */
void world_shift_actor(int actor, double dx, double dy, int dz)
{
    Actor *a = actor_of(actor);
    if (!a || a->gone) return;
    if (actor > 0) world_walk_cancel(actor);
    a->px += dx; a->py += dy; a->lift += dz;
    seats_ready();
    if (seats[actor].uid) { seats[actor].fromX += dx; seats[actor].fromY += dy; seats[actor].fromLift += dz; }
    if (a->body >= 0 && !seats[actor].uid)
    {
        int l, t;
        foot_at(a, a->px, a->py, &l, &t);
        Body *b = &bodies[a->body]; b->left = l; b->top = t; b->z = a->lift;
    }
    if (actor == 0) world_update(0, 0, 0); /* the camera follows by its dead zone (GameTestWindow.UpdateCamera), not centred */
}

void world_actor_set_facing(int actor, int facing) { Actor *a = actor_of(actor); if (a) a->facing = facing & 7; }
int world_actor_facing_of(int actor) { Actor *a = actor_of(actor); return a ? a->facing : 0; }

static int index_of_actor(const Actor *a) { return a == &player ? 0 : (int)(a - npcs) + 1; }

/* Exult's is_roof for a character: anything solid over its head (inside a house). */
static int actor_under_roof(const Actor *a)
{
    int l, t;
    foot_at(a, a->px, a->py, &l, &t);
    for (int i = 0; i < bodyCount; i++)
        if (overlaps(&bodies[i], l, t, a->sizeX, a->sizeY) && bodies[i].z >= a->lift + 4) return 1;
    return 0;
}

void world_set_key_check(int (*check)(int actor, int doorUid)) { holdsKey = check; }

/* Going to sleep: its own doors near it closed (if they can be) and locked (LockHouseDoorsForNight). */
void world_lock_own_doors(int actor, int cells)
{
    Actor *a = actor_of(actor);
    if (!a || !holdsKey) return;
    int ax = (int)floor(a->px / tileW), ay = (int)floor(a->py / tileH);
    for (int i = 0; i < thingCount; i++)
    {
        Thing *d = &things[i];
        if (d->gone || !d->def || d->def->type != T_DOOR || abs(d->x - ax) > cells || abs(d->y - ay) > cells || !holdsKey(actor, d->uid)) continue;
        if (d->open && !world_door_close(d->uid)) continue; /* someone in the doorway */
        if (!d->locked) { d->locked = 1; world_sound_at_thing(door_lock_sound(d), d->uid); }
    }
}

/* Whether a character is heading for a chair or bed it hasn't reached yet. */
int world_heading_to_seat(int actor) { seats_ready(); return actor >= 0 && actor <= npcCount && heading[actor].uid != 0; }

/* ---------------- Traps' state; a thing's facing; the camera ---------------- */

int world_trap_sprung(int uid) { Thing *t = thing_by_uid(uid); return t && t->sprung; }
void world_trap_set_sprung(int uid) { Thing *t = thing_by_uid(uid); if (t) t->sprung = 1; }
int world_thing_facing(int uid) { Thing *t = thing_by_uid(uid); return t ? t->facing : DIR_S; }
void world_thing_set_facing(int uid, int facing) { Thing *t = thing_by_uid(uid); if (t && facing >= 0 && facing < 8) t->facing = facing; }
void world_camera(double *x, double *y) { *x = floor(cameraX); *y = floor(cameraY); }

/* ---------------- Status outlines (Actor::paint: charmed, paralyzed, protected, cursed, poisoned) ---------------- */

static uint32_t status_colour(int characterId)
{
    if (vitals_dead(characterId) || vitals_has(characterId, 32)) return 0;
    if (vitals_has(characterId, 8)) return 0xFF78A0FCu;  /* charmed: light blue */
    if (vitals_has(characterId, 4)) return 0xFFC46CC4u;  /* paralyzed: purple */
    if (vitals_has(characterId, 64)) return 0xFFF8F8DCu; /* protected: pale white */
    if (vitals_has(characterId, 16)) return 0xFFF8F814u; /* cursed: yellow */
    if (vitals_has(characterId, 1)) return 0xFF10FC10u;  /* poisoned: green */
    return 0;
}

static void draw_status_outlines(uint32_t *pixels)
{
    for (int i = 0; i < drawnCount; i++)
    {
        const Drawn *d = &drawn[i];
        if (d->actor < 0) continue;
        if (d->actor == outlineActor && lastMs < outlineUntil) continue; /* the red hit outline instead, for its moment */
        Actor *a = actor_of(d->actor);
        uint32_t colour = a ? status_colour(a->characterId) : 0;
        if (!colour) continue;
        const Picture *p = d->picture;
        double s = fmin((double)d->w / p->width, (double)d->h / p->height);
        int dw = (int)(p->width * s + 0.5), dh = (int)(p->height * s + 0.5), ox = d->x + (d->w - dw) / 2, oy = d->y + (d->h - dh) / 2;
#define SOLID(x, y) ((x) >= 0 && (y) >= 0 && (x) < dw && (y) < dh && (p->pixels[(size_t)((y) * p->height / dh) * (size_t)p->width + (size_t)((x) * p->width / dw)] >> 24) > 127)
        for (int y = -1; y <= dh; y++)
            for (int x = -1; x <= dw; x++)
            {
                if (SOLID(x, y) || !(SOLID(x - 1, y) || SOLID(x + 1, y) || SOLID(x, y - 1) || SOLID(x, y + 1))) continue;
                int sx = ox + x, sy = oy + y;
                frame_dot(pixels, sx, sy, world_shade_colour(colour)); /* under the sky's light too */
            }
#undef SOLID
    }
}

/* ---------------- Bleeding (actors.cc reduce_health / bleed, npctime.cc Npc_bleed_timer) ---------------- */

static double puddleSweepMs;
static uint32_t bloodRng = 2718281828u;
static int blood_rnd(int n) { bloodRng ^= bloodRng << 13; bloodRng ^= bloodRng >> 17; bloodRng ^= bloodRng << 5; return n > 0 ? (int)(((uint64_t)bloodRng * (uint32_t)n) >> 32) : 0; }

static const Sprite *blood_of(int actor)
{
    Actor *a = actor_of(actor);
    if (!a) return NULL;
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(get(gameJson, "Project"), "Characters"), "Characters"))
        if (num(c, "Id", -2) == a->characterId)
        {
            const char *name = str(c, "BloodSpriteName");
            const Sprite *s = name[0] ? sprite_find("Blood", name) : NULL;
            return s && s->frameCount ? s : NULL;
        }
    return NULL;
}

static void feet_unit(Actor *a, int *ux, int *uy) { *ux = (int)floor(a->px / UNIT); *uy = (int)floor((a->py + tileH / 2.0 - 1) / UNIT); }

/* Actor::bleed: the puddle on this tile grows, else a new small one. */
static void bleed(int actor)
{
    Actor *a = actor_of(actor);
    const Sprite *blood = blood_of(actor);
    if (!a || !blood) return;
    int ux, uy, last = (blood->frameCount < 4 ? blood->frameCount : 4) - 1;
    feet_unit(a, &ux, &uy);
    for (int i = 0; i < puddleCount; i++)
        if (puddles[i].ux == ux && puddles[i].uy == uy && puddles[i].lift == a->lift)
        {
            if (puddles[i].frame < last) { puddles[i].frame++; puddles[i].mirrored = blood_rnd(2); }
            return;
        }
    if (puddleCount >= 128) return;
    puddles[puddleCount++] = (Puddle){ ux, uy, a->lift, blood_rnd(last + 1 < 2 ? last + 1 : 2), blood_rnd(2), blood }; /* "Lets start with a smaller pool." */
}

/* reduce_health: a blow of this much may start the character bleeding (rand() % 10 < damage, not with 2 puddles round it already). */
void world_maybe_bleed(int actor, int damage)
{
    Actor *a = actor_of(actor);
    if (!a || !blood_of(actor) || vitals_dead(a->characterId)) return;
    int ux, uy, nearby = 0;
    feet_unit(a, &ux, &uy);
    for (int i = 0; i < puddleCount; i++) nearby += abs(puddles[i].ux - ux) <= 1 && abs(puddles[i].uy - uy) <= 1;
    if (blood_rnd(10) >= damage || nearby >= 2) return;
    for (int i = 0; i < bleedingCount; i++) if (bleeding[i].actor == actor) return;
    if (bleedingCount >= 32) return;
    int hp = 0;
    vitals_hp(a->characterId, &hp);
    bleeding[bleedingCount++] = (Bleeding){ actor, hp, 60000 + blood_rnd(120000), 500 + blood_rnd(500) };
    bleed(actor); /* drips at once */
}

/* Npc_bleed_timer: a drip every 0.5-1 s for 1-3 minutes - healing or dying stops it; far-off puddles are let go. */
void world_update_bleeding(double ms)
{
    for (int i = 0; i < bleedingCount; i++)
    {
        Bleeding *b = &bleeding[i];
        Actor *a = actor_of(b->actor);
        int hp = 0;
        if (a) vitals_hp(a->characterId, &hp);
        b->left -= ms;
        if (!a || a->gone || vitals_dead(a->characterId) || hp > b->lastHp || b->left <= 0) { bleeding[i--] = bleeding[--bleedingCount]; continue; }
        b->lastHp = hp;
        if ((b->next -= ms) > 0) continue;
        b->next = 500 + blood_rnd(500);
        bleed(b->actor);
    }
    if ((puddleSweepMs -= ms) > 0) return;
    puddleSweepMs = 2000;
    double cx = cameraX + SCREEN_W / 2.0, cy = cameraY + SCREEN_H / 2.0;
    for (int i = 0; i < puddleCount; i++)
        if (fabs(puddles[i].ux * 8.0 - cx) > 2.5 * SCREEN_W || fabs(puddles[i].uy * 8.0 - cy) > 2.5 * SCREEN_H) puddles[i--] = puddles[--puddleCount];
}

/* ---------------- Characters in each other's way (actors.cc Actor::move_aside, objs.cc swap_positions) ---------------- */

/* Puts a character straight onto a cell (Game_object::move) - its body too. */
static void put_on_cell(int actor, int x, int y)
{
    Actor *a = actor_of(actor);
    Walk *w = walk_of(actor);
    if (!a) return;
    a->px = cell_cx(x); a->py = cell_cy(y);
    int l, t;
    foot_at(a, a->px, a->py, &l, &t);
    if (a->body >= 0) bodies[a->body].z = -1000;
    a->lift = standing_lift(l, t, a->sizeX, a->sizeY, floor_of(a->lift));
    if (a->body >= 0) { Body *b = &bodies[a->body]; b->left = l; b->top = t; b->z = a->lift; }
    if (actor != 0 && w) { w->active = 0; a->walkingNow = 0; } /* thinks again from there */
}

/* Whether the character could stand on that cell now (on its floor). */
static int free_cell(int actor, int x, int y)
{
    Actor *a = actor_of(actor);
    if (x < 0 || y < 0 || x >= mapW || y >= mapH) return 0;
    int own = lift_own_body(a), lift, ok = can_stand_at(a, x, y, a->lift, &lift) && abs(lift - a->lift) <= 1;
    put_own_body(a, own);
    return ok;
}

/* Actor::move_aside: the blocker steps to a free cell at right angles to the walker's way (one side, then the other), else a free diagonal;
   with nowhere to go they swap places, if the same size. Fighting, walking, sitting, lying or down: it stays an obstacle.
   The player moves aside too, like Exult's avatar, but only while standing still and not in combat mode. */
static int move_aside(int blocker, int walker, int dx, int dy)
{
    Actor *b = actor_of(blocker), *wa = actor_of(walker);
    if (!b || !wa || b->gone || vitals_dead(b->characterId) || vitals_has(b->characterId, 2 | 4) || world_seated(blocker) || b->movement == 3) return 0; /* sitting, or stationary: an obstacle */
    if (blocker == 0 ? (keysHeld || world_walking(0) || combat_mode()) : (world_walking(blocker) || combat_busy(blocker))) return 0;
    int bx = (int)floor(b->px / tileW), by = (int)floor(b->py / tileH);
    const int ax[6] = { -dy, dy, 1, 1, -1, -1 }, ay[6] = { dx, -dx, 1, -1, 1, -1 };
    for (int i = 0; i < 6; i++)
    {
        int x = bx + ax[i], y = by + ay[i];
        if (!free_cell(blocker, x, y)) continue;
        if (blocker == 0) { put_on_cell(0, x, y); return 1; }
        Walk *w = walk_of(blocker);
        w->x[0] = bx; w->y[0] = by; w->x[1] = x; w->y[1] = y; w->ox[0] = w->oy[0] = w->ox[1] = w->oy[1] = 0; /* steps there, then carries on with what it was doing */
        w->count = 2; w->at = 0; w->active = 1; w->blocked = 0;
        return 1;
    }
    if (blocker == 0)
        for (int k = 0; k < 2; k++) /* a narrow way with no room beside (a one-wide stair): on along it, ahead of the walker - or back behind it */
        {
            int x = bx + (k ? -dx : dx), y = by + (k ? -dy : dy);
            if ((x == (int)floor(wa->px / tileW) && y == (int)floor(wa->py / tileH)) || !free_cell(0, x, y)) continue;
            put_on_cell(0, x, y);
            return 1;
        }
    if (b->sizeX != wa->sizeX || b->sizeY != wa->sizeY) return 0;
    int wx = (int)floor(wa->px / tileW), wy = (int)floor(wa->py / tileH);
    if (wx == bx && wy == by) return 0;
    put_on_cell(blocker, wx, wy);
    put_on_cell(walker, bx, by);
    if (walker != 0) { Walk *w = walk_of(walker); if (w) { w->active = 0; wa->walkingNow = 0; } } /* swapped: thinks again from its new place */
    return 1;
}

/* Actor::is_really_blocked: the character whose body takes up the walker's next footprint is asked to move aside. */
static int ask_aside(int walker, int l, int t, int dx, int dy)
{
    Actor *wa = actor_of(walker);
    if (!wa || (!dx && !dy)) return 0;
    Body next = { l, t, wa->sizeX, wa->sizeY, 0, 0 };
    for (int k = 0; k <= npcCount; k++)
    {
        Actor *o = actor_of(k);
        if (k == walker || !o || o->gone) continue;
        int ol, ot;
        foot_at(o, o->px, o->py, &ol, &ot);
        if (!overlaps(&next, ol, ot, o->sizeX, o->sizeY) || !(o->lift < wa->lift + wa->sizeZ && wa->lift < o->lift + o->sizeZ)) continue;
        return move_aside(k, walker, dx, dy);
    }
    return 0;
}
