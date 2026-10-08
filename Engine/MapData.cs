namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Describes the tileset image a map paints from. Tile count is derived at
/// runtime from the image dimensions / tile size - there is no fixed cap on
/// how many tiles a tileset can contain.
/// </summary>
public class TilesetDefinition
{
    public string ImagePath { get; set; } = string.Empty;
    public int TileWidth { get; set; } = 32;
    public int TileHeight { get; set; } = 32;

    /// <summary>
    /// The name of the SpriteEntry (within SpriteDatabase.TilesetGroupName)
    /// this tileset was built from. When set, ImagePath is meaningless -
    /// the actual tile images come from that sprite's Frames, recomposited
    /// into a working bitmap on load. Empty means this tileset still uses
    /// the old direct-image-file path (pre-existing maps, or a fallback).
    /// </summary>
    public string TilesetSpriteName { get; set; } = string.Empty;
}

/// <summary>
/// A single paintable layer of the map. Stored as a flat, dynamically-sized
/// list (row-major) rather than a fixed-size array, so map dimensions are
/// never baked into the type itself. -1 means "empty / no tile".
/// Multiple layers exist so future Z-stacking (Ultima 7 style layered
/// terrain/objects) can slot in without changing the map format.
/// </summary>
public class MapLayer
{
    public string Name { get; set; } = "Ground";
    public List<int> TileIndices { get; set; } = new();

    /// <summary>Which floor (1-9, MapFloors) this layer is the tiles of. 1 = the ground; an upper floor's tiles are solid slabs you walk on.</summary>
    public int Floor { get; set; } = 1;

    public int GetTile(int x, int y, int mapWidth)
    {
        int i = y * mapWidth + x;
        return (i >= 0 && i < TileIndices.Count) ? TileIndices[i] : -1;
    }

    public void SetTile(int x, int y, int mapWidth, int tileIndex)
    {
        int i = y * mapWidth + x;
        if (i >= 0 && i < TileIndices.Count)
            TileIndices[i] = tileIndex;
    }
}

/// <summary>
/// A full map: its dimensions, the tileset it paints from, and however many
/// layers it has. Width/Height/layer count are all ordinary ints/lists -
/// there is no compiled-in ceiling on map size, tile count, or layer count.
/// </summary>
public class MapData
{
    public string MapName { get; set; } = "Untitled Map";
    public int Width { get; set; }
    public int Height { get; set; }
    public TilesetDefinition Tileset { get; set; } = new();
    public List<MapLayer> Layers { get; set; } = new();

    /// <summary>Path to the ObjectDatabase JSON this map's placed objects were chosen from (so Load Map can auto-load it too). Empty if none.</summary>
    public string ObjectDatabasePath { get; set; } = string.Empty;

    /// <summary>Path to the TilePropertiesDatabase JSON for this map's tileset (so Load Map can auto-load it too). Empty if none.</summary>
    public string TilePropertiesPath { get; set; } = string.Empty;

    /// <summary>Objects placed directly on the map (not inside a container). Dynamic list - no placement cap.</summary>
    public List<ObjectInstance> Objects { get; set; } = new();

    /// <summary>Events placed on this map. Dynamic list - no cap on how many events a map can hold.</summary>
    public List<GameEvent> Events { get; set; } = new();

    /// <summary>
    /// Where the player appears when the game is tested/run. -1 means
    /// unset. Per spec, only one Start Position can exist across the
    /// WHOLE PROJECT, not per map - this lives on MapData for now only
    /// because the project supports a single map today; once multi-map
    /// support exists, this needs to move up to project level and this
    /// field becomes "this map's copy, cleared when another map claims it."
    /// </summary>
    public int StartX { get; set; } = -1;
    public int StartY { get; set; } = -1;
    /// <summary>The floor (1-9) the player starts on.</summary>
    public int StartFloor { get; set; } = 1;

    /// <summary>The tile layer of this floor (1 = the ground), or null if nothing was ever painted there. create: make it (empty) if missing.</summary>
    public MapLayer? LayerForFloor(int floor, bool create = false)
    {
        var layer = floor <= 1 ? Layers.FirstOrDefault() : Layers.FirstOrDefault(l => l.Floor == floor);
        if (layer is not null || !create) return layer;
        layer = new MapLayer { Name = $"Floor {floor}", Floor = floor };
        layer.TileIndices.AddRange(Enumerable.Repeat(-1, Width * Height));
        Layers.Add(layer);
        return layer;
    }

