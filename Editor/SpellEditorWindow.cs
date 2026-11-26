using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Tools > Spell Editor (Ultima 6 projects only - the user, 2026-11-25): the project's magic as spell systems, one in use.
/// Each says how a spell is cast - Ultima 6's spellbook and words of power, The Savage Empire's fetish and ingredients, or
/// events only - and its Common Events (what the Cast key does, what runs after any spell). The spells themselves are the
/// spellbook objects' (Object Editor > Spellbook), found by name. Ready-made systems to learn from: Ultima 6 and The Savage
/// Empire (it adds a spellbook object of example spells). Engine/SpellSystem.cs; the game: U6GameWindow.SpellSystems.cs and
/// the Apple IIgs player.
/// </summary>
public class SpellEditorWindow : Window
{
    private readonly SpellSystemSettings _settings;
    private readonly ObjectDatabase _objects;
    private readonly List<CommonEventDefinition> _commonEvents;
    private readonly ListBox _list = new() { DisplayMemberPath = nameof(SpellSystem.Name) };
    private readonly StackPanel _body = new() { Margin = new Thickness(10, 0, 0, 0) };
    private readonly TextBlock _inUse = new() { FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap };

    public SpellEditorWindow(SpellSystemSettings settings, ObjectDatabase objects, List<CommonEventDefinition> commonEvents)
    {
        _settings = settings;
        _objects = objects;
        _commonEvents = commonEvents;
        Title = "Spell Editor";
        Width = 980;
        Height = 660;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var buttons = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        Button B(string text, Action click) { var b = new Button { Content = text, Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 4, 4) }; b.Click += (_, _) => click(); buttons.Children.Add(b); return b; }
        B("New (blank)", () => Add(new SpellSystem { Id = NextId(), Name = "New spell system", Style = SpellCastStyle.EventsOnly }));
        B("Ultima 6 template", () => Add(SpellSystemSettings.UltimaSixTemplate(NextId())));
        B("Savage Empire template", AddSavageEmpire);
        B("Delete", () =>
        {
            if (_list.SelectedItem is not SpellSystem s) return;
            if (MessageBox.Show(this, $"Delete \"{s.Name}\"?", Title, MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            _settings.Systems.Remove(s);
            if (_settings.ActiveId == s.Id) _settings.ActiveId = -1;
            Refresh(_settings.Systems.FirstOrDefault());
        });
        var left = new DockPanel { Width = 250 };
        DockPanel.SetDock(buttons, Dock.Bottom);
        left.Children.Add(buttons);
        var intro = new TextBlock { Text = "Spell systems (one is used in the game)", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) };
        DockPanel.SetDock(intro, Dock.Top);
        left.Children.Add(intro);
        left.Children.Add(_list);
        _list.SelectionChanged += (_, _) => Show(_list.SelectedItem as SpellSystem);

        var right = new DockPanel();
        DockPanel.SetDock(_inUse, Dock.Top);
        right.Children.Add(_inUse);
        right.Children.Add(new ScrollViewer { Content = _body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var root = new DockPanel { Margin = new Thickness(10) };
        DockPanel.SetDock(left, Dock.Left);
        root.Children.Add(left);
        root.Children.Add(right);
        Content = root;
        Refresh(_settings.Systems.FirstOrDefault(s => s.Id == _settings.ActiveId) ?? _settings.Systems.FirstOrDefault());
    }

    private int NextId() => _settings.Systems.Count == 0 ? 1 : _settings.Systems.Max(s => s.Id) + 1;

    private void Add(SpellSystem system)
    {
        _settings.Systems.Add(system);
        Refresh(system);
    }

    /// <summary>The Savage Empire's: a fetish system, with a spellbook object of example spells to start from (once).</summary>
    private void AddSavageEmpire()
    {
        var system = SpellSystemSettings.SavageEmpireTemplate(NextId());
        const string bookName = "Spells (The Savage Empire)";
        if (_objects.Objects.All(o => o.Name != bookName))
        {
            var book = new WorldObject { Id = _objects.Objects.Count == 0 ? 1 : _objects.Objects.Max(o => o.Id) + 1, Name = bookName, Type = ObjectType.Spellbook };
            book.Spellbook.StartsWithAllSpells = true;
            book.Spellbook.Spells.Add(new SpellDefinition { Name = "Light", Circle = 1, Effects = { new MagicEffect { Kind = MagicEffectKind.Light, Min = 1, Max = 1, Seconds = 300 } } });
            book.Spellbook.Spells.Add(new SpellDefinition { Name = "Heal", Circle = 1, NeedsTarget = true, Effects = { new MagicEffect { Kind = MagicEffectKind.Heal, Min = 10, Max = 20 } } });
            book.Spellbook.Spells.Add(new SpellDefinition { Name = "Fire", Circle = 2, NeedsTarget = true, Effects = { new MagicEffect { Kind = MagicEffectKind.Damage, Min = 5, Max = 15 } } });
            _objects.Objects.Add(book);
        }
        foreach (var name in new[] { "Light", "Heal", "Fire" }) system.Recipes.Add(new SpellRecipe { SpellName = name });
        Add(system);
        MessageBox.Show(this, $"Added \"{system.Name}\" and the spellbook object \"{bookName}\" with three example spells (Light, Heal, Fire).\n\nPick the fetish object(s) and each spell's ingredient below, then Use this system in the game.", Title);
    }

    private void Refresh(SpellSystem? select)
    {
        _list.ItemsSource = null;
        _list.ItemsSource = _settings.Systems;
        _list.SelectedItem = select;
        if (select is null) Show(null);
        ShowInUse();
    }

    private void ShowInUse() => _inUse.Text = "In use in the game: " + _settings.Active.Name;

    private IEnumerable<string> SpellNames() =>
        _objects.Objects.Where(o => o.Type == ObjectType.Spellbook).SelectMany(o => o.Spellbook.Spells).Select(s => s.Name).Distinct().OrderBy(n => n);

    private void Show(SpellSystem? system)
    {
        _body.Children.Clear();
        if (system is null)
        {
            _body.Children.Add(Note("No spell system yet: the game uses Ultima 6's spellbook and words of power. Add one from a template to change it."));
            return;
        }
        var use = new Button { Content = _settings.ActiveId == system.Id ? "This system is in use" : "Use this system in the game", Padding = new Thickness(8, 2, 8, 2), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
        use.Click += (_, _) => { _settings.ActiveId = system.Id; use.Content = "This system is in use"; ShowInUse(); };
        _body.Children.Add(use);
        var name = new TextBox { Text = system.Name, Width = 360 };
        name.TextChanged += (_, _) => { system.Name = name.Text; _list.Items.Refresh(); ShowInUse(); };
        _body.Children.Add(Row("Name", name));
        var style = new ComboBox { Width = 360, ItemsSource = new[] { "Ultima 6: a spellbook and words of power", "The Savage Empire: a fetish and ingredients", "Events only: the Cast key runs a Common Event" }, SelectedIndex = (int)system.Style };
        style.SelectionChanged += (_, _) => { system.Style = (SpellCastStyle)Math.Max(0, style.SelectedIndex); Show(system); };
        _body.Children.Add(Row("How spells are cast", style));

        switch (system.Style)
        {
            case SpellCastStyle.SpellbookWords:
                _body.Children.Add(Note("As Ultima 6: a spellbook readied in a hand; Cast shows its pages, or the syllables' first letters are typed (A An, B Bet ... I In, L Lor: In Lor = I L). Circles, the caster's level, mana and reagents as each spell says. The spells: Object Editor > a Spellbook object; Learn Spell puts one into a book (if it doesn't know all its spells)."));
                break;
            case SpellCastStyle.FetishIngredient:
                _body.Children.Add(Note("As The Savage Empire: no spellbook. Use one of the ingredients below while the caster carries a fetish (or stands next to one): its spell is cast - at a target the map cursor picks, if the spell needs one. The spells are found by name in the spellbook objects (Object Editor)."));
                _body.Children.Add(FetishList(system));
                _body.Children.Add(Recipes(system));
                var mana = new CheckBox { Content = "Costs the spell's mana and needs its circle's level (unticked: free, as The Savage Empire)", IsChecked = system.UsesMana, Margin = new Thickness(0, 6, 0, 0) };
                mana.Click += (_, _) => system.UsesMana = mana.IsChecked == true;
                _body.Children.Add(mana);
                var line = new TextBox { Text = system.NoFetishLine, Width = 360 };
                line.TextChanged += (_, _) => system.NoFetishLine = line.Text;
                _body.Children.Add(Row("Said with no fetish near", line));
                break;
            case SpellCastStyle.EventsOnly:
                _body.Children.Add(Note("Your own magic: the Cast key runs this Common Event (Who: the one casting). With event commands - Show Text with choices, Input Text, Advance IF, Cast Spell, Play Animation, Hurt In Area, the stats - any spell system can be made: runes, songs, prayers, a mana of your own."));
                _body.Children.Add(Row("When Cast is pressed", EventPicker(() => system.OnCastEvent, v => system.OnCastEvent = v)));
                break;
        }
        _body.Children.Add(Row("After any spell is cast", EventPicker(() => system.AfterCastEvent, v => system.AfterCastEvent = v)));
        _body.Children.Add(Note($"Its Common Event runs right after a spell is cast in this system; the variable {SpellSystemSettings.SpellCastVariable} holds the spell's name (Advance IF on it: a crowd gasps at a Fire spell, a priest frowns at necromancy)."));
        _body.Children.Add(Note("Common Events are made in Tools > Common Events; spells in the Object Editor (a Spellbook object)."));
    }

    private FrameworkElement FetishList(SpellSystem system)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "Fetishes (the caster carries one, or stands next to one)", FontWeight = FontWeights.SemiBold });
        void Show()
        {
            while (panel.Children.Count > 1) panel.Children.RemoveAt(1);
            foreach (int id in system.FetishObjectIds.ToList())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                row.Children.Add(new TextBlock { Text = _objects.Objects.FirstOrDefault(o => o.Id == id)?.Name ?? $"(object {id})", Width = 240, VerticalAlignment = VerticalAlignment.Center });
                var remove = new Button { Content = "Remove", Padding = new Thickness(6, 0, 6, 0) };
                remove.Click += (_, _) => { system.FetishObjectIds.Remove(id); Show(); };
                row.Children.Add(remove);
                panel.Children.Add(row);
            }
            var pick = ObjectCombo(-1);
            var add = new Button { Content = "Add fetish", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(4, 0, 0, 0) };
            add.Click += (_, _) => { if (pick.SelectedItem is WorldObject o && !system.FetishObjectIds.Contains(o.Id)) { system.FetishObjectIds.Add(o.Id); Show(); } };
            var addRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            addRow.Children.Add(pick);
            addRow.Children.Add(add);
            panel.Children.Add(addRow);
        }
        Show();
        return panel;
    }

    private FrameworkElement Recipes(SpellSystem system)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        panel.Children.Add(new TextBlock { Text = "Ingredients and their spells", FontWeight = FontWeights.SemiBold });
        void Show()
        {
            while (panel.Children.Count > 1) panel.Children.RemoveAt(1);
            foreach (var recipe in system.Recipes.ToList())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
                var ingredient = ObjectCombo(recipe.IngredientObjectId);
                ingredient.SelectionChanged += (_, _) => { if (ingredient.SelectedItem is WorldObject o) recipe.IngredientObjectId = o.Id; };
                row.Children.Add(ingredient);
                row.Children.Add(new TextBlock { Text = "casts", Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
                var spell = new ComboBox { Width = 170, IsEditable = true, ItemsSource = SpellNames().ToList(), Text = recipe.SpellName };
                spell.LostFocus += (_, _) => recipe.SpellName = spell.Text.Trim();
                spell.SelectionChanged += (_, _) => { if (spell.SelectedItem is string s) recipe.SpellName = s; };
                row.Children.Add(spell);
                var used = new CheckBox { Content = "used up", IsChecked = recipe.UsesUpIngredient, Margin = new Thickness(6, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center };
                used.Click += (_, _) => recipe.UsesUpIngredient = used.IsChecked == true;
                row.Children.Add(used);
                var remove = new Button { Content = "Remove", Padding = new Thickness(6, 0, 6, 0) };
                remove.Click += (_, _) => { system.Recipes.Remove(recipe); Show(); };
                row.Children.Add(remove);
                panel.Children.Add(row);
            }
            var add = new Button { Content = "Add ingredient", Padding = new Thickness(6, 0, 6, 0), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 0) };
            add.Click += (_, _) => { system.Recipes.Add(new SpellRecipe()); Show(); };
            panel.Children.Add(add);
        }
        Show();
        return panel;
    }

    private ComboBox ObjectCombo(int selectedId)
    {
        var all = _objects.Objects.OrderBy(o => o.Name).ToList();
        return new ComboBox { Width = 240, ItemsSource = all, DisplayMemberPath = nameof(WorldObject.Name), SelectedItem = all.FirstOrDefault(o => o.Id == selectedId) };
    }

    private ComboBox EventPicker(Func<string> get, Action<string> set)
    {
        var names = new List<string> { "(none)" };
        names.AddRange(_commonEvents.Select(c => c.Name).OrderBy(n => n));
        var box = new ComboBox { Width = 360, ItemsSource = names, SelectedItem = get().Length > 0 && names.Contains(get()) ? get() : "(none)" };
        box.SelectionChanged += (_, _) => set(box.SelectedItem is string s && s != "(none)" ? s : string.Empty);
        return box;
    }

    private static FrameworkElement Row(string label, FrameworkElement field)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(new TextBlock { Text = label + ":", Width = 180, VerticalAlignment = VerticalAlignment.Center });
        row.Children.Add(field);
        return row;
    }

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.75, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 660, HorizontalAlignment = HorizontalAlignment.Left };
}
