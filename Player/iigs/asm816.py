#!/usr/bin/env python3
"""
asm816 - Sandcastle RPG Maker's own 65816 assembler for the Apple IIgs player.

Writes a GS/OS S16 application (OMF 2): a code segment, a data segment (its own bank: every .db .dw .ds... goes there,
except tables that jmp (t,x) / jsr (t,x) read - the 65816 reads those from the code's bank), relocated by the System
Loader (cRELOC within a segment, cINTERSEG between them), and a direct page / stack segment. The program sets its data
bank register to the data segment's bank (bank(a data label)). Written for this project so the IIgs player builds with
nothing but Python.

Source syntax:
  label:            a label (a name starting with '.' is local to the last label without one)
  NAME = expr       a constant
  .a8 .a16 .i8 .i16 the accumulator / index width for '#' operands (REP / SEP #constant also set them)
  .db .dw .dl .dd   bytes, words, 3-byte longs, 4-byte longs (expressions or "strings" in .db)
  .ascii "t"        text;  .cstr "t" text + 0;  .pstr "t" length byte + text;  .gsstr "t" length word + text
  .ds n             n zero bytes
  .include "file"
  .code2 / .code1   the code that follows goes into the second code segment (its own bank) / back into the first. A jsr to
                    a routine in the other code segment becomes a jsl to a stub there (jsr routine / rtl); a jmp to one
                    (a tail call) becomes that jsl and an rts. Data stays in the data segment either way.
  tool $xxxx        a toolbox call: ldx #$xxxx / jsl $E10000
  gsos $xxxx,label  a GS/OS call: jsl $E100A8 / .dw $xxxx / .dl label
Operands: #imm  <dp  !abs  >long  (dp) (dp,x) (dp),y [dp] [dp],y  n,s (n,s),y  (abs) (abs,x) [abs]  src,dst (mvn/mvp)
Expressions: $hex %bin decimal 'c' labels + - * / & | ^ << >> ( ); lo(x) hi(x) bank(x)
A label inside the program is relocatable: its 16-bit address and bank() are patched by the loader.
Usage: asm816.py main.s -o SANDCASTLE [-l listing.txt]
"""
import os, re, struct, sys

OPS = {}
def add(mn, **modes):
    OPS.setdefault(mn, {}).update(modes)

G1 = ['imm', 'dp', 'dpx', 'ind', 'indx', 'indy', 'indl', 'indly', 'abs', 'absx', 'absy', 'long', 'longx', 'sr', 'sriy']
for mn, codes in {
    'adc': [0x69,0x65,0x75,0x72,0x61,0x71,0x67,0x77,0x6D,0x7D,0x79,0x6F,0x7F,0x63,0x73],
    'and': [0x29,0x25,0x35,0x32,0x21,0x31,0x27,0x37,0x2D,0x3D,0x39,0x2F,0x3F,0x23,0x33],
    'cmp': [0xC9,0xC5,0xD5,0xD2,0xC1,0xD1,0xC7,0xD7,0xCD,0xDD,0xD9,0xCF,0xDF,0xC3,0xD3],
    'eor': [0x49,0x45,0x55,0x52,0x41,0x51,0x47,0x57,0x4D,0x5D,0x59,0x4F,0x5F,0x43,0x53],
    'lda': [0xA9,0xA5,0xB5,0xB2,0xA1,0xB1,0xA7,0xB7,0xAD,0xBD,0xB9,0xAF,0xBF,0xA3,0xB3],
    'ora': [0x09,0x05,0x15,0x12,0x01,0x11,0x07,0x17,0x0D,0x1D,0x19,0x0F,0x1F,0x03,0x13],
    'sbc': [0xE9,0xE5,0xF5,0xF2,0xE1,0xF1,0xE7,0xF7,0xED,0xFD,0xF9,0xEF,0xFF,0xE3,0xF3],
    'sta': [None,0x85,0x95,0x92,0x81,0x91,0x87,0x97,0x8D,0x9D,0x99,0x8F,0x9F,0x83,0x93],
}.items():
    add(mn, **{m: c for m, c in zip(G1, codes) if c is not None})
