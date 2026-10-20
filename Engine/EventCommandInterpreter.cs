namespace SandcastleRpgMaker.Engine;

/// <summary>
/// What a host (Book Editor's Preview today; the actual game engine later)
/// provides so EventCommandInterpreter can produce real effects without
/// knowing anything about WPF, NAudio, or how any particular editor stores
/// its data. Execute returns true when the command actually did something
/// real, false when it was merely acknowledged/logged - that distinction is
/// what lets Preview show "Executed: Play Sound" next to "(not implemented
/// yet: Give Item)" instead of pretending everything already works.
/// </summary>
public interface IEventExecutionContext
{
    bool Execute(EventCommand command);
    bool GetSwitch(string name);
    void SetSwitch(string name, bool value);
    double GetVariable(string name);
    void SetVariable(string name, double value);
    /// <summary>Whether a runtime content-override actually got stored for this specific command (Change Self/External Book Content) - lets the Debug window show real, verified confirmation instead of just trusting Execute's return value. Any command type other than those two should just return false.</summary>
    bool HasContentOverrideFor(EventCommand command);
}

/// <summary>
/// A host whose characters can carry out commands that take time (walk
/// somewhere, sit, pick a lock). The interpreter hands it the command and
/// stops; <c>done</c> is called - with whether it worked - once it has
/// finished or been cancelled, and the commands go on from the next one.
/// </summary>
public interface IActionCommandHost
{
    /// <summary>False: not a timed command here (run it as usual). True: started (or failed at once) - <paramref name="done"/> is called exactly once.</summary>
    bool TryBegin(EventCommand command, Action<bool> done);
}

/// <summary>
/// A host that can take the controls away from the player for a while
/// (Cinema Mode). Each running command list is its own key: the controls
/// come back when every list that took them has ended its cinema.
/// </summary>
public interface ICinemaHost
{
    void BeginCinema(object run);
    void EndCinema(object run);
}

/// <summary>Told when an event's run is over (its last line, Exit Event Processing, or stopped) - the characters it moved go back to their schedules then.</summary>
public interface IEventRunHost
{
    void RunEnded(object run);
}

/// <summary>
/// Walks one command list for real. ConditionalBranchSwitch/Variable,
/// Else, EndBranch, and End are genuinely interpreted here (not stubbed) -
/// this is the same loop the actual game engine will eventually run
/// verbatim, since IEventExecutionContext is exactly the seam a real
/// running game plugs into instead of a preview log. GoToLabel/Label are
/// deliberately left as acknowledged-but-not-implemented for now (logged,
/// not executed) - real jump support is a follow-up, not part of getting
/// the foundation live.
/// </summary>
public static class EventCommandInterpreter
{
    /// <summary>With no host to carry on next frame (the editor's preview): a list that runs this many steps at once is stopped (a loop that never ends).</summary>
    private const int MaxSteps = 100000;
    /// <summary>In the game: after this many steps at once the list carries on next frame (IScriptYieldHost), so a long loop never freezes the game.</summary>
    private const int StepsPerFrame = 2000;
    /// <summary>Common Events calling Common Events: deeper than this is a mistake (one calling itself for ever).</summary>
    private const int MaxCallDepth = 40;

    /// <param name="start">Where to begin - a reply's branch, when the player has just picked it.</param>
    /// <param name="returns">Pinned replies being answered: reaching StopAt (the end of that reply's branch) goes back to ResumeAt (its Show Text). Innermost last.</param>
    /// <param name="state">Where its loops are (kept across waits); a fresh one when null.</param>
    /// <param name="finished">Called once the list has ended (its last line, Exit Event Processing or Return) - how a Common Event goes back to its caller.</param>
    public static void Run(List<EventCommand> commands, IEventExecutionContext context, List<string> trace, int start = 0, IReadOnlyList<(int StopAt, int ResumeAt)>? returns = null,
        ScriptRunState? state = null, Action? finished = null)
    {
        // Each Common Event call (and each command finishing at once) runs on inside this one: past a depth, carry on next frame instead.
        if (_nesting > MaxNesting && context is IScriptYieldHost deferHost)
        {
            deferHost.Defer(() => Run(commands, context, trace, start, returns, state, finished));
            return;
        }
        _nesting++;
        try { RunCore(commands, context, trace, start, returns, state, finished); }
        finally { _nesting--; }
    }

