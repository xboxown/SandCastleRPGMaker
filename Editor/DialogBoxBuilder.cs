using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>Everything one dialog box shows.</summary>
internal sealed record DialogBoxContent(string Title, string Text, BitmapSource? Face, bool FaceRight, string Position, bool Dim, SpriteEntry? Frame, IReadOnlyList<string> Replies)
{
    /// <summary>Which page of replies shows, when they need more than one (More / Previous).</summary>
    public int ReplyPage { get; init; }
}

/// <summary>A dialog box drawn on a layer: where its replies are (for clicks and hover), and its frame (stop it when the box goes).</summary>
internal sealed class DialogBoxDrawing
{
    public const int More = -1, Previous = -2;

    public DialogFrameVisual? Frame;
    public readonly List<Rect> ReplyRects = new();
    public readonly List<Rectangle> ReplyHighlights = new();
    /// <summary>What each reply spot on the box is: the reply's index in the list, or More / Previous.</summary>
    public readonly List<int> ReplySlots = new();
    /// <summary>How many pages the replies take (1 = they all fit).</summary>
    public int ReplyPageCount = 1;
    /// <summary>The page shown (the one asked for, kept in range).</summary>
    public int ReplyPage;

    public int ReplyAt(Point p) => ReplyRects.FindIndex(r => r.Contains(p));

    public void Highlight(int index)
    {
        for (int i = 0; i < ReplyHighlights.Count; i++) ReplyHighlights[i].Visibility = i == index ? Visibility.Visible : Visibility.Hidden;
    }

    public void Stop() => Frame?.Stop();
}

/// <summary>
/// Draws the Show Text dialog box - the same for the game and for the Show
/// Text window's preview: the DialogFrame sprite (or none), the speaker's
/// face, an optional title, the text and the replies, all in the DialogFont.
/// </summary>
internal static class DialogBoxBuilder
{
    public const string FrameKey = "Frame";
    public const string DimKey = "Dim";
    public const string TitleKey = "Title";

    /// <summary>Exult's NPC text box is four lines tall (Conversation::set_face_rect: 4 * text_height); more waits for a click.</summary>
    public const int LinesPerPage = 4;
    /// <summary>The system font's line height while the DialogFont has no letters.</summary>
    private const double FallbackLineHeight = 10;

    private static double FaceWidth(BitmapSource? faceBitmap)
    {
        if (faceBitmap is null) return 0;
        double scale = Math.Min(1, Math.Min(DialogBoxMetrics.FaceMax / faceBitmap.PixelWidth, DialogBoxMetrics.FaceMax / faceBitmap.PixelHeight));
        return faceBitmap.PixelWidth * scale;
    }

    private static double TextWidth(DialogBoxContent content)
    {
        double face = content.Face is null ? 0 : FaceWidth(content.Face) + DialogBoxMetrics.Padding;
        var inset = DialogBoxMetrics.Inset(content.Frame);
        return Math.Max(16, DialogBoxMetrics.Width - inset.Left - inset.Right - face);
    }

    private static TextBlock FallbackBlock(string text, double width, bool bold = false) => new()
    {
        Text = text, Width = width, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 8,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        LineHeight = FallbackLineHeight, LineStackingStrategy = LineStackingStrategy.BlockLineHeight
    };

    /// <summary>
    /// The box's text cut into pages of four lines, as Exult's
    /// Conversation::show_npc_message pages an NPC's text through
    /// Font::paint_text_box: a page that can't hold the rest ends after the
    /// last word on it ending in . ? ! , or " (or, with none, after its
    /// fourth line), the next page starts at the next word, and a word
    /// starting with * begins a new page. One page when it all fits.
    /// </summary>
    public static List<string> Pages(DialogBoxContent content, SpriteDatabase sprites)
    {
        var pages = new List<string>();
        string text = content.Text.Replace("\r\n", "\n");
        if (text.Trim().Length == 0) { pages.Add(content.Text); return pages; }
        double width = TextWidth(content);
        var font = SpriteFont.For(sprites, SpriteDatabase.DialogFontName);
        int Lines(string s)
        {
            if (font is not null) return font.LineCount(s, width);
            var block = FallbackBlock(s, width);
            block.Measure(new Size(width, double.PositiveInfinity));
            return (int)Math.Round(block.DesiredSize.Height / FallbackLineHeight);
        }

        // "*word": a page of its own from here (Exult skips the '*' and ends the page there).
        var parts = System.Text.RegularExpressions.Regex.Split(text, @"(?<=^|\s)\*");
        foreach (string part in parts)
        {
            string rest = part.Trim();
            while (rest.Length > 0)
            {
                if (Lines(rest) <= LinesPerPage) { pages.Add(rest); break; }
                // The word ends that still fit on the page; the last of them ending in punctuation is where Exult cuts.
                int fits = -1, punct = -1;
                for (int k = 1; k <= rest.Length; k++)
                {
                    bool wordEnd = !char.IsWhiteSpace(rest[k - 1]) && (k == rest.Length || char.IsWhiteSpace(rest[k]));
                    if (!wordEnd) continue;
                    if (Lines(rest[..k]) > LinesPerPage) break;
                    fits = k;
                    if (rest[k - 1] is '.' or '?' or '!' or ',' or '"') punct = k;
                }
                int cut = punct > 0 ? punct : fits;
                if (cut <= 0) { pages.Add(rest); break; } // one word longer than a whole page: it goes as it is
                pages.Add(rest[..cut].TrimEnd());
                rest = rest[cut..].TrimStart();
            }
        }
        if (pages.Count == 0) pages.Add("");
        return pages;
    }

