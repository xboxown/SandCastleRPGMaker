namespace SandcastleRpgMaker.Engine;

/// <summary>A Common Event (Tools > Common Events): a named list of commands made once, run from anywhere with Call Common Event.</summary>
public class CommonEventDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Common Event";
    /// <summary>The variables its parameters go into, in order (Call Common Event's values).</summary>
    public List<string> Parameters { get; set; } = new();
    public List<EventCommand> Commands { get; set; } = new();
}

/// <summary>A host that can carry on a long-running command list on its next frame, so a loop never freezes the game.</summary>
public interface IScriptYieldHost
{
    void Defer(Action goOn);
}

/// <summary>A host that can wait for a condition (Wait Until), checking it every frame.</summary>
public interface IScriptWaitHost
{
    /// <param name="timeoutSeconds">0 = for ever.</param>
    void WaitUntil(Func<bool> condition, double timeoutSeconds, Action goOn);
}

/// <summary>A host that has the project's Common Events.</summary>
public interface IScriptLibraryHost
{
    CommonEventDefinition? GetCommonEvent(int id);
}

/// <summary>The state of one running command list: where each loop is (by its opening line), and how deep the Common Event calls go.</summary>
public sealed class ScriptRunState
{
    public sealed class LoopState
    {
        public int Count, Total;
    }

    private readonly Dictionary<EventCommand, LoopState> _loops = new(ReferenceEqualityComparer.Instance);
    public int Depth { get; init; }
    /// <summary>The run that called this one (a Common Event's caller); null for an event's own run.</summary>
    public ScriptRunState? Parent { get; init; }
    /// <summary>The event's own run - the same for every Common Event it calls: what a character it moved belongs to till it's over.</summary>
    public ScriptRunState Root => Parent?.Root ?? this;

    public LoopState Loop(EventCommand opener)
    {
        if (!_loops.TryGetValue(opener, out var state)) _loops[opener] = state = new LoopState();
        return state;
    }

    public void Clear(EventCommand opener) => _loops.Remove(opener);
}

/// <summary>
/// Runs the script commands that only work on data (variables, texts,
/// arrays, grids, the debug log) - the same in the game and the editor's
/// preview. Everything that touches the world is the game's (GameHandlers).
/// </summary>
public static class ScriptRuntime
{
    /// <summary>True when the command was one of these (done, or failed with its error in the trace and the log).</summary>
    public static bool TryExecute(EventCommand cmd, IEventExecutionContext ctx, List<string> trace)
    {
        if (!Handles(cmd.Type)) return false;
        try
        {
            Execute(cmd, ctx);
            trace.Add($"Executed: {ScriptCommands.Of(cmd.Type)?.Name}");
        }
        catch (ScriptException ex)
        {
            string line = $"Script error in {ScriptCommands.Of(cmd.Type)?.Name}: {ex.Message}";
            trace.Add(line);
            ScriptExpression.DataOf(ctx).AddLog(line);
        }
        return true;
    }

    private static bool Handles(EventCommandType type) => type is EventCommandType.Calculate or EventCommandType.TextOperation or EventCommandType.SwapVariables
        or EventCommandType.CreateArray or EventCommandType.SetElement or EventCommandType.GetElement or EventCommandType.AddToArray or EventCommandType.InsertAt
        or EventCommandType.RemoveAt or EventCommandType.ArrayLength or EventCommandType.FindInArray or EventCommandType.ReorderArray or EventCommandType.FillArray
        or EventCommandType.ClearArray or EventCommandType.CopyArray or EventCommandType.ArrayStatistic
        or EventCommandType.CreateGrid or EventCommandType.SetCell or EventCommandType.GetCell or EventCommandType.FillGrid or EventCommandType.GridSize
        or EventCommandType.ResizeGrid or EventCommandType.FindInGrid or EventCommandType.GridLineToArray or EventCommandType.ArrayToGridLine
        or EventCommandType.DebugLog or EventCommandType.Comment;

