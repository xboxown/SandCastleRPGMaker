using System.IO;
using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

public static class ResourceDatabaseSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Save(ResourceDatabase db, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(db, Options));
    }

    public static ResourceDatabase Load(string path)
    {
        var db = JsonSerializer.Deserialize<ResourceDatabase>(File.ReadAllText(path), Options)
                 ?? throw new InvalidDataException($"Could not parse resource database file: {path}");
        db.SyncNextIdAfterLoad();
        return db;
    }
}
