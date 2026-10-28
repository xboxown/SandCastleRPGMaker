// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using System.Windows;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Pentagram draws a shape frame at its offset (ItemSorter: the frame's xoff / yoff on the item's point). Here a sprite's
/// Origin (Sprite Editor) is that offset: set, it is the point of the picture that sits on the thing's (or character's)
/// map point in the Ultima 8 view; not set (0, 0), the picture stands on its bottom-middle.
/// </summary>
public partial class U8GameWindow
{
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
