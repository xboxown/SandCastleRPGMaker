using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Mode > Wall Plot (Quick Wall Plot): walls drawn with the mouse from a wall kit (Tools > Wall Kits). Every wall cell
/// picks its piece from the wall cells round it - a straight, a corner where the wall turns, a T where it meets
/// another wall, a cross, an end - and the cells round a changed one are fixed up too, like RPG Maker's auto-tiles.
/// Wall pencil: hold the left button and move. Wall rectangle: drag a room's outline. Wall eraser (or the right
/// button with any tool): takes the wall away and fixes the pieces round it. Door / window: a door, a window or a
/// Wall Designer set put into a straight wall, kept when the pieces round it change. A whole stroke is one Undo.
/// The pieces are ordinary objects (Mode > Objects still moves, turns and deletes them one by one).
/// </summary>
public partial class MainWindow
{
    private enum WallTool { Pencil, Rectangle, Eraser, Door }

    private MenuItem? _wallPlotModeItem;
    private TabItem? _wallKitsTab;
    private ListBox? _wallKitList;
    private ComboBox? _wallDoorCombo;
    private WallTool _wallTool = WallTool.Pencil;
    private (int X, int Y)? _wallRectStart;
    private Rectangle? _wallRectPreview;
    private bool _wallStroking, _wallErasing;
    /// <summary>During a stroke: the kit pieces of the picked floor by cell (built once per stroke - a big map is not searched per cell).</summary>
    private Dictionary<(int X, int Y), List<ObjectInstance>>? _wallCells;

    private WallKit? PickedWallKit => _wallKitList?.SelectedItem as WallKit;

    private void AddWallPlotMode()
    {
        if (_wallPlotModeItem is not null) return;
        _wallPlotModeItem = new MenuItem { Header = "Wall Plot (draw walls with a wall kit)", IsCheckable = true, ToolTip = "Draw walls with the mouse: corners, T-joins and ends are picked by themselves from a wall kit (Tools > Wall Kits)" };
        _wallPlotModeItem.Click += (_, _) => EnterWallPlotMode();
        ModeMenu.Items.Insert(ModeMenu.Items.IndexOf(FreeModeMenuItem) + 1, _wallPlotModeItem);
        BuildWallKitsTab();
    }

    private void EnterWallPlotMode()
    {
        _editMode = EditMode.WallPlot;
        ClearModeChecks();
        _wallPlotModeItem!.IsChecked = true;
        RefreshWallKitList();
        PaletteTabs.SelectedItem = _wallKitsTab;
        StatusText.Text = PickedWallKit is null
            ? "Wall Plot: make a wall kit first - Tools > Wall Kits (or the button in the Wall Kits tab)."
            : "Wall Plot: hold the left button and move to draw walls; the right button erases. Tools in the Wall Kits tab.";
    }

    private void BuildWallKitsTab()
    {
        var panel = new StackPanel { Margin = new Thickness(6) };
        panel.Children.Add(new TextBlock { Text = "Mode > Wall Plot: pick a kit and a tool, draw on the map. Corners, T-joins and ends are picked by themselves.", TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
        _wallKitList = new ListBox { Height = 150, DisplayMemberPath = nameof(WallKit.Name) };
        _wallKitList.SelectionChanged += (_, _) => { if (_editMode != EditMode.WallPlot) EnterWallPlotMode(); };
        panel.Children.Add(_wallKitList);
        var edit = new Button { Content = "Wall Kits...", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 4, 0, 8), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Make and change wall kits: the 16 pieces and their nudges" };
        edit.Click += (_, _) => WallKitsMenuItem_Click(this, new RoutedEventArgs());
        panel.Children.Add(edit);
        panel.Children.Add(new TextBlock { Text = "Tool", FontWeight = FontWeights.Bold });
        foreach (var (tool, name, tip) in new[]
                 {
                     (WallTool.Pencil, "Wall pencil", "Hold the left button and move: every cell you pass becomes wall"),
                     (WallTool.Rectangle, "Wall rectangle", "Drag from one corner of a room to the other: its outline becomes wall"),
                     (WallTool.Eraser, "Wall eraser", "Hold and move: the wall goes and the pieces round it are fixed (the right button erases with any tool)"),
                     (WallTool.Door, "Door / window", "Click a straight piece of drawn wall: the door, window or wall set below goes in its place")
                 })
        {
            var t = tool;
            var radio = new RadioButton { Content = name, GroupName = "WallTool", IsChecked = tool == _wallTool, ToolTip = tip, Margin = new Thickness(0, 2, 0, 2) };
            radio.Checked += (_, _) => { _wallTool = t; if (_editMode != EditMode.WallPlot) EnterWallPlotMode(); };
            panel.Children.Add(radio);
        }
        panel.Children.Add(new TextBlock { Text = "Door / window to put in:", Margin = new Thickness(0, 8, 0, 2) });
        _wallDoorCombo = new ComboBox { DisplayMemberPath = nameof(WorldObject.Name), ToolTip = "Doors, windows and Wall objects built from a Wall Designer set (a wall with a window or a doorway)" };
        _wallDoorCombo.DropDownOpened += (_, _) => RefreshWallDoorCombo();
        panel.Children.Add(_wallDoorCombo);
        panel.Children.Add(new TextBlock
        {
            Text = "A whole stroke is one Undo (Ctrl+Z). The pieces are ordinary objects: Mode > Objects still moves, turns or deletes one. Each floor is drawn on its own (Floor in the toolbar).",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 8, 0, 0)
        });
        _wallKitsTab = new TabItem { Header = "Wall Kits", Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } };
        PaletteTabs.Items.Add(_wallKitsTab);
        RefreshWallKitList();
    }

