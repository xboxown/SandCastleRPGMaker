namespace SandcastleRpgMaker.Engine;

public enum WatchKind { None, Switch, Variable, ReadOnlyInfo }

/// <summary>One inspectable piece of state for whichever command is about to run - what the debug UI's "Locals" panel shows and, for Switch/Variable, lets the person edit live before continuing.</summary>
public class WatchEntry
{
    public string Label { get; set; } = string.Empty;

    /// <summary>The real switch/variable name to read/write through the context - kept separate from Label so a decorated display label (e.g. "x (current, before this line runs)") never has to be parsed back apart to find it.</summary>
    public string Key { get; set; } = string.Empty;

    public WatchKind Kind { get; set; }
    public string Value { get; set; } = string.Empty;
}

/// <summary>
/// The same control-flow rules as EventCommandInterpreter.Run (branches,
/// End, everything else dispatched through IEventExecutionContext) -
/// reusing its EvaluateBranch/FindElseOrEndBranch/FindEndBranch directly so
/// there is exactly one place that logic lives - but exposed one Step() at
/// a time instead of running straight through. This is the foundation the
/// step-debugger UI is built on: initialize at whatever line the person
/// selected as the starting point, call Step() once per "Next"/F5 press,
/// and inspect/edit switches or variables between steps through the same
/// IEventExecutionContext the interpreter already uses - there is no
/// separate state to keep in sync, editing through GetSwitch/SetSwitch or
/// GetVariable/SetVariable changes exactly what the next branch check sees.
/// </summary>
public class EventCommandDebugger
{
    private readonly List<EventCommand> _commands;
    private readonly IEventExecutionContext _context;

    public int CurrentIndex { get; private set; }
    public bool IsFinished { get; private set; }
    public string LastStepDescription { get; private set; } = string.Empty;

    public EventCommandDebugger(List<EventCommand> commands, IEventExecutionContext context, int startIndex = 0)
    {
        _commands = commands;
        _context = context;
        CurrentIndex = commands.Count == 0 ? 0 : Math.Clamp(startIndex, 0, commands.Count - 1);
        IsFinished = commands.Count == 0;
    }

    public EventCommand? CurrentCommand => !IsFinished && CurrentIndex >= 0 && CurrentIndex < _commands.Count ? _commands[CurrentIndex] : null;

    /// <summary>What to show in the watch panel for the command about to run - called BEFORE Step() so the person can inspect (and, for Switch/Variable, edit) state that Step() is about to read.</summary>
    public List<WatchEntry> GetWatchEntriesForCurrentCommand()
    {
        var cmd = CurrentCommand;
        var entries = new List<WatchEntry>();
        if (cmd is null) return entries;

        switch (cmd.Type)
        {
            case EventCommandType.ConditionalBranchSwitch:
            {
                string name = cmd.Get("SwitchName");
                entries.Add(new WatchEntry { Label = name, Key = name, Kind = WatchKind.Switch, Value = _context.GetSwitch(name).ToString() });
                entries.Add(new WatchEntry { Label = "Comparing against", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("Value") });
                break;
            }
            case EventCommandType.ConditionalBranchVariable:
            {
                string name = cmd.Get("VariableName");
                entries.Add(new WatchEntry { Label = name, Key = name, Kind = WatchKind.Variable, Value = _context.GetVariable(name).ToString("0.###") });
                entries.Add(new WatchEntry { Label = "Comparison", Kind = WatchKind.ReadOnlyInfo, Value = $"{cmd.Get("Comparison")} {cmd.Get("Value")}" });
                break;
            }
            case EventCommandType.SetSwitch:
            {
                string name = cmd.Get("SwitchName");
                entries.Add(new WatchEntry { Label = $"{name} (current, before this line runs)", Key = name, Kind = WatchKind.Switch, Value = _context.GetSwitch(name).ToString() });
                entries.Add(new WatchEntry { Label = "Will be set to", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("Value") });
                break;
            }
            case EventCommandType.SetVariable:
            {
                string name = cmd.Get("VariableName");
                entries.Add(new WatchEntry { Label = $"{name} (current, before this line runs)", Key = name, Kind = WatchKind.Variable, Value = _context.GetVariable(name).ToString("0.###") });
                entries.Add(new WatchEntry { Label = "Operation", Kind = WatchKind.ReadOnlyInfo, Value = $"{cmd.Get("Operation")} {cmd.Get("Value")}" });
                break;
            }
            case EventCommandType.PlaySound:
                entries.Add(new WatchEntry { Label = "Sound resource id", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("SoundResourceId") });
                entries.Add(new WatchEntry { Label = "Volume", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("Volume") });
                break;
            case EventCommandType.PlayMusic:
                entries.Add(new WatchEntry { Label = "Music resource id", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("MusicResourceId") });
                entries.Add(new WatchEntry { Label = "Volume", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("Volume") });
                break;
            case EventCommandType.ShakeScreen:
                entries.Add(new WatchEntry { Label = "Intensity", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("Intensity") });
                entries.Add(new WatchEntry { Label = "Duration (s)", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("DurationSeconds") });
                entries.Add(new WatchEntry { Label = "Affects characters", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("AffectsCharacters") });
                break;
            case EventCommandType.ChangeDayNight:
                entries.Add(new WatchEntry { Label = "Period", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("Period") });
                break;
            case EventCommandType.ApplyPaletteColor:
                entries.Add(new WatchEntry { Label = "Palette group", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("PaletteGroup") });
                entries.Add(new WatchEntry { Label = "Color index", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("ColorIndex") });
                entries.Add(new WatchEntry { Label = "Hex", Kind = WatchKind.ReadOnlyInfo, Value = "#" + cmd.Get("Hex") });
                entries.Add(new WatchEntry { Label = "Opacity %", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("OpacityPercent") });
                break;
            case EventCommandType.ChangeSelfBookContent:
                entries.Add(new WatchEntry { Label = "Page index", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("PageIndex") });
                entries.Add(new WatchEntry { Label = "Frame id", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("FrameId") });
                entries.Add(new WatchEntry { Label = "Override stored?", Kind = WatchKind.ReadOnlyInfo, Value = _context.HasContentOverrideFor(cmd) ? "Yes - step onto this line to confirm" : "Not yet - step onto this line" });
                break;
            case EventCommandType.ChangeExternalBookContent:
                entries.Add(new WatchEntry { Label = "Target book id", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("TargetBookId") });
                entries.Add(new WatchEntry { Label = "Page index", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("PageIndex") });
                entries.Add(new WatchEntry { Label = "Frame id", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("FrameId") });
                entries.Add(new WatchEntry { Label = "Override stored?", Kind = WatchKind.ReadOnlyInfo, Value = _context.HasContentOverrideFor(cmd) ? "Yes - step onto this line to confirm" : "Not yet - step onto this line" });
                break;
            case EventCommandType.ChangeBookSettings:
                entries.Add(new WatchEntry { Label = "Target book id", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("TargetBookId") });
                entries.Add(new WatchEntry { Label = "Note", Kind = WatchKind.ReadOnlyInfo, Value = "Runtime-only - never touches the book's own saved settings" });
                break;
            case EventCommandType.ShowText:
                entries.Add(new WatchEntry { Label = "Text", Kind = WatchKind.ReadOnlyInfo, Value = cmd.Get("Text") });
                break;
        }
        return entries;
    }

