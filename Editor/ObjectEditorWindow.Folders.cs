using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Object Editor's list as folders: New / Rename / Delete Folder, drag
/// an object onto a folder to put it there (or onto the empty space below
/// to take it out), or right-click it > Move to folder. Only for finding
/// things - an object works the same in any folder. The Map Editor's
/// Objects tab shows the same folders.
/// </summary>
public partial class ObjectEditorWindow
{
    private readonly TreeView _list = new() { Margin = new Thickness(6), AllowDrop = true, Background = System.Windows.Media.Brushes.Transparent };
    private bool _refreshingTree;
    private ObjectFolder? _selectedFolder;
    private readonly HashSet<int> _expandedFolders = new();
    private Point? _dragStart;

    private void InitFolders(ToolBar toolbar)
    {
        var newFolder = new Button { Content = "New Folder", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(2) };
        newFolder.Click += (_, _) =>
        {
            var name = SimpleInputWindow.Show(this, "Folder name:", "New Folder", "New Folder");
            if (string.IsNullOrWhiteSpace(name)) return;
            var folder = new ObjectFolder { Id = _db.NextFolderId(), Name = name.Trim() };
            _db.Folders.Add(folder);
            _expandedFolders.Add(folder.Id);
            RefreshList();
        };
        var rename = new Button { Content = "Rename Folder", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(2) };
        rename.Click += (_, _) => RenameFolder(_selectedFolder);
        var delete = new Button { Content = "Delete Folder", Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(2) };
        delete.Click += (_, _) => DeleteFolder(_selectedFolder);
        toolbar.Items.Insert(2, new Separator());
        toolbar.Items.Insert(3, newFolder);
        toolbar.Items.Insert(4, rename);
        toolbar.Items.Insert(5, delete);

        _list.SelectedItemChanged += (_, _) =>
        {
            if (_refreshingTree) return;
            switch ((_list.SelectedItem as TreeViewItem)?.Tag)
            {
                case WorldObject obj:
                    _current = obj;
                    ShowCurrent();
                    break;
                case ObjectFolder folder:
                    _selectedFolder = folder;
                    break;
            }
        };
        // Dropped on the empty space (not on a folder): out of its folder.
        _list.Drop += (_, e) =>
        {
            if (e.Handled || e.Data.GetData(typeof(WorldObject)) is not WorldObject obj) return;
            obj.FolderId = -1;
            RefreshList(obj);
        };
    }

    private void RenameFolder(ObjectFolder? folder)
    {
        if (folder is null) { MessageBox.Show(this, "Click a folder first.", "Rename Folder"); return; }
        var name = SimpleInputWindow.Show(this, "Folder name:", "Rename Folder", folder.Name);
        if (string.IsNullOrWhiteSpace(name)) return;
        folder.Name = name.Trim();
        RefreshList();
    }

