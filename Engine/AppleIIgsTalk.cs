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
/// 6 having / not having an item, 7 a thing dropped on it, 8 a thing picked up from it.
/// More commands (b636; "who" = u16 map, u16 place in its list; $FFFF $FFFF the Avatar):
///   $0C GIVE u16 name, u16 sprite, u16 icon, u16 flags      a thing to the Avatar (into a spare object)
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
/// Commands the IIgs player doesn't do yet are left out (Unsupported lists them).
/// </summary>
public sealed class AppleIIgsTalk
{
    public const byte End = 0, Say = 1, Ask = 2, Jump = 3, SetV = 4, IfV = 5, Play = 6, Map = 7, Party = 8, Trig = 9, ItemOp = 10, Hit = 11,
        Give = 12, Take = 13, Hp = 14, StatOp = 15, WaitOp = 16, Place = 17, Kill = 18, Sched = 19, Time = 20, Door = 21, Pic = 22, Erase = 23, GameOp = 24;
    /// <summary>What the commands need from the game being built (all optional).</summary>
    public sealed class Env
    {
        /// <summary>An item (Category, ItemId) -> its name, sprite, icon and object flags in the game.</summary>
        public Func<string, string, (int Name, int Sprite, int Icon, int Flags)?>? Item;
        /// <summary>A sprite (group, name) -> its picture's sprite number.</summary>
        public Func<string, string, int>? Picture;
        /// <summary>A stat's id -> its word in a character's numbers (0 hit points ... 10 experience), -1 not on the IIgs.</summary>
        public Func<string, int>? Stat;
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

    private void Run(List<EventCommand> commands)
    {
        var frames = new Stack<Frame>();
        var labels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var gotos = new List<(int At, string Label)>();
        foreach (var cmd in commands)
        {
            switch (cmd.Type)
            {
                case EventCommandType.ShowText:
                {
                    var choices = DialogChoices.HasChoices(cmd) ? DialogChoices.Read(cmd) : new List<DialogChoice>();
                    string text = Marked(cmd.Get("Text").Replace("\r", "").Trim(), choices.Select(c => c.Text).Where(t => t.Trim().Length > 0).ToList());
                    if (text.Length > 0)
                    {
                        var bytes = Encoding.Latin1.GetBytes(new string(text.Select(ch => ch is '\n' or '\u0001' or '\u0002' || ch is >= ' ' and <= '~' ? ch : ch == '’' ? '\'' : '?').Take(1000).ToArray()));
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
                    if (cmd.Get("Receiver") is not ("" or "Player" or "Activator") || Extra?.Item?.Invoke(cmd.Get("Category"), cmd.Get("ItemId")) is not { } it)
                    { Unsupported.Add("Give Item (to someone but the Avatar, or an item not on the IIgs)"); break; }
                    int count = Math.Clamp(Number(cmd.Get("Quantity")), 1, 8);
                    for (int i = 0; i < count; i++) { B(Give); W(it.Name); W(it.Sprite); W(it.Icon); W(it.Flags); }
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
                    if (cmd.Get("Who") is not ("" or "Activator" or "Player")) { Unsupported.Add("Teleport (someone but the Avatar)"); break; }
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
                case EventCommandType.GameOver: B(GameOp); B(0); break;
                case EventCommandType.QuitGame: B(GameOp); B(1); break;
                case EventCommandType.SaveGame: B(GameOp); B(2); break;
                case EventCommandType.LoadGame: B(GameOp); B(3); break;
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
        B(End);
        foreach (var (at, label) in gotos) Fill(at, labels.TryGetValue(label, out int to) ? to : Here - 1);
    }

    /// <summary>The end of a reply's branch: back to its question (a pinned reply), or on after the replies.</summary>
    /// <summary>A Who as the IIgs player finds it: the Avatar ($FFFF $FFFF) or a character by map and place.</summary>
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
