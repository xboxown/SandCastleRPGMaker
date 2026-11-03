// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Pentagram draws a shape frame at its offset (ItemSorter: the frame's xoff / yoff on the item's point). Here a sprite's
/// Origin (Sprite Editor) is that offset: set, it is the point of the picture that sits on the thing's (or character's)
/// map point in the Ultima 8 view; not set (0, 0), the picture stands on its bottom-middle.
/// </summary>
public partial class U8GameWindow
{
    /// <summary>
    /// A character's picture at the size of the frame it shows now: Ultima 8's frames are cut to what's drawn in each (a
    /// stride is wider than a step), so fitted into one box - the first frame's - they swelled and shrank as he walked.
    /// The box moves so the feet (its bottom-middle) stay where they were; a frame with a stand point (Sprite Editor)
    /// stands on it instead, so the body keeps still from frame to frame (Pentagram's per-frame offset).
    /// </summary>
    private static void FitToFrame(FrameworkElement picture, SpriteEntry sprite, int frameIndex)
    {
        if (sprite.Frames.Count == 0) return;
        var shown = sprite.Frames[Math.Clamp(frameIndex, 0, sprite.Frames.Count - 1)];
        if (FrameCache.Get(shown.FilePath) is not { } frame) return;
        double width = frame.PixelWidth, height = frame.PixelHeight, oldWidth = picture.Width, oldHeight = picture.Height;
        if (width != oldWidth || height != oldHeight)
        {
            double left = Canvas.GetLeft(picture), top = Canvas.GetTop(picture);
            if (!double.IsNaN(oldWidth) && !double.IsNaN(left)) Canvas.SetLeft(picture, left + (oldWidth - width) / 2);
            if (!double.IsNaN(oldHeight) && !double.IsNaN(top)) Canvas.SetTop(picture, top + (oldHeight - height));
            picture.Width = width;
            picture.Height = height;
        }
        if (shown.HasAnchor) U8ProjectionCanvas.SetAnchor(picture, new Point(shown.AnchorX / width, shown.AnchorY / height)); // its stand point on the spot
        else SetU8Anchor(picture, sprite);
    }

    /// <summary>A visual showing this sprite: its Origin as its anchor (a fraction of the box the picture is fitted in).</summary>
    private static void SetU8Anchor(FrameworkElement visual, SpriteEntry? sprite)
    {
        if (sprite is null || (sprite.OriginX == 0 && sprite.OriginY == 0) || sprite.Frames.Count == 0
            || FrameCache.Get(sprite.Frames[0].FilePath) is not { } first || double.IsNaN(visual.Width) || double.IsNaN(visual.Height) || visual.Width <= 0 || visual.Height <= 0)
        {
            U8ProjectionCanvas.SetAnchor(visual, null);
            return;
        }
        double scale = Math.Min(visual.Width / Math.Max(1, first.PixelWidth), visual.Height / Math.Max(1, first.PixelHeight));
        double insetX = (visual.Width - first.PixelWidth * scale) / 2, insetY = (visual.Height - first.PixelHeight * scale) / 2;
        U8ProjectionCanvas.SetAnchor(visual, new Point((insetX + sprite.OriginX * scale) / visual.Width, (insetY + sprite.OriginY * scale) / visual.Height));
    }
}
