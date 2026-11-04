namespace SandcastleRpgMaker.Engine;

/// <summary>Which way a wall runs: along the map's width or its height.</summary>
public enum WallRun { WestEast, NorthSouth }

/// <summary>
/// One piece of a wall set (Wall Designer): any object - a half wall, a
/// narrow side piece, the piece over a window, a Window, a Door - standing at
/// a spot and a height of its own. As in Ultima VII / Exult, a window is its
/// own shape standing at a lift between the wall pieces.
/// </summary>
public class WallPiece
{
    /// <summary>The object it is (Object Editor).</summary>
    public int ObjectId { get; set; } = -1;
    /// <summary>Where it stands, in world units (8px) from the set's anchor cell - the same as a placed object's sub-tile offset.</summary>
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
    /// <summary>How many lifts (4px each) above the set's foot it stands: the window on the half wall, the top piece on the window.</summary>
    public int Lift { get; set; }

    public WallPiece Clone() => (WallPiece)MemberwiseClone();
}

/// <summary>A wall set's pieces for one way it runs.</summary>
public class WallPose
{
    public WallRun Run { get; set; } = WallRun.WestEast;
    public List<WallPiece> Pieces { get; set; } = new();
}

/// <summary>
/// A wall set made in the Wall Designer: pieces glued together once - a
/// wall with a window in it, a doorway with wall over it - placed on the map
/// as one object (a Wall object linked to it), and in play every piece its
/// own: the wall is a wall, the window a window, the door a door.
/// </summary>
public class WallDesign
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Wall";
    public List<WallPose> Poses { get; set; } = new();

    /// <summary>The pieces for this way: its own pose, else the first one there is.</summary>
    public WallPose? Resolve(WallRun run) =>
        Poses.FirstOrDefault(p => p.Run == run && p.Pieces.Count > 0) ?? Poses.FirstOrDefault(p => p.Pieces.Count > 0);

    /// <summary>A placed copy's facing (Map Editor) as the way it runs: east or west = it runs north-south; else west-east.</summary>
    public static WallRun RunOf(SpriteDirection facing) =>
        facing is SpriteDirection.East or SpriteDirection.West ? WallRun.NorthSouth : WallRun.WestEast;

    /// <summary>The facing a placed copy gets for this way.</summary>
    public static SpriteDirection FacingOf(WallRun run) => run == WallRun.NorthSouth ? SpriteDirection.East : SpriteDirection.South;

    public static string RunName(WallRun run) => run == WallRun.NorthSouth ? "Runs north-south" : "Runs west-east";
}

/// <summary>A Wall object: the Wall Designer set it is built from (-1 = none: its own sprite).</summary>
public class WallSettings
{
    public int DesignId { get; set; } = -1;
}

/// <summary>
/// Wall sets in play: a set is placed in the Map Editor as one object; when
/// the game starts it becomes its pieces, each the real object it is, standing
/// where and as high as it was designed. The Windows games split the map they
/// open; a player pack (Amiga, MorphOS) holds its maps already split.
/// </summary>
public static class WallSets
{
    /// <summary>A world unit in pixels (SpriteSizing.BaseUnit).</summary>
    private const int BaseUnit = 8;

    public static WallDesign? DesignOf(WorldObject? obj, ObjectDatabase objects) =>
        obj is { Type: ObjectType.Wall, Wall.DesignId: >= 0 } ? objects.WallDesigns.FirstOrDefault(d => d.Id == obj.Wall.DesignId) : null;

    /// <summary>The pieces for a placed copy facing this way, or null when the object isn't a wall set.</summary>
    public static WallPose? PoseOf(WorldObject? obj, ObjectDatabase objects, SpriteDirection facing) =>
        DesignOf(obj, objects)?.Resolve(WallDesign.RunOf(facing));

    /// <summary>Every placed wall set on this map becomes its pieces (a save holds the pieces, so a loaded game has none left to split).</summary>
    public static void SplitInto(MapData map, ObjectDatabase objects, int tileWidth, int tileHeight)
    {
        if (objects.WallDesigns.Count == 0) return;
        var sets = map.Objects.Where(o => o.ContainerInstanceId < 0 && !o.WallSetPiece
            && PoseOf(objects.Objects.FirstOrDefault(d => d.Id == o.ObjectId), objects, o.Facing) is not null).ToList();
        int next = map.GetNextInstanceId(); // every piece its own number
        foreach (var set in sets)
        {
            var pose = PoseOf(objects.Objects.First(d => d.Id == set.ObjectId), objects, set.Facing)!;
            map.Objects.Remove(set);
            var pieces = Pieces(set, pose, tileWidth, tileHeight, () => next++);
            // The set's starting open / locked (Map Editor, right-click) is its door's; a window's shutters start as the Window object says.
            foreach (var piece in pieces.Where(p => objects.Objects.FirstOrDefault(d => d.Id == p.ObjectId)?.Type != ObjectType.Door))
            {
                piece.StartsOpen = null;
                piece.StartsLocked = null;
            }
            map.Objects.AddRange(pieces);
        }
    }

    /// <summary>
    /// The real objects a placed set is made of, in play: each piece at the
    /// set's cell plus its offset (whole cells carried into the cell, the rest
    /// kept as the sub-tile offset) and the set's lift plus its own. Doors and
    /// windows take the set's starting state from the Map Editor (open,
    /// locked).
    /// </summary>
    private static List<ObjectInstance> Pieces(ObjectInstance set, WallPose pose, int tileWidth, int tileHeight, Func<int> nextInstanceId)
    {
        int unitsX = Math.Max(1, tileWidth / BaseUnit), unitsY = Math.Max(1, tileHeight / BaseUnit);
        var pieces = new List<ObjectInstance>();
        foreach (var piece in pose.Pieces.Where(p => p.ObjectId >= 0))
        {
            int ox = set.OffsetX + piece.OffsetX, oy = set.OffsetY + piece.OffsetY;
            int cellsX = (int)Math.Floor(ox / (double)unitsX), cellsY = (int)Math.Floor(oy / (double)unitsY);
            pieces.Add(new ObjectInstance
            {
                ObjectId = piece.ObjectId,
                InstanceId = nextInstanceId(),
                X = set.X + cellsX, Y = set.Y + cellsY,
                OffsetX = ox - cellsX * unitsX, OffsetY = oy - cellsY * unitsY,
                Z = set.Z + piece.Lift,
                Facing = set.Facing,
                StartsOpen = set.StartsOpen, StartsLocked = set.StartsLocked, StartsLit = set.StartsLit,
                WallSetPiece = true
            });
        }
        return pieces;
    }
}
