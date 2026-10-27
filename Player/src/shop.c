/*
 * The Shop command, as the Windows engine runs it (GameTestWindow.Shop.cs, Engine/ShopModel.cs): the merchant's lines and the
 * player's choices step by step down its talk; a thing picked - its price asked (yes / no / haggle: ShopHaggle.Offer, Serpent
 * Isle's); bought - paid in its money from the party, carried by the main character or someone in the party who can; selling -
 * what he buys, from the party. Every word is the shop's own. The stock sold is written back into the command (a save keeps it).
 */
#include "shop.h"
#include "dialog.h"
#include "slider.h"
#include "items.h"
#include "world.h"
#include "vitals.h"
#include "pictures.h"
#include <math.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#define MAX_CHOICES 64

static struct
{
    int open;
    cJSON *params, *shop, *node, *parent, *item;
    int speaker, asking, tries, lastPrice, sellPick;
    void (*done)(void *);
    void *context;
    /* what each reply of the box up now does */
    int kinds[MAX_CHOICES];
    cJSON *targets[MAX_CHOICES];
} s;

enum { R_CHILD, R_ITEM, R_SELL, R_BACK, R_LEAVE, R_YES, R_NO, R_HAGGLE, R_SELL_PICK, R_SELL_BACK };

static cJSON *get(cJSON *o, const char *k) { return o ? cJSON_GetObjectItem(o, k) : NULL; }
static int num(cJSON *o, const char *k, int fallback) { cJSON *v = get(o, k); return cJSON_IsNumber(v) ? v->valueint : fallback; }
static const char *str(cJSON *o, const char *k) { const char *x = cJSON_GetStringValue(get(o, k)); return x ? x : ""; }
static const char *text(const char *k) { return str(get(s.shop, "Texts"), k); }

/* {item}, {price} and {money} filled in. */
static void fill(char *out, size_t size, const char *t, const char *item, int price, const char *money)
{
    size_t n = 0;
    out[0] = 0;
    while (*t && n + 1 < size)
    {
        const char *with = NULL; char number[16]; int skip = 0;
        if (!strncmp(t, "{item}", 6)) { with = item; skip = 6; }
        else if (!strncmp(t, "{price}", 7)) { snprintf(number, sizeof number, "%d", price); with = number; skip = 7; }
        else if (!strncmp(t, "{money}", 7)) { with = money; skip = 7; }
        if (with) { size_t w = strlen(with); if (n + w >= size) w = size - n - 1; memcpy(out + n, with, w); n += w; t += skip; continue; }
        out[n++] = *t++;
    }
    out[n] = 0;
}

static const char *item_name(cJSON *thing) { const char *n = items_name(num(thing, "Category", CAT_OBJECT), num(thing, "ItemId", -1)); return n ? n : "?"; }
static const char *money_name(void) { const char *n = items_name(CAT_OBJECT, num(s.shop, "CurrencyObjectId", -1)); return n && n[0] ? n : "gold"; }

static void end_shop(void);
static void chosen(void *context, int reply);

/* A box from the merchant: words, and the replies (none: a line read and gone). */
static int box(const char *words, const char **replies, int count)
{
    if (s.speaker > 0 && !world_actor_in_sight(s.speaker)) return 0; /* out of sight: their line isn't shown */
    DialogBox b;
    memset(&b, 0, sizeof b);
    b.text = words; b.title = "";
    b.face = s.speaker >= 0 ? world_actor_face(s.speaker) : NULL;
    b.position = 2;
    b.frame = sprite_dialog_frame("");
    b.replyCount = count;
    for (int i = 0; i < count && i < DIALOG_MAX_REPLIES; i++) { b.replies[i] = replies[i]; b.replyIds[i] = i; }
    if (count) { b.chosen = chosen; b.context = NULL; }
    dialog_queue(&b);
    return 1;
}

static void say(const char *words) { if (words[0]) box(words, NULL, 0); }
static void ask(const char *words, const char **replies, int count) { if (!box(words, replies, count)) end_shop(); }

