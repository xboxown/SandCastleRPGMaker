using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

/// <summary>The bare identity/bookkeeping fields - who, what, when. The actual project-wide data (factions, variables, stats, tile properties) lives alongside this in ManifestPayload, not inside ProjectManifest itself, so this stays a stable, simple identity block even as the rest of the payload grows.</summary>
public class ProjectManifest
{
    public int FormatVersion { get; set; } = 1;
    public Guid ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    /// <summary>The game's style (File > New Project): GameStyle.Ultima7 (every project made before styles existed) or GameStyle.Ultima8 - its own engine.</summary>
    public string Style { get; set; } = GameStyle.Ultima7;
}

/// <summary>A project's style: which engine its game runs in.</summary>
public static class GameStyle
{
    public const string Ultima7 = "Ultima7";
    public const string Ultima8 = "Ultima8";
}

/// <summary>
/// What actually lives inside .msrp once encrypted and unwrapped - not
/// just identity anymore. Per spec: everything that "glues the whole
/// project together" and isn't specifically sprite-shape data
/// (shapes.sdbx), audio data (audios.sdbx), or object/item/NPC data
/// (objects.sdbx) belongs here - factions, variables, stats, and tile
/// properties now; maps and map-level events once a real Map Editor
/// exists to give them a real shape. Faction/Variable/Stat/TileProperties
/// were all previously either separate, unencrypted, unmarried loose files
/// (FactionDatabaseSerializer, TilePropertiesSerializer) or, briefly for
/// Variable/Stat, their own separate variables.sdbx - every one of those
/// was exactly the "files could get mixed between projects" risk this
/// restructuring closes. Adding Maps here later is a simple additive JSON
/// field, not a breaking change, once there's a real MapDatabase to put in
/// it - nothing about that requires touching this class's existing fields.
/// </summary>
public class ManifestPayload
{
    public ProjectManifest Manifest { get; set; } = new();
    public FactionDatabase Factions { get; set; } = new();
    public VariableDatabase Variables { get; set; } = new();
    public StatDatabase Stats { get; set; } = new();
    public PoseDatabase Poses { get; set; } = new();
    public TilePropertiesDatabase TileProperties { get; set; } = new();
    /// <summary>Null until "New Map" has actually been used - not every project necessarily has a map yet.</summary>
    public MapData? Map { get; set; }
    /// <summary>Per spec: "Information of the player are stored at the .msrp file. Not a separate file."</summary>
    public CharacterDatabase Characters { get; set; } = new();
    /// <summary>Same real pattern as Characters - stored directly in the .msrp file, not a separate one.</summary>
    public ContainerDatabase Containers { get; set; } = new();
    /// <summary>The Object Editor's objects (walls, doors, lights, junk...). Saved with the project.</summary>
    public ObjectDatabase Objects { get; set; } = new();
    /// <summary>The Currency Editor's kinds of money.</summary>
    public CurrencyDatabase Currencies { get; set; } = new();
    /// <summary>Project-wide system settings (mouse pointers today). Older projects without it load with defaults.</summary>
    public SystemSettings Settings { get; set; } = new();
    /// <summary>The Video Library's videos (names, codecs); their bytes are in videos.sdbx. Older projects load with none.</summary>
    public VideoDatabase Videos { get; set; } = new();
    /// <summary>The other worlds (Maps tab) - Map is the starting world. Older projects have none.</summary>
    public List<MapData> Worlds { get; set; } = new();

    /// <summary>The houses of the House Editor (Map Editor > Houses), plotted on maps as often as you like.</summary>
    public List<HouseTemplate> Houses { get; set; } = new();
}

/// <summary>
/// What actually lives inside shapes.sdbx: the sprite database exactly as
/// edited, plus the raw bytes of every image file it references, keyed by
/// the FilePath string the database itself uses to find them. This is the
/// "apartment building" - every sprite, its frames, and every layer image
/// travel together in one file instead of a folder of loose PNGs.
/// Sound still isn't embedded here - SpriteFrame only carries a
/// SoundResourceId (a reference into ResourceDatabase/audios.sdbx), and the
/// sprite editor doesn't wire up sound at all yet, so there's still nothing
/// real on this side to embed even though audios.sdbx itself now exists.
/// </summary>
public class ShapeContainerPayload
{
    public SpriteDatabase Database { get; set; } = new();
    public Dictionary<string, byte[]> EmbeddedFiles { get; set; } = new();
}

