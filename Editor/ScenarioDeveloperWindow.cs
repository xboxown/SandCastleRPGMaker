using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Game > Scenario Developer: places and states to jump into while testing, so a late part of a game can be tested without playing
/// up to it. Each scenario says where the game starts, its variables, and for each character whether it joined or died, what it
/// carries and its stats. F5 starts at the one chosen here; in Game Test each scenario's own key, or the Next Scenario key, jumps to
/// another. Game Test only: a built game never looks at any of this. Delete them all, or choose none, and F5 starts as usual.
/// </summary>
public sealed class ScenarioDeveloperWindow : Window
{
    private readonly ScenarioSettings _settings;
    private readonly List<MapData> _worlds;
    private readonly CharacterDatabase _characters;
    private readonly VariableDatabase _variables;
    private readonly StatDatabase _stats;
    private readonly BookDatabase _books;
    private readonly ObjectDatabase _objects;
    private readonly ContainerDatabase _containers;
    private readonly SpriteDatabase _sprites;

    private readonly ListBox _list = new() { MinHeight = 200 };
    private readonly ComboBox _startBox = new() { Width = 210 };
    private readonly StackPanel _details = new() { Margin = new Thickness(12, 0, 0, 0) };
    private readonly StackPanel _characterDetails = new() { Margin = new Thickness(0, 8, 0, 0) };
    private bool _showing;

    private Scenario? Picked => _list.SelectedItem is ListBoxItem { Tag: Scenario s } ? s : null;

