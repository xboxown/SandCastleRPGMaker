using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Doors beyond Ultima VII's swing (Object Editor > Door > How it opens; DoorMotion):
///  - Moving: as a door opens or closes, a picture of it plays its animation, slides or fades where it stands, its real picture
///    hidden till it's done (then that shows: open, or closed). Whether it's open - who can walk through - changes at once, as
///    Exult's doors do; only the look takes its time.
///  - Double doors: opening or closing one opens or closes the double doors right next to it too.
///  - Opening by itself: when anyone comes within its distance it opens, and closes once nobody has been near for its time.
///  - A sliding door isn't opened by hand, nor one set so (a laser door) - by a lever, an event (Set Door State) or by itself (OpensByHand).
/// </summary>
public partial class GameTestWindow
{
    private sealed class DoorMove
    {
        public required ObjectInstance Door;
        public required WorldObject Definition;
        public required Image Picture;
        public required SpriteEntry Sprite;
        public required bool Opening, Backwards;
        public required double DurationMs;
        public double ElapsedMs;
    }

    private readonly Dictionary<ObjectInstance, DoorMove> _doorMoves = new(ReferenceEqualityComparer.Instance);
    private List<ObjectInstance>? _selfOpeningDoors;
    private double _selfOpeningListAgeMs, _selfOpeningCheckMs;
    private readonly Dictionary<ObjectInstance, double> _nobodyNearMs = new(ReferenceEqualityComparer.Instance);
    private bool _openingPartners;

    /// <summary>Opened by hand (double-click, an NPC on its way)? Never a sliding door, nor one set Not by hand (a laser door's button): a lever, an event (Open Door, Set Door State), or opening by itself does it.</summary>
    private static bool OpensByHand(WorldObject door) => door.Door.Opening != DoorOpening.Slide && !door.Door.NotByHand;

