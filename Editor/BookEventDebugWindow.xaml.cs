using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Replaces the old single-shot "run everything and show a trace" Preview
/// with a real step debugger for the command side, while keeping the same
/// condition-simulation setup the old window had - conditions still need
/// simulated input the same way they always did, that part isn't what
/// changed here. Foundation only: watch/edit support covers
/// Switch/Variable/PlaySound/ShowText, the command types that currently
/// exist; every future command type extends EventCommandDebugger's watch
/// list, not this window.
/// </summary>
public partial class BookEventDebugWindow : Window
{
    private readonly BookEventConditions _conditions;
    private readonly List<EventCommand> _commands;
    private readonly ResourceDatabase _resources;
    private readonly StatDatabase _stats;
    /// <summary>Which book "Self" resolves to for Change Self Book Content - without this, that command's Execute has no way to know which book it's changing and silently no-ops (bookId < 0), which was a real bug: the stepper claimed "a real command executes for real" while this one specifically never did anything at all.</summary>
    private readonly int _selfBookId;

    private CheckBox? _triggerOccurredCheck;
    private readonly Dictionary<int, TextBox> _variableBoxes = new();
    private CheckBox? _eventCompletedCheck;
    private readonly Dictionary<int, TextBox> _statBoxes = new();
    private CheckBox? _slot1Check;
    private CheckBox? _slot2Check;
    private TextBox? _distanceBox;
    private TextBox? _timesTriggeredBox;
    private CheckBox? _untilMetCheck;
    private CheckBox? _dropToSelfPresentCheck;
    private ComboBox? _complexTriggerCombo;
    private CheckBox? _complexHasBeenReadCheck;

    private int _selectedStartIndex;
    private readonly List<Border> _scriptLineBorders = new();
    private EventCommandDebugger? _debugger;
    private BookEventPreviewExecutionContext? _executionContext;
    private readonly List<TextBox> _watchEditBoxes = new();
    private List<WatchEntry> _currentWatchEntries = new();

