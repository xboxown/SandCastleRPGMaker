using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Object Editor objects, each frame the game clock runs:
///  - Lights burn down by the game minutes passing (spent when done).
///  - Tiles and traps: who is standing on each (player and NPCs alike).
///    Stepping onto a tile fires its By Step On, standing on it By Stand On
///    once a second, stepping off By Step Off. Stepping onto a trap sets it
///    off: damage, maybe an ailment, its sound - unless it's one-shot and
///    spent, or not re-armed yet; a hidden trap shows itself once sprung.
///  - NPCs that opened a door to pass close it behind them once they're
///    clear of the doorway (Exult's Path_walking_actor_action::open_door).
/// </summary>
public partial class GameTestWindow
{
    private double _lastObjectMinute = -1;
    private double _floorStayMs;
    private readonly Dictionary<CharacterDefinition, HashSet<ObjectInstance>> _onFloorObjects = new();
    private readonly Dictionary<ObjectInstance, double> _trapRearmMs = new(ReferenceEqualityComparer.Instance);
    /// <summary>Who was just burnt by a fire, and how long till it burns them again.</summary>
    private readonly Dictionary<CharacterDefinition, double> _burnAgainMs = new();
    /// <summary>Doors NPCs opened on their way, to close behind them.</summary>
    private readonly Dictionary<MapCharacter, ObjectInstance> _doorsToClose = new();

    private void UpdateObjects(double elapsedMs)
    {
        double now = TotalMinutes();
        if (_lastObjectMinute < 0) _lastObjectMinute = now;
        if (now > _lastObjectMinute) { BurnLights(now - _lastObjectMinute); _lastObjectMinute = now; }

        foreach (var key in _trapRearmMs.Keys.ToList())
            if ((_trapRearmMs[key] -= elapsedMs) <= 0) _trapRearmMs.Remove(key);
        foreach (var key in _burnAgainMs.Keys.ToList())
            if ((_burnAgainMs[key] -= elapsedMs) <= 0) _burnAgainMs.Remove(key);
        UpdateSpellTimers(elapsedMs); // summoned creatures, fields, fear, time stop (GameTestWindow.Spells.cs)

        UpdateFloorObjects();
        _floorStayMs += elapsedMs;
        if (_floorStayMs >= 1000)
        {
            _floorStayMs = 0;
            foreach (var (who, tiles) in _onFloorObjects.ToList())
                foreach (var tile in tiles.ToList())
                    if (DefinitionOf(tile) is { Type: ObjectType.Tile } obj) FireObjectEvent(tile, obj, BookTriggerConditionBased.ByStay, MapCharacterOf(who));
        }
        CloseDoorsBehindNpcs();
        UpdateNewTypeObjects(elapsedMs); // plants, fuses, buttons, crafting, roofs
        UpdateLighting(elapsedMs);
        UpdateProjectiles(elapsedMs); // shooting traps, and whatever is flying
        UpdateEggs(elapsedMs); // spawn points and the other eggs (GameTestWindow.Spawns.cs)
        UpdateWeather(elapsedMs); // rain, snow, clouds, lightning
    }

    // ---------- Tiles and traps ----------

    private List<ObjectInstance> _floorThings = new();
    private BodyIndex? _floorIndex;
    private long _floorVersion = -1;

    /// <summary>The tiles, traps and fires on the map, by area - kept till the world changes (GameTestWindow.Perf.cs).</summary>
    private (List<ObjectInstance> Things, BodyIndex Index) FloorThings()
    {
        if (_floorIndex is null || _floorVersion != _worldVersion)
        {
            _floorThings = _map.Objects.Where(o => o.ContainerInstanceId < 0 && DefinitionOf(o) is { Type: ObjectType.Tile or ObjectType.Trap } or { Type: ObjectType.LightSource, Light.BurnsWhenLit: true }).ToList();
            _floorIndex = new BodyIndex(_floorThings.Select(o => BodyOf(null, o)).ToList());
            _floorVersion = _worldVersion;
        }
        return (_floorThings, _floorIndex);
    }

    private UnitRect FootOf(CharacterDefinition who) => who == _player ? PlayerFootprint() : MapCharacterOf(who) is { } mc ? CharacterFootprint(mc) : new UnitRect(-999, -999, 0, 0);

