/* The game's encrypted files (ProjectContainer's format) and archives of them. */
#ifndef SANDCASTLE_PACK_H
#define SANDCASTLE_PACK_H

#include <stdint.h>

typedef struct Archive Archive;

/* A container's plain bytes (malloc'd, followed by a 0); NULL if it can't be read. */
unsigned char *pack_decrypt(const unsigned char *data, long size, long *plainLength);
unsigned char *pack_read(const char *path, long *plainLength);

Archive *pack_archive_open(const char *path);
void *pack_archive_index(Archive *archive);                 /* its index (a cJSON object) */
unsigned char *pack_archive_read(Archive *archive, const char *name, long *plainLength);

/* A file of the archive read as it plays: kept encrypted, decrypted a block at a time as it's read. */
typedef struct PackStream PackStream;
PackStream *pack_archive_stream(Archive *archive, const char *name);
long pack_stream_length(const PackStream *stream);
long pack_stream_read(const PackStream *stream, long offset, void *dst, long n);
void pack_stream_close(PackStream *stream);

#endif
