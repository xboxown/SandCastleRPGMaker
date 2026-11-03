namespace SandcastleRpgMaker.Engine;

/// <summary>
/// What only an Ultima 8 project's engine uses (System Settings > Ultima 8; the Ultima 7 engine never reads it). Every
/// word it shows is here, none fixed in the engine.
/// </summary>
public class Ultima8Settings
{
    // ---------- Moving with the mouse (Pentagram's walk_threshold / run_threshold) ----------
    // (The paperdoll's stats are the PaperDoll Editor's and the Character Editor's - nothing of them here.)

    /// <summary>The pointer nearer the Avatar than this (of 255, of half the view) doesn't move them at all; beyond it, a step. 0: always at least a step (Pentagram).</summary>
    public int StepThreshold { get; set; }

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
