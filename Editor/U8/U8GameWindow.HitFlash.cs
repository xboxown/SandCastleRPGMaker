// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// How a hit shows, ported from Exult (actors.cc Actor::reduce_health,
/// Actor::figure_hit_points, Actor::paint, Clear_hit; combat.cc show_hits):
///  - Whoever is hit gets a momentary red outline around its sprite
///    (paint_outline(HIT_PIXEL)), cleared 200 ms later.
///  - The main character instead flashes the whole screen red when badly
///    hurt: a blow of a third of its maximum health or more, or while it's
///    below a quarter (Palette::flash_red).
///  - The hit numbers ("-4", "miss") only show with System Settings >
///    Combat > Show hit numbers - Exult's show_hits, off by default.
/// </summary>
public partial class U8GameWindow
{
    private const int HitOutlineMs = 200;
    private static readonly Color HitColor = Color.FromRgb(0xE0, 0x10, 0x10);

    private bool ShowHitNumbers => _systemSettings.Combat.ShowHitNumbers;

    /// <summary>Outlines showing right now, each with the character picture it belongs to - moved with it every frame, so it never trails behind someone walking.</summary>
    private readonly List<(Image Outline, Image Visual, BitmapSource Frame)> _hitOutlines = new();

    /// <summary>reduce_health's flash for <paramref name="amount"/> damage to a character that had <paramref name="oldHp"/>.</summary>
    private void FlashHit(CharacterDefinition character, int amount, int oldHp)
    {
        int maxHp = MaxHp(character);
        if (character == _player && maxHp > 0 && (amount >= maxHp / 3 || oldHp < maxHp / 4)) FlashScreenRed();
        else ShowHitOutline(character);
    }

    /// <summary>A red outline drawn round the character's current frame for 200 ms.</summary>
    private void ShowHitOutline(CharacterDefinition character)
    {
        var visual = character == _player ? _playerImage : MapCharacterOf(character)?.Visual as Image;
        if (visual?.Source is not BitmapSource frame || visual.Width <= 0 || frame.PixelWidth <= 0) return;
        var outline = OutlineOf(frame, HitColor);
        var image = new Image { Source = outline, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        var entry = (image, visual, frame);
        _hitOutlines.Add(entry);
        GameCanvas.Children.Add(image);
        FollowHitOutlines();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(HitOutlineMs) };
        timer.Tick += (_, _) => { timer.Stop(); GameCanvas.Children.Remove(image); _hitOutlines.RemoveAll(h => ReferenceEquals(h.Outline, image)); };
        timer.Start();
    }

    /// <summary>
    /// The red hit outline and the condition outlines (poisoned, cursed...) laid over their characters as they are now - called
    /// after the frame's moves and on every screen refresh between them, so walking, running or striking they never trail
    /// behind or keep an old frame's shape.
    /// </summary>
    private void UpdateOutlines()
    {
        FollowHitOutlines();
        using (Measure("UpdateStatusOutlines")) UpdateStatusOutlines(); // poisoned green, protected white, paralyzed purple... (Exult's Actor::paint)
        if (_perfLogPath is not null) MeasureOutlines();
    }

    private double _outlineMeasureMs;

    /// <summary>Measuring only (SANDCASTLE_PERF_LOG): where each outlined body is drawn and where its outline is, on the screen - a test reads them.</summary>
    private void MeasureOutlines()
    {
        foreach (var (outline, visual, frame) in _hitOutlines.ToList()) // the red hit outlines too, as they are
        {
            if (!visual.IsVisible || !outline.IsVisible) continue;
            try
            {
                UpdateLayout();
                var (_, _, sx, sy) = DrawnFrame(visual, frame);
                var body = visual.TransformToAncestor(this).TransformBounds(new Rect(0, 0, frame.PixelWidth * sx, frame.PixelHeight * sy));
                var ring = outline.TransformToAncestor(this).TransformBounds(new Rect(0, 0, outline.ActualWidth, outline.ActualHeight));
                double pad = body.Width / Math.Max(1, frame.PixelWidth);
                bool same = ReferenceEquals(visual.Source, frame);
                SoakLog($"hit outline: off {ring.X + pad - body.X:0.0},{ring.Y + pad - body.Y:0.0} shape of the frame now {same}");
            }
            catch { }
        }
        if ((Environment.TickCount64 - _outlineMeasureMs) < 150) return;
        _outlineMeasureMs = Environment.TickCount64;
        foreach (var (character, status) in _statusOutlines)
        {
            if (status.Outline.Visibility != Visibility.Visible || status.Frame is not BitmapSource frame) continue;
            var visual = character == _player ? _playerImage : MapCharacterOf(character)?.Visual as Image;
            if (visual is null || !visual.IsVisible || !status.Outline.IsVisible) continue;
            try
            {
                UpdateLayout();
                var (dx, dy, sx, sy) = DrawnFrame(visual, frame);
                var body = visual.TransformToAncestor(this).TransformBounds(new Rect(0, 0, frame.PixelWidth * sx, frame.PixelHeight * sy)); // WPF's own layout already puts the fitted frame's corner at the picture's origin
                var ring = status.Outline.TransformToAncestor(this).TransformBounds(new Rect(0, 0, status.Outline.ActualWidth, status.Outline.ActualHeight));
                double pad = sx * (body.Width / Math.Max(1, frame.PixelWidth * sx)); // one frame pixel on the screen
                SoakLog($"outline: {character.Name} body {body.X:0.0},{body.Y:0.0} {body.Width:0.0}x{body.Height:0.0} ring {ring.X:0.0},{ring.Y:0.0} {ring.Width:0.0}x{ring.Height:0.0} off {ring.X + pad - body.X:0.0},{ring.Y + pad - body.Y:0.0} at {Canvas.GetLeft(visual):0},{Canvas.GetTop(visual):0}");
            }
            catch { }
        }
    }

