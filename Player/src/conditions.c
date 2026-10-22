/*
 * The game conditions events test (Advance IF, a Complex Condition's Game
 * Condition, a page's "Until Condition Met"), and the history they read:
 * what has happened in this game - used, lit, put out, burnt out, equipped
 * and unequipped, sounds played and stopped, events and their pages run...
 * A "happened" condition is true once it has happened at least so many
 * times (and, if set, within so many game hours). As GameTestWindow.Conditions.
 * Sold / traded wait for the shops (false till then), as in the editor.
 */
#include "conditions.h"
#include "events.h"
#include "items.h"
#include "world.h"
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

double sc_strtod(const char *text, char **end);

typedef struct Happening { char key[96]; int count; long lastMinute; } Happening;
static Happening *history; static int historyCount, historyCap;

static Happening *find(const char *key)
{
    for (int i = 0; i < historyCount; i++) if (!strcmp(history[i].key, key)) return &history[i];
    return NULL;
}

void conditions_record(const char *key)
{
    Happening *h = find(key);
    if (!h)
    {
        if (historyCount == historyCap) { historyCap = historyCap ? historyCap * 2 : 64; history = (Happening *)realloc(history, sizeof(Happening) * (size_t)historyCap); }
        h = &history[historyCount++];
        snprintf(h->key, sizeof h->key, "%s", key);
        h->count = 0;
    }
    h->count++;
    h->lastMinute = events_total_minutes();
}

static const char *category_name(int category) { return category == 3 ? "Book" : category == 4 ? "Object" : category == 5 ? "Container" : category == 0 ? "Weapon" : category == 1 ? "Armor" : "Item"; }

/* Something happened to an item: under its own key, and under "any" of that happening. */
void conditions_record_item(const char *what, int category, int itemId)
{
    char key[96];
    snprintf(key, sizeof key, "%s:%s:%d", what, category_name(category), itemId); conditions_record(key);
    snprintf(key, sizeof key, "%s:*", what); conditions_record(key);
}

/* Someone did something with an item (put it on, took it off): by who and what, and "anyone" / "anything". */
void conditions_record_person_item(const char *what, int characterId, int category, int itemId)
{
    char key[96];
    snprintf(key, sizeof key, "%s:%d:%s:%d", what, characterId, category_name(category), itemId); conditions_record(key);
    snprintf(key, sizeof key, "%s:*:%s:%d", what, category_name(category), itemId); conditions_record(key);
    snprintf(key, sizeof key, "%s:%d:*", what, characterId); conditions_record(key);
    snprintf(key, sizeof key, "%s:*:*", what); conditions_record(key);
}

static int happened(const char *key, int atLeast, int withinHours)
{
    Happening *h = find(key);
    if (!h || h->count < (atLeast > 1 ? atLeast : 1)) return 0;
    return withinHours <= 0 || events_total_minutes() - h->lastMinute <= withinHours * 60L;
}

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }

static int compare(double a, int op, double b)
{
    switch (op) { case 0: return a == b; case 1: return a != b; case 2: return a > b; case 3: return a >= b; case 4: return a < b; default: return a <= b; }
}

/* The item a condition is about, or "*" (any). */
static void item_key(cJSON *item, char *out, size_t size)
{
    if (cJSON_IsObject(item) && num(item, "ItemId", -1) >= 0) snprintf(out, size, "%s:%d", category_name(num(item, "Category", 4)), num(item, "ItemId", -1));
    else snprintf(out, size, "*");
}

static int happened_to(const char *what, cJSON *c)
{
    char item[48], key[96];
    item_key(get(c, "Item"), item, sizeof item);
    snprintf(key, sizeof key, "%s:%s", what, item);
    return happened(key, num(c, "Number", 1), num(c, "Number2", 0));
}

static cJSON *character_json(int characterId)
{
    cJSON *c, *list = get(get(get((cJSON *)world_json(), "Project"), "Characters"), "Characters");
    cJSON_ArrayForEach(c, list) if (num(c, "Id", -2) == characterId) return c;
    return NULL;
}