static cJSON *parent_of(cJSON *root, cJSON *node)
{
    cJSON *child;
    cJSON_ArrayForEach(child, get(root, "Children"))
    {
        if (child == node) return root;
        cJSON *found = parent_of(child, node);
        if (found) return found;
    }
    return NULL;
}

/* A step of the talk: its line; the choices after it, its goods, Sell (at the start), Back or Leave. */
static void show_node(cJSON *node, cJSON *parent)
{
    s.node = node; s.parent = parent;
    const char *replies[MAX_CHOICES];
    int n = 0;
    cJSON *e;
    cJSON_ArrayForEach(e, get(node, "Children")) if (n < MAX_CHOICES - 3) { replies[n] = str(e, "Label"); s.kinds[n] = R_CHILD; s.targets[n++] = e; }
    cJSON_ArrayForEach(e, get(node, "Items")) if (n < MAX_CHOICES - 3) { replies[n] = item_name(e); s.kinds[n] = R_ITEM; s.targets[n++] = e; }
    if (!parent && cJSON_GetArraySize(get(s.shop, "Buys")) > 0) { replies[n] = text("SellChoice"); s.kinds[n++] = R_SELL; }
    if (parent) { replies[n] = text("Back"); s.kinds[n++] = R_BACK; }
    replies[n] = text("Leave"); s.kinds[n++] = R_LEAVE;
    ask(str(node, "Line"), replies, n);
}

static int party_count(int category, int itemId) { return items_party_count_any(category, itemId); }

static int member(int k) /* the k-th of the party: the main character first */
{
    for (int a = 0, i = 0; a < world_actor_count(); a++)
        if ((a == 0 || (!world_actor_gone(a) && items_in_party(world_actor_character(a)))) && !vitals_dead(world_actor_character(a))) { if (i++ == k) return a; }
    return -1;
}

/* So many taken from the party: from the main character first, then the others. */
static void take_from_party(int category, int itemId, int quantity)
{
    for (int k = 0; quantity > 0; k++)
    {
        int a = member(k);
        if (a < 0) break;
        int id = world_actor_character(a), has = items_count_any(id, category, itemId), take = has < quantity ? has : quantity, taken = 0;
        if (take <= 0) continue;
        Item *out[128];
        int n = items_take(id, category, itemId, take, out, 128, &taken);
        for (int i = 0; i < n; i++) items_free(out[i]);
        quantity -= taken;
    }
}

/* Carried by the main character or someone in the party who can. */
static int hand_to_party(Item *it)
{
    for (int k = 0;; k++) { int a = member(k); if (a < 0) return 0; if (items_give_to(world_actor_character(a), it)) return 1; }
}

static void buy(cJSON *item, int price)
{
    char line[512];
    const char *name = item_name(item), *money = money_name();
    int currency = num(s.shop, "CurrencyObjectId", -1);
    if (party_count(CAT_OBJECT, currency) < price) { fill(line, sizeof line, text("NotEnoughMoney"), name, price, money); say(line); return; }
    Item *gifts[100];
    int n = items_make_gifts(num(item, "Category", CAT_OBJECT), num(item, "ItemId", -1), 1, gifts, 100);
    for (int i = 0; i < n; i++)
        if (!hand_to_party(gifts[i])) { for (int k = i; k < n; k++) items_free(gifts[k]); fill(line, sizeof line, text("CantCarry"), name, price, money); say(line); return; } /* nobody can carry it: no sale */
    take_from_party(CAT_OBJECT, currency, price);
    cJSON_ReplaceItemInObject(item, "SoldRuntime", cJSON_CreateNumber(num(item, "SoldRuntime", 0) + 1));
    fill(line, sizeof line, text("Agreed"), name, price, money);
    say(line);
}

