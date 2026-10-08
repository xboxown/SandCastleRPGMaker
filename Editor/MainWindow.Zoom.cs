using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Map Editor zoom. 100% is the game's own size in full screen: Game Test
/// draws its 320x200 view scaled by the largest whole number that fits the
/// window (GameTestWindow.RecomputeViewportScale), so on a 1920x1080 screen
/// 100% here is 5x the map's own pixels. Other levels are percentages of
/// that. The whole map canvas is scaled as one, so clicks (measured in the
/// canvas's own coordinates) land on the same cells at any zoom.
/// </summary>
public partial class MainWindow
{
    private const string GridLineTag = "grid";
    private static readonly int[] ZoomLevels = { 10, 20, 25, 33, 50, 75, 100, 150, 200, 300 };
    private int _zoomPercent = 100;
    private bool _settingZoomCombo;

    /// <summary>Game Test's full-screen scale: the 320x200 view, scaled by the largest whole number that fits the screen.</summary>
    private static int GameFullScreenScale =>
        Math.Max(1, (int)Math.Floor(Math.Min(SystemParameters.PrimaryScreenWidth / 320.0, SystemParameters.PrimaryScreenHeight / 200.0)));

    private double ZoomScale => GameFullScreenScale * _zoomPercent / 100.0;

    /// <summary>Grid lines stay one screen pixel thin at any zoom.</summary>
    private double GridLineThickness => 1.0 / ZoomScale;

    private void InitZoom()
    {
        _settingZoomCombo = true;
        ZoomCombo.ItemsSource = ZoomLevels.Select(z => $"{z}%").ToList();
        ZoomCombo.SelectedIndex = Array.IndexOf(ZoomLevels, _zoomPercent);
        _settingZoomCombo = false;
        RenderOptions.SetBitmapScalingMode(MapCanvas, BitmapScalingMode.NearestNeighbor); // crisp pixels, like the game
        ApplyZoom();
    }

    private void ZoomCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settingZoomCombo || ZoomCombo.SelectedIndex < 0) return;
        SetZoom(ZoomLevels[ZoomCombo.SelectedIndex], null);
    }

    /// <summary>Ctrl + mouse wheel: the next zoom level in or out, keeping the spot under the mouse where it is.</summary>
    private void MapScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) return;
        e.Handled = true;
        int index = Array.IndexOf(ZoomLevels, _zoomPercent);
        if (index < 0) index = Array.IndexOf(ZoomLevels, 100);
        index = Math.Clamp(index + (e.Delta > 0 ? 1 : -1), 0, ZoomLevels.Length - 1);
        SetZoom(ZoomLevels[index], e.GetPosition(MapScrollViewer));
    }

    private void SetZoom(int percent, Point? anchorInViewer)
    {
        if (percent == _zoomPercent) return;
        // The map point under the anchor (the mouse, or the middle of the view) before zooming.
        var anchor = anchorInViewer ?? new Point(MapScrollViewer.ViewportWidth / 2, MapScrollViewer.ViewportHeight / 2);
        double oldScale = ZoomScale;
        double mapX = (MapScrollViewer.HorizontalOffset + anchor.X) / oldScale;
        double mapY = (MapScrollViewer.VerticalOffset + anchor.Y) / oldScale;

        _zoomPercent = percent;
        _settingZoomCombo = true;
        ZoomCombo.SelectedIndex = Array.IndexOf(ZoomLevels, percent);
        _settingZoomCombo = false;
        ApplyZoom();

        MapScrollViewer.UpdateLayout();
        MapScrollViewer.ScrollToHorizontalOffset(mapX * ZoomScale - anchor.X);
        MapScrollViewer.ScrollToVerticalOffset(mapY * ZoomScale - anchor.Y);
        StatusText.Text = $"Zoom {percent}% ({ZoomScale:0.##}x the map's pixels - 100% is the game's own size in full screen).";
    }

    private void ApplyZoom()
    {
        double scale = ZoomScale;
        MapCanvas.LayoutTransform = new ScaleTransform(scale, scale);
        foreach (var line in MapCanvas.Children.OfType<Line>().Where(l => Equals(l.Tag, GridLineTag)))
            line.StrokeThickness = GridLineThickness;
    }
}
