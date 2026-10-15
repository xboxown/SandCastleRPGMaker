using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Game Test's world physics - stacking, collision, climbing and gravity -
/// modeled on Exult's own source:
///
/// - The world is a grid of 8px world units (Exult's tiles; SpriteSizing.
///   BaseUnit) x lifts. Every thing has a footprint (SizeX x SizeY units)
///   and a height (SizeZ lifts), anchored under its sprite exactly the way
///   ComputeQuantizedPosition draws it - so a footprint is where the thing
///   visibly stands, and can span map tiles.
/// - chunks.cc Chunk_cache::update_object: a thing occupies (blocks) the
///   lifts [Z, Z + SizeZ) over its whole footprint only if it is solid.
///   Non-solid things never block anyone.
/// - chunks.cc Chunk_cache::is_blocked, as used for a walking actor
///   (chunks.h defaults max_drop = 1, max_rise = -1 -> 1 for MOVE_WALK):
///   stepping onto a unit, an actor rises at most 1 lift and drops at most
///   1 lift, and needs headroom for its whole height there. At lift 0 the
///   terrain must be walkable. Every unit of the actor's footprint is
///   checked (Map_chunk::is_blocked over an area).
/// - chunks.cc Map_chunk::gravity: when support is removed, things above
///   fall - actors included ("We DO want NPC's to fall").
/// - drag.cc drop_on_map: a dropped thing can go at most 5 lifts above the
///   avatar (max_lift = avatar lift + 5).
///
/// This project's own item flags keep their established meaning on top of
/// that (by the designer's choice): Allow Elevation = the item rests on top
/// of what is under it and holds up what is above it (one without it lies
/// on the ground and holds nothing up, the "key under a vase"); Is Stackable
/// = other things may overlap it at all.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>chunks.h: Chunk_cache::is_blocked(..., int max_drop = 1, int max_rise = -1), max_rise -1 meaning 1 for a walker.</summary>
    private const int ActorMaxRise = 1;
    private const int ActorMaxDrop = 1;
    /// <summary>drag.cc drop_on_map: "max_lift = ... get_main_actor()->get_lift() + 5".</summary>
    private const int MaxDropAboveActor = 5;

    /// <summary>The player's Exult-style 3D size (footprint units, height lifts) - resolved once at startup from the character's SizeX/SizeY/SizeZ (0 = automatic, see ActorSizing).</summary>
    private (int X, int Y, int Z) _playerSize = ActorSizing.Fallback;

    /// <summary>One thing in the world, as the physics sees it: its footprint (absolute units), its lift range, and the flags that matter.</summary>
    private readonly record struct Body(object Entity, int Left, int Top, int Width, int Depth, int Z, int Height, bool Solid, bool Elevates, bool Stackable)
    {
        public int Right => Left + Width;
        public int Bottom => Top + Depth;
        public int TopLift => Z + Height;
        public bool Overlaps(UnitRect r) => Left < r.Right && r.Left < Right && Top < r.Bottom && r.Top < Bottom;
    }

    /// <summary>A rectangle of world units (8px each).</summary>
    private readonly record struct UnitRect(int Left, int Top, int Width, int Depth)
    {
        public int Right => Left + Width;
        public int Bottom => Top + Depth;
        public bool Overlaps(UnitRect other) => Left < other.Right && other.Left < Right && Top < other.Bottom && other.Top < Bottom;
    }

    private int UnitsPerTileX => Math.Max(1, _tileWidth / SpriteSizing.BaseUnit);
    private int UnitsPerTileY => Math.Max(1, _tileHeight / SpriteSizing.BaseUnit);

    /// <summary>A thing's footprint in absolute world units - the same anchor ComputeQuantizedPosition draws its sprite at: horizontally centered on its tile, bottom edge on the tile's bottom edge, then shifted by its sub-tile offset.</summary>
    private UnitRect FootprintAt(int cellX, int cellY, int offsetX, int offsetY, int sizeX, int sizeY) =>
        new(cellX * UnitsPerTileX + (int)Math.Floor((UnitsPerTileX - sizeX) / 2.0) + offsetX,
            (cellY + 1) * UnitsPerTileY - sizeY + offsetY,
            sizeX, sizeY);

    private Body BodyOf(PlacedItemInstance? item, ObjectInstance? obj)
    {
        var (stackable, elevates, sizeX, sizeY, sizeZ) = ResolveStackInfo(item, obj);
        var (solidOffsetX, solidOffsetY) = ResolveSolidOffset(item, obj);
        int x = item?.X ?? obj!.X, y = item?.Y ?? obj!.Y;
        int offsetX = item?.OffsetX ?? obj!.OffsetX, offsetY = item?.OffsetY ?? obj!.OffsetY;
        var foot = FootprintAt(x, y, offsetX + solidOffsetX, offsetY + solidOffsetY, sizeX, sizeY);
        if (item?.HangsOnWall == true || obj?.HangsOnWall == true)
            return new Body((object?)item ?? obj!, foot.Left, foot.Top, foot.Width, foot.Depth, item?.Z ?? obj!.Z, sizeZ, Solid: false, Elevates: false, Stackable: false); // on the wall: in nobody's way, holds nothing up
        return new Body((object?)item ?? obj!, foot.Left, foot.Top, foot.Width, foot.Depth, item?.Z ?? obj!.Z, sizeZ, IsSolidAt(item, obj), elevates, stackable);
    }

    /// <summary>Hung on the wall in the Map Editor (ObjectInstance / PlacedItemInstance.HangsOnWall).</summary>
    private static bool HangsOnWall(object entity) => entity is ObjectInstance { HangsOnWall: true } or PlacedItemInstance { HangsOnWall: true };

    /// <summary>Where the item's solid box sits relative to its sprite (set by moving the box in the size editor), in 8px units - added on top of where the item itself stands.</summary>
    private (int X, int Y) ResolveSolidOffset(PlacedItemInstance? item, ObjectInstance? obj)
    {
        if (item is { Category: ReferencedItemCategory.Book } && _books.Books.FirstOrDefault(b => b.Id == item.ItemId) is { } book) return (book.SolidOffsetX, book.SolidOffsetY);
        if (item is { Category: ReferencedItemCategory.Container } && _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId) is { } container) return (container.SolidOffsetX, container.SolidOffsetY);
        if (obj is not null && _objects.Objects.FirstOrDefault(o => o.Id == obj.ObjectId) is { } worldObject) return (worldObject.SolidOffsetX, worldObject.SolidOffsetY);
        return (0, 0);
    }

    private List<Body> WorldBodies() =>
        _map.Items.Select(i => BodyOf(i, null))
            .Concat(_map.Objects.Where(o => o.ContainerInstanceId < 0).SelectMany(ObjectBodies)) // stairs: one body a step
            .Concat(CharacterBodies()) // placed characters: solid, like Exult's actors
            .Concat(_floorSlabBodies) // upper floors' tiles: slabs to stand on, ceilings for whoever is below
            .ToList();

    // ================= Item stacking and gravity =================

    /// <summary>
    /// Settles every thing on the map under gravity (Map_chunk::gravity,
    /// applied to the whole map): lowest first, each elevating thing comes
    /// to rest on top of the highest elevating thing already settled under
    /// any part of its footprint (or the ground); a non-elevating thing
    /// lies on the ground. Things only ever fall here, never rise, except
    /// on first load where every authored Z starts at 0 and stacks form in
    /// placement order.
    /// </summary>
    private void SettleAll()
    {
        var bodies = WorldBodies().Where(b => b.Entity is not PlacedCharacter).ToList(); // gravity here is for items and objects - characters stand where they stand
        // Stable by current Z, then list order - so ties (e.g. a freshly loaded map, everything at Z 0) stack in placement order.
        var order = bodies.Select((b, index) => (b, index)).OrderBy(t => t.b.Z).ThenBy(t => t.index).Select(t => t.b).ToList();
        var settled = new List<Body>(order.Count);
        foreach (var body in order)
        {
            if (body.Entity is FloorSlab || (body.Entity is ObjectInstance tile && DefinitionOf(tile)?.Type == ObjectType.Tile)) { settled.Add(body); continue; } // a tile has no gravity: it stays at its height (a floor, a roof)
            if (HangsOnWall(body.Entity)) { settled.Add(body); continue; } // hung on the wall: stays where it was put
            var rect = new UnitRect(body.Left, body.Top, body.Width, body.Depth);
            int floorBase = HomeFloorBase(body);
            var supports = settled.Where(b => BelowCeilingOf(b, body.Z) && !LiesUnder(b.Entity) && !HangsOnWall(b.Entity) // never held up by the floor above (its slab is the ceiling), nor by something put underneath or hung on the wall
                && b.Z >= floorBase - 1).ToList(); // nor by a floor below: on floor 2 only floor 2's slab and what's on floor 2 hold things up
            int z = LiesUnder(body.Entity) ? FloorTop(supports, rect) // put underneath: on the floor itself, under the table
                : body.Elevates ? RestingLift(supports, rect) : SurfaceTop(supports, rect); // lying flat: on the floor - a table top is a floor
            z = Math.Max(z, floorBase); // never below the floor it was put on: a wall plotted on floor 2 stands at floor 2's height
            if (body.Entity is ObjectInstance fallen && z < body.Z) NoteGlassFall(fallen, body.Z, z); // glass that falls far enough shatters
            SetZ(body.Entity, z);
            settled.Add(body with { Z = z });
        }
    }

    /// <summary>The floor each thing is on (from its lift when the game first sees it, and again whenever it's moved).</summary>
    private readonly Dictionary<object, int> _homeFloor = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    /// The lowest lift a thing can come to rest at: the base of its floor.
    /// As in Ultima VII / Exult, something placed on an upper storey stands at
    /// that storey's height (5 lifts a floor, drawn 20px up-left per floor),
    /// on that floor's tiles or on what's on that floor - never down on the
    /// floor below, whatever is or isn't under it. Within a floor, things
    /// still fall onto whatever is left under them.
    /// </summary>
    private int HomeFloorBase(Body body)
    {
        if (!_homeFloor.TryGetValue(body.Entity, out int floor)) _homeFloor[body.Entity] = floor = MapFloors.FloorOfLift(Math.Max(0, body.Z));
        return MapFloors.BaseLift(floor);
    }

    /// <summary>Something was moved to a new lift (put down, knocked, carried): its floor is wherever it is now.</summary>
    private void ForgetHomeFloor(object entity) => _homeFloor.Remove(entity);

    /// <summary>The lift something with this footprint comes to rest at: the top of the highest elevating thing under any part of it.</summary>
    private static int RestingLift(IEnumerable<Body> candidates, UnitRect foot) =>
        candidates.Where(b => b.Elevates && b.Overlaps(foot)).Select(b => b.TopLift).DefaultIfEmpty(0).Max();

    /// <summary>
    /// Surfaces - a table, a counter, a shelf, a solid tile (a floor upstairs):
    /// whatever is put down over one lies on top of it, as in Exult, whatever its
    /// own flags (a key, an apple). Between other things, each one's own
    /// Stackable / elevation rules still decide.
    /// </summary>
    private bool IsSurface(Body body) =>
        body.Entity is FloorSlab || body.Entity is ObjectInstance instance && DefinitionOf(instance) is { } obj
        && (obj.Type == ObjectType.Table || (obj.Type == ObjectType.Tile && obj.IsSolid));

    /// <summary>The floor something lying flat comes to rest on: the top of the highest surface under it, or the ground.</summary>
    private int SurfaceTop(IEnumerable<Body> candidates, UnitRect foot) =>
        candidates.Where(b => b.Overlaps(foot) && IsSurface(b)).Select(b => b.TopLift).DefaultIfEmpty(0).Max();

    /// <summary>The floor itself under this footprint - an upper floor's slab or a solid floor tile, never a table: where something put underneath lies.</summary>
    private int FloorTop(IEnumerable<Body> candidates, UnitRect foot) =>
        candidates.Where(b => b.Overlaps(foot) && (b.Entity is FloorSlab || (b.Entity is ObjectInstance o && DefinitionOf(o) is { Type: ObjectType.Tile, IsSolid: true }))).Select(b => b.TopLift).DefaultIfEmpty(0).Max();

    /// <summary>Put underneath in the Map Editor (ObjectInstance / PlacedItemInstance.LiesUnder).</summary>
    private static bool LiesUnder(object entity) => entity is ObjectInstance { LiesUnder: true } or PlacedItemInstance { LiesUnder: true };

    private static void SetZ(object entity, int z)
    {
        if (entity is PlacedItemInstance item) item.Z = z;
        else if (entity is ObjectInstance obj) obj.Z = z;
    }

    /// <summary>Gravity for the whole world after anything moved: things fall onto what's left under them, and so does the player (Map_chunk::gravity drops NPCs too).</summary>
    private void SettleWorld()
    {
        SettleAll();
        ApplyGravityToPlayer();
        ShatterFallenGlass();
    }

    /// <summary>
    /// The map drop rules (drag.cc drop_on_map / drop_at_lift, with this
    /// project's Stackable flag): where the released thing lands, or why it
    /// can't. Overlapping anything requires both it and the dragged thing to
    /// be stackable; it then rests on the highest elevating thing under its
    /// footprint (or the ground, if it doesn't elevate) - anywhere across
    /// tiles, not just within one. Exult refuses a drop more than 5 lifts
    /// above the avatar, and a solid thing can't be dropped into the space
    /// the avatar itself occupies.
    /// </summary>
    private bool TryResolveMapPlacement(PlacedItemInstance? item, ObjectInstance? obj, int newCellX, int newCellY, double visualLeft, double visualTop, double visualWidth, double visualHeight, out int newOffsetX, out int newOffsetY, out int newZ, out string rejection, out MousePointerKind rejectionPointer)
    {
        rejection = "";
        rejectionPointer = MousePointerKind.RedX; // drop_on_map: nowhere to put it -> red X
        newZ = 0;
        // Sub-tile offset, solved from ComputeQuantizedPosition's own formula, so wherever the visual was released maps back to the nearest 8px world unit.
        var (defaultLeft, defaultTop) = ComputeQuantizedPosition(newCellX, newCellY, visualWidth, visualHeight);
        newOffsetX = (int)Math.Round((visualLeft - defaultLeft) / SpriteSizing.BaseUnit);
        newOffsetY = (int)Math.Round((visualTop - defaultTop) / SpriteSizing.BaseUnit);

        var (stackable, elevates, sizeX, sizeY, sizeZ) = ResolveStackInfo(item, obj);
        var (solidOffsetX, solidOffsetY) = ResolveSolidOffset(item, obj);
        var foot = FootprintAt(newCellX, newCellY, newOffsetX + solidOffsetX, newOffsetY + solidOffsetY, sizeX, sizeY);
        var self = (object?)item ?? obj;
        int ceiling = RoofLiftOverPlayer() ?? int.MaxValue; // under a ceiling, nothing goes up through it (the floor above isn't reachable from here)
        var others = WorldBodies().Where(b => !ReferenceEquals(b.Entity, self) && b.Overlaps(foot) && b.Z < ceiling && !LiesUnder(b.Entity)).ToList(); // something lying under a table is no stack to put things on
        var stackedOn = others.Where(b => !IsSurface(b)).ToList(); // a table or a floor under it isn't a stack: anything can go on them

        if (stackedOn.Count > 0 && (!stackable || stackedOn.Any(b => !b.Stackable)))
        {
            rejection = "Can't stack there - one of the items isn't marked Is Stackable.";
            return false;
        }

        newZ = elevates ? RestingLift(others, foot) : SurfaceTop(others, foot); // lying flat: on the table top (or the ground)
        if (newZ > _playerZ + MaxDropAboveActor)
        {
            rejection = "That's too high to reach from here.";
            rejectionPointer = MousePointerKind.Blocked; // drop_on_map: above max_lift -> "blocked"
            return false;
        }

        if (IsSolidAt(item, obj) && VolumesIntersect(foot, newZ, sizeZ, PlayerFootprint(), _playerZ, _playerSize.Z))
        {
            rejection = "Something is in the way - you're standing there.";
            return false;
        }

        // drag.cc drop_at_lift: "Needs to be reachable, except when it is just lower, so the object can fall down." The spot is the dropped thing's own tile (lower-right of its footprint) at the lift it lands on.
        int spotX = foot.Right - 1, spotY = foot.Bottom - 1;
        if (!IsSpotReachable(spotX, spotY, newZ) && !(_playerZ > newZ && IsSpotReachable(spotX, spotY, _playerZ)))
        {
            rejection = "You can't reach there from here.";
            return false; // red X
        }
        return true;
    }

    private static bool VolumesIntersect(UnitRect a, int aLift, int aHeight, UnitRect b, int bLift, int bHeight) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom
        && aLift < bLift + bHeight && bLift < aLift + aHeight;

    // ================= Actors: collision, climbing, headroom =================

    /// <summary>The player's footprint in world units: centered under the sprite's feet (the bottom-center anchor the sprite is drawn from), snapped to whole units the way Exult actors stand on whole tiles.</summary>
    private UnitRect PlayerFootprintAt(double pixelX, double pixelY)
    {
        double feetX = pixelX;
        double feetY = pixelY + _tileHeight / 2.0;
        int left = (int)Math.Round((feetX - _playerSize.X * SpriteSizing.BaseUnit / 2.0) / SpriteSizing.BaseUnit);
        int top = (int)Math.Round((feetY - _playerSize.Y * SpriteSizing.BaseUnit) / SpriteSizing.BaseUnit);
        return new UnitRect(left, top, _playerSize.X, _playerSize.Y);
    }

    private UnitRect PlayerFootprint() => PlayerFootprintAt(_playerPixelX, _playerPixelY);

    /// <summary>
    /// Exult's walking step (Chunk_cache::is_blocked for each unit of the
    /// new footprint, combined as Map_chunk::is_blocked does for an area):
    /// returns false if the actor can't stand on the new footprint at all,
    /// otherwise true with the lift it ends up at. Each unit decides on its
    /// own: free (with headroom for the whole actor) at the current lift or
    /// 1 above; or, if not, the floor just below the current lift if it's
    /// not more than 1 down. The actor then stands at the highest of those,
    /// and must have headroom there over the whole footprint.
    /// </summary>
    /// <param name="mover">The character taking the step (a PlacedCharacter), or null for the player: the mover never blocks itself, and for a character the player is in the way like any other actor.</param>
    private bool TryStepActor(UnitRect to, int lift, int height, out int newLift, out string blockReason, object? mover = null, List<Body>? world = null)
    {
        newLift = lift;
        blockReason = "";
        if (to.Left < 0 || to.Top < 0 || to.Right > _map.Width * UnitsPerTileX || to.Bottom > _map.Height * UnitsPerTileY)
        {
            blockReason = "Blocked: edge of the map.";
            return false;
        }

        var solids = (world ?? WorldBodies()).Where(b => b.Solid && b.Overlaps(to) && !ReferenceEquals(b.Entity, mover)).ToList();
        if (mover is not null && _player is not null && PlayerBody() is { } playerBody && playerBody.Overlaps(to)) solids.Add(playerBody);
        int highest = int.MinValue;
        for (int y = to.Top; y < to.Bottom; y++)
        {
            for (int x = to.Left; x < to.Right; x++)
            {
                var here = solids.Where(b => x >= b.Left && x < b.Right && y >= b.Top && y < b.Bottom).ToList();
                if (!TryStepUnit(here, lift, height, out int unitLift))
                {
                    blockReason = DescribeBlock(here, lift, height);
                    return false;
                }
                highest = Math.Max(highest, unitLift);
            }
        }
        newLift = highest;
        int standLift = highest;
        // The drop is judged by where the walker will stand (the highest support under its feet), not by every square under
        // them: going down stairs with steps shallower than the feet, the back of the feet is over a step two lifts lower
        // while the walker stands on the one just below - a 1-lift step down, as walking down stairs is.
        if (lift - standLift > ActorMaxDrop)
        {
            blockReason = $"Blocked: a drop of {lift - standLift} lifts - a walker steps down only {ActorMaxDrop} at a time.";
            return false;
        }
        // Standing on the highest support under any part of the footprint, the whole footprint must be clear for the actor's full height.
        if (!IsClear(solids, standLift, height))
        {
            var overhead = solids.Where(b => b.Z < standLift + height && standLift < b.TopLift).OrderBy(b => b.Z).First();
            blockReason = $"Blocked: \"{NameOfEntity(overhead.Entity)}\" is in the way at head height - you need {height * LiftPixelsPerLift}px of headroom.";
            return false;
        }
        if (newLift == 0 && !TerrainWalkableUnder(to))
        {
            blockReason = "Blocked: the ground there isn't walkable.";
            return false;
        }
        return true;
    }

    private const int LiftPixelsPerLift = 4;

    /// <summary>Why one unit of a step was refused - so the player (and the designer testing it) sees the actual Exult rule that stopped them, not just "Blocked".</summary>
    private string DescribeBlock(List<Body> here, int lift, int height)
    {
        int floor = FloorBelow(here, lift);
        bool clearHere = IsClear(here, lift, height);
        if (clearHere && lift - floor > ActorMaxDrop)
            return $"Blocked: that's a drop of {(lift - floor) * LiftPixelsPerLift}px - you can only step down 4px (1 lift) at a time.";
        var riser = here.Where(b => b.TopLift > lift + ActorMaxRise && b.Z <= lift + ActorMaxRise).OrderByDescending(b => b.TopLift).FirstOrDefault();
        if (riser.Entity is not null)
            return $"Blocked: \"{NameOfEntity(riser.Entity)}\" rises {(riser.TopLift - lift) * LiftPixelsPerLift}px above where you stand (lift {lift}) - you can only step up 4px (1 lift) at a time.";
        var overhead = here.Where(b => b.Z < lift + ActorMaxRise + height && lift < b.TopLift).OrderBy(b => b.Z).FirstOrDefault();
        if (overhead.Entity is not null)
            return $"Blocked: \"{NameOfEntity(overhead.Entity)}\" is in the way at head height - you need {height * LiftPixelsPerLift}px of headroom.";
        return "Blocked.";
    }

    private string NameOfEntity(object entity) => entity switch
    {
        PlacedItemInstance { Category: ReferencedItemCategory.Book } item => _books.Books.FirstOrDefault(b => b.Id == item.ItemId) is { } book ? EffectiveBookName(book) : "book",
        PlacedItemInstance { Category: ReferencedItemCategory.Container } item => _containers.Containers.FirstOrDefault(c => c.Id == item.ItemId)?.Name ?? "container",
        PlacedItemInstance item => item.Category.ToString(),
        ObjectInstance obj => _objects.Objects.FirstOrDefault(o => o.Id == obj.ObjectId)?.Name ?? "object",
        PlacedCharacter placed => _mapCharacters.FirstOrDefault(mc => mc.Placement == placed)?.Character.Name ?? "someone",
        CharacterDefinition character => character.Name,
        FloorSlab => "the floor above",
        _ => "something"
    };

    /// <summary>Chunk_cache::is_blocked for one unit, for a walker (max_rise 1, max_drop 1, not levitating). here = the solid things covering this unit.</summary>
    private static bool TryStepUnit(List<Body> here, int lift, int height, out int newLift)
    {
        int maxLift = lift + ActorMaxRise;
        for (newLift = lift; newLift <= maxLift; newLift++)
        {
            if (IsClear(here, newLift, height)) break; // not blocked, and nothing lower than our head above
        }
        if (newLift > maxLift)
        {
            // Spot not found at lift or higher - look downwards.
            newLift = FloorBelow(here, lift);
            if (newLift >= lift) return false; // couldn't drop
            if (!IsClear(here, newLift, height)) return false; // still blocked above
        }
        if (newLift <= lift)
        {
            // Not going up - see if falling. (How far is checked for the whole footprint, in TryStepActor: by where it will stand.)
            newLift = FloorBelow(here, lift);
            if (!IsClear(here, newLift, height)) return false;
        }
        return true;
    }

    /// <summary>True if nothing solid occupies any lift in [lift, lift + height).</summary>
    private static bool IsClear(IEnumerable<Body> solids, int lift, int height) =>
        !solids.Any(b => b.Z < lift + height && lift < b.TopLift);

    /// <summary>"highest blocked below lift" + 1: the top of the highest solid thing that starts below this lift (capped at the lift itself), or 0 - the ground.</summary>
    private static int FloorBelow(IEnumerable<Body> solids, int lift) =>
        solids.Where(b => b.Z < lift).Select(b => Math.Min(b.TopLift, lift)).DefaultIfEmpty(0).Max();

    /// <summary>Exult only consults the terrain itself at lift 0 ("Can only walk and terrain is solid (and 0-height)" -> blocked).</summary>
    private bool TerrainWalkableUnder(UnitRect foot)
    {
        for (int y = foot.Top; y < foot.Bottom; y++)
        {
            for (int x = foot.Left; x < foot.Right; x++)
            {
                int tileIndex = _map.Layers[0].GetTile(x / UnitsPerTileX, y / UnitsPerTileY, _map.Width);
                if (tileIndex >= 0 && !TilePropertiesOf(tileIndex).Walkable) return false;
            }
        }
        return true;
    }

    /// <summary>Where the player stands when placed somewhere directly (startup): on top of whatever solid thing is under their footprint, or the ground.</summary>
    /// <summary>The lift something would stand at on that footprint: on top of whatever solid is under it. <paramref name="self"/> (a character's own placement) is left out, or it would stand on top of itself.</summary>
    private int StandingLiftAt(UnitRect foot, object? self = null) =>
        FloorBelow(WorldBodies().Where(b => b.Solid && b.Overlaps(foot) && !ReferenceEquals(b.Entity, self) && b.Entity is not PlacedCharacter), int.MaxValue); // nobody stands on another character's head

    // ================= Draw order (objs.cc Game_object::compare) =================

    /// <summary>
    /// Exult's paint-order rule for things that overlap on the floor (their
    /// footprints overlap in X and Y): the one whose bottom is at or above
    /// the other's top is "greater in Z" and painted later - so whatever
    /// stands on something is always drawn on top of it. The position-based
    /// order (ComputeDepthZIndex) alone can't guarantee that: something
    /// resting on a chest but a little further back would be drawn behind
    /// the chest and look sunk inside it. Applied lowest first, so a whole
    /// pile orders correctly.
    /// </summary>
    private void ApplyRestingDrawOrder()
    {
        var bodies = WorldBodies().OrderBy(b => b.Z).ToList();
        var order = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
        foreach (var body in bodies)
        {
            if (VisualOf(body.Entity) is not { } visual) continue;
            // A floor tile (type Tile) is a flat: Exult paints flats first, under everything standing on its floor,
            // so the tile a row in front never covers the feet of a rat standing on the floor behind it.
            int zIndex = body.Entity is ObjectInstance tile && DefinitionOf(tile)?.Type == ObjectType.Tile
                ? FloorBand(body.Z) + 1 + body.Z % MapFloors.LiftsPerFloor
                : Panel.GetZIndex(visual);
            var foot = new UnitRect(body.Left, body.Top, body.Width, body.Depth);
            foreach (var below in bodies)
            {
                if (ReferenceEquals(below.Entity, body.Entity) || below.TopLift > body.Z || !below.Overlaps(foot)) continue;
                if (order.TryGetValue(below.Entity, out int belowIndex)) zIndex = Math.Max(zIndex, belowIndex + 1);
            }
            order[body.Entity] = zIndex;
            Panel.SetZIndex(visual, zIndex);
        }
        // Put underneath: drawn below everything over it (the table it lies under, and what's on it).
        foreach (var body in bodies.Where(b => LiesUnder(b.Entity)))
        {
            if (VisualOf(body.Entity) is not { } visual) continue;
            var foot = new UnitRect(body.Left, body.Top, body.Width, body.Depth);
            int zIndex = Panel.GetZIndex(visual);
            foreach (var over in bodies)
                if (!ReferenceEquals(over.Entity, body.Entity) && !LiesUnder(over.Entity) && over.Overlaps(foot) && VisualOf(over.Entity) is { } overVisual)
                    zIndex = Math.Min(zIndex, Panel.GetZIndex(overVisual) - 1);
            Panel.SetZIndex(visual, zIndex);
        }
    }

    /// <summary>The player's paint order: their usual position-based order, but always after anything they are standing on (the same Exult rule as ApplyRestingDrawOrder).</summary>
    private int PlayerZIndex()
    {
        int zIndex = ComputeDepthZIndex(_playerPixelX / _tileWidth, _playerPixelY / _tileHeight, (int)Math.Round(_playerVisualLiftZ));
        if (_player is not null && SeatedZIndex(_player, zIndex) is int seated) return seated; // on a chair or in bed: over it, under its cover
        if (_playerZ <= 0) return PlayerOrderAmongWalls(zIndex); // on the ground - nothing under them to be drawn after; walls by Exult's compare
        var foot = PlayerFootprint();
        foreach (var body in WorldBodies())
        {
            if (body.TopLift > _playerZ || !body.Overlaps(foot)) continue;
            if (VisualOf(body.Entity) is { } visual) zIndex = Math.Max(zIndex, Panel.GetZIndex(visual) + 1);
        }
        return PlayerOrderAmongWalls(zIndex);
    }

    private UIElement? VisualOf(object entity) => entity switch
    {
        PlacedItemInstance item => _itemVisuals.GetValueOrDefault(item),
        ObjectInstance obj => _objectVisuals.GetValueOrDefault(obj),
        PlacedCharacter placed => _mapCharacters.FirstOrDefault(mc => mc.Placement == placed)?.Visual,
        _ => null
    };

    /// <summary>Map_chunk::gravity for the player: if what they stood on is gone, they fall onto whatever is left beneath them (any distance - this is falling, not stepping).</summary>
    private void ApplyGravityToPlayer()
    {
        var foot = PlayerFootprint();
        int floor = FloorBelow(WorldBodies().Where(b => b.Solid && b.Overlaps(foot)), _playerZ);
        if (floor >= _playerZ) return;
        _playerZ = floor;
        _playerVisualLiftZ = _playerZ;
        SnapPlayerPosition();
    }
}
