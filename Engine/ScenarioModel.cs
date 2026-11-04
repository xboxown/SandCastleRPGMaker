namespace SandcastleRpgMaker.Engine;

/// <summary>How a scenario treats whether a character is in the party.</summary>
public enum ScenarioPartyRule { AsInTheGame, Joined, NotJoined }

public class ScenarioVariable
{
    public string Name { get; set; } = string.Empty;
    /// <summary>A number, true / false, or words.</summary>
    public string Value { get; set; } = "0";
}

public class ScenarioItem
{
    public ReferencedItemCategory Category { get; set; } = ReferencedItemCategory.Object;
    public int ItemId { get; set; } = -1;
    public int Quantity { get; set; } = 1;
    public string DisplayName { get; set; } = string.Empty;
}

/// <summary>What a scenario changes about one character. Anything not set stays as the game starts it.</summary>
public class ScenarioCharacter
{
    public int CharacterId { get; set; }
    public ScenarioPartyRule Party { get; set; } = ScenarioPartyRule.AsInTheGame;
    /// <summary>Starts dead: off the map, on every map, with no body.</summary>
    public bool Dead { get; set; }
    /// <summary>Ticked: it carries only Items. Unticked: Items are added to what it carries.</summary>
    public bool OnlyTheseItems { get; set; }
    public List<ScenarioItem> Items { get; set; } = new();
    /// <summary>Stat id to value; a stat not here stays as it is.</summary>
    public Dictionary<int, string> Stats { get; set; } = new();
}

/// <summary>A place and state to jump into while testing (Game > Scenario Developer). Game Test only: a built game never looks at it.</summary>
public class Scenario
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Scenario";
    /// <summary>Unticked: kept, but F5 and the keys skip it.</summary>
    public bool On { get; set; } = true;
    /// <summary>The key that jumps here during Game Test, named as U8Keys names them ("F2", "Ctrl+2"); empty: none.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>The map (world) to start on; 0: the first.</summary>
    public int WorldId { get; set; }
    public int X { get; set; } = -1;
    public int Y { get; set; } = -1;
    public int Floor { get; set; } = 1;
    public List<ScenarioVariable> Variables { get; set; } = new();
    public List<ScenarioCharacter> Characters { get; set; } = new();

    public ScenarioCharacter For(int characterId)
    {
        var entry = Characters.FirstOrDefault(c => c.CharacterId == characterId);
        if (entry is null) Characters.Add(entry = new ScenarioCharacter { CharacterId = characterId });
        return entry;
    }
}

/// <summary>The project's scenarios (System Settings keeps them, with the project).</summary>
public class ScenarioSettings
{
    public List<Scenario> Scenarios { get; set; } = new();
    /// <summary>F5 starts here; -1: as usual (the Title Screen or the map's start).</summary>
    public int StartScenarioId { get; set; } = -1;
    /// <summary>The key that jumps to the next scenario in the list, round and round; empty: none.</summary>
    public string NextKey { get; set; } = string.Empty;

    public Scenario? Find(int id) => Scenarios.FirstOrDefault(s => s.Id == id);
    public Scenario? StartScenario => Scenarios.FirstOrDefault(s => s.Id == StartScenarioId && s.On);
    public int NextId() => Scenarios.Count == 0 ? 1 : Scenarios.Max(s => s.Id) + 1;

    /// <summary>The next scenario that's on after this one, round to the first; null: none is on.</summary>
    public Scenario? After(int id)
    {
        var on = Scenarios.Where(s => s.On).ToList();
        if (on.Count == 0) return null;
        int at = on.FindIndex(s => s.Id == id);
        return on[(at + 1) % on.Count];
    }
}
