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
/// Commands the IIgs player doesn't do yet are left out (Unsupported lists them).
/// </summary>
public sealed class AppleIIgsTalk
{
    public const byte End = 0, Say = 1, Ask = 2, Jump = 3, SetV = 4, IfV = 5;
    private readonly List<byte> _code = new();
    private readonly Func<string, int> _variable;
    public HashSet<string> Unsupported { get; } = new();

    private AppleIIgsTalk(Func<string, int> variable) => _variable = variable;

    /// <summary>The code for a list of commands; variable(name) gives a variable's or switch's number.</summary>
    public static AppleIIgsTalk Compile(List<EventCommand> commands, Func<string, int> variable, out byte[] code)
    {
        var talk = new AppleIIgsTalk(variable);
        talk.Run(commands);
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
