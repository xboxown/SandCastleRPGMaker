using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 8: each frame's stand point (SpriteFrame.AnchorX / AnchorY) - where the frame stands on the character's spot.
/// Pentagram keeps an offset per frame; frames cut out to what's drawn in each lose it, so a walk sways. Auto-align puts it
/// back from the pictures themselves: every frame stands so the head keeps still (its middle and its top), the feet below.
/// Shared by the Sprite Editor (its marks, Auto-align, the preview) and the Ultima 8 engine.
/// </summary>
internal static class StandPoints
{
    /// <summary>Where a frame stands, in its own pixels: its stand point, or its bottom-middle.</summary>
    public static (double X, double Y) Of(SpriteFrame frame, BitmapSource picture) =>
        frame.HasAnchor ? (frame.AnchorX, frame.AnchorY) : (picture.PixelWidth / 2.0, picture.PixelHeight);

    /// <summary>
    /// Every frame stood so its head keeps still: the stand point straight under the middle of the head (its top rows), as
    /// far below the head's top as the frames are tall, mostly (their median). False: no picture to read.
    /// </summary>
    public static bool AutoAlign(SpriteEntry sprite)
    {
        var heads = new List<(SpriteFrame Frame, int Top, double HeadX, int Bottom)>();
        foreach (var frame in sprite.Frames)
            if (FrameCache.Get(frame.FilePath) is { } picture && Head(picture) is { } head)
                heads.Add((frame, head.Top, head.HeadX, head.Bottom));
        if (heads.Count == 0) return false;
        var heights = heads.Select(h => h.Bottom - h.Top + 1).OrderBy(h => h).ToList();
        int tall = heights[heights.Count / 2];
        foreach (var (frame, top, headX, _) in heads)
        {
            frame.AnchorX = (int)Math.Round(headX);
            frame.AnchorY = top + tall;
        }
        return true;
    }

    /// <summary>The picture's drawn pixels: the top row, the middle of the head (its top 8 rows), the bottom row - null: nothing drawn.</summary>
    public static (int Top, double HeadX, int Bottom)? Head(BitmapSource picture)
    {
        var source = picture.Format == PixelFormats.Bgra32 ? picture : new FormatConvertedBitmap(picture, PixelFormats.Bgra32, null, 0);
        int width = source.PixelWidth, height = source.PixelHeight, stride = width * 4;
        var pixels = new byte[stride * height];
        source.CopyPixels(pixels, stride, 0);
        bool Drawn(int x, int y) => pixels[y * stride + x * 4 + 3] != 0;
        int top = -1, bottom = -1;
        for (int y = 0; y < height && top < 0; y++)
            for (int x = 0; x < width; x++) if (Drawn(x, y)) { top = y; break; }
        if (top < 0) return null;
        for (int y = height - 1; y >= 0 && bottom < 0; y--)
            for (int x = 0; x < width; x++) if (Drawn(x, y)) { bottom = y; break; }
        double sum = 0;
        int count = 0;
        for (int y = top; y < Math.Min(height, top + 8); y++)
            for (int x = 0; x < width; x++) if (Drawn(x, y)) { sum += x + 0.5; count++; }
        return (top, count > 0 ? sum / count : width / 2.0, bottom);
    }
}
