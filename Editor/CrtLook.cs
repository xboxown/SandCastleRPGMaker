using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// System Settings > Game Window > CRT effect (off unless ticked): the game drawn as on an old TV - 400 scanlines over its
/// 320 x 200 (each row of game pixels a bright line and a line 45% darker), the picture softened a little (a CRT's beam).
/// The Sandcastle player does the same (Player/src: the PS3's on its SPEs, at 640 x 400). The game windows (Ultima 7, 6 and 8)
/// call Apply whenever their view is sized; the pointer stays sharp over it.
/// </summary>
internal static class CrtLook
{
    /// <summary>How much darker every other line is (0..1), and the softening, in game pixels.</summary>
    public const double ScanlineDarkness = 0.45, SoftenPixels = 0.4;

    /// <summary>root: the window's outermost Grid; area: the game's area (world, windows, paperdolls); view: the scaled
    /// 320 x 200 view (its rows the lines follow); scale: its size on the screen per game pixel.</summary>
    public static void Apply(Grid root, FrameworkElement area, FrameworkElement view, double scale, bool on)
    {
        var lines = FindLines(root);
        if (!on || scale <= 0 || area.ActualWidth <= 0)
        {
            if (lines is not null) root.Children.Remove(lines);
            if (area.Effect is BlurEffect) area.Effect = null;
            return;
        }
        area.Effect = new BlurEffect { Radius = SoftenPixels * scale, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Quality };
        if (lines is null)
        {
            lines = new Rectangle { Tag = LinesTag, IsHitTestVisible = false, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            Panel.SetZIndex(lines, 999999); // over the game, under the pointer
            root.Children.Add(lines);
        }
        // over the game's area exactly
        var at = area.TranslatePoint(new Point(0, 0), root);
        lines.Margin = new Thickness(at.X, at.Y, 0, 0);
        lines.Width = area.ActualWidth;
        lines.Height = area.ActualHeight;
        // the lines: a period of half a game pixel (400 to the view's 200 rows), its lower half darkened, lined up with the view's top
        double period = scale / 2.0;
        double top = view.TranslatePoint(new Point(0, 0), area).Y;
        double offset = top - Math.Floor(top / period) * period;
        var dark = new SolidColorBrush(Color.FromArgb((byte)Math.Round(255 * ScanlineDarkness), 0, 0, 0));
        dark.Freeze();
        var tile = new DrawingBrush(new GeometryDrawing(dark, null, new RectangleGeometry(new Rect(0, period / 2, 1, period / 2))))
        {
            TileMode = TileMode.Tile, Stretch = Stretch.None,
            ViewportUnits = BrushMappingMode.Absolute, Viewport = new Rect(0, offset, 1, period),
            ViewboxUnits = BrushMappingMode.Absolute, Viewbox = new Rect(0, 0, 1, period)
        };
        tile.Freeze();
        lines.Fill = tile;
    }

    private const string LinesTag = "CrtLines";
    private static Rectangle? FindLines(Grid root)
    {
        foreach (var child in root.Children) if (child is Rectangle r && Equals(r.Tag, LinesTag)) return r;
        return null;
    }
}
