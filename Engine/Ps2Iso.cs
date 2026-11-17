using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Game > Build > PlayStation 2: the game as a disc image (ISO) the PS2 boots.
///
/// The disc: ISO 9660 (level 1 names, what the PS2's disc driver reads) with the boot program and SYSTEM.CNF at the top and
/// the game's data in \DATA\ (the player opens them by these short names - Player/src/platform_ps2.c: short_name, the same
/// rule as Ps2Name here). Beside it a UDF bridge, laid out as mkisofs lays it out (its Partition Descriptor at sectors 34 and
/// 50), so the ESR patch fits it exactly - and the patch is put on while writing, every time: the original Partition
/// Descriptors kept at sectors 14 and 15, the partition moved to sector 128, and the 12 sectors of DVD-Video there
/// (VIDEO_TS, AUDIO_TS) that make the drive take the disc as a DVD-Video. ESR (ffgriever's, r10f; launched from FreeMcBoot)
/// then starts it. The ISO 9660 side, which the game reads, is untouched by the patch.
///
/// Size: the image is exactly the game's files (each rounded up to a 2048-byte sector) plus the disc's own structures. Most
/// a PS2 disc holds here: 8 GB (MaxBytes) - more than a single-layer DVD (4.7 GB) needs a dual-layer DVD.
/// </summary>
public static class Ps2Iso
{
    public const int Sector = 2048;
    /// <summary>The most a PlayStation 2 disc built here may be: 8 GB (a dual-layer DVD holds 8.5).</summary>
    public const long MaxBytes = 8_000_000_000L;
    /// <summary>More than this needs a dual-layer DVD (DVD+R DL); a single-layer DVD-R holds 4.7 GB.</summary>
    public const long SingleLayerBytes = 4_700_372_992L;
    /// <summary>ISO 9660 can't hold one file of 4 GB or more.</summary>
    public const long MaxFileBytes = 0xFFFF_FFFFL;

    /// <summary>The ESR patch's DVD-Video sectors: 12 sectors at sector 128 (SHA-256 checked - the same block every ESR patcher writes).</summary>
    public const string EsrBlockSha256 = "D61083E8BC90A959C21958E46216A8531C2095C2F6F780B779D9489F3FD5A845";
    private const int EsrLba = 128, EsrSectors = 12;

    private const int MainVds = 32, ReserveVds = 48, Lvid = 64, Avdp = 256, PathL = 257, PathM = 258, RootDir = 259;

    public sealed record DiscFile(string DiscName, string SourcePath, long Length);

    /// <summary>A file's name on the disc: upper case, 8 letters and a 3-letter ending - the player's short_name (platform_ps2.c).</summary>
    public static string Ps2Name(string name)
    {
        int dot = name.LastIndexOf('.');
        string stem = dot >= 0 ? name[..dot] : name, ext = dot >= 0 ? name[(dot + 1)..] : "";
        static string Clean(string s, int most) => new string(s.Where(c => char.IsAsciiLetterOrDigit(c) || c == '_').Select(char.ToUpperInvariant).Take(most).ToArray());
        string b = Clean(stem, 8), e = Clean(ext, 3);
        return e.Length > 0 ? b + "." + e : b;
    }

    /// <summary>The boot program's name, as a PS2 disc's ("SRPG_123.45"), made from the project's id so each game keeps its own (OPL and memory card tools show it).</summary>
    public static string BootName(string projectId)
    {
        uint n = 0;
        foreach (char c in projectId ?? "") n = n * 31 + c;
        n %= 100000;
        return $"SRPG_{n / 100:000}.{n % 100:00}";
    }

    /// <summary>How big the disc will be for these files (bytes), before writing it.</summary>
    public static long ImageBytes(IReadOnlyList<DiscFile> data, long elfBytes, string bootName)
    {
        long sectors = RootDir + DirSectors(new[] { "DATA", bootName + ";1", "SYSTEM.CNF;1" }) + DirSectors(data.Select(f => f.DiscName + ";1"));
        sectors += Math.Max(1, Sectors(elfBytes)) + 1; // the boot program, SYSTEM.CNF
        foreach (var f in data) sectors += Math.Max(1, Sectors(f.Length));
        return (sectors + 1) * Sector; // + the closing anchor
    }

    private static long Sectors(long bytes) => (bytes + Sector - 1) / Sector;
    private static int DirRecordLength(string name) { int n = 33 + name.Length; return n + (n & 1); }

