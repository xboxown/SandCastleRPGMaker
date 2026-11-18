using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Magic weapons and armor in play (Engine/MagicItems.cs has the rules):
/// a few times a second every character's worn-item stat bonuses and On
/// Equip / On Unequip common events are brought up to date with what they
/// wear; defense, damage, reach, light and immunities are read where blows
/// and lights are worked out (GameTestWindow.Weapons / Combat / Lighting);
/// a cursed item won't be dragged off, unequipped, dropped or given away.
/// The Equipment tab's commands: Curse Item, Lift Curse, Is Item Cursed,
/// Enchant Item, Remove Enchantments, Get Magic Bonus, Get Equipped Item,
/// Combine Items.
/// </summary>
public partial class U6GameWindow
{
    private double _magicCheckMs;

    /// <summary>The objects someone wears or holds, with their kinds.</summary>
    private List<(InventoryItemInstance Item, WorldObject? Def)> WornMagic(CharacterDefinition who) =>
        ItemsInSlots(who).Where(e => e.Item.Category == ReferencedItemCategory.Object).Select(e => (e.Item, ObjectDef(e.Item))).ToList();

    private int MagicTotal(CharacterDefinition who, MagicBonusKind kind, int statId = -1) => MagicItems.Total(WornMagic(who), kind, statId);

    /// <summary>Worn (in a slot, straight on them) and cursed: it won't come off.</summary>
    private bool IsWornCursed(CharacterDefinition who, InventoryItemInstance item) =>
        item.Category == ReferencedItemCategory.Object && MagicItems.IsCursed(ObjectDef(item), item) && ItemsInSlots(who).Any(e => ReferenceEquals(e.Item, item));

    /// <summary>A few times a second: everyone's worn magic up to date.</summary>
    private void UpdateMagicItems(double elapsedMs)
    {
        if ((_magicCheckMs -= elapsedMs) > 0) return;
        _magicCheckMs = 250;
        foreach (var who in AllLivingCharacters().ToList()) ReconcileMagic(who);
    }

    private void ReconcileMagic(CharacterDefinition who)
    {
        bool changed = false;
        MagicItems.Reconcile(who, WornMagic(who),
            id => _stats.Stats.FirstOrDefault(s => s.Id == id) is { } stat ? IntStat(who, stat) : 0,
            (id, value) =>
            {
                if (_stats.Stats.FirstOrDefault(s => s.Id == id) is not { Type: VariableValueType.Integer } stat) return;
                SetIntStat(who, stat, value, "a cursed item");
                changed = true;
            },
            ObjectDef,
            commonEventId => { if (_systemSettings.CommonEvents.FirstOrDefault(c => c.Id == commonEventId) is { Commands.Count: > 0 } common) RunCommandsOn(common.Commands, who); });
        if (changed) RefreshStatsViews();
    }

    private void InitializeMagicItemCommands()
    {
        var h = _execContext.GameHandlers;
        h[EventCommandType.CurseItem] = CurseItemCommand;
        h[EventCommandType.LiftCurse] = LiftCurseCommand;
        h[EventCommandType.IsItemCursed] = IsItemCursedCommand;
        h[EventCommandType.EnchantItem] = EnchantItemCommand;
        h[EventCommandType.RemoveEnchantments] = RemoveEnchantmentsCommand;
        h[EventCommandType.GetMagicBonus] = GetMagicBonusCommand;
        h[EventCommandType.GetEquippedItem] = GetEquippedItemCommand;
        h[EventCommandType.CombineItems] = CombineItemsCommand;
    }

    /// <summary>The items a magic command works on: of an item kind, in a slot, or everything worn or held.</summary>
    private List<(object Slot, InventoryItemInstance Item)> MagicTargets(EventCommand c, CharacterDefinition who)
    {
        var worn = ItemsInSlots(who).Where(e => e.Item.Category == ReferencedItemCategory.Object).ToList();
        string which = c.Get("Which");
        if (which.StartsWith("Everything")) return worn;
        if (which.StartsWith("What is in the slot"))
            return Enum.TryParse<EquipmentSlot>(c.Get("Slot"), out var slot) ? worn.Where(e => Equals(e.Slot, slot)).ToList() : new();
        var (category, itemId) = ItemFieldOf(c);
        return worn.Where(e => e.Item.Category == category && e.Item.ItemId == itemId).ToList();
    }

