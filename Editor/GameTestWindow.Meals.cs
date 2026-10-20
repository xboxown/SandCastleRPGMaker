using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Characters feeding themselves, inns and waiters, and house doors - all
/// for characters that aren't in the party (the party is fed by you, as in
/// Exult):
///  - Hungry (food at or below its Hungry level, Character Editor > Health):
///    it stops what it's doing and, in this order (each if ticked), eats
///    food on the map that belongs to it (walking to it), eats food it
///    carries, or goes to its inn, sits down and waits to be served - until
///    it's full (food 31), then gets up and goes back to its schedule.
///  - At the inn (Exult's Eat_at_inn_schedule): sitting, it eats a dish in
///    front of it (within 2 tiles) bit by bit, saying munch lines; with
///    nothing there it asks for food now and then (5-17 seconds). Its dishes
///    are its own: take one and that's theft - and it says so.
///  - Waiter (Exult's Waiter_schedule): finds a customer sitting and waiting
///    (within 32 tiles), walks over and asks the order, goes back to its
///    counter (its schedule spot) to prepare it, then brings a dish from its
///    menu and puts it on the table in front of them (the drop sound), with a
///    word two times in three.
///  - House doors (Character Editor > Verbose > Locks its house door): with
///    the key to a door that belongs to it, carried, a human unlocks the door
///    to get through, locks it behind itself when it's left the house (out
///    from under the roof), and locks it again when it goes to sleep.
/// </summary>
public partial class GameTestWindow
{
    private enum MealStep { None, GoingToOwnFood, EatingCarried, GoingToInn, AtInn }
    private enum WaiterStep { FindCustomer, GoToCustomer, GoToCounter, Prepare, Bring }

    private const int FoodFullLevel = SurvivalSettings.MaxFood; // eats until full

    /// <summary>"Next to" in Exult tiles: a map cell and a bit (Exult's 2 tiles, for cells bigger than its 8-pixel tiles).</summary>
    private int NearTiles => Math.Max(2, (int)Math.Ceiling(_tileWidth / (double)SpriteSizing.BaseUnit) + 1);
    /// <summary>Customers whose order a waiter has taken and not yet brought.</summary>
    private readonly HashSet<CharacterDefinition> _ordersTaken = new(ReferenceEqualityComparer.Instance);

    // ================= Getting hungry =================

    /// <summary>Every frame for each character: hungry and free, it sets out to eat (checked every 2 seconds).</summary>
    private void CheckHunger(MapCharacter mapCharacter, double elapsedMs)
    {
        var npc = mapCharacter.Npc;
        if (npc.Rule is not null) return; // following a When... rule (a "when hungry" one feeds it its own way)
        if (npc.Meal != MealStep.None || (npc.MealCheckMs -= elapsedMs) > 0) return;
        npc.MealCheckMs = 2000;
        var me = mapCharacter.Character;
        var s = me.Survival;
        if (IsPartyOrPlayer(me) || !s.CanGetHungry || !(s.EatsOwnFood || s.EatsCarriedFood || s.GoesToInn)) return;
        if (npc.InCombat || npc.Fleeing || npc.Hounding || npc.Activity is ScheduleType.Sleep or ScheduleType.EatAtInn || _seats.TryGetValue(me, out var seat) && seat.Bed) return;
        if (VitalsOf(me).Food > s.HungryAtOrBelow) return;
        if (!PlanMeal(mapCharacter)) npc.MealCheckMs = 30000; // nothing to eat anywhere: try again later
    }

