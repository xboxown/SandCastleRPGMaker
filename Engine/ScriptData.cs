using System.Globalization;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// The running game's script data beyond number variables and switches (those
/// stay in the event context, by name): text variables, one-dimension arrays,
/// two-dimension grids and each event's own self switches. A value is either a
/// number (double) or a text (string) - see ScriptValues.
/// </summary>
public class ScriptData
{
    /// <summary>Text variables, by name. A name set to a number leaves this list.</summary>
    public Dictionary<string, string> Texts { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Arrays by name: each element a number or a text. Index 0 is the first.</summary>
    public Dictionary<string, List<object>> Arrays { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Grids by name (x = column, y = row, both from 0).</summary>
    public Dictionary<string, ScriptGrid> Grids { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Control Self Switch: "event key|A" -> on.</summary>
    public HashSet<string> SelfSwitches { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Debug Log lines, newest last (Game Test only; the oldest go past 500).</summary>
    public List<string> Log { get; } = new();

    /// <summary>A line was added to the debug log.</summary>
    public event Action<string>? Logged;

    public void AddLog(string line)
    {
        Log.Add(line);
        Logged?.Invoke(line);
        if (Log.Count > 500) Log.RemoveRange(0, Log.Count - 500);
    }

    public List<object> ArrayOf(string name) =>
        Arrays.TryGetValue(name, out var list) ? list : throw new ScriptException($"there is no array called \"{name}\" (Create Array first)");

    public ScriptGrid GridOf(string name) =>
        Grids.TryGetValue(name, out var grid) ? grid : throw new ScriptException($"there is no grid called \"{name}\" (Create Grid first)");

    // ---------- Saving ----------

    public SavedScriptData ToSave() => new()
    {
        Texts = new Dictionary<string, string>(Texts),
        Arrays = Arrays.ToDictionary(kv => kv.Key, kv => kv.Value.Select(ScriptValues.Encode).ToList()),
        Grids = Grids.ToDictionary(kv => kv.Key, kv => new SavedGrid { Columns = kv.Value.Columns, Rows = kv.Value.Rows, Cells = kv.Value.Cells.Select(ScriptValues.Encode).ToList() }),
        SelfSwitches = SelfSwitches.ToList()
    };

    public void Load(SavedScriptData? saved)
    {
        Texts.Clear(); Arrays.Clear(); Grids.Clear(); SelfSwitches.Clear();
        if (saved is null) return;
        foreach (var (k, v) in saved.Texts) Texts[k] = v;
        foreach (var (k, v) in saved.Arrays) Arrays[k] = v.Select(ScriptValues.Decode).ToList();
        foreach (var (k, g) in saved.Grids)
        {
            var grid = new ScriptGrid(g.Columns, g.Rows);
            for (int i = 0; i < Math.Min(grid.Cells.Length, g.Cells.Count); i++) grid.Cells[i] = ScriptValues.Decode(g.Cells[i]);
            Grids[k] = grid;
        }
        foreach (var s in saved.SelfSwitches) SelfSwitches.Add(s);
    }
}

/// <summary>A two-dimension array: Columns x Rows cells, each a number or a text.</summary>
public class ScriptGrid
{
    public int Columns { get; private set; }
    public int Rows { get; private set; }
    public object[] Cells { get; private set; }

    public ScriptGrid(int columns, int rows, object? fill = null)
    {
        Columns = Math.Max(0, columns);
        Rows = Math.Max(0, rows);
        Cells = new object[Columns * Rows];
        Array.Fill(Cells, fill ?? 0.0);
    }

    public bool Inside(int x, int y) => x >= 0 && y >= 0 && x < Columns && y < Rows;

    public object this[int x, int y]
    {
        get => Inside(x, y) ? Cells[y * Columns + x] : throw new ScriptException($"cell ({x}, {y}) is outside the grid ({Columns} x {Rows})");
        set { if (!Inside(x, y)) throw new ScriptException($"cell ({x}, {y}) is outside the grid ({Columns} x {Rows})"); Cells[y * Columns + x] = value; }
    }

    /// <summary>Resize Grid: what fits stays where it was, new cells get the fill value.</summary>
    public void Resize(int columns, int rows, object fill)
    {
        var old = Cells;
        int oldColumns = Columns, oldRows = Rows;
        Columns = Math.Max(0, columns);
        Rows = Math.Max(0, rows);
        Cells = new object[Columns * Rows];
        Array.Fill(Cells, fill);
        for (int y = 0; y < Math.Min(oldRows, Rows); y++)
            for (int x = 0; x < Math.Min(oldColumns, Columns); x++)
                Cells[y * Columns + x] = old[y * oldColumns + x];
    }
}

/// <summary>Script data in a saved game: values as "n:5" (number) or "t:words" (text).</summary>
public class SavedScriptData
{
    public Dictionary<string, string> Texts { get; set; } = new();
    public Dictionary<string, List<string>> Arrays { get; set; } = new();
    public Dictionary<string, SavedGrid> Grids { get; set; } = new();
    public List<string> SelfSwitches { get; set; } = new();
}

public class SavedGrid
{
    public int Columns { get; set; }
    public int Rows { get; set; }
    public List<string> Cells { get; set; } = new();
}

/// <summary>Something wrong in a formula or a script command - the message is written for the designer.</summary>
public class ScriptException : Exception
{
    public ScriptException(string message) : base(message) { }
}

/// <summary>Numbers and texts: how one becomes the other, how they compare and how they're shown.</summary>
public static class ScriptValues
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static bool TryNumber(object? value, out double number)
    {
        switch (value)
        {
            case double d: number = d; return true;
            case bool b: number = b ? 1 : 0; return true;
            case string s when double.TryParse(s.Trim(), NumberStyles.Float, Inv, out number): return true;
            default: number = 0; return false;
        }
    }

    public static double Number(object? value) => TryNumber(value, out double n) ? n : 0;

    public static string Text(object? value) => value switch
    {
        null => string.Empty,
        double d => d == Math.Floor(d) && Math.Abs(d) < 1e15 ? ((long)d).ToString(Inv) : d.ToString("0.######", Inv),
        bool b => b ? "1" : "0",
        _ => value.ToString() ?? string.Empty
    };

    public static bool Truth(object? value) => value switch
    {
        double d => d != 0,
        bool b => b,
        string s => s.Length > 0 && s != "0" && !s.Equals("false", StringComparison.OrdinalIgnoreCase),
        _ => false
    };

    /// <summary>Numbers compare as numbers (a text that reads as a number too); otherwise as texts, ignoring upper / lower case.</summary>
    public static int Compare(object? a, object? b)
    {
        if (TryNumber(a, out double x) && TryNumber(b, out double y)) return x.CompareTo(y);
        return string.Compare(Text(a), Text(b), StringComparison.OrdinalIgnoreCase);
    }

    public static bool Same(object? a, object? b) => Compare(a, b) == 0;

    public static string Encode(object value) => value is string s ? "t:" + s : "n:" + Text(value);

    public static object Decode(string saved) =>
        saved.StartsWith("t:") ? saved[2..] : double.TryParse(saved.StartsWith("n:") ? saved[2..] : saved, NumberStyles.Float, Inv, out double d) ? d : saved;

    /// <summary>A value typed into a command's box with no formula: a number if it reads as one, else the text.</summary>
    public static object Literal(string typed) => double.TryParse(typed.Trim(), NumberStyles.Float, Inv, out double d) ? d : typed;
}
