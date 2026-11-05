namespace SandcastleRpgMaker.Engine;

/// <summary>One level of the curve: the experience (EXP total) it takes to reach it, and the training points given on reaching it.</summary>
public class LevelStep
{
    public int Level { get; set; }
    public int Experience { get; set; }
    public int TrainingPoints { get; set; }
}

/// <summary>
/// The level curve (System Settings > Levels), as RPG Maker's parameter
/// curves: a Max Level, and for every level the experience needed and the
/// training points it gives. The defaults are Exult's (Actor::get_level and
/// set_property(exp)): level 1 + log2(exp / 50) - level 2 at 100, doubling
/// each level - and 3 training points per level gained, up to level 8.
/// Every character levels by it - the player, the party and monsters alike -
/// each up to its own Level Lock (Character Editor).
/// </summary>
public class LevelSettings
{
    public const int MaxMaxLevel = 100;

    public int MaxLevel { get; set; } = 8;
    /// <summary>Levels 2..MaxLevel (level 1 is where everyone starts: 0 experience).</summary>
    public List<LevelStep> Steps { get; set; } = Generate(8, 100, 100, 3);

    /// <summary>The curve from a formula: level 2 at <paramref name="first"/>, each level after <paramref name="growthPercent"/>% more than the one before (100 = doubling, as Exult), <paramref name="points"/> training points each.</summary>
    public static List<LevelStep> Generate(int maxLevel, int first, double growthPercent, int points)
    {
        var steps = new List<LevelStep>();
        double exp = Math.Max(1, first);
        for (int level = 2; level <= Math.Clamp(maxLevel, 1, MaxMaxLevel); level++)
        {
            steps.Add(new LevelStep { Level = level, Experience = (int)Math.Min(int.MaxValue, Math.Round(exp)), TrainingPoints = Math.Max(0, points) });
            exp = Math.Min(int.MaxValue, exp * (1 + growthPercent / 100.0));
        }
        return steps;
    }

    /// <summary>A copy of its own (a character's own curve, starting from this one).</summary>
    public LevelSettings Clone() => new() { MaxLevel = MaxLevel, Steps = Steps.Select(s => new LevelStep { Level = s.Level, Experience = s.Experience, TrainingPoints = s.TrainingPoints }).ToList() };

    /// <summary>Exactly one step for each level 2..MaxLevel: missing ones continue the curve, extra ones are dropped.</summary>
    public void Normalize()
    {
        MaxLevel = Math.Clamp(MaxLevel, 1, MaxMaxLevel);
        var byLevel = Steps.GroupBy(s => s.Level).ToDictionary(g => g.Key, g => g.First());
        var steps = new List<LevelStep>();
        for (int level = 2; level <= MaxLevel; level++)
        {
            if (!byLevel.TryGetValue(level, out var step))
            {
                var before = steps.LastOrDefault();
                step = new LevelStep { Level = level, Experience = before is null ? 100 : (int)Math.Min(int.MaxValue, before.Experience * 2L), TrainingPoints = before?.TrainingPoints ?? 3 };
            }
            steps.Add(step);
        }
        Steps = steps;
    }

    /// <summary>The experience needed for a level (level 1 and below: 0; past the curve: never).</summary>
    public int ExperienceFor(int level) =>
        level <= 1 ? 0 : Steps.FirstOrDefault(s => s.Level == level)?.Experience ?? int.MaxValue;

    /// <summary>The level this much experience reaches, from level 1 up while each next level's experience is met, never past the cap.</summary>
    public int LevelFor(int experience, int cap)
    {
        int top = Math.Min(MaxLevel, cap <= 0 ? MaxLevel : cap), level = 1;
        while (level < top && experience >= ExperienceFor(level + 1)) level++;
        return level;
    }
}
