// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using System.Windows;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Ultima 8 view (U8ProjectionCanvas, Pentagram's ItemSorter projection): the map's cells are square in the
/// engine's own coordinates and drawn as diamonds - a tile on the sheet is a diamond W x H, its map cell H x H.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>A tile's size on its sheet (the diamond drawn): what TileSource cuts out.</summary>
    private int _sheetTileWidth, _sheetTileHeight;
    /// <summary>The screen size the whole map covers in the Ultima 8 view (the camera stays inside it).</summary>
    private Size _viewSize;
}
