/*
 * Sound out on the Amiga 68k: AHI (ahi.device: any sound card, or Paula
 * through AHI's own drivers) when it's there, else Paula itself through
 * audio.device - two channels (left, right), 8-bit from chip memory. A sound
 * task of its own keeps two buffers going (AHI's linked writes; queued
 * writes on Paula) and asks the mixer for each one in turn; the mixer and
 * the game share their state under a semaphore. It runs at the game's own
 * priority and steps aside when it falls behind, so it can never take the
 * whole machine (above Workbench it froze the Amiga when mixing was slow).
 */
#include "platform.h"
#include <exec/exec.h>
#include <dos/dostags.h>
#include <devices/ahi.h>
#include <devices/audio.h>
#include <proto/exec.h>
#include <proto/dos.h>
#include <string.h>

#define AMIGA_RATE 22050           /* half CD rate: the 68060 / 68080 keeps the game's speed */
#define FRAMES 1024                /* each buffer: ~46 ms */
#define PAL_CLOCK 3546895

static AudioFill audioFill;
static struct SignalSemaphore lock;
static volatile int running, quit, started;
static struct Task *mainTask;
static int16_t mixed[FRAMES * 2];
/* The sound task's stack: the MP3 decoder takes some 17 KB a frame and the mixer 8 KB more - 16 KB overflowed it and froze the Amiga (nothing guards a stack there). */

void platform_audio_lock(void) { if (running) ObtainSemaphore(&lock); }
void platform_audio_unlock(void) { if (running) ReleaseSemaphore(&lock); }

static void mix_into(void)
{
    ObtainSemaphore(&lock);
    audioFill(mixed, FRAMES);
    ReleaseSemaphore(&lock);
}

/* ---- AHI: two requests, each linked to the one before (seamless) ---- */
static int run_ahi(void)
{
    struct MsgPort *port = CreateMsgPort();
    if (!port) return 0;
    struct AHIRequest *req[2] = { (struct AHIRequest *)CreateIORequest(port, sizeof(struct AHIRequest)), NULL };
    if (!req[0]) { DeleteMsgPort(port); return 0; }
    req[0]->ahir_Version = 4;
    if (OpenDevice((CONST_STRPTR)AHINAME, AHI_DEFAULT_UNIT, (struct IORequest *)req[0], 0) != 0) { DeleteIORequest((struct IORequest *)req[0]); DeleteMsgPort(port); return 0; }
    req[1] = (struct AHIRequest *)AllocVec(sizeof(struct AHIRequest), MEMF_PUBLIC);
    if (!req[1]) { CloseDevice((struct IORequest *)req[0]); DeleteIORequest((struct IORequest *)req[0]); DeleteMsgPort(port); return 0; }
    CopyMem(req[0], req[1], sizeof(struct AHIRequest));
    int16_t *buffer[2] = { (int16_t *)AllocVec(FRAMES * 4, MEMF_PUBLIC), (int16_t *)AllocVec(FRAMES * 4, MEMF_PUBLIC) };
    started = 1;
    Signal(mainTask, SIGBREAKF_CTRL_F);
    struct AHIRequest *link = NULL;
    int cur = 0;
    while (!quit && buffer[0] && buffer[1])
    {
        mix_into();
        memcpy(buffer[cur], mixed, FRAMES * 4);
        struct AHIRequest *r = req[cur];
        r->ahir_Std.io_Message.mn_Node.ln_Pri = 0;
        r->ahir_Std.io_Command = CMD_WRITE;
        r->ahir_Std.io_Data = buffer[cur];
        r->ahir_Std.io_Length = FRAMES * 4;
        r->ahir_Std.io_Offset = 0;
        r->ahir_Frequency = AMIGA_RATE;
        r->ahir_Type = AHIST_S16S;    /* 16-bit stereo, big-endian: as the mixer makes it on the 68k */
        r->ahir_Volume = 0x10000;
        r->ahir_Position = 0x8000;
        r->ahir_Link = link;
        SendIO((struct IORequest *)r);
        if (link)
        {
            if (CheckIO((struct IORequest *)link)) Delay(1); /* behind: it had played already - let the rest of the machine run */
            WaitIO((struct IORequest *)link); /* the one before has played: this one follows on */
        }
        link = r;
        cur ^= 1;
    }
    if (link) { AbortIO((struct IORequest *)link); WaitIO((struct IORequest *)link); }
    FreeVec(buffer[0]); FreeVec(buffer[1]);
    CloseDevice((struct IORequest *)req[0]);
    FreeVec(req[1]);
    DeleteIORequest((struct IORequest *)req[0]);
    DeleteMsgPort(port);
    return 1;
}

