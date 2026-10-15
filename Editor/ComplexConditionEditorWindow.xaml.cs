using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class ComplexConditionEditorWindow : Window
{
    private readonly ComplexConditionGroup _group;
    private readonly BookDatabase _books;
    private readonly ObjectDatabase _objects;
    private readonly ContainerDatabase _containers;
    private readonly SpriteDatabase _sprites;
    private readonly VariableDatabase _variables;
    private readonly StatDatabase _stats;
    private readonly List<VariableDefinition> _localVariables;
    private readonly int _currentBookId;
    private bool _suppress;

    public ComplexConditionEditorWindow(ComplexConditionGroup group, BookDatabase books, ObjectDatabase objects, ContainerDatabase containers, SpriteDatabase sprites,
        VariableDatabase variables, StatDatabase stats, List<VariableDefinition> localVariables, int currentBookId)
    {
        InitializeComponent();
        _group = group;
        _books = books;
        _objects = objects;
        _containers = containers;
        _sprites = sprites;
        _variables = variables;
        _stats = stats;
        _localVariables = localVariables;
        _currentBookId = currentBookId;

        _suppress = true;
        NameBox.Text = _group.Name;
        _suppress = false;

        RebuildSubConditionsList();
    }

    private static string Humanize(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)([A-Z])", " $1");

    private void NameBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress) return;
        _group.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _group.Name : NameBox.Text;
        Title = $"Complex Condition - {_group.Name}";
    }

    private void UpdateSummary()
    {
        if (_group.SubConditions.Count == 0)
        {
            SummaryText.Text = "No conditions added yet - click \"Add Condition...\" to start building this rule.";
            return;
        }
        var parts = new List<string>();
        for (int i = 0; i < _group.SubConditions.Count; i++)
        {
            var sub = _group.SubConditions[i];
            parts.Add((sub.Invert ? "NOT " : "") + Humanize(sub.Type.ToString()));
            if (i < _group.SubConditions.Count - 1)
                parts.Add(sub.NextOperator.ToString().ToUpperInvariant());
        }
        SummaryText.Text = string.Join(" ", parts);
    }

    // ---------- Add / reorder / delete sub-conditions ----------

    private void AddConditionButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (ComplexSubConditionType type in Enum.GetValues<ComplexSubConditionType>())
        {
            var item = new MenuItem { Header = Humanize(type.ToString()) };
            item.Click += (_, _) =>
            {
                _group.SubConditions.Add(new ComplexSubCondition { Type = type });
                RebuildSubConditionsList();
            };
            menu.Items.Add(item);
        }
        menu.PlacementTarget = AddConditionButton;
        menu.IsOpen = true;
    }

    private void RebuildSubConditionsList()
    {
        SubConditionsList.Children.Clear();
        for (int i = 0; i < _group.SubConditions.Count; i++)
            SubConditionsList.Children.Add(BuildSubConditionRow(_group.SubConditions[i], i));
        UpdateSummary();
    }

    private UIElement BuildSubConditionRow(ComplexSubCondition sub, int index)
    {
        var outer = new Border { BorderBrush = Brushes.LightGray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 8), Padding = new Thickness(6) };
        var stack = new StackPanel();

        // Header: delete X, type name, invert, reorder
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

        var deleteButton = new Button { Content = "X", Width = 20, Height = 20, Padding = new Thickness(0), Foreground = Brushes.White, Background = Brushes.Firebrick, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Delete this condition" };
        deleteButton.Click += (_, _) => { _group.SubConditions.Remove(sub); RebuildSubConditionsList(); };

        var typeLabel = new TextBlock { Text = Humanize(sub.Type.ToString()), FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center, Width = 160 };

        var invertCheck = new CheckBox { Content = "NOT", IsChecked = sub.Invert, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        invertCheck.Checked += (_, _) => { if (!_suppress) { sub.Invert = true; UpdateSummary(); } };
        invertCheck.Unchecked += (_, _) => { if (!_suppress) { sub.Invert = false; UpdateSummary(); } };

        var upButton = new Button { Content = "\u2191", Width = 24, IsEnabled = index > 0, Margin = new Thickness(0, 0, 2, 0) };
        upButton.Click += (_, _) =>
        {
            int i = _group.SubConditions.IndexOf(sub);
            (_group.SubConditions[i - 1], _group.SubConditions[i]) = (_group.SubConditions[i], _group.SubConditions[i - 1]);
            RebuildSubConditionsList();
        };
        var downButton = new Button { Content = "\u2193", Width = 24, IsEnabled = index < _group.SubConditions.Count - 1 };
        downButton.Click += (_, _) =>
        {
            int i = _group.SubConditions.IndexOf(sub);
            (_group.SubConditions[i + 1], _group.SubConditions[i]) = (_group.SubConditions[i], _group.SubConditions[i + 1]);
            RebuildSubConditionsList();
        };

        header.Children.Add(deleteButton);
        header.Children.Add(typeLabel);
        header.Children.Add(invertCheck);
        header.Children.Add(upButton);
        header.Children.Add(downButton);
        stack.Children.Add(header);

        // Type-specific mini-editor
        UIElement editor = sub.Type switch
        {
            ComplexSubConditionType.InACondition => BuildInAConditionEditor(sub),
            ComplexSubConditionType.EventTriggerCompleted => BuildEventCompletedEditor(sub),
            ComplexSubConditionType.StatCondition => BuildStatEditor(sub),
            ComplexSubConditionType.CombineItemCondition => BuildCombineItemEditor(sub),
            ComplexSubConditionType.DropToSelfCondition => BuildDropToSelfEditor(sub),
            ComplexSubConditionType.JoinedPartyCondition => BuildJoinedPartyEditor(sub),
            ComplexSubConditionType.GameCondition => BuildGameConditionEditor(sub),
            ComplexSubConditionType.Always or ComplexSubConditionType.ByTradeItem or ComplexSubConditionType.ByUseItem or
                ComplexSubConditionType.ByDropItem or ComplexSubConditionType.ByDestroyItem or ComplexSubConditionType.ByAfterReadItem or
                ComplexSubConditionType.ByPickupItem or ComplexSubConditionType.ByHavingAnItem or ComplexSubConditionType.ByNotHavingAnItem
                => BuildTriggerTypeEditor(sub),
            _ => new TextBlock { Text = "(unknown type)" }
        };
        stack.Children.Add(editor);

        // Connector to the next sub-condition
        if (index < _group.SubConditions.Count - 1)
        {
            var connectorRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            connectorRow.Children.Add(new TextBlock { Text = "Connects to next with:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var opCombo = new ComboBox { Width = 70, ItemsSource = Enum.GetValues<ComplexLogicOperator>(), SelectedItem = sub.NextOperator };
            opCombo.SelectionChanged += (_, _) => { if (!_suppress && opCombo.SelectedItem is ComplexLogicOperator o) { sub.NextOperator = o; UpdateSummary(); } };
            connectorRow.Children.Add(opCombo);
            stack.Children.Add(connectorRow);
        }

        outer.Child = stack;
        return outer;
    }

    // ---------- Per-type mini-editors ----------

    /// <summary>
    /// Shared editor for the trigger-based sub-condition types (the same
    /// ones the top-level Trigger Conditions Based dropdown offers, now
    /// also selectable inside a Complex Condition). All nine are genuinely
    /// active - Trade/Destroy/Having/Not-Having get the same Select Item
    /// picker Drop/Pickup already use, and are simulated the same way via
    /// the Debug window; Always/Use/After Read need no extra fields since
    /// they're inherently about the book itself.
    /// </summary>
    private UIElement BuildTriggerTypeEditor(ComplexSubCondition sub)
    {
        var panel = new StackPanel();

        if (sub.Type is ComplexSubConditionType.Always or ComplexSubConditionType.ByUseItem or ComplexSubConditionType.ByAfterReadItem)
        {
            panel.Children.Add(new TextBlock
            {
                Text = "No extra settings - this checks the book itself, same as the top-level Trigger Conditions Based dropdown.",
                FontStyle = FontStyles.Italic,
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap
            });
            return panel;
        }

        // Drop/Pickup/Trade/Destroy/Having/Not-Having: needs a Select Item target, exactly like the top-level condition.
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var label = new TextBlock
        {
            Text = "Select Item: " + (string.IsNullOrEmpty(sub.TriggerItemReference?.DisplayName) ? "(none)" : sub.TriggerItemReference!.DisplayName),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var pickButton = new Button { Content = "...", Width = 30 };
        pickButton.Click += (_, _) =>
        {
            var picker = new SelectItemPickerWindow(_books, _objects, _containers, _sprites, sub.TriggerItemReference) { Owner = this };
            if (picker.ShowDialog() == true)
            {
                sub.TriggerItemReference = picker.Result;
                label.Text = "Select Item: " + (string.IsNullOrEmpty(sub.TriggerItemReference?.DisplayName) ? "(none)" : sub.TriggerItemReference!.DisplayName);
            }
        };
        row.Children.Add(label);
        row.Children.Add(pickButton);
        panel.Children.Add(row);
        return panel;
    }

    private UIElement BuildInAConditionEditor(ComplexSubCondition sub)
    {
        var entry = sub.InACondition;
        var line1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        var line2 = new StackPanel { Orientation = Orientation.Horizontal };

        var modeCombo = new ComboBox { Width = 80, ItemsSource = Enum.GetValues<VariableConditionMode>(), SelectedItem = entry.Mode, Margin = new Thickness(0, 0, 6, 0) };
        var varNameLabel = new TextBlock { Text = string.IsNullOrEmpty(entry.VariableDisplayName) ? "(none)" : entry.VariableDisplayName, Width = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var pickVarButton = new Button { Content = "...", Width = 26 };

        var comparisonCombo = new ComboBox { Width = 90, ItemsSource = BuildComparisonChoices(), Margin = new Thickness(0, 0, 6, 0) };
        SelectComparison(comparisonCombo, entry.Comparison);
        var valueContainer = new ContentControl();

        void RebuildValueControl()
        {
            if (entry.Mode == VariableConditionMode.Boolean)
            {
                var boolCombo = new ComboBox { Width = 70, ItemsSource = new[] { "True", "False" }, SelectedItem = string.IsNullOrEmpty(entry.Value) ? "True" : entry.Value };
                boolCombo.SelectionChanged += (_, _) => { if (!_suppress) entry.Value = boolCombo.SelectedItem as string ?? "True"; };
                valueContainer.Content = boolCombo;
            }
            else
            {
                var textBox = new TextBox { Width = entry.Mode == VariableConditionMode.String ? 180 : 80, Text = entry.Value };
                textBox.TextChanged += (_, _) => { if (!_suppress) entry.Value = textBox.Text; };
                valueContainer.Content = textBox;
            }
        }
        RebuildValueControl();

        modeCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress || modeCombo.SelectedItem is not VariableConditionMode m) return;
            entry.Mode = m;
            entry.VariableId = -1;
            entry.VariableDisplayName = string.Empty;
            varNameLabel.Text = "(none)";
            RebuildValueControl();
        };
        comparisonCombo.SelectionChanged += (_, _) => { if (!_suppress && comparisonCombo.SelectedItem is ComparisonChoice c) entry.Comparison = c.Value; };

        pickVarButton.Click += (_, _) =>
        {
            var allowedTypes = entry.Mode switch
            {
                VariableConditionMode.String => new[] { VariableValueType.String },
                VariableConditionMode.Boolean => new[] { VariableValueType.Boolean },
                _ => new[] { VariableValueType.Integer, VariableValueType.Float, VariableValueType.Decimal }
            };
            var picker = new SelectVariableWindow(_localVariables, _variables, allowedTypes, entry.Scope, entry.VariableId >= 0 ? entry.VariableId : null) { Owner = this };
            if (picker.ShowDialog() == true && picker.Result is not null)
            {
                entry.VariableId = picker.Result.Id;
                entry.VariableDisplayName = picker.Result.Name;
                entry.Scope = picker.ResultScope;
                varNameLabel.Text = picker.Result.Name;
            }
        };

        line1.Children.Add(modeCombo);
        line1.Children.Add(varNameLabel);
        line1.Children.Add(pickVarButton);
        line2.Children.Add(comparisonCombo);
        line2.Children.Add(valueContainer);

        var container = new StackPanel();
        container.Children.Add(line1);
        container.Children.Add(line2);
        return container;
    }

    private UIElement BuildEventCompletedEditor(ComplexSubCondition sub)
    {
        var container = new StackPanel();
        var choices = BuildEventChoices();

        var eventCombo = new ComboBox { ItemsSource = choices.Select(c => c.DisplayName).ToList(), Margin = new Thickness(0, 0, 0, 4) };
        if (sub.EventCompletedReference is { } current)
        {
            var match = choices.FirstOrDefault(c => c.BookId == current.BookId && c.PageIndex == current.PageIndex);
            if (match is not null) eventCombo.SelectedItem = match.DisplayName;
        }
        eventCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress) return;
            int idx = eventCombo.SelectedIndex;
            sub.EventCompletedReference = idx >= 0 && idx < choices.Count ? choices[idx] : null;
        };

        var stateCombo = new ComboBox { ItemsSource = new[] { "True", "False" }, SelectedItem = sub.EventCompletedExpectedState ? "True" : "False" };
        stateCombo.SelectionChanged += (_, _) => { if (!_suppress) sub.EventCompletedExpectedState = stateCombo.SelectedItem as string == "True"; };

        container.Children.Add(eventCombo);
        container.Children.Add(stateCombo);
        return container;
    }

    private List<BookEventPageReference> BuildEventChoices()
    {
        var result = new List<BookEventPageReference>();
        foreach (var book in _books.Books)
        {
            if (book.Id == _currentBookId) continue;
            for (int i = 0; i < book.EventPages.Count; i++)
                result.Add(new BookEventPageReference { BookId = book.Id, PageIndex = i, DisplayName = $"{book.Name} - Page {i + 1}" });
        }
        return result;
    }

    private UIElement BuildStatEditor(ComplexSubCondition sub)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var entry = sub.StatCondition;

        var statCombo = new ComboBox { Width = 100, ItemsSource = _stats.Stats.Select(s => s.Name).ToList(), Margin = new Thickness(0, 0, 6, 0) };
        var currentStat = _stats.Stats.FirstOrDefault(s => s.Id == entry.StatId);
        if (currentStat is not null) statCombo.SelectedItem = currentStat.Name;
        statCombo.SelectionChanged += (_, _) =>
        {
            if (_suppress) return;
            var picked = _stats.Stats.FirstOrDefault(s => s.Name == statCombo.SelectedItem as string);
            if (picked is not null) entry.StatId = picked.Id;
        };

        var comparisonCombo = new ComboBox { Width = 90, ItemsSource = BuildComparisonChoices(), Margin = new Thickness(0, 0, 6, 0) };
        SelectComparison(comparisonCombo, entry.Comparison);
        comparisonCombo.SelectionChanged += (_, _) => { if (!_suppress && comparisonCombo.SelectedItem is ComparisonChoice c) entry.Comparison = c.Value; };

        var valueBox = new TextBox { Width = 60, Text = entry.Value.ToString() };
        valueBox.TextChanged += (_, _) => { if (!_suppress && int.TryParse(valueBox.Text, out int v)) entry.Value = v; };

        row.Children.Add(statCombo);
        row.Children.Add(comparisonCombo);
        row.Children.Add(valueBox);
        return row;
    }

    private UIElement BuildCombineItemEditor(ComplexSubCondition sub)
    {
        var container = new StackPanel();
        container.Children.Add(BuildItemSlot(sub.CombineItemSlot1, sub.CombineItemSlot2, "Item 1:"));
        container.Children.Add(BuildItemSlot(sub.CombineItemSlot2, sub.CombineItemSlot1, "Item 2:"));
        return container;
    }

    private UIElement BuildItemSlot(CombineItemSlot slot, CombineItemSlot other, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 45 });

        var itemLabel = new TextBlock { Text = slot.IsSelf ? "(Self)" : (slot.Item?.DisplayName ?? "(none)"), VerticalAlignment = VerticalAlignment.Center, Width = 100, Margin = new Thickness(0, 0, 6, 0) };
        var pickButton = new Button { Content = "...", Width = 26, IsEnabled = !slot.IsSelf, Margin = new Thickness(0, 0, 10, 0) };
        pickButton.Click += (_, _) =>
        {
            var picker = new SelectItemPickerWindow(_books, _objects, _containers, _sprites, slot.Item) { Owner = this };
            if (picker.ShowDialog() == true) { slot.Item = picker.Result; itemLabel.Text = slot.Item?.DisplayName ?? "(none)"; }
        };

        var selfCheck = new CheckBox { Content = "Self", IsChecked = slot.IsSelf, VerticalAlignment = VerticalAlignment.Center };
        selfCheck.Checked += (_, _) =>
        {
            if (_suppress) return;
            if (other.IsSelf) { selfCheck.IsChecked = false; MessageBox.Show("Only one item can be marked Self.", "Combine Item Condition", MessageBoxButton.OK, MessageBoxImage.Information); return; }
            slot.IsSelf = true; pickButton.IsEnabled = false; itemLabel.Text = "(Self)";
        };
        selfCheck.Unchecked += (_, _) => { if (_suppress) return; slot.IsSelf = false; pickButton.IsEnabled = true; itemLabel.Text = slot.Item?.DisplayName ?? "(none)"; };

        panel.Children.Add(itemLabel);
        panel.Children.Add(pickButton);
        panel.Children.Add(selfCheck);
        return panel;
    }

    private UIElement BuildDropToSelfEditor(ComplexSubCondition sub)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var itemLabel = new TextBlock { Text = sub.DropToSelfItemReference?.DisplayName ?? "(none)", VerticalAlignment = VerticalAlignment.Center, Width = 120, Margin = new Thickness(0, 0, 6, 0) };
        var pickButton = new Button { Content = "...", Width = 26 };
        pickButton.Click += (_, _) =>
        {
            var picker = new SelectItemPickerWindow(_books, _objects, _containers, _sprites, sub.DropToSelfItemReference) { Owner = this };
            if (picker.ShowDialog() == true) { sub.DropToSelfItemReference = picker.Result; itemLabel.Text = sub.DropToSelfItemReference?.DisplayName ?? "(none)"; }
        };
        row.Children.Add(new TextBlock { Text = "Selected Item: ", VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(itemLabel);
        row.Children.Add(pickButton);
        return row;
    }

    /// <summary>Any game condition (the Advance IF list): its kind, then its own settings.</summary>
    private UIElement BuildGameConditionEditor(ComplexSubCondition sub)
    {
        var host = new StackPanel();
        void Fill()
        {
            host.Children.Clear();
            var kinds = GameConditions.All.Select(k => $"{(int)k}. {GameConditions.Name(k)}").ToList();
            var kind = new ComboBox { Width = 320, ItemsSource = kinds, SelectedIndex = Array.IndexOf(GameConditions.All, sub.Game.Kind), HorizontalAlignment = HorizontalAlignment.Left };
            kind.SelectionChanged += (_, _) => { if (kind.SelectedIndex >= 0 && GameConditions.All[kind.SelectedIndex] != sub.Game.Kind) { sub.Game.Kind = GameConditions.All[kind.SelectedIndex]; Fill(); UpdateSummary(); } };
            host.Children.Add(kind);
            host.Children.Add(GameConditionFields.Build(sub.Game, this));
        }
        Fill();
        return host;
    }

    private UIElement BuildJoinedPartyEditor(ComplexSubCondition sub)
    {
        // No NPC Editor/database exists yet - honestly empty, same as the top-level equivalent.
        return new ComboBox { ItemsSource = Array.Empty<string>() };
    }

    // ---------- Comparison symbol helper (mirrors BookEventConditionsControl's) ----------

    private sealed class ComparisonChoice
    {
        public ConditionComparisonOperator Value { get; }
        private readonly string _display;
        public ComparisonChoice(ConditionComparisonOperator value)
        {
            Value = value;
            string symbol = value switch
            {
                ConditionComparisonOperator.Equal => "=",
                ConditionComparisonOperator.NotEqual => "!=",
                ConditionComparisonOperator.GreaterThan => ">",
                ConditionComparisonOperator.GreaterOrEqual => ">=",
                ConditionComparisonOperator.LessThan => "<",
                ConditionComparisonOperator.LessOrEqual => "<=",
                _ => ""
            };
            _display = $"{Humanize(value.ToString())} ({symbol})";
        }
        public override string ToString() => _display;
    }

    private static List<ComparisonChoice> BuildComparisonChoices() =>
        Enum.GetValues<ConditionComparisonOperator>().Select(v => new ComparisonChoice(v)).ToList();

    private static void SelectComparison(ComboBox combo, ConditionComparisonOperator value)
    {
        foreach (ComparisonChoice item in combo.Items)
            if (item.Value == value) { combo.SelectedItem = item; return; }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();
}
