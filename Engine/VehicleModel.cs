namespace SandcastleRpgMaker.Engine;

/// <summary>
/// A vehicle (Map Editor > Vehicles): a house that moves - Ultima VII's ships,
/// carts and flying carpets (Exult's Barge_object). Built in the House Editor
/// facing south (toward you), from objects: hull, deck, wheels, seats, sails,
/// a helm. In play it is real pieces with a footprint; everything standing on
/// it moves with it, and it turns in quarter turns, every piece swinging round
/// its middle and showing its look for the new direction (Object Editor >
/// Vehicle part > Turned looks).
/// </summary>
public class VehicleSettings
{
    /// <summary>Land rolls over walkable ground, Sea sails only on Water tiles, Air flies.</summary>
    public MovementKind Movement { get; set; } = MovementKind.Land;
    /// <summary>How fast it goes, in map cells a second.</summary>
    public double CellsPerSecond { get; set; } = 3;
    /// <summary>A deed (an object) someone in the party must carry to drive it (Ultima VII's ship deeds); -1 = none needed.</summary>
    public int DeedObjectId { get; set; } = -1;
    public string NeedDeedText { get; set; } = "You need the deed to use this.";
    /// <summary>Sounds: as it starts off, while it moves (looping), as it stops.</summary>
    public int StartSoundResourceId { get; set; } = -1;
    public int MovingSoundResourceId { get; set; } = -1;
    public int StopSoundResourceId { get; set; } = -1;
}

/// <summary>What a piece of a vehicle is for (Object Editor > Vehicle part).</summary>
public enum VehiclePartRole
{
    None,
    /// <summary>A chair: sitting in it takes the controls; getting up gives them up.</summary>
    DriverSeat,
    /// <summary>A helm, a lever, a tassel: double-click to take the controls or give them up.</summary>
    Controls,
    /// <summary>A wheel or a draft animal: shows its Moving look while the vehicle rolls (Exult's barge_wheel / barge_draftanimal).</summary>
    Wheel
}

/// <summary>A vehicle in play (built from one plotted on the map): its footprint in 8px units, the height it rides at, and the way it faces.</summary>
public class VehicleState
{
    public int Id { get; set; }
    /// <summary>The vehicle (Vehicles tab) it was built from.</summary>
    public int TemplateId { get; set; }
    public int Left { get; set; }
    public int Top { get; set; }
    public int Width { get; set; }
    public int Depth { get; set; }
    /// <summary>The lift its floor is at (0 on the ground).</summary>
    public int Lift { get; set; }
    /// <summary>North, East, South or West - built facing South.</summary>
    public SpriteDirection Direction { get; set; } = SpriteDirection.South;
}
