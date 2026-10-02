using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Factions while the game runs: the game's own copy of the project's
/// factions (relationships change during play without touching the
/// project), who belongs where, and the two event commands that change
/// them. What a character does about another is
/// <see cref="BehaviorBetween"/> - the band the viewer's faction's view of
/// the other's faction falls in. The NPC AI (later stage) acts on it;
/// attacks, theft and kills will call <see cref="ReportFactionIncident"/>.
/// </summary>
public partial class GameTestWindow
{
    private FactionDatabase _factions = new();

    private void InitializeFactions(FactionDatabase factions)
    {
        _factions = factions.Clone();
        _execContext.OnChangeFactionRelationship = ChangeFactionRelationship;
        _execContext.OnChangeFaction = ChangeCharacterFaction;
    }

    /// <summary>What <paramref name="viewer"/> does about <paramref name="target"/>: Attack, Ignore, Ally or Flee.</summary>
    private FactionBehavior BehaviorBetween(CharacterDefinition viewer, CharacterDefinition target) =>
        _factions.BehaviorToward(viewer.FactionId, target.FactionId);

    /// <summary>A member of <paramref name="victim"/>'s faction was attacked, robbed or killed by <paramref name="offender"/>: that faction thinks less of the offender's.</summary>
    private void ReportFactionIncident(CharacterDefinition victim, CharacterDefinition offender, FactionIncident incident) =>
        _factions.ApplyIncident(victim.FactionId, offender.FactionId, incident);

    private void ChangeFactionRelationship(int viewerId, int targetId, string operation, int amount, bool bothWays)
    {
        void Apply(int from, int to)
        {
            int current = _factions.GetRelation(from, to);
            int next = operation switch { "Add" => current + amount, "Subtract" => current - amount, _ => amount };
            _factions.SetRelation(from, to, next);
        }
        Apply(viewerId, targetId);
        if (bothWays && viewerId != targetId) Apply(targetId, viewerId);
    }

    private bool ChangeCharacterFaction(string who, int factionId)
    {
        if (_characters is null) return false;
        var character = who == ChangeFactionCommandWindow.MainPlayer
            ? _player
            : int.TryParse(who, out int id) ? _characters.Characters.FirstOrDefault(c => c.Id == id) : null;
        if (character is null) return false;
        character.FactionId = _factions.Find(factionId) is null ? -1 : factionId;
        return true;
    }

    /// <summary>For identifying a character: its faction and how it feels about the player right now.</summary>
    private string DescribeFactionStanding(CharacterDefinition character)
    {
        string factionName = _factions.Find(character.FactionId)?.Name ?? "no faction";
        if (_player is null) return factionName;
        int value = _factions.GetRelation(character.FactionId, _player.FactionId);
        var band = _factions.BandFor(value);
        return $"{factionName} - {band?.Name ?? "?"} toward you ({value}%)";
    }
}
