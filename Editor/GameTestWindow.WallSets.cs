using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Wall sets (Wall Designer) in play. A set is placed in the Map Editor as
/// one object; when the game starts it becomes its pieces - each the real
/// object it is (a half wall, the piece over a window, a Window with its
/// shutters and glass, a Door with its lock), standing where and as high as
/// it was designed, never falling. Clicked, the wall is a wall and the window
/// a window; each keeps its own state in saves (a save holds the pieces, so
/// a loaded game isn't split again).
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Every placed wall set becomes its pieces (on the game's own copy of the map).</summary>
    private void SplitWallSets() => WallSets.SplitInto(_map, _objects, _tileWidth, _tileHeight);

    /// <summary>A piece of a wall set: it stays at its designed height.</summary>
    private static bool IsWallSetPiece(object entity) => entity is ObjectInstance { WallSetPiece: true };
}
