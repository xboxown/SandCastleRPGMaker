namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The main character walking somewhere on their own - Exult's
/// set_action_sequence for the Avatar (a chair double-clicked: walk to it,
/// then sit; Sit_schedule). A* over map cells with the player's own size and
/// the same walking rules as a step; then walked cell by cell at walking
/// speed. Pressing a direction key takes over (the walk stops); something
/// blocking the way stops it too.
/// </summary>
public partial class GameTestWindow
{
    private List<(int X, int Y)>? _playerWalkPath;
    private int _playerWalkIndex;
    private Action? _playerWalkArrived;

    /// <summary>Walks the player to (next to) a cell, then calls <paramref name="arrived"/>. False if there's no way there.</summary>
    private bool WalkPlayerTo((int X, int Y) goal, Action arrived)
    {
        var path = FindPlayerPath(goal);
        if (path is null) return false;
        // First to the middle of the cell they're standing in: keyboard walking leaves them anywhere in it, and the path is cell centre to cell centre.
        if (path.Count > 0) path.Insert(0, (_playerX, _playerY));
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

    /// <summary>One frame of the walk: toward the next cell's centre, one axis at a time, never past it.</summary>
    private void StepPlayerAlongPath(double deltaSeconds)
    {
        if (_playerWalkPath is null) return;
        if (_playerWalkIndex >= _playerWalkPath.Count) { FinishPlayerWalk(); return; }
        var (cx, cy) = _playerWalkPath[_playerWalkIndex];
        double rx = CellCenterX(cx) - _playerPixelX, ry = CellCenterY(cy) - _playerPixelY;
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

    /// <summary>A* from the player's cell to the goal (or next to it, if the goal itself is taken - a chair).</summary>
    private List<(int X, int Y)>? FindPlayerPath((int X, int Y) goal)
    {
        var start = (_playerX, _playerY);
        if (start == goal) return new List<(int X, int Y)>();
        var world = WorldBodies();
        bool PlayerCanStand((int X, int Y) cell, int fromLift, out int lift) =>
            TryStepActor(PlayerFootprintAt(CellCenterX(cell.X), CellCenterY(cell.Y)), fromLift, _playerSize.Z, out lift, out _, null, world);
        bool goalFree = PlayerCanStand(goal, _playerZ, out _);

        var open = new PriorityQueue<(int X, int Y), int>();
        var cameFrom = new Dictionary<(int X, int Y), (int X, int Y)>();
        var cost = new Dictionary<(int X, int Y), int> { [start] = 0 };
        var liftAt = new Dictionary<(int X, int Y), int> { [start] = _playerZ };
        open.Enqueue(start, Heuristic(start, goal));
        int expansions = 0;
        while (open.Count > 0 && expansions++ < 6000)
        {
            var current = open.Dequeue();
            bool done = current == goal || (!goalFree && Math.Max(Math.Abs(current.X - goal.X), Math.Abs(current.Y - goal.Y)) <= 1);
            if (done)
            {
                var path = new List<(int X, int Y)>();
                for (var step = current; step != start; step = cameFrom[step]) path.Add(step);
                path.Reverse();
                return path;
            }
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var next = (current.X + dx, current.Y + dy);
                if (!InsideMap(next.Item1, next.Item2)) continue;
                int newCost = cost[current] + 1;
                if (cost.TryGetValue(next, out int known) && known <= newCost) continue;
                if (!PlayerCanStand(next, liftAt[current], out int lift)) continue;
                cost[next] = newCost;
                liftAt[next] = lift;
                cameFrom[next] = current;
                open.Enqueue(next, newCost + Heuristic(next, goal));
            }
        }
        return null;
    }
}
