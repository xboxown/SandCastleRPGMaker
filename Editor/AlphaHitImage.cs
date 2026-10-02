using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// An Image that only counts as "under the mouse" where its picture has a
/// non-transparent pixel - the way Exult hit-tests a gump (Gump::has_point
/// asks the shape whether that exact pixel is drawn). A plain WPF Image is
/// clickable over its whole rectangle, transparent corners included, which
/// is what made paperdolls and containers grab clicks meant for whatever is
/// behind them. Works with every Stretch mode and with animated sources
/// (the alpha map is rebuilt when the frame changes, only when hit-tested).
/// </summary>
public class AlphaHitImage : Image
{
    private BitmapSource? _alphaSource;
    private byte[]? _alpha;
    private int _alphaWidth, _alphaHeight;

    protected override HitTestResult? HitTestCore(PointHitTestParameters hitTestParameters)
    {
        if (Source is not BitmapSource bitmap || bitmap.PixelWidth == 0 || bitmap.PixelHeight == 0) return null;
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return null;

        // Where the picture is actually drawn inside this element, for its Stretch mode (Image centers its content).
        double contentWidth = bitmap.Width, contentHeight = bitmap.Height; // device-independent size
        double scaleX = 1, scaleY = 1;
        switch (Stretch)
        {
            case Stretch.Fill:
                scaleX = size.Width / contentWidth;
                scaleY = size.Height / contentHeight;
                break;
            case Stretch.Uniform:
                scaleX = scaleY = Math.Min(size.Width / contentWidth, size.Height / contentHeight);
                break;
            case Stretch.UniformToFill:
                scaleX = scaleY = Math.Max(size.Width / contentWidth, size.Height / contentHeight);
                break;
        }
        double drawnWidth = contentWidth * scaleX, drawnHeight = contentHeight * scaleY;
        double offsetX = (size.Width - drawnWidth) / 2, offsetY = (size.Height - drawnHeight) / 2;

        var point = hitTestParameters.HitPoint;
        double u = (point.X - offsetX) / drawnWidth, v = (point.Y - offsetY) / drawnHeight;
        if (u < 0 || v < 0 || u >= 1 || v >= 1) return null;

        EnsureAlpha(bitmap);
        int px = Math.Min(_alphaWidth - 1, (int)(u * _alphaWidth));
        int py = Math.Min(_alphaHeight - 1, (int)(v * _alphaHeight));
        return _alpha![py * _alphaWidth + px] > 0 ? new PointHitTestResult(this, point) : null;
    }

    private void EnsureAlpha(BitmapSource bitmap)
    {
        if (ReferenceEquals(bitmap, _alphaSource) && _alpha is not null) return;
        var converted = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int width = converted.PixelWidth, height = converted.PixelHeight, stride = width * 4;
        var pixels = new byte[height * stride];
        converted.CopyPixels(pixels, stride, 0);
        var alpha = new byte[width * height];
        for (int i = 0; i < alpha.Length; i++) alpha[i] = pixels[i * 4 + 3];
        _alpha = alpha;
        _alphaWidth = width;
        _alphaHeight = height;
        _alphaSource = bitmap;
    }
}
