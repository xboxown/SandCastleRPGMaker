#!/usr/bin/env python3
"""
sim816 - a 65816 simulator to test the Apple IIgs player headless (no emulator, no ROM, no GS/OS needed).

Loads an S16 (OMF 2) as the System Loader would (relocating cRELOC / RELOC records), runs it in native mode with the
toolbox (JSL $E10000) and GS/OS (JSL $E100A8) calls it uses answered by simple stand-ins, feeds it keys, and saves the
Super Hi-Res screen as a PNG. It tests the player's own logic - the real toolbox and GS/OS are tested in KEGS.
Usage: sim816.py SANDCASTLE folder-with-GAME.DAT out.png [--keys "RRDDL q"] [--slow] [--trace]
Keys: U D L R = arrows, @x,y; a mouse click, ` Tab, { Ctrl+S, } Ctrl+R, | Ctrl+Q, ~ Enter, ^ Esc, < Delete, ! the other disks go in, other letters as typed.
"""
import struct, sys, zlib, os

class Stop(Exception):
    pass

class Machine:
    def __init__(self, folder, keys, accelerated=True, trace=False):
        self.mem = bytearray(1 << 24)
        self.folder = folder
        self.keys = []                      # keys, and mouse clicks written @x,y;
        i = 0
        while i < len(keys):
            if keys[i] == '@' and ';' in keys[i:]:
                j = keys.index(';', i)
                x, y = keys[i + 1:j].split(',')
                self.keys.append(('click', int(x), int(y)))
                i = j + 1
            else:
                self.keys.append(keys[i])
                i += 1
        self.accelerated = accelerated
        self.trace = trace
        self.a = self.x = self.y = 0
        self.s = 0x01FF
        self.d = 0
        self.db = self.pb = 0
        self.pc = 0
        self.p = 0x30
        self.cycles = 0
        self.texts = []
        self.alerts = []
        self.heap = 0x040000
        self.files = {}
        self.wfiles = {}
        self.written = []
        self.quit_keys = 0
        self.next_ref = 1
        self.pen = 0
        self.quit = False
        self.steps = 0
        self.key_waits = 0
        self.typed = []
        self.sounds = []
        self.opened = []
        self.inserted = False

    # ---- memory ----
    def rd(self, a):
        a &= 0xFFFFFF
        if a == 0xE0C019 or a == 0x00C019:
            # vertical blank: high for an eighth of a frame. A frame is about 13000 instructions on a 2.8 MHz IIgs
            # (46667 cycles, some 3.5 a instruction), about 38000 with an 8 MHz accelerator.
            frame = 38000 if self.accelerated else 13000
            return 0x80 if self.steps % frame < frame // 8 else 0x00
        return self.mem[a]
    def wr(self, a, v):
        self.mem[a & 0xFFFFFF] = v & 0xFF
    def rd16(self, a): return self.rd(a) | self.rd(a + 1) << 8
    def rd24(self, a): return self.rd(a) | self.rd(a + 1) << 8 | self.rd(a + 2) << 16
    def wr16(self, a, v): self.wr(a, v); self.wr(a + 1, v >> 8)
    def bank0(self, a): return a & 0xFFFF

    @property
    def m8(self): return bool(self.p & 0x20)
    @property
    def x8(self): return bool(self.p & 0x10)

    def flag(self, bit, on):
        self.p = (self.p | bit) if on else (self.p & ~bit)
    def nz(self, v, eight):
        if eight: self.flag(0x02, (v & 0xFF) == 0); self.flag(0x80, v & 0x80)
        else: self.flag(0x02, (v & 0xFFFF) == 0); self.flag(0x80, v & 0x8000)

    def push8(self, v): self.wr(self.s, v); self.s = (self.s - 1) & 0xFFFF
    def push16(self, v): self.push8(v >> 8); self.push8(v)
    def pull8(self): self.s = (self.s + 1) & 0xFFFF; return self.rd(self.s)
    def pull16(self): lo = self.pull8(); return lo | self.pull8() << 8

    def fetch8(self):
        v = self.rd(self.pb << 16 | self.pc); self.pc = (self.pc + 1) & 0xFFFF; return v
    def fetch16(self): lo = self.fetch8(); return lo | self.fetch8() << 8
    def fetch24(self): lo = self.fetch16(); return lo | self.fetch8() << 16

    # ---- loading ----
    def load_omf(self, path, at=0x020000):
        data = open(path, 'rb').read()
        pos, segs = 0, []
        place = at
        while pos < len(data):
            (bytecnt, resspc, length, _, lablen, numlen, version, banksize, kind, _, org, align, numsex, _, segnum, entry,
             dispname, dispdata) = struct.unpack_from('<IIIBBBBIHHIIBBHIHH', data, pos)
            body = data[pos + dispdata: pos + bytecnt]
            base = 0x000800 if (kind & 0x1F) == 0x12 else place
            i, lc = 0, 0
            while i < len(body):
                op = body[i]
                if op == 0x00: break
                if op == 0xF2:
                    n = struct.unpack_from('<I', body, i + 1)[0]
                    self.mem[base + lc: base + lc + n] = body[i + 5: i + 5 + n]
                    lc += n; i += 5 + n
                elif op == 0xF1:
                    lc += struct.unpack_from('<I', body, i + 1)[0]; i += 5
                elif op == 0xF5:
                    cnt, shift = body[i + 1], struct.unpack('b', body[i + 2:i + 3])[0]
                    off, ref = struct.unpack_from('<HH', body, i + 3)
                    v = base + ref
                    v = v >> -shift if shift < 0 else v << shift
                    for k in range(cnt): self.mem[base + off + k] = (v >> (8 * k)) & 0xFF
                    i += 7
                elif op == 0xE2:
                    cnt, shift = body[i + 1], struct.unpack('b', body[i + 2:i + 3])[0]
                    off, ref = struct.unpack_from('<II', body, i + 3)
                    v = base + ref
                    v = v >> -shift if shift < 0 else v << shift
                    for k in range(cnt): self.mem[base + off + k] = (v >> (8 * k)) & 0xFF
                    i += 11
                else:
                    raise Stop(f'OMF record ${op:02X} not handled')
            segs.append((kind, base, length))
            if (kind & 0x1F) != 0x12: place += (length + 0xFFFF) & ~0xFFFF
            pos += bytecnt
        code = [s for s in segs if (s[0] & 0x1F) != 0x12][0]
        dp = [s for s in segs if (s[0] & 0x1F) == 0x12]
        self.pb = code[1] >> 16
        self.pc = code[1] & 0xFFFF
        if dp:
            self.d = dp[0][1]
            self.s = dp[0][1] + dp[0][2] - 1
        self.a = 0x1001  # the user ID the loader passes
        self.code_base = code[1]

    # ---- the toolbox and GS/OS stand-ins ----
    def tool(self):
        n = self.x
        sp = self.s + 4  # first byte above the return address
        def word(off): return self.rd16(sp + off)
        def long(off): return self.rd24(sp + off) | self.rd(sp + off + 3) << 24
        # (params bytes, result bytes, action)
        res = None
        if n == 0x0201: pb, rb = 0, 0                                      # TLStartUp
        elif n == 0x0202: pb, rb = 0, 2; res = 0x1001                      # MMStartUp
        elif n == 0x1801: pb, rb = 8, 4; res = 0xBEEF                      # StartUpTools
        elif n == 0x1901: pb, rb = 6, 0                                    # ShutDownTools
        elif n == 0x0302: pb, rb = 2, 0                                    # MMShutDown
        elif n == 0x0301: pb, rb = 0, 0                                    # TLShutDown
        elif n == 0x0902:                                                  # NewHandle
            pb, rb = 12, 4
            size = long(8)
            handle = self.heap; ptr = self.heap + 16
            self.heap = (ptr + size + 0xFF) & ~0xFF
            self.wr16(handle, ptr); self.wr16(handle + 2, ptr >> 16)
            res = handle
        elif n == 0x1002: pb, rb = 4, 0                                    # DisposeHandle
        elif n in (0x9004, 0x9104, 0xCA04): pb, rb = 0, 0                  # HideCursor / ShowCursor / InitCursor
        elif n == 0x590E:                                                  # AlertWindow
            pb, rb = 10, 2
            ptr = long(0); s = b''
            while self.mem[ptr]: s += bytes([self.mem[ptr]]); ptr += 1
            self.alerts.append(s.decode('latin-1')); res = 0
        elif n == 0x0A06:                                                  # GetNextEvent
            pb, rb = 6, 2
            rec = long(0)
            self.key_waits += 1
            if self.keys and self.key_waits % 3 == 0:
                k = self.keys.pop(0)
                if isinstance(k, tuple):  # a mouse click: mouseDown at (x, y)
                    self.wr16(rec, 1); self.wr16(rec + 2, 0); self.wr16(rec + 4, 0)
                    self.wr16(rec + 10, k[2]); self.wr16(rec + 12, k[1]); res = 0xFFFF
                elif k == '!':  # the other disks go in (no key event)
                    self.inserted = True
                    res = 0
                else:
                    code = {'U': 0x0B, 'D': 0x0A, 'L': 0x08, 'R': 0x15, '~': 0x0D, '^': 0x1B, '<': 0x7F, '`': 0x09, '{': 0x13, '}': 0x12, '|': 0x11}.get(k, ord(k))
                    self.wr16(rec, 3); self.wr16(rec + 2, code); self.wr16(rec + 4, 0); res = 0xFFFF
            elif not self.keys and self.key_waits > 10:  # the end: q (the quit question), then y
                self.quit_keys += 1
                self.wr16(rec, 3); self.wr16(rec + 2, ord('q') if self.quit_keys % 2 else ord('y')); self.wr16(rec + 4, 0); res = 0xFFFF
            else:
                res = 0
        elif n == 0x3704: pb, rb = 2, 0; self.pen = word(0)                # SetSolidPenPat
        elif n == 0x5404:                                                  # FrameRect: drawn, to see it
            pb, rb = 4, 0
            r = long(0); top, left, bottom, right = (self.rd16(r + i) for i in (0, 2, 4, 6))
            for x in range(left, right):
                self.pset(x, top); self.pset(x, bottom - 1)
            for y in range(top, bottom):
                self.pset(left, y); self.pset(right - 1, y)
        elif n == 0xA004: pb, rb = 2, 0; self.fore = word(0)               # SetForeColor
        elif n == 0xA204: pb, rb = 2, 0                                    # SetBackColor
        elif n == 0x3A04: pb, rb = 4, 0; self.penpos = (word(2), word(0))  # MoveTo
        elif n == 0xA804: pb, rb = 2, 2; res = 6                           # CharWidth: 6 pixels a letter here
        elif n == 0xAA04:                                                  # CStringWidth
            pb, rb = 4, 2
            ptr = long(0); k = 0
            while self.mem[ptr + k]: k += 1
            res = 6 * k
        elif n == 0x0E08:                                                  # FFStartSound
            pb, rb = 6, 0
            blk = long(0)
            self.sounds.append(dict(gen=word(4), wave=self.rd24(blk), pages=self.rd16(blk + 4), freq=self.rd16(blk + 6),
                                    doc=self.rd16(blk + 8), size=self.rd16(blk + 10), vol=self.rd16(blk + 16),
                                    first=bytes(self.mem[self.rd24(blk):self.rd24(blk) + 4]).hex(), loop=self.rd24(blk + 12) == blk))
            self.playing = getattr(self, 'playing', 0) | (1 << (word(4) >> 8))
        elif n == 0x0F08: pb, rb = 2, 0; self.playing = getattr(self, 'playing', 0) & ~word(0)   # FFStopSound
        elif n == 0x1008: pb, rb = 0, 2; res = getattr(self, 'playing', 0)                       # FFSoundStatus (they never end here)
        elif n == 0x0D08: pb, rb = 4, 0; self.volumes = getattr(self, 'volumes', []) + [(word(0), word(2))]  # SetSoundVolume
        elif n == 0x2503: pb, rb = 0, 4; res = self.steps // int(os.environ.get("SIM_TICK", "2000"))  # GetTick
        elif n == 0xA404:                                                  # DrawChar
            pb, rb = 2, 0
            self.typed.append(chr(word(0) & 0xFF))
        elif n == 0xA604:                                                  # DrawCString
            pb, rb = 4, 0
            ptr = long(0); s = b''
            while self.mem[ptr]: s += bytes([self.mem[ptr]]); ptr += 1
            self.texts.append((getattr(self, 'penpos', (0, 0)), getattr(self, 'fore', 0), s.decode('latin-1')))
        else:
            raise Stop(f'tool ${n:04X} not stood in for')
        ret = self.rd24(self.s + 1)
        if res is not None and rb:
            at = sp + pb
            for k in range(rb): self.wr(at + k, res >> (8 * k))
        self.s = (self.s + 3 + pb) & 0xFFFF
        self.wr(self.s, ret >> 16); self.wr(self.s - 1, ret >> 8); self.wr(self.s - 2, ret)
        self.s = (self.s - 3) & 0xFFFF
        self.a = 0
        self.flag(0x01, False)
        self.do_rtl()

    def pset(self, x, y):
        if 0 <= x < 320 and 0 <= y < 200:
            a = 0xE12000 + y * 160 + x // 2
            v = self.mem[a]
            self.mem[a] = (v & 0x0F) | (self.pen << 4) if x % 2 == 0 else (v & 0xF0) | self.pen

    def gsos(self):
        ret = self.rd24(self.s + 1)
        base = (ret & 0xFF0000) | ((ret + 1) & 0xFFFF)
        call = self.rd16(base)
        parm = self.rd24(base + 2)
        err = 0
        if call == 0x2010:   # Open
            pathp = self.rd24(parm + 4)
            n = self.rd16(pathp)
            path = bytes(self.mem[pathp + 2: pathp + 2 + n]).decode()
            name = path.split(':')[-1]
            full = os.path.join(self.folder, name)
            if path.startswith(':'):  # a disk by its name: there only once "put in" (the key !)
                vol = path.split(':')[1]
                full = os.path.join(self.folder, vol, name) if self.inserted else ''
            self.opened.append((path, os.path.exists(full)))
            if os.path.exists(full) and self.rd16(parm) >= 3 and self.rd16(parm + 8) & 2:  # opened to write
                self.wfiles[self.next_ref] = (full, bytearray())
                self.wr16(parm + 2, self.next_ref)
                self.next_ref += 1
            elif os.path.exists(full):
                self.files[self.next_ref] = open(full, 'rb').read()
                # pCount, refNum, pathname: refNum is at +2
                self.wr16(parm + 2, self.next_ref)
                self.next_ref += 1
            else:
                err = 0x46  # file not found
        elif call == 0x2019:  # GetEOF
            ref = self.rd16(parm + 2)
            n = len(self.files.get(ref, b''))
            self.wr16(parm + 4, n); self.wr16(parm + 6, n >> 16)
        elif call == 0x2012:  # Read
            ref = self.rd16(parm + 2)
            buf = self.rd24(parm + 4)
            req = self.rd16(parm + 8) | self.rd16(parm + 10) << 16
            d = self.files.get(ref, b'')[:req]
            self.mem[buf: buf + len(d)] = d
            self.wr16(parm + 12, len(d)); self.wr16(parm + 14, len(d) >> 16)
        elif call == 0x2014:  # Close: a file written is saved
            ref = self.rd16(parm + 2)
            if ref in self.wfiles:
                full, data = self.wfiles.pop(ref)
                open(full, 'wb').write(bytes(data))
                self.written.append((os.path.basename(full), len(data)))
        elif call == 0x2013:  # Write
            ref = self.rd16(parm + 2)
            buf = self.rd24(parm + 4)
            req = self.rd16(parm + 8) | self.rd16(parm + 10) << 16
            if ref in self.wfiles: self.wfiles[ref][1].extend(self.mem[buf: buf + req])
            self.wr16(parm + 12, req); self.wr16(parm + 14, req >> 16)
        elif call == 0x2006:  # GetFileInfo: there or not
            pathp = self.rd24(parm + 2)
            n = self.rd16(pathp)
            name = bytes(self.mem[pathp + 2: pathp + 2 + n]).decode().split(':')[-1]
            if not os.path.exists(os.path.join(self.folder, name)): err = 0x46
        elif call in (0x2001, 0x2002):  # Create, Destroy (in the game's folder)
            pathp = self.rd24(parm + 2)
            n = self.rd16(pathp)
            name = bytes(self.mem[pathp + 2: pathp + 2 + n]).decode().split(':')[-1]
            full = os.path.join(self.folder, name)
            if call == 0x2001:
                if os.path.exists(full): err = 0x47
                else: open(full, 'wb').close()
            elif os.path.exists(full): os.remove(full)
            else: err = 0x46
        elif call == 0x2029:  # Quit
            self.quit = True
            raise Stop('quit')
        else:
            raise Stop(f'GS/OS call ${call:04X} not stood in for')
        self.s = (self.s + 3) & 0xFFFF
        self.pb = base >> 16
        self.pc = (base + 6) & 0xFFFF
        self.a = err
        self.flag(0x01, err != 0)

    def do_rtl(self):
        lo = self.pull8(); hi = self.pull8(); bank = self.pull8()
        self.pc = ((hi << 8 | lo) + 1) & 0xFFFF
        self.pb = bank

    # ---- addressing ----
    def ea(self, mode):
        """The effective address (24-bit) of an operand."""
        if mode == 'dp': return self.bank0(self.d + self.fetch8())
        if mode == 'dpx': return self.bank0(self.d + self.fetch8() + self.x)
        if mode == 'dpy': return self.bank0(self.d + self.fetch8() + self.y)
        if mode == 'ind': return self.db << 16 | self.rd16(self.bank0(self.d + self.fetch8()))
        if mode == 'indx': return self.db << 16 | self.rd16(self.bank0(self.d + self.fetch8() + self.x))
        if mode == 'indy': return ((self.db << 16 | self.rd16(self.bank0(self.d + self.fetch8()))) + self.y) & 0xFFFFFF
        if mode == 'indl': return self.rd24(self.bank0(self.d + self.fetch8()))
        if mode == 'indly': return (self.rd24(self.bank0(self.d + self.fetch8())) + self.y) & 0xFFFFFF
        if mode == 'abs': return self.db << 16 | self.fetch16()
        if mode == 'absx': return ((self.db << 16 | self.fetch16()) + self.x) & 0xFFFFFF
        if mode == 'absy': return ((self.db << 16 | self.fetch16()) + self.y) & 0xFFFFFF
        if mode == 'long': return self.fetch24()
        if mode == 'longx': return (self.fetch24() + self.x) & 0xFFFFFF
        if mode == 'sr': return self.bank0(self.s + self.fetch8())
        if mode == 'sriy': return ((self.db << 16 | self.rd16(self.bank0(self.s + self.fetch8()))) + self.y) & 0xFFFFFF
        raise Stop('mode ' + mode)

    def load(self, mode, eight):
        if mode in ('imm', 'immx'):
            return self.fetch8() if eight else self.fetch16()
        a = self.ea(mode)
        return self.rd(a) if eight else self.rd16(a)

    def store(self, mode, v, eight):
        a = self.ea(mode)
        self.wr(a, v)
        if not eight: self.wr(a + 1, v >> 8)

    # ---- run ----
    def run(self, max_steps=int(os.environ.get("SIM_STEPS", "20000000"))):
        sys.path.insert(0, os.path.dirname(__file__))
        from asm816 import OPS
        table = {}
        for mn, modes in OPS.items():
            for mode, code in modes.items():
                if code not in table or mn not in ('blt', 'bge', 'jml', 'ina', 'dea'):
                    table.setdefault(code, (mn, mode))
        while self.steps < max_steps:
            self.steps += 1
            addr = self.pb << 16 | self.pc
            if addr == 0xE10000: self.tool(); continue
            if addr == 0xE100A8: self.gsos(); continue
            op = self.fetch8()
            if op not in table: raise Stop(f'unknown opcode ${op:02X} at {addr:06X}')
            mn, mode = table[op]
            if self.trace: print(f'{addr:06X} {mn} {mode} A={self.a:04X} X={self.x:04X} Y={self.y:04X} S={self.s:04X} P={self.p:02X}')
            self.execute(mn, mode)
        raise Stop('too many steps (a loop that never ends?)')

    def execute(self, mn, mode):
        m8, x8 = self.m8, self.x8
        mask = 0xFF if m8 else 0xFFFF
        xmask = 0xFF if x8 else 0xFFFF
        if mn == 'lda': self.a = (self.a & 0xFF00 | self.load(mode, True)) if m8 else self.load(mode, False); self.nz(self.a, m8)
        elif mn == 'ldx': self.x = self.load(mode, x8); self.nz(self.x, x8)
        elif mn == 'ldy': self.y = self.load(mode, x8); self.nz(self.y, x8)
        elif mn == 'sta': self.store(mode, self.a, m8)
        elif mn == 'stx': self.store(mode, self.x, x8)
        elif mn == 'sty': self.store(mode, self.y, x8)
        elif mn == 'stz': self.store(mode, 0, m8)
        elif mn in ('adc', 'sbc'):
            v = self.load(mode, m8)
            if mn == 'sbc': v = v ^ mask
            c = self.p & 1
            r = (self.a & mask) + v + c
            ov = (~((self.a & mask) ^ v) & ((self.a & mask) ^ r)) & (0x80 if m8 else 0x8000)
            self.flag(0x01, r > mask); self.flag(0x40, ov)
            r &= mask
            self.a = (self.a & 0xFF00 | r) if m8 else r
            self.nz(r, m8)
        elif mn in ('cmp', 'cpx', 'cpy'):
            eight = m8 if mn == 'cmp' else x8
            reg = {'cmp': self.a, 'cpx': self.x, 'cpy': self.y}[mn] & (0xFF if eight else 0xFFFF)
            v = self.load(mode, eight)
            r = reg - v
            self.flag(0x01, r >= 0); self.nz(r, eight)
        elif mn in ('and', 'ora', 'eor'):
            v = self.load(mode, m8)
            r = {'and': self.a & v, 'ora': self.a | v, 'eor': self.a ^ v}[mn] & mask
            self.a = (self.a & 0xFF00 | r) if m8 else r
            self.nz(r, m8)
        elif mn == 'bit':
            v = self.load(mode, m8)
            self.flag(0x02, (self.a & v & mask) == 0)
            if mode != 'imm':
                self.flag(0x80, v & (0x80 if m8 else 0x8000)); self.flag(0x40, v & (0x40 if m8 else 0x4000))
        elif mn in ('asl', 'lsr', 'rol', 'ror', 'inc', 'dec', 'tsb', 'trb'):
            if mode == 'acc':
                v = self.a & mask; a = None
            else:
                a = self.ea(mode); v = self.rd(a) if m8 else self.rd16(a)
            hb = 0x80 if m8 else 0x8000
            if mn == 'asl': self.flag(0x01, v & hb); v = (v << 1) & mask
            elif mn == 'lsr': self.flag(0x01, v & 1); v >>= 1
            elif mn == 'rol': c = self.p & 1; self.flag(0x01, v & hb); v = ((v << 1) | c) & mask
            elif mn == 'ror': c = self.p & 1; self.flag(0x01, v & 1); v = (v >> 1) | (hb if c else 0)
            elif mn == 'inc': v = (v + 1) & mask
            elif mn == 'dec': v = (v - 1) & mask
            elif mn == 'tsb': self.flag(0x02, (v & self.a & mask) == 0); v = (v | self.a) & mask
            elif mn == 'trb': self.flag(0x02, (v & self.a & mask) == 0); v = v & ~self.a & mask
            if mn not in ('tsb', 'trb'): self.nz(v, m8)
            if a is None: self.a = (self.a & 0xFF00 | v) if m8 else v
            else:
                self.wr(a, v)
                if not m8: self.wr(a + 1, v >> 8)
        elif mn in ('ina',): self.execute('inc', 'acc')
        elif mn in ('dea',): self.execute('dec', 'acc')
        elif mn in ('inx', 'dex', 'iny', 'dey'):
            r = 'x' if 'x' in mn else 'y'
            v = (getattr(self, r) + (1 if mn.startswith('in') else -1)) & xmask
            setattr(self, r, v); self.nz(v, x8)
        elif mn in ('tax', 'tay'):
            v = self.a & xmask; setattr(self, mn[2], v); self.nz(v, x8)
        elif mn in ('txa', 'tya'):
            v = getattr(self, mn[1])
            self.a = (self.a & 0xFF00 | v & 0xFF) if m8 else v & 0xFFFF; self.nz(self.a, m8)
        elif mn == 'txy': self.y = self.x & xmask; self.nz(self.y, x8)
        elif mn == 'tyx': self.x = self.y & xmask; self.nz(self.x, x8)
        elif mn == 'tcd': self.d = self.a & 0xFFFF; self.nz(self.d, False)
        elif mn == 'tdc': self.a = self.d; self.nz(self.a, False)
        elif mn == 'tcs': self.s = self.a & 0xFFFF
        elif mn == 'tsc': self.a = self.s; self.nz(self.a, False)
        elif mn == 'tsx': self.x = self.s & xmask; self.nz(self.x, x8)
        elif mn == 'txs': self.s = self.x
        elif mn == 'xba': self.a = ((self.a & 0xFF) << 8) | (self.a >> 8); self.nz(self.a & 0xFF, True)
        elif mn == 'pha': (self.push8(self.a) if m8 else self.push16(self.a))
        elif mn == 'phx': (self.push8(self.x) if x8 else self.push16(self.x))
        elif mn == 'phy': (self.push8(self.y) if x8 else self.push16(self.y))
        elif mn == 'pla':
            v = self.pull8() if m8 else self.pull16()
            self.a = (self.a & 0xFF00 | v) if m8 else v; self.nz(v, m8)
        elif mn == 'plx': self.x = self.pull8() if x8 else self.pull16(); self.nz(self.x, x8)
        elif mn == 'ply': self.y = self.pull8() if x8 else self.pull16(); self.nz(self.y, x8)
        elif mn == 'phb': self.push8(self.db)
        elif mn == 'plb': self.db = self.pull8(); self.nz(self.db, True)
        elif mn == 'phk': self.push8(self.pb)
        elif mn == 'phd': self.push16(self.d)
        elif mn == 'pld': self.d = self.pull16(); self.nz(self.d, False)
        elif mn == 'php': self.push8(self.p)
        elif mn == 'plp': self.p = self.pull8(); self.fix_widths()
        elif mn == 'pea': self.push16(self.fetch16())
        elif mn == 'pei': self.push16(self.rd16(self.bank0(self.d + self.fetch8())))
        elif mn == 'per': d = self.fetch16(); self.push16((self.pc + d) & 0xFFFF)
        elif mn in ('rep', 'sep'):
            v = self.fetch8()
            self.p = (self.p & ~v) if mn == 'rep' else (self.p | v)
            self.fix_widths()
        elif mn in ('clc', 'sec'): self.flag(0x01, mn == 'sec')
        elif mn in ('cli', 'sei'): self.flag(0x04, mn == 'sei')
        elif mn in ('cld', 'sed'): self.flag(0x08, mn == 'sed')
        elif mn == 'clv': self.flag(0x40, False)
        elif mode == 'rel':
            d = self.fetch8(); d = d - 256 if d > 127 else d
            cond = {'bcc': not self.p & 1, 'bcs': self.p & 1, 'beq': self.p & 2, 'bne': not self.p & 2,
                    'bmi': self.p & 0x80, 'bpl': not self.p & 0x80, 'bvc': not self.p & 0x40, 'bvs': self.p & 0x40,
                    'bra': True, 'blt': not self.p & 1, 'bge': self.p & 1}[mn]
            if cond: self.pc = (self.pc + d) & 0xFFFF
        elif mn == 'brl': d = self.fetch16(); self.pc = (self.pc + d) & 0xFFFF
        elif mn == 'jmp':
            if mode == 'abs': self.pc = self.fetch16()
            elif mode == 'long': t = self.fetch24(); self.pc, self.pb = t & 0xFFFF, t >> 16
            elif mode == 'absind': self.pc = self.rd16(self.fetch16())
            elif mode == 'absindx': self.pc = self.rd16(self.pb << 16 | ((self.fetch16() + self.x) & 0xFFFF))
            elif mode == 'absindl': t = self.rd24(self.fetch16()); self.pc, self.pb = t & 0xFFFF, t >> 16
        elif mn == 'jsr':
            if mode == 'abs':
                t = self.fetch16(); self.push16((self.pc - 1) & 0xFFFF); self.pc = t
            else:
                t = self.fetch16(); self.push16((self.pc - 1) & 0xFFFF); self.pc = self.rd16(self.pb << 16 | ((t + self.x) & 0xFFFF))
        elif mn == 'jsl':
            t = self.fetch24(); self.push8(self.pb); self.push16((self.pc - 1) & 0xFFFF); self.pc, self.pb = t & 0xFFFF, t >> 16
        elif mn == 'rts': self.pc = (self.pull16() + 1) & 0xFFFF
        elif mn == 'rtl': self.do_rtl()
        elif mn in ('mvn', 'mvp'):
            dst = self.fetch8(); src = self.fetch8()
            while True:
                self.wr(dst << 16 | self.y, self.rd(src << 16 | self.x))
                step = 1 if mn == 'mvn' else -1
                self.x = (self.x + step) & xmask; self.y = (self.y + step) & xmask
                self.a = (self.a - 1) & 0xFFFF
                if self.a == 0xFFFF: break
            self.db = dst
        elif mn == 'nop': pass
        elif mn == 'xce': pass
        elif mn == 'brk': raise Stop(f'BRK at {self.pb:02X}{(self.pc - 1) & 0xFFFF:04X}')
        elif mn in ('wai', 'stp', 'cop', 'wdm', 'rti'): raise Stop(f'{mn} reached')
        else: raise Stop(f'{mn} not simulated')

    def fix_widths(self):
        if self.x8: self.x &= 0xFF; self.y &= 0xFF

