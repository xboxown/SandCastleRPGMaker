/*
 * The game's files, encrypted as the Windows game's are (ProjectContainer):
 * "SRPG", the format version, the project's id (a Guid's 16 bytes), the IV,
 * then the AES-256-CBC cipher text (PKCS7). The key is
 * SHA-256("SandcastleRpgMaker.ProjectKey.v1:" + the id as text), the id read
 * from the file itself, as the Windows game reads its own. pictures.sdbx and
 * sounds.sdbx are archives (SRPK): an encrypted index, then each file in a
 * container of its own - only what's shown or played is decrypted. A
 * sound is decrypted a little at a time as it plays (CBC lets any block be
 * decrypted with the one before it as its IV), never all at once: a 68060
 * would spend minutes on a long piece of music before a note was heard.
 * AES-256 decryption by lookup tables (the inverse cipher's Td tables, made
 * at start from the S-box), many times faster than doing it byte by byte.
 */
#include "pack.h"
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <time.h>

/* ---------------- SHA-256 ---------------- */

typedef struct Sha { uint32_t h[8]; unsigned char block[64]; uint64_t length; int used; } Sha;

static const uint32_t K[64] = {
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5, 0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3,
    0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174, 0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967, 0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13,
    0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85, 0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3, 0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208,
    0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2 };

#define ROR(x, n) (((x) >> (n)) | ((x) << (32 - (n))))

static void sha_block(Sha *s, const unsigned char *p)
{
    uint32_t w[64], a, b, c, d, e, f, g, h;
    for (int i = 0; i < 16; i++) w[i] = (uint32_t)p[i * 4] << 24 | (uint32_t)p[i * 4 + 1] << 16 | (uint32_t)p[i * 4 + 2] << 8 | p[i * 4 + 3];
    for (int i = 16; i < 64; i++)
    {
        uint32_t s0 = ROR(w[i - 15], 7) ^ ROR(w[i - 15], 18) ^ (w[i - 15] >> 3), s1 = ROR(w[i - 2], 17) ^ ROR(w[i - 2], 19) ^ (w[i - 2] >> 10);
        w[i] = w[i - 16] + s0 + w[i - 7] + s1;
    }
    a = s->h[0]; b = s->h[1]; c = s->h[2]; d = s->h[3]; e = s->h[4]; f = s->h[5]; g = s->h[6]; h = s->h[7];
    for (int i = 0; i < 64; i++)
    {
        uint32_t t1 = h + (ROR(e, 6) ^ ROR(e, 11) ^ ROR(e, 25)) + ((e & f) ^ (~e & g)) + K[i] + w[i];
        uint32_t t2 = (ROR(a, 2) ^ ROR(a, 13) ^ ROR(a, 22)) + ((a & b) ^ (a & c) ^ (b & c));
        h = g; g = f; f = e; e = d + t1; d = c; c = b; b = a; a = t1 + t2;
    }
    s->h[0] += a; s->h[1] += b; s->h[2] += c; s->h[3] += d; s->h[4] += e; s->h[5] += f; s->h[6] += g; s->h[7] += h;
}

static void sha256(const unsigned char *data, size_t n, unsigned char out[32])
{
    Sha s = { { 0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19 }, { 0 }, 0, 0 };
    s.length = (uint64_t)n * 8;
    while (n >= 64) { sha_block(&s, data); data += 64; n -= 64; }
    unsigned char last[128] = { 0 };
    memcpy(last, data, n);
    last[n] = 0x80;
    size_t total = n + 9 <= 64 ? 64 : 128;
    for (int i = 0; i < 8; i++) last[total - 1 - i] = (unsigned char)(s.length >> (8 * i));
    sha_block(&s, last);
    if (total == 128) sha_block(&s, last + 64);
    for (int i = 0; i < 8; i++) { out[i * 4] = (unsigned char)(s.h[i] >> 24); out[i * 4 + 1] = (unsigned char)(s.h[i] >> 16); out[i * 4 + 2] = (unsigned char)(s.h[i] >> 8); out[i * 4 + 3] = (unsigned char)s.h[i]; }
}

/* ---------------- AES-256 decryption (FIPS-197, table-driven) ---------------- */

#define NR 14
static uint8_t sbox[256], inv[256];
static uint32_t Td0[256], Td1[256], Td2[256], Td3[256], Te0[256], Te1[256], Te2[256], Te3[256];
static int tablesReady;

