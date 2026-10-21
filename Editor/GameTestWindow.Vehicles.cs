using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Vehicles in play - Exult's Barge_object: ships, carts, flying carpets,
/// and whatever else is built in the Vehicles tab.
///  - A vehicle is its pieces (objects marked with it) over a footprint.
///    Everything on it - loose things, characters, the player - is gathered
///    before each step (Barge_object::gather: within its footprint, from its
///    floor up) and moves with it.
///  - Taking the controls: sit in its driver's seat, or double-click its
///    controls (a helm, a lever). A deed may be needed (Vehicles tab >
///    Settings): someone in the party must carry it.
///  - Driving: the direction keys. It turns first if it faces another way -
///    quarter turns, every piece swung round its middle, showing its look
///    for the new direction (Barge_object::turn_right / turn_left /
///    turn_around) - then moves an 8px unit at a time at its speed. Land
///    rolls over walkable ground, Sea sails on Water tiles only, Air flies;
///    nothing solid may stand where it goes (okay_to_rotate, step).
///  - Wheels and draft animals show their Moving look while it rolls (Exult
///    animates barge_wheel / barge_draftanimal frames each step).
/// </summary>
public partial class GameTestWindow
{
    /// <summary>The vehicle the player is driving, and how they took the controls (in its driver's seat, or at its controls).</summary>
    private VehicleState? _driving;
    private ObjectInstance? _drivingFrom;
    /// <summary>A driver's seat the player is walking to: they drive once seated in it.</summary>
    private ObjectInstance? _seatToDriveFrom;
    private double _driveUnits;
    private bool _vehicleRolling;
    private static readonly object VehicleSoundOwner = new();

    private VehicleState? VehicleOf(ObjectInstance piece) => piece.VehicleStateId > 0 ? _map.VehicleStates.FirstOrDefault(v => v.Id == piece.VehicleStateId) : null;
    private static HouseTemplate? TemplateOf(VehicleState vehicle) => ProjectSession.Houses.FirstOrDefault(h => h.Id == vehicle.TemplateId);
    private static VehicleSettings SettingsOf(VehicleState vehicle) => TemplateOf(vehicle)?.Vehicle ?? new VehicleSettings();

    // ---------------- Taking the controls ----------------

    /// <summary>Double-clicking a piece of a vehicle: its controls take or give up the controls; its driver's seat, sat in, drives. False: not a vehicle's controls (used as usual).</summary>
    private bool TryUseVehiclePart(ObjectInstance instance, WorldObject obj)
    {
        if (VehicleOf(instance) is not { } vehicle || _player is null) return false;
        switch (obj.VehicleRole)
        {
            case VehiclePartRole.Controls:
                if (_driving == vehicle) { StopDriving(); return true; }
                if (_driving is not null) StopDriving();
                if (!HasDeedFor(vehicle)) return true;
                BeginDriving(vehicle, instance);
                return true;
            case VehiclePartRole.DriverSeat:
                if (_driving == vehicle && ReferenceEquals(_drivingFrom, instance)) { StopDriving(); StandUp(_player); return true; }
                if (!HasDeedFor(vehicle)) return true;
                _seatToDriveFrom = instance; // drives once seated in it (UpdateVehicles)
                return false; // the chair's own sitting goes on
            default:
                return false;
        }
    }

    /// <summary>Ultima VII's deed: someone in the party carries it (anywhere in their things), if the vehicle needs one.</summary>
    private bool HasDeedFor(VehicleState vehicle)
    {
        var settings = SettingsOf(vehicle);
        if (settings.DeedObjectId < 0) return true;
        bool Carries(IEnumerable<InventoryItemInstance> items) => items.Any(i => (i.Category == ReferencedItemCategory.Object && i.ItemId == settings.DeedObjectId) || Carries(i.Contents));
        if (AllLivingCharacters().Where(IsPartyOrPlayer).Any(c => Carries(c.Inventory))) return true;
        StatusText.Text = settings.NeedDeedText;
        if (settings.NeedDeedText.Length > 0) ShowFloatingTextOver(_playerImage, settings.NeedDeedText, 3000);
        return false;
    }

