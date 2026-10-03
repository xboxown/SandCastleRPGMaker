namespace SandcastleRpgMaker.Engine;

/// <summary>One frame of an animated sprite - its image, how long it holds, and an optional sound to play when this frame plays.</summary>
public class SpriteFrame
{
    public string FilePath { get; set; } = string.Empty;
    public int DurationMs { get; set; } = 100;

    /// <summary>-1 = no sound attached to this frame.</summary>
    public int SoundResourceId { get; set; } = -1;

    /// <summary>Maps a SpriteLayer's Id to this frame's own image file for that layer. Empty means this frame predates layers (or the sprite has none) - FilePath alone is still authoritative in that case. Once layers exist, FilePath instead holds the composited (flattened) result of all visible layers, kept in sync automatically.</summary>
    public Dictionary<int, string> LayerFilePaths { get; set; } = new();
}

/// <summary>
/// One layer (or layer group) in a sprite's layer stack - shared structure
/// across every frame of the sprite, matching Aseprite/GameMaker: the
/// layer's name/visibility/lock/order is the same for all frames, but each
/// frame has its own pixel content per layer (see SpriteFrame.LayerFilePaths).
/// Stored top-to-bottom (index 0 = topmost in the panel and in compositing).
/// A group has IsGroup=true and contains no pixels itself; its own Visible
/// flag also hides everything nested under it.
/// </summary>
public class SpriteLayer
{
    public int Id { get; set; }
    public string Name { get; set; } = "Layer";
    public bool Visible { get; set; } = true;
    public bool Locked { get; set; }
    public bool IsGroup { get; set; }

    /// <summary>-1 = top-level (not inside a group).</summary>
    public int ParentGroupId { get; set; } = -1;
}

/// <summary>
/// 9-slice scaling guides - how many pixels in from each outer edge the
/// image gets cut into a 3x3 grid, so corners stay crisp/unscaled while
/// edges and the center stretch or tile when the sprite is resized (used
/// for UI panels, dialog boxes, buttons).
/// </summary>
public class NineSliceSettings
{
    public bool Enabled { get; set; }
    public int Left { get; set; }
    public int Right { get; set; }
    public int Top { get; set; }
    public int Bottom { get; set; }
}

/// <summary>One sprite within a group - a name plus the image file it points to.</summary>
public class SpriteEntry
{
    public string Name { get; set; } = "New Sprite";

    /// <summary>Kept exactly as-is for backward compatibility - every existing consumer (Book Editor, item picker, etc.) reads just this for "the sprite's picture" and is unaffected by animation. Kept in sync with Frames[0] by the sprite editor itself once frames exist.</summary>
    public string FilePath { get; set; } = string.Empty;

    /// <summary>
    /// True once this sprite has been packaged into (or loaded back out of)
    /// shapes.sdbx as part of an open project. Not saved to disk itself
    /// (shapes.sdbx's own existence already implies it) - MainWindow sets
    /// this on every sprite right after New/Open/Save Project succeeds.
    /// Purely a display flag: FilePath underneath is still a real, editable
    /// loose file (a temp-extracted copy) the whole time - see ProjectFiles.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsProjectIntegrated { get; set; }

    /// <summary>What the Sprite Editor's location column should actually show - the real path only matters, and is only ever shown, before a project exists.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string DisplayLocation => IsProjectIntegrated ? "\U0001F5A5 Integrated Into System" : FilePath;

    /// <summary>Animation frames - empty means this is a plain static sprite (the original, unchanged behavior). Non-empty means it's animated; only editors updated to look for this will actually animate it.</summary>
    public List<SpriteFrame> Frames { get; set; } = new();
    public int Fps { get; set; } = 30;
    public bool Loop { get; set; } = true;
    public int OriginX { get; set; }
    public int OriginY { get; set; }
    public string OriginAnchor { get; set; } = "Top Left";

    public NineSliceSettings NineSlice { get; set; } = new();

    /// <summary>Empty means this sprite has no layers yet - it behaves exactly as before (a single flat image per frame). Once populated, FilePath per frame becomes the composited result of all visible layers.</summary>
    public List<SpriteLayer> Layers { get; set; } = new();
    private int _nextLayerId = 1;
    public int GetNextLayerId() => _nextLayerId++;

    /// <summary>Re-syncs the layer id counter after loading from disk (private fields aren't persisted).</summary>
    public void SyncLayerIdAfterLoad()
    {
        _nextLayerId = Layers.Count == 0 ? 1 : Layers.Max(l => l.Id) + 1;
    }
}

