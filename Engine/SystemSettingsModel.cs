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
    /// <summary>
    /// Ultima 8 projects: the direction arrows (Pentagram's mouse frames) - "Walk.North", "Step.SouthEast", "Run.West",
    /// "Combat.NorthWest"... - eight screen directions (North: up the screen) for walking, stepping, running and combat mode.
    /// </summary>
    public Dictionary<string, MousePointerSprite> U8Arrows { get; set; } = new();
}

/// <summary>How conversations show.</summary>
public class ConversationSettings
{
    /// <summary>
    /// Off: the replies are in the speaker's own box, under their words.
    /// On (as Exult, Conversation::show_avatar_choices): the main character
    /// answers - the replies are in a box of their own under the speaker's,
    /// with the main character's face.
    /// </summary>
    public bool RepliesUnderMainCharacter { get; set; }
}

/// <summary>Project-wide system settings, stored in the .msrp manifest alongside everything else.</summary>
/// <summary>How combat shows on screen.</summary>
public class CombatSettings
{
    /// <summary>Exult's "show hits" option (Combat::show_hits, off by default): the numbers over whoever is hit ("-4", "miss"). Hits always flash a red outline, as in Exult; this only adds the numbers.</summary>
    public bool ShowHitNumbers { get; set; }
    /// <summary>The music the engine plays by itself around fights (System Settings > Combat).</summary>
    public CombatMusicSettings Music { get; set; } = new();
}

/// <summary>One piece of combat music: a Sound list entry (-1 = none: nothing changes), and whether it plays over and over.</summary>
public class CombatMusicCue
{
    public int MusicId { get; set; } = -1;
    public bool Loop { get; set; }
}

/// <summary>
/// Exult's combat music (combat.cc Combat_schedule::start_music_combat). Exult
/// plays every one of these once, never looping (continuous = false), and the
/// map's own music comes back when it ends. Near death and Death are not in
/// Exult and loop by default.
/// </summary>
public class CombatMusicSettings
{
    /// <summary>CSDanger: an enemy close by is fighting, and the main character is not in combat mode.</summary>
    public CombatMusicCue Danger { get; set; } = new();
    /// <summary>CSAttacked1 / CSAttacked2: the battle begins (one of the two picked at random; at most once every 30 seconds).</summary>
    public CombatMusicCue Battle { get; set; } = new();
    public CombatMusicCue Battle2 { get; set; } = new();
    /// <summary>CSRun_Away: combat mode is turned off with a foe still close by.</summary>
    public CombatMusicCue RunAway { get; set; } = new();
    /// <summary>CSHidden_Danger: the enemies were left behind (out of sight) while danger or battle music played.</summary>
    public CombatMusicCue Escaped { get; set; } = new();
    /// <summary>CSVictory: the last enemy around dies.</summary>
    public CombatMusicCue Victory { get; set; } = new();
    /// <summary>CSBattle_Over: the last enemy dies after a long fight (more than 15 seconds, more likely the longer it went).</summary>
    public CombatMusicCue BattleOver { get; set; } = new();
    /// <summary>Not Exult: the main character is below a quarter of its health with an enemy around.</summary>
    public CombatMusicCue NearDeath { get; set; } = new() { Loop = true };
    /// <summary>The main character died (Exult's CSAvatar_died is never played): until the Save/Load screen opens or a game is loaded.</summary>
    public CombatMusicCue Death { get; set; } = new() { Loop = true };
}

/// <summary>Sounds the engine itself plays, project-wide (System Settings > Sounds).</summary>
public class SoundSettings
{
    /// <summary>Played whenever anything is put down on the map - an item, an object, a container, a stack, a body. -1 = silent.</summary>
    public int DropOnMapSoundId { get; set; } = -1;
    /// <summary>Played whenever something is put into a container's window or onto a paperdoll (Exult's gump drop) - every container and paperdoll. -1 = silent (an older project's own per-container sound still plays).</summary>
    public int NavigateSoundId { get; set; } = -1;
    /// <summary>One opening sound for every paperdoll - a character's inventory, a book, a spellbook, a map, a container's window, a PaperDoll Editor paperdoll - when ticked; unticked, each uses its own.</summary>
    public bool PaperdollOpenSoundEnabled { get; set; }
    public int PaperdollOpenSoundId { get; set; } = -1;
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

