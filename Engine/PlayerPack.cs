using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// The game as the Sandcastle player (the C engine for Linux, Mac, MorphOS,
/// AmigaOS 4 and Amiga 68k) reads it - written by Build for those platforms.
/// A folder holding:
///  - game.json: the project (maps with their houses and vehicles already
///    built in, characters, objects, settings...) and its books;
///  - sprites.json: every sprite group, sprite and frame (its picture's
///    file, its time), so a tile is frame N of its tileset sprite;
///  - pictures/: each frame's picture as it was (PNG, BMP, GIF...).
/// Plain JSON and picture files: nothing the player can't read on any
/// machine.
/// </summary>
public static class PlayerPack
{
    public const int FormatVersion = 1;

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static void Export(string folder, ManifestPayload project, BookDatabase books, SpriteDatabase sprites)
    {
        Directory.CreateDirectory(folder);
        // The project as it plays: a copy, every plotted house and vehicle built into its map (as a game starts).
        var game = JsonSerializer.Deserialize<ManifestPayload>(JsonSerializer.Serialize(project))!;
        foreach (var map in new[] { game.Map }.Concat(game.Worlds))
            if (map is not null) HouseBuilder.BuildInto(map, game.Houses);
        File.WriteAllText(Path.Combine(folder, "game.json"), JsonSerializer.Serialize(new { Format = FormatVersion, Project = game, Books = books }, Json));

        string pictures = Path.Combine(folder, "pictures");
        Directory.CreateDirectory(pictures);
        int next = 0;
        var copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string Picture(string source)
        {
            if (string.IsNullOrEmpty(source) || !File.Exists(source)) return "";
            if (copied.TryGetValue(source, out var known)) return known;
            string name = $"{next++}{Path.GetExtension(source).ToLowerInvariant()}";
            File.Copy(source, Path.Combine(pictures, name), overwrite: true);
            return copied[source] = name;
        }
        var index = sprites.Groups.Select(g => new
        {
            g.Name,
            Sprites = g.Sprites.Select(s => new
            {
                s.Name, s.Fps, s.Loop,
                NineSlice = s.NineSlice.Enabled ? new { s.NineSlice.Left, s.NineSlice.Top, s.NineSlice.Right, s.NineSlice.Bottom } : null, // a dialog frame's borders
                Frames = s.Frames.Select(f => new { File = Picture(f.FilePath), Ms = Math.Max(1, f.DurationMs) }).ToList()
            }).ToList()
        }).ToList();
        File.WriteAllText(Path.Combine(folder, "sprites.json"), JsonSerializer.Serialize(new { Format = FormatVersion, Groups = index }, Json));
    }
}
