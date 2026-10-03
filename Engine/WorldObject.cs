namespace SandcastleRpgMaker.Engine;

/// <summary>
/// A generic interactive object definition (a "type", not a placed instance).
/// This is the Ultima-7-style building block: nearly everything in the world
/// - items, food, containers, NPCs, furniture - is one of these, described by
/// behavior flags rather than being a hardcoded special case or a scripted
/// map tile. There is no fixed catalog size; ObjectDatabase holds however
/// many definitions you create.
/// </summary>
public class WorldObject
{
    /// <summary>Stable unique ID. Referenced by ObjectInstance and, later, by events/dialogue.</summary>
    public int Id { get; set; }

    public string Name { get; set; } = "New Object";
    public string Description { get; set; } = string.Empty;

    /// <summary>Path to this object's icon/sprite image.</summary>
    public string IconPath { get; set; } = string.Empty;

    /// <summary>Can this object be picked up and carried?</summary>
    public bool IsPortable { get; set; } = true;

    /// <summary>
    /// Real, confirmed fix, per spec - closes the honest gap flagged
    /// earlier ("WorldObject has no real Volume field yet"). Confirmed
    /// directly against real Exult forum discussion of its own actual
    /// container mechanics - a real per-item Volume number is exactly
    /// what real Exult sums against a container's own capacity to decide
    /// what fits, the same real concept Book/Container already have via
    /// PlaceableItemBase's own Volume. Without this, a WorldObject always
    /// counted as taking up zero space in a container's own Starting
    /// Contents, so any number of them could be added there with no real
    /// limit ever applying at all - the real, concrete cause of items
    /// piling up far past a container's own stated Max Volume before the
    /// check ever caught anything. Same real default (0) as
    /// PlaceableItemBase's own Volume, for the same real reason - a
    /// newly-created one shouldn't silently claim space the designer
    /// never actually gave it.
    /// </summary>
    public double Volume { get; set; } = 0;

    /// <summary>Can multiple copies combine into one inventory slot (e.g. gold, arrows, food)?</summary>
    public bool IsStackable { get; set; } = false;

    /// <summary>Max stack size when IsStackable is true. 0 = unlimited.</summary>
    public int MaxStackSize { get; set; } = 0;

    /// <summary>Can this object hold other objects (bags, chests, barrels)?</summary>
    public bool IsContainer { get; set; } = false;

    /// <summary>Does clicking/using this object trigger interaction (open, talk, read, eat...)?</summary>
    public bool IsInteractable { get; set; } = true;

    /// <summary>Is this object a living NPC (has schedule/dialogue) rather than an inert item?</summary>
    public bool IsNpc { get; set; } = false;

    /// <summary>
    /// Can another object/item share this one's map cell at all - a key
    /// under a vase, a book on a book. Deliberately named differently from
    /// the existing IsStackable above (which is about inventory-slot
    /// combining, a completely different concept) to avoid the two silently
    /// colliding under one name. Engine-side only, same as PlaceableItemBase's
    /// own IsStackable/AllowElevation (Books, Weapons, Armor, Items) - the
    /// Map Editor and every other preview keep showing one object per cell.
    /// </summary>
    public bool IsCellStackable { get; set; } = false;

    /// <summary>Only meaningful when IsCellStackable is also true: does stacking raise this object's visual height (Z) above what is underneath it? A key under a vase is cell-stackable but NOT elevated. Objects piled on objects (crates, say) ARE elevated - each one added sits visibly higher than the one below it.</summary>
    public bool AllowCellElevation { get; set; } = false;

    /// <summary>Real, logical footprint and height in world units (8px each) - same principle as PlaceableItemBase's own SizeX/Y/Z, never derived from IconPath's own rendered pixel dimensions. SizeZ is what something resting on top of this object actually rests on top of.</summary>
    public int SizeX { get; set; } = 1;
    public int SizeY { get; set; } = 1;
    public int SizeZ { get; set; } = 1;

    /// <summary>Where the solid box sits relative to the sprite, in 8px world units (Exult tiles): 0,0 = centered under the sprite's bottom edge (the default anchor). Set by dragging the box in the size editor, or by Automated, so the box can line up with where the art actually stands.</summary>
    public int SolidOffsetX { get; set; }
    public int SolidOffsetY { get; set; }

