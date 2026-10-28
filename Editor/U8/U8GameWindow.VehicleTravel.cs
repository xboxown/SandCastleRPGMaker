// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Vehicles, stage 2: travelling by themselves and flying.
///  - Autopilot (Travel Vehicle): the vehicle makes its own way to a spot -
///    straight there, a side at a time (Exult's barge travel_to_tile with its
///    straight-line path), turning as it goes - while everyone aboard is free
///    to walk about it, talk, open things (Final Fantasy's airship). Blocked
///    on the way, it stops.
///  - Flying (Air vehicles): taking the controls (or setting off) it rises
///    to floor 11 - above every floor, and above the birds at floor 10 - if
///    nothing is over it. Letting go of the controls (or arriving) it comes
///    down - only where the ground under all of it is clear and level and not
///    water (Barge_object::okay_to_land); otherwise it stays up and says so.
///  - Drive Vehicle / Stop Driving: a cutscene puts the player at the
///    controls, or takes them away (no deed asked).
/// </summary>
public partial class U8GameWindow
{
    /// <summary>Floor 11: where flying vehicles fly - one floor above the birds (floor 10).</summary>
    private static int VehicleFlyLift => FlyLift + MapFloors.LiftsPerFloor;

    private sealed class Autopilot
    {
        public required VehicleState Vehicle;
        public double ToX, ToY; // the footprint's middle, in 8px units
        public Action<bool>? Done;
        public bool Landing;
        public double Units;
    }

    private readonly List<Autopilot> _autopilots = new();
    /// <summary>Air vehicles going up or down: the lift each is heading for.</summary>
    private readonly Dictionary<VehicleState, int> _climbTo = new(ReferenceEqualityComparer.Instance);
    private double _climbMs;

    private bool OnAutopilot(VehicleState vehicle) => _autopilots.Any(a => a.Vehicle == vehicle);

    // ---------------- Commands ----------------

    private void InitializeVehicleCommands()
    {
        _execContext.GameHandlers[EventCommandType.DriveVehicle] = DriveVehicleCommand;
        _execContext.GameHandlers[EventCommandType.StopDriving] = _ => { if (_driving is null) return false; StopDriving(); return true; };
        _execContext.GameActions[EventCommandType.TravelVehicle] = TravelVehicleCommand;
        _execContext.GameHandlers[EventCommandType.SetVehicleSpeed] = command =>
        {
            if (VehicleFor(command) is not { } vehicle) return false;
            vehicle.Speed = Math.Clamp(FormulaNumber(command, "Speed"), 0, 60);
            return true;
        };
    }

    /// <summary>The vehicle a command names: the built copy of it nearest the player.</summary>
    private VehicleState? VehicleFor(EventCommand command)
    {
        if (!int.TryParse(command.Get("Vehicle"), out int templateId)) return null;
        double px = _playerPixelX / SpriteSizing.BaseUnit, py = _playerPixelY / SpriteSizing.BaseUnit;
        return _map.VehicleStates.Where(v => v.TemplateId == templateId)
            .OrderBy(v => Math.Abs(v.Left + v.Width / 2.0 - px) + Math.Abs(v.Top + v.Depth / 2.0 - py)).FirstOrDefault();
    }

    private bool DriveVehicleCommand(EventCommand command)
    {
        if (VehicleFor(command) is not { } vehicle || _player is null) { StatusText.Text = "Drive Vehicle: there's no such vehicle on this map."; return false; }
        if (_driving is not null) StopDriving();
        var pieces = _map.Objects.Where(o => o.VehicleStateId == vehicle.Id && o.ContainerInstanceId < 0).ToList();
        var from = pieces.FirstOrDefault(o => DefinitionOf(o)?.VehicleRole == VehiclePartRole.Controls)
                   ?? pieces.FirstOrDefault(o => DefinitionOf(o)?.VehicleRole == VehiclePartRole.DriverSeat) ?? pieces.FirstOrDefault();
        if (from is null) return false;
        if (DefinitionOf(from)?.VehicleRole == VehiclePartRole.DriverSeat) _seatToDriveFrom = null;
        BeginDriving(vehicle, from);
        if (DefinitionOf(from)?.VehicleRole == VehiclePartRole.DriverSeat) _drivingFrom = pieces.FirstOrDefault(o => DefinitionOf(o)?.VehicleRole != VehiclePartRole.DriverSeat) ?? from; // put at the controls without sitting: getting up isn't what stops it
        return true;
    }

