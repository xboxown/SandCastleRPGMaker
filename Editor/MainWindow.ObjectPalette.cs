using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Map Editor's Objects tab: one list, laid out as the Object Editor's
/// (its folders, then the objects in none), with Books and Containers last;
/// a type filter and a search box above it. Animated objects animate here
/// (silently); hover one and hold Shift to hear its frame sounds.
/// </summary>
public partial class MainWindow
{
    private readonly List<SpriteAnimator> _paletteObjectAnimators = new();
    private readonly HashSet<int> _paletteExpandedFolders = new();
    private bool _paletteFilterReady;
    /// <summary>The palette entry under the mouse: its picture, sprite, and its (silent or sounding) animator.</summary>
    private (Image Image, SpriteEntry Sprite, SpriteAnimator Animator, bool Audible)? _paletteHover;
    private DispatcherTimer? _paletteShiftTimer;

    private const string BooksFilter = "Books", ContainersFilter = "Containers";

    private void RefreshObjectPalette()
    {
        if (!_paletteFilterReady)
        {
            _paletteFilterReady = true;
            var choices = new List<string> { "All types" };
            choices.AddRange(ObjectTypes.All.Select(ObjectTypes.Name));
            choices.Add(BooksFilter);
            choices.Add(ContainersFilter);
            PaletteTypeFilter.ItemsSource = choices;
            PaletteTypeFilter.SelectedIndex = 0;
            PaletteTypeFilter.SelectionChanged += (_, _) => RefreshObjectPalette();
            PaletteSearchBox.TextChanged += (_, _) => RefreshObjectPalette();
            ObjectPaletteTree.SelectedItemChanged += (_, _) => PalettePicked((ObjectPaletteTree.SelectedItem as TreeViewItem)?.Tag);
        }
        foreach (var animator in _paletteObjectAnimators) animator.Stop();
        _paletteObjectAnimators.Clear();
        EndPaletteHover();
        ObjectPaletteTree.Items.Clear();

        string filter = PaletteTypeFilter.SelectedItem as string ?? "All types";
        string search = PaletteSearchBox.Text.Trim();
        bool narrowed = filter != "All types" || search.Length > 0;
        bool NameMatches(string name) => search.Length == 0 || name.Contains(search, StringComparison.CurrentCultureIgnoreCase);
        bool ObjectMatches(WorldObject o) => o.TypeChosen && NameMatches(o.Name)
            && (filter == "All types" || (filter != BooksFilter && filter != ContainersFilter && ObjectTypes.Name(o.Type) == filter));

        foreach (var folder in _objectDatabase.Folders.OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var inside = _objectDatabase.Objects.Where(o => o.FolderId == folder.Id && ObjectMatches(o)).ToList();
            if (inside.Count == 0 && narrowed) continue;
            var node = PaletteFolderNode($"{folder.Name}  ({inside.Count})", narrowed || _paletteExpandedFolders.Contains(folder.Id), folder.Id);
            foreach (var obj in inside) node.Items.Add(PaletteEntryNode(obj.Name, obj, ObjectPaletteSprite(obj), ObjectIcon(obj)));
            ObjectPaletteTree.Items.Add(node);
        }
        foreach (var obj in _objectDatabase.Objects.Where(o => (o.FolderId < 0 || _objectDatabase.Folders.All(f => f.Id != o.FolderId)) && ObjectMatches(o)))
            ObjectPaletteTree.Items.Add(PaletteEntryNode(obj.Name, obj, ObjectPaletteSprite(obj), ObjectIcon(obj)));

        // Books and Containers (Free Placement).
        void AddItems(string title, IEnumerable<FreePaletteEntry> entries, int key)
        {
            if (filter != "All types" && filter != title) return;
            var shown = entries.Where(e => NameMatches(e.Name)).ToList();
            if (shown.Count == 0 && narrowed) return;
            var node = PaletteFolderNode($"{title}  ({shown.Count})", narrowed || _paletteExpandedFolders.Contains(key), key);
            foreach (var entry in shown) node.Items.Add(PaletteEntryNode(entry.Name, entry, PaletteSprite(entry.SpriteGroup, entry.SpriteName), entry.Icon));
            ObjectPaletteTree.Items.Add(node);
        }
        AddItems(BooksFilter, _bookDatabase.Books.Select(b => new FreePaletteEntry { Name = b.Name, Category = ReferencedItemCategory.Book, Id = b.Id, Icon = FirstFrameOf(b.SpriteGroup, b.SpriteName), SpriteGroup = b.SpriteGroup, SpriteName = b.SpriteName }), -2);
        AddItems(ContainersFilter, _containerDatabase.Containers.Where(c => !c.IsBodyContainer)
            .Select(c => new FreePaletteEntry { Name = c.Name, Category = ReferencedItemCategory.Container, Id = c.Id, Icon = FirstFrameOf(c.SpriteGroup, c.SpriteName), SpriteGroup = c.SpriteGroup, SpriteName = c.SpriteName }), -3);

        if (ObjectPaletteTree.Items.Count == 0)
            ObjectPaletteTree.Items.Add(new TreeViewItem { Header = narrowed ? "(nothing matches)" : "(no objects yet - make them in the Object Editor)", IsEnabled = false });
        _selectedObjectId = -1;
    }

