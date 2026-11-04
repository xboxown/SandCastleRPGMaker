namespace SandcastleRpgMaker.Engine;

/// <summary>
/// A named area of the map (Map Editor > Mode > Zones) - a house, a pub, a
/// hospital - covering the cells of its building on one floor. Its Purpose
/// is what kind of place it is ("Pub", "Hospital", "Home"...), so several
/// places can share one: a character's schedule can go to one zone, or to
/// the nearest zone with a purpose ("the nearest Pub").
/// </summary>
public class MapZone
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Zone";
    public string Purpose { get; set; } = string.Empty;
    /// <summary>Its cells: top-left corner and size.</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 1;
    public int Height { get; set; } = 1;
    /// <summary>The floor (1-9) it's on.</summary>
    public int Floor { get; set; } = 1;
    /// <summary>Its colour in the Map Editor, #RRGGBB.</summary>
    public string Color { get; set; } = "#3FA7FF";
    /// <summary>The sound looping while the main character is in it, instead of the map's ambient sounds (-1: none). A Cave silences the world's ambient sounds always - quiet if it has none of its own. Each house's zones keep their own (one cave drips, another howls).</summary>
    public int AmbientSoundId { get; set; } = -1;

    public bool Contains(int x, int y) => x >= X && y >= Y && x < X + Width && y < Y + Height;
    public (int X, int Y) Center => (X + Width / 2, Y + Height / 2);

    public string Describe() => string.IsNullOrWhiteSpace(Purpose) ? Name : $"{Name} ({Purpose})";
}

public static class Zones
{
    /// <summary>Purposes offered in the editors (any other can be typed).</summary>
    public static readonly string[] CommonPurposes = { "Home", "Pub", "Inn", "Hospital", "Shop", "Smithy", "Farm", "Temple", "Guard post", "Workplace", CavePurpose, ColdPurpose, HotPurpose };

    /// <summary>A Cave: inside, no rain, no weather, complete darkness that only a light source lifts; the world's ambient sounds stop and its own (AmbientSoundId) loops instead.</summary>
    public const string CavePurpose = "Cave";
    /// <summary>A Cold zone: anyone inside freezes unless it's wearing enough warmth (Serpent Isle). Can lie inside another zone.</summary>
    public const string ColdPurpose = "Cold";
    /// <summary>A Hot zone: the same with heat, and what's worn to cool it.</summary>
    public const string HotPurpose = "Hot";

    public static bool IsCave(MapZone zone) => string.Equals(zone.Purpose.Trim(), CavePurpose, StringComparison.OrdinalIgnoreCase);

    private static bool AnyAt(IEnumerable<MapZone> zones, string purpose, int x, int y, int floor) =>
        zones.Any(z => z.Floor == floor && z.Contains(x, y) && string.Equals(z.Purpose.Trim(), purpose, StringComparison.OrdinalIgnoreCase));

    public static bool IsInCave(IEnumerable<MapZone> zones, int x, int y, int floor) => AnyAt(zones, CavePurpose, x, y, floor);
    public static bool IsCold(IEnumerable<MapZone> zones, int x, int y, int floor) => AnyAt(zones, ColdPurpose, x, y, floor);
    public static bool IsHot(IEnumerable<MapZone> zones, int x, int y, int floor) => AnyAt(zones, HotPurpose, x, y, floor);

    /// <summary>The zone on that cell and floor with a sound of its own or a Cave (its ambient instead of the map's), or null. Zones inside zones: the smallest, with a sound of its own before a silent one.</summary>
    public static MapZone? AmbientZoneAt(IEnumerable<MapZone> zones, int x, int y, int floor) =>
        zones.Where(z => z.Floor == floor && z.Contains(x, y) && (IsCave(z) || z.AmbientSoundId >= 0)).OrderBy(z => z.AmbientSoundId >= 0 ? 0 : 1).ThenBy(z => z.Width * z.Height).FirstOrDefault();

    /// <summary>Zones of a purpose (any case).</summary>
    public static IEnumerable<MapZone> WithPurpose(IEnumerable<MapZone> zones, string purpose) =>
        zones.Where(z => string.Equals(z.Purpose.Trim(), purpose.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>The zone of that purpose whose centre is nearest a cell, or null.</summary>
    public static MapZone? Nearest(IEnumerable<MapZone> zones, string purpose, int fromX, int fromY) =>
        WithPurpose(zones, purpose).OrderBy(z => Math.Max(Math.Abs(z.Center.X - fromX), Math.Abs(z.Center.Y - fromY))).FirstOrDefault();
}

/// <summary>What starts a schedule rule (Character Editor > Schedule > When...).</summary>
public enum ScheduleRuleCondition
{
    /// <summary>Food at or below its Hungry level (Health tab); until it's full.</summary>
    Hungry,
    /// <summary>Health at or below a share of its most; until it's back up to another share.</summary>
    Injured,
    /// <summary>A variable compared with a value; for as long as that's so.</summary>
    Variable
}

/// <summary>
/// "When hungry, go to the nearest Pub and eat at the inn": while its
/// condition holds, the character leaves its timetable, goes where the rule
/// says and does what it says there; when the condition ends it goes back
/// to its timetable. The first rule in the list that holds wins.
/// </summary>
public class ScheduleRule
{
    public ScheduleRuleCondition Condition { get; set; } = ScheduleRuleCondition.Hungry;
    /// <summary>Injured: starts at or below this % of its most health...</summary>
    public int InjuredAtOrBelowPercent { get; set; } = 50;
    /// <summary>...and ends at or above this %.</summary>
    public int HealedAtPercent { get; set; } = 100;
    /// <summary>Variable: name, comparison, value.</summary>
    public string VariableName { get; set; } = string.Empty;
    public ConditionComparisonOperator Comparison { get; set; } = ConditionComparisonOperator.Equal;
    public double Value { get; set; }
    /// <summary>Where it goes and what it does there (Period unused).</summary>
    public ScheduleEntry Do { get; set; } = new() { Type = ScheduleType.EatAtInn, ZonePurpose = "Pub" };

    public string Describe()
    {
        string when = Condition switch
        {
            ScheduleRuleCondition.Hungry => "hungry",
            ScheduleRuleCondition.Injured => $"health at or below {InjuredAtOrBelowPercent}%",
            _ => $"{VariableName} {Comparison} {Value}"
        };
        return $"When {when}: {Schedules.Describe(Do.Type)} at {Do.DescribePlace()}";
    }

    public static bool Compare(double left, ConditionComparisonOperator op, double right) => op switch
    {
        ConditionComparisonOperator.Equal => Math.Abs(left - right) < 0.0001,
        ConditionComparisonOperator.NotEqual => Math.Abs(left - right) >= 0.0001,
        ConditionComparisonOperator.GreaterThan => left > right,
        ConditionComparisonOperator.GreaterOrEqual => left >= right,
        ConditionComparisonOperator.LessThan => left < right,
        _ => left <= right
    };
}
