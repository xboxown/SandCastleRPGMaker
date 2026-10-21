using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The window of every script command (Engine/ScriptCommands.cs): one box per
/// field, built from the command's own list - a formula box, a variable
/// picker, a choice, a character, an item, a thing or a spot on the map, a
/// paperdoll, a sprite, a sound, a Common Event, a condition.
/// </summary>
public class ScriptCommandWindow : FactionCommandWindowBase
{
    /// <summary>Array and grid names used so far, offered in their boxes.</summary>
    private static readonly SortedSet<string> KnownArrays = new(StringComparer.OrdinalIgnoreCase), KnownGrids = new(StringComparer.OrdinalIgnoreCase);

    private readonly ScriptCommandSpec _spec;
    private readonly List<Func<EventCommand, bool>> _savers = new();

    public ScriptCommandWindow(ScriptCommandSpec spec, EventCommand? existing) : base(spec.Name)
    {
        _spec = spec;
        Width = 500;
        AddLabel(spec.Help, bold: false);
        bool anyFormula = false;
        foreach (var field in spec.Fields)
        {
            AddField(field, existing);
            anyFormula |= field.Kind is ScriptFieldKind.Formula or ScriptFieldKind.Condition;
        }
        anyFormula |= spec.Fields.Any(f => f.Kind == ScriptFieldKind.Words);
        if (anyFormula)
            AddLabel("Formulas: 12, \"a text\", a variable (Gold, or {Gold Coins} with spaces), Items[i], Board[x, y], + - * / %, = != < > <= >=, and / or / not, " +
                     "random(1, 6), min, max, round, length(Items), contains(Items, 5), upper(Name)... Arrays and grids count from 0.", bold: false);
    }

