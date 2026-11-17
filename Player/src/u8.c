/*
 * The Ultima 8 engine's own rules in the player (U8GameWindow and its U8*.cs files); the rest is the Ultima 7 engine's, as
 * Windows' U8 engine is a copy of it. The diamond view is world.c's (u8View).
 *
 * Keys (U8GameWindow.U8Keys, Engine/U8Keys.cs): every key the game reads is an action's - Ultima 8's, the maker's (System
 * Settings > Ultima 8) or the player's own, changed in the game and kept beside the saves (keys.json). Here the key pressed is
 * turned into the key the engine below reads (u8_translate); the actions the Ultima 7 engine doesn't have are this file's.
 */
#include "u8.h"
#include "platform.h"
#include "events.h"
#include "save.h"
#include "script.h"
#include "commands.h"
#include "world.h"
#include "dolls.h"
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>
#include "gumps.h"
#include "dialog.h"
#include "combat.h"
#include "vitals.h"
#include "items.h"
#include "font.h"
#include "pictures.h"
#include "schedules.h"
#include "vehicles.h"

static int on;

/* Case not minded: p starts with pat. */
static int same_start(const char *p, const char *pat) { for (; *pat; p++, pat++) { char a = *p, b = *pat; if (a >= 'A' && a <= 'Z') a += 32; if (b >= 'A' && b <= 'Z') b += 32; if (a != b) return 0; } return 1; }

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static const char *str(cJSON *o, const char *k) { const char *s = cJSON_GetStringValue(get(o, k)); return s ? s : ""; }
static cJSON *project(void) { return get((cJSON *)world_json(), "Project"); }

int u8_on(void) { return on; }

/* ---------------- The keys (Engine/U8Keys.cs) ---------------- */

enum { G_WORLD, G_SCREENS, G_EVERYWHERE };
typedef struct KeyAction { const char *id, *name, *key; int group; } KeyAction;
static const KeyAction ACTIONS[] = {
    { "Up", "Walk up the screen", "Up", G_WORLD }, { "Down", "Walk down the screen", "Down", G_WORLD },
    { "Left", "Walk left", "Left", G_WORLD }, { "Right", "Walk right", "Right", G_WORLD },
    { "Run", "Run (held)", "LeftShift", G_WORLD }, { "Jump", "Jump", "J", G_WORLD },
    { "Combat", "Combat mode", "C", G_WORLD }, { "Inventory", "Inventory", "I", G_WORLD },
    { "Stats", "Stats", "Z", G_WORLD }, { "SaveLoad", "Save / Load", "S", G_WORLD },
    { "Menu", "Keys paperdoll", "F1", G_WORLD }, { "WholeStack", "Move a whole stack, no question (held)", "LeftCtrl", G_WORLD },
    { "ControlNext", "Control the next party member", "Tab", G_WORLD }, { "ControlMain", "Control the main character again", "Home", G_WORLD },
    { "Attack", "Attack (at the crosshair, or the way you face)", "A", G_WORLD }, { "NextTarget", "Crosshair: next target", "T", G_WORLD },
    { "RollLeft", "Roll left", "Ctrl+Left", G_WORLD }, { "RollRight", "Roll right", "Ctrl+Right", G_WORLD },
    { "SideStepLeft", "Side-step left", "OemComma", G_WORLD }, { "SideStepRight", "Side-step right", "OemPeriod", G_WORLD },
    { "Kneel", "Kneel / stand up", "K", G_WORLD },
    { "TestNextPeriod", "Game Test: next schedule period", "F6", G_WORLD }, { "TestScriptState", "Game Test: script data", "F9", G_WORLD },
    { "TestFrameRate", "Game Test: frame rate", "F10", G_WORLD },
    { "Quit", "Quit prompt", "Ctrl+Q", G_EVERYWHERE }, { "Cancel", "Cancel / close", "Escape", G_EVERYWHERE },
    { "Number1", "Party member / reply 1", "D1", G_EVERYWHERE }, { "Number2", "Party member / reply 2", "D2", G_EVERYWHERE },
    { "Number3", "Party member / reply 3", "D3", G_EVERYWHERE }, { "Number4", "Party member / reply 4", "D4", G_EVERYWHERE },
    { "Number5", "Party member / reply 5", "D5", G_EVERYWHERE }, { "Number6", "Party member / reply 6", "D6", G_EVERYWHERE },
    { "Number7", "Party member / reply 7", "D7", G_EVERYWHERE }, { "Number8", "Party member / reply 8", "D8", G_EVERYWHERE },
    { "Number9", "Party member / reply 9", "D9", G_EVERYWHERE },
    { "Number1Second", "Party member / reply 1 (second key)", "NumPad1", G_EVERYWHERE }, { "Number2Second", "Party member / reply 2 (second key)", "NumPad2", G_EVERYWHERE },
    { "Number3Second", "Party member / reply 3 (second key)", "NumPad3", G_EVERYWHERE }, { "Number4Second", "Party member / reply 4 (second key)", "NumPad4", G_EVERYWHERE },
    { "Number5Second", "Party member / reply 5 (second key)", "NumPad5", G_EVERYWHERE }, { "Number6Second", "Party member / reply 6 (second key)", "NumPad6", G_EVERYWHERE },
    { "Number7Second", "Party member / reply 7 (second key)", "NumPad7", G_EVERYWHERE }, { "Number8Second", "Party member / reply 8 (second key)", "NumPad8", G_EVERYWHERE },
    { "Number9Second", "Party member / reply 9 (second key)", "NumPad9", G_EVERYWHERE },
    { "Accept", "Accept / OK", "Enter", G_SCREENS }, { "Accept2", "Accept (second key)", "Space", G_SCREENS },
    { "ScreenUp", "Previous choice", "Up", G_SCREENS }, { "ScreenUp2", "Previous choice (second key), slider down", "Left", G_SCREENS },
    { "ScreenDown", "Next choice", "Down", G_SCREENS }, { "ScreenDown2", "Next choice (second key), slider up", "Right", G_SCREENS },
    { "ScreenDown3", "Next choice (third key)", "Tab", G_SCREENS },
    { "PagePrev", "Previous page of replies", "PageUp", G_SCREENS }, { "PageNext", "Next page of replies", "PageDown", G_SCREENS },
    { "Erase", "Erase a typed letter", "Back", G_SCREENS },
};
#define ACTION_COUNT ((int)(sizeof ACTIONS / sizeof ACTIONS[0]))

static const char *BINDABLE[] = {
    "A", "B", "C", "D", "E", "F", "G", "H", "I", "J", "K", "L", "M", "N", "O", "P", "Q", "R", "S", "T", "U", "V", "W", "X", "Y", "Z",
    "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
    "NumPad0", "NumPad1", "NumPad2", "NumPad3", "NumPad4", "NumPad5", "NumPad6", "NumPad7", "NumPad8", "NumPad9",
    "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
    "Up", "Down", "Left", "Right", "Space", "Enter", "Tab", "Back", "Escape", "Insert", "Delete", "Home", "End", "PageUp", "PageDown",
    "LeftShift", "RightShift", "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt", "CapsLock", "NumLock", "Scroll", "Pause",
    "OemTilde", "OemMinus", "OemPlus", "OemOpenBrackets", "OemCloseBrackets", "OemSemicolon", "OemQuotes", "OemComma", "OemPeriod", "OemQuestion", "OemPipe",
    "Add", "Subtract", "Multiply", "Divide", "Decimal",
};
#define BINDABLE_COUNT ((int)(sizeof BINDABLE / sizeof BINDABLE[0]))

static char keyOf[ACTION_COUNT][32];   /* each action's key now ("Ctrl+Q") */
static struct { char key[16]; int mods; } parsed[ACTION_COUNT];

static int action_index(const char *id) { for (int i = 0; i < ACTION_COUNT; i++) if (!strcmp(ACTIONS[i].id, id)) return i; return -1; }
static int action_by_name(const char *name) { for (int i = 0; i < ACTION_COUNT; i++) if (!strcmp(ACTIONS[i].name, name)) return i; return -1; }
static int bindable(const char *k) { for (int i = 0; i < BINDABLE_COUNT; i++) if (!strcmp(BINDABLE[i], k)) return 1; return 0; }
static int is_modifier(const char *k) { return !strcmp(k, "LeftShift") || !strcmp(k, "RightShift") || !strcmp(k, "LeftCtrl") || !strcmp(k, "RightCtrl") || !strcmp(k, "LeftAlt") || !strcmp(k, "RightAlt"); }

/* "Ctrl+Shift+Q" -> the key ("Q") and what's held with it. 0: not a key this game can read (U8Keys.IsKeyName). */
static int split_key(const char *name, char *key, size_t size, int *mods)
{
    *mods = 0;
    const char *p = name;
    for (;;)
    {
        const char *plus = strchr(p, '+');
        if (!plus) break;
        size_t n = (size_t)(plus - p);
        if (n == 4 && !strncmp(p, "Ctrl", 4)) *mods |= KEYMOD_CTRL;
        else if (n == 5 && !strncmp(p, "Shift", 5)) *mods |= KEYMOD_SHIFT;
        else if (n == 3 && !strncmp(p, "Alt", 3)) *mods |= KEYMOD_ALT;
        else return 0;
        p = plus + 1;
    }
    snprintf(key, size, "%s", p);
    return bindable(key) && (*mods == 0 || !is_modifier(key));
}
static int is_key_name(const char *name) { char k[32]; int m; return split_key(name, k, sizeof k, &m); }

static void cache_keys(void) { for (int i = 0; i < ACTION_COUNT; i++) split_key(keyOf[i], parsed[i].key, sizeof parsed[i].key, &parsed[i].mods); }

