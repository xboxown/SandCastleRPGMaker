/*
 * Tiles, traps, fires and flying projectiles, as the Windows engine has them
 * (GameTestWindow.ObjectsTick / Projectiles):
 *  - Who stands on each tile, trap or burning fire (the player and everyone
 *    else): stepping onto a tile fires its By Step On, standing on it By
 *    Stand On once a second, stepping off By Step Off.
 *  - Traps: Ground (hurts, maybe an ailment), Explosive (a blast at once),
 *    Shooting (stepped on, or someone in its line of fire: its projectile
 *    the way the placed copy faces). One-shot traps are then spent; others
 *    re-arm after a while; a hidden trap shows itself once sprung.
 *  - A lit fire (Light: Burns when lit) burns whoever is in it, again every
 *    second they stay.
 *  - Projectiles fly across the map: they hurt the first character they meet
 *    (and may explode), stop at anything solid, end at their range.
 *  - Something put down on the map: By Item Dropped On It on the tiles under
 *    it, on what it rests on, and on the map event of its cell.
 */
#include "traps.h"
#include "world.h"
#include "items.h"
#include "events.h"
#include "vitals.h"
#include "combat.h"
#include "sound.h"
#include "pictures.h"
#include "../third_party/cJSON.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

void game_log(const char *line);

enum { OT_TILE = 1, OT_LIGHT = 4, OT_TRAP = 9, OT_EXPLOSIVE = 29, OT_PROJECTILE = 34 };
enum { TK_GROUND, TK_SHOOTING, TK_EXPLOSIVE };
enum { TR_DROP = 3, TR_USE = 2, TR_ENTER = 9, TR_STAY = 10, TR_EXIT = 11 };
enum { A_POISONED = 1, A_ASLEEP = 2, A_PARALYZED = 4 };

static uint32_t rng = 3141592653u;
static int rnd(int n) { rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5; return n > 0 ? (int)(((uint64_t)rng * (uint32_t)n) >> 32) : 0; }

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static double dbl(cJSON *o, const char *k, double fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valuedouble : fallback; }
static int flag(cJSON *o, const char *k) { return cJSON_IsTrue(get(o, k)); }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }
static cJSON *object_def(int id) { cJSON *o; cJSON_ArrayForEach(o, get(get(get((cJSON *)world_json(), "Project"), "Objects"), "Objects")) if (num(o, "Id", -2) == id) return o; return NULL; }
static const Sprite *link_sprite(cJSON *link) { return flag(link, "IsSet") ? sprite_find(str(link, "SpriteGroup"), str(link, "SpriteName")) : NULL; }

static int roll(int a, int b) { int lo = a < b ? a : b, hi = a < b ? b : a; return hi <= 0 ? 0 : lo + rnd(hi - lo + 1); }

static void lower_name(cJSON *def, char *out, size_t size)
{
    snprintf(out, size, "a %s", str(def, "Name"));
    for (char *c = out + 2; *c; c++) if (*c >= 'A' && *c <= 'Z') *c = (char)(*c + 32);
}

static void ailment_of(int kind, int characterId)
{
    int a = kind == 1 ? A_POISONED : kind == 2 ? A_ASLEEP : kind == 3 ? A_PARALYZED : 0;
    if (a && !vitals_dead(characterId)) vitals_inflict(characterId, a);
}

/* ---------------- Who stands on what ---------------- */

typedef struct On { int uids[8], count; } On;
static On on[256];
static double stayMs;
typedef struct Rearm { int uid; double ms; } Rearm;
static Rearm rearms[64]; static int rearmCount;
static double burnAgain[256]; /* per actor: till a fire burns them again */

static int rearming(int uid) { for (int i = 0; i < rearmCount; i++) if (rearms[i].uid == uid) return 1; return 0; }
static void rearm(int uid, double ms) { if (rearmCount < 64 && ms > 0) rearms[rearmCount++] = (Rearm){ uid, ms }; }

static int floor_kind(int uid)
{
    int type = world_thing_type(uid);
    if (type == OT_TILE && !world_thing_solid(uid)) return 1;
    if (type == OT_TRAP) return 2;
    if (type == OT_LIGHT && flag(get(object_def(world_thing_object_id(uid)), "Light"), "BurnsWhenLit")) return 3;
    return 0;
}

static void blast(double cx, double cy, int radius, int dmin, int dmax, const char *what, int source);

