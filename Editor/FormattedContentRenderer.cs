using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Renders book page/frame content as pure graphics - a bitmap for the
/// text, separate live-animated Image elements for embedded sprites -
/// instead of ever displaying it through a RichTextBox. This mirrors how
/// the real game engine will actually work (Ultima VII/VIII draw glyphs
/// and sprites directly onto the screen; there's no "text control" concept
/// at all in an SDL/OpenGL engine), and it sidesteps the entire category
/// of bug the RichTextBox-based display surfaces kept hitting - Floater
/// layout not resolving outside a live visual tree, IsHitTestVisible
/// blocking tooltips, a Margin property that never resolves - by simply
/// never asking RichTextBox to lay out or render anything here.
///
/// RichTextBox is still used, but only as a PARSER: reading the stored
/// XAML's paragraph/run/Floater structure is exactly what it's for, and
/// that's the same data model authoring and saving already produce.
/// What's replaced is RichTextBox's OWN layout/measure/render pipeline -
/// paragraphs are laid out here via FormattedText, a low-level WPF text
/// primitive with none of RichTextBox's editing or live-tree requirements.
///
/// The editing popup (BookPageTextPopup) is deliberately untouched by any
/// of this - it's a real text editor (cursor, selection, dragging to
/// resize a picture) and stays RichTextBox for exactly that reason.
/// </summary>
internal static class FormattedContentRenderer
{
    public sealed class SpriteBox
    {
        public required SpriteEntry Sprite { get; init; }
        public required Rect Bounds { get; init; }
    }

    public sealed class RenderResult
    {
        public required BitmapSource? TextBitmap { get; init; }
        public required List<SpriteBox> Sprites { get; init; }
        public required double TotalHeight { get; init; }
    }

    /// <summary>Fixed, explicit spacing WE control between paragraphs - no ambiguous WPF Margin property to misread, unlike everything the pagination-measurement saga fought with.</summary>
    private const double ParagraphSpacing = 10;

