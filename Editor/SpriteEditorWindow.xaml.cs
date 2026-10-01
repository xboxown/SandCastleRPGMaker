using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class SpriteEditorWindow : Window
{
    public SpriteDatabase Database { get; private set; } = new();

    private SpriteGroup? _currentGroup;
    private readonly ResourceDatabase _resources;

    public SpriteEditorWindow(SpriteDatabase? existing = null, ResourceDatabase? resources = null)
    {
        InitializeComponent();
        Title += $" [{BuildInfo.CurrentBuild}]";
        if (existing is not null) Database = existing;
        _resources = resources ?? new ResourceDatabase();
        Database.EnsureTilesetGroup();
        RefreshGroups();
    }

    private void RefreshGroups()
    {
        GroupListBox.ItemsSource = null;
        GroupListBox.ItemsSource = Database.Groups;
    }

    private void RefreshSprites()
    {
        SpriteListView.ItemsSource = null;
        SpriteListView.ItemsSource = _currentGroup?.Sprites;
    }

    private void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var input = SimpleInputWindow.Show(this,
            "Group name (e.g. \"NPCWalkLeft\" to match a convention another editor expects, or anything else for a custom group):",
            "New Sprite Group", "New Group");
        if (string.IsNullOrWhiteSpace(input)) return;

        var group = Database.GetOrCreateGroup(input);
        RefreshGroups();
        GroupListBox.SelectedItem = group;
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGroup is null) return;
        if (_currentGroup.Name == SpriteDatabase.TilesetGroupName)
        {
            MessageBox.Show("The Tileset group is hardcoded and can't be deleted - it's how the Map Editor finds every tileset.", "Protected Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Database.Groups.Remove(_currentGroup);
        _currentGroup = null;
        RefreshGroups();
        RefreshSprites();
    }

    private void GroupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentGroup = GroupListBox.SelectedItem as SpriteGroup;
        RefreshSprites();
    }

    private void NewSprite_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGroup is null)
        {
            MessageBox.Show("Select or create a group first.", "No group selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var name = SimpleInputWindow.Show(this, "Sprite name:", "New Sprite", "New Sprite");
        if (string.IsNullOrWhiteSpace(name)) return;

        var entry = new SpriteEntry { Name = name.Trim() };
        _currentGroup.Sprites.Add(entry);
        RefreshSprites();
        SpriteListView.SelectedItem = entry;
    }

    private void AddSprite_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGroup is null)
        {
            MessageBox.Show("Select or create a group first.", "No group selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new OpenFileDialog { Filter = "Image files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp", Multiselect = true };
        if (dialog.ShowDialog() != true) return;

        foreach (var file in dialog.FileNames)
        {
            _currentGroup.Sprites.Add(new SpriteEntry
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(file),
                FilePath = file,
                Frames = new List<SpriteFrame> { new() { FilePath = file } }
            });
        }
        RefreshSprites();
    }

    private void RemoveSprite_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGroup is null || SpriteListView.SelectedItem is not SpriteEntry entry) return;
        _currentGroup.Sprites.Remove(entry);
        RefreshSprites();
    }

    private void SpriteListView_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        OpenFrameEditor();
    }

    private void EditSprite_Click(object sender, RoutedEventArgs e)
    {
        OpenFrameEditor();
    }

    private void OpenFrameEditor()
    {
        if (SpriteListView.SelectedItem is not SpriteEntry entry)
        {
            MessageBox.Show("Select a sprite first.", "Edit Sprite", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        bool isTilesetGroup = _currentGroup?.Name == SpriteDatabase.TilesetGroupName;
        var editor = new SpriteFrameEditorWindow(entry, _resources, isTilesetGroup) { Owner = this };
        editor.ShowDialog();
        RefreshSprites(); // reflect any name change in the list
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Sprite DB JSON (*.spritedb.json)|*.spritedb.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        SpriteDatabaseSerializer.Save(Database, dialog.FileName);
    }

    private void Load_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Sprite DB JSON (*.spritedb.json)|*.spritedb.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;
        Database = SpriteDatabaseSerializer.Load(dialog.FileName);
        _currentGroup = null;
        RefreshGroups();
        RefreshSprites();
    }
}
