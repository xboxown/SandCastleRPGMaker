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

    /// <summary>How heavy one of it is (Exult stones). Counts toward what a party member can carry: 2 x Strength.</summary>
    public double Weight { get; set; } = 0;

    /// <summary>Older projects' Key/Lockpick setting - now part of Type (kept so they load; see ObjectDatabase.Upgrade).</summary>
    public ObjectUse UseAs { get; set; } = ObjectUse.Nothing;

    // ---------- Object Editor ----------

    /// <summary>What it is - chosen once in the Object Editor, then fixed (ObjectTypes.cs).</summary>
    public ObjectType Type { get; set; } = ObjectType.Junk;
    /// <summary>False only for a brand-new object whose type hasn't been picked yet.</summary>
    public bool TypeChosen { get; set; } = true;

    /// <summary>Its look, from the Sprite Editor (for a door: closed; for a light: unlit; for ammo/money: a single one). Older objects without one show IconPath.</summary>
    public string SpriteGroup { get; set; } = string.Empty;
    public string SpriteName { get; set; } = string.Empty;

    public int Price { get; set; }
    /// <summary>The character it belongs to (-1 = nobody): they and their faction react to its theft or destruction.</summary>
    public int BelongToId { get; set; } = -1;
    public int FactionId { get; set; } = -1;

    /// <summary>Exult object hit points (0 = can't be destroyed, like most of Exult's objects).</summary>
    public int HitPoints { get; set; }
    /// <summary>Each blow is reduced by 1 to this much (not in Exult).</summary>
    public int Defense { get; set; }
    /// <summary>A steel door, an iron grate...: blows never hurt it, whatever its hit points.</summary>
    public bool CannotBeDestroyed { get; set; }
    public int DestroyedSoundResourceId { get; set; } = -1;

    /// <summary>Junk only: its frames are states, not an animation (a bed cover: frame 1 made, frame 2 turned down).</summary>
    public bool FramesAreStates { get; set; }

    public DoorSettings Door { get; set; } = new();
    public LightSettings Light { get; set; } = new();
    public BundleSettings Bundle { get; set; } = new();
    public AmmoSettings Ammo { get; set; } = new();
    public MoneySettings Money { get; set; } = new();
    public FurnitureSettings Furniture { get; set; } = new();
    public TrapSettings Trap { get; set; } = new();
    public WindowSettings Window { get; set; } = new();
    public PlantSettings Plant { get; set; } = new();
    public KeychainSettings Keychain { get; set; } = new();
    public SextantSettings Sextant { get; set; } = new();
    public MapSettings MapPicture { get; set; } = new();
    public FoodSettings Food { get; set; } = new();
    public PotionSettings Potion { get; set; } = new();
    public GlassSettings Glass { get; set; } = new();
    public SpellbookSettings Spellbook { get; set; } = new();
    public UseableSettings Useable { get; set; } = new();
    public LeverSettings Lever { get; set; } = new();
    public StairsSettings Stairs { get; set; } = new();
    public RoofSettings Roof { get; set; } = new();
    public ClockSettings Clock { get; set; } = new();
    public ToolSettings Tool { get; set; } = new();
    public CraftingSettings Crafting { get; set; } = new();
    public InstrumentSettings Instrument { get; set; } = new();
    public ExplosiveSettings Explosive { get; set; } = new();
    public BedrollSettings Bedroll { get; set; } = new();
    public LiquidContainerSettings Liquid { get; set; } = new();

    /// <summary>Any object can be a liquid source (Exult's well, water trough): a liquid container used on it is filled with this liquid. Empty = not a source.</summary>
    public string LiquidSource { get; set; } = string.Empty;

    /// <summary>Its events (Object Editor > Event), as books and containers have.</summary>
    public List<BookEventPage> EventPages { get; set; } = new();
    public List<VariableDefinition> LocalVariables { get; set; } = new();
    private int _nextLocalVariableId = 1;
    public int GetNextLocalVariableId() => _nextLocalVariableId++;
    public void SyncLocalVariableIdAfterLoad() => _nextLocalVariableId = LocalVariables.Count == 0 ? 1 : LocalVariables.Max(v => v.Id) + 1;

    /// <summary>The name it goes by in play: a burnt-out light is "Spent ..." (as Ultima VII's "spent torch").</summary>
    public string DisplayName(bool spent = false) => !spent ? Name : Type switch
    {
        ObjectType.LightSource => $"Spent {Name.ToLowerInvariant()}",
        ObjectType.Window => $"Broken {Name.ToLowerInvariant()}",
        ObjectType.UseableObject => $"Used {Name.ToLowerInvariant()}",
        _ => Name
    };
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

    /// <summary>Which way it faces (chairs, beds) - set in the Map Editor.</summary>
    public SpriteDirection Facing { get; set; } = SpriteDirection.South;

    /// <summary>Map Editor overrides of the object's starting state (null = as the Object Editor says).</summary>
    public bool? StartsOpen { get; set; }
    public bool? StartsLocked { get; set; }
    public bool? StartsLit { get; set; }

    // ---------- Live state in play (the game's own copy of the map; null/0 until something changes it) ----------
    public bool? IsOpenRuntime { get; set; }
    public bool? IsLockedRuntime { get; set; }
    public bool? IsLitRuntime { get; set; }
    /// <summary>Game minutes of burning left (light sources), -1 = not started counting.</summary>
    public double BurnLeftRuntime { get; set; } = -1;
    public bool IsSpentRuntime { get; set; }
    /// <summary>Hit points left once struck (-1 = untouched).</summary>
    public int HitPointsRuntime { get; set; } = -1;
    /// <summary>Frame shown, for objects whose frames are states (a bed cover).</summary>
    public int FrameRuntime { get; set; }
    /// <summary>A trap that has gone off (one-shot), or is revealed.</summary>
    public bool TrapSprungRuntime { get; set; }

    // ---------- Build 248 types: set in the Map Editor ----------
    /// <summary>A plant's starting growth stage (null = the first).</summary>
    public int? StartStage { get; set; }
    /// <summary>A liquid container's starting liquid (null = as the Object Editor says; "" = empty).</summary>
    public string? StartsFullOf { get; set; }
    /// <summary>A lever's links: the InstanceIds of the placed objects it works.</summary>
    public List<int> LinkedInstanceIds { get; set; } = new();
    /// <summary>A ladder's destination cell and lift (-1 = none set).</summary>
    public int DestinationX { get; set; } = -1;
    public int DestinationY { get; set; } = -1;
    public int DestinationLift { get; set; }

    // ---------- Build 248 types: live state in play ----------
    /// <summary>Game minutes a plant has grown since its last stage.</summary>
    public double GrowMinutesRuntime { get; set; }
    /// <summary>A keychain's keys (Key object ids); null until play starts (then its Starting Keys).</summary>
    public List<int>? KeyIdsRuntime { get; set; }
    /// <summary>Uses left (-1 = not counting yet: as many as its Charges).</summary>
    public int ChargesRuntime { get; set; } = -1;
    /// <summary>What a liquid container holds now ("" = empty; null = not set yet).</summary>
    public string? LiquidRuntime { get; set; }
    /// <summary>Seconds left on a lit fuse (-1 = not lit).</summary>
    public double FuseRuntime { get; set; } = -1;
}

/// <summary>
/// The full catalog of object definitions for a project. Dynamic list, no
/// cap on how many object types you can define.
/// </summary>
public class ObjectDatabase
{
    public List<WorldObject> Objects { get; set; } = new();
    /// <summary>Custom effects (Object Editor > Add Custom Stat), shared by every potion, food, spell and useable object.</summary>
    public List<CustomEffect> CustomEffects { get; set; } = new();
    public int NextCustomEffectId() => CustomEffects.Count == 0 ? 1 : CustomEffects.Max(c => c.Id) + 1;

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
        Upgrade();
    }

    /// <summary>Objects saved before types existed: a Key or Lockpick (Use As) becomes that type; everything else is Junk. Local variable counters are re-synced.</summary>
    public void Upgrade()
    {
        foreach (var obj in Objects)
        {
            if (obj.UseAs == ObjectUse.Key) obj.Type = ObjectType.Key;
            else if (obj.UseAs == ObjectUse.Lockpick) obj.Type = ObjectType.Lockpick;
            obj.UseAs = ObjectUse.Nothing; // Type says it now
            obj.SyncLocalVariableIdAfterLoad();
            while (obj.Bundle.Levels.Count < 8) obj.Bundle.Levels.Add(new SpriteLink());
        }
    }
}