static void maker_key(int i, char *out, size_t size)
{
    cJSON *k = get(get(get(get(project(), "Settings"), "Ultima8"), "Keys"), ACTIONS[i].id);
    snprintf(out, size, "%s", cJSON_IsString(k) && is_key_name(k->valuestring) ? k->valuestring : ACTIONS[i].key);
}

static void keys_file(char *path, size_t size) { snprintf(path, size, "%s/keys.json", save_folder() ? save_folder() : "."); }

static void load_keys(void)
{
    for (int i = 0; i < ACTION_COUNT; i++) maker_key(i, keyOf[i], sizeof keyOf[i]);
    char path[1200]; keys_file(path, sizeof path);
    long n = 0; char *text = NULL;
    FILE *f = fopen(path, "rb");
    if (f) { fseek(f, 0, SEEK_END); n = ftell(f); fseek(f, 0, SEEK_SET); text = n > 0 ? (char *)malloc((size_t)n + 1) : NULL; if (text) { n = (long)fread(text, 1, (size_t)n, f); text[n] = 0; } fclose(f); }
    cJSON *own = text ? cJSON_Parse(text) : NULL, *k;
    free(text);
    cJSON_ArrayForEach(k, own) { int i = action_index(k->string); if (i >= 0 && cJSON_IsString(k) && is_key_name(k->valuestring)) snprintf(keyOf[i], sizeof keyOf[i], "%s", k->valuestring); }
    cJSON_Delete(own);
    cache_keys();
}

/* U8Keys.Save: only what differs from the maker's keys; nothing differing, no file. */
static int save_keys(void)
{
    cJSON *changed = cJSON_CreateObject();
    int any = 0;
    for (int i = 0; i < ACTION_COUNT; i++) { char m[32]; maker_key(i, m, sizeof m); if (strcmp(m, keyOf[i])) { cJSON_AddStringToObject(changed, ACTIONS[i].id, keyOf[i]); any = 1; } }
    char path[1200]; keys_file(path, sizeof path);
    int ok = 1;
    if (!any) remove(path);
    else
    {
        if (save_folder()) platform_make_dir(save_folder());
        char *text = cJSON_PrintUnformatted(changed);
        FILE *f = text ? fopen(path, "wb") : NULL;
        if (f) { ok = fwrite(text, 1, strlen(text), f) == strlen(text); fclose(f); } else ok = 0;
        free(text);
    }
    cJSON_Delete(changed);
    return ok;
}

static int clash(int a, int b) { return ACTIONS[a].group == G_EVERYWHERE || ACTIONS[b].group == G_EVERYWHERE || ACTIONS[a].group == ACTIONS[b].group; }

/* U8Keys.Assign: another action with that key takes this one's old key (swap), else nothing changes. */
static int assign(int id, const char *key, int swap)
{
    char old[32]; snprintf(old, sizeof old, "%s", keyOf[id]);
    if (!strcmp(old, key)) return 1;
    int holders[ACTION_COUNT], n = 0;
    for (int a = 0; a < ACTION_COUNT; a++) if (a != id && !strcmp(keyOf[a], key) && clash(a, id)) holders[n++] = a;
    if (n > 0 && !swap) return 0;
    for (int h = 0; h < n; h++)
        for (int o = 0; o < ACTION_COUNT; o++)
            if (o != id && o != holders[h] && !strcmp(keyOf[o], old) && clash(o, holders[h])) return 0;
    for (int h = 0; h < n; h++) snprintf(keyOf[holders[h]], sizeof keyOf[0], "%s", old);
    snprintf(keyOf[id], sizeof keyOf[id], "%s", key);
    return 1;
}

static int keys_changed(void) { cache_keys(); u8_release_all(); return save_keys(); }

/* U8Keys.Display: a key's name as the player reads it. */
static void display_one(const char *k, char *out, size_t size)
{
    static const char *D[][2] = {
        { "Up", "Up Arrow" }, { "Down", "Down Arrow" }, { "Left", "Left Arrow" }, { "Right", "Right Arrow" }, { "Back", "Backspace" },
        { "PageUp", "Page Up" }, { "PageDown", "Page Down" }, { "LeftShift", "Left Shift" }, { "RightShift", "Right Shift" },
        { "LeftCtrl", "Left Ctrl" }, { "RightCtrl", "Right Ctrl" }, { "LeftAlt", "Left Alt" }, { "RightAlt", "Right Alt" },
        { "CapsLock", "Caps Lock" }, { "NumLock", "Num Lock" }, { "Scroll", "Scroll Lock" }, { "OemTilde", "`" }, { "OemMinus", "-" },
        { "OemPlus", "=" }, { "OemOpenBrackets", "[" }, { "OemCloseBrackets", "]" }, { "OemSemicolon", ";" }, { "OemQuotes", "'" },
        { "OemComma", "," }, { "OemPeriod", "." }, { "OemQuestion", "/" }, { "OemPipe", "\\" }, { "Add", "Keypad +" },
        { "Subtract", "Keypad -" }, { "Multiply", "Keypad *" }, { "Divide", "Keypad /" }, { "Decimal", "Keypad ." } };
    if (strlen(k) == 2 && k[0] == 'D' && k[1] >= '0' && k[1] <= '9') { snprintf(out, size, "%c", k[1]); return; }
    if (strlen(k) == 7 && !strncmp(k, "NumPad", 6)) { snprintf(out, size, "Keypad %c", k[6]); return; }
    for (size_t i = 0; i < sizeof D / sizeof D[0]; i++) if (!strcmp(D[i][0], k)) { snprintf(out, size, "%s", D[i][1]); return; }
    snprintf(out, size, "%s", k);
}
void u8_display_key(const char *name, char *out, size_t size)
{
    char key[32], one[32]; int mods;
    if (!split_key(name, key, sizeof key, &mods)) { snprintf(out, size, "%s", name); return; }
    display_one(key, one, sizeof one);
    snprintf(out, size, "%s%s%s%s", mods & KEYMOD_CTRL ? "Ctrl+" : "", mods & KEYMOD_SHIFT ? "Shift+" : "", mods & KEYMOD_ALT ? "Alt+" : "", one);
}

/* U8Keys.FromWords: the key with these words (Set Key's list). */
static const char *from_words(const char *words)
{
    char d[32];
    for (int i = 0; i < BINDABLE_COUNT; i++) { display_one(BINDABLE[i], d, sizeof d); if (!strcmp(d, words)) return BINDABLE[i]; }
    return NULL;
}

void u8_key_words(const char *actionId, char *out, size_t size)
{
    int i = action_index(actionId);
    if (i < 0) { snprintf(out, size, "%s", actionId); return; }
    u8_display_key(keyOf[i], out, size);
}

/* ---------------- Keys pressed ---------------- */

static char heldKeys[16][16]; static int heldCount; /* the key names down now */
static int mods; /* what's held now (Ctrl, Shift, Alt) */

void u8_release_all(void) { heldCount = 0; }

/* KeyIs: this press is this action's. A plain key counts whatever else is held, unless the press is another action's "Ctrl+...". */
static int key_is(const char *name, int pressedMods, int i)
{
    if (strcmp(parsed[i].key, name)) return 0;
    if (parsed[i].mods) return (pressedMods & parsed[i].mods) == parsed[i].mods;
    for (int o = 0; o < ACTION_COUNT; o++)
        if (parsed[o].mods && !strcmp(parsed[o].key, name) && (pressedMods & parsed[o].mods) == parsed[o].mods) return 0;
    return 1;
}
int u8_key_is(const PlatformEvent *e, const char *actionId) { int i = action_index(actionId); return i >= 0 && key_is(e->keyName, e->keyMods, i); }

/* KeyHeld: an action's key is down now. A Shift, Ctrl or Alt: either of the pair, as Shift always ran. */
int u8_held(const char *actionId)
{
    int i = action_index(actionId);
    if (i < 0 || (mods & parsed[i].mods) != parsed[i].mods) return 0;
    const char *k = parsed[i].key;
    if (!strcmp(k, "LeftShift") || !strcmp(k, "RightShift")) return (mods & KEYMOD_SHIFT) != 0;
    if (!strcmp(k, "LeftCtrl") || !strcmp(k, "RightCtrl")) return (mods & KEYMOD_CTRL) != 0;
    if (!strcmp(k, "LeftAlt") || !strcmp(k, "RightAlt")) return (mods & KEYMOD_ALT) != 0;
    for (int h = 0; h < heldCount; h++) if (!strcmp(heldKeys[h], k)) return 1;
    return 0;
}

/* The engine's own key for what this key does here: on a screen (a box, a slider, a question) the Screens' and Everywhere's
   actions, on the map the World's and Everywhere's. The arrows that walk stay the engine's arrows whatever key walks. */
static enum PlatformKey engine_key(const char *id)
{
    static const struct { const char *id; enum PlatformKey key; } M[] = {
        { "Up", KEY_UP }, { "Down", KEY_DOWN }, { "Left", KEY_LEFT }, { "Right", KEY_RIGHT }, { "Combat", KEY_C }, { "Inventory", KEY_I },
        { "Stats", KEY_Z }, { "SaveLoad", KEY_S }, { "Quit", KEY_CTRL_Q }, { "Cancel", KEY_ESCAPE },
        { "Accept", KEY_ENTER }, { "Accept2", KEY_SPACE }, { "ScreenUp", KEY_UP }, { "ScreenUp2", KEY_LEFT }, { "ScreenDown", KEY_DOWN },
        { "ScreenDown2", KEY_RIGHT }, { "ScreenDown3", KEY_TAB }, { "PagePrev", KEY_PAGEUP }, { "PageNext", KEY_PAGEDOWN }, { "Erase", KEY_BACKSPACE } };
    for (size_t i = 0; i < sizeof M / sizeof M[0]; i++) if (!strcmp(M[i].id, id)) return M[i].key;
    if (!strncmp(id, "Number", 6) && id[6] >= '1' && id[6] <= '9') return (enum PlatformKey)(KEY_1 + (id[6] - '1'));
    return KEY_OTHER;
}

