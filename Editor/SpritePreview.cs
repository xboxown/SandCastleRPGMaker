using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A small picture of a sprite for lists and pickers (the Sprite Editor's list, the Object Editor's,
/// the Houses and Vehicles lists): an animated sprite plays its frames while it's on screen - never
/// its frame sounds - and stops when the row scrolls away (a recycled row starts the one it shows now).
/// </summary>
internal sealed class SpritePreview : Image
{
    public static readonly DependencyProperty SpriteProperty =
        DependencyProperty.Register(nameof(Sprite), typeof(SpriteEntry), typeof(SpritePreview), new PropertyMetadata(null, (d, _) => ((SpritePreview)d).Restart()));

    public SpriteEntry? Sprite
    {
        get => (SpriteEntry?)GetValue(SpriteProperty);
        set => SetValue(SpriteProperty, value);
    }

    private SpriteAnimator? _animator;

    public SpritePreview()
    {
        Stretch = Stretch.Uniform;
        StretchDirection = StretchDirection.Both;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        Loaded += (_, _) => Restart();
        Unloaded += (_, _) => { _animator?.Stop(); _animator = null; };
    }

    private void Restart()
    {
        _animator?.Stop();
        _animator = null;
        if (Sprite is not { } sprite) { Source = null; return; }
        if (!IsLoaded) return;
        try { _animator = new SpriteAnimator(sprite, this, resources: null); } // no resources: no frame sounds
        catch { Source = null; } // a picture that can't be read: blank
    }
}
