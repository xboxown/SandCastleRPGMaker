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
    /// <summary>Flat, no gravity (it stays at its height). Not solid: on the floor, no collision; its events fire when someone steps on it, uses it, or drops something on it. Solid: a floor or a ceiling - walked on, and over someone's head it counts as being inside (Exult).</summary>
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
    Lockpick,

    // ---- Added in build 248 (appended, so saved projects keep their numbers) ----

    /// <summary>Solid like a wall, but you can see through it; may have shutters that open, and glass that breaks.</summary>
    Window,
    /// <summary>Grows through its stage pictures with the game clock; ripe, it can be harvested for produce.</summary>
    Plant,
    /// <summary>Exult's keyring: holds keys and opens whatever any of them fits.</summary>
    Keychain,
    /// <summary>Tells where you are in degrees (Exult's coordinates), and lets a map show your position.</summary>
    Sextant,
    /// <summary>Double-click to look at it: its picture fills the screen (with your position, if the party has a sextant).</summary>
    Map,
    /// <summary>Eaten by whoever you click (Exult's food): fills them up by its nutrition.</summary>
    Food,
    /// <summary>Drunk by whoever you click: heals, cures, poisons, puts to sleep... (its effects).</summary>
    Potion,
    /// <summary>Glass, vases, bottles: shatters when struck (or dropped), leaving shards if you like.</summary>
    BreakableGlass,
    /// <summary>A book of spells, by circle: cast with mana and reagents (Exult's spellbook).</summary>
    Spellbook,
    /// <summary>Runs its events and effects when used - magic scrolls, wands, one-off items; can have charges.</summary>
    UseableObject,
    /// <summary>A lever, switch or button: flips on and off, and works the doors, lights and other things it's linked to.</summary>
    Lever,
    /// <summary>Stairs you walk up a step at a time, or a ladder that takes you up or down to another spot.</summary>
    Stairs,
    /// <summary>No longer used (build 263): a solid Tile is a floor or a roof, and anything solid overhead counts as being inside, as Exult. Kept only so older projects load - such objects become solid Tiles.</summary>
    Roof,
    /// <summary>Double-click to see the time (Exult's pocketwatch); a sundial only by day.</summary>
    Clock,
    /// <summary>A hammer, scythe, shears...: used on plants and crafting stations that need it.</summary>
    Tool,
    /// <summary>An anvil, millstone, loom, oven...: turns ingredients into something new (its recipes).</summary>
    CraftingStation,
    /// <summary>A lute, harp, harpsichord: double-click to play a tune.</summary>
    MusicalInstrument,
    /// <summary>A powder keg: lit or struck, it blows up, hurting and breaking what's around it.</summary>
    Explosive,
    /// <summary>Laid out to sleep the night through: the party rests, heals and gets hungry.</summary>
    Bedroll,
    /// <summary>Spell ingredients (black pearl, garlic...): stack up to 100; spells use them up.</summary>
    Reagent,
    /// <summary>A table, counter, shelf or altar: solid, and things put on it rest on top.</summary>
    Table,
    /// <summary>A bucket, bottle or cauldron: filled at a source (a well, a trough) and drunk, poured or emptied.</summary>
    LiquidContainer
}

public static class ObjectTypes
{
    /// <summary>Every type, in the order the Object Editor lists them.</summary>
    public static readonly ObjectType[] All =
    {
        ObjectType.Junk, ObjectType.Tile, ObjectType.Wall, ObjectType.Door, ObjectType.LightSource, ObjectType.Ammo,
        ObjectType.Money, ObjectType.Chair, ObjectType.Bed, ObjectType.Trap, ObjectType.Key, ObjectType.Lockpick,
        ObjectType.Window, ObjectType.Plant, ObjectType.Keychain, ObjectType.Sextant, ObjectType.Map, ObjectType.Food,
        ObjectType.Potion, ObjectType.BreakableGlass, ObjectType.Spellbook, ObjectType.UseableObject, ObjectType.Lever,
        ObjectType.Stairs, ObjectType.Clock, ObjectType.Tool, ObjectType.CraftingStation,
        ObjectType.MusicalInstrument, ObjectType.Explosive, ObjectType.Bedroll, ObjectType.Reagent, ObjectType.Table,
        ObjectType.LiquidContainer
    };

    public static string Name(ObjectType type) => type switch
    {
        ObjectType.LightSource => "Light Source",
        ObjectType.Lockpick => "Picklock",
        ObjectType.BreakableGlass => "Breakable Glass",
        ObjectType.UseableObject => "Useable Object",
        ObjectType.Lever => "Lever / Switch / Button",
        ObjectType.Stairs => "Stairs / Ladder",
        ObjectType.CraftingStation => "Crafting Station",
        ObjectType.MusicalInstrument => "Musical Instrument",
        ObjectType.Table => "Table / Surface",
        ObjectType.LiquidContainer => "Liquid Container",
        _ => type.ToString()
    };