/* ShopHaggle.Offer. */
enum { H_ACCEPT, H_TOO_LOW, H_COUNTER, H_GIVE_UP };
static int haggle(int basePrice, int asking, int offer, int lowestPercent, int triesUsed, int tries, int *price)
{
    if (lowestPercent < 1) lowestPercent = 1;
    if (lowestPercent > 100) lowestPercent = 100;
    int lowest = (int)ceil(basePrice * lowestPercent / 100.0);
    if (lowest < 1) lowest = 1;
    *price = asking;
    if (offer >= asking) return H_ACCEPT;
    int last = triesUsed + 1 >= (tries > 1 ? tries : 1);
    if (offer < lowest) return last ? H_GIVE_UP : H_TOO_LOW;
    int counter = (asking + offer + 1) / 2;
    if (counter < lowest) counter = lowest;
    if (counter <= offer + 1) { *price = offer; return H_ACCEPT; }
    *price = counter;
    return last ? H_GIVE_UP : H_COUNTER;
}

/* Its price asked: yes, no, or (Serpent Isle) haggle. */
static void offer(cJSON *item, int asking, int tries, int lastPrice)
{
    char line[512];
    const char *name = item_name(item), *money = money_name();
    s.item = item; s.asking = asking; s.tries = tries; s.lastPrice = lastPrice;
    if (!cJSON_IsTrue(get(item, "Infinite")) && num(item, "SoldRuntime", 0) >= num(item, "Quantity", 1))
    {
        fill(line, sizeof line, text("OutOfStock"), name, asking, money);
        say(line);
        show_node(s.node, s.parent);
        return;
    }
    const char *replies[3] = { text("Yes"), text("No"), text("HaggleChoice") };
    s.kinds[0] = R_YES; s.kinds[1] = R_NO; s.kinds[2] = R_HAGGLE;
    int canHaggle = cJSON_IsTrue(get(s.shop, "Haggle")) && !lastPrice && tries < (num(s.shop, "HaggleTries", 3) > 1 ? num(s.shop, "HaggleTries", 3) : 1);
    const char *own = str(item, "PriceLine");
    fill(line, sizeof line, asking >= num(item, "Price", 0) ? (own[0] ? own : text("PriceLine")) : text("HaggleCounter"), name, asking, money); /* his counter only once he's come down */
    ask(line, replies, canHaggle ? 3 : 2);
}

/* The offer from the slider (Escape: back to his price, yes or no). */
static void haggled(int ok, int offered, void *context)
{
    (void)context;
    char line[512];
    cJSON *item = s.item;
    const char *name = item_name(item), *money = money_name();
    if (!ok) { offer(item, s.asking, s.tries, 0); return; }
    int price, answer = haggle(num(item, "Price", 0), s.asking, offered, num(s.shop, "LowestPercent", 70), s.tries, num(s.shop, "HaggleTries", 3), &price);
    switch (answer)
    {
        case H_ACCEPT: fill(line, sizeof line, text("HaggleAccept"), name, price, money); say(line); buy(item, price); show_node(s.node, s.parent); break;
        case H_TOO_LOW: fill(line, sizeof line, text("HaggleTooLow"), name, s.asking, money); say(line); offer(item, s.asking, s.tries + 1, 0); break;
        case H_COUNTER: offer(item, price, s.tries + 1, 0); break;
        default: fill(line, sizeof line, text("HaggleGiveUp"), name, price, money); say(line); offer(item, price, s.tries + 1, 1); break; /* his last price: yes or no */
    }
}

/* Selling: the things the party has that he buys; one picked - his offer, yes or no. */
static void show_sell(void)
{
    const char *replies[MAX_CHOICES];
    int n = 0;
    cJSON *b;
    cJSON_ArrayForEach(b, get(s.shop, "Buys"))
        if (n < MAX_CHOICES - 1 && party_count(num(b, "Category", CAT_OBJECT), num(b, "ItemId", -1)) > 0) { replies[n] = item_name(b); s.kinds[n] = R_SELL_PICK; s.targets[n++] = b; }
    if (!n) { say(text("NothingToSell")); show_node(cJSON_GetObjectItem(s.shop, "Root"), NULL); return; }
    replies[n] = text("Back"); s.kinds[n++] = R_SELL_BACK;
    ask(text("SellLine"), replies, n);
}

static cJSON *selling;