/* A letter that types (TypesLetter): while a name is typed it writes, whatever it does elsewhere. */
static int types_letter(const char *k, int m)
{
    if (m & (KEYMOD_CTRL | KEYMOD_ALT)) return 0;
    if (strlen(k) == 1 && k[0] >= 'A' && k[0] <= 'Z') return 1;
    if (strlen(k) == 2 && k[0] == 'D' && k[1] >= '0' && k[1] <= '9') return 1;
    if (!strncmp(k, "NumPad", 6) || !strncmp(k, "Oem", 3) || !strcmp(k, "Space")) return 1;
    return !strcmp(k, "Multiply") || !strcmp(k, "Add") || !strcmp(k, "Subtract") || !strcmp(k, "Decimal") || !strcmp(k, "Divide");
}

static int lastAction = -1;
int u8_action(void) { return lastAction; }
const char *u8_action_id(void) { return lastAction >= 0 ? ACTIONS[lastAction].id : ""; }

void u8_translate(PlatformEvent *e, int onScreen, int typing)
{
    lastAction = -1;
    if (!on || (e->type != EV_KEY_DOWN && e->type != EV_KEY_UP)) return;
    mods = e->keyMods;
    if (e->key == KEY_F11) return; /* the window's own (full screen) */
    if (e->type == EV_KEY_DOWN)
    {
        int there = 0;
        for (int h = 0; h < heldCount; h++) if (!strcmp(heldKeys[h], e->keyName)) there = 1;
        if (!there && heldCount < 16 && e->keyName[0]) snprintf(heldKeys[heldCount++], sizeof heldKeys[0], "%s", e->keyName);
        enum PlatformKey k = KEY_OTHER;
        if (!(typing && types_letter(e->keyName, e->keyMods)))
            for (int i = 0; i < ACTION_COUNT; i++)
            {
                if (ACTIONS[i].group == (onScreen ? G_WORLD : G_SCREENS) || !key_is(e->keyName, e->keyMods, i)) continue;
                lastAction = i;
                k = engine_key(ACTIONS[i].id);
                break;
            }
        e->key = k;
        return;
    }
    for (int h = 0; h < heldCount; h++) if (!strcmp(heldKeys[h], e->keyName)) { heldKeys[h][0] = 0; memmove(heldKeys[h], heldKeys[heldCount - 1], sizeof heldKeys[0]); heldCount--; break; }
    /* WalkArrowReleased: a released key stops the arrow it walks, whatever was held with it */
    e->key = KEY_OTHER;
    static const char *W[] = { "Left", "Right", "Up", "Down" };
    static const enum PlatformKey A[] = { KEY_LEFT, KEY_RIGHT, KEY_UP, KEY_DOWN };
    for (int w = 0; w < 4; w++) if (!strcmp(parsed[action_index(W[w])].key, e->keyName)) { e->key = A[w]; break; }
}

/* FillKeyWords: a paperdoll's %key... filled in (longest id first: %keyaccept2 before %keyaccept). */
static int askAction = -1, askToken, askSwap; /* Ask For Key waiting */
static char askWords[64] = "Press a key...";
void u8_fill_key_words(const char *text, char *out, size_t size)
{
    snprintf(out, size, "%s", text);
    if (!on || !strstr(out, "%")) return;
    int order[ACTION_COUNT];
    for (int i = 0; i < ACTION_COUNT; i++) order[i] = i;
    for (int i = 1; i < ACTION_COUNT; i++) { int v = order[i], j = i; while (j > 0 && strlen(ACTIONS[order[j - 1]].id) < strlen(ACTIONS[v].id)) { order[j] = order[j - 1]; j--; } order[j] = v; }
    for (int n = 0; n < ACTION_COUNT; n++)
    {
        int i = order[n];
        char pat[40], shown[64];
        snprintf(pat, sizeof pat, "%%key%s", ACTIONS[i].id);
        if (askAction == i) snprintf(shown, sizeof shown, "%s", askWords); else u8_display_key(keyOf[i], shown, sizeof shown);
        for (;;)
        {
            char *hit = NULL;
            for (char *p = out; *p; p++) if (same_start(p, pat)) { hit = p; break; }
            if (!hit) break;
            char rest[2048]; snprintf(rest, sizeof rest, "%s", hit + strlen(pat));
            *hit = 0;
            size_t used = strlen(out);
            snprintf(out + used, size - used, "%s%s", shown, rest);
        }
    }
}

/* ---------------- Ask For Key (the event waits for the next key) ---------------- */

static char capturedMod[16]; /* U8KeyCapture: a Shift, Ctrl or Alt down, waiting to see what comes with it */

int u8_asking_key(void) { return askAction >= 0; }

static void finish_ask(int ok)
{
    int token = askToken;
    askAction = -1; askToken = 0;
    if (token) events_doll_answered(token, ok);
}
void u8_cancel_key_ask(void) { if (askAction >= 0) finish_ask(0); }

/* KeyAskTook / KeyAskReleased: 1 when the key went to Ask For Key. */
int u8_key_ask_event(const PlatformEvent *e)
{
    if (askAction < 0 || (e->type != EV_KEY_DOWN && e->type != EV_KEY_UP)) return 0;
    if (e->keyRepeat) return 1;
    if (e->type == EV_KEY_DOWN)
    {
        if (strcmp(ACTIONS[askAction].id, "Cancel") && u8_key_is(e, "Cancel")) { finish_ask(0); return 1; }
        if (!bindable(e->keyName)) return 1;
        if (is_modifier(e->keyName)) { if (!capturedMod[0]) snprintf(capturedMod, sizeof capturedMod, "%s", e->keyName); return 1; }
        capturedMod[0] = 0;
        char name[48];
        snprintf(name, sizeof name, "%s%s%s%s", e->keyMods & KEYMOD_CTRL ? "Ctrl+" : "", e->keyMods & KEYMOD_SHIFT ? "Shift+" : "", e->keyMods & KEYMOD_ALT ? "Alt+" : "", e->keyName);
        int ok = assign(askAction, name, askSwap) && keys_changed();
        finish_ask(ok);
        return 1;
    }
    if (capturedMod[0] && !strcmp(capturedMod, e->keyName))
    {
        char name[16]; snprintf(name, sizeof name, "%s", capturedMod);
        capturedMod[0] = 0;
        int ok = assign(askAction, name, askSwap) && keys_changed();
        finish_ask(ok);
    }
    return 1;
}

/* ---------------- Commands (the Ultima 8 tab) ---------------- */

static const char *param_of(cJSON *params, const char *k) { return str(params, k); }
int u8_build_up_stat_command(int whoCharacter, const char *stat, int points);

int u8_extras_command(int type, cJSON *params, int whoCharacter, int token, double (*formula)(const char *key), int thingUid);
static const char *(*textOf)(const char *key);
int u8_command(int type, cJSON *params, int whoCharacter, int activator, int token, double (*formula)(const char *key), const char *(*text)(const char *key), int thingUid)
{
    (void)activator;
    textOf = text;
    switch (type)
    {
        case C_U8AskKey:
        {
            int a = action_by_name(param_of(params, "Action"));
            if (a < 0) return -1;
            u8_cancel_key_ask();
            capturedMod[0] = 0;
            askAction = a; askToken = token; askSwap = strcmp(param_of(params, "IfUsed"), "Fail") != 0;
            return 0; /* waits for the key */
        }
        case C_U8SetKey:
        {
            int a = action_by_name(param_of(params, "Action"));
            const char *key = from_words(param_of(params, "Key")), *with = param_of(params, "With");
            if (a < 0 || !key) return -1;
            char name[48];
            if (!strcmp(with, "Ctrl") || !strcmp(with, "Shift") || !strcmp(with, "Alt")) snprintf(name, sizeof name, "%s+%s", with, key);
            else snprintf(name, sizeof name, "%s", key);
            if (!is_key_name(name)) return -1;
            return assign(a, name, strcmp(param_of(params, "IfUsed"), "Fail") != 0) && keys_changed() ? 1 : -1;
        }
        case C_U8GetKeyName:
        {
            int a = action_by_name(param_of(params, "Action"));
            if (a < 0) return -1;
            char words[64]; u8_display_key(keyOf[a], words, sizeof words);
            script_set(param_of(params, "Variable"), value_text(words));
            return 1;
        }
        case C_U8BuildUpStat: return u8_build_up_stat_command(whoCharacter, param_of(params, "Stat"), formula ? (int)formula("Points") : 0);
        case C_U8ResetKeys:
        {
            if (!strcmp(param_of(params, "Action"), "Every key"))
            {
                for (int i = 0; i < ACTION_COUNT; i++) maker_key(i, keyOf[i], sizeof keyOf[i]);
                return keys_changed() ? 1 : -1;
            }
            int a = action_by_name(param_of(params, "Action"));
            if (a < 0) return -1;
            char m[32]; maker_key(a, m, sizeof m);
            return assign(a, m, 1) && keys_changed() ? 1 : -1;
        }
    }
    return u8_extras_command(type, params, whoCharacter, token, formula, thingUid);
}

