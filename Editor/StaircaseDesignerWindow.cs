using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Staircase Designer: a staircase built from separate step pieces, as
/// Exult's are - each step its own picture (any sprite), its own spot to
/// stand on and its own height. A Stairs object links to a design (Object
/// Editor) and is placed on the map as one object; in play every step is a
/// real step, climbed a lift at a time, and each can be clicked and named.
/// A design has a pose for each facing (north, east, south, west), picked
/// per placed copy in the Map Editor as a chair's facing is.
/// </summary>
public class StaircaseDesignerWindow : Window
{
    private static readonly SpriteDirection[] Facings = { SpriteDirection.North, SpriteDirection.East, SpriteDirection.South, SpriteDirection.West };
    private const int AreaTiles = 10; // the drawing area: 10 x 10 cells, the anchor cell at the bottom middle
    private const int LiftPixels = 4;

    private readonly ObjectDatabase _objects;
    private readonly SpriteDatabase _sprites;
    private readonly ResourceDatabase _resources;
    private readonly int _tileW, _tileH;

    private readonly ListBox _designList = new() { Margin = new Thickness(0, 4, 0, 4), DisplayMemberPath = nameof(StairDesign.Name) };
    private readonly TextBox _nameBox = new() { Margin = new Thickness(0, 2, 0, 6) };
    private readonly StackPanel _poseBar = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x1c, 0x1c)), ClipToBounds = true, Focusable = true };
    private readonly ListBox _stepList = new() { Height = 150, Margin = new Thickness(0, 4, 0, 4) };
    private readonly StackPanel _stepProps = new();
    private readonly TextBlock _check = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _zoomBox = new() { Width = 70, ItemsSource = new[] { 1, 2, 3, 4 }, SelectedItem = 3 };
    private readonly RadioButton _dragPicture = new() { Content = "Picture", IsChecked = true, Margin = new Thickness(4, 0, 6, 0) };
    private readonly RadioButton _dragFoot = new() { Content = "Stand-on area", Margin = new Thickness(0, 0, 6, 0) };
    private readonly RadioButton _dragBoth = new() { Content = "Both", Margin = new Thickness(0, 0, 6, 0) };

    private SpriteDirection _facing = SpriteDirection.North;
    private int _selectedStep = -1;
    private bool _refreshing;
    private (Point Start, int PicX, int PicY, int FootX, int FootY)? _drag;

    private int Zoom => _zoomBox.SelectedItem is int z ? z : 3;
    private double AnchorLeft => (AreaTiles / 2) * _tileW;
    private double AnchorTop => (AreaTiles - 2) * _tileH;
    private StairDesign? Design => _designList.SelectedItem as StairDesign;
    private StairPose? Pose => Design?.Poses.FirstOrDefault(p => p.Facing == _facing);
    private StairStep? Step => Pose is { } pose && _selectedStep >= 0 && _selectedStep < pose.Steps.Count ? pose.Steps[_selectedStep] : null;

    public StaircaseDesignerWindow(ObjectDatabase objects, SpriteDatabase sprites, ResourceDatabase resources, int tileWidth, int tileHeight, int selectDesignId = -1)
    {
        _objects = objects;
        _sprites = sprites;
        _resources = resources;
        _tileW = Math.Max(8, tileWidth);
        _tileH = Math.Max(8, tileHeight);
        Title = "Staircase Designer";
        Width = 1240;
        Height = 780;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(8) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });

        // ---- Designs ----
        var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        var leftTop = new StackPanel();
        leftTop.Children.Add(new TextBlock { Text = "Staircases", FontWeight = FontWeights.Bold });
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
        leftBottom.Children.Add(Note("A Stairs object uses a staircase: Object Editor > type Stairs / Ladder > Kind: Stairs > Design. Placed on the map it is one object; in play each step is a real step."));
        DockPanel.SetDock(leftBottom, Dock.Bottom);
        left.Children.Add(leftBottom);
        left.Children.Add(_designList);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        // ---- Drawing ----
        var middle = new DockPanel();
        var top = new StackPanel();
        top.Children.Add(_poseBar);
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        tools.Children.Add(new TextBlock { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        tools.Children.Add(_zoomBox);
        tools.Children.Add(new TextBlock { Text = "   Dragging moves:", VerticalAlignment = VerticalAlignment.Center });
        tools.Children.Add(_dragPicture);
        tools.Children.Add(_dragFoot);
        tools.Children.Add(_dragBoth);
        top.Children.Add(tools);
        top.Children.Add(Note("Click a step to pick it; drag to move it. Arrows: its picture 1px (Shift: 4px). Ctrl+arrows: its stand-on area 1 unit (8px). Page Up / Page Down: one lift higher / lower. Delete: remove it. " +
                              "The yellow square is the cell the staircase is placed on; nothing may be drawn below its red line. The cyan box is where you stand on the picked step, drawn at its height."));
        DockPanel.SetDock(top, Dock.Top);
        middle.Children.Add(top);
        DockPanel.SetDock(_check, Dock.Bottom);
        middle.Children.Add(_check);
        middle.Children.Add(new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(middle, 1);
        root.Children.Add(middle);

        // ---- Steps ----
        var right = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        right.Children.Add(new TextBlock { Text = "Steps of this pose (1 = the bottom step)", FontWeight = FontWeights.Bold });
        var stepButtons = new WrapPanel();
        stepButtons.Children.Add(MakeButton("Add step", AddStep));
        stepButtons.Children.Add(MakeButton("Delete", DeleteStep));
        stepButtons.Children.Add(MakeButton("Move up", () => MoveStep(-1)));
        stepButtons.Children.Add(MakeButton("Move down", () => MoveStep(1)));
        right.Children.Add(stepButtons);
        right.Children.Add(_stepList);
        right.Children.Add(_stepProps);
        right.Children.Add(BuildQuickBuild());
        Grid.SetColumn(rightScroll, 2);
        root.Children.Add(rightScroll);

        Content = root;

        foreach (var facing in Facings)
        {
            var f = facing;
            var button = new ToggleButton { Content = facing.ToString(), Width = 70, Margin = new Thickness(0, 0, 4, 0), Tag = facing };
            button.Click += (_, _) => { _facing = f; _selectedStep = 0; RefreshAll(); };
            _poseBar.Children.Add(button);
        }
        _poseBar.Children.Add(new TextBlock { Text = "  Copy steps from", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) });
        var copyFrom = new ComboBox { Width = 80, ItemsSource = Facings, SelectedIndex = 0 };
        _poseBar.Children.Add(copyFrom);
        _poseBar.Children.Add(MakeButton("Copy", () => CopyPoseFrom((SpriteDirection)copyFrom.SelectedItem)));
        _poseBar.Children.Add(MakeButton("Mirror left-right", MirrorPose));

        _designList.ItemsSource = _objects.StairDesigns;
        _designList.SelectionChanged += (_, _) => { _selectedStep = 0; _facing = Design?.Poses.FirstOrDefault(p => p.Steps.Count > 0)?.Facing ?? SpriteDirection.North; RefreshAll(); };
        _nameBox.TextChanged += (_, _) =>
        {
            if (_refreshing || Design is not { } design) return;
            design.Name = _nameBox.Text;
            _designList.Items.Refresh();
        };
        _stepList.SelectionChanged += (_, _) =>
        {
            if (_refreshing) return;
            _selectedStep = _stepList.SelectedIndex;
            ShowStepProps();
            Redraw();
        };
        _zoomBox.SelectionChanged += (_, _) => Redraw();
        _canvas.MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += (_, _) => { _drag = null; _canvas.ReleaseMouseCapture(); RefreshStepList(); ShowStepProps(); };
        PreviewKeyDown += Window_PreviewKeyDown;

        _designList.SelectedItem = _objects.StairDesigns.FirstOrDefault(d => d.Id == selectDesignId) ?? _objects.StairDesigns.FirstOrDefault();
        RefreshAll();
    }

    public int SelectedDesignId => Design?.Id ?? -1;

    // ================= Designs =================

    private void NewDesign()
    {
        var design = new StairDesign { Id = _objects.NextStairDesignId(), Name = $"Staircase {_objects.StairDesigns.Count + 1}" };
        design.Poses.Add(new StairPose { Facing = SpriteDirection.North });
        _objects.StairDesigns.Add(design);
        _designList.Items.Refresh();
        _designList.SelectedItem = design;
        _check.Text = "New staircase: use Quick build (right) to lay out its steps from a sprite, then adjust any step by hand.";
    }

    private void DuplicateDesign()
    {
        if (Design is not { } source) return;
        var copy = new StairDesign { Id = _objects.NextStairDesignId(), Name = source.Name + " copy" };
        foreach (var pose in source.Poses) copy.Poses.Add(new StairPose { Facing = pose.Facing, Steps = pose.Steps.Select(s => s.Clone()).ToList() });
        _objects.StairDesigns.Add(copy);
        _designList.Items.Refresh();
        _designList.SelectedItem = copy;
    }

    private void DeleteDesign()
    {
        if (Design is not { } design) return;
        var users = _objects.Objects.Where(o => o.Type == ObjectType.Stairs && o.Stairs.DesignId == design.Id).Select(o => o.Name).ToList();
        string question = users.Count == 0 ? $"Delete \"{design.Name}\"?"
            : $"Delete \"{design.Name}\"? It is used by: {string.Join(", ", users)} - they go back to their own sprite.";
        if (MessageBox.Show(this, question, "Staircase Designer", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var obj in _objects.Objects.Where(o => o.Type == ObjectType.Stairs && o.Stairs.DesignId == design.Id)) obj.Stairs.DesignId = -1;
        _objects.StairDesigns.Remove(design);
        _designList.Items.Refresh();
        _designList.SelectedItem = _objects.StairDesigns.FirstOrDefault();
    }

    private StairPose EnsurePose()
    {
        var design = Design!;
        var pose = design.Poses.FirstOrDefault(p => p.Facing == _facing);
        if (pose is null) { pose = new StairPose { Facing = _facing }; design.Poses.Add(pose); }
        return pose;
    }

    private void CopyPoseFrom(SpriteDirection from)
    {
        if (Design is not { } design || from == _facing) return;
        var source = design.Poses.FirstOrDefault(p => p.Facing == from);
        if (source is null || source.Steps.Count == 0) { _check.Text = $"The {from} pose has no steps to copy."; return; }
        var pose = EnsurePose();
        if (pose.Steps.Count > 0 && MessageBox.Show(this, $"Replace the {_facing} pose's {pose.Steps.Count} steps with a copy of the {from} pose?", "Staircase Designer", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        pose.Steps = source.Steps.Select(s => s.Clone()).ToList();
        _selectedStep = 0;
        RefreshAll();
    }

    /// <summary>Flips this pose left to right (a staircase going up east becomes one going up west) - pictures keep their art; swap in mirrored sprites if the art needs it.</summary>
    private void MirrorPose()
    {
        if (Pose is not { Steps.Count: > 0 } pose) return;
        int unitsX = Math.Max(1, _tileW / SpriteSizing.BaseUnit);
        foreach (var step in pose.Steps)
        {
            var size = StairLook.PictureSize(step, _sprites, _tileW, _tileH);
            step.PictureX = (int)(_tileW - step.PictureX - size.Width);
            step.FootX = unitsX - step.FootX - step.SizeX;
        }
        RefreshAll();
    }

    // ================= Steps =================

    private void AddStep()
    {
        if (Design is null) { NewDesign(); }
        var pose = EnsurePose();
        StairStep step;
        if (Step is { } from)
        {
            // The next one up: one unit further back and a lift higher, its picture drawn a lift up-left of where it stands.
            step = from.Clone();
            step.Name = string.Empty;
            step.FootY -= from.SizeY;
            step.PictureY -= from.SizeY * SpriteSizing.BaseUnit + LiftPixels;
            step.PictureX -= LiftPixels;
            step.TopLift = from.TopLift + 1;
        }
        else
        {
            int unitsX = Math.Max(1, _tileW / SpriteSizing.BaseUnit), unitsY = Math.Max(1, _tileH / SpriteSizing.BaseUnit);
            step = new StairStep { FootX = 0, FootY = unitsY - 1, SizeX = unitsX, SizeY = 1, TopLift = 1, PictureX = 0, PictureY = 0 };
        }
        pose.Steps.Add(step);
        _selectedStep = pose.Steps.Count - 1;
        RefreshAll();
    }

    private void DeleteStep()
    {
        if (Pose is not { } pose || Step is not { } step) return;
        pose.Steps.Remove(step);
        _selectedStep = Math.Min(_selectedStep, pose.Steps.Count - 1);
        RefreshAll();
    }

    private void MoveStep(int by)
    {
        if (Pose is not { } pose || Step is not { } step) return;
        int to = _selectedStep + by;
        if (to < 0 || to >= pose.Steps.Count) return;
        pose.Steps.RemoveAt(_selectedStep);
        pose.Steps.Insert(to, step);
        _selectedStep = to;
        RefreshAll();
    }

    // ================= Quick build =================

    private UIElement BuildQuickBuild()
    {
        var body = new StackPanel();
        var spriteChoices = SpriteChoices();
        var sprite = new ComboBox { Width = 280, ItemsSource = spriteChoices, DisplayMemberPath = "Display", SelectedIndex = 0 };
        var count = new TextBox { Width = 50, Text = "5" };
        var toward = new ComboBox { Width = 80, ItemsSource = Facings, SelectedIndex = 0 };
        var across = new TextBox { Width = 50, Text = Math.Max(1, _tileW / SpriteSizing.BaseUnit).ToString() };
        var depth = new TextBox { Width = 50, Text = "1" };
        var raise = new TextBox { Width = 50, Text = LiftPixels.ToString() };
        body.Children.Add(new TextBlock { Text = "Step sprite" });
        body.Children.Add(sprite);
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(Labeled("Steps", count));
        row.Children.Add(Labeled("Goes up toward", toward));
        row.Children.Add(Labeled("Width (units)", across));
        row.Children.Add(Labeled("Depth (units)", depth));
        row.Children.Add(Labeled("Picture raise / step (px)", raise));
        body.Children.Add(row);
        body.Children.Add(Note("Lays out the steps of the pose shown: each one a step further toward that side and one lift (4px) higher - Exult's walking rule climbs a lift a step, so 5 steps reach the floor above. " +
                               "Each picture is drawn up-left by the raise (4px = one lift, as the game draws heights). A unit is 8px."));
        body.Children.Add(MakeButton("Build steps", () =>
        {
            if (!int.TryParse(count.Text, out int n) || n < 1 || n > 40) { _check.Text = "Steps: 1 to 40."; return; }
            if (!int.TryParse(across.Text, out int w) || w < 1) w = 4;
            if (!int.TryParse(depth.Text, out int dep) || dep < 1) dep = 1;
            if (!int.TryParse(raise.Text, out int r)) r = LiftPixels;
            var choice = sprite.SelectedItem as SpriteChoice;
            QuickBuild(n, (SpriteDirection)toward.SelectedItem, w, dep, r, choice?.Group ?? "", choice?.Name ?? "");
        }));
        return new GroupBox { Header = "Quick build", Padding = new Thickness(6), Margin = new Thickness(0, 8, 0, 0), Content = body };
    }

    private void QuickBuild(int n, SpriteDirection toward, int width, int depth, int raise, string group, string name)
    {
        if (Design is null) NewDesign();
        var pose = EnsurePose();
        if (pose.Steps.Count > 0 && MessageBox.Show(this, $"Replace the {_facing} pose's {pose.Steps.Count} steps?", "Staircase Designer", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        int unitsX = Math.Max(1, _tileW / SpriteSizing.BaseUnit), unitsY = Math.Max(1, _tileH / SpriteSizing.BaseUnit);
        var probe = new StairStep { Sprite = new SpriteLink { SpriteGroup = group, SpriteName = name } };
        var picture = StairLook.PictureSize(probe, _sprites, _tileW, _tileH);
        pose.Steps.Clear();
        for (int i = 0; i < n; i++)
        {
            // Where step i stands. Going up south or east, the top step ends on the anchor cell, so nothing is drawn below it.
            int footX = (unitsX - width) / 2, footY = unitsY - depth;
            switch (toward)
            {
                case SpriteDirection.North: footY -= depth * i; break;
                case SpriteDirection.South: footY -= depth * (n - 1 - i); break;
                case SpriteDirection.East: footX += depth * i - depth * (n - 1); break;
                case SpriteDirection.West: footX -= depth * i; break;
            }
            if (toward is SpriteDirection.East or SpriteDirection.West) { footY = unitsY - width; }
            int sizeX = toward is SpriteDirection.East or SpriteDirection.West ? depth : width;
            int sizeY = toward is SpriteDirection.East or SpriteDirection.West ? width : depth;
            if (toward is SpriteDirection.East or SpriteDirection.West) footX = toward == SpriteDirection.East ? footX : (unitsX - depth) / 2 - depth * i;
            // Its picture: bottom-centre under the middle of where it stands, then raised up-left for its height.
            int pictureX = (int)Math.Round(footX * SpriteSizing.BaseUnit + sizeX * SpriteSizing.BaseUnit / 2.0 - picture.Width / 2.0) - raise * i;
            int pictureY = (int)Math.Round((double)(footY + sizeY) * SpriteSizing.BaseUnit - picture.Height) - raise * i;
            pose.Steps.Add(new StairStep
            {
                Sprite = new SpriteLink { SpriteGroup = group, SpriteName = name },
                FootX = footX, FootY = footY, SizeX = sizeX, SizeY = sizeY, TopLift = i + 1, PictureX = pictureX, PictureY = pictureY
            });
        }
        // Nothing below the anchor cell's bottom edge: lift the whole drawing if a picture hangs under it.
        int below = pose.Steps.Max(s => s.PictureY + (int)StairLook.PictureSize(s, _sprites, _tileW, _tileH).Height) - _tileH;
        if (below > 0) foreach (var s in pose.Steps) s.PictureY -= below;
        _selectedStep = 0;
        RefreshAll();
    }

    // ================= Showing =================

    private void RefreshAll()
    {
        _refreshing = true;
        _nameBox.Text = Design?.Name ?? "";
        _nameBox.IsEnabled = Design is not null;
        foreach (var button in _poseBar.Children.OfType<ToggleButton>())
        {
            var facing = (SpriteDirection)button.Tag;
            button.IsChecked = facing == _facing;
            int steps = Design?.Poses.FirstOrDefault(p => p.Facing == facing)?.Steps.Count ?? 0;
            button.Content = steps > 0 ? $"{facing} ({steps})" : facing.ToString();
        }
        _refreshing = false;
        RefreshStepList();
        ShowStepProps();
        Redraw();
    }

    private void RefreshStepList()
    {
        _refreshing = true;
        var pose = Pose;
        _stepList.ItemsSource = pose?.Steps.Select((s, i) => $"{i + 1}. {(i == 0 ? "(the staircase's own name)" : string.IsNullOrWhiteSpace(s.Name) ? $"Step {i + 1}" : s.Name)} - lift {s.TopLift}").ToList();
        _stepList.SelectedIndex = pose is null ? -1 : Math.Min(_selectedStep, pose.Steps.Count - 1);
        _selectedStep = _stepList.SelectedIndex;
        _refreshing = false;
        UpdateCheck();
    }

    private void ShowStepProps()
    {
        _stepProps.Children.Clear();
        if (Step is not { } step) { _stepProps.Children.Add(Note(Design is null ? "Make a staircase with New." : "No step picked: Add step, or Quick build below.")); return; }
        var panel = new StackPanel();
        if (_selectedStep > 0)
            panel.Children.Add(Field("Name in play (empty = \"Step N of ...\")", step.Name, v => { step.Name = v; RefreshStepList(); }, 200));
        else
            panel.Children.Add(Note("The bottom step goes by the staircase object's own name when clicked."));
        var choices = SpriteChoices();
        var sprite = new ComboBox { Width = 280, ItemsSource = choices, DisplayMemberPath = "Display" };
        sprite.SelectedItem = choices.FirstOrDefault(c => c.Name == step.Sprite.SpriteName && (c.Group == step.Sprite.SpriteGroup || c.Name.Length == 0)) ?? choices[0];
        sprite.SelectionChanged += (_, _) =>
        {
            var c = sprite.SelectedItem as SpriteChoice;
            step.Sprite = new SpriteLink { SpriteGroup = c?.Group ?? "", SpriteName = c?.Name ?? "" };
            Redraw();
        };
        panel.Children.Add(new TextBlock { Text = "Sprite" });
        panel.Children.Add(sprite);
        var pictureRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        pictureRow.Children.Add(IntField("Picture X (px)", () => step.PictureX, v => step.PictureX = v));
        pictureRow.Children.Add(IntField("Picture Y (px)", () => step.PictureY, v => step.PictureY = v));
        panel.Children.Add(pictureRow);
        var footRow = new WrapPanel();
        footRow.Children.Add(IntField("Stand X", () => step.FootX, v => step.FootX = v));
        footRow.Children.Add(IntField("Stand Y", () => step.FootY, v => step.FootY = v));
        footRow.Children.Add(IntField("Width", () => step.SizeX, v => step.SizeX = Math.Max(1, v)));
        footRow.Children.Add(IntField("Depth", () => step.SizeY, v => step.SizeY = Math.Max(1, v)));
        panel.Children.Add(footRow);
        panel.Children.Add(IntField("Top lift (where you stand on it; 4px each)", () => step.TopLift, v => step.TopLift = Math.Clamp(v, 1, 40)));
        _stepProps.Children.Add(new GroupBox { Header = $"Step {_selectedStep + 1}", Padding = new Thickness(6), Content = panel });
    }

    private void UpdateCheck()
    {
        if (Pose is not { Steps.Count: > 0 } pose) { _check.Text = ""; return; }
        var lines = new List<string>();
        int top = pose.Steps.Max(s => s.TopLift);
        lines.Add(top % MapFloors.LiftsPerFloor == 0
            ? $"The top step is {top} lifts up: walking off it you are on the floor {top / MapFloors.LiftsPerFloor} storey{(top / MapFloors.LiftsPerFloor == 1 ? "" : "s")} above."
            : $"The top step is {top} lift{(top == 1 ? "" : "s")} up ({top * LiftPixels}px). A storey is {MapFloors.LiftsPerFloor} lifts - make the top step lift {MapFloors.LiftsPerFloor} to reach the floor above.");
        var byLift = pose.Steps.Select((s, i) => (Step: s, Index: i)).OrderBy(t => t.Step.TopLift).ToList();
        int previous = 0;
        foreach (var (step, index) in byLift)
        {
            if (step.TopLift - previous > 1) lines.Add($"Step {index + 1} is {step.TopLift - previous} lifts above the one below it - walkers only step up 1 lift (4px) at a time, so it can't be climbed.");
            previous = step.TopLift;
        }
        _check.Text = string.Join("  ", lines);
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        double z = Zoom;
        _canvas.Width = AreaTiles * _tileW * z;
        _canvas.Height = AreaTiles * _tileH * z;
        // The cell grid.
        for (int c = 0; c <= AreaTiles; c++)
            _canvas.Children.Add(new Line { X1 = c * _tileW * z, Y1 = 0, X2 = c * _tileW * z, Y2 = _canvas.Height, Stroke = Brushes.DimGray, StrokeThickness = 1, Opacity = 0.5 });
        for (int r = 0; r <= AreaTiles; r++)
            _canvas.Children.Add(new Line { X1 = 0, Y1 = r * _tileH * z, X2 = _canvas.Width, Y2 = r * _tileH * z, Stroke = Brushes.DimGray, StrokeThickness = 1, Opacity = 0.5 });
        // The anchor cell and its bottom line.
        var anchor = new Rectangle { Width = _tileW * z, Height = _tileH * z, Stroke = Brushes.Gold, StrokeThickness = 2, StrokeDashArray = new DoubleCollection { 3, 2 } };
        Canvas.SetLeft(anchor, AnchorLeft * z);
        Canvas.SetTop(anchor, AnchorTop * z);
        _canvas.Children.Add(anchor);
        _canvas.Children.Add(new Line { X1 = 0, Y1 = (AnchorTop + _tileH) * z, X2 = _canvas.Width, Y2 = (AnchorTop + _tileH) * z, Stroke = Brushes.IndianRed, StrokeThickness = 1.5 });
        if (Pose is not { } pose) return;
        // The pictures, in the game's paint order.
        foreach (var (step, index) in StairLook.PaintOrder(pose))
        {
            var size = StairLook.PictureSize(step, _sprites, _tileW, _tileH);
            FrameworkElement picture = StairLook.FirstFrameOf(step, _sprites) is { } bitmap
                ? new Image { Source = bitmap, Width = size.Width * z, Height = size.Height * z, Stretch = Stretch.Fill }
                : new Border { Width = size.Width * z, Height = size.Height * z, Background = new SolidColorBrush(Color.FromArgb(120, 120, 90, 60)), BorderBrush = Brushes.Tan, BorderThickness = new Thickness(1), Child = new TextBlock { Text = $"{index + 1}", Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.NearestNeighbor);
            picture.Tag = index;
            Canvas.SetLeft(picture, (AnchorLeft + step.PictureX) * z);
            Canvas.SetTop(picture, (AnchorTop + step.PictureY) * z);
            _canvas.Children.Add(picture);
        }
        // Every step's stand-on area, faint; the picked one bright, with its picture outlined.
        for (int i = 0; i < pose.Steps.Count; i++)
        {
            var step = pose.Steps[i];
            bool picked = i == _selectedStep;
            double lift = step.TopLift * LiftPixels;
            var top = new Rectangle
            {
                Width = step.SizeX * SpriteSizing.BaseUnit * z, Height = step.SizeY * SpriteSizing.BaseUnit * z,
                Stroke = picked ? Brushes.Cyan : new SolidColorBrush(Color.FromArgb(110, 0, 200, 220)), StrokeThickness = picked ? 2 : 1,
                Fill = picked ? new SolidColorBrush(Color.FromArgb(50, 0, 220, 255)) : null, IsHitTestVisible = false
            };
            Canvas.SetLeft(top, (AnchorLeft + step.FootX * SpriteSizing.BaseUnit - lift) * z);
            Canvas.SetTop(top, (AnchorTop + step.FootY * SpriteSizing.BaseUnit - lift) * z);
            _canvas.Children.Add(top);
            var label = new TextBlock { Text = (i + 1).ToString(), Foreground = picked ? Brushes.Cyan : Brushes.LightBlue, FontSize = 10, IsHitTestVisible = false };
            Canvas.SetLeft(label, (AnchorLeft + step.FootX * SpriteSizing.BaseUnit - lift) * z + 2);
            Canvas.SetTop(label, (AnchorTop + step.FootY * SpriteSizing.BaseUnit - lift) * z);
            _canvas.Children.Add(label);
            if (picked)
            {
                // Where it stands on the ground, dashed.
                var ground = new Rectangle
                {
                    Width = step.SizeX * SpriteSizing.BaseUnit * z, Height = step.SizeY * SpriteSizing.BaseUnit * z,
                    Stroke = Brushes.Cyan, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 2 }, Opacity = 0.6, IsHitTestVisible = false
                };
                Canvas.SetLeft(ground, (AnchorLeft + step.FootX * SpriteSizing.BaseUnit) * z);
                Canvas.SetTop(ground, (AnchorTop + step.FootY * SpriteSizing.BaseUnit) * z);
                _canvas.Children.Add(ground);
                var size = StairLook.PictureSize(step, _sprites, _tileW, _tileH);
                var outline = new Rectangle { Width = size.Width * z, Height = size.Height * z, Stroke = Brushes.White, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 2 }, IsHitTestVisible = false };
                Canvas.SetLeft(outline, (AnchorLeft + step.PictureX) * z);
                Canvas.SetTop(outline, (AnchorTop + step.PictureY) * z);
                _canvas.Children.Add(outline);
            }
        }
    }

    // ================= Mouse and keys =================

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _canvas.Focus();
        if (Pose is not { } pose) return;
        var p = e.GetPosition(_canvas);
        // The front-most picture under the mouse, else the stand-on area under it.
        int hit = _canvas.Children.OfType<FrameworkElement>().Reverse()
            .Where(c => c.Tag is int && new Rect(Canvas.GetLeft(c), Canvas.GetTop(c), c.Width, c.Height).Contains(p))
            .Select(c => (int)c.Tag).DefaultIfEmpty(-1).First();
        if (hit < 0)
        {
            double z = Zoom;
            for (int i = pose.Steps.Count - 1; i >= 0 && hit < 0; i--)
            {
                var s = pose.Steps[i];
                double lift = s.TopLift * LiftPixels;
                var rect = new Rect((AnchorLeft + s.FootX * SpriteSizing.BaseUnit - lift) * z, (AnchorTop + s.FootY * SpriteSizing.BaseUnit - lift) * z, s.SizeX * SpriteSizing.BaseUnit * z, s.SizeY * SpriteSizing.BaseUnit * z);
                if (rect.Contains(p)) hit = i;
            }
        }
        if (hit < 0) return;
        _selectedStep = hit;
        var step = pose.Steps[hit];
        _drag = (p, step.PictureX, step.PictureY, step.FootX, step.FootY);
        _canvas.CaptureMouse();
        RefreshStepList();
        ShowStepProps();
        Redraw();
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag is not { } drag || Step is not { } step || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_canvas);
        double dx = (p.X - drag.Start.X) / Zoom, dy = (p.Y - drag.Start.Y) / Zoom;
        bool picture = _dragPicture.IsChecked == true, foot = _dragFoot.IsChecked == true, both = _dragBoth.IsChecked == true;
        if (both)
        {
            // Together, in whole units, so the picture keeps standing on its area.
            int ux = (int)Math.Round(dx / SpriteSizing.BaseUnit), uy = (int)Math.Round(dy / SpriteSizing.BaseUnit);
            step.FootX = drag.FootX + ux;
            step.FootY = drag.FootY + uy;
            step.PictureX = drag.PicX + ux * SpriteSizing.BaseUnit;
            step.PictureY = drag.PicY + uy * SpriteSizing.BaseUnit;
        }
        else if (foot)
        {
            step.FootX = drag.FootX + (int)Math.Round(dx / SpriteSizing.BaseUnit);
            step.FootY = drag.FootY + (int)Math.Round(dy / SpriteSizing.BaseUnit);
        }
        else if (picture)
        {
            step.PictureX = drag.PicX + (int)Math.Round(dx);
            step.PictureY = drag.PicY + (int)Math.Round(dy);
        }
        KeepAboveBaseline(step);
        Redraw();
    }

    /// <summary>Nothing is drawn below the anchor cell's bottom edge (the picture is anchored there, as every sprite is).</summary>
    private void KeepAboveBaseline(StairStep step)
    {
        double height = StairLook.PictureSize(step, _sprites, _tileW, _tileH).Height;
        if (step.PictureY + height > _tileH) step.PictureY = (int)(_tileH - height);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        if (Step is not { } step) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        int dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0, dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
        if (dx != 0 || dy != 0)
        {
            if (ctrl) { step.FootX += dx; step.FootY += dy; }
            else { step.PictureX += dx * (shift ? 4 : 1); step.PictureY += dy * (shift ? 4 : 1); KeepAboveBaseline(step); }
        }
        else if (e.Key == Key.PageUp) step.TopLift = Math.Min(40, step.TopLift + 1);
        else if (e.Key == Key.PageDown) step.TopLift = Math.Max(1, step.TopLift - 1);
        else if (e.Key == Key.Delete) { DeleteStep(); e.Handled = true; return; }
        else return;
        e.Handled = true;
        RefreshStepList();
        ShowStepProps();
        Redraw();
    }

    // ================= Building blocks =================

    private sealed record SpriteChoice(string Group, string Name, string Display);

    private List<SpriteChoice> SpriteChoices()
    {
        var choices = new List<SpriteChoice> { new("", "", "(none)") };
        foreach (var group in _sprites.Groups.Where(g => g.Name != SpriteDatabase.TilesetGroupName && g.Name != SpriteDatabase.FontsGroupName))
            choices.AddRange(group.Sprites.Select(s => new SpriteChoice(group.Name, s.Name, $"{s.Name}   ({group.Name})")));
        return choices;
    }

    private FrameworkElement IntField(string label, Func<int> get, Action<int> set)
    {
        var box = new TextBox { Width = 60, Text = get().ToString() };
        void Commit()
        {
            if (int.TryParse(box.Text.Trim(), out int v)) { set(v); RefreshStepList(); Redraw(); }
            box.Text = get().ToString();
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return Labeled(label, box);
    }

    private static FrameworkElement Field(string label, string value, Action<string> set, double width)
    {
        var box = new TextBox { Width = width, Text = value };
        box.LostFocus += (_, _) => set(box.Text.Trim());
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) set(box.Text.Trim()); };
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