for mn, (acc, dp, dpx, ab, abx) in {'asl': (0x0A,0x06,0x16,0x0E,0x1E), 'lsr': (0x4A,0x46,0x56,0x4E,0x5E),
                                    'rol': (0x2A,0x26,0x36,0x2E,0x3E), 'ror': (0x6A,0x66,0x76,0x6E,0x7E),
                                    'dec': (0x3A,0xC6,0xD6,0xCE,0xDE), 'inc': (0x1A,0xE6,0xF6,0xEE,0xFE)}.items():
    add(mn, acc=acc, dp=dp, dpx=dpx, abs=ab, absx=abx)
add('bit', imm=0x89, dp=0x24, dpx=0x34, abs=0x2C, absx=0x3C)
add('cpx', immx=0xE0, dp=0xE4, abs=0xEC)
add('cpy', immx=0xC0, dp=0xC4, abs=0xCC)
add('ldx', immx=0xA2, dp=0xA6, dpy=0xB6, abs=0xAE, absy=0xBE)
add('ldy', immx=0xA0, dp=0xA4, dpx=0xB4, abs=0xAC, absx=0xBC)
add('stx', dp=0x86, dpy=0x96, abs=0x8E)
add('sty', dp=0x84, dpx=0x94, abs=0x8C)
add('stz', dp=0x64, dpx=0x74, abs=0x9C, absx=0x9E)
add('trb', dp=0x14, abs=0x1C)
add('tsb', dp=0x04, abs=0x0C)
add('jmp', abs=0x4C, absind=0x6C, absindx=0x7C, long=0x5C, absindl=0xDC)
add('jml', long=0x5C, absindl=0xDC)
add('jsr', abs=0x20, absindx=0xFC)
add('jsl', long=0x22)
for mn, c in {'bcc':0x90,'bcs':0xB0,'beq':0xF0,'bmi':0x30,'bne':0xD0,'bpl':0x10,'bra':0x80,'bvc':0x50,'bvs':0x70,'blt':0x90,'bge':0xB0}.items():
    add(mn, rel=c)
add('brl', rell=0x82)
add('per', rell=0x62)
add('pea', abs=0xF4)
add('pei', ind=0xD4)
add('mvn', bm=0x54)
add('mvp', bm=0x44)
for mn, c in {'rep':0xC2,'sep':0xE2,'cop':0x02,'wdm':0x42}.items():
    add(mn, imm8=c)
add('brk', imp=0x00, imm8=0x00)
for mn, c in {'clc':0x18,'cld':0xD8,'cli':0x58,'clv':0xB8,'sec':0x38,'sed':0xF8,'sei':0x78,'dex':0xCA,'dey':0x88,
              'inx':0xE8,'iny':0xC8,'nop':0xEA,'pha':0x48,'phb':0x8B,'phd':0x0B,'phk':0x4B,'php':0x08,'phx':0xDA,
              'phy':0x5A,'pla':0x68,'plb':0xAB,'pld':0x2B,'plp':0x28,'plx':0xFA,'ply':0x7A,'rti':0x40,'rtl':0x6B,
              'rts':0x60,'stp':0xDB,'tax':0xAA,'tay':0xA8,'tcd':0x5B,'tcs':0x1B,'tdc':0x7B,'tsc':0x3B,'tsx':0xBA,
              'txa':0x8A,'txs':0x9A,'txy':0x9B,'tya':0x98,'tyx':0xBB,'wai':0xCB,'xba':0xEB,'xce':0xFB}.items():
    add(mn, imp=c)
add('ina', imp=0x1A)
add('dea', imp=0x3A)

SIZE = {'imp':0,'acc':0,'imm8':1,'dp':1,'dpx':1,'dpy':1,'ind':1,'indx':1,'indy':1,'indl':1,'indly':1,'sr':1,'sriy':1,
        'abs':2,'absx':2,'absy':2,'absind':2,'absindx':2,'absindl':2,'long':3,'longx':3,'rel':1,'rell':2,'bm':2}

class AsmError(Exception):
    pass

