using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// The game as the Sandcastle player (the C engine for Linux, Mac, MorphOS,
/// AmigaOS 4 and Amiga 68k) reads it - written by Build for those platforms.
/// Encrypted as the Windows game's files are (ProjectContainer: AES-256,
/// stamped with the project's id), in a Game_Data folder:
///  - game.sdbx: the project (maps with their houses and vehicles already
///    built in, characters, objects, settings...) and its books;
///  - sprites.sdbx: every sprite group, sprite and frame (its picture's
///    name, its time, its sound);
///  - pictures.sdbx: each frame's picture as it was (PNG, BMP, GIF...);
///  - sounds.sdbx: each sound and piece of music (its id, volume, whether
///    it's music, and its file as it was - WAV, MP3, IFF 8SVX).
/// pictures.sdbx and sounds.sdbx are archives (SRPK): an encrypted index,
/// then each file encrypted on its own - the player decrypts only what it
/// shows or plays.
/// </summary>
public static class PlayerPack
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static void Export(string folder, ManifestPayload project, BookDatabase books, SpriteDatabase sprites, ResourceDatabase? resources = null, VideoDatabase? videos = null, Func<string, string>? richText = null)
    {
        Directory.CreateDirectory(folder);
        // The project as it plays: a copy, every plotted house and vehicle built into its map (as a game starts).
        var game = JsonSerializer.Deserialize<ManifestPayload>(JsonSerializer.Serialize(project))!;
        foreach (var map in new[] { game.Map }.Concat(game.Worlds))
            if (map is not null) HouseBuilder.BuildInto(map, game.Houses);
        var id = project.Manifest.ProjectId;
        foreach (string old in new[] { "game.json", "sprites.json", "sounds.json" }) File.Delete(Path.Combine(folder, old)); // an older, plain pack
        foreach (string old in new[] { "pictures", "sounds" }) if (Directory.Exists(Path.Combine(folder, old))) Directory.Delete(Path.Combine(folder, old), recursive: true);
        var whole = JsonSerializer.SerializeToNode(new { Format = FormatVersion, Project = game, Books = books }, Json)!;
        if (richText is not null) PlainBookText(whole, richText);
        File.WriteAllBytes(Path.Combine(folder, "game.sdbx"), ProjectContainer.Encrypt(id, System.Text.Encoding.UTF8.GetBytes(whole.ToJsonString(Json))));

        int next = 0;
        var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pictureFiles = new List<(string Name, byte[] Data)>();
        string Picture(string source)
        {
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return "";
            if (copied.TryGetValue(source, out var known)) return known;
            string name = $"{next++}{Path.GetExtension(source).ToLowerInvariant()}";
            pictureFiles.Add((name, File.ReadAllBytes(source)));
            return copied[source] = name;
        }
        var index = sprites.Groups.Select(g => new
        {
            g.Name,
            Sprites = g.Sprites.Select(s => new
            {
                s.Name, s.Fps, s.Loop, s.OriginX, s.OriginY, // a pointer's hot spot
                NineSlice = s.NineSlice.Enabled ? new { s.NineSlice.Left, s.NineSlice.Top, s.NineSlice.Right, s.NineSlice.Bottom } : null, // a dialog frame's borders
                Frames = s.Frames.Select(f => new { File = Picture(f.FilePath), Ms = Math.Max(1, f.DurationMs), Sound = f.SoundResourceId, f.HandX, f.HandY, f.WeaponFrame, f.WeaponBehind, f.GripX, f.GripY }).ToList()
            }).ToList()
        }).ToList();
        File.WriteAllBytes(Path.Combine(folder, "sprites.sdbx"), ProjectContainer.Encrypt(id, JsonSerializer.SerializeToUtf8Bytes(new { Format = FormatVersion, Groups = index }, Json)));
        WriteArchive(Path.Combine(folder, "pictures.sdbx"), id, pictureFiles, null);

        // Sounds and music: the files as they are (the player reads WAV, MP3 and IFF 8SVX).
        var soundIndex = new List<object>();
        var soundFiles = new List<(string Name, byte[] Data)>();
        int nextSound = 0;
        foreach (var entry in resources?.Entries.Where(e => e.Kind is ResourceKind.Sound or ResourceKind.Music) ?? Enumerable.Empty<ResourceEntry>())
        {
            if (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath)) continue;
            string name = $"{nextSound++}{Path.GetExtension(entry.FilePath).ToLowerInvariant()}";
            soundFiles.Add((name, File.ReadAllBytes(entry.FilePath)));
            soundIndex.Add(new { entry.Id, entry.Name, File = name, entry.Volume, Music = entry.Kind == ResourceKind.Music });
        }
        WriteArchive(Path.Combine(folder, "sounds.sdbx"), id, soundFiles, soundIndex);

        // Videos (Play Video, the title's intro): the .mp4 files as they are - the player has them played by the machine's own decoder.
        var videoIndex = new List<object>();
        var videoFiles = new List<(string Name, byte[] Data)>();
        foreach (var video in videos?.Videos ?? new List<VideoEntry>())
        {
            if (string.IsNullOrEmpty(video.FilePath) || !File.Exists(video.FilePath)) continue;
            string name = $"{videoIndex.Count}{Path.GetExtension(video.FilePath).ToLowerInvariant()}";
            videoFiles.Add((name, File.ReadAllBytes(video.FilePath)));
            videoIndex.Add(new { video.Id, video.Name, File = name, video.Codec, video.Width, video.Height });
        }
        if (videoFiles.Count > 0) WriteArchive(Path.Combine(folder, "videos.sdbx"), id, videoFiles, videoIndex);
    }

    /// <summary>
    /// Book text as the player reads it: every page's frame text, and every
    /// Change Book Content's replacement, turned from rich text (XAML) into
    /// plain paragraphs by richText (the editor's converter); the whole-book
    /// stream the pages were cut from isn't needed.
    /// </summary>
    private static void PlainBookText(System.Text.Json.Nodes.JsonNode node, Func<string, string> richText)
    {
        if (node is System.Text.Json.Nodes.JsonArray array) { foreach (var item in array) if (item is not null) PlainBookText(item, richText); return; }
        if (node is not System.Text.Json.Nodes.JsonObject obj) return;
        if (obj["FrameTexts"] is System.Text.Json.Nodes.JsonObject texts)
            foreach (var key in texts.Select(kv => kv.Key).ToList())
                texts[key] = richText(texts[key]?.GetValue<string>() ?? "");
        if (obj.ContainsKey("FullContentXaml")) obj["FullContentXaml"] = "";
        if (obj["Type"] is System.Text.Json.Nodes.JsonValue type && type.TryGetValue(out int t)
            && (t == (int)EventCommandType.ChangeSelfBookContent || t == (int)EventCommandType.ChangeExternalBookContent)
            && obj["Parameters"] is System.Text.Json.Nodes.JsonObject parameters && parameters["ReplacementXaml"] is { } replacement)
        {
            parameters["ReplacementText"] = richText(replacement.GetValue<string>());
            parameters.Remove("ReplacementXaml");
        }
        foreach (var child in obj.Select(kv => kv.Value).ToList()) if (child is not null) PlainBookText(child, richText);
    }

    /// <summary>
    /// An archive of files (SRPK): "SRPK", version, the length of the index,
    /// the index (an encrypted container: each file's name, and where its own
    /// encrypted container starts after the index and how long it is - and,
    /// for sounds, the Sound list), then each file's container.
    /// </summary>
    public static void WriteArchive(string path, Guid id, List<(string Name, byte[] Data)> files, object? sounds)
    {
        var blobs = files.Select(f => ProjectContainer.Encrypt(id, f.Data)).ToList();
        long offset = 0;
        var entries = new List<object>();
        for (int i = 0; i < files.Count; i++)
        {
            entries.Add(new { files[i].Name, Offset = offset, Length = blobs[i].Length });
            offset += blobs[i].Length;
        }
        byte[] index = ProjectContainer.Encrypt(id, JsonSerializer.SerializeToUtf8Bytes(new { Format = FormatVersion, Entries = entries, Sounds = sounds }, Json));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);
        bw.Write(System.Text.Encoding.ASCII.GetBytes("SRPK"));
        bw.Write(FormatVersion);
        bw.Write(index.Length);
        bw.Write(index);
        foreach (var blob in blobs) bw.Write(blob);
    }
}
