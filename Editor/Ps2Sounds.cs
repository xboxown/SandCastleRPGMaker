using System.IO;
using System.Text.Json;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Build for PlayStation 2 only: the game's sounds made easy for the PS2. Every MP3 and WAV goes on the disc as IMA ADPCM
/// (a WAV file, stereo, 44,100 Hz, 4 bits a sample): decoding it is a few additions a sample - nothing like MP3 - and it's
/// a quarter of plain 16-bit sound to read off the disc.
/// History: b576 put plain 16-bit sound on the disc (176 KB every second of music): a real PS2 then had to load a new
/// 128 KB piece of the disc every 3/4 of a second, and from a slow disc the music hiccuped every second (b578). IMA ADPCM:
/// 44 KB a second - a piece every 3 seconds.
/// The project's own sound files are never changed, and Windows builds are untouched (PlayerPack.Export as it is): this
/// writes the PS2's own sounds.sdbx over the one Export made, in the same archive format. Other sounds (8SVX...) as they are.
/// </summary>
internal static class Ps2Sounds
{
    public const int Rate = 44100;
    private const int BlockAlign = 2048;                                  // bytes a block, stereo
    private const int FramesPerBlock = (BlockAlign - 8) * 8 / (4 * 2) + 1;  // 2041: the header's sample and 2040 more

