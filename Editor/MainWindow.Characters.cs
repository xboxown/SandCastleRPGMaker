using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Map Editor: placing characters (from the Character Editor) on the map.
/// Mode > Characters: pick a character in the Characters palette tab and
/// click an empty cell to place it, shown in its real pose (Idle, facing
/// down, by default). Press a placed character and drag to move it.
/// Right-click it for Pose / Facing / Cut / Copy / Paste / Delete;
/// Ctrl+X, Ctrl+C, Ctrl+V and Delete work on the selected one. One
/// character per cell. The main player isn't placed here - it starts at
/// the Start Position.
/// </summary>
public partial class MainWindow
{
    /// <summary>A row in the Characters palette.</summary>
    private sealed class CharacterPaletteEntry
    {
        public required CharacterDefinition Character { get; init; }
        public string Name => Character.Name;
        public BitmapSource? Icon { get; init; }
    }

    private readonly Dictionary<PlacedCharacter, FrameworkElement> _mapCharacterVisuals = new();
    private PlacedCharacter? _selectedPlacedCharacter;
    private PlacedCharacter? _draggingCharacter;
    private (int CharacterId, int PoseId, SpriteDirection Facing)? _copiedCharacter;
    private (int x, int y) _lastHoverCell = (-1, -1);
    private Border? _characterSelectionVisual;

