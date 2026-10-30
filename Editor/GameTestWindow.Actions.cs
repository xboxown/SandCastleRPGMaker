using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// CommandList part 3: the commands a character carries out over time
/// (Exult's set_action_sequence / Actor_action chains, driven by usecode):
/// walk somewhere, then do something there. While one runs the character's
/// own schedule waits (as Exult's usecode-driven actors do); when it's over
/// (done or cancelled) the event's next command runs and the schedule picks
/// up again. Every step goes through the same rules the rest of the game
/// uses: the walking rules and path finding, Actor::add for picking things
/// up, the door and lockpick rules, the furniture seats.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>One command being carried out: its frame-by-frame step (null = still going, true = done, false = cancelled) and who waits for it.</summary>
    private sealed class ScriptAction
    {
        public required CharacterDefinition Who;
        public required Func<double, bool?> Tick;
        /// <summary>The event waiting for it; null for a hold nobody waits on (sitting on after Sit).</summary>
        public Action<bool>? Done;
        /// <summary>After its event is over (sitting on after Sit): part of their routine - it doesn't stop a save.</summary>
        public bool AfterEvent;
        /// <summary>The event run that gave the command (null: not an event's - a schedule's own work).</summary>
        public object? Run;
    }

    /// <summary>
    /// Characters an event moved (walked, sat...) whose event is still going:
    /// they stay where it left them - no schedule, not sent back to it when
    /// off screen - till that event is over. They still fight back, and follow
    /// their leader. Change Behavior lets one go at once.
    /// </summary>
    private readonly Dictionary<CharacterDefinition, object> _heldByEvent = new(ReferenceEqualityComparer.Instance);
    /// <summary>Held characters whose hour came round while held: their schedule is looked at again when they're let go.</summary>
    private readonly HashSet<CharacterDefinition> _heldMissedHour = new(ReferenceEqualityComparer.Instance);

    private bool IsHeldByEvent(CharacterDefinition character) => _heldByEvent.ContainsKey(character);

    /// <summary>An event's run is over: everyone it was holding goes back to their schedule.</summary>
    private void ReleaseHeldBy(object run)
    {
        foreach (var who in _heldByEvent.Where(h => ReferenceEquals(h.Value, run)).Select(h => h.Key).ToList()) LetGo(who);
    }

    private void LetGo(CharacterDefinition who)
    {
        if (!_heldByEvent.Remove(who)) return;
        bool missedHour = _heldMissedHour.Remove(who);
        if (MapCharacterOf(who) is not { } mapCharacter || IsScripted(who)) return;
        if (missedHour) UpdateSchedule(mapCharacter, _clock.Period, 0);
        if (mapCharacter.Npc.Path is null) mapCharacter.Npc.WaitMs = StdDelay;
    }

    /// <summary>A Play Animation showing: its picture, how long is left, and who it follows (or null: a fixed spot).</summary>
    private sealed class ScriptEffect
    {
        public required Image Image;
        public required Func<double, bool> Over;
        public CharacterDefinition? Follows;
        public Action? Done;
    }

    private readonly Dictionary<CharacterDefinition, ScriptAction> _scriptActions = new(ReferenceEqualityComparer.Instance);
    private readonly List<ScriptEffect> _scriptEffects = new();
    /// <summary>Wait commands counting down: the event goes on when one is over.</summary>
    private readonly List<(Func<double, bool?> Tick, Action<bool> Done)> _scriptWaits = new();

    private void InitializeActionCommands()
    {
        var a = _execContext.GameActions;
        a[EventCommandType.WalkToLocation] = WalkToLocationCommand;
        a[EventCommandType.WalkToFloorLocation] = WalkToFloorLocationCommand;
        a[EventCommandType.PlayVideo] = PlayVideoCommand;
        a[EventCommandType.OpenPaperdoll] = OpenPaperdollCommand;
        a[EventCommandType.PickupItem] = PickupItemCommand;
        a[EventCommandType.DropItem] = DropItemCommand;
        a[EventCommandType.PlayPose] = PlayPoseCommand;
        a[EventCommandType.AttackObject] = AttackObjectCommand;
        a[EventCommandType.PlayAnimation] = PlayAnimationCommand;
        a[EventCommandType.Sit] = SitCommand;
        a[EventCommandType.OpenDoor] = OpenDoorCommand;
        a[EventCommandType.PicklockDoor] = PicklockDoorCommand;
        a[EventCommandType.TakeFromContainer] = TakeFromContainerCommand;
        a[EventCommandType.Wait] = WaitCommand;
        var h = _execContext.GameHandlers;
        h[EventCommandType.EquipItem] = EquipItemCommand;
        h[EventCommandType.UnequipItem] = UnequipItemCommand;
        h[EventCommandType.ChangePose] = ChangePoseCommand;
        h[EventCommandType.Stand] = c => CharacterDefinitionFor(c.Get("Who")) is { } who && !IsDead(who) && StandUpIfSeated(who);
    }

    // ================= Running them =================

    private bool IsScripted(CharacterDefinition character) => _scriptActions.ContainsKey(character);

    /// <summary>Starts a command for someone - whatever they were doing for an earlier command is cancelled.</summary>
    private void BeginScriptAction(CharacterDefinition who, Func<double, bool?> tick, Action<bool>? done, bool afterEvent = false)
    {
        if (_scriptActions.Remove(who, out var old)) old.Done?.Invoke(false);
        if (MapCharacterOf(who) is { } mapCharacter) StopWalking(mapCharacter);
        _scriptActions[who] = new ScriptAction { Who = who, Tick = tick, Done = done, AfterEvent = afterEvent, Run = afterEvent ? null : EventCommandInterpreter.CurrentRun };
    }

    /// <summary>Each frame (not while the game is paused): every command being carried out takes its step.</summary>
    private void UpdateScriptActions(double elapsedMs)
    {
        foreach (var action in _scriptActions.Values.ToList())
        {
            if (!_scriptActions.TryGetValue(action.Who, out var current) || !ReferenceEquals(current, action)) continue;
            bool? result = IsDead(action.Who) ? false : IsIncapacitated(action.Who) ? null : action.Tick(elapsedMs); // asleep or paralyzed: it waits
            if (result is not bool ok) continue;
            _scriptActions.Remove(action.Who);
            // Its event still going: it stays where the event left it till the event is over (then LetGo). Otherwise back to its schedule (Exult: the usecode's actions done, the schedule's now_what again).
            if (action.Run is { } run && action.Done is not null && action.Who != _player) _heldByEvent[action.Who] = run;
            else if (MapCharacterOf(action.Who) is { } mapCharacter && !IsScripted(action.Who) && mapCharacter.Npc.Path is null) mapCharacter.Npc.WaitMs = StdDelay;
            action.Done?.Invoke(ok);
        }
        foreach (var wait in _scriptWaits.ToList())
        {
            if (wait.Tick(elapsedMs) is not true) continue;
            _scriptWaits.Remove(wait);
            wait.Done(true);
        }
        foreach (var effect in _scriptEffects.ToList())
        {
            if (effect.Follows is { } who) PlaceEffect(effect.Image, PixelOf(who), LiftOf(who));
            if (!effect.Over(elapsedMs)) continue;
            _scriptEffects.Remove(effect);
            UnregisterWorldAnimator(effect.Image);
            GameCanvas.Children.Remove(effect.Image);
            effect.Done?.Invoke();
        }
    }

    // ---- Steps, chained ----

    /// <summary>Steps one after another: each is made when its turn comes (null = can't be done: cancelled).</summary>
    private static Func<double, bool?> Chain(params Func<Func<double, bool?>?>[] steps)
    {
        int index = -1;
        Func<double, bool?>? current = null;
        return elapsedMs =>
        {
            while (true)
            {
                if (current is null)
                {
                    if (++index >= steps.Length) return true;
                    current = steps[index]();
                    if (current is null) return false;
                }
                var result = current(elapsedMs);
                if (result is null) return null;
                if (result == false) return false;
                current = null;
                elapsedMs = 0; // the next step starts now
            }
        };
    }

    private static Func<Func<double, bool?>?> Do(Func<bool> act) => () => _ => act();
    private static Func<double, bool?> WaitFor(double ms) { double left = ms; return elapsed => (left -= elapsed) <= 0 ? true : null; }

    // ================= Walking there =================

    /// <summary>Walks someone to a cell (or right beside it): null if they aren't on this map.</summary>
    /// <param name="floor">The floor the cell is on (up or down the stairs to it); null: the floor they're on.</param>
    private Func<double, bool?>? WalkTo(CharacterDefinition who, (int X, int Y) goal, bool beside, int? floor = null)
    {
        if (!InsideMap(goal.X, goal.Y)) return null;
        if (who == _player) return PlayerWalkStep(goal, beside, floor);
        return MapCharacterOf(who) is { } mapCharacter ? NpcWalkStep(mapCharacter, goal, beside, floor) : null;
    }

    /// <summary>Lifts at least between this lift's floor and the goal floor (a floor is 5 lifts, each step climbs at most 1): 0 when no floor is asked for.</summary>
    private static int FloorSteps(int lift, int? floor) => floor is { } f ? Math.Abs(MapFloors.FloorOfLift(lift) - f) * MapFloors.LiftsPerFloor : 0;

    private bool Arrived((int X, int Y) here, (int X, int Y) goal, bool beside, Func<(int X, int Y), bool> canStand)
    {
        int dx = Math.Abs(CellsAcross(here.X - goal.X)), dy = Math.Abs(CellsUpDown(here.Y - goal.Y)); // the short way round a map that wraps around
        if (beside) return Math.Max(dx, dy) <= 1; // next to it (or on it, for a thing lying on the floor)
        return dx + dy == 0 || (dx + dy == 1 && !canStand(goal)); // someone or something on the spot: right beside it will do
    }

    /// <summary>
    /// Walk_to_schedule's way of getting somewhere far (schedule.cc): a leg at a
    /// time, as far towards it as can be planned, then on from there; a leg that
    /// gets no nearer counts as a try. Only after 5 such tries is there truly no
    /// way, and the command is cancelled (where the schedule would jump there).
    /// </summary>
    private Func<double, bool?> NpcWalkStep(MapCharacter mapCharacter, (int X, int Y) goal, bool beside, int? floor = null)
    {
        int retries = 0, best = int.MaxValue;
        double wait = 0;
        StandUp(mapCharacter.Character);
        bool CanStandThere((int X, int Y) cell) => floor is { } f
            ? CanStandAt(mapCharacter, cell, MapFloors.BaseLift(f), null, out int lift) && MapFloors.FloorOfLift(lift) == f
            : CanStandAt(mapCharacter, cell, mapCharacter.Lift, null, out _, anyLift: true);
        return elapsedMs =>
        {
            var npc = mapCharacter.Npc;
            if (npc.Path is not null) return null; // walking a leg (MoveAlongPath, from UpdateNpc)
            var here = (mapCharacter.Placement.X, mapCharacter.Placement.Y);
            if (FloorSteps(mapCharacter.Lift, floor) == 0 && Arrived(here, goal, beside, CanStandThere)) return true;
            if ((wait -= elapsedMs) > 0) return null;
            int distance = Heuristic(here, goal) + FloorSteps(mapCharacter.Lift, floor);
            if (distance < best) { best = distance; retries = 0; }
            else if (++retries >= 5) return false;
            var path = FindPath(mapCharacter, goal, allowPartial: true, besideOnly: beside, goalFloor: floor);
            if (path is not { Count: > 0 }) { wait = 500 + 500 * retries; return null; } // someone in the way: shortly again
            StartPath(mapCharacter, path);
            return null;
        };
    }

    /// <summary>The main character walking there on their own (as to a chair); pressing a direction key takes over - cancelled.</summary>
    private Func<double, bool?> PlayerWalkStep((int X, int Y) goal, bool beside, int? floor = null)
    {
        int tries = 0;
        double wait = 0;
        if (_player is not null) StandUpIfSeated(_player);
        bool CanStandThere((int X, int Y) cell) => floor is { } f
            ? TryStepActor(PlayerFootprintAt(CellCenterX(cell.X), CellCenterY(cell.Y)), MapFloors.BaseLift(f), _playerSize.Z, out int lift, out _, null, WorldNow()) && MapFloors.FloorOfLift(lift) == f
            : PlayerCanStandAt(cell);
        return elapsedMs =>
        {
            if (_heldDirectionKeys.Count > 0) { CancelPlayerWalk(); return false; }
            if (_playerWalkPath is not null) return null;
            if (FloorSteps(_playerZ, floor) == 0 && Arrived((_playerX, _playerY), goal, beside, CanStandThere)) return true;
            if ((wait -= elapsedMs) > 0) return null;
            if (++tries > 5) return false;
            if (!WalkPlayerTo(goal, () => { }, besideOnly: beside, goalFloor: floor)) wait = 700; // no way just now (someone in it): again shortly
            return null;
        };
    }

    private bool PlayerCanStandAt((int X, int Y) cell) =>
        TryStepActor(PlayerFootprintAt(CellCenterX(cell.X), CellCenterY(cell.Y)), _playerZ, _playerSize.Z, out _, out _, null, WorldNow());

    // ================= Poses =================

    private void ShowPose(CharacterDefinition who, string poseName, SpriteDirection? facing = null, bool restart = false)
    {
        if (who == _player)
        {
            if (facing is { } f) _playerFacing = f;
            ApplyPoseSprite(PoseByName(poseName) ?? _idlePose);
        }
        else if (MapCharacterOf(who) is { } mapCharacter)
        {
            if (restart) mapCharacter.ShownSprite = null; // from its first frame
            SetCharacterPose(mapCharacter, poseName, facing ?? mapCharacter.Facing);
        }
    }

    /// <summary>How long a pose's animation takes to play once, as they face now.</summary>
    private double PoseLengthMs(CharacterDefinition who, string poseName)
    {
        var pose = PoseByName(poseName);
        var sprite = who == _player ? ResolveFacingSprite(pose, out _) : ResolveCharacterSprite(who, pose, MapCharacterOf(who)?.Facing ?? SpriteDirection.South, out _, out _);
        return sprite is { Frames.Count: > 0 } ? sprite.Frames.Sum(f => Math.Max(1, f.DurationMs)) : 500;
    }

    private void FaceCell(CharacterDefinition who, int cellX, int cellY, string pose = "Idle")
    {
        var (x, y) = PixelOf(who);
        if (double.IsNaN(x)) return;
        ShowPose(who, pose, FacingTowards(x, y, CellCenterX(cellX), CellCenterY(cellY)));
    }

    /// <summary>A pose played once through (at least <paramref name="minMs"/>), then standing again.</summary>
    private Func<Func<double, bool?>?> PoseOnce(CharacterDefinition who, string poseName, double minMs = 300) => () =>
    {
        ShowPose(who, poseName, restart: true);
        var wait = WaitFor(Math.Max(minMs, PoseLengthMs(who, poseName)));
        return elapsed => wait(elapsed) is true ? StandIdle(who) : null;
    };

    private bool StandIdle(CharacterDefinition who)
    {
        if (!_seats.ContainsKey(who)) ShowPose(who, "Idle");
        return true;
    }

    /// <summary>A length of time: real seconds, game minutes or hours, or a number of milliseconds for "until done".</summary>
    private Func<double, bool?> Lasting(string unit, double amount, double doneMs)
    {
        long startMinute = TotalMinutes();
        double elapsed = 0;
        return ms =>
        {
            elapsed += ms;
            bool over = unit switch
            {
                "Seconds" => elapsed >= amount * 1000,
                "Minutes" => TotalMinutes() - startMinute >= amount,
                "Hours" => TotalMinutes() - startMinute >= amount * 60,
                _ => elapsed >= doneMs
            };
            return over ? true : null;
        };
    }

    private static double Amount(EventCommand command) =>
        double.TryParse(command.Get("Duration"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || double.TryParse(command.Get("Duration"), out v) ? Math.Max(0.01, v) : 1;

    private static SpriteDirection? FacingParam(EventCommand command) => Enum.TryParse<SpriteDirection>(command.Get("Facing"), out var f) ? f : null;

    // ================= Things on the map =================

    /// <summary>The thing a command picked on the map: the very one if it's still there, else the same kind of thing within 2 cells of where it was.</summary>
    private object? ThingOf(EventCommand command)
    {
        var parts = command.Get("Thing").Split(':');
        int.TryParse(command.Get("X"), out int x);
        int.TryParse(command.Get("Y"), out int y);
        int Near(int tx, int ty) => CellsApart((tx, ty), (x, y));
        if (parts.Length == 3 && parts[0] == "Object" && int.TryParse(parts[1], out int instanceId) && int.TryParse(parts[2], out int objectId))
            return _map.Objects.FirstOrDefault(o => o.InstanceId == instanceId && o.ObjectId == objectId && o.ContainerInstanceId < 0)
                ?? _map.Objects.Where(o => o.ObjectId == objectId && o.ContainerInstanceId < 0 && Near(o.X, o.Y) <= 2).OrderBy(o => Near(o.X, o.Y)).FirstOrDefault();
        if (parts.Length == 3 && parts[0] == "Item" && Enum.TryParse<ReferencedItemCategory>(parts[1], out var category) && int.TryParse(parts[2], out int itemId))
            return _map.Items.Where(i => i.Category == category && i.ItemId == itemId && Near(i.X, i.Y) <= 2).OrderBy(i => Near(i.X, i.Y)).FirstOrDefault();
        return null;
    }

    private static (int X, int Y) CellOfThing(object thing) => thing switch
    {
        ObjectInstance o => (o.X, o.Y),
        PlacedItemInstance i => (i.X, i.Y),
        _ => (-1, -1)
    };

    private bool ThingIsThere(object thing) => thing switch
    {
        ObjectInstance o => _map.Objects.Contains(o),
        PlacedItemInstance i => _map.Items.Contains(i),
        _ => false
    };

    /// <summary>Walk up to a thing, then turn to it.</summary>
    private Func<Func<double, bool?>?> GoTo(CharacterDefinition who, object thing) => () =>
    {
        if (!ThingIsThere(thing)) return null;
        var cell = CellOfThing(thing);
        var walk = WalkTo(who, cell, beside: true);
        if (walk is null) return null;
        return elapsed =>
        {
            if (!ThingIsThere(thing)) return false; // gone meanwhile
            var result = walk(elapsed);
            if (result == true) FaceCell(who, cell.X, cell.Y);
            return result;
        };
    };

    private bool Cancel(string why)
    {
        StatusText.Text = why;
        return false;
    }

    // ================= Walk To Location =================

    private bool WalkToLocationCommand(EventCommand command, Action<bool> done)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who)) return false;
        if (!int.TryParse(command.Get("X"), out int x) || !int.TryParse(command.Get("Y"), out int y)) return false;
        if (WalkTo(who, (x, y), beside: false) is not { } walk) return false;
        BeginScriptAction(who, elapsed =>
        {
            var result = walk(elapsed);
            if (result == false) _execContext.ScriptData.AddLog($"{who.Name} can't find a way to ({x}, {y})."); // in the F9 debug log too: the next command shows its own words straight away
            return result;
        }, done);
        return true;
    }

    // ================= Walk To Floor Location =================

    /// <summary>As Walk To Location, but to the cell on the floor the command names - up or down the stairs to it.</summary>
    private bool WalkToFloorLocationCommand(EventCommand command, Action<bool> done)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who)) return false;
        if (!int.TryParse(command.Get("X"), out int x) || !int.TryParse(command.Get("Y"), out int y)) return false;
        int floor = int.TryParse(command.Get("Floor"), out int f) ? Math.Clamp(f, 1, MapFloors.MaxFloors) : 1;
        if (WalkTo(who, (x, y), beside: false, floor) is not { } walk) return false;
        BeginScriptAction(who, elapsed =>
        {
            var result = walk(elapsed);
            if (result == false) _execContext.ScriptData.AddLog($"{who.Name} can't find a way to ({x}, {y}) on floor {floor}."); // in the F9 debug log too: the next command shows its own words straight away
            return result;
        }, done);
        return true;
    }

    // ================= Pickup Item =================

    private bool PickupItemCommand(EventCommand command, Action<bool> done)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who) || ThingOf(command) is not { } thing) return false;
        BeginScriptAction(who, Chain(GoTo(who, thing), PoseOnce(who, "Picking"), Do(() => PickUp(who, thing))), done);
        return true;
    }

    /// <summary>Dragging_info::start's rules (too heavy to move, someone on it), then Actor::add as for a drop on them.</summary>
    private bool PickUp(CharacterDefinition who, object thing)
    {
        if (!ThingIsThere(thing)) return Cancel("It isn't there any more.");
        if (thing is ObjectInstance obj)
        {
            if (!IsMovable(obj)) return Cancel($"\"{NameOfEntity(obj)}\" is too heavy to move.");
            if (_seats.Values.Any(s => ReferenceEquals(s.Furniture, obj)) || _headingToSeat.Values.Any(h => ReferenceEquals(h.Furniture, obj))) return Cancel("Someone is on it.");
        }
        if (thing is PlacedItemInstance item && IsTooHeavyToLift(item)) return Cancel("That's too heavy to lift.");
        DragOrigin origin = thing is ObjectInstance o ? new MapObjectOrigin(o) : new MapItemOrigin((PlacedItemInstance)thing);
        var (category, itemId) = PayloadIdentity(origin);
        _quietRefusals = true;
        bool taken;
        try { taken = DropOnCharacter(origin, who); }
        finally { _quietRefusals = false; }
        if (!taken) return false; // too heavy, no room (the status bar says which)
        StatusText.Text = $"{who.Name} picks up \"{PayloadName(origin)}\".";
        FireDragTrigger(category, itemId, BookTriggerConditionBased.ByPickupItem);
        return true;
    }

    // ================= Drop Item =================

    private bool DropItemCommand(EventCommand command, Action<bool> done)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who)) return false;
        if (!Enum.TryParse<ReferencedItemCategory>(command.Get("Category"), out var category) || !int.TryParse(command.Get("ItemId"), out int itemId)) return false;
        int quantity = int.TryParse(command.Get("Quantity"), out int q) ? Math.Max(1, q) : 1;
        (int X, int Y)? spot = command.Get("AtSpot") == "True" && int.TryParse(command.Get("X"), out int x) && int.TryParse(command.Get("Y"), out int y) ? (x, y) : null;
        if (!CarriedItems(who).Any(c => c.Item.Category == category && c.Item.ItemId == itemId)) return Cancel($"{who.Name} hasn't got it to drop.");
        var steps = new List<Func<Func<double, bool?>?>>();
        if (spot is { } s) steps.Add(() => WalkTo(who, s, beside: true) is { } walk ? e => { var r = walk(e); if (r == true) FaceCell(who, s.X, s.Y); return r; } : null);
        steps.Add(PoseOnce(who, "Picking"));
        steps.Add(Do(() =>
        {
            if (TakeFromCarrier(who, category, itemId, quantity) is not { } items) return Cancel($"{who.Name} hasn't got {quantity} of it to drop.");
            var (cx, cy) = spot ?? CellOf(who);
            foreach (var item in items) PutDownAt(item, who, (cx, cy));
            RefreshAfterMove();
            FireGlobalInventoryStateTrigger(BookTriggerConditionBased.ByNotHavingAnItem);
            FireDragTrigger(category, itemId, BookTriggerConditionBased.ByDropItem);
            return true;
        }));
        BeginScriptAction(who, Chain(steps.ToArray()), done);
        return true;
    }

    // ================= Equip / Unequip =================

    /// <summary>Where something they carry is, at any depth: the item, and the container holding it (null = straight on them, in a slot).</summary>
    private (InventoryItemInstance Item, InventoryItemInstance? Holder)? FindCarried(CharacterDefinition who, ReferencedItemCategory category, int itemId, Func<InventoryItemInstance, bool>? prefer = null)
    {
        var all = new List<(InventoryItemInstance Item, InventoryItemInstance? Holder)>();
        void Walk(List<InventoryItemInstance> list, InventoryItemInstance? holder)
        {
            foreach (var item in list)
            {
                if (item.Category == category && item.ItemId == itemId) all.Add((item, holder));
                Walk(item.Contents, item);
            }
        }
        Walk(who.Inventory, null);
        if (all.Count == 0) return null;
        return prefer is not null && all.FirstOrDefault(f => prefer(f.Item)) is { Item: not null } preferred ? preferred : all[0];
    }

    /// <summary>Where it is now, as a drag would pick it up from there.</summary>
    private DragOrigin? OriginOfCarried(CharacterDefinition who, InventoryItemInstance item, InventoryItemInstance? holder)
    {
        if (holder is not null)
            return ContainerDef(holder.ItemId) is { } data ? new ContainerOrigin(holder, data, item) : null;
        var slot = ItemsInSlots(who).FirstOrDefault(e => ReferenceEquals(e.Item, item)).Slot;
        return slot is not null ? new SlotOrigin(who, slot, item) : new BackpackOrigin(who, item);
    }

    /// <summary>The slot something is readied in: its own (weapon, armor, ammo, a container's Location), else the right hand.</summary>
    private object SlotToReady(CharacterDefinition who, InventoryItemInstance item)
    {
        object? own = item.Category == ReferencedItemCategory.Container ? ContainerLocationSlot(item.ItemId, who)
            : DefinitionOf(item) is { } def ? (def.Type == ObjectType.Ammo ? AmmoSlotFor(def, who) : OwnSlotFor(def, who)) : null;
        if (own is EquipmentSlot builtin && !who.Paperdoll.IsSlotShown(builtin)) own = null; // a slot turned off isn't there
        return own ?? EquipmentSlot.RightHand;
    }

    private bool EquipItemCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who)) return false;
        if (!Enum.TryParse<ReferencedItemCategory>(command.Get("Category"), out var category) || !int.TryParse(command.Get("ItemId"), out int itemId)) return false;
        // One not readied yet, if there is one.
        if (FindCarried(who, category, itemId, i => !ItemsInSlots(who).Any(e => ReferenceEquals(e.Item, i))) is not { } found) return Cancel($"{who.Name} hasn't got it to ready.");
        var (item, holder) = found;
        object slot = SlotToReady(who, item);
        if (TryGetEquippedInstanceId(who, slot, out int heldId) && heldId == item.InstanceId && holder is null) return true; // already there
        // Whatever is in the way comes off into the pack (or onto the floor): the slot, and the left hand for a two-handed thing.
        var clear = new List<object> { slot };
        if (IsPaperdollItemTwoHanded(item) && Equals(slot, EquipmentSlot.RightHand)) clear.Add(EquipmentSlot.LeftHand);
        if (Equals(slot, EquipmentSlot.LeftHand) && IsPaperdollLeftHandLocked(who)) clear.Add(EquipmentSlot.RightHand); // a two-handed weapon holds the left hand
        foreach (var key in clear)
            if (TryGetEquippedInstanceId(who, key, out int occupantId) && who.Inventory.FirstOrDefault(i => i.InstanceId == occupantId) is { } occupant && !ReferenceEquals(occupant, item))
                StowOrDrop(who, key, occupant);
        if (OriginOfCarried(who, item, holder) is not { } origin) return false;
        EquipInSlot(origin, who, slot);
        StatusText.Text = $"{who.Name} readies \"{PayloadName(origin)}\".";
        return true;
    }

    private bool UnequipItemCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who)) return false;
        if (!Enum.TryParse<ReferencedItemCategory>(command.Get("Category"), out var category) || !int.TryParse(command.Get("ItemId"), out int itemId)) return false;
        var worn = ItemsInSlots(who).FirstOrDefault(e => e.Item.Category == category && e.Item.ItemId == itemId);
        if (worn.Item is null) return Cancel($"{who.Name} isn't wearing or holding it.");
        StowOrDrop(who, worn.Slot, worn.Item);
        RefreshAfterMove();
        return true;
    }

    /// <summary>Off a slot and into the Backpack, then the Pouch, if it fits - otherwise dropped at their feet.</summary>
    private void StowOrDrop(CharacterDefinition who, object slot, InventoryItemInstance item)
    {
        if (TryStowInBag(who, slot, item)) return;
        RemoveFromCharacter(who, item);
        PutDownAt(item, who);
    }

    /// <summary>Off a slot into the Backpack, then the Pouch, if it fits. False = no bag has room (it stays where it is).</summary>
    private bool TryStowInBag(CharacterDefinition who, object slot, InventoryItemInstance item)
    {
        var origin = new SlotOrigin(who, slot, item);
        foreach (var bagSlot in new[] { EquipmentSlot.Backpack, EquipmentSlot.Pouch })
        {
            if (Equals(bagSlot, slot) || !who.EquippedSlots.TryGetValue(bagSlot, out int bagId)) continue;
            if (who.Inventory.FirstOrDefault(i => i.InstanceId == bagId) is not { Category: ReferencedItemCategory.Container } bag || ReferenceEquals(bag, item)) continue;
            if (ContainerDef(bag.ItemId) is not { } data || !CanContainerAccept(data, bag, origin, out _, out _)) continue;
            if (TryCombineIntoContents(bag.Contents, item.Category, item.ItemId, Math.Max(1, item.Quantity))) { RemoveFromSource(origin); return true; }
            var stored = DetachAsInventoryItem(origin, data.GetNextContentsInstanceId);
            PlaceWithinFrame(stored, data, null);
            bag.Contents.Add(stored);
            StatusText.Text = $"{who.Name} puts \"{PayloadName(new ContainerOrigin(bag, data, stored))}\" away.";
            return true;
        }
        return false;
    }

    // ================= Poses =================

    /// <summary>Wait: nobody does anything - the event's next command just comes that much later (real seconds, or game minutes / hours by the clock).</summary>
    private bool WaitCommand(EventCommand command, Action<bool> done)
    {
        string unit = command.Get("Unit") is { Length: > 0 } u ? u : "Seconds";
        string text = command.Get("Duration") is { Length: > 0 } d ? d : command.Get("Seconds");
        double amount = double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) || double.TryParse(text, out v) ? Math.Max(0, v) : 1;
        _scriptWaits.Add((Lasting(unit, amount, 0), done));
        return true;
    }

    private bool PlayPoseCommand(EventCommand command, Action<bool> done)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who)) return false;
        string pose = command.Get("Pose");
        if (PoseByName(pose) is null) return Cancel($"There's no pose \"{pose}\".");
        var facing = FacingParam(command);
        string unit = command.Get("Unit");
        double amount = Amount(command);
        BeginScriptAction(who, Chain(
            () =>
            {
                StandUpIfSeated(who, unless: pose is "Sit" or "Sleeping");
                ShowPose(who, pose, facing, restart: true);
                return Lasting(unit, amount, PoseLengthMs(who, pose));
            },
            Do(() => StandIdle(who))), done);
        return true;
    }

    private bool ChangePoseCommand(EventCommand command)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who)) return false;
        string pose = command.Get("Pose");
        if (PoseByName(pose) is null) return Cancel($"There's no pose \"{pose}\".");
        ShowPose(who, pose, FacingParam(command), restart: true);
        return true;
    }

    private bool StandUpIfSeated(CharacterDefinition who, bool unless = false)
    {
        if (!unless && _seats.ContainsKey(who)) StandUp(who);
        return true;
    }

    // ================= Attack Object =================

    private bool AttackObjectCommand(EventCommand command, Action<bool> done)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who) || ThingOf(command) is not { } thing) return false;
        BeginScriptAction(who, Chain(GoTo(who, thing), () => StrikeUntilBroken(who, thing)), done);
        return true;
    }

    /// <summary>Blows at the speed of a fight (Exult's dex points: each standard delay adds Dexterity; 30 makes a blow), until it's gone - a minute of blows and it still stands: cancelled.</summary>
    private Func<double, bool?> StrikeUntilBroken(CharacterDefinition who, object thing)
    {
        double elapsed = 0, untilTick = 0, poseBack = -1;
        int dexPoints = 0;
        var cell = CellOfThing(thing);
        if (!CanBeBroken(thing)) return _ => Cancel($"\"{NameOfEntity(thing)}\" can't be broken.");
        return ms =>
        {
            if (!ThingIsThere(thing) || thing is ObjectInstance { IsSpentRuntime: true } && DefinitionOf((ObjectInstance)thing) is { Type: ObjectType.Window }) { StandIdle(who); return true; } // broken (a window broken where it is)
            elapsed += ms;
            if (elapsed > 60_000) { StandIdle(who); return Cancel($"{who.Name} gives up on \"{NameOfEntity(thing)}\" - a minute of blows and it still stands."); }
            if (poseBack > 0 && (poseBack -= ms) <= 0) ShowPose(who, "Idle");
            if ((untilTick -= ms) > 0) return null;
            untilTick = StdDelay;
            if (dexPoints < DexToAttack) { dexPoints += Math.Max(1, Stat(who, BuiltInStatKind.Dexterity, 10)); return null; }
            dexPoints -= DexToAttack;
            FaceCell(who, cell.X, cell.Y, "Attacking Melee");
            poseBack = 2 * StdDelay;
            StrikeThing(thing, who);
            return null;
        };
    }

    /// <summary>Whether blows can break it at all (StrikeThing's "no effect" cases): an object marked breakable, or a container that can be destroyed (not a body, not one with its own key).</summary>
    private bool CanBeBroken(object thing) => thing switch
    {
        ObjectInstance o => DefinitionOf(o) is { } def && (IsBreakable(def) || (def.Type == ObjectType.Explosive && def.Explosive.ExplodesWhenStruck)),
        PlacedItemInstance { Category: ReferencedItemCategory.Container } c => ContainerDef(c.ItemId) is { Destructible: true, IsBodyContainer: false, RequiredKeyItemId: < 0 },
        _ => false
    };

    // ================= Play Animation =================

    private bool PlayAnimationCommand(EventCommand command, Action<bool> done)
    {
        var sprite = _sprites.Groups.FirstOrDefault(g => g.Name == command.Get("SpriteGroup"))?.Sprites.FirstOrDefault(s => s.Name == command.Get("SpriteName"));
        if (sprite is not { Frames.Count: > 0 }) return Cancel($"Play Animation: there's no sprite \"{command.Get("SpriteName")}\" with frames.");
        CharacterDefinition? over = null;
        (double X, double Y) at;
        int lift = 0;
        if (command.Get("Where") == "Character")
        {
            if (CharacterDefinitionFor(command.Get("Who")) is not { } who || double.IsNaN(PixelOf(who).X)) return false;
            over = who;
            at = PixelOf(who);
            lift = LiftOf(who);
        }
        else
        {
            if (!int.TryParse(command.Get("X"), out int x) || !int.TryParse(command.Get("Y"), out int y) || !InsideMap(x, y)) return false;
            at = (CellCenterX(x), CellCenterY(y));
            lift = StandingLiftOnFloor(new UnitRect(x * _tileWidth / SpriteSizing.BaseUnit, y * _tileHeight / SpriteSizing.BaseUnit, Math.Max(1, _tileWidth / SpriteSizing.BaseUnit), Math.Max(1, _tileHeight / SpriteSizing.BaseUnit)), MapFloors.FloorOfLift(_playerZ)); // on whatever is there, on the player's floor
        }
        var (width, height) = QuantizedSpriteSize(sprite);
        var image = new Image { Width = width, Height = height, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        Panel.SetZIndex(image, AboveEverythingZIndex - 1);
        GameCanvas.Children.Add(image);
        PlaceEffect(image, at, lift);
        RegisterWorldAnimator(image, new SpriteAnimator(sprite, image, _resources));
        double onceMs = sprite.Frames.Sum(f => Math.Max(1, f.DurationMs));
        var lasting = Lasting(command.Get("Unit"), Amount(command), onceMs * Math.Max(1, Math.Round(Amount(command))));
        bool wait = command.Get("Wait") != "False";
        _scriptEffects.Add(new ScriptEffect { Image = image, Follows = over, Over = ms => lasting(ms) == true, Done = wait ? () => done(true) : null });
        if (!wait) done(true);
        return true;
    }

    /// <summary>Bottom-centre on the feet line of a spot, shifted up-left 4px a lift as everything raised is.</summary>
    private void PlaceEffect(Image image, (double X, double Y) feet, int lift)
    {
        if (double.IsNaN(feet.X)) return;
        Canvas.SetLeft(image, feet.X - image.Width / 2 - lift * LiftPixelShift);
        Canvas.SetTop(image, feet.Y + _tileHeight / 2.0 - image.Height - lift * LiftPixelShift);
    }

    // ================= Sit =================

    private bool SitCommand(EventCommand command, Action<bool> done)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who)) return false;
        if (ThingOf(command) is not ObjectInstance chair || DefinitionOf(chair) is not { Type: ObjectType.Chair } obj) return Cancel("Sit: that chair isn't there.");
        if (who == _player)
        {
            bool started = false;
            BeginScriptAction(who, _ =>
            {
                if (_heldDirectionKeys.Count > 0) return false;
                if (!started) { started = true; SitOn(chair, obj); return null; } // walks there, looks at it, turns, sits (as a double-click)
                if (_seats.TryGetValue(who, out var seat)) return ReferenceEquals(seat.Furniture, chair);
                return _pendingSit is null && _playerWalkPath is null ? false : null;
            }, done);
            return true;
        }
        if (MapCharacterOf(who) is not { } mapCharacter) return false;
        int attempts = 0;
        double wait = 0;
        BeginScriptAction(who, ms =>
        {
            if (_seats.TryGetValue(who, out var seat)) return ReferenceEquals(seat.Furniture, chair);
            if (mapCharacter.Npc.Path is not null) return null; // on the way
            if ((wait -= ms) > 0) return null;
            if (++attempts > 5) return Cancel($"{who.Name} can't get a seat on \"{obj.Name}\".");
            if (!HeadForChair(mapCharacter, chair, obj)) wait = 700;
            return null;
        }, ok =>
        {
            done(ok);
            // Seated, with nothing else to do: it sits for 10-25 seconds, then gets up and goes back to its schedule.
            if (ok && !IsScripted(who) && _seats.ContainsKey(who))
                BeginScriptAction(who, WaitFor(10_000 + Rng.Next(15_000)), stayed => { if (stayed) StandUp(who); }, afterEvent: true);
        });
        return true;
    }

    /// <summary>Sit_schedule for one chair: its free seat nearest them, walked to (beside it, round the table), then sat on (ArriveAtFurniture at the end of the path).</summary>
    private bool HeadForChair(MapCharacter mapCharacter, ObjectInstance chair, WorldObject obj)
    {
        var me = mapCharacter.Character;
        StandUp(me);
        int spot = NearestFreeSpot(chair, obj, mapCharacter.PixelX, mapCharacter.PixelY, me);
        if (spot < 0 || SpotPosition(chair, obj, spot) is not { } p) return false;
        var goal = ((int)Math.Floor(p.X / _tileWidth), (int)Math.Floor((p.Y - 1) / _tileHeight));
        var here = (mapCharacter.Placement.X, mapCharacter.Placement.Y);
        var path = Heuristic(here, goal) <= 1 ? new List<(int X, int Y)>() : FindPath(mapCharacter, goal, besideOnly: true);
        if (path is null) return false;
        _headingToSeat[me] = (chair, spot);
        if (path.Count == 0) ArriveAtFurniture(mapCharacter);
        else StartPath(mapCharacter, path);
        return true;
    }

    // ================= Doors =================

    private bool OpenDoorCommand(EventCommand command, Action<bool> done)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who)) return false;
        if (ThingOf(command) is not ObjectInstance door || DefinitionOf(door) is not { Type: ObjectType.Door } obj) return Cancel("Open Door: that door isn't there.");
        BeginScriptAction(who, Chain(GoTo(who, door),
            Do(() =>
            {
                if (IsDoorOpen(door)) return true;
                if (!IsDoorLocked(door)) return true;
                if (obj.Door.MagicallyLocked || !CarriesKeyFor(who, obj.Door.RequiredKeyItemId))
                {
                    PlaySoundAt(obj.Door.LockedRattleSoundResourceId, door);
                    ShowFloatingTextOver(VisualOf(door), "Locked");
                    return Cancel(obj.Door.MagicallyLocked ? $"\"{obj.Name}\" is magically locked." : $"{who.Name} has no key for \"{obj.Name}\".");
                }
                UnlockDoor(door, obj, "unlockkey"); // keyring.uc UseKeyOnDoor, as the main character's key does
                return true;
            }),
            () => WaitFor(IsDoorOpen(door) ? 0 : 400),
            Do(() =>
            {
                if (IsDoorOpen(door)) return true;
                OpenDoor(door, obj, who == _player ? null : MapCharacterOf(who));
                return true;
            })), done);
        return true;
    }

    /// <summary>Their key for it, loose or on a keyring they carry (keyring.uc).</summary>
    private bool CarriesKeyFor(CharacterDefinition who, int keyId) =>
        keyId >= 0 && CarriedItems(who).Any(c => c.Item.Category == ReferencedItemCategory.Object && (c.Item.ItemId == keyId || c.Item.KeyIdsRuntime?.Contains(keyId) == true));

    /// <summary>Unlocked: its sound, "Unlocked" over it, its look, the record (Advance IF) and its By Stay event - as UseKeyOnDoor / UseLockpickOnDoor do.</summary>
    private void UnlockDoor(ObjectInstance door, WorldObject obj, string record)
    {
        door.IsLockedRuntime = false;
        PlaySoundAt(obj.Door.UnlockSoundResourceId >= 0 ? obj.Door.UnlockSoundResourceId : obj.Door.LockSoundResourceId, door);
        ShowFloatingTextOver(VisualOf(door), "Unlocked");
        RefreshObjectLook(door);
        RecordItem(record, ReferencedItemCategory.Object, obj.Id);
        FireObjectEvent(door, obj, BookTriggerConditionBased.ByStay);
    }

    private bool PicklockDoorCommand(EventCommand command, Action<bool> done)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who)) return false;
        if (ThingOf(command) is not ObjectInstance door || DefinitionOf(door) is not { Type: ObjectType.Door } obj) return Cancel("Picklock Door: that door isn't there.");
        BeginScriptAction(who, Chain(GoTo(who, door), () => PickTheLock(who, door, obj)), done);
        return true;
    }

    /// <summary>UseLockpickOnDoor, again and again: Dexterity against the lock; a failure breaks that lockpick and the next is tried; out of lockpicks - cancelled.</summary>
    private Func<double, bool?> PickTheLock(CharacterDefinition who, ObjectInstance door, WorldObject obj)
    {
        double wait = 0;
        var cell = (door.X, door.Y);
        return ms =>
        {
            if (!IsDoorLocked(door)) { StandIdle(who); return true; }
            if (obj.Door.MagicallyLocked) return Cancel($"\"{obj.Name}\" is magically locked - no lockpick opens it.");
            if ((wait -= ms) > 0) return null;
            var lockpick = CarriedItems(who).Select(c => DefinitionOf(c.Item)).FirstOrDefault(d => d is { Type: ObjectType.Lockpick });
            if (lockpick is null) { StandIdle(who); return Cancel($"{who.Name} has no lockpick left, and \"{obj.Name}\" is still locked."); }
            FaceCell(who, cell.X, cell.Y, "Picking");
            wait = Math.Max(1500, PoseLengthMs(who, "Picking"));
            int chance = Math.Clamp((Stat(who, BuiltInStatKind.Dexterity, 10) - obj.Door.PickLockDifficulty) * 5 + 50, 5, 95);
            if (Rng.Next(100) < chance)
            {
                UnlockDoor(door, obj, "picklocked");
                StatusText.Text = $"{who.Name} picks the lock on \"{obj.Name}\". ({chance}% chance)";
                return null; // done next frame (unlocked)
            }
            TakeFromCarrier(who, ReferencedItemCategory.Object, lockpick.Id, 1); // it breaks
            PlaySoundFrom(obj.Door.LockpickBrokeSoundResourceId, who);
            Bark(who, VerboseKind.LockpickFailed);
            StatusText.Text = $"{who.Name} fails to pick the lock on \"{obj.Name}\", and \"{lockpick.Name}\" breaks. ({chance}% chance)";
            FireObjectEvent(door, obj, BookTriggerConditionBased.ByExit);
            RefreshAfterMove();
            return null;
        };
    }

    // ================= Pick Item From Container =================

    private bool TakeFromContainerCommand(EventCommand command, Action<bool> done)
    {
        if (CharacterDefinitionFor(command.Get("Who")) is not { } who || IsDead(who)) return false;
        if (ThingOf(command) is not PlacedItemInstance { Category: ReferencedItemCategory.Container } container
            || ContainerDef(container.ItemId) is not { } data) return Cancel("Pick Item From Container: that container isn't there.");
        bool hasCategory = Enum.TryParse<ReferencedItemCategory>(command.Get("Category"), out var category);
        bool any = !int.TryParse(command.Get("ItemId"), out int itemId) || itemId < 0 || !hasCategory; // "(everything)"
        int quantity = int.TryParse(command.Get("Quantity"), out int q) ? Math.Max(1, q) : 1;
        BeginScriptAction(who, Chain(GoTo(who, container), PoseOnce(who, "Picking"), Do(() =>
        {
            if (IsLockedNow(container, data)) { PlaySoundFrom(data.LockedRattleSoundResourceId, who); return Cancel($"\"{data.Name}\" is locked."); } // it must be unlocked
            EnsureContainerSeeded(container, data); // never opened: its starting contents are in it
            int taken = 0, left = quantity;
            foreach (var item in container.Contents.ToList())
            {
                if (!any && (item.Category != category || item.ItemId != itemId)) continue;
                if (!any && left <= 0) break;
                int have = Math.Max(1, item.Quantity);
                if (!any && have > left)
                {
                    // Part of a stack: that many split off and handed over; refused - back on the stack.
                    item.Quantity = have - left;
                    var part = new InventoryItemInstance { Category = item.Category, ItemId = item.ItemId, Quantity = left };
                    if (HandOver(part, who)) { taken += left; left = 0; }
                    else item.Quantity = have;
                    continue;
                }
                _quietRefusals = true;
                bool ok;
                try { ok = DropOnCharacter(new ContainerOrigin(container, data, item), who); }
                finally { _quietRefusals = false; }
                if (!ok) continue; // too heavy / no room: it stays inside
                taken += have;
                left -= have;
            }
            RefreshAfterMove();
            if (taken == 0) return Cancel($"{who.Name} takes nothing from \"{data.Name}\" - {StatusText.Text}");
            StatusText.Text = $"{who.Name} takes {taken} thing{(taken == 1 ? "" : "s")} from \"{data.Name}\".";
            return true;
        })), done);
        return true;
    }
}
