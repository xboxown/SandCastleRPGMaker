using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Spawn points - eggs (Map Editor > Mode > Spawn), as Exult's (objs/egg.cc):
///  - When it hatches (Egg_object::is_active): CachedIn as the map is entered; PartyNear / AvatarNear when someone
///    of the party / the main character comes into its area (its distance round it, on its floor); AvatarFar when
///    the main character goes out of it; the footpads when they step on its own square; SomethingOn when a thing
///    (not a character) is put down in its area; External only by a Button egg. Nocturnal: only 21:00 - 04:59.
///  - Egg_object::hatch: it is marked hatched; a roll of 1-100 at most its Probability does its kind's hatch_now;
///    Once: then it's gone. Auto-reset (unhatch): it can hatch again once the party has left its area - a
///    continuous music or sound egg stops then.
///  - Monster_egg: its characters, each by its chance for each of how many, as copies of their own by it (its own
///    square first, then the squares round it), their timetable its own all day: Loiter within its reach, or
///    Wander unbounded. Jukebox / Soundsfx / Usecode / Teleport / Weather run the same event commands an event
///    would (Play Music, Play Sound, Call Common Event, Change Map, Change Weather), so every engine does the same.
///    Missile_egg: its projectile from it, in its direction or at a party member, again every DelaySeconds while
///    the main character stays within its range (Missile_launcher). Button_egg: sets off the External eggs within
///    its distance. Path_egg: only where teleports go.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>What an egg saw last time it was looked at, and its missile launcher / weather timer.</summary>
    private sealed class EggState
    {
        public bool AvatarIn, PartyIn;
        public int ThingsIn = -1;
        public bool Launching;
        public double LaunchMs;
        public long WeatherUntil = -1;
    }

    private readonly Dictionary<SpawnPoint, EggState> _eggs = new(ReferenceEqualityComparer.Instance);
    private double _eggCheckMs;
    private bool _eggsStarted;

    /// <summary>Before the characters are placed (BuildCharacterVisuals): the CachedIn monster eggs not hatched yet - their characters go among the map's placements.</summary>
    private void HatchSpawnPoints()
    {
        if (_characters is null) return;
        var taken = new HashSet<(int, int, int)>(_map.Characters.Where(p => !p.Dead).Select(p => (p.X, p.Y, p.Floor)));
        foreach (var spawn in _map.SpawnPoints.Where(s => s.Kind == EggKind.Monster && s.Criteria == EggCriteria.CachedIn && !s.HatchedRuntime && !s.GoneRuntime && !s.Nocturnal)) // a nocturnal one: at the eggs' first look (the hour known then)
        {
            spawn.HatchedRuntime = true;
            if (spawn.Once) spawn.GoneRuntime = true;
            if (Rng.Next(100) + 1 > spawn.Probability) continue;
            foreach (var placement in SpawnCopies(spawn, taken)) _map.Characters.Add(placement.Placement);
        }
    }

    /// <summary>A monster egg's characters: each by its chance for each of how many, a copy of its own on a free square by the egg.</summary>
    private List<(CharacterDefinition Copy, PlacedCharacter Placement)> SpawnCopies(SpawnPoint spawn, HashSet<(int, int, int)> taken)
    {
        var made = new List<(CharacterDefinition, PlacedCharacter)>();
        foreach (var entry in spawn.Entries)
        {
            if (_characters!.Characters.FirstOrDefault(c => c.Id == entry.CharacterId) is not { IsTestPlayer: false } kind) continue;
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
                made.Add((copy, new PlacedCharacter
                {
                    Id = _map.Characters.Count == 0 ? 1 : _map.Characters.Max(p => p.Id) + 1 + made.Count, CharacterId = copy.Id, X = at.X, Y = at.Y, Floor = spawn.Floor,
                    SavedCharacterIndex = _characters.Characters.IndexOf(copy) // this very copy (as a save's placements say)
                }));
            }
        }
        return made;
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

    /// <summary>Exult: a nocturnal egg is only active from 21:00 to 04:59.</summary>
    private bool NightAllows(SpawnPoint egg) => !egg.Nocturnal || _clock.Hour >= 21 || _clock.Hour <= 4;

    private bool InEggArea(SpawnPoint egg, int x, int y, int floor)
    {
        if (floor != egg.Floor) return false;
        int reach = egg.Criteria is EggCriteria.AvatarFootpad or EggCriteria.PartyFootpad ? 0 : Math.Max(0, egg.Distance);
        return Math.Abs(x - egg.X) <= reach && Math.Abs(y - egg.Y) <= reach;
    }

    /// <summary>Each frame (looked at four times a second): who is in each egg's area now, and what that sets off.</summary>
    private void UpdateEggs(double elapsedMs)
    {
        if (_map.SpawnPoints.Count == 0 || _player is null) return;
        foreach (var (egg, state) in _eggs.Where(e => e.Value.Launching).ToList()) UpdateLauncher(egg, state, elapsedMs);
        foreach (var (egg, state) in _eggs.Where(e => e.Value.WeatherUntil >= 0 && TotalMinutes() >= e.Value.WeatherUntil).ToList())
        {
            state.WeatherUntil = -1;
            RunEggCommands(new EventCommand { Type = EventCommandType.ChangeWeather, Parameters = { ["Kind"] = "Clear", ["Intensity"] = "0", ["Clouds"] = "" } });
        }
        if ((_eggCheckMs -= elapsedMs) > 0) return;
        _eggCheckMs = 250;
        int playerFloor = MapFloors.FloorOfLift(_playerZ);
        var party = _mapCharacters.Where(mc => mc.Character.IsInParty && !IsDead(mc.Character)).Select(mc => (mc.Placement.X, mc.Placement.Y, MapFloors.FloorOfLift(mc.Lift))).ToList();
        party.Add((_playerX, _playerY, playerFloor));
        foreach (var egg in _map.SpawnPoints.ToList())
        {
            if (egg.GoneRuntime) continue;
            if (!_eggs.TryGetValue(egg, out var state)) _eggs[egg] = state = new EggState();
            bool avatarIn = InEggArea(egg, _playerX, _playerY, playerFloor), partyIn = party.Any(p => InEggArea(egg, p.Item1, p.Item2, p.Item3));
            bool wasAvatarIn = state.AvatarIn, wasPartyIn = state.PartyIn;
            state.AvatarIn = avatarIn; state.PartyIn = partyIn;
            if (!_eggsStarted && egg.Criteria == EggCriteria.CachedIn && !egg.HatchedRuntime) { Hatch(egg); continue; } // as the map is entered
            bool fire = egg.Criteria switch
            {
                EggCriteria.PartyNear or EggCriteria.PartyFootpad => partyIn && !wasPartyIn,
                EggCriteria.AvatarNear or EggCriteria.AvatarFootpad => avatarIn && !wasAvatarIn,
                EggCriteria.AvatarFar => !avatarIn && wasAvatarIn,
                EggCriteria.SomethingOn => ThingsInEggArea(egg, state),
                _ => false
            };
            if (fire && !egg.HatchedRuntime) Hatch(egg); // a hatched auto-reset egg is armed again by Unhatch
            // Exult's unhatch: an auto-reset egg the party has left can hatch again (a continuous one stops).
            bool left = egg.Criteria switch
            {
                EggCriteria.PartyNear or EggCriteria.PartyFootpad => !partyIn && wasPartyIn,
                EggCriteria.AvatarNear or EggCriteria.AvatarFootpad => !avatarIn && wasAvatarIn,
                EggCriteria.AvatarFar => avatarIn && !wasAvatarIn,
                _ => false
            };
            if (left && egg.HatchedRuntime && egg.AutoReset) Unhatch(egg);
        }
        _eggsStarted = true;
    }

    /// <summary>SomethingOn: a thing (not a character) more in its area than before.</summary>
    private bool ThingsInEggArea(SpawnPoint egg, EggState state)
    {
        int count = _map.Objects.Count(o => o.ContainerInstanceId < 0 && InEggArea(egg, o.X, o.Y, MapFloors.FloorOfLift(o.Z)))
                    + _map.Items.Count(i => InEggArea(egg, i.X, i.Y, MapFloors.FloorOfLift(i.Z)));
        bool more = state.ThingsIn >= 0 && count > state.ThingsIn;
        state.ThingsIn = count;
        return more;
    }

    /// <summary>Egg_object::hatch: marked hatched; its chance rolled; its kind's hatch_now; a "once" egg is gone.</summary>
    private void Hatch(SpawnPoint egg)
    {
        if (egg.GoneRuntime || !NightAllows(egg)) return;
        egg.HatchedRuntime = true;
        if (egg.Once) egg.GoneRuntime = true;
        if (Rng.Next(100) + 1 > egg.Probability) return;
        switch (egg.Kind)
        {
            case EggKind.Monster:
            {
                var taken = new HashSet<(int, int, int)>(_mapCharacters.Select(mc => (mc.Placement.X, mc.Placement.Y, MapFloors.FloorOfLift(mc.Lift)))) { (_playerX, _playerY, MapFloors.FloorOfLift(_playerZ)) };
                foreach (var (copy, placement) in SpawnCopies(egg, taken))
                {
                    _map.Characters.Add(placement); // kept by a save
                    BringOntoMap(copy, placement);
                    HealFully(copy);
                }
                break;
            }
            case EggKind.Music:
                RunEggCommands(new EventCommand { Type = EventCommandType.PlayMusic, Parameters = { ["MusicResourceId"] = egg.SoundId.ToString(), ["Loop"] = egg.Continuous ? "True" : "False" } });
                break;
            case EggKind.Sound:
                if (egg.Continuous) RunEggCommands(new EventCommand { Type = EventCommandType.PlaySound, Parameters = { ["SoundResourceId"] = egg.SoundId.ToString(), ["Loop"] = "True" } });
                else if (HearingAt(egg.X * _tileWidth + _tileWidth / 2.0 - MapFloors.BaseLift(egg.Floor) * LiftPixelShift, egg.Y * _tileHeight + _tileHeight / 2.0 - MapFloors.BaseLift(egg.Floor) * LiftPixelShift) is { } heard)
                    SoundPlayback.TryPlayAt(_resources, egg.SoundId, heard.Volume, heard.Balance); // from the egg, by the distance (Soundsfx_egg plays at the egg)
                break;
            case EggKind.Usecode:
                if (egg.CommonEventId >= 0) RunEggCommands(new EventCommand { Type = EventCommandType.CallCommonEvent, Parameters = { ["CommonEventId"] = egg.CommonEventId.ToString() } });
                break;
            case EggKind.Missile:
            {
                var state = _eggs.TryGetValue(egg, out var s) ? s : _eggs[egg] = new EggState();
                LaunchFromEgg(egg);
                // Missile_launcher: near eggs go on shooting while the main character stays within range.
                if (egg.Criteria is EggCriteria.PartyNear or EggCriteria.AvatarNear) { state.Launching = true; state.LaunchMs = Math.Max(0.2, egg.DelaySeconds) * 1000; }
                break;
            }
            case EggKind.Teleport:
            {
                int world = egg.TeleportWorld, x = egg.TeleportX, y = egg.TeleportY, floor = egg.TeleportFloor;
                if (egg.TeleportPath >= 0)
                {
                    if (_map.SpawnPoints.FirstOrDefault(p => p.Kind == EggKind.Path && p.PathNumber == egg.TeleportPath) is not { } path) break;
                    (world, x, y, floor) = (-1, path.X, path.Y, path.Floor);
                }
                if (x < 0 || y < 0) break;
                RunEggCommands(new EventCommand { Type = EventCommandType.ChangeMap, Parameters = { ["World"] = world > 0 ? world.ToString() : "", ["X"] = x.ToString(), ["Y"] = y.ToString(), ["Floor"] = floor.ToString(), ["Fade"] = "False" } });
                break;
            }
            case EggKind.Weather:
            {
                RunEggCommands(new EventCommand { Type = EventCommandType.ChangeWeather, Parameters = { ["Kind"] = egg.WeatherKind, ["Intensity"] = egg.WeatherIntensity.ToString(), ["Clouds"] = "" } });
                var state = _eggs.TryGetValue(egg, out var s) ? s : _eggs[egg] = new EggState();
                state.WeatherUntil = egg.WeatherMinutes > 0 ? TotalMinutes() + egg.WeatherMinutes : -1;
                break;
            }
            case EggKind.Button:
                foreach (var other in _map.SpawnPoints.ToList())
                    if (!ReferenceEquals(other, egg) && other.Criteria == EggCriteria.External && (!other.HatchedRuntime || other.AutoReset) && other.Floor == egg.Floor
                        && Math.Abs(other.X - egg.X) <= egg.Distance && Math.Abs(other.Y - egg.Y) <= egg.Distance)
                        Hatch(other);
                break;
        }
    }

    /// <summary>Exult's unhatch: it can hatch again; a continuous music or sound egg stops.</summary>
    private void Unhatch(SpawnPoint egg)
    {
        egg.HatchedRuntime = false;
        if (_eggs.TryGetValue(egg, out var state)) state.Launching = false;
        if (egg.Kind == EggKind.Music && egg.Continuous) RunEggCommands(new EventCommand { Type = EventCommandType.StopMusic, Parameters = { ["Seconds"] = "0" } });
        if (egg.Kind == EggKind.Sound && egg.Continuous) RunEggCommands(new EventCommand { Type = EventCommandType.StopSound, Parameters = { ["SoundResourceId"] = egg.SoundId.ToString() } });
    }

    private void UpdateLauncher(SpawnPoint egg, EggState state, double elapsedMs)
    {
        if (egg.GoneRuntime || MapFloors.FloorOfLift(_playerZ) != egg.Floor || Math.Max(Math.Abs(_playerX - egg.X), Math.Abs(_playerY - egg.Y)) > egg.RangeTiles) { state.Launching = false; return; } // out of range: it stops
        if ((state.LaunchMs -= elapsedMs) > 0) return;
        state.LaunchMs = Math.Max(0.2, egg.DelaySeconds) * 1000;
        LaunchFromEgg(egg);
    }

    /// <summary>Its projectile from its middle: in its direction, or at a party member within its range.</summary>
    private void LaunchFromEgg(SpawnPoint egg)
    {
        if (_objects.Objects.FirstOrDefault(o => o.Id == egg.ProjectileId && o.Type == ObjectType.Projectile) is not { } projectile) return;
        double x = egg.X * _tileWidth + _tileWidth / 2.0, y = egg.Y * _tileHeight + _tileHeight / 2.0;
        int lift = MapFloors.BaseLift(egg.Floor) + 1;
        if (egg.Direction is >= 0 and < 8)
        {
            var (dx, dy) = FacingStep((SpriteDirection)egg.Direction);
            Launch(projectile, x, y, dx, dy, lift, null);
            return;
        }
        var targets = new List<CharacterDefinition>();
        if (Math.Max(Math.Abs(_playerX - egg.X), Math.Abs(_playerY - egg.Y)) <= egg.RangeTiles) targets.Add(_player!);
        targets.AddRange(_mapCharacters.Where(mc => mc.Character.IsInParty && !IsDead(mc.Character) && Math.Max(Math.Abs(mc.Placement.X - egg.X), Math.Abs(mc.Placement.Y - egg.Y)) <= egg.RangeTiles).Select(mc => mc.Character));
        if (targets.Count == 0) return;
        var (tx, ty) = PixelOf(targets[Rng.Next(targets.Count)]);
        double ddx = tx - x, ddy = ty - y, length = Math.Sqrt(ddx * ddx + ddy * ddy);
        if (length < 1) return;
        LaunchToward(projectile, x, y, ddx / length, ddy / length, lift, null, 0, null);
    }

    /// <summary>An egg's effect as the event commands an event would run (the same in every engine), run as the main character's.</summary>
    private void RunEggCommands(params EventCommand[] commands)
    {
        if (_player is null) return;
        RunCommandsOn(commands.ToList(), _player);
    }
}