/* ---------------- Start ---------------- */

void u8_load(void)
{
    on = !strcmp(str(get(project(), "Manifest"), "Style"), "Ultima8");
    if (!on) return;
    const char *w = str(get(get(project(), "Settings"), "Ultima8"), "KeyAskWords");
    snprintf(askWords, sizeof askWords, "%s", w[0] ? w : "Press a key...");
    heldCount = 0; askAction = -1;
    u8_reset_control();
    load_keys();
}


/* ---------------- Moving the Avatar: what the player presses (U8GameWindow.U8Movement) ---------------- */

static int rightHeld, leftHeld, mouseX, mouseY;
static int u8_left_down(void) { return leftHeld; }
int u8_combat_mouse(const PlatformEvent *e);

/* WorldFromScreen: a screen direction (one of 8) as the world's - up the screen is north-west, right is north-east. */
static int sgn(int v) { return (v > 0) - (v < 0); }

/* PointerFromAvatar: the pointer from the Avatar as one of 8 screen directions, and how far - 0 a step, 1 a walk, 2 a run, -1 too near. */
static int pointer_from_avatar(int *sx, int *sy)
{
    double ax, ay;
    world_u8_player_screen(&ax, &ay);
    double dx = mouseX - ax, dy = mouseY - ay;
    static const int DX[8] = { 1, 1, 0, -1, -1, -1, 0, 1 }, DY[8] = { 0, 1, 1, 1, 0, -1, -1, -1 };
    int sector = (int)floor(atan2(dy, dx) / (3.14159265358979 / 4) + 0.5);
    sector = ((sector % 8) + 8) % 8;
    *sx = DX[sector]; *sy = DY[sector];
    cJSON *u8s = get(get(project(), "Settings"), "Ultima8");
    int run = cJSON_IsNumber(get(u8s, "RunThreshold")) ? get(u8s, "RunThreshold")->valueint : 160;
    int walk = cJSON_IsNumber(get(u8s, "WalkThreshold")) ? get(u8s, "WalkThreshold")->valueint : 50;
    int step = cJSON_IsNumber(get(u8s, "StepThreshold")) ? get(u8s, "StepThreshold")->valueint : 0;
    double halfW = SCREEN_W / 2.0, halfH = SCREEN_H / 2.0;
    if (fabs(dx) > halfW * run / 255 || fabs(dy) > halfH * run / 255) return 2;
    if (fabs(dx) > halfW * walk / 255 || fabs(dy) > halfH * walk / 255) return 1;
    if (step <= 0 || fabs(dx) > halfW * step / 255 || fabs(dy) > halfH * step / 255) return 0;
    return -1; /* nearer than Step beyond: the Avatar stays put */
}

/* TryGetU8Direction: the way the player wants the Avatar to go this frame (world, -1..1 each) and its speed (world_u8_set_speed); 0: none. */
static int u8_direction(int left, int right, int up, int down, int *dx, int *dy)
{
    *dx = *dy = 0;
    if (u8_stunned() || u8_hanging()) return 0;          /* knocked down, or holding a ledge */
    if (gumps_dragging_now()) return 0;                  /* holding something on the mouse */
    if (dialog_showing() || events_cinema()) return 0;   /* talking (the world goes on), or a cutscene */
    int sx = right - left, sy = down - up, run = u8_held("Run");
    if (sx || sy)
    {
        *dx = sgn(sx + sy); *dy = sgn(sy - sx);
        world_u8_set_speed(run ? 2.0 : 1.0);
        return 1;
    }
    if (rightHeld)
    {
        int length = pointer_from_avatar(&sx, &sy);
        if (length < 0) return 0;
        *dx = sgn(sx + sy); *dy = sgn(sy - sx);
        /* a run only once walking (or running) already, as U8AvatarMoverProcess */
        world_u8_set_speed(length == 0 ? 0.5 : length == 2 && (world_u8_was_moving() || run) ? 2.0 : 1.0);
        return 1;
    }
    return 0;
}

static int heldArrows[4]; /* left, right, up, down - the engine's arrows held now (main.c) */

void u8_walk_input(int left, int right, int up, int down, int *dx, int *dy)
{
    heldArrows[0] = left; heldArrows[1] = right; heldArrows[2] = up; heldArrows[3] = down;
    if (!u8_direction(left, right, up, down, dx, dy)) world_u8_set_speed(1.0);
}

/* TryStartU8Jump: J (the way pressed, else straight up where it faces) or both mouse buttons (toward the pointer; on the Avatar: up). */
static void start_jump(int fromMouse)
{
    int dx, dy, up;
    if (fromMouse)
    {
        int sx, sy, length = pointer_from_avatar(&sx, &sy);
        dx = sgn(sx + sy); dy = sgn(sy - sx); up = length <= 0;
    }
    else if (u8_direction(heldArrows[0], heldArrows[1], heldArrows[2], heldArrows[3], &dx, &dy)) up = 0;
    else { world_u8_facing_vector(&dx, &dy); up = 1; }
    world_u8_jump(dx, dy, up);
}

/* The mouse's part in moving: the right button held walks toward the pointer; both buttons jump. 1: the press was this. */
int u8_mouse(const PlatformEvent *e)
{
    if (!on) return 0;
    if (e->type == EV_MOUSE_MOVE || e->type == EV_MOUSE_DOWN) { mouseX = e->mouseX; mouseY = e->mouseY; }
    if (e->type == EV_MOUSE_UP && e->button == 1) { leftHeld = 0; u8_combat_mouse(e); } /* a block ends */
    if (e->type == EV_MOUSE_UP && e->button == 2) { rightHeld = 0; return 1; }
    if (e->type != EV_MOUSE_DOWN) return 0;
    if (e->button == 2)
    {
        if (u8_combat_mouse(e)) return 1; /* combat mode: a swing, a kick, a block (U8Combat) */
        rightHeld = 1;
        if (u8_left_down()) start_jump(1);
        return 1;
    }
    if (e->button == 1)
    {
        if (u8_combat_mouse(e)) return 1;
        leftHeld = 1;
        if (rightHeld) { start_jump(1); return 1; } /* both buttons: a jump */
    }
    return 0;
}
/* ---------------- Ultima 8's own world keys ---------------- */

/* A world key the Ultima 7 engine hasn't got: 1 when it was this file's. */
int u8_world_key(const char *actionId)
{
    if (!on || !actionId[0]) return 0;
    if (!strcmp(actionId, "Jump")) { start_jump(0); return 1; } /* Ultima 8: jump (both mouse buttons too) */
    /* Crusader's keys (U8CrusaderKey) */
    if (!strcmp(actionId, "Attack"))
    {
        if (vitals_incapacitated(world_actor_character(0))) return 1;
        if (!combat_mode()) { if (events_combat_forbidden()) return 1; combat_toggle_mode(); }
        combat_u8_swing(0, 0, 0); /* the way they face, or at the crosshair */
        return 1;
    }
    if (!strcmp(actionId, "NextTarget")) { combat_u8_next_target(); return 1; }
    if (!strcmp(actionId, "RollLeft")) { world_u8_dodge(1, 1); return 1; }
    if (!strcmp(actionId, "RollRight")) { world_u8_dodge(1, 0); return 1; }
    if (!strcmp(actionId, "SideStepLeft")) { world_u8_dodge(0, 1); return 1; }
    if (!strcmp(actionId, "SideStepRight")) { world_u8_dodge(0, 0); return 1; }
    if (!strcmp(actionId, "Kneel")) { world_u8_toggle_kneel(); return 1; }
    if (!strcmp(actionId, "ControlNext")) { u8_control_next(); return 1; }
    if (!strcmp(actionId, "ControlMain")) { u8_control_main(); return 1; }
    if (!strcmp(actionId, "Menu"))
    {
        /* ToggleKeysPaperdoll: the maker's keys paperdoll (System Settings > Ultima 8) opens, or closes if it's open */
        cJSON *id = get(get(get(project(), "Settings"), "Ultima8"), "KeysDollDesignId");
        int design = cJSON_IsNumber(id) ? id->valueint : -1;
        if (design < 0) return 1;
        if (!dolls_close_command("One paperdoll", NULL, design)) dolls_open_form(design, 0, 0, 0, 0);
        return 1;
    }
    return 0;
}

/* ---------------- The Avatar's Ultima 8 rules the movement asks about (filled in with U8Reactions, U8Liquids, U8Party, U8Crusader) ---------------- */

int u8_liquid_step_ok(int l, int t, int w, int d) { (void)l; (void)t; (void)w; (void)d; return 1; }

/* ---------------- Combat with the mouse (U8GameWindow.U8Combat U8CombatMouseDown / Up) ----------------
   In combat mode on the map: a double left click swings toward the pointer, a double right click kicks (on the Avatar: out of
   combat), the left button held blocks. A window takes its own clicks. */

static uint32_t lastLeft, lastRight; static int lastLeftX, lastLeftY, lastRightX, lastRightY;

static int double_click(uint32_t *last, int *lx, int *ly, int x, int y)
{
    uint32_t now = platform_ticks();
    int twice = *last && now - *last <= 500 && abs(x - *lx) <= 4 && abs(y - *ly) <= 4;
    *last = twice ? 0 : now; *lx = x; *ly = y;
    return twice;
}

