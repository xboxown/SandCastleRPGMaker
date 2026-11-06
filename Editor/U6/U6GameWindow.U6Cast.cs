// Ultima 6 engine (U6GameWindow): the Cast verb, as Ultima 6 - Ultima 6 projects only.
using System.Windows;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Cast, as Nuvie's Magic (start_new_spell, cast, callback): a spellbook must be readied in a hand ("No spellbook is readied.").
/// The panel turns into the book - a circle a page, Left / Right to turn, Up / Down to pick - or the spell's syllables are typed
/// (A An, B Bet, C Corp ... I In, L Lor: "In Lor" is I L); Enter casts, Escape stops. Then, in Ultima 6's order: in the book
/// ("That spell is not in thy spellbook!"), the caster's level at least the circle ("Your level is not high enough."), the
/// magic points ("Not enough magic points."), the reagents ("No Reagents."). Its name and words are said; a spell that needs a
/// target asks for one with the map cursor (or a click).
/// </summary>
public partial class U6GameWindow
{
    private static readonly string[] Syllables =
    {
        "An", "Bet", "Corp", "Des", "Ex", "Flam", "Grav", "Hur", "In", "Jux", "Kal", "Lor", "Mani",
        "Nox", "Ort", "Por", "Quas", "Rel", "Sanct", "Tym", "Uus", "Vas", "Wis", "Xen", "Ylem", "Zu"
    };

    private sealed class CastState
    {
        public required CharacterDefinition Caster;
        public required List<SpellDefinition> Spells;
        public List<int> Circles = new();
        public int Page;
        public int Picked;
        public string Typed = string.Empty;
    }

    private CastState? _casting;
    /// <summary>A spell cast that waits for its target (the map cursor).</summary>
    private (CharacterDefinition Caster, SpellDefinition Spell)? _castAiming;

    /// <summary>The spells of the books the caster has readied in a hand (Magic::book_equipped), those each copy knows.</summary>
    private List<SpellDefinition>? ReadiedBookSpells(CharacterDefinition caster)
    {
        List<SpellDefinition>? spells = null;
        foreach (var slot in new[] { EquipmentSlot.RightHand, EquipmentSlot.LeftHand })
        {
            if (!caster.EquippedSlots.TryGetValue(slot, out int id) || caster.Inventory.FirstOrDefault(i => i.InstanceId == id) is not { } held) continue;
            if (DefinitionOf(held) is not { Type: ObjectType.Spellbook } book || book.Spellbook.U8Focus) continue;
            spells ??= new List<SpellDefinition>();
            spells.AddRange(book.Spellbook.Spells.Where(s => KnowsSpell(book, held.LearnedSpellsRuntime, s) && !spells.Contains(s)));
        }
        return spells;
    }

    private void U6StartCast()
    {
        var caster = Actor;
        string verb = W("Cast") + "-";
        if (ReadiedBookSpells(caster) is not { } spells) { U6Say(verb); U6Say(W("NoSpellbook")); return; }
        if (spells.Count == 0) { U6Say(verb); U6Say(W("NoSpells")); return; }
        var state = new CastState { Caster = caster, Spells = spells.OrderBy(s => s.Circle).ToList() };
        state.Circles = state.Spells.Select(s => s.Circle).Distinct().ToList();
        _casting = state;
        StartHudVerb(HudVerb.Cast);
        _hudPrompt = verb;
        RefreshHudText();
        RefreshHudPanel(); // the panel shows the book
    }

    private List<SpellDefinition> CastPage(CastState s) => s.Circles.Count == 0 ? new() : s.Spells.Where(x => x.Circle == s.Circles[s.Page]).ToList();

    /// <summary>The panel while choosing: the circle's spells, the one picked marked.</summary>
    private FrameworkElement? HudCastPanel()
    {
        if (_casting is not { } s || Hud.First(HudPieceKind.Panel) is not { } panel) return null;
        var page = CastPage(s);
        string circle = s.Circles.Count == 0 ? "" : _objects.CircleName(s.Circles[s.Page]);
        var lines = new List<string> { $"{circle}  ({s.Page + 1}/{Math.Max(1, s.Circles.Count)})" };
        for (int i = 0; i < page.Count; i++) lines.Add((i == s.Picked && s.Typed.Length == 0 ? "> " : "  ") + page[i].Name);
        var piece = new HudPiece { X = panel.X, Y = panel.Y, Width = panel.Width, Height = panel.Height };
        return HudWordsAt(piece, string.Join("\n", lines));
    }