    /// <summary>The door's look takes its time to open or close (its real picture already rebuilt for the new state).</summary>
    private void StartDoorMove(ObjectInstance door, WorldObject obj, bool opening)
    {
        EndDoorMove(door);
        if (!DoorMotion.Moves(obj.Door) || DoorMotion.MovingPicture(obj, opening, _sprites) is not { } moving) return;
        var (width, height) = QuantizedSpriteSize(moving.Sprite);
        var picture = new Image { Width = width, Height = height, IsHitTestVisible = false, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
        picture.Source = FrameCache.Get(moving.Sprite.Frames[moving.Backwards ? moving.Sprite.Frames.Count - 1 : 0].FilePath);
        ApplyZToVisual(picture, door.X, door.Y, door.Z, door.OffsetX, door.OffsetY); // where the closed door stands
        if (_objectVisuals.TryGetValue(door, out var real)) Panel.SetZIndex(picture, Panel.GetZIndex(real));
        var move = new DoorMove { Door = door, Definition = obj, Picture = picture, Sprite = moving.Sprite, Opening = opening, Backwards = moving.Backwards, DurationMs = DoorMotion.DurationMs(obj.Door, moving.Sprite) };
        if (obj.Door.Opening != DoorOpening.Animation) DoorMotion.Apply(picture, obj.Door, opening ? 0 : 1);
        GameCanvas.Children.Add(picture);
        _doorMoves[door] = move;
        HideRealDoor(move);
    }

    /// <summary>While it moves, its real picture stays hidden - except a fading door opening onto its open sprite, which shows under it.</summary>
    private void HideRealDoor(DoorMove move)
    {
        if (!_objectVisuals.TryGetValue(move.Door, out var real)) return;
        bool showUnder = move.Opening && move.Definition.Door.Opening == DoorOpening.Fade;
        real.Visibility = showUnder ? Visibility.Visible : Visibility.Hidden;
    }

    private void EndDoorMove(ObjectInstance door)
    {
        if (!_doorMoves.Remove(door, out var move)) return;
        GameCanvas.Children.Remove(move.Picture);
        if (_objectVisuals.TryGetValue(door, out var real)) real.Visibility = Visibility.Visible;
    }

    /// <summary>Each frame: every moving door a step further; done, its real picture shows.</summary>
    private void UpdateDoorMoves(double elapsedMs)
    {
        if (_doorMoves.Count == 0) return;
        foreach (var move in _doorMoves.Values.ToList())
        {
            if (!_map.Objects.Contains(move.Door)) { EndDoorMove(move.Door); continue; }
            move.ElapsedMs += elapsedMs;
            double t = Math.Clamp(move.ElapsedMs / move.DurationMs, 0, 1);
            if (move.Definition.Door.Opening == DoorOpening.Animation)
                move.Picture.Source = FrameCache.Get(move.Sprite.Frames[DoorMotion.FrameAt(move.Sprite, move.ElapsedMs, move.Backwards)].FilePath);
            else
            {
                double eased = DoorMotion.Ease(t);
                DoorMotion.Apply(move.Picture, move.Definition.Door, move.Opening ? eased : 1 - eased);
            }
            HideRealDoor(move); // its picture may have been made again meanwhile
            if (t >= 1) EndDoorMove(move.Door);
        }
    }

    /// <summary>A double door opened or closed: the double doors right next to it (on its floor) follow.</summary>
    private void MoveDoorPartners(ObjectInstance door, WorldObject obj, bool opening, MapCharacter? by)
    {
        if (!obj.Door.OpensWithNeighbours || _openingPartners) return;
        _openingPartners = true;
        try
        {
            int floor = MapFloors.FloorOfLift(door.Z);
            foreach (var other in _map.Objects.ToList())
            {
                if (ReferenceEquals(other, door) || other.ContainerInstanceId >= 0 || DefinitionOf(other) is not { Type: ObjectType.Door, Door.OpensWithNeighbours: true } otherObj) continue;
                if (MapFloors.FloorOfLift(other.Z) != floor || Math.Max(Math.Abs(other.X - door.X), Math.Abs(other.Y - door.Y)) > 1) continue;
                if (opening && !IsDoorOpen(other) && !IsDoorLocked(other)) OpenDoor(other, otherObj, by, quiet: true);
                else if (!opening && IsDoorOpen(other)) TryCloseDoor(other, otherObj, by, quiet: true);
            }
        }
        finally { _openingPartners = false; }
    }

    /// <summary>Doors that open by themselves: open when anyone is near, closed again once nobody has been near for their time.</summary>
    private void UpdateSelfOpeningDoors(double elapsedMs)
    {
        if ((_selfOpeningListAgeMs -= elapsedMs) <= 0 || _selfOpeningDoors is null)
        {
            _selfOpeningListAgeMs = 3000; // objects come and go: the list again now and then
            _selfOpeningDoors = _map.Objects.Where(o => o.ContainerInstanceId < 0 && DefinitionOf(o) is { Type: ObjectType.Door, Door.OpensByItself: true }).ToList();
        }
        if (_selfOpeningDoors.Count == 0) return;
        if ((_selfOpeningCheckMs += elapsedMs) < 250) return; // four times a second is plenty
        double step = _selfOpeningCheckMs;
        _selfOpeningCheckMs = 0;

        var walkers = new List<(int X, int Y, int Floor)>();
        if (_player is not null && !IsDead(_player)) walkers.Add((_playerX, _playerY, MapFloors.FloorOfLift(_playerZ)));
        foreach (var mapCharacter in _mapCharacters)
            if (!IsDead(mapCharacter.Character)) walkers.Add((mapCharacter.Placement.X, mapCharacter.Placement.Y, MapFloors.FloorOfLift(mapCharacter.Lift)));
        foreach (var door in _selfOpeningDoors)
        {
            if (!_map.Objects.Contains(door) || DefinitionOf(door) is not { } obj) continue;
            int floor = MapFloors.FloorOfLift(door.Z), reach = Math.Max(0, obj.Door.OpensWithinCells);
            bool near = walkers.Any(w => w.Floor == floor && Math.Max(Math.Abs(w.X - door.X), Math.Abs(w.Y - door.Y)) <= reach);
            if (near)
            {
                _nobodyNearMs.Remove(door);
                if (!IsDoorOpen(door) && !IsDoorLocked(door)) OpenDoor(door, obj, null, quiet: true);
                continue;
            }
            if (!IsDoorOpen(door)) continue;
            double away = _nobodyNearMs.GetValueOrDefault(door) + step;
            _nobodyNearMs[door] = away;
            if (away >= obj.Door.StaysOpenMs && TryCloseDoor(door, obj, null, quiet: true)) _nobodyNearMs.Remove(door);
        }
    }
}