int u8_combat_mouse(const PlatformEvent *e)
{
    if (e->type == EV_MOUSE_UP && e->button == 1) { if (combat_u8_blocking()) combat_u8_block(0); return 0; }
    if (!combat_mode() || e->type != EV_MOUSE_DOWN || vitals_game_over() || u8_stunned() || gumps_choosing()) return 0;
    if (gumps_window_at(e->mouseX, e->mouseY)) return 0; /* a window takes its own clicks */
    int sx, sy, dx, dy;
    if (e->button == 2)
    {
        if (!double_click(&lastRight, &lastRightX, &lastRightY, e->mouseX, e->mouseY)) return 0;
        int actor, uid;
        world_hit(e->mouseX, e->mouseY, &actor, &uid);
        if (actor == 0) { combat_toggle_mode(); return 1; } /* a double right click on the Avatar */
        pointer_from_avatar(&sx, &sy); dx = sgn(sx + sy); dy = sgn(sy - sx);
        combat_u8_swing(1, dx, dy); /* a kick */
        return 1;
    }
    if (e->button == 1)
    {
        if (double_click(&lastLeft, &lastLeftX, &lastLeftY, e->mouseX, e->mouseY))
        {
            combat_u8_block(0);
            pointer_from_avatar(&sx, &sy); dx = sgn(sx + sy); dy = sgn(sy - sx);
            combat_u8_swing(0, dx, dy);
            return 1;
        }
        if (!rightHeld) combat_u8_block(1); /* held: a block */
        return 1;
    }
    return 0;
}

/* ---------------- Hooks the combat asks about (filled in with U8Extras, U8Reactions, U8Crusader) ---------------- */

/* PlayerBackToPose: after a blow, back to standing - kneeling stays kneeling; in combat the combat stance. */
const char *u8_player_back_to_pose(int inCombat)
{
    if (u8_kneeling() && world_has_pose(0, "Kneel")) return "Kneel";
    return inCombat && world_has_pose(0, "Combat Stand") ? "Combat Stand" : "Idle";
}

/* ---------------- The pointer's arrows (U8GameWindow.Pointer UpdateU8Pointer, Pentagram's Mouse::getMouseFrame) ---------------- */

/* In combat mode the combat arrow toward the pointer (the running one while running); walking with the right button, the step,
   walk or run arrow by how far the pointer is. Its set and screen direction (0 up .. 7 up-left); 0: the resting pointer. */
int u8_pointer_arrow(const char **set, int *direction)
{
    int moving = rightHeld && !dialog_showing() && !events_cinema();
    if (!combat_mode() && !moving) return 0;
    int sx, sy, length = pointer_from_avatar(&sx, &sy);
    static const int DIR[3][3] = { { 7, 0, 1 }, { 6, -1, 2 }, { 5, 4, 3 } }; /* [sy + 1][sx + 1] */
    *direction = DIR[sy + 1][sx + 1];
    if (*direction < 0) *direction = 7;
    if (!combat_mode() && length < 0) return 0; /* too close to move: the hand */
    *set = combat_mode() && !(moving && length == 2) ? "Combat" : length <= 0 ? "Step" : length == 2 ? "Run" : "Walk"; /* the combat arrows are off while running */
    return 1;
}

/* ---------------- Stats grow by use, eating and healing, stumbling (U8GameWindow.U8Reactions) ---------------- */

