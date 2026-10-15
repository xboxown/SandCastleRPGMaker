namespace SandcastleRpgMaker.Engine;

/// <summary>
/// One step of a designed staircase (Staircase Designer). As in Exult, a
/// staircase is a set of separate step pieces, each at its own height, and
/// walkers climb from one onto the next by the walking rule (a lift at a
/// time). Coordinates are relative to the top-left corner of the cell the
/// staircase is placed on (its anchor cell, at its floor's lift).
/// </summary>
public class StairStep
{
    /// <summary>What it's called when clicked in play ("" = "Step 2", ...). The first step goes by the object's own name.</summary>
    public string Name { get; set; } = string.Empty;
    public SpriteLink Sprite { get; set; } = new();
    /// <summary>Where its picture's top-left corner is drawn, in pixels from the anchor cell's top-left.</summary>
    public int PictureX { get; set; }
    public int PictureY { get; set; }
    /// <summary>The part you stand on: its top-left corner in world units (8px) from the anchor cell's top-left, and its size in units.</summary>
    public int FootX { get; set; }
    public int FootY { get; set; }
    public int SizeX { get; set; } = 4;
    public int SizeY { get; set; } = 1;
    /// <summary>How many lifts (4px each) above the staircase's foot its top is: where you stand on it.</summary>
    public int TopLift { get; set; } = 1;

    public StairStep Clone() => new()
    {
        Name = Name, Sprite = new SpriteLink { SpriteGroup = Sprite.SpriteGroup, SpriteName = Sprite.SpriteName },
        PictureX = PictureX, PictureY = PictureY, FootX = FootX, FootY = FootY, SizeX = SizeX, SizeY = SizeY, TopLift = TopLift
    };
}

/// <summary>A staircase's layout for one facing (its pose) - chosen per placed copy in the Map Editor, as a chair's facing is.</summary>
public class StairPose
{
    public SpriteDirection Facing { get; set; } = SpriteDirection.North;
    public List<StairStep> Steps { get; set; } = new();
}

/// <summary>A staircase made in the Staircase Designer; a Stairs object links to it (Object Editor).</summary>
public class StairDesign
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Staircase";
    public List<StairPose> Poses { get; set; } = new();

    /// <summary>The layout for this facing: its own pose, else the first one there is.</summary>
    public StairPose? Resolve(SpriteDirection facing) =>
        Poses.FirstOrDefault(p => p.Facing == facing && p.Steps.Count > 0) ?? Poses.FirstOrDefault(p => p.Steps.Count > 0);

    /// <summary>The name a step goes by in play: the first step is the staircase itself.</summary>
    public static string StepName(StairStep step, int index, string objectName) =>
        index == 0 ? objectName : string.IsNullOrWhiteSpace(step.Name) ? $"Step {index + 1} of {objectName}" : step.Name;
}
