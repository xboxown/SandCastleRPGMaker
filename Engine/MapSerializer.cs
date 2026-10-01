using System.IO;
using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

public static class MapSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static void Save(MapData map, string path)
    {
        string json = JsonSerializer.Serialize(map, Options);
        File.WriteAllText(path, json);
    }

    public static MapData Load(string path)
    {
        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<MapData>(json, Options)
               ?? throw new InvalidDataException($"Could not parse map file: {path}");
    }
}
