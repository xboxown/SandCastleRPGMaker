using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.AI;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class SpriteComposerWindow
{
    // ---------- Parts: cut from a picture, pinned to a bone ----------

    private readonly ListBox _partList = new() { Height = 300 };
    private readonly StackPanel _partProps = new();
    private readonly Image _cutImage = new() { Stretch = Stretch.Fill };
    private readonly Canvas _cutCanvas = new() { Background = Brushes.Transparent };
    private readonly Rectangle _cutRect = new() { Stroke = Brushes.Red, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 2 }, Visibility = Visibility.Collapsed };
    private readonly System.Windows.Shapes.Path _pivotMark = new() { Stroke = Brushes.Blue, StrokeThickness = 2, Visibility = Visibility.Collapsed, Data = Geometry.Parse("M -6,0 L 6,0 M 0,-6 L 0,6") };
    private readonly ComboBox _cutZoom = new() { ItemsSource = new[] { 1, 2, 3, 4, 6, 8 }, SelectedIndex = 3, Width = 50 };
    private readonly ComboBox _cutBone = new() { Width = 110 };
    private readonly ComboBox _cutDirection = new() { ItemsSource = new[] { "(every)", "South", "North", "West", "East" }, SelectedIndex = 0, Width = 80 };
    private readonly TextBox _cutFigureHeight = new() { Width = 50, Text = "64", ToolTip = "How tall a whole figure is in this picture, in its pixels: the part is scaled to match the skeleton" };
    private readonly TextBox _cutOption = new() { Width = 90, ToolTip = "Empty: always worn. A name (Helmet, Shield...): worn when the recipe ticks it - and the words can name it" };
    private readonly TextBox _cutName = new() { Width = 110 };
    private readonly CheckBox _cutTint = new() { Content = "Take the recipe's colour", ToolTip = "Recoloured with the bone's colour (its own light and dark kept): one part, any colour" };
    private readonly Slider _cutBackground = new() { Minimum = 0, Maximum = 120, Value = 30, Width = 120, IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly CheckBox _settingJoint = new() { Content = "Click sets the joint", ToolTip = "The point of the part that sits on the bone's start (a shoulder, a hip, the neck)" };
    private BitmapSource? _cutSource;
    private Point? _cutFrom;
    private Int32Rect _cut;
    private Point _pivot;

    private int CutZoom => _cutZoom.SelectedItem is int z ? z : 4;

    private UIElement BuildPartsTab()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(280) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new StackPanel { Margin = new Thickness(0, 4, 8, 0) };
        grid.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        left.Children.Add(new TextBlock { Text = "Parts of this library", FontWeight = FontWeights.Bold });
        left.Children.Add(_partList);
        var delete = new Button { Content = "Delete the part", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(0, 3, 0, 0) };
        delete.Click += (_, _) =>
        {
            if (_partList.SelectedItem is not PartChoice choice) return;
            _lib.Parts.Remove(choice.Part);
            ComposerRenderer.ForgetPart(choice.Part);
            RefreshPartList();
            RefreshPartOptions();
        };
        left.Children.Add(delete);
        left.Children.Add(_partProps);
        _partList.SelectionChanged += (_, _) => ShowPartProps();

        var right = new DockPanel();
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        var bar1 = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
        DockPanel.SetDock(bar1, Dock.Top);
        var load = new Button { Content = "Picture...", Padding = new Thickness(8, 1, 8, 1), ToolTip = "A reference to cut parts from: your own art, an Ultima sprite sheet, an AI Artist picture" };
        load.Click += (_, _) => LoadCutPicture();
        bar1.Children.Add(load);
        bar1.Children.Add(Label("  Zoom "));
        bar1.Children.Add(_cutZoom);
        bar1.Children.Add(Label("  Background "));
        bar1.Children.Add(_cutBackground);
        bar1.Children.Add(Label("  Figure height in it "));
        bar1.Children.Add(_cutFigureHeight);
        bar1.Children.Add(Label(" px  "));
        bar1.Children.Add(_settingJoint);
        right.Children.Add(bar1);
        var bar2 = new WrapPanel { Margin = new Thickness(0, 2, 0, 4) };
        DockPanel.SetDock(bar2, Dock.Top);
        bar2.Children.Add(Label("Bone "));
        bar2.Children.Add(_cutBone);
        bar2.Children.Add(Label("  Direction "));
        bar2.Children.Add(_cutDirection);
        bar2.Children.Add(Label("  Name "));
        bar2.Children.Add(_cutName);
        bar2.Children.Add(Label("  Option "));
        bar2.Children.Add(_cutOption);
        bar2.Children.Add(Label("  "));
        bar2.Children.Add(_cutTint);
        var addPart = new Button { Content = "Add the part", FontWeight = FontWeights.Bold, Padding = new Thickness(10, 1, 10, 1), Margin = new Thickness(8, 0, 0, 0) };
        addPart.Click += (_, _) => AddCutPart();
        bar2.Children.Add(addPart);
        right.Children.Add(bar2);
        var help = new TextBlock
        {
            Text = "Drag a box round the part (drawn as at rest: an arm hanging down, the head upright). Tick \"Click sets the joint\" and click where it joins the body. The background inside the box (the colour at its edge) is taken away. Then pick its bone and Add the part.",
            TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 4)
        };
        DockPanel.SetDock(help, Dock.Top);
        right.Children.Add(help);

        RenderOptions.SetBitmapScalingMode(_cutImage, BitmapScalingMode.NearestNeighbor);
        _cutCanvas.Children.Add(_cutImage);
        _cutCanvas.Children.Add(_cutRect);
        _cutCanvas.Children.Add(_pivotMark);
        right.Children.Add(new Border { Background = CheckerBrush(), Child = new ScrollViewer { Content = _cutCanvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });
        _cutZoom.SelectionChanged += (_, _) => PlaceCutPicture();
        _cutCanvas.MouseLeftButtonDown += (_, e) =>
        {
            if (_cutSource is null) return;
            var p = e.GetPosition(_cutCanvas);
            var px = new Point(Math.Floor(p.X / CutZoom), Math.Floor(p.Y / CutZoom));
            if (_settingJoint.IsChecked == true) { _pivot = px; PlaceCutMarks(); return; }
            _cutFrom = px;
            _cutCanvas.CaptureMouse();
        };
        _cutCanvas.MouseMove += (_, e) =>
        {
            if (_cutFrom is not { } from || e.LeftButton != MouseButtonState.Pressed || _cutSource is null) return;
            var p = e.GetPosition(_cutCanvas);
            int x0 = (int)Math.Clamp(Math.Min(from.X, Math.Floor(p.X / CutZoom)), 0, _cutSource.PixelWidth - 1), y0 = (int)Math.Clamp(Math.Min(from.Y, Math.Floor(p.Y / CutZoom)), 0, _cutSource.PixelHeight - 1);
            int x1 = (int)Math.Clamp(Math.Max(from.X, Math.Floor(p.X / CutZoom)), 0, _cutSource.PixelWidth - 1), y1 = (int)Math.Clamp(Math.Max(from.Y, Math.Floor(p.Y / CutZoom)), 0, _cutSource.PixelHeight - 1);
            _cut = new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1);
            if (_pivot.X < x0 || _pivot.X > x1 || _pivot.Y < y0 || _pivot.Y > y1) _pivot = new Point((x0 + x1) / 2, y0); // a joint at the top middle until one is set
            PlaceCutMarks();
        };
        _cutCanvas.MouseLeftButtonUp += (_, _) => { _cutFrom = null; _cutCanvas.ReleaseMouseCapture(); };
        return grid;
    }

    private static TextBlock Label(string text) => new() { Text = text, VerticalAlignment = VerticalAlignment.Center };

    private sealed record PartChoice(ComposerPart Part)
    {
        public override string ToString() => $"{Part.Name} - {Part.Bone}" + (Part.Direction.Length > 0 ? $" ({Part.Direction})" : "") + (Part.Option.Length > 0 ? $" [{Part.Option}]" : "") + (Part.Tint ? " tinted" : "");
    }

    private void RefreshPartList()
    {
        _partList.ItemsSource = _lib.Parts.Select(p => new PartChoice(p)).ToList();
        _cutBone.ItemsSource = _lib.Bones();
        if (_cutBone.SelectedItem is null) _cutBone.SelectedIndex = 0;
    }

    private void ShowPartProps()
    {
        _partProps.Children.Clear();
        if (_partList.SelectedItem is not PartChoice { Part: var part }) return;
        if (ComposerRenderer.PartBitmap(part, Colors.Gray) is { } bitmap)
        {
            var img = new Image { Source = bitmap, Width = bitmap.PixelWidth * 3, Height = bitmap.PixelHeight * 3, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 4) };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            _partProps.Children.Add(new Border { Background = CheckerBrush(), Child = img, HorizontalAlignment = HorizontalAlignment.Left });
        }
        void Row(string label, FrameworkElement control) { var r = new DockPanel { Margin = new Thickness(0, 1, 0, 1) }; r.Children.Add(new TextBlock { Text = label, Width = 80 }); r.Children.Add(control); _partProps.Children.Add(r); }
        void Changed() { ComposerRenderer.ForgetPart(part); RefreshPartListKeep(); RefreshPartOptions(); }
        var name = new TextBox { Text = part.Name };
        name.LostFocus += (_, _) => { part.Name = name.Text.Trim(); Changed(); };
        Row("Name", name);
        var bone = new ComboBox { ItemsSource = _lib.Bones(), SelectedItem = part.Bone };
        bone.SelectionChanged += (_, _) => { part.Bone = bone.SelectedItem as string ?? part.Bone; Changed(); };
        Row("Bone", bone);
        var direction = new ComboBox { ItemsSource = new[] { "(every)", "South", "North", "West", "East" }, SelectedItem = part.Direction.Length == 0 ? "(every)" : part.Direction };
        direction.SelectionChanged += (_, _) => { part.Direction = direction.SelectedItem as string is "(every)" or null ? "" : (string)direction.SelectedItem; Changed(); };
        Row("Direction", direction);
        var option = new TextBox { Text = part.Option };
        option.LostFocus += (_, _) => { part.Option = option.Text.Trim(); Changed(); };
        Row("Option", option);
        var tint = new CheckBox { Content = "Take the recipe's colour", IsChecked = part.Tint };
        tint.Click += (_, _) => { part.Tint = tint.IsChecked == true; Changed(); };
        Row("", tint);
        var scale = new TextBox { Text = part.Scale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) };
        scale.LostFocus += (_, _) => { if (double.TryParse(scale.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double s) && s > 0) { part.Scale = s; Changed(); } };
        Row("Scale", scale);
    }

    private void RefreshPartListKeep()
    {
        var keep = (_partList.SelectedItem as PartChoice)?.Part;
        RefreshPartList();
        _partList.SelectedItem = ((List<PartChoice>)_partList.ItemsSource).FirstOrDefault(c => ReferenceEquals(c.Part, keep));
    }

    private void LoadCutPicture()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Pictures|*.png;*.bmp;*.jpg;*.jpeg;*.gif|All files|*.*", Title = "A picture to cut parts from" };
        if (Directory.Exists(AiArtistWindow.OutputFolder)) dialog.InitialDirectory = AiArtistWindow.OutputFolder;
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(dialog.FileName);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            _cutSource = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            _cutSource.Freeze();
            _cutFigureHeight.Text = _cutSource.PixelHeight.ToString();
            _cut = Int32Rect.Empty;
            PlaceCutPicture();
        }
        catch (Exception ex) { MessageBox.Show(this, "That picture can't be read: " + ex.Message, "Sprite Composer"); }
    }

    private void PlaceCutPicture()
    {
        if (_cutSource is null) return;
        _cutImage.Source = _cutSource;
        _cutImage.Width = _cutSource.PixelWidth * CutZoom;
        _cutImage.Height = _cutSource.PixelHeight * CutZoom;
        _cutCanvas.Width = _cutImage.Width;
        _cutCanvas.Height = _cutImage.Height;
        PlaceCutMarks();
    }

    private void PlaceCutMarks()
    {
        _cutRect.Visibility = _pivotMark.Visibility = _cut.IsEmpty || _cut.Width == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (_cut.IsEmpty || _cut.Width == 0) return;
        Canvas.SetLeft(_cutRect, _cut.X * CutZoom);
        Canvas.SetTop(_cutRect, _cut.Y * CutZoom);
        _cutRect.Width = _cut.Width * CutZoom;
        _cutRect.Height = _cut.Height * CutZoom;
        Canvas.SetLeft(_pivotMark, (_pivot.X + 0.5) * CutZoom);
        Canvas.SetTop(_pivotMark, (_pivot.Y + 0.5) * CutZoom);
    }

    private void AddCutPart()
    {
        if (_cutSource is null || _cut.IsEmpty || _cut.Width < 1) { MessageBox.Show(this, "Load a picture and drag a box round the part first.", "Sprite Composer"); return; }
        if (_cutBone.SelectedItem is not string bone) { MessageBox.Show(this, "Pick the bone it goes on.", "Sprite Composer"); return; }
        if (!double.TryParse(_cutFigureHeight.Text, out double figure) || figure < 4) { MessageBox.Show(this, "How tall is a whole figure in this picture (pixels)?", "Sprite Composer"); return; }
        int w = _cut.Width, h = _cut.Height;
        var bgra = new byte[w * h * 4];
        _cutSource.CopyPixels(_cut, bgra, w * 4, 0);
        var rgba = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4) { rgba[i] = bgra[i + 2]; rgba[i + 1] = bgra[i + 1]; rgba[i + 2] = bgra[i]; rgba[i + 3] = bgra[i + 3]; }
        if (_cutBackground.Value > 0) SpriteMaker.RemoveBackground(rgba, w, h, (int)_cutBackground.Value);
        var part = new ComposerPart
        {
            Name = _cutName.Text.Trim().Length > 0 ? _cutName.Text.Trim() : bone,
            Bone = bone,
            Direction = _cutDirection.SelectedItem as string is "(every)" or null ? "" : (string)_cutDirection.SelectedItem,
            Png = Convert.ToBase64String(ComposerRenderer.Png(w, h, rgba)),
            PivotX = _pivot.X - _cut.X + 0.5,
            PivotY = _pivot.Y - _cut.Y + 0.5,
            Scale = Composer.ReferenceHeight / figure,
            Tint = _cutTint.IsChecked == true,
            Option = _cutOption.Text.Trim()
        };
        _lib.Parts.Add(part);
        RefreshPartList();
        RefreshPartOptions();
        if (part.Option.Length > 0) _recipe.Options.Add(part.Option);
        _partList.SelectedItem = ((List<PartChoice>)_partList.ItemsSource).Last();
        _libraryState.Text = $"Part \"{part.Name}\" pinned to {bone} - see it in Compose. Save library to keep it.";
    }

    // ---------- Review and make ----------

    private readonly WrapPanel _reviewPoses = new();
    private readonly WrapPanel _reviewDirections = new();
    private readonly TabControl _reviewTabs = new();
    private readonly TextBox _reviewName = new() { Width = 160, Text = "Hero" };
    private readonly ComboBox _reviewFolder = new() { Width = 170, IsEditable = true };
    private readonly TextBlock _reviewStatus = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Dictionary<(string Pose, string Direction), List<(PixelImage Image, int Ms, CheckBox Keep)>> _made = new();

    private UIElement BuildReviewTab()
    {
        var root = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
        var top = new StackPanel();
        DockPanel.SetDock(top, Dock.Top);
        root.Children.Add(top);
        var row1 = new WrapPanel();
        row1.Children.Add(Label("Poses: "));
        row1.Children.Add(_reviewPoses);
        top.Children.Add(row1);
        var row2 = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        row2.Children.Add(Label("Directions: "));
        foreach (var d in Composer.Directions) _reviewDirections.Children.Add(new CheckBox { Content = d, IsChecked = true, Margin = new Thickness(0, 0, 10, 0) });
        row2.Children.Add(_reviewDirections);
        var make = new Button { Content = "Make the frames", FontWeight = FontWeights.Bold, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(12, 0, 0, 0) };
        make.Click += (_, _) => MakeFrames();
        row2.Children.Add(make);
        top.Children.Add(row2);
        var row3 = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        row3.Children.Add(Label("Name "));
        row3.Children.Add(_reviewName);
        row3.Children.Add(Label("   Sprite Editor folder "));
        _reviewFolder.ItemsSource = _sprites.Groups.Where(g => !SpriteDatabase.IsLockedGroup(g.Name)).Select(g => g.Name).ToList();
        _reviewFolder.Text = "Composed";
        row3.Children.Add(_reviewFolder);
        var add = new Button { Content = "Add to Sprite Editor", FontWeight = FontWeights.Bold, Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Each pose and direction becomes an animated sprite \"Name Pose Direction\" of the ticked frames" };
        add.Click += (_, _) => AddMadeSprites();
        row3.Children.Add(add);
        top.Children.Add(row3);
        top.Children.Add(_reviewStatus);
        root.Children.Add(_reviewTabs);
        return root;
    }

    private void RefreshReviewPoses()
    {
        var ticked = _reviewPoses.Children.OfType<CheckBox>().Where(c => c.IsChecked != true).Select(c => c.Content as string).ToHashSet();
        _reviewPoses.Children.Clear();
        foreach (var pose in _lib.Poses) _reviewPoses.Children.Add(new CheckBox { Content = pose.Name, IsChecked = !ticked.Contains(pose.Name), Margin = new Thickness(0, 0, 10, 0) });
    }

    private void MakeFrames()
    {
        var poses = _lib.Poses.Where(p => _reviewPoses.Children.OfType<CheckBox>().Any(c => c.IsChecked == true && (string)c.Content == p.Name)).ToList();
        var directions = _reviewDirections.Children.OfType<CheckBox>().Where(c => c.IsChecked == true).Select(c => (string)c.Content).ToList();
        if (poses.Count == 0 || directions.Count == 0) { _reviewStatus.Text = "Tick at least one pose and one direction."; return; }
        var jobs = new List<(string Pose, string Direction, int Ms)>();
        var raws = new List<(int W, int H, byte[] Rgba)>();
        foreach (var pose in poses)
            foreach (var d in directions)
                foreach (var frame in pose.Frames)
                {
                    jobs.Add((pose.Name, d, frame.DurationMs));
                    raws.Add(ComposerRenderer.RenderFrame(_lib, d, frame, _recipe, SpriteHeight));
                }
        // one box round everything drawn: all the sprites the same size, the feet in the same place
        int w = raws[0].W, h = raws[0].H, x0 = w, y0 = h, x1 = -1, y1 = -1;
        foreach (var r in raws)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    if (r.Rgba[(y * w + x) * 4 + 3] > 0) { x0 = Math.Min(x0, x); x1 = Math.Max(x1, x); y0 = Math.Min(y0, y); y1 = Math.Max(y1, y); }
        if (x1 < 0) { _reviewStatus.Text = "Nothing was drawn."; return; }
        // keep the feet in the middle: the box is made even round the feet's column
        int feet = (int)Math.Round(ComposerRenderer.FeetX * SpriteHeight / Composer.ReferenceHeight);
        int half = Math.Max(feet - x0, x1 - feet) + 1;
        x0 = Math.Max(0, feet - half); x1 = Math.Min(w - 1, feet + half);
        int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        var cropped = raws.Select(r =>
        {
            var c = new byte[cw * ch * 4];
            for (int y = 0; y < ch; y++) Array.Copy(r.Rgba, ((y0 + y) * w + x0) * 4, c, y * cw * 4, cw * 4);
            return (cw, ch, c);
        }).ToList();
        var finished = Finish(cropped);

        _made.Clear();
        _reviewTabs.Items.Clear();
        for (int i = 0; i < jobs.Count; i++)
        {
            var key = (jobs[i].Pose, jobs[i].Direction);
            if (!_made.TryGetValue(key, out var list)) _made[key] = list = new();
            list.Add((finished[i], jobs[i].Ms, new CheckBox { IsChecked = true, HorizontalAlignment = HorizontalAlignment.Center }));
        }
        foreach (var pose in poses)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };
            foreach (var d in directions)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
                row.Children.Add(new TextBlock { Text = d, Width = 60, VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold });
                foreach (var (image, _, keep) in _made[(pose.Name, d)])
                {
                    var cell = new StackPanel { Margin = new Thickness(3) };
                    var img = new Image { Source = ComposerRenderer.ToBitmap(image.Width, image.Height, image.Rgba), Width = image.Width * 2, Height = image.Height * 2 };
                    RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
                    cell.Children.Add(new Border { Background = CheckerBrush(), Child = img });
                    cell.Children.Add(keep);
                    row.Children.Add(cell);
                }
                panel.Children.Add(row);
            }
            _reviewTabs.Items.Add(new TabItem { Header = pose.Name, Content = new ScrollViewer { Content = panel, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto } });
        }
        _reviewTabs.SelectedIndex = 0;
        _reviewStatus.Text = $"{jobs.Count} frames, each {finished[0].Width} x {finished[0].Height}. Untick any you don't want, then Add to Sprite Editor.";
    }

    private void AddMadeSprites()
    {
        if (_made.Count == 0) { _reviewStatus.Text = "Make the frames first."; return; }
        string name = _reviewName.Text.Trim().Length > 0 ? _reviewName.Text.Trim() : "Composed";
        string folderName = _reviewFolder.Text.Trim().Length > 0 ? _reviewFolder.Text.Trim() : "Composed";
        if (SpriteDatabase.IsLockedGroup(folderName)) { _reviewStatus.Text = $"\"{folderName}\" is one of the editor's own folders - pick another."; return; }
        string safe = string.Concat(name.Select(c => System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sandcastle Sprite Composer", $"{safe}_{DateTime.Now:yyyyMMdd_HHmmss}");
        Directory.CreateDirectory(dir);
        var group = _sprites.GetOrCreateGroup(folderName);
        int made = 0;
        foreach (var ((pose, direction), frames) in _made)
        {
            var kept = frames.Where(f => f.Keep.IsChecked == true).ToList();
            if (kept.Count == 0) continue;
            string spriteName = $"{name} {pose} {direction}";
            for (int n = 2; group.Sprites.Any(s => s.Name == spriteName); n++) spriteName = $"{name} {pose} {direction} {n}";
            var entry = new SpriteEntry { Name = spriteName, Loop = _lib.Poses.FirstOrDefault(p => p.Name == pose)?.Loop ?? true };
            for (int i = 0; i < kept.Count; i++)
            {
                string file = System.IO.Path.Combine(dir, $"{pose}_{direction}_{i + 1}.png");
                File.WriteAllBytes(file, ComposerRenderer.Png(kept[i].Image.Width, kept[i].Image.Height, kept[i].Image.Rgba));
                entry.Frames.Add(new SpriteFrame { FilePath = file, DurationMs = kept[i].Ms });
            }
            entry.FilePath = entry.Frames[0].FilePath;
            group.Sprites.Add(entry);
            made++;
        }
        _reviewStatus.Text = $"{made} sprites added to the Sprite Editor's folder \"{folderName}\" (\"{name} Walk South\"...). Save the project to keep them.";
    }
}
