using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>Everything one dialog box shows.</summary>
internal sealed record DialogBoxContent(string Title, string Text, BitmapSource? Face, bool FaceRight, string Position, bool Dim, SpriteEntry? Frame, IReadOnlyList<string> Replies);

/// <summary>A dialog box drawn on a layer: where its replies are (for clicks and hover), and its frame (stop it when the box goes).</summary>
internal sealed class DialogBoxDrawing
{
    public DialogFrameVisual? Frame;
    public readonly List<Rect> ReplyRects = new();
    public readonly List<Rectangle> ReplyHighlights = new();

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

    public static DialogBoxDrawing Draw(Canvas layer, DialogBoxContent content, SpriteDatabase sprites, ResourceDatabase? resources)
    {
        var drawing = new DialogBoxDrawing();
        const double margin = DialogBoxMetrics.Margin, padding = DialogBoxMetrics.Padding;
        double boxWidth = DialogBoxMetrics.Width;
        var inset = DialogBoxMetrics.Inset(content.Frame);

        Image? face = null;
        if (content.Face is { } faceBitmap)
        {
            double scale = Math.Min(1, Math.Min(DialogBoxMetrics.FaceMax / faceBitmap.PixelWidth, DialogBoxMetrics.FaceMax / faceBitmap.PixelHeight));
            face = new Image { Source = faceBitmap, Width = faceBitmap.PixelWidth * scale, Height = faceBitmap.PixelHeight * scale, Stretch = Stretch.Fill };
            RenderOptions.SetBitmapScalingMode(face, BitmapScalingMode.NearestNeighbor);
        }
        double faceWidth = face is null ? 0 : face.Width + padding;
        double textWidth = Math.Max(16, boxWidth - inset.Left - inset.Right - faceWidth);

        var font = SpriteFont.For(sprites, SpriteDatabase.DialogFontName);
        FrameworkElement Words(string text, bool bold = false)
        {
            if (font?.RenderText(text, 1, null, textWidth) is { } bitmap)
            {
                var image = new Image { Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Stretch = Stretch.None };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                return image;
            }
            // A small system font until the DialogFont has letters.
            var block = new TextBlock { Text = text, Width = textWidth, TextWrapping = TextWrapping.Wrap, Foreground = Brushes.White, FontSize = 8, FontWeight = bold ? FontWeights.Bold : FontWeights.Normal };
            block.Measure(new Size(textWidth, double.PositiveInfinity));
            block.Height = block.DesiredSize.Height;
            return block;
        }

        // Stack up the content: title, text, replies.
        var pieces = new List<(FrameworkElement Element, double Top, int Reply)>();
        double y = 0;
        if (!string.IsNullOrWhiteSpace(content.Title))
        {
            var title = Words(content.Title.Trim(), bold: true);
            pieces.Add((title, y, -1));
            y += title.Height + 3;
        }
        if (!string.IsNullOrEmpty(content.Text))
        {
            var text = Words(content.Text);
            pieces.Add((text, y, -1));
            y += text.Height;
        }
        if (content.Replies.Count > 0) y += 4;
        for (int i = 0; i < content.Replies.Count; i++)
        {
            var reply = Words(content.Replies[i]);
            pieces.Add((reply, y, i));
            y += reply.Height + 2;
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
        foreach (var (element, pieceTop, replyIndex) in pieces)
        {
            if (replyIndex >= 0)
            {
                // The reply's line: what the mouse hits, lit up under the pointer.
                var rect = new Rect(left - 2, top + pieceTop - 1, textWidth + 4, element.Height + 2);
                var highlight = new Rectangle { Width = rect.Width, Height = rect.Height, Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), Visibility = Visibility.Hidden, IsHitTestVisible = false };
                Canvas.SetLeft(highlight, rect.X);
                Canvas.SetTop(highlight, rect.Y);
                layer.Children.Add(highlight);
                drawing.ReplyRects.Add(rect);
                drawing.ReplyHighlights.Add(highlight);
            }
            if (top + pieceTop > boxTop + boxHeight) continue; // past the bottom of a full-screen box
            Canvas.SetLeft(element, left);
            Canvas.SetTop(element, top + pieceTop);
            layer.Children.Add(element);
        }
        return drawing;
    }
}
