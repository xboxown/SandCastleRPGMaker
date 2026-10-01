using System.IO;
using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

public static class FactionDatabaseSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Save(FactionDatabase db, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(db, Options));

    public static FactionDatabase Load(string path)
    {
        var db = JsonSerializer.Deserialize<FactionDatabase>(File.ReadAllText(path), Options)
                 ?? throw new InvalidDataException($"Could not parse faction database file: {path}");
        db.SyncNextIdAfterLoad();
        return db;
    }
}
