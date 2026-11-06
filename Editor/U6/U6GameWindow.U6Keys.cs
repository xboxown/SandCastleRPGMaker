// Ultima 6 engine (U6GameWindow): Ultima 6's keys - every one changeable (System Settings > Ultima 6, and the Ultima 6 tab's commands).
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The keys of an Ultima 6 game (Engine/U6Keys.cs). Ultima 6's own by default: A Attack, C Cast, T Talk, L Look, G Get, D Drop,
/// M Move, U Use, R Rest, B combat; Tab the cursor to the inventory; F1-F9 a member, F10 the party; 1-9 solo, 0 party; * portrait.
/// Nothing else the Ultima 7 engine read is left: Z (stats), I (inventory), S (save screen) and the rest do nothing unless an
/// action is given them.
/// </summary>
public partial class U6GameWindow
{
    private Dictionary<string, string> _keyNames = new();
    private readonly Dictionary<string, (Key Key, ModifierKeys Held)> _keyOf = new();
    private readonly U8KeyCapture _keyCapture = new();
    private sealed record KeyAsk(U8KeyAction Action, bool Swap, Action<bool> Done, (Func<double, bool?> Tick, Action<bool> Done) Wait);
    private KeyAsk? _keyAsk;

    private void InitializeU6Keys()
    {
        _keyNames = U6Keys.Load(_systemSettings.Ultima6, ProjectSession.ProjectIdProvider?.Invoke());
        CacheKeys();
        var h = _execContext.GameHandlers;
        _execContext.GameActions[EventCommandType.U6AskKey] = AskKeyCommand;
        h[EventCommandType.U6SetKey] = c =>
        {
            if (U6Keys.ByName(c.Get("Action")) is not { } action || U8Keys.FromWords(c.Get("Key")) is not { } key) return false;
            string name = U8Keys.HeldWith.Contains(c.Get("With")) ? c.Get("With") + "+" + key : key;
            return U8Keys.IsKeyName(name) && ChangeKey(action, name, c.Get("IfUsed") != "Fail");
        };
        h[EventCommandType.U6GetKeyName] = c =>
        {
            if (U6Keys.ByName(c.Get("Action")) is not { } action) return false;
            SetScriptVariable(c.Get("Variable"), U8Keys.Display(_keyNames[action.Id]));
            return true;
        };
        h[EventCommandType.U6ResetKeys] = c =>
        {
            var maker = U6Keys.MakerKeys(_systemSettings.Ultima6);
            if (c.Get("Action") == "Every key") { _keyNames = maker; return KeysChanged(); }
            return U6Keys.ByName(c.Get("Action")) is { } action && ChangeKey(action, maker[action.Id], swap: true);
        };
    }

    private void CacheKeys()
    {
        _keyOf.Clear();
        foreach (var (id, name) in _keyNames) _keyOf[id] = U8KeyCapture.Parse(name);
    }

    /// <summary>This key press is this action's (a plain key counts unless the press is another action's "Ctrl+...").</summary>
    private bool KeyIs(Key pressed, string action)
    {
        if (!_keyOf.TryGetValue(action, out var bound)) return false;
        var (key, held) = bound;
        if (key != pressed) return false;
        var now = Keyboard.Modifiers;
        if (held != ModifierKeys.None) return (now & held) == held;
        return !_keyOf.Values.Any(o => o.Key == pressed && o.Held != ModifierKeys.None && (now & o.Held) == o.Held);
    }

    private string KeyWords(string action) => U8Keys.Display(_keyNames[action]);

    /// <summary>The walking arrows a key walks as (the engine's held keys stay the arrows); empty: it doesn't walk.</summary>
    private Key[] WalkArrowsFor(Key pressed) =>
        KeyIs(pressed, "Up") ? new[] { Key.Up } : KeyIs(pressed, "Down") ? new[] { Key.Down } : KeyIs(pressed, "Left") ? new[] { Key.Left } : KeyIs(pressed, "Right") ? new[] { Key.Right }
        : KeyIs(pressed, "UpLeft") ? new[] { Key.Up, Key.Left } : KeyIs(pressed, "UpRight") ? new[] { Key.Up, Key.Right }
        : KeyIs(pressed, "DownLeft") ? new[] { Key.Down, Key.Left } : KeyIs(pressed, "DownRight") ? new[] { Key.Down, Key.Right }
        : Array.Empty<Key>();