    /// <summary>Keys while the book is open: syllables, Backspace, the arrows, Enter, Escape.</summary>
    private bool U6CastKey(Key key)
    {
        if (_casting is not { } s) return false;
        if (KeyIs(key, "Cancel") || key == Key.Escape) { EndCasting(); CancelHudVerb(); return true; }
        if (key is Key.Enter or Key.Return) { ChooseSpell(); return true; }
        if (key == Key.Back) { if (s.Typed.Length > 0) s.Typed = s.Typed[..^1]; ShowTyped(); return true; }
        if (key is >= Key.A and <= Key.Z)
        {
            if (s.Typed.Length < 4) s.Typed += (char)('A' + (key - Key.A));
            ShowTyped();
            return true;
        }
        int count = CastPage(s).Count;
        if (key == Key.Up && count > 0) s.Picked = (s.Picked + count - 1) % count;
        else if (key == Key.Down && count > 0) s.Picked = (s.Picked + 1) % count;
        else if (key == Key.Left && s.Circles.Count > 0) { s.Page = (s.Page + s.Circles.Count - 1) % s.Circles.Count; s.Picked = 0; }
        else if (key == Key.Right && s.Circles.Count > 0) { s.Page = (s.Page + 1) % s.Circles.Count; s.Picked = 0; }
        RefreshHudPanel();
        return true;
    }

    private void ShowTyped()
    {
        if (_casting is not { } s) return;
        _hudPrompt = W("Cast") + "-" + string.Join(" ", s.Typed.Select(c => Syllables[c - 'A']));
        RefreshHudText();
        RefreshHudPanel();
    }

    /// <summary>A spell's syllables as typed letters ("In Lor" -> "IL").</summary>
    private static string Invocation(SpellDefinition spell) =>
        string.Concat(spell.Words.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])));

    private void EndCasting()
    {
        _casting = null;
        RefreshHudPanel();
    }

    /// <summary>Magic::cast: the spell named, then Ultima 6's checks, then its reagents and magic points spent.</summary>
    private void ChooseSpell()
    {
        if (_casting is not { } s) return;
        var caster = s.Caster;
        string typed = s.Typed;
        SpellDefinition? spell = typed.Length > 0
            ? s.Spells.FirstOrDefault(x => Invocation(x) == typed)
            : CastPage(s).ElementAtOrDefault(s.Picked);
        string line = _hudPrompt;
        EndCasting();
        EndHudVerb();
        SetHudFocus(HudFocus.Map);
        if (spell is null) { U6Say(line); U6Say(W("NotInBook")); return; }
        if (typed.Length > 0) { U6Say(line); U6Say("(" + spell.Name + ")"); }
        else { U6Say(W("Cast") + "-" + spell.Name); if (spell.Words.Trim().Length > 0) U6Say("\"" + spell.Words.Trim() + "\""); }

        int circle = Math.Max(1, spell.Circle);
        if (Stat(caster, BuiltInStatKind.Level, 1) < circle) { U6Say(W("LevelTooLow")); return; }
        if (Stat(caster, BuiltInStatKind.Mana, 0) < spell.EffectiveManaCost) { U6Say(W("NoMagicPoints")); return; }
        var carried = CarriedItems(caster);
        if (spell.AllReagents().Any(r => CountOf(carried, r.ObjectId) < Math.Max(1, r.Quantity))) { U6Say(W("NoReagents")); return; }
        SetStat(caster, BuiltInStatKind.Mana, Stat(caster, BuiltInStatKind.Mana, 0) - spell.EffectiveManaCost);
        foreach (var reagent in spell.AllReagents()) TakeFrom(carried, reagent.ObjectId, Math.Max(1, reagent.Quantity));
        RefreshAfterMove();
        PlaySoundFrom(spell.CastSoundResourceId, caster);
        if (!spell.NeedsTarget)
        {
            DeliverSpell(caster, spell, caster, null, CellOf(caster)); // U6GameWindow.Spells.cs
            RefreshHudPanel();
            PassTurnIfFighting();
            return;
        }
        // Its target: the map cursor (it starts on the caster) or a click; Enter there.
        _castAiming = (caster, spell);
        StartHudVerb(HudVerb.Cast);
        _hudPrompt = W("OnWhom");
        RefreshHudText();
        _mapCursor = CellOf(caster);
        SetHudFocus(HudFocus.Map);
    }

    /// <summary>The target picked: the one there, the thing there, or the spot.</summary>
    private void CastOnCell(int cx, int cy, object? thing)
    {
        if (_castAiming is not { } aim) { FinishHudVerb(ThingName(thing), W("NotPossible")); return; }
        _castAiming = null;
        CharacterDefinition? who = thing switch
        {
            CharacterDefinition c => c,
            PlacedCharacter pc => _mapCharacters.FirstOrDefault(m => m.Placement == pc)?.Character,
            _ => null
        };
        object? target = thing is ObjectInstance or PlacedItemInstance ? thing : null;
        FinishHudVerb(ThingName(thing));
        DeliverSpell(aim.Caster, aim.Spell, who, target, (cx, cy));
        RefreshHudPanel();
    }
}