    [ThreadStatic] private static int _nesting;

    /// <summary>The event run whose command is starting right now (its Root - see ScriptRunState): who began a command that takes time.</summary>
    [ThreadStatic] private static object? _currentRun;
    public static object? CurrentRun => _currentRun;

    private static void RunEnded(IEventExecutionContext context, ScriptRunState state)
    {
        if (state.Parent is null) (context as IEventRunHost)?.RunEnded(state);
    }
    private const int MaxNesting = 150;

    private static void RunCore(List<EventCommand> commands, IEventExecutionContext context, List<string> trace, int start, IReadOnlyList<(int StopAt, int ResumeAt)>? returns,
        ScriptRunState? state, Action? finished)
    {
        int i = start;
        returns ??= Array.Empty<(int, int)>();
        state ??= new ScriptRunState();
        int guard = 0;

        while (i < commands.Count)
        {
            if (++guard > StepsPerFrame && context is IScriptYieldHost yieldHost)
            {
                int resumeAt = i;
                var returnsThen = returns;
                yieldHost.Defer(() => Run(commands, context, trace, resumeAt, returnsThen, state, finished));
                return;
            }
            if (guard > MaxSteps)
            {
                trace.Add("(stopped - exceeded max command steps, likely a runaway loop)");
                (context as ICinemaHost)?.EndCinema(commands);
                RunEnded(context, state);
                return;
            }

            // The end of a pinned reply's branch: back to its Show Text.
            int pin = LastIndexOf(returns, i);
            if (pin >= 0)
            {
                trace.Add("End of a pinned reply - back to its box");
                i = returns[pin].ResumeAt;
                returns = returns.Take(pin).ToList();
                continue;
            }

            var cmd = commands[i];
            switch (cmd.Type)
            {
                case EventCommandType.End:
                case EventCommandType.ReturnFromCommon:
                    if (cmd.Type == EventCommandType.ReturnFromCommon && cmd.Get("Value").Trim().Length > 0)
                    {
                        try { ScriptExpression.SetVariable(context, "Result", ScriptExpression.Evaluate(cmd.Get("Value"), context)); }
                        catch (ScriptException ex) { ScriptError(context, trace, "Return", ex); }
                    }
                    trace.Add(cmd.Type == EventCommandType.End ? "End - command list stopped." : "Return");
                    (context as ICinemaHost)?.EndCinema(commands); // the event is over: a cinema still on ends with it
                    RunEnded(context, state);
                    finished?.Invoke();
                    return;

                // ---- Loops (ScriptCommands): an opener, its End, Break Loop / Next Loop inside ----
                case EventCommandType.While:
                {
                    bool go = SafeCondition(cmd, context, trace);
                    trace.Add($"While -> {(go ? "true" : "false")}");
                    i = go ? i + 1 : FindCloser(commands, i) + 1;
                    continue;
                }
                case EventCommandType.EndWhile:
                    i = FindOpener(commands, i); // back to its While, which checks again
                    if (i < 0) i = commands.Count;
                    continue;

                case EventCommandType.RepeatTimes:
                {
                    int total = (int)Math.Floor(SafeNumber(cmd.Get("Count"), context, trace));
                    var loop = state.Loop(cmd);
                    loop.Count = 1;
                    loop.Total = total;
                    if (total < 1) { state.Clear(cmd); i = FindCloser(commands, i) + 1; continue; }
                    SafeSet(context, trace, cmd.Get("Variable"), 1.0);
                    i++;
                    continue;
                }
                case EventCommandType.EndRepeat:
                {
                    int open = FindOpener(commands, i);
                    if (open < 0) { i++; continue; }
                    var loop = state.Loop(commands[open]);
                    if (++loop.Count <= loop.Total) { SafeSet(context, trace, commands[open].Get("Variable"), (double)loop.Count); i = open + 1; }
                    else { state.Clear(commands[open]); i++; }
                    continue;
                }

                case EventCommandType.ForEach:
                {
                    var loop = state.Loop(cmd);
                    loop.Count = 0;
                    if (ForEachStep(cmd, loop, context, trace)) i++;
                    else { state.Clear(cmd); i = FindCloser(commands, i) + 1; }
                    continue;
                }
                case EventCommandType.EndForEach:
                {
                    int open = FindOpener(commands, i);
                    if (open < 0) { i++; continue; }
                    var loop = state.Loop(commands[open]);
                    loop.Count++;
                    if (ForEachStep(commands[open], loop, context, trace)) i = open + 1;
                    else { state.Clear(commands[open]); i++; }
                    continue;
                }

                case EventCommandType.BreakLoop:
                {
                    int open = EnclosingLoop(commands, i);
                    if (open < 0) { trace.Add("Break Loop - not inside a loop"); i++; continue; }
                    state.Clear(commands[open]);
                    i = FindCloser(commands, open) + 1;
                    continue;
                }
                case EventCommandType.NextLoop:
                {
                    int open = EnclosingLoop(commands, i);
                    i = open < 0 ? i + 1 : FindCloser(commands, open); // its End moves the loop on
                    continue;
                }

                // ---- Select Case ----
                case EventCommandType.SelectCase:
                {
                    object value = SafeValue(cmd.Get("Formula"), context, trace);
                    int indent = cmd.IndentLevel, target = -1, otherwise = -1, end = commands.Count - 1;
                    for (int j = i + 1; j < commands.Count; j++)
                    {
                        if (commands[j].IndentLevel != indent) continue;
                        var t = commands[j].Type;
                        if (t == EventCommandType.EndSelect) { end = j; break; }
                        if (t == EventCommandType.CaseElse && otherwise < 0) otherwise = j + 1;
                        if (t == EventCommandType.Case && target < 0 && ScriptRuntime.SplitList(commands[j].Get("Formula")).Any(f => ScriptValues.Same(SafeValue(f, context, trace), value))) target = j + 1;
                    }
                    trace.Add($"Select Case {ScriptValues.Text(value)}");
                    i = target >= 0 ? target : otherwise >= 0 ? otherwise : end + 1;
                    continue;
                }
                case EventCommandType.Case:
                case EventCommandType.CaseElse:
                {
                    // The end of the Case that ran: on past End Select.
                    int indent = cmd.IndentLevel;
                    int end = commands.FindIndex(i + 1, c => c.IndentLevel == indent && c.Type == EventCommandType.EndSelect);
                    i = end < 0 ? commands.Count : end + 1;
                    continue;
                }
                case EventCommandType.EndSelect:
                case EventCommandType.Comment:
                    i++;
                    continue;

                case EventCommandType.WaitUntil:
                {
                    if (context is not IScriptWaitHost waitHost) { trace.Add("Wait Until - nothing to wait in here, going on"); i++; continue; }
                    if (SafeCondition(cmd, context, trace)) { i++; continue; }
                    int next = i + 1;
                    var returnsThen = returns;
                    double.TryParse(cmd.Get("Timeout"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double timeout);
                    trace.Add("Wait Until - waiting");
                    waitHost.WaitUntil(() => SafeCondition(cmd, context, trace), Math.Max(0, timeout), () => Run(commands, context, trace, next, returnsThen, state, finished));
                    return;
                }

                case EventCommandType.CallCommonEvent:
                {
                    if (context is not IScriptLibraryHost library || !int.TryParse(cmd.Get("CommonEventId"), out int id) || library.GetCommonEvent(id) is not { } common)
                    {
                        trace.Add("Call Common Event - it isn't there any more, going on");
                        i++;
                        continue;
                    }
                    if (state.Depth >= MaxCallDepth)
                    {
                        ScriptError(context, trace, "Call Common Event", new ScriptException($"\"{common.Name}\" calls Common Events more than {MaxCallDepth} deep (one calling itself?)"));
                        i++;
                        continue;
                    }
                    var values = ScriptRuntime.SplitList(cmd.Get("Args")).Select(f => SafeValue(f, context, trace)).ToList();
                    for (int p = 0; p < common.Parameters.Count && p < values.Count; p++) SafeSet(context, trace, common.Parameters[p], values[p]);
                    int next = i + 1;
                    var returnsThen = returns;
                    trace.Add($"Call Common Event \"{common.Name}\"");
                    Run(common.Commands, context, trace, 0, null, new ScriptRunState { Depth = state.Depth + 1, Parent = state }, () => Run(commands, context, trace, next, returnsThen, state, finished));
                    return;
                }

                case EventCommandType.CinemaMode:
                    (context as ICinemaHost)?.BeginCinema(commands);
                    trace.Add("Cinema Mode - the player can't move or click till End Cinema Mode");
                    i++;
                    continue;

                case EventCommandType.EndCinemaMode:
                    (context as ICinemaHost)?.EndCinema(commands);
                    trace.Add("End Cinema Mode - the player has control again");
                    i++;
                    continue;

                case EventCommandType.ConditionalBranchSwitch:
                case EventCommandType.ConditionalBranchVariable:
                case EventCommandType.AdvancedIf:
                {
                    bool branchTrue = EvaluateBranch(cmd, context);
                    trace.Add($"{cmd.Type} -> {(branchTrue ? "true" : "false")}");
                    i = branchTrue ? i + 1 : FindElseOrEndBranch(commands, i) + 1;
                    continue;
                }

                case EventCommandType.Else:
                    // Reached naturally while running a TRUE branch's own
                    // commands - its contents must not also run, so skip
                    // straight past the matching EndBranch.
                    i = FindEndBranch(commands, i) + 1;
                    continue;

                case EventCommandType.EndBranch:
                    i++;
                    continue;

                case EventCommandType.Label:
                    i++;
                    continue;

                case EventCommandType.GoToLabel:
                {
                    string name = cmd.Get("LabelName");
                    int target = commands.FindIndex(c => c.Type == EventCommandType.Label && string.Equals(c.Get("LabelName"), name, StringComparison.OrdinalIgnoreCase));
                    trace.Add(target >= 0 ? $"Jump to Label \"{name}\"" : $"Jump to Label \"{name}\" - no such label, going on");
                    i = target >= 0 ? target + 1 : i + 1;
                    continue;
                }

                case EventCommandType.ShowText when DialogChoices.HasChoices(cmd):
                {
                    var choices = DialogChoices.Read(cmd);
                    int at = i;
                    int end = DialogChoices.FindEndChoices(commands, at);
                    int afterBranches = end >= 0 ? end + 1 : at + 1;
                    if (context is IDialogChoiceHost { CanAskChoice: true } host)
                    {
                        // The game: the box goes up with its replies and everything waits for the player's pick.
                        var returnsHere = returns;
                        bool waiting = host.AskChoice(cmd, choices, id =>
                        {
                            int when = DialogChoices.FindWhen(commands, at, id);
                            if (when < 0) { Run(commands, context, trace, afterBranches, returnsHere, state, finished); return; }
                            var next = returnsHere;
                            if (choices.FirstOrDefault(c => c.Id == id) is { Pinned: true })
                                next = returnsHere.Append((DialogChoices.FindBranchEnd(commands, when), at)).ToList();
                            Run(commands, context, trace, when + 1, next, state, finished);
                        });
                        if (waiting)
                        {
                            trace.Add($"Show Text with {choices.Count} repl{(choices.Count == 1 ? "y" : "ies")} - waiting for the player's pick");
                            return;
                        }
                        i = afterBranches;
                        continue;
                    }
                    // A preview with nobody to pick: show the text and take the first reply.
                    bool shown = context.Execute(cmd);
                    trace.Add(shown ? $"Executed: {Describe(cmd)}" : $"(not implemented in preview yet: {Describe(cmd)})");
                    int first = choices.Count > 0 ? DialogChoices.FindWhen(commands, at, choices[0].Id) : -1;
                    if (first >= 0) trace.Add($"(preview takes the first reply: \"{choices[0].Text}\")");
                    i = first >= 0 ? first + 1 : afterBranches;
                    continue;
                }

                case EventCommandType.WhenChoice:
                    // The end of the picked reply's branch: skip the other replies' branches.
                    i = DialogChoices.FindEndChoicesFrom(commands, i) + 1;
                    continue;

                case EventCommandType.EndChoices:
                    i++;
                    continue;

                case EventCommandType.StopConversation:
                {
                    // No pinned reply brings its box back after this.
                    returns = Array.Empty<(int, int)>();
                    if (context is IDialogChoiceHost { CanAskChoice: true } waiter)
                    {
                        int next = i + 1;
                        if (waiter.WaitUntilRead(() => Run(commands, context, trace, next, null, state, finished)))
                        {
                            trace.Add("Stop Conversation - the box closes once its lines are read");
                            return;
                        }
                    }
                    trace.Add("Stop Conversation");
                    i++;
                    continue;
                }

                default:
                {
                    // Variables, texts, arrays, grids, the debug log: the same everywhere (ScriptRuntime).
                    if (ScriptRuntime.TryExecute(cmd, context, trace)) { i++; continue; }
                    if (context is IActionCommandHost actions)
                    {
                        // A command that takes time: everything after it waits till it's done.
                        int next = i + 1;
                        var returnsHere = returns;
                        bool ranOn = false;
                        var outerRun = _currentRun;
                        _currentRun = state.Root;
                        bool began;
                        try
                        {
                            began = actions.TryBegin(cmd, ok =>
                            {
                                if (ranOn) return;
                                ranOn = true;
                                trace.Add($"{(ok ? "Done" : "Cancelled")}: {Describe(cmd)}");
                                Run(commands, context, trace, next, returnsHere, state, finished);
                            });
                        }
                        finally { _currentRun = outerRun; }
                        if (began)
                        {
                            if (!ranOn) trace.Add($"Started: {Describe(cmd)} - waiting till it's done");
                            return;
                        }
                    }
                    bool didReal = context.Execute(cmd);
                    trace.Add(didReal ? $"Executed: {Describe(cmd)}" : $"(not implemented in preview yet: {Describe(cmd)})");
                    i++;
                    continue;
                }
            }
        }
        (context as ICinemaHost)?.EndCinema(commands); // the last line done: a cinema still on ends with the event
        RunEnded(context, state);
        finished?.Invoke();
    }

    // ---------- Script helpers ----------

    /// <summary>An opener's End line (same indent, after it); the list's end when it's missing.</summary>
    private static int FindCloser(List<EventCommand> commands, int openIndex)
    {
        var closer = ScriptCommands.CloserOf(commands[openIndex].Type);
        int indent = commands[openIndex].IndentLevel;
        for (int j = openIndex + 1; j < commands.Count; j++)
            if (commands[j].IndentLevel == indent && commands[j].Type == closer) return j;
        return commands.Count - 1;
    }

    /// <summary>An End line's opener (same indent, before it), or -1.</summary>
    private static int FindOpener(List<EventCommand> commands, int closeIndex)
    {
        var opener = ScriptCommands.OpenerOf(commands[closeIndex].Type);
        int indent = commands[closeIndex].IndentLevel;
        for (int j = closeIndex - 1; j >= 0; j--)
            if (commands[j].IndentLevel == indent && commands[j].Type == opener) return j;
        return -1;
    }

    /// <summary>The loop a line is inside (the nearest While / Repeat / For Each whose End is after it), or -1.</summary>
    private static int EnclosingLoop(List<EventCommand> commands, int index)
    {
        int indent = commands[index].IndentLevel;
        for (int j = index - 1; j >= 0; j--)
        {
            if (commands[j].IndentLevel >= indent || !ScriptCommands.IsLoopOpener(commands[j].Type)) continue;
            if (FindCloser(commands, j) > index) return j;
            indent = commands[j].IndentLevel;
        }
        return -1;
    }

    /// <summary>For Each: the element / cell number loop.Count into its variables; false once past the last.</summary>
    private static bool ForEachStep(EventCommand cmd, ScriptRunState.LoopState loop, IEventExecutionContext context, List<string> trace)
    {
        var data = ScriptExpression.DataOf(context);
        string name = cmd.Get("Array");
        if (cmd.Get("Of") == "Cell of a grid")
        {
            if (!data.Grids.TryGetValue(name, out var grid)) { ScriptError(context, trace, "For Each", new ScriptException($"there is no grid called \"{name}\"")); return false; }
            if (loop.Count >= grid.Cells.Length) return false;
            int x = loop.Count % grid.Columns, y = loop.Count / grid.Columns;
            SafeSet(context, trace, cmd.Get("Variable"), grid.Cells[loop.Count]);
            SafeSet(context, trace, cmd.Get("Index"), (double)x);
            SafeSet(context, trace, cmd.Get("Index2"), (double)y);
            return true;
        }
        if (!data.Arrays.TryGetValue(name, out var list)) { ScriptError(context, trace, "For Each", new ScriptException($"there is no array called \"{name}\"")); return false; }
        if (loop.Count >= list.Count) return false;
        SafeSet(context, trace, cmd.Get("Variable"), list[loop.Count]);
        SafeSet(context, trace, cmd.Get("Index"), (double)loop.Count);
        return true;
    }

    private static bool SafeCondition(EventCommand cmd, IEventExecutionContext context, List<string> trace)
    {
        try { return ScriptRuntime.Condition(cmd, context); }
        catch (ScriptException ex) { ScriptError(context, trace, ScriptCommands.Of(cmd.Type)?.Name ?? "Condition", ex); return false; }
    }

    private static object SafeValue(string formula, IEventExecutionContext context, List<string> trace)
    {
        try { return ScriptExpression.Evaluate(formula, context); }
        catch (ScriptException ex) { ScriptError(context, trace, "Formula", ex); return 0.0; }
    }

    private static double SafeNumber(string formula, IEventExecutionContext context, List<string> trace) => ScriptValues.Number(SafeValue(formula, context, trace));

    /// <summary>Sets a variable if a name was given (an optional counter / index left empty is skipped).</summary>
    private static void SafeSet(IEventExecutionContext context, List<string> trace, string name, object value)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        try { ScriptExpression.SetVariable(context, name, value); }
        catch (ScriptException ex) { ScriptError(context, trace, "Variable", ex); }
    }

