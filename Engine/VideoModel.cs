using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// One video of the project (Video Library): an .mp4 in H.264, H.265 (HEVC)
/// or AV1, played by Windows' own decoder - never one bundled with the game,
/// so no codec licence falls on the game's maker. HEVC plays only where the
/// player has Microsoft's HEVC Video Extensions.
/// </summary>
public class VideoEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Video";
    /// <summary>The file on disk: the one added, or after a load, the one unpacked from videos.sdbx.</summary>
    public string FilePath { get; set; } = string.Empty;
    /// <summary>"H.264", "H.265 (HEVC)" or "AV1", as found in the file when it was added.</summary>
    public string Codec { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double DurationSeconds { get; set; }
}

/// <summary>The project's videos (their names and settings live in the .msrp; their bytes in videos.sdbx).</summary>
public class VideoDatabase
{
    public List<VideoEntry> Videos { get; set; } = new();

    private int _nextId = 1;

    public VideoEntry Add(string name, string filePath, VideoInfo info)
    {
        var video = new VideoEntry { Id = _nextId++, Name = name, FilePath = filePath, Codec = info.Codec, Width = info.Width, Height = info.Height, DurationSeconds = info.DurationSeconds };
        Videos.Add(video);
        return video;
    }

    public void SyncNextIdAfterLoad() => _nextId = Videos.Count == 0 ? 1 : Videos.Max(v => v.Id) + 1;
}

/// <summary>What a video file holds, read from its MP4 boxes.</summary>
public class VideoInfo
{
    public string Codec { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public double DurationSeconds { get; set; }
    public bool IsHevc => Codec.StartsWith("H.265", StringComparison.Ordinal);
}

/// <summary>Reads an .mp4's video track (its codec, size and length) without loading the file - the boxes moov > trak > mdia > minf > stbl > stsd.</summary>
public static class VideoProbe
{
    public static readonly string[] Extensions = { ".mp4", ".m4v" };

    /// <summary>The video's codec, size and length; null with the reason if it isn't an .mp4 in H.264, H.265 or AV1.</summary>
    public static VideoInfo? Probe(string path, out string problem)
    {
        problem = string.Empty;
        if (!Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()))
        {
            problem = "Only .mp4 (or .m4v) videos can be used.";
            return null;
        }
        try
        {
            using var stream = File.OpenRead(path);
            var info = new VideoInfo();
            string fourCc = string.Empty;
            Scan(stream, 0, stream.Length, info, ref fourCc, null);
            if (fourCc.Length == 0)
            {
                problem = "No video track was found in this file.";
                return null;
            }
            info.Codec = fourCc switch
            {
                "avc1" or "avc3" => "H.264",
                "hvc1" or "hev1" => "H.265 (HEVC)",
                "av01" => "AV1",
                _ => string.Empty
            };
            if (info.Codec.Length == 0)
            {
                problem = $"This video uses the \"{fourCc}\" codec, which can't be used - save it as H.264, H.265 (HEVC) or AV1 in an .mp4.";
                return null;
            }
            return info;
        }
        catch (Exception ex)
        {
            problem = $"The file couldn't be read as an .mp4: {ex.Message}";
            return null;
        }
    }

    private sealed class Track
    {
        public string Handler = string.Empty;
        public string FourCc = string.Empty;
        public int Width, Height;
    }

    private static void Scan(Stream stream, long start, long end, VideoInfo info, ref string videoFourCc, Track? track)
    {
        long at = start;
        var header = new byte[16];
        while (at + 8 <= end)
        {
            stream.Seek(at, SeekOrigin.Begin);
            stream.ReadExactly(header, 0, 8);
            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = Encoding.ASCII.GetString(header, 4, 4);
            long payload = at + 8;
            if (size == 1)
            {
                stream.ReadExactly(header, 8, 8);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(8));
                payload = at + 16;
            }
            else if (size == 0) size = end - at;
            if (size < 8 || at + size > end) return; // broken box: stop here
            long boxEnd = at + size;

            switch (type)
            {
                case "moov" or "mdia" or "minf" or "stbl":
                    Scan(stream, payload, boxEnd, info, ref videoFourCc, track);
                    break;
                case "trak":
                {
                    var inner = new Track();
                    Scan(stream, payload, boxEnd, info, ref videoFourCc, inner);
                    if (inner.Handler == "vide" && inner.FourCc.Length > 0 && videoFourCc.Length == 0)
                    {
                        videoFourCc = inner.FourCc;
                        info.Width = inner.Width;
                        info.Height = inner.Height;
                    }
                    break;
                }
                case "mvhd":
                {
                    var b = Read(stream, payload, (int)Math.Min(32, boxEnd - payload));
                    if (b[0] == 1 && b.Length >= 32)
                    {
                        uint scale = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(20));
                        ulong duration = BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(24));
                        if (scale > 0) info.DurationSeconds = duration / (double)scale;
                    }
                    else if (b.Length >= 20)
                    {
                        uint scale = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(12));
                        uint duration = BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(16));
                        if (scale > 0) info.DurationSeconds = duration / (double)scale;
                    }
                    break;
                }
                case "tkhd" when track is not null && boxEnd - payload >= 8:
                {
                    var b = Read(stream, boxEnd - 8, 8); // width and height, 16.16 fixed point, end the box
                    track.Width = (int)(BinaryPrimitives.ReadUInt32BigEndian(b) >> 16);
                    track.Height = (int)(BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(4)) >> 16);
                    break;
                }
                case "hdlr" when track is not null && boxEnd - payload >= 12:
                    track.Handler = Encoding.ASCII.GetString(Read(stream, payload + 8, 4));
                    break;
                case "stsd" when track is not null && boxEnd - payload >= 16:
                    track.FourCc = Encoding.ASCII.GetString(Read(stream, payload + 12, 4)); // after version/flags, entry count, the first entry's size
                    break;
            }
            at = boxEnd;
        }
    }

    private static byte[] Read(Stream stream, long at, int count)
    {
        var bytes = new byte[count];
        stream.Seek(at, SeekOrigin.Begin);
        stream.ReadExactly(bytes);
        return bytes;
    }
}