    /// <summary>Its next way to eat, in order: its own food, food it carries, the inn. False if none.</summary>
    private bool PlanMeal(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        var me = mapCharacter.Character;
        var s = me.Survival;
        if (s.EatsOwnFood && NearestOwnFood(mapCharacter) is { } food && FindPath(mapCharacter, (food.X, food.Y), allowPartial: true) is { Count: > 0 } path)
        {
            BeginMeal(mapCharacter, MealStep.GoingToOwnFood);
            npc.MealFood = food;
            StartPath(mapCharacter, path);
            return true;
        }
        if (s.EatsOwnFood && NearestOwnFood(mapCharacter) is { } here && TileDistance(mapCharacter, (here.X, here.Y)) <= NearTiles)
        {
            BeginMeal(mapCharacter, MealStep.GoingToOwnFood);
            npc.MealFood = here;
            npc.WaitMs = 0;
            return true;
        }
        if (s.EatsCarriedFood && CarriedFood(me) is not null)
        {
            BeginMeal(mapCharacter, MealStep.EatingCarried);
            npc.WaitMs = 800;
            return true;
        }
        if (s.GoesToInn && InnSpotOf(mapCharacter) is not null) // its cell, or the nearest zone of its inn purpose
        {
            BeginMeal(mapCharacter, MealStep.GoingToInn);
            npc.WaitMs = 0;
            return true;
        }
        return false;
    }

    private void BeginMeal(MapCharacter mapCharacter, MealStep step)
    {
        var npc = mapCharacter.Npc;
        if (npc.Meal == MealStep.None)
        {
            StandUp(mapCharacter.Character); // out of the chair to go and eat
            StopWalking(mapCharacter);
            npc.WalkingToSchedule = false;
        }
        npc.Meal = step;
        npc.MealFood = null;
    }

    /// <summary>Full (or nothing more to eat): up, and back to its schedule.</summary>
    private void FinishMeal(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        npc.Meal = MealStep.None;
        npc.MealFood = null;
        npc.MealCheckMs = 60000;
        StandUp(mapCharacter.Character);
        npc.Entry = null;
        npc.Activity = null;
        SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
        UpdateSchedule(mapCharacter, _clock.Period, 0); // back to what it should be doing now
    }

    // ================= Eating =================

    private void MealNowWhat(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        var me = mapCharacter.Character;
        if (VitalsOf(me).Food >= FoodFullLevel) { FinishMeal(mapCharacter); return; }
        switch (npc.Meal)
        {
            case MealStep.GoingToOwnFood:
            {
                var food = npc.MealFood;
                if (food is null || !_map.Objects.Contains(food) || OwnerOf(food) != me.Id)
                {
                    if (food is not null && !_map.Objects.Contains(food) && me.IsHuman) Bark(me, VerboseKind.FoodStolen); // it's gone: taken
                    if (!PlanMeal(mapCharacter)) FinishMeal(mapCharacter);
                    return;
                }
                if (TileDistance(mapCharacter, (food.X, food.Y)) > NearTiles)
                {
                    if (FindPath(mapCharacter, (food.X, food.Y), allowPartial: true) is { Count: > 0 } path) StartPath(mapCharacter, path);
                    else if (!PlanMeal(mapCharacter)) FinishMeal(mapCharacter);
                    return;
                }
                EatPlaced(mapCharacter, food);
                npc.MealFood = null;
                npc.WaitMs = 1500 + Rng.Next(1500);
                npc.Meal = MealStep.GoingToOwnFood; // the next of its own, if any - PlanMeal picks again
                if (VitalsOf(me).Food < FoodFullLevel && !PlanMeal(mapCharacter)) FinishMeal(mapCharacter);
                return;
            }
            case MealStep.EatingCarried:
            {
                if (CarriedFood(me) is not { } carried)
                {
                    if (me.Survival.GoesToInn && InnSpotOf(mapCharacter) is not null) { npc.Meal = MealStep.GoingToInn; npc.WaitMs = 0; }
                    else FinishMeal(mapCharacter);
                    return;
                }
                var (item, def) = carried;
                TakeFrom(CarriedItems(me), item.ItemId, 1);
                Nourish(mapCharacter, def);
                npc.WaitMs = 2000 + Rng.Next(1500);
                return;
            }
            case MealStep.GoingToInn:
            {
                if (InnSpotOf(mapCharacter) is not { } inn) { FinishMeal(mapCharacter); return; } // its inn zone is gone
                if (TileDistance(mapCharacter, inn) > NearTiles + 1)
                {
                    if (FindPath(mapCharacter, inn, allowPartial: true) is { Count: > 0 } path) StartPath(mapCharacter, path);
                    else npc.WaitMs = 3000; // someone in the way: shortly
                    return;
                }
                npc.Meal = MealStep.AtInn;
                if (!_seats.ContainsKey(me) && !GoToFurniture(mapCharacter, bed: false)) npc.WaitMs = 1000; // no chair: it waits standing
                return;
            }
            case MealStep.AtInn:
                InnCustomerNowWhat(mapCharacter);
                return;
        }
    }

