using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Builds a standalone game: the engine's own program files copied to a
/// chosen folder; the project's .msrp (maps, characters, variables, settings)
/// merged into the game's .exe itself, appended after the program image where
/// Windows never looks; and its big media files - shapes.sdbx, audios.sdbx,
/// objects.sdbx, videos.sdbx - left beside it in "&lt;game&gt;_Data", so the
/// .exe doesn't grow to gigabytes. Those files stay married to this game: each
/// is stamped with the project's ID (checked on loading, as always), and the
/// .exe keeps each one's size and fingerprint - a file changed or swapped for
/// another game's is refused. When the .exe starts, it finds its own embedded
/// project and runs the game directly, with no editor.
///
/// Layout appended to the .exe: the .msrp's bytes, then a JSON index (the
/// data folder's name; each file: name, offset, length, fingerprint - offset
/// -1 for a file beside the .exe), then the index length (8 bytes), then a
/// 16-byte marker. Games built before (marker ...01) hold every file inside
/// and still run.
/// </summary>
public static class GameBundle
{
    private static readonly byte[] Marker = Encoding.ASCII.GetBytes("SANDCASTLEGAME02");
    private static readonly byte[] OldMarker = Encoding.ASCII.GetBytes("SANDCASTLEGAME01");

    private sealed record Entry(string Name, long Offset, long Length, string Fingerprint = "");
    private sealed record Index(string DataFolder, List<Entry> Files);

    /// <summary>The media files a project keeps beside its .msrp.</summary>
    private static readonly string[] MediaFileNames = { "shapes.sdbx", "audios.sdbx", "objects.sdbx", VideosFileName };

    /// <summary>The videos (Video Library) - the one file a project may not have (saved before videos existed): left out then.</summary>
    private const string VideosFileName = "videos.sdbx";

    /// <summary>The data folder beside a game's .exe.</summary>
    public static string DataFolderName(string exePath) => Path.GetFileNameWithoutExtension(exePath) + "_Data";

    /// <summary>
    /// Copies the engine's program files (everything in the editor's own
    /// folder except debug symbols) next to targetExePath, makes the editor's
    /// .exe the game's .exe under the chosen name, merges the .msrp into it
    /// and puts the media files in its data folder.
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

        string msrpPath = Path.Combine(projectFolder, msrpFileName);
        if (!File.Exists(msrpPath)) throw new FileNotFoundException($"The project file \"{msrpFileName}\" is missing - save the project and try again.", msrpPath);
        string dataFolderName = DataFolderName(targetExePath);
        string dataFolder = Path.Combine(targetFolder, dataFolderName);
        Directory.CreateDirectory(dataFolder);

        var files = new List<Entry>();
        foreach (var name in MediaFileNames)
        {
            string source = Path.Combine(projectFolder, name);
            string destination = Path.Combine(dataFolder, name);
            if (!File.Exists(source))
            {
                if (name == VideosFileName) { if (File.Exists(destination)) File.Delete(destination); continue; }
                throw new FileNotFoundException($"The project file \"{name}\" is missing - save the project and try again.", source);
            }
            File.Copy(source, destination, overwrite: true);
            files.Add(new Entry(name, -1, new FileInfo(destination).Length, Fingerprint(destination)));
        }

        using var exe = new FileStream(targetExePath, FileMode.Append, FileAccess.Write);
        using (var source = File.OpenRead(msrpPath))
        {
            files.Insert(0, new Entry(msrpFileName, exe.Position, source.Length));
            source.CopyTo(exe);
        }
        byte[] indexBytes = JsonSerializer.SerializeToUtf8Bytes(new Index(dataFolderName, files));
        exe.Write(indexBytes);
        exe.Write(BitConverter.GetBytes((long)indexBytes.Length));
        exe.Write(Marker);
    }

    /// <summary>A quick fingerprint of a (possibly huge) file: its length, its first and its last megabyte.</summary>
    private static string Fingerprint(string path)
    {
        const int chunk = 1 << 20;
        using var file = File.OpenRead(path);
        using var sha = SHA256.Create();
        var buffer = new byte[chunk];
        int read = file.Read(buffer, 0, chunk);
        sha.TransformBlock(BitConverter.GetBytes(file.Length), 0, sizeof(long), null, 0);
        sha.TransformBlock(buffer, 0, read, null, 0);
        if (file.Length > chunk)
        {
            file.Seek(Math.Max(chunk, file.Length - chunk), SeekOrigin.Begin);
            read = file.Read(buffer, 0, chunk);
            sha.TransformBlock(buffer, 0, read, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    /// <summary>
    /// If this program has a game merged into it, unpacks its .msrp to a
    /// private folder and returns its path, and the folder the media files
    /// are read from (checked: each must be this game's own); otherwise false
    /// (it's the editor). Throws when the data files are missing or aren't
    /// this game's - the message says which.
    /// </summary>
    public static bool TryExtractEmbeddedGame(string? exePath, out string msrpPath, out string? dataFolder)
    {
        msrpPath = string.Empty;
        dataFolder = null;
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath)) return false;

        using var exe = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        int tail = Marker.Length + sizeof(long);
        if (exe.Length < tail) return false;
        exe.Seek(-tail, SeekOrigin.End);
        var tailBytes = new byte[tail];
        exe.ReadExactly(tailBytes);
        var marker = tailBytes.AsSpan(sizeof(long));
        bool current = marker.SequenceEqual(Marker);
        if (!current && !marker.SequenceEqual(OldMarker)) return false;

        long indexLength = BitConverter.ToInt64(tailBytes, 0);
        if (indexLength <= 0 || indexLength > exe.Length - tail) return false;
        exe.Seek(-(tail + indexLength), SeekOrigin.End);
        var indexBytes = new byte[indexLength];
        exe.ReadExactly(indexBytes);
        var index = current ? JsonSerializer.Deserialize<Index>(indexBytes) : new Index("", JsonSerializer.Deserialize<List<Entry>>(indexBytes) ?? new List<Entry>());
        if (index is null || index.Files.Count == 0) return false;

        // A private folder per built game (by its file's identity), so two different games never share one.
        string folder = Path.Combine(Path.GetTempPath(), "SandcastleRpgMaker", "game_" + Math.Abs((exePath + new FileInfo(exePath).Length).GetHashCode()).ToString());
        Directory.CreateDirectory(folder);
        foreach (var entry in index.Files.Where(e => e.Offset >= 0))
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
        msrpPath = Path.Combine(folder, Path.GetFileName(index.Files[0].Name));
        if (!current) return true; // an older game: everything was inside

        // The media files beside it: present, and this game's own.
        dataFolder = Path.Combine(Path.GetDirectoryName(exePath)!, index.DataFolder);
        foreach (var entry in index.Files.Where(e => e.Offset < 0))
        {
            string path = Path.Combine(dataFolder, entry.Name);
            if (!File.Exists(path))
                throw new GameDataException($"The game's data file \"{index.DataFolder}\\{entry.Name}\" is missing. Keep the \"{index.DataFolder}\" folder next to the game's .exe, as it was built.");
            if (new FileInfo(path).Length != entry.Length || Fingerprint(path) != entry.Fingerprint)
                throw new GameDataException($"The game's data file \"{index.DataFolder}\\{entry.Name}\" isn't this game's own (it was changed, or comes from another game). Copy the game's folder again as it was built.");
        }
        return true;
    }
}

/// <summary>A built game's data files are missing or aren't its own - the message is for the player.</summary>
public class GameDataException : Exception
{
    public GameDataException(string message) : base(message) { }
}