    private void AddField(ScriptField field, EventCommand? existing)
    {
        string Existing(string key, string fallback) => existing is not null && existing.Parameters.TryGetValue(key, out var v) ? v : fallback;
        switch (field.Kind)
        {
            case ScriptFieldKind.Formula:
            case ScriptFieldKind.Text:
            {
                AddLabel(field.Label);
                var box = new TextBox { Text = Existing(field.Key, field.Default) };
                Rows.Children.Add(box);
                bool formula = field.Kind == ScriptFieldKind.Formula;
                _savers.Add(c =>
                {
                    if (formula && ScriptExpression.Check(box.Text) is { } error)
                    {
                        MessageBox.Show(this, $"{field.Label}: {error}.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    c.Parameters[field.Key] = box.Text.Trim();
                    return true;
                });
                break;
            }
            case ScriptFieldKind.LongText:
            {
                AddLabel(field.Label);
                var box = new TextBox { Text = Existing(field.Key, field.Default), AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 80, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                Rows.Children.Add(box);
                _savers.Add(c => { c.Parameters[field.Key] = box.Text; return true; });
                break;
            }
            case ScriptFieldKind.Variable:
            {
                AddLabel(field.Label);
                var names = ProjectSession.VariablesProvider?.Invoke()?.Variables.Select(v => v.Name).Distinct().OrderBy(n => n).ToList() ?? new List<string>();
                var combo = new ComboBox { IsEditable = true, ItemsSource = names, Text = Existing(field.Key, field.Default) };
                Rows.Children.Add(combo);
                bool optional = field.Label.Contains("optional", StringComparison.OrdinalIgnoreCase) || field.Key is "Variable2" or "Index" or "Index2" or "IdVariable";
                _savers.Add(c =>
                {
                    string name = combo.Text.Trim();
                    if (name.Length == 0 && !optional)
                    {
                        MessageBox.Show(this, $"{field.Label}: pick or type a variable's name.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    c.Parameters[field.Key] = name;
                    return true;
                });
                break;
            }
            case ScriptFieldKind.Array:
            case ScriptFieldKind.Grid:
            {
                AddLabel(field.Label);
                var known = field.Kind == ScriptFieldKind.Array ? KnownArrays : KnownGrids;
                var combo = new ComboBox { IsEditable = true, ItemsSource = known.ToList(), Text = Existing(field.Key, field.Default) };
                Rows.Children.Add(combo);
                _savers.Add(c =>
                {
                    string name = combo.Text.Trim();
                    if (name.Length == 0)
                    {
                        MessageBox.Show(this, $"{field.Label}: give its name.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    known.Add(name);
                    c.Parameters[field.Key] = name;
                    return true;
                });
                break;
            }
            case ScriptFieldKind.Choice:
            {
                AddLabel(field.Label);
                var combo = new ComboBox { ItemsSource = field.Options };
                combo.SelectedItem = field.Options!.FirstOrDefault(o => o == Existing(field.Key, field.Default)) ?? field.Options![0];
                Rows.Children.Add(combo);
                _savers.Add(c => { c.Parameters[field.Key] = combo.SelectedItem as string ?? field.Options![0]; return true; });
                break;
            }
            case ScriptFieldKind.Check:
            {
                var check = new CheckBox { Content = field.Label, IsChecked = Existing(field.Key, field.Default) == "True", Margin = new Thickness(0, 6, 0, 0) };
                Rows.Children.Add(check);
                _savers.Add(c => { c.Parameters[field.Key] = check.IsChecked == true ? "True" : "False"; return true; });
                break;
            }
            case ScriptFieldKind.Who:
            {
                AddLabel(field.Label);
                var choices = WhoChoice.Build(includePlayer: true);
                var combo = new ComboBox { ItemsSource = choices };
                combo.SelectedItem = choices.FirstOrDefault(w => w.Key == Existing(field.Key, "")) ?? choices.FirstOrDefault(w => w.Key == WhoChoice.Player && field.Key != "Who") ?? choices[0];
                Rows.Children.Add(combo);
                _savers.Add(c => { c.Parameters[field.Key] = CommandFields.WhoKey(combo); return true; });
                break;
            }
            case ScriptFieldKind.Item:
            {
                AddLabel(field.Label);
                var row = CommandFields.Row(Rows);
                bool optional = field.Label.Contains("optional", StringComparison.OrdinalIgnoreCase);
                var item = new ItemKindField(this, row, existing, optional ? "(none)" : null);
                _savers.Add(c => { if (!item.Validate()) return false; item.Save(c); return true; });
                break;
            }
            case ScriptFieldKind.Thing:
            {
                AddLabel(field.Label);
                var thing = new MapThingField(this, Rows, existing, "thing", MapThingField.IsAnyThing);
                bool optional = field.Label.Contains("for a container", StringComparison.OrdinalIgnoreCase);
                _savers.Add(c =>
                {
                    if (optional && c.Get("Where") != "A container on the map") return true;
                    if (!thing.Validate()) return false;
                    thing.Save(c);
                    return true;
                });
                break;
            }
            case ScriptFieldKind.Spot:
            {
                AddLabel(field.Label);
                string prefix = field.Key.EndsWith("X") ? field.Key[..^1] : field.Key;
                var spot = new MapCellField(this, Rows, existing, prefix, withFloor: true);
                bool optional = _spec.Type is EventCommandType.MoveCamera or EventCommandType.PlaceCharacter or EventCommandType.ChangeMap;
                _savers.Add(c =>
                {
                    if (optional && spot.Cell is null) return true;
                    if (!spot.Validate()) return false;
                    spot.Save(c, prefix);
                    return true;
                });
                break;
            }
            case ScriptFieldKind.World:
            {
                AddLabel(field.Label);
                var choices = new List<KeyValuePair<int, string>> { new(-1, "(This world - the one the party is in)") };
                choices.AddRange((ProjectSession.WorldsProvider?.Invoke() ?? new List<MapData>()).Select(w => new KeyValuePair<int, string>(w.Id, w.MapName)));
                var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Value" };
                combo.SelectedItem = choices.FirstOrDefault(w => w.Key.ToString() == Existing("World", "-1"));
                if (combo.SelectedItem is null) combo.SelectedIndex = 0;
                Rows.Children.Add(combo);
                Rows.Children.Add(new TextBlock { Text = "To pick a spot in another world, open that world in the Maps tab first.", Opacity = 0.7, FontSize = 11, TextWrapping = TextWrapping.Wrap });
                _savers.Add(c =>
                {
                    var picked = combo.SelectedItem is KeyValuePair<int, string> w ? w : choices[0];
                    c.Parameters["World"] = picked.Key.ToString();
                    c.Parameters["WorldName"] = picked.Key < 0 ? "" : picked.Value;
                    return true;
                });
                break;
            }
            case ScriptFieldKind.Vehicle:
            {
                AddLabel(field.Label);
                var choices = ProjectSession.Houses.Where(h => h.Vehicle is not null).Select(h => new KeyValuePair<int, string>(h.Id, h.Name)).ToList();
                var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Value" };
                combo.SelectedItem = choices.FirstOrDefault(v => v.Key.ToString() == Existing("Vehicle", ""));
                if (combo.SelectedItem is null && choices.Count > 0) combo.SelectedIndex = 0;
                Rows.Children.Add(combo);
                if (choices.Count == 0) Rows.Children.Add(new TextBlock { Text = "No vehicles yet - make one in the Map Editor's Vehicles tab.", Opacity = 0.7, FontSize = 11, TextWrapping = TextWrapping.Wrap });
                _savers.Add(c =>
                {
                    if (combo.SelectedItem is not KeyValuePair<int, string> picked) { MessageBox.Show(this, "Pick a vehicle.", Title); return false; }
                    c.Parameters["Vehicle"] = picked.Key.ToString();
                    c.Parameters["VehicleName"] = picked.Value;
                    return true;
                });
                break;
            }
            case ScriptFieldKind.Paperdoll:
            {
                AddLabel(field.Label);
                var choices = new List<KeyValuePair<int, string>> { new(-1, "(This paperdoll - the one running the event)") };
                choices.AddRange((ProjectSession.ObjectsProvider?.Invoke()?.DollDesigns ?? new List<DollDesign>()).Select(d => new KeyValuePair<int, string>(d.Id, d.Name)));
                var combo = new ComboBox { ItemsSource = choices, DisplayMemberPath = "Value" };
                combo.SelectedItem = choices.FirstOrDefault(d => d.Key.ToString() == Existing("DesignId", "-1"));
                if (combo.SelectedItem is null) combo.SelectedIndex = 0;
                Rows.Children.Add(combo);
                _savers.Add(c =>
                {
                    var picked = combo.SelectedItem is KeyValuePair<int, string> d ? d : choices[0];
                    c.Parameters["DesignId"] = picked.Key.ToString();
                    c.Parameters["DesignName"] = picked.Key < 0 ? "" : picked.Value;
                    return true;
                });
                break;
            }
            case ScriptFieldKind.Sprite:
            {
                AddLabel(field.Label);
                var row = CommandFields.Row(Rows);
                var group = new ComboBox { Width = 180 };
                var sprite = new ComboBox { Width = 200, Margin = new Thickness(6, 0, 0, 0) };
                var groups = ProjectSession.Sprites.Groups.Select(g => g.Name).ToList();
                group.ItemsSource = groups;
                group.SelectionChanged += (_, _) =>
                {
                    var sprites = ProjectSession.Sprites.Groups.FirstOrDefault(g => g.Name == group.SelectedItem as string)?.Sprites.Select(s => s.Name).ToList() ?? new List<string>();
                    sprite.ItemsSource = sprites;
                    sprite.SelectedItem = sprites.FirstOrDefault(s => s == Existing("SpriteName", "")) ?? sprites.FirstOrDefault();
                };
                group.SelectedItem = groups.FirstOrDefault(g => g == Existing("SpriteGroup", "")) ?? groups.FirstOrDefault();
                row.Children.Add(group);
                row.Children.Add(sprite);
                bool optional = _spec.Fields.Any(f => f.Key == "Own");
                _savers.Add(c =>
                {
                    if (sprite.SelectedItem is not string name)
                    {
                        if (optional) return true;
                        MessageBox.Show(this, "Pick a sprite (make one in the Sprite Editor first).", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    c.Parameters["SpriteGroup"] = group.SelectedItem as string ?? "";
                    c.Parameters["SpriteName"] = name;
                    return true;
                });
                break;
            }
            case ScriptFieldKind.Sound:
            {
                AddLabel(field.Label);
                var sounds = (ProjectSession.ResourcesProvider?.Invoke()?.Entries ?? new List<ResourceEntry>()).Where(r => r.Kind is ResourceKind.Sound or ResourceKind.Music).ToList();
                var combo = new ComboBox { ItemsSource = sounds, DisplayMemberPath = "Name" };
                combo.SelectedItem = sounds.FirstOrDefault(r => r.Id.ToString() == Existing(field.Key, "")) ?? sounds.FirstOrDefault();
                Rows.Children.Add(combo);
                _savers.Add(c =>
                {
                    if (combo.SelectedItem is not ResourceEntry entry)
                    {
                        MessageBox.Show(this, "Pick a sound or a piece of music (add one in the Sound Editor first).", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    c.Parameters[field.Key] = entry.Id.ToString();
                    return true;
                });
                break;
            }
            case ScriptFieldKind.CommonEvent:
            {
                AddLabel(field.Label);
                var events = ProjectSession.SettingsProvider?.Invoke()?.CommonEvents ?? new List<CommonEventDefinition>();
                var combo = new ComboBox { ItemsSource = events, DisplayMemberPath = nameof(CommonEventDefinition.Name) };
                combo.SelectedItem = events.FirstOrDefault(e => e.Id.ToString() == Existing(field.Key, "")) ?? events.FirstOrDefault();
                Rows.Children.Add(combo);
                if (events.Count == 0) AddLabel("There are no Common Events yet - make them in Tools > Common Events.", bold: false);
                _savers.Add(c =>
                {
                    if (combo.SelectedItem is not CommonEventDefinition e)
                    {
                        MessageBox.Show(this, "Pick a Common Event (Tools > Common Events).", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    c.Parameters[field.Key] = e.Id.ToString();
                    return true;
                });
                break;
            }
            case ScriptFieldKind.Condition:
                AddConditionField(field, existing);
                break;
            case ScriptFieldKind.Words:
            {
                AddLabel(field.Label);
                // New: Words. Made before the switch existed: a formula, as it was.
                bool formula = existing is not null ? existing.Get(field.Key + "Mode") != "Words" : false;
                var row = CommandFields.Row(Rows);
                var asWords = new RadioButton { Content = "Words (as typed)", GroupName = "words" + field.Key, IsChecked = !formula, Margin = new Thickness(0, 0, 12, 0) };
                var asFormula = new RadioButton { Content = "Formula (\"Gold: \" + Gold)", GroupName = "words" + field.Key, IsChecked = formula };
                row.Children.Add(asWords);
                row.Children.Add(asFormula);
                var box = new TextBox { Text = Existing(field.Key, field.Default), Margin = new Thickness(0, 4, 0, 0) };
                Rows.Children.Add(box);
                _savers.Add(c =>
                {
                    bool isFormula = asFormula.IsChecked == true;
                    if (isFormula && ScriptExpression.Check(box.Text) is { } error)
                    {
                        MessageBox.Show(this, $"{field.Label}: {error}.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                        return false;
                    }
                    c.Parameters[field.Key] = isFormula ? box.Text.Trim() : box.Text;
                    c.Parameters[field.Key + "Mode"] = isFormula ? "Formula" : "Words";
                    return true;
                });
                break;
            }
            case ScriptFieldKind.Pose:
            {
                AddLabel(field.Label);
                var poses = ProjectSession.Poses.Poses.Select(p => p.Name).ToList();
                var combo = new ComboBox { ItemsSource = poses };
                combo.SelectedItem = poses.FirstOrDefault(p => p == Existing(field.Key, "")) ?? poses.FirstOrDefault();
                Rows.Children.Add(combo);
                _savers.Add(c => { c.Parameters[field.Key] = combo.SelectedItem as string ?? ""; return true; });
                break;
            }
            case ScriptFieldKind.Faction:
            {
                AddLabel(field.Label);
                var combo = AddFactionCombo(Existing(field.Key, ""), allowNone: true);
                _savers.Add(c => { c.Parameters[field.Key] = (combo.SelectedItem as Faction)?.Id.ToString() ?? "-1"; return true; });
                break;
            }
        }
    }

    /// <summary>A condition: a formula, or any game condition (Advance IF's window).</summary>
    private void AddConditionField(ScriptField field, EventCommand? existing)
    {
        AddLabel(field.Label);
        bool game = existing?.Get("Mode") == "Game";
        var formulaChoice = new RadioButton { Content = "This formula is true:", IsChecked = !game, GroupName = "cond" + field.Key };
        var formula = new TextBox { Text = existing?.Get("Formula") ?? "", Margin = new Thickness(18, 2, 0, 4) };
        var gameChoice = new RadioButton { Content = "This game condition is true:", IsChecked = game, GroupName = "cond" + field.Key, Margin = new Thickness(0, 4, 0, 0) };
        string condition = existing?.Get("Condition") ?? "", summary = existing?.Get("Summary") ?? "";
        var pick = new Button { Content = summary.Length > 0 ? summary : "(pick a condition...)", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(18, 2, 0, 0) };
        pick.Click += (_, _) =>
        {
            var seed = new EventCommand { Type = EventCommandType.AdvancedIf };
            if (condition.Length > 0) { seed.Parameters["Condition"] = condition; seed.Parameters["Summary"] = summary; }
            var window = new AdvanceIfCommandWindow(condition.Length > 0 ? seed : null) { Owner = this };
            if (window.ShowDialog() == true && window.Results.FirstOrDefault() is { } result)
            {
                condition = result.Get("Condition");
                summary = result.Get("Summary");
                pick.Content = summary;
                gameChoice.IsChecked = true;
            }
        };
        Rows.Children.Add(formulaChoice);
        Rows.Children.Add(formula);
        Rows.Children.Add(gameChoice);
        Rows.Children.Add(pick);
        _savers.Add(c =>
        {
            if (gameChoice.IsChecked == true)
            {
                if (condition.Length == 0)
                {
                    MessageBox.Show(this, "Pick the game condition.", Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return false;
                }
                c.Parameters["Mode"] = "Game";
                c.Parameters["Condition"] = condition;
                c.Parameters["Summary"] = summary;
                return true;
            }
            string text = formula.Text.Trim();
            string? problem = text.Length == 0 ? "Type the formula (Count < 10, say)." : ScriptExpression.Check(text) is { } error ? $"The formula: {error}." : null;
            if (problem is not null)
            {
                MessageBox.Show(this, problem, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }
            c.Parameters["Mode"] = "Formula";
            c.Parameters["Formula"] = text;
            return true;
        });
    }

    protected override bool TryBuild()
    {
        var command = new EventCommand { Type = _spec.Type };
        foreach (var save in _savers)
            if (!save(command)) return false;
        Result = command;
        return true;
    }

    // ---------- How a script command reads in the list ----------

    public static string Describe(EventCommand command)
    {
        if (ScriptCommands.Of(command.Type) is not { } spec) return "";
        string text = spec.Summary;
        foreach (var field in spec.Fields)
            text = text.Replace("{" + field.Key + "}", Show(field, command));
        if (spec.Type == EventCommandType.GetGameData && command.Get("What") is "Hour" or "Minute" or "Day" or "Party size" or "Map name" or "Leader's name")
            text = $"{command.Get("Variable")} = {command.Get("What")}";
        if (spec.Type is EventCommandType.ShowHidePart or EventCommandType.SetPartLook && command.Get("DesignId") is "-1" or "")
            text = text.Replace("this paperdoll ", "");
        return text.Replace("()", "").Replace("  ", " ").Trim();
    }

    private static string Show(ScriptField field, EventCommand command) => field.Kind switch
    {
        ScriptFieldKind.Who => WhoChoice.Describe(command.Get(field.Key)),
        ScriptFieldKind.Vehicle => command.Get("VehicleName"),
        ScriptFieldKind.Item => command.Get("ItemId") is "" or "-1" ? "(none)" : CommandFields.ItemName(command),
        ScriptFieldKind.Thing => command.Get("ThingName") is { Length: > 0 } name ? $"{name} at ({command.Get("X")}, {command.Get("Y")})" : "?",
        ScriptFieldKind.Spot => SpotText(command, field.Key.EndsWith("X") ? field.Key[..^1] : field.Key),
        ScriptFieldKind.Paperdoll => command.Get("DesignName") is { Length: > 0 } doll ? doll : "this paperdoll",
        ScriptFieldKind.Sprite => $"{command.Get("SpriteGroup")}/{command.Get("SpriteName")}",
        ScriptFieldKind.Sound => int.TryParse(command.Get(field.Key), out int id) && ProjectSession.ResourcesProvider?.Invoke()?.Entries.FirstOrDefault(r => r.Id == id) is { } r ? r.Name : "?",
        ScriptFieldKind.CommonEvent => int.TryParse(command.Get(field.Key), out int ce) && ProjectSession.SettingsProvider?.Invoke()?.CommonEvents.FirstOrDefault(e => e.Id == ce) is { } e ? e.Name : "?",
        ScriptFieldKind.Condition => command.Get("Mode") == "Game" ? command.Get("Summary") : command.Get("Formula"),
        ScriptFieldKind.Check => command.Get(field.Key) == "True" ? field.Label.ToLowerInvariant() : "",
        ScriptFieldKind.LongText => command.Get(field.Key).Replace("\r", "").Replace("\n", " | "),
        _ => command.Get(field.Key)
    };

    private static string SpotText(EventCommand command, string prefix) =>
        command.Get(prefix + "X") is { Length: > 0 } x ? $"({x}, {command.Get(prefix + "Y")}) floor {command.Get(prefix + "Floor")}" : "";
}
