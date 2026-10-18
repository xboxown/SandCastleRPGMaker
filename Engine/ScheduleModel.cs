namespace SandcastleRpgMaker.Engine;

/// <summary>
/// What a character does during a schedule period - Exult's Schedule_types
/// (schedule.h), keeping Exult's own numbers. Only the activities this
/// engine can actually carry out are offered; the rest of Exult's list
/// (farm, miner, blacksmith, bake, sew, tend shop...) depend on U7-specific
/// objects and come later. Custom is Exult's "first_scripted_schedule":
/// an activity defined by your own event commands.
/// </summary>
public enum ScheduleType
{
    HorizPace = 1,
    VertPace = 2,
    Dance = 4,
    Stand = 10,
    Loiter = 11,
    Wander = 12,
    Sleep = 14,
    Wait = 15,
    /// <summary>Exult's sit schedule (Schedule::sit): finds the nearest free chair and sits in it.</summary>
    Sit = 16,
    /// <summary>Exult's waiter schedule (Schedule::waiter): serves the customers sitting in the inn - asks their order, fetches it, puts it on the table in front of them.</summary>
    Waiter = 23,
    /// <summary>Exult's eat at inn schedule (Schedule::eat_at_inn): sits in a chair and eats what's served, asking for more.</summary>
    EatAtInn = 26,
    Custom = 0x80
}

/// <summary>
/// One line of a character's daily schedule - Exult's Schedule_change:
/// from the start of a 3-hour period (0 = midnight, 1 = 3 AM ... 7 = 9 PM)
/// the character goes to Location and does Type there. Exult keeps at most
/// one change per period; a period with none simply continues the last.
/// </summary>
public class ScheduleEntry
{
    /// <summary>0-7: the 3-hour period this takes effect (Exult: "Time*3hours").</summary>
    public int Period { get; set; }
    public ScheduleType Type { get; set; } = ScheduleType.Stand;
    /// <summary>The map cell to go to; -1 = where the character was placed in the Map Editor.</summary>
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    /// <summary>Custom only: a name for the activity, shown in the editor.</summary>
    public string CustomName { get; set; } = string.Empty;
    /// <summary>Custom only: the event commands run when the character arrives and starts this activity.</summary>
    public List<EventCommand> CustomCommands { get; set; } = new();
    /// <summary>Goes to this zone (Map Editor > Mode > Zones) instead of a cell; -1 = none.</summary>
    public int ZoneId { get; set; } = -1;
    /// <summary>Goes to the nearest zone with this purpose ("Pub") instead of a cell; "" = none.</summary>
    public string ZonePurpose { get; set; } = string.Empty;

    public string DescribePlace() =>
        ZoneId >= 0 ? $"zone #{ZoneId}" : ZonePurpose.Length > 0 ? $"the nearest {ZonePurpose}" : X >= 0 ? $"cell ({X}, {Y})" : "its placed spot";
}

public static class Schedules
{
    public static readonly string[] PeriodNames = { "Midnight", "3 AM", "6 AM", "9 AM", "Noon", "3 PM", "6 PM", "9 PM" };

    public static string Describe(ScheduleType type) => type switch
    {
        ScheduleType.HorizPace => "Pace East-West",
        ScheduleType.VertPace => "Pace North-South",
        ScheduleType.Dance => "Dance",
        ScheduleType.Stand => "Stand",
        ScheduleType.Loiter => "Loiter",
        ScheduleType.Wander => "Wander",
        ScheduleType.Sleep => "Sleep",
        ScheduleType.Wait => "Wait",
        ScheduleType.Sit => "Sit",
        ScheduleType.Waiter => "Waiter",
        ScheduleType.EatAtInn => "Eat at Inn",
        ScheduleType.Custom => "Custom (event commands)",
        _ => type.ToString()
    };

