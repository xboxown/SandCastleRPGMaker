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
/// Click a cell on a picture of the open map - where a schedule entry sends
/// the character, where a command happens. The whole map, scrolled with the
/// scroll bars or the mouse wheel (Shift + wheel: sideways); any floor 1-9;
/// "See inside" leaves out roofs and whatever is over a standing person's
/// head (a cave's mountain top), so the rooms show, as in the game.
/// </summary>
public class MapCellPickerWindow : Window
{
    public (int X, int Y)? Picked { get; private set; }
    /// <summary>The floor the cell was picked on.</summary>
    public int PickedFloor { get; private set; } = 1;

    private readonly Canvas _canvas = new() { Background = Brushes.Black, Cursor = Cursors.Cross };
    private readonly Image _picture = new();
    private readonly ScrollViewer _scroller = new() { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private readonly int _tileWidth, _tileHeight, _columns, _rows;

    public MapCellPickerWindow(BitmapSource map, int tileWidth, int tileHeight, int columns, int rows, (int X, int Y)? current, (int X, int Y)? placedSpot)
    {
        Title = "Pick the spot on the map";
        (_tileWidth, _tileHeight, _columns, _rows) = (tileWidth, tileHeight, columns, rows);
        Width = Math.Min(1100, map.PixelWidth + 60);
        Height = Math.Min(800, map.PixelHeight + 150);
        MinWidth = 420;
        MinHeight = 300;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.CanResizeWithGrip;

        _canvas.Children.Add(_picture);
        ShowPicture(map);

        Rectangle Marker(Brush stroke, (int X, int Y) cell, string tip)
        {
            var r = new Rectangle { Width = tileWidth, Height = tileHeight, Stroke = stroke, StrokeThickness = 2, IsHitTestVisible = false, ToolTip = tip };
            Canvas.SetLeft(r, cell.X * tileWidth);
            Canvas.SetTop(r, cell.Y * tileHeight);
            _canvas.Children.Add(r);
            return r;
        }
        if (placedSpot is { } placed) Marker(Brushes.DeepSkyBlue, placed, "Where the character is placed");
        if (current is { } cur && cur.X >= 0) Marker(Brushes.Gold, cur, "Current spot");
        var hover = Marker(Brushes.Lime, (0, 0), "");
        hover.Visibility = Visibility.Collapsed;

        var status = new TextBlock { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Text = "Blue = where the character is placed, gold = the current spot. Click a cell to choose it." };
        _canvas.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(_canvas);
            int x = (int)(p.X / tileWidth), y = (int)(p.Y / tileHeight);
            if (x < 0 || y < 0 || x >= columns || y >= rows) { hover.Visibility = Visibility.Collapsed; return; }
            Canvas.SetLeft(hover, x * tileWidth);
            Canvas.SetTop(hover, y * tileHeight);
            hover.Visibility = Visibility.Visible;
            status.Text = $"Floor {PickedFloor}, cell ({x}, {y}) - click to choose it.";
        };
        _canvas.MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(_canvas);
            int x = (int)(p.X / tileWidth), y = (int)(p.Y / tileHeight);
            if (x < 0 || y < 0 || x >= columns || y >= rows) return;
            Picked = (x, y);
            DialogResult = true;
        };
        // Shift + wheel scrolls sideways (the wheel alone scrolls up and down).
        _scroller.PreviewMouseWheel += (_, e) =>
        {
            if (Keyboard.Modifiers != ModifierKeys.Shift) return;
            _scroller.ScrollToHorizontalOffset(_scroller.HorizontalOffset - e.Delta);
            e.Handled = true;
        };

        var bar = new DockPanel { Margin = new Thickness(6) };
        var floorPicker = ProjectSession.MapFloorPictureProvider;
        if (floorPicker is not null)
        {
            var floors = new ComboBox { Width = 80, ItemsSource = Enumerable.Range(1, MapFloors.MaxFloors).Select(f => $"Floor {f}").ToList(), ToolTip = "Which floor to see and pick on (the Map Editor's floors 1-9)." };
            var inside = new CheckBox { Content = "See inside", IsChecked = true, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0),
                ToolTip = "Leaves out roofs and anything over a standing person's head (a cave's mountain top), so the inside of houses and caves shows - as in the game when you walk in." };
            PickedFloor = Math.Clamp(ProjectSession.MapEditorFloorProvider?.Invoke() ?? 1, 1, MapFloors.MaxFloors);
            floors.SelectedIndex = PickedFloor - 1;
            void Redraw()
            {
                PickedFloor = floors.SelectedIndex + 1;
                if (floorPicker(PickedFloor, inside.IsChecked == true) is { } picture) ShowPicture(picture.Picture);
            }
            floors.SelectionChanged += (_, _) => Redraw();
            inside.Checked += (_, _) => Redraw();
            inside.Unchecked += (_, _) => Redraw();
            var left = new StackPanel { Orientation = Orientation.Horizontal };
            left.Children.Add(floors);
            left.Children.Add(inside);
            DockPanel.SetDock(left, Dock.Left);
            bar.Children.Add(left);
        }
        bar.Children.Add(status);

        var root = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        _scroller.Content = _canvas;
        root.Children.Add(_scroller);
        Content = root;

        // Open on the current spot (or where the character is placed), not always the top-left corner.
        if ((current is { X: >= 0 } ? current : placedSpot) is { } focus)
            Loaded += (_, _) =>
            {
                _scroller.ScrollToHorizontalOffset(focus.X * tileWidth - _scroller.ViewportWidth / 2);
                _scroller.ScrollToVerticalOffset(focus.Y * tileHeight - _scroller.ViewportHeight / 2);
            };
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
