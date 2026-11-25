using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Tools > Wall Kits: which wall piece goes where when walls are drawn in the Map Editor (Mode > Wall Plot). A kit has
/// a piece for each of 16 shapes, picked by the walls a cell joins - the two straights, four corners, four T-joins,
/// the cross, four ends and a lone pillar. Each piece is any object (a Wall, or a Wall built from a Wall Designer
/// set), with its facing and its nudge on the cell set once, so every piece drawn sits against the next. A missing
/// piece is filled in from the straights. The sample plot shows the kit as the Map Editor will draw it.
/// </summary>
public class WallKitWindow : Window
{
    /// <summary>A sample plot with every shape in it: rooms with T-joins and a cross, a short north-south run, a pillar, a short west-east run.</summary>
    private static readonly string[] Sample =
    {
        "#########.#",
        "#...#...#.#",
        "#...#...#.#",
        "#########..",
        "#...#...#.#",
        "#...#...#..",
        "#########..",
        "...........",
        "#.###......"
    };
    private const int SampleTop = 4; // room above the plot for tall walls, in cells

    private readonly ObjectDatabase _objects;
    private readonly SpriteDatabase _sprites;
    private readonly int _tileW, _tileH;

    private readonly ListBox _kitList = new() { Margin = new Thickness(0, 4, 0, 4), DisplayMemberPath = nameof(WallKit.Name) };
    private readonly TextBox _nameBox = new() { Margin = new Thickness(0, 2, 0, 6) };
    private readonly UniformGrid _shapeGrid = new() { Columns = 4, Margin = new Thickness(0, 0, 0, 6) };
    private readonly Canvas _sample = new() { Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x1c, 0x1c)), ClipToBounds = true };
    private readonly ComboBox _zoomBox = new() { Width = 60, ItemsSource = new[] { 1, 2, 3 }, SelectedItem = 2 };
    private readonly StackPanel _pieceProps = new();
    private readonly CheckBox _everyObject = new() { Content = "Show every object (not only walls)", Margin = new Thickness(0, 4, 0, 4) };

    private int _shape = WallKit.WestEast;
    private bool _refreshing;

    private WallKit? Kit => _kitList.SelectedItem as WallKit;
    private int Zoom => _zoomBox.SelectedItem is int z ? z : 2;
    private int UnitsX => Math.Max(1, _tileW / SpriteSizing.BaseUnit);
    private int UnitsY => Math.Max(1, _tileH / SpriteSizing.BaseUnit);

    public WallKitWindow(ObjectDatabase objects, SpriteDatabase sprites, int tileWidth, int tileHeight, int selectKitId = -1)
    {
        _objects = objects;
        _sprites = sprites;
        _tileW = Math.Max(8, tileWidth);
        _tileH = Math.Max(8, tileHeight);
        Title = "Wall Kits";
        Width = 1280;
        Height = 820;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(8) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });

        // ---- Kits ----
        var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        var leftTop = new StackPanel();
        leftTop.Children.Add(new TextBlock { Text = "Wall kits", FontWeight = FontWeights.Bold });
        var kitButtons = new WrapPanel();
        kitButtons.Children.Add(MakeButton("New", NewKit));
        kitButtons.Children.Add(MakeButton("Duplicate", DuplicateKit));
        kitButtons.Children.Add(MakeButton("Delete", DeleteKit));
        leftTop.Children.Add(kitButtons);
        DockPanel.SetDock(leftTop, Dock.Top);
        left.Children.Add(leftTop);
        var leftBottom = new StackPanel();
        leftBottom.Children.Add(new TextBlock { Text = "Name" });
        leftBottom.Children.Add(_nameBox);
        leftBottom.Children.Add(Note("Draw with a kit in the Map Editor: Mode > Wall Plot, then the Wall Kits tab.\n\n" +
                                     "Make the pieces first in the Object Editor (type Wall): one per shape, drawn in your game's view. A wall with a window or a doorway in it: a Wall built from a Wall Designer set."));
        DockPanel.SetDock(leftBottom, Dock.Bottom);
        left.Children.Add(leftBottom);
        left.Children.Add(_kitList);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        // ---- Shapes and the sample plot ----
        var middle = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(new TextBlock { Text = "The 16 pieces - click one to set it", FontWeight = FontWeights.Bold });
        top.Children.Add(Note("The orange lines show which walls a piece joins. A piece left empty is filled in from the two straights (a corner or a cross: both straights on one cell; an end: its straight)."));
        top.Children.Add(_shapeGrid);
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        tools.Children.Add(new TextBlock { Text = "Sample plot, as the Map Editor draws it.  Zoom", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 4, 0) });
        tools.Children.Add(_zoomBox);
        tools.Children.Add(new TextBlock { Text = "  Click a wall cell to pick its piece; yellow = the picked piece's cells.", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7, FontSize = 11 });
        top.Children.Add(tools);
        DockPanel.SetDock(top, Dock.Top);
        middle.Children.Add(top);
        middle.Children.Add(new ScrollViewer { Content = _sample, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(middle, 1);
        root.Children.Add(middle);

        // ---- The picked piece ----
        var right = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        right.Children.Add(_pieceProps);
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(rightScroll, 2);
        root.Children.Add(rightScroll);

        Content = root;

        _kitList.ItemsSource = _objects.WallKits;
        _kitList.SelectionChanged += (_, _) => RefreshAll();
        _nameBox.TextChanged += (_, _) =>
        {
            if (_refreshing || Kit is not { } kit) return;
            kit.Name = _nameBox.Text;
            _kitList.Items.Refresh();
        };
        _zoomBox.SelectionChanged += (_, _) => DrawSample();
        _everyObject.Click += (_, _) => ShowPieceProps();
        _sample.MouseLeftButtonDown += Sample_MouseLeftButtonDown;
        PreviewKeyDown += Window_PreviewKeyDown;

        _kitList.SelectedItem = _objects.WallKits.FirstOrDefault(k => k.Id == selectKitId) ?? _objects.WallKits.FirstOrDefault();
        RefreshAll();
    }

    // ================= Kits =================

    private void NewKit()
    {
        var kit = new WallKit { Id = _objects.NextWallKitId(), Name = $"Wall Kit {_objects.WallKits.Count + 1}" };
        _objects.WallKits.Add(kit);
        _kitList.Items.Refresh();
        _kitList.SelectedItem = kit;
    }

    private void DuplicateKit()
    {
        if (Kit is not { } source) return;
        var copy = source.Clone();
        copy.Id = _objects.NextWallKitId();
        copy.Name = source.Name + " copy";
        _objects.WallKits.Add(copy);
        _kitList.Items.Refresh();
        _kitList.SelectedItem = copy;
    }

    private void DeleteKit()
    {
        if (Kit is not { } kit) return;
        if (MessageBox.Show(this, $"Delete \"{kit.Name}\"? Walls already drawn with it stay on the map as they are (ordinary objects); they just aren't fixed up any more when walls are drawn next to them.",
                "Wall Kits", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _objects.WallKits.Remove(kit);
        _kitList.Items.Refresh();
        _kitList.SelectedItem = _objects.WallKits.FirstOrDefault();
    }

    // ================= Pieces =================

    private WallKitPiece? PieceOf(int joins) => Kit?.Pieces.FirstOrDefault(p => p.Joins == joins);

    /// <summary>The picked shape's piece, made when it is first set.</summary>
    private WallKitPiece PieceToSet()
    {
        var kit = Kit!;
        if (PieceOf(_shape) is { } piece) return piece;
        piece = new WallKitPiece { Joins = _shape, Facing = WallKit.DefaultFacing(_shape) };
        kit.Pieces.Add(piece);
        return piece;
    }

    private void RefreshAll()
    {
        _refreshing = true;
        _nameBox.Text = Kit?.Name ?? "";
        _nameBox.IsEnabled = Kit is not null;
        _refreshing = false;
        BuildShapeGrid();
        ShowPieceProps();
        DrawSample();
    }

    private void BuildShapeGrid()
    {
        _shapeGrid.Children.Clear();
        foreach (int joins in WallKit.Shapes)
        {
            int j = joins;
            var piece = PieceOf(joins);
            var cell = new Grid { Width = 64, Height = 64 };
            cell.Children.Add(new Rectangle { Fill = new SolidColorBrush(Color.FromRgb(0x24, 0x24, 0x24)) });
            if (piece is { ObjectId: >= 0 } && PictureFor(piece.ObjectId, piece.Facing) is { } picture)
            {
                var thumb = new Image { Source = picture, Stretch = Stretch.Uniform, Margin = new Thickness(6) };
                RenderOptions.SetBitmapScalingMode(thumb, BitmapScalingMode.NearestNeighbor);
                cell.Children.Add(thumb);
            }
            var lines = new Canvas { IsHitTestVisible = false };
            void Line(double x1, double y1, double x2, double y2) =>
                lines.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = Brushes.Orange, StrokeThickness = 3, StrokeEndLineCap = PenLineCap.Round, StrokeStartLineCap = PenLineCap.Round });
            if ((joins & WallKit.North) != 0) Line(32, 32, 32, 2);
            if ((joins & WallKit.South) != 0) Line(32, 32, 32, 62);
            if ((joins & WallKit.East) != 0) Line(32, 32, 62, 32);
            if ((joins & WallKit.West) != 0) Line(32, 32, 2, 32);
            lines.Children.Add(At(new Ellipse { Width = 8, Height = 8, Fill = Brushes.Orange }, 28, 28));
            // A straight: an arrow the way its face looks (south / east, or - its other face - north / west).
            int bare = joins & ~WallKit.Reverse;
            if (bare is WallKit.WestEast or WallKit.NorthSouth)
            {
                bool back = (joins & WallKit.Reverse) != 0, across = bare == WallKit.WestEast;
                var (tx, ty, dx, dy) = across ? (32.0, back ? 20.0 : 44.0, 0.0, back ? -1.0 : 1.0) : (back ? 20.0 : 44.0, 32.0, back ? -1.0 : 1.0, 0.0);
                var arrow = new Polygon { Fill = Brushes.DeepSkyBlue, Points = new PointCollection
                {
                    new Point(tx + dx * 8, ty + dy * 8), new Point(tx - dy * 6, ty - dx * 6), new Point(tx + dy * 6, ty + dx * 6)
                } };
                lines.Children.Add(arrow);
            }
            cell.Children.Add(lines);
            bool filledIn = piece is not { ObjectId: >= 0 };
            var button = new ToggleButton
            {
                Content = new StackPanel { Children = { cell, new TextBlock { Text = filledIn ? ((joins & WallKit.Reverse) != 0 ? "(as the other face)" : "(filled in)") : NameOf(piece!.ObjectId), FontSize = 10, TextTrimming = TextTrimming.CharacterEllipsis, Width = 110, TextAlignment = TextAlignment.Center, Opacity = filledIn ? 0.6 : 1 } } },
                IsChecked = joins == _shape, Margin = new Thickness(2), Padding = new Thickness(2), ToolTip = WallKit.ShapeName(joins), IsEnabled = Kit is not null
            };
            button.Click += (_, _) => { _shape = j; RefreshAll(); };
            _shapeGrid.Children.Add(button);
        }
    }

    private sealed record ObjectChoice(int Id, string Name)
    {
        public override string ToString() => Name;
    }

    private void ShowPieceProps()
    {
        _pieceProps.Children.Clear();
        if (Kit is not { } kit)
        {
            _pieceProps.Children.Add(Note("New (left) makes a wall kit."));
            return;
        }
        var piece = PieceOf(_shape);
        _pieceProps.Children.Add(new TextBlock { Text = WallKit.ShapeName(_shape), FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap });

        _pieceProps.Children.Add(new TextBlock { Text = "Object", Margin = new Thickness(0, 8, 0, 0) });
        var choices = new List<ObjectChoice> { new(-1, "(none - filled in from the straights)") };
        choices.AddRange(_objects.Objects.Where(o => _everyObject.IsChecked == true || o.Type == ObjectType.Wall || o.Id == piece?.ObjectId)
            .OrderBy(o => o.Name).Select(o => new ObjectChoice(o.Id, o.Type == ObjectType.Wall && o.Wall.DesignId >= 0 ? o.Name + " (wall set)" : o.Name)));
        var objectBox = new ComboBox { ItemsSource = choices, SelectedItem = choices.FirstOrDefault(c => c.Id == (piece?.ObjectId ?? -1)) ?? choices[0] };
        objectBox.SelectionChanged += (_, _) =>
        {
            if (objectBox.SelectedItem is not ObjectChoice choice) return;
            if (choice.Id < 0) { if (PieceOf(_shape) is { } p) p.ObjectId = -1; }
            else PieceToSet().ObjectId = choice.Id;
            RefreshAll();
        };
        _pieceProps.Children.Add(objectBox);
        _pieceProps.Children.Add(_everyObject);

        _pieceProps.Children.Add(new TextBlock { Text = "Facing", Margin = new Thickness(0, 6, 0, 0) });
        var facings = new[] { SpriteDirection.North, SpriteDirection.East, SpriteDirection.South, SpriteDirection.West };
        var facingBox = new ComboBox { ItemsSource = facings, SelectedItem = piece?.Facing ?? WallKit.DefaultFacing(_shape), IsEnabled = piece is { ObjectId: >= 0 } };
        facingBox.SelectionChanged += (_, _) => { if (facingBox.SelectedItem is SpriteDirection f) { PieceToSet().Facing = f; RefreshAll(); } };
        _pieceProps.Children.Add(facingBox);
        _pieceProps.Children.Add(Note("Which of the object's pictures it shows (Object Editor > its sprites per facing). A wall set: south = its west-east way, east = its north-south way."));

        _pieceProps.Children.Add(new TextBlock { Text = "Nudge on its cell (8px a step)", Margin = new Thickness(0, 6, 0, 0) });
        var nudge = new WrapPanel();
        foreach (var (text, dx, dy) in new[] { ("Left", -1, 0), ("Right", 1, 0), ("Up", 0, -1), ("Down", 0, 1), ("Centre", 0, 0) })
        {
            int ddx = dx, ddy = dy;
            bool centre = text == "Centre";
            var b = MakeButton(text, () => Nudge(ddx, ddy, centre));
            b.IsEnabled = piece is { ObjectId: >= 0 };
            nudge.Children.Add(b);
        }
        _pieceProps.Children.Add(nudge);
        _pieceProps.Children.Add(new TextBlock { Text = piece is null ? "" : $"Now: {piece.OffsetX * SpriteSizing.BaseUnit} px across, {piece.OffsetY * SpriteSizing.BaseUnit} px down", Opacity = 0.8 });
        _pieceProps.Children.Add(Note("The arrow keys nudge too. Set it once here: every piece of this shape drawn on the map gets it, so the pieces always meet."));

        _pieceProps.Children.Add(MakeButton("Copy the nudge to every piece", () =>
        {
            if (PieceOf(_shape) is not { } p) return;
            foreach (var other in kit.Pieces) { other.OffsetX = p.OffsetX; other.OffsetY = p.OffsetY; }
            RefreshAll();
        }));

        int set = kit.Pieces.Count(p => p.ObjectId >= 0);
        _pieceProps.Children.Add(Note($"\n{set} of {WallKit.Shapes.Length} pieces set. " + (kit.PieceFor(WallKit.WestEast) is null || kit.PieceFor(WallKit.NorthSouth) is null
            ? "Set both straights first: every piece you leave empty is made from them."
            : "The empty ones are made from the straights - set them for the best look (a real corner, a T, a cross).")));
        _pieceProps.Children.Add(Note("Ultima 6: square pieces seen from above. Ultima 7 and 8: each piece drawn in the game's own view - the kit only says which piece goes where."));
    }

    private void Nudge(int dx, int dy, bool centre)
    {
        if (Kit is null || PieceOf(_shape) is not { ObjectId: >= 0 } piece) return;
        if (centre) { piece.OffsetX = 0; piece.OffsetY = 0; }
        else
        {
            piece.OffsetX = Math.Clamp(piece.OffsetX + dx, -UnitsX, UnitsX);
            piece.OffsetY = Math.Clamp(piece.OffsetY + dy, -UnitsY, UnitsY);
        }
        ShowPieceProps();
        DrawSample();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        (int dx, int dy) = e.Key switch { Key.Left => (-1, 0), Key.Right => (1, 0), Key.Up => (0, -1), Key.Down => (0, 1), _ => (0, 0) };
        if (dx == 0 && dy == 0) return;
        Nudge(dx, dy, false);
        e.Handled = true;
    }

    // ================= The sample plot =================

    private static bool SampleWall(int x, int y) => y >= 0 && y < Sample.Length && x >= 0 && x < Sample[y].Length && Sample[y][x] == '#';

    private static int SampleJoins(int x, int y) =>
        (SampleWall(x, y - 1) ? WallKit.North : 0) | (SampleWall(x + 1, y) ? WallKit.East : 0) | (SampleWall(x, y + 1) ? WallKit.South : 0) | (SampleWall(x - 1, y) ? WallKit.West : 0);

    private void DrawSample()
    {
        _sample.Children.Clear();
        int cols = Sample.Max(r => r.Length), rows = Sample.Length;
        double width = (cols + 2) * _tileW, height = (rows + SampleTop + 1) * _tileH;
        _sample.Width = width * Zoom;
        _sample.Height = height * Zoom;
        var layer = new Canvas { Width = width, Height = height, LayoutTransform = new ScaleTransform(Zoom, Zoom) };
        _sample.Children.Add(layer);
        double ox = _tileW, oy = SampleTop * _tileH;
        // The grid, and the cells of the picked shape.
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                bool wall = SampleWall(x, y), picked = wall && SampleJoins(x, y) == _shape;
                layer.Children.Add(At(new Rectangle
                {
                    Width = _tileW, Height = _tileH, Stroke = picked ? Brushes.Yellow : new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                    StrokeThickness = picked ? 1.5 : 0.5, Fill = wall ? new SolidColorBrush(Color.FromArgb(30, 255, 160, 0)) : null
                }, ox + x * _tileW, oy + y * _tileH));
            }
        if (Kit is not { } kit) return;
        // The pieces, back to front (row by row, as the map draws them).
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
            {
                if (!SampleWall(x, y)) continue;
                foreach (var piece in kit.Resolve(SampleJoins(x, y)))
                {
                    if (PictureFor(piece.ObjectId, piece.Facing) is not { } picture) continue;
                    var (w, h) = SpriteSizing.Quantize(picture.PixelWidth, picture.PixelHeight);
                    var image = new Image { Source = picture, Width = w, Height = h, Stretch = Stretch.Uniform, IsHitTestVisible = false };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                    Canvas.SetLeft(image, ox + x * _tileW + (_tileW - w) / 2.0 + piece.OffsetX * SpriteSizing.BaseUnit);
                    Canvas.SetTop(image, oy + (y + 1) * _tileH - h + piece.OffsetY * SpriteSizing.BaseUnit);
                    layer.Children.Add(image);
                }
            }
        if (!kit.HasAnyPiece)
            layer.Children.Add(At(new TextBlock { Text = "Pick an object for the straights (above) - the plot is drawn with them.", Foreground = Brushes.LightGray, FontSize = 11 }, ox, 4));
    }

    private void Sample_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var p = e.GetPosition(_sample);
        int x = (int)Math.Floor(p.X / Zoom / _tileW) - 1, y = (int)Math.Floor(p.Y / Zoom / _tileH) - SampleTop;
        if (!SampleWall(x, y)) return;
        _shape = SampleJoins(x, y);
        RefreshAll();
    }

    // ================= Pictures =================

    /// <summary>An object's picture for a facing, as the Map Editor draws it placed: its sprite's first frame; a wall set: all its pieces.</summary>
    private BitmapSource? PictureFor(int objectId, SpriteDirection facing)
    {
        if (_objects.Objects.FirstOrDefault(o => o.Id == objectId) is not { } obj) return null;
        if (WallLook.PoseOf(obj, _objects, facing) is { } pose) return WallLook.Bitmap(pose, _objects, _sprites, _tileW, _tileH);
        var sprite = ObjectLooks.SpriteFor(obj, ObjectLooks.DefaultState(obj) with { Facing = facing }, _sprites);
        if (sprite is null) return null;
        string path = sprite.Frames.Count > 0 ? sprite.Frames[0].FilePath : sprite.FilePath;
        return string.IsNullOrEmpty(path) ? null : FrameCache.Get(path);
    }

    private static T At<T>(T element, double left, double top) where T : UIElement
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        return element;
    }

    private string NameOf(int objectId) => _objects.Objects.FirstOrDefault(o => o.Id == objectId)?.Name ?? "(deleted)";

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 4) };

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 2, 4, 2) };
        button.Click += (_, _) => click();
        return button;
    }
}