static uint8_t xtime(uint8_t x) { return (uint8_t)((x << 1) ^ ((x & 0x80) ? 0x1b : 0)); }
static uint8_t gmul(uint8_t a, uint8_t b) { uint8_t p = 0; while (b) { if (b & 1) p ^= a; a = xtime(a); b >>= 1; } return p; }
static uint8_t rotl8(uint8_t x, int n) { return (uint8_t)((x << n) | (x >> (8 - n))); }
static uint32_t ror32(uint32_t x, int n) { return (x >> n) | (x << (32 - n)); }

static void make_tables(void)
{
    if (tablesReady) return;
    /* the S-box: the multiplicative inverse in GF(2^8), then the affine map */
    uint8_t p = 1, q = 1;
    do
    {
        p = (uint8_t)(p ^ (p << 1) ^ ((p & 0x80) ? 0x1b : 0));
        q ^= (uint8_t)(q << 1); q ^= (uint8_t)(q << 2); q ^= (uint8_t)(q << 4);
        if (q & 0x80) q ^= 0x09;
        sbox[p] = (uint8_t)(q ^ rotl8(q, 1) ^ rotl8(q, 2) ^ rotl8(q, 3) ^ rotl8(q, 4) ^ 0x63);
    } while (p != 1);
    sbox[0] = 0x63;
    for (int i = 0; i < 256; i++) inv[sbox[i]] = (uint8_t)i;
    for (int i = 0; i < 256; i++)
    {
        uint8_t s = inv[i];
        Td0[i] = (uint32_t)gmul(s, 14) << 24 | (uint32_t)gmul(s, 9) << 16 | (uint32_t)gmul(s, 13) << 8 | gmul(s, 11);
        Td1[i] = ror32(Td0[i], 8); Td2[i] = ror32(Td0[i], 16); Td3[i] = ror32(Td0[i], 24);
        uint8_t f = sbox[i]; /* the forward cipher's: (S.02, S, S, S.03) */
        Te0[i] = (uint32_t)gmul(f, 2) << 24 | (uint32_t)f << 16 | (uint32_t)f << 8 | gmul(f, 3);
        Te1[i] = ror32(Te0[i], 8); Te2[i] = ror32(Te0[i], 16); Te3[i] = ror32(Te0[i], 24);
    }
    tablesReady = 1;
}

static uint32_t be32(const uint8_t *b) { return (uint32_t)b[0] << 24 | (uint32_t)b[1] << 16 | (uint32_t)b[2] << 8 | b[3]; }
static void put_be32(uint8_t *b, uint32_t v) { b[0] = (uint8_t)(v >> 24); b[1] = (uint8_t)(v >> 16); b[2] = (uint8_t)(v >> 8); b[3] = (uint8_t)v; }
static uint32_t sub_word(uint32_t w) { return (uint32_t)sbox[w >> 24] << 24 | (uint32_t)sbox[(w >> 16) & 255] << 16 | (uint32_t)sbox[(w >> 8) & 255] << 8 | sbox[w & 255]; }

/* The round keys (the key expansion of FIPS-197). */
static void aes_round_keys(const uint8_t key[32], uint32_t rk[4 * (NR + 1)])
{
    make_tables();
    for (int i = 0; i < 8; i++) rk[i] = be32(key + 4 * i);
    uint8_t rcon = 1;
    for (int i = 8; i < 4 * (NR + 1); i++)
    {
        uint32_t t = rk[i - 1];
        if (i % 8 == 0) { t = sub_word((t << 8) | (t >> 24)) ^ ((uint32_t)rcon << 24); rcon = xtime(rcon); }
        else if (i % 8 == 4) t = sub_word(t);
        rk[i] = rk[i - 8] ^ t;
    }
}

/* The decryption round keys (the equivalent inverse cipher's: in reverse, the middle ones through InvMixColumns). */
static void aes_keys(const uint8_t key[32], uint32_t dk[4 * (NR + 1)])
{
    uint32_t rk[4 * (NR + 1)];
    aes_round_keys(key, rk);
    for (int r = 0; r <= NR; r++)
        for (int c = 0; c < 4; c++)
        {
            uint32_t w = rk[4 * (NR - r) + c];
            if (r > 0 && r < NR) w = Td0[sbox[w >> 24]] ^ Td1[sbox[(w >> 16) & 255]] ^ Td2[sbox[(w >> 8) & 255]] ^ Td3[sbox[w & 255]];
            dk[4 * r + c] = w;
        }
}