static cJSON *u8_settings(void) { return get(get(project(), "Settings"), "Ultima8"); }
static int u8_flag(const char *k) { return cJSON_IsTrue(get(u8_settings(), k)); }
static int u8_num(const char *k, int fallback) { cJSON *v = get(u8_settings(), k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
int u8_stats_grow_by_use(void) { return on && u8_flag("StatsGrowByUse"); }
int u8_eating_as_u8(void) { return on && u8_flag("EatingAsUltima8"); }

static cJSON *character_of(int characterId)
{
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Characters"), "Characters")) if (cJSON_IsNumber(get(c, "Id")) && get(c, "Id")->valueint == characterId) return c;
    return NULL;
}

/* BuildUpStat: points toward a stat - only the main character and the party; a random chance each time, certain at the maker's number. */
static void build_up_stat(int actor, int kind, int points)
{
    int id = world_actor_character(actor);
    if (!u8_stats_grow_by_use() || points <= 0 || id < 0 || !(actor == 0 || items_in_party(id)) || vitals_dead(id)) return;
    cJSON *ch = character_of(id);
    const char *capKey = kind == 1 ? "U8MaxStrength" : kind == 2 ? "U8MaxDexterity" : "U8MaxIntelligence";
    int cap = cJSON_IsNumber(get(ch, capKey)) ? get(ch, capKey)->valueint : 25;
    int value = combat_stat(actor, kind, 0);
    if (cap > 0 && value >= cap) return;
    U8Vitals *v = vitals_u8(id);
    if (!v) return;
    int *built = kind == 1 ? &v->builtStrength : kind == 2 ? &v->builtDexterity : &v->builtIntelligence;
    int now = *built + points, need = u8_num("PointsForStatGain", 650);
    if (need < 1) need = 1;
    if (now >= need || rand() % (need - now + 1) == 0)
    {
        now = 0;
        combat_set_stat(actor, kind, value + 1);
        world_sound_from_actor(u8_num("StatGainSoundId", -1), actor);
        char line[160]; snprintf(line, sizeof line, "%s gains %s (%d).", world_actor_name(actor), kind == 1 ? "Strength" : kind == 2 ? "Dexterity" : "Intelligence", value + 1);
        game_log(line);
    }
    *built = now;
}

/* A swing or a kick: Strength 1-2, Dexterity 2-3. */
void u8_build_up_from_swing(int actor) { build_up_stat(actor, 1, 1 + rand() % 2); build_up_stat(actor, 2, 2 + rand() % 2); }

/* A blow that landed: Dexterity by how much better the attacker is; Strength by the damage, unless a kick. */
void u8_build_up_from_blow(int attacker, int defender, int damage, int kick)
{
    int attack = combat_stat(attacker, 2, 0), defend = combat_stat(defender, 2, 0);
    build_up_stat(attacker, 2, attack > defend ? 2 * (attack - defend) : 2);
    if (!kick) build_up_stat(attacker, 1, damage / 4);
}

/* Climbing a ledge this many lifts up: Strength 2 and up, Dexterity twice that. */
void u8_build_up_from_climb(int rise)
{
    int points = 2 + (rise - 2 > 0 ? rise - 2 : 0);
    build_up_stat(0, 1, points);
    build_up_stat(0, 2, 2 * points);
}

/* HealProcess: for the main character and the party - mana at the half minute, health at the minute; hunger builds. */
void u8_update_healing(double ms)
{
    for (int a = 0; a < world_actor_count(); a++)
    {
        int id = world_actor_character(a);
        if (id < 0 || !(a == 0 || items_in_party(id)) || vitals_dead(id) || world_actor_gone(a)) continue;
        U8Vitals *v = vitals_u8(id);
        if (!v) continue;
        double before = v->healClockMs;
        v->healClockMs += ms;
        if (before < 30000 && v->healClockMs >= 30000) { int mana = combat_stat(a, 7, 0); if (mana < combat_stat(a, 5, 0)) combat_set_stat(a, 7, mana + 1); }
        if (v->healClockMs < 60000) continue;
        v->healClockMs = 0;
        int hp;
        if (vitals_hp(id, &hp) && hp < vitals_max_hp(id)) vitals_set_hp(id, hp + 1);
        if (v->hunger < 200) v->hunger++;
    }
}

/* HealProcess::feedAvatar: eating takes hunger off, and gives back a quarter of it as health. */
void u8_feed(int characterId, int food)
{
    U8Vitals *v = vitals_u8(characterId);
    if (!v) return;
    if (food < 0) food = 0;
    if (food > v->hunger) food = v->hunger;
    if (food == 0) return;
    int old = v->hunger, hp;
    v->hunger -= food;
    if (vitals_hp(characterId, &hp)) { int to = hp - v->hunger / 4 + old / 4, most = vitals_max_hp(characterId); vitals_set_hp(characterId, to < most ? to : most); }
}

/* Actor::receiveHitU8: a blow that hurt and didn't kill - Stumble Backwards, if it has the art; not while blocking or knocked down. */
void u8_stumble_from_hit(int actor)
{
    int id = world_actor_character(actor);
    if (id < 0 || vitals_dead(id) || vitals_incapacitated(id) || !world_has_pose(actor, "Stumble Backwards")) return;
    if (actor == 0 && (combat_u8_blocking() || u8_stunned() || world_u8_jumping())) return;
    world_set_pose(actor, "Stumble Backwards", -1);
    int ms = world_pose_ms(actor, "Stumble Backwards");
    if (actor == 0) combat_pose_back(0, ms);
    else schedules_wait_at_least(actor, ms);
}

int u8_build_up_stat_command(int whoCharacter, const char *stat, int points)
{
    int actor = world_actor_by_character(whoCharacter);
    if (actor < 0) return -1;
    build_up_stat(actor, !strcmp(stat, "Strength") ? 1 : !strcmp(stat, "Dexterity") ? 2 : 3, points);
    return 1;
}

/* A way held (the walking keys or the right button): hanging from a ledge. */
int u8_direction_held(void) { return heldArrows[0] || heldArrows[1] || heldArrows[2] || heldArrows[3] || rightHeld; }
static int dropArrows;
void u8_drop_arrows(void) { dropArrows = 1; }
int u8_take_drop_arrows(void) { int d = dropArrows; dropArrows = 0; return d; }

/* ---------------- Ultima 8's and Crusader's extras (U8GameWindow.U8Extras) ----------------
   Move Platform, Weight On, Flip Screen, Speak, Reading Screen, Keypad, Computer, Recharge, Alarm; difficulty, energy shields,
   surrender; By Coming Into View / By Leaving View. A command that takes its time waits on its token (u8_update finishes it). */

enum { WAIT_PLATFORM = 1, WAIT_FLIP, WAIT_TIMER, WAIT_RECHARGE };
typedef struct U8Wait { int kind, token, thing, dir, left, actor, mana; double msPer, since, ms, perSecond, carry; } U8Wait;
static U8Wait waits[16];

static U8Wait *new_wait(int kind, int token)
{
    for (int i = 0; i < 16; i++) if (!waits[i].kind) { memset(&waits[i], 0, sizeof waits[i]); waits[i].kind = kind; waits[i].token = token; return &waits[i]; }
    return NULL;
}
static void finish_wait(U8Wait *w, int ok) { int token = w->token; w->kind = 0; if (token) events_doll_answered(token, ok); }

/* Flip Screen: the world turned upside down and back (ViewportHost's ScaleY from -1 to 1). */
static double flipScale = 1, flipFrom = 1, flipTo = 1;
static int flipped;

/* The screens: a reading screen, a keypad, a computer - drawn over everything, they take the clicks and keys. */
enum { SCREEN_NONE, SCREEN_READING, SCREEN_KEYPAD, SCREEN_COMPUTER };
static struct { int kind, token; const Sprite *sprite; char words[1024], code[64], typed[16], variable[64], prompt[160]; double msPerLetter, since; int shown; } scr;

static void close_screen(int ok)
{
    int token = scr.token;
    if (scr.kind == SCREEN_KEYPAD && scr.variable[0]) script_set(scr.variable, value_text(scr.typed));
    if (scr.kind == SCREEN_KEYPAD) ok = ok && !strcmp(scr.typed, scr.code);
    scr.kind = SCREEN_NONE; scr.token = 0;
    if (token) events_doll_answered(token, ok);
}

int u8_screen_open(void) { return scr.kind != SCREEN_NONE; }

/* textOf: the command's words (a formula's text, from events.c) */
static const char *text_of(cJSON *params, const char *key, double (*formula)(const char *), char *buf, size_t size)
{
    (void)formula;
    snprintf(buf, size, "%s", textOf ? textOf(key) : str(params, key));
    return buf;
}

int u8_extras_command(int type, cJSON *params, int whoCharacter, int token, double (*formula)(const char *key), int thingUid)
{
    int who = whoCharacter >= 0 ? world_actor_by_character(whoCharacter) : -1;
    char buf[1024];
    switch (type)
    {
        case C_U8MovePlatform:
        {
            int lifts = formula ? (int)formula("Lifts") : 0;
            if (thingUid <= 0 || lifts == 0) return -1;
            double seconds = formula ? formula("Seconds") : 0, msPer = seconds * 1000 / abs(lifts);
            int wait = strcmp(str(params, "Wait"), "False") != 0;
            U8Wait *w = new_wait(WAIT_PLATFORM, wait ? token : 0);
            if (!w) return -1;
            w->thing = thingUid; w->dir = lifts > 0 ? 1 : -1; w->left = abs(lifts); w->msPer = msPer > 10 ? msPer : 10;
            return wait ? 0 : 1;
        }
        case C_U8WeightOn:
            if (thingUid <= 0) return -1;
            script_set_number(str(params, "Variable"), world_weight_on(thingUid));
            return 1;
        case C_U8FlipScreen:
        {
            const char *v = str(params, "Value");
            int to = !strcmp(v, "Upside down") ? 1 : !strcmp(v, "Right way up") ? 0 : !flipped;
            double ms = formula ? formula("Seconds") * 1000 : 0;
            U8Wait *w = new_wait(WAIT_FLIP, token);
            if (!w) return -1;
            flipFrom = flipped ? -1 : 1; flipTo = to ? -1 : 1; flipped = to;
            w->ms = ms > 1 ? ms : 1;
            return 0;
        }
        case C_U8Speak:
        {
            if (who < 0) return -1;
            double seconds = formula ? formula("Seconds") : 0;
            if (seconds < 0.5) seconds = 0.5;
            world_bark(who, text_of(params, "Value", formula, buf, sizeof buf), (int)(seconds * 1000));
            if (str(params, "SoundId")[0]) world_sound_from_actor(atoi(str(params, "SoundId")), who);
            if (strcmp(str(params, "Wait"), "True")) return 1;
            U8Wait *w = new_wait(WAIT_TIMER, token);
            if (!w) return 1;
            w->ms = seconds * 1000;
            return 0;
        }
        case C_U8ReadingScreen: case C_U8Keypad: case C_U8Computer:
        {
            if (scr.kind) close_screen(0);
            memset(&scr, 0, sizeof scr);
            scr.kind = type == C_U8ReadingScreen ? SCREEN_READING : type == C_U8Keypad ? SCREEN_KEYPAD : SCREEN_COMPUTER;
            scr.token = token;
            if (type == C_U8ReadingScreen) { scr.sprite = sprite_find(str(params, "SpriteGroup"), str(params, "SpriteName")); text_of(params, "Value", formula, scr.words, sizeof scr.words); }
            if (type == C_U8Keypad)
            {
                text_of(params, "Code", formula, scr.code, sizeof scr.code);
                snprintf(scr.variable, sizeof scr.variable, "%s", str(params, "Variable"));
                snprintf(scr.prompt, sizeof scr.prompt, "%s", str(params, "Prompt"));
            }
            if (type == C_U8Computer)
            {
                text_of(params, "Value", formula, scr.words, sizeof scr.words);
                scr.msPerLetter = formula ? formula("MsPerLetter") : 1;
                if (scr.msPerLetter < 1) scr.msPerLetter = 1;
            }
            return 0;
        }
        case C_U8Recharge:
        {
            if (who < 0) return -1;
            U8Wait *w = new_wait(WAIT_RECHARGE, token);
            if (!w) return -1;
            w->actor = who; w->mana = !strcmp(str(params, "Stat"), "Mana");
            w->perSecond = formula ? formula("PerSecond") : 0.1; if (w->perSecond < 0.1) w->perSecond = 0.1;
            w->ms = formula ? formula("Seconds") * 1000 : 0; if (w->ms < 0) w->ms = 0;
            return 0;
        }
        case C_U8ControlCharacter: case C_U8ReturnControl: return u8_control_command(type, params, whoCharacter);
        case C_U8Alarm:
        {
            if (who < 0 || !str(params, "Faction")[0]) return -1;
            int faction = atoi(str(params, "Faction")), count = 0;
            double cells = formula ? formula("Cells") : 1, x, y, ox, oy;
            if (cells < 1) cells = 1;
            world_actor_pixel(who, &x, &y);
            for (int a = 1; a < world_actor_count(); a++)
            {
                int id = world_actor_character(a);
                cJSON *ch = character_of(id);
                if (a == who || id < 0 || world_actor_gone(a) || vitals_dead(id) || vitals_has(id, 512) || !cJSON_IsNumber(get(ch, "FactionId")) || get(ch, "FactionId")->valueint != faction) continue;
                world_actor_pixel(a, &ox, &oy);
                if (fmax(fabs(ox - x) / world_tile_w(), fabs(oy - y) / world_tile_h()) > cells) continue;
                combat_start(a, who);
                count++;
            }
            return count > 0 ? 1 : -1;
        }
    }
    return -2;
}

/* Each frame the game runs: the commands that take their time. */
void u8_update(double ms)
{
    if (!on) return;
    for (int i = 0; i < 16; i++)
    {
        U8Wait *w = &waits[i];
        if (!w->kind) continue;
        switch (w->kind)
        {
            case WAIT_PLATFORM:
                w->since += ms;
                while (w->since >= w->msPer && w->left > 0) { w->since -= w->msPer; w->left--; world_raise_platform(w->thing, w->dir); }
                if (w->left <= 0) finish_wait(w, 1);
                break;
            case WAIT_FLIP:
                w->since = fmin(w->ms, w->since + ms);
                flipScale = flipFrom + (flipTo - flipFrom) * w->since / w->ms;
                if (w->since >= w->ms) finish_wait(w, 1);
                break;
            case WAIT_TIMER:
                if ((w->since += ms) >= w->ms) finish_wait(w, 1);
                break;
            case WAIT_RECHARGE:
            {
                int id = world_actor_character(w->actor);
                if (id < 0 || vitals_dead(id)) { finish_wait(w, 0); break; }
                w->carry += w->perSecond * ms / 1000;
                int whole = (int)w->carry, now, most;
                w->carry -= whole;
                if (w->mana) { now = combat_stat(w->actor, 7, 0); most = combat_stat(w->actor, 5, 0); }
                else { now = 0; vitals_hp(id, &now); most = vitals_max_hp(id); }
                if (whole > 0) { now = now + whole < most ? now + whole : most; if (w->mana) combat_set_stat(w->actor, 7, now); else vitals_set_hp(id, now); }
                w->ms -= ms;
                if (now >= most || w->ms <= 0) finish_wait(w, 1);
                break;
            }
        }
    }
    if (scr.kind == SCREEN_COMPUTER && scr.shown < (int)strlen(scr.words))
        for (scr.since += ms; scr.since >= scr.msPerLetter && scr.shown < (int)strlen(scr.words); scr.since -= scr.msPerLetter) scr.shown++;
    world_u8_liquids(ms); /* water and lava (U8Liquids) */
    u8_update_view_events(ms);
}

/* Flip Screen: the frame turned upside down by the scale now (about its middle row). */
void u8_flip_frame(uint32_t *pixels)
{
    if (!on || flipScale > 0.999) return;
    static uint32_t *copy;
    if (!copy) copy = (uint32_t *)malloc(sizeof(uint32_t) * FRAME_PIXELS);
    if (!copy) return;
    memcpy(copy, pixels, sizeof(uint32_t) * FRAME_PIXELS);
    double mid = FRAME_H / 2.0;
    for (int y = 0; y < FRAME_H; y++)
    {
        double sy = fabs(flipScale) < 0.001 ? -1 : mid + (y + 0.5 - mid) / flipScale;
        uint32_t *row = pixels + (size_t)y * FRAME_W;
        if (sy < 0 || sy >= FRAME_H) { for (int x = 0; x < FRAME_W; x++) row[x] = 0xFF000000u; continue; }
        memcpy(row, copy + (size_t)(int)sy * FRAME_W, sizeof(uint32_t) * FRAME_W);
    }
}

/* ---- The screens drawn and clicked ---- */

static const int KP_W = 22, KP_H = 16; /* a keypad key */
static void keypad_layout(int *x0, int *y0, int *w, int *h)
{
    const Font *f = font_get("InGameFont");
    int lh = font_line_height(f), promptH = scr.prompt[0] ? lh + 2 : 0;
    *w = 3 * KP_W + 6; *h = promptH + lh + 6 + 4 * KP_H + 4;
    if (scr.prompt[0] && font_text_width(f, scr.prompt) + 6 > *w) *w = font_text_width(f, scr.prompt) + 6;
    *x0 = (SCREEN_W - *w) / 2; *y0 = (SCREEN_H - *h) / 2;
}

void u8_draw_screen(uint32_t *pixels)
{
    if (!on || !scr.kind) return;
    const Font *f = font_get("InGameFont");
    if (scr.kind == SCREEN_READING)
    {
        const Picture *p = scr.sprite && scr.sprite->frameCount ? frame_picture(&scr.sprite->frames[0]) : NULL;
        if (p) { int qw = (p->width + 7) / 8 * 8, qh = (p->height + 7) / 8 * 8; world_blit(pixels, p, (SCREEN_W - qw) / 2, (SCREEN_H - qh) / 2, qw, qh); }
        int width = SCREEN_W - 40, lines = font_line_count(f, scr.words, width), bw = font_block_width(f, scr.words, width);
        font_draw_block(f, pixels, (SCREEN_W - bw) / 2, (SCREEN_H - lines * font_line_height(f)) / 2, scr.words, width, 0);
        return;
    }
    if (scr.kind == SCREEN_COMPUTER)
    {
        int w = 260, h = 150, x = (SCREEN_W - w) / 2, y = (SCREEN_H - h) / 2;
        frame_fill(pixels, x, y, w, h, 0xFF006400u);
        frame_fill(pixels, x + 2, y + 2, w - 4, h - 4, 0xFF000000u);
        char shown[1024]; snprintf(shown, sizeof shown, "%.*s", scr.shown, scr.words);
        font_draw_block(f, pixels, x + 8, y + 8, shown, w - 16, 0xFF90EE90u);
        return;
    }
    int x0, y0, w, h;
    keypad_layout(&x0, &y0, &w, &h);
    frame_fill(pixels, x0, y0, w, h, 0xFF696969u);
    int y = y0 + 2, lh = font_line_height(f);
    if (scr.prompt[0]) { font_draw(f, pixels, x0 + (w - font_text_width(f, scr.prompt)) / 2, y, scr.prompt, 0); y += lh + 2; }
    frame_fill(pixels, x0 + 3, y, w - 6, lh + 4, 0xFF000000u);
    char stars[16]; int n = (int)strlen(scr.typed); for (int i = 0; i < n; i++) stars[i] = '*'; stars[n] = 0;
    font_draw(f, pixels, x0 + (w - font_text_width(f, stars)) / 2, y + 2, stars, 0xFF90EE90u);
    y += lh + 6;
    static const char *FACES[12] = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "X", "0", "OK" };
    for (int i = 0; i < 12; i++)
    {
        int kx = x0 + 3 + (i % 3) * KP_W, ky = y + (i / 3) * KP_H;
        frame_fill(pixels, kx, ky, KP_W - 1, KP_H - 1, 0xFFD3D3D3u);
        font_draw(f, pixels, kx + (KP_W - 1 - font_text_width(f, FACES[i])) / 2, ky + (KP_H - 1 - lh) / 2, FACES[i], 0xFF000000u);
    }
}

static void keypad_press(const char *digit) { size_t n = strlen(scr.typed); if (n < 12) { scr.typed[n] = digit[0]; scr.typed[n + 1] = 0; } }

/* A click while a screen is up: it's the screen's (1). */
int u8_screen_click(int x, int y)
{
    if (!on || !scr.kind) return 0;
    if (scr.kind == SCREEN_READING) { close_screen(1); return 1; }
    if (scr.kind == SCREEN_COMPUTER)
    {
        int len = (int)strlen(scr.words);
        if (scr.shown < len) scr.shown = len; else close_screen(1);
        return 1;
    }
    int x0, y0, w, h, lh = font_line_height(font_get("InGameFont"));
    keypad_layout(&x0, &y0, &w, &h);
    int ky0 = y0 + 2 + (scr.prompt[0] ? lh + 2 : 0) + lh + 6;
    if (x < x0 + 3 || y < ky0) return 1;
    int col = (x - x0 - 3) / KP_W, row = (y - ky0) / KP_H;
    if (col < 0 || col > 2 || row < 0 || row > 3) return 1;
    static const char *FACES[12] = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "X", "0", "OK" };
    const char *face = FACES[row * 3 + col];
    if (!strcmp(face, "OK")) close_screen(1);
    else if (!strcmp(face, "X")) close_screen(0);
    else keypad_press(face);
    return 1;
}