class Val:
    """A value: a constant, or a place in the program (reloc=True: the loader adds where the program was put)."""
    __slots__ = ('v', 'reloc', 'fn')
    def __init__(self, v, reloc=False, fn=None):
        self.v, self.reloc, self.fn = v, reloc, fn
    def _c(self, o):
        return o if isinstance(o, Val) else Val(o)
    def __add__(self, o):
        o = self._c(o)
        if self.reloc and o.reloc: raise AsmError('adding two program addresses')
        return Val(self.v + o.v, self.reloc or o.reloc)
    __radd__ = __add__
    def __sub__(self, o):
        o = self._c(o)
        if o.reloc and not self.reloc: raise AsmError('constant minus a program address')
        if self.reloc and o.reloc and self.reloc != o.reloc: raise AsmError('a code address minus a data address')
        return Val(self.v - o.v, self.reloc if not o.reloc else 0)
    def __rsub__(self, o):
        return self._c(o).__sub__(self)
    def _const(self, o, f):
        o = self._c(o)
        if self.reloc or o.reloc: raise AsmError('arithmetic on a program address (only + and - work)')
        return Val(f(self.v, o.v))
    def __mul__(self, o): return self._const(o, lambda a, b: a * b)
    __rmul__ = __mul__
    def __floordiv__(self, o): return self._const(o, lambda a, b: a // b)
    def __truediv__(self, o): return self._const(o, lambda a, b: a // b)
    def __and__(self, o): return self._const(o, lambda a, b: a & b)
    __rand__ = __and__
    def __or__(self, o): return self._const(o, lambda a, b: a | b)
    __ror__ = __or__
    def __xor__(self, o): return self._const(o, lambda a, b: a ^ b)
    def __lshift__(self, o): return self._const(o, lambda a, b: a << b)
    def __rshift__(self, o): return self._const(o, lambda a, b: a >> b)
    def __neg__(self):
        if self.reloc: raise AsmError('negating a program address')
        return Val(-self.v)

def f_lo(x):
    x = x if isinstance(x, Val) else Val(x)
    if x.reloc: return Val(x.v, x.reloc, 'lo')
    return Val(x.v & 0xFF)
def f_hi(x):
    x = x if isinstance(x, Val) else Val(x)
    if x.reloc: return Val(x.v, x.reloc, 'hi')
    return Val((x.v >> 8) & 0xFF)
def f_bank(x):
    x = x if isinstance(x, Val) else Val(x)
    if x.reloc: return Val(x.v, x.reloc, 'bank')
    return Val((x.v >> 16) & 0xFF)

TOKEN = re.compile(r"\s*(\$[0-9A-Fa-f]+|%[01]+|\d+|'(?:[^'\\]|\\.)'|[A-Za-z_.][A-Za-z0-9_.]*|<<|>>|[-+*/&|^()~,])")

class Assembler:
    def __init__(self):
        self.symbols = {}
        self.pass_no = 0
        self.changed = False
        self.code_tables = set()   # tables jmp (t,x) / jsr (t,x) read: they stay with the code
        self.codesec = 1           # where code goes: 1 the first code segment, 3 the second (.code2)
        self.far = set()           # routines called from the other code segment: each gets a stub (jsr it / rtl) in its own

    def expr(self, text, scope, line):
        out = []
        pos = 0
        text = text.strip()
        while pos < len(text):
            m = TOKEN.match(text, pos)
            if not m: raise AsmError(f'cannot read "{text[pos:]}"')
            t = m.group(1)
            pos = m.end()
            if t.startswith('$'): out.append(f'Val({int(t[1:], 16)})')
            elif t.startswith('%'): out.append(f'Val({int(t[1:], 2)})')
            elif t[0].isdigit(): out.append(f'Val({int(t)})')
            elif t.startswith("'"):
                ch = t[1:-1].encode().decode('unicode_escape')
                out.append(f'Val({ord(ch)})')
            elif t in ('lo', 'hi', 'bank'): out.append('f_' + t)
            elif re.match(r'[A-Za-z_.]', t):
                name = scope + t if t.startswith('.') else t
                if name in self.symbols:
                    out.append(f'_s[{name!r}]')
                elif self.pass_no == 1:
                    self.changed = True
                    out.append('Val(0, 1)')
                else:
                    raise AsmError(f'unknown name "{t}"')
            elif t == '/': out.append('//')
            elif t == '~': out.append('-1-')
            else: out.append(t)
        try:
            v = eval(' '.join(out), {'Val': Val, 'f_lo': f_lo, 'f_hi': f_hi, 'f_bank': f_bank, '_s': self.symbols})
        except AsmError:
            raise
        except Exception as e:
            raise AsmError(f'bad expression "{text}": {e}')
        return v if isinstance(v, Val) else Val(v)

    def read(self, path, seen=None):
        lines = []
        seen = seen or set()
        full = os.path.abspath(path)
        if full in seen: raise AsmError(f'{path} includes itself')
        seen = seen | {full}
        with open(path, encoding='utf-8') as f:
            for n, raw in enumerate(f, 1):
                code = strip_comment(raw).rstrip()
                m = re.match(r'\s*\.include\s+"([^"]+)"', code, re.I)
                if m:
                    lines += self.read(os.path.join(os.path.dirname(path), m.group(1)), seen)
                else:
                    lines.append((path, n, code))
        return lines

    def assemble(self, path):
        lines = self.read(path)
        for self.pass_no in range(1, 10):
            self.changed = False
            self.outs = {1: bytearray(), 2: bytearray(), 3: bytearray()}
            self.rels = {1: [], 2: [], 3: []}
            self.codesec = 1
            self.section(1)
            self.listing = []
            self.run(lines)
            self.far_stubs()
            if self.pass_no > 1 and not self.changed: break
        else:
            raise AsmError('sizes never settled')
        return {k: bytes(v) for k, v in self.outs.items()}, self.rels

    def far_stubs(self):
        """Each routine called from the other code segment: its stub at the end of its own segment - jsr it, then rtl."""
        for target in sorted(self.far):
            v = self.symbols.get(target)
            sec = v.reloc if v is not None and v.reloc in (1, 3) else 1
            self.section(sec)
            self.define('__far_' + target, Val(len(self.out), sec))
            self.emit(0x20)
            self.value(v if v is not None else Val(0, sec), 2)
            self.emit(0x6B)
        self.section(1)

    def section(self, sec):
        self.sec = sec
        self.out = self.outs[sec]
        self.relocs = self.rels[sec]

    def define(self, name, val):
        old = self.symbols.get(name)
        if self.pass_no == 1 and old is not None:
            raise AsmError(f'{name} defined twice')
        if old is None or old.v != val.v or old.reloc != val.reloc:
            self.changed = True
        self.symbols[name] = val

    DATA = ('.db', '.dw', '.dl', '.dd', '.ascii', '.cstr', '.pstr', '.gsstr', '.ds', '.align')

    def run(self, lines):
        self.m16 = True
        self.x16 = True
        scope = ''
        pending = []          # labels waiting for what follows them: code, or data (and which)
        table = False         # data that follows a jump table's label: with the code
        for path, n, code in lines:
            try:
                if not code.strip(): continue
                m = re.match(r'\s*([A-Za-z_][A-Za-z0-9_]*)\s*=\s*(.+)$', code)
                if m:
                    self.define(m.group(1), self.expr(m.group(2), scope, n))
                    continue
                m = re.match(r'\s*(\.?[A-Za-z_][A-Za-z0-9_.]*):\s*(.*)$', code)
                if m:
                    name = m.group(1)
                    if not name.startswith('.'):
                        scope = name
                    else:
                        name = scope + name
                    pending.append(name)
                    code = m.group(2)
                    if not code.strip(): continue
                op = code.strip().split(None, 1)[0].lower()
                if op in ('.a8', '.a16', '.i8', '.i16'):
                    self.statement(code.strip(), scope, n)
                    continue
                if op in ('.code1', '.code2'):
                    self.codesec = 1 if op == '.code1' else 3
                    self.section(self.codesec)
                    continue
                if op in self.DATA:
                    if pending: table = any(p in self.code_tables for p in pending)
                    self.section(self.codesec if table else 2)
                else:
                    self.section(self.codesec)
                    table = False
                for name in pending: self.define(name, Val(len(self.out), self.sec))
                pending = []
                self.statement(code.strip(), scope, n)
            except AsmError as e:
                raise AsmError(f'{os.path.basename(path)}:{n}: {e}\n    {code.strip()}')
        self.section(self.codesec)
        for name in pending: self.define(name, Val(len(self.out), self.codesec))

    def emit(self, *bs):
        self.out += bytes(b & 0xFF for b in bs)

    def value(self, v, size, signed_ok=True):
        """Writes a value of size bytes; a program address gets a relocation record."""
        at = len(self.out)
        if v.reloc:
            if v.fn == 'bank':
                if size != 1 and size != 2: raise AsmError('bank() is one or two bytes')
                self.relocs.append((at, 1, -16, v.v, v.reloc))
                self.emit(0, *([0] * (size - 1)))
            elif v.fn == 'hi':
                self.relocs.append((at, 1, -8, v.v, v.reloc))
                self.emit(0, *([0] * (size - 1)))
            elif v.fn == 'lo':
                self.relocs.append((at, 1, 0, v.v, v.reloc))
                self.emit(0, *([0] * (size - 1)))
            else:
                if size not in (2, 3, 4): raise AsmError('a program address needs 2, 3 or 4 bytes')
                self.relocs.append((at, min(size, 3), 0, v.v, v.reloc))
                for i in range(size): self.emit(v.v >> (8 * i) if i < 2 else 0)
        else:
            x = v.v
            lim = 1 << (8 * size)
            if self.pass_no > 1 and not (-lim // 2 <= x < lim): raise AsmError(f'{x} does not fit in {size} byte(s)')
            for i in range(size): self.emit(x >> (8 * i))

    def strings(self, args):
        parts, cur, q = [], '', False
        i = 0
        while i < len(args):
            c = args[i]
            if c == '"':
                q = not q; cur += c
            elif c == '\\' and q and i + 1 < len(args):
                cur += args[i:i + 2]; i += 1
            elif c == ',' and not q:
                parts.append(cur.strip()); cur = ''
            else:
                cur += c
            i += 1
        if cur.strip(): parts.append(cur.strip())
        return parts

    def text(self, s):
        if not (s.startswith('"') and s.endswith('"')): raise AsmError('text goes in "quotes"')
        return s[1:-1].encode('latin-1').decode('unicode_escape').encode('latin-1')

    def statement(self, code, scope, n):
        parts = code.split(None, 1)
        op = parts[0].lower()
        args = parts[1].strip() if len(parts) > 1 else ''
        if op.startswith('.'):
            return self.directive(op, args, scope)
        if op == 'tool':
            v = self.expr(args, scope, n)
            self.emit(0xA2); self.value(v, 2)
            self.emit(0x22, 0x00, 0x00, 0xE1)
            return
        if op == 'gsos':
            num, parm = [a.strip() for a in args.split(',', 1)]
            self.emit(0x22, 0xA8, 0x00, 0xE1)
            self.value(self.expr(num, scope, n), 2)
            self.value(self.expr(parm, scope, n), 4)
            return
        if op not in OPS: raise AsmError(f'unknown instruction "{op}"')
        modes = OPS[op]
        mode, v, v2 = self.mode(op, modes, args, scope, n)
        target = args.strip()
        if (op in ('jsr', 'jmp') and mode == 'abs' and v.reloc in (1, 3) and v.reloc != self.sec and not v.fn
                and re.match(r'^[A-Za-z_]\w*$', target) and target in self.symbols and self.symbols[target].reloc == v.reloc):
            # Into the other code segment (a routine already placed): jsl its stub there (jsr routine / rtl); a jmp (a tail call) then returns.
            if target not in self.far: self.far.add(target); self.changed = True
            stub = self.symbols.get('__far_' + target)
            self.emit(0x22)
            self.value(stub if stub is not None else Val(0, v.reloc), 3)
            if op == 'jmp': self.emit(0x60)
            return
        code_byte = modes[mode]
        self.emit(code_byte)
        if mode in ('imp', 'acc'): return
        if mode == 'imm':
            self.value(v, 2 if self.m16 else 1)
        elif mode == 'immx':
            self.value(v, 2 if self.x16 else 1)
        elif mode == 'imm8':
            self.value(v, 1)
            if op in ('rep', 'sep') and not v.reloc:
                on = op == 'sep'
                if v.v & 0x20: self.m16 = not on
                if v.v & 0x10: self.x16 = not on
        elif mode == 'rel':
            if v.reloc == 2: raise AsmError('a branch to data')
            if v.reloc and v.reloc != self.sec and self.pass_no > 1: raise AsmError('a branch into the other code segment')
            if v.reloc:
                d = v.v - (len(self.out) + 1)
                if not -128 <= d <= 127:
                    if self.pass_no > 1: raise AsmError(f'branch too far ({d})')
                    d = 0
                self.emit(d)
            else: raise AsmError('a branch goes to a label')
        elif mode == 'rell':
            d = v.v - (len(self.out) + 2)
            self.emit(d, d >> 8)
        elif mode == 'bm':
            self.value(v2, 1)  # destination bank first
            self.value(v, 1)
        else:
            self.value(v, SIZE[mode])

    def mode(self, op, modes, a, scope, n):
        al = a.lower().replace(' ', '')
        if a == '' or al == 'a':
            if a == '' and 'imp' in modes: return 'imp', None, None
            if 'acc' in modes: return 'acc', None, None
            if 'imp' in modes: return 'imp', None, None
            raise AsmError(f'{op} needs an operand')
        if a.startswith('#'):
            v = self.expr(a[1:], scope, n)
            for m in ('imm', 'immx', 'imm8'):
                if m in modes: return m, v, None
            if 'abs' in modes and op == 'pea': return 'abs', v, None
            raise AsmError(f'{op} has no immediate mode')
        if op in ('brk', 'cop', 'wdm'): return 'imm8', self.expr(a, scope, n), None
        if 'rel' in modes: return 'rel', self.expr(a, scope, n), None
        if 'rell' in modes: return 'rell', self.expr(a, scope, n), None
        if 'bm' in modes:
            s, d = a.split(',')
            return 'bm', self.expr(s, scope, n), self.expr(d, scope, n)
        m = re.match(r'^\((.+),\s*s\s*\)\s*,\s*y$', a, re.I)
        if m: return 'sriy', self.expr(m.group(1), scope, n), None
        m = re.match(r'^(.+),\s*s$', a, re.I)
        if m: return 'sr', self.expr(m.group(1), scope, n), None
        m = re.match(r'^\[(.+)\]\s*,\s*y$', a, re.I)
        if m: return 'indly', self.expr(m.group(1), scope, n), None
        m = re.match(r'^\[(.+)\]$', a)
        if m:
            v = self.expr(m.group(1), scope, n)
            if 'absindl' in modes: return 'absindl', v, None
            return 'indl', v, None
        m = re.match(r'^\((.+)\)\s*,\s*y$', a, re.I)
        if m: return 'indy', self.expr(m.group(1), scope, n), None
        m = re.match(r'^\((.+),\s*x\)$', a, re.I)
        if m:
            v = self.expr(m.group(1), scope, n)
            if 'absindx' in modes:
                t = m.group(1).strip()
                if t not in self.code_tables: self.code_tables.add(t); self.changed = True
                return 'absindx', v, None
            return 'indx', v, None
        m = re.match(r'^\((.+)\)$', a)
        if m and not re.search(r'[-+*/&|^]\s*\(|\)\s*[-+*/&|^]', a):
            v = self.expr(m.group(1), scope, n)
            if 'absind' in modes: return 'absind', v, None
            return 'ind', v, None
        index = None
        m = re.match(r'^(.+),\s*([xy])$', a, re.I)
        if m: a, index = m.group(1).strip(), m.group(2).lower()
        force = None
        if a[0] in '<!|>':
            force = {'<': 'dp', '!': 'abs', '|': 'abs', '>': 'long'}[a[0]]
            a = a[1:]
        v = self.expr(a, scope, n)
        if force is None:
            if 'long' in modes and not ('abs' in modes) : force = 'long'
            elif not v.reloc and 0 <= v.v < 0x100 and ('dp' in modes or 'dpx' in modes or 'dpy' in modes): force = 'dp'
            elif not v.reloc and v.v >= 0x10000 and ('long' in modes or 'longx' in modes): force = 'long'
            else: force = 'abs'
        name = force + (index or '')
        if name == 'longy': raise AsmError('there is no long,y - use [dp],y')
        if name not in modes:
            if force == 'dp' and ('abs' + (index or '')) in modes: name = 'abs' + (index or '')
            else: raise AsmError(f'{op} has no {name} mode')
        return name, v, None

    def directive(self, op, args, scope):
        n = 0
        if op == '.a8': self.m16 = False
        elif op == '.a16': self.m16 = True
        elif op == '.i8': self.x16 = False
        elif op == '.i16': self.x16 = True
        elif op in ('.db', '.dw', '.dl', '.dd'):
            size = {'.db': 1, '.dw': 2, '.dl': 3, '.dd': 4}[op]
            for item in self.strings(args):
                if item.startswith('"'):
                    if size != 1: raise AsmError('text only in .db')
                    self.out += self.text(item)
                else:
                    self.value(self.expr(item, scope, n), size)
        elif op == '.ascii': self.out += self.text(args)
        elif op == '.cstr': self.out += self.text(args) + b'\0'
        elif op == '.pstr':
            t = self.text(args); self.emit(len(t)); self.out += t
        elif op == '.gsstr':
            t = self.text(args); self.emit(len(t), len(t) >> 8); self.out += t
        elif op == '.ds':
            self.out += bytes(self.expr(args, scope, n).v)
        elif op == '.align':
            a = self.expr(args, scope, n).v
            while len(self.out) % a: self.emit(0)
        else:
            raise AsmError(f'unknown directive {op}')

def strip_comment(line):
    out, q, sq = '', False, False
    i = 0
    while i < len(line):
        c = line[i]
        if c == '"' and not sq: q = not q
        elif c == "'" and not q and i + 2 < len(line) and line[i + 2] == "'" :
            out += line[i:i + 3]; i += 3; continue
        elif c == ';' and not q: break
        out += c
        i += 1
    return out

def omf_segment(segnum, kind, name, body, length, banksize=0x10000, align=0):
    loadname = b' ' * 10
    segname = name.encode()
    disp_name = 44
    disp_data = disp_name + 10 + 1 + len(segname)
    head = bytearray(disp_data)
    total = disp_data + len(body)
    struct.pack_into('<IIIBBBBIHHIIBBHIHH', head, 0,
                     total, 0, length, 0, 0, 4, 2, banksize, kind, 0, 0, align, 0, 0, segnum, 0, disp_name, disp_data)
    head[disp_name:disp_name + 10] = loadname
    head[disp_name + 10] = len(segname)
    head[disp_name + 11:disp_data] = segname
    return bytes(head) + body

def omf_body(segnum, data, relocs):
    body = bytearray()
    body += bytes([0xF2]) + struct.pack('<I', len(data)) + data
    for at, size, shift, ref, refseg in relocs:
        if at > 0xFFFF or ref > 0xFFFF: raise AsmError('a segment is bigger than 64 KB')
        if refseg == segnum:
            body += bytes([0xF5, size, shift & 0xFF]) + struct.pack('<HH', at, ref)
        else:  # cINTERSEG: count, shift, offset, segment, offset in it
            body += bytes([0xF6, size, shift & 0xFF]) + struct.pack('<HBH', at, refseg, ref)
    body += b'\0'
    return bytes(body)

def omf_file(outs, rels, stack_size=0x1000):
    seg1 = omf_segment(1, 0x0000, 'main', omf_body(1, outs[1], rels[1]), len(outs[1]))
    seg2 = omf_segment(2, 0x0001, 'data', omf_body(2, outs[2], rels[2]), len(outs[2]))
    segs = seg1 + seg2
    n = 3
    if outs.get(3):  # the second code segment (.code2)
        segs += omf_segment(3, 0x0000, 'main2', omf_body(3, outs[3], rels[3]), len(outs[3]))
        n = 4
    # The direct page / stack segment (KIND $12): zeroes, a page-aligned bank-0 block the loader gives the program.
    ds = bytes([0xF1]) + struct.pack('<I', stack_size) + b'\0'
    return segs + omf_segment(n, 0x0012, '~Direct', ds, stack_size, banksize=0, align=0)

def main(argv):
    src, out, lst = None, None, None
    i = 1
    while i < len(argv):
        if argv[i] == '-o': out = argv[i + 1]; i += 2
        elif argv[i] == '-l': lst = argv[i + 1]; i += 2
        else: src = argv[i]; i += 1
    if not src or not out:
        print(__doc__); return 2
    asm = Assembler()
    try:
        outs, rels = asm.assemble(src)
    except AsmError as e:
        print('error:', e); return 1
    for k, what in ((1, 'code'), (2, 'data'), (3, 'second code segment')):
        if len(outs[k]) > 0xFFFF:
            print(f'error: the program\'s {what} is {len(outs[k])} bytes - a segment holds 64 KB'); return 1
    try:
        image = omf_file(outs, rels)
    except AsmError as e:
        print('error:', e); return 1
    with open(out, 'wb') as f:
        f.write(image)
    if lst:
        with open(lst, 'w') as f:
            for name, v in sorted(asm.symbols.items(), key=lambda kv: (kv[1].reloc, kv[1].v)):
                f.write(f'{v.v:06X} {"R" if v.reloc == 1 else "D" if v.reloc == 2 else "S" if v.reloc == 3 else " "} {name}\n')
    print(f'{out}: {len(outs[1])} bytes of code, {len(outs[3])} in the second code segment, {len(outs[2])} of data, {len(rels[1]) + len(rels[2]) + len(rels[3])} relocations')
    return 0

if __name__ == '__main__':
    sys.exit(main(sys.argv))
