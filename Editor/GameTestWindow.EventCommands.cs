using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// CommandList part 2, the commands that finish at once:
///  - Give Item: Exult's Actor::add for the receiver (the same rules as a
///    drop on them - weight for a party member, then pack / pouch / hands),
///    then each other party member in turn (the usecode give-to-party
///    pattern, UI_add_party_items); nobody can take it - it is put down at
///    the receiver's feet.
///  - Change Stat / Reset Stat: every change is remembered by how much it
///    actually moved the stat, so a temporary one (or a reset) takes back
///    exactly that much and keeps whatever else changed it meanwhile.
///  - Train: Ultima 7's trainers - training points checked, then the money,
///    both paid, the stats raised.
///  - Kill Character: a fatal blow 0.05 s later (the hit flash) - death.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>One Change Stat still in force: how much it moved an Integer stat, or what a String stat was before and became.</summary>
    private sealed class StatChange
    {
        public required CharacterDefinition Who;
        public required StatDefinition Stat;
        public int Delta;
        public string? Before, After;
        /// <summary>The game minute it's taken back at; null = for ever (until a Reset Stat).</summary>
        public long? UntilMinute;
    }

    private readonly List<StatChange> _statChanges = new();
    /// <summary>While set, a refused hand-over only says why in the status bar - nobody dragged anything, so no pointer flashes.</summary>
    private bool _quietRefusals;

    private void InitializeEventCommands()
    {
        _execContext.GameHandlers[EventCommandType.GiveItem] = GiveItemCommand;
        _execContext.GameHandlers[EventCommandType.ChangeStat] = ChangeStatCommand;
        _execContext.GameHandlers[EventCommandType.ResetStat] = ResetStatCommand;
        _execContext.GameHandlers[EventCommandType.Train] = TrainCommand;
        _execContext.GameHandlers[EventCommandType.KillCharacter] = KillCharacterCommand;
    }

    /// <summary>A line of a command said in a dialog box by <paramref name="speaker"/> - one set for that character in the command first, else the command's own. Empty: nothing is said.</summary>
    private void SayCommandLine(EventCommand command, string lineKey, CharacterDefinition? speaker)
    {
        string speakerKey = speaker == _player ? WhoChoice.Player : speaker?.Id.ToString() ?? "";
        string text = speaker is not null && command.Parameters.TryGetValue($"Line_{lineKey}_{speakerKey}", out var own) ? own : command.Get(lineKey);
        if (string.IsNullOrWhiteSpace(text)) return;
        string speakerId = speaker is null ? "" : speaker == _player ? "Player" : speaker.Id.ToString();
        if (!QueueMessage(new EventCommand { Type = EventCommandType.ShowText, Parameters = { ["Text"] = text, ["SpeakerId"] = speakerId } }) && speaker is not null)
            ShowFloatingText(speaker, text, 3000); // no box for them (out of sight): the words over their head, as a bark
    }

    // ---------- Give Item ----------

    private bool GiveItemCommand(EventCommand command)
    {
        var giver = CharacterDefinitionFor(command.Get("Giver"));
        var receiver = CharacterDefinitionFor(command.Get("Receiver"));
        if (giver is null || receiver is null || giver == receiver || IsDead(receiver)) return false;
        if (!Enum.TryParse<ReferencedItemCategory>(command.Get("Category"), out var category) || !int.TryParse(command.Get("ItemId"), out int itemId)) return false;
        int quantity = Math.Clamp(int.TryParse(command.Get("Quantity"), out int q) ? q : 1, 1, ObjectTypes.MaxQuantity);
        bool mustHave = command.Get("MustHave") == "True";

        var gifts = mustHave ? TakeFromCarrier(giver, category, itemId, quantity) : MakeGifts(category, itemId, quantity);
        if (gifts is null)
        {
            StatusText.Text = $"{giver.Name} hasn't got {quantity} of it to give.";
            SayCommandLine(command, "NotHaveText", giver);
            return false;
        }
        if (mustHave) { RefreshAfterMove(); FireGlobalInventoryStateTrigger(BookTriggerConditionBased.ByNotHavingAnItem); }

        bool partyHelps = receiver == _player || receiver.IsInParty;
        var helpers = partyHelps ? PartyAndPlayer().Where(c => c != receiver && !IsDead(c)).ToList() : new List<CharacterDefinition>();
        bool saidCantCarry = false, saidNobody = false;
        var spoken = new HashSet<CharacterDefinition>(ReferenceEqualityComparer.Instance);
        foreach (var gift in gifts)
        {
            if (HandOver(gift, receiver)) continue;
            bool taken = false;
            foreach (var helper in helpers)
            {
                if (!HandOver(gift, helper)) continue;
                if (spoken.Add(helper)) SayCommandLine(command, "PartyTryText", helper); // "I shall carry it."
                taken = true;
                break;
            }
            if (taken) continue;
            // Nobody can: it goes down at the receiver's feet.
            if (partyHelps && !saidNobody) { SayCommandLine(command, "NobodyText", giver); saidNobody = true; }
            if (!partyHelps && !saidCantCarry) { SayCommandLine(command, "CantCarryText", giver); saidCantCarry = true; }
            PutDownAt(gift, receiver);
        }
        RefreshAfterMove();
        return true;
    }

    /// <summary>Actor::add for a gift, through the same rules as a drop on them (weight, pack, pouch, hands) - quietly.</summary>
    private bool HandOver(InventoryItemInstance gift, CharacterDefinition to)
    {
        var holder = new InventoryItemInstance();
        holder.Contents.Add(gift);
        _quietRefusals = true;
        try { return DropOnCharacter(new ContainerOrigin(holder, new ContainerData(), gift), to); }
        finally { _quietRefusals = false; }
    }

    /// <summary>Put down where they stand - or at a cell - resting on whatever is there (a table top), on their floor.</summary>
    private void PutDownAt(InventoryItemInstance gift, CharacterDefinition at, (int X, int Y)? cell = null)
    {
        var (x, y) = cell ?? CellOf(at);
        if (!InsideMap(x, y)) (x, y) = (_playerX, _playerY);
        var entity = CreateMapEntityFor(gift);
        var placedItem = entity as PlacedItemInstance;
        var placedObject = entity as ObjectInstance;
        if (placedObject is not null) { placedObject.ContainerInstanceId = -1; placedObject.InstanceId = _map.GetNextInstanceId(); }
        AddMapEntity(placedItem, placedObject);
        SetMapPosition(placedItem, placedObject, x, y, 0, 0);
        var body = BodyOf(placedItem, placedObject);
        SetZ(entity, StandingLiftOnFloor(new UnitRect(body.Left, body.Top, body.Width, body.Depth), MapFloors.FloorOfLift(LiftOf(at)), entity));
        ForgetHomeFloor(entity);
        SettleWorld();
        TilesUnderDropped(placedItem, placedObject); // By Item Dropped On It, as when the player puts something down
        StatusText.Text = $"\"{DollLook.ItemName(new ItemReference { Category = gift.Category, ItemId = gift.ItemId }, _objects, _books, _containers)}\" is put down at {at.Name}'s feet.";
    }

    /// <summary>Given without being owned: a stack of a stackable thing, otherwise one each. A container comes with its Starting Contents.</summary>
    private List<InventoryItemInstance> MakeGifts(ReferencedItemCategory category, int itemId, int quantity)
    {
        bool stacks = category == ReferencedItemCategory.Object && CanCombine(ObjectDef(itemId));
        InventoryItemInstance Make(int count) => new()
        {
            Category = category, ItemId = itemId, Quantity = count,
            Contents = category == ReferencedItemCategory.Container ? SeedStartingContents(itemId) : new List<InventoryItemInstance>()
        };
        return stacks ? new List<InventoryItemInstance> { Make(quantity) } : Enumerable.Range(0, quantity).Select(_ => Make(1)).ToList();
    }

    /// <summary>Takes this many out of everything a character carries (any depth, hands and slots too), or null - nothing taken - if they haven't got that many.</summary>
    private List<InventoryItemInstance>? TakeFromCarrier(CharacterDefinition carrier, ReferencedItemCategory category, int itemId, int quantity)
    {
        var found = new List<(InventoryItemInstance Item, List<InventoryItemInstance> List)>();
        void Walk(List<InventoryItemInstance> list)
        {
            foreach (var item in list)
            {
                if (item.Category == category && item.ItemId == itemId) found.Add((item, list));
                Walk(item.Contents);
            }
        }
        Walk(carrier.Inventory);
        if (found.Sum(f => Math.Max(1, f.Item.Quantity)) < quantity) return null;

        var gifts = new List<InventoryItemInstance>();
        int left = quantity;
        foreach (var (item, list) in found)
        {
            if (left <= 0) break;
            int have = Math.Max(1, item.Quantity);
            if (have > left)
            {
                item.Quantity = have - left; // the stack shrinks; the rest is given
                gifts.Add(new InventoryItemInstance { Category = category, ItemId = itemId, Quantity = left, FacingRuntime = item.FacingRuntime });
                left = 0;
                break;
            }
            if (ReferenceEquals(list, carrier.Inventory)) RemoveFromCharacter(carrier, item); // out of its hand / slot too
            else list.Remove(item);
            gifts.Add(item); // the very thing, as it is (its contents, charges, damage)
            left -= have;
        }
        return gifts;
    }

    // ---------- Change Stat / Reset Stat ----------

    private StatDefinition? StatById(string id) => _stats.Stats.FirstOrDefault(s => s.Id.ToString() == id);

    private int IntStat(CharacterDefinition who, StatDefinition stat) =>
        who.StatValues.TryGetValue(stat.Id, out var text) && int.TryParse(text, out int value) ? value : 0;

    /// <summary>An Integer stat set to a value (never below 0); Hits going down is a hurt - the flash, and at 0 a knock-out or death.</summary>
    private void SetIntStat(CharacterDefinition who, StatDefinition stat, int value, string cause)
    {
        value = Math.Max(StatDatabase.BuiltInMinimum, value);
        int old = IntStat(who, stat);
        if (stat.Kind == BuiltInStatKind.Hits && value < old && GetHp(who) is not null)
        {
            Damage(who, old - value, cause);
            return;
        }
        who.StatValues[stat.Id] = value.ToString();
    }

    private bool ChangeStatCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who) || StatById(command.Get("StatId")) is not { } stat) return false;
        long? until = null;
        if (command.Get("Temporary") == "True" && int.TryParse(command.Get("Duration"), out int duration) && duration > 0)
            until = TotalMinutes() + (command.Get("Unit") == "Hours" ? duration * 60L : duration);

        if (stat.Type == VariableValueType.String)
        {
            string before = who.StatValues.TryGetValue(stat.Id, out var text) ? text : "";
            who.StatValues[stat.Id] = command.Get("Value");
            _statChanges.Add(new StatChange { Who = who, Stat = stat, Before = before, After = command.Get("Value"), UntilMinute = until });
        }
        else
        {
            if (!int.TryParse(command.Get("Value"), out int amount)) return false;
            int old = IntStat(who, stat);
            int wanted = command.Get("Operation") switch { "Set" => amount, "Subtract" => old - amount, _ => old + amount };
            SetIntStat(who, stat, wanted, "a change of stats");
            int delta = IntStat(who, stat) - old; // what it really moved (0 is the floor)
            if (delta != 0) _statChanges.Add(new StatChange { Who = who, Stat = stat, Delta = delta, UntilMinute = until });
        }
        RefreshStatsViews();
        return true;
    }

    private bool ResetStatCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who) return false;
        string statId = command.Get("StatId");
        var undo = _statChanges.Where(c => c.Who == who && (statId == "All" || c.Stat.Id.ToString() == statId)).ToList();
        for (int i = undo.Count - 1; i >= 0; i--) TakeBack(undo[i]); // newest first, so a String stat ends as it began
        RefreshStatsViews();
        return true;
    }

    /// <summary>A Change Stat taken back: an Integer stat moves back by what it moved (training and the rest since are kept); a String stat goes back if nothing changed it since.</summary>
    private void TakeBack(StatChange change)
    {
        _statChanges.Remove(change);
        if (IsDead(change.Who)) return;
        if (change.Stat.Type == VariableValueType.String)
        {
            if (change.Who.StatValues.TryGetValue(change.Stat.Id, out var now) && now == change.After) change.Who.StatValues[change.Stat.Id] = change.Before ?? "";
            return;
        }
        SetIntStat(change.Who, change.Stat, IntStat(change.Who, change.Stat) - change.Delta, "a change of stats wearing off");
    }

    /// <summary>Each game minute: temporary changes whose time is up are taken back.</summary>
    private void ExpireStatChanges(long minute)
    {
        var due = _statChanges.Where(c => c.UntilMinute is long until && minute >= until).ToList();
        if (due.Count == 0) return;
        foreach (var change in due) TakeBack(change);
        RefreshStatsViews();
    }

    /// <summary>Open paperdolls and stats paperdolls show the new numbers.</summary>
    private void RefreshStatsViews() => RefreshAfterMove();

    // ---------- Train ----------

    private bool TrainCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Trainee")) is not { } trainee || IsDead(trainee)) return false;
        var trainer = CharacterDefinitionFor(command.Get("Trainer"));
        int points = int.TryParse(command.Get("Points"), out int p) ? Math.Max(0, p) : 0;
        int price = int.TryParse(command.Get("Price"), out int m) ? Math.Max(0, m) : 0;
        var pointsStat = _stats.Stats.FirstOrDefault(s => s.Kind == BuiltInStatKind.TrainingPoints);

        // Ultima 7's trainers: not enough training points - "thou dost need more experience"; then not enough gold.
        if (points > 0 && (pointsStat is null || IntStat(trainee, pointsStat) < points))
        {
            StatusText.Text = $"{trainee.Name} hasn't enough training points ({(pointsStat is null ? 0 : IntStat(trainee, pointsStat))} of {points}).";
            SayCommandLine(command, "NoExperienceText", trainer);
            return false;
        }
        var moneyIds = int.TryParse(command.Get("CurrencyId"), out int currencyId)
            ? _objects.Objects.Where(o => o.Type == ObjectType.Money && o.Money.CurrencyId == currencyId).Select(o => o.Id).ToList()
            : new List<int>();
        if (price > 0 && moneyIds.Sum(PartyCount) < price)
        {
            StatusText.Text = $"The party hasn't enough money ({moneyIds.Sum(PartyCount)} of {price}).";
            SayCommandLine(command, "NoMoneyText", trainer);
            return false;
        }

        int owed = price;
        foreach (int id in moneyIds)
        {
            int take = Math.Min(owed, PartyCount(id));
            if (take <= 0) continue;
            TakeFromParty(id, take);
            owed -= take;
        }
        if (pointsStat is not null && points > 0) trainee.StatValues[pointsStat.Id] = (IntStat(trainee, pointsStat) - points).ToString();
        var raised = new List<string>();
        foreach (var (key, value) in command.Parameters.Where(kv => kv.Key.StartsWith("Gain_")))
        {
            if (StatById(key[5..]) is not { Type: VariableValueType.Integer } stat || !int.TryParse(value, out int gain) || gain <= 0) continue;
            trainee.StatValues[stat.Id] = (IntStat(trainee, stat) + gain).ToString();
            raised.Add($"{stat.Name} +{gain}");
        }
        RefreshStatsViews();
        StatusText.Text = $"{trainee.Name} trains: {string.Join(", ", raised)}.";
        SayCommandLine(command, "Description", trainer);
        Record($"trained:{trainee.Id}");
        return true;
    }

    // ---------- Kill Character ----------

    private bool KillCharacterCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who)) return false;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (IsDead(who)) return;
            // The fatal blow: more than enough to go past -(Strength / 3), so it kills rather than knocks out.
            if (GetHp(who) is int hp) Damage(who, Math.Max(1, hp + MaxHp(who) / 3 + 1), "a fatal blow");
            if (!IsDead(who)) Die(who, "a fatal blow");
        };
        timer.Start();
        return true;
    }
}
