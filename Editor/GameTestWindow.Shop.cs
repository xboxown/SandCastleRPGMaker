using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Shop command (Engine/ShopModel.cs): the merchant's lines and the player's choices, step by step down its talk;
/// a thing picked - its price asked (yes / no / haggle, Serpent Isle's ShopHaggle); bought: paid in its money from the
/// party, carried by the main character or someone in the party; selling: what he buys, from the party. Every word is
/// the shop's own (ShopTexts). The stock sold is written back into the command, so a save keeps it. The event waits
/// until the player leaves the shop.
/// </summary>
public partial class GameTestWindow
{
    private sealed class ShopVisit
    {
        public required EventCommand Command;
        public required ShopDefinition Shop;
        public required Action<bool> Done;
        public required string SpeakerSetting;
    }

    private bool ShopCommand(EventCommand command, Action<bool> done)
    {
        ShopDefinition? shop;
        try { shop = System.Text.Json.JsonSerializer.Deserialize<ShopDefinition>(command.Get("Shop")); } catch (System.Text.Json.JsonException) { shop = null; }
        if (shop is null || _player is null) return false;
        string setting = shop.SpeakerId;
        // "Self": the merchant, fixed now - the shop's later boxes are put up from the player's picks, when "who set it off" may be another.
        if (shop.SpeakerId == "Self") shop.SpeakerId = _activator is { } merchant ? merchant.Character.Id.ToString() : "Player";
        ShowShopNode(new ShopVisit { Command = command, Shop = shop, Done = done, SpeakerSetting = setting }, shop.Root, null);
        return true;
    }

    private string ShopItemName(ReferencedItemCategory category, int itemId) => DollLook.ItemName(new ItemReference { Category = category, ItemId = itemId }, _objects, _books, _containers);
    private string ShopMoney(ShopVisit v) => ObjectDef(v.Shop.CurrencyObjectId)?.Name ?? "gold";

    /// <summary>A line of the merchant's (no choices): the next comes after it's read.</summary>
    private void ShopSay(ShopVisit v, string text)
    {
        if (text.Trim().Length == 0) return;
        if (BuildMessage(new EventCommand { Type = EventCommandType.ShowText, Parameters = { ["Text"] = text, ["SpeakerId"] = v.Shop.SpeakerId } }) is { } message)
        {
            _messages.Enqueue(message);
            if (_currentMessage is null) ShowNextMessage();
        }
    }

    /// <summary>A line with the player's choices; picked: its number.</summary>
    private void ShopAsk(ShopVisit v, string text, List<string> choices, Action<int> picked)
    {
        var replies = choices.Select((c, i) => new DialogChoice { Id = i, Text = c }).ToList();
        if (BuildMessage(new EventCommand { Type = EventCommandType.ShowText, Parameters = { ["Text"] = text, ["SpeakerId"] = v.Shop.SpeakerId } }, replies, picked) is { } message)
        {
            _messages.Enqueue(message);
            if (_currentMessage is null) ShowNextMessage();
        }
        else EndShop(v); // the merchant can't be heard (out of sight): the shop closes
    }

    private void EndShop(ShopVisit v)
    {
        // The stock sold: kept in the command (a save keeps it) - with its own speaker setting, not the merchant fixed for this visit.
        var keep = System.Text.Json.JsonSerializer.Deserialize<ShopDefinition>(System.Text.Json.JsonSerializer.Serialize(v.Shop))!;
        keep.SpeakerId = v.SpeakerSetting;
        v.Command.Parameters["Shop"] = System.Text.Json.JsonSerializer.Serialize(keep);
        v.Done(true);
    }

