using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The "Expand..." beside an editor's sprite list: the folders of that editor's kind (Sprite Editor >
/// folder identity) on the left with how many sprites each holds, their sprites on the right - animated
/// (never their sounds) - and a search box over the names, for when there are thousands of them.
/// Double-click a sprite (or OK) to pick it.
/// </summary>
internal sealed class SpriteBrowserWindow : Window
{
    private sealed record FolderRow(SpriteGroup? Group, string Display);
    private sealed record SpriteRow(SpriteEntry Sprite, string Folder, string Name);

    private readonly List<SpriteGroup> _folders;
    private readonly ListBox _folderList = new() { Margin = new Thickness(0, 0, 6, 0), DisplayMemberPath = nameof(FolderRow.Display) };
    private readonly ListView _spriteList = new() { SelectionMode = SelectionMode.Single };
    private readonly TextBox _search = new() { Margin = new Thickness(0, 0, 0, 6) };
    private readonly TextBlock _count = new() { Opacity = 0.7, Margin = new Thickness(0, 6, 0, 0) };

    public (string Group, string Name)? Picked { get; private set; }

    private SpriteBrowserWindow(SpriteDatabase sprites, SpriteFolderIdentity identity, ObjectType? kind, string? currentGroup, string? currentName)
    {
        Title = "Sprites" + (identity == SpriteFolderIdentity.None ? "" : $" - {IdentityName(identity, kind)} folders");
        Width = 760; Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _folders = sprites.FoldersFor(identity, kind).Where(g => g.Name is not (SpriteDatabase.TilesetGroupName or SpriteDatabase.FontsGroupName)).ToList();

        var rows = new List<FolderRow> { new(null, $"(all of them - {_folders.Sum(f => f.Sprites.Count)})") };
        rows.AddRange(_folders.Select(f => new FolderRow(f, $"{f.Name} ({f.Sprites.Count})")));
        _folderList.ItemsSource = rows;
        _folderList.SelectedItem = rows.FirstOrDefault(r => r.Group?.Name == currentGroup) ?? rows[0];
        _folderList.SelectionChanged += (_, _) => ShowSprites();

        var view = new GridView();
        var look = new FrameworkElementFactory(typeof(SpritePreview));
        look.SetBinding(SpritePreview.SpriteProperty, new Binding(nameof(SpriteRow.Sprite)));
        look.SetValue(WidthProperty, 48.0);
        look.SetValue(HeightProperty, 40.0);
        view.Columns.Add(new GridViewColumn { Header = "Look", Width = 60, CellTemplate = new DataTemplate { VisualTree = look } });
        view.Columns.Add(new GridViewColumn { Header = "Name", Width = 260, DisplayMemberBinding = new Binding(nameof(SpriteRow.Name)) });
        view.Columns.Add(new GridViewColumn { Header = "Folder", Width = 180, DisplayMemberBinding = new Binding(nameof(SpriteRow.Folder)) });
        _spriteList.View = view;
        VirtualizingPanel.SetIsVirtualizing(_spriteList, true);
        VirtualizingPanel.SetVirtualizationMode(_spriteList, VirtualizationMode.Recycling);
        _spriteList.MouseDoubleClick += (_, _) => Pick();

        _search.TextChanged += (_, _) => ShowSprites();
        var searchRow = new DockPanel();
        searchRow.Children.Add(new TextBlock { Text = "Search:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 6) });
        searchRow.Children.Add(_search);

        var ok = new Button { Content = "OK", Width = 80, IsDefault = true, Margin = new Thickness(0, 0, 6, 0) };
        ok.Click += (_, _) => Pick();
        var cancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var grid = new Grid { Margin = new Thickness(10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var folderHead = new TextBlock { Text = "Folders", FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) };
        grid.Children.Add(folderHead);
        Grid.SetColumn(searchRow, 1);
        grid.Children.Add(searchRow);
        Grid.SetRow(_folderList, 1);
        grid.Children.Add(_folderList);
        Grid.SetRow(_spriteList, 1); Grid.SetColumn(_spriteList, 1);
        grid.Children.Add(_spriteList);
        Grid.SetRow(_count, 2); Grid.SetColumn(_count, 1);
        grid.Children.Add(_count);
        Grid.SetRow(buttons, 3); Grid.SetColumnSpan(buttons, 2);
        grid.Children.Add(buttons);
        Content = grid;

        ShowSprites();
        if (_spriteList.ItemsSource is List<SpriteRow> shown && shown.FirstOrDefault(r => r.Name == currentName && (currentGroup is null || r.Folder == currentGroup)) is { } current)
        {
            _spriteList.SelectedItem = current;
            Loaded += (_, _) => _spriteList.ScrollIntoView(current);
        }
        Loaded += (_, _) => _search.Focus();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Down && _search.IsKeyboardFocused) { _spriteList.Focus(); e.Handled = true; } };
    }

    private void ShowSprites()
    {
        string search = _search.Text.Trim();
        var folders = _folderList.SelectedItem is FolderRow { Group: { } one } ? new List<SpriteGroup> { one } : _folders;
        var rows = folders.SelectMany(f => f.Sprites.Select(s => new SpriteRow(s, f.Name, s.Name)))
            .Where(r => search.Length == 0 || r.Name.Contains(search, StringComparison.OrdinalIgnoreCase))
            .ToList();
        _spriteList.ItemsSource = rows;
        _count.Text = $"{rows.Count} sprite(s)";
    }

    private void Pick()
    {
        if (_spriteList.SelectedItem is not SpriteRow row) return;
        Picked = (row.Folder, row.Name);
        DialogResult = true;
    }

    /// <summary>A folder identity as the menus name it.</summary>
    public static string IdentityName(SpriteFolderIdentity identity, ObjectType? kind) => identity switch
    {
        SpriteFolderIdentity.Object => kind is { } k ? $"Object: {ObjectTypes.Name(k)}" : "Object",
        _ => identity.ToString()
    };

    /// <summary>Opens it; the sprite picked (its folder and name), or null.</summary>
    public static (string Group, string Name)? Pick(Window owner, SpriteDatabase sprites, SpriteFolderIdentity identity, ObjectType? kind = null, string? currentGroup = null, string? currentName = null)
    {
        var window = new SpriteBrowserWindow(sprites, identity, kind, currentGroup, currentName) { Owner = owner };
        return window.ShowDialog() == true ? window.Picked : null;
    }
}