/* SpringTrap: damage (and an ailment), its sound, revealed if hidden; one-shot traps are then spent. */
static void spring(int trap, cJSON *def, int victim)
{
    cJSON *t = get(def, "Trap");
    if ((flag(t, "OneShot") && world_trap_sprung(trap)) || rearming(trap)) return;
    world_trap_set_sprung(trap);
    if (!flag(t, "OneShot") && dbl(t, "RearmSeconds", 3) > 0) rearm(trap, dbl(t, "RearmSeconds", 3) * 1000);
    sound_play(num(t, "TriggerSoundResourceId", -1));
    char line[160], what[96];
    if (victim == 0) { snprintf(line, sizeof line, "You set off \"%s\"!", str(def, "Name")); game_log(line); }
    events_object_event(trap, TR_ENTER, victim > 0 ? victim : -1);
    int damage = roll(num(t, "DamageMin", 1), num(t, "DamageMax", 5));
    lower_name(def, what, sizeof what);
    if (damage > 0) vitals_damage(world_actor_character(victim), damage, what);
    ailment_of(num(t, "Ailment", 0), world_actor_character(victim));
}

/* ---------------- Projectiles ---------------- */

typedef struct Flying { int used, objectId, shooter, shooterUid, lift; double x, y, vx, vy, travelled; } Flying;
static Flying flying[32];

/* A projectile sets off from a point (map pixels) one way. shooter: a character (or -1), shooterUid: a trap (or 0). */
static void launch(int objectId, double x, double y, double dx, double dy, int lift, int shooter, int shooterUid)
{
    cJSON *def = object_def(objectId), *p = get(def, "Projectile");
    Flying *f = NULL;
    for (int i = 0; i < 32 && !f; i++) if (!flying[i].used) f = &flying[i];
    if (!f || !def) return;
    if (fabs(dx) < 0.001 && fabs(dy) < 0.001) dy = 1;
    double length = sqrt(dx * dx + dy * dy), speed = (dbl(p, "SpeedTiles", 24) > 1 ? dbl(p, "SpeedTiles", 24) : 1) * 8;
    *f = (Flying){ 1, objectId, shooter, shooterUid, lift, x, y, dx / length * speed, dy / length * speed, 0 };
    sound_play(num(p, "LaunchSoundResourceId", -1));
}

static void end_flight(Flying *f, int victim)
{
    cJSON *def = object_def(f->objectId), *p = get(def, "Projectile");
    char what[96];
    lower_name(def, what, sizeof what);
    f->used = 0;
    if (victim >= 0)
    {
        int damage = roll(num(p, "DamageMin", 2), num(p, "DamageMax", 8));
        sound_play(num(p, "HitSoundResourceId", -1));
        if (damage > 0) vitals_damage(world_actor_character(victim), damage, what);
        ailment_of(num(p, "Ailment", 0), world_actor_character(victim));
    }
    if (flag(p, "Explodes") && (victim >= 0 || flag(p, "ExplodesAtEnd")))
    {
        const Sprite *boom = link_sprite(get(p, "ExplosionSprite"));
        if (boom) world_effect_once(boom, (int)floor(f->x / world_tile_w()), (int)floor(f->y / world_tile_h()));
        sound_play(num(p, "ExplosionSoundResourceId", -1));
        blast(f->x / 8, f->y / 8, num(p, "RadiusTiles", 2), num(p, "DamageMin", 2), num(p, "DamageMax", 8), what, 0);
    }
}

/* Whether it met someone (hurt) or something solid (stopped). */
static int check_hit(Flying *f)
{
    cJSON *def = object_def(f->objectId);
    int size = num(def, "SizeX", 1) > 1 ? num(def, "SizeX", 1) : 1;
    int l = (int)floor(f->x / 8) - size / 2, t = (int)floor(f->y / 8) - size / 2, d = num(def, "SizeY", 1) > 1 ? num(def, "SizeY", 1) : 1;
    for (int a = 0; a < world_actor_count(); a++)
    {
        if (a == f->shooter || world_actor_gone(a) || vitals_dead(world_actor_character(a))) continue;
        int al, at, aw, ad, lift, h;
        world_actor_foot(a, &al, &at, &aw, &ad, &lift, &h);
        if (f->lift < lift - 1 || f->lift > lift + 5) continue;
        if (al >= l + size || l >= al + aw || at >= t + d || t >= at + ad) continue;
        end_flight(f, a);
        return 1;
    }
    int shooterUid = f->shooterUid, none = -1;
    if (world_blocked(l, t, size, d, f->lift, f->lift + 1, &shooterUid, shooterUid ? 1 : 0, &none, 0)) { end_flight(f, -1); return 1; }
    return 0;
}