    private static void ScriptError(IEventExecutionContext context, List<string> trace, string where, ScriptException ex)
    {
        string line = $"Script error in {where}: {ex.Message}";
        trace.Add(line);
        ScriptExpression.DataOf(context).AddLog(line);
    }

    private static int LastIndexOf(IReadOnlyList<(int StopAt, int ResumeAt)> returns, int index)
    {
        for (int r = returns.Count - 1; r >= 0; r--)
            if (returns[r].StopAt == index) return r;
        return -1;
    }

    internal static bool EvaluateBranch(EventCommand cmd, IEventExecutionContext context)
    {
        if (cmd.Type == EventCommandType.AdvancedIf)
            return context is IGameConditionContext game && GameConditions.FromCommand(cmd) is { } condition && game.EvaluateGameCondition(condition);
        if (cmd.Type == EventCommandType.ConditionalBranchSwitch)
        {
            bool actual = context.GetSwitch(cmd.Get("SwitchName"));
            bool.TryParse(cmd.Get("Value"), out bool expected);
            return actual == expected;
        }

        double actualValue = context.GetVariable(cmd.Get("VariableName"));
        double.TryParse(cmd.Get("Value"), out double target);
        return cmd.Get("Comparison").Trim() switch
        {
            "==" => actualValue == target,
            ">" => actualValue > target,
            "<" => actualValue < target,
            ">=" => actualValue >= target,
            "<=" => actualValue <= target,
            "!=" => actualValue != target,
            _ => actualValue == target
        };
    }

