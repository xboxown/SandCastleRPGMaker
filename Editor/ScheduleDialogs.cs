using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Click a cell - or a thing - on a picture of the open map: where a schedule
/// entry sends the character, where a command happens, which object it uses.
/// It opens at the Map Editor's 100% (the game's full-screen size) with the
/// same zoom levels (Ctrl + wheel), both scroll bars (Shift + wheel: sideways),
/// any floor 1-9, and "See inside" (roofs left out). Every object, book and
/// container on the floor is shown; the one under the mouse gets a green
/// outline and a click selects it (only the kinds the window can use, when it
/// picks a thing); elsewhere a click picks the cell.
/// </summary>
public class MapCellPickerWindow : Window
{
    public (int X, int Y)? Picked { get; private set; }
    /// <summary>The floor the cell was picked on.</summary>
    public int PickedFloor { get; private set; } = 1;
    /// <summary>The thing clicked on (its key, as ProjectSession.MapThingsProvider gives it), or null for a bare cell.</summary>
    public string? PickedThingKey { get; private set; }

    private readonly Canvas _canvas = new() { Background = Brushes.Black, Cursor = Cursors.Cross };
    private readonly Image _picture = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Visible, VerticalScrollBarVisibility = ScrollBarVisibility.Visible };
    private readonly ComboBox _zoom = new() { Width = 70, Margin = new Thickness(10, 0, 0, 0), ToolTip = "Zoom - 100% is the Map Editor's 100% (the game's own size in full screen). Ctrl + mouse wheel zooms too." };
    private readonly Border _thingOutline = new() { BorderBrush = Brushes.LimeGreen, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly Rectangle _cellOutline = new() { Stroke = Brushes.LimeGreen, IsHitTestVisible = false, Visibility = Visibility.Collapsed };
    private readonly int _tileWidth, _tileHeight, _columns, _rows;
    private readonly Func<string, bool>? _thingFilter;
    private List<MapPickerThing> _things = new();
    private double _zoom100 = 1;
    private int[] _zoomLevels = { 10, 20, 25, 33, 50, 75, 100, 150, 200, 300 };
    private double _scale = 1;

    /// <param name="selectThings">Picking a thing: which keys can be picked (a door, a chair...). Null: any object shown can be clicked to take its cell.</param>
    public MapCellPickerWindow(BitmapSource map, int tileWidth, int tileHeight, int columns, int rows, (int X, int Y)? current, (int X, int Y)? placedSpot, Func<string, bool>? selectThings = null)
    {
        Title = selectThings is null ? "Pick the spot on the map" : "Pick it on the map";
        (_tileWidth, _tileHeight, _columns, _rows) = (tileWidth, tileHeight, columns, rows);
        _thingFilter = selectThings;
        Width = 1100;
        Height = 800;
        MinWidth = 480;
        MinHeight = 320;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.NearestNeighbor); // crisp pixels, like the Map Editor

        _canvas.Children.Add(_picture);
        ShowPicture(map);

        Rectangle Marker(Brush stroke, (int X, int Y) cell, string tip)
        {
            var r = new Rectangle { Width = tileWidth, Height = tileHeight, Stroke = stroke, IsHitTestVisible = false, ToolTip = tip };
            Canvas.SetLeft(r, cell.X * tileWidth);
            Canvas.SetTop(r, cell.Y * tileHeight);
            Panel.SetZIndex(r, 10);
            _canvas.Children.Add(r);
            return r;
        }
        var markers = new List<Shape>();
        if (placedSpot is { } placed) markers.Add(Marker(Brushes.DeepSkyBlue, placed, "Where the character is placed"));
        if (current is { } cur && cur.X >= 0) markers.Add(Marker(Brushes.Gold, cur, "Current spot"));
        _cellOutline.Width = tileWidth;
        _cellOutline.Height = tileHeight;
        Panel.SetZIndex(_cellOutline, 11);
        Panel.SetZIndex(_thingOutline, 11);
        _canvas.Children.Add(_cellOutline);
        _canvas.Children.Add(_thingOutline);

        var status = new TextBlock { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
            Text = selectThings is null ? "Click a cell (or a thing, for its cell). Blue = where the character is placed, gold = the current spot." : "Click it - its outline goes green under the mouse. Gold = the current one." };
        _canvas.MouseMove += (_, e) =>
        {
            var point = e.GetPosition(_canvas);
            var thing = ThingAt(point);
            int x = (int)(point.X / tileWidth), y = (int)(point.Y / tileHeight);
            if (thing is not null)
            {
                _thingOutline.Width = thing.Bounds.Width + 2;
                _thingOutline.Height = thing.Bounds.Height + 2;
                Canvas.SetLeft(_thingOutline, thing.Bounds.X - 1);
                Canvas.SetTop(_thingOutline, thing.Bounds.Y - 1);
                _thingOutline.Visibility = Visibility.Visible;
                _cellOutline.Visibility = Visibility.Collapsed;
                status.Text = $"Floor {PickedFloor}: \"{thing.Name}\" at cell ({thing.Cell.X}, {thing.Cell.Y}) - click to choose it.";
                return;
            }
            _thingOutline.Visibility = Visibility.Collapsed;
            if (selectThings is not null || x < 0 || y < 0 || x >= columns || y >= rows) { _cellOutline.Visibility = Visibility.Collapsed; return; }
            Canvas.SetLeft(_cellOutline, x * tileWidth);
            Canvas.SetTop(_cellOutline, y * tileHeight);
            _cellOutline.Visibility = Visibility.Visible;
            status.Text = $"Floor {PickedFloor}, cell ({x}, {y}) - click to choose it.";
        };
        _canvas.MouseLeave += (_, _) => { _thingOutline.Visibility = Visibility.Collapsed; _cellOutline.Visibility = Visibility.Collapsed; };
        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            var point = e.GetPosition(_canvas);
            if (ThingAt(point) is { } thing)
            {
                Picked = thing.Cell;
                PickedThingKey = thing.Key;
                DialogResult = true;
                return;
            }
            if (selectThings is not null) return; // a thing is wanted: a bare cell won't do
            int x = (int)(point.X / tileWidth), y = (int)(point.Y / tileHeight);
            if (x < 0 || y < 0 || x >= columns || y >= rows) return;
            Picked = (x, y);
            DialogResult = true;
        };
        _scroller.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                int index = Math.Clamp(_zoom.SelectedIndex + (e.Delta > 0 ? 1 : -1), 0, _zoomLevels.Length - 1);
                SetZoom(index, e.GetPosition(_scroller));
                e.Handled = true;
            }
            else if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _scroller.ScrollToHorizontalOffset(_scroller.HorizontalOffset - e.Delta);
                e.Handled = true;
            }
        };

        var bar = new DockPanel { Margin = new Thickness(6) };
        var left = new StackPanel { Orientation = Orientation.Horizontal };
        var floorPicker = ProjectSession.MapFloorPictureProvider;
        if (floorPicker is not null)
        {
            var floors = new ComboBox { Width = 80, ItemsSource = Enumerable.Range(1, MapFloors.MaxFloors).Select(f => $"Floor {f}").ToList(), ToolTip = "Which floor to see and pick on (the Map Editor's floors 1-9)." };
            var inside = new CheckBox { Content = "See inside", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
                ToolTip = "Leaves out the roofs, so the inside of houses shows - as in the game when you walk in." };
            PickedFloor = Math.Clamp(ProjectSession.MapEditorFloorProvider?.Invoke() ?? 1, 1, MapFloors.MaxFloors);
            floors.SelectedIndex = PickedFloor - 1;
            void Redraw()
            {
                PickedFloor = floors.SelectedIndex + 1;
                if (floorPicker(PickedFloor, inside.IsChecked == true) is not { } picture) return;
                ShowPicture(picture.Picture);
                _things = picture.Things.Where(t => _thingFilter?.Invoke(t.Key) ?? true).ToList();
                _zoom100 = picture.Zoom100;
                _zoomLevels = picture.ZoomLevels;
            }
            floors.SelectionChanged += (_, _) => Redraw();
            inside.Checked += (_, _) => Redraw();
            inside.Unchecked += (_, _) => Redraw();
            Redraw(); // the things, and the editor's 100%
            left.Children.Add(floors);
            left.Children.Add(inside);
        }
        _zoom.ItemsSource = _zoomLevels.Select(z => $"{z}%").ToList();
        _zoom.SelectedIndex = Math.Max(0, Array.IndexOf(_zoomLevels, 100));
        _zoom.SelectionChanged += (_, _) => { if (_zoom.SelectedIndex >= 0) SetZoom(_zoom.SelectedIndex, null); };
        left.Children.Add(_zoom);
        DockPanel.SetDock(left, Dock.Left);
        bar.Children.Add(left);
        bar.Children.Add(status);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        _scroller.Content = _canvas;
        root.Children.Add(_scroller);
        Content = root;
        ApplyScale();

        // Opens on the current spot (or where the character is placed), not always the top-left corner.
        var focus = current is { X: >= 0 } ? current : placedSpot;
        Loaded += (_, _) =>
        {
            if (focus is not { } f) return;
            _scroller.UpdateLayout();
            _scroller.ScrollToHorizontalOffset((f.X + 0.5) * tileWidth * _scale - _scroller.ViewportWidth / 2);
            _scroller.ScrollToVerticalOffset((f.Y + 0.5) * tileHeight * _scale - _scroller.ViewportHeight / 2);
        };
    }

    /// <summary>The topmost thing whose drawn pixels are under this point (map pixels).</summary>
    private MapPickerThing? ThingAt(Point point) =>
        _things.Where(t => t.Hits(point)).OrderByDescending(t => t.ZIndex).ThenByDescending(t => t.Order).FirstOrDefault();

    private void SetZoom(int index, Point? anchor)
    {
        var at = anchor ?? new Point(_scroller.ViewportWidth / 2, _scroller.ViewportHeight / 2);
        double mapX = (_scroller.HorizontalOffset + at.X) / _scale, mapY = (_scroller.VerticalOffset + at.Y) / _scale;
        if (_zoom.SelectedIndex != index) { _zoom.SelectedIndex = index; return; } // its SelectionChanged comes back here
        ApplyScale();
        _scroller.UpdateLayout();
        _scroller.ScrollToHorizontalOffset(mapX * _scale - at.X);
        _scroller.ScrollToVerticalOffset(mapY * _scale - at.Y);
    }

    private void ApplyScale()
    {
        int percent = _zoom.SelectedIndex >= 0 && _zoom.SelectedIndex < _zoomLevels.Length ? _zoomLevels[_zoom.SelectedIndex] : 100;
        _scale = _zoom100 * percent / 100.0;
        _canvas.LayoutTransform = new ScaleTransform(_scale, _scale);
        double line = 2 / _scale; // outlines stay 2 screen pixels thick at any zoom
        _thingOutline.BorderThickness = new Thickness(line);
        _cellOutline.StrokeThickness = line;
        foreach (var marker in _canvas.Children.OfType<Rectangle>()) marker.StrokeThickness = line;
    }

    private void ShowPicture(BitmapSource map)
    {
        _picture.Source = map;
        _picture.Width = map.PixelWidth;
        _picture.Height = map.PixelHeight;
        _canvas.Width = Math.Max(map.PixelWidth, _columns * _tileWidth);
        _canvas.Height = Math.Max(map.PixelHeight, _rows * _tileHeight);
    }
}

