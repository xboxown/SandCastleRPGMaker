using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Book text for the Sandcastle player (Player/src/books.c): a frame's rich
/// text (a XAML package, as the Book Editor saves it) turned into plain
/// paragraphs - each one's alignment and its runs (text, size, colour,
/// bold), or a sprite placed in the text - read the way
/// FormattedContentRenderer reads them for a book's sprite font. The player
/// lays them out itself with the game's own fonts.
/// </summary>
internal static class PlayerBookText
{
    /// <summary>The fonts the converted books use, with their sizes: PlayerFontPack draws them into fonts.sdbx.</summary>
    public static readonly HashSet<(string Family, double Size)> UsedFonts = new();

    public static string Convert(string xaml, SpriteDatabase sprites)
    {
        if (string.IsNullOrEmpty(xaml)) return "[]";
        var box = new RichTextBox { FontFamily = new FontFamily("Segoe UI"), FontSize = 12, Foreground = Brushes.Black };
        try { RichTextXamlHelper.Load(box, xaml); }
        catch { return "[]"; }
        var paragraphs = new List<object>();
        Collect(box.Document.Blocks, paragraphs, sprites);
        return JsonSerializer.Serialize(paragraphs);
    }

    private static void Collect(IEnumerable<Block> blocks, List<object> into, SpriteDatabase sprites)
    {
        foreach (var block in blocks)
        {
            if (block is Section section) { Collect(section.Blocks, into, sprites); continue; }
            if (block is not Paragraph para) continue;
            var floater = para.Inlines.OfType<Floater>().FirstOrDefault();
            if (floater is not null && LiveSpriteEmbedding.TryResolveSprite(floater, sprites, out var sprite, out _) && sprite is not null)
            {
                string group = sprites.Groups.FirstOrDefault(g => g.Sprites.Contains(sprite))?.Name ?? "";
                into.Add(new { Sprite = new { SpriteGroup = group, SpriteName = sprite.Name } });
                continue;
            }
            var runs = new List<object>();
            Runs(para.Inlines, runs);
            into.Add(new { Align = para.TextAlignment.ToString(), Runs = runs });
        }
    }

    private static void Runs(InlineCollection inlines, List<object> into)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case Run run:
                    if (run.Text.Length == 0) break;
                    var colour = (run.Foreground as SolidColorBrush)?.Color ?? Colors.Black;
                    double size = run.FontSize > 0 ? run.FontSize : 12;
                    string family = run.FontFamily.Source;
                    UsedFonts.Add((family, size));
                    into.Add(new
                    {
                        Font = PlayerFontPack.BookFontKey(family, size),
                        Text = run.Text.Replace("\r\n", "\n"),
                        Size = run.FontSize > 0 ? run.FontSize : 12,
                        Color = $"#{colour.R:X2}{colour.G:X2}{colour.B:X2}",
                        Bold = run.FontWeight.ToOpenTypeWeight() >= System.Windows.FontWeights.Bold.ToOpenTypeWeight()
                    });
                    break;
                case LineBreak:
                    into.Add(new { Text = "\n", Size = 12.0, Color = "#000000", Bold = false });
                    break;
                case Span span:
                    Runs(span.Inlines, into);
                    break;
            }
        }
    }
}
