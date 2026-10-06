using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The game clock and every AI character's schedule, ported from Exult:
///  - Game_clock::handle_event: the clock (Engine/ScheduleModel.cs GameClock);
///    at every hour change Game_window::schedule_npcs re-checks each NPC
///    (skipping one in the Wait schedule, as Exult does for companions).
///  - Npc_actor::update_schedule: the entry for the current 3-hour period, or,
///    if none, the closest earlier one - unless already doing it there.
///  - Actor::set_schedule_and_loc: don't interrupt the same activity close to
///    its spot; if the NPC and the spot are both off-screen, just put it there
///    (teleport_offscreen_to_schedule, more than 12 tiles away); otherwise
///    walk there (Walk_to_schedule).
///  - Walk_to_schedule::now_what: within 3 tiles = arrived; after 40 legs or 2
///    failed tries, jump there; an unfindable path is retried after 1 second.
///  - The activities' now_what: Loiter (12 tiles), Wander (legs of 32 tiles
///    within 128), Pace, Stand, Wait, Sleep, Dance, and Custom (event commands).
/// Party members and the main player never follow schedules (Exult:
/// find_schedule_change fails for party members). Nothing here runs while
/// the game is paused by an open gump.
/// </summary>
public partial class GameTestWindow
{
    private sealed class NpcState
    {
        /// <summary>The activity being done now; null while walking to it, or with no schedule at all.</summary>
        public ScheduleType? Activity;
        public bool WalkingToSchedule;
        public ScheduleEntry? Entry;
        /// <summary>The schedule spot (cell) and the "old_schedule_loc" Exult compares against.</summary>
        public (int X, int Y) Dest;
        public (int X, int Y)? OldScheduleLoc;
        /// <summary>Where it was placed in the Map Editor - the spot of entries set to "placed spot".</summary>
        public (int X, int Y) Home;
        /// <summary>Loiter/wander/dance centre: where the activity began.</summary>
        public (int X, int Y) Center;
        public int Retries, Legs, Phase, PaceDirection = 1;
        /// <summary>Milliseconds until the activity's next now_what; negative = nothing pending.</summary>
        public double WaitMs = -1;
        public List<(int X, int Y)>? Path;
        public int PathIndex;
        public bool WasBlocked;
        /// <summary>The map event under it, for By Enter / By Exit / By Stay like the player's.</summary>
        public GameEvent? EventUnder;

        // Combat (GameTestWindow.Combat.cs): Exult's Combat_schedule state.
        public bool InCombat, Fleeing, Hounding;
        public CharacterDefinition? Target, FleeFrom, Oppressor;
        public int DexPoints, Failures, FleeCount;
    }

    private GameClock _clock = new();
    private static readonly Random Rng = new();
    private const int StdDelay = GameClock.StdDelayMs;

    // ---------- Clock ----------

    private void UpdateClockAndCharacters(double elapsedMs)
    {
        int hourChanges = _clock.Advance(elapsedMs);
        if (hourChanges > 0) ScheduleNpcs(_clock.Hour);
        UpdateVitals(elapsedMs, hourChanges); // hunger, healing, poison, ailments, tile effects - everyone
        UpdateCombat(elapsedMs); // the player's blows; who sees whom (enemies on sight, fear, allies under attack)
        UpdateBleeding(elapsedMs); // drips from whoever is bleeding, and temporary puddles far away cleared
        foreach (var character in AllLivingCharacters().ToList()) UpdateUnconscious(character, elapsedMs); // the knocked out mend and get up
        UpdateObjects(elapsedMs); // lights burning, tiles and traps underfoot, doors closed behind NPCs
        ShowClock();
        foreach (var mapCharacter in _mapCharacters.ToList())
        {
            if (IsShowingMessage) break; // a dialog box just went up: everything stops right here
            UpdateNpc(mapCharacter, elapsedMs);
        }
    }

    /// <summary>Exult's fake_next_period (F6): straight to the start of the next 3-hour period, everyone re-scheduled.</summary>
    private void SkipToNextPeriod()
    {
        _clock.SkipToNextPeriod();
        ShowClock();
        ScheduleNpcs(_clock.Hour);
        StatusText.Text = $"Skipped to {_clock} ({Schedules.PeriodNames[_clock.Period]} period).";
    }

