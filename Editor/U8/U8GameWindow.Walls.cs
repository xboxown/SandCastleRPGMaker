// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Who is drawn in front of a wall (or door), ported line for line from
/// Exult (objs.cc Game_object::compare, Compare_ranges; ordinfo.h). Every
/// thing is a 3D box of tiles - x from xleft to xright, y from yfar to ynear,
/// lifts from zbot to ztop (a flat thing's zbot one lower). Two things whose
/// pictures overlap on screen are compared dimension by dimension: wholly
/// before or after in every one decides it; otherwise the overlapping
/// dimensions decide which one goes first. A wall's long box is what makes
/// walking along it look right - in front of it on the near side, behind it
/// on the far side, never "underneath" it - which the plain position order
/// can't do for something longer than a cell.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>Exult's Ordering_info: a thing's tile box.</summary>
    private readonly record struct OrderBox(int XLeft, int XRight, int YFar, int YNear, int ZBot, int ZTop, bool Flat, Rect Screen, int Width, int Height);

    private static OrderBox BoxOf(UnitRect foot, int lift, int height, Rect screen) => new(
        foot.Left, foot.Right - 1, foot.Top, foot.Bottom - 1,
        height == 0 ? lift - 1 : lift, lift + Math.Max(height, 1) - 1, height == 0, screen, foot.Width, foot.Depth);

    /// <summary>Compare_ranges.</summary>
    private static void CompareRanges(int from1, int to1, int from2, int to2, out int cmp, out bool overlap)
    {
        if (to1 < from2) { overlap = false; cmp = -1; }
        else if (to2 < from1) { overlap = false; cmp = 1; }
        else
        {
            overlap = true;
            cmp = from1 < from2 ? -1 : from1 > from2 ? 1 : to1 < to2 ? 1 : to1 > to2 ? -1 : 0;
        }
    }

    /// <summary>Game_object::compare: -1 if the first is drawn before the second, 1 if after, 0 if it doesn't matter.</summary>
    private static int ExultCompare(OrderBox a, OrderBox b, bool aOccludes, bool bOccludes)
    {
        if (!a.Screen.IntersectsWith(b.Screen)) return 0;
        CompareRanges(a.XLeft, a.XRight, b.XLeft, b.XRight, out int xcmp, out bool xover);
        CompareRanges(a.YFar, a.YNear, b.YFar, b.YNear, out int ycmp, out bool yover);
        CompareRanges(a.ZBot, a.ZTop, b.ZBot, b.ZTop, out int zcmp, out bool zover);
        if (xcmp == 0 && ycmp == 0 && zcmp == 0)
            return a.Screen.Width < b.Screen.Width && a.Screen.Height < b.Screen.Height ? -1 : a.Screen.Width > b.Screen.Width && a.Screen.Height > b.Screen.Height ? 1 : 0;
        if (xover && yover && zover)
        {
            if (a.Flat) return b.Flat ? 0 : -1; // flat ones first
            if (b.Flat) return 1;
        }
        if (xcmp >= 0 && ycmp >= 0 && zcmp >= 0) return 1;
        if (xcmp <= 0 && ycmp <= 0 && zcmp <= 0) return -1;
        if (yover)
        {
            if (xover) return zcmp;
            if (zover) return xcmp;
            if (zcmp == 0) return xcmp;
            if (xcmp == zcmp) return xcmp;
            if (a.ZTop / 5 < b.ZBot / 5 && bOccludes) return -1;
            if (b.ZTop / 5 < a.ZBot / 5 && aOccludes) return 1;
            return 0;
        }
        if (xover)
        {
            if (zover || zcmp == 0) return ycmp;
            return ycmp == zcmp ? ycmp : 0;
        }
        if (xcmp == -1)
        {
            if (ycmp == -1) return zover || zcmp <= 0 ? -1 : 0;
        }
        else if (ycmp == 1)
        {
            if (zover || zcmp >= 0) return 1;
            if (a.ZTop / 5 < b.ZBot / 5) return -1;
            return 0;
        }
        return 0;
    }

    private static Rect ScreenRectOf(UIElement visual) =>
        visual is FrameworkElement fe ? new Rect(Canvas.GetLeft(fe), Canvas.GetTop(fe), Math.Max(1, fe.Width), Math.Max(1, fe.Height)) : Rect.Empty;

    /// <summary>The walls and doors on the map, with their boxes and pictures.</summary>
    private IEnumerable<(ObjectInstance Wall, OrderBox Box, UIElement Visual, bool Occludes)> WallBoxes()
    {
        foreach (var instance in _map.Objects)
        {
            if (instance.ContainerInstanceId >= 0 || DefinitionOf(instance) is not { Type: ObjectType.Wall or ObjectType.Door or ObjectType.Window } obj) continue;
            if (!_objectVisuals.TryGetValue(instance, out var visual)) continue;
            var body = BodyOf(null, instance);
            yield return (instance, BoxOf(new UnitRect(body.Left, body.Top, body.Width, body.Depth), body.Z, body.Height, ScreenRectOf(visual)), visual, obj.Occludes);
        }
    }

    // The walls kept, with a grid of where their pictures are: ExultCompare orders only pictures that overlap on screen,
    // so something moving needs only the walls whose pictures are near its own - not every wall on the map.
    private List<(ObjectInstance Wall, OrderBox Box, UIElement Visual, bool Occludes)>? _walls;
    private Dictionary<(int, int), List<int>>? _wallCells;
    private long _wallsVersion = -1;
    private const double WallCellPixels = 64;

    private List<(ObjectInstance Wall, OrderBox Box, UIElement Visual, bool Occludes)> KeptWalls()
    {
        if (_walls is not null && _wallCells is not null && _wallsVersion == _worldVersion) return _walls;
        _walls = WallBoxes().ToList();
        _wallCells = new Dictionary<(int, int), List<int>>();
        for (int i = 0; i < _walls.Count; i++)
            foreach (var cell in CellsOf(_walls[i].Box.Screen))
            {
                if (!_wallCells.TryGetValue(cell, out var list)) _wallCells[cell] = list = new List<int>(2);
                list.Add(i);
            }
        _wallsVersion = _worldVersion;
        return _walls;
    }

    private static IEnumerable<(int, int)> CellsOf(Rect screen)
    {
        if (screen.IsEmpty) yield break;
        for (int cy = (int)Math.Floor(screen.Top / WallCellPixels); cy <= (int)Math.Floor(screen.Bottom / WallCellPixels); cy++)
            for (int cx = (int)Math.Floor(screen.Left / WallCellPixels); cx <= (int)Math.Floor(screen.Right / WallCellPixels); cx++)
                yield return (cx, cy);
    }

    /// <summary>The walls whose pictures could overlap a picture at this place on screen.</summary>
    private IEnumerable<(ObjectInstance Wall, OrderBox Box, UIElement Visual, bool Occludes)> WallsNear(Rect screen)
    {
        var walls = KeptWalls();
        var seen = new HashSet<int>();
        foreach (var cell in CellsOf(screen))
            if (_wallCells!.TryGetValue(cell, out var list))
                foreach (int i in list)
                    if (seen.Add(i)) yield return walls[i];
    }

    /// <summary>A paint order for something moving among walls: after every wall Exult says it's in front of, before every wall it's behind.</summary>
    private int OrderAmongWalls(UnitRect foot, int lift, int height, Rect screen, int zIndex)
    {
        var me = BoxOf(foot, lift, height, screen);
        int lowest = int.MaxValue;
        foreach (var (_, box, visual, occludes) in WallsNear(screen))
        {
            int cmp = ExultCompare(me, box, false, occludes);
            int wallZ = Panel.GetZIndex(visual);
            if (cmp > 0) zIndex = Math.Max(zIndex, wallZ + 1);
            else if (cmp < 0) lowest = Math.Min(lowest, wallZ - 1);
        }
        return lowest == int.MaxValue ? zIndex : Math.Min(zIndex, lowest);
    }

    /// <summary>The player's paint order, walls taken into account.</summary>
    private int PlayerOrderAmongWalls(int zIndex) =>
        OrderAmongWalls(PlayerFootprint(), _playerZ, _playerSize.Z, ScreenRectOf(_playerImage),
            OverUpperFloorsInFront(PlayerFootprint(), _playerZ, ScreenRectOf(_playerImage), zIndex)); // in front of an upper floor's tiles: over them

    /// <summary>A placed character's paint order, walls taken into account.</summary>
    private void OrderCharacterAmongWalls(MapCharacter mapCharacter)
    {
        if (mapCharacter.Visual is not { } visual) return;
        var foot = CharacterFootprint(mapCharacter);
        int zIndex = OverUpperFloorsInFront(foot, mapCharacter.Lift, ScreenRectOf(visual), Panel.GetZIndex(visual)); // in front of an upper floor's tiles: over them
        Panel.SetZIndex(visual, OrderAmongWalls(foot, mapCharacter.Lift, mapCharacter.Size.Z, ScreenRectOf(visual), zIndex));
    }

    /// <summary>Items and objects lying near walls (after every stacking refresh).</summary>
    private void ApplyWallDrawOrder()
    {
        // Upper floors' tiles first: whatever stands wholly in front of them is drawn over them (U8GameWindow.UpperFloorOrder.cs).
        _upperSlabs = null;
        foreach (var (item, visual) in _itemVisuals.ToList()) Panel.SetZIndex(visual, OverUpperFloorsInFront(item, visual));
        foreach (var (obj, visual) in _objectVisuals.ToList())
            if (!IsUpperFloorTile(obj)) Panel.SetZIndex(visual, OverUpperFloorsInFront(obj, visual));
        if (KeptWalls().Count == 0) return;
        void Order(object entity, UIElement visual)
        {
            var body = entity is PlacedItemInstance item ? BodyOf(item, null) : BodyOf(null, (ObjectInstance)entity);
            var foot = new UnitRect(body.Left, body.Top, body.Width, body.Depth);
            if (HangsOnWall(entity))
            {
                // On the wall's face: drawn over every wall its picture is over.
                var screen = ScreenRectOf(visual);
                int z = Panel.GetZIndex(visual);
                foreach (var (_, _, wallVisual, _) in WallsNear(screen))
                    if (ScreenRectOf(wallVisual).IntersectsWith(screen)) z = Math.Max(z, Panel.GetZIndex(wallVisual) + 1);
                Panel.SetZIndex(visual, z);
                return;
            }
            Panel.SetZIndex(visual, OrderAmongWalls(foot, body.Z, body.Height, ScreenRectOf(visual), Panel.GetZIndex(visual)));
        }
        foreach (var (item, visual) in _itemVisuals.ToList()) Order(item, visual);
        foreach (var (obj, visual) in _objectVisuals.ToList())
            if (DefinitionOf(obj)?.Type is not (ObjectType.Wall or ObjectType.Door or ObjectType.Window or ObjectType.Roof)) Order(obj, visual);
    }
}
