// Ultima 6 engine (U6GameWindow): the spell system in use (Tools > Spell Editor; Engine/SpellSystem.cs) - Ultima 6 projects only.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The project's magic is one spell system (the user, 2026-11-25): Ultima 6's spellbook and words (U6GameWindow.U6Cast.cs,
/// as before), The Savage Empire's fetish and ingredients (an ingredient Used while a fetish is carried or next to the
/// caster casts its spell), or events only (the Cast key runs a Common Event). In every style a Common Event can run after
/// any spell is cast, the variable SpellCast holding its name. Spells are those of the spellbook objects, found by name.
/// </summary>
public partial class U6GameWindow
{
    private SpellSystem ActiveSpellSystem => _systemSettings.Ultima6.SpellSystems.Active;

    /// <summary>A spell of any spellbook object, by its name.</summary>
    private SpellDefinition? SpellNamed(string name) =>
        _objects.Objects.Where(o => o.Type == ObjectType.Spellbook).SelectMany(o => o.Spellbook.Spells).FirstOrDefault(s => s.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>A Common Event by its name, run for someone. False if there is none with commands.</summary>
    private bool RunNamedCommonEvent(string name, CharacterDefinition who)
    {
        if (name.Trim().Length == 0 || _systemSettings.CommonEvents.FirstOrDefault(c => c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)) is not { Commands.Count: > 0 } common) return false;
        RunCommandsOn(common.Commands, who);
        return true;
    }

    /// <summary>A spell was cast (DeliverSpell): the system's After Cast event, SpellCast = its name.</summary>
    private void AfterSpellCast(CharacterDefinition caster, SpellDefinition spell)
    {
        var system = ActiveSpellSystem;
        if (system.AfterCastEvent.Trim().Length == 0) return;
        SetScriptVariable(SpellSystemSettings.SpellCastVariable, spell.Name);
        RunNamedCommonEvent(system.AfterCastEvent, caster);
    }

    /// <summary>The Cast key in a system that isn't the spellbook's: true if it was handled here.</summary>
    private bool CastBySystem()
    {
        var system = ActiveSpellSystem;
        if (system.Style == SpellCastStyle.SpellbookWords) return false;
        U6Say(W("Cast") + "-");
        if (system.Style == SpellCastStyle.FetishIngredient) { U6Say("Use an ingredient with thy fetish."); return true; }
        if (!RunNamedCommonEvent(system.OnCastEvent, Actor)) U6Say(W("NoSpells"));
        return true;
    }

    /// <summary>Fetish style: a carried ingredient Used - its spell cast if a fetish is carried or next to the one using it. True if it was an ingredient.</summary>
    private bool TryFetishCast(InventoryItemInstance item, WorldObject ingredient, CharacterDefinition? who)
    {
        var system = ActiveSpellSystem;
        if (system.Style != SpellCastStyle.FetishIngredient || who is null) return false;
        var recipe = system.Recipes.FirstOrDefault(r => r.IngredientObjectId == ingredient.Id);
        if (recipe is null) return false;
        var at = CellOf(who);
        bool fetish = CarriedItems(who).Any(c => c.Item.Category == ReferencedItemCategory.Object && system.FetishObjectIds.Contains(c.Item.ItemId))
                      || _map.Objects.Any(o => o.ContainerInstanceId < 0 && system.FetishObjectIds.Contains(o.ObjectId) && CellsApart((o.X, o.Y), at) <= 1);
        if (!fetish) { U6Say(system.NoFetishLine); return true; }
        if (SpellNamed(recipe.SpellName) is not { } spell) { U6Say(W("NotPossible")); return true; }
        if (system.UsesMana)
        {
            if (Stat(who, BuiltInStatKind.Level, 1) < Math.Max(1, spell.Circle)) { U6Say(W("LevelTooLow")); return true; }
            if (Stat(who, BuiltInStatKind.Mana, 0) < spell.EffectiveManaCost) { U6Say(W("NoMagicPoints")); return true; }
            SetStat(who, BuiltInStatKind.Mana, Stat(who, BuiltInStatKind.Mana, 0) - spell.EffectiveManaCost);
        }
        if (recipe.UsesUpIngredient) TakeFromCarrier(who, ReferencedItemCategory.Object, ingredient.Id, 1);
        RefreshAfterMove();
        U6Say(W("Cast") + "-" + spell.Name);
        PlaySoundFrom(spell.CastSoundResourceId, who);
        if (!spell.NeedsTarget) { DeliverSpell(who, spell, who, null, at); RefreshHudPanel(); return true; }
        _castAiming = (who, spell); // its target: the map cursor, as a cast from the book
        StartHudVerb(HudVerb.Cast);
        _hudPrompt = W("OnWhom");
        RefreshHudText();
        _mapCursor = at;
        SetHudFocus(HudFocus.Map);
        return true;
    }

    /// <summary>Cast Spell (the command) in a system with no spellbook: the spell by its name, its mana if the system uses it.</summary>
    private bool CastSpellBySystem(CharacterDefinition caster, string name, CharacterDefinition? target)
    {
        var system = ActiveSpellSystem;
        if (system.Style == SpellCastStyle.SpellbookWords || SpellNamed(name) is not { } spell) return false;
        if (system.UsesMana)
        {
            if (Stat(caster, BuiltInStatKind.Mana, 0) < spell.EffectiveManaCost) { StatusText.Text = $"{caster.Name} hasn't the mana for {spell.Name}."; return false; }
            SetStat(caster, BuiltInStatKind.Mana, Stat(caster, BuiltInStatKind.Mana, 0) - spell.EffectiveManaCost);
        }
        PlaySoundFrom(spell.CastSoundResourceId, caster);
        var on = target ?? caster;
        DeliverSpell(caster, spell, on, null, CellOf(on));
        return true;
    }
}