static void aes_decrypt_block(const uint32_t *dk, const uint8_t in[16], uint8_t out[16])
{
    uint32_t s0 = be32(in) ^ dk[0], s1 = be32(in + 4) ^ dk[1], s2 = be32(in + 8) ^ dk[2], s3 = be32(in + 12) ^ dk[3], t0, t1, t2, t3;
    for (int r = 1; r < NR; r++)
    {
        const uint32_t *k = dk + 4 * r;
        t0 = Td0[s0 >> 24] ^ Td1[(s3 >> 16) & 255] ^ Td2[(s2 >> 8) & 255] ^ Td3[s1 & 255] ^ k[0];
        t1 = Td0[s1 >> 24] ^ Td1[(s0 >> 16) & 255] ^ Td2[(s3 >> 8) & 255] ^ Td3[s2 & 255] ^ k[1];
        t2 = Td0[s2 >> 24] ^ Td1[(s1 >> 16) & 255] ^ Td2[(s0 >> 8) & 255] ^ Td3[s3 & 255] ^ k[2];
        t3 = Td0[s3 >> 24] ^ Td1[(s2 >> 16) & 255] ^ Td2[(s1 >> 8) & 255] ^ Td3[s0 & 255] ^ k[3];
        s0 = t0; s1 = t1; s2 = t2; s3 = t3;
    }
    const uint32_t *k = dk + 4 * NR;
    put_be32(out, ((uint32_t)inv[s0 >> 24] << 24 | (uint32_t)inv[(s3 >> 16) & 255] << 16 | (uint32_t)inv[(s2 >> 8) & 255] << 8 | inv[s1 & 255]) ^ k[0]);
    put_be32(out + 4, ((uint32_t)inv[s1 >> 24] << 24 | (uint32_t)inv[(s0 >> 16) & 255] << 16 | (uint32_t)inv[(s3 >> 8) & 255] << 8 | inv[s2 & 255]) ^ k[1]);
    put_be32(out + 8, ((uint32_t)inv[s2 >> 24] << 24 | (uint32_t)inv[(s1 >> 16) & 255] << 16 | (uint32_t)inv[(s0 >> 8) & 255] << 8 | inv[s3 & 255]) ^ k[2]);
    put_be32(out + 12, ((uint32_t)inv[s3 >> 24] << 24 | (uint32_t)inv[(s2 >> 16) & 255] << 16 | (uint32_t)inv[(s1 >> 8) & 255] << 8 | inv[s0 & 255]) ^ k[3]);
}

static void aes_encrypt_block(const uint32_t *rk, const uint8_t in[16], uint8_t out[16])
{
    uint32_t s0 = be32(in) ^ rk[0], s1 = be32(in + 4) ^ rk[1], s2 = be32(in + 8) ^ rk[2], s3 = be32(in + 12) ^ rk[3], t0, t1, t2, t3;
    for (int r = 1; r < NR; r++)
    {
        const uint32_t *k = rk + 4 * r;
        t0 = Te0[s0 >> 24] ^ Te1[(s1 >> 16) & 255] ^ Te2[(s2 >> 8) & 255] ^ Te3[s3 & 255] ^ k[0];
        t1 = Te0[s1 >> 24] ^ Te1[(s2 >> 16) & 255] ^ Te2[(s3 >> 8) & 255] ^ Te3[s0 & 255] ^ k[1];
        t2 = Te0[s2 >> 24] ^ Te1[(s3 >> 16) & 255] ^ Te2[(s0 >> 8) & 255] ^ Te3[s1 & 255] ^ k[2];
        t3 = Te0[s3 >> 24] ^ Te1[(s0 >> 16) & 255] ^ Te2[(s1 >> 8) & 255] ^ Te3[s2 & 255] ^ k[3];
        s0 = t0; s1 = t1; s2 = t2; s3 = t3;
    }
    const uint32_t *k = rk + 4 * NR;
    put_be32(out, ((uint32_t)sbox[s0 >> 24] << 24 | (uint32_t)sbox[(s1 >> 16) & 255] << 16 | (uint32_t)sbox[(s2 >> 8) & 255] << 8 | sbox[s3 & 255]) ^ k[0]);
    put_be32(out + 4, ((uint32_t)sbox[s1 >> 24] << 24 | (uint32_t)sbox[(s2 >> 16) & 255] << 16 | (uint32_t)sbox[(s3 >> 8) & 255] << 8 | sbox[s0 & 255]) ^ k[1]);
    put_be32(out + 8, ((uint32_t)sbox[s2 >> 24] << 24 | (uint32_t)sbox[(s3 >> 16) & 255] << 16 | (uint32_t)sbox[(s0 >> 8) & 255] << 8 | sbox[s1 & 255]) ^ k[2]);
    put_be32(out + 12, ((uint32_t)sbox[s3 >> 24] << 24 | (uint32_t)sbox[(s0 >> 16) & 255] << 16 | (uint32_t)sbox[(s1 >> 8) & 255] << 8 | sbox[s2 & 255]) ^ k[3]);
}

