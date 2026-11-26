namespace SandcastleRpgMaker.Engine;

/// <summary>How a spell system casts (Ultima 6 projects; Tools > Spell Editor).</summary>
public enum SpellCastStyle
{
    /// <summary>Ultima 6: a spellbook readied in a hand, its syllables typed (or picked from its pages); circles, mana, reagents.</summary>
    SpellbookWords,
    /// <summary>The Savage Empire: no book - an ingredient Used while a fetish is carried or next to the caster casts that ingredient's spell.</summary>
    FetishIngredient,
    /// <summary>Your own: the Cast key runs a Common Event, and Cast Spell / the effects are all yours.</summary>
    EventsOnly,
}

/// <summary>An ingredient and the spell it casts on a fetish (the spell by its name in a spellbook object).</summary>
public class SpellRecipe
{
    public int IngredientObjectId { get; set; } = -1;
    public string SpellName { get; set; } = string.Empty;
    public bool UsesUpIngredient { get; set; } = true;
}

/// <summary>
/// One way of doing magic (the user, 2026-11-25: Ultima 6 mode's magic is a whole mechanic of its own - not Ultima 7's
/// spellbook - made with event commands; only one used at a time; ready-made ones to learn from). Its spells are the
/// spells of the project's spellbook objects (Object Editor > Spellbook), found by name; its events are Common Events.
/// </summary>
public class SpellSystem
{
    public int Id { get; set; }
    public string Name { get; set; } = "New spell system";
    public SpellCastStyle Style { get; set; } = SpellCastStyle.SpellbookWords;
    /// <summary>Fetish style: the objects ingredients are used with (a skull, a totem) - carried by the caster or next to them.</summary>
    public List<int> FetishObjectIds { get; set; } = new();
    /// <summary>Fetish style: which ingredient casts which spell.</summary>
    public List<SpellRecipe> Recipes { get; set; } = new();
    /// <summary>Fetish style: casting costs the spell's mana and needs its circle's level, as Ultima 6 (unticked: The Savage Empire's - free).</summary>
    public bool UsesMana { get; set; }
    /// <summary>Events only: the Common Event the Cast key runs (by name; empty: none).</summary>
    public string OnCastEvent { get; set; } = string.Empty;
    /// <summary>Every style: a Common Event run after any spell is cast (the variable SpellCast holds its name; empty: none).</summary>
    public string AfterCastEvent { get; set; } = string.Empty;
    /// <summary>Said when an ingredient is used with no fetish near.</summary>
    public string NoFetishLine { get; set; } = "Thou needest a fetish.";
}

/// <summary>The project's spell systems (Ultima6Settings.SpellSystems) and the one in use.</summary>
public class SpellSystemSettings
{
    public List<SpellSystem> Systems { get; set; } = new();
    /// <summary>The one in use (its Id); -1 or missing: Ultima 6's spellbook (the built-in way).</summary>
    public int ActiveId { get; set; } = -1;

    /// <summary>The variable that holds the name of the spell just cast (After Cast's Common Event reads it).</summary>
    public const string SpellCastVariable = "SpellCast";

    public SpellSystem Active => Systems.FirstOrDefault(s => s.Id == ActiveId) ?? new SpellSystem { Id = -1, Name = "Ultima 6 (spellbook and words)" };

    /// <summary>Ready-made systems to start from.</summary>
    public static SpellSystem UltimaSixTemplate(int id) => new() { Id = id, Name = "Ultima 6 - spellbook and words of power", Style = SpellCastStyle.SpellbookWords };

    public static SpellSystem SavageEmpireTemplate(int id) => new()
    {
        Id = id, Name = "The Savage Empire - fetish and ingredients", Style = SpellCastStyle.FetishIngredient, UsesMana = false,
        NoFetishLine = "Thou needest thy fetish to work the magic."
    };
}
