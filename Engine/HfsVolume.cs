using System.Text;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// An HFS volume (Apple's Hierarchical File System, as GS/OS's HFS FST reads it) written from scratch: up to 2 GB, files
/// in its top folder with their ProDOS file type and aux type kept the way GS/OS keeps them on HFS (Finder type
/// 'p' + type + aux type, creator 'pdos'). Inside Macintosh: Files, chapter 2 (the Master Directory Block, the volume
/// bitmap, the extents and catalog B-trees, 512-byte nodes).
/// </summary>
public sealed class HfsVolume
{
    public const long MaxBytes = 2L * 1024 * 1024 * 1024 - 1024 * 1024; // 2 GB (less a little: HFS counts in 16 bits of allocation blocks)
    private sealed record FileEntry(string Name, byte FileType, ushort AuxType, byte[] Data);
    private readonly List<FileEntry> _files = new();
    public string VolumeName { get; }

    public HfsVolume(string volumeName)
    {
        string name = new(volumeName.Where(c => c is >= ' ' and <= '~' && c != ':').Take(27).ToArray());
        VolumeName = name.Trim().Length == 0 ? "Game" : name.Trim();
    }

    public void Add(string name, byte fileType, ushort auxType, byte[] data)
    {
        string clean = new(name.Where(c => c is >= ' ' and <= '~' && c != ':').Take(31).ToArray());
        if (clean.Length == 0) clean = "File" + _files.Count;
        _files.Add(new FileEntry(clean, fileType, auxType, data));
    }

    private const int NodeSize = 512;

