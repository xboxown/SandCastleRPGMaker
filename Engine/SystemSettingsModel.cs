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

    /// <summary>Classic: Ultima VII's slider (everything above). Custom: one built from pieces (Custom below).</summary>
    public SliderStyle Style { get; set; } = SliderStyle.Classic;
    public CustomSliderDesign Custom { get; set; } = new();
}

public enum SliderStyle { Classic, Custom }

public enum SliderPieceKind
{
    /// <summary>A button that takes Step off the number (and Left).</summary>
    Decrease,
    /// <summary>A button that adds Step to the number (and Right).</summary>
    Increase,
    /// <summary>A button that accepts the number (and Enter).</summary>
    Accept,
    /// <summary>A button that cancels (and Escape).</summary>
    Cancel,
    /// <summary>The number: drawn with a digit sprite (its frames 1-10 are 0-9) or the game's font.</summary>
    Number,
    /// <summary>A track the thumb (this piece's sprite) slides along, Width pixels long; clicking it sets the number.</summary>
    Track,
    /// <summary>Just a picture (a frame, a label, a light).</summary>
    Decoration
}

/// <summary>One piece of a custom slider, placed on its background.</summary>
public class SliderPiece
{
    public SliderPieceKind Kind { get; set; } = SliderPieceKind.Decoration;
    /// <summary>Its picture. Buttons: frame 1 normal, frame 2 (if any) while pressed. Number: the digit sprite (frames 1-10 = 0-9; an 11th = a blank). Track: the thumb.</summary>
    public SpriteLink Sprite { get; set; } = new();
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>Buttons: the sound when it's clicked.</summary>
    public int SoundResourceId { get; set; } = -1;
    /// <summary>Number: how many digits are shown (0 = as many as the number has).</summary>
    public int DigitCount { get; set; } = 3;
    /// <summary>Number: "008" rather than "  8".</summary>
    public bool LeadingZeros { get; set; } = true;
    /// <summary>Number: pixels between digits.</summary>
    public int DigitSpacing { get; set; } = 1;
    /// <summary>Track: its length in pixels.</summary>
    public int Width { get; set; } = 64;
}

/// <summary>A slider made from pieces (System Settings > Slider > Custom): a background, and buttons, a number, a track and decorations placed on it, with sounds.</summary>
public class CustomSliderDesign
{
    public SpriteLink Background { get; set; } = new();
    public List<SliderPiece> Pieces { get; set; } = new()
    {
        new() { Kind = SliderPieceKind.Decrease, X = 4, Y = 4 },
        new() { Kind = SliderPieceKind.Number, X = 24, Y = 4 },
        new() { Kind = SliderPieceKind.Increase, X = 60, Y = 4 },
        new() { Kind = SliderPieceKind.Accept, X = 80, Y = 4 }
    };
    /// <summary>A sound for each digit 0-9, played when the number changes to end in it (-1 = none: the change sound instead).</summary>
    public List<int> DigitSoundIds { get; set; } = Enumerable.Repeat(-1, 10).ToList();
    /// <summary>Played when the number changes (when its last digit has no sound of its own).</summary>
    public int ChangeSoundResourceId { get; set; } = -1;
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
