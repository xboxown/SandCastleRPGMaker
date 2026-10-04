using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A small play/stop button for an editor's sprite preview. Stopped (the
/// default), the preview shows the sprite's first frame, still and silent.
/// Play runs the animation with each frame's sound, exactly as the game
/// will, and the button turns into a red stop button; Stop goes back to the
/// still first frame. So opening an editor never sets off a chorus of
/// sounds - only what you choose to play makes a noise.
/// </summary>
internal sealed class AnimationPreviewButton : Button
{
    private readonly SpriteEntry _sprite;
    private readonly Image _target;
    private readonly ResourceDatabase? _resources;
    private readonly double? _volume;
    private SpriteAnimator? _animator;

    /// <summary>Every preview button that is (or was) playing, so a window can stop them all when it rebuilds or closes.</summary>
    public static readonly List<AnimationPreviewButton> Playing = new();

    /// <param name="volume">null: each frame's sound at its own volume; 0: silent; 0-1: scaled (a pose's Muted / Volume).</param>
    public AnimationPreviewButton(SpriteEntry sprite, Image target, ResourceDatabase? resources, double? volume = null)
    {
        _sprite = sprite;
        _target = target;
        _resources = resources;
        _volume = volume;
        Padding = new Thickness(6, 0, 6, 0);
        MinWidth = 26;
        HorizontalAlignment = HorizontalAlignment.Center;
        ShowStill();
        Click += (_, _) => { if (_animator is null) Play(); else StopPlaying(); };
        Unloaded += (_, _) => StopPlaying();
    }

    public bool IsPlaying => _animator is not null;

    private void Play()
    {
        _animator = new SpriteAnimator(_sprite, _target, _resources, _volume);
        // Frame 1's sound: SpriteAnimator leaves it for the first loop round, but pressing Play is a real start.
        if (_resources is not null && _volume != 0 && _sprite.Frames.Count > 0 && _sprite.Frames[0].SoundResourceId >= 0)
            SoundPlayback.TryPlay(_resources, _sprite.Frames[0].SoundResourceId, _volume);
        Playing.Add(this);
        Content = new TextBlock { Text = "■", Foreground = Brushes.Red, FontWeight = FontWeights.Bold };
        ToolTip = "Stop";
    }

    public void StopPlaying()
    {
        _animator?.Stop();
        _animator = null;
        Playing.Remove(this);
        ShowStill();
    }

    /// <summary>The still first frame, and the play symbol.</summary>
    private void ShowStill()
    {
        var still = new SpriteAnimator(_sprite, _target, null); // draws frame 1 silently...
        still.Stop();                                           // ...and stays there
        Content = new TextBlock { Text = "▶", Foreground = Brushes.Green, FontWeight = FontWeights.Bold };
        ToolTip = "Play the animation and its sounds";
    }

    /// <summary>Stops every playing preview (a window rebuilding or closing).</summary>
    public static void StopAll()
    {
        foreach (var button in Playing.ToList()) button.StopPlaying();
    }
}
