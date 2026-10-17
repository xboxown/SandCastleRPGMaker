using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The picture of the open map other windows pick cells on (schedule spots,
/// Teleport, Walk To Location, things on the map): the whole map at its true
/// size whatever the Map Editor's zoom, on any floor, and - "see inside" - with
/// what is over a standing person's head left out (roofs, a cave's mountain
/// top, the upper floors), as the game does when you walk in (gamerend.cc
/// skip_above: everything 4 lifts or more above the floor goes).
/// </summary>
public partial class MainWindow
{
    private (BitmapSource Picture, int TileWidth, int TileHeight, int Columns, int Rows)? RenderMapPicture(int floor, bool seeInside)
    {
        if (_map is null || _tileset is null) return null;
        int width = _map.Width * _tileset.TileWidth, height = _map.Height * _tileset.TileHeight;
        if (width <= 0 || height <= 0) return null;
        floor = Math.Clamp(floor, 1, MapFloors.MaxFloors);

        int shownFloor = _currentFloor;
        double h = MapScrollViewer.HorizontalOffset, v = MapScrollViewer.VerticalOffset;
        var zoom = MapCanvas.LayoutTransform;
        var hidden = new List<UIElement>();
        try
        {
            if (floor != _currentFloor) { _currentFloor = floor; BuildMapCanvas(); }
            if (seeInside)
            {
                int overhead = MapFloors.BaseLift(floor) + 4; // Exult's is_roof: from 4 lifts above the feet
                void Hide(UIElement? visual)
                {
                    if (visual is null || visual.Visibility != Visibility.Visible) return;
                    visual.Visibility = Visibility.Hidden;
                    hidden.Add(visual);
                }
                foreach (var o in _map.Objects.Where(o => o.ContainerInstanceId < 0 && MapFloors.FloorOfLift(o.Z) == floor))
                    if (o.Z >= overhead || _objectDatabase.Objects.FirstOrDefault(d => d.Id == o.ObjectId)?.Type == ObjectType.Roof)
                        Hide(_mapObjectVisuals.GetValueOrDefault(o.InstanceId));
                foreach (var i in _map.Items.Where(i => i.Z >= overhead && MapFloors.FloorOfLift(i.Z) == floor))
                    Hide(_mapItemVisuals.GetValueOrDefault(i));
            }
            // At its own size: the zoom (100% is already larger than the map's pixels) would leave only the top-left corner in the picture.
            MapCanvas.LayoutTransform = Transform.Identity;
            MapCanvas.UpdateLayout();

            // Through a VisualBrush, so the canvas's own position inside the window doesn't shift the picture.
            var drawing = new DrawingVisual();
            using (var dc = drawing.RenderOpen())
            {
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
            return (picture, _tileset.TileWidth, _tileset.TileHeight, _map.Width, _map.Height);
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
