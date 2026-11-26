using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Advance IF > Event executed / Event name + page number executed: the event picked in three steps - the kind
/// (map events, characters, objects, containers, books, paperdolls), then who or what (only those that have event
/// pages), then its pages. In the end an event is chosen (its owner's name, as the game records it - and a page),
/// never the deer or the chest itself.
/// </summary>
public class EventPickerWindow : Window
{
    private sealed record Owner(string Name, string Label, List<BookEventPage> Pages)
    {
        public override string ToString() => Label;
    }

    private sealed record PageRow(int Number, string Label)
    {
        public override string ToString() => Label;
    }

    private readonly bool _wantPage;
    private readonly ListBox _kinds = new() { Margin = new Thickness(0, 4, 8, 0) };
    private readonly ListBox _owners = new() { Margin = new Thickness(0, 4, 8, 0) };
    private readonly ListBox _pages = new() { Margin = new Thickness(0, 4, 0, 0) };
    private readonly TextBox _search = new() { Margin = new Thickness(0, 4, 8, 0), ToolTip = "Type part of a name" };
    private readonly Dictionary<string, List<Owner>> _byKind = new();

    /// <summary>The event's name (its owner's name, as the game records it).</summary>
    public string EventName { get; private set; } = "";
    /// <summary>The page (1 = the first); 0 = any of its pages.</summary>
    public int PageNumber { get; private set; }

    public EventPickerWindow(string currentName, int currentPage, bool wantPage)
    {
        _wantPage = wantPage;
        Title = wantPage ? "Pick an event's page" : "Pick an event";
        Width = 900;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Collect();

        var root = new Grid { Margin = new Thickness(8) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        AddAt(root, Header("1. Kind"), 0, 0);
        AddAt(root, Header("2. Who / what (only those with event pages)"), 0, 1);
        AddAt(root, Header(wantPage ? "3. Its page" : "3. Its event pages"), 0, 2);
        AddAt(root, _kinds, 1, 0);
        var middle = new DockPanel();
        DockPanel.SetDock(_search, Dock.Top);
        middle.Children.Add(_search);
        middle.Children.Add(_owners);
        AddAt(root, middle, 1, 1);
        AddAt(root, _pages, 1, 2);

        var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) => Accept();
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        DockPanel.SetDock(buttons, Dock.Right);
        bottom.Children.Add(buttons);
        bottom.Children.Add(new TextBlock
        {
            Text = wantPage ? "Click the page that must have run, then OK (or double-click it)."
                : "The event ran: any of its pages. To test one page, use \"Event name + page number executed\".",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, VerticalAlignment = VerticalAlignment.Center
        });
        Grid.SetColumnSpan(bottom, 3);
        AddAt(root, bottom, 2, 0);
        Content = root;

        _kinds.ItemsSource = _byKind.Keys.ToList();
        _kinds.SelectionChanged += (_, _) => ShowOwners();
        _search.TextChanged += (_, _) => ShowOwners();
        _owners.SelectionChanged += (_, _) => ShowPages();
        _owners.MouseDoubleClick += (_, _) => { if (!_wantPage) Accept(); };
        _pages.MouseDoubleClick += (_, _) => Accept();

        // Open on the current choice.
        var current = _byKind.SelectMany(k => k.Value.Select(o => (Kind: k.Key, Owner: o))).FirstOrDefault(t => t.Owner.Name == currentName);
        _kinds.SelectedItem = current.Owner is not null ? current.Kind : _byKind.FirstOrDefault(k => k.Value.Count > 0).Key ?? _byKind.Keys.First();
        if (current.Owner is not null)
        {
            _owners.SelectedItem = current.Owner;
            _owners.ScrollIntoView(current.Owner);
            _pages.SelectedItem = (_pages.ItemsSource as List<PageRow>)?.FirstOrDefault(p => p.Number == currentPage);
        }
    }

    private static TextBlock Header(string text) => new() { Text = text, FontWeight = FontWeights.Bold };

