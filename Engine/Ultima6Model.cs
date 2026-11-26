namespace SandcastleRpgMaker.Engine;

/// <summary>
/// What only an Ultima 6 project's engine uses (System Settings > Ultima 6, and the HUD Editor). Ultima 6, Savage Empire
/// and Martian Dreams share it (Nuvie is the reference). The Ultima 7 and Ultima 8 engines never read it.
/// </summary>
public class Ultima6Settings
{
    /// <summary>The most who can be in the party, the main character with them - as Ultima 6: fixed, not a setting.</summary>
    public const int MaxPartyMembers = 9;
    /// <summary>The most in the party, the main character with them: 0-9 (never more than Ultima 6's 9). 0 or 1: nobody joins.</summary>
    public int PartySize { get => Math.Clamp(_partySize, 0, MaxPartyMembers); set => _partySize = Math.Clamp(value, 0, MaxPartyMembers); }
    private int _partySize = MaxPartyMembers;

    /// <summary>The game's screen: its frame and every piece on it (HUD Editor). One per project; Change HUD Frame swaps its frame in play.</summary>
    public HudDesign Hud { get; set; } = new();

    /// <summary>The magic: the spell systems (Tools > Spell Editor) and the one in use (Engine/SpellSystem.cs).</summary>
    public SpellSystemSettings SpellSystems { get; set; } = new();

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
    /// <summary>
    /// Off (Ultima 6): no hunger and no starving, no healing by the hour - food is eaten at Rest's mealtime, and only the fed heal
    /// when they wake. On: the Ultima 7 engine's food (hunger, starving, eating, healing by the hour; the Character Editor's Hunger).
    /// </summary>
    /// <summary>Gone (the user, 2026-11-24): Ultima 6 has no hunger or meals - food heals the party at Rest. Always false; an old project's value is ignored.</summary>
    public bool UseUltima7Food { get => false; set { } }
    /// <summary>
    /// The Leveling Editor's systems - the project's own leveling, each its table and its events (made of event commands).
    /// Many can be kept, one is used (ActiveLevelSystem; none: levels are only what events set). A new project starts with
    /// templates to learn from - Ultima 6's shrines, Savage Empire's and Martian Dreams' dreams - delete them, make your own.
    /// </summary>
    public List<U6LevelSystem> LevelSystems { get; set; } = U6LevelSystem.Templates();
    public string ActiveLevelSystem { get; set; } = U6LevelSystem.U6TemplateName;
    public U6LevelSystem? Active => LevelSystems.FirstOrDefault(s => s.Name == ActiveLevelSystem);
    /// <summary>How a level earned is taken: meditating at a shrine (Ultima 6) or dreaming when the party rests (Savage Empire, Martian Dreams).</summary>
    public string LevelUpWay { get; set; } = LevelUpShrine;
    /// <summary>
    /// The engine's own notes (a spell's "Light on Andrew.", "\"Chest\" is locked.", "That's too heavy to lift." - everything that
    /// isn't one of the Words) as they reach the text area: changed or removed by these rules, first match wins (U6Notes).
    /// </summary>
    public List<U6NoteRule> NoteRules { get; set; } = new();
    /// <summary>A note no rule matches: said as the engine words it (on), or not at all (off).</summary>
    public bool ShowOtherNotes { get; set; } = true;
    /// <summary>Game Test's debugging notes (U6DebugNotes) the maker turned off in the text area.</summary>
    public List<string> HiddenDebugNotes { get; set; } = new();
    /// <summary>Game Test's debugging notes the maker reworded (note id -> words, with its {0} {1} blanks).</summary>
    public Dictionary<string, string> DebugNoteWords { get; set; } = new();
    /// <summary>What the avatar can't see is black (walls, closed doors, Occludes stop sight) - Ultima 6's view (U6GameWindow.U6Sight.cs).</summary>
    public bool LineOfSight { get; set; } = true;
    /// <summary>The title menu's picture, filling the screen (U6GameWindow.U6Title.cs); not set: black.</summary>
    public SpriteLink TitleSprite { get; set; } = new();
    /// <summary>What the title menu's Acknowledgments shows.</summary>
    public string Acknowledgments { get; set; } = "Made with Sandcastle RPG Maker.";
    public const string LevelUpShrine = "At a shrine (Ultima 6)", LevelUpDream = "In a dream when the party rests (Savage Empire, Martian Dreams)";
}

