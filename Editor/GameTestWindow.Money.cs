using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Money changers (the Exchange Currency event command): the party's coins
/// of one currency become another's at the Currency Editor's exchange rate,
/// the changer keeping its cut. The new money goes to the main character
/// (joining a stack they carry, as money does).
/// </summary>
public partial class GameTestWindow
{
    private bool ExchangeCurrencyCommand(int fromId, int toId, string amountText)
    {
        if (_player is null) return false;
        var currencies = ProjectSession.Currencies;
        var from = currencies.Currencies.FirstOrDefault(c => c.Id == fromId);
        var to = currencies.Currencies.FirstOrDefault(c => c.Id == toId);
        if (from is null || to is null) return false;
        // The money objects of each currency (a Money object is one currency).
        var fromObjects = _objects.Objects.Where(o => o.Type == ObjectType.Money && o.Money.CurrencyId == fromId).Select(o => o.Id).ToHashSet();
        var toObject = _objects.Objects.FirstOrDefault(o => o.Type == ObjectType.Money && o.Money.CurrencyId == toId);
        if (fromObjects.Count == 0 || toObject is null) { StatusText.Text = $"Exchange Currency: there's no Money object of {(fromObjects.Count == 0 ? from.Name : to.Name)} (Object Editor)."; return false; }
        int have = PartyItems().Where(p => p.Item.Category == ReferencedItemCategory.Object && fromObjects.Contains(p.Item.ItemId)).Sum(p => Math.Max(1, p.Item.Quantity));
        int give = amountText == "All" ? have : Math.Min(have, int.TryParse(amountText, out int n) ? n : 0);
        if (give <= 0) { StatusText.Text = $"You have no {from.Name} to change."; return false; }
        var (rate, cut) = currencies.RateOf(fromId, toId);
        int get = (int)Math.Floor(give * rate * (1 - cut / 100.0));
        int left = give;
        foreach (int id in fromObjects)
        {
            int count = Math.Min(left, PartyCount(id));
            if (count <= 0) continue;
            TakeFromParty(id, count);
            left -= count;
        }
        if (get > 0 && !TryCombineIntoContents(_player.Inventory, ReferencedItemCategory.Object, toObject.Id, get))
            _player.Inventory.Add(new InventoryItemInstance
            {
                InstanceId = _player.Inventory.Count == 0 ? 1 : _player.Inventory.Max(i => i.InstanceId) + 1,
                Category = ReferencedItemCategory.Object, ItemId = toObject.Id, Quantity = get
            });
        RefreshAfterMove();
        StatusText.Text = $"You change {give} {from.Name} into {get} {to.Name}.";
        return true;
    }
}
