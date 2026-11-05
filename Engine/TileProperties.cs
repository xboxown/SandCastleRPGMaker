namespace SandcastleRpgMaker.Engine;

public enum TileEffectTrigger
{
    OnEnter,  // fires once the moment the player steps onto the tile
    OnStay,   // fires repeatedly (every TickIntervalSeconds) while the player remains on the tile
    OnExit    // fires once when the player leaves the tile
}

/// <summary>
/// One custom rule attached to a tile type. Deliberately generic rather than
/// a fixed "poison/fire/ice" enum, so a designer can build anything from a
/// simple poison tick to a Serpent-Isle-style ramping frost effect (damage
/// that grows the longer you're exposed, mitigated by specific gear, with a
/// recovery window after leaving) purely through these fields - no engine
/// code changes needed to invent a new hazard.
/// </summary>
public class CustomTileEffect
{
    public string Name { get; set; } = "New Effect";
    public TileEffectTrigger Trigger { get; set; } = TileEffectTrigger.OnStay;

    /// <summary>How often (seconds) this effect re-fires while Trigger is OnStay.</summary>
    public double TickIntervalSeconds { get; set; } = 1.0;

    /// <summary>Damage dealt per tick (0 = no direct damage; use for non-damage effects too).</summary>
    public double DamagePerTick { get; set; } = 0;

    /// <summary>If true, DamagePerTick grows by RampIncreasePerTick each consecutive tick of continuous exposure (the frost/Serpent Isle mechanic).</summary>
    public bool RampingDamage { get; set; } = false;
    public double RampIncreasePerTick { get; set; } = 0;

    /// <summary>Owning any one of these WorldObject ids negates (or reduces, at engine's discretion) this effect while equipped/carried.</summary>
    public List<int> MitigatingObjectIds { get; set; } = new();

    /// <summary>Seconds after leaving the tile before the effect's ramped state fully clears (e.g. "still cold for a bit after leaving the ice").</summary>
    public double RecoverySeconds { get; set; } = 0;

    public string Notes { get; set; } = string.Empty;
}

/// <summary>
/// Properties for one tile *type* (a specific tile index within one named
/// tileset), applying to every placed copy of that tile across every map
/// that uses it.
/// </summary>
public class TileTypeProperties
{
    public string TilesetSpriteName { get; set; } = string.Empty;
    public int TileIndex { get; set; }

    /// <summary>Free-text name for the designer's own reference (e.g. "Ocean", "Swamp", "Lava") - also doubles as the tile's display name, per spec ("Name: that will be the name of the tile and the property name at the same time"). Not used by the engine directly - CustomEffects drive actual behavior.</summary>
    public string TerrainLabel { get; set; } = string.Empty;

    public bool Walkable { get; set; } = true;

    /// <summary>Water: sea creatures (Character Editor > Moves on: Sea) swim here - and only here. Land walkers go by Walkable (deep water: not walkable).</summary>
    public bool IsWater { get; set; }

    /// <summary>Ultima 8 projects only: what this ground does to anyone in it at lift 0 - real water drowns (gone for good), lava burns.
    /// Things that come down in it are lost. The Ultima 7 engine never reads it.</summary>
    public U8Liquid U8Liquid { get; set; }

    /// <summary>On the ground: a tile of the same sheet drawn first, under it, showing through its see-through pixels (grass under a half-tile floor); -1 none.</summary>
    public int UnderTileIndex { get; set; } = -1;

    /// <summary>WorldObject ids required (equipped/carried) to enter this tile at all, independent of any damage effects (e.g. "needs a boat" for deep ocean).</summary>
    public List<int> RequiredObjectIds { get; set; } = new();

    /// <summary>Dynamic list of custom rules - no cap on how many effects one tile type can carry.</summary>
    public List<CustomTileEffect> CustomEffects { get; set; } = new();

    /// <summary>
    /// Name of a dedicated SpriteEntry (in a real, ordinary sprite group -
    /// NOT SpriteDatabase.TilesetGroupName, since this is one tile's own
    /// animation, not a whole tilesheet) whose Frames are this specific
    /// tile's real animation sequence - ocean waves, grass in the wind,
    /// etc. Empty means this tile is static and just shows its one frame
    /// from the tileset sheet, exactly as before.
    /// </summary>
    public string AnimationSpriteName { get; set; } = string.Empty;

    /// <summary>
    /// Same multi-page structure as BookData.EventPages/GameEvent.
    /// EventPages, for the exact same reason - reusing EventEditorControl
    /// and BookEventConditionsControl unmodified rather than building a
    /// third parallel implementation. "By After Read Item" is excluded
    /// from this context's own trigger dropdown (a tile isn't a readable
    /// book), per spec.
    /// </summary>
    public List<BookEventPage> EventPages { get; set; } = new();
}

/// <summary>
/// All tile-type property definitions across every tileset, keyed by
/// "{tilesetSpriteName}:{tileIndex}" - a plain string key, since two
/// different named tilesets can each have their own "tile #0" with
/// completely different meanings, and System.Text.Json handles a string-
/// keyed dictionary far more cleanly than a tuple-keyed one. Only tiles a
/// designer has actually customized need an entry - anything missing is
/// treated as walkable with no effects and no animation.
/// </summary>
/// <summary>Ultima 8's ground that is more than ground (Tile Properties).</summary>
public enum U8Liquid { None, Water, Lava }

public class TilePropertiesDatabase
{
    public Dictionary<string, TileTypeProperties> ByTileKey { get; set; } = new();

    private static string BuildKey(string tilesetSpriteName, int tileIndex) => $"{tilesetSpriteName}:{tileIndex}";

    public TileTypeProperties GetOrCreate(string tilesetSpriteName, int tileIndex)
    {
        string key = BuildKey(tilesetSpriteName, tileIndex);
        if (!ByTileKey.TryGetValue(key, out var props))
        {
            props = new TileTypeProperties { TilesetSpriteName = tilesetSpriteName, TileIndex = tileIndex };
            ByTileKey[key] = props;
        }
        return props;
    }

    public void Remove(string tilesetSpriteName, int tileIndex) => ByTileKey.Remove(BuildKey(tilesetSpriteName, tileIndex));
}
