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
