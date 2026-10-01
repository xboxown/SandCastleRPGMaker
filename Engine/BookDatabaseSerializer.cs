using System.IO;
using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

public static class BookDatabaseSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Save(BookDatabase db, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(db, Options));

    public static BookDatabase Load(string path)
    {
        var db = JsonSerializer.Deserialize<BookDatabase>(File.ReadAllText(path), Options)
                 ?? throw new InvalidDataException($"Could not parse book database file: {path}");
        db.SyncNextIdAfterLoad();
        return db;
    }
}
