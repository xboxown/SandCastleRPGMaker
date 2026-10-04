using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>What an object looks like in a given state - one rule for the Map Editor and the game alike.</summary>
internal readonly record struct ObjectState(bool Open, bool Locked, bool Lit, bool Spent, int Quantity, SpriteDirection Facing);

/// <summary>
/// Which sprite an object shows: a door's closed / open / locked look, a
/// light's unlit / lit / spent look, the bundle picture for a stack's size
/// (Exult's quantity frames), a chair's or bed's look for its facing, or the
/// object's own sprite. Falls back sensibly when a look isn't set.
/// </summary>
internal static class ObjectLooks
{
    public static SpriteEntry? Find(SpriteDatabase sprites, string group, string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        return sprites.Groups.FirstOrDefault(g => g.Name == group)?.Sprites.FirstOrDefault(s => s.Name == name)
            ?? sprites.Groups.SelectMany(g => g.Sprites).FirstOrDefault(s => s.Name == name);
    }

    private static SpriteEntry? Find(SpriteDatabase sprites, SpriteLink link) => Find(sprites, link.SpriteGroup, link.SpriteName);

    public static SpriteEntry? SpriteFor(WorldObject obj, ObjectState state, SpriteDatabase sprites)
    {
        var own = Find(sprites, obj.SpriteGroup, obj.SpriteName);
        switch (obj.Type)
        {
            case ObjectType.Door:
                if (state.Open) return Find(sprites, obj.Door.OpenSprite) ?? own;
                if (state.Locked) return Find(sprites, obj.Door.LockedSprite) ?? own;
                return own;
            case ObjectType.LightSource:
                if (state.Spent) return Find(sprites, obj.Light.SpentSprite) ?? own;
                if (state.Lit) return Find(sprites, obj.Light.LitSprite) ?? own;
                return own;
            case ObjectType.Ammo:
            case ObjectType.Money:
                for (int level = ObjectTypes.BundleLevel(Math.Max(1, state.Quantity)); level >= 0; level--)
                    if (level < obj.Bundle.Levels.Count && Find(sprites, obj.Bundle.Levels[level]) is { } bundle) return bundle;
                return own;
            case ObjectType.Chair:
            case ObjectType.Bed:
                return obj.Furniture.Resolve(state.Facing) is { } facing ? Find(sprites, facing.Sprite) ?? own : own;
            default:
                return own;
        }
    }

    /// <summary>The state an object starts in when the game begins (the Map Editor's overrides, else the Object Editor's).</summary>
    public static ObjectState StartState(WorldObject obj, ObjectInstance instance) => new(
        Open: obj.Type == ObjectType.Door && (instance.StartsOpen ?? obj.Door.StartsOpen),
        Locked: obj.Type == ObjectType.Door && !(instance.StartsOpen ?? obj.Door.StartsOpen) && ((instance.StartsLocked ?? obj.Door.StartsLocked) || obj.Door.MagicallyLocked),
        Lit: obj.Type == ObjectType.LightSource && (instance.StartsLit ?? obj.Light.StartsLit),
        Spent: false,
        Quantity: Math.Max(1, instance.Quantity),
        Facing: instance.Facing);
}
