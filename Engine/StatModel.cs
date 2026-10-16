namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Stable identity for the 11 built-in stats, independent of their
/// display Name - per spec, the engine must keep working correctly even
/// after a built-in stat is renamed (e.g. "STR" renamed to "Power"), so
/// anything that needs to mean "the Strength stat specifically" should
/// key off this Kind, never off StatDefinition.Name. None = a custom
/// stat, which has no fixed meaning the engine can rely on - only a
/// name and a type.
/// </summary>
public enum BuiltInStatKind
{
    None,
    Strength, Dexterity, Intelligence, Combat, Magic,
    Hits, Mana, Level, Exp, TrainingPoints, NextExp
}

public class StatDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Stat";

    /// <summary>Reuses VariableValueType rather than a duplicate stat-specific enum - "Integer or String" per spec is already exactly what that type covers, no need for a second type system that means the same thing.</summary>
    public VariableValueType Type { get; set; } = VariableValueType.Integer;

    /// <summary>None for every custom stat. One of the 11 fixed values for a built-in one - set once in CreateDefault() and never changed afterward, since it's what survives a rename.</summary>
    public BuiltInStatKind Kind { get; set; } = BuiltInStatKind.None;

    /// <summary>Per spec: built-ins can't be deleted or retyped, but CAN be renamed - Kind is what makes renaming safe.</summary>
    public bool IsBuiltIn => Kind != BuiltInStatKind.None;
}

/// <summary>
/// Project-wide stat list. The ten default combat/RPG stats plus Next Exp
/// are seeded here, matching the PC/NPC design doc's own table - custom
/// stats beyond these are added via the Variable Editor-style Stat Editor,
/// with an open-ended Integer/String type per stat (actual behavior for a
/// custom stat gets programmed via the event editor later, per spec - this
/// is just the definition, not behavior). Every condition/complex-condition
/// UI that lists stats reads this same list live, so a stat added here
/// shows up everywhere automatically - no separate sync step needed.
/// </summary>
public class StatDatabase
{
    public List<StatDefinition> Stats { get; set; } = new();
    private int _nextId = 1;

    public int GetNextId() => _nextId++;

    public void SyncNextIdAfterLoad()
    {
        _nextId = Stats.Count == 0 ? 1 : Stats.Max(s => s.Id) + 1;
    }

    /// <summary>The lowest value an Integer stat can hold (Exult allows 0: most people have no Magic, Mana, Exp or Training). There is no highest - unlike Exult's 30, a project may go as high as it likes.</summary>
    public const int BuiltInMinimum = 0;

    /// <summary>True for every Integer stat, built-in or added: never blank, never below <see cref="BuiltInMinimum"/>, no upper limit.</summary>
    public static bool HasMinimum(StatDefinition stat) => stat.Type == VariableValueType.Integer;

    /// <summary>An Integer stat's value, made valid: blank gives 0, below 0 gives 0, and something that isn't a number keeps the previous value (or 0).</summary>
    public static string ClampBuiltIn(string? text, string? previous = null)
    {
        if (int.TryParse(text?.Trim(), out int value)) return Math.Max(BuiltInMinimum, value).ToString();
        if (!string.IsNullOrWhiteSpace(text) && int.TryParse(previous?.Trim(), out int old)) return Math.Max(BuiltInMinimum, old).ToString();
        return BuiltInMinimum.ToString();
    }

    /// <summary>Fills in or fixes every Integer stat on the character: a blank Hits becomes the character's Strength (full health), any other blank becomes 0.</summary>
    public void EnsureBuiltInMinimums(CharacterDefinition character)
    {
        var strength = Stats.FirstOrDefault(s => s.Kind == BuiltInStatKind.Strength);
        foreach (var stat in Stats.Where(HasMinimum).OrderBy(s => s.Kind == BuiltInStatKind.Hits)) // Strength settled before Hits copies it
        {
            character.StatValues.TryGetValue(stat.Id, out var text);
            if (stat.Kind == BuiltInStatKind.Hits && string.IsNullOrWhiteSpace(text) && strength is not null && character.StatValues.TryGetValue(strength.Id, out var str)) text = str;
            character.StatValues[stat.Id] = ClampBuiltIn(text);
        }
    }

    public static StatDatabase CreateDefault()
    {
        var db = new StatDatabase();
        foreach (var (name, kind) in new[]
        {
            ("STR", BuiltInStatKind.Strength), ("DEX", BuiltInStatKind.Dexterity), ("INT", BuiltInStatKind.Intelligence),
            ("COM", BuiltInStatKind.Combat), ("MAG", BuiltInStatKind.Magic), ("Hits", BuiltInStatKind.Hits),
            ("Mana", BuiltInStatKind.Mana), ("Level", BuiltInStatKind.Level), ("EXP", BuiltInStatKind.Exp),
            ("Training Points", BuiltInStatKind.TrainingPoints), ("Next Exp", BuiltInStatKind.NextExp)
        })
        {
            db.Stats.Add(new StatDefinition { Id = db.GetNextId(), Name = name, Type = VariableValueType.Integer, Kind = kind });
        }
        return db;
    }
}