    /// <summary>Which tiles and traps each character stands on now, and what changed since the last frame.</summary>
    private void UpdateFloorObjects()
    {
        var (floorThings, floorIndex) = FloorThings();
        if (floorThings.Count == 0 && _onFloorObjects.Count == 0) return;
        foreach (var who in AllLivingCharacters().ToList())
        {
            if (who == _player && PlayerSeated) continue;
            var foot = FootOf(who);
            int lift = LiftOf(who);
            var now = new HashSet<ObjectInstance>(floorIndex.Overlapping(foot).Select(b => (ObjectInstance)b.Entity).Where(o => Math.Abs(o.Z - lift) <= 1), ReferenceEqualityComparer.Instance);
            var before = _onFloorObjects.GetValueOrDefault(who) ?? new HashSet<ObjectInstance>(ReferenceEqualityComparer.Instance);
            var activator = MapCharacterOf(who);
            foreach (var entered in now.Where(o => !before.Contains(o)).ToList())
            {
                if (DefinitionOf(entered) is not { } obj) continue;
                if (obj.Type == ObjectType.Trap) TriggerTrap(entered, obj, who); // ground, shooting or explosive (GameTestWindow.Projectiles.cs)
                else if (obj.Type == ObjectType.Tile) FireObjectEvent(entered, obj, BookTriggerConditionBased.ByEnter, activator);
            }
            foreach (var fire in now)
                if (fire.IsLitRuntime == true && DefinitionOf(fire) is { Type: ObjectType.LightSource, Light.BurnsWhenLit: true } fireObj && !_burnAgainMs.ContainsKey(who))
                    Burn(fire, fireObj, who);
            foreach (var left in before.Where(o => !now.Contains(o)).ToList())
                if (DefinitionOf(left) is { Type: ObjectType.Tile } obj) FireObjectEvent(left, obj, BookTriggerConditionBased.ByExit, activator);
            _onFloorObjects[who] = now;
        }
    }

    /// <summary>A trap goes off under someone: damage (and an ailment), its sound, revealed if hidden; one-shot traps are then spent.</summary>
    private void SpringTrap(ObjectInstance trap, WorldObject obj, CharacterDefinition victim)
    {
        var t = obj.Trap;
        if ((t.OneShot && trap.TrapSprungRuntime) || _trapRearmMs.ContainsKey(trap)) return;
        bool wasHidden = t.Hidden && !trap.TrapSprungRuntime;
        trap.TrapSprungRuntime = true;
        if (!t.OneShot && t.RearmSeconds > 0) _trapRearmMs[trap] = t.RearmSeconds * 1000;
        PlaySoundAt(t.TriggerSoundResourceId, trap);
        if (wasHidden && VisualOf(trap) is { } visual) visual.Visibility = Visibility.Visible;
        if (victim == _player) StatusText.Text = $"You set off \"{obj.Name}\"!";
        FireObjectEvent(trap, obj, BookTriggerConditionBased.ByEnter, MapCharacterOf(victim));
        int low = Math.Min(t.DamageMin, t.DamageMax), high = Math.Max(t.DamageMin, t.DamageMax);
        int damage = high <= 0 ? 0 : low + Rng.Next(high - low + 1);
        if (damage > 0) Damage(victim, damage, $"a {obj.Name.ToLowerInvariant()}");
        var ailment = t.Ailment switch { TrapAilment.Poisoned => Ailment.Poisoned, TrapAilment.Asleep => Ailment.Asleep, TrapAilment.Paralyzed => Ailment.Paralyzed, _ => (Ailment?)null };
        if (ailment is { } a && !IsDead(victim)) Inflict(victim, a);
    }

    /// <summary>A lit fire burns whoever is in it - again every second they stay (Exult's fire field).</summary>
    private void Burn(ObjectInstance fire, WorldObject obj, CharacterDefinition victim)
    {
        _burnAgainMs[victim] = 1000;
        int low = Math.Min(obj.Light.BurnDamageMin, obj.Light.BurnDamageMax), high = Math.Max(obj.Light.BurnDamageMin, obj.Light.BurnDamageMax);
        int damage = high <= 0 ? 0 : low + Rng.Next(high - low + 1);
        if (victim == _player) StatusText.Text = $"You are burnt by \"{obj.Name}\"!";
        if (damage > 0) Damage(victim, damage, $"a {obj.Name.ToLowerInvariant()}");
    }

