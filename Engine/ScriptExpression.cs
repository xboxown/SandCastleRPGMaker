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
        public Unary(string op, Node a) { _op = op; _a = a; }
        public override object Eval(Env env) => _op == "-" ? -ScriptValues.Number(_a.Eval(env)) : (ScriptValues.Truth(_a.Eval(env)) ? 0.0 : 1.0);
    }

    private sealed class Binary : Node
    {
        private readonly string _op; private readonly Node _a, _b;
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