    /// <summary>Lets the watch panel push an edited Switch/Variable value back through the same context Step() will read from - this is what makes "change the value, then press Next" actually affect the next branch check.</summary>
    public void ApplyWatchEdit(WatchEntry entry, string newValue)
    {
        switch (entry.Kind)
        {
            case WatchKind.Switch when bool.TryParse(newValue, out bool b):
                _context.SetSwitch(entry.Key, b);
                break;
            case WatchKind.Variable when double.TryParse(newValue, out double v):
                _context.SetVariable(entry.Key, v);
                break;
        }
    }

    /// <summary>Runs exactly the command CurrentCommand points at, then advances - identical branch/End/dispatch behavior to EventCommandInterpreter.Run, just paused after one command instead of looping to completion.</summary>
    public void Step()
    {
        if (IsFinished) return;
        var cmd = CurrentCommand;
        if (cmd is null) { IsFinished = true; return; }

        switch (cmd.Type)
        {
            case EventCommandType.End:
                LastStepDescription = "End - command list stopped.";
                IsFinished = true;
                return;

            case EventCommandType.ConditionalBranchSwitch:
            case EventCommandType.ConditionalBranchVariable:
            case EventCommandType.AdvancedIf:
            {
                bool branchTrue = EventCommandInterpreter.EvaluateBranch(cmd, _context);
                LastStepDescription = $"{cmd.Type} -> {(branchTrue ? "true" : "false")}";
                CurrentIndex = branchTrue ? CurrentIndex + 1 : EventCommandInterpreter.FindElseOrEndBranch(_commands, CurrentIndex) + 1;
                break;
            }

            case EventCommandType.Else:
                LastStepDescription = "Else (skipped - the true branch already ran)";
                CurrentIndex = EventCommandInterpreter.FindEndBranch(_commands, CurrentIndex) + 1;
                break;

            case EventCommandType.EndBranch:
                LastStepDescription = "EndBranch";
                CurrentIndex++;
                break;

            case EventCommandType.Label:
                LastStepDescription = $"Label \"{cmd.Get("LabelName")}\"";
                CurrentIndex++;
                break;

            case EventCommandType.GoToLabel:
            {
                string name = cmd.Get("LabelName");
                int target = _commands.FindIndex(c => c.Type == EventCommandType.Label && string.Equals(c.Get("LabelName"), name, StringComparison.OrdinalIgnoreCase));
                LastStepDescription = target >= 0 ? $"Jump to Label \"{name}\"" : $"Jump to Label \"{name}\" - no such label";
                CurrentIndex = target >= 0 ? target + 1 : CurrentIndex + 1;
                break;
            }

            case EventCommandType.ShowText when DialogChoices.HasChoices(cmd):
            {
                // Nobody to pick in the debugger: show the text and take the first reply.
                _context.Execute(cmd);
                var choices = DialogChoices.Read(cmd);
                int end = DialogChoices.FindEndChoices(_commands, CurrentIndex);
                int first = choices.Count > 0 ? DialogChoices.FindWhen(_commands, CurrentIndex, choices[0].Id) : -1;
                LastStepDescription = first >= 0 ? $"Show Text with replies - taking the first: \"{choices[0].Text}\"" : "Show Text with replies";
                CurrentIndex = first >= 0 ? first + 1 : end >= 0 ? end + 1 : CurrentIndex + 1;
                break;
            }

            case EventCommandType.WhenChoice:
                LastStepDescription = "End of the reply's branch";
                CurrentIndex = DialogChoices.FindEndChoicesFrom(_commands, CurrentIndex) + 1;
                break;

            case EventCommandType.EndChoices:
                LastStepDescription = "End Replies";
                CurrentIndex++;
                break;

            case EventCommandType.StopConversation:
                LastStepDescription = "Stop Conversation";
                CurrentIndex++;
                break;

            default:
            {
                bool didReal = _context.Execute(cmd);
                LastStepDescription = didReal ? $"Executed: {cmd.Type}" : $"(not implemented yet: {cmd.Type})";
                CurrentIndex++;
                break;
            }
        }

        if (CurrentIndex >= _commands.Count) IsFinished = true;
    }
}
