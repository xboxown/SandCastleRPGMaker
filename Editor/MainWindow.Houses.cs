using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The House Editor (Houses tab): a house is built once and plotted on maps
/// as often as you like.
///  - New House asks its name and size - the size is its foundation - and
///    opens it in the Map Editor like a map of its own: tiles on any floor,
///    objects, things, characters, events, zones, all with the usual tools.
///    "Back to the map" (or a map in the Maps tab) ends the editing.
///  - Mode > Houses plots the house picked in the list: a see-through picture
///    of it follows the mouse - green where it fits, red where something's in
///    the way or it goes off the map - and a click puts it there.
///  - A plotted house stays linked: change the house and every plotted copy
///    changes. Right-click one (in Houses mode) to edit its house or take it
///    off the map. In play every copy is real pieces, characters and events
///    each its own (HouseBuilder).
///  - The Vehicles tab is the same for vehicles - houses that move (Engine/
///    VehicleModel.cs): built facing south from objects (painted tiles don't
///    move), with their own settings (how they move, speed, deed, sounds).
/// </summary>
public partial class MainWindow
{
    private List<HouseTemplate> _houses = new();
    private HouseTemplate? _editingHouse;
    private MapData? _mapBeforeHouse;
    private ListBox? _houseList;
    private ListBox? _vehicleList;
    /// <summary>The list plotting takes its house (or vehicle) from.</summary>
    private ListBox? _plotList;
    private MenuItem? _vehicleModeItem;
    private MenuItem? _houseModeItem;
    private FrameworkElement? _houseGhost;
    private (int HouseId, int Floor)? _houseGhostOf;
    private readonly List<FrameworkElement> _placedHouseVisuals = new();

    private HouseTemplate? PickedHouse => ((_plotList ?? _houseList)?.SelectedItem as HouseRow)?.House;

    private sealed record HouseRow(HouseTemplate House, bool Editing)
    {
        public override string ToString() => $"{(Editing ? "✎ " : "")}{House.Name}   ({House.Layout.Width} x {House.Layout.Height}{(House.Vehicle is { } v ? $", {v.Movement}" : "")})";
    }

    /// <summary>The Houses and Vehicles tabs (after Characters) and Mode > Houses / Vehicles, made once.</summary>
    private void BuildHousesTab()
    {
        if (_houseList is not null) return;
        _houseList = BuildTemplateTab(vehicles: false);
        _vehicleList = BuildTemplateTab(vehicles: true);
        _houseModeItem = new MenuItem { Header = "Houses (plot a house)", IsCheckable = true, ToolTip = "Plot the house picked in the Houses tab: a see-through picture follows the mouse - green fits, red is in the way" };
        _houseModeItem.Click += (_, _) => EnterHouseMode(_houseList);
        _vehicleModeItem = new MenuItem { Header = "Vehicles (plot a vehicle)", IsCheckable = true, ToolTip = "Plot the vehicle picked in the Vehicles tab: a see-through picture follows the mouse - green fits, red is in the way" };
        _vehicleModeItem.Click += (_, _) => EnterHouseMode(_vehicleList);
        ModeMenu.Items.Insert(ModeMenu.Items.IndexOf(ZoneModeMenuItem) + 1, _houseModeItem);
        ModeMenu.Items.Insert(ModeMenu.Items.IndexOf(_houseModeItem) + 1, _vehicleModeItem);
        RefreshHouseList();
    }

