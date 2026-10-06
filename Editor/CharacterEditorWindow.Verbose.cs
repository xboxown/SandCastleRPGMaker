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
        human.Checked += (_, _) => character.IsHuman = true;
        notHuman.Checked += (_, _) => character.IsHuman = false;
        var kindRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 4) };
        kindRow.Children.Add(human);
        kindRow.Children.Add(notHuman);
        VerbosePanel.Children.Add(kindRow);
        VerbosePanel.Children.Add(VerboseNote("Human (Exult's sentient, can speak): opens doors, sits on chairs, sleeps in beds, and says the lines below. " +
            "Not human: says nothing, never opens a door, sits or takes a bed (a rat doesn't scream \"Help!\" or open doors). A closed door stops it - unless its faction is hostile to the door's, when it breaks locked doors down."));

        var breaks = new CheckBox
        {
            Content = "Breaks down closed doors of a side it's hostile to, even unlocked ones",
            IsChecked = character.BreaksClosedDoors, Margin = new Thickness(0, 6, 0, 2)
        };
        breaks.Checked += (_, _) => character.BreaksClosedDoors = true;
        breaks.Unchecked += (_, _) => character.BreaksClosedDoors = false;
        VerbosePanel.Children.Add(breaks);
        VerbosePanel.Children.Add(VerboseNote("Unticked: a locked door of a side it's hostile to (Faction Editor) is broken down; an unlocked one is opened (human) or stops it (not human). " +
            "Ticked: it breaks closed doors of that side down whether locked or not - a raider, a bear. Doors with a key, magically locked doors and doors that can't be destroyed always stop it (Exult)."));

        // ---------- Lines ----------
        VerbosePanel.Children.Add(VerboseHeading("What it says"));
        VerbosePanel.Children.Add(VerboseNote("One line per row; in play a line is picked from the list for the situation and floats over its head. An empty list: it says nothing then. " +
            "\"Ultima VII's lines\" puts back the original game's text. Only humans speak."));
        foreach (var kind in Enum.GetValues<VerboseKind>())
            VerbosePanel.Children.Add(BuildVerboseLines(character, kind));
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
