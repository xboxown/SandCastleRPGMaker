using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Path = System.Windows.Shapes.Path;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The editor's splash screen (as RPG Maker's): shown on its own thread the
/// moment the program starts, so it's there at once while the editor loads,
/// and closed once the editor is up. The picture is a still anime-style beach
/// scene drawn here (SplashScene); a "Splash.png" beside the program is shown
/// instead, for art of your own.
/// </summary>
public sealed class SplashWindow : Window
{
    private static SplashWindow? _shown;
    private DateTime _openedAt;

    private SplashWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = SplashScene.Width;
        Height = SplashScene.Height;
        Title = "Sandcastle RPG Maker";

        UIElement picture = SplashScene.Build();
        string own = System.IO.Path.Combine(AppContext.BaseDirectory, "Splash.png");
        if (File.Exists(own))
        {
            try { picture = new Image { Source = new BitmapImage(new Uri(own)), Stretch = Stretch.UniformToFill, Width = Width, Height = Height }; }
            catch { /* not a picture: the drawn one */ }
        }
        Content = new Border
        {
            CornerRadius = new CornerRadius(18),
            ClipToBounds = true,
            Child = picture,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.45 }
        };
        // Rounded corners: the picture clipped to the border's shape.
        Loaded += (_, _) => ((Border)Content).Child.Clip = new RectangleGeometry(new Rect(0, 0, Width, Height), 18, 18);
    }

    /// <summary>Shows the splash on a thread of its own (it stays up and painted while the editor loads on the main one).</summary>
    public static void ShowWhileLoading()
    {
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            try
            {
                var splash = new SplashWindow();
                splash.Closed += (_, _) => splash.Dispatcher.InvokeShutdown();
                splash.Show();
                splash._openedAt = DateTime.Now;
                _shown = splash;
            }
            catch { _shown = null; } // no splash then - the editor opens all the same
            finally { ready.Set(); }
            if (_shown is not null) Dispatcher.Run();
        }) { IsBackground = true, Name = "Splash" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));
    }

    /// <summary>The editor is up: the splash goes - after being seen at least a moment, fading out.</summary>
    public static void CloseWhenEditorShown()
    {
        if (_shown is not { } splash) return;
        _shown = null;
        splash.Dispatcher.BeginInvoke(() =>
        {
            var wait = TimeSpan.FromSeconds(2) - (DateTime.Now - splash._openedAt);
            var timer = new DispatcherTimer { Interval = wait > TimeSpan.Zero ? wait : TimeSpan.FromMilliseconds(1) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                var fade = new System.Windows.Media.Animation.DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(350));
                fade.Completed += (_, _) => splash.Close();
                splash.BeginAnimation(OpacityProperty, fade);
            };
            timer.Start();
        });
    }
}

/// <summary>
/// The splash picture: a sunny beach in bright anime colours - a smiling boy
/// in swim trunks with his bucket in front of a big sand castle, his friends
/// playing beside him (a girl with a beach ball, a little one with a spade),
/// the sea, the sun, and the title.
/// </summary>
public static class SplashScene
{
    public const double Width = 720, Height = 450;

    private static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    private static SolidColorBrush S(string hex) { var b = new SolidColorBrush(C(hex)); b.Freeze(); return b; }
    private static LinearGradientBrush V(params string[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };
        for (int i = 0; i < stops.Length; i++) b.GradientStops.Add(new GradientStop(C(stops[i]), stops.Length == 1 ? 0 : i / (double)(stops.Length - 1)));
        b.Freeze();
        return b;
    }
    private static RadialGradientBrush R(string inner, string outer)
    {
        var b = new RadialGradientBrush(C(inner), C(outer)) { GradientOrigin = new Point(0.35, 0.3) };
        b.Freeze();
        return b;
    }