    /// <summary>Hidden traps start out of sight.</summary>
    private void HideHiddenTraps()
    {
        foreach (var trap in _map.Objects)
            if (DefinitionOf(trap) is { Type: ObjectType.Trap, Trap.Hidden: true } && !trap.TrapSprungRuntime && VisualOf(trap) is { } visual)
                visual.Visibility = Visibility.Hidden;
    }

    /// <summary>What was just put down while its By Item Dropped On It pages run - only pages for that item (or for any item) answer.</summary>
    private (ReferencedItemCategory Category, int ItemId)? _droppedThing;

    /// <summary>
    /// Something put down on the map (by the player, or a Drop Item / Give Item
    /// command): By Item Dropped On It fires on the tiles under it, on every
    /// object it now rests on (a table, a pedestal, a chest - any type), and on
    /// the map event on its cell. It stays where it was put; a page with a
    /// Select Item only answers to that item.
    /// </summary>
    private void TilesUnderDropped(PlacedItemInstance? item, ObjectInstance? obj)
    {
        var body = BodyOf(item, obj);
        var foot = new UnitRect(body.Left, body.Top, body.Width, body.Depth);
        var previous = _droppedThing;
        _droppedThing = item is not null ? (item.Category, item.ItemId) : (ReferencedItemCategory.Object, obj!.ObjectId);
        try
        {
            foreach (var under in _map.Objects.Where(o => !ReferenceEquals(o, obj) && o.ContainerInstanceId < 0).ToList())
            {
                if (DefinitionOf(under) is not { } def || !def.EventPages.Any(p => p.Conditions.TriggerConditionBased == BookTriggerConditionBased.ByDropItem)) continue;
                bool hit = def.Type == ObjectType.Tile
                    ? BodyOf(null, under).Overlaps(foot) // a tile: anything over it
                    : ObjectBodies(under).Any(b => b.Overlaps(foot) && b.TopLift == body.Z); // anything else: resting right on top of it
                if (hit) FireObjectEvent(under, def, BookTriggerConditionBased.ByDropItem);
            }
            int x = item?.X ?? obj!.X, y = item?.Y ?? obj!.Y;
            if (EventAt(x, y, body.Z) is { } gameEvent)
                TryFireEventPages(gameEvent, gameEvent.EventPages, BookTriggerConditionBased.ByDropItem, selfBookId: -1, $"\"{gameEvent.Name}\" fired (By Drop Item)");
        }
        finally { _droppedThing = previous; }
    }

    /// <summary>By Item Dropped On It with a Select Item: only that item sets the page off.</summary>
    private bool DroppedThingAllowed(BookEventConditions conditions, BookTriggerConditionBased justOccurred)
    {
        if (_droppedThing is not { } dropped || justOccurred != BookTriggerConditionBased.ByDropItem || conditions.TriggerConditionBased != BookTriggerConditionBased.ByDropItem) return true;
        return conditions.TriggerItemReference is not { ItemId: >= 0 } wanted || (wanted.Category == dropped.Category && wanted.ItemId == dropped.ItemId);
    }

    // ---------- NPCs and doors ----------

    /// <summary>Exult paths.cc: for planning a route, a closed unlocked door counts as open (it will be opened on the way); a locked one stays shut - unless the walker is hostile to the door's side and can break it down.</summary>
    private BodySource WorldBodiesForPathing(MapCharacter? walker = null)
    {
        var (_, index, doors) = StillBodies(); // everything still, kept (GameTestWindow.Perf.cs); only the doors depend on the walker
        var extra = doors.Select(b => b.Entity is ObjectInstance o && DefinitionOf(o) is { Type: ObjectType.Door } door && !IsDoorOpen(o)
            && ((!IsDoorLocked(o) && (walker is null || walker.Character.IsHuman)) // a human opens it on the way
                || (walker is not null && IsDoorLocked(o) && HoldsHouseKey(walker, door)) // its own door: it has the key
                || (walker is not null && WouldBreakDown(walker, door, IsDoorLocked(o)))) // or it breaks it down
            ? b with { Solid = false } : b).ToList();
        extra.AddRange(CharacterBodies());
        return new BodySource { Index = index, Extra = extra };
    }

