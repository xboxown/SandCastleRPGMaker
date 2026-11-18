#!/usr/bin/env python3
"""
hfscheck - reads an HFS volume (a .hdv, or a .2mg with HFS inside) on its own, from Apple's description of HFS (Inside
Macintosh: Files), to check what the editor's HfsVolume.cs writes: the Master Directory Block and its copy, the volume
bitmap against the files' extents, the catalog B-tree (header, leaves linked both ways, keys in order, index nodes),
each file's Finder type / creator (GS/OS: 'p' + ProDOS type + aux type, 'pdos') and its bytes.
Usage: hfscheck.py image [--extract folder]
"""
import struct, sys, os

def main(argv):
    if len(argv) < 2: print(__doc__); return 2
    img = open(argv[1], 'rb').read()
    off = struct.unpack_from('<I', img, 24)[0] if img[:4] == b'2IMG' else 0
    vol = img[off:]
    problems = []
    def be16(b, o): return struct.unpack_from('>H', b, o)[0]
    def be32(b, o): return struct.unpack_from('>I', b, o)[0]
    mdb = vol[1024:1536]
    if be16(mdb, 0) != 0x4244: print('not HFS (no BD signature)'); return 1
    if vol[len(vol) - 1024: len(vol) - 512] != mdb: problems.append('the copy of the MDB (second-to-last block) differs')
    nfiles, vbm, alloc_ptr, nalloc, alloc_size = be16(mdb, 12), be16(mdb, 14), be16(mdb, 16), be16(mdb, 18), be32(mdb, 20)
    al_st, next_id, free = be16(mdb, 28), be32(mdb, 30), be16(mdb, 34)
    name = mdb[37:37 + mdb[36]].decode('latin-1')
    print(f'volume "{name}": {len(vol)} bytes, {nalloc} allocation blocks of {alloc_size}, first at block {al_st}, {free} free, {nfiles} files')
    if alloc_size % 512: problems.append('allocation block size not a multiple of 512')
    def ext(o): return [(be16(mdb, o + i * 4), be16(mdb, o + i * 4 + 2)) for i in range(3)]
    def read_extents(extents, length):
        out = b''
        for s, c in extents:
            if c == 0: continue
            out += vol[al_st * 512 + s * alloc_size: al_st * 512 + (s + c) * alloc_size]
        return out[:length]
    xt = read_extents(ext(134), be32(mdb, 130))
    ct = read_extents(ext(150), be32(mdb, 146))
    used = set()
    for s, c in ext(134) + ext(150):
        used.update(range(s, s + c))
    # the extents tree: a header
    if xt[8] != 1: problems.append('extents tree: node 0 is not a header')
    # the catalog
    if ct[8] != 1: problems.append('catalog: node 0 is not a header')
    depth, root, nrecs, first, last, nodesize, keylen, nnodes, nfree = struct.unpack_from('>HIIIIHHII', ct, 14)
    print(f'catalog: depth {depth}, root {root}, {nrecs} leaf records, leaves {first}-{last}, node {nodesize}, {nnodes} nodes ({nfree} free)')
    if nodesize != 512: problems.append('catalog node size not 512')
    def node(n): return ct[n * nodesize:(n + 1) * nodesize]
    def records(nd):
        count = be16(nd, 10)
        offs = [be16(nd, nodesize - 2 * (i + 1)) for i in range(count + 1)]
        return [nd[offs[i]:offs[i + 1]] for i in range(count)]
    def key_of(rec):
        kl = rec[0]
        return be32(rec, 2), rec[7:7 + rec[6]].decode('latin-1').upper(), kl
    # walk the leaves
    n, prev, seen, files, last_key, count = first, 0, set(), [], None, 0
    while n:
        if n in seen: problems.append('leaf chain loops'); break
        seen.add(n)
        nd = node(n)
        if nd[8] != 0xFF: problems.append(f'node {n} in the leaf chain is not a leaf')
        if be32(nd, 4) != prev: problems.append(f'leaf {n}: back link {be32(nd, 4)}, expected {prev}')
        for rec in records(nd):
            parent, nm, kl = key_of(rec)
            k = (parent, nm)
            if last_key is not None and k <= last_key: problems.append(f'keys out of order: {last_key} then {k}')
            last_key = k
            data = rec[(kl + 2) & ~1:]
            kind = data[0]
            count += 1
            if kind == 1: print(f'  folder "{nm}" (parent {parent}) id {be32(data, 6)} holds {be16(data, 4)}')
            elif kind == 3: print(f'  thread of folder {parent}: parent {be32(data, 10)} "{data[15:15 + data[14]].decode("latin-1")}"')
            elif kind == 2:
                ftype, creator = data[4:8], data[8:12]
                length, start, cnt = be32(data, 26), be16(data, 74), be16(data, 76)
                files.append((nm, ftype, creator, length, [(start, cnt), (be16(data, 78), be16(data, 80)), (be16(data, 82), be16(data, 84))]))
            else: problems.append(f'record of unknown kind {kind}')
        prev, n = n, be32(nd, 0)
    if prev != last: problems.append(f'last leaf {prev}, header says {last}')
    if count != nrecs: problems.append(f'{count} leaf records, header says {nrecs}')
    # the index: from the root down to the first leaf
    if depth > 1:
        nd = node(root)
        if nd[8] != 0: problems.append('the root is not an index node')
        for rec in records(nd):
            child = be32(rec, 38)
            if child >= nnodes: problems.append(f'index points at node {child}')
    for nm, ftype, creator, length, extents in files:
        for s, c in extents: used.update(range(s, s + c))
        data = read_extents(extents, length)
        desc = f'type ${ftype[1]:02X} aux ${ftype[2]:02X}{ftype[3]:02X}' if ftype[:1] == b'p' and creator == b'pdos' else f'type {ftype} creator {creator}'
        print(f'  file "{nm}": {length} bytes, {desc}, extents {extents}')
        if '--extract' in argv:
            folder = argv[argv.index('--extract') + 1]
            os.makedirs(folder, exist_ok=True)
            open(os.path.join(folder, nm), 'wb').write(data)
    if len(files) != nfiles: problems.append(f'{len(files)} files, the MDB says {nfiles}')
    # the bitmap against what is used
    bm = vol[vbm * 512:]
    marked = {i for i in range(nalloc) if bm[i // 8] & (0x80 >> (i % 8))}
    if marked != used: problems.append(f'bitmap: {len(marked)} blocks marked, {len(used)} used by files (differ: {sorted(marked ^ used)[:10]})')
    if nalloc - len(marked) != free: problems.append(f'free blocks {nalloc - len(marked)}, the MDB says {free}')
    print('PROBLEMS:' if problems else 'all checks passed')
    for p in problems: print('  -', p)
    return 1 if problems else 0

if __name__ == '__main__':
    sys.exit(main(sys.argv))
