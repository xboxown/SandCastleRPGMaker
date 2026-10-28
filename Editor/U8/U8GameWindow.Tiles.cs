// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The map's tiles in play:
///  - The ground is drawn in chunks of 16 x 16 tiles - one picture each
///    (made of the tiles' shared pictures), not one per tile - and only the
///    chunks near the view are drawn: a big map costs no more than a small one.
///  - Changing a tile while the game runs (Change Tile, Grid To Map): the
///    map's data and its picture together - a ground tile (its chunk drawn
///    again), or an upper floor's slab drawn at its floor's height.
/// </summary>
public partial class U8GameWindow
{
    private const int ChunkTiles = 16;
    private readonly Dictionary<(int CX, int CY), Image> _groundChunks = new();
    private readonly Dictionary<int, ImageSource?> _tilePictures = new();
    private (int X0, int Y0, int X1, int Y1) _shownChunks = (-1, -1, -2, -2);
    private readonly Dictionary<(int Floor, int X, int Y), Image> _upperTileImages = new();

    /// <summary>A tile's picture, made once and shared by every chunk that shows it.</summary>
    private ImageSource? TilePicture(int tile)
    {
        if (_tilePictures.TryGetValue(tile, out var picture)) return picture;
        picture = TileSource(tile);
        if (picture is { CanFreeze: true }) picture.Freeze();
        _tilePictures[tile] = picture;
        return picture;
    }

    /// <summary>A chunk's diamond on the screen (the Ultima 8 view): from its north corner, as wide as two chunks of cells and one tall.</summary>
    private Rect ChunkOnScreen(int cx, int cy)
    {
        double side = ChunkTiles * _tileWidth;
        var north = GameCanvas.ToScreen(cx * side, cy * side);
        return new Rect(north.X - side, north.Y, 2 * side, side);
    }

    private void BuildGroundChunks()
    {
        int columns = (_map.Width + ChunkTiles - 1) / ChunkTiles, rows = (_map.Height + ChunkTiles - 1) / ChunkTiles;
        for (int cy = 0; cy < rows; cy++)
            for (int cx = 0; cx < columns; cx++)
            {
                var at = ChunkOnScreen(cx, cy);
                var image = new Image { Width = at.Width, Height = at.Height, IsHitTestVisible = false, Stretch = Stretch.Fill }; // all shown at first; the camera hides the far ones
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                image.Source = GroundChunkPicture(cx, cy);
                U8ProjectionCanvas.SetPlacement(image, U8Placement.Screen); // drawn as diamonds already
                Canvas.SetLeft(image, at.X);
                Canvas.SetTop(image, at.Y);
                GameCanvas.Children.Insert(0, image); // the ground is under everything
                _groundChunks[(cx, cy)] = image;
            }
        _shownChunks = (-1, -1, -2, -2);
    }

    /// <summary>One chunk's ground: its tiles drawn into one picture (its full size kept even where it has no tiles).</summary>
    private ImageSource GroundChunkPicture(int cx, int cy)
    {
        var ground = _map.Layers[0];
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(group, EdgeMode.Aliased); // no seams between tiles
        double t = _tileWidth, side = ChunkTiles * t;
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 2 * side, side));
            // The Ultima 8 view: each cell's diamond, back to front, its north corner (x - y) cells across and (x + y) / 2 down from the chunk's.
            for (int sum = 0; sum <= 2 * (ChunkTiles - 1); sum++)
                for (int y = Math.Max(0, sum - ChunkTiles + 1); y <= Math.Min(sum, ChunkTiles - 1); y++)
                {
                    int x = sum - y;
                    int mx = cx * ChunkTiles + x, my = cy * ChunkTiles + y;
                    if (mx >= _map.Width || my >= _map.Height) continue;
                    int tile = ground.GetTile(mx, my, _map.Width);
                    if (tile < 0 || TilePicture(tile) is not { } picture) continue;
                    var cell = new Rect(side + (x - y) * t - t, (x + y) * t / 2, 2 * t, t);
                    if (TilePropertiesOf(tile).UnderTileIndex is >= 0 and int under && TilePicture(TileSheets.Make(TileSheets.Slot(tile), under)) is { } below)
                        dc.DrawImage(below, cell); // its tile under it, through its see-through pixels
                    dc.DrawImage(picture, cell);
                }
        }
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>The ground chunks in or next to the view are drawn; the rest aren't (they cost nothing).</summary>
    private void ShowGroundNearView()
    {
        if (_groundChunks.Count == 0) return;
        double chunkW = ChunkTiles * _tileWidth;
        // The Ultima 8 view: a chunk is drawn while its diamond is in or next to the view, on the screen.
        var now = ((int)Math.Floor(_cameraX / chunkW), (int)Math.Floor(_cameraY / chunkW), 0, 0);
        if (now == _shownChunks) return;
        var view = new Rect(_cameraX - chunkW, _cameraY - chunkW, ViewportWidth + 2 * chunkW, ViewportHeight + 2 * chunkW);
        foreach (var ((cx, cy), image) in _groundChunks)
        {
            bool near = ChunkOnScreen(cx, cy).IntersectsWith(view);
            var wanted = near ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
            if (image.Visibility != wanted) image.Visibility = wanted;
        }
        _shownChunks = now;
    }

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
            if (_groundChunks.TryGetValue((x / ChunkTiles, y / ChunkTiles), out var chunk)) chunk.Source = GroundChunkPicture(x / ChunkTiles, y / ChunkTiles); // its chunk drawn again
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
        var image = new Image { Width = _sheetTileWidth, Height = _sheetTileHeight, IsHitTestVisible = hitTest, Source = TileSource(tile) };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        U8ProjectionCanvas.SetPlacement(image, U8Placement.Cell); // its cell's diamond (the Ultima 8 view)
        return image;
    }
}
