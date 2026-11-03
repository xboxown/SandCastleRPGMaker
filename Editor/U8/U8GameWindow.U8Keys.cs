// Ultima 8 engine (U8GameWindow) only - the Ultima 7 engine keeps its fixed keys.
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The keys of an Ultima 8 game (Engine/U8Keys.cs): the maker's in System Settings > Ultima 8, the player's own changed in the
/// game by the Ultima 8 tab's commands - kept on the disk at once, beside the saves but in none of them. A paperdoll shows them
/// with %keyjump, %keyrun...; the "keys paperdoll" opens on its own key.
/// </summary>
public partial class U8GameWindow
{
    private Dictionary<string, string> _keyNames = new();
    private readonly Dictionary<string, (Key Key, ModifierKeys Held)> _keyOf = new();
    private readonly U8KeyCapture _keyCapture = new();

    /// <summary>Ask For Key waiting for the player's key press: the next key goes to it.</summary>
    private sealed record KeyAsk(U8KeyAction Action, bool Swap, Action<bool> Done, (Func<double, bool?> Tick, Action<bool> Done) Wait);
    private KeyAsk? _keyAsk;

    private void InitializeU8Keys()
    {
        _keyNames = U8Keys.Load(_systemSettings.Ultima8, ProjectSession.ProjectIdProvider?.Invoke());
        CacheKeys();
        var h = _execContext.GameHandlers;
        _execContext.GameActions[EventCommandType.U8AskKey] = AskKeyCommand;
        h[EventCommandType.U8SetKey] = c =>
        {
            if (U8Keys.ByName(c.Get("Action")) is not { } action || U8Keys.FromWords(c.Get("Key")) is not { } key) return false;
            string name = U8Keys.HeldWith.Contains(c.Get("With")) ? c.Get("With") + "+" + key : key;
            return U8Keys.IsKeyName(name) && ChangeKey(action, name, c.Get("IfUsed") != "Fail");
        };
        h[EventCommandType.U8GetKeyName] = c =>
        {
            if (U8Keys.ByName(c.Get("Action")) is not { } action) return false;
            SetScriptVariable(c.Get("Variable"), U8Keys.Display(_keyNames[action.Id]));
            return true;
        };
        h[EventCommandType.U8ResetKeys] = ResetKeysCommand;
    }

    private void CacheKeys()
    {
        _keyOf.Clear();
        foreach (var (id, name) in _keyNames) _keyOf[id] = U8KeyCapture.Parse(name);
    }

    /// <summary>This key press is this action's. A plain key counts whatever else is held (Shift runs while the Up Arrow walks), unless the press is another action's "Ctrl+...".</summary>
    private bool KeyIs(Key pressed, string action)
    {
        var (key, held) = _keyOf[action];
        if (key != pressed) return false;
        var now = Keyboard.Modifiers;
        if (held != ModifierKeys.None) return (now & held) == held;
        return !_keyOf.Values.Any(o => o.Key == pressed && o.Held != ModifierKeys.None && (now & o.Held) == o.Held);
    }

    /// <summary>A key's words for what the game writes on the screen ("Enter to accept").</summary>
    private string KeyWords(string action) => U8Keys.Display(_keyNames[action]);

    /// <summary>The number key's place, 0-8 (the party's members, a box's replies); -1: it isn't one.</summary>
    private int NumberOf(Key pressed)
    {
        for (int i = 0; i < 9; i++)
            if (KeyIs(pressed, $"Number{i + 1}") || KeyIs(pressed, $"Number{i + 1}Second")) return i;
        return -1;
    }

    /// <summary>A key that writes a letter. While a name is typed it writes it, whatever it does elsewhere: the mouse is still there to finish.</summary>
    private static bool TypesLetter(Key key) =>
        (Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) == 0
        && key is (>= Key.A and <= Key.Z) or (>= Key.D0 and <= Key.D9) or (>= Key.NumPad0 and <= Key.NumPad9) or (>= Key.Multiply and <= Key.Divide) or (>= Key.Oem1 and <= Key.Oem102) or Key.Space;

    /// <summary>This key press is this action's while a name is typed.</summary>
    private bool TypingKeyIs(Key pressed, string action) => !TypesLetter(pressed) && KeyIs(pressed, action);

    /// <summary>The arrow a pressed key walks as (the engine's held keys stay the arrows, whatever key walks); Key.None: it doesn't walk.</summary>
    private Key WalkArrowFor(Key pressed) =>
        KeyIs(pressed, "Left") ? Key.Left : KeyIs(pressed, "Right") ? Key.Right : KeyIs(pressed, "Up") ? Key.Up : KeyIs(pressed, "Down") ? Key.Down : Key.None;

    /// <summary>The arrow a released key stops: what's held with it may have been let go first.</summary>
    private Key WalkArrowReleased(Key released) =>
        _keyOf["Left"].Key == released ? Key.Left : _keyOf["Right"].Key == released ? Key.Right : _keyOf["Up"].Key == released ? Key.Up : _keyOf["Down"].Key == released ? Key.Down : Key.None;

    /// <summary>An action's key is down now (Run, the whole stack). A Shift, Ctrl or Alt: either of the pair, as Shift always ran.</summary>
    private bool KeyHeld(string action)
    {
        var (key, held) = _keyOf[action];
        if ((Keyboard.Modifiers & held) != held) return false;
        return key switch
        {
            Key.LeftShift or Key.RightShift => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
            Key.LeftCtrl or Key.RightCtrl => Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
            Key.LeftAlt or Key.RightAlt => Keyboard.Modifiers.HasFlag(ModifierKeys.Alt),
            _ => Keyboard.IsKeyDown(key)
        };
    }

    private bool RunKeyHeld => KeyHeld("Run");