    private SpriteEntry? PaletteSprite(string group, string name) =>
        string.IsNullOrEmpty(name) ? null
            : _spriteDatabase.Groups.FirstOrDefault(g => g.Name == group)?.Sprites.FirstOrDefault(s => s.Name == name)
              ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == name);

    /// <summary>The sprite an object shows in the palette (its starting look) - null for a designed staircase (its still picture).</summary>
    private SpriteEntry? ObjectPaletteSprite(WorldObject obj) =>
        StairLook.DesignOf(obj, _objectDatabase) is not null || WallLook.DesignOf(obj, _objectDatabase) is not null ? null : ObjectLooks.SpriteFor(obj, ObjectLooks.DefaultState(obj), _spriteDatabase);

    private TreeViewItem PaletteFolderNode(string title, bool expanded, int key)
    {
        var node = new TreeViewItem { Header = $"\U0001F4C1 {title}", IsExpanded = expanded, FontWeight = FontWeights.SemiBold };
        node.Expanded += (_, e) => { if (ReferenceEquals(e.OriginalSource, node)) _paletteExpandedFolders.Add(key); };
        node.Collapsed += (_, e) => { if (ReferenceEquals(e.OriginalSource, node)) _paletteExpandedFolders.Remove(key); };
        return node;
    }

    private TreeViewItem PaletteEntryNode(string name, object tag, SpriteEntry? sprite, System.Windows.Media.Imaging.BitmapSource? icon)
    {
        var image = new Image { Width = 28, Height = 28, Margin = new Thickness(0, 0, 8, 0), Source = icon, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        SpriteAnimator? animator = null;
        if (sprite is { Frames.Count: > 1 } && !(tag is WorldObject { FramesAreStates: true }))
        {
            animator = new SpriteAnimator(sprite, image, _resourceDatabase, 0); // animated, silently
            _paletteObjectAnimators.Add(animator);
        }
        var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1) };
        header.Children.Add(image);
        header.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center });
        var item = new TreeViewItem { Header = header, Tag = tag, FontWeight = FontWeights.Normal, ToolTip = sprite is not null ? "Hold Shift over it to hear its sounds" : null };
        if (sprite is not null)
        {
            item.MouseEnter += (_, _) => BeginPaletteHover(image, sprite, animator);
            item.MouseLeave += (_, _) => EndPaletteHover();
        }
        return item;
    }

    private void PalettePicked(object? tag)
    {
        switch (tag)
        {
            case WorldObject obj:
                _selectedObjectId = obj.Id;
                _freeItemPick = null;
                if (_editMode is not (EditMode.Object or EditMode.Free)) EnterObjectMode();
                StatusText.Text = $"Placing \"{obj.Name}\" ({ObjectTypes.Name(obj.Type)}) - click the map.";
                break;
            case FreePaletteEntry entry:
                _freeItemPick = (entry.Category, entry.Id);
                _selectedObjectId = -1;
                if (_editMode != EditMode.Free) EnterFreeMode();
                StatusText.Text = $"Placing \"{entry.Name}\" - Free Placement: click the map.";
                break;
        }
    }

    // ---------- Shift + hover: its frame sounds ----------

    private void BeginPaletteHover(Image image, SpriteEntry sprite, SpriteAnimator? silent)
    {
        EndPaletteHover();
        var animator = silent ?? new SpriteAnimator(sprite, image, _resourceDatabase, 0);
        if (silent is null) animator.Stop(); // a still picture: no animation until Shift
        _paletteHover = (image, sprite, animator, false);
        _paletteShiftTimer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _paletteShiftTimer.Tick -= PaletteShiftTick;
        _paletteShiftTimer.Tick += PaletteShiftTick;
        _paletteShiftTimer.Start();
        PaletteShiftTick(null, EventArgs.Empty);
    }

    private void PaletteShiftTick(object? sender, EventArgs e)
    {
        if (_paletteHover is not { } hover) { _paletteShiftTimer?.Stop(); return; }
        bool shift = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        if (shift == hover.Audible) return;
        hover.Animator.Stop();
        // Shift held: the same animation with its sounds; let go: silent again.
        var next = new SpriteAnimator(hover.Sprite, hover.Image, _resourceDatabase, shift ? null : 0);
        int at = _paletteObjectAnimators.IndexOf(hover.Animator);
        if (at >= 0) _paletteObjectAnimators[at] = next; else _paletteObjectAnimators.Add(next);
        _paletteHover = (hover.Image, hover.Sprite, next, shift);
    }

    private void EndPaletteHover()
    {
        _paletteShiftTimer?.Stop();
        if (_paletteHover is { Audible: true } hover)
        {
            hover.Animator.Stop();
            var silent = new SpriteAnimator(hover.Sprite, hover.Image, _resourceDatabase, 0);
            int at = _paletteObjectAnimators.IndexOf(hover.Animator);
            if (at >= 0) _paletteObjectAnimators[at] = silent; else _paletteObjectAnimators.Add(silent);
        }
        _paletteHover = null;
    }
}