    /// <summary>The faction a door belongs to: its own, or its owner's.</summary>
    private int DoorFaction(WorldObject door) =>
        door.FactionId >= 0 ? door.FactionId : door.BelongToId >= 0 ? _characters?.Characters.FirstOrDefault(c => c.Id == door.BelongToId)?.FactionId ?? -1 : -1;

    /// <summary>An NPC breaks a door down when its faction would attack the door's (a bear breaking into a house) and the door can be broken - a locked one always; a closed, unlocked one only if it's set to break closed doors (Character Editor > Verbose).</summary>
    private bool WouldBreakDown(MapCharacter walker, WorldObject door, bool locked) =>
        (locked || walker.Character.BreaksClosedDoors)
        && IsBreakable(door) && DoorFaction(door) is int faction && faction >= 0
        && _factions.BehaviorToward(walker.Character.FactionId, faction) == FactionBehavior.Attack;

    private readonly Dictionary<MapCharacter, double> _doorBashMs = new();

    /// <summary>A walking NPC blocked by a locked door it's hostile to: it hits the door (a blow every two standard delays) until it breaks. True while doing so.</summary>
    private bool NpcBashesDoor(MapCharacter mapCharacter, UnitRect nextFoot, double elapsedMs)
    {
        foreach (var door in _map.Objects.ToList())
        {
            if (DefinitionOf(door) is not { Type: ObjectType.Door } obj || IsDoorOpen(door) || !BodyOf(null, door).Overlaps(nextFoot)) continue;
            if (!WouldBreakDown(mapCharacter, obj, IsDoorLocked(door))) return false;
            double wait = _doorBashMs.GetValueOrDefault(mapCharacter) - elapsedMs;
            if (wait > 0) { _doorBashMs[mapCharacter] = wait; return true; }
            _doorBashMs[mapCharacter] = 2 * StdDelay;
            var (mx, my) = PixelOf(mapCharacter.Character);
            SetCharacterPose(mapCharacter, "Attacking Melee", FacingTowards(mx, my, door.X * _tileWidth + _tileWidth / 2.0, door.Y * _tileHeight + _tileHeight / 2.0));
            int str = Stat(mapCharacter.Character, BuiltInStatKind.Strength, 10) / 3;
            StrikeObject(door, obj, (str > 0 ? 1 + Rng.Next(str) : 0) + 1, mapCharacter);
            return true;
        }
        return false;
    }

    /// <summary>A walking NPC blocked by a closed, unlocked door opens it (actions.cc open_door). True if it did.</summary>
    private bool NpcOpensDoor(MapCharacter mapCharacter, UnitRect nextFoot)
    {
        if (!mapCharacter.Character.IsHuman) return false; // Exult: only the sentient open doors
        if (NpcUnlocksOwnDoor(mapCharacter, nextFoot)) return true; // its own locked door: the key from its pocket
        if (ClosedUnlockedDoorIn(nextFoot) is not { } door || DefinitionOf(door) is not { } obj) return false;
        if (mapCharacter.Character.BreaksClosedDoors && WouldBreakDown(mapCharacter, obj, locked: false)) return false; // it'd rather break it down
        OpenDoor(door, obj, mapCharacter);
        _doorsToClose[mapCharacter] = door;
        return true;
    }

    /// <summary>Once past it and clear of the doorway, the NPC closes the door behind it.</summary>
    private void CloseDoorsBehindNpcs()
    {
        foreach (var (mapCharacter, door) in _doorsToClose.ToList())
        {
            if (!_map.Objects.Contains(door) || !IsDoorOpen(door) || DefinitionOf(door) is not { } obj || IsDead(mapCharacter.Character)) { _doorsToClose.Remove(mapCharacter); continue; }
            var body = BodyOf(null, door);
            var doorway = new UnitRect(body.Left - 1, body.Top - 1, body.Width + 2, body.Depth + 2); // a tile clear all round
            if (CharacterFootprint(mapCharacter).Overlaps(doorway)) continue;
            if (TryCloseDoor(door, obj, mapCharacter)) // someone in the doorway: try again next frame
            {
                _doorsToClose.Remove(mapCharacter);
                LockBehindIfLeaving(mapCharacter, door, obj); // its own house door, and it's left: locked behind it
            }
        }
    }
}