    /// <summary>Game_window::schedule_npcs - each hour; companions told to Wait are left alone.</summary>
    private void ScheduleNpcs(int hour)
    {
        foreach (var mapCharacter in _mapCharacters)
        {
            if (mapCharacter.Npc.Activity == ScheduleType.Wait) continue;
            if (mapCharacter.Npc.InCombat && mapCharacter.Npc.Target is not null) continue; // Exult: not while fighting someone
            UpdateSchedule(mapCharacter, hour / 3, hour % 3 == 0 ? -1 : 0);
        }
    }

    private void StartSchedules()
    {
        UpdateCamera(); // "off-screen" (teleport straight to the spot) is judged from where the camera really is
        foreach (var mapCharacter in _mapCharacters)
        {
            mapCharacter.Npc.Home = (mapCharacter.Placement.X, mapCharacter.Placement.Y);
            mapCharacter.Npc.EventUnder = EventAt(mapCharacter.Placement.X, mapCharacter.Placement.Y); // standing there from the start doesn't set it off
            UpdateSchedule(mapCharacter, _clock.Period, -1);
        }
        ShowClock();
    }

    /// <summary>The clock, and the player's own health and food, in the status bar.</summary>
    private void ShowClock() => ClockText.Text = (_player is null ? _clock.ToString() : $"{_clock}   |   {DescribeVitals(_player)}") + (_combatMode ? "   |   COMBAT" : "");

    private (int X, int Y) SpotOf(MapCharacter mapCharacter, ScheduleEntry entry) =>
        entry.X >= 0 && entry.Y >= 0 ? (Math.Clamp(entry.X, 0, _map.Width - 1), Math.Clamp(entry.Y, 0, _map.Height - 1)) : mapCharacter.Npc.Home;

    /// <summary>Npc_actor::update_schedule.</summary>
    private void UpdateSchedule(MapCharacter mapCharacter, int period, int delayMs)
    {
        var schedules = mapCharacter.Character.Schedules;
        if (mapCharacter.Character.IsInParty || mapCharacter.Character.IsTestPlayer || schedules.Count == 0 || IsDead(mapCharacter.Character)) return;
        var npc = mapCharacter.Npc;
        var entry = schedules.FirstOrDefault(s => s.Period == period);
        if (entry is null)
        {
            // Not found? Look at the closest earlier one (find_schedule_at_time) - unless already in it.
            entry = schedules.OrderBy(s => (period - s.Period + 8) % 8).First();
            if ((npc.Activity == entry.Type || (npc.WalkingToSchedule && npc.Entry?.Type == entry.Type)) && npc.OldScheduleLoc == SpotOf(mapCharacter, entry)) return;
        }
        var spot = SpotOf(mapCharacter, entry);
        npc.OldScheduleLoc = spot;
        SetScheduleAndLoc(mapCharacter, entry, spot, delayMs);
    }

    /// <summary>Actor::set_schedule_and_loc.</summary>
    private void SetScheduleAndLoc(MapCharacter mapCharacter, ScheduleEntry entry, (int X, int Y) spot, int delayMs)
    {
        var npc = mapCharacter.Npc;
        // Already on the way there, or doing the same thing close by: don't interrupt.
        if (npc.Entry == entry && (npc.WalkingToSchedule || (npc.Activity == entry.Type && TileDistance(mapCharacter, spot) <= 16))) return;
        StandUp(mapCharacter.Character); // off the chair / out of bed (Sleep_schedule::ending makes the bed)
        StopWalking(mapCharacter);
        npc.Hounding = false;
        npc.Entry = entry;
        npc.Dest = spot;
        npc.Activity = null;
        if (!IsOnScreen(mapCharacter.PixelX, mapCharacter.PixelY) && !IsOnScreen(CellCenterX(spot.X), CellCenterY(spot.Y)))
        {
            // Both off-screen: Exult just puts it there (if it's more than 12 tiles away).
            if (TileDistance(mapCharacter, spot) > 12) Teleport(mapCharacter, spot);
            BeginActivity(mapCharacter);
            return;
        }
        npc.WalkingToSchedule = true;
        npc.Retries = 0;
        npc.Legs = 0;
        // Walk_to_schedule: first delay 0-5 s when random, then 0-1 s more before setting off.
        npc.WaitMs = (delayMs >= 0 ? delayMs : Rng.Next(5000)) + Rng.Next(1000);
    }

