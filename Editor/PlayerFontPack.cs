using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// fonts.sdbx, for the games built for the C player (Amiga, MorphOS, Linux, Mac): those machines don't have the fonts a book was
/// written in, so every font used is drawn here, letter by letter, into pictures in an encrypted archive beside the game's other files.
/// Also the game's own two fonts when no sprite font was drawn for them (Windows then uses a system font; the player had only a plain
/// built-in one). Player/src/font.c reads "name/N.png", N the character minus 32, exactly as a sprite font's frames.
/// </summary>
internal static class PlayerFontPack
{
    /// <summary>Glyphs are drawn this much smaller than the book's own size: its 700 x 400 canvas on a 320 x 200 screen, kept readable.</summary>
    private const double BookSizeToPixels = 0.8;
    private const double SmallestPixels = 10;
    /// <summary>The game's own fonts drawn from a system font, for a project with no sprite font for them: as Windows falls back.</summary>
    private const string FallbackFamily = "Segoe UI";
    private const double FallbackPixels = 10;

    /// <summary>A book run's key in the archive: what PlayerBookText writes into the run and books.c asks for.</summary>
    public static string BookFontKey(string family, double size) => $"{family}|{Math.Round(size, 1).ToString(CultureInfo.InvariantCulture)}";

    /// <summary>A book's 700 x 400 canvas on the game's 320 x 200: a font this much smaller, as the Windows engine fits a book to the view.</summary>
    private const double BookCanvasToScreen = 320.0 / 700.0;
    /// <summary>The picture's own fineness on a HIRES machine (Player/src/platform.h): a font is drawn for each, its pixels the picture's.</summary>
    private static readonly int[] HiresDensities = { 2, 3, 4 };

    /// <param name="hires">For MorphOS: drawn for its finer picture, true to the book's own sizes. Otherwise (the Amiga 68k): for 320 x 200, kept readable there.</param>
    public static void Write(string folder, Guid id, IEnumerable<(string Family, double Size)> bookFonts, SpriteDatabase sprites, bool hires)
    {
        var files = new List<(string Name, byte[] Data)>();
        var fonts = bookFonts.Distinct().ToList();
        var fallbacks = new[] { SpriteDatabase.InGameFontName, SpriteDatabase.DialogFontName }
            .Where(name => sprites.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.FontsGroupName)?.Sprites.FirstOrDefault(s => s.Name == name) is not { Frames.Count: > 0 }).ToList();
        if (hires)
            foreach (int density in HiresDensities)
            {
                foreach (var (family, size) in fonts) AddFont(files, $"{BookFontKey(family, size)}@{density}", family, Math.Max(density * 4, Math.Round(size * BookCanvasToScreen * density)));
                foreach (string name in fallbacks) AddFont(files, $"{name}@{density}", FallbackFamily, FallbackPixels * density);
            }
        else
        {
            foreach (var (family, size) in fonts) AddFont(files, BookFontKey(family, size), family, Math.Max(SmallestPixels, Math.Round(size * BookSizeToPixels)));
            foreach (string name in fallbacks) AddFont(files, name, FallbackFamily, FallbackPixels);
        }
        string path = Path.Combine(folder, "fonts.sdbx");
        if (files.Count == 0) { File.Delete(path); return; }
        PlayerPack.WriteArchive(path, id, files, null);
    }

    private static void AddFont(List<(string Name, byte[] Data)> files, string key, string family, double pixels)
    {
        var typeface = new Typeface(new FontFamily(family), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
        // One height for all, so every letter stands on the same line.
        int height = (int)Math.Ceiling(new FormattedText("Ag", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, pixels, Brushes.White, 1).Height);
        for (int code = 32; code <= 126; code++)
        {
            var text = new FormattedText(((char)code).ToString(), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, pixels, Brushes.White, 1) { };
            int width = Math.Max(1, (int)Math.Ceiling(text.WidthIncludingTrailingWhitespace));
            files.Add(($"{key}/{code - 32}.png", Glyph(text, width, height)));
        }
    }

    /// <summary>The letter in hard pixels, white, each one on or off: the player tints it and draws no half-see-through edges.</summary>
    private static byte[] Glyph(FormattedText text, int width, int height)
    {
        var visual = new DrawingVisual();
        TextOptions.SetTextFormattingMode(visual, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Aliased);
        using (var dc = visual.RenderOpen()) dc.DrawText(text, new Point(0, 0));
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(pixels, width * 4, 0);
        for (int i = 0; i < pixels.Length; i += 4)
        {
            byte value = pixels[i + 3] >= 128 ? (byte)255 : (byte)0;
            pixels[i] = pixels[i + 1] = pixels[i + 2] = pixels[i + 3] = value;
        }
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4)));
        using var memory = new MemoryStream();
        encoder.Save(memory);
        return memory.ToArray();
    }
}
