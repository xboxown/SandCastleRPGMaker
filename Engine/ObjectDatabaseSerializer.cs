using System.IO;
using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

public static class ObjectDatabaseSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true
    };

    public static void Save(ObjectDatabase db, string path)
    {
        string json = JsonSerializer.Serialize(db, Options);
        File.WriteAllText(path, json);
    }

    public static ObjectDatabase Load(string path)
    {
        string json = File.ReadAllText(path);
        var db = JsonSerializer.Deserialize<ObjectDatabase>(json, Options)
                 ?? throw new InvalidDataException($"Could not parse object database file: {path}");
        db.SyncNextIdAfterLoad();
        return db;
    }
}