    private void DeleteFolder(ObjectFolder? folder)
    {
        if (folder is null) { MessageBox.Show(this, "Click a folder first.", "Delete Folder"); return; }
        int inside = _db.Objects.Count(o => o.FolderId == folder.Id);
        if (inside > 0) // a folder with anything in it is kept (shown purple) - empty it first
        {
            MessageBox.Show(this, $"\"{folder.Name}\" still holds {inside} object{(inside == 1 ? "" : "s")} - move or delete {(inside == 1 ? "it" : "them")} first. Only an empty folder can be deleted.",
                "Delete Folder", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, $"Delete the empty folder \"{folder.Name}\"?", "Delete Folder", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _db.Folders.Remove(folder);
        _selectedFolder = null;
        RefreshList();
    }

    private void RefreshList(WorldObject? select = null)
    {
        select ??= _current;
        int filter = _filter.SelectedIndex;
        bool Matches(WorldObject o) => filter <= 0 || (o.TypeChosen && o.Type == ObjectTypes.All[filter - 1]);
        _refreshingTree = true;
        _list.Items.Clear();
        TreeViewItem? toSelect = null;
        foreach (var folder in _db.Folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var inside = _db.Objects.Where(o => o.FolderId == folder.Id && Matches(o)).ToList();
            if (filter > 0 && inside.Count == 0) continue;
            var node = FolderNode(folder, inside.Count);
            node.IsExpanded = _expandedFolders.Contains(folder.Id) || (select is not null && inside.Contains(select)) || filter > 0;
            foreach (var obj in inside)
            {
                var item = ObjectNode(obj);
                node.Items.Add(item);
                if (obj == select) toSelect = item;
            }
            _list.Items.Add(node);
        }
        foreach (var obj in _db.Objects.Where(o => (o.FolderId < 0 || _db.Folders.All(f => f.Id != o.FolderId)) && Matches(o)))
        {
            var item = ObjectNode(obj);
            _list.Items.Add(item);
            if (obj == select) toSelect = item;
        }
        if (toSelect is not null) toSelect.IsSelected = true;
        _refreshingTree = false;
    }

    private TreeViewItem FolderNode(ObjectFolder folder, int count)
    {
        var node = new TreeViewItem { Header = $"\U0001F4C1 {folder.Name}  ({count})", Tag = folder, AllowDrop = true, FontWeight = FontWeights.SemiBold };
        bool holdsAny = _db.Objects.Any(o => o.FolderId == folder.Id);
        if (holdsAny) // purple: it holds objects, so it can't be deleted until it's empty
        {
            node.Header = new TextBlock { Text = $"\U0001F4C1 {folder.Name}  ({count})", Foreground = System.Windows.Media.Brushes.MediumPurple }; // the folder's name only, not its objects
            node.ToolTip = "Holds objects - it can't be deleted until it's empty.";
        }
        node.Expanded += (_, e) => { if (ReferenceEquals(e.OriginalSource, node)) _expandedFolders.Add(folder.Id); };
        node.Collapsed += (_, e) => { if (ReferenceEquals(e.OriginalSource, node)) _expandedFolders.Remove(folder.Id); };
        node.Drop += (_, e) =>
        {
            if (e.Data.GetData(typeof(WorldObject)) is not WorldObject obj) return;
            obj.FolderId = folder.Id;
            _expandedFolders.Add(folder.Id);
            e.Handled = true;
            RefreshList(obj);
        };
        var menu = new ContextMenu();
        var rename = new MenuItem { Header = "Rename folder..." };
        rename.Click += (_, _) => RenameFolder(folder);
        var delete = new MenuItem { Header = holdsAny ? "Delete folder (empty it first)" : "Delete folder", IsEnabled = !holdsAny };
        delete.Click += (_, _) => DeleteFolder(folder);
        menu.Items.Add(rename);
        menu.Items.Add(delete);
        node.ContextMenu = menu;
        return node;
    }

    private TreeViewItem ObjectNode(WorldObject obj)
    {
        // Its sprite beside its name (animated, no sound) - what it looks like without opening it.
        var header = new StackPanel { Orientation = Orientation.Horizontal };
        header.Children.Add(new SpritePreview { Sprite = ObjectLooks.SpriteFor(obj, ObjectLooks.DefaultState(obj), _sprites), Width = 24, Height = 24, Margin = new Thickness(0, 1, 6, 1) });
        header.Children.Add(new TextBlock { Text = new ListEntry(obj).ToString(), VerticalAlignment = VerticalAlignment.Center });
        var item = new TreeViewItem { Header = header, Tag = obj, FontWeight = FontWeights.Normal };
        item.PreviewMouseLeftButtonDown += (_, e) => _dragStart = e.GetPosition(_list);
        item.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _dragStart is not { } start) return;
            var now = e.GetPosition(_list);
            if (Math.Abs(now.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(now.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
            _dragStart = null;
            DragDrop.DoDragDrop(item, new DataObject(typeof(WorldObject), obj), DragDropEffects.Move);
        };
        var menu = new ContextMenu();
        var move = new MenuItem { Header = "Move to folder" };
        var none = new MenuItem { Header = "(no folder)", IsCheckable = true, IsChecked = obj.FolderId < 0 };
        none.Click += (_, _) => { obj.FolderId = -1; RefreshList(obj); };
        move.Items.Add(none);
        foreach (var folder in _db.Folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var f = folder;
            var to = new MenuItem { Header = f.Name, IsCheckable = true, IsChecked = obj.FolderId == f.Id };
            to.Click += (_, _) => { obj.FolderId = f.Id; _expandedFolders.Add(f.Id); RefreshList(obj); };
            move.Items.Add(to);
        }
        if (_db.Folders.Count == 0) move.Items.Add(new MenuItem { Header = "(make one: New Folder)", IsEnabled = false });
        menu.Items.Add(move);
        item.ContextMenu = menu;
        return item;
    }
}