    /// <summary>Whether the type stacks into quantities with bundle pictures (Ammo, Money).</summary>
    public static bool IsBundle(ObjectType type) => type is ObjectType.Ammo or ObjectType.Money or ObjectType.Lockpick;

    /// <summary>Things that can't be broken by blows (no Durability): plants, food, potions, reagents, keys...</summary>
    public static bool CanBeBroken(ObjectType type) => type is not (ObjectType.Plant or ObjectType.Food or ObjectType.Potion or ObjectType.Reagent
        or ObjectType.Key or ObjectType.Lockpick or ObjectType.Keychain or ObjectType.Ammo or ObjectType.Money or ObjectType.Tile or ObjectType.Wall
        or ObjectType.Roof or ObjectType.Stairs or ObjectType.Sextant or ObjectType.Map or ObjectType.Spellbook);

    /// <summary>Always solid and drawn by Exult's wall rule (Game_object::compare): walls, doors and windows.</summary>
    public static bool IsWallLike(ObjectType type) => type is ObjectType.Wall or ObjectType.Door or ObjectType.Window;

    /// <summary>Types that stack into quantities (up to 100) without bundle pictures: reagents (and food, as Ultima VII's garlic).</summary>
    public static bool StacksByDefault(ObjectType type) => type is ObjectType.Reagent;

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
            case ObjectType.Lockpick:
                obj.IsStackable = true;
                obj.MaxStackSize = MaxQuantity;
                break;
            case ObjectType.Trap:
                obj.IsSolid = false;
                obj.SizeZ = 0;
                break;
            case ObjectType.Window:
                obj.IsPortable = false;
                obj.IsSolid = true;          // you can't climb through it
                obj.Occludes = false;        // but you can see through it
                obj.SizeZ = Math.Max(obj.SizeZ, 5);
                break;
            case ObjectType.Plant:
                obj.IsPortable = false;
                obj.IsSolid = false;
                break;
            case ObjectType.Keychain:
                obj.FramesAreStates = true;  // Exult: the keyring's frame shows how many keys (0-4)
                break;
            case ObjectType.BreakableGlass:
                obj.HitPoints = 1;           // one blow shatters it
                break;
            case ObjectType.Lever:
                obj.IsPortable = false;
                break;
            case ObjectType.Stairs:
                obj.IsPortable = false;
                obj.IsSolid = true;
                obj.SizeY = Math.Max(obj.SizeY, 4);
                obj.SizeZ = Math.Max(obj.SizeZ, 4); // four steps of 1 lift - Exult's stairs rise a lift a step
                break;
            case ObjectType.Roof:
                obj.IsPortable = false;
                obj.IsSolid = true;          // you can walk on a roof (stairs up to it)
                obj.SizeZ = 1;
                break;
            case ObjectType.CraftingStation:
                obj.IsPortable = false;
                obj.IsSolid = true;
                obj.SizeZ = Math.Max(obj.SizeZ, 2);
                break;
            case ObjectType.Explosive:
                obj.HitPoints = 1;
                obj.IsSolid = true;
                break;
            case ObjectType.Reagent:
                obj.IsStackable = true;
                obj.MaxStackSize = MaxQuantity;
                break;
            case ObjectType.Table:
                obj.IsPortable = false;
                obj.IsSolid = true;
                obj.AllowCellElevation = true; // things put on it rest on top
                obj.IsCellStackable = true;
                obj.SizeZ = Math.Max(obj.SizeZ, 2);
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

    // ---------- Bed: sleeping the hours away (Exult's rest: the clock jumps on) ----------
    /// <summary>Bed only: lying down, the player is asked how many hours to sleep (the slider, System Settings > Slider).</summary>
    public bool AsksSleepHours { get; set; } = true;
    public string SleepQuestion { get; set; } = "How many hours wilt thou sleep?";
    /// <summary>The slider's range for this bed (the slider allows at most 999).</summary>
    public int SleepMinHours { get; set; } = 1;
    public int SleepMaxHours { get; set; } = 12;
    public int SleepDefaultHours { get; set; } = 8;
    public int SleepStepHours { get; set; } = 1;
    /// <summary>0 hours on the slider (don't sleep): the sleeper says this and gets up ("" = says nothing).</summary>
    public string CancelSleepLine { get; set; } = "Perhaps later.";
    /// <summary>Its animation sounds in play, as a character pose's: muted, or at this volume (0-100).</summary>
    public bool Muted { get; set; }
    public int Volume { get; set; } = 100;

    /// <summary>Which facing's look and spots are really used for this facing (it, else South, else any with a sprite) - the sitter or sleeper faces that way too, so it lies exactly as shown in the Object Editor for that facing.</summary>
    public SpriteDirection? ResolveDirection(SpriteDirection facing)
    {
        if (Facings.TryGetValue(facing, out var exact) && exact.Sprite.IsSet) return facing;
        if (Facings.TryGetValue(SpriteDirection.South, out var south) && south.Sprite.IsSet) return SpriteDirection.South;
        foreach (var (direction, f) in Facings) if (f.Sprite.IsSet) return direction;
        return null;
    }

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
