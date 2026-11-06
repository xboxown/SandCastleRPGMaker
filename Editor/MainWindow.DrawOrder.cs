using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Map Editor's paint order for the Ultima 7 / Ultima 6 view, the game's own, step for step, so what you see here is
/// what the game draws (before, the editor ordered by a footprint's far corner and the game by its cell, so two walls
/// meeting could swap which one was in front):
///  1. ApplyZToVisual / ComputeDepthZIndex - the cell (X + Y) and the lift;
///  2. ApplyRestingDrawOrder - floor tiles first, tiles flat on their floor, each thing over what it rests on, things
///     put underneath under what's over them;
///  3. ApplyWallDrawOrder - everything near a wall, door or window ordered against it by Exult's compare (Walls.cs),
///     hung things over the walls their pictures are over;
///  4. equal orders as the game's canvas has them: objects, then books and containers, each in map order.
/// </summary>
public partial class MainWindow
{
    /// <summary>Exult's Ordering_info: a thing's tile box (GameTestWindow.Walls.cs).</summary>
    private readonly record struct OrderBox(int XLeft, int XRight, int YFar, int YNear, int ZBot, int ZTop, bool Flat, Rect Screen);

    private static OrderBox OrderBoxOf(EditorBody body, Rect screen) => new(
        body.Left, body.Left + body.Width - 1, body.Top, body.Top + body.Depth - 1,
        body.Height == 0 ? body.Z - 1 : body.Z, body.Z + Math.Max(body.Height, 1) - 1, body.Height == 0, screen);

    private static void CompareRanges(int from1, int to1, int from2, int to2, out int cmp, out bool overlap)
    {
        if (to1 < from2) { overlap = false; cmp = -1; }
        else if (to2 < from1) { overlap = false; cmp = 1; }
        else
        {
            overlap = true;
            cmp = from1 < from2 ? -1 : from1 > from2 ? 1 : to1 < to2 ? 1 : to1 > to2 ? -1 : 0;
        }
    }

    /// <summary>Game_object::compare, as the game has it (GameTestWindow.ExultCompare): -1 drawn before, 1 after, 0 either.</summary>
    private static int ExultCompare(OrderBox a, OrderBox b, bool aOccludes, bool bOccludes)
    {
        if (!a.Screen.IntersectsWith(b.Screen)) return 0;
        CompareRanges(a.XLeft, a.XRight, b.XLeft, b.XRight, out int xcmp, out bool xover);
        CompareRanges(a.YFar, a.YNear, b.YFar, b.YNear, out int ycmp, out bool yover);
        CompareRanges(a.ZBot, a.ZTop, b.ZBot, b.ZTop, out int zcmp, out bool zover);
        if (xcmp == 0 && ycmp == 0 && zcmp == 0)
            return a.Screen.Width < b.Screen.Width && a.Screen.Height < b.Screen.Height ? -1 : a.Screen.Width > b.Screen.Width && a.Screen.Height > b.Screen.Height ? 1 : 0;
        if (xover && yover && zover)
        {
            if (a.Flat) return b.Flat ? 0 : -1;
            if (b.Flat) return 1;
        }
        if (xcmp >= 0 && ycmp >= 0 && zcmp >= 0) return 1;
        if (xcmp <= 0 && ycmp <= 0 && zcmp <= 0) return -1;
        if (yover)
        {
            if (xover) return zcmp;
            if (zover) return xcmp;
            if (zcmp == 0) return xcmp;
            if (xcmp == zcmp) return xcmp;
            if (a.ZTop / 5 < b.ZBot / 5 && bOccludes) return -1;
            if (b.ZTop / 5 < a.ZBot / 5 && aOccludes) return 1;
            return 0;
        }
        if (xover)
        {
            if (zover || zcmp == 0) return ycmp;
            return ycmp == zcmp ? ycmp : 0;
        }
        if (xcmp == -1)
        {
            if (ycmp == -1) return zover || zcmp <= 0 ? -1 : 0;
        }
        else if (ycmp == 1)
        {
            if (zover || zcmp >= 0) return 1;
            if (a.ZTop / 5 < b.ZBot / 5) return -1;
            return 0;
        }
        return 0;
    }

