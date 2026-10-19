using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Show Text: one dialog box - an optional title, the text, who says it
/// (their face), its frame (a DialogFrame sprite, or none), and the replies
/// the player can pick (Ultima VII's conversation answers). Each reply has
/// its own text and an optional condition for being offered; what a reply
/// does is its "When" branch in the command list (EventEditorControl adds
/// and keeps those branches in step with the replies).
/// </summary>
public partial class ShowTextCommandWindow : Window
{
    public EventCommand Result { get; private set; } = new() { Type = EventCommandType.ShowText };

    /// <summary>Speaker choices: key stored in the command ("" none, "Self", "Player", or a character id) and what the list shows.</summary>
    private sealed record SpeakerChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record FrameChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly List<DialogChoice> _replies;
    private bool _loading = true;
    private DialogBoxDrawing? _preview;

    public ShowTextCommandWindow(EventCommand? existing = null)
    {
        InitializeComponent();
        var speakers = new List<SpeakerChoice>
        {
            new("", "(None - narration)"),
            new("Self", "(Self - the character this event belongs to, or who set it off)"),
            new("Player", "(Main Player)")
        };
        speakers.AddRange(ProjectSession.Characters.Characters.Select(c => new SpeakerChoice(c.Id.ToString(), c.Name)));
        SpeakerCombo.ItemsSource = speakers;
        SpeakerCombo.SelectedItem = speakers.FirstOrDefault(s => s.Key == (existing?.Get("SpeakerId") ?? "")) ?? speakers[0];
        if (existing?.Get("FaceSide") == "Right") FaceSideCombo.SelectedIndex = 1;

        var frameNames = ProjectSession.Sprites.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.DialogFrameGroupName)?.Sprites.Select(s => s.Name).ToList() ?? new List<string>();
        var frames = new List<FrameChoice>
        {
            new("", frameNames.Count > 0 ? $"(Default - {frameNames[0]})" : "(Default - the DialogFrame group is empty: no frame)"),
            new("None", "(None - no box, just the text)")
        };
        frames.AddRange(frameNames.Select(n => new FrameChoice(n, n)));
        FrameCombo.ItemsSource = frames;
        var (frame, dim) = existing is null ? ("", false) : DialogBoxBuilder.LookOf(existing);
        FrameCombo.SelectedItem = frames.FirstOrDefault(f => f.Key == frame) ?? frames[0];
        DimCheck.IsChecked = dim;

