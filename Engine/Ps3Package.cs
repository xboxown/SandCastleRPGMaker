using System.Buffers.Binary;
using System.IO.Compression;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// The PlayStation 3 build's installer: one PKG (Package Manager > Install Package Files; installed from the hard drive's
/// packages folder it is deleted once installed) holding PARAM.SFO, ICON0.PNG and USRDIR (EBOOT.BIN and the game's data).
/// Never a disc image - that is the PS2's.
///  - EBOOT.BIN: the player (Runtimes/PS3/EBOOT.elf, built from the Player folder by build_ps3.sh) signed as an NPDRM SELF for
///    this game's content ID (PSL1GHT's make_self_npdrm, by geohot - the same steps and the same public homebrew keys).
///  - PARAM.SFO: PSL1GHT's sfo tool's layout and its template's values; the game's title and title ID.
///  - The PKG: PSL1GHT's pkg tool, then package_finalize (retail AES-CTR) in one go, written a piece at a time.
/// </summary>
public static class Ps3Package
{
    // ---------------- Keys (PSL1GHT tools/geohot/include: keys.h, oddkeys.h - the published homebrew keys) ----------------
    private static readonly byte[] NpdrmR = Hex("A38BCB3E4E7309904AEFDFC5047D0FDF06E35C0D");
    private static readonly byte[] NpdrmN = Hex("B0E7CAFFC8DEEE8A55A3050D809ADFE38FA01DAB");
    private static readonly byte[] NpdrmK = Hex("974E906279705E58D9DAB4BFDA8BDA93135117E2");
    private static readonly byte[] NpdrmDa = Hex("040AB47509BED04BD96521AD1B365B86BF620A98");
    private static readonly byte[] RetailPkgAesKey = Hex("2E7B71D7C9C9A14EA3221F188828B8F8");
    private static readonly byte[] NpdrmKeypairE = Hex("A1C013ABCE98A7E3DC69923B07C0285F7554C512B0B0A96F245240F2FD433AF23F4EFEC6C183EA378D1BECB09D88DB328F2C8637B7AC72059B1556B0D95B5BE0");
    private static readonly byte[] NpdrmKeypairD = Hex("87C74FFE66930BAAA16F864091C566FB000000000000000000000000000000000828B58FACF9DEC8D70DFEF0F37663AE00000000000000000000000000000000");
    private static readonly byte[] NpdrmOmacKey1 = Hex("72F990788F9CFF745725F08E4C128387");
    private static readonly byte[] NpdrmOmacKey2 = Hex("6BA52976EFDA16EF3C339FB2971E256B");
    private static readonly byte[] NpdrmOmacKey3 = Hex("9B515FEACF75064981AA604D91A54E97");

    /// <summary>make_self.c's cflags_static (NPDRM): 0x70 bytes.</summary>
    private static readonly byte[] CflagsNpdrm = Hex(
        "00000001000000300000000000000001" +
        "40000000000000000000000000000000" +
        "00000000000000000000000000000002" +
        "00000002000000400000000000000001" +
        "627CB1808AB938E32C8C091708726A57" +
        "9E2586E4000000000000000000000000" +
        "00000000000000000000000000005DC0");
    /// <summary>make_self.c's nubpadding_static (NPDRM): 0x30 bytes.</summary>
    private static readonly byte[] NubPaddingNpdrm = Hex(
        "00000001000000300000000000000000" +
        "00000000000000000000000000000000" +
        "000000000000003B0000000100002000");
    private static readonly byte[] SdkVersion = Hex("00000001000000000000001000000000");

    private static byte[] Hex(string s)
    {
        s = s.Replace(" ", "");
        var b = new byte[s.Length / 2];
        for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(s.Substring(2 * i, 2), 16);
        return b;
    }

    // ---------------- Names ----------------

    /// <summary>A title ID for this game (four letters, five digits), the same every build: "SNDC" and five from the project's id.</summary>
    public static string TitleId(Guid projectId)
    {
        uint n = BinaryPrimitives.ReadUInt32LittleEndian(projectId.ToByteArray()) % 100000;
        return $"SNDC{n:D5}";
    }

