using SandcastleRpgMaker.Engine;

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
    /// <param name="goalFloor">The goal is on this floor (up or down the stairs to it); null: whatever floor it's reached at.</param>
    private bool WalkPlayerTo((int X, int Y) goal, Action arrived, bool besideOnly = false, int? goalFloor = null)
    {
        var path = FindPlayerPath(goal, besideOnly, goalFloor);
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

    /// <summary>A* from the player's cell to the goal (or next to it, if the goal itself is taken - a chair). With a goal floor, the same cell on two floors is two places (as FindPath).</summary>
    private List<(int X, int Y)>? FindPlayerPath((int X, int Y) goal, bool besideOnly = false, int? goalFloor = null)
    {
        int FloorKey(int lift) => goalFloor is null ? 0 : MapFloors.FloorOfLift(lift);
        int wantFloor = goalFloor ?? 0;
        (int X, int Y, int F) start = (_playerX, _playerY, FloorKey(_playerZ));
        if ((start.X, start.Y) == goal && start.F == wantFloor && !besideOnly) return new List<(int X, int Y)>();
        var world = WorldNow(); // kept by area (GameTestWindow.Perf.cs)
        bool PlayerCanStand((int X, int Y) cell, int fromLift, out int lift) =>
            TryStepActor(PlayerFootprintAt(CellCenterX(cell.X), CellCenterY(cell.Y)), fromLift, _playerSize.Z, out lift, out _, null, world);
        bool goalFree = !besideOnly && (goalFloor is { } gf
            ? PlayerCanStand(goal, MapFloors.BaseLift(gf), out int goalLift) && MapFloors.FloorOfLift(goalLift) == gf
            : PlayerCanStand(goal, _playerZ, out _)); // a chair: beside it, not on it
        int H((int X, int Y, int F) node) => Heuristic((node.X, node.Y), goal) + (goalFloor is null ? 0 : Math.Abs(node.F - wantFloor) * MapFloors.LiftsPerFloor);

        var open = new PriorityQueue<(int X, int Y, int F), int>();
        var cameFrom = new Dictionary<(int X, int Y, int F), (int X, int Y, int F)>();
        var cost = new Dictionary<(int X, int Y, int F), int> { [start] = 0 };
        var liftAt = new Dictionary<(int X, int Y, int F), int> { [start] = _playerZ };
        open.Enqueue(start, H(start));
        int expansions = 0;
        while (open.Count > 0 && expansions++ < 6000)
        {
            var current = open.Dequeue();
            bool done = current.F == wantFloor
                && (((current.X, current.Y) == goal && goalFree) || (!goalFree && Math.Abs(current.X - goal.X) + Math.Abs(current.Y - goal.Y) == 1));
            if (done)
            {
                var path = new List<(int X, int Y)>();
                for (var step = current; step != start; step = cameFrom[step]) path.Add((step.X, step.Y));
                path.Reverse();
                return path;
            }
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var cell = (current.X + dx, current.Y + dy);
                if (!InsideMap(cell.Item1, cell.Item2)) continue;
                if (goalFloor is null && cost.TryGetValue((cell.Item1, cell.Item2, 0), out int reached) && reached <= cost[current] + 1) continue; // no floor asked for: one place per cell
                int lift;
                if (goalFloor is not null)
                {
                    // To another floor: only the walk itself, step by step, gets up or down the stairs (several lifts a cell, 1 a step).
                    if (!PlayerCanWalkBetween((current.X, current.Y), cell, liftAt[current], world, out lift)) continue;
                }
                else
                {
                    if (!PlayerCanStand(cell, liftAt[current], out lift)) continue;
                    if (!PlayerCanWalkBetween((current.X, current.Y), cell, liftAt[current], world)) continue; // the walk itself, not just where it ends
                }
                var next = (cell.Item1, cell.Item2, FloorKey(lift));
                int newCost = cost[current] + 1;
                if (cost.TryGetValue(next, out int known) && known <= newCost) continue;
                cost[next] = newCost;
                liftAt[next] = lift;
                cameFrom[next] = current;
                open.Enqueue(next, newCost + H(next));
            }
        }
        return null;
    }
}
