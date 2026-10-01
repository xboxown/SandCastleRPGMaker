using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Visual Ultima 7 / Exult-style 3D size editor for anything placed in the
/// world (books, containers, objects, and any future item editor): the
/// sprite as the game draws it, with its solid box drawn over it exactly the
/// way the engine sees it - the footprint on the floor (X wide, Y deep) and
/// the top face lifted by its height Z. In Exult every lift of height draws
/// 4 pixels up-and-left (drag.cc drop_at_lift: "x += at_lift * 4"), so the
/// top face is the footprint shifted diagonally by Z.
///
/// Drag the colored handles to resize - X and Y snap to 8px (one Exult
/// tile), Z snaps to 4px (one lift): 4, 8, 12, 16, 20... A walker can step
/// up or down one lift (4px) at a time, so anything 4px tall can be stepped
/// onto and anything taller blocks unless there's a step beside it.
/// "Automated" sizes the box from the sprite's own visible pixels.
///
/// Values are stored the way the engine uses them: SizeX/SizeY in 8px world
/// units, SizeZ in lifts.
/// </summary>
public class SolidSizeEditor : UserControl
{
    private const int UnitPixels = 8;  // one Exult tile / SpriteSizing.BaseUnit
    private const int LiftPixels = 4;  // one Exult lift, as drawn
    private const double CanvasWidth = 280, CanvasHeight = 220;
    private const double HandleSize = 9;

