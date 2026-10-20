using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>Operates on the SAME shared ResourceDatabase as the generic Resources window - this is just a Sound/Music-filtered view of it with its own Tools-menu entry, not a separate data store.</summary>
public partial class SoundListWindow : Window
{
    public ResourceDatabase Database { get; }

    public SoundListWindow(ResourceDatabase database)
    {
        InitializeComponent();
        Database = database;
        Refresh();
    }

    private void Refresh()
    {
        SoundsListView.ItemsSource = null;
        SoundsListView.ItemsSource = Database.Entries.Where(e => e.Kind is ResourceKind.Sound or ResourceKind.Music).ToList();
    }

    private void AddEntry(ResourceKind kind)
    {
        var dialog = new OpenFileDialog { Filter = "Audio files (*.wav;*.mp3;*.ogg;*.iff;*.8svx)|*.wav;*.mp3;*.ogg;*.iff;*.8svx|All files|*.*" };
        if (dialog.ShowDialog() != true) return;

        var entry = Database.CreateNew(kind);
        entry.Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        entry.FilePath = dialog.FileName;
        Refresh();
    }

    private void AddSound_Click(object sender, RoutedEventArgs e) => AddEntry(ResourceKind.Sound);
    private void AddMusic_Click(object sender, RoutedEventArgs e) => AddEntry(ResourceKind.Music);

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (SoundsListView.SelectedItem is ResourceEntry entry)
        {
            Database.Entries.Remove(entry);
            Refresh();
        }
    }

    private void SoundsListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SoundsListView.SelectedItem is not ResourceEntry entry) return;
        var editor = new SoundEditorWindow(entry, Database) { Owner = this };
        editor.ShowDialog();
        Refresh();
    }

    private void OpenMixer_Click(object sender, RoutedEventArgs e)
    {
        var mixer = new SoundMixerWindow(Database) { Owner = this };
        mixer.Composed += _ => Refresh();
        mixer.Show();
    }

    private void OpenInSoundMixer_Click(object sender, RoutedEventArgs e)
    {
        var entries = SoundsListView.SelectedItems.Cast<ResourceEntry>().ToList();
        if (entries.Count == 0) return;

        var mixer = new SoundMixerWindow(Database, entries[0]) { Owner = this };
        foreach (var extra in entries.Skip(1)) mixer.AddSound(extra);
        mixer.Composed += _ => Refresh();
        mixer.Show();
    }
}