static int main_character(void) { return world_actor_character(0); }

static int test(cJSON *c, const char *evaluatingEvent)
{
    int kind = num(c, "Kind", 1), charId = num(c, "CharacterId", -2);
    int who = charId == -2 ? main_character() : charId;
    cJSON *item = get(c, "Item");
    int category = cJSON_IsObject(item) && num(item, "ItemId", -1) >= 0 ? num(item, "Category", 4) : -1, itemId = category >= 0 ? num(item, "ItemId", -1) : -1;
    char key[128], itemText[48];
    item_key(item, itemText, sizeof itemText);
    switch (kind)
    {
        case 1: /* Variable */
        {
            double value = events_variable(str(c, "VariableName")), target = 0;
            const char *t = str(c, "Value");
            while (*t == ' ') t++;
            if (!strncmp(t, "true", 4) || !strncmp(t, "True", 4)) target = 1;
            else if (!strncmp(t, "false", 5) || !strncmp(t, "False", 5)) target = 0;
            else target = sc_strtod(t, NULL);
            return compare(value, num(c, "Comparison", 0), target);
        }
        case 2: return who >= 0 && items_equipped(who, category, itemId);                 /* Equipped */
        case 12: return who >= 0 && items_carried_not_worn(who, category, itemId);        /* Unequipped: carried, not worn */
        case 27: case 3:                                                                   /* Having / Not having an item */
        {
            int count = charId == -2 ? items_party_count_any(category, itemId) : who >= 0 ? items_count_any(who, category, itemId) : 0;
            int has = count >= (num(c, "Number", 1) > 1 ? num(c, "Number", 1) : 1);
            return kind == 27 ? has : !has;
        }
        case 4: { cJSON *ch = character_json(who); return ch && num(ch, "Gender", 0) == num(c, "Gender", 0); }
        case 5: { cJSON *ch = character_json(who); return ch && compare(num(ch, "Age", 0), num(c, "Comparison", 0), num(c, "Number", 1)); }
        case 9:
        {
            cJSON *ch = character_json(who);
            char id[16];
            snprintf(id, sizeof id, "%d", num(c, "StatId", -1));
            const char *v = cJSON_GetStringValue(get(get(ch, "StatValues"), id));
            return v && compare(atoi(v), num(c, "Comparison", 0), num(c, "Number", 1));
        }
        case 6: return happened_to("destroyed", c);
        case 7: return happened_to("stolen", c);
        case 8: return happened_to("used", c);
        case 10: return happened_to("ignited", c);
        case 15: return happened_to("picklocked", c);
        case 29: return happened_to("unlockkey", c);
        case 30: return happened_to("unlockmagic", c);
        case 31: return happened_to("burnedout", c);
        case 32: return happened_to("putout", c);
        case 33: return happened_to("lit", c);
        case 34: return events_last_result();
        case 11: /* Seeing a character: it sees the main character (1), or it's on the screen */
        {
            int actor = world_actor_by_character(who);
            if (actor < 0) return 0;
            if (num(c, "Number", 1) == 1) return actor != 0 && world_actor_in_sight(actor);
            return world_actor_in_sight(actor);
        }
        case 13: /* Character killed */
            if (charId == -1) return happened("killed:*", num(c, "Number", 1), num(c, "Number2", 0));
            snprintf(key, sizeof key, "killed:%d", who);
            return happened(key, num(c, "Number", 1), num(c, "Number2", 0));
        case 14: case 18:
        {
            int sound = num(c, "SoundId", -1);
            if (sound >= 0) snprintf(key, sizeof key, "%s:%d", kind == 14 ? "sound" : "soundstop", sound);
            else snprintf(key, sizeof key, "%s:*", kind == 14 ? "sound" : "soundstop");
            return happened(key, num(c, "Number", 1), num(c, "Number2", 0));
        }
        case 16: return world_lever_on(itemId, cJSON_IsTrue(get(c, "On")) || !get(c, "On")); /* Switch / button / lever */
        case 17: /* Time / date */
        {
            int from = num(c, "Number", 1), to = num(c, "Number2", 0), hour = events_hour();
            from = from < 0 ? 0 : from > 23 ? 23 : from; to = to < 0 ? 0 : to > 23 ? 23 : to;
            int inHours = from == to ? hour == from : from < to ? hour >= from && hour < to : hour >= from || hour < to;
            const char *v = str(c, "Value");
            while (*v == ' ') v++;
            int onDay = !*v || compare(events_day() + 1, num(c, "Comparison", 0), atoi(v));
            return inHours && onDay;
        }
        case 20: case 19:
        {
            if (charId == -1 || who < 0) snprintf(key, sizeof key, "%s:*:%s", kind == 20 ? "equip" : "unequip", itemText);
            else snprintf(key, sizeof key, "%s:%d:%s", kind == 20 ? "equip" : "unequip", who, itemText);
            return happened(key, num(c, "Number", 1), num(c, "Number2", 0));
        }
        case 21: case 22: case 23: return 0; /* sold / traded: no shops yet */
        case 24: snprintf(key, sizeof key, "event:%s", str(c, "EventName")); return happened(key, num(c, "Number", 1), num(c, "Number2", 0));
        case 25: snprintf(key, sizeof key, "eventpage:%s:%d", evaluatingEvent ? evaluatingEvent : "", num(c, "Number", 1)); return happened(key, 1, num(c, "Number2", 0));
        case 26: snprintf(key, sizeof key, "eventpage:%s:%d", str(c, "EventName"), num(c, "Number", 1)); return happened(key, 1, num(c, "Number2", 0));
        case 28: /* Door / container open (an open container window: none in the player yet) */
            if (category == 5) return !(cJSON_IsTrue(get(c, "On")) || !get(c, "On"));
            return world_door_open_any(itemId, cJSON_IsTrue(get(c, "On")) || !get(c, "On"));
    }
    return 0;
}