    /// <summary>The false-branch's landing spot: the matching Else at this same indent if one exists before the matching EndBranch, otherwise the EndBranch itself (so the caller's +1 skips past it with no Else content to run).</summary>
    internal static int FindElseOrEndBranch(List<EventCommand> commands, int branchIndex)
    {
        int indent = commands[branchIndex].IndentLevel;
        for (int j = branchIndex + 1; j < commands.Count; j++)
        {
            if (commands[j].IndentLevel != indent) continue;
            if (commands[j].Type is EventCommandType.Else or EventCommandType.EndBranch) return j;
        }
        return commands.Count - 1;
    }

    internal static int FindEndBranch(List<EventCommand> commands, int fromIndex)
    {
        int indent = commands[fromIndex].IndentLevel;
        for (int j = fromIndex + 1; j < commands.Count; j++)
        {
            if (commands[j].IndentLevel == indent && commands[j].Type == EventCommandType.EndBranch) return j;
        }
        return commands.Count - 1;
    }

    private static string Describe(EventCommand cmd) => cmd.Type switch
    {
        EventCommandType.PlaySound => $"Play Sound (resource #{cmd.Get("SoundResourceId")}{(cmd.Get("Loop") == "True" ? ", looping" : "")})",
        EventCommandType.OpenPaperdoll => $"Open Paperdoll ({cmd.Get("DesignName")}{(cmd.Get("Modal") == "True" ? ", modal" : "")}{(cmd.Get("Wait") == "True" ? ", waits" : "")})",
        EventCommandType.PlayVideo => $"Play Video (video #{cmd.Get("VideoId")}{(cmd.Get("Skippable") == "False" ? ", can't be skipped" : "")})",
        EventCommandType.StopSound => $"Stop Sound ({(cmd.Get("SoundResourceId") == "All" ? "every sound" : "resource #" + cmd.Get("SoundResourceId"))})",
        EventCommandType.PlayMusic => $"Play Music (resource #{cmd.Get("MusicResourceId")})",
        EventCommandType.ShowText => $"Show Text: \"{cmd.Get("Text")}\"",
        EventCommandType.SetSwitch => $"Control Switches: {cmd.Get("SwitchName")} = {cmd.Get("Value")}",
        EventCommandType.SetVariable => $"Control Variables: {cmd.Get("VariableName")} {cmd.Get("Operation")} {cmd.Get("Value")}",
        EventCommandType.ShakeScreen => $"Shake Screen: Intensity {cmd.Get("Intensity")}, {cmd.Get("DurationSeconds")}s",
        EventCommandType.ChangeDayNight => $"Change Day/Night: {cmd.Get("Period")}",
        EventCommandType.ApplyPaletteColor => $"Apply Palette Color: {cmd.Get("PaletteGroup")} #{cmd.Get("ColorIndex")} (#{cmd.Get("Hex")})",
        EventCommandType.ChangeSelfBookContent => $"Change Self Book Content: page {cmd.Get("PageIndex")}, frame {cmd.Get("FrameId")}",
        EventCommandType.ChangeExternalBookContent => $"Change External Book Content: book #{cmd.Get("TargetBookId")}, page {cmd.Get("PageIndex")}, frame {cmd.Get("FrameId")}",
        EventCommandType.ChangeBookSettings => $"Change Book Settings (runtime-only): book #{cmd.Get("TargetBookId")}",
        EventCommandType.ChangeFactionRelationship => $"Change Faction Relationship: faction #{cmd.Get("ViewerFactionId")} sees #{cmd.Get("TargetFactionId")} {cmd.Get("Operation")} {cmd.Get("Value")}%{(cmd.Get("BothWays") == "True" ? " (both ways)" : "")}",
        EventCommandType.Teleport => $"Teleport: {cmd.Get("Who")} to ({cmd.Get("X")}, {cmd.Get("Y")})",
        EventCommandType.ChangeBehavior => $"Change Behavior: {cmd.Get("CharacterId")} -> {cmd.Get("Behavior")}",
        EventCommandType.ChangeFoodLevel => $"Food Level: {cmd.Get("Who")} {cmd.Get("Operation")} {cmd.Get("Value")}",
        EventCommandType.ModifyAilments => $"Modify Ailments: {cmd.Get("Action")} {cmd.Get("Ailment")} on {cmd.Get("Who")}",
        EventCommandType.Attack => $"Attack: {cmd.Get("Attacker")} attacks {cmd.Get("Target")}",
        EventCommandType.AdvancedIf => $"Advance IF: {cmd.Get("Summary")}",
        EventCommandType.ChangeWeather => $"Change Weather: {cmd.Get("Kind")} {cmd.Get("Intensity")}%",
        EventCommandType.ExchangeCurrency => $"Exchange Currency: {cmd.Get("Amount")} {cmd.Get("FromName")} into {cmd.Get("ToName")}",
        EventCommandType.SleepOnBed => $"Sleep On Bed: {cmd.Get("Which")}",
        EventCommandType.Paperdoll => $"Paperdoll: {cmd.Get("Action")} {cmd.Get("PartName")}",
        EventCommandType.ChangeFaction => $"Change Faction: character {cmd.Get("CharacterId")} joins faction #{cmd.Get("FactionId")}",
        EventCommandType.GiveItem => $"Give Item: {cmd.Get("Quantity")} of {cmd.Get("Category")} #{cmd.Get("ItemId")} from {cmd.Get("Giver")} to {cmd.Get("Receiver")}",
        EventCommandType.ChangeStat => $"Change Stat: {cmd.Get("Who")} stat #{cmd.Get("StatId")} {cmd.Get("Operation")} {cmd.Get("Value")}{(cmd.Get("Temporary") == "True" ? $" for {cmd.Get("Duration")} {cmd.Get("Unit")}" : "")}",
        EventCommandType.ResetStat => $"Reset Stat: {cmd.Get("Who")} stat {cmd.Get("StatId")}",
        EventCommandType.Train => $"Train: {cmd.Get("Trainee")} for {cmd.Get("Points")} training points, {cmd.Get("Price")} money",
        EventCommandType.KillCharacter => $"Kill Character: {cmd.Get("Who")}",
        EventCommandType.PickupItem => $"Pickup Item: {cmd.Get("Who")} picks up {cmd.Get("Thing")}",
        EventCommandType.DropItem => $"Drop Item: {cmd.Get("Who")} drops {cmd.Get("Category")} #{cmd.Get("ItemId")}",
        EventCommandType.EquipItem => $"Equip: {cmd.Get("Who")} readies {cmd.Get("Category")} #{cmd.Get("ItemId")}",
        EventCommandType.UnequipItem => $"Unequip: {cmd.Get("Who")} takes off {cmd.Get("Category")} #{cmd.Get("ItemId")}",
        EventCommandType.PlayPose => $"Play Pose: {cmd.Get("Who")} {cmd.Get("Pose")}",
        EventCommandType.WalkToLocation => $"Walk To Location: {cmd.Get("Who")} to ({cmd.Get("X")}, {cmd.Get("Y")})",
        EventCommandType.WalkToFloorLocation => $"Walk To Floor Location: {cmd.Get("Who")} to floor {cmd.Get("Floor")} ({cmd.Get("X")}, {cmd.Get("Y")})",
        EventCommandType.Wait => $"Wait: {(cmd.Get("Duration") is { Length: > 0 } d ? d : cmd.Get("Seconds"))} {(cmd.Get("Unit") is { Length: > 0 } u ? u.ToLowerInvariant() : "seconds")}",
        EventCommandType.ChangePose => $"Change Pose: {cmd.Get("Who")} {cmd.Get("Pose")}",
        EventCommandType.AttackObject => $"Attack Object: {cmd.Get("Who")} attacks {cmd.Get("Thing")}",
        EventCommandType.PlayAnimation => $"Play Animation: {cmd.Get("SpriteGroup")}/{cmd.Get("SpriteName")}",
        EventCommandType.Sit => $"Sit: {cmd.Get("Who")} on {cmd.Get("Thing")}",
        EventCommandType.Stand => $"Stand: {cmd.Get("Who")}",
        EventCommandType.OpenDoor => $"Open Door: {cmd.Get("Who")} opens {cmd.Get("Thing")}",
        EventCommandType.PicklockDoor => $"Picklock Door: {cmd.Get("Who")} picks {cmd.Get("Thing")}",
        EventCommandType.TakeFromContainer => $"Pick Item From Container: {cmd.Get("Who")} from {cmd.Get("Thing")}",
        _ => cmd.Type.ToString()
    };
}
