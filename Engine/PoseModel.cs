namespace SandcastleRpgMaker.Engine;

/// <summary>
/// One pose SLOT (e.g. "Walking") - the definition, not any character's
/// actual sprite for it. Project-wide, same relationship to
/// CharacterDefinition.PoseSprites as StatDefinition has to StatValues:
/// defined once here, a value/link per character there.
/// </summary>
public class PoseDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Pose";

    /// <summary>The 11 defaults from spec - hardcoded into the editor and the internal engine itself, so they can be neither deleted NOR renamed. A custom pose can be renamed, edited, and deleted freely, and there's no cap on how many can be added.</summary>
    public bool IsBuiltIn { get; set; }
}

/// <summary>Which sprite (and which group it was filtered from, for the Filter By Group checkbox) a character uses for one specific pose, in one specific facing direction.</summary>
public class PoseSpriteLink
{
    public string SpriteGroup { get; set; } = string.Empty;
    public string SpriteName { get; set; } = string.Empty;
}

/// <summary>
/// The 8 compass directions the internal engine supports for any
/// direction-dependent pose. A character isn't required to fill all 8 -
/// per spec, someone can assign only South/West/North/East (a 4-
/// directional character) and leave the diagonals empty; the engine
/// falls back sensibly (PoseAssignment.Resolve) rather than showing
/// nothing for an unfilled direction.
/// </summary>
public enum SpriteDirection { North, NorthEast, East, SouthEast, South, SouthWest, West, NorthWest }

/// <summary>
/// One character's full assignment for one pose: up to 8 directional
/// sprite links, plus a per-pose mute flag - per spec, muting sound is
/// scoped to "this pose is making noise I don't want right now" rather
/// than to the sprite itself (the same sprite might be fine unmuted
/// elsewhere), so it lives here, not on the sprite or the frame.
/// </summary>
public class PoseAssignment
{
    public Dictionary<SpriteDirection, PoseSpriteLink> Directions { get; set; } = new();

    /// <summary>Muted by default per spec ("for each they are all muted by default") - a person opts in to hearing a pose's sound rather than opting out.</summary>
    public bool Muted { get; set; } = true;

    /// <summary>0-100. Only meaningful while not Muted - raising/lowering volume doesn't itself unmute.</summary>
    public int Volume { get; set; } = 100;

    /// <summary>
    /// The sprite for a specific facing, falling back gracefully if that
    /// exact direction was never assigned - per spec ("you are not forced
    /// to use all 8 directions... the internal engine will accommodate
    /// accordingly"). Falls back to South first (the conventional default
    /// facing), then to whatever direction IS populated, in a fixed order,
    /// so the fallback is at least deterministic rather than random.
    /// </summary>
    public PoseSpriteLink? Resolve(SpriteDirection facing)
    {
        if (Directions.TryGetValue(facing, out var exact)) return exact;
        if (Directions.TryGetValue(SpriteDirection.South, out var south)) return south;
        foreach (SpriteDirection d in Enum.GetValues<SpriteDirection>())
            if (Directions.TryGetValue(d, out var any)) return any;
        return null;
    }
}

public class PoseDatabase
{
    public List<PoseDefinition> Poses { get; set; } = new();
    private int _nextId = 1;

    public int GetNextId() => _nextId++;
    public void SyncNextIdAfterLoad() => _nextId = Poses.Count == 0 ? 1 : Poses.Max(p => p.Id) + 1;

    public static PoseDatabase CreateDefault()
    {
        var db = new PoseDatabase();
        db.EnsureBuiltInPoses();
        return db;
    }

    /// <summary>
    /// Adds any of the 11 canonical built-in poses that are missing,
    /// without touching existing ones (custom or built-in) or their
    /// character sprite links. Defensive repair for a project saved by an
    /// earlier build whose PoseDatabase predates the full 11-pose set -
    /// same idea as SpriteDatabase.EnsureTilesetGroup(). Also renames any
    /// existing "Poisoned"/"Sick" built-ins to "Picking"/"Dancing" - a
    /// real rename, not a remove-and-recreate, so an already-saved
    /// project's existing sprite links for that pose (keyed by pose Id,
    /// not name) survive untouched.
    /// </summary>
    public void EnsureBuiltInPoses()
    {
        foreach (var pose in Poses.Where(p => p.IsBuiltIn))
        {
            if (pose.Name == "Poisoned") pose.Name = "Picking";
            else if (pose.Name == "Sick") pose.Name = "Dancing";
        }

        foreach (string name in new[]
        {
            "Idle", "Walking", "Attacking Melee", "Shooting", "Unconscious",
            "Body", "Fall", "Stand up", "Picking", "Dancing", "Sleeping",
            "Sit" // Exult's sit frame: chairs, benches, and the Sit schedule
        })
        {
            if (!Poses.Any(p => p.IsBuiltIn && p.Name == name))
                Poses.Add(new PoseDefinition { Id = GetNextId(), Name = name, IsBuiltIn = true });
        }
    }
}
