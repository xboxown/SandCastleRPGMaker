namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Common properties shared by every "leaf item" editor (Book, Door, Trap,
/// Item, Equipment, Stair). Each of those editors' own model class inherits
/// this instead of repeating these fields. House/NPC/Skill/etc. do NOT
/// inherit this - they have their own, different shared surfaces.
/// </summary>
public abstract class PlaceableItemBase
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Item";
    /// <summary>Worn on a Serpent Isle-style paperdoll (a backpack on the back): its picture on the body.</summary>
    public WornLookSettings Worn { get; set; } = new();

    /// <summary>WorldObject/NPC id that "owns" this item, or -1 for unowned/free.</summary>
    public int BelongToId { get; set; } = -1;

    public float Weight { get; set; } = 0f;
    public float Volume { get; set; } = 0f;
    public int Price { get; set; } = 0;

    public bool Destructible { get; set; } = false;
    /// <summary>Resource id of the sound played when this is destroyed.</summary>
    public int DestructibleSoundResourceId { get; set; } = -1;

    public bool CanBeSold { get; set; } = true;
    public bool CanBeTraded { get; set; } = true;
    public bool CanBeDropped { get; set; } = true;
    public bool CanBePicked { get; set; } = true;

    /// <summary>Sprite Editor group name this item's normal sprite is drawn from.</summary>
    public string SpriteGroup { get; set; } = string.Empty;
    public string SpriteName { get; set; } = string.Empty;

    /// <summary>Sprite shown while this item is being destroyed (its break/shatter animation).</summary>
    public string ChatteredSpriteGroup { get; set; } = string.Empty;
    public string ChatteredSpriteName { get; set; } = string.Empty;

    /// <summary>Sprite shown after this item has been destroyed (its rubble/remains state).</summary>
    public string DestroyedSpriteGroup { get; set; } = string.Empty;
    public string DestroyedSpriteName { get; set; } = string.Empty;

    /// <summary>Faction id this item belongs to (yes, even inanimate objects have one - lets faction rules affect the environment, not just NPCs).</summary>
    public int FactionId { get; set; } = -1;

    /// <summary>Can another item share this same map cell at all - a key can sit under a vase, one book can sit on another. Applies to every placeable type, not just Books. The actual multi-item-per-cell rendering/placement behavior is engine-side only (Game Test) - the Map Editor and every other preview keep showing one item per cell.</summary>
    public bool IsStackable { get; set; } = false;

    /// <summary>Only meaningful when IsStackable is also true: does stacking raise this item's visual height (Z) above what is underneath it? A key under a vase is Stackable but NOT elevated - both sit at the same visual height. Books stacked on books ARE elevated - each one added sits visibly higher than the one below it. Engine-side only, same as IsStackable.</summary>
    public bool AllowElevation { get; set; } = false;

    /// <summary>
    /// The object's real, LOGICAL footprint and height in world units (8px
    /// each, matching SpriteSizing.BaseUnit) - per spec, this is NEVER
    /// derived from the sprite's own rendered pixel dimensions. A tiny
    /// sprite and a huge sprite can share the same real SizeX/Y/Z, and a
    /// visually huge sprite can have a small real size, if that's what was
    /// authored - the sprite is only ever what gets drawn; these are what
    /// the object actually occupies and rests on in the world. SizeZ
    /// specifically is what a stacked item on top actually rests on (its
    /// resting Z = the supporting object's own Z + that object's SizeZ),
    /// not a flat "+1 per item" - so a tall object correctly holds
    /// whatever's on it higher than a short one would.
    /// </summary>
    public int SizeX { get; set; } = 1;
    public int SizeY { get; set; } = 1;
    public int SizeZ { get; set; } = 1;

    /// <summary>Where the solid box sits relative to the sprite, in 8px world units (Exult tiles): 0,0 = centered under the sprite's bottom edge (the default anchor). Set by dragging the box in the size editor, or by Automated, so the box can line up with where the art actually stands.</summary>
    public int SolidOffsetX { get; set; }
    public int SolidOffsetY { get; set; }

    /// <summary>Real line-of-sight blocking, confirmed directly from Exult's own occlude.dat - see WorldObject's own Occludes for the full rationale. Off by default for most items (a book doesn't block sight), but available for anything authored tall/wide enough to matter.</summary>
    public bool Occludes { get; set; } = false;

    /// <summary>Real collision, confirmed directly from Exult's own shape data as a genuinely separate flag from Occludes (is_solid(), distinct from occludes_flag) - see WorldObject's own IsSolid for the full rationale. Off by default; most books/items can be walked over, but a large crate or similar could be marked solid.</summary>
    public bool IsSolid { get; set; } = false;

    /// <summary>Real two-handed requirement, confirmed directly from Exult's own ChangeLog and mechanics - see WorldObject's own IsTwoHanded for the full rationale. A two-handed weapon or a ranged weapon that needs a free hand to reload both count as this.</summary>
    public bool IsTwoHanded { get; set; } = false;
}
