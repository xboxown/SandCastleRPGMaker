using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Spawn points (Map Editor > Mode > Spawn), as Ultima VII's monster eggs: the first time the map is entered,
/// each of a point's characters has its chance for each of how many - a copy of its own is placed by the point
/// (the point's own cell first, then the cells round it). A copy's timetable is the point's, all day long:
/// Loiter round it within its reach (Loiter_schedule picks spots round its centre, so it comes back), or, set
/// free, Wander with no bounds. The point remembers it has hatched (saves and worlds come back to keep it).
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Before the characters are placed (BuildCharacterVisuals): each point not hatched yet has its chance.</summary>
    private void HatchSpawnPoints()
    {
        if (_characters is null) return;
        var taken = new HashSet<(int, int, int)>(_map.Characters.Where(p => !p.Dead).Select(p => (p.X, p.Y, p.Floor)));
        foreach (var spawn in _map.SpawnPoints.Where(s => !s.HatchedRuntime))
        {
            spawn.HatchedRuntime = true;
            foreach (var entry in spawn.Entries)
            {
                if (_characters.Characters.FirstOrDefault(c => c.Id == entry.CharacterId) is not { IsTestPlayer: false } kind) continue;
                for (int n = 0; n < Math.Max(1, entry.Count); n++)
                {
                    if (Rng.Next(100) >= entry.Percent) continue;
                    if (FreeSpawnCell(spawn, taken) is not { } at) break;
                    taken.Add((at.X, at.Y, spawn.Floor));
                    var copy = CopyOfCharacter(kind);
                    copy.IsInParty = false;
                    copy.Schedules = Enumerable.Range(0, 8).Select(period => new ScheduleEntry
                    {
                        Period = period, Type = spawn.Free ? ScheduleType.Wander : ScheduleType.Loiter, X = spawn.X, Y = spawn.Y,
                        WorkAreaTiles = spawn.Free ? 0 : Math.Max(1, spawn.WanderTiles) * Math.Max(1, (int)Math.Round(_tileWidth / 8.0)) // map squares as Exult tiles: the reach Loiter keeps to; 0: free
                    }).ToList();
                    _map.Characters.Add(new PlacedCharacter
                    {
                        Id = _map.Characters.Count == 0 ? 1 : _map.Characters.Max(p => p.Id) + 1, CharacterId = copy.Id, X = at.X, Y = at.Y, Floor = spawn.Floor,
                        SavedCharacterIndex = _characters.Characters.IndexOf(copy) // this very copy (as a save's placements say)
                    });
                }
            }
        }
    }

    /// <summary>The point's own cell if free, else the nearest free one round it (up to 3 cells out).</summary>
    private (int X, int Y)? FreeSpawnCell(SpawnPoint spawn, HashSet<(int, int, int)> taken)
    {
        for (int ring = 0; ring <= 3; ring++)
            for (int dy = -ring; dy <= ring; dy++)
                for (int dx = -ring; dx <= ring; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != ring) continue;
                    int x = spawn.X + dx, y = spawn.Y + dy;
                    if (InsideMap(x, y) && !taken.Contains((x, y, spawn.Floor))) return (x, y);
                }
        return null;
    }
}
