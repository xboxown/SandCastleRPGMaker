namespace SandcastleRpgMaker.Engine;

/// <summary>
/// The engine's mouse pointers - the same set Exult defines in mouse.h
/// (Mouse::Mouse_shapes), minus the hourglass: hand = 0, redx = 1,
/// greenselect = 2, tooheavy = 3, outofrange = 4, outofammo = 5,
/// wontfit = 6, greensquare = 23, blocked = 49.
/// </summary>
public enum MousePointerKind { Hand, RedX, GreenSelect, TooHeavy, OutOfRange, OutOfAmmo, WontFit, GreenSquare, Blocked, Attack }

/// <summary>Which Sprite Editor sprite draws one pointer - same group + name link every other sprite reference in the project uses.</summary>
public class MousePointerSprite
{
    public string SpriteGroup { get; set; } = string.Empty;
    public string SpriteName { get; set; } = string.Empty;
}

/// <summary>
/// The engine's own pointer graphics, replacing the operating system's
/// cursor entirely in the game. All pointers share one pixel size
/// (Width x Height): only sprites of exactly that size can be picked. A
/// pointer with no sprite shows as a black square of that size.
/// </summary>
public class MousePointerSettings
{
    public int Width { get; set; }
    public int Height { get; set; }
    public Dictionary<MousePointerKind, MousePointerSprite> Sprites { get; set; } = new();
}

/// <summary>Project-wide system settings, stored in the .msrp manifest alongside everything else.</summary>
/// <summary>How combat shows on screen.</summary>
public class CombatSettings
{
    /// <summary>Exult's "show hits" option (Combat::show_hits, off by default): the numbers over whoever is hit ("-4", "miss"). Hits always flash a red outline, as in Exult; this only adds the numbers.</summary>
    public bool ShowHitNumbers { get; set; }
}

/// <summary>Sounds the engine itself plays, project-wide (System Settings > Sounds).</summary>
public class SoundSettings
{
    /// <summary>Played whenever anything is put down on the map - an item, an object, a container, a stack, a body. -1 = silent.</summary>
    public int DropOnMapSoundId { get; set; } = -1;
}

/// <summary>
/// The number slider (System Settings > Slider) - Ultima VII's Slider_gump
/// (Exult gumps/Slider_gump.cc, Slider_widget.cc), used whenever the game
/// asks "how many" (how many hours to sleep...). Its look is the game's own:
/// a background, left and right arrows, a diamond sliding along a track
/// between them, the number, and a check mark to accept. Positions are in
/// the background's pixels; the defaults are Exult's layout. Values never go
/// past 999.
/// </summary>
public class SliderSettings
{
    public const int MaxValue = 999;

    public SpriteLink Background { get; set; } = new();
    public SpriteLink LeftArrow { get; set; } = new();
    public SpriteLink RightArrow { get; set; } = new();
    public SpriteLink Diamond { get; set; } = new();
    public SpriteLink CheckMark { get; set; } = new();
    /// <summary>Where the left arrow starts (Exult: the widget at 24, 6).</summary>
    public int SliderX { get; set; } = 24;
    public int SliderY { get; set; } = 6;
    /// <summary>The track's length between the arrows (Exult: 64).</summary>
    public int TrackWidth { get; set; } = 64;
    /// <summary>Where the number ends (it's written right-aligned there; Exult: 128, 7).</summary>
    public int NumberX { get; set; } = 128;
    public int NumberY { get; set; } = 7;
    /// <summary>The check mark (Exult: 22, 18).</summary>
    public int CheckX { get; set; } = 22;
    public int CheckY { get; set; } = 18;
    /// <summary>Played each time the value changes.</summary>
    public int ClickSoundResourceId { get; set; } = -1;
}

public class SystemSettings
{
    public SliderSettings Slider { get; set; } = new();
    public MousePointerSettings MousePointers { get; set; } = new();
    public SoundSettings Sounds { get; set; } = new();
    public GameClockSettings Clock { get; set; } = new();
    public CombatSettings Combat { get; set; } = new();
    /// <summary>
    /// The Body Container (System Settings > Body Container): how every dead
    /// body looks when opened - its paperdoll picture and the frame its
    /// belongings lie within. It has no world sprite of its own: in the world
    /// a body is the dead character's Body pose. As in Exult, things can be
    /// taken out of a body but never put back in.
    /// </summary>
    public ContainerData BodyContainer { get; set; } = new() { Name = "Body", IsBodyContainer = true, MaxVolume = 9999 };
}