    /// <summary>The arrows a released key stops (what's held with it may have been let go first).</summary>
    private Key[] WalkArrowsReleased(Key released)
    {
        var arrows = new List<Key>();
        foreach (var (id, arrowsOf) in new (string, Key[])[] { ("Up", new[] { Key.Up }), ("Down", new[] { Key.Down }), ("Left", new[] { Key.Left }), ("Right", new[] { Key.Right }),
                     ("UpLeft", new[] { Key.Up, Key.Left }), ("UpRight", new[] { Key.Up, Key.Right }), ("DownLeft", new[] { Key.Down, Key.Left }), ("DownRight", new[] { Key.Down, Key.Right }) })
            if (_keyOf.TryGetValue(id, out var k) && k.Key == released) arrows.AddRange(arrowsOf);
        return arrows.ToArray();
    }

    private bool ChangeKey(U8KeyAction action, string key, bool swap) => U6Keys.Assign(_keyNames, action.Id, key, swap) && KeysChanged();

    private bool KeysChanged()
    {
        CacheKeys();
        _heldDirectionKeys.Clear(); // a walking key changed under the finger mustn't walk on by itself
        try { U6Keys.Save(_systemSettings.Ultima6, ProjectSession.ProjectIdProvider?.Invoke(), _keyNames); }
        catch (Exception ex) { GameError(ex, 1000); return false; }
        return true;
    }

    /// <summary>Ask For Key: the event waits for the next key (the text area asks for it). The Cancel key: no change, and "last command succeeded" is false.</summary>
    private bool AskKeyCommand(EventCommand command, Action<bool> done)
    {
        if (U6Keys.ByName(command.Get("Action")) is not { } action) return false;
        if (_keyAsk is { } old) FinishKeyAsk(old, false);
        _keyCapture.Reset();
        (Func<double, bool?> Tick, Action<bool> Done) wait = (_ => null, done);
        _scriptWaits.Add(wait);
        _keyAsk = new KeyAsk(action, command.Get("IfUsed") != "Fail", done, wait);
        U6Say(U6Words.Of(_systemSettings.Ultima6, "PressAKey"));
        return true;
    }

    private void FinishKeyAsk(KeyAsk ask, bool ok)
    {
        _scriptWaits.Remove(ask.Wait);
        _keyAsk = null;
        ask.Done(ok);
    }

    /// <summary>Ask For Key takes the key press first.</summary>
    private bool KeyAskTook(KeyEventArgs e)
    {
        if (_keyAsk is not { } ask) return false;
        var pressed = U8KeyCapture.RealKey(e);
        if (pressed == Key.F4 && Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return false;
        e.Handled = true;
        if (e.IsRepeat) return true;
        if (ask.Action.Id != "Cancel" && KeyIs(pressed, "Cancel")) { FinishKeyAsk(ask, false); return true; }
        if (_keyCapture.Down(pressed, Keyboard.Modifiers) is { } key) AnswerKeyAsk(ask, key);
        return true;
    }

    private bool KeyAskReleased(KeyEventArgs e)
    {
        if (_keyAsk is not { } ask) return false;
        e.Handled = true;
        if (_keyCapture.Up(U8KeyCapture.RealKey(e)) is { } key) AnswerKeyAsk(ask, key);
        return true;
    }

    private void AnswerKeyAsk(KeyAsk ask, string key)
    {
        _keyAsk = null;
        _scriptWaits.Remove(ask.Wait);
        bool ok = ChangeKey(ask.Action, key, ask.Swap);
        U6Say(U8Keys.Display(key));
        ask.Done(ok);
    }
}
