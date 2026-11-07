// Ultima 6 engine (U6GameWindow): how conversations look and work - Ultima 6's by default, the maker's own by Conversation Style - Ultima 6 projects only.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Conversation Style (Ultima 6 tab > Conversations): from now on (kept in saves) a conversation's answers are typed (Ultima 6),
/// picked from a list (arrows, 1-9, a click) or either; its keywords coloured or not, in any colour; the portrait in the HUD's
/// Portrait (Ultima 6), inside the words' box, or none; the words in the HUD's text area (Ultima 6) or in a box of their own -
/// its place, size, colour or picture, font and colour. Back to Ultima 6's: as it starts. Put it at the start of the game, or
/// at the top of one character's Talk event (and back at its end) - with the HUD Editor and the Scene commands, Savage Empire's,
/// Martian Dreams' or any way of the maker's own.
/// </summary>
public partial class U6GameWindow
{
    private U6TalkStyle? _talkStyle;
    private bool _talkBoxOpen;
    private Canvas? _talkBox;
    private readonly List<string> _boxLines = new();
    private int _replyPick;

    private bool PickingReplies => _talkStyle is { Answers: not U6TalkStyle.Typed } && _currentMessage is { HasReplies: true } && _talkMore.Count == 0;
    private bool TypingReplies => _talkStyle is null || _talkStyle.Answers != U6TalkStyle.List;

    private void InitU6TalkStyle()
    {
        _execContext.GameHandlers[EventCommandType.U6ConversationStyle] = c =>
        {
            if (c.Get("Set") == "Back to Ultima 6's") { _talkStyle = null; return true; }
            double N(string key, double otherwise) => c.Get(key).Trim().Length == 0 ? otherwise : FormulaNumber(c, key);
            _talkStyle = new U6TalkStyle
            {
                Answers = c.Get("Answers") is { Length: > 0 } a ? a : U6TalkStyle.Typed,
                MarkKeywords = c.Get("MarkKeywords") != "False",
                KeywordColor = c.Get("KeywordColour").Trim(),
                Portrait = c.Get("Portrait") is { Length: > 0 } p ? p : U6TalkStyle.PortraitHud,
                Words = c.Get("Words") is { Length: > 0 } w ? w : U6TalkStyle.WordsTextArea,
                BoxX = (int)N("BoxX", 8), BoxY = (int)N("BoxY", 120), BoxWidth = (int)N("BoxWidth", 304), BoxHeight = (int)N("BoxHeight", 72),
                BoxColor = c.Get("BoxColour").Trim(), BoxSprite = new SpriteLink { SpriteGroup = c.Get("SpriteGroup"), SpriteName = c.Get("SpriteName") },
                Font = c.Get("Font").Trim(), TextColor = c.Get("TextColour").Trim()
            };
            return true;
        };
    }

    /// <summary>The words' box while a conversation goes on (Conversation Style: Words in a box), as a text piece; null: the HUD's text area.</summary>
    private HudPiece? TalkBoxPiece
    {
        get
        {
            if (_talkStyle is not { Words: U6TalkStyle.WordsBox } s || !_talkBoxOpen) return null;
            int faceRoom = s.Portrait == U6TalkStyle.PortraitBox && TalkFace() is { } f ? (DollLook.Frame(f)?.PixelWidth ?? 0) + 6 : 0;
            return new HudPiece { Id = -77, X = s.BoxX + 4 + faceRoom, Y = s.BoxY + 4, Width = Math.Max(16, s.BoxWidth - 8 - faceRoom), Height = Math.Max(8, s.BoxHeight - 8), FontName = s.Font, TextColor = s.TextColor };
        }
    }

    private SpriteEntry? TalkFace() =>
        _hudTalker is { } t && t.FaceSpriteName.Length > 0 ? FindSprite(t.FaceSpriteGroup, t.FaceSpriteName) : null;

    /// <summary>The keywords' colour now: the style's, else System Settings > Ultima 6's.</summary>
    private string KeywordColour => _talkStyle is { KeywordColor.Length: > 0 } s ? (s.KeywordColor.StartsWith('#') ? s.KeywordColor : "#" + s.KeywordColor) : _systemSettings.Ultima6.KeywordColor;

    /// <summary>The replies listed (pick from a list): "1. Name" ..., the one picked marked.</summary>
    private List<string> ReplyListLines()
    {
        if (!PickingReplies || _currentMessage?.Replies is not { } replies) return new List<string>();
        return replies.Select((r, i) => $"{(i == _replyPick ? ">" : " ")}{i + 1}. {r.Text}").ToList();
    }

