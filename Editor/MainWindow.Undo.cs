using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Input;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Undo / Redo in the Map Editor (Edit > Undo, Ctrl+Z; Redo, Ctrl+Y), in every mode: tiles painted or erased,
/// events, objects, books and containers, characters, spawn points, zones and the start position - added,
/// deleted, cut, pasted, moved or changed. What's on the map (all but its tiles) is kept as it was at each
/// press of the mouse or key: anything changed since is one step. Tiles are kept cell by cell as they're
/// painted (a whole stroke is one step), so a big map costs nothing to keep.
/// </summary>
public partial class MainWindow
{
    /// <summary>One step back: what the map's things were (as JSON), and/or the tiles a stroke changed (their old numbers).</summary>
    private sealed class UndoStep
    {
        public string? Things;
        public List<(MapLayer Layer, int Index, int Tile)>? Tiles;
    }

    /// <summary>The map's things, all but its tiles.</summary>
    private sealed record ThingsState(List<ObjectInstance> Objects, List<PlacedItemInstance> Items, List<GameEvent> Events, List<PlacedCharacter> Characters,
        List<SpawnPoint> SpawnPoints, List<MapZone> Zones, int StartX, int StartY, int StartFloor);

    private const int UndoLimit = 200;
    private readonly List<UndoStep> _undoSteps = new(), _redoSteps = new();
    private MapData? _undoMap;
    private string? _undoBaseline;
    private readonly List<(MapLayer Layer, int Index, int Tile)> _tileStroke = new();

    private static string ThingsOf(MapData map) =>
        JsonSerializer.Serialize(new ThingsState(map.Objects, map.Items, map.Events, map.Characters, map.SpawnPoints, map.Zones, map.StartX, map.StartY, map.StartFloor));

    /// <summary>A tile set, its old number kept for Undo.</summary>
    private void SetTileUndoable(MapLayer layer, int x, int y, int tile)
    {
        if (_map is null) return;
        int index = y * _map.Width + x, old = layer.GetTile(x, y, _map.Width);
        if (old == tile) return;
        _tileStroke.Add((layer, index, old));
        layer.SetTile(x, y, _map.Width, tile);
    }

    /// <summary>What has changed since the last check becomes one step back (called at each press of the mouse or a key).</summary>
    private void UndoCheckpoint()
    {
        if (_map is null) return;
        if (!ReferenceEquals(_undoMap, _map))
        {
            // Another map (opened, or another world): its own history.
            _undoSteps.Clear(); _redoSteps.Clear(); _tileStroke.Clear();
            _undoMap = _map;
            _undoBaseline = ThingsOf(_map);
            return;
        }
        if (_tileStroke.Count > 0)
        {
            Push(new UndoStep { Tiles = _tileStroke.ToList() });
            _tileStroke.Clear();
        }
        string now = ThingsOf(_map);
        if (now != _undoBaseline)
        {
            Push(new UndoStep { Things = _undoBaseline });
            _undoBaseline = now;
        }
    }

    private void Push(UndoStep step)
    {
        _undoSteps.Add(step);
        if (_undoSteps.Count > UndoLimit) _undoSteps.RemoveAt(0);
        _redoSteps.Clear();
    }

    private void UndoMenuItem_Click(object sender, System.Windows.RoutedEventArgs e) => StepBack(_undoSteps, _redoSteps, "Undone");

    private void RedoMenuItem_Click(object sender, System.Windows.RoutedEventArgs e) => StepBack(_redoSteps, _undoSteps, "Redone");

    /// <summary>The last step of one list taken back, the way back to it kept in the other.</summary>
    private void StepBack(List<UndoStep> from, List<UndoStep> to, string what)
    {
        if (_map is null) return;
        if (from == _undoSteps) UndoCheckpoint(); // what was just done is a step too
        else if (_tileStroke.Count > 0 || ThingsOf(_map) != _undoBaseline) { StatusText.Text = "Nothing to redo - something has changed since."; return; }
        if (from.Count == 0) { StatusText.Text = what == "Undone" ? "Nothing to undo." : "Nothing to redo."; return; }
        var step = from[^1];
        from.RemoveAt(from.Count - 1);
        var back = new UndoStep();
        if (step.Tiles is { } tiles)
        {
            back.Tiles = new List<(MapLayer, int, int)>();
            for (int i = tiles.Count - 1; i >= 0; i--)
            {
                var (layer, index, tile) = tiles[i];
                if (index < 0 || index >= layer.TileIndices.Count) continue;
                back.Tiles.Add((layer, index, layer.TileIndices[index]));
                layer.TileIndices[index] = tile;
            }
            back.Tiles.Reverse();
        }
        if (step.Things is { } things && JsonSerializer.Deserialize<ThingsState>(things) is { } state)
        {
            back.Things = ThingsOf(_map);
            _map.Objects = state.Objects; _map.Items = state.Items; _map.Events = state.Events; _map.Characters = state.Characters;
            _map.SpawnPoints = state.SpawnPoints; _map.Zones = state.Zones;
            _map.StartX = state.StartX; _map.StartY = state.StartY; _map.StartFloor = state.StartFloor;
            _undoBaseline = things;
        }
        to.Add(back);
        SelectObjectInstance(null);
        SelectPlacedCharacter(null);
        ClearFreeSelection();
        BuildMapCanvas();
        StatusText.Text = $"{what}. ({_undoSteps.Count} step(s) to undo, {_redoSteps.Count} to redo)";
    }

    /// <summary>Ctrl+Z / Ctrl+Y, and a checkpoint before any key that may change the map. True when it was Undo / Redo.</summary>
    private bool UndoKeys(KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return false; // typing: the box's own undo
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.Z) { UndoMenuItem_Click(this, new System.Windows.RoutedEventArgs()); return true; }
        if (ctrl && e.Key == Key.Y) { RedoMenuItem_Click(this, new System.Windows.RoutedEventArgs()); return true; }
        if (ctrl || e.Key is Key.Delete or Key.Back or Key.PageUp or Key.PageDown) UndoCheckpoint();
        return false;
    }
}
