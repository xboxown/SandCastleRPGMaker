using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Schedule tab: Exult's daily schedule for a character - the day in
/// eight 3-hour periods, each optionally changing where the character goes
/// and what it does there. Disabled (and emptied) for the main player, who
/// is controlled by the person playing, not by AI.
/// </summary>
public partial class CharacterEditorWindow
{
    private sealed record ActivityChoice(ScheduleType? Type)
    {
        public override string ToString() => Type is { } t ? Schedules.Describe(t) : "(continue the previous)";
    }

    private void BuildSchedulePanel(CharacterDefinition character)
    {
        SchedulePanel.Children.Clear();
        ScheduleTab.IsEnabled = !character.IsTestPlayer;
        TalkTab.IsEnabled = !character.IsTestPlayer;
        MainPlayerNote.Visibility = character.IsTestPlayer ? Visibility.Visible : Visibility.Collapsed;
        if (character.IsTestPlayer) return;

        SchedulePanel.Children.Add(new TextBlock
        {
            Text = "As in Exult, the day has eight 3-hour periods. At the start of a period with an activity, the character walks to that " +
                   "period's spot (or, if it and the spot are both far off-screen, is simply placed there) and does the activity. A period left " +
                   "on \"continue the previous\" changes nothing. Party members don't follow schedules. In the game, F6 skips to the next period for testing.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 10)
        });

        var placed = ProjectSession.PlacedSpotProvider?.Invoke(character.Id);
        if (placed is null)
            SchedulePanel.Children.Add(new TextBlock
            {
                Text = "This character isn't placed on the open map yet (Map Editor > Mode > Characters) - a schedule only runs for a placed character.",
                TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DarkOrange, Margin = new Thickness(0, 0, 0, 8)
            });

        for (int period = 0; period < 8; period++)
            SchedulePanel.Children.Add(BuildScheduleRow(character, period, placed));
        BuildScheduleRules(character); // When... needs that come before the timetable
    }

