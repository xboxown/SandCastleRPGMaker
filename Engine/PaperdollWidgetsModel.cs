namespace SandcastleRpgMaker.Engine;

/// <summary>Exult's Actor::Attack_mode, in its own order (the combat-mode button's 10 frames): how a character picks and fights its foes (combat.cc find_foe). Manual is the player's alone.</summary>
public enum AttackMode { Nearest, Weakest, Strongest, Berserk, Protect, Defend, Flank, Flee, Random, Manual }

/// <summary>A button on a character's inventory paperdoll (Exult's Actor_gump): its picture, where it is in the 320x400 layout, and a click sound.</summary>
public class PaperdollButton
{
    public bool Enabled { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    /// <summary>Its frames: Combat - peace, war; Combat behavior - one per behavior (Nearest ... Manual, 10); Halo - off, on; Heart and Disk - up, pressed.</summary>
    public SpriteLink Sprite { get; set; } = new();
    public int ClickSoundResourceId { get; set; } = -1;
}

public enum StatsElementKind { Text, Name, Stat, Ailment, Hunger }

/// <summary>How an inventory paperdoll brings up a PaperDoll Editor paperdoll: a button on it (click: open / close), or opening and closing with it.</summary>
public enum DollLinkWay { Button, WithInventory }

/// <summary>
/// A PaperDoll Editor paperdoll this inventory brings up - Ultima 8's small hit points / mana bars under the inventory.
/// Opened for this character: its parts following "the character it was opened for" show their stats.
/// A button: its X / Y, sprite (frames: closed, open) and click sound are the button's.
/// </summary>
public class PaperdollDollLink : PaperdollButton
{
    public int DesignId { get; set; } = -1;
    public DollLinkWay Way { get; set; } = DollLinkWay.Button;
    /// <summary>Where it opens, from the inventory's top-left, in the inventory's own 320 x 400 pixels (330, 0: just to its right).</summary>
    public double DollX { get; set; } = 330;
    public double DollY { get; set; }
}
public enum StatShowAs { Number, Bar, Pie, SpriteFrames, Icons }

/// <summary>One thing on a stats paperdoll: a label, the name, a stat (as a number or a gauge), an ailment's picture while it lasts, or how hungry they are.</summary>
public class StatsDollElement
{
    public int Id { get; set; }
    public StatsElementKind Kind { get; set; } = StatsElementKind.Text;
    /// <summary>Its text style and place (X/Y here are the element's place, in the stats paperdoll's own pixels).</summary>
    public PaperdollReadout Style { get; set; } = new() { Enabled = true };
    /// <summary>Text: what it says ("Strength:").</summary>
    public string Text { get; set; } = string.Empty;
    /// <summary>Stat: which (built-in or custom).</summary>
    public int StatId { get; set; } = -1;
    public StatShowAs ShowAs { get; set; } = StatShowAs.Number;
    /// <summary>A gauge's range: from Min to Max, or to another stat's value (Hits out of Strength).</summary>
    public int Min { get; set; }
    public int Max { get; set; } = 30;
    public int MaxStatId { get; set; } = -1;
    /// <summary>A bar, pie or sprite frames (empty to full) - X/Y follow the element's own.</summary>
    public PaperdollGauge Gauge { get; set; } = new() { Enabled = true };
    /// <summary>Ailment: which; its picture (Gauge.Sprite) shows only while they have it.</summary>
    public Ailment Ailment { get; set; } = Ailment.Poisoned;
    /// <summary>Hunger: shown only while hungry (else always, as a gauge of how full they are).</summary>
    public bool OnlyWhenHungry { get; set; } = true;
}

/// <summary>
/// A character's stats paperdoll (Exult's Stats_gump, Z in the game, the
/// heart on the inventory paperdoll): a background, a close button, and as
/// many elements as you like, each placed anywhere - names, stats, ailment
/// pictures, hunger.
/// </summary>
public class StatsDollDefinition
{
    public SpriteLink Background { get; set; } = new();
    /// <summary>Two frames: up, pressed.</summary>
    public SpriteLink CloseButton { get; set; } = new();
    public double CloseX { get; set; } = 140;
    public double CloseY { get; set; } = 4;
    public int OpenSoundResourceId { get; set; } = -1;
    public List<StatsDollElement> Elements { get; set; } = new();
    public int NextElementId() => Elements.Count == 0 ? 1 : Elements.Max(e => e.Id) + 1;
}