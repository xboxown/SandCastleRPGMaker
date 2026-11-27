using System.Text;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// A character's talk (Character Editor > Talk: event commands) made into the Apple IIgs player's own small code, run by
/// its talk interpreter (Player/iigs/main.s, RunTalk). Ultima 6's way, as the Windows Ultima 6 engine: Show Text writes the
/// words in the text area (its replies' words marked to show in the keyword colour); a Show Text with replies asks
/// "you say:" and a typed word picks the reply whose first four letters it starts with. Ops (one byte, then its data;
/// places are u16 offsets from the code's start):
///   $00 END                      the talk is over
///   $01 SAY  u16 len, text, 0    words (\u0001 word \u0002: a keyword; \n: a new line)
///   $02 ASK  u8 n, n x (u8 len, keyword lower case, u16 place)    waits for the player's word
///   $03 JUMP u16 place
///   $04 SETV u16 variable, u8 op (0 set, 1 add, 2 subtract), i16 value
///   $05 IFV  u16 variable, u8 test (0 ==, 1 !=, 2 &gt;, 3 &lt;, 4 &gt;=, 5 &lt;=), i16 value, u16 place if not
///   $06 PLAY u16 sound (its number in GAME.DAT), u8 volume (0-255)
///   $07 MAP  u16 map (its number in the game; $FFFF this one), u16 x, u16 y ($FFFF: the map's start)   Change Map
///   $08 PARTY u16 map, u16 character (its place in that map's list; $FFFF $FFFF: the one talked to), u8 1 join / 0 leave
/// A map event's code (CompileEvent): its pages in order, each
///   $09 TRIG u8 trigger, u16 the next page    the page answers this trigger only
///   $0A ITEM u16 name, u8 0 carried / 1 not carried / 2 the thing dropped or picked up is it, u16 the next page
///   (IFVs for its variable conditions, jumping to the next page if they fail)
///   $0B HIT                                   the page runs: its commands, then END
/// Triggers: 1 stepped on, 2 stood on, 3 stepped off, 4 Use on it (the action button), 5 arriving on the map (Always),
/// 6 having / not having an item, 7 a thing dropped on it, 8 a thing picked up from it; a character's or an object's own:
/// 9 hit, 10 killed, 11 an ailment, 12 Used, 13 an item used on it (ITEM kind 2: that item).
/// More commands (b636; "who" = u16 map, u16 place in its list; $FFFF $FFFF the Avatar):
///   $0C GIVE u16 name, u16 sprite, u16 icon, u16 flags, u16 other   a thing to the Avatar (into a spare object)
///   $0D TAKE u16 name, u16 how many                         Remove Item from the Avatar's things
///   $0E HP   who, u8 op (0 add 1 subtract 2 set), i16 amount, u8 can kill
///   $0F STAT who, u8 the stat's word (0 hit points ... 10 experience), u8 op, i16 value
///   $10 WAIT u16 ticks (60 a second)
///   $11 PLACE who, u8 1 place / 0 remove, u16 x, u16 y
///   $12 KILL who
///   $13 SCHED who, u8 activity (as the schedules; $FF back to its schedule)
///   $14 TIME u8 0 set / 1 advance minutes / 2 advance hours, u16 hour or how many, u16 minute
///   $15 DOOR u16 x, u16 y, u8 0 locked / 1 unlocked / 2 closed / 3 open
///   $16 PIC  u16 sprite, u16 x, u16 y                       Show Picture (over the game until Erase Picture)
///   $17 ERASE                                               Erase Picture (the game drawn again)
///   $18 GAME u8 0 game over / 1 quit / 2 save / 3 load
///   $19 PAY  u16 price, u16 place if the Avatar's gold is short    (gold taken)
///   $1A SELL u16 name, u16 price, u16 place if the Avatar has none  (one taken, gold given)
///   $1B MORE                                                "<more>": a key goes on, Escape ends (a book's next page)
///   $1D CALC u16 variable, a formula (ScriptExpression.CompileIIgs: stack code ending 0)   Calculate, loops' counters
///   $1E IFX  a formula, u16 the place if it is 0 (false)         While, Repeat, Select Case, Wait Until, Advance IF
///   $1F HAGGLE u16 the price's variable, u16 asking, u16 lowest, u8 tries, u16 accepted, u16 gave up, u16 declined, then
///        C strings: the question, too low, counter, accepted, gives up ({0}: a price)   (ShopHaggle's rules)
///   $20 PAYV u16 variable, u16 the place if the gold is short   (the variable's price paid)
///   $21 MUSIC u16 sound ($FFFF stop), u8 loop, u8 volume, u16 fade ticks   Play / Fade In / Stop Music (event music)
///   $22 VOLUME a formula (music 0-100), a formula (sounds 0-100)              Set Volume
///   $25 VEHICLE u8 0 drive / 1 stop driving / 2 speed (a formula follows, cells a second) / 3 travel (u16 x, u16 y),
///        u16 the vehicle (template) - the copy nearest the Avatar   (expression op 28 u16: that vehicle travelling)
///   $26 SAYF parts (1 a C string, 2 a formula, 3 u8 a name: 1 the leveling event's someone, 2 the guard), 0 - then a new line
///   $27 RISE who, u8 flags (1 the party, 2 only if earned, 4 tell, 8 a level's hit points, 16 a shrine: Not Ready word),
///        formulas strength, dexterity, intelligence, hit points; a C string (no one ready)            Rise A Level, Meditate
///   $28 PICK u16 variable, u8 how many, their first letters                                          Scene Choice
///   $29 INPUT u8 0 text / 1 number, u16 variable, u8 longest, C string (the words above)          Input Text / Number
///   $2A LIST u8 what, u8 the array, then its values (formulas) / variables                          the array commands
///   $2B GRID u8 what, u8 the grid, then its values / variables                                      the grid commands
///   (expression ops: 29 a text, 30 an array, 31 a grid, 32 array[i], 33 grid[x, y], 34 a function, 35 the game's)
///   $24 WEATHER u8 kind (0 clear 1 rain 2 snow 3 hail 4 storm), u8 intensity, u8 clouds (0 off, 1 on, 2 as they are)
///   $23 RESUME                                                             Resume Map Music
///   (a formula's extra ops for game conditions: 24 STAT u8 the Avatar's stat word, 25 HOUR, 26 HAVE u16 name, 27 MINUTE)
///   $1C AIL  who, u8 the ailment's bit (1 poisoned, 2 asleep, 4 paralyzed, 8 charmed, 16 cursed, 32 invisible, 64 protected), u8 1 on / 0 off
/// The Shop command is made into Ultima 6 keyword talk: the merchant's line, what can be asked (the stock, the shop's
/// parts, sell, bye); a thing asked for: its price, yes / no; bought: paid, given.
/// Commands the IIgs player doesn't do yet are left out (Unsupported lists them).
/// </summary>
public sealed class AppleIIgsTalk
{
    public const byte End = 0, Say = 1, Ask = 2, Jump = 3, SetV = 4, IfV = 5, Play = 6, Map = 7, Party = 8, Trig = 9, ItemOp = 10, Hit = 11,
        Give = 12, Take = 13, Hp = 14, StatOp = 15, WaitOp = 16, Place = 17, Kill = 18, Sched = 19, Time = 20, Door = 21, Pic = 22, Erase = 23, GameOp = 24, Pay = 25, Sell = 26, More = 27, Ail = 28, Calc = 29, IfX = 30, Haggle = 31, PayV = 32, Music = 33, Volume = 34, Resume = 35, Weather = 36, Vehicle = 37, SayF = 38, Rise = 39, Pick = 40, Input = 41, ListOp = 42, GridOp = 43, Ctl = 44, Act = 45, Fac = 46, Xchg = 47, Ext = 48;
    /// <summary>Op 48 EXT's sub-commands (Player/iigs/ext.s).</summary>
    private enum ExtOp : byte { Equip = 0, Pose = 1, AttackObject = 2, Animation = 3, Picklock = 4, TakeFromContainer = 5, Follow = 6, Behavior = 7, SleepOnBed = 8, Shake = 9, Fade = 10, Flash = 11, DayNight = 12, Camera = 13, CameraFollow = 14, Look = 15, Name = 16, Resurrect = 17, CreateObject = 18, DeleteObject = 19, DeleteSelf = 20, MoveObject = 21, ObjectLook = 22, Count = 23, Inside = 24, Charges = 25, Equipped = 26, SetBox = 27, AreaFind = 28, AreaDelete = 29, AreaHurt = 30, FindThing = 31, ThingInfo = 32, Combine = 33, Learn = 34, Cast = 35, ResetStat = 36, StopSound = 37, Light = 38, BookText = 39, Log = 40, Keys = 41, HudFrame = 42, Portrait = 43, Style = 44, Scene = 45, Hud = 46, Fight = 47, GiveTo = 48 }
    /// <summary>What the commands need from the game being built (all optional).</summary>
    public sealed class Env
    {
        /// <summary>An item (Category, ItemId) -> its name, sprite, icon and object flags in the game.</summary>
        public Func<string, string, (int Name, int Sprite, int Icon, int Flags, int Other)?>? Item;
        /// <summary>A sprite (group, name) -> its picture's sprite number.</summary>
        public Func<string, string, int>? Picture;
        /// <summary>Play Pose (ext.s): (who - a character id, Player or Activator; the pose; its facing or "") -> its frames (sprite, ticks of 60); null: it has no such pose.</summary>
        public Func<string, string, string, List<(int Sprite, int Ticks)>?>? PoseFrames;
        /// <summary>Play Animation (fx.s): (sprite group, sprite name) -> its frames (sprite, ticks of 60); null: no such sprite.</summary>
        public Func<string, string, List<(int Sprite, int Ticks)>?>? SpriteFrames;
        /// <summary>A colour (#RRGGBB) -> the game palette's nearest (0-15).</summary>
        public Func<int, int, int, int>? Colour;
        /// <summary>Change Look: a character (its id, Player) -> its look's first sprite (four facings, then sleeping and sitting if PoseLooks); null: none.</summary>
        public Func<string, (int Sprite, bool PoseLooks)?>? LookOf;
        /// <summary>Get Equipped Item: the wearable things' names and Object Editor ids.</summary>
        public Func<List<(int Name, int Id)>>? WornIds;
        /// <summary>Learn / Cast Spell: a spell's place in the game's spells by its name; -1 none.</summary>
        public Func<string, int>? SpellIndex;
        /// <summary>Change Book Content (Ultima 6): a book's text by its id (its place in Texts; -1 none), and new words compiled as a text of their own.</summary>
        public Func<int, int>? BookText;
        public Func<string, int>? NewText;
        /// <summary>The project's variables (Pause Here writes each into LOG.TXT) and an error's words by its number (Show Error).</summary>
        public List<string>? VariableNames;
        /// <summary>Change HUD Frame: the HUD with this frame (group, name) as GAME.DAT's other picture n (1-4; -1 more than that).
        /// Change Portrait: a sprite as a portrait picture, and someone's own (by Who) - -1 none.</summary>
        public Func<string, string, int>? HudFrame;
        public Func<string, string, int>? PortraitPic;
        public Func<string, int>? OwnPortrait;
        /// <summary>Conversation Style's box picture: a sprite fitted to w x h (-1 none).</summary>
        public Func<string, string, int, int, int>? BoxPicture;
        /// <summary>Scenes: the Game View's size; a sprite group's sprites (their names).</summary>
        public (int W, int H) ViewSize = (320, 192);
        public Func<string, List<string>>? GroupSprites;
        /// <summary>The HUD commands: a piece by its name as a live piece (hudlive.s; -1 not one the IIgs can change), a custom panel's
        /// number (-1 none), a sprite fitted to a live piece's size.</summary>
        public Func<string, int>? LivePiece;
        public Func<string, int>? HudPanel;
        public Func<string, string, string, int>? PiecePicture;
        public Func<int, string>? ErrorWords;
        /// <summary>A stat's id -> its word in a character's numbers (0 hit points ... 10 experience), -1 not on the IIgs.</summary>
        public Func<string, int>? Stat;
        /// <summary>A word's place in the names (Order On A Target's prompt).</summary>
        public Func<string, int>? NameOf;
        /// <summary>The character an object belongs to (its Belongs To), as a Who; null: no one.</summary>
        public Func<int, string?>? OwnerOf;
        /// <summary>Exchange Currency (money.s): (from currency id, to currency id) -> the purses and the rate (8.8, the cut taken off); null: no such money.</summary>
        public Func<int, int, (int From, int To, int Rate)?>? Exchange;
        /// <summary>The Avatar's faction (Change Faction Relationship towards it; -1 none).</summary>
        public int AvatarFaction = -1;
        /// <summary>A Who's name (Show Bark).</summary>
        public Func<string, string?>? WhoName;
        /// <summary>Formulas' names: arrays, grids, characters, stats, things.</summary>
        public ScriptExpression.IIgsNames? Names;
        /// <summary>A stat's word by its name (Get Game Data's Stat).</summary>
        public Func<string, int>? StatByName;
        /// <summary>A leveling event's commands by its name (Run Leveling Event).</summary>
        public Func<string, List<EventCommand>?>? LevelEvent;
        /// <summary>A Common Event's commands (Call Common Event).</summary>
        public Func<int, List<EventCommand>?>? Common;
        /// <summary>An item's name as shown (a shop's {item} / {money}).</summary>
        public Func<ReferencedItemCategory, int, string>? ItemName;
    }
    public Env? Extra { get; set; }
    private readonly List<byte> _code = new();
    private readonly Func<string, int> _variable;
    private readonly Func<int, int>? _sound;
    private readonly Func<int, int>? _world;
    private readonly Func<string, (int Map, int Index)?>? _who;
    public HashSet<string> Unsupported { get; } = new();

    private AppleIIgsTalk(Func<string, int> variable, Func<int, int>? sound, Func<int, int>? world, Func<string, (int Map, int Index)?>? who) { _variable = variable; _sound = sound; _world = world; _who = who; }

    /// <summary>The code for a list of commands; variable(name) gives a variable's or switch's number, sound(id) a Sound Editor sound's number in GAME.DAT (-1: not there).</summary>
    /// <param name="world">a Maps-tab world's id -> its map number in the game (-1: not in it).</param>
    /// <param name="who">a Who (a character's id) -> where it is in the game (its map, its place in that map's characters).</param>
    public static AppleIIgsTalk Compile(List<EventCommand> commands, Func<string, int> variable, out byte[] code, Func<int, int>? sound = null, Func<int, int>? world = null,
        Func<string, (int Map, int Index)?>? who = null, Env? env = null)
    {
        var talk = new AppleIIgsTalk(variable, sound, world, who) { Extra = env };
        talk.Run(commands);
        code = talk._code.ToArray();
        return talk;
    }

    /// <summary>One event page as the Apple IIgs runs it: its trigger (1-8), an item it waits for (a name's number, -1 none;
    /// ItemKind 0 carried, 1 not carried, 2 the one dropped / picked up), its variable conditions (variable, test, value, Or with the one before).</summary>
    public sealed record EventPage(int Trigger, int Item, int ItemKind, List<(int Variable, int Test, int Value, bool Or)> Conditions, List<EventCommand> Commands);

    /// <summary>A map event's code: its pages in order (the first one whose trigger and conditions hold runs); mask: a bit for each trigger it has.</summary>
    public static AppleIIgsTalk CompileEvent(List<EventPage> pages, Func<string, int> variable, out byte[] code, out int mask, Func<int, int>? sound = null,
        Func<int, int>? world = null, Func<string, (int Map, int Index)?>? who = null, Env? env = null)
    {
        var talk = new AppleIIgsTalk(variable, sound, world, who) { Extra = env };
        mask = 0;
        foreach (var page in pages)
        {
            mask |= 1 << page.Trigger;
            talk.B(Trig); talk.B(page.Trigger);
            var next = new List<int> { talk.Hole() };
            if (page.Item >= 0) { talk.B(ItemOp); talk.W(page.Item); talk.B(page.ItemKind); next.Add(talk.Hole()); }
            // The conditions, left to right as the Windows evaluator: (((c0 op1 c1) op2 c2) ...)
            var falseHoles = new List<int>();
            var trueHoles = new List<int>();
            for (int i = 0; i < page.Conditions.Count; i++)
            {
                var (v, test, value, or) = page.Conditions[i];
                if (i > 0 && or)
                {
                    talk.B(Jump); trueHoles.Add(talk.Hole()); // true stays true
                    foreach (int h in falseHoles) talk.Fill(h, talk.Here);
                    falseHoles.Clear();
                }
                else
                {
                    foreach (int h in trueHoles) talk.Fill(h, talk.Here);
                    trueHoles.Clear();
                }
                talk.B(IfV); talk.W(v); talk.B(test); talk.W(value); falseHoles.Add(talk.Hole());
            }
            foreach (int h in trueHoles) talk.Fill(h, talk.Here);
            next.AddRange(falseHoles);
            talk.B(Hit);
            talk.Run(page.Commands);
            foreach (int h in next) talk.Fill(h, talk.Here);
        }
        talk.B(End);
        code = talk._code.ToArray();
        return talk;
    }

