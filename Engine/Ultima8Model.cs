namespace SandcastleRpgMaker.Engine;

/// <summary>
/// What only an Ultima 8 project's engine uses (System Settings > Ultima 8; the Ultima 7 engine never reads it). Every
/// word it shows is here, none fixed in the engine.
/// </summary>
public class Ultima8Settings
{
    // ---------- The paperdoll's stats (Pentagram's PaperdollGump::PaintStats) ----------

    /// <summary>The stats are written on the paperdoll, beside the body, as Ultima 8 shows them.</summary>
    public bool ShowPaperdollStats { get; set; } = true;
    /// <summary>The labels, in Ultima 8's order: Strength, Intelligence, Dexterity, armour class, hit points, mana, weight carried.</summary>
    public List<string> StatLabels { get; set; } = new() { "STR", "INT", "DEX", "ARMR", "HITS", "MANA", "WGHT" };
    /// <summary>Where they are written on the paperdoll (its 320 x 400 layout): the labels' left edge, the values' right edge, the first line, the line height.</summary>
    public int StatsLabelX { get; set; } = 196;
    public int StatsValueX { get; set; } = 300;
    public int StatsTopY { get; set; } = 52;
    public int StatsLineHeight { get; set; } = 20;
    /// <summary>The weight shown is the weight carried divided by this (Ultima 8: weight / 10).</summary>
    public int WeightDivisor { get; set; } = 10;

    // ---------- Moving with the mouse (Pentagram's walk_threshold / run_threshold) ----------

    /// <summary>The pointer further from the Avatar than this (of 255, of half the view) walks rather than steps.</summary>
    public int WalkThreshold { get; set; } = 50;
    /// <summary>Further than this (of 255) runs.</summary>
    public int RunThreshold { get; set; } = 160;

    // ---------- Conversations (Pentagram's BarkGump / AskGump) ----------

    /// <summary>What a character says floats over its head, wrapped to this width (game pixels; BarkGump's text 194 wide).</summary>
    public int BarkWidth { get; set; } = 194;
    /// <summary>The answers over the Avatar, in rows of at most this width (AskGump: 160).</summary>
    public int AskWidth { get; set; } = 160;
    /// <summary>A page with no answers stays this long per letter (at least BarkMinMs), then goes on by itself.</summary>
    public int BarkMsPerChar { get; set; } = 60;
    public int BarkMinMs { get; set; } = 1500;
}
