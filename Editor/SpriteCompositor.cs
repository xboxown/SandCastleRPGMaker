using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Flattens a sprite's layers for one frame into a single composited image,
/// written to frame.FilePath - the same field every existing consumer
/// (Book Editor's paper doll, the item picker, BookPreviewWindow's
/// animator, etc.) already reads for "the sprite's picture". None of them
/// need to know layers exist; they just keep seeing an up-to-date flat
/// image, same as before layers were added.
/// </summary>
public static class SpriteCompositor
{
    public static void Composite(SpriteEntry sprite, SpriteFrame frame)
    {
        if (sprite.Layers.Count == 0 || frame.LayerFilePaths.Count == 0) return; // no layers - FilePath is already authoritative, nothing to do

        var result = BuildComposite(sprite, frame, _ => true);
        if (result is null) return;

        string outputPath = string.IsNullOrEmpty(frame.FilePath)
            ? Path.Combine(Path.GetTempPath(), $"sandcastle_composite_{Guid.NewGuid():N}.png")
            : frame.FilePath;

        using (var stream = new FileStream(outputPath, FileMode.Create))
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(result));
            encoder.Save(stream);
        }

        frame.FilePath = outputPath;
    }

    /// <summary>
    /// Same blend logic as Composite above, but returns the flattened image
    /// in memory instead of writing it to disk - used by the drawing window
    /// to show "everything except the layer I'm currently editing" as live
    /// context while drawing, without touching any files. includeLayer lets
    /// the caller restrict which layers count (e.g. only those above/below
    /// the active one).
    /// </summary>
    public static WriteableBitmap? BuildComposite(SpriteEntry sprite, SpriteFrame frame, Func<SpriteLayer, bool> includeLayer)
    {
        int width = 0, height = 0;
        foreach (var path in frame.LayerFilePaths.Values)
        {
            var bmp = LoadBitmap(path);
            if (bmp is null) continue;
            width = Math.Max(width, bmp.PixelWidth);
            height = Math.Max(height, bmp.PixelHeight);
        }
        if (width == 0 || height == 0) return null;

        int stride = width * 4;
        var basePixels = new byte[height * stride];

        // Layers are stored top-to-bottom (index 0 = topmost), so compositing bottom-first means walking the list in reverse.
        foreach (var layer in Enumerable.Reverse(sprite.Layers))
        {
            if (layer.IsGroup) continue;
            if (!includeLayer(layer)) continue;
            if (!IsLayerVisible(sprite, layer)) continue;
            if (!frame.LayerFilePaths.TryGetValue(layer.Id, out var layerPath)) continue;

            var bmp = LoadBitmap(layerPath);
            if (bmp is null) continue;

            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int layerStride = bmp.PixelWidth * 4;
            var layerPixels = new byte[bmp.PixelHeight * layerStride];
            converted.CopyPixels(layerPixels, layerStride, 0);

            int copyW = Math.Min(bmp.PixelWidth, width), copyH = Math.Min(bmp.PixelHeight, height);
            for (int y = 0; y < copyH; y++)
            {
                for (int x = 0; x < copyW; x++)
                {
                    int srcI = y * layerStride + x * 4;
                    int dstI = y * stride + x * 4;
                    byte srcA = layerPixels[srcI + 3];
                    if (srcA == 0) continue;

                    if (srcA == 255)
                    {
                        basePixels[dstI] = layerPixels[srcI];
                        basePixels[dstI + 1] = layerPixels[srcI + 1];
                        basePixels[dstI + 2] = layerPixels[srcI + 2];
                        basePixels[dstI + 3] = 255;
                    }
                    else
                    {
                        double a = srcA / 255.0;
                        basePixels[dstI] = (byte)(layerPixels[srcI] * a + basePixels[dstI] * (1 - a));
                        basePixels[dstI + 1] = (byte)(layerPixels[srcI + 1] * a + basePixels[dstI + 1] * (1 - a));
                        basePixels[dstI + 2] = (byte)(layerPixels[srcI + 2] * a + basePixels[dstI + 2] * (1 - a));
                        basePixels[dstI + 3] = (byte)Math.Min(255, basePixels[dstI + 3] + srcA);
                    }
                }
            }
        }

        var result = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        result.WritePixels(new Int32Rect(0, 0, width, height), basePixels, stride, 0);
        return result;
    }

    /// <summary>A layer inside a hidden group is also hidden, even if its own Visible flag is true.</summary>
    private static bool IsLayerVisible(SpriteEntry sprite, SpriteLayer layer)
    {
        if (!layer.Visible) return false;
        int parentId = layer.ParentGroupId;
        while (parentId >= 0)
        {
            var parent = sprite.Layers.FirstOrDefault(l => l.Id == parentId);
            if (parent is null) break;
            if (!parent.Visible) return false;
            parentId = parent.ParentGroupId;
        }
        return true;
    }

    private static BitmapSource? LoadBitmap(string path) => FrameCache.Get(path);
}