    /// <summary>A step of the talk: its line; the choices after it, its goods, Sell (at the start), Back or Leave.</summary>
    private void ShowShopNode(ShopVisit v, ShopNode node, ShopNode? parent)
    {
        var t = v.Shop.Texts;
        var choices = new List<string>();
        var actions = new List<Action>();
        foreach (var child in node.Children) { var c = child; choices.Add(c.Label); actions.Add(() => ShowShopNode(v, c, node)); }
        foreach (var item in node.Items) { var it = item; choices.Add(ShopItemName(it.Category, it.ItemId)); actions.Add(() => OfferShopItem(v, node, parent, it, it.Price, 0)); }
        if (parent is null && v.Shop.Buys.Count > 0) { choices.Add(t.SellChoice); actions.Add(() => ShowShopSell(v)); }
        if (parent is not null) { choices.Add(t.Back); actions.Add(() => ShowShopNode(v, parent, ParentOf(v.Shop.Root, parent))); } // up a step
        choices.Add(t.Leave);
        actions.Add(() => { ShopSay(v, t.Farewell); EndShop(v); });
        ShopAsk(v, node.Line, choices, i => actions[i]());
    }

    /// <summary>The step a step hangs from (null for the first).</summary>
    private static ShopNode? ParentOf(ShopNode root, ShopNode node)
    {
        foreach (var child in root.Children)
        {
            if (ReferenceEquals(child, node)) return root;
            if (ParentOf(child, node) is { } found) return found;
        }
        return null;
    }

    /// <summary>Its price asked: yes, no, or (Serpent Isle) haggle.</summary>
    private void OfferShopItem(ShopVisit v, ShopNode node, ShopNode? parent, ShopItem item, int asking, int triesUsed, bool lastPrice = false)
    {
        var t = v.Shop.Texts;
        string name = ShopItemName(item.Category, item.ItemId), money = ShopMoney(v);
        if (!item.Infinite && item.SoldRuntime >= item.Quantity) { ShopSay(v, ShopTexts.Fill(t.OutOfStock, name, asking, money)); ShowShopNode(v, node, parent); return; }
        var choices = new List<string> { t.Yes, t.No };
        bool canHaggle = v.Shop.Haggle && !lastPrice && triesUsed < Math.Max(1, v.Shop.HaggleTries);
        if (canHaggle) choices.Add(t.HaggleChoice);
        string line = asking >= item.Price ? (item.PriceLine.Length > 0 ? item.PriceLine : t.PriceLine) : t.HaggleCounter; // his counter only once he's come down
        ShopAsk(v, ShopTexts.Fill(line, name, asking, money), choices, i =>
        {
            if (i == 0) { BuyShopItem(v, item, asking); ShowShopNode(v, node, parent); }
            else if (i == 1) { ShopSay(v, ShopTexts.Fill(t.Declined, name, asking, money)); ShowShopNode(v, node, parent); }
            else
            {
                // Serpent Isle: the slider (UI_input_numeric_value), from 1 to his asking price; Escape - back to his price, yes or no.
                ShowNumberSlider(ShopTexts.Fill(t.HagglePrompt, name, asking, money), 1, asking, 1, asking, offer =>
                {
                    var (answer, price) = ShopHaggle.Offer(item.Price, asking, offer, v.Shop.LowestPercent, triesUsed, v.Shop.HaggleTries);
                    switch (answer)
                    {
                        case ShopHaggle.Answer.Accept:
                            ShopSay(v, ShopTexts.Fill(t.HaggleAccept, name, price, money));
                            BuyShopItem(v, item, price);
                            ShowShopNode(v, node, parent);
                            break;
                        case ShopHaggle.Answer.TooLow:
                            ShopSay(v, ShopTexts.Fill(t.HaggleTooLow, name, asking, money));
                            OfferShopItem(v, node, parent, item, asking, triesUsed + 1);
                            break;
                        case ShopHaggle.Answer.Counter:
                            OfferShopItem(v, node, parent, item, price, triesUsed + 1);
                            break;
                        default: // no more haggling: his last price, yes or no
                            ShopSay(v, ShopTexts.Fill(t.HaggleGiveUp, name, price, money));
                            OfferShopItem(v, node, parent, item, price, triesUsed + 1, lastPrice: true);
                            break;
                    }
                }, () => OfferShopItem(v, node, parent, item, asking, triesUsed));
            }
        });
    }

