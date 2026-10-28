// Ultima 8 engine (U8GameWindow) - copied from the Ultima 7 engine (GameTestWindow, tag u7-final-b471) and changed for Ultima 8 here only.
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Paint order between the tiles of an upper floor and the things on the
/// floors below. Everything on floor N is drawn over the floors below it -
/// but an upper floor's tiles (painted slabs, landing tiles, solid or Floor
/// tile objects up there) are only a thin slab: something on a lower floor
/// standing wholly in front of them (south or east of them, not under them)
/// is nearer the viewer and is drawn over them, as Exult's compare does
/// (objs.cc Game_object::compare: wholly nearer in y or x, not farther in
/// the other). What is under them or behind them stays covered - the room
/// below a floor, the tree behind a house.
///
/// Each floor's band holds its slabs in a slot of their own just under the
/// band (SlabZIndex), and a lower-floor thing in front of them is lifted
/// into that slot, over the slabs and under everything standing on that
/// floor, keeping its own order among the other lifted things.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>The slot under each floor's band: its slabs at the bottom, then lower-floor things drawn in front of them.</summary>
    private const int SlabRoom = 5_000_000;

    /// <summary>An upper floor's slab (its tiles): over everything on the floors below, under everything on its own floor.</summary>
    private static int SlabZIndex(int floor, int slabLift) =>
        (Math.Max(2, floor) - 1) * FloorZIndexBand - SlabRoom + Math.Clamp(slabLift % MapFloors.LiftsPerFloor, 0, MapFloors.LiftsPerFloor - 1);

    /// <summary>An upper floor's tile area: its floor, its tile box (8px units), where it is drawn.</summary>
    private readonly record struct UpperSlab(int Floor, int XLeft, int XRight, int YFar, int YNear, Rect Screen);

    private List<UpperSlab>? _upperSlabs;

    /// <summary>A Tile object that is part of an upper floor: solid or Floor, lying on a floor's own height above the ground.</summary>
    private bool IsUpperFloorTile(ObjectInstance instance) =>
        instance.Z >= MapFloors.BaseLift(2) && instance.Z == MapFloors.BaseLift(MapFloors.FloorOfLift(instance.Z))
        && DefinitionOf(instance) is { Type: ObjectType.Tile } tile && (tile.IsSolid || tile.IsFloorTile);

    private List<UpperSlab> UpperSlabs() => _upperSlabs ??= BuildUpperSlabs();

    private List<UpperSlab> BuildUpperSlabs()
    {
        var slabs = new List<UpperSlab>();
        void Add(int floor, int left, int top, int width, int depth, int liftShift)
        {
            if (floor < 2) return;
            double px = SpriteSizing.BaseUnit, shift = liftShift * LiftPixelShift;
            slabs.Add(new UpperSlab(floor, left, left + width - 1, top, top + depth - 1, new Rect(left * px - shift, top * px - shift, width * px, depth * px)));
        }
        // Painted upper-floor tiles (one slab per run along a row), drawn at their floor's height.
        foreach (var body in _floorSlabBodies) Add(MapFloors.FloorOfLift(body.Z + 1), body.Left, body.Top, body.Width, body.Depth, body.Z + 1);
        foreach (var instance in _map.Objects.Where(o => o.ContainerInstanceId < 0))
        {
            if (IsUpperFloorTile(instance))
            {
                var body = BodyOf(null, instance);
                Add(MapFloors.FloorOfLift(instance.Z), body.Left, body.Top, body.Width, body.Depth, instance.Z);
            }
            else if (DefinitionOf(instance) is { } obj && StairLook.PoseOf(obj, _objects, instance.Facing) is { } pose)
            {
                // A staircase's landing tiles, as ObjectBodies places them.
                int anchorX = instance.X * UnitsPerTileX + instance.OffsetX, anchorY = instance.Y * UnitsPerTileY + instance.OffsetY;
                foreach (var landing in pose.Steps.Where(s => s.IsLanding))
                    Add(StairLook.LandingFloor(landing, instance.Z), anchorX + landing.FootX, anchorY + landing.FootY, Math.Max(1, landing.SizeX), Math.Max(1, landing.SizeY), instance.Z + landing.TopLift);
            }
        }
        return slabs;
    }

    /// <summary>Back to its own floor's order, if an earlier pass lifted it into a slab slot.</summary>
    private static int Unlifted(int zIndex, int lift)
    {
        int band = FloorBand(lift);
        if (zIndex - band < FloorZIndexBand - SlabRoom) return zIndex;
        int t = zIndex + SlabRoom - 10;
        return band + ((t % FloorZIndexBand) + FloorZIndexBand) % FloorZIndexBand;
    }

    /// <summary>
    /// The paint order for something on a lower floor: lifted over an upper
    /// floor's tiles it overlaps on screen and stands wholly in front of; left
    /// as it is if it's under or behind any of them.
    /// </summary>
    private int OverUpperFloorsInFront(UnitRect foot, int lift, Rect screen, int zIndex)
    {
        zIndex = Unlifted(zIndex, lift);
        if (screen.IsEmpty) return zIndex;
        int own = MapFloors.FloorOfLift(Math.Max(0, lift)), liftTo = 0;
        int xLeft = foot.Left, xRight = foot.Right - 1, yFar = foot.Top, yNear = foot.Bottom - 1;
        foreach (var slab in UpperSlabs())
        {
            if (slab.Floor <= own || !slab.Screen.IntersectsWith(screen)) continue;
            bool south = yFar > slab.YNear, east = xLeft > slab.XRight, north = yNear < slab.YFar, west = xRight < slab.XLeft;
            if ((south || east) && !north && !west) liftTo = Math.Max(liftTo, slab.Floor); // wholly nearer: in front of it
            else if (!(south && west) && !(north && east)) return zIndex; // under it or behind it: the floor above covers it
        }
        return liftTo == 0 ? zIndex : SlabZIndex(liftTo, 0) + 10 + (zIndex - FloorBand(lift));
    }

    private int OverUpperFloorsInFront(object entity, UIElement visual)
    {
        var body = entity is PlacedItemInstance item ? BodyOf(item, null) : BodyOf(null, (ObjectInstance)entity);
        return OverUpperFloorsInFront(new UnitRect(body.Left, body.Top, body.Width, body.Depth), body.Z, ScreenRectOf(visual), Panel.GetZIndex(visual));
    }
}
