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

    public static void Run(List<EventCommand> commands, IEventExecutionContext context, List<string> trace)
    {
        int i = 0;
        int guard = 0;

        while (i < commands.Count)
        {
            if (++guard > MaxSteps)
            {
                trace.Add("(stopped - exceeded max command steps, likely a runaway loop)");
                return;
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

                case EventCommandType.GoToLabel:
                case EventCommandType.Label:
                    trace.Add($"(not implemented in preview yet: {cmd.Type})");
                    i++;
                    continue;

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
        _ => cmd.Type.ToString()
    };
}
