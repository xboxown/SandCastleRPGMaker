using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A full-screen view over the game - a map being looked at (Exult's
/// display_map, shown until a click: Get_click) or an open spellbook. The
/// game is paused while it's up, as with a book.
/// </summary>
public partial class GameTestWindow
{
    private Border? _objectOverlay;
    /// <summary>True while a map or spellbook is up.</summary>
    private bool _objectOverlayOpen;
    /// <summary>Its own buttons take clicks (a spellbook); false: any click closes it (a map).</summary>
    private bool _objectOverlayTakesClicks;
    /// <summary>Keys for what's up (the slider's Left / Right / Enter / Escape); null: Escape, Enter or Space close it.</summary>
    private Func<System.Windows.Input.Key, bool>? _overlayKeys;

    /// <summary>
    /// Ultima VII's number slider (System Settings > Slider; Exult's
    /// input_numeric_value / Slider_gump) over the game, with its question
    /// above it: the value is passed on when it's accepted (check mark or
    /// Enter); Escape cancels. The game waits while it's up.
    /// </summary>
    private void ShowNumberSlider(string question, int min, int max, int step, int value, Action<int> accepted, Action? cancelled = null)
    {
        var slider = new NumberSliderControl(_systemSettings.Slider, _sprites, _resources, min, max, step, value, GameTextLabel);
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        if (question.Trim().Length > 0)
        {
            var label = GameTextLabel(question.Trim());
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.Margin = new Thickness(0, 0, 0, 3);
            panel.Children.Add(label);
        }
        panel.Children.Add(slider);
        slider.Accepted += v => { CloseObjectOverlay(); accepted(v); };
        slider.Cancelled += () => { CloseObjectOverlay(); cancelled?.Invoke(); };
        ShowObjectOverlay(panel, takesClicks: true, scaleLikeViewport: true);
        _overlayKeys = slider.HandleKey;
        StatusText.Text = $"{question.Trim()} ({min}-{max}: arrows or drag, Enter to accept, Escape to cancel)";
    }

    private void ShowObjectOverlay(UIElement content, bool takesClicks, bool scaleLikeViewport)
    {
        CloseObjectOverlay();
        CancelPendingIdentify();
        _objectOverlay = new Border
        {
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LayoutTransform = scaleLikeViewport ? ViewportScale : Transform.Identity // the screen's own zoom: it grows and shrinks with the window, like everything else
        };
        RenderOptions.SetBitmapScalingMode(_objectOverlay, BitmapScalingMode.NearestNeighbor);
        Panel.SetZIndex(_objectOverlay, 1001);
        ViewportHost.Children.Add(_objectOverlay);
        _objectOverlayOpen = true;
        _objectOverlayTakesClicks = takesClicks;
    }

    private void CloseObjectOverlay()
    {
        if (_objectOverlay is not null) ViewportHost.Children.Remove(_objectOverlay);
        _objectOverlay = null;
        _objectOverlayOpen = false;
        _overlayKeys = null;
        GameCanvas.Focus();
    }
}
