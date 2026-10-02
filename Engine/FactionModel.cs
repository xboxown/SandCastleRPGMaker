namespace SandcastleRpgMaker.Engine;

/// <summary>
/// What a member of one faction does about someone, decided by which band
/// their relationship value falls in. Exult only has the fixed alignment
/// table (combat.cc is_enemy: good vs evil/chaotic, chaotic vs both, neutral
/// never) - this is the designer-tunable generalisation of it.
/// </summary>
public enum FactionBehavior
{
    /// <summary>Hostile: attacks on sight (Exult's "enemy").</summary>
    Attack,
    /// <summary>Neutral: leaves them alone (Exult's neutral alignment).</summary>
    Ignore,
    /// <summary>Friendly: comes to their defence when they're attacked.</summary>
    Ally,
    /// <summary>Afraid: runs from them.</summary>
    Flee
}

/// <summary>One slice of the 0-100% relationship scale. Bands are laid end to end from 0% in list order and must add up to 100%.</summary>
public class FactionBand
{
    public string Name { get; set; } = "New Band";
    public int WidthPercent { get; set; } = 10;
    public FactionBehavior Behavior { get; set; } = FactionBehavior.Ignore;
    /// <summary>Only for the editor's grid, so a glance shows who hates whom. #RRGGBB.</summary>
    public string ColorHex { get; set; } = "#C0C0C0";
}

public class Faction
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Faction";
    public string Notes { get; set; } = string.Empty;

    /// <summary>The face shown for this faction in the relationship grid.</summary>
    public string FaceSpriteGroup { get; set; } = string.Empty;
    public string FaceSpriteName { get; set; } = string.Empty;

    /// <summary>
    /// One-way: how THIS faction's members see each other faction, keyed by
    /// the other faction's Id, 0-100%. A missing entry means the database's
    /// DefaultRelation (or 100% toward itself).
    /// </summary>
    public Dictionary<int, int> Relations { get; set; } = new();

    /// <summary>How far (in %) this faction's opinion of the offender's faction drops when one of its members is attacked.</summary>
    public int AttackPenalty { get; set; } = 20;
    /// <summary>How far it drops when something owned by one of its members (or by the faction) is stolen.</summary>
    public int TheftPenalty { get; set; } = 10;
    /// <summary>How far it drops when one of its members is killed.</summary>
    public int KillPenalty { get; set; } = 50;
}

/// <summary>Why a faction's opinion of someone just got worse.</summary>
public enum FactionIncident { Attack, Theft, Kill }

public class FactionDatabase
{
    public List<Faction> Factions { get; set; } = new();

    /// <summary>The single, game-wide behaviour scale, from 0% upward. Must total 100%.</summary>
    public List<FactionBand> Bands { get; set; } = DefaultBands();

    /// <summary>The relationship assumed between two factions nobody has set, and toward/from characters with no faction.</summary>
    public int DefaultRelation { get; set; } = 50;

    private int _nextId = 1;

    public static List<FactionBand> DefaultBands() => new()
    {
        new FactionBand { Name = "Hostile", WidthPercent = 25, Behavior = FactionBehavior.Attack, ColorHex = "#E57373" },
        new FactionBand { Name = "Afraid", WidthPercent = 15, Behavior = FactionBehavior.Flee, ColorHex = "#FFB74D" },
        new FactionBand { Name = "Neutral", WidthPercent = 30, Behavior = FactionBehavior.Ignore, ColorHex = "#BDBDBD" },
        new FactionBand { Name = "Allied", WidthPercent = 30, Behavior = FactionBehavior.Ally, ColorHex = "#81C784" }
    };

    public Faction CreateNew()
    {
        var f = new Faction { Id = _nextId++, Name = $"Faction {_nextId - 1}" };
        Factions.Add(f);
        return f;
    }

    public void SyncNextIdAfterLoad()
    {
        _nextId = Factions.Count == 0 ? 1 : Factions.Max(f => f.Id) + 1;
        if (Bands is null || Bands.Count == 0) Bands = DefaultBands(); // projects saved before bands existed
        foreach (var faction in Factions) faction.Relations ??= new();
    }

    public Faction? Find(int id) => id < 0 ? null : Factions.FirstOrDefault(f => f.Id == id);

    public int TotalBandPercent() => Bands.Sum(b => b.WidthPercent);

    /// <summary>How faction <paramref name="viewerId"/> sees faction <paramref name="targetId"/> (-1 = no faction), 0-100.</summary>
    public int GetRelation(int viewerId, int targetId)
    {
        var viewer = Find(viewerId);
        if (viewer is null || Find(targetId) is null) return DefaultRelation;
        if (viewer.Relations.TryGetValue(targetId, out int value)) return Math.Clamp(value, 0, 100);
        return viewerId == targetId ? 100 : DefaultRelation;
    }

    public void SetRelation(int viewerId, int targetId, int value)
    {
        if (Find(viewerId) is not { } viewer || Find(targetId) is null) return;
        viewer.Relations[targetId] = Math.Clamp(value, 0, 100);
    }

    /// <summary>The band a relationship value falls in: bands laid end to end from 0%; 100% belongs to the last band.</summary>
    public FactionBand? BandFor(int value)
    {
        if (Bands.Count == 0) return null;
        int start = 0;
        foreach (var band in Bands)
        {
            if (value < start + band.WidthPercent) return band;
            start += band.WidthPercent;
        }
        return Bands[^1];
    }

    /// <summary>What a member of <paramref name="viewerId"/> does about a member of <paramref name="targetId"/>.</summary>
    public FactionBehavior BehaviorToward(int viewerId, int targetId) => BandFor(GetRelation(viewerId, targetId))?.Behavior ?? FactionBehavior.Ignore;

    /// <summary>
    /// Someone of faction <paramref name="offenderId"/> attacked, robbed or
    /// killed a member of <paramref name="victimId"/>: the victim's faction
    /// thinks less of the offender's by that faction's own penalty. One-way,
    /// like every relationship here.
    /// </summary>
    public void ApplyIncident(int victimId, int offenderId, FactionIncident incident)
    {
        if (Find(victimId) is not { } victim || Find(offenderId) is null) return;
        int penalty = incident switch
        {
            FactionIncident.Attack => victim.AttackPenalty,
            FactionIncident.Theft => victim.TheftPenalty,
            _ => victim.KillPenalty
        };
        SetRelation(victimId, offenderId, GetRelation(victimId, offenderId) - penalty);
    }

    /// <summary>Drops every relationship pointing at a faction that no longer exists.</summary>
    public void Remove(Faction faction)
    {
        Factions.Remove(faction);
        foreach (var other in Factions) other.Relations.Remove(faction.Id);
    }

    /// <summary>A fully separate copy, so a running game can change relationships without touching the project.</summary>
    public FactionDatabase Clone()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(this);
        var clone = System.Text.Json.JsonSerializer.Deserialize<FactionDatabase>(json) ?? new FactionDatabase();
        clone.SyncNextIdAfterLoad();
        return clone;
    }
}
