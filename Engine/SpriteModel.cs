namespace SandcastleRpgMaker.Engine;

/// <summary>One frame of an animated sprite - its image, how long it holds, and an optional sound to play when this frame plays.</summary>
public class SpriteFrame
{
    public string FilePath { get; set; } = string.Empty;
    public int DurationMs { get; set; } = 100;

    /// <summary>-1 = no sound attached to this frame.</summary>
    public int SoundResourceId { get; set; } = -1;

    /// <summary>
    /// A character's frame: where its weapon hand is, in this frame's pixels (Exult's weapon offset for an actor frame,
    /// wihh) - it may be a little outside the picture, a weapon held out from the body. Both NoHand (-1, -1): no weapon
    /// is drawn in this frame (lying down, asleep; Exult's 255).
    /// </summary>
    public int HandX { get; set; } = NoHand;
    public int HandY { get; set; } = NoHand;
    public const int NoHand = -1;
    /// <summary>This frame has a hand point (HandX/HandY aren't both NoHand).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasHand => HandX != NoHand || HandY != NoHand;
    /// <summary>A character's frame: which frame of the weapon-in-hand sprite it shows (-1: chosen by itself - held, or the swing for an attack).</summary>
    public int WeaponFrame { get; set; } = -1;
    /// <summary>A character's frame: the weapon drawn behind the body (facing away) - else over it, as Exult paints it.</summary>
    public bool WeaponBehind { get; set; }
    /// <summary>A weapon-in-hand frame: where the hand holds it, in this frame's pixels (Exult's weapon offset for a weapon frame) - -1: its middle.</summary>
    public int GripX { get; set; } = -1;
    public int GripY { get; set; } = -1;
    /// <summary>
    /// Ultima 8: where this frame stands - the point (on the feet) that sits on the character's spot, in this frame's own
    /// pixels: Pentagram's frame offset, lost when frames are cut to what's drawn in each, so a walk would sway. -1: its
    /// bottom-middle (or the sprite's Origin). Set in the Sprite Editor (Ultima 8 projects only).
    /// </summary>
    public int AnchorX { get; set; } = -1;
    public int AnchorY { get; set; } = -1;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool HasAnchor => AnchorX >= 0 && AnchorY >= 0;
    /// <summary>
    /// Ultima 8: a step doesn't move in this frame (both feet planted) - Pentagram's animations move by each frame's own
    /// distance, nothing in a standing frame. None ticked in a step's sprite: its first frame holds still.
    /// </summary>
    public bool HoldsStill { get; set; }

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
/// <summary>What a folder's sprites are for (Sprite Editor > right-click a folder > Identity): the editors' sprite lists show the folders of their own kind.</summary>
public enum SpriteFolderIdentity { None, Object, Container, Book, Character, House, Vehicle, Paperdoll }

/// <summary>A folder of sprites (the Sprite Editor's folders; "group" in the data, as before).</summary>
public class SpriteGroup
{
    public string Name { get; set; } = "New Group";
    public List<SpriteEntry> Sprites { get; set; } = new();
    /// <summary>What its sprites are for; None: anything (shown in every list).</summary>
    public SpriteFolderIdentity Identity { get; set; }
    /// <summary>Identity Object: the object type it's for (null: any object).</summary>
    public ObjectType? ObjectKind { get; set; }
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
    /// The folders an editor's sprite list shows: those given its identity (an object type: that type's, or any object's) -
    /// or, while no folder has it, every folder, as before identities existed.
    /// </summary>
    public List<SpriteGroup> FoldersFor(SpriteFolderIdentity identity, ObjectType? kind = null)
    {
        var own = Groups.Where(g => g.Identity == identity && (identity != SpriteFolderIdentity.Object || kind is null || g.ObjectKind is null || g.ObjectKind == kind)).ToList();
        return own.Count > 0 ? own : Groups.ToList();
    }

    /// <summary>The sprites of FoldersFor.</summary>
    public List<SpriteEntry> SpritesFor(SpriteFolderIdentity identity, ObjectType? kind = null) => FoldersFor(identity, kind).SelectMany(g => g.Sprites).ToList();

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

    /// <summary>
    /// Reserved group for blood - always present, red, can't be deleted. Each
    /// sprite in it is one blood type (red, green, purple...), chosen per
    /// character in the Character Editor. As Exult's blood shape (912), its
    /// frames are puddle sizes, not an animation: frame 1 the smallest, each
    /// next one bigger, up to BloodSizes. No sound, no animation.
    /// </summary>
    public const string BloodGroupName = "Blood";
    /// <summary>Exult's blood frames 0-3: four sizes, small to large.</summary>
    public const int BloodSizes = 4;

    /// <summary>Reserved groups for the bundle pictures of Ammo and Money objects (Exult's quantity frames): one sprite per picture, each free to animate.</summary>
    public const string AmmoGroupName = "Ammo";
    public const string MoneyGroupName = "Money";

    /// <summary>Lockpicks stack too (Ultima VII: "5 lockpicks"), drawn by how many - the same eight bundle pictures.</summary>
    public const string LockpickGroupName = "Lockpick";

    public static bool IsBundleGroup(string name) => name == AmmoGroupName || name == MoneyGroupName || name == LockpickGroupName;

