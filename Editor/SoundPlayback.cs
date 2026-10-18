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

    /// <summary>Everything playing right now, so it can all be stopped at once (closing Game Test stops its music and sounds).</summary>
    private static readonly HashSet<WaveOutEvent> Playing = new();
    /// <summary>Sounds that belong to something (a paperdoll's Play Sound), so that thing can stop them; and the ones looping until then.</summary>
    private static readonly Dictionary<WaveOutEvent, object> Owners = new();
    private static readonly HashSet<WaveOutEvent> Looping = new();
    /// <summary>Which Sound list entry each one is playing, so Stop Sound can stop a sound by name.</summary>
    private static readonly Dictionary<WaveOutEvent, int> SoundIds = new();

    /// <summary>Stop Sound: stops every copy of this sound that is playing (looping ones too).</summary>
    public static void StopSound(int resourceId)
    {
        List<WaveOutEvent> matching;
        lock (Playing)
        {
            matching = SoundIds.Where(kv => kv.Value == resourceId).Select(kv => kv.Key).ToList();
            foreach (var waveOut in matching) Looping.Remove(waveOut);
        }
        foreach (var waveOut in matching)
        {
            try { waveOut.Stop(); } catch { /* already finishing */ }
        }
    }

    /// <summary>Plays a sound that belongs to <paramref name="owner"/> (it plays on wherever its owner's window is), looping until StopFor if asked.</summary>
    public static bool TryPlayFor(object owner, ResourceDatabase resources, int resourceId, bool loop)
    {
        var entry = resources.Entries.FirstOrDefault(e => e.Id == resourceId);
        if (entry is null) { LastError = $"sound #{resourceId} is not in the Sound list"; return false; }
        if (!File.Exists(entry.FilePath)) { LastError = $"file not found: {entry.FilePath}"; return false; }
        return Play(entry, entry.Volume, owner, loop);
    }

    /// <summary>A sound (its Sound list id) started, and one finished or was stopped - for the game's "Sound played / stopped" conditions.</summary>
    public static event Action<int>? Played;
    public static event Action<int>? Stopped;

    /// <summary>Whether anything that belongs to <paramref name="owner"/> is still playing.</summary>
    public static bool IsPlayingFor(object owner)
    {
        lock (Playing) return Owners.Values.Any(o => ReferenceEquals(o, owner));
    }

    /// <summary>Stops every sound that belongs to <paramref name="owner"/> (a Stop button).</summary>
    public static void StopFor(object owner)
    {
        List<WaveOutEvent> owned;
        lock (Playing)
        {
            owned = Owners.Where(kv => ReferenceEquals(kv.Value, owner)).Select(kv => kv.Key).ToList();
            foreach (var waveOut in owned) Looping.Remove(waveOut);
        }
        foreach (var waveOut in owned)
        {
            try { waveOut.Stop(); } catch { /* already finishing */ }
        }
    }

    /// <summary>Stops every sound and piece of music still playing.</summary>
    public static void StopAll()
    {
        List<WaveOutEvent> playing;
        lock (Playing) { playing = Playing.ToList(); Looping.Clear(); }
        foreach (var waveOut in playing)
        {
            try { waveOut.Stop(); } catch { /* already finishing */ }
        }
    }

    public static bool TryPlay(ResourceDatabase resources, int resourceId, double? volumeOverride = null, bool loop = false)
    {
        var entry = resources.Entries.FirstOrDefault(e => e.Id == resourceId);
        if (entry is null) { LastError = $"sound #{resourceId} is not in the Sound list"; return false; }
        if (!File.Exists(entry.FilePath)) { LastError = $"file not found: {entry.FilePath}"; return false; }
        return Play(entry, volumeOverride ?? entry.Volume, null, loop);
    }

    public static bool TryPlay(ResourceEntry entry, double volume) => Play(entry, volume, null, false);

    private static bool Play(ResourceEntry entry, double volume, object? owner, bool loop)
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
                bool again;
                lock (Playing) again = Looping.Contains(waveOut);
                if (again)
                {
                    // Looping: from the start again, until its owner stops it.
                    try { reader.Position = 0; waveOut.Play(); return; } catch { /* fall through and end it */ }
                }
                lock (Playing) { Playing.Remove(waveOut); Owners.Remove(waveOut); Looping.Remove(waveOut); SoundIds.Remove(waveOut); }
                waveOut.Dispose();
                reader.Dispose();
                Stopped?.Invoke(entry.Id);
            };
            lock (Playing)
            {
                Playing.Add(waveOut);
                SoundIds[waveOut] = entry.Id;
                if (owner is not null) Owners[waveOut] = owner;
                if (loop) Looping.Add(waveOut);
            }
            waveOut.Play();
            Played?.Invoke(entry.Id);
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"{ex.GetType().Name}: {ex.Message}";
            return false;
        }
    }
}
