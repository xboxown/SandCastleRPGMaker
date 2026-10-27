using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Any number of tilesheets in one map (Tileset tab > Tilesheet(s)): the
/// map's first sheet keeps its tiles' own numbers; every other sheet picked
/// is added to the map (its tiles numbered sheet x TileSheets.Block + their
/// number in it - TilesetDefinition.ExtraSheetNames), so painting from a new
/// sheet never re-reads the tiles already painted. All sheets of one map
/// share its tile size. The tilesheets themselves are made, renamed and
/// deleted in the Sprite Editor (its Tileset group).
/// </summary>
public partial class MainWindow
{
    /// <summary>Each tilesheet's working bitmap (built from its sprite's frames), by name.</summary>
    private readonly Dictionary<string, (BitmapSource Bitmap, int Columns, int Rows)> _sheetCache = new();

    /// <summary>The sheet shown in the palette (its name, and its number in the map: 0 = the first).</summary>
    private string _paletteSheetName = string.Empty;
    private int _paletteSlot;

    /// <summary>The tile a palette pick paints: its number in the map (sheet and tile).</summary>
    private int PaintTileIndex => _selectedTileIndex < 0 ? -1 : TileSheets.Make(_paletteSlot, _selectedTileIndex);

    private SpriteEntry? TilesheetSprite(string name) =>
        _spriteDatabase.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.TilesetGroupName)?.Sprites.FirstOrDefault(s => s.Name == name);

    /// <summary>A sheet's bitmap, built once (rebuilt after its tiles are edited).</summary>
    private (BitmapSource Bitmap, int Columns, int Rows)? Sheet(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (_sheetCache.TryGetValue(name, out var cached)) return cached;
        if (TilesheetSprite(name) is not { Frames.Count: > 0 } sprite) return null;
        var (bitmap, _, _, columns, rows) = BuildCompositeTilesetBitmap(sprite);
        if (bitmap is null) return null;
        return _sheetCache[name] = (bitmap, columns, rows);
    }

    /// <summary>The map's first sheet (the game gets it whole; its tiles keep their own numbers).</summary>
    private (BitmapSource Bitmap, int Columns, int Rows)? PrimarySheet() => _tileset is null ? null : Sheet(_tileset.TilesetSpriteName);

    /// <summary>A painted tile's picture, from whichever of the map's sheets it's from.</summary>
    private BitmapSource? TileSource(int tileIndex)
    {
        if (_tileset is null || tileIndex < 0) return null;
        if (Sheet(_tileset.SheetNameOf(tileIndex)) is not { } sheet) return null;
        int local = TileSheets.Local(tileIndex), w = _tileset.TileWidth, h = _tileset.TileHeight;
        int col = local % sheet.Columns, row = local / sheet.Columns;
        if ((col + 1) * w > sheet.Bitmap.PixelWidth || (row + 1) * h > sheet.Bitmap.PixelHeight) return null; // the sheet lost tiles since
        // One shared, frozen picture per tile (not a new one per cell): a big map is built in moments, not minutes.
        // Keyed by the sheet's bitmap, so a sheet rebuilt after its tiles are edited gets fresh pictures.
        var key = (sheet.Bitmap, local, w, h);
        if (_tilePictures.TryGetValue(key, out var cached)) return cached;
        if (_tilePictures.Count > 50000) _tilePictures.Clear();
        var crop = new CroppedBitmap(sheet.Bitmap, new Int32Rect(col * w, row * h, w, h));
        if (crop.CanFreeze) crop.Freeze();
        return _tilePictures[key] = crop;
    }

    private readonly Dictionary<(BitmapSource Sheet, int Local, int W, int H), BitmapSource> _tilePictures = new();

    /// <summary>A tile's picture by a given world's tileset (another world in the game may use other sheets than the one open here).</summary>
    private BitmapSource? TileSourceFor(TilesetDefinition tileset, int tileIndex)
    {
        if (tileIndex < 0 || Sheet(tileset.SheetNameOf(tileIndex)) is not { } sheet) return null;
        int local = TileSheets.Local(tileIndex), w = tileset.TileWidth, h = tileset.TileHeight;
        int col = local % sheet.Columns, row = local / sheet.Columns;
        if ((col + 1) * w > sheet.Bitmap.PixelWidth || (row + 1) * h > sheet.Bitmap.PixelHeight) return null;
        return new CroppedBitmap(sheet.Bitmap, new Int32Rect(col * w, row * h, w, h));
    }

    /// <summary>A painted tile's animation (Tile properties > Animation of its own sheet's tile).</summary>
    private SpriteEntry? LinkedAnimationOfMapTile(int tileIndex)
    {
        if (_tileset is null) return null;
        var props = _tilePropertiesDatabase.GetOrCreate(_tileset.SheetNameOf(tileIndex), TileSheets.Local(tileIndex));
        if (string.IsNullOrEmpty(props.AnimationSpriteName)) return null;
        return _spriteDatabase.Groups.FirstOrDefault(g => g.Name == "TileAnimations")?.Sprites.FirstOrDefault(s => s.Name == props.AnimationSpriteName);
    }

    /// <summary>
    /// Another tilesheet picked while the map has tiles painted: it's added to
    /// the map and shown in the palette to paint from - the tiles already
    /// painted keep their pictures. A sheet of another tile size can't be.
    /// </summary>
    private bool ShowSheetInPalette(SpriteEntry sprite)
    {
        if (_tileset is null || _map is null) return false;
        _sheetCache.Remove(sprite.Name); // freshly built: its tiles may have been edited
        var (bitmap, tileWidth, tileHeight, columns, rows) = BuildCompositeTilesetBitmap(sprite);
        if (bitmap is null) { MessageBox.Show(this, $"Could not build \"{sprite.Name}\" - one or more of its tile images could not be loaded.", "Tilesheet", MessageBoxButton.OK, MessageBoxImage.Error); return false; }
        if (tileWidth != _tileset.TileWidth || tileHeight != _tileset.TileHeight)
        {
            MessageBox.Show(this, $"\"{sprite.Name}\" has {tileWidth} x {tileHeight} tiles; this map's are {_tileset.TileWidth} x {_tileset.TileHeight}. All the tilesheets of one map have one tile size.",
                "Tilesheet", MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        _sheetCache[sprite.Name] = (bitmap, columns, rows);
        int slot = 0;
        if (!string.Equals(sprite.Name, _tileset.TilesetSpriteName, StringComparison.Ordinal))
        {
            int at = _tileset.ExtraSheetNames.IndexOf(sprite.Name);
            if (at < 0) { _tileset.ExtraSheetNames.Add(sprite.Name); at = _tileset.ExtraSheetNames.Count - 1; }
            slot = at + 1;
        }
        _paletteSheetName = sprite.Name;
        _paletteSlot = slot;
        _tilesetBitmap = bitmap;
        _paletteColumns = columns;
        _paletteRows = rows;
        DrawPalette();
        StatusText.Text = slot == 0
            ? $"Tilesheet \"{sprite.Name}\" (this map's first)."
            : $"Tilesheet \"{sprite.Name}\" - added to this map (sheet {slot + 1}); paint with its tiles alongside the others.";
        return true;
    }
}
