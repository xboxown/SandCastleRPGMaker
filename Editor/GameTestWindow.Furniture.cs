using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Chairs and beds, ported from Exult (schedule.cc Sit_schedule /
/// Sit_actor_action, Sleep_schedule; intrinsics.cc sit_down):
///  - A chair or bed has a look per facing, each with its seats / sleeping
///    places (Object Editor). Someone sitting faces the chair's facing (Exult:
///    the chair's frame gives the sitter's direction), in the Sit pose.
///  - Double-click a chair: the main character sits in the free seat nearest
///    them (the chair's usecode calls sit_down). Double-click a bed: lies down
///    on the free place nearest them. Moving gets up again.
///  - A seat or place taken by someone is occupied (is_occupied).
///  - The Sit schedule: the NPC finds the nearest free chair it can reach,
///    walks to it and sits; none free: tries again in 1-2 seconds.
///  - The Sleep schedule: the nearest free bed within 24 tiles; the NPC
///    walks to it, lies on it, and the bed's cover is turned down over it
///    (frame 2); getting up makes the bed again (frame 1). No bed: it lies
///    down where it is (the floor).
///  - A bed's cover is its own object lying on the bed (spawned at the start
///    if the bed has one); taken off, the bed has no cover.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Who is sitting or lying where: the furniture, the spot, and where they stood before, to get up to.</summary>
    private sealed record Seat(ObjectInstance Furniture, int Spot, bool Bed, double FromX, double FromY, int FromLift);

    private readonly Dictionary<CharacterDefinition, Seat> _seats = new();
    /// <summary>NPCs on their way to a seat or bed (Sit / Sleep schedules).</summary>
    private readonly Dictionary<CharacterDefinition, (ObjectInstance Furniture, int Spot)> _headingToSeat = new();

    private bool PlayerSeated => _player is not null && _seats.ContainsKey(_player);

    // ---------- Spots ----------

    /// <summary>The seats / sleeping places for its facing - or, with none set, one in the middle of its picture (as Exult's chairs have one sitting place each).</summary>
    private List<SeatSpot> SpotsOf(ObjectInstance furniture, WorldObject obj)
    {
        if (obj.Furniture.Resolve(furniture.Facing) is { Spots.Count: > 0 } facing) return facing.Spots;
        var sprite = ObjectLooks.SpriteFor(obj, StateOf(furniture), _sprites);
        var frame = sprite is { Frames.Count: > 0 } ? LoadStaticFrame(sprite.Frames[0].FilePath) : null;
        // Feet on the floor at the middle of the chair (its picture's bottom) - the Sit pose's art does the sitting, as Exult's sit frame.
        return frame is null ? new List<SeatSpot>() : new List<SeatSpot> { new() { X = frame.PixelWidth / 2.0, Y = frame.PixelHeight } };
    }

    private bool IsSpotTaken(ObjectInstance furniture, int spot, CharacterDefinition? except = null) =>
        _seats.Any(kv => kv.Key != except && ReferenceEquals(kv.Value.Furniture, furniture) && kv.Value.Spot == spot)
        || _headingToSeat.Any(kv => kv.Key != except && ReferenceEquals(kv.Value.Furniture, furniture) && kv.Value.Spot == spot);

    /// <summary>Where someone sitting or lying in this spot has their feet, in world pixels (without the lift shift), and the lift.</summary>
    private (double X, double Y, int Lift)? SpotPosition(ObjectInstance furniture, WorldObject obj, int spot)
    {
        var spots = SpotsOf(furniture, obj);
        if (spot < 0 || spot >= spots.Count || !_objectVisuals.TryGetValue(furniture, out var visual) || visual is not FrameworkElement element) return null;
        var sprite = ObjectLooks.SpriteFor(obj, StateOf(furniture), _sprites);
        var frame = sprite is { Frames.Count: > 0 } ? LoadStaticFrame(sprite.Frames[0].FilePath) : null;
        // The picture is drawn fitted into its box (rounded up to 8 pixels), keeping its shape, centred - the spot is on the picture itself.
        double fw = frame?.PixelWidth ?? 0, fh = frame?.PixelHeight ?? 0;
        double scale = fw <= 0 || fh <= 0 ? 1 : Math.Min(element.Width / fw, element.Height / fh);
        double insetX = fw <= 0 ? 0 : (element.Width - fw * scale) / 2, insetY = fh <= 0 ? 0 : (element.Height - fh * scale) / 2;
        double left = Canvas.GetLeft(element) + furniture.Z * LiftPixelShift + insetX, top = Canvas.GetTop(element) + furniture.Z * LiftPixelShift + insetY;
        return (left + spots[spot].X * scale, top + spots[spot].Y * scale, furniture.Z);
    }

    /// <summary>The free spot nearest a point, or -1.</summary>
    private int NearestFreeSpot(ObjectInstance furniture, WorldObject obj, double x, double y, CharacterDefinition? who)
    {
        int best = -1;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < SpotsOf(furniture, obj).Count; i++)
        {
            if (IsSpotTaken(furniture, i, who) || SpotPosition(furniture, obj, i) is not { } p) continue;
            double d = Math.Abs(p.X - x) + Math.Abs(p.Y - y);
            if (d < bestDistance) { bestDistance = d; best = i; }
        }
        return best;
    }

    // ---------- The main character ----------

    /// <summary>Double-clicking a chair (sit_down / Sit_schedule): walk to the free seat nearest you, then sit.</summary>
    private void SitOn(ObjectInstance chair, WorldObject obj) => ApproachAndSeat(chair, obj, bed: false);

    /// <summary>Double-clicking a bed: walk to the free place nearest you, then lie down.</summary>
    private void LieOn(ObjectInstance bed, WorldObject obj) => ApproachAndSeat(bed, obj, bed: true);

    /// <summary>A walk-to-sit in progress: after arriving in front of the chair, looking at it, then turning to sit.</summary>
    private sealed class PendingSit
    {
        public required ObjectInstance Furniture;
        public required WorldObject Obj;
        public required bool Bed;
        /// <summary>0 = walking there, 3 = the last steps up to the chair, 1 = looking at it, 2 = turned the chair's way, about to sit.</summary>
        public int Phase;
        public double WaitMs;
        /// <summary>Where the last steps go: right in front of the seat (world pixels, the player's own position convention).</summary>
        public double TargetX, TargetY;
    }

    private PendingSit? _pendingSit;

    /// <summary>The one-step offset in the direction something faces (cells).</summary>
    private static (int X, int Y) FacingStep(SpriteDirection facing) => facing switch
    {
        SpriteDirection.North => (0, -1), SpriteDirection.NorthEast => (1, -1), SpriteDirection.East => (1, 0), SpriteDirection.SouthEast => (1, 1),
        SpriteDirection.South => (0, 1), SpriteDirection.SouthWest => (-1, 1), SpriteDirection.West => (-1, 0), _ => (-1, -1)
    };

    /// <summary>
    /// Exult set_action_sequence + Sit_actor_action, for the main character: walk to the spot just in front of the
    /// seat (on the side the chair faces), look at the chair for a moment, turn the way the chair faces, then sit
    /// with feet on the floor at the seat - the Sit pose's art does the rest, as Ultima VII's sit frame does.
    /// </summary>
    private void ApproachAndSeat(ObjectInstance furniture, WorldObject obj, bool bed)
    {
        if (_player is null) return;
        if (PlayerSeated) StandUp(_player);
        int spot = NearestFreeSpot(furniture, obj, _playerPixelX, _playerPixelY + _tileHeight / 2.0, _player);
        if (spot < 0 || SpotPosition(furniture, obj, spot) is not { } p) { StatusText.Text = $"There's no room on \"{obj.Name}\"."; return; }
        var seatCell = ((int)Math.Floor(p.X / _tileWidth), (int)Math.Floor((p.Y - 1) / _tileHeight));
        _pendingSit = new PendingSit
        {
            Furniture = furniture, Obj = obj, Bed = bed,
            // The last steps go up to the seat itself, from whichever side of it can be reached (not always its front: a chair at a table faces the table).
            TargetX = p.X,
            TargetY = p.Y - _tileHeight / 2.0
        };
        if (Math.Abs(_playerX - seatCell.Item1) + Math.Abs(_playerY - seatCell.Item2) <= 1) { BeginLastSteps(); return; }
        if (!WalkPlayerTo(seatCell, BeginLastSteps, besideOnly: true)) // a cell right beside it, walked round whatever is in the way
        {
            _pendingSit = null;
            StatusText.Text = $"You can't get to \"{obj.Name}\" from here.";
            return;
        }
        StatusText.Text = bed ? $"Going to \"{obj.Name}\"..." : $"Going to sit on \"{obj.Name}\"...";
    }

    /// <summary>Arrived a cell in front of it: walk on, straight up to the chair (until it's right there, or the chair stops us).</summary>
    private void BeginLastSteps()
    {
        if (_pendingSit is not { } pending) return;
        pending.Phase = 3;
    }

    /// <summary>One frame of the last steps: toward the spot in front of the seat, one axis at a time; there (or blocked by the chair) - look at it.</summary>
    private void StepUpToSeat(PendingSit pending, double elapsedMs)
    {
        double rx = pending.TargetX - _playerPixelX, ry = pending.TargetY - _playerPixelY;
        bool alongX = Math.Abs(rx) > 0.5;
        if (!alongX && Math.Abs(ry) <= 0.5) { BeginLookingAtSeat(); return; }
        double remaining = Math.Abs(alongX ? rx : ry);
        double step = Math.Min(elapsedMs / 1000.0, remaining / (PlayerTilesPerSecond * _tileWidth));
        if (step <= 0) return;
        double beforeX = _playerPixelX, beforeY = _playerPixelY;
        StepPlayerContinuous(alongX ? Math.Sign(rx) : 0, alongX ? 0 : Math.Sign(ry), step);
        if (_playerPixelX != beforeX || _playerPixelY != beforeY) return;
        // Stopped by the chair itself (or something beside it): close enough - try the other axis once, else look.
        if (alongX && Math.Abs(ry) > 0.5) { pending.TargetX = _playerPixelX; return; }
        BeginLookingAtSeat();
    }

    /// <summary>Arrived in front of it: face the chair and look at it for a moment.</summary>
    private void BeginLookingAtSeat()
    {
        if (_pendingSit is not { } pending || !_map.Objects.Contains(pending.Furniture)) { _pendingSit = null; return; }
        var furnitureVisual = VisualOf(pending.Furniture) as FrameworkElement;
        double cx = furnitureVisual is null ? pending.Furniture.X * _tileWidth + _tileWidth / 2.0 : Canvas.GetLeft(furnitureVisual) + furnitureVisual.Width / 2;
        double cy = furnitureVisual is null ? pending.Furniture.Y * _tileHeight + _tileHeight / 2.0 : Canvas.GetTop(furnitureVisual) + furnitureVisual.Height;
        _playerFacing = FacingTowards(_playerPixelX, _playerPixelY + _tileHeight / 2.0, cx, cy);
        ApplyPoseSprite(_idlePose);
        pending.Phase = 1;
        pending.WaitMs = 1000;
    }

    /// <summary>Each frame of a walk-to-sit: the look, the turn, then sitting down.</summary>
    private void UpdatePendingSit(double elapsedMs)
    {
        if (_pendingSit is not { Phase: > 0 } pending) return;
        if (pending.Phase == 3) { StepUpToSeat(pending, elapsedMs); return; }
        if ((pending.WaitMs -= elapsedMs) > 0) return;
        if (pending.Phase == 1)
        {
            _playerFacing = pending.Furniture.Facing; // turn the way the chair faces
            ApplyPoseSprite(_idlePose);
            pending.Phase = 2;
            pending.WaitMs = 300;
            return;
        }
        _pendingSit = null;
        if (!_map.Objects.Contains(pending.Furniture)) return;
        // Not actually beside it (the way was blocked at the last): no jumping onto it from across a table.
        if (Math.Abs(_playerPixelX - pending.TargetX) > _tileWidth * 1.5 || Math.Abs(_playerPixelY - pending.TargetY) > _tileHeight * 1.5)
        {
            StatusText.Text = $"You can't reach \"{pending.Obj.Name}\" from here.";
            return;
        }
        TakeSeatAsPlayer(pending.Furniture, pending.Obj, pending.Bed);
    }

    private void CancelPendingSit() => _pendingSit = null;

    private void TakeSeatAsPlayer(ObjectInstance furniture, WorldObject obj, bool bed)
    {
        if (_player is null) return;
        if (PlayerSeated) StandUp(_player);
        if (SpotsOf(furniture, obj).Count == 0) { StatusText.Text = $"\"{obj.Name}\" has no {(bed ? "place to lie" : "seat")} set for this facing (Object Editor)."; return; }
        int spot = NearestFreeSpot(furniture, obj, _playerPixelX, _playerPixelY + _tileHeight / 2.0, _player);
        if (spot < 0 || SpotPosition(furniture, obj, spot) is not { } p) { StatusText.Text = $"There's no room on \"{obj.Name}\"."; return; }
        _seats[_player] = new Seat(furniture, spot, bed, _playerPixelX, _playerPixelY, _playerZ);
        _heldDirectionKeys.Clear();
        _playerPixelX = p.X;
        _playerPixelY = p.Y - _tileHeight / 2.0;
        _playerX = (int)Math.Floor(_playerPixelX / _tileWidth);
        _playerY = (int)Math.Floor(_playerPixelY / _tileHeight);
        _playerZ = p.Lift;
        _playerVisualLiftZ = _playerZ;
        _playerFacing = obj.Furniture.ResolveDirection(furniture.Facing) ?? furniture.Facing; // Exult: the sitter faces the chair's way - the facing whose look and spots are used
        ApplyPoseSprite(PoseByName(bed ? "Sleeping" : "Sit") ?? _idlePose);
        PlaySound(obj.Furniture.SitSoundResourceId);
        if (bed) SetCover(furniture, obj, turnedDown: true);
        StatusText.Text = bed ? $"You lie down on \"{obj.Name}\". Move to get up." : $"You sit on \"{obj.Name}\". Move to get up.";
        FixFurnitureDrawOrder();
        FireObjectEvent(furniture, obj, BookTriggerConditionBased.ByEnter);
        if (bed) AskHowLongToSleep(obj); // the slider: how many hours (Exult's rest)
    }

    // ---------- NPCs (Sit and Sleep schedules) ----------

    /// <summary>Sit_schedule / Sleep_schedule: the nearest free chair (any distance) or bed (within 24 tiles) the NPC can walk to - heading there. False if none.</summary>
    /// <param name="maxTiles">How far to look (a chair otherwise any distance); a party member sitting with the main character: 24 tiles (Exult find_closest).</param>
    private bool GoToFurniture(MapCharacter mapCharacter, bool bed, int? maxTiles = null)
    {
        var me = mapCharacter.Character;
        if (!me.IsHuman) return false; // a rat doesn't take a chair or a bed: it stands, or sleeps where it is
        int range = maxTiles is { } tiles ? CellsForTiles(tiles) : bed ? CellsForTiles(24) : int.MaxValue;
        var candidates = _map.Objects
            .Where(o => o.ContainerInstanceId < 0 && DefinitionOf(o)?.Type == (bed ? ObjectType.Bed : ObjectType.Chair))
            .Where(o => Math.Max(Math.Abs(o.X - mapCharacter.Placement.X), Math.Abs(o.Y - mapCharacter.Placement.Y)) <= range)
            .OrderBy(o => Math.Abs(o.X - mapCharacter.Placement.X) + Math.Abs(o.Y - mapCharacter.Placement.Y));
        foreach (var furniture in candidates)
        {
            var obj = DefinitionOf(furniture)!;
            int spot = NearestFreeSpot(furniture, obj, mapCharacter.PixelX, mapCharacter.PixelY, me);
            if (spot < 0 || SpotPosition(furniture, obj, spot) is not { } p) continue;
            var goal = ((int)Math.Floor(p.X / _tileWidth), (int)Math.Floor((p.Y - 1) / _tileHeight));
            var here = (mapCharacter.Placement.X, mapCharacter.Placement.Y);
            var path = Math.Abs(here.Item1 - goal.Item1) + Math.Abs(here.Item2 - goal.Item2) <= 1 ? new List<(int X, int Y)>() : FindPath(mapCharacter, goal, besideOnly: true); // beside it, walked round the table
            if (path is null) continue; // not reachable: the next one
            _headingToSeat[me] = (furniture, spot);
            if (path.Count == 0) { ArriveAtFurniture(mapCharacter); return true; }
            StartPath(mapCharacter, path);
            return true;
        }
        return false;
    }

    /// <summary>Reached the chair or bed it was heading for: sits or lies down (if the spot is still free).</summary>
    private bool ArriveAtFurniture(MapCharacter mapCharacter)
    {
        var me = mapCharacter.Character;
        if (!_headingToSeat.Remove(me, out var target)) return false;
        if (DefinitionOf(target.Furniture) is not { } obj || !_map.Objects.Contains(target.Furniture)
            || IsSpotTaken(target.Furniture, target.Spot, me) || SpotPosition(target.Furniture, obj, target.Spot) is not { } p)
        {
            mapCharacter.Npc.WaitMs = 1000 + Rng.Next(1000); // taken meanwhile: look again shortly
            return true;
        }
        bool bed = obj.Type == ObjectType.Bed;
        _seats[me] = new Seat(target.Furniture, target.Spot, bed, mapCharacter.PixelX, mapCharacter.PixelY, mapCharacter.Lift);
        mapCharacter.PixelX = p.X;
        mapCharacter.PixelY = p.Y - _tileHeight / 2.0;
        mapCharacter.Placement.X = (int)Math.Floor(mapCharacter.PixelX / _tileWidth);
        mapCharacter.Placement.Y = (int)Math.Floor(mapCharacter.PixelY / _tileHeight);
        mapCharacter.Lift = p.Lift;
        SetCharacterPose(mapCharacter, bed ? "Sleeping" : "Sit", obj.Furniture.ResolveDirection(target.Furniture.Facing) ?? target.Furniture.Facing); // the facing whose look and spots are used
        PositionCharacterVisual(mapCharacter);
        if (bed) SetCover(target.Furniture, obj, turnedDown: true);
        if (bed) LockHouseDoorsForNight(mapCharacter); // its own doors locked for the night
        FixFurnitureDrawOrder();
        FireObjectEvent(target.Furniture, obj, BookTriggerConditionBased.ByEnter, mapCharacter);
        return true;
    }

    /// <summary>Gets up from a chair or bed: back to where they stood (or the nearest free cell), standing; a bed is made again.</summary>
    private void StandUp(CharacterDefinition who)
    {
        _headingToSeat.Remove(who);
        if (!_seats.Remove(who, out var seat)) return;
        SyncChairBacks(); // the chair's back goes at once - nobody sits under it any more
        var obj = DefinitionOf(seat.Furniture);
        if (seat.Bed && obj is not null && !_seats.Values.Any(s => ReferenceEquals(s.Furniture, seat.Furniture))) SetCover(seat.Furniture, obj, turnedDown: false);
        if (obj is { Type: ObjectType.Bedroll } && !_seats.Values.Any(s => ReferenceEquals(s.Furniture, seat.Furniture)) && _map.Objects.Contains(seat.Furniture))
        {
            seat.Furniture.IsOpenRuntime = false; // rolled up again
            RefreshObjectLook(seat.Furniture);
        }
        if (who == _player)
        {
            _playerPixelX = seat.FromX;
            _playerPixelY = seat.FromY;
            _playerX = (int)Math.Floor(_playerPixelX / _tileWidth);
            _playerY = (int)Math.Floor(_playerPixelY / _tileHeight);
            _playerZ = seat.FromLift;
            _playerVisualLiftZ = _playerZ;
            ApplyPoseSprite(_idlePose);
            UpdateCamera();
        }
        else if (MapCharacterOf(who) is { } mapCharacter)
        {
            mapCharacter.PixelX = seat.FromX;
            mapCharacter.PixelY = seat.FromY;
            mapCharacter.Placement.X = (int)Math.Floor(seat.FromX / _tileWidth);
            mapCharacter.Placement.Y = (int)Math.Floor(seat.FromY / _tileHeight);
            mapCharacter.Lift = seat.FromLift;
            SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
            PositionCharacterVisual(mapCharacter);
        }
        if (obj is not null) FireObjectEvent(seat.Furniture, obj, BookTriggerConditionBased.ByExit, MapCharacterOf(who));
    }

    // ---------- Bed covers ----------

    /// <summary>At the start of play: a bed with a cover gets it laid on it (its own object), unless one is already there.</summary>
    private void SpawnBedCovers()
    {
        foreach (var bed in _map.Objects.Where(o => o.ContainerInstanceId < 0).ToList())
        {
            if (DefinitionOf(bed) is not { Type: ObjectType.Bed } obj || obj.Furniture.CoverObjectId < 0) continue;
            if (_objects.Objects.All(o => o.Id != obj.Furniture.CoverObjectId) || CoverOf(bed, obj) is not null) continue;
            _map.Objects.Add(new ObjectInstance
            {
                ObjectId = obj.Furniture.CoverObjectId, X = bed.X, Y = bed.Y, Z = bed.Z, OffsetX = bed.OffsetX, OffsetY = bed.OffsetY,
                ContainerInstanceId = -1, InstanceId = _map.GetNextInstanceId(), Facing = bed.Facing
            });
        }
    }

    /// <summary>The bed's cover lying on it, if it's still there.</summary>
    private ObjectInstance? CoverOf(ObjectInstance bed, WorldObject obj) =>
        obj.Furniture.CoverObjectId < 0 ? null
            : _map.Objects.FirstOrDefault(o => o.ObjectId == obj.Furniture.CoverObjectId && o.X == bed.X && o.Y == bed.Y && o.ContainerInstanceId < 0);

    /// <summary>Exult Sleep_schedule: the cover turned down over a sleeper (frame 2), or the bed made again (frame 1).</summary>
    private void SetCover(ObjectInstance bed, WorldObject obj, bool turnedDown)
    {
        if (CoverOf(bed, obj) is not { } cover) return;
        cover.FrameRuntime = turnedDown ? 1 : 0;
        RefreshObjectLook(cover);
    }

    /// <summary>Paint order on furniture: whoever sits or lies is drawn over the chair or bed, and a bed's cover over its sleeper.</summary>
    /// <summary>
    /// Someone sitting or lying down is drawn over their chair or bed (and a
    /// bed's cover over them) - whatever their place on the map would say: the
    /// seat or pillow is usually toward the back of the furniture, which on
    /// its own would draw them behind it. Null if they aren't seated.
    /// </summary>
    private int? SeatedZIndex(CharacterDefinition who, int zIndex)
    {
        if (!_seats.TryGetValue(who, out var seat) || VisualOf(seat.Furniture) is not { } furnitureVisual) return null;
        int z = Math.Max(zIndex, Panel.GetZIndex(furnitureVisual) + 1);
        if (_chairBacks.TryGetValue(seat.Furniture, out var back)) Panel.SetZIndex(back, z + 1); // the chair's back over them
        if (seat.Bed && DefinitionOf(seat.Furniture) is { } obj && CoverOf(seat.Furniture, obj) is { } cover && VisualOf(cover) is { } coverVisual)
            Panel.SetZIndex(coverVisual, z + 1);
        return z;
    }

    /// <summary>Occupied chairs' backs (FurnitureFacing.BackSprite): a picture laid exactly over the chair, drawn over its sitter.</summary>
    private readonly Dictionary<ObjectInstance, Image> _chairBacks = new(ReferenceEqualityComparer.Instance);

    /// <summary>The chair's back for its facing, if it has one.</summary>
    private SpriteEntry? ChairBackOf(ObjectInstance chair) =>
        DefinitionOf(chair) is { Type: ObjectType.Chair } obj && obj.Furniture.Resolve(chair.Facing) is { BackSprite.IsSet: true } facing
            ? ObjectLooks.Find(_sprites, facing.BackSprite.SpriteGroup, facing.BackSprite.SpriteName) : null;

    /// <summary>Each occupied chair with a back gets it, laid exactly over the chair; a chair nobody sits in any more loses it.</summary>
    private void SyncChairBacks()
    {
        var occupied = _seats.Values.Where(s => !s.Bed).Select(s => s.Furniture).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var (chair, back) in _chairBacks.ToList())
            if (!occupied.Contains(chair) || ChairBackOf(chair) is null || VisualOf(chair) is null) { GameCanvas.Children.Remove(back); _chairBacks.Remove(chair); }
        foreach (var chair in occupied.Cast<ObjectInstance>())
        {
            if (ChairBackOf(chair) is not { Frames.Count: > 0 } sprite || VisualOf(chair) is not FrameworkElement chairVisual) continue;
            if (!_chairBacks.TryGetValue(chair, out var back))
            {
                back = new Image { Stretch = Stretch.Uniform, IsHitTestVisible = false, Source = LoadStaticFrame(sprite.Frames[0].FilePath) };
                RenderOptions.SetBitmapScalingMode(back, BitmapScalingMode.NearestNeighbor);
                GameCanvas.Children.Add(back);
                _chairBacks[chair] = back;
            }
            (back.Width, back.Height) = (chairVisual.Width, chairVisual.Height);
            Canvas.SetLeft(back, Canvas.GetLeft(chairVisual));
            Canvas.SetTop(back, Canvas.GetTop(chairVisual));
            back.RenderTransform = chairVisual.RenderTransform;
            back.Visibility = chairVisual.Visibility;
            back.Opacity = chairVisual.Opacity;
        }
    }

    private void FixFurnitureDrawOrder()
    {
        SyncChairBacks();
        foreach (var (who, seat) in _seats)
        {
            if (VisualOf(seat.Furniture) is not { } furnitureVisual) continue;
            int z = Panel.GetZIndex(furnitureVisual) + 1;
            UIElement? sitter = who == _player ? _playerImage : MapCharacterOf(who)?.Visual;
            if (sitter is null) continue;
            // The chair under its sitter (whichever way it faces); its back, if it has one, over them.
            Panel.SetZIndex(sitter, Math.Max(Panel.GetZIndex(sitter), z));
            if (_chairBacks.TryGetValue(seat.Furniture, out var back)) Panel.SetZIndex(back, Panel.GetZIndex(sitter) + 1);
            if (seat.Bed && DefinitionOf(seat.Furniture) is { } obj && CoverOf(seat.Furniture, obj) is { } cover && VisualOf(cover) is { } coverVisual)
                Panel.SetZIndex(coverVisual, Panel.GetZIndex(sitter) + 1);
        }
        // Covers on empty beds lie on them.
        foreach (var bed in _map.Objects.Where(o => o.ContainerInstanceId < 0))
        {
            if (DefinitionOf(bed) is not { Type: ObjectType.Bed } obj || CoverOf(bed, obj) is not { } cover) continue;
            if (_seats.Values.Any(s => ReferenceEquals(s.Furniture, bed))) continue;
            if (VisualOf(bed) is { } bedVisual && VisualOf(cover) is { } coverVisual)
                Panel.SetZIndex(coverVisual, Math.Max(Panel.GetZIndex(coverVisual), Panel.GetZIndex(bedVisual) + 1));
        }
    }
}
