using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A map that wraps around (Maps tab: "Wraps around the edges") - the world
/// of Ultima V and VI: walk off the east edge and you come back on the west,
/// off the north and you come back on the south. The player's position and
/// the camera move across the seam together; the land on the other side is
/// drawn in the gap beyond the edge (copies of the world's own picture, so
/// everything over there - characters walking, fires burning - shows live).
/// Everyone crosses it as the player does: characters' paths go over the
/// seam, a party follows its leader across, fights are fought over it (the
/// short way round counts, as Exult's delta_wrap_check), and a missile flies
/// on over it - gone once it has crossed and is off the screen. Things seen
/// across the seam are clicked on once you've stepped over it.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>The gaps beyond the edges: across, up/down, and the corner.</summary>
    private Rectangle? _wrapAcross, _wrapUpDown, _wrapCorner;

    private bool Wraps => _map.WrapsAround;
    private double WorldPixelWidth => _map.Width * _tileWidth;
    private double WorldPixelHeight => _map.Height * _tileHeight;
    /// <summary>The camera goes past an edge only when the map is bigger than the screen that way (a smaller map stays centred).</summary>
    private bool WrapsAcross => Wraps && WorldPixelWidth > ViewportWidth;
    private bool WrapsUpDown => Wraps && WorldPixelHeight > ViewportHeight;

    /// <summary>The world's own place and its copies round it (on a wrapping map): what the screen can show of it.</summary>
    private IEnumerable<(double DX, double DY)> WrapShifts()
    {
        yield return (0, 0);
        if (!Wraps) yield break;
        double w = WorldPixelWidth, h = WorldPixelHeight;
        foreach (var dx in new[] { -w, 0, w })
            foreach (var dy in new[] { -h, 0, h })
                if (dx != 0 || dy != 0) yield return (dx, dy);
    }

    /// <summary>A spot brought back onto a wrapping map (how far it was moved, in pixels).</summary>
    private (double DX, double DY) WrapPixel(ref double x, ref double y)
    {
        if (!Wraps) return (0, 0);
        double w = WorldPixelWidth, h = WorldPixelHeight, dx = 0, dy = 0;
        if (x < 0) dx = w; else if (x >= w) dx = -w;
        if (y < 0) dy = h; else if (y >= h) dy = -h;
        x += dx;
        y += dy;
        return (dx, dy);
    }

    /// <summary>The shortest way from one spot to another (pixels), across the seam if that's nearer.</summary>
    private (double DX, double DY) WrappedDelta(double dx, double dy) =>
        Wraps ? (ShortWay(dx, WorldPixelWidth), ShortWay(dy, WorldPixelHeight)) : (dx, dy);

    /// <summary>Where another spot is as seen from this one: itself, or its copy over the seam if that's nearer (aiming, sight).</summary>
    private (double X, double Y) NearestImage(double fromX, double fromY, double x, double y)
    {
        var (dx, dy) = WrappedDelta(x - fromX, y - fromY);
        return (fromX + dx, fromY + dy);
    }

    /// <summary>Cells apart across (or up/down), the short way round on a map that wraps around.</summary>
    private int CellsAcross(int d) => Wraps ? (int)ShortWay(d, _map.Width) : d;
    private int CellsUpDown(int d) => Wraps ? (int)ShortWay(d, _map.Height) : d;
    /// <summary>8px units apart, the short way round.</summary>
    private int UnitsAcross(int d) => Wraps ? (int)ShortWay(d, _map.Width * UnitsPerTileX) : d;
    private int UnitsUpDown(int d) => Wraps ? (int)ShortWay(d, _map.Height * UnitsPerTileY) : d;

    /// <summary>Cells between two cells, the larger axis (Exult's distance), the short way round.</summary>
    private int CellsApart((int X, int Y) a, (int X, int Y) b) => Math.Max(Math.Abs(CellsAcross(a.X - b.X)), Math.Abs(CellsUpDown(a.Y - b.Y)));

    /// <summary>A cell past the edge of a map that wraps around: the one it is on the other side (any other map: as it is).</summary>
    private (int X, int Y) WrapCell((int X, int Y) cell) =>
        Wraps ? (((cell.X % _map.Width) + _map.Width) % _map.Width, ((cell.Y % _map.Height) + _map.Height) % _map.Height) : cell;

    private static double ShortWay(double d, double size)
    {
        if (size <= 0) return d;
        d %= size;
        if (d > size / 2) d -= size; else if (d < -size / 2) d += size;
        return d;
    }

    /// <summary>A footprint over the seam of a wrapping map, cut into its pieces on either side (null: it isn't over one).</summary>
    private List<UnitRect>? SplitAtSeam(UnitRect foot)
    {
        if (!Wraps) return null;
        int w = _map.Width * UnitsPerTileX, h = _map.Height * UnitsPerTileY;
        if (foot.Left >= 0 && foot.Top >= 0 && foot.Right <= w && foot.Bottom <= h) return null;
        if (foot.Width > w || foot.Depth > h) return null;
        List<(int From, int To)> Spans(int from, int to, int size) =>
            from < 0 ? new() { (from + size, size), (0, to) }
            : to > size ? new() { (from, size), (0, to - size) }
            : new() { (from, to) };
        var parts = new List<UnitRect>();
        foreach (var (x0, x1) in Spans(foot.Left, foot.Right, w))
            foreach (var (y0, y1) in Spans(foot.Top, foot.Bottom, h))
                parts.Add(new UnitRect(x0, y0, x1 - x0, y1 - y0));
        return parts;
    }

    /// <summary>Exult's step over the seam: each piece of the footprint must take it; it stands at the highest.</summary>
    private bool TryStepAcrossSeam(List<UnitRect> parts, int lift, int height, out int newLift, out string blockReason, object? mover, BodySource? world, bool explain)
    {
        newLift = int.MinValue;
        blockReason = "";
        foreach (var part in parts)
        {
            if (!TryStepActor(part, lift, height, out int partLift, out blockReason, mover, world, explain)) { newLift = lift; return false; }
            newLift = Math.Max(newLift, partLift);
        }
        return true;
    }

    /// <summary>The land beyond the edge, drawn from the world's own picture, wherever the camera has gone past an edge.</summary>
    private void ShowAcrossTheSeam()
    {
        if (!Wraps && _wrapAcross is null) return;
        if (_wrapAcross is null)
        {
            if (GameCanvas.Parent is not Grid holder) return;
            Rectangle Gap()
            {
                var gap = new Rectangle
                {
                    HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false, Visibility = Visibility.Collapsed,
                    Fill = new VisualBrush(GameCanvas) { ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.Fill, AutoLayoutContent = false }
                };
                Panel.SetZIndex(gap, 1); // over the world's own edge, under clouds, weather and the night
                holder.Children.Insert(holder.Children.IndexOf(GameCanvas) + 1, gap);
                return gap;
            }
            _wrapAcross = Gap();
            _wrapUpDown = Gap();
            _wrapCorner = Gap();
        }
        // The world's picture is drawn already moved by the camera: a spot on the screen is that spot in it. The gap shows the world one map-width (or height) along.
        double w = WorldPixelWidth, h = WorldPixelHeight;
        (double From, double To, double Shift) across = (0, 0, 0), upDown = (0, 0, 0);
        if (WrapsAcross)
        {
            if (_cameraX < 0) across = (0, -_cameraX, w);
            else if (_cameraX + ViewportWidth > w) across = (w - _cameraX, ViewportWidth, -w);
        }
        if (WrapsUpDown)
        {
            if (_cameraY < 0) upDown = (0, -_cameraY, h);
            else if (_cameraY + ViewportHeight > h) upDown = (h - _cameraY, ViewportHeight, -h);
        }
        Place(_wrapAcross, across.From, 0, across.To - across.From, ViewportHeight, across.Shift, 0);
        Place(_wrapUpDown!, 0, upDown.From, ViewportWidth, upDown.To - upDown.From, 0, upDown.Shift);
        Place(_wrapCorner!, across.From, upDown.From, across.To - across.From, upDown.To - upDown.From, across.Shift, upDown.Shift);

        static void Place(Rectangle gap, double x, double y, double width, double height, double shiftX, double shiftY)
        {
            if (width < 0.5 || height < 0.5)
            {
                if (gap.Visibility != Visibility.Collapsed) gap.Visibility = Visibility.Collapsed;
                return;
            }
            gap.Margin = new Thickness(x, y, 0, 0);
            gap.Width = width;
            gap.Height = height;
            ((VisualBrush)gap.Fill).Viewbox = new Rect(x + shiftX, y + shiftY, width, height);
            if (gap.Visibility != Visibility.Visible) gap.Visibility = Visibility.Visible;
        }
    }
}