    /// <summary>The key the slider (shared with the Ultima 7 engine, its keys fixed) understands for what this key does here.</summary>
    private Key SliderKeyFor(Key pressed) =>
        KeyIs(pressed, "ScreenUp2") ? Key.Left : KeyIs(pressed, "ScreenDown2") ? Key.Right : KeyIs(pressed, "Accept") ? Key.Enter : KeyIs(pressed, "Cancel") ? Key.Escape : Key.None;

    /// <summary>The keys paperdoll's key: it opens, or closes if it's open.</summary>
    private void ToggleKeysPaperdoll()
    {
        if (_objects.DollDesigns.FirstOrDefault(d => d.Id == _systemSettings.Ultima8.KeysDollDesignId) is not { } design) return;
        if (_openDolls.FirstOrDefault(g => g.Design.Id == design.Id) is { } open) CloseDollGump(open);
        else OpenForm(design, modal: false, answered: null, fresh: false);
    }

    /// <summary>A paperdoll's words with its %key... filled in: the key's name, or the waiting words while Ask For Key waits for that one.</summary>
    private string FillKeyWords(string text)
    {
        foreach (var action in U8Keys.ActionsLongestIdFirst)
        {
            string shown = _keyAsk?.Action == action ? _systemSettings.Ultima8.KeyAskWords : U8Keys.Display(_keyNames[action.Id]);
            text = text.Replace("%key" + action.Id, shown, StringComparison.OrdinalIgnoreCase);
        }
        return text;
    }

    /// <summary>Every open paperdoll drawn again: what it shows of the keys has changed.</summary>
    private void RefreshKeyWords()
    {
        foreach (var gump in _openDolls.ToList()) RefreshDollGump(gump);
    }

    /// <summary>A key given to an action in the game: at once, and on the disk for good. False: the key is another action's and it isn't to be swapped.</summary>
    private bool ChangeKey(U8KeyAction action, string key, bool swap)
    {
        if (!U8Keys.Assign(_keyNames, action.Id, key, swap)) return false;
        return KeysChanged();
    }

    private bool KeysChanged()
    {
        CacheKeys();
        _heldDirectionKeys.Clear(); // a walking key changed under the finger mustn't walk on by itself
        RefreshKeyWords();
        try { U8Keys.Save(_systemSettings.Ultima8, ProjectSession.ProjectIdProvider?.Invoke(), _keyNames); }
        catch (Exception ex) { GameError(ex, 1000); return false; } // the disk's own trouble: the key works until the game closes
        return true;
    }

    /// <summary>Ask For Key: the event waits; the next key pressed goes to the action. The Cancel key: no change, and "last command succeeded" is false.</summary>
    private bool AskKeyCommand(EventCommand command, Action<bool> done)
    {
        if (U8Keys.ByName(command.Get("Action")) is not { } action) return false;
        CancelKeyAsk();
        _keyCapture.Reset();
        (Func<double, bool?> Tick, Action<bool> Done) wait = (_ => null, done); // in the waits' list: Cinema Mode and saving see an event still waiting
        _scriptWaits.Add(wait);
        _keyAsk = new KeyAsk(action, command.Get("IfUsed") != "Fail", done, wait);
        RefreshKeyWords();
        return true;
    }

    private void FinishKeyAsk(KeyAsk ask, bool ok)
    {
        _scriptWaits.Remove(ask.Wait);
        _keyAsk = null;
        RefreshKeyWords();
        ask.Done(ok);
    }

    /// <summary>Whatever opened the question has gone: the event isn't left waiting.</summary>
    private void CancelKeyAsk()
    {
        if (_keyAsk is { } ask) FinishKeyAsk(ask, false);
    }

    /// <summary>Ask For Key takes the key press first. A key Windows keeps for itself, or a Shift, Ctrl or Alt still held for what comes with it: it goes on waiting.</summary>
    private bool KeyAskTook(KeyEventArgs e)
    {
        if (_keyAsk is not { } ask) return false;
        var pressed = U8KeyCapture.RealKey(e);
        if (pressed == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return false; // Windows' own: the window closes
        e.Handled = true;
        if (e.IsRepeat) return true;
        if (ask.Action.Id != "Cancel" && KeyIs(pressed, "Cancel")) { FinishKeyAsk(ask, false); return true; }
        if (_keyCapture.Down(pressed, Keyboard.Modifiers) is { } key) AnswerKeyAsk(ask, key);
        return true;
    }

    /// <summary>A Shift, Ctrl or Alt let go with nothing pressed with it: that's the key.</summary>
    private bool KeyAskReleased(KeyEventArgs e)
    {
        if (_keyAsk is not { } ask) return false;
        e.Handled = true;
        if (_keyCapture.Up(U8KeyCapture.RealKey(e)) is { } key) AnswerKeyAsk(ask, key);
        return true;
    }

    private void AnswerKeyAsk(KeyAsk ask, string key)
    {
        _keyAsk = null; // answered: what it shows goes back to the key
        _scriptWaits.Remove(ask.Wait);
        bool ok = ChangeKey(ask.Action, key, ask.Swap);
        if (!ok) RefreshKeyWords();
        ask.Done(ok);
    }

    /// <summary>Reset Keys: one action or all back to the maker's keys - on the disk too.</summary>
    private bool ResetKeysCommand(EventCommand command)
    {
        var maker = U8Keys.MakerKeys(_systemSettings.Ultima8);
        if (command.Get("Action") == AllKeysWords)
        {
            _keyNames = maker;
            return KeysChanged();
        }
        return U8Keys.ByName(command.Get("Action")) is { } action && ChangeKey(action, maker[action.Id], swap: true);
    }

    private const string AllKeysWords = "Every key";
}
