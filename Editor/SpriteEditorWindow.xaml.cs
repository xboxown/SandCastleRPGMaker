using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
        Database.EnsureBuiltInGroups();
        RefreshGroups();
        // Ctrl+X / C / V and Del on the sprite list, as its right-click menu.
        SpriteListView.KeyDown += (_, e) =>
        {
            bool ctrl = System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control);
            var selected = SelectedSprites();
            bool locked = _currentGroup is not null && selected.Any(s => SpriteDatabase.IsFontSprite(_currentGroup.Name, s.Name));
            if (ctrl && e.Key == System.Windows.Input.Key.X && !locked) { ToClipboard(selected, cutting: true); e.Handled = true; }
            else if (ctrl && e.Key == System.Windows.Input.Key.C) { ToClipboard(selected, cutting: false); e.Handled = true; }
            else if (ctrl && e.Key == System.Windows.Input.Key.V) { Paste(); e.Handled = true; }
            else if (e.Key == System.Windows.Input.Key.Delete && !locked) { DeleteSprites(selected); e.Handled = true; }
        };
    }

    /// <summary>The folders: each with how many sprites it holds and its identity's badge - red the built-in ones, orange one with an identity, purple one holding a single sprite.</summary>
    private void RefreshGroups()
    {
        var keep = _currentGroup;
        GroupListBox.SelectionChanged -= GroupListBox_SelectionChanged;
        GroupListBox.Items.Clear();
        foreach (var group in Database.Groups)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            if (Badge(group) is { } badge) row.Children.Add(badge);
            var text = new TextBlock { Text = $"{group.Name} ({group.Sprites.Count})" };
            if (IsProtected(group.Name)) { text.Foreground = Brushes.Red; text.FontWeight = FontWeights.Bold; }
            else if (group.Identity != SpriteFolderIdentity.None) { text.Foreground = Brushes.DarkOrange; text.FontWeight = FontWeights.SemiBold; }
            else if (group.Sprites.Count == 1) text.Foreground = Brushes.Purple;
            row.Children.Add(text);
            var item = new ListBoxItem { Content = row, Tag = group, ToolTip = group.Identity == SpriteFolderIdentity.None ? null : $"For: {SpriteBrowserWindow.IdentityName(group.Identity, group.ObjectKind)}" };
            item.MouseRightButtonUp += (_, e) => { GroupListBox.SelectedItem = item; ShowFolderMenu(group, item); e.Handled = true; };
            GroupListBox.Items.Add(item);
        }
        GroupListBox.SelectionChanged += GroupListBox_SelectionChanged;
        GroupListBox.SelectedItem = GroupListBox.Items.Cast<ListBoxItem>().FirstOrDefault(i => i.Tag == keep);
        if (GroupListBox.SelectedItem is null) _currentGroup = null;
    }

    /// <summary>The small badge to the left of a folder with an identity: a face for characters, the type's first letters for an object type.</summary>
    private static FrameworkElement? Badge(SpriteGroup group)
    {
        string? text = group.Identity switch
        {
            SpriteFolderIdentity.Object => group.ObjectKind is { } kind ? ObjectTypes.Name(kind)[..Math.Min(2, ObjectTypes.Name(kind).Length)] : "Ob",
            SpriteFolderIdentity.Container => "Co",
            SpriteFolderIdentity.Book => "Bk",
            SpriteFolderIdentity.Character => "\u263A", // a face
            SpriteFolderIdentity.House => "\u2302", // a house
            SpriteFolderIdentity.Vehicle => "Ve",
            SpriteFolderIdentity.Paperdoll => "Pd",
            _ => null
        };
        if (text is null) return null;
        return new Border
        {
            Background = Brushes.DarkOrange, CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center, MinWidth = 20,
            Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center }
        };
    }

    /// <summary>The folders the engine and editors rely on by name: never renamed, deleted or given an identity.</summary>
    private static bool IsProtected(string name) =>
        name is SpriteDatabase.TilesetGroupName or SpriteDatabase.FontsGroupName or SpriteDatabase.BloodGroupName or SpriteDatabase.KeychainGroupName or SpriteDatabase.DialogFrameGroupName
            or SpriteDatabase.WeaponInHandGroupName or SpriteDatabase.JoinedMonstersGroupName or SpriteDatabase.TrailGroupName
        || SpriteDatabase.IsBundleGroup(name);

    /// <summary>Right-click a folder: what it's for (its identity), rename, paste, delete.</summary>
    private void ShowFolderMenu(SpriteGroup group, FrameworkElement target)
    {
        var menu = new ContextMenu { PlacementTarget = target };
        if (!IsProtected(group.Name))
        {
            var identity = new MenuItem { Header = "Identity (which editors list it)" };
            MenuItem Choice(string header, SpriteFolderIdentity id, ObjectType? kind)
            {
                var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = group.Identity == id && group.ObjectKind == kind };
                item.Click += (_, _) => { group.Identity = id; group.ObjectKind = kind; RefreshGroups(); };
                return item;
            }
            identity.Items.Add(Choice("None (listed everywhere)", SpriteFolderIdentity.None, null));
            var objects = new MenuItem { Header = "Object type" };
            objects.Items.Add(Choice("Any object", SpriteFolderIdentity.Object, null));
            foreach (var type in ObjectTypes.All) objects.Items.Add(Choice(ObjectTypes.Name(type), SpriteFolderIdentity.Object, type));
            identity.Items.Add(objects);
            identity.Items.Add(Choice("Container", SpriteFolderIdentity.Container, null));
            identity.Items.Add(Choice("Book", SpriteFolderIdentity.Book, null));
            identity.Items.Add(Choice("Character", SpriteFolderIdentity.Character, null));
            identity.Items.Add(Choice("House", SpriteFolderIdentity.House, null));
            identity.Items.Add(Choice("Vehicle", SpriteFolderIdentity.Vehicle, null));
            identity.Items.Add(Choice("Paperdoll", SpriteFolderIdentity.Paperdoll, null));
            menu.Items.Add(identity);
            var rename = new MenuItem { Header = "Rename..." };
            rename.Click += (_, _) => RenameFolder(group);
            menu.Items.Add(rename);
        }
        if (_clipboard.Count > 0)
        {
            var paste = new MenuItem { Header = $"Paste {_clipboard.Count} sprite(s) here" };
            paste.Click += (_, _) => { _currentGroup = group; Paste(); };
            menu.Items.Add(paste);
        }
        var delete = new MenuItem { Header = "Delete folder...", IsEnabled = !IsProtected(group.Name) };
        delete.Click += (_, _) => { _currentGroup = group; DeleteGroup_Click(this, new RoutedEventArgs()); };
        menu.Items.Add(delete);
        menu.IsOpen = true;
    }

    private void RenameFolder(SpriteGroup group)
    {
        var name = SimpleInputWindow.Show(this, "The folder's new name:", "Rename Folder", group.Name)?.Trim();
        if (string.IsNullOrEmpty(name) || name == group.Name) return;
        if (Database.Groups.Any(g => g != group && g.Name == name) || IsProtected(name))
        {
            MessageBox.Show($"There's already a folder called \"{name}\".", "Rename Folder", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string old = group.Name;
        group.Name = name;
        SpriteReferences.RenameFolder(old, name); // everything using its sprites names the new folder
        RefreshGroups();
    }

    // ---------- Sprites: cut, copy, paste, rename, delete ----------

    /// <summary>The sprites cut or copied (kept while the editor is closed and opened again), and whether they were cut.</summary>
    private static readonly List<(SpriteGroup From, SpriteEntry Sprite)> _clipboard = new();
    private static bool _clipboardCut;

    private List<SpriteEntry> SelectedSprites() => SpriteListView.SelectedItems.OfType<SpriteEntry>().ToList();

    private void SpriteListView_MouseRightButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_currentGroup is null) return;
        var selected = SelectedSprites();
        bool any = selected.Count > 0, locked = selected.Any(s => SpriteDatabase.IsFontSprite(_currentGroup.Name, s.Name));
        var menu = new ContextMenu { PlacementTarget = SpriteListView };
        var cut = new MenuItem { Header = "Cut", IsEnabled = any && !locked, InputGestureText = "Ctrl+X" };
        cut.Click += (_, _) => ToClipboard(selected, cutting: true);
        var copy = new MenuItem { Header = "Copy", IsEnabled = any, InputGestureText = "Ctrl+C" };
        copy.Click += (_, _) => ToClipboard(selected, cutting: false);
        var paste = new MenuItem { Header = _clipboard.Count > 0 ? $"Paste ({_clipboard.Count})" : "Paste", IsEnabled = _clipboard.Count > 0, InputGestureText = "Ctrl+V" };
        paste.Click += (_, _) => Paste();
        var rename = new MenuItem { Header = "Rename...", IsEnabled = selected.Count == 1 && !locked };
        rename.Click += (_, _) => RenameSprite(selected[0]);
        var delete = new MenuItem { Header = "Delete...", IsEnabled = any && !locked, InputGestureText = "Del" };
        delete.Click += (_, _) => DeleteSprites(selected);
        menu.Items.Add(cut); menu.Items.Add(copy); menu.Items.Add(paste); menu.Items.Add(new Separator()); menu.Items.Add(rename); menu.Items.Add(delete);
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void ToClipboard(List<SpriteEntry> sprites, bool cutting)
    {
        if (_currentGroup is null || sprites.Count == 0) return;
        _clipboard.Clear();
        _clipboard.AddRange(sprites.Select(s => (_currentGroup, s)));
        _clipboardCut = cutting;
    }

    /// <summary>Into the folder shown: cut ones move (everything using them follows), copied ones are new copies (" (copy)" when the name is taken).</summary>
    private void Paste()
    {
        if (_currentGroup is not { } target || _clipboard.Count == 0) return;
        foreach (var (from, sprite) in _clipboard.ToList())
        {
            if (_clipboardCut)
            {
                if (from == target || !from.Sprites.Contains(sprite)) continue;
                string name = UniqueName(target, sprite.Name);
                from.Sprites.Remove(sprite);
                string oldName = sprite.Name;
                sprite.Name = name;
                target.Sprites.Add(sprite);
                SpriteReferences.Move(from.Name, oldName, target.Name, name);
            }
            else
            {
                var copy = System.Text.Json.JsonSerializer.Deserialize<SpriteEntry>(System.Text.Json.JsonSerializer.Serialize(sprite))!;
                copy.Name = UniqueName(target, sprite.Name);
                target.Sprites.Add(copy);
            }
        }
        if (_clipboardCut) _clipboard.Clear(); // moved: nothing left to paste
        RefreshGroups();
        RefreshSprites();
    }

    private static string UniqueName(SpriteGroup folder, string name)
    {
        if (folder.Sprites.All(s => s.Name != name)) return name;
        for (int n = 1; ; n++)
        {
            string candidate = n == 1 ? $"{name} (copy)" : $"{name} (copy {n})";
            if (folder.Sprites.All(s => s.Name != candidate)) return candidate;
        }
    }

    private void RenameSprite(SpriteEntry sprite)
    {
        if (_currentGroup is not { } folder) return;
        var name = SimpleInputWindow.Show(this, "The sprite's new name:", "Rename Sprite", sprite.Name)?.Trim();
        if (string.IsNullOrEmpty(name) || name == sprite.Name) return;
        if (folder.Sprites.Any(s => s != sprite && s.Name == name))
        {
            MessageBox.Show($"There's already a sprite called \"{name}\" in this folder.", "Rename Sprite", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        string old = sprite.Name;
        sprite.Name = name;
        SpriteReferences.Move(folder.Name, old, folder.Name, name); // everything using it follows the new name
        RefreshSprites();
    }

    private void DeleteSprites(List<SpriteEntry> sprites)
    {
        if (_currentGroup is not { } folder || sprites.Count == 0) return;
        string what = sprites.Count == 1 ? $"the sprite \"{sprites[0].Name}\"" : $"these {sprites.Count} sprites";
        if (MessageBox.Show($"Delete {what} from \"{folder.Name}\"? Anything that uses it loses its look. This can't be undone.", "Delete Sprite",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        foreach (var sprite in sprites) folder.Sprites.Remove(sprite);
        RefreshGroups();
        RefreshSprites();
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

        _currentGroup = Database.GetOrCreateGroup(input);
        RefreshGroups();
        RefreshSprites();
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGroup is null) return;
        if (_currentGroup.Name == SpriteDatabase.TilesetGroupName)
        {
            MessageBox.Show("The Tileset group is hardcoded and can't be deleted - it's how the Map Editor finds every tileset.", "Protected Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_currentGroup.Name == SpriteDatabase.FontsGroupName)
        {
            MessageBox.Show("The Fonts group is hardcoded and can't be deleted - the game writes all its text with the fonts in it.", "Protected Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (SpriteDatabase.IsBundleGroup(_currentGroup.Name))
        {
            MessageBox.Show($"The {_currentGroup.Name} group is hardcoded and can't be deleted - {_currentGroup.Name} objects take their bundle pictures from it.", "Protected Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_currentGroup.Name == SpriteDatabase.BloodGroupName)
        {
            MessageBox.Show("The Blood group is hardcoded and can't be deleted - every blood type characters bleed is a sprite in it.", "Protected Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_currentGroup.Name == SpriteDatabase.KeychainGroupName)
        {
            MessageBox.Show("The Keychain group is hardcoded and can't be deleted - Keychain objects take their look (a frame for each number of keys) from it.", "Protected Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_currentGroup.Name == SpriteDatabase.TrailGroupName)
        {
            MessageBox.Show("The Slime Trail group is hardcoded and can't be deleted - monsters that leave a trail take their puddles from it.", "Protected Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_currentGroup.Name == SpriteDatabase.JoinedMonstersGroupName)
        {
            MessageBox.Show("The Joined Monsters group is hardcoded and can't be deleted - monsters that join up take their joined look from it.", "Protected Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_currentGroup.Name == SpriteDatabase.WeaponInHandGroupName)
        {
            MessageBox.Show("The Weapon in hand group is hardcoded and can't be deleted - weapons take their look held in the hand from it.", "Protected Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (_currentGroup.Name == SpriteDatabase.DialogFrameGroupName)
        {
            MessageBox.Show("The DialogFrame group is hardcoded and can't be deleted - Show Text takes its dialog box frames from it.", "Protected Group", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(_currentGroup.Sprites.Count == 0 ? $"Delete the empty folder \"{_currentGroup.Name}\"?"
                : $"There are {_currentGroup.Sprites.Count} sprite(s) in the folder \"{_currentGroup.Name}\". Are you sure you want to delete it and all of them? Anything that uses them loses its look.",
                "Delete Folder", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        Database.Groups.Remove(_currentGroup);
        _currentGroup = null;
        RefreshGroups();
        RefreshSprites();
    }

    private void GroupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentGroup = (GroupListBox.SelectedItem as ListBoxItem)?.Tag as SpriteGroup;
        RefreshSprites();
    }

    private void NewSprite_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGroup is null)
        {
            MessageBox.Show("Select or create a group first.", "No group selected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Ammo / Money: how bundle pictures work, before the first one is made.
        if (SpriteDatabase.IsBundleGroup(_currentGroup.Name)) new BundleGuideWindow(_currentGroup.Name) { Owner = this }.ShowDialog();
        if (_currentGroup.Name == SpriteDatabase.KeychainGroupName) new FrameStatesGuideWindow(null, FrameStatesGuideWindow.Keychain) { Owner = this }.ShowDialog();
        if (_currentGroup.Name == SpriteDatabase.DialogFrameGroupName) new DialogFrameGuideWindow { Owner = this }.ShowDialog();
        if (_currentGroup.Name == SpriteDatabase.JoinedMonstersGroupName) new JoiningGuideWindow { Owner = this }.ShowDialog();
        if (_currentGroup.Name == SpriteDatabase.TrailGroupName)
            MessageBox.Show(this, "A sprite here is one kind of puddle a monster leaves as it walks, as in Ultima 7.\n\nGive it up to 4 frames, small to large: they are sizes, not an animation. New puddles start at frame 1 or 2 and grow if it drips on the same spot.\n\nThen Character Editor > the monster > Health > Joining > Trail: pick this sprite, and tick whether its puddles hurt.", "Slime Trail", MessageBoxButton.OK, MessageBoxImage.Information);
        if (_currentGroup.Name == SpriteDatabase.WeaponInHandGroupName)
            new FrameStatesGuideWindow(null, FrameStatesGuideWindow.WeaponInHand(ProjectSession.SettingsProvider?.Invoke()?.WeaponInHand ?? new WeaponInHandSettings())) { Owner = this }.ShowDialog();
        var name = SimpleInputWindow.Show(this, "Sprite name:", "New Sprite", "New Sprite");
        if (string.IsNullOrWhiteSpace(name)) return;

        if (_currentGroup.Name == SpriteDatabase.FontsGroupName && _currentGroup.Sprites.Any(s => s.Name == name.Trim()))
        {
            MessageBox.Show($"There's already a font called \"{name.Trim()}\".", "New Font", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        // In the Fonts group a new sprite is an extra font - offered only in the Book Editor's Text Font list.
        // In the Blood group a new sprite is a blood type: puddle sizes, never animated.
        var entry = new SpriteEntry { Name = name.Trim(), Loop = _currentGroup.Name is not (SpriteDatabase.FontsGroupName or SpriteDatabase.BloodGroupName or SpriteDatabase.TrailGroupName or SpriteDatabase.KeychainGroupName or SpriteDatabase.WeaponInHandGroupName) };
        _currentGroup.Sprites.Add(entry);
        RefreshGroups();
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
        RefreshGroups();
        RefreshSprites();
    }

    private void RemoveSprite_Click(object sender, RoutedEventArgs e)
    {
        if (_currentGroup is null || SpriteListView.SelectedItem is not SpriteEntry entry) return;
        if (SpriteDatabase.IsFontSprite(_currentGroup.Name, entry.Name))
        {
            MessageBox.Show($"\"{entry.Name}\" is one of the game's two fonts and can't be removed or renamed - edit it to draw or import its letters.", "Protected Sprite", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DeleteSprites(SelectedSprites());
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
        bool isFont = _currentGroup?.Name == SpriteDatabase.FontsGroupName; // every sprite in Fonts is a font: glyph frames, no sound or animation
        bool isTrail = _currentGroup?.Name == SpriteDatabase.TrailGroupName; // a puddle a monster leaves: size frames, as blood
        bool isBlood = isTrail || _currentGroup?.Name == SpriteDatabase.BloodGroupName; // every sprite in Blood is a blood type: size frames, no sound or animation
        bool isKeychain = _currentGroup?.Name == SpriteDatabase.KeychainGroupName; // frames by how many keys: no sound or animation
        bool isWeaponInHand = _currentGroup?.Name == SpriteDatabase.WeaponInHandGroupName; // a weapon held in the hand: its frames as System Settings says, grip points
        var editor = new SpriteFrameEditorWindow(entry, _resources, isTilesetGroup, isFont, isBlood, isKeychain, isWeaponInHand, isTrail) { Owner = this };
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
        Database.EnsureBuiltInGroups();
        _currentGroup = null;
        RefreshGroups();
        RefreshSprites();
    }
}