    /// <summary>The volume's bytes: totalBytes 0 = big enough for its files with room to spare (at least 800 KB).</summary>
    public byte[] Build(long totalBytes = 0)
    {
        long need = 64 * 1024 + _files.Sum(f => (long)f.Data.Length + 32 * 1024);
        if (totalBytes == 0) totalBytes = Math.Max(819200, need + need / 4);
        totalBytes = (totalBytes + 511) / 512 * 512;
        if (totalBytes > MaxBytes) throw new InvalidOperationException("The game is bigger than an HFS disk for the Apple IIgs (2 GB).");
        long blocks = totalBytes / 512;
        // Allocation blocks: at most 65535 of them, a multiple of 512 bytes each.
        int perAlloc = (int)Math.Max(1, (blocks - 8 + 65534) / 65535);
        int allocSize = perAlloc * 512;
        int bitmapBlocks = (int)((Math.Min(65535, (blocks - 8) / perAlloc) + 4095) / 4096);
        int firstAlloc = 3 + bitmapBlocks;
        int allocCount = (int)Math.Min(65535, (blocks - firstAlloc - 2) / perAlloc);
        var disk = new byte[totalBytes];
        var used = new bool[allocCount];
        int next = 0;
        (int Start, int Count) Alloc(long bytes)
        {
            int count = (int)Math.Max(1, (bytes + allocSize - 1) / allocSize);
            if (next + count > allocCount) throw new InvalidOperationException("The HFS disk is full.");
            int start = next;
            for (int i = 0; i < count; i++) used[next++] = true;
            return (start, count);
        }
        long AllocOffset(int allocBlock) => (long)(firstAlloc * 512) + (long)allocBlock * allocSize;
        uint now = (uint)((DateTime.Now - new DateTime(1904, 1, 1)).TotalSeconds);

        // The catalog's records, in key order: the top folder (parent 1), its thread, then its files by name.
        var records = new List<(byte[] Key, byte[] Data)>();
        records.Add((CatalogKey(1, VolumeName), DirectoryRecord(2, _files.Count, now)));
        records.Add((CatalogKey(2, ""), ThreadRecord(1, VolumeName)));
        var fileExtents = new List<(int Start, int Count)>();
        var sorted = _files.OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();

        // The extents tree (empty: a header node) and the catalog tree: their sizes first.
        var leaves = PackNodes(sorted.Select((f, i) => (CatalogKey(2, f.Name), new byte[102])).Prepend(records[1]).Prepend(records[0]).ToList(), out _);
        int catalogNodes = 1 + leaves.Count + IndexNodeCount(leaves.Count) + 4;
        var extentsFile = Alloc(NodeSize * 4);
        var catalogFile = Alloc((long)catalogNodes * NodeSize);
        foreach (var f in sorted) fileExtents.Add(f.Data.Length == 0 ? (0, 0) : Alloc(f.Data.Length));
        uint nextId = 16;
        for (int i = 0; i < sorted.Count; i++)
        {
            var f = sorted[i];
            var (start, count) = fileExtents[i];
            if (f.Data.Length > 0) f.Data.CopyTo(disk, AllocOffset(start));
            records.Add((CatalogKey(2, f.Name), FileRecord(nextId++, f, start, count, allocSize, now)));
        }

        // The catalog: leaves, then index levels up to one root.
        int catalogNodeTotal = catalogFile.Count * allocSize / NodeSize;
        var nodes = new List<byte[]> { new byte[NodeSize] }; // node 0: the header
        var leafNodes = PackNodes(records, out var firstKeys);
        int firstLeaf = nodes.Count;
        for (int i = 0; i < leafNodes.Count; i++)
        {
            var n = leafNodes[i];
            W32(n, 0, i + 1 < leafNodes.Count ? (uint)(firstLeaf + i + 1) : 0);
            W32(n, 4, i > 0 ? (uint)(firstLeaf + i - 1) : 0);
            n[8] = 0xFF; n[9] = 1; // a leaf, height 1
            nodes.Add(n);
        }
        int lastLeaf = nodes.Count - 1;
        var level = Enumerable.Range(0, leafNodes.Count).Select(i => (Node: firstLeaf + i, Key: firstKeys[i])).ToList();
        int height = 1;
        while (level.Count > 1)
        {
            height++;
            var up = new List<(int Node, byte[] Key)>();
            var indexRecords = level.Select(l => (Key: IndexKey(l.Key), Data: Be32((uint)l.Node))).ToList();
            var packed = PackNodes(indexRecords, out var keys);
            for (int i = 0; i < packed.Count; i++)
            {
                var n = packed[i];
                int me = nodes.Count;
                W32(n, 0, i + 1 < packed.Count ? (uint)(me + 1) : 0);
                W32(n, 4, i > 0 ? (uint)(me - 1) : 0);
                n[8] = 0x00; n[9] = (byte)height; // an index node
                nodes.Add(n);
                up.Add((me, keys[i]));
            }
            level = up;
        }
        int root = level.Count == 1 ? level[0].Node : 0;
        if (nodes.Count > catalogNodeTotal) throw new InvalidOperationException("The catalog is too big.");
        HeaderNode(nodes[0], depth: height, root: root, leafRecords: records.Count, firstLeaf: firstLeaf, lastLeaf: lastLeaf, keyLength: 37, totalNodes: catalogNodeTotal, usedNodes: nodes.Count);
        for (int i = 0; i < nodes.Count; i++) nodes[i].CopyTo(disk, AllocOffset(catalogFile.Start) + (long)i * NodeSize);
        // The extents tree: its header alone.
        int extentNodes = extentsFile.Count * allocSize / NodeSize;
        var xHeader = new byte[NodeSize];
        HeaderNode(xHeader, depth: 0, root: 0, leafRecords: 0, firstLeaf: 0, lastLeaf: 0, keyLength: 7, totalNodes: extentNodes, usedNodes: 1);
        xHeader.CopyTo(disk, AllocOffset(extentsFile.Start));

        // The volume bitmap: a set bit is an allocation block in use.
        for (int i = 0; i < allocCount; i++) if (used[i]) disk[3 * 512 + i / 8] |= (byte)(0x80 >> (i % 8));
        // The Master Directory Block, and its copy in the second-to-last block.
        var mdb = new byte[512];
        W16(mdb, 0, 0x4244);
        W32(mdb, 2, now); W32(mdb, 6, now);
        W16(mdb, 10, 0x0100);
        W16(mdb, 12, sorted.Count);
        W16(mdb, 14, 3);
        W16(mdb, 16, next);
        W16(mdb, 18, allocCount);
        W32(mdb, 20, (uint)allocSize);
        W32(mdb, 24, (uint)allocSize * 4);
        W16(mdb, 28, firstAlloc);
        W32(mdb, 30, nextId);
        W16(mdb, 34, allocCount - next);
        var vn = Encoding.ASCII.GetBytes(VolumeName);
        mdb[36] = (byte)vn.Length; vn.CopyTo(mdb, 37);
        W32(mdb, 70, 1);
        W32(mdb, 74, (uint)allocSize); W32(mdb, 78, (uint)allocSize);
        W16(mdb, 82, 0);
        W32(mdb, 84, (uint)sorted.Count);
        W32(mdb, 88, 0);
        W32(mdb, 130, (uint)(extentsFile.Count * allocSize));
        W16(mdb, 134, extentsFile.Start); W16(mdb, 136, extentsFile.Count);
        W32(mdb, 146, (uint)(catalogFile.Count * allocSize));
        W16(mdb, 150, catalogFile.Start); W16(mdb, 152, catalogFile.Count);
        mdb.CopyTo(disk, 1024);
        mdb.CopyTo(disk, totalBytes - 1024);
        return disk;
    }

    // ---- the catalog's pieces ----

    /// <summary>A catalog key: its length, a reserved byte, the parent folder's id, the name (a Pascal string), padded to even.</summary>
    private static byte[] CatalogKey(uint parent, string name)
    {
        var n = Encoding.ASCII.GetBytes(name);
        int keyLength = 6 + n.Length;
        var key = new byte[(keyLength + 2) & ~1];
        key[0] = (byte)keyLength;
        W32(key, 2, parent);
        key[6] = (byte)n.Length;
        n.CopyTo(key, 7);
        return key;
    }

