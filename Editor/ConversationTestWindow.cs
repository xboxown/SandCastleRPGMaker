using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Test Conversation: runs an event page's commands the way the game does -
/// the same interpreter, the same dialog boxes - but here in the editor, with
/// you picking the replies. Boxes show one at a time on a copy of the game
/// screen (click it, or Next, to go on); replies are clicked in the box or
/// on the buttons below it. Variables and switches really change (on a copy,
/// for this test only), so reply conditions and If branches behave as in
/// play. Commands that act on the world (Attack, Teleport...) are only
/// written down in the transcript.
/// </summary>
public class ConversationTestWindow : Window
{
    private abstract record Step;
    private sealed record BoxStep(EventCommand Command, List<DialogChoice>? Replies, Action<int>? Chosen) : Step;
    private sealed record AfterReadStep(Action GoOn) : Step;

    private readonly List<EventCommand> _commands;
    private readonly CharacterDefinition? _self;
    private readonly ResourceDatabase? _resources;
    private readonly Queue<Step> _steps = new();
    private BoxStep? _current;
    private DialogBoxDrawing? _drawing;
    private TestContext _context = null!;
    private readonly Dictionary<(EventCommand, int), int> _picked = new();

    private readonly Canvas _screen = new() { Width = DialogBoxMetrics.ScreenWidth, Height = DialogBoxMetrics.ScreenHeight, Background = Brushes.Transparent };
    private readonly WrapPanel _replyButtons = new() { Margin = new Thickness(0, 6, 0, 0) };
    private readonly Button _next = new() { Content = "Next  ▶", Padding = new Thickness(12, 3, 12, 3), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _status = new() { Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold };
    private readonly ListBox _transcript = new();
    private readonly CheckBox _hasItems = new() { Content = "The party carries the items replies ask about", IsChecked = true, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    public ConversationTestWindow(List<EventCommand> commands, CharacterDefinition? self, ResourceDatabase? resources)
    {
        _commands = commands;
        _self = self;
        _resources = resources;
        Title = "Test Conversation" + (self is null ? "" : $" - {self.Name}");
        Width = 1180;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(10) };
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var restart = new Button { Content = "Restart", Padding = new Thickness(12, 3, 12, 3) };
        restart.Click += (_, _) => Start();
        top.Children.Add(restart);
        top.Children.Add(_hasItems);
        top.Children.Add(new TextBlock
        {
            Text = "   Variables and switches change for this test only. Attack, Teleport and other world commands are written down, not done.",
            Opacity = 0.75, VerticalAlignment = VerticalAlignment.Center
        });
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        var left = new StackPanel();
        var screenGrid = new Grid { Width = DialogBoxMetrics.ScreenWidth, Height = DialogBoxMetrics.ScreenHeight, ClipToBounds = true };
        screenGrid.Children.Add(new System.Windows.Shapes.Rectangle
        {
            Fill = new LinearGradientBrush(Color.FromRgb(0x3A, 0x5A, 0x2A), Color.FromRgb(0x2F, 0x4F, 0x5F), 45)
        });
        RenderOptions.SetBitmapScalingMode(_screen, BitmapScalingMode.NearestNeighbor);
        screenGrid.Children.Add(_screen);
        var view = new Viewbox { Width = 640, Height = 400, Stretch = Stretch.Uniform, Child = screenGrid };
        left.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = view, HorizontalAlignment = HorizontalAlignment.Left, Cursor = Cursors.Hand });
        left.Children.Add(_status);
        left.Children.Add(_replyButtons);
        left.Children.Add(_next);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var right = new DockPanel();
        var transcriptTitle = new TextBlock { Text = "What happened", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(transcriptTitle, Dock.Top);
        right.Children.Add(transcriptTitle);
        right.Children.Add(_transcript);
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);
        root.Children.Add(grid);
        Content = root;

