using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The game conditions events test (Advance IF, a Complex Condition's Game
/// Condition, "Until Condition Met"), and the history they read: what has
/// happened in this game - destroyed, stolen, used, ignited, killed, sounds
/// played and stopped, picklocked, unlocked by key or by magic, equipped and
/// unequipped, lit, put out and burnt out, events and their pages run. A
/// "happened" condition is true once it has happened at least so many times
/// (and, if set, within so many game hours).
/// Sold / traded conditions wait for the shop and trade system: nothing
/// sells or trades yet, so they stay false until it does.
/// </summary>
public partial class GameTestWindow
{
    private readonly Dictionary<string, (int Count, double LastMinute)> _history = new();
    /// <summary>Whose event's conditions are being looked at (for "this event's page N").</summary>
    private string _evaluatingEventName = string.Empty;
    private Action<int>? _soundPlayedHandler, _soundStoppedHandler;

    // ================= History =================

    private void Record(string key)
    {
        var (count, _) = _history.GetValueOrDefault(key);
        _history[key] = (count + 1, TotalMinutes());
    }

    private static string ItemKey(ReferencedItemCategory category, int id) => $"{category}:{id}";

    /// <summary>Something happened to an item: under its own key, and under "any" of that happening.</summary>
    private void RecordItem(string what, ReferencedItemCategory category, int id)
    {
        Record($"{what}:{ItemKey(category, id)}");
        Record($"{what}:*");
    }

    /// <summary>Someone did something with an item (put it on, took it off): by who and what, and "anyone" / "anything".</summary>
    private void RecordPersonItem(string what, CharacterDefinition who, InventoryItemInstance item)
    {
        string thing = ItemKey(item.Category, item.ItemId);
        Record($"{what}:{who.Id}:{thing}");
        Record($"{what}:*:{thing}");
        Record($"{what}:{who.Id}:*");
        Record($"{what}:*:*");
    }

    private bool Happened(string key, int atLeast, int withinHours)
    {
        if (!_history.TryGetValue(key, out var entry) || entry.Count < Math.Max(1, atLeast)) return false;
        return withinHours <= 0 || TotalMinutes() - entry.LastMinute <= withinHours * 60.0;
    }

    private bool HappenedTo(string what, ItemReference? item, GameCondition c) =>
        Happened(item is { ItemId: >= 0 } ? $"{what}:{ItemKey(item.Category, item.ItemId)}" : $"{what}:*", c.Number, c.Number2);

    /// <summary>The object types' own happenings, recorded as their events fire (whether or not they have pages).</summary>
    private void RecordObjectHappening(WorldObject obj, BookTriggerConditionBased trigger)
    {
        switch (trigger)
        {
            case BookTriggerConditionBased.ByDestroyItem: RecordItem("destroyed", ReferencedItemCategory.Object, obj.Id); break;
            case BookTriggerConditionBased.ByUseItem: RecordItem("used", ReferencedItemCategory.Object, obj.Id); break;
        }
        if (obj.Type != ObjectType.LightSource) return;
        switch (trigger)
        {
            case BookTriggerConditionBased.ByAfterReadItem: RecordItem("lit", ReferencedItemCategory.Object, obj.Id); break;
            case BookTriggerConditionBased.ByEnter: RecordItem("putout", ReferencedItemCategory.Object, obj.Id); break;
            case BookTriggerConditionBased.ByStay: RecordItem("burnedout", ReferencedItemCategory.Object, obj.Id); break;
        }
    }

    /// <summary>Every sound's start and end, from wherever it plays (Play Sound, a sprite's frame, a door).</summary>
    private void WatchSounds()
    {
        if (_soundPlayedHandler is not null) return;
        _soundPlayedHandler = id => Dispatcher.BeginInvoke(new Action(() => Record($"sound:{id}")));
        _soundStoppedHandler = id => Dispatcher.BeginInvoke(new Action(() => Record($"soundstop:{id}")));
        SoundPlayback.Played += _soundPlayedHandler;
        SoundPlayback.Stopped += _soundStoppedHandler;
        Closed += (_, _) =>
        {
            SoundPlayback.Played -= _soundPlayedHandler;
            SoundPlayback.Stopped -= _soundStoppedHandler;
        };
    }

    /// <summary>What an event's owner is called (its name, for "Event executed").</summary>
    private string EventOwnerName(object owner) => owner switch
    {
        ObjectInstance o => DefinitionOf(o)?.Name ?? "",
        InventoryItemInstance i => DefinitionOf(i)?.Name ?? (i.Category == ReferencedItemCategory.Book ? _books.Books.FirstOrDefault(b => b.Id == i.ItemId)?.Name : _containers.Containers.FirstOrDefault(c => c.Id == i.ItemId)?.Name) ?? "",
        PlacedItemInstance p => p.Category == ReferencedItemCategory.Book ? _books.Books.FirstOrDefault(b => b.Id == p.ItemId)?.Name ?? "" : _containers.Containers.FirstOrDefault(c => c.Id == p.ItemId)?.Name ?? "",
        BookData b => b.Name,
        ContainerData c => c.Name,
        GameEvent e => e.Name,
        CharacterDefinition ch => ch.Name,
        ValueTuple<DollState, int> part => _objects.DollDesigns.FirstOrDefault(d => d.Id == part.Item1.DesignId)?.Part(part.Item2)?.Name ?? "",
        _ => owner.ToString() ?? ""
    };

    // ================= Testing =================

    private bool EvaluateGameCondition(GameCondition c)
    {
        bool result;
        try { result = TestCondition(c); }
        catch { result = false; }
        return c.Not ? !result : result;
    }

    private CharacterDefinition? ConditionCharacter(GameCondition c) =>
        c.CharacterId == GameCondition.MainPlayer ? _player : AllLivingCharacters(includeDead: true).FirstOrDefault(ch => ch.Id == c.CharacterId);

    private static IEnumerable<InventoryItemInstance> AllWithin(IEnumerable<InventoryItemInstance> items) =>
        items.SelectMany(i => new[] { i }.Concat(AllWithin(i.Contents)));

    private static bool IsItem(InventoryItemInstance i, ItemReference? wanted) =>
        wanted is not { ItemId: >= 0 } || (i.Category == wanted.Category && i.ItemId == wanted.ItemId);

    private static IEnumerable<InventoryItemInstance> EquippedItems(CharacterDefinition who)
    {
        var ids = who.EquippedSlots.Values.Concat(who.CustomEquippedSlots.Values).ToHashSet();
        return who.Inventory.Where(i => ids.Contains(i.InstanceId));
    }

    private bool TestCondition(GameCondition c)
    {
        var who = ConditionCharacter(c);
        switch (c.Kind)
        {
            case GameConditionKind.Variable:
            {
                double value = _execContext.GetVariable(c.VariableName);
                string text = c.Value.Trim();
                double target = text.Equals("true", StringComparison.OrdinalIgnoreCase) ? 1 : text.Equals("false", StringComparison.OrdinalIgnoreCase) ? 0 : double.TryParse(text, out double n) ? n : 0;
                return GameConditions.Compare(value, c.Comparison, target);
            }
            case GameConditionKind.Equipped:
                return who is not null && EquippedItems(who).Any(i => IsItem(i, c.Item));
            case GameConditionKind.Unequipped:
            {
                if (who is null) return false;
                var worn = EquippedItems(who).ToHashSet();
                return AllWithin(who.Inventory).Any(i => !worn.Contains(i) && IsItem(i, c.Item));
            }
            case GameConditionKind.HavingItem:
            case GameConditionKind.NotHavingItem:
            {
                // The main character: the party's things; anyone else: their own.
                int count = c.CharacterId == GameCondition.MainPlayer
                    ? PartyItems().Where(p => IsItem(p.Item, c.Item)).Sum(p => Math.Max(1, p.Item.Quantity))
                    : who is null ? 0 : AllWithin(who.Inventory).Where(i => IsItem(i, c.Item)).Sum(i => Math.Max(1, i.Quantity));
                bool has = count >= Math.Max(1, c.Number);
                return c.Kind == GameConditionKind.HavingItem ? has : !has;
            }
            case GameConditionKind.Gender:
                return who is not null && who.Gender == c.Gender;
            case GameConditionKind.Age:
                return who is not null && GameConditions.Compare(who.Age, c.Comparison, c.Number);
            case GameConditionKind.Stat:
                return who is not null && who.StatValues.TryGetValue(c.StatId, out var raw) && int.TryParse(raw, out int stat) && GameConditions.Compare(stat, c.Comparison, c.Number);
            case GameConditionKind.Destroyed: return HappenedTo("destroyed", c.Item, c);
            case GameConditionKind.Stolen: return HappenedTo("stolen", c.Item, c);
            case GameConditionKind.Used: return HappenedTo("used", c.Item, c);
            case GameConditionKind.ExplosiveIgnited: return HappenedTo("ignited", c.Item, c);
            case GameConditionKind.Picklocked: return HappenedTo("picklocked", c.Item, c);
            case GameConditionKind.UnlockedByKey: return HappenedTo("unlockkey", c.Item, c);
            case GameConditionKind.MagicallyUnlocked: return HappenedTo("unlockmagic", c.Item, c);
            case GameConditionKind.LightBurnedOut: return HappenedTo("burnedout", c.Item, c);
            case GameConditionKind.LightPutOut: return HappenedTo("putout", c.Item, c);
            case GameConditionKind.LightLit: return HappenedTo("lit", c.Item, c);
            case GameConditionKind.LastCommandSucceeded: return _execContext.LastResult;
            case GameConditionKind.SeesCharacter:
            {
                if (who is null || _player is null || IsDead(who)) return false;
                if (c.Number == 1) return who != _player && CanSee(who, _player); // it sees the main character
                var (x, y) = PixelOf(who);
                return IsOnScreen(x, y); // on the screen
            }
            case GameConditionKind.CharacterKilled:
                return c.CharacterId == GameCondition.Anyone ? Happened("killed:*", c.Number, c.Number2) : who is not null && (IsDead(who) || Happened($"killed:{who.Id}", c.Number, c.Number2));
            case GameConditionKind.SoundPlayed: return Happened(c.SoundId >= 0 ? $"sound:{c.SoundId}" : "sound:*", c.Number, c.Number2);
            case GameConditionKind.SoundStopped: return Happened(c.SoundId >= 0 ? $"soundstop:{c.SoundId}" : "soundstop:*", c.Number, c.Number2);
            case GameConditionKind.LeverOnOff:
                return _map.Objects.Any(o => o.ContainerInstanceId < 0 && (c.Item is not { ItemId: >= 0 } || o.ObjectId == c.Item.ItemId)
                    && DefinitionOf(o) is { Type: ObjectType.Lever } && (o.IsOpenRuntime == true) == c.On);
            case GameConditionKind.TimeDate:
            {
                int from = Math.Clamp(c.Number, 0, 23), to = Math.Clamp(c.Number2, 0, 23), hour = _clock.Hour;
                bool inHours = from == to ? hour == from : from < to ? hour >= from && hour < to : hour >= from || hour < to;
                bool onDay = c.Value.Trim().Length == 0 || (int.TryParse(c.Value.Trim(), out int day) && GameConditions.Compare(_clock.Day + 1, c.Comparison, day));
                return inHours && onDay;
            }
            case GameConditionKind.EquipAction:
            case GameConditionKind.UnequipAction:
            {
                string what = c.Kind == GameConditionKind.EquipAction ? "equip" : "unequip";
                string person = c.CharacterId == GameCondition.Anyone || who is null ? "*" : who.Id.ToString();
                string item = c.Item is { ItemId: >= 0 } ? ItemKey(c.Item.Category, c.Item.ItemId) : "*";
                return Happened($"{what}:{person}:{item}", c.Number, c.Number2);
            }
            case GameConditionKind.SoldTo:
            case GameConditionKind.TradedTo:
            case GameConditionKind.TradedWithin:
            {
                string what = c.Kind == GameConditionKind.SoldTo ? "sold" : "traded";
                string person = c.Kind == GameConditionKind.TradedWithin || c.CharacterId == GameCondition.Anyone || who is null ? "*" : who.Id.ToString();
                string item = c.Item is { ItemId: >= 0 } ? ItemKey(c.Item.Category, c.Item.ItemId) : "*";
                return Happened($"{what}:{person}:{item}", c.Number, c.Number2);
            }
            case GameConditionKind.EventExecuted: return Happened($"event:{c.EventName}", c.Number, c.Number2);
            case GameConditionKind.EventPageExecuted: return Happened($"eventpage:{_evaluatingEventName}:{c.Number}", 1, c.Number2);
            case GameConditionKind.EventNamePageExecuted: return Happened($"eventpage:{c.EventName}:{c.Number}", 1, c.Number2);
            case GameConditionKind.DoorOpen:
                if (c.Item is { Category: ReferencedItemCategory.Container, ItemId: >= 0 } chest)
                    return _openContainers.Any(g => g.Data.Id == chest.ItemId) == c.On;
                return _map.Objects.Any(o => o.ContainerInstanceId < 0 && (c.Item is not { ItemId: >= 0 } || o.ObjectId == c.Item.ItemId)
                    && DefinitionOf(o) is { Type: ObjectType.Door } && IsDoorOpen(o) == c.On);
        }
        return false;
    }

    /// <summary>A page set to "Until Condition Met" keeps going while its Until conditions aren't all met yet.</summary>
    private bool UntilStillGoing(BookEventConditions conditions) =>
        conditions.CycleCondition != CycleConditionType.UntilConditionMet || conditions.UntilConditions.Count == 0
        || !GameConditions.EvaluateAll(conditions.UntilConditions, EvaluateGameCondition);
}
