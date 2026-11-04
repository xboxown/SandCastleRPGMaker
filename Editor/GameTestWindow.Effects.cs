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

    /// <param name="by">Whoever cast it (a spell): a kill gives them the victim's experience; the spot effects happen where the target is.</param>
    private void ApplyEffects(IEnumerable<MagicEffect> effects, CharacterDefinition target, string cause, CharacterDefinition? by = null)
    {
        foreach (var effect in effects.ToList())
        {
            if (IsDead(target)) return;
            int amount = RollEffect(effect);
            if (IsSpotEffect(effect.Kind)) { ApplySpotEffect(effect, by ?? target, CellOf(target), 0, target); continue; } // a potion of teleport, a scroll of summoning (GameTestWindow.Spells.cs)
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
                    _lightCheckMs = 0;
                    break;
                case MagicEffectKind.Damage:
                    if (amount > 0 && WoundsIt(target, null, by is not null ? cause : null)) DamageAllowed(target, amount, cause, DamageType.Magic); // cause: the spell's name
                    if (by is not null && by != target && IsDead(target)) AwardKill(by, target); // a spell's kill (GameTestWindow.Levels.cs)
                    break;
                case MagicEffectKind.Charm or MagicEffectKind.Fear or MagicEffectKind.Dance or MagicEffectKind.RevealInvisible or MagicEffectKind.Clone:
                    ApplyCharacterSpell(effect, target, by); // GameTestWindow.Spells.cs
                    break;
                case MagicEffectKind.Custom:
                    RunCustomEffect(effect.CustomEffectId, target);
                    break;
                case MagicEffectKind.Translate:
                    _translateMs = Math.Max(_translateMs, effect.Seconds * 1000);
                    break;
                case MagicEffectKind.Levitate:
                    Levitate(target, effect.Seconds);
                    break;
                case MagicEffectKind.CommonEvent:
                    if (_systemSettings.CommonEvents.FirstOrDefault(c => c.Id == effect.CommonEventId) is { Commands.Count: > 0 } common)
                        RunCommandsOn(common.Commands, target);
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

    /// <summary>A custom effect's event commands, run with whoever it's used on as the Activator.</summary>
    private void RunCustomEffect(int customEffectId, CharacterDefinition target)
    {
        if (_objects.CustomEffects.FirstOrDefault(c => c.Id == customEffectId) is not { Commands.Count: > 0 } custom) return;
        RunCommandsOn(custom.Commands, target);
    }

    /// <summary>Commands run with someone as "Whoever set it off" (a custom effect, a Common Event spell).</summary>
    private void RunCommandsOn(List<EventCommand> commands, CharacterDefinition target)
    {
        var (previous, previousStep) = (_activator, _activatedByCharacterStep);
        _activator = target == _player ? null : MapCharacterOf(target);
        _activatedByCharacterStep = false;
        try
        {
            _execContext.SelfBookId = -1;
            EventCommandInterpreter.Run(commands, _execContext, new List<string>());
        }
        finally { (_activator, _activatedByCharacterStep) = (previous, previousStep); }
    }

    private void SetStat(CharacterDefinition character, BuiltInStatKind kind, int value)
    {
        if (_stats.Stats.FirstOrDefault(s => s.Kind == kind) is { } stat) character.StatValues[stat.Id] = value.ToString();
    }
}
