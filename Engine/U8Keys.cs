using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

/// <summary>Something the player does with a key in an Ultima 8 project. %key + its id in lower case shows its key on a paperdoll (%keyjump).</summary>
public sealed record U8KeyAction(string Id, string Name, string DefaultKey);

/// <summary>
/// The keys of an Ultima 8 project: what each action starts on (Ultima 8's, as the engine had them), what the maker set
/// (System Settings > Ultima 8) and what the player changed in the game - kept beside the game's saves, in no save slot, so
/// they hold through new games and loads until changed again. Keys are named as WPF names them ("LeftShift"), never drawn
/// as such: Display gives the words. Escape, Alt, the party's 1-9 and Game Test's F6 / F9 / F10 can't be taken: they aren't in Bindable.
/// </summary>
public static class U8Keys
{
    public static readonly U8KeyAction[] Actions =
    {
        new("Up", "Walk up the screen", "Up"),
        new("Down", "Walk down the screen", "Down"),
        new("Left", "Walk left", "Left"),
        new("Right", "Walk right", "Right"),
        new("Run", "Run (held)", "LeftShift"),
        new("Jump", "Jump", "J"),
        new("Combat", "Combat mode", "C"),
        new("Inventory", "Inventory", "I"),
        new("Stats", "Stats", "Z"),
        new("SaveLoad", "Save / Load", "S"),
        new("Menu", "Keys paperdoll", "F1"),
    };

    public static readonly string[] ActionNames = Actions.Select(a => a.Name).ToArray();

    public static U8KeyAction? ByName(string name) => Actions.FirstOrDefault(a => a.Name == name);

    public static readonly string[] Bindable = Enumerable.Range('A', 26).Select(c => ((char)c).ToString())
        .Concat(new[]
        {
            "D0", "NumPad0", "F1", "F2", "F3", "F4", "F5", "F7", "F8", "F11", "F12",
            "Up", "Down", "Left", "Right", "Space", "Enter", "Tab", "Back", "Insert", "Delete", "Home", "End", "PageUp", "PageDown",
            "LeftShift", "RightShift", "LeftCtrl", "RightCtrl",
            "OemTilde", "OemMinus", "OemPlus", "OemOpenBrackets", "OemCloseBrackets", "OemSemicolon", "OemQuotes", "OemComma", "OemPeriod", "OemQuestion", "OemPipe",
            "Add", "Subtract", "Multiply", "Divide", "Decimal",
        }).ToArray();

    /// <summary>The words for every key that can be taken, in Bindable's order (Set Key's list).</summary>
    public static readonly string[] BindableWords = Bindable.Select(Display).ToArray();

    /// <summary>A key's name as the player reads it.</summary>
    public static string Display(string key) => key switch
    {
        "D0" => "0", "NumPad0" => "Keypad 0", "Up" => "Up Arrow", "Down" => "Down Arrow", "Left" => "Left Arrow", "Right" => "Right Arrow",
        "Back" => "Backspace", "PageUp" => "Page Up", "PageDown" => "Page Down",
        "LeftShift" => "Left Shift", "RightShift" => "Right Shift", "LeftCtrl" => "Left Ctrl", "RightCtrl" => "Right Ctrl",
        "OemTilde" => "`", "OemMinus" => "-", "OemPlus" => "=", "OemOpenBrackets" => "[", "OemCloseBrackets" => "]", "OemSemicolon" => ";", "OemQuotes" => "'",
        "OemComma" => ",", "OemPeriod" => ".", "OemQuestion" => "/", "OemPipe" => "\\",
        "Add" => "Keypad +", "Subtract" => "Keypad -", "Multiply" => "Keypad *", "Divide" => "Keypad /", "Decimal" => "Keypad .",
        _ => key
    };

    /// <summary>The key with these words (Set Key's list).</summary>
    public static string? FromWords(string words) => Bindable.FirstOrDefault(k => Display(k) == words);

    /// <summary>Ultima 8's keys under the maker's (System Settings > Ultima 8).</summary>
    public static Dictionary<string, string> MakerKeys(Ultima8Settings settings)
    {
        var keys = Actions.ToDictionary(a => a.Id, a => a.DefaultKey);
        foreach (var (id, key) in settings.Keys)
            if (keys.ContainsKey(id) && Bindable.Contains(key)) keys[id] = key;
        return keys;
    }

    /// <summary>
    /// Gives an action a key. Another action on that key: with <paramref name="swap"/> it takes this one's old key (no action
    /// is left without a key), otherwise nothing changes and the answer is false.
    /// </summary>
    public static bool Assign(Dictionary<string, string> keys, string id, string key, bool swap)
    {
        string? holder = keys.FirstOrDefault(kv => kv.Value == key && kv.Key != id).Key;
        if (holder is not null)
        {
            if (!swap) return false;
            keys[holder] = keys[id];
        }
        keys[id] = key;
        return true;
    }

    // ---------- What the player changed: kept on the disk, for good ----------

    private const string PlayerFileName = "keys.json";

    private static string PlayerFile(Guid projectId) => Path.Combine(GameSaveStore.GameFolder(projectId), PlayerFileName);

    /// <summary>The keys the game starts with: the maker's, with what the player changed over them.</summary>
    public static Dictionary<string, string> Load(Ultima8Settings settings, Guid? projectId)
    {
        var keys = MakerKeys(settings);
        if (projectId is not { } id) return keys;
        try
        {
            string file = PlayerFile(id);
            if (!File.Exists(file)) return keys;
            foreach (var (action, key) in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new())
                if (keys.ContainsKey(action) && Bindable.Contains(key)) keys[action] = key;
        }
        catch { /* a broken file: the maker's keys, as if the player had changed nothing */ }
        return keys;
    }

    /// <summary>Keeps what differs from the maker's keys (a key the maker changes later still reaches a player who never touched it). Nothing differing: no file.</summary>
    public static void Save(Ultima8Settings settings, Guid? projectId, Dictionary<string, string> keys)
    {
        if (projectId is not { } id) return;
        var maker = MakerKeys(settings);
        var changed = keys.Where(kv => maker.TryGetValue(kv.Key, out var m) && m != kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
        string file = PlayerFile(id);
        if (changed.Count == 0)
        {
            if (File.Exists(file)) File.Delete(file);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = file + ".saving";
        File.WriteAllText(temp, JsonSerializer.Serialize(changed));
        File.Move(temp, file, overwrite: true);
    }

    /// <summary>True: the player of this game has changed keys (System Settings > Ultima 8 offers to forget them).</summary>
    public static bool HasPlayerKeys(Guid projectId) => File.Exists(PlayerFile(projectId));

    public static void ForgetPlayerKeys(Guid projectId)
    {
        string file = PlayerFile(projectId);
        if (File.Exists(file)) File.Delete(file);
    }
}
