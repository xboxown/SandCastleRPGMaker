using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The figure drawn big with a handle on every bone's end: drag a handle (left button) to turn and stretch that bone;
/// a reference picture can lie underneath (drag it with the right button) to trace a skeleton from your own art.
/// </summary>
internal sealed class FigureView : Grid
{
    public const double K = 6; // screen pixels per figure unit
    public readonly Image Reference = new() { Stretch = Stretch.Fill, Opacity = 0.45, IsHitTestVisible = false };
    private readonly Image _figure = new() { Stretch = Stretch.None, IsHitTestVisible = false };
    private readonly Canvas _handles = new() { Background = Brushes.Transparent };
    private readonly Canvas _referenceLayer = new() { IsHitTestVisible = false };
    public Func<(ComposerLibrary Lib, string Direction, ComposerPoseFrame? Frame, ComposerRecipe Recipe)>? Source;
    public string? Selected;
    public event Action<PlacedBone, Point>? Dragged;
    public event Action<PlacedBone>? Picked;
    private List<PlacedBone> _placed = new();
    private PlacedBone? _dragging;
    private Point? _referenceDrag;
    public double ReferenceX = 0, ReferenceY = 0, ReferenceScale = 1;

    public FigureView()
    {
        Width = ComposerRenderer.CanvasWidth * K;
        Height = ComposerRenderer.CanvasHeight * K;
        Background = SpriteComposerWindow.CheckerBrush();
        ClipToBounds = true;
        _referenceLayer.Children.Add(Reference);
        Children.Add(_referenceLayer);
        Children.Add(_figure);
        Children.Add(_handles);
        // the ground
        _handles.MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(_handles);
            _dragging = _placed.Where(b => (ScreenOf(b.End) - p).Length < 9).OrderBy(b => (ScreenOf(b.End) - p).Length).FirstOrDefault();
            if (_dragging is null) return;
            Selected = _dragging.Bone.Name;
            Picked?.Invoke(_dragging);
            _handles.CaptureMouse();
            e.Handled = true;
        };
        _handles.MouseMove += (_, e) =>
        {
            if (_dragging is not null && e.LeftButton == MouseButtonState.Pressed)
            {
                Dragged?.Invoke(_dragging, UnitsOf(e.GetPosition(_handles)));
                Redraw();
                _dragging = _placed.FirstOrDefault(b => b.Bone.Name == Selected) ?? _dragging;
            }
            else if (_referenceDrag is { } from && e.RightButton == MouseButtonState.Pressed)
            {
                var now = e.GetPosition(_handles);
                ReferenceX += now.X - from.X;
                ReferenceY += now.Y - from.Y;
                _referenceDrag = now;
                PlaceReference();
            }
        };
        _handles.MouseLeftButtonUp += (_, _) => { _dragging = null; _handles.ReleaseMouseCapture(); };
        _handles.MouseRightButtonDown += (_, e) => { if (Reference.Source is null) return; _referenceDrag = e.GetPosition(_handles); _handles.CaptureMouse(); };
        _handles.MouseRightButtonUp += (_, _) => { _referenceDrag = null; _handles.ReleaseMouseCapture(); };
    }

    private bool Mirror => Source?.Invoke() is { } s && ComposerRenderer.Mirrored(s.Lib, s.Direction);
    private Point ScreenOf(Point u) => new(((Mirror ? -u.X : u.X) + ComposerRenderer.FeetX) * K, (u.Y + ComposerRenderer.FeetY) * K);
    private Point UnitsOf(Point s) { double x = s.X / K - ComposerRenderer.FeetX; return new Point(Mirror ? -x : x, s.Y / K - ComposerRenderer.FeetY); }

    public void PlaceReference()
    {
        if (Reference.Source is not BitmapSource b) return;
        // the reference is fitted to the figure's height first, then scaled and moved by hand
        double fit = Composer.ReferenceHeight * K / Math.Max(1, b.PixelHeight) * ReferenceScale;
        Reference.Width = b.PixelWidth * fit;
        Reference.Height = b.PixelHeight * fit;
        Canvas.SetLeft(Reference, ComposerRenderer.FeetX * K - Reference.Width / 2 + ReferenceX);
        Canvas.SetTop(Reference, ComposerRenderer.FeetY * K - Reference.Height + ReferenceY);
    }

    public void Redraw()
    {
        if (Source?.Invoke() is not { } s) return;
        _placed = ComposerRenderer.Place(s.Lib, s.Direction, s.Frame, s.Recipe);
        var group = new DrawingGroup();
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Width, Height)); // keeps the drawing the canvas's size
            var m = Matrix.Identity;
            if (Mirror) m.Scale(-1, 1);
            m.Translate(ComposerRenderer.FeetX, ComposerRenderer.FeetY);
            m.Scale(K, K);
            dc.PushTransform(new MatrixTransform(m));
            ComposerRenderer.Draw(dc, s.Lib, s.Direction, _placed, s.Recipe, K);
            dc.Pop();
        }
        _figure.Source = new DrawingImage(group);
        _handles.Children.Clear();
        _handles.Children.Add(new Line { X1 = 0, X2 = Width, Y1 = ComposerRenderer.FeetY * K, Y2 = ComposerRenderer.FeetY * K, Stroke = Brushes.SaddleBrown, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } });
        foreach (var b in _placed)
        {
            bool selected = b.Bone.Name == Selected;
            var a = ScreenOf(b.Start);
            var e = ScreenOf(b.End);
            _handles.Children.Add(new Line { X1 = a.X, Y1 = a.Y, X2 = e.X, Y2 = e.Y, Stroke = selected ? Brushes.Red : Brushes.Yellow, StrokeThickness = selected ? 2 : 1, Opacity = b.Shown ? 0.9 : 0.4 });
            var dot = new Ellipse { Width = 8, Height = 8, Fill = selected ? Brushes.Red : Brushes.Gold, Stroke = Brushes.Black, StrokeThickness = 1, ToolTip = b.Bone.Name };
            Canvas.SetLeft(dot, e.X - 4);
            Canvas.SetTop(dot, e.Y - 4);
            _handles.Children.Add(dot);
        }
    }

    /// <summary>Degrees of a direction in the composer's way (0 down, 90 right, 180 up).</summary>
    public static double AngleOf(Vector v) => Math.Atan2(v.X, v.Y) * 180 / Math.PI;
}

