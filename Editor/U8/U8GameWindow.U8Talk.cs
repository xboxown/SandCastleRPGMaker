// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 8's conversations (Pentagram's BarkGump and AskGump): what a character says floats over its head - words
/// wrapped to BarkWidth, a page at a time; a page with no answers goes on by itself after its time (its length at the
/// talk speed) or on a click - and the player's answers are a row of words over the Avatar, rows of at most AskWidth,
/// 4 apart. Neither pauses the game: the world goes on (the Avatar waits for the answer). Narration (no speaker on the
/// map) keeps the box. The clicks, the hover and the keys are the box's own (U8GameWindow.Replies.cs): the drawing fills
/// the same reply spots a box does.
/// </summary>
public partial class U8GameWindow
{
    private DispatcherTimer? _barkTimer;
    /// <summary>The words up now and whose head they float over: they follow it (ItemRelativeGump), every frame.</summary>
    private FrameworkElement? _barkWords, _barkOwner;

    /// <summary>Each frame: the words over their speaker's head, wherever it has gone.</summary>
    private void FollowU8Bark()
    {
        if (_barkWords is null || _barkOwner is null) return;
        if (_currentMessage is null || !MessageLayer.Children.Contains(_barkWords)) { _barkWords = _barkOwner = null; return; }
        var head = _barkOwner.TranslatePoint(new Point(_barkOwner.ActualWidth / 2, 0), MessageLayer);
        Canvas.SetLeft(_barkWords, Math.Clamp(head.X - _barkWords.DesiredSize.Width / 2, 0, Math.Max(0, MessageLayer.Width - _barkWords.DesiredSize.Width)));
        Canvas.SetTop(_barkWords, Math.Clamp(head.Y - _barkWords.DesiredSize.Height - 2, 0, Math.Max(0, MessageLayer.Height - _barkWords.DesiredSize.Height)));
    }

    /// <summary>The message up now drawn Ultima 8's way; false: drawn as a box (nobody to float over).</summary>
    private bool TryDrawU8Message(GameMessage message)
    {
        _barkTimer?.Stop();
        var speakerVisual = message.Speaker is { } speaker ? (speaker == _player ? _playerImage : MapCharacterOf(speaker)?.Visual) : null;
        if (speakerVisual is null || !speakerVisual.IsVisible) return false;
        var u8 = _systemSettings.Ultima8;
        var drawing = new DialogBoxDrawing();

        // The words, over the speaker's head.
        var words = new StackPanel();
        foreach (var line in WrapGameText(message.Text, u8.BarkWidth))
        {
            var label = GameTextLabel(line);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            words.Children.Add(label);
        }
        words.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var head = speakerVisual.TranslatePoint(new Point(speakerVisual.ActualWidth / 2, 0), MessageLayer);
        PlaceInLayer(words, head.X - words.DesiredSize.Width / 2, head.Y - words.DesiredSize.Height - 2);
        (_barkWords, _barkOwner) = (words, speakerVisual);

        if (message.Replies is { Count: > 0 } replies)
        {
            // The answers, over the Avatar (AskGump): rows of at most AskWidth, 4 apart.
            var avatarHead = _playerImage.TranslatePoint(new Point(_playerImage.ActualWidth / 2, 0), MessageLayer);
            var labels = replies.Select(r => { var l = GameTextLabel(r.Text); l.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return l; }).ToList();
            var rows = new List<List<FrameworkElement>> { new() };
            double rowWidth = 0;
            foreach (var l in labels)
            {
                double w = l.DesiredSize.Width;
                if (rowWidth + w > u8.AskWidth && rowWidth > 0) { rows.Add(new List<FrameworkElement>()); rowWidth = 0; }
                rows[^1].Add(l);
                rowWidth += w + 4;
            }
            double lineHeight = labels.Max(l => l.DesiredSize.Height);
            double top = avatarHead.Y - rows.Count * lineHeight - 2;
            if (ReferenceEquals(speakerVisual, _playerImage)) top -= words.DesiredSize.Height; // the Avatar's own words above
            int index = 0;
            foreach (var row in rows)
            {
                double width = row.Sum(l => l.DesiredSize.Width) + 4 * (row.Count - 1), x = avatarHead.X - width / 2;
                foreach (var l in row)
                {
                    var spot = new Rect(Math.Clamp(x, 0, Math.Max(0, MessageLayer.Width - l.DesiredSize.Width)), Math.Max(0, top), l.DesiredSize.Width, l.DesiredSize.Height);
                    var lit = new Rectangle { Width = spot.Width + 2, Height = spot.Height, Fill = new SolidColorBrush(Color.FromArgb(110, 255, 255, 255)), Visibility = Visibility.Hidden, IsHitTestVisible = false };
                    Canvas.SetLeft(lit, spot.X - 1);
                    Canvas.SetTop(lit, spot.Y);
                    MessageLayer.Children.Add(lit);
                    Canvas.SetLeft(l, spot.X);
                    Canvas.SetTop(l, spot.Y);
                    MessageLayer.Children.Add(l);
                    drawing.ReplyRects.Add(spot);
                    drawing.ReplyHighlights.Add(lit);
                    drawing.ReplySlots.Add(index++);
                    x += l.DesiredSize.Width + 4;
                }
                top += lineHeight;
            }
        }
        else
        {
            // No answers: it goes on by itself after its time, as a bark (or on a click).
            var shown = message;
            int ms = Math.Max(u8.BarkMinMs, message.Text.Length * u8.BarkMsPerChar);
            _barkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            _barkTimer.Tick += (_, _) => { _barkTimer?.Stop(); if (ReferenceEquals(_currentMessage, shown)) AdvanceMessage(); };
            _barkTimer.Start();
        }
        _messageDrawing = drawing;
        _replyPage = 0;
        return true;
    }

    private void PlaceInLayer(FrameworkElement element, double x, double y)
    {
        element.IsHitTestVisible = false;
        Canvas.SetLeft(element, Math.Clamp(x, 0, Math.Max(0, MessageLayer.Width - element.DesiredSize.Width)));
        Canvas.SetTop(element, Math.Clamp(y, 0, Math.Max(0, MessageLayer.Height - element.DesiredSize.Height)));
        MessageLayer.Children.Add(element);
    }

    /// <summary>Words broken into lines no wider than this in the game's font (a '*' or a new line starts a new one).</summary>
    private List<string> WrapGameText(string text, double width)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("*", "\n").Split('\n'))
        {
            string line = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                var probe = GameTextLabel(candidate);
                probe.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (probe.DesiredSize.Width > width && line.Length > 0) { lines.Add(line); line = word; }
                else line = candidate;
            }
            if (line.Length > 0) lines.Add(line);
        }
        if (lines.Count == 0) lines.Add(string.Empty);
        return lines;
    }
}
