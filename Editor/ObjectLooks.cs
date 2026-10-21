using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>What an object looks like in a given state - one rule for the Map Editor and the game alike.</summary>
/// <summary>Open = a door or window open, a lever on, a bedroll laid out; Spent = a light burnt out, a window broken, a useable object used up; Stage = a plant's growth stage; Liquid = what a liquid container holds ("" or null = empty).</summary>
internal readonly record struct ObjectState(bool Open, bool Locked, bool Lit, bool Spent, int Quantity, SpriteDirection Facing, int Stage = 0, int LiquidId = -1);

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
        // A vehicle's piece turned another way: its look for that way (Object Editor > Vehicle part > Turned looks).
        var own = (obj.TurnLooks.TryGetValue(state.Facing, out var turned) && turned.IsSet ? Find(sprites, turned) : null) ?? Find(sprites, obj.SpriteGroup, obj.SpriteName);
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
            case ObjectType.Lockpick:
                for (int level = ObjectTypes.BundleLevel(Math.Max(1, state.Quantity)); level >= 0; level--)
                    if (level < obj.Bundle.Levels.Count && Find(sprites, obj.Bundle.Levels[level]) is { } bundle) return bundle;
                return own;
            case ObjectType.Chair:
            case ObjectType.Bed:
                return obj.Furniture.Resolve(state.Facing) is { } facing ? Find(sprites, facing.Sprite) ?? own : own;
            case ObjectType.Window:
                if (state.Spent) return Find(sprites, obj.Window.BrokenSprite) ?? own;
                if (state.Open && obj.Window.HasShutters) return Find(sprites, obj.Window.OpenSprite) ?? own;
                return own;
            case ObjectType.Plant:
                return obj.Plant.Stages.Count > 0 ? Find(sprites, obj.Plant.Stages[Math.Clamp(state.Stage, 0, obj.Plant.Stages.Count - 1)]) ?? own : own;
            case ObjectType.Lever:
                return state.Open ? Find(sprites, obj.Lever.OnSprite) ?? own : own;
            case ObjectType.Explosive:
                return state.Lit ? Find(sprites, obj.Explosive.LitSprite) ?? own : own;
            case ObjectType.CraftingStation:
                return state.Lit ? Find(sprites, obj.Crafting.WorkingSprite) ?? own : own;
            case ObjectType.Bedroll:
                // Laid out: its look for the way it faces (as a bed's), else its laid-out sprite.
                if (!state.Open) return own;
                return (obj.Furniture.Resolve(state.Facing) is { } laid ? Find(sprites, laid.Sprite) : null) ?? Find(sprites, obj.Bedroll.LaidOutSprite) ?? own;
            case ObjectType.UseableObject:
                return state.Spent ? Find(sprites, obj.Useable.UsedUpSprite) ?? own : own;
            case ObjectType.LiquidContainer:
                if (state.LiquidId < 0) return own;
                return obj.Liquid.Liquids.FirstOrDefault(l => l.FoodId == state.LiquidId) is { } liquid ? Find(sprites, liquid.Sprite) ?? own : own;
            default:
                return own;
        }
    }

    /// <summary>A liquid container's starting liquid, a liquid Food's id (the placement's choice, else the Object Editor's) - -1 if empty.</summary>
    public static int StartLiquid(WorldObject obj, ObjectInstance? instance) =>
        obj.Type != ObjectType.LiquidContainer ? -1 : instance?.StartsFullOfId ?? obj.Liquid.StartsFullOfId;

    /// <summary>The state an object starts in when the game begins (the Map Editor's overrides, else the Object Editor's).</summary>
    public static ObjectState StartState(WorldObject obj, ObjectInstance instance) => new(
        Open: obj.Type switch
        {
            ObjectType.Door => instance.StartsOpen ?? obj.Door.StartsOpen,
            ObjectType.Window => obj.Window.HasShutters && (instance.StartsOpen ?? obj.Window.StartsOpen),
            ObjectType.Lever => obj.Lever.Kind != LeverKind.Button && (instance.StartsOpen ?? obj.Lever.StartsOn),
            _ => false
        },
        Locked: obj.Type == ObjectType.Door && !(instance.StartsOpen ?? obj.Door.StartsOpen) && ((instance.StartsLocked ?? obj.Door.StartsLocked) || obj.Door.MagicallyLocked),
        Lit: obj.Type == ObjectType.LightSource && (instance.StartsLit ?? obj.Light.StartsLit),
        Spent: false,
        Quantity: Math.Max(1, instance.Quantity),
        Facing: instance.Facing,
        Stage: obj.Type == ObjectType.Plant ? Math.Clamp(instance.StartStage ?? 0, 0, Math.Max(0, obj.Plant.Stages.Count - 1)) : 0,
        LiquidId: StartLiquid(obj, instance));

    /// <summary>How an object first looks when it isn't placed yet (a palette entry): its Object Editor starting state.</summary>
    public static ObjectState DefaultState(WorldObject obj) => new(
        Open: obj.Type switch
        {
            ObjectType.Door => obj.Door.StartsOpen,
            ObjectType.Window => obj.Window.HasShutters && obj.Window.StartsOpen,
            ObjectType.Lever => obj.Lever.Kind != LeverKind.Button && obj.Lever.StartsOn,
            _ => false
        },
        Locked: obj.Type == ObjectType.Door && obj.Door.StartsLocked,
        Lit: obj.Type == ObjectType.LightSource && obj.Light.StartsLit,
        Spent: false, Quantity: 1, Facing: SpriteDirection.South,
        Stage: obj.Type == ObjectType.Plant ? Math.Max(0, obj.Plant.Stages.Count - 1) : 0,
        LiquidId: StartLiquid(obj, null));
}
