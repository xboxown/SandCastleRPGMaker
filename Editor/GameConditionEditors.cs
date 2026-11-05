using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Editing game conditions: the settings each kind needs (who, which item,
/// a variable, a number...), shared by the Advance IF command (one numbered
/// tab per kind), a Complex Condition's Game Condition, and a page's
/// "Until Condition Met" list.
/// </summary>
internal static class GameConditionFields
{
    private sealed record Choice(int Id, string Name)
    {
        public override string ToString() => Name;
    }

    private static TextBlock Label(string text) => new() { Text = text, FontSize = 11, Margin = new Thickness(0, 6, 0, 2) };
    private static TextBlock Note(string text) => new() { Text = text, FontSize = 11, Opacity = 0.7, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };

    /// <summary>The settings for a condition's kind, editing it in place.</summary>
    public static FrameworkElement Build(GameCondition c, Window owner)
    {
        var panel = new StackPanel();
        void Who(string label, bool anyone = false)
        {
            var choices = new List<Choice> { new(GameCondition.MainPlayer, "The main character (you)") };
            if (anyone) choices.Add(new(GameCondition.Anyone, "Anyone"));
            choices.AddRange(ProjectSession.Characters.Characters.Where(ch => !ch.IsTestPlayer).Select(ch => new Choice(ch.Id, ch.Name)));
            var combo = new ComboBox { Width = 260, ItemsSource = choices, HorizontalAlignment = HorizontalAlignment.Left };
            combo.SelectedItem = choices.FirstOrDefault(x => x.Id == c.CharacterId) ?? choices[0];
            combo.SelectionChanged += (_, _) => c.CharacterId = (combo.SelectedItem as Choice)?.Id ?? GameCondition.MainPlayer;
            panel.Children.Add(Label(label));
            panel.Children.Add(combo);
        }
        void Item(string label, string anyText = "(any)")
        {
            var button = new Button { HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2) };
            void Show() => button.Content = c.Item is { ItemId: >= 0 } item
                ? DollLook.ItemName(item, ProjectSession.ObjectsProvider?.Invoke() ?? new ObjectDatabase(), ProjectSession.BooksProvider?.Invoke() ?? new BookDatabase(), ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase())
                : anyText + " - pick...";
            button.Click += (_, _) =>
            {
                var picker = new SelectItemPickerWindow(ProjectSession.BooksProvider?.Invoke() ?? new BookDatabase(), ProjectSession.ObjectsProvider?.Invoke() ?? new ObjectDatabase(),
                    ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase(), ProjectSession.Sprites, c.Item) { Owner = owner };
                if (picker.ShowDialog() == true) c.Item = picker.Result is { ItemId: >= 0 } r ? new ItemReference { Category = r.Category, ItemId = r.ItemId, DisplayName = r.DisplayName } : null;
                Show();
            };
            var clear = new Button { Content = anyText, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6, 0, 0, 0) };
            clear.Click += (_, _) => { c.Item = null; Show(); };
            Show();
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(button);
            row.Children.Add(clear);
            panel.Children.Add(Label(label));
            panel.Children.Add(row);
        }
        void Number(string label, Func<int> get, Action<int> set, string? after = null)
        {
            var box = new TextBox { Width = 60, Text = get().ToString() };
            box.LostFocus += (_, _) => { if (int.TryParse(box.Text.Trim(), out int n)) set(n); box.Text = get().ToString(); };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(box);
            if (after is not null) row.Children.Add(new TextBlock { Text = "  " + after, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
            panel.Children.Add(Label(label));
            panel.Children.Add(row);
        }
        void Compare(string label, bool numberValue = true)
        {
            var ops = Enum.GetValues<ConditionComparisonOperator>().ToList();
            var op = new ComboBox { Width = 60, ItemsSource = ops.Select(GameConditions.Symbol).ToList(), SelectedIndex = ops.IndexOf(c.Comparison) };
            op.SelectionChanged += (_, _) => { if (op.SelectedIndex >= 0) c.Comparison = ops[op.SelectedIndex]; };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(op);
            if (numberValue)
            {
                var value = new TextBox { Width = 70, Margin = new Thickness(6, 0, 0, 0), Text = c.Number.ToString() };
                value.LostFocus += (_, _) => { if (int.TryParse(value.Text.Trim(), out int n)) c.Number = n; value.Text = c.Number.ToString(); };
                row.Children.Add(value);
            }
            panel.Children.Add(Label(label));
            panel.Children.Add(row);
        }
        void OnOff(string onText, string offText)
        {
            var combo = new ComboBox { Width = 160, ItemsSource = new[] { onText, offText }, SelectedIndex = c.On ? 0 : 1, HorizontalAlignment = HorizontalAlignment.Left };
            combo.SelectionChanged += (_, _) => c.On = combo.SelectedIndex == 0;
            panel.Children.Add(Label("Is"));
            panel.Children.Add(combo);
        }
        void Happened(string what)
        {
            Number($"{what} at least this many times", () => Math.Max(1, c.Number), v => c.Number = Math.Max(1, v));
            Number("Within the last (game hours)", () => c.Number2, v => c.Number2 = Math.Max(0, v), "0 = ever");
        }
        void EventName()
        {
            var names = ProjectSession.EventNamesProvider?.Invoke() ?? new List<string>();
            var combo = new ComboBox { Width = 260, IsEditable = true, ItemsSource = names, Text = c.EventName, HorizontalAlignment = HorizontalAlignment.Left };
            combo.LostFocus += (_, _) => c.EventName = combo.Text.Trim();
            combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string s) c.EventName = s; };
            panel.Children.Add(Label("Event (its name: a map event, an object, a book, a container, a character...)"));
            panel.Children.Add(combo);
        }

        switch (c.Kind)
        {
            case GameConditionKind.Variable:
            {
                var names = ProjectSession.VariablesProvider?.Invoke()?.Variables.Select(v => v.Name).ToList() ?? new List<string>();
                var variable = new ComboBox { Width = 220, IsEditable = true, ItemsSource = names, Text = c.VariableName, HorizontalAlignment = HorizontalAlignment.Left };
                variable.LostFocus += (_, _) => c.VariableName = variable.Text.Trim();
                variable.SelectionChanged += (_, _) => { if (variable.SelectedItem is string s) c.VariableName = s; };
                panel.Children.Add(Label("Variable"));
                panel.Children.Add(variable);
                Compare("Is", numberValue: false);
                var value = new TextBox { Width = 120, Text = c.Value, HorizontalAlignment = HorizontalAlignment.Left };
                value.LostFocus += (_, _) => c.Value = value.Text.Trim();
                panel.Children.Add(Label("Value (a number for Integer / Float / Decimal, True / False for Boolean - String variables can't be tested yet)"));
                panel.Children.Add(value);
                break;
            }
            case GameConditionKind.Equipped: Who("Who"); Item("Wears / holds (in a paperdoll slot)"); break;
            case GameConditionKind.Unequipped: Who("Who"); Item("Carries it, but isn't wearing / holding it"); break;
            case GameConditionKind.HavingItem: Who("Who (the main character: the whole party's things)"); Item("Has"); Number("At least", () => Math.Max(1, c.Number), v => c.Number = Math.Max(1, v)); break;
            case GameConditionKind.NotHavingItem: Who("Who (the main character: the whole party's things)"); Item("Doesn't have"); Number("...at least this many of it", () => Math.Max(1, c.Number), v => c.Number = Math.Max(1, v)); break;
            case GameConditionKind.Gender:
            {
                Who("Who");
                var gender = new ComboBox { Width = 120, ItemsSource = Enum.GetValues<CharacterGender>(), SelectedItem = c.Gender, HorizontalAlignment = HorizontalAlignment.Left };
                gender.SelectionChanged += (_, _) => c.Gender = (CharacterGender)gender.SelectedItem;
                panel.Children.Add(Label("Is"));
                panel.Children.Add(gender);
                break;
            }
            case GameConditionKind.Age: Who("Who"); Compare("Age"); break;
            case GameConditionKind.Stat:
            {
                Who("Who (you, or any character)");
                var stats = ProjectSession.StatsProvider?.Invoke()?.Stats.Select(s => new Choice(s.Id, s.Name)).ToList() ?? new List<Choice>();
                var stat = new ComboBox { Width = 200, ItemsSource = stats, SelectedItem = stats.FirstOrDefault(s => s.Id == c.StatId) ?? stats.FirstOrDefault(), HorizontalAlignment = HorizontalAlignment.Left };
                if (c.StatId < 0 && stat.SelectedItem is Choice first) c.StatId = first.Id;
                stat.SelectionChanged += (_, _) => c.StatId = (stat.SelectedItem as Choice)?.Id ?? -1;
                panel.Children.Add(Label("Stat (Alignment, Strength... - Stats Editor)"));
                panel.Children.Add(stat);
                Compare("Is");
                break;
            }
            case GameConditionKind.Destroyed: Item("Object destroyed"); Happened("Destroyed"); break;
            case GameConditionKind.Stolen: Item("Object stolen (taken from its owner)"); Happened("Stolen"); break;
            case GameConditionKind.Used: Item("Object used (double-clicked)"); Happened("Used"); break;
            case GameConditionKind.ExplosiveIgnited: Item("Explosive lit"); Happened("Lit"); break;
            case GameConditionKind.Picklocked: Item("Door or container picklocked"); Happened("Picklocked"); break;
            case GameConditionKind.UnlockedByKey: Item("Door or container unlocked by its key"); Happened("Unlocked"); break;
            case GameConditionKind.MagicallyUnlocked: Item("Door or container unlocked by magic"); Happened("Unlocked"); panel.Children.Add(Note("Recorded when a spell unlocks it (magic unlocking comes with the spellbook rework).")); break;
            case GameConditionKind.LightBurnedOut: Item("Light"); Happened("Burnt out"); break;
            case GameConditionKind.LightPutOut: Item("Light"); Happened("Put out"); break;
            case GameConditionKind.LightLit: Item("Light"); Happened("Lit"); break;
            case GameConditionKind.LastCommandSucceeded:
                panel.Children.Add(Note("True when the last command that can fail worked: Walk To Location got there, Pickup Item picked it up, Open Door opened it, Give Item gave it, Train trained... False when it was cancelled (no way there, too heavy, locked, not enough money). Put it straight after that command."));
                break;
            case GameConditionKind.SeesCharacter:
            {
                Who("Who");
                var how = new ComboBox { Width = 260, ItemsSource = new[] { "is on the screen", "can see the main character" }, SelectedIndex = c.Number == 1 ? 1 : 0, HorizontalAlignment = HorizontalAlignment.Left };
                how.SelectionChanged += (_, _) => c.Number = how.SelectedIndex;
                panel.Children.Add(Label("...and"));
                panel.Children.Add(how);
                break;
            }
            case GameConditionKind.CharacterKilled: Who("Who (Anyone: someone has been killed)", anyone: true); Happened("Killed"); break;
            case GameConditionKind.SoundPlayed:
            case GameConditionKind.SoundStopped:
            {
                var sounds = new List<Choice> { new(-1, "(any sound)") };
                sounds.AddRange(ProjectSession.ResourcesProvider?.Invoke()?.Entries.Where(r => r.Kind == ResourceKind.Sound).Select(r => new Choice(r.Id, r.Name)) ?? Enumerable.Empty<Choice>());
                var sound = new ComboBox { Width = 240, ItemsSource = sounds, SelectedItem = sounds.FirstOrDefault(s => s.Id == c.SoundId) ?? sounds[0], HorizontalAlignment = HorizontalAlignment.Left };
                sound.SelectionChanged += (_, _) => c.SoundId = (sound.SelectedItem as Choice)?.Id ?? -1;
                panel.Children.Add(Label("Sound"));
                panel.Children.Add(sound);
                Happened(c.Kind == GameConditionKind.SoundPlayed ? "Played" : "Stopped");
                break;
            }
            case GameConditionKind.LeverOnOff: Item("Switch / button / lever (any placed copy of it)"); OnOff("On", "Off"); break;
            case GameConditionKind.TimeDate:
            {
                Number("From hour (0-23)", () => c.Number, v => c.Number = Math.Clamp(v, 0, 23));
                Number("To hour (0-23)", () => c.Number2, v => c.Number2 = Math.Clamp(v, 0, 23), "the same hour as From: that hour only; it can go past midnight");
                Compare("And the day", numberValue: false);
                var day = new TextBox { Width = 60, Text = c.Value, HorizontalAlignment = HorizontalAlignment.Left };
                day.LostFocus += (_, _) => c.Value = day.Text.Trim();
                panel.Children.Add(Label("Day number (empty: any day; day 1 is the first)"));
                panel.Children.Add(day);
                break;
            }
            case GameConditionKind.EquipAction: Who("Who (Anyone: whoever)", anyone: true); Item("Put on"); Happened("Put on"); break;
            case GameConditionKind.UnequipAction: Who("Who (Anyone: whoever)", anyone: true); Item("Took off"); Happened("Taken off"); break;
            case GameConditionKind.SoldTo: Who("Sold to", anyone: true); Item("Item sold"); Happened("Sold"); panel.Children.Add(Note("Nothing is sold yet - this is true once shops can buy and sell.")); break;
            case GameConditionKind.TradedTo: Who("Traded to", anyone: true); Item("Item traded"); Happened("Traded"); panel.Children.Add(Note("Nothing is traded yet - this is true once trading is in.")); break;
            case GameConditionKind.TradedWithin: Item("Item traded"); Happened("Traded"); panel.Children.Add(Note("Nothing is traded yet - this is true once trading is in.")); break;
            case GameConditionKind.EventExecuted: EventName(); Happened("Run"); break;
            case GameConditionKind.EventPageExecuted: Number("This event's page number", () => Math.Max(1, c.Number), v => c.Number = Math.Max(1, v)); Number("Within the last (game hours)", () => c.Number2, v => c.Number2 = Math.Max(0, v), "0 = ever"); break;
            case GameConditionKind.EventNamePageExecuted: EventName(); Number("Its page number", () => Math.Max(1, c.Number), v => c.Number = Math.Max(1, v)); Number("Within the last (game hours)", () => c.Number2, v => c.Number2 = Math.Max(0, v), "0 = ever"); break;
            case GameConditionKind.InParty:
                Who("Who (Anyone: at least this many have joined)", anyone: true);
                Number("How many joined (for Anyone)", () => Math.Max(1, c.Number), v => c.Number = Math.Max(1, v));
                panel.Children.Add(Note("The main character always counts as in the party; Anyone counts those who joined."));
                break;
            case GameConditionKind.CharacterState:
            {
                Who("Who");
                string[] states = { "Poisoned", "Asleep", "Paralyzed", "Charmed", "Cursed", "Invisible", "Protected", "FeignDeath", "WithstandDeath", "Unconscious", "In combat", "Dead" };
                if (Array.IndexOf(states, c.Value) < 0) c.Value = states[0];
                var state = new ComboBox { Width = 200, ItemsSource = states, SelectedItem = c.Value, HorizontalAlignment = HorizontalAlignment.Left };
                state.SelectionChanged += (_, _) => { if (state.SelectedItem is string s) c.Value = s; };
                panel.Children.Add(Label("Is"));
                panel.Children.Add(state);
                break;
            }
            case GameConditionKind.EnemyNear:
                Who("Who");
                Number("Within (map cells)", () => Math.Max(1, c.Number), v => c.Number = Math.Max(1, v));
                panel.Children.Add(Note("Someone who would attack them, or they it, on the same floor."));
                break;
            case GameConditionKind.ObjectsInArea:
            {
                Who("Around whom");
                Item("Object or item (none: anything)");
                Number("Within (map cells; 0: their cell)", () => c.Number2, v => c.Number2 = Math.Max(0, v));
                Compare("How many");
                var lit = new CheckBox { Content = "Lit ones only", IsChecked = !c.On, Margin = new Thickness(0, 6, 0, 0) };
                lit.Checked += (_, _) => c.On = false;
                lit.Unchecked += (_, _) => c.On = true;
                panel.Children.Add(lit);
                break;
            }
            case GameConditionKind.WeightOnObject: Item("The object (any placed copy of it)"); Compare("Weight on it"); break;
            case GameConditionKind.ItemCharges: Who("Who carries it"); Item("Item"); Compare("Charges"); break;
            case GameConditionKind.DoorOpen: Item("Door (a placed copy of it), or container (its window open)"); OnOff("Open", "Closed"); break;
        }
        var not = new CheckBox { Content = "NOT - true when this isn't so", IsChecked = c.Not, Margin = new Thickness(0, 10, 0, 0) };
        not.Checked += (_, _) => c.Not = true;
        not.Unchecked += (_, _) => c.Not = false;
        panel.Children.Add(not);
        return panel;
    }

    public static string Describe(GameCondition c) =>
        $"{(c.Not ? "NOT " : "")}{GameConditions.Name(c.Kind)}" + c.Kind switch
        {
            GameConditionKind.Variable => $": {c.VariableName} {GameConditions.Symbol(c.Comparison)} {c.Value}",
            GameConditionKind.EventExecuted or GameConditionKind.EventNamePageExecuted => $": {c.EventName}{(c.Kind == GameConditionKind.EventNamePageExecuted ? $" page {c.Number}" : "")}",
            GameConditionKind.TimeDate => $": {c.Number}:00-{c.Number2}:00",
            _ => c.Item is { ItemId: >= 0 } item ? $": {item.DisplayName}" : ""
        };
}

