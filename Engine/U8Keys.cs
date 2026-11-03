using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

/// <summary>Where an action's key counts. A key can do one thing on the map and another on a screen (the Up Arrow walks and moves a reply's cursor); Everywhere clashes with both.</summary>
public enum U8KeyGroup { World, Screens, Everywhere }

/// <summary>Something the player does with a key in an Ultima 8 project. %key + its id in lower case shows its key on a paperdoll (%keyjump).</summary>
public sealed record U8KeyAction(string Id, string Name, string DefaultKey, U8KeyGroup Group = U8KeyGroup.World);

/// <summary>
/// The keys of an Ultima 8 project: what each action starts on (Ultima 8's, as the engine had them), what the maker set
/// (System Settings > Ultima 8) and what the player changed in the game - kept beside the game's saves, in no save slot, so
/// they hold through new games and loads until changed again. Keys are named as WPF names them ("LeftShift"), never drawn
/// as such: Display gives the words; "Ctrl+Q" is a key held with Ctrl, Shift or Alt. Every key the game reads is an action here: nothing
/// is fixed but what Windows keeps for itself (the Windows key, Print Screen, Alt+F4).
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
        new("WholeStack", "Move a whole stack, no question (held)", "LeftCtrl"),
        new("TestNextPeriod", "Game Test: next schedule period", "F6"),
        new("TestScriptState", "Game Test: script data", "F9"),
        new("TestFrameRate", "Game Test: frame rate", "F10"),
        new("Quit", "Quit prompt", "Ctrl+Q", U8KeyGroup.Everywhere),
        new("Cancel", "Cancel / close", "Escape", U8KeyGroup.Everywhere),
        new("Number1", "Party member / reply 1", "D1", U8KeyGroup.Everywhere),
        new("Number2", "Party member / reply 2", "D2", U8KeyGroup.Everywhere),
        new("Number3", "Party member / reply 3", "D3", U8KeyGroup.Everywhere),
        new("Number4", "Party member / reply 4", "D4", U8KeyGroup.Everywhere),
        new("Number5", "Party member / reply 5", "D5", U8KeyGroup.Everywhere),
        new("Number6", "Party member / reply 6", "D6", U8KeyGroup.Everywhere),
        new("Number7", "Party member / reply 7", "D7", U8KeyGroup.Everywhere),
        new("Number8", "Party member / reply 8", "D8", U8KeyGroup.Everywhere),
        new("Number9", "Party member / reply 9", "D9", U8KeyGroup.Everywhere),
        new("Number1Second", "Party member / reply 1 (second key)", "NumPad1", U8KeyGroup.Everywhere),
        new("Number2Second", "Party member / reply 2 (second key)", "NumPad2", U8KeyGroup.Everywhere),
        new("Number3Second", "Party member / reply 3 (second key)", "NumPad3", U8KeyGroup.Everywhere),
        new("Number4Second", "Party member / reply 4 (second key)", "NumPad4", U8KeyGroup.Everywhere),
        new("Number5Second", "Party member / reply 5 (second key)", "NumPad5", U8KeyGroup.Everywhere),
        new("Number6Second", "Party member / reply 6 (second key)", "NumPad6", U8KeyGroup.Everywhere),
        new("Number7Second", "Party member / reply 7 (second key)", "NumPad7", U8KeyGroup.Everywhere),
        new("Number8Second", "Party member / reply 8 (second key)", "NumPad8", U8KeyGroup.Everywhere),
        new("Number9Second", "Party member / reply 9 (second key)", "NumPad9", U8KeyGroup.Everywhere),
        new("Accept", "Accept / OK", "Enter", U8KeyGroup.Screens),
        new("Accept2", "Accept (second key)", "Space", U8KeyGroup.Screens),
        new("ScreenUp", "Previous choice", "Up", U8KeyGroup.Screens),
        new("ScreenUp2", "Previous choice (second key), slider down", "Left", U8KeyGroup.Screens),
        new("ScreenDown", "Next choice", "Down", U8KeyGroup.Screens),
        new("ScreenDown2", "Next choice (second key), slider up", "Right", U8KeyGroup.Screens),
        new("ScreenDown3", "Next choice (third key)", "Tab", U8KeyGroup.Screens),
        new("PagePrev", "Previous page of replies", "PageUp", U8KeyGroup.Screens),
        new("PageNext", "Next page of replies", "PageDown", U8KeyGroup.Screens),
        new("Erase", "Erase a typed letter", "Back", U8KeyGroup.Screens),
    };

    public static readonly string[] ActionNames = Actions.Select(a => a.Name).ToArray();

    /// <summary>Longest id first: %keyaccept2 is filled in before %keyaccept can take the front of it.</summary>
    public static readonly U8KeyAction[] ActionsLongestIdFirst = Actions.OrderByDescending(a => a.Id.Length).ToArray();

    public static U8KeyAction? ByName(string name) => Actions.FirstOrDefault(a => a.Name == name);

    public static readonly string[] Bindable = Enumerable.Range('A', 26).Select(c => ((char)c).ToString())
        .Concat(new[]
        {
            "D0", "D1", "D2", "D3", "D4", "D5", "D6", "D7", "D8", "D9",
            "NumPad0", "NumPad1", "NumPad2", "NumPad3", "NumPad4", "NumPad5", "NumPad6", "NumPad7", "NumPad8", "NumPad9",
            "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
            "Up", "Down", "Left", "Right", "Space", "Enter", "Tab", "Back", "Escape", "Insert", "Delete", "Home", "End", "PageUp", "PageDown",
            "LeftShift", "RightShift", "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt", "CapsLock", "NumLock", "Scroll", "Pause",
            "OemTilde", "OemMinus", "OemPlus", "OemOpenBrackets", "OemCloseBrackets", "OemSemicolon", "OemQuotes", "OemComma", "OemPeriod", "OemQuestion", "OemPipe",
            "Add", "Subtract", "Multiply", "Divide", "Decimal",
        }).ToArray();

    /// <summary>What a key can be held with ("Ctrl+Q"). The Shifts, Ctrls and Alts can also be keys themselves, but then alone.</summary>
    public static readonly string[] HeldWith = { "Ctrl", "Shift", "Alt" };

    private static readonly string[] Modifiers = { "LeftShift", "RightShift", "LeftCtrl", "RightCtrl", "LeftAlt", "RightAlt" };

    /// <summary>A key's name with what it's held with apart: "Ctrl+Q" is ({"Ctrl"}, "Q").</summary>
    public static (string[] Held, string Key) Split(string key)
    {
        string[] parts = key.Split('+');
        return (parts[..^1], parts[^1]);
    }

    /// <summary>A key this game can read: in Bindable, and a Shift, Ctrl or Alt isn't held with anything.</summary>
    public static bool IsKeyName(string key)
    {
        var (held, name) = Split(key);
        return Bindable.Contains(name) && held.All(HeldWith.Contains) && (held.Length == 0 || !Modifiers.Contains(name));
    }

    /// <summary>The words for every key that can be taken, in Bindable's order (Set Key's list).</summary>
    public static readonly string[] BindableWords = Bindable.Select(Display).ToArray();

    /// <summary>A key's name as the player reads it.</summary>
    public static string Display(string key)
    {
        var (held, name) = Split(key);
        return string.Concat(held.Select(h => h + "+")) + DisplayOne(name);
    }

    private static string DisplayOne(string key) => key switch
    {
        { Length: 2 } when key[0] == 'D' && char.IsDigit(key[1]) => key[1..],
        { Length: 7 } when key.StartsWith("NumPad") => "Keypad " + key[6],
        "Up" => "Up Arrow", "Down" => "Down Arrow", "Left" => "Left Arrow", "Right" => "Right Arrow",
        "Back" => "Backspace", "PageUp" => "Page Up", "PageDown" => "Page Down",
        "LeftShift" => "Left Shift", "RightShift" => "Right Shift", "LeftCtrl" => "Left Ctrl", "RightCtrl" => "Right Ctrl", "LeftAlt" => "Left Alt", "RightAlt" => "Right Alt",
        "CapsLock" => "Caps Lock", "NumLock" => "Num Lock", "Scroll" => "Scroll Lock",
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
            if (keys.ContainsKey(id) && IsKeyName(key)) keys[id] = key;
        return keys;
    }

    /// <summary>
    /// Gives an action a key. Another action that clashes on that key: with <paramref name="swap"/> it takes this one's old key (no
    /// action is left without a key), otherwise nothing changes and the answer is false. Also false, with nothing changed, when the
    /// old key would clash in its new place (Everywhere's Cancel can't take the Up Arrow while it still walks and picks replies).
    /// </summary>
    public static bool Assign(Dictionary<string, string> keys, string id, string key, bool swap)
    {
        string old = keys[id];
        if (old == key) return true;
        var action = Actions.First(a => a.Id == id);
        var holders = Actions.Where(a => a.Id != id && keys[a.Id] == key && Clash(a, action)).ToList();
        if (holders.Count > 0 && !swap) return false;
        if (holders.Any(h => Actions.Any(o => o.Id != id && o.Id != h.Id && keys[o.Id] == old && Clash(o, h)))) return false;
        foreach (var holder in holders) keys[holder.Id] = old;
        keys[id] = key;
        return true;
    }

    private static bool Clash(U8KeyAction a, U8KeyAction b) =>
        a.Group == U8KeyGroup.Everywhere || b.Group == U8KeyGroup.Everywhere || a.Group == b.Group;

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
                if (keys.ContainsKey(action) && IsKeyName(key)) keys[action] = key;
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
