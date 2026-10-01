using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class FactionEditorWindow : Window
{
    public FactionDatabase Database { get; private set; } = new();

    private Faction? _current;
    private bool _suppress;

    public FactionEditorWindow(FactionDatabase? existing = null)
    {
        InitializeComponent();
        Title += $" [{BuildInfo.CurrentBuild}]";
        if (existing is not null) Database = existing;
        Refresh();
    }

    private void Refresh()
    {
        FactionListBox.ItemsSource = null;
        FactionListBox.ItemsSource = Database.Factions;
    }

    private void NewFaction_Click(object sender, RoutedEventArgs e)
    {
        var f = Database.CreateNew();
        Refresh();
        FactionListBox.SelectedItem = f;
    }

    private void DeleteFaction_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        Database.Factions.Remove(_current);
        _current = null;
        Refresh();
        EditorPanel.IsEnabled = false;
    }

    private void FactionListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _current = FactionListBox.SelectedItem as Faction;
        if (_current is null) { EditorPanel.IsEnabled = false; return; }

        _suppress = true;
        NameBox.Text = _current.Name;
        NotesBox.Text = _current.Notes;
        _suppress = false;
        EditorPanel.IsEnabled = true;
    }

    private void Field_Changed(object sender, RoutedEventArgs e)
    {
        if (_suppress || _current is null) return;
        _current.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _current.Name : NameBox.Text;
        _current.Notes = NotesBox.Text;
        Refresh();
        FactionListBox.SelectedItem = _current;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Faction DB JSON (*.factiondb.json)|*.factiondb.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        FactionDatabaseSerializer.Save(Database, dialog.FileName);
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Faction DB JSON (*.factiondb.json)|*.factiondb.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        Database = FactionDatabaseSerializer.Load(dialog.FileName);
        _current = null;
        EditorPanel.IsEnabled = false;
        Refresh();
    }
}
