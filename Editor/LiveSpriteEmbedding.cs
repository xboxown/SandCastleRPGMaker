using System.IO;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A sprite embedded in book page text has been through two failed
/// identification schemes, both directly disproven by Check Image
/// (Debug): first Image.Uid (comes back empty after any save/load round-
/// trip), then the Image's own Source file path (comes back as something
/// like "./Image1.png" - not a real path at all). The reason is the same
/// for both: DataFormats.XamlPackage doesn't just serialize a reference to
/// an external image, it EMBEDS a copy of the actual image data into the
/// package and rewrites the Image element to point at its own internal
/// resource name - which explains why the picture still displays
/// correctly (the pixel data really is copied in) while every piece of
/// metadata on the Image element gets reconstructed from scratch.
///
/// So identification here uses the one thing that genuinely can't be
/// rewritten: the actual pixel content. A sprite is resolved by hashing
/// the displayed Image's pixels and comparing against a hash of each
/// candidate sprite's first-frame file, computed once and cached by path.
///
/// Any RichTextBox that might display this content - the editing popup,
/// and (via FormattedContentRenderer) the main canvas and Preview window -
/// resolves the sprite this way and attaches a fresh SpriteAnimator, since
/// a DispatcherTimer obviously can't itself survive serialization.
/// </summary>
internal static class LiveSpriteEmbedding
{
    /// <summary>Sprite first-frame file path -> pixel hash, computed once per path rather than re-hashing the same file every time a paragraph needs resolving.</summary>
    private static readonly Dictionary<string, string?> SpriteHashCache = new();

    /// <summary>
    /// Looks inside a Floater for an embedded sprite, checking both
    /// structural forms WPF's own XAML save/load round-trip can produce:
    /// originally always a bare BlockUIContainer directly, but a range
    /// that's been through an extract/split (any page after the first,
    /// once pagination has actually split something) can come back as a
    /// Paragraph containing an InlineUIContainer instead.
    /// </summary>
    public static bool TryResolveSprite(Floater floater, SpriteDatabase sprites, out SpriteEntry? sprite, out Image? image)
    {
        sprite = null;
        image = null;

        foreach (var innerBlock in floater.Blocks)
        {
            var candidate = innerBlock switch
            {
                BlockUIContainer buc => buc.Child as Image,
                Paragraph innerPara => innerPara.Inlines.OfType<InlineUIContainer>().FirstOrDefault()?.Child as Image,
                _ => null
            };
            if (candidate is not null) { image = candidate; break; }
        }

        if (image?.Source is not BitmapSource displayed) return false;

        string? displayedHash = ComputePixelHash(displayed);
        if (displayedHash is null) return false;

        sprite = sprites.Groups
            .SelectMany(g => g.Sprites)
            .FirstOrDefault(s =>
            {
                string path = s.Frames.Count > 0 ? s.Frames[0].FilePath : s.FilePath;
                if (string.IsNullOrEmpty(path)) return false;
                return GetOrComputeFileHash(path) == displayedHash;
            });

        return sprite is not null;
    }

    private static string? GetOrComputeFileHash(string path)
    {
        if (SpriteHashCache.TryGetValue(path, out var cached)) return cached;

        string? hash = null;
        if (File.Exists(path))
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                hash = ComputePixelHash(bmp);
            }
            catch { /* leave hash null - this sprite just won't match anything */ }
        }

        SpriteHashCache[path] = hash;
        return hash;
    }

    private static string? ComputePixelHash(BitmapSource bmp)
    {
        try
        {
            var converted = new FormatConvertedBitmap(bmp, PixelFormats.Bgra32, null, 0);
            int stride = converted.PixelWidth * 4;
            if (stride <= 0 || converted.PixelHeight <= 0) return null;
            byte[] pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            using var sha = System.Security.Cryptography.SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(pixels));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Returns the animators it created, so the caller can Stop() them later (e.g. before the next reload, or when the containing window closes) - this method has no way to know when that should happen on its own.</summary>
    public static List<SpriteAnimator> ReattachLiveSprites(RichTextBox box, SpriteDatabase sprites, ResourceDatabase? resources = null)
    {
        var animators = new List<SpriteAnimator>();
        WalkBlocks(box.Document.Blocks, sprites, resources, animators);
        return animators;
    }

    /// <summary>
    /// Recurses into Section (and any other Block container), not just
    /// Paragraph directly under the document - a range that's been through
    /// an extract/split round-trip isn't guaranteed to keep the same flat
    /// Paragraph-under-Document shape a freshly-typed/pasted document has;
    /// WPF's own TextRange.Save/Load can wrap partial-range content in a
    /// Section. A flat, non-recursive walk would silently find zero
    /// Floaters on exactly that content and never re-animate it.
    /// </summary>
    private static void WalkBlocks(IEnumerable<Block> blocks, SpriteDatabase sprites, ResourceDatabase? resources, List<SpriteAnimator> animators)
    {
        foreach (var block in blocks)
        {
            switch (block)
            {
                case Paragraph para:
                    foreach (var floater in para.Inlines.OfType<Floater>())
                    {
                        if (TryResolveSprite(floater, sprites, out var sprite, out var image) && sprite is not null && image is not null)
                            animators.Add(new SpriteAnimator(sprite, image, resources));
                    }
                    break;

                case Section section:
                    WalkBlocks(section.Blocks, sprites, resources, animators);
                    break;
            }
        }
    }
}
