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
        Give = 12, Take = 13, Hp = 14, StatOp = 15, WaitOp = 16, Place = 17, Kill = 18, Sched = 19, Time = 20, Door = 21, Pic = 22, Erase = 23, GameOp = 24, Pay = 25, Sell = 26, More = 27, Ail = 28, Calc = 29, IfX = 30, Haggle = 31, PayV = 32, Music = 33, Volume = 34, Resume = 35, Weather = 36, Vehicle = 37;
    /// <summary>What the commands need from the game being built (all optional).</summary>
    public sealed class Env
    {
        /// <summary>An item (Category, ItemId) -> its name, sprite, icon and object flags in the game.</summary>
        public Func<string, string, (int Name, int Sprite, int Icon, int Flags, int Other)?>? Item;
        /// <summary>A sprite (group, name) -> its picture's sprite number.</summary>
        public Func<string, string, int>? Picture;
        /// <summary>A stat's id -> its word in a character's numbers (0 hit points ... 10 experience), -1 not on the IIgs.</summary>
        public Func<string, int>? Stat;
        /// <summary>A Who's name (Show Bark).</summary>
        public Func<string, string?>? WhoName;
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
        foreach (var paragraph in text.Replace("\r", "").TrimEnd().Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                if (line.Length > 0 && line.Length + 1 + word.Length > chars) { all.Add(line); line = ""; }
                line = line.Length == 0 ? word : line + " " + word;
            }
            all.Add(line);
        }
        for (int i = 0; i < all.Count; i += Math.Max(1, lines))
        {
            if (i > 0) talk.B(More);
            talk.SayLine(string.Join("\n", all.Skip(i).Take(Math.Max(1, lines))));
        }
        talk.B(End);
        return talk._code.ToArray();
    }

    /// <summary>SAY the words (printable letters only).</summary>
    private void SayLine(string text)
    {
        var bytes = Encoding.Latin1.GetBytes(new string(text.Select(ch => ch is '\n' or '\u0001' or '\u0002' || ch is >= ' ' and <= '~' ? ch : ch == '\u2019' ? '\'' : '?').Take(1000).ToArray()));
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
        public int Top, Continue = -1, NextCase = -1, Select = -1;
        public List<int> Ends = new();
    }
    private readonly Stack<Loop> _loops = new();
    private int _hidden;

    /// <summary>A formula into the code (its stack code); false if the Apple IIgs can't do it.</summary>
    private bool Formula(string formula, string what)
    {
        var code = ScriptExpression.CompileIIgs(formula.Trim().Length == 0 ? "0" : formula, _variable, out _);
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

    /// <summary>A fade's seconds in ticks (60 a second, at most a minute).</summary>
    private int FadeTicks(EventCommand cmd, double least)
    {
        var code = ScriptExpression.CompileIIgs(cmd.Get("Seconds").Trim().Length == 0 ? "0" : cmd.Get("Seconds"), _variable, out _);
        double s = double.TryParse(cmd.Get("Seconds"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v) ? v : 0;
        if (code is null || (s == 0 && cmd.Get("Seconds").Trim() is { Length: > 0 } t && t != "0")) Unsupported.Add(cmd.Type + " (a fade time that's a formula: a number of seconds only)");
        return (int)Math.Clamp(Math.Max(least, s) * 60, 0, 3600);
    }

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
                    for (int i = 0; i < count; i++) { B(Give); W(it.Name); W(it.Sprite); W(it.Icon); W(it.Flags); W(it.Other); }
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
                            var code = ScriptExpression.CompileIIgs(values[k], _variable, out _);
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
                    var loop = _loops.FirstOrDefault(l => l.Kind is EventCommandType.While or EventCommandType.RepeatTimes);
                    if (loop is null) break;
                    B(Jump);
                    if (cmd.Type == EventCommandType.BreakLoop) loop.Ends.Add(Hole()); else W(loop.Continue);
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
                case EventCommandType.ShowBark:
                {
                    string who = Extra?.WhoName?.Invoke(cmd.Get("Who")) ?? "";
                    SayLine(who.Length > 0 ? $"{who}: \"{cmd.Get("Value")}\"" : cmd.Get("Value"));
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