    private void RefreshWallKitList()
    {
        if (_wallKitList is null) return;
        int keep = PickedWallKit?.Id ?? -1;
        _wallKitList.ItemsSource = null;
        _wallKitList.ItemsSource = _objectDatabase.WallKits;
        _wallKitList.SelectedItem = _objectDatabase.WallKits.FirstOrDefault(k => k.Id == keep) ?? _objectDatabase.WallKits.FirstOrDefault();
    }

    private void RefreshWallDoorCombo()
    {
        if (_wallDoorCombo is null) return;
        int keep = (_wallDoorCombo.SelectedItem as WorldObject)?.Id ?? -1;
        var list = _objectDatabase.Objects.Where(o => o.Type is ObjectType.Door or ObjectType.Window || o is { Type: ObjectType.Wall, Wall.DesignId: >= 0 }).OrderBy(o => o.Name).ToList();
        _wallDoorCombo.ItemsSource = list;
        _wallDoorCombo.SelectedItem = list.FirstOrDefault(o => o.Id == keep) ?? list.FirstOrDefault();
    }

    private void WallKitsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        // Kits are saved with the project's objects (as the Wall Designer's sets).
        new WallKitWindow(_objectDatabase, _spriteDatabase, _tileset?.TileWidth ?? 32, _tileset?.TileHeight ?? 32, PickedWallKit?.Id ?? -1) { Owner = this }.ShowDialog();
        RefreshWallKitList();
    }

    // ---------- Mouse ----------

    private void WallPlot_MouseLeftButtonDown(int x, int y)
    {
        if (_map is null) return;
        if (_wallTool == WallTool.Eraser) { BeginWallStroke(erase: true); WallStrokeCell(x, y); return; }
        if (_wallTool == WallTool.Door) { PutDoorInWall(x, y); return; }
        if (PickedWallKit is not { } kit || !kit.HasAnyPiece)
        {
            StatusText.Text = PickedWallKit is null ? "Make a wall kit first: Tools > Wall Kits." : $"\"{PickedWallKit.Name}\" has no pieces yet: Tools > Wall Kits.";
            return;
        }
        if (_wallTool == WallTool.Rectangle) { _wallRectStart = (x, y); ShowWallRect(x, y, x, y); return; }
        BeginWallStroke(erase: false);
        WallStrokeCell(x, y);
    }

    private void WallPlot_MouseRightButtonDown(int x, int y)
    {
        if (_map is null) return;
        BeginWallStroke(erase: true);
        WallStrokeCell(x, y);
    }

    private void WallPlot_MouseMove(int x, int y, MouseEventArgs e)
    {
        if (_wallRectStart is { } start)
        {
            if (e.LeftButton != MouseButtonState.Pressed) { EndWallRect(x, y); return; }
            ShowWallRect(start.X, start.Y, x, y);
            return;
        }
        if (!_wallStroking) return;
        bool held = _wallErasing && e.RightButton == MouseButtonState.Pressed || e.LeftButton == MouseButtonState.Pressed;
        if (!held) { EndWallStroke(); return; }
        WallStrokeCell(x, y);
    }

    /// <summary>The left button let go: a rectangle is drawn; a stroke ends. True when Wall Plot used it.</summary>
    private bool WallPlot_MouseLeftButtonUp(int x, int y)
    {
        if (_editMode != EditMode.WallPlot) return false;
        if (_wallRectStart is not null) EndWallRect(x, y);
        else EndWallStroke();
        return true;
    }

    private void ShowWallRect(int x0, int y0, int x1, int y1)
    {
        if (_wallRectPreview is null)
        {
            _wallRectPreview = new Rectangle { Stroke = Brushes.Orange, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 }, IsHitTestVisible = false };
            Panel.SetZIndex(_wallRectPreview, 100000);
            MapCanvas.Children.Add(_wallRectPreview);
        }
        int left = Math.Min(x0, x1), top = Math.Min(y0, y1);
        Canvas.SetLeft(_wallRectPreview, left * CellW);
        Canvas.SetTop(_wallRectPreview, top * CellH);
        _wallRectPreview.Width = (Math.Abs(x1 - x0) + 1) * CellW;
        _wallRectPreview.Height = (Math.Abs(y1 - y0) + 1) * CellH;
    }

    private void HideWallRect()
    {
        if (_wallRectPreview is not null) MapCanvas.Children.Remove(_wallRectPreview);
        _wallRectPreview = null;
    }

    private void EndWallRect(int x, int y)
    {
        if (_wallRectStart is not { } start || _map is null) return;
        _wallRectStart = null;
        HideWallRect();
        x = Math.Clamp(x, 0, _map.Width - 1);
        y = Math.Clamp(y, 0, _map.Height - 1);
        int left = Math.Min(start.X, x), right = Math.Max(start.X, x), top = Math.Min(start.Y, y), bottom = Math.Max(start.Y, y);
        BeginWallStroke(erase: false);
        for (int cx = left; cx <= right; cx++) { WallStrokeCell(cx, top); WallStrokeCell(cx, bottom); }
        for (int cy = top; cy <= bottom; cy++) { WallStrokeCell(left, cy); WallStrokeCell(right, cy); }
        EndWallStroke();
        StatusText.Text = $"Walled a room from ({left}, {top}) to ({right}, {bottom}).";
    }

    // ---------- Drawing ----------

    private void BeginWallStroke(bool erase)
    {
        _wallStroking = true;
        _wallErasing = erase;
        _wallCells = new Dictionary<(int, int), List<ObjectInstance>>();
        foreach (var o in _map!.Objects)
            if (o.WallKitId > 0 && o.ContainerInstanceId < 0 && FloorOf(o) == _currentFloor)
            {
                if (!_wallCells.TryGetValue((o.X, o.Y), out var list)) _wallCells[(o.X, o.Y)] = list = new List<ObjectInstance>();
                list.Add(o);
            }
    }

    private void EndWallStroke()
    {
        if (_wallStroking && _map is not null) RedrawAfterSettle(); // once a stroke: what's under and over the new walls comes to rest
        _wallStroking = false;
        _wallErasing = false;
        _wallCells = null;
    }

    private bool IsWallCell(int x, int y) => _wallCells!.TryGetValue((x, y), out var list) && list.Count > 0;

    /// <summary>The walls a cell joins: its wall neighbours north, east, south and west (a wrapping map joins across its seam).</summary>
    private int WallJoinsAt(int x, int y)
    {
        int joins = 0;
        if (IsWallCell(WrapX(x), WrapY(y - 1))) joins |= WallKit.North;
        if (IsWallCell(WrapX(x + 1), WrapY(y))) joins |= WallKit.East;
        if (IsWallCell(WrapX(x), WrapY(y + 1))) joins |= WallKit.South;
        if (IsWallCell(WrapX(x - 1), WrapY(y))) joins |= WallKit.West;
        return joins;
    }

    private int WrapX(int x) => _map!.WrapsAround ? (x % _map.Width + _map.Width) % _map.Width : x;
    private int WrapY(int y) => _map!.WrapsAround ? (y % _map.Height + _map.Height) % _map.Height : y;

    /// <summary>One cell of a stroke: it becomes wall (or stops being one), then it and the walls round it pick their pieces anew.</summary>
    private void WallStrokeCell(int x, int y)
    {
        if (_map is null || _wallCells is null || !IsInsideMap(x, y)) return;
        if (_wallErasing)
        {
            if (!IsWallCell(x, y)) return;
            foreach (var piece in _wallCells[(x, y)].ToList()) DeleteObjectInstance(piece, announce: false);
            _wallCells.Remove((x, y));
            StatusText.Text = $"Wall taken away at ({x}, {y}).";
        }
        else
        {
            if (IsWallCell(x, y) || PickedWallKit is not { } kit) return;
            _wallCells[(x, y)] = new List<ObjectInstance>();
            StampWallCell(x, y, kit);
            StatusText.Text = $"Wall at ({x}, {y}).";
        }
        foreach (var (nx, ny) in new[] { (x, y - 1), (x + 1, y), (x, y + 1), (x - 1, y) })
        {
            int wx = WrapX(nx), wy = WrapY(ny);
            if (IsWallCell(wx, wy)) StampWallCell(wx, wy, null);
        }
    }

    /// <summary>A wall cell's pieces put down anew for its joins (its own kit; a new cell the picked one). A door or window put in stays.</summary>
    private void StampWallCell(int x, int y, WallKit? kit)
    {
        var here = _wallCells![(x, y)];
        if (here.Any(o => o.WallKitFixed)) return;
        kit ??= _objectDatabase.WallKits.FirstOrDefault(k => here.Count > 0 && k.Id == here[0].WallKitId) ?? PickedWallKit;
        if (kit is null) return;
        var pieces = kit.Resolve(WallJoinsAt(x, y));
        if (pieces.Count == 0) return;
        // Unchanged: leave them be (a stroke past a finished wall changes nothing in it).
        if (here.Count == pieces.Count && here.Zip(pieces).All(t => t.First.ObjectId == t.Second.ObjectId && t.First.Facing == t.Second.Facing
                && t.First.OffsetX == t.Second.OffsetX && t.First.OffsetY == t.Second.OffsetY && t.First.WallKitId == kit.Id)) return;
        foreach (var old in here.ToList()) DeleteObjectInstance(old, announce: false);
        here.Clear();
        foreach (var piece in pieces)
        {
            var instance = new ObjectInstance
            {
                ObjectId = piece.ObjectId, X = x, Y = y, Z = MapFloors.BaseLift(_currentFloor), Quantity = 1, ContainerInstanceId = -1,
                InstanceId = _map!.GetNextInstanceId(), Facing = piece.Facing, OffsetX = piece.OffsetX, OffsetY = piece.OffsetY, WallKitId = kit.Id
            };
            _map.Objects.Add(instance);
            here.Add(instance);
            PlaceObjectVisual(instance);
        }
    }

    /// <summary>Door / window tool: the picked door, window or wall set takes the place of a straight piece of drawn wall, facing the way the wall runs.</summary>
    private void PutDoorInWall(int x, int y)
    {
        if (_map is null) return;
        if (_wallDoorCombo?.SelectedItem is not WorldObject door) { RefreshWallDoorCombo(); door = (_wallDoorCombo?.SelectedItem as WorldObject)!; }
        if (door is null) { StatusText.Text = "Make a Door, a Window or a Wall built from a Wall Designer set first (Object Editor)."; return; }
        BeginWallStroke(erase: false);
        try
        {
            if (!IsWallCell(x, y)) { StatusText.Text = "Click a cell of drawn wall (Wall Plot) to put the door or window in."; return; }
            int joins = WallJoinsAt(x, y);
            bool ns = (joins & WallKit.WestEast) == 0, we = (joins & WallKit.NorthSouth) == 0;
            if (!ns && !we) { StatusText.Text = "A door or window goes in a straight piece of wall, not a corner or a join."; return; }
            var here = _wallCells![(x, y)];
            var first = here.FirstOrDefault();
            int kitId = first?.WallKitId ?? PickedWallKit?.Id ?? 1;
            foreach (var old in here.ToList()) DeleteObjectInstance(old, announce: false);
            here.Clear();
            var instance = new ObjectInstance
            {
                ObjectId = door.Id, X = x, Y = y, Z = MapFloors.BaseLift(_currentFloor), Quantity = 1, ContainerInstanceId = -1, InstanceId = _map.GetNextInstanceId(),
                Facing = we ? SpriteDirection.South : SpriteDirection.East, OffsetX = first?.OffsetX ?? 0, OffsetY = first?.OffsetY ?? 0, WallKitId = kitId, WallKitFixed = true
            };
            _map.Objects.Add(instance);
            here.Add(instance);
            RedrawAfterSettle(instance);
            StatusText.Text = $"Put \"{door.Name}\" in the wall at ({x}, {y}). The eraser (or the right button) takes it out with the wall.";
        }
        finally { EndWallStroke(); }
    }
}