    /// <summary>Over the slider when a stack is moved and the game asks how many of it (drag.cc prompt_for_number shows no words - empty, as Exult).</summary>
    public string QuantityQuestion { get; set; } = string.Empty;

    /// <summary>Classic: Ultima VII's slider (everything above). Custom: one built from pieces (Custom below).</summary>
    public SliderStyle Style { get; set; } = SliderStyle.Classic;
    public CustomSliderDesign Custom { get; set; } = new();
}

public enum SliderStyle { Classic, Custom }

/// <summary>
/// How a weapon in the hand is drawn (Exult's Actor::paint_weapon). Exult's (the default): a weapon-in-hand sprite has 4
/// frames - held, and the swing pointing north, east, south - mirrored for east- and west-facing characters. Smooth: each
/// of the eight directions its own held frame and SwingFrames frames of swing. The Sprite Editor's Weapon in hand group
/// follows this.
/// </summary>
public class WeaponInHandSettings
{
    public bool Smooth { get; set; }
    /// <summary>Smooth: how many frames a swing takes in each direction (1-8).</summary>
    public int SwingFrames { get; set; } = 3;
    /// <summary>How many frames a weapon-in-hand sprite has now.</summary>
    public int FrameCount => SpriteDatabase.WeaponInHandFrameNames(Smooth, SwingFrames).Length;
}

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

/// <summary>The in-game Save/Load screen (System Settings > Save / Load): a paperdoll made in the PaperDoll Editor with the Save slots / Save list, Save / Load / Delete buttons... parts.</summary>
public class SaveLoadSettings
{
    /// <summary>The PaperDoll Editor paperdoll that is the Save/Load screen (-1 = none: the game can't be saved).</summary>
    public int DollDesignId { get; set; } = -1;
    /// <summary>The "Are you sure?" paperdoll opened before writing over or deleting a save - its Yes / OK button goes ahead, No / Cancel doesn't (-1 = none: press the button again).</summary>
    public int ConfirmDollDesignId { get; set; } = -1;
    /// <summary>The paperdoll shown once a game is saved, closed by its own button (-1 = none: a "Game saved" label).</summary>
    public int SavedDollDesignId { get; set; } = -1;
    /// <summary>The paperdoll shown while the game saves or loads - its progress bars and displays following "the save / load progress" fill as it goes (-1 = none).</summary>
    public int ProgressDollDesignId { get; set; } = -1;
    /// <summary>It stays at least this long (ms), so a quick save is still seen - full once the work is done.</summary>
    public int ProgressShownAtLeastMs { get; set; } = 600;
}

/// <summary>The Title Screen (System Settings > Title Screen): a PaperDoll Editor paperdoll shown before the game, its music and an intro video.</summary>
public class TitleScreenSettings
{
    /// <summary>The title paperdoll (-1 = none: the game starts at once).</summary>
    public int DollDesignId { get; set; } = -1;
    public int MusicId { get; set; } = -1;
    public bool MusicLoops { get; set; } = true;
    /// <summary>A video (Video Library) played before the title (-1 = none).</summary>
    public int IntroVideoId { get; set; } = -1;
    /// <summary>Game Test (F5) starts at the Title Screen too; a built game always does when there is one.</summary>
    public bool InGameTest { get; set; }
}

/// <summary>Quitting (Ctrl+Q, a Quit button): the "Are you sure?" paperdoll asked first.</summary>
public class QuitSettings
{
    /// <summary>The Quit prompt - its Yes / OK quits, No / Cancel closes it (-1 = none: quits at once).</summary>
    public int DollDesignId { get; set; } = -1;
}