/// <summary>Conversation Style (Ultima 6 tab): how conversations look and work from now on - null in play: Ultima 6's.</summary>
public class U6TalkStyle
{
    public const string Typed = "Typed keywords (Ultima 6)", List = "Picked from a list (arrows, 1-9, a click)", Both = "Typed or picked from the list";
    public const string PortraitHud = "In the HUD's Portrait (Ultima 6)", PortraitBox = "Inside the words' box", PortraitNone = "None";
    public const string WordsTextArea = "In the HUD's text area (Ultima 6)", WordsBox = "In a box of their own (below)";
    public string Answers { get; set; } = Typed;
    public bool MarkKeywords { get; set; } = true;
    public string KeywordColor { get; set; } = string.Empty;
    public string Portrait { get; set; } = PortraitHud;
    public string Words { get; set; } = WordsTextArea;
    public int BoxX { get; set; } = 8;
    public int BoxY { get; set; } = 120;
    public int BoxWidth { get; set; } = 304;
    public int BoxHeight { get; set; } = 72;
    public string BoxColor { get; set; } = string.Empty;
    public SpriteLink BoxSprite { get; set; } = new();
    public string Font { get; set; } = string.Empty;
    public string TextColor { get; set; } = string.Empty;
}

/// <summary>One leveling system of the Leveling Editor: its level table and its events.</summary>
public class U6LevelSystem
{
    public const string U6TemplateName = "Ultima 6 - shrines (template)", DreamTemplateName = "Savage Empire / Martian Dreams - dreams (template)";

    public string Name { get; set; } = "New leveling system";
    /// <summary>The experience each level needs: more than [0] for level 2, [1] for level 3... The levels: one more than these.</summary>
    public List<int> LevelExperience { get; set; } = new() { 100, 200, 400, 800, 1600, 3200, 6400 };
    /// <summary>The most hit points: this many a level...</summary>
    public int HitsPerLevel { get; set; } = 30;
    /// <summary>...never past this.</summary>
    public int MostHits { get; set; } = 255;
    /// <summary>No stat Rise A Level raises goes past this.</summary>
    public int StatCap { get; set; } = 30;
    /// <summary>On: a level earned is taken at once (its hit points); its events still run. Off: only Rise A Level raises it.</summary>
    public bool RiseByThemselves { get; set; }
    public List<U6LevelEvent> Events { get; set; } = new();

    private static EventCommand C(EventCommandType type, params string[] kv)
    {
        var c = new EventCommand { Type = type };
        for (int i = 0; i + 1 < kv.Length; i += 2) c.Parameters[kv[i]] = kv[i + 1];
        return c;
    }

    private static EventCommand Rise(string who, string party, string str, string dex, string intel, string notReady) =>
        C(EventCommandType.U6RiseLevel, "Who", who, "Party", party, "OnlyIfEarned", "True", "Str", str, "Dex", dex, "Int", intel, "Hits", "", "Tell", "True", "NotReady", notReady);

    /// <summary>The templates a project starts with, to learn from (and delete).</summary>
    public static List<U6LevelSystem> Templates() => new()
    {
        new U6LevelSystem
        {
            Name = U6TemplateName,
            Events =
            {
                new U6LevelEvent { Name = "A level is earned", Trigger = U6LevelEvent.WhenEarned, Commands =
                {
                    C(EventCommandType.U6SceneText, "Text", "{LevelWho} could rise to level {LevelEarned}. Meditate at a shrine.", "Where", "In the text area"),
                } },
                new U6LevelEvent { Name = "Shrine of Valor", Trigger = U6LevelEvent.WhenCalled, Commands =
                {
                    C(EventCommandType.U6SceneText, "Text", "You chant the mantra: RA...", "Where", "In the text area"),
                    Rise("Player", "True", "3", "0", "0", "Thou art not ready."),
                } },
                new U6LevelEvent { Name = "Shrine of Honesty", Trigger = U6LevelEvent.WhenCalled, Commands =
                {
                    C(EventCommandType.U6SceneText, "Text", "You chant the mantra: AHM...", "Where", "In the text area"),
                    Rise("Player", "True", "0", "0", "3", "Thou art not ready."),
                } },
                new U6LevelEvent { Name = "Shrine of Justice", Trigger = U6LevelEvent.WhenCalled, Commands =
                {
                    C(EventCommandType.U6SceneText, "Text", "You chant the mantra: BEH...", "Where", "In the text area"),
                    Rise("Player", "True", "0", "1", "1", "Thou art not ready."),
                } },
            }
        },
        new U6LevelSystem
        {
            Name = DreamTemplateName,
            Events =
            {
                new U6LevelEvent { Name = "A dream after resting", Trigger = U6LevelEvent.WhenRestedEach, Commands =
                {
                    C(EventCommandType.U6SceneText, "Text", "{LevelWho} has a dream. Three stone obelisks...", "Where", "In the text area"),
                    C(EventCommandType.U6SceneChoice, "Question", "Which does {LevelWho} touch?", "Choices", "Sword (strength)\nHeart (dexterity)\nBook (intelligence)", "X", "8", "Y", "8", "Variable", "dreamPick"),
                    Rise("Activator", "False", "dreamPick = 1", "dreamPick = 2", "dreamPick = 3", ""),
                } },
            }
        },
    };
}

