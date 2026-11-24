using System.IO;
using System.Text.Json;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Sprite Composer's libraries on this PC (not in a project, so every project can use them):
/// %AppData%\SandcastleRpgMaker\SpriteComposer\&lt;Style&gt;-v&lt;N&gt;.json - Ultima 6, Ultima 7 and Ultima 8, each in versions.
/// A new version is a copy: changing it never changes an older one, nor any sprite already made.
/// </summary>
public static class ComposerLibraries
{
    public static readonly string[] Styles = { "Ultima 6", "Ultima 7", "Ultima 8" };
    public static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SandcastleRpgMaker", "SpriteComposer");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    /// <summary>Every library there is (each style gets its built-in version 1 the first time).</summary>
    public static List<ComposerLibrary> All()
    {
        Directory.CreateDirectory(Folder);
        var list = new List<ComposerLibrary>();
        foreach (var file in Directory.GetFiles(Folder, "*.json"))
        {
            try { if (JsonSerializer.Deserialize<ComposerLibrary>(File.ReadAllText(file)) is { } lib) list.Add(lib); }
            catch (Exception) { /* a damaged file is skipped, not lost */ }
        }
        foreach (var style in Styles.Where(s => list.All(l => l.Style != s)))
        {
            var lib = ComposerLibrary.BuiltIn(style);
            Save(lib);
            list.Add(lib);
        }
        return list.OrderBy(l => Array.IndexOf(Styles, l.Style) is var i && i < 0 ? 99 : i).ThenBy(l => l.Style).ThenBy(l => l.Version).ToList();
    }

    public static void Save(ComposerLibrary lib)
    {
        Directory.CreateDirectory(Folder);
        File.WriteAllText(Path.Combine(Folder, lib.FileName), JsonSerializer.Serialize(lib, Json));
    }

    /// <summary>A copy as the style's next version, saved.</summary>
    public static ComposerLibrary NewVersion(ComposerLibrary from)
    {
        var copy = from.DeepCopy();
        copy.Version = All().Where(l => l.Style == from.Style).Select(l => l.Version).DefaultIfEmpty(0).Max() + 1;
        copy.Notes = $"From version {from.Version}. " + from.Notes;
        Save(copy);
        return copy;
    }
}
