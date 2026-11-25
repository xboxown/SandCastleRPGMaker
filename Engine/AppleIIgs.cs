using System.Text;

namespace SandcastleRpgMaker.Engine;

/// <summary>The machine a project's game is made for (ProjectManifest.Machine): "" = every platform, or one machine with its own player and limits.</summary>
public static class GameMachine
{
    public const string Any = "";
    /// <summary>Ultima 6 Apple IIgs mode (File > New Project > Style: Ultima 6 Apple IIgs): its own 65816 player under GS/OS, an accelerator required.</summary>
    public const string AppleIIgs = "AppleIIgs";
}

/// <summary>
/// What a real Apple IIgs can hold - the Ultima 6 Apple IIgs mode's editor keeps a project inside these (Game > Apple IIgs Check,
/// Build, Test Game). Super Hi-Res 320 x 200: 16 colours on screen (from 4096: 4 bits of red, green and blue), the Ensoniq
/// DOC's 64 KB of sound memory, a ProDOS volume of at most 32 MB.
/// </summary>
public static class AppleIIgsLimits
{
    public const int ScreenWidth = 320, ScreenHeight = 200;
    /// <summary>Colours on screen at once (one Super Hi-Res palette), each 12-bit ($0RGB).</summary>
    public const int Colors = 16;
    public const int TileSize = 16;
    /// <summary>Different tiles in a game (16 x 16, 128 bytes each).</summary>
    public const int MaxTiles = 1024;
    public const int MaxMapWidth = 256, MaxMapHeight = 256;
    public const int MaxMaps = 16;
    /// <summary>A character's or an object's picture: 16 x 16, or up to 32 x 32 for big monsters (four tiles, as Ultima 6's dragons).</summary>
    public const int MaxSpriteWidth = 32, MaxSpriteHeight = 32;
    /// <summary>Portraits in the HUD: Ultima 6's own size.</summary>
    public const int PortraitWidth = 56, PortraitHeight = 64;
    public const int MaxCharactersPerMap = 64;
    public const int MaxObjectsPerMap = 1024;
    /// <summary>Sounds: SND (8-bit, mono, unsigned), at most 22050 per second, each at most 60 KB so it fits the DOC's 64 KB.</summary>
    public const int MaxSampleRate = 22050;
    public const int MaxSoundBytes = 60 * 1024;
    public const int MaxSounds = 255;
    /// <summary>ProDOS: 65535 blocks of 512 bytes.</summary>
    public const int MaxVolumeBlocks = 65535;
    public static readonly int[] SampleRates = { 22050, 11025, 8000 };
}

/// <summary>
/// A ProDOS volume written from scratch, inside a .2mg (or a plain .po / .hdv: the same blocks without the 64-byte header).
/// GS/OS reads it as any hard disk partition: double-click it on the desktop, then the game. Files go in the volume's top
/// folder (51 at most), each with its ProDOS file type and aux type.
/// </summary>
public sealed class ProDosVolume
{
    private sealed record FileEntry(string Name, byte FileType, ushort AuxType, byte[] Data);
    private readonly List<FileEntry> _files = new();
    public string VolumeName { get; }

    public ProDosVolume(string volumeName) => VolumeName = CleanName(volumeName, "GAME");

    /// <summary>A ProDOS name: a letter first, then letters, digits and dots; at most 15.</summary>
    public static string CleanName(string name, string fallback)
    {
        var sb = new StringBuilder();
        foreach (char c in name.ToUpperInvariant())
        {
            if (sb.Length == 15) break;
            if (c is >= 'A' and <= 'Z' || (sb.Length > 0 && (c is >= '0' and <= '9' || c == '.'))) sb.Append(c);
            else if (sb.Length > 0 && c is ' ' or '_' or '-') sb.Append('.');
        }
        string s = sb.ToString().TrimEnd('.');
        return s.Length == 0 ? fallback : s;
    }

    public void Add(string name, byte fileType, ushort auxType, byte[] data)
    {
        if (_files.Count >= 51) throw new InvalidOperationException("A ProDOS volume's top folder holds 51 files at most.");
        _files.Add(new FileEntry(CleanName(name, "FILE" + _files.Count), fileType, auxType, data));
    }

    /// <summary>The volume's blocks; totalBlocks 0 = just big enough (at least 1600 blocks, an 800K disk).</summary>
    public byte[] Build(int totalBlocks = 0)
    {
        int need = 6 + 16; // boot blocks, the top folder (4 blocks), room for the bitmap
        foreach (var f in _files) need += BlocksFor(f.Data.Length);
        if (totalBlocks == 0) totalBlocks = Math.Max(1600, need + need / 8 + 64);
        if (totalBlocks > AppleIIgsLimits.MaxVolumeBlocks) throw new InvalidOperationException("The game is bigger than a ProDOS volume (32 MB).");
        var disk = new byte[totalBlocks * 512];
        int bitmapBlocks = (totalBlocks + 4095) / 4096;
        var used = new bool[totalBlocks];
        for (int b = 0; b < 6 + bitmapBlocks; b++) used[b] = true;
        int next = 6 + bitmapBlocks;
        int Alloc() { if (next >= totalBlocks) throw new InvalidOperationException("The disk is full."); used[next] = true; return next++; }
        Span<byte> Block(int b) => disk.AsSpan(b * 512, 512);
        static void W16(Span<byte> s, int at, int v) { s[at] = (byte)v; s[at + 1] = (byte)(v >> 8); }

        var stamp = ProDosTime(DateTime.Now);
        // The top folder: blocks 2-5, linked.
        for (int b = 2; b <= 5; b++) { var blk = Block(b); W16(blk, 0, b == 2 ? 0 : b - 1); W16(blk, 2, b == 5 ? 0 : b + 1); }
        var head = Block(2);
        head[4] = (byte)(0xF0 | VolumeName.Length);
        Encoding.ASCII.GetBytes(VolumeName).CopyTo(head[5..]);
        stamp.CopyTo(head[0x1C..]);
        head[0x20] = 0; head[0x21] = 0; head[0x22] = 0xC3; head[0x23] = 0x27; head[0x24] = 0x0D;
        W16(head, 0x25, _files.Count);
        W16(head, 0x27, 6);
        W16(head, 0x29, totalBlocks);

        for (int i = 0; i < _files.Count; i++)
        {
            var f = _files[i];
            int slot = i + 1; // entry 0 is the folder's own header
            int dirBlock = 2 + slot / 13, at = 4 + (slot % 13) * 0x27;
            var e = Block(dirBlock)[at..];
            int key, storage, blocksUsed = 0;
            int dataBlocks = Math.Max(1, (f.Data.Length + 511) / 512);
            var data = new int[dataBlocks];
            for (int d = 0; d < dataBlocks; d++)
            {
                data[d] = Alloc(); blocksUsed++;
                int len = Math.Min(512, f.Data.Length - d * 512);
                if (len > 0) f.Data.AsSpan(d * 512, len).CopyTo(Block(data[d]));
            }
            if (dataBlocks == 1) { storage = 1; key = data[0]; }
            else if (dataBlocks <= 256) { storage = 2; key = Alloc(); blocksUsed++; WriteIndex(Block(key), data, 0); }
            else
            {
                storage = 3; key = Alloc(); blocksUsed++;
                int indexes = (dataBlocks + 255) / 256;
                var master = Block(key);
                for (int x = 0; x < indexes; x++)
                {
                    int ib = Alloc(); blocksUsed++;
                    WriteIndex(Block(ib), data, x * 256);
                    master[x] = (byte)ib; master[256 + x] = (byte)(ib >> 8);
                }
            }
            e[0] = (byte)(storage << 4 | f.Name.Length);
            Encoding.ASCII.GetBytes(f.Name).CopyTo(e[1..]);
            e[0x10] = f.FileType;
            W16(e, 0x11, key);
            W16(e, 0x13, blocksUsed);
            e[0x15] = (byte)f.Data.Length; e[0x16] = (byte)(f.Data.Length >> 8); e[0x17] = (byte)(f.Data.Length >> 16);
            stamp.CopyTo(e[0x18..]);
            e[0x1C] = 0; e[0x1D] = 0; e[0x1E] = 0xE3; // destroy, rename, write, read
            W16(e, 0x1F, f.AuxType);
            stamp.CopyTo(e[0x21..]);
            W16(e, 0x25, 2);
        }
        // The free-block bitmap: a set bit is a free block.
        for (int b = 0; b < totalBlocks; b++)
            if (!used[b]) disk[6 * 512 + b / 8] |= (byte)(0x80 >> (b % 8));
        return disk;

        void WriteIndex(Span<byte> index, int[] blocks, int from)
        {
            for (int n = 0; n < 256 && from + n < blocks.Length; n++) { index[n] = (byte)blocks[from + n]; index[256 + n] = (byte)(blocks[from + n] >> 8); }
        }
    }

    private static int BlocksFor(int length)
    {
        int data = Math.Max(1, (length + 511) / 512);
        return data == 1 ? 1 : data <= 256 ? data + 1 : data + 1 + (data + 255) / 256;
    }

    private static byte[] ProDosTime(DateTime t)
    {
        int date = ((t.Year % 100) << 9) | (t.Month << 5) | t.Day;
        return new[] { (byte)date, (byte)(date >> 8), (byte)t.Minute, (byte)t.Hour };
    }

    /// <summary>A .2mg: the 64-byte header, then the volume's blocks in ProDOS order.</summary>
    public static byte[] Wrap2mg(byte[] blocks)
    {
        var img = new byte[64 + blocks.Length];
        Encoding.ASCII.GetBytes("2IMG").CopyTo(img, 0);
        Encoding.ASCII.GetBytes("SCRM").CopyTo(img, 4); // the creator: Sandcastle RPG Maker
        BitConverter.GetBytes((ushort)64).CopyTo(img, 8);
        BitConverter.GetBytes((ushort)1).CopyTo(img, 10);
        BitConverter.GetBytes(1).CopyTo(img, 12); // ProDOS order
        BitConverter.GetBytes(0).CopyTo(img, 16); // flags
        BitConverter.GetBytes(blocks.Length / 512).CopyTo(img, 20);
        BitConverter.GetBytes(64).CopyTo(img, 24);
        BitConverter.GetBytes(blocks.Length).CopyTo(img, 28);
        blocks.CopyTo(img, 64);
        return img;
    }