    /// <summary>A book's words read in the text area (Ultima 6): a screenful (lines of about chars letters) at a time, "<more>" between.</summary>
    public static byte[] CompileText(string text, int chars, int lines)
    {
        var talk = new AppleIIgsTalk(_ => 0, null, null, null);
        var all = new List<string>();
        static int Shown(string s) => s.Count(ch => ch is not ('\u0003' or '\u0004')) - s.Count(ch => ch == '\u0003'); // (a colour's number takes no room either)
        foreach (var paragraph in ColourMarks(text.Replace("\r", "").TrimEnd()).Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                if (line.Length > 0 && Shown(line) + 1 + Shown(word) > chars) { all.Add(line); line = ""; }
                line = line.Length == 0 ? word : line + " " + word;
            }
            all.Add(line);
        }
        string? open = null;
        for (int i = 0; i < all.Count; i += Math.Max(1, lines))
        {
            if (i > 0) talk.B(More);
            string chunk = (open is null ? "" : "\u0003" + open) + string.Join("\n", all.Skip(i).Take(Math.Max(1, lines)));
            for (int k = 0; k < chunk.Length; k++) if (chunk[k] == '\u0003' && k + 1 < chunk.Length) open = chunk[k + 1].ToString(); else if (chunk[k] == '\u0004') open = null;
            talk.SayLineMarked(chunk);
        }
        talk.B(End);
        return talk._code.ToArray();
    }

    /// <summary>The Ultima 6 actions the Apple IIgs's keys can change, in actkeys.s's order (U6Keys ids). The IIgs has no
    /// diagonal, F or Tab keys: those actions aren't on it.</summary>
    public static readonly string[] IIgsKeyActions = { "Up", "Down", "Left", "Right", "Attack", "Cast", "Talk", "Look", "Get", "Drop", "Move", "Use", "Rest", "Combat", "TogglePortrait",
        "Solo1", "Solo2", "Solo3", "Solo4", "Solo5", "Solo6", "Solo7", "Solo8", "Solo9", "PartyMode", "Save", "Load", "Quit", "Cancel" };

    /// <summary>A key's name (U8Keys: "T", "Ctrl+S", "Up", "D1", "NumPad4") as the IIgs's key code - a letter in lower case,
    /// Ctrl and a letter its control code; Shift and Alt (Option) not told apart. 0: the IIgs's keyboard has no such key.</summary>
    public static int IIgsKeyCode(string name)
    {
        var (held, key) = U8Keys.Split(name);
        if (key.Length == 1 && key[0] is >= 'A' and <= 'Z') return held.Contains("Ctrl") ? key[0] & 0x1F : char.ToLowerInvariant(key[0]);
        if (held.Contains("Ctrl")) return 0;
        if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) return key[1];
        if (key.Length == 7 && key.StartsWith("NumPad")) return key[6];
        return key switch
        {
            "Up" => 0x0B, "Down" => 0x0A, "Left" => 0x08, "Right" => 0x15, "Escape" => 0x1B, "Enter" or "Return" => 0x0D, "Space" => 0x20, "Tab" => 0x09, "Back" => 0x7F,
            "Multiply" => '*', "Add" => '+', "Subtract" => '-', "Divide" => '/', "Decimal" => '.',
            "OemTilde" => '`', "OemMinus" => '-', "OemPlus" => '=', "OemOpenBrackets" => '[', "OemCloseBrackets" => ']', "OemSemicolon" => ';', "OemQuotes" => '\'',
            "OemComma" => ',', "OemPeriod" => '.', "OemQuestion" => '/', "OemPipe" => '\\',
            _ => 0
        };
    }

    /// <summary>[COLOR=name or #RRGGBB] / [/COLOR]: the game palette's nearest colour of a name (the editor sets it; null: the codes dropped).</summary>
    public static Func<string, uint?>? ColourArgb;
    /// <summary>The colours the game's words ask for ([COLOR=...], Conversation Style), each a slot: GAME.DAT turns the slots into the
    /// palette's nearest once the palette is made (AppleIIgsGamePack.Write; talk.s / talkstyle.s ColTab). The editor empties it first.</summary>
    public static readonly List<uint> Colours = new();
    /// <summary>A colour's name or #RRGGBB as a slot (at most 64); -1 not a colour.</summary>
    public static int ColourIndex(string name)
    {
        if (ColourArgb?.Invoke(name) is not uint c) return -1;
        int i = Colours.IndexOf(c);
        if (i >= 0) return i;
        if (Colours.Count >= 64) return -1;
        Colours.Add(c);
        return Colours.Count - 1;
    }
    private static readonly System.Text.RegularExpressions.Regex ColourCode = new(@"\[COLOR=([^\]]+)\]|\[/COLOR\]", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>[COLOR=...] codes as the IIgs's: \x03 and the colour ('0' + 0-15), \x04 at its end (talk.s SayText).</summary>
    public static string ColourMarks(string text) => text.IndexOf('[') < 0 ? text : ColourCode.Replace(text, m =>
        !m.Groups[1].Success ? (ColourArgb is null ? "" : "\u0004")
        : ColourIndex(m.Groups[1].Value.Trim()) is int c and >= 0 ? "\u0003" + (char)('0' + c) : "");

    /// <summary>SAY the words (printable letters only).</summary>
    private void SayLine(string text)
    {
        text = ColourMarks(text);
        var bytes = Encoding.Latin1.GetBytes(new string(text.Select(ch => ch is '\n' or '\u0001' or '\u0002' or '\u0003' or '\u0004' || ch is >= ' ' and <= '~' ? ch : ch == '\u2019' ? '\'' : '?').Take(1000).ToArray()));
        if (bytes.Length == 0) return;
        B(Say); W(bytes.Length); _code.AddRange(bytes); B(0);
    }

    /// <summary>A scene's words caught (EXT 40 LOG 0, then SAY / SAYF): the scene op after takes them from LogBuf.</summary>
    private void SceneWords(string text)
    {
        B(Ext); B((int)ExtOp.Log); B(0);
        if (text.Trim().Length > 0) SayWords(text);
    }
    private int SceneName(string name) => name.Trim().Length == 0 ? 0xFFFF : Extra?.NameOf?.Invoke("scene:" + name.Trim()) ?? 0xFFFF;
    private static int SceneColour(string colour, int otherwise) =>
        colour.Trim() is { Length: > 0 } c && ColourIndex(c.StartsWith('#') || !System.Text.RegularExpressions.Regex.IsMatch(c, "^[0-9A-Fa-f]{6}$") ? c : "#" + c) is int i and >= 0 ? 0x8000 | i : otherwise;
    private bool _sceneView;

    /// <summary>A line into LOG.TXT (EXT 40 LOG, ext.s): each part's words, then its formula's value (a text or a number);
    /// a formula the IIgs can't work out is written as it is.</summary>
    private void LogLine(List<(string Words, string Formula)> parts)
    {
        B(Ext); B((int)ExtOp.Log); B(0);
        B(SayF);
        foreach (var (words, formula) in parts)
        {
            if (words.Length > 0) LogPart(words);
            if (formula.Trim().Length == 0) continue;
            if (ScriptExpression.CompileIIgs(formula, _variable, out _, Extra?.Names) is { } code) { B(2); _code.AddRange(code); }
            else LogPart(formula);
        }
        B(0);
        B(Ext); B((int)ExtOp.Log); B(1);
    }
    private void LogPart(string part)
    {
        var bytes = Encoding.ASCII.GetBytes(new string(part.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').Take(200).ToArray()));
        if (bytes.Length == 0) return;
        B(1); _code.AddRange(bytes); B(0);
    }
    /// <summary>"Log error generated." in the text area.</summary>
    private void LogShown() { B(Ext); B((int)ExtOp.Log); B(2); }

    /// <summary>SAY words already marked (CompileText's).</summary>
    private void SayLineMarked(string text)
    {
        var bytes = Encoding.Latin1.GetBytes(new string(text.Select(ch => ch is '\n' or '\u0001' or '\u0002' or '\u0003' or '\u0004' || ch is >= ' ' and <= '~' ? ch : ch == '\u2019' ? '\'' : '?').Take(1000).ToArray()));
        if (bytes.Length == 0) return;
        B(Say); W(bytes.Length); _code.AddRange(bytes); B(0);
    }

    /// <summary>A haggling line with {price} as {0} (the player writes the price in).</summary>
    private static string HagglePrice(string text, string item, string money) => text.Replace("{item}", item).Replace("{money}", money).Replace("{price}", "{0}");

    /// <summary>The Shop command as keyword talk (Engine/ShopModel.cs; the Windows engine's U6GameWindow.Shop.cs, without haggling).</summary>
    private void CompileShop(ShopDefinition shop)
    {
        string money = Extra?.ItemName?.Invoke(ReferencedItemCategory.Object, shop.CurrencyObjectId) is { Length: > 0 } m ? m : "gold";
        var t = shop.Texts;
        var nodeAt = new Dictionary<ShopNode, int>();
        var jumps = new List<(int Hole, ShopNode Node)>();
        var leave = new List<int>();
        int sellAt = -1;
        var sellHoles = new List<int>();
        string Key(string s) => new string(s.Trim().ToLowerInvariant().Where(ch => ch is >= ' ' and <= '~').TakeWhile(ch => ch != ' ').Take(20).ToArray());
        void AskOp(List<(string Word, Action<int> Fill)> replies)
        {
            B(AppleIIgsTalk.Ask); B(replies.Count);
            foreach (var (word, fill) in replies)
            {
                var w = Encoding.ASCII.GetBytes(word);
                B(w.Length); _code.AddRange(w);
                fill(Hole());
            }
        }
        void Node(ShopNode node, ShopNode? parent)
        {
            nodeAt[node] = Here;
            var items = node.Items.Where(i => Extra?.Item?.Invoke(i.Category.ToString(), i.ItemId.ToString()) is not null).ToList();
            var words = node.Children.Select(c => Key(c.Label)).Concat(items.Select(i => Key(Extra!.ItemName?.Invoke(i.Category, i.ItemId) ?? "thing")))
                .Concat(parent is null ? (shop.Buys.Count > 0 ? new[] { "sell" } : Array.Empty<string>()).Append("bye") : new[] { "back", "bye" }).ToList();
            SayLine(Marked(node.Line, words) + "\n" + string.Join(", ", words.Select(w => "\u0001" + w + "\u0002")));
            var replies = new List<(string, Action<int>)>();
            var itemHoles = new List<(int Hole, ShopItem Item)>();
            foreach (var c in node.Children) replies.Add((Key(c.Label), h => jumps.Add((h, c))));
            foreach (var i in items) replies.Add((Key(Extra!.ItemName?.Invoke(i.Category, i.ItemId) ?? "thing"), h => itemHoles.Add((h, i))));
            if (parent is null && shop.Buys.Count > 0) replies.Add(("sell", h => sellHoles.Add(h)));
            if (parent is not null) replies.Add(("back", h => jumps.Add((h, parent))));
            replies.Add(("bye", h => leave.Add(h)));
            AskOp(replies);
            foreach (var (hole, item) in itemHoles)
            {
                Fill(hole, Here);
                var it = Extra!.Item!.Invoke(item.Category.ToString(), item.ItemId.ToString())!.Value;
                string name = Extra.ItemName?.Invoke(item.Category, item.ItemId) ?? "thing";
                SayLine(ShopTexts.Fill(item.PriceLine.Length > 0 ? item.PriceLine : t.PriceLine, name, item.Price, money));
                int yes = -1, no = -1, haggle = -1;
                var answers = new List<(string, Action<int>)> { ("yes", h => yes = h), ("no", h => no = h) };
                if (shop.Haggle) answers.Add((Key(t.HaggleChoice), h => haggle = h));
                AskOp(answers);
                Fill(yes, Here);
                B(Pay); W(item.Price); int poor = Hole();
                int give = Here;
                B(Give); W(it.Name); W(it.Sprite); W(it.Icon); W(it.Flags); W(it.Other);
                SayLine(ShopTexts.Fill(t.Agreed, name, item.Price, money));
                B(Jump); jumps.Add((Hole(), node));
                Fill(poor, Here);
                SayLine(ShopTexts.Fill(t.NotEnoughMoney, name, item.Price, money));
                B(Jump); jumps.Add((Hole(), node));
                Fill(no, Here);
                int declined = Here;
                SayLine(ShopTexts.Fill(t.Declined, name, item.Price, money));
                B(Jump); jumps.Add((Hole(), node));
                if (haggle >= 0)
                {
                    // Haggling (ShopHaggle.Offer): the price agreed into a variable, then paid from it.
                    int priceVar = _variable($"__iigs_haggle_{_hidden++}");
                    Fill(haggle, Here);
                    int lowest = Math.Max(1, (int)Math.Ceiling(item.Price * Math.Clamp(shop.LowestPercent, 1, 100) / 100.0));
                    B(Haggle); W(priceVar); W(item.Price); W(lowest); B(Math.Clamp(shop.HaggleTries, 1, 20));
                    int accepted = Hole(), gaveUp = Hole(); W(declined);
                    string P(string text) => ShopTexts.Fill(text, name, 0, money).Replace(" 0 ", " {0} ").Replace("0 " + money, "{0} " + money);
                    foreach (var text in new[] { ShopTexts.Fill(t.HagglePrompt, name, item.Price, money), HagglePrice(t.HaggleTooLow, name, money), HagglePrice(t.HaggleCounter, name, money),
                        HagglePrice(t.HaggleAccept, name, money), HagglePrice(t.HaggleGiveUp, name, money) })
                    {
                        var bytes = Encoding.ASCII.GetBytes(new string(text.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').Take(120).ToArray()));
                        _code.AddRange(bytes); B(0);
                    }
                    Fill(accepted, Here);
                    B(PayV); W(priceVar); int poor2 = Hole();
                    B(Jump); W(give + 0); // (given; the Agreed words)
                    Fill(poor2, Here);
                    SayLine(ShopTexts.Fill(t.NotEnoughMoney, name, item.Price, money));
                    B(Jump); jumps.Add((Hole(), node));
                    Fill(gaveUp, Here); // his last price: yes or no
                    int y2 = -1, n2 = -1;
                    AskOp(new() { ("yes", h => y2 = h), ("no", h => n2 = h) });
                    Fill(y2, accepted);
                    Fill(n2, declined);
                }
            }
            foreach (var c in node.Children) Node(c, node);
        }
        Node(shop.Root, null);
        if (shop.Buys.Count > 0)
        {
            sellAt = Here;
            var buys = shop.Buys.Where(b => Extra?.Item?.Invoke(b.Category.ToString(), b.ItemId.ToString()) is not null).ToList();
            SayLine(t.SellLine + "\n" + string.Join(", ", buys.Select(b => "\u0001" + Key(Extra!.ItemName?.Invoke(b.Category, b.ItemId) ?? "thing") + "\u0002").Append("\u0001back\u0002")));
            var holes = new List<(int Hole, ShopBuy Buy)>();
            var replies = buys.Select(b => (Key(Extra!.ItemName?.Invoke(b.Category, b.ItemId) ?? "thing"), (Action<int>)(h => holes.Add((h, b))))).ToList();
            replies.Add(("back", h => jumps.Add((h, shop.Root))));
            AskOp(replies);
            foreach (var (hole, b) in holes)
            {
                Fill(hole, Here);
                var it = Extra!.Item!.Invoke(b.Category.ToString(), b.ItemId.ToString())!.Value;
                string name = Extra.ItemName?.Invoke(b.Category, b.ItemId) ?? "thing";
                B(Sell); W(it.Name); W(b.Price); int none = Hole();
                SayLine(ShopTexts.Fill(t.SellAgreed, name, b.Price, money));
                B(Jump); jumps.Add((Hole(), shop.Root));
                Fill(none, Here);
                SayLine(ShopTexts.Fill(t.NothingToSell, name, b.Price, money));
                B(Jump); jumps.Add((Hole(), shop.Root));
            }
        }
        foreach (int h in sellHoles) Fill(h, sellAt);
        foreach (var (h, n) in jumps) Fill(h, nodeAt[n]);
        foreach (int h in leave) Fill(h, Here);
        SayLine(t.Farewell);
    }

    private sealed class Frame
    {
        public bool IsChoices;
        public int Indent;
        // a branch
        public int ElseAt = -1;
        // replies
        public int AskAt;
        public Dictionary<int, int> TargetAt = new();
        public Dictionary<int, bool> Pinned = new();
        public int? Current;
        public List<int> EndAt = new();
    }

    private int Here => _code.Count;
    private void B(int v) => _code.Add((byte)v);
    private void W(int v) { _code.Add((byte)v); _code.Add((byte)(v >> 8)); }
    private int Hole() { int at = Here; W(0); return at; }
    private void Fill(int at, int value) { _code[at] = (byte)value; _code[at + 1] = (byte)(value >> 8); }

    private sealed class Loop
    {
        public EventCommandType Kind;
        public int Top, Continue = -1, NextCase = -1, Select = -1, Step = -1;
        public List<int> Ends = new(), Nexts = new();
    }
    private readonly Stack<Loop> _loops = new();
    private int _hidden;

    /// <summary>A formula into the code (its stack code); false if the Apple IIgs can't do it.</summary>
    private bool Formula(string formula, string what)
    {
        var code = ScriptExpression.CompileIIgs(formula.Trim().Length == 0 ? "0" : formula, _variable, out _, Extra?.Names);
        if (code is null) { Unsupported.Add(what + " (a formula with texts, lists or game functions)"); B(1); W(0); B(0); return false; }
        _code.AddRange(code);
        return true;
    }

    /// <summary>A While / Wait Until's condition: its formula, or its game condition (Advance IF).</summary>
    private void Condition(EventCommand cmd, string what)
    {
        if (cmd.Get("Mode") == "Game") GameCondition(GameConditions.FromCommand(cmd), what);
        else Formula(cmd.Get("Formula"), what);
    }

    /// <summary>An Advance IF game condition as a formula: variables, having or not having an item, the Avatar's stats, the time.</summary>
    private void GameCondition(GameCondition? c, string what)
    {
        int Cmp(ConditionComparisonOperator op) => op switch { ConditionComparisonOperator.NotEqual => 10, ConditionComparisonOperator.LessThan => 11, ConditionComparisonOperator.GreaterThan => 12,
            ConditionComparisonOperator.LessOrEqual => 13, ConditionComparisonOperator.GreaterOrEqual => 14, _ => 9 };
        if (c is null) { B(1); W(0); B(0); return; }
        switch (c.Kind)
        {
            case GameConditionKind.Variable:
                B(2); W(_variable(c.VariableName)); B(1); W(int.TryParse(c.Value, out int v) ? v : bool.TryParse(c.Value, out bool bv) && bv ? 1 : 0); B(Cmp(c.Comparison));
                break;
            case GameConditionKind.HavingItem or GameConditionKind.NotHavingItem when c.Item is { } it && Extra?.Item?.Invoke(it.Category.ToString(), it.ItemId.ToString()) is { } info:
                B(26); W(info.Name);
                if (c.Kind == GameConditionKind.NotHavingItem) B(17);
                break;
            case GameConditionKind.Stat when Extra?.Stat?.Invoke(c.StatId.ToString()) is int word and >= 0:
                B(24); B(word); B(1); W(int.TryParse(c.Value, out int sv) ? sv : 0); B(Cmp(c.Comparison));
                break;
            case GameConditionKind.TimeDate:
                B(25); B(1); W(c.Number); B(14); B(25); B(1); W(c.Number2 >= c.Number ? c.Number2 : 23); B(13); B(15);
                break;
            default:
                Unsupported.Add($"{what} ({c.Kind} game condition)");
                B(1); W(0);
                break;
        }
        if (c.Not) B(17);
        B(0);
    }

    /// <summary>
    /// Words with {formulas} in them (Windows' SceneWords): the words, each {formula}'s value, {LevelWho} / {RestGuard}
    /// (names), then a new line. Without any: a plain line.
    /// </summary>
    private void SayWords(string text)
    {
        text = text.Replace("\r", "").Trim();
        if (text.Length == 0) return;
        if (!text.Contains('{')) { SayLine(text); return; }
        B(SayF);
        int at = 0;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"\{([^{}]+)\}"))
        {
            if (m.Index > at) SayPart(text[at..m.Index]);
            string inner = m.Groups[1].Value.Trim();
            if (inner.Equals("LevelWho", StringComparison.OrdinalIgnoreCase)) { B(3); B(1); }
            else if (inner.Equals("RestGuard", StringComparison.OrdinalIgnoreCase)) { B(3); B(2); }
            else if (ScriptExpression.CompileIIgs(inner, _variable, out _, Extra?.Names) is { } code) { B(2); _code.AddRange(code); }
            else { Unsupported.Add("words with a {text formula} (only numbers, LevelWho and RestGuard on the Apple IIgs)"); SayPart(m.Value); }
            at = m.Index + m.Length;
        }
        if (at < text.Length) SayPart(text[at..]);
        B(0);
    }
    private void SayPart(string part)
    {
        part = ColourMarks(part); // [COLOR=...]
        var bytes = Encoding.Latin1.GetBytes(new string(part.Select(ch => ch is '\n' or '\u0003' or '\u0004' || ch is >= ' ' and <= '~' ? ch : ch == '\u2019' ? '\'' : '?').Take(400).ToArray()));
        if (bytes.Length == 0) return;
        B(1); _code.AddRange(bytes); B(0);
    }

    /// <summary>A fade's seconds in ticks (60 a second, at most a minute).</summary>
    private int FadeTicks(EventCommand cmd, double least)
    {
        var code = ScriptExpression.CompileIIgs(cmd.Get("Seconds").Trim().Length == 0 ? "0" : cmd.Get("Seconds"), _variable, out _);
        double s = double.TryParse(cmd.Get("Seconds"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
        if (code is null || (s == 0 && cmd.Get("Seconds").Trim() is { Length: > 0 } t && t != "0")) Unsupported.Add(cmd.Type + " (a fade time that's a formula: a number of seconds only)");
        return (int)Math.Clamp(Math.Max(least, s) * 60, 0, 3600);
    }

    private static string Or0(string formula) => formula.Trim().Length == 0 ? "0" : formula;

    private int Hidden(string name) => _variable($"__iigs_{name}_{_hidden++}");

    private void Run(List<EventCommand> commands, int depth = 0)
    {
        var frames = new Stack<Frame>();
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var gotos = new List<(int At, string Label)>();
        foreach (var cmd in commands)
        {
            switch (cmd.Type)
            {
                case var ultima7 when ScriptCommands.NotInUltima6.Contains(ultima7) && ultima7 != EventCommandType.ShowBark:
                    Unsupported.Add($"{ultima7} (an Ultima 7 command - not in Ultima 6)");
                    break;
                case EventCommandType.ShowText:
                {
                    var choices = DialogChoices.HasChoices(cmd) ? DialogChoices.Read(cmd) : new List<DialogChoice>();
                    string text = Marked(cmd.Get("Text").Replace("\r", "").Trim(), choices.Select(c => c.Text).Where(t => t.Trim().Length > 0).ToList());
                    if (text.Length > 0)
                    {
                        text = ColourMarks(text); // [COLOR=...] (talk.s SayText)
                        var bytes = Encoding.Latin1.GetBytes(new string(text.Select(ch => ch is '\n' or '\u0001' or '\u0002' or '\u0003' or '\u0004' || ch is >= ' ' and <= '~' ? ch : ch == '’' ? '\'' : '?').Take(1000).ToArray()));
                        B(Say); W(bytes.Length); _code.AddRange(bytes); B(0);
                    }
                    if (choices.Count > 0)
                    {
                        var frame = new Frame { IsChoices = true, Indent = cmd.IndentLevel, AskAt = Here };
                        B(Ask); B(Math.Min(choices.Count, 40));
                        foreach (var c in choices.Take(40))
                        {
                            var word = Encoding.ASCII.GetBytes(new string(c.Text.Trim().ToLowerInvariant().Where(ch => ch is >= ' ' and <= '~').Take(30).ToArray()));
                            B(word.Length); _code.AddRange(word);
                            frame.TargetAt[c.Id] = Hole();
                            frame.Pinned[c.Id] = c.Pinned;
                        }
                        frames.Push(frame);
                    }
                    break;
                }
                case EventCommandType.WhenChoice when frames.TryPeek(out var f) && f.IsChoices:
                {
                    LeaveBranch(f);
                    if (int.TryParse(cmd.Get(DialogChoices.ChoiceIdKey), out int id) && f.TargetAt.TryGetValue(id, out int at)) { Fill(at, Here); f.TargetAt.Remove(id); }
                    f.Current = int.TryParse(cmd.Get(DialogChoices.ChoiceIdKey), out int cur) ? cur : null;
                    break;
                }
                case EventCommandType.EndChoices when frames.TryPeek(out var f) && f.IsChoices:
                {
                    LeaveBranch(f);
                    frames.Pop();
                    foreach (int at in f.EndAt.Concat(f.TargetAt.Values)) Fill(at, Here); // a reply with no branch: on after the replies
                    break;
                }
                case EventCommandType.ConditionalBranchVariable or EventCommandType.ConditionalBranchSwitch:
                {
                    bool isSwitch = cmd.Type == EventCommandType.ConditionalBranchSwitch;
                    string name = isSwitch ? cmd.Get("SwitchName") : cmd.Get("VariableName");
                    int test = isSwitch ? 0 : cmd.Get("Comparison").Trim() switch { "!=" or "<>" => 1, ">" => 2, "<" => 3, ">=" => 4, "<=" => 5, _ => 0 };
                    string raw = cmd.Get("Value").Trim();
                    if (!isSwitch && raw.Length > 0 && !double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _) && !bool.TryParse(raw, out _))
                    {
                        // A text: compared as Windows does (without case).
                        string t = raw.Length >= 2 && raw[0] == '"' && raw[^1] == '"' ? raw[1..^1] : raw;
                        var bytes = Encoding.ASCII.GetBytes(new string(t.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').Take(47).ToArray()));
                        B(IfX); B(2); W(_variable(name)); B(29); B(bytes.Length); _code.AddRange(bytes);
                        B(test switch { 1 => 10, 2 => 12, 3 => 11, 4 => 14, 5 => 13, _ => 9 }); B(0);
                        frames.Push(new Frame { Indent = cmd.IndentLevel, ElseAt = Hole() });
                        break;
                    }
                    int value = isSwitch ? (bool.TryParse(cmd.Get("Value"), out bool on) && on ? 1 : 0) : Number(cmd.Get("Value"));
                    B(IfV); W(_variable(name)); B(test); W(value);
                    frames.Push(new Frame { Indent = cmd.IndentLevel, ElseAt = Hole() });
                    break;
                }
                case EventCommandType.Else when frames.TryPeek(out var f) && !f.IsChoices:
                    B(Jump); f.EndAt.Add(Hole());
                    Fill(f.ElseAt, Here); f.ElseAt = -1;
                    break;
                case EventCommandType.EndBranch when frames.TryPeek(out var f) && !f.IsChoices:
                    frames.Pop();
                    if (f.ElseAt >= 0) Fill(f.ElseAt, Here);
                    foreach (int at in f.EndAt) Fill(at, Here);
                    break;
                case EventCommandType.SetVariable:
                {
                    int op = cmd.Get("Operation").Trim().ToLowerInvariant() switch { "add" => 1, "subtract" => 2, _ => 0 };
                    if (!int.TryParse(cmd.Get("Value").Trim(), out int value)) { Unsupported.Add("Set Variable to a formula"); break; }
                    B(SetV); W(_variable(cmd.Get("VariableName"))); B(op); W(value);
                    break;
                }
                case EventCommandType.SetSwitch:
                    B(SetV); W(_variable(cmd.Get("SwitchName"))); B(0); W(bool.TryParse(cmd.Get("Value"), out bool sw) && sw ? 1 : 0);
                    break;
                case EventCommandType.Label:
                    labels[cmd.Get("LabelName")] = Here;
                    break;
                case EventCommandType.GoToLabel:
                    B(Jump); gotos.Add((Hole(), cmd.Get("LabelName")));
                    break;
                case EventCommandType.PlaySound:
                {
                    int n = int.TryParse(cmd.Get("SoundResourceId"), out int id) && _sound is not null ? _sound(id) : -1;
                    if (n < 0) { Unsupported.Add("Play Sound (a sound that isn't in the game)"); break; }
                    double volume = double.TryParse(cmd.Get("Volume"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 1.0;
                    B(Play); W(n); B((int)Math.Clamp(volume * 255, 0, 255));
                    break;
                }
                case EventCommandType.ChangeMap:
                {
                    int map = 0xFFFF;
                    if (int.TryParse(cmd.Get("World"), out int worldId) && worldId > 0)
                    {
                        int n = _world?.Invoke(worldId) ?? -1;
                        if (n < 0) { Unsupported.Add("Change Map (to a map that isn't in the game)"); break; }
                        map = n;
                    }
                    bool spot = int.TryParse(cmd.Get("X"), out int x) & int.TryParse(cmd.Get("Y"), out int y);
                    B(Map); W(map); W(spot ? x : 0xFFFF); W(spot ? y : 0xFFFF);
                    break;
                }
                case EventCommandType.JoinParty:
                {
                    string w = cmd.Get("Who");
                    (int Map, int Index)? at = w is "" or "Activator" ? (0xFFFF, 0xFFFF) : _who?.Invoke(w);
                    if (at is not { } place) { Unsupported.Add("Join / Leave Party (someone not on a map of the game)"); break; }
                    B(Party); W(place.Map); W(place.Index); B(cmd.Get("Value") == "Leave the party" ? 0 : 1);
                    break;
                }
                case EventCommandType.GiveItem:
                {
                    if (Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId")) is not { } it) { Unsupported.Add("Give Item (an item not on the IIgs)"); break; }
                    int count = Math.Clamp(Number(cmd.Get("Quantity")), 1, 8);
                    if (cmd.Get("Receiver") is "" or "Player" or "Activator")
                    {
                        for (int i = 0; i < count; i++) { B(Give); W(it.Name); W(it.Sprite); W(it.Icon); W(it.Flags); W(it.Other); }
                        break;
                    }
                    // EXT 48 GIVETO (ext.s): someone else carries it.
                    if (!WhoCode(cmd.Get("Receiver"), out int gm, out int gx)) { Unsupported.Add("Give Item (to someone not on a map)"); break; }
                    for (int i = 0; i < count; i++) { B(Ext); B((int)ExtOp.GiveTo); W(gm); W(gx); W(it.Name); W(it.Sprite); W(it.Icon); W(it.Flags); W(it.Other); }
                    break;
                }
                case EventCommandType.EquipItem:
                case EventCommandType.UnequipItem:
                {
                    // EXT 0 EQUIP who name on (ext.s): only weapons and armour have slots on the IIgs.
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x) || Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId")) is not { } it)
                    { Unsupported.Add($"{(cmd.Type == EventCommandType.EquipItem ? "Equip" : "Unequip")} Item (someone not on a map, or an item not on the IIgs)"); break; }
                    B(Ext); B((int)ExtOp.Equip); W(m); W(x); W(it.Name); B(cmd.Type == EventCommandType.EquipItem ? 1 : 0);
                    break;
                }
                case EventCommandType.AttackObject:
                {
                    // EXT 2 ATKOBJ who x y name (ext.s, break.s): the thing - the object (or container) of that name nearest x, y.
                    var thing = cmd.Get("Thing").Split(':');
                    bool thingVar = cmd.Get("Thing").StartsWith("Var:");
                    int? thingName = thingVar ? 0xFFFE : thing.Length == 3 && thing[0] is "Object" or "Item" ? Extra?.Item?.Invoke(thing[0] == "Object" ? "Object" : thing[1], thing[2])?.Name : null;
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x) || thingName is null) { Unsupported.Add("Attack Object (someone not on a map, or a thing not on the IIgs)"); break; }
                    B(Ext); B((int)ExtOp.AttackObject); W(m); W(x); W(thingVar ? _variable(cmd.Get("Thing")[4..]) : Number(cmd.Get("X"))); W(thingVar ? 0 : Number(cmd.Get("Y"))); W(thingName.Value);
                    break;
                }
                case EventCommandType.PlayAnimation:
                {
                    // EXT 3 ANIM where a b wait n ticks frames (ext.s, fx.s): "Done" = played Duration times through.
                    var animFrames = Extra?.SpriteFrames?.Invoke(cmd.Get("SpriteGroup"), cmd.Get("SpriteName"));
                    bool overWho = cmd.Get("Where") == "Character";
                    int am = 0xFFFF, ax = 0xFFFF;
                    if (animFrames is not { Count: > 0 } || overWho && !WhoCode(cmd.Get("Who"), out am, out ax))
                    { Unsupported.Add("Play Animation (a sprite not in the game, or someone not on a map)"); break; }
                    double amount = Seconds(cmd.Get("Duration")) is > 0 and var amt ? amt : 1;
                    int once = animFrames.Take(8).Sum(f => Math.Clamp(f.Ticks, 1, 255));
                    int ticks = cmd.Get("Unit") switch { "Seconds" => (int)(amount * 60), "Minutes" => (int)(amount * 300), "Hours" => (int)(amount * 18000), _ => once * Math.Max(1, (int)Math.Round(amount)) };
                    B(Ext); B((int)ExtOp.Animation); B(overWho ? 1 : 0);
                    if (overWho) { W(am); W(ax); } else { W(Number(cmd.Get("X"))); W(Number(cmd.Get("Y"))); }
                    B(cmd.Get("Wait") != "False" ? 1 : 0); B(Math.Min(animFrames.Count, 8)); W(Math.Clamp(ticks, 1, 65535));
                    foreach (var (fs, ft) in animFrames.Take(8)) { W(fs); B(Math.Clamp(ft, 1, 255)); }
                    break;
                }
                case EventCommandType.ChangeLook:
                {
                    // EXT 15 LOOK who own sprite poselooks (ext.s).
                    bool own = cmd.Get("Own") == "True";
                    var look = own ? (0, false) : Extra?.LookOf?.Invoke(cmd.Get("Other"));
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x) || look is null) { Unsupported.Add("Change Look (someone not on a map, or a look not in the game)"); break; }
                    B(Ext); B((int)ExtOp.Look); W(m); W(x); B(own ? 1 : 0); W(look.Value.Item1); B(look.Value.Item2 ? 1 : 0);
                    break;
                }
                case EventCommandType.Resurrect:
                {
                    // EXT 17 RES who resurrect where x y hp (ext.s): the Avatar can't be raised on the IIgs (its death ends the game there).
                    bool raise = cmd.Get("Value") == "Resurrect";
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x) || raise && m == 0xFFFF) { Unsupported.Add("Resurrect (the Avatar, or someone not on a map)"); break; }
                    bool spot = cmd.Get("Where") == "At a spot";
                    int hp = cmd.Get("Health") == "This many hit points" ? Math.Clamp(Number(cmd.Get("HP")), 1, 32767) : 0xFFFF;
                    B(Ext); B((int)ExtOp.Resurrect); W(m); W(x); B(raise ? 1 : 0); B(spot ? 1 : 0); W(spot ? Number(cmd.Get("X")) : 0); W(spot ? Number(cmd.Get("Y")) : 0); W(hp);
                    break;
                }
                case EventCommandType.CreateObject:
                {
                    // EXT 18 CREATE (ext.s): at most 8 at once.
                    if (Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId")) is not { } ci) { Unsupported.Add("Create Object (a thing not on the IIgs)"); break; }
                    B(Ext); B((int)ExtOp.CreateObject); W(ci.Name); W(ci.Sprite); W(ci.Icon); W(ci.Flags); W(ci.Other); W(Number(cmd.Get("X"))); W(Number(cmd.Get("Y")));
                    B(Math.Clamp(Number(cmd.Get("Quantity")), 1, 8));
                    break;
                }
                case EventCommandType.DeleteObject:
                case EventCommandType.MoveObject:
                case EventCommandType.ChangeObjectLook:
                {
                    // EXT 19 / 21 / 22 (ext.s): the thing - the one of that name at its spot (a thing from a variable: Find Thing, item 20).
                    var tp = cmd.Get("Thing").Split(':');
                    bool fromVar = cmd.Get("Thing").StartsWith("Var:");
                    int? tn = fromVar ? 0xFFFE : tp.Length == 3 && tp[0] is "Object" or "Item" ? Extra?.Item?.Invoke(tp[0] == "Object" ? "Object" : tp[1], tp[2])?.Name : null;
                    if (tn is null) { Unsupported.Add($"{ScriptCommands.Of(cmd.Type)?.Name ?? cmd.Type.ToString()} (a thing not on the IIgs)"); break; }
                    int tx = fromVar ? _variable(cmd.Get("Thing")[4..]) : Number(cmd.Get("X")), ty = fromVar ? 0 : Number(cmd.Get("Y"));
                    if (cmd.Type == EventCommandType.DeleteObject) { B(Ext); B((int)ExtOp.DeleteObject); W(tx); W(ty); W(tn.Value); }
                    else if (cmd.Type == EventCommandType.MoveObject) { B(Ext); B((int)ExtOp.MoveObject); W(tx); W(ty); W(tn.Value); W(Number(cmd.Get("ToX"))); W(Number(cmd.Get("ToY"))); }
                    else
                    {
                        bool own = cmd.Get("Own") == "True";
                        int spr = own ? 0 : Extra?.SpriteFrames?.Invoke(cmd.Get("SpriteGroup"), cmd.Get("SpriteName")) is { Count: > 0 } fr ? fr[0].Sprite : -1;
                        if (spr < 0) { Unsupported.Add("Change Object Look (a sprite not in the game)"); break; }
                        B(Ext); B((int)ExtOp.ObjectLook); W(tx); W(ty); W(tn.Value); B(own ? 1 : 0); W(spr);
                    }
                    break;
                }
                case EventCommandType.CountItems:
                {
                    // EXT 23 COUNT var name mode who x y (ext.s): each object one (stacks are single things on the IIgs).
                    string where = cmd.Get("Where");
                    int mode = where == "The whole party" ? 1 : where == "A container on the map" ? 2 : 0;
                    int m = 0xFFFF, x = 0xFFFF;
                    if (Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId")) is not { } cn || mode == 0 && !WhoCode(cmd.Get("Who"), out m, out x))
                    { Unsupported.Add("Count Items (an item not on the IIgs, or someone not on a map)"); break; }
                    B(Ext); B((int)ExtOp.Count); W(_variable(cmd.Get("Variable"))); W(cn.Name); B(mode); W(m); W(x);
                    W(mode == 2 ? Number(cmd.Get("X")) : 0); W(mode == 2 ? Number(cmd.Get("Y")) : 0);
                    break;
                }
                case EventCommandType.ItemsInside:
                {
                    // EXT 24 INSIDE who bag name var remove (ext.s).
                    string bag = cmd.Get("BagName").Trim();
                    int name = cmd.Get("Category").Length == 0 || !int.TryParse(cmd.Get("ItemId"), out int iid) || iid < 0 ? 0xFFFF : Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId"))?.Name ?? -1;
                    if (bag.Length == 0 || name < 0 || Extra?.NameOf is null || !WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Items Inside (no bag, an item not on the IIgs, or someone not on a map)"); break; }
                    B(Ext); B((int)ExtOp.Inside); W(m); W(x); W(Extra.NameOf(bag)); W(name); W(_variable(cmd.Get("Variable"))); B(cmd.Get("Remove") == "True" ? 1 : 0);
                    break;
                }
                case EventCommandType.ItemCharges:
                {
                    // EXT 25 CHARGES who name op var amountvar (ext.s): the amount's formula worked out first, into a hidden variable.
                    if (Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId")) is not { } chi || !WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Item Charges (an item not on the IIgs, or someone not on a map)"); break; }
                    int amountVar = Hidden("charges");
                    B(Calc); W(amountVar); Formula(cmd.Get("Amount"), "Item Charges");
                    string chVar = cmd.Get("Variable").Trim();
                    B(Ext); B((int)ExtOp.Charges); W(m); W(x); W(chi.Name); B(cmd.Get("Operation") switch { "Set" => 1, "Add" => 2, _ => 0 });
                    W(chVar.Length > 0 ? _variable(chVar) : 0xFFFF); W(amountVar);
                    break;
                }
                case EventCommandType.GetEquippedItem:
                {
                    // EXT 26 EQUIPPED who slot var namevar n (name, id)... (ext.s): the IIgs's slot is EquipmentSlot + 1.
                    if (!Enum.TryParse<EquipmentSlot>(cmd.Get("Slot"), out var slot) || !WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Get Equipped Item (someone not on a map)"); break; }
                    var ids = (Extra?.WornIds?.Invoke() ?? new()).Take(60).ToList();
                    string nameVar = cmd.Get("NameVariable").Trim();
                    B(Ext); B((int)ExtOp.Equipped); W(m); W(x); B((int)slot + 1); W(_variable(cmd.Get("Variable"))); W(nameVar.Length > 0 ? _variable(nameVar) : 0xFFFF);
                    B(ids.Count); foreach (var (wn, wid) in ids) { W(wn); W(wid); }
                    break;
                }
                case EventCommandType.SetContainerContents:
                {
                    // EXT 27 SETBOX x y boxname empty name sprite icon flags other count (ext.s): at most 8 put in.
                    var bp = cmd.Get("Thing").Split(':');
                    bool boxVar = cmd.Get("Thing").StartsWith("Var:");
                    int? boxName = boxVar ? 0xFFFE : bp.Length == 3 && bp[0] is "Object" or "Item" ? Extra?.Item?.Invoke(bp[0] == "Object" ? "Object" : bp[1], bp[2])?.Name : null;
                    bool put = cmd.Get("Category").Length > 0 && int.TryParse(cmd.Get("ItemId"), out int pid) && pid >= 0;
                    var pi = put ? Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId")) : null;
                    if (boxName is null || put && pi is null) { Unsupported.Add("Set Container Contents (a container from a variable, or a thing not on the IIgs)"); break; }
                    B(Ext); B((int)ExtOp.SetBox); W(boxVar ? _variable(cmd.Get("Thing")[4..]) : Number(cmd.Get("X"))); W(boxVar ? 0 : Number(cmd.Get("Y"))); W(boxName.Value); B(cmd.Get("Empty") == "True" ? 1 : 0);
                    if (pi is { } it) { W(it.Name); W(it.Sprite); W(it.Icon); W(it.Flags); W(it.Other); } else { W(0xFFFF); W(0); W(0); W(0); W(0xFFFF); }
                    B(Math.Clamp(Number(cmd.Get("Quantity")), 1, 8));
                    break;
                }
                case EventCommandType.FindObjectsInArea:
                case EventCommandType.DeleteObjectsInArea:
                case EventCommandType.HurtInArea:
                {
                    // EXT 28 / 29 / 30 (ext.s): the centre - who, a spot, this object; the formulas worked out first into hidden variables.
                    string around = cmd.Get("Around");
                    int mode = around == "A spot (below)" ? 1 : around == "This object" ? 2 : 0;
                    int m = 0xFFFF, x = 0xFFFF;
                    if (mode == 0 && !WhoCode(cmd.Get("Who"), out m, out x)) { Unsupported.Add("Area commands (someone not on a map)"); break; }
                    if (cmd.Type == EventCommandType.HurtInArea && mode != 0 && !string.IsNullOrEmpty(cmd.Get("Who"))) WhoCode(cmd.Get("Who"), out m, out x);
                    int name = 0xFFFF;
                    if (cmd.Type != EventCommandType.HurtInArea && cmd.Get("Category").Length > 0 && int.TryParse(cmd.Get("ItemId"), out int aid) && aid >= 0)
                    {
                        if (Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId")) is not { } ai) { Unsupported.Add("Area commands (a thing not on the IIgs)"); break; }
                        name = ai.Name;
                    }
                    int cellsVar = Hidden("cells");
                    B(Calc); W(cellsVar); Formula(cmd.Get("Cells"), "Area commands");
                    int lit = cmd.Get("OnlyLit") == "True" ? 1 : 0;
                    void Centre() { B(mode); W(m); W(x); W(mode == 1 ? Number(cmd.Get("X")) : 0); W(mode == 1 ? Number(cmd.Get("Y")) : 0); W(cellsVar); }
                    if (cmd.Type == EventCommandType.FindObjectsInArea) { B(Ext); B((int)ExtOp.AreaFind); W(_variable(cmd.Get("Variable"))); W(name); B(lit); Centre(); }
                    else if (cmd.Type == EventCommandType.DeleteObjectsInArea)
                    {
                        int mostVar = Hidden("most");
                        B(Calc); W(mostVar); Formula(cmd.Get("Most"), "Delete Objects In Area");
                        B(Ext); B((int)ExtOp.AreaDelete); W(name); B(lit); W(mostVar); Centre();
                    }
                    else
                    {
                        int amountVar = Hidden("amount");
                        B(Calc); W(amountVar); Formula(cmd.Get("Amount"), "Hurt In Area");
                        int dtype = Enum.TryParse<DamageType>(cmd.Get("DamageType"), out var dt) ? (int)dt : 0;
                        int aff = cmd.Get("Affects") switch { "Enemies of Who" => 1, "Only those with a rule for this type" => 2, _ => 0 };
                        B(Ext); B((int)ExtOp.AreaHurt); W(amountVar); B(dtype); B(aff); Centre();
                    }
                    break;
                }
                case EventCommandType.FindThing:
                {
                    // EXT 31 FINDTHING mode who x y name var (ext.s).
                    string find = cmd.Get("Find");
                    int mode = find.StartsWith("The body") ? 0 : find.StartsWith("The nearest of an item to someone") ? 1 : find.StartsWith("The nearest of an item to a spot") ? 2 : 3;
                    int m = 0xFFFF, x = 0xFFFF, fname = 0xFFFF;
                    if (mode < 2 && !WhoCode(cmd.Get("Who"), out m, out x)) { Unsupported.Add("Find Thing (someone not on a map)"); break; }
                    if (mode is 1 or 2)
                    {
                        if (Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId")) is not { } fi) { Unsupported.Add("Find Thing (a thing not on the IIgs)"); break; }
                        fname = fi.Name;
                    }
                    B(Ext); B((int)ExtOp.FindThing); B(mode); W(m); W(x); W(Number(cmd.Get("AtX"))); W(Number(cmd.Get("AtY"))); W(fname); W(_variable(cmd.Get("Variable")));
                    break;
                }
                case EventCommandType.ThingInfo:
                {
                    // EXT 32 THINGINFO handlevar xvar yvar floorvar namevar (ext.s): the handle's formula worked out first.
                    int handleVar = Hidden("thing");
                    B(Calc); W(handleVar); Formula(cmd.Get("Value"), "Thing Info");
                    int V(string key) => cmd.Get(key).Trim() is { Length: > 0 } n ? _variable(n) : 0xFFFF;
                    B(Ext); B((int)ExtOp.ThingInfo); W(handleVar); W(V("XVariable")); W(V("YVariable")); W(V("FloorVariable")); W(V("NameVariable"));
                    break;
                }
                case EventCommandType.CombineItems:
                {
                    // EXT 33 COMBINE who name1 n1 name2 n2 made n3 missing (ext.s): how many - plain numbers.
                    var a1 = Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId"));
                    var a2 = Extra?.Item?.Invoke(cmd.Get("Category2"), cmd.Get("ItemId2"));
                    var a3 = Extra?.Item?.Invoke(cmd.Get("Category3"), cmd.Get("ItemId3"));
                    if (a1 is null || a2 is null || a3 is null || !WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Combine Items (a thing not on the IIgs, or someone not on a map)"); break; }
                    string missing = cmd.Get("Missing").Trim();
                    B(Ext); B((int)ExtOp.Combine); W(m); W(x);
                    W(a1.Value.Name); W(Math.Clamp(Number(cmd.Get("Quantity")), 1, 99)); W(a2.Value.Name); W(Math.Clamp(Number(cmd.Get("Quantity2")), 1, 99));
                    W(a3.Value.Name); W(a3.Value.Sprite); W(a3.Value.Icon); W(a3.Value.Flags); W(a3.Value.Other); W(Math.Clamp(Number(cmd.Get("Quantity3")), 1, 8));
                    W(missing.Length > 0 && Extra?.NameOf is not null ? Extra.NameOf(missing) : 0xFFFF);
                    break;
                }
                case EventCommandType.ChangeSelfBookContent:
                case EventCommandType.ChangeExternalBookContent:
                {
                    // EXT 39 BOOKTEXT self old new (ext.s): an Ultima 6 book's notepad (page -1); a page frame's words: not on the IIgs.
                    bool selfBook = cmd.Type == EventCommandType.ChangeSelfBookContent;
                    int old = selfBook ? 0 : int.TryParse(cmd.Get("TargetBookId"), out int tb) ? Extra?.BookText?.Invoke(tb) ?? -1 : -1;
                    if (cmd.Get("PageIndex") != "-1" || old < 0 || Extra?.NewText is null) { Unsupported.Add("Change Book Content (a page's frame - Ultima 6 books are their notepad)"); break; }
                    B(Ext); B((int)ExtOp.BookText); B(selfBook ? 1 : 0); W(old); W(Extra.NewText(cmd.Get("ReplacementXaml")));
                    break;
                }
                case EventCommandType.LightSource:
                {
                    // EXT 38 LIGHT which x y name do var minutesvar (ext.s): the minutes' formula worked out first.
                    bool self = cmd.Get("Which") != "A thing on the map (below)";
                    int lx = 0, ly = 0, ln = 0xFFFF;
                    if (!self)
                    {
                        var lp = cmd.Get("Thing").Split(':');
                        if (cmd.Get("Thing").StartsWith("Var:")) { ln = 0xFFFE; lx = _variable(cmd.Get("Thing")[4..]); }
                        else if (lp.Length == 3 && lp[0] is "Object" or "Item" && Extra?.Item?.Invoke(lp[0] == "Object" ? "Object" : lp[1], lp[2]) is { } li) { ln = li.Name; lx = Number(cmd.Get("X")); ly = Number(cmd.Get("Y")); }
                        else { Unsupported.Add("Light Source (a thing not on the IIgs)"); break; }
                    }
                    int doing = cmd.Get("Do") switch
                    {
                        "Put it out" => 1, "Light or put out (switch)" => 2, "Burn it out (spent: can't be lit)" => 3, "Make it fresh (can be lit, full time)" => 4,
                        "Set its minutes left" => 5, "Read its minutes left" => 6, "Read: lit (1) or not (0)" => 7, "Read: burnt out (1) or not (0)" => 8, _ => 0
                    };
                    int minVar = Hidden("minutes");
                    B(Calc); W(minVar); Formula(doing == 5 ? cmd.Get("Minutes") : "0", "Light Source");
                    string lv = cmd.Get("Variable").Trim();
                    B(Ext); B((int)ExtOp.Light); B(self ? 0 : 1); W(lx); W(ly); W(ln); B(doing); W(lv.Length > 0 ? _variable(lv) : 0xFFFF); W(minVar);
                    break;
                }
                case EventCommandType.StopSound:
                {
                    // EXT 37 STOPSND sound (ext.s): All - $FFFF.
                    string key = cmd.Get("SoundResourceId");
                    int sn = key == "All" || key.Length == 0 ? 0xFFFF : int.TryParse(key, out int sid) && _sound is not null ? _sound(sid) : -1;
                    if (sn < 0) { Unsupported.Add("Stop Sound (a sound not in the game)"); break; }
                    B(Ext); B((int)ExtOp.StopSound); W(sn);
                    break;
                }
                case EventCommandType.ResetStat:
                {
                    // EXT 36 RESETSTAT who word (ext.s): All - $FF; a custom stat: item 24 covers the built-in ones the IIgs keeps.
                    string sid = cmd.Get("StatId");
                    int word = sid == "All" ? 0xFF : Extra?.Stat?.Invoke(sid) ?? -1;
                    if (word < 0 || !WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Reset Stat (a custom stat, or someone not on a map)"); break; }
                    B(Ext); B((int)ExtOp.ResetStat); W(m); W(x); B(word);
                    break;
                }
                case EventCommandType.LearnSpell:
                case EventCommandType.CastSpell:
                {
                    // EXT 34 LEARN spell / 35 CAST spell who (spellsys.s): the spell's name as typed (quotes off); a name from a formula: item 45.
                    string spellName = cmd.Get("Value").Trim().Trim('"');
                    int si = spellName.Length > 0 && !spellName.Contains('{') ? Extra?.SpellIndex?.Invoke(spellName) ?? -1 : -1;
                    if (si < 0) { Unsupported.Add($"{(cmd.Type == EventCommandType.LearnSpell ? "Learn" : "Cast")} Spell (a spell not in a spellbook, or a name from a formula)"); break; }
                    if (cmd.Type == EventCommandType.LearnSpell) { B(Ext); B((int)ExtOp.Learn); W(si); break; }
                    if (!WhoCode(cmd.Get("Who"), out int cm, out _) || cm != 0xFFFF) { Unsupported.Add("Cast Spell (a caster but the Avatar)"); break; }
                    int tm = 0xFFFF, tx = 0xFFFF;
                    if (cmd.Get("Other").Length > 0 && !WhoCode(cmd.Get("Other"), out tm, out tx)) { Unsupported.Add("Cast Spell (a target not on a map)"); break; }
                    B(Ext); B((int)ExtOp.Cast); W(si); W(tm); W(tx);
                    break;
                }
                case EventCommandType.DeleteObjectSelf:
                    B(Ext); B((int)ExtOp.DeleteSelf);
                    break;
                case EventCommandType.ChangeName:
                {
                    // EXT 16 NAME who name (ext.s): a name as typed (a {formula} in it: item 45, texts).
                    string newName = cmd.Get("Value").Trim();
                    if (newName.Contains('{') || newName.Length == 0 || !WhoCode(cmd.Get("Who"), out int m, out int x) || Extra?.NameOf is null)
                    { Unsupported.Add("Change Name (a name from a formula, or someone not on a map)"); break; }
                    B(Ext); B((int)ExtOp.Name); W(m); W(x); W(Extra.NameOf(newName.Trim('"')));
                    break;
                }
                case EventCommandType.MoveCamera:
                {
                    // EXT 13 CAMERA back x y ticks (ext.s).
                    bool back = cmd.Get("Value") == "Back to the player";
                    B(Ext); B((int)ExtOp.Camera); B(back ? 1 : 0); W(back ? 0 : Number(cmd.Get("X"))); W(back ? 0 : Number(cmd.Get("Y")));
                    W(Math.Clamp((int)(Seconds(cmd.Get("Seconds")) * 60), 0, 65535));
                    break;
                }
                case EventCommandType.CameraFollow:
                {
                    // EXT 14 CAMFOLLOW who (ext.s): the Avatar - back to normal.
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Camera Follows (someone not on a map)"); break; }
                    B(Ext); B((int)ExtOp.CameraFollow); W(m); W(x);
                    break;
                }
                case EventCommandType.ChangeDayNight:
                    // EXT 12 DAYNIGHT (ext.s): Ultima 6's darkness (Engine/U6Lighting.cs) - Morning day, Afternoon dusk's middle, Evening night.
                    B(Ext); B((int)ExtOp.DayNight); B(cmd.Get("Period") switch { "Evening" => U6Lighting.NightLevel, "Afternoon" => 7, _ => 0 });
                    break;
                case EventCommandType.FadeScreen:
                case EventCommandType.FlashScreen:
                {
                    // EXT 10 FADE / 11 FLASH (ext.s, fx.s): the view dissolved into the colour (Ultima 6's pixelated fade).
                    bool flash = cmd.Type == EventCommandType.FlashScreen;
                    string hex = cmd.Get("Color").Trim().TrimStart('#');
                    int rgb = hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int hv) ? hv : flash ? 0xFFFFFF : 0;
                    int colour = Extra?.Colour?.Invoke(rgb >> 16 & 255, rgb >> 8 & 255, rgb & 255) ?? 0;
                    int ticks = Math.Clamp((int)(Seconds(cmd.Get("Seconds")) * 60), 0, 65535);
                    if (flash) { B(Ext); B((int)ExtOp.Flash); B(colour); W(Math.Max(3, ticks)); }
                    else { B(Ext); B((int)ExtOp.Fade); B(cmd.Get("Value") != "In" ? 1 : 0); B(colour); W(ticks); }
                    break;
                }
                case EventCommandType.ShakeScreen:
                {
                    // EXT 9 SHAKE (ext.s, fx.s): Windows shakes Intensity x 2 pixels each way; the event goes on meanwhile.
                    int px = Math.Clamp(Number(cmd.Get("Intensity")) * 2, 1, 16);
                    B(Ext); B((int)ExtOp.Shake); B(px); W(Math.Clamp((int)(Seconds(cmd.Get("DurationSeconds")) * 60), 1, 65535)); B(0); W(0xFFFF);
                    break;
                }
                case EventCommandType.Earthquake:
                {
                    // EXT 9 SHAKE (ext.s, fx.s): Exult's earthquake, 4 pixels each way (Windows: PlayRealShakeEffect(4)), its sound once.
                    int quakeSound = int.TryParse(cmd.Get("SoundId"), out int qs) && _sound is not null && _sound(qs) is int qn and >= 0 ? qn : 0xFFFF;
                    B(Ext); B((int)ExtOp.Shake); B(8); W(Math.Clamp((int)(Seconds(cmd.Get("Seconds")) * 60), 6, 36000)); B(cmd.Get("Wait") != "False" ? 1 : 0); W(quakeSound);
                    break;
                }
                case EventCommandType.SleepOnBed:
                    B(Ext); B((int)ExtOp.SleepOnBed); // EXT 8 BED (bed.s): the bed nearest the Avatar
                    break;
                case EventCommandType.ChangeBehavior:
                {
                    // EXT 7 BEHAV who activity (ext.s): Resume ($FF) - back to its schedule.
                    string behavior = cmd.Get("Behavior");
                    int act = behavior == "Resume" ? 0xFF : Enum.TryParse<ScheduleType>(behavior, out var st) ? AppleIIgsGamePack.ActivityCode(st) : -1;
                    if (act < 0 || !WhoCode(cmd.Get("CharacterId"), out int m, out int x)) { Unsupported.Add("Change Behavior (someone not on a map)"); break; }
                    B(Ext); B((int)ExtOp.Behavior); W(m); W(x); B(act);
                    break;
                }
                case EventCommandType.FollowCharacter:
                {
                    // EXT 6 FOLLOW who leader stop (ext.s).
                    bool stop = cmd.Get("Value") == "Stop following";
                    int lm = 0xFFFF, lx = 0xFFFF;
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x) || !stop && !WhoCode(cmd.Get("Other"), out lm, out lx)) { Unsupported.Add("Follow Character (someone not on a map)"); break; }
                    B(Ext); B((int)ExtOp.Follow); W(m); W(x); W(lm); W(lx); B(stop ? 1 : 0);
                    break;
                }
                case EventCommandType.TakeFromContainer:
                {
                    // EXT 5 TAKEC who x y name count (ext.s): the container at x, y; no item ("(everything)"): $FFFF.
                    bool everything = !int.TryParse(cmd.Get("ItemId"), out int takeId) || takeId < 0 || cmd.Get("Category").Length == 0;
                    int? takeName = everything ? 0xFFFF : Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId"))?.Name;
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x) || takeName is null) { Unsupported.Add("Take From Container (someone not on a map, or an item not on the IIgs)"); break; }
                    B(Ext); B((int)ExtOp.TakeFromContainer); W(m); W(x); W(Number(cmd.Get("X"))); W(Number(cmd.Get("Y"))); W(takeName.Value);
                    W(Math.Clamp(int.TryParse(cmd.Get("Quantity"), out int tq) ? tq : 1, 1, 255));
                    break;
                }
                case EventCommandType.PicklockDoor:
                {
                    // EXT 4 PICK who x y (ext.s): the door at x, y.
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Picklock Door (someone not on a map)"); break; }
                    B(Ext); B((int)ExtOp.Picklock); W(m); W(x); W(Number(cmd.Get("X"))); W(Number(cmd.Get("Y")));
                    break;
                }
                case EventCommandType.PlayPose:
                {
                    // EXT 1 POSE who n ticks frames (ext.s): Seconds 60 ticks each, game minutes 300 (a game minute is 5 seconds), "Done" once through.
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x) || Extra?.PoseFrames?.Invoke(cmd.Get("Who"), cmd.Get("Pose"), cmd.Get("Facing")) is not { Count: > 0 } poseFrames)
                    { Unsupported.Add("Play Pose (someone not on a map, or a pose they have no pictures for)"); break; }
                    double amount = Seconds(cmd.Get("Duration")) is > 0 and var am ? am : 1;
                    int ticks = cmd.Get("Unit") switch { "Seconds" => (int)(amount * 60), "Minutes" => (int)(amount * 300), "Hours" => (int)(amount * 18000), _ => 0 };
                    B(Ext); B((int)ExtOp.Pose); W(m); W(x); B(Math.Min(poseFrames.Count, 8)); W(Math.Clamp(ticks, 0, 65535));
                    foreach (var (fs, ft) in poseFrames.Take(8)) { W(fs); B(Math.Clamp(ft, 1, 255)); }
                    break;
                }
                case EventCommandType.RemoveItem:
                {
                    if (Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId")) is not { } it) { Unsupported.Add("Remove Item (an item not on the IIgs)"); break; }
                    B(Take); W(it.Name); W(Math.Clamp(Number(cmd.Get("Quantity")), 1, 255));
                    break;
                }
                case EventCommandType.ChangeHp:
                {
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Change HP (someone not on a map)"); break; }
                    B(Hp); W(m); W(x); B(cmd.Get("Operation") switch { "Subtract" => 1, "Set" => 2, _ => 0 }); W(Number(cmd.Get("Amount"))); B(cmd.Get("CanKill") == "True" ? 1 : 0);
                    break;
                }
                case EventCommandType.U6SetCombatMode:
                {
                    // A member's combat mode: its word 20 (solo.s); the Avatar is always Command.
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Set Combat Mode (someone not on a map)"); break; }
                    B(StatOp); W(m); W(x); B(20); B(2); W(AppleIIgsGamePack.CombatModeCode(cmd.Get("Mode")));
                    break;
                }
                case EventCommandType.ChangeStat:
                {
                    int word = Extra?.Stat?.Invoke(cmd.Get("StatId")) ?? -1;
                    if (word < 0 || !WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Change Stat (a custom stat, or someone not on a map)"); break; }
                    B(StatOp); W(m); W(x); B(word); B(cmd.Get("Operation") switch { "Subtract" => 1, "Set" => 2, _ => 0 }); W(Number(cmd.Get("Value")));
                    break;
                }
                case EventCommandType.Wait:
                {
                    double secs = Seconds(cmd.Get("Duration") is { Length: > 0 } d ? d : cmd.Get("Seconds"));
                    if (cmd.Get("Unit") is "Minutes") secs *= 60;
                    B(WaitOp); W((int)Math.Clamp(secs * 60, 0, 60000));
                    break;
                }
                case EventCommandType.Teleport:
                {
                    if (cmd.Get("Who") is not ("" or "Activator" or "Player"))
                    {
                        // Someone else (list item 38): put at x, y on their own map (op PLACE) - another map's list can't take them on the IIgs.
                        if (!WhoCode(cmd.Get("Who"), out int pm, out int px) || pm == 0xFFFF) { Unsupported.Add("Teleport (someone not on a map)"); break; }
                        if (int.TryParse(cmd.Get("World"), out int ow) && ow > 0 && (_world?.Invoke(ow) ?? -1) != pm) { Unsupported.Add("Teleport (someone to another map: on the IIgs a character stays on its own map)"); break; }
                        if (!int.TryParse(cmd.Get("X"), out int ptx) || !int.TryParse(cmd.Get("Y"), out int pty)) break;
                        B(Place); W(pm); W(px); B(1); W(ptx); W(pty);
                        break;
                    }
                    int map = 0xFFFF;
                    if (int.TryParse(cmd.Get("World"), out int worldId) && worldId > 0)
                    {
                        int n = _world?.Invoke(worldId) ?? -1;
                        if (n < 0) { Unsupported.Add("Teleport (to a map not in the game)"); break; }
                        map = n;
                    }
                    if (!int.TryParse(cmd.Get("X"), out int tx) || !int.TryParse(cmd.Get("Y"), out int ty)) break;
                    B(Map); W(map); W(tx); W(ty);
                    break;
                }
                case EventCommandType.PlaceCharacter:
                {
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x) || m == 0xFFFF) { Unsupported.Add("Place / Remove Character (someone not on a map)"); break; }
                    bool place = cmd.Get("Value") != "Remove it from the map";
                    int.TryParse(cmd.Get("X"), out int px); int.TryParse(cmd.Get("Y"), out int py);
                    B(Place); W(m); W(x); B(place ? 1 : 0); W(px); W(py);
                    break;
                }
                case EventCommandType.KillCharacter:
                {
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Kill Character (someone not on a map)"); break; }
                    B(Kill); W(m); W(x);
                    break;
                }
                case EventCommandType.ChangeSchedule:
                {
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x) || m == 0xFFFF) { Unsupported.Add("Change Schedule (someone not on a map)"); break; }
                    int act = cmd.Get("Activity") switch
                    {
                        "Stand" or "Wait" or "Sit" => 0, "Wander" => 1, "Sleep" => 2, "Pace east-west" => 3, "Pace north-south" => 4, "Back to its schedule" => 0xFF, _ => 5
                    };
                    B(Sched); W(m); W(x); B(act);
                    break;
                }
                case EventCommandType.SetTime:
                {
                    int mode = cmd.Get("Value") switch { "Advance by minutes" => 1, "Advance by hours" => 2, _ => 0 };
                    B(Time); B(mode); W(Math.Clamp(Number(cmd.Get("Hour")), 0, 9999)); W(Math.Clamp(Number(cmd.Get("Minute")), 0, 59));
                    break;
                }
                case EventCommandType.SetDoorState:
                {
                    if (!int.TryParse(cmd.Get("X"), out int dx) || !int.TryParse(cmd.Get("Y"), out int dy)) { Unsupported.Add("Set Door State (no place)"); break; }
                    B(Door); W(dx); W(dy); B(cmd.Get("Value") switch { "Locked" => 0, "Unlocked" => 1, "Closed" => 2, _ => 3 });
                    break;
                }
                case EventCommandType.ShowPicture:
                {
                    int sprite = Extra?.Picture?.Invoke(cmd.Get("SpriteGroup"), cmd.Get("SpriteName")) ?? -1;
                    if (sprite < 0) { Unsupported.Add("Show Picture (no sprite)"); break; }
                    B(Pic); W(sprite); W(Math.Clamp(Number(cmd.Get("X")), 0, 319) & ~1); W(Math.Clamp(Number(cmd.Get("Y")), 0, 199));
                    break;
                }
                case EventCommandType.ErasePicture:
                    B(Erase);
                    break;
                case EventCommandType.Shop:
                {
                    ShopDefinition? shop;
                    try { shop = System.Text.Json.JsonSerializer.Deserialize<ShopDefinition>(cmd.Get("Shop")); } catch (System.Text.Json.JsonException) { shop = null; }
                    if (shop is null || Extra?.Item is null) { Unsupported.Add("Shop (no shop)"); break; }
                    CompileShop(shop);
                    break;
                }
                case EventCommandType.ChangeState or EventCommandType.ModifyAilments:
                {
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Change State (someone not on a map)"); break; }
                    string state = cmd.Type == EventCommandType.ChangeState ? cmd.Get("State") : cmd.Get("Ailment");
                    int bit = state switch { "Poisoned" => 1, "Asleep" => 2, "Paralyzed" => 4, "Charmed" => 8, "Cursed" => 16, "Invisible" => 32, "Protected" => 64, _ => 0 };
                    if (bit == 0) break;
                    bool on = cmd.Type == EventCommandType.ChangeState ? cmd.Get("Value") != "Off" : cmd.Get("Action") != "Cure";
                    B(Ail); W(m); W(x); B(bit); B(on ? 1 : 0);
                    break;
                }
                case EventCommandType.ChangeFoodLevel:
                {
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Change Food Level (someone not on a map)"); break; }
                    B(StatOp); W(m); W(x); B(17); B(cmd.Get("Operation") switch { "Subtract" => 1, "Set" => 2, _ => 0 }); W(Math.Clamp(Number(cmd.Get("Value")), 0, 31));
                    break;
                }
                case EventCommandType.GameOver: B(GameOp); B(0); break;
                case EventCommandType.QuitGame: B(GameOp); B(1); break;
                case EventCommandType.SaveGame: B(GameOp); B(2); break;
                case EventCommandType.LoadGame: B(GameOp); B(3); break;
                case EventCommandType.ReturnToTitle: B(GameOp); B(4); break;
                case EventCommandType.OpenSaveLoadScreen: B(GameOp); B(5); break; // Ultima 6: the title menu in play (Journey Onward loads, Escape goes back)
                case EventCommandType.AllowSaving: B(GameOp); B(cmd.Get("Value") == "Forbidden" ? 6 : 7); break;
                case EventCommandType.Calculate:
                    B(Calc); W(_variable(cmd.Get("Variable"))); Formula(cmd.Get("Formula"), "Calculate");
                    break;
                case EventCommandType.AdvancedIf:
                    B(IfX); GameCondition(GameConditions.FromCommand(cmd), "Advance IF");
                    frames.Push(new Frame { Indent = cmd.IndentLevel, ElseAt = Hole() });
                    break;
                case EventCommandType.While:
                {
                    var loop = new Loop { Kind = cmd.Type, Top = Here };
                    loop.Continue = loop.Top;
                    B(IfX); Condition(cmd, "While"); loop.Ends.Add(Hole());
                    _loops.Push(loop);
                    break;
                }
                case EventCommandType.RepeatTimes:
                {
                    int count = Hidden("count"), i = Hidden("i");
                    B(Calc); W(count); Formula(cmd.Get("Count"), "Repeat");
                    B(Calc); W(i); B(1); W(0); B(0);
                    var loop = new Loop { Kind = cmd.Type, Top = Here };
                    loop.Continue = loop.Top;
                    B(IfX); B(2); W(i); B(2); W(count); B(11); B(0); loop.Ends.Add(Hole());
                    B(Calc); W(i); B(2); W(i); B(1); W(1); B(3); B(0);
                    if (cmd.Get("Variable").Trim().Length > 0) { B(Calc); W(_variable(cmd.Get("Variable"))); B(2); W(i); B(0); }
                    _loops.Push(loop);
                    break;
                }
                case EventCommandType.ForEach:
                {
                    // For each element (or cell, row by row): a hidden counter; the value, its index (x, y) into the variables.
                    bool grid = cmd.Get("Of") == "Cell of a grid";
                    string name = cmd.Get("Array");
                    int li = grid ? Extra?.Names?.Grid?.Invoke(name) ?? -1 : Extra?.Names?.Array?.Invoke(name) ?? -1;
                    if (li < 0) { Unsupported.Add("For Each (an array or grid the game doesn't make)"); _loops.Push(new Loop { Kind = cmd.Type, Top = Here, Step = -2 }); break; }
                    int k = Hidden("each");
                    B(Calc); W(k); B(1); W(0); B(0);
                    var loop = new Loop { Kind = cmd.Type, Top = Here };
                    B(IfX); B(2); W(k);
                    if (grid) { B(31); B(li); B(34); B(3); B(1); B(31); B(li); B(34); B(2); B(1); B(5); } else { B(30); B(li); B(34); B(1); B(1); }
                    B(11); B(0); loop.Ends.Add(Hole());
                    void Into(string field, Action value) { string v = cmd.Get(field).Trim(); if (v.Length == 0) return; B(Calc); W(_variable(v)); value(); B(0); }
                    if (grid)
                    {
                        Into("Index", () => { B(2); W(k); B(31); B(li); B(34); B(3); B(1); B(7); });
                        Into("Index2", () => { B(2); W(k); B(31); B(li); B(34); B(3); B(1); B(6); });
                        Into("Variable", () => { B(31); B(li); B(2); W(k); B(31); B(li); B(34); B(3); B(1); B(7); B(2); W(k); B(31); B(li); B(34); B(3); B(1); B(6); B(33); });
                    }
                    else
                    {
                        Into("Index", () => { B(2); W(k); });
                        Into("Variable", () => { B(30); B(li); B(2); W(k); B(32); });
                    }
                    loop.Step = k;
                    _loops.Push(loop);
                    break;
                }
                case EventCommandType.EndForEach when _loops.Count > 0:
                {
                    var loop = _loops.Pop();
                    if (loop.Step == -2) break;
                    foreach (int at in loop.Nexts) Fill(at, Here);
                    B(Calc); W(loop.Step); B(2); W(loop.Step); B(1); W(1); B(3); B(0);
                    B(Jump); W(loop.Top);
                    foreach (int at in loop.Ends) Fill(at, Here);
                    break;
                }
                case EventCommandType.TextOperation:
                {
                    string a = $"({Or0(cmd.Get("A"))})", b = $"({Or0(cmd.Get("B"))})", c = cmd.Get("C").Trim();
                    string formula = cmd.Get("Operation") switch
                    {
                        "Join" => $"text{a} + text{b}", "Length" => $"length{a}", "Part" => c.Length > 0 ? $"part({a}, {b}, ({c}))" : $"part({a}, {b})",
                        "Upper case" => $"upper{a}", "Lower case" => $"lower{a}", "Trim" => $"trim{a}", "Replace" => $"replace({a}, {b}, ({Or0(c)}))",
                        "Contains" => $"contains({a}, {b})", "Find" => $"indexof({a}, {b})", "Number to text" => $"text{a}", "Text to number" => $"number{a}", _ => a
                    };
                    B(Calc); W(_variable(cmd.Get("Variable"))); Formula(formula, "Text Operation");
                    break;
                }
                case EventCommandType.GetGameData:
                {
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Get Game Data (someone not on a map)"); break; }
                    string what = cmd.Get("What");
                    int fn = what switch
                    {
                        "X (cell)" => 3, "Y (cell)" => 4, "Floor" => 17, "HP" => 1, "Max HP" => 2, "Stat" => 15, "Gold (all money)" => 5, "Item count" => 16,
                        "Food level" => 6, "Name" => 13, "Is dead" => 11, "Is in party" => 12, "Party size" => 10, "Hour" => 7, "Minute" => 8, "Day" => 9,
                        "Map name" => 19, "Leader's name" => 13, "Facing" => 14, _ => 0
                    };
                    if (fn == 0) { Unsupported.Add($"Get Game Data ({what})"); break; }
                    if (what == "Leader's name") m = x = 0xFFFF;
                    B(Calc); W(_variable(cmd.Get("Variable"))); B(35); B(fn); W(m); W(x);
                    if (fn == 15) B(Math.Max(0, Extra?.StatByName?.Invoke(cmd.Get("StatName")) ?? 0));
                    if (fn == 16)
                    {
                        string cat = cmd.Get("Category"), id = cmd.Get("ItemId");
                        W(Extra?.Item?.Invoke(cat, id) is { } it ? it.Name : 0xFFFF);
                    }
                    B(0);
                    break;
                }
                case EventCommandType.SwapVariables:
                {
                    int t = Hidden("swap"), va = _variable(cmd.Get("A")), vb = _variable(cmd.Get("B"));
                    B(Calc); W(t); B(2); W(va); B(0);
                    B(Calc); W(va); B(2); W(vb); B(0);
                    B(Calc); W(vb); B(2); W(t); B(0);
                    break;
                }
                case EventCommandType.InputText or EventCommandType.InputNumber:
                {
                    bool number = cmd.Type == EventCommandType.InputNumber;
                    B(Input); B(number ? 1 : 0); W(_variable(cmd.Get("Variable")));
                    B(Math.Clamp((int)Seconds(cmd.Get(number ? "Digits" : "Letters")) is int n && n > 0 ? n : number ? 4 : 16, 1, number ? 5 : 40));
                    var words = Encoding.ASCII.GetBytes(new string(cmd.Get("Prompt").Select(ch => ch is >= ' ' and <= '~' ? ch : '?').Take(80).ToArray()));
                    _code.AddRange(words); B(0);
                    if (!number && cmd.Get("Start").Trim() is { Length: > 0 } start && start != "\"\"") { B(Calc); W(_variable(cmd.Get("Variable"))); Formula(start, "Input Text"); }
                    break;
                }
                case EventCommandType.CreateArray or EventCommandType.SetElement or EventCommandType.GetElement or EventCommandType.AddToArray or EventCommandType.InsertAt
                    or EventCommandType.RemoveAt or EventCommandType.ArrayLength or EventCommandType.FindInArray or EventCommandType.ReorderArray or EventCommandType.FillArray
                    or EventCommandType.ClearArray or EventCommandType.CopyArray or EventCommandType.ArrayStatistic:
                {
                    int li = Extra?.Names?.Array?.Invoke(cmd.Get("Array")) ?? -1;
                    if (li < 0) { Unsupported.Add($"{cmd.Type} (no array)"); break; }
                    void F(string field) => Formula(cmd.Get(field), cmd.Type.ToString());
                    int V(string field) => _variable(cmd.Get(field));
                    switch (cmd.Type)
                    {
                        case EventCommandType.CreateArray: B(ListOp); B(0); B(li); F("Size"); F("Fill"); break;
                        case EventCommandType.SetElement: B(ListOp); B(1); B(li); F("Index"); F("Value"); break;
                        case EventCommandType.GetElement: B(ListOp); B(2); B(li); F("Index"); W(V("Variable")); break;
                        case EventCommandType.AddToArray: B(ListOp); B(3); B(li); F("Value"); break;
                        case EventCommandType.InsertAt: B(ListOp); B(4); B(li); F("Index"); F("Value"); break;
                        case EventCommandType.RemoveAt: B(ListOp); B(cmd.Get("By") == "First equal to" ? 6 : 5); B(li); F("Index"); break;
                        case EventCommandType.ArrayLength: B(ListOp); B(7); B(li); W(V("Variable")); break;
                        case EventCommandType.FindInArray: B(ListOp); B(8); B(li); F("Value"); W(V("Variable")); break;
                        case EventCommandType.ReorderArray: B(ListOp); B(9); B(li); B(cmd.Get("Order") switch { "Big to small" => 1, "Shuffle" => 2, "Reverse" => 3, _ => 0 }); break;
                        case EventCommandType.FillArray: B(ListOp); B(10); B(li); F("Value"); break;
                        case EventCommandType.ClearArray: B(ListOp); B(11); B(li); break;
                        case EventCommandType.CopyArray:
                        {
                            int from = Extra?.Names?.Array?.Invoke(cmd.Get("From")) ?? -1;
                            if (from < 0) { Unsupported.Add("Copy Array (no array)"); break; }
                            B(ListOp); B(12); B(li); B(from); break;
                        }
                        case EventCommandType.ArrayStatistic: B(ListOp); B(13); B(li); B(cmd.Get("What") switch { "Smallest" => 1, "Biggest" => 2, "Average" => 3, _ => 0 }); W(V("Variable")); break;
                    }
                    break;
                }
                case EventCommandType.CreateGrid or EventCommandType.SetCell or EventCommandType.GetCell or EventCommandType.FillGrid or EventCommandType.GridSize
                    or EventCommandType.ResizeGrid or EventCommandType.FindInGrid or EventCommandType.GridLineToArray or EventCommandType.ArrayToGridLine
                    or EventCommandType.GridFromMap or EventCommandType.GridToMap:
                {
                    int gi = Extra?.Names?.Grid?.Invoke(cmd.Get("Grid")) ?? -1;
                    if (gi < 0) { Unsupported.Add($"{cmd.Type} (no grid)"); break; }
                    void F(string field) => Formula(cmd.Get(field), cmd.Type.ToString());
                    int V(string field) => cmd.Get(field).Trim().Length == 0 ? 0xFFFF : _variable(cmd.Get(field));
                    int arr = Extra?.Names?.Array?.Invoke(cmd.Get("Array")) ?? -1;
                    switch (cmd.Type)
                    {
                        case EventCommandType.CreateGrid: B(GridOp); B(0); B(gi); F("Columns"); F("Rows"); F("Fill"); break;
                        case EventCommandType.SetCell: B(GridOp); B(1); B(gi); F("X"); F("Y"); F("Value"); break;
                        case EventCommandType.GetCell: B(GridOp); B(2); B(gi); F("X"); F("Y"); W(V("Variable")); break;
                        case EventCommandType.FillGrid:
                            B(GridOp); B(3); B(gi); F("Value"); B(cmd.Get("Area") == "True" ? 1 : 0);
                            if (cmd.Get("Area") == "True") { F("X"); F("Y"); F("Width"); F("Height"); }
                            break;
                        case EventCommandType.GridSize: B(GridOp); B(4); B(gi); W(V("Variable")); W(V("Variable2")); break;
                        case EventCommandType.ResizeGrid: B(GridOp); B(5); B(gi); F("Columns"); F("Rows"); F("Fill"); break;
                        case EventCommandType.FindInGrid: B(GridOp); B(6); B(gi); F("Value"); W(V("Variable")); W(V("Variable2")); break;
                        case EventCommandType.GridLineToArray or EventCommandType.ArrayToGridLine:
                            if (arr < 0) { Unsupported.Add($"{cmd.Type} (no array)"); break; }
                            B(GridOp); B(cmd.Type == EventCommandType.GridLineToArray ? 7 : 8); B(gi); B(cmd.Get("Line") == "Column" ? 1 : 0); F("Index"); B(arr);
                            break;
                        case EventCommandType.GridFromMap: B(GridOp); B(9); B(gi); F("X"); F("Y"); F("Width"); F("Height"); break;
                        case EventCommandType.GridToMap: B(GridOp); B(10); B(gi); F("X"); F("Y"); break;
                    }
                    break;
                }
                case EventCommandType.EndWhile or EventCommandType.EndRepeat when _loops.Count > 0:
                {
                    var loop = _loops.Pop();
                    B(Jump); W(loop.Top);
                    foreach (int at in loop.Ends) Fill(at, Here);
                    break;
                }
                case EventCommandType.SelectCase:
                {
                    int sel = Hidden("select");
                    B(Calc); W(sel); Formula(cmd.Get("Formula"), "Select Case");
                    _loops.Push(new Loop { Kind = cmd.Type, Select = sel });
                    break;
                }
                case EventCommandType.Case or EventCommandType.CaseElse when _loops.Count > 0 && _loops.Peek().Kind == EventCommandType.SelectCase:
                {
                    var loop = _loops.Peek();
                    if (loop.NextCase >= 0 || loop.Ends.Count > 0 || loop.Top > 0) { B(Jump); loop.Ends.Add(Hole()); } // the case before: to the end
                    if (loop.NextCase >= 0) Fill(loop.NextCase, Here);
                    loop.NextCase = -1;
                    loop.Top = 1;
                    if (cmd.Type == EventCommandType.Case)
                    {
                        var values = ScriptRuntime.SplitList(cmd.Get("Formula"));
                        B(IfX);
                        for (int k = 0; k < values.Count; k++)
                        {
                            B(2); W(loop.Select);
                            var code = ScriptExpression.CompileIIgs(values[k], _variable, out _, Extra?.Names);
                            if (code is null) { Unsupported.Add("Case (a formula with texts)"); B(1); W(0); } else _code.AddRange(code.Take(code.Count - 1));
                            B(9);
                            if (k > 0) B(16);
                        }
                        if (values.Count == 0) { B(1); W(0); }
                        B(0);
                        loop.NextCase = Hole();
                    }
                    break;
                }
                case EventCommandType.EndSelect when _loops.Count > 0 && _loops.Peek().Kind == EventCommandType.SelectCase:
                {
                    var loop = _loops.Pop();
                    if (loop.NextCase >= 0) Fill(loop.NextCase, Here);
                    foreach (int at in loop.Ends) Fill(at, Here);
                    break;
                }
                case EventCommandType.BreakLoop or EventCommandType.NextLoop:
                {
                    var loop = _loops.FirstOrDefault(l => l.Kind is EventCommandType.While or EventCommandType.RepeatTimes or EventCommandType.ForEach);
                    if (loop is null) break;
                    B(Jump);
                    if (cmd.Type == EventCommandType.BreakLoop) loop.Ends.Add(Hole());
                    else if (loop.Kind == EventCommandType.ForEach) loop.Nexts.Add(Hole()); // to its step, at its end
                    else W(loop.Continue);
                    break;
                }
                case EventCommandType.WaitUntil:
                {
                    int top = Here;
                    B(IfX); Condition(cmd, "Wait Until"); int wait = Hole();
                    B(Jump); int done = Hole();
                    Fill(wait, Here);
                    B(WaitOp); W(10); B(Jump); W(top);
                    Fill(done, Here);
                    break;
                }
                case EventCommandType.CallCommonEvent:
                    if (depth < 4 && int.TryParse(cmd.Get("CommonEventId"), out int ceId) && Extra?.Common?.Invoke(ceId) is { } ceCommands) Run(ceCommands, depth + 1);
                    else Unsupported.Add("Call Common Event (a common event not there)");
                    break;
                case EventCommandType.PlayMusic or EventCommandType.FadeMusicIn:
                {
                    bool fade = cmd.Type == EventCommandType.FadeMusicIn;
                    int n = int.TryParse(cmd.Get(fade ? "SoundId" : "MusicResourceId"), out int mid) && _sound is not null ? _sound(mid) : -1;
                    if (n < 0) { Unsupported.Add(cmd.Type + " (music that isn't in the game)"); break; }
                    double volume = !fade && double.TryParse(cmd.Get("Volume"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double mv) ? mv : 1.0;
                    B(Music); W(n); B(cmd.Get("Loop") == "True" ? 1 : 0); B((int)Math.Clamp(volume * 255, 0, 255)); W(fade ? FadeTicks(cmd, 0.05) : 0);
                    break;
                }
                case EventCommandType.StopMusic:
                    B(Music); W(0xFFFF); B(0); B(0); W(FadeTicks(cmd, 0));
                    break;
                case EventCommandType.SetVolume:
                    B(Volume); Formula(cmd.Get("Music"), "Set Volume"); Formula(cmd.Get("Sounds"), "Set Volume");
                    break;
                case EventCommandType.ResumeMapMusic:
                    B(Resume);
                    break;
                case EventCommandType.ChangeWeather:
                {
                    if (!Enum.TryParse<WeatherKind>(cmd.Get("Kind"), out var wk)) { Unsupported.Add("Change Weather (no kind)"); break; }
                    if (!int.TryParse(cmd.Get("Intensity"), out int wi)) { wi = 50; if (cmd.Get("Intensity").Trim().Length > 0) Unsupported.Add("Change Weather (an intensity that's a formula: a number only)"); }
                    B(Weather); B((int)wk); B(Math.Clamp(wi, 1, 100)); B(cmd.Get("Clouds") switch { "True" => 1, "False" => 0, _ => 2 });
                    break;
                }
                case EventCommandType.DriveVehicle or EventCommandType.StopDriving or EventCommandType.SetVehicleSpeed or EventCommandType.TravelVehicle:
                {
                    int vid = int.TryParse(cmd.Get("Vehicle"), out int vv) ? vv : 0;
                    if (cmd.Type == EventCommandType.DriveVehicle) { B(Vehicle); B(0); W(vid); }
                    else if (cmd.Type == EventCommandType.StopDriving) { B(Vehicle); B(1); W(0); }
                    else if (cmd.Type == EventCommandType.SetVehicleSpeed) { B(Vehicle); B(2); W(vid); Formula(cmd.Get("Speed"), "Set Vehicle Speed"); }
                    else
                    {
                        B(Vehicle); B(3); W(vid); W(int.TryParse(cmd.Get("X"), out int tx) ? tx : 0); W(int.TryParse(cmd.Get("Y"), out int ty) ? ty : 0);
                        if (cmd.Get("Wait") != "False")
                        {
                            int top = Here;          // the event waits till it gets there
                            B(IfX); B(28); W(vid); B(0); int done = Hole();
                            B(WaitOp); W(10); B(Jump); W(top);
                            Fill(done, Here);
                        }
                    }
                    break;
                }
                case EventCommandType.U6SceneText:
                    // EXT 45 SCENE 2 (scene.s): the words caught (EXT 40 LOG 0: {formulas} worked out), then on the scene - or in the text area.
                    if (cmd.Get("Where") == "In the text area") { SayWords(cmd.Get("Text")); break; }
                    SceneWords(cmd.Get("Text"));
                    B(Ext); B((int)ExtOp.Scene); B(2); W(Number(cmd.Get("X"))); W(Number(cmd.Get("Y"))); W(Math.Max(0, Number(cmd.Get("Width"))));
                    W(SceneColour(cmd.Get("Colour"), 0xFFFF)); W(SceneName(cmd.Get("Name"))); B(0);
                    break;
                case EventCommandType.U6SceneChoice:
                {
                    // On the scene at X, Y (a line each; no scene: the text area), then the answer's letter or number (op PICK).
                    var answers = cmd.Get("Choices").Replace("\r", "").Split('\n').Select(a => a.Trim()).Where(a => a.Length > 0).Take(9).ToList();
                    if (answers.Count == 0) break;
                    int cx = Number(cmd.Get("X")), cy = Number(cmd.Get("Y")), line = 0;
                    var lines = cmd.Get("Question").Replace("\r", "").Split('\n').Where(q => q.Trim().Length > 0).ToList();
                    lines.AddRange(answers.Select((a, i) => $"{i + 1}. {a}"));
                    int choiceName = SceneName("__choice");
                    foreach (var l in lines)
                    {
                        SceneWords(l);
                        B(Ext); B((int)ExtOp.Scene); B(2); W(cx); W(cy + 10 * line++); W(0); W(0xFFFF); W(SceneName("__choice" + line)); B(0);
                    }
                    B(Pick); W(_variable(cmd.Get("Variable"))); B(answers.Count);
                    foreach (var a in answers) B(char.ToUpperInvariant(a[0]) is char ch and >= ' ' and <= '~' ? ch : '?');
                    for (int l = 1; l <= line; l++) { B(Ext); B((int)ExtOp.Scene); B(4); W(SceneName("__choice" + l)); }
                    break;
                }
                case EventCommandType.U6ShowHidePiece:
                case EventCommandType.U6ShowHidePanel:
                {
                    // EXT 46 HUD (hudlive.s): a custom panel - 4 PANEL p v; any other piece - 0 SHOW i v (0 shown / open, 1 hidden / closed, 2 toggled).
                    int v = cmd.Get("Value") is "Hidden" or "Closed" ? 1 : cmd.Get("Value") == "Toggled" ? 2 : 0;
                    int p = Extra?.HudPanel?.Invoke(cmd.Get("Piece")) ?? -1;
                    if (p >= 0) { B(Ext); B((int)ExtOp.Hud); B(4); B(p); B(v); break; }
                    int i = cmd.Type == EventCommandType.U6ShowHidePiece ? Extra?.LivePiece?.Invoke(cmd.Get("Piece")) ?? -1 : -1;
                    if (i < 0) { Unsupported.Add($"{cmd.Type} ({cmd.Get("Piece")}: no such piece the Apple IIgs can change)"); break; }
                    B(Ext); B((int)ExtOp.Hud); B(0); B(i); B(v);
                    break;
                }
                case EventCommandType.U6SetPiecePicture:
                {
                    int i = Extra?.LivePiece?.Invoke(cmd.Get("Piece")) ?? -1;
                    if (i < 0) { Unsupported.Add($"Set HUD Piece Picture ({cmd.Get("Piece")}: no such piece the Apple IIgs can change)"); break; }
                    int pic = cmd.Get("Own") == "True" || cmd.Get("SpriteName").Length == 0 ? 0xFFFF : Extra?.PiecePicture?.Invoke(cmd.Get("Piece"), cmd.Get("SpriteGroup"), cmd.Get("SpriteName")) ?? -1;
                    if (pic < 0) { Unsupported.Add("Set HUD Piece Picture (a sprite with no picture)"); break; }
                    B(Ext); B((int)ExtOp.Hud); B(1); B(i); W(pic);
                    break;
                }
                case EventCommandType.U6SetPieceText:
                {
                    int i = Extra?.LivePiece?.Invoke(cmd.Get("Piece")) ?? -1;
                    if (i < 0) { Unsupported.Add($"Set HUD Piece Text ({cmd.Get("Piece")}: no such label)"); break; }
                    SceneWords(cmd.Get("Text")); // (caught in LogBuf, {formulas} worked out)
                    B(Ext); B((int)ExtOp.Hud); B(2); B(i);
                    break;
                }
                case EventCommandType.U6MovePiece:
                {
                    int i = Extra?.LivePiece?.Invoke(cmd.Get("Piece")) ?? -1;
                    if (i < 0) { Unsupported.Add($"Move HUD Piece ({cmd.Get("Piece")}: no such piece the Apple IIgs can change)"); break; }
                    B(Ext); B((int)ExtOp.Hud); B(3); B(i); B(cmd.Get("Back") == "True" ? 1 : 0);
                    Formula(cmd.Get("X"), "Move HUD Piece"); Formula(cmd.Get("Y"), "Move HUD Piece");
                    break;
                }
                case EventCommandType.U6EnablePanelKey:
                case EventCommandType.U6DisablePanelKey:
                {
                    int p = Extra?.HudPanel?.Invoke(cmd.Get("Piece")) ?? -1;
                    if (p < 0) { Unsupported.Add($"{cmd.Type} ({cmd.Get("Piece")}: no such custom panel)"); break; }
                    B(Ext); B((int)ExtOp.Hud); B(5); B(p); B(cmd.Type == EventCommandType.U6EnablePanelKey ? 1 : 0);
                    break;
                }
                case EventCommandType.U6SceneBegin:
                    _sceneView = cmd.Get("Area") == "The Game View";
                    B(Ext); B((int)ExtOp.Scene); B(0); B(_sceneView ? 1 : 0); W(SceneColour(cmd.Get("Colour"), SceneColour("#000000", 0)));
                    break;
                case EventCommandType.U6ScenePicture:
                {
                    int pw = Number(cmd.Get("Width")), ph = Number(cmd.Get("Height")), px = Number(cmd.Get("X")), py = Number(cmd.Get("Y"));
                    if (cmd.Get("Fill") == "True") { (pw, ph) = _sceneView ? (Extra?.ViewSize.W ?? 320, Extra?.ViewSize.H ?? 192) : (320, 200); px = py = 0; }
                    int pic = Extra?.BoxPicture?.Invoke(cmd.Get("SpriteGroup"), cmd.Get("SpriteName"), pw <= 0 ? 0 : (pw + 3) / 4 * 4, ph) ?? -1;
                    if (pic < 0) { Unsupported.Add("Scene Picture (a sprite with no picture)"); break; }
                    B(Ext); B((int)ExtOp.Scene); B(1); W(pic); W(px); W(py); W(SceneName(cmd.Get("Name")));
                    break;
                }
                case EventCommandType.U6SceneWaitKey:
                    if (cmd.Get("Prompt").Trim().Length > 0) SayWords(cmd.Get("Prompt"));
                    B(Ext); B((int)ExtOp.Scene); B(3);
                    break;
                case EventCommandType.U6SceneRemove:
                    B(Ext); B((int)ExtOp.Scene); B(4); W(cmd.Get("Name").Trim().Length == 0 ? 0xFFFF : SceneName(cmd.Get("Name")));
                    break;
                case EventCommandType.U6SceneEnd:
                    _sceneView = false;
                    B(Ext); B((int)ExtOp.Scene); B(5);
                    break;
                case EventCommandType.U6SceneTypeText:
                    SceneWords(cmd.Get("Prompt"));
                    B(Ext); B((int)ExtOp.Scene); B(6); W(_variable(cmd.Get("Variable"))); B(Math.Clamp(Number(cmd.Get("Letters")), 1, 60)); W(Number(cmd.Get("X"))); W(Number(cmd.Get("Y")));
                    Formula(cmd.Get("Start").Trim().Length == 0 ? "\"\"" : cmd.Get("Start"), "Scene Type Text");
                    break;
                case EventCommandType.U6ScenePickPicture:
                {
                    var names = Extra?.GroupSprites?.Invoke(cmd.Get("Group").Trim()) ?? new List<string>();
                    int pw = Number(cmd.Get("Width")), ph = Number(cmd.Get("Height"));
                    var pics = names.Select(n => (Pic: Extra?.BoxPicture?.Invoke(cmd.Get("Group").Trim(), n, pw <= 0 ? 0 : (pw + 3) / 4 * 4, ph) ?? -1,
                        Por: cmd.Get("Portrait") == "True" ? Extra?.PortraitPic?.Invoke(cmd.Get("Group").Trim(), n) ?? -1 : -1)).Where(p => p.Pic >= 0).Take(40).ToList();
                    bool portrait = cmd.Get("Portrait") == "True" && WhoCode(cmd.Get("Who"), out _, out _);
                    WhoCode(cmd.Get("Who"), out int wm, out int wx);
                    if (pics.Count == 0) { Unsupported.Add("Scene Pick Picture (a sprite group with no pictures)"); break; }
                    SceneWords(cmd.Get("Prompt"));
                    B(Ext); B((int)ExtOp.Scene); B(7); W(_variable(cmd.Get("Variable"))); W(Number(cmd.Get("X"))); W(Number(cmd.Get("Y"))); B(pics.Count); B(Math.Clamp(Number(cmd.Get("Start")), 1, pics.Count));
                    W(wm); W(wx); B(portrait ? 1 : 0);
                    foreach (var (pic, por) in pics) { W(pic); W(por < 0 ? 0xFFFF : por); }
                    break;
                }
                case EventCommandType.U6SetGender:
                {
                    if (!WhoCode(cmd.Get("Who"), out int gm, out int gx)) { Unsupported.Add("Set Gender (someone not on a map)"); break; }
                    int how = cmd.Get("Gender") == "Male" ? 1 : cmd.Get("Gender") == "Female" ? 2 : 0;
                    B(Ext); B((int)ExtOp.Scene); B(8); W(gm); W(gx); B(how); W(how == 0 ? _variable(cmd.Get("Variable")) : 0);
                    break;
                }
                case EventCommandType.U6SceneDistributePoints:
                {
                    if (!WhoCode(cmd.Get("Who"), out int pm, out int pxw)) { Unsupported.Add("Scene Distribute Points (someone not on a map)"); break; }
                    int StatOf(string n) => Extra?.StatByName?.Invoke(n) is int s and >= 0 ? s : n.Trim().ToLowerInvariant() switch
                    {
                        "strength" or "str" => AppleIIgsGamePack.Stat.Str, "dexterity" or "dex" => AppleIIgsGamePack.Stat.Dex, "intelligence" or "int" => AppleIIgsGamePack.Stat.Int,
                        "combat" => AppleIIgsGamePack.Stat.Combat, "magic" => AppleIIgsGamePack.Stat.Magic, _ => -1
                    };
                    var stats = cmd.Get("Stats").Replace("\r", "").Split('\n').Select(s => s.Trim()).Where(s => s.Length > 0).Select(s => (Name: s, Code: StatOf(s))).Where(s => s.Code >= 0).Take(8).ToList();
                    if (stats.Count == 0) { Unsupported.Add("Scene Distribute Points (no stat the Apple IIgs has)"); break; }
                    SceneWords(cmd.Get("Prompt"));
                    B(Ext); B((int)ExtOp.Scene); B(9); W(pm); W(pxw); W(Number(cmd.Get("Least"))); W(Math.Max(0, Number(cmd.Get("Most"))));
                    B(cmd.Get("SpendAll") == "False" ? 0 : 1); W(Number(cmd.Get("X"))); W(Number(cmd.Get("Y")));
                    W(cmd.Get("LeftVariable").Trim().Length == 0 ? 0xFFFF : _variable(cmd.Get("LeftVariable"))); B(stats.Count);
                    foreach (var (n, code) in stats) { B(code); _code.AddRange(Encoding.ASCII.GetBytes(new string(n.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').Take(20).ToArray()))); B(0); }
                    Formula(cmd.Get("Points"), "Scene Distribute Points");
                    break;
                }
                case EventCommandType.U6RiseLevel or EventCommandType.U6Meditate:
                {
                    bool shrine = cmd.Type == EventCommandType.U6Meditate;
                    int m = 0xFFFF, x = 0xFFFF;
                    bool party = shrine || cmd.Get("Party") == "True";
                    if (!party && !WhoCode(cmd.Get("Who"), out m, out x)) { Unsupported.Add("Rise A Level (someone not on a map)"); break; }
                    bool hitsDefault = shrine || cmd.Get("Hits").Trim().Length == 0;
                    int flags = (party ? 1 : 0) | (shrine || cmd.Get("OnlyIfEarned") != "False" ? 2 : 0) | (shrine || cmd.Get("Tell") != "False" ? 4 : 0) | (hitsDefault ? 8 : 0) | (shrine ? 16 : 0);
                    B(Rise); W(m); W(x); B(flags);
                    var virtue = shrine ? U6Leveling.Shrines.FirstOrDefault(s => s.Virtue == cmd.Get("Virtue")) : default;
                    if (virtue.Virtue is not null) { foreach (int n in new[] { virtue.Str, virtue.Dex, virtue.Int }) { B(1); W(n); B(0); } }
                    else { Formula(cmd.Get("Str"), "Rise A Level"); Formula(cmd.Get("Dex"), "Rise A Level"); Formula(cmd.Get("Int"), "Rise A Level"); }
                    if (hitsDefault) { B(1); W(0); B(0); } else Formula(cmd.Get("Hits"), "Rise A Level");
                    string notReady = shrine ? "" : cmd.Get("NotReady").Trim();
                    _code.AddRange(Encoding.ASCII.GetBytes(new string(notReady.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').Take(120).ToArray()))); B(0);
                    break;
                }
                case EventCommandType.U6RunLevelEvent:
                    if (depth < 4 && Extra?.LevelEvent?.Invoke(cmd.Get("Name")) is { } levelCommands) Run(levelCommands, depth + 1);
                    else Unsupported.Add("Run Leveling Event (no leveling event of that name)");
                    break;
                // What an event has a character do (acts.s): ACT who, what, a, b, c.
                case EventCommandType.WalkToLocation or EventCommandType.WalkToFloorLocation:
                    ActCmd(cmd, "Who", 0, Number(cmd.Get("X")), Number(cmd.Get("Y")));
                    break;
                case EventCommandType.TurnToFace:
                    ActCmd(cmd, "Who", 1, cmd.Get("Towards") switch { "North" => 1, "East" => 2, "West" => 3, "South" => 0, _ => 0xFFFF }, 0);
                    break;
                case EventCommandType.PickupItem:
                    ActCmd(cmd, "Who", 2, Number(cmd.Get("X")), Number(cmd.Get("Y")));
                    break;
                case EventCommandType.DropItem:
                {
                    var dropped = Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId"));
                    if (dropped is null) { Unsupported.Add("Drop Item (a thing not in the game)"); break; }
                    ActCmd(cmd, "Who", 3, dropped.Value.Name, 0);
                    break;
                }
                case EventCommandType.OpenDoor:
                    ActCmd(cmd, "Who", 4, Number(cmd.Get("X")), Number(cmd.Get("Y")));
                    break;
                case EventCommandType.Sit:
                    ActCmd(cmd, "Who", 5, cmd.Get("X").Length > 0 ? Number(cmd.Get("X")) : 0xFFFF, Number(cmd.Get("Y")));
                    break;
                case EventCommandType.Stand:
                    ActCmd(cmd, "Who", 6, 0, 0);
                    break;
                case EventCommandType.Attack:
                {
                    if (cmd.Get("Target") is "Player" or "Activator" or "") { ActCmd(cmd, "Attacker", 7, 0, 0); break; } // the Avatar: hostile (combat.s)
                    // EXT 47 FIGHT attacker target (fight.s): someone else - Windows' StartCombat.
                    if (cmd.Get("Attacker") is "Player" or "Activator" or "" || !WhoCode(cmd.Get("Attacker"), out int am, out int ax) || !WhoCode(cmd.Get("Target"), out int tm, out int tx))
                    { Unsupported.Add("Attack (the Avatar as the attacker, or someone not on a map)"); break; }
                    B(Ext); B((int)ExtOp.Fight); W(am); W(ax); W(tm); W(tx);
                    break;
                }
                case EventCommandType.ChangePose:
                    ActCmd(cmd, "Who", 8, cmd.Get("Pose") == "Sleeping" ? 1 : 0, 0);
                    break;
                // Pets (pets.s).
                case EventCommandType.CallCharacter:
                    ActCmd(cmd, "Who", 9, cmd.Get("Follows") != "False" ? 1 : 0, 0);
                    break;
                case EventCommandType.OrderOnTarget:
                {
                    string prompt = cmd.Get("Prompt").Trim();
                    if (prompt.Length == 0) prompt = $"{cmd.Get("Order")}: which one?";
                    ActCmd(cmd, "Who", 10, cmd.Get("Order") switch { "Fetch" => 1, "Go to" => 2, _ => 0 }, 0, Extra?.NameOf?.Invoke(prompt) ?? 0xFFFF);
                    break;
                }
                case EventCommandType.SniffAndLead:
                {
                    string find = cmd.Get("Find");
                    string? scentWho = find == "A character (below)" ? cmd.Get("Other") : find == "The owner of the object (below)" ? Extra?.OwnerOf?.Invoke(Number(cmd.Get("ItemId"))) : null;
                    if (scentWho is not null)
                    {
                        if (!WhoCode(scentWho, out int sm, out int sx) || sm == 0xFFFF) { Unsupported.Add("Sniff And Lead (someone not on a map)"); break; }
                        ActCmd(cmd, "Who", 11, sm, sx);
                    }
                    else if (find is "A character (below)" or "The owner of the object (below)") Unsupported.Add("Sniff And Lead (no one to find)");
                    else if (Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId")) is { } scent) ActCmd(cmd, "Who", 12, scent.Name, 0);
                    else Unsupported.Add("Sniff And Lead (a thing not in the game)");
                    break;
                }
                // Exchange Currency (money.s): XCHG from, to, how many ($FFFF all), rate.
                case EventCommandType.ExchangeCurrency:
                {
                    if (Extra?.Exchange?.Invoke(Number(cmd.Get("FromId")), Number(cmd.Get("ToId"))) is not { } ex) { Unsupported.Add("Exchange Currency (a currency with no Money object)"); break; }
                    B(Xchg); B(ex.From); B(ex.To); W(cmd.Get("Amount") == "All" ? 0xFFFF : Math.Clamp(Number(cmd.Get("Amount")), 0, 65534)); W(ex.Rate);
                    break;
                }
                // Factions (fac.s): FAC who, what, a, b, c.
                case EventCommandType.ChangeFaction:
                    if (!WhoCode(cmd.Get("CharacterId"), out int fm, out int fx)) { Unsupported.Add("Change Faction (someone not on a map)"); break; }
                    B(Fac); W(fm); W(fx); B(0); W(Number(cmd.Get("FactionId"))); W(0); W(0);
                    break;
                case EventCommandType.ChangeFactionRelationship:
                {
                    int viewer = Number(cmd.Get("ViewerFactionId")), target = Number(cmd.Get("TargetFactionId")), avatarFaction = Extra?.AvatarFaction ?? -1;
                    int op = cmd.Get("Operation") switch { "Add" => 1, "Subtract" => 2, _ => 0 }, value = Number(cmd.Get("Value"));
                    // Only how a faction thinks of the Avatar's counts on the IIgs (who attacks the party).
                    int towards = target == avatarFaction ? viewer : cmd.Get("BothWays") == "True" && viewer == avatarFaction ? target : -1;
                    if (towards < 0) { Unsupported.Add("Change Faction Relationship (between factions other than the Avatar's)"); break; }
                    B(Fac); W(0xFFFF); W(0xFFFF); B(1); W(towards); W(op); W(value);
                    break;
                }
                case EventCommandType.CinemaMode:
                    B(Ctl); B(1); // the player has no controls till End Cinema Mode or the event's end (cmds.s OpCtl)
                    break;
                case EventCommandType.EndCinemaMode:
                    B(Ctl); B(0);
                    break;
                case EventCommandType.AllowControls:
                    if (cmd.Get("What") is "Combat mode" or "Keys") { Unsupported.Add($"Allow / Forbid Controls ({cmd.Get("What")})"); break; }
                    B(Ctl); B(cmd.Get("Value") == "Forbidden" ? 2 : 3); // the whole player control
                    break;
                case EventCommandType.ShowBark:
                    // Ultima 6 / Savage Empire have no barks (an Ultima 7 thing) - the user, 2026-11-24: left out on the Apple IIgs.
                    Unsupported.Add("Show Bark (Ultima 6 has no barks)");
                    break;
                case EventCommandType.U6ConversationStyle:
                {
                    // EXT 44 STYLE (talkstyle.s): as U6GameWindow.U6TalkStyle.cs; numbers as written (formulas: their default); one font.
                    int N(string key, int otherwise, int lo, int hi) => int.TryParse(cmd.Get(key).Trim(), out int v) ? Math.Clamp(v, lo, hi) : otherwise;
                    int Col(string key, int otherwise) => cmd.Get(key).Trim() is { Length: > 0 } c && ColourIndex(c.StartsWith('#') || !System.Text.RegularExpressions.Regex.IsMatch(c, "^[0-9A-Fa-f]{6}$") ? c : "#" + c) is int i and >= 0 ? 0x8000 | i : otherwise; // (a slot: talkstyle.s)
                    bool on = cmd.Get("Set") != "Back to Ultima 6's";
                    int answers = cmd.Get("Answers") == U6TalkStyle.List ? 1 : cmd.Get("Answers") == U6TalkStyle.Both ? 2 : 0;
                    int portrait = cmd.Get("Portrait") == U6TalkStyle.PortraitBox ? 1 : cmd.Get("Portrait") == U6TalkStyle.PortraitNone ? 2 : 0;
                    int words = cmd.Get("Words") == U6TalkStyle.WordsBox ? 1 : 0;
                    int bx = N("BoxX", 8, 0, 300), by = N("BoxY", 120, 0, 190), bw = N("BoxWidth", 304, 16, 320 - bx), bh = N("BoxHeight", 72, 12, 200 - by);
                    int pic = cmd.Get("SpriteName").Length > 0 ? Extra?.BoxPicture?.Invoke(cmd.Get("SpriteGroup"), cmd.Get("SpriteName"), bw & ~1, bh) ?? -1 : -1;
                    B(Ext); B((int)ExtOp.Style); B(on ? 1 : 0); B(answers); B(cmd.Get("MarkKeywords") == "False" ? 0 : 1); B(portrait); B(words);
                    W(Col("KeywordColour", 0xFFFF)); W(bx); W(by); W(bw); W(bh); W(Col("BoxColour", ColourIndex("#000000") is int k and >= 0 ? 0x8000 | k : 0));
                    W(pic < 0 ? 0xFFFF : pic); W(Col("TextColour", 0xFFFF));
                    break;
                }
                case EventCommandType.U6ChangeHudFrame:
                {
                    // EXT 42 HUDFRAME n (actkeys.s): the HUD's picture with that frame, made by the editor.
                    int n = cmd.Get("SpriteName").Length == 0 ? 0 : Extra?.HudFrame?.Invoke(cmd.Get("SpriteGroup"), cmd.Get("SpriteName")) ?? -1;
                    if (n < 0) { Unsupported.Add("Change HUD Frame (at most 4 other frames on the Apple IIgs)"); break; }
                    B(Ext); B((int)ExtOp.HudFrame); B(n);
                    break;
                }
                case EventCommandType.U6ChangePortrait:
                {
                    // EXT 43 PORTRAIT who pic (actkeys.s): the Avatar's +12 is the picture + 1 (0 its own), anyone else's the picture.
                    if (!WhoCode(cmd.Get("Who"), out int m, out int x)) { Unsupported.Add("Change Portrait (someone not on a map)"); break; }
                    bool avatar = m == 0xFFFF;
                    int pic = cmd.Get("Own") == "True" ? (avatar ? -2 : Extra?.OwnPortrait?.Invoke(cmd.Get("Who")) ?? -1)
                        : Extra?.PortraitPic?.Invoke(cmd.Get("SpriteGroup"), cmd.Get("SpriteName")) ?? -1;
                    if (pic == -1 && !(cmd.Get("Own") == "True" && !avatar)) { Unsupported.Add("Change Portrait (a sprite with no picture)"); break; }
                    int word = avatar ? (pic == -2 ? 0 : pic + 1) : pic < 0 ? 0xFFFF : pic;
                    B(Ext); B((int)ExtOp.Portrait); W(m); W(x); W(word);
                    break;
                }
                case EventCommandType.U6SetKey:
                case EventCommandType.U6AskKey:
                case EventCommandType.U6GetKeyName:
                case EventCommandType.U6ResetKeys:
                {
                    // EXT 41 KEYS mode action arg swap (actkeys.s) - as U6GameWindow.U6Keys.cs.
                    bool every = cmd.Type == EventCommandType.U6ResetKeys && cmd.Get("Action") == "Every key";
                    int act = every ? 255 : Array.IndexOf(IIgsKeyActions, U6Keys.ByName(cmd.Get("Action"))?.Id ?? "");
                    if (act < 0) { Unsupported.Add($"{cmd.Type} ({cmd.Get("Action")}: no such key on the Apple IIgs)"); break; }
                    int mode = cmd.Type switch { EventCommandType.U6SetKey => 0, EventCommandType.U6AskKey => 1, EventCommandType.U6GetKeyName => 2, _ => 3 };
                    int arg = 0;
                    if (mode == 0)
                    {
                        string? key = U8Keys.FromWords(cmd.Get("Key"));
                        arg = key is null ? 0 : IIgsKeyCode(U8Keys.HeldWith.Contains(cmd.Get("With")) ? cmd.Get("With") + "+" + key : key);
                        if (arg == 0) { Unsupported.Add($"Set Key ({cmd.Get("Key")}: no such key on the Apple IIgs)"); break; }
                    }
                    else if (mode == 2) arg = _variable(cmd.Get("Variable"));
                    B(Ext); B((int)ExtOp.Keys); B(mode); B(act); W(arg); B(cmd.Get("IfUsed") == "Fail" ? 0 : 1);
                    break;
                }
                case EventCommandType.DebugLog:
                    // The user, 2026-11-25 (list item 29): on the Apple IIgs every testing line goes into LOG.TXT on the disk (made if
                    // it isn't there) and the text area says "Log error generated." - quit the game and read it.
                    LogLine(new() { ("", cmd.Get("Value")) });
                    LogShown();
                    break;
                case EventCommandType.PauseHere:
                    LogLine(new() { ("Pause Here", "") });
                    foreach (var name in Extra?.VariableNames ?? new List<string>())
                        LogLine(new() { ("  " + name + " = ", name) });
                    LogShown();
                    break;
                case EventCommandType.ShowError:
                {
                    string number = cmd.Get("Number").Trim(), detail = cmd.Get("Detail").Trim();
                    var parts = new List<(string, string)>();
                    if (int.TryParse(number, out int en)) parts.Add(($"Error {en}: {Extra?.ErrorWords?.Invoke(en) ?? ""}", ""));
                    else parts.Add(("Error ", number));
                    if (detail.Length > 0) parts.Add((" - ", detail));
                    LogLine(parts);
                    LogShown();
                    break;
                }
                case EventCommandType.Comment or EventCommandType.ReturnFromCommon:
                    break;
                case EventCommandType.StopConversation or EventCommandType.End:
                    B(End);
                    break;
                default:
                    Unsupported.Add(cmd.Type.ToString());
                    break;
            }
        }
        while (frames.Count > 0)
        {
            var f = frames.Pop();
            if (f.IsChoices) { LeaveBranch(f); foreach (int at in f.EndAt.Concat(f.TargetAt.Values)) Fill(at, Here); }
            else { if (f.ElseAt >= 0) Fill(f.ElseAt, Here); foreach (int at in f.EndAt) Fill(at, Here); }
        }
        if (depth > 0) { foreach (var (at, label) in gotos) Fill(at, labels.TryGetValue(label, out int to) ? to : Here); return; }
        B(End);
        foreach (var (at, label) in gotos) Fill(at, labels.TryGetValue(label, out int to) ? to : Here - 1);
    }

    /// <summary>The end of a reply's branch: back to its question (a pinned reply), or on after the replies.</summary>
    /// <summary>A Who as the IIgs player finds it: the Avatar ($FFFF $FFFF) or a character by map and place.</summary>
    /// <summary>ACT (acts.s): who, what, a, b, c - 12 bytes.</summary>
    private void ActCmd(EventCommand cmd, string whoKey, int what, int a, int b, int c = 0)
    {
        if (!WhoCode(cmd.Get(whoKey), out int m, out int x)) { Unsupported.Add($"{cmd.Type} (someone not on a map)"); return; }
        B(Act); W(m); W(x); B(what); W(a & 0xFFFF); W(b & 0xFFFF); W(c & 0xFFFF);
    }

    private bool WhoCode(string who, out int map, out int index)
    {
        map = index = 0xFFFF;
        if (who is "" or "Activator" or "Player") return true;
        if (_who?.Invoke(who) is not { } at) return false;
        (map, index) = at;
        return true;
    }

    private static double Seconds(string s) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;

    private void LeaveBranch(Frame f)
    {
        if (f.Current is not int id) return;
        B(Jump);
        if (f.Pinned.GetValueOrDefault(id)) W(f.AskAt);
        else f.EndAt.Add(Hole());
        f.Current = null;
    }

    private static int Number(string s) => int.TryParse(s.Trim(), out int v) ? Math.Clamp(v, -32768, 32767) : 0;

    /// <summary>The replies' words marked in the text (each word between \u0001 and \u0002), as the Windows Ultima 6 engine marks them.</summary>
    public static string Marked(string text, List<string> keywords)
    {
        foreach (var keyword in keywords.OrderByDescending(k => k.Length))
        {
            int at = 0;
            while ((at = text.IndexOf(keyword, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                bool startOk = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
                bool endOk = at + keyword.Length >= text.Length || !char.IsLetterOrDigit(text[at + keyword.Length]);
                if (!startOk || !endOk || (at > 0 && text[at - 1] == '\u0001')) { at += keyword.Length; continue; }
                string marked = string.Join(" ", text.Substring(at, keyword.Length).Split(' ').Select(w => "\u0001" + w + "\u0002"));
                text = text[..at] + marked + text[(at + keyword.Length)..];
                at += marked.Length;
            }
        }
        return text;
    }
}
