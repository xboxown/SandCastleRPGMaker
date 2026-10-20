using System.Runtime.CompilerServices;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Land, sea and air characters (Character Editor > Moves on):
///  - Land: walks by Exult's walking rule (a lift up or down a step).
///  - Sea: swims, only where the ground's tile is Water (Tile Properties) -
///    it never leaves the water.
///  - Air: flies. Each cell's open air is cut into free bands of lifts
///    (from the ground to a ceiling, from a roof to the sky). A flier goes
///    from one cell to the next where their bands overlap (there's room for
///    its height), and climbs or drops inside its own cell's band. Under the
///    open sky it keeps to "floor 10" - above every floor, out of reach of
///    any blow (missiles and spells still reach it); indoors it flies under
///    the ceiling. To get into a house it comes down outside a door (floor 1,
///    or a door on floor 4) and flies in through it.
///  - Knocked out, asleep, paralyzed or killed, a flier falls onto whatever
///    is under it.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>"Floor 10": just above the highest floor.</summary>
    private static int FlyLift => MapFloors.MaxFloors * MapFloors.LiftsPerFloor;
    /// <summary>How high the sky goes, for the top band.</summary>
    private static int SkyLift => FlyLift + 20;

    private readonly Dictionary<PlacedCharacter, MovementKind> _movementOf = new(ReferenceEqualityComparer.Instance);
    /// <summary>A flier's path: the lift to be at on entering each of its cells.</summary>
    private readonly ConditionalWeakTable<List<(int X, int Y)>, List<int>> _flyPlans = new();

    /// <summary>How the character taking a step moves (the player: walks).</summary>
    private MovementKind MovementOf(object? mover)
    {
        if (mover is not PlacedCharacter placed) return MovementKind.Land;
        if (_movementOf.TryGetValue(placed, out var known)) return known;
        var kind = _mapCharacters.FirstOrDefault(mc => ReferenceEquals(mc.Placement, placed))?.Character.Movement ?? MovementKind.Land;
        _movementOf[placed] = kind;
        return kind;
    }

    private bool IsFlier(MapCharacter mapCharacter) => mapCharacter.Character.Movement == MovementKind.Air;

    /// <summary>Every unit under the footprint is a Water tile.</summary>
    private bool AllWaterUnder(UnitRect foot)
    {
        for (int y = foot.Top; y < foot.Bottom; y++)
            for (int x = foot.Left; x < foot.Right; x++)
            {
                int tileIndex = _map.Layers[0].GetTile(x / UnitsPerTileX, y / UnitsPerTileY, _map.Width);
                if (tileIndex < 0 || !TilePropertiesOf(tileIndex).IsWater) return false;
            }
        return true;
    }

    /// <summary>The free bands of lifts over a footprint: [Low, High) with nothing solid in them, ground to sky.</summary>
    private List<(int Low, int High)> FreeBands(UnitRect foot, object? mover, BodySource? world)
    {
        var bands = new List<(int, int)>();
        int cursor = 0;
        foreach (var body in SolidsOverlapping(foot, mover, world).OrderBy(b => b.Z))
        {
            if (body.Z > cursor) bands.Add((cursor, body.Z));
            cursor = Math.Max(cursor, body.TopLift);
        }
        if (cursor < SkyLift) bands.Add((cursor, SkyLift));
        return bands;
    }

    /// <summary>A flier's step: room for its whole height at the lift it flies at, on the new footprint - nothing else (no climbing rule, no ground).</summary>
    private bool TryFlyStep(UnitRect to, int lift, int height, object? mover, BodySource? world, out int newLift, out string blockReason)
    {
        newLift = lift;
        blockReason = "";
        if (SplitAtSeam(to) is { } pieces) // over the seam of a map that wraps around: room on both sides
        {
            foreach (var piece in pieces) if (!TryFlyStep(piece, lift, height, mover, world, out _, out blockReason)) return false;
            return true;
        }
        if (to.Left < 0 || to.Top < 0 || to.Right > _map.Width * UnitsPerTileX || to.Bottom > _map.Height * UnitsPerTileY) { blockReason = "Blocked: edge of the map."; return false; }
        if (FreeBands(to, mover, world).Any(b => lift >= b.Low && lift + height <= b.High)) return true;
        blockReason = "Blocked: no room to fly there.";
        return false;
    }

    /// <summary>The highest it can fly over where it is now, up to floor 10 (under a ceiling: just under it).</summary>
    private int CruiseLift(MapCharacter mapCharacter)
    {
        int h = mapCharacter.Size.Z;
        var band = FreeBands(CharacterFootprint(mapCharacter), mapCharacter.Placement, null).FirstOrDefault(b => mapCharacter.Lift >= b.Low && mapCharacter.Lift < b.High);
        if (band.High <= band.Low) return mapCharacter.Lift;
        return Math.Clamp(FlyLift, band.Low, Math.Max(band.Low, band.High - h));
    }

    /// <summary>The top of whatever solid thing is under it (a roof, a table, the ground).</summary>
    private int GroundBelow(MapCharacter mapCharacter) =>
        FloorBelow(SolidsOverlapping(CharacterFootprint(mapCharacter), mapCharacter.Placement, null).ToList(), mapCharacter.Lift);

    /// <summary>A flier that can't fly (knocked out, asleep, paralyzed) falls onto what's under it.</summary>
    private void FallIfGrounded(MapCharacter mapCharacter)
    {
        if (!IsFlier(mapCharacter) || !IsIncapacitated(mapCharacter.Character)) return;
        int ground = GroundBelow(mapCharacter);
        if (mapCharacter.Lift <= ground) return;
        StopWalking(mapCharacter);
        mapCharacter.Lift = ground;
        PositionCharacterVisual(mapCharacter);
    }

    /// <summary>Climbs or drops a lift at a time (inside its own cell) toward the lift its path wants. True while still on the way.</summary>
    private bool ClimbToward(MapCharacter mapCharacter, int target, double elapsedMs)
    {
        var npc = mapCharacter.Npc;
        npc.ClimbMs += elapsedMs;
        while (npc.ClimbMs >= 25 && mapCharacter.Lift != target)
        {
            npc.ClimbMs -= 25;
            int next = mapCharacter.Lift + Math.Sign(target - mapCharacter.Lift);
            if (!TryFlyStep(CharacterFootprint(mapCharacter), next, mapCharacter.Size.Z, mapCharacter.Placement, null, out _, out _)) { npc.ClimbMs = 0; return false; } // can't go further: try the step as it is
            mapCharacter.Lift = next;
            PositionCharacterVisual(mapCharacter);
        }
        if (mapCharacter.Lift == target) { npc.ClimbMs = 0; return false; }
        return true;
    }

    /// <summary>
    /// A flier's way: A* over (cell, band). From a band to the next cell's band
    /// where they overlap by its height; it enters each cell as high as the
    /// overlap lets it (floor 10 under the open sky). The goal cell on its floor
    /// (a band starting on that floor) when one is asked for.
    /// </summary>
    private List<(int X, int Y)>? FindFlyPath(MapCharacter mapCharacter, (int X, int Y) goal, bool allowPartial, bool besideOnly, int? goalFloor)
    {
        int h = mapCharacter.Size.Z;
        int prefer = goalFloor is { } floor ? MapFloors.BaseLift(floor) : FlyLift; // headed for a floor (a foe on the ground): it flies down at that height
        var world = WorldBodiesForPathing(mapCharacter);
        var bandsAt = new Dictionary<(int, int), List<(int Low, int High)>>();
        List<(int Low, int High)> Bands((int X, int Y) cell)
        {
            if (!bandsAt.TryGetValue(cell, out var bands))
                bandsAt[cell] = bands = FreeBands(ActorFootprintAt(CellCenterX(cell.X), CellCenterY(cell.Y), mapCharacter.Size), mapCharacter.Placement, world).Where(b => b.High - b.Low >= h).ToList();
            return bands;
        }
        var startCell = (mapCharacter.Placement.X, mapCharacter.Placement.Y);
        var hereBands = FreeBands(CharacterFootprint(mapCharacter), mapCharacter.Placement, world);
        var startBand = hereBands.FirstOrDefault(b => mapCharacter.Lift >= b.Low && mapCharacter.Lift < b.High);
        if (startBand.High <= startBand.Low) return null;
        var start = (startCell.Item1, startCell.Item2, startBand.Low);
        bool AtGoal((int X, int Y, int Low) node) =>
            (goalFloor is null || MapFloors.FloorOfLift(node.Low) == goalFloor)
            && (besideOnly ? Heuristic((node.X, node.Y), goal) == 1 : (node.X, node.Y) == goal);
        int H((int X, int Y, int Low) node) => Heuristic((node.X, node.Y), goal);

        var open = new PriorityQueue<(int X, int Y, int Low), int>();
        var cameFrom = new Dictionary<(int, int, int), (int, int, int)>();
        var enterLift = new Dictionary<(int, int, int), int> { [start] = mapCharacter.Lift };
        var cost = new Dictionary<(int, int, int), int> { [start] = 0 };
        open.Enqueue(start, H(start));
        var nearest = start;
        int nearestH = H(start), expansions = 0;
        List<(int X, int Y)> PathTo((int X, int Y, int Low) end)
        {
            var cells = new List<(int X, int Y)>();
            var lifts = new List<int>();
            for (var step = end; step != start; step = cameFrom[step]) { cells.Add((step.X, step.Y)); lifts.Add(enterLift[step]); }
            cells.Reverse();
            lifts.Reverse();
            _flyPlans.AddOrUpdate(cells, lifts);
            return cells;
        }
        while (open.Count > 0 && expansions++ < 6000)
        {
            var current = open.Dequeue();
            int hh = H(current);
            if (hh < nearestH) { nearest = current; nearestH = hh; }
            if (current != start && AtGoal(current)) return PathTo(current);
            var band = (current.Item1, current.Item2) == startCell && current.Item3 == startBand.Low ? startBand : Bands((current.Item1, current.Item2)).FirstOrDefault(b => b.Low == current.Item3);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var cell = WrapCell((current.Item1 + dx, current.Item2 + dy)); // over the seam of a map that wraps around too
                if (!InsideMap(cell.Item1, cell.Item2)) continue;
                foreach (var next in Bands(cell))
                {
                    int low = Math.Max(band.Low, next.Low), high = Math.Min(band.High, next.High);
                    if (high - low < h) continue; // no room to get across
                    var node = (cell.Item1, cell.Item2, next.Low);
                    int newCost = cost[current] + 1;
                    if (cost.TryGetValue(node, out int known) && known <= newCost) continue;
                    cost[node] = newCost;
                    cameFrom[node] = current;
                    enterLift[node] = Math.Clamp(prefer, low, high - h); // as high as it can go across - floor 10 in the open (or down at a floor it's headed for)
                    open.Enqueue(node, newCost + H(node));
                }
            }
        }
        if (!allowPartial || nearest == start) return null;
        return PathTo(nearest);
    }

    /// <summary>The lift a flier's path wants on entering its next cell, if it has one.</summary>
    private int? PlannedFlyLift(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        return npc.Path is not null && _flyPlans.TryGetValue(npc.Path, out var lifts) && npc.PathIndex < lifts.Count ? lifts[npc.PathIndex] : null;
    }
}
