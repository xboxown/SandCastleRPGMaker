using System.IO;
using System.Text;
using System.Text.Json;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Builds a standalone game: the engine's own program files copied to a
/// chosen folder, with the project's data files (.msrp - maps, PCs,
/// variables, stats, factions, settings - plus shapes.sdbx, audios.sdbx and
/// objects.sdbx) merged into the game's .exe itself, appended after the
/// program image where Windows never looks. When that .exe starts, it finds
/// its own embedded project and runs the game directly, with no editor.
///
/// Layout appended to the .exe: each file's bytes, then a JSON index
/// (name, offset, length), then the index length (8 bytes), then a 16-byte
/// marker. The project files stay exactly as saved (still encrypted).
/// </summary>
public static class GameBundle
{
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("SANDCASTLEGAME01");

    private sealed record Entry(string Name, long Offset, long Length);

    /// <summary>The project files that make up a game, in the order they're stored.</summary>
    public static string[] ProjectFileNames(string msrpFileName) => new[] { msrpFileName, "shapes.sdbx", "audios.sdbx", "objects.sdbx", VideosFileName };

    /// <summary>The videos (Video Library) - the one file a project may not have (saved before videos existed): left out then.</summary>
    private const string VideosFileName = "videos.sdbx";

    /// <summary>
    /// Copies the engine's program files (everything in the editor's own
    /// folder except debug symbols) next to targetExePath, makes the editor's
    /// .exe the game's .exe under the chosen name, and merges the project's
    /// data files into it.
    /// </summary>
    public static void Build(string engineFolder, string engineExePath, string targetExePath, string projectFolder, string msrpFileName)
    {
        string targetFolder = Path.GetDirectoryName(targetExePath)!;
        Directory.CreateDirectory(targetFolder);

        foreach (var source in Directory.EnumerateFiles(engineFolder, "*", SearchOption.AllDirectories))
        {
            if (source.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) continue;
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(engineExePath), StringComparison.OrdinalIgnoreCase)) continue; // copied below under the game's own name
            string relative = Path.GetRelativePath(engineFolder, source);
            string destination = Path.Combine(targetFolder, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(source, destination, overwrite: true);
        }

        File.Copy(engineExePath, targetExePath, overwrite: true);

        using var exe = new FileStream(targetExePath, FileMode.Append, FileAccess.Write);
        var index = new List<Entry>();
        foreach (var name in ProjectFileNames(msrpFileName))
        {
            string path = Path.Combine(projectFolder, name);
            if (!File.Exists(path))
            {
                if (name == VideosFileName) continue;
                throw new FileNotFoundException($"The project file \"{name}\" is missing - save the project and try again.", path);
            }
            using var source = File.OpenRead(path); // streamed: videos can be bigger than any one array
            index.Add(new Entry(name, exe.Position, source.Length));
            source.CopyTo(exe);
        }
        byte[] indexBytes = JsonSerializer.SerializeToUtf8Bytes(index);
        exe.Write(indexBytes);
        exe.Write(BitConverter.GetBytes((long)indexBytes.Length));
        exe.Write(Marker);
    }

    /// <summary>
    /// If this program has a game merged into it, unpacks the project files to
    /// a private folder and returns the .msrp's path; otherwise false (it's
    /// the editor).
    /// </summary>
    public static bool TryExtractEmbeddedGame(string? exePath, out string msrpPath)
    {
        msrpPath = string.Empty;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;

        using var exe = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        int tail = Marker.Length + sizeof(long);
        if (exe.Length < tail) return false;
        exe.Seek(-tail, SeekOrigin.End);
        var tailBytes = new byte[tail];
        exe.ReadExactly(tailBytes);
        if (!tailBytes.AsSpan(sizeof(long)).SequenceEqual(Marker)) return false;

        long indexLength = BitConverter.ToInt64(tailBytes, 0);
        if (indexLength <= 0 || indexLength > exe.Length - tail) return false;
        exe.Seek(-(tail + indexLength), SeekOrigin.End);
        var indexBytes = new byte[indexLength];
        exe.ReadExactly(indexBytes);
        var index = JsonSerializer.Deserialize<List<Entry>>(indexBytes);
        if (index is null || index.Count == 0) return false;

        // A private folder per built game (by its file's identity), so two different games never share one.
        string folder = Path.Combine(Path.GetTempPath(), "SandcastleRpgMaker", "game_" + Math.Abs((exePath + new FileInfo(exePath).Length).GetHashCode()).ToString());
        Directory.CreateDirectory(folder);
        foreach (var entry in index)
        {
            exe.Seek(entry.Offset, SeekOrigin.Begin);
            using var output = new FileStream(Path.Combine(folder, Path.GetFileName(entry.Name)), FileMode.Create, FileAccess.Write);
            var buffer = new byte[1 << 20];
            for (long left = entry.Length; left > 0;)
            {
                int count = (int)Math.Min(buffer.Length, left);
                exe.ReadExactly(buffer, 0, count);
                output.Write(buffer, 0, count);
                left -= count;
            }
        }
        msrpPath = Path.Combine(folder, Path.GetFileName(index[0].Name));
        return true;
    }
}