    /// <summary>Reads the files back from a volume's blocks (or a .2mg) - for checking what Build wrote.</summary>
    public static List<(string Name, byte FileType, ushort AuxType, byte[] Data)> ReadFiles(byte[] image)
    {
        int off = image.Length >= 64 && Encoding.ASCII.GetString(image, 0, 4) == "2IMG" ? BitConverter.ToInt32(image, 24) : 0;
        int B(int b) => off + b * 512;
        int R16(int at) => image[at] | image[at + 1] << 8;
        var list = new List<(string, byte, ushort, byte[])>();
        for (int blk = 2; blk != 0; blk = R16(B(blk) + 2))
            for (int n = 0; n < 13; n++)
            {
                int e = B(blk) + 4 + n * 0x27;
                int st = image[e] >> 4, len = image[e] & 15;
                if (st is 0 or 0xF or 0xE || len == 0) continue;
                string name = Encoding.ASCII.GetString(image, e + 1, len);
                int key = R16(e + 0x11), eof = image[e + 0x15] | image[e + 0x16] << 8 | image[e + 0x17] << 16;
                var blocks = new List<int>();
                if (st == 1) blocks.Add(key);
                else if (st == 2) for (int i = 0; i < 256; i++) blocks.Add(image[B(key) + i] | image[B(key) + 256 + i] << 8);
                else if (st == 3)
                    for (int x = 0; x < 128; x++)
                    {
                        int ib = image[B(key) + x] | image[B(key) + 256 + x] << 8;
                        if (ib == 0) break;
                        for (int i = 0; i < 256; i++) blocks.Add(image[B(ib) + i] | image[B(ib) + 256 + i] << 8);
                    }
                var data = new byte[eof];
                for (int i = 0; i * 512 < eof; i++) Array.Copy(image, B(blocks[i]), data, i * 512, Math.Min(512, eof - i * 512));
                list.Add((name, image[e + 0x10], (ushort)R16(e + 0x1F), data));
            }
        return list;
    }
}

/// <summary>Super Hi-Res pictures: 12-bit colours, one 16-colour palette, 4 bits a pixel (two pixels a byte, the left one in the high half).</summary>
/// <summary>Ultima 6 Apple IIgs mode: the 24-bit pictures Build reduced (kept for the Colour Preview, never written to GAME.DAT).</summary>
public sealed class AppleIIgsPreviewData
{
    public List<uint[]> Tiles { get; } = new();
    public List<(uint[] Px, int W, int H)> Sprites { get; } = new();
    public List<uint[]> Avatar { get; } = new();
    public uint[]? Hud { get; set; }
    public uint[]? Title { get; set; }
    public List<string> MapNames { get; } = new();
}

public static class SuperHiRes
{
    /// <summary>
    /// What the reduction to the IIgs's 16 colours (of 4096) costs pictures: colours that are clearly different becoming
    /// the same, the edges between different colours that vanish, smooth steps (gradients) turning into bands, and how far
    /// the colours move on average. Notes for the Apple IIgs Check; none if the colours come through well.
    /// </summary>
    public static List<string> ColourNotes(ushort[] palette, IEnumerable<(uint[] Px, int W, int H)> pictures)
    {
        var notes = new List<string>();
        var index = new Dictionary<uint, int>();
        var uses = new Dictionary<uint, long>();
        long pairs = 0, lost = 0, smooth = 0, bands = 0, n = 0;
        double err = 0;
        int Idx(uint c) { if (!index.TryGetValue(c, out int i)) index[c] = i = Nearest(palette, c | 0xFF000000u, false); return i; }
        static double Dist(uint a, uint b)
        {
            int dr = (int)(a >> 16 & 255) - (int)(b >> 16 & 255), dg = (int)(a >> 8 & 255) - (int)(b >> 8 & 255), db = (int)(a & 255) - (int)(b & 255);
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }
        uint Shown(int i) => FromRgb12(palette[i]) & 0xFFFFFF;
        foreach (var (px, w, h) in pictures)
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    uint a = px[y * w + x];
                    if ((a >> 24) < 128) continue;
                    a &= 0xFFFFFF;
                    int ia = Idx(a);
                    uses[a] = uses.GetValueOrDefault(a) + 1;
                    err += Dist(a, Shown(ia));
                    n++;
                    for (int k = 0; k < 2; k++)
                    {
                        int nx = k == 0 ? x + 1 : x, ny = k == 0 ? y : y + 1;
                        if (nx >= w || ny >= h) continue;
                        uint b = px[ny * w + nx];
                        if ((b >> 24) < 128) continue;
                        b &= 0xFFFFFF;
                        double d = Dist(a, b);
                        if (d == 0) continue;
                        pairs++;
                        int ib = Idx(b);
                        if (ia == ib && d >= 24) lost++;
                        if (d <= 32) { smooth++; double sd = Dist(Shown(ia), Shown(ib)); if (sd >= 24 && sd >= 4 * d) bands++; }
                    }
                }
        if (n == 0) return notes;
        // Clearly different colours, much used, that become one.
        var common = uses.OrderByDescending(kv => kv.Value).Take(200).Select(kv => kv.Key).ToList();
        var merged = new List<string>();
        for (int i = 0; i < common.Count && merged.Count < 4; i++)
            for (int j = i + 1; j < common.Count && merged.Count < 4; j++)
                if (Idx(common[i]) == Idx(common[j]) && Dist(common[i], common[j]) >= 48)
                    merged.Add($"#{common[i]:X6} and #{common[j]:X6} both show as ${palette[Idx(common[i])]:X3}");
        int groups = uses.Keys.Select(Idx).Distinct().Count();
        if (merged.Count > 0)
            notes.Add($"Note: colours that look clearly different become the same on the Apple IIgs ({string.Join("; ", merged)}). Your {uses.Count} colours become {groups} of the 16 - see Game > Apple IIgs Colour Preview.");
        if (pairs > 0 && lost * 100 / pairs >= 5)
            notes.Add($"Note: {lost * 100 / pairs}% of the edges between different colours vanish on the Apple IIgs (both sides become the same colour): details are lost - see Game > Apple IIgs Colour Preview.");
        if (smooth > 200 && bands * 100 / smooth >= 2)
            notes.Add($"Note: smooth shading (gradients) turns into bands on the Apple IIgs: {bands * 100 / smooth}% of the gentle steps between colours become hard jumps - fewer, flatter shades will look better - see Game > Apple IIgs Colour Preview.");
        if (err / n >= 30)
            notes.Add($"Note: on average the colours move {(int)(err / n)} (of 441) on the Apple IIgs - they will look noticeably different - see Game > Apple IIgs Colour Preview.");
        return notes;
    }

    /// <summary>An ARGB colour to the IIgs's $0RGB.</summary>
    public static ushort To12Bit(uint argb) => (ushort)((((argb >> 16) & 0xFF) * 15 + 127) / 255 << 8 | (((argb >> 8) & 0xFF) * 15 + 127) / 255 << 4 | ((argb & 0xFF) * 15 + 127) / 255);

    public static uint FromRgb12(ushort c) => 0xFF000000u | (uint)((c >> 8 & 15) * 17) << 16 | (uint)((c >> 4 & 15) * 17) << 8 | (uint)((c & 15) * 17);

    /// <summary>
    /// The 16 colours that best show every pixel given (median cut on their 12-bit colours, most-used first). Colour 0 is
    /// black: in a sprite it is see-through (the IIgs player draws colour 0 of a sprite as nothing).
    /// </summary>
    /// <summary>The palette entry nearest a colour (4 bits each).</summary>
    public static int NearestColour(ushort[] Palette, int r, int g, int b)
    {
        int best = 0, bestD = int.MaxValue;
        for (int i = 0; i < Palette.Length; i++)
        {
            int c = Palette[i], d = Sq(((c >> 8) & 15) - r) + Sq(((c >> 4) & 15) - g) + Sq((c & 15) - b);
            if (d < bestD) { bestD = d; best = i; }
        }
        return best;
        static int Sq(int v) => v * v;
    }

    public static ushort[] MakePalette(IEnumerable<uint> pixels)
    {
        var counts = new Dictionary<ushort, int>();
        foreach (uint p in pixels)
        {
            if ((p >> 24) < 128) continue;
            ushort c = To12Bit(p);
            counts[c] = counts.GetValueOrDefault(c) + 1;
        }
        counts.Remove(0);
        var palette = new List<ushort> { 0 };
        if (counts.Count <= 15) palette.AddRange(counts.OrderByDescending(kv => kv.Value).Select(kv => kv.Key));
        else
        {
            var boxes = new List<List<KeyValuePair<ushort, int>>> { counts.ToList() };
            while (boxes.Count < 15)
            {
                var box = boxes.Where(b => b.Count > 1).OrderByDescending(b => Spread(b) * (long)b.Sum(kv => kv.Value)).FirstOrDefault();
                if (box is null) break;
                int ch = Channel(box);
                box.Sort((a, b) => (a.Key >> ch & 15).CompareTo(b.Key >> ch & 15));
                long half = box.Sum(kv => (long)kv.Value) / 2, run = 0;
                int cut = 1;
                for (; cut < box.Count - 1; cut++) { run += box[cut - 1].Value; if (run >= half) break; }
                boxes.Remove(box);
                boxes.Add(box.Take(cut).ToList());
                boxes.Add(box.Skip(cut).ToList());
            }
            foreach (var box in boxes)
            {
                long w = box.Sum(kv => (long)kv.Value), r = 0, g = 0, b = 0;
                foreach (var kv in box) { r += (kv.Key >> 8 & 15) * (long)kv.Value; g += (kv.Key >> 4 & 15) * (long)kv.Value; b += (kv.Key & 15) * (long)kv.Value; }
                ushort c = (ushort)((r + w / 2) / w << 8 | (g + w / 2) / w << 4 | (b + w / 2) / w);
                if (c == 0) c = 0x111; // black stays colour 0 alone
                palette.Add(c);
            }
        }
        while (palette.Count < 16) palette.Add(0);
        return palette.ToArray();

        static int Spread(List<KeyValuePair<ushort, int>> b) => Enumerable.Range(0, 3).Max(i => b.Max(kv => kv.Key >> (i * 4) & 15) - b.Min(kv => kv.Key >> (i * 4) & 15));
        static int Channel(List<KeyValuePair<ushort, int>> b)
        {
            int best = 0, ch = 0;
            for (int i = 0; i < 3; i++) { int s = b.Max(kv => kv.Key >> (i * 4) & 15) - b.Min(kv => kv.Key >> (i * 4) & 15); if (s > best) { best = s; ch = i * 4; } }
            return ch;
        }
    }

    /// <summary>The palette colour nearest a pixel; a see-through pixel (or a black one, when see-through is allowed) is colour 0.</summary>
    public static int Nearest(ushort[] palette, uint argb, bool spriteZeroIsClear)
    {
        if ((argb >> 24) < 128) return 0;
        ushort c = To12Bit(argb);
        int best = -1, bestD = int.MaxValue;
        for (int i = spriteZeroIsClear ? 1 : 0; i < palette.Length; i++)
        {
            int dr = (palette[i] >> 8 & 15) - (c >> 8 & 15), dg = (palette[i] >> 4 & 15) - (c >> 4 & 15), db = (palette[i] & 15) - (c & 15);
            int d = dr * dr * 3 + dg * dg * 4 + db * db * 2;
            if (d < bestD) { bestD = d; best = i; }
        }
        return best < 0 ? 0 : best;
    }

    /// <summary>A w x h picture (ARGB, row by row) as Super Hi-Res bytes: w/2 bytes a row.</summary>
    public static byte[] Pack(uint[] argb, int w, int h, ushort[] palette, bool spriteZeroIsClear)
    {
        var bytes = new byte[w / 2 * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x += 2)
                bytes[y * (w / 2) + x / 2] = (byte)(Nearest(palette, argb[y * w + x], spriteZeroIsClear) << 4 | Nearest(palette, argb[y * w + x + 1], spriteZeroIsClear));
        return bytes;
    }
}

