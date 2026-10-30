using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Map Editor's "On This Map" tab: everything placed on the map open now, to find it again on a big map - Objects (in the
/// Object Editor's folders, then Books and Containers, as the Objects tab), Characters, Events and Houses (vehicles too), each
/// with its look (animated) and where it is. What's inside a plotted house is listed too, marked with its house, at its spot on
/// the map. A search box narrows them by name. Click one: the map goes to it (its floor too) and it's outlined, pulsing, for a
/// few seconds - a black thing on a black map stands out. A folder fills in when it's opened, so a map with thousands of wall
/// pieces stays quick.
/// </summary>
public partial class MainWindow
{
    private TabItem? _onMapTab;
    private TextBox? _onMapSearch;
    private TreeView? _onMapObjects, _onMapCharacters, _onMapEvents, _onMapHouses;
    private TabItem? _onMapObjectsTab, _onMapCharactersTab, _onMapEventsTab, _onMapHousesTab;
    private readonly List<SpriteAnimator> _onMapAnimators = new();
    private readonly HashSet<string> _onMapExpanded = new();
    private readonly List<UIElement> _onMapHighlight = new();
    private DispatcherTimer? _onMapHighlightTimer;

    /// <summary>
    /// One thing placed on the map, as the list shows it: X, Y are its map cell (a house's contents: the house's spot plus its
    /// own), Width/Height in cells for a house, InHouse the house it's inside of (null: placed on the map itself).
    /// </summary>
    private sealed record OnMapEntry(object Thing, string Name, int X, int Y, int Floor, SpriteEntry? Sprite, BitmapSource? Icon,
        string? InHouse = null, int Width = 1, int Height = 1, string? Note = null);

    /// <summary>The map's own layout, then each plotted house's layout at its spot.</summary>
    private IEnumerable<(MapData Layout, int DX, int DY, string? HouseName)> MapAndPlottedHouses()
    {
        if (_map is null) yield break;
        yield return (_map, 0, 0, null);
        foreach (var plot in _map.Houses)
            if (_houses.FirstOrDefault(h => h.Id == plot.HouseId) is { } house)
                yield return (house.Layout, plot.X, plot.Y, house.Name);
    }

    private void BuildOnThisMapTab()
    {
        _onMapSearch = new TextBox { Margin = new Thickness(4, 4, 4, 2), ToolTip = "Search by name (what's inside a house counts for it too)" };
        _onMapObjects = OnMapTree();
        _onMapCharacters = OnMapTree();
        _onMapEvents = OnMapTree();
        _onMapHouses = OnMapTree();
        var sub = new TabControl { Margin = new Thickness(2) };
        sub.Items.Add(_onMapObjectsTab = new TabItem { Header = "Objects", Content = _onMapObjects });
        sub.Items.Add(_onMapCharactersTab = new TabItem { Header = "Characters", Content = _onMapCharacters });
        sub.Items.Add(_onMapEventsTab = new TabItem { Header = "Events", Content = _onMapEvents });
        sub.Items.Add(_onMapHousesTab = new TabItem { Header = "Houses", Content = _onMapHouses });

        var refresh = new Button { Content = "Refresh", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 4, 0), ToolTip = "The list again, as the map is now" };
        refresh.Click += (_, _) => RefreshOnThisMap();
        var note = new TextBlock { Text = "Everything placed on this map. Click one to go to it.", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, Margin = new Thickness(6, 6, 6, 2) };
        var searchRow = new DockPanel { Margin = new Thickness(0, 0, 0, 2) };
        DockPanel.SetDock(refresh, Dock.Right);
        searchRow.Children.Add(refresh);
        searchRow.Children.Add(_onMapSearch);

        var panel = new DockPanel();
        DockPanel.SetDock(note, Dock.Top);
        DockPanel.SetDock(searchRow, Dock.Top);
        panel.Children.Add(note);
        panel.Children.Add(searchRow);
        panel.Children.Add(sub);
        _onMapTab = new TabItem { Header = "On This Map", Content = panel, ToolTip = "Everything placed on the map open now - click one to go to it" };
        PaletteTabs.Items.Add(_onMapTab);

