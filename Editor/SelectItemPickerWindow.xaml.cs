using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class SelectItemPickerWindow : Window
{
    private readonly BookDatabase _books;
    private readonly ObjectDatabase _objects;
    private readonly ContainerDatabase _containers;
    private readonly SpriteDatabase _sprites;
    private readonly List<SpriteAnimator> _activeAnimators = new();

    public ItemReference? Result { get; private set; }

    public SelectItemPickerWindow(BookDatabase books, ObjectDatabase objects, ContainerDatabase containers, SpriteDatabase sprites, ItemReference? current)
    {
        InitializeComponent();
        _books = books;
        _objects = objects;
        _containers = containers;
        _sprites = sprites;

        BuildEmptyCategoryTab(WeaponsTabContent, "No Weapon Editor built yet.");
        BuildEmptyCategoryTab(ArmorsTabContent, "No Armor Editor built yet.");
        BuildEmptyCategoryTab(ItemsTabContent, "No Item Editor built yet.");

        // Books reference a real animated SpriteEntry (Frames/FPS/Loop) - show it animating, same as the Preview window's paper doll and world icon, instead of a frozen single frame.
        BuildAnimatedCategoryTab(BooksTabContent, _books.Books,
            b => b.Id, b => b.Name,
            b => $"Name: {b.Name}\nWeight: {b.Weight}\nVolume: {b.Volume}\nPrice: {b.Price}\nDestructible: {b.Destructible}",
            b => ResolveSpriteEntry(b.SpriteGroup, b.SpriteName),
            ReferencedItemCategory.Book);

        // Objects have their own Sprite Editor look now (SpriteGroup/SpriteName), animated like a book's; older objects without one still show their flat IconPath.
        BuildAnimatedCategoryTab(ObjectsTabContent, _objects.Objects,
            o => o.Id, o => o.Name,
            o => $"Name: {o.Name}\nPortable: {o.IsPortable}\nStackable: {o.IsStackable}\nContainer: {o.IsContainer}\nInteractable: {o.IsInteractable}",
            o => string.IsNullOrEmpty(o.SpriteName) ? null : ResolveSpriteEntry(o.SpriteGroup, o.SpriteName),
            ReferencedItemCategory.Object,
            o => string.IsNullOrEmpty(o.IconPath) ? null : o.IconPath);

        // Containers reference a real animated SpriteEntry, exactly the same shape as Book's own world sprite - same real preview treatment, not the static-picture path Objects use.
        BuildAnimatedCategoryTab(ContainersTabContent, _containers.Containers,
            c => c.Id, c => c.Name,
            c => $"Name: {c.Name}\nBody Container: {c.IsBodyContainer}\nMax Volume: {c.MaxVolume}\nLocked: {c.IsLocked}",
            c => ResolveSpriteEntry(c.SpriteGroup, c.SpriteName),
            ReferencedItemCategory.Container);

        if (current is not null)
        {
            Result = current;
            int tabIndex = current.Category switch
            {
                ReferencedItemCategory.Book => 3,
                ReferencedItemCategory.Object => 4,
                ReferencedItemCategory.Container => 5,
                _ => 0
            };
            CategoryTabs.SelectedIndex = tabIndex;
        }

        Closing += (_, _) =>
        {
            foreach (var animator in _activeAnimators) animator.Stop();
        };
    }

    private SpriteEntry? ResolveSpriteEntry(string group, string name)
    {
        return _sprites.Groups.FirstOrDefault(g => g.Name == group)?.Sprites.FirstOrDefault(s => s.Name == name);
    }

    private void BuildEmptyCategoryTab(Grid container, string message)
    {
        container.Children.Add(new TextBlock
        {
            Text = message,
            FontStyle = FontStyles.Italic,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
    }

    private void BuildCategoryTab<T>(Grid container, IEnumerable<T> entries,
        Func<T, int> getId, Func<T, string> getName, Func<T, string> buildInfoText, Func<T, string?> getImagePath,
        ReferencedItemCategory category)
    {
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        var list = entries.ToList();
        var listBox = new ListBox { Margin = new Thickness(0, 8, 8, 0) };
        listBox.ItemsSource = list.Select(getName).ToList();
        Grid.SetColumn(listBox, 0);

        var infoImage = new Image { Width = 96, Height = 96, Stretch = Stretch.Uniform, Margin = new Thickness(0, 8, 0, 8) };
        var infoText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var infoPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        infoPanel.Children.Add(infoImage);
        infoPanel.Children.Add(infoText);
        Grid.SetColumn(infoPanel, 1);

        listBox.SelectionChanged += (_, _) =>
        {
            int index = listBox.SelectedIndex;
            if (index < 0 || index >= list.Count)
            {
                infoImage.Source = null;
                infoText.Text = string.Empty;
                return;
            }

            var entry = list[index];
            infoText.Text = buildInfoText(entry);

            string? imagePath = getImagePath(entry);
            infoImage.Source = imagePath is not null && File.Exists(imagePath) ? LoadBitmap(imagePath) : null;

            Result = new ItemReference { Category = category, ItemId = getId(entry), DisplayName = getName(entry) };
        };

        // Pre-select if this tab matches the current reference.
        if (Result?.Category == category)
        {
            int preselect = list.FindIndex(e => getId(e) == Result.ItemId);
            if (preselect >= 0) listBox.SelectedIndex = preselect;
        }

        container.Children.Add(listBox);
        container.Children.Add(infoPanel);
    }

    /// <summary>Same shape as BuildCategoryTab, but for entries with a real animated SpriteEntry (Books) instead of a flat static-image path (Objects) - the preview shows real Frames/FPS/Loop animation, same as everywhere else that's been upgraded to this.</summary>
    private void BuildAnimatedCategoryTab<T>(Grid container, IEnumerable<T> entries,
        Func<T, int> getId, Func<T, string> getName, Func<T, string> buildInfoText, Func<T, SpriteEntry?> getSprite,
        ReferencedItemCategory category, Func<T, string?>? getFallbackImagePath = null)
    {
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        container.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });

        var list = entries.ToList();
        var listBox = new ListBox { Margin = new Thickness(0, 8, 8, 0) };
        listBox.ItemsSource = list.Select(getName).ToList();
        Grid.SetColumn(listBox, 0);

        var infoImage = new Image { Width = 96, Height = 96, Stretch = Stretch.Uniform, Margin = new Thickness(0, 8, 0, 8) };
        var infoText = new TextBlock { TextWrapping = TextWrapping.Wrap };
        var infoPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        infoPanel.Children.Add(infoImage);
        infoPanel.Children.Add(infoText);
        Grid.SetColumn(infoPanel, 1);

        SpriteAnimator? currentAnimator = null;

        listBox.SelectionChanged += (_, _) =>
        {
            currentAnimator?.Stop();
            currentAnimator = null;
            infoImage.Source = null;

            int index = listBox.SelectedIndex;
            if (index < 0 || index >= list.Count)
            {
                infoText.Text = string.Empty;
                return;
            }

            var entry = list[index];
            infoText.Text = buildInfoText(entry);

            var sprite = getSprite(entry);
            if (sprite is not null)
            {
                currentAnimator = new SpriteAnimator(sprite, infoImage);
                _activeAnimators.Add(currentAnimator);
            }
            else if (getFallbackImagePath?.Invoke(entry) is { } imagePath && File.Exists(imagePath))
                infoImage.Source = LoadBitmap(imagePath); // older objects without a sprite

            Result = new ItemReference { Category = category, ItemId = getId(entry), DisplayName = getName(entry) };
        };

        if (Result?.Category == category)
        {
            int preselect = list.FindIndex(e => getId(e) == Result.ItemId);
            if (preselect >= 0) listBox.SelectedIndex = preselect;
        }

        container.Children.Add(listBox);
        container.Children.Add(infoPanel);
    }

    private static BitmapImage? LoadBitmap(string path)
    {
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.EndInit();
            return bmp;
        }
        catch { return null; }
    }

    private void CategoryTabs_SelectionChanged(object sender, SelectionChangedEventArgs e) { }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (Result is null)
        {
            MessageBox.Show("Select an item first.", "Select Item", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }
}
