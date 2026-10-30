// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The main character walking somewhere on their own - Exult's
/// set_action_sequence for the Avatar (a chair double-clicked: walk to it,
/// then sit; Sit_schedule). A* in 8px steps (Exult's own tiles - its paths
/// are planned tile by tile, not by our 32px map cells) from where the player
/// really stands, with their own size and the same walking rules as a step,
/// so a scripted walk gets through every gap the arrow keys do; then walked
/// point by point at walking speed, ending in the middle of the cell when
/// there's room. Pressing a direction key takes over (the walk stops);
/// something blocking the way stops it too.
/// </summary>
public partial class U8GameWindow
{
    private List<(double X, double Y)>? _playerWalkPath; // the points walked to, in pixels
    private int _playerWalkIndex;
    private Action? _playerWalkArrived;

    /// <summary>Walks the player to (next to) a cell, then calls <paramref name="arrived"/>. False if there's no way there.</summary>
    /// <param name="goalFloor">The goal is on this floor (up or down the stairs to it); null: whatever floor it's reached at.</param>
    private bool WalkPlayerTo((int X, int Y) goal, Action arrived, bool besideOnly = false, int? goalFloor = null)
    {
        var path = FindPlayerPath(goal, besideOnly, goalFloor);
        if (path is null) return false;
        _playerWalkPath = path;
        _playerWalkIndex = 0;
        _playerWalkArrived = arrived;
        if (path.Count == 0) FinishPlayerWalk();
        return true;
    }

    private void CancelPlayerWalk()
    {
        _playerWalkPath = null;
        _playerWalkArrived = null;
    }

    private void FinishPlayerWalk()
    {
        var arrived = _playerWalkArrived;
        CancelPlayerWalk();
        ApplyPoseSprite(_idlePose);
        arrived?.Invoke();
    }

    /// <summary>One frame of the walk: toward the next point, one axis at a time, never past it.</summary>
    private void StepPlayerAlongPath(double deltaSeconds)
    {
        if (_playerWalkPath is null) return;
        if (_playerWalkIndex >= _playerWalkPath.Count) { FinishPlayerWalk(); return; }
        var (px, py) = _playerWalkPath[_playerWalkIndex];
        var (rx, ry) = WrappedDelta(px - _playerPixelX, py - _playerPixelY); // over the seam of a map that wraps around
        if (Math.Abs(rx) < 0.01 && Math.Abs(ry) < 0.01) { _playerWalkIndex++; return; }
        bool alongX = Math.Abs(rx) > 0.01;
        double remaining = Math.Abs(alongX ? rx : ry);
        int dx = alongX ? Math.Sign(rx) : 0, dy = alongX ? 0 : Math.Sign(ry);
        double step = Math.Min(deltaSeconds, remaining / (PlayerTilesPerSecond * _tileWidth));
        if (step <= 0) return; // a frame with no time in it (WPF can render twice at the same moment): nothing moves, nothing is blocked
        double beforeX = _playerPixelX, beforeY = _playerPixelY;
        StepPlayerContinuous(dx, dy, step);
        if (_playerPixelX == beforeX && _playerPixelY == beforeY)
        {
            // Blocked: stop cleanly - standing, nothing pending.
            CancelPlayerWalk();
            _pendingSit = null;
            ApplyPoseSprite(_idlePose);
            StatusText.Text = $"Something is in the way - {StatusText.Text}"; // keep the walking rule's own reason
        }
    }

