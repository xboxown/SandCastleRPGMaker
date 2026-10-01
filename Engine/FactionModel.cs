namespace SandcastleRpgMaker.Engine;

public class Faction
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Faction";
    public string Notes { get; set; } = string.Empty;
}

public class FactionDatabase
{
    public List<Faction> Factions { get; set; } = new();

    private int _nextId = 1;

    public Faction CreateNew()
    {
        var f = new Faction { Id = _nextId++, Name = $"Faction {_nextId - 1}" };
        Factions.Add(f);
        return f;
    }

    public void SyncNextIdAfterLoad()
    {
        _nextId = Factions.Count == 0 ? 1 : Factions.Max(f => f.Id) + 1;
    }
}