    /// <summary>Travel Vehicle: off it goes to the spot (its middle there) - flying ones take off first and land there; the event waits for it if asked.</summary>
    private bool TravelVehicleCommand(EventCommand command, Action<bool> done)
    {
        if (VehicleFor(command) is not { } vehicle) { StatusText.Text = "Travel Vehicle: there's no such vehicle on this map."; return false; }
        if (!int.TryParse(command.Get("X"), out int x) || !int.TryParse(command.Get("Y"), out int y) || !InsideMap(x, y)) return false;
        _autopilots.RemoveAll(a => a.Vehicle == vehicle);
        bool wait = command.Get("Wait") != "False";
        var pilot = new Autopilot { Vehicle = vehicle, ToX = (x + 0.5) * UnitsPerTileX, ToY = (y + 0.5) * UnitsPerTileY, Done = wait ? done : null };
        _autopilots.Add(pilot);
        if (SettingsOf(vehicle).Movement == MovementKind.Air) TakeOff(vehicle);
        PlaySound(SettingsOf(vehicle).StartSoundResourceId);
        if (!wait) done(true);
        return true;
    }

    // ---------------- Each frame ----------------

    /// <summary>Autopilots step their vehicles on; flying vehicles climb or come down.</summary>
    private void UpdateVehicleTravel(double deltaSeconds)
    {
        UpdateClimbing(deltaSeconds);
        foreach (var pilot in _autopilots.ToList())
        {
            var vehicle = pilot.Vehicle;
            if (!_map.VehicleStates.Contains(vehicle)) { Finish(pilot, false); continue; }
            if (_climbTo.TryGetValue(vehicle, out int climbing) && climbing > vehicle.Lift) continue; // taking off first
            if (pilot.Landing)
            {
                if (!_climbTo.ContainsKey(vehicle)) Finish(pilot, true); // down
                continue;
            }
            double cx = vehicle.Left + vehicle.Width / 2.0, cy = vehicle.Top + vehicle.Depth / 2.0;
            double rx = pilot.ToX - cx, ry = pilot.ToY - cy;
            if (Math.Abs(rx) < 1 && Math.Abs(ry) < 1)
            {
                SetPilotRolling(vehicle, false);
                if (SettingsOf(vehicle).Movement == MovementKind.Air && vehicle.Lift > 0)
                {
                    if (Land(vehicle)) { pilot.Landing = true; continue; }
                    Finish(pilot, false); // nowhere to land: it stays up, its event goes on
                    continue;
                }
                Finish(pilot, true);
                continue;
            }
            // Keep the heading while it still gets nearer that way; then the other side (one turn, not a zigzag).
            bool facingAlongX = vehicle.Direction is SpriteDirection.East or SpriteDirection.West;
            double ahead = facingAlongX ? rx * (vehicle.Direction == SpriteDirection.East ? 1 : -1) : ry * (vehicle.Direction == SpriteDirection.South ? 1 : -1);
            bool alongX = ahead >= 1 ? facingAlongX : Math.Abs(rx) >= 1 && (Math.Abs(rx) >= Math.Abs(ry) || Math.Abs(ry) < 1);
            int dx = alongX ? Math.Sign(rx) : 0, dy = alongX ? 0 : Math.Sign(ry);
            var want = (dx, dy) switch { (0, -1) => SpriteDirection.North, (1, 0) => SpriteDirection.East, (-1, 0) => SpriteDirection.West, _ => SpriteDirection.South };
            if (want != vehicle.Direction)
            {
                if (!TurnVehicle(vehicle, want)) { Finish(pilot, false); continue; }
                pilot.Units = 0;
                continue;
            }
            pilot.Units += SpeedOf(vehicle) * UnitsPerTileX * deltaSeconds;
            while (pilot.Units >= 1 && Math.Abs(alongX ? pilot.ToX - (vehicle.Left + vehicle.Width / 2.0) : pilot.ToY - (vehicle.Top + vehicle.Depth / 2.0)) >= 1)
            {
                pilot.Units -= 1;
                if (!StepVehicle(vehicle, dx, dy)) { Finish(pilot, false); break; }
            }
            if (_autopilots.Contains(pilot)) SetPilotRolling(vehicle, true);
        }
    }

    private void Finish(Autopilot pilot, bool arrived)
    {
        _autopilots.Remove(pilot);
        SetPilotRolling(pilot.Vehicle, false);
        if (!arrived && pilot.Done is not null) StatusText.Text = $"The {TemplateOf(pilot.Vehicle)?.Name ?? "vehicle"} couldn't get there - {StatusText.Text}";
        pilot.Done?.Invoke(arrived);
    }

    /// <summary>The rolling look and sound for a vehicle under way by itself (the driven one has its own in DriveFrame).</summary>
    private void SetPilotRolling(VehicleState vehicle, bool rolling)
    {
        if (_driving == vehicle && !OnAutopilot(vehicle)) return;
        SetRolling(vehicle, rolling);
    }

    // ---------------- Up and down ----------------

