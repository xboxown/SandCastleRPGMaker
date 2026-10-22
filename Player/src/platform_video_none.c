/*
 * Videos where the machine has no decoder the game may use (the Amiga,
 * Linux, Mac for now): Play Video can't play one, so it's skipped and the
 * event goes on - as the Windows engine does with a video the PC can't play.
 */
#include <stdint.h>
#include "platform.h"

int platform_video_open(const char *path, int *width, int *height) { (void)path; *width = *height = 0; return 0; }
int platform_video_next(uint32_t *frame, double *seconds) { (void)frame; (void)seconds; return 0; }
int platform_video_audio(int16_t *out, int max, int *rate, int *channels) { (void)out; (void)max; *rate = *channels = 0; return 0; }
int platform_video_audio_ended(void) { return 1; }
void platform_video_close(void) { }