/// <summary>
/// What lives inside audios.sdbx: the whole ResourceDatabase (every sound
/// and music entry, its Attributes/Target Options/Audio Group/Export
/// Directory settings, and the Sound Mixer's saved column/link layout -
/// since MixerLayout travels as a property ON ResourceDatabase itself, it
/// needs no separate handling here) plus the raw bytes of every sound/music
/// file it references, embedded the same way shapes.sdbx embeds sprite
/// images. Sprite-kind entries some ResourceDatabase rows may also hold are
/// deliberately left alone here - those are a different, older concept
/// from the dedicated SpriteDatabase shapes.sdbx already owns, not something
/// this piece unifies.
/// </summary>
public class AudioContainerPayload
{
    public ResourceDatabase Database { get; set; } = new();
    public Dictionary<string, byte[]> EmbeddedFiles { get; set; } = new();
}

/// <summary>
/// Packs/unpacks the project's files (.msrp, shapes.sdbx, objects.sdbx,
/// audios.sdbx). Saving always embeds fresh bytes read from whatever loose
/// files the relevant database currently points at; loading always
/// extracts those bytes back out to real files and rewrites the database
/// to point at the extracted copies - so between a Load and the next Save,
/// sprites and sounds are ordinary loose-file-backed data again, exactly
/// like before any project existed. That's a deliberate, temporary bridge:
/// swapping the Sprite/Sound Editors over to read/write their .sdbx files
/// directly (showing "Integrated Into System" instead of a path) is its
/// own follow-up piece, not done here - audios.sdbx has this same
/// still-open gap as shapes.sdbx, not yet closed for either.
/// </summary>
public static class ProjectFiles
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>
    /// The name an embedded file is unpacked under: a fresh "NNNN_" number in
    /// front of its own name, with any earlier "NNNN_" numbers removed first.
    /// The saved key is the previous unpacked path, so without the stripping
    /// every load added one more "0000_" and the name grew by 5 characters
    /// per load/save cycle - until it passed Windows' path limits and every
    /// sound (and soon every sprite) failed to open.
    /// </summary>
    private static string ExtractedFileName(int counter, string originalPath)
    {
        string name = Path.GetFileName(originalPath);
        while (name.Length > 5 && char.IsDigit(name[0]) && char.IsDigit(name[1]) && char.IsDigit(name[2]) && char.IsDigit(name[3]) && name[4] == '_')
            name = name[5..];
        return $"{counter:0000}_{name}";
    }

    /// <summary>
    /// Where each unpacked picture and sound came from: the .sdbx it was in, and its name there. If its file is gone
    /// when the project is saved (deleted by something else), its bytes are taken from that .sdbx again - never
    /// silently left out of the new one (which lost a project's sprite pictures, build 462).
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string Sdbx, string Key)> Unpacked = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The pictures and sounds the last save couldn't find at all (file gone, and not in the file it came from): what they belong to.</summary>
    public static List<string> LastSaveMissing { get; } = new();

    /// <summary>The bytes of each file a database uses: read from disk, or - its file gone - from the .sdbx it was unpacked from.</summary>
    private static void EmbedFiles(IEnumerable<(string Path, string Owner)> uses, Dictionary<string, byte[]> into, Func<string, Dictionary<string, byte[]>?> readSdbx)
    {
        var sdbxCache = new Dictionary<string, Dictionary<string, byte[]>?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, owner) in uses)
        {
            if (string.IsNullOrEmpty(path) || into.ContainsKey(path)) continue;
            if (File.Exists(path)) { into[path] = File.ReadAllBytes(path); continue; }
            if (Unpacked.TryGetValue(path, out var from) && File.Exists(from.Sdbx))
            {
                if (!sdbxCache.TryGetValue(from.Sdbx, out var old)) sdbxCache[from.Sdbx] = old = readSdbx(from.Sdbx);
                if (old is not null && old.TryGetValue(from.Key, out var bytes))
                {
                    into[path] = bytes;
                    try { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, bytes); } catch (IOException) { } catch (UnauthorizedAccessException) { } // the editor shows it again
                    continue;
                }
            }
            if (!LastSaveMissing.Contains(owner)) LastSaveMissing.Add(owner);
        }
    }

    /// <summary>The unpack folder is only a cache of what's embedded in the project file - old copies from earlier loads are removed so they don't pile up. A file still in use is simply left alone.</summary>
    private static void ClearExtractFolder(string extractFolder)
    {
        foreach (var file in Directory.EnumerateFiles(extractFolder))
        {
            try { File.Delete(file); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    public static void SaveManifest(string path, ManifestPayload payload)
    {
        // Written through a stream, a little at a time: serialized to one string, a big project's JSON (80 MB and up)
        // grew a shared buffer to twice its size, and the runtime's buffer pool kept that for good.
        using var utf8 = new MemoryStream();
        JsonSerializer.Serialize(utf8, payload, JsonOptions);
        string json = System.Text.Encoding.UTF8.GetString(utf8.GetBuffer(), 0, (int)utf8.Length);
        ProjectContainer.Save(path, payload.Manifest.ProjectId, json);
    }

    /// <summary>Reads the whole payload without needing to know the project ID up front - it discovers its own ID from the file's header, then fully decrypts and validates against that same ID.</summary>
    public static ManifestPayload LoadManifest(string path)
    {
        Guid id = ProjectContainer.PeekProjectId(path);
        string json = ProjectContainer.Load(path, id);
        // Read from its UTF-8 bytes as they are (a span): from one string the whole JSON went through a pooled buffer
        // (twice its size) that the runtime kept afterwards - hundreds of MB for a big project.
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes(json);
        json = string.Empty;
        var payload = JsonSerializer.Deserialize<ManifestPayload>(new ReadOnlySpan<byte>(utf8), JsonOptions)
                       ?? throw new ProjectFileException($"\"{Path.GetFileName(path)}\" could not be read as a project manifest.");
        payload.Factions.SyncNextIdAfterLoad();
        payload.Variables.SyncNextIdAfterLoad();
        payload.Stats.SyncNextIdAfterLoad();
        payload.Poses.SyncNextIdAfterLoad();
        payload.Characters.SyncNextIdAfterLoad();
        payload.Objects ??= new ObjectDatabase();
        payload.Objects.SyncNextIdAfterLoad();
        payload.Currencies ??= new CurrencyDatabase();
        return payload;
    }

    public static void SaveShapeSdbx(string path, Guid projectId, SpriteDatabase database)
    {
        var payload = new ShapeContainerPayload { Database = database };
        var uniquePaths = new HashSet<string>();

        foreach (var group in database.Groups)
        {
            foreach (var sprite in group.Sprites)
            {
                if (!string.IsNullOrEmpty(sprite.FilePath)) uniquePaths.Add(sprite.FilePath);
                foreach (var frame in sprite.Frames)
                {
                    if (!string.IsNullOrEmpty(frame.FilePath)) uniquePaths.Add(frame.FilePath);
                    foreach (var layerPath in frame.LayerFilePaths.Values)
                        if (!string.IsNullOrEmpty(layerPath)) uniquePaths.Add(layerPath);
                }
            }
        }

        LastSaveMissing.Clear();
        var uses = database.Groups.SelectMany(g => g.Sprites.SelectMany(sp =>
            new[] { (sp.FilePath, $"{g.Name}/{sp.Name}") }
                .Concat(sp.Frames.Select(f => (f.FilePath, $"{g.Name}/{sp.Name}")))
                .Concat(sp.Frames.SelectMany(f => f.LayerFilePaths.Values.Select(l => (l, $"{g.Name}/{sp.Name}"))))));
        EmbedFiles(uses.Where(u => uniquePaths.Contains(u.Item1)), payload.EmbeddedFiles, sdbx =>
        {
            try { return JsonSerializer.Deserialize<ShapeContainerPayload>(ProjectContainer.Load(sdbx, projectId), JsonOptions)?.EmbeddedFiles; }
            catch (Exception) { return null; }
        });

        string json = JsonSerializer.Serialize(payload, JsonOptions);
        ProjectContainer.Save(path, projectId, json);
    }

    /// <summary>Decrypts shapes.sdbx, writes its embedded image bytes out to real files under extractFolder, and rewrites the returned database's paths to point at them.</summary>
    public static SpriteDatabase LoadShapeSdbx(string path, Guid projectId, string extractFolder)
    {
        string json = ProjectContainer.Load(path, projectId);
        var payload = JsonSerializer.Deserialize<ShapeContainerPayload>(json, JsonOptions)
                      ?? throw new ProjectFileException($"\"{Path.GetFileName(path)}\" could not be read as a sprite database.");

        Directory.CreateDirectory(extractFolder);
        ClearExtractFolder(extractFolder);
        var remap = new Dictionary<string, string>();
        int counter = 0;
        foreach (var (originalPath, bytes) in payload.EmbeddedFiles)
        {
            string extractedPath = Path.Combine(extractFolder, ExtractedFileName(counter, originalPath));
            File.WriteAllBytes(extractedPath, bytes);
            remap[originalPath] = extractedPath;
            Unpacked[extractedPath] = (Path.GetFullPath(path), originalPath); // its bytes can be found again if the file goes
            counter++;
        }

        foreach (var group in payload.Database.Groups)
        {
            foreach (var sprite in group.Sprites)
            {
                if (remap.TryGetValue(sprite.FilePath, out var newSpritePath)) sprite.FilePath = newSpritePath;
                foreach (var frame in sprite.Frames)
                {
                    if (remap.TryGetValue(frame.FilePath, out var newFramePath)) frame.FilePath = newFramePath;
                    foreach (var key in frame.LayerFilePaths.Keys.ToList())
                        if (remap.TryGetValue(frame.LayerFilePaths[key], out var newLayerPath))
                            frame.LayerFilePaths[key] = newLayerPath;
                }
                sprite.SyncLayerIdAfterLoad();
            }
        }

        return payload.Database;
    }

    public static void SaveAudiosSdbx(string path, Guid projectId, ResourceDatabase database)
    {
        var payload = new AudioContainerPayload { Database = database };
        var uniquePaths = database.Entries
            .Where(e => e.Kind is ResourceKind.Sound or ResourceKind.Music)
            .Select(e => e.FilePath)
            .Where(p => !string.IsNullOrEmpty(p))
            .Distinct();

        LastSaveMissing.Clear();
        var owners = database.Entries.Where(e => e.Kind is ResourceKind.Sound or ResourceKind.Music).GroupBy(e => e.FilePath).ToDictionary(g => g.Key ?? "", g => g.First().Name);
        EmbedFiles(uniquePaths.Select(p => (p, owners.TryGetValue(p, out var n) ? $"sound \"{n}\"" : p)), payload.EmbeddedFiles, sdbx =>
        {
            try { return JsonSerializer.Deserialize<AudioContainerPayload>(ProjectContainer.Load(sdbx, projectId), JsonOptions)?.EmbeddedFiles; }
            catch (Exception) { return null; }
        });

        string json = JsonSerializer.Serialize(payload, JsonOptions);
        ProjectContainer.Save(path, projectId, json);
    }

    /// <summary>Decrypts audios.sdbx and writes its embedded sound/music bytes back out to real files under extractFolder, rewriting the returned database's FilePaths to point at them - same bridge pattern as LoadShapeSdbx, for the same reason (the Sound Editor only knows how to work with real files on disk).</summary>
    public static ResourceDatabase LoadAudiosSdbx(string path, Guid projectId, string extractFolder)
    {
        string json = ProjectContainer.Load(path, projectId);
        var payload = JsonSerializer.Deserialize<AudioContainerPayload>(json, JsonOptions)
                      ?? throw new ProjectFileException($"\"{Path.GetFileName(path)}\" could not be read as an audio database.");

        Directory.CreateDirectory(extractFolder);
        ClearExtractFolder(extractFolder);
        var remap = new Dictionary<string, string>();
        int counter = 0;
        foreach (var (originalPath, bytes) in payload.EmbeddedFiles)
        {
            string extractedPath = Path.Combine(extractFolder, ExtractedFileName(counter, originalPath));
            File.WriteAllBytes(extractedPath, bytes);
            remap[originalPath] = extractedPath;
            Unpacked[extractedPath] = (Path.GetFullPath(path), originalPath); // its bytes can be found again if the file goes
            counter++;
        }

        foreach (var entry in payload.Database.Entries)
        {
            if (entry.Kind is ResourceKind.Sound or ResourceKind.Music && remap.TryGetValue(entry.FilePath, out var newPath))
                entry.FilePath = newPath;
        }

        payload.Database.SyncNextIdAfterLoad();
        return payload.Database;
    }

    public static void SaveObjectsSdbx(string path, Guid projectId, BookDatabase database)
    {
        string json = JsonSerializer.Serialize(database, JsonOptions);
        ProjectContainer.Save(path, projectId, json);
    }

    public static BookDatabase LoadObjectsSdbx(string path, Guid projectId)
    {
        string json = ProjectContainer.Load(path, projectId);
        var db = JsonSerializer.Deserialize<BookDatabase>(json, JsonOptions)
                 ?? throw new ProjectFileException($"\"{Path.GetFileName(path)}\" could not be read as a book database.");
        db.SyncNextIdAfterLoad();
        return db;
    }
}
