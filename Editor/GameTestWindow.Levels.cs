using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Experience and levels, for every character alike - the player, the party,
/// and monsters killing each other (all characters are the same prefab).
/// As in Exult (Actor::reduce_health, set_property(exp)): whoever lands the
/// killing blow gets experience - here the dead one's own EXP stat (a Giant
/// Rat with EXP 30 gives 30) - and reaching a level's experience
/// (System Settings > Levels) raises Level and gives that level's training
/// points, never past the game's Max Level or the character's Level Lock.
/// </summary>
public partial class GameTestWindow
{
    private LevelSettings Levels => _systemSettings.Levels;

    /// <summary>A killing blow: the killer gets the dead one's experience.</summary>
    private void AwardKill(CharacterDefinition killer, CharacterDefinition victim)
    {
        if (ReferenceEquals(killer, victim)) return;
        int worth = Stat(victim, BuiltInStatKind.Exp, 0);
        if (worth > 0) GainExperience(killer, worth);
    }

    private void GainExperience(CharacterDefinition character, int amount)
    {
        if (_stats.Stats.FirstOrDefault(s => s.Kind == BuiltInStatKind.Exp) is null) return;
        SetStat(character, BuiltInStatKind.Exp, (int)Math.Min(int.MaxValue, (long)Stat(character, BuiltInStatKind.Exp, 0) + amount));
        if (character == _player) StatusText.Text = $"{StatusText.Text} +{amount} experience.".Trim();
        CheckLevel(character);
    }

    /// <summary>Its level from its experience: up only (a level set by hand above it stays), each new level's training points added; Next Exp kept up to date.</summary>
    private void CheckLevel(CharacterDefinition character)
    {
        Levels.Normalize();
        int cap = character.LevelLock > 0 ? Math.Min(character.LevelLock, Levels.MaxLevel) : Levels.MaxLevel;
        int old = Math.Max(1, Stat(character, BuiltInStatKind.Level, 1));
        int level = Math.Max(old, Levels.LevelFor(Stat(character, BuiltInStatKind.Exp, 0), cap));
        if (level > old)
        {
            int points = Levels.Steps.Where(s => s.Level > old && s.Level <= level).Sum(s => s.TrainingPoints);
            SetStat(character, BuiltInStatKind.Level, level);
            SetStat(character, BuiltInStatKind.TrainingPoints, Stat(character, BuiltInStatKind.TrainingPoints, 0) + points);
            if (character == _player || character.IsInParty)
                StatusText.Text = $"{character.Name} is now level {level}" + (points > 0 ? $" (+{points} training points)." : ".");
        }
        SetStat(character, BuiltInStatKind.NextExp, level < cap ? Levels.ExperienceFor(level + 1) : 0); // 0: at its highest level
    }
}