    /// <summary>An index node's key: the same, at its full length (37), as HFS's catalog index records have it.</summary>
    private static byte[] IndexKey(byte[] leafKey)
    {
        var key = new byte[38];
        key[0] = 37;
        Array.Copy(leafKey, 1, key, 1, Math.Min((int)leafKey[0], 37));
        return key;
    }

    private static byte[] DirectoryRecord(uint id, int valence, uint now)
    {
        var r = new byte[70];
        r[0] = 1;
        W16(r, 4, valence);
        W32(r, 6, id);
        W32(r, 10, now); W32(r, 14, now);
        return r;
    }

    private static byte[] ThreadRecord(uint parent, string name)
    {
        var r = new byte[46];
        r[0] = 3;
        W32(r, 10, parent);
        var n = Encoding.ASCII.GetBytes(name);
        r[14] = (byte)n.Length;
        n.CopyTo(r, 15);
        return r;
    }

    private static byte[] FileRecord(uint id, FileEntry f, int start, int count, int allocSize, uint now)
    {
        var r = new byte[102];
        r[0] = 2;
        // Finder info: GS/OS's HFS FST keeps a ProDOS type as type 'p', the type, the aux type (high, low); creator 'pdos'.
        r[4] = (byte)'p'; r[5] = f.FileType; r[6] = (byte)(f.AuxType >> 8); r[7] = (byte)f.AuxType;
        Encoding.ASCII.GetBytes("pdos").CopyTo(r, 8);
        W32(r, 20, id);
        W16(r, 24, start);
        W32(r, 26, (uint)f.Data.Length);
        W32(r, 30, (uint)(count * allocSize));
        W32(r, 44, now); W32(r, 48, now);
        W16(r, 74, start); W16(r, 76, count); // the data fork's first extent
        return r;
    }

    /// <summary>Records into 512-byte nodes, in order, as many to a node as fit; each node's first key returned.</summary>
    private static List<byte[]> PackNodes(List<(byte[] Key, byte[] Data)> records, out List<byte[]> firstKeys)
    {
        var nodes = new List<byte[]>();
        firstKeys = new List<byte[]>();
        int i = 0;
        while (i < records.Count)
        {
            var n = new byte[NodeSize];
            int at = 14, count = 0;
            firstKeys.Add(records[i].Key);
            while (i < records.Count)
            {
                int size = records[i].Key.Length + records[i].Data.Length;
                if (at + size > NodeSize - 2 * (count + 2)) break;
                records[i].Key.CopyTo(n, at);
                records[i].Data.CopyTo(n, at + records[i].Key.Length);
                W16(n, NodeSize - 2 * (count + 1), at);
                at += size;
                count++;
                i++;
            }
            if (count == 0) throw new InvalidOperationException("A catalog record is bigger than a node.");
            W16(n, NodeSize - 2 * (count + 1), at); // the free space's offset
            W16(n, 10, count);
            nodes.Add(n);
        }
        return nodes;
    }

    private static int IndexNodeCount(int leaves)
    {
        int total = 0;
        for (int level = leaves; level > 1; level = (level + 10) / 11) total += (level + 10) / 11;
        return total;
    }

    /// <summary>A B-tree's header node: the header record, the user record, the map of nodes in use.</summary>
    private static void HeaderNode(byte[] n, int depth, int root, int leafRecords, int firstLeaf, int lastLeaf, int keyLength, int totalNodes, int usedNodes)
    {
        n[8] = 1; n[9] = 0;
        W16(n, 10, 3);
        W16(n, 14, depth);
        W32(n, 16, (uint)root);
        W32(n, 20, (uint)leafRecords);
        W32(n, 24, (uint)firstLeaf);
        W32(n, 28, (uint)lastLeaf);
        W16(n, 32, NodeSize);
        W16(n, 34, keyLength);
        W32(n, 36, (uint)totalNodes);
        W32(n, 40, (uint)(totalNodes - usedNodes));
        for (int i = 0; i < usedNodes && i < 256 * 8; i++) n[248 + i / 8] |= (byte)(0x80 >> (i % 8));
        W16(n, 510, 14); W16(n, 508, 120); W16(n, 506, 248); W16(n, 504, 504);
    }

    private static byte[] Be32(uint v) { var b = new byte[4]; W32(b, 0, v); return b; }
    private static void W16(byte[] b, int at, int v) { b[at] = (byte)(v >> 8); b[at + 1] = (byte)v; }
    private static void W32(byte[] b, int at, uint v) { b[at] = (byte)(v >> 24); b[at + 1] = (byte)(v >> 16); b[at + 2] = (byte)(v >> 8); b[at + 3] = (byte)v; }
}