    private static void Execute(EventCommand cmd, IEventExecutionContext ctx)
    {
        var data = ScriptExpression.DataOf(ctx);
        object E(string key) => ScriptExpression.Evaluate(cmd.Get(key), ctx);
        int I(string key) => (int)Math.Floor(ScriptValues.Number(E(key)));
        void Set(string key, object value) => ScriptExpression.SetVariable(ctx, cmd.Get(key), value);
        string Name(string key) => cmd.Get(key).Trim() is { Length: > 0 } n ? n : throw new ScriptException($"no {key.ToLowerInvariant()} name was given");

        switch (cmd.Type)
        {
            case EventCommandType.Calculate:
                Set("Variable", E("Formula"));
                break;

            case EventCommandType.TextOperation:
            {
                string a = ScriptValues.Text(E("A"));
                object b = E("B"), c = E("C");
                string bt = ScriptValues.Text(b);
                object result = cmd.Get("Operation") switch
                {
                    "Join" => a + bt,
                    "Length" => (double)a.Length,
                    "Part" => Part(a, (int)ScriptValues.Number(b), cmd.Get("C").Trim().Length == 0 ? int.MaxValue : (int)ScriptValues.Number(c)),
                    "Upper case" => a.ToUpperInvariant(),
                    "Lower case" => a.ToLowerInvariant(),
                    "Trim" => a.Trim(),
                    "Replace" => bt.Length == 0 ? a : a.Replace(bt, ScriptValues.Text(c), StringComparison.OrdinalIgnoreCase),
                    "Contains" => a.Contains(bt, StringComparison.OrdinalIgnoreCase) ? 1.0 : 0.0,
                    "Find" => (double)a.IndexOf(bt, StringComparison.OrdinalIgnoreCase),
                    "Number to text" => ScriptValues.Text(E("A")),
                    "Text to number" => ScriptValues.Number(a),
                    _ => a
                };
                // A text that reads as a number still stays a text when asked for one.
                if (cmd.Get("Operation") is "Join" or "Part" or "Upper case" or "Lower case" or "Trim" or "Replace" or "Number to text") data.Texts[Name("Variable")] = ScriptValues.Text(result);
                else Set("Variable", result);
                break;
            }

            case EventCommandType.SwapVariables:
            {
                object a = ScriptExpression.GetVariable(ctx, Name("A")), b = ScriptExpression.GetVariable(ctx, Name("B"));
                Set("A", b);
                Set("B", a);
                break;
            }

            // ---- Arrays ----
            case EventCommandType.CreateArray:
            {
                int size = Math.Clamp(I("Size"), 0, 100000);
                object fill = E("Fill");
                data.Arrays[Name("Array")] = Enumerable.Repeat(fill, size).ToList();
                break;
            }
            case EventCommandType.SetElement:
            {
                var list = data.ArrayOf(Name("Array"));
                int i = I("Index");
                if (i < 0 || i >= list.Count) throw new ScriptException($"{cmd.Get("Array")}[{i}] is outside the array (it has {list.Count})");
                list[i] = E("Value");
                break;
            }
            case EventCommandType.GetElement:
            {
                var list = data.ArrayOf(Name("Array"));
                int i = I("Index");
                if (i < 0 || i >= list.Count) throw new ScriptException($"{cmd.Get("Array")}[{i}] is outside the array (it has {list.Count})");
                Set("Variable", list[i]);
                break;
            }
            case EventCommandType.AddToArray:
                ArrayOrNew(data, Name("Array")).Add(E("Value"));
                break;
            case EventCommandType.InsertAt:
            {
                var list = ArrayOrNew(data, Name("Array"));
                list.Insert(Math.Clamp(I("Index"), 0, list.Count), E("Value"));
                break;
            }
            case EventCommandType.RemoveAt:
            {
                var list = data.ArrayOf(Name("Array"));
                if (cmd.Get("By") == "First equal to")
                {
                    object value = E("Index");
                    int at = list.FindIndex(e => ScriptValues.Same(e, value));
                    if (at >= 0) list.RemoveAt(at);
                }
                else
                {
                    int i = I("Index");
                    if (i >= 0 && i < list.Count) list.RemoveAt(i);
                }
                break;
            }
            case EventCommandType.ArrayLength:
                Set("Variable", (double)data.ArrayOf(Name("Array")).Count);
                break;
            case EventCommandType.FindInArray:
            {
                object value = E("Value");
                Set("Variable", (double)data.ArrayOf(Name("Array")).FindIndex(e => ScriptValues.Same(e, value)));
                break;
            }
            case EventCommandType.ReorderArray:
            {
                var list = data.ArrayOf(Name("Array"));
                switch (cmd.Get("Order"))
                {
                    case "Big to small": list.Sort((a, b) => ScriptValues.Compare(b, a)); break;
                    case "Shuffle":
                        var rng = new Random();
                        for (int n = list.Count - 1; n > 0; n--) { int k = rng.Next(n + 1); (list[n], list[k]) = (list[k], list[n]); }
                        break;
                    case "Reverse": list.Reverse(); break;
                    default: list.Sort(ScriptValues.Compare); break;
                }
                break;
            }
            case EventCommandType.FillArray:
            {
                var list = data.ArrayOf(Name("Array"));
                object value = E("Value");
                for (int n = 0; n < list.Count; n++) list[n] = value;
                break;
            }
            case EventCommandType.ClearArray:
                ArrayOrNew(data, Name("Array")).Clear();
                break;
            case EventCommandType.CopyArray:
                data.Arrays[Name("Array")] = new List<object>(data.ArrayOf(Name("From")));
                break;
            case EventCommandType.ArrayStatistic:
            {
                var numbers = data.ArrayOf(Name("Array")).Select(ScriptValues.Number).ToList();
                double result = cmd.Get("What") switch
                {
                    "Smallest" => numbers.Count == 0 ? 0 : numbers.Min(),
                    "Biggest" => numbers.Count == 0 ? 0 : numbers.Max(),
                    "Average" => numbers.Count == 0 ? 0 : numbers.Average(),
                    _ => numbers.Sum()
                };
                Set("Variable", result);
                break;
            }

            // ---- Grids ----
            case EventCommandType.CreateGrid:
                data.Grids[Name("Grid")] = new ScriptGrid(Math.Clamp(I("Columns"), 0, 2000), Math.Clamp(I("Rows"), 0, 2000), E("Fill"));
                break;
            case EventCommandType.SetCell:
                data.GridOf(Name("Grid"))[I("X"), I("Y")] = E("Value");
                break;
            case EventCommandType.GetCell:
                Set("Variable", data.GridOf(Name("Grid"))[I("X"), I("Y")]);
                break;
            case EventCommandType.FillGrid:
            {
                var grid = data.GridOf(Name("Grid"));
                object value = E("Value");
                if (cmd.Get("Area") == "True")
                {
                    int x0 = I("X"), y0 = I("Y"), w = I("Width"), h = I("Height");
                    for (int y = Math.Max(0, y0); y < Math.Min(grid.Rows, y0 + h); y++)
                        for (int x = Math.Max(0, x0); x < Math.Min(grid.Columns, x0 + w); x++)
                            grid[x, y] = value;
                }
                else Array.Fill(grid.Cells, value);
                break;
            }
            case EventCommandType.GridSize:
            {
                var grid = data.GridOf(Name("Grid"));
                Set("Variable", (double)grid.Columns);
                if (cmd.Get("Variable2").Length > 0) Set("Variable2", (double)grid.Rows);
                break;
            }
            case EventCommandType.ResizeGrid:
                data.GridOf(Name("Grid")).Resize(Math.Clamp(I("Columns"), 0, 2000), Math.Clamp(I("Rows"), 0, 2000), E("Fill"));
                break;
            case EventCommandType.FindInGrid:
            {
                var grid = data.GridOf(Name("Grid"));
                object value = E("Value");
                int at = Array.FindIndex(grid.Cells, e => ScriptValues.Same(e, value));
                Set("Variable", at < 0 ? -1.0 : at % Math.Max(1, grid.Columns));
                if (cmd.Get("Variable2").Length > 0) Set("Variable2", at < 0 ? -1.0 : at / Math.Max(1, grid.Columns));
                break;
            }
            case EventCommandType.GridLineToArray:
            {
                var grid = data.GridOf(Name("Grid"));
                int line = I("Index");
                bool row = cmd.Get("Line") != "Column";
                var list = new List<object>();
                if (row) for (int x = 0; x < grid.Columns; x++) list.Add(grid[x, line]);
                else for (int y = 0; y < grid.Rows; y++) list.Add(grid[line, y]);
                data.Arrays[Name("Array")] = list;
                break;
            }
            case EventCommandType.ArrayToGridLine:
            {
                var grid = data.GridOf(Name("Grid"));
                var list = data.ArrayOf(Name("Array"));
                int line = I("Index");
                bool row = cmd.Get("Line") != "Column";
                for (int n = 0; n < list.Count && n < (row ? grid.Columns : grid.Rows); n++)
                {
                    if (row) grid[n, line] = list[n];
                    else grid[line, n] = list[n];
                }
                break;
            }

            case EventCommandType.DebugLog:
                data.AddLog(ScriptValues.Text(E("Value")));
                break;
            case EventCommandType.Comment:
                break;
        }
    }

