using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// System Settings > Slider > Custom: the designer for a slider of your own,
/// like a little paint program - a background, and pieces you add and drag
/// into place on it (minus, plus, accept, cancel buttons; the number, drawn
/// with your own digit sprite; a track with its thumb; decorations), each
/// with its picture and sound, a sound for every digit, and a live test.
/// The Classic style (Ultima VII's) stays as it is.
/// </summary>
public partial class SystemSettingsWindow
{
    private SliderPiece? _selectedPiece;

    private UIElement BuildSliderStyleChooser()
    {
        var s = _settings.Slider;
        var root = new DockPanel();
        var chooser = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 10, 10, 0) };
        var classic = new RadioButton { Content = "Classic (Ultima VII's slider)", GroupName = "SliderStyle", IsChecked = s.Style == SliderStyle.Classic, Margin = new Thickness(0, 0, 18, 0), FontWeight = FontWeights.SemiBold };
        var custom = new RadioButton { Content = "Custom (build your own from pieces)", GroupName = "SliderStyle", IsChecked = s.Style == SliderStyle.Custom, FontWeight = FontWeights.SemiBold };
        chooser.Children.Add(new TextBlock { Text = "Style:  ", VerticalAlignment = VerticalAlignment.Center, FontWeight = FontWeights.Bold });
        chooser.Children.Add(classic);
        chooser.Children.Add(custom);
        DockPanel.SetDock(chooser, Dock.Top);
        root.Children.Add(chooser);
        var body = new ContentControl();
        root.Children.Add(body);
        void Show() => body.Content = s.Style == SliderStyle.Classic ? BuildSliderTab() : BuildCustomSliderDesigner();
        classic.Checked += (_, _) => { s.Style = SliderStyle.Classic; Show(); };
        custom.Checked += (_, _) => { s.Style = SliderStyle.Custom; Show(); };
        Show();
        return root;
    }

    private UIElement BuildCustomSliderDesigner()
    {
        var design = _settings.Slider.Custom;
        while (design.DigitSoundIds.Count < 10) design.DigitSoundIds.Add(-1);
        var panel = new StackPanel { Margin = new Thickness(10) };
        panel.Children.Add(new TextBlock
        {
            Text = "Build a slider of your own: pick a background, then add pieces and drag them where you want them on it. " +
                   "Buttons: frame 1 is how it looks, frame 2 (if it has one) while it's pressed; minus and plus keep going while held down. " +
                   "The number: a digit sprite whose frames 1 to 10 are the digits 0 to 9 (an 11th frame is a blank, for no leading zeros) - or, with none, the game's font. " +
                   "The keys work as ever: Left / Right step, Enter accepts, Escape cancels.",
            TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 0, 0, 8)
        });

        var sprites = SliderSprites();
        var canvasHost = new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20)), HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8) };
        var properties = new StackPanel { Margin = new Thickness(14, 0, 0, 0), Width = 300 };
        var pieceList = new ListBox { Height = 120, Width = 220 };
        var testHost = new Border { Background = Brushes.Black, Padding = new Thickness(10), HorizontalAlignment = HorizontalAlignment.Left };
        var testNote = new TextBlock { Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 4, 0, 0) };

        void Refresh()
        {
            DrawDesign(canvasHost, design, Refresh);
            pieceList.ItemsSource = design.Pieces.Select((p, i) => $"{i + 1}. {PieceName(p.Kind)}").ToList();
            pieceList.SelectedIndex = _selectedPiece is null ? -1 : design.Pieces.IndexOf(_selectedPiece);
            ShowPieceProperties(properties, design, sprites, Refresh);
            var test = new NumberSliderControl(_settings.Slider, _sprites, _resources, 0, 120, 1, 8) { LayoutTransform = new ScaleTransform(3, 3) };
            test.ValueChanged += v => testNote.Text = $"Value: {v} (a test range of 0 to 120)";
            test.Accepted += v => testNote.Text = $"Accepted: {v}";
            test.Cancelled += () => testNote.Text = "Cancelled";
            test.PreviewKeyDown += (_, e) => { if (test.HandleKey(e.Key)) e.Handled = true; };
            test.MouseLeftButtonDown += (_, _) => test.Focus();
            testHost.Child = test;
            testNote.Text = "Try it: click the buttons (hold minus or plus), drag the thumb, or click it and use Left / Right / Enter / Escape.";
        }
        pieceList.SelectionChanged += (_, _) =>
        {
            int i = pieceList.SelectedIndex;
            var picked = i >= 0 && i < design.Pieces.Count ? design.Pieces[i] : null;
            if (ReferenceEquals(picked, _selectedPiece)) return;
            _selectedPiece = picked;
            Refresh();
        };

        panel.Children.Add(SliderSpritePicker("Background:", design.Background, sprites, Refresh));

        // The design canvas and, beside it, the selected piece's properties.
        var middle = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        var left = new StackPanel();
        left.Children.Add(new TextBlock { Text = "Design (four times its size) - drag the pieces, click one to change it", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 4) });
        left.Children.Add(canvasHost);
        var listRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        listRow.Children.Add(pieceList);
        var buttons = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        var add = new Button { Content = "Add a piece...", Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 0, 0, 4) };
        add.Click += (_, _) =>
        {
            var menu = new ContextMenu();
            foreach (var kind in Enum.GetValues<SliderPieceKind>())
            {
                var item = new MenuItem { Header = PieceName(kind) };
                var k = kind;
                item.Click += (_, _) =>
                {
                    var piece = new SliderPiece { Kind = k, X = 4, Y = 4 };
                    design.Pieces.Add(piece);
                    _selectedPiece = piece;
                    Refresh();
                };
                menu.Items.Add(item);
            }
            menu.PlacementTarget = add;
            menu.IsOpen = true;
        };
        var remove = new Button { Content = "Remove it", Padding = new Thickness(8, 1, 8, 1) };
        remove.Click += (_, _) => { if (_selectedPiece is not null) { design.Pieces.Remove(_selectedPiece); _selectedPiece = null; Refresh(); } };
        buttons.Children.Add(add);
        buttons.Children.Add(remove);
        listRow.Children.Add(buttons);
        left.Children.Add(listRow);
        middle.Children.Add(left);
        middle.Children.Add(properties);
        panel.Children.Add(middle);

        // Sounds.
        panel.Children.Add(new TextBlock { Text = "Sounds", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 2) });
        panel.Children.Add(SoundRow("When the number changes:", () => design.ChangeSoundResourceId, v => design.ChangeSoundResourceId = v));
        panel.Children.Add(new TextBlock { Text = "A sound for each digit - played when the number changes to end in it (instead of the one above):", Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 4, 0, 2) });
        var digits = new WrapPanel();
        for (int d = 0; d < 10; d++)
        {
            int digit = d;
            digits.Children.Add(SoundRow($"  {digit}:", () => design.DigitSoundIds[digit], v => design.DigitSoundIds[digit] = v, narrow: true));
        }
        panel.Children.Add(digits);

        panel.Children.Add(new TextBlock { Text = "Test it (three times its size)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 12, 0, 2) });
        panel.Children.Add(testHost);
        panel.Children.Add(testNote);
        Refresh();
        return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    }

    /// <summary>The design, four times its size: the background and every piece where it sits; drag a piece to move it, click it to choose it.</summary>
    private void DrawDesign(Border host, CustomSliderDesign design, Action refresh)
    {
        const double zoom = 4;
        var canvas = new Canvas();
        RenderOptions.SetBitmapScalingMode(canvas, BitmapScalingMode.NearestNeighbor);
        var background = SolidSizeEditor.LoadSpriteFrame(_sprites, design.Background.SpriteGroup, design.Background.SpriteName);
        double w = background?.PixelWidth ?? 120, h = background?.PixelHeight ?? 24;
        foreach (var piece in design.Pieces) { var (pw, ph) = PieceSize(piece); w = Math.Max(w, piece.X + pw + 2); h = Math.Max(h, piece.Y + ph + 2); }
        canvas.Width = w * zoom;
        canvas.Height = h * zoom;
        if (background is not null) Place(canvas, new Image { Source = background, Width = background.PixelWidth * zoom, Height = background.PixelHeight * zoom, IsHitTestVisible = false }, 0, 0);
        else Place(canvas, new Rectangle { Width = w * zoom, Height = h * zoom, Fill = new SolidColorBrush(Color.FromRgb(0x30, 0x34, 0x3A)), IsHitTestVisible = false }, 0, 0);

        SliderPiece? dragging = null;
        Point grab = default;
        int startX = 0, startY = 0;
        foreach (var piece in design.Pieces)
        {
            var (pw, ph) = PieceSize(piece);
            var art = PieceArt(piece);
            var grid = new Grid { Width = pw * zoom, Height = ph * zoom, Cursor = Cursors.SizeAll, Background = Brushes.Transparent, ToolTip = $"{PieceName(piece.Kind)} - drag to move" };
            if (art is not null) grid.Children.Add(new Image { Source = art, Stretch = Stretch.Fill });
            else grid.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(120, 0x80, 0x90, 0xA0)), Child = new TextBlock { Text = PieceShortName(piece.Kind), Foreground = Brushes.White, FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            if (ReferenceEquals(piece, _selectedPiece)) grid.Children.Add(new Border { BorderBrush = Brushes.Gold, BorderThickness = new Thickness(2) });
            var p = piece;
            grid.MouseLeftButtonDown += (_, e) =>
            {
                dragging = p;
                grab = e.GetPosition(canvas);
                startX = p.X;
                startY = p.Y;
                if (!ReferenceEquals(_selectedPiece, p)) { _selectedPiece = p; }
                canvas.CaptureMouse();
                e.Handled = true;
            };
            Place(canvas, grid, piece.X * zoom, piece.Y * zoom);
        }
        canvas.MouseMove += (_, e) =>
        {
            if (dragging is null) return;
            var now = e.GetPosition(canvas);
            dragging.X = Math.Max(0, startX + (int)Math.Round((now.X - grab.X) / zoom));
            dragging.Y = Math.Max(0, startY + (int)Math.Round((now.Y - grab.Y) / zoom));
            var visual = canvas.Children.OfType<Grid>().ElementAtOrDefault(_settings.Slider.Custom.Pieces.IndexOf(dragging));
            if (visual is not null) { Canvas.SetLeft(visual, dragging.X * zoom); Canvas.SetTop(visual, dragging.Y * zoom); }
        };
        canvas.MouseLeftButtonUp += (_, _) => { if (dragging is null) return; dragging = null; canvas.ReleaseMouseCapture(); refresh(); };
        host.Child = canvas;
    }

    private void ShowPieceProperties(StackPanel host, CustomSliderDesign design, List<SliderSpriteChoice> sprites, Action refresh)
    {
        host.Children.Clear();
        if (_selectedPiece is not { } piece)
        {
            host.Children.Add(new TextBlock { Text = "Click a piece (on the design or in the list) to change it.", Opacity = 0.7, TextWrapping = TextWrapping.Wrap });
            return;
        }
        host.Children.Add(new TextBlock { Text = PieceName(piece.Kind), FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 0, 0, 2) });
        host.Children.Add(new TextBlock { Text = PieceHelp(piece.Kind), TextWrapping = TextWrapping.Wrap, Opacity = 0.7, FontSize = 11, Margin = new Thickness(0, 0, 0, 6) });
        string spriteLabel = piece.Kind switch { SliderPieceKind.Number => "Digit sprite:", SliderPieceKind.Track => "Thumb:", _ => "Picture:" };
        host.Children.Add(SliderSpritePicker(spriteLabel, piece.Sprite, sprites, refresh));
        host.Children.Add(PieceNumber("X:", () => piece.X, v => piece.X = Math.Max(0, v), refresh));
        host.Children.Add(PieceNumber("Y:", () => piece.Y, v => piece.Y = Math.Max(0, v), refresh));
        switch (piece.Kind)
        {
            case SliderPieceKind.Decrease:
            case SliderPieceKind.Increase:
            case SliderPieceKind.Accept:
            case SliderPieceKind.Cancel:
                host.Children.Add(SoundRow("Its sound:", () => piece.SoundResourceId, v => piece.SoundResourceId = v));
                break;
            case SliderPieceKind.Number:
                host.Children.Add(PieceNumber("Digits shown:", () => piece.DigitCount, v => piece.DigitCount = Math.Clamp(v, 0, 3), refresh, "0 = as many as needed"));
                host.Children.Add(PieceNumber("Spacing:", () => piece.DigitSpacing, v => piece.DigitSpacing = Math.Clamp(v, -8, 32), refresh, "pixels between digits"));
                var zeros = new CheckBox { Content = "Leading zeros (\"008\")", IsChecked = piece.LeadingZeros, Margin = new Thickness(0, 4, 0, 0) };
                zeros.Checked += (_, _) => { piece.LeadingZeros = true; refresh(); };
                zeros.Unchecked += (_, _) => { piece.LeadingZeros = false; refresh(); };
                host.Children.Add(zeros);
                break;
            case SliderPieceKind.Track:
                host.Children.Add(PieceNumber("Length:", () => piece.Width, v => piece.Width = Math.Clamp(v, 8, 2000), refresh, "pixels"));
                break;
        }
    }

    // ---------- Pieces ----------

    private (double W, double H) PieceSize(SliderPiece piece)
    {
        var art = PieceArt(piece);
        double w = art?.PixelWidth ?? 12, h = art?.PixelHeight ?? 11;
        if (piece.Kind == SliderPieceKind.Number) w = (art?.PixelWidth ?? 7) * Math.Max(1, piece.DigitCount == 0 ? 3 : piece.DigitCount) + piece.DigitSpacing * Math.Max(0, (piece.DigitCount == 0 ? 3 : piece.DigitCount) - 1);
        if (piece.Kind == SliderPieceKind.Track) w = Math.Max(8, piece.Width);
        return (w, h);
    }

    private BitmapSource? PieceArt(SliderPiece piece) =>
        piece.Sprite.IsSet ? SolidSizeEditor.LoadSpriteFrame(_sprites, piece.Sprite.SpriteGroup, piece.Sprite.SpriteName) : null;

    private static string PieceName(SliderPieceKind kind) => kind switch
    {
        SliderPieceKind.Decrease => "Minus button",
        SliderPieceKind.Increase => "Plus button",
        SliderPieceKind.Accept => "Accept button",
        SliderPieceKind.Cancel => "Cancel button",
        SliderPieceKind.Number => "Number display",
        SliderPieceKind.Track => "Track and thumb",
        _ => "Decoration"
    };

    private static string PieceShortName(SliderPieceKind kind) => kind switch
    {
        SliderPieceKind.Decrease => "-", SliderPieceKind.Increase => "+", SliderPieceKind.Accept => "OK", SliderPieceKind.Cancel => "X",
        SliderPieceKind.Number => "123", SliderPieceKind.Track => "track", _ => "deco"
    };

    private static string PieceHelp(SliderPieceKind kind) => kind switch
    {
        SliderPieceKind.Decrease => "Takes a step off the number (Left key too). Held down, it keeps going.",
        SliderPieceKind.Increase => "Adds a step to the number (Right key too). Held down, it keeps going.",
        SliderPieceKind.Accept => "Accepts the number (Enter too).",
        SliderPieceKind.Cancel => "Cancels (Escape too).",
        SliderPieceKind.Number => "The number. With a digit sprite: frames 1-10 are 0-9, an 11th a blank. Without one: the game's font.",
        SliderPieceKind.Track => "The thumb (its picture) slides along the track; clicking or dragging sets the number.",
        _ => "Just a picture - a frame, a label, a light."
    };

    // ---------- Building blocks ----------

    private List<SliderSpriteChoice> SliderSprites()
    {
        var sprites = new List<SliderSpriteChoice> { new("", "", "(none)") };
        foreach (var group in _sprites.Groups.Where(g => g.Name != SpriteDatabase.TilesetGroupName && g.Name != SpriteDatabase.FontsGroupName))
            sprites.AddRange(group.Sprites.Select(sp => new SliderSpriteChoice(group.Name, sp.Name, $"{sp.Name}   ({group.Name})")));
        return sprites;
    }

    private static FrameworkElement SliderSpritePicker(string label, SpriteLink link, List<SliderSpriteChoice> sprites, Action changed)
    {
        var combo = new ComboBox { Width = 200, ItemsSource = sprites, DisplayMemberPath = nameof(SliderSpriteChoice.Display) };
        combo.SelectedItem = sprites.FirstOrDefault(c => c.Name == link.SpriteName && c.Group == link.SpriteGroup) ?? sprites[0];
        combo.SelectionChanged += (_, _) =>
        {
            var choice = combo.SelectedItem as SliderSpriteChoice ?? sprites[0];
            link.SpriteGroup = choice.Group;
            link.SpriteName = choice.Name;
            changed();
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(combo);
        return row;
    }

    private static FrameworkElement PieceNumber(string label, Func<int> get, Action<int> set, Action changed, string hint = "")
    {
        var box = new TextBox { Width = 50, Text = get().ToString() };
        void Commit() { if (int.TryParse(box.Text.Trim(), out int v)) { set(v); changed(); } box.Text = get().ToString(); }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
        row.Children.Add(new TextBlock { Text = label, Width = 90, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(box);
        if (hint.Length > 0) row.Children.Add(new TextBlock { Text = "  " + hint, Opacity = 0.6, FontSize = 11, VerticalAlignment = VerticalAlignment.Center });
        return row;
    }

    private FrameworkElement SoundRow(string label, Func<int> get, Action<int> set, bool narrow = false)
    {
        var sounds = new List<ResourceEntry> { new() { Id = -1, Name = "(None)" } };
        sounds.AddRange(_resources.Entries.Where(r => r.Kind == ResourceKind.Sound));
        var combo = new ComboBox { Width = narrow ? 110 : 200, ItemsSource = sounds, DisplayMemberPath = "Name", SelectedItem = sounds.FirstOrDefault(r => r.Id == get()) ?? sounds[0] };
        combo.SelectionChanged += (_, _) => set((combo.SelectedItem as ResourceEntry)?.Id ?? -1);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 8, 2) };
        row.Children.Add(new TextBlock { Text = label, Width = narrow ? 24 : 170, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(combo);
        return row;
    }

    private static void Place(Canvas canvas, UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        canvas.Children.Add(element);
    }
}