    /// <summary>One tab: its list, its buttons, its banner while one of its own is being edited.</summary>
    private ListBox BuildTemplateTab(bool vehicles)
    {
        string kind = vehicles ? "vehicle" : "house";
        var list = new ListBox { Margin = new Thickness(6), Tag = vehicles };
        HouseTemplate? Picked() => (list.SelectedItem as HouseRow)?.House;
        list.MouseDoubleClick += (_, _) => { if (Picked() is { } house) EditHouse(house); };
        list.SelectionChanged += (_, _) => { _houseGhostOf = null; HideHouseGhost(); };
        var menu = new ContextMenu();
        void Item(string header, Action<HouseTemplate> act)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => { if (Picked() is { } house) act(house); };
            menu.Items.Add(item);
        }
        Item($"Edit the {kind}", EditHouse);
        if (vehicles) Item("Settings (moves on, speed, deed, sounds)...", EditVehicleSettings);
        Item("Plot it on the map", _ => EnterHouseMode(list));
        Item("Rename...", RenameHouse);
        Item("Resize...", ResizeHouse);
        menu.Items.Add(new Separator());
        Item("Delete...", DeleteHouse);
        list.ContextMenu = menu;
        list.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(list, d) is ListBoxItem item) item.IsSelected = true;
        };

        Button Small(string text, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 6, 4) };
            b.Click += click;
            return b;
        }
        var buttons = new WrapPanel { Margin = new Thickness(6, 6, 6, 0) };
        buttons.Children.Add(Small(vehicles ? "New Vehicle..." : "New House...", (_, _) => NewHouse(vehicles)));
        buttons.Children.Add(Small("Edit", (_, _) => { if (Picked() is { } h) EditHouse(h); }));
        if (vehicles) buttons.Children.Add(Small("Settings...", (_, _) => { if (Picked() is { } h) EditVehicleSettings(h); }));
        buttons.Children.Add(Small("Plot", (_, _) => EnterHouseMode(list)));
        var banner = new TextBlock { TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold, Foreground = Brushes.DarkGreen, Margin = new Thickness(6, 6, 6, 0), Visibility = Visibility.Collapsed };
        var back = Small("Back to the map", (_, _) => EndHouseEditing());
        back.Margin = new Thickness(6, 4, 6, 0);
        back.HorizontalAlignment = HorizontalAlignment.Left;
        back.Visibility = Visibility.Collapsed;
        _templateBanners.Add((vehicles, banner, back));
        var note = new TextBlock
        {
            Text = vehicles
                ? "A vehicle is a house that moves: a ship, a cart, a carpet, a car. Build it facing south (toward you) from objects - painted tiles don't move with it. Give its pieces their roles (Object Editor > Vehicle part): a driver's seat or controls to drive it from, wheels that turn, a look for each way it can face. Settings: land, sea or air, its speed, a deed, its sounds."
                : "Build a house once, plot it as often as you like. New House: its name and size (its foundation), then build it with the usual tools. Plot (Mode > Houses): green fits, red is in the way. Plotted copies follow every change to the house.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, Margin = new Thickness(6, 6, 6, 0), FontSize = 11
        };
        var panel = new DockPanel();
        foreach (var top in new FrameworkElement[] { buttons, banner, back, note })
        {
            DockPanel.SetDock(top, Dock.Top);
            panel.Children.Add(top);
        }
        panel.Children.Add(list);
        PaletteTabs.Items.Add(new TabItem { Header = vehicles ? "Vehicles" : "Houses", Content = panel });
        return list;
    }

    private readonly List<(bool Vehicles, TextBlock Banner, Button Back)> _templateBanners = new();

    private void EditVehicleSettings(HouseTemplate vehicle)
    {
        if (vehicle.Vehicle is null) return;
        new VehicleSettingsWindow(vehicle.Name, vehicle.Vehicle, _objectDatabase, _resourceDatabase) { Owner = this }.ShowDialog();
        RefreshHouseList();
    }

    private void RefreshHouseList()
    {
        foreach (var list in new[] { _houseList, _vehicleList })
        {
            if (list is null) continue;
            bool vehicles = list.Tag is true;
            var picked = (list.SelectedItem as HouseRow)?.House;
            list.ItemsSource = _houses.Where(h => (h.Vehicle is not null) == vehicles).Select(h => new HouseRow(h, ReferenceEquals(h, _editingHouse))).ToList();
            list.SelectedItem = list.Items.Cast<HouseRow>().FirstOrDefault(r => ReferenceEquals(r.House, picked ?? _editingHouse));
        }
        foreach (var (vehicles, banner, back) in _templateBanners)
        {
            bool editing = _editingHouse is not null && (_editingHouse.Vehicle is not null) == vehicles;
            banner.Text = editing ? $"Editing the {(vehicles ? "vehicle" : "house")} \"{_editingHouse!.Name}\"." : "";
            banner.Visibility = back.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    /// <summary>The project's houses as loaded (or none, for a new project).</summary>
    private void LoadHouses(List<HouseTemplate>? houses)
    {
        _houses = houses ?? new List<HouseTemplate>();
        _editingHouse = null;
        _mapBeforeHouse = null;
        RefreshHouseList();
    }

    private void NewHouse(bool vehicle = false)
    {
        string kind = vehicle ? "Vehicle" : "House";
        if (_tileset is null || _tilesetBitmap is null)
        {
            MessageBox.Show(this, $"Pick a tileset first (Tileset tab) - a new {kind.ToLowerInvariant()} is built with it.", $"New {kind}", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var ask = new MapSizeWindow($"New {kind}", $"{kind} {_houses.Count(h => (h.Vehicle is not null) == vehicle) + 1}", vehicle ? 4 : 8, vehicle ? 6 : 6) { Owner = this };
        if (ask.ShowDialog() != true) return;
        var tileset = new TilesetDefinition { TileWidth = _tileset.TileWidth, TileHeight = _tileset.TileHeight, TilesetSpriteName = _tileset.TilesetSpriteName };
        var house = new HouseTemplate { Id = _houses.Count == 0 ? 1 : _houses.Max(h => h.Id) + 1, Name = ask.MapName, Layout = MapData.CreateEmpty(ask.MapName, ask.MapWidth, ask.MapHeight, tileset), Vehicle = vehicle ? new VehicleSettings() : null };
        _houses.Add(house);
        RefreshHouseList();
        EditHouse(house);
    }

    /// <summary>The house opened in the Map Editor like a map of its own.</summary>
    private void EditHouse(HouseTemplate house)
    {
        if (_editingHouse is null) _mapBeforeHouse = _map;
        _editingHouse = house;
        if (_editMode == EditMode.House) MapModeMenuItem_Click(this, new RoutedEventArgs()); // no plotting houses inside a house
        _map = house.Layout;
        ShowMapInEditor();
        RefreshWorldList();
        RefreshHouseList();
        StatusText.Text = house.Vehicle is not null
            ? $"Editing the vehicle \"{house.Name}\" ({house.Layout.Width} x {house.Layout.Height}): build it facing south (toward you) from objects - painted tiles don't move with it. Back to the map (Vehicles tab) when it's done."
            : $"Editing the house \"{house.Name}\" ({house.Layout.Width} x {house.Layout.Height}): build it with the usual tools, on any floor. Back to the map (Houses tab) when it's done.";
    }

    /// <summary>Back to the map the house was opened from.</summary>
    private void EndHouseEditing()
    {
        if (_editingHouse is null) return;
        var house = _editingHouse;
        _editingHouse = null;
        var back = _mapBeforeHouse is not null && _worlds.Contains(_mapBeforeHouse) ? _mapBeforeHouse : _worlds.FirstOrDefault();
        _mapBeforeHouse = null;
        if (back is not null)
        {
            _map = back;
            ShowMapInEditor();
            RefreshWorldList();
        }
        RefreshHouseList();
        StatusText.Text = $"Back to the map. Every plotted \"{house.Name}\" shows the house as it is now.";
    }

    private void RenameHouse(HouseTemplate house)
    {
        var ask = new MapSizeWindow("Rename House", house.Name, house.Layout.Width, house.Layout.Height, nameOnly: true) { Owner = this };
        if (ask.ShowDialog() != true) return;
        house.Name = ask.MapName;
        house.Layout.MapName = ask.MapName;
        RefreshHouseList();
        DrawPlacedHouses();
    }

    private void ResizeHouse(HouseTemplate house)
    {
        var ask = new MapSizeWindow("Resize House", house.Name, house.Layout.Width, house.Layout.Height, sizeOnly: true) { Owner = this };
        if (ask.ShowDialog() != true || (ask.MapWidth == house.Layout.Width && ask.MapHeight == house.Layout.Height)) return;
        var lost = house.Layout.LostByResize(ask.MapWidth, ask.MapHeight);
        if (lost.Count > 0 && MessageBox.Show(this,
                $"Making \"{house.Name}\" {ask.MapWidth} x {ask.MapHeight} cuts off:\n\n  {string.Join("\n  ", lost)}\n\nThey'll be gone from the house. Go ahead?",
                "Resize House", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        house.Layout.Resize(ask.MapWidth, ask.MapHeight);
        if (ReferenceEquals(house, _editingHouse)) ShowMapInEditor();
        else DrawPlacedHouses();
        RefreshHouseList();
        StatusText.Text = $"\"{house.Name}\" is now {house.Layout.Width} x {house.Layout.Height}. Plotted copies grow or shrink with it - check none now runs into something.";
    }

    private void DeleteHouse(HouseTemplate house)
    {
        int plotted = _worlds.Sum(w => w.Houses.Count(p => p.HouseId == house.Id));
        string where = plotted == 0 ? "It isn't plotted on any map." : $"It's plotted {plotted} time{(plotted == 1 ? "" : "s")} - those copies go too.";
        if (MessageBox.Show(this, $"Delete the house \"{house.Name}\"?\n\n{where}\n\nThis can't be undone (unless you close without saving).", "Delete House", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        if (ReferenceEquals(house, _editingHouse)) EndHouseEditing();
        _houses.Remove(house);
        foreach (var world in _worlds) world.Houses.RemoveAll(p => p.HouseId == house.Id);
        RefreshHouseList();
        DrawPlacedHouses();
    }

    // ---------------- Plotting ----------------

    private void EnterHouseMode(ListBox? from = null)
    {
        if (from is not null && !ReferenceEquals(from, _plotList)) { _plotList = from; _houseGhostOf = null; HideHouseGhost(); }
        if (_editingHouse is not null)
        {
            StatusText.Text = "A house can't be plotted inside a house - go Back to the map first (Houses tab).";
            if (_houseModeItem is not null) _houseModeItem.IsChecked = false;
            return;
        }
        ClearModeChecks();
        _editMode = EditMode.House;
        bool vehicles = (_plotList ?? _houseList)?.Tag is true;
        if ((vehicles ? _vehicleModeItem : _houseModeItem) is { } modeItem) modeItem.IsChecked = true;
        StatusText.Text = PickedHouse is { } house
            ? $"Houses mode: move over the map - \"{house.Name}\" follows the mouse; green fits, red is in the way. Click to plot it. Right-click a plotted house to edit it or take it off the map."
            : vehicles ? "Vehicles mode: pick a vehicle in the Vehicles tab first (or make one with New Vehicle)." : "Houses mode: pick a house in the Houses tab first (or make one with New House).";
    }

    /// <summary>Where a house goes with the mouse on a cell: centred on it.</summary>
    private static (int X, int Y) HouseCornerAt(HouseTemplate house, int cellX, int cellY) => (cellX - house.Layout.Width / 2, cellY - house.Layout.Height / 2);

    /// <summary>Whether the house fits there: on the map, its tileset the map's, and nothing in its foundation - no other house, object, thing, character or event.</summary>
    private bool CanPlotHouse(HouseTemplate house, int x, int y, out string why)
    {
        why = "";
        var map = _map!;
        int w = house.Layout.Width, h = house.Layout.Height;
        bool In(int cx, int cy) => cx >= x && cy >= y && cx < x + w && cy < y + h;
        if (x < 0 || y < 0 || x + w > map.Width || y + h > map.Height) { why = "it goes off the edge of the map"; return false; }
        if (house.Layout.Tileset.TilesetSpriteName != map.Tileset.TilesetSpriteName) { why = $"it's built with the tileset \"{house.Layout.Tileset.TilesetSpriteName}\", this map uses \"{map.Tileset.TilesetSpriteName}\""; return false; }
        foreach (var other in map.Houses)
            if (_houses.FirstOrDefault(t => t.Id == other.HouseId) is { } t
                && other.X < x + w && x < other.X + t.Layout.Width && other.Y < y + h && y < other.Y + t.Layout.Height) { why = $"\"{t.Name}\" is already there"; return false; }
        if (map.Objects.FirstOrDefault(o => o.ContainerInstanceId < 0 && In(o.X, o.Y)) is { } obj) { why = $"{_objectDatabase.Objects.FirstOrDefault(d => d.Id == obj.ObjectId)?.Name ?? "an object"} is in the way at ({obj.X}, {obj.Y})"; return false; }
        if (map.Items.FirstOrDefault(i => In(i.X, i.Y)) is { } item) { why = $"a placed {item.Category.ToString().ToLowerInvariant()} is in the way at ({item.X}, {item.Y})"; return false; }
        if (map.Characters.FirstOrDefault(c => In(c.X, c.Y)) is { } character) { why = $"{_characterDatabase.Characters.FirstOrDefault(c => c.Id == character.CharacterId)?.Name ?? "a character"} stands there at ({character.X}, {character.Y})"; return false; }
        if (map.Events.FirstOrDefault(ev => In(ev.X, ev.Y)) is { } gameEvent) { why = $"the event \"{gameEvent.Name}\" is there at ({gameEvent.X}, {gameEvent.Y})"; return false; }
        return true;
    }

    private void HouseMode_MouseMove(int cellX, int cellY)
    {
        if (PickedHouse is not { } house || _tileset is null) { HideHouseGhost(); return; }
        if (_houseGhost is null || _houseGhostOf != (house.Id, _currentFloor))
        {
            HideHouseGhost();
            var picture = HousePicture(house, _currentFloor);
            picture.Opacity = 0.6;
            var grid = new Grid { IsHitTestVisible = false };
            grid.Children.Add(picture);
            grid.Children.Add(new Rectangle { StrokeThickness = 3, Tag = "outline" });
            Panel.SetZIndex(grid, 50);
            _houseGhost = grid;
            _houseGhostOf = (house.Id, _currentFloor);
            MapCanvas.Children.Add(grid);
        }
        var (x, y) = HouseCornerAt(house, cellX, cellY);
        bool fits = CanPlotHouse(house, x, y, out string why);
        var outline = ((Grid)_houseGhost).Children.OfType<Rectangle>().First();
        outline.Stroke = fits ? Brushes.LimeGreen : Brushes.Red;
        outline.Fill = new SolidColorBrush(fits ? Color.FromArgb(40, 0, 255, 0) : Color.FromArgb(80, 255, 0, 0));
        Canvas.SetLeft(_houseGhost, x * _tileset.TileWidth);
        Canvas.SetTop(_houseGhost, y * _tileset.TileHeight);
        _houseGhost.Visibility = Visibility.Visible;
        StatusText.Text = fits ? $"\"{house.Name}\" fits at ({x}, {y}) - click to plot it." : $"\"{house.Name}\" can't go at ({x}, {y}): {why}.";
    }

    private void HideHouseGhost()
    {
        if (_houseGhost is null) return;
        MapCanvas.Children.Remove(_houseGhost);
        _houseGhost = null;
        _houseGhostOf = null;
    }

    private void HouseMode_Click(int cellX, int cellY)
    {
        if (_map is null) return;
        if (PickedHouse is not { } house) { StatusText.Text = "Pick a house in the Houses tab first (or make one with New House)."; return; }
        var (x, y) = HouseCornerAt(house, cellX, cellY);
        if (!CanPlotHouse(house, x, y, out string why)) { StatusText.Text = $"\"{house.Name}\" can't go there: {why}."; System.Media.SystemSounds.Beep.Play(); return; }
        _map.Houses.Add(new PlacedHouse { Id = _map.Houses.Count == 0 ? 1 : _map.Houses.Max(p => p.Id) + 1, HouseId = house.Id, X = x, Y = y });
        DrawPlacedHouses();
        StatusText.Text = $"\"{house.Name}\" plotted at ({x}, {y}). Right-click it to edit the house or take it off the map.";
    }

    private PlacedHouse? PlacedHouseAt(int x, int y) =>
        _map?.Houses.LastOrDefault(p => _houses.FirstOrDefault(t => t.Id == p.HouseId) is { } t && x >= p.X && y >= p.Y && x < p.X + t.Layout.Width && y < p.Y + t.Layout.Height);

    private void HouseMode_RightClick(int x, int y)
    {
        if (_map is null || PlacedHouseAt(x, y) is not { } placed || _houses.FirstOrDefault(t => t.Id == placed.HouseId) is not { } house) return;
        var menu = new ContextMenu();
        var edit = new MenuItem { Header = $"Edit the house \"{house.Name}\"" };
        edit.Click += (_, _) => { var list = house.Vehicle is null ? _houseList : _vehicleList; if (list is not null) list.SelectedItem = list.Items.Cast<HouseRow>().FirstOrDefault(r => ReferenceEquals(r.House, house)); EditHouse(house); };
        var remove = new MenuItem { Header = "Take it off the map" };
        remove.Click += (_, _) => { _map.Houses.Remove(placed); DrawPlacedHouses(); StatusText.Text = $"\"{house.Name}\" taken off the map (the house itself is kept in the Houses tab)."; };
        menu.Items.Add(edit);
        menu.Items.Add(remove);
        menu.IsOpen = true;
    }

    // ---------------- Drawing ----------------

    /// <summary>Every house plotted on this map, as the house is now (the floor shown), with a dashed outline and its name.</summary>
    private void DrawPlacedHouses()
    {
        foreach (var visual in _placedHouseVisuals) MapCanvas.Children.Remove(visual);
        _placedHouseVisuals.Clear();
        if (_map is null || _tileset is null) return;
        foreach (var placed in _map.Houses)
        {
            if (_houses.FirstOrDefault(t => t.Id == placed.HouseId) is not { } house) continue;
            var grid = new Grid { IsHitTestVisible = false };
            grid.Children.Add(HousePicture(house, _currentFloor));
            grid.Children.Add(new Rectangle { Stroke = Brushes.DarkOrange, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 } });
            grid.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = (house.Vehicle is null ? "🏠 " : "🚗 ") + house.Name, FontSize = 10, Margin = new Thickness(3, 0, 3, 0), Foreground = Brushes.SaddleBrown }
            });
            Canvas.SetLeft(grid, placed.X * _tileset.TileWidth);
            Canvas.SetTop(grid, placed.Y * _tileset.TileHeight);
            Panel.SetZIndex(grid, 8);
            MapCanvas.Children.Add(grid);
            _placedHouseVisuals.Add(grid);
        }
    }

    /// <summary>One floor of a house as a picture: its tiles, objects and things.</summary>
    private Canvas HousePicture(HouseTemplate house, int floor)
    {
        var layout = house.Layout;
        int tw = _tileset!.TileWidth, th = _tileset.TileHeight;
        var canvas = new Canvas { Width = layout.Width * tw, Height = layout.Height * th, ClipToBounds = false };
        if (layout.LayerForFloor(floor) is { } layer)
            for (int y = 0; y < layout.Height; y++)
                for (int x = 0; x < layout.Width; x++)
                {
                    int tile = layer.GetTile(x, y, layout.Width);
                    if (tile < 0 || TileSource(tile) is not { } source) continue;
                    var image = new Image { Source = source, Width = tw, Height = th };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                    Canvas.SetLeft(image, x * tw);
                    Canvas.SetTop(image, y * th);
                    canvas.Children.Add(image);
                }
        foreach (var instance in layout.Objects.Where(o => o.ContainerInstanceId < 0 && FloorOf(o) == floor).OrderBy(o => o.Y).ThenBy(o => o.Z))
        {
            if (_objectDatabase.Objects.FirstOrDefault(o => o.Id == instance.ObjectId) is not { } obj || ObjectIcon(obj, instance) is not { } source) continue;
            var (w, h) = SpriteSizing.Quantize(source.PixelWidth, source.PixelHeight);
            var image = new Image { Source = source, Width = w, Height = h, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            PositionPlacedVisual(image, instance.X, instance.Y, w, h, instance.OffsetX, instance.OffsetY, instance.Z);
            canvas.Children.Add(image);
        }
        foreach (var item in layout.Items.Where(i => FloorOf(i) == floor))
        {
            var sprite = item.Category switch
            {
                ReferencedItemCategory.Book => _bookDatabase.Books.FirstOrDefault(b => b.Id == item.ItemId) is { } book ? _spriteDatabase.Groups.FirstOrDefault(g => g.Name == book.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == book.SpriteName) : null,
                ReferencedItemCategory.Container => _containerDatabase.Containers.FirstOrDefault(c => c.Id == item.ItemId) is { } chest ? _spriteDatabase.Groups.FirstOrDefault(g => g.Name == chest.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == chest.SpriteName) : null,
                _ => null
            };
            if (sprite is not { Frames.Count: > 0 } || !File.Exists(sprite.Frames[0].FilePath) || LoadStaticFrame(sprite.Frames[0].FilePath) is not { } frame) continue;
            var (w, h) = SpriteSizing.Quantize(frame.PixelWidth, frame.PixelHeight);
            var image = new Image { Source = frame, Width = w, Height = h };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            PositionPlacedVisual(image, item.X, item.Y, w, h, 0, 0, item.Z);
            canvas.Children.Add(image);
        }
        return canvas;
    }
}
