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
        /// <summary>The When... rule it's following now, instead of its timetable (GameTestWindow.Zones.cs).</summary>
        public ScheduleRule? Rule;
        public double RuleCheckMs = 2000;
        /// <summary>The schedule spot (cell) and the "old_schedule_loc" Exult compares against.</summary>
        public (int X, int Y) Dest;
        public (int X, int Y)? OldScheduleLoc;
        /// <summary>Where it was placed in the Map Editor - the spot of entries set to "placed spot".</summary>
        public (int X, int Y) Home;
        /// <summary>Loiter/wander/dance centre: where the activity began.</summary>
        public (int X, int Y) Center;
        public int Retries, Legs, Phase, PaceDirection = 1;
        /// <summary>Walking to a schedule spot: the nearest it has got so far (Exult tiles) - legs that get no nearer count as tries.</summary>
        public int BestDistance = int.MaxValue;
        // Eating (GameTestWindow.Meals.cs).
        public MealStep Meal;
        public ObjectInstance? MealFood;
        public double MealCheckMs = 2000;
        public readonly HashSet<ObjectInstance> ServedDishes = new(ReferenceEqualityComparer.Instance);
        // Waiting at table (the Waiter schedule).
        public WaiterStep Waiter;
        public MapCharacter? WaiterCustomer;
        public int WaiterDishId = -1;
        /// <summary>Milliseconds until the activity's next now_what; negative = nothing pending.</summary>
        public double WaitMs = -1;
        public List<(int X, int Y)>? Path;
        public int PathIndex;
        public bool WasBlocked;
        /// <summary>A flier climbing or dropping: time toward its next lift.</summary>
        public double ClimbMs;
        /// <summary>The map event under it, for By Enter / By Exit / By Stay like the player's.</summary>
        public GameEvent? EventUnder;

        // Combat (GameTestWindow.Combat.cs): Exult's Combat_schedule state.
        public bool InCombat, Fleeing, Hounding;
        /// <summary>Exult's dormant: off screen and more than a screen away, it does nothing till it's on screen again (GameTestWindow.Perf.cs).</summary>
        public bool Dormant;
        public CharacterDefinition? Target, FleeFrom, Oppressor;
        public int DexPoints, Failures, FleeCount;
        /// <summary>Approach_actor_action: chasing the target, and the cell it stood on when the chase began (moved more than 2 tiles = think again).</summary>
        public bool Chasing;
        public (int X, int Y) ChaseFrom;
    }

    private GameClock _clock = new();
    private static readonly Random Rng = new();
    private const int StdDelay = GameClock.StdDelayMs;
    /// <summary>How long a character stands still at least each time it stops walking (so it can be clicked on).</summary>
    private const int StopPauseMs = 3500;

    // ---------- Clock ----------

    private void UpdateClockAndCharacters(double elapsedMs)
    {
        int hourChanges = _clock.Advance(elapsedMs);
        if (hourChanges > 0) using (Measure("ScheduleNpcs")) ScheduleNpcs(_clock.Hour);
        using (Measure("UpdateVitals")) UpdateVitals(elapsedMs, hourChanges); // hunger, healing, poison, ailments, tile effects - everyone
        using (Measure("UpdateCombat")) UpdateCombat(elapsedMs); // the player's blows; who sees whom (enemies on sight, fear, allies under attack)
        using (Measure("UpdateBleeding")) UpdateBleeding(elapsedMs); // drips from whoever is bleeding, and temporary puddles far away cleared
        foreach (var character in AllLivingCharacters().ToList()) UpdateUnconscious(character, elapsedMs); // the knocked out mend and get up
        using (Measure("UpdateObjects")) UpdateObjects(elapsedMs); // lights burning, tiles and traps underfoot, doors closed behind NPCs
        using (Measure("UpdateScriptActions")) UpdateScriptActions(elapsedMs); // event commands being carried out: walking there, sitting, picking a lock (GameTestWindow.Actions.cs)
        UpdateCinema(elapsedMs); // a Cinema Mode whose event has died gives the controls back (GameTestWindow.Cinema.cs)
        using (Measure("UpdateAlwaysPages")) UpdateAlwaysPages(elapsedMs); // characters' Always pages, run on their own (GameTestWindow.AlwaysPages.cs)
        ShowClock();
        foreach (var mapCharacter in _mapCharacters.ToList())
        {
            if (IsShowingMessage) break; // a dialog box just went up: everything stops right here
            FallIfGrounded(mapCharacter); // a flier knocked out falls (GameTestWindow.Movement.cs)
            if (KeepDormant(mapCharacter)) continue; // far off screen: does nothing (Exult)
            if (FrozenInTime(mapCharacter)) continue; // Time Stop (GameTestWindow.Spells.cs)
            using (Measure("UpdateNpc")) UpdateNpc(mapCharacter, elapsedMs);
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
            if (IsHeldByEvent(mapCharacter.Character)) { _heldMissedHour.Add(mapCharacter.Character); continue; } // an event still has them: their new hour waits till it's over
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
            mapCharacter.Npc.EventUnder = EventAt(mapCharacter.Placement.X, mapCharacter.Placement.Y, mapCharacter.Lift); // standing there from the start doesn't set it off
            UpdateSchedule(mapCharacter, _clock.Period, -1);
        }
        ShowClock();
    }

    /// <summary>The clock, and the player's own health and food, in the status bar.</summary>
    private void ShowClock() => ClockText.Text = (_player is null ? _clock.ToString() : $"{_clock}   |   {DescribeVitals(_player)}") + (_combatMode ? "   |   COMBAT" : "");

    private (int X, int Y) SpotOf(MapCharacter mapCharacter, ScheduleEntry entry) =>
        ZoneSpotOf(mapCharacter, entry) ?? // a zone, or the nearest of a purpose (GameTestWindow.Zones.cs)
        (entry.X >= 0 && entry.Y >= 0 ? (Math.Clamp(entry.X, 0, _map.Width - 1), Math.Clamp(entry.Y, 0, _map.Height - 1)) : mapCharacter.Npc.Home);

    /// <summary>Npc_actor::update_schedule.</summary>
    private void UpdateSchedule(MapCharacter mapCharacter, int period, int delayMs)
    {
        var schedules = mapCharacter.Character.Schedules;
        if (mapCharacter.Character.IsInParty || mapCharacter.Character.IsTestPlayer || schedules.Count == 0 || IsDead(mapCharacter.Character)) return;
        if (mapCharacter.Npc.Meal != MealStep.None) return; // eating: its schedule waits till it's full
        if (mapCharacter.Npc.Rule is not null) return; // a When... rule holds: its timetable waits till it's over
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
        // Exult puts a character straight there when it and the spot are both off-screen; here it always walks
        // (the designer's choice) - whatever happens on the way happens: a fight, a trap, a locked door.
        npc.WalkingToSchedule = true;
        npc.Retries = 0;
        npc.Legs = 0;
        npc.BestDistance = int.MaxValue;
        // Walk_to_schedule: first delay 0-5 s when random, then 0-1 s more before setting off.
        npc.WaitMs = (delayMs >= 0 ? delayMs : Rng.Next(5000)) + Rng.Next(1000);
    }

    // ---------- What next (each schedule's now_what) ----------

    private void UpdateNpc(MapCharacter mapCharacter, double elapsedMs)
    {
        var npc = mapCharacter.Npc;
        if (IsIncapacitated(mapCharacter.Character)) return; // dead, asleep or paralyzed: does nothing at all
        if (IsScripted(mapCharacter.Character))
        {
            // Carrying out an event command: its schedule, hunger and rules wait; only its walking goes on.
            if (npc.Path is not null) MoveAlongPath(mapCharacter, elapsedMs);
            return;
        }
        if (IsHeldByEvent(mapCharacter.Character) && !npc.InCombat)
        {
            // Left there by an event that is still going: it stays (following its leader, if it has one) - its schedule waits till the event is over.
            if (npc.Path is not null) MoveAlongPath(mapCharacter, elapsedMs);
            return;
        }
        CheckScheduleRules(mapCharacter, elapsedMs); // its When... rules: hungry, injured... (GameTestWindow.Zones.cs)
        CheckHunger(mapCharacter, elapsedMs); // hungry: off to eat (GameTestWindow.Meals.cs)
        if (npc.Path is not null)
        {
            MoveAlongPath(mapCharacter, elapsedMs);
            // Stopped walking (there, or blocked): it stands a moment before going on, long enough to be clicked or
            // double-clicked - not in a fight, running away or hounding someone.
            if (npc.Path is null && npc.WaitMs >= 0 && !npc.InCombat && !npc.Fleeing && !npc.Hounding && !IsScripted(mapCharacter.Character))
                npc.WaitMs = Math.Max(npc.WaitMs, StopPauseMs);
            return;
        }
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
        if (npc.Meal != MealStep.None) { MealNowWhat(mapCharacter); return; } // eating comes before its schedule
        if (npc.Hounding) { HoundNowWhat(mapCharacter); return; }
        if (!IsWorkSchedule(npc.Activity) && NeedsTidying(mapCharacter.Character) && !WorkOf(mapCharacter.Character).RoundRunning) { TidyUpAfterWork(mapCharacter); return; } // the work is over: tools back first (GameTestWindow.Work.cs)
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
            case ScheduleType.Waiter: WaiterNowWhat(mapCharacter); break;
            case ScheduleType.EatAtInn: InnCustomerNowWhat(mapCharacter); break;
            case ScheduleType.Farm or ScheduleType.Miner or ScheduleType.Blacksmith or ScheduleType.Bake or ScheduleType.Sew or ScheduleType.Lab:
                WorkNowWhat(mapCharacter); // GameTestWindow.Work.cs
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
        // No teleporting (Exult jumps there after 2 failed tries or 40 legs): it keeps walking, a leg at a time.
        // A leg that gets it no nearer counts as a try; after 5 such tries the spot can't be reached (a locked
        // door, a wall all round) - and only then, as Exult does, it's put there.
        int distance = TileDistance(mapCharacter, npc.Dest);
        if (distance < npc.BestDistance) { npc.BestDistance = distance; npc.Retries = 0; }
        else npc.Retries++;
        if (npc.Retries >= 5)
        {
            Teleport(mapCharacter, npc.Dest); // no way there: Exult's jump
            BeginActivity(mapCharacter);
            return;
        }
        var path = FindPath(mapCharacter, npc.Dest, allowPartial: true); // a long way: as far toward it as can be planned, then on from there
        if (path is not { Count: > 0 })
        {
            npc.WaitMs = 1000 + 500 * npc.Retries; // nothing to walk yet (someone in the way): try again shortly
            return;
        }
        npc.Legs++;
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
                if (!GoToFurniture(mapCharacter, bed: true)) { SetCharacterPose(mapCharacter, "Sleeping", mapCharacter.Facing); LockHouseDoorsForNight(mapCharacter); }
                break;
            case ScheduleType.Waiter:
                SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
                npc.Waiter = WaiterStep.FindCustomer;
                npc.WaiterCustomer = null;
                npc.WaitMs = 1000;
                break;
            case ScheduleType.EatAtInn:
                SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
                if (!GoToFurniture(mapCharacter, bed: false)) npc.WaitMs = 1000 + Rng.Next(1000);
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
            case ScheduleType.Farm or ScheduleType.Miner or ScheduleType.Blacksmith or ScheduleType.Bake or ScheduleType.Sew or ScheduleType.Lab:
                SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
                WorkOf(mapCharacter.Character).SaidIdle = false;
                npc.WaitMs = 500 + Rng.Next(1000);
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
        mapCharacter.Npc.Chasing = false; // a chase says so itself, after starting its path
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
        // A flier first climbs or drops in its own cell to the height its way goes on at (down outside a door, up into the open sky).
        if (IsFlier(mapCharacter) && PlannedFlyLift(mapCharacter) is { } wantLift && mapCharacter.Lift != wantLift && ClimbToward(mapCharacter, wantLift, elapsedMs)) return;
        double targetX = CellCenterX(cellX), targetY = CellCenterY(cellY);
        if (_pathPoints.TryGetValue(npc.Path, out var points) && npc.PathIndex < points.Count) (targetX, targetY) = points[npc.PathIndex]; // its spot in that cell (a doorway off the middle)
        var (remainingX, remainingY) = WrappedDelta(targetX - mapCharacter.PixelX, targetY - mapCharacter.PixelY); // the next cell can be over the seam of a map that wraps around
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
                // A locked door of a side it's hostile to: break it down.
                if (NpcBashesDoor(mapCharacter, newFoot, elapsedMs)) return;
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
                SetCharacterPose(mapCharacter, npc.Activity == ScheduleType.Dance && !npc.InCombat ? "Dancing" : "Idle", mapCharacter.Facing);
                // In a fight (Path_walking_actor_action stops when blocked): think again on the next standard delay, not the schedule's pause.
                npc.Chasing = false;
                npc.WaitMs = npc.InCombat || npc.Activity is ScheduleType.HorizPace or ScheduleType.VertPace ? StdDelay : 500 + Rng.Next(500);
                return;
            }
            mapCharacter.Lift = newLift;
        }
        WrapPixel(ref newX, ref newY); // over the edge of a map that wraps around: on the other side (GameTestWindow.Wrap.cs)
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
            if (ChaseCheck(mapCharacter)) return; // in reach, or the target moved off: strike / think again now
            npc.PathIndex++;
            if (npc.PathIndex >= npc.Path.Count) FinishPath(mapCharacter);
        }
    }

    private void FinishPath(MapCharacter mapCharacter)
    {
        var npc = mapCharacter.Npc;
        npc.Path = null;
        if (npc.InCombat)
        {
            // A chase or a run away done: Combat_schedule::now_what at once - no schedule pause in a fight.
            npc.Chasing = false;
            SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
            npc.WaitMs = 0;
            return;
        }
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
        mapCharacter.Lift = StandingLiftOnFloor(foot, MapFloors.FloorOfLift(mapCharacter.Lift), mapCharacter.Placement); // on the floor they were on
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
    /// <param name="allowPartial">No way found (too far to plan in one go, or blocked): a path to the reachable cell nearest the goal instead of null - a leg of a long walk.</param>
    /// <param name="besideOnly">Heading for a chair or bed: end beside it (never on top of it, never across a corner).</param>
    /// <param name="goalFloor">The goal is on this floor: a cell under an upper floor and the same cell on it are different places (Exult paths in 3D, Tile_coord with its lift), and the way there goes up or down the stairs. Null: the goal cell on whatever floor it's reached at.</param>
    private List<(int X, int Y)>? FindPath(MapCharacter mapCharacter, (int X, int Y) goal, bool allowPartial = false, bool besideOnly = false, int? goalFloor = null)
    {
        using var measured = Measure("FindPath");
        if (IsFlier(mapCharacter)) // a flier's way through the air (GameTestWindow.Movement.cs) - in a fight, down beside its foe
            return mapCharacter.Npc.InCombat && mapCharacter.Npc.Target is { } foe
                ? FindFlyPath(mapCharacter, goal, allowPartial, besideOnly: true, goalFloor: MapFloors.FloorOfLift(LiftOf(foe)))
                : FindFlyPath(mapCharacter, goal, allowPartial, besideOnly, goalFloor);
        int FloorKey(int lift) => goalFloor is null ? 0 : MapFloors.FloorOfLift(lift); // floors told apart only when one is asked for
        int wantFloor = goalFloor ?? 0;
        (int X, int Y, int F) start = (mapCharacter.Placement.X, mapCharacter.Placement.Y, FloorKey(mapCharacter.Lift));
        if ((start.X, start.Y) == goal && start.F == wantFloor && !besideOnly) return new List<(int X, int Y)>();
        var world = WorldBodiesForPathing(mapCharacter); // closed unlocked doors will be opened on the way (Exult paths.cc); hostile ones broken down
        bool goalFree = !besideOnly && (goalFloor is { } gf
            ? CanStandAt(mapCharacter, goal, MapFloors.BaseLift(gf), world, out int goalLift) && MapFloors.FloorOfLift(goalLift) == gf
            : CanStandAt(mapCharacter, goal, mapCharacter.Lift, world, out _, anyLift: true));
        int H((int X, int Y, int F) node) => Heuristic((node.X, node.Y), goal) + (goalFloor is null ? 0 : Math.Abs(node.F - wantFloor) * MapFloors.LiftsPerFloor);
        // The player is no wall to plan around (Exult): the way goes through them, and they're asked to step aside when the walker gets there (MoveAlongPath).
        bool wasPlanning = _planningThroughPlayer;
        _planningThroughPlayer = true;
        try { return FindPathAStar(mapCharacter, goal, allowPartial, goalFloor, start, wantFloor, world, goalFree, besideOnly, H); }
        finally { _planningThroughPlayer = wasPlanning; }
    }

    /// <summary>The paths planned now go through the player (they step aside when reached): only the player's own body is let through.</summary>
    private bool _planningThroughPlayer;

    private List<(int X, int Y)>? FindPathAStar(MapCharacter mapCharacter, (int X, int Y) goal, bool allowPartial, int? goalFloor, (int X, int Y, int F) start, int wantFloor,
        BodySource world, bool goalFree, bool besideOnly, Func<(int X, int Y, int F), int> H)
    {
        int FloorKey(int lift) => goalFloor is null ? 0 : MapFloors.FloorOfLift(lift);
        var open = new PriorityQueue<(int X, int Y, int F), int>();
        var cameFrom = new Dictionary<(int X, int Y, int F), (int X, int Y, int F)>();
        var cost = new Dictionary<(int X, int Y, int F), int> { [start] = 0 };
        var liftAt = new Dictionary<(int X, int Y, int F), int> { [start] = mapCharacter.Lift };
        // Where in each cell the walker stands: its middle, or - when walls built in 8px steps leave the way between two cells
        // off the middles (a doorway between wall pieces) - the nearest 8px spot in it that can be stood on and walked to.
        var pointAt = new Dictionary<(int X, int Y, int F), (double X, double Y)> { [start] = (mapCharacter.PixelX, mapCharacter.PixelY) };
        open.Enqueue(start, H(start));
        int expansions = 0;
        var nearest = start;
        int nearestH = H(start);
        List<(int X, int Y)> PathTo((int X, int Y, int F) end)
        {
            var path = new List<(int X, int Y)>();
            var points = new List<(double X, double Y)>();
            bool offMiddle = false;
            for (var step = end; step != start; step = cameFrom[step])
            {
                path.Add((step.X, step.Y));
                var point = pointAt[step];
                points.Add(point);
                if (Math.Abs(point.X - CellCenterX(step.X)) > 0.01 || Math.Abs(point.Y - CellCenterY(step.Y)) > 0.01) offMiddle = true;
            }
            path.Reverse();
            points.Reverse();
            if (offMiddle) _pathPoints.AddOrUpdate(path, points);
            return path;
        }
        while (open.Count > 0 && expansions++ < 6000) // the way round can be far longer than the straight way (out of a house, round to its door)
        {
            var current = open.Dequeue();
            int h = H(current);
            if (h < nearestH) { nearest = current; nearestH = h; }
            // A taken goal (someone on it, a chair): right beside it - not across a corner, where a table can be in between.
            bool done = current.F == wantFloor
                && (((current.X, current.Y) == goal && goalFree) || (!goalFree && Heuristic((current.X, current.Y), goal) == 1 && (current != start || besideOnly)));
            if (done) return PathTo(current);
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var stepTo = (current.X + dx, current.Y + dy); // the walk itself, measured from here (past the edge of a map that wraps around: over the seam)
                var cell = WrapCell(stepTo);
                if (!InsideMap(cell.Item1, cell.Item2)) continue;
                if (goalFloor is null && cost.TryGetValue((cell.Item1, cell.Item2, 0), out int reached) && reached <= cost[current] + 1) continue; // no floor asked for: one place per cell, so skip the walking rules for a cell already reached as cheaply
                // The walk from its spot in this cell to its spot in the next: the next cell's middle first, else its other 8px spots, nearest first.
                var (fromX, fromY) = pointAt[current];
                int lift = 0;
                (double X, double Y)? spot = null;
                foreach (var (ox, oy) in SpotsInCell())
                {
                    double tx = CellCenterX(stepTo.Item1) + ox, ty = CellCenterY(stepTo.Item2) + oy;
                    if (goalFloor is not null)
                    {
                        // To another floor: the stairs climb several lifts a cell, 1 a step - only the walk itself, step by step, gets up or down them (a jump cell to cell is a cliff).
                        if (!CanWalkBetweenPoints(mapCharacter, fromX, fromY, tx, ty, liftAt[current], world, out lift)) continue;
                    }
                    else
                    {
                        if (!TryStepActor(ActorFootprintAt(tx, ty, mapCharacter.Size), liftAt[current], mapCharacter.Size.Z, out lift, out _, mapCharacter.Placement, world)) continue;
                        if (!CanWalkBetweenPoints(mapCharacter, fromX, fromY, tx, ty, liftAt[current], world, out _)) continue; // the walk itself, not just where it ends (GameTestWindow.Sweep.cs)
                    }
                    spot = (tx, ty);
                    break;
                }
                if (spot is null) continue;
                var next = (cell.Item1, cell.Item2, FloorKey(lift));
                int newCost = cost[current] + 1;
                if (cost.TryGetValue(next, out int known) && known <= newCost) continue;
                cost[next] = newCost;
                liftAt[next] = lift;
                pointAt[next] = (spot.Value.X - (stepTo.Item1 - cell.Item1) * _tileWidth, spot.Value.Y - (stepTo.Item2 - cell.Item2) * _tileHeight); // over the seam of a map that wraps around: on the cell's own side
                cameFrom[next] = current;
                open.Enqueue(next, newCost + H(next));
            }
        }
        if (!allowPartial || nearest == start) return null;
        return PathTo(nearest);
    }

    /// <summary>A path's spots in its cells (FindPathAStar), when not all are the cells' middles - MoveAlongPath heads for them.</summary>
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<List<(int X, int Y)>, List<(double X, double Y)>> _pathPoints = new();

    private List<(double X, double Y)>? _spotsInCell;
    /// <summary>Offsets from a cell's middle where a walker may stand in it: the middle, then each 8px step off it within the cell, nearest first.</summary>
    private List<(double X, double Y)> SpotsInCell()
    {
        if (_spotsInCell is not null) return _spotsInCell;
        var spots = new List<(double X, double Y)>();
        int rx = _tileWidth / SpriteSizing.BaseUnit / 2, ry = _tileHeight / SpriteSizing.BaseUnit / 2;
        for (int j = -ry; j <= ry; j++)
            for (int i = -rx; i <= rx; i++)
            {
                double x = i * SpriteSizing.BaseUnit, y = j * SpriteSizing.BaseUnit;
                if (Math.Abs(x) >= _tileWidth / 2.0 || Math.Abs(y) >= _tileHeight / 2.0) continue; // in the cell
                spots.Add((x, y));
            }
        return _spotsInCell = spots.OrderBy(p => Math.Abs(p.X) + Math.Abs(p.Y)).ThenBy(p => Math.Abs(p.Y)).ToList();
    }

    private int Heuristic((int X, int Y) a, (int X, int Y) b) => Math.Abs(CellsAcross(a.X - b.X)) + Math.Abs(CellsUpDown(a.Y - b.Y)); // the short way round a map that wraps around

    /// <summary>Whether the character could step onto that cell from a lift (the walking rules), and at what lift it would stand.</summary>
    private bool CanStandAt(MapCharacter mapCharacter, (int X, int Y) cell, int fromLift, BodySource? world, out int lift, bool anyLift = false)
    {
        var foot = ActorFootprintAt(CellCenterX(cell.X), CellCenterY(cell.Y), mapCharacter.Size);
        if (anyLift)
        {
            lift = 0;
            var here = SolidsOverlapping(foot, mapCharacter.Placement, world).ToList();
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
        WrappedDelta(CellCenterX(cell.X) - mapCharacter.PixelX, CellCenterY(cell.Y) - mapCharacter.PixelY) is var (dx, dy) ? (int)Math.Round(Math.Max(Math.Abs(dx), Math.Abs(dy)) / SpriteSizing.BaseUnit) : 0;

    /// <summary>A distance in Exult tiles as map cells (at least 1).</summary>
    private int CellsForTiles(int tiles) => Math.Max(1, (int)Math.Round(tiles * SpriteSizing.BaseUnit / (double)_tileWidth));

    /// <summary>On the screen, enlarged by 6 tiles all round (Walk_to_schedule's screen.enlarge(6)).</summary>
    private bool IsOnScreen(double pixelX, double pixelY)
    {
        double margin = 6 * SpriteSizing.BaseUnit;
        foreach (var (dx, dy) in WrapShifts()) // a map that wraps around: seen over the seam too
        {
            double x = pixelX + dx, y = pixelY + dy;
            if (x >= _cameraX - margin && x <= _cameraX + ViewportWidth + margin
                && y >= _cameraY - margin && y <= _cameraY + ViewportHeight + margin) return true;
        }
        return false;
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