    /// <summary>A Show Text's frame ("" default, "None", or a sprite name) and dimming - older commands kept these in "Background" (Window / Dim / Transparent).</summary>
    public static (string Frame, bool Dim) LookOf(EventCommand command)
    {
        if (command.Parameters.ContainsKey(FrameKey)) return (command.Get(FrameKey), command.Get(DimKey) == "True");
        return command.Get("Background") switch
        {
            "Dim" => ("None", true),
            "Transparent" => ("None", false),
            _ => ("", false)
        };
    }

    /// <summary>Replies to a row and rows to a page: past that, More and Previous.</summary>
    public const int RepliesPerRow = 5, ReplyRows = 5;

    /// <summary>
    /// The replies laid out as Exult's Conversation::show_avatar_choices
    /// does: left to right, a new row when the next one won't fit the width
    /// - here also at most five to a row and five rows, with More (and on
    /// later pages Previous) when there are more than fit. Each page: its
    /// spots (reply index or More / Previous) with their x and row.
    /// </summary>
    private static List<List<(int Slot, double X, int Row)>> LayoutReplies(IReadOnlyList<double> widths, double moreWidth, double previousWidth, double maxWidth, double gap)
    {
        List<(int Slot, double X, int Row)>? Flow(List<(int Slot, double Width)> spots)
        {
            var placed = new List<(int, double, int)>();
            double x = 0;
            int row = 0, inRow = 0;
            foreach (var (slot, width) in spots)
            {
                if (inRow > 0 && (inRow >= RepliesPerRow || x + width > maxWidth)) { row++; x = 0; inRow = 0; }
                if (row >= ReplyRows) return null;
                placed.Add((slot, x, row));
                x += width + gap;
                inRow++;
            }
            return placed;
        }
        var pages = new List<List<(int Slot, double X, int Row)>>();
        if (widths.Count == 0) { pages.Add(new()); return pages; }
        List<(int Slot, double Width)> Spots(int next, int count)
        {
            var spots = new List<(int Slot, double Width)>();
            if (pages.Count > 0) spots.Add((DialogBoxDrawing.Previous, previousWidth));
            for (int i = next; i < next + count; i++) spots.Add((i, widths[i]));
            if (next + count < widths.Count) spots.Add((DialogBoxDrawing.More, moreWidth));
            return spots;
        }
        int next = 0;
        do
        {
            // As many replies as fit on this page (with Previous / More where needed).
            List<(int Slot, double X, int Row)>? best = null;
            int taken = 0;
            for (int count = 1; next + count <= widths.Count; count++)
            {
                if (Flow(Spots(next, count)) is not { } placed) break;
                best = placed;
                taken = count;
            }
            if (best is null) { best = Spots(next, 1).Select((s, i) => (s.Slot, 0.0, i)).ToList(); taken = 1; } // never stuck
            pages.Add(best);
            next += taken;
        }
        while (next < widths.Count);
        return pages;
    }

