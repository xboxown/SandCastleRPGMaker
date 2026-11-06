using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Tools > Leveling Editor (Ultima 6 projects): leveling is the maker's own, as the PaperDoll Editor's paperdolls are - nothing
/// wired. Leveling systems (many kept, one used), each its level table and its events made of event commands: When someone
/// earns a level, When the party wakes from Rest (once, or once for each member who earned a level), When Run Leveling Event
/// calls it (a shrine's, a crystal's). Templates show Ultima 6's shrines and Savage Empire's / Martian Dreams' dreams - delete
/// them and make your own.
/// </summary>
public sealed class LevelingEditorWindow : Window
{
    private readonly Ultima6Settings _u6;
    private readonly SpriteDatabase _sprites;
    private readonly ListBox _systems = new() { Height = 170 };
    private readonly StackPanel _systemProps = new();
    private readonly ListBox _events = new() { Height = 150 };
    private readonly StackPanel _eventProps = new();
    private readonly ContentControl _commands = new();
    private U6LevelSystem? Picked => _systems.SelectedItem is ListBoxItem { Tag: U6LevelSystem s } ? s : null;
    private U6LevelEvent? Event => _events.SelectedItem is ListBoxItem { Tag: U6LevelEvent e } ? e : null;

    public LevelingEditorWindow(Ultima6Settings u6, SpriteDatabase sprites)
    {
        _u6 = u6;
        _sprites = sprites;
        Title = $"Leveling Editor [{BuildInfo.CurrentBuild}]";
        Width = 1250;
        Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new Grid { Margin = new Thickness(8) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        left.Children.Add(new TextBlock { Text = "Leveling systems", FontWeight = FontWeights.Bold });
        left.Children.Add(Note("Your game's leveling, made of event commands - nothing is wired. Keep as many as you like; the one In use is the game's. The templates show Ultima 6's shrines and Savage Empire's / Martian Dreams' dreams: learn from them, change them, delete them."));
        left.Children.Add(_systems);
        var row = new WrapPanel();
        row.Children.Add(Button("New", () => AddSystem(new U6LevelSystem { Name = UniqueName("New leveling system") })));
        row.Children.Add(Button("Duplicate", () => { if (Picked is { } s) AddSystem(Copy(s)); }));
        row.Children.Add(Button("Delete", DeleteSystem));
        row.Children.Add(Button("Use this one", () => { if (Picked is { } s) { _u6.ActiveLevelSystem = s.Name; ShowSystems(s); } }));
        row.Children.Add(Button("Add the templates", () => { foreach (var t in U6LevelSystem.Templates()) { t.Name = UniqueName(t.Name); _u6.LevelSystems.Add(t); } ShowSystems(Picked); }));
        left.Children.Add(row);
        left.Children.Add(_systemProps);
        Grid.SetColumn(left, 0);
        root.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var right = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "Its events", FontWeight = FontWeights.Bold });
        top.Children.Add(Note("Each runs at its moment, with its character as Self (Activator), these variables set first: LevelWho (the name), LevelWhoId, LevelNow (the level now), LevelEarned (the level earned), LevelRose (1: it already rose by itself); after Rest also RestHours, RestGuard. "
            + "Rise A Level (Ultima 6 tab > Levels) raises someone - Only if earned: nothing happens if they aren't ready. Run Leveling Event (same tab) calls one of these by name from a shrine's, a crystal's or any object's event. Scene Choice asks in the text area when no scene is up. {LevelWho} in words puts the name in."));
        top.Children.Add(_events);
        var eventButtons = new WrapPanel();
        eventButtons.Children.Add(Button("New event", () => { if (Picked is { } s) { var e = new U6LevelEvent { Name = UniqueEventName(s, "New event") }; s.Events.Add(e); ShowEvents(e); } }));
        eventButtons.Children.Add(Button("Delete event", () => { if (Picked is { } s && Event is { } e) { s.Events.Remove(e); ShowEvents(null); } }));
        top.Children.Add(eventButtons);
        top.Children.Add(_eventProps);
        DockPanel.SetDock(top, Dock.Top);
        right.Children.Add(top);
        right.Children.Add(_commands);
        Grid.SetColumn(right, 1);
        root.Children.Add(right);
        Content = root;