/// <summary>
/// videos.sdbx: every video's bytes, encrypted like the other project files
/// (the project's key) but streamed - a video can be hundreds of MB, far too
/// big for the JSON the other .sdbx files hold. Layout: "SRPV", version,
/// project id, count; then per video its id, IV, cipher length, cipher.
/// </summary>
public static class VideoStore
{
    private const int FormatVersion = 1;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SRPV");

    public static void Save(string path, Guid projectId, VideoDatabase database)
    {
        string temp = path + ".saving";
        using (var file = new FileStream(temp, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(file, Encoding.UTF8, leaveOpen: true))
        {
            var present = database.Videos.Where(v => File.Exists(v.FilePath)).ToList();
            writer.Write(Magic);
            writer.Write(FormatVersion);
            writer.Write(projectId.ToByteArray());
            writer.Write(present.Count);
            using var aes = Aes.Create();
            aes.Key = ProjectContainer.DeriveKey(projectId);
            foreach (var video in present)
            {
                aes.GenerateIV();
                writer.Write(video.Id);
                writer.Write(aes.IV.Length);
                writer.Write(aes.IV);
                writer.Flush();
                long lengthAt = file.Position;
                writer.Write(0L);
                writer.Flush();
                long cipherStart = file.Position;
                using (var source = File.OpenRead(video.FilePath))
                using (var crypto = new CryptoStream(file, aes.CreateEncryptor(), CryptoStreamMode.Write, leaveOpen: true))
                    source.CopyTo(crypto);
                long cipherEnd = file.Position;
                file.Seek(lengthAt, SeekOrigin.Begin);
                writer.Write(cipherEnd - cipherStart);
                writer.Flush();
                file.Seek(cipherEnd, SeekOrigin.Begin);
            }
        }
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>Unpacks every video to extractFolder and points its entry there. A project saved before videos existed has no videos.sdbx: nothing to do.</summary>
    public static void Load(string path, Guid projectId, VideoDatabase database, string extractFolder)
    {
        database.SyncNextIdAfterLoad();
        if (!File.Exists(path)) return;
        Directory.CreateDirectory(extractFolder);
        foreach (var old in Directory.EnumerateFiles(extractFolder)) { try { File.Delete(old); } catch { /* still open from before: overwritten below if it's needed */ } }

        using var file = File.OpenRead(path);
        using var reader = new BinaryReader(file);
        if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic))
            throw new ProjectFileException($"\"{Path.GetFileName(path)}\" isn't a Sandcastle video file.");
        int version = reader.ReadInt32();
        if (version != FormatVersion)
            throw new ProjectFileException($"\"{Path.GetFileName(path)}\" was made with a different version of Sandcastle RPG Maker (video format {version}, this copy reads format {FormatVersion}).");
        if (new Guid(reader.ReadBytes(16)) != projectId)
            throw new ProjectFileException($"\"{Path.GetFileName(path)}\" doesn't belong to this project. Project files can't be mixed between projects.");

        int count = reader.ReadInt32();
        using var aes = Aes.Create();
        aes.Key = ProjectContainer.DeriveKey(projectId);
        for (int i = 0; i < count; i++)
        {
            int id = reader.ReadInt32();
            aes.IV = reader.ReadBytes(reader.ReadInt32());
            long length = reader.ReadInt64();
            long next = file.Position + length;
            var video = database.Videos.FirstOrDefault(v => v.Id == id);
            if (video is not null)
            {
                string target = Path.Combine(extractFolder, $"video_{id}.mp4");
                using (var output = new FileStream(target, FileMode.Create, FileAccess.Write))
                using (var decryptor = aes.CreateDecryptor())
                    Decrypt(file, length, decryptor, output);
                video.FilePath = target;
            }
            file.Seek(next, SeekOrigin.Begin);
        }
    }

    private static void Decrypt(Stream input, long length, ICryptoTransform decryptor, Stream output)
    {
        var buffer = new byte[1 << 20]; // a multiple of the 16-byte block
        var plain = new byte[buffer.Length + 16];
        long left = length;
        while (left > 0)
        {
            int count = (int)Math.Min(buffer.Length, left);
            input.ReadExactly(buffer, 0, count);
            left -= count;
            if (left == 0)
            {
                output.Write(decryptor.TransformFinalBlock(buffer, 0, count));
                break;
            }
            int written = decryptor.TransformBlock(buffer, 0, count, plain, 0);
            output.Write(plain, 0, written);
        }
    }
}