    /// <summary>
    /// Reserved group for keychains (Exult's keyring): each sprite is one
    /// keychain look, its frames how many keys it holds - frame 1 none, then
    /// 1, 2, 3, and 4 or more (Exult's set_keyring_frame). States, not an
    /// animation: no sound, no animation.
    /// </summary>
    public const string KeychainGroupName = "Keychain";
    public const int KeychainFrames = 5;

    /// <summary>
    /// Reserved group for dialog box frames (Show Text): each sprite is one
    /// look for the box. Ordinary sprites - frames animate and play their
    /// sounds. With Nine Slice on, the corners stay as drawn and the edges
    /// and middle stretch to the box's size. Empty group = no frame at all.
    /// </summary>
    public const string DialogFrameGroupName = "DialogFrame";

    /// <summary>
    /// Reserved group for weapons held in the hand (Exult's Actor::paint_weapon): each sprite is one weapon's in-hand
    /// look, picked in the Object Editor; its frames are the poses the weapon is drawn in - states, not an animation:
    /// as System Settings > Weapon in hand says (WeaponInHandFrameNames). Each frame has a grip point; each character
    /// frame a hand point - the weapon is drawn with its grip on the hand.
    /// </summary>
    public const string WeaponInHandGroupName = "Weapon in hand";
    /// <summary>Monsters drawn joining up with their own kind: 32 frames by which sides touch (Exult's slimes). See JoiningGuideWindow.</summary>
    public const string JoinedMonstersGroupName = "Joined Monsters";
    /// <summary>Puddles a monster leaves as it walks: as blood, frames are sizes, small to large, up to BloodSizes (Exult's blood shape, frames 4-7). Chosen in Character Editor > Health > Joining.</summary>
    public const string TrailGroupName = "Slime Trail";
    /// <summary>Ultima 8 projects only: the splash when something comes down in real water (an animation played once, scaled to what went in).</summary>
    public const string WaterSplashGroupName = "Water Splash";
    /// <summary>Ultima 8 projects only: lava's flare when something or someone goes in.</summary>
    public const string LavaFlareGroupName = "Lava Flare";

    /// <summary>The eight directions in the order a smooth weapon-in-hand sprite's frames go.</summary>
    public static readonly string[] WeaponDirectionNames = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };

    /// <summary>
    /// What each frame of a weapon-in-hand sprite is. Exult's (not smooth): 1 held, 2-4 the swing pointing north, east,
    /// south (Exult's weapon frames 1-4) - east- and west-facing characters show them mirrored, as Exult reflects them.
    /// Smooth: for each of the eight directions, held, then the swing in so many frames.
    /// </summary>
    public static string[] WeaponInHandFrameNames(bool smooth, int swingFrames)
    {
        if (!smooth) return new[] { "Held (facing north or south; mirrored east / west)", "Swing: pointing north", "Swing: pointing east", "Swing: pointing south" };
        int k = Math.Clamp(swingFrames, 1, 8);
        var names = new List<string>();
        foreach (var dir in WeaponDirectionNames)
        {
            names.Add($"Held, facing {dir}");
            for (int i = 1; i <= k; i++) names.Add($"Swing {i} of {k}, facing {dir}");
        }
        return names.ToArray();
    }

    public static bool IsLockedGroup(string name) => name == TilesetGroupName || name == FontsGroupName || name == BloodGroupName || name == KeychainGroupName || name == DialogFrameGroupName || name == WeaponInHandGroupName || name == JoinedMonstersGroupName || name == TrailGroupName || name == WaterSplashGroupName || name == LavaFlareGroupName || IsBundleGroup(name);

    /// <summary>A dialog frame by name: "" = the first one in the DialogFrame group, "None" = none. Null when there's no frame to draw.</summary>
    public SpriteEntry? FindDialogFrame(string name)
    {
        if (name == "None") return null;
        var frames = Groups.FirstOrDefault(g => g.Name == DialogFrameGroupName)?.Sprites;
        if (frames is null || frames.Count == 0) return null;
        return frames.FirstOrDefault(s => s.Name == name) ?? frames[0];
    }
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
        GetOrCreateGroup(BloodGroupName);
        GetOrCreateGroup(AmmoGroupName);
        GetOrCreateGroup(MoneyGroupName);
        GetOrCreateGroup(LockpickGroupName);
        GetOrCreateGroup(KeychainGroupName);
        GetOrCreateGroup(DialogFrameGroupName);
        GetOrCreateGroup(WeaponInHandGroupName);
        GetOrCreateGroup(JoinedMonstersGroupName);
        GetOrCreateGroup(TrailGroupName);
    }

    /// <summary>Ultima 8 projects: their own locked groups as well (never made in an Ultima 7 project).</summary>
    public void EnsureUltima8Groups()
    {
        GetOrCreateGroup(WaterSplashGroupName);
        GetOrCreateGroup(LavaFlareGroupName);
    }

    /// <summary>A blood type (a sprite in the Blood group) by name, or null.</summary>
    public SpriteEntry? FindBlood(string bloodName) =>
        string.IsNullOrEmpty(bloodName) ? null : Groups.FirstOrDefault(g => g.Name == BloodGroupName)?.Sprites.FirstOrDefault(s => s.Name == bloodName);

    /// <summary>A trail (a sprite in the Slime Trail group) by name, or null.</summary>
    public SpriteEntry? FindTrail(string trailName) =>
        string.IsNullOrEmpty(trailName) ? null : Groups.FirstOrDefault(g => g.Name == TrailGroupName)?.Sprites.FirstOrDefault(s => s.Name == trailName);

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
