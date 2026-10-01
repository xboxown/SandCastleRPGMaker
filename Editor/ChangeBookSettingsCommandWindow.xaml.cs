using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Change Book Settings, per spec - runtime-only overrides for a target
/// book's own Name/Weight/Volume/Price and behavior checkmarks (including
/// the two new engine-side-only ones, Is Stackable and Allow Elevation).
/// Never touches the book's own saved data, same principle as Change
/// Self/External Book Content: this only ever writes to the shared
/// execution context's own runtime override storage, applied for the
/// rest of the Game Test session and reset the moment it restarts (there
/// is no save-game feature yet for it to persist into). Each setting is
/// independently opt-in via its own "Override" checkbox - a setting left
/// unchecked keeps using the book's real authored value.
/// </summary>
public partial class ChangeBookSettingsCommandWindow : Window
{
    private readonly BookDatabase _books;
    private readonly ObjectDatabase _objects;
    private readonly ContainerDatabase _containers;
    private readonly SpriteDatabase _sprites;
    private BookData? _targetBook;

    public EventCommand Result { get; private set; }

    public ChangeBookSettingsCommandWindow(BookDatabase books, ObjectDatabase objects, ContainerDatabase containers, SpriteDatabase sprites, EventCommand? existing = null)
    {
        InitializeComponent();
        _books = books;
        _objects = objects;
        _containers = containers;
        _sprites = sprites;
        Result = new EventCommand { Type = EventCommandType.ChangeBookSettings };

        if (existing is not null && int.TryParse(existing.Get("TargetBookId"), out int targetId))
        {
            _targetBook = _books.Books.FirstOrDefault(b => b.Id == targetId);
            if (_targetBook is not null) BookNameBox.Text = _targetBook.Name;

            LoadOverride(existing, "Name", OverrideNameCheck, NameValueBox);
            LoadOverride(existing, "Weight", OverrideWeightCheck, WeightValueBox);
            LoadOverride(existing, "Volume", OverrideVolumeCheck, VolumeValueBox);
            LoadOverride(existing, "Price", OverridePriceCheck, PriceValueBox);
            LoadOverrideBool(existing, "Destructible", OverrideDestructibleCheck, DestructibleValueCheck);
            LoadOverrideBool(existing, "CanBeSold", OverrideCanBeSoldCheck, CanBeSoldValueCheck);
            LoadOverrideBool(existing, "CanBeTraded", OverrideCanBeTradedCheck, CanBeTradedValueCheck);
            LoadOverrideBool(existing, "CanBeDropped", OverrideCanBeDroppedCheck, CanBeDroppedValueCheck);
            LoadOverrideBool(existing, "CanBePicked", OverrideCanBePickedCheck, CanBePickedValueCheck);
            LoadOverrideBool(existing, "IsStackable", OverrideIsStackableCheck, IsStackableValueCheck);
            LoadOverrideBool(existing, "AllowElevation", OverrideAllowElevationCheck, AllowElevationValueCheck);
        }
    }

    private static void LoadOverride(EventCommand cmd, string key, CheckBox overrideCheck, TextBox valueBox)
    {
        bool has = cmd.Get("Override_" + key) == "True";
        overrideCheck.IsChecked = has;
        valueBox.IsEnabled = has;
        valueBox.Text = cmd.Get("Value_" + key);
    }

    private static void LoadOverrideBool(EventCommand cmd, string key, CheckBox overrideCheck, CheckBox valueCheck)
    {
        bool has = cmd.Get("Override_" + key) == "True";
        overrideCheck.IsChecked = has;
        valueCheck.IsEnabled = has;
        valueCheck.IsChecked = cmd.Get("Value_" + key) == "True";
    }

    private void OverrideCheck_Changed(object sender, RoutedEventArgs e)
    {
        // Every "Override" checkbox enables/disables its own value control right next to it - a value can't be edited (or accidentally applied) unless its override is actually turned on.
        SyncEnabled(OverrideNameCheck, NameValueBox);
        SyncEnabled(OverrideWeightCheck, WeightValueBox);
        SyncEnabled(OverrideVolumeCheck, VolumeValueBox);
        SyncEnabled(OverridePriceCheck, PriceValueBox);
        SyncEnabled(OverrideDestructibleCheck, DestructibleValueCheck);
        SyncEnabled(OverrideCanBeSoldCheck, CanBeSoldValueCheck);
        SyncEnabled(OverrideCanBeTradedCheck, CanBeTradedValueCheck);
        SyncEnabled(OverrideCanBeDroppedCheck, CanBeDroppedValueCheck);
        SyncEnabled(OverrideCanBePickedCheck, CanBePickedValueCheck);
        SyncEnabled(OverrideIsStackableCheck, IsStackableValueCheck);
        SyncEnabled(OverrideAllowElevationCheck, AllowElevationValueCheck);
    }

