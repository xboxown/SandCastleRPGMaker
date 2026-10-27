using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Path planning checks the whole walk between two cells, not just where
/// the walker stands at each cell's middle: walkers go along X first, then
/// Y, so a tree trunk or a table edge between two clear cell middles blocks
/// the walk - and a plan through it would bump into it, try the same way
/// again and again, and in the end be put there (the "teleport").
/// </summary>
public partial class GameTestWindow
{
    /// <summary>
    /// Whether a walker can get from one point to another the way it walks
    /// (along X first, then Y), half an 8px unit at a time, each new
    /// footprint by the walking rules (1 lift up or down, headroom, solids).
    /// </summary>
    private bool CanSweep(Func<double, double, UnitRect> footAt, double fromX, double fromY, double toX, double toY,
        int fromLift, int height, object? mover, BodySource world, out int lift)
    {
        lift = fromLift;
        double step = SpriteSizing.BaseUnit / 2.0;
        double x = fromX, y = fromY;
        var last = footAt(x, y);
        foreach (bool alongX in new[] { true, false })
        {
            while (true)
            {
                double remaining = (alongX ? toX : toY) - (alongX ? x : y);
                if (Math.Abs(remaining) < 0.01) break;
                double move = Math.Sign(remaining) * Math.Min(step, Math.Abs(remaining));
                if (alongX) x += move; else y += move;
                var foot = footAt(x, y);
                if (foot == last) continue;
                if (!TryStepActor(foot, lift, height, out lift, out _, mover, world, explain: false)) return false;
                last = foot;
            }
        }
        return true;
    }

    /// <summary>A character's walk from one cell (or, leaving where it stands, its own spot) to the middle of the next.</summary>
    /// <param name="lift">The lift it stands at in the end - walked there step by step, so stairs (several lifts a cell, 1 a step) are climbed and gone down.</param>
    private bool CanWalkBetween(MapCharacter mapCharacter, (int X, int Y) from, (int X, int Y) to, bool fromWhereItStands, int fromLift, BodySource world, out int lift) =>
        CanSweep((px, py) => ActorFootprintAt(px, py, mapCharacter.Size),
            fromWhereItStands ? mapCharacter.PixelX : CellCenterX(from.X), fromWhereItStands ? mapCharacter.PixelY : CellCenterY(from.Y),
            CellCenterX(to.X), CellCenterY(to.Y), fromLift, mapCharacter.Size.Z, mapCharacter.Placement, world, out lift);

    private bool CanWalkBetween(MapCharacter mapCharacter, (int X, int Y) from, (int X, int Y) to, bool fromWhereItStands, int fromLift, BodySource world) =>
        CanWalkBetween(mapCharacter, from, to, fromWhereItStands, fromLift, world, out _);

    /// <summary>A character's walk between two points (its spots in two cells, FindPathAStar).</summary>
    private bool CanWalkBetweenPoints(MapCharacter mapCharacter, double fromX, double fromY, double toX, double toY, int fromLift, BodySource world, out int lift) =>
        CanSweep((px, py) => ActorFootprintAt(px, py, mapCharacter.Size), fromX, fromY, toX, toY, fromLift, mapCharacter.Size.Z, mapCharacter.Placement, world, out lift);

    /// <summary>The player's walk from one cell's middle to the next (and the lift they end at).</summary>
    private bool PlayerCanWalkBetween((int X, int Y) from, (int X, int Y) to, int fromLift, BodySource world, out int lift) =>
        CanSweep(PlayerFootprintAt, CellCenterX(from.X), CellCenterY(from.Y), CellCenterX(to.X), CellCenterY(to.Y), fromLift, _playerSize.Z, null, world, out lift);

    private bool PlayerCanWalkBetween((int X, int Y) from, (int X, int Y) to, int fromLift, BodySource world) =>
        PlayerCanWalkBetween(from, to, fromLift, world, out _);
}
