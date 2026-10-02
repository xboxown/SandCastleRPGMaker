using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>Shared frame for the small faction command dialogs: a titled stack of rows, OK/Cancel.</summary>
public abstract class FactionCommandWindowBase : Window
{
    public EventCommand Result { get; protected set; } = new();
    protected readonly StackPanel Rows = new() { Margin = new Thickness(12) };

    protected FactionCommandWindowBase(string title)
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel();
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12, 0, 12, 12) };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        ok.Click += (_, _) => { if (TryBuild()) DialogResult = true; };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(Rows);
        Content = root;
    }

    /// <summary>Fills Result from the controls; false (after telling the user why) keeps the dialog open.</summary>
    protected abstract bool TryBuild();

    protected void AddLabel(string text, bool bold = true) =>
        Rows.Children.Add(new TextBlock { Text = text, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 2) });

    protected ComboBox AddFactionCombo(string existingId, bool allowNone)
    {
        var combo = new ComboBox { DisplayMemberPath = nameof(Faction.Name) };
        var choices = new List<Faction>();
        if (allowNone) choices.Add(new Faction { Id = -1, Name = "(None)" });
        choices.AddRange(ProjectSession.Factions.Factions);
        combo.ItemsSource = choices;
        combo.SelectedItem = choices.FirstOrDefault(f => f.Id.ToString() == existingId) ?? choices.FirstOrDefault();
        Rows.Children.Add(combo);
        return combo;
    }

    protected static string FactionName(string id) =>
        int.TryParse(id, out int value) ? ProjectSession.Factions.Find(value)?.Name ?? (value < 0 ? "(None)" : $"faction #{value}") : "?";
}

/// <summary>
/// Change Faction Relationship: how one faction sees another (one-way, like
/// the grid), set to a value or moved up/down by an amount - optionally the
/// same change the other way round too.
/// </summary>
public class ChangeFactionRelationshipCommandWindow : FactionCommandWindowBase
{
    private readonly ComboBox _viewer, _target, _operation;
    private readonly TextBox _value;
    private readonly CheckBox _bothWays;

    public ChangeFactionRelationshipCommandWindow(EventCommand? existing = null) : base("Change Faction Relationship")
    {
        AddLabel("How this faction...");
        _viewer = AddFactionCombo(existing?.Get("ViewerFactionId") ?? "", allowNone: false);
        AddLabel("...sees this faction");
        _target = AddFactionCombo(existing?.Get("TargetFactionId") ?? "", allowNone: false);

        AddLabel("Change");
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        _operation = new ComboBox { Width = 110, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var op in new[] { "Set to", "Increase by", "Decrease by" }) _operation.Items.Add(op);
        _operation.SelectedIndex = existing?.Get("Operation") switch { "Add" => 1, "Subtract" => 2, _ => 0 };
        _value = new TextBox { Width = 50, Text = existing?.Get("Value") is { Length: > 0 } v ? v : "50", TextAlignment = TextAlignment.Right };
        line.Children.Add(_operation);
        line.Children.Add(_value);
        line.Children.Add(new TextBlock { Text = " %  (0-100, the result is kept within 0-100)", VerticalAlignment = VerticalAlignment.Center });
        Rows.Children.Add(line);

        _bothWays = new CheckBox { Content = "Both ways (the second faction's view of the first changes the same way)", Margin = new Thickness(0, 10, 0, 0), IsChecked = existing?.Get("BothWays") == "True" };
        Rows.Children.Add(_bothWays);

        if (ProjectSession.Factions.Factions.Count == 0)
            AddLabel("There are no factions yet - add some in Tools > Faction Editor first.", bold: false);
    }

    protected override bool TryBuild()
    {
        if (_viewer.SelectedItem is not Faction viewer || _target.SelectedItem is not Faction target)
        {
            MessageBox.Show(this, "Choose both factions.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (!int.TryParse(_value.Text.Trim().TrimEnd('%'), out int amount) || amount < 0 || amount > 100)
        {
            MessageBox.Show(this, "The amount must be a whole number from 0 to 100.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var command = new EventCommand { Type = EventCommandType.ChangeFactionRelationship };
        command.Parameters["ViewerFactionId"] = viewer.Id.ToString();
        command.Parameters["TargetFactionId"] = target.Id.ToString();
        command.Parameters["Operation"] = _operation.SelectedIndex switch { 1 => "Add", 2 => "Subtract", _ => "Set" };
        command.Parameters["Value"] = amount.ToString();
        command.Parameters["BothWays"] = (_bothWays.IsChecked == true).ToString();
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command)
    {
        string op = command.Get("Operation") switch { "Add" => "+", "Subtract" => "-", _ => "= " };
        string arrow = command.Get("BothWays") == "True" ? "↔" : "→";
        return $"{FactionName(command.Get("ViewerFactionId"))} {arrow} {FactionName(command.Get("TargetFactionId"))} {op}{command.Get("Value")}%";
    }
}

/// <summary>Change Faction: moves a character (or the main player) into another faction, or out of every faction.</summary>
public class ChangeFactionCommandWindow : FactionCommandWindowBase
{
    public const string MainPlayer = "Player";
    private sealed record CharacterChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ComboBox _character, _faction;

    public ChangeFactionCommandWindow(EventCommand? existing = null) : base("Change Faction")
    {
        AddLabel("Character");
        _character = new ComboBox();
        var choices = new List<CharacterChoice> { new(MainPlayer, "(Main Player)") };
        choices.AddRange(ProjectSession.Characters.Characters.Select(c => new CharacterChoice(c.Id.ToString(), c.Name)));
        _character.ItemsSource = choices;
        _character.SelectedItem = choices.FirstOrDefault(c => c.Key == existing?.Get("CharacterId")) ?? choices[0];
        Rows.Children.Add(_character);

        AddLabel("Joins faction");
        _faction = AddFactionCombo(existing?.Get("FactionId") ?? "", allowNone: true);
        AddLabel("Its members now treat it - and it treats everyone - by the new faction's relationships.", bold: false);
    }

    protected override bool TryBuild()
    {
        if (_character.SelectedItem is not CharacterChoice character || _faction.SelectedItem is not Faction faction) return false;
        var command = new EventCommand { Type = EventCommandType.ChangeFaction };
        command.Parameters["CharacterId"] = character.Key;
        command.Parameters["FactionId"] = faction.Id.ToString();
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command)
    {
        string id = command.Get("CharacterId");
        string who = id == MainPlayer ? "Main Player"
            : ProjectSession.Characters.Characters.FirstOrDefault(c => c.Id.ToString() == id)?.Name ?? $"character #{id}";
        return $"{who} joins {FactionName(command.Get("FactionId"))}";
    }
}