/// <summary>The event commands a Custom schedule runs when the character arrives at its spot - the same command editor as everywhere else.</summary>
public class CustomScheduleWindow : Window
{
    private readonly ScheduleEntry _entry;
    private readonly TextBox _name;
    private readonly EventEditorControl _editor;
    private readonly ObservableCollection<EventCommandPage> _pages;

    public CustomScheduleWindow(ScheduleEntry entry)
    {
        _entry = entry;
        Title = $"Custom Schedule [{BuildInfo.CurrentBuild}]";
        Width = 760;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var top = new StackPanel { Margin = new Thickness(10, 10, 10, 4) };
        top.Children.Add(new TextBlock { Text = "Activity name", FontWeight = FontWeights.Bold });
        _name = new TextBox { Text = entry.CustomName, Margin = new Thickness(0, 2, 0, 6) };
        top.Children.Add(_name);
        top.Children.Add(new TextBlock
        {
            Text = "These commands run each time the character arrives at the schedule's spot and starts this activity (Exult's scripted schedules). Afterwards it stands there until the next schedule change. Only the first page is used.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75
        });

        _pages = new ObservableCollection<EventCommandPage> { new() { Label = "1", Commands = entry.CustomCommands.Select(Clone).ToList() } };
        _editor = new EventEditorControl
        {
            ResourceDatabase = ProjectSession.ResourcesProvider?.Invoke(),
            Variables = ProjectSession.VariablesProvider?.Invoke(),
            CurrentBookLocalVariables = new List<VariableDefinition>(),
            Books = ProjectSession.BooksProvider?.Invoke(),
            Objects = ProjectSession.ObjectsProvider?.Invoke(),
            Containers = ProjectSession.ContainersProvider?.Invoke(),
            Sprites = ProjectSession.Sprites,
            Margin = new Thickness(10, 0, 10, 0)
        };
        _editor.Pages = _pages;

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(10) };
        var ok = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        ok.Click += (_, _) =>
        {
            _entry.CustomName = _name.Text.Trim();
            _entry.CustomCommands = _pages.Count > 0 ? _pages[0].Commands.ToList() : new List<EventCommand>();
            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(_editor);
        Content = root;
    }

    private static EventCommand Clone(EventCommand c) => new() { Type = c.Type, IndentLevel = c.IndentLevel, Parameters = new Dictionary<string, string>(c.Parameters) };
}