    /// <summary>Rewrites folder/sounds.sdbx; how many files were made into ADPCM. work: a folder for the files on the way.</summary>
    public static int Rewrite(string folder, Guid id, ResourceDatabase? resources, string work, IProgress<string>? status)
    {
        Directory.CreateDirectory(work);
        var soundIndex = new List<object>();
        var parts = new List<(string Name, string Encrypted, long Length)>();
        int nextSound = 0, converted = 0;
        try
        {
            // the same files, in the same order and with the same numbers, as PlayerPack.Export
            var entries = resources?.Entries.Where(e => e.Kind is ResourceKind.Sound or ResourceKind.Music).ToList() ?? new List<ResourceEntry>();
            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.FilePath) || !File.Exists(entry.FilePath)) continue;
                int number = nextSound++;
                string ext = Path.GetExtension(entry.FilePath).ToLowerInvariant();
                byte[] data;
                string? made = ext is ".mp3" or ".wav" ? MakeAdpcm(entry.FilePath, Path.Combine(work, number + ".ima.wav"), entry.Name, status) : null;
                if (made is not null) { data = File.ReadAllBytes(made); File.Delete(made); ext = ".wav"; converted++; }
                else data = File.ReadAllBytes(entry.FilePath);
                string name = $"{number}{ext}";
                byte[] sealedBytes = ProjectContainer.Encrypt(id, data);
                data = Array.Empty<byte>();
                string enc = Path.Combine(work, number + ".enc");
                File.WriteAllBytes(enc, sealedBytes);
                parts.Add((name, enc, sealedBytes.LongLength));
                soundIndex.Add(new { entry.Id, entry.Name, File = name, entry.Volume, Music = entry.Kind == ResourceKind.Music });
            }

            // the archive (PlayerPack.WriteArchive's format): "SRPK", version, the index's length, the index, each file
            long offset = 0;
            var list = new List<object>();
            foreach (var p in parts) { list.Add(new { p.Name, Offset = offset, Length = (int)p.Length }); offset += p.Length; }
            byte[] index = ProjectContainer.Encrypt(id, JsonSerializer.SerializeToUtf8Bytes(new { Format = PlayerPack.FormatVersion, Entries = list, Sounds = soundIndex }, new JsonSerializerOptions { WriteIndented = false }));
            string path = Path.Combine(folder, "sounds.sdbx"), temp = path + ".ps2";
            using (var fs = new FileStream(temp, FileMode.Create, FileAccess.Write))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(System.Text.Encoding.ASCII.GetBytes("SRPK"));
                bw.Write(PlayerPack.FormatVersion);
                bw.Write(index.Length);
                bw.Write(index);
                bw.Flush();
                foreach (var p in parts) using (var input = File.OpenRead(p.Encrypted)) input.CopyTo(fs);
            }
            File.Move(temp, path, overwrite: true);
            return converted;
        }
        finally
        {
            foreach (var p in parts) try { File.Delete(p.Encrypted); } catch { /* the work folder goes anyway */ }
        }
    }

    /// <summary>The file as IMA ADPCM stereo at 44,100 Hz (a WAV) at target; null if it can't be read (then it goes as it is).</summary>
    private static string? MakeAdpcm(string source, string target, string name, IProgress<string>? status)
    {
        try
        {
            status?.Report($"PlayStation 2: making \"{name}\" light sound for the PS2...");
            var pcm = new List<short>(1 << 20); // interleaved left, right
            using (var reader = new AudioFileReader(source))
            {
                ISampleProvider samples = reader;
                if (samples.WaveFormat.Channels == 1) samples = new MonoToStereoSampleProvider(samples);
                else if (samples.WaveFormat.Channels != 2) return null;
                if (samples.WaveFormat.SampleRate != Rate) samples = new WdlResamplingSampleProvider(samples, Rate);
                var buffer = new float[16384];
                int got;
                while ((got = samples.Read(buffer, 0, buffer.Length)) > 0)
                    for (int i = 0; i < got; i++)
                    {
                        float s = buffer[i] * 32767f;
                        pcm.Add((short)(s > 32767f ? 32767 : s < -32768f ? -32768 : MathF.Round(s)));
                    }
            }
            if (pcm.Count < 2) return null;
            using var fs = new FileStream(target, FileMode.Create, FileAccess.Write);
            WriteImaWav(fs, pcm);
            return target;
        }
        catch
        {
            try { if (File.Exists(target)) File.Delete(target); } catch { }
            return null;
        }
    }

    // ---------------- IMA ADPCM (Microsoft / DVI, format 0x11), as dr_wav reads it ----------------

    private static readonly int[] IndexTable = { -1, -1, -1, -1, 2, 4, 6, 8, -1, -1, -1, -1, 2, 4, 6, 8 };
    private static readonly int[] StepTable =
    {
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
        130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060, 1166,
        1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484, 7132, 7845,
        8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767
    };

    /// <summary>One sample: the nibble nearest it, the predictor and step index moved exactly as the decoder will.</summary>
    private static int Encode(int sample, ref int predictor, ref int index)
    {
        int step = StepTable[index], diff = sample - predictor, nibble = 0;
        if (diff < 0) { nibble = 8; diff = -diff; }
        if (diff >= step) { nibble |= 4; diff -= step; }
        if (diff >= step >> 1) { nibble |= 2; diff -= step >> 1; }
        if (diff >= step >> 2) nibble |= 1;
        // the decoder's own sum (dr_wav), so both sides stay together
        int d = step >> 3;
        if ((nibble & 1) != 0) d += step >> 2;
        if ((nibble & 2) != 0) d += step >> 1;
        if ((nibble & 4) != 0) d += step;
        if ((nibble & 8) != 0) d = -d;
        predictor = Math.Clamp(predictor + d, -32768, 32767);
        index = Math.Clamp(index + IndexTable[nibble], 0, 88);
        return nibble;
    }

    /// <summary>The WAV: fmt (0x11, block 2048, 2041 frames a block), fact, data. The last block is cut short (whole groups of
    /// 8 frames, the end padded with quiet) - dr_wav counts the frames from the data's length.</summary>
    private static void WriteImaWav(Stream output, List<short> pcm)
    {
        int frames = pcm.Count / 2;
        var data = new MemoryStream();
        int[] predictor = new int[2], index = new int[2];
        short At(int frame, int channel) => frame < frames ? pcm[frame * 2 + channel] : (short)0;
        for (int start = 0; start < frames; start += FramesPerBlock)
        {
            int inBlock = Math.Min(FramesPerBlock, frames - start);
            int groups = (inBlock - 1 + 7) / 8; // groups of 8 frames after the header's one
            for (int c = 0; c < 2; c++)
            {
                predictor[c] = At(start, c);
                data.WriteByte((byte)(predictor[c] & 0xFF)); data.WriteByte((byte)((predictor[c] >> 8) & 0xFF));
                data.WriteByte((byte)index[c]); data.WriteByte(0);
            }
            for (int g = 0; g < groups; g++)
                for (int c = 0; c < 2; c++)
                    for (int b = 0; b < 4; b++)
                    {
                        int f = start + 1 + g * 8 + b * 2;
                        int lo = Encode(At(f, c), ref predictor[c], ref index[c]);
                        int hi = Encode(At(f + 1, c), ref predictor[c], ref index[c]);
                        data.WriteByte((byte)(lo | hi << 4));
                    }
        }
        long dataLength = data.Length;
        int written = (int)(dataLength / BlockAlign) * FramesPerBlock + (dataLength % BlockAlign == 0 ? 0 : (int)((dataLength % BlockAlign - 8) * 2 / 2 + 1));
        using var w = new BinaryWriter(output, System.Text.Encoding.ASCII, leaveOpen: true);
        w.Write("RIFF"u8.ToArray()); w.Write((uint)(4 + (8 + 20) + (8 + 4) + 8 + dataLength + (dataLength & 1)));
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(20);
        w.Write((ushort)0x11); w.Write((ushort)2); w.Write(Rate);
        w.Write((int)((long)Rate * BlockAlign / FramesPerBlock)); // bytes a second
        w.Write((ushort)BlockAlign); w.Write((ushort)4); w.Write((ushort)2); w.Write((ushort)FramesPerBlock);
        w.Write("fact"u8.ToArray()); w.Write(4); w.Write(written);
        w.Write("data"u8.ToArray()); w.Write((uint)dataLength);
        data.Position = 0; data.CopyTo(output);
        if ((dataLength & 1) != 0) output.WriteByte(0);
    }
}