    /// <summary>Every frame: each outline sits exactly round its character, wherever it has walked to.</summary>
    private void FollowHitOutlines()
    {
        for (int i = 0; i < _hitOutlines.Count; i++)
        {
            var (outline, visual, frame) = _hitOutlines[i];
            // Its frame changed (a swing, a step) during the red moment: the outline takes the new shape at once.
            if (visual.Source is BitmapSource now && !ReferenceEquals(now, frame))
            {
                if (!_outlineCache.TryGetValue((now, HitColor), out var shape))
                {
                    if (_outlineCache.Count > 512) _outlineCache.Clear();
                    _outlineCache[(now, HitColor)] = shape = OutlineOf(now, HitColor);
                }
                outline.Source = shape;
                _hitOutlines[i] = (outline, visual, frame = now);
            }
            PlaceOutlineOver(outline, visual, frame);
        }
    }

    /// <summary>
    /// An outline (the frame and 1 pixel all round) laid exactly over the picture as it's drawn in the diamond view: placed as the
    /// picture is (its placement, its anchor moved by the gap and the 1-pixel rim), the frame fitted into its box (Uniform) and
    /// centred - so it sits on the body, not off it (before, it took the box's corner and the default anchor).
    /// </summary>
    private static void PlaceOutlineOver(Image outline, Image visual, BitmapSource frame)
    {
        var (dx, dy, sx, sy) = DrawnFrame(visual, frame);
        double w = double.IsNaN(visual.Width) ? visual.ActualWidth : visual.Width, h = double.IsNaN(visual.Height) ? visual.ActualHeight : visual.Height;
        double ow = (frame.PixelWidth + 2) * sx, oh = (frame.PixelHeight + 2) * sy;
        outline.Width = ow;
        outline.Height = oh;
        // The diamond canvas takes a picture's stand point from its Left / Top and its own size (bottom-middle): the outline's put
        // where its stand point is the body's, then its anchor lays it on the same spot of the drawn frame.
        Canvas.SetLeft(outline, Canvas.GetLeft(visual) + w / 2 - ow / 2);
        Canvas.SetTop(outline, Canvas.GetTop(visual) + h - oh);
        U8ProjectionCanvas.SetPlacement(outline, U8ProjectionCanvas.GetPlacement(visual));
        var hot = U8ProjectionCanvas.GetAnchor(visual) ?? new Point(0.5, 1);
        // The point of the outline that lands where the picture's anchor lands: the same spot of the drawn frame.
        U8ProjectionCanvas.SetAnchor(outline, new Point((hot.X * w - dx + sx) / ow, (hot.Y * h - dy + sy) / oh));
        Panel.SetZIndex(outline, Panel.GetZIndex(visual) + 1);
    }

    /// <summary>Where a frame is drawn inside its picture's box: the gap from the box's corner and the scale on each side.</summary>
    private static (double DX, double DY, double SX, double SY) DrawnFrame(Image visual, BitmapSource frame)
    {
        double w = double.IsNaN(visual.Width) ? visual.ActualWidth : visual.Width, h = double.IsNaN(visual.Height) ? visual.ActualHeight : visual.Height;
        double pw = Math.Max(1, frame.PixelWidth), ph = Math.Max(1, frame.PixelHeight);
        double sx = w / pw, sy = h / ph;
        if (visual.Stretch == Stretch.Uniform) sx = sy = Math.Min(sx, sy);
        else if (visual.Stretch == Stretch.UniformToFill) sx = sy = Math.Max(sx, sy);
        else if (visual.Stretch == Stretch.None) sx = sy = 1;
        return ((w - pw * sx) / 2, (h - ph * sy) / 2, sx, sy);
    }

    /// <summary>The 1-pixel outline of a frame's shape: every see-through pixel touching a solid one, in the hit colour, one pixel bigger all round.</summary>
    private static BitmapSource OutlineOf(BitmapSource frame, Color color)
    {
        var source = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
        int w = source.PixelWidth, h = source.PixelHeight;
        var pixels = new byte[w * h * 4];
        source.CopyPixels(pixels, w * 4, 0);
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && pixels[(y * w + x) * 4 + 3] > 127;

        int ow = w + 2, oh = h + 2;
        var outline = new byte[ow * oh * 4];
        for (int y = -1; y <= h; y++)
            for (int x = -1; x <= w; x++)
            {
                if (Solid(x, y) || !(Solid(x - 1, y) || Solid(x + 1, y) || Solid(x, y - 1) || Solid(x, y + 1))) continue;
                int i = ((y + 1) * ow + (x + 1)) * 4;
                outline[i] = color.B; outline[i + 1] = color.G; outline[i + 2] = color.R; outline[i + 3] = 255;
            }
        var bitmap = BitmapSource.Create(ow, oh, 96, 96, PixelFormats.Bgra32, null, outline, ow * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Palette::flash_red: the whole view goes red for a moment.</summary>
    private void FlashScreenRed()
    {
        if (GameCanvas.Parent is not Panel viewport) return;
        var flash = new Rectangle { Fill = new SolidColorBrush(Color.FromArgb(150, 0xE0, 0, 0)), IsHitTestVisible = false };
        Panel.SetZIndex(flash, 35); // over the world and its lighting, under dialog boxes
        viewport.Children.Add(flash);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += (_, _) => { timer.Stop(); viewport.Children.Remove(flash); };
        timer.Start();
    }
}
