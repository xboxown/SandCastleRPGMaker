using System.IO;
using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

public static class SpriteDatabaseSerializer
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static void Save(SpriteDatabase db, string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(db, Options));

    public static SpriteDatabase Load(string path)
    {
        var db = JsonSerializer.Deserialize<SpriteDatabase>(File.ReadAllText(path), Options)
                 ?? throw new InvalidDataException($"Could not parse sprite database file: {path}");
        foreach (var group in db.Groups)
            foreach (var sprite in group.Sprites)
                sprite.SyncLayerIdAfterLoad();
        return db;
    }
}
