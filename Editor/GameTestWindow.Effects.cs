using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Effects - what potions, food, drinks, spells and useable objects do to
/// whoever they're used on - each mapped onto the mechanic Exult has for it:
///  - Heal / Restore mana: health (the Hits stat) up to Strength, mana up to
///    Magic (Exult's MAX_MANA is the Magic property).
///  - Poison, sleep, paralysis, curse and their cures: the Npc_*_timer
///    ailments (GameTestWindow.Vitals.cs), with Exult's durations.
///  - Protection: +3 to defence for 60-80 seconds (actors.cc
///    figure_hit_points: defval + 3 when protected; Npc_protection_timer).
///  - Invisibility: 60-80 seconds (Npc_invisibility_timer); enemies who
///    can't see invisible stop attacking (stop_attacking_invisible).
///  - Light: Exult's add_special_light - the night is lit as by many lights
///    for that many game minutes.
///  - Damage, and Nourish (food level, 0-31).
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Game minutes left of a light spell's (or potion's) glow (Exult's special_light).</summary>
    private double _specialLightMinutes;

    private void ApplyEffects(IEnumerable<MagicEffect> effects, CharacterDefinition target, string cause)
    {
        foreach (var effect in effects.ToList())
        {
            if (IsDead(target)) return;
            int amount = RollEffect(effect);
            switch (effect.Kind)
            {
                case MagicEffectKind.Heal:
                    if (GetHp(target) is int hp)
                    {
                        int max = MaxHp(target);
                        SetHp(target, max > 0 ? Math.Min(max, hp + amount) : hp + amount);
                        if (target == _player) StatusText.Text = $"You feel better ({GetHp(target)} HP).";
                    }
                    break;
                case MagicEffectKind.RestoreMana:
                {
                    int mana = Stat(target, BuiltInStatKind.Mana, 0);
                    int max = Stat(target, BuiltInStatKind.Magic, int.MaxValue);
                    SetStat(target, BuiltInStatKind.Mana, Math.Min(max, mana + amount));
                    break;
                }
                case MagicEffectKind.CurePoison: Cure(target, Ailment.Poisoned); break;
                case MagicEffectKind.Poison: Inflict(target, Ailment.Poisoned); break;
                case MagicEffectKind.Sleep: Inflict(target, Ailment.Asleep); break;
                case MagicEffectKind.Awaken: Cure(target, Ailment.Asleep); break;
                case MagicEffectKind.Paralyze: Inflict(target, Ailment.Paralyzed); break;
                case MagicEffectKind.CureParalysis: Cure(target, Ailment.Paralyzed); break;
                case MagicEffectKind.Curse: Inflict(target, Ailment.Cursed); break;
                case MagicEffectKind.RemoveCurse: Cure(target, Ailment.Cursed); break;
                case MagicEffectKind.Protection: Inflict(target, Ailment.Protected); break;
                case MagicEffectKind.Invisibility: Inflict(target, Ailment.Invisible); break;
                case MagicEffectKind.Light:
                    _specialLightMinutes = Math.Max(_specialLightMinutes, amount);
                    _lightLevel = -1; // looked at again at once
                    _lightCheckMs = 0;
                    break;
                case MagicEffectKind.Damage:
                    if (amount > 0) Damage(target, amount, cause);
                    break;
                case MagicEffectKind.Nourish:
                {
                    var vitals = VitalsOf(target);
                    vitals.Food = Math.Clamp(vitals.Food + amount, 0, SurvivalSettings.MaxFood);
                    break;
                }
            }
        }
    }

    private static int RollEffect(MagicEffect effect)
    {
        int min = Math.Min(effect.Min, effect.Max), max = Math.Max(effect.Min, effect.Max);
        return min + Rng.Next(max - min + 1);
    }

    /// <summary>Damage effects (a fireball scroll) on a placed object: struck for that much.</summary>
    private void ApplyEffectsToObject(IEnumerable<MagicEffect> effects, ObjectInstance instance, WorldObject obj)
    {
        foreach (var effect in effects.Where(e => e.Kind == MagicEffectKind.Damage).ToList())
            if (_map.Objects.Contains(instance)) StrikeObject(instance, obj, RollEffect(effect));
    }

    private void SetStat(CharacterDefinition character, BuiltInStatKind kind, int value)
    {
        if (_stats.Stats.FirstOrDefault(s => s.Kind == kind) is { } stat) character.StatValues[stat.Id] = value.ToString();
    }
}