    /// <summary>Eat_at_inn_schedule::now_what: sit; a dish of its own in front of it gets eaten (with a munch), otherwise it asks for food.</summary>
    private void InnCustomerNowWhat(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        var me = mapCharacter.Character;
        if (!_seats.ContainsKey(me) && _headingToSeat.ContainsKey(me)) { npc.WaitMs = 1000; return; } // still walking to its chair
        if (!_seats.ContainsKey(me) && !GoToFurniture(mapCharacter, bed: false)) { } // no free chair: eats standing
        // Its dishes that are gone, and not by its own mouth: someone took them.
        foreach (var gone in npc.ServedDishes.Where(d => !_map.Objects.Contains(d)).ToList())
        {
            npc.ServedDishes.Remove(gone);
            if (me.IsHuman) Bark(me, VerboseKind.FoodStolen);
        }
        var dish = DishesFor(mapCharacter).FirstOrDefault();
        if (dish is not null)
        {
            npc.ServedDishes.Remove(dish);
            EatPlaced(mapCharacter, dish);
            if (npc.Meal == MealStep.AtInn && VitalsOf(me).Food >= FoodFullLevel) { FinishMeal(mapCharacter); return; }
            npc.WaitMs = 3000 + Rng.Next(3000);
            return;
        }
        if (me.IsHuman && Rng.Next(4) > 0) Bark(me, VerboseKind.WantsFood);
        npc.WaitMs = 5000 + Rng.Next(12000); // Exult: "Wake up in a little while" - 5 to 17 s
    }

    /// <summary>Its own dishes in front of it (within 2 tiles - Exult's find_nearby 2).</summary>
    private IEnumerable<ObjectInstance> DishesFor(MapCharacter mapCharacter) =>
        _map.Objects.Where(o => o.ContainerInstanceId < 0 && o.OwnerIdRuntime == mapCharacter.Character.Id
            && DefinitionOf(o)?.Type == ObjectType.Food && TileDistance(mapCharacter, (o.X, o.Y)) <= NearTiles + 1);

    /// <summary>One of a placed food eaten: gone from the map (one of a stack), the character fed.</summary>
    private void EatPlaced(MapCharacter mapCharacter, ObjectInstance food)
    {
        if (DefinitionOf(food) is not { } def) return;
        if (food.Quantity > 1) { food.Quantity--; RefreshObjectLook(food); }
        else { RemoveMapObject(food); SettleWorld(); RefreshAfterMove(); }
        Nourish(mapCharacter, def);
    }

    /// <summary>The food's nutrition and effects, its eat sound, and (three times in four) a munch.</summary>
    private void Nourish(MapCharacter mapCharacter, WorldObject food)
    {
        var me = mapCharacter.Character;
        var vitals = VitalsOf(me);
        vitals.Food = Math.Clamp(vitals.Food + food.Food.Nutrition, 0, SurvivalSettings.MaxFood);
        PlaySound(food.Food.EatSoundResourceId);
        if (me.IsHuman && Rng.Next(4) > 0) Bark(me, VerboseKind.Munching);
        ApplyEffects(food.Food.Effects, me, $"\"{food.Name}\"");
    }

    /// <summary>Who a placed object belongs to now: its own (a served dish's customer), else its Object Editor Belong To.</summary>
    private int OwnerOf(ObjectInstance instance) =>
        instance.OwnerIdRuntime >= 0 ? instance.OwnerIdRuntime : instance.BelongsToId >= 0 ? instance.BelongsToId : DefinitionOf(instance)?.BelongToId ?? -1;

