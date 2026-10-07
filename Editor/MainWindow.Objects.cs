using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Map Editor: placing Object Editor objects. Mode > Objects (or pick one in
/// the Objects palette): the palette has a tab per object type, each listing
/// its objects with their picture. Click a cell to place the picked object;
/// keep the button down and move to place one on every cell you pass, like
/// drawing tiles. Press a placed object and drag it to move it. Right-click
/// it for Facing (chairs, beds), its starting state (doors: open / closed /
/// locked; lights: lit), Quantity (ammo, money), Cut, Copy, Paste and
/// Delete; Ctrl+X, Ctrl+C, Ctrl+V and Delete work on the selected one.
/// </summary>
public partial class MainWindow
{
    private sealed class ObjectPaletteEntry
    {
        public required WorldObject WorldObject { get; init; }
        public string Name => WorldObject.Name;
        public BitmapSource? Icon { get; init; }
    }

    private ObjectInstance? _selectedObjectInstance;
    private ObjectInstance? _draggingObject;
    private bool _paintingObjects;
    private ObjectInstance? _copiedObject;
    private Border? _objectSelectionVisual;

    private void ObjectModeMenuItem_Click(object sender, RoutedEventArgs e) => EnterObjectMode();

    private void EnterObjectMode()
    {
        _editMode = EditMode.Object;
        ClearModeChecks();
        ObjectModeMenuItem.IsChecked = true;
        PaletteTabs.SelectedItem = ObjectsPaletteTab;
        StatusText.Text = "Objects mode: pick an object in the Objects tab and click the map to place it (hold and move to place several). Drag a placed object to move it; right-click it for Facing, starting state, Quantity, Cut, Copy, Paste and Delete.";
    }

    // ---------- Palette ----------

    /// <summary>One tab per object type, each listing that type's objects with the picture they'll have on the map.</summary>
    private void RefreshObjectPalette()
    {
        int keepTab = Math.Max(0, ObjectTypeTabs.SelectedIndex);
        ObjectTypeTabs.Items.Clear();
        foreach (var type in ObjectTypes.All)
        {
            var objects = _objectDatabase.Objects.Where(o => o.TypeChosen && o.Type == type).ToList();
            var list = new ListBox { Margin = new Thickness(2) };
            list.ItemTemplate = PaletteItemTemplate();
            list.ItemsSource = objects.Select(o => new ObjectPaletteEntry { WorldObject = o, Icon = ObjectIcon(o) }).ToList();
            list.SelectionChanged += (_, _) =>
            {
                if (list.SelectedItem is not ObjectPaletteEntry entry) return;
                _selectedObjectId = entry.WorldObject.Id;
                if (_editMode != EditMode.Object) EnterObjectMode();
                StatusText.Text = $"Placing \"{entry.Name}\" ({ObjectTypes.Name(entry.WorldObject.Type)}) - click the map.";
            };
            ObjectTypeTabs.Items.Add(new TabItem
            {
                Header = $"{ObjectTypes.Name(type)} ({objects.Count})",
                Content = objects.Count == 0
                    ? new TextBlock { Text = $"No {ObjectTypes.Name(type)} objects yet - make one in the Object Editor.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(6), Opacity = 0.7 }
                    : list
            });
        }
        ObjectTypeTabs.SelectedIndex = Math.Min(keepTab, ObjectTypeTabs.Items.Count - 1);
        _selectedObjectId = -1;
    }

    private static DataTemplate PaletteItemTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        panel.SetValue(MarginProperty, new Thickness(2));
        var image = new FrameworkElementFactory(typeof(Image));
        image.SetBinding(Image.SourceProperty, new System.Windows.Data.Binding(nameof(ObjectPaletteEntry.Icon)));
        image.SetValue(WidthProperty, 28.0);
        image.SetValue(HeightProperty, 28.0);
        image.SetValue(MarginProperty, new Thickness(0, 0, 8, 0));
        image.SetValue(RenderOptions.BitmapScalingModeProperty, BitmapScalingMode.NearestNeighbor);
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(ObjectPaletteEntry.Name)));
        text.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        panel.AppendChild(image);
        panel.AppendChild(text);
        return new DataTemplate { VisualTree = panel };
    }

    /// <summary>How an object first looks: its sprite's first frame (for its starting state), or an older object's icon file.</summary>
    private BitmapSource? ObjectIcon(WorldObject obj, ObjectInstance? instance = null)
    {
        var state = instance is null ? ObjectLooks.DefaultState(obj) : ObjectLooks.StartState(obj, instance);
        var sprite = ObjectLooks.SpriteFor(obj, state, _spriteDatabase);
        if (sprite is not null)
        {
            int frame = obj.Type == ObjectType.Keychain ? Math.Clamp(Math.Min(obj.Keychain.StartingKeyIds.Count, 4), 0, Math.Max(0, sprite.Frames.Count - 1)) // Exult: the keyring's frame is its key count
                : obj.FramesAreStates && instance is not null ? Math.Clamp(instance.FrameRuntime, 0, Math.Max(0, sprite.Frames.Count - 1)) : 0;
            string path = sprite.Frames.Count > 0 ? sprite.Frames[frame].FilePath : sprite.FilePath;
            return LoadStaticFrame(path);
        }
        return !string.IsNullOrWhiteSpace(obj.IconPath) && File.Exists(obj.IconPath) ? LoadStaticFrame(obj.IconPath) : null;
    }

    // ---------- Mouse ----------

    private ObjectInstance? ObjectAt(int x, int y) =>
        _map?.Objects.Where(o => o.ContainerInstanceId < 0 && o.X == x && o.Y == y).LastOrDefault();

    /// <summary>Left button in Objects mode: on a placed object, select it and start dragging it; on a cell without the picked object, place it (and keep placing while the button is held).</summary>
    private void ObjectMode_MouseLeftButtonDown(int x, int y)
    {
        if (_map is null) return;
        if (_objectPick is { } pick) { pick(x, y); return; } // linking a lever, setting a ladder's destination
        // On a placed object: select it and drag it (Ctrl+click places another on top of it instead).
        if (ObjectAt(x, y) is { } existing && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            SelectObjectInstance(existing);
            _draggingObject = existing;
            return;
        }
        if (_selectedObjectId < 0)
        {
            StatusText.Text = "Pick an object in the Objects tab first.";
            return;
        }
        PlaceNewObject(x, y);
        _paintingObjects = true;
    }

    /// <summary>Holding the button and moving: one of the picked object on each new cell, like drawing tiles.</summary>
    private void ObjectMode_MouseMove(int x, int y, MouseButtonState left)
    {
        if (!_paintingObjects) return;
        if (left != MouseButtonState.Pressed) { _paintingObjects = false; return; }
        if (!IsInsideMap(x, y) || _selectedObjectId < 0) return;
        if (_map!.Objects.Any(o => o.ContainerInstanceId < 0 && o.X == x && o.Y == y && o.ObjectId == _selectedObjectId)) return;
        PlaceNewObject(x, y);
    }

    private void PlaceNewObject(int x, int y)
    {
        if (_map is null) return;
        var obj = _objectDatabase.Objects.FirstOrDefault(o => o.Id == _selectedObjectId);
        var instance = new ObjectInstance { ObjectId = _selectedObjectId, X = x, Y = y, Quantity = 1, ContainerInstanceId = -1, InstanceId = _map.GetNextInstanceId() };
        _map.Objects.Add(instance);
        PlaceObjectVisual(instance);
        SelectObjectInstance(instance);
        StatusText.Text = $"Placed \"{obj?.Name}\" at ({x}, {y}).";
    }

    private bool ObjectMode_MouseLeftButtonUp(int x, int y)
    {
        _paintingObjects = false;
        if (_draggingObject is not { } dragged) return false;
        _draggingObject = null;
        if ((x, y) == (dragged.X, dragged.Y) || !IsInsideMap(x, y)) return true;
        dragged.X = x;
        dragged.Y = y;
        PlaceObjectVisual(dragged);
        SelectObjectInstance(dragged);
        StatusText.Text = $"Moved \"{ObjectName(dragged)}\" to ({x}, {y}).";
        return true;
    }

    private void ObjectMode_MouseRightButtonDown(int x, int y)
    {
        var existing = ObjectAt(x, y);
        if (existing is not null) SelectObjectInstance(existing);
        var obj = existing is null ? null : _objectDatabase.Objects.FirstOrDefault(o => o.Id == existing.ObjectId);
        var menu = new ContextMenu();
        if (existing is not null && obj is not null)
        {
            menu.Items.Add(new MenuItem { Header = $"{obj.Name} ({ObjectTypes.Name(obj.Type)})", IsEnabled = false });
            if (obj.Type is ObjectType.Chair or ObjectType.Bed)
            {
                var facingMenu = new MenuItem { Header = "Facing" };
                foreach (var direction in Enum.GetValues<SpriteDirection>())
                {
                    bool hasLook = obj.Furniture.Facings.TryGetValue(direction, out var f) && f.Sprite.IsSet;
                    var item = new MenuItem { Header = hasLook ? direction.ToString() : $"{direction} (no sprite)", IsCheckable = true, IsChecked = existing.Facing == direction };
                    var d = direction;
                    item.Click += (_, _) => { existing.Facing = d; PlaceObjectVisual(existing); StatusText.Text = $"\"{obj.Name}\" now faces {d}."; };
                    facingMenu.Items.Add(item);
                }
                menu.Items.Add(facingMenu);
            }
            if (obj.Type == ObjectType.Door)
            {
                var state = ObjectLooks.StartState(obj, existing);
                AddStateItem(menu, "Starts open", state.Open, () => { existing.StartsOpen = true; existing.StartsLocked = false; }, existing);
                AddStateItem(menu, "Starts closed", !state.Open && !state.Locked, () => { existing.StartsOpen = false; existing.StartsLocked = false; }, existing);
                AddStateItem(menu, "Starts closed and locked", state.Locked, () => { existing.StartsOpen = false; existing.StartsLocked = true; }, existing);
            }
            if (obj.Type == ObjectType.LightSource)
            {
                bool lit = existing.StartsLit ?? obj.Light.StartsLit;
                AddStateItem(menu, "Starts lit", lit, () => existing.StartsLit = !lit, existing);
            }
            AddNewTypeMenuItems(menu, obj, existing);
            if (ObjectTypes.IsBundle(obj.Type) || obj.IsStackable)
            {
                var quantity = new MenuItem { Header = $"Quantity ({existing.Quantity})..." };
                quantity.Click += (_, _) =>
                {
                    var text = SimpleInputWindow.Show(this, $"How many (1 - {ObjectTypes.MaxQuantity}):", "Quantity", existing.Quantity.ToString());
                    if (int.TryParse(text, out int n)) { existing.Quantity = Math.Clamp(n, 1, ObjectTypes.MaxQuantity); PlaceObjectVisual(existing); }
                };
                menu.Items.Add(quantity);
            }
            menu.Items.Add(new Separator());
        }
        var cut = new MenuItem { Header = "Cut", InputGestureText = "Ctrl+X", IsEnabled = existing is not null };
        cut.Click += (_, _) => { if (existing is not null) CutObjectInstance(existing); };
        var copy = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C", IsEnabled = existing is not null };
        copy.Click += (_, _) => { if (existing is not null) CopyObjectInstance(existing); };
        var paste = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V", IsEnabled = _copiedObject is not null };
        paste.Click += (_, _) => PasteObjectInstance(x, y);
        var delete = new MenuItem { Header = "Delete", InputGestureText = "Del", IsEnabled = existing is not null };
        delete.Click += (_, _) => { if (existing is not null) DeleteObjectInstance(existing); };
        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);
        menu.PlacementTarget = MapCanvas;
        menu.IsOpen = true;
    }

    private void AddStateItem(ContextMenu menu, string header, bool isChecked, Action apply, ObjectInstance instance)
    {
        var item = new MenuItem { Header = header, IsCheckable = true, IsChecked = isChecked };
        item.Click += (_, _) => { apply(); PlaceObjectVisual(instance); StatusText.Text = $"\"{ObjectName(instance)}\": {header.ToLowerInvariant()}."; };
        menu.Items.Add(item);
    }

    private bool ObjectMode_KeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (e.Key == Key.Escape && _objectPick is not null) { EndObjectPick("Done."); return true; }
        if (ctrl && e.Key == Key.X && _selectedObjectInstance is { } toCut) { CutObjectInstance(toCut); return true; }
        if (ctrl && e.Key == Key.C && _selectedObjectInstance is { } toCopy) { CopyObjectInstance(toCopy); return true; }
        if (ctrl && e.Key == Key.V && _copiedObject is not null)
        {
            var (x, y) = _lastHoverCell;
            if (!IsInsideMap(x, y)) { StatusText.Text = "Point at a cell to paste there."; return true; }
            PasteObjectInstance(x, y);
            return true;
        }
        if (e.Key == Key.Delete && _selectedObjectInstance is { } toDelete) { DeleteObjectInstance(toDelete); return true; }
        return false;
    }

    // ---------- Clipboard ----------

    private static ObjectInstance CopyOf(ObjectInstance source) => new()
    {
        ObjectId = source.ObjectId, Quantity = source.Quantity, Facing = source.Facing, ContainerInstanceId = -1,
        StartsOpen = source.StartsOpen, StartsLocked = source.StartsLocked, StartsLit = source.StartsLit, OffsetX = source.OffsetX, OffsetY = source.OffsetY,
        StartStage = source.StartStage, StartsFullOfId = source.StartsFullOfId, LinkedInstanceIds = source.LinkedInstanceIds.ToList(),
        DestinationX = source.DestinationX, DestinationY = source.DestinationY, DestinationLift = source.DestinationLift
    };

    private void CopyObjectInstance(ObjectInstance source)
    {
        _copiedObject = CopyOf(source);
        StatusText.Text = $"Copied \"{ObjectName(source)}\" - Ctrl+V (or right-click > Paste) to place it again.";
    }

    private void CutObjectInstance(ObjectInstance source)
    {
        _copiedObject = CopyOf(source);
        string name = ObjectName(source);
        DeleteObjectInstance(source, announce: false);
        StatusText.Text = $"Cut \"{name}\" - Ctrl+V (or right-click > Paste) to place it.";
    }

    private void PasteObjectInstance(int x, int y)
    {
        if (_map is null || _copiedObject is null || !IsInsideMap(x, y)) return;
        if (_objectDatabase.Objects.All(o => o.Id != _copiedObject.ObjectId)) { StatusText.Text = "That object no longer exists in the Object Editor."; return; }
        var pasted = CopyOf(_copiedObject);
        pasted.X = x;
        pasted.Y = y;
        pasted.InstanceId = _map.GetNextInstanceId();
        _map.Objects.Add(pasted);
        PlaceObjectVisual(pasted);
        SelectObjectInstance(pasted);
        StatusText.Text = $"Pasted \"{ObjectName(pasted)}\" at ({x}, {y}).";
    }

    private void DeleteObjectInstance(ObjectInstance target, bool announce = true)
    {
        if (_map is null) return;
        _map.Objects.Remove(target);
        if (_mapObjectVisuals.Remove(target.InstanceId, out var visual)) MapCanvas.Children.Remove(visual);
        if (_selectedObjectInstance == target) SelectObjectInstance(null);
        if (announce) StatusText.Text = $"Deleted \"{ObjectName(target)}\" from the map.";
    }

    private string ObjectName(ObjectInstance instance) =>
        _objectDatabase.Objects.FirstOrDefault(o => o.Id == instance.ObjectId)?.Name ?? $"Object #{instance.ObjectId}";

    // ---------- Visuals ----------

    /// <summary>Draws (or redraws) a placed object as it will start in the game: its sprite for its type, state, facing and quantity, anchored on its cell.</summary>
    private void PlaceObjectVisual(ObjectInstance instance)
    {
        if (_tileset is null) return;
        if (_mapObjectVisuals.Remove(instance.InstanceId, out var old)) MapCanvas.Children.Remove(old);
        var obj = _objectDatabase.Objects.FirstOrDefault(o => o.Id == instance.ObjectId);
        var source = obj is null ? null : ObjectIcon(obj, instance);
        (int w, int h) = source is not null ? SpriteSizing.Quantize(source.PixelWidth, source.PixelHeight) : (_tileset.TileWidth, _tileset.TileHeight);
        string tip = obj is null ? $"Object #{instance.ObjectId} (deleted)" : $"{obj.Name} ({ObjectTypes.Name(obj.Type)})" + (instance.Quantity > 1 ? $" x{instance.Quantity}" : "");
        var image = new Image { Source = source, Width = w, Height = h, IsHitTestVisible = false, ToolTip = tip, Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        PositionQuantizedVisual(image, instance.X, instance.Y, w, h);
        if (obj is { Type: ObjectType.Door } door && ObjectLooks.StartState(door, instance).Open)
        {
            // An open door hangs from its hinge (Object Editor > Open sprite position).
            Canvas.SetLeft(image, Canvas.GetLeft(image) + door.Door.OpenOffsetX);
            Canvas.SetTop(image, Canvas.GetTop(image) + door.Door.OpenOffsetY);
        }
        // Flat things (tiles, traps) under everything; the rest above the tiles, ordered by position.
        Panel.SetZIndex(image, obj?.Type is ObjectType.Tile or ObjectType.Trap ? 9 : 10);
        MapCanvas.Children.Add(image);
        _mapObjectVisuals[instance.InstanceId] = image;
        if (_selectedObjectInstance == instance) UpdateObjectSelectionVisual();
    }

    /// <summary>After the Object Editor closes: every placed object redrawn with its (maybe new) look.</summary>
    private void RefreshAllObjectVisuals()
    {
        if (_map is null) return;
        foreach (var instance in _map.Objects.Where(o => o.ContainerInstanceId < 0).ToList()) PlaceObjectVisual(instance);
    }

    private void SelectObjectInstance(ObjectInstance? instance)
    {
        _selectedObjectInstance = instance;
        UpdateObjectSelectionVisual();
    }

    /// <summary>A yellow outline around the selected object.</summary>
    private void UpdateObjectSelectionVisual()
    {
        if (_objectSelectionVisual is not null) MapCanvas.Children.Remove(_objectSelectionVisual);
        _objectSelectionVisual = null;
        ClearLinkVisuals();
        if (_selectedObjectInstance is not { } selected || !_mapObjectVisuals.TryGetValue(selected.InstanceId, out var visual)) return;
        _objectSelectionVisual = new Border { BorderBrush = Brushes.Yellow, BorderThickness = new Thickness(1.5), IsHitTestVisible = false, Width = visual.Width + 4, Height = visual.Height + 4 };
        Canvas.SetLeft(_objectSelectionVisual, Canvas.GetLeft(visual) - 2);
        Canvas.SetTop(_objectSelectionVisual, Canvas.GetTop(visual) - 2);
        Panel.SetZIndex(_objectSelectionVisual, 17);
        MapCanvas.Children.Add(_objectSelectionVisual);
        ShowLinksOf(selected);
    }
}
