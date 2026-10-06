namespace SandcastleRpgMaker.Engine;

/// <summary>
/// What an object is. Chosen once in the Object Editor, then fixed - it
/// decides which settings the object has and what double-clicking it does.
/// (To change it, delete the object and make a new one.)
/// </summary>
public enum ObjectType
{
    /// <summary>Picked up, carried, identified - does nothing when used, unless its events do.</summary>
    Junk,
    /// <summary>Flat, on the floor, no collision. Its events fire when someone steps on it, uses it, or drops something on it.</summary>
    Tile,
    /// <summary>Always solid, can't be moved; drawn in front of or behind whoever walks by it exactly as Exult does.</summary>
    Wall,
    /// <summary>Opens and closes; can be locked (key, lockpick); NPCs open unlocked doors in their way and close them behind them.</summary>
    Door,
    /// <summary>Lit and put out by double-clicking; lights up the night; can burn out ("Spent ...").</summary>
    LightSource,
    /// <summary>Stacks of up to 100, drawn by how many there are (Exult's quantity frames).</summary>
    Ammo,
    /// <summary>Stacks of up to 100, drawn by how many there are; worth its currency's value.</summary>
    Money,
    /// <summary>Somewhere to sit: one or more seats, facing the chair's direction.</summary>
    Chair,
    /// <summary>Somewhere to sleep: one or more places to lie, with a cover drawn over the sleeper.</summary>
    Bed,
    /// <summary>Hurts (and can poison, sleep or paralyze) whoever steps on it.</summary>
    Trap,
    /// <summary>Locks and unlocks the containers and doors it is the key of.</summary>
    Key,
    /// <summary>Tries to pick a lock; breaks when it fails.</summary>
    Lockpick
}

public static class ObjectTypes
{
    /// <summary>Every type, in the order the Object Editor lists them.</summary>
    public static readonly ObjectType[] All =
    {
        ObjectType.Junk, ObjectType.Tile, ObjectType.Wall, ObjectType.Door, ObjectType.LightSource, ObjectType.Ammo,
        ObjectType.Money, ObjectType.Chair, ObjectType.Bed, ObjectType.Trap, ObjectType.Key, ObjectType.Lockpick
    };

    public static string Name(ObjectType type) => type switch
    {
        ObjectType.LightSource => "Light Source",
        ObjectType.Lockpick => "Picklock",
        _ => type.ToString()
    };

    /// <summary>Whether the type stacks into quantities with bundle pictures (Ammo, Money).</summary>
    public static bool IsBundle(ObjectType type) => type is ObjectType.Ammo or ObjectType.Money;

    /// <summary>
    /// Exult's quantity frames (objs.cc modify_quantity, "Verified"): 1 to 6
    /// each have their own picture, 7-12 share one, 13 and up share the last.
    /// Returns the bundle level 0-7 for a quantity.
    /// </summary>
    public static int BundleLevel(int quantity) => quantity > 12 ? 7 : quantity > 6 ? 6 : Math.Max(0, quantity - 1);

    /// <summary>What each bundle level stands for, for editors and guides.</summary>
    public static readonly string[] BundleLevelNames = { "1", "2", "3", "4", "5", "6", "7 - 12", "13 or more" };

    /// <summary>Exult's MAX_QUANTITY: the most one stack can hold.</summary>
    public const int MaxQuantity = 100;