static void update_flying(double ms)
{
    for (int i = 0; i < 32; i++)
    {
        Flying *f = &flying[i];
        if (!f->used) continue;
        double seconds = ms / 1000, step = sqrt(f->vx * f->vx + f->vy * f->vy) * seconds;
        int parts = (int)ceil(step / 4.0);
        if (parts < 1) parts = 1;
        cJSON *p = get(object_def(f->objectId), "Projectile");
        int range = num(p, "RangeTiles", 24) > 1 ? num(p, "RangeTiles", 24) : 1;
        for (int k = 0; k < parts && f->used; k++)
        {
            f->x += f->vx * seconds / parts; f->y += f->vy * seconds / parts; f->travelled += step / parts;
            if (world_wraps()) { double w = world_map_w() * world_tile_w(), h = world_map_h() * world_tile_h(); f->x = f->x < 0 ? f->x + w : f->x >= w ? f->x - w : f->x; f->y = f->y < 0 ? f->y + h : f->y >= h ? f->y - h : f->y; }
            if (check_hit(f)) break;
            if (f->travelled / 8 >= range || f->x < 0 || f->y < 0 || f->x >= world_map_w() * world_tile_w() || f->y >= world_map_h() * world_tile_h()) { end_flight(f, -1); break; }
        }
    }
}

void traps_draw(uint32_t *pixels, uint32_t ms)
{
    double cx, cy;
    world_camera(&cx, &cy);
    for (int i = 0; i < 32; i++)
    {
        Flying *f = &flying[i];
        if (!f->used) continue;
        cJSON *def = object_def(f->objectId);
        const Picture *pic = sprite_picture_at(sprite_find(str(def, "SpriteGroup"), str(def, "SpriteName")), ms);
        if (!pic) continue;
        world_blit(pixels, pic, (int)(f->x - pic->width / 2.0 - cx) - f->lift * 4, (int)(f->y - pic->height / 2.0 - cy) - f->lift * 4, pic->width, pic->height);
    }
}

/* ---------------- Blasts ---------------- */

/* Everyone and every breakable thing within the radius (8px tiles) of a point (8px units) is hurt; explosives catch. */
static void blast(double cx, double cy, int radius, int dmin, int dmax, const char *what, int source)
{
    if (radius < 1) radius = 1;
    for (int a = 0; a < world_actor_count(); a++)
    {
        if (world_actor_gone(a) || vitals_dead(world_actor_character(a))) continue;
        double px, py;
        world_actor_pixel(a, &px, &py);
        if (fmax(fabs(px / 8 - cx), fabs(py / 8 - cy)) <= radius) vitals_damage(world_actor_character(a), roll(dmin, dmax), what);
    }
    int near[512], n = world_things_in((int)cx - radius - 2, (int)cy - radius - 2, 2 * radius + 4, 2 * radius + 4, near, 512);
    for (int i = 0; i < n; i++)
    {
        if (near[i] == source || !world_thing_there(near[i]) || world_thing_object_id(near[i]) < 0) continue;
        int l, t, w, d, z, h;
        world_thing_box(near[i], &l, &t, &w, &d, &z, &h);
        if (fmax(fabs(l + w / 2.0 - cx), fabs(t + d / 2.0 - cy)) > radius) continue;
        if (world_thing_type(near[i]) == OT_EXPLOSIVE) { combat_force_fuse(near[i]); continue; }
        combat_strike_object(near[i], roll(dmin, dmax));
    }
}

/* ---------------- Traps going off ---------------- */

static void fire_trap(int trap, cJSON *def, int victim)
{
    cJSON *t = get(def, "Trap"), *projectile = object_def(num(t, "ProjectileObjectId", -1));
    char line[200];
    if (rearming(trap) || (flag(t, "OneShot") && world_trap_sprung(trap))) return;
    if (num(projectile, "Type", -1) != OT_PROJECTILE) { snprintf(line, sizeof line, "\"%s\" has no projectile to shoot.", str(def, "Name")); game_log(line); return; }
    world_trap_set_sprung(trap);
    if (!flag(t, "OneShot")) rearm(trap, (dbl(t, "RearmSeconds", 3) > 0.2 ? dbl(t, "RearmSeconds", 3) : 0.2) * 1000);
    sound_play(num(t, "TriggerSoundResourceId", -1));
    events_object_event(trap, TR_ENTER, victim > 0 ? victim : -1);
    int l, tp, w, d, z, h;
    world_thing_box(trap, &l, &tp, &w, &d, &z, &h);
    static const int DX[8] = { 0, 1, 1, 1, 0, -1, -1, -1 }, DY[8] = { -1, -1, 0, 1, 1, 1, 0, -1 };
    int f = world_thing_facing(trap) & 7;
    launch(num(projectile, "Id", -1), (l + w / 2.0) * 8, (tp + d / 2.0) * 8, DX[f], DY[f], z + 1, -1, trap);
}

