using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Spellbooks, ported from Exult (objs/spellbook.cc):
///  - can_do_spell: the caster needs mana of at least the spell's cost
///    (Exult: its circle), a Level of at least its circle, and one of each
///    of its reagents carried. Here a cursed caster can't cast either
///    (Ultima VII's curse).
///  - do_spell: the mana is spent and one of each reagent used up, then the
///    spell is cast - its words said over the caster (begin_casting), its
///    sound, and, if it needs one, a target clicked (Get_click).
///  - The book opens over the game, its spells by circle; those that can't
///    be cast now are greyed, saying why.
/// </summary>
public partial class GameTestWindow
{
    private void UseSpellbook(Used u)
    {
        if (FireUseEvent(u)) return;
        var caster = UserOf(u);
        if (caster is null) return;
        var spells = u.Obj.Spellbook.Spells.Where(s => KnowsSpell(u.Obj, u.Carried?.LearnedSpellsRuntime, s)).OrderBy(s => s.Circle).ThenBy(s => s.Name).ToList(); // only those this copy knows
        var panel = new StackPanel { Margin = new Thickness(14) };
        panel.Children.Add(new TextBlock { Text = u.Obj.Name, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = Brushes.Black, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock
        {
            Text = $"{caster.Name}: mana {Stat(caster, BuiltInStatKind.Mana, 0)} of {Stat(caster, BuiltInStatKind.Magic, 0)}, level {Stat(caster, BuiltInStatKind.Level, 0)}",
            Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 8)
        });
        if (spells.Count == 0) panel.Children.Add(new TextBlock { Text = "(no spells in this book)", Foreground = Brushes.DimGray, HorizontalAlignment = HorizontalAlignment.Center });
        foreach (var circle in spells.GroupBy(s => s.Circle))
        {
            panel.Children.Add(new TextBlock { Text = CircleTitle(circle.Key), FontWeight = FontWeights.Bold, Foreground = Brushes.SaddleBrown, Margin = new Thickness(0, 6, 0, 2) });
            foreach (var spell in circle)
            {
                string? why = WhyCannotCast(caster, spell);
                var button = new Button
                {
                    Content = $"{spell.Name}   ({spell.EffectiveManaCost} mana{ReagentList(spell)})",
                    HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 1, 0, 1),
                    IsEnabled = why is null, ToolTip = why ?? (spell.Words.Length > 0 ? $"\"{spell.Words}\"" : null)
                };
                var chosen = spell;
                button.Click += (_, _) => { CloseObjectOverlay(); CastSpell(caster, chosen); };
                panel.Children.Add(button);
                if (why is not null) panel.Children.Add(new TextBlock { Text = why, FontSize = 10, Foreground = Brushes.Firebrick, Margin = new Thickness(10, 0, 0, 2) });
            }
        }
        var close = new Button { Content = "Close", Padding = new Thickness(12, 2, 12, 2), Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
        close.Click += (_, _) => CloseObjectOverlay();
        panel.Children.Add(close);
        var book = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xE4, 0xC2)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x6B, 0x45, 0x1E)),
            BorderThickness = new Thickness(4), CornerRadius = new CornerRadius(4), MinWidth = 320, MaxHeight = Math.Max(300, ActualHeight - 120),
            Child = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }
        };
        if (PaperdollSoundIsGlobal) PlaySound(_systemSettings.Sounds.PaperdollOpenSoundId); // a spellbook is a paperdoll too
        ShowObjectOverlay(book, takesClicks: true, scaleLikeViewport: false);
    }

    private static string CircleTitle(int circle) => circle switch
    {
        0 => "Linear", 1 => "First Circle", 2 => "Second Circle", 3 => "Third Circle", 4 => "Fourth Circle",
        5 => "Fifth Circle", 6 => "Sixth Circle", 7 => "Seventh Circle", _ => "Eighth Circle"
    };

    private string ReagentList(SpellDefinition spell) => spell.ReagentObjectIds.Count == 0 ? ""
        : "; " + string.Join(", ", spell.ReagentObjectIds.Select(id => _objects.Objects.FirstOrDefault(o => o.Id == id)?.Name ?? "?"));

    /// <summary>can_do_spell - null if it can be cast now, else why not.</summary>
    private string? WhyCannotCast(CharacterDefinition caster, SpellDefinition spell)
    {
        if (IsIncapacitated(caster)) return "The caster can't act.";
        if (Has(caster, Ailment.Cursed)) return "The caster is cursed.";
        int mana = Stat(caster, BuiltInStatKind.Mana, 0);
        if (mana < spell.EffectiveManaCost) return $"Not enough mana ({mana} of {spell.EffectiveManaCost}).";
        int level = Stat(caster, BuiltInStatKind.Level, int.MaxValue);
        if (level < spell.Circle) return $"Needs level {spell.Circle} (has {level}).";
        var carried = CarriedItems(caster);
        foreach (int reagent in spell.ReagentObjectIds.Distinct())
            if (!carried.Any(i => i.Item.Category == ReferencedItemCategory.Object && i.Item.ItemId == reagent))
                return $"Missing {_objects.Objects.FirstOrDefault(o => o.Id == reagent)?.Name ?? "a reagent"}.";
        return null;
    }

    /// <summary>do_spell, then the spell's effects - on the caster, or a target clicked.</summary>
    /// <param name="castOn">Cast Spell (event command): its target given, no clicking.</param>
    private void CastSpell(CharacterDefinition caster, SpellDefinition spell, CharacterDefinition? castOn = null)
    {
        if (WhyCannotCast(caster, spell) is { } why) { StatusText.Text = why; return; }
        SetStat(caster, BuiltInStatKind.Mana, Stat(caster, BuiltInStatKind.Mana, 0) - spell.EffectiveManaCost);
        var carried = CarriedItems(caster);
        foreach (int reagent in spell.ReagentObjectIds.Distinct()) TakeFrom(carried, reagent, 1);
        RefreshAfterMove();
        if (spell.Words.Trim().Length > 0) ShowFloatingText(caster, spell.Words.Trim(), 2500); // begin_casting: the words of power
        PlaySound(spell.CastSoundResourceId);
        ShowClock();
        if (castOn is not null && spell.NeedsTarget)
        {
            ApplyEffects(spell.Effects, castOn, spell.Name);
            if (castOn != caster && !IsPartyOrPlayer(castOn) && spell.Effects.Any(e => e.Kind is MagicEffectKind.Damage or MagicEffectKind.Poison or MagicEffectKind.Sleep or MagicEffectKind.Paralyze or MagicEffectKind.Curse))
                FightBack(castOn, caster);
            StatusText.Text = $"{spell.Name} on {castOn.Name}.";
            return;
        }
        if (!spell.NeedsTarget)
        {
            ApplyEffects(spell.Effects, caster, spell.Name);
            StatusText.Text = $"{(caster == _player ? "You cast" : $"{caster.Name} casts")} {spell.Name}.";
            return;
        }
        BeginChoosingTarget($"Cast {spell.Name} on whom?", target =>
        {
            if (CharacterTargeted(target) is { } who)
            {
                ApplyEffects(spell.Effects, who, spell.Name);
                if (who != caster && !IsPartyOrPlayer(who) && spell.Effects.Any(e => e.Kind is MagicEffectKind.Damage or MagicEffectKind.Poison or MagicEffectKind.Sleep or MagicEffectKind.Paralyze or MagicEffectKind.Curse))
                    FightBack(who, caster); // a harmful spell is an attack
                StatusText.Text = $"{spell.Name} on {who.Name}.";
            }
            else if (ObjectTargeted(target) is { } hit) ApplyEffectsToObject(spell.Effects, hit.Instance, hit.Obj);
            else StatusText.Text = $"{spell.Name} fizzles - nothing there.";
        });
    }

    /// <summary>Everything one character carries, at any depth.</summary>
    private List<(InventoryItemInstance Item, List<InventoryItemInstance> List, CharacterDefinition Carrier)> CarriedItems(CharacterDefinition character)
    {
        var result = new List<(InventoryItemInstance, List<InventoryItemInstance>, CharacterDefinition)>();
        void Walk(List<InventoryItemInstance> list)
        {
            foreach (var item in list) { result.Add((item, list, character)); Walk(item.Contents); }
        }
        Walk(character.Inventory);
        return result;
    }
}