    /// <summary>Sensible starting settings for a type the moment it is chosen (the designer can change them).</summary>
    public static void ApplyDefaults(WorldObject obj)
    {
        switch (obj.Type)
        {
            case ObjectType.Tile:
                obj.IsPortable = false;      // "Too heavy" ticked by default
                obj.IsSolid = false;         // no collision at all
                obj.Occludes = false;
                obj.SizeZ = 0;               // flat, at floor level
                break;
            case ObjectType.Wall:
                obj.IsPortable = false;
                obj.IsSolid = true;          // always an obstacle
                obj.Occludes = true;
                obj.SizeZ = Math.Max(obj.SizeZ, 5); // a storey high (Exult: 5 lifts a floor)
                break;
            case ObjectType.Door:
                obj.IsPortable = false;
                obj.IsSolid = true;          // while closed
                obj.Occludes = true;
                obj.SizeZ = Math.Max(obj.SizeZ, 5);
                obj.HitPoints = 6;           // Exult: a door without a key has 6 HP
                break;
            case ObjectType.Bed:
                obj.IsPortable = false;
                obj.IsSolid = true;
                break;
            case ObjectType.Chair:
                obj.IsSolid = true;
                break;
            case ObjectType.Ammo:
            case ObjectType.Money:
                obj.IsStackable = true;
                obj.MaxStackSize = MaxQuantity;
                break;
            case ObjectType.Trap:
                obj.IsSolid = false;
                obj.SizeZ = 0;
                break;
        }
    }
}

/// <summary>A sprite chosen from the Sprite Editor (group + name). Empty name = none.</summary>
public class SpriteLink
{
    public string SpriteGroup { get; set; } = string.Empty;
    public string SpriteName { get; set; } = string.Empty;
    public bool IsSet => !string.IsNullOrEmpty(SpriteName);
}

/// <summary>Door settings (Ultima VII / Exult doors).</summary>
public class DoorSettings
{
    /// <summary>How it looks open. (The object's own sprite is how it looks closed.)</summary>
    public SpriteLink OpenSprite { get; set; } = new();
    /// <summary>Where the open sprite is drawn, in pixels from where the closed one is (so it swings on its hinge, not its middle).</summary>
    public int OpenOffsetX { get; set; }
    public int OpenOffsetY { get; set; }
    /// <summary>How it looks closed and locked (optional - empty: like closed).</summary>
    public SpriteLink LockedSprite { get; set; } = new();

    public bool StartsOpen { get; set; }
    public bool StartsLocked { get; set; }
    /// <summary>The Key object that locks and unlocks it (-1 = none: only a lockpick or force opens it).</summary>
    public int RequiredKeyItemId { get; set; } = -1;
    public int PickLockDifficulty { get; set; } = 10;
    /// <summary>Exult's magically locked doors (frame % 4 == 3): no key or lockpick opens them, and they can't be broken.</summary>
    public bool MagicallyLocked { get; set; }

    public int OpenSoundResourceId { get; set; } = -1;
    public int CloseSoundResourceId { get; set; } = -1;
    public int UnlockSoundResourceId { get; set; } = -1;
    public int LockSoundResourceId { get; set; } = -1;
    public int WrongKeySoundResourceId { get; set; } = -1;
    public int LockpickBrokeSoundResourceId { get; set; } = -1;
    public int LockedRattleSoundResourceId { get; set; } = -1;
}

/// <summary>Light source settings: lit and spent looks, how bright, how long it burns.</summary>
public class LightSettings
{
    /// <summary>How it looks lit (usually an animation). The object's own sprite is how it looks unlit.</summary>
    public SpriteLink LitSprite { get; set; } = new();
    /// <summary>How it looks burnt out. Empty: like unlit.</summary>
    public SpriteLink SpentSprite { get; set; } = new();
    public bool StartsLit { get; set; }
    /// <summary>Exult light_data brightness: a candle is 1, a sconce 5, a lamp 7.</summary>
    public int Brightness { get; set; } = 5;
    /// <summary>Game minutes it burns before it's spent; 0 = never burns out.</summary>
    public int BurnMinutes { get; set; }
    public int LitSoundResourceId { get; set; } = -1;
    public int OffSoundResourceId { get; set; } = -1;
    public int SpentSoundResourceId { get; set; } = -1;
}

/// <summary>Ammo and money: the eight bundle pictures (Exult's quantity frames), and what each is for.</summary>
public class BundleSettings
{
    /// <summary>Eight pictures, by bundle level: 1, 2, 3, 4, 5, 6, 7-12, 13+ (ObjectTypes.BundleLevel). Empty ones use the nearest smaller one that's set.</summary>
    public List<SpriteLink> Levels { get; set; } = Enumerable.Range(0, 8).Select(_ => new SpriteLink()).ToList();
}