    /// <summary>The conversation's box over the screen: its colour or picture, the portrait if it goes there, the words, the replies and the prompt.</summary>
    private void DrawTalkBox()
    {
        if (TalkBoxPiece is not { } words || _talkStyle is not { } s)
        {
            if (_talkBox is not null) { HudLayer.Children.Remove(_talkBox); _talkBox = null; }
            return;
        }
        if (_talkBox is null)
        {
            _talkBox = new Canvas { ClipToBounds = true };
            Panel.SetZIndex(_talkBox, 90000);
            HudLayer.Children.Add(_talkBox);
        }
        Canvas.SetLeft(_talkBox, s.BoxX);
        Canvas.SetTop(_talkBox, s.BoxY);
        _talkBox.Width = s.BoxWidth;
        _talkBox.Height = s.BoxHeight;
        _talkBox.Background = s.BoxColor.Length > 0 && ColorConverter.ConvertFromString(s.BoxColor.StartsWith('#') ? s.BoxColor : "#" + s.BoxColor) is Color c ? new SolidColorBrush(c) : Brushes.Black;
        _talkBox.Children.Clear();
        if (SpriteOfLink(s.BoxSprite) is { } picture) _talkBox.Children.Add(HudImage(picture, 0, 0, s.BoxWidth, s.BoxHeight, null));
        if (s.Portrait == U6TalkStyle.PortraitBox && TalkFace() is { } face && DollLook.Frame(face) is { } ff)
            _talkBox.Children.Add(HudImage(face, 4, 4, ff.PixelWidth, Math.Min(ff.PixelHeight, s.BoxHeight - 8), null, fit: true));
        var lines = _boxLines.ToList();
        lines.AddRange(ReplyListLines());
        if (_hudPrompt.Length > 0 || _talkMore.Count > 0) lines.AddRange(HudWrap(_hudPrompt + "_", words.Width, words.FontName));
        int lineH = HudLineHeight(words.FontName), fit = Math.Max(1, words.Height / Math.Max(1, lineH));
        var holder = new Canvas { Width = words.Width, Height = words.Height, ClipToBounds = true };
        Canvas.SetLeft(holder, words.X - s.BoxX);
        Canvas.SetTop(holder, words.Y - s.BoxY);
        double y = 0;
        foreach (var line in lines.Skip(Math.Max(0, lines.Count - fit)))
        {
            var shown = HudWords(line, words);
            Canvas.SetTop(shown, y);
            holder.Children.Add(shown);
            y += lineH;
        }
        _talkBox.Children.Add(holder);
    }

    private void CloseTalkBox()
    {
        _talkBoxOpen = false;
        _boxLines.Clear();
        if (_talkBox is not null) { HudLayer.Children.Remove(_talkBox); _talkBox = null; }
    }

    /// <summary>Picking from the list: Up / Down, 1-9, Enter. True when the key was the list's.</summary>
    private bool ReplyPickKey(Key key)
    {
        if (!PickingReplies || _currentMessage?.Replies is not { } replies) return false;
        int pick = -1;
        if (key is >= Key.D1 and <= Key.D9 && !(TypingReplies && _talkTyped.Length > 0)) pick = key - Key.D1;
        else if (key is >= Key.NumPad1 and <= Key.NumPad9) pick = key - Key.NumPad1;
        else if (key == Key.Up) { _replyPick = (_replyPick + replies.Count - 1) % replies.Count; RefreshHudText(); return true; }
        else if (key == Key.Down) { _replyPick = (_replyPick + 1) % replies.Count; RefreshHudText(); return true; }
        else if (key is Key.Enter or Key.Return && !(TypingReplies && _talkTyped.Trim().Length > 0)) pick = _replyPick;
        if (pick < 0 || pick >= replies.Count) return !TypingReplies; // list only: nothing else
        U6Say(W("YouSay") + replies[pick].Text);
        _talkTyped = string.Empty;
        _hudPrompt = string.Empty;
        _replyPick = 0;
        PickReply(pick);
        return true;
    }

    /// <summary>A click on a listed reply (in the text area or the box).</summary>
    private bool ReplyClicked(Point onScreen)
    {
        if (!PickingReplies || _currentMessage?.Replies is not { } replies) return false;
        var target = TalkBoxPiece ?? Hud.First(HudPieceKind.TextArea);
        if (target is null || !new Rect(target.X, target.Y, target.Width, target.Height).Contains(onScreen)) return false;
        int lineH = HudLineHeight(target.FontName), fit = Math.Max(1, target.Height / Math.Max(1, lineH));
        var history = TalkBoxPiece is not null ? _boxLines : _hudLines;
        int promptLines = _hudPrompt.Length > 0 ? 1 : 0;
        int total = history.Count + replies.Count + promptLines, first = Math.Max(0, total - fit);
        int clicked = first + (int)((onScreen.Y - target.Y) / lineH) - history.Count;
        if (clicked < 0 || clicked >= replies.Count) return false;
        _replyPick = clicked;
        return ReplyPickKey(Key.Enter) || true;
    }
}
