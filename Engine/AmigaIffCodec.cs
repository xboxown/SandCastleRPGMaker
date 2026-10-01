using System.IO;
using System.Text;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Reads and writes the classic Amiga 8SVX sound format (an IFF FORM of
/// type "8SVX") - 8-bit signed mono PCM, uncompressed. That's a deliberate,
/// historically-accurate limitation, not a shortcut: real Amiga hardware
/// (the Paula audio chip) natively plays 8-bit mono samples; stereo on real
/// Amiga hardware came from playing two separate mono samples through the
/// left/right channel pairs, never from one interleaved-stereo file. Any
/// conversion to mono/8-bit happens before Write is called, not in here.
///
/// IFF chunks are big-endian and padded to an even byte boundary (a
/// chunk with an odd declared size gets one unused pad byte after it, not
/// counted in that chunk's own size field) - both are easy to get wrong
/// silently, so every offset here is computed from the chunk's declared
/// size rather than assumed from what fields happen to be read.
/// </summary>
public static class AmigaIffCodec
{
    public static void Write(string path, sbyte[] samples, int sampleRate)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var bw = new BinaryWriter(fs);

        const int vhdrSize = 20;
        int bodySize = samples.Length;
        bool bodyPad = bodySize % 2 != 0;

        int formContentSize =
            4 /* "8SVX" tag */ +
            8 + vhdrSize /* VHDR is always even-sized, no pad needed */ +
            8 + bodySize + (bodyPad ? 1 : 0);

        WriteId(bw, "FORM");
        WriteBE32(bw, (uint)formContentSize);
        WriteId(bw, "8SVX");

        WriteId(bw, "VHDR");
        WriteBE32(bw, vhdrSize);
        WriteBE32(bw, (uint)samples.Length); // oneShotHiSamples
        WriteBE32(bw, 0);                    // repeatHiSamples
        WriteBE32(bw, 0);                    // samplesPerHiCycle
        WriteBE16(bw, (ushort)Math.Clamp(sampleRate, 1, ushort.MaxValue));
        bw.Write((byte)1); // ctOctave
        bw.Write((byte)0); // sCompression - 0 = none (uncompressed)
        WriteBE32(bw, 0x10000); // volume, fixed-point 1.0

        WriteId(bw, "BODY");
        WriteBE32(bw, (uint)bodySize);
        foreach (var sample in samples) bw.Write((byte)sample);
        if (bodyPad) bw.Write((byte)0);
    }

    public static (sbyte[] Samples, int SampleRate) Read(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var br = new BinaryReader(fs);

        if (ReadId(br) != "FORM")
            throw new InvalidDataException($"\"{Path.GetFileName(path)}\" isn't an IFF file (missing FORM header).");
        uint formSize = ReadBE32(br);
        if (ReadId(br) != "8SVX")
            throw new InvalidDataException($"\"{Path.GetFileName(path)}\" is an IFF file, but not an 8SVX (Amiga sound) one.");

        long formEnd = fs.Position + (formSize - 4); // formSize covers everything from the "8SVX" tag onward, already consumed above
        int sampleRate = 8000;
        byte[]? bodyBytes = null;

        while (fs.Position + 8 <= formEnd && fs.Position + 8 <= fs.Length)
        {
            string chunkId = ReadId(br);
            uint chunkSize = ReadBE32(br);
            long chunkDataStart = fs.Position;

            if (chunkId == "VHDR" && chunkSize >= 14)
            {
                br.ReadBytes(4); // oneShotHiSamples
                br.ReadBytes(4); // repeatHiSamples
                br.ReadBytes(4); // samplesPerHiCycle
                sampleRate = ReadBE16(br);
            }
            else if (chunkId == "BODY")
            {
                bodyBytes = br.ReadBytes((int)chunkSize);
            }

            // Always advance by the chunk's DECLARED size (plus even-byte pad),
            // regardless of how many of its bytes were actually parsed above -
            // this is what keeps chunk types this reader doesn't care about
            // (NAME, ANNO, etc.) from desyncing every chunk that follows them.
            long consumed = fs.Position - chunkDataStart;
            long remaining = chunkSize - consumed;
            if (remaining > 0) fs.Seek(remaining, SeekOrigin.Current);
            if (chunkSize % 2 != 0) fs.Seek(1, SeekOrigin.Current);
        }

        if (bodyBytes is null)
            throw new InvalidDataException($"\"{Path.GetFileName(path)}\" has no BODY (sample data) chunk.");

        var samples = new sbyte[bodyBytes.Length];
        for (int i = 0; i < bodyBytes.Length; i++) samples[i] = unchecked((sbyte)bodyBytes[i]);
        return (samples, sampleRate);
    }

    private static void WriteId(BinaryWriter bw, string id) => bw.Write(System.Text.Encoding.ASCII.GetBytes(id));
    private static string ReadId(BinaryReader br) => System.Text.Encoding.ASCII.GetString(br.ReadBytes(4));

    private static void WriteBE32(BinaryWriter bw, uint value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        bw.Write(bytes);
    }

    private static void WriteBE16(BinaryWriter bw, ushort value)
    {
        var bytes = BitConverter.GetBytes(value);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        bw.Write(bytes);
    }

    private static uint ReadBE32(BinaryReader br)
    {
        var bytes = br.ReadBytes(4);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }

    private static ushort ReadBE16(BinaryReader br)
    {
        var bytes = br.ReadBytes(2);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return BitConverter.ToUInt16(bytes, 0);
    }
}
