using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The databases of the project open in the Map Editor, for windows that
/// need to reach across editors (the event-command dialogs listing factions
/// and characters, the Faction Editor opening the Character Editor) without
/// every host window having to pass each database along. MainWindow binds
/// it once; each read returns whatever the project holds right now, so a
/// database replaced by loading a project is never stale here.
/// </summary>
internal static class ProjectSession
{
    private static Func<FactionDatabase>? _factions;
    private static Func<CharacterDatabase>? _characters;
    private static Func<SpriteDatabase>? _sprites;
    private static Action<System.Windows.Window, int>? _openCharacterEditor;

    public static void Bind(Func<FactionDatabase> factions, Func<CharacterDatabase> characters, Func<SpriteDatabase> sprites,
        Action<System.Windows.Window, int> openCharacterEditor)
    {
        _factions = factions;
        _characters = characters;
        _sprites = sprites;
        _openCharacterEditor = openCharacterEditor;
    }

    /// <summary>More of the project, for the schedule and event dialogs. MainWindow sets these once.</summary>
    public static Func<ResourceDatabase>? ResourcesProvider;
    public static Func<VariableDatabase>? VariablesProvider;
    public static Func<BookDatabase>? BooksProvider;
    public static Func<ObjectDatabase>? ObjectsProvider;
    public static Func<PoseDatabase>? PosesProvider;
    public static PoseDatabase Poses => PosesProvider?.Invoke() ?? PoseDatabase.CreateDefault();
    public static Func<ContainerDatabase>? ContainersProvider;
    public static Func<CurrencyDatabase>? CurrenciesProvider;
    public static CurrencyDatabase Currencies => CurrenciesProvider?.Invoke() ?? new CurrencyDatabase();
    public static Func<StatDatabase>? StatsProvider;
    /// <summary>A picture of the map open in the Map Editor and its cell size, or null when no map is open.</summary>
    public static Func<(System.Windows.Media.Imaging.BitmapSource Picture, int TileWidth, int TileHeight, int Columns, int Rows)?>? MapSnapshotProvider;
    /// <summary>The whole open map on one floor (1-9), optionally seen inside (roofs and what's overhead left out), for the cell pickers.</summary>
    public static Func<int, bool, (System.Windows.Media.Imaging.BitmapSource Picture, int TileWidth, int TileHeight, int Columns, int Rows)?>? MapFloorPictureProvider;
    /// <summary>The floor the Map Editor is showing.</summary>
    public static Func<int>? MapEditorFloorProvider;
    /// <summary>The things placed on a cell of the open map (Map Editor): each one's key ("Object:{instance id}" or "Item:{category}:{item id}") and its name. Empty when no map is open.</summary>
    public static Func<int, int, List<(string Key, string Name)>>? MapThingsProvider;
    /// <summary>Where a character (by id) is placed on the open map, or null.</summary>
    public static Func<int, (int X, int Y)?>? PlacedSpotProvider;
    /// <summary>The zones of the open map (Map Editor > Mode > Zones), or null when no map is open.</summary>
    public static Func<List<MapZone>?>? ZonesProvider;
    /// <summary>A painted tile's picture from any of the open map's tilesheets (the game's tiles from a map's second, third... sheet).</summary>
    public static Func<int, System.Windows.Media.Imaging.BitmapSource?>? TileSourceProvider;
    /// <summary>The names events go by (map events, objects, books, containers, characters) - for "Event executed".</summary>
    public static Func<List<string>>? EventNamesProvider;
    /// <summary>The project's System Settings.</summary>
    public static Func<SystemSettings>? SettingsProvider;
    /// <summary>System Settings > Sounds: one opening sound for every paperdoll - each one's own picker is then greyed out.</summary>
    public static bool PaperdollSoundIsGlobal => SettingsProvider?.Invoke()?.Sounds.PaperdollOpenSoundEnabled == true;
    public const string PaperdollSoundIsGlobalTip = "System Settings > Sounds > Paperdoll open sound is ticked: every paperdoll uses that one.";

    public static FactionDatabase Factions => _factions?.Invoke() ?? new FactionDatabase();
    public static CharacterDatabase Characters => _characters?.Invoke() ?? new CharacterDatabase();
    public static SpriteDatabase Sprites => _sprites?.Invoke() ?? new SpriteDatabase();

    /// <summary>Opens the Character Editor (modal, over <paramref name="owner"/>) with that character selected.</summary>
    public static void OpenCharacterEditor(System.Windows.Window owner, int characterId) => _openCharacterEditor?.Invoke(owner, characterId);

    public static SpriteEntry? FindSprite(string group, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var sprites = Sprites;
        return sprites.Groups.FirstOrDefault(g => g.Name == group)?.Sprites.FirstOrDefault(s => s.Name == name)
            ?? sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == name);
    }

    /// <summary>The first frame of a sprite as a still picture (faces, icons), or null.</summary>
    public static System.Windows.Media.Imaging.BitmapImage? FirstFrame(SpriteEntry? sprite)
    {
        if (sprite is null) return null;
        string path = sprite.Frames.Count > 0 ? sprite.Frames[0].FilePath : sprite.FilePath;
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.IgnoreImageCache; // a frame redrawn in place must show its new pixels
            bmp.UriSource = new Uri(path);
            bmp.EndInit();
            return bmp;
        }
        catch { return null; }
    }
}
