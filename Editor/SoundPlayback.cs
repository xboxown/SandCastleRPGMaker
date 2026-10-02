using System.IO;
using NAudio.Wave;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Plays a real ResourceEntry by Id, wherever that need comes from - Play
/// Sound event commands, and now per-frame sprite sound too, wherever that
/// sprite happens to be displayed (a book icon, a paper doll, sitting on a
/// table, the Sprite Editor's own Play button - SpriteAnimator drives all
/// of these from one place, so wiring sound in there once covers every one
/// of them automatically). Amiga IFF/8SVX sources get the same throwaway-
/// temp-WAV bridge the Sound Editor itself uses for playback, since
/// nothing else here can decode that format directly.
/// </summary>
internal static class SoundPlayback
{
    /// <summary>Why the most recent TryPlay failed (the real exception message), so a failure can be reported instead of silently swallowed.</summary>
    public static string? LastError { get; private set; }

    public static bool TryPlay(ResourceDatabase resources, int resourceId, double? volumeOverride = null)
    {
        var entry = resources.Entries.FirstOrDefault(e => e.Id == resourceId);
        if (entry is null) { LastError = $"sound #{resourceId} is not in the Sound list"; return false; }
        if (!File.Exists(entry.FilePath)) { LastError = $"file not found: {entry.FilePath}"; return false; }
        return TryPlay(entry, volumeOverride ?? entry.Volume);
    }

    public static bool TryPlay(ResourceEntry entry, double volume)
    {
        try
        {
            string playbackPath = entry.FilePath;
            string ext = Path.GetExtension(entry.FilePath).ToLowerInvariant();
            if (ext is ".iff" or ".8svx")
            {
                playbackPath = Path.Combine(Path.GetTempPath(), $"sandcastle_soundplayback_{Guid.NewGuid():N}.wav");
                AudioConversionEngine.ConvertAndExport(entry.FilePath, playbackPath, "WAV", 44100, 16, entry.ChannelFormat);
            }

            var reader = new AudioFileReader(playbackPath) { Volume = (float)Math.Clamp(volume, 0, 1) };
            var waveOut = new WaveOutEvent();
            waveOut.Init(reader);
            waveOut.PlaybackStopped += (_, _) =>
            {
                waveOut.Dispose();
                reader.Dispose();
            };
            waveOut.Play();
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }
}