public enum GameScreenMode { Window, FullScreen }

/// <summary>How a built game opens (System Settings > Game Window). Game Test always opens in a window.</summary>
public class DisplaySettings
{
    public GameScreenMode Mode { get; set; } = GameScreenMode.Window;
    /// <summary>Window mode: the game's view drawn this many times its 320 x 200 (2-6), the window sized round it.</summary>
    public int WindowScale { get; set; } = 3;
}

/// <summary>The editor saving the project by itself (System Settings > Autosave) - every so many real minutes while it's open.</summary>
public class AutoSaveSettings
{
    public bool Enabled { get; set; } = true;
    public int Minutes { get; set; } = 5;
}

/// <summary>
/// A language of the game's own (System Settings > Languages), as Serpent Isle's Ophidian: words written in it
/// (a Show Text, a book) show in its font - runes - till the party understands it: someone in it knows the
/// language (Learn Language), or a Translate spell is on.
/// </summary>
public class LanguageDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Language";
    /// <summary>The font (Sprite Editor > Fonts) its words show in while not understood; none: the ordinary one.</summary>
    public string FontSpriteName { get; set; } = string.Empty;
}

public class SystemSettings
{
    /// <summary>The Scenario Developer's scenarios (Game Test only).</summary>
    public ScenarioSettings Scenarios { get; set; } = new();
    /// <summary>Starvation can kill (System Settings > Combat). Not set: Ultima 7's way, off (it only knocks out, again and again); Ultima 8 starts with it on.</summary>
    public bool? StarvationKills { get; set; }
    /// <summary>The game's own languages (System Settings > Languages).</summary>
    public List<LanguageDefinition> Languages { get; set; } = new();
    public AutoSaveSettings AutoSave { get; set; } = new();
    public DisplaySettings Display { get; set; } = new();
    public TitleScreenSettings Title { get; set; } = new();
    public QuitSettings Quit { get; set; } = new();
    /// <summary>Common Events (Tools > Common Events): command lists made once, run from anywhere with Call Common Event.</summary>
    public List<CommonEventDefinition> CommonEvents { get; set; } = new();
    public SaveLoadSettings SaveLoad { get; set; } = new();
    /// <summary>The game's errors: their paperdoll and their words by number (System Settings > Errors).</summary>
    public ErrorSettings Errors { get; set; } = new();
    public SliderSettings Slider { get; set; } = new();
    /// <summary>Weapons drawn in the hand (Sprite Editor > Weapon in hand): Exult's frames, or smooth ones.</summary>
    public WeaponInHandSettings WeaponInHand { get; set; } = new();
    /// <summary>Only an Ultima 8 project's engine reads these (Engine/Ultima8Model.cs).</summary>
    public Ultima8Settings Ultima8 { get; set; } = new();
    public MousePointerSettings MousePointers { get; set; } = new();
    public SoundSettings Sounds { get; set; } = new();
    public GameClockSettings Clock { get; set; } = new();
    public CombatSettings Combat { get; set; } = new();
    /// <summary>How conversations show (System Settings > Conversations).</summary>
    public ConversationSettings Conversation { get; set; } = new();
    /// <summary>The level curve: Max Level, experience needed and training points per level (System Settings > Levels).</summary>
    public LevelSettings Levels { get; set; } = new();
    /// <summary>The day and night cycle (System Settings > Day &amp; Night).</summary>
    public DayNightSettings DayNight { get; set; } = new();
    /// <summary>
    /// The Body Container (System Settings > Body Container): how every dead
    /// body looks when opened - its paperdoll picture and the frame its
    /// belongings lie within. It has no world sprite of its own: in the world
    /// a body is the dead character's Body pose. As in Exult, things can be
    /// taken out of a body but never put back in.
    /// </summary>
    public ContainerData BodyContainer { get; set; } = new() { Name = "Body", IsBodyContainer = true, MaxVolume = 9999 };
}