/* CBC: each block decrypted, then XORed with the cipher block before it (the IV for the first). */
static void cbc_decrypt(const uint32_t *dk, const uint8_t *iv, const uint8_t *cipher, uint8_t *plain, long blocks)
{
    for (long b = 0; b < blocks; b++)
    {
        const uint8_t *before = b == 0 ? iv : cipher + (b - 1) * 16;
        aes_decrypt_block(dk, cipher + b * 16, plain + b * 16);
        for (int i = 0; i < 16; i++) plain[b * 16 + i] ^= before[i];
    }
}

/* ---------------- Containers ---------------- */

static uint32_t le32(const unsigned char *p) { return (uint32_t)p[0] | (uint32_t)p[1] << 8 | (uint32_t)p[2] << 16 | (uint32_t)p[3] << 24; }

/* A Guid's bytes (ToByteArray) as .NET writes it as text: "00000000-0000-0000-0000-000000000000". */
static void guid_text(const unsigned char *g, char *out)
{
    sprintf(out, "%02x%02x%02x%02x-%02x%02x-%02x%02x-%02x%02x-%02x%02x%02x%02x%02x%02x",
            g[3], g[2], g[1], g[0], g[5], g[4], g[7], g[6], g[8], g[9], g[10], g[11], g[12], g[13], g[14], g[15]);
}

/* The container's plain bytes (malloc'd, a 0 after them), or NULL if it isn't one or is damaged. */
unsigned char *pack_decrypt(const unsigned char *data, long size, long *plainLength)
{
    if (size < 8 + 16 + 4 || memcmp(data, "SRPG", 4) || le32(data + 4) != 1) return NULL;
    const unsigned char *id = data + 8;
    uint32_t ivLength = le32(data + 24);
    if (ivLength != 16 || 28 + 16 + 4 > size) return NULL;
    const unsigned char *iv = data + 28;
    uint32_t cipherLength = le32(data + 44);
    if (cipherLength == 0 || cipherLength % 16 || 48 + (long)cipherLength > size) return NULL;
    char text[80];
    strcpy(text, "SandcastleRpgMaker.ProjectKey.v1:");
    guid_text(id, text + strlen(text));
    unsigned char key[32];
    sha256((const unsigned char *)text, strlen(text), key);
    unsigned char *plain = (unsigned char *)malloc(cipherLength + 1);
    if (!plain) return NULL;
    uint32_t dk[4 * (NR + 1)];
    aes_keys(key, dk);
    cbc_decrypt(dk, iv, data + 48, plain, (long)cipherLength / 16);
    unsigned pad = plain[cipherLength - 1];
    if (pad < 1 || pad > 16) { free(plain); return NULL; } /* the wrong key, or damaged */
    for (unsigned i = 0; i < pad; i++) if (plain[cipherLength - 1 - i] != pad) { free(plain); return NULL; }
    long n = (long)cipherLength - pad;
    plain[n] = 0;
    if (plainLength) *plainLength = n;
    return plain;
}

char *read_whole_file(const char *path, long *length);

static unsigned char projectId[16]; static int haveProjectId;

unsigned char *pack_read(const char *path, long *plainLength)
{
    long size;
    unsigned char *data = (unsigned char *)read_whole_file(path, &size);
    if (!data) return NULL;
    if (size >= 24 && !memcmp(data, "SRPG", 4) && !haveProjectId) { memcpy(projectId, data + 8, 16); haveProjectId = 1; } /* the game's id: saves are encrypted with it */
    unsigned char *plain = pack_decrypt(data, size, plainLength);
    free(data);
    return plain;
}

/* ---------------- Archives ---------------- */

struct Archive { char path[1100]; long dataStart; int count; char (*names)[64]; long *offsets, *lengths; cJSON *index; };