    private void CharacterModeMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _editMode = EditMode.Character;
        ClearModeChecks();
        CharacterModeMenuItem.IsChecked = true;
        RefreshCharactersPalette();
        PaletteTabs.SelectedItem = CharactersPaletteTab;
        StatusText.Text = "Characters mode: pick a character in the Characters tab and click an empty cell to place it. Drag a placed character to move it; right-click it for Pose, Facing, Cut, Copy, Paste and Delete.";
    }

    /// <summary>Fills the Characters palette - every character except the main player, each shown in its Idle pose facing down.</summary>
    private void RefreshCharactersPalette()
    {
        var selectedId = (CharactersPaletteListBox.SelectedItem as CharacterPaletteEntry)?.Character.Id;
        var entries = _characterDatabase.Characters
            .Where(c => !c.IsTestPlayer)
            .Select(c => new CharacterPaletteEntry { Character = c, Icon = LoadCharacterFrame(c, -1, SpriteDirection.South) })
            .ToList();
        CharactersPaletteListBox.ItemsSource = entries;
        CharactersPaletteListBox.SelectedItem = entries.FirstOrDefault(entry => entry.Character.Id == selectedId);
    }

    /// <summary>The first frame of a character's pose sprite at a facing (PoseId -1 = Idle) - what the Map Editor shows for it.</summary>
    private BitmapImage? LoadCharacterFrame(CharacterDefinition character, int poseId, SpriteDirection facing)
    {
        var sprite = ResolveCharacterSprite(character, poseId, facing);
        if (sprite is null) return null;
        string path = sprite.Frames.Count > 0 ? sprite.Frames[0].FilePath : sprite.FilePath;
        return LoadStaticFrame(path);
    }

    private SpriteEntry? ResolveCharacterSprite(CharacterDefinition character, int poseId, SpriteDirection facing)
    {
        var pose = poseId >= 0 ? _poseDatabase.Poses.FirstOrDefault(p => p.Id == poseId) : _poseDatabase.Poses.FirstOrDefault(p => p.Name == "Idle");
        if (pose is null || !character.PoseSprites.TryGetValue(pose.Id, out var assignment)) return null;
        var link = assignment.Resolve(facing);
        if (link is null) return null;
        return _spriteDatabase.Groups.FirstOrDefault(g => g.Name == link.SpriteGroup)?.Sprites.FirstOrDefault(s => s.Name == link.SpriteName)
            ?? _spriteDatabase.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == link.SpriteName);
    }

    private PlacedCharacter? CharacterAt(int x, int y) => _map?.Characters.FirstOrDefault(c => c.X == x && c.Y == y && c.Floor == _currentFloor);

    // ---------- Mouse ----------

    /// <summary>Left button in Characters mode: on a placed character, select it and start dragging it; on an empty cell, place the character picked in the palette.</summary>
    private void CharacterMode_MouseLeftButtonDown(int x, int y)
    {
        if (_map is null) return;
        var existing = CharacterAt(x, y);
        if (existing is not null)
        {
            SelectPlacedCharacter(existing);
            _draggingCharacter = existing;
            return;
        }

        if (CharactersPaletteListBox.SelectedItem is not CharacterPaletteEntry entry)
        {
            StatusText.Text = "Pick a character in the Characters tab first.";
            return;
        }
        var placed = new PlacedCharacter { Id = _map.GetNextCharacterPlacementId(), CharacterId = entry.Character.Id, X = x, Y = y, Floor = _currentFloor };
        _map.Characters.Add(placed);
        PlaceCharacterVisual(placed);
        SelectPlacedCharacter(placed);
        StatusText.Text = $"Placed \"{entry.Character.Name}\" at ({x}, {y}).";
    }

    /// <summary>Left button released: finish moving a dragged character onto the cell under the mouse (if it's free).</summary>
    private bool CharacterMode_MouseLeftButtonUp(int x, int y)
    {
        if (_draggingCharacter is not { } dragged) return false;
        _draggingCharacter = null;
        if ((x, y) == (dragged.X, dragged.Y) || !IsInsideMap(x, y)) return true;
        if (CharacterAt(x, y) is not null)
        {
            StatusText.Text = "There's already a character on that cell.";
            return true;
        }
        dragged.X = x;
        dragged.Y = y;
        PlaceCharacterVisual(dragged);
        SelectPlacedCharacter(dragged);
        StatusText.Text = $"Moved \"{CharacterName(dragged)}\" to ({x}, {y}).";
        return true;
    }

    private void CharacterMode_MouseRightButtonDown(int x, int y)
    {
        var existing = CharacterAt(x, y);
        if (existing is not null) SelectPlacedCharacter(existing);
        var menu = new ContextMenu();

        if (existing is not null && _characterDatabase.Characters.FirstOrDefault(c => c.Id == existing.CharacterId) is { } character)
        {
            var poseMenu = new MenuItem { Header = "Pose" };
            foreach (var pose in _poseDatabase.Poses)
            {
                bool hasArt = character.PoseSprites.ContainsKey(pose.Id);
                bool current = existing.PoseId == pose.Id || (existing.PoseId < 0 && pose.Name == "Idle");
                var item = new MenuItem { Header = hasArt ? pose.Name : $"{pose.Name} (no sprite)", IsCheckable = true, IsChecked = current };
                var thisPose = pose;
                item.Click += (_, _) => { existing.PoseId = thisPose.Name == "Idle" ? -1 : thisPose.Id; PlaceCharacterVisual(existing); StatusText.Text = $"\"{character.Name}\" now starts in pose {thisPose.Name}."; };
                poseMenu.Items.Add(item);
            }
            menu.Items.Add(poseMenu);

            var facingMenu = new MenuItem { Header = "Facing" };
            foreach (var direction in Enum.GetValues<SpriteDirection>())
            {
                var item = new MenuItem { Header = direction.ToString(), IsCheckable = true, IsChecked = existing.Facing == direction };
                var thisDirection = direction;
                item.Click += (_, _) => { existing.Facing = thisDirection; PlaceCharacterVisual(existing); StatusText.Text = $"\"{character.Name}\" now faces {thisDirection}."; };
                facingMenu.Items.Add(item);
            }
            menu.Items.Add(facingMenu);
            menu.Items.Add(new Separator());
        }

        var cut = new MenuItem { Header = "Cut", InputGestureText = "Ctrl+X", IsEnabled = existing is not null };
        cut.Click += (_, _) => { if (existing is not null) CutPlacedCharacter(existing); };
        var copy = new MenuItem { Header = "Copy", InputGestureText = "Ctrl+C", IsEnabled = existing is not null };
        copy.Click += (_, _) => { if (existing is not null) CopyPlacedCharacter(existing); };
        var paste = new MenuItem { Header = "Paste", InputGestureText = "Ctrl+V", IsEnabled = _copiedCharacter is not null && existing is null };
        paste.Click += (_, _) => PastePlacedCharacter(x, y);
        var delete = new MenuItem { Header = "Delete", InputGestureText = "Del", IsEnabled = existing is not null };
        delete.Click += (_, _) => { if (existing is not null) DeletePlacedCharacter(existing); };
        menu.Items.Add(cut);
        menu.Items.Add(copy);
        menu.Items.Add(paste);
        menu.Items.Add(new Separator());
        menu.Items.Add(delete);

        menu.PlacementTarget = MapCanvas;
        menu.IsOpen = true;
    }

    /// <summary>Ctrl+X / Ctrl+C / Ctrl+V / Delete in Characters mode. Paste goes to the cell under the mouse if it's free, otherwise next to the selected character.</summary>
    private bool CharacterMode_KeyDown(KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.X && _selectedPlacedCharacter is { } toCut) { CutPlacedCharacter(toCut); return true; }
        if (ctrl && e.Key == Key.C && _selectedPlacedCharacter is { } toCopy) { CopyPlacedCharacter(toCopy); return true; }
        if (ctrl && e.Key == Key.V && _copiedCharacter is not null)
        {
            var (x, y) = _lastHoverCell;
            if (!IsInsideMap(x, y) || CharacterAt(x, y) is not null)
            {
                if (_selectedPlacedCharacter is not { } anchor) { StatusText.Text = "Point at an empty cell to paste there."; return true; }
                (x, y) = (anchor.X, anchor.Y);
                if (IsInsideMap(x + 1, y) && CharacterAt(x + 1, y) is null) x++;
                else if (IsInsideMap(x, y + 1) && CharacterAt(x, y + 1) is null) y++;
            }
            PastePlacedCharacter(x, y);
            return true;
        }
        if (e.Key == Key.Delete && _selectedPlacedCharacter is { } toDelete) { DeletePlacedCharacter(toDelete); return true; }
        return false;
    }

    // ---------- Clipboard ----------

    private void CopyPlacedCharacter(PlacedCharacter source)
    {
        _copiedCharacter = (source.CharacterId, source.PoseId, source.Facing);
        StatusText.Text = $"Copied \"{CharacterName(source)}\" - Ctrl+V (or right-click an empty cell > Paste) to place it again.";
    }

    private void CutPlacedCharacter(PlacedCharacter source)
    {
        _copiedCharacter = (source.CharacterId, source.PoseId, source.Facing);
        string name = CharacterName(source);
        DeletePlacedCharacter(source, announce: false);
        StatusText.Text = $"Cut \"{name}\" - Ctrl+V (or right-click an empty cell > Paste) to place it.";
    }

    private void PastePlacedCharacter(int x, int y)
    {
        if (_map is null || _copiedCharacter is not { } copied) return;
        if (!IsInsideMap(x, y)) return;
        if (CharacterAt(x, y) is not null)
        {
            StatusText.Text = "There's already a character on that cell.";
            return;
        }
        if (_characterDatabase.Characters.All(c => c.Id != copied.CharacterId))
        {
            StatusText.Text = "That character no longer exists in the Character Editor.";
            return;
        }
        var pasted = new PlacedCharacter { Id = _map.GetNextCharacterPlacementId(), CharacterId = copied.CharacterId, PoseId = copied.PoseId, Facing = copied.Facing, X = x, Y = y, Floor = _currentFloor };
        _map.Characters.Add(pasted);
        PlaceCharacterVisual(pasted);
        SelectPlacedCharacter(pasted);
        StatusText.Text = $"Pasted \"{CharacterName(pasted)}\" at ({x}, {y}).";
    }

    private void DeletePlacedCharacter(PlacedCharacter target, bool announce = true)
    {
        if (_map is null) return;
        _map.Characters.Remove(target);
        if (_mapCharacterVisuals.Remove(target, out var visual)) MapCanvas.Children.Remove(visual);
        if (_selectedPlacedCharacter == target) SelectPlacedCharacter(null);
        if (announce) StatusText.Text = $"Deleted \"{CharacterName(target)}\" from the map.";
    }

    private string CharacterName(PlacedCharacter placed) =>
        _characterDatabase.Characters.FirstOrDefault(c => c.Id == placed.CharacterId)?.Name ?? $"Character #{placed.CharacterId}";

    // ---------- Visuals ----------

    /// <summary>Draws (or redraws) a placed character in its pose and facing, anchored on its cell the way the game draws it (bottom-center); a labelled marker if it has no art for that pose.</summary>
    private void PlaceCharacterVisual(PlacedCharacter placed)
    {
        if (_tileset is null) return;
        if (_mapCharacterVisuals.Remove(placed, out var old)) MapCanvas.Children.Remove(old);

        var character = _characterDatabase.Characters.FirstOrDefault(c => c.Id == placed.CharacterId);
        var bitmap = character is null ? null : LoadCharacterFrame(character, placed.PoseId, placed.Facing);
        FrameworkElement visual;
        double width = _tileset.TileWidth, height = _tileset.TileHeight;
        string tip = character is null ? $"Character #{placed.CharacterId} (not found)" : $"{character.Name} - right-click for Pose, Facing, Cut, Copy, Paste, Delete";
        if (bitmap is not null)
        {
            (int qw, int qh) = SpriteSizing.Quantize(bitmap.PixelWidth, bitmap.PixelHeight);
            width = qw;
            height = qh;
            visual = new Image { Source = bitmap, Width = qw, Height = qh, ToolTip = tip };
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        }
        else
        {
            visual = BuildLetteredItemMarker("C", Color.FromArgb(200, 160, 60, 160), Brushes.Orchid, tip);
        }
        PositionQuantizedVisual(visual, placed.X, placed.Y, width, height);
        Panel.SetZIndex(visual, 16); // above items, below the event/start markers
        if (FitToFloor(visual, placed.Floor) is null) return; // on a floor above the picked one
        MapCanvas.Children.Add(visual);
        _mapCharacterVisuals[placed] = visual;
        if (_selectedPlacedCharacter == placed) UpdateCharacterSelectionVisual();
    }

    private void SelectPlacedCharacter(PlacedCharacter? placed)
    {
        _selectedPlacedCharacter = placed;
        UpdateCharacterSelectionVisual();
    }

    /// <summary>A yellow outline around the selected character.</summary>
    private void UpdateCharacterSelectionVisual()
    {
        if (_characterSelectionVisual is not null) MapCanvas.Children.Remove(_characterSelectionVisual);
        _characterSelectionVisual = null;
        if (_selectedPlacedCharacter is not { } placed || !_mapCharacterVisuals.TryGetValue(placed, out var visual)) return;
        _characterSelectionVisual = new Border
        {
            BorderBrush = Brushes.Yellow, BorderThickness = new Thickness(1.5), IsHitTestVisible = false,
            Width = visual.Width + 4, Height = visual.Height + 4
        };
        Canvas.SetLeft(_characterSelectionVisual, Canvas.GetLeft(visual) - 2);
        Canvas.SetTop(_characterSelectionVisual, Canvas.GetTop(visual) - 2);
        Panel.SetZIndex(_characterSelectionVisual, 17);
        MapCanvas.Children.Add(_characterSelectionVisual);
    }

    /// <summary>Draws every placed character (called when the map canvas is rebuilt).</summary>
    private void PlaceAllCharacterVisuals()
    {
        _mapCharacterVisuals.Clear();
        _characterSelectionVisual = null;
        if (_map is null) return;
        foreach (var placed in _map.Characters) PlaceCharacterVisual(placed);
    }
}