/// <summary>One event of a leveling system.</summary>
public class U6LevelEvent
{
    public const string WhenEarned = "When someone earns a level", WhenRested = "When the party wakes from Rest",
        WhenRestedEach = "When the party wakes from Rest - once for each member who has earned a level", WhenCalled = "When Run Leveling Event calls it";
    public static readonly string[] Triggers = { WhenEarned, WhenRested, WhenRestedEach, WhenCalled };

    public string Name { get; set; } = "New event";
    public string Trigger { get; set; } = WhenCalled;
    public List<EventCommand> Commands { get; set; } = new();
}

/// <summary>A text area note rule: when the engine says When (* stands for any words), the text area says Say instead ({1}, {2}... the words each * stood for); Say empty: nothing is said.</summary>
public class U6NoteRule
{
    public string When { get; set; } = string.Empty;
    public string Say { get; set; } = string.Empty;
}

public static class U6Notes
{
    /// <summary>What the text area says for an engine note: a rule's words (null: nothing), or the note itself / nothing when no rule matches.</summary>
    public static string? Rewrite(Ultima6Settings settings, string note)
    {
        foreach (var rule in settings.NoteRules)
        {
            if (rule.When.Trim().Length == 0) continue;
            string pattern = "^" + System.Text.RegularExpressions.Regex.Escape(rule.When.Trim()).Replace("\\*", "(.*?)") + "$";
            var match = System.Text.RegularExpressions.Regex.Match(note, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
            if (!match.Success) continue;
            string said = rule.Say;
            for (int i = match.Groups.Count - 1; i >= 1; i--) said = said.Replace("{" + i + "}", match.Groups[i].Value);
            return said.Trim().Length == 0 ? null : said;
        }
        return settings.ShowOtherNotes ? note : null;
    }
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
    /// <summary>The active leveling system's table (Leveling Editor) - the game and the editors set it.</summary>
    public static U6LevelSystem Table { get; set; } = new();
    public static void Use(Ultima6Settings settings) => Table = settings.Active ?? new U6LevelSystem();

    public static int MaxLevel => Table.LevelExperience.Count + 1;
    public static int StatCap => Math.Max(1, Table.StatCap);
    public static int HitsPerLevel => Math.Max(0, Table.HitsPerLevel);
    /// <summary>More than Needs(level) experience earns the next level.</summary>
    public static int Needs(int level) => level >= 1 && level < MaxLevel ? Table.LevelExperience[level - 1] : int.MaxValue;
    public static int MaxHits(int level) => Math.Min(Math.Max(1, Table.MostHits), Math.Clamp(level, 1, MaxLevel) * Math.Max(1, Table.HitsPerLevel));
    public static bool HasEarned(int level, int experience) => level >= 1 && level < MaxLevel && experience > Needs(level);
    /// <summary>The experience the next level needs (one more than its step); 0 at the highest level.</summary>
    public static int NextExp(int level) => Math.Max(1, level) < MaxLevel ? Needs(Math.Max(1, level)) + 1 : 0;

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
    CombatMode,
    /// <summary>Words of your own (Set HUD Piece Text changes them in play).</summary>
    Label
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
    /// <summary>Picture: drawn in front of the panel's pieces (body slots and what they wear, carried items, portrait, stats, name).
    /// Off (the default): behind them - a paperdoll picture under the weapons and armour.</summary>
    public bool InFront { get; set; }
    /// <summary>Panel contents: drawn over the HUD's other pieces, as the game draws them - after them, member by member.</summary>
    public static bool IsPanelContent(HudPieceKind kind) => kind is HudPieceKind.BodySlot or HudPieceKind.CarriedItems or HudPieceKind.Portrait
        or HudPieceKind.Stats or HudPieceKind.MemberName or HudPieceKind.CombatMode;
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
    /// <summary>The panel it belongs to (a custom panel's Id): shown only while that panel is open. 0: none - on the HUD itself.</summary>
    public int PanelId { get; set; }
    /// <summary>A custom panel (any Panel after the first): the key that opens and closes it in play ("P", "Ctrl+P"; empty: none) -
    /// Enable / Disable Panel Key switch it in play.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>A custom panel: open as the game starts (else closed till its key or Show / Hide HUD Panel).</summary>
    public bool StartsOpen { get; set; }
    /// <summary>A Label's words.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>A copy to change in play (Move / Set Picture / Set Text) - the HUD Editor's stays as made.</summary>
    public HudPiece Copy() => (HudPiece)MemberwiseClone();
    /// <summary>Pieces that can go in a custom panel.</summary>
    public static bool CanBeInPanel(HudPieceKind kind) => kind is HudPieceKind.Picture or HudPieceKind.CustomButton or HudPieceKind.CommandButton
        or HudPieceKind.Label or HudPieceKind.DateText or HudPieceKind.Compass or HudPieceKind.SunMoon;

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
        HudPieceKind.Label => "Label",
        _ => kind.ToString()
    };

