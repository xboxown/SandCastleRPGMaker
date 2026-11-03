using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Map Editor's tiles, drawn 16 x 16 cells to a picture (as Game Test's ground chunks) instead of a picture
/// per cell: a 300 x 500 map is 600 pictures, not 150 000 - built in moments, light on memory, and nothing for
/// WPF to walk each frame. Painting a cell redraws its chunk. Animated tiles keep a picture of their own (their
/// animation plays in it) over the chunk.
/// </summary>
public partial class MainWindow
{
    private const int EditorChunkCells = 16;
    private readonly Dictionary<(int X, int Y), Image> _tileChunks = new();
    private readonly HashSet<(int X, int Y)> _dirtyChunks = new();
    private bool _deferChunks;

    /// <summary>A cell's tile changed: an animated tile gets its own picture; the chunk is drawn again (or, while the whole map is built, once at the end).</summary>
    private void PaintCellVisual(int x, int y, int tileIndex)
    {
        if (_tilesetBitmap is null || _tileset is null) return;
        RemoveAnimatedCell(x, y);
        var animationSprite = tileIndex >= 0 ? LinkedAnimationOfMapTile(tileIndex) : null; // its own sheet's tile animation
        if (animationSprite is { Frames.Count: > 0 })
        {
            var image = new Image { Width = _tileset.TileWidth, Height = _tileset.TileHeight, IsHitTestVisible = false };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            Canvas.SetLeft(image, x * CellW);
            Canvas.SetTop(image, y * CellH);
            if (IsUltima8) U8ProjectionCanvas.SetPlacement(image, U8Placement.Cell); // its diamond on its cell, as the game draws it
            MapCanvas.Children.Add(image);
            _mapCellVisuals[(x, y)] = image;
            _mapCellAnimators[(x, y)] = new SpriteAnimator(animationSprite, image, _resourceDatabase);
        }
        MarkChunk(x, y);
    }

    /// <summary>A cell's own animated picture, stopped and taken away.</summary>
    private void RemoveAnimatedCell(int x, int y)
    {
        if (_mapCellAnimators.Remove((x, y), out var animator)) animator.Stop();
        if (_mapCellVisuals.Remove((x, y), out var image)) MapCanvas.Children.Remove(image);
    }

    private void MarkChunk(int x, int y)
    {
        var key = (x / EditorChunkCells, y / EditorChunkCells);
        if (_deferChunks) _dirtyChunks.Add(key);
        else RebuildChunk(key);
    }

    /// <summary>While the whole map is built: chunks are drawn once, at the end.</summary>
    private void FlushChunks()
    {
        _deferChunks = false;
        foreach (var key in _dirtyChunks) RebuildChunk(key);
        _dirtyChunks.Clear();
    }

    /// <summary>One chunk of the picked floor drawn again: its static tiles (with their tiles under them on the ground).</summary>
    private void RebuildChunk((int X, int Y) key)
    {
        if (_map is null || _tileset is null) return;
        var picture = _map.LayerForFloor(_currentFloor) is { } layer ? ChunkPicture(layer, key.X, key.Y, skipAnimated: true, withUnder: _currentFloor <= 1) : null;
        if (picture is null)
        {
            if (_tileChunks.Remove(key, out var gone)) MapCanvas.Children.Remove(gone);
            return;
        }
        if (!_tileChunks.TryGetValue(key, out var image))
        {
            image = new Image { IsHitTestVisible = false, Stretch = Stretch.None };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            PlaceChunk(image, key.X, key.Y, 0);
            MapCanvas.Children.Insert(0, image); // under everything else on this floor (the floors below have their own, lower Z)
            _tileChunks[key] = image;
        }
        image.Source = picture;
    }

    /// <summary>
    /// Where a chunk's picture goes, lowered by <paramref name="shift"/> (a floor below). Ultima 7: its square of cells.
    /// Ultima 8: its cells' diamonds are drawn into it already (ChunkPicture), so it's placed on the screen as it is - a
    /// lift's up-left shift is a straight drop in the diamond view.
    /// </summary>
    private void PlaceChunk(Image image, int cx, int cy, double shift)
    {
        if (!IsUltima8)
        {
            Canvas.SetLeft(image, cx * EditorChunkCells * CellW + shift);
            Canvas.SetTop(image, cy * EditorChunkCells * CellH + shift);
            return;
        }
        var (left, top) = DiamondChunkCorner(cx, cy);
        U8ProjectionCanvas.SetPlacement(image, U8Placement.Screen);
        Canvas.SetLeft(image, left + MapCanvas.OriginX);
        Canvas.SetTop(image, top + MapCanvas.OriginY + shift);
    }