/* ---- Paula through audio.device: a left and a right channel, two buffers each queued ---- */
static int run_paula(void)
{
    static UBYTE channels[] = { 3, 5, 10, 12 }; /* a left and a right channel, any pair */
    struct MsgPort *port = CreateMsgPort();
    if (!port) return 0;
    struct IOAudio *alloc = (struct IOAudio *)CreateIORequest(port, sizeof(struct IOAudio));
    if (!alloc) { DeleteMsgPort(port); return 0; }
    alloc->ioa_Request.io_Message.mn_Node.ln_Pri = 127;
    alloc->ioa_Data = channels;
    alloc->ioa_Length = sizeof channels;
    if (OpenDevice((CONST_STRPTR)AUDIONAME, 0, (struct IORequest *)alloc, 0) != 0) { DeleteIORequest((struct IORequest *)alloc); DeleteMsgPort(port); return 0; }
    ULONG unit = (ULONG)alloc->ioa_Request.io_Unit, left = unit & 9, right = unit & 6;
    BYTE *chip[2][2];
    struct IOAudio *io[2][2];
    for (int c = 0; c < 2; c++)
        for (int b = 0; b < 2; b++)
        {
            chip[c][b] = (BYTE *)AllocVec(FRAMES, MEMF_CHIP | MEMF_CLEAR);
            io[c][b] = (struct IOAudio *)AllocVec(sizeof(struct IOAudio), MEMF_PUBLIC);
            if (io[c][b]) CopyMem(alloc, io[c][b], sizeof(struct IOAudio));
        }
    started = 1;
    Signal(mainTask, SIGBREAKF_CTRL_F);
    int sent[2] = { 0, 0 }, cur = 0;
    UWORD period = (UWORD)(PAL_CLOCK / AMIGA_RATE);
    while (!quit)
    {
        if (sent[cur])
        {
            if (CheckIO((struct IORequest *)io[0][cur])) Delay(1); /* behind: let the rest of the machine run */
            WaitIO((struct IORequest *)io[0][cur]); WaitIO((struct IORequest *)io[1][cur]);
        }
        mix_into();
        for (int i = 0; i < FRAMES; i++) { chip[0][cur][i] = (BYTE)(mixed[i * 2] >> 8); chip[1][cur][i] = (BYTE)(mixed[i * 2 + 1] >> 8); }
        for (int c = 0; c < 2; c++)
        {
            struct IOAudio *a = io[c][cur];
            a->ioa_Request.io_Command = CMD_WRITE;
            a->ioa_Request.io_Flags = ADIOF_PERVOL;
            a->ioa_Request.io_Unit = (struct Unit *)(c == 0 ? left : right);
            a->ioa_Data = (UBYTE *)chip[c][cur];
            a->ioa_Length = FRAMES;
            a->ioa_Period = period;
            a->ioa_Volume = 64;
            a->ioa_Cycles = 1;
            BeginIO((struct IORequest *)a);
        }
        sent[cur] = 1;
        cur ^= 1;
    }
    for (int b = 0; b < 2; b++)
        if (sent[b]) for (int c = 0; c < 2; c++) { AbortIO((struct IORequest *)io[c][b]); WaitIO((struct IORequest *)io[c][b]); }
    for (int c = 0; c < 2; c++) for (int b = 0; b < 2; b++) { FreeVec(chip[c][b]); FreeVec(io[c][b]); }
    CloseDevice((struct IORequest *)alloc);
    DeleteIORequest((struct IORequest *)alloc);
    DeleteMsgPort(port);
    return 1;
}

static void sound_task(void)
{
    if (!run_ahi() && !run_paula()) { started = -1; Signal(mainTask, SIGBREAKF_CTRL_F); }
    Forbid();
    running = 0;
    Signal(mainTask, SIGBREAKF_CTRL_F);
}

int platform_audio_open(int rate, AudioFill fill)
{
    (void)rate;
    audioFill = fill;
    InitSemaphore(&lock);
    mainTask = FindTask(NULL);
    SetSignal(0, SIGBREAKF_CTRL_F);
    running = 1; quit = 0; started = 0;
    struct Process *p = CreateNewProcTags(NP_Entry, (ULONG)sound_task, NP_Name, (ULONG)"Sandcastle sound", NP_Priority, 0, NP_StackSize, 131072, TAG_DONE);
    if (!p) { running = 0; return 0; }
    while (!started) Wait(SIGBREAKF_CTRL_F);
    if (started < 0) { while (running) Wait(SIGBREAKF_CTRL_F); return 0; }
    return AMIGA_RATE;
}

void platform_audio_close(void)
{
    if (!running) return;
    quit = 1;
    while (running) Wait(SIGBREAKF_CTRL_F);
}
