/* The Shop command (GameTestWindow.Shop.cs, Engine/ShopModel.cs). */
#ifndef SANDCASTLE_SHOP_H
#define SANDCASTLE_SHOP_H

#include "../third_party/cJSON.h"

/* A shop opened: params is the command's parameters (its "Shop" JSON - the stock sold is written back into it); speaker the
   actor who talks (-1: narration); done called when the player leaves. */
void shop_open(cJSON *params, int speaker, void (*done)(void *context), void *context);
void shop_reset(void);

#endif