    // ---------- What next (each schedule's now_what) ----------

    private void UpdateNpc(MapCharacter mapCharacter, double elapsedMs)
    {
        var npc = mapCharacter.Npc;
        if (IsIncapacitated(mapCharacter.Character)) return; // dead, asleep or paralyzed: does nothing at all
        if (npc.Path is not null) { MoveAlongPath(mapCharacter, elapsedMs); return; }
        if (npc.WaitMs < 0) return;
        npc.WaitMs -= elapsedMs;
        if (npc.WaitMs > 0) return;
        npc.WaitMs = -1;
        NowWhat(mapCharacter);
    }

    private void NowWhat(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        if (npc.InCombat) { CombatNowWhat(mapCharacter); return; }
        if (npc.Hounding) { HoundNowWhat(mapCharacter); return; }
        if (npc.WalkingToSchedule) { WalkToScheduleNowWhat(mapCharacter); return; }
        switch (npc.Activity)
        {
            case ScheduleType.Loiter: LoiterNowWhat(mapCharacter, CellsForTiles(12)); break;
            case ScheduleType.Wander: WanderNowWhat(mapCharacter); break;
            case ScheduleType.HorizPace: PaceNowWhat(mapCharacter, horizontal: true); break;
            case ScheduleType.VertPace: PaceNowWhat(mapCharacter, horizontal: false); break;
            case ScheduleType.Dance: DanceNowWhat(mapCharacter); break;
            case ScheduleType.Sit:
                if (!_seats.ContainsKey(mapCharacter.Character) && !GoToFurniture(mapCharacter, bed: false)) npc.WaitMs = 1000 + Rng.Next(1000); // Sit_schedule: try again later
                break;
            case ScheduleType.Sleep:
                if (!_seats.ContainsKey(mapCharacter.Character) && !GoToFurniture(mapCharacter, bed: true)) SetCharacterPose(mapCharacter, "Sleeping", mapCharacter.Facing); // no bed: the floor
                break;
        }
    }

    /// <summary>Walk_to_schedule::now_what.</summary>
    private void WalkToScheduleNowWhat(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        if (TileDistance(mapCharacter, npc.Dest) <= 3)
        {
            BeginActivity(mapCharacter); // close enough
            return;
        }
        if (npc.Legs >= 40 || npc.Retries >= 2)
        {
            Teleport(mapCharacter, npc.Dest); // trying too hard: going to jump there
            BeginActivity(mapCharacter);
            return;
        }
        if (!IsOnScreen(mapCharacter.PixelX, mapCharacter.PixelY) && !IsOnScreen(CellCenterX(npc.Dest.X), CellCenterY(npc.Dest.Y)))
        {
            npc.Retries = 100; // force a teleport on the next tick
            npc.WaitMs = 100;
            return;
        }
        var path = FindPath(mapCharacter, npc.Dest);
        if (path is null)
        {
            npc.Retries++; // failed: wait a second and try again
            npc.WaitMs = 1000;
            return;
        }
        npc.Legs++;
        npc.Retries = 0;
        StartPath(mapCharacter, path);
    }

    /// <summary>Arrived (or put there): set_schedule_type - start the activity itself.</summary>
    private void BeginActivity(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        npc.WalkingToSchedule = false;
        var entry = npc.Entry;
        if (entry is null) return;
        npc.Activity = entry.Type;
        npc.Center = (mapCharacter.Placement.X, mapCharacter.Placement.Y);
        npc.Phase = 0;
        npc.WasBlocked = false;
        switch (entry.Type)
        {
            case ScheduleType.Sleep:
                // Sleep_schedule: the nearest free bed within 24 tiles; none: lie down here.
                if (!GoToFurniture(mapCharacter, bed: true)) SetCharacterPose(mapCharacter, "Sleeping", mapCharacter.Facing);
                break;
            case ScheduleType.Sit:
                SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
                if (!GoToFurniture(mapCharacter, bed: false)) npc.WaitMs = 1000 + Rng.Next(1000); // Sit_schedule: none free, try again
                break;
            case ScheduleType.Dance:
                SetCharacterPose(mapCharacter, "Dancing", mapCharacter.Facing);
                npc.WaitMs = 2 * StdDelay;
                break;
            case ScheduleType.Loiter:
            case ScheduleType.Wander:
                SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
                npc.WaitMs = Rng.Next(2000);
                break;
            case ScheduleType.HorizPace:
            case ScheduleType.VertPace:
                SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
                npc.WaitMs = StdDelay;
                break;
            case ScheduleType.Custom:
                SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
                if (entry.CustomCommands.Count > 0)
                {
                    var trace = new List<string>();
                    EventCommandInterpreter.Run(entry.CustomCommands, _execContext, trace);
                }
                break;
            default: // Stand, Wait
                SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
                break;
        }
    }

