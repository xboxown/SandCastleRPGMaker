using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// After a faction in use is deleted: the characters that lost it. Pick one
/// (Edit or double-click) to open it in the Character Editor and choose a
/// new faction; with several selected, the faction chosen for the one
/// edited is offered to all of them. Each disappears from the list as soon
/// as it has a faction again.
/// </summary>
public class FactionlessCharactersWindow : Window
{
    private readonly List<CharacterDefinition> _remaining;
    private readonly ListBox _list;

    public FactionlessCharactersWindow(IEnumerable<CharacterDefinition> characters)
    {
        _remaining = characters.ToList();
        Title = $"Characters Without a Faction [{BuildInfo.CurrentBuild}]";
        Width = 420;
        Height = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var root = new DockPanel { Margin = new Thickness(10) };
        var intro = new TextBlock
        {
            Text = "These characters no longer belong to a faction. Select one and press Edit (or double-click it) to choose a new faction in the Character Editor. " +
                   "Ctrl/Shift-click to select several: the faction you give the one you edit can then be given to all of them.",
            TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(intro, Dock.Top);
        root.Children.Add(intro);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        var edit = new Button { Content = "Edit...", Width = 80, Margin = new Thickness(0, 0, 6, 0), IsDefault = true };
        edit.Click += (_, _) => EditSelected();
        var close = new Button { Content = "Close", Width = 80, IsCancel = true };
        close.Click += (_, _) => Close();
        buttons.Children.Add(edit);
        buttons.Children.Add(close);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);

        _list = new ListBox { SelectionMode = SelectionMode.Extended, DisplayMemberPath = nameof(CharacterDefinition.Name) };
        _list.MouseDoubleClick += (_, e) =>
        {
            if (e.OriginalSource is FrameworkElement { DataContext: CharacterDefinition }) EditSelected();
        };
        _list.KeyDown += (_, e) => { if (e.Key == Key.Enter) { EditSelected(); e.Handled = true; } };
        root.Children.Add(_list);

        Content = root;
        Refresh();
        if (_remaining.Count > 0) _list.SelectedIndex = 0;
    }

    private void Refresh()
    {
        _list.ItemsSource = null;
        _list.ItemsSource = _remaining;
    }

    private void EditSelected()
    {
        var selected = _list.SelectedItems.Cast<CharacterDefinition>().ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "Select a character first.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var edited = _list.SelectedItem as CharacterDefinition ?? selected[0];
        ProjectSession.OpenCharacterEditor(this, edited.Id);

        var others = selected.Where(c => c != edited && c.FactionId < 0).ToList();
        if (edited.FactionId >= 0 && others.Count > 0)
        {
            string factionName = ProjectSession.Factions.Find(edited.FactionId)?.Name ?? "that faction";
            var answer = MessageBox.Show(this,
                $"Give \"{factionName}\" to the other {others.Count} selected character{(others.Count == 1 ? "" : "s")} as well?\n\n" +
                string.Join(", ", others.Take(10).Select(c => c.Name)) + (others.Count > 10 ? ", ..." : ""),
                "Apply to All Selected", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer == MessageBoxResult.Yes)
                foreach (var character in others) character.FactionId = edited.FactionId;
        }

        _remaining.RemoveAll(c => c.FactionId >= 0 || !ProjectSession.Characters.Characters.Contains(c));
        Refresh();
        if (_remaining.Count == 0)
        {
            MessageBox.Show(this, "Every character has a faction again.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
    }
}
