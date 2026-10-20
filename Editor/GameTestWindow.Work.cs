using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The work schedules, after Exult's (schedule.cc): Bake, Sew, Blacksmith and
/// Lab work the crafting stations around their spot; Farm and Miner harvest
/// (and sow) the plants there. Nothing is faked - a worker really does what
/// the player would:
///  - its things: its own (Map Editor > Belongs to) first, then anything that
///    belongs to nobody, inside its work area (the schedule's zone, or so many
///    tiles round its spot) - never someone else's;
///  - a station's recipe it can make: the ingredients it carries, else fetched
///    from the area's chests and tables (walked to, taken as the player takes
///    them: weight, room in the pack and hands);
///  - the tool the recipe needs: in its pack, or fetched; readied in its hand.
///    Hands full: what's in them goes into a bag it carries, else onto the
///    nearest table (its own first), else at its feet - picked up again later;
///  - the work itself: at the station, its working sprite and sound, the
///    worker's work pose, the ingredients used up; what it makes appears on
///    the station (the station's By Made Something event runs);
///  - then it carries what it made to its schedule's output place (a chest or
///    a table) - or leaves it on the station;
///  - when its schedule changes, it picks up what it put down and puts back
///    the tools it took, where it took them from (Exult's Tool_schedule::ending).
/// Each round is an action chain (GameTestWindow.Actions.cs): the same walking,
/// picking up and putting down as the Actions commands.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>A tool taken for the work: from a chest (put back in it), or from a spot on the map.</summary>
    private sealed record BorrowedTool(int ObjectId, PlacedItemInstance? Container, (int X, int Y)? Cell);

    private sealed class WorkState
    {
        public bool RoundRunning;
        public readonly List<BorrowedTool> Borrowed = new();
        /// <summary>What it put down to free its hands (on a table, at its feet) - picked up again when the work is over.</summary>
        public readonly List<object> SetDown = new();
        /// <summary>What it harvested (Farm, Miner) - carried to its output place.</summary>
        public readonly HashSet<int> ProduceIds = new();
        public bool SaidIdle;
    }

    private readonly Dictionary<CharacterDefinition, WorkState> _work = new(ReferenceEqualityComparer.Instance);
    /// <summary>Stations a worker is working at now (the player can't use them meanwhile).</summary>
    private readonly Dictionary<ObjectInstance, CharacterDefinition> _stationsInUse = new(ReferenceEqualityComparer.Instance);
    /// <summary>Plants and soil a farmer is on its way to, so two farmers don't go for the same one.</summary>
    private readonly Dictionary<ObjectInstance, CharacterDefinition> _fieldClaims = new(ReferenceEqualityComparer.Instance);

    private WorkState WorkOf(CharacterDefinition who) => _work.TryGetValue(who, out var w) ? w : _work[who] = new WorkState();

    private static bool IsStationWork(ScheduleType? type) => type is ScheduleType.Bake or ScheduleType.Sew or ScheduleType.Blacksmith or ScheduleType.Lab;
    private static bool IsFieldWork(ScheduleType? type) => type is ScheduleType.Farm or ScheduleType.Miner;
    private static bool IsWorkSchedule(ScheduleType? type) => IsStationWork(type) || IsFieldWork(type);

    // ================= The work area and whose things are whose =================

    private MapZone? WorkZone(MapCharacter mapCharacter)
    {
        var entry = mapCharacter.Npc.Entry;
        if (entry is null) return null;
        if (entry.ZoneId >= 0) return _map.Zones.FirstOrDefault(z => z.Id == entry.ZoneId);
        if (entry.ZonePurpose.Length > 0)
            return Zones.WithPurpose(_map.Zones, entry.ZonePurpose).FirstOrDefault(z => z.Contains(mapCharacter.Npc.Dest.X, mapCharacter.Npc.Dest.Y));
        return null;
    }

    /// <summary>Inside its work area, on its floor: the schedule's zone, or so many tiles round its spot.</summary>
    private bool InWorkArea(MapCharacter mapCharacter, int x, int y, int lift)
    {
        if (MapFloors.FloorOfLift(lift) != MapFloors.FloorOfLift(mapCharacter.Lift)) return false;
        if (WorkZone(mapCharacter) is { } zone) return zone.Contains(x, y);
        int reach = CellsForTiles(Math.Max(1, mapCharacter.Npc.Entry?.WorkAreaTiles ?? 12));
        var dest = mapCharacter.Npc.Dest;
        return Math.Max(Math.Abs(x - dest.X), Math.Abs(y - dest.Y)) <= reach;
    }

    /// <summary>Who a placed item (a chest, a book) belongs to: this copy's own owner, else its editor's Belong To.</summary>
    private int OwnerOfPlaced(PlacedItemInstance item) =>
        item.BelongsToId >= 0 ? item.BelongsToId
        : (item.Category switch
        {
            ReferencedItemCategory.Container => ContainerDef(item.ItemId)?.BelongToId,
            ReferencedItemCategory.Book => BookDef(item.ItemId)?.BelongToId,
            _ => null
        }) ?? -1;

    /// <summary>A worker uses its own things and nobody's - never someone else's.</summary>
    private static bool MineOrNobodys(int ownerId, CharacterDefinition me) => ownerId < 0 || ownerId == me.Id;

    private static int Distance(MapCharacter mapCharacter, int x, int y) => Math.Max(Math.Abs(x - mapCharacter.Placement.X), Math.Abs(y - mapCharacter.Placement.Y));

    /// <summary>Its sounds are heard only near the player.</summary>
    private bool HeardFrom(CharacterDefinition who) => _player is not null && TilesBetween(who, _player) <= 24;

    /// <summary>The chests in its area it may take from (unlocked), its own first, then the nearest.</summary>
    private List<(PlacedItemInstance Chest, ContainerData Data)> WorkContainers(MapCharacter mapCharacter)
    {
        var me = mapCharacter.Character;
        var result = new List<(PlacedItemInstance, ContainerData)>();
        foreach (var item in _map.Items)
        {
            if (item.Category != ReferencedItemCategory.Container || !InWorkArea(mapCharacter, item.X, item.Y, item.Z)) continue;
            if (!MineOrNobodys(OwnerOfPlaced(item), me) || ContainerDef(item.ItemId) is not { } data || IsLockedNow(item, data)) continue;
            EnsureContainerSeeded(item, data); // never opened: its starting contents are in it
            result.Add((item, data));
        }
        return result.OrderBy(c => OwnerOfPlaced(c.Item1) == me.Id ? 0 : 1).ThenBy(c => Distance(mapCharacter, c.Item1.X, c.Item1.Y)).ToList();
    }

    /// <summary>Things lying in its area (on tables, on the floor) it may take.</summary>
    private IEnumerable<ObjectInstance> LooseInArea(MapCharacter mapCharacter, Func<WorldObject, bool> what) =>
        _map.Objects.Where(o => o.ContainerInstanceId < 0 && InWorkArea(mapCharacter, o.X, o.Y, o.Z) && IsMovable(o)
                                && MineOrNobodys(OwnerOf(o), mapCharacter.Character) && DefinitionOf(o) is { } d && what(d))
            .OrderBy(o => OwnerOf(o) == mapCharacter.Character.Id ? 0 : 1).ThenBy(o => Distance(mapCharacter, o.X, o.Y));

    private static int CountIn(List<InventoryItemInstance> list, Func<InventoryItemInstance, bool> what) =>
        list.Sum(i => (what(i) ? Math.Max(1, i.Quantity) : 0) + CountIn(i.Contents, what));

    private static Func<InventoryItemInstance, bool> IsObject(int objectId) => i => i.Category == ReferencedItemCategory.Object && i.ItemId == objectId;

    private int CountCarried(CharacterDefinition who, int objectId) => CountIn(who.Inventory, IsObject(objectId));

    /// <summary>How many it could get: carried, in its area's chests, lying in its area.</summary>
    private int AvailableFor(MapCharacter mapCharacter, int objectId, List<(PlacedItemInstance Chest, ContainerData Data)> chests) =>
        CountCarried(mapCharacter.Character, objectId)
        + chests.Sum(c => CountIn(c.Chest.Contents, IsObject(objectId)))
        + LooseInArea(mapCharacter, d => d.Id == objectId).Sum(o => Math.Max(1, o.Quantity));

    private static bool IsToolOfKind(WorldObject? obj, string kind) =>
        obj is { Type: ObjectType.Tool } && string.Equals(obj.Tool.ToolKind.Trim(), kind.Trim(), StringComparison.OrdinalIgnoreCase);

    private Func<InventoryItemInstance, bool> IsTool(string kind) => i => i.Category == ReferencedItemCategory.Object && IsToolOfKind(ObjectDef(i.ItemId), kind);

    private bool ToolAvailable(MapCharacter mapCharacter, string kind, List<(PlacedItemInstance Chest, ContainerData Data)> chests) =>
        kind.Trim().Length == 0
        || CountIn(mapCharacter.Character.Inventory, IsTool(kind)) > 0
        || chests.Any(c => CountIn(c.Chest.Contents, IsTool(kind)) > 0)
        || LooseInArea(mapCharacter, d => IsToolOfKind(d, kind)).Any();

    /// <summary>The pose it works in: its schedule's, else Attacking Melee (Exult's workers strike with their tools).</summary>
    private static string WorkPoseOf(MapCharacter mapCharacter) =>
        mapCharacter.Npc.Entry?.WorkPose is { Length: > 0 } pose ? pose : "Attacking Melee";

    // ================= What next =================

    /// <summary>A work schedule's now_what: the next round of work, or - nothing to do - a word and a while later again.</summary>
    private void WorkNowWhat(MapCharacter mapCharacter)
    {
        var work = WorkOf(mapCharacter.Character);
        if (work.RoundRunning) return;
        var type = mapCharacter.Npc.Activity;
        bool started = IsStationWork(type) ? StartStationRound(mapCharacter) : StartFieldRound(mapCharacter);
        if (started) { work.SaidIdle = false; return; }
        if (!work.SaidIdle)
        {
            work.SaidIdle = true;
            if (mapCharacter.Character.IsHuman) Say(mapCharacter, IsFieldWork(type) ? "Nothing to harvest yet." : "Nothing to work with...");
        }
        if (type == ScheduleType.Lab && TrySitAndRead(mapCharacter)) return; // Exult's Lab_schedule: sits and reads between brews
        if (!_seats.ContainsKey(mapCharacter.Character)) ShowPose(mapCharacter.Character, "Idle");
        mapCharacter.Npc.WaitMs = 15_000 + Rng.Next(10_000); // look again a little later
    }

    /// <summary>One round of work as an action chain; when it's over (done or not), the next now_what.</summary>
    private void RunWorkRound(MapCharacter mapCharacter, params Func<Func<double, bool?>?>[] steps)
    {
        var me = mapCharacter.Character;
        var work = WorkOf(me);
        work.RoundRunning = true;
        var chain = Chain(steps);
        BeginScriptAction(me, elapsed =>
        {
            if (mapCharacter.Npc.InCombat || mapCharacter.Npc.Fleeing) return false; // a fight comes first
            return chain(elapsed);
        }, ok =>
        {
            work.RoundRunning = false;
            ReleaseStationsOf(me);
            foreach (var claim in _fieldClaims.Where(c => c.Value == me).Select(c => c.Key).ToList()) _fieldClaims.Remove(claim);
            if (!_seats.ContainsKey(me)) ShowPose(me, "Idle");
            if (!ok) mapCharacter.Npc.WaitMs = 4000 + Rng.Next(3000);
            else if (mapCharacter.Npc.Activity == ScheduleType.Lab && Rng.Next(2) == 0 && TrySitAndRead(mapCharacter)) { }
            else mapCharacter.Npc.WaitMs = 600;
        }, afterEvent: true);
    }

    // ================= Stations (Bake, Sew, Blacksmith, Lab) =================

    private bool StationBusy(ObjectInstance station, CharacterDefinition me) =>
        _craftingJobs.Any(j => ReferenceEquals(j.Station, station)) || (_stationsInUse.TryGetValue(station, out var user) && user != me);

    private void ReleaseStationsOf(CharacterDefinition me)
    {
        foreach (var station in _stationsInUse.Where(s => s.Value == me).Select(s => s.Key).ToList())
        {
            _stationsInUse.Remove(station);
            if (!_craftingJobs.Any(j => ReferenceEquals(j.Station, station)) && _map.Objects.Contains(station)) { station.IsLitRuntime = false; RefreshObjectLook(station); }
        }
    }

    /// <summary>The first station in its area (its own first, then the nearest) with a recipe it can make now.</summary>
    private bool StartStationRound(MapCharacter mapCharacter)
    {
        var me = mapCharacter.Character;
        var chests = WorkContainers(mapCharacter);
        var stations = _map.Objects
            .Where(o => o.ContainerInstanceId < 0 && DefinitionOf(o) is { Type: ObjectType.CraftingStation } && InWorkArea(mapCharacter, o.X, o.Y, o.Z)
                        && MineOrNobodys(OwnerOf(o), me) && !StationBusy(o, me))
            .OrderBy(o => OwnerOf(o) == me.Id ? 0 : 1).ThenBy(o => Distance(mapCharacter, o.X, o.Y)).ToList();
        // Later steps first: a recipe that uses something made in this workshop (thread at the loom) before one that starts from raw stuff (cotton at the wheel).
        var madeHere = stations.SelectMany(s => DefinitionOf(s)!.Crafting.Recipes).Select(r => r.OutputObjectId).Where(id => id >= 0).ToHashSet();
        var candidates = stations.SelectMany((s, order) => DefinitionOf(s)!.Crafting.Recipes.Where(r => r.OutputObjectId >= 0).Select(r => (Station: s, Recipe: r, Order: order)))
            .OrderBy(c => c.Recipe.Inputs.Any(i => madeHere.Contains(i.ObjectId)) ? 0 : 1).ThenBy(c => c.Order).ToList();
        foreach (var (station, recipe, _) in candidates)
        {
            var obj = DefinitionOf(station)!;
            {
                if (recipe.Inputs.Any(i => i.ObjectId >= 0 && AvailableFor(mapCharacter, i.ObjectId, chests) < Math.Max(1, i.Quantity))) continue;
                if (!ToolAvailable(mapCharacter, recipe.ToolKind, chests)) continue;
                var steps = new List<Func<Func<double, bool?>?>>();
                foreach (var input in recipe.Inputs.Where(i => i.ObjectId >= 0)) steps.Add(Fetch(mapCharacter, input.ObjectId, Math.Max(1, input.Quantity)));
                if (recipe.ToolKind.Trim().Length > 0) { steps.Add(GetTool(mapCharacter, recipe.ToolKind)); steps.Add(ReadyTool(mapCharacter, recipe.ToolKind)); }
                steps.Add(GoTo(me, station));
                ObjectInstance? made = null;
                steps.Add(WorkAtStation(mapCharacter, station, obj, recipe, product => made = product));
                steps.Add(Deliver(mapCharacter, () => made));
                RunWorkRound(mapCharacter, steps.ToArray());
                return true;
            }
        }
        return false;
    }

    /// <summary>Gets this many of a thing into its pack: what it carries, then from its area's chests and tables - walking to each.</summary>
    private Func<Func<double, bool?>?> Fetch(MapCharacter mapCharacter, int objectId, int quantity) => () =>
    {
        Func<double, bool?>? current = null;
        int tries = 0;
        return elapsed =>
        {
            while (true)
            {
                if (current is null)
                {
                    int need = quantity - CountCarried(mapCharacter.Character, objectId);
                    if (need <= 0) return true;
                    if (++tries > 8) return false;
                    current = FetchOnce(mapCharacter, objectId, need);
                    if (current is null) return false; // nowhere left to get it
                }
                var result = current(elapsed);
                if (result is null) return null;
                current = null;
                elapsed = 0;
                if (result == false) return false;
            }
        };
    };

    private Func<double, bool?>? FetchOnce(MapCharacter mapCharacter, int objectId, int need)
    {
        var me = mapCharacter.Character;
        var chest = WorkContainers(mapCharacter).FirstOrDefault(c => CountIn(c.Chest.Contents, IsObject(objectId)) > 0);
        if (chest.Chest is not null)
            return Chain(GoTo(me, chest.Chest), PoseOnce(me, "Picking"), Do(() => TakeOutOf(me, chest.Chest, chest.Data, IsObject(objectId), need) > 0));
        var loose = LooseInArea(mapCharacter, d => d.Id == objectId).FirstOrDefault();
        if (loose is not null)
            return Chain(GoTo(me, loose), PoseOnce(me, "Picking"), Do(() => PickUp(me, loose)));
        return null;
    }

    /// <summary>Takes up to this many matching things out of a chest into its pack (as Pick Item From Container does). How many it got.</summary>
    private int TakeOutOf(CharacterDefinition who, PlacedItemInstance chest, ContainerData data, Func<InventoryItemInstance, bool> what, int quantity)
    {
        if (!_map.Items.Contains(chest) || IsLockedNow(chest, data)) return 0;
        EnsureContainerSeeded(chest, data);
        int taken = 0, left = quantity;
        foreach (var item in chest.Contents.ToList())
        {
            if (left <= 0) break;
            if (!what(item)) continue;
            int have = Math.Max(1, item.Quantity);
            if (have > left)
            {
                // Part of a stack: that many split off and handed over; refused - back on the stack.
                item.Quantity = have - left;
                var part = new InventoryItemInstance { Category = item.Category, ItemId = item.ItemId, Quantity = left, BelongsToId = item.BelongsToId };
                if (HandOver(part, who)) { taken += left; left = 0; }
                else item.Quantity = have;
                continue;
            }
            _quietRefusals = true;
            bool ok;
            try { ok = DropOnCharacter(new ContainerOrigin(chest, data, item), who); }
            finally { _quietRefusals = false; }
            if (!ok) continue; // too heavy / no room: it stays inside
            taken += have;
            left -= have;
        }
        RefreshAfterMove();
        return taken;
    }

    // ---- Tools ----

    /// <summary>A tool of this kind into its pack: carried already, else from its area's chests or tables (remembered, to be put back).</summary>
    private Func<Func<double, bool?>?> GetTool(MapCharacter mapCharacter, string kind) => () =>
    {
        var me = mapCharacter.Character;
        if (CountIn(me.Inventory, IsTool(kind)) > 0) return _ => true;
        var chest = WorkContainers(mapCharacter).FirstOrDefault(c => CountIn(c.Chest.Contents, IsTool(kind)) > 0);
        if (chest.Chest is not null)
        {
            var (box, data) = chest;
            return Chain(GoTo(me, box), PoseOnce(me, "Picking"), Do(() =>
            {
                var tool = FindIn(box.Contents, IsTool(kind));
                if (tool is null || TakeOutOf(me, box, data, i => ReferenceEquals(i, tool), 1) == 0) return false;
                WorkOf(me).Borrowed.Add(new BorrowedTool(tool.ItemId, box, null));
                return true;
            }));
        }
        var loose = LooseInArea(mapCharacter, d => IsToolOfKind(d, kind)).FirstOrDefault();
        if (loose is not null)
        {
            var cell = (loose.X, loose.Y);
            int id = loose.ObjectId;
            return Chain(GoTo(me, loose), PoseOnce(me, "Picking"), Do(() =>
            {
                if (!PickUp(me, loose)) return false;
                WorkOf(me).Borrowed.Add(new BorrowedTool(id, null, cell));
                return true;
            }));
        }
        return null;
    };

    private static InventoryItemInstance? FindIn(List<InventoryItemInstance> list, Func<InventoryItemInstance, bool> what)
    {
        foreach (var item in list)
        {
            if (what(item)) return item;
            if (FindIn(item.Contents, what) is { } inside) return inside;
        }
        return null;
    }

    /// <summary>Where a carried thing is: the item and the container holding it (null = straight on them).</summary>
    private static (InventoryItemInstance Item, InventoryItemInstance? Holder)? FindCarriedWhere(CharacterDefinition who, Func<InventoryItemInstance, bool> what)
    {
        (InventoryItemInstance, InventoryItemInstance?)? Walk(List<InventoryItemInstance> list, InventoryItemInstance? holder)
        {
            foreach (var item in list)
            {
                if (what(item)) return (item, holder);
                if (Walk(item.Contents, item) is { } inside) return inside;
            }
            return null;
        }
        return Walk(who.Inventory, null);
    }

    /// <summary>
    /// The tool readied in its right hand. Hands full: what's in the way goes
    /// into a bag it carries, else onto the nearest table in its area (its own
    /// first), else at its feet - and is picked up again when the work is over.
    /// </summary>
    private Func<Func<double, bool?>?> ReadyTool(MapCharacter mapCharacter, string kind) => () =>
    {
        var me = mapCharacter.Character;
        if (FindCarriedWhere(me, IsTool(kind)) is not { } found) return null;
        var hands = new List<object> { EquipmentSlot.RightHand };
        if (IsPaperdollItemTwoHanded(found.Item) || IsPaperdollLeftHandLocked(me)) hands.Add(EquipmentSlot.LeftHand);
        bool InHand() => hands.Any(h => TryGetEquippedInstanceId(me, h, out int id) && id == found.Item.InstanceId);
        if (found.Holder is null && InHand()) return _ => true;
        var steps = new List<Func<Func<double, bool?>?>>();
        foreach (var hand in hands)
        {
            if (!TryGetEquippedInstanceId(me, hand, out int heldId) || me.Inventory.FirstOrDefault(i => i.InstanceId == heldId) is not { } held || ReferenceEquals(held, found.Item)) continue;
            if (TryStowInBag(me, hand, held)) continue;
            var table = _map.Objects.Where(o => o.ContainerInstanceId < 0 && DefinitionOf(o) is { Type: ObjectType.Table } && InWorkArea(mapCharacter, o.X, o.Y, o.Z) && MineOrNobodys(OwnerOf(o), me))
                .OrderBy(o => OwnerOf(o) == me.Id ? 0 : 1).ThenBy(o => Distance(mapCharacter, o.X, o.Y)).FirstOrDefault();
            if (table is not null) steps.Add(GoTo(me, table));
            steps.Add(PoseOnce(me, "Picking"));
            steps.Add(Do(() =>
            {
                if (!me.Inventory.Contains(held)) return true;
                RemoveFromCharacter(me, held);
                WorkOf(me).SetDown.Add(PutDownAt(held, me, table is not null && _map.Objects.Contains(table) ? (table.X, table.Y) : null));
                RefreshAfterMove();
                return true;
            }));
        }
        steps.Add(Do(() =>
        {
            if (FindCarriedWhere(me, IsTool(kind)) is not { } now || OriginOfCarried(me, now.Item, now.Holder) is not { } origin) return false;
            if (now.Holder is null && hands.Any(h => TryGetEquippedInstanceId(me, h, out int id) && id == now.Item.InstanceId)) return true;
            EquipInSlot(origin, me, EquipmentSlot.RightHand);
            RefreshAfterMove();
            return true;
        }));
        return Chain(steps.ToArray());
    };

    // ---- The work itself ----

    /// <summary>At the station: the ingredients used up, its working look and sound, the worker at work; then what it makes appears on it.</summary>
    private Func<Func<double, bool?>?> WorkAtStation(MapCharacter mapCharacter, ObjectInstance station, WorldObject obj, CraftingRecipe recipe, Action<ObjectInstance?> made) => () =>
    {
        var me = mapCharacter.Character;
        if (!_map.Objects.Contains(station) || StationBusy(station, me)) return null;
        if (recipe.Inputs.Any(i => i.ObjectId >= 0 && CountCarried(me, i.ObjectId) < Math.Max(1, i.Quantity))) return null;
        foreach (var input in recipe.Inputs.Where(i => i.ObjectId >= 0)) TakeFromCarrier(me, ReferencedItemCategory.Object, input.ObjectId, Math.Max(1, input.Quantity));
        RefreshAfterMove();
        _stationsInUse[station] = me;
        station.IsLitRuntime = true; // working: its working sprite
        RefreshObjectLook(station);
        FaceCell(me, station.X, station.Y);
        string pose = WorkPoseOf(mapCharacter);
        double left = Math.Max(0.5, recipe.WorkSeconds) * 1000, beat = 0;
        return elapsed =>
        {
            if (!_map.Objects.Contains(station)) return false;
            left -= elapsed;
            if ((beat -= elapsed) <= 0 && left > 0)
            {
                beat = 800; // Exult's millstone: a turn, with its sound
                if (HeardFrom(me)) PlaySound(recipe.WorkSoundResourceId);
                ShowPose(me, pose, restart: true);
            }
            if (left > 0) return null;
            _stationsInUse.Remove(station);
            station.IsLitRuntime = false;
            RefreshObjectLook(station);
            ShowPose(me, "Idle");
            var product = SpawnObjectAt(recipe.OutputObjectId, Math.Max(1, recipe.OutputQuantity), station.X, station.Y);
            if (product is not null && OwnerOf(station) == me.Id) product.BelongsToId = me.Id; // made at its own station: its own
            made(product);
            FireObjectEvent(station, obj, BookTriggerConditionBased.ByAfterReadItem, mapCharacter); // By Made Something
            return true;
        };
    };

    // ---- Where what it makes goes ----

    /// <summary>Its schedule's output place: the chest at that spot (or right beside it), else the spot itself (a table, a counter). Null = leave it where it's made.</summary>
    private (PlacedItemInstance? Chest, (int X, int Y) Cell)? OutputPlace(MapCharacter mapCharacter)
    {
        var entry = mapCharacter.Npc.Entry;
        if (entry is not null && entry.OutputX >= 0 && entry.OutputY >= 0 && InsideMap(entry.OutputX, entry.OutputY))
        {
            var chest = _map.Items.Where(i => i.Category == ReferencedItemCategory.Container && Math.Max(Math.Abs(i.X - entry.OutputX), Math.Abs(i.Y - entry.OutputY)) <= 1)
                .OrderBy(i => Math.Abs(i.X - entry.OutputX) + Math.Abs(i.Y - entry.OutputY)).FirstOrDefault();
            return (chest, (entry.OutputX, entry.OutputY));
        }
        if (IsFieldWork(mapCharacter.Npc.Activity))
        {
            // A farmer with no output place: its own chest in the field (a granary), else it keeps what it harvested.
            var own = WorkContainers(mapCharacter).FirstOrDefault(c => OwnerOfPlaced(c.Chest) == mapCharacter.Character.Id);
            if (own.Chest is not null) return (own.Chest, (own.Chest.X, own.Chest.Y));
        }
        return null;
    }

    /// <summary>What it just made: picked up and carried to its output place - or left on the station.</summary>
    private Func<Func<double, bool?>?> Deliver(MapCharacter mapCharacter, Func<ObjectInstance?> product) => () =>
    {
        var me = mapCharacter.Character;
        var made = product();
        if (made is null || !_map.Objects.Contains(made) || OutputPlace(mapCharacter) is null) return _ => true;
        if (NeededInWorkshop(mapCharacter, made.ObjectId)) return _ => true; // thread for the loom: it stays on the station for the next step

        int id = made.ObjectId;
        return Chain(GoTo(me, made), PoseOnce(me, "Picking"), Do(() => PickUp(me, made)), DeliverCarried(mapCharacter, new[] { id }));
    };

    /// <summary>Something another recipe in its area uses (the spinner's thread, the loom's): a step on the way, not finished work.</summary>
    private bool NeededInWorkshop(MapCharacter mapCharacter, int objectId) =>
        _map.Objects.Any(o => o.ContainerInstanceId < 0 && DefinitionOf(o) is { Type: ObjectType.CraftingStation } d && InWorkArea(mapCharacter, o.X, o.Y, o.Z)
                              && MineOrNobodys(OwnerOf(o), mapCharacter.Character) && d.Crafting.Recipes.Any(r => r.Inputs.Any(i => i.ObjectId == objectId)));

    /// <summary>Everything it carries of these kinds, into the output chest or onto the output spot.</summary>
    private Func<Func<double, bool?>?> DeliverCarried(MapCharacter mapCharacter, IReadOnlyCollection<int> objectIds) => () =>
    {
        var me = mapCharacter.Character;
        if (OutputPlace(mapCharacter) is not { } place) return _ => true;
        if (place.Chest is { } chest)
            return Chain(GoTo(me, chest), PoseOnce(me, "Picking"), Do(() =>
            {
                if (!_map.Items.Contains(chest) || ContainerDef(chest.ItemId) is not { } data) return false;
                foreach (int id in objectIds)
                    while (FindCarriedWhere(me, IsObject(id)) is { } found && OriginOfCarried(me, found.Item, found.Holder) is { } origin)
                    {
                        _quietRefusals = true;
                        bool put;
                        try { put = TryPutIntoContainer(origin, data, chest, null); }
                        finally { _quietRefusals = false; }
                        if (!put) return Cancel($"{me.Name} can't fit any more into \"{data.Name}\".");
                    }
                StatusText.Text = $"{me.Name} puts the work away in \"{data.Name}\".";
                return true;
            }));
        var cell = place.Cell;
        return Chain(() => WalkTo(me, cell, beside: true) is { } walk ? e => { var r = walk(e); if (r == true) FaceCell(me, cell.X, cell.Y); return r; } : null,
            PoseOnce(me, "Picking"),
            Do(() =>
            {
                foreach (int id in objectIds)
                {
                    int count = CountCarried(me, id);
                    if (count > 0 && TakeFromCarrier(me, ReferencedItemCategory.Object, id, count) is { } items)
                        foreach (var item in items) PutDownAt(item, me, cell);
                }
                RefreshAfterMove();
                return true;
            }));
    };

    // ================= Fields (Farm, Miner) =================

    private static bool IsRipe(ObjectInstance plant, WorldObject obj) => plant.FrameRuntime >= Math.Max(0, obj.Plant.Stages.Count - 1);

    /// <summary>
    /// Exult's Farmer_schedule, real: the nearest ripe plant in its area (crops,
    /// an ore vein) - with the tool it needs - harvested, and what it gives
    /// picked up; carried to its output place when its arms are full (8 or
    /// more) or nothing is left to harvest. Then it sows empty soil, if it has
    /// the seeds (Plant: Sown from / in).
    /// </summary>
    private bool StartFieldRound(MapCharacter mapCharacter)
    {
        var me = mapCharacter.Character;
        var work = WorkOf(me);
        var chests = WorkContainers(mapCharacter);
        int carrying = work.ProduceIds.Sum(id => CountCarried(me, id));
        var ripe = _map.Objects
            .Where(o => o.ContainerInstanceId < 0 && DefinitionOf(o) is { Type: ObjectType.Plant } d && d.Plant.ProduceObjectId >= 0 && IsRipe(o, d)
                        && InWorkArea(mapCharacter, o.X, o.Y, o.Z) && MineOrNobodys(OwnerOf(o), me) && !_fieldClaims.ContainsKey(o)
                        && ToolAvailable(mapCharacter, d.Plant.HarvestToolKind, chests))
            .OrderBy(o => Distance(mapCharacter, o.X, o.Y)).FirstOrDefault();
        if (carrying > 0 && (ripe is null || carrying >= 8) && OutputPlace(mapCharacter) is not null)
        {
            RunWorkRound(mapCharacter, DeliverCarried(mapCharacter, work.ProduceIds.ToList()));
            return true;
        }
        if (ripe is not null)
        {
            var obj = DefinitionOf(ripe)!;
            _fieldClaims[ripe] = me;
            var steps = new List<Func<Func<double, bool?>?>>();
            string kind = obj.Plant.HarvestToolKind;
            if (kind.Trim().Length > 0) { steps.Add(GetTool(mapCharacter, kind)); steps.Add(ReadyTool(mapCharacter, kind)); }
            steps.Add(GoTo(me, ripe));
            ObjectInstance? produce = null;
            steps.Add(HarvestAt(mapCharacter, ripe, obj, p => produce = p));
            steps.Add(() =>
            {
                if (produce is null || !_map.Objects.Contains(produce)) return _ => true;
                work.ProduceIds.Add(produce.ObjectId);
                var take = produce;
                return Chain(GoTo(me, take), PoseOnce(me, "Picking"), Do(() => PickUp(me, take) || true));
            });
            RunWorkRound(mapCharacter, steps.ToArray());
            return true;
        }
        return StartSowing(mapCharacter, chests);
    }

    /// <summary>At the plant, its tool in hand: a few strokes, then harvested as the player harvests it.</summary>
    private Func<Func<double, bool?>?> HarvestAt(MapCharacter mapCharacter, ObjectInstance plant, WorldObject obj, Action<ObjectInstance?> gave) => () =>
    {
        var me = mapCharacter.Character;
        if (!_map.Objects.Contains(plant) || DefinitionOf(plant) is not { Type: ObjectType.Plant } || !IsRipe(plant, obj)) return null;
        FaceCell(me, plant.X, plant.Y);
        string pose = WorkPoseOf(mapCharacter);
        double left = 2400, beat = 0;
        return elapsed =>
        {
            if (!_map.Objects.Contains(plant)) return false;
            left -= elapsed;
            if ((beat -= elapsed) <= 0 && left > 0) { beat = 800; ShowPose(me, pose, restart: true); }
            if (left > 0) return null;
            ShowPose(me, "Idle");
            gave(HarvestPlant(plant, obj, mapCharacter));
            return true;
        };
    };

    /// <summary>A plant harvested (by the player or a worker): its sound, back to an earlier stage, taken whole, or back to the soil it was sown in; what it gives appears where it stood.</summary>
    private ObjectInstance? HarvestPlant(ObjectInstance plant, WorldObject obj, MapCharacter? by)
    {
        var p = obj.Plant;
        int owner = OwnerOf(plant);
        if (by is null || HeardFrom(by.Character)) PlaySound(p.HarvestSoundResourceId);
        var (x, y) = (plant.X, plant.Y);
        int last = Math.Max(0, p.Stages.Count - 1);
        if (p.RemovedWhenHarvested && p.SoilObjectId >= 0 && ObjectDef(p.SoilObjectId) is { } soil) BecomeObject(plant, soil); // the soil it was sown in, ready to sow again
        else if (p.RemovedWhenHarvested) RemoveMapObject(plant);
        else
        {
            plant.FrameRuntime = Math.Clamp(p.StageAfterHarvest, 0, last);
            plant.GrowMinutesRuntime = 0;
            RefreshObjectLook(plant);
        }
        var produce = SpawnObjectAt(p.ProduceObjectId, Math.Max(1, p.ProduceQuantity), x, y);
        if (produce is not null && by is not null && owner == by.Character.Id) produce.BelongsToId = owner; // from its own field: its own
        if (_map.Objects.Contains(plant) && plant.ObjectId == obj.Id) FireObjectEvent(plant, obj, BookTriggerConditionBased.ByAfterReadItem, by); // By Harvested
        return produce;
    }

    /// <summary>A placed object becomes another where it is, starting as that one starts (a plant at its first stage).</summary>
    private void BecomeObject(ObjectInstance placed, WorldObject becomes)
    {
        placed.ObjectId = becomes.Id;
        placed.DollRuntime = null;
        placed.FrameRuntime = becomes.Type == ObjectType.Plant ? 0 : placed.FrameRuntime;
        placed.GrowMinutesRuntime = 0;
        placed.IsSpentRuntime = false;
        var start = ObjectLooks.StartState(becomes, placed);
        placed.IsOpenRuntime = start.Open;
        placed.IsLockedRuntime = start.Locked;
        placed.IsLitRuntime = start.Lit;
        placed.LiquidIdRuntime = becomes.Type == ObjectType.LiquidContainer ? ObjectLooks.StartLiquid(becomes, placed) : null;
        placed.HitPointsRuntime = becomes.HitPoints > 0 ? becomes.HitPoints : -1;
        RefreshObjectLook(placed);
        SettleWorld();
    }

    // ---- Sowing ----

    /// <summary>The plant whose seed this is, sown in this soil (Plant: Sown from / in).</summary>
    private WorldObject? PlantSownWith(int seedId, int soilId) =>
        _objects.Objects.FirstOrDefault(o => o.Type == ObjectType.Plant && o.Plant.SeedObjectId == seedId && o.Plant.SoilObjectId == soilId && seedId >= 0);

    /// <summary>Soil sown: it becomes the plant, at its first stage.</summary>
    private void Sow(ObjectInstance soil, WorldObject plant, CharacterDefinition? by)
    {
        int owner = soil.BelongsToId;
        BecomeObject(soil, plant);
        soil.BelongsToId = owner; // the same field, the same owner
        if (by is not null && (by == _player || HeardFrom(by))) PlaySound(plant.Plant.HarvestSoundResourceId);
    }

    /// <summary>Empty soil in its area that it has seeds for: fetch a seed, walk there, sow.</summary>
    private bool StartSowing(MapCharacter mapCharacter, List<(PlacedItemInstance Chest, ContainerData Data)> chests)
    {
        var me = mapCharacter.Character;
        var sowable = _objects.Objects.Where(o => o.Type == ObjectType.Plant && o.Plant.SeedObjectId >= 0 && o.Plant.SoilObjectId >= 0).ToList();
        if (sowable.Count == 0) return false;
        foreach (var soil in _map.Objects.Where(o => o.ContainerInstanceId < 0 && InWorkArea(mapCharacter, o.X, o.Y, o.Z) && MineOrNobodys(OwnerOf(o), me) && !_fieldClaims.ContainsKey(o))
                     .OrderBy(o => Distance(mapCharacter, o.X, o.Y)))
        {
            var plant = sowable.FirstOrDefault(p => p.Plant.SoilObjectId == soil.ObjectId && AvailableFor(mapCharacter, p.Plant.SeedObjectId, chests) > 0);
            if (plant is null) continue;
            _fieldClaims[soil] = me;
            int seed = plant.Plant.SeedObjectId;
            string pose = WorkPoseOf(mapCharacter);
            RunWorkRound(mapCharacter, Fetch(mapCharacter, seed, 1), GoTo(me, soil), () =>
            {
                if (!_map.Objects.Contains(soil) || soil.ObjectId != plant.Plant.SoilObjectId || CountCarried(me, seed) < 1) return null;
                FaceCell(me, soil.X, soil.Y);
                ShowPose(me, "Picking", restart: true);
                var wait = WaitFor(1500);
                return elapsed =>
                {
                    if (wait(elapsed) is not true) return null;
                    if (!_map.Objects.Contains(soil) || soil.ObjectId != plant.Plant.SoilObjectId || TakeFromCarrier(me, ReferencedItemCategory.Object, seed, 1) is null) return false;
                    Sow(soil, plant, me);
                    ShowPose(me, "Idle");
                    RefreshAfterMove();
                    return true;
                };
            });
            return true;
        }
        return false;
    }

    // ================= Lab: sitting and reading between brews =================

    /// <summary>Exult's Lab_schedule: between brews the mage sits down in a chair of its lab and reads a while.</summary>
    private bool TrySitAndRead(MapCharacter mapCharacter)
    {
        var me = mapCharacter.Character;
        var chair = _map.Objects.Where(o => o.ContainerInstanceId < 0 && DefinitionOf(o) is { Type: ObjectType.Chair } && InWorkArea(mapCharacter, o.X, o.Y, o.Z) && MineOrNobodys(OwnerOf(o), me))
            .OrderBy(o => OwnerOf(o) == me.Id ? 0 : 1).ThenBy(o => Distance(mapCharacter, o.X, o.Y)).FirstOrDefault();
        if (chair is null || DefinitionOf(chair) is not { } obj) return false;
        var work = WorkOf(me);
        work.RoundRunning = true;
        int attempts = 0;
        double wait = 0, reading = 20_000 + Rng.Next(20_000);
        BeginScriptAction(me, ms =>
        {
            if (mapCharacter.Npc.InCombat) return false;
            if (_seats.TryGetValue(me, out var seat) && ReferenceEquals(seat.Furniture, chair))
            {
                if ((reading -= ms) > 0) return null;
                StandUp(me);
                return true;
            }
            if (mapCharacter.Npc.Path is not null) return null; // on the way
            if ((wait -= ms) > 0) return null;
            if (++attempts > 5) return false;
            if (!HeadForChair(mapCharacter, chair, obj)) wait = 700;
            return null;
        }, ok =>
        {
            work.RoundRunning = false;
            if (_seats.ContainsKey(me)) StandUp(me);
            mapCharacter.Npc.WaitMs = 800;
        }, afterEvent: true);
        if (me.IsHuman && Rng.Next(3) == 0) Say(mapCharacter, "Now, where was I...");
        return true;
    }

    // ================= When the work is over =================

    private bool NeedsTidying(CharacterDefinition who) => _work.TryGetValue(who, out var w) && (w.Borrowed.Count > 0 || w.SetDown.Count > 0);

    /// <summary>
    /// Exult's Tool_schedule::ending, real: before going on to its next
    /// activity, the worker picks up what it put down and puts back each tool
    /// it took - into the chest, or onto the spot, it came from.
    /// </summary>
    private void TidyUpAfterWork(MapCharacter mapCharacter)
    {
        var me = mapCharacter.Character;
        var work = WorkOf(me);
        var steps = new List<Func<Func<double, bool?>?>>();
        foreach (var thing in work.SetDown.ToList())
            steps.Add(() => ThingIsThere(thing) ? Chain(GoTo(me, thing), PoseOnce(me, "Picking"), Do(() => PickUp(me, thing) || true)) : _ => true);
        foreach (var tool in work.Borrowed.ToList())
            steps.Add(() =>
            {
                if (FindCarriedWhere(me, IsObject(tool.ObjectId)) is null) return _ => true; // not carried any more
                if (tool.Container is { } chest && _map.Items.Contains(chest) && ContainerDef(chest.ItemId) is { } data)
                    return Chain(GoTo(me, chest), PoseOnce(me, "Picking"), Do(() =>
                    {
                        if (FindCarriedWhere(me, IsObject(tool.ObjectId)) is not { } found || OriginOfCarried(me, found.Item, found.Holder) is not { } origin) return true;
                        _quietRefusals = true;
                        try { if (!TryPutIntoContainer(origin, data, chest, null)) PutBack(me, tool.ObjectId, (chest.X, chest.Y)); }
                        finally { _quietRefusals = false; }
                        return true;
                    }));
                var cell = tool.Cell ?? CellOf(me);
                return Chain(() => WalkTo(me, cell, beside: true) is { } walk ? e => { var r = walk(e); if (r == true) FaceCell(me, cell.X, cell.Y); return r; } : null,
                    PoseOnce(me, "Picking"), Do(() => { PutBack(me, tool.ObjectId, cell); return true; }));
            });
        work.SetDown.Clear();
        work.Borrowed.Clear();
        work.ProduceIds.Clear();
        work.RoundRunning = true;
        BeginScriptAction(me, Chain(steps.ToArray()), _ =>
        {
            work.RoundRunning = false;
            mapCharacter.Npc.WaitMs = 300;
        }, afterEvent: true);
    }

    private void PutBack(CharacterDefinition me, int objectId, (int X, int Y) cell)
    {
        if (TakeFromCarrier(me, ReferencedItemCategory.Object, objectId, 1) is { } items)
            foreach (var item in items) PutDownAt(item, me, cell);
        RefreshAfterMove();
    }
}