/// <summary>
/// Advance IF: one condition from any of the numbered tabs (1-27, then
/// doors, containers and lights), its own settings on its tab; what follows
/// runs when it's true (and an Else branch when it isn't, if wanted).
/// </summary>
public class AdvanceIfCommandWindow : Window
{
    // The conditions by name ("1. Variable", "2. Equipped"...) down the left - 33 named tabs wouldn't fit across the top.
    private readonly ListBox _tabs = new() { Margin = new Thickness(8, 8, 0, 8), Width = 250 };
    private readonly ContentControl _page = new() { Margin = new Thickness(8) };
    private readonly Dictionary<ListBoxItem, GameCondition> _byTab = new();
    private readonly CheckBox _else = new() { Content = "Set what to do when it isn't so (an Else branch)", Margin = new Thickness(8, 0, 8, 0) };
    public List<EventCommand> Results { get; private set; } = new();

    public AdvanceIfCommandWindow(EventCommand? existing = null)
    {
        Title = "Advance IF";
        Width = 900;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var current = existing is null ? null : GameConditions.FromCommand(existing);
        foreach (var kind in GameConditions.All)
        {
            var condition = current is not null && current.Kind == kind ? current.Clone() : new GameCondition { Kind = kind };
            if (current is null && kind == GameConditionKind.TimeDate) { condition.Number = 9; condition.Number2 = 17; condition.Value = ""; }
            var body = new StackPanel { Margin = new Thickness(10) };
            body.Children.Add(new TextBlock { Text = $"{(int)kind}. {GameConditions.Name(kind)}", FontWeight = FontWeights.Bold, FontSize = 14 });
            body.Children.Add(GameConditionFields.Build(condition, this));
            var tab = new ListBoxItem { Content = $"{(int)kind}. {GameConditions.Name(kind)}", Tag = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Padding = new Thickness(4, 2, 4, 2) };
            _byTab[tab] = condition;
            _tabs.Items.Add(tab);
            if (current?.Kind == kind) _tabs.SelectedItem = tab;
        }
        _tabs.SelectionChanged += (_, _) => _page.Content = (_tabs.SelectedItem as ListBoxItem)?.Tag;
        if (_tabs.SelectedItem is null && _tabs.Items.Count > 0) _tabs.SelectedIndex = 0;
        _page.Content = (_tabs.SelectedItem as ListBoxItem)?.Tag;
        Loaded += (_, _) => _tabs.ScrollIntoView(_tabs.SelectedItem);
        _else.Visibility = existing is null ? Visibility.Visible : Visibility.Collapsed;
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(8) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (_tabs.SelectedItem is not ListBoxItem tab) return;
            var command = new EventCommand { Type = EventCommandType.AdvancedIf };
            GameConditions.ToCommand(command, _byTab[tab]);
            command.Parameters["Summary"] = GameConditionFields.Describe(_byTab[tab]);
            Results = new List<EventCommand> { command };
            if (existing is null)
            {
                if (_else.IsChecked == true) Results.Add(new EventCommand { Type = EventCommandType.Else });
                Results.Add(new EventCommand { Type = EventCommandType.EndBranch });
            }
            DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var bottom = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(_else);
        var root = new DockPanel();
        DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(bottom);
        root.Children.Add(new TextBlock { Text = "Pick the condition on the left, set it, and OK: the commands under the IF run when it's so.", Margin = new Thickness(10, 8, 10, 0), Opacity = 0.75 });
        DockPanel.SetDock(root.Children[1], Dock.Top);
        DockPanel.SetDock(_tabs, Dock.Left);
        root.Children.Add(_tabs);
        root.Children.Add(new Border { BorderBrush = System.Windows.Media.Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 8, 8, 8), Child = _page });
        Content = root;
    }

    public static string Describe(EventCommand command) => command.Get("Summary");
}

