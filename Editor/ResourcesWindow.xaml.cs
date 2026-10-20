using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class ResourcesWindow : Window
{
    public ResourceDatabase Database { get; private set; } = new();

    public ResourcesWindow()
    {
        InitializeComponent();
        Refresh();
    }

    private void Refresh()
    {
        ResourcesListView.ItemsSource = null;
        ResourcesListView.ItemsSource = Database.Entries;
    }

    private void AddEntry(ResourceKind kind, string filter)
    {
        var dialog = new OpenFileDialog { Filter = filter };
        if (dialog.ShowDialog() != true) return;

        var entry = Database.CreateNew(kind);
        entry.Name = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
        entry.FilePath = dialog.FileName;
        Refresh();
    }

    private void AddSprite_Click(object sender, RoutedEventArgs e)
        => AddEntry(ResourceKind.Sprite, "Image files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp");

    private void AddSound_Click(object sender, RoutedEventArgs e)
        => AddEntry(ResourceKind.Sound, "Audio files (*.wav;*.mp3;*.ogg)|*.wav;*.mp3;*.ogg");

    private void AddMusic_Click(object sender, RoutedEventArgs e)
        => AddEntry(ResourceKind.Music, "Audio files (*.wav;*.mp3;*.ogg)|*.wav;*.mp3;*.ogg");

    private void RemoveSelected_Click(object sender, RoutedEventArgs e)
    {
        if (ResourcesListView.SelectedItem is ResourceEntry entry)
        {
            Database.Entries.Remove(entry);
            Refresh();
        }
    }

    private void ResourcesListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (ResourcesListView.SelectedItem is not ResourceEntry entry) return;
        if (entry.Kind != ResourceKind.Sound && entry.Kind != ResourceKind.Music) return; // Sprite resources are managed through the Sprite Editor instead

        var editor = new SoundEditorWindow(entry, Database) { Owner = this };
        editor.ShowDialog();
        Refresh(); // picks up a possible Name edit
    }

    private void OpenInSoundMixer_Click(object sender, RoutedEventArgs e)
    {
        var entries = ResourcesListView.SelectedItems.Cast<ResourceEntry>()
            .Where(r => r.Kind is ResourceKind.Sound or ResourceKind.Music)
            .ToList();
        if (entries.Count == 0) return;

        var mixer = new SoundMixerWindow(Database, entries[0]) { Owner = this };
        foreach (var extra in entries.Skip(1)) mixer.AddSound(extra);
        mixer.Composed += _ => Refresh();
        mixer.Show();
    }

    private void SaveResources_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Resource DB JSON (*.resdb.json)|*.resdb.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        ResourceDatabaseSerializer.Save(Database, dialog.FileName);
    }

    private void LoadResources_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Resource DB JSON (*.resdb.json)|*.resdb.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        Database = ResourceDatabaseSerializer.Load(dialog.FileName);
        Refresh();
    }
}
