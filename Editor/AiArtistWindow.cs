using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.AI;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Tools > AI Artist (sprite generator B): Stable Diffusion 1.5 on this PC's graphics card (SandcastleRpgMaker.AI.dll)
/// draws what the words say; the picture becomes a sprite - background taken away, cropped, shrunk to the sprite's
/// height, its colours cut down (or Ultima 6's sixteen), an outline if wanted - and goes into a Sprite Editor folder.
/// A style (Ultima 6 / 7 / 8) adds its own words. The same seed and words draw the same picture again. "Redraw a
/// picture" starts from one of yours instead of noise. Nothing leaves this PC: the model runs offline.
/// </summary>
public class AiArtistWindow : Window
{
    /// <summary>The model, loaded once per editor run (loading takes a while; generating after that is quick).</summary>
    private static StableDiffusion? _model;
    private static string? _modelFolder;

    private static readonly string SettingsFile = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SandcastleRpgMaker", "ai-artist.txt");
    private const string DefaultModelFolder = @"H:\SandcastleAI\sd15-fp16";
    public static readonly string OutputFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Sandcastle AI Artist");

    private sealed record ArtStyle(string Name, string Words, int Height, int Colours, bool Ega, bool Outline)
    {
        public override string ToString() => Name;
    }
    private static readonly ArtStyle[] Styles =
    {
        new("Ultima 7", "pixel art, top-down three-quarter view, detailed, earthy muted colours, one single character, full body, centered, plain white background", 48, 32, false, false),
        new("Ultima 6", "16-colour pixel art, top-down view, bold simple shapes, one single character, full body, centered, plain white background", 24, 16, true, true),
        new("Ultima 8", "isometric pixel art, dark gritty colours, detailed shading, one single character, full body, centered, plain white background", 64, 48, false, false),
        new("None - only my words", "", 48, 0, false, false),
    };
    private static readonly (string Label, string Words)[] Facings =
    {
        ("Facing you (south)", "facing the viewer, front view"),
        ("Facing away (north)", "seen from behind, back view"),
        ("Facing left (west)", "side view, facing left"),
        ("Facing right (east)", "side view, facing right"),
        ("(not said)", "")
    };