    public ScenarioDeveloperWindow(ScenarioSettings settings, List<MapData> worlds, CharacterDatabase characters, VariableDatabase variables, StatDatabase stats,
        BookDatabase books, ObjectDatabase objects, ContainerDatabase containers, SpriteDatabase sprites)
    {
        _settings = settings;
        _worlds = worlds;
        _characters = characters;
        _variables = variables;
        _stats = stats;
        _books = books;
        _objects = objects;
        _containers = containers;
        _sprites = sprites;
        Title = $"Scenario Developer [{BuildInfo.CurrentBuild}]";
        Width = 980;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var left = new DockPanel { Width = 230 };
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "F5 starts at:", FontWeight = FontWeights.Bold });
        _startBox.SelectionChanged += (_, _) =>
        {
            if (!_showing) _settings.StartScenarioId = (_startBox.SelectedItem as ComboBoxItem)?.Tag is int id ? id : -1;
        };
        top.Children.Add(_startBox);
        top.Children.Add(new TextBlock { Text = "Next Scenario key:", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 0) });
        top.Children.Add(KeyButton(() => _settings.NextKey, v => _settings.NextKey = v));
        top.Children.Add(new TextBlock { Text = "Scenarios", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 2) });
        var buttons = new WrapPanel();
        buttons.Children.Add(Button("New", () =>
        {
            var scenario = new Scenario { Id = _settings.NextId(), Name = $"Scenario {_settings.Scenarios.Count + 1}", WorldId = _worlds.FirstOrDefault()?.Id ?? 0 };
            _settings.Scenarios.Add(scenario);
            RefreshList(scenario);
        }));
        buttons.Children.Add(Button("Duplicate", () =>
        {
            if (Picked is not { } from) return;
            var copy = System.Text.Json.JsonSerializer.Deserialize<Scenario>(System.Text.Json.JsonSerializer.Serialize(from))!;
            copy.Id = _settings.NextId();
            copy.Name = from.Name + " copy";
            copy.Key = string.Empty;
            _settings.Scenarios.Add(copy);
            RefreshList(copy);
        }));
        buttons.Children.Add(Button("Delete", () =>
        {
            if (Picked is not { } scenario) return;
            _settings.Scenarios.Remove(scenario);
            if (_settings.StartScenarioId == scenario.Id) _settings.StartScenarioId = -1;
            RefreshList(_settings.Scenarios.FirstOrDefault());
        }));
        buttons.Children.Add(Button("Up", () => Move(-1)));
        buttons.Children.Add(Button("Down", () => Move(1)));
        top.Children.Add(buttons);
        DockPanel.SetDock(top, Dock.Top);
        left.Children.Add(top);
        var note = new TextBlock
        {
            Text = "For testing only: a built game never looks at this. In Game Test, a scenario's key (or the Next Scenario key) ends the game you're in and starts that scenario fresh. Choose a key nothing else uses, like F2, F3 or Ctrl+1.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 8, 0, 0)
        };
        DockPanel.SetDock(note, Dock.Bottom);
        left.Children.Add(note);
        left.Children.Add(_list);
        _list.SelectionChanged += (_, _) => { if (!_showing) ShowDetails(); };

        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = _details });
        Content = root;
        RefreshList(_settings.Scenarios.FirstOrDefault());
    }

    private void Move(int by)
    {
        if (Picked is not { } scenario) return;
        int at = _settings.Scenarios.IndexOf(scenario), to = at + by;
        if (to < 0 || to >= _settings.Scenarios.Count) return;
        _settings.Scenarios.RemoveAt(at);
        _settings.Scenarios.Insert(to, scenario);
        RefreshList(scenario);
    }

    private void RefreshList(Scenario? pick)
    {
        _showing = true;
        _list.Items.Clear();
        foreach (var scenario in _settings.Scenarios)
        {
            string key = scenario.Key.Length > 0 ? $"  [{U8Keys.Display(scenario.Key)}]" : "";
            _list.Items.Add(new ListBoxItem { Content = (scenario.On ? "" : "(off) ") + scenario.Name + key, Tag = scenario, IsSelected = scenario == pick });
        }
        _startBox.Items.Clear();
        _startBox.Items.Add(new ComboBoxItem { Content = "(as usual: no scenario)", Tag = -1 });
        foreach (var scenario in _settings.Scenarios) _startBox.Items.Add(new ComboBoxItem { Content = scenario.Name, Tag = scenario.Id });
        _startBox.SelectedItem = _startBox.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == _settings.StartScenarioId) ?? _startBox.Items[0];
        _showing = false;
        ShowDetails();
    }

    private void ShowDetails()
    {
        _details.Children.Clear();
        _characterDetails.Children.Clear();
        if (Picked is not { } scenario)
        {
            _details.Children.Add(new TextBlock { Text = "New makes a scenario.", Opacity = 0.7 });
            return;
        }

        // ---------- Where ----------
        _details.Children.Add(Heading("The scenario"));
        var name = new TextBox { Text = scenario.Name, Width = 300 };
        name.LostFocus += (_, _) => { if (scenario.Name != name.Text) { scenario.Name = name.Text; RefreshList(scenario); } };
        _details.Children.Add(Row("Name:", name));
        var on = new CheckBox { Content = "On (unticked: F5 and the keys skip it)", IsChecked = scenario.On };
        on.Checked += (_, _) => { scenario.On = true; RefreshList(scenario); };
        on.Unchecked += (_, _) => { scenario.On = false; RefreshList(scenario); };
        _details.Children.Add(on);
        _details.Children.Add(Row("Its key:", KeyButton(() => scenario.Key, v => { scenario.Key = v; RefreshList(scenario); })));

        _details.Children.Add(Heading("Where the game starts"));
        var world = new ComboBox { Width = 300, ItemsSource = _worlds, DisplayMemberPath = nameof(MapData.MapName), SelectedItem = _worlds.FirstOrDefault(w => w.Id == scenario.WorldId) ?? _worlds.FirstOrDefault() };
        world.SelectionChanged += (_, _) => { if (world.SelectedItem is MapData m) scenario.WorldId = m.Id; };
        _details.Children.Add(Row("Map:", world));
        var spot = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        void ShowSpot() => spot.Text = scenario.X >= 0 ? $"floor {scenario.Floor}, cell ({scenario.X}, {scenario.Y})" : "(the map's Start Position)";
        ShowSpot();
        var spotRow = new StackPanel { Orientation = Orientation.Horizontal };
        spotRow.Children.Add(spot);
        spotRow.Children.Add(Button("Pick on the map...", () =>
        {
            int worldId = (world.SelectedItem as MapData)?.Id ?? 0;
            if (ProjectSession.WorldFloorPictureProvider is not { } provider) return;
            Func<int, bool, MapPickerPicture?> floors = (f, inside) => provider(worldId, f, inside);
            if (floors(scenario.X >= 0 ? scenario.Floor : 1, true) is not { } first) return;
            var picker = new MapCellPickerWindow(first.Picture, first.TileWidth, first.TileHeight, first.Columns, first.Rows, scenario.X >= 0 ? (scenario.X, scenario.Y) : null, null, startFloor: scenario.X >= 0 ? scenario.Floor : null, floorPictures: floors) { Owner = this };
            if (picker.ShowDialog() == true && picker.Picked is { } cell) { (scenario.X, scenario.Y, scenario.Floor) = (cell.X, cell.Y, picker.PickedFloor); ShowSpot(); }
        }));
        spotRow.Children.Add(Button("Start Position", () => { (scenario.X, scenario.Y, scenario.Floor) = (-1, -1, 1); ShowSpot(); }));
        _details.Children.Add(Row("Spot:", spotRow));

        // ---------- Variables ----------
        _details.Children.Add(Heading("Variables and flags"));
        var variables = new StackPanel();
        void ShowVariables()
        {
            variables.Children.Clear();
            var names = _variables.Variables.Select(v => v.Name).OrderBy(n => n).ToList();
            foreach (var variable in scenario.Variables.ToList())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                var which = new ComboBox { Width = 240, IsEditable = true, ItemsSource = names, Text = variable.Name };
                which.LostFocus += (_, _) => variable.Name = which.Text.Trim();
                which.SelectionChanged += (_, _) => { if (which.SelectedItem is string n) variable.Name = n; };
                var value = new TextBox { Width = 140, Text = variable.Value, Margin = new Thickness(6, 0, 6, 0), ToolTip = "A number, true, false, or words." };
                value.TextChanged += (_, _) => variable.Value = value.Text;
                row.Children.Add(which);
                row.Children.Add(value);
                row.Children.Add(Button("Remove", () => { scenario.Variables.Remove(variable); ShowVariables(); }));
                variables.Children.Add(row);
            }
        }
        ShowVariables();
        _details.Children.Add(variables);
        _details.Children.Add(Button("Add a variable", () => { scenario.Variables.Add(new ScenarioVariable()); ShowVariables(); }));

        // ---------- Characters ----------
        _details.Children.Add(Heading("Characters"));
        _details.Children.Add(new TextBlock { Text = "Who joined, who died, what they carry, their stats. Pick a character to change it; those left alone start as in the game.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7 });
        var who = new ComboBox { Width = 300, ItemsSource = _characters.Characters, DisplayMemberPath = nameof(CharacterDefinition.Name) };
        who.SelectionChanged += (_, _) => { if (who.SelectedItem is CharacterDefinition c) ShowCharacter(scenario, c); };
        _details.Children.Add(Row("Character:", who));
        _details.Children.Add(_characterDetails);
        who.SelectedItem = _characters.Characters.FirstOrDefault(c => c.IsTestPlayer) ?? _characters.Characters.FirstOrDefault();
    }

    private void ShowCharacter(Scenario scenario, CharacterDefinition character)
    {
        _characterDetails.Children.Clear();
        var rule = scenario.For(character.Id);
        if (character.IsTestPlayer)
            _characterDetails.Children.Add(new TextBlock { Text = "The main character: always there, and can't start dead.", FontSize = 11, Opacity = 0.7 });
        else
        {
            var party = new ComboBox { Width = 220, ItemsSource = new[] { "As in the game", "Has joined the party", "Hasn't joined the party" }, SelectedIndex = (int)rule.Party };
            party.SelectionChanged += (_, _) => rule.Party = (ScenarioPartyRule)party.SelectedIndex;
            _characterDetails.Children.Add(Row("Party:", party));
            var dead = new CheckBox { Content = "Has died (off the map, on every map, with no body)", IsChecked = rule.Dead };
            dead.Checked += (_, _) => rule.Dead = true;
            dead.Unchecked += (_, _) => rule.Dead = false;
            _characterDetails.Children.Add(dead);
        }

        _characterDetails.Children.Add(Heading("What it carries"));
        var only = new CheckBox { Content = "Only these (unticked: these on top of what it has in the game)", IsChecked = rule.OnlyTheseItems };
        only.Checked += (_, _) => rule.OnlyTheseItems = true;
        only.Unchecked += (_, _) => rule.OnlyTheseItems = false;
        _characterDetails.Children.Add(only);
        var items = new StackPanel();
        void ShowItems()
        {
            items.Children.Clear();
            foreach (var item in rule.Items.ToList())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(new TextBlock { Text = item.DisplayName, Width = 240, VerticalAlignment = VerticalAlignment.Center });
                var quantity = new TextBox { Width = 50, Text = item.Quantity.ToString(), Margin = new Thickness(0, 0, 6, 0), ToolTip = "How many" };
                quantity.LostFocus += (_, _) => { if (int.TryParse(quantity.Text, out int q)) item.Quantity = Math.Max(1, q); quantity.Text = item.Quantity.ToString(); };
                row.Children.Add(quantity);
                row.Children.Add(Button("Remove", () => { rule.Items.Remove(item); ShowItems(); }));
                items.Children.Add(row);
            }
        }
        ShowItems();
        _characterDetails.Children.Add(items);
        _characterDetails.Children.Add(Button("Add an item...", () =>
        {
            var picker = new SelectItemPickerWindow(_books, _objects, _containers, _sprites, null) { Owner = this };
            if (picker.ShowDialog() != true || picker.Result is not { ItemId: >= 0 } picked) return;
            rule.Items.Add(new ScenarioItem { Category = picked.Category, ItemId = picked.ItemId, DisplayName = picked.DisplayName, Quantity = 1 });
            ShowItems();
        }));

        _characterDetails.Children.Add(Heading("Its stats (empty: as in the game)"));
        var grid = new WrapPanel();
        foreach (var stat in _stats.Stats)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 14, 1) };
            row.Children.Add(new TextBlock { Text = stat.Name + ":", Width = 110, VerticalAlignment = VerticalAlignment.Center });
            var box = new TextBox { Width = 60, Text = rule.Stats.TryGetValue(stat.Id, out var value) ? value : "" };
            box.LostFocus += (_, _) => { if (box.Text.Trim().Length == 0) rule.Stats.Remove(stat.Id); else rule.Stats[stat.Id] = box.Text.Trim(); };
            row.Children.Add(box);
            grid.Children.Add(row);
        }
        _characterDetails.Children.Add(grid);
    }

    /// <summary>A button showing a key: click it, then press the key (alone or with Ctrl, Shift or Alt). Backspace: no key. Click elsewhere: no change.</summary>
    private static FrameworkElement KeyButton(Func<string> get, Action<string> set)
    {
        var button = new Button { Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
        void Show() => button.Content = get().Length > 0 ? U8Keys.Display(get()) : "(no key)";
        Show();
        var capture = new U8KeyCapture();
        bool waiting = false;
        button.ToolTip = "Click, then press the key. Backspace: no key.";
        button.Click += (_, _) => { waiting = true; capture.Reset(); button.Content = "Press a key..."; };
        button.LostFocus += (_, _) => { waiting = false; Show(); };
        void Take(string key)
        {
            waiting = false;
            set(key);
            Show();
        }
        button.PreviewKeyDown += (_, e) =>
        {
            if (!waiting) return;
            var pressed = U8KeyCapture.RealKey(e);
            if (pressed == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return;
            e.Handled = true;
            if (e.IsRepeat) return;
            if (pressed == Key.Back) { Take(string.Empty); return; }
            if (capture.Down(pressed, Keyboard.Modifiers) is { } key) Take(key);
        };
        button.PreviewKeyUp += (_, e) =>
        {
            if (!waiting) return;
            e.Handled = true;
            if (capture.Up(U8KeyCapture.RealKey(e)) is { } key) Take(key);
        };
        return button;
    }

    private static Button Button(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 2, 4, 2), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => click();
        return button;
    }

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 10, 0, 3) };

    private static FrameworkElement Row(string label, FrameworkElement control)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = label, Width = 80, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(control);
        return row;
    }
}
