using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Factions, how each sees every other (a one-way grid: rows see columns,
/// the way 001 Game Creator's Team Alliances table reads), and the one
/// game-wide list of behaviour bands that turns a relationship percentage
/// into what a member actually does (attack, ignore, defend, flee).
/// </summary>
public partial class FactionEditorWindow : Window
{
    public FactionDatabase Database { get; private set; } = new();

    private Faction? _selected;
    private const double CellWidth = 78;
    private const double CellHeight = 52;
    private const double HeaderWidth = 170;
    private const double HeaderHeight = 84;

    public FactionEditorWindow(FactionDatabase? existing = null)
    {
        InitializeComponent();
        Title += $" [{BuildInfo.CurrentBuild}]";
        if (existing is not null) Database = existing;
        _selected = Database.Factions.FirstOrDefault();
        RebuildAll();
    }

    private void RebuildAll()
    {
        RebuildGrid();
        RebuildDetails();
        RebuildBands();
    }

    // ---------- Relationship grid ----------

    private void RebuildGrid()
    {
        RelationGrid.Children.Clear();
        RelationGrid.RowDefinitions.Clear();
        RelationGrid.ColumnDefinitions.Clear();
        var factions = Database.Factions;

        RelationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(HeaderWidth) });
        RelationGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(HeaderHeight) });
        foreach (var _ in factions)
        {
            RelationGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(CellWidth) });
            RelationGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CellHeight) });
        }

        if (factions.Count == 0)
        {
            var empty = new TextBlock { Text = "No factions yet - press Add Faction.", Margin = new Thickness(6), FontStyle = FontStyles.Italic };
            Grid.SetColumnSpan(empty, 1);
            RelationGrid.Children.Add(empty);
            return;
        }

        var corner = new TextBlock { Text = "Row sees column →", VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(4), Opacity = 0.7, FontStyle = FontStyles.Italic };
        RelationGrid.Children.Add(corner);

        for (int i = 0; i < factions.Count; i++)
        {
            var faction = factions[i];
            var column = BuildColumnHeader(faction);
            Grid.SetColumn(column, i + 1);
            RelationGrid.Children.Add(column);

            var row = BuildRowHeader(faction);
            Grid.SetRow(row, i + 1);
            RelationGrid.Children.Add(row);

            for (int j = 0; j < factions.Count; j++)
            {
                var cell = BuildCell(faction, factions[j]);
                Grid.SetRow(cell, i + 1);
                Grid.SetColumn(cell, j + 1);
                RelationGrid.Children.Add(cell);
            }
        }
    }

    private Image FaceOf(Faction faction, double size) => new()
    {
        Source = ProjectSession.FirstFrame(ProjectSession.FindSprite(faction.FaceSpriteGroup, faction.FaceSpriteName)),
        Width = size, Height = size, Stretch = Stretch.Uniform,
        SnapsToDevicePixels = true
    };

    private Button DeleteButtonFor(Faction faction)
    {
        var button = new Button
        {
            Content = "×", Width = 18, Height = 18, Padding = new Thickness(0), FontWeight = FontWeights.Bold,
            Foreground = Brushes.DarkRed, ToolTip = $"Delete \"{faction.Name}\"", VerticalAlignment = VerticalAlignment.Top
        };
        button.Click += (_, _) => DeleteFaction(faction);
        return button;
    }

    private Brush HeaderBackground(Faction faction) => faction == _selected ? new SolidColorBrush(Color.FromRgb(0xCC, 0xE4, 0xFF)) : new SolidColorBrush(Color.FromRgb(0xF2, 0xF2, 0xF2));

    private FrameworkElement BuildColumnHeader(Faction faction)
    {
        var face = FaceOf(faction, 36);
        RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.NearestNeighbor);
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        stack.Children.Add(face);
        stack.Children.Add(new TextBlock { Text = faction.Name, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, FontSize = 11, MaxHeight = 30 });
        var grid = new Grid();
        grid.Children.Add(stack);
        var delete = DeleteButtonFor(faction);
        delete.HorizontalAlignment = HorizontalAlignment.Right;
        grid.Children.Add(delete);
        var border = new Border { Child = grid, Background = HeaderBackground(faction), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5), Padding = new Thickness(2), Cursor = Cursors.Hand, ToolTip = faction.Name };
        border.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not Button) SelectFaction(faction); };
        return border;
    }

    private FrameworkElement BuildRowHeader(Faction faction)
    {
        var face = FaceOf(faction, 36);
        RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.NearestNeighbor);
        var dock = new DockPanel { LastChildFill = true };
        var delete = DeleteButtonFor(faction);
        DockPanel.SetDock(delete, Dock.Right);
        dock.Children.Add(delete);
        DockPanel.SetDock(face, Dock.Left);
        face.Margin = new Thickness(0, 0, 6, 0);
        dock.Children.Add(face);
        dock.Children.Add(new TextBlock { Text = faction.Name, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
        var border = new Border { Child = dock, Background = HeaderBackground(faction), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5), Padding = new Thickness(4, 2, 2, 2), Cursor = Cursors.Hand, ToolTip = faction.Name };
        border.MouseLeftButtonDown += (_, e) => { if (e.OriginalSource is not Button) SelectFaction(faction); };
        return border;
    }

    private static Brush BandBrush(FactionBand? band)
    {
        try { if (band is not null) return new SolidColorBrush((Color)ColorConverter.ConvertFromString(band.ColorHex)); }
        catch { }
        return Brushes.White;
    }

    private FrameworkElement BuildCell(Faction viewer, Faction target)
    {
        int value = Database.GetRelation(viewer.Id, target.Id);
        var band = Database.BandFor(value);
        var box = new TextBox
        {
            Text = value.ToString(), Width = 34, TextAlignment = TextAlignment.Right, Background = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 1), FontWeight = FontWeights.Bold
        };
        var bandLabel = new TextBlock { Text = band?.Name ?? "", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.8 };
        var line = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
        line.Children.Add(box);
        line.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center });
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        stack.Children.Add(line);
        stack.Children.Add(bandLabel);
        var border = new Border
        {
            Child = stack, Background = BandBrush(band), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0.5),
            ToolTip = $"How {viewer.Name} sees {target.Name}" + (viewer == target ? " (its own members)" : "")
        };

        void Commit()
        {
            if (!int.TryParse(box.Text.Trim().TrimEnd('%'), out int newValue)) newValue = Database.GetRelation(viewer.Id, target.Id);
            newValue = Math.Clamp(newValue, 0, 100);
            Database.SetRelation(viewer.Id, target.Id, newValue);
            box.Text = newValue.ToString();
            var newBand = Database.BandFor(newValue);
            bandLabel.Text = newBand?.Name ?? "";
            border.Background = BandBrush(newBand);
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); e.Handled = true; } };
        box.GotKeyboardFocus += (_, _) => box.SelectAll();
        return border;
    }

    private void SelectFaction(Faction faction)
    {
        _selected = faction;
        RebuildGrid();
        RebuildDetails();
    }

    // ---------- Selected faction's details ----------

    private sealed record SpriteChoice(string Group, SpriteEntry Sprite)
    {
        public override string ToString() => $"{Group} / {Sprite.Name}";
    }

    private void RebuildDetails()
    {
        DetailsPanel.Children.Clear();
        if (_selected is not { } faction || !Database.Factions.Contains(faction))
        {
            DetailsPanel.Children.Add(new TextBlock { Text = "Select a faction (click its face or name).", TextWrapping = TextWrapping.Wrap, FontStyle = FontStyles.Italic });
            return;
        }

        DetailsPanel.Children.Add(new TextBlock { Text = "Name", FontWeight = FontWeights.Bold });
        var nameBox = new TextBox { Text = faction.Name, Margin = new Thickness(0, 2, 0, 10) };
        nameBox.LostFocus += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(nameBox.Text) || nameBox.Text == faction.Name) { nameBox.Text = faction.Name; return; }
            faction.Name = nameBox.Text.Trim();
            RebuildGrid();
        };
        nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
        DetailsPanel.Children.Add(nameBox);

        DetailsPanel.Children.Add(new TextBlock { Text = "Face", FontWeight = FontWeights.Bold });
        var choices = ProjectSession.Sprites.Groups.SelectMany(g => g.Sprites.Select(s => new SpriteChoice(g.Name, s))).ToList();
        var faceCombo = new ComboBox { Margin = new Thickness(0, 2, 0, 4) };
        faceCombo.Items.Add("(None)");
        foreach (var choice in choices) faceCombo.Items.Add(choice);
        faceCombo.SelectedItem = choices.FirstOrDefault(c => c.Group == faction.FaceSpriteGroup && c.Sprite.Name == faction.FaceSpriteName)
            ?? choices.FirstOrDefault(c => c.Sprite.Name == faction.FaceSpriteName) as object ?? "(None)";
        var preview = FaceOf(faction, 48);
        RenderOptions.SetBitmapScalingMode(preview, BitmapScalingMode.NearestNeighbor);
        preview.HorizontalAlignment = HorizontalAlignment.Left;
        preview.Margin = new Thickness(0, 0, 0, 10);
        faceCombo.SelectionChanged += (_, _) =>
        {
            if (faceCombo.SelectedItem is SpriteChoice chosen) { faction.FaceSpriteGroup = chosen.Group; faction.FaceSpriteName = chosen.Sprite.Name; }
            else { faction.FaceSpriteGroup = string.Empty; faction.FaceSpriteName = string.Empty; }
            preview.Source = ProjectSession.FirstFrame(ProjectSession.FindSprite(faction.FaceSpriteGroup, faction.FaceSpriteName));
            RebuildGrid();
        };
        DetailsPanel.Children.Add(faceCombo);
        DetailsPanel.Children.Add(preview);

        DetailsPanel.Children.Add(new TextBlock { Text = "When a member is wronged", FontWeight = FontWeights.Bold });
        DetailsPanel.Children.Add(new TextBlock
        {
            Text = "How many % this faction's opinion of the offender's faction drops (one-way). Like Exult, the victim also turns on the offender at once.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 4)
        });
        DetailsPanel.Children.Add(PenaltyRow("Attacked:", () => faction.AttackPenalty, v => faction.AttackPenalty = v));
        DetailsPanel.Children.Add(PenaltyRow("Robbed:", () => faction.TheftPenalty, v => faction.TheftPenalty = v));
        DetailsPanel.Children.Add(PenaltyRow("Killed:", () => faction.KillPenalty, v => faction.KillPenalty = v));

        DetailsPanel.Children.Add(new TextBlock { Text = "Notes", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 0) });
        var notes = new TextBox { Text = faction.Notes, Height = 90, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        notes.TextChanged += (_, _) => faction.Notes = notes.Text;
        DetailsPanel.Children.Add(notes);

        var members = ProjectSession.Characters.Characters.Where(c => c.FactionId == faction.Id).Select(c => c.Name).ToList();
        DetailsPanel.Children.Add(new TextBlock
        {
            Text = members.Count == 0 ? "No characters belong to it yet (set it in the Character Editor)." : $"Members ({members.Count}): {string.Join(", ", members)}",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8
        });
    }

    private static FrameworkElement PenaltyRow(string label, Func<int> get, Action<int> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        row.Children.Add(new TextBlock { Text = label, Width = 70, VerticalAlignment = VerticalAlignment.Center });
        var box = new TextBox { Text = get().ToString(), Width = 44, TextAlignment = TextAlignment.Right };
        box.LostFocus += (_, _) =>
        {
            if (int.TryParse(box.Text.Trim().TrimEnd('%'), out int v)) set(Math.Clamp(v, 0, 100));
            box.Text = get().ToString();
        };
        row.Children.Add(box);
        row.Children.Add(new TextBlock { Text = " %", VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    // ---------- Add / delete ----------

    private void NewFaction_Click(object sender, RoutedEventArgs e)
    {
        Keyboard.ClearFocus(); // commit whatever cell is being typed in first
        _selected = Database.CreateNew();
        RebuildGrid();
        RebuildDetails();
    }

    private void DeleteFaction(Faction faction)
    {
        Keyboard.ClearFocus();
        var users = ProjectSession.Characters.Characters.Where(c => c.FactionId == faction.Id).ToList();
        if (users.Count == 0)
        {
            if (MessageBox.Show(this, $"Delete the faction \"{faction.Name}\"?", "Delete Faction", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            RemoveFaction(faction);
            return;
        }

        string names = string.Join(", ", users.Take(8).Select(c => c.Name)) + (users.Count > 8 ? ", ..." : "");
        var answer = MessageBox.Show(this,
            $"\"{faction.Name}\" can't be deleted: {users.Count} character{(users.Count == 1 ? "" : "s")} belong{(users.Count == 1 ? "s" : "")} to it ({names}).\n\n" +
            "Remove the faction from them and delete it?\nYou'll then get the list of those characters so you can give each a new faction.",
            "Faction In Use", MessageBoxButton.YesNo, MessageBoxImage.Error);
        if (answer != MessageBoxResult.Yes) return;

        foreach (var character in users) character.FactionId = -1;
        RemoveFaction(faction);
        new FactionlessCharactersWindow(users) { Owner = this }.ShowDialog();
        RebuildDetails(); // member lists may have changed
    }

    private void RemoveFaction(Faction faction)
    {
        Database.Remove(faction);
        if (_selected == faction) _selected = Database.Factions.FirstOrDefault();
        RebuildGrid();
        RebuildDetails();
    }

    // ---------- Behaviour bands ----------

    private TextBlock? _bandTotal;
    private Grid? _bandBar;

    private void RebuildBands()
    {
        BandsPanel.Children.Clear();
        BandsPanel.Children.Add(new TextBlock
        {
            Text = "One scale for the whole game. Bands run from 0% upward in this order and must add up to exactly 100%. " +
                   "Whatever band a relationship falls in decides what that faction's members do: Attack on sight, Ignore (neutral), Ally (defend them), or Flee (fear).",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10), Opacity = 0.8
        });

        _bandBar = new Grid { Height = 34, Margin = new Thickness(0, 0, 0, 4) };
        BandsPanel.Children.Add(_bandBar);
        _bandTotal = new TextBlock { FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 10) };
        BandsPanel.Children.Add(_bandTotal);

        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        foreach (var (text, width) in new[] { ("", 52.0), ("Name", 150.0), ("Width", 70.0), ("Range", 80.0), ("Behaviour", 170.0), ("Colour", 110.0) })
            header.Children.Add(new TextBlock { Text = text, Width = width, FontWeight = FontWeights.Bold });
        BandsPanel.Children.Add(header);

        int start = 0;
        for (int i = 0; i < Database.Bands.Count; i++)
        {
            BandsPanel.Children.Add(BuildBandRow(Database.Bands[i], i, start));
            start += Database.Bands[i].WidthPercent;
        }

        var add = new Button { Content = "Add Band", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 6, 0, 16) };
        add.Click += (_, _) =>
        {
            Keyboard.ClearFocus();
            int remaining = Math.Max(0, 100 - Database.TotalBandPercent());
            Database.Bands.Add(new FactionBand { Name = "New Band", WidthPercent = remaining, Behavior = FactionBehavior.Ignore });
            RebuildBands();
            RebuildGrid();
        };
        BandsPanel.Children.Add(add);

        var defaultRow = new StackPanel { Orientation = Orientation.Horizontal };
        defaultRow.Children.Add(new TextBlock { Text = "Relationship to and from characters with no faction, and between factions not set in the grid: ", VerticalAlignment = VerticalAlignment.Center });
        var defaultBox = new TextBox { Text = Database.DefaultRelation.ToString(), Width = 40, TextAlignment = TextAlignment.Right };
        defaultBox.LostFocus += (_, _) =>
        {
            if (int.TryParse(defaultBox.Text.Trim().TrimEnd('%'), out int v)) Database.DefaultRelation = Math.Clamp(v, 0, 100);
            defaultBox.Text = Database.DefaultRelation.ToString();
            RebuildGrid();
        };
        defaultRow.Children.Add(defaultBox);
        defaultRow.Children.Add(new TextBlock { Text = " %", VerticalAlignment = VerticalAlignment.Center });
        BandsPanel.Children.Add(defaultRow);

        RefreshBandSummary();
    }

    private FrameworkElement BuildBandRow(FactionBand band, int index, int start)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };

        var up = new Button { Content = "▲", Width = 22, Margin = new Thickness(0, 0, 2, 0), IsEnabled = index > 0, ToolTip = "Move down the scale (lower %)" };
        up.Click += (_, _) => MoveBand(index, -1);
        var down = new Button { Content = "▼", Width = 22, Margin = new Thickness(0, 0, 8, 0), IsEnabled = index < Database.Bands.Count - 1, ToolTip = "Move up the scale (higher %)" };
        down.Click += (_, _) => MoveBand(index, +1);
        row.Children.Add(up);
        row.Children.Add(down);

        var name = new TextBox { Text = band.Name, Width = 140, Margin = new Thickness(0, 0, 10, 0) };
        name.LostFocus += (_, _) => { band.Name = string.IsNullOrWhiteSpace(name.Text) ? band.Name : name.Text.Trim(); RefreshBandSummary(); RebuildGrid(); };
        row.Children.Add(name);

        var width = new TextBox { Text = band.WidthPercent.ToString(), Width = 44, TextAlignment = TextAlignment.Right };
        width.LostFocus += (_, _) =>
        {
            if (int.TryParse(width.Text.Trim().TrimEnd('%'), out int v)) band.WidthPercent = Math.Clamp(v, 0, 100);
            RebuildBands();
            RebuildGrid();
        };
        width.KeyDown += (_, e) => { if (e.Key == Key.Enter) Keyboard.ClearFocus(); };
        row.Children.Add(width);
        row.Children.Add(new TextBlock { Text = " %", Width = 26, VerticalAlignment = VerticalAlignment.Center });

        int end = Math.Min(100, start + band.WidthPercent - 1);
        row.Children.Add(new TextBlock { Text = band.WidthPercent <= 0 ? "(empty)" : $"{start}–{(index == Database.Bands.Count - 1 ? Math.Max(end, 100) : end)}%", Width = 80, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.75 });

        var behavior = new ComboBox { Width = 160, Margin = new Thickness(0, 0, 10, 0) };
        foreach (var kind in Enum.GetValues<FactionBehavior>()) behavior.Items.Add(new ComboBoxItem { Content = BehaviorLabel(kind), Tag = kind });
        behavior.SelectedItem = behavior.Items.Cast<ComboBoxItem>().First(i => (FactionBehavior)i.Tag == band.Behavior);
        behavior.SelectionChanged += (_, _) => { if (behavior.SelectedItem is ComboBoxItem { Tag: FactionBehavior chosen }) band.Behavior = chosen; };
        row.Children.Add(behavior);

        var swatch = new Border { Width = 18, Height = 18, Background = BandBrush(band), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 4, 0) };
        var color = new TextBox { Text = band.ColorHex, Width = 70, Margin = new Thickness(0, 0, 10, 0), ToolTip = "#RRGGBB - only colours the grid" };
        color.LostFocus += (_, _) =>
        {
            try { ColorConverter.ConvertFromString(color.Text.Trim()); band.ColorHex = color.Text.Trim(); } catch { color.Text = band.ColorHex; }
            swatch.Background = BandBrush(band);
            RefreshBandSummary();
            RebuildGrid();
        };
        row.Children.Add(swatch);
        row.Children.Add(color);

        var delete = new Button { Content = "×", Width = 22, FontWeight = FontWeights.Bold, Foreground = Brushes.DarkRed, ToolTip = "Delete this band", IsEnabled = Database.Bands.Count > 1 };
        delete.Click += (_, _) =>
        {
            Keyboard.ClearFocus();
            Database.Bands.Remove(band);
            RebuildBands();
            RebuildGrid();
        };
        row.Children.Add(delete);
        return row;
    }

    private static string BehaviorLabel(FactionBehavior kind) => kind switch
    {
        FactionBehavior.Attack => "Attack on sight",
        FactionBehavior.Ignore => "Ignore (neutral)",
        FactionBehavior.Ally => "Ally (defend)",
        _ => "Flee (fear)"
    };

    private void MoveBand(int index, int delta)
    {
        Keyboard.ClearFocus();
        int target = index + delta;
        if (target < 0 || target >= Database.Bands.Count) return;
        (Database.Bands[index], Database.Bands[target]) = (Database.Bands[target], Database.Bands[index]);
        RebuildBands();
        RebuildGrid();
    }

    private void RefreshBandSummary()
    {
        if (_bandBar is null || _bandTotal is null) return;
        _bandBar.Children.Clear();
        _bandBar.ColumnDefinitions.Clear();
        int column = 0;
        foreach (var band in Database.Bands.Where(b => b.WidthPercent > 0))
        {
            _bandBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(band.WidthPercent, GridUnitType.Star) });
            var cell = new Border
            {
                Background = BandBrush(band), BorderBrush = Brushes.DimGray, BorderThickness = new Thickness(0.5),
                Child = new TextBlock { Text = $"{band.Name}\n{BehaviorLabel(band.Behavior)}", FontSize = 10, TextAlignment = TextAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }
            };
            Grid.SetColumn(cell, column++);
            _bandBar.Children.Add(cell);
        }
        int total = Database.TotalBandPercent();
        if (total < 100)
        {
            _bandBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100 - total, GridUnitType.Star) });
            var gap = new Border { Background = Brushes.White, BorderBrush = Brushes.Red, BorderThickness = new Thickness(1), Child = new TextBlock { Text = "unassigned", Foreground = Brushes.Red, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            Grid.SetColumn(gap, column);
            _bandBar.Children.Add(gap);
        }
        _bandTotal.Text = total == 100 ? "Total: 100% ✔" : $"Total: {total}% - must be exactly 100%";
        _bandTotal.Foreground = total == 100 ? Brushes.DarkGreen : Brushes.Red;
    }

    // ---------- Save / load / close ----------

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        Keyboard.ClearFocus();
        var dialog = new SaveFileDialog { Filter = "Faction DB JSON (*.factiondb.json)|*.factiondb.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        FactionDatabaseSerializer.Save(Database, dialog.FileName);
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Faction DB JSON (*.factiondb.json)|*.factiondb.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        var loaded = FactionDatabaseSerializer.Load(dialog.FileName);
        // Copied into the project's own database rather than swapping it, so every other window looking at the project's factions sees the loaded ones.
        Database.Factions = loaded.Factions;
        Database.Bands = loaded.Bands;
        Database.DefaultRelation = loaded.DefaultRelation;
        Database.SyncNextIdAfterLoad();
        _selected = Database.Factions.FirstOrDefault();
        RebuildAll();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Commit a cell still being typed in, then insist on a whole scale.
        if (Keyboard.FocusedElement is TextBox) Keyboard.ClearFocus();
        int total = Database.TotalBandPercent();
        if (total == 100) return;
        MessageBox.Show(this, $"The behaviour bands add up to {total}%. They must total exactly 100% - adjust their widths before closing.",
            "Faction Editor", MessageBoxButton.OK, MessageBoxImage.Warning);
        Tabs.SelectedItem = BandsTab;
        e.Cancel = true;
    }
}
