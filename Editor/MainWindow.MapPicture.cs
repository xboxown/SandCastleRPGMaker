using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>A thing on the map picture the picker can select: its key (as MapThingsProvider gives it), name, cell, and where its picture is (map pixels).</summary>
public sealed class MapPickerThing
{
    public required string Key;
    public required string Name;
    public required (int X, int Y) Cell;
    public required Rect Bounds;
    public int ZIndex;
    public int Order;
    /// <summary>Its picture, for clicking only on its drawn pixels (null: anywhere in its bounds).</summary>
    public BitmapSource? Picture;
    private byte[]? _alpha;
    private int _stride;

    /// <summary>Whether this point (map pixels) is on it - on a drawn pixel of its picture, not a see-through one.</summary>
    public bool Hits(Point p)
    {
        if (!Bounds.Contains(p)) return false;
        if (Picture is null || Picture.PixelWidth == 0 || Picture.PixelHeight == 0) return true;
        if (_alpha is null)
        {
            var bgra = new FormatConvertedBitmap(Picture, PixelFormats.Bgra32, null, 0);
            _stride = bgra.PixelWidth * 4;
            _alpha = new byte[_stride * bgra.PixelHeight];
            bgra.CopyPixels(_alpha, _stride, 0);
        }
        int x = Math.Clamp((int)((p.X - Bounds.X) / Bounds.Width * Picture.PixelWidth), 0, Picture.PixelWidth - 1);
        int y = Math.Clamp((int)((p.Y - Bounds.Y) / Bounds.Height * Picture.PixelHeight), 0, Picture.PixelHeight - 1);
        return _alpha[y * _stride + x * 4 + 3] > 16;
    }
}

/// <summary>The map picture for the pickers, with the things drawn on it and the Map Editor's 100% scale.</summary>
public sealed class MapPickerPicture
{
    public required BitmapSource Picture;
    public required int TileWidth, TileHeight, Columns, Rows;
    /// <summary>The Map Editor's 100% (the game's full-screen size), as a scale of the map's own pixels.</summary>
    public required double Zoom100;
    public required int[] ZoomLevels;
    public List<MapPickerThing> Things = new();
}

/// <summary>
/// The picture of the open map other windows pick cells and things on
/// (schedule spots, Teleport, Walk To Location, Pickup Item...): the whole map
/// at its own size whatever the Map Editor's zoom (the picker zooms it itself),
/// on any floor, with every object, book and container on it - but no "E"
/// event or "S" start markers, grid cursor or selection outline. "See inside"
/// leaves out the roofs (Roof objects) and the floors above, as the game does
/// when you walk in.
/// </summary>
public partial class MainWindow
{
    private MapPickerPicture? RenderMapPicture(int floor, bool seeInside)
    {
        if (_map is null || _tileset is null) return null;
        int width = _map.Width * _tileset.TileWidth, height = _map.Height * _tileset.TileHeight;
        if (width <= 0 || height <= 0) return null;
        floor = Math.Clamp(floor, 1, MapFloors.MaxFloors);

        int shownFloor = _currentFloor;
        double h = MapScrollViewer.HorizontalOffset, v = MapScrollViewer.VerticalOffset;
        var zoom = MapCanvas.LayoutTransform;
        var hidden = new List<UIElement>();
        void Hide(UIElement? visual)
        {
            if (visual is null || visual.Visibility != Visibility.Visible) return;
            visual.Visibility = Visibility.Hidden;
            hidden.Add(visual);
        }
        try
        {
            if (floor != _currentFloor) { _currentFloor = floor; BuildMapCanvas(); }
            // Editing marks aren't part of the map.
            foreach (var marker in _mapEventVisuals.Values) Hide(marker);
            Hide(_startPositionVisual);
            Hide(_hoverCursorVisual);
            Hide(_objectSelectionVisual);
            Hide(_freeSelectionVisual);
            if (seeInside)
                foreach (var o in _map.Objects.Where(o => o.ContainerInstanceId < 0 && MapFloors.FloorOfLift(o.Z) == floor))
                    if (_objectDatabase.Objects.FirstOrDefault(d => d.Id == o.ObjectId)?.Type == ObjectType.Roof)
                        Hide(_mapObjectVisuals.GetValueOrDefault(o.InstanceId));

            // At its own size: the picker zooms it (the editor's zoom would leave only its top-left corner in the picture).
            MapCanvas.LayoutTransform = Transform.Identity;
            MapCanvas.UpdateLayout();

            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
                // Through a VisualBrush, so the canvas's own position inside the window doesn't shift the picture.
                var brush = new VisualBrush(MapCanvas)
                {
                    Viewbox = new Rect(0, 0, width, height), ViewboxUnits = BrushMappingMode.Absolute, Stretch = Stretch.None,
                    AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top
                };
                dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
            }
            var picture = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            picture.Render(drawing);
            picture.Freeze();

            var result = new MapPickerPicture
            {
                Picture = picture, TileWidth = _tileset.TileWidth, TileHeight = _tileset.TileHeight, Columns = _map.Width, Rows = _map.Height,
                Zoom100 = GameFullScreenScale, ZoomLevels = ZoomLevels
            };
            // The things on this floor that are showing, where their pictures are - to be picked by clicking on them.
            int order = 0;
            MapPickerThing? Thing(UIElement? visual, string key, string name, (int, int) cell)
            {
                if (visual is not FrameworkElement element || element.Visibility != Visibility.Visible) return null;
                double left = Canvas.GetLeft(element), top = Canvas.GetTop(element);
                if (double.IsNaN(left) || double.IsNaN(top)) return null;
                double w = double.IsNaN(element.Width) ? element.ActualWidth : element.Width, ht = double.IsNaN(element.Height) ? element.ActualHeight : element.Height;
                if (w <= 0 || ht <= 0) return null;
                return new MapPickerThing
                {
                    Key = key, Name = name, Cell = cell, Bounds = new Rect(left, top, w, ht), ZIndex = Panel.GetZIndex(element), Order = order++,
                    Picture = (element as Image)?.Source as BitmapSource
                };
            }
            foreach (var o in _map.Objects.Where(o => o.ContainerInstanceId < 0 && MapFloors.FloorOfLift(o.Z) == floor))
                if (Thing(_mapObjectVisuals.GetValueOrDefault(o.InstanceId), $"Object:{o.InstanceId}:{o.ObjectId}",
                        _objectDatabase.Objects.FirstOrDefault(d => d.Id == o.ObjectId)?.Name ?? "object", (o.X, o.Y)) is { } thing) result.Things.Add(thing);
            foreach (var i in _map.Items.Where(i => MapFloors.FloorOfLift(i.Z) == floor))
                if (Thing(_mapItemVisuals.GetValueOrDefault(i), $"Item:{i.Category}:{i.ItemId}", i.Category == ReferencedItemCategory.Book
                        ? _bookDatabase.Books.FirstOrDefault(b => b.Id == i.ItemId)?.Name ?? "book"
                        : _containerDatabase.Containers.FirstOrDefault(c => c.Id == i.ItemId)?.Name ?? "container", (i.X, i.Y)) is { } thing) result.Things.Add(thing);
            return result;
        }
        finally
        {
            foreach (var visual in hidden) visual.Visibility = Visibility.Visible;
            MapCanvas.LayoutTransform = zoom;
            if (_currentFloor != shownFloor) { _currentFloor = shownFloor; BuildMapCanvas(); }
            MapScrollViewer.UpdateLayout();
            MapScrollViewer.ScrollToHorizontalOffset(h);
            MapScrollViewer.ScrollToVerticalOffset(v);
        }
    }
}
