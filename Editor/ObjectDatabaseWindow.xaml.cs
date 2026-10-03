using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

public partial class ObjectDatabaseWindow : Window
{
    public ObjectDatabase Database { get; private set; } = new();
    public string? DatabasePath { get; private set; }

    private WorldObject? _current;
    private bool _suppressFieldEvents;

    public ObjectDatabaseWindow()
    {
        InitializeComponent();
        UseAsCombo.ItemsSource = Enum.GetValues<ObjectUse>();
        SolidSize.SizeEdited += (_, _) =>
        {
            if (_current is null) return;
            _current.SizeX = SolidSize.SizeX;
            _current.SizeY = SolidSize.SizeY;
            _current.SizeZ = SolidSize.SizeZ;
            _current.SolidOffsetX = SolidSize.OffsetX;
            _current.SolidOffsetY = SolidSize.OffsetY;
        };
        RefreshList();
    }

    private void RefreshList()
    {
        ObjectListBox.ItemsSource = null;
        ObjectListBox.ItemsSource = Database.Objects;
    }

    private void NewObjectButton_Click(object sender, RoutedEventArgs e)
    {
        var obj = Database.CreateNew();
        RefreshList();
        ObjectListBox.SelectedItem = obj;
    }

    private void DeleteObjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;
        Database.Objects.Remove(_current);
        _current = null;
        RefreshList();
        EditorPanel.IsEnabled = false;
    }

    private void ObjectListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _current = ObjectListBox.SelectedItem as WorldObject;
        if (_current is null)
        {
            EditorPanel.IsEnabled = false;
            return;
        }

        _suppressFieldEvents = true;

        NameBox.Text = _current.Name;
        DescriptionBox.Text = _current.Description;
        IconPathBox.Text = _current.IconPath;
        LoadIconPreview(_current.IconPath);

        PortableCheck.IsChecked = _current.IsPortable;
        StackableCheck.IsChecked = _current.IsStackable;
        MaxStackBox.Text = _current.MaxStackSize.ToString();
        VolumeBox.Text = _current.Volume.ToString();
        ContainerCheck.IsChecked = _current.IsContainer;
        InteractableCheck.IsChecked = _current.IsInteractable;
        OccludesCheck.IsChecked = _current.Occludes;
        IsSolidCheck.IsChecked = _current.IsSolid;
        SolidSize.Load(SolidSizeEditor.LoadImageFile(_current.IconPath), _current.SizeX, _current.SizeY, _current.SizeZ, _current.SolidOffsetX, _current.SolidOffsetY, _current.IsSolid);
        IsTwoHandedCheck.IsChecked = _current.IsTwoHanded;
        NpcCheck.IsChecked = _current.IsNpc;
        UseAsCombo.SelectedItem = _current.UseAs;

        _suppressFieldEvents = false;
        EditorPanel.IsEnabled = true;
    }

    private void LoadIconPreview(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            IconPreview.Source = null;
            return;
        }

        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        bmp.UriSource = new Uri(path, UriKind.Absolute);
        bmp.EndInit();
        IconPreview.Source = bmp;
    }

    private void BrowseIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_current is null) return;

        var dialog = new OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.bmp)|*.png;*.jpg;*.jpeg;*.bmp"
        };
        if (dialog.ShowDialog() != true) return;

        _current.IconPath = dialog.FileName;
        IconPathBox.Text = dialog.FileName;
        LoadIconPreview(dialog.FileName);
        SolidSize.SetSprite(SolidSizeEditor.LoadImageFile(dialog.FileName));
        RefreshList();
    }

    private void Field_Changed(object sender, RoutedEventArgs e) => Field_Changed(sender, (RoutedEventArgs?)null);

    private void Field_Changed(object sender, TextChangedEventArgs? e = null)
    {
        if (_suppressFieldEvents || _current is null) return;

        _current.Name = string.IsNullOrWhiteSpace(NameBox.Text) ? _current.Name : NameBox.Text;
        _current.Description = DescriptionBox.Text;
        _current.IsPortable = PortableCheck.IsChecked == true;
        _current.IsStackable = StackableCheck.IsChecked == true;
        _current.IsContainer = ContainerCheck.IsChecked == true;
        _current.IsInteractable = InteractableCheck.IsChecked == true;
        _current.Occludes = OccludesCheck.IsChecked == true;
        _current.IsSolid = IsSolidCheck.IsChecked == true;
        SolidSize.SetSolid(_current.IsSolid);
        _current.IsTwoHanded = IsTwoHandedCheck.IsChecked == true;
        _current.IsNpc = NpcCheck.IsChecked == true;

        if (int.TryParse(MaxStackBox.Text, out int maxStack))
            _current.MaxStackSize = maxStack;
        if (double.TryParse(VolumeBox.Text, out double volume))
            _current.Volume = volume;

        RefreshList();
        ObjectListBox.SelectedItem = _current;
    }

    private void UseAsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressFieldEvents || _current is null || UseAsCombo.SelectedItem is not ObjectUse use) return;
        _current.UseAs = use;
    }

    private void SaveDbButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog { Filter = "Object Database JSON (*.objdb.json)|*.objdb.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;

        ObjectDatabaseSerializer.Save(Database, dialog.FileName);
        DatabasePath = dialog.FileName;
        MessageBox.Show("Object database saved.", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void LoadDbButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "Object Database JSON (*.objdb.json)|*.objdb.json|All files (*.*)|*.*" };
        if (dialog.ShowDialog() != true) return;

        Database = ObjectDatabaseSerializer.Load(dialog.FileName);
        DatabasePath = dialog.FileName;
        _current = null;
        EditorPanel.IsEnabled = false;
        RefreshList();
    }
}
