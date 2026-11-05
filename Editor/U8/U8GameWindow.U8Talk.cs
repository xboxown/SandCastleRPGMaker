// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

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
        if (_currentMessage is null && (_talkPoser is not null || _portraitAnimators.Count > 0)) { EndTalkPose(); StopPortraits(); } // the talk is over
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
        EndTalkPose();
        StopPortraits();
        var speakerVisual = message.Speaker is { } speaker ? (speaker == _player ? _playerImage : MapCharacterOf(speaker)?.Visual) : null;
        if (speakerVisual is null || !speakerVisual.IsVisible) return false;
        var u8 = _systemSettings.Ultima8;
        var drawing = new DialogBoxDrawing();

        // The words, over the speaker's head - in its own colour and font (Character Editor > Verbose).
        var words = new StackPanel();
        foreach (var line in WrapGameText(message.Text, u8.BarkWidth, message.Speaker))
        {
            var label = BarkLabel(line, message.Speaker);
            label.HorizontalAlignment = HorizontalAlignment.Center;
            words.Children.Add(label);
        }
        FrameworkElement bark = words;
        if (u8.TalkPortrait == Ultima8Settings.PortraitBeside && PortraitOf(message.Speaker) is { } beside)
        {
            // Its portrait beside the words, moving with them.
            words.VerticalAlignment = VerticalAlignment.Bottom;
            beside.VerticalAlignment = VerticalAlignment.Bottom;
            beside.Margin = new Thickness(0, 0, 4, 0);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(beside);
            row.Children.Add(words);
            bark = row;
        }
        bark.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var head = speakerVisual.TranslatePoint(new Point(speakerVisual.ActualWidth / 2, 0), MessageLayer);
        PlaceInLayer(bark, head.X - bark.DesiredSize.Width / 2, head.Y - bark.DesiredSize.Height - 2);
        (_barkWords, _barkOwner) = (bark, speakerVisual);
        StartTalkPose(message.Speaker); // its Talk pose while it speaks, if it has art for it
        if (u8.TalkPortrait == Ultima8Settings.PortraitCorner && PortraitOf(message.Replies is { Count: > 0 } ? _player : message.Speaker) is { } corner)
        {
            // In the corner: the speaker's portrait - the main character's while the player picks an answer.
            corner.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            PlaceInLayer(corner, 6, MessageLayer.Height - corner.DesiredSize.Height - 6);
        }

        if (message.Replies is { Count: > 0 } replies)
        {
            // The answers, over the Avatar (AskGump): rows of at most AskWidth, 4 apart.
            var avatarHead = _playerImage.TranslatePoint(new Point(_playerImage.ActualWidth / 2, 0), MessageLayer);
            var labels = replies.Select(r => { var l = BarkLabel(r.Text, _player); l.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); return l; }).ToList();
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

    // ---------- Its colour, its font, its portrait, its Talk pose ----------

    /// <summary>Words in the speaker's own bark font and colour (Character Editor > Verbose); none set: the game's font as it is.</summary>
    private FrameworkElement BarkLabel(string text, CharacterDefinition? who)
    {
        Brush? tint = null;
        if (who is { BarkColor.Length: > 0 } && ColorConverter.ConvertFromString(who.BarkColor) is Color c) tint = new SolidColorBrush(c);
        var font = who is { BarkFont.Length: > 0 } ? SpriteFont.For(_sprites, who.BarkFont) ?? InGameFont : InGameFont;
        if (font?.RenderText(text, 1, tint) is { } bitmap)
        {
            var image = new Image { Source = bitmap, Width = bitmap.PixelWidth, Height = bitmap.PixelHeight, Stretch = Stretch.None, IsHitTestVisible = false, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
            return image;
        }
        var label = GameTextLabel(text);
        if (tint is not null && label is TextBlock block) block.Foreground = tint;
        return label;
    }

    private readonly List<SpriteAnimator> _portraitAnimators = new();
    private CharacterDefinition? _talkPoser;

    /// <summary>Its Face (Character Editor), its frames playing - a talking head - at most 64 pixels tall. Null: it has none.</summary>
    private FrameworkElement? PortraitOf(CharacterDefinition? who)
    {
        if (who is null || who.FaceSpriteName.Length == 0 || FindSprite(who.FaceSpriteGroup, who.FaceSpriteName) is not { Frames.Count: > 0 } sprite) return null;
        var (width, height) = QuantizedSpriteSize(sprite);
        double scale = Math.Min(1.0, 64.0 / Math.Max(1, height));
        var image = new Image { Width = width * scale, Height = height * scale, Stretch = Stretch.Fill, IsHitTestVisible = false };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        _portraitAnimators.Add(new SpriteAnimator(sprite, image, _resources, 0));
        return image;
    }

    private void StopPortraits()
    {
        foreach (var animator in _portraitAnimators) animator.Stop();
        _portraitAnimators.Clear();
    }

    private void StartTalkPose(CharacterDefinition? who)
    {
        if (who is null || !HasArtFor(who, "Talk")) return;
        if (who == _player) ApplyPoseSprite(PoseByName("Talk"));
        else if (MapCharacterOf(who) is { } mc) SetCharacterPose(mc, "Talk", mc.Facing);
        else return;
        _talkPoser = who;
    }

    private void EndTalkPose()
    {
        if (_talkPoser is not { } who) return;
        _talkPoser = null;
        if (who == _player) { if (_currentAppliedPose?.Name == "Talk") ApplyPoseSprite(U8RestPose()); }
        else if (MapCharacterOf(who) is { PoseName: "Talk" } mc) SetCharacterPose(mc, "Idle", mc.Facing);
    }

    private void PlaceInLayer(FrameworkElement element, double x, double y)
    {
        element.IsHitTestVisible = false;
        Canvas.SetLeft(element, Math.Clamp(x, 0, Math.Max(0, MessageLayer.Width - element.DesiredSize.Width)));
        Canvas.SetTop(element, Math.Clamp(y, 0, Math.Max(0, MessageLayer.Height - element.DesiredSize.Height)));
        MessageLayer.Children.Add(element);
    }

    /// <summary>Words broken into lines no wider than this in the game's font (a '*' or a new line starts a new one).</summary>
    private List<string> WrapGameText(string text, double width, CharacterDefinition? speaker = null)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("*", "\n").Split('\n'))
        {
            string line = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = line.Length == 0 ? word : line + " " + word;
                var probe = BarkLabel(candidate, speaker);
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
