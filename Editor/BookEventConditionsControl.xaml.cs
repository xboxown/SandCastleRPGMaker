using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class BookEventConditionsControl : UserControl
{
    private BookEventConditions? _conditions;
    private bool _suppress;

    private BookDatabase? _books;
    private ObjectDatabase? _objects;
    private ContainerDatabase? _containers;
    private SpriteDatabase? _sprites;
    private VariableDatabase? _variables;
    private StatDatabase? _stats;

    /// <summary>Which book this control is currently editing conditions for - used to exclude that book's own events from the "Event Trigger Completed" list.</summary>
    public int CurrentBookId { get; set; } = -1;

    /// <summary>The current book's own local variables (Local tab of the variable picker) - set alongside CurrentBookId whenever the selected book changes.</summary>
    public List<VariableDefinition>? CurrentBookLocalVariables { get; set; }

    /// <summary>Wraps an enum value with a human-readable display string, without changing the underlying value or dropdown behavior at all.</summary>
    private sealed class EnumChoice<T> where T : struct, Enum
    {
        public T Value { get; }
        private readonly string _display;
        public EnumChoice(T value, string? suffix = null)
        {
            Value = value;
            _display = Humanize(value.ToString()) + (suffix is not null ? $" {suffix}" : string.Empty);
        }
        private EnumChoice(T value, string explicitDisplay, bool _) { Value = value; _display = explicitDisplay; }
        /// <summary>Real, explicit label override, per spec - the underlying stored Value never changes, only how it reads in the dropdown; used for a container's own real, different meaning of the exact same shared trigger values Book already uses (ByAfterReadItem, ByEnter, ByStay, ByExit), so Book's own dropdown is completely untouched by this.</summary>
        public static EnumChoice<T> WithExplicitLabel(T value, string label) => new(value, label, true);
        public override string ToString() => _display;
    }

    /// <summary>Inserts a space before each internal capital letter - "ByTradeItem" becomes "By Trade Item".</summary>
    private static string Humanize(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, "(?<!^)([A-Z])", " $1");

    private static List<EnumChoice<T>> BuildChoices<T>(Func<T, string?>? suffixFor = null) where T : struct, Enum =>
        Enum.GetValues<T>().Select(v => new EnumChoice<T>(v, suffixFor?.Invoke(v))).ToList();

    /// <summary>
    /// Per the design doc: Trade/Destroy/Having/Not-Having genuinely have
    /// no mechanics to fire on yet (no NPC trading, no destruction
    /// simulation, no inventory model) - labeled honestly in the dropdown
    /// itself rather than silently letting someone pick a trigger that
    /// will never fire. Use/Drop/Pickup are real (see BookPreviewWindow);
    /// Always and After Read Item were already real before this.
    /// </summary>
    private static string? TriggerInProgressSuffix(BookTriggerConditionBased v) => null;

    /// <summary>Comparison operators shown with their symbol - "Equal" alone reads as noise in a narrow panel, "Equal (=)" is unambiguous.</summary>
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

    public BookEventConditionsControl()
    {
        InitializeComponent();
        TriggerConditionBasedCombo.ItemsSource = BuildChoices<BookTriggerConditionBased>(TriggerInProgressSuffix).Where(c => !CharacterTriggers.IsCharacterTrigger(c.Value)).ToList();
        CycleConditionCombo.ItemsSource = BuildChoices<CycleConditionType>();
        EventCompletedStateCombo.ItemsSource = new[] { "True", "False" };
    }

    /// <summary>
    /// Removes specific trigger types from the dropdown entirely - for a
    /// tile, "By After Read Item" doesn't apply (a tile isn't a readable
    /// book), per spec ("remove by read... for the tile custom part... we
    /// don't need it"). Book/Map events don't call this, so their own
    /// dropdown stays exactly as it was.
    /// </summary>
    public void ExcludeTriggerTypes(params BookTriggerConditionBased[] excluded)
    {
        var current = Conditions?.TriggerConditionBased;
        TriggerConditionBasedCombo.ItemsSource = BuildChoices<BookTriggerConditionBased>(TriggerInProgressSuffix)
            .Where(c => !excluded.Contains(c.Value) && !CharacterTriggers.IsCharacterTrigger(c.Value))
            .ToList();
        if (current.HasValue)
        {
            foreach (EnumChoice<BookTriggerConditionBased> item in TriggerConditionBasedCombo.Items)
                if (item.Value.Equals(current.Value)) { TriggerConditionBasedCombo.SelectedItem = item; break; }
        }
    }

    /// <summary>
    /// Real, container-specific relabeling, per spec - these 4 shared
    /// trigger values genuinely mean something different for a container
    /// than for a book (By After Read Item -&gt; By After Open Container,
    /// By Enter -&gt; By After Close Container, By Stay -&gt; By After
    /// Picklock Container, By Exit -&gt; By Failed Picklock Container),
    /// but the underlying stored BookTriggerConditionBased value is
    /// completely unchanged - only how it reads here. Book/Map/Tile
    /// events never call this, so their own dropdowns are untouched.
    /// </summary>
    /// <summary>An object type's own names for some triggers (Object Editor > Event): e.g. a tile's By Enter is "By Step On". Character-only triggers are left out, as for books and containers.</summary>
    public void ApplyTriggerLabels(IReadOnlyDictionary<BookTriggerConditionBased, string> relabels)
    {
        var current = Conditions?.TriggerConditionBased;
        TriggerConditionBasedCombo.ItemsSource = Enum.GetValues<BookTriggerConditionBased>()
            .Where(v => !CharacterTriggers.IsCharacterTrigger(v))
            .Select(v => relabels.TryGetValue(v, out string? label) ? EnumChoice<BookTriggerConditionBased>.WithExplicitLabel(v, label) : new EnumChoice<BookTriggerConditionBased>(v, TriggerInProgressSuffix(v)))
            .ToList();
        if (current.HasValue)
        {
            foreach (EnumChoice<BookTriggerConditionBased> item in TriggerConditionBasedCombo.Items)
                if (item.Value.Equals(current.Value)) { TriggerConditionBasedCombo.SelectedItem = item; break; }
        }
    }

    public void ApplyContainerTriggerLabels()
    {
        var current = Conditions?.TriggerConditionBased;
        var relabels = new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByAfterReadItem] = "By After Open Container",
            [BookTriggerConditionBased.ByEnter] = "By After Close Container",
            [BookTriggerConditionBased.ByStay] = "By After Picklock Container",
            [BookTriggerConditionBased.ByExit] = "By Failed Picklock Container"
        };
        TriggerConditionBasedCombo.ItemsSource = Enum.GetValues<BookTriggerConditionBased>()
            .Where(v => !CharacterTriggers.IsCharacterTrigger(v))
            .Select(v => relabels.TryGetValue(v, out string? label) ? EnumChoice<BookTriggerConditionBased>.WithExplicitLabel(v, label) : new EnumChoice<BookTriggerConditionBased>(v, TriggerInProgressSuffix(v)))
            .ToList();
        if (current.HasValue)
        {
            foreach (EnumChoice<BookTriggerConditionBased> item in TriggerConditionBasedCombo.Items)
                if (item.Value.Equals(current.Value)) { TriggerConditionBasedCombo.SelectedItem = item; break; }
        }
    }

    /// <summary>
    /// The Character Editor's own trigger list, and nothing else: By Equip,
    /// By UnEquip, By Hit, By Ailment Effect, By Death. Book, container, map
    /// and tile events never call this, so their lists are unchanged.
    /// </summary>
    public void ApplyCharacterTriggerChoices()
    {
        var current = Conditions?.TriggerConditionBased;
        var labels = new Dictionary<BookTriggerConditionBased, string>
        {
            [BookTriggerConditionBased.ByEquip] = "By Equip",
            [BookTriggerConditionBased.ByUnEquip] = "By UnEquip",
            [BookTriggerConditionBased.ByHit] = "By Hit",
            [BookTriggerConditionBased.ByAilmentEffect] = "By Ailment Effect",
            [BookTriggerConditionBased.ByDeath] = "By Death"
        };
        TriggerConditionBasedCombo.ItemsSource = CharacterTriggers.All.Select(v => EnumChoice<BookTriggerConditionBased>.WithExplicitLabel(v, labels[v])).ToList();
        if (current.HasValue)
        {
            foreach (EnumChoice<BookTriggerConditionBased> item in TriggerConditionBasedCombo.Items)
                if (item.Value.Equals(current.Value)) { TriggerConditionBasedCombo.SelectedItem = item; break; }
        }
    }

    /// <summary>Wires the databases the picker sub-editors need - call once when the control is created.</summary>
    public void SetDatabases(BookDatabase books, ObjectDatabase objects, ContainerDatabase containers, SpriteDatabase sprites, VariableDatabase variables, StatDatabase stats)
    {
        _books = books;
        _objects = objects;
        _containers = containers;
        _sprites = sprites;
        _variables = variables;
        _stats = stats;
    }

    /// <summary>The conditions this control is editing - set this when the selected event page changes.</summary>
    public BookEventConditions? Conditions
    {
        get => _conditions;
        set
        {
            _conditions = value;
            LoadFromModel();
        }
    }

    private void LoadFromModel()
    {
        _suppress = true;
        if (_conditions is null)
        {
            IsEnabled = false;
        }
        else
        {
            IsEnabled = true;
            SelectValue(TriggerConditionBasedCombo, _conditions.TriggerConditionBased);
            SelectValue(CycleConditionCombo, _conditions.CycleCondition);
            CycleTimesBox.Text = _conditions.CycleSpecificTimes.ToString();

            InACondCheck.IsChecked = _conditions.ConditionInACondition;
            EventCompletedCheck.IsChecked = _conditions.ConditionEventTriggerCompleted;
            StatCheck.IsChecked = _conditions.ConditionStatCondition;
            CombineItemCheck.IsChecked = _conditions.ConditionCombineItem;
            DropToSelfCheck.IsChecked = _conditions.ConditionDropToSelf;
            JoinedPartyCheck.IsChecked = _conditions.ConditionJoinedParty;
            DistanceCheck.IsChecked = _conditions.ConditionDistance;
            DistanceSlider.Value = Math.Clamp(_conditions.DistanceValue, 0, 4_000_000_000);
            DistanceValueBox.Text = _conditions.DistanceValue.ToString("0.00");
            ComplexCheck.IsChecked = _conditions.ConditionComplex;

            RebuildVariableConditionsList();
            RebuildStatRows();
            RebuildCombineItemSlots();
            RebuildEventCompletedCombo();
            RebuildJoinedPartyCombo();
            RebuildComplexGroupsList();
            UpdateDropToSelfItemLabel();

            UpdateSelectedItemLabel();
            UpdateSelectItemRowVisibility();
            UpdateConditionPanelsVisibility();
            UpdateVisibility();
        }
        _suppress = false;
    }

    private static void SelectValue<T>(ComboBox combo, T value) where T : struct, Enum
    {
        foreach (EnumChoice<T> item in combo.Items)
        {
            if (EqualityComparer<T>.Default.Equals(item.Value, value)) { combo.SelectedItem = item; return; }
        }
    }

    private void UpdateVisibility()
    {
        CycleTimesPanel.Visibility = _conditions?.CycleCondition == CycleConditionType.SpecificNumberOfTimes
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateConditionPanelsVisibility()
    {
        InACondPanel.Visibility = InACondCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        EventCompletedPanel.Visibility = EventCompletedCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        StatPanel.Visibility = StatCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        CombineItemPanel.Visibility = CombineItemCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DropToSelfPanel.Visibility = DropToSelfCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        JoinedPartyPanel.Visibility = JoinedPartyCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        DistancePanel.Visibility = DistanceCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ComplexPanel.Visibility = ComplexCheck.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ConditionCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress || _conditions is null) return;
        _conditions.ConditionInACondition = InACondCheck.IsChecked == true;
        _conditions.ConditionEventTriggerCompleted = EventCompletedCheck.IsChecked == true;
        _conditions.ConditionStatCondition = StatCheck.IsChecked == true;
        _conditions.ConditionCombineItem = CombineItemCheck.IsChecked == true;
        _conditions.ConditionDropToSelf = DropToSelfCheck.IsChecked == true;
        _conditions.ConditionJoinedParty = JoinedPartyCheck.IsChecked == true;
        _conditions.ConditionDistance = DistanceCheck.IsChecked == true;
        _conditions.ConditionComplex = ComplexCheck.IsChecked == true;
        UpdateConditionPanelsVisibility();
    }

    private void TriggerConditionBasedCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _conditions is null) return;
        if (TriggerConditionBasedCombo.SelectedItem is EnumChoice<BookTriggerConditionBased> v) _conditions.TriggerConditionBased = v.Value;
        UpdateSelectItemRowVisibility();
        WarnIfAlwaysFiresWithNoRestriction();
    }

    private void CycleConditionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _conditions is null) return;
        if (CycleConditionCombo.SelectedItem is EnumChoice<CycleConditionType> v) _conditions.CycleCondition = v.Value;
        UpdateVisibility();
        WarnIfAlwaysFiresWithNoRestriction();
    }

    /// <summary>
    /// Trigger=Always plus Cycle=Always plus every other condition
    /// unchecked means this page fires on every eligible moment with
    /// absolutely nothing to stop it - a real way to accidentally spam a
    /// game with whatever commands are on the page. Only warns on a
    /// user-driven change (guarded by the _suppress check in both callers)
    /// so loading an existing book that already has this combination
    /// doesn't nag on open.
    /// </summary>
    private void WarnIfAlwaysFiresWithNoRestriction()
    {
        if (_conditions is null) return;
        bool noOtherConditions = !_conditions.ConditionInACondition && !_conditions.ConditionEventTriggerCompleted &&
            !_conditions.ConditionStatCondition && !_conditions.ConditionCombineItem && !_conditions.ConditionDropToSelf &&
            !_conditions.ConditionJoinedParty && !_conditions.ConditionDistance && !_conditions.ConditionComplex;

        if (_conditions.TriggerConditionBased == BookTriggerConditionBased.Always &&
            _conditions.CycleCondition == CycleConditionType.Always &&
            noOtherConditions)
        {
            MessageBox.Show(
                "Trigger is Always, Cycle is Always, and no other condition is checked - this page will fire continuously, every eligible moment, with nothing to stop it. That can spam the game with whatever commands are on this page and cause real performance problems.",
                "This Page Will Always Fire", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CycleTimesBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _conditions is null) return;
        if (int.TryParse(CycleTimesBox.Text, out int n) && n > 0) _conditions.CycleSpecificTimes = n;
    }

    // ---------- Checkmark 8: Distance Condition ----------

    private void DistanceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_suppress || _conditions is null) return;
        _conditions.DistanceValue = DistanceSlider.Value;
        _suppress = true;
        DistanceValueBox.Text = DistanceSlider.Value.ToString("0.00");
        _suppress = false;
    }

    private void DistanceValueBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppress || _conditions is null) return;
        if (!double.TryParse(DistanceValueBox.Text, out double v)) return;

        v = Math.Clamp(v, 0, 4_000_000_000);
        _conditions.DistanceValue = v;
        _suppress = true;
        DistanceSlider.Value = v; // no-op once already synced from the slider itself, but keeps a typed value visible on the slider too
        _suppress = false;
    }

    // ---------- Select Item (appears under Trigger Conditions Based, hidden for Always) ----------

    private void UpdateSelectedItemLabel()
    {
        SelectedItemLabel.Text = _conditions?.TriggerItemReference is { } r
            ? $"Select Item: {r.DisplayName}"
            : "Select Item: (none)";
    }

    private void UpdateSelectItemRowVisibility()
    {
        bool show = _conditions is not null && _conditions.TriggerConditionBased != BookTriggerConditionBased.Always;
        SelectedItemLabel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        SelectItemButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SelectItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (_conditions is null || _books is null || _objects is null || _sprites is null) return;

        var picker = new SelectItemPickerWindow(_books, _objects, _containers, _sprites, _conditions.TriggerItemReference)
        {
            Owner = Window.GetWindow(this)
        };
        if (picker.ShowDialog() == true)
        {
            _conditions.TriggerItemReference = picker.Result;
            UpdateSelectedItemLabel();
        }
    }

    // ---------- Checkmark 5: Drop To Self Condition ----------

    private void UpdateDropToSelfItemLabel()
    {
        DropToSelfItemLabel.Text = _conditions?.DropToSelfItemReference is { } r
            ? $"Selected Item: {r.DisplayName}"
            : "Selected Item: (none)";
    }

    private void DropToSelfItemButton_Click(object sender, RoutedEventArgs e)
    {
        if (_conditions is null || _books is null || _objects is null || _sprites is null) return;

        var picker = new SelectItemPickerWindow(_books, _objects, _containers, _sprites, _conditions.DropToSelfItemReference)
        {
            Owner = Window.GetWindow(this)
        };
        if (picker.ShowDialog() == true)
        {
            _conditions.DropToSelfItemReference = picker.Result;
            UpdateDropToSelfItemLabel();
        }
    }

    // ---------- Checkmark 1: In A Condition ----------

    private void RebuildVariableConditionsList()
    {
        VariableConditionsList.Children.Clear();
        if (_conditions is null) return;
        foreach (var entry in _conditions.VariableConditions)
            VariableConditionsList.Children.Add(BuildVariableConditionRow(entry));
    }

    private UIElement BuildVariableConditionRow(VariableConditionEntry entry)
    {
        var outer = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };

        var line1 = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };
        var line2 = new StackPanel { Orientation = Orientation.Horizontal };

        var deleteButton = new Button
        {
            Content = "X",
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Foreground = Brushes.White,
            Background = Brushes.Firebrick,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Delete this condition"
        };
        deleteButton.Click += (_, _) =>
        {
            _conditions?.VariableConditions.Remove(entry);
            RebuildVariableConditionsList();
        };

        var modeCombo = new ComboBox { Width = 80, ItemsSource = Enum.GetValues<VariableConditionMode>(), SelectedItem = entry.Mode, Margin = new Thickness(0, 0, 6, 0) };
        var varNameLabel = new TextBlock { Text = string.IsNullOrEmpty(entry.VariableDisplayName) ? "(none)" : entry.VariableDisplayName, Width = 70, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        var pickVarButton = new Button { Content = "...", Width = 26 };

        var comparisonCombo = new ComboBox { Width = 90, ItemsSource = BuildComparisonChoices(), Margin = new Thickness(0, 0, 6, 0) };
        SelectComparison(comparisonCombo, entry.Comparison);
        var valueContainer = new ContentControl { Margin = new Thickness(0, 0, 6, 0) };
        var operatorCombo = new ComboBox { Width = 55, ItemsSource = Enum.GetValues<ConditionLogicOperator>(), SelectedItem = entry.NextOperator };

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
                var textBox = new TextBox { Width = entry.Mode == VariableConditionMode.String ? 140 : 70, Text = entry.Value };
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
        operatorCombo.SelectionChanged += (_, _) => { if (!_suppress && operatorCombo.SelectedItem is ConditionLogicOperator o) entry.NextOperator = o; };

        pickVarButton.Click += (_, _) =>
        {
            if (_variables is null) return;
            var allowedTypes = entry.Mode switch
            {
                VariableConditionMode.String => new[] { VariableValueType.String },
                VariableConditionMode.Boolean => new[] { VariableValueType.Boolean },
                _ => new[] { VariableValueType.Integer, VariableValueType.Float, VariableValueType.Decimal }
            };
            var picker = new SelectVariableWindow(CurrentBookLocalVariables ?? new List<VariableDefinition>(), _variables, allowedTypes, entry.Scope, entry.VariableId >= 0 ? entry.VariableId : null)
            {
                Owner = Window.GetWindow(this)
            };
            if (picker.ShowDialog() == true && picker.Result is not null)
            {
                entry.VariableId = picker.Result.Id;
                entry.VariableDisplayName = picker.Result.Name;
                entry.Scope = picker.ResultScope;
                varNameLabel.Text = picker.Result.Name;
            }
        };

        line1.Children.Add(deleteButton);
        line1.Children.Add(modeCombo);
        line1.Children.Add(varNameLabel);
        line1.Children.Add(pickVarButton);

        line2.Children.Add(comparisonCombo);
        line2.Children.Add(valueContainer);
        line2.Children.Add(operatorCombo);

        outer.Children.Add(line1);
        outer.Children.Add(line2);
        return outer;
    }

    private void AddVariableConditionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_conditions is null) return;
        _conditions.VariableConditions.Add(new VariableConditionEntry());
        RebuildVariableConditionsList();
    }

    // ---------- Checkmark 2: Event Trigger Completed ----------

    private List<BookEventPageReference> BuildEventChoices()
    {
        var result = new List<BookEventPageReference>();
        if (_books is null) return result;
        foreach (var book in _books.Books)
        {
            if (book.Id == CurrentBookId) continue; // never depend on this same book's own event
            for (int i = 0; i < book.EventPages.Count; i++)
                result.Add(new BookEventPageReference { BookId = book.Id, PageIndex = i, DisplayName = $"{book.Name} - Page {i + 1}" });
        }
        return result;
    }

    private void RebuildEventCompletedCombo()
    {
        var choices = BuildEventChoices();
        EventCompletedEventCombo.ItemsSource = choices.Select(c => c.DisplayName).ToList();
        if (_conditions?.EventCompletedReference is { } current)
        {
            var match = choices.FirstOrDefault(c => c.BookId == current.BookId && c.PageIndex == current.PageIndex);
            if (match is not null) EventCompletedEventCombo.SelectedItem = match.DisplayName;
        }
        EventCompletedStateCombo.SelectedItem = (_conditions?.EventCompletedExpectedState ?? true) ? "True" : "False";
    }

    private void EventCompletedEventCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _conditions is null) return;
        var choices = BuildEventChoices();
        int index = EventCompletedEventCombo.SelectedIndex;
        _conditions.EventCompletedReference = index >= 0 && index < choices.Count ? choices[index] : null;
    }

    private void EventCompletedStateCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _conditions is null) return;
        _conditions.EventCompletedExpectedState = EventCompletedStateCombo.SelectedItem as string == "True";
    }

    // ---------- Checkmark 3: Stat Condition ----------

    private void RebuildStatRows()
    {
        StatRowsList.Children.Clear();
        if (_conditions is null || _stats is null) return;

        foreach (var stat in _stats.Stats)
        {
            var entry = _conditions.StatConditions.FirstOrDefault(s => s.StatId == stat.Id);
            if (entry is null)
            {
                entry = new StatConditionEntry { StatId = stat.Id };
                _conditions.StatConditions.Add(entry);
            }
            StatRowsList.Children.Add(BuildStatRow(stat, entry));
        }
    }

    private UIElement BuildStatRow(StatDefinition stat, StatConditionEntry entry)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

        var activeCheck = new CheckBox { IsChecked = entry.IsActive, Content = stat.Name, Width = 100, VerticalAlignment = VerticalAlignment.Center };
        activeCheck.Checked += (_, _) => { if (!_suppress) entry.IsActive = true; };
        activeCheck.Unchecked += (_, _) => { if (!_suppress) entry.IsActive = false; };

        var comparisonCombo = new ComboBox { Width = 90, ItemsSource = BuildComparisonChoices(), Margin = new Thickness(6, 0, 6, 0) };
        SelectComparison(comparisonCombo, entry.Comparison);
        comparisonCombo.SelectionChanged += (_, _) => { if (!_suppress && comparisonCombo.SelectedItem is ComparisonChoice c) entry.Comparison = c.Value; };

        var valueBox = new TextBox { Width = 60, Text = entry.Value.ToString() };
        valueBox.TextChanged += (_, _) => { if (!_suppress && int.TryParse(valueBox.Text, out int v)) entry.Value = v; };

        row.Children.Add(activeCheck);
        row.Children.Add(comparisonCombo);
        row.Children.Add(valueBox);
        return row;
    }

    // ---------- Checkmark 4: Combine Item Condition ----------

    private void RebuildCombineItemSlots()
    {
        CombineItemSlot1Row.Children.Clear();
        CombineItemSlot2Row.Children.Clear();
        if (_conditions is null) return;
        CombineItemSlot1Row.Children.Add(BuildCombineItemSlotUI(_conditions.CombineItemSlot1, _conditions.CombineItemSlot2, "Item 1:"));
        CombineItemSlot2Row.Children.Add(BuildCombineItemSlotUI(_conditions.CombineItemSlot2, _conditions.CombineItemSlot1, "Item 2:"));
    }

    private UIElement BuildCombineItemSlotUI(CombineItemSlot slot, CombineItemSlot other, string label)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Width = 45 });

        var itemLabel = new TextBlock
        {
            Text = slot.IsSelf ? "(Self)" : (slot.Item?.DisplayName ?? "(none)"),
            VerticalAlignment = VerticalAlignment.Center,
            Width = 100,
            Margin = new Thickness(0, 0, 6, 0)
        };
        var pickButton = new Button { Content = "...", Width = 28, IsEnabled = !slot.IsSelf, Margin = new Thickness(0, 0, 10, 0) };
        pickButton.Click += (_, _) =>
        {
            if (_books is null || _objects is null || _sprites is null) return;
            var picker = new SelectItemPickerWindow(_books, _objects, _containers, _sprites, slot.Item) { Owner = Window.GetWindow(this) };
            if (picker.ShowDialog() == true)
            {
                slot.Item = picker.Result;
                itemLabel.Text = slot.Item?.DisplayName ?? "(none)";
            }
        };

        var selfCheck = new CheckBox { Content = "Self", IsChecked = slot.IsSelf, VerticalAlignment = VerticalAlignment.Center };
        selfCheck.Checked += (_, _) =>
        {
            if (_suppress) return;
            if (other.IsSelf)
            {
                selfCheck.IsChecked = false;
                MessageBox.Show("Only one item can be marked Self.", "Combine Item Condition", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            slot.IsSelf = true;
            pickButton.IsEnabled = false;
            itemLabel.Text = "(Self)";
        };
        selfCheck.Unchecked += (_, _) =>
        {
            if (_suppress) return;
            slot.IsSelf = false;
            pickButton.IsEnabled = true;
            itemLabel.Text = slot.Item?.DisplayName ?? "(none)";
        };

        panel.Children.Add(itemLabel);
        panel.Children.Add(pickButton);
        panel.Children.Add(selfCheck);
        return panel;
    }

    // ---------- Checkmark 7: Complex Condition ----------

    private void RebuildComplexGroupsList()
    {
        ComplexGroupsList.Children.Clear();
        if (_conditions is null) return;
        foreach (var group in _conditions.ComplexConditions)
            ComplexGroupsList.Children.Add(BuildComplexGroupRow(group));
    }

    private UIElement BuildComplexGroupRow(ComplexConditionGroup group)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };

        var deleteButton = new Button
        {
            Content = "X",
            Width = 20,
            Height = 20,
            Padding = new Thickness(0),
            Foreground = Brushes.White,
            Background = Brushes.Firebrick,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 6, 0),
            ToolTip = "Delete this complex condition"
        };
        deleteButton.Click += (_, _) =>
        {
            _conditions?.ComplexConditions.Remove(group);
            RebuildComplexGroupsList();
        };

        var nameLabel = new TextBlock { Text = group.Name, Width = 140, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) };
        var editButton = new Button { Content = "Edit..." };
        editButton.Click += (_, _) => OpenComplexEditor(group, nameLabel);

        row.Children.Add(deleteButton);
        row.Children.Add(nameLabel);
        row.Children.Add(editButton);
        return row;
    }

    private void OpenComplexEditor(ComplexConditionGroup group, TextBlock nameLabel)
    {
        if (_books is null || _objects is null || _sprites is null || _variables is null || _stats is null) return;
        var editor = new ComplexConditionEditorWindow(group, _books, _objects, _containers, _sprites, _variables, _stats, CurrentBookLocalVariables ?? new List<VariableDefinition>(), CurrentBookId)
        {
            Owner = Window.GetWindow(this)
        };
        editor.ShowDialog();
        nameLabel.Text = group.Name; // reflect any rename made inside the editor
    }

    private void AddComplexConditionButton_Click(object sender, RoutedEventArgs e)
    {
        if (_conditions is null) return;
        var group = new ComplexConditionGroup();
        _conditions.ComplexConditions.Add(group);
        RebuildComplexGroupsList();

        // Open it immediately for editing, same as the RPG Maker "add and configure right away" flow.
        var lastRow = ComplexGroupsList.Children[^1] as StackPanel;
        var nameLabel = lastRow?.Children.OfType<TextBlock>().FirstOrDefault();
        if (nameLabel is not null) OpenComplexEditor(group, nameLabel);
    }

    // ---------- Checkmark 6: Joined Party Condition (proximity trigger) ----------

    private void RebuildJoinedPartyCombo()
    {
        // No NPC Editor/database exists yet - this stays empty until one does,
        // same honesty pattern as the Weapons/Armors/Items tabs in the item picker.
        JoinedPartyNpcCombo.ItemsSource = Array.Empty<string>();
    }

    private void JoinedPartyNpcCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppress || _conditions is null) return;
        // Nothing to wire up yet - no NPC data exists to select from.
    }
}