    private static void AddAt(Grid grid, UIElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    private static bool HasPages(List<BookEventPage>? pages) => pages is { Count: > 0 } && pages.Any(p => p.Commands.Count > 0);

    private void Collect()
    {
        var mapEvents = new List<Owner>();
        foreach (var map in ProjectSession.WorldsProvider?.Invoke() ?? new List<MapData>())
            foreach (var e in map.Events.Where(e => HasPages(e.EventPages) && !string.IsNullOrWhiteSpace(e.Name)))
                mapEvents.Add(new Owner(e.Name, $"{e.Name}   ({map.MapName}, {e.X}, {e.Y}, floor {e.Floor})", e.EventPages));
        _byKind["Map events"] = mapEvents.OrderBy(o => o.Label).ToList();
        _byKind["Characters"] = ProjectSession.Characters.Characters.Where(c => HasPages(c.EventPages)).Select(c => new Owner(c.Name, c.Name, c.EventPages)).OrderBy(o => o.Label).ToList();
        var objects = ProjectSession.ObjectsProvider?.Invoke() ?? new ObjectDatabase();
        _byKind["Objects"] = objects.Objects.Where(o => HasPages(o.EventPages)).Select(o => new Owner(o.Name, $"{o.Name}   ({ObjectTypes.Name(o.Type)})", o.EventPages)).OrderBy(o => o.Label).ToList();
        _byKind["Containers"] = (ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase()).Containers.Where(c => HasPages(c.EventPages)).Select(c => new Owner(c.Name, c.Name, c.EventPages)).OrderBy(o => o.Label).ToList();
        _byKind["Books"] = (ProjectSession.BooksProvider?.Invoke() ?? new BookDatabase()).Books.Where(b => HasPages(b.EventPages)).Select(b => new Owner(b.Name, b.Name, b.EventPages)).OrderBy(o => o.Label).ToList();
        if (!ProjectSession.IsUltima6) _byKind["Paperdolls"] = objects.DollDesigns.SelectMany(d => d.Parts.Where(p => HasPages(p.EventPages) && !string.IsNullOrWhiteSpace(p.Name))
            .Select(p => new Owner(p.Name, $"{p.Name}   ({d.Name})", p.EventPages))).OrderBy(o => o.Label).ToList();
    }

    private void ShowOwners()
    {
        if (_kinds.SelectedItem is not string kind) return;
        string find = _search.Text.Trim();
        var list = _byKind[kind].Where(o => find.Length == 0 || o.Label.Contains(find, StringComparison.OrdinalIgnoreCase)).ToList();
        _owners.ItemsSource = list;
        if (list.Count == 1) _owners.SelectedIndex = 0;
        ShowPages();
    }

    private void ShowPages()
    {
        if (_owners.SelectedItem is not Owner owner) { _pages.ItemsSource = null; return; }
        var rows = new List<PageRow>();
        if (!_wantPage) rows.Add(new PageRow(0, "(any of its pages)"));
        for (int i = 0; i < owner.Pages.Count; i++)
        {
            var page = owner.Pages[i];
            string first = page.Commands.Count > 0 ? page.Commands[0].Type.ToString() : "(empty)";
            rows.Add(new PageRow(i + 1, $"Page {i + 1}: {page.Conditions.TriggerConditionBased} - {page.Commands.Count} command(s), first: {first}"));
        }
        _pages.ItemsSource = rows;
        _pages.SelectedIndex = 0;
        _pages.IsEnabled = _wantPage;
    }

    private void Accept()
    {
        if (_owners.SelectedItem is not Owner owner) { MessageBox.Show(this, "Pick who or what (the middle list).", Title); return; }
        int page = (_pages.SelectedItem as PageRow)?.Number ?? 0;
        if (_wantPage && page < 1) { MessageBox.Show(this, "Pick one of its pages (the right list).", Title); return; }
        EventName = owner.Name;
        PageNumber = _wantPage ? page : 0;
        DialogResult = true;
    }
}