/// <summary>
/// GAME.DAT - everything the Apple IIgs player (Player/iigs) reads, little-endian:
///   "SCGS", u16 version, u16 flags
///   palette: 16 x u16 ($0RGB)
///   title: u8 length + text (at most 60)
///   tiles: u16 count, then count x 128 bytes (16 x 16, 4 bits a pixel), then count x u8 (bit 0: walkable, bit 1: water),
///          then count x u16 its name (Tile Properties' terrain name; $FFFF none)
///   map: u16 width, u16 height, u16 start x, u16 start y, then width x height u16 tile numbers (row by row)
///   avatar: u16 frames (one per facing: south, north, east, west), each 128 bytes (16 x 16, colour 0 see-through)
///   sprites: u16 count, count x u32 offset (from the first sprite), u32 all the sprites' bytes, then each: u16 width (a multiple of 4, at most 32),
///            u16 height (at most 32), width/2 x height bytes (colour 0 see-through)
///   objects: u16 count, each 16 bytes: u16 x, u16 y, u16 sprite, u16 flags, u16 name, u16 other sprite, u16 icon, u16 0
///            - top to bottom, then the Avatar's things. flags: 1 solid, 2 can be taken, 4 a door, 8 locked, 16 open,
///            32 gone, 64 carried by the Avatar (x, y $FFFF). A door's other sprite: its open look while closed, its
///            closed look while open. Its icon: its look fitted into the inventory's cell ($FFFF none).
///   characters: u16 count, each 16 bytes: u16 x, u16 y, u16 sprite (south; +1 north, +2 east, +3 west), u16 flags,
///               u16 facing, u16 talk (a script; $FFFF none), u16 portrait (a sprite the portrait's size; $FFFF none), u16 name
///   hud: u16 1 = a HUD (then 32000 bytes: the 320 x 200 background, 4 bits a pixel), 0 = none (the world in the middle)
///        u16 view x (even), view y, columns, rows (the Game View, 16 x 16 cells); u16 text x, y, width, height
///        u16 labels, each: u16 x, u16 y (top), u16 colour (0-15; $FFFF: the brightest), u8 length, text, 0
///        u16 portrait x, y, width, height; u16 name x, y, width, height; u16 text colour, keyword colour ($FFFF: the brightest)
///        u16 carried things' grid: x, y, columns, rows, cell (columns 0: none)
///        u16 the panel: x, y, width, height (width 0: none), then its portrait view: width/2 x height bytes (the HUD as it
///        is there in that view, the member's portrait in it), u16 labels (its stats, as the labels above)
///   words: u16 count, each u8 length, text, 0 - you say, bye, no help, funny no response, talk, nothing, talking to
///          yourself, you see {0}, {1} gets {0}, not possible, out of range, look, get, use, locked, drop, where?,
///          {1} drops {0}, the Avatar's name (always the last)
///   names: u16 count, each u8 length, text, 0 (objects', characters', tiles' names)
///   variables: u16 count, i16 each (their starting values; switches are variables 0 / 1)
///   talks: u16 count, count x u32 offset, u32 all their bytes, then each: u8 length, the speaker's name, 0, its code
///          (Engine/AppleIIgsTalk.cs)
///   sounds: u16 count, then each: u8 name length + name, u16 rate, u32 length, u16 the Sound Tool Set's frequency offset
///           (rate x 32 / 1645), u8 volume (0-255) - sound n's bytes are the file SND.n, from 1
///   the Disk Swap screen: u16 1 = a picture (then 32000 bytes) / 0; u16 animation frames, x, y, then a sprite each;
///           u16 words x, y, colour; u8 length, the words ({0}: the disk's name), 0; u16 its sound ($FFFF none)
/// A sprite stands on the bottom middle of its cell, as in the Windows Ultima 6 engine.
/// Version 9: the map, its tiles, the Avatar, objects (containers and what is in them), characters, the HUD, talk, the verbs, the inventory and the sounds.
/// </summary>
public sealed class AppleIIgsGamePack
{
    public const ushort Version = 56;
    public ushort[] Palette { get; set; } = new ushort[16];
    /// <summary>The 24-bit pictures reduced into it (the editor's Colour Preview and the check; not written).</summary>
    public AppleIIgsPreviewData? Preview { get; set; }
    public string Title { get; set; } = "";
    public List<byte[]> Tiles { get; } = new();
    public List<byte> TileFlags { get; } = new();
    public int MapWidth { get; set; }
    public int MapHeight { get; set; }
    public int StartX { get; set; }
    public int StartY { get; set; }
    public ushort[] Map { get; set; } = Array.Empty<ushort>();
    public List<byte[]> AvatarFrames { get; } = new();
    public List<(string Name, int Rate, byte[] Data)> Sounds { get; } = new();
    /// <summary>Each sound's volume, 0-255 (255 when not given).</summary>
    public List<int> SoundVolumes { get; } = new();
    public byte[]? SwapPicture { get; set; }
    public List<int> SwapFrames { get; } = new();
    public (int X, int Y) SwapAnimation { get; set; }
    public (int X, int Y, int Color) SwapText { get; set; } = (16, 120, 0xFFFF);
    /// <summary>The Disk Swap screen's words as the HUD Editor has them: {0} the disk's number, {$} its name.</summary>
    public string SwapMessage { get; set; } = "Please insert the disk \"{$}\" and press a key.";
    public int SwapSound { get; set; } = 0xFFFF;
    public List<(int Width, int Height, byte[] Data)> Sprites { get; } = new();
    /// <summary>Flags: 1 solid, 2 can be taken, 4 a door, 8 locked, 16 open, 32 gone, 64 carried, 128 a container, 256 inside a
    /// container (In: that container's place in this list), 512 a key, 1024 a lockpick, 2048 magically locked; the top 4 bits
    /// its map. A key: Other = its key code (its Object Editor id). A door: Icon = its pick-lock difficulty, In = the key
    /// code that fits it ($FFFF none). A container: Other = the key code that fits it, Icon its look carried.</summary>
    public List<(int X, int Y, int Sprite, int Flags, int Name, int Other, int Icon, int In)> Objects { get; } = new();
    /// <summary>The HUD's Carried Items grid (Columns 0: none).</summary>
    public (int X, int Y, int Columns, int Rows, int Cell) CarriedGrid { get; set; }
    /// <summary>The HUD's panel (Width 0: none) and its portrait view: the pixels there (width/2 x height bytes) and its words (the stats).</summary>
    public (int X, int Y, int Width, int Height) PanelRect { get; set; }
    public byte[]? PortraitPanel { get; set; }
    public List<(int X, int Y, int Color, string Text)> PortraitLabels { get; } = new();
    public List<int> TileNames { get; } = new();
    public List<string> Names { get; } = new();
    /// <summary>A name's number in Names (the same name once); "" = $FFFF.</summary>
    public int NameOf(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return 0xFFFF;
        int at = Names.IndexOf(name);
        if (at < 0) { at = Names.Count; Names.Add(name); }
        return at;
    }
    public List<(int X, int Y, int Sprite, int Flags, int Facing, int Talk, int Portrait, int Name, int[] Stats)> Characters { get; } = new();
    public (int X, int Y, int Width, int Height) PortraitRect { get; set; }
    public (int X, int Y, int Width, int Height) NameRect { get; set; }
    public int TextColor { get; set; } = 0xFFFF;
    public int KeywordColor { get; set; } = 0xFFFF;
    /// <summary>you say, bye, no help, funny no response, talk, nothing, talking to yourself.</summary>
    public List<string> Words { get; } = new() { "you say: ", "bye", "\"I cannot help thee with that.\"", "Funny, no response.", "Talk", "nothing", "Talking to yourself?",
        "You see {0}.", "{1} gets {0}.", "Not possible.", "Out of range!", "Look", "Get", "Use", "Locked.", "Drop", "Where?", "{1} drops {0}.",
        "Move", "Searching here, you find {0}.", "Searching here, you find nothing.", "Quit the game? (Y/N) ", "Game saved.", "Game loaded.", "No saved game.",
        "Attack", "{1} attacks {0}.", "Missed!", "{0} barely wounded.", "{0} lightly wounded.", "{0} heavily wounded.", "{0} critically wounded!", "{0} killed!",
        "Begin combat!", "Break off combat!", "Pass", "Combat", "Press a key...", "{0} is knocked out!", "No response.",
        "{1} is now level {0}!", "Rest", "-Not while in Combat!", "-Not while foes are near!", "How many hours? ", "The party is full: {1} doesn't join.",
        "On what? ", "Unlocked.", "The key doesn't fit.", "The lockpick broke!", "Locked.", "{1} readies {0}.", "{1} puts away {0}.",
        "Cast", "No spellbook is readied.", "No spells in the spellbook.", "That spell is not in thy spellbook!", "Your level is not high enough.",
        "Not enough magic points.", "No Reagents.", "On whom? ", "{1} has healed.", "<more>", "There is nothing written.",
        "Introduction", "Create New Character", "Acknowledgments", "Journey Onward",
        "To whom? ", "{1} gets {0}.", "{1} eats {0}.", "You are at {0}.",
        "-Not while others are near!", "Who will guard? ", "none", "Mealtime!", "{1} has food.", "{1} has no food.", "{1} stands guard while the party rests.",
        "{1} gains {0}.", "{1} is not ready.", "strength", "dexterity", "intelligence", "Too heavy!", "Solo mode: {1}.", "Party mode.", "Not in solo mode.", "-Not in solo mode!", "{0} goes off!", "{0} has burnt out.", "{1} is cold.", "{1} is freezing!", "{1} is hot.", "{1} is burning up!", "Avatar" };
    public static readonly string[] WordIds = { "YouSay", "Bye", "NoHelp", "FunnyNoResponse", "Talk", "Nothing", "TalkSelf", "YouSee", "Gets", "NotPossible", "OutOfRange", "Look", "Get", "Use", "Locked", "Drop", "WhatPlace", "Drops",
        "Move", "Searching", "SearchNothing", "QuitAsk", "Saved", "Loaded", "NoSave",
        "Attack", "Attacks", "Missed", "Barely", "Lightly", "Heavily", "Critical", "Killed", "CombatBegin", "CombatBreakOff", "Pass", "Combat", "PressAKey", "KnockedOut", "NoResponse",
        "NewLevel", "Rest", "NotWhileCombat", "FoesNear", "HowManyHours", "PartyFull", "OnWhat", "Unlocked", "KeyNoFit", "PickBroke", "Locked", "Readies", "Unreadies",
        "Cast", "NoSpellbook", "NoSpells", "NotInBook", "LevelTooLow", "NoMagicPoints", "NoReagents", "OnWhom", "Healed", "More", "NothingWritten", "TitleIntro", "TitleCreate", "TitleAcks", "TitleJourney", "ToWhom", "Gives", "Eats", "Locate",
        "OthersNear", "WhoGuards", "NoOne", "Mealtime", "HasFood", "NoFood", "StandsGuard",
        "Gains", "NotReady", "Strength", "Dexterity", "Intelligence", "TooHeavy", "Solo", "PartyMode", "NotInSolo", "RestNotSolo", "TrapOff", "BurntOut", "TempCold", "TempFreezing", "TempHot", "TempBurning" };
    public List<short> Variables { get; } = new();
    /// <summary>A character's 24 numbers after its first 16 bytes (64 bytes a character): hit points, most hit points,
    /// strength, dexterity, intelligence, combat, magic, mana, most mana, level, experience, armour, weapon damage, weapon
    /// reach, behaviour (0 stays, 1 wanders, 2 its schedule), home x, home y, then free.</summary>
    public const int StatWords = 24;
    public static class Stat
    {
        public const int Hp = 0, MaxHp = 1, Str = 2, Dex = 3, Int = 4, Combat = 5, Magic = 6, Mana = 7, MaxMana = 8, Level = 9, Exp = 10,
            Armour = 11, Weapon = 12, Reach = 13, Behaviour = 14, HomeX = 15, HomeY = 16, Food = 17;
    }
    public int[] AvatarStats { get; set; } = new int[StatWords];
    /// <summary>Schedules (Character Editor > Schedule), as Exult's: (the character's place in its map's list, the 3-hour
    /// period 0-7, the activity: 0 stand, 1 wander, 2 sleep, 3 pace east-west, 4 pace north-south, 5 loiter; the spot).</summary>
    public List<(int Character, int Period, int Activity, int X, int Y)> Schedules { get; } = new();
    public int StartHour { get; set; } = 6;
    /// <summary>The HUD's Body Slot pieces: the slot (EquipmentSlot + 1), where (x, y). A worn thing: carried (64) with its slot in the flags' top 4 bits.</summary>
    public List<(int Slot, int X, int Y)> BodySlots { get; } = new();
    /// <summary>A weapon's or armour's Other: 1 weapon / 2 armour (bits 14-15), its slot (bits 10-13), reach 1, 2, 4, 8 (bits 8-9), damage or armour points (0-255).</summary>
    public static int GearCode(bool weapon, int slot, int reach, int value) =>
        ((weapon ? 1 : 2) << 14) | ((slot & 15) << 10) | ((reach >= 8 ? 3 : reach >= 4 ? 2 : reach >= 2 ? 1 : 0) << 8) | Math.Clamp(value, 0, 255);
    /// <summary>Spells (Object Editor > Spellbook): name, words (a name's number), the syllables' first letters ("IL" for In Lor),
    /// circle, magic points, needs a target, up to 4 reagents (name, how many), up to 2 effects (kind, min, max), radius.</summary>
    public List<Spell> Spells { get; } = new();
    public sealed class Spell
    {
        public int Name, Words, Circle, Mana, Radius;
        public string Letters = "";
        public bool Target;
        public List<(int Name, int Count)> Reagents { get; } = new();
        /// <summary>Its effects: kind (MagicEffectKind), min, max, its thing (a summoned kind / an item / a common event's code: their
        /// place in Summons / Items / Codes; $FFFF none), seconds.</summary>
        public List<(int Kind, int Min, int Max, int Thing, int Seconds)> Effects { get; } = new();
    }
    /// <summary>The title menu (System Settings > Ultima 6 > Title): its picture (320 x 200, packed; null: black), the Acknowledgments' words,
    /// the Introduction (the intro commands as talk code; empty: no Introduction).</summary>
    public byte[]? TitlePicture { get; set; }
    public string Acknowledgments { get; set; } = "Made with Sandcastle RPG Maker.";
    public byte[] IntroCode { get; set; } = Array.Empty<byte>();
    /// <summary>The HUD's command buttons (a click = its verb's key): the key, where (x, y, width, height).</summary>
    public List<(char Key, int X, int Y, int W, int H)> Buttons { get; } = new();
    /// <summary>Line of sight (System Settings > Ultima 6 > World view). A cell's tile number's bit 15: a wall or something that
    /// Occludes stands there (it stops sight); bit 14: a wall, door or window's square (always shown).</summary>
    public bool LineOfSight { get; set; } = true;
    /// <summary>Weapons that shoot or are thrown (Object Editor > Weapon): its name, 0 nothing used / 1 ammo of a family / 2 it is
    /// thrown itself, the family's number, what flies (a sprite; $FFFF none). Ammo carries $C800 + its family in Other.</summary>
    public List<(int Name, int Uses, int Family, int Missile)> Weapons { get; } = new();
    /// <summary>Animated tiles (Tile Properties > animation): a tile and its frames' tiles (the first is itself), up to 4.</summary>
    public List<List<int>> AnimTiles { get; } = new();
    /// <summary>Day and night (System Settings > Day / Night): on, dawn, day, dusk and night's hours.</summary>
    public (bool On, int Dawn, int Day, int Dusk, int Night) DayNight { get; set; } = (true, 5, 7, 19, 21);
    /// <summary>Books and signs to read: each one's words as talk code (AppleIIgsTalk.CompileText). A book's Other: 3 (bits 14-15), slot 0, its place here.</summary>
    public List<byte[]> Texts { get; } = new();
    /// <summary>Spellbooks: the spells each knows (their places in Spells). A spellbook's Other: 3 (bits 14-15), its slot (a hand), its place here.</summary>
    public List<List<int>> Books { get; } = new();
    /// <summary>Things spells make (Create Item / Field): name, sprite, icon, flags, other.</summary>
    public List<(int Name, int Sprite, int Icon, int Flags, int Other)> Items { get; } = new();
    /// <summary>Kinds spells summon (Summon): a character's 64 bytes (its sprite, name, stats).</summary>
    public List<(int Sprite, int Name, int[] Stats)> Summons { get; } = new();
    /// <summary>Common events spells run, characters' and objects' own events: their commands as talk code.</summary>
    public List<byte[]> Codes { get; } = new();
    /// <summary>Characters' own events (Character Editor > Event): the character's map and place, its code in Codes.</summary>
    public List<(int Map, int Index, int Code)> CharEvents { get; } = new();
    /// <summary>Objects' and containers' own events (Object / Container Editor > Event): the thing's name, its code in Codes.</summary>
    public List<(int Name, int Code)> ObjEvents { get; } = new();
    /// <summary>Each map's ambient sounds (Edit > Map Ambient Sounds): dawn, day, dusk, night - a sound's number or $FFFF.</summary>
    public List<(int Dawn, int Day, int Dusk, int Night)> Ambient { get; } = new();
    /// <summary>Zones with a sound of their own, or Caves (silence: $FFFF): map, x, y, width, height, sound - the one to use first first.</summary>
    public List<(int Map, int X, int Y, int W, int H, int Sound, bool Cave)> MusicZones { get; } = new();
    /// <summary>Each map's weather (Edit > Map Weather): kind (0 clear, 1 rain, 2 snow, 3 hail, 4 storm), intensity, clouds,
    /// cloud cover, the wind (x, y: -1, 0, 1), changes by itself, its chance an hour, the rain and thunder sounds ($FFFF none).</summary>
    public List<int[]> Weather { get; } = new();
    /// <summary>Vehicles (Map Editor > Vehicles), built: map, vehicle (template) id, footprint in cells, movement (0 land,
    /// 1 sea, 2 air), ticks a cell, deed's name ($FFFF none), its words, start / moving / stop sounds.</summary>
    public List<(int Map, int Template, int Left, int Top, int W, int D, int Move, int Ticks, int Deed, string DeedText, int SndStart, int SndMove, int SndStop)> Vehicles { get; } = new();
    /// <summary>Their pieces: vehicle, object (its number), role (0 none, 1 driver's seat, 2 controls, 3 wheel), its look
    /// built (south) and turned 1, 2, 3 quarters clockwise, a wheel's moving look ($FFFF none).</summary>
    public List<(int Vehicle, int Object, int Role, int[] Looks, int Moving)> VehicleParts { get; } = new();
    /// <summary>Ultima 6 leveling (Leveling Editor): levels rise by themselves, or are only earned (shrines, dreams).</summary>
    public bool RiseByThemselves { get; set; } = true;
    public int StatCap { get; set; } = 30;
    /// <summary>The leveling events: 21 when someone earns a level, 22 when the party wakes from Rest, 23 the same once for
    /// each member who has earned a level - their code in Codes.</summary>
    public List<(int Trigger, int Code)> LevelEvents { get; } = new();
    /// <summary>The variables the leveling events read: LevelWhoId, LevelNow, LevelEarned, LevelRose, RestHours.</summary>
    public int[] LevelVars { get; set; } = { 0xFFFF, 0xFFFF, 0xFFFF, 0xFFFF, 0xFFFF };
    /// <summary>Characters' ids by their names (LevelWhoId); the Avatar's.</summary>
    public List<(int Name, int Id)> CharIds { get; } = new();
    public int AvatarId { get; set; }
    /// <summary>Plants, tools, crafting stations, musical instruments: their settings (an object's Other $D000 | its number).
    /// Plant: 1, stages (4 sprites), hours a stage, produce (name, sprite, icon, flags, other), how many, tool kind, only when
    /// ripe, stage after, taken whole, sound, soil left (an item). Tool: 2, kind, sound. Station: 3, working sprite, hint
    /// (a name), recipes (tool kind, ingredients (name, how many), output (an item), how many, ticks, sound). Instrument:
    /// 4, tunes (sounds). Useable (a wand, a scroll): 5, its spell (Spells), charges (0 no limit), gone when used up, asks
    /// for a target, its used-up look, sound. Potion: 6, its spell, sound, what is left (an item).</summary>
    public List<byte[]> Specials { get; } = new();
    /// <summary>The HUD's dials: 0 the compass (8 frames, by the wind), 1 the sun and moons (frames over the day) - place, size,
    /// their first frame's sprite, how many.</summary>
    public List<(int Kind, int X, int Y, int W, int H, int First, int Frames)> Dials { get; } = new();
    /// <summary>(made by the HUD's composing: the dials' frames, before they are sprites)</summary>
    public List<(int Kind, int X, int Y, int W, int H, List<uint[]> Frames)> PendingDials { get; } = new();
    /// <summary>The HUD's date-and-wind words: place, colour.</summary>
    public List<(int X, int Y, int Color)> DateTexts { get; } = new();
    /// <summary>Bodies (Character Editor > Health > Body): by a character's name its Body pose's picture ($FFFF none),
    /// 1 leaves no body, 2 too heavy to pick up; the Avatar's picture; the bodies' name (System Settings > Body Container).</summary>
    public List<(int Name, int Sprite, int Flags)> CharBodies { get; } = new();
    /// <summary>The maps that wrap around (Maps tab: Wraps around the edges) - their numbers (a world's upper floors too).</summary>
    public List<int> WrapMaps { get; } = new();
    /// <summary>Weights by a thing's name (Object / Container / Book Editor's weight) in tenths, and whether it is a bag that
    /// weighs only itself (a bag of holding): the carry limit, 2 x Strength (U6GameWindow.Weight.cs).</summary>
    public List<(int Name, int Tenths, bool Bag)> Weights { get; } = new();
    /// <summary>The Avatar's four looks as sprites (south, north, east, west): solo mode draws the Avatar as a member.</summary>
    public int AvatarSprite { get; set; }
    /// <summary>Traps by name (Object Editor > Trap), 12 words each - floor.s: name, damage min, max, ailment bit (1 poisoned,
    /// 2 asleep, 4 paralyzed), flags (1 hidden, 2 one-shot), re-arm turns, sound ($FFFF none), kind (0 ground, 1 explosive,
    /// 2 shooting), radius / range in cells, the projectile's sprite ($FFFF none), 1 fires at whoever is in its line of fire,
    /// 1 gone after it explodes. A placed shooting trap's facing: its object's Icon (0 south, 1 north, 2 east, 3 west).</summary>
    public List<int[]> Traps { get; } = new();
    /// <summary>Light sources that burn down or burn (Object Editor > Light Source): name, burn minutes (0 never), fire damage min, max (max 0: not a fire).</summary>
    public List<(int Name, int Minutes, int FireMin, int FireMax)> Lights { get; } = new();
    /// <summary>Ultima 6's blood under a body: the Blood sprite's frames as sprites (first, how many; $FFFF none) and its name ("blood").</summary>
    public (int First, int Frames, int Name) Blood { get; set; } = (0xFFFF, 0, 0xFFFF);
    /// <summary>Levers, switches, buttons (an object's Other $FFFB): object, kind (0 lever, 1 switch, 2 button), its other look,
    /// on / off sounds, reset turns, on, the objects it is linked to (4 at most) - lever.s.</summary>
    public List<(int Obj, int Kind, int Other, int OnSnd, int OffSnd, int Reset, bool On, int[] Links)> Levers { get; } = new();
    /// <summary>Characters' combat modes by name (CombatModeCode; Front not listed) - solo.s's ModeTab.</summary>
    public List<(int Name, int Mode)> Modes { get; } = new();
    /// <summary>Cold (1) and hot (2) zones (Map Editor > Zones): map, x, y, width, height, kind; worn gear's warmth and cooling by name;
    /// characters immune (1 cold, 2 heat) by name - temp.s.</summary>
    public List<(int Map, int X, int Y, int W, int H, int Kind)> TempZones { get; } = new();
    public List<(int Name, int Warmth, int Cooling)> Gear { get; } = new();
    public List<(int Name, int Flags)> TempImmune { get; } = new();
    /// <summary>Doors beyond the swing (door.s), by name: opening frames (first sprite, how many), closing frames, flags (1 with
    /// neighbours, 2 by itself, 4 not by hand), within cells, stays-open turns, open / close sounds, ticks a frame.</summary>
    public List<int[]> Doors { get; } = new();
    /// <summary>Monsters that join (Character Editor > Health > Joining), by name: flags (1 splits when hit), their joined look's
    /// first sprite of 32 ($FFFF none) - join.s. Each map gets spare copies of the ones that split (flags 4 + $8000).</summary>
    public List<(int Name, int Flags, int Look)> Joiners { get; } = new();
    /// <summary>Magic worn or held (Object Editor > Weapon / Armor > Magic), by name: reach cells (0 none), defense, light radius
    /// (0 none), strength, dexterity, intelligence - worn.s.</summary>
    public List<int[]> WornMagic { get; } = new();
    /// <summary>Damage types (tough.s): weapons' types by name; characters' responses by name (type, 1 immune / 2 half / 3 adjust,
    /// amount); those who get back up (least / most turns, the types that stop it as bits).</summary>
    public List<(int Name, int Type)> WeaponTypes { get; } = new();
    public List<(int Name, int Type, int Kind, int Amount)> Responses { get; } = new();
    public List<(int Name, int Least, int Most, int NotFrom)> Risers { get; } = new();
    /// <summary>Weapons drawn in the hand (inhand.s): character looks' hand points (sprite, x, y from its standing point,
    /// behind) and weapons' held looks (name, held sprite, mirrored sprite, grip x, y from its standing point).</summary>
    public List<(int Sprite, int X, int Y, bool Behind)> Hands { get; } = new();
    public List<(int Name, int Held, int Mirrored, int GripX, int GripY)> HeldWeapons { get; } = new();
    /// <summary>A character's combat mode in its word 20: 0 Front, 1 Rear, 2 Flank, 3 Berserk, 4 Retreat, 5 Assault, 6 Command.</summary>
    public static int CombatModeCode(string mode) => mode switch { "Rear" => 1, "Flank" => 2, "Berserk" => 3, "Retreat" => 4, "Assault" => 5, "Command" => 6, _ => 0 };
    public int AvatarBody { get; set; } = 0xFFFF;
    public int BodyName { get; set; } = 0xFFFF;
    /// <summary>Spawn points (eggs, Map Editor > Spawn): map, x, y, kind (0 monster, 1 music, 2 sound, 3 usecode, 4 missile,
    /// 5 teleport, 6 weather, 7 path, 8 button), when (0 map entered, 1 party near, 2 avatar near, 3 avatar far, 4 avatar on it,
    /// 5 party on it, 6 something put down, 7 a button), distance, chance, flags (1 nocturnal, 2 once, 4 auto-reset,
    /// 8 continuous), its code (Codes; $FFFF none), its stop code, missile sprite, damage min / max, direction ($FFFF at the
    /// party, else 0-7 N..NW), delay (ticks), range, weather minutes.</summary>
    public List<int[]> Eggs { get; } = new();
    /// <summary>Monster eggs' characters: map, place in its list (absent till hatched), egg, chance.</summary>
    public List<(int Map, int Index, int Egg, int Percent)> EggChars { get; } = new();
    /// <summary>Variables that hold a text from the start (Variable type Text: empty).</summary>
    public List<int> TextVariables { get; } = new();
    /// <summary>Each map's name (Get Game Data's Map name).</summary>
    public List<int> MapNames { get; } = new();
    /// <summary>Day & Night's overcast strength in 16ths.</summary>
    public int Overcast16 { get; set; } = 4;
    /// <summary>System Settings > Combat's music: danger, battle, battle 2, run away, escaped, victory, battle over, near death, death.</summary>
    public List<(int Sound, bool Loop)> CombatCues { get; } = new();
    /// <summary>Ultima 6's conversation music ($FFFF none); after a talk a random song of the map's.</summary>
    public int TalkMusic { get; set; } = 0xFFFF;
    public bool AfterTalkRandom { get; set; }
    /// <summary>The party's most, the Avatar with them (System Settings > Ultima 6, Party Size).</summary>
    public int PartySize { get; set; } = 9;
    public static void WriteSchedules(BinaryWriter w, List<(int Character, int Period, int Activity, int X, int Y)> list)
    {
        w.Write((ushort)list.Count);
        foreach (var e in list) { w.Write((ushort)e.Character); w.Write((ushort)e.Period); w.Write((ushort)e.Activity); w.Write((ushort)e.X); w.Write((ushort)e.Y); }
    }
    /// <summary>Ultima 6 levels (Leveling Editor): the experience each next level needs, hit points a level, most hit points; Off: Ultima 7's (most hit points = strength).</summary>
    public List<int> LevelNeeds { get; } = new();
    public int HitsPerLevel { get; set; } = 30;
    public int MostHits { get; set; } = 240;
    public bool U6Levels { get; set; } = true;
    public static void WriteStats(BinaryWriter w, int[]? stats)
    {
        for (int i = 0; i < StatWords; i++) w.Write((ushort)(stats is not null && i < stats.Length ? Math.Clamp(stats[i], -32768, 65535) : 0));
    }
    /// <summary>The first map's events: stepped on, their code runs (the talk code: AppleIIgsTalk).</summary>
    public List<(int X, int Y, byte[] Code)> Events { get; } = new();
    /// <summary>Each event's triggers (a bit each: 1 stepped on ... 8 picked up), as Events.</summary>
    public List<int> EventMasks { get; } = new();
    /// <summary>Maps 1, 2...: each its own file MAP.n, loaded when the game goes there (their objects are in Objects, flags' top 4 bits = the map).</summary>
    public List<AppleIIgsMap> OtherMaps { get; } = new();
    public List<(string Name, byte[] Code)> Talks { get; } = new();
    /// <summary>The HUD's background (320 x 200, 4 bits a pixel, 32000 bytes); null: no HUD.</summary>
    public byte[]? HudBackground { get; set; }
    public (int X, int Y, int Columns, int Rows) View { get; set; } = (80, 20, 10, 10);
    /// <summary>Where the words go; without a HUD two lines under the world.</summary>
    public (int X, int Y, int Width, int Height) TextArea { get; set; } = (8, 182, 304, 18);
    public List<(int X, int Y, int Color, string Text)> Labels { get; } = new();