/// <summary>
/// A named, freely-created bag of sprites. Some group names are, by
/// convention, "expected" by other editors (e.g. an NPC's walk-left pose
/// looks for a group named to match) - but that convention is enforced by
/// the *consuming* editor, not by SpriteGroup itself, which stays a simple
/// container. Any group name can become a "Preserved" one just by matching
/// what a consuming editor looks for.
/// </summary>
public class SpriteGroup
{
    public string Name { get; set; } = "New Group";
    public List<SpriteEntry> Sprites { get; set; } = new();
}

/// <summary>
/// The full set of sprite groups for a project. Every editor that needs a
/// sprite reads from here - including tilesets now, via the hardcoded
/// TilesetGroupName group (this used to be an exception; it isn't
/// anymore, per an explicit, deliberate architecture change).
/// </summary>
public class SpriteDatabase
{
    /// <summary>
    /// Reserved group name for tilesets - always present, cannot be
    /// deleted or renamed. The Map Editor's tileset dropdown reads its
    /// options directly from this group's Sprites: each SpriteEntry here
    /// is one whole named tileset (e.g. "Overworld"), and the map editor's
    /// own hardcoded logic treats that entry's Frames as a grid of
    /// distinct tiles rather than a played animation sequence - unlike
    /// every other group, where Frames really do mean "play these in
    /// order."
    /// </summary>
    public const string TilesetGroupName = "Tileset";

    public List<SpriteGroup> Groups { get; set; } = new();

    /// <summary>Call once after construction/load so the Tileset group always exists, without needing every caller to remember to create it.</summary>
    public SpriteGroup EnsureTilesetGroup() => GetOrCreateGroup(TilesetGroupName);

    /// <summary>
    /// Reserved group for the game's graphical fonts - always present, red,
    /// can't be deleted or renamed, and holds exactly the two locked font
    /// sprites below. A font sprite's Frames are glyphs, not an animation:
    /// frame N is the character (char)(FontFirstChar + N), laid out like a
    /// font sheet of FontSheetColumns columns (space ! " # ... ~).
    /// </summary>
    public const string FontsGroupName = "Fonts";
    /// <summary>The main in-game font: identify labels, gumps, books - all in-game text.</summary>
    public const string InGameFontName = "InGameFont";
    /// <summary>The font conversations/dialog boxes are written in.</summary>
    public const string DialogFontName = "DialogFont";
    public const int FontFirstChar = 32;
    public const int FontGlyphCount = 95; // ' ' (32) through '~' (126)
    public const int FontSheetColumns = 18;

    public static bool IsLockedGroup(string name) => name == TilesetGroupName || name == FontsGroupName;
    public static bool IsFontSprite(string groupName, string spriteName) =>
        groupName == FontsGroupName && (spriteName == InGameFontName || spriteName == DialogFontName);

    /// <summary>Which character a font frame draws, or null past the end of the sheet.</summary>
    public static char? FontCharForFrame(int frameIndex) =>
        frameIndex >= 0 && frameIndex < FontGlyphCount ? (char)(FontFirstChar + frameIndex) : null;

    /// <summary>Call once after construction/load: the Tileset group, and the Fonts group with its two font sprites, always exist.</summary>
    public void EnsureBuiltInGroups()
    {
        EnsureTilesetGroup();
        var fonts = GetOrCreateGroup(FontsGroupName);
        foreach (string name in new[] { InGameFontName, DialogFontName })
            if (fonts.Sprites.All(s => s.Name != name)) fonts.Sprites.Add(new SpriteEntry { Name = name, Loop = false });
    }

    /// <summary>Every font in the Fonts group - the two game fonts plus any extra ones added for books.</summary>
    public List<string> FontNames() =>
        Groups.FirstOrDefault(g => g.Name == FontsGroupName)?.Sprites.Select(s => s.Name).ToList() ?? new List<string>();

    public SpriteEntry? FindFont(string fontName) =>
        Groups.FirstOrDefault(g => g.Name == FontsGroupName)?.Sprites.FirstOrDefault(s => s.Name == fontName);

    public SpriteGroup GetOrCreateGroup(string name)
    {
        var existing = Groups.FirstOrDefault(g => g.Name == name);
        if (existing is not null) return existing;

        var group = new SpriteGroup { Name = name };
        Groups.Add(group);
        return group;
    }
}