    /// <summary>The nearest solid food on the map that belongs to it (within 64 tiles).</summary>
    private ObjectInstance? NearestOwnFood(MapCharacter mapCharacter)
    {
        int range = CellsForTiles(64);
        return _map.Objects
            .Where(o => o.ContainerInstanceId < 0 && DefinitionOf(o) is { Type: ObjectType.Food, Food.IsLiquid: false } && OwnerOf(o) == mapCharacter.Character.Id)
            .Where(o => Math.Max(Math.Abs(o.X - mapCharacter.Placement.X), Math.Abs(o.Y - mapCharacter.Placement.Y)) <= range)
            .OrderBy(o => Math.Abs(o.X - mapCharacter.Placement.X) + Math.Abs(o.Y - mapCharacter.Placement.Y))
            .FirstOrDefault();
    }

    /// <summary>A solid food it carries (anywhere in its inventory), with its Object Editor entry.</summary>
    private (InventoryItemInstance Item, WorldObject Food)? CarriedFood(CharacterDefinition who)
    {
        foreach (var (item, _, _) in CarriedItems(who))
            if (DefinitionOf(item) is { Type: ObjectType.Food, Food.IsLiquid: false } food) return (item, food);
        return null;
    }

    // ================= The waiter =================

    private void WaiterNowWhat(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        var me = mapCharacter.Character;
        var customer = npc.WaiterCustomer;
        bool customerOk = customer is not null && !IsDead(customer.Character) && IsWaitingCustomer(customer, forWaiter: me);
        switch (npc.Waiter)
        {
            case WaiterStep.FindCustomer:
            {
                var next = _mapCharacters
                    .Where(c => c != mapCharacter && IsWaitingCustomer(c, forWaiter: null) && !_ordersTaken.Contains(c.Character)
                        && TileDistance(mapCharacter, (c.Placement.X, c.Placement.Y)) <= 32)
                    .OrderBy(c => TileDistance(mapCharacter, (c.Placement.X, c.Placement.Y)))
                    .FirstOrDefault();
                if (next is null)
                {
                    // No one to serve: back to the counter, and wait there (Exult wait_at_counter: 2-4 s).
                    if (TileDistance(mapCharacter, npc.Center) > 3 && FindPath(mapCharacter, npc.Center, allowPartial: true) is { Count: > 0 } home) { StartPath(mapCharacter, home); return; }
                    npc.WaitMs = 2000 + Rng.Next(2000);
                    return;
                }
                npc.WaiterCustomer = next;
                npc.Waiter = WaiterStep.GoToCustomer;
                if (!WalkTowards(mapCharacter, (next.Placement.X, next.Placement.Y))) npc.WaitMs = 0;
                return;
            }
            case WaiterStep.GoToCustomer:
                if (!customerOk) { npc.Waiter = WaiterStep.FindCustomer; npc.WaitMs = 500; return; }
                if (WalkTowards(mapCharacter, (customer!.Placement.X, customer.Placement.Y))) return;
                FaceTowards(mapCharacter, customer);
                if (me.IsHuman) Bark(me, VerboseKind.WaiterAsks);
                _ordersTaken.Add(customer.Character);
                npc.Waiter = WaiterStep.GoToCounter;
                npc.WaitMs = 1200;
                return;
            case WaiterStep.GoToCounter:
                if (WalkTowards(mapCharacter, npc.Center)) return;
                npc.Waiter = WaiterStep.Prepare;
                SetCharacterPose(mapCharacter, "Attacking Melee", mapCharacter.Facing); // working at the counter (Exult's Prep_animation)
                npc.WaitMs = 1500 + Rng.Next(1500);
                return;
            case WaiterStep.Prepare:
                SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
                if (!customerOk || me.WaiterServesFoodIds.Count == 0)
                {
                    if (customer is not null) _ordersTaken.Remove(customer.Character);
                    npc.Waiter = WaiterStep.FindCustomer;
                    npc.WaitMs = 1000;
                    return;
                }
                npc.WaiterDishId = me.WaiterServesFoodIds[Rng.Next(me.WaiterServesFoodIds.Count)];
                npc.Waiter = WaiterStep.Bring;
                if (!WalkTowards(mapCharacter, (customer!.Placement.X, customer.Placement.Y))) npc.WaitMs = 0;
                return;
            case WaiterStep.Bring:
                if (!customerOk) { if (customer is not null) _ordersTaken.Remove(customer.Character); npc.Waiter = WaiterStep.FindCustomer; npc.WaitMs = 500; return; }
                if (WalkTowards(mapCharacter, (customer!.Placement.X, customer.Placement.Y))) return;
                FaceTowards(mapCharacter, customer);
                var dish = ServeDish(npc.WaiterDishId, customer);
                if (dish is not null)
                {
                    customer.Npc.ServedDishes.Add(dish);
                    PlayDropSound(); // Exult: the drop sound
                    if (me.IsHuman && Rng.Next(3) > 0) Bark(me, VerboseKind.WaiterServes);
                }
                _ordersTaken.Remove(customer.Character);
                npc.WaiterCustomer = null;
                npc.Waiter = WaiterStep.FindCustomer;
                npc.WaitMs = 1000 + Rng.Next(1000);
                return;
        }
    }

