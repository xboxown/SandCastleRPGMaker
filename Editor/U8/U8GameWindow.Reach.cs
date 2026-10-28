// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Exult's reach rule - Fast_pathfinder_client::is_grabable (paths.cc),
/// ported line for line, on this engine's own grid (8px world units are
/// Exult's tiles, Z is lifts):
///
/// 1. Anything within 1 tile of the actor (3D distance that accounts for
///    both sizes, Game_object::distance) is always reachable.
/// 2. Otherwise, for reach margins 1..5 (MAX_GRAB_DIST): A* walks a 1-tile
///    point from the actor's nearest edge, using the walking step rule
///    (up or down at most 1 lift, terrain at lift 0), 8 directions, giving
///    up past 2x the straight distance (min 8, max 64 steps), until the
///    actor's footprint is within that margin of the target and within 5
///    lifts of its level ("got to be on same floor").
/// 3. From where that walk ends, a straight 3D line (Zombie path) to the
///    target's nearest edge must not pass through anything solid that
///    can't be moved (movable objects and actors are ignored).
///
/// Used exactly where Exult uses it: double-click (refused with the
/// "blocked" pointer, Game_window::double_clicked), starting a drag (red X,
/// Dragging_info::start), dropping into a container whose outermost owner
/// is out of reach ("out of range", drop_on_gump), and dropping onto the
/// map (red X, drop_at_lift - the spot must be reachable, unless it is
/// lower than the actor and reachable at the actor's own level, so things
/// can fall). Single-click identify has no reach check, as in Exult.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>paths.cc: #define MAX_GRAB_DIST 5</summary>
    private const int MaxGrabDistance = 5;
    private const string CantReachMessage = "You can't reach that from here.";

    /// <summary>A tile coordinate (8px world unit) plus lift - Exult's Tile_coord.</summary>
    private record struct Tile3(int X, int Y, int Z);

    /// <summary>Exult's Block: a 3D box - footprint origin (top-left unit), lift, footprint size, height.</summary>
    private readonly record struct Volume(int X, int Y, int Z, int W, int D, int H)
    {
        public bool Has(Tile3 t) => t.X >= X && t.X < X + W && t.Y >= Y && t.Y < Y + D && t.Z >= Z && t.Z < Z + H;
    }

    /// <summary>Exult's object tile is the lower-right corner of its footprint, at its lift.</summary>
    private static Tile3 AnchorOf(Volume v) => new(v.X + v.W - 1, v.Y + v.D - 1, v.Z);
    private static Volume VolumeOf(Body b) => new(b.Left, b.Top, b.Z, b.Width, b.Depth, b.Height);

    private Volume PlayerVolume()
    {
        var foot = PlayerFootprint();
        return new Volume(foot.Left, foot.Top, _playerZ, foot.Width, foot.Depth, _playerSize.Z);
    }

    /// <summary>Exult is_dragable: whether a solid thing can be moved out of the way. Objects say so with Portable; books and containers can always be picked up here.</summary>
    private bool IsMovable(object entity) => entity switch
    {
        ObjectInstance obj => ObjectDef(obj.ObjectId)?.IsPortable ?? true,
        FloorSlab => false, // a floor overhead: you can't reach through it
        _ => true
    };

    /// <summary>Is this map thing within the player's reach?</summary>
    private bool IsReachable(object entity)
    {
        var body = WorldBodies().FirstOrDefault(b => ReferenceEquals(b.Entity, entity));
        if (body.Entity is null) return true;
        var volume = VolumeOf(body);
        // Staying where it's put (a potion on a cabinet, against a wall): what it's stuck in or on doesn't stand in the way of reaching it.
        return IsGrabable(volume, isSpot: false, stuckIn: StaysPut(entity) ? volume : null);
    }

    /// <summary>
    /// A container's outermost owner (Game_object::get_outermost) must be in
    /// reach to take from or put into it. A container sitting on the map is
    /// its own outermost owner - unless it is inside another container, whose
    /// owner is checked instead; anything carried by a character is owned by
    /// that character, who is never out of their own reach.
    /// </summary>
    private bool IsContainerOwnerReachable(IHasContainerContents container)
    {
        var outermost = OutermostOnMap(container);
        return outermost is null || IsReachable(outermost);
    }

    /// <summary>The map item that ultimately holds this container, or null if it's carried by a character.</summary>
    private PlacedItemInstance? OutermostOnMap(IHasContainerContents container)
    {
        if (container is PlacedItemInstance placed && _map.Items.Contains(placed)) return placed;
        foreach (var mapItem in _map.Items)
            if (mapItem.Category == ReferencedItemCategory.Container && ReferenceEquals(mapItem, container) || ContainsRecursively(mapItem, container))
                return mapItem;
        return null;
    }

    /// <summary>Is this spot (a tile at a lift) within the player's reach?</summary>
    private bool IsSpotReachable(int x, int y, int z) => IsGrabable(new Volume(x, y, z, 1, 1, 1), isSpot: true);

    /// <summary>Fast_pathfinder_client::is_grabable(Actor*, Game_object*) / (Actor*, Tile_coord).</summary>
    private bool IsGrabable(Volume target, bool isSpot, Volume? stuckIn = null)
    {
        var from = PlayerVolume();
        // Game_object::distance(Tile_coord) treats a spot as having no width/height of its own.
        if (ExultDistance(from, isSpot ? target with { W = 1, D = 1, H = 0 } : target) <= 1) return true;

        var query = new ReachQuery(this);
        for (int margin = 1; margin <= MaxGrabDistance; margin++)
        {
            // Fast_pathfinder_client::init: destbox = footprint.enlarge(dist); for a spot, TileRect(tx, ty, 0, 0).enlarge(dist).
            var destBox = isSpot
                ? new UnitRect(target.X - margin, target.Y - margin, 2 * margin, 2 * margin)
                : new UnitRect(target.X - margin, target.Y - margin, target.W + 2 * margin, target.D + 2 * margin);
            if (IsGrabableInternal(from, AnchorOf(target), target, destBox, query, stuckIn)) return true;
        }
        return false;
    }

    /// <summary>Fast_pathfinder_client::is_grabable_internal.</summary>
    private static bool IsGrabableInternal(Volume fromVol, Tile3 targetTile, Volume toVol, UnitRect destBox, ReachQuery query, Volume? stuckIn = null)
    {
        var fromTile = AnchorOf(fromVol);
        var src = fromTile;
        var dst = targetTile;
        ClosestEdge(fromVol, toVol, ref src, ref dst);
        // Not Exult: the walk is made with the actor's whole footprint (below), so it starts from where the actor really stands.
        src = fromTile with { Z = fromVol.Z };

        var path = query.FindPath(src, dst, destBox, fromVol.W, fromVol.D);
        if (path is null) return false;

        Tile3 t;
        if (path.Count == 0)
        {
            t = fromTile;
        }
        else
        {
            t = default;
            foreach (var step in path)
            {
                t = step;
                if (step != fromTile && !query.AtGoal(step, dst, destBox, fromVol.W, fromVol.D) && query.IsOccupied(step)) return false; // blocked
            }
        }
        if (!query.AtGoal(t, dst, destBox, fromVol.W, fromVol.D)) return false;

        var srcVol = fromVol;
        var movedVol = fromVol with { X = fromVol.X + t.X - src.X, Y = fromVol.Y + t.Y - src.Y, Z = fromVol.Z + t.Z - src.Z };
        dst = targetTile;
        ClosestEdge(movedVol, toVol, ref t, ref dst);

        // The straight 3D line from there to the target (Zombie path) must not pass through anything solid that can't be moved.
        var line = ZombieLine(t, dst);
        if (line.Count == 0) return false; // Exult: "if (zpath.get_num_steps() == 0) return false;"
        foreach (var p in line)
        {
            if (toVol.Has(p) || srcVol.Has(p) || movedVol.Has(p)) continue;
            if (query.ReachBlockerAt(p) is { } blocker && !query.Movable(blocker.Entity)
                && !(stuckIn is { } s && blocker.Left < s.X + s.W && s.X < blocker.Right && blocker.Top < s.Y + s.D && s.Y < blocker.Bottom)) return false; // not what a staying thing is stuck in or on
        }
        return true;
    }

    /// <summary>paths.cc Get_closest_edge: move both points to the edges facing each other; pos1 to the top of its volume, pos2 to the top of its volume when going down.</summary>
    private static void ClosestEdge(Volume fromVol, Volume toVol, ref Tile3 pos1, ref Tile3 pos2)
    {
        if (pos2.X < pos1.X) pos1.X = fromVol.X; else pos2.X = toVol.X;   // going left / right
        if (pos2.Y < pos1.Y) pos1.Y = fromVol.Y; else pos2.Y = toVol.Y;   // going north / south
        if (pos2.Z < pos1.Z) pos2.Z += toVol.H - 1;                       // going down
        pos1.Z += fromVol.H - 1;                                          // use top tile
    }

    /// <summary>Game_object::distance with both 3D sizes accounted for (delta_wrap_check / delta_check), then Tile_coord::distance (the larger of the 2D and Z distances).</summary>
    private static int ExultDistance(Volume a, Volume b)
    {
        var t1 = AnchorOf(a);
        var t2 = AnchorOf(b);
        int x1 = t1.X, y1 = t1.Y, z1 = t1.Z, x2 = t2.X, y2 = t2.Y, z2 = t2.Z;
        // An object's tile is its lower-right corner: measure from the nearer edges.
        if (x2 > x1) x2 -= b.W - 1; else if (x2 < x1) x1 -= a.W - 1;
        if (y2 > y1) y2 -= b.D - 1; else if (y2 < y1) y1 -= a.D - 1;
        int dz = z1 - z2;
        if (dz < 0) { if (z1 + a.H > z2) z1 = z2; else z1 += a.H; }
        else if (dz > 0) { if (z2 + b.H > z1) z2 = z1; else z2 += b.H; }
        int planar = Math.Max(Math.Abs(x2 - x1), Math.Abs(y2 - y1));
        return Math.Max(planar, Math.Abs(z2 - z1));
    }

    /// <summary>Tile_coord::distance: the larger of the 2D (chessboard) distance and the Z distance.</summary>
    private static int TileDistance(Tile3 a, Tile3 b) =>
        Math.Max(Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Y - a.Y)), Math.Abs(b.Z - a.Z));

    /// <summary>Zombie.cc: Bresenham-style straight line through 3D tiles, one step along the fastest-changing axis at a time (Z wins ties, then Y), stopping if it would go below ground. Excludes the start.</summary>
    private static List<Tile3> ZombieLine(Tile3 from, Tile3 to)
    {
        var points = new List<Tile3>();
        int deltaX = to.X - from.X, deltaY = to.Y - from.Y, deltaZ = to.Z - from.Z;
        if (deltaX == 0 && deltaY == 0 && deltaZ == 0) return points;
        int absX = Math.Abs(deltaX), absY = Math.Abs(deltaY), absZ = Math.Abs(deltaZ);
        int dirX = deltaX >= 0 ? 1 : -1, dirY = deltaY >= 0 ? 1 : -1, dirZ = deltaZ >= 0 ? 1 : -1;
        int[] cur = { from.X, from.Y, from.Z };
        int major, minor1, minor2, majorDelta, minorDelta1, minorDelta2, majorDir, minorDir1, minorDir2;
        if (absZ >= absX && absZ >= absY) { major = 2; minor1 = 0; minor2 = 1; majorDelta = absZ; minorDelta1 = absX; minorDelta2 = absY; majorDir = dirZ; minorDir1 = dirX; minorDir2 = dirY; }
        else if (absY >= absX && absY >= absZ) { major = 1; minor1 = 0; minor2 = 2; majorDelta = absY; minorDelta1 = absX; minorDelta2 = absZ; majorDir = dirY; minorDir1 = dirX; minorDir2 = dirZ; }
        else { major = 0; minor1 = 1; minor2 = 2; majorDelta = absX; minorDelta1 = absY; minorDelta2 = absZ; majorDir = dirX; minorDir1 = dirY; minorDir2 = dirZ; }

        int sum1 = 0, sum2 = 0;
        for (int remaining = majorDelta; remaining > 0; remaining--)
        {
            sum1 += minorDelta1;
            sum2 += minorDelta2;
            int step1 = sum1 / majorDelta, step2 = sum2 / majorDelta;
            sum1 %= majorDelta;
            sum2 %= majorDelta;
            cur[major] += majorDir;
            cur[minor1] += minorDir1 * step1;
            cur[minor2] += minorDir2 * step2;
            if (cur[2] < 0) break; // below ground level
            points.Add(new Tile3(cur[0], cur[1], cur[2]));
        }
        return points;
    }

    /// <summary>
    /// One reach question's view of the world: the solid things, looked up
    /// per tile once and remembered, so the pathfinder stays fast - plus the
    /// A* search itself (path.cc Find_path with Fast_pathfinder_client).
    /// </summary>
    private sealed class ReachQuery
    {
        private readonly U8GameWindow _window;
        private readonly List<Body> _solids;
        private BodySource? _solidsSource;
        private readonly Dictionary<(int, int), List<Body>> _solidsAt = new();
        private readonly int _mapUnitsWide, _mapUnitsHigh;

        public ReachQuery(U8GameWindow window)
        {
            _window = window;
            // Walls and closed doors reach up to the roof, as Ultima VII's house walls do - nothing is reached over them, however short their drawn box.
            _solids = window.WorldBodies().Where(b => b.Solid)
                .Select(b => b.Entity is ObjectInstance o && window.DefinitionOf(o)?.Type is ObjectType.Wall or ObjectType.Door or ObjectType.Window ? b with { Height = Math.Max(b.Height, 64) } : b)
                .ToList();
            _mapUnitsWide = window._map.Width * window.UnitsPerTileX;
            _mapUnitsHigh = window._map.Height * window.UnitsPerTileY;
        }

        private List<Body> SolidsAt(int x, int y)
        {
            if (!_solidsAt.TryGetValue((x, y), out var list))
            {
                list = _solids.Where(b => x >= b.Left && x < b.Right && y >= b.Top && y < b.Bottom).ToList();
                _solidsAt[(x, y)] = list;
            }
            return list;
        }

        /// <summary>Game_map::is_tile_occupied / Game_object::find_blocking: the solid thing filling this exact tile and lift, if any.</summary>
        public Body? BlockingAt(Tile3 t)
        {
            foreach (var body in SolidsAt(t.X, t.Y))
                if (t.Z >= body.Z && t.Z < body.TopLift) return body;
            return null;
        }

        public bool IsOccupied(Tile3 t) => BlockingAt(t) is not null;

        /// <summary>
        /// The solid thing a reaching arm would hit here. A table never does: you reach over it to
        /// put things on its top, take them off, or reach what's behind it (in Ultima VII a table is
        /// always lower than the avatar's head, so Exult's line passes over it - here a table may be
        /// drawn as tall as the player, and must still be reachable over). You still walk around it.
        /// </summary>
        public Body? ReachBlockerAt(Tile3 t)
        {
            foreach (var body in SolidsAt(t.X, t.Y))
                if (t.Z >= body.Z && t.Z < body.TopLift && !(body.Entity is ObjectInstance o && _window.DefinitionOf(o)?.Type == ObjectType.Table)) return body;
            return null;
        }

        public bool Movable(object entity) => _window.IsMovable(entity);

        /// <summary>Fast_pathfinder_client::at_goal: within 5 lifts of the goal's level, and the actor's footprint (placed with its lower-right at this tile) touching the destination box.</summary>
        public bool AtGoal(Tile3 tile, Tile3 goal, UnitRect destBox, int actorW, int actorD)
        {
            int dz = tile.Z - goal.Z;
            if (dz > 5 || dz < -5) return false;
            int left = tile.X - actorW + 1, top = tile.Y - actorD + 1;
            return left < destBox.Left + destBox.Width && destBox.Left < left + actorW
                && top < destBox.Top + destBox.Depth && destBox.Top < top + actorD;
        }

        /// <summary>
        /// Fast_pathfinder_client::get_step_cost, with one difference from Exult: the step is taken by the actor's whole
        /// footprint (its lower-right at this tile), 1 lift tall, rising or dropping at most 1 lift - not by a one-tile
        /// point. Exult's walls are unbroken lines of tiles, so a point can't slip through them; walls built from pieces
        /// whose solid boxes don't quite meet (a corner) would let a point reach in where nobody could walk. -1 if blocked.
        /// </summary>
        private int StepTo(int x, int y, int z, int actorW, int actorD)
        {
            var foot = new UnitRect(x - actorW + 1, y - actorD + 1, actorW, actorD);
            if (foot.Left < 0 || foot.Top < 0 || foot.Right > _mapUnitsWide || foot.Bottom > _mapUnitsHigh) return -1;
            _solidsSource ??= new BodySource { Index = new BodyIndex(_solids), Extra = new List<Body>() };
            return _window.TryStepActor(foot, z, 1, out int newLift, out _, null, _solidsSource) ? newLift : -1;
        }

        /// <summary>path.cc Find_path (A*) with Fast_pathfinder_client's costs and limits. Returns the steps (not including the start), or null if no path.</summary>
        public List<Tile3>? FindPath(Tile3 start, Tile3 goal, UnitRect destBox, int actorW, int actorD)
        {
            int startEstimate = TileDistance(start, goal);
            // Fast_pathfinder_client::get_max_cost: "Don't try too hard."
            int maxCost = Math.Clamp(2 * startEstimate, 8, 64);

            var open = new PriorityQueue<Tile3, (int Total, long Order)>();
            var startCost = new Dictionary<Tile3, int> { [start] = 0 };
            var parent = new Dictionary<Tile3, Tile3>();
            var closed = new HashSet<Tile3>();
            long order = 0;
            open.Enqueue(start, (startEstimate, order++));

            while (open.TryDequeue(out var current, out _))
            {
                if (!closed.Add(current)) continue; // stale queue entry
                if (AtGoal(current, goal, destBox, actorW, actorD))
                {
                    var path = new List<Tile3>();
                    for (var node = current; node != start; node = parent[node]) path.Add(node);
                    path.Reverse();
                    return path;
                }
                int costHere = startCost[current];
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        if (dx == 0 && dy == 0) continue;
                        int lift = StepTo(current.X + dx, current.Y + dy, current.Z, actorW, actorD);
                        if (lift < 0) continue; // blocked
                        // No cutting a corner: a diagonal step needs both straight steps beside it open.
                        if (dx != 0 && dy != 0 && (StepTo(current.X + dx, current.Y, current.Z, actorW, actorD) < 0 || StepTo(current.X, current.Y + dy, current.Z, actorW, actorD) < 0)) continue;
                        var next = new Tile3(current.X + dx, current.Y + dy, lift);
                        int newCost = costHere + 1;
                        if (startCost.TryGetValue(next, out int known) && known <= newCost) continue;
                        int estimate = TileDistance(next, goal);
                        if (newCost + estimate >= maxCost) continue; // too far away
                        startCost[next] = newCost;
                        parent[next] = current;
                        closed.Remove(next);
                        open.Enqueue(next, (newCost + estimate, order++));
                    }
                }
            }
            return null;
        }
    }
}