    public static string Explain(ScheduleType type) => type switch
    {
        ScheduleType.HorizPace => "Walks east and west from the spot, turning back whenever something is in the way (Exult horiz_pace).",
        ScheduleType.VertPace => "Walks north and south from the spot, turning back whenever something is in the way (Exult vert_pace).",
        ScheduleType.Dance => "Dances on the spot, now and then stepping about (Exult dance) - uses the Dancing pose.",
        ScheduleType.Stand => "Stands at the spot (Exult stand).",
        ScheduleType.Loiter => "Strolls to random places within 12 tiles of the spot, pausing between walks (Exult loiter).",
        ScheduleType.Wander => "Walks off up to 32 tiles at a time, staying within 128 tiles of the spot (Exult wander).",
        ScheduleType.Sleep => "Goes to bed: the nearest free bed within 24 tiles, lying on it with its cover turned down over them; with no bed, lies down at the spot - uses the Sleeping pose (Exult sleep).",
        ScheduleType.Sit => "Finds the nearest free chair seat, walks to it and sits facing the way the chair faces - uses the Sit pose; with none free, tries again a little later (Exult sit).",
        ScheduleType.Wait => "Stays exactly where it is - and, as in Exult (where Wait is how companions are told to stay), later schedule changes no longer move it.",
        ScheduleType.Waiter => "Works at the spot (its counter): finds customers sitting nearby, walks over and asks their order, goes back to the counter to prepare it, then brings a dish (from its menu, Character Editor > Health) and puts it on the table in front of them (Exult waiter).",
        ScheduleType.EatAtInn => "Sits in the nearest free chair and eats whatever is served to it, asking for more when there's nothing in front of it (Exult eat_at_inn). A Waiter brings the food.",
        ScheduleType.Custom => "Goes to the spot and runs your event commands there, then stands.",
        _ => ""
    };
}

/// <summary>
/// Exult's Game_clock (gameclk.cc): the clock advances one tick every
/// standard delay (c_std_delay, 200 ms) by the time rate, 25 ticks make a
/// game minute - so a game minute is 5 real seconds, an hour 5 minutes, a
/// day 2 hours - and a new game starts at 6:00. Time stands still while the
/// game is paused (a gump is open), as Exult's time queue does.
/// </summary>
public class GameClock
{
    public const int TicksPerMinute = 25;
    public const int StdDelayMs = 200;

    public int Day { get; private set; }
    public int Hour { get; private set; }
    public int Minute { get; private set; }
    public int Ticks { get; private set; }
    public int TimeRate { get; set; } = 1;

    private double _msAccumulated;

    public GameClock(int startHour = 6, int timeRate = 1)
    {
        Hour = Math.Clamp(startHour, 0, 23);
        TimeRate = Math.Max(1, timeRate);
    }

    /// <summary>The current 3-hour period, 0-7 (Exult's hour3).</summary>
    public int Period => Hour / 3;

    /// <summary>Advances by real elapsed time; returns the number of hour changes that happened (Exult re-schedules NPCs at each).</summary>
    public int Advance(double elapsedMs)
    {
        _msAccumulated += elapsedMs;
        int hoursPassed = 0;
        while (_msAccumulated >= StdDelayMs)
        {
            _msAccumulated -= StdDelayMs;
            Ticks += TimeRate;
            Minute += Ticks / TicksPerMinute;
            Ticks %= TicksPerMinute;
            while (Minute >= 60)
            {
                Minute -= 60;
                if (++Hour >= 24) { Hour -= 24; Day++; }
                hoursPassed++;
            }
        }
        return hoursPassed;
    }

    /// <summary>Time passes at once (resting): this many game minutes. Returns the hour changes.</summary>
    public int AdvanceMinutes(int minutes)
    {
        int hoursPassed = 0;
        Minute += Math.Max(0, minutes);
        while (Minute >= 60)
        {
            Minute -= 60;
            if (++Hour >= 24) { Hour -= 24; Day++; }
            hoursPassed++;
        }
        return hoursPassed;
    }

    /// <summary>A saved game's time, put back on loading it.</summary>
    public void Restore(int day, int hour, int minute, int ticks)
    {
        Day = Math.Max(0, day);
        Hour = Math.Clamp(hour, 0, 23);
        Minute = Math.Clamp(minute, 0, 59);
        Ticks = Math.Clamp(ticks, 0, TicksPerMinute - 1);
        _msAccumulated = 0;
    }

    /// <summary>Exult's fake_next_period: jump to the start of the next 3-hour period.</summary>
    public void SkipToNextPeriod()
    {
        Ticks = 0;
        Minute = 0;
        _msAccumulated = 0;
        Hour = (Hour / 3 + 1) * 3;
        Day += Hour / 24;
        Hour %= 24;
    }

    public override string ToString()
    {
        int hour12 = Hour % 12 == 0 ? 12 : Hour % 12;
        return $"Day {Day + 1}, {hour12}:{Minute:00} {(Hour < 12 ? "AM" : "PM")}";
    }
}

/// <summary>Game clock options (System Settings > Game Clock).</summary>
public class GameClockSettings
{
    /// <summary>The hour a new game starts at - Exult starts at 6:00.</summary>
    public int StartHour { get; set; } = 6;
    /// <summary>Exult's time rate: ticks added per standard delay. 1 = Exult's normal speed (a game minute every 5 seconds).</summary>
    public int TimeRate { get; set; } = 1;
}