    /// <summary>Ultima 8: a chunk's cells as a range, and the top-left of the box round their diamonds (without the canvas's origin).</summary>
    private (double Left, double Top) DiamondChunkCorner(int cx, int cy)
    {
        int c = CellH, x0 = cx * EditorChunkCells, y0 = cy * EditorChunkCells, y1 = Math.Min(y0 + EditorChunkCells, _map!.Height);
        return ((x0 - y1) * c, (x0 + y0) * c / 2.0);
    }

    /// <summary>A layer's chunk as one frozen picture (null when it has no tiles), its size the chunk's full size.</summary>
    private ImageSource? ChunkPicture(MapLayer layer, int cx, int cy, bool skipAnimated, bool withUnder)
    {
        if (_map is null || _tileset is null) return null;
        if (IsUltima8) return DiamondChunkPicture(layer, cx, cy, skipAnimated, withUnder);
        int tw = _tileset.TileWidth, th = _tileset.TileHeight, drawn = 0;
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(group, EdgeMode.Aliased); // no seams between tiles
        using (var dc = group.Open())
        {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, EditorChunkCells * tw, EditorChunkCells * th));
            for (int y = 0; y < EditorChunkCells; y++)
                for (int x = 0; x < EditorChunkCells; x++)
                {
                    int mx = cx * EditorChunkCells + x, my = cy * EditorChunkCells + y;
                    if (mx >= _map.Width || my >= _map.Height) continue;
                    if (skipAnimated && _mapCellVisuals.ContainsKey((mx, my))) continue;
                    int tile = layer.GetTile(mx, my, _map.Width);
                    if (tile < 0 || TileSource(tile) is not { } source) continue;
                    var at = new Rect(x * tw, y * th, tw, th);
                    if (withUnder && UnderTileOf(tile) is int under && TileSource(under) is { } below) dc.DrawImage(below, at); // its tile under it, through its see-through pixels
                    dc.DrawImage(source, at);
                    drawn++;
                }
        }
        if (drawn == 0) return null;
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    /// <summary>
    /// Ultima 8: a chunk drawn as the game draws its ground - each cell's tile, a diamond twice as wide as the cell, laid on
    /// that cell's diamond (square map cell c: its diamond 2c wide, c tall), so the tiles interlock with no gaps.
    /// </summary>
    private ImageSource? DiamondChunkPicture(MapLayer layer, int cx, int cy, bool skipAnimated, bool withUnder)
    {
        int c = CellH, drawn = 0;
        int x0 = cx * EditorChunkCells, y0 = cy * EditorChunkCells;
        int x1 = Math.Min(x0 + EditorChunkCells, _map!.Width), y1 = Math.Min(y0 + EditorChunkCells, _map.Height);
        var (left, top) = DiamondChunkCorner(cx, cy);
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
        RenderOptions.SetEdgeMode(group, EdgeMode.Aliased);
        using (var dc = group.Open())
        {
            int span = x1 - x0 + y1 - y0;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, span * c, span * c / 2.0));
            for (int my = y0; my < y1; my++)
                for (int mx = x0; mx < x1; mx++)
                {
                    if (skipAnimated && _mapCellVisuals.ContainsKey((mx, my))) continue;
                    int tile = layer.GetTile(mx, my, _map.Width);
                    if (tile < 0 || TileSource(tile) is not { } source) continue;
                    // The cell's north corner on the screen: (x - y, (x + y) / 2) of its top-left; its diamond spans c either side.
                    var at = new Rect((mx - my) * c - c - left, (mx + my) * c / 2.0 - top, 2 * c, c);
                    if (withUnder && UnderTileOf(tile) is int under && TileSource(under) is { } below) dc.DrawImage(below, at);
                    dc.DrawImage(source, at);
                    drawn++;
                }
        }
        if (drawn == 0) return null;
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }
}
