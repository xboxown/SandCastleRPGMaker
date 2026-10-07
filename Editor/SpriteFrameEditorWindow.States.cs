using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Sprites whose frames are states, not an animation - keychains (frame by
/// how many keys, the Keychain group) and tilesheets (a tile a frame, the
/// Tileset group): no sound, no animation, and a guide alongside saying
/// what each frame is - as the Blood and Font groups already work.
/// </summary>
public partial class SpriteFrameEditorWindow
{
    private FrameStatesGuideWindow? _statesGuide;
    private FrameStatesGuideWindow.GuideText? _statesText;
    /// <summary>Most frames allowed (0 = no limit).</summary>
    private int _statesMaxFrames;

    private bool IsStatesSprite => _statesText is not null;

    private void SetUpStatesMode(FrameStatesGuideWindow.GuideText text, int maxFrames)
    {
        _statesText = text;
        _statesMaxFrames = maxFrames;
        foreach (var frame in _sprite.Frames) frame.SoundResourceId = -1; // never played, so never heard
        _sprite.Loop = false;
        foreach (UIElement control in new UIElement[] { FpsBox, LoopCheck, PlayPauseButton })
        {
            control.IsEnabled = false;
            ((FrameworkElement)control).ToolTip = "These frames are states, not an animation";
        }
        TrimStatesFrames();
        var guideButton = new Button { Content = text.Title + "...", Margin = new Thickness(0, 0, 6, 0), ToolTip = "What each frame is, and how it's used" };
        guideButton.Click += (_, _) => ShowStatesGuide();
        if (AddFramesButton.Parent is Panel panel) panel.Children.Add(guideButton);
        Loaded += (_, _) => ShowStatesGuide();
    }

    /// <summary>A keychain has five frames (0-4+ keys): frames past that are removed, with a word why.</summary>
    private void TrimStatesFrames()
    {
        if (_statesMaxFrames <= 0 || _sprite.Frames.Count <= _statesMaxFrames) return;
        int extra = _sprite.Frames.Count - _statesMaxFrames;
        _sprite.Frames.RemoveRange(_statesMaxFrames, extra);
        if (_currentFrameIndex >= _sprite.Frames.Count) _currentFrameIndex = _sprite.Frames.Count - 1;
        MessageBox.Show($"This sprite has {_statesMaxFrames} frames at most - the {extra} extra frame{(extra == 1 ? " was" : "s were")} removed.", _statesText?.Title ?? "Frames", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void ShowStatesGuide()
    {
        if (_statesText is null) return;
        if (_statesGuide is { IsLoaded: true }) { _statesGuide.Activate(); return; }
        _statesGuide = new FrameStatesGuideWindow(_sprite, _statesText) { Owner = this };
        _statesGuide.FrameChosen += index =>
        {
            if (index < 0 || index >= _sprite.Frames.Count) return;
            _currentFrameIndex = index;
            UpdatePreview();
            HighlightSelectedThumbnail();
        };
        _statesGuide.Closed += (_, _) => _statesGuide = null;
        _statesGuide.Left = Math.Max(0, Left + ActualWidth - 40);
        _statesGuide.Top = Math.Max(0, Top + 40);
        _statesGuide.Show();
        _statesGuide.Refresh(_currentFrameIndex);
    }
}