    /// <summary>An Air vehicle goes up to floor 11 (as high as it can, under anything over it).</summary>
    private void TakeOff(VehicleState vehicle)
    {
        if (SettingsOf(vehicle).Movement != MovementKind.Air || vehicle.Lift >= VehicleFlyLift) return;
        _climbTo[vehicle] = VehicleFlyLift;
    }

    /// <summary>Barge_object::okay_to_land: every unit under it clear down to the same level ground, and not water - then it comes down there. False (it stays up): nowhere to land.</summary>
    private bool Land(VehicleState vehicle)
    {
        if (vehicle.Lift == 0) return true;
        var aboard = Gather(vehicle);
        var mine = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var o in aboard.Objects) mine.Add(o);
        foreach (var i in aboard.Items) mine.Add(i);
        foreach (var c in aboard.Characters) mine.Add(c.Placement);
        var foot = FootprintOf(vehicle);
        var world = WorldNow();
        int? level = null;
        for (int y = foot.Top; y < foot.Bottom; y++)
            for (int x = foot.Left; x < foot.Right; x++)
            {
                var unit = new UnitRect(x, y, 1, 1);
                int top = 0;
                foreach (var body in world.Overlapping(unit))
                    if (body.Solid && !mine.Contains(body.Entity) && body.Overlaps(unit) && body.Z < vehicle.Lift) top = Math.Max(top, body.TopLift);
                if (top == 0)
                {
                    int tile = _map.Layers[0].GetTile(x / UnitsPerTileX, y / UnitsPerTileY, _map.Width);
                    if (tile >= 0 && TilePropertiesOf(tile) is { IsWater: true }) { StatusText.Text = "There's no place to land here: water below."; return false; }
                }
                if (level is null) level = top;
                else if (level != top) { StatusText.Text = "There's no place to land here: the ground below isn't clear and level."; return false; }
            }
        _climbTo[vehicle] = level ?? 0;
        return true;
    }

    /// <summary>A lift at a time (as a flier climbs): everything aboard goes up or down with it; something in the way above stops the climb.</summary>
    private void UpdateClimbing(double deltaSeconds)
    {
        if (_climbTo.Count == 0) return;
        _climbMs += deltaSeconds * 1000;
        while (_climbMs >= 60 && _climbTo.Count > 0)
        {
            _climbMs -= 60;
            foreach (var (vehicle, target) in _climbTo.ToList())
            {
                if (!_map.VehicleStates.Contains(vehicle) || vehicle.Lift == target) { _climbTo.Remove(vehicle); continue; }
                int dz = Math.Sign(target - vehicle.Lift);
                if (!LiftVehicle(vehicle, dz)) { _climbTo.Remove(vehicle); StatusText.Text = dz > 0 ? "It can't go any higher here." : "It can't come down any further here."; }
            }
        }
    }

    /// <summary>Everything aboard one lift up or down, if nothing that isn't aboard is in the way.</summary>
    private bool LiftVehicle(VehicleState vehicle, int dz)
    {
        var aboard = Gather(vehicle);
        var mine = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var o in aboard.Objects) mine.Add(o);
        foreach (var i in aboard.Items) mine.Add(i);
        foreach (var c in aboard.Characters) mine.Add(c.Placement);
        var foot = FootprintOf(vehicle);
        int newBottom = vehicle.Lift + dz, newTop = aboard.TopLift + dz;
        foreach (var body in WorldNow().Overlapping(foot))
        {
            if (!body.Solid || mine.Contains(body.Entity) || !body.Overlaps(foot)) continue;
            if (dz > 0 && body.Z < newTop && body.TopLift > aboard.TopLift - 1 && body.Z >= vehicle.Lift) return false; // a roof, a bridge overhead
            if (dz < 0 && body.TopLift > newBottom && body.Z < vehicle.Lift) return false; // the ground, a roof below
        }
        if (dz < 0 && newBottom < 0) return false;
        vehicle.Lift = newBottom;
        foreach (var o in aboard.Objects) { o.Z += dz; RedrawPlaced(null, o); }
        foreach (var i in aboard.Items) { i.Z += dz; RedrawPlaced(i, null); }
        foreach (var mc in aboard.Characters)
        {
            mc.Lift += dz;
            if (_seats.TryGetValue(mc.Character, out var seat)) _seats[mc.Character] = seat with { FromLift = seat.FromLift + dz };
            PositionCharacterVisual(mc);
        }
        if (aboard.Player)
        {
            _playerZ += dz;
            _playerVisualLiftZ = _playerZ;
            if (_player is not null && _seats.TryGetValue(_player, out var seat)) _seats[_player] = seat with { FromLift = seat.FromLift + dz };
            SnapPlayerPosition();
            UpdateCamera();
        }
        WorldChanged();
        return true;
    }
}
