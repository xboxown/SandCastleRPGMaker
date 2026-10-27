using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A spawn point's - an egg's - window (Map Editor > Mode > Spawn), with everything Exult's eggs have: what it is
/// (a monster egg - who may appear, each with its walking-down picture, how many and their chance, and how far they
/// roam - or music, a sound, a Common Event, a missile, a teleport, weather, a path spot, a button), when it hatches,
/// its chance, its distance, and whether it's nocturnal, once only, or resets.
/// </summary>
internal sealed class SpawnPointWindow : Window
{
    private readonly SpawnPoint _spawn;
    private readonly SpawnPoint _working;
    private readonly CharacterDatabase _characters;
    private readonly PoseDatabase _poses;
    private readonly SpriteDatabase _sprites;
    private readonly ResourceDatabase _resources;
    private readonly StackPanel _kindPanel = new();
    private readonly StackPanel _rows = new();

    private static readonly (EggKind Kind, string Name)[] Kinds =
    {
        (EggKind.Monster, "Characters appear (spawn point - monster egg)"), (EggKind.Music, "Music plays (jukebox egg)"), (EggKind.Sound, "A sound plays (sound egg)"),
        (EggKind.Usecode, "A Common Event runs (usecode egg)"), (EggKind.Missile, "Shoots a projectile (missile egg)"), (EggKind.Teleport, "Takes the party somewhere (teleport egg)"),
        (EggKind.Weather, "The weather changes (weather egg)"), (EggKind.Path, "A spot teleports can go to (path egg)"), (EggKind.Button, "Sets off nearby eggs (button egg)")
    };

    private static readonly (EggCriteria Criteria, string Name)[] Criterias =
    {
        (EggCriteria.CachedIn, "As the map is entered"), (EggCriteria.PartyNear, "Someone in the party comes near"), (EggCriteria.AvatarNear, "The main character comes near"),
        (EggCriteria.AvatarFar, "The main character goes away from it"), (EggCriteria.AvatarFootpad, "The main character steps on it"), (EggCriteria.PartyFootpad, "Someone in the party steps on it"),
        (EggCriteria.SomethingOn, "Something is put down on or near it"), (EggCriteria.External, "Only when a button egg sets it off")
    };