    /// <summary>Loiter_schedule::now_what: a random spot within dist of the centre, then pause 2 std delays plus up to 2 s.</summary>
    private void LoiterNowWhat(MapCharacter mapCharacter, int distCells)
    {
        var npc = mapCharacter.Npc;
        int x = npc.Center.X - distCells + Rng.Next(2 * distCells + 1);
        int y = npc.Center.Y - distCells + Rng.Next(2 * distCells + 1);
        var path = InsideMap(x, y) ? FindPath(mapCharacter, (x, y)) : null;
        if (path is { Count: > 0 }) StartPath(mapCharacter, path);
        else npc.WaitMs = 2 * StdDelay + Rng.Next(2000);
    }

    /// <summary>Wander_schedule::now_what: a leg of up to 32 tiles, kept within 128 tiles of the centre, to a free spot.</summary>
    private void WanderNowWhat(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        int leg = CellsForTiles(32), dist = CellsForTiles(128);
        int x = mapCharacter.Placement.X - leg + Rng.Next(2 * leg + 1);
        int y = mapCharacter.Placement.Y - leg + Rng.Next(2 * leg + 1);
        x = Math.Clamp(x, npc.Center.X - dist, npc.Center.X + dist);
        y = Math.Clamp(y, npc.Center.Y - dist, npc.Center.Y + dist);
        x = Math.Clamp(x, 0, _map.Width - 1);
        y = Math.Clamp(y, 0, _map.Height - 1);
        var path = FindPath(mapCharacter, (x, y));
        if (path is { Count: > 0 }) StartPath(mapCharacter, path);
        else npc.WaitMs = 250 + Rng.Next(3000); // failed? try again a little later
    }

    /// <summary>Pace_schedule: step along the line until blocked, stand, turn round (two quarter turns), go back.</summary>
    private void PaceNowWhat(MapCharacter mapCharacter, bool horizontal)
    {
        var npc = mapCharacter.Npc;
        switch (npc.Phase)
        {
            case 0:
                npc.Phase = 1;
                npc.WaitMs = StdDelay;
                break;
            case 1:
            {
                if (npc.WasBlocked) { npc.WasBlocked = false; npc.Phase = 2; npc.WaitMs = StdDelay; break; }
                int dx = horizontal ? npc.PaceDirection : 0, dy = horizontal ? 0 : npc.PaceDirection;
                var next = (mapCharacter.Placement.X + dx, mapCharacter.Placement.Y + dy);
                if (InsideMap(next.Item1, next.Item2) && CanStandAt(mapCharacter, next, mapCharacter.Lift, null, out _))
                    StartPath(mapCharacter, new List<(int X, int Y)> { next });
                else { npc.Phase = 2; npc.WaitMs = StdDelay; }
                break;
            }
            case 2:
                npc.Phase = 3;
                SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
                npc.WaitMs = 2 * StdDelay;
                break;
            case 3:
                npc.Phase = 4;
                SetCharacterPose(mapCharacter, "Idle", QuarterTurn(mapCharacter.Facing));
                npc.WaitMs = 2 * StdDelay;
                break;
            default:
                npc.PaceDirection = -npc.PaceDirection;
                SetCharacterPose(mapCharacter, "Idle", horizontal
                    ? (npc.PaceDirection > 0 ? SpriteDirection.East : SpriteDirection.West)
                    : (npc.PaceDirection > 0 ? SpriteDirection.South : SpriteDirection.North));
                npc.Phase = 1;
                npc.WaitMs = 2 * StdDelay;
                break;
        }
    }

