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
            Canvas.SetLeft(image, x * _tileset.TileWidth);
            Canvas.SetTop(image, y * _tileset.TileHeight);
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
            Canvas.SetLeft(image, key.X * EditorChunkCells * _tileset.TileWidth);
            Canvas.SetTop(image, key.Y * EditorChunkCells * _tileset.TileHeight);
            MapCanvas.Children.Insert(0, image); // under everything else on this floor (the floors below have their own, lower Z)
            _tileChunks[key] = image;
        }
        image.Source = picture;
    }

    /// <summary>A layer's chunk as one frozen picture (null when it has no tiles), its size the chunk's full size.</summary>
    private ImageSource? ChunkPicture(MapLayer layer, int cx, int cy, bool skipAnimated, bool withUnder)
    {
        if (_map is null || _tileset is null) return null;
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
}
