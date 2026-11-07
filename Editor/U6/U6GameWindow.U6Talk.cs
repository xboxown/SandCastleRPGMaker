// Ultima 6 engine (U6GameWindow): conversations as Ultima 6 has them - the portrait, the words in the text area, typed keywords.
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A Show Text in an Ultima 6 game: no box and no clicking. The speaker's portrait shows in the HUD's panel, what they say is
/// written in the text area with the words the player can ask about in the keyword colour (System Settings > Ultima 6), and
/// the player types: "you say: job". A typed word matches a reply by its first four letters, as Ultima 6; nothing typed is
/// "bye". A word with no reply: the character can't help with that. A page with no replies waits for Enter or Space.
/// Music while talking, as Nuvie: the map's stops, and after the talk it starts again from the beginning, or a random one.
/// </summary>
public partial class U6GameWindow
{
    /// <summary>The words written as typed so far ("you say: ...").</summary>
    private string _talkTyped = string.Empty;
    /// <summary>Who is talking: their portrait in the panel while the talk goes on.</summary>
    private CharacterDefinition? _hudTalker;
    private readonly object _talkMusicOwner = new();
    private bool _talkMusicOn;
    /// <summary>After a talk, a random song of the map's in place of the hour's own - until the hour's own would change.</summary>
    private (int Due, int Random)? _ambientHold;

    /// <summary>Ultima 6's way to talk: the HUD has a text area to talk in.</summary>
    private bool U6Talking => Hud.First(HudPieceKind.GameView) is not null && Hud.First(HudPieceKind.TextArea) is not null;

    /// <summary>Puts a message up the Ultima 6 way (ShowNextMessage's, when the HUD has a text area).</summary>
    private System.Windows.Threading.DispatcherTimer? _talkEndTimer;

    /// <summary>The talk ends when no words have come for a moment and nothing is being asked (a name typed, a choice) - so Show Text, Scene Type Text, Show Text stays one conversation.</summary>
    private void ScheduleEndU6Talk()
    {
        if (_talkEndTimer is null)
        {
            _talkEndTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
            _talkEndTimer.Tick += (_, _) =>
            {
                if (_currentMessage is not null || _sceneInput is not null || _letterAsk is not null || _sceneChoice is not null) return; // still talking or asking
                _talkEndTimer.Stop();
                EndU6Talk();
            };
        }
        _talkEndTimer.Stop();
        _talkEndTimer.Start();
    }

    private void ShowU6Message(GameMessage message)
    {
        _talkEndTimer?.Stop(); // more words: the same conversation
        _currentMessage = message;
        _replyCursor = -1;
        _replyPage = 0;
        _talkTyped = string.Empty;
        MessageLayer.Visibility = System.Windows.Visibility.Collapsed;
        if (message.Speaker is { } speaker && speaker != _player && _hudTalker != speaker)
        {
            _hudTalker = speaker;
            StartTalkMusic();
            RefreshHudPanel();
        }
        var keywords = message.Replies?.Select(r => r.Text).Where(t => t.Length > 0).ToList() ?? new List<string>();
        _talkMore.Clear();
        _hudPrompt = string.Empty;
        if (message.Text.Trim().Length > 0 && Hud.First(HudPieceKind.TextArea) is { } area)
        {
            // {Variable} in the words: its value (U6GameWindow.U6Scene.cs). Wrapped to the text area; more than it holds: a screenful, then the cursor waits for a key (as Ultima 6's scroll).
            var lines = MarkKeywords(SceneWords(message.Text.Trim()), keywords).Replace("\r", "").Split('\n').SelectMany(p => HudWrap(p, area.Width, area.FontName)).ToList();
            int fit = Math.Max(1, area.Height / Math.Max(1, HudLineHeight(area.FontName)) - 1);
            foreach (var line in lines.Take(fit)) U6Say(line);
            foreach (var line in lines.Skip(fit)) _talkMore.Enqueue(line);
            _talkMoreFit = fit;
        }
        if (_talkMore.Count == 0) _hudPrompt = message.HasReplies ? W("YouSay") : string.Empty;
        RefreshHudText();
    }

    /// <summary>The talk is over: the portrait goes back to the member, the map's music comes back.</summary>
    private void EndU6Talk()
    {
        if (_hudTalker is null && !_talkMusicOn) return;
        _hudTalker = null;
        _talkTyped = string.Empty;
        _hudPrompt = string.Empty;
        EndTalkMusic();
        RefreshHudPanel();
        RefreshHudText();
    }

    /// <summary>A key while a message is up: Enter answers (or goes on), Backspace erases; the letters come by U6TalkText.</summary>
    /// <summary>The rest of a long speech, waiting for a key (a screenful at a time).</summary>
    private readonly Queue<string> _talkMore = new();
    private int _talkMoreFit = 4;

