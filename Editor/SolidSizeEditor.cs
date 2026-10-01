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
/// tile), Z snaps to 4px (one lift): 4, 8, 12, 16, 20... Drag the box itself
/// (its floor, or the yellow handle) to move it under the art, in 8px steps.
/// A walker can step up or down one lift (4px) at a time, so anything 4px
/// tall can be stepped onto and anything taller blocks unless there's a step
/// beside it. "Automated" sizes and places the box from the sprite's own
/// visible pixels.
///
/// Values are stored the way the engine uses them: SizeX/SizeY and the
/// offsets in 8px world units, SizeZ in lifts.
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
    private readonly TextBlock _positionText = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 150 };
    private readonly TextBlock _climbText = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.8, Margin = new Thickness(0, 4, 0, 0), MaxWidth = CanvasWidth };

    private BitmapSource? _sprite;
    private bool _solid;

    /// <summary>Footprint width in 8px world units.</summary>
    public int SizeX { get; private set; } = 1;
    /// <summary>Footprint depth in 8px world units.</summary>
    public int SizeY { get; private set; } = 1;
    /// <summary>Height in lifts (4px each).</summary>
    public int SizeZ { get; private set; } = 1;
    /// <summary>Where the box sits relative to the sprite's bottom-center, in 8px world units (right/down positive).</summary>
    public int OffsetX { get; private set; }
    public int OffsetY { get; private set; }

    /// <summary>Raised whenever the user changes the size or position (handles, dragging, +/- buttons, Automated, Center) - never when the host loads values in.</summary>
    public event EventHandler? SizeEdited;

    // Layout frozen for the duration of a drag, so the view doesn't re-fit (and jump) under the mouse.
    private double _zoom = 1;
    private Point _worldOrigin; // world point shown at the canvas's top-left
    private char _dragging;     // 'X', 'Y', 'Z', 'M' (move) or '\0'
    private Vector _moveGrab;   // pointer position relative to the box's anchor when a move started

    public SolidSizeEditor()
    {
        RenderOptions.SetBitmapScalingMode(_canvas, BitmapScalingMode.NearestNeighbor);
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += Canvas_MouseLeftButtonUp;
        _canvas.LostMouseCapture += (_, _) => { if (_dragging != '\0') { _dragging = '\0'; Redraw(refit: true); } };

        var automated = new Button { Content = "Automated", Padding = new Thickness(10, 2, 10, 2), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left, ToolTip = "Size and place the solid box from how the sprite looks (its visible pixels)" };
        automated.Click += (_, _) => Automate();

        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = "Solid size (Ultima 7 / Exult)", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(new TextBlock { Text = "Drag the handles: red = width X, green = depth Y, blue = height Z. Drag the box (or the yellow handle) to move it under the art.", FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap, MaxWidth = CanvasWidth });
        panel.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), Child = _canvas, HorizontalAlignment = HorizontalAlignment.Left });
        panel.Children.Add(BuildRow(_xText, Brushes.IndianRed, ("-", () => Change(dx: -1)), ("+", () => Change(dx: +1))));
        panel.Children.Add(BuildRow(_yText, Brushes.MediumSeaGreen, ("-", () => Change(dy: -1)), ("+", () => Change(dy: +1))));
        panel.Children.Add(BuildRow(_zText, Brushes.CornflowerBlue, ("-", () => Change(dz: -1)), ("+", () => Change(dz: +1))));
        panel.Children.Add(BuildRow(_positionText, Brushes.Gold,
            ("←", () => Change(ox: -1)), ("→", () => Change(ox: +1)), ("↑", () => Change(oy: -1)), ("↓", () => Change(oy: +1)),
            ("Center", () => SetBox(SizeX, SizeY, SizeZ, 0, 0, refit: true))));
        panel.Children.Add(automated);
        panel.Children.Add(_climbText);
        Content = panel;
        Redraw(refit: true);
    }

    private static UIElement BuildRow(TextBlock text, Brush swatch, params (string Label, Action OnClick)[] buttons)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(new Rectangle { Width = 10, Height = 10, Fill = swatch, Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(text);
        foreach (var (label, onClick) in buttons)
        {
            var button = new Button { Content = label, MinWidth = 24, Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(2, 0, 0, 0) };
            button.Click += (_, _) => onClick();
            row.Children.Add(button);
        }
        return row;
    }

    /// <summary>Shows a thing's sprite, current size and box position. Never raises SizeEdited.</summary>
    public void Load(BitmapSource? sprite, int sizeX, int sizeY, int sizeZ, int offsetX, int offsetY, bool solid)
    {
        _sprite = sprite;
        _solid = solid;
        SizeX = Math.Max(1, sizeX);
        SizeY = Math.Max(1, sizeY);
        SizeZ = Math.Max(1, sizeZ);
        OffsetX = offsetX;
        OffsetY = offsetY;
        Redraw(refit: true);
    }

    /// <summary>The sprite changed (a different picture was chosen) - the size and position are kept.</summary>
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

    private void Change(int dx = 0, int dy = 0, int dz = 0, int ox = 0, int oy = 0) =>
        SetBox(SizeX + dx, SizeY + dy, SizeZ + dz, OffsetX + ox, OffsetY + oy, refit: true);

    private void SetBox(int x, int y, int z, int ox, int oy, bool refit)
    {
        x = Math.Max(1, x); y = Math.Max(1, y); z = Math.Max(1, z);
        if (x == SizeX && y == SizeY && z == SizeZ && ox == OffsetX && oy == OffsetY) { Redraw(refit); return; }
        SizeX = x; SizeY = y; SizeZ = z; OffsetX = ox; OffsetY = oy;
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
    /// units and whole 4px lifts. The box is then placed under the visible
    /// art: centered on it, its floor's front edge on the art's bottom edge.
    /// A starting point; adjust by hand after.
    /// </summary>
    private void Automate()
    {
        if (_sprite is null) return;
        var visible = VisibleBounds(_sprite);
        if (visible is null) return;
        var (boxW, boxH) = SpriteBox();
        // The game draws the bitmap Stretch.Uniform into its quantized box, centered, bottom-center anchored at (0, 0).
        double scale = Math.Min(boxW / _sprite.PixelWidth, boxH / _sprite.PixelHeight);
        double drawnLeft = -boxW / 2 + (boxW - _sprite.PixelWidth * scale) / 2;
        double drawnTop = -boxH + (boxH - _sprite.PixelHeight * scale) / 2;
        double visibleLeft = drawnLeft + visible.Value.X * scale;
        double visibleTop = drawnTop + visible.Value.Y * scale;
        double visibleWidth = visible.Value.Width * scale;
        double visibleHeight = visible.Value.Height * scale;

        int x = Math.Max(1, (int)Math.Round(visibleWidth / UnitPixels));
        int y = Math.Max(1, (int)Math.Round(Math.Min(visibleHeight / 2, x * UnitPixels) / UnitPixels));
        int z = Math.Max(1, (int)Math.Round((visibleHeight - y * UnitPixels) / LiftPixels));
        int ox = (int)Math.Round((visibleLeft + visibleWidth / 2) / UnitPixels);
        int oy = (int)Math.Round((visibleTop + visibleHeight) / UnitPixels);
        SetBox(x, y, z, ox, oy, refit: true);
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

    // World coordinates (game pixels): the thing's position - the bottom-center of its sprite - is (0, 0); up is negative Y.
    // The box's own anchor (bottom-center of its floor) is that plus the offset.
    private double FootWidth => SizeX * UnitPixels;
    private double FootDepth => SizeY * UnitPixels;
    private double Lift => SizeZ * LiftPixels;
    private double AnchorX => OffsetX * UnitPixels;
    private double AnchorY => OffsetY * UnitPixels;

    private Point ToScreen(double worldX, double worldY) => new((worldX - _worldOrigin.X) * _zoom, (worldY - _worldOrigin.Y) * _zoom);
    private Point ToWorld(Point screen) => new(screen.X / _zoom + _worldOrigin.X, screen.Y / _zoom + _worldOrigin.Y);

    private void Redraw(bool refit)
    {
        var (spriteW, spriteH) = SpriteBox();
        double fl = AnchorX - FootWidth / 2, fr = AnchorX + FootWidth / 2, ft = AnchorY - FootDepth, fb = AnchorY;
        if (refit && _dragging == '\0')
        {
            // Fit sprite + box (+ a margin) into the canvas at a whole-number zoom, so pixel art stays crisp.
            double left = Math.Min(-spriteW / 2, fl - Lift) - 6;
            double right = Math.Max(spriteW / 2, fr) + 6;
            double top = Math.Min(-spriteH, ft - Lift) - 6;
            double bottom = Math.Max(0, fb) + 6;
            _zoom = Math.Clamp(Math.Floor(Math.Min(CanvasWidth / (right - left), CanvasHeight / (bottom - top))), 1, 8);
            _worldOrigin = new Point((left + right) / 2 - CanvasWidth / _zoom / 2, (top + bottom) / 2 - CanvasHeight / _zoom / 2);
        }

        _canvas.Children.Clear();

        // The ground line through the thing's position, for orientation.
        var groundA = ToScreen(-1000, 0);
        var groundB = ToScreen(1000, 0);
        _canvas.Children.Add(new Line { X1 = groundA.X, Y1 = groundA.Y, X2 = groundB.X, Y2 = groundB.Y, Stroke = Brushes.DimGray, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 2, 3 } });

        if (_sprite is not null)
        {
            var spriteTopLeft = ToScreen(-spriteW / 2, -spriteH);
            var image = new Image { Source = _sprite, Width = spriteW * _zoom, Height = spriteH * _zoom, Stretch = Stretch.Uniform, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(image, spriteTopLeft.X);
            Canvas.SetTop(image, spriteTopLeft.Y);
            _canvas.Children.Add(image);
        }
        else
        {
            _canvas.Children.Add(new TextBlock { Text = "(no sprite chosen yet)", Foreground = Brushes.Gray, Margin = new Thickness(6), IsHitTestVisible = false });
        }

        // The solid box: floor (footprint) and top face (footprint lifted by Z, i.e. shifted up-left by Z*4px), joined at the corners.
        Brush stroke = _solid ? Brushes.Orange : Brushes.Gray;
        var dash = _solid ? null : new DoubleCollection { 3, 2 };
        Point[] floor = { ToScreen(fl, ft), ToScreen(fr, ft), ToScreen(fr, fb), ToScreen(fl, fb) };
        Point[] topFace = { ToScreen(fl - Lift, ft - Lift), ToScreen(fr - Lift, ft - Lift), ToScreen(fr - Lift, fb - Lift), ToScreen(fl - Lift, fb - Lift) };
        for (int i = 0; i < 4; i++)
            _canvas.Children.Add(new Line { X1 = floor[i].X, Y1 = floor[i].Y, X2 = topFace[i].X, Y2 = topFace[i].Y, Stroke = stroke, StrokeThickness = 1, StrokeDashArray = dash, IsHitTestVisible = false });
        _canvas.Children.Add(new Polygon { Points = new PointCollection(topFace), Stroke = stroke, StrokeThickness = 1.5, StrokeDashArray = dash, Fill = new SolidColorBrush(Color.FromArgb(70, 255, 200, 80)), IsHitTestVisible = false });
        // The floor is drawn last and is the grab area for moving the whole box.
        var floorPolygon = new Polygon { Points = new PointCollection(floor), Stroke = stroke, StrokeThickness = 1.5, StrokeDashArray = dash, Fill = new SolidColorBrush(Color.FromArgb(60, 255, 165, 0)), Cursor = Cursors.SizeAll, ToolTip = "Drag to move the box (8px steps)" };
        floorPolygon.MouseLeftButtonDown += (_, e) => BeginMove(e);
        _canvas.Children.Add(floorPolygon);

        // Handles: X on the floor's front-right corner (drag sideways), Y on the floor's back-right corner (drag up/down), Z on the top face's back-left corner (drag up/down), move in the floor's middle.
        AddHandle('X', ToScreen(fr, fb), Brushes.IndianRed, Cursors.SizeWE, "Drag sideways: width X (8px steps)");
        AddHandle('Y', ToScreen(fr, ft), Brushes.MediumSeaGreen, Cursors.SizeNS, "Drag up/down: depth Y (8px steps)");
        AddHandle('Z', ToScreen(fl - Lift, ft - Lift), Brushes.CornflowerBlue, Cursors.SizeNS, "Drag up/down: height Z (4px steps = 1 lift)");
        AddHandle('M', ToScreen(AnchorX, AnchorY - FootDepth / 2), Brushes.Gold, Cursors.SizeAll, "Drag to move the box (8px steps)");

        _xText.Text = $"Width X: {SizeX * UnitPixels} px ({SizeX} unit{(SizeX == 1 ? "" : "s")})";
        _yText.Text = $"Depth Y: {SizeY * UnitPixels} px ({SizeY} unit{(SizeY == 1 ? "" : "s")})";
        _zText.Text = $"Height Z: {SizeZ * LiftPixels} px ({SizeZ} lift{(SizeZ == 1 ? "" : "s")})";
        _positionText.Text = $"Position: {Signed(OffsetX * UnitPixels)} px, {Signed(OffsetY * UnitPixels)} px";
        _climbText.Text = !_solid
            ? "Not solid: never blocks anyone - walked through, never stood on."
            : SizeZ <= 1
                ? "Solid, 4px tall (1 lift): a walker can step up onto it."
                : $"Solid, {SizeZ * LiftPixels}px tall ({SizeZ} lifts): blocks a walker - it needs something {(SizeZ - 1) * LiftPixels}px tall beside it as a step to climb on.";
    }

    private static string Signed(int value) => value > 0 ? $"+{value}" : value.ToString();

    private void AddHandle(char which, Point at, Brush fill, Cursor cursor, string tip)
    {
        var handle = new Rectangle { Width = HandleSize, Height = HandleSize, Fill = fill, Stroke = Brushes.Black, StrokeThickness = 1, Cursor = cursor, ToolTip = tip };
        Canvas.SetLeft(handle, at.X - HandleSize / 2);
        Canvas.SetTop(handle, at.Y - HandleSize / 2);
        handle.MouseLeftButtonDown += (_, e) =>
        {
            if (which == 'M') { BeginMove(e); return; }
            _dragging = which;
            // Capture on the canvas itself, which survives every redraw - the handle element is rebuilt on each move.
            _canvas.CaptureMouse();
            e.Handled = true;
        };
        _canvas.Children.Add(handle);
    }

    private void BeginMove(MouseButtonEventArgs e)
    {
        _moveGrab = ToWorld(e.GetPosition(_canvas)) - new Point(AnchorX, AnchorY);
        _dragging = 'M';
        _canvas.CaptureMouse();
        e.Handled = true;
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragging == '\0') return;
        var world = ToWorld(e.GetPosition(_canvas));
        switch (_dragging)
        {
            case 'X': SetBox((int)Math.Round(2 * (world.X - AnchorX) / UnitPixels), SizeY, SizeZ, OffsetX, OffsetY, refit: false); break;
            case 'Y': SetBox(SizeX, (int)Math.Round((AnchorY - world.Y) / UnitPixels), SizeZ, OffsetX, OffsetY, refit: false); break;
            // The top face's back edge sits at anchor - (depth + lift); whatever is above the footprint's back edge is height.
            case 'Z': SetBox(SizeX, SizeY, (int)Math.Round((AnchorY - FootDepth - world.Y) / LiftPixels), OffsetX, OffsetY, refit: false); break;
            case 'M':
                var anchor = world - _moveGrab;
                SetBox(SizeX, SizeY, SizeZ, (int)Math.Round(anchor.X / UnitPixels), (int)Math.Round(anchor.Y / UnitPixels), refit: false);
                break;
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