    private static Rect PictureRectOf(FrameworkElement visual) =>
        new(Canvas.GetLeft(visual), Canvas.GetTop(visual), Math.Max(1, visual.Width), Math.Max(1, visual.Height));

    private const double OrderCellPixels = 64;

    private static IEnumerable<(int, int)> PictureCellsOf(Rect screen)
    {
        if (screen.IsEmpty || double.IsNaN(screen.Left) || double.IsNaN(screen.Top)) yield break;
        for (int cy = (int)Math.Floor(screen.Top / OrderCellPixels); cy <= (int)Math.Floor(screen.Bottom / OrderCellPixels); cy++)
            for (int cx = (int)Math.Floor(screen.Left / OrderCellPixels); cx <= (int)Math.Floor(screen.Right / OrderCellPixels); cx++)
                yield return (cx, cy);
    }

    /// <summary>The game's paint order (Ultima 7 / Ultima 6 view) for everything on the picked floor.</summary>
    private void ApplyGameDrawOrderNow()
    {
        if (_map is null) return;
        var definitions = new Dictionary<int, WorldObject>();
        foreach (var d in _objectDatabase.Objects) definitions.TryAdd(d.Id, d);
        WorldObject? Def(object entity) => entity is ObjectInstance o ? definitions.GetValueOrDefault(o.ObjectId) : null;

        // Every thing on the floor with its picture, in the game's list order (books and containers, then objects).
        var things = MapThings().Where(OnCurrentFloor)
            .Select(t => (Body: BodyOf(t), Visual: VisualOfThing(t)))
            .Where(t => t.Visual is not null)
            .Select(t => (t.Body, Visual: t.Visual!, Screen: PictureRectOf(t.Visual!)))
            .ToList();
        var z = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);

        // 1. ComputeDepthZIndex(X + 0.5, Y + 0.5, Z) - its floor's band is the same for all of them.
        foreach (var (body, _, _) in things)
        {
            var (x, y) = body.Entity switch { ObjectInstance o => (o.X, o.Y), PlacedItemInstance i => (i.X, i.Y), _ => (0, 0) };
            int lift = body.Entity switch { ObjectInstance o => o.Z, PlacedItemInstance i => i.Z, _ => 0 };
            z[body.Entity] = (int)Math.Round((x + 0.5 + y + 0.5) * 100) + lift;
        }

