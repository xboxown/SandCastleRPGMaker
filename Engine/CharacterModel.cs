namespace SandcastleRpgMaker.Engine;

public enum CharacterGender { Male, Female }

/// <summary>
/// Basic character sheet, shared shape for PC and NPC (the doc only
/// details the PC Editor today, but nothing here is PC-specific - an NPC
/// editor later can reuse this same class rather than a parallel one).
/// Deliberately minimal per spec ("we are only doing the basics for
/// now... just the stat base itself") - no inventory, no poses wiring
/// yet, no real Class system (ClassName is a free string, "(None)" by
/// default, until a real Class editor exists).
/// </summary>
public class CharacterDefinition
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Character";
    public int Age { get; set; } = 18;
    public CharacterGender Gender { get; set; } = CharacterGender.Male;

    /// <summary>Free string, not a real Class database reference - no Class system exists yet. "(None)" means unset.</summary>
    public string ClassName { get; set; } = "(None)";

    /// <summary>Which faction this character belongs to (FactionDatabase id), -1 = none. Decides how it treats everyone else and how they treat it.</summary>
    public int FactionId { get; set; } = -1;

    /// <summary>Which sprite group the Face sprite was picked from - empty means "search every group" (Filter unchecked).</summary>
    public string FaceSpriteGroup { get; set; } = string.Empty;
    public string FaceSpriteName { get; set; } = string.Empty;

    /// <summary>Keyed by StatDefinition.Id, not name - a stat can be renamed without orphaning every character's value for it. Stored as a string regardless of the stat's own Integer/String type, matching how EventCommand.Parameters already stores everything as text.</summary>
    public Dictionary<int, string> StatValues { get; set; } = new();

    /// <summary>Keyed by PoseDefinition.Id, same reasoning as StatValues - a pose can be renamed (if custom) without breaking every character's sprite link to it. Each pose now holds up to 8 directional sprite links plus its own mute flag, not a single sprite - per spec, the engine supports 8-directional facing.</summary>
    public Dictionary<int, PoseAssignment> PoseSprites { get; set; } = new();

    /// <summary>Which character the Internal Engine test controls - only one can be true at a time, same "only one" rule as Start Position. Toggled from the PC Editor.</summary>
    public bool IsTestPlayer { get; set; }

    /// <summary>
    /// Exult-style daily schedule (Schedule_change list): at the start of each
    /// 3-hour period with an entry, the character goes to that entry's spot
    /// and does its activity. Always empty for the main player, who is
    /// controlled by the person playing, not by AI.
    /// </summary>
    public List<ScheduleEntry> Schedules { get; set; } = new();

    /// <summary>What happens when the player double-clicks this character (Exult: Actor::activate runs its conversation) - event commands, usually Show Text lines spoken by Self.</summary>
    public List<EventCommand> TalkCommands { get; set; } = new();

    /// <summary>Food, poison and ailment immunities (Character Editor > Health).</summary>
    public SurvivalSettings Survival { get; set; } = new();

    /// <summary>The body left behind on death (Character Editor > Health): a container holding everything it carried.</summary>
    public BodySettings Body { get; set; } = new();

    /// <summary>This character's own events (Character Editor > Event): By Equip, By UnEquip, By Hit, By Ailment Effect, By Death.</summary>
    public List<BookEventPage> EventPages { get; set; } = new();

    /// <summary>
    /// Exult-style 3D size - the same thing every shape in Exult carries
    /// (Shape_info dims: x/y footprint in tiles, z height in lifts), and the
    /// same SizeX/SizeY/SizeZ books, containers and objects already have.
    /// SizeX/SizeY are the footprint in 8px world units, SizeZ the height in
    /// lifts. The engine uses them for an actor exactly as Exult does: the
    /// whole footprint must be free from the actor's lift up to lift + SizeZ,
    /// so an actor never walks into or under anything lower than it is tall.
    /// 0 means "automatic" - derived from the Idle sprite (see ActorSizing).
    /// </summary>
    public int SizeX { get; set; }
    public int SizeY { get; set; }
    public int SizeZ { get; set; }

    /// <summary>Whether this character is a member of the active party (distinct from IsTestPlayer, which is who you directly walk around as) - confirmed from Exult's own real model, where you can open ANY party member's own inventory/paperdoll (number keys 1-8), not just the one you're controlling.</summary>
    public bool IsInParty { get; set; }

    /// <summary>Real, persistent inventory - items carried, whether currently equipped in a slot or just sitting in the backpack. One list is the single source of truth for both; EquippedSlots below only records which slot (if any) each instance is assigned to.</summary>
    public List<InventoryItemInstance> Inventory { get; set; } = new();

    /// <summary>Maps a slot to the InventoryItemInstance.InstanceId currently equipped there - absent means that slot is empty. The item itself still lives in Inventory above; this is only the slot assignment.</summary>
    public Dictionary<EquipmentSlot, int> EquippedSlots { get; set; } = new();

    /// <summary>Real, confirmed new field - same exact real role as EquippedSlots, for this character's own custom slots instead of the fixed built-in list. Keyed by CustomEquipmentSlot.Id (never by its display Name, which can be renamed freely without orphaning whatever's equipped here).</summary>
    public Dictionary<int, int> CustomEquippedSlots { get; set; } = new();

    private int _nextInventoryInstanceId = 1;
    public int GetNextInventoryInstanceId() => _nextInventoryInstanceId++;
    public void SyncNextInventoryInstanceIdAfterLoad()
    {
        _nextInventoryInstanceId = Inventory.Count == 0 ? 1 : Inventory.Max(i => i.InstanceId) + 1;
    }

    /// <summary>Real, per-character paperdoll setup - body sprite plus each slot's own authored screen position, confirmed from Exult's own paperdoll.vga/paperdoll_info.txt model.</summary>
    public PaperdollDefinition Paperdoll { get; set; } = new();
}

/// <summary>Every PC defined in the project. Per spec, stored in .msrp, not a separate file.</summary>
public class CharacterDatabase
{
    public List<CharacterDefinition> Characters { get; set; } = new();
    private int _nextId = 1;

    public int GetNextId() => _nextId++;

    public void SyncNextIdAfterLoad()
    {
        _nextId = Characters.Count == 0 ? 1 : Characters.Max(c => c.Id) + 1;
    }
}