    private void BeginDriving(VehicleState vehicle, ObjectInstance from)
    {
        _driving = vehicle;
        _drivingFrom = from;
        _driveUnits = 0;
        PlaySound(SettingsOf(vehicle).StartSoundResourceId);
        StatusText.Text = $"You have the controls of the {TemplateOf(vehicle)?.Name ?? "vehicle"}: the direction keys drive it. {(DefinitionOf(from)?.VehicleRole == VehiclePartRole.DriverSeat ? "Get up" : "Double-click the controls again")} to stop.";
    }

    private void StopDriving()
    {
        if (_driving is not { } vehicle) return;
        SetRolling(vehicle, false);
        _driving = null;
        _drivingFrom = null;
        StatusText.Text = "You let go of the controls.";
    }

    /// <summary>Each frame: a seat taken to drive from starts the driving; getting up from it (or the vehicle gone) stops it.</summary>
    private void UpdateVehicles()
    {
        if (_seatToDriveFrom is { } seat && PlayerSeated && _seats.TryGetValue(_player!, out var sitting) && ReferenceEquals(sitting.Furniture, seat))
        {
            _seatToDriveFrom = null;
            if (VehicleOf(seat) is { } vehicle) BeginDriving(vehicle, seat);
        }
        else if (_seatToDriveFrom is not null && _pendingSit is null && !PlayerSeated) _seatToDriveFrom = null; // walked off instead
        if (_driving is not null && _drivingFrom is { } from && DefinitionOf(from)?.VehicleRole == VehiclePartRole.DriverSeat
            && (!PlayerSeated || !ReferenceEquals(_seats[_player!].Furniture, from))) StopDriving();
    }

    /// <summary>A frame while driving: the direction keys turn and move the vehicle (the driver stays at the controls). False when not driving.</summary>
    private bool DriveFrame(double deltaSeconds)
    {
        UpdateVehicles();
        if (_driving is not { } vehicle) return false;
        if (!_map.VehicleStates.Contains(vehicle)) { _driving = null; return false; }
        if (!TryGetActiveHeldDirection(out int dx, out int dy)) { SetRolling(vehicle, false); _driveUnits = 0; return true; }
        var want = (dx, dy) switch { (0, -1) => SpriteDirection.North, (1, 0) => SpriteDirection.East, (-1, 0) => SpriteDirection.West, _ => SpriteDirection.South };
        if (want != vehicle.Direction)
        {
            if (!TurnVehicle(vehicle, want)) { SetRolling(vehicle, false); return true; }
            _driveUnits = 0;
            return true; // the turn is this frame's move
        }
        _driveUnits += Math.Max(0.5, SettingsOf(vehicle).CellsPerSecond) * UnitsPerTileX * deltaSeconds;
        bool moved = false;
        while (_driveUnits >= 1)
        {
            _driveUnits -= 1;
            if (!StepVehicle(vehicle, dx, dy)) { _driveUnits = 0; break; }
            moved = true;
        }
        SetRolling(vehicle, moved || _driveUnits > 0);
        return true;
    }

    /// <summary>Wheels turning and the moving sound while it rolls; the stop sound as it stops.</summary>
    private void SetRolling(VehicleState vehicle, bool rolling)
    {
        if (rolling == _vehicleRolling) return;
        _vehicleRolling = rolling;
        var settings = SettingsOf(vehicle);
        if (rolling) SoundPlayback.TryPlayFor(VehicleSoundOwner, _resources, settings.MovingSoundResourceId, loop: true);
        else { SoundPlayback.StopFor(VehicleSoundOwner); PlaySound(settings.StopSoundResourceId); }
        foreach (var piece in _map.Objects.Where(o => o.VehicleStateId == vehicle.Id && o.ContainerInstanceId < 0).ToList())
        {
            if (DefinitionOf(piece) is not { VehicleRole: VehiclePartRole.Wheel, MovingLook.IsSet: true } wheel) continue;
            piece.LookOverrideRuntime = rolling ? $"{wheel.MovingLook.SpriteGroup}/{wheel.MovingLook.SpriteName}" : null;
            RefreshObjectLook(piece);
        }
    }

    // ---------------- What's on it ----------------

    private sealed record Aboard(List<ObjectInstance> Objects, List<PlacedItemInstance> Items, List<MapCharacter> Characters, bool Player, int TopLift);

    private UnitRect FootprintOf(VehicleState vehicle) => new(vehicle.Left, vehicle.Top, vehicle.Width, vehicle.Depth);

