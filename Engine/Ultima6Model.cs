namespace SandcastleRpgMaker.Engine;

/// <summary>
/// What only an Ultima 6 project's engine uses (System Settings > Ultima 6, and the HUD Editor). Ultima 6, Savage Empire
/// and Martian Dreams share it (Nuvie is the reference). The Ultima 7 and Ultima 8 engines never read it.
/// </summary>
public class Ultima6Settings
{
    /// <summary>The most who can be in the party, the main character with them - as Ultima 6: fixed, not a setting.</summary>
    public const int MaxPartyMembers = 9;

    /// <summary>The game's screen: its frame and every piece on it (HUD Editor). One per project; Change HUD Frame swaps its frame in play.</summary>
    public HudDesign Hud { get; set; } = new();

    // ---------- Conversations (typed keywords, as Ultima 6) ----------

    /// <summary>The colour of the keywords in what a character says ("#RRGGBB") - the words the player can type to ask about.</summary>
    public string KeywordColor { get; set; } = "#FFD800";
    /// <summary>Music while talking (-1: none - the map's music goes on, as Nuvie's default conversations_stop_music = no).</summary>
    public int ConversationMusicId { get; set; } = -1;
    /// <summary>After a conversation with its own music: the map's music starts again from the beginning, or a random one of the map's.</summary>
    public string AfterTalkMusic { get; set; } = AfterTalkRestart;
    public const string AfterTalkRestart = "Start the map's music again", AfterTalkRandom = "A random song of the map's";

    /// <summary>The keys the maker changed from Ultima 6's (action id -> key name); the rest are Ultima 6's (Engine/U6Keys.cs).</summary>
    public Dictionary<string, string> Keys { get; set; } = new();
    /// <summary>The text area's words the maker changed (word id -> words); the rest are Ultima 6's (U6Words).</summary>
    public Dictionary<string, string> Words { get; set; } = new();

    // ---------- Levels (U6Leveling) ----------

    /// <summary>
    /// Off (always, unless the maker deliberately unlocks it in System Settings > Ultima 6): Ultima 6's levels - eight, the
    /// experience steps of Ultima 6, 30 hit points a level, stats raised by a shrine or a dream. On: the Ultima 7 engine's
    /// levels instead (automatic, training points, each character's curve in the Character Editor).
    /// </summary>
    public bool UseUltima7Leveling { get; set; }
    /// <summary>How a level earned is taken: meditating at a shrine (Ultima 6) or dreaming when the party rests (Savage Empire, Martian Dreams).</summary>
    public string LevelUpWay { get; set; } = LevelUpShrine;
    /// <summary>The title menu's picture, filling the screen (U6GameWindow.U6Title.cs); not set: black.</summary>
    public SpriteLink TitleSprite { get; set; } = new();
    /// <summary>What the title menu's Acknowledgments shows.</summary>
    public string Acknowledgments { get; set; } = "Made with Sandcastle RPG Maker.";
    public const string LevelUpShrine = "At a shrine (Ultima 6)", LevelUpDream = "In a dream when the party rests (Savage Empire, Martian Dreams)";
}

/// <summary>
/// Ultima 6's levels, as Nuvie has them (u6/actor.lua, md/usecode.lua rest_level_up_actor, U6Actor::get_maxhp): eight levels,
/// the next one earned with MORE experience than 100, 200, 400 ... 6400; the most hit points 30 a level (never past 255);
/// a stat never past 30. A level isn't taken by itself: at a shrine, chanting its mantra, every member who has earned one
/// rises and gets the shrine's virtue's points (Honesty 3 intelligence, Compassion 3 dexterity, Valor 3 strength, Justice,
/// Sacrifice and Honor 1 each of two, Spirituality 1 of each, Humility none) - or, Savage Empire and Martian Dreams, in a
/// dream when the party rests: one stat, picked, 1 point, and 30 hit points back.
/// </summary>
public static class U6Leveling
{
    public const int MaxLevel = 8, StatCap = 30, HitsPerLevel = 30, MostHits = 255;
    /// <summary>More than Needs[level] experience earns the next level (Nuvie's exp_level_tbl).</summary>
    public static readonly int[] Needs = { 0, 100, 200, 400, 800, 1600, 3200, 6400 };

    public static int MaxHits(int level) => Math.Min(MostHits, Math.Clamp(level, 1, MaxLevel) * HitsPerLevel);
    public static bool HasEarned(int level, int experience) => level >= 1 && level < MaxLevel && experience > Needs[level];
    /// <summary>The experience the next level needs (one more than its step); 0 at level 8.</summary>
    public static int NextExp(int level) => level >= 1 && level < MaxLevel ? Needs[level] + 1 : level < 1 ? Needs[1] + 1 : 0;

    public static readonly (string Virtue, string Mantra, int Str, int Dex, int Int)[] Shrines =
    {
        ("Honesty", "AHM", 0, 0, 3), ("Compassion", "MU", 0, 3, 0), ("Valor", "RA", 3, 0, 0), ("Justice", "BEH", 0, 1, 1),
        ("Sacrifice", "CAH", 1, 1, 0), ("Honor", "SUMM", 1, 0, 1), ("Spirituality", "OM", 1, 1, 1), ("Humility", "LUM", 0, 0, 0),
    };
}

