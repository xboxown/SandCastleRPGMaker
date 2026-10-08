using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// "How custom sliders work", with pictures, a page at a time: what the
/// pieces are, what to make in the Sprite Editor (frame by frame), a worked
/// example (a sci-fi counter with three digit windows), how it behaves in
/// play, and what it can and can't do. Opened from System Settings >
/// Slider > Custom.
/// </summary>
public class CustomSliderHelpWindow : Window
{
    private static readonly Brush Paper = new SolidColorBrush(Color.FromRgb(0xFB, 0xF7, 0xEE));
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x33, 0x33, 0x33));
    private static readonly Brush Panel = new SolidColorBrush(Color.FromRgb(0x5A, 0x60, 0x66));
    private static readonly Brush PanelEdge = new SolidColorBrush(Color.FromRgb(0x2A, 0x2E, 0x32));
    private static readonly Brush Glass = new SolidColorBrush(Color.FromRgb(0x1C, 0x2B, 0x26));
    private static readonly Brush Lcd = new SolidColorBrush(Color.FromRgb(0x4C, 0xF0, 0xA0));
    private static readonly Brush Callout = Brushes.SteelBlue;

    private readonly List<Func<UIElement>> _pages = new();
    private readonly ContentControl _page = new();
    private readonly TextBlock _pageNumber = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 10, 0), Foreground = Brushes.DimGray };
    private readonly Button _back = new() { Content = "< Back", Width = 80 };
    private readonly Button _next = new() { Content = "Next >", Width = 80 };
    private int _index;

    public CustomSliderHelpWindow()
    {
        Title = "How custom sliders work";
        Width = 880;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = Paper;

        _pages.Add(PageWhat);
        _pages.Add(PageSprites);
        _pages.Add(PageExample);
        _pages.Add(PagePlay);
        _pages.Add(PageCanAndCannot);

        _back.Click += (_, _) => Show(_index - 1);
        _next.Click += (_, _) => Show(_index + 1);
        var close = new Button { Content = "Got it", Width = 90, IsCancel = true, Margin = new Thickness(20, 0, 0, 0) };
        close.Click += (_, _) => Close();
        var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(12) };
        nav.Children.Add(_back);
        nav.Children.Add(_pageNumber);
        nav.Children.Add(_next);
        nav.Children.Add(close);
        var root = new DockPanel();
        DockPanel.SetDock(nav, Dock.Bottom);
        root.Children.Add(nav);
        root.Children.Add(new ScrollViewer { Content = _page, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
        Show(0);
    }

    private void Show(int index)
    {
        _index = Math.Clamp(index, 0, _pages.Count - 1);
        _page.Content = _pages[_index]();
        _pageNumber.Text = $"Page {_index + 1} of {_pages.Count}";
        _back.IsEnabled = _index > 0;
        _next.IsEnabled = _index < _pages.Count - 1;
    }

    // ================= Page 1: what a custom slider is =================

    private UIElement PageWhat()
    {
        var page = Page("1. What a custom slider is");
        page.Children.Add(Text("Whenever the game asks \"how many\" - how many hours to sleep, how many arrows to drop - it shows the slider. " +
                               "Classic is Ultima VII's own. Custom is one you build: a background, and pieces placed on it. Here's a sci-fi counter made of five pieces:"));
        page.Children.Add(SciFiPanel(808, annotate: true));
        page.Children.Add(Bullets(
            "Background - the panel itself: one picture.",
            "Minus button and Plus button - press them to count down and up (hold them to keep counting).",
            "Number display - the number, drawn with your own digit pictures: here, three windows showing 008.",
            "Accept button - takes the number (Enter does too). A Cancel button is optional (Escape does it anyway).",
            "Also possible: a Track with a thumb that slides, and Decorations (lights, labels, rivets) - as many as you like."));
        page.Children.Add(Note("You drag every piece where you want it on the background, in the designer (Edit > Settings > Slider > Custom). Nothing is fixed: the layout is yours."));
        return page;
    }

    // ================= Page 2: the Sprite Editor =================

    private UIElement PageSprites()
    {
        var page = Page("2. What to make in the Sprite Editor");
        page.Children.Add(Text("Any sprite group works - there's no special group. To keep things tidy, make a group called \"Slider\" (Sprite Editor > New Group) and put all its pieces there. These are the sprites, frame by frame:"));

        page.Children.Add(SpriteRow("SciFi_Panel", "Background", new[] { ("Frame 1", "the panel") }, PanelThumb));
        page.Children.Add(SpriteRow("SciFi_Minus", "Minus button", new[] { ("Frame 1", "normal"), ("Frame 2", "pressed (optional)") }, i => ButtonThumb("-", pressed: i == 1)));
        page.Children.Add(SpriteRow("SciFi_Plus", "Plus button", new[] { ("Frame 1", "normal"), ("Frame 2", "pressed (optional)") }, i => ButtonThumb("+", pressed: i == 1)));
        page.Children.Add(SpriteRow("SciFi_OK", "Accept button", new[] { ("Frame 1", "normal"), ("Frame 2", "pressed (optional)") }, i => ButtonThumb("OK", pressed: i == 1)));

        page.Children.Add(Heading("The digits - the important one", 14));
        page.Children.Add(Text("One sprite, ten frames in order: frame 1 is the picture of 0, frame 2 of 1 ... frame 10 of 9. An 11th frame, a blank window, is used only without leading zeros (\"  8\" instead of \"008\"). All frames the same size."));
        var strip = new WrapPanel { Margin = new Thickness(0, 6, 0, 6) };
        for (int frame = 1; frame <= 11; frame++)
        {
            var cell = new StackPanel { Margin = new Thickness(3) };
            cell.Children.Add(DigitThumb(frame <= 10 ? (frame - 1).ToString() : ""));
            cell.Children.Add(new TextBlock { Text = $"Frame {frame}", FontSize = 10, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center });
            cell.Children.Add(new TextBlock { Text = frame <= 10 ? $"= {frame - 1}" : "= blank", FontSize = 10, Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Center });
            strip.Children.Add(cell);
        }
        page.Children.Add(new TextBlock { Text = "SciFi_Digits", FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(0, 4, 0, 0) });
        page.Children.Add(strip);
        page.Children.Add(Note("Tip: in the Sprite Editor, Import Sheet cuts a strip of ten digits into ten frames at once. Leave Loop and FPS alone - the slider shows one frame per digit, it doesn't animate them."));
        return page;
    }

    // ================= Page 3: the worked example =================

    private UIElement PageExample()
    {
        var page = Page("3. Building it, step by step");
        page.Children.Add(SciFiPanel(8, annotate: false));
        page.Children.Add(Numbered(
            "Sprite Editor: make the five sprites from page 2 (panel, minus, plus, OK, digits).",
            "Edit > Settings > Slider: choose Style: Custom.",
            "Background: pick SciFi_Panel. It appears in the design, four times its size.",
            "Click a piece in the list (or on the design). The starting design already has a Minus button, a Number display, a Plus button and an Accept button.",
            "Minus button: Picture = SciFi_Minus, and pick a sound if you like. Drag it onto the panel's left button.",
            "Plus button: Picture = SciFi_Plus. Drag it onto the right button.",
            "Number display: Digit sprite = SciFi_Digits, Digits shown = 3, Leading zeros ticked. Drag it so the first digit sits in the first window; set Spacing so the next two land in the other windows.",
            "Accept button: Picture = SciFi_OK. Drag it where your panel has room (or leave it off the art - Enter accepts anyway).",
            "Sounds: a \"number changed\" sound, or one for each digit 0-9 - a different beep as it lands on each.",
            "Try it in the test below the designer: click, hold, use Left / Right / Enter / Escape. It's saved with the project."));
        return page;
    }

    // ================= Page 4: in play =================

    private UIElement PagePlay()
    {
        var page = Page("4. How it behaves in the game");
        page.Children.Add(Text("With three digits and leading zeros, it counts like an odometer: the right digit goes 0 to 9, then rolls over and the middle one goes up."));
        var counting = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        var values = new[] { "000", "001", "002", "...", "009", "010", "011", "...", "099", "100" };
        for (int v = 0; v < values.Length; v++)
        {
            if (values[v] == "...") { counting.Children.Add(new TextBlock { Text = "...", FontSize = 18, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0) }); continue; }
            var digits = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4) };
            foreach (char c in values[v]) digits.Children.Add(DigitThumb(c.ToString(), small: true));
            counting.Children.Add(digits);
            if (v < values.Length - 1 && values[v + 1] != "...") counting.Children.Add(new TextBlock { Text = "→", FontSize = 16, Foreground = Callout, VerticalAlignment = VerticalAlignment.Center });
        }
        page.Children.Add(counting);
        page.Children.Add(Bullets(
            "Plus or the Right key: up a step. Minus or the Left key: down a step. Held down, a button keeps counting, faster after a moment.",
            "Accept button or Enter: done. Cancel button or Escape: nevermind.",
            "Each press plays that button's sound; each change plays the sound of the digit it lands on (or the \"changed\" sound).",
            "A button's frame 2 shows while it's held - it looks pressed.",
            "The range is set by whatever is asking: a bed set to 1-12 hours stops at 012. Never above 999.",
            "It grows with the window, like everything else in the game (2x, 3x, 4x...)."));
        return page;
    }

    // ================= Page 5: can and can't =================

    private UIElement PageCanAndCannot()
    {
        var page = Page("5. What you can do - and what not yet");
        page.Children.Add(Heading("You can", 14));
        page.Children.Add(Bullets(
            "Any look at all: your own panel, buttons, digits, thumb and decorations - fantasy, sci-fi, a wooden abacus, a magic scroll.",
            "Put every piece anywhere, in any order; leave out the ones you don't need (no track, no cancel button...).",
            "Buttons that look pressed while held, each with its own sound; counting that repeats while held.",
            "Digits drawn in your own style, from 1 to 3 of them, with or without leading zeros, spaced as you like.",
            "A sound for every digit 0-9, and a sound for any change.",
            "A thumb that slides along a track, clicked or dragged.",
            "As many decorations as you like (frames, glass, labels, lights).",
            "Switch back to Classic at any time - it's kept exactly as it is."));
        page.Children.Add(Heading("Not yet", 14));
        page.Children.Add(Bullets(
            "Pieces that animate (a blinking light shows its first frame).",
            "A piece whose picture changes with the number - a clock hand that turns, a gauge that fills.",
            "A track that runs up and down (only left to right for now).",
            "More than one number display."));
        page.Children.Add(Note("For choosing a quantity, everything that matters is there - the look is entirely yours. The \"not yet\" list can be added whenever you want it."));
        return page;
    }

    // ================= Drawings =================

    /// <summary>The example: a grey panel with a button at each end and three digit windows, showing a number.</summary>
    private UIElement SciFiPanel(int value, bool annotate)
    {
        const double scale = 5;
        var canvas = new Canvas { Width = 104 * scale + (annotate ? 0 : 0), Height = (annotate ? 40 : 22) * scale, Margin = new Thickness(0, 10, 0, 10) };
        double top = annotate ? 12 * scale : 2 * scale;
        canvas.Children.Add(At(new Rectangle { Width = 92 * scale, Height = 16 * scale, Fill = Panel, Stroke = PanelEdge, StrokeThickness = 3, RadiusX = 6, RadiusY = 6 }, 6 * scale, top));
        canvas.Children.Add(At(ButtonThumb("-", false, scale * 0.5), 9 * scale, top + 3 * scale));
        canvas.Children.Add(At(ButtonThumb("+", false, scale * 0.5), 72 * scale, top + 3 * scale));
        canvas.Children.Add(At(ButtonThumb("OK", false, scale * 0.5), 85 * scale, top + 3 * scale));
        string digits = value.ToString().PadLeft(3, '0');
        for (int i = 0; i < 3; i++) canvas.Children.Add(At(DigitThumb(digits[i].ToString(), small: false, scale: scale * 0.5), (24 + i * 15) * scale, top + 2 * scale));
        if (!annotate) return canvas;
        void Label(string text, double x, double arrowX)
        {
            canvas.Children.Add(At(new TextBlock { Text = text, Foreground = Callout, FontWeight = FontWeights.SemiBold, FontSize = 12 }, x, 0));
            canvas.Children.Add(new Line { X1 = arrowX, Y1 = 16, X2 = arrowX, Y2 = top - 2, Stroke = Callout, StrokeThickness = 1.5 });
        }
        Label("Minus button", 2 * scale, 12 * scale);
        Label("Number display (3 digits)", 28 * scale, 46 * scale);
        Label("Plus", 72 * scale, 75 * scale);
        Label("Accept", 85 * scale, 88 * scale);
        canvas.Children.Add(At(new TextBlock { Text = "Background (the panel)", Foreground = Callout, FontWeight = FontWeights.SemiBold, FontSize = 12 }, 34 * scale, top + 17 * scale));
        return canvas;
    }

    private static FrameworkElement ButtonThumb(string label, bool pressed, double size = 2.4)
    {
        double w = 12 * size, h = 10 * size;
        var grid = new Grid { Width = w, Height = h };
        grid.Children.Add(new Rectangle { Fill = pressed ? new SolidColorBrush(Color.FromRgb(0x2E, 0x8B, 0x57)) : new SolidColorBrush(Color.FromRgb(0x8C, 0x96, 0xA0)), Stroke = PanelEdge, StrokeThickness = 2, RadiusX = 3, RadiusY = 3 });
        grid.Children.Add(new TextBlock { Text = label, Foreground = pressed ? Brushes.White : PanelEdge, FontWeight = FontWeights.Bold, FontSize = Math.Max(10, 4 * size), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return grid;
    }

    private static FrameworkElement PanelThumb(int frame) => new Rectangle { Width = 110, Height = 22, Fill = Panel, Stroke = PanelEdge, StrokeThickness = 2, RadiusX = 4, RadiusY = 4 };

    private static FrameworkElement DigitThumb(string digit, bool small = false, double scale = 2.6)
    {
        if (small) scale = 1.8;
        var grid = new Grid { Width = 11 * scale, Height = 13 * scale, Margin = new Thickness(1, 0, 1, 0) };
        grid.Children.Add(new Rectangle { Fill = Glass, Stroke = Brushes.Black, StrokeThickness = 1.5 });
        grid.Children.Add(new TextBlock { Text = digit, Foreground = Lcd, FontFamily = new FontFamily("Consolas"), FontWeight = FontWeights.Bold, FontSize = 9 * scale, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
        return grid;
    }

    private static UIElement SpriteRow(string spriteName, string piece, (string Frame, string Meaning)[] frames, Func<int, FrameworkElement> thumb)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 2) };
        var name = new StackPanel { Width = 170, VerticalAlignment = VerticalAlignment.Center };
        name.Children.Add(new TextBlock { Text = spriteName, FontWeight = FontWeights.Bold, Foreground = Ink });
        name.Children.Add(new TextBlock { Text = piece, FontSize = 11, Foreground = Brushes.DimGray });
        row.Children.Add(name);
        for (int i = 0; i < frames.Length; i++)
        {
            var cell = new StackPanel { Margin = new Thickness(6, 0, 6, 0) };
            cell.Children.Add(thumb(i));
            cell.Children.Add(new TextBlock { Text = frames[i].Frame, FontSize = 10, Foreground = Ink, HorizontalAlignment = HorizontalAlignment.Center });
            cell.Children.Add(new TextBlock { Text = frames[i].Meaning, FontSize = 10, Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Center });
            row.Children.Add(cell);
        }
        return row;
    }

    // ================= Text pieces =================

    private static StackPanel Page(string title)
    {
        var page = new StackPanel { Margin = new Thickness(20) };
        page.Children.Add(Heading(title, 20));
        return page;
    }

    private static TextBlock Heading(string text, double size) => new() { Text = text, FontSize = size, FontWeight = FontWeights.Bold, Foreground = Ink, Margin = new Thickness(0, 8, 0, 6) };
    private static TextBlock Text(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Ink, Margin = new Thickness(0, 2, 0, 4) };
    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.DimGray, FontStyle = FontStyles.Italic, Margin = new Thickness(0, 8, 0, 4) };

    private static StackPanel Bullets(params string[] lines)
    {
        var panel = new StackPanel { Margin = new Thickness(8, 2, 0, 4) };
        foreach (var line in lines) panel.Children.Add(new TextBlock { Text = "•  " + line, TextWrapping = TextWrapping.Wrap, Foreground = Ink, Margin = new Thickness(0, 2, 0, 2) });
        return panel;
    }

    private static StackPanel Numbered(params string[] lines)
    {
        var panel = new StackPanel { Margin = new Thickness(8, 2, 0, 4) };
        for (int i = 0; i < lines.Length; i++)
        {
            var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
            var number = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Callout, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Top,
                Child = new TextBlock { Text = (i + 1).ToString(), Foreground = Brushes.White, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
            DockPanel.SetDock(number, Dock.Left);
            row.Children.Add(number);
            row.Children.Add(new TextBlock { Text = lines[i], TextWrapping = TextWrapping.Wrap, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Add(row);
        }
        return panel;
    }

    private static UIElement At(UIElement element, double x, double y)
    {
        Canvas.SetLeft(element, x);
        Canvas.SetTop(element, y);
        return element;
    }
}
