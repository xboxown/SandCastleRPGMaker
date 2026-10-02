using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A graphical font made from a font sprite in the Fonts group (InGameFont
/// or DialogFont): frame N is the glyph for (char)(32 + N), the font-sheet
/// order. Text is drawn glyph by glyph the way Exult draws its fonts.vga
/// fonts - no system font involved. Glyphs with transparent edges are
/// trimmed to their ink and get 1px between letters (a proportional font,
/// like Exult's); glyphs with no transparency at all are drawn as whole
/// cells (monospaced).
///
/// Loaded fonts are cached per sprite and rebuilt automatically when the
/// sprite's frames change (added, removed, redrawn).
/// </summary>
internal sealed class SpriteFont
{
    private sealed class Glyph
    {
        public required BitmapSource Ink { get; init; }
        public required int InkWidth { get; init; }
        public required int Advance { get; init; }
    }

    /// <summary>One character to draw, with its own size and colour - a run of book text keeps its own.</summary>
    public readonly record struct StyledChar(char Ch, int Scale, Brush? Tint, bool Bold);

    private readonly Dictionary<char, Glyph> _glyphs = new();

    /// <summary>The font's line height in pixels at scale 1 (the tallest glyph cell).</summary>
    public int CellHeight { get; private set; }
    public int SpaceWidth { get; private set; }

    private static readonly Dictionary<SpriteEntry, (string Signature, SpriteFont Font)> Cache = new(ReferenceEqualityComparer.Instance);

    /// <summary>The named font from the project's Fonts group, or null when it has no glyphs yet (callers then fall back to ordinary text).</summary>
    public static SpriteFont? For(SpriteDatabase sprites, string fontName)
    {
        var entry = sprites.FindFont(fontName);
        return entry is null ? null : FromEntry(entry);
    }

    /// <summary>The font a font sprite makes, or null while it has no glyphs.</summary>
    public static SpriteFont? FromEntry(SpriteEntry entry)
    {
        if (entry.Frames.Count == 0) return null;

        string signature = string.Join("|", entry.Frames.Select(f =>
            f.FilePath + ":" + (File.Exists(f.FilePath) ? File.GetLastWriteTimeUtc(f.FilePath).Ticks : 0)));
        if (Cache.TryGetValue(entry, out var cached) && cached.Signature == signature) return cached.Font;

        var font = Build(entry);
        if (font is null) { Cache.Remove(entry); return null; }
        Cache[entry] = (signature, font);
        return font;
    }

    /// <summary>The in-game font of the open project (InGameFont), or null.</summary>
    public static SpriteFont? InGame => For(ProjectSession.Sprites, SpriteDatabase.InGameFontName);

    private static SpriteFont? Build(SpriteEntry entry)
    {
        var font = new SpriteFont();
        var inkWidths = new List<int>();
        int maxCellWidth = 0;
        for (int i = 0; i < entry.Frames.Count; i++)
        {
            if (SpriteDatabase.FontCharForFrame(i) is not char ch) break;
            var bitmap = LoadPixels(entry.Frames[i].FilePath);
            if (bitmap is null) continue;
            font.CellHeight = Math.Max(font.CellHeight, bitmap.PixelHeight);
            maxCellWidth = Math.Max(maxCellWidth, bitmap.PixelWidth);

            var (left, width, hasTransparency) = InkColumns(bitmap);
            if (!hasTransparency) { left = 0; width = bitmap.PixelWidth; } // a solid cell: monospaced, drawn whole
            BitmapSource ink = width > 0 ? new CroppedBitmap(bitmap, new Int32Rect(left, 0, width, bitmap.PixelHeight)) : bitmap;
            ink.Freeze();
            font._glyphs[ch] = new Glyph { Ink = ink, InkWidth = width, Advance = width + (hasTransparency && width > 0 ? 1 : 0) };
            if (width > 0 && hasTransparency && ch != ' ') inkWidths.Add(width);
        }
        if (font._glyphs.Count == 0 || font.CellHeight == 0) return null;

        // A blank space glyph (the usual case) has no ink to measure: give it a width in proportion to the letters.
        font.SpaceWidth = font._glyphs.TryGetValue(' ', out var space) && space.InkWidth > 0
            ? space.Advance
            : Math.Max(2, (int)Math.Round((inkWidths.Count > 0 ? inkWidths.Average() : maxCellWidth / 2.0) * 0.6) + 1);
        return font;
    }

    private static BitmapSource? LoadPixels(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.EndInit();
            var converted = new FormatConvertedBitmap(image, PixelFormats.Pbgra32, null, 0);
            converted.Freeze();
            return converted;
        }
        catch { return null; }
    }

    /// <summary>The span of columns holding any visible pixel, and whether the cell has any transparency at all.</summary>
    private static (int Left, int Width, bool HasTransparency) InkColumns(BitmapSource bitmap)
    {
        int w = bitmap.PixelWidth, h = bitmap.PixelHeight;
        var pixels = new byte[w * h * 4];
        bitmap.CopyPixels(pixels, w * 4, 0);
        int left = w, right = -1;
        bool transparent = false;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                byte alpha = pixels[(y * w + x) * 4 + 3];
                if (alpha == 0) { transparent = true; continue; }
                if (x < left) left = x;
                if (x > right) right = x;
            }
        return right < 0 ? (0, 0, true) : (left, right - left + 1, transparent);
    }

    private Glyph? GlyphFor(char ch)
    {
        if (_glyphs.TryGetValue(ch, out var glyph)) return glyph;
        if (_glyphs.TryGetValue(char.ToUpperInvariant(ch), out glyph)) return glyph; // a sheet with capitals only still writes lowercase
        if (_glyphs.TryGetValue(char.ToLowerInvariant(ch), out glyph)) return glyph;
        return _glyphs.GetValueOrDefault('?');
    }

    private int AdvanceOf(StyledChar c)
    {
        if (c.Ch == ' ' || c.Ch == '\t') return SpaceWidth * c.Scale * (c.Ch == '\t' ? 4 : 1);
        var glyph = GlyphFor(c.Ch);
        int advance = glyph is null ? SpaceWidth : glyph.Advance;
        return advance * c.Scale + (c.Bold ? c.Scale : 0);
    }

    // ---------- Layout ----------

    private sealed class Line
    {
        public readonly List<StyledChar> Chars = new();
        public int Width;
        public int Height;
    }

    /// <summary>Word-wraps the characters into lines no wider than maxWidth ('\n' forces a break).</summary>
    private List<Line> Wrap(IReadOnlyList<StyledChar> chars, double maxWidth)
    {
        var lines = new List<Line>();
        var line = new Line();
        int i = 0;
        while (i < chars.Count)
        {
            var c = chars[i];
            if (c.Ch == '\n') { lines.Add(line); line = new Line(); i++; continue; }

            // A word, or a stretch of spaces, is placed as one piece.
            bool isSpace = char.IsWhiteSpace(c.Ch);
            int j = i;
            int pieceWidth = 0;
            while (j < chars.Count && chars[j].Ch != '\n' && char.IsWhiteSpace(chars[j].Ch) == isSpace)
            {
                pieceWidth += AdvanceOf(chars[j]);
                j++;
            }

            if (isSpace)
            {
                if (line.Chars.Count > 0) // spaces at the start of a wrapped line are dropped
                    for (int k = i; k < j; k++) Append(line, chars[k]);
            }
            else if (line.Chars.Count == 0 || line.Width + pieceWidth <= maxWidth)
            {
                if (pieceWidth > maxWidth && line.Chars.Count == 0)
                {
                    // A word wider than the whole line: broken wherever it runs out of room.
                    for (int k = i; k < j; k++)
                    {
                        if (line.Chars.Count > 0 && line.Width + AdvanceOf(chars[k]) > maxWidth) { lines.Add(line); line = new Line(); }
                        Append(line, chars[k]);
                    }
                }
                else
                {
                    for (int k = i; k < j; k++) Append(line, chars[k]);
                }
            }
            else
            {
                TrimTrailingSpaces(line);
                lines.Add(line);
                line = new Line();
                continue; // place the same word again on the fresh line
            }
            i = j;
        }
        TrimTrailingSpaces(line);
        lines.Add(line);
        foreach (var l in lines)
            l.Height = l.Chars.Count == 0 ? CellHeight * (chars.Count > 0 ? chars[0].Scale : 1) : l.Chars.Max(ch => CellHeight * ch.Scale);
        return lines;
    }

    private void Append(Line line, StyledChar c)
    {
        line.Chars.Add(c);
        line.Width += AdvanceOf(c);
    }

    private void TrimTrailingSpaces(Line line)
    {
        while (line.Chars.Count > 0 && char.IsWhiteSpace(line.Chars[^1].Ch))
        {
            line.Width -= AdvanceOf(line.Chars[^1]);
            line.Chars.RemoveAt(line.Chars.Count - 1);
        }
    }

    /// <summary>The height a block of text takes when wrapped to maxWidth.</summary>
    public double Measure(IReadOnlyList<StyledChar> chars, double maxWidth) => Wrap(chars, maxWidth).Sum(l => l.Height);

    /// <summary>
    /// Draws a wrapped block of text with its top-left at origin and returns
    /// its height. Glyphs sit on a shared bottom line, so mixed sizes line up.
    /// Drawn through a DrawingGroup so scaled glyphs stay sharp pixels.
    /// </summary>
    public double Draw(DrawingContext target, IReadOnlyList<StyledChar> chars, Point origin, double maxWidth, TextAlignment alignment = TextAlignment.Left)
    {
        var lines = Wrap(chars, maxWidth);
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
        double y = origin.Y;
        using (var dc = group.Open())
        {
            foreach (var line in lines)
            {
                double x = origin.X + alignment switch
                {
                    TextAlignment.Center => Math.Floor((maxWidth - line.Width) / 2),
                    TextAlignment.Right => maxWidth - line.Width,
                    _ => 0
                };
                foreach (var c in line.Chars)
                {
                    if (!char.IsWhiteSpace(c.Ch) && GlyphFor(c.Ch) is { InkWidth: > 0 } glyph)
                    {
                        double h = glyph.Ink.PixelHeight * c.Scale;
                        var rect = new Rect(x, y + line.Height - h, glyph.InkWidth * c.Scale, h);
                        DrawGlyph(dc, glyph.Ink, rect, c.Tint);
                        if (c.Bold) { rect.X += c.Scale; DrawGlyph(dc, glyph.Ink, rect, c.Tint); }
                    }
                    x += AdvanceOf(c);
                }
                y += line.Height;
            }
        }
        target.DrawDrawing(group);
        return y - origin.Y;
    }

    private static void DrawGlyph(DrawingContext dc, BitmapSource ink, Rect rect, Brush? tint)
    {
        if (tint is null) { dc.DrawImage(ink, rect); return; }
        // Coloured text: the glyph's shape cut out of the colour.
        var mask = new ImageBrush(ink);
        RenderOptions.SetBitmapScalingMode(mask, BitmapScalingMode.NearestNeighbor);
        dc.PushOpacityMask(mask);
        dc.DrawRectangle(tint, null, rect);
        dc.Pop();
    }

    public static List<StyledChar> Plain(string text, int scale = 1, Brush? tint = null, bool bold = false) =>
        text.Replace("\r\n", "\n").Select(ch => new StyledChar(ch, scale, tint, bold)).ToList();

    /// <summary>A line (or wrapped block) of text as one picture, sized to fit exactly - for labels. Null for empty text.</summary>
    public BitmapSource? RenderText(string text, int scale = 1, Brush? tint = null, double maxWidth = 100000)
    {
        if (string.IsNullOrEmpty(text)) return null;
        var chars = Plain(text, scale, tint);
        var lines = Wrap(chars, maxWidth);
        int width = Math.Max(1, lines.Max(l => l.Width));
        int height = Math.Max(1, lines.Sum(l => l.Height));
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen()) Draw(dc, chars, new Point(0, 0), width);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }
}
