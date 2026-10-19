using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Changing a map tile while the game runs (Change Tile, Grid To Map): the
/// map's data and the tile's picture together - a ground tile, or an upper
/// floor's slab drawn at its floor's height and draw band.
/// </summary>
public partial class GameTestWindow
{
    private readonly Dictionary<(int X, int Y), Image> _groundTileImages = new();
    private readonly Dictionary<(int Floor, int X, int Y), Image> _upperTileImages = new();

    /// <summary>Sets a cell's tile on a floor (-1 = no tile); false when the cell is off the map.</summary>
    private bool SetMapTile(int x, int y, int floor, int tile)
    {
        if (!InsideMap(x, y)) return false;
        floor = Math.Clamp(floor, 1, MapFloors.MaxFloors);
        var layer = _map.LayerForFloor(floor, create: true);
        if (layer is null) return false;
        if (layer.GetTile(x, y, _map.Width) == tile) return true;
        layer.SetTile(x, y, _map.Width, tile);

        if (floor <= 1)
        {
            if (_groundTileImages.TryGetValue((x, y), out var ground))
            {
                if (tile < 0) { GameCanvas.Children.Remove(ground); _groundTileImages.Remove((x, y)); }
                else ground.Source = TileSource(tile);
                return true;
            }
            if (tile < 0) return true;
            var image = NewTileImage(tile, hitTest: false);
            Canvas.SetLeft(image, x * _tileWidth);
            Canvas.SetTop(image, y * _tileHeight);
            GameCanvas.Children.Insert(0, image); // the ground is under everything
            _groundTileImages[(x, y)] = image;
            return true;
        }

        if (_upperTileImages.TryGetValue((floor, x, y), out var slab))
        {
            if (tile < 0)
            {
                GameCanvas.Children.Remove(slab);
                _upperTileImages.Remove((floor, x, y));
                _floorTileVisuals.RemoveAll(t => ReferenceEquals(t.Visual, slab));
            }
            else slab.Source = TileSource(tile);
            return true;
        }
        if (tile < 0) return true;
        int lift = MapFloors.BaseLift(floor);
        var upper = NewTileImage(tile, hitTest: true);
        Canvas.SetLeft(upper, x * _tileWidth - lift * LiftPixelShift);
        Canvas.SetTop(upper, y * _tileHeight - lift * LiftPixelShift);
        Panel.SetZIndex(upper, SlabZIndex(floor, MapFloors.SlabLift(floor)));
        GameCanvas.Children.Add(upper);
        _floorTileVisuals.Add((MapFloors.SlabLift(floor), upper));
        _upperTileImages[(floor, x, y)] = upper;
        return true;
    }

    private Image NewTileImage(int tile, bool hitTest)
    {
        var image = new Image { Width = _tileWidth, Height = _tileHeight, IsHitTestVisible = hitTest, Source = TileSource(tile) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }
}