Archive *pack_archive_open(const char *path)
{
    FILE *f = fopen(path, "rb");
    if (!f) return NULL;
    unsigned char head[12];
    if (fread(head, 1, 12, f) != 12 || memcmp(head, "SRPK", 4)) { fclose(f); return NULL; }
    long indexLength = (long)le32(head + 8);
    unsigned char *raw = (unsigned char *)malloc((size_t)indexLength);
    if (!raw || fread(raw, 1, (size_t)indexLength, f) != (size_t)indexLength) { free(raw); fclose(f); return NULL; }
    fclose(f);
    unsigned char *plain = pack_decrypt(raw, indexLength, NULL);
    free(raw);
    if (!plain) return NULL;
    cJSON *index = cJSON_Parse((const char *)plain);
    free(plain);
    if (!index) return NULL;
    Archive *a = (Archive *)calloc(1, sizeof(Archive));
    snprintf(a->path, sizeof a->path, "%s", path);
    a->dataStart = 12 + indexLength;
    a->index = index;
    cJSON *entries = cJSON_GetObjectItem(index, "Entries"), *e;
    int n = cJSON_GetArraySize(entries);
    a->names = (char (*)[64])calloc((size_t)(n + 1), 64);
    a->offsets = (long *)calloc((size_t)(n + 1), sizeof(long));
    a->lengths = (long *)calloc((size_t)(n + 1), sizeof(long));
    cJSON_ArrayForEach(e, entries)
    {
        snprintf(a->names[a->count], 64, "%s", cJSON_GetStringValue(cJSON_GetObjectItem(e, "Name")));
        a->offsets[a->count] = (long)cJSON_GetObjectItem(e, "Offset")->valuedouble;
        a->lengths[a->count] = (long)cJSON_GetObjectItem(e, "Length")->valuedouble;
        a->count++;
    }
    return a;
}

void *pack_archive_index(Archive *a) { return a ? a->index : NULL; }

unsigned char *pack_archive_read(Archive *a, const char *name, long *plainLength)
{
    if (!a) return NULL;
    for (int i = 0; i < a->count; i++)
    {
        if (strcmp(a->names[i], name)) continue;
        FILE *f = fopen(a->path, "rb");
        if (!f) return NULL;
        unsigned char *raw = (unsigned char *)malloc((size_t)a->lengths[i]);
        int ok = raw && fseek(f, a->dataStart + a->offsets[i], SEEK_SET) == 0 && fread(raw, 1, (size_t)a->lengths[i], f) == (size_t)a->lengths[i];
        fclose(f);
        unsigned char *plain = ok ? pack_decrypt(raw, a->lengths[i], plainLength) : NULL;
        free(raw);
        return plain;
    }
    return NULL;
}

/* ---------------- Streams: decrypted as they're read ---------------- */

struct PackStream { unsigned char *raw; const unsigned char *iv, *cipher; long plainLength; uint32_t dk[4 * (NR + 1)]; };

/* Read from the archive as it is (still encrypted); the key made once; the last block decrypted for its padding (the true length). */
PackStream *pack_archive_stream(Archive *a, const char *name)
{
    if (!a) return NULL;
    for (int i = 0; i < a->count; i++)
    {
        if (strcmp(a->names[i], name)) continue;
        long size = a->lengths[i];
        FILE *f = fopen(a->path, "rb");
        if (!f) return NULL;
        unsigned char *raw = (unsigned char *)malloc((size_t)size);
        int ok = raw && fseek(f, a->dataStart + a->offsets[i], SEEK_SET) == 0 && fread(raw, 1, (size_t)size, f) == (size_t)size;
        fclose(f);
        if (!ok || size < 48 || memcmp(raw, "SRPG", 4) || le32(raw + 4) != 1 || le32(raw + 24) != 16) { free(raw); return NULL; }
        uint32_t cipherLength = le32(raw + 44);
        if (cipherLength == 0 || cipherLength % 16 || 48 + (long)cipherLength > size) { free(raw); return NULL; }
        PackStream *s = (PackStream *)calloc(1, sizeof(PackStream));
        s->raw = raw; s->iv = raw + 28; s->cipher = raw + 48;
        char text[80];
        strcpy(text, "SandcastleRpgMaker.ProjectKey.v1:");
        guid_text(raw + 8, text + strlen(text));
        unsigned char key[32];
        sha256((const unsigned char *)text, strlen(text), key);
        aes_keys(key, s->dk);
        long blocks = (long)cipherLength / 16;
        unsigned char last[16];
        cbc_decrypt(s->dk, blocks == 1 ? s->iv : s->cipher + (blocks - 2) * 16, s->cipher + (blocks - 1) * 16, last, 1);
        unsigned pad = last[15];
        if (pad < 1 || pad > 16) { free(raw); free(s); return NULL; } /* the wrong key, or damaged */
        s->plainLength = (long)cipherLength - pad;
        return s;
    }
    return NULL;
}

