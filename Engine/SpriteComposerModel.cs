using System.Text.Json;
using System.Text.Json.Serialization;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Sprite generator A (Tools > Sprite Composer): a character drawn from a skeleton - bones, each wearing a part (a shape
/// in a colour of the recipe, or a picture cut from a reference) - posed frame by frame in each direction, so a whole
/// set of sprites (stand, walk, attack, sit, sleep, dead in four directions) comes out of one recipe. It only draws what
/// its bones and parts are: nothing is invented. Libraries (Ultima 6 / 7 / 8, each in versions) hold the skeleton, the
/// poses and the parts; a new version never changes sprites already made (they are pictures by then).
/// </summary>
public static class Composer
{
    public static readonly string[] Directions = { "South", "North", "West", "East" };
    /// <summary>Where the colours of the shapes come from.</summary>
    public static readonly string[] Regions = { "Skin", "Hair", "Shirt", "Pants", "Boots", "Cloak", "Weapon", "Belt" };
    public static readonly string[] Shapes = { "Capsule", "Ellipse", "Box", "Head", "Blade", "Axe", "None" };
    /// <summary>The figure's units: the reference figure is this tall (feet at 0, head at about -64).</summary>
    public const double ReferenceHeight = 64;
}

/// <summary>One bone in one direction: where it hangs from, which way it points, how long and wide its shape is.</summary>
public class ComposerBone
{
    public string Name { get; set; } = "Bone";
    /// <summary>The bone it hangs from; "" = the root (the hips' point).</summary>
    public string Parent { get; set; } = "";
    /// <summary>On the parent's start instead of its end (legs from the hips, not from the top of the body).</summary>
    public bool AtStart { get; set; }
    /// <summary>Moved from that point, in figure units on the screen at rest (X right +, Y down +); it turns as the parent turns.</summary>
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    /// <summary>Degrees, added to the parent's: 0 = straight down, 90 = right, 180 = up.</summary>
    public double Angle { get; set; }
    public double Length { get; set; } = 10;
    public double Width { get; set; } = 4;
    /// <summary>Drawn later = in front. Each direction has its own order (the far arm behind the body).</summary>
    public int Layer { get; set; }
    public string Region { get; set; } = "Skin";
    public string Shape { get; set; } = "Capsule";
    /// <summary>The far side (an arm or leg behind the body seen from the side): drawn a little darker.</summary>
    public bool Far { get; set; }
    /// <summary>Only drawn when the recipe has it (Cloak, Hair, Weapon, Belt); "" = always.</summary>
    public string NeedsOption { get; set; } = "";

    public ComposerBone Clone() => (ComposerBone)MemberwiseClone();
}

/// <summary>One frame of a pose: bones turned and stretched, the whole body lifted or laid down.</summary>
public class ComposerPoseFrame
{
    /// <summary>Per direction ("South", "North", "West"; East = West mirrored unless it has its own): bone name -> degrees added.</summary>
    public Dictionary<string, Dictionary<string, double>> Turn { get; set; } = new();
    /// <summary>Per direction: bone name -> length times this (a knee lifted toward you looks shorter).</summary>
    public Dictionary<string, Dictionary<string, double>> Stretch { get; set; } = new();
    /// <summary>The body down by this many units (sitting); the whole figure turned by this many degrees (lying: 90).</summary>
    public double Lower { get; set; }
    public double BodyAngle { get; set; }
    public int DurationMs { get; set; } = 150;

    public double TurnOf(string direction, string bone) => Lookup(Turn, direction, bone, 0);
    public double StretchOf(string direction, string bone) => Lookup(Stretch, direction, bone, 1);

    private static double Lookup(Dictionary<string, Dictionary<string, double>> table, string direction, string bone, double none)
    {
        if (table.TryGetValue(direction, out var own) && own.TryGetValue(bone, out double v)) return v;
        if (direction == "East" && !table.ContainsKey("East") && table.TryGetValue("West", out var west) && west.TryGetValue(bone, out double w)) return w;
        if (direction == "North" && !table.ContainsKey("North") && table.TryGetValue("South", out var south) && south.TryGetValue(bone, out double s)) return s;
        return none;
    }
}

public class ComposerPose
{
    public string Name { get; set; } = "Stand";
    public bool Loop { get; set; } = true;
    public List<ComposerPoseFrame> Frames { get; set; } = new();
}

/// <summary>A picture worn by a bone in a direction (cut from a reference): drawn instead of the bone's shape.</summary>
public class ComposerPart
{
    public string Name { get; set; } = "Part";
    public string Bone { get; set; } = "";
    /// <summary>"" = every direction; else only this one (East falls back to West, mirrored).</summary>
    public string Direction { get; set; } = "";
    /// <summary>The picture: PNG bytes, base64.</summary>
    public string Png { get; set; } = "";
    /// <summary>The joint in the picture's pixels: it sits on the bone's start; the picture's "down" lies along the bone.</summary>
    public double PivotX { get; set; }
    public double PivotY { get; set; }
    /// <summary>Picture pixels to figure units (the reference figure's height / its height in that picture).</summary>
    public double Scale { get; set; } = 1;
    /// <summary>Recoloured with the bone's region colour (its light and dark kept) - one part, any colour.</summary>
    public bool Tint { get; set; }
    /// <summary>The recipe's choice this part is one of ("" = always worn; e.g. "Helmet": worn when the recipe picks it).</summary>
    public string Option { get; set; } = "";
}

/// <summary>A library: the skeleton in each direction, the poses, the parts. Saved as its own file, one per version.</summary>
public class ComposerLibrary
{
    public string Style { get; set; } = "Ultima 7";
    public int Version { get; set; } = 1;
    public string Notes { get; set; } = "";
    /// <summary>Direction -> bones (the same names in each direction).</summary>
    public Dictionary<string, List<ComposerBone>> Skeleton { get; set; } = new();
    /// <summary>Where the root (the hips) is above the feet, in units.</summary>
    public double RootHeight { get; set; } = 27;
    public List<ComposerPose> Poses { get; set; } = new();
    public List<ComposerPart> Parts { get; set; } = new();

    [JsonIgnore] public string FileName => $"{Style.Replace(' ', '-')}-v{Version}.json";

    public List<ComposerBone> BonesFor(string direction) =>
        Skeleton.TryGetValue(direction, out var own) ? own : Skeleton.TryGetValue("West", out var west) ? west : new List<ComposerBone>();

    public ComposerLibrary DeepCopy() => JsonSerializer.Deserialize<ComposerLibrary>(JsonSerializer.Serialize(this))!;

    // ---------- the built-in human ----------

    /// <summary>A human: body, head and hair, two arms (upper arm and forearm), two legs (thigh and shin with the boot),
    /// a cloak, a belt and a weapon in the right hand - front (South), back (North) and side (West; East mirrored).</summary>
    public static ComposerLibrary BuiltIn(string style)
    {
        var lib = new ComposerLibrary { Style = style, Version = 1, Notes = "Built-in human (Sandcastle)." };
        lib.Skeleton["South"] = Human(front: true, back: false);
        lib.Skeleton["North"] = Human(front: false, back: true);
        lib.Skeleton["West"] = Side();
        lib.Poses = BuiltInPoses();
        return lib;
    }

    private static ComposerBone B(string name, string parent, double angle, double length, double width, int layer, string region, string shape = "Capsule",
        bool atStart = false, double ox = 0, double oy = 0, bool far = false, string needs = "") =>
        new() { Name = name, Parent = parent, Angle = angle, Length = length, Width = width, Layer = layer, Region = region, Shape = shape, AtStart = atStart, OffsetX = ox, OffsetY = oy, Far = far, NeedsOption = needs };

    private static List<ComposerBone> Human(bool front, bool back)
    {
        // Seen from the front the character's right arm is on the screen's left; from the back, on the right.
        double r = front ? -1 : 1;
        int cloakLayer = back ? 60 : 0;
        return new List<ComposerBone>
        {
            B("Cloak", "Body", 180, 27, 17, cloakLayer, "Cloak", "Box", oy: 1, needs: "Cloak"),
            B("LegR", "", 0, 14, 6, 10, "Pants", atStart: true, ox: 3.5 * r),
            B("ShinR", "LegR", 0, 13, 5.5, 11, "Boots"),
            B("LegL", "", 0, 14, 6, 10, "Pants", atStart: true, ox: -3.5 * r),
            B("ShinL", "LegL", 0, 13, 5.5, 11, "Boots"),
            B("Body", "", 180, 19, 14, 20, "Shirt", "Box"),
            B("Belt", "Body", 0, 2.5, 14.5, 21, "Belt", "Box", atStart: true, oy: -1, needs: "Belt"),
            B("Head", "Body", 0, 11, 10, 30, "Skin", front ? "Head" : "Ellipse", oy: 1),
            B("Hair", "Head", 0, back ? 12 : 7, 11, 31, "Hair", "Ellipse", oy: back ? 11 : 6, needs: "Hair"),
            B("ArmR", "Body", 180, 9.5, 4.5, 25, "Shirt", ox: 8.5 * r),
            B("ForearmR", "ArmR", 0, 8.5, 4, 26, "Skin"),
            B("Weapon", "ForearmR", 180, 17, 3, back ? 5 : 27, "Weapon", "Blade", needs: "Weapon"),
            B("ArmL", "Body", 180, 9.5, 4.5, 25, "Shirt", ox: -8.5 * r),
            B("ForearmL", "ArmL", 0, 8.5, 4, 26, "Skin"),
        };
    }

    private static List<ComposerBone> Side() => new()
    {
        // Facing west (left): the far (right) arm and leg behind the body, darker.
        B("ArmR", "Body", 180, 9.5, 4.5, 2, "Shirt", ox: 1, far: true),
        B("ForearmR", "ArmR", 0, 8.5, 4, 3, "Skin", far: true),
        B("Weapon", "ForearmR", 180, 17, 3, 1, "Weapon", "Blade", needs: "Weapon"),
        B("LegR", "", 0, 14, 6, 5, "Pants", atStart: true, ox: 1, far: true),
        B("ShinR", "LegR", 0, 13, 5.5, 6, "Boots", far: true),
        B("Cloak", "Body", 180, 27, 9, 8, "Cloak", "Box", ox: 4, oy: 1, needs: "Cloak"),
        B("LegL", "", 0, 14, 6, 10, "Pants", atStart: true, ox: -1),
        B("ShinL", "LegL", 0, 13, 5.5, 11, "Boots"),
        B("Body", "", 180, 19, 9, 20, "Shirt", "Box"),
        B("Belt", "Body", 0, 2.5, 9.5, 21, "Belt", "Box", atStart: true, oy: -1, needs: "Belt"),
        B("Head", "Body", 0, 11, 9, 30, "Skin", "Head", oy: 1),
        B("Hair", "Head", 0, 7, 10, 31, "Hair", "Ellipse", oy: 6, ox: 1, needs: "Hair"),
        B("ArmL", "Body", 180, 9.5, 4.5, 40, "Shirt", ox: -1),
        B("ForearmL", "ArmL", 0, 8.5, 4, 41, "Skin"),
    };

    private static ComposerPoseFrame F(int ms, (string Dir, string Bone, double Deg)[] turns, (string Dir, string Bone, double Times)[]? stretch = null, double lower = 0, double bodyAngle = 0)
    {
        var f = new ComposerPoseFrame { DurationMs = ms, Lower = lower, BodyAngle = bodyAngle };
        foreach (var (d, b, deg) in turns) { if (!f.Turn.TryGetValue(d, out var t)) f.Turn[d] = t = new(); t[b] = deg; }
        foreach (var (d, b, x) in stretch ?? Array.Empty<(string, string, double)>()) { if (!f.Stretch.TryGetValue(d, out var s)) f.Stretch[d] = s = new(); s[b] = x; }
        return f;
    }

    public static List<ComposerPose> BuiltInPoses()
    {
        const string S = "South", W = "West";
        return new List<ComposerPose>
        {
            new() { Name = "Stand", Frames = { F(400, Array.Empty<(string, string, double)>()) } },
            new()
            {
                Name = "Walk", Frames =
                {
                    F(150, new[] { (W, "LegL", 28.0), (W, "LegR", -24.0), (W, "ShinR", 20.0), (W, "ArmL", -22.0), (W, "ArmR", 22.0), (S, "ArmL", 8.0), (S, "ArmR", 8.0) },
                        new[] { (S, "LegL", 0.85), (S, "ShinL", 0.8) }),
                    F(150, new[] { (W, "LegL", 8.0), (W, "LegR", -6.0), (W, "ShinR", 30.0) }),
                    F(150, new[] { (W, "LegR", 28.0), (W, "LegL", -24.0), (W, "ShinL", 20.0), (W, "ArmR", -22.0), (W, "ArmL", 22.0), (S, "ArmL", -8.0), (S, "ArmR", -8.0) },
                        new[] { (S, "LegR", 0.85), (S, "ShinR", 0.8) }),
                    F(150, new[] { (W, "LegR", 8.0), (W, "LegL", -6.0), (W, "ShinL", 30.0) }),
                }
            },
            new()
            {
                Name = "Attack", Loop = false, Frames =
                {
                    F(150, new[] { (W, "ArmR", 150.0), (W, "ForearmR", 30.0), (S, "ArmR", 140.0), (S, "ForearmR", 20.0) }),
                    F(100, new[] { (W, "ArmR", 85.0), (W, "ForearmR", 0.0), (S, "ArmR", 70.0) }),
                    F(200, new[] { (W, "ArmR", 25.0), (W, "ForearmR", -20.0), (S, "ArmR", 15.0), (S, "ForearmR", -10.0) }),
                }
            },
            new()
            {
                Name = "Sit", Frames =
                {
                    F(400, new[] { (W, "LegL", -85.0), (W, "ShinL", 85.0), (W, "LegR", -85.0), (W, "ShinR", 85.0) },
                        new[] { (S, "LegL", 0.35), (S, "LegR", 0.35) }, lower: 9)
                }
            },
            new() { Name = "Sleep", Frames = { F(400, Array.Empty<(string, string, double)>(), bodyAngle: 90) } },
            new()
            {
                Name = "Dead", Frames =
                {
                    F(400, new[] { (S, "ArmL", -50.0), (S, "ArmR", 50.0), (W, "ArmL", -40.0), (W, "LegL", 15.0) }, bodyAngle: -90)
                }
            },
        };
    }
}

/// <summary>A character to compose: its knobs and colours (and the words they came from).</summary>
public class ComposerRecipe
{
    public double Height { get; set; } = 1;     // times the reference height
    public double Build { get; set; } = 1;      // widths: thin 0.8 .. stout 1.35
    public double HeadSize { get; set; } = 1;
    public double ArmLength { get; set; } = 1;
    public double LegLength { get; set; } = 1;
    /// <summary>Region -> colour (#RRGGBB).</summary>
    public Dictionary<string, string> Colours { get; set; } = new()
    {
        ["Skin"] = "#E0AC80", ["Hair"] = "#5A3A1E", ["Shirt"] = "#3A5A9A", ["Pants"] = "#6A5034",
        ["Boots"] = "#3A2A1A", ["Cloak"] = "#8A2020", ["Weapon"] = "#B8B8C0", ["Belt"] = "#4A3018"
    };
    /// <summary>What it has on: Hair, Cloak, Weapon, Belt (and any part's Option).</summary>
    public HashSet<string> Options { get; set; } = new() { "Hair", "Belt" };
    /// <summary>The weapon's shape: Blade (sword), Axe, Capsule (a staff or a club).</summary>
    public string WeaponShape { get; set; } = "Blade";
}

/// <summary>Words to knobs: "tall, stout, green skin, red cloak, axe, bald" - only what the composer can draw; the rest is told back.</summary>
public static class ComposerWords
{
    public static readonly Dictionary<string, string> ColourNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["red"] = "#A82020", ["dark red"] = "#601010", ["crimson"] = "#8A1028", ["orange"] = "#D07020", ["yellow"] = "#D8C030", ["gold"] = "#C8A030", ["golden"] = "#C8A030",
        ["green"] = "#3A8A30", ["dark green"] = "#1E4A1A", ["olive"] = "#6A7030", ["blue"] = "#2A4AA8", ["dark blue"] = "#1A2A60", ["light blue"] = "#6A9AD8",
        ["purple"] = "#6A2A8A", ["violet"] = "#7A4AB0", ["pink"] = "#D88AA0", ["brown"] = "#6A4428", ["dark brown"] = "#3A2414", ["tan"] = "#C8A070",
        ["black"] = "#202020", ["grey"] = "#808080", ["gray"] = "#808080", ["white"] = "#E8E8E8", ["silver"] = "#C0C0C8", ["blonde"] = "#E0C870",
        ["pale"] = "#F0D0B8", ["dark"] = "#7A4A2A", ["tanned"] = "#C08A5A", ["teal"] = "#2A8080", ["grey-green"] = "#6A806A", ["bone"] = "#E0D8C0"
    };
    private static readonly Dictionary<string, string> RegionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["skin"] = "Skin", ["face"] = "Skin", ["hair"] = "Hair", ["beard"] = "Hair", ["shirt"] = "Shirt", ["tunic"] = "Shirt", ["robe"] = "Shirt", ["armor"] = "Shirt",
        ["armour"] = "Shirt", ["dress"] = "Shirt", ["jerkin"] = "Shirt", ["top"] = "Shirt", ["pants"] = "Pants", ["trousers"] = "Pants", ["leggings"] = "Pants", ["legs"] = "Pants",
        ["boots"] = "Boots", ["shoes"] = "Boots", ["cloak"] = "Cloak", ["cape"] = "Cloak", ["mantle"] = "Cloak", ["belt"] = "Belt", ["sword"] = "Weapon", ["axe"] = "Weapon",
        ["staff"] = "Weapon", ["club"] = "Weapon", ["blade"] = "Weapon", ["weapon"] = "Weapon"
    };

    /// <summary>Changes the recipe by the words; returns the words it couldn't use (it says so instead of guessing).</summary>
    public static List<string> Apply(ComposerRecipe recipe, string text)
    {
        var unknown = new List<string>();
        foreach (var raw in text.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string phrase = raw.Trim().ToLowerInvariant();
            if (phrase.Length == 0) continue;
            if (Knob(recipe, phrase)) continue;
            // "<colour> <region>" or "<region> <colour>"
            var words = phrase.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w is not ("a" or "an" or "the" or "with")).ToList();
            string? region = words.Select(w => RegionWords.TryGetValue(w, out var r) ? r : null).FirstOrDefault(r => r is not null);
            string? regionWord = words.FirstOrDefault(w => RegionWords.ContainsKey(w));
            string colourText = string.Join(" ", words.Where(w => w != regionWord));
            string? colour = colourText.Length == 0 ? null : ColourNames.TryGetValue(colourText, out var c) ? c : colourText.StartsWith('#') && colourText.Length == 7 ? colourText.ToUpperInvariant() : null;
            if (region is not null && (colour is not null || colourText.Length == 0))
            {
                if (colour is not null) recipe.Colours[region] = colour;
                if (region is "Cloak" or "Hair" or "Belt" or "Weapon") recipe.Options.Add(region);
                if (regionWord is "sword" or "blade") recipe.WeaponShape = "Blade";
                else if (regionWord is "axe") recipe.WeaponShape = "Axe";
                else if (regionWord is "staff" or "club") recipe.WeaponShape = "Capsule";
                continue;
            }
            unknown.Add(raw.Trim());
        }
        return unknown;
    }

    private static bool Knob(ComposerRecipe r, string p)
    {
        switch (p)
        {
            case "tall": r.Height = 1.12; return true;
            case "very tall": case "giant": r.Height = 1.3; return true;
            case "short": r.Height = 0.88; return true;
            case "very short": case "dwarf": case "child": r.Height = 0.72; return true;
            case "thin": case "slim": case "skinny": r.Build = 0.8; return true;
            case "stout": case "fat": case "heavy": case "broad": r.Build = 1.35; return true;
            case "muscular": case "strong": r.Build = 1.2; return true;
            case "big head": r.HeadSize = 1.25; return true;
            case "small head": r.HeadSize = 0.85; return true;
            case "long arms": r.ArmLength = 1.2; return true;
            case "long legs": r.LegLength = 1.15; return true;
            case "short legs": r.LegLength = 0.8; return true;
            case "bald": case "no hair": r.Options.Remove("Hair"); return true;
            case "no cloak": r.Options.Remove("Cloak"); return true;
            case "no belt": r.Options.Remove("Belt"); return true;
            case "unarmed": case "no weapon": r.Options.Remove("Weapon"); return true;
            default: return false;
        }
    }
}
