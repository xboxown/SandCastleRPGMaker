using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using SandcastleRpgMaker.AI;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Tools > Sprite Composer (sprite generator A): a character made from a skeleton and parts, posed in every direction,
/// out comes a whole set of sprites - stand, walk, attack, sit, sleep, dead; south, north, west, east.
/// Compose: knobs, colours and words ("tall, stout, green skin, red cloak, axe") - only what it has bones and parts for;
/// words it can't draw are told back. Skeleton: the bones in each direction, dragged or typed, over a reference picture
/// (trace your Ultima art). Poses: each frame's turns, dragged. Parts: pictures cut from a reference (or the AI Artist's)
/// pinned to bones, recoloured by the recipe. Review: every frame, untick what you don't want, into the Sprite Editor.
/// Libraries (Ultima 6 / 7 / 8, each in versions) live on this PC (ComposerLibraries).
/// </summary>
public partial class SpriteComposerWindow : Window
{
    private readonly SpriteDatabase _sprites;
    private List<ComposerLibrary> _libraries;
    private ComposerLibrary _lib;
    private readonly ComposerRecipe _recipe = new();

    private readonly ComboBox _libraryBox = new() { Width = 170 };
    private readonly TextBlock _libraryState = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Opacity = 0.75 };
    private readonly TabControl _tabs = new();

    // the sprite's look (shared by the preview and the review)
    private readonly Slider _spriteHeight = new() { Minimum = 12, Maximum = 128, Value = 48, IsSnapToTickEnabled = true, TickFrequency = 1, Width = 140 };
    private readonly ComboBox _colours = new() { ItemsSource = new[] { "As drawn", "64", "32", "16", "Ultima 6's 16 (EGA)" }, SelectedIndex = 2, Width = 140 };
    private readonly CheckBox _outline = new() { Content = "Dark outline", Margin = new Thickness(0, 4, 0, 0) };

    // compose tab
    private readonly TextBox _words = new() { Height = 54, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Text = "tall, red cloak, green shirt, brown pants, sword" };
    private readonly TextBlock _wordsResult = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Dictionary<string, Slider> _knobs = new();
    private readonly Dictionary<string, Button> _colourButtons = new();
    private readonly Dictionary<string, CheckBox> _optionBoxes = new();
    private readonly WrapPanel _partOptions = new();
    private readonly ComboBox _weaponShape = new() { ItemsSource = new[] { "Blade", "Axe", "Capsule" }, Width = 90 };
    private readonly ComboBox _previewPose = new() { Width = 110 };
    private readonly ComboBox _previewDirection = new() { ItemsSource = Composer.Directions, SelectedIndex = 0, Width = 90 };
    private readonly ComboBox _previewZoom = new() { ItemsSource = new[] { 2, 3, 4, 6, 8 }, SelectedIndex = 2, Width = 50 };
    private readonly Image _preview = new() { Stretch = Stretch.Fill };
    private readonly StackPanel _directionStrip = new() { Orientation = Orientation.Horizontal };
    private readonly DispatcherTimer _timer = new();
    private int _previewFrame;
    private bool _loading;

    public SpriteComposerWindow(SpriteDatabase sprites)
    {
        _sprites = sprites;
        Title = "Sprite Composer - characters from a skeleton and parts, every pose and direction";
        Width = 1320;
        Height = 880;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _libraries = ComposerLibraries.All();
        string style = ProjectSession.IsUltima6 ? "Ultima 6" : ProjectSession.IsUltima8 ? "Ultima 8" : "Ultima 7";
        _lib = _libraries.Where(l => l.Style == style).OrderByDescending(l => l.Version).FirstOrDefault() ?? _libraries[0];

        var root = new DockPanel { Margin = new Thickness(8) };
        Content = root;
        var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(bar, Dock.Top);
        root.Children.Add(bar);
        bar.Children.Add(new TextBlock { Text = "Library ", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold });
        bar.Children.Add(_libraryBox);
        var newVersion = new Button { Content = "New version", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1), ToolTip = "A copy of this library as the next version: change it freely, the older one stays as it was" };
        newVersion.Click += (_, _) => { _lib = ComposerLibraries.NewVersion(_lib); RefreshLibraries(); };
        bar.Children.Add(newVersion);
        var save = new Button { Content = "Save library", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1), ToolTip = "Saves the skeleton, poses and parts of this library version" };
        save.Click += (_, _) => { ComposerLibraries.Save(_lib); _libraryState.Text = $"Saved {_lib.FileName}"; };
        bar.Children.Add(save);
        var builtIn = new Button { Content = "Built-in poses", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(8, 1, 8, 1), ToolTip = "Puts back any built-in pose (Stand, Walk, Attack, Sit, Sleep, Dead) this library doesn't have" };
        builtIn.Click += (_, _) => { foreach (var p in ComposerLibrary.BuiltInPoses().Where(p => _lib.Poses.All(x => x.Name != p.Name))) _lib.Poses.Add(p); RefreshPoseLists(); };
        bar.Children.Add(builtIn);
        bar.Children.Add(_libraryState);
        _libraryBox.SelectionChanged += (_, _) =>
        {
            if (_loading || _libraryBox.SelectedItem is not LibraryChoice choice) return;
            _lib = choice.Library;
            LibraryChanged();
        };

        root.Children.Add(_tabs);
        _tabs.Items.Add(new TabItem { Header = "Compose", Content = BuildComposeTab() });
        _tabs.Items.Add(new TabItem { Header = "Skeleton", Content = BuildSkeletonTab() });
        _tabs.Items.Add(new TabItem { Header = "Poses", Content = BuildPosesTab() });
        _tabs.Items.Add(new TabItem { Header = "Parts", Content = BuildPartsTab() });
        _tabs.Items.Add(new TabItem { Header = "Review and make", Content = BuildReviewTab() });
        _tabs.SelectionChanged += (_, e) => { if (e.Source == _tabs) RedrawAll(); };

        _timer.Tick += (_, _) => { _previewFrame++; ShowPreview(); };
        _timer.Interval = TimeSpan.FromMilliseconds(150);
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        Closing += (_, e) =>
        {
            var answer = MessageBox.Show(this, "Save the library (skeleton, poses, parts) before closing?", "Sprite Composer", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (answer == MessageBoxResult.Cancel) e.Cancel = true;
            else if (answer == MessageBoxResult.Yes) ComposerLibraries.Save(_lib);
        };

        RefreshLibraries();
        _words.Text = "";
        ApplyStyleLook();
    }

    private sealed record LibraryChoice(ComposerLibrary Library)
    {
        public override string ToString() => $"{Library.Style} v{Library.Version}";
    }

    private void RefreshLibraries()
    {
        _loading = true;
        _libraries = ComposerLibraries.All();
        var match = _libraries.FirstOrDefault(l => l.Style == _lib.Style && l.Version == _lib.Version);
        if (match is not null && !ReferenceEquals(match, _lib)) _libraries[_libraries.IndexOf(match)] = _lib; // keep the one being edited
        _libraryBox.ItemsSource = _libraries.Select(l => new LibraryChoice(l)).ToList();
        _libraryBox.SelectedItem = ((List<LibraryChoice>)_libraryBox.ItemsSource).FirstOrDefault(c => ReferenceEquals(c.Library, _lib));
        _loading = false;
        LibraryChanged();
    }

    private void LibraryChanged()
    {
        _libraryState.Text = $"{_lib.Bones().Count} bones, {_lib.Poses.Count} poses, {_lib.Parts.Count} parts - {_lib.Notes}";
        ApplyStyleLook();
        RefreshPoseLists();
        RefreshBoneList();
        RefreshPartList();
        RefreshPartOptions();
        RedrawAll();
    }

    /// <summary>The style's size and colours: Ultima 6 small in its sixteen colours with an outline; 7 middling; 8 big.</summary>
    private void ApplyStyleLook()
    {
        switch (_lib.Style)
        {
            case "Ultima 6": _spriteHeight.Value = 24; _colours.SelectedIndex = 4; _outline.IsChecked = true; break;
            case "Ultima 8": _spriteHeight.Value = 64; _colours.SelectedIndex = 1; _outline.IsChecked = false; break;
            default: _spriteHeight.Value = 48; _colours.SelectedIndex = 2; _outline.IsChecked = false; break;
        }
    }

    private void RedrawAll()
    {
        ShowPreview();
        ShowDirectionStrip();
        RedrawFigureCanvases();
    }

    // ---------- Compose ----------

    private UIElement BuildComposeTab()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(360) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var left = new StackPanel { Margin = new Thickness(0, 4, 10, 0) };
        grid.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        void Head(string t) => left.Children.Add(new TextBlock { Text = t, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) });

        Head("Words");
        left.Children.Add(new TextBlock { Text = "Comma between each: tall / short / stout / thin, big head, long legs, bald, no cloak, \"green skin\", \"red cloak\", \"blue tunic\", axe / sword / staff...", TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.75 });
        left.Children.Add(_words);
        var apply = new Button { Content = "Use the words", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 3, 0, 0) };
        apply.Click += (_, _) => UseWords();
        left.Children.Add(apply);
        left.Children.Add(_wordsResult);

        Head("Body");
        foreach (var (key, label, min, max) in new[] { ("Height", "Height", 0.6, 1.4), ("Build", "Build (thin - stout)", 0.7, 1.5), ("HeadSize", "Head", 0.7, 1.4), ("ArmLength", "Arms", 0.7, 1.3), ("LegLength", "Legs", 0.7, 1.3) })
        {
            var s = new Slider { Minimum = min, Maximum = max, Value = 1, Width = 170, TickFrequency = 0.01, IsSnapToTickEnabled = true };
            var value = new TextBlock { Width = 36, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
            s.ValueChanged += (_, _) => { value.Text = s.Value.ToString("0.00"); if (!_loading) { KnobsToRecipe(); RedrawAll(); } };
            value.Text = "1.00";
            _knobs[key] = s;
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
            row.Children.Add(new TextBlock { Text = label, Width = 130, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(s);
            row.Children.Add(value);
            left.Children.Add(row);
        }

        Head("Colours (click to change)");
        var colourGrid = new UniformGrid { Columns = 2 };
        foreach (var region in Composer.Regions)
        {
            var button = new Button { Margin = new Thickness(1), Padding = new Thickness(4, 1, 4, 1), HorizontalContentAlignment = HorizontalAlignment.Left };
            button.Click += (_, _) => PickColour(region, button);
            _colourButtons[region] = button;
            colourGrid.Children.Add(button);
        }
        left.Children.Add(colourGrid);

        Head("Wearing");
        var options = new WrapPanel();
        foreach (var option in new[] { "Hair", "Cloak", "Belt", "Weapon" })
        {
            var box = new CheckBox { Content = option, Margin = new Thickness(0, 0, 10, 0) };
            box.Click += (_, _) => { if (box.IsChecked == true) _recipe.Options.Add(option); else _recipe.Options.Remove(option); RedrawAll(); };
            _optionBoxes[option] = box;
            options.Children.Add(box);
        }
        left.Children.Add(options);
        var weaponRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        weaponRow.Children.Add(new TextBlock { Text = "Weapon shape ", VerticalAlignment = VerticalAlignment.Center });
        weaponRow.Children.Add(_weaponShape);
        _weaponShape.SelectionChanged += (_, _) => { if (_weaponShape.SelectedItem is string w) { _recipe.WeaponShape = w; RedrawAll(); } };
        left.Children.Add(weaponRow);
        left.Children.Add(new TextBlock { Text = "Parts that are a choice (Parts tab > Option):", FontSize = 11, Opacity = 0.75, Margin = new Thickness(0, 4, 0, 0) });
        left.Children.Add(_partOptions);

        Head("The sprite");
        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal };
        var sizeValue = new TextBlock { Width = 40, VerticalAlignment = VerticalAlignment.Center };
        _spriteHeight.ValueChanged += (_, _) => { sizeValue.Text = $"{_spriteHeight.Value:0} px"; RedrawAll(); };
        sizeRow.Children.Add(new TextBlock { Text = "Height ", Width = 60, VerticalAlignment = VerticalAlignment.Center });
        sizeRow.Children.Add(_spriteHeight);
        sizeRow.Children.Add(sizeValue);
        left.Children.Add(sizeRow);
        var colourRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        colourRow.Children.Add(new TextBlock { Text = "Colours ", Width = 60, VerticalAlignment = VerticalAlignment.Center });
        colourRow.Children.Add(_colours);
        _colours.SelectionChanged += (_, _) => RedrawAll();
        left.Children.Add(colourRow);
        _outline.Click += (_, _) => RedrawAll();
        left.Children.Add(_outline);

        // the preview
        var right = new DockPanel();
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        var previewBar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
        DockPanel.SetDock(previewBar, Dock.Top);
        previewBar.Children.Add(new TextBlock { Text = "Pose ", VerticalAlignment = VerticalAlignment.Center });
        previewBar.Children.Add(_previewPose);
        previewBar.Children.Add(new TextBlock { Text = "  Direction ", VerticalAlignment = VerticalAlignment.Center });
        previewBar.Children.Add(_previewDirection);
        previewBar.Children.Add(new TextBlock { Text = "  Zoom ", VerticalAlignment = VerticalAlignment.Center });
        previewBar.Children.Add(_previewZoom);
        _previewPose.SelectionChanged += (_, _) => { _previewFrame = 0; ShowPreview(); };
        _previewDirection.SelectionChanged += (_, _) => ShowPreview();
        _previewZoom.SelectionChanged += (_, _) => ShowPreview();
        right.Children.Add(previewBar);
        var strip = new GroupBox { Header = "Standing, every direction", Content = new ScrollViewer { Content = _directionStrip, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        DockPanel.SetDock(strip, Dock.Bottom);
        right.Children.Add(strip);
        RenderOptions.SetBitmapScalingMode(_preview, BitmapScalingMode.NearestNeighbor);
        right.Children.Add(new Border { Background = CheckerBrush(), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = new ScrollViewer { Content = _preview, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } });

        RecipeToControls();
        return grid;
    }

    private void UseWords()
    {
        var unknown = ComposerWords.Apply(_recipe, _words.Text);
        RecipeToControls();
        _wordsResult.Foreground = unknown.Count > 0 ? Brushes.DarkRed : Brushes.DarkGreen;
        _wordsResult.Text = unknown.Count > 0
            ? "Not drawn - nothing to draw them with: " + string.Join(", ", unknown) + ". (Add parts for them in the Parts tab, with an Option of that name.)"
            : "All used.";
        RedrawAll();
    }

    private void KnobsToRecipe()
    {
        _recipe.Height = _knobs["Height"].Value;
        _recipe.Build = _knobs["Build"].Value;
        _recipe.HeadSize = _knobs["HeadSize"].Value;
        _recipe.ArmLength = _knobs["ArmLength"].Value;
        _recipe.LegLength = _knobs["LegLength"].Value;
    }

    private void RecipeToControls()
    {
        _loading = true;
        _knobs["Height"].Value = _recipe.Height;
        _knobs["Build"].Value = _recipe.Build;
        _knobs["HeadSize"].Value = _recipe.HeadSize;
        _knobs["ArmLength"].Value = _recipe.ArmLength;
        _knobs["LegLength"].Value = _recipe.LegLength;
        foreach (var (region, button) in _colourButtons)
        {
            var colour = ComposerRenderer.ColourOf(_recipe, region);
            var swatch = new StackPanel { Orientation = Orientation.Horizontal };
            swatch.Children.Add(new Border { Width = 18, Height = 14, Background = new SolidColorBrush(colour), BorderBrush = Brushes.Black, BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 6, 0) });
            swatch.Children.Add(new TextBlock { Text = region });
            button.Content = swatch;
        }
        foreach (var (option, box) in _optionBoxes) box.IsChecked = _recipe.Options.Contains(option);
        _weaponShape.SelectedItem = _recipe.WeaponShape;
        _loading = false;
        RefreshPartOptions();
    }

    private void RefreshPartOptions()
    {
        _partOptions.Children.Clear();
        foreach (var option in _lib.Parts.Select(p => p.Option).Where(o => o.Length > 0).Distinct().OrderBy(o => o))
        {
            var box = new CheckBox { Content = option, IsChecked = _recipe.Options.Contains(option), Margin = new Thickness(0, 0, 10, 0) };
            box.Click += (_, _) => { if (box.IsChecked == true) _recipe.Options.Add(option); else _recipe.Options.Remove(option); RedrawAll(); };
            _partOptions.Children.Add(box);
        }
        if (_partOptions.Children.Count == 0) _partOptions.Children.Add(new TextBlock { Text = "(none yet)", Opacity = 0.6, FontSize = 11 });
    }

    /// <summary>A colour: the named colours the words know, or any as #RRGGBB.</summary>
    private void PickColour(string region, Button anchor)
    {
        var popup = new Popup { PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false };
        var panel = new StackPanel { Background = Brushes.White, Margin = new Thickness(0) };
        var swatches = new UniformGrid { Columns = 8, Margin = new Thickness(4) };
        foreach (var (name, hex) in ComposerWords.ColourNames.GroupBy(c => c.Value).Select(g => (g.First().Key, g.Key)))
        {
            var b = new Button { Width = 22, Height = 22, Margin = new Thickness(1), ToolTip = name, Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)) };
            b.Click += (_, _) => { _recipe.Colours[region] = hex; popup.IsOpen = false; RecipeToControls(); RedrawAll(); };
            swatches.Children.Add(b);
        }
        panel.Children.Add(swatches);
        var hexRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
        var hexBox = new TextBox { Width = 80, Text = _recipe.Colours.TryGetValue(region, out var now) ? now : "#808080" };
        var ok = new Button { Content = "Use", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(6, 0, 6, 0) };
        ok.Click += (_, _) =>
        {
            try { ColorConverter.ConvertFromString(hexBox.Text.Trim()); _recipe.Colours[region] = hexBox.Text.Trim().ToUpperInvariant(); popup.IsOpen = false; RecipeToControls(); RedrawAll(); }
            catch (FormatException) { hexBox.Background = Brushes.MistyRose; }
        };
        hexRow.Children.Add(new TextBlock { Text = "#RRGGBB ", VerticalAlignment = VerticalAlignment.Center });
        hexRow.Children.Add(hexBox);
        hexRow.Children.Add(ok);
        panel.Children.Add(hexRow);
        popup.Child = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = panel };
        popup.IsOpen = true;
    }

    // ---------- making frames into sprites ----------

    private int SpriteHeight => (int)_spriteHeight.Value;

    /// <summary>Frames as the sprite will be: the colours cut down together (one palette for the whole set), an outline.</summary>
    private List<PixelImage> Finish(List<(int W, int H, byte[] Rgba)> frames)
    {
        byte[]? palette = _colours.SelectedIndex switch
        {
            4 => SpriteMaker.Ega16,
            1 or 2 or 3 => SpriteMaker.MedianCut(frames.SelectMany(f => f.Rgba).ToArray(), _colours.SelectedIndex == 1 ? 64 : _colours.SelectedIndex == 2 ? 32 : 16),
            _ => null
        };
        var result = new List<PixelImage>();
        foreach (var f in frames)
        {
            var rgba = (byte[])f.Rgba.Clone();
            if (palette is not null) SpriteMaker.FitPalette(rgba, palette);
            var image = new PixelImage(f.W, f.H, rgba);
            result.Add(_outline.IsChecked == true ? SpriteMaker.AddOutline(image) : image);
        }
        return result;
    }

    private ComposerPose? PreviewPose => _lib.Poses.FirstOrDefault(p => p.Name == _previewPose.SelectedItem as string) ?? _lib.Poses.FirstOrDefault();

    private void ShowPreview()
    {
        if (_tabs.SelectedIndex != 0 || !IsLoaded && _preview.Source is not null) return;
        var pose = PreviewPose;
        string direction = _previewDirection.SelectedItem as string ?? "South";
        var frames = pose?.Frames.Count > 0 ? pose.Frames : new List<ComposerPoseFrame> { new() };
        int index = pose is { Loop: false } && _previewFrame >= frames.Count ? frames.Count - 1 : _previewFrame % frames.Count;
        if (pose is { Loop: false } && _previewFrame > frames.Count + 4) _previewFrame = 0;
        var frame = frames[index];
        _timer.Interval = TimeSpan.FromMilliseconds(Math.Max(40, frame.DurationMs));
        var raw = ComposerRenderer.RenderFrame(_lib, direction, frame, _recipe, SpriteHeight);
        var image = Finish(new List<(int, int, byte[])> { raw })[0];
        int zoom = _previewZoom.SelectedItem is int z ? z : 4;
        _preview.Source = ComposerRenderer.ToBitmap(image.Width, image.Height, image.Rgba);
        _preview.Width = image.Width * zoom;
        _preview.Height = image.Height * zoom;
    }

    private void ShowDirectionStrip()
    {
        if (_tabs.SelectedIndex != 0) return;
        _directionStrip.Children.Clear();
        var stand = _lib.Poses.FirstOrDefault(p => p.Name == "Stand")?.Frames.FirstOrDefault();
        var raws = Composer.Directions.Select(d => ComposerRenderer.RenderFrame(_lib, d, stand, _recipe, SpriteHeight)).ToList();
        var images = Finish(raws);
        for (int i = 0; i < images.Count; i++)
        {
            var panel = new StackPanel { Margin = new Thickness(4) };
            var img = new Image { Source = ComposerRenderer.ToBitmap(images[i].Width, images[i].Height, images[i].Rgba), Width = images[i].Width * 2, Height = images[i].Height * 2 };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.NearestNeighbor);
            panel.Children.Add(img);
            panel.Children.Add(new TextBlock { Text = Composer.Directions[i], HorizontalAlignment = HorizontalAlignment.Center });
            _directionStrip.Children.Add(panel);
        }
    }

    internal static Brush CheckerBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xa8, 0xa8, 0xa8)), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xa8, 0xa8, 0xa8)), null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        return new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
    }
}

internal static class ComposerLibraryExtensions
{
    /// <summary>Every bone name of the library (from all directions).</summary>
    public static List<string> Bones(this ComposerLibrary lib) => lib.Skeleton.Values.SelectMany(b => b).Select(b => b.Name).Distinct().ToList();
}