/// <summary>A list of game conditions joined by And / Or / Xor - "Until Condition Met" (the page stops once they're met).</summary>
public class GameConditionListWindow : Window
{
    private readonly List<GameCondition> _list;
    private readonly StackPanel _rows = new() { Margin = new Thickness(8) };

    public GameConditionListWindow(List<GameCondition> list, string title)
    {
        _list = list;
        Title = title;
        Width = 720;
        Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var add = new Button { Content = "Add a condition", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => { _list.Add(new GameCondition()); Refresh(); };
        var close = new Button { Content = "Close", Width = 80, Margin = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Right, IsCancel = true };
        close.Click += (_, _) => Close();
        var top = new DockPanel();
        DockPanel.SetDock(close, Dock.Right);
        top.Children.Add(close);
        top.Children.Add(add);
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(new ScrollViewer { Content = _rows, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Refresh();
    }

    private void Refresh()
    {
        _rows.Children.Clear();
        _rows.Children.Add(new TextBlock { Text = "The page keeps going (by its own conditions) until these are met; then it stops. Joined left to right by And / Or / Xor.", TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 8) });
        for (int i = 0; i < _list.Count; i++)
        {
            var c = _list[i];
            var box = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var kinds = GameConditions.All.Select(k => $"{(int)k}. {GameConditions.Name(k)}").ToList();
            var kind = new ComboBox { Width = 320, ItemsSource = kinds, SelectedIndex = Array.IndexOf(GameConditions.All, c.Kind) };
            kind.SelectionChanged += (_, _) => { if (kind.SelectedIndex >= 0) { c.Kind = GameConditions.All[kind.SelectedIndex]; Refresh(); } };
            var remove = new Button { Content = "Remove", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(8, 0, 0, 0) };
            remove.Click += (_, _) => { _list.Remove(c); Refresh(); };
            head.Children.Add(kind);
            head.Children.Add(remove);
            box.Children.Add(head);
            box.Children.Add(GameConditionFields.Build(c, this));
            if (i < _list.Count - 1)
            {
                var op = new ComboBox { Width = 70, ItemsSource = Enum.GetValues<ComplexLogicOperator>(), SelectedItem = c.NextOperator, Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                op.SelectionChanged += (_, _) => c.NextOperator = (ComplexLogicOperator)op.SelectedItem;
                box.Children.Add(op);
            }
            _rows.Children.Add(new Border { BorderBrush = System.Windows.Media.Brushes.Gray, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 6, 0, 8), Child = box });
        }
        if (_list.Count == 0) _rows.Children.Add(new TextBlock { Text = "(none yet: Add a condition)", Opacity = 0.6 });
    }
}