    /// <summary>Bought: the money from the party (all of it there), the thing to the main character or someone in the party who can carry it.</summary>
    private void BuyShopItem(ShopVisit v, ShopItem item, int price)
    {
        var t = v.Shop.Texts;
        string name = ShopItemName(item.Category, item.ItemId), money = ShopMoney(v);
        if (PartyCountOf(ReferencedItemCategory.Object, v.Shop.CurrencyObjectId) < price) { ShopSay(v, ShopTexts.Fill(t.NotEnoughMoney, name, price, money)); return; }
        var gifts = MakeGifts(item.Category, item.ItemId, 1);
        var carriers = PartyAndPlayer().Where(c => !IsDead(c)).ToList();
        foreach (var gift in gifts)
            if (!carriers.Any(c => HandOver(gift, c))) { ShopSay(v, ShopTexts.Fill(t.CantCarry, name, price, money)); return; } // nobody can carry it: no sale
        TakeFromPartyOf(ReferencedItemCategory.Object, v.Shop.CurrencyObjectId, price);
        item.SoldRuntime++;
        RefreshAfterMove();
        ShopSay(v, ShopTexts.Fill(t.Agreed, name, price, money));
    }

    /// <summary>Selling: the things the party has that he buys; one picked - his offer, yes or no.</summary>
    private void ShowShopSell(ShopVisit v)
    {
        var t = v.Shop.Texts;
        var wanted = v.Shop.Buys.Where(b => PartyCountOf(b.Category, b.ItemId) > 0).ToList();
        if (wanted.Count == 0) { ShopSay(v, t.NothingToSell); ShowShopNode(v, v.Shop.Root, null); return; }
        var choices = wanted.Select(b => ShopItemName(b.Category, b.ItemId)).Append(t.Back).ToList();
        ShopAsk(v, t.SellLine, choices, i =>
        {
            if (i >= wanted.Count) { ShowShopNode(v, v.Shop.Root, null); return; }
            var buy = wanted[i];
            string name = ShopItemName(buy.Category, buy.ItemId), money = ShopMoney(v);
            ShopAsk(v, ShopTexts.Fill(t.SellOffer, name, buy.Price, money), new List<string> { t.Yes, t.No }, yes =>
            {
                if (yes == 0 && PartyCountOf(buy.Category, buy.ItemId) > 0)
                {
                    TakeFromPartyOf(buy.Category, buy.ItemId, 1);
                    var carriers = PartyAndPlayer().Where(c => !IsDead(c)).ToList();
                    foreach (var coins in MakeGifts(ReferencedItemCategory.Object, v.Shop.CurrencyObjectId, buy.Price))
                        if (!carriers.Any(c => HandOver(coins, c))) PutDownAt(coins, _player!); // nobody can carry it: at the main character's feet
                    RefreshAfterMove();
                    ShopSay(v, ShopTexts.Fill(t.SellAgreed, name, buy.Price, money));
                }
                else if (yes != 0) ShopSay(v, ShopTexts.Fill(t.Declined, name, buy.Price, money));
                ShowShopSell(v);
            });
        });
    }

    private int PartyCountOf(ReferencedItemCategory category, int itemId) =>
        PartyItems().Where(p => p.Item.Category == category && p.Item.ItemId == itemId).Sum(p => Math.Max(1, p.Item.Quantity));

    /// <summary>So many taken from the party: from the main character first, then the others.</summary>
    private void TakeFromPartyOf(ReferencedItemCategory category, int itemId, int quantity)
    {
        foreach (var member in PartyAndPlayer())
        {
            if (quantity <= 0) break;
            int has = PartyItems().Where(p => p.Carrier == member && p.Item.Category == category && p.Item.ItemId == itemId).Sum(p => Math.Max(1, p.Item.Quantity));
            int take = Math.Min(has, quantity);
            if (take > 0 && TakeFromCarrier(member, category, itemId, take) is not null) quantity -= take;
        }
    }
}
