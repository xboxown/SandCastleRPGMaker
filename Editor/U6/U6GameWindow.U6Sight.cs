// Ultima 6 engine (U6GameWindow): what the avatar can't see is black - Ultima 6 projects only.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 6's blacking (Nuvie MapWindow::generateTmpMap / boundaryFill): from the avatar's square the view spreads to every
/// square sight can pass through; those, and the walls bordering them, are drawn - everything else is black. Sight is stopped
/// by walls, closed doors, anything marked Occludes in the Object Editor, and ground that can't be walked on and isn't water
/// (a mountain, a cliff). Windows, open doors and water let it through. No roofs in Ultima 6: this is what hides the inside of
/// a house from the street. Off in System Settings > Ultima 6 > World view.
/// </summary>
public partial class U6GameWindow
{
    private Path? _sightShade;
    private (int X, int Y, long World, int Left, int Top, int Right, int Bottom)? _sightKey;
    private HashSet<(int, int)>? _sightBlockers;
    /// <summary>The squares walls, doors and windows stand on: never black - the dark is only what's inside them.</summary>
    private HashSet<(int, int)> _sightWallCells = new();
    private long _sightBlockersVersion = -1;

    /// <summary>The squares things stop sight on (walls, closed doors, Occludes), made again when the world changes.</summary>
    private HashSet<(int, int)> SightBlockers()
    {
        if (_sightBlockers is not null && _sightBlockersVersion == _worldVersion) return _sightBlockers;
        var cells = new HashSet<(int, int)>();
        var wallCells = new HashSet<(int, int)>();
        bool allPictured = true;
        foreach (var obj in _map.Objects)
        {
            if (obj.ContainerInstanceId >= 0 || DefinitionOf(obj) is not { } def) continue;
            if (obj.Z >= MapFloors.BaseLift(2)) continue;
            bool wall = def.Type is ObjectType.Wall or ObjectType.Door or ObjectType.Window;
            bool blocks = def.Occludes || def.Type == ObjectType.Wall || (def.Type == ObjectType.Door && !IsDoorOpen(obj));
            if (!wall && (!blocks || def.Type == ObjectType.Window)) continue;
            var body = BodyOf(null, obj);
            for (int ux = body.Left; ux < body.Left + Math.Max(1, body.Width); ux++)
                for (int uy = body.Top; uy < body.Top + Math.Max(1, body.Depth); uy++)
                {
                    var cell = ((int)Math.Floor(ux / (double)UnitsPerTileX), (int)Math.Floor(uy / (double)UnitsPerTileY));
                    if (blocks && def.Type != ObjectType.Window) cells.Add(cell);
                }
            // The square its picture stands on (a wall piece is a tile; its solid box can reach into the room beside it).
            if (!wall) continue;
            if (_objectVisuals.TryGetValue(obj, out var visual) && ScreenRectOf(visual) is { IsEmpty: false } picture)
                wallCells.Add(((int)Math.Floor((picture.Left + picture.Width / 2) / _tileWidth), (int)Math.Floor((picture.Top + picture.Height / 2) / _tileHeight)));
            else allPictured = false;
        }
        _sightBlockers = cells;
        _sightWallCells = wallCells;
        _sightBlockersVersion = allPictured ? _worldVersion : -1; // a wall not drawn yet: worked out again next time
        return cells;
    }

    private bool BlocksSight(int x, int y, HashSet<(int, int)> blockers)
    {
        if (blockers.Contains((x, y))) return true;
        if (x < 0 || y < 0 || x >= _map.Width || y >= _map.Height) return true;
        int tile = _map.Layers[0].GetTile(x, y, _map.Width);
        return tile >= 0 && TilePropertiesOf(tile) is { Walkable: false, IsWater: false };
    }