        _systems.SelectionChanged += (_, _) => { ShowSystemProps(); ShowEvents(Picked?.Events.FirstOrDefault()); };
        _events.SelectionChanged += (_, _) => ShowEventProps();
        ShowSystems(_u6.Active ?? _u6.LevelSystems.FirstOrDefault());
    }

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.75, Margin = new Thickness(0, 2, 0, 6) };

    private static Button Button(string text, Action click)
    {
        var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 2, 4, 2) };
        b.Click += (_, _) => click();
        return b;
    }

    private string UniqueName(string name)
    {
        string n = name;
        for (int i = 2; _u6.LevelSystems.Any(s => s.Name == n); i++) n = $"{name} {i}";
        return n;
    }

    private static string UniqueEventName(U6LevelSystem s, string name)
    {
        string n = name;
        for (int i = 2; s.Events.Any(e => e.Name == n); i++) n = $"{name} {i}";
        return n;
    }

    private U6LevelSystem Copy(U6LevelSystem s)
    {
        var copy = System.Text.Json.JsonSerializer.Deserialize<U6LevelSystem>(System.Text.Json.JsonSerializer.Serialize(s))!;
        copy.Name = UniqueName(s.Name + " (copy)");
        return copy;
    }

    private void AddSystem(U6LevelSystem s)
    {
        _u6.LevelSystems.Add(s);
        ShowSystems(s);
    }

    private void DeleteSystem()
    {
        if (Picked is not { } s) return;
        if (MessageBox.Show(this, $"Delete \"{s.Name}\" and its events?", "Leveling Editor", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        _u6.LevelSystems.Remove(s);
        if (_u6.ActiveLevelSystem == s.Name) _u6.ActiveLevelSystem = string.Empty;
        ShowSystems(_u6.LevelSystems.FirstOrDefault());
    }

    private void ShowSystems(U6LevelSystem? select)
    {
        _systems.Items.Clear();
        foreach (var s in _u6.LevelSystems)
        {
            bool used = s.Name == _u6.ActiveLevelSystem;
            var item = new ListBoxItem { Tag = s, Content = used ? $"{s.Name}   (in use)" : s.Name, FontWeight = used ? FontWeights.Bold : FontWeights.Normal };
            _systems.Items.Add(item);
            if (ReferenceEquals(s, select)) _systems.SelectedItem = item;
        }
        if (_systems.SelectedItem is null) { ShowSystemProps(); ShowEvents(null); }
    }

    private void ShowSystemProps()
    {
        _systemProps.Children.Clear();
        if (Picked is not { } s) return;
        _systemProps.Children.Add(new TextBlock { Text = "Its name and level table", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) });
        var name = new TextBox { Text = s.Name };
        name.LostFocus += (_, _) =>
        {
            string n = name.Text.Trim();
            if (n.Length == 0 || n == s.Name || _u6.LevelSystems.Any(o => o != s && o.Name == n)) { name.Text = s.Name; return; }
            if (_u6.ActiveLevelSystem == s.Name) _u6.ActiveLevelSystem = n;
            s.Name = n;
            ShowSystems(s);
        };
        _systemProps.Children.Add(name);
        _systemProps.Children.Add(Note("Experience each level needs - more than the first for level 2, the second for level 3... (as many levels as you list, plus level 1):"));
        var steps = new TextBox { Text = string.Join(", ", s.LevelExperience) };
        steps.LostFocus += (_, _) =>
        {
            var numbers = steps.Text.Split(',', ';', ' ').Select(x => int.TryParse(x.Trim(), out int v) ? v : -1).Where(v => v >= 0).ToList();
            if (numbers.Count > 0) s.LevelExperience = numbers;
            steps.Text = string.Join(", ", s.LevelExperience);
        };
        _systemProps.Children.Add(steps);
        _systemProps.Children.Add(Number("Most hit points a level:", () => s.HitsPerLevel, v => s.HitsPerLevel = Math.Max(1, v)));
        _systemProps.Children.Add(Number("Never more hit points than:", () => s.MostHits, v => s.MostHits = Math.Max(1, v)));
        _systemProps.Children.Add(Number("No stat raised past:", () => s.StatCap, v => s.StatCap = Math.Max(1, v)));
        var rise = new CheckBox { Content = "A level earned rises at once (its hit points; the events still run)", IsChecked = s.RiseByThemselves, Margin = new Thickness(0, 4, 0, 0) };
        rise.Checked += (_, _) => s.RiseByThemselves = true;
        rise.Unchecked += (_, _) => s.RiseByThemselves = false;
        _systemProps.Children.Add(rise);
        _systemProps.Children.Add(Note("Unticked (as Ultima 6): a level is only taken when an event does Rise A Level - a shrine, a dream, a crystal."));
    }

    private static UIElement Number(string label, Func<int> get, Action<int> set)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = label, Width = 190, VerticalAlignment = VerticalAlignment.Center });
        var box = new TextBox { Width = 60, Text = get().ToString() };
        box.LostFocus += (_, _) => { if (int.TryParse(box.Text, out int v)) set(v); box.Text = get().ToString(); };
        row.Children.Add(box);
        return row;
    }

    private void ShowEvents(U6LevelEvent? select)
    {
        _events.Items.Clear();
        if (Picked is { } s)
            foreach (var e in s.Events)
            {
                var item = new ListBoxItem { Tag = e, Content = $"{e.Name}   - {e.Trigger}" };
                _events.Items.Add(item);
                if (ReferenceEquals(e, select)) _events.SelectedItem = item;
            }
        if (_events.SelectedItem is null) ShowEventProps();
    }

    private void ShowEventProps()
    {
        _eventProps.Children.Clear();
        _commands.Content = null;
        if (Event is not { } e) return;
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        row.Children.Add(new TextBlock { Text = "Name:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var name = new TextBox { Width = 240, Text = e.Name };
        name.LostFocus += (_, _) => { if (name.Text.Trim().Length > 0) { e.Name = name.Text.Trim(); ShowEvents(e); } };
        row.Children.Add(name);
        row.Children.Add(new TextBlock { Text = "   Runs:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        var trigger = new ComboBox { Width = 420, ItemsSource = U6LevelEvent.Triggers, SelectedItem = e.Trigger };
        trigger.SelectionChanged += (_, _) => { if (trigger.SelectedItem is string t) { e.Trigger = t; ShowEvents(e); } };
        row.Children.Add(trigger);
        _eventProps.Children.Add(row);
        var pages = new ObservableCollection<EventCommandPage> { new() { Label = "1", Commands = e.Commands } };
        var editor = new EventEditorControl
        {
            ResourceDatabase = ProjectSession.ResourcesProvider?.Invoke() ?? new ResourceDatabase(),
            Variables = ProjectSession.VariablesProvider?.Invoke() ?? new VariableDatabase(),
            CurrentBookLocalVariables = new List<VariableDefinition>(),
            Books = ProjectSession.BooksProvider?.Invoke() ?? new BookDatabase(),
            Objects = ProjectSession.ObjectsProvider?.Invoke() ?? new ObjectDatabase(),
            Containers = ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase(),
            Sprites = _sprites,
            CurrentBook = null
        };
        editor.ContentChanged += () => { if (editor.Pages.Count > 0) e.Commands = editor.Pages[0].Commands; };
        editor.Pages = pages;
        _commands.Content = editor;
    }
}