    private static void SyncEnabled(CheckBox overrideCheck, Control valueControl) => valueControl.IsEnabled = overrideCheck.IsChecked == true;

    private void PickBook_Click(object sender, RoutedEventArgs e)
    {
        var current = _targetBook is null ? null : new ItemReference { Category = ReferencedItemCategory.Book, ItemId = _targetBook.Id };
        var picker = new SelectItemPickerWindow(_books, _objects, _containers, _sprites, current) { Owner = this };
        if (picker.ShowDialog() != true || picker.Result is null) return;
        if (picker.Result.Category != ReferencedItemCategory.Book)
        {
            MessageBox.Show("Pick a Book specifically - this command changes a book's settings.", "Change Book Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var found = _books.Books.FirstOrDefault(b => b.Id == picker.Result.ItemId);
        if (found is null) return;

        _targetBook = found;
        BookNameBox.Text = found.Name;
    }

    private static void SaveOverride(EventCommand cmd, string key, CheckBox overrideCheck, TextBox valueBox)
    {
        bool has = overrideCheck.IsChecked == true;
        cmd.Parameters["Override_" + key] = has.ToString();
        if (has) cmd.Parameters["Value_" + key] = valueBox.Text;
    }

    private static void SaveOverrideBool(EventCommand cmd, string key, CheckBox overrideCheck, CheckBox valueCheck)
    {
        bool has = overrideCheck.IsChecked == true;
        cmd.Parameters["Override_" + key] = has.ToString();
        if (has) cmd.Parameters["Value_" + key] = (valueCheck.IsChecked == true).ToString();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_targetBook is null)
        {
            MessageBox.Show("Pick a book first.", "Change Book Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool anyOverride = OverrideNameCheck.IsChecked == true || OverrideWeightCheck.IsChecked == true || OverrideVolumeCheck.IsChecked == true
            || OverridePriceCheck.IsChecked == true || OverrideDestructibleCheck.IsChecked == true || OverrideCanBeSoldCheck.IsChecked == true
            || OverrideCanBeTradedCheck.IsChecked == true || OverrideCanBeDroppedCheck.IsChecked == true || OverrideCanBePickedCheck.IsChecked == true
            || OverrideIsStackableCheck.IsChecked == true || OverrideAllowElevationCheck.IsChecked == true;
        if (!anyOverride)
        {
            MessageBox.Show("Check at least one setting to override.", "Change Book Settings", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var command = new EventCommand { Type = EventCommandType.ChangeBookSettings };
        command.Parameters["TargetBookId"] = _targetBook.Id.ToString();
        SaveOverride(command, "Name", OverrideNameCheck, NameValueBox);
        SaveOverride(command, "Weight", OverrideWeightCheck, WeightValueBox);
        SaveOverride(command, "Volume", OverrideVolumeCheck, VolumeValueBox);
        SaveOverride(command, "Price", OverridePriceCheck, PriceValueBox);
        SaveOverrideBool(command, "Destructible", OverrideDestructibleCheck, DestructibleValueCheck);
        SaveOverrideBool(command, "CanBeSold", OverrideCanBeSoldCheck, CanBeSoldValueCheck);
        SaveOverrideBool(command, "CanBeTraded", OverrideCanBeTradedCheck, CanBeTradedValueCheck);
        SaveOverrideBool(command, "CanBeDropped", OverrideCanBeDroppedCheck, CanBeDroppedValueCheck);
        SaveOverrideBool(command, "CanBePicked", OverrideCanBePickedCheck, CanBePickedValueCheck);
        SaveOverrideBool(command, "IsStackable", OverrideIsStackableCheck, IsStackableValueCheck);
        SaveOverrideBool(command, "AllowElevation", OverrideAllowElevationCheck, AllowElevationValueCheck);

        Result = command;
        DialogResult = true;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
