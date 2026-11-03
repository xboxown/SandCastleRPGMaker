using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Map Editor > Mode > Zones: named areas of the map - a house, a pub, a
/// hospital - each with a purpose. Drag on the map to draw one; drag a zone
/// to move it; double-click it (or right-click) to name it, set its purpose
/// and size, or delete it. Characters' schedules go to a zone, or to the
/// nearest zone with a purpose (Character Editor > Schedule).
/// </summary>
public partial class MainWindow
{
    private Canvas? _zoneLayer;
    private (int X, int Y)? _zoneDrawStart;
    private (MapZone Zone, int GrabX, int GrabY, int FromX, int FromY)? _zoneMove;
    private MapZone? _selectedZone;

    private void ZoneModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _editMode = EditMode.Zone;
        ClearModeChecks();
        ZoneModeMenuItem.IsChecked = true;
        DrawZones();
        StatusText.Text = "Zones mode: drag on the map to draw a zone (a house, a pub, a hospital); drag a zone to move it; double-click or right-click it to name it, set its purpose and size, or delete it.";
    }

    private IEnumerable<MapZone> ZonesOnFloor => _map?.Zones.Where(z => z.Floor == _currentFloor) ?? Enumerable.Empty<MapZone>();

    private MapZone? ZoneAt(int x, int y) => ZonesOnFloor.LastOrDefault(z => z.Contains(x, y));

    private static Color ZoneColor(MapZone zone)
    {
        try { return (Color)ColorConverter.ConvertFromString(zone.Color); }
        catch { return Color.FromRgb(0x3F, 0xA7, 0xFF); }
    }

    /// <summary>The zones of the floor shown, over the map - only in Zones mode.</summary>
    private void DrawZones()
    {
        if (_zoneLayer is not null) MapCanvas.Children.Remove(_zoneLayer);
        _zoneLayer = null;
        if (_editMode != EditMode.Zone || _map is null || _tileset is null) return;
        _zoneLayer = FlatOnMap(new Canvas { IsHitTestVisible = false }); // the Ultima 8 view: the zones lie flat, as diamonds
        Panel.SetZIndex(_zoneLayer, 5000);
        int w = CellW, h = CellH;
        foreach (var zone in ZonesOnFloor)
        {
            var color = ZoneColor(zone);
            bool picked = ReferenceEquals(zone, _selectedZone);
            var rect = new Rectangle
            {
                Width = zone.Width * w, Height = zone.Height * h,
                Fill = new SolidColorBrush(Color.FromArgb(picked ? (byte)90 : (byte)55, color.R, color.G, color.B)),
                Stroke = new SolidColorBrush(color), StrokeThickness = picked ? 3 : 2
            };
            Canvas.SetLeft(rect, zone.X * w);
            Canvas.SetTop(rect, zone.Y * h);
            _zoneLayer.Children.Add(rect);
            var label = new TextBlock
            {
                Text = zone.Describe(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, FontSize = 12,
                Background = new SolidColorBrush(Color.FromArgb(170, color.R, color.G, color.B)), Padding = new Thickness(3, 1, 3, 1)
            };
            Canvas.SetLeft(label, zone.X * w + 2);
            Canvas.SetTop(label, zone.Y * h + 2);
            if (IsUltima8) label.RenderTransform = U8ProjectionCanvas.UprightTransform; // its words upright over the diamond
            _zoneLayer.Children.Add(label);
        }
        if (_zoneDrawStart is { } start && _lastHoverCell is var (hx, hy))
        {
            int x0 = Math.Min(start.X, hx), y0 = Math.Min(start.Y, hy), x1 = Math.Max(start.X, hx), y1 = Math.Max(start.Y, hy);
            var drawing = new Rectangle { Width = (x1 - x0 + 1) * w, Height = (y1 - y0 + 1) * h, Stroke = Brushes.White, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 4, 3 }, Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)) };
            Canvas.SetLeft(drawing, x0 * w);
            Canvas.SetTop(drawing, y0 * h);
            _zoneLayer.Children.Add(drawing);
        }
        MapCanvas.Children.Add(_zoneLayer);
    }

    private void ZoneMode_MouseLeftButtonDown(int x, int y, int clickCount)
    {
        if (_map is null) return;
        var zone = ZoneAt(x, y);
        if (zone is not null)
        {
            _selectedZone = zone;
            if (clickCount >= 2) { _zoneMove = null; EditZone(zone); return; }
            _zoneMove = (zone, x, y, zone.X, zone.Y);
            StatusText.Text = $"Zone \"{zone.Describe()}\": drag to move it; double-click or right-click to edit it.";
            DrawZones();
            return;
        }
        _selectedZone = null;
        _zoneDrawStart = (x, y);
        DrawZones();
    }

    private void ZoneMode_MouseMove(int x, int y, System.Windows.Input.MouseButtonState left)
    {
        if (left != System.Windows.Input.MouseButtonState.Pressed) { _zoneMove = null; _zoneDrawStart = null; return; }
        if (_zoneMove is { } move && _map is not null)
        {
            move.Zone.X = Math.Clamp(move.FromX + x - move.GrabX, 0, Math.Max(0, _map.Width - move.Zone.Width));
            move.Zone.Y = Math.Clamp(move.FromY + y - move.GrabY, 0, Math.Max(0, _map.Height - move.Zone.Height));
            DrawZones();
            return;
        }
        if (_zoneDrawStart is not null) DrawZones();
    }

    /// <summary>True if Zones mode took the release.</summary>
    private bool ZoneMode_MouseLeftButtonUp(int x, int y)
    {
        if (_editMode != EditMode.Zone || _map is null) return false;
        if (_zoneMove is not null) { _zoneMove = null; DrawZones(); return true; }
        if (_zoneDrawStart is not { } start) return true;
        _zoneDrawStart = null;
        x = Math.Clamp(x, 0, _map.Width - 1);
        y = Math.Clamp(y, 0, _map.Height - 1);
        var zone = new MapZone
        {
            Id = _map.NextZoneId(), Name = $"Zone {_map.Zones.Count + 1}", Floor = _currentFloor,
            X = Math.Min(start.X, x), Y = Math.Min(start.Y, y), Width = Math.Abs(x - start.X) + 1, Height = Math.Abs(y - start.Y) + 1,
            Color = ZonePalette[_map.Zones.Count % ZonePalette.Length]
        };
        if (!EditZone(zone, isNew: true)) { DrawZones(); return true; }
        _map.Zones.Add(zone);
        _selectedZone = zone;
        DrawZones();
        StatusText.Text = $"Zone \"{zone.Describe()}\" made: {zone.Width} x {zone.Height} cells on floor {zone.Floor}.";
        return true;
    }

    private void ZoneMode_MouseRightButtonDown(int x, int y)
    {
        if (ZoneAt(x, y) is not { } zone) return;
        _selectedZone = zone;
        DrawZones();
        var menu = new ContextMenu();
        var edit = new MenuItem { Header = "Name, purpose, size..." };
        edit.Click += (_, _) => EditZone(zone);
        var delete = new MenuItem { Header = "Delete zone" };
        delete.Click += (_, _) =>
        {
            if (_map is null) return;
            int users = CharactersUsingZone(zone);
            if (users > 0 && MessageBox.Show(this, $"\"{zone.Describe()}\" is used by {users} schedule line{(users == 1 ? "" : "s")} - they'll go to their placed spot instead. Delete it?", "Delete zone", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            _map.Zones.Remove(zone);
            _selectedZone = null;
            DrawZones();
        };
        menu.Items.Add(edit);
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    private int CharactersUsingZone(MapZone zone) =>
        _characterDatabase.Characters.Sum(c => c.Schedules.Count(s => s.ZoneId == zone.Id) + c.ScheduleRules.Count(r => r.Do.ZoneId == zone.Id));

    private static readonly string[] ZonePalette = { "#3FA7FF", "#FF8C3F", "#5CD65C", "#E05CE0", "#FFD23F", "#3FE0D0", "#FF5C5C", "#A07CFF" };

    /// <summary>The zone's name, purpose, colour and cells. False if cancelled.</summary>
    private bool EditZone(MapZone zone, bool isNew = false)
    {
        if (_map is null) return false;
        var window = new Window
        {
            Title = isNew ? "New Zone" : $"Zone - {zone.Name}", Width = 380, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = this, ShowInTaskbar = false
        };
        var rows = new StackPanel { Margin = new Thickness(12) };
        TextBlock Label(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 2) };
        var name = new TextBox { Text = zone.Name };
        var purposes = Zones.CommonPurposes.Concat(_map.Zones.Select(z => z.Purpose)).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(p => p).ToList();
        var purpose = new ComboBox { IsEditable = true, ItemsSource = purposes, Text = zone.Purpose };
        var color = new ComboBox { ItemsSource = ZonePalette, SelectedItem = ZonePalette.Contains(zone.Color) ? zone.Color : ZonePalette[0] };
        color.ItemTemplate = null;
        var cells = new WrapPanel();
        TextBox Num(int value) => new() { Width = 50, Text = value.ToString(), Margin = new Thickness(0, 0, 10, 0) };
        var bx = Num(zone.X); var by = Num(zone.Y); var bw = Num(zone.Width); var bh = Num(zone.Height);
        foreach (var (text, box) in new[] { ("X", bx), ("Y", by), ("Width", bw), ("Height", bh) })
        {
            var cell = new StackPanel();
            cell.Children.Add(new TextBlock { Text = text, FontSize = 11 });
            cell.Children.Add(box);
            cells.Children.Add(cell);
        }
        rows.Children.Add(Label("Name (yours: \"The Blue Boar\", \"Iolo's house\")"));
        rows.Children.Add(name);
        rows.Children.Add(Label("Purpose (what kind of place - schedules can go to \"the nearest Pub\")"));
        rows.Children.Add(purpose);
        rows.Children.Add(Label("Colour in the Map Editor"));
        rows.Children.Add(color);
        // Its own ambient sound: a Cave stops the world's ambient sounds inside it and loops this instead.
        var none = new ResourceEntry { Id = -1, Name = "(None)" };
        var sounds = new List<ResourceEntry> { none };
        sounds.AddRange(_resourceDatabase.Entries.Where(r => r.Kind == ResourceKind.Sound));
        var ambient = new ComboBox { ItemsSource = sounds, DisplayMemberPath = "Name", SelectedItem = sounds.FirstOrDefault(s => s.Id == zone.AmbientSoundId) ?? none };
        var listen = new Button { Content = "Play", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(6, 0, 0, 0) };
        listen.Click += (_, _) => { if (ambient.SelectedItem is ResourceEntry { Id: >= 0 } s) SoundPlayback.TryPlay(_resourceDatabase, s.Id); };
        var hush = new Button { Content = "Stop", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(4, 0, 0, 0) };
        hush.Click += (_, _) => SoundPlayback.StopAll();
        var ambientRow = new DockPanel();
        DockPanel.SetDock(hush, Dock.Right); DockPanel.SetDock(listen, Dock.Right);
        ambientRow.Children.Add(hush); ambientRow.Children.Add(listen); ambientRow.Children.Add(ambient);
        rows.Children.Add(Label("Ambient sound inside it (a Cave: instead of the world's - none keeps a cave quiet)"));
        rows.Children.Add(ambientRow);
        rows.Children.Add(Label($"Cells (floor {zone.Floor})"));
        rows.Children.Add(cells);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        ok.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(name.Text)) { MessageBox.Show(window, "Give the zone a name.", window.Title); return; }
            if (!int.TryParse(bx.Text, out int x) || !int.TryParse(by.Text, out int y) || !int.TryParse(bw.Text, out int w) || !int.TryParse(bh.Text, out int h) || w < 1 || h < 1)
            { MessageBox.Show(window, "X, Y, Width and Height are whole numbers (size at least 1).", window.Title); return; }
            zone.Name = name.Text.Trim();
            zone.Purpose = purpose.Text.Trim();
            zone.AmbientSoundId = ambient.SelectedItem is ResourceEntry picked ? picked.Id : -1;
            zone.Color = color.SelectedItem as string ?? zone.Color;
            zone.X = Math.Clamp(x, 0, _map.Width - 1);
            zone.Y = Math.Clamp(y, 0, _map.Height - 1);
            zone.Width = Math.Clamp(w, 1, _map.Width - zone.X);
            zone.Height = Math.Clamp(h, 1, _map.Height - zone.Y);
            window.DialogResult = true;
        };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        rows.Children.Add(buttons);
        window.Content = rows;
        bool done = window.ShowDialog() == true;
        DrawZones();
        return done;
    }
}