    private static int DirSectors(IEnumerable<string> names)
    {
        int sectors = 1, used = 68; // "." and ".."
        foreach (var name in names)
        {
            int len = DirRecordLength(name);
            if (used + len > Sector) { sectors++; used = 0; }
            used += len;
        }
        return sectors;
    }

    /// <summary>
    /// Writes the disc image. <paramref name="esrBlock"/>: the ESR patch's 12 DVD-Video sectors (Runtimes/PS2/esr_dvd_video.bin).
    /// <paramref name="progress"/>: 0..1 while the files are copied.
    /// </summary>
    public static void Write(string isoPath, string volumeName, byte[] elf, string bootName, IReadOnlyList<DiscFile> data, byte[] esrBlock, Action<double>? progress = null)
    {
        if (esrBlock.Length != EsrSectors * Sector || Convert.ToHexString(SHA256.HashData(esrBlock)) != EsrBlockSha256)
            throw new InvalidDataException("The ESR patch's DVD-Video block (Runtimes/PS2/esr_dvd_video.bin) is missing or damaged. Reinstall the editor.");
        var names = data.Select(f => f.DiscName).ToList();
        if (names.Distinct().Count() != names.Count) throw new InvalidDataException("Two of the game's files have the same name on the disc: " + string.Join(", ", names.GroupBy(n => n).Where(g => g.Count() > 1).Select(g => g.Key)));

        string systemCnf = $"BOOT2 = cdrom0:\\{bootName};1\r\nVER = 1.00\r\nVMODE = NTSC\r\n";
        byte[] cnf = Encoding.ASCII.GetBytes(systemCnf);

        // ---- Where everything goes ----
        var root = new List<(string Name, long Lba, long Length, bool Dir)>();
        var dataDir = new List<(string Name, long Lba, long Length, bool Dir)>();
        int rootSectors = DirSectors(new[] { "DATA", bootName + ";1", "SYSTEM.CNF;1" });
        int dataSectors = DirSectors(data.Select(f => f.DiscName + ";1"));
        long dataDirLba = RootDir + rootSectors;
        long next = dataDirLba + dataSectors;
        long elfLba = next; next += Math.Max(1, Sectors(elf.Length));
        long cnfLba = next; next += 1;
        var fileLbas = new long[data.Count];
        for (int i = 0; i < data.Count; i++) { fileLbas[i] = next; next += Math.Max(1, Sectors(data[i].Length)); }
        long lastSector = next; // the closing anchor
        long total = lastSector + 1;
        if (total * Sector > MaxBytes) throw new InvalidDataException($"The disc would be {total * (double)Sector / 1e9:0.00} GB - more than the 8 GB a PlayStation 2 disc can hold.");

        root.Add(("DATA", dataDirLba, dataSectors * (long)Sector, true));
        root.Add((bootName + ";1", elfLba, elf.Length, false));
        root.Add(("SYSTEM.CNF;1", cnfLba, cnf.Length, false));
        root.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
        for (int i = 0; i < data.Count; i++) dataDir.Add((data[i].DiscName + ";1", fileLbas[i], data[i].Length, false));
        dataDir.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var now = DateTime.UtcNow;
        using var fs = new FileStream(isoPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1 << 20);
        fs.SetLength(total * Sector);
        void At(long lba, byte[] bytes) { fs.Position = lba * Sector; fs.Write(bytes, 0, bytes.Length); }

        // ---- ISO 9660 ----
        At(16, PrimaryVolumeDescriptor(volumeName, total, RootDir, rootSectors, now));
        var term = new byte[Sector]; term[0] = 255; Encoding.ASCII.GetBytes("CD001").CopyTo(term, 1); term[6] = 1;
        At(17, term);
        At(PathL, PathTable(littleEndian: true, dataDirLba));
        At(PathM, PathTable(littleEndian: false, dataDirLba));
        At(RootDir, Directory(RootDir, rootSectors, RootDir, rootSectors, root, now, rootSectors));
        At(dataDirLba, Directory(dataDirLba, dataSectors, RootDir, rootSectors, dataDir, now, dataSectors));

        // ---- UDF bridge (as mkisofs), then the ESR patch on it ----
        string[] vrs = { "BEA01", "NSR02", "TEA01" };
        for (int i = 0; i < 3; i++) { var s = new byte[Sector]; Encoding.ASCII.GetBytes(vrs[i]).CopyTo(s, 1); s[6] = 1; At(18 + i, s); }
        long partStart = Avdp + 1, partLength = lastSector - partStart;
        foreach (uint vds in new uint[] { MainVds, ReserveVds })
        {
            At(vds + 0, UdfPrimary(vds + 0, volumeName, now));
            At(vds + 1, UdfImplUse(vds + 1, volumeName));
            At(vds + 2, UdfPartition(vds + 2, (uint)partStart, (uint)partLength));
            At(vds + 3, UdfLogicalVolume(vds + 3, volumeName));
            At(vds + 4, UdfUnallocated(vds + 4));
            At(vds + 5, UdfTerminating(vds + 5));
        }
        At(Lvid, UdfIntegrity(Lvid, now, (uint)partLength));
        At(Lvid + 1, UdfTerminating(Lvid + 1));
        At(Avdp, UdfAnchor(Avdp));
        At(lastSector, UdfAnchor((uint)lastSector));
        ApplyEsrPatch(fs, esrBlock);

        // ---- The files ----
        At(elfLba, elf);
        At(cnfLba, cnf);
        long copied = 0, all = Math.Max(1, data.Sum(f => f.Length));
        var buffer = new byte[1 << 20];
        for (int i = 0; i < data.Count; i++)
        {
            fs.Position = fileLbas[i] * Sector;
            using var src = File.OpenRead(data[i].SourcePath);
            int n;
            while ((n = src.Read(buffer, 0, buffer.Length)) > 0) { fs.Write(buffer, 0, n); copied += n; progress?.Invoke(copied / (double)all); }
        }
        fs.Flush();
    }

