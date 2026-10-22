/*
 * Videos on Windows: the machine's own decoder (Media Foundation's Source
 * Reader), as the Windows engine's MediaElement uses - nothing about codecs
 * ships with a game. Frames come out as 32-bit RGB, sound as 16-bit PCM.
 */
#define COBJMACROS
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mfreadwrite.h>
#include <mferror.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
#include "platform.h"
#include <stdio.h>
void game_log(const char *line);

static IMFSourceReader *reader;
static int started, frameW, frameH, stride, rate, channels, videoEnded, audioEnded, hasAudio;

#define VIDEO_STREAM ((DWORD)MF_SOURCE_READER_FIRST_VIDEO_STREAM)
#define AUDIO_STREAM ((DWORD)MF_SOURCE_READER_FIRST_AUDIO_STREAM)

int platform_video_open(const char *path, int *width, int *height)
{
    platform_video_close();
    if (!started)
    {
        if (FAILED(CoInitializeEx(NULL, COINIT_MULTITHREADED)) && FAILED(CoInitializeEx(NULL, COINIT_APARTMENTTHREADED))) { /* already set up another way: fine */ }
        if (FAILED(MFStartup(MF_VERSION, MFSTARTUP_FULL))) return 0;
        started = 1;
    }
    wchar_t wide[1024];
    if (!MultiByteToWideChar(CP_UTF8, 0, path, -1, wide, 1024)) return 0;
    IMFAttributes *attrs = NULL;
    MFCreateAttributes(&attrs, 1);
    if (attrs) IMFAttributes_SetUINT32(attrs, &MF_SOURCE_READER_ENABLE_VIDEO_PROCESSING, TRUE); /* YUV to RGB on the way out */
    HRESULT hr = MFCreateSourceReaderFromURL(wide, attrs, &reader);
    if (attrs) IMFAttributes_Release(attrs);
    if (FAILED(hr)) { reader = NULL; return 0; }
    /* video: RGB32 */
    IMFMediaType *type = NULL;
    MFCreateMediaType(&type);
    IMFMediaType_SetGUID(type, &MF_MT_MAJOR_TYPE, &MFMediaType_Video);
    IMFMediaType_SetGUID(type, &MF_MT_SUBTYPE, &MFVideoFormat_RGB32);
    hr = IMFSourceReader_SetCurrentMediaType(reader, VIDEO_STREAM, NULL, type);
    IMFMediaType_Release(type);
    if (FAILED(hr)) { platform_video_close(); return 0; }
    IMFMediaType *now = NULL;
    if (FAILED(IMFSourceReader_GetCurrentMediaType(reader, VIDEO_STREAM, &now))) { platform_video_close(); return 0; }
    UINT64 size = 0;
    IMFMediaType_GetUINT64(now, &MF_MT_FRAME_SIZE, &size);
    frameW = (int)(size >> 32); frameH = (int)(size & 0xFFFFFFFF);
    UINT32 s = 0;
    stride = SUCCEEDED(IMFMediaType_GetUINT32(now, &MF_MT_DEFAULT_STRIDE, &s)) ? (int)(INT32)s : frameW * 4;
    IMFMediaType_Release(now);
    if (frameW <= 0 || frameH <= 0) { platform_video_close(); return 0; }
    /* sound: 16-bit PCM at its own rate */
    hasAudio = 0;
    MFCreateMediaType(&type);
    IMFMediaType_SetGUID(type, &MF_MT_MAJOR_TYPE, &MFMediaType_Audio);
    IMFMediaType_SetGUID(type, &MF_MT_SUBTYPE, &MFAudioFormat_PCM);
    IMFMediaType_SetUINT32(type, &MF_MT_AUDIO_BITS_PER_SAMPLE, 16);
    if (SUCCEEDED(IMFSourceReader_SetCurrentMediaType(reader, AUDIO_STREAM, NULL, type)) && SUCCEEDED(IMFSourceReader_GetCurrentMediaType(reader, AUDIO_STREAM, &now)))
    {
        UINT32 r = 0, c = 0;
        IMFMediaType_GetUINT32(now, &MF_MT_AUDIO_SAMPLES_PER_SECOND, &r);
        IMFMediaType_GetUINT32(now, &MF_MT_AUDIO_NUM_CHANNELS, &c);
        rate = (int)r; channels = (int)c;
        hasAudio = rate > 0 && channels > 0;
        IMFMediaType_Release(now);
    }
    IMFMediaType_Release(type);
    if (!hasAudio) IMFSourceReader_SetStreamSelection(reader, AUDIO_STREAM, FALSE);
    videoEnded = 0; audioEnded = !hasAudio;
    *width = frameW; *height = frameH;
    return 1;
}

