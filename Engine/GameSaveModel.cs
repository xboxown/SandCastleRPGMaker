using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// One saved game - everything a game in progress has changed from the
/// project: the map as it is now (what's where, doors, objects' own states,
/// the main character's spot as its Start Position), every character
/// (inventory, stats, party), containers, factions, the clock, variables,
/// hunger and ailments, and which event pages have already fired (so a
/// once-only cutscene stays done). Loading builds the game from it the way
/// a new game is built from the project. What's under way the instant it's
/// saved - a walk, a fight, a script - isn't kept; Exult likewise refuses to
/// save mid-script.
/// </summary>
public class GameSaveData
{
    public const int CurrentFormat = 1;

    public int Format { get; set; } = CurrentFormat;
    public MapData Map { get; set; } = new();
    public CharacterDatabase Characters { get; set; } = new();
    public ContainerDatabase Containers { get; set; } = new();
    public FactionDatabase Factions { get; set; } = new();

    public int Day { get; set; }
    public int Hour { get; set; }
    public int Minute { get; set; }
    public int Ticks { get; set; }

    /// <summary>The main character's exact spot and facing (the map's Start Position holds the cell and floor).</summary>
    public double PlayerPixelX { get; set; }
    public double PlayerPixelY { get; set; }
    public int PlayerLift { get; set; }
    public SpriteDirection PlayerFacing { get; set; } = SpriteDirection.South;
    public bool CombatMode { get; set; }

    /// <summary>Variables and switches, as the running game holds them (by name).</summary>
    public Dictionary<string, double> Variables { get; set; } = new();
    /// <summary>Change Book Content: (book, page, frame) -> text.</summary>
    public List<SavedBookContent> BookContents { get; set; } = new();
    /// <summary>Change Book Settings: (book, field) -> value.</summary>
    public List<SavedBookSetting> BookSettings { get; set; } = new();
    /// <summary>Books read at least once (By After Read Item, "has been read").</summary>
    public List<int> ReadBooks { get; set; } = new();

    public List<SavedVitals> Vitals { get; set; } = new();
    public List<SavedFiredPage> FiredPages { get; set; } = new();
    public WeatherSettings? Weather { get; set; }
}

public class SavedBookContent
{
    public int BookId { get; set; }
    public int PageIndex { get; set; }
    public int FrameId { get; set; }
    public string Text { get; set; } = string.Empty;
}

public class SavedBookSetting
{
    public int BookId { get; set; }
    public string Field { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}

/// <summary>A character's hunger, ailments and the time left on them.</summary>
public class SavedVitals
{
    public int CharacterId { get; set; }
    public int Food { get; set; }
    public int Ailments { get; set; }
    public double PoisonRemainingMs { get; set; }
    public double SleepRemainingMs { get; set; }
    public double ParalyzeRemainingMs { get; set; }
    public double CharmRemainingMs { get; set; }
    public double CurseRemainingMs { get; set; }
    public double InvisibleRemainingMs { get; set; }
    public double ProtectedRemainingMs { get; set; }
    public bool Dead { get; set; }
    public bool Unconscious { get; set; }
}

/// <summary>How many times one event page has fired: its owner ("Event:3", "Character:2", "Book:5", "Container:1", "Object:12" - a placed object's instance) and its place in the owner's pages.</summary>
public class SavedFiredPage
{
    public string Owner { get; set; } = string.Empty;
    public int PageIndex { get; set; }
    public int Count { get; set; }
}

/// <summary>A save as the Save/Load screen lists it.</summary>
public class SaveSlotInfo
{
    public string Folder { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public DateTime SavedAtUtc { get; set; }
    /// <summary>The game's own time ("Day 3, 9:15 AM").</summary>
    public string GameTime { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public string ScreenshotPath => Path.Combine(Folder, GameSaveStore.ScreenshotFileName);
}

/// <summary>
/// Saved games on disk: one folder per game (by project id) under the
/// player's own app data, one sub-folder per save - its listing (info.json),
/// the save itself (game.sav, encrypted with the project's key like the
/// project files) and a screenshot (shot.png). As many saves as the player
/// wants, as in Exult.
/// </summary>
public static class GameSaveStore
{
    public const string ScreenshotFileName = "shot.png";
    private const string InfoFileName = "info.json";
    private const string SaveFileName = "game.sav";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public static string GameFolder(Guid projectId) =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SandcastleRpgMaker", "Saves", projectId.ToString("N"));

    /// <summary>Every save of this game, newest first.</summary>
    public static List<SaveSlotInfo> List(Guid projectId)
    {
        var saves = new List<SaveSlotInfo>();
        string root = GameFolder(projectId);
        if (!Directory.Exists(root)) return saves;
        foreach (var folder in Directory.EnumerateDirectories(root))
        {
            try
            {
                string infoPath = Path.Combine(folder, InfoFileName);
                if (!File.Exists(infoPath) || !File.Exists(Path.Combine(folder, SaveFileName))) continue;
                var info = JsonSerializer.Deserialize<SaveSlotInfo>(File.ReadAllText(infoPath), JsonOptions);
                if (info is null) continue;
                info.Folder = folder;
                saves.Add(info);
            }
            catch { /* a broken save is left out of the list */ }
        }
        return saves.OrderByDescending(s => s.SavedAtUtc).ToList();
    }

    /// <summary>Writes a save - into <paramref name="overwrite"/>'s folder, or a new one. The screenshot is PNG bytes (or null).</summary>
    public static SaveSlotInfo Write(Guid projectId, GameSaveData data, SaveSlotInfo info, byte[]? screenshotPng, SaveSlotInfo? overwrite)
    {
        string folder = overwrite?.Folder ?? Path.Combine(GameFolder(projectId), $"save_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}");
        Directory.CreateDirectory(folder);
        string json = JsonSerializer.Serialize(data, JsonOptions);
        string temp = Path.Combine(folder, SaveFileName + ".saving");
        ProjectContainer.Save(temp, projectId, json);
        File.Move(temp, Path.Combine(folder, SaveFileName), overwrite: true);
        if (screenshotPng is not null) File.WriteAllBytes(Path.Combine(folder, ScreenshotFileName), screenshotPng);
        info.Folder = folder;
        File.WriteAllText(Path.Combine(folder, InfoFileName), JsonSerializer.Serialize(info, JsonOptions));
        return info;
    }

    public static GameSaveData Read(Guid projectId, SaveSlotInfo save)
    {
        string json = ProjectContainer.Load(Path.Combine(save.Folder, SaveFileName), projectId);
        var data = JsonSerializer.Deserialize<GameSaveData>(json, JsonOptions)
                   ?? throw new ProjectFileException($"The save \"{save.Name}\" couldn't be read.");
        if (data.Format > GameSaveData.CurrentFormat)
            throw new ProjectFileException($"The save \"{save.Name}\" was made by a newer version of this game.");
        return data;
    }

    public static void Delete(SaveSlotInfo save)
    {
        if (Directory.Exists(save.Folder)) Directory.Delete(save.Folder, recursive: true);
    }
}
