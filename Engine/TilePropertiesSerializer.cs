using System.IO;
using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

public static class TilePropertiesSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Save(TilePropertiesDatabase db, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(db, Options));
    }

    public static TilePropertiesDatabase Load(string path)
    {
        return JsonSerializer.Deserialize<TilePropertiesDatabase>(File.ReadAllText(path), Options)
               ?? throw new InvalidDataException($"Could not parse tile properties file: {path}");
    }
}
