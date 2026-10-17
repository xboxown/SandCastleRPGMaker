using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A character's "Always" event pages run on their own: when play starts, if
/// their conditions hold, and again each time their conditions go from not
/// holding to holding (checked twice a second while the game runs) - within
/// the page's Cycle (Once, so many times...). They still run on any of the
/// character's triggers too (By Hit, By Equip...), as Always does everywhere.
/// Only on a change, never over and over while nothing changes: a page with
/// no conditions runs once.
/// </summary>
public partial class GameTestWindow
{
    private readonly Dictionary<(CharacterDefinition Who, BookEventPage Page), bool> _alwaysPageMet = new();
    private double _alwaysCheckMs;

    private void UpdateAlwaysPages(double elapsedMs)
    {
        if ((_alwaysCheckMs -= elapsedMs) > 0) return;
        _alwaysCheckMs = 500;
        foreach (var who in AllLivingCharacters().ToList())
        {
            foreach (var page in who.EventPages.Where(p => p.Conditions.TriggerConditionBased == BookTriggerConditionBased.Always && p.Commands.Count > 0).ToList())
            {
                if (IsDead(who) || IsShowingMessage) return; // a box went up: the rest wait for the next check
                if (!_firedCounts.TryGetValue(who, out var counts)) _firedCounts[who] = counts = new Dictionary<BookEventPage, int>();
                var context = BuildConditionContext(BookTriggerConditionBased.Always, BookTriggerConditionBased.Always, -1, counts.GetValueOrDefault(page));
                context.GameConditionEvaluator = EvaluateGameCondition;
                context.UntilConditionStillMet = UntilStillGoing(page.Conditions);
                bool met = BookEventConditionEvaluator.Evaluate(page.Conditions, context, new List<string>());
                bool was = _alwaysPageMet.GetValueOrDefault((who, page));
                _alwaysPageMet[(who, page)] = met;
                if (!met || was) continue;
                RunAlwaysPage(who, page, counts);
            }
        }
    }

    /// <summary>The page's commands, run as the character's own event (Self = them).</summary>
    private void RunAlwaysPage(CharacterDefinition who, BookEventPage page, Dictionary<BookEventPage, int> counts)
    {
        counts[page] = counts.GetValueOrDefault(page) + 1;
        string eventName = EventOwnerName(who);
        Record($"event:{eventName}");
        Record($"eventpage:{eventName}:{who.EventPages.IndexOf(page) + 1}");
        var (previous, previousStep, previousOwner) = (_activator, _activatedByCharacterStep, _firingOwner);
        _activator = MapCharacterOf(who); // Self = this character (null = the player)
        _activatedByCharacterStep = false;
        _firingOwner = who;
        _execContext.SelfBookId = -1;
        try { EventCommandInterpreter.Run(page.Commands, _execContext, new List<string>()); }
        finally { (_activator, _activatedByCharacterStep, _firingOwner) = (previous, previousStep, previousOwner); }
    }
}