long pack_stream_length(const PackStream *s) { return s ? s->plainLength : 0; }

/* Plain bytes from an offset: only the blocks they lie in are decrypted. Reads nothing past the end. Safe from two tasks at once (nothing in it changes). */
long pack_stream_read(const PackStream *s, long offset, void *dst, long n)
{
    if (!s || offset < 0 || offset >= s->plainLength || n <= 0) return 0;
    if (offset + n > s->plainLength) n = s->plainLength - offset;
    unsigned char *out = (unsigned char *)dst, block[16];
    long done = 0;
    while (done < n)
    {
        long at = offset + done, b = at / 16, in = at % 16;
        long whole = (n - done - (in ? 16 - in : 0)) / 16; /* full blocks straight into the destination */
        if (!in && whole > 0)
        {
            cbc_decrypt(s->dk, b == 0 ? s->iv : s->cipher + (b - 1) * 16, s->cipher + b * 16, out + done, whole);
            done += whole * 16;
            continue;
        }
        cbc_decrypt(s->dk, b == 0 ? s->iv : s->cipher + (b - 1) * 16, s->cipher + b * 16, block, 1);
        long take = 16 - in < n - done ? 16 - in : n - done;
        memcpy(out + done, block + in, (size_t)take);
        done += take;
    }
    return n;
}

void pack_stream_close(PackStream *s) { if (s) { free(s->raw); free(s); } }

/* ---------------- Writing: a save, encrypted as the game's own files are ---------------- */

/* ProjectContainer.Save: "SRPG", version 1, the game's id, a fresh IV, AES-256-CBC (PKCS7) with the game's key. malloc'd; NULL without the game's id. */
unsigned char *pack_encrypt(const unsigned char *plain, long length, long *size)
{
    if (!haveProjectId) return NULL;
    long blocks = length / 16 + 1, cipherLength = blocks * 16;
    unsigned char *out = (unsigned char *)malloc((size_t)(48 + cipherLength));
    if (!out) return NULL;
    memcpy(out, "SRPG", 4);
    out[4] = 1; out[5] = out[6] = out[7] = 0;
    memcpy(out + 8, projectId, 16);
    out[24] = 16; out[25] = out[26] = out[27] = 0;
    /* the IV: different for every save (the clock, a counter, where the buffer is) */
    static uint32_t counter = 0x9E3779B9u;
    uint32_t mix = (uint32_t)time(NULL) ^ (uint32_t)clock() ^ (uint32_t)(uintptr_t)out ^ (counter += 0x6D2B79F5u);
    for (int i = 0; i < 16; i++) { mix ^= mix << 13; mix ^= mix >> 17; mix ^= mix << 5; out[28 + i] = (unsigned char)(mix >> 24); }
    out[44] = (unsigned char)cipherLength; out[45] = (unsigned char)(cipherLength >> 8); out[46] = (unsigned char)(cipherLength >> 16); out[47] = (unsigned char)(cipherLength >> 24);
    char text[80];
    strcpy(text, "SandcastleRpgMaker.ProjectKey.v1:");
    guid_text(projectId, text + strlen(text));
    unsigned char key[32];
    sha256((const unsigned char *)text, strlen(text), key);
    uint32_t rk[4 * (NR + 1)];
    aes_round_keys(key, rk);
    const unsigned char *before = out + 28;
    unsigned char block[16];
    unsigned pad = (unsigned)(cipherLength - length);
    for (long b = 0; b < blocks; b++)
    {
        for (int i = 0; i < 16; i++)
        {
            long at = b * 16 + i;
            block[i] = (unsigned char)((at < length ? plain[at] : (unsigned char)pad) ^ before[i]);
        }
        aes_encrypt_block(rk, block, out + 48 + b * 16);
        before = out + 48 + b * 16;
    }
    *size = 48 + cipherLength;
    return out;
}