public partial class SpriteComposerWindow
{
    private readonly FigureView _skeletonView = new();
    private readonly FigureView _poseView = new();
    private readonly ComboBox _skeletonDirection = new() { ItemsSource = Composer.Directions, SelectedIndex = 0, Width = 90 };
    private readonly ListBox _boneList = new() { Height = 260 };
    private readonly StackPanel _boneProps = new();
    private readonly ComboBox _poseList = new() { Width = 130 };
    private readonly ListBox _frameList = new() { Height = 120 };
    private readonly ComboBox _poseDirection = new() { ItemsSource = new[] { "South", "North", "West" }, SelectedIndex = 0, Width = 90 };
    private readonly TextBlock _poseInfo = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8 };
    private readonly StackPanel _frameProps = new();

    private string SkeletonDirection => _skeletonDirection.SelectedItem as string ?? "South";
    private ComposerBone? SelectedBone => _lib.BonesFor(ComposerRenderer.Mirrored(_lib, SkeletonDirection) ? "West" : SkeletonDirection).FirstOrDefault(b => b.Name == _boneList.SelectedItem as string);

    private void RedrawFigureCanvases()
    {
        if (_tabs.SelectedIndex == 1) _skeletonView.Redraw();
        if (_tabs.SelectedIndex == 2) _poseView.Redraw();
    }

    // ---------- Skeleton ----------

    private UIElement BuildSkeletonTab()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(290) });

        var left = new StackPanel { Margin = new Thickness(0, 4, 8, 0) };
        grid.Children.Add(left);
        var dirRow = new StackPanel { Orientation = Orientation.Horizontal };
        dirRow.Children.Add(new TextBlock { Text = "Direction ", VerticalAlignment = VerticalAlignment.Center });
        dirRow.Children.Add(_skeletonDirection);
        left.Children.Add(dirRow);
        left.Children.Add(new TextBlock { Text = "East is West mirrored until East has its own bones (Copy to...).", TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7 });
        left.Children.Add(new TextBlock { Text = "Bones", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 2) });
        left.Children.Add(_boneList);
        var add = new Button { Content = "Add a bone (on the picked one)", Margin = new Thickness(0, 4, 0, 0) };
        add.Click += (_, _) => AddBone();
        left.Children.Add(add);
        var remove = new Button { Content = "Delete the bone (every direction)", Margin = new Thickness(0, 2, 0, 0) };
        remove.Click += (_, _) => DeleteBone();
        left.Children.Add(remove);
        var copyRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var copyTo = new ComboBox { ItemsSource = Composer.Directions, SelectedIndex = 3, Width = 80 };
        var copy = new Button { Content = "Copy this direction to", Padding = new Thickness(4, 0, 4, 0) };
        copy.Click += (_, _) =>
        {
            if (copyTo.SelectedItem is not string to || to == SkeletonDirection) return;
            _lib.Skeleton[to] = _lib.BonesFor(SkeletonDirection).Select(b => b.Clone()).ToList();
            RedrawAll();
        };
        copyRow.Children.Add(copy);
        copyRow.Children.Add(copyTo);
        left.Children.Add(copyRow);

        left.Children.Add(new TextBlock { Text = "Reference picture", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 2) });
        var refRow = new StackPanel { Orientation = Orientation.Horizontal };
        var pick = new Button { Content = "Picture...", Padding = new Thickness(6, 0, 6, 0), ToolTip = "Your own art (an Ultima sprite, a drawing) under the skeleton, to trace its bones" };
        pick.Click += (_, _) => LoadReference(_skeletonView);
        var clear = new Button { Content = "None", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0) };
        clear.Click += (_, _) => _skeletonView.Reference.Source = null;
        refRow.Children.Add(pick);
        refRow.Children.Add(clear);
        left.Children.Add(refRow);
        var opacity = new Slider { Minimum = 0.1, Maximum = 1, Value = 0.45, ToolTip = "How strongly the reference shows" };
        opacity.ValueChanged += (_, _) => _skeletonView.Reference.Opacity = opacity.Value;
        left.Children.Add(new TextBlock { Text = "Show it", FontSize = 11 });
        left.Children.Add(opacity);
        var scale = new Slider { Minimum = 0.3, Maximum = 3, Value = 1, ToolTip = "The reference's size (it starts fitted to the figure's height)" };
        scale.ValueChanged += (_, _) => { _skeletonView.ReferenceScale = scale.Value; _skeletonView.PlaceReference(); };
        left.Children.Add(new TextBlock { Text = "Its size (drag it with the right button)", FontSize = 11 });
        left.Children.Add(scale);

        var view = new ScrollViewer { Content = _skeletonView, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(view, 1);
        grid.Children.Add(view);
        var right = new ScrollViewer { Content = _boneProps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(8, 4, 0, 0) };
        Grid.SetColumn(right, 2);
        grid.Children.Add(right);

        _skeletonView.Source = () => (_lib, SkeletonDirection, null, _recipe);
        _skeletonView.Picked += b => _boneList.SelectedItem = b.Bone.Name;
        _skeletonView.Dragged += (placed, point) =>
        {
            // the bone points at the mouse and reaches it
            var v = point - placed.Start;
            if (v.Length < 0.5) return;
            placed.Bone.Angle += FigureView.AngleOf(v) - placed.Angle;
            if (placed.Length > 0.1) placed.Bone.Length = Math.Round(placed.Bone.Length * v.Length / placed.Length, 1);
            ShowBoneProps();
        };
        _skeletonDirection.SelectionChanged += (_, _) => { RefreshBoneList(); _skeletonView.Redraw(); };
        _boneList.SelectionChanged += (_, _) => { _skeletonView.Selected = _boneList.SelectedItem as string; ShowBoneProps(); _skeletonView.Redraw(); };
        return grid;
    }

    private void RefreshBoneList()
    {
        var selected = _boneList.SelectedItem as string;
        _boneList.ItemsSource = _lib.BonesFor(ComposerRenderer.Mirrored(_lib, SkeletonDirection) ? "West" : SkeletonDirection).Select(b => b.Name).ToList();
        _boneList.SelectedItem = selected;
    }

    private void ShowBoneProps()
    {
        _boneProps.Children.Clear();
        if (SelectedBone is not { } bone) { _boneProps.Children.Add(new TextBlock { Text = "Pick a bone in the list, or click a handle.", TextWrapping = TextWrapping.Wrap }); return; }
        _boneProps.Children.Add(new TextBlock { Text = $"{bone.Name} ({SkeletonDirection})", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        void Row(string label, UIElement control, string tip)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), ToolTip = tip };
            row.Children.Add(new TextBlock { Text = label, Width = 100, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(control);
            _boneProps.Children.Add(row);
        }
        TextBox Number(double value, Action<double> set)
        {
            var box = new TextBox { Text = value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) };
            void Commit()
            {
                if (double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) { set(v); _skeletonView.Redraw(); }
                else box.Background = Brushes.MistyRose;
            }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
            return box;
        }
        var name = new TextBox { Text = bone.Name };
        name.LostFocus += (_, _) => RenameBone(bone.Name, name.Text.Trim());
        Row("Name", name, "Renamed in every direction, its parts and the poses");
        var parent = new ComboBox { ItemsSource = new[] { "(the hips)" }.Concat(_lib.BonesFor(SkeletonDirection).Where(b => b != bone).Select(b => b.Name)).ToList(), SelectedItem = bone.Parent.Length == 0 ? "(the hips)" : bone.Parent };
        parent.SelectionChanged += (_, _) => { bone.Parent = parent.SelectedItem as string is "(the hips)" or null ? "" : (string)parent.SelectedItem; _skeletonView.Redraw(); };
        Row("Hangs from", parent, "The bone it is joined to (it moves with it)");
        var atStart = new CheckBox { Content = "at its start (not its end)", IsChecked = bone.AtStart };
        atStart.Click += (_, _) => { bone.AtStart = atStart.IsChecked == true; _skeletonView.Redraw(); };
        Row("", atStart, "Legs hang from the start of the hips, arms from the end of the body (the shoulders)");
        Row("Across (x)", Number(bone.OffsetX, v => bone.OffsetX = v), "Moved right (+) or left (-) from that point, in figure units");
        Row("Down (y)", Number(bone.OffsetY, v => bone.OffsetY = v), "Moved down (+) or up (-)");
        Row("Angle", Number(bone.Angle, v => bone.Angle = v), "Degrees added to its parent's: 0 straight on, 90 / -90 to the sides, 180 back");
        Row("Length", Number(bone.Length, v => bone.Length = Math.Max(0, v)), "In figure units (the figure is 64 tall)");
        Row("Width", Number(bone.Width, v => bone.Width = Math.Max(0, v)), "Its shape's width");
        Row("Layer", Number(bone.Layer, v => bone.Layer = (int)v), "Higher is drawn later, in front (each direction its own)");
        var region = new ComboBox { ItemsSource = Composer.Regions, SelectedItem = bone.Region };
        region.SelectionChanged += (_, _) => { bone.Region = region.SelectedItem as string ?? "Skin"; _skeletonView.Redraw(); };
        Row("Colour of", region, "Which recipe colour it is drawn in");
        var shape = new ComboBox { ItemsSource = Composer.Shapes, SelectedItem = bone.Shape };
        shape.SelectionChanged += (_, _) => { bone.Shape = shape.SelectedItem as string ?? "Capsule"; _skeletonView.Redraw(); };
        Row("Shape", shape, "Drawn when no part picture is pinned to the bone (Head: with eyes)");
        var far = new CheckBox { Content = "far side (darker)", IsChecked = bone.Far };
        far.Click += (_, _) => { bone.Far = far.IsChecked == true; _skeletonView.Redraw(); };
        Row("", far, "Behind the body from this side");
        var needs = new ComboBox { IsEditable = true, ItemsSource = new[] { "", "Hair", "Cloak", "Belt", "Weapon" }, Text = bone.NeedsOption };
        needs.LostFocus += (_, _) => { bone.NeedsOption = needs.Text.Trim(); _skeletonView.Redraw(); };
        Row("Only with", needs, "Drawn only when the recipe wears this (empty: always)");
        _boneProps.Children.Add(new TextBlock { Text = "Drag the bone's handle in the picture to turn and stretch it.", TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 8, 0, 0) });
    }

    private void AddBone()
    {
        string parent = _boneList.SelectedItem as string ?? "";
        string name = "Bone";
        for (int n = 1; _lib.Bones().Contains(name); n++) name = $"Bone{n}";
        foreach (var bones in _lib.Skeleton.Values)
            bones.Add(new ComposerBone { Name = name, Parent = bones.Any(b => b.Name == parent) ? parent : "", Length = 8, Width = 4, Layer = 50, Region = "Skin" });
        RefreshBoneList();
        _boneList.SelectedItem = name;
        _skeletonView.Redraw();
    }

    private void DeleteBone()
    {
        if (_boneList.SelectedItem is not string name) return;
        if (MessageBox.Show(this, $"Delete the bone {name} in every direction (its children hang from the hips then)?", "Sprite Composer", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        foreach (var bones in _lib.Skeleton.Values)
        {
            bones.RemoveAll(b => b.Name == name);
            foreach (var b in bones.Where(b => b.Parent == name)) b.Parent = "";
        }
        RefreshBoneList();
        _skeletonView.Redraw();
    }

    private void RenameBone(string from, string to)
    {
        if (to.Length == 0 || to == from || _lib.Bones().Contains(to)) return;
        foreach (var bones in _lib.Skeleton.Values)
            foreach (var b in bones)
            {
                if (b.Name == from) b.Name = to;
                if (b.Parent == from) b.Parent = to;
            }
        foreach (var part in _lib.Parts.Where(p => p.Bone == from)) part.Bone = to;
        foreach (var frame in _lib.Poses.SelectMany(p => p.Frames))
            foreach (var table in frame.Turn.Values.Concat(frame.Stretch.Values))
                if (table.Remove(from, out double v)) table[to] = v;
        RefreshBoneList();
        _boneList.SelectedItem = to;
    }

    private void LoadReference(FigureView view)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Pictures|*.png;*.bmp;*.jpg;*.jpeg;*.gif|All files|*.*", Title = "A reference picture" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(dialog.FileName);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            view.Reference.Source = bitmap;
            RenderOptions.SetBitmapScalingMode(view.Reference, BitmapScalingMode.NearestNeighbor);
            view.ReferenceX = view.ReferenceY = 0;
            view.PlaceReference();
        }
        catch (Exception ex) { MessageBox.Show(this, "That picture can't be read: " + ex.Message, "Sprite Composer"); }
    }

    // ---------- Poses ----------

    private ComposerPose? SelectedPose => _lib.Poses.FirstOrDefault(p => p.Name == _poseList.SelectedItem as string);
    private ComposerPoseFrame? SelectedFrame => SelectedPose is { } pose && _frameList.SelectedIndex >= 0 && _frameList.SelectedIndex < pose.Frames.Count ? pose.Frames[_frameList.SelectedIndex] : null;
    private string PoseDirection => _poseDirection.SelectedItem as string ?? "South";

    private UIElement BuildPosesTab()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(270) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 4, 8, 0) };
        grid.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        var poseRow = new StackPanel { Orientation = Orientation.Horizontal };
        poseRow.Children.Add(new TextBlock { Text = "Pose ", VerticalAlignment = VerticalAlignment.Center });
        poseRow.Children.Add(_poseList);
        left.Children.Add(poseRow);
        var poseButtons = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        Button Small(string text, string tip, Action click) { var b = new Button { Content = text, ToolTip = tip, Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0, 0, 3, 3) }; b.Click += (_, _) => click(); return b; }
        poseButtons.Children.Add(Small("New pose", "A new pose with one standing frame (name it below)", () =>
        {
            string name = "Pose";
            for (int n = 1; _lib.Poses.Any(p => p.Name == name); n++) name = $"Pose {n}";
            _lib.Poses.Add(new ComposerPose { Name = name, Frames = { new ComposerPoseFrame() } });
            RefreshPoseLists();
            _poseList.SelectedItem = name;
        }));
        poseButtons.Children.Add(Small("Delete pose", "", () =>
        {
            if (SelectedPose is { } p && MessageBox.Show(this, $"Delete the pose {p.Name}?", "Sprite Composer", MessageBoxButton.YesNo) == MessageBoxResult.Yes) { _lib.Poses.Remove(p); RefreshPoseLists(); }
        }));
        left.Children.Add(poseButtons);
        var poseName = new TextBox { ToolTip = "The pose's name (press Enter)" };
        poseName.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || SelectedPose is not { } p || poseName.Text.Trim().Length == 0 || _lib.Poses.Any(x => x.Name == poseName.Text.Trim())) return;
            p.Name = poseName.Text.Trim();
            RefreshPoseLists();
            _poseList.SelectedItem = p.Name;
        };
        var nameRow = new DockPanel();
        nameRow.Children.Add(new TextBlock { Text = "Name ", VerticalAlignment = VerticalAlignment.Center });
        nameRow.Children.Add(poseName);
        left.Children.Add(nameRow);
        var loop = new CheckBox { Content = "Plays round and round (Loop)" };
        loop.Click += (_, _) => { if (SelectedPose is { } p) p.Loop = loop.IsChecked == true; };
        left.Children.Add(loop);

        left.Children.Add(new TextBlock { Text = "Frames", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) });
        left.Children.Add(_frameList);
        var frameButtons = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
        frameButtons.Children.Add(Small("Add (a copy)", "A copy of the picked frame after it", () =>
        {
            if (SelectedPose is not { } p) return;
            var from = SelectedFrame ?? new ComposerPoseFrame();
            var copy = System.Text.Json.JsonSerializer.Deserialize<ComposerPoseFrame>(System.Text.Json.JsonSerializer.Serialize(from))!;
            int at = _frameList.SelectedIndex < 0 ? p.Frames.Count : _frameList.SelectedIndex + 1;
            p.Frames.Insert(at, copy);
            RefreshFrames();
            _frameList.SelectedIndex = at;
        }));
        frameButtons.Children.Add(Small("Delete", "", () =>
        {
            if (SelectedPose is { } p && SelectedFrame is { } f && p.Frames.Count > 1) { p.Frames.Remove(f); RefreshFrames(); }
        }));
        frameButtons.Children.Add(Small("Clear the bone", "The picked bone back to its rest in this frame and direction", () =>
        {
            if (SelectedFrame is { } f && _poseView.Selected is { } bone)
            {
                if (f.Turn.TryGetValue(PoseDirection, out var t)) t.Remove(bone);
                if (f.Stretch.TryGetValue(PoseDirection, out var s)) s.Remove(bone);
                _poseView.Redraw();
            }
        }));
        left.Children.Add(frameButtons);
        left.Children.Add(_frameProps);
        var dirRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        dirRow.Children.Add(new TextBlock { Text = "Direction ", VerticalAlignment = VerticalAlignment.Center });
        dirRow.Children.Add(_poseDirection);
        left.Children.Add(dirRow);
        left.Children.Add(_poseInfo);
        _poseInfo.Text = "Drag a bone's handle to turn it in this frame. West's turns are East's too (mirrored); North uses South's until it has its own turns - the first one you make copies South's. A pose made for one direction may need its own turns in the others.";

        var view = new ScrollViewer { Content = _poseView, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(view, 1);
        grid.Children.Add(view);

        _poseView.Source = () => (_lib, PoseDirection, SelectedFrame, _recipe);
        _poseView.Dragged += (placed, point) =>
        {
            if (SelectedFrame is not { } frame) return;
            var v = point - placed.Start;
            if (v.Length < 0.5) return;
            if (!frame.Turn.TryGetValue(PoseDirection, out var table))
            {
                // the first turn of a direction that used another's: start from those
                string from = PoseDirection == "North" ? "South" : "";
                table = from.Length > 0 && frame.Turn.TryGetValue(from, out var source) ? new Dictionary<string, double>(source) : new Dictionary<string, double>();
                frame.Turn[PoseDirection] = table;
            }
            table[placed.Bone.Name] = Math.Round(frame.TurnOf(PoseDirection, placed.Bone.Name) + FigureView.AngleOf(v) - placed.Angle, 1);
        };
        _poseList.SelectionChanged += (_, _) =>
        {
            if (SelectedPose is { } p) { poseName.Text = p.Name; loop.IsChecked = p.Loop; }
            RefreshFrames();
        };
        _frameList.SelectionChanged += (_, _) => { ShowFrameProps(); _poseView.Redraw(); };
        _poseView.Picked += _ => ShowFrameProps();
        _poseDirection.SelectionChanged += (_, _) => _poseView.Redraw();
        return grid;
    }

    private void RefreshPoseLists()
    {
        var names = _lib.Poses.Select(p => p.Name).ToList();
        var a = _poseList.SelectedItem as string;
        var b = _previewPose.SelectedItem as string;
        _poseList.ItemsSource = names;
        _previewPose.ItemsSource = names.ToList();
        _poseList.SelectedItem = a != null && names.Contains(a) ? a : names.FirstOrDefault();
        _previewPose.SelectedItem = b != null && names.Contains(b) ? b : names.Contains("Walk") ? "Walk" : names.FirstOrDefault();
        RefreshReviewPoses();
    }

    private void RefreshFrames()
    {
        int keep = _frameList.SelectedIndex;
        _frameList.ItemsSource = SelectedPose?.Frames.Select((f, i) => $"Frame {i + 1} - {f.DurationMs} ms" + (f.BodyAngle != 0 ? " (lying)" : f.Lower != 0 ? " (lowered)" : "")).ToList();
        _frameList.SelectedIndex = SelectedPose is { } p && p.Frames.Count > 0 ? Math.Clamp(keep, 0, p.Frames.Count - 1) : -1;
    }

    private void ShowFrameProps()
    {
        _frameProps.Children.Clear();
        if (SelectedFrame is not { } f) return;
        void Row(string label, double value, string tip, Action<double> set)
        {
            var box = new TextBox { Text = value.ToString(System.Globalization.CultureInfo.InvariantCulture), Width = 70 };
            void Commit() { if (double.TryParse(box.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)) { set(v); RefreshFramesKeep(); _poseView.Redraw(); } }
            box.LostFocus += (_, _) => Commit();
            box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0), ToolTip = tip };
            row.Children.Add(new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(box);
            _frameProps.Children.Add(row);
        }
        Row("Time (ms)", f.DurationMs, "How long this frame shows", v => f.DurationMs = (int)Math.Max(20, v));
        Row("Body lowered", f.Lower, "The body down by this many units (sitting: about 9)", v => f.Lower = v);
        Row("Body turned (deg)", f.BodyAngle, "The whole figure turned: 90 or -90 lies it down", v => f.BodyAngle = v);
        if (_poseView.Selected is { } bone)
            Row($"{bone} length x", f.StretchOf(PoseDirection, bone), "This bone's length times this in this frame and direction (a knee lifted toward you looks shorter)", v =>
            {
                if (!f.Stretch.TryGetValue(PoseDirection, out var t)) f.Stretch[PoseDirection] = t = new Dictionary<string, double>();
                t[bone] = v;
            });
    }

    private void RefreshFramesKeep()
    {
        int at = _frameList.SelectedIndex;
        RefreshFrames();
        _frameList.SelectedIndex = at;
    }
}
