using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Wall Designer: walls with windows and doorways, glued together once.
/// A set is pieces - any objects: a half wall, narrow side pieces, the piece
/// over the window, a Window, a Door - each standing at its own spot and
/// height (as in Ultima VII, the window is its own shape standing at a lift
/// between the wall pieces). A Wall object links to a set (Object Editor) and
/// is placed on the map as one wall; in play every piece is its own object.
/// A set has its pieces for a wall running west-east and one running
/// north-south, picked per placed copy in the Map Editor.
/// </summary>
public class WallDesignerWindow : Window
{
    private const int AreaTiles = 12; // the drawing area: 12 x 12 cells, the anchor cell at the bottom middle
    private const int LiftPixels = 4;
    private const int MaxLift = 4; // a piece starts within its storey (5 lifts a floor)
    private static readonly WallRun[] Runs = { WallRun.WestEast, WallRun.NorthSouth };

    private readonly ObjectDatabase _objects;
    private readonly SpriteDatabase _sprites;
    private readonly int _tileW, _tileH;

    private readonly ListBox _designList = new() { Margin = new Thickness(0, 4, 0, 4), DisplayMemberPath = nameof(WallDesign.Name) };
    private readonly TextBox _nameBox = new() { Margin = new Thickness(0, 2, 0, 6) };
    private readonly StackPanel _runBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x1c, 0x1c)), ClipToBounds = true, Focusable = true };
    private readonly ListBox _pieceList = new() { Height = 170, Margin = new Thickness(0, 4, 0, 4) };
    private readonly StackPanel _pieceProps = new();
    private readonly TextBlock _check = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _zoomBox = new() { Width = 70, ItemsSource = new[] { 1, 2, 3, 4 }, SelectedItem = 3 };
    private readonly CheckBox _showFeet = new() { Content = "Show footprints", IsChecked = true, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    private WallRun _run = WallRun.WestEast;
    private int _selected = -1;
    private bool _refreshing;
    private (Point Start, int X, int Y)? _drag;

    private int Zoom => _zoomBox.SelectedItem is int z ? z : 3;
    private double AnchorLeft => (AreaTiles / 2) * _tileW;
    private double AnchorTop => (AreaTiles - 3) * _tileH;
    private WallDesign? Design => _designList.SelectedItem as WallDesign;
    private WallPose? Pose => Design?.Poses.FirstOrDefault(p => p.Run == _run);
    private WallPiece? Piece => Pose is { } pose && _selected >= 0 && _selected < pose.Pieces.Count ? pose.Pieces[_selected] : null;

    public int SelectedDesignId => Design?.Id ?? -1;

    public WallDesignerWindow(ObjectDatabase objects, SpriteDatabase sprites, int tileWidth, int tileHeight, int selectDesignId = -1)
    {
        _objects = objects;
        _sprites = sprites;
        _tileW = Math.Max(8, tileWidth);
        _tileH = Math.Max(8, tileHeight);
        Title = "Wall Designer";
        Width = 1260;
        Height = 800;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(8) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });

        // ---- Sets ----
        var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        var leftTop = new StackPanel();
        leftTop.Children.Add(new TextBlock { Text = "Wall sets", FontWeight = FontWeights.Bold });
        var designButtons = new WrapPanel();
        designButtons.Children.Add(MakeButton("New", NewDesign));
        designButtons.Children.Add(MakeButton("Duplicate", DuplicateDesign));
        designButtons.Children.Add(MakeButton("Delete", DeleteDesign));
        leftTop.Children.Add(designButtons);
        DockPanel.SetDock(leftTop, Dock.Top);
        left.Children.Add(leftTop);
        var leftBottom = new StackPanel();
        leftBottom.Children.Add(new TextBlock { Text = "Name" });
        leftBottom.Children.Add(_nameBox);
        leftBottom.Children.Add(Note("A Wall object uses a set: Object Editor > type Wall > Built from. Placed on the map it is one wall; in play each piece is its own object - the window a window, the door a door.\n\n" +
                                     "Make the pieces first in the Object Editor: a half wall is a Wall with a lower Height (Size Z), the side and top pieces are Walls with their own sprites and sizes, the window a Window, the door a Door."));
        DockPanel.SetDock(leftBottom, Dock.Bottom);
        left.Children.Add(leftBottom);
        left.Children.Add(_designList);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        // ---- Drawing ----
        var middle = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(_runBar);
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        tools.Children.Add(new TextBlock { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        tools.Children.Add(_zoomBox);
        tools.Children.Add(_showFeet);
        top.Children.Add(tools);
        top.Children.Add(Note("Click a piece to pick it; drag it to move it (8px at a time - the world's unit). Arrows: 1 unit. Page Up / Page Down: one lift higher / lower (4px up-left a lift, as in the game). Delete: remove it. " +
                              "The yellow square is the cell the set is placed on; nothing may reach below its red line (build away from it: back and to the sides). Cyan: the picked piece's footprint - dashed on the ground, solid at its height."));
        DockPanel.SetDock(top, Dock.Top);
        middle.Children.Add(top);
        DockPanel.SetDock(_check, Dock.Bottom);
        middle.Children.Add(_check);
        middle.Children.Add(new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(middle, 1);
        root.Children.Add(middle);

        // ---- Pieces ----
        var right = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        right.Children.Add(new TextBlock { Text = "Pieces of this way", FontWeight = FontWeights.Bold });
        var pieceButtons = new WrapPanel();
        pieceButtons.Children.Add(MakeButton("Add piece", AddPiece));
        pieceButtons.Children.Add(MakeButton("Duplicate", DuplicatePiece));
        pieceButtons.Children.Add(MakeButton("Delete", DeletePiece));
        right.Children.Add(pieceButtons);
        right.Children.Add(_pieceList);
        right.Children.Add(_pieceProps);
        right.Children.Add(Note("How to build a wall with a window:\n" +
                                "1. Add piece: the half wall (lift 0).\n" +
                                "2. Add piece: the Window, then \"Stand it on what's under it\" - it goes on top of the half wall.\n" +
                                "3. Add pieces: the side pieces left and right of the window (lift 0, full height), moved beside it.\n" +
                                "4. Add piece: the piece over the window, \"Stand it on what's under it\".\n" +
                                "A doorway: the Door at lift 0, side pieces, the piece over the door on top.\n" +
                                "Then the other way (Runs north-south): \"Copy from the other way, turned\" and swap in that way's pictures."));
        Grid.SetColumn(rightScroll, 2);
        root.Children.Add(rightScroll);

        Content = root;

        foreach (var run in Runs)
        {
            var r = run;
            var button = new ToggleButton { Content = WallDesign.RunName(run), Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 0, 4, 0), Tag = run };
            button.Click += (_, _) => { _run = r; _selected = 0; RefreshAll(); };
            _runBar.Children.Add(button);
        }
        _runBar.Children.Add(MakeButton("Copy from the other way, turned", CopyFromOtherWay));

        _designList.ItemsSource = _objects.WallDesigns;
        _designList.SelectionChanged += (_, _) => { _selected = 0; _run = Design?.Poses.FirstOrDefault(p => p.Pieces.Count > 0)?.Run ?? WallRun.WestEast; RefreshAll(); };
        _nameBox.TextChanged += (_, _) =>
        {
            if (_refreshing || Design is not { } design) return;
            design.Name = _nameBox.Text;
            _designList.Items.Refresh();
        };
        _pieceList.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            _selected = _pieceList.SelectedIndex;
            ShowPieceProps();
            Redraw();
        };
        _zoomBox.SelectionChanged += (_, _) => Redraw();
        _showFeet.Click += (_, _) => Redraw();
        _canvas.MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += (_, _) => { _drag = null; _canvas.ReleaseMouseCapture(); RefreshPieceList(); ShowPieceProps(); };
        PreviewKeyDown += Window_PreviewKeyDown;

        _designList.SelectedItem = _objects.WallDesigns.FirstOrDefault(d => d.Id == selectDesignId) ?? _objects.WallDesigns.FirstOrDefault();
        RefreshAll();
    }

    // ================= Sets =================

    private void NewDesign()
    {
        var design = new WallDesign { Id = _objects.NextWallDesignId(), Name = $"Wall {_objects.WallDesigns.Count + 1}" };
        design.Poses.Add(new WallPose { Run = WallRun.WestEast });
        _objects.WallDesigns.Add(design);
        _designList.Items.Refresh();
        _designList.SelectedItem = design;
        _check.Text = "New wall set: Add piece (right) for each part - the half wall first, then the window on it, the sides, the top.";
    }

    private void DuplicateDesign()
    {
        if (Design is not { } source) return;
        var copy = new WallDesign { Id = _objects.NextWallDesignId(), Name = source.Name + " copy" };
        foreach (var pose in source.Poses) copy.Poses.Add(new WallPose { Run = pose.Run, Pieces = pose.Pieces.Select(p => p.Clone()).ToList() });
        _objects.WallDesigns.Add(copy);
        _designList.Items.Refresh();
        _designList.SelectedItem = copy;
    }

    private void DeleteDesign()
    {
        if (Design is not { } design) return;
        var users = _objects.Objects.Where(o => o.Type == ObjectType.Wall && o.Wall.DesignId == design.Id).Select(o => o.Name).ToList();
        string question = users.Count == 0 ? $"Delete \"{design.Name}\"?"
            : $"Delete \"{design.Name}\"? It is used by: {string.Join(", ", users)} - they go back to plain walls (their own sprite).";
        if (MessageBox.Show(this, question, "Wall Designer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var obj in _objects.Objects.Where(o => o.Type == ObjectType.Wall && o.Wall.DesignId == design.Id)) obj.Wall.DesignId = -1;
        _objects.WallDesigns.Remove(design);
        _designList.Items.Refresh();
        _designList.SelectedItem = _objects.WallDesigns.FirstOrDefault();
    }

    private WallPose EnsurePose()
    {
        var design = Design!;
        var pose = design.Poses.FirstOrDefault(p => p.Run == _run);
        if (pose is null) { pose = new WallPose { Run = _run }; design.Poses.Add(pose); }
        return pose;
    }

    /// <summary>The other way's pieces, turned: across becomes along (offsets swapped). Swap in this way's own pictures (pieces) afterwards.</summary>
    private void CopyFromOtherWay()
    {
        if (Design is not { } design) return;
        var other = _run == WallRun.WestEast ? WallRun.NorthSouth : WallRun.WestEast;
        var source = design.Poses.FirstOrDefault(p => p.Run == other);
        if (source is null || source.Pieces.Count == 0) { _check.Text = $"\"{WallDesign.RunName(other)}\" has no pieces to copy."; return; }
        var pose = EnsurePose();
        if (pose.Pieces.Count > 0 && MessageBox.Show(this, $"Replace this way's {pose.Pieces.Count} pieces with the other way's, turned?", "Wall Designer", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        pose.Pieces = source.Pieces.Select(p => { var c = p.Clone(); (c.OffsetX, c.OffsetY) = (p.OffsetY, p.OffsetX); return c; }).ToList();
        _selected = 0;
        RefreshAll();
        _check.Text = "Copied and turned. Now pick each piece and change its Object to this way's version (a north-south wall has its own pictures).";
    }

    // ================= Pieces =================

    private void AddPiece()
    {
        if (Design is null) { NewDesign(); if (Design is null) return; }
        var pose = EnsurePose();
        // A new piece: the object of the picked one (or the first wall), beside it.
        var from = Piece;
        int objectId = from?.ObjectId ?? ObjectChoices().FirstOrDefault(o => o.Id >= 0 && o.Type == ObjectType.Wall)?.Id ?? -1;
        var piece = new WallPiece { ObjectId = objectId, OffsetX = from is null ? 0 : from.OffsetX + Math.Max(1, _tileW / SpriteSizing.BaseUnit), OffsetY = from?.OffsetY ?? 0 };
        pose.Pieces.Add(piece);
        _selected = pose.Pieces.Count - 1;
        RefreshAll();
    }

    private void DuplicatePiece()
    {
        if (Piece is not { } piece) return;
        var copy = piece.Clone();
        copy.OffsetX += 1;
        Pose!.Pieces.Add(copy);
        _selected = Pose.Pieces.Count - 1;
        RefreshAll();
    }

    private void DeletePiece()
    {
        if (Piece is not { } piece) return;
        Pose!.Pieces.Remove(piece);
        _selected = Math.Min(_selected, Pose.Pieces.Count - 1);
        RefreshAll();
    }

    private WorldObject? ObjectOf(WallPiece piece) => _objects.Objects.FirstOrDefault(o => o.Id == piece.ObjectId);

    /// <summary>The lifts a piece takes up: from its lift to its lift + its object's height.</summary>
    private (int Bottom, int Top) LiftsOf(WallPiece piece) => (piece.Lift, piece.Lift + Math.Max(1, ObjectOf(piece)?.SizeZ ?? 1));

    /// <summary>Stand the picked piece on the highest piece under its footprint (the window on the half wall, the top piece on the window).</summary>
    private void StandOnWhatsUnder()
    {
        if (Pose is not { } pose || Piece is not { } piece) return;
        var foot = WallLook.FootRect(piece, _objects, _tileW, _tileH);
        int lift = pose.Pieces.Where(p => p != piece && WallLook.FootRect(p, _objects, _tileW, _tileH).IntersectsWith(foot) && Overlaps(WallLook.FootRect(p, _objects, _tileW, _tileH), foot))
            .Select(p => LiftsOf(p).Top).DefaultIfEmpty(0).Max();
        piece.Lift = Math.Clamp(lift, 0, MaxLift);
        KeepAboveBaseline(piece);
        _check.Text = lift > MaxLift ? $"What's under it is {lift} lifts high - a piece starts within its storey (0-{MaxLift}); it stands at {MaxLift}." : $"It stands at lift {piece.Lift}.";
        RefreshPieceList();
        ShowPieceProps();
        Redraw();
    }

    /// <summary>Nothing reaches below the set's cell (red line) - neither its picture nor where it stands: the set is anchored there, as every sprite is.</summary>
    private void KeepAboveBaseline(WallPiece piece)
    {
        while (piece.OffsetY > -64 && (WallLook.PictureRect(piece, _objects, _sprites, _tileW, _tileH).Bottom > _tileH + 0.5 || WallLook.FootRect(piece, _objects, _tileW, _tileH).Bottom > _tileH + 0.5))
            piece.OffsetY--;
    }

    private static bool Overlaps(Rect a, Rect b) => a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    private sealed record ObjectChoice(int Id, string Display, ObjectType Type)
    {
        public override string ToString() => Display;
    }

    /// <summary>Every object, the wall-like ones (walls, windows, doors) first.</summary>
    private List<ObjectChoice> ObjectChoices()
    {
        var choices = new List<ObjectChoice> { new(-1, "(none)", ObjectType.Wall) };
        choices.AddRange(_objects.Objects
            .OrderBy(o => o.Type switch { ObjectType.Wall => 0, ObjectType.Window => 1, ObjectType.Door => 2, _ => 3 })
            .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
            .Select(o => new ObjectChoice(o.Id, $"{o.Name}   ({ObjectTypes.Name(o.Type)})", o.Type)));
        return choices;
    }

    // ================= Showing =================

    private void RefreshAll()
    {
        _refreshing = true;
        _nameBox.Text = Design?.Name ?? string.Empty;
        _nameBox.IsEnabled = Design is not null;
        foreach (var button in _runBar.Children.OfType<ToggleButton>())
        {
            var run = (WallRun)button.Tag;
            button.IsChecked = run == _run;
            bool has = Design?.Poses.Any(p => p.Run == run && p.Pieces.Count > 0) == true;
            button.Content = WallDesign.RunName(run) + (has ? "" : " (empty)");
        }
        _refreshing = false;
        RefreshPieceList();
        ShowPieceProps();
        Redraw();
    }

    private void RefreshPieceList()
    {
        _refreshing = true;
        var pose = Pose;
        _pieceList.Items.Clear();
        if (pose is not null)
            for (int i = 0; i < pose.Pieces.Count; i++)
            {
                var piece = pose.Pieces[i];
                var obj = ObjectOf(piece);
                _pieceList.Items.Add($"{i + 1}. {obj?.Name ?? "(no object)"}{(obj is null ? "" : $" - {ObjectTypes.Name(obj.Type)}")}   at ({piece.OffsetX}, {piece.OffsetY}), lift {piece.Lift}");
            }
        _pieceList.SelectedIndex = pose is not null && _selected < pose.Pieces.Count ? _selected : -1;
        _refreshing = false;
        UpdateCheck();
    }

    private void ShowPieceProps()
    {
        _pieceProps.Children.Clear();
        if (Piece is not { } piece) return;
        var choices = ObjectChoices();
        var combo = new ComboBox { Width = 300, ItemsSource = choices, IsEditable = false };
        combo.SelectedItem = choices.FirstOrDefault(c => c.Id == piece.ObjectId) ?? choices[0];
        combo.SelectionChanged += (_, _) => { piece.ObjectId = (combo.SelectedItem as ObjectChoice)?.Id ?? -1; KeepAboveBaseline(piece); RefreshPieceList(); Redraw(); };
        _pieceProps.Children.Add(Labeled("Object (a wall piece, a Window, a Door - anything)", combo));
        var row = new WrapPanel();
        row.Children.Add(IntField("Across (units)", () => piece.OffsetX, v => piece.OffsetX = v));
        row.Children.Add(IntField("Front-back (units)", () => piece.OffsetY, v => piece.OffsetY = v));
        row.Children.Add(IntField($"Lift (0-{MaxLift})", () => piece.Lift, v => piece.Lift = Math.Clamp(v, 0, MaxLift)));
        _pieceProps.Children.Add(row);
        _pieceProps.Children.Add(MakeButton("Stand it on what's under it", StandOnWhatsUnder));
        if (ObjectOf(piece) is { } obj)
            _pieceProps.Children.Add(Note($"\"{obj.Name}\": footprint {obj.SizeX} x {obj.SizeY} units, {obj.SizeZ} lifts high - so it reaches lift {piece.Lift + Math.Max(1, obj.SizeZ)}. " +
                                          (obj.Type switch
                                          {
                                              ObjectType.Window => "In play: a window - its shutters open and close, its glass breaks, you see through it.",
                                              ObjectType.Door => "In play: a door - it opens, closes and locks (each placed set's starting state: Map Editor, right-click).",
                                              ObjectType.Wall => "In play: a wall - solid, it hides what's behind it.",
                                              _ => "In play: this object as it always is."
                                          })));
    }

    /// <summary>What's wrong with this way's pieces, if anything.</summary>
    private void UpdateCheck()
    {
        if (Pose is not { Pieces.Count: > 0 } pose) { _check.Text = Design is null ? "Make a set: New (left)." : "No pieces this way yet: Add piece (right)."; return; }
        var problems = new List<string>();
        for (int i = 0; i < pose.Pieces.Count; i++)
        {
            var a = pose.Pieces[i];
            if (ObjectOf(a) is null) { problems.Add($"Piece {i + 1} has no object."); continue; }
            for (int j = i + 1; j < pose.Pieces.Count; j++)
            {
                var b = pose.Pieces[j];
                if (ObjectOf(b) is null) continue;
                var (aBottom, aTop) = LiftsOf(a);
                var (bBottom, bTop) = LiftsOf(b);
                if (aBottom < bTop && bBottom < aTop && Overlaps(WallLook.FootRect(a, _objects, _tileW, _tileH), WallLook.FootRect(b, _objects, _tileW, _tileH)))
                    problems.Add($"Pieces {i + 1} and {j + 1} are inside each other (same spot, same lifts) - move one, or stand it on the other.");
            }
            if (LiftsOf(a).Top > MapFloors.LiftsPerFloor + 1) problems.Add($"Piece {i + 1} reaches lift {LiftsOf(a).Top} - higher than a storey ({MapFloors.LiftsPerFloor}).");
        }
        _check.Foreground = problems.Count == 0 ? Brushes.LightGreen : Brushes.Orange;
        _check.Text = problems.Count == 0 ? $"{pose.Pieces.Count} pieces, nothing inside anything else." : string.Join("\n", problems);
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        double z = Zoom;
        _canvas.Width = AreaTiles * _tileW * z;
        _canvas.Height = AreaTiles * _tileH * z;
        for (int c = 0; c <= AreaTiles; c++)
            _canvas.Children.Add(new Line { X1 = c * _tileW * z, Y1 = 0, X2 = c * _tileW * z, Y2 = _canvas.Height, Stroke = Brushes.DimGray, StrokeThickness = 1, Opacity = 0.5 });
        for (int r = 0; r <= AreaTiles; r++)
            _canvas.Children.Add(new Line { X1 = 0, Y1 = r * _tileH * z, X2 = _canvas.Width, Y2 = r * _tileH * z, Stroke = Brushes.DimGray, StrokeThickness = 1, Opacity = 0.5 });
        var anchor = new Rectangle { Width = _tileW * z, Height = _tileH * z, Stroke = Brushes.Gold, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 } };
        Canvas.SetLeft(anchor, AnchorLeft * z);
        Canvas.SetTop(anchor, AnchorTop * z);
        _canvas.Children.Add(anchor);
        _canvas.Children.Add(new Line { X1 = 0, Y1 = (AnchorTop + _tileH) * z, X2 = _canvas.Width, Y2 = (AnchorTop + _tileH) * z, Stroke = Brushes.IndianRed, StrokeThickness = 1.5 });
        if (Pose is not { } pose) return;
        foreach (var (piece, index) in WallLook.PaintOrder(pose, _objects, _tileW, _tileH))
        {
            var rect = WallLook.PictureRect(piece, _objects, _sprites, _tileW, _tileH);
            FrameworkElement picture = WallLook.PictureOf(piece, _objects, _sprites) is { } bitmap
                ? new Image { Source = bitmap, Width = rect.Width * z, Height = rect.Height * z, Stretch = Stretch.Fill }
                : new Border { Width = rect.Width * z, Height = rect.Height * z, Background = new SolidColorBrush(Color.FromArgb(120, 120, 90, 60)), BorderBrush = Brushes.Tan, BorderThickness = new Thickness(1), Child = new TextBlock { Text = $"{index + 1}", Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
            picture.Tag = index;
            Canvas.SetLeft(picture, (AnchorLeft + rect.Left) * z);
            Canvas.SetTop(picture, (AnchorTop + rect.Top) * z);
            _canvas.Children.Add(picture);
        }
        for (int i = 0; i < pose.Pieces.Count; i++)
        {
            var piece = pose.Pieces[i];
            bool picked = i == _selected;
            if (!picked && _showFeet.IsChecked != true) continue;
            var foot = WallLook.FootRect(piece, _objects, _tileW, _tileH);
            double lift = piece.Lift * LiftPixels;
            var atHeight = new Rectangle
            {
                Width = foot.Width * z, Height = foot.Height * z,
                Stroke = picked ? Brushes.Cyan : new SolidColorBrush(Color.FromArgb(110, 0, 200, 220)), StrokeThickness = picked ? 2 : 1,
                Fill = picked ? new SolidColorBrush(Color.FromArgb(50, 0, 220, 255)) : null, IsHitTestVisible = false
            };
            Canvas.SetLeft(atHeight, (AnchorLeft + foot.Left - lift) * z);
            Canvas.SetTop(atHeight, (AnchorTop + foot.Top - lift) * z);
            _canvas.Children.Add(atHeight);
            var label = new TextBlock { Text = (i + 1).ToString(), Foreground = picked ? Brushes.Cyan : Brushes.LightBlue, FontSize = 10, IsHitTestVisible = false };
            Canvas.SetLeft(label, (AnchorLeft + foot.Left - lift) * z + 2);
            Canvas.SetTop(label, (AnchorTop + foot.Top - lift) * z);
            _canvas.Children.Add(label);
            if (!picked) continue;
            var ground = new Rectangle { Width = foot.Width * z, Height = foot.Height * z, Stroke = Brushes.Cyan, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 2 }, Opacity = 0.6, IsHitTestVisible = false };
            Canvas.SetLeft(ground, (AnchorLeft + foot.Left) * z);
            Canvas.SetTop(ground, (AnchorTop + foot.Top) * z);
            _canvas.Children.Add(ground);
            var rect = WallLook.PictureRect(piece, _objects, _sprites, _tileW, _tileH);
            var outline = new Rectangle { Width = rect.Width * z, Height = rect.Height * z, Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 2 }, IsHitTestVisible = false };
            Canvas.SetLeft(outline, (AnchorLeft + rect.Left) * z);
            Canvas.SetTop(outline, (AnchorTop + rect.Top) * z);
            _canvas.Children.Add(outline);
        }
    }

    // ================= Mouse and keys =================

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _canvas.Focus();
        if (Pose is not { } pose) return;
        var p = e.GetPosition(_canvas);
        // The front-most picture under the mouse.
        int hit = _canvas.Children.OfType<FrameworkElement>().Reverse()
            .Where(c => c.Tag is int && new Rect(Canvas.GetLeft(c), Canvas.GetTop(c), c.Width, c.Height).Contains(p))
            .Select(c => (int)c.Tag).DefaultIfEmpty(-1).First();
        if (hit < 0) return;
        _selected = hit;
        var piece = pose.Pieces[hit];
        _drag = (p, piece.OffsetX, piece.OffsetY);
        _canvas.CaptureMouse();
        RefreshPieceList();
        ShowPieceProps();
        Redraw();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag is not { } drag || Piece is not { } piece || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_canvas);
        piece.OffsetX = drag.X + (int)Math.Round((p.X - drag.Start.X) / Zoom / SpriteSizing.BaseUnit);
        piece.OffsetY = drag.Y + (int)Math.Round((p.Y - drag.Start.Y) / Zoom / SpriteSizing.BaseUnit);
        KeepAboveBaseline(piece);
        Redraw();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox or ComboBox) return;
        if (Piece is not { } piece) return;
        int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0, dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
        if (dx != 0 || dy != 0) { piece.OffsetX += dx; piece.OffsetY += dy; }
        else if (e.Key == Key.PageUp) piece.Lift = Math.Min(MaxLift, piece.Lift + 1);
        else if (e.Key == Key.PageDown) piece.Lift = Math.Max(0, piece.Lift - 1);
        else if (e.Key == Key.Delete) { DeletePiece(); e.Handled = true; return; }
        else return;
        KeepAboveBaseline(piece);
        e.Handled = true;
        RefreshPieceList();
        ShowPieceProps();
        Redraw();
    }

    // ================= Building blocks =================

    private FrameworkElement IntField(string label, Func<int> get, Action<int> set)
    {
        var box = new TextBox { Width = 60, Text = get().ToString() };
        void Commit()
        {
            if (int.TryParse(box.Text.Trim(), out int v)) { set(v); if (Piece is { } picked) KeepAboveBaseline(picked); RefreshPieceList(); Redraw(); }
            box.Text = get().ToString();
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return Labeled(label, box);
    }

    private static FrameworkElement Labeled(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 4) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11 });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(control);
        return panel;
    }

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 4) };

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 2, 4, 2) };
        button.Click += (_, _) => click();
        return button;
    }
}
