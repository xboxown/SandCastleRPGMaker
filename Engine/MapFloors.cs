namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Floors (storeys) of a map, 1 to 9. Ultima VII builds a storey every 5
/// lifts (20px): floor N stands at lift 5 x (N - 1), so floor 9 is lift 40 -
/// well within Exult's own range (chunks.cc is_roof looks up to lift 255).
/// Ultima VII's own maps only reach 3 storeys; the extra floors are ours.
///
/// An upper floor's tiles (its MapLayer) are solid slabs one lift thick, the
/// lift just under the floor (lift 4 for floor 2): whoever is on floor 2
/// stands on lift 5, and for whoever is below, the slab is the ceiling
/// (Exult's is_roof: anything solid 4 or more lifts over their feet).
/// </summary>
public static class MapFloors
{
    public const int MaxFloors = 9;
    public const int LiftsPerFloor = 5;

    /// <summary>The lift people stand at on this floor (floor 1 = 0, the ground).</summary>
    public static int BaseLift(int floor) => (Math.Clamp(floor, 1, MaxFloors) - 1) * LiftsPerFloor;

    /// <summary>The lift of this floor's tile slab (one lift thick, its top is the floor): floor 2's is lift 4.</summary>
    public static int SlabLift(int floor) => BaseLift(floor) - 1;

    /// <summary>The floor something at this lift belongs to: lifts 0-4 are floor 1, 5-9 floor 2...</summary>
    public static int FloorOfLift(int lift) => Math.Clamp(lift / LiftsPerFloor + 1, 1, MaxFloors);
}
