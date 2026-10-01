using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Saves/loads RichTextBox content as base64-encoded XamlPackage data (not
/// plain "Xaml" format). XamlPackage is the more robust, self-contained
/// format WPF uses internally for RichTextBox content transfer - plain Xaml
/// can silently fail to round-trip certain content when reloaded into a
/// different RichTextBox, which was the root cause of frame text not
/// reliably appearing in the live preview.
/// </summary>
internal static class RichTextXamlHelper
{
    /// <summary>
    /// A genuinely invisible character (U+200B) with real width-zero
    /// rendering, used to "anchor" an otherwise completely empty Paragraph.
    /// A Paragraph with truly zero Inlines is what a second, third, etc.
    /// consecutive Enter press produces - and multiple of those in a row
    /// don't reliably survive a Save/Load round-trip through
    /// DataFormats.XamlPackage (the first blank line works, further ones
    /// silently collapse). Giving each one a single zero-width character
    /// means it's never "empty" from XamlPackage's own perspective, so it
    /// survives - and it changes nothing about how FormattedContentRenderer
    /// measures or draws it (a zero-width character still measures at the
    /// line's real height, just draws nothing visible).
    /// </summary>
    private const string ZeroWidthAnchor = "\u200B";

    public static string Save(RichTextBox box)
    {
        AnchorEmptyParagraphs(box.Document.Blocks);
        var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
        using var stream = new MemoryStream();
        range.Save(stream, DataFormats.XamlPackage);
        return Convert.ToBase64String(stream.ToArray());
    }

    /// <summary>
    /// Recurses into Section the same way LiveSpriteEmbedding's own block
    /// walk does, for the same reason - content that's been through an
    /// extract/split round-trip isn't guaranteed to stay a flat list of
    /// Paragraphs directly under the document.
    ///
    /// ToList() here is load-bearing, not decorative: box.Document.Blocks
    /// is the LIVE collection of an attached, actively-edited RichTextBox.
    /// Modifying a Paragraph's Inlines (inside AnchorIfEmpty) can trigger
    /// WPF's own internal document restructuring, which changes the
    /// Blocks collection itself while this loop is still iterating it -
    /// "Collection was modified; enumeration operation may not execute."
    /// Iterating a snapshot instead of the live collection avoids that
    /// entirely, regardless of what WPF decides to restructure as a side
    /// effect of the edit.
    /// </summary>
    private static void AnchorEmptyParagraphs(IEnumerable<Block> blocks)
    {
        foreach (var block in blocks.ToList())
        {
            switch (block)
            {
                case Paragraph para:
                    AnchorIfEmpty(para);
                    break;
                case Section section:
                    AnchorEmptyParagraphs(section.Blocks);
                    break;
            }
        }
    }

    /// <summary>
    /// "Empty" here means exactly what FormattedContentRenderer's own
    /// BuildFormattedText treats as empty (no Runs, or every Run's Text is
    /// zero-length) - not just Inlines.Count == 0. Pressing Enter in WPF's
    /// RichTextBox typically leaves a single empty-string Run inside the
    /// new paragraph (to carry the caret's current formatting forward),
    /// not zero Inlines - checking only Inlines.Count == 0 silently missed
    /// exactly that shape, which is why the anchor never actually landed.
    /// Reuses an existing empty Run's Text property instead of replacing
    /// the Run object outright, so a caret currently anchored to it (the
    /// person may still be actively typing on this exact line) isn't
    /// disrupted.
    /// </summary>
    private static void AnchorIfEmpty(Paragraph para)
    {
        if (para.Inlines.OfType<Floater>().Any()) return; // holds a sprite - never "empty"
        var runs = para.Inlines.OfType<Run>().ToList();
        if (runs.Count > 0 && runs.Any(r => r.Text.Length > 0)) return; // has real text already

        if (runs.Count > 0) runs[0].Text = ZeroWidthAnchor;
        else para.Inlines.Add(new Run(ZeroWidthAnchor));
    }

    public static void Load(RichTextBox box, string data)
    {
        box.Document.Blocks.Clear();
        if (string.IsNullOrWhiteSpace(data)) return;

        try
        {
            byte[] bytes = Convert.FromBase64String(data);
            var range = new TextRange(box.Document.ContentStart, box.Document.ContentEnd);
            using var stream = new MemoryStream(bytes);
            range.Load(stream, DataFormats.XamlPackage);
        }
        catch
        {
            // Corrupt/empty/legacy content - leave blank rather than crash.
        }
    }
}
