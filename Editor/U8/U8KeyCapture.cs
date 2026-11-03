// Ultima 8 only: reading a key to give to an action, in the game (Ask For Key) and in System Settings.
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Reads the next key as a name for Engine/U8Keys: a Shift, Ctrl or Alt waits to see what comes with it ("Ctrl+K"), and is the key
/// itself if it's let go alone.
/// </summary>
public sealed class U8KeyCapture
{
    private static readonly Dictionary<Key, string> Names = U8Keys.Bindable.ToDictionary(name => Enum.Parse<Key>(name), name => name);

    private string? _modifierDown;

    /// <summary>The key pressed: with Alt held, or for F10, WPF says only "System" and keeps the key in SystemKey.</summary>
    public static Key RealKey(KeyEventArgs e) => e.Key == Key.System ? e.SystemKey : e.Key;

    /// <summary>A key's name as the key and what's held with it: "Ctrl+Q" is (Q, Control).</summary>
    public static (Key Key, ModifierKeys Held) Parse(string name)
    {
        var (held, key) = U8Keys.Split(name);
        var modifiers = ModifierKeys.None;
        foreach (string h in held) modifiers |= h switch { "Ctrl" => ModifierKeys.Control, "Shift" => ModifierKeys.Shift, _ => ModifierKeys.Alt };
        return (Enum.Parse<Key>(key), modifiers);
    }

    public void Reset() => _modifierDown = null;

    /// <summary>A key went down. Its name, or null: still waiting (a Shift, Ctrl or Alt held, or a key that can't be taken).</summary>
    public string? Down(Key key, ModifierKeys held)
    {
        if (!Names.TryGetValue(key, out var name)) return null;
        if (key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt)
        {
            _modifierDown ??= name;
            return null;
        }
        _modifierDown = null;
        string with = (held.HasFlag(ModifierKeys.Control) ? "Ctrl+" : "") + (held.HasFlag(ModifierKeys.Shift) ? "Shift+" : "") + (held.HasFlag(ModifierKeys.Alt) ? "Alt+" : "");
        return with + name;
    }

    /// <summary>A key came up. The name of the Shift, Ctrl or Alt let go with nothing pressed with it; otherwise null.</summary>
    public string? Up(Key key)
    {
        if (_modifierDown is not { } down || !Names.TryGetValue(key, out var name) || name != down) return null;
        _modifierDown = null;
        return down;
    }
}
