using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Characters in each other's way, ported from Exult (actors.cc
/// Actor::is_really_blocked, Actor::move_aside, objs.cc
/// Game_object::swap_positions). Characters are solid, and nobody climbs onto
/// another (a step of more than 1 lift is refused, and characters are taller
/// than that). When a walker's step is blocked by a character, the blocker is
/// asked to move aside: unless it's fighting, already walking, or sitting /
/// kneeling / sleeping, it steps to a free cell at right angles to the
/// walker's direction (one side, then the other), else a free diagonal; if
/// there's none, the two swap places - only when their footprints are the
/// same size. The walker then tries its step again.
/// The player is moved aside too, like Exult's avatar, but only while
/// standing still (not walking, not in combat mode).
/// </summary>
public partial class GameTestWindow
{
    /// <summary>The character on the map whose body takes up any of that footprint at that height, if any.</summary>
    private MapCharacter? CharacterInTheWay(UnitRect foot, int lift, int height, MapCharacter? walker)
    {
        foreach (var body in CharacterBodies())
        {
            if (walker is not null && ReferenceEquals(body.Entity, walker.Placement)) continue;
            if (!body.Overlaps(foot) || !(body.Z < lift + height && lift < body.TopLift)) continue;
            return _mapCharacters.FirstOrDefault(mc => ReferenceEquals(mc.Placement, body.Entity));
        }
        return null;
    }

    private bool PlayerInTheWay(UnitRect foot, int lift, int height) =>
        PlayerBody() is { } player && player.Overlaps(foot) && player.Z < lift + height && lift < player.TopLift;

    /// <summary>Cells to try, in Exult's order: right angles to the walker's direction first, then the diagonals.</summary>
    private static IEnumerable<(int DX, int DY)> AsideDirections(int dx, int dy)
    {
        yield return (-dy, dx);
        yield return (dy, -dx);
        foreach (var diagonal in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) }) yield return diagonal;
    }

    /// <summary>Actor::move_aside for a character on the map, for a walker heading (dx, dy). Swapping is with <paramref name="walker"/> (null = the player).</summary>
    private bool MoveAside(MapCharacter blocker, int dx, int dy, MapCharacter? walker)
    {
        var npc = blocker.Npc;
        if (npc.InCombat || npc.Path is not null || npc.Activity == ScheduleType.Sleep || IsIncapacitated(blocker.Character) || _seats.ContainsKey(blocker.Character) || blocker.Character.Movement == MovementKind.Stationary) return false; // sitting or lying: an obstacle (Exult) A stationary one never moves.
        var here = (blocker.Placement.X, blocker.Placement.Y);
        foreach (var (ax, ay) in AsideDirections(dx, dy))
        {
            var cell = (here.X + ax, here.Y + ay);
            if (!InsideMap(cell.Item1, cell.Item2) || !CanStandAt(blocker, cell, blocker.Lift, null, out _)) continue;
            StartPath(blocker, new List<(int X, int Y)> { cell }); // steps there, then carries on with what it was doing
            return true;
        }
        // Nowhere to go: swap places, if the same size.
        if (walker is null)
        {
            if ((blocker.Size.X, blocker.Size.Y) != (_playerSize.X, _playerSize.Y)) return false;
            var playerCell = (_playerX, _playerY);
            PutCharacter(blocker, playerCell);
            TeleportCommand(WhoChoice.Player, here.X, here.Y);
            return true;
        }
        if ((blocker.Size.X, blocker.Size.Y) != (walker.Size.X, walker.Size.Y)) return false;
        var walkerCell = (walker.Placement.X, walker.Placement.Y);
        PutCharacter(blocker, walkerCell);
        PutCharacter(walker, here);
        return true;
    }

    /// <summary>The player moved aside for a character walking into them (Exult's avatar does this too) - only while standing still.</summary>
    private bool MovePlayerAside(MapCharacter walker, int dx, int dy)
    {
        if (_player is null || _heldDirectionKeys.Count > 0 || _combatMode || IsIncapacitated(_player) || PlayerSeated) return false;
        foreach (var (ax, ay) in AsideDirections(dx, dy))
        {
            int x = _playerX + ax, y = _playerY + ay;
            if (!InsideMap(x, y)) continue;
            var foot = PlayerFootprintAt(CellCenterX(x), CellCenterY(y));
            if (!TryStepActor(foot, _playerZ, _playerSize.Z, out _, out _)) continue;
            TeleportCommand(WhoChoice.Player, x, y);
            return true;
        }
        // A narrow way with no room beside (a one-wide stair): on along it, ahead of the walker - or back behind it - walked there step by step (up or down the stairs).
        foreach (var (sx, sy) in new[] { (dx, dy), (-dx, -dy) })
        {
            var to = (X: _playerX + sx, Y: _playerY + sy);
            if (!InsideMap(to.X, to.Y) || to == (walker.Placement.X, walker.Placement.Y)) continue;
            if (!PlayerCanWalkBetween((_playerX, _playerY), to, _playerZ, WorldNow(), out int lift)) continue;
            PlacePlayer(to, lift);
            return true;
        }
        if (walker.Size.X > _playerSize.X || walker.Size.Y > _playerSize.Y) return false; // swapped only if the walker fits where the player stood
        var walkerCell = (walker.Placement.X, walker.Placement.Y);
        if (walkerCell == (_playerX, _playerY)) return false; // already in the same cell: a swap changes nothing
        PutCharacter(walker, (_playerX, _playerY));
        TeleportCommand(WhoChoice.Player, walkerCell.X, walkerCell.Y);
        return true;
    }

    /// <summary>The player put straight onto a cell, at a lift (stepping aside on a stair).</summary>
    private void PlacePlayer((int X, int Y) cell, int lift)
    {
        _playerX = cell.X;
        _playerY = cell.Y;
        _playerPixelX = CellCenterX(cell.X);
        _playerPixelY = CellCenterY(cell.Y);
        _playerZ = lift;
        _playerVisualLiftZ = lift;
        _eventUnderPlayer = EventAt(cell.X, cell.Y, lift); // stepping aside doesn't set off the event there
        SnapPlayerPosition();
        UpdateCamera();
    }

    /// <summary>Puts a character straight onto a cell (Game_object::move), and lets it think again from there.</summary>
    private void PutCharacter(MapCharacter mapCharacter, (int X, int Y) cell)
    {
        Teleport(mapCharacter, cell);
        mapCharacter.Npc.EventUnder = EventAt(cell.X, cell.Y, mapCharacter.Lift);
        mapCharacter.Npc.WaitMs = StdDelay;
    }
}