    /// <summary>The content ID: UP0001-TITLEID_00- and sixteen from the project's id ("UP0001-SNDC12345_00-0123456789ABCDEF").</summary>
    public static string ContentId(Guid projectId) => $"UP0001-{TitleId(projectId)}_00-{projectId.ToString("N")[..16].ToUpperInvariant()}";

    // ---------------- EBOOT.BIN (make_self_npdrm) ----------------

    private static void Be16(byte[] b, int o, int v) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o), (ushort)v);
    private static void Be32(byte[] b, int o, long v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o), (uint)v);
    private static void Be64(byte[] b, int o, long v) => BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(o), (ulong)v);
    private static long RBe16(byte[] b, long o) => BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan((int)o));
    private static long RBe64(byte[] b, long o) => (long)BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan((int)o));

    /// <summary>AES-128-CTR (OpenSSL's CRYPTO_ctr128: the whole 16-byte counter counts up, big-endian), in place.</summary>
    private static void AesCtr(byte[] key, byte[] iv, byte[] data, int offset, int length)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        var counter = (byte[])iv.Clone();
        var block = new byte[16];
        for (int done = 0; done < length; done += 16)
        {
            aes.EncryptEcb(counter, block, PaddingMode.None);
            int n = Math.Min(16, length - done);
            for (int i = 0; i < n; i++) data[offset + done + i] ^= block[i];
            for (int i = 15; i >= 0; i--) if (++counter[i] != 0) break;
        }
    }

    /// <summary>aesOmac1Mode (AES-CMAC) as make_self has it.</summary>
    private static byte[] AesOmac1(byte[] input, byte[] key)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        byte[] Enc(byte[] x) => aes.EncryptEcb(x, PaddingMode.None);
        static void Rol1(byte[] w)
        {
            byte xor = (byte)((w[0] & 0x80) != 0 ? 0x87 : 0);
            for (int i = 0; i < 15; i++) w[i] = (byte)((w[i] << 1) | (w[i + 1] >> 7));
            w[15] = (byte)((w[15] << 1) ^ xor);
        }
        var running = new byte[16];
        var worthless = Enc(new byte[16]);
        Rol1(worthless);
        int len = input.Length, i = 0;
        var hash = new byte[16];
        if (len > 0x10)
            for (i = 0; i < len - 0x10; i += 0x10)
            {
                for (int j = 0; j < 16; j++) hash[j] = (byte)(running[j] ^ input[i + j]);
                running = Enc(hash);
            }
        int overrun = len % 0x10 == 0 ? 0x10 : len & 0xF;
        hash = new byte[16];
        Array.Copy(input, i, hash, 0, overrun);
        if (overrun != 0x10) { hash[overrun] = 0x80; Rol1(worthless); }
        for (int j = 0; j < 16; j++) hash[j] ^= (byte)(running[j] ^ worthless[j]);
        return Enc(hash);
    }

    private sealed class Segment { public byte[] Data = Array.Empty<byte>(); public long RLen; public bool Compressed; public byte[] Sha1 = new byte[0x20], Hmac = new byte[0x40], Erk = new byte[16], Riv = new byte[16]; public int Padding; }

    /// <summary>The player's ELF (stripped, sprx-linked) as EBOOT.BIN: an NPDRM SELF for this content ID (make_self_npdrm, NPDRM).</summary>
    public static byte[] SignEboot(byte[] elf, string contentId, string fileName = "EBOOT.BIN")
    {
        long phoff = RBe64(elf, 0x20), shoff = RBe64(elf, 0x28);
        int phnum = (int)RBe16(elf, 0x38), shnum = (int)RBe16(elf, 0x3C);
        // ---- the segments: each compressed (zlib, as deflate level 9), hashed (HMAC-SHA1), encrypted (AES-CTR) ----
        var segs = new List<Segment>();
        for (int i = 0; i < phnum; i++)
        {
            long ph = phoff + i * 0x38, off = RBe64(elf, ph + 8), size = RBe64(elf, ph + 0x20);
            var s = new Segment { RLen = size, Compressed = size != 0 };
            RandomNumberGenerator.Fill(s.Hmac); RandomNumberGenerator.Fill(s.Erk); RandomNumberGenerator.Fill(s.Riv);
            if (s.Compressed)
            {
                using var ms = new MemoryStream();
                using (var zs = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true)) zs.Write(elf, (int)off, (int)size);
                s.Data = ms.ToArray();
            }
            s.Padding = (0x10 - (s.Data.Length & 0xF)) & 0xF;
            HMACSHA1.HashData(s.Hmac, s.Data).CopyTo(s.Sha1, 0);
            AesCtr(s.Erk, s.Riv, s.Data, 0, s.Data.Length);
            segs.Add(s);
        }
        // ---- the layout ----
        int elfHead = (int)(phoff + phnum * 0x38);
        int at = 0x20 + 0x50;                                   // Self_Shdr, Self_Ehdr
        int ihoff = at; at += 0x20;                             // Self_Ihdr
        int ehoff = at; at += elfHead; at += (0x10 - (at & 0xF)) & 0xF;
        int pmoff = at; at += 0x20 * phnum;
        int svoff = at; at += 0x10;
        int cfoff = at; at += 0x70;
        int npdoff = at; at += 0x90;
        int esize = at - 0x20;
        int mdoff = at; at += 0x40;
        int shoff2 = at; at += 0x20;                            // segment_certification_header
        int encoff = at; at += 0x30 * phnum;
        int cryptoff = at; at += 0x80 * phnum;
        int nuboff = at; at += 0x30;
        int sigoff = at; at += 0x30;
        if (at % 0x80 != 0) at += 0x80 - at % 0x80;
        int dataoff = at;
        var segOff = new int[phnum];
        for (int i = 0; i < phnum; i++) { segOff[i] = at; at += segs[i].Data.Length + segs[i].Padding; }
        int sectoff = at; at += shnum * 0x40;
        var self = new byte[at];
        // ---- the headers ----
        Be32(self, 0, 0x53434500); Be32(self, 4, 2); Be16(self, 8, 1); Be16(self, 10, 1); Be32(self, 0xC, esize);
        Be64(self, 0x10, dataoff); Be64(self, 0x18, elf.Length);
        Be64(self, 0x20, 3); Be64(self, 0x28, ihoff); Be64(self, 0x30, ehoff); Be64(self, 0x38, ehoff + phoff);
        Be64(self, 0x40, sectoff); Be64(self, 0x48, pmoff); Be64(self, 0x50, svoff); Be64(self, 0x58, cfoff); Be64(self, 0x60, 0x70 + 0x90);
        Be64(self, ihoff, 0x1010000001000003L); Be32(self, ihoff + 8, 0x01000002); Be32(self, ihoff + 12, 8); Be64(self, ihoff + 16, 0x0001000000000000L);
        Array.Copy(elf, 0, self, ehoff, elfHead);
        for (int i = 0; i < phnum; i++)
        {
            int p = pmoff + 0x20 * i;
            Be64(self, p, segOff[i]); Be64(self, p + 8, segs[i].Data.Length); Be32(self, p + 16, segs[i].Compressed ? 2 : 1); Be32(self, p + 28, 1);
        }
        SdkVersion.CopyTo(self, svoff);
        CflagsNpdrm.CopyTo(self, cfoff);
        // NPDRM: the content ID and its two OMACs
        Be32(self, npdoff, 3); Be32(self, npdoff + 4, 0x90); Be32(self, npdoff + 16, 0x4E504400); Be32(self, npdoff + 20, 1); Be32(self, npdoff + 24, 3); Be32(self, npdoff + 28, 1);
        var cid = Encoding.ASCII.GetBytes(contentId);
        Array.Copy(cid, 0, self, npdoff + 0x20, Math.Min(0x30, cid.Length));
        var forHash1 = new byte[0x30 + fileName.Length];
        Array.Copy(self, npdoff + 0x20, forHash1, 0, 0x30);
        Encoding.ASCII.GetBytes(fileName).CopyTo(forHash1, 0x30);
        AesOmac1(forHash1, NpdrmOmacKey3).CopyTo(self, npdoff + 0x60);
        var key12 = new byte[16];
        for (int i = 0; i < 16; i++) key12[i] = (byte)(NpdrmOmacKey1[i] ^ NpdrmOmacKey2[i]);
        AesOmac1(self.AsSpan(npdoff + 0x10, 0x60).ToArray(), key12).CopyTo(self, npdoff + 0x70);
        NpdrmKeypairD.CopyTo(self, mdoff);
        Be64(self, shoff2, sigoff); Be32(self, shoff2 + 8, 1); Be32(self, shoff2 + 12, phnum); Be32(self, shoff2 + 16, 0x80 * phnum / 0x10); Be32(self, shoff2 + 20, 0x30);
        for (int i = 0; i < phnum; i++)
        {
            int e = encoff + 0x30 * i, c = 0x80 * i;
            Be64(self, e, segOff[i]); Be64(self, e + 8, segs[i].Data.Length); Be32(self, e + 16, 2); Be32(self, e + 20, i); Be32(self, e + 24, 2);
            Be32(self, e + 28, c / 0x10); Be32(self, e + 32, 3); Be32(self, e + 36, (c + 0x60) / 0x10); Be32(self, e + 40, (c + 0x70) / 0x10); Be32(self, e + 44, segs[i].Compressed ? 2 : 1);
            segs[i].Sha1.CopyTo(self, cryptoff + c); segs[i].Hmac.CopyTo(self, cryptoff + c + 0x20); segs[i].Erk.CopyTo(self, cryptoff + c + 0x60); segs[i].Riv.CopyTo(self, cryptoff + c + 0x70);
        }
        NubPaddingNpdrm.CopyTo(self, nuboff);
        NpdrmR.CopyTo(self, sigoff + 1);
        for (int i = 0; i < phnum; i++) segs[i].Data.CopyTo(self, segOff[i]);
        Array.Copy(elf, shoff, self, sectoff, shnum * 0x40);
        // ---- the signature (ECDSA with the published key: S = (z + r * da) / k mod n) ----
        static BigInteger U(byte[] b) => new(b, isUnsigned: true, isBigEndian: true);
        BigInteger n = U(NpdrmN), r = U(NpdrmR), da = U(NpdrmDa), k = U(NpdrmK), z = U(SHA1.HashData(self.AsSpan(0, sigoff)));
        BigInteger kinv = ModInverse(k, n); // mpz_invert
        BigInteger sv = ((r * da % n + z) % n) * kinv % n;
        var sb = sv.ToByteArray(isUnsigned: true, isBigEndian: true);
        Array.Copy(sb, 0, self, sigoff + 0x16 + (0x14 - sb.Length), sb.Length);
        // ---- the metadata encrypted, then its keys hidden ----
        AesCtr(self.AsSpan(mdoff, 16).ToArray(), self.AsSpan(mdoff + 0x20, 16).ToArray(), self, mdoff + 0x40, dataoff - mdoff - 0x40);
        NpdrmKeypairE.CopyTo(self, mdoff);
        return self;
    }

    private static BigInteger ModInverse(BigInteger a, BigInteger m)
    {
        BigInteger t = 0, newT = 1, r = m, newR = ((a % m) + m) % m;
        while (newR != 0)
        {
            var q = r / newR;
            (t, newT) = (newT, t - q * newT);
            (r, newR) = (newR, r - q * newR);
        }
        return t < 0 ? t + m : t;
    }

    // ---------------- PARAM.SFO (PSL1GHT's sfo, its sfo.xml template) ----------------

    public static byte[] ParamSfo(string title, string titleId)
    {
        var kvs = new List<(string Key, object Value)>
        {
            ("APP_VER", "01.00"), ("ATTRIBUTE", 0), ("BOOTABLE", 1), ("CATEGORY", "HG"),
            ("LICENSE", "Made with Sandcastle RPG Maker. This is in no way associated with Sony Interactive Entertainment."),
            ("PARENTAL_LEVEL", 0), ("PS3_SYSTEM_VER", "01.8000"), ("RESOLUTION", 63), ("SOUND_FORMAT", 279),
            ("TITLE", title.Length > 127 ? title[..127] : title), ("TITLE_ID", titleId), ("VERSION", "01.00"),
        };
        static int Align(int v, int a) => (v + a - 1) & ~(a - 1);
        static int AlignOf(string k) => k == "TITLE" ? 0x80 : k == "LICENSE" ? 0x200 : k == "TITLE_ID" ? 0x10 : 4;
        int keyOff = 0, valueOff = 0;
        var entries = new List<(int KeyOff, int Type, int Len, int Padded, int ValueOff)>();
        foreach (var (k, v) in kvs)
        {
            int len = v is int ? 4 : Encoding.UTF8.GetByteCount((string)v) + 1, padded = v is int ? 4 : Align(len, AlignOf(k));
            entries.Add((keyOff, v is int ? 4 : 2, len, padded, valueOff));
            keyOff += k.Length + 1; valueOff += padded;
        }
        int keyTable = 0x14 + 0x10 * kvs.Count, valueTable = Align(keyTable + keyOff, 4);
        var b = new byte[valueTable + valueOff];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0), 0x46535000); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(4), 0x101);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(8), (uint)keyTable); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(12), (uint)valueTable);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(16), (uint)kvs.Count);
        for (int i = 0; i < kvs.Count; i++)
        {
            int e = 0x14 + 0x10 * i;
            var en = entries[i];
            BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(e), (ushort)en.KeyOff); b[e + 2] = 4; b[e + 3] = (byte)en.Type;
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(e + 4), (uint)en.Len); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(e + 8), (uint)en.Padded);
            BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(e + 12), (uint)en.ValueOff);
            Encoding.ASCII.GetBytes(kvs[i].Key).CopyTo(b, keyTable + en.KeyOff);
            if (kvs[i].Value is int iv) BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(valueTable + en.ValueOff), (uint)iv);
            else Encoding.UTF8.GetBytes((string)kvs[i].Value).CopyTo(b, valueTable + en.ValueOff);
        }
        return b;
    }

    // ---------------- The PKG (PSL1GHT's pkg, then package_finalize) ----------------

    /// <summary>A file (or folder) in the package: its path inside ("USRDIR/EBOOT.BIN"), and where its bytes come from (null: a folder).</summary>
    public sealed record Entry(string Name, string? SourcePath, byte[]? Bytes = null)
    {
        public bool IsFolder => SourcePath is null && Bytes is null;
        public long Length => Bytes?.LongLength ?? (SourcePath is null ? 0 : new FileInfo(SourcePath).Length);
        public Stream Open() => Bytes is not null ? new MemoryStream(Bytes, writable: false) : File.OpenRead(SourcePath!);
    }

    /// <summary>The entries of a folder as the pkg tool walks it: in each folder its files first, then its folders (each followed by what's in it).</summary>
    public static void AddFolder(List<Entry> into, string folder, string prefix)
    {
        foreach (var f in Directory.GetFiles(folder).OrderBy(x => x, StringComparer.Ordinal)) into.Add(new Entry(prefix + Path.GetFileName(f), f));
        foreach (var d in Directory.GetDirectories(folder).OrderBy(x => x, StringComparer.Ordinal))
        {
            string name = prefix + Path.GetFileName(d);
            into.Add(new Entry(name, null));
            AddFolder(into, d, name + "/");
        }
    }

    private static byte[] KeyToContext(ReadOnlySpan<byte> key)
    {
        var c = new byte[0x40];
        key[..8].CopyTo(c.AsSpan(0)); key[..8].CopyTo(c.AsSpan(8)); key.Slice(8, 8).CopyTo(c.AsSpan(16)); key.Slice(8, 8).CopyTo(c.AsSpan(24));
        return c;
    }

    /// <summary>The debug PKG's SHA-1 keystream (pkg_crypt): each 16 bytes XORed with SHA-1 of the context, its counter (at 0x38) then +1.</summary>
    private static byte[] DebugCrypt(byte[] context, byte[] input)
    {
        var o = new byte[input.Length];
        for (int at = 0; at < input.Length; at += 16)
        {
            var h = SHA1.HashData(context);
            for (int i = 0; i < 16 && at + i < input.Length; i++) o[at + i] = (byte)(input[at + i] ^ h[i]);
            ulong n = BinaryPrimitives.ReadUInt64BigEndian(context.AsSpan(0x38)) + 1;
            BinaryPrimitives.WriteUInt64BigEndian(context.AsSpan(0x38), n);
        }
        return o;
    }

    /// <summary>Writes the finalized (retail) PKG for these entries. progress: 0..1 as the data goes in.</summary>
    public static void WritePkg(string outPath, string contentId, IReadOnlyList<Entry> entries, Action<double>? progress = null)
    {
        int count = entries.Count;
        var sizes = new long[count];
        var flags = new uint[count];
        for (int i = 0; i < count; i++)
        {
            var e = entries[i];
            if (e.IsFolder) { flags[i] = 0x80000004; continue; }
            sizes[i] = e.Length;
            if (e.Name == "USRDIR/EBOOT.BIN") { sizes[i] = ((sizes[i] - 0x30 + 63) & ~63L) + 0x30; flags[i] = 0x80000001; }
            else flags[i] = 0x80000003;
        }
        // ---- the file descriptors and their names (the start of the data) ----
        var names = entries.Select(e => Encoding.UTF8.GetBytes(e.Name)).ToArray();
        long cur = 0x20L * count;
        var nameOff = new long[count];
        for (int i = 0; i < count; i++) { nameOff[i] = cur; cur += (names[i].Length + 0xF) & ~0xF; }
        var fileOff = new long[count];
        for (int i = 0; i < count; i++) { fileOff[i] = cur; if (!entries[i].IsFolder) cur += (sizes[i] + 0xF) & ~0xFL; }
        long dataSize = cur;
        int descLength = count > 0 ? (int)(nameOff[^1] + ((names[^1].Length + 0xF) & ~0xF)) : 0; /* the descriptors and the names */
        var desc = new byte[descLength];
        for (int i = 0; i < count; i++)
        {
            int d = 0x20 * i;
            Be32(desc, d, nameOff[i]); Be32(desc, d + 4, names[i].Length); Be64(desc, d + 8, fileOff[i]); Be64(desc, d + 16, sizes[i]); Be32(desc, d + 24, flags[i]);
            names[i].CopyTo(desc, (int)nameOff[i]);
        }
        // ---- the header, its QA digest (the files, then the header as pkg.py packed it, then the descriptors) ----
        var hdr = new byte[0x80];
        Be32(hdr, 0, 0x7F504B47); Be32(hdr, 4, 1); Be32(hdr, 8, 0xC0); Be32(hdr, 12, 5); Be32(hdr, 16, 0x80); Be32(hdr, 20, count);
        Be64(hdr, 24, dataSize + 0x1A0); Be64(hdr, 32, 0x140); Be64(hdr, 40, dataSize);
        using (var qa = IncrementalHash.CreateHash(HashAlgorithmName.SHA1))
        {
            var buffer = new byte[1 << 20];
            foreach (var e in entries)
            {
                if (e.IsFolder) continue;
                using var s = e.Open();
                int n;
                while ((n = s.Read(buffer, 0, buffer.Length)) > 0) qa.AppendData(buffer, 0, n);
            }
            qa.AppendData(hdr, 0, 0x30);
            qa.AppendData(hdr, 0x60, 0x20); qa.AppendData(hdr, 0x60, 0x20); qa.AppendData(hdr, 0x60, 0x10);
            qa.AppendData(desc);
            Array.Copy(qa.GetHashAndReset(), 0, hdr, 0x60, 0x10);
        }
        var cid = Encoding.ASCII.GetBytes(contentId);
        Array.Copy(cid, 0, hdr, 0x30, Math.Min(0x30, cid.Length));
        var klic = KeyToContext(hdr.AsSpan(0x60, 0x10));
        BinaryPrimitives.WriteUInt64BigEndian(klic.AsSpan(0x38), 0xFFFFFFFFFFFFFFFF);
        DebugCrypt(klic, new byte[16]).CopyTo(hdr, 0x70);
        // ---- the meta block ----
        var meta = new byte[0x40];
        Be32(meta, 0, 1); Be32(meta, 4, 4); Be32(meta, 8, 3); Be32(meta, 12, 2); Be32(meta, 16, 4); Be32(meta, 20, 5); Be32(meta, 24, 3); Be32(meta, 28, 4);
        Be32(meta, 32, 0xE); Be32(meta, 36, 4); Be32(meta, 40, 8); Be32(meta, 48, dataSize); Be32(meta, 52, 5); Be32(meta, 56, 4); Be16(meta, 60, 0x1061);
        var hdrSha = SHA1.HashData(hdr).AsSpan(3, 16).ToArray();
        var metaSha = SHA1.HashData(meta).AsSpan(3, 16).ToArray();
        var enc1 = DebugCrypt(KeyToContext(metaSha), new byte[0x30]);
        var enc2 = DebugCrypt(KeyToContext(hdrSha), enc1);
        // ---- written: finalized as package_finalize does (the type's top byte 0x80, the data AES-CTR with the retail key and K Licensee as its counter) ----
        using var outFile = new FileStream(outPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        using var fin = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        void Put(byte[] b, int off, int len) { outFile.Write(b, off, len); fin.AppendData(b, off, len); }
        var hdrOut = (byte[])hdr.Clone();
        hdrOut[4] = 0x80;
        Put(hdrOut, 0, 0x80); Put(hdrSha, 0, 16); Put(enc2, 0, 0x30); Put(meta, 0, 0x40); Put(metaSha, 0, 16); Put(enc1, 0, 0x30);
        using var aes = Aes.Create();
        aes.Key = RetailPkgAesKey;
        var counter = hdr.AsSpan(0x70, 16).ToArray();
        var ks = new byte[16];
        long written = 0;
        void PutData(byte[] b, int len) // whole 16-byte blocks: every piece is 16-aligned
        {
            for (int at = 0; at < len; at += 16)
            {
                aes.EncryptEcb(counter, ks, PaddingMode.None);
                for (int i = 0; i < 16; i++) b[at + i] ^= ks[i];
                for (int i = 15; i >= 0; i--) if (++counter[i] != 0) break;
            }
            Put(b, 0, len);
            written += len;
            progress?.Invoke(dataSize > 0 ? (double)written / dataSize : 1);
        }
        PutData(desc, desc.Length);
        var chunk = new byte[1 << 20];
        for (int i = 0; i < count; i++)
        {
            if (entries[i].IsFolder) continue;
            long left = (sizes[i] + 0xF) & ~0xFL;
            using var s = entries[i].Open();
            while (left > 0)
            {
                int want = (int)Math.Min(chunk.Length, left), got = 0, n;
                while (got < want && (n = s.Read(chunk, got, want - got)) > 0) got += n;
                Array.Clear(chunk, got, want - got); // past its end: zeros (the EBOOT's rounded size, the 16-byte padding)
                PutData(chunk, want);
                left -= want;
            }
        }
        var trailer = new byte[0x60];
        Put(trailer, 0, 0x40); // the last 0x20 bytes: the SHA-1 of everything before them
        var sum = new byte[0x20];
        fin.GetHashAndReset().CopyTo(sum, 0);
        outFile.Write(sum, 0, 0x20);
    }
}
