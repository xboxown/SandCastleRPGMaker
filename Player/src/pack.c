/*
 * The game's files, encrypted as the Windows game's are (ProjectContainer):
 * "SRPG", the format version, the project's id (a Guid's 16 bytes), the IV,
 * then the AES-256-CBC cipher text (PKCS7). The key is
 * SHA-256("SandcastleRpgMaker.ProjectKey.v1:" + the id as text), the id read
 * from the file itself, as the Windows game reads its own. pictures.sdbx and
 * sounds.sdbx are archives (SRPK): an encrypted index, then each file in a
 * container of its own - only what's shown or played is decrypted.
 */
#include "pack.h"
#include "../third_party/aes.h"
#include "../third_party/cJSON.h"
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

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
    memcpy(plain, data + 48, cipherLength);
    struct AES_ctx ctx;
    AES_init_ctx_iv(&ctx, key, iv);
    AES_CBC_decrypt_buffer(&ctx, plain, cipherLength);
    unsigned pad = plain[cipherLength - 1];
    if (pad < 1 || pad > 16) { free(plain); return NULL; } /* the wrong key, or damaged */
    for (unsigned i = 0; i < pad; i++) if (plain[cipherLength - 1 - i] != pad) { free(plain); return NULL; }
    long n = (long)cipherLength - pad;
    plain[n] = 0;
    if (plainLength) *plainLength = n;
    return plain;
}

char *read_whole_file(const char *path, long *length);

unsigned char *pack_read(const char *path, long *plainLength)
{
    long size;
    unsigned char *data = (unsigned char *)read_whole_file(path, &size);
    if (!data) return NULL;
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
