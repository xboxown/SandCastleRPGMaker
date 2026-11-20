using System.Collections.Concurrent;
using System.Globalization;
using System.Text;

namespace SandcastleRpgMaker.Engine;

/// <summary>The game's own functions a formula can call (hp(Player), hour(), gold()...) - only a running game has them.</summary>
public interface IScriptGameFunctions
{
    /// <summary>False when the game has no function of that name.</summary>
    bool TryCall(string name, IReadOnlyList<object> args, out object result);
}

/// <summary>An event context that holds script data (text variables, arrays, grids, self switches).</summary>
public interface IScriptDataHost
{
    ScriptData ScriptData { get; }
}

/// <summary>
/// Formulas, as typed into the script commands' boxes:
///   numbers 12, 3.5 - texts "hello" ("" inside for a quote) - true / false
///   variables: Gold, or {Gold Coins} when the name has spaces
///   arrays Items[0], Items[i + 1] - grids Board[x, y] (both count from 0)
///   + - * / % (remainder) - ( ) - = != &lt; &gt; &lt;= &gt;= - and or not
///   + joins texts: "Gold: " + Gold
///   functions: random(1, 6), chance(25), min, max, abs, round, floor, ceil, sqrt, pow, clamp,
///   length(x), rows(g), columns(g), contains(x, v), indexof(x, v), sum(a), average(a),
///   upper, lower, trim, part(t, start, count), left, right, replace(t, a, b), join(a, ", "),
///   text(v), number(v), if(condition, a, b), and the game's own (hp(Player), hour()...).
/// Parsed once and kept, so a loop doesn't parse its formula every turn.
/// </summary>
public static class ScriptExpression
{
    private static readonly ConcurrentDictionary<string, Node> Cache = new();
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    [ThreadStatic] private static Random? _random;
    private static Random Rng => _random ??= new Random();

    /// <summary>Evaluates a formula; an empty one is 0.</summary>
    public static object Evaluate(string formula, IEventExecutionContext context)
    {
        if (string.IsNullOrWhiteSpace(formula)) return 0.0;
        var node = Cache.GetOrAdd(formula, Parse);
        return node.Eval(new Env(context));
    }

    public static bool IsTrue(string formula, IEventExecutionContext context) => ScriptValues.Truth(Evaluate(formula, context));

    /// <summary>Null when the formula is fine, else what's wrong (for the command windows' OK).</summary>
    public static string? Check(string formula)
    {
        if (string.IsNullOrWhiteSpace(formula)) return null;
        try { Cache.GetOrAdd(formula, Parse); return null; }
        catch (ScriptException ex) { return ex.Message; }
    }

    // ---------- Reading and writing variables (numbers in the context, texts in ScriptData) ----------

    public static ScriptData DataOf(IEventExecutionContext context) =>
        context is IScriptDataHost host ? host.ScriptData : FallbackData.GetValue(context, _ => new ScriptData());

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IEventExecutionContext, ScriptData> FallbackData = new();

    public static object GetVariable(IEventExecutionContext context, string name)
    {
        var data = DataOf(context);
        return data.Texts.TryGetValue(name, out var text) ? text : context.GetVariable(name);
    }

