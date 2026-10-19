using System.Windows.Media.Imaging;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// Worlds in play (Maps tab: one map is one world). Change Map to another
/// world takes the party there - at that world's Start Position or a spot in
/// it - as a loaded game does: the game goes on in a new window built from
/// it, with everything the party has, knows and has done. The world left is
/// remembered as it was (things moved, doors, the dead) and comes back so on
/// a return, and in saved games. Party members come along, beside the main
/// character; everyone else stays in their own world. Commands after a
/// Change Map to another world don't run (put them in the new world's events).
/// </summary>
public partial class GameTestWindow
{
    /// <summary>The project's worlds as made in the editor (the starting world first) - a world is built fresh from these the first time it's visited.</summary>
    public List<MapData> Worlds { get; init; } = new();
    /// <summary>The other worlds visited, as they were left (by world id).</summary>
    private Dictionary<int, MapData> _worldStates = new();
    /// <summary>Arriving from another world: the view fades in.</summary>
    public bool FadeInOnArrival { get; init; }

    /// <summary>A world's tileset picture and tile size, for its game window (worlds can use different tilesets).</summary>
    private (BitmapSource Bitmap, int TileWidth, int TileHeight, int Columns) TilesetFor(MapData world)
    {
        if (world.Tileset.TilesetSpriteName == _tilesetSpriteName) return (_tilesetBitmap, _tileWidth, _tileHeight, _columns);
        if (ProjectSession.SheetProvider?.Invoke(world.Tileset.TilesetSpriteName) is { } sheet)
            return (sheet.Bitmap, Math.Max(1, world.Tileset.TileWidth), Math.Max(1, world.Tileset.TileHeight), sheet.Columns);
        return (_tilesetBitmap, _tileWidth, _tileHeight, _columns); // its sheet is gone: this one, rather than nothing
    }

    /// <summary>The party goes to another world (fading out first if asked); false when there's no such world.</summary>
    private bool TravelToWorld(int worldId, (int X, int Y, int Floor)? spot, bool fade, Action<bool> done)
    {
        var world = Worlds.FirstOrDefault(w => w.Id == worldId);
        if (world is null) { StatusText.Text = $"Change Map: there's no world #{worldId} any more (Maps tab)."; return false; }
        // done is never called: this window closes, and its event with it (the rest goes in the new world's events).
        if (!fade) { Dispatcher.BeginInvoke(() => Travel(world, spot)); return true; }
        return FadeScreenCommand(new EventCommand { Parameters = { ["Value"] = "Out", ["Seconds"] = "0.4", ["Color"] = "#000000" } }, _ => Travel(world, spot));
    }

    private void Travel(MapData world, (int X, int Y, int Floor)? spot)
    {
        var data = CaptureSave();
        var leaving = data.Map;
        // The party goes with the main character: out of this world's placements, into the new one's.
        var partyPlacements = leaving.Characters.Where(pc => _mapCharacters.FirstOrDefault(mc => mc.Placement.Id == pc.Id) is { } mc && mc.Character.IsInParty && !IsDead(mc.Character)).ToList();
        leaving.Characters.RemoveAll(partyPlacements.Contains);
        var states = data.WorldStates;
        states[_map.Id > 0 ? _map.Id : Worlds.FirstOrDefault()?.Id ?? 1] = leaving; // a save from before worlds: the starting world
        var arriving = states.Remove(world.Id, out var kept) ? kept : JsonCopy(world);

        int x = spot?.X ?? arriving.StartX, y = spot?.Y ?? arriving.StartY, floor = spot?.Floor ?? arriving.StartFloor;
        if (x < 0 || y < 0 || x >= arriving.Width || y >= arriving.Height) { x = arriving.Width / 2; y = arriving.Height / 2; }
        floor = Math.Clamp(floor, 1, MapFloors.MaxFloors);
        arriving.StartX = x;
        arriving.StartY = y;
        arriving.StartFloor = floor;
        int n = 0;
        foreach (var placed in partyPlacements)
        {
            placed.Id = arriving.GetNextCharacterPlacementId();
            placed.X = Math.Clamp(x + n % 3 - 1, 0, arriving.Width - 1);
            placed.Y = Math.Clamp(y + 1 + n / 3, 0, arriving.Height - 1);
            placed.Floor = floor;
            arriving.Characters.Add(placed);
            n++;
        }

        var (bitmap, tileWidth, tileHeight, columns) = TilesetFor(arriving);
        data.Map = arriving;
        data.WorldStates = states;
        data.Weather = null; // the new world's own weather
        data.PlayerPixelX = x * tileWidth + tileWidth / 2.0;
        data.PlayerPixelY = y * tileHeight + tileHeight / 2.0;
        data.PlayerLift = MapFloors.BaseLift(floor);
        data.CombatMode = false;

        GameTestWindow next;
        try
        {
            next = new GameTestWindow(arriving, bitmap, tileWidth, tileHeight, columns, arriving.Tileset.TilesetSpriteName, _resources, _sprites, _books, _objects,
                data.Characters, _poseDatabase, _tileProperties, _variables, _stats, data.Containers, _systemSettings, data.Factions)
            { IsShippedGame = IsShippedGame, Worlds = Worlds, FadeInOnArrival = true };
            next.ApplySave(data);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Couldn't go to \"{world.MapName}\": {ex.Message}";
            FadeScreenCommand(new EventCommand { Parameters = { ["Value"] = "In", ["Seconds"] = "0.3", ["Color"] = "#000000" } }, _ => { });
            return;
        }
        next.Title = Title;
        next.WindowStartupLocation = System.Windows.WindowStartupLocation.Manual;
        next.Left = Left;
        next.Top = Top;
        next.Width = Width;
        next.Height = Height;
        next.WindowState = WindowState;
        next.WindowStyle = WindowStyle;
        next.ResizeMode = ResizeMode;
        next.Loaded += (_, _) => next.StatusText.Text = $"You arrive in {world.MapName}.";
        ReplacedBy = next;
        Close(); // whoever opened this window shows the next one (MainWindow), as for a loaded game
    }

    /// <summary>Arrived from another world: black, then the world fades in.</summary>
    private void FadeInIfArriving()
    {
        if (!FadeInOnArrival) return;
        FadeScreenCommand(new EventCommand { Parameters = { ["Value"] = "Out", ["Seconds"] = "0", ["Color"] = "#000000" } }, _ => { });
        FadeScreenCommand(new EventCommand { Parameters = { ["Value"] = "In", ["Seconds"] = "0.5", ["Color"] = "#000000" } }, _ => { });
    }
}