    private bool U6TalkKey(Key key)
    {
        if (!U6Talking || _currentMessage is not { } message) return false;
        if (_talkMore.Count > 0)
        {
            for (int i = 0; i < _talkMoreFit && _talkMore.Count > 0; i++) U6Say(_talkMore.Dequeue());
            if (_talkMore.Count == 0) _hudPrompt = message.HasReplies ? W("YouSay") : string.Empty;
            RefreshHudText();
            return true;
        }
        if (!message.HasReplies)
        {
            if (KeyIs(key, "Accept") || KeyIs(key, "Accept2") || KeyIs(key, "Cancel")) AdvanceMessage();
            return true;
        }
        if (KeyIs(key, "Erase"))
        {
            if (_talkTyped.Length > 0) _talkTyped = _talkTyped[..^1];
            _hudPrompt = W("YouSay") + _talkTyped;
            RefreshHudText();
            return true;
        }
        if (KeyIs(key, "Accept")) { AnswerTyped(); return true; }
        if (KeyIs(key, "Cancel")) { _talkTyped = string.Empty; AnswerTyped(); return true; } // Escape: as nothing typed - bye
        if (LetterOf(key) is char letter && _talkTyped.Length < 40)
        {
            _talkTyped += letter;
            _hudPrompt = W("YouSay") + _talkTyped;
            RefreshHudText();
        }
        return true;
    }

    /// <summary>The letter a key writes (the key-down is the game's, so the letter is read from it): letters, digits, space and a few marks.</summary>
    private static char? LetterOf(Key key)
    {
        bool shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (key is >= Key.A and <= Key.Z) { char c = (char)('a' + (key - Key.A)); return shift ? char.ToUpperInvariant(c) : c; }
        if (key is >= Key.D0 and <= Key.D9 && !shift) return (char)('0' + (key - Key.D0));
        if (key is >= Key.NumPad0 and <= Key.NumPad9) return (char)('0' + (key - Key.NumPad0));
        return key switch
        {
            Key.Space => ' ',
            Key.OemQuotes => shift ? '"' : '\'',
            Key.OemComma => ',',
            Key.OemPeriod => '.',
            Key.OemMinus => shift ? '_' : '-',
            Key.OemQuestion => shift ? '?' : '/',
            Key.D1 when shift => '!',
            _ => null
        };
    }

    /// <summary>Enter: the typed word picks the reply it starts like (four letters, as Ultima 6); nothing typed is bye.</summary>
    private void AnswerTyped()
    {
        if (_currentMessage is not { Replies: { } replies }) return;
        string typed = _talkTyped.Trim();
        U6Say(W("YouSay") + typed);
        _talkTyped = string.Empty;
        _hudPrompt = W("YouSay");
        string asked = typed.Length == 0 ? W("Bye") : typed;
        int index = replies.FindIndex(r => KeywordMatches(r.Text, asked));
        if (index < 0)
        {
            U6Say(W("NoHelp", asked, _currentMessage.Speaker?.Name));
            RefreshHudText();
            return;
        }
        _hudPrompt = string.Empty;
        PickReply(index);
    }

    /// <summary>Ultima 6 compares the first four letters: "jobs" asks about "job", "nam" doesn't ask about "name".</summary>
    private static bool KeywordMatches(string keyword, string typed)
    {
        string k = keyword.Trim().ToLowerInvariant(), t = typed.Trim().ToLowerInvariant();
        if (k.Length == 0) return false;
        int n = Math.Min(4, k.Length);
        return t.Length >= n && t[..n] == k[..n];
    }

    /// <summary>The words that are keywords marked (each word between \u0001 and \u0002) to be drawn in the keyword colour.</summary>
    private static string MarkKeywords(string text, List<string> keywords)
    {
        foreach (var keyword in keywords.OrderByDescending(k => k.Length))
        {
            int at = 0;
            while ((at = text.IndexOf(keyword, at, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                bool startOk = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
                bool endOk = at + keyword.Length >= text.Length || !char.IsLetterOrDigit(text[at + keyword.Length]);
                if (!startOk || !endOk || (at > 0 && text[at - 1] == '\u0001')) { at += keyword.Length; continue; }
                string marked = string.Join(" ", text.Substring(at, keyword.Length).Split(' ').Select(w => "\u0001" + w + "\u0002"));
                text = text[..at] + marked + text[(at + keyword.Length)..];
                at += marked.Length;
            }
        }
        return text;
    }

    // ---------- Music ----------

    private void StartTalkMusic()
    {
        var u6 = _systemSettings.Ultima6;
        if (_talkMusicOn || u6.ConversationMusicId < 0) return;
        _talkMusicOn = true;
        SilenceAmbient();
        SoundPlayback.TryPlayFor(_talkMusicOwner, _resources, u6.ConversationMusicId, loop: true);
    }

    private void EndTalkMusic()
    {
        if (!_talkMusicOn) return;
        _talkMusicOn = false;
        SoundPlayback.StopFor(_talkMusicOwner);
        _ambientPlaying = -2; // the map's music starts again from the beginning on the next tick
        if (_systemSettings.Ultima6.AfterTalkMusic == Ultima6Settings.AfterTalkRandom)
        {
            var a = _map.Ambient;
            var songs = new[] { a.DawnSoundId, a.DaySoundId, a.DuskSoundId, a.NightSoundId }.Where(id => id >= 0).Distinct().ToList();
            if (songs.Count > 0) _ambientHold = (-1, songs[Rng.Next(songs.Count)]);
        }
    }
}
