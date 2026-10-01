using System.IO;
using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

public static class StatDatabaseSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Save(StatDatabase db, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(db, Options));

    public static StatDatabase Load(string path)
    {
        var db = JsonSerializer.Deserialize<StatDatabase>(File.ReadAllText(path), Options)
                 ?? throw new InvalidDataException($"Could not parse stat database file: {path}");
        db.SyncNextIdAfterLoad();
        return db;
    }
}
