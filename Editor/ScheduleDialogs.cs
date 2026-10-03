using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>Click a cell on a picture of the open map - where a schedule entry sends the character.</summary>
public class MapCellPickerWindow : Window
{
    public (int X, int Y)? Picked { get; private set; }

    public MapCellPickerWindow(BitmapSource map, int tileWidth, int tileHeight, int columns, int rows, (int X, int Y)? current, (int X, int Y)? placedSpot)
    {
        Title = "Pick the spot on the map";
        Width = Math.Min(1100, map.PixelWidth + 60);
        Height = Math.Min(800, map.PixelHeight + 120);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var canvas = new Canvas { Width = map.PixelWidth, Height = map.PixelHeight, Background = Brushes.Black, Cursor = Cursors.Cross };
        canvas.Children.Add(new Image { Source = map, Width = map.PixelWidth, Height = map.PixelHeight });

        Rectangle Marker(Brush stroke, (int X, int Y) cell, string tip)
        {
            var r = new Rectangle { Width = tileWidth, Height = tileHeight, Stroke = stroke, StrokeThickness = 2, IsHitTestVisible = false, ToolTip = tip };
            Canvas.SetLeft(r, cell.X * tileWidth);
            Canvas.SetTop(r, cell.Y * tileHeight);
            canvas.Children.Add(r);
            return r;
        }
        if (placedSpot is { } placed) Marker(Brushes.DeepSkyBlue, placed, "Where the character is placed");
        if (current is { } cur && cur.X >= 0) Marker(Brushes.Gold, cur, "Current spot");
        var hover = Marker(Brushes.Lime, (0, 0), "");
        hover.Visibility = Visibility.Collapsed;

        var status = new TextBlock { Margin = new Thickness(6), Text = "Blue = where the character is placed, gold = the current spot. Click a cell to choose it." };
        canvas.MouseMove += (_, e) =>
        {
            var p = e.GetPosition(canvas);
            int x = (int)(p.X / tileWidth), y = (int)(p.Y / tileHeight);
            if (x < 0 || y < 0 || x >= columns || y >= rows) { hover.Visibility = Visibility.Collapsed; return; }
            Canvas.SetLeft(hover, x * tileWidth);
            Canvas.SetTop(hover, y * tileHeight);
            hover.Visibility = Visibility.Visible;
            status.Text = $"Cell ({x}, {y}) - click to choose it.";
        };
        canvas.MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(canvas);
            int x = (int)(p.X / tileWidth), y = (int)(p.Y / tileHeight);
            if (x < 0 || y < 0 || x >= columns || y >= rows) return;
            Picked = (x, y);
            DialogResult = true;
        };

        var root = new DockPanel();
        DockPanel.SetDock(status, Dock.Top);
        root.Children.Add(status);
        root.Children.Add(new ScrollViewer { Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
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
