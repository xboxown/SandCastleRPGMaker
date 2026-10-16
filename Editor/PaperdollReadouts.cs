using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The pictures a paperdoll draws for an amount (Character Editor > Paperdoll):
/// a number in a sprite font, and a gauge - a bar, a pie, or the frames of a
/// sprite from empty to full. One rule for the editor's preview and the game,
/// in the paperdoll's 320x400 layout; counterScale is what the game scales
/// item pictures by inside it (2), so a font or sprite frame shows at the same
/// size as the items.
/// </summary>
internal static class PaperdollReadouts
{
    public static Brush? Brush(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex.Trim())); }
        catch { return null; }
    }

    private static readonly Dictionary<string, BitmapSource> Cache = new();

    private static BitmapSource? Load(string path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            bmp.Freeze();
            Cache[path] = bmp;
            return bmp;
        }
        catch { return null; }
    }

    /// <summary>A number (or any text), placed by its X/Y and alignment.</summary>
    public static FrameworkElement Readout(PaperdollReadout r, string text, SpriteDatabase sprites, double counterScale)
    {
        var font = (string.IsNullOrWhiteSpace(r.FontName) ? null : SpriteFont.For(sprites, r.FontName)) ?? SpriteFont.For(sprites, SpriteDatabase.InGameFontName);
        FrameworkElement element;
        double width;
        if (font?.RenderText(text, Math.Max(1, r.Scale), Brush(r.Tint)) is { } bitmap)
        {
            width = bitmap.PixelWidth * counterScale;
            var image = new Image { Source = bitmap, Width = width, Height = bitmap.PixelHeight * counterScale, Stretch = Stretch.Fill, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            element = image;
        }
        else
        {
            var block = new TextBlock { Text = text, FontSize = 8 * Math.Max(1, r.Scale) * counterScale, Foreground = Brush(r.Tint) ?? Brushes.Black, FontWeight = FontWeights.Bold, IsHitTestVisible = false };
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            width = block.DesiredSize.Width;
            element = block;
        }
        double left = r.Align switch { ReadoutAlign.Right => r.X - width, ReadoutAlign.Center => r.X - width / 2, _ => r.X };
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, r.Y);
        return element;
    }

    /// <summary>A gauge at a fraction (0 empty .. 1 full).</summary>
    public static FrameworkElement? Gauge(PaperdollGauge g, double fraction, SpriteDatabase sprites, double counterScale)
    {
        fraction = Math.Clamp(double.IsNaN(fraction) ? 0 : fraction, 0, 1);
        FrameworkElement? element = null;
        switch (g.Kind)
        {
            case GaugeKind.Bar:
            {
                var grid = new Grid { Width = Math.Max(2, g.Width), Height = Math.Max(2, g.Height), IsHitTestVisible = false };
                grid.Children.Add(new Rectangle { Fill = Brush(g.BackColor) ?? Brushes.Transparent });
                grid.Children.Add(new Rectangle { Fill = Brush(g.FillColor) ?? Brushes.Goldenrod, Width = Math.Max(2, g.Width) * fraction, HorizontalAlignment = HorizontalAlignment.Left });
                element = grid;
                break;
            }
            case GaugeKind.Pie:
            {
                double size = Math.Max(4, Math.Min(g.Width, g.Height)), r = size / 2;
                var canvas = new Canvas { Width = size, Height = size, IsHitTestVisible = false };
                canvas.Children.Add(new Ellipse { Width = size, Height = size, Fill = Brush(g.BackColor) ?? Brushes.Transparent });
                var fill = Brush(g.FillColor) ?? Brushes.Goldenrod;
                if (fraction >= 0.999) canvas.Children.Add(new Ellipse { Width = size, Height = size, Fill = fill });
                else if (fraction > 0.001)
                {
                    double angle = fraction * 2 * Math.PI; // clockwise from the top
                    var end = new Point(r + r * Math.Sin(angle), r - r * Math.Cos(angle));
                    var figure = new PathFigure { StartPoint = new Point(r, r), IsClosed = true };
                    figure.Segments.Add(new LineSegment(new Point(r, 0), true));
                    figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
                    canvas.Children.Add(new System.Windows.Shapes.Path { Fill = fill, Data = new PathGeometry(new[] { figure }) });
                }
                element = canvas;
                break;
            }
            case GaugeKind.SpriteFrames:
            {
                var sprite = ObjectLooks.Find(sprites, g.Sprite.SpriteGroup, g.Sprite.SpriteName);
                if (sprite is null || sprite.Frames.Count == 0) return null;
                int frame = Math.Min(sprite.Frames.Count - 1, (int)Math.Floor(fraction * sprite.Frames.Count)); // first frame empty .. last frame full
                if (Load(sprite.Frames[frame].FilePath) is not { } bitmap) return null;
                var image = new Image { Source = bitmap, Width = bitmap.PixelWidth * counterScale, Height = bitmap.PixelHeight * counterScale, Stretch = Stretch.Fill, IsHitTestVisible = false };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                element = image;
                break;
            }
        }
        if (element is null) return null;
        Canvas.SetLeft(element, g.X);
        Canvas.SetTop(element, g.Y);
        return element;
    }
}