/* The next frame (frameW x frameH, 0xAARRGGBB) and when it's shown; 0 at the end. */
int platform_video_next(uint32_t *frame, double *seconds)
{
    if (!reader || videoEnded) return 0;
    for (;;)
    {
        DWORD index = 0, flags = 0;
        LONGLONG stamp = 0;
        IMFSample *sample = NULL;
        if (FAILED(IMFSourceReader_ReadSample(reader, VIDEO_STREAM, 0, &index, &flags, &stamp, &sample))) { videoEnded = 1; return 0; }
        if (flags & MF_SOURCE_READERF_ENDOFSTREAM) { if (sample) IMFSample_Release(sample); videoEnded = 1; return 0; }
        if (!sample) continue; /* a gap: the next one */
        IMFMediaBuffer *buffer = NULL;
        if (SUCCEEDED(IMFSample_GetBufferByIndex(sample, 0, &buffer)))
        {
            /* its rows as they really lie (a 2D buffer knows its pitch; padded widths and bottom-up frames included) */
            IMF2DBuffer *flat = NULL;
            BYTE *scan0 = NULL;
            LONG pitch = 0;
            if (SUCCEEDED(IMFMediaBuffer_QueryInterface(buffer, &IID_IMF2DBuffer, (void **)&flat)) && SUCCEEDED(IMF2DBuffer_Lock2D(flat, &scan0, &pitch)))
            {
                for (int y = 0; y < frameH; y++)
                {
                    const uint32_t *px = (const uint32_t *)(scan0 + (LONG_PTR)y * pitch);
                    for (int x = 0; x < frameW; x++) frame[y * frameW + x] = px[x] | 0xFF000000u;
                }
                IMF2DBuffer_Unlock2D(flat);
            }
            else
            {
                BYTE *data = NULL;
                DWORD length = 0;
                if (SUCCEEDED(IMFMediaBuffer_Lock(buffer, &data, NULL, &length)))
                {
                    /* the rows as they lie: decoders pad a frame to whole 16 (or 32) pixel blocks, which the stride they report may not say */
                    int rowBytes = stride < 0 ? -stride : stride, heights[3] = { frameH, (frameH + 15) & ~15, (frameH + 31) & ~31 };
                    if (rowBytes < frameW * 4) rowBytes = frameW * 4;
                    if ((DWORD)rowBytes * (DWORD)frameH != length)
                        for (int k = 0; k < 3; k++)
                            if (length % (DWORD)heights[k] == 0 && length / (DWORD)heights[k] >= (DWORD)frameW * 4 && (length / (DWORD)heights[k]) % 4 == 0) { rowBytes = (int)(length / (DWORD)heights[k]); break; }
                    for (int y = 0; y < frameH; y++)
                    {
                        const BYTE *row = stride < 0 ? data + (size_t)(frameH - 1 - y) * rowBytes : data + (size_t)y * rowBytes;
                        if ((size_t)(row - data) + (size_t)frameW * 4 > length) break;
                        const uint32_t *px = (const uint32_t *)row;
                        for (int x = 0; x < frameW; x++) frame[y * frameW + x] = px[x] | 0xFF000000u;
                    }
                    IMFMediaBuffer_Unlock(buffer);
                }
            }
            if (flat) IMF2DBuffer_Release(flat);
            IMFMediaBuffer_Release(buffer);
        }
        IMFSample_Release(sample);
        *seconds = stamp / 10000000.0;
        return 1;
    }
}

/* The sound decoded next: up to max frames (interleaved 16-bit), its rate and channels; 0 when there's no more. */
int platform_video_audio(int16_t *out, int max, int *outRate, int *outChannels)
{
    if (!reader || audioEnded) return 0;
    DWORD index = 0, flags = 0;
    LONGLONG stamp = 0;
    IMFSample *sample = NULL;
    if (FAILED(IMFSourceReader_ReadSample(reader, AUDIO_STREAM, 0, &index, &flags, &stamp, &sample))) { audioEnded = 1; return 0; }
    if (flags & MF_SOURCE_READERF_ENDOFSTREAM) audioEnded = 1;
    if (!sample) return 0;
    int frames = 0;
    IMFMediaBuffer *buffer = NULL;
    if (SUCCEEDED(IMFSample_ConvertToContiguousBuffer(sample, &buffer)))
    {
        BYTE *data = NULL;
        DWORD length = 0;
        if (SUCCEEDED(IMFMediaBuffer_Lock(buffer, &data, NULL, &length)))
        {
            frames = (int)(length / 2 / (DWORD)channels);
            if (frames > max) frames = max;
            memcpy(out, data, (size_t)frames * (size_t)channels * 2);
            IMFMediaBuffer_Unlock(buffer);
        }
        IMFMediaBuffer_Release(buffer);
    }
    IMFSample_Release(sample);
    *outRate = rate; *outChannels = channels;
    return frames;
}

int platform_video_audio_ended(void) { return audioEnded; }

void platform_video_close(void)
{
    if (reader) { IMFSourceReader_Release(reader); reader = NULL; }
    videoEnded = audioEnded = 1;
}
