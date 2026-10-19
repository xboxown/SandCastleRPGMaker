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
    private void SplitWallSets()
    {
        if (_objects.WallDesigns.Count == 0) return;
        var sets = _map.Objects.Where(o => o.ContainerInstanceId < 0 && !o.WallSetPiece
            && WallLook.PoseOf(_objects.Objects.FirstOrDefault(d => d.Id == o.ObjectId), _objects, o.Facing) is not null).ToList();
        int next = _map.GetNextInstanceId(); // every piece its own number
        foreach (var set in sets)
        {
            var pose = WallLook.PoseOf(_objects.Objects.First(d => d.Id == set.ObjectId), _objects, set.Facing)!;
            _map.Objects.Remove(set);
            var pieces = WallLook.Pieces(set, pose, _tileWidth, _tileHeight, () => next++);
            // The set's starting open / locked (Map Editor, right-click) is its door's; a window's shutters start as the Window object says.
            foreach (var piece in pieces.Where(p => _objects.Objects.FirstOrDefault(d => d.Id == p.ObjectId)?.Type != ObjectType.Door))
            {
                piece.StartsOpen = null;
                piece.StartsLocked = null;
            }
            _map.Objects.AddRange(pieces);
        }
    }

    /// <summary>A piece of a wall set: it stays at its designed height.</summary>
    private static bool IsWallSetPiece(object entity) => entity is ObjectInstance { WallSetPiece: true };
}