    /// <summary>Only one of these on a HUD.</summary>
    public static bool IsSingle(HudPieceKind kind) => kind is HudPieceKind.GameView or HudPieceKind.TextArea; // (panels: as many as wanted, at least one)

    public override string ToString() => $"{Name}  ({KindName(Kind)})";
}

public enum HudPanelView { Always, Inventory, Portrait }

/// <summary>A HUD piece as the HUD commands left it in play (null: as the HUD Editor made it) - kept in saves.</summary>
public class U6HudPieceState
{
    public int Id { get; set; }
    public bool? Shown { get; set; }
    public int? X { get; set; }
    public int? Y { get; set; }
    public SpriteLink? Sprite { get; set; }
    public string? Text { get; set; }
    /// <summary>A custom panel's key switched off (Disable Panel Key).</summary>
    public bool KeyOff { get; set; }
}

/// <summary>The HUD: a frame picture (Sprite Editor) the size of the game's screen, and its pieces, drawn in list order.</summary>
public class HudDesign
{
    /// <summary>The game's screen in pixels (Ultima 6: 320 x 200), scaled up to the window.</summary>
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 200;
    public SpriteLink Frame { get; set; } = new();
    public List<HudPiece> Pieces { get; set; } = new();
    /// <summary>The title menu's Introduction: its scene (Scene commands); none - the intro video.</summary>
    public List<EventCommand> IntroCommands { get; set; } = new();
    /// <summary>The title menu's Create New Character: its scene (questions, choices) before the game begins; none - straight in.</summary>
    public List<EventCommand> CreateCommands { get; set; } = new();
    /// <summary>Ultima 6 Apple IIgs mode: the screen asking for another floppy, and the floppies' names.</summary>
    public DiskSwapScreen DiskSwap { get; set; } = new();

    public HudPiece? Piece(int id) => Pieces.FirstOrDefault(p => p.Id == id);
    public HudPiece? First(HudPieceKind kind) => Pieces.FirstOrDefault(p => p.Kind == kind);
    /// <summary>The main panel (the first Panel: one member's inventory or portrait); every other Panel is a custom panel.</summary>
    public bool IsCustomPanel(HudPiece piece) => piece.Kind == HudPieceKind.Panel && !ReferenceEquals(First(HudPieceKind.Panel), piece);
    public IEnumerable<HudPiece> CustomPanels => Pieces.Where(IsCustomPanel);
    /// <summary>A piece by its name (the event commands name them).</summary>
    public HudPiece? Named(string name) => Pieces.FirstOrDefault(p => string.Equals(p.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase));
    public int NextId() => Pieces.Count == 0 ? 1 : Pieces.Max(p => p.Id) + 1;
}

/// <summary>
/// Ultima 6 Apple IIgs mode, a game built as 3.5-inch floppies: the screen that asks for another disk when the game needs
/// a file on it (HUD Editor > Disk Swap screen) - a picture over the whole screen, an animation on it, a sound, and the
/// words ({0}: the disk's name) - and the disks' names (disk 1 first; more disks than names: "Game 4"...).
/// </summary>
public class DiskSwapScreen
{
    public SpriteLink Picture { get; set; } = new();
    public SpriteLink Animation { get; set; } = new();
    public int AnimationX { get; set; } = 144;
    public int AnimationY { get; set; } = 60;
    /// <summary>The words: {0} = the disk's number, {$} = its name (the build's list of disk names).</summary>
    public string Message { get; set; } = "Please insert the disk \"{$}\" and press a key.";
    public int TextX { get; set; } = 16;
    public int TextY { get; set; } = 120;
    public string TextColor { get; set; } = string.Empty;
    /// <summary>A sound (Sound Editor) played as the screen comes up; -1 none.</summary>
    public int SoundId { get; set; } = -1;
    public List<string> DiskNames { get; set; } = new();

    /// <summary>Disk n's name (1 = the first).</summary>
    public string NameOf(int disk, string game) => disk - 1 < DiskNames.Count && DiskNames[disk - 1].Trim().Length > 0 ? DiskNames[disk - 1].Trim() : $"{game} {disk}";
}
