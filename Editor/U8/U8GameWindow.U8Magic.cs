// Ultima 8 engine (U8GameWindow) - added for Ultima 8.
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Ultima 8's magic, on the Ultima 7 engine's spells (a Spellbook object's spells - mana, reagents used up, effects):
///  - Each spell can name its school (Necromancy, Theurgy, Sorcery, Thaumaturgy, Tempestry).
///  - A focus (a wand, a rod, a symbol, an amulet - a Spellbook object marked a focus): double-clicked it casts its
///    spell at once, using one of its charges (each copy its own; -1 never runs out) - no book opened.
///  - Casting shows the Cast pose. A spellbook open doesn't pause the game (U8GameWindow.Pause.cs).
/// Pentagram has no rules of its own for these - Ultima 8's spells are in the game's data - so they are the designer's:
/// reagents, mana, the focus's charges, what each spell does.
/// </summary>
public partial class U8GameWindow
{
    private void CastFromFocus(Used u, CharacterDefinition caster)
    {
        var book = u.Obj.Spellbook;
        var spell = book.Spells.FirstOrDefault(s => KnowsSpell(u.Obj, u.Carried?.LearnedSpellsRuntime, s));
        if (spell is null) { StatusText.Text = $"\"{u.Obj.Name}\" holds no spell."; return; }
        int charges = u.Carried?.ChargesRuntime ?? u.Placed?.ChargesRuntime ?? -1;
        if (charges < 0 && book.U8FocusCharges >= 0) charges = book.U8FocusCharges; // not used yet: as it starts
        if (book.U8FocusCharges >= 0 && charges <= 0) { StatusText.Text = $"\"{u.Obj.Name}\" has no charges left."; return; }
        if (WhyCannotCast(caster, spell) is { } why) { StatusText.Text = why; return; }
        CastSpell(caster, spell);
        if (book.U8FocusCharges < 0) return;
        charges--;
        if (u.Carried is { } carried) carried.ChargesRuntime = charges;
        else if (u.Placed is { } placed) placed.ChargesRuntime = charges;
        if (StatusText.Text.Length == 0) StatusText.Text = $"\"{u.Obj.Name}\": {charges} charge{(charges == 1 ? "" : "s")} left.";
    }
}