/// <summary>The kinds of piece a HUD is built from (HUD Editor's toolbox).</summary>
public enum HudPieceKind
{
    /// <summary>A still or animated picture laid on the frame.</summary>
    Picture,
    /// <summary>Where the world is shown, and how big.</summary>
    GameView,
    /// <summary>The messages (scrolling) and what the player types (Move-, Talk to-, the conversation).</summary>
    TextArea,
    /// <summary>The panel: inventory (body slots, carried things) or portrait and stats, one member at a time, in the same place.</summary>
    Panel,
    /// <summary>Inventory view: one body slot (head, neck, body, hands, rings, feet...).</summary>
    BodySlot,
    /// <summary>Inventory view: the things the member carries, a grid of cells.</summary>
    CarriedItems,
    /// <summary>Portrait view: the member's face.</summary>
    Portrait,
    /// <summary>Portrait view: the member's stats, one per line.</summary>
    Stats,
    /// <summary>Both views: the shown member's name.</summary>
    MemberName,
    /// <summary>A verb (Attack, Talk, Look, Get, Drop, Move, Use, Rest, Cast, Combat): clicked, or its key typed.</summary>
    CommandButton,
    /// <summary>Your own button: one click runs its event (Always, Only Once - again once it has finished).</summary>
    CustomButton,
    /// <summary>Which way the party faces / the wind - a sprite of 8 frames (N, NE, E, SE, S, SW, W, NW).</summary>
    Compass,
    /// <summary>The sun and the moons over the day.</summary>
    SunMoon,
    /// <summary>The date and the wind, as words.</summary>
    DateText,
    /// <summary>The shown member's combat mode, as words.</summary>
    CombatMode
}

/// <summary>The verbs of the command bar (Ultima 6's reference card: the first letter of each).</summary>
public enum HudVerb { Attack, Cast, Talk, Look, Get, Drop, Move, Use, Rest, Combat }

/// <summary>One piece of the HUD.</summary>
public class HudPiece
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public HudPieceKind Kind { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; } = 16;
    public int Height { get; set; } = 16;
    /// <summary>Its look (animated or not); a command button's while not pressed.</summary>
    public SpriteLink Sprite { get; set; } = new();
    /// <summary>A button's look while pressed / its verb active (none: its own look, darkened).</summary>
    public SpriteLink PressedSprite { get; set; } = new();
    public HudVerb Verb { get; set; }
    public EquipmentSlot Slot { get; set; } = EquipmentSlot.Head;
    /// <summary>Carried items: cells across and down, each this many pixels.</summary>
    public int Columns { get; set; } = 4;
    public int Rows { get; set; } = 3;
    public int CellSize { get; set; } = 16;
    /// <summary>Words: a font of Sprite Editor > Fonts (empty: InGameFont) and its colour ("#RRGGBB", empty: the font's own).</summary>
    public string FontName { get; set; } = string.Empty;
    public string TextColor { get; set; } = string.Empty;
    /// <summary>Stats: which (Stat Editor ids); empty: every stat.</summary>
    public List<int> StatIds { get; set; } = new();
    /// <summary>A custom button's event (one page: Always, Only Once - fixed).</summary>
    public List<BookEventPage> EventPages { get; set; } = new();

    /// <summary>Which view of the panel shows it: inventory pieces, portrait pieces, or always.</summary>
    public static HudPanelView ViewOf(HudPieceKind kind) => kind switch
    {
        HudPieceKind.BodySlot or HudPieceKind.CarriedItems => HudPanelView.Inventory,
        HudPieceKind.Portrait or HudPieceKind.Stats => HudPanelView.Portrait,
        _ => HudPanelView.Always
    };

    public static string KindName(HudPieceKind kind) => kind switch
    {
        HudPieceKind.GameView => "Game View",
        HudPieceKind.TextArea => "Text Area",
        HudPieceKind.BodySlot => "Body Slot",
        HudPieceKind.CarriedItems => "Carried Items",
        HudPieceKind.MemberName => "Member Name",
        HudPieceKind.CommandButton => "Command Button",
        HudPieceKind.CustomButton => "Custom Button",
        HudPieceKind.SunMoon => "Sun And Moons",
        HudPieceKind.DateText => "Date And Wind",
        HudPieceKind.CombatMode => "Combat Mode",
        _ => kind.ToString()
    };

    /// <summary>Only one of these on a HUD.</summary>
    public static bool IsSingle(HudPieceKind kind) => kind is HudPieceKind.GameView or HudPieceKind.TextArea or HudPieceKind.Panel;

    public override string ToString() => $"{Name}  ({KindName(Kind)})";
}

public enum HudPanelView { Always, Inventory, Portrait }

/// <summary>The HUD: a frame picture (Sprite Editor) the size of the game's screen, and its pieces, drawn in list order.</summary>
public class HudDesign
{
    /// <summary>The game's screen in pixels (Ultima 6: 320 x 200), scaled up to the window.</summary>
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 200;
    public SpriteLink Frame { get; set; } = new();
    public List<HudPiece> Pieces { get; set; } = new();

    public HudPiece? Piece(int id) => Pieces.FirstOrDefault(p => p.Id == id);
    public HudPiece? First(HudPieceKind kind) => Pieces.FirstOrDefault(p => p.Kind == kind);
    public int NextId() => Pieces.Count == 0 ? 1 : Pieces.Max(p => p.Id) + 1;
}
