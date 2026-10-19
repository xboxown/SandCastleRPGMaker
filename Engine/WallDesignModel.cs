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
