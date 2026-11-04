namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Exult's actor ailments (Obj_flags poisoned / asleep / paralyzed / charmed
/// / cursed), as flags so a character can have several at once and be
/// immune to any of them.
/// </summary>
[Flags]
public enum Ailment
{
    None = 0,
    Poisoned = 1,
    Asleep = 2,
    Paralyzed = 4,
    Charmed = 8,
    Cursed = 16,
    /// <summary>Exult's invisibility flag (a potion or spell; 60-80 s): enemies that can't see invisible leave them be. Not an ailment one can be immune to.</summary>
    Invisible = 32,
    /// <summary>Exult's protection flag (60-80 s): +3 to defence. Not an ailment one can be immune to.</summary>
    Protected = 64
}

public static class Ailments
{
    public static readonly Ailment[] All = { Ailment.Poisoned, Ailment.Asleep, Ailment.Paralyzed, Ailment.Charmed, Ailment.Cursed };

    public static string Describe(Ailment ailment) => ailment switch
    {
        Ailment.Poisoned => "Poisoned",
        Ailment.Asleep => "Asleep",
        Ailment.Paralyzed => "Paralyzed",
        Ailment.Charmed => "Charmed",
        Ailment.Cursed => "Cursed",
        Ailment.Invisible => "Invisible",
        Ailment.Protected => "Protected",
        _ => ailment.ToString()
    };

    public static string Explain(Ailment ailment) => ailment switch
    {
        Ailment.Poisoned => "Loses health until cured or it wears off (Exult: 0-2 HP every 10-20 s, for 1-3 minutes). Here poison can kill.",
        Ailment.Asleep => "Lies asleep and can't act for 5-10 seconds (Exult's sleep timer).",
        Ailment.Paralyzed => "Can't move or act for 60-120 seconds (Exult).",
        Ailment.Charmed => "Fights for the other side for 60-120 seconds (Exult) - takes effect with combat.",
        Ailment.Cursed => "Cursed for 60-120 seconds (Exult: can't cast spells).",
        _ => ""
    };
}

/// <summary>
/// The body a character leaves when it dies - as in Exult, an object in the
/// world that holds everything the character carried, has its own weight
/// and volume, and can be picked up and carried - unless it's too heavy.
/// It looks like the character's Body pose; when opened it uses the Body
/// Container from System Settings.
/// </summary>
public class BodySettings
{
    public float Weight { get; set; } = 50;
    public float Volume { get; set; } = 20;
    /// <summary>Can't be picked up or moved at all - only opened and looted where it lies.</summary>
    public bool TooHeavy { get; set; }
    /// <summary>Leaves no body: what it carried falls on the ground around where it died (Exult's has_no_body, within 5 tiles).</summary>
    public bool NoBody { get; set; }
}

/// <summary>
/// A character's needs and frailties: Exult's food level (0-31, hungry at 9
/// or below, 0-3 used each game hour, starving at 0 loses health), poison,
/// and the ailments it can never catch. Unlike Exult, starvation and poison
/// both kill, and they apply to every character, not just the party.
/// </summary>
public class SurvivalSettings
{
    public const int MaxFood = 31;

    /// <summary>False = never gets hungry (Exult's "does not eat").</summary>
    public bool CanGetHungry { get; set; } = true;

    // ---------- When hungry, what it does about it (not the party: as in Exult, you feed them) ----------
    /// <summary>It eats food on the map that belongs to it (Belong To) - at home - walking to it.</summary>
    public bool EatsOwnFood { get; set; } = true;
    /// <summary>It eats food it carries.</summary>
    public bool EatsCarriedFood { get; set; } = true;
    /// <summary>It goes to the inn (the spot below), sits down and is served by the waiter.</summary>
    public bool GoesToInn { get; set; }
    /// <summary>The inn: a cell on the map (-1 = none set).</summary>
    public int InnX { get; set; } = -1;
    public int InnY { get; set; } = -1;
    /// <summary>With no inn cell: the nearest zone of this purpose ("Pub") is its inn (Map Editor > Mode > Zones).</summary>
    public string InnZonePurpose { get; set; } = string.Empty;
    /// <summary>Food level the game starts with, 0-31.</summary>
    public int StartingFood { get; set; } = 24;
    /// <summary>Hungry (and complaining) at or below this - Exult: 9.</summary>
    public int HungryAtOrBelow { get; set; } = 9;
    /// <summary>Each game hour, between 0 and this much food is used - Exult: 3 (1.5 on average).</summary>
    public int FoodUsedPerHourMax { get; set; } = 3;
    /// <summary>At food 0, each game minute: this % chance of losing StarvingDamage HP - Exult: 40% (2 in 5), 1 HP.</summary>
    public int StarvingChancePercent { get; set; } = 40;
    public int StarvingDamage { get; set; } = 1;

    /// <summary>Poison wears off by itself after a random time in this range (seconds) - Exult: 60-180. Off = only a cure ends it.</summary>
    public bool PoisonWearsOff { get; set; } = true;
    public int PoisonCureMinSeconds { get; set; } = 60;
    public int PoisonCureMaxSeconds { get; set; } = 180;
    /// <summary>Each poison tick (every 10-20 s) loses 0 to this many HP - Exult: 2.</summary>
    public int PoisonDamageMax { get; set; } = 2;

    /// <summary>Ailments this character can never catch.</summary>
    public Ailment Immunities { get; set; } = Ailment.None;
}
