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
    private const int MaxSteps = 5000; // safety valve - nothing in a real command list should ever need this many steps; a runaway loop (once GoToLabel is real) would otherwise hang forever

    /// <param name="start">Where to begin - a reply's branch, when the player has just picked it.</param>
    /// <param name="returns">Pinned replies being answered: reaching StopAt (the end of that reply's branch) goes back to ResumeAt (its Show Text). Innermost last.</param>
    public static void Run(List<EventCommand> commands, IEventExecutionContext context, List<string> trace, int start = 0, IReadOnlyList<(int StopAt, int ResumeAt)>? returns = null)
    {
        int i = start;
        returns ??= Array.Empty<(int, int)>();
        int guard = 0;

        while (i < commands.Count)
        {
            if (++guard > MaxSteps)
            {
                trace.Add("(stopped - exceeded max command steps, likely a runaway loop)");
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
                    trace.Add("End - command list stopped.");
                    return;

                case EventCommandType.ConditionalBranchSwitch:
                case EventCommandType.ConditionalBranchVariable:
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
                            if (when < 0) { Run(commands, context, trace, afterBranches, returnsHere); return; }
                            var next = returnsHere;
                            if (choices.FirstOrDefault(c => c.Id == id) is { Pinned: true })
                                next = returnsHere.Append((DialogChoices.FindBranchEnd(commands, when), at)).ToList();
                            Run(commands, context, trace, when + 1, next);
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

                case EventCommandType.CloseShowText:
                {
                    if (context is IDialogChoiceHost { CanAskChoice: true } waiter)
                    {
                        int next = i + 1;
                        var returnsHere = returns;
                        if (waiter.WaitUntilRead(() => Run(commands, context, trace, next, returnsHere)))
                        {
                            trace.Add("Close Show Text - waiting for the box to be read");
                            return;
                        }
                    }
                    trace.Add("Close Show Text");
                    i++;
                    continue;
                }

                default:
                {
                    bool didReal = context.Execute(cmd);
                    trace.Add(didReal ? $"Executed: {Describe(cmd)}" : $"(not implemented in preview yet: {Describe(cmd)})");
                    i++;
                    continue;
                }
            }
        }
    }

    private static int LastIndexOf(IReadOnlyList<(int StopAt, int ResumeAt)> returns, int index)
    {
        for (int r = returns.Count - 1; r >= 0; r--)
            if (returns[r].StopAt == index) return r;
        return -1;
    }

    internal static bool EvaluateBranch(EventCommand cmd, IEventExecutionContext context)
    {
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
        EventCommandType.PlaySound => $"Play Sound (resource #{cmd.Get("SoundResourceId")})",
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
        EventCommandType.ChangeFaction => $"Change Faction: character {cmd.Get("CharacterId")} joins faction #{cmd.Get("FactionId")}",
        _ => cmd.Type.ToString()
    };
}