    /// <summary>Dance: turning to new directions on the spot in the Dancing pose, now and then stepping to a neighbouring cell near the centre.</summary>
    private void DanceNowWhat(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        var directions = new[] { SpriteDirection.North, SpriteDirection.East, SpriteDirection.South, SpriteDirection.West };
        if (Rng.Next(4) == 0)
        {
            var next = (npc.Center.X + Rng.Next(3) - 1, npc.Center.Y + Rng.Next(3) - 1);
            if (next != (mapCharacter.Placement.X, mapCharacter.Placement.Y) && InsideMap(next.Item1, next.Item2)
                && FindPath(mapCharacter, next) is { Count: > 0 } path)
            {
                StartPath(mapCharacter, path);
                return;
            }
        }
        SetCharacterPose(mapCharacter, "Dancing", directions[Rng.Next(directions.Length)]);
        npc.WaitMs = 2 * StdDelay + Rng.Next(4 * StdDelay);
    }

    private static SpriteDirection QuarterTurn(SpriteDirection facing) => facing switch
    {
        SpriteDirection.North => SpriteDirection.East,
        SpriteDirection.East => SpriteDirection.South,
        SpriteDirection.South => SpriteDirection.West,
        _ => SpriteDirection.North
    };

    // ---------- Walking ----------

    private void StartPath(MapCharacter mapCharacter, List<(int X, int Y)> path)
    {
        mapCharacter.Npc.Path = path;
        mapCharacter.Npc.PathIndex = 0;
    }

    private void StopWalking(MapCharacter mapCharacter)
    {
        mapCharacter.Npc.Path = null;
        mapCharacter.Npc.WaitMs = -1;
    }

    /// <summary>
    /// Walks towards the next cell of the path at the player's own speed,
    /// taking every step by the same Exult rules the player's steps follow
    /// (TryStepActor: 1 lift up or down, headroom, solids, the player in the way).
    /// </summary>
    private void MoveAlongPath(MapCharacter mapCharacter, double elapsedMs)
    {
        var npc = mapCharacter.Npc;
        if (npc.Path is null) return;
        if (npc.PathIndex >= npc.Path.Count) { FinishPath(mapCharacter); return; }
        var (cellX, cellY) = npc.Path[npc.PathIndex];
        double targetX = CellCenterX(cellX), targetY = CellCenterY(cellY);
        double remainingX = targetX - mapCharacter.PixelX, remainingY = targetY - mapCharacter.PixelY;
        // Cardinal steps, one axis at a time, like the player.
        bool alongX = Math.Abs(remainingX) > 0.01;
        double remaining = alongX ? remainingX : remainingY;
        var facing = alongX ? (remaining > 0 ? SpriteDirection.East : SpriteDirection.West) : (remaining > 0 ? SpriteDirection.South : SpriteDirection.North);
        SetCharacterPose(mapCharacter, mapCharacter.Npc.Activity == ScheduleType.Dance ? "Dancing" : "Walking", facing);

        double distance = Math.Min(Math.Abs(remaining), PlayerTilesPerSecond * _tileWidth * elapsedMs / 1000.0);
        double newX = mapCharacter.PixelX + (alongX ? Math.Sign(remaining) * distance : 0);
        double newY = mapCharacter.PixelY + (alongX ? 0 : Math.Sign(remaining) * distance);

        var newFoot = ActorFootprintAt(newX, newY, mapCharacter.Size);
        if (newFoot != CharacterFootprint(mapCharacter))
        {
            if (!TryStepActor(newFoot, mapCharacter.Lift, mapCharacter.Size.Z, out int newLift, out _, mapCharacter.Placement))
            {
                // A closed, unlocked door in the way: open it and walk on (actions.cc open_door); closed again behind them.
                if (NpcOpensDoor(mapCharacter, newFoot)) return;
                // Actor::is_really_blocked: someone in the way is asked to move aside (or swap places); then the step is tried again.
                int stepX = alongX ? Math.Sign(remaining) : 0, stepY = alongX ? 0 : Math.Sign(remaining);
                bool cleared = CharacterInTheWay(newFoot, mapCharacter.Lift, mapCharacter.Size.Z, mapCharacter) is { } other
                    ? MoveAside(other, stepX, stepY, mapCharacter)
                    : PlayerInTheWay(newFoot, mapCharacter.Lift, mapCharacter.Size.Z) && MovePlayerAside(mapCharacter, stepX, stepY);
                if (cleared) return; // carries on next frame (or, swapped, thinks again from its new place)
                // Blocked on the way (someone stepped in front, say): stop and think again shortly.
                npc.Path = null;
                npc.WasBlocked = true;
                if (npc.WalkingToSchedule) npc.Retries++;
                SetCharacterPose(mapCharacter, npc.Activity == ScheduleType.Dance ? "Dancing" : "Idle", mapCharacter.Facing);
                npc.WaitMs = npc.Activity is ScheduleType.HorizPace or ScheduleType.VertPace ? StdDelay : 500 + Rng.Next(500);
                return;
            }
            mapCharacter.Lift = newLift;
        }
        mapCharacter.PixelX = newX;
        mapCharacter.PixelY = newY;
        var oldCell = (mapCharacter.Placement.X, mapCharacter.Placement.Y);
        mapCharacter.Placement.X = (int)Math.Floor(newX / _tileWidth);
        mapCharacter.Placement.Y = (int)Math.Floor(newY / _tileHeight);
        PositionCharacterVisual(mapCharacter);
        if (oldCell != (mapCharacter.Placement.X, mapCharacter.Placement.Y))
        {
            CharacterChangedCell(mapCharacter); // map events set off by characters, like the player
            if (npc.Path is null) return; // an event moved it (teleporter) or changed what it's doing
        }

        if (Math.Abs(targetX - newX) < 0.01 && Math.Abs(targetY - newY) < 0.01)
        {
            npc.PathIndex++;
            if (npc.PathIndex >= npc.Path.Count) FinishPath(mapCharacter);
        }
    }

