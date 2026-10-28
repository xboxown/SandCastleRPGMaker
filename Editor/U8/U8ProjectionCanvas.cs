using System.Windows;
using System.Windows.Controls;

namespace SandcastleRpgMaker.Editor;

/// <summary>How a picture on the Ultima 8 map is placed (U8ProjectionCanvas).</summary>
public enum U8Placement
{
    /// <summary>A thing on the map: its picture's bottom on the nearest corner of its cell (Pentagram places an item by its near corner).</summary>
    Thing,
    /// <summary>A character (and what follows one: words over its head, blood, a flying stone): its picture's bottom on its own point - its feet in the middle of the diamond it stands in.</summary>
    Character,
    /// <summary>A tile picture placed at its cell's top-left: drawn as that cell's diamond.</summary>
    Cell,
    /// <summary>Placed by its middle (a blood puddle, a thing in flight): its middle on its own point.</summary>
    Center,
    /// <summary>Already in screen space (the ground's chunks): not projected.</summary>
    Screen
}

/// <summary>
/// The Ultima 8 view (Pentagram's ItemSorter: screen x = (x - y) / 4, screen y = (x + y) / 8 - z). The engine keeps its
/// own map coordinates - the copied Ultima 7 engine's world pixels, a square cell CellSize wide, lifts shifting a picture
/// up-left 4px each. This canvas lays each picture out in Ultima 8's diamond view instead: map point (x, y) goes to
/// (x - y + OriginX, (x + y) / 2), so a cell is a diamond twice as wide as it is tall, and a lift's up-left shift
/// becomes a straight rise - Ultima 8's z. Hit testing follows the pictures where they are drawn; ToMap turns a point on
/// the canvas back into the map.
/// </summary>
public sealed class U8ProjectionCanvas : Canvas
{
    public static readonly DependencyProperty PlacementProperty =
        DependencyProperty.RegisterAttached("Placement", typeof(U8Placement), typeof(U8ProjectionCanvas),
            new FrameworkPropertyMetadata(U8Placement.Thing, FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static U8Placement GetPlacement(UIElement element) => (U8Placement)element.GetValue(PlacementProperty);
    public static void SetPlacement(UIElement element, U8Placement value) { if (GetPlacement(element) != value) element.SetValue(PlacementProperty, value); }

    /// <summary>A map cell's size in map pixels (square).</summary>
    public double CellSize { get; set; } = 32;
    /// <summary>Added to every screen x, so the map's west corner is at 0: the map's height in map pixels.</summary>
    public double OriginX { get; set; }

    public Point ToScreen(double mapX, double mapY) => new(mapX - mapY + OriginX, (mapX + mapY) / 2);

    /// <summary>A point on this canvas back on the map (at lift 0).</summary>
    public Point ToMap(Point screen)
    {
        double a = screen.X - OriginX, b = 2 * screen.Y;
        return new Point((a + b) / 2, (b - a) / 2);
    }

    /// <summary>The screen size the whole map covers.</summary>
    public Size ScreenSize(double mapWidth, double mapHeight) => new(mapWidth + mapHeight, (mapWidth + mapHeight) / 2);

    protected override Size ArrangeOverride(Size arrangeSize)
    {
        foreach (UIElement child in InternalChildren)
            if (child is not null) child.Arrange(Place(child, child.DesiredSize));
        return arrangeSize;
    }

    /// <summary>Where a picture is drawn on this canvas (its own Width / Height when it has them), by its placement.</summary>
    public Rect ScreenRectOf(UIElement child)
    {
        var size = child is FrameworkElement fe && !double.IsNaN(fe.Width) && !double.IsNaN(fe.Height) ? new Size(fe.Width, fe.Height) : child.DesiredSize;
        return Place(child, size);
    }

    private Rect Place(UIElement child, Size size)
    {
        double left = GetLeft(child), top = GetTop(child);
        if (double.IsNaN(left)) left = 0;
        if (double.IsNaN(top)) top = 0;
        var placement = GetPlacement(child);
        if (placement == U8Placement.Screen) return new Rect(new Point(left, top), size);
        if (placement == U8Placement.Center)
        {
            var c = ToScreen(left + size.Width / 2, top + size.Height / 2);
            return new Rect(c.X - size.Width / 2, c.Y - size.Height / 2, size.Width, size.Height);
        }
        // The picture's anchor on the map, as the Ultima 7 engine placed it (its bottom-middle, its lift's shift in it)...
        double ax = left + size.Width / 2, ay = top + size.Height;
        switch (placement)
        {
            case U8Placement.Thing: ax += CellSize / 2; break;          // ...the nearest corner of its cell
            case U8Placement.Character: ay -= CellSize / 2; break;      // ...its feet's own point
            case U8Placement.Cell: ax = left + CellSize; ay = top + CellSize; break; // ...its cell's nearest corner
        }
        var s = ToScreen(ax, ay);
        return new Rect(s.X - size.Width / 2, s.Y - size.Height, size.Width, size.Height);
    }
}