    /// <summary>Barge_object::gather: its own pieces, and whatever is on it - from its floor up to above its highest piece.</summary>
    private Aboard Gather(VehicleState vehicle)
    {
        var foot = FootprintOf(vehicle);
        var objects = _map.Objects.Where(o => o.ContainerInstanceId < 0 && o.VehicleStateId == vehicle.Id).ToList();
        int top = vehicle.Lift + 1;
        foreach (var piece in objects) { var b = BodyOf(null, piece); top = Math.Max(top, b.TopLift); }
        bool OnIt(Body b) => b.Overlaps(foot) && b.Z >= vehicle.Lift && b.Z < top + MapFloors.LiftsPerFloor;
        foreach (var loose in _map.Objects.Where(o => o.ContainerInstanceId < 0 && o.VehicleStateId == 0 && !o.WallSetPiece))
            if (OnIt(BodyOf(null, loose)) && IsMovableAboard(loose)) objects.Add(loose);
        var items = _map.Items.Where(i => OnIt(BodyOf(i, null))).ToList();
        var characters = _mapCharacters.Where(mc => CharacterFootprint(mc).Overlaps(foot) && mc.Lift >= vehicle.Lift && mc.Lift < top + MapFloors.LiftsPerFloor).ToList();
        bool player = _player is not null && PlayerFootprint().Overlaps(foot) && _playerZ >= vehicle.Lift && _playerZ < top + MapFloors.LiftsPerFloor;
        return new Aboard(objects, items, characters, player, top);
    }

    /// <summary>A loose thing rides along unless it's fixed to the world (a wall) or part of the ground under it (a flat tile, a trap).</summary>
    private bool IsMovableAboard(ObjectInstance o) => DefinitionOf(o) is not ({ Type: ObjectType.Wall } or { Type: ObjectType.Tile, IsSolid: false } or { Type: ObjectType.Trap });

    // ---------------- Moving ----------------

    /// <summary>Barge_object::step: one 8px unit that way, if the ground ahead takes it and nothing is in the way; everything aboard goes along.</summary>
    private bool StepVehicle(VehicleState vehicle, int dx, int dy)
    {
        var aboard = Gather(vehicle);
        var from = FootprintOf(vehicle);
        var to = new UnitRect(from.Left + dx, from.Top + dy, from.Width, from.Depth);
        if (!CanVehicleCover(vehicle, aboard, to, from, out string why)) { StatusText.Text = why; return false; }
        vehicle.Left = to.Left;
        vehicle.Top = to.Top;
        MoveAboard(aboard, dx, dy);
        return true;
    }