static void trigger(int trap, cJSON *def, int victim)
{
    cJSON *t = get(def, "Trap");
    char line[160], what[96];
    switch (num(t, "Kind", TK_GROUND))
    {
        case TK_EXPLOSIVE:
        {
            if (rearming(trap) || (flag(t, "OneShot") && world_trap_sprung(trap))) return;
            world_trap_set_sprung(trap);
            if (!flag(t, "OneShot") && dbl(t, "RearmSeconds", 3) > 0) rearm(trap, dbl(t, "RearmSeconds", 3) * 1000);
            if (victim == 0) { snprintf(line, sizeof line, "You set off \"%s\"!", str(def, "Name")); game_log(line); }
            events_object_event(trap, TR_ENTER, victim > 0 ? victim : -1);
            int l, tp, w, d, z, h, x, y, c, i, q, n;
            world_thing_box(trap, &l, &tp, &w, &d, &z, &h);
            world_thing_info(trap, &c, &i, &q, &x, &y, &n);
            sound_play(num(t, "ExplosionSoundResourceId", -1) >= 0 ? num(t, "ExplosionSoundResourceId", -1) : num(t, "TriggerSoundResourceId", -1));
            const Sprite *boom = link_sprite(get(t, "ExplosionSprite"));
            if (boom) world_effect_once(boom, x, y);
            lower_name(def, what, sizeof what);
            blast(l + w / 2.0, tp + d / 2.0, num(t, "RadiusTiles", 3), num(t, "DamageMin", 1), num(t, "DamageMax", 5), what, trap);
            if (!cJSON_IsFalse(get(t, "RemovedAfterExplosion")) && world_thing_there(trap)) world_thing_remove(trap);
            return;
        }
        case TK_SHOOTING: if (num(t, "TriggeredBy", 0) == 0) fire_trap(trap, def, victim); return;
        default: spring(trap, def, victim); return;
    }
}

/* Shooting traps set off by someone in their line of fire (checked a few times a second). */
static double lineOfFireMs;
static void shooting_traps(double ms)
{
    if ((lineOfFireMs -= ms) > 0) return;
    lineOfFireMs = 250;
    for (int i = 0; i < world_thing_count(); i++)
    {
        int trap = world_thing_uid_at(i);
        if (!trap || world_thing_type(trap) != OT_TRAP) continue;
        cJSON *def = object_def(world_thing_object_id(trap)), *t = get(def, "Trap");
        if (num(t, "Kind", 0) != TK_SHOOTING || num(t, "TriggeredBy", 0) != 1 || rearming(trap) || (flag(t, "OneShot") && world_trap_sprung(trap))) continue;
        int l, tp, w, d, z, h;
        world_thing_box(trap, &l, &tp, &w, &d, &z, &h);
        double tx = l + w / 2.0, ty = tp + d / 2.0;
        static const int DX[8] = { 0, 1, 1, 1, 0, -1, -1, -1 }, DY[8] = { -1, -1, 0, 1, 1, 1, 0, -1 };
        int f = world_thing_facing(trap) & 7;
        for (int a = 0; a < world_actor_count(); a++)
        {
            if (world_actor_gone(a) || vitals_dead(world_actor_character(a))) continue;
            double px, py;
            world_actor_pixel(a, &px, &py);
            double ux = px / 8 - tx, uy = py / 8 - ty, along = ux * DX[f] + uy * DY[f], across = fabs(ux * DY[f] - uy * DX[f]) / sqrt((double)(DX[f] * DX[f] + DY[f] * DY[f]));
            int range = num(t, "RangeTiles", 12) > 1 ? num(t, "RangeTiles", 12) : 1;
            if (along <= 0 || along > range || across > 2) continue;
            fire_trap(trap, def, a);
            break;
        }
    }
}

/* ---------------- Each frame ---------------- */