    public byte[] Write()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("SCGS"));
        w.Write(Version);
        w.Write((ushort)0);
        foreach (ushort c in Palette) w.Write(c);
        var title = Encoding.ASCII.GetBytes(new string(Title.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').Take(60).ToArray()));
        w.Write((byte)title.Length); w.Write(title);
        w.Write((ushort)Tiles.Count);
        foreach (var t in Tiles) w.Write(t);
        foreach (var f in TileFlags) w.Write(f);
        for (int i = 0; i < Tiles.Count; i++) w.Write((ushort)(i < TileNames.Count ? TileNames[i] : 0xFFFF));
        w.Write((ushort)MapWidth); w.Write((ushort)MapHeight); w.Write((ushort)StartX); w.Write((ushort)StartY);
        foreach (ushort t in Map) w.Write(t);
        w.Write((ushort)AvatarFrames.Count);
        foreach (var f in AvatarFrames) w.Write(f);
        w.Write((ushort)Sprites.Count);
        int offset = 0;
        foreach (var sp in Sprites) { w.Write(offset); offset += 4 + sp.Data.Length; }
        w.Write(offset);
        foreach (var sp in Sprites) { w.Write((ushort)sp.Width); w.Write((ushort)sp.Height); w.Write(sp.Data); }
        w.Write((ushort)Objects.Count);
        // On the map top to bottom (then carried, then inside things); In follows its container to its new place.
        var order = Enumerable.Range(0, Objects.Count).OrderBy(i => (Objects[i].Flags & (64 | 256)) != 0 ? 1 : 0).ThenBy(i => Objects[i].Y).ThenBy(i => Objects[i].X).ToList();
        var newPlace = new int[Objects.Count];
        for (int i = 0; i < order.Count; i++) newPlace[order[i]] = i;
        foreach (int i in order)
        {
            var o = Objects[i];
            w.Write((ushort)o.X); w.Write((ushort)o.Y); w.Write((ushort)o.Sprite); w.Write((ushort)o.Flags); w.Write((ushort)o.Name); w.Write((ushort)o.Other);
            // In: a door's key, a carried thing's owner ($FFFF the Avatar, $8000 + map x 64 + place: a character), a thing inside: its container.
            w.Write((ushort)o.Icon); w.Write((ushort)((o.Flags & 4) != 0 || o.In >= 0x8000 ? o.In : o.In >= 0 && o.In < Objects.Count ? newPlace[o.In] : 0xFFFF));
        }
        w.Write((ushort)Characters.Count);
        foreach (var c in Characters)
        {
            w.Write((ushort)c.X); w.Write((ushort)c.Y); w.Write((ushort)c.Sprite); w.Write((ushort)c.Flags); w.Write((ushort)c.Facing);
            w.Write((ushort)c.Talk); w.Write((ushort)c.Portrait); w.Write((ushort)c.Name);
            AppleIIgsGamePack.WriteStats(w, c.Stats);
        }
        w.Write((ushort)(HudBackground is null ? 0 : 1));
        if (HudBackground is not null) w.Write(HudBackground);
        w.Write((ushort)(View.X & ~1)); w.Write((ushort)View.Y); w.Write((ushort)View.Columns); w.Write((ushort)View.Rows);
        w.Write((ushort)(TextArea.X & ~1)); w.Write((ushort)TextArea.Y); w.Write((ushort)(TextArea.Width & ~1)); w.Write((ushort)TextArea.Height);
        w.Write((ushort)Labels.Count);
        foreach (var (x, y, color, text) in Labels)
        {
            var t = Encoding.ASCII.GetBytes(new string(text.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').Take(40).ToArray()));
            w.Write((ushort)x); w.Write((ushort)y); w.Write((ushort)color); w.Write((byte)t.Length); w.Write(t); w.Write((byte)0);
        }
        foreach (var r in new[] { PortraitRect, NameRect }) { w.Write((ushort)r.X); w.Write((ushort)r.Y); w.Write((ushort)r.Width); w.Write((ushort)r.Height); }
        w.Write((ushort)TextColor); w.Write((ushort)KeywordColor);
        w.Write((ushort)(CarriedGrid.X & ~1)); w.Write((ushort)CarriedGrid.Y); w.Write((ushort)CarriedGrid.Columns); w.Write((ushort)CarriedGrid.Rows); w.Write((ushort)CarriedGrid.Cell);
        bool panel = PortraitPanel is not null && PanelRect.Width > 0;
        w.Write((ushort)(panel ? PanelRect.X & ~1 : 0)); w.Write((ushort)PanelRect.Y); w.Write((ushort)(panel ? PanelRect.Width & ~1 : 0)); w.Write((ushort)PanelRect.Height);
        if (panel) w.Write(PortraitPanel!);
        w.Write((ushort)PortraitLabels.Count);
        foreach (var (x, y, color, text) in PortraitLabels)
        {
            var t = Ascii(text, 40);
            w.Write((ushort)x); w.Write((ushort)y); w.Write((ushort)color); w.Write((byte)t.Length); w.Write(t); w.Write((byte)0);
        }
        w.Write((ushort)Words.Count);
        foreach (var word in Words) { var t = Ascii(word, 60); w.Write((byte)t.Length); w.Write(t); w.Write((byte)0); }
        w.Write((ushort)Names.Count);
        foreach (var name in Names) { var t = Ascii(name, 40); w.Write((byte)t.Length); w.Write(t); w.Write((byte)0); }
        w.Write((ushort)Variables.Count);
        foreach (short v in Variables) w.Write(v);
        w.Write((ushort)Talks.Count);
        var blobs = Talks.Select(t => { var n = Ascii(t.Name, 30); return new byte[] { (byte)n.Length }.Concat(n).Append((byte)0).Concat(t.Code).ToArray(); }).ToList();
        int at = 0;
        foreach (var b in blobs) { w.Write(at); at += b.Length; }
        w.Write(at);
        foreach (var b in blobs) w.Write(b);
        w.Write((ushort)Sounds.Count);
        foreach (var (name, rate, data) in Sounds)
        {
            var n = Encoding.ASCII.GetBytes(new string(name.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').Take(31).ToArray()));
            w.Write((byte)n.Length); w.Write(n); w.Write((ushort)rate); w.Write(data.Length);
            w.Write((ushort)Math.Clamp(rate * 32 / 1645, 1, 0xFFFF));
            int index = Sounds.FindIndex(x => ReferenceEquals(x.Data, data));
            w.Write((byte)(index >= 0 && index < SoundVolumes.Count ? SoundVolumes[index] : 255));
        }
        w.Write((ushort)(SwapPicture is null ? 0 : 1));
        if (SwapPicture is not null) w.Write(SwapPicture);
        w.Write((ushort)SwapFrames.Count); w.Write((ushort)(SwapAnimation.X & ~1)); w.Write((ushort)SwapAnimation.Y);
        foreach (int f in SwapFrames) w.Write((ushort)f);
        w.Write((ushort)SwapText.X); w.Write((ushort)SwapText.Y); w.Write((ushort)SwapText.Color);
        // The player's own marks: {0} the name, {2} the number (FillFmt).
        string before = SwapMessage == "Please insert the disk \"{0}\" and press a key." ? "Please insert the disk \"{$}\" and press a key." : SwapMessage; // the b626 words: {0} was the name then
        var m = Ascii(before.Replace("{0}", "{2}").Replace("{$}", "{0}"), 100);
        w.Write((byte)m.Length); w.Write(m); w.Write((byte)0);
        w.Write((ushort)SwapSound);
        w.Write((ushort)(1 + OtherMaps.Count));
        AppleIIgsMap.WriteEvents(w, Events, EventMasks);
        WriteStats(w, AvatarStats);
        w.Write((ushort)(U6Levels ? 1 : 0)); w.Write((ushort)HitsPerLevel); w.Write((ushort)MostHits);
        w.Write((ushort)Math.Min(LevelNeeds.Count, 16));
        foreach (int need in LevelNeeds.Take(16)) w.Write((ushort)Math.Clamp(need, 0, 65535));
        WriteSchedules(w, Schedules);
        w.Write((ushort)Math.Clamp(StartHour, 0, 23));
        w.Write((ushort)Math.Clamp(PartySize, 1, 9));
        w.Write((ushort)Math.Min(BodySlots.Count, 16));
        foreach (var (slot, x, y) in BodySlots.Take(16)) { w.Write((ushort)slot); w.Write((ushort)(x & ~1)); w.Write((ushort)y); }
        w.Write((ushort)Math.Min(Spells.Count, 128));
        foreach (var sp in Spells.Take(128))
        {
            w.Write((ushort)sp.Name); w.Write((ushort)sp.Words);
            var letters = Encoding.ASCII.GetBytes(new string(sp.Letters.ToUpperInvariant().Where(char.IsLetter).Take(7).ToArray()));
            w.Write(letters); for (int i = letters.Length; i < 8; i++) w.Write((byte)0);
            w.Write((ushort)sp.Circle); w.Write((ushort)sp.Mana); w.Write((ushort)(sp.Target ? 1 : 0));
            for (int i = 0; i < 4; i++) { var r = i < sp.Reagents.Count ? sp.Reagents[i] : (0xFFFF, 0); w.Write((ushort)r.Item1); w.Write((ushort)r.Item2); }
            for (int i = 0; i < 2; i++) { var e = i < sp.Effects.Count ? sp.Effects[i] : (0xFFFF, 0, 0, 0xFFFF, 0); w.Write((ushort)e.Item1); w.Write((ushort)e.Item2); w.Write((ushort)e.Item3); w.Write((ushort)e.Item4); w.Write((ushort)e.Item5); }
            w.Write((ushort)sp.Radius);
            for (int i = 0; i < 4; i++) w.Write((ushort)0); // 64 bytes
        }
        w.Write((ushort)Math.Min(Books.Count, 64));
        foreach (var book in Books.Take(64)) { w.Write((ushort)Math.Min(book.Count, 64)); foreach (int i in book.Take(64)) w.Write((ushort)i); }
        w.Write((ushort)Math.Min(Items.Count, 64));
        foreach (var it in Items.Take(64)) { w.Write((ushort)it.Name); w.Write((ushort)it.Sprite); w.Write((ushort)it.Icon); w.Write((ushort)it.Flags); w.Write((ushort)it.Other); }
        w.Write((ushort)Math.Min(Summons.Count, 32));
        foreach (var (sprite, name, st) in Summons.Take(32))
        {
            w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)sprite); w.Write((ushort)1); w.Write((ushort)0); w.Write((ushort)0xFFFF); w.Write((ushort)0xFFFF); w.Write((ushort)name);
            WriteStats(w, st);
        }
        w.Write((ushort)Math.Min(Codes.Count, 255));
        foreach (var code in Codes.Take(255)) { w.Write((ushort)code.Length); w.Write(code); }
        w.Write((ushort)Math.Min(CharEvents.Count, 256));
        foreach (var (cm, ci, cc) in CharEvents.Take(256)) { w.Write((ushort)cm); w.Write((ushort)ci); w.Write((ushort)cc); }
        w.Write((ushort)Math.Min(ObjEvents.Count, 128));
        foreach (var (n, c) in ObjEvents.Take(128)) { w.Write((ushort)n); w.Write((ushort)c); }
        w.Write((ushort)Ambient.Count);
        foreach (var (a, b, c, d) in Ambient) { w.Write((ushort)a); w.Write((ushort)b); w.Write((ushort)c); w.Write((ushort)d); }
        w.Write((ushort)Math.Min(MusicZones.Count, 256));
        foreach (var z in MusicZones.Take(256)) { w.Write((ushort)z.Map); w.Write((ushort)z.X); w.Write((ushort)z.Y); w.Write((ushort)z.W); w.Write((ushort)z.H); w.Write((ushort)z.Sound); w.Write((ushort)(z.Cave ? 1 : 0)); }
        for (int i = 0; i < 9; i++) { var (snd, loop) = i < CombatCues.Count ? CombatCues[i] : (0xFFFF, false); w.Write((ushort)snd); w.Write((ushort)(loop ? 1 : 0)); }
        w.Write((ushort)TalkMusic);
        w.Write((ushort)(AfterTalkRandom ? 1 : 0));
        // The weather: each map's, then the colours drops are drawn in (the palette's nearest) and the overcast strength.
        w.Write((ushort)Weather.Count);
        foreach (var wt in Weather) for (int i = 0; i < 10; i++) w.Write((ushort)(i < wt.Length ? wt[i] : 0));
        w.Write((ushort)SuperHiRes.NearestColour(Palette, 0xA, 0xB, 0xD));   // rain
        w.Write((ushort)SuperHiRes.NearestColour(Palette, 0xF, 0xF, 0xF));   // snow, hail
        w.Write((ushort)SuperHiRes.NearestColour(Palette, 0x0, 0x0, 0x0));   // cloud shadows
        w.Write((ushort)Math.Clamp(Overcast16, 0, 16));
        // Vehicles, their pieces, their deeds' words.
        var vtext = new List<byte>();
        w.Write((ushort)Math.Min(Vehicles.Count, 16));
        foreach (var v in Vehicles.Take(16))
        {
            int vat = vtext.Count;
            vtext.AddRange(System.Text.Encoding.ASCII.GetBytes(new string(v.DeedText.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').Take(100).ToArray())));
            vtext.Add(0);
            foreach (int n in new[] { v.Map, v.Template, v.Left, v.Top, v.W, v.D, v.Move, v.Ticks, v.Deed, vat, v.SndStart, v.SndMove, v.SndStop }) w.Write((ushort)n);
        }
        var parts = VehicleParts.Where(p => p.Vehicle < 16).Take(128).ToList();
        w.Write((ushort)parts.Count);
        foreach (var p in parts) { w.Write((ushort)p.Vehicle); w.Write((ushort)(p.Object < newPlace.Length ? newPlace[p.Object] : 0)); w.Write((ushort)p.Role); foreach (int l in p.Looks) w.Write((ushort)l); w.Write((ushort)p.Moving); }
        if (vtext.Count % 2 == 1) vtext.Add(0);
        w.Write((ushort)vtext.Count);
        w.Write(vtext.ToArray());
        // Ultima 6 leveling.
        w.Write((ushort)(RiseByThemselves ? 1 : 0));
        w.Write((ushort)Math.Clamp(StatCap, 1, 255));
        foreach (int v in LevelVars) w.Write((ushort)v);
        w.Write((ushort)AvatarId);
        w.Write((ushort)Math.Min(LevelEvents.Count, 32));
        foreach (var (t, c) in LevelEvents.Take(32)) { w.Write((ushort)t); w.Write((ushort)c); }
        w.Write((ushort)Math.Min(CharIds.Count, 256));
        foreach (var (n, i) in CharIds.Take(256)) { w.Write((ushort)n); w.Write((ushort)i); }
        // Texts and lists.
        w.Write((ushort)Math.Min(TextVariables.Count, 256));
        foreach (int v in TextVariables.Take(256)) w.Write((ushort)v);
        w.Write((ushort)Math.Min(MapNames.Count, 16));
        foreach (int n in MapNames.Take(16)) w.Write((ushort)n);
        // The HUD's dials and date words.
        w.Write((ushort)Math.Min(Dials.Count, 8));
        foreach (var d in Dials.Take(8)) { w.Write((ushort)d.Kind); w.Write((ushort)d.X); w.Write((ushort)d.Y); w.Write((ushort)d.W); w.Write((ushort)d.H); w.Write((ushort)d.First); w.Write((ushort)d.Frames); }
        w.Write((ushort)Math.Min(DateTexts.Count, 4));
        foreach (var d in DateTexts.Take(4)) { w.Write((ushort)d.X); w.Write((ushort)d.Y); w.Write((ushort)d.Color); }
        // Plants, tools, stations, instruments: their places, then their records.
        var specs = Specials.Take(256).ToList();
        w.Write((ushort)specs.Count);
        int specAt = 0;
        foreach (var sp in specs) { w.Write((ushort)specAt); specAt += sp.Length + (sp.Length & 1); }
        w.Write((ushort)specAt);
        foreach (var sp in specs) { w.Write(sp); if ((sp.Length & 1) == 1) w.Write((byte)0); }
        // Eggs and their characters.
        w.Write((ushort)Math.Min(Eggs.Count, 64));
        foreach (var e in Eggs.Take(64)) for (int i = 0; i < 18; i++) w.Write((ushort)(i < e.Length ? e[i] : 0));
        w.Write((ushort)Math.Min(EggChars.Count, 128));
        foreach (var (em, ix, eg, pc) in EggChars.Take(128)) { w.Write((ushort)em); w.Write((ushort)ix); w.Write((ushort)eg); w.Write((ushort)pc); }
        // Bodies.
        w.Write((ushort)AvatarBody); w.Write((ushort)BodyName);
        w.Write((ushort)Math.Min(CharBodies.Count, 128));
        foreach (var (n, sp, f) in CharBodies.Take(128)) { w.Write((ushort)n); w.Write((ushort)sp); w.Write((ushort)f); }
        // Maps that wrap around; weights (moves.s).
        w.Write((ushort)Math.Min(WrapMaps.Count, 32));
        foreach (int wm in WrapMaps.Take(32)) w.Write((ushort)wm);
        w.Write((ushort)Math.Min(Weights.Count, 256));
        foreach (var (n, t, bag) in Weights.Take(256)) { w.Write((ushort)n); w.Write((ushort)Math.Clamp(t, 0, 65535)); w.Write((ushort)(bag ? 1 : 0)); }
        w.Write((ushort)AvatarSprite);
        w.Write((ushort)Math.Min(Traps.Count, 32));
        foreach (var t in Traps.Take(32)) for (int i = 0; i < 12; i++) w.Write((ushort)(i < t.Length ? t[i] : 0));
        w.Write((ushort)Math.Min(Lights.Count, 64));
        foreach (var l in Lights.Take(64)) { w.Write((ushort)l.Name); w.Write((ushort)l.Minutes); w.Write((ushort)l.FireMin); w.Write((ushort)l.FireMax); }
        w.Write((ushort)Blood.First); w.Write((ushort)Blood.Frames); w.Write((ushort)Blood.Name);
        w.Write((ushort)Math.Min(Levers.Count, 32));
        foreach (var l in Levers.Take(32))
        {
            int Place(int i) => i >= 0 && i < newPlace.Length ? newPlace[i] : 0xFFFF; // the objects were written top to bottom
            w.Write((ushort)Place(l.Obj)); w.Write((ushort)l.Kind); w.Write((ushort)l.Other); w.Write((ushort)l.OnSnd); w.Write((ushort)l.OffSnd); w.Write((ushort)l.Reset);
            w.Write((ushort)(l.On ? 1 : 0)); w.Write((ushort)Math.Min(l.Links.Length, 4));
            for (int i = 0; i < 4; i++) w.Write((ushort)(i < l.Links.Length ? Place(l.Links[i]) : 0));
            for (int i = 0; i < 4; i++) w.Write((ushort)0);
        }
        w.Write((ushort)Math.Min(Modes.Count, 64));
        foreach (var (mn, mm) in Modes.Take(64)) { w.Write((ushort)mn); w.Write((ushort)mm); }
        w.Write((ushort)Math.Min(TempZones.Count, 32));
        foreach (var z in TempZones.Take(32)) { w.Write((ushort)z.Map); w.Write((ushort)z.X); w.Write((ushort)z.Y); w.Write((ushort)z.W); w.Write((ushort)z.H); w.Write((ushort)z.Kind); }
        w.Write((ushort)Math.Min(Gear.Count, 64));
        foreach (var g in Gear.Take(64)) { w.Write((ushort)g.Name); w.Write((short)Math.Clamp(g.Warmth, -999, 999)); w.Write((short)Math.Clamp(g.Cooling, -999, 999)); }
        w.Write((ushort)Math.Min(TempImmune.Count, 32));
        foreach (var t in TempImmune.Take(32)) { w.Write((ushort)t.Name); w.Write((ushort)t.Flags); }
        w.Write((ushort)Math.Min(Doors.Count, 24));
        foreach (var d in Doors.Take(24)) for (int i = 0; i < 12; i++) w.Write((ushort)(i < d.Length ? d[i] : 0));
        w.Write((ushort)Math.Min(Joiners.Count, 16));
        foreach (var (jn, jf, jl) in Joiners.Take(16)) { w.Write((ushort)jn); w.Write((ushort)jf); w.Write((ushort)jl); }
        w.Write((ushort)Math.Min(WornMagic.Count, 32));
        foreach (var wmg in WornMagic.Take(32)) for (int i = 0; i < 8; i++) w.Write((ushort)(i < wmg.Length ? wmg[i] : 0));
        w.Write((ushort)Math.Min(WeaponTypes.Count, 32));
        foreach (var (wn, wt) in WeaponTypes.Take(32)) { w.Write((ushort)wn); w.Write((ushort)wt); }
        w.Write((ushort)Math.Min(Responses.Count, 48));
        foreach (var r in Responses.Take(48)) { w.Write((ushort)r.Name); w.Write((ushort)r.Type); w.Write((ushort)r.Kind); w.Write((ushort)r.Amount); }
        w.Write((ushort)Math.Min(Risers.Count, 16));
        foreach (var r in Risers.Take(16)) { w.Write((ushort)r.Name); w.Write((ushort)r.Least); w.Write((ushort)r.Most); w.Write((ushort)r.NotFrom); }
        w.Write((ushort)Math.Min(Hands.Count, 128));
        foreach (var hp in Hands.Take(128)) { w.Write((ushort)hp.Sprite); w.Write((short)hp.X); w.Write((short)hp.Y); w.Write((ushort)(hp.Behind ? 1 : 0)); }
        w.Write((ushort)Math.Min(HeldWeapons.Count, 32));
        foreach (var hw in HeldWeapons.Take(32)) { w.Write((ushort)hw.Name); w.Write((ushort)hw.Held); w.Write((ushort)hw.Mirrored); w.Write((short)hw.GripX); w.Write((short)hw.GripY); }
        w.Write((ushort)Math.Min(Texts.Count, 1000));
        foreach (var text in Texts.Take(1000)) { w.Write((ushort)text.Length); w.Write(text); }
        w.Write((ushort)(DayNight.On ? 1 : 0)); w.Write((ushort)DayNight.Dawn); w.Write((ushort)DayNight.Day); w.Write((ushort)DayNight.Dusk); w.Write((ushort)DayNight.Night);
        w.Write((ushort)(TitlePicture is null ? 0 : 1));
        if (TitlePicture is not null) w.Write(TitlePicture);
        var acks = Ascii(Acknowledgments.Replace("\r", "").Replace('\n', ' '), 200);
        w.Write((byte)acks.Length); w.Write(acks); w.Write((byte)0);
        w.Write((ushort)IntroCode.Length); w.Write(IntroCode);
        w.Write((ushort)(LineOfSight ? 1 : 0));
        w.Write((ushort)Math.Min(Weapons.Count, 64));
        foreach (var (wn, uses, fam, missile) in Weapons.Take(64)) { w.Write((ushort)wn); w.Write((ushort)uses); w.Write((ushort)fam); w.Write((ushort)missile); }
        w.Write((ushort)Math.Min(Buttons.Count, 16));
        foreach (var (key, x, y, bw, bh) in Buttons.Take(16)) { w.Write((ushort)key); w.Write((ushort)x); w.Write((ushort)y); w.Write((ushort)bw); w.Write((ushort)bh); }
        w.Write((ushort)Math.Min(AnimTiles.Count, 128));
        foreach (var frames in AnimTiles.Take(128)) { w.Write((ushort)Math.Min(frames.Count, 4)); foreach (int f in frames.Take(4)) w.Write((ushort)f); for (int i = frames.Count; i < 4; i++) w.Write((ushort)frames[0]); }
        return ms.ToArray();
    }

    private static byte[] Ascii(string text, int max) => Encoding.ASCII.GetBytes(new string(text.Select(ch => ch is >= ' ' and <= '~' ? ch : ch == '\u2019' ? '\'' : '?').Take(max).ToArray()));

    /// <summary>Every file of the game but the player: GAME.DAT, then each sound as its own file SND.1, SND.2... (ProDOS type $D8: SND).</summary>
    public List<(string Name, byte FileType, ushort AuxType, byte[] Data)> Files()
    {
        var files = new List<(string, byte, ushort, byte[])> { ("GAME.DAT", 0x06, 0x0000, Write()) };
        for (int i = 0; i < Sounds.Count; i++) files.Add(($"SND.{i + 1}", 0xD8, 0x0000, Sounds[i].Data));
        for (int i = 0; i < OtherMaps.Count; i++) files.Add(($"MAP.{i + 1}", 0x06, 0x0000, OtherMaps[i].Write()));
        return files;
    }

    /// <summary>8-bit unsigned samples for the Ensoniq DOC: a 0 byte ends a sound there, so none may be 0.</summary>
    public static byte[] DocSafe(byte[] samples)
    {
        var s = (byte[])samples.Clone();
        for (int i = 0; i < s.Length; i++) if (s[i] == 0) s[i] = 1;
        return s;
    }
}

/// <summary>
/// One of the game's other maps, as its file MAP.n: "SCMP", its size, its start, its cells (tile numbers), its characters
/// (16 bytes each, as GAME.DAT's), its events (x, y, the code's length, the code).
/// </summary>
public sealed class AppleIIgsMap
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int StartX { get; set; }
    public int StartY { get; set; }
    public ushort[] Map { get; set; } = Array.Empty<ushort>();
    public List<(int X, int Y, int Sprite, int Flags, int Facing, int Talk, int Portrait, int Name, int[] Stats)> Characters { get; } = new();
    public List<(int X, int Y, byte[] Code)> Events { get; } = new();
    public List<int> EventMasks { get; } = new();
    public List<(int Character, int Period, int Activity, int X, int Y)> Schedules { get; } = new();

    public byte[] Write()
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("SCMP"));
        w.Write((ushort)Width); w.Write((ushort)Height); w.Write((ushort)StartX); w.Write((ushort)StartY);
        foreach (ushort t in Map) w.Write(t);
        w.Write((ushort)Characters.Count);
        foreach (var c in Characters)
        {
            w.Write((ushort)c.X); w.Write((ushort)c.Y); w.Write((ushort)c.Sprite); w.Write((ushort)c.Flags); w.Write((ushort)c.Facing);
            w.Write((ushort)c.Talk); w.Write((ushort)c.Portrait); w.Write((ushort)c.Name);
            AppleIIgsGamePack.WriteStats(w, c.Stats);
        }
        WriteEvents(w, Events, EventMasks);
        AppleIIgsGamePack.WriteSchedules(w, Schedules);
        return ms.ToArray();
    }

    /// <summary>Each event: x, y, its triggers (a bit each), its code's length, its code.</summary>
    public static void WriteEvents(BinaryWriter w, List<(int X, int Y, byte[] Code)> events, List<int> masks)
    {
        w.Write((ushort)events.Count);
        for (int i = 0; i < events.Count; i++)
        {
            var (x, y, code) = events[i];
            w.Write((ushort)x); w.Write((ushort)y); w.Write((ushort)(i < masks.Count ? masks[i] : 1 << 1)); w.Write((ushort)code.Length); w.Write(code);
        }
    }
}

/// <summary>
/// A game on 3.5-inch floppies (800 KB ProDOS disks): the player and GAME.DAT on the first, the rest wherever there is room
/// (the biggest first). Not bootable: GS/OS starts from its own disk, the game from disk 1; every file can also be copied
/// into one folder of a hard disk, and the game finds them all there.
/// </summary>
public static class AppleIIgsFloppies
{
    public const int Blocks = 1600;
    private const int Overhead = 2 + 4 + 1; // boot blocks, the top folder, the bitmap
    private const int MostFiles = 51;

    public static int BlocksFor(int length)
    {
        int data = Math.Max(1, (length + 511) / 512);
        return data == 1 ? 1 : data <= 256 ? data + 1 : data + 1 + (data + 255) / 256;
    }

    /// <summary>The files on each disk; the first files given (the player, GAME.DAT) go on disk 1.</summary>
    public static List<List<(string Name, byte FileType, ushort AuxType, byte[] Data)>> Spread(List<(string Name, byte FileType, ushort AuxType, byte[] Data)> first, List<(string Name, byte FileType, ushort AuxType, byte[] Data)> rest)
    {
        var disks = new List<List<(string, byte, ushort, byte[])>> { new() };
        var free = new List<int> { Blocks - Overhead };
        foreach (var f in first)
        {
            int need = BlocksFor(f.Data.Length);
            if (need > free[0]) throw new InvalidOperationException($"{f.Name} ({f.Data.Length / 1024} KB) and the player don't fit on one 800 KB floppy - build a hard disk instead.");
            disks[0].Add(f);
            free[0] -= need;
        }
        foreach (var f in rest.OrderByDescending(f => f.Data.Length))
        {
            int need = BlocksFor(f.Data.Length);
            if (need > Blocks - Overhead) throw new InvalidOperationException($"{f.Name} is bigger than a floppy.");
            int at = Enumerable.Range(0, disks.Count).FirstOrDefault(i => free[i] >= need && disks[i].Count < MostFiles, -1);
            if (at < 0) { disks.Add(new()); free.Add(Blocks - Overhead); at = disks.Count - 1; }
            disks[at].Add(f);
            free[at] -= need;
        }
        return disks;
    }
}

/// <summary>
/// DISKS.DAT, on disk 1 of a game built as floppies: the disks (ProDOS name, the name shown) and which disk each file is on.
/// The player looks for a file in its own folder first, then on its disk ":NAME:FILE", then asks for that disk (the Disk
/// Swap screen). "SCDK", u16 disks, each: u8 length, ProDOS name, 0, u8 length, shown name, 0; u16 files, each:
/// u8 length, name, 0, u8 its disk (1 = the first).
/// </summary>
public static class AppleIIgsDisks
{
    public static byte[] Write(List<(string Volume, string Shown)> disks, List<(string File, int Disk)> files)
    {
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(Encoding.ASCII.GetBytes("SCDK"));
        w.Write((ushort)disks.Count);
        foreach (var (vol, shown) in disks) { Str(vol, 15); Str(shown, 40); }
        w.Write((ushort)files.Count);
        foreach (var (file, disk) in files) { Str(file, 15); w.Write((byte)disk); }
        return ms.ToArray();
        void Str(string text, int max)
        {
            var t = Encoding.ASCII.GetBytes(new string(text.Select(ch => ch is >= ' ' and <= '~' ? ch : '?').Take(max).ToArray()));
            w.Write((byte)t.Length); w.Write(t); w.Write((byte)0);
        }
    }
}