    private static List<object> ArrayOrNew(ScriptData data, string name)
    {
        if (!data.Arrays.TryGetValue(name, out var list)) data.Arrays[name] = list = new List<object>();
        return list;
    }

    private static string Part(string text, int start, int count)
    {
        start = Math.Clamp(start, 0, text.Length);
        count = Math.Clamp(count, 0, text.Length - start);
        return text.Substring(start, count);
    }

    // ---------- Conditions and lists of values ----------

    /// <summary>A While / Wait Until condition: its formula, or its game condition (Advance IF).</summary>
    public static bool Condition(EventCommand cmd, IEventExecutionContext ctx)
    {
        if (cmd.Get("Mode") == "Game")
            return ctx is IGameConditionContext game && GameConditions.FromCommand(cmd) is { } condition && game.EvaluateGameCondition(condition);
        return ScriptExpression.IsTrue(cmd.Get("Formula"), ctx);
    }

    /// <summary>"1, 2, max(a, b)" -> its three formulas (commas inside brackets and texts don't split).</summary>
    public static List<string> SplitList(string text)
    {
        var parts = new List<string>();
        int depth = 0, start = 0;
        bool inText = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '"') inText = !inText;
            else if (inText) continue;
            else if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if ((c == ',' || c == '\n') && depth == 0)
            {
                if (text[start..i].Trim() is { Length: > 0 } p) parts.Add(p);
                start = i + 1;
            }
        }
        if (text[start..].Trim() is { Length: > 0 } last) parts.Add(last);
        return parts;
    }
}