/* Which tiles, traps and fires each character stands on now, and what changed since the last frame. */
static void floor_objects(double ms)
{
    stayMs += ms;
    int stay = stayMs >= 1000;
    if (stay) stayMs = 0;
    for (int a = 0; a < world_actor_count() && a < 256; a++)
    {
        On *o = &on[a];
        if (burnAgain[a] > 0) burnAgain[a] -= ms;
        if (world_actor_gone(a) || vitals_dead(world_actor_character(a)) || (a == 0 && world_seated(0))) { o->count = 0; continue; }
        int l, t, w, d, lift, h, near[64];
        world_actor_foot(a, &l, &t, &w, &d, &lift, &h);
        int n = world_things_in(l, t, w, d, near, 64);
        On now = { { 0 }, 0 };
        for (int i = 0; i < n && now.count < 8; i++)
        {
            int kind = floor_kind(near[i]), bl, bt, bw, bd, bz, bh;
            if (!kind) continue;
            world_thing_box(near[i], &bl, &bt, &bw, &bd, &bz, &bh);
            if (abs(bz - lift) > 1) continue;
            now.uids[now.count++] = near[i];
        }
        for (int i = 0; i < now.count; i++)
        {
            int uid = now.uids[i], before = 0;
            for (int k = 0; k < o->count; k++) before |= o->uids[k] == uid;
            if (!world_thing_there(uid)) continue;
            int kind = floor_kind(uid);
            cJSON *def = object_def(world_thing_object_id(uid));
            if (!before && kind == 2) trigger(uid, def, a);
            else if (!before && kind == 1) events_object_event(uid, TR_ENTER, a > 0 ? a : -1);
            else if (before && kind == 1 && stay) events_object_event(uid, TR_STAY, a > 0 ? a : -1);
            if (kind == 3 && world_thing_lit(uid) && burnAgain[a] <= 0 && world_thing_there(uid))
            {
                /* a lit fire burns whoever is in it - again every second they stay */
                burnAgain[a] = 1000;
                cJSON *light = get(def, "Light");
                char line[160], what[96];
                int damage = roll(num(light, "BurnDamageMin", 2), num(light, "BurnDamageMax", 6));
                if (a == 0) { snprintf(line, sizeof line, "You are burnt by \"%s\"!", str(def, "Name")); game_log(line); }
                lower_name(def, what, sizeof what);
                if (damage > 0) vitals_damage(world_actor_character(a), damage, what);
            }
        }
        for (int k = 0; k < o->count; k++)
        {
            int uid = o->uids[k], still = 0;
            for (int i = 0; i < now.count; i++) still |= now.uids[i] == uid;
            if (!still && world_thing_there(uid) && floor_kind(uid) == 1) events_object_event(uid, TR_EXIT, a > 0 ? a : -1);
        }
        *o = now;
    }
}

void traps_update(double ms)
{
    for (int i = 0; i < rearmCount; i++) if ((rearms[i].ms -= ms) <= 0) rearms[i--] = rearms[--rearmCount];
    floor_objects(ms);
    shooting_traps(ms);
    update_flying(ms);
}

/* Something put down on the map: By Item Dropped On It on the tiles under it, on what it rests on, and on the map event of its cell. */
void traps_dropped(int uid, int category, int itemId)
{
    int l, t, w, d, z, h, near[128];
    world_thing_box(uid, &l, &t, &w, &d, &z, &h);
    int n = world_things_in(l, t, w, d, near, 128);
    for (int i = 0; i < n; i++)
    {
        if (near[i] == uid || world_thing_object_id(near[i]) < 0 || !events_has_trigger(CAT_OBJECT, world_thing_object_id(near[i]), TR_DROP)) continue;
        int bl, bt, bw, bd, bz, bh;
        world_thing_box(near[i], &bl, &bt, &bw, &bd, &bz, &bh);
        int hit = world_thing_type(near[i]) == OT_TILE ? 1 : bz + bh == z; /* a tile: anything over it; anything else: resting right on top */
        if (hit) events_dropped_on(near[i], category, itemId);
    }
    int c, i, q, x, y, k;
    world_thing_info(uid, &c, &i, &q, &x, &y, &k);
    events_dropped_at_cell(x, y, z, category, itemId);
}

void traps_launch(int objectId, double x, double y, double dx, double dy, int lift, int shooter) { launch(objectId, x, y, dx, dy, lift, shooter, 0); }

void traps_reset(void) { memset(on, 0, sizeof on); memset(flying, 0, sizeof flying); rearmCount = 0; stayMs = 0; memset(burnAgain, 0, sizeof burnAgain); }
