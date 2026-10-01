using System.IO;
using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

public static class VariableDatabaseSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Save(VariableDatabase db, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(db, Options));

    public static VariableDatabase Load(string path)
    {
        var db = JsonSerializer.Deserialize<VariableDatabase>(File.ReadAllText(path), Options)
                 ?? throw new InvalidDataException($"Could not parse variable database file: {path}");
        db.SyncNextIdAfterLoad();
        return db;
    }
}