    private bool CurseItemCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        var targets = MagicTargets(c, who);
        foreach (var (_, item) in targets) item.CursedRuntime = true;
        return targets.Count > 0;
    }

    private bool LiftCurseCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        int lifted = 0;
        foreach (var (slot, item) in MagicTargets(c, who))
        {
            if (MagicItems.IsCursed(ObjectDef(item), item)) lifted++;
            item.CursedRuntime = false;
            if (c.Get("Unequip") == "True" && !IsDead(who)) StowOrDrop(who, slot, item);
        }
        ReconcileMagic(who);
        RefreshAfterMove();
        return lifted > 0;
    }

    private bool IsItemCursedCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        int cursed = MagicTargets(c, who).Count(t => MagicItems.IsCursed(ObjectDef(t.Item), t.Item));
        if (!c.Get("Which").StartsWith("Everything")) cursed = Math.Min(1, cursed);
        if (c.Get("Variable").Length > 0) SetScriptVariable(c.Get("Variable"), (double)cursed);
        return true;
    }

    /// <summary>A bonus kind as the command names it, and its stat (by name in the Stat Editor) for a stat bonus.</summary>
    private (MagicBonusKind Kind, int StatId)? MagicKindOf(EventCommand c)
    {
        string kind = c.Get("Kind");
        if (kind.StartsWith("Stat"))
        {
            string name = c.Get("StatName").Trim();
            return _stats.Stats.FirstOrDefault(s => string.Equals(s.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)) is { } stat ? (MagicBonusKind.Stat, stat.Id) : null;
        }
        return Enum.TryParse<MagicBonusKind>(kind, out var parsed) ? (parsed, -1) : null;
    }

    private bool EnchantItemCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        if (MagicKindOf(c) is not { } kind) { StatusText.Text = $"Enchant Item: there is no stat named \"{c.Get("StatName")}\" (Stat Editor)."; return false; }
        int amount = (int)FormulaNumber(c, "Amount");
        var targets = MagicTargets(c, who);
        foreach (var (_, item) in targets)
        {
            var list = item.EnchantsRuntime ??= new List<MagicBonus>();
            if (list.FirstOrDefault(b => b.Kind == kind.Kind && b.StatId == kind.StatId) is { } same) same.Amount += amount;
            else list.Add(new MagicBonus { Kind = kind.Kind, StatId = kind.StatId, Amount = amount });
            list.RemoveAll(b => b.Amount == 0);
            if (list.Count == 0) item.EnchantsRuntime = null;
        }
        ReconcileMagic(who);
        return targets.Count > 0;
    }

    private bool RemoveEnchantmentsCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who) return false;
        var targets = MagicTargets(c, who);
        bool any = targets.Any(t => t.Item.EnchantsRuntime is { Count: > 0 });
        foreach (var (_, item) in targets) item.EnchantsRuntime = null;
        ReconcileMagic(who);
        return any;
    }

    private bool GetMagicBonusCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who || MagicKindOf(c) is not { } kind) return false;
        int total = kind.Kind == MagicBonusKind.Reach ? MagicItems.Reach(WornMagic(who)) : MagicTotal(who, kind.Kind, kind.StatId);
        if (c.Get("Variable").Length > 0) SetScriptVariable(c.Get("Variable"), (double)total);
        return true;
    }

    private bool GetEquippedItemCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who || !Enum.TryParse<EquipmentSlot>(c.Get("Slot"), out var slot)) return false;
        var item = ItemsInSlots(who).FirstOrDefault(e => Equals(e.Slot, slot)).Item;
        if (c.Get("Variable").Length > 0) SetScriptVariable(c.Get("Variable"), (double)(item?.ItemId ?? -1));
        if (c.Get("NameVariable").Length > 0)
            SetScriptVariable(c.Get("NameVariable"), item is null ? "" : item.Category == ReferencedItemCategory.Container ? ContainerDef(item.ItemId)?.Name ?? "" : ObjectDef(item)?.Name ?? "");
        return item is not null;
    }

    private bool CombineItemsCommand(EventCommand c)
    {
        if (CharacterDefinitionFor(c.Get("Who")) is not { } who || IsDead(who)) return false;
        static (ReferencedItemCategory, int)? Kind(EventCommand c, string suffix) =>
            Enum.TryParse<ReferencedItemCategory>(c.Get("Category" + suffix), out var cat) && int.TryParse(c.Get("ItemId" + suffix), out int id) && id >= 0 ? (cat, id) : null;
        if (Kind(c, "") is not { } first || Kind(c, "2") is not { } second || Kind(c, "3") is not { } made) return false;
        int q1 = Math.Max(1, (int)FormulaNumber(c, "Quantity")), q2 = Math.Max(1, (int)FormulaNumber(c, "Quantity2")), q3 = Math.Max(1, (int)FormulaNumber(c, "Quantity3"));
        bool same = first == second;
        bool enough = same ? CountFree(first) >= q1 + q2 : CountFree(first) >= q1 && CountFree(second) >= q2;
        if (!enough)
        {
            SayCommandLine(c, "Missing", who);
            return false;
        }
        TakeFromCarrier(who, first.Item1, first.Item2, same ? q1 + q2 : q1);
        if (!same) TakeFromCarrier(who, second.Item1, second.Item2, q2);
        foreach (var gift in MakeGifts(made.Item1, made.Item2, q3))
            if (!HandOver(gift, who)) PutDownAt(gift, who);
        RefreshAfterMove();
        FireGlobalInventoryStateTrigger(BookTriggerConditionBased.ByNotHavingAnItem);
        return true;

        // What they carry of it that can be taken (a cursed thing they wear can't).
        int CountFree((ReferencedItemCategory Category, int Id) kind) =>
            AllWithin(who.Inventory).Where(i => i.Category == kind.Category && i.ItemId == kind.Id && !IsWornCursed(who, i)).Sum(i => Math.Max(1, i.Quantity));
    }
}
