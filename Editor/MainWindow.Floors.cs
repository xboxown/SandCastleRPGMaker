using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Map Editor floors (MapFloors, 1-9). Pick a floor in the toolbar: tiles
/// are painted on that floor's own layer, and objects, books, containers,
/// characters, events and the Start Position are placed on it. The floors
/// below show through, darkened, wherever this floor has no tiles; the
/// floors above are hidden. Only the picked floor can be clicked.
///
/// The picked floor is drawn lined up with the grid. The floors below are
/// drawn where the game puts them relative to it - the game draws each lift
/// 4px up and left, so a floor 5 lifts lower sits 20px down and right - and
/// darker the further down they are. So floor 2's tiles and walls line up
/// with floor 1's walls here exactly as they will in the game.
/// </summary>
public partial class MainWindow
{
    private int _currentFloor = 1;
    private bool _settingFloorCombo;

    /// <summary>Draw-order step per floor below: a lower floor's things are all under this floor's.</summary>
    private const int LowerFloorZIndexStep = 1_000_000;

    /// <summary>How far down and right a floor this many floors below the picked one is drawn (the game's 4px per lift).</summary>
    private static double LowerFloorShift(int floorsDown) => floorsDown * MapFloors.LiftsPerFloor * 4.0;

    private void InitFloors()
    {
        _settingFloorCombo = true;
        FloorCombo.ItemsSource = Enumerable.Range(1, MapFloors.MaxFloors).Select(f => $"Floor {f}").ToList();
        FloorCombo.SelectedIndex = _currentFloor - 1;
        _settingFloorCombo = false;
    }

    private void FloorCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingFloorCombo || FloorCombo.SelectedIndex < 0) return;
        _currentFloor = FloorCombo.SelectedIndex + 1;
        if (_map is null || _tileset is null) return;
        double h = MapScrollViewer.HorizontalOffset, v = MapScrollViewer.VerticalOffset;
        SelectObjectInstance(null);
        SelectPlacedCharacter(null);
        BuildMapCanvas();
        MapScrollViewer.UpdateLayout();
        MapScrollViewer.ScrollToHorizontalOffset(h);
        MapScrollViewer.ScrollToVerticalOffset(v);
        StatusText.Text = _currentFloor == 1
            ? "Floor 1 (the ground)."
            : $"Floor {_currentFloor} (lift {MapFloors.BaseLift(_currentFloor)}): tiles painted here are floor slabs you walk on - and a ceiling for whoever is below. The floors below show darkened.";
    }

    /// <summary>The picked floor's tile layer (made when first painted on).</summary>
    private MapLayer CurrentFloorLayer => _map!.LayerForFloor(_currentFloor, create: true)!;

    private static int FloorOf(ObjectInstance instance) => MapFloors.FloorOfLift(instance.Z);
    private static int FloorOf(PlacedItemInstance instance) => MapFloors.FloorOfLift(instance.Z);

    /// <summary>
    /// Fits a freshly built visual of something on this floor into the view:
    /// true = it belongs to the picked floor (draw it normally); false with
    /// the visual turned into a dimmed, unclickable piece of a floor below; or
    /// null - a floor above: don't draw it at all.
    /// </summary>
    private bool? FitToFloor(UIElement visual, int floor)
    {
        if (floor == _currentFloor) return true;
        if (floor > _currentFloor) return null;
        visual.IsHitTestVisible = false;
        if (visual is FrameworkElement element) element.ToolTip = null;
        Panel.SetZIndex(visual, Panel.GetZIndex(visual) - LowerFloorZIndexStep * (_currentFloor - floor));
        double shift = LowerFloorShift(_currentFloor - floor); // lower down, as the game draws it
        Canvas.SetLeft(visual, (double.IsNaN(Canvas.GetLeft(visual)) ? 0 : Canvas.GetLeft(visual)) + shift);
        Canvas.SetTop(visual, (double.IsNaN(Canvas.GetTop(visual)) ? 0 : Canvas.GetTop(visual)) + shift);
        return false;
    }

    /// <summary>The floors below the picked one: their tiles, under a dark veil (their objects, items and characters are drawn by their own Place* methods, dimmed by FitToFloor).</summary>
    private void DrawLowerFloorTiles()
    {
        if (_map is null || _tileset is null || _tilesetBitmap is null || _currentFloor <= 1) return;
        for (int floor = 1; floor < _currentFloor; floor++)
        {
            if (_map.LayerForFloor(floor) is not { } layer) continue;
            for (int y = 0; y < _map.Height; y++)
            {
                for (int x = 0; x < _map.Width; x++)
                {
                    int tileIndex = layer.GetTile(x, y, _map.Width);
                    if (tileIndex < 0) continue;
                    var image = new Image
                    {
                        Width = _tileset.TileWidth,
                        Height = _tileset.TileHeight,
                        IsHitTestVisible = false,
                        Source = new CroppedBitmap(_tilesetBitmap, new Int32Rect(tileIndex % _paletteColumns * _tileset.TileWidth, tileIndex / _paletteColumns * _tileset.TileHeight, _tileset.TileWidth, _tileset.TileHeight))
                    };
                    RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                    double shift = LowerFloorShift(_currentFloor - floor);
                    Canvas.SetLeft(image, x * _tileset.TileWidth + shift);
                    Canvas.SetTop(image, y * _tileset.TileHeight + shift);
                    Panel.SetZIndex(image, -LowerFloorZIndexStep * (_currentFloor - floor));
                    MapCanvas.Children.Add(image);
                }
            }
            // A veil over each floor below: out of focus, and darker the further down.
            double reach = LowerFloorShift(_currentFloor - 1);
            var veil = new Rectangle { Width = _map.Width * _tileset.TileWidth + reach, Height = _map.Height * _tileset.TileHeight + reach, Fill = Brushes.Black, Opacity = 0.45, IsHitTestVisible = false };
            Panel.SetZIndex(veil, -LowerFloorZIndexStep * (_currentFloor - floor) + LowerFloorZIndexStep / 2);
            MapCanvas.Children.Add(veil);
        }
    }
}