    private readonly Canvas _canvas = new() { Width = CanvasWidth, Height = CanvasHeight, Background = new SolidColorBrush(Color.FromRgb(0x2A, 0x2A, 0x2A)), ClipToBounds = true };
    private readonly TextBlock _xText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
    private readonly TextBlock _yText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
    private readonly TextBlock _zText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
    private readonly TextBlock _climbText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8, Margin = new Thickness(0, 4, 0, 0), MaxWidth = CanvasWidth };

    private BitmapSource? _sprite;
    private bool _solid;

    /// <summary>Footprint width in 8px world units.</summary>
    public int SizeX { get; private set; } = 1;
    /// <summary>Footprint depth in 8px world units.</summary>
    public int SizeY { get; private set; } = 1;
    /// <summary>Height in lifts (4px each).</summary>
    public int SizeZ { get; private set; } = 1;

    /// <summary>Raised whenever the user changes the size (handles, +/- buttons, Automated) - never when the host loads values in.</summary>
    public event EventHandler? SizeEdited;

    // Layout frozen for the duration of a handle drag, so the view doesn't re-fit (and jump) under the mouse.
    private double _zoom = 1;
    private Point _worldOrigin; // world point shown at the canvas's top-left
    private char _dragging;     // 'X', 'Y', 'Z' or '\0'

    public SolidSizeEditor()
    {
        RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.NearestNeighbor);
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += Canvas_MouseLeftButtonUp;
        _canvas.LostMouseCapture += (_, _) => { if (_dragging != '\0') { _dragging = '\0'; Redraw(refit: true); } };

        var automated = new Button { Content = "Automated", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Size the solid box from how the sprite looks (its visible pixels)" };
        automated.Click += (_, _) => Automate();

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Solid size (Ultima 7 / Exult)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(new TextBlock { Text = "Drag the handles: red = width X, green = depth Y, blue = height Z.", FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap, MaxWidth = CanvasWidth });
        panel.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _canvas, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(BuildRow(_xText, () => Change(dx: -1), () => Change(dx: +1), Brushes.IndianRed));
        panel.Children.Add(BuildRow(_yText, () => Change(dy: -1), () => Change(dy: +1), Brushes.MediumSeaGreen));
        panel.Children.Add(BuildRow(_zText, () => Change(dz: -1), () => Change(dz: +1), Brushes.CornflowerBlue));
        panel.Children.Add(automated);
        panel.Children.Add(_climbText);
        Content = panel;
        Redraw(refit: true);
    }

    private static UIElement BuildRow(TextBlock text, Action minus, Action plus, Brush swatch)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(new Rectangle { Width = 10, Height = 10, Fill = swatch, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(text);
        var minusButton = new Button { Content = "-", Width = 24, Margin = new Thickness(4, 0, 2, 0) };
        minusButton.Click += (_, _) => minus();
        var plusButton = new Button { Content = "+", Width = 24 };
        plusButton.Click += (_, _) => plus();
        row.Children.Add(minusButton);
        row.Children.Add(plusButton);
        return row;
    }

    /// <summary>Shows a thing's sprite and current size. Never raises SizeEdited.</summary>
    public void Load(BitmapSource? sprite, int sizeX, int sizeY, int sizeZ, bool solid)
    {
        _sprite = sprite;
        _solid = solid;
        SizeX = Math.Max(1, sizeX);
        SizeY = Math.Max(1, sizeY);
        SizeZ = Math.Max(1, sizeZ);
        Redraw(refit: true);
    }

    /// <summary>The sprite changed (a different picture was chosen) - the size is kept.</summary>
    public void SetSprite(BitmapSource? sprite)
    {
        _sprite = sprite;
        Redraw(refit: true);
    }

    /// <summary>Solid only changes how the box is drawn - a non-solid thing never blocks anyone, so its box is shown dashed and grey.</summary>
    public void SetSolid(bool solid)
    {
        _solid = solid;
        Redraw(refit: false);
    }

    private void Change(int dx = 0, int dy = 0, int dz = 0) =>
        SetSize(SizeX + dx, SizeY + dy, SizeZ + dz, refit: true);

    private void SetSize(int x, int y, int z, bool refit)
    {
        x = Math.Max(1, x); y = Math.Max(1, y); z = Math.Max(1, z);
        if (x == SizeX && y == SizeY && z == SizeZ) { Redraw(refit); return; }
        SizeX = x; SizeY = y; SizeZ = z;
        Redraw(refit);
        SizeEdited?.Invoke(this, EventArgs.Empty);
    }

    // ================= The sprite as the game draws it =================

    /// <summary>The sprite's box in the game: its pixel size rounded up to whole 8px units (SpriteSizing.Quantize), bottom-center anchored on the thing's position.</summary>
    private (double Width, double Height) SpriteBox()
    {
        if (_sprite is null) return (UnitPixels * 4, UnitPixels * 4);
        var (w, h) = SpriteSizing.Quantize(_sprite.PixelWidth, _sprite.PixelHeight);
        return (w, h);
    }

    /// <summary>
    /// "Automated": measures the sprite's visible (non-transparent) pixels as
    /// the game draws them and fits a box to them. Exult draws a solid's
    /// footprint on the floor and its height rising up-left from it, so the
    /// visible width becomes the footprint width X, the lower part of the
    /// visible height (half, at most the width) becomes the footprint depth
    /// Y, and the rest of the visible height becomes Z - rounded to whole 8px
    /// units and whole 4px lifts. A starting point; adjust by hand after.
    /// </summary>
    private void Automate()
    {
        if (_sprite is null) return;
        var visible = VisibleBounds(_sprite);
        if (visible is null) return;
        var (boxW, boxH) = SpriteBox();
        double scale = Math.Min(boxW / _sprite.PixelWidth, boxH / _sprite.PixelHeight); // the game's Stretch.Uniform into the quantized box
        double visibleWidth = visible.Value.Width * scale;
        double visibleHeight = visible.Value.Height * scale;

        int x = Math.Max(1, (int)Math.Round(visibleWidth / UnitPixels));
        int y = Math.Max(1, (int)Math.Round(Math.Min(visibleHeight / 2, x * UnitPixels) / UnitPixels));
        int z = Math.Max(1, (int)Math.Round((visibleHeight - y * UnitPixels) / LiftPixels));
        SetSize(x, y, z, refit: true);
    }

    private static Int32Rect? VisibleBounds(BitmapSource bitmap)
    {
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth, height = converted.PixelHeight;
        if (width == 0 || height == 0) return null;
        int stride = width * 4;
        var pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);
        int minX = width, minY = height, maxX = -1, maxY = -1;
        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                if (pixels[py * stride + px * 4 + 3] == 0) continue;
                if (px < minX) minX = px;
                if (px > maxX) maxX = px;
                if (py < minY) minY = py;
                if (py > maxY) maxY = py;
            }
        }
        return maxX < 0 ? null : new Int32Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    // ================= Drawing =================

    // World coordinates (game pixels): the thing's anchor - the bottom-center of its sprite and of its footprint - is (0, 0); up is negative Y.
    private double FootWidth => SizeX * UnitPixels;
    private double FootDepth => SizeY * UnitPixels;
    private double Lift => SizeZ * LiftPixels;

    private Point ToScreen(double worldX, double worldY) => new((worldX - _worldOrigin.X) * _zoom, (worldY - _worldOrigin.Y) * _zoom);
    private Point ToWorld(Point screen) => new(screen.X / _zoom + _worldOrigin.X, screen.Y / _zoom + _worldOrigin.Y);

    private void Redraw(bool refit)
    {
        var (spriteW, spriteH) = SpriteBox();
        if (refit && _dragging == '\0')
        {
            // Fit sprite + box (+ a margin) into the canvas at a whole-number zoom, pixel art stays crisp.
            double left = Math.Min(-spriteW / 2, -FootWidth / 2 - Lift) - 6;
            double right = Math.Max(spriteW / 2, FootWidth / 2) + 6;
            double top = Math.Min(-spriteH, -FootDepth - Lift) - 6;
            double bottom = 6;
            _zoom = Math.Clamp(Math.Floor(Math.Min(CanvasWidth / (right - left), CanvasHeight / (bottom - top))), 1, 8);
            // Center the content in the canvas.
            _worldOrigin = new Point((left + right) / 2 - CanvasWidth / _zoom / 2, (top + bottom) / 2 - CanvasHeight / _zoom / 2);
        }

        _canvas.Children.Clear();

        // The ground line through the anchor, for orientation.
        var groundA = ToScreen(-1000, 0);
        var groundB = ToScreen(1000, 0);
        _canvas.Children.Add(new Line { X1 = groundA.X, Y1 = groundA.Y, X2 = groundB.X, Y2 = groundB.Y, Stroke = Brushes.DimGray, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 3 } });

        if (_sprite is not null)
        {
            var spriteTopLeft = ToScreen(-spriteW / 2, -spriteH);
            var image = new Image { Source = _sprite, Width = spriteW * _zoom, Height = spriteH * _zoom, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(image, spriteTopLeft.X);
            Canvas.SetTop(image, spriteTopLeft.Y);
            _canvas.Children.Add(image);
        }
        else
        {
            _canvas.Children.Add(new TextBlock { Text = "(no sprite chosen yet)", Foreground = Brushes.Gray, Margin = new Thickness(6) });
        }

        // The solid box: floor (footprint) and top face (footprint lifted by Z, i.e. shifted up-left by Z*4px), joined at the corners.
        Brush stroke = _solid ? Brushes.Orange : Brushes.Gray;
        var dash = _solid ? null : new DoubleCollection { 3, 2 };
        double fl = -FootWidth / 2, fr = FootWidth / 2, ft = -FootDepth, fb = 0;
        Point[] floor = { ToScreen(fl, ft), ToScreen(fr, ft), ToScreen(fr, fb), ToScreen(fl, fb) };
        Point[] topFace = { ToScreen(fl - Lift, ft - Lift), ToScreen(fr - Lift, ft - Lift), ToScreen(fr - Lift, fb - Lift), ToScreen(fl - Lift, fb - Lift) };
        _canvas.Children.Add(new Polygon { Points = new PointCollection(floor), Stroke = stroke, StrokeThickness = 1.5, StrokeDashArray = dash, Fill = new SolidColorBrush(Color.FromArgb(50, 255, 165, 0)) });
        for (int i = 0; i < 4; i++)
            _canvas.Children.Add(new Line { X1 = floor[i].X, Y1 = floor[i].Y, X2 = topFace[i].X, Y2 = topFace[i].Y, Stroke = stroke, StrokeThickness = 1, StrokeDashArray = dash });
        _canvas.Children.Add(new Polygon { Points = new PointCollection(topFace), Stroke = stroke, StrokeThickness = 1.5, StrokeDashArray = dash, Fill = new SolidColorBrush(Color.FromArgb(70, 255, 200, 80)) });

        // Handles: X on the floor's front-right corner (drag sideways), Y on the floor's back-right corner (drag up/down), Z on the top face's back-left corner (drag up/down).
        AddHandle('X', ToScreen(fr, fb), Brushes.IndianRed, Cursors.SizeWE, "Drag sideways: width X (8px steps)");
        AddHandle('Y', ToScreen(fr, ft), Brushes.MediumSeaGreen, Cursors.SizeNS, "Drag up/down: depth Y (8px steps)");
        AddHandle('Z', ToScreen(fl - Lift, ft - Lift), Brushes.CornflowerBlue, Cursors.SizeNS, "Drag up/down: height Z (4px steps = 1 lift)");

        _xText.Text = $"Width X: {SizeX * UnitPixels} px ({SizeX} unit{(SizeX == 1 ? "" : "s")})";
        _yText.Text = $"Depth Y: {SizeY * UnitPixels} px ({SizeY} unit{(SizeY == 1 ? "" : "s")})";
        _zText.Text = $"Height Z: {SizeZ * LiftPixels} px ({SizeZ} lift{(SizeZ == 1 ? "" : "s")})";
        _climbText.Text = !_solid
            ? "Not solid: never blocks anyone - walked through, never stood on."
            : SizeZ <= 1
                ? "Solid, 4px tall (1 lift): a walker can step up onto it."
                : $"Solid, {SizeZ * LiftPixels}px tall ({SizeZ} lifts): blocks a walker - it needs something {(SizeZ - 1) * LiftPixels}px tall beside it as a step to climb on.";
    }

    private void AddHandle(char which, Point at, Brush fill, Cursor cursor, string tip)
    {
        var handle = new Rectangle { Width = HandleSize, Height = HandleSize, Fill = fill, Stroke = Brushes.Black, StrokeThickness = 1, Cursor = cursor, ToolTip = tip };
        Canvas.SetLeft(handle, at.X - HandleSize / 2);
        Canvas.SetTop(handle, at.Y - HandleSize / 2);
        handle.MouseLeftButtonDown += (_, e) =>
        {
            _dragging = which;
            // Capture on the canvas itself, which survives every redraw - the handle element is rebuilt on each move.
            _canvas.CaptureMouse();
            e.Handled = true;
        };
        _canvas.Children.Add(handle);
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == '\0') return;
        var world = ToWorld(e.GetPosition(_canvas));
        switch (_dragging)
        {
            case 'X': SetSize((int)Math.Round(2 * world.X / UnitPixels), SizeY, SizeZ, refit: false); break;
            case 'Y': SetSize(SizeX, (int)Math.Round(-world.Y / UnitPixels), SizeZ, refit: false); break;
            // The top face's back edge sits at -(depth + lift); whatever is above the footprint's back edge is height.
            case 'Z': SetSize(SizeX, SizeY, (int)Math.Round((-world.Y - FootDepth) / LiftPixels), refit: false); break;
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragging == '\0') return;
        _dragging = '\0';
        _canvas.ReleaseMouseCapture();
        Redraw(refit: true);
    }

    // ================= Loading a sprite for the host editors =================

    /// <summary>Frame 1 of a Sprite Editor sprite (group + name, falling back to a search by name alone), or null.</summary>
    public static BitmapSource? LoadSpriteFrame(SpriteDatabase sprites, string? group, string? name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var sprite = sprites.Groups.FirstOrDefault(g => g.Name == group)?.Sprites.FirstOrDefault(s => s.Name == name)
            ?? sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == name);
        return sprite is null || sprite.Frames.Count == 0 ? null : LoadImageFile(sprite.Frames[0].FilePath);
    }

    public static BitmapSource? LoadImageFile(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