    /// <summary>Each frame: the black drawn again when the avatar's square, the world or the view moved.</summary>
    private void UpdateSight()
    {
        bool on = _systemSettings.Ultima6.LineOfSight && Hud.First(HudPieceKind.GameView) is not null && _player is not null && !_gameOver;
        if (!on) { if (_sightShade is not null) _sightShade.Visibility = Visibility.Collapsed; return; }
        int left = (int)Math.Floor(_cameraX / _tileWidth) - 1, top = (int)Math.Floor(_cameraY / _tileHeight) - 1;
        int right = (int)Math.Ceiling((_cameraX + ViewportWidth) / _tileWidth) + 1, bottom = (int)Math.Ceiling((_cameraY + ViewportHeight) / _tileHeight) + 1;
        // Seen from whoever the view follows (Camera Follows someone else: from them - else the view would be all black).
        var (eyeX, eyeY) = _cameraFollow is { } followed && MapCharacterOf(followed) is { } eye ? (eye.Placement.X, eye.Placement.Y) : (_playerX, _playerY);
        var key = (eyeX, eyeY, _worldVersion, left, top, right, bottom);
        if (_sightKey == key && _sightShade is { Visibility: Visibility.Visible }) return;
        _sightKey = key;

        var blockers = SightBlockers();
        var seen = new HashSet<(int, int)>();
        var queue = new Queue<(int, int)>();
        seen.Add((eyeX, eyeY));
        queue.Enqueue((eyeX, eyeY));
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if ((x, y) != (eyeX, eyeY) && BlocksSight(x, y, blockers)) continue; // a wall is seen, not seen through
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nx = x + dx, ny = y + dy;
                if (nx < left || ny < top || nx > right || ny > bottom || !seen.Add((nx, ny))) continue;
                queue.Enqueue((nx, ny));
            }
        }
        // Walls at a corner of what is seen show too (a room's corners).
        foreach (var (x, y) in seen.ToList())
        {
            if (BlocksSight(x, y, blockers) && (x, y) != (eyeX, eyeY)) continue;
            foreach (var (dx, dy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
                if (BlocksSight(x + dx, y + dy, blockers)) seen.Add((x + dx, y + dy));
        }

        var black = new GeometryGroup { FillRule = FillRule.Nonzero };
        for (int y = top; y <= bottom; y++)
        {
            int runStart = -1;
            for (int x = left; x <= right + 1; x++)
            {
                bool hidden = x <= right && !seen.Contains((x, y)) && !_sightWallCells.Contains((x, y)); // walls always show; only inside them is dark
                if (hidden && runStart < 0) runStart = x;
                if (!hidden && runStart >= 0)
                {
                    black.Children.Add(new RectangleGeometry(new Rect(runStart * _tileWidth, y * _tileHeight, (x - runStart) * _tileWidth, _tileHeight)));
                    runStart = -1;
                }
            }
        }
        black.Freeze();
        if (_sightShade is null)
        {
            _sightShade = new Path { Fill = Brushes.Black, IsHitTestVisible = false, SnapsToDevicePixels = true };
            Panel.SetZIndex(_sightShade, AboveEverythingZIndex - 1000);
            GameCanvas.Children.Add(_sightShade);
        }
        _sightShade.Data = black;
        _sightShade.Visibility = Visibility.Visible;
        if (_sightBlockersVersion != _worldVersion) _sightKey = null; // some wall wasn't drawn yet: again next frame, so it shows
    }

    /// <summary>The ground's name on a square: the top tile with a name (Tile Properties > Name); null: none named.</summary>
    private string? TileNameAt(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _map.Width || y >= _map.Height) return null;
        for (int layer = _map.Layers.Count - 1; layer >= 0; layer--)
        {
            if (_map.Layers[layer].Floor > 1) continue; // one floor in Ultima 6
            int tile = _map.Layers[layer].GetTile(x, y, _map.Width);
            if (tile >= 0 && TilePropertiesOf(tile).TerrainLabel is { Length: > 0 } name) return name;
        }
        return null;
    }

    /// <summary>Can the avatar see this square now (Look, a click: what's black is "nothing")?</summary>
    private bool SquareSeen(int x, int y) => _sightShade is not { Visibility: Visibility.Visible } || _sightShade.Data is not GeometryGroup g
        || !g.FillContains(new Point((x + 0.5) * _tileWidth, (y + 0.5) * _tileHeight));
}