/* A key while a screen is up: the keypad's digits, Accept, Cancel; any key puts a reading screen or a computer away. */
int u8_screen_key(const PlatformEvent *e)
{
    if (!on || !scr.kind || e->type != EV_KEY_DOWN) return 0;
    if (scr.kind != SCREEN_KEYPAD) { u8_screen_click(0, 0); return 1; }
    if (u8_key_is(e, "Accept")) { close_screen(1); return 1; }
    if (u8_key_is(e, "Cancel")) { close_screen(0); return 1; }
    const char *k = e->keyName;
    size_t n = strlen(k);
    if (n >= 2 && k[n - 1] >= '0' && k[n - 1] <= '9' && (n == 2 || !strncmp(k, "NumPad", 6))) { char d[2] = { k[n - 1], 0 }; keypad_press(d); }
    return 1;
}

/* ---- Blows: difficulty, energy shields, surrender ---- */

/* Item::scaleReceivedDamageCru: by the variable the maker names (1, 2, 3...), what the party takes and what others take. */
int u8_after_difficulty(int characterId, int damage)
{
    const char *name = str(u8_settings(), "DifficultyVariable");
    while (*name == ' ') name++;
    if (damage <= 0 || !name[0]) return damage;
    int level = (int)events_variable(name), percent = 100;
    int party = world_actor_by_character(characterId) == 0 || items_in_party(characterId);
    char list[256]; snprintf(list, sizeof list, "%s", str(u8_settings(), party ? "DifficultyPartyTakes" : "DifficultyOthersTake"));
    int i = 1;
    for (char *p = strtok(list, ","); p; p = strtok(NULL, ","), i++)
        if (i == level) { while (*p == ' ') p++; char *end; long v = strtol(p, &end, 10); if (end != p) percent = v > 0 ? (int)v : 0; break; }
    if (percent == 100) return damage;
    int scaled = damage * percent / 100, least = damage > 0 && percent > 0 ? 1 : 0;
    return scaled > least ? scaled : least;
}

/* MainActor's shields: this part of a blow comes off its Mana, as long as there is any. */
int u8_after_energy_shield(int characterId, int damage)
{
    cJSON *ch = character_of(characterId);
    int percent = cJSON_IsNumber(get(ch, "U8ShieldPercent")) ? get(ch, "U8ShieldPercent")->valueint : 0;
    int actor = world_actor_by_character(characterId);
    if (damage <= 0 || percent <= 0 || actor < 0) return damage;
    int mana = combat_stat(actor, 7, 0), absorbed = damage * (percent < 100 ? percent : 100) / 100;
    if (absorbed > mana) absorbed = mana;
    if (absorbed <= 0) return damage;
    combat_set_stat(actor, 7, mana - absorbed);
    return damage - absorbed;
}

/* SurrenderProcess: hurt below its point, it gives up - it stops fighting and says so. It can still be struck. */
void u8_maybe_surrender(int actor)
{
    int id = world_actor_character(actor), hp, max;
    cJSON *ch = character_of(id);
    int percent = cJSON_IsNumber(get(ch, "U8SurrenderPercent")) ? get(ch, "U8SurrenderPercent")->valueint : 0;
    if (percent <= 0 || actor <= 0 || items_in_party(id) || vitals_dead(id) || vitals_has(id, 512)) return;
    max = vitals_max_hp(id);
    if (max <= 0 || !vitals_hp(id, &hp) || hp * 100 >= max * percent) return;
    vitals_inflict(id, 512);
    combat_stop_fighting(actor);
    world_walk_cancel(actor);
    if (world_has_pose(actor, "Surrender")) world_set_pose(actor, "Surrender", -1);
    char words[160]; snprintf(words, sizeof words, "%s", str(ch, "U8SurrenderWords"));
    char *w = words; while (*w == ' ') w++;
    size_t n = strlen(w); while (n && w[n - 1] == ' ') w[--n] = 0;
    if (*w) world_bark(actor, w, 3000);
}

/* ---- Coming into and leaving view (Item::enterFastArea, leaveFastArea): twice a second ---- */