        // 2. ApplyRestingDrawOrder: lowest first (stable), each over what it rests on.
        var bodies = things.Select(t => t.Body).OrderBy(b => b.Z).ToList();
        var grid = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < bodies.Count; i++)
            foreach (var square in SquaresOf(bodies[i]))
            {
                if (!grid.TryGetValue(square, out var list)) grid[square] = list = new List<int>(4);
                list.Add(i);
            }
        IEnumerable<EditorBody> Near(EditorBody body)
        {
            var seen = new HashSet<int>();
            foreach (var square in SquaresOf(body))
                if (grid.TryGetValue(square, out var list))
                    foreach (int i in list)
                        if (seen.Add(i)) yield return bodies[i];
        }
        var rested = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        foreach (var body in bodies)
        {
            var def = Def(body.Entity);
            int lift = body.Entity switch { ObjectInstance o => o.Z, _ => body.Z };
            bool upperTile = body.Entity is ObjectInstance && def is { Type: ObjectType.Tile } t && (t.IsSolid || t.IsFloorTile)
                && lift >= MapFloors.BaseLift(2) && lift == MapFloors.BaseLift(MapFloors.FloorOfLift(lift));
            int index = upperTile ? -2 // part of an upper floor: under its floor's band
                : def is { Type: ObjectType.Tile, IsFloorTile: true } ? 0 // part of the floor: painted before everything on it
                : def is { Type: ObjectType.Tile } ? 1 + body.Z % MapFloors.LiftsPerFloor
                : z[body.Entity];
            foreach (var below in Near(body))
            {
                if (ReferenceEquals(below.Entity, body.Entity) || below.TopLift > body.Z || !below.Overlaps(body)) continue;
                if (rested.TryGetValue(below.Entity, out int belowIndex)) index = Math.Max(index, belowIndex + 1);
            }
            rested[body.Entity] = index;
            z[body.Entity] = index;
        }
        foreach (var body in bodies.Where(b => b.Under))
            foreach (var over in Near(body))
                if (!ReferenceEquals(over.Entity, body.Entity) && !over.Under && over.Overlaps(body))
                    z[body.Entity] = Math.Min(z[body.Entity], z[over.Entity] - 1);

        // 3. ApplyWallDrawOrder: walls, doors and windows keep their order; the rest is ordered against those its picture overlaps.
        var walls = things.Where(t => Def(t.Body.Entity)?.Type is ObjectType.Wall or ObjectType.Door or ObjectType.Window)
            .Select(t => (t.Body.Entity, Box: OrderBoxOf(t.Body, t.Screen), t.Screen, Occludes: Def(t.Body.Entity)!.Occludes)).ToList();
        if (walls.Count > 0)
        {
            var wallCells = new Dictionary<(int, int), List<int>>();
            for (int i = 0; i < walls.Count; i++)
                foreach (var cell in PictureCellsOf(walls[i].Screen))
                {
                    if (!wallCells.TryGetValue(cell, out var list)) wallCells[cell] = list = new List<int>(2);
                    list.Add(i);
                }
            IEnumerable<int> WallsNear(Rect screen)
            {
                var seen = new HashSet<int>();
                foreach (var cell in PictureCellsOf(screen))
                    if (wallCells.TryGetValue(cell, out var list))
                        foreach (int i in list)
                            if (seen.Add(i)) yield return i;
            }
            // The game orders its books and containers first, then its objects - each one against the walls as they are.
            foreach (var (body, _, screen) in things.Where(t => t.Body.Entity is PlacedItemInstance).Concat(things.Where(t => t.Body.Entity is ObjectInstance)))
            {
                if (Def(body.Entity)?.Type is ObjectType.Wall or ObjectType.Door or ObjectType.Window or ObjectType.Roof) continue;
                int order = z[body.Entity];
                if (IsPinned(body.Entity)) // hung on the wall: over every wall its picture is over
                {
                    foreach (int i in WallsNear(screen))
                        if (walls[i].Screen.IntersectsWith(screen)) order = Math.Max(order, z[walls[i].Entity] + 1);
                    z[body.Entity] = order;
                    continue;
                }
                var me = OrderBoxOf(body, screen);
                int lowest = int.MaxValue;
                foreach (int i in WallsNear(screen))
                {
                    int cmp = ExultCompare(me, walls[i].Box, false, walls[i].Occludes);
                    int wallZ = z[walls[i].Entity];
                    if (cmp > 0) order = Math.Max(order, wallZ + 1);
                    else if (cmp < 0) lowest = Math.Min(lowest, wallZ - 1);
                }
                z[body.Entity] = lowest == int.MaxValue ? order : Math.Min(order, lowest);
            }
        }

        // 4. Equal orders as the game's canvas draws them: its objects were put on it first, then its books and containers.
        var objectRank = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        int rank = 0;
        foreach (var o in _map.Objects) objectRank[o] = rank++;
        foreach (var i in _map.Items) objectRank[i] = rank++;
        int place = 0;
        foreach (var (body, visual, _) in things.OrderBy(t => z[t.Body.Entity]).ThenBy(t => objectRank.GetValueOrDefault(t.Body.Entity)))
            Panel.SetZIndex(visual, Math.Clamp(1000 + place++, 1, CharacterZIndex - 1));
    }
}
