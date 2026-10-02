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
/// </summary>
internal sealed class SpriteAnimator
{
    private readonly SpriteEntry _sprite;
    private readonly Image _target;
    private readonly ResourceDatabase? _resources;
    private readonly double? _volumeOverride;
    private readonly System.Windows.Threading.DispatcherTimer _timer = new(System.Windows.Threading.DispatcherPriority.Render);
    private int _frameIndex;
    private int _tickCount;

    public event Action<int>? Ticked;

    /// <summary>volumeOverride: null plays each frame's sound at the resource's own default volume (existing behavior, unchanged for every caller that doesn't pass it). 0 mutes it entirely; 0-1 scales it - used by the Internal Engine to respect a pose's own Muted/Volume setting.</summary>
    public SpriteAnimator(SpriteEntry sprite, Image target, ResourceDatabase? resources = null, double? volumeOverride = null)
    {
        _sprite = sprite;
        _target = target;
        _resources = resources;
        _volumeOverride = volumeOverride;

        if (_sprite.Frames.Count == 0)
        {
            _target.Source = LoadBitmap(_sprite.FilePath);
            return;
        }

        // Frame 0's own sound deliberately does NOT play here at
        // construction - many hosts rebuild their SpriteAnimator on every
        // refresh (e.g. every time the main canvas reloads a page's text),
        // and playing a sound on every one of those reconstructions would
        // spam it far more often than the animation is actually starting
        // fresh. It still plays the first time Advance() naturally cycles
        // back around to frame 0 on a loop.
        _target.Source = LoadBitmap(_sprite.Frames[0].FilePath);
        if (_sprite.Frames.Count > 1)
        {
            _timer.Interval = TimeSpan.FromMilliseconds(1000.0 / Math.Max(1, _sprite.Fps));
            _timer.Tick += (_, _) => Advance();
            _timer.Start();
        }
    }

    private void Advance()
    {
        _tickCount++;
        _frameIndex++;
        if (_frameIndex >= _sprite.Frames.Count)
        {
            if (_sprite.Loop) _frameIndex = 0;
            else { _frameIndex = _sprite.Frames.Count - 1; _timer.Stop(); return; }
        }

        var frame = _sprite.Frames[_frameIndex];
        _target.Source = LoadBitmap(frame.FilePath);
        if (_resources is not null && frame.SoundResourceId >= 0 && _volumeOverride != 0)
            SoundPlayback.TryPlay(_resources, frame.SoundResourceId, _volumeOverride);
        Ticked?.Invoke(_tickCount);
    }

    public void Stop() { _timer.Stop(); _pausedWhileRunning = false; }

    private bool _pausedWhileRunning;

    /// <summary>Freezes the animation on its current frame (Exult's time queue pause) - Resume carries on from that frame. Safe to call repeatedly.</summary>
    public void Pause()
    {
        if (!_timer.IsEnabled) return;
        _timer.Stop();
        _pausedWhileRunning = true;
    }

    public void Resume()
    {
        if (!_pausedWhileRunning) return;
        _pausedWhileRunning = false;
        _timer.Start();
    }

    private static BitmapImage? LoadBitmap(string path)
    {
        if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            return bmp;
        }
        catch { return null; }
    }
}