    /// <summary>Real line-of-sight blocking, confirmed directly from Exult's own occlude.dat: a per-shape authored flag (walls, tall furniture, closed doors) that blocks visibility THROUGH it, independent of walkability - a locked gate can occlude sight without blocking movement, and vice versa.</summary>
    public bool Occludes { get; set; } = false;

    /// <summary>Real collision, confirmed directly from Exult's own shape data as a genuinely separate flag from Occludes (is_solid(), distinct from occludes_flag) - blocks an actor from walking ONTO this object's tile at all, independent of whether it blocks sight. A locked gate can occlude sight without being solid, and a low hedge can be solid (blocking movement) without occluding sight.</summary>
    public bool IsSolid { get; set; } = false;

    /// <summary>Real two-handed requirement, confirmed directly from Exult's own real ChangeLog ("make sure non-party members equip a shield if they have one and aren't using a two handed weapon") plus forum-documented ranged-weapon behavior (a sling needs a free hand to reload, so it's two-handed too, even though it isn't melee). When equipped in RightHand, this makes LeftHand unavailable while it's worn - see PaperdollWindow, not a separate "both hands" slot pretending to be two slots.</summary>
    public bool IsTwoHanded { get; set; } = false;

    /// <summary>What double-clicking it does (Exult's key / lockpick usecode): a Key or Lockpick asks for a target, then works on the container clicked.</summary>
    public ObjectUse UseAs { get; set; } = ObjectUse.Nothing;
}

/// <summary>What double-clicking an object does in play.</summary>
public enum ObjectUse
{
    Nothing,
    /// <summary>Locks or unlocks a container whose Required Key it is.</summary>
    Key,
    /// <summary>Tries to pick a locked container's lock; breaks on a failure.</summary>
    Lockpick
}

/// <summary>
/// A placed occurrence of a WorldObject on the map (or inside a container).
/// Keeping the definition (WorldObject) separate from the placement
/// (ObjectInstance) is what lets one "torch" or "goblin" definition be
/// placed hundreds of times without duplicating its data.
/// </summary>
public class ObjectInstance
{
    public int ObjectId { get; set; }

    /// <summary>Map tile position. Ignored if ContainerInstanceId is set (i.e. it's inside something).</summary>
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>How many (for stackable objects). 1 for non-stackable.</summary>
    public int Quantity { get; set; } = 1;

    /// <summary>If this instance is sitting inside a container instance, this holds that instance's InstanceId. -1 = on the map, not in a container.</summary>
    public int ContainerInstanceId { get; set; } = -1;

    /// <summary>Unique id for this specific placed instance, so containers can reference their contents.</summary>
    public int InstanceId { get; set; }

    /// <summary>
    /// Real, logical elevation/lift level - Ultima 7-style, per spec: NOT
    /// derived from sprite pixel height, NOT recomputed fresh every
    /// render from list order. Genuine, persistent world state - set once
    /// on placement/drop based on what it's landing on and whether it
    /// allows elevation, and only ever changed again by a real settle
    /// pass when whatever was supporting it is removed. 0 is ground level.
    /// </summary>
    public int Z { get; set; } = 0;

    /// <summary>Sub-tile placement, in 8px world units, purely additive on top of X/Y - see PlacedItemInstance's own OffsetX/Y for the full rationale (never a reinterpretation of X/Y, so existing saved maps are unaffected).</summary>
    public int OffsetX { get; set; } = 0;
    public int OffsetY { get; set; } = 0;
}

/// <summary>
/// The full catalog of object definitions for a project. Dynamic list, no
/// cap on how many object types you can define.
/// </summary>
public class ObjectDatabase
{
    public List<WorldObject> Objects { get; set; } = new();

    private int _nextId = 1;

    public WorldObject CreateNew()
    {
        var obj = new WorldObject { Id = _nextId++, Name = $"Object {_nextId - 1}" };
        Objects.Add(obj);
        return obj;
    }

    /// <summary>Recomputes _nextId after a Load, so newly created objects never collide with loaded ones.</summary>
    public void SyncNextIdAfterLoad()
    {
        _nextId = Objects.Count == 0 ? 1 : Objects.Max(o => o.Id) + 1;
    }
}