    /// <summary>Sitting (or standing) at the inn and waiting: hungry ones there, and those on the Eat at Inn schedule - with no dish of their own in front of them.</summary>
    private bool IsWaitingCustomer(MapCharacter customer, CharacterDefinition? forWaiter)
    {
        if (IsDead(customer.Character) || IsIncapacitated(customer.Character)) return false;
        var npc = customer.Npc;
        bool atInn = npc.Meal == MealStep.AtInn || (npc.Meal == MealStep.None && npc.Activity == ScheduleType.EatAtInn && !npc.WalkingToSchedule);
        if (!atInn || _headingToSeat.ContainsKey(customer.Character)) return false;
        return !DishesFor(customer).Any();
    }

    /// <summary>Heads next to the cell (within 2 tiles). True if it set off; false if it's there already (or can't get nearer).</summary>
    private bool WalkTowards(MapCharacter mapCharacter, (int X, int Y) cell)
    {
        if (TileDistance(mapCharacter, cell) <= NearTiles) return false;
        if (FindPath(mapCharacter, cell, allowPartial: true) is not { Count: > 0 } path) return false;
        StartPath(mapCharacter, path);
        return true;
    }

    private void FaceTowards(MapCharacter mapCharacter, MapCharacter other) =>
        SetCharacterPose(mapCharacter, "Idle", FacingTowards(mapCharacter.PixelX, mapCharacter.PixelY, other.PixelX, other.PixelY));

    /// <summary>
    /// Waiter_schedule serve_food: the dish is put down in front of the
    /// customer - on a table next to them if there is one (find_serving_spot),
    /// else on the floor beside them. It's theirs.
    /// </summary>
    private ObjectInstance? ServeDish(int foodId, MapCharacter customer)
    {
        if (ObjectDef(foodId) is null) return null;
        var (cx, cy) = (customer.Placement.X, customer.Placement.Y);
        var table = _map.Objects
            .Where(o => o.ContainerInstanceId < 0 && DefinitionOf(o)?.Type == ObjectType.Table && Math.Max(Math.Abs(o.X - cx), Math.Abs(o.Y - cy)) <= 2)
            .OrderBy(o => Math.Abs(o.X - cx) + Math.Abs(o.Y - cy))
            .FirstOrDefault();
        (int X, int Y) spot = table is not null ? (table.X, table.Y) : InFrontOf(customer);
        var dish = new ObjectInstance
        {
            ObjectId = foodId, Quantity = 1, X = spot.X, Y = spot.Y, ContainerInstanceId = -1,
            InstanceId = _map.GetNextInstanceId(), OwnerIdRuntime = customer.Character.Id
        };
        AddMapEntity(null, dish);
        SettleWorld();
        RefreshAfterMove();
        return dish;
    }