        _onMapSearch.TextChanged += (_, _) => RefreshOnThisMap();
        PaletteTabs.SelectionChanged += (_, e) =>
        {
            if (!ReferenceEquals(e.OriginalSource, PaletteTabs)) return; // a list or tab inside it
            if (ReferenceEquals(PaletteTabs.SelectedItem, _onMapTab)) RefreshOnThisMap();
            else StopOnMapAnimators(); // out of sight: nothing animates for nobody
        };
    }

    private TreeView OnMapTree()
    {
        var tree = new TreeView { Margin = new Thickness(2) };
        tree.SelectedItemChanged += (_, _) => { if ((tree.SelectedItem as TreeViewItem)?.Tag is OnMapEntry entry) GoToOnMap(entry); };
        return tree;
    }

    private void StopOnMapAnimators()
    {
        foreach (var animator in _onMapAnimators) animator.Stop();
        _onMapAnimators.Clear();
    }

    /// <summary>The lists again, from the map as it is now (and the search box).</summary>
    private void RefreshOnThisMap()
    {
        if (_onMapObjects is null || _onMapCharacters is null || _onMapEvents is null || _onMapHouses is null || _onMapSearch is null) return;
        StopOnMapAnimators();
        foreach (var tree in new[] { _onMapObjects, _onMapCharacters, _onMapEvents, _onMapHouses }) tree.Items.Clear();
        if (_map is null)
        {
            _onMapObjects.Items.Add(new TreeViewItem { Header = "(no map open)", IsEnabled = false });
            return;
        }
        string search = _onMapSearch.Text.Trim();
        bool searching = search.Length > 0;
        bool Matches(string name) => !searching || name.Contains(search, StringComparison.CurrentCultureIgnoreCase);

        // Objects: in the Object Editor's folders, then those in none; then Books and Containers (as the Objects tab).
        var byFolder = new Dictionary<int, List<OnMapEntry>>();
        var noFolder = new List<OnMapEntry>();
        var books = new List<OnMapEntry>();
        var containers = new List<OnMapEntry>();
        var characters = new List<OnMapEntry>();
        var events = new List<OnMapEntry>();
        foreach (var (layout, dx, dy, houseName) in MapAndPlottedHouses())
        {
            foreach (var placed in layout.Objects.Where(o => o.ContainerInstanceId < 0))
            {
                if (_objectDatabase.Objects.FirstOrDefault(d => d.Id == placed.ObjectId) is not { } def || !Matches(def.Name)) continue;
                var entry = new OnMapEntry(placed, def.Name, placed.X + dx, placed.Y + dy, FloorOf(placed), ObjectPaletteSprite(def), ObjectIcon(def), houseName);
                if (def.FolderId >= 0 && _objectDatabase.Folders.Any(f => f.Id == def.FolderId))
                {
                    if (!byFolder.TryGetValue(def.FolderId, out var list)) byFolder[def.FolderId] = list = new List<OnMapEntry>();
                    list.Add(entry);
                }
                else noFolder.Add(entry);
            }
            foreach (var item in layout.Items)
            {
                if (item.Category == ReferencedItemCategory.Book && _bookDatabase.Books.FirstOrDefault(b => b.Id == item.ItemId) is { } book && Matches(book.Name))
                    books.Add(new OnMapEntry(item, book.Name, item.X + dx, item.Y + dy, FloorOf(item), PaletteSprite(book.SpriteGroup, book.SpriteName), FirstFrameOf(book.SpriteGroup, book.SpriteName), houseName));
                else if (item.Category == ReferencedItemCategory.Container && _containerDatabase.Containers.FirstOrDefault(c => c.Id == item.ItemId) is { IsBodyContainer: false } container && Matches(container.Name))
                    containers.Add(new OnMapEntry(item, container.Name, item.X + dx, item.Y + dy, FloorOf(item), PaletteSprite(container.SpriteGroup, container.SpriteName), FirstFrameOf(container.SpriteGroup, container.SpriteName), houseName));
            }
            // Characters: each with its look as placed (its pose and facing), animated.
            foreach (var placed in layout.Characters)
            {
                if (_characterDatabase.Characters.FirstOrDefault(c => c.Id == placed.CharacterId) is not { } character || !Matches(character.Name)) continue;
                var sprite = ResolveCharacterSprite(character, placed.PoseId, placed.Facing) ?? ResolveCharacterSprite(character, -1, placed.Facing);
                var icon = LoadCharacterFrame(character, placed.PoseId, placed.Facing) ?? LoadCharacterFrame(character, -1, placed.Facing);
                characters.Add(new OnMapEntry(placed, character.Name, placed.X + dx, placed.Y + dy, placed.Floor, sprite, icon, houseName));
            }
            foreach (var gameEvent in layout.Events.Where(e => Matches(e.Name)))
                events.Add(new OnMapEntry(gameEvent, gameEvent.Name, gameEvent.X + dx, gameEvent.Y + dy, gameEvent.Floor, null, null, houseName));
        }

        var groups = new List<(string Key, string Title, List<OnMapEntry> Entries)>();
        foreach (var folder in _objectDatabase.Folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
            if (byFolder.TryGetValue(folder.Id, out var list)) groups.Add(($"folder:{folder.Id}", folder.Name, list));
        if (noFolder.Count > 0) groups.Add(("folder:none", "(in no folder)", noFolder));
        if (books.Count > 0) groups.Add(("books", BooksFilter, books));
        if (containers.Count > 0) groups.Add(("containers", ContainersFilter, containers));
        foreach (var (key, title, entries) in groups) _onMapObjects.Items.Add(OnMapFolder(key, title, Sorted(entries), searching));
        int objectCount = groups.Sum(g => g.Entries.Count);
        if (objectCount == 0) _onMapObjects.Items.Add(new TreeViewItem { Header = searching ? "(nothing matches)" : "(no objects placed yet)", IsEnabled = false });

        foreach (var entry in Sorted(characters)) _onMapCharacters.Items.Add(OnMapRow(entry));
        if (characters.Count == 0) _onMapCharacters.Items.Add(new TreeViewItem { Header = searching ? "(nothing matches)" : "(no characters placed yet)", IsEnabled = false });

        foreach (var entry in Sorted(events)) _onMapEvents.Items.Add(OnMapRow(entry));
        if (events.Count == 0) _onMapEvents.Items.Add(new TreeViewItem { Header = searching ? "(nothing matches)" : "(no events placed yet)", IsEnabled = false });

        // Houses (and vehicles): each plotted copy, its footprint; searching, a house counts if anything inside it matches.
        var houses = new List<OnMapEntry>();
        foreach (var plot in _map.Houses)
        {
            if (_houses.FirstOrDefault(h => h.Id == plot.HouseId) is not { } house) continue;
            string? inside = null;
            if (searching && !Matches(house.Name))
            {
                inside = HouseContentNames(house.Layout).FirstOrDefault(Matches);
                if (inside is null) continue;
            }
            houses.Add(new OnMapEntry(plot, house.Name, plot.X, plot.Y, 1, null, null, null, Math.Max(1, house.Layout.Width), Math.Max(1, house.Layout.Height),
                (house.Vehicle is not null ? "vehicle" : "house") + (inside is not null ? $", has \"{inside}\"" : "")));
        }
        foreach (var entry in Sorted(houses)) _onMapHouses.Items.Add(OnMapRow(entry));
        if (houses.Count == 0) _onMapHouses.Items.Add(new TreeViewItem { Header = searching ? "(nothing matches)" : "(no houses plotted yet)", IsEnabled = false });

        if (_onMapObjectsTab is not null) _onMapObjectsTab.Header = $"Objects ({objectCount})";
        if (_onMapCharactersTab is not null) _onMapCharactersTab.Header = $"Characters ({characters.Count})";
        if (_onMapEventsTab is not null) _onMapEventsTab.Header = $"Events ({events.Count})";
        if (_onMapHousesTab is not null) _onMapHousesTab.Header = $"Houses ({houses.Count})";
    }

    /// <summary>The names of what's inside a house: its objects, books, containers, characters and events.</summary>
    private IEnumerable<string> HouseContentNames(MapData layout)
    {
        foreach (var placed in layout.Objects)
            if (_objectDatabase.Objects.FirstOrDefault(d => d.Id == placed.ObjectId) is { } def) yield return def.Name;
        foreach (var item in layout.Items)
        {
            if (item.Category == ReferencedItemCategory.Book && _bookDatabase.Books.FirstOrDefault(b => b.Id == item.ItemId) is { } book) yield return book.Name;
            else if (item.Category == ReferencedItemCategory.Container && _containerDatabase.Containers.FirstOrDefault(c => c.Id == item.ItemId) is { } container) yield return container.Name;
        }
        foreach (var placed in layout.Characters)
            if (_characterDatabase.Characters.FirstOrDefault(c => c.Id == placed.CharacterId) is { } character) yield return character.Name;
        foreach (var gameEvent in layout.Events) yield return gameEvent.Name;
    }

    private static List<OnMapEntry> Sorted(IEnumerable<OnMapEntry> entries) =>
        entries.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(e => e.Floor).ThenBy(e => e.Y).ThenBy(e => e.X).ToList();

    /// <summary>A folder: its rows made when it's opened (searching: open, and made now).</summary>
    private TreeViewItem OnMapFolder(string key, string title, List<OnMapEntry> entries, bool searching)
    {
        var node = new TreeViewItem { Header = $"\U0001F4C1 {title}  ({entries.Count})", FontWeight = FontWeights.SemiBold };
        bool filled = false;
        void Fill()
        {
            if (filled) return;
            filled = true;
            node.Items.Clear();
            foreach (var entry in entries) node.Items.Add(OnMapRow(entry));
        }
        node.Items.Add(new TreeViewItem { Header = "...", IsEnabled = false }); // so it can be opened
        node.Expanded += (_, e) => { if (!ReferenceEquals(e.OriginalSource, node)) return; _onMapExpanded.Add(key); Fill(); };
        node.Collapsed += (_, e) => { if (ReferenceEquals(e.OriginalSource, node)) _onMapExpanded.Remove(key); };
        if (searching || _onMapExpanded.Contains(key)) { Fill(); node.IsExpanded = true; }
        return node;
    }

    /// <summary>A row: its look (animated, silently), its name, where it is, and the house it's in.</summary>
    private TreeViewItem OnMapRow(OnMapEntry entry)
    {
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        if (entry.Thing is GameEvent or PlacedHouse)
        {
            bool house = entry.Thing is PlacedHouse;
            header.Children.Add(new Border
            {
                Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(house ? Color.FromRgb(0xA0, 0x6E, 0x2E) : Color.FromRgb(0x2E, 0xA0, 0x2E)),
                Child = new TextBlock { Text = house ? "H" : "E", Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
            });
        }
        else
        {
            var image = new Image { Width = 24, Height = 24, Margin = new Thickness(0, 0, 8, 0), Source = entry.Icon, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            if (entry.Sprite is { Frames.Count: > 1 } sprite && !(entry.Thing is ObjectInstance o && _objectDatabase.Objects.FirstOrDefault(d => d.Id == o.ObjectId) is { FramesAreStates: true }))
                _onMapAnimators.Add(new SpriteAnimator(sprite, image, _resourceDatabase, 0)); // animated, silently
            header.Children.Add(image);
        }
        header.Children.Add(new TextBlock { Text = entry.Name, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Normal });
        string where = $"   ({entry.X}, {entry.Y}){(entry.Floor > 1 ? $"  floor {entry.Floor}" : "")}"
            + (entry.Thing is PlacedHouse ? $"  {entry.Width}x{entry.Height}" : "")
            + (entry.Note is not null ? $"  {entry.Note}" : "")
            + (entry.InHouse is not null ? $"  in house \"{entry.InHouse}\"" : "");
        header.Children.Add(new TextBlock { Text = where, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.6, FontWeight = FontWeights.Normal });
        var row = new TreeViewItem { Header = header, Tag = entry, ToolTip = "Click: go to it on the map" };
        row.MouseLeftButtonUp += (_, e) => { if (row.IsSelected) { GoToOnMap(entry); e.Handled = true; } }; // clicked again: there again
        return row;
    }

    /// <summary>To it on the map: its floor, scrolled to the middle of the view, outlined and pulsing for a few seconds.</summary>
    private void GoToOnMap(OnMapEntry entry)
    {
        if (_map is null || _tileset is null) return;
        if (entry.Floor != _currentFloor) FloorCombo.SelectedIndex = Math.Clamp(entry.Floor, 1, MapFloors.MaxFloors) - 1; // builds the map on that floor
        MapScrollViewer.UpdateLayout();

        // Where it's drawn (its picture), else its cells (a house: its whole footprint; inside a house: its cell).
        FrameworkElement? visual = entry.InHouse is not null ? null : entry.Thing switch
        {
            ObjectInstance or PlacedItemInstance => VisualOfThing(entry.Thing),
            PlacedCharacter placed => _mapCharacterVisuals.GetValueOrDefault(placed),
            GameEvent gameEvent => _mapEventVisuals.GetValueOrDefault(gameEvent.Id) as FrameworkElement,
            _ => null
        };
        double tileW = _tileset.TileWidth, tileH = _tileset.TileHeight;
        var area = new Rect(entry.X * tileW, entry.Y * tileH, entry.Width * tileW, entry.Height * tileH);
        if (visual is not null && !double.IsNaN(Canvas.GetLeft(visual)) && !double.IsNaN(Canvas.GetTop(visual)))
        {
            double width = double.IsNaN(visual.Width) ? visual.ActualWidth : visual.Width, height = double.IsNaN(visual.Height) ? visual.ActualHeight : visual.Height;
            if (width > 0 && height > 0) area = new Rect(Canvas.GetLeft(visual), Canvas.GetTop(visual), width, height);
        }

        double scale = ZoomScale;
        MapScrollViewer.ScrollToHorizontalOffset((area.X + area.Width / 2) * scale - MapScrollViewer.ViewportWidth / 2);
        MapScrollViewer.ScrollToVerticalOffset((area.Y + area.Height / 2) * scale - MapScrollViewer.ViewportHeight / 2);
        HighlightOnMap(area, scale);
        StatusText.Text = $"\"{entry.Name}\" - cell ({entry.X}, {entry.Y}), floor {entry.Floor}" + (entry.InHouse is not null ? $", in house \"{entry.InHouse}\"." : ".");
    }

    /// <summary>A bright outline round it and a wider ring round that, pulsing, gone after a few seconds (or the next one).</summary>
    private void HighlightOnMap(Rect area, double scale)
    {
        ClearOnMapHighlight();
        double line = 3 / Math.Max(0.05, scale); // about 3 screen pixels at any zoom
        var pulse = new DoubleAnimation(1, 0.25, TimeSpan.FromMilliseconds(450)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
        foreach (var (grow, brush, dashed) in new[] { (line * 2, (Brush)Brushes.Yellow, false), (line * 8, Brushes.Magenta, true) })
        {
            var ring = new Rectangle
            {
                Width = area.Width + grow * 2, Height = area.Height + grow * 2, Stroke = brush, StrokeThickness = line, IsHitTestVisible = false,
                StrokeDashArray = dashed ? new DoubleCollection { 2, 1 } : null
            };
            Canvas.SetLeft(ring, area.X - grow);
            Canvas.SetTop(ring, area.Y - grow);
            Panel.SetZIndex(ring, int.MaxValue);
            ring.BeginAnimation(OpacityProperty, pulse);
            MapCanvas.Children.Add(ring);
            _onMapHighlight.Add(ring);
        }
        _onMapHighlightTimer?.Stop();
        _onMapHighlightTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _onMapHighlightTimer.Tick += (_, _) => ClearOnMapHighlight();
        _onMapHighlightTimer.Start();
    }

    private void ClearOnMapHighlight()
    {
        _onMapHighlightTimer?.Stop();
        foreach (var ring in _onMapHighlight) MapCanvas.Children.Remove(ring);
        _onMapHighlight.Clear();
    }
}