    private readonly SpriteDatabase _sprites;
    private readonly ComboBox _style = new() { ItemsSource = Styles, SelectedIndex = 0 };
    private readonly TextBox _prompt = new() { Height = 70, TextWrapping = TextWrapping.Wrap, AcceptsReturn = false, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Text = "an orc warrior with green skin, red cloak and a battle axe" };
    private readonly TextBox _negative = new() { Height = 44, TextWrapping = TextWrapping.Wrap, Text = "blurry, text, watermark, cropped, several people, frame, border, shadow on the ground" };
    private readonly ComboBox _facing = new() { SelectedIndex = 0 };
    private readonly Slider _steps = new() { Minimum = 10, Maximum = 50, Value = 25, IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly Slider _guidance = new() { Minimum = 1, Maximum = 15, Value = 7.5, IsSnapToTickEnabled = true, TickFrequency = 0.5 };
    private readonly TextBox _seed = new() { Text = "-1", Width = 110 };
    private readonly ComboBox _count = new() { ItemsSource = new[] { 1, 2, 3, 4 }, SelectedIndex = 1, Width = 50 };
    private readonly Slider _height = new() { Minimum = 8, Maximum = 160, Value = 48, IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly ComboBox _colours = new() { ItemsSource = new[] { "As drawn (full colour)", "64", "32", "16", "8", "Ultima 6's 16 (EGA)" }, SelectedIndex = 2 };
    private readonly Slider _background = new() { Minimum = 0, Maximum = 120, Value = 40, IsSnapToTickEnabled = true, TickFrequency = 1 };
    private readonly CheckBox _outline = new() { Content = "Dark outline round the sprite" };
    private readonly CheckBox _keepBiggest = new() { Content = "Keep only the biggest figure", IsChecked = true, ToolTip = "The AI sometimes draws two or three side by side: only the biggest stays" };
    private readonly Slider _strength = new() { Minimum = 0.1, Maximum = 1, Value = 0.6, IsSnapToTickEnabled = true, TickFrequency = 0.05 };
    private readonly TextBlock _startLabel = new() { Text = "(none - drawn from the words)", Opacity = 0.7, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _generate = new() { Content = "Draw", Padding = new Thickness(18, 4, 18, 4), FontWeight = FontWeights.Bold };
    private readonly Button _stop = new() { Content = "Stop", Padding = new Thickness(10, 4, 10, 4), IsEnabled = false, Margin = new Thickness(6, 0, 0, 0) };
    private readonly ProgressBar _progress = new() { Height = 14, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
    private readonly Image _big = new() { Width = 512, Height = 512, Stretch = Stretch.Uniform };
    private readonly Image _sprite = new() { Stretch = Stretch.None };
    private readonly ComboBox _zoom = new() { ItemsSource = new[] { 1, 2, 3, 4, 6, 8 }, SelectedIndex = 3, Width = 50 };
    private readonly WrapPanel _variants = new();
    private readonly ComboBox _folder = new() { IsEditable = true, Width = 180 };
    private readonly TextBox _name = new() { Width = 180 };
    private readonly TextBlock _modelLabel = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11 };

    private readonly List<GeneratedImage> _pictures = new();
    private GeneratedImage? _picked;
    private PixelImage? _pickedSprite;
    private byte[]? _startImage;
    private CancellationTokenSource? _cancel;

    public AiArtistWindow(SpriteDatabase sprites)
    {
        _sprites = sprites;
        Title = "AI Artist - sprites drawn from words, on this PC";
        Width = 1240;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(8) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Content = root;

        // ---- left: what to draw and how ----
        var left = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        root.Children.Add(new ScrollViewer { Content = left, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        void Head(string text) => left.Children.Add(new TextBlock { Text = text, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 8, 0, 2) });
        void Row(string label, FrameworkElement control, string tip)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), ToolTip = tip };
            row.Children.Add(new TextBlock { Text = label, Width = 110, VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(control);
            left.Children.Add(row);
        }
        TextBlock ValueOf(Slider s, string format)
        {
            var t = new TextBlock { Width = 36, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            void Show() => t.Text = s.Value.ToString(format);
            s.ValueChanged += (_, _) => Show();
            Show();
            return t;
        }
        FrameworkElement SliderRow(Slider s, string format)
        {
            var d = new DockPanel();
            var v = ValueOf(s, format);
            DockPanel.SetDock(v, Dock.Right);
            d.Children.Add(v);
            d.Children.Add(s);
            return d;
        }

        Head("What to draw");
        left.Children.Add(new TextBlock { Text = "Words - what it is and how it looks:", FontSize = 11, Opacity = 0.8 });
        left.Children.Add(_prompt);
        left.Children.Add(new TextBlock { Text = "Not - what it must not have:", FontSize = 11, Opacity = 0.8, Margin = new Thickness(0, 4, 0, 0) });
        left.Children.Add(_negative);
        Row("Style", _style, "Adds the style's own words and sets the sprite's size and colours (you can change them after).");
        _facing.ItemsSource = Facings.Select(f => f.Label).ToList();
        Row("Facing", _facing, "Which way it looks - added to the words.");

        Head("Drawing");
        Row("Steps", SliderRow(_steps, "0"), "How many times the picture is refined: more is finer and slower (20-30 is good).");
        Row("Follow words", SliderRow(_guidance, "0.0"), "How closely it follows the words (7-8 is usual; higher: stricter but harsher).");
        var seedRow = new StackPanel { Orientation = Orientation.Horizontal };
        seedRow.Children.Add(_seed);
        var dice = new Button { Content = "New", Margin = new Thickness(4, 0, 0, 0), Padding = new Thickness(6, 0, 6, 0), ToolTip = "-1: a new picture every time" };
        dice.Click += (_, _) => _seed.Text = "-1";
        seedRow.Children.Add(dice);
        Row("Seed", seedRow, "The same seed and words draw the same picture again; -1: a new one each time. Picking a picture below puts its seed here.");
        Row("How many", _count, "Several pictures at once (seed, seed + 1...): pick the best.");

        Head("Redraw a picture (optional)");
        var startRow = new StackPanel { Orientation = Orientation.Horizontal };
        var pickStart = new Button { Content = "Picture...", Padding = new Thickness(8, 1, 8, 1), ToolTip = "Start from your own picture (a sketch, a sprite, a photo) instead of noise" };
        pickStart.Click += (_, _) => PickStartImage();
        var clearStart = new Button { Content = "None", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(4, 0, 0, 0) };
        clearStart.Click += (_, _) => { _startImage = null; _startLabel.Text = "(none - drawn from the words)"; };
        startRow.Children.Add(pickStart);
        startRow.Children.Add(clearStart);
        left.Children.Add(startRow);
        left.Children.Add(_startLabel);
        Row("Change it", SliderRow(_strength, "0.00"), "How much of your picture is redrawn: 0.3 keeps its shapes, 0.8 only its idea.");

        Head("Making it a sprite");
        Row("Height (px)", SliderRow(_height, "0"), "The sprite's height in pixels; its width follows the shape.");
        Row("Colours", _colours, "Cut the colours down to a few, or to Ultima 6's sixteen.");
        Row("Background", SliderRow(_background, "0"), "How far from the border's colour still counts as background and is taken away (0: keep the background).");
        left.Children.Add(_outline);
        left.Children.Add(_keepBiggest);
        _keepBiggest.Click += (_, _) => RemakeSprite();
        _height.ValueChanged += (_, _) => RemakeSprite();
        _colours.SelectionChanged += (_, _) => RemakeSprite();
        _background.ValueChanged += (_, _) => RemakeSprite();
        _outline.Click += (_, _) => RemakeSprite();
        _style.SelectionChanged += (_, _) => ApplyStyle();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
        buttons.Children.Add(_generate);
        buttons.Children.Add(_stop);
        left.Children.Add(buttons);
        left.Children.Add(_progress);
        left.Children.Add(_status);
        var modelButton = new Button { Content = "Model folder...", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 10, 0, 2), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Where the Stable Diffusion model is (text_encoder, unet, vae_decoder, tokenizer)" };
        modelButton.Click += (_, _) => PickModelFolder();
        left.Children.Add(modelButton);
        left.Children.Add(_modelLabel);
        _generate.Click += async (_, _) => await GenerateAsync();
        _stop.Click += (_, _) => _cancel?.Cancel();

        // ---- right: the pictures and the sprite ----
        var right = new Grid();
        right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetColumn(right, 1);
        root.Children.Add(right);

        var pictures = new Grid();
        pictures.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        pictures.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        pictures.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _big, VerticalAlignment = VerticalAlignment.Top, Background = Brushes.White });
        var spritePanel = new DockPanel { Margin = new Thickness(10, 0, 0, 0) };
        Grid.SetColumn(spritePanel, 1);
        var zoomRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
        zoomRow.Children.Add(new TextBlock { Text = "The sprite - zoom ", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold });
        zoomRow.Children.Add(_zoom);
        _zoom.SelectionChanged += (_, _) => ShowSprite();
        DockPanel.SetDock(zoomRow, Dock.Top);
        spritePanel.Children.Add(zoomRow);
        RenderOptions.SetBitmapScalingMode(_sprite, BitmapScalingMode.NearestNeighbor);
        spritePanel.Children.Add(new Border
        {
            Background = CheckerBrush(), Child = new ScrollViewer { Content = _sprite, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
            BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1)
        });
        pictures.Children.Add(spritePanel);
        right.Children.Add(pictures);

        var variantBox = new GroupBox { Header = "Pictures drawn - click one to pick it", Margin = new Thickness(0, 6, 0, 0), Content = new ScrollViewer { Content = _variants, Height = 112, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled } };
        Grid.SetRow(variantBox, 1);
        right.Children.Add(variantBox);

        var save = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        Grid.SetRow(save, 2);
        save.Children.Add(new TextBlock { Text = "Sprite Editor folder ", VerticalAlignment = VerticalAlignment.Center });
        _folder.ItemsSource = _sprites.Groups.Where(g => !SpriteDatabase.IsLockedGroup(g.Name)).Select(g => g.Name).ToList();
        _folder.Text = "AI Artist";
        save.Children.Add(_folder);
        save.Children.Add(new TextBlock { Text = "  Name ", VerticalAlignment = VerticalAlignment.Center });
        save.Children.Add(_name);
        var add = new Button { Content = "Add to sprites", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(8, 0, 0, 0), FontWeight = FontWeights.Bold, ToolTip = "The sprite goes into that Sprite Editor folder (a new folder if it isn't there); saved with the project" };
        add.Click += (_, _) => AddToSprites();
        save.Children.Add(add);
        var png = new Button { Content = "Save PNG...", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        png.Click += (_, _) => SavePng(false);
        save.Children.Add(png);
        var pngBig = new Button { Content = "Save the big picture...", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(6, 0, 0, 0) };
        pngBig.Click += (_, _) => SavePng(true);
        save.Children.Add(pngBig);
        right.Children.Add(save);

        _modelFolder ??= LoadModelFolder();
        ShowModelState();
        ArtStyle first = ProjectSession.IsUltima6 ? Styles[1] : ProjectSession.IsUltima8 ? Styles[2] : Styles[0];
        _style.SelectedItem = first;
        ApplyStyle();
    }

    // ---------- the model ----------

    private static string LoadModelFolder()
    {
        try { if (File.Exists(SettingsFile) && File.ReadAllText(SettingsFile).Trim() is { Length: > 0 } saved) return saved; }
        catch (IOException) { }
        return DefaultModelFolder;
    }

    private void ShowModelState() =>
        _modelLabel.Text = StableDiffusion.IsModelFolder(_modelFolder ?? "")
            ? $"Model: {_modelFolder}" + (_model is null ? " (loads on the first Draw - about 20 seconds)" : _model.OnGraphicsCard ? " - on the graphics card" : " - on the processor (slow)")
            : $"No model in {_modelFolder} - pick the folder with Model folder... (Stable Diffusion 1.5 ONNX: text_encoder, unet, vae_decoder, tokenizer).";

    private void PickModelFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog { Description = "The Stable Diffusion model's folder (text_encoder, unet, vae_decoder, tokenizer)", SelectedPath = _modelFolder ?? "" };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        if (!StableDiffusion.IsModelFolder(dialog.SelectedPath))
        {
            MessageBox.Show(this, "That folder doesn't hold the model's parts: tokenizer\\vocab.json, text_encoder\\model.onnx, unet\\model.onnx, vae_decoder\\model.onnx.", "AI Artist", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _model?.Dispose();
        _model = null;
        _modelFolder = dialog.SelectedPath;
        try { Directory.CreateDirectory(System.IO.Path.GetDirectoryName(SettingsFile)!); File.WriteAllText(SettingsFile, _modelFolder); }
        catch (IOException) { }
        ShowModelState();
    }

    // ---------- drawing ----------

    private void ApplyStyle()
    {
        if (_style.SelectedItem is not ArtStyle style) return;
        _height.Value = style.Height;
        _colours.SelectedIndex = style.Ega ? 5 : style.Colours switch { 0 => 0, >= 64 => 1, >= 32 => 2, >= 16 => 3, _ => 4 };
        if (style.Colours == 48) _colours.SelectedIndex = 1;
        _outline.IsChecked = style.Outline;
    }

    private string FullPrompt()
    {
        var parts = new List<string> { _prompt.Text.Trim() };
        if (_facing.SelectedIndex >= 0 && Facings[_facing.SelectedIndex].Words is { Length: > 0 } facing) parts.Add(facing);
        if (_style.SelectedItem is ArtStyle { Words.Length: > 0 } style) parts.Add(style.Words);
        return string.Join(", ", parts.Where(p => p.Length > 0));
    }

    private async Task GenerateAsync()
    {
        if (_prompt.Text.Trim().Length == 0) { _status.Text = "Write what to draw first."; return; }
        if (!StableDiffusion.IsModelFolder(_modelFolder ?? "")) { ShowModelState(); _status.Text = "No model yet: Model folder..."; return; }
        _generate.IsEnabled = false;
        _stop.IsEnabled = true;
        _cancel = new CancellationTokenSource();
        var cancel = _cancel.Token;
        try
        {
            if (_model is null)
            {
                _status.Text = "Loading the model onto the graphics card (once per editor run)...";
                _progress.IsIndeterminate = true;
                string folder = _modelFolder!;
                _model = await Task.Run(() => new StableDiffusion(folder));
                _progress.IsIndeterminate = false;
                ShowModelState();
            }
            int count = _count.SelectedItem is int c ? c : 1, steps = (int)_steps.Value;
            long seed = long.TryParse(_seed.Text.Trim(), out long s) && s >= 0 ? s : Random.Shared.NextInt64(0, int.MaxValue - 8);
            string prompt = FullPrompt(), negative = _negative.Text.Trim();
            double guidance = _guidance.Value, strength = _strength.Value;
            byte[]? start = _startImage;
            _progress.Maximum = count * steps;
            for (int k = 0; k < count; k++)
            {
                int done = k * steps;
                _status.Text = $"Drawing {k + 1} of {count} (seed {seed + k})...";
                var progress = new Progress<int>(step => _progress.Value = done + step);
                var model = _model;
                var request = new GenerateRequest(prompt, negative, 512, 512, steps, guidance, seed + k, start, strength);
                var picture = await Task.Run(() => model.Generate(request, progress, cancel), cancel);
                _pictures.Add(picture);
                AddVariant(picture);
                Pick(picture);
            }
            _status.Text = $"Done. Seed {seed}" + (count > 1 ? $" to {seed + count - 1}" : "") + ". Pick the best below, then Add to sprites.";
        }
        catch (OperationCanceledException) { _status.Text = "Stopped."; }
        catch (Exception ex) { _status.Text = "The AI artist couldn't draw: " + ex.Message; }
        finally
        {
            _progress.IsIndeterminate = false;
            _generate.IsEnabled = true;
            _stop.IsEnabled = false;
        }
    }

    private void AddVariant(GeneratedImage picture)
    {
        var thumb = new Button { Margin = new Thickness(2), Padding = new Thickness(1), ToolTip = $"Seed {picture.Seed}", Content = new Image { Source = ToBitmap(picture.Width, picture.Height, picture.Rgba), Width = 96, Height = 96 } };
        thumb.Click += (_, _) => Pick(picture);
        _variants.Children.Insert(0, thumb);
    }

    private void Pick(GeneratedImage picture)
    {
        _picked = picture;
        _seed.Text = picture.Seed.ToString();
        _big.Source = ToBitmap(picture.Width, picture.Height, picture.Rgba);
        if (_name.Text.Trim().Length == 0) _name.Text = string.Join(" ", _prompt.Text.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(3));
        RemakeSprite();
    }

    private void RemakeSprite()
    {
        if (_picked is null) return;
        int colours = _colours.SelectedIndex switch { 0 => 0, 1 => 64, 2 => 32, 3 => 16, 4 => 8, _ => 16 };
        var options = new SpriteOptions((int)_height.Value, colours, _colours.SelectedIndex == 5 ? SpriteMaker.Ega16 : null, (int)_background.Value, _outline.IsChecked == true, _keepBiggest.IsChecked == true);
        _pickedSprite = SpriteMaker.Make(_picked, options);
        ShowSprite();
    }

    private void ShowSprite()
    {
        if (_pickedSprite is not { } s) return;
        int zoom = _zoom.SelectedItem is int z ? z : 4;
        _sprite.Source = ToBitmap(s.Width, s.Height, s.Rgba);
        _sprite.Width = s.Width * zoom;
        _sprite.Height = s.Height * zoom;
        _sprite.Stretch = Stretch.Fill;
    }

    private void PickStartImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Pictures|*.png;*.bmp;*.jpg;*.jpeg;*.gif|All files|*.*", Title = "A picture to redraw" };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var source = new BitmapImage(new Uri(dialog.FileName));
            // fitted into 512 x 512 on white, small sprites scaled up without blurring
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            double scale = Math.Min(512.0 / source.PixelWidth, 512.0 / source.PixelHeight);
            double w = source.PixelWidth * scale, h = source.PixelHeight * scale;
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 512, 512));
                dc.DrawImage(source, new Rect((512 - w) / 2, (512 - h) / 2, w, h));
            }
            var target = new RenderTargetBitmap(512, 512, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            var bgra = new byte[512 * 512 * 4];
            target.CopyPixels(bgra, 512 * 4, 0);
            for (int i = 0; i < bgra.Length; i += 4) (bgra[i], bgra[i + 2]) = (bgra[i + 2], bgra[i]);
            _startImage = bgra;
            _startLabel.Text = System.IO.Path.GetFileName(dialog.FileName);
        }
        catch (Exception ex) { MessageBox.Show(this, "That picture can't be read: " + ex.Message, "AI Artist"); }
    }