    private (int X, int Y) InFrontOf(MapCharacter who)
    {
        var (dx, dy) = who.Facing switch
        {
            SpriteDirection.North => (0, -1), SpriteDirection.South => (0, 1), SpriteDirection.East => (1, 0), SpriteDirection.West => (-1, 0),
            SpriteDirection.NorthEast => (1, -1), SpriteDirection.NorthWest => (-1, -1), SpriteDirection.SouthEast => (1, 1), _ => (-1, 1)
        };
        int x = Math.Clamp(who.Placement.X + dx, 0, _map.Width - 1), y = Math.Clamp(who.Placement.Y + dy, 0, _map.Height - 1);
        return (x, y);
    }

    // ================= House doors =================

    /// <summary>Whether this character locks this door: it's set to (and human), the door's key belongs to it, and it carries the key.</summary>
    private bool HoldsHouseKey(MapCharacter mapCharacter, WorldObject door)
    {
        var me = mapCharacter.Character;
        if (!me.LocksHouseDoor || !me.IsHuman || door.Door.RequiredKeyItemId < 0 || door.Door.MagicallyLocked) return false;
        if (ObjectDef(door.Door.RequiredKeyItemId) is not { } key || key.BelongToId != me.Id) return false;
        return CarriedItems(me).Any(i => i.Item.Category == ReferencedItemCategory.Object && i.Item.ItemId == key.Id);
    }

    /// <summary>A walking character at its own locked door, key in hand: unlocks it and opens it. True if it did.</summary>
    private bool NpcUnlocksOwnDoor(MapCharacter mapCharacter, UnitRect nextFoot)
    {
        foreach (var door in _map.Objects)
        {
            if (DefinitionOf(door) is not { Type: ObjectType.Door } obj || IsDoorOpen(door) || !IsDoorLocked(door) || !BodyOf(null, door).Overlaps(nextFoot)) continue;
            if (!HoldsHouseKey(mapCharacter, obj)) return false;
            door.IsLockedRuntime = false;
            PlaySound(obj.Door.UnlockSoundResourceId >= 0 ? obj.Door.UnlockSoundResourceId : obj.Door.LockSoundResourceId);
            RefreshObjectLook(door);
            OpenDoor(door, obj, mapCharacter);
            _doorsToClose[mapCharacter] = door;
            return true;
        }
        return false;
    }

    /// <summary>Just closed behind it: its own door, and it's now outside (not under a roof) - it locks it.</summary>
    private void LockBehindIfLeaving(MapCharacter mapCharacter, ObjectInstance door, WorldObject obj)
    {
        if (!HoldsHouseKey(mapCharacter, obj) || IsDoorLocked(door) || IsUnderRoof(CharacterFootprint(mapCharacter), mapCharacter.Lift)) return;
        LockDoor(door, obj);
    }

    /// <summary>Going to sleep: its own doors near it (within 24 tiles) closed if it can, and locked.</summary>
    private void LockHouseDoorsForNight(MapCharacter mapCharacter)
    {
        int range = CellsForTiles(24);
        foreach (var door in _map.Objects.ToList())
        {
            if (DefinitionOf(door) is not { Type: ObjectType.Door } obj || !HoldsHouseKey(mapCharacter, obj)) continue;
            if (Math.Max(Math.Abs(door.X - mapCharacter.Placement.X), Math.Abs(door.Y - mapCharacter.Placement.Y)) > range) continue;
            if (IsDoorOpen(door) && !TryCloseDoor(door, obj, mapCharacter)) continue; // someone in the doorway
            if (!IsDoorLocked(door)) LockDoor(door, obj);
        }
    }

    private void LockDoor(ObjectInstance door, WorldObject obj)
    {
        door.IsLockedRuntime = true;
        PlaySound(obj.Door.LockSoundResourceId >= 0 ? obj.Door.LockSoundResourceId : obj.Door.UnlockSoundResourceId);
        RefreshObjectLook(door);
    }

    /// <summary>Whether anything solid is over this footprint (Exult's is_roof) - inside a house.</summary>
    private bool IsUnderRoof(UnitRect foot, int lift) => CeilingOver(foot, lift) is not null;
}