    /// <summary>The upper floors' tile layers (floor 2 and up).</summary>
    public IEnumerable<MapLayer> UpperFloorLayers => Layers.Skip(1).Where(l => l.Floor > 1);

    /// <summary>Weapons/Armors/Items/Books placed as physical, interactable objects on the map - one generic mechanism instead of a separate one per item category, since they all come from the same Select Item picker. Objects keep their own separate, established placement system (the Objects palette tab) rather than duplicating through here too.</summary>
    public List<PlacedItemInstance> Items { get; set; } = new();

    /// <summary>Characters (from the Character Editor) standing on this map - each with the pose and facing it starts in. The main player is not placed here; it starts at the Start Position.</summary>
    public List<PlacedCharacter> Characters { get; set; } = new();

    public int GetNextCharacterPlacementId()
        => Characters.Count == 0 ? 1 : Characters.Max(c => c.Id) + 1;

    public int GetNextInstanceId()
        => Objects.Count == 0 ? 1 : Objects.Max(o => o.InstanceId) + 1;

    public int GetNextEventId()
        => Events.Count == 0 ? 1 : Events.Max(ev => ev.Id) + 1;

    public static MapData CreateEmpty(string name, int width, int height, TilesetDefinition tileset)
    {
        var map = new MapData
        {
            MapName = name,
            Width = width,
            Height = height,
            Tileset = tileset
        };

        var ground = new MapLayer { Name = "Ground" };
        // Fill with -1 (empty) for every cell. Using a List here (not a
        // fixed array) means resizing the map later is just a list operation,
        // not a full type change.
        ground.TileIndices.AddRange(Enumerable.Repeat(-1, width * height));
        map.Layers.Add(ground);

        return map;
    }
}

/// <summary>One item (Weapon/Armor/Item/Book) placed as a physical object on a map cell - the map-placement counterpart to ItemReference, which already generalizes "a pointer to one specific item somewhere in the project" the same way.</summary>
public class PlacedItemInstance : IHasContainerContents
{
    public ReferencedItemCategory Category { get; set; } = ReferencedItemCategory.Book;
    public int ItemId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }

    /// <summary>Unique id for this specific placed instance - only meaningful for a Container, so its own real, live contents can be tracked and so items placed inside it can reference it (matching ObjectInstance's own real ContainerInstanceId). 0 for a non-container, or a container never yet opened at runtime.</summary>
    public int InstanceId { get; set; } = 0;

    /// <summary>Real, live runtime contents - only meaningful for a Container. Seeded once from ContainerData.StartingContents the first time this specific placed instance is opened, then mutated freely from there; never the container's own authored template directly, so opening the SAME chest twice never re-seeds it.</summary>
    public List<InventoryItemInstance> Contents { get; set; } = new();

    /// <summary>Per-instance lock state in play - only meaningful for a Container. Null until a key or lockpick changes it (it's then as the container's IsLocked says); afterwards whether THIS chest is locked, since a key can lock it again (Exult's UseKeyOnChest).</summary>
    public bool? IsLockedRuntime { get; set; }

    /// <summary>
    /// Real, logical elevation/lift level - Ultima 7-style, per spec: NOT
    /// derived from sprite pixel height, NOT recomputed fresh every
    /// render from list order. This is genuine, persistent world state -
    /// set once when an item is placed or dropped (based on what it's
    /// landing on and whether it allows elevation), and only ever changed
    /// again by a real settle pass when whatever was supporting it is
    /// removed. Rendering reads this value to compute a screen offset;
    /// it never writes it. 0 is ground level.
    /// </summary>
    public int Z { get; set; } = 0;

    /// <summary>
    /// Sub-tile placement, in 8px world units, purely additive on top of
    /// X/Y - deliberately NOT a reinterpretation of X/Y itself, so every
    /// already-saved map keeps meaning exactly what it always meant (0 =
    /// unchanged, top-left/default position within the tile). Lets two
    /// items share the same tile without being a stacking attempt at all,
    /// as long as their real footprints (SizeX/SizeY) don't actually
    /// overlap once these offsets are accounted for.
    /// </summary>
    public int OffsetX { get; set; } = 0;
    public int OffsetY { get; set; } = 0;
}

/// <summary>One character placed on a map: which character, which cell, and the pose and facing it starts in (PoseId -1 = Idle).</summary>
public class PlacedCharacter
{
    public int Id { get; set; }
    public int CharacterId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int PoseId { get; set; } = -1;
    public SpriteDirection Facing { get; set; } = SpriteDirection.South;
    /// <summary>The floor (1-9) they start on.</summary>
    public int Floor { get; set; } = 1;
}
