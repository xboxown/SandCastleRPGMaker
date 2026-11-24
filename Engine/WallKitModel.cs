namespace SandcastleRpgMaker.Engine;

/// <summary>
/// A wall kit (Tools > Wall Kits; Map Editor > Mode > Wall Plot): which wall piece goes where, so walls are drawn
/// with the mouse and every cell picks its own piece from its neighbours - like RPG Maker's auto-tiles. A piece is
/// chosen by the walls it joins: north, east, south and west (16 shapes - straights, corners, T-joins, the cross, the
/// ends and a lone pillar). Each piece's nudge on its cell is set once in the kit, so drawn pieces always meet.
/// Only the Map Editor knows about kits: the pieces it puts down are ordinary objects in play, on every platform.
/// </summary>
public class WallKit
{
    public const int North = 1, East = 2, South = 4, West = 8;
    public const int NorthSouth = North | South, WestEast = West | East;

    public int Id { get; set; }
    public string Name { get; set; } = "New Wall Kit";
    public List<WallKitPiece> Pieces { get; set; } = new();

    /// <summary>The piece set for exactly these joins, or null.</summary>
    public WallKitPiece? PieceFor(int joins) => Pieces.FirstOrDefault(p => p.Joins == joins && p.ObjectId >= 0);

    public bool HasAnyPiece => Pieces.Any(p => p.ObjectId >= 0);

    /// <summary>
    /// What goes on a cell with these joins: its own piece; a piece the kit doesn't have is filled in from the
    /// straights (a cross or a corner = the north-south and the west-east straight on one cell, an end = its straight),
    /// and a lone pillar is the west-east straight. Empty only when the kit has no pieces at all.
    /// </summary>
    public List<WallKitPiece> Resolve(int joins)
    {
        if (PieceFor(joins) is { } own) return new List<WallKitPiece> { own };
        var list = new List<WallKitPiece>();
        bool ns = (joins & NorthSouth) != 0, we = (joins & WestEast) != 0;
        if (!ns && !we) { if ((PieceFor(WestEast) ?? PieceFor(NorthSouth)) is { } lone) list.Add(lone); }
        else
        {
            if (ns && PieceFor(NorthSouth) is { } upDown) list.Add(upDown);
            if (we && PieceFor(WestEast) is { } across) list.Add(across);
        }
        if (list.Count == 0 && Pieces.FirstOrDefault(p => p.ObjectId >= 0) is { } any) list.Add(any); // still a wall: any piece the kit has
        return list;
    }

    /// <summary>The 16 shapes in the kit window's order: straights, corners, T-joins, the cross, the ends, the pillar.</summary>
    public static readonly int[] Shapes =
    {
        WestEast, NorthSouth,
        East | South, West | South, North | East, North | West,
        West | East | South, West | East | North, North | South | East, North | South | West,
        North | East | South | West,
        East, West, South, North, 0
    };

    public static string ShapeName(int joins) => joins switch
    {
        WestEast => "Straight west-east",
        NorthSouth => "Straight north-south",
        East | South => "Corner top-left (joins east and south)",
        West | South => "Corner top-right (joins west and south)",
        North | East => "Corner bottom-left (joins north and east)",
        North | West => "Corner bottom-right (joins north and west)",
        West | East | South => "T-join, wall going south",
        West | East | North => "T-join, wall going north",
        North | South | East => "T-join, wall going east",
        North | South | West => "T-join, wall going west",
        North | East | South | West => "Cross",
        East => "End (the wall goes on east)",
        West => "End (the wall goes on west)",
        South => "End (the wall goes on south)",
        North => "End (the wall goes on north)",
        _ => "Pillar (no wall next to it)"
    };

    /// <summary>The facing a straight-run piece is placed with by default: a west-east wall faces south, a north-south one east (as the Wall Designer).</summary>
    public static SpriteDirection DefaultFacing(int joins) =>
        (joins & NorthSouth) != 0 && (joins & WestEast) == 0 ? SpriteDirection.East : SpriteDirection.South;

    public WallKit Clone() => new() { Id = Id, Name = Name, Pieces = Pieces.Select(p => p.Clone()).ToList() };
}

/// <summary>One piece of a wall kit: the object for one shape, how it faces and its nudge on the cell.</summary>
public class WallKitPiece
{
    /// <summary>The walls it joins: WallKit.North | East | South | West.</summary>
    public int Joins { get; set; }
    /// <summary>The object (Object Editor) - a Wall, or a Wall built from a Wall Designer set; -1 = not set.</summary>
    public int ObjectId { get; set; } = -1;
    public SpriteDirection Facing { get; set; } = SpriteDirection.South;
    /// <summary>The nudge on its cell, in world units (8px), as a placed object's offset.</summary>
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }

    public WallKitPiece Clone() => (WallKitPiece)MemberwiseClone();
}
