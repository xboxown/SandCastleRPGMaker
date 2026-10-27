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
///  - The book opens over the game as Exult's Spellbook_gump (or as the
///    paperdoll designed for it); only those who can cast magic - and, if the
///    book names them, only they - can read it.
/// </summary>
public partial class GameTestWindow
{
    /// <summary>Who is reading the spellbook that's open now (for Cast Spell from its own paperdoll).</summary>
    private CharacterDefinition? _spellbookReader;
    /// <summary>Each copy's bookmark: the last spell chosen in it (Exult's bookmark).</summary>
    private readonly Dictionary<object, string> _bookmarks = new(ReferenceEqualityComparer.Instance);

    /// <summary>Not everyone can read a spellbook: Can cast magic (Character Editor), and on the book's own list when it has one. True = refused (and said why).</summary>
    private bool RefusedSpellbook(WorldObject book, CharacterDefinition reader)
    {
        string? why = !reader.CanCastMagic ? $"{reader.Name} can't make sense of the runes in \"{book.Name}\"."
            : book.Spellbook.AllowedCharacterIds.Count > 0 && !book.Spellbook.AllowedCharacterIds.Contains(reader.Id) ? $"\"{book.Name}\" isn't {reader.Name}'s to read."
            : null;
        if (why is null) return false;
        StatusText.Text = why;
        if (reader.IsHuman) ShowFloatingText(reader, "I cannot read this.", 2500);
        return true;
    }

    /// <summary>How many times the caster could cast it now: by mana and by each reagent carried (Exult shows it on the page).</summary>
    private int CastsLeft(CharacterDefinition caster, SpellDefinition spell)
    {
        int left = spell.EffectiveManaCost > 0 ? Stat(caster, BuiltInStatKind.Mana, 0) / spell.EffectiveManaCost : 99;
        var carried = CarriedItems(caster);
        foreach (var reagent in spell.AllReagents())
            left = Math.Min(left, CountOf(carried, reagent.ObjectId) / Math.Max(1, reagent.Quantity));
        return Math.Clamp(left, 0, 99);
    }

