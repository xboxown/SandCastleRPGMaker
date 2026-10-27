using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Mode > Spawn: spawn points on the map (SpawnPoint, as Ultima VII's monster eggs) - a pulsating yellow
/// "SP" marker each. Click an empty cell for a new one (its window opens), double-click one to edit it;
/// right-click for Edit, Cut, Copy, Paste and Delete.
/// </summary>
public partial class MainWindow
{
    private MenuItem? _spawnModeItem;
    private readonly Dictionary<SpawnPoint, FrameworkElement> _spawnVisuals = new(ReferenceEqualityComparer.Instance);
    private SpawnPoint? _copiedSpawn;

    private void AddSpawnMode()
    {
        if (_spawnModeItem is not null) return;
        _spawnModeItem = new MenuItem { Header = "Spawn", IsCheckable = true, ToolTip = "Spawn points: characters that may appear on the map when it's entered, by their chance, and roam round the point (or go free)" };
        _spawnModeItem.Click += (_, _) =>
        {
            _editMode = EditMode.Spawn;
            ClearModeChecks();
            _spawnModeItem.IsChecked = true;
            StatusText.Text = "Spawn mode: click an empty cell to add a spawn point, double-click one to edit it, right-click for cut, copy, paste and delete.";
        };
        ModeMenu.Items.Insert(ModeMenu.Items.IndexOf(StartPositionModeMenuItem) + 1, _spawnModeItem);
    }

    private SpawnPoint? SpawnAt(int x, int y) => _map?.SpawnPoints.FirstOrDefault(s => s.X == x && s.Y == y && s.Floor == _currentFloor);

    /// <summary>The picked floor's spawn points, pulsating yellow "SP".</summary>
    private void DrawSpawnPoints()
    {
        foreach (var old in _spawnVisuals.Values) MapCanvas.Children.Remove(old);
        _spawnVisuals.Clear();
        if (_map is null || _tileset is null) return;
        foreach (var spawn in _map.SpawnPoints.Where(s => s.Floor == _currentFloor)) PlaceSpawnVisual(spawn);
    }

    private void PlaceSpawnVisual(SpawnPoint spawn)
    {
        if (_tileset is null) return;
        if (_spawnVisuals.Remove(spawn, out var old)) MapCanvas.Children.Remove(old);
        if (spawn.Floor != _currentFloor) return;
        var dim = Color.FromArgb(160, 150, 130, 0);
        var bright = Color.FromArgb(230, 255, 230, 0);
        var fill = new SolidColorBrush(dim);
        fill.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation { From = dim, To = bright, Duration = TimeSpan.FromSeconds(0.9), AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
        var names = spawn.Entries.Select(e => $"{_characterDatabase.Characters.FirstOrDefault(c => c.Id == e.CharacterId)?.Name ?? "(gone)"} {e.Count} x {e.Percent}%");
        var marker = new Border
        {
            Width = _tileset.TileWidth, Height = _tileset.TileHeight, Background = fill, BorderBrush = Brushes.Gold, BorderThickness = new Thickness(1), IsHitTestVisible = false,
            ToolTip = spawn.Entries.Count == 0 ? "Spawn point (nobody yet)" : $"Spawn point: {string.Join(", ", names)} - {(spawn.Free ? "free" : $"within {spawn.WanderTiles} tiles")}",
            Child = new TextBlock { Text = "SP", Foreground = Brushes.Black, FontWeight = FontWeights.Bold, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        Canvas.SetLeft(marker, spawn.X * _tileset.TileWidth);
        Canvas.SetTop(marker, spawn.Y * _tileset.TileHeight);
        Panel.SetZIndex(marker, MarkerZIndex);
        MapCanvas.Children.Add(marker);
        _spawnVisuals[spawn] = marker;
    }

    private void SpawnMode_Click(int x, int y, int clicks)
    {
        if (_map is null) return;
        if (SpawnAt(x, y) is { } existing)
        {
            if (clicks >= 2) EditSpawn(existing);
            else StatusText.Text = $"Spawn point at ({x}, {y}): double-click to edit it, right-click for more.";
            return;
        }
        var spawn = new SpawnPoint { Id = NextSpawnId(), X = x, Y = y, Floor = _currentFloor };
        if (!EditSpawn(spawn, isNew: true)) return;
        _map.SpawnPoints.Add(spawn);
        PlaceSpawnVisual(spawn);
    }

    private int NextSpawnId() => _map is null || _map.SpawnPoints.Count == 0 ? 1 : _map.SpawnPoints.Max(s => s.Id) + 1;

    /// <summary>Its window; false when cancelled.</summary>
    private bool EditSpawn(SpawnPoint spawn, bool isNew = false)
    {
        var window = new SpawnPointWindow(spawn, _characterDatabase, _poseDatabase, _spriteDatabase, _resourceDatabase) { Owner = this };
        if (window.ShowDialog() != true) return false;
        if (!isNew) PlaceSpawnVisual(spawn);
        StatusText.Text = $"Spawn point at ({spawn.X}, {spawn.Y}): {spawn.Entries.Count} character(s).";
        return true;
    }

    private void SpawnMode_RightClick(int x, int y)
    {
        if (_map is not { } map) return;
        var spawn = SpawnAt(x, y);
        var menu = new ContextMenu();
        MenuItem Item(string header, bool enabled, Action click)
        {
            var item = new MenuItem { Header = header, IsEnabled = enabled };
            item.Click += (_, _) => click();
            menu.Items.Add(item);
            return item;
        }
        Item("Edit...", spawn is not null, () => EditSpawn(spawn!));
        menu.Items.Add(new Separator());
        Item("Cut", spawn is not null, () => { _copiedSpawn = Copy(spawn!); map.SpawnPoints.Remove(spawn!); if (_spawnVisuals.Remove(spawn!, out var v)) MapCanvas.Children.Remove(v); });
        Item("Copy", spawn is not null, () => _copiedSpawn = Copy(spawn!));
        Item("Paste", _copiedSpawn is not null && spawn is null, () =>
        {
            var pasted = Copy(_copiedSpawn!);
            pasted.Id = NextSpawnId(); pasted.X = x; pasted.Y = y; pasted.Floor = _currentFloor;
            map.SpawnPoints.Add(pasted);
            PlaceSpawnVisual(pasted);
        });
        menu.Items.Add(new Separator());
        Item("Delete", spawn is not null, () =>
        {
            if (MessageBox.Show(this, "Delete this spawn point?", "Spawn", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            map.SpawnPoints.Remove(spawn!);
            if (_spawnVisuals.Remove(spawn!, out var v)) MapCanvas.Children.Remove(v);
        });
        menu.PlacementTarget = MapCanvas;
        menu.IsOpen = true;
    }

    private static SpawnPoint Copy(SpawnPoint spawn) =>
        System.Text.Json.JsonSerializer.Deserialize<SpawnPoint>(System.Text.Json.JsonSerializer.Serialize(spawn))!;
}