    private FrameworkElement BuildScheduleRow(CharacterDefinition character, int period, (int X, int Y)? placed)
    {
        var entry = character.Schedules.FirstOrDefault(s => s.Period == period);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        row.Children.Add(new TextBlock { Text = Schedules.PeriodNames[period], Width = 70, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });

        var activity = new ComboBox { Width = 190, Margin = new Thickness(0, 0, 8, 0) };
        var choices = new List<ActivityChoice> { new(null) };
        choices.AddRange(Enum.GetValues<ScheduleType>().Select(t => new ActivityChoice(t)));
        activity.ItemsSource = choices;
        activity.SelectedItem = choices.First(c => c.Type == entry?.Type);
        activity.ToolTip = entry is null ? "Nothing changes at this time" : Schedules.Explain(entry.Type);
        row.Children.Add(activity);
        Action whereChanged = () => { }; // RefreshButtons, once it exists (below)
        var (where, syncWhere) = WhereCombo(() => entry, () => whereChanged()); // a cell, a zone, or the nearest zone of a purpose
        row.Children.Add(where);

        var spot = new TextBlock { Width = 150, VerticalAlignment = VerticalAlignment.Center };
        void ShowSpot()
        {
            if (entry is null) { spot.Text = ""; return; }
            spot.Text = PlaceText(entry, placed);
        }
        ShowSpot();

        var pick = new Button { Content = "Pick on Map...", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 4, 0), ToolTip = "Choose the spot on the open map" };
        var usePlaced = new Button { Content = "Placed Spot", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 4, 0), ToolTip = "Use wherever the character is placed in the Map Editor" };
        var custom = new Button { Content = "Commands...", Padding = new Thickness(6, 1, 6, 1), ToolTip = "The event commands this custom activity runs" };
        var workSettings = new Button { Content = "Work...", Padding = new Thickness(6, 1, 6, 1), ToolTip = "Its work area, where what it makes goes, and its work pose" };
        var customName = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0), Opacity = 0.8 };

        void RefreshButtons()
        {
            bool has = entry is not null;
            where.IsEnabled = has;
            syncWhere();
            pick.IsEnabled = has;
            usePlaced.IsEnabled = has && (entry!.X >= 0 || entry.ZoneId >= 0 || entry.ZonePurpose.Length > 0);
            custom.Visibility = entry?.Type == ScheduleType.Custom ? Visibility.Visible : Visibility.Collapsed;
            bool working = entry?.Type is ScheduleType.Farm or ScheduleType.Miner or ScheduleType.Blacksmith or ScheduleType.Bake or ScheduleType.Sew or ScheduleType.Lab;
            workSettings.Visibility = working ? Visibility.Visible : Visibility.Collapsed;
            customName.Text = entry?.Type == ScheduleType.Custom
                ? (string.IsNullOrEmpty(entry.CustomName) ? "" : $"\"{entry.CustomName}\"") + $" {entry.CustomCommands.Count} command{(entry.CustomCommands.Count == 1 ? "" : "s")}"
                : working ? $"area {(entry!.ZoneId >= 0 || entry.ZonePurpose.Length > 0 ? "its zone" : $"{entry.WorkAreaTiles} tiles")}, " + (entry.OutputX >= 0 ? $"puts it at ({entry.OutputX}, {entry.OutputY})" : "leaves it where it's made")
                : "";
            ShowSpot();
        }

        whereChanged = RefreshButtons;

        activity.SelectionChanged += (_, _) =>
        {
            var chosen = (activity.SelectedItem as ActivityChoice)?.Type;
            if (chosen is null)
            {
                if (entry is not null) character.Schedules.Remove(entry);
                entry = null;
            }
            else
            {
                if (entry is null)
                {
                    entry = new ScheduleEntry { Period = period };
                    character.Schedules.Add(entry);
                    character.Schedules.Sort((a, b) => a.Period.CompareTo(b.Period));
                }
                entry.Type = chosen.Value;
            }
            activity.ToolTip = entry is null ? "Nothing changes at this time" : Schedules.Explain(entry.Type);
            RefreshButtons();
        };
        pick.Click += (_, _) =>
        {
            if (entry is null) return;
            if (ProjectSession.MapSnapshotProvider?.Invoke() is not { } map)
            {
                MessageBox.Show(this, "Open or create a map in the Map Editor first.", "Pick on Map", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var picker = new MapCellPickerWindow(map.Picture, map.TileWidth, map.TileHeight, map.Columns, map.Rows,
                entry.X >= 0 ? (entry.X, entry.Y) : placed, placed) { Owner = this };
            if (picker.ShowDialog() == true && picker.Picked is { } cell) { entry.X = cell.X; entry.Y = cell.Y; entry.ZoneId = -1; entry.ZonePurpose = ""; RefreshButtons(); }
        };
        usePlaced.Click += (_, _) => { if (entry is null) return; entry.X = -1; entry.Y = -1; entry.ZoneId = -1; entry.ZonePurpose = ""; RefreshButtons(); };
        custom.Click += (_, _) =>
        {
            if (entry is null) return;
            new CustomScheduleWindow(entry) { Owner = this }.ShowDialog();
            RefreshButtons();
        };
        workSettings.Click += (_, _) =>
        {
            if (entry is null) return;
            new WorkScheduleWindow(entry, placed) { Owner = this }.ShowDialog();
            RefreshButtons();
        };

        row.Children.Add(spot);
        row.Children.Add(pick);
        row.Children.Add(usePlaced);
        row.Children.Add(custom);
        row.Children.Add(workSettings);
        row.Children.Add(customName);
        RefreshButtons();
        return row;
    }

    /// <summary>The Talk tab: the shared event command editor on this character's talk commands.</summary>
    private void BuildTalkEditor(CharacterDefinition character)
    {
        TalkTab.IsEnabled = !character.IsTestPlayer;
        var page = new EventCommandPage { Label = "1", Commands = character.TalkCommands };
        var editor = new EventEditorControl
        {
            ResourceDatabase = _resources,
            Variables = ProjectSession.VariablesProvider?.Invoke(),
            CurrentBookLocalVariables = new List<VariableDefinition>(),
            Books = _books,
            Objects = _objects,
            Containers = ProjectSession.ContainersProvider?.Invoke(),
            Sprites = _sprites
        };
        editor.SelfCharacter = character;
        editor.Pages = new System.Collections.ObjectModel.ObservableCollection<EventCommandPage> { page };
        editor.ContentChanged += () => character.TalkCommands = page.Commands; // the command list may be replaced wholesale (typed in and compiled)
        TalkEditorHost.Content = editor;
    }

    /// <summary>
    /// Ticking Is Main Player: the main player has no schedule and no AI, so
    /// any schedule it had is cleared (after asking). Returns false if the
    /// person changed their mind.
    /// </summary>
    private bool ConfirmBecomingMainPlayer(CharacterDefinition character)
    {
        if (character.Schedules.Count == 0) return true;
        var answer = MessageBox.Show(this,
            $"The main player is controlled by the person playing - it has no schedule and no AI.\n\nMaking \"{character.Name}\" the main player clears its {character.Schedules.Count} schedule entr{(character.Schedules.Count == 1 ? "y" : "ies")}. Continue?",
            "Is Main Player", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes) return false;
        character.Schedules.Clear();
        return true;
    }
}
