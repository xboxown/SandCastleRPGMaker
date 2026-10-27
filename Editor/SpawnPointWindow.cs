using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A spawn point's window (Map Editor > Mode > Spawn): the characters that may appear here - each with its
/// walking-down picture (animated, no sound), how many and the chance (0-100 %) of each - and how far they roam:
/// within so many tiles of the point (farther, they come back near it), or free to wander anywhere.
/// </summary>
internal sealed class SpawnPointWindow : Window
{
    private readonly SpawnPoint _spawn;
    private readonly SpawnPoint _working;
    private readonly CharacterDatabase _characters;
    private readonly PoseDatabase _poses;
    private readonly SpriteDatabase _sprites;
    private readonly StackPanel _rows = new();

    public SpawnPointWindow(SpawnPoint spawn, CharacterDatabase characters, PoseDatabase poses, SpriteDatabase sprites, ResourceDatabase resources)
    {
        _spawn = spawn;
        _working = System.Text.Json.JsonSerializer.Deserialize<SpawnPoint>(System.Text.Json.JsonSerializer.Serialize(spawn))!;
        _characters = characters;
        _poses = poses;
        _sprites = sprites;
        Title = $"Spawn point ({spawn.X}, {spawn.Y}, floor {spawn.Floor})";
        Width = 640; Height = 520;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new DockPanel { Margin = new Thickness(10) };
        var head = new StackPanel();
        head.Children.Add(new TextBlock { Text = "Who may appear here", FontWeight = FontWeights.Bold });
        head.Children.Add(new TextBlock
        {
            Text = "When the map is first entered, each one has its chance (0-100 %) for each of how many - a copy of its own appears here (as Ultima VII's monster eggs). Once only: a save keeps it.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 2, 0, 6)
        });
        DockPanel.SetDock(head, Dock.Top);
        root.Children.Add(head);

        // ---- how far they roam ----
        var roam = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        roam.Children.Add(new TextBlock { Text = "How far they roam", FontWeight = FontWeights.Bold });
        var within = new RadioButton { Content = "Round here: within", GroupName = "roam", IsChecked = !_working.Free, VerticalAlignment = VerticalAlignment.Center };
        var tiles = new TextBox { Width = 50, Text = _working.WanderTiles.ToString(), Margin = new Thickness(6, 0, 6, 0) };
        var withinRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        withinRow.Children.Add(within);
        withinRow.Children.Add(tiles);
        withinRow.Children.Add(new TextBlock { Text = "map squares of the spawn point (wandered farther, they come back near it)", VerticalAlignment = VerticalAlignment.Center });
        roam.Children.Add(withinRow);
        var free = new RadioButton { Content = "Free to the wild: they wander anywhere - towns, caves, the end of the earth", GroupName = "roam", IsChecked = _working.Free, Margin = new Thickness(0, 4, 0, 0) };
        roam.Children.Add(free);
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        roam.Children.Add(buttons);
        DockPanel.SetDock(roam, Dock.Bottom);
        root.Children.Add(roam);

        var add = new Button { Content = "Add a character", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 6, 0, 0) };
        add.Click += (_, _) => { _working.Entries.Add(new SpawnEntry { CharacterId = Choices().FirstOrDefault()?.Id ?? -1 }); ShowRows(); };
        var list = new StackPanel();
        list.Children.Add(_rows);
        list.Children.Add(add);
        root.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;

        ok.Click += (_, _) =>
        {
            _working.Free = free.IsChecked == true;
            if (int.TryParse(tiles.Text.Trim(), out int t)) _working.WanderTiles = Math.Clamp(t, 1, 1000);
            _spawn.Entries = _working.Entries.Where(e => e.CharacterId >= 0).ToList();
            _spawn.Free = _working.Free;
            _spawn.WanderTiles = _working.WanderTiles;
            DialogResult = true;
        };
        ShowRows();
    }

    /// <summary>The characters that can be spawned: every one but the main player.</summary>
    private List<CharacterDefinition> Choices() => _characters.Characters.Where(c => !c.IsTestPlayer).OrderBy(c => c.Name).ToList();

    /// <summary>Its Walking pose facing down (south), else its Idle one.</summary>
    private SpriteEntry? WalkingDown(CharacterDefinition? character)
    {
        if (character is null) return null;
        foreach (var name in new[] { "Walking", "Idle" })
            if (_poses.Poses.FirstOrDefault(p => p.Name == name) is { } pose && character.PoseSprites.TryGetValue(pose.Id, out var assignment) && assignment.Resolve(SpriteDirection.South) is { } link
                && ObjectLooks.Find(_sprites, link.SpriteGroup, link.SpriteName) is { } sprite)
                return sprite;
        return null;
    }

    private void ShowRows()
    {
        _rows.Children.Clear();
        var choices = Choices();
        foreach (var entry in _working.Entries.ToList())
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
            var picture = new SpritePreview { Width = 48, Height = 48, Sprite = WalkingDown(choices.FirstOrDefault(c => c.Id == entry.CharacterId)) };
            row.Children.Add(new Border { Background = Brushes.Black, Child = picture, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 8, 0) });
            var who = new ComboBox { Width = 200, ItemsSource = choices, DisplayMemberPath = nameof(CharacterDefinition.Name), SelectedItem = choices.FirstOrDefault(c => c.Id == entry.CharacterId), VerticalAlignment = VerticalAlignment.Center };
            who.SelectionChanged += (_, _) => { entry.CharacterId = (who.SelectedItem as CharacterDefinition)?.Id ?? -1; picture.Sprite = WalkingDown(who.SelectedItem as CharacterDefinition); };
            row.Children.Add(who);
            row.Children.Add(new TextBlock { Text = "how many", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) });
            var count = new TextBox { Width = 40, Text = entry.Count.ToString(), VerticalAlignment = VerticalAlignment.Center };
            count.LostFocus += (_, _) => { if (int.TryParse(count.Text.Trim(), out int n)) entry.Count = Math.Clamp(n, 1, 50); count.Text = entry.Count.ToString(); };
            row.Children.Add(count);
            row.Children.Add(new TextBlock { Text = "chance", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 4, 0) });
            var percent = new TextBox { Width = 40, Text = entry.Percent.ToString(), VerticalAlignment = VerticalAlignment.Center };
            percent.LostFocus += (_, _) => { if (int.TryParse(percent.Text.Trim(), out int p)) entry.Percent = Math.Clamp(p, 0, 100); percent.Text = entry.Percent.ToString(); };
            row.Children.Add(percent);
            row.Children.Add(new TextBlock { Text = "%", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) });
            var remove = new Button { Content = "✕", Width = 22, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Remove it" };
            remove.Click += (_, _) => { _working.Entries.Remove(entry); ShowRows(); };
            row.Children.Add(remove);
            _rows.Children.Add(row);
        }
        if (_working.Entries.Count == 0) _rows.Children.Add(new TextBlock { Text = "Nobody yet - add a character.", Opacity = 0.6, Margin = new Thickness(0, 4, 0, 4) });
    }
}