    // ---------- keeping it ----------

    private string? WritePng(int width, int height, byte[] rgba, string? path = null)
    {
        Directory.CreateDirectory(OutputFolder);
        string name = string.Concat((_name.Text.Trim().Length > 0 ? _name.Text.Trim() : "sprite").Select(ch => System.IO.Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));
        path ??= System.IO.Path.Combine(OutputFolder, $"{name}_{_picked?.Seed}_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(ToBitmap(width, height, rgba)));
        using var stream = File.Create(path);
        encoder.Save(stream);
        return path;
    }

    private void AddToSprites()
    {
        if (_pickedSprite is not { } s) { _status.Text = "Draw and pick a picture first."; return; }
        string folderName = _folder.Text.Trim().Length > 0 ? _folder.Text.Trim() : "AI Artist";
        if (SpriteDatabase.IsLockedGroup(folderName)) { _status.Text = $"\"{folderName}\" is one of the editor's own folders - pick another."; return; }
        string name = _name.Text.Trim().Length > 0 ? _name.Text.Trim() : "AI sprite";
        var group = _sprites.GetOrCreateGroup(folderName);
        string unique = name;
        for (int n = 2; group.Sprites.Any(x => x.Name == unique); n++) unique = $"{name} {n}";
        string? file = WritePng(s.Width, s.Height, s.Rgba);
        if (file is null) return;
        group.Sprites.Add(new SpriteEntry { Name = unique, FilePath = file, Frames = new List<SpriteFrame> { new() { FilePath = file } } });
        if (_folder.ItemsSource is List<string> names && !names.Contains(folderName)) { names.Add(folderName); _folder.Items.Refresh(); }
        _status.Text = $"\"{unique}\" is in the Sprite Editor's folder \"{folderName}\" ({s.Width} x {s.Height}). Save the project to keep it.";
    }

    private void SavePng(bool big)
    {
        if (_picked is null) { _status.Text = "Draw a picture first."; return; }
        var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "PNG picture|*.png", FileName = (_name.Text.Trim().Length > 0 ? _name.Text.Trim() : "sprite") + ".png" };
        if (dialog.ShowDialog(this) != true) return;
        if (big) WritePng(_picked.Width, _picked.Height, _picked.Rgba, dialog.FileName);
        else if (_pickedSprite is { } s) WritePng(s.Width, s.Height, s.Rgba, dialog.FileName);
        _status.Text = "Saved " + dialog.FileName;
    }

    // ---------- pictures ----------

    private static BitmapSource ToBitmap(int width, int height, byte[] rgba)
    {
        var bgra = new byte[rgba.Length];
        for (int i = 0; i < rgba.Length; i += 4) { bgra[i] = rgba[i + 2]; bgra[i + 1] = rgba[i + 1]; bgra[i + 2] = rgba[i]; bgra[i + 3] = rgba[i + 3]; }
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, bgra, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static Brush CheckerBrush()
    {
        var group = new DrawingGroup();
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc)), null, new RectangleGeometry(new Rect(0, 0, 16, 16))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)), null, new RectangleGeometry(new Rect(0, 0, 8, 8))));
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x99, 0x99, 0x99)), null, new RectangleGeometry(new Rect(8, 8, 8, 8))));
        return new DrawingBrush(group) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, 16, 16), ViewportUnits = BrushMappingMode.Absolute };
    }
}
