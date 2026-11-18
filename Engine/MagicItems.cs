namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Magic weapons and armor in play (every engine - U6, U7, U8 - and the C
/// player's items.c / combat.c do the same):
///  - An item's magic is its Object Editor > Weapon / Armor > Magic plus
///    whatever Enchant Item put on that one copy.
///  - Stat bonuses are made on the wearer as it goes on and taken back
///    exactly (what was really moved - a stat never goes below 0) as it
///    comes off, however it comes off: dragged, stowed, dropped, stolen,
///    burnt in a fire. They are kept on the character (saved with it).
///  - Defense adds to all the armor worn; reach: hand-to-hand blows land
///    this many cells away; light: a light that never burns out; immunities
///    as armor's.
///  - Cursed: once on, it can't be taken off (dragged, unequipped by a
///    command, dropped) until Lift Curse.
///  - On Equip / On Unequip common events run with the wearer as the one
///    who set them off.
/// </summary>
public static class MagicItems
{
    /// <summary>The kinds whose Magic settings count (another type's leftover settings don't).</summary>
    public static bool HasMagic(WorldObject? def) => def is { Type: ObjectType.Weapon or ObjectType.Armor };

    public static bool IsCursed(WorldObject? def, bool? cursedRuntime) => cursedRuntime ?? (HasMagic(def) && def!.Magic.Cursed);
    public static bool IsCursed(WorldObject? def, InventoryItemInstance item) => IsCursed(def, item.CursedRuntime);

    public static string CursedText(WorldObject? def) =>
        HasMagic(def) && !string.IsNullOrWhiteSpace(def!.Magic.CursedText) ? def.Magic.CursedText : "It will not come off - it is cursed!";

    /// <summary>Every bonus one worn copy gives: its kind's own, then the copy's enchantments.</summary>
    public static IEnumerable<MagicBonus> Bonuses(WorldObject? def, InventoryItemInstance item)
    {
        if (HasMagic(def))
        {
            var m = def!.Magic;
            foreach (var b in m.StatBonuses) if (b.Amount != 0) yield return new MagicBonus { Kind = MagicBonusKind.Stat, StatId = b.StatId, Amount = b.Amount };
            if (m.DefenseBonus != 0) yield return new MagicBonus { Kind = MagicBonusKind.Defense, Amount = m.DefenseBonus };
            if (m.ReachCells > 1) yield return new MagicBonus { Kind = MagicBonusKind.Reach, Amount = m.ReachCells - 1 };
            if (m.LightBrightness > 0) yield return new MagicBonus { Kind = MagicBonusKind.Light, Amount = m.LightBrightness };
        }
        if (item.EnchantsRuntime is { } extra)
            foreach (var b in extra) if (b.Amount != 0) yield return b;
    }

    /// <summary>The sum of one kind of bonus over everything worn (Stat: of that stat).</summary>
    public static int Total(IEnumerable<(InventoryItemInstance Item, WorldObject? Def)> worn, MagicBonusKind kind, int statId = -1) =>
        worn.SelectMany(w => Bonuses(w.Def, w.Item)).Where(b => b.Kind == kind && (kind != MagicBonusKind.Stat || b.StatId == statId)).Sum(b => b.Amount);

    /// <summary>Hand-to-hand reach in cells: 1, plus every reach bonus worn (the magic sword's 4 cells).</summary>
    public static int Reach(IEnumerable<(InventoryItemInstance Item, WorldObject? Def)> worn) => Math.Max(1, 1 + Total(worn, MagicBonusKind.Reach));

    /// <summary>Damage types nothing worn lets through (Magic immunities of weapons and armor).</summary>
    public static bool Immune(IEnumerable<(InventoryItemInstance Item, WorldObject? Def)> worn, DamageType type) =>
        worn.Any(w => HasMagic(w.Def) && w.Def!.Magic.Immunities.Contains(type));

    /// <summary>
    /// Brings the character's worn-item stat bonuses and On Equip / On Unequip
    /// events up to date with what is worn now: what came off is taken back
    /// (and its On Unequip runs), what went on is made (and its On Equip runs).
    /// getStat / setStat read and write an Integer stat by id (setStat keeps it at 0 or more).
    /// </summary>
    public static void Reconcile(CharacterDefinition who, IReadOnlyList<(InventoryItemInstance Item, WorldObject? Def)> worn,
        Func<int, int> getStat, Action<int, int> setStat, Func<int, WorldObject?> objectById, Action<int>? runCommonEvent)
    {
        // The stat bonuses each worn copy wants now.
        var wanted = new Dictionary<(int Instance, int Stat), int>();
        foreach (var (item, def) in worn)
            foreach (var b in Bonuses(def, item))
                if (b.Kind == MagicBonusKind.Stat && b.StatId >= 0)
                    wanted[(item.InstanceId, b.StatId)] = wanted.GetValueOrDefault((item.InstanceId, b.StatId)) + b.Amount;

        var applied = who.MagicAppliedRuntime ??= new List<AppliedMagic>();
        // Taken back: what came off, or changed (an enchantment added or removed).
        foreach (var a in applied.ToList())
        {
            if (wanted.TryGetValue((a.InstanceId, a.StatId), out int w) && w == a.Wanted) continue;
            if (a.Amount != 0) setStat(a.StatId, getStat(a.StatId) - a.Amount);
            applied.Remove(a);
        }
        // Made: what went on.
        foreach (var ((instance, stat), amount) in wanted)
        {
            if (amount == 0 || applied.Any(a => a.InstanceId == instance && a.StatId == stat)) continue;
            int before = getStat(stat);
            setStat(stat, before + amount);
            applied.Add(new AppliedMagic { InstanceId = instance, StatId = stat, Amount = getStat(stat) - before, Wanted = amount });
        }
        if (applied.Count == 0) who.MagicAppliedRuntime = null;

        // On Equip / On Unequip.
        var seen = who.MagicWornRuntime ??= new List<WornMagicItem>();
        var nowIds = worn.Select(w => w.Item.InstanceId).ToHashSet();
        foreach (var gone in seen.Where(s => !nowIds.Contains(s.InstanceId)).ToList())
        {
            seen.Remove(gone);
            if (runCommonEvent is not null && objectById(gone.ObjectId) is { } d && HasMagic(d) && d.Magic.OnUnequipCommonEventId >= 0) runCommonEvent(d.Magic.OnUnequipCommonEventId);
        }
        foreach (var (item, def) in worn)
        {
            if (!HasMagic(def) || seen.Any(s => s.InstanceId == item.InstanceId && s.ObjectId == def!.Id)) continue;
            if (def!.Magic.OnEquipCommonEventId < 0 && def.Magic.OnUnequipCommonEventId < 0) continue;
            seen.RemoveAll(s => s.InstanceId == item.InstanceId);
            seen.Add(new WornMagicItem { InstanceId = item.InstanceId, ObjectId = def.Id });
            if (def.Magic.OnEquipCommonEventId >= 0) runCommonEvent?.Invoke(def.Magic.OnEquipCommonEventId);
        }
        if (seen.Count == 0) who.MagicWornRuntime = null;
    }
}