def png(path, m):
    pal = [m.rd16(0xE19E00 + i * 2) for i in range(16)]
    rgb = [((c >> 8 & 15) * 17, (c >> 4 & 15) * 17, (c & 15) * 17) for c in pal]
    rows = b''
    for y in range(200):
        line = bytearray([0])
        for x in range(320):
            b = m.mem[0xE12000 + y * 160 + x // 2]
            line += bytes(rgb[b >> 4 if x % 2 == 0 else b & 15])
        rows += bytes(line)
    def chunk(t, d): return struct.pack('>I', len(d)) + t + d + struct.pack('>I', zlib.crc32(t + d) & 0xFFFFFFFF)
    with open(path, 'wb') as f:
        f.write(b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', 320, 200, 8, 2, 0, 0, 0)) + chunk(b'IDAT', zlib.compress(rows)) + chunk(b'IEND', b''))

def main(argv):
    if len(argv) < 4: print(__doc__); return 2
    keys = ''
    if '--keys' in argv: keys = argv[argv.index('--keys') + 1]
    m = Machine(argv[2], keys, accelerated='--slow' not in argv, trace='--trace' in argv)
    m.load_omf(argv[1])
    try:
        m.run()
    except Stop as e:
        print('stopped:', e)
    png(argv[3], m)
    dp = m.d
    print(f'steps {m.steps}  speed count {m.rd16(dp + 0x4C)}  avatar at {m.rd16(dp + 0x2E)},{m.rd16(dp + 0x30)} facing {m.rd16(dp + 0x32)}')
    print('alerts:', m.alerts)
    print('texts:')
    for (pos, colour, text) in m.texts: print(f'   {pos} colour {colour}: {text}')
    print('typed letters echoed:', ''.join(m.typed))
    print('files opened:', m.opened)
    print('sounds started:', m.sounds)
    print('volumes set:', getattr(m, 'volumes', [])[-12:])
    print('files written:', m.written)
    print('quit to the desktop:', m.quit)
    return 0

if __name__ == '__main__':
    sys.exit(main(sys.argv))