    private static Path P(Canvas on, string data, Brush? fill, string? stroke = null, double thickness = 0)
    {
        var path = new Path { Data = Geometry.Parse(data), Fill = fill, Stroke = stroke is null ? null : S(stroke), StrokeThickness = thickness, StrokeLineJoin = PenLineJoin.Round, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        on.Children.Add(path);
        return path;
    }

    private static Ellipse O(Canvas on, double cx, double cy, double rx, double ry, Brush fill, string? stroke = null, double thickness = 0)
    {
        var e = new Ellipse { Width = rx * 2, Height = ry * 2, Fill = fill, Stroke = stroke is null ? null : S(stroke), StrokeThickness = thickness };
        Canvas.SetLeft(e, cx - rx);
        Canvas.SetTop(e, cy - ry);
        on.Children.Add(e);
        return e;
    }

    /// <summary>A limb: an outlined, rounded stroke.</summary>
    private static void Limb(Canvas on, string data, string skin, double width)
    {
        P(on, data, null, "#7A3F1D", width + 3);
        P(on, data, null, skin, width);
    }

    public static Canvas Build(bool title = true)
    {
        var scene = new Canvas { Width = Width, Height = Height, ClipToBounds = true };
        Sky(scene);
        Sea(scene);
        Sand(scene);
        Castle(scene);
        Kids(scene);
        if (title) TitleAndFooter(scene);
        return scene;
    }

    /// <summary>The scene as a square icon (Game.info on the Amiga, MorphOS, AmigaOS 4): the boy and his sand castle, size x size, 0xAARRGGBB.</summary>
    public static uint[] IconPixels(int size)
    {
        var scene = Build(title: false);
        scene.Measure(new Size(Width, Height));
        scene.Arrange(new Rect(0, 0, Width, Height));
        scene.UpdateLayout();
        var whole = new RenderTargetBitmap((int)Width, (int)Height, 96, 96, PixelFormats.Pbgra32);
        whole.Render(scene);
        var square = new CroppedBitmap(whole, new Int32Rect(150, 130, 320, 320)); // the boy and the castle's tower
        var small = new TransformedBitmap(square, new ScaleTransform(size / 320.0, size / 320.0));
        var straight = new FormatConvertedBitmap(small, PixelFormats.Bgra32, null, 0);
        var bytes = new byte[size * size * 4];
        straight.CopyPixels(bytes, size * 4, 0);
        var pixels = new uint[size * size];
        for (int i = 0; i < pixels.Length; i++)
            pixels[i] = (uint)bytes[i * 4 + 3] << 24 | (uint)bytes[i * 4 + 2] << 16 | (uint)bytes[i * 4 + 1] << 8 | bytes[i * 4];
        return pixels;
    }

    private static void Sky(Canvas s)
    {
        s.Children.Add(new Rectangle { Width = Width, Height = Height, Fill = V("#3DB7F5", "#8FD8FF", "#D9F3FF", "#FFF1D6") });
        // A rainbow far away
        string[] bands = { "#FF5A5F", "#FF9F1C", "#FFE14D", "#5BD16E", "#3FA9F5", "#9B6BFF" };
        for (int i = 0; i < bands.Length; i++)
        {
            double r = 230 - i * 9;
            var arc = P(s, $"M{360 - r},215 A{r},{r} 0 0 1 {360 + r},215", null, bands[i], 9);
            arc.Opacity = 0.32;
        }
        // The sun, smiling
        for (int i = 0; i < 12; i++)
        {
            double a = i * Math.PI / 6, cx = 640, cy = 74;
            var ray = P(s, $"M{cx + 46 * Math.Cos(a - 0.12):0.#},{cy + 46 * Math.Sin(a - 0.12):0.#} L{cx + 66 * Math.Cos(a):0.#},{cy + 66 * Math.Sin(a):0.#} L{cx + 46 * Math.Cos(a + 0.12):0.#},{cy + 46 * Math.Sin(a + 0.12):0.#} Z", S("#FFC93C"));
            ray.Opacity = 0.9;
        }
        O(s, 640, 74, 40, 40, R("#FFF7B0", "#FFB300"), "#F08C00", 2.5);
        P(s, "M626,70 Q630,64 634,70 M646,70 Q650,64 654,70", null, "#9A4B00", 2.2);
        P(s, "M628,82 Q640,94 652,82", null, "#9A4B00", 2.2);
        O(s, 621, 81, 5, 3, S("#FF8A65")).Opacity = 0.6;
        O(s, 659, 81, 5, 3, S("#FF8A65")).Opacity = 0.6;
        // Clouds
        foreach (var (x, y, k) in new[] { (300.0, 70.0, 1.0), (520.0, 150.0, 0.7), (80.0, 140.0, 0.6) })
        {
            var cloud = new Canvas { RenderTransform = new TransformGroup { Children = { new ScaleTransform(k, k), new TranslateTransform(x, y) } } };
            O(cloud, 0, 10, 34, 20, S("#FFFFFF"));
            O(cloud, 34, 0, 34, 28, S("#FFFFFF"));
            O(cloud, 70, 10, 30, 20, S("#FFFFFF"));
            O(cloud, 34, 18, 60, 14, S("#FFFFFF"));
            cloud.Opacity = 0.95;
            s.Children.Add(cloud);
        }
        // Seagulls
        foreach (var (x, y) in new[] { (430.0, 105.0), (462.0, 92.0), (210.0, 120.0) })
            P(s, $"M{x},{y} Q{x + 7},{y - 8} {x + 14},{y} Q{x + 21},{y - 8} {x + 28},{y}", null, "#37474F", 2.4);
    }

    private static void Sea(Canvas s)
    {
        P(s, "M0,205 H720 V290 H0 Z", V("#2EC4F5", "#0C8CE0", "#0A6FC2"));
        // Gentle wave bands and sparkles
        for (int row = 0; row < 3; row++)
        {
            double y = 222 + row * 18;
            var d = new System.Text.StringBuilder($"M0,{y}");
            for (double x = 0; x < 720; x += 60) d.Append($" Q{x + 15},{y - 5} {x + 30},{y} T{x + 60},{y}");
            P(s, d.ToString(), null, "#B8ECFF", 2).Opacity = 0.55 - row * 0.12;
        }
        var rng = new Random(7);
        for (int i = 0; i < 14; i++)
        {
            double x = rng.Next(20, 700), y = rng.Next(212, 270), r = rng.Next(3, 6);
            P(s, $"M{x},{y - r} L{x + r * 0.3},{y - r * 0.3} L{x + r},{y} L{x + r * 0.3},{y + r * 0.3} L{x},{y + r} L{x - r * 0.3},{y + r * 0.3} L{x - r},{y} L{x - r * 0.3},{y - r * 0.3} Z", S("#FFFFFF")).Opacity = 0.85;
        }
        // A little sailboat far out
        P(s, "M560,214 L600,214 L594,222 L566,222 Z", S("#E53935"), "#7F1D1D", 1);
        P(s, "M580,212 L580,184 L598,210 Z", S("#FFFFFF"), "#90A4AE", 1);
        P(s, "M578,212 L578,190 L566,210 Z", S("#FFE082"), "#90A4AE", 1);
    }

    private static void Sand(Canvas s)
    {
        // Foam where the waves meet the sand
        P(s, "M0,276 C60,266 120,284 200,272 C290,260 360,282 450,270 C540,258 620,280 720,268 V290 H0 Z", S("#E8FAFF"));
        P(s, "M0,284 C110,272 230,292 360,280 C480,268 600,290 720,276 V450 H0 Z", V("#FFE4A8", "#F9CF7E", "#EDB45E"));
        var rng = new Random(3);
        for (int i = 0; i < 70; i++)
            O(s, rng.Next(0, 720), rng.Next(300, 450), 1.4, 1, S("#D99A47")).Opacity = 0.5;
        // Shells and a starfish
        P(s, "M60,420 L64,408 L68,420 L80,421 L70,428 L74,440 L64,432 L54,440 L58,428 L48,421 Z", V("#FF8A80", "#F4511E"), "#B23A1A", 1.2);
        P(s, "M650,430 Q660,412 670,430 Z", V("#FFD1DC", "#F48FB1"), "#C2185B", 1);
        P(s, "M655,430 L660,418 M660,430 L660,416 M665,430 L661,418", null, "#C2185B", 0.8);
        P(s, "M330,438 Q338,424 346,438 Z", V("#E1BEE7", "#BA68C8"), "#6A1B9A", 1);
        // An umbrella on the far left
        P(s, "M30,300 L46,215", null, "#5D4037", 3.5);
        P(s, "M-14,232 Q46,168 106,206 Z", V("#FF5A8A", "#E91E63"), "#880E4F", 1.5);
        P(s, "M46,184 Q40,205 30,218 M46,184 Q62,198 70,212 M46,184 L48,210", null, "#FFFFFF", 2);
    }

    private static string Battlements(double x0, double x1, double top, double step = 14)
    {
        var d = new System.Text.StringBuilder($"M{x0},{top + 10}");
        for (double x = x0; x < x1 - 1; x += step)
        {
            double w = Math.Min(step / 2, x1 - x);
            d.Append($" L{x},{top} L{x + w},{top} L{x + w},{top + 10}");
            if (x + step < x1) d.Append($" L{x + step},{top + 10}");
        }
        return d.ToString();
    }

    private static void Tower(Canvas s, double x0, double x1, double top, double bottom, Brush sand)
    {
        string body = $"{Battlements(x0, x1, top)} L{x1},{top + 10} L{x1},{bottom} L{x0},{bottom} Z";
        P(s, body, sand, "#A0642A", 2);
        for (double y = top + 26; y < bottom - 6; y += 16)
            P(s, $"M{x0 + 4},{y} Q{(x0 + x1) / 2},{y + 3} {x1 - 4},{y}", null, "#D69A52", 1.2);
    }

    private static void Castle(Canvas s)
    {
        var sand = V("#FFE2A2", "#F2C177", "#DE9F55");
        O(s, 505, 376, 130, 12, S("#C98A40")).Opacity = 0.45;
        Tower(s, 400, 445, 228, 376, sand);
        Tower(s, 565, 610, 228, 376, sand);
        Tower(s, 425, 585, 290, 376, sand);
        Tower(s, 476, 534, 196, 292, sand);
        // Cone tops on the side towers
        P(s, "M398,230 L422.5,178 L447,230 Z", V("#FFE8B8", "#E6A85E"), "#A0642A", 2);
        P(s, "M563,230 L587.5,178 L612,230 Z", V("#FFE8B8", "#E6A85E"), "#A0642A", 2);
        // Flags
        P(s, "M505,198 V140", null, "#6D4C41", 2.5);
        P(s, "M505,142 Q525,136 540,146 Q524,150 505,160 Z", V("#FF5252", "#D50000"), "#7F0000", 1.2);
        P(s, "M422.5,180 V156 M587.5,180 V156", null, "#6D4C41", 2);
        P(s, "M422.5,157 L438,163 L422.5,169 Z", S("#29B6F6"), "#01579B", 1);
        P(s, "M587.5,157 L603,163 L587.5,169 Z", S("#FFD600"), "#8D6E00", 1);
        // Door and windows
        P(s, "M488,376 V344 A17,17 0 0 1 522,344 V376 Z", V("#A86A32", "#7A4518"), "#5D3410", 2);
        P(s, "M505,330 V376", null, "#5D3410", 1);
        foreach (var (x, y) in new[] { (415.0, 262.0), (580.0, 262.0), (497.0, 222.0), (449.0, 318.0), (553.0, 318.0) })
            P(s, $"M{x},{y + 18} V{y + 7} A6.5,6.5 0 0 1 {x + 13},{y + 7} V{y + 18} Z", S("#7A4518"), "#5D3410", 1.2);
        // Shells pressed into the walls
        foreach (var (x, y, color) in new[] { (432.0, 352.0, "#FF8A80"), (470.0, 340.0, "#80DEEA"), (540.0, 350.0, "#F48FB1"), (578.0, 340.0, "#FFD54F") })
        {
            P(s, $"M{x},{y} Q{x + 7},{y - 13} {x + 14},{y} Z", S(color), "#8D5524", 1);
            P(s, $"M{x + 4},{y} L{x + 7},{y - 9} M{x + 10},{y} L{x + 7},{y - 9}", null, "#8D5524", 0.7);
        }
        // A bucket and a spade beside it
        P(s, "M622,366 L652,366 L647,394 L627,394 Z", V("#40C4FF", "#0091EA"), "#01579B", 1.6);
        P(s, "M624,366 Q637,350 650,366", null, "#01579B", 1.6);
        P(s, "M380,330 L362,380", null, "#8D6E63", 4);
        P(s, "M354,378 L370,382 L364,396 L350,392 Z", V("#FF7043", "#E64A19"), "#8B2500", 1.4);
    }

    // ---------------- The children ----------------

    private enum Hair { Spiky, Pigtails, Tuft }

    /// <summary>A child, drawn in a 160 x 250 box (feet at the bottom), scaled and placed.</summary>
    private static Canvas Kid(Hair hair, string hairTop, string hairBottom, string eyes, Action<Canvas> clothes, Action<Canvas> arms, double x, double y, double scale)
    {
        const string skin = "#FFD8B5";
        var k = new Canvas { Width = 160, Height = 250, RenderTransform = new TransformGroup { Children = { new ScaleTransform(scale, scale), new TranslateTransform(x, y) } } };
        O(k, 80, 246, 46, 7, S("#8A5A20")).Opacity = 0.25;
        // Legs and feet
        Limb(k, "M68,175 L66,238", skin, 15);
        Limb(k, "M92,175 L94,238", skin, 15);
        O(k, 62, 241, 12, 6, S(skin), "#7A3F1D", 1.5);
        O(k, 98, 241, 12, 6, S(skin), "#7A3F1D", 1.5);
        // Long hair goes behind the head
        if (hair == Hair.Pigtails)
        {
            P(k, "M38,60 C30,98 38,124 50,130 L110,130 C122,124 130,98 122,60 Z", V(hairTop, hairBottom), "#4E2A10", 1.5);
            O(k, 26, 76, 13, 24, V(hairTop, hairBottom), "#4E2A10", 1.5);
            O(k, 134, 76, 13, 24, V(hairTop, hairBottom), "#4E2A10", 1.5);
            O(k, 34, 56, 6, 6, S("#FF4081"), "#880E4F", 1);
            O(k, 126, 56, 6, 6, S("#FF4081"), "#880E4F", 1);
        }
        // Body
        P(k, "M56,100 Q80,92 104,100 L108,160 Q80,166 52,160 Z", V("#FFE6CF", "#FFC79A"), "#7A3F1D", 1.5);
        clothes(k);
        arms(k);
        // Head
        O(k, 37, 64, 7, 9, S(skin), "#7A3F1D", 1.5);
        O(k, 123, 64, 7, 9, S(skin), "#7A3F1D", 1.5);
        O(k, 80, 60, 44, 42, R("#FFEEDD", "#FFCB9E"), "#7A3F1D", 2);
        // Big anime eyes
        foreach (double ex in new[] { 63.0, 97.0 })
        {
            O(k, ex, 66, 10.5, 13, S("#FFFFFF"), "#3B1E0E", 1.2);
            O(k, ex, 68, 8, 10.5, V(eyes, "#1A1030"));
            O(k, ex, 69, 4, 5.5, S("#0D0820"));
            O(k, ex - 3, 63, 3, 3.5, S("#FFFFFF"));
            O(k, ex + 3.5, 73, 1.6, 1.6, S("#FFFFFF"));
            P(k, $"M{ex - 12},{58} Q{ex},{50} {ex + 12},{58}", null, "#3B1E0E", 3);
            P(k, $"M{ex - 8},{46} Q{ex},{42} {ex + 8},{46}", null, "#5D3A1A", 2.2);
        }
        O(k, 50, 82, 8, 4.5, S("#FF8A80")).Opacity = 0.55;
        O(k, 110, 82, 8, 4.5, S("#FF8A80")).Opacity = 0.55;
        P(k, "M79,75 Q81.5,77 79,78.5", null, "#C77A4A", 1.2);
        // A big open smile
        P(k, "M68,85 Q80,102 92,85 Q80,89 68,85 Z", S("#9E2A2A"), "#5D1010", 1.5);
        P(k, "M73,92 Q80,98 87,92 Q80,94 73,92 Z", S("#FF8A8A"));
        P(k, "M70,86 Q80,89 90,86", null, "#FFFFFF", 1.6);
        // Hair on top
        switch (hair)
        {
            case Hair.Spiky:
                P(k, "M35,60 C29,32 50,10 80,11 C112,10 132,32 125,60 L119,42 L113,53 L105,35 L97,49 L87,31 L79,47 L69,31 L61,49 L53,36 L47,52 L41,41 Z", V(hairTop, hairBottom), "#2A1708", 1.8);
                P(k, "M80,11 L72,-2 L90,8 L100,-1 L99,14", V(hairTop, hairBottom), "#2A1708", 1.8);
                P(k, "M60,22 Q70,16 82,18", null, "#FFFFFF", 2).Opacity = 0.45;
                break;
            case Hair.Pigtails:
                P(k, "M36,60 C34,28 56,13 80,13 C104,13 126,28 124,60 C118,46 108,40 99,44 C93,34 86,33 80,40 C74,33 67,34 61,44 C52,40 42,46 36,60 Z", V(hairTop, hairBottom), "#4E2A10", 1.8);
                P(k, "M58,24 Q70,18 84,20", null, "#FFFFFF", 2).Opacity = 0.45;
                break;
            case Hair.Tuft:
                P(k, "M40,54 C40,26 60,16 80,16 C100,16 120,26 120,54 C112,42 104,38 96,42 C90,32 70,32 64,42 C56,38 46,42 40,54 Z", V(hairTop, hairBottom), "#8D6E00", 1.8);
                P(k, "M78,17 C74,6 84,2 88,8 C84,8 81,11 82,17 Z", V(hairTop, hairBottom), "#8D6E00", 1.5);
                break;
        }
        return k;
    }

    private static void Kids(Canvas s)
    {
        // The hero: a smiling boy in red trunks, waving, his bucket in the other hand
        s.Children.Add(Kid(Hair.Spiky, "#6B4423", "#3E2410", "#2E7BD6",
            clothes: k =>
            {
                P(k, "M51,152 H109 L113,192 H86 L80,180 L74,192 H47 Z", V("#FF5A5A", "#D32F2F"), "#7F1010", 1.6);
                P(k, "M50,166 H110", null, "#FFFFFF", 3);
                foreach (var (fx, fy) in new[] { (60.0, 180.0), (100.0, 178.0) }) O(k, fx, fy, 3, 3, S("#FFEB3B"));
            },
            arms: k =>
            {
                const string skin = "#FFD8B5";
                Limb(k, "M56,106 Q42,132 44,160", skin, 13);
                P(k, "M28,160 H60 L56,188 H32 Z", V("#FFE14D", "#FFB300"), "#8D5A00", 1.6);
                P(k, "M30,162 Q44,144 58,162", null, "#8D5A00", 1.6);
                O(k, 44, 160, 7, 7, S(skin), "#7A3F1D", 1.5);
                Limb(k, "M104,106 Q122,88 130,62", skin, 13);
                O(k, 131, 57, 9, 9, S(skin), "#7A3F1D", 1.5);
                P(k, "M124,50 L126,44 M131,47 L132,40 M138,50 L140,44", null, "#7A3F1D", 1.4);
            },
            x: 175, y: 182, scale: 1.0));

        // A girl with a beach ball
        O(s, 668, 214, 22, 22, R("#FFFFFF", "#E0E0E0"), "#455A64", 1.5);
        P(s, "M668,192 Q656,214 668,236 Q660,214 668,192 Z", S("#FF5252"));
        P(s, "M668,192 Q682,214 668,236 Q676,214 668,192 Z", S("#2979FF"));
        P(s, "M646,214 Q668,204 690,214 Q668,208 646,214 Z", S("#FFD600"));
        O(s, 662, 205, 5, 3, S("#FFFFFF")).Opacity = 0.8;
        s.Children.Add(Kid(Hair.Pigtails, "#F08A3C", "#B5501E", "#3E9E5A",
            clothes: k =>
            {
                P(k, "M53,100 Q80,110 107,100 L110,176 Q80,186 50,176 Z", V("#FF6FAE", "#E91E63"), "#880E4F", 1.6);
                foreach (var (dx, dy) in new[] { (64.0, 124.0), (90.0, 118.0), (76.0, 146.0), (98.0, 156.0), (60.0, 162.0) }) O(k, dx, dy, 3.2, 3.2, S("#FFFFFF"));
                P(k, "M60,100 L64,92 M100,100 L96,92", null, "#880E4F", 2);
            },
            arms: k =>
            {
                const string skin = "#FFD8B5";
                Limb(k, "M58,106 Q44,80 50,52", skin, 12);
                Limb(k, "M102,106 Q116,80 110,52", skin, 12);
                O(k, 50, 48, 8, 8, S(skin), "#7A3F1D", 1.5);
                O(k, 110, 48, 8, 8, S(skin), "#7A3F1D", 1.5);
            },
            x: 612, y: 248, scale: 0.72));

        // The little one, digging with a spade
        s.Children.Add(Kid(Hair.Tuft, "#FFE066", "#F2B705", "#8E5BD6",
            clothes: k =>
            {
                P(k, "M51,152 H109 L112,186 H86 L80,176 L74,186 H48 Z", V("#69F0AE", "#00C853"), "#1B5E20", 1.6);
                P(k, "M60,160 L64,172 M70,158 L74,170 M90,158 L94,170 M100,160 L104,172", null, "#FFFFFF", 1.6);
            },
            arms: k =>
            {
                const string skin = "#FFD8B5";
                Limb(k, "M56,106 Q44,126 52,148", skin, 13);
                O(k, 54, 150, 7, 7, S(skin), "#7A3F1D", 1.5);
                P(k, "M120,148 L148,196", null, "#8D6E63", 5);
                P(k, "M140,192 L158,190 L160,212 L144,214 Z", V("#B388FF", "#7C4DFF"), "#311B92", 1.6);
                Limb(k, "M104,106 Q118,128 122,148", skin, 13);
                O(k, 122, 150, 7, 7, S(skin), "#7A3F1D", 1.5);
            },
            x: 62, y: 292, scale: 0.6));
        P(s, "M128,430 Q150,404 176,430 Z", V("#FFE4A8", "#E5A955"), "#A0642A", 1.5); // the little one's own sand pile
        P(s, "M152,412 V396", null, "#6D4C41", 1.5);
        P(s, "M152,396 L162,400 L152,404 Z", S("#00E5FF"), "#006064", 1);
    }

    // ---------------- Title ----------------

    private static void TitleAndFooter(Canvas s)
    {
        var face = new Typeface(new FontFamily("Segoe UI Black, Arial Black, Segoe UI"), FontStyles.Normal, FontWeights.Black, FontStretches.Normal);
        var text = new FormattedText("Sandcastle RPG Maker", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, face, 52, Brushes.Black, 1.0);
        var geometry = text.BuildGeometry(new Point(26, 16));
        var shadow = new Path { Data = geometry, Fill = S("#7A2E00"), RenderTransform = new TranslateTransform(3, 4), Opacity = 0.5 };
        s.Children.Add(shadow);
        s.Children.Add(new Path { Data = geometry, Stroke = S("#FFFFFF"), StrokeThickness = 9, StrokeLineJoin = PenLineJoin.Round });
        s.Children.Add(new Path { Data = geometry, Fill = V("#FFEA5C", "#FFB020", "#FF6A00"), Stroke = S("#8A3300"), StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round });
        var shine = new Path { Data = geometry, Fill = new LinearGradientBrush(Color.FromArgb(150, 255, 255, 255), Color.FromArgb(0, 255, 255, 255), new Point(0, 0), new Point(0, 0.5)) };
        s.Children.Add(shine);
        var sub = new TextBlock
        {
            Text = "Build worlds that live and breathe",
            FontFamily = new FontFamily("Segoe UI"), FontSize = 17, FontWeight = FontWeights.SemiBold, FontStyle = FontStyles.Italic,
            Foreground = S("#0D47A1"), Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 6, ShadowDepth = 0, Opacity = 1 }
        };
        Canvas.SetLeft(sub, 32);
        Canvas.SetTop(sub, 86);
        s.Children.Add(sub);

        var strip = new Border { Width = Width, Height = 26, Background = new SolidColorBrush(Color.FromArgb(120, 20, 40, 70)) };
        Canvas.SetTop(strip, Height - 26);
        s.Children.Add(strip);
        var loading = new TextBlock { Text = "Loading the editor...", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold };
        Canvas.SetLeft(loading, 14);
        Canvas.SetTop(loading, Height - 21);
        s.Children.Add(loading);
        var build = new TextBlock { Text = BuildInfo.CurrentBuild, Foreground = Brushes.White, FontSize = 11, Opacity = 0.85, TextAlignment = TextAlignment.Right, Width = 200 };
        Canvas.SetLeft(build, Width - 214);
        Canvas.SetTop(build, Height - 20);
        s.Children.Add(build);
    }
}
