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
public static class SuperHiRes
{
    /// <summary>An ARGB colour to the IIgs's $0RGB.</summary>
    public static ushort To12Bit(uint argb) => (ushort)((((argb >> 16) & 0xFF) * 15 + 127) / 255 << 8 | (((argb >> 8) & 0xFF) * 15 + 127) / 255 << 4 | ((argb & 0xFF) * 15 + 127) / 255);

    public static uint FromRgb12(ushort c) => 0xFF000000u | (uint)((c >> 8 & 15) * 17) << 16 | (uint)((c >> 4 & 15) * 17) << 8 | (uint)((c & 15) * 17);

    /// <summary>
    /// The 16 colours that best show every pixel given (median cut on their 12-bit colours, most-used first). Colour 0 is
    /// black: in a sprite it is see-through (the IIgs player draws colour 0 of a sprite as nothing).
    /// </summary>
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
///   words: u16 count, each u8 length, text, 0 - you say, bye, no help, funny no response, talk, nothing, talking to
///          yourself, you see {0}, {1} gets {0}, not possible, out of range, look, get, use, locked, drop, where?,
///          {1} drops {0}, the Avatar's name (always the last)
///   names: u16 count, each u8 length, text, 0 (objects', characters', tiles' names)
///   variables: u16 count, i16 each (their starting values; switches are variables 0 / 1)
///   talks: u16 count, count x u32 offset, u32 all their bytes, then each: u8 length, the speaker's name, 0, its code
///          (Engine/AppleIIgsTalk.cs)
///   sounds: u16 count, then each: u8 name length + name, u16 rate, u32 length (the bytes are in SOUNDS.DAT, one after another)
/// A sprite stands on the bottom middle of its cell, as in the Windows Ultima 6 engine.
/// Version 6: the map, its tiles, the Avatar, objects, characters, the HUD, talk, the verbs, the inventory and the sounds.
/// </summary>
public sealed class AppleIIgsGamePack
{
    public const ushort Version = 6;
    public ushort[] Palette { get; set; } = new ushort[16];
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
    public List<(int Width, int Height, byte[] Data)> Sprites { get; } = new();
    public List<(int X, int Y, int Sprite, int Flags, int Name, int Other, int Icon)> Objects { get; } = new();
    /// <summary>The HUD's Carried Items grid (Columns 0: none).</summary>
    public (int X, int Y, int Columns, int Rows, int Cell) CarriedGrid { get; set; }
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
    public List<(int X, int Y, int Sprite, int Flags, int Facing, int Talk, int Portrait, int Name)> Characters { get; } = new();
    public (int X, int Y, int Width, int Height) PortraitRect { get; set; }
    public (int X, int Y, int Width, int Height) NameRect { get; set; }
    public int TextColor { get; set; } = 0xFFFF;
    public int KeywordColor { get; set; } = 0xFFFF;
    /// <summary>you say, bye, no help, funny no response, talk, nothing, talking to yourself.</summary>
    public List<string> Words { get; } = new() { "you say: ", "bye", "\"I cannot help thee with that.\"", "Funny, no response.", "Talk", "nothing", "Talking to yourself?",
        "You see {0}.", "{1} gets {0}.", "Not possible.", "Out of range!", "Look", "Get", "Use", "Locked.", "Drop", "Where?", "{1} drops {0}.", "Avatar" };
    public static readonly string[] WordIds = { "YouSay", "Bye", "NoHelp", "FunnyNoResponse", "Talk", "Nothing", "TalkSelf", "YouSee", "Gets", "NotPossible", "OutOfRange", "Look", "Get", "Use", "Locked", "Drop", "WhatPlace", "Drops" };
    public List<short> Variables { get; } = new();
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
        foreach (var o in Objects.OrderBy(o => (o.Flags & 64) != 0 ? 1 : 0).ThenBy(o => o.Y).ThenBy(o => o.X))
        {
            w.Write((ushort)o.X); w.Write((ushort)o.Y); w.Write((ushort)o.Sprite); w.Write((ushort)o.Flags); w.Write((ushort)o.Name); w.Write((ushort)o.Other);
            w.Write((ushort)o.Icon); w.Write((ushort)0);
        }
        w.Write((ushort)Characters.Count);
        foreach (var c in Characters)
        {
            w.Write((ushort)c.X); w.Write((ushort)c.Y); w.Write((ushort)c.Sprite); w.Write((ushort)c.Flags); w.Write((ushort)c.Facing);
            w.Write((ushort)c.Talk); w.Write((ushort)c.Portrait); w.Write((ushort)c.Name);
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
        }
        return ms.ToArray();
    }

    private static byte[] Ascii(string text, int max) => Encoding.ASCII.GetBytes(new string(text.Select(ch => ch is >= ' ' and <= '~' ? ch : ch == '\u2019' ? '\'' : '?').Take(max).ToArray()));

    /// <summary>SOUNDS.DAT: every sound's bytes, one after another (as GAME.DAT lists them).</summary>
    public byte[] WriteSounds() => Sounds.SelectMany(s => s.Data).ToArray();

    /// <summary>8-bit unsigned samples for the Ensoniq DOC: a 0 byte ends a sound there, so none may be 0.</summary>
    public static byte[] DocSafe(byte[] samples)
    {
        var s = (byte[])samples.Clone();
        for (int i = 0; i < s.Length; i++) if (s[i] == 0) s[i] = 1;
        return s;
    }
}