static double viewCheckMs;
static unsigned char inView[1024];

void u8_update_view_events(double ms)
{
    if ((viewCheckMs += ms) < 500) return;
    viewCheckMs = 0;
    for (int a = 1; a < world_actor_count() && a < 1024; a++)
    {
        int id = world_actor_character(a);
        if (id < 0 || world_actor_gone(a) || !(events_character_has_trigger(id, 23) || events_character_has_trigger(id, 24))) continue;
        double x, y;
        world_actor_pixel(a, &x, &y);
        int now = world_on_screen(x, y, 6 * 8);
        if (now && !inView[a]) { inView[a] = 1; events_fire_character(id, 23); }
        else if (!now && inView[a]) { inView[a] = 0; events_fire_character(id, 24); }
    }
}

/* ---------------- The party (U8GameWindow.U8Party) ---------------- */

/* PartyHasRoomFor: the most who can join (System Settings > Ultima 8 > The party); one more asking says its Party is full words. */
int u8_party_full(int characterId)
{
    if (!on) return 0;
    int most = u8_num("MaxPartyMembers", 0), members = 0;
    if (most <= 0) return 0;
    int mainId = world_actor_character(0);
    cJSON *c;
    cJSON_ArrayForEach(c, get(get(project(), "Characters"), "Characters"))
    {
        int id = cJSON_IsNumber(get(c, "Id")) ? get(c, "Id")->valueint : -1;
        if (id >= 0 && id != mainId && id != characterId && items_in_party(id) && !vitals_dead(id)) members++;
    }
    if (members < most) return 0;
    if (!vitals_bark(characterId, 28))
    {
        int actor = world_actor_by_character(characterId);
        if (actor >= 0) world_bark(actor, "Thy party is full. I cannot join thee.", 3000);
    }
    char line[160]; snprintf(line, sizeof line, "The party is full (%d): %s doesn't join.", most, world_actor_name(world_actor_by_character(characterId)));
    game_log(line);
    return 1;
}

/* ---------------- Crusader: who the player controls (U8GameWindow.U8Crusader) ----------------
   The keys switch between the party's members; Control Character takes any character, Return Control gives the main character
   back. The one controlled is "the player" for everything. The one left stands where it was: a party member follows, anyone else
   goes on with its own day. Saves are written as if the main character were controlled, and say who was. */

static int mainCharacter = -1, mainWasInParty, controlLocked;

int u8_main_character(void) { return mainCharacter >= 0 ? mainCharacter : world_actor_character(0); }
int u8_controlling_other(void) { return on && mainCharacter >= 0 && world_actor_character(0) != mainCharacter; }

static const char *why_cannot_control(int target, int force)
{
    static char why[160];
    int actor = world_actor_by_character(target);
    const char *name = actor >= 0 ? world_actor_name(actor) : "That one";
    if (actor < 0 || world_actor_gone(actor)) { snprintf(why, sizeof why, "%s isn't here.", name); return why; }
    if (force) return NULL;
    if (vitals_game_over()) return "The game is over.";
    if (vitals_dead(target)) { snprintf(why, sizeof why, "%s is dead.", name); return why; }
    if (vitals_unconscious(target)) { snprintf(why, sizeof why, "%s is knocked out.", name); return why; }
    if (vitals_incapacitated(target)) { snprintf(why, sizeof why, "%s can't move now (asleep or paralyzed).", name); return why; }
    if (events_actor_busy(actor)) { snprintf(why, sizeof why, "%s is busy.", name); return why; }
    if (vehicles_driving()) return "Not while driving.";
    if (world_u8_in_air()) return "Not in mid-air.";
    return NULL;
}

/* SwapControlTo: the player controls this character from now on. */
static int swap_control_to(int target, int quiet, int force)
{
    int now = world_actor_character(0);
    if (target == now) return 1;
    const char *why = why_cannot_control(target, force);
    if (why) { if (!quiet) game_log(why); return 0; }
    int k = world_actor_by_character(target);
    if (mainCharacter < 0) mainCharacter = now;
    int mainId = mainCharacter;
    if (world_seated(0)) world_stand_up(0);
    if (world_seated(k)) world_stand_up(k);
    u8_release_all();
    if (now == mainId) { mainWasInParty = items_in_party(mainId); items_set_in_party(mainId, 1); }
    if (!world_u8_swap(k)) return 0;
    combat_u8_control_changed(k);
    schedules_rebind(k);           /* the one left: a character on the map, with its own day (or following) */
    if (target == mainId) items_set_in_party(mainId, mainWasInParty);
    /* who follows whom: the party the one controlled, if it is of the party; else the main character */
    int ofParty = target == mainId || items_in_party(target), mainActor = world_actor_by_character(mainId);
    for (int a = 1; a < world_actor_count(); a++)
    {
        int id = world_actor_character(a);
        if (id < 0 || world_actor_gone(a) || vitals_dead(id) || !items_in_party(id)) continue;
        if (!ofParty && id == mainId) { schedules_follow(a, -1); continue; } /* waits where it stood */
        schedules_follow(a, ofParty ? 0 : mainActor);
    }
    if (!quiet) { char line[160]; snprintf(line, sizeof line, target == mainId ? "You control %s again." : "You control %s.", world_actor_name(0)); game_log(line); }
    return 1;
}

/* ControlNextMember: the main character, then each member here, round again. */
void u8_control_next(void)
{
    if (controlLocked) { game_log("You can't switch now."); return; }
    int mainId = u8_main_character(), order[256], n = 0, now = world_actor_character(0);
    order[n++] = mainId;
    for (int a = 1; a < world_actor_count() && n < 255; a++)
    {
        int id = world_actor_character(a);
        if (id >= 0 && id != mainId && !world_actor_gone(a) && items_in_party(id) && !vitals_dead(id) && !vitals_incapacitated(id)) order[n++] = id;
    }
    int at = -1;
    for (int i = 0; i < n; i++) if (order[i] == now) at = i;
    if (at < 0) { order[n] = now; at = n++; } /* controlled by an event, not of the party */
    if (n < 2) { game_log("Nobody else in the party to control."); return; }
    for (int step = 1; step < n; step++) if (swap_control_to(order[(at + step) % n], 0, 0)) return;
}

void u8_control_main(void)
{
    if (controlLocked) { game_log("You can't switch now."); return; }
    if (u8_controlling_other()) swap_control_to(mainCharacter, 0, 0);
}

/* ControlBackOnDeath: the one controlled, or the main character, died - the main character is controlled again first. */
void u8_control_back_on_death(int dyingCharacter)
{
    if (!u8_controlling_other() || (dyingCharacter != world_actor_character(0) && dyingCharacter != mainCharacter)) return;
    controlLocked = 0;
    swap_control_to(mainCharacter, 1, 1);
    if (dyingCharacter != mainCharacter) { char line[160]; snprintf(line, sizeof line, "%s is lost - you control %s again.", world_actor_name(world_actor_by_character(dyingCharacter)), world_actor_name(0)); game_log(line); }
}

/* ControlBackForTravel: to another world, the main character goes, controlled again. */
void u8_control_back(void) { controlLocked = 0; if (u8_controlling_other()) swap_control_to(mainCharacter, 1, 1); }

/* Saves: written as if the main character were controlled (u8_save_begin), then control as it was (u8_save_end). */
static int savedControlled = -1, savedLocked, savedKneel;
void u8_save_begin(void)
{
    savedControlled = -1;
    if (!u8_controlling_other()) return;
    savedControlled = world_actor_character(0); savedLocked = controlLocked; savedKneel = u8_kneeling();
    swap_control_to(mainCharacter, 1, 1);
}
int u8_saved_controlled(void) { return savedControlled; }
int u8_saved_locked(void) { return savedLocked; }
void u8_save_end(void)
{
    if (savedControlled < 0) return;
    swap_control_to(savedControlled, 1, 1);
    controlLocked = savedLocked;
    if (savedKneel) u8_kneel(1);
    savedControlled = -1;
}

/* RestoreControl: a game loaded - who the player controlled. */
void u8_restore_control(int controlledCharacter, int locked)
{
    mainCharacter = -1; controlLocked = 0;
    if (controlledCharacter < 0) return;
    if (swap_control_to(controlledCharacter, 1, 1)) controlLocked = locked;
}

void u8_reset_control(void) { mainCharacter = -1; controlLocked = 0; mainWasInParty = 0; }

/* Control Character, Return Control. */
int u8_control_command(int type, cJSON *params, int whoCharacter)
{
    if (type == C_U8ControlCharacter)
    {
        if (whoCharacter < 0) return -1;
        if (whoCharacter != world_actor_character(0) && !swap_control_to(whoCharacter, 0, 0)) return -1;
        controlLocked = strcmp(str(params, "Lock"), "False") != 0 && whoCharacter != u8_main_character();
        return 1;
    }
    controlLocked = 0;
    return !u8_controlling_other() || swap_control_to(mainCharacter, 0, 1) ? 1 : -1;
}

/* The save's own part: who the player controlled (Crusader). */
void u8_save(cJSON *runtime)
{
    if (!on || !runtime || u8_saved_controlled() < 0) return;
    cJSON_AddNumberToObject(runtime, "U8Controlled", u8_saved_controlled());
    cJSON_AddBoolToObject(runtime, "U8ControlLocked", u8_saved_locked());
}

void u8_restore(cJSON *runtime)
{
    if (!on) return;
    cJSON *c = get(runtime, "U8Controlled");
    u8_restore_control(cJSON_IsNumber(c) ? c->valueint : -1, cJSON_IsTrue(get(runtime, "U8ControlLocked")));
}
