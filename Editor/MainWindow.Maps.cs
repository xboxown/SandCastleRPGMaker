using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Maps tab: a project's worlds. One map is one world - a story in one
/// world needs one map; a portal to a dark world is another. The first is the
/// starting world (the game begins there, saved as the project's map as
/// always); the others are saved with it. Click a world to edit it; Add makes
/// one (its name, size and whether it wraps around asked); right-click renames,
/// turns wrapping around the edges on or off, resizes (what a smaller
/// size would cut off is listed first), makes it the starting world, or
/// deletes it (asked first). Each world has its own tileset, tiles, objects,
/// characters, events, zones, weather and ambient sound.
/// </summary>
public partial class MainWindow
{
    private List<MapData> _worlds = new();
    private ListBox? _worldList;
    private bool _showingWorlds;

    /// <summary>The Maps tab, added once beside Tileset / Objects / Characters.</summary>
    private void BuildMapsTab()
    {
        if (_worldList is not null) return;
        _worldList = new ListBox { Margin = new Thickness(6) };
        _worldList.SelectionChanged += (_, _) =>
        {
            if (_showingWorlds || _worldList.SelectedItem is not WorldRow row || ReferenceEquals(row.Map, _map)) return;
            ShowWorld(row.Map);
        };
        var menu = new ContextMenu();
        MenuItem Item(string header, Action<MapData> act)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => { if (_worldList.SelectedItem is WorldRow row) act(row.Map); };
            menu.Items.Add(item);
            return item;
        }
        Item("Rename...", RenameWorld);
        Item("Resize...", ResizeWorld);
        var wraps = Item("Wraps around the edges", world =>
        {
            world.WrapsAround = !world.WrapsAround;
            RefreshWorldList();
            StatusText.Text = world.WrapsAround
                ? $"\"{world.MapName}\" wraps around: walk off one edge and you come back on the other."
                : $"\"{world.MapName}\" no longer wraps: its edges are the end of the world.";
        });
        menu.Opened += (_, _) => wraps.IsChecked = _worldList.SelectedItem is WorldRow { Map.WrapsAround: true };
        Item("Make it the starting world", MakeStartingWorld);
        menu.Items.Add(new Separator());
        Item("Delete...", DeleteWorld);
        _worldList.ContextMenu = menu;
        // Right-click picks the world under the mouse first.
        _worldList.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(_worldList, d) is ListBoxItem item) item.IsSelected = true;
        };

        var add = new Button { Content = "Add Map...", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(6, 6, 6, 0), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) => AddWorld();
        var note = new TextBlock
        {
            Text = "One map is one world (a portal to a dark world is another). The first is the starting world. Click to edit; right-click to rename, resize, make it wrap around (or not) or delete.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(6, 6, 6, 0), FontSize = 11
        };
        var panel = new DockPanel();
        DockPanel.SetDock(add, Dock.Top);
        DockPanel.SetDock(note, Dock.Top);
        panel.Children.Add(add);
        panel.Children.Add(note);
        panel.Children.Add(_worldList);
        PaletteTabs.Items.Insert(0, new TabItem { Header = "Maps", Content = panel });
        PaletteTabs.SelectedIndex = 1; // still opens on the Tileset tab
        BuildHousesTab(); // the House Editor's tab, last (MainWindow.Houses.cs)
    }

    private sealed record WorldRow(MapData Map, bool Starting)
    {
        public override string ToString() => $"{(Starting ? "★ " : "")}{Map.MapName}   ({Map.Width} x {Map.Height}{(Map.WrapsAround ? ", wraps" : "")})";
    }

    /// <summary>The project's worlds as loaded: its map (the starting world) and the others; each with its own id.</summary>
    private void LoadWorlds(MapData? start, List<MapData>? others)
    {
        _worlds = new List<MapData>();
        if (start is not null) _worlds.Add(start);
        if (others is not null) _worlds.AddRange(others);
        int next = 1;
        var used = new HashSet<int>();
        foreach (var world in _worlds)
        {
            if (world.Id <= 0 || !used.Add(world.Id)) // an older project's map, or a repeated id: its own one
            {
                while (used.Contains(next)) next++;
                world.Id = next;
                used.Add(next);
            }
        }
        RefreshWorldList();
    }

    private void RefreshWorldList()
    {
        BuildMapsTab();
        _showingWorlds = true;
        _worldList!.ItemsSource = _worlds.Select((w, i) => new WorldRow(w, i == 0)).ToList();
        _worldList.SelectedItem = _worldList.Items.Cast<WorldRow>().FirstOrDefault(r => ReferenceEquals(r.Map, _map));
        _showingWorlds = false;
    }

    /// <summary>Edit this world: its tileset, palette and map.</summary>
    private void ShowWorld(MapData world)
    {
        bool wasHouse = _editingHouse is not null;
        _editingHouse = null; // a map picked: no longer editing a house (MainWindow.Houses.cs)
        _mapBeforeHouse = null;
        _map = world;
        ShowMapInEditor();
        RefreshWorldList();
        if (wasHouse) RefreshHouseList();
        StatusText.Text = $"Editing the world \"{world.MapName}\" ({world.Width} x {world.Height}).";
    }

    private void AddWorld()
    {
        if (_tileset is null || _tilesetBitmap is null)
        {
            MessageBox.Show(this, "Pick a tileset first (Tileset tab) - a new map starts with it.", "Add Map", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var ask = new MapSizeWindow("Add Map", _worlds.Count == 0 ? "World" : $"World {_worlds.Count + 1}", _map?.Width ?? 30, _map?.Height ?? 20, askWrap: true) { Owner = this };
        if (ask.ShowDialog() != true) return;
        var tileset = new TilesetDefinition { TileWidth = _tileset.TileWidth, TileHeight = _tileset.TileHeight, TilesetSpriteName = _tileset.TilesetSpriteName };
        var world = MapData.CreateEmpty(ask.MapName, ask.MapWidth, ask.MapHeight, tileset);
        world.WrapsAround = ask.Wraps;
        world.Id = _worlds.Count == 0 ? 1 : _worlds.Max(w => w.Id) + 1;
        _worlds.Add(world);
        ShowWorld(world);
    }

    private void RenameWorld(MapData world)
    {
        var ask = new MapSizeWindow("Rename Map", world.MapName, world.Width, world.Height, nameOnly: true) { Owner = this };
        if (ask.ShowDialog() != true) return;
        world.MapName = ask.MapName;
        RefreshWorldList();
    }

    private void ResizeWorld(MapData world)
    {
        var ask = new MapSizeWindow("Resize Map", world.MapName, world.Width, world.Height, sizeOnly: true) { Owner = this };
        if (ask.ShowDialog() != true || (ask.MapWidth == world.Width && ask.MapHeight == world.Height)) return;
        var lost = world.LostByResize(ask.MapWidth, ask.MapHeight);
        if (lost.Count > 0 && MessageBox.Show(this,
                $"Making \"{world.MapName}\" {ask.MapWidth} x {ask.MapHeight} cuts off:\n\n  {string.Join("\n  ", lost)}\n\nThey'll be gone from the map. Go ahead?",
                "Resize Map", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        world.Resize(ask.MapWidth, ask.MapHeight);
        if (ReferenceEquals(world, _map)) ShowMapInEditor();
        RefreshWorldList();
        StatusText.Text = $"\"{world.MapName}\" is now {world.Width} x {world.Height}.";
    }

    private void MakeStartingWorld(MapData world)
    {
        if (_worlds.IndexOf(world) <= 0) return;
        _worlds.Remove(world);
        _worlds.Insert(0, world);
        RefreshWorldList();
        StatusText.Text = $"The game now begins in \"{world.MapName}\" (at its Start Position).";
    }

    private void DeleteWorld(MapData world)
    {
        if (_worlds.Count <= 1)
        {
            MessageBox.Show(this, "A game needs at least one map - this is the only one.", "Delete Map", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Delete the world \"{world.MapName}\" and everything on it (tiles, objects, characters placed there, events, zones)?\n\nThis can't be undone (unless you close without saving).",
                "Delete Map", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        bool wasShown = ReferenceEquals(world, _map);
        _worlds.Remove(world);
        if (wasShown) ShowWorld(_worlds[0]);
        else RefreshWorldList();
    }
}

/// <summary>A map's name and size (Add, Rename, Resize) - and, for a new one, whether it wraps around.</summary>
internal sealed class MapSizeWindow : Window
{
    public string MapName { get; private set; } = "";
    public int MapWidth { get; private set; }
    public int MapHeight { get; private set; }
    public bool Wraps { get; private set; }

    public MapSizeWindow(string title, string name, int width, int height, bool nameOnly = false, bool sizeOnly = false, bool askWrap = false)
    {
        Title = title;
        Width = 340;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        var rows = new StackPanel { Margin = new Thickness(12) };
        var nameBox = new TextBox { Text = name };
        var widthBox = new TextBox { Text = width.ToString(), Width = 70 };
        var heightBox = new TextBox { Text = height.ToString(), Width = 70 };
        if (!sizeOnly)
        {
            rows.Children.Add(new TextBlock { Text = "Name", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
            rows.Children.Add(nameBox);
        }
        if (!nameOnly)
        {
            var size = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            size.Children.Add(new TextBlock { Text = "Width (cells):", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
            size.Children.Add(widthBox);
            size.Children.Add(new TextBlock { Text = "Height:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) });
            size.Children.Add(heightBox);
            rows.Children.Add(size);
        }
        var wrapBox = new CheckBox { Content = "Wraps around the edges", Margin = new Thickness(0, 10, 0, 0) };
        if (askWrap)
        {
            rows.Children.Add(wrapBox);
            rows.Children.Add(new TextBlock
            {
                Text = "Walk off one edge and you come back on the other (Ultima V and VI). Unticked: the edges are the end of the world. You can change it later (right-click the map).",
                TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(18, 2, 0, 0)
            });
        }
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            MapName = nameBox.Text.Trim();
            if (!sizeOnly && MapName.Length == 0) { MessageBox.Show(this, "Give the map a name.", Title); return; }
            if (!nameOnly && (!int.TryParse(widthBox.Text, out int w) || !int.TryParse(heightBox.Text, out int h) || w < 1 || h < 1 || w > 4000 || h > 4000))
            {
                MessageBox.Show(this, "Width and height are numbers of cells, 1 to 4000.", Title);
                return;
            }
            MapWidth = nameOnly ? width : int.Parse(widthBox.Text);
            MapHeight = nameOnly ? height : int.Parse(heightBox.Text);
            Wraps = wrapBox.IsChecked == true;
            DialogResult = true;
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        rows.Children.Add(buttons);
        Content = rows;
        Loaded += (_, _) => (sizeOnly ? widthBox : nameBox).Focus();
    }
}