        _replies = existing is null ? new List<DialogChoice>() : DialogChoices.Read(existing);
        if (existing is not null)
        {
            TextBox1.Text = existing.Get("Text");
            TitleBox.Text = existing.Get(DialogBoxBuilder.TitleKey);
            SelectComboByContent(PositionCombo, existing.Get("Position"), fallbackIndex: 2);
        }
        RefreshReplies();
        _loading = false;
        Loaded += (_, _) => RefreshPreview();
        Closed += (_, _) => _preview?.Stop();
    }

    private static void SelectComboByContent(ComboBox combo, string value, int fallbackIndex)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Content as string, value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                return;
            }
        }
        combo.SelectedIndex = fallbackIndex;
    }

    private string FrameKey => (FrameCombo.SelectedItem as FrameChoice)?.Key ?? "";
    private string PositionText => (PositionCombo.SelectedItem as ComboBoxItem)?.Content as string ?? "Bottom";

    // ---------- Preview ----------

    private void Any_Changed(object sender, RoutedEventArgs e) => RefreshPreview();

    private void RefreshPreview()
    {
        if (_loading || PreviewCanvas is null) return;
        _preview?.Stop();
        PreviewCanvas.Children.Clear();
        var sprites = ProjectSession.Sprites;
        string key = (SpeakerCombo.SelectedItem as SpeakerChoice)?.Key ?? "";
        var speaker = key switch
        {
            "" or "Self" => null,
            "Player" => ProjectSession.Characters.Characters.FirstOrDefault(c => c.IsTestPlayer),
            _ => ProjectSession.Characters.Characters.FirstOrDefault(c => c.Id.ToString() == key)
        };
        var face = speaker is null ? null : ProjectSession.FirstFrame(ProjectSession.FindSprite(speaker.FaceSpriteGroup, speaker.FaceSpriteName));
        var content = new DialogBoxContent(TitleBox.Text, TextBox1.Text, face, FaceSideCombo.SelectedIndex == 1, PositionText, DimCheck.IsChecked == true,
            sprites.FindDialogFrame(FrameKey), _replies.Select(r => r.Text).ToList());
        _preview = DialogBoxBuilder.Draw(PreviewCanvas, DialogBoxBuilder.AsProjectShowsIt(content), sprites, ProjectSession.ResourcesProvider?.Invoke());
    }

    private void FrameHelp_Click(object sender, RoutedEventArgs e) => new DialogFrameGuideWindow { Owner = this }.ShowDialog();

    // ---------- Replies ----------

    private DialogChoice? SelectedReply => RepliesList.SelectedIndex >= 0 && RepliesList.SelectedIndex < _replies.Count ? _replies[RepliesList.SelectedIndex] : null;

    /// <summary>A reply's line in the list: a bright red pin when it's pinned, its words, and a note when it has a condition.</summary>
    private static TextBlock ReplyLine(DialogChoice reply)
    {
        var line = new TextBlock();
        if (reply.Pinned)
            line.Inlines.Add(new System.Windows.Documents.Run(PinGlyph + " ") { Foreground = PinBrush, FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol") });
        line.Inlines.Add(new System.Windows.Documents.Run(string.IsNullOrWhiteSpace(reply.Text) ? "(empty)" : reply.Text));
        if (reply.Condition is not null) line.Inlines.Add(new System.Windows.Documents.Run("   [only when...]") { Foreground = System.Windows.Media.Brushes.Gray });
        return line;
    }

    internal const string PinGlyph = "\U0001F4CC";
    internal static readonly System.Windows.Media.Brush PinBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 0, 0));

    private void RefreshReplies(int select = -1)
    {
        RepliesList.Items.Clear();
        foreach (var reply in _replies) RepliesList.Items.Add(ReplyLine(reply));
        if (select >= 0 && select < _replies.Count) RepliesList.SelectedIndex = select;
        UpdateReplyEditor();
        RefreshPreview();
    }

    private bool _updatingReplyEditor;

    private void UpdateReplyEditor()
    {
        _updatingReplyEditor = true;
        var reply = SelectedReply;
        ReplyTextBox.IsEnabled = ConditionButton.IsEnabled = PinButton.IsEnabled = reply is not null;
        PinButton.IsChecked = reply?.Pinned == true;
        ClearConditionButton.IsEnabled = reply?.Condition is not null;
        if (ReplyTextBox.Text != (reply?.Text ?? "")) ReplyTextBox.Text = reply?.Text ?? ""; // resetting it would jump the cursor to the start
        ConditionSummary.Text = reply is null ? "" : reply.Condition is null ? "Always offered." : DescribeCondition(reply.Condition);
        _updatingReplyEditor = false;
    }

    private static string DescribeCondition(BookEventConditions c)
    {
        var parts = new List<string>();
        if (c.TriggerConditionBased == BookTriggerConditionBased.ByHavingAnItem) parts.Add($"the party has {c.TriggerItemReference?.DisplayName ?? "(no item picked)"}");
        if (c.TriggerConditionBased == BookTriggerConditionBased.ByNotHavingAnItem) parts.Add($"the party doesn't have {c.TriggerItemReference?.DisplayName ?? "(no item picked)"}");
        if (c.ConditionInACondition) parts.Add($"{c.VariableConditions.Count} variable condition(s)");
        if (c.ConditionStatCondition) parts.Add("stat condition");
        if (c.ConditionComplex) parts.Add($"{c.ComplexConditions.Count} complex condition(s)");
        if (c.CycleCondition == CycleConditionType.Once) parts.Add("only until it's picked once");
        if (c.CycleCondition == CycleConditionType.SpecificNumberOfTimes) parts.Add($"until it's picked {c.CycleSpecificTimes} times");
        return parts.Count == 0 ? "Always offered." : "Offered only when: " + string.Join(", ", parts) + ".";
    }

    private void RepliesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingReplyEditor) return; // the list line is being redrawn while typing - don't touch the text box
        UpdateReplyEditor();
    }

    private void ReplyTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_updatingReplyEditor || SelectedReply is not { } reply) return;
        reply.Text = ReplyTextBox.Text;
        int index = RepliesList.SelectedIndex;
        _updatingReplyEditor = true;
        RepliesList.Items[index] = ReplyLine(reply);
        RepliesList.SelectedIndex = index;
        _updatingReplyEditor = false;
        RefreshPreview();
    }

    private void AddReply_Click(object sender, RoutedEventArgs e)
    {
        int id = _replies.Count == 0 ? 1 : _replies.Max(r => r.Id) + 1;
        _replies.Add(new DialogChoice { Id = id, Text = _replies.Count == 0 ? "name" : "New reply" });
        RefreshReplies(_replies.Count - 1);
        ReplyTextBox.Focus();
        ReplyTextBox.SelectAll();
    }

    private void DeleteReply_Click(object sender, RoutedEventArgs e)
    {
        int index = RepliesList.SelectedIndex;
        if (index < 0 || index >= _replies.Count) return;
        _replies.RemoveAt(index);
        RefreshReplies(Math.Min(index, _replies.Count - 1));
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e) => Move(-1);
    private void MoveDown_Click(object sender, RoutedEventArgs e) => Move(1);

    private void Move(int by)
    {
        int index = RepliesList.SelectedIndex, to = index + by;
        if (index < 0 || to < 0 || to >= _replies.Count) return;
        (_replies[index], _replies[to]) = (_replies[to], _replies[index]);
        RefreshReplies(to);
    }

    private void ConditionButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedReply is not { } reply) return;
        // Edit a copy: Cancel leaves the reply as it was.
        var working = reply.Condition is null
            ? new BookEventConditions { CycleCondition = CycleConditionType.Always }
            : JsonSerializer.Deserialize<BookEventConditions>(JsonSerializer.Serialize(reply.Condition))!;
        var control = new BookEventConditionsControl();
        control.SetDatabases(ProjectSession.BooksProvider?.Invoke() ?? new BookDatabase(), ProjectSession.ObjectsProvider?.Invoke() ?? new ObjectDatabase(),
            ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase(), ProjectSession.Sprites,
            ProjectSession.VariablesProvider?.Invoke() ?? new VariableDatabase(), ProjectSession.StatsProvider?.Invoke() ?? new StatDatabase());
        control.CurrentBookLocalVariables = new List<VariableDefinition>();
        control.ApplyReplyChoices();
        control.Conditions = working;

        var window = new Window
        {
            Title = $"Show \"{reply.Text}\" only when...",
            Owner = this, Width = 520, Height = 640, WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        var root = new DockPanel { Margin = new Thickness(10) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var ok = new Button { Content = "OK", Width = 70, Margin = new Thickness(4), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 70, Margin = new Thickness(4), IsCancel = true };
        ok.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        var note = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap, Opacity = 0.8, Margin = new Thickness(0, 0, 0, 6),
            Text = "The reply is in the list only while all of these pass, checked each time the box comes up. Trigger: the party (anyone in it, any bag) has or doesn't have an item. Cycle: how often it's offered - Once takes it out after it's picked (Ultima VII's remove-answer)."
        };
        DockPanel.SetDock(note, Dock.Top);
        root.Children.Add(note);
        root.Children.Add(control);
        window.Content = root;
        if (window.ShowDialog() != true) return;
        reply.Condition = working;
        RefreshReplies(RepliesList.SelectedIndex);
    }

    private void PinButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedReply is not { } reply) return;
        reply.Pinned = PinButton.IsChecked == true;
        RefreshReplies(RepliesList.SelectedIndex);
    }

    private void ClearConditionButton_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedReply is not { } reply) return;
        reply.Condition = null;
        RefreshReplies(RepliesList.SelectedIndex);
    }

    // ---------- OK ----------

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_replies.Any(r => string.IsNullOrWhiteSpace(r.Text)))
        {
            MessageBox.Show(this, "A reply has no text - type its words or delete it.", "Show Text", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var command = new EventCommand { Type = EventCommandType.ShowText };
        command.Parameters["Text"] = TextBox1.Text;
        if (!string.IsNullOrWhiteSpace(TitleBox.Text)) command.Parameters[DialogBoxBuilder.TitleKey] = TitleBox.Text.Trim();
        command.Parameters[DialogBoxBuilder.FrameKey] = FrameKey;
        if (DimCheck.IsChecked == true) command.Parameters[DialogBoxBuilder.DimKey] = "True";
        command.Parameters["Position"] = PositionText;
        command.Parameters["SpeakerId"] = (SpeakerCombo.SelectedItem as SpeakerChoice)?.Key ?? "";
        command.Parameters["FaceSide"] = FaceSideCombo.SelectedIndex == 1 ? "Right" : "Left";
        DialogChoices.Write(command, _replies);
        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