    public SpawnPointWindow(SpawnPoint spawn, CharacterDatabase characters, PoseDatabase poses, SpriteDatabase sprites, ResourceDatabase resources)
    {
        _spawn = spawn;
        _working = System.Text.Json.JsonSerializer.Deserialize<SpawnPoint>(System.Text.Json.JsonSerializer.Serialize(spawn))!;
        _characters = characters;
        _poses = poses;
        _sprites = sprites;
        _resources = resources;
        Title = $"Spawn point / egg ({spawn.X}, {spawn.Y}, floor {spawn.Floor})";
        Width = 680; Height = 640;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var panel = new StackPanel { Margin = new Thickness(10) };
        // ---- what it is ----
        panel.Children.Add(Heading("What it does"));
        var kind = new ComboBox { Width = 380, ItemsSource = Kinds.Select(k => k.Name).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(Kinds, k => k.Kind == _working.Kind)), HorizontalAlignment = HorizontalAlignment.Left };
        kind.SelectionChanged += (_, _) => { _working.Kind = Kinds[Math.Max(0, kind.SelectedIndex)].Kind; ShowKind(); };
        panel.Children.Add(kind);
        // ---- when ----
        panel.Children.Add(Heading("When it hatches (as Exult's eggs)"));
        var when = new ComboBox { Width = 380, ItemsSource = Criterias.Select(c => c.Name).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(Criterias, c => c.Criteria == _working.Criteria)), HorizontalAlignment = HorizontalAlignment.Left };
        when.SelectionChanged += (_, _) => _working.Criteria = Criterias[Math.Max(0, when.SelectedIndex)].Criteria;
        panel.Children.Add(when);
        var numbers = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        numbers.Children.Add(Number("Chance (1-100 %)", () => _working.Probability, v => _working.Probability = Math.Clamp(v, 0, 100)));
        numbers.Children.Add(Number("Distance (map squares round it)", () => _working.Distance, v => _working.Distance = Math.Clamp(v, 0, 60)));
        panel.Children.Add(numbers);
        var flags = new WrapPanel { Margin = new Thickness(0, 2, 0, 0) };
        flags.Children.Add(Check("Nocturnal (only 21:00 - 04:59)", () => _working.Nocturnal, v => _working.Nocturnal = v));
        flags.Children.Add(Check("Once (gone after it hatches)", () => _working.Once, v => _working.Once = v));
        flags.Children.Add(Check("Auto-reset (hatches again after the party has left)", () => _working.AutoReset, v => _working.AutoReset = v));
        panel.Children.Add(flags);
        panel.Children.Add(Note("Its area is its distance round it on its floor (the footpads: its own square). It hatches once (a save keeps that), unless it resets; each time, its chance is rolled first."));
        // ---- its kind's own ----
        panel.Children.Add(_kindPanel);

        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        ok.Click += (_, _) =>
        {
            // Everything but where it is and what it has done in play.
            foreach (var property in typeof(SpawnPoint).GetProperties())
                if (property.Name is not (nameof(SpawnPoint.Id) or nameof(SpawnPoint.X) or nameof(SpawnPoint.Y) or nameof(SpawnPoint.Floor) or nameof(SpawnPoint.HatchedRuntime) or nameof(SpawnPoint.GoneRuntime)))
                    property.SetValue(_spawn, property.GetValue(_working));
            _spawn.Entries = _working.Entries.Where(e => e.CharacterId >= 0).ToList();
            DialogResult = true;
        };
        ShowKind();
    }

    // ---------- Its kind's settings ----------

    private void ShowKind()
    {
        _kindPanel.Children.Clear();
        switch (_working.Kind)
        {
            case EggKind.Monster: ShowMonster(); break;
            case EggKind.Music:
            case EggKind.Sound:
                _kindPanel.Children.Add(Heading(_working.Kind == EggKind.Music ? "The music" : "The sound"));
                _kindPanel.Children.Add(SoundPicker());
                _kindPanel.Children.Add(Check("Continuous (it loops; an auto-reset egg stops it as the party leaves)", () => _working.Continuous, v => _working.Continuous = v));
                if (_working.Kind == EggKind.Sound) _kindPanel.Children.Add(Note("Heard from the egg, softer further away (Exult's sound distance); a continuous one plays as long as it loops."));
                break;
            case EggKind.Usecode:
            {
                _kindPanel.Children.Add(Heading("The Common Event"));
                var commons = ProjectSession.SettingsProvider?.Invoke()?.CommonEvents.OrderBy(c => c.Name).ToList() ?? new List<CommonEventDefinition>();
                var which = new ComboBox { Width = 300, ItemsSource = commons, DisplayMemberPath = nameof(CommonEventDefinition.Name), SelectedItem = commons.FirstOrDefault(c => c.Id == _working.CommonEventId), HorizontalAlignment = HorizontalAlignment.Left };
                which.SelectionChanged += (_, _) => _working.CommonEventId = (which.SelectedItem as CommonEventDefinition)?.Id ?? -1;
                _kindPanel.Children.Add(which);
                if (commons.Count == 0) _kindPanel.Children.Add(Note("There are no Common Events yet - make them in Tools > Common Events."));
                break;
            }
            case EggKind.Missile:
            {
                _kindPanel.Children.Add(Heading("The missile"));
                var projectiles = (ProjectSession.ObjectsProvider?.Invoke()?.Objects ?? new List<WorldObject>()).Where(o => o.Type == ObjectType.Projectile).OrderBy(o => o.Name).ToList();
                var what = new ComboBox { Width = 300, ItemsSource = projectiles, DisplayMemberPath = nameof(WorldObject.Name), SelectedItem = projectiles.FirstOrDefault(o => o.Id == _working.ProjectileId), HorizontalAlignment = HorizontalAlignment.Left };
                what.SelectionChanged += (_, _) => _working.ProjectileId = (what.SelectedItem as WorldObject)?.Id ?? -1;
                _kindPanel.Children.Add(what);
                var directions = new List<string> { "At someone in the party (within range)" };
                directions.AddRange(Enum.GetNames<SpriteDirection>());
                var direction = new ComboBox { Width = 300, ItemsSource = directions, SelectedIndex = _working.Direction is >= 0 and < 8 ? _working.Direction + 1 : 0, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
                direction.SelectionChanged += (_, _) => _working.Direction = direction.SelectedIndex - 1;
                _kindPanel.Children.Add(direction);
                var row = new WrapPanel();
                row.Children.Add(Number("Range (map squares)", () => _working.RangeTiles, v => _working.RangeTiles = Math.Clamp(v, 1, 200)));
                row.Children.Add(Number("Seconds between shots", () => (int)Math.Round(_working.DelaySeconds), v => _working.DelaySeconds = Math.Clamp(v, 1, 600)));
                _kindPanel.Children.Add(row);
                _kindPanel.Children.Add(Note("A \"near\" egg goes on shooting while the main character stays within its range (Exult's missile launcher); otherwise it shoots once."));
                break;
            }
            case EggKind.Teleport:
            {
                _kindPanel.Children.Add(Heading("Where the party goes"));
                var worlds = new List<MapData?> { null };
                worlds.AddRange(ProjectSession.WorldsProvider?.Invoke() ?? new List<MapData>());
                var world = new ComboBox { Width = 300, ItemsSource = worlds.Select(w => w is null ? "(this map)" : w.MapName).ToList(), SelectedIndex = Math.Max(0, worlds.FindIndex(w => w?.Id == _working.TeleportWorld)), HorizontalAlignment = HorizontalAlignment.Left };
                world.SelectionChanged += (_, _) => _working.TeleportWorld = world.SelectedIndex > 0 ? worlds[world.SelectedIndex]!.Id : -1;
                _kindPanel.Children.Add(world);
                var spot = new WrapPanel();
                spot.Children.Add(Number("X", () => _working.TeleportX, v => _working.TeleportX = v));
                spot.Children.Add(Number("Y", () => _working.TeleportY, v => _working.TeleportY = v));
                spot.Children.Add(Number("Floor", () => _working.TeleportFloor, v => _working.TeleportFloor = Math.Clamp(v, 1, MapFloors.MaxFloors)));
                spot.Children.Add(Number("Or to the path egg number (-1: the spot)", () => _working.TeleportPath, v => _working.TeleportPath = v));
                _kindPanel.Children.Add(spot);
                break;
            }
            case EggKind.Weather:
            {
                _kindPanel.Children.Add(Heading("The weather"));
                var kinds = Enum.GetNames<WeatherKind>().ToList();
                var weather = new ComboBox { Width = 200, ItemsSource = kinds, SelectedItem = kinds.Contains(_working.WeatherKind) ? _working.WeatherKind : "Rain", HorizontalAlignment = HorizontalAlignment.Left };
                weather.SelectionChanged += (_, _) => _working.WeatherKind = weather.SelectedItem as string ?? "Rain";
                _kindPanel.Children.Add(weather);
                var row = new WrapPanel();
                row.Children.Add(Number("How strong (0-100)", () => _working.WeatherIntensity, v => _working.WeatherIntensity = Math.Clamp(v, 0, 100)));
                row.Children.Add(Number("For game minutes (0: it stays)", () => _working.WeatherMinutes, v => _working.WeatherMinutes = Math.Max(0, v)));
                _kindPanel.Children.Add(row);
                break;
            }
            case EggKind.Path:
                _kindPanel.Children.Add(Heading("Its number"));
                _kindPanel.Children.Add(Number("Path number (teleport eggs going to it name this)", () => _working.PathNumber, v => _working.PathNumber = v));
                break;
            case EggKind.Button:
                _kindPanel.Children.Add(Heading("What it sets off"));
                _kindPanel.Children.Add(Note("Every egg within its distance whose \"when\" is \"Only when a button egg sets it off\" hatches."));
                break;
        }
    }

    private void ShowMonster()
    {
        _kindPanel.Children.Add(Heading("Who may appear here"));
        _kindPanel.Children.Add(Note("Each one has its chance (0-100 %) for each of how many - a copy of its own appears here."));
        _kindPanel.Children.Add(_rows);
        var add = new Button { Content = "Add a character", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 6, 0, 0) };
        add.Click += (_, _) => { _working.Entries.Add(new SpawnEntry { CharacterId = Choices().FirstOrDefault()?.Id ?? -1 }); ShowRows(); };
        _kindPanel.Children.Add(add);
        _kindPanel.Children.Add(Heading("How far they roam"));
        var within = new RadioButton { Content = "Round here: within", GroupName = "roam", IsChecked = !_working.Free, VerticalAlignment = VerticalAlignment.Center };
        var tiles = new TextBox { Width = 50, Text = _working.WanderTiles.ToString(), Margin = new Thickness(6, 0, 6, 0) };
        tiles.LostFocus += (_, _) => { if (int.TryParse(tiles.Text.Trim(), out int t)) _working.WanderTiles = Math.Clamp(t, 1, 1000); tiles.Text = _working.WanderTiles.ToString(); };
        within.Checked += (_, _) => _working.Free = false;
        var withinRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        withinRow.Children.Add(within);
        withinRow.Children.Add(tiles);
        withinRow.Children.Add(new TextBlock { Text = "map squares of the spawn point (wandered farther, they come back near it)", VerticalAlignment = VerticalAlignment.Center });
        _kindPanel.Children.Add(withinRow);
        var free = new RadioButton { Content = "Free to the wild: they wander anywhere - towns, caves, the end of the earth", GroupName = "roam", IsChecked = _working.Free, Margin = new Thickness(0, 4, 0, 0) };
        free.Checked += (_, _) => _working.Free = true;
        _kindPanel.Children.Add(free);
        ShowRows();
    }

    private FrameworkElement SoundPicker()
    {
        var sounds = _resources.Entries.Where(r => r.Kind is ResourceKind.Sound or ResourceKind.Music).OrderBy(r => r.Name).ToList();
        var combo = new ComboBox { Width = 300, ItemsSource = sounds, DisplayMemberPath = nameof(ResourceEntry.Name), SelectedItem = sounds.FirstOrDefault(r => r.Id == _working.SoundId), HorizontalAlignment = HorizontalAlignment.Left };
        combo.SelectionChanged += (_, _) => _working.SoundId = (combo.SelectedItem as ResourceEntry)?.Id ?? -1;
        return combo;
    }

    // ---------- Little builders ----------

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 4) };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 2, 0, 4) };

    private static FrameworkElement Number(string label, Func<int> get, Action<int> set)
    {
        var box = new TextBox { Width = 60, Text = get().ToString() };
        box.LostFocus += (_, _) => { if (int.TryParse(box.Text.Trim(), out int v)) set(v); box.Text = get().ToString(); };
        var panel = new StackPanel { Margin = new Thickness(0, 2, 14, 2) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11 });
        panel.Children.Add(box);
        return panel;
    }

    private static CheckBox Check(string label, Func<bool> get, Action<bool> set)
    {
        var box = new CheckBox { Content = label, IsChecked = get(), Margin = new Thickness(0, 2, 14, 2) };
        box.Checked += (_, _) => set(true);
        box.Unchecked += (_, _) => set(false);
        return box;
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
