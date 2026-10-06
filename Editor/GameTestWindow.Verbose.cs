using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Characters speaking up (Character Editor > Verbose): one line, floating
/// over their head, from their own list for the situation. Only humans speak
/// (Exult's can_speak) - a rat never cries for help. Fleeing uses Exult's
/// rule: the first line three times in four, another otherwise; theft
/// warnings go in order; everything else picks at random.
/// </summary>
public partial class GameTestWindow
{
    private readonly Dictionary<CharacterDefinition, double> _uneasyCooldownMs = new();

    /// <summary>Says a line for the situation; <paramref name="index"/> picks a given line (theft warnings). False if nothing was said.</summary>
    private bool Bark(CharacterDefinition who, VerboseKind kind, int? index = null)
    {
        if (!who.IsHuman || IsDead(who) || IsUnconscious(who)) return false;
        var lines = who.Verbose.Get(kind);
        if (lines.Count == 0) return false;
        string line = index is int i ? lines[Math.Clamp(i, 0, lines.Count - 1)]
            : kind == VerboseKind.Fleeing && lines.Count > 1 && Rng.Next(4) > 0 ? lines[0]
            : lines[Rng.Next(lines.Count)];
        ShowFloatingText(who, line, 3000);
        return true;
    }

    /// <summary>Exult's say_hunger_message, each game hour: starving 3 in 4, very hungry (food 4 or less) 2 in 3, hungry 1 in 2.</summary>
    private void SayHungerMessage(CharacterDefinition who, int food)
    {
        var s = who.Survival;
        if (!s.CanGetHungry || food > s.HungryAtOrBelow) return;
        if (food <= 0) { if (Rng.Next(4) > 0) Bark(who, VerboseKind.Starving); }
        else if (food <= 4) { if (Rng.Next(3) > 0) Bark(who, VerboseKind.VeryHungry); }
        else if (Rng.Next(2) > 0) Bark(who, VerboseKind.Hungry);
    }

    /// <summary>Not feeling safe: an NPC watching a fight it's no part of, now and then (once in 30 seconds at most).</summary>
    private void MaybeFeelUneasy(MapCharacter watcher, double elapsedMs)
    {
        var me = watcher.Character;
        if (_uneasyCooldownMs.TryGetValue(me, out double left) && (left -= elapsedMs) > 0) { _uneasyCooldownMs[me] = left; return; }
        bool fightInSight = _mapCharacters.Any(other => other != watcher && other.Npc.InCombat && other.Npc.Target is { } target
            && target != me && other.Npc.Target != me && CanSee(me, other.Character));
        if (!fightInSight || Rng.Next(3) > 0) { _uneasyCooldownMs[me] = 1000; return; }
        Bark(me, VerboseKind.Uneasy);
        _uneasyCooldownMs[me] = 30000;
    }
}
