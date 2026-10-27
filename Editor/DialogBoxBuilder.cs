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
    /// <summary>The font the words (not the title or replies) show in - a language not understood (System Settings > Languages); null: the DialogFont.</summary>
    public string? WordsFont { get; init; }
    /// <summary>The replies in a box of their own under the speaker's, with ReplyFace (the main character's) - as Exult (System Settings > Conversations).</summary>
    public bool RepliesApart { get; init; }
    public BitmapSource? ReplyFace { get; init; }
}

/// <summary>A dialog box drawn on a layer: where its replies are (for clicks and hover), and its frame (stop it when the box goes).</summary>
internal sealed class DialogBoxDrawing
{
    public const int More = -1, Previous = -2;

    /// <summary>The frames drawn (two with the replies in a box of their own).</summary>
    public readonly List<DialogFrameVisual> Frames = new();
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

    public void Stop() => Frames.ForEach(f => f.Stop());
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

    /// <summary>The editor's previews (Show Text, Conversation Test): the replies where the project's System Settings put them, with the test player's face.</summary>
    public static DialogBoxContent AsProjectShowsIt(DialogBoxContent content)
    {
        if (ProjectSession.SettingsProvider?.Invoke()?.Conversation.RepliesUnderMainCharacter != true) return content;
        var player = ProjectSession.Characters.Characters.FirstOrDefault(c => c.IsTestPlayer);
        return content with { RepliesApart = true, ReplyFace = player is null ? null : ProjectSession.FirstFrame(ProjectSession.FindSprite(player.FaceSpriteGroup, player.FaceSpriteName)) };
    }

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
        var font = (content.WordsFont is { Length: > 0 } words ? SpriteFont.For(sprites, words) : null) ?? SpriteFont.For(sprites, SpriteDatabase.DialogFontName);
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

    /// <summary>One box, measured: its face, what's in it (reply spots numbered in the drawing) and its height.</summary>
    private sealed record BoxPlan(DialogBoxContent Content, Image? Face, List<(FrameworkElement Element, double Left, double Top, int Reply)> Pieces, double Height);

    /// <summary>
    /// Draws the box. With RepliesApart and replies to pick, as Exult's
    /// conversation: the speaker's box with their words, and under it a box
    /// of its own with the main character's face and the replies (Exult's
    /// avatar face and choices) - the two kept together at the box's Position.
    /// </summary>
    public static DialogBoxDrawing Draw(Canvas layer, DialogBoxContent content, SpriteDatabase sprites, ResourceDatabase? resources)
    {
        var drawing = new DialogBoxDrawing();
        const double margin = DialogBoxMetrics.Margin;
        if (content.Dim)
            layer.Children.Add(new Border { Width = DialogBoxMetrics.ScreenWidth, Height = DialogBoxMetrics.ScreenHeight, Background = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0)), IsHitTestVisible = false });

        if (!content.RepliesApart || content.Replies.Count == 0)
        {
            var plan = Measure(content, sprites, drawing, DialogBoxMetrics.ScreenHeight - 2 * margin);
            Place(layer, drawing, plan, TopFor(content.Position, plan.Height), resources);
            return drawing;
        }

        bool speaks = !string.IsNullOrWhiteSpace(content.Title) || !string.IsNullOrEmpty(content.Text);
        var replies = Measure(content with { Title = "", Text = "", Face = content.ReplyFace }, sprites, drawing, DialogBoxMetrics.ScreenHeight - 2 * margin);
        if (!speaks)
        {
            Place(layer, drawing, replies, TopFor(content.Position, replies.Height), resources);
            return drawing;
        }
        var words = Measure(content with { Replies = Array.Empty<string>() }, sprites, drawing,
            Math.Max(DialogBoxMetrics.MinHeight, DialogBoxMetrics.ScreenHeight - 3 * margin - replies.Height));
        double top = TopFor(content.Position, words.Height + margin + replies.Height);
        Place(layer, drawing, words, top, resources);
        Place(layer, drawing, replies, top + words.Height + margin, resources);
        return drawing;
    }

    private static double TopFor(string position, double height) => position switch
    {
        "Top" => DialogBoxMetrics.Margin,
        "Middle" => (DialogBoxMetrics.ScreenHeight - height) / 2,
        _ => DialogBoxMetrics.ScreenHeight - DialogBoxMetrics.Margin - height
    };

    private static BoxPlan Measure(DialogBoxContent content, SpriteDatabase sprites, DialogBoxDrawing drawing, double maxHeight)
    {
        var inset = DialogBoxMetrics.Inset(content.Frame);
        Image? face = null;
        if (content.Face is { } faceBitmap)
        {
            double faceW = FaceWidth(faceBitmap);
            face = new Image { Source = faceBitmap, Width = faceW, Height = faceBitmap.PixelHeight * (faceW / faceBitmap.PixelWidth), Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.NearestNeighbor);
        }
        double textWidth = TextWidth(content);

        var font = SpriteFont.For(sprites, SpriteDatabase.DialogFontName);
        var wordsFont = (content.WordsFont is { Length: > 0 } words ? SpriteFont.For(sprites, words) : null) ?? font; // a language not understood: its font
        FrameworkElement Words(string text, bool bold = false, bool natural = false, SpriteFont? useFont = null)
        {
            if ((useFont ?? font)?.RenderText(text, 1, null, textWidth) is { } bitmap)
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
            var text = Words(content.Text, useFont: wordsFont);
            pieces.Add((text, 0, y, -1));
            y += text.Height;
        }
        if (content.Replies.Count > 0)
        {
            // Left to right, row under row (Exult show_avatar_choices), More / Previous past five rows of five.
            if (y > 0) y += 4;
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
        double height = Math.Min(maxHeight, Math.Max(DialogBoxMetrics.MinHeight, Math.Max(face?.Height ?? 0, y) + inset.Top + inset.Bottom));
        return new BoxPlan(content, face, pieces, height);
    }

    private static void Place(Canvas layer, DialogBoxDrawing drawing, BoxPlan plan, double boxTop, ResourceDatabase? resources)
    {
        const double margin = DialogBoxMetrics.Margin, padding = DialogBoxMetrics.Padding;
        double boxWidth = DialogBoxMetrics.Width, boxHeight = plan.Height;
        var content = plan.Content;
        var inset = DialogBoxMetrics.Inset(content.Frame);
        var face = plan.Face;

        if (content.Frame is { } frameSprite)
        {
            var frame = new DialogFrameVisual(frameSprite, boxWidth, boxHeight, resources);
            Canvas.SetLeft(frame, margin);
            Canvas.SetTop(frame, boxTop);
            layer.Children.Add(frame);
            drawing.Frames.Add(frame);
        }

        if (face is not null)
        {
            Canvas.SetLeft(face, content.FaceRight ? margin + boxWidth - inset.Right - face.Width : margin + inset.Left);
            Canvas.SetTop(face, boxTop + inset.Top);
            layer.Children.Add(face);
        }

        double faceWidth = face is null ? 0 : face.Width + padding;
        double left = margin + inset.Left + (content.FaceRight ? 0 : faceWidth);
        double top = boxTop + inset.Top;
        foreach (var (element, pieceLeft, pieceTop, replyIndex) in plan.Pieces)
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
    }
}