    /// <summary>Whether the vehicle can cover the new footprint: its new ground (the cells it moves onto) suits it, and nothing solid that isn't aboard is in the way, from its floor to its top.</summary>
    private bool CanVehicleCover(VehicleState vehicle, Aboard aboard, UnitRect to, UnitRect from, out string why)
    {
        why = "";
        int wu = _map.Width * UnitsPerTileX, hu = _map.Height * UnitsPerTileY;
        if (to.Left < 0 || to.Top < 0 || to.Right > wu || to.Bottom > hu) { why = "Blocked: the edge of the map."; return false; }
        var movement = SettingsOf(vehicle).Movement;
        var mine = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var o in aboard.Objects) mine.Add(o);
        foreach (var i in aboard.Items) mine.Add(i);
        foreach (var c in aboard.Characters) mine.Add(c.Placement);
        var world = WorldNow();
        for (int y = to.Top; y < to.Bottom; y++)
            for (int x = to.Left; x < to.Right; x++)
            {
                if (x >= from.Left && x < from.Right && y >= from.Top && y < from.Bottom) continue; // already under it
                if (vehicle.Lift == 0 && movement != MovementKind.Air)
                {
                    int tile = _map.Layers[0].GetTile(x / UnitsPerTileX, y / UnitsPerTileY, _map.Width);
                    var ground = tile >= 0 ? TilePropertiesOf(tile) : null;
                    if (movement == MovementKind.Sea && ground is not { IsWater: true }) { why = "Blocked: a ship sails only on water."; return false; }
                    if (movement == MovementKind.Land && (ground is null || !ground.Walkable || ground.IsWater)) { why = "Blocked: it can't go over that ground."; return false; }
                }
                var unit = new UnitRect(x, y, 1, 1);
                foreach (var body in world.Overlapping(unit))
                {
                    if (!body.Solid || mine.Contains(body.Entity) || !body.Overlaps(unit)) continue;
                    if (body.TopLift <= vehicle.Lift || body.Z >= aboard.TopLift) continue; // under its floor (the ground, a floor slab) or above it (a bridge)
                    why = $"Blocked: {NameOfEntity(body.Entity) ?? "something"} is in the way.";
                    return false;
                }
                if (!aboard.Player && _player is not null && PlayerBody() is { } you && you.Overlaps(unit) && you.TopLift > vehicle.Lift && you.Z < aboard.TopLift) { why = "Blocked: you are in the way."; return false; }
            }
        return true;
    }

    /// <summary>Everything aboard moved by (dx, dy) units - pieces, things, characters and the player - and drawn there.</summary>
    private void MoveAboard(Aboard aboard, int dx, int dy)
    {
        foreach (var o in aboard.Objects) ShiftThing(null, o, dx, dy);
        foreach (var i in aboard.Items) ShiftThing(i, null, dx, dy);
        foreach (var mc in aboard.Characters)
        {
            StopWalking(mc);
            mc.PixelX += dx * SpriteSizing.BaseUnit;
            mc.PixelY += dy * SpriteSizing.BaseUnit;
            mc.Placement.X = (int)Math.Floor(mc.PixelX / _tileWidth);
            mc.Placement.Y = (int)Math.Floor(mc.PixelY / _tileHeight);
            ShiftSeat(mc.Character, dx * SpriteSizing.BaseUnit, dy * SpriteSizing.BaseUnit);
            PositionCharacterVisual(mc);
        }
        if (aboard.Player) MovePlayerBy(dx * SpriteSizing.BaseUnit, dy * SpriteSizing.BaseUnit);
        WorldChanged();
    }

    /// <summary>A thing's place moved by whole 8px units (its cell and the offset within it).</summary>
    private void ShiftThing(PlacedItemInstance? item, ObjectInstance? obj, int dx, int dy)
    {
        int x = item?.X ?? obj!.X, y = item?.Y ?? obj!.Y, ox = item?.OffsetX ?? obj!.OffsetX, oy = item?.OffsetY ?? obj!.OffsetY;
        int ux = x * UnitsPerTileX + ox + dx, uy = y * UnitsPerTileY + oy + dy;
        int cx = (int)Math.Floor(ux / (double)UnitsPerTileX), cy = (int)Math.Floor(uy / (double)UnitsPerTileY);
        SetMapPosition(item, obj, cx, cy, ux - cx * UnitsPerTileX, uy - cy * UnitsPerTileY);
        RedrawPlaced(item, obj);
    }

    private void RedrawPlaced(PlacedItemInstance? item, ObjectInstance? obj)
    {
        if (item is not null && _itemVisuals.TryGetValue(item, out var iv) && iv is FrameworkElement ife) ApplyZToVisual(ife, item.X, item.Y, item.Z, item.OffsetX, item.OffsetY);
        if (obj is not null && _objectVisuals.TryGetValue(obj, out var ov) && ov is FrameworkElement ofe) { ApplyZToVisual(ofe, obj.X, obj.Y, obj.Z, obj.OffsetX, obj.OffsetY); ShiftOpenDoor(obj, ofe); }
    }

    /// <summary>Someone seated aboard: where they'd get up to moves with them.</summary>
    private void ShiftSeat(CharacterDefinition who, double dx, double dy)
    {
        if (_seats.TryGetValue(who, out var seat)) _seats[who] = seat with { FromX = seat.FromX + dx, FromY = seat.FromY + dy };
    }

    private void MovePlayerBy(double dx, double dy)
    {
        _playerPixelX += dx;
        _playerPixelY += dy;
        _playerX = (int)Math.Floor(_playerPixelX / _tileWidth);
        _playerY = (int)Math.Floor(_playerPixelY / _tileHeight);
        if (_player is not null) ShiftSeat(_player, dx, dy);
        SnapPlayerPosition();
        UpdateCamera();
    }

    // ---------------- Turning ----------------

    /// <summary>Barge_object::face_direction: a quarter turn right or left, or right round, if there's room for its new footprint.</summary>
    private bool TurnVehicle(VehicleState vehicle, SpriteDirection want)
    {
        int quarters = (((int)want - (int)vehicle.Direction) / 2 % 4 + 4) % 4; // clockwise quarter turns
        if (quarters == 0) return true;
        var aboard = Gather(vehicle);
        var from = FootprintOf(vehicle);
        double cx = from.Left + from.Width / 2.0, cy = from.Top + from.Depth / 2.0;
        bool sideways = quarters % 2 == 1;
        int w = sideways ? from.Depth : from.Width, d = sideways ? from.Width : from.Depth;
        var to = new UnitRect((int)Math.Round(cx - w / 2.0), (int)Math.Round(cy - d / 2.0), w, d);
        if (!CanVehicleCover(vehicle, aboard, to, from, out string why)) { StatusText.Text = why.Replace("Blocked:", "No room to turn:"); return false; }

        (double X, double Y) Turned(double x, double y) => quarters switch
        {
            1 => (cx - (y - cy), cy + (x - cx)),     // a quarter turn right (clockwise, as the screen is seen)
            2 => (2 * cx - x, 2 * cy - y),
            _ => (cx + (y - cy), cy - (x - cx))      // a quarter turn left
        };
        SpriteDirection TurnedFacing(SpriteDirection f) => (SpriteDirection)(((int)f + quarters * 2) % 8);

        foreach (var o in aboard.Objects)
        {
            var before = BodyOf(null, o);
            var (mx, my) = Turned(before.Left + before.Width / 2.0, before.Top + before.Depth / 2.0);
            o.Facing = TurnedFacing(o.Facing); // its look for the new way (Object Editor > Turned looks), its width and depth swapped if it has them
            PlaceBodyCentre(null, o, mx, my);
            if (DefinitionOf(o) is { TurnLooks.Count: > 0 } || DefinitionOf(o)?.Type is ObjectType.Chair or ObjectType.Bed) RefreshObjectLook(o);
            else RedrawPlaced(null, o);
        }
        foreach (var i in aboard.Items)
        {
            var before = BodyOf(i, null);
            var (mx, my) = Turned(before.Left + before.Width / 2.0, before.Top + before.Depth / 2.0);
            PlaceBodyCentre(i, null, mx, my);
            RedrawPlaced(i, null);
        }
        double unit = SpriteSizing.BaseUnit;
        foreach (var mc in aboard.Characters)
        {
            StopWalking(mc);
            var (px, py) = Turned(mc.PixelX / unit, mc.PixelY / unit);
            ShiftSeat(mc.Character, px * unit - mc.PixelX, py * unit - mc.PixelY);
            mc.PixelX = px * unit;
            mc.PixelY = py * unit;
            mc.Placement.X = (int)Math.Floor(mc.PixelX / _tileWidth);
            mc.Placement.Y = (int)Math.Floor(mc.PixelY / _tileHeight);
            SetCharacterPose(mc, mc.PoseName, TurnedFacing(mc.Facing));
            PositionCharacterVisual(mc);
        }
        if (aboard.Player)
        {
            var (px, py) = Turned(_playerPixelX / unit, _playerPixelY / unit);
            _playerFacing = TurnedFacing(_playerFacing);
            MovePlayerBy(px * unit - _playerPixelX, py * unit - _playerPixelY);
            ApplyPoseSprite(_currentAppliedPose ?? _idlePose);
        }
        vehicle.Left = to.Left;
        vehicle.Top = to.Top;
        vehicle.Width = to.Width;
        vehicle.Depth = to.Depth;
        vehicle.Direction = want;
        WorldChanged();
        RefreshStackingVisuals();
        return true;
    }

    /// <summary>A thing put so its footprint's middle is at (cx, cy) units.</summary>
    private void PlaceBodyCentre(PlacedItemInstance? item, ObjectInstance? obj, double cx, double cy)
    {
        var (_, _, sizeX, sizeY, _) = ResolveStackInfo(item, obj);
        var (solidX, solidY) = ResolveSolidOffset(item, obj);
        int left = (int)Math.Round(cx - sizeX / 2.0), top = (int)Math.Round(cy - sizeY / 2.0);
        // FootprintAt the other way round: the cell and the offset within it that put the footprint there.
        int ux = left - (int)Math.Floor((UnitsPerTileX - sizeX) / 2.0) - solidX;
        int uy = top + sizeY - solidY - UnitsPerTileY;
        int cellX = (int)Math.Floor(ux / (double)UnitsPerTileX), cellY = (int)Math.Floor(uy / (double)UnitsPerTileY);
        SetMapPosition(item, obj, cellX, cellY, ux - cellX * UnitsPerTileX, uy - cellY * UnitsPerTileY);
    }
}