    /// <summary>
    /// Exult's Spellbook_gump: the open book over the game - two pages, a
    /// circle a page (more than eight spells: it goes on over the next page),
    /// each spell's icon, name and how many times it can be cast now; spells
    /// that can't be cast are dimmed, saying why. Click marks a spell (the
    /// bookmark - the book opens at it next time), double-click casts it and
    /// closes the book; the bottom corners turn the pages.
    /// </summary>
    private void UseSpellbook(Used u)
    {
        if (FireUseEvent(u)) return;
        var caster = UserOf(u);
        if (caster is null || RefusedSpellbook(u.Obj, caster)) return;
        var book = u.Obj.Spellbook;
        var spells = book.Spells.Where(s => KnowsSpell(u.Obj, u.Carried?.LearnedSpellsRuntime, s)).OrderBy(s => s.Circle).ToList(); // only those this copy knows
        object key = (object?)u.Placed ?? (object?)u.Carried ?? u.Obj;

        // Its pages: a circle each, eight spells a page (two columns of four, as Exult's).
        var pages = new List<(int Circle, List<SpellDefinition> Spells)>();
        foreach (var circle in spells.GroupBy(s => s.Circle))
        {
            var list = circle.ToList();
            for (int i = 0; i < list.Count; i += 8) pages.Add((circle.Key, list.Skip(i).Take(8).ToList()));
        }
        if (pages.Count == 0) pages.Add((-1, new List<SpellDefinition>()));
        int SpreadOf(string? spellName) => Math.Max(0, pages.FindIndex(p => p.Spells.Any(s => s.Name == spellName)) / 2);
        int spread = SpreadOf(_bookmarks.GetValueOrDefault(key));

        var background = FrameOf(book.OpenBookSprite, 0);
        double width = background?.PixelWidth ?? 296, height = background?.PixelHeight ?? 184;
        var canvas = new Canvas { Width = width, Height = height, Background = Brushes.Transparent, ClipToBounds = true };
        var ink = new SolidColorBrush(Color.FromRgb(0x3B, 0x24, 0x10));

        void Draw()
        {
            canvas.Children.Clear();
            if (background is not null)
                canvas.Children.Add(new Image { Source = background, Width = width, Height = height, Stretch = Stretch.Fill });
            else
            {
                // A plain book: a leather cover, two parchment pages and the spine.
                canvas.Children.Add(new Border { Width = width, Height = height, CornerRadius = new CornerRadius(5), Background = new SolidColorBrush(Color.FromRgb(0x5A, 0x34, 0x18)), BorderBrush = new SolidColorBrush(Color.FromRgb(0x2E, 0x18, 0x08)), BorderThickness = new Thickness(2) });
                foreach (double left in new[] { 8.0, width / 2 + 1 })
                {
                    var page = new Border { Width = width / 2 - 9, Height = height - 16, Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xE4, 0xC2)), BorderBrush = new SolidColorBrush(Color.FromRgb(0xB8, 0x9B, 0x6A)), BorderThickness = new Thickness(0.5) };
                    Canvas.SetLeft(page, left);
                    Canvas.SetTop(page, 8);
                    canvas.Children.Add(page);
                }
                var spine = new Border { Width = 2, Height = height - 16, Background = new SolidColorBrush(Color.FromRgb(0x8A, 0x6A, 0x3C)) };
                Canvas.SetLeft(spine, width / 2 - 1);
                Canvas.SetTop(spine, 8);
                canvas.Children.Add(spine);
            }
            string? marked = _bookmarks.GetValueOrDefault(key);
            for (int side = 0; side < 2; side++)
            {
                int index = spread * 2 + side;
                if (index >= pages.Count) continue;
                var (circle, pageSpells) = pages[index];
                double pageLeft = side == 0 ? width * 0.07 : width * 0.53, pageTop = height * 0.08, pageWidth = width * 0.40, pageHeight = height * 0.80;
                bool continued = index > 0 && pages[index - 1].Circle == circle;
                var title = new TextBlock { Text = circle < 0 ? "(no spells in this book)" : CircleTitle(circle) + (continued ? " (cont.)" : ""), FontSize = 8, FontWeight = FontWeights.Bold, Foreground = ink, Width = pageWidth, TextAlignment = TextAlignment.Center };
                Canvas.SetLeft(title, pageLeft);
                Canvas.SetTop(title, pageTop);
                canvas.Children.Add(title);
                double cellWidth = pageWidth / 2, cellHeight = (pageHeight - 14) / 4;
                for (int i = 0; i < pageSpells.Count; i++)
                {
                    var spell = pageSpells[i];
                    string? why = WhyCannotCast(caster, spell);
                    int casts = CastsLeft(caster, spell);
                    double cellLeft = pageLeft + (i / 4) * cellWidth, cellTop = pageTop + 14 + (i % 4) * cellHeight;
                    var cell = new Grid { Width = cellWidth, Height = cellHeight, Background = Brushes.Transparent, Opacity = why is null ? 1 : 0.45, Cursor = System.Windows.Input.Cursors.Hand };
                    cell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(cellHeight) });
                    cell.ColumnDefinitions.Add(new ColumnDefinition());
                    UIElement icon = FrameOf(spell.Icon, 0) is { } picture
                        ? new Image { Source = picture, Width = cellHeight - 4, Height = cellHeight - 4, Stretch = Stretch.Uniform }
                        : new Border { Width = cellHeight - 6, Height = cellHeight - 6, BorderBrush = ink, BorderThickness = new Thickness(0.6), CornerRadius = new CornerRadius(2),
                                       Child = new TextBlock { Text = spell.Name.Length > 0 ? spell.Name[..1] : "?", FontSize = 9, Foreground = ink, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                    cell.Children.Add(icon);
                    var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
                    words.Children.Add(new TextBlock { Text = spell.Name, FontSize = 6.5, Foreground = ink, TextTrimming = TextTrimming.CharacterEllipsis });
                    words.Children.Add(new TextBlock { Text = why is null ? $"x{casts}" : "-", FontSize = 5.5, Foreground = Brushes.SaddleBrown });
                    Grid.SetColumn(words, 1);
                    cell.Children.Add(words);
                    cell.ToolTip = $"{spell.Name}{(spell.Words.Length > 0 ? $" - \"{spell.Words}\"" : "")}\n{spell.EffectiveManaCost} mana{ReagentList(spell)}\n" + (why ?? $"Can be cast {casts} time{(casts == 1 ? "" : "s")} now. Double-click to cast.");
                    var chosen = spell;
                    cell.MouseLeftButtonDown += (_, e) =>
                    {
                        e.Handled = true;
                        _bookmarks[key] = chosen.Name;
                        if (e.ClickCount < 2) { Draw(); return; } // a click: the bookmark
                        if (why is not null) { StatusText.Text = why; Draw(); return; }
                        CloseObjectOverlay(); // Exult: casting closes the book
                        CastSpell(caster, chosen);
                    };
                    Canvas.SetLeft(cell, cellLeft);
                    Canvas.SetTop(cell, cellTop);
                    canvas.Children.Add(cell);
                    if (marked == spell.Name)
                    {
                        UIElement ribbon = FrameOf(book.BookmarkSprite, 0) is { } mark ? new Image { Source = mark, Stretch = Stretch.None }
                            : new Border { Width = 4, Height = 12, Background = Brushes.DarkRed };
                        Canvas.SetLeft(ribbon, cellLeft + cellHeight - 6);
                        Canvas.SetTop(ribbon, cellTop - 4);
                        canvas.Children.Add(ribbon);
                    }
                }
            }
            // Who is reading, and their mana.
            var reader = new TextBlock { Text = $"{caster.Name}: mana {Stat(caster, BuiltInStatKind.Mana, 0)} of {Stat(caster, BuiltInStatKind.Magic, 0)}, level {Stat(caster, BuiltInStatKind.Level, 0)}", FontSize = 5.5, Foreground = Brushes.SaddleBrown, Width = width, TextAlignment = TextAlignment.Center };
            Canvas.SetTop(reader, height * 0.89);
            canvas.Children.Add(reader);
            // The corners turn the pages; the X closes it (and Escape).
            void Corner(string text, double left, int turn)
            {
                var corner = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = ink, Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(3, 0, 3, 0) };
                corner.MouseLeftButtonDown += (_, e) =>
                {
                    e.Handled = true;
                    spread += turn;
                    PlaySound(book.PageTurnSoundResourceId);
                    Draw();
                };
                Canvas.SetLeft(corner, left);
                Canvas.SetTop(corner, height * 0.84);
                canvas.Children.Add(corner);
            }
            if (spread > 0) Corner("<", width * 0.07, -1);
            if (spread * 2 + 2 < pages.Count) Corner(">", width * 0.89, +1);
            var close = new TextBlock { Text = "x", FontSize = 8, Foreground = ink, Cursor = System.Windows.Input.Cursors.Hand, Padding = new Thickness(3, 0, 3, 0) };
            close.MouseLeftButtonDown += (_, e) => { e.Handled = true; CloseObjectOverlay(); };
            Canvas.SetLeft(close, width * 0.92);
            Canvas.SetTop(close, height * 0.05);
            canvas.Children.Add(close);
        }

        Draw();
        if (PaperdollSoundIsGlobal) PlaySound(_systemSettings.Sounds.PaperdollOpenSoundId); // a spellbook is a paperdoll too
        ShowObjectOverlay(canvas, takesClicks: true, scaleLikeViewport: true);
    }

    private string CircleTitle(int circle) => _objects.CircleName(circle);

    private string ReagentList(SpellDefinition spell)
    {
        var reagents = spell.AllReagents().ToList();
        return reagents.Count == 0 ? "" : "; " + string.Join(", ", reagents.Select(r => $"{(r.Quantity > 1 ? $"{r.Quantity} " : "")}{ObjectDef(r.ObjectId)?.Name ?? "?"}"));
    }

    private static int CountOf(List<(InventoryItemInstance Item, List<InventoryItemInstance> List, CharacterDefinition Carrier)> carried, int objectId) =>
        carried.Where(c => c.Item.Category == ReferencedItemCategory.Object && c.Item.ItemId == objectId).Sum(c => Math.Max(1, c.Item.Quantity));

    /// <summary>can_do_spell - null if it can be cast now, else why not.</summary>
    private string? WhyCannotCast(CharacterDefinition caster, SpellDefinition spell)
    {
        if (IsIncapacitated(caster)) return "The caster can't act.";
        if (Has(caster, Ailment.Cursed)) return "The caster is cursed.";
        int mana = Stat(caster, BuiltInStatKind.Mana, 0);
        if (mana < spell.EffectiveManaCost) return $"Not enough mana ({mana} of {spell.EffectiveManaCost}).";
        int level = Stat(caster, BuiltInStatKind.Level, int.MaxValue), needed = _objects.LevelForCircle(spell.Circle);
        if (level < needed) return $"Needs level {needed} (has {level}).";
        var carried = CarriedItems(caster);
        foreach (var reagent in spell.AllReagents())
        {
            int have = CountOf(carried, reagent.ObjectId), need = Math.Max(1, reagent.Quantity);
            if (have < need) return $"Needs {need} {ObjectDef(reagent.ObjectId)?.Name ?? "of a reagent"} (has {have}).";
        }
        return null;
    }

    /// <summary>do_spell, then the spell's effects - on the caster, or a target clicked.</summary>
    /// <param name="castOn">Cast Spell (event command): its target given, no clicking.</param>
    private void CastSpell(CharacterDefinition caster, SpellDefinition spell, CharacterDefinition? castOn = null)
    {
        if (WhyCannotCast(caster, spell) is { } why) { StatusText.Text = why; return; }
        SetStat(caster, BuiltInStatKind.Mana, Stat(caster, BuiltInStatKind.Mana, 0) - spell.EffectiveManaCost);
        var carried = CarriedItems(caster);
        foreach (var reagent in spell.AllReagents()) TakeFrom(carried, reagent.ObjectId, Math.Max(1, reagent.Quantity));
        RefreshAfterMove();
        if (spell.Words.Trim().Length > 0) ShowFloatingText(caster, spell.Words.Trim(), 2500); // begin_casting: the words of power
        PlaySoundFrom(spell.CastSoundResourceId, caster);
        ShowClock();
        StatusText.Text = "";
        if (castOn is not null && spell.NeedsTarget) { DeliverSpell(caster, spell, castOn, null, CellOf(castOn)); return; } // GameTestWindow.Spells.cs
        if (!spell.NeedsTarget) { DeliverSpell(caster, spell, caster, null, CellOf(caster)); return; }
        if (caster != _player && !IsPartyOrPlayer(caster)) { DeliverSpell(caster, spell, caster, null, CellOf(caster)); return; } // nobody to click for it
        BeginChoosingTarget($"Cast {spell.Name} on whom?", target =>
        {
            // Someone, something (a door, a chest, a body), or a spot on the ground.
            var who = CharacterTargeted(target);
            object? thing = target is MapEntityTarget { Entity: ObjectInstance or PlacedItemInstance } entity ? entity.Entity : null;
            var cell = who is not null ? CellOf(who) : thing switch { ObjectInstance o => (o.X, o.Y), PlacedItemInstance i => (i.X, i.Y), _ => LastTargetCell() };
            DeliverSpell(caster, spell, who, thing, cell);
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
