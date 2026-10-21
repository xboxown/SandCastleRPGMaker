using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// A house built once in the House Editor (Map Editor > Houses tab) and
/// plotted on maps as often as you like. Its layout is a little map of its
/// own, sized to the house - its size is its foundation - with tiles on any
/// floor, objects, things, characters, events and zones, edited with the Map
/// Editor's own tools. Plotted copies stay linked to it: change the house and
/// every copy changes. In play each copy is real pieces (HouseBuilder).
/// </summary>
public class HouseTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "House";
    public MapData Layout { get; set; } = new();
}

/// <summary>A house plotted on a map: which house, and the map cell of its top-left corner.</summary>
public class PlacedHouse
{
    public int Id { get; set; }
    public int HouseId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
}

/// <summary>
/// Turns every house plotted on a map into the map's own pieces as a game
/// starts (each plot its own copy: two inns, two innkeepers): its tiles on
/// every floor laid over the map's, and its objects, things, characters,
/// events and zones put in at its spot with numbers of their own. Events'
/// spots (X, Y) are the house's own, so they move with it; an event naming a
/// thing of the house names its copy.
/// </summary>
public static class HouseBuilder
{
    public static void BuildInto(MapData map, IReadOnlyList<HouseTemplate> houses)
    {
        if (map.Houses.Count == 0) return;
        foreach (var placed in map.Houses.ToList())
            if (houses.FirstOrDefault(h => h.Id == placed.HouseId) is { } house)
                Stamp(map, Copy(house.Layout), placed.X, placed.Y);
        map.Houses.Clear(); // built: a save of this map holds the pieces, not the plots
    }

    private static T Copy<T>(T source) => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(source))!;

    private static void Stamp(MapData map, MapData house, int ox, int oy)
    {
        bool Inside(int x, int y) => x >= 0 && y >= 0 && x < map.Width && y < map.Height;

        // Tiles, floor by floor: the house's own over the map's (an empty house cell leaves the map's).
        for (int floor = 1; floor <= MapFloors.MaxFloors; floor++)
        {
            if (house.LayerForFloor(floor) is not { } layer) continue;
            MapLayer? target = null;
            for (int y = 0; y < house.Height; y++)
                for (int x = 0; x < house.Width; x++)
                {
                    int tile = layer.GetTile(x, y, house.Width);
                    if (tile < 0 || !Inside(ox + x, oy + y)) continue;
                    target ??= map.LayerForFloor(floor, create: true);
                    target!.SetTile(ox + x, oy + y, map.Width, tile);
                }
        }

        // Objects (and what's inside object containers): numbers of their own.
        var objectIds = new Dictionary<int, int>();
        int nextObject = map.GetNextInstanceId();
        foreach (var o in house.Objects) objectIds[o.InstanceId] = nextObject++;
        foreach (var o in house.Objects)
        {
            o.InstanceId = objectIds[o.InstanceId];
            if (o.ContainerInstanceId >= 0) o.ContainerInstanceId = objectIds.GetValueOrDefault(o.ContainerInstanceId, o.ContainerInstanceId);
            else { o.X += ox; o.Y += oy; }
            map.Objects.Add(o);
        }
        foreach (var item in house.Items)
        {
            item.X += ox;
            item.Y += oy;
            map.Items.Add(item);
        }
        foreach (var character in house.Characters)
        {
            character.Id = map.GetNextCharacterPlacementId();
            character.X += ox;
            character.Y += oy;
            map.Characters.Add(character);
        }
        foreach (var zone in house.Zones)
        {
            zone.Id = map.NextZoneId();
            zone.X += ox;
            zone.Y += oy;
            map.Zones.Add(zone);
        }
        foreach (var ev in house.Events)
        {
            ev.Id = map.GetNextEventId();
            ev.X += ox;
            ev.Y += oy;
            foreach (var page in ev.EventPages)
                foreach (var command in page.Commands) MoveWithHouse(command, ox, oy, objectIds);
            map.Events.Add(ev);
        }
    }

    /// <summary>A command of a house's event: its spot moves with the house (not one in another world), a thing of the house is its copy.</summary>
    private static void MoveWithHouse(EventCommand command, int ox, int oy, Dictionary<int, int> objectIds)
    {
        var p = command.Parameters;
        bool otherWorld = p.TryGetValue("World", out var world) && int.TryParse(world, out int worldId) && worldId > 0; // Change Map to a world: its own spot
        if (!otherWorld && p.TryGetValue("X", out var xs) && p.TryGetValue("Y", out var ys) && int.TryParse(xs, out int x) && int.TryParse(ys, out int y))
        {
            p["X"] = (x + ox).ToString();
            p["Y"] = (y + oy).ToString();
        }
        if (p.TryGetValue("Thing", out var thing) && thing.Split(':') is ["Object", var id, var objectId] && int.TryParse(id, out int instance) && objectIds.TryGetValue(instance, out int copy))
            p["Thing"] = $"Object:{copy}:{objectId}";
    }
}
