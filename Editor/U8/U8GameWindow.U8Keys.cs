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
    private readonly Dictionary<string, Key> _keyOf = new();

    /// <summary>Ask For Key waiting for the player's key press: the next key goes to it.</summary>
    private sealed record KeyAsk(U8KeyAction Action, bool Swap, Action<bool> Done, (Func<double, bool?> Tick, Action<bool> Done) Wait);
    private KeyAsk? _keyAsk;

    private void InitializeU8Keys()
    {
        _keyNames = U8Keys.Load(_systemSettings.Ultima8, ProjectSession.ProjectIdProvider?.Invoke());
        CacheKeys();
        var h = _execContext.GameHandlers;
        _execContext.GameActions[EventCommandType.U8AskKey] = AskKeyCommand;
        h[EventCommandType.U8SetKey] = c => U8Keys.ByName(c.Get("Action")) is { } action && U8Keys.FromWords(c.Get("Key")) is { } key && ChangeKey(action, key, c.Get("IfUsed") != "Fail");
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
        foreach (var (id, name) in _keyNames) _keyOf[id] = Enum.Parse<Key>(name);
    }

    private bool KeyIs(Key pressed, string action) => _keyOf[action] == pressed;

    /// <summary>The arrow a pressed key walks as (the engine's held keys stay the arrows, whatever key walks); Key.None: it doesn't walk.</summary>
    private Key WalkArrowFor(Key pressed) =>
        KeyIs(pressed, "Left") ? Key.Left : KeyIs(pressed, "Right") ? Key.Right : KeyIs(pressed, "Up") ? Key.Up : KeyIs(pressed, "Down") ? Key.Down : Key.None;

    /// <summary>The run key held. A Shift or a Ctrl as the run key: either of the pair, as Shift always ran.</summary>
    private bool RunKeyHeld => _keyOf["Run"] switch
    {
        Key.LeftShift or Key.RightShift => Keyboard.Modifiers.HasFlag(ModifierKeys.Shift),
        Key.LeftCtrl or Key.RightCtrl => Keyboard.Modifiers.HasFlag(ModifierKeys.Control),
        var key => Keyboard.IsKeyDown(key)
    };

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
        foreach (var action in U8Keys.Actions)
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

    /// <summary>Ask For Key: the event waits; the next key pressed goes to the action. Escape: no change, and "last command succeeded" is false.</summary>
    private bool AskKeyCommand(EventCommand command, Action<bool> done)
    {
        if (U8Keys.ByName(command.Get("Action")) is not { } action) return false;
        CancelKeyAsk();
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

    /// <summary>Ask For Key takes the key press first. A key that can't be taken (the party's 1-9, Alt...): it goes on waiting.</summary>
    private bool KeyAskTook(KeyEventArgs e)
    {
        if (_keyAsk is not { } ask) return false;
        e.Handled = true;
        if (e.IsRepeat) return true;
        if (e.Key == Key.Escape) { FinishKeyAsk(ask, false); return true; }
        if (U8Keys.Bindable.FirstOrDefault(name => Enum.Parse<Key>(name) == e.Key) is not { } key) return true;
        _keyAsk = null; // answered: what it shows goes back to the key
        _scriptWaits.Remove(ask.Wait);
        bool ok = ChangeKey(ask.Action, key, ask.Swap);
        if (!ok) RefreshKeyWords();
        ask.Done(ok);
        return true;
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