    private void FinishPath(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        npc.Path = null;
        if (ArriveAtFurniture(mapCharacter)) return; // was heading for a seat or bed: sits / lies down
        var pose = npc.Activity switch { ScheduleType.Dance => "Dancing", _ => "Idle" };
        SetCharacterPose(mapCharacter, pose, mapCharacter.Facing);
        npc.WaitMs = npc.Activity switch
        {
            ScheduleType.Loiter => 2 * StdDelay + Rng.Next(2000),
            ScheduleType.Wander => Rng.Next(2000),
            ScheduleType.Dance => 2 * StdDelay,
            _ => StdDelay // walking to a schedule, pacing: carry straight on
        };
    }

    private void Teleport(MapCharacter mapCharacter, (int X, int Y) cell)
    {
        StopWalking(mapCharacter);
        mapCharacter.PixelX = CellCenterX(cell.X);
        mapCharacter.PixelY = CellCenterY(cell.Y);
        mapCharacter.Placement.X = cell.X;
        mapCharacter.Placement.Y = cell.Y;
        var foot = CharacterFootprint(mapCharacter);
        mapCharacter.Lift = FloorBelow(WorldBodies().Where(b => b.Solid && b.Overlaps(foot) && !ReferenceEquals(b.Entity, mapCharacter.Placement)), int.MaxValue);
        SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
        PositionCharacterVisual(mapCharacter);
    }

    // ---------- Path finding (Exult's A*, on map cells) ----------