    public BookEventDebugWindow(BookEventConditions conditions, List<EventCommand> commands, ResourceDatabase resources, StatDatabase stats, int selfBookId)
    {
        InitializeComponent();
        _conditions = conditions;
        _commands = commands;
        _resources = resources;
        _stats = stats;
        _selfBookId = selfBookId;

        BuildSetupFields();
        BuildScriptView();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F5 && StepButton.IsEnabled)
            {
                StepButton_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        };
    }

    // ---------- Condition simulation setup (same shape as the old Preview window) ----------

    private void BuildSetupFields()
    {
        SetupPanel.Children.Clear();
        bool addedAnyConditionSpecificField = false;

        if (_conditions.TriggerConditionBased != BookTriggerConditionBased.Always)
        {
            _triggerOccurredCheck = new CheckBox { Content = $"Trigger occurred ({_conditions.TriggerConditionBased})", IsChecked = true, Margin = new Thickness(0, 0, 0, 10) };
            SetupPanel.Children.Add(_triggerOccurredCheck);
            addedAnyConditionSpecificField = true;
        }

        if (_conditions.ConditionInACondition && _conditions.VariableConditions.Count > 0)
        {
            SetupPanel.Children.Add(Header("In A Condition"));
            foreach (var entry in _conditions.VariableConditions)
            {
                SetupPanel.Children.Add(new TextBlock { Text = $"{entry.VariableDisplayName}:", Margin = new Thickness(0, 4, 0, 0) });
                var box = new TextBox();
                SetupPanel.Children.Add(box);
                _variableBoxes[entry.VariableId] = box;
            }
            addedAnyConditionSpecificField = true;
        }

        if (_conditions.ConditionEventTriggerCompleted)
        {
            _eventCompletedCheck = new CheckBox { Content = "Referenced event has completed", Margin = new Thickness(0, 10, 0, 10) };
            SetupPanel.Children.Add(_eventCompletedCheck);
            addedAnyConditionSpecificField = true;
        }

        if (_conditions.ConditionStatCondition && _conditions.StatConditions.Any(s => s.IsActive))
        {
            SetupPanel.Children.Add(Header("Stat Condition"));
            foreach (var entry in _conditions.StatConditions.Where(s => s.IsActive))
            {
                string name = _stats.Stats.FirstOrDefault(s => s.Id == entry.StatId)?.Name ?? $"Stat #{entry.StatId}";
                SetupPanel.Children.Add(new TextBlock { Text = $"{name}:", Margin = new Thickness(0, 4, 0, 0) });
                var box = new TextBox { Text = "0" };
                SetupPanel.Children.Add(box);
                _statBoxes[entry.StatId] = box;
            }
            addedAnyConditionSpecificField = true;
        }

        if (_conditions.ConditionCombineItem)
        {
            SetupPanel.Children.Add(Header("Combine Item Condition"));
            if (!_conditions.CombineItemSlot1.IsSelf)
            {
                _slot1Check = new CheckBox { Content = "Slot 1 item present", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
                SetupPanel.Children.Add(_slot1Check);
            }
            if (!_conditions.CombineItemSlot2.IsSelf)
            {
                _slot2Check = new CheckBox { Content = "Slot 2 item present", IsChecked = true, Margin = new Thickness(0, 4, 0, 10) };
                SetupPanel.Children.Add(_slot2Check);
            }
            addedAnyConditionSpecificField = true;
        }

        if (_conditions.ConditionDropToSelf)
        {
            SetupPanel.Children.Add(Header("Drop To Self Condition"));
            _dropToSelfPresentCheck = new CheckBox { Content = "Target item was dropped on this book", IsChecked = true, Margin = new Thickness(0, 4, 0, 10) };
            SetupPanel.Children.Add(_dropToSelfPresentCheck);
            addedAnyConditionSpecificField = true;
        }

        if (_conditions.ConditionJoinedParty || _conditions.ConditionDistance)
        {
            SetupPanel.Children.Add(Header("Simulated distance"));
            _distanceBox = new TextBox { Text = "0" };
            SetupPanel.Children.Add(_distanceBox);
            addedAnyConditionSpecificField = true;
        }

        if (_conditions.ConditionComplex)
        {
            SetupPanel.Children.Add(Header("Complex Condition"));
            SetupPanel.Children.Add(new TextBlock
            {
                Text = "Shares the same simulated values as the matching top-level checkmarks above (Stat/Variable/etc.) - a referenced type with no corresponding checkmark checked here uses default values (0, unchecked, etc). A trigger-type sub-condition (Use/Pickup/Drop/After Read) needs its own simulated interaction, separate from this page's own top-level trigger:",
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            });
            SetupPanel.Children.Add(new TextBlock { Text = "Simulate this trigger having just occurred:" });
            _complexTriggerCombo = new ComboBox
            {
                ItemsSource = new[] { "(none)", "By Use Item", "By Pickup Item", "By Drop Item", "By Trade Item", "By Destroy Item", "By Having An Item", "By Not Having An Item" },
                SelectedIndex = 0,
                Margin = new Thickness(0, 2, 0, 6)
            };
            SetupPanel.Children.Add(_complexTriggerCombo);
            _complexHasBeenReadCheck = new CheckBox { Content = "Has been read (for By After Read Item)", Margin = new Thickness(0, 0, 0, 10) };
            SetupPanel.Children.Add(_complexHasBeenReadCheck);
            addedAnyConditionSpecificField = true;
        }

        if (!addedAnyConditionSpecificField)
        {
            SetupPanel.Children.Add(new TextBlock
            {
                Text = "No conditions are checked on this page besides Cycle - it fires on every eligible trigger.",
                FontStyle = FontStyles.Italic,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 10)
            });
        }

        SetupPanel.Children.Add(Header("Cycle"));
        SetupPanel.Children.Add(new TextBlock { Text = "Times already triggered:" });
        _timesTriggeredBox = new TextBox { Text = "0", Margin = new Thickness(0, 0, 0, 4) };
        SetupPanel.Children.Add(_timesTriggeredBox);
        if (_conditions.CycleCondition == CycleConditionType.UntilConditionMet)
        {
            _untilMetCheck = new CheckBox { Content = "Condition still met", IsChecked = true, Margin = new Thickness(0, 4, 0, 0) };
            SetupPanel.Children.Add(_untilMetCheck);
        }
    }

    private static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 2) };

    private void EvaluateButton_Click(object sender, RoutedEventArgs e)
    {
        var context = new BookEventPreviewContext
        {
            TriggerActuallyOccurred = _triggerOccurredCheck?.IsChecked ?? true,
            EventCompletedActual = _eventCompletedCheck?.IsChecked ?? false,
            CombineItemSlot1Present = _slot1Check?.IsChecked ?? true,
            CombineItemSlot2Present = _slot2Check?.IsChecked ?? true,
            DropToSelfItemPresent = _dropToSelfPresentCheck?.IsChecked ?? true,
            SimulatedDistance = double.TryParse(_distanceBox?.Text, out double d) ? d : 0,
            TimesTriggeredSoFar = int.TryParse(_timesTriggeredBox?.Text, out int t) ? t : 0,
            UntilConditionStillMet = _untilMetCheck?.IsChecked ?? true,
            MomentaryTriggerJustOccurred = (_complexTriggerCombo?.SelectedItem as string) switch
            {
                "By Use Item" => BookTriggerConditionBased.ByUseItem,
                "By Pickup Item" => BookTriggerConditionBased.ByPickupItem,
                "By Drop Item" => BookTriggerConditionBased.ByDropItem,
                "By Trade Item" => BookTriggerConditionBased.ByTradeItem,
                "By Destroy Item" => BookTriggerConditionBased.ByDestroyItem,
                "By Having An Item" => BookTriggerConditionBased.ByHavingAnItem,
                "By Not Having An Item" => BookTriggerConditionBased.ByNotHavingAnItem,
                _ => null
            },
            HasBeenRead = _complexHasBeenReadCheck?.IsChecked ?? false
        };
        foreach (var kv in _variableBoxes) context.VariableValues[kv.Key] = kv.Value.Text;
        foreach (var kv in _statBoxes) context.StatValues[kv.Key] = int.TryParse(kv.Value.Text, out int v) ? v : 0;

        var trace = new List<string>();
        bool passed = BookEventConditionEvaluator.Evaluate(_conditions, context, trace);
        trace.Add(string.Empty);
        trace.Add(passed ? "=== Conditions PASSED - click a line in the script, then Step (F5) ===" : "=== Conditions FAILED - nothing to debug ===");
        StepLogBox.Text = string.Join(Environment.NewLine, trace);

        _debugger = null;
        _executionContext = passed ? new BookEventPreviewExecutionContext(_resources, _selfBookId) : null;
        // Real, confirmed fix - PlaySound/PlayMusic/SetSwitch/SetVariable/Change*BookContent/ChangeBookSettings all execute directly with no callback needed at all, which is why those always "just worked". ShakeScreen/ShowText/ChangeDayNight/ApplyPaletteColor each genuinely require their own real OnXxx callback to be wired here - left unwired, they silently no-op (return false) rather than fail loudly, which is exactly why they looked broken. Wired here to a real, visible effect within this debug window itself (a real animated shake for ShakeScreen, a real trace-log line for the other three, since a scene-wide day/night or palette tint has no obvious visual surface to apply to in an isolated debug window).
        if (_executionContext is not null)
        {
            _executionContext.OnShakeScreen = (intensity, duration, _) => RunRealScreenShake(intensity, duration);
            _executionContext.OnShowText = text => AppendLiveEffectLog($"Show Text: \"{text}\"");
            _executionContext.OnChangeDayNight = period => AppendLiveEffectLog($"Day/Night changed to: {period}");
            _executionContext.OnApplyPaletteColor = (hex, opacity) => AppendLiveEffectLog($"Palette color applied: {hex} at {opacity:P0} opacity");
        }
        StepButton.IsEnabled = passed && _commands.Count > 0;
        WatchPanel.Children.Clear();
        HighlightCurrentLine(-1);
    }

    /// <summary>A real, visible shake, per spec - since this debug window has no real game canvas of its own to shake, the effect is applied directly to the window's own content instead, so Shake Screen genuinely does something visible here too, not just in a real Game Test session.</summary>
    private void RunRealScreenShake(int intensity, double durationSeconds)
    {
        if (Content is not UIElement root) return;
        var transform = new TranslateTransform();
        root.RenderTransform = transform;
        double amplitude = Math.Clamp(intensity, 1, 20);
        var animation = new System.Windows.Media.Animation.DoubleAnimationUsingKeyFrames();
        var timeline = TimeSpan.FromSeconds(Math.Max(0.1, durationSeconds));
        int steps = Math.Max(4, (int)(timeline.TotalSeconds * 10));
        var rand = new Random();
        for (int i = 0; i <= steps; i++)
        {
            double t = timeline.TotalSeconds * i / steps;
            double value = i == steps ? 0 : (rand.NextDouble() * 2 - 1) * amplitude;
            animation.KeyFrames.Add(new System.Windows.Media.Animation.LinearDoubleKeyFrame(value, System.Windows.Media.Animation.KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        }
        transform.BeginAnimation(TranslateTransform.XProperty, animation);
        AppendLiveEffectLog($"Shake Screen: intensity {intensity}, {durationSeconds:0.#}s");
    }

    private void AppendLiveEffectLog(string message)
    {
        StepLogBox.Text += Environment.NewLine + $">>> {message}";
        StepLogBox.ScrollToEnd();
    }

    // ---------- Script view ----------

    private void BuildScriptView()
    {
        ScriptLinesPanel.Children.Clear();
        _scriptLineBorders.Clear();

        for (int i = 0; i < _commands.Count; i++)
        {
            int index = i;
            var line = BuildColoredLine(_commands[i]);
            var border = new Border { Padding = new Thickness(4, 2, 4, 2), Cursor = Cursors.Hand, Child = line };
            border.MouseLeftButtonDown += (_, _) =>
            {
                if (_debugger is not null) return; // line selection only matters before stepping starts
                _selectedStartIndex = index;
                HighlightSelectedStart(index);
            };
            _scriptLineBorders.Add(border);
            ScriptLinesPanel.Children.Add(border);
        }

        if (_commands.Count > 0) HighlightSelectedStart(0);
    }

    /// <summary>Simple dark-theme syntax coloring: command type as a keyword, parameter keys/values distinguished - not a real tokenizer, just enough to make the script visually scannable the way the design doc's "text version" implies.</summary>
    private static TextBlock BuildColoredLine(EventCommand cmd)
    {
        var block = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 12 };
        var keyword = new SolidColorBrush(Color.FromRgb(0x56, 0x9C, 0xD6));
        var punctuation = new SolidColorBrush(Color.FromRgb(0xD4, 0xD4, 0xD4));
        var paramKey = new SolidColorBrush(Color.FromRgb(0x9C, 0xDC, 0xFE));
        var paramValue = new SolidColorBrush(Color.FromRgb(0xCE, 0x91, 0x78));

        block.Inlines.Add(new Run(cmd.Type.ToString()) { Foreground = keyword, FontWeight = FontWeights.Bold });
        block.Inlines.Add(new Run("(") { Foreground = punctuation });
        bool first = true;
        foreach (var kv in cmd.Parameters)
        {
            if (!first) block.Inlines.Add(new Run(", ") { Foreground = punctuation });
            first = false;
            block.Inlines.Add(new Run(kv.Key) { Foreground = paramKey });
            block.Inlines.Add(new Run("=") { Foreground = punctuation });
            block.Inlines.Add(new Run($"\"{kv.Value}\"") { Foreground = paramValue });
        }
        block.Inlines.Add(new Run(");") { Foreground = punctuation });
        return block;
    }

    private void HighlightSelectedStart(int index)
    {
        for (int i = 0; i < _scriptLineBorders.Count; i++)
            _scriptLineBorders[i].Background = i == index ? new SolidColorBrush(Color.FromRgb(0x3A, 0x3D, 0x2A)) : Brushes.Transparent;
    }

    private void HighlightCurrentLine(int index)
    {
        for (int i = 0; i < _scriptLineBorders.Count; i++)
            _scriptLineBorders[i].Background = i == index ? new SolidColorBrush(Color.FromRgb(0x00, 0x4A, 0x00)) : Brushes.Transparent;
    }

    // ---------- Stepping ----------

    private void StepButton_Click(object sender, RoutedEventArgs e)
    {
        if (_executionContext is null) return;

        if (_debugger is null)
        {
            // First press: arm the debugger at the selected line and show its
            // pre-execution state - nothing has run yet, matching a real
            // debugger landing on a breakpoint before that line executes.
            _debugger = new EventCommandDebugger(_commands, _executionContext, _selectedStartIndex);
            AppendLog($"--- Debugging started at line {_selectedStartIndex + 1} ---");
            ShowCurrentState();
            return;
        }

        ApplyWatchEditsBeforeStep();
        _debugger.Step();
        AppendLog(_debugger.LastStepDescription);

        if (_debugger.IsFinished)
        {
            AppendLog("=== Finished ===");
            StepButton.IsEnabled = false;
            HighlightCurrentLine(-1);
            WatchPanel.Children.Clear();
            return;
        }

        ShowCurrentState();
    }

    private void ShowCurrentState()
    {
        if (_debugger is null) return;
        HighlightCurrentLine(_debugger.CurrentIndex);
        _currentWatchEntries = _debugger.GetWatchEntriesForCurrentCommand();
        RenderWatchPanel();
    }

    private void RenderWatchPanel()
    {
        WatchPanel.Children.Clear();
        _watchEditBoxes.Clear();

        if (_currentWatchEntries.Count == 0)
        {
            WatchPanel.Children.Add(new TextBlock { Text = "(nothing to inspect for this line)", FontStyle = FontStyles.Italic, Opacity = 0.7 });
            return;
        }

        foreach (var entry in _currentWatchEntries)
        {
            WatchPanel.Children.Add(new TextBlock { Text = entry.Label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 4, 0, 0) });
            var box = new TextBox { Text = entry.Value, IsReadOnly = entry.Kind == WatchKind.ReadOnlyInfo, Tag = entry };
            WatchPanel.Children.Add(box);
            if (entry.Kind != WatchKind.ReadOnlyInfo) _watchEditBoxes.Add(box);
        }
    }

    /// <summary>Pushes whatever the person typed into an editable watch box back into the execution context, right before Step() runs and reads it - this is what makes editing a value actually change what the next branch check sees.</summary>
    private void ApplyWatchEditsBeforeStep()
    {
        if (_debugger is null) return;
        foreach (var box in _watchEditBoxes)
        {
            if (box.Tag is WatchEntry entry) _debugger.ApplyWatchEdit(entry, box.Text);
        }
    }

    private void AppendLog(string line)
    {
        StepLogBox.Text = string.IsNullOrEmpty(StepLogBox.Text) ? line : StepLogBox.Text + Environment.NewLine + line;
        StepLogBox.CaretIndex = StepLogBox.Text.Length;
        StepLogBox.ScrollToEnd();
    }
}
