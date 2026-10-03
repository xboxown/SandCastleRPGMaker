using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>Who a character command acts on: whoever set the event off, the main player, or a named character.</summary>
internal sealed record WhoChoice(string Key, string Label)
{
    public override string ToString() => Label;

    public const string Activator = "Activator";
    public const string Player = "Player";

    public static List<WhoChoice> Build(bool includePlayer)
    {
        var list = new List<WhoChoice> { new(Activator, "(Whoever set this event off - the player, or a character)") };
        if (includePlayer) list.Add(new(Player, "(Main Player)"));
        list.AddRange(ProjectSession.Characters.Characters.Where(c => !c.IsTestPlayer).Select(c => new WhoChoice(c.Id.ToString(), c.Name)));
        return list;
    }

    public static string Describe(string key) => key switch
    {
        "" or Activator => "whoever set it off",
        Player => "Main Player",
        _ => ProjectSession.Characters.Characters.FirstOrDefault(c => c.Id.ToString() == key)?.Name ?? $"character #{key}"
    };
}

/// <summary>
/// Teleport: moves the player or a character to a cell of this map at once.
/// "Whoever set it off" makes a teleporter tile work on characters walking
/// over it exactly as it does on the player.
/// </summary>
public class TeleportCommandWindow : FactionCommandWindowBase
{
    private readonly ComboBox _who;
    private readonly TextBlock _spotText;
    private (int X, int Y)? _spot;

    public TeleportCommandWindow(EventCommand? existing = null) : base("Teleport")
    {
        AddLabel("Who");
        _who = new ComboBox();
        var choices = WhoChoice.Build(includePlayer: true);
        _who.ItemsSource = choices;
        _who.SelectedItem = choices.FirstOrDefault(c => c.Key == existing?.Get("Who")) ?? choices[0];
        Rows.Children.Add(_who);

        AddLabel("To");
        if (int.TryParse(existing?.Get("X"), out int x) && int.TryParse(existing?.Get("Y"), out int y)) _spot = (x, y);
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        _spotText = new TextBlock { Width = 150, VerticalAlignment = VerticalAlignment.Center };
        var pick = new Button { Content = "Pick on Map...", Padding = new Thickness(8, 1, 8, 1) };
        pick.Click += (_, _) =>
        {
            if (ProjectSession.MapSnapshotProvider?.Invoke() is not { } map)
            {
                MessageBox.Show(this, "Open or create a map in the Map Editor first.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var picker = new MapCellPickerWindow(map.Picture, map.TileWidth, map.TileHeight, map.Columns, map.Rows, _spot, null) { Owner = this };
            if (picker.ShowDialog() == true && picker.Picked is { } cell) { _spot = cell; ShowSpot(); }
        };
        line.Children.Add(_spotText);
        line.Children.Add(pick);
        Rows.Children.Add(line);
        AddLabel("On this map. They arrive standing on top of whatever is there; the event tile they land on isn't set off by arriving.", bold: false);
        ShowSpot();
    }

    private void ShowSpot() => _spotText.Text = _spot is { } s ? $"cell ({s.X}, {s.Y})" : "(no spot chosen)";

    protected override bool TryBuild()
    {
        if (_spot is not { } spot)
        {
            MessageBox.Show(this, "Pick the spot on the map.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var command = new EventCommand { Type = EventCommandType.Teleport };
        command.Parameters["Who"] = (_who.SelectedItem as WhoChoice)?.Key ?? WhoChoice.Activator;
        command.Parameters["X"] = spot.X.ToString();
        command.Parameters["Y"] = spot.Y.ToString();
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => $"{WhoChoice.Describe(command.Get("Who"))} to ({command.Get("X")}, {command.Get("Y")})";
}

/// <summary>
/// Change Behavior: Exult's set_schedule_type - a character starts doing an
/// activity right where it is, until its next schedule change; or goes back
/// to its schedule straight away.
/// </summary>
public class ChangeBehaviorCommandWindow : FactionCommandWindowBase
{
    public const string Resume = "Resume";
    private sealed record BehaviorChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly ComboBox _who, _behavior;

    public ChangeBehaviorCommandWindow(EventCommand? existing = null) : base("Change Behavior")
    {
        AddLabel("Character");
        _who = new ComboBox();
        var who = WhoChoice.Build(includePlayer: false);
        _who.ItemsSource = who;
        _who.SelectedItem = who.FirstOrDefault(c => c.Key == existing?.Get("CharacterId")) ?? who[0];
        Rows.Children.Add(_who);

        AddLabel("Now does");
        _behavior = new ComboBox();
        var behaviors = new List<BehaviorChoice> { new(Resume, "(Go back to its schedule)") };
        behaviors.AddRange(Enum.GetValues<ScheduleType>().Where(t => t != ScheduleType.Custom).Select(t => new BehaviorChoice(t.ToString(), Schedules.Describe(t))));
        _behavior.ItemsSource = behaviors;
        _behavior.SelectedItem = behaviors.FirstOrDefault(b => b.Key == existing?.Get("Behavior")) ?? behaviors[1];
        Rows.Children.Add(_behavior);
        AddLabel("As in Exult (set_schedule_type), it starts right where it is and keeps at it until the next hour, when it checks its schedule again - except Wait, which lasts until changed again. The main player has no AI and is never changed.", bold: false);
    }

    protected override bool TryBuild()
    {
        var command = new EventCommand { Type = EventCommandType.ChangeBehavior };
        command.Parameters["CharacterId"] = (_who.SelectedItem as WhoChoice)?.Key ?? WhoChoice.Activator;
        command.Parameters["Behavior"] = (_behavior.SelectedItem as BehaviorChoice)?.Key ?? Resume;
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command)
    {
        string behavior = command.Get("Behavior");
        string what = behavior == Resume ? "back to its schedule" : Enum.TryParse<ScheduleType>(behavior, out var t) ? Schedules.Describe(t) : behavior;
        return $"{WhoChoice.Describe(command.Get("CharacterId"))}: {what}";
    }
}
