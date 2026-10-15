using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Zones in play (Map Editor > Mode > Zones): a schedule line or a When...
/// rule that goes to a zone sends the character to the middle of it (its
/// activity then finds its chair or bed there, as anywhere); one that goes
/// to "the nearest Pub" picks the zone of that purpose nearest to where the
/// character is when it sets off. The When... rules (Character Editor >
/// Schedule) come before the timetable: checked every 2 seconds, the first
/// that holds takes the character away from what it's doing; when it no
/// longer holds, the character goes back to its timetable.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>The cell a line's zone sends it to, or null when it goes to a cell (or its zone is gone).</summary>
    private (int X, int Y)? ZoneSpotOf(MapCharacter mapCharacter, ScheduleEntry entry)
    {
        MapZone? zone = entry.ZoneId >= 0 ? _map.Zones.FirstOrDefault(z => z.Id == entry.ZoneId)
            : entry.ZonePurpose.Length > 0 ? Zones.Nearest(_map.Zones, entry.ZonePurpose, mapCharacter.Placement.X, mapCharacter.Placement.Y)
            : null;
        if (zone is null) return null;
        var (x, y) = zone.Center;
        return (Math.Clamp(x, 0, _map.Width - 1), Math.Clamp(y, 0, _map.Height - 1));
    }

    /// <summary>A hungry character's inn: its cell, else the nearest zone of its inn purpose ("Pub"), else none.</summary>
    private (int X, int Y)? InnSpotOf(MapCharacter mapCharacter)
    {
        var s = mapCharacter.Character.Survival;
        if (s.InnX >= 0 && InsideMap(s.InnX, s.InnY)) return (s.InnX, s.InnY);
        if (string.IsNullOrWhiteSpace(s.InnZonePurpose)) return null;
        return Zones.Nearest(_map.Zones, s.InnZonePurpose, mapCharacter.Placement.X, mapCharacter.Placement.Y)?.Center;
    }

    /// <summary>Every frame for each character: its When... rules, checked every 2 seconds.</summary>
    private void CheckScheduleRules(MapCharacter mapCharacter, double elapsedMs)
    {
        var npc = mapCharacter.Npc;
        var me = mapCharacter.Character;
        if (me.ScheduleRules.Count == 0 || IsPartyOrPlayer(me)) return;
        if ((npc.RuleCheckMs -= elapsedMs) > 0) return;
        npc.RuleCheckMs = 2000;
        if (npc.InCombat || npc.Fleeing || npc.Hounding) return; // a fight comes first

        if (npc.Rule is { } active)
        {
            if (RuleHolds(me, active, starting: false)) return;
            // Done (full, healed, the variable changed): back to the timetable.
            npc.Rule = null;
            npc.Entry = null;
            npc.Activity = null;
            StandUp(me);
            StopWalking(mapCharacter);
            SetCharacterPose(mapCharacter, "Idle", mapCharacter.Facing);
            UpdateSchedule(mapCharacter, _clock.Period, 0);
            return;
        }
        if (npc.Meal != MealStep.None) return; // already off to eat its own way (Health tab)
        if (me.ScheduleRules.FirstOrDefault(r => RuleHolds(me, r, starting: true)) is not { } rule) return;
        npc.Rule = rule;
        SetScheduleAndLoc(mapCharacter, rule.Do, SpotOf(mapCharacter, rule.Do), 0);
    }

    /// <summary>Whether a rule holds - to start (hungry, injured) or to keep going (until full, until healed).</summary>
    private bool RuleHolds(CharacterDefinition me, ScheduleRule rule, bool starting)
    {
        switch (rule.Condition)
        {
            case ScheduleRuleCondition.Hungry:
            {
                var s = me.Survival;
                if (!s.CanGetHungry) return false;
                int food = VitalsOf(me).Food;
                return starting ? food <= s.HungryAtOrBelow : food < FoodFullLevel;
            }
            case ScheduleRuleCondition.Injured:
            {
                int most = MaxHp(me);
                if (most <= 0 || GetHp(me) is not int hp) return false;
                return starting ? hp * 100 <= rule.InjuredAtOrBelowPercent * most : hp * 100 < rule.HealedAtPercent * most;
            }
            default:
                return !string.IsNullOrWhiteSpace(rule.VariableName)
                    && ScheduleRule.Compare(_execContext.GetVariable(rule.VariableName), rule.Comparison, rule.Value);
        }
    }
}
