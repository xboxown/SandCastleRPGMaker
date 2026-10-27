using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Verbose tab: what kind of creature this is (human or not), how it
/// treats doors, and what it says in each situation - its barks, floating
/// over its head in play. Lines start as Ultima VII's own (Exult's text).
/// </summary>
public partial class CharacterEditorWindow
{
    private void BuildVerbosePanel(CharacterDefinition character)
    {
        VerbosePanel.Children.Clear();

        // ---------- Kind ----------
        VerbosePanel.Children.Add(VerboseHeading("Kind"));
        var human = new RadioButton { Content = "Human", GroupName = "Kind" + character.Id, IsChecked = character.IsHuman, Margin = new Thickness(0, 0, 16, 0) };
        var notHuman = new RadioButton { Content = "Not human (an animal, a monster)", GroupName = "Kind" + character.Id, IsChecked = !character.IsHuman };
        human.Checked += (_, _) => { character.IsHuman = true; BuildVerbosePanel(character); };
        notHuman.Checked += (_, _) => { character.IsHuman = false; BuildVerbosePanel(character); };
        var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
        kindRow.Children.Add(human);
        kindRow.Children.Add(notHuman);
        VerbosePanel.Children.Add(kindRow);
        VerbosePanel.Children.Add(VerboseNote("Human (Exult's sentient, can speak): opens doors, sits on chairs, sleeps in beds, and says the lines below. " +
            "Not human: says nothing, never opens a door, sits or takes a bed (a rat doesn't scream \"Help!\" or open doors). A closed door stops it - unless its faction is hostile to the door's, when it breaks locked doors down. " +
            "It has no paperdoll: in the party, its inventory doesn't open (a red X) and it can't be given things - its stats (Z) still show."));
        if (!character.IsHuman) VerbosePanel.Children.Add(BuildDeathDrops(character));

        var breaks = new CheckBox
        {
            Content = "Breaks down closed doors of a side it's hostile to, even unlocked ones",
            IsChecked = character.BreaksClosedDoors, Margin = new Thickness(0, 6, 0, 2)
        };
        breaks.Checked += (_, _) => character.BreaksClosedDoors = true;
        breaks.Unchecked += (_, _) => character.BreaksClosedDoors = false;
        VerbosePanel.Children.Add(breaks);
        VerbosePanel.Children.Add(BuildLocksHouseDoor(character));
        VerbosePanel.Children.Add(VerboseNote("Unticked: a locked door of a side it's hostile to (Faction Editor) is broken down; an unlocked one is opened (human) or stops it (not human). " +
            "Ticked: it breaks closed doors of that side down whether locked or not - a raider, a bear. Doors with a key, magically locked doors and doors that can't be destroyed always stop it (Exult)."));

        // ---------- Lines ----------
        VerbosePanel.Children.Add(VerboseHeading("What it says"));
        VerbosePanel.Children.Add(VerboseNote("One line per row; in play a line is picked from the list for the situation and floats over its head. An empty list: it says nothing then. " +
            "\"Ultima VII's lines\" puts back the original game's text. Only humans speak."));
        foreach (var kind in Enum.GetValues<VerboseKind>())
            VerbosePanel.Children.Add(BuildVerboseLines(character, kind));
    }

    /// <summary>Not human: the objects that may be in its body when it's killed, each with how many and the chance it's there.</summary>
    private UIElement BuildDeathDrops(CharacterDefinition character)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 4) };
        panel.Children.Add(VerboseHeading("When it dies"));
        panel.Children.Add(VerboseNote("What may be found in its body: each object, how many, and the chance (0-100 %) it's there - rolled once as it dies. As many as you like."));
        var objects = (ProjectSession.ObjectsProvider?.Invoke()?.Objects ?? new List<WorldObject>()).OrderBy(o => o.Name).ToList();
        var rows = new StackPanel();
        void Show()
        {
            rows.Children.Clear();
            foreach (var drop in character.DeathDrops.ToList())
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 2) };
                var what = new ComboBox { Width = 200, ItemsSource = objects, DisplayMemberPath = nameof(WorldObject.Name), SelectedItem = objects.FirstOrDefault(o => o.Id == drop.ObjectId) };
                what.SelectionChanged += (_, _) => drop.ObjectId = (what.SelectedItem as WorldObject)?.Id ?? -1;
                row.Children.Add(what);
                row.Children.Add(new TextBlock { Text = "x", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 4, 0) });
                var quantity = new TextBox { Width = 44, Text = drop.Quantity.ToString() };
                quantity.LostFocus += (_, _) => { if (int.TryParse(quantity.Text.Trim(), out int q)) drop.Quantity = Math.Clamp(q, 1, ObjectTypes.MaxQuantity); quantity.Text = drop.Quantity.ToString(); };
                row.Children.Add(quantity);
                var percent = new TextBox { Width = 44, Text = drop.Percent.ToString(), Margin = new Thickness(10, 0, 0, 0) };
                percent.LostFocus += (_, _) => { if (int.TryParse(percent.Text.Trim(), out int p)) drop.Percent = Math.Clamp(p, 0, 100); percent.Text = drop.Percent.ToString(); };
                row.Children.Add(percent);
                row.Children.Add(new TextBlock { Text = "% chance", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 0, 0) });
                var remove = new Button { Content = "✕", Width = 22, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Remove it" };
                remove.Click += (_, _) => { character.DeathDrops.Remove(drop); Show(); };
                row.Children.Add(remove);
                rows.Children.Add(row);
            }
        }
        Show();
        panel.Children.Add(rows);
        var add = new Button { Content = "Add object", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 1, 8, 1), Margin = new Thickness(0, 4, 0, 0) };
        add.Click += (_, _) => { character.DeathDrops.Add(new DeathDrop { ObjectId = objects.FirstOrDefault()?.Id ?? -1 }); Show(); };
        panel.Children.Add(add);
        return panel;
    }

    private UIElement BuildVerboseLines(CharacterDefinition character, VerboseKind kind)
    {
        var box = new TextBox
        {
            AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, Height = 76, Width = 420, HorizontalAlignment = HorizontalAlignment.Left,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Text = string.Join(Environment.NewLine, character.Verbose.Get(kind))
        };
        box.TextChanged += (_, _) => // saved as you type, so closing the window never loses an edit
            character.Verbose.Lines[kind] = box.Text.Split('\n').Select(l => l.Trim('\r', ' ')).Where(l => l.Length > 0).ToList();
        var reset = new Button { Content = "Ultima VII's lines", Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
        reset.Click += (_, _) =>
        {
            character.Verbose.Lines.Remove(kind);
            box.Text = string.Join(Environment.NewLine, VerboseLines.Defaults(kind));
        };
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(box);
        row.Children.Add(reset);
        var panel = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        panel.Children.Add(new TextBlock { Text = VerboseLines.Title(kind), FontWeight = FontWeights.SemiBold });
        panel.Children.Add(VerboseNote(VerboseLines.When(kind)));
        panel.Children.Add(row);
        return panel;
    }

    private static TextBlock VerboseHeading(string text) => new() { Text = text, FontWeight = FontWeights.Bold, FontSize = 14, Margin = new Thickness(0, 10, 0, 2) };

    private static TextBlock VerboseNote(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 2) };
}
