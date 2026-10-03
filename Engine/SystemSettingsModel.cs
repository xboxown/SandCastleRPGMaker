namespace SandcastleRpgMaker.Engine;

/// <summary>
/// The engine's mouse pointers - the same set Exult defines in mouse.h
/// (Mouse::Mouse_shapes), minus the hourglass: hand = 0, redx = 1,
/// greenselect = 2, tooheavy = 3, outofrange = 4, outofammo = 5,
/// wontfit = 6, greensquare = 23, blocked = 49.
/// </summary>
public enum MousePointerKind { Hand, RedX, GreenSelect, TooHeavy, OutOfRange, OutOfAmmo, WontFit, GreenSquare, Blocked }

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
public class SystemSettings
{
    public MousePointerSettings MousePointers { get; set; } = new();
    public GameClockSettings Clock { get; set; } = new();
}