    public static void SetVariable(IEventExecutionContext context, string name, object value)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ScriptException("no variable was picked");
        var data = DataOf(context);
        if (value is string text && !ScriptValues.TryNumber(text, out _)) { data.Texts[name] = text; return; }
        data.Texts.Remove(name);
        context.SetVariable(name, ScriptValues.Number(value));
    }

    // ---------- Tree ----------

    private sealed class Env
    {
        public readonly IEventExecutionContext Context;
        public readonly ScriptData Data;
        public Env(IEventExecutionContext context) { Context = context; Data = DataOf(context); }
    }

    private abstract class Node { public abstract object Eval(Env env); }

    private sealed class Literal : Node
    {
        private readonly object _value;
        public object Value => _value;
        public Literal(object value) => _value = value;
        public override object Eval(Env env) => _value;
    }

    /// <summary>A plain name: a variable - or, as a function's argument, an array or grid.</summary>
    private sealed class Name : Node
    {
        public readonly string Text;
        public Name(string text) => Text = text;
        public override object Eval(Env env)
        {
            if (env.Data.Texts.TryGetValue(Text, out var text)) return text;
            if (env.Data.Arrays.TryGetValue(Text, out var list)) return list;
            if (env.Data.Grids.TryGetValue(Text, out var grid)) return grid;
            return env.Context.GetVariable(Text);
        }
    }

    private sealed class Index : Node
    {
        private readonly string _name;
        private readonly Node _first;
        private readonly Node? _second;
        public Index(string name, Node first, Node? second) { _name = name; _first = first; _second = second; }
        public string ListName => _name; public Node First => _first; public Node? Second => _second;
        public override object Eval(Env env)
        {
            if (_second is null)
            {
                var list = env.Data.ArrayOf(_name);
                int i = (int)ScriptValues.Number(_first.Eval(env));
                if (i < 0 || i >= list.Count) throw new ScriptException($"{_name}[{i}] is outside the array (it has {list.Count}, from 0 to {list.Count - 1})");
                return list[i];
            }
            var grid = env.Data.GridOf(_name);
            return grid[(int)ScriptValues.Number(_first.Eval(env)), (int)ScriptValues.Number(_second.Eval(env))];
        }
    }

    private sealed class Unary : Node
    {
        private readonly string _op; private readonly Node _a;
        public string Op => _op; public Node Operand => _a;
        public Unary(string op, Node a) { _op = op; _a = a; }
        public override object Eval(Env env) => _op == "-" ? -ScriptValues.Number(_a.Eval(env)) : (ScriptValues.Truth(_a.Eval(env)) ? 0.0 : 1.0);
    }

    private sealed class Binary : Node
    {
        private readonly string _op; private readonly Node _a, _b;
        public string Op => _op; public Node Left => _a; public Node Right => _b;
        public Binary(string op, Node a, Node b) { _op = op; _a = a; _b = b; }
        public override object Eval(Env env)
        {
            if (_op == "and") return ScriptValues.Truth(_a.Eval(env)) && ScriptValues.Truth(_b.Eval(env)) ? 1.0 : 0.0;
            if (_op == "or") return ScriptValues.Truth(_a.Eval(env)) || ScriptValues.Truth(_b.Eval(env)) ? 1.0 : 0.0;
            object x = _a.Eval(env), y = _b.Eval(env);
            switch (_op)
            {
                case "+":
                    // Two numbers (a typed "12" too) add up; anything else joins as text.
                    return ScriptValues.TryNumber(x, out double p) && ScriptValues.TryNumber(y, out double q) ? p + q : ScriptValues.Text(x) + ScriptValues.Text(y);
                case "-": return ScriptValues.Number(x) - ScriptValues.Number(y);
                case "*": return ScriptValues.Number(x) * ScriptValues.Number(y);
                case "/": { double d = ScriptValues.Number(y); if (d == 0) throw new ScriptException("division by zero"); return ScriptValues.Number(x) / d; }
                case "%": { double d = ScriptValues.Number(y); if (d == 0) throw new ScriptException("remainder of a division by zero"); double r = ScriptValues.Number(x) % d; return r < 0 ? r + Math.Abs(d) : r; }
                case "=": return ScriptValues.Compare(x, y) == 0 ? 1.0 : 0.0;
                case "!=": return ScriptValues.Compare(x, y) != 0 ? 1.0 : 0.0;
                case "<": return ScriptValues.Compare(x, y) < 0 ? 1.0 : 0.0;
                case ">": return ScriptValues.Compare(x, y) > 0 ? 1.0 : 0.0;
                case "<=": return ScriptValues.Compare(x, y) <= 0 ? 1.0 : 0.0;
                case ">=": return ScriptValues.Compare(x, y) >= 0 ? 1.0 : 0.0;
            }
            throw new ScriptException($"unknown operator {_op}");
        }
    }

    private sealed class Call : Node
    {
        private readonly string _name; private readonly List<Node> _args;
        public string FunctionName => _name; public List<Node> Arguments => _args;
        public Call(string name, List<Node> args) { _name = name.ToLowerInvariant(); _args = args; }
        public override object Eval(Env env)
        {
            if (_name == "if")
            {
                Need(3);
                return ScriptValues.Truth(_args[0].Eval(env)) ? _args[1].Eval(env) : _args[2].Eval(env);
            }
            var a = _args.Select(n => n.Eval(env)).ToList();
            double N(int i) => ScriptValues.Number(a[i]);
            string T(int i) => ScriptValues.Text(a[i]);
            switch (_name)
            {
                case "random": Need(2); { int lo = (int)Math.Min(N(0), N(1)), hi = (int)Math.Max(N(0), N(1)); return (double)Rng.Next(lo, hi + 1); }
                case "chance": Need(1); return Rng.NextDouble() * 100 < N(0) ? 1.0 : 0.0;
                case "min": return Numbers(a).DefaultIfEmpty(0).Min();
                case "max": return Numbers(a).DefaultIfEmpty(0).Max();
                case "sum": return Numbers(a).Sum();
                case "average": { var n = Numbers(a).ToList(); return n.Count == 0 ? 0.0 : n.Average(); }
                case "abs": Need(1); return Math.Abs(N(0));
                case "round": return a.Count >= 2 ? Math.Round(N(0), (int)Math.Clamp(N(1), 0, 10), MidpointRounding.AwayFromZero) : Math.Round(N(0), MidpointRounding.AwayFromZero);
                case "floor": Need(1); return Math.Floor(N(0));
                case "ceil": Need(1); return Math.Ceiling(N(0));
                case "sqrt": Need(1); return Math.Sqrt(Math.Max(0, N(0)));
                case "pow": Need(2); return Math.Pow(N(0), N(1));
                case "clamp": Need(3); return Math.Clamp(N(0), Math.Min(N(1), N(2)), Math.Max(N(1), N(2)));
                case "length":
                case "count": Need(1); return a[0] switch { List<object> l => (double)l.Count, ScriptGrid g => (double)(g.Columns * g.Rows), _ => (double)T(0).Length };
                case "rows": Need(1); return a[0] is ScriptGrid gr ? (double)gr.Rows : throw new ScriptException("rows() needs a grid");
                case "columns":
                case "cols": Need(1); return a[0] is ScriptGrid gc ? (double)gc.Columns : throw new ScriptException("columns() needs a grid");
                case "contains": Need(2); return IndexOf(a[0], a[1]) >= 0 ? 1.0 : 0.0;
                case "indexof": Need(2); return (double)IndexOf(a[0], a[1]);
                case "upper": Need(1); return T(0).ToUpperInvariant();
                case "lower": Need(1); return T(0).ToLowerInvariant();
                case "trim": Need(1); return T(0).Trim();
                case "part":
                case "sub": Need(2); { string s = T(0); int start = Math.Clamp((int)N(1), 0, s.Length); int count = a.Count >= 3 ? Math.Clamp((int)N(2), 0, s.Length - start) : s.Length - start; return s.Substring(start, count); }
                case "left": Need(2); { string s = T(0); return s[..Math.Clamp((int)N(1), 0, s.Length)]; }
                case "right": Need(2); { string s = T(0); int n = Math.Clamp((int)N(1), 0, s.Length); return s[(s.Length - n)..]; }
                case "replace": Need(3); return T(1).Length == 0 ? T(0) : T(0).Replace(T(1), T(2), StringComparison.OrdinalIgnoreCase);
                case "join": Need(1); return a[0] is List<object> jl ? string.Join(a.Count >= 2 ? T(1) : ", ", jl.Select(ScriptValues.Text)) : T(0);
                case "text": Need(1); return T(0);
                case "number": Need(1); return N(0);
            }
            if (env.Context is IScriptGameFunctions game && game.TryCall(_name, a, out var result)) return result;
            throw new ScriptException($"there is no function called {_name}()");
        }

        private void Need(int count)
        {
            if (_args.Count < count) throw new ScriptException($"{_name}() needs {count} value{(count == 1 ? "" : "s")}");
        }

        private static IEnumerable<double> Numbers(List<object> values)
        {
            foreach (var v in values)
            {
                if (v is List<object> list) foreach (var e in list) yield return ScriptValues.Number(e);
                else if (v is ScriptGrid grid) foreach (var e in grid.Cells) yield return ScriptValues.Number(e);
                else yield return ScriptValues.Number(v);
            }
        }

        private static int IndexOf(object haystack, object needle)
        {
            if (haystack is List<object> list) return list.FindIndex(e => ScriptValues.Same(e, needle));
            if (haystack is ScriptGrid grid) return Array.FindIndex(grid.Cells, e => ScriptValues.Same(e, needle));
            return ScriptValues.Text(haystack).IndexOf(ScriptValues.Text(needle), StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---------- Parsing ----------

    private enum Tok { Number, Text, Name, Op, End }

    private sealed class Parser
    {
        private readonly List<(Tok Kind, string Text)> _tokens;
        private int _at;
        public Parser(List<(Tok, string)> tokens) => _tokens = tokens;
        private (Tok Kind, string Text) Peek => _tokens[_at];
        private bool IsOp(string op) => Peek.Kind == Tok.Op && Peek.Text == op;
        private bool IsWord(string word) => Peek.Kind == Tok.Name && string.Equals(Peek.Text, word, StringComparison.OrdinalIgnoreCase) && !_bracedNames.Contains(_at);
        public readonly HashSet<int> _bracedNames = new();

        public Node ParseAll()
        {
            var node = Or();
            if (Peek.Kind != Tok.End) throw new ScriptException($"didn't expect \"{Peek.Text}\" there");
            return node;
        }

        private Node Or()
        {
            var a = And();
            while (IsWord("or") || IsOp("||")) { _at++; a = new Binary("or", a, And()); }
            return a;
        }

        private Node And()
        {
            var a = Not();
            while (IsWord("and") || IsOp("&&")) { _at++; a = new Binary("and", a, Not()); }
            return a;
        }

        private Node Not()
        {
            if (IsWord("not") || IsOp("!")) { _at++; return new Unary("not", Not()); }
            return Compare();
        }

        private Node Compare()
        {
            var a = Add();
            if (Peek.Kind == Tok.Op && Peek.Text is "=" or "==" or "!=" or "<>" or "<" or ">" or "<=" or ">=")
            {
                string op = Peek.Text switch { "==" => "=", "<>" => "!=", var o => o };
                _at++;
                return new Binary(op, a, Add());
            }
            return a;
        }

        private Node Add()
        {
            var a = Mul();
            while (IsOp("+") || IsOp("-")) { string op = Peek.Text; _at++; a = new Binary(op, a, Mul()); }
            return a;
        }

        private Node Mul()
        {
            var a = UnaryMinus();
            while (IsOp("*") || IsOp("/") || IsOp("%")) { string op = Peek.Text; _at++; a = new Binary(op, a, UnaryMinus()); }
            return a;
        }

        private Node UnaryMinus()
        {
            if (IsOp("-")) { _at++; return new Unary("-", UnaryMinus()); }
            if (IsOp("+")) { _at++; return UnaryMinus(); }
            return Primary();
        }

        private Node Primary()
        {
            var (kind, text) = Peek;
            switch (kind)
            {
                case Tok.Number: _at++; return new Literal(double.Parse(text, Inv));
                case Tok.Text: _at++; return new Literal(text);
                case Tok.Op when text == "(":
                {
                    _at++;
                    var inner = Or();
                    Expect(")");
                    return inner;
                }
                case Tok.Name:
                {
                    bool braced = _bracedNames.Contains(_at);
                    _at++;
                    if (!braced && text.Equals("true", StringComparison.OrdinalIgnoreCase)) return new Literal(1.0);
                    if (!braced && text.Equals("false", StringComparison.OrdinalIgnoreCase)) return new Literal(0.0);
                    if (!braced && IsOp("("))
                    {
                        _at++;
                        var args = new List<Node>();
                        if (!IsOp(")"))
                        {
                            args.Add(Or());
                            while (IsOp(",")) { _at++; args.Add(Or()); }
                        }
                        Expect(")");
                        return new Call(text, args);
                    }
                    if (IsOp("["))
                    {
                        _at++;
                        var first = Or();
                        Node? second = null;
                        if (IsOp(",")) { _at++; second = Or(); }
                        Expect("]");
                        return new Index(text, first, second);
                    }
                    return new Name(text);
                }
            }
            throw new ScriptException(kind == Tok.End ? "the formula ends too soon" : $"didn't expect \"{text}\" there");
        }

        private void Expect(string op)
        {
            if (!IsOp(op)) throw new ScriptException($"a \"{op}\" is missing");
            _at++;
        }
    }

    // ---------- The Apple IIgs player's formulas (Player/iigs/expr.s): whole numbers on a stack ----------

    /// <summary>The formula as the Apple IIgs player's stack code (ops: 1 number, 2 variable, 3 + 4 - 5 * 6 / 7 %, 8 negate,
    /// 9 = 10 != 11 &lt; 12 &gt; 13 &lt;= 14 &gt;=, 15 and 16 or 17 not, 18 random(a, b) 19 chance(n) 20 min 21 max 22 abs 23 clamp,
    /// 0 the end). Null (and why): something the Apple IIgs can't do - texts, lists, grids, the game's own functions.</summary>
    /// <summary>The Apple IIgs's names for a formula: arrays, grids (their numbers), characters (their map and place), stats
    /// (their word), things (their name's number).</summary>
    public sealed class IIgsNames
    {
        public Func<string, int>? Array, Grid, Stat, Item;
        public Func<string, (int Map, int Index)?>? Who;
    }

    /// <summary>
    /// A formula as the Apple IIgs player's stack code (Player/iigs/expr.s): numbers (whole), texts, variables (a number or
    /// a text), lists and grids (list[i], grid[x, y]), + - * / %, comparisons (numbers, or texts without case), and / or /
    /// not, the functions, and the game's (of the Avatar, or of a character named). Null (why) if the IIgs can't do it.
    /// </summary>
    public static List<byte>? CompileIIgs(string formula, Func<string, int> variable, out string? why, IIgsNames? names = null)
    {
        why = null;
        try
        {
            var code = new List<byte>();
            Emit(Parse(formula), code, variable, names);
            code.Add(0);
            return code;
        }
        catch (ScriptException e) { why = e.Message; return null; }
    }

    private static void Word(List<byte> code, int v) { code.Add((byte)v); code.Add((byte)(v >> 8)); }

    private static void TextOp(List<byte> code, string text)
    {
        var bytes = Encoding.ASCII.GetBytes(new string(text.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').Take(47).ToArray()));
        code.Add(29); code.Add((byte)bytes.Length); code.AddRange(bytes);
    }

    // The game's functions on the IIgs (expression op 35: function, who's map and place).
    private static readonly Dictionary<string, int> IIgsGame = new()
    {
        ["hp"] = 1, ["maxhp"] = 2, ["x"] = 3, ["y"] = 4, ["gold"] = 5, ["food"] = 6, ["hour"] = 7, ["minute"] = 8, ["day"] = 9, ["partysize"] = 10,
        ["isdead"] = 11, ["inparty"] = 12, ["name"] = 13, ["facing"] = 14, ["stat"] = 15, ["items"] = 16, ["floorof"] = 17, ["combat"] = 18
    };
    // Text and list functions (expression op 34: function, how many values).
    private static readonly Dictionary<string, int> IIgsFns = new()
    {
        ["length"] = 1, ["count"] = 1, ["rows"] = 2, ["columns"] = 3, ["cols"] = 3, ["contains"] = 4, ["indexof"] = 5, ["upper"] = 6, ["lower"] = 7, ["trim"] = 8,
        ["part"] = 9, ["sub"] = 9, ["left"] = 10, ["right"] = 11, ["replace"] = 12, ["join"] = 13, ["text"] = 14, ["number"] = 15, ["sum"] = 16, ["average"] = 17,
        ["sqrt"] = 18, ["pow"] = 19
    };

    private static void Emit(Node node, List<byte> code, Func<string, int> variable, IIgsNames? names = null)
    {
        switch (node)
        {
            case Literal l when l.Value is double d:
                code.Add(1); Word(code, (int)Math.Clamp(Math.Round(d), -32768, 32767)); break;
            case Literal l2:
                TextOp(code, ScriptValues.Text(l2.Value)); break;
            case Name n:
                if (names?.Array?.Invoke(n.Text) is int ai and >= 0) { code.Add(30); code.Add((byte)ai); break; }
                if (names?.Grid?.Invoke(n.Text) is int gi and >= 0) { code.Add(31); code.Add((byte)gi); break; }
                code.Add(2); Word(code, variable(n.Text)); break;
            case Unary u:
                Emit(u.Operand, code, variable, names); code.Add(u.Op == "-" ? (byte)8 : (byte)17); break;
            case Index ix:
            {
                if (ix.Second is null)
                {
                    if (names?.Array?.Invoke(ix.ListName) is not int li || li < 0) throw new ScriptException($"{ix.ListName} isn't an array the game makes");
                    code.Add(30); code.Add((byte)li); Emit(ix.First, code, variable, names); code.Add(32);
                }
                else
                {
                    if (names?.Grid?.Invoke(ix.ListName) is not int gi2 || gi2 < 0) throw new ScriptException($"{ix.ListName} isn't a grid the game makes");
                    code.Add(31); code.Add((byte)gi2); Emit(ix.First, code, variable, names); Emit(ix.Second, code, variable, names); code.Add(33);
                }
                break;
            }
            case Binary b:
                Emit(b.Left, code, variable, names); Emit(b.Right, code, variable, names);
                code.Add(b.Op switch { "+" => 3, "-" => 4, "*" => 5, "/" => 6, "%" => 7, "=" => 9, "!=" => 10, "<" => 11, ">" => 12, "<=" => 13, ">=" => 14, "and" => 15, "or" => 16,
                    _ => throw new ScriptException($"\"{b.Op}\" isn't on the Apple IIgs") });
                break;
            case Call c:
            {
                string f = c.FunctionName.ToLowerInvariant();
                if (f is "round" or "floor" or "ceil" && c.Arguments.Count >= 1) { Emit(c.Arguments[0], code, variable, names); break; } // whole numbers already
                if (IIgsGame.TryGetValue(f, out int gf) && !(f is "x" or "y" && names is null))
                {
                    // Of whom: the first value, a character's name ("Self" / "Player" / none: the Avatar).
                    (int Map, int Index) who = (0xFFFF, 0xFFFF);
                    int extra = -1;
                    var rest = c.Arguments.ToList();
                    if (gf is 15 or 16)
                    {
                        // stat(who, "name") / items("name") or items(who, "name"): the last value is what.
                        if (rest.Count == 0 || rest[^1] is not Literal { Value: string what }) throw new ScriptException($"{f}() needs its name as a text");
                        extra = gf == 15 ? names?.Stat?.Invoke(what) ?? -1 : names?.Item?.Invoke(what) ?? -1;
                        if (extra < 0) throw new ScriptException($"{f}(\"{what}\") isn't on the Apple IIgs");
                        rest.RemoveAt(rest.Count - 1);
                    }
                    if (rest.Count > 0)
                    {
                        if (rest[0] is not Literal { Value: string whoName }) throw new ScriptException($"{f}() of someone: their name as a text");
                        if (whoName is not ("Self" or "Player" or "Activator" or ""))
                            who = names?.Who?.Invoke(whoName) ?? throw new ScriptException($"{f}(\"{whoName}\"): no one of that name on a map");
                    }
                    code.Add(35); code.Add((byte)gf); Word(code, who.Map); Word(code, who.Index);
                    if (gf == 15) code.Add((byte)extra);
                    if (gf == 16) Word(code, extra);
                    break;
                }
                if ((f is "min" or "max" && c.Arguments.Count == 1) || IIgsFns.ContainsKey(f))
                {
                    int fn = f == "min" ? 20 : f == "max" ? 21 : IIgsFns[f];
                    foreach (var a in c.Arguments.Take(3)) Emit(a, code, variable, names);
                    code.Add(34); code.Add((byte)fn); code.Add((byte)Math.Min(3, c.Arguments.Count));
                    break;
                }
                (int ops, byte op) = f switch { "random" => (2, (byte)18), "chance" => (1, (byte)19), "abs" => (1, (byte)22), "clamp" => (3, (byte)23), "min" => (-1, (byte)20), "max" => (-1, (byte)21),
                    _ => throw new ScriptException($"{f}() isn't on the Apple IIgs") };
                if (ops < 0)
                {
                    if (c.Arguments.Count == 0) { code.Add(1); Word(code, 0); break; }
                    Emit(c.Arguments[0], code, variable, names);
                    foreach (var a in c.Arguments.Skip(1)) { Emit(a, code, variable, names); code.Add(op); }
                    break;
                }
                if (c.Arguments.Count < ops) throw new ScriptException($"{f}() needs {ops} values");
                foreach (var a in c.Arguments.Take(ops)) Emit(a, code, variable, names);
                code.Add(op);
                break;
            }
            default:
                throw new ScriptException("lists and grids aren't on the Apple IIgs");
        }
    }

    private static Node Parse(string formula)
    {
        var tokens = new List<(Tok, string)>();
        var braced = new HashSet<int>();
        int i = 0;
        while (i < formula.Length)
        {
            char c = formula[i];
            if (char.IsWhiteSpace(c)) { i++; continue; }
            if (char.IsDigit(c) || (c == '.' && i + 1 < formula.Length && char.IsDigit(formula[i + 1])))
            {
                int start = i;
                while (i < formula.Length && (char.IsDigit(formula[i]) || formula[i] == '.')) i++;
                tokens.Add((Tok.Number, formula[start..i]));
                continue;
            }
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (true)
                {
                    if (i >= formula.Length) throw new ScriptException("a text is missing its closing \"");
                    if (formula[i] == '"')
                    {
                        if (i + 1 < formula.Length && formula[i + 1] == '"') { sb.Append('"'); i += 2; continue; }
                        i++;
                        break;
                    }
                    sb.Append(formula[i++]);
                }
                tokens.Add((Tok.Text, sb.ToString()));
                continue;
            }
            if (c == '{')
            {
                int close = formula.IndexOf('}', i + 1);
                if (close < 0) throw new ScriptException("a name is missing its closing }");
                braced.Add(tokens.Count);
                tokens.Add((Tok.Name, formula[(i + 1)..close].Trim()));
                i = close + 1;
                continue;
            }
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < formula.Length && (char.IsLetterOrDigit(formula[i]) || formula[i] == '_')) i++;
                tokens.Add((Tok.Name, formula[start..i]));
                continue;
            }
            string two = i + 1 < formula.Length ? formula.Substring(i, 2) : string.Empty;
            if (two is "==" or "!=" or "<>" or "<=" or ">=" or "&&" or "||") { tokens.Add((Tok.Op, two)); i += 2; continue; }
            if ("+-*/%()[],=<>!".IndexOf(c) >= 0) { tokens.Add((Tok.Op, c.ToString())); i++; continue; }
            throw new ScriptException($"didn't expect \"{c}\" there");
        }
        tokens.Add((Tok.End, string.Empty));
        var parser = new Parser(tokens);
        foreach (int b in braced) parser._bracedNames.Add(b);
        return parser.ParseAll();
    }
}