    /// <summary>
    /// A* from where the player stands to the goal cell (or next to it, if the goal itself is taken - a chair), in 8px steps
    /// (Exult's tiles), so it threads a gap as narrow as the player wherever it lies in a cell - a 2x2 player through a passage
    /// in the top half of a row, which cell centre to cell centre never reaches. A turn costs a little more than a step, so the
    /// walk goes in straight lines, not a zigzag. In the goal cell, the last point is its middle when there's room. With a goal
    /// floor, the same spot on two floors is two places (as FindPath). Null: no way there.
    /// </summary>
    private List<(double X, double Y)>? FindPlayerPath((int X, int Y) goal, bool besideOnly = false, int? goalFloor = null)
    {
        const int StepCost = 2, TurnCost = 1, MaxExpansions = 60000;
        double unit = SpriteSizing.BaseUnit;
        int FloorKey(int lift) => goalFloor is null ? 0 : MapFloors.FloorOfLift(lift);
        int wantFloor = goalFloor ?? 0;
        var world = WorldNow(); // kept by area (U8GameWindow.Perf.cs)
        double goalX = CellCenterX(goal.X), goalY = CellCenterY(goal.Y);
        bool goalFree = !besideOnly && (goalFloor is { } gf
            ? TryStepActor(PlayerFootprintAt(goalX, goalY), MapFloors.BaseLift(gf), _playerSize.Z, out int goalLift, out _, null, world) && MapFloors.FloorOfLift(goalLift) == gf
            : TryStepActor(PlayerFootprintAt(goalX, goalY), _playerZ, _playerSize.Z, out _, out _, null, world)); // a chair: beside it, not on it
        (int X, int Y) CellOf(double x, double y) => ((int)Math.Floor(x / _tileWidth), (int)Math.Floor(y / _tileHeight));
        bool Done(double x, double y, int floor)
        {
            if (floor != wantFloor) return false;
            var cell = CellOf(x, y);
            if (besideOnly) return Heuristic(cell, goal) == 1;
            return cell == goal || (!goalFree && Heuristic(cell, goal) == 1); // its middle taken: anywhere in it, or right beside it, will do
        }
        int H(double x, double y, int floor)
        {
            var (dx, dy) = WrappedDelta(goalX - x, goalY - y);
            return StepCost * ((int)Math.Round((Math.Abs(dx) + Math.Abs(dy)) / unit) + (goalFloor is null ? 0 : Math.Abs(floor - wantFloor) * MapFloors.LiftsPerFloor));
        }
        // A spot is kept by its quarter pixels: the steps keep where the player stood within an 8px tile, as their footprint does.
        (long, long) Key(double x, double y) => ((long)Math.Round(x * 4), (long)Math.Round(y * 4));

        var points = new List<(double X, double Y, int Lift)> { (_playerPixelX, _playerPixelY, _playerZ) };
        var cameFrom = new List<int> { -1 };
        var cost = new Dictionary<((long, long) At, int Floor, int Dir), int> { [(Key(_playerPixelX, _playerPixelY), FloorKey(_playerZ), -1)] = 0 };
        var open = new PriorityQueue<(int Point, int Floor, int Dir), int>();
        open.Enqueue((0, FloorKey(_playerZ), -1), H(_playerPixelX, _playerPixelY, FloorKey(_playerZ)));
        var directions = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        int expansions = 0;
        while (open.Count > 0 && expansions++ < MaxExpansions)
        {
            var (index, floor, dir) = open.Dequeue();
            var here = points[index];
            int hereCost = cost[(Key(here.X, here.Y), floor, dir)];
            if (Done(here.X, here.Y, floor))
            {
                var path = new List<(double X, double Y)>();
                for (int i = index; i > 0; i = cameFrom[i]) path.Add((points[i].X, points[i].Y));
                path.Reverse();
                // The last steps to the middle of the cell, when there's room (a chair's last steps start from there, as before).
                var cell = CellOf(here.X, here.Y);
                double middleX = CellCenterX(cell.X), middleY = CellCenterY(cell.Y);
                if ((Math.Abs(middleX - here.X) > 0.01 || Math.Abs(middleY - here.Y) > 0.01)
                    && CanSweep(PlayerFootprintAt, here.X, here.Y, middleX, middleY, here.Lift, _playerSize.Z, null, world, out int middleLift) && FloorKey(middleLift) == floor)
                    path.Add((middleX, middleY));
                return path;
            }
            for (int d = 0; d < directions.Length; d++)
            {
                double x = here.X + directions[d].Item1 * unit, y = here.Y + directions[d].Item2 * unit;
                if (!CanSweep(PlayerFootprintAt, here.X, here.Y, x, y, here.Lift, _playerSize.Z, null, world, out int lift)) continue; // the step itself: stairs are climbed a lift at a time
                WrapPixel(ref x, ref y); // past the edge of a map that wraps around: over the seam
                var cellThere = CellOf(x, y);
                if (!InsideMap(cellThere.X, cellThere.Y)) continue;
                int nextFloor = FloorKey(lift);
                int newCost = hereCost + StepCost + (dir >= 0 && dir != d ? TurnCost : 0);
                var key = (Key(x, y), nextFloor, d);
                if (cost.TryGetValue(key, out int known) && known <= newCost) continue;
                cost[key] = newCost;
                points.Add((x, y, lift));
                cameFrom.Add(index);
                open.Enqueue((points.Count - 1, nextFloor, d), newCost + H(x, y, nextFloor));
            }
        }
        return null;
    }
}