static void chosen(void *context, int reply)
{
    (void)context;
    if (!s.open || reply < 0 || reply >= MAX_CHOICES) return;
    char line[512];
    switch (s.kinds[reply])
    {
        case R_CHILD: show_node(s.targets[reply], s.node); break;
        case R_ITEM: offer(s.targets[reply], num(s.targets[reply], "Price", 0), 0, 0); break;
        case R_SELL: show_sell(); break;
        case R_BACK: show_node(s.parent, parent_of(get(s.shop, "Root"), s.parent)); break; /* up a step */
        case R_LEAVE: say(text("Farewell")); end_shop(); break;
        case R_YES:
            if (selling) /* his offer for a thing of the party's: taken, the money to them */
            {
                cJSON *b = selling;
                selling = NULL;
                const char *name = item_name(b), *money = money_name();
                if (party_count(num(b, "Category", CAT_OBJECT), num(b, "ItemId", -1)) > 0)
                {
                    take_from_party(num(b, "Category", CAT_OBJECT), num(b, "ItemId", -1), 1);
                    Item *coins[100];
                    int n = items_make_gifts(CAT_OBJECT, num(s.shop, "CurrencyObjectId", -1), num(b, "Price", 0), coins, 100);
                    for (int i = 0; i < n; i++)
                        if (!hand_to_party(coins[i]))
                        {
                            int x, y, f;
                            world_actor_cell(0, &x, &y, &f);
                            int uid = world_thing_put(CAT_OBJECT, items_item_id(coins[i]), items_quantity(coins[i]), x, y, f); /* nobody can carry it: at the main character's feet */
                            if (uid >= 0) items_park(uid, coins[i]); else items_free(coins[i]);
                        }
                    fill(line, sizeof line, text("SellAgreed"), name, num(b, "Price", 0), money);
                    say(line);
                }
                show_sell();
                break;
            }
            buy(s.item, s.asking);
            show_node(s.node, s.parent);
            break;
        case R_NO:
        {
            cJSON *about = selling ? selling : s.item;
            int price = selling ? num(selling, "Price", 0) : s.asking, wasSelling = selling != NULL;
            selling = NULL;
            fill(line, sizeof line, text("Declined"), item_name(about), price, money_name());
            say(line);
            if (wasSelling) show_sell(); else show_node(s.node, s.parent);
            break;
        }
        case R_HAGGLE: /* Serpent Isle: the slider (UI_input_numeric_value), from 1 to his asking price */
            fill(line, sizeof line, text("HagglePrompt"), item_name(s.item), s.asking, money_name());
            slider_open(line, 1, s.asking, 1, s.asking, haggled, NULL);
            break;
        case R_SELL_PICK:
        {
            cJSON *b = s.targets[reply];
            selling = b;
            const char *replies[2] = { text("Yes"), text("No") };
            s.kinds[0] = R_YES; s.kinds[1] = R_NO;
            fill(line, sizeof line, text("SellOffer"), item_name(b), num(b, "Price", 0), money_name());
            ask(line, replies, 2);
            break;
        }
        case R_SELL_BACK: show_node(get(s.shop, "Root"), NULL); break;
    }
}

static void end_shop(void)
{
    if (!s.open) return;
    s.open = 0;
    selling = NULL;
    /* the stock sold: written back into the command (a save keeps it) */
    char *json = cJSON_PrintUnformatted(s.shop);
    if (json && s.params) { cJSON_DeleteItemFromObject(s.params, "Shop"); cJSON_AddStringToObject(s.params, "Shop", json); }
    free(json);
    cJSON_Delete(s.shop);
    s.shop = NULL;
    if (s.done) s.done(s.context);
}

void shop_open(cJSON *params, int speaker, void (*done)(void *context), void *context)
{
    if (s.open) end_shop();
    memset(&s, 0, sizeof s);
    s.shop = cJSON_Parse(cJSON_GetStringValue(cJSON_GetObjectItem(params, "Shop")) ? cJSON_GetStringValue(cJSON_GetObjectItem(params, "Shop")) : "");
    s.done = done; s.context = context;
    if (!s.shop) { if (done) done(context); return; }
    s.open = 1; s.params = params; s.speaker = speaker;
    show_node(get(s.shop, "Root"), NULL);
}

void shop_reset(void) { if (s.shop) cJSON_Delete(s.shop); memset(&s, 0, sizeof s); selling = NULL; }