    /// <summary>The ESR patch, as every ESR patcher puts it on: the Partition Descriptors (34, 50) kept at 14 and 15, then pointed at sector 128, where the DVD-Video block goes.</summary>
    private static void ApplyEsrPatch(FileStream fs, byte[] esrBlock)
    {
        foreach (var (target, backup) in new[] { (34, 14), (50, 15) })
        {
            var block = new byte[Sector];
            fs.Position = target * (long)Sector; fs.ReadExactly(block);
            fs.Position = backup * (long)Sector; fs.Write(block);
            BinaryPrimitives.WriteUInt32LittleEndian(block.AsSpan(188), EsrLba);
            FinishTag(block, BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(10)));
            fs.Position = target * (long)Sector; fs.Write(block);
        }
        fs.Position = EsrLba * (long)Sector; fs.Write(esrBlock);
    }

    // ================= ISO 9660 =================

    private static void Both32(byte[] b, int at, uint v) { BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v); BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at + 4), v); }
    private static void Both16(byte[] b, int at, ushort v) { BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), v); BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at + 2), v); }
    private static void Text(byte[] b, int at, int length, string s) { for (int i = 0; i < length; i++) b[at + i] = (byte)(i < s.Length ? s[i] : ' '); }

    private static string DName(string s)
    {
        var clean = new string((s ?? "").ToUpperInvariant().Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
        if (clean.Length > 32) clean = clean[..32];
        return clean.Length == 0 ? "SANDCASTLE" : clean;
    }

    private static void LongDate(byte[] b, int at, DateTime? t)
    {
        string s = t is { } d ? d.ToString("yyyyMMddHHmmss") + "00" : "0000000000000000";
        Encoding.ASCII.GetBytes(s).CopyTo(b, at);
        b[at + 16] = 0;
    }

    private static void ShortDate(byte[] b, int at, DateTime t)
    {
        b[at] = (byte)(t.Year - 1900); b[at + 1] = (byte)t.Month; b[at + 2] = (byte)t.Day;
        b[at + 3] = (byte)t.Hour; b[at + 4] = (byte)t.Minute; b[at + 5] = (byte)t.Second; b[at + 6] = 0;
    }

    private static int Record(byte[] b, int at, string name, long lba, long length, bool dir, DateTime t)
    {
        byte[] id = name switch { "\0" => new byte[] { 0 }, "\u0001" => new byte[] { 1 }, _ => Encoding.ASCII.GetBytes(name) };
        int len = 33 + id.Length; len += len & 1;
        b[at] = (byte)len;
        Both32(b, at + 2, (uint)lba);
        Both32(b, at + 10, (uint)length);
        ShortDate(b, at + 18, t);
        b[at + 25] = (byte)(dir ? 2 : 0);
        Both16(b, at + 28, 1);
        b[at + 32] = (byte)id.Length;
        id.CopyTo(b, at + 33);
        return len;
    }

    private static byte[] PrimaryVolumeDescriptor(string volume, long totalSectors, long rootLba, int rootSectors, DateTime now)
    {
        var b = new byte[Sector];
        b[0] = 1; Encoding.ASCII.GetBytes("CD001").CopyTo(b, 1); b[6] = 1;
        Text(b, 8, 32, "PLAYSTATION");
        Text(b, 40, 32, DName(volume));
        Both32(b, 80, (uint)totalSectors);
        Both16(b, 120, 1); Both16(b, 124, 1); Both16(b, 128, Sector);
        Both32(b, 132, 22); // the path table: the root and DATA
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(140), PathL);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(148), PathM);
        Record(b, 156, "\0", rootLba, rootSectors * (long)Sector, true, now);
        Text(b, 190, 128, DName(volume));
        Text(b, 318, 128, "");
        Text(b, 446, 128, "");
        Text(b, 574, 128, "SANDCASTLE RPG MAKER");
        Text(b, 702, 37, ""); Text(b, 739, 37, ""); Text(b, 776, 37, "");
        LongDate(b, 813, now); LongDate(b, 830, now); LongDate(b, 847, null); LongDate(b, 864, null);
        b[881] = 1;
        return b;
    }

    private static byte[] PathTable(bool littleEndian, long dataLba)
    {
        var b = new byte[Sector];
        void U32(int at, uint v) { if (littleEndian) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(at), v); else BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(at), v); }
        void U16(int at, ushort v) { if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), v); else BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(at), v); }
        b[0] = 1; U32(2, RootDir); U16(6, 1); b[8] = 0;            // the root (10 bytes)
        b[10] = 4; U32(12, (uint)dataLba); U16(16, 1); Encoding.ASCII.GetBytes("DATA").CopyTo(b, 18); // DATA (12 bytes)
        return b;
    }

    private static byte[] Directory(long selfLba, int selfSectors, long parentLba, int parentSectors, List<(string Name, long Lba, long Length, bool Dir)> entries, DateTime now, int sectors)
    {
        var b = new byte[sectors * Sector];
        int at = 0;
        at += Record(b, at, "\0", selfLba, selfSectors * (long)Sector, true, now);
        at += Record(b, at, "\u0001", parentLba, parentSectors * (long)Sector, true, now);
        foreach (var e in entries)
        {
            int len = DirRecordLength(e.Name);
            if (at % Sector + len > Sector) at = (at / Sector + 1) * Sector; // a record never crosses a sector
            at += Record(b, at, e.Name, e.Lba, e.Length, e.Dir, now);
        }
        return b;
    }

    // ================= UDF (ECMA-167, UDF 1.02) =================

    private static readonly ushort[] CrcTable = Enumerable.Range(0, 256).Select(i =>
    {
        int crc = i << 8;
        for (int k = 0; k < 8; k++) crc = (crc & 0x8000) != 0 ? ((crc << 1) ^ 0x1021) & 0xFFFF : (crc << 1) & 0xFFFF;
        return (ushort)crc;
    }).ToArray();

    private static void FinishTag(byte[] b, int crcLength)
    {
        ushort crc = 0;
        for (int i = 16; i < 16 + crcLength; i++) crc = (ushort)((crc << 8) ^ CrcTable[((crc >> 8) ^ b[i]) & 0xFF]);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(8), crc);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(10), (ushort)crcLength);
        int sum = 0;
        for (int i = 0; i < 16; i++) if (i != 4) sum += b[i];
        b[4] = (byte)sum;
    }

    private static byte[] Tagged(ushort id, uint location)
    {
        var b = new byte[Sector];
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0), id);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(2), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), location);
        return b;
    }

    private static void CharSpec(byte[] b, int at) { b[at] = 0; Encoding.ASCII.GetBytes("OSTA Compressed Unicode").CopyTo(b, at + 1); }

    private static void DString(byte[] b, int at, int length, string s)
    {
        var chars = Encoding.ASCII.GetBytes(s.Length > length - 2 ? s[..(length - 2)] : s);
        b[at] = 8;
        chars.CopyTo(b, at + 1);
        b[at + length - 1] = (byte)(chars.Length + 1);
    }

    private static void RegId(byte[] b, int at, string id, params byte[] suffix)
    {
        Encoding.ASCII.GetBytes(id).CopyTo(b, at + 1);
        suffix.CopyTo(b, at + 24);
    }

    private static readonly byte[] UdfRevision = { 0x02, 0x01 }; // UDF 1.02

    private static void Timestamp(byte[] b, int at, DateTime t)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at), 0x1000); // local time, UTC
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(at + 2), (ushort)t.Year);
        b[at + 4] = (byte)t.Month; b[at + 5] = (byte)t.Day; b[at + 6] = (byte)t.Hour; b[at + 7] = (byte)t.Minute; b[at + 8] = (byte)t.Second;
    }

    private static string VolumeLabel(string name) => DName(name);

    private static byte[] UdfPrimary(uint lba, string volume, DateTime now)
    {
        var b = Tagged(1, lba);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 0);
        DString(b, 24, 32, VolumeLabel(volume));
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(56), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(58), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(60), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(62), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(64), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(68), 1);
        DString(b, 72, 128, "SANDCASTLE" + now.Ticks.ToString("X16"));
        CharSpec(b, 200); CharSpec(b, 264);
        RegId(b, 344, "*Sandcastle RPG Maker");
        Timestamp(b, 376, now);
        RegId(b, 388, "*Sandcastle RPG Maker");
        FinishTag(b, 496);
        return b;
    }

    private static byte[] UdfImplUse(uint lba, string volume)
    {
        var b = Tagged(4, lba);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 1);
        RegId(b, 20, "*UDF LV Info", UdfRevision);
        CharSpec(b, 52);
        DString(b, 116, 128, VolumeLabel(volume));
        RegId(b, 352, "*Sandcastle RPG Maker");
        FinishTag(b, 496);
        return b;
    }

    private static byte[] UdfPartition(uint lba, uint start, uint length)
    {
        var b = Tagged(5, lba);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(20), 1); // allocated
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(22), 0);
        RegId(b, 24, "+NSR02");                                  // "+NSR" at byte 25: what ESR patchers look for
        b[24] = 2;                                               // its flags, as mkisofs writes them (protected)
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(184), 1); // read only
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(188), start);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(192), length);
        RegId(b, 196, "*Sandcastle RPG Maker");
        FinishTag(b, 496);
        return b;
    }

    private static byte[] UdfLogicalVolume(uint lba, string volume)
    {
        var b = Tagged(6, lba);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 3);
        CharSpec(b, 20);
        DString(b, 84, 128, VolumeLabel(volume));
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(212), Sector);
        RegId(b, 216, "*OSTA UDF Compliant", UdfRevision[0], UdfRevision[1], 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(248), Sector); // the File Set Descriptor: block 0 of the partition
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(264), 6);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(268), 1);
        RegId(b, 272, "*Sandcastle RPG Maker");
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(432), 2 * Sector);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(436), Lvid);
        b[440] = 1; b[441] = 6;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(442), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(444), 0);
        FinishTag(b, 446 - 16);
        return b;
    }

    private static byte[] UdfUnallocated(uint lba)
    {
        var b = Tagged(7, lba);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 4);
        FinishTag(b, 24 - 16);
        return b;
    }

    private static byte[] UdfTerminating(uint lba)
    {
        var b = Tagged(8, lba);
        FinishTag(b, 496);
        return b;
    }

    private static byte[] UdfIntegrity(uint lba, DateTime now, uint partitionBlocks)
    {
        var b = Tagged(9, lba);
        Timestamp(b, 16, now);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), 1); // closed
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(40), 16); // next unique id
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(72), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(76), 46);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(80), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(84), partitionBlocks);
        RegId(b, 88, "*Sandcastle RPG Maker");
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(120), 2); // files: VIDEO_TS.IFO, VIDEO_TS.BUP
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(124), 3); // folders: the root, VIDEO_TS, AUDIO_TS
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(128), 0x0102);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(130), 0x0102);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(132), 0x0102);
        FinishTag(b, 134 - 16);
        return b;
    }

    private static byte[] UdfAnchor(uint lba)
    {
        var b = Tagged(2, lba);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), 16 * Sector);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(20), MainVds);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24), 16 * Sector);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(28), ReserveVds);
        FinishTag(b, 496);
        return b;
    }

    /// <summary>Is the ESR patch on this image (the "+NSR" mark every ESR patcher looks for at sector 14)?</summary>
    public static bool HasEsrPatch(string isoPath)
    {
        using var fs = File.OpenRead(isoPath);
        var mark = new byte[4];
        fs.Position = 14L * Sector + 25;
        return fs.Read(mark, 0, 4) == 4 && Encoding.ASCII.GetString(mark) == "+NSR";
    }
}