int conditions_test(cJSON *condition, const char *evaluatingEvent)
{
    if (!cJSON_IsObject(condition)) return 0;
    int result = test(condition, evaluatingEvent);
    return cJSON_IsTrue(get(condition, "Not")) ? !result : result;
}

/* A list joined by And / Or / Xor, left to right (empty: true). */
int conditions_all(cJSON *list, const char *evaluatingEvent)
{
    int n = cJSON_GetArraySize(list);
    if (n == 0) return 1;
    int result = conditions_test(cJSON_GetArrayItem(list, 0), evaluatingEvent);
    for (int i = 1; i < n; i++)
    {
        int next = conditions_test(cJSON_GetArrayItem(list, i), evaluatingEvent);
        switch (num(cJSON_GetArrayItem(list, i - 1), "NextOperator", 0)) { case 1: result = result || next; break; case 2: result = result ^ next; break; default: result = result && next; }
    }
    return result;
}

/* ---------------- Saving: what has happened ---------------- */
void conditions_save(cJSON *runtime)
{
    cJSON *all = cJSON_CreateArray();
    for (int i = 0; i < historyCount; i++)
    {
        cJSON *e = cJSON_CreateObject();
        cJSON_AddStringToObject(e, "Key", history[i].key); cJSON_AddNumberToObject(e, "Count", history[i].count); cJSON_AddNumberToObject(e, "LastMinute", history[i].lastMinute);
        cJSON_AddItemToArray(all, e);
    }
    cJSON_AddItemToObject(runtime, "Happenings", all);
}

void conditions_restore(cJSON *runtime)
{
    cJSON *e;
    historyCount = 0;
    cJSON_ArrayForEach(e, get(runtime, "Happenings"))
    {
        Happening *h = find(str(e, "Key"));
        if (!h)
        {
            if (historyCount == historyCap) { historyCap = historyCap ? historyCap * 2 : 64; history = (Happening *)realloc(history, sizeof(Happening) * (size_t)historyCap); }
            h = &history[historyCount++];
            snprintf(h->key, sizeof h->key, "%s", str(e, "Key"));
        }
        h->count = num(e, "Count", 0); h->lastMinute = (long)num(e, "LastMinute", 0);
    }
}
