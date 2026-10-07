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

    private void ShowObjectOverlay(UIElement content, bool takesClicks, bool scaleLikeViewport)
    {
        CloseObjectOverlay();
        CancelPendingIdentify();
        _objectOverlay = new Border
        {
            Child = content,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            LayoutTransform = scaleLikeViewport ? new ScaleTransform(ViewportScale.ScaleX, ViewportScale.ScaleY) : Transform.Identity
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
        GameCanvas.Focus();
    }
}