    public static DialogBoxDrawing Draw(Canvas layer, DialogBoxContent content, SpriteDatabase sprites, ResourceDatabase? resources)
    {
        var drawing = new DialogBoxDrawing();
        const double margin = DialogBoxMetrics.Margin, padding = DialogBoxMetrics.Padding;
        double boxWidth = DialogBoxMetrics.Width;
        var inset = DialogBoxMetrics.Inset(content.Frame);

        Image? face = null;
        if (content.Face is { } faceBitmap)
        {
            double faceW = FaceWidth(faceBitmap);
            face = new Image { Source = faceBitmap, Width = faceW, Height = faceBitmap.PixelHeight * (faceW / faceBitmap.PixelWidth), Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.NearestNeighbor);
        }
        double faceWidth = face is null ? 0 : face.Width + padding;
        double textWidth = TextWidth(content);

        var font = SpriteFont.For(sprites, SpriteDatabase.DialogFontName);
        FrameworkElement Words(string text, bool bold = false, bool natural = false)
        {
            if (font?.RenderText(text, 1, null, textWidth) is { } bitmap)
            {
                var image = new Image { Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Stretch = Stretch.None };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                return image;
            }
            // A small system font until the DialogFont has letters.
            var block = FallbackBlock(text, textWidth, bold);
            if (natural)
            {
                // A reply: as wide as its words (up to the whole line).
                block.Width = double.NaN;
                block.MaxWidth = textWidth;
                block.Measure(new Size(textWidth, double.PositiveInfinity));
                block.Width = Math.Ceiling(block.DesiredSize.Width);
            }
            block.Measure(new Size(textWidth, double.PositiveInfinity));
            block.Height = block.DesiredSize.Height;
            return block;
        }

        // Stack up the content: title, text, replies.
        var pieces = new List<(FrameworkElement Element, double Left, double Top, int Reply)>();
        double y = 0;
        if (!string.IsNullOrWhiteSpace(content.Title))
        {
            var title = Words(content.Title.Trim(), bold: true);
            pieces.Add((title, 0, y, -1));
            y += title.Height + 3;
        }
        if (!string.IsNullOrEmpty(content.Text))
        {
            var text = Words(content.Text);
            pieces.Add((text, 0, y, -1));
            y += text.Height;
        }
        if (content.Replies.Count > 0)
        {
            // Left to right, row under row (Exult show_avatar_choices), More / Previous past five rows of five.
            y += 4;
            var replyWords = content.Replies.Select(r => Words(r, natural: true)).ToList();
            var more = Words("More", natural: true);
            var previous = Words("Previous", natural: true);
            double gap = Math.Max(6, 2 * (font?.SpaceWidth ?? 3));
            var pages = LayoutReplies(replyWords.Select(w => Math.Min(textWidth, w.Width)).ToList(), more.Width, previous.Width, textWidth, gap);
            drawing.ReplyPageCount = pages.Count;
            drawing.ReplyPage = Math.Clamp(content.ReplyPage, 0, pages.Count - 1);
            var spots = pages[drawing.ReplyPage];
            int rows = spots.Count == 0 ? 0 : spots.Max(s => s.Row) + 1;
            for (int row = 0; row < rows; row++)
            {
                double rowHeight = 0;
                foreach (var (slot, x, r) in spots)
                {
                    if (r != row) continue;
                    var element = slot switch { DialogBoxDrawing.More => more, DialogBoxDrawing.Previous => previous, _ => replyWords[slot] };
                    pieces.Add((element, x, y, drawing.ReplySlots.Count));
                    drawing.ReplySlots.Add(slot);
                    rowHeight = Math.Max(rowHeight, element.Height);
                }
                y += rowHeight + 2;
            }
        }
        double contentHeight = y;

        double boxHeight = Math.Min(DialogBoxMetrics.ScreenHeight - 2 * margin,
            Math.Max(DialogBoxMetrics.MinHeight, Math.Max(face?.Height ?? 0, contentHeight) + inset.Top + inset.Bottom));
        double boxTop = content.Position switch
        {
            "Top" => margin,
            "Middle" => (DialogBoxMetrics.ScreenHeight - boxHeight) / 2,
            _ => DialogBoxMetrics.ScreenHeight - margin - boxHeight
        };

        if (content.Dim)
            layer.Children.Add(new Border { Width = DialogBoxMetrics.ScreenWidth, Height = DialogBoxMetrics.ScreenHeight, Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), IsHitTestVisible = false });

        if (content.Frame is { } frameSprite)
        {
            drawing.Frame = new DialogFrameVisual(frameSprite, boxWidth, boxHeight, resources);
            Canvas.SetLeft(drawing.Frame, margin);
            Canvas.SetTop(drawing.Frame, boxTop);
            layer.Children.Add(drawing.Frame);
        }

        if (face is not null)
        {
            Canvas.SetLeft(face, content.FaceRight ? margin + boxWidth - inset.Right - face.Width : margin + inset.Left);
            Canvas.SetTop(face, boxTop + inset.Top);
            layer.Children.Add(face);
        }

        double left = margin + inset.Left + (content.FaceRight ? 0 : faceWidth);
        double top = boxTop + inset.Top;
        foreach (var (element, pieceLeft, pieceTop, replyIndex) in pieces)
        {
            if (replyIndex >= 0)
            {
                // The reply's words: what the mouse hits, lit up under the pointer.
                var rect = new Rect(left + pieceLeft - 2, top + pieceTop - 1, element.Width + 4, element.Height + 2);
                var highlight = new Rectangle { Width = rect.Width, Height = rect.Height, Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), Visibility = Visibility.Hidden, IsHitTestVisible = false };
                Canvas.SetLeft(highlight, rect.X);
                Canvas.SetTop(highlight, rect.Y);
                layer.Children.Add(highlight);
                drawing.ReplyRects.Add(rect);
                drawing.ReplyHighlights.Add(highlight);
            }
            if (top + pieceTop > boxTop + boxHeight) continue; // past the bottom of a full-screen box
            Canvas.SetLeft(element, left + pieceLeft);
            Canvas.SetTop(element, top + pieceTop);
            layer.Children.Add(element);
        }
        return drawing;
    }
}
