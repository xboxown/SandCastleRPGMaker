using System.Collections.Concurrent;
using System.IO;
using System.Windows.Media.Imaging;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Every sprite frame's picture, decoded once and shared (frozen) by every
/// place that shows it - the game drew hundreds of animated things, each
/// decoding its next frame from disk on every tick. A file changed on disk
/// since (a frame redrawn in the Sprite Editor) is seen by its time and size
/// and decoded again, so edits still show.
/// </summary>
internal static class FrameCache
{
    private sealed record Entry(DateTime Written, long Length, BitmapImage Picture);

    private static readonly ConcurrentDictionary<string, Entry> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The picture of an image file, or null (no file, not an image).</summary>
    public static BitmapImage? Get(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        FileInfo info;
        try
        {
            info = new FileInfo(path);
            if (!info.Exists) return null;
        }
        catch { return null; }
        if (Cache.TryGetValue(path, out var cached) && cached.Written == info.LastWriteTimeUtc && cached.Length == info.Length) return cached.Picture;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache; // decoded fresh from the file as it is now
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            bmp.Freeze(); // shared by every Image showing it, on any thread
            Cache[path] = new Entry(info.LastWriteTimeUtc, info.Length, bmp);
            return bmp;
        }
        catch { return null; }
    }
}
