using System.Windows.Controls;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Drives an Image control from a sprite's real animation data - cycles
/// through Frames at the sprite's Fps, respecting Loop, and plays each
/// frame's attached sound (if any) the moment that frame is reached. A
/// sprite with zero frames (never touched in the new frame editor) is a
/// plain legacy static sprite - shows its one FilePath image, no
/// animation, no sound, so nothing that predates this feature ever breaks.
///
/// This is the single class every sprite display in the project routes
/// through - the Preview window's world icon and paper doll, embedded
/// sprites in book text, the Select Item picker's animated preview, all of
/// it - so wiring sound in here once means it plays wherever that sprite
/// is shown, with no need to touch each individual window separately.
/// resources is optional: a caller with no ResourceDatabase on hand just
/// gets silent animation, exactly like before this existed.
///
/// Speed: its frames' pictures come decoded and shared from FrameCache
/// (loaded once when it starts, not from disk on every tick), and every
/// animator runs on one shared clock rather than a timer each.
/// </summary>
internal sealed class SpriteAnimator
{
    private readonly SpriteEntry _sprite;
    private readonly Image _target;
    private readonly ResourceDatabase? _resources;
    private readonly double? _volumeOverride;
    private readonly BitmapImage?[] _frames = Array.Empty<BitmapImage?>();
    private readonly double _intervalMs;
    private int _frameIndex;
    private int _tickCount;
    private double _dueMs;
    private bool _running;

    public event Action<int>? Ticked;

    /// <summary>A picture in the game world: how its frame sounds are heard from where it is (GameTestWindow.Hearing) - null when too far, then they're not played. Unset: heard as they are (the editors, the windows).</summary>
    public Func<(double Volume, double Balance)?>? Hearing { get; set; }

    /// <summary>volumeOverride: null plays each frame's sound at the resource's own default volume (existing behavior, unchanged for every caller that doesn't pass it). 0 mutes it entirely; 0-1 scales it - used by the Internal Engine to respect a pose's own Muted/Volume setting.</summary>
    public SpriteAnimator(SpriteEntry sprite, Image target, ResourceDatabase? resources = null, double? volumeOverride = null)
    {
        _sprite = sprite;
        _target = target;
        _resources = resources;
        _volumeOverride = volumeOverride;

        if (_sprite.Frames.Count == 0)
        {
            _target.Source = FrameCache.Get(_sprite.FilePath);
            return;
        }

        // Frame 0's own sound deliberately does NOT play here at
        // construction - many hosts rebuild their SpriteAnimator on every
        // refresh (e.g. every time the main canvas reloads a page's text),
        // and playing a sound on every one of those reconstructions would
        // spam it far more often than the animation is actually starting
        // fresh. It still plays the first time Advance() naturally cycles
        // back around to frame 0 on a loop.
        _frames = _sprite.Frames.Select(f => FrameCache.Get(f.FilePath)).ToArray();
        _target.Source = _frames[0];
        if (_sprite.Frames.Count > 1)
        {
            _intervalMs = 1000.0 / Math.Max(1, _sprite.Fps);
            Start();
        }
    }

    private void Start()
    {
        if (_running) return;
        _running = true;
        _dueMs = AnimationClock.NowMs + _intervalMs;
        AnimationClock.Add(this);
    }

    private void Halt()
    {
        if (!_running) return;
        _running = false;
        AnimationClock.Remove(this);
    }

    /// <summary>The shared clock's tick: on (or past) its time, the next frame.</summary>
    internal void Tick(double nowMs)
    {
        if (!_running || nowMs < _dueMs) return;
        // Behind by more than a frame (the window was busy): catch up the time, not every frame.
        _dueMs = nowMs - _dueMs > _intervalMs ? nowMs + _intervalMs : _dueMs + _intervalMs;
        Advance();
    }

    private void Advance()
    {
        _tickCount++;
        _frameIndex++;
        if (_frameIndex >= _sprite.Frames.Count)
        {
            if (_sprite.Loop) _frameIndex = 0;
            else { _frameIndex = _sprite.Frames.Count - 1; Halt(); return; }
        }

        var frame = _sprite.Frames[_frameIndex];
        _target.Source = _frameIndex < _frames.Length ? _frames[_frameIndex] : FrameCache.Get(frame.FilePath);
        if (_resources is not null && frame.SoundResourceId >= 0 && _volumeOverride != 0)
        {
            if (Hearing is { } hearing) { if (hearing() is { } heard) SoundPlayback.TryPlayAt(_resources, frame.SoundResourceId, heard.Volume, heard.Balance, _volumeOverride); }
            else SoundPlayback.TryPlay(_resources, frame.SoundResourceId, _volumeOverride);
        }
        Ticked?.Invoke(_tickCount);
    }

    public void Stop() { Halt(); _pausedWhileRunning = false; }

    private bool _pausedWhileRunning;

    /// <summary>Freezes the animation on its current frame (Exult's time queue pause) - Resume carries on from that frame. Safe to call repeatedly.</summary>
    public void Pause()
    {
        if (!_running) return;
        Halt();
        _pausedWhileRunning = true;
    }

    public void Resume()
    {
        if (!_pausedWhileRunning) return;
        _pausedWhileRunning = false;
        Start();
    }
}

/// <summary>One clock for every running SpriteAnimator (a timer each cost hundreds of timers on a busy map).</summary>
internal static class AnimationClock
{
    private static readonly HashSet<SpriteAnimator> Running = new(ReferenceEqualityComparer.Instance);
    private static readonly System.Diagnostics.Stopwatch Watch = System.Diagnostics.Stopwatch.StartNew();
    private static System.Windows.Threading.DispatcherTimer? _timer;
    private static SpriteAnimator[] _snapshot = Array.Empty<SpriteAnimator>();
    private static bool _changed;

    public static double NowMs => Watch.Elapsed.TotalMilliseconds;

    public static void Add(SpriteAnimator animator)
    {
        if (!Running.Add(animator)) return;
        _changed = true;
        if (_timer is null)
        {
            _timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
            _timer.Tick += (_, _) => TickAll();
        }
        if (!_timer.IsEnabled) _timer.Start();
    }

    public static void Remove(SpriteAnimator animator)
    {
        if (Running.Remove(animator)) _changed = true;
        if (Running.Count == 0) _timer?.Stop();
    }

    /// <summary>Measuring: how long the ticks took and how many animators ran (GameTestWindow.Perf.cs logs and clears them).</summary>
    public static double TickMs;
    public static int Count => Running.Count;
    public static bool Off = Environment.GetEnvironmentVariable("SANDCASTLE_PERF_HIDE")?.Contains("anim") == true;

    private static void TickAll()
    {
        if (Off) return;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_changed) { _snapshot = Running.ToArray(); _changed = false; }
        double now = NowMs;
        foreach (var animator in _snapshot) animator.Tick(now);
        TickMs += System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }
}