    public static RenderResult Render(string xaml, double width, double height, SpriteDatabase sprites)
    {
        var spriteBoxes = new List<SpriteBox>();
        if (width < 1 || height < 1) return new RenderResult { TextBitmap = null, Sprites = spriteBoxes, TotalHeight = 0 };

        var scratch = new RichTextBox { FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Foreground = Brushes.Black };
        RichTextXamlHelper.Load(scratch, xaml);

        double cursorY = 0;
        var visual = new DrawingVisual();
        var font = SpriteFont.For(sprites, SpriteDatabase.InGameFontName);
        using (var dc = visual.RenderOpen())
        {
            LayoutBlocks(scratch.Document.Blocks, dc, ref cursorY, width, sprites, spriteBoxes, font);
        }

        var rtb = new RenderTargetBitmap((int)Math.Ceiling(width), (int)Math.Ceiling(height), 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();

        return new RenderResult { TextBitmap = rtb, Sprites = spriteBoxes, TotalHeight = cursorY };
    }

    /// <summary>
    /// The height book content takes when drawn with the project's InGameFont
    /// at this width - what pagination measures with whenever that font has
    /// glyphs, so pages break exactly where the drawn text runs out of room.
    /// Null when there is no font (pagination then measures as before).
    /// </summary>
    public static double? MeasureWithInGameFont(string xaml, double width)
    {
        var sprites = ProjectSession.Sprites;
        var font = SpriteFont.For(sprites, SpriteDatabase.InGameFontName);
        if (font is null) return null;
        var scratch = new RichTextBox { FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Foreground = Brushes.Black };
        RichTextXamlHelper.Load(scratch, xaml);
        double cursorY = 0;
        LayoutBlocks(scratch.Document.Blocks, null, ref cursorY, width, sprites, new List<SpriteBox>(), font);
        return Math.Max(0, cursorY - ParagraphSpacing); // no spacing is needed after the last paragraph
    }

    private static void LayoutBlocks(IEnumerable<Block> blocks, DrawingContext? dc, ref double cursorY, double width, SpriteDatabase sprites, List<SpriteBox> spriteBoxes, SpriteFont? font)
    {
        foreach (var block in blocks)
        {
            if (block is Section section)
            {
                LayoutBlocks(section.Blocks, dc, ref cursorY, width, sprites, spriteBoxes, font);
                continue;
            }

            if (block is not Paragraph para) continue;

            var floater = para.Inlines.OfType<Floater>().FirstOrDefault();
            if (floater is not null && LiveSpriteEmbedding.TryResolveSprite(floater, sprites, out var sprite, out _) && sprite is not null)
            {
                var (boxWidth, boxHeight) = GetSpriteDisplaySize(sprite);
                spriteBoxes.Add(new SpriteBox { Sprite = sprite, Bounds = new Rect(0, cursorY, boxWidth, boxHeight) });
                cursorY += boxHeight + ParagraphSpacing;
                continue;
            }

            if (font is not null)
            {
                // The game's own graphical font: each run keeps its colour and size (12 = the font's own pixels, 24 = twice that...).
                var chars = new List<SpriteFont.StyledChar>();
                CollectStyledChars(para.Inlines, chars);
                if (chars.Count == 0 || chars.All(c => char.IsWhiteSpace(c.Ch))) { cursorY += ParagraphSpacing; continue; }
                double height = dc is null ? font.Measure(chars, width) : font.Draw(dc, chars, new Point(0, cursorY), width, para.TextAlignment);
                cursorY += height + ParagraphSpacing;
                continue;
            }

            var ft = BuildFormattedText(para, width);
            if (ft is not null)
            {
                dc?.DrawText(ft, new Point(0, cursorY));
                cursorY += ft.Height + ParagraphSpacing;
            }
            else
            {
                cursorY += ParagraphSpacing; // a blank paragraph still takes up a blank line's worth of space
            }
        }
    }

    /// <summary>
    /// Direct visibility into the actual document structure for a given
    /// frame's XAML, instead of guessing again at why a sprite isn't
    /// showing - lists every paragraph, every Floater found inside one,
    /// the exact inner block types WPF actually produced, the Image's
    /// actual Source file path, and whether that path resolves to a real
    /// sprite in the database.
    /// </summary>
    public static string DiagnoseSpriteDetection(string xaml, SpriteDatabase sprites)
    {
        var scratch = new RichTextBox();
        RichTextXamlHelper.Load(scratch, xaml);
        var sb = new System.Text.StringBuilder();
        int paraIndex = 0;
        DiagnoseBlocks(scratch.Document.Blocks, sprites, sb, ref paraIndex);
        return sb.Length > 0 ? sb.ToString() : "(no blocks at all in this content)";
    }

    private static void DiagnoseBlocks(IEnumerable<Block> blocks, SpriteDatabase sprites, System.Text.StringBuilder sb, ref int paraIndex)
    {
        foreach (var block in blocks)
        {
            if (block is Section section)
            {
                sb.AppendLine("[Section]");
                DiagnoseBlocks(section.Blocks, sprites, sb, ref paraIndex);
                continue;
            }

            if (block is not Paragraph para)
            {
                sb.AppendLine($"[{block.GetType().Name}] (not a Paragraph, skipped entirely by the renderer)");
                continue;
            }

            paraIndex++;
            var floaters = para.Inlines.OfType<Floater>().ToList();
            if (floaters.Count == 0)
            {
                string text = string.Concat(para.Inlines.OfType<Run>().Select(r => r.Text));
                sb.AppendLine($"Paragraph {paraIndex}: no Floater - text=\"{(text.Length > 40 ? text[..40] + "..." : text)}\"");
                continue;
            }

            foreach (var floater in floaters)
            {
                sb.AppendLine($"Paragraph {paraIndex}: Floater found, {floater.Blocks.Count} inner block(s):");
                foreach (var innerBlock in floater.Blocks)
                {
                    if (innerBlock is BlockUIContainer buc)
                    {
                        sb.AppendLine($"  - BlockUIContainer, Child={buc.Child?.GetType().Name ?? "NULL"}" +
                                      (buc.Child is Image img1 ? $", Source path=\"{(img1.Source as BitmapImage)?.UriSource?.OriginalString ?? "NULL"}\"" : string.Empty));
                    }
                    else if (innerBlock is Paragraph innerPara)
                    {
                        var iucs = innerPara.Inlines.OfType<InlineUIContainer>().ToList();
                        sb.AppendLine($"  - Paragraph wrapper, {innerPara.Inlines.Count} inline(s), {iucs.Count} InlineUIContainer(s)");
                        foreach (var iuc in iucs)
                            sb.AppendLine($"    InlineUIContainer Child={iuc.Child?.GetType().Name ?? "NULL"}" +
                                          (iuc.Child is Image img2 ? $", Source path=\"{(img2.Source as BitmapImage)?.UriSource?.OriginalString ?? "NULL"}\"" : string.Empty));
                    }
                    else
                    {
                        sb.AppendLine($"  - {innerBlock.GetType().Name} (neither form the detector checks!)");
                    }
                }

                bool found = LiveSpriteEmbedding.TryResolveSprite(floater, sprites, out var resolvedSprite, out _);
                sb.AppendLine(found ? $"  -> TryResolveSprite: FOUND sprite \"{resolvedSprite!.Name}\"" : "  -> TryResolveSprite: NOT FOUND (no sprite's first-frame pixel content matches this Image's displayed pixels)");
            }
        }
    }

    /// <summary>Every character of a paragraph with the size, colour and weight it was written in - Runs, line breaks, and Bold/Italic/Underline spans alike.</summary>
    private static void CollectStyledChars(InlineCollection inlines, List<SpriteFont.StyledChar> chars)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    int scale = Math.Max(1, (int)Math.Round((run.FontSize > 0 ? run.FontSize : 12) / 12.0));
                    bool bold = run.FontWeight.ToOpenTypeWeight() >= FontWeights.Bold.ToOpenTypeWeight();
                    foreach (char ch in run.Text.Replace("\r\n", "\n"))
                        chars.Add(new SpriteFont.StyledChar(ch, scale, run.Foreground ?? Brushes.Black, bold));
                    break;
                case LineBreak:
                    chars.Add(new SpriteFont.StyledChar('\n', 1, null, false));
                    break;
                case Span span:
                    CollectStyledChars(span.Inlines, chars);
                    break;
            }
        }
    }

    private static (double Width, double Height) GetSpriteDisplaySize(SpriteEntry sprite)
    {
        string path = sprite.Frames.Count > 0 ? sprite.Frames[0].FilePath : sprite.FilePath;
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            try
            {
                var frame = BitmapFrame.Create(new Uri(path, UriKind.Absolute), BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
                return (frame.PixelWidth, frame.PixelHeight);
            }
            catch { /* fall through to the default box size below */ }
        }
        return (96, 96);
    }

    private static FormattedText? BuildFormattedText(Paragraph para, double width)
    {
        var runs = para.Inlines.OfType<Run>().ToList();
        if (runs.Count == 0 || runs.All(r => r.Text.Length == 0)) return null;

        string combinedText = string.Concat(runs.Select(r => r.Text));

        var baseline = runs[0];
        var ft = new FormattedText(
            combinedText,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(baseline.FontFamily ?? new FontFamily("Segoe UI"), baseline.FontStyle, baseline.FontWeight, FontStretches.Normal),
            baseline.FontSize > 0 ? baseline.FontSize : 12,
            baseline.Foreground ?? Brushes.Black,
            1.0)
        {
            MaxTextWidth = Math.Max(1, width),
            Trimming = TextTrimming.None
        };

        int offset = 0;
        foreach (var run in runs)
        {
            int len = run.Text.Length;
            if (len > 0)
            {
                ft.SetFontWeight(run.FontWeight, offset, len);
                ft.SetFontStyle(run.FontStyle, offset, len);
                ft.SetFontSize(run.FontSize > 0 ? run.FontSize : 12, offset, len);
                if (run.Foreground is not null) ft.SetForegroundBrush(run.Foreground, offset, len);
                ft.SetFontFamily(run.FontFamily ?? new FontFamily("Segoe UI"), offset, len);
            }
            offset += len;
        }

        return ft;
    }
}