    /// <summary>
    /// A* from the character's cell to the goal over map cells, each step
    /// checked with the same walking rules as a real step (so stairs of 1 lift
    /// are climbed, drops of more than 1 avoided). If the goal itself is taken
    /// (someone standing on it), a neighbouring cell will do - Exult's walk to
    /// a schedule is satisfied within 3 tiles. Null if there's no way there.
    /// </summary>
    private List<(int X, int Y)>? FindPath(MapCharacter mapCharacter, (int X, int Y) goal)
    {
        var start = (mapCharacter.Placement.X, mapCharacter.Placement.Y);
        if (start == goal) return new List<(int X, int Y)>();
        var world = WorldBodiesForPathing(); // closed unlocked doors will be opened on the way (Exult paths.cc)
        bool goalFree = CanStandAt(mapCharacter, goal, mapCharacter.Lift, world, out _, anyLift: true);

        var open = new PriorityQueue<(int X, int Y), int>();
        var cameFrom = new Dictionary<(int X, int Y), (int X, int Y)>();
        var cost = new Dictionary<(int X, int Y), int> { [start] = 0 };
        var liftAt = new Dictionary<(int X, int Y), int> { [start] = mapCharacter.Lift };
        open.Enqueue(start, Heuristic(start, goal));
        int expansions = 0;
        while (open.Count > 0 && expansions++ < 6000)
        {
            var current = open.Dequeue();
            bool done = current == goal || (!goalFree && Math.Max(Math.Abs(current.X - goal.X), Math.Abs(current.Y - goal.Y)) <= 1 && current != start);
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
                if (!CanStandAt(mapCharacter, next, liftAt[current], world, out int lift)) continue;
                cost[next] = newCost;
                liftAt[next] = lift;
                cameFrom[next] = current;
                open.Enqueue(next, newCost + Heuristic(next, goal));
            }
        }
        return null;
    }

    private static int Heuristic((int X, int Y) a, (int X, int Y) b) => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

    /// <summary>Whether the character could step onto that cell from a lift (the walking rules), and at what lift it would stand.</summary>
    private bool CanStandAt(MapCharacter mapCharacter, (int X, int Y) cell, int fromLift, List<Body>? world, out int lift, bool anyLift = false)
    {
        var foot = ActorFootprintAt(CellCenterX(cell.X), CellCenterY(cell.Y), mapCharacter.Size);
        if (anyLift)
        {
            lift = 0;
            var here = (world ?? WorldBodies()).Where(b => b.Solid && b.Overlaps(foot) && !ReferenceEquals(b.Entity, mapCharacter.Placement)).ToList();
            if (PlayerBody() is { } playerBody && playerBody.Overlaps(foot)) return false;
            return IsClear(here, FloorBelow(here, int.MaxValue), mapCharacter.Size.Z);
        }
        return TryStepActor(foot, fromLift, mapCharacter.Size.Z, out lift, out _, mapCharacter.Placement, world);
    }

    // ---------- Geometry ----------

    private double CellCenterX(int cellX) => cellX * _tileWidth + _tileWidth / 2.0;
    private double CellCenterY(int cellY) => cellY * _tileHeight + _tileHeight / 2.0;
    private bool InsideMap(int x, int y) => x >= 0 && y >= 0 && x < _map.Width && y < _map.Height;

    /// <summary>Exult tiles (8px) between the character and a cell, the larger of the two axes (Exult's distance()).</summary>
    private int TileDistance(MapCharacter mapCharacter, (int X, int Y) cell) =>
        (int)Math.Round(Math.Max(Math.Abs(CellCenterX(cell.X) - mapCharacter.PixelX), Math.Abs(CellCenterY(cell.Y) - mapCharacter.PixelY)) / SpriteSizing.BaseUnit);

    /// <summary>A distance in Exult tiles as map cells (at least 1).</summary>
    private int CellsForTiles(int tiles) => Math.Max(1, (int)Math.Round(tiles * SpriteSizing.BaseUnit / (double)_tileWidth));

    /// <summary>On the screen, enlarged by 6 tiles all round (Walk_to_schedule's screen.enlarge(6)).</summary>
    private bool IsOnScreen(double pixelX, double pixelY)
    {
        double margin = 6 * SpriteSizing.BaseUnit;
        return pixelX >= _cameraX - margin && pixelX <= _cameraX + ViewportWidth + margin
            && pixelY >= _cameraY - margin && pixelY <= _cameraY + ViewportHeight + margin;
    }

    /// <summary>What a character is up to, for identifying it while testing.</summary>
    private string DescribeActivity(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        if (mapCharacter.Character.IsInParty) return "in the party (no schedule)";
        if (npc.WalkingToSchedule && npc.Entry is { } next) return $"walking to {Schedules.Describe(next.Type)} at ({npc.Dest.X}, {npc.Dest.Y})";
        if (npc.Activity is { } activity)
            return activity == ScheduleType.Custom && !string.IsNullOrEmpty(npc.Entry?.CustomName) ? $"{npc.Entry!.CustomName} (custom)" : Schedules.Describe(activity);
        return mapCharacter.Character.Schedules.Count == 0 ? "no schedule" : "waiting for its schedule";
    }
}