        _screen.MouseLeftButtonDown += (_, e) => ScreenClicked(e.GetPosition(_screen));
        _screen.MouseMove += (_, e) => { if (_current?.Replies is not null) _drawing?.Highlight(_drawing.ReplyAt(e.GetPosition(_screen))); };
        _next.Click += (_, _) => GoOn();
        Loaded += (_, _) => Start();
        Closed += (_, _) => _drawing?.Stop();
    }

    // ---------- Running ----------

    private void Start()
    {
        _drawing?.Stop();
        _steps.Clear();
        _current = null;
        _picked.Clear();
        _transcript.Items.Clear();
        _context = new TestContext(this, _resources);
        Log("— Start —", Brushes.Gray);
        EventCommandInterpreter.Run(_commands, _context, new List<string>());
        if (_current is null) ShowNext();
    }

    private bool _inShowNext;

    private void Enqueue(Step step)
    {
        _steps.Enqueue(step);
        if (_current is null && !_inShowNext) ShowNext();
    }

    private void ShowNext()
    {
        _inShowNext = true;
        try { ShowNextStep(); }
        finally { _inShowNext = false; }
    }

    private void ShowNextStep()
    {
        _drawing?.Stop();
        _drawing = null;
        _screen.Children.Clear();
        _replyButtons.Children.Clear();
        _current = null;
        while (_steps.TryDequeue(out var step))
        {
            if (step is AfterReadStep after)
            {
                Log("Stop Conversation - the box closes", Brushes.Gray);
                after.GoOn();
                if (_current is not null) return; // that put up the next box
                continue;
            }
            Show((BoxStep)step);
            return;
        }
        _next.IsEnabled = false;
        _status.Text = "The conversation is over. Restart to try other replies.";
        Log("— End —", Brushes.Gray);
    }

    private void Show(BoxStep box)
    {
        _current = box;
        var command = box.Command;
        var speaker = SpeakerOf(command.Get("SpeakerId"));
        string title = command.Get(DialogBoxBuilder.TitleKey);
        string name = speaker?.Name ?? (command.Get("SpeakerId") == "Self" ? "Self" : "");
        Log((title.Length > 0 ? $"[{title}] " : "") + (name.Length > 0 ? $"{name}: " : "") + command.Get("Text"), Brushes.Black);

        var (frame, dim) = DialogBoxBuilder.LookOf(command);
        BitmapSource? face = speaker is null ? null : ProjectSession.FirstFrame(ProjectSession.FindSprite(speaker.FaceSpriteGroup, speaker.FaceSpriteName));
        var content = new DialogBoxContent(title, command.Get("Text"), face, command.Get("FaceSide") == "Right", command.Get("Position"), dim,
            ProjectSession.Sprites.FindDialogFrame(frame), box.Replies?.Select(r => r.Text).ToList() ?? new List<string>());
        _drawing = DialogBoxBuilder.Draw(_screen, DialogBoxBuilder.AsProjectShowsIt(content),ProjectSession.Sprites, _resources);

        if (box.Replies is { } replies)
        {
            _next.IsEnabled = false;
            _status.Text = "Pick a reply - click it in the box or below:";
            for (int i = 0; i < replies.Count; i++)
            {
                int index = i;
                var button = new Button { Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 6) };
                var label = new TextBlock();
                if (replies[i].Pinned) label.Inlines.Add(new System.Windows.Documents.Run(ShowTextCommandWindow.PinGlyph + " ") { Foreground = ShowTextCommandWindow.PinBrush, FontFamily = new FontFamily("Segoe UI Symbol") });
                label.Inlines.Add(new System.Windows.Documents.Run($"{i + 1}. {replies[i].Text}"));
                button.Content = label;
                button.Click += (_, _) => Pick(index);
                _replyButtons.Children.Add(button);
            }
        }
        else
        {
            _next.IsEnabled = true;
            _status.Text = "Click the screen or Next to go on.";
        }
    }

    private void ScreenClicked(Point p)
    {
        if (_current?.Replies is not null)
        {
            if (_drawing?.ReplyAt(p) is int index and >= 0 && _drawing.ReplySlots[index] >= 0) Pick(_drawing.ReplySlots[index]);
            return;
        }
        GoOn();
    }

    private void GoOn()
    {
        if (_current is null || _current.Replies is not null) return;
        ShowNext();
    }

    private void Pick(int index)
    {
        if (_current is not { Replies: { } replies, Chosen: { } chosen } || index >= replies.Count) return;
        var reply = replies[index];
        Log($"▶ You pick: \"{reply.Text}\"" + (reply.Pinned ? "  (pinned - comes back here)" : ""), Brushes.MediumBlue);
        _picked[(_current.Command, reply.Id)] = _picked.TryGetValue((_current.Command, reply.Id), out int n) ? n + 1 : 1;
        _drawing?.Stop();
        _drawing = null;
        _screen.Children.Clear();
        _replyButtons.Children.Clear();
        _current = null;
        chosen(reply.Id);
        if (_current is null) ShowNext();
    }

    private void Log(string text, Brush color)
    {
        _transcript.Items.Add(new TextBlock { Text = text, Foreground = color, TextWrapping = TextWrapping.Wrap, MaxWidth = 460 });
        _transcript.ScrollIntoView(_transcript.Items[^1]);
    }

    private CharacterDefinition? SpeakerOf(string key) => key switch
    {
        "" => null,
        "Self" => _self,
        "Player" => ProjectSession.Characters.Characters.FirstOrDefault(c => c.IsTestPlayer),
        _ => ProjectSession.Characters.Characters.FirstOrDefault(c => c.Id.ToString() == key)
    };

    // ---------- Reply conditions, as the game checks them ----------

    private bool Offered(EventCommand showText, DialogChoice reply)
    {
        if (reply.Condition is not { } condition) return true;
        var context = new BookEventPreviewContext
        {
            TriggerActuallyOccurred = true,
            TimesTriggeredSoFar = _picked.TryGetValue((showText, reply.Id), out int n) ? n : 0
        };
        foreach (var def in (ProjectSession.VariablesProvider?.Invoke() ?? new VariableDatabase()).Variables)
        {
            double raw = _context.GetVariable(def.Name);
            context.VariableValues[def.Id] = def.Type == VariableValueType.Boolean ? (raw != 0).ToString() : raw.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        if (ProjectSession.Characters.Characters.FirstOrDefault(c => c.IsTestPlayer) is { } player)
            foreach (var (id, text) in player.StatValues)
                if (int.TryParse(text, out int value)) context.StatValues[id] = value;
        bool has = _hasItems.IsChecked == true;
        if (condition.TriggerConditionBased == BookTriggerConditionBased.ByHavingAnItem) context.TriggerActuallyOccurred = has;
        if (condition.TriggerConditionBased == BookTriggerConditionBased.ByNotHavingAnItem) context.TriggerActuallyOccurred = !has;
        return BookEventConditionEvaluator.Evaluate(condition, context, new List<string>());
    }

    /// <summary>The test's own context: real switches and variables (a fresh copy), boxes go to the window, everything else is written down.</summary>
    private sealed class TestContext : IEventExecutionContext, IDialogChoiceHost
    {
        private readonly ConversationTestWindow _window;
        private readonly BookEventPreviewExecutionContext _values;

        public TestContext(ConversationTestWindow window, ResourceDatabase? resources)
        {
            _window = window;
            _values = new BookEventPreviewExecutionContext(resources ?? new ResourceDatabase(), selfBookId: -1);
        }

        public bool Execute(EventCommand command)
        {
            switch (command.Type)
            {
                case EventCommandType.ShowText:
                    _window.Enqueue(new BoxStep(command, null, null));
                    return true;
                case EventCommandType.SetSwitch:
                case EventCommandType.SetVariable:
                {
                    bool done = _values.Execute(command);
                    string name = command.Type == EventCommandType.SetSwitch ? command.Get("SwitchName") : command.Get("VariableName");
                    string value = command.Type == EventCommandType.SetSwitch ? _values.GetSwitch(name).ToString() : _values.GetVariable(name).ToString(System.Globalization.CultureInfo.InvariantCulture);
                    _window.Log($"   {name} is now {value}", Brushes.DarkGreen);
                    return done;
                }
                default:
                    _window.Log($"   (would run) {EventCommandCatalog.Summarize(command).Trim()}", Brushes.DarkRed);
                    return true;
            }
        }

        public bool GetSwitch(string name) => _values.GetSwitch(name);
        public void SetSwitch(string name, bool value) => _values.SetSwitch(name, value);
        public double GetVariable(string name) => _values.GetVariable(name);
        public void SetVariable(string name, double value) => _values.SetVariable(name, value);
        public bool HasContentOverrideFor(EventCommand command) => false;

        public bool CanAskChoice => true;

        public bool AskChoice(EventCommand showText, List<DialogChoice> choices, Action<int> chosen)
        {
            var offered = choices.Where(c => _window.Offered(showText, c)).ToList();
            int hidden = choices.Count - offered.Count;
            if (hidden > 0) _window.Log($"   ({hidden} repl{(hidden == 1 ? "y" : "ies")} not offered - condition not met)", Brushes.Gray);
            if (offered.Count == 0)
            {
                _window.Enqueue(new BoxStep(showText, null, null));
                return false;
            }
            _window.Enqueue(new BoxStep(showText, offered, chosen));
            return true;
        }

        public bool WaitUntilRead(Action goOn)
        {
            if (_window._current is null && _window._steps.Count == 0) return false;
            _window.Enqueue(new AfterReadStep(goOn));
            return true;
        }
    }
}
