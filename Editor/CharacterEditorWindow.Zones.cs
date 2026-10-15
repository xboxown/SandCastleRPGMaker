using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Character Editor > Schedule: where a schedule line goes - a cell, a zone
/// of the map (Map Editor > Mode > Zones), or the nearest zone with a
/// purpose ("the nearest Pub") - and the When... rules that take over from
/// the timetable while they hold ("when hungry, eat at the nearest Pub";
/// "when injured, sleep at the Hospital").
/// </summary>
public partial class CharacterEditorWindow
{
    /// <summary>One "where" choice: a cell (the line's own spot), one zone, or the nearest zone of a purpose.</summary>
    private sealed record PlaceChoice(int ZoneId, string Purpose, string Label)
    {
        public bool IsCell => ZoneId < 0 && Purpose.Length == 0;
        public override string ToString() => Label;
    }

    private static List<MapZone> OpenMapZones => ProjectSession.ZonesProvider?.Invoke() ?? new List<MapZone>();

    private static List<PlaceChoice> PlaceChoices()
    {
        var zones = OpenMapZones;
        var choices = new List<PlaceChoice> { new(-1, "", "A cell (Pick on Map / Placed Spot)") };
        foreach (var purpose in zones.Select(z => z.Purpose).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p))
            choices.Add(new(-1, purpose, $"The nearest {purpose}"));
        foreach (var zone in zones.OrderBy(z => z.Name))
            choices.Add(new(zone.Id, "", $"Zone: {zone.Describe()}{(zone.Floor > 1 ? $" - floor {zone.Floor}" : "")}"));
        return choices;
    }

    /// <summary>A "where" chooser for a schedule line (or a rule's Do); sync() shows the entry's current choice.</summary>
    private (ComboBox Combo, Action Sync) WhereCombo(Func<ScheduleEntry?> entry, Action changed)
    {
        var choices = PlaceChoices();
        // A purpose typed in earlier that no zone has now still shows.
        var combo = new ComboBox { Width = 220, Margin = new Thickness(0, 0, 8, 0), ItemsSource = choices, ToolTip = "Where it goes: a cell, a zone (Map Editor > Mode > Zones), or the nearest zone with a purpose" };
        bool syncing = false;
        void Sync()
        {
            syncing = true;
            var e = entry();
            if (e is { ZonePurpose.Length: > 0 } && choices.All(c => !string.Equals(c.Purpose, e.ZonePurpose, StringComparison.OrdinalIgnoreCase)))
            {
                choices.Add(new(-1, e.ZonePurpose, $"The nearest {e.ZonePurpose} (no such zone yet)"));
                combo.Items.Refresh();
            }
            combo.SelectedItem = e is null ? choices[0]
                : e.ZoneId >= 0 ? choices.FirstOrDefault(c => c.ZoneId == e.ZoneId) ?? choices[0]
                : e.ZonePurpose.Length > 0 ? choices.FirstOrDefault(c => string.Equals(c.Purpose, e.ZonePurpose, StringComparison.OrdinalIgnoreCase)) ?? choices[0]
                : choices[0];
            syncing = false;
        }
        combo.SelectionChanged += (_, _) =>
        {
            if (syncing || entry() is not { } e || combo.SelectedItem is not PlaceChoice choice) return;
            e.ZoneId = choice.ZoneId;
            e.ZonePurpose = choice.Purpose;
            changed();
        };
        Sync();
        return (combo, Sync);
    }

    /// <summary>What the line's place reads as.</summary>
    private static string PlaceText(ScheduleEntry entry, (int X, int Y)? placed)
    {
        if (entry.ZoneId >= 0) return OpenMapZones.FirstOrDefault(z => z.Id == entry.ZoneId) is { } zone ? $"in {zone.Name}" : "zone deleted: placed spot";
        if (entry.ZonePurpose.Length > 0) return $"nearest {entry.ZonePurpose}";
        return entry.X < 0 ? (placed is { } p ? $"placed spot ({p.X}, {p.Y})" : "placed spot") : $"cell ({entry.X}, {entry.Y})";
    }

    // ================= When... rules =================

    private sealed record ConditionChoice(ScheduleRuleCondition Condition, string Label)
    {
        public override string ToString() => Label;
    }

    private static readonly ConditionChoice[] ConditionChoices =
    {
        new(ScheduleRuleCondition.Hungry, "When hungry (until full)"),
        new(ScheduleRuleCondition.Injured, "When injured"),
        new(ScheduleRuleCondition.Variable, "When a variable...")
    };

    private void BuildScheduleRules(CharacterDefinition character)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock
        {
            Text = "These come before the timetable above: while one holds, the character leaves what it's doing, goes where it says and does what it says there; " +
                   "when it no longer holds, it goes back to its timetable. The first in the list that holds wins. Zones are drawn in the Map Editor (Mode > Zones).",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 0, 0, 6)
        });
        var list = new StackPanel();
        body.Children.Add(list);
        var add = new Button { Content = "Add a rule", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
        body.Children.Add(add);

        void Refill()
        {
            list.Children.Clear();
            for (int i = 0; i < character.ScheduleRules.Count; i++) list.Children.Add(RuleRow(character, character.ScheduleRules[i], i, Refill));
            if (character.ScheduleRules.Count == 0) list.Children.Add(new TextBlock { Text = "(none - it always follows its timetable)", Opacity = 0.6, FontStyle = FontStyles.Italic });
        }
        add.Click += (_, _) =>
        {
            var purpose = OpenMapZones.Select(z => z.Purpose).FirstOrDefault(p => string.Equals(p, "Pub", StringComparison.OrdinalIgnoreCase) || string.Equals(p, "Inn", StringComparison.OrdinalIgnoreCase)) ?? "Pub";
            character.ScheduleRules.Add(new ScheduleRule { Do = new ScheduleEntry { Type = ScheduleType.EatAtInn, ZonePurpose = purpose } });
            Refill();
        };
        Refill();
        SchedulePanel.Children.Add(new GroupBox { Header = "When... (needs that come first)", Padding = new Thickness(8), Margin = new Thickness(0, 12, 0, 0), Content = body });
    }

    private FrameworkElement RuleRow(CharacterDefinition character, ScheduleRule rule, int index, Action refill)
    {
        var box = new StackPanel();
        var line = new WrapPanel();
        var condition = new ComboBox { Width = 170, Margin = new Thickness(0, 0, 8, 0), ItemsSource = ConditionChoices, SelectedItem = ConditionChoices.First(c => c.Condition == rule.Condition) };
        line.Children.Add(condition);
        var parameters = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
        line.Children.Add(parameters);

        TextBox Number(string text, Action<string> set, double width = 44)
        {
            var b = new TextBox { Width = width, Text = text, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center };
            b.LostFocus += (_, _) => set(b.Text.Trim());
            return b;
        }
        TextBlock Words(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 2, 0) };

        void ShowParameters()
        {
            parameters.Children.Clear();
            switch (rule.Condition)
            {
                case ScheduleRuleCondition.Hungry:
                    parameters.Children.Add(Words(character.Survival.CanGetHungry
                        ? $"(food {character.Survival.HungryAtOrBelow} or less - Health tab)"
                        : "(it never gets hungry - Health tab)"));
                    break;
                case ScheduleRuleCondition.Injured:
                    parameters.Children.Add(Words("health at or below"));
                    parameters.Children.Add(Number(rule.InjuredAtOrBelowPercent.ToString(), v => { if (int.TryParse(v, out int n)) rule.InjuredAtOrBelowPercent = Math.Clamp(n, 1, 99); }));
                    parameters.Children.Add(Words("%, until back to"));
                    parameters.Children.Add(Number(rule.HealedAtPercent.ToString(), v => { if (int.TryParse(v, out int n)) rule.HealedAtPercent = Math.Clamp(n, 2, 100); }));
                    parameters.Children.Add(Words("%"));
                    break;
                case ScheduleRuleCondition.Variable:
                {
                    var names = ProjectSession.VariablesProvider?.Invoke()?.Variables.Select(v => v.Name).ToList() ?? new List<string>();
                    var variable = new ComboBox { Width = 130, IsEditable = true, ItemsSource = names, Text = rule.VariableName, Margin = new Thickness(2, 0, 2, 0) };
                    variable.LostFocus += (_, _) => rule.VariableName = variable.Text.Trim();
                    variable.SelectionChanged += (_, _) => { if (variable.SelectedItem is string s) rule.VariableName = s; };
                    var op = new ComboBox { Width = 60, ItemsSource = Enum.GetValues<ConditionComparisonOperator>(), SelectedItem = rule.Comparison, Margin = new Thickness(2, 0, 2, 0) };
                    op.SelectionChanged += (_, _) => rule.Comparison = (ConditionComparisonOperator)op.SelectedItem;
                    parameters.Children.Add(variable);
                    parameters.Children.Add(op);
                    parameters.Children.Add(Number(rule.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        v => { if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)) rule.Value = d; }, 60));
                    break;
                }
            }
        }
        condition.SelectionChanged += (_, _) =>
        {
            rule.Condition = ((ConditionChoice)condition.SelectedItem).Condition;
            // A sensible "do" for a new kind of need: eat at an inn, or rest in a bed.
            if (rule.Condition == ScheduleRuleCondition.Injured && rule.Do.Type == ScheduleType.EatAtInn) rule.Do.Type = ScheduleType.Sleep;
            if (rule.Condition == ScheduleRuleCondition.Hungry && rule.Do.Type == ScheduleType.Sleep) rule.Do.Type = ScheduleType.EatAtInn;
            refill();
        };
        ShowParameters();

        var doLine = new WrapPanel { Margin = new Thickness(16, 4, 0, 0) };
        doLine.Children.Add(Words("then:"));
        var activity = new ComboBox { Width = 170, Margin = new Thickness(0, 0, 8, 0), ItemsSource = Enum.GetValues<ScheduleType>().Where(t => t != ScheduleType.Wait).Select(t => new ActivityChoice(t)).ToList() };
        activity.SelectedItem = ((List<ActivityChoice>)activity.ItemsSource).FirstOrDefault(c => c.Type == rule.Do.Type);
        activity.SelectionChanged += (_, _) => { if (activity.SelectedItem is ActivityChoice { Type: { } t }) rule.Do.Type = t; activity.ToolTip = Schedules.Explain(rule.Do.Type); };
        activity.ToolTip = Schedules.Explain(rule.Do.Type);
        doLine.Children.Add(activity);
        doLine.Children.Add(Words("at"));
        var placeText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0), Opacity = 0.8 };
        var placed = ProjectSession.PlacedSpotProvider?.Invoke(character.Id);
        void ShowPlace() => placeText.Text = PlaceText(rule.Do, placed);
        var (where, syncWhere) = WhereCombo(() => rule.Do, ShowPlace);
        doLine.Children.Add(where);
        var pick = new Button { Content = "Pick on Map...", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 6, 0) };
        pick.Click += (_, _) =>
        {
            if (ProjectSession.MapSnapshotProvider?.Invoke() is not { } map) { MessageBox.Show(this, "Open or create a map in the Map Editor first.", "Pick on Map"); return; }
            var picker = new MapCellPickerWindow(map.Picture, map.TileWidth, map.TileHeight, map.Columns, map.Rows, rule.Do.X >= 0 ? (rule.Do.X, rule.Do.Y) : placed, placed) { Owner = this };
            if (picker.ShowDialog() == true && picker.Picked is { } cell) { rule.Do.X = cell.X; rule.Do.Y = cell.Y; rule.Do.ZoneId = -1; rule.Do.ZonePurpose = ""; syncWhere(); ShowPlace(); }
        };
        doLine.Children.Add(pick);
        doLine.Children.Add(placeText);
        ShowPlace();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(16, 4, 0, 0) };
        Button Small(string text, Action act)
        {
            var b = new Button { Content = text, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 0, 4, 0) };
            b.Click += (_, _) => act();
            return b;
        }
        buttons.Children.Add(Small("Up", () => { if (index > 0) { character.ScheduleRules.RemoveAt(index); character.ScheduleRules.Insert(index - 1, rule); refill(); } }));
        buttons.Children.Add(Small("Down", () => { if (index < character.ScheduleRules.Count - 1) { character.ScheduleRules.RemoveAt(index); character.ScheduleRules.Insert(index + 1, rule); refill(); } }));
        buttons.Children.Add(Small("Delete", () => { character.ScheduleRules.Remove(rule); refill(); }));

        box.Children.Add(line);
        box.Children.Add(doLine);
        box.Children.Add(buttons);
        return new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(0, 4, 0, 6), Margin = new Thickness(0, 0, 0, 4), Child = box };
    }

    /// <summary>The inn as a zone purpose: "or the nearest Pub" (Health tab, when hungry).</summary>
    private FrameworkElement InnZoneRow(SurvivalSettings s)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(18, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = "or, with no cell set, the nearest zone that is a", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
        var purposes = Zones.CommonPurposes.Concat(OpenMapZones.Select(z => z.Purpose)).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
        var combo = new ComboBox { Width = 130, IsEditable = true, ItemsSource = purposes, Text = s.InnZonePurpose };
        combo.LostFocus += (_, _) => s.InnZonePurpose = combo.Text.Trim();
        combo.SelectionChanged += (_, _) => { if (combo.SelectedItem is string p) s.InnZonePurpose = p; };
        row.Children.Add(combo);
        row.Children.Add(new TextBlock { Text = "(Map Editor > Mode > Zones)", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Opacity = 0.6, FontSize = 11 });
        return row;
    }
}