/// <summary>Ammo settings (Exult's ammo info).</summary>
public class AmmoSettings
{
    /// <summary>The paperdoll slot it goes into when picked up (empty: the Backpack/Pouch, then the hands).</summary>
    public EquipmentSlot? PreferredEquipSlot { get; set; }
    public string? PreferredCustomSlotName { get; set; }
    /// <summary>Which weapons can fire it (Exult's ammo family), e.g. "Arrows", "Bolts".</summary>
    public string Family { get; set; } = string.Empty;
    /// <summary>Extra damage it adds to a hit.</summary>
    public int DamageBonus { get; set; }
    public bool Poisons { get; set; }
    public bool Explodes { get; set; }
    /// <summary>The sound it makes when it hits.</summary>
    public int HitSoundResourceId { get; set; } = -1;
}

/// <summary>Money settings: which currency it is.</summary>
public class MoneySettings
{
    public int CurrencyId { get; set; } = -1;
}

/// <summary>One place to sit or lie on a chair or bed: where the sitter's feet go, in the sprite's own pixels from its top-left.</summary>
public class SeatSpot
{
    public double X { get; set; }
    public double Y { get; set; }
}

/// <summary>The look and the seats of a chair or bed facing one direction.</summary>
public class FurnitureFacing
{
    public SpriteLink Sprite { get; set; } = new();
    public List<SeatSpot> Spots { get; set; } = new();
}

/// <summary>Chairs and beds: one look per facing (like a character pose), each with its own seats or sleeping places.</summary>
public class FurnitureSettings
{
    public Dictionary<SpriteDirection, FurnitureFacing> Facings { get; set; } = new();
    /// <summary>Bed only: the Junk object laid over the bed as its cover - frame 1 made, frame 2 turned down over a sleeper.</summary>
    public int CoverObjectId { get; set; } = -1;
    public int SitSoundResourceId { get; set; } = -1;
    /// <summary>Its animation sounds in play, as a character pose's: muted, or at this volume (0-100).</summary>
    public bool Muted { get; set; }
    public int Volume { get; set; } = 100;

    /// <summary>The facing's settings, falling back to the nearest one that has a sprite.</summary>
    public FurnitureFacing? Resolve(SpriteDirection facing)
    {
        if (Facings.TryGetValue(facing, out var exact) && exact.Sprite.IsSet) return exact;
        if (Facings.TryGetValue(SpriteDirection.South, out var south) && south.Sprite.IsSet) return south;
        return Facings.Values.FirstOrDefault(f => f.Sprite.IsSet);
    }
}

public enum TrapAilment { None, Poisoned, Asleep, Paralyzed }

/// <summary>Trap settings: what it does to whoever steps on it.</summary>
public class TrapSettings
{
    public int DamageMin { get; set; } = 1;
    public int DamageMax { get; set; } = 5;
    public TrapAilment Ailment { get; set; } = TrapAilment.None;
    /// <summary>Hidden until it goes off (then shown).</summary>
    public bool Hidden { get; set; }
    /// <summary>Goes off once, then does nothing more.</summary>
    public bool OneShot { get; set; }
    /// <summary>Seconds before it can go off again (not one-shot).</summary>
    public double RearmSeconds { get; set; } = 3;
    public int TriggerSoundResourceId { get; set; } = -1;
}

/// <summary>A kind of money (Currency Editor): gold coins, silver pieces, gems... worth Value of the base unit each.</summary>
public class Currency
{
    public int Id { get; set; }
    public string Name { get; set; } = "Gold";
    /// <summary>What one is worth in the base unit (the first currency is usually 1).</summary>
    public double Value { get; set; } = 1;
}

public class CurrencyDatabase
{
    public List<Currency> Currencies { get; set; } = new();
    public int GetNextId() => Currencies.Count == 0 ? 1 : Currencies.Max(c => c.Id) + 1;
}
