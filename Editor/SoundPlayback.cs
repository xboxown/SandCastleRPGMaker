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

    /// <summary>The game's music and sound volume (Set Volume), 0-1 - every piece playing follows a change at once.</summary>
    public static double MusicVolume { get; private set; } = 1;
    public static double SoundVolume { get; private set; } = 1;

    /// <summary>What each one playing was asked to play at, whether it's music, and how far a fade has it (1 = full).</summary>
    private sealed class Level
    {
        public required AudioFileReader Reader;
        public double Base;
        public bool Music;
        public double Fade = 1;
        public void Apply() => Reader.Volume = (float)Math.Clamp(Base * Fade * (Music ? MusicVolume : SoundVolume), 0, 1);
    }

    private static readonly Dictionary<WaveOutEvent, Level> Levels = new();

    public static void SetVolumes(double music, double sounds)
    {
        MusicVolume = Math.Clamp(music, 0, 1);
        SoundVolume = Math.Clamp(sounds, 0, 1);
        lock (Playing) foreach (var level in Levels.Values) level.Apply();
    }

    /// <summary>Plays music owned by <paramref name="owner"/> at a volume, starting at a fade level (0 = silent, for a fade in).</summary>
    public static bool TryPlayFor(object owner, ResourceDatabase resources, int resourceId, bool loop, double? volume, double startFade = 1)
    {
        var entry = resources.Entries.FirstOrDefault(e => e.Id == resourceId);
        if (entry is null) { LastError = $"sound #{resourceId} is not in the Sound list"; return false; }
        if (!File.Exists(entry.FilePath)) { LastError = $"file not found: {entry.FilePath}"; return false; }
        return Play(entry, volume ?? entry.Volume, owner, loop, startFade);
    }

    /// <summary>Fades everything <paramref name="owner"/> plays up to full or down to silent over some seconds; down to silent, it stops at the end.</summary>
    public static void Fade(object owner, double seconds, bool fadeIn)
    {
        List<Level> levels;
        lock (Playing) levels = Owners.Where(kv => ReferenceEquals(kv.Value, owner) && Levels.ContainsKey(kv.Key)).Select(kv => Levels[kv.Key]).ToList();
        if (levels.Count == 0) return;
        if (seconds <= 0)
        {
            if (fadeIn) foreach (var l in levels) { l.Fade = 1; l.Apply(); }
            else StopFor(owner);
            return;
        }
        var started = DateTime.UtcNow;
        var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        var from = levels.ToDictionary(l => l, l => l.Fade);
        timer.Tick += (_, _) =>
        {
            double t = Math.Min(1, (DateTime.UtcNow - started).TotalSeconds / seconds);
            foreach (var l in levels) { l.Fade = from[l] + ((fadeIn ? 1 : 0) - from[l]) * t; l.Apply(); }
            if (t < 1) return;
            timer.Stop();
            if (!fadeIn) StopFor(owner);
        };
        timer.Start();
    }

    private static bool Play(ResourceEntry entry, double volume, object? owner, bool loop, double startFade = 1)
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

            var reader = new AudioFileReader(playbackPath);
            var level = new Level { Reader = reader, Base = Math.Clamp(volume, 0, 1), Music = entry.Kind == ResourceKind.Music, Fade = startFade };
            level.Apply();
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
                lock (Playing) { Playing.Remove(waveOut); Owners.Remove(waveOut); Looping.Remove(waveOut); SoundIds.Remove(waveOut); Levels.Remove(waveOut); }
                waveOut.Dispose();
                reader.Dispose();
                Stopped?.Invoke(entry.Id);
            };
            lock (Playing)
            {
                Playing.Add(waveOut);
                SoundIds[waveOut] = entry.Id;
                Levels[waveOut] = level;
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
