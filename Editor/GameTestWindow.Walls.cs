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
public partial class GameTestWindow
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

    /// <summary>A paint order for something moving among walls: after every wall Exult says it's in front of, before every wall it's behind.</summary>
    private int OrderAmongWalls(UnitRect foot, int lift, int height, Rect screen, int zIndex)
    {
        var me = BoxOf(foot, lift, height, screen);
        int lowest = int.MaxValue;
        foreach (var (_, box, visual, occludes) in WallBoxes())
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
        OrderAmongWalls(PlayerFootprint(), _playerZ, _playerSize.Z, ScreenRectOf(_playerImage), zIndex);

    /// <summary>A placed character's paint order, walls taken into account.</summary>
    private void OrderCharacterAmongWalls(MapCharacter mapCharacter)
    {
        if (mapCharacter.Visual is not { } visual) return;
        Panel.SetZIndex(visual, OrderAmongWalls(CharacterFootprint(mapCharacter), mapCharacter.Lift, mapCharacter.Size.Z, ScreenRectOf(visual), Panel.GetZIndex(visual)));
    }

    /// <summary>Items and objects lying near walls (after every stacking refresh).</summary>
    private void ApplyWallDrawOrder()
    {
        var walls = WallBoxes().ToList();
        if (walls.Count == 0) return;
        void Order(object entity, UIElement visual)
        {
            var body = entity is PlacedItemInstance item ? BodyOf(item, null) : BodyOf(null, (ObjectInstance)entity);
            var foot = new UnitRect(body.Left, body.Top, body.Width, body.Depth);
            Panel.SetZIndex(visual, OrderAmongWalls(foot, body.Z, body.Height, ScreenRectOf(visual), Panel.GetZIndex(visual)));
        }
        foreach (var (item, visual) in _itemVisuals.ToList()) Order(item, visual);
        foreach (var (obj, visual) in _objectVisuals.ToList())
            if (DefinitionOf(obj)?.Type is not (ObjectType.Wall or ObjectType.Door or ObjectType.Window or ObjectType.Roof)) Order(obj, visual);
    }
}
