namespace SandcastleRpgMaker.Engine;

// Settings for the object types added in build 248 (see ObjectTypes.cs).
// Each type's behaviour follows Exult's own item usecode wherever Exult has
// it (content/bgkeyring/src/items/*.uc and the engine's intrinsics); the
// GameTestWindow partial files that run them cite their source.

/// <summary>What a potion, a food, a drink, a spell or a useable object does to whoever it's used on.</summary>
public enum MagicEffectKind
{
    /// <summary>Health back: Min..Max HP, never above the maximum (Strength).</summary>
    Heal,
    /// <summary>Mana back: Min..Max, never above the maximum (Magic).</summary>
    RestoreMana,
    CurePoison,
    Poison,
    /// <summary>Asleep 5-10 seconds (Exult's Npc_sleep_timer).</summary>
    Sleep,
    /// <summary>Wakes a sleeper.</summary>
    Awaken,
    /// <summary>Paralyzed 60-120 seconds (Exult's Npc_flag_timer).</summary>
    Paralyze,
    CureParalysis,
    Curse,
    RemoveCurse,
    /// <summary>Exult's protection: +3 to defence for 60-80 seconds (actors.cc figure_hit_points).</summary>
    Protection,
    /// <summary>Exult's invisibility for 60-80 seconds: enemies that can't see invisible stop attacking (Combat_schedule::stop_attacking_invisible).</summary>
    Invisibility,
    /// <summary>Exult's light spell (add_special_light): the night is lit as by many lights for Min..Max game minutes.</summary>
    Light,
    /// <summary>Min..Max damage.</summary>
    Damage,
    /// <summary>Food: Min..Max nutrition.</summary>
    Nourish,
    /// <summary>A custom effect (Object Editor > Add Custom Stat): its own event commands, run on whoever it's used on.</summary>
    Custom,
    // ---- Spells (build 377), after Exult's: appended, so saved projects keep their numbers ----
    /// <summary>The caster (the party with the main character) is at the spot clicked at once.</summary>
    Teleport,
    /// <summary>Remembers where the caster stands, for Recall.</summary>
    Mark,
    /// <summary>Back to the caster's Mark.</summary>
    Recall,
    /// <summary>Min..Max copies of a character (Thing) appear by the spot, on the caster's side, for Seconds.</summary>
    Summon,
    /// <summary>Min..Max of an object (Thing) for the caster - Create Food, Create Gold.</summary>
    CreateItem,
    /// <summary>An object (Thing: a fire field, a poison field) on the spot - every cell of the spell's reach - for Seconds.</summary>
    CreateField,
    /// <summary>The door or chest clicked is unlocked - magically locked ones too (Unlock Magic).</summary>
    MagicUnlock,
    /// <summary>The door or chest clicked is locked.</summary>
    MagicLock,
    /// <summary>Lights within reach go out (Great Douse).</summary>
    DouseLights,
    /// <summary>Lights within reach are lit (Great Ignite).</summary>
    IgniteLights,
    /// <summary>Hidden traps within reach are seen (Detect Trap).</summary>
    RevealTraps,
    /// <summary>Traps within reach are gone (Destroy Trap).</summary>
    DestroyTraps,
    /// <summary>The thing clicked is used from afar, as a double-click (a lever across a chasm).</summary>
    Telekinesis,
    /// <summary>Charmed: fights for the other side a while.</summary>
    Charm,
    /// <summary>Runs from the caster for Seconds (Cause Fear).</summary>
    Fear,
    /// <summary>Dances for Seconds, whatever it was doing (Dance).</summary>
    Dance,
    /// <summary>The dead body clicked (or every body within reach) lives again.</summary>
    Resurrect,
    /// <summary>A copy of the one it's cast on appears, on the caster's side, for Seconds (Clone).</summary>
    Clone,
    /// <summary>The invisible are seen again (Reveal).</summary>
    RevealInvisible,
    /// <summary>Everyone but the party stands frozen for Seconds (Time Stop).</summary>
    TimeStop,
    /// <summary>The ground shakes (Tremor) - Min..Max is how hard; add Damage for the hurt.</summary>
    Tremor,
    /// <summary>Tells where the caster stands (Locate).</summary>
    Locate,
    // ---- build 453: appended ----
    /// <summary>Fields within reach of the spot are gone (Ultima VII's Dispel Field): those Create Field made - and, with an object chosen, any of that object there.</summary>
    DestroyField,
    /// <summary>A Common Event (System Settings > Common Events) runs on whoever it's used on ("Whoever set it off") - a spell of the designer's own, scripted with any commands.</summary>
    CommonEvent,
    /// <summary>Whoever it's cast on rises up to 3 floors (under a ceiling: till their head touches it) and walks in the air for Seconds, then falls onto whatever is under them. Party members it isn't cast on stay on the ground.</summary>
    Levitate,
    /// <summary>For Seconds every language (System Settings > Languages) reads plainly, as if the party knew it (Translate).</summary>
    Translate
}

/// <summary>Who a spell with a reach touches.</summary>
public enum SpellAffects { Everyone, Foes, Friends }

public class MagicEffect
{
    public MagicEffectKind Kind { get; set; } = MagicEffectKind.Heal;
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 1;
    /// <summary>Kind Custom: which custom effect (ObjectDatabase.CustomEffects).</summary>
    public int CustomEffectId { get; set; } = -1;
    /// <summary>Summon: the character; Create Item, Create Field: the object.</summary>
    public int ThingId { get; set; } = -1;
    /// <summary>How long it lasts (real seconds): summoned creatures, fields, fear, dance, time stop, clones.</summary>
    public double Seconds { get; set; } = 60;
    /// <summary>Kind CommonEvent: which Common Event runs.</summary>
    public int CommonEventId { get; set; } = -1;

    public static bool NeedsCharacter(MagicEffectKind kind) => kind == MagicEffectKind.Summon;
    public static bool NeedsObject(MagicEffectKind kind) => kind is MagicEffectKind.CreateItem or MagicEffectKind.CreateField;
    public static bool HasSeconds(MagicEffectKind kind) => kind is MagicEffectKind.Summon or MagicEffectKind.CreateField or MagicEffectKind.Fear or MagicEffectKind.Dance or MagicEffectKind.TimeStop or MagicEffectKind.Clone or MagicEffectKind.Levitate or MagicEffectKind.Translate;

    /// <summary>Levitate: how high it lifts (3 floors).</summary>
    public const int LevitateLifts = 3 * MapFloors.LiftsPerFloor;

    /// <summary>A built-in effect's name as the editor lists it ("Restore Mana", "Cure Poison").</summary>
    public static string KindName(MagicEffectKind kind) => kind switch
    {
        MagicEffectKind.RestoreMana => "Restore Mana",
        MagicEffectKind.CurePoison => "Cure Poison",
        MagicEffectKind.CureParalysis => "Cure Paralysis",
        MagicEffectKind.RemoveCurse => "Remove Curse",
        MagicEffectKind.CreateItem => "Create Item",
        MagicEffectKind.CreateField => "Create Field",
        MagicEffectKind.MagicUnlock => "Magic Unlock",
        MagicEffectKind.MagicLock => "Magic Lock",
        MagicEffectKind.DouseLights => "Douse Lights",
        MagicEffectKind.IgniteLights => "Ignite Lights",
        MagicEffectKind.RevealTraps => "Reveal Traps",
        MagicEffectKind.DestroyTraps => "Destroy Traps",
        MagicEffectKind.RevealInvisible => "Reveal Invisible",
        MagicEffectKind.TimeStop => "Time Stop",
        MagicEffectKind.DestroyField => "Destroy Field",
        MagicEffectKind.CommonEvent => "Common Event (own spell)",
        _ => kind.ToString()
    };

    public static string Describe(MagicEffect e) => e.Kind switch
    {
        MagicEffectKind.Heal => $"Heal {Range(e)} HP",
        MagicEffectKind.RestoreMana => $"Restore {Range(e)} mana",
        MagicEffectKind.CurePoison => "Cure poison",
        MagicEffectKind.Poison => "Poison",
        MagicEffectKind.Sleep => "Put to sleep",
        MagicEffectKind.Awaken => "Awaken",
        MagicEffectKind.Paralyze => "Paralyze",
        MagicEffectKind.CureParalysis => "Cure paralysis",
        MagicEffectKind.Curse => "Curse",
        MagicEffectKind.RemoveCurse => "Remove curse",
        MagicEffectKind.Protection => "Protection (+3 defence, about a minute)",
        MagicEffectKind.Invisibility => "Invisibility (about a minute)",
        MagicEffectKind.Light => $"Light ({Range(e)} game minutes)",
        MagicEffectKind.Damage => $"Damage {Range(e)}",
        MagicEffectKind.Nourish => $"Nourish {Range(e)}",
        MagicEffectKind.Custom => "Custom (its own event commands)",
        MagicEffectKind.Teleport => "To the spot clicked",
        MagicEffectKind.Mark => "Marks where the caster stands",
        MagicEffectKind.Recall => "Back to the caster's mark",
        MagicEffectKind.Summon => $"{Range(e)} appear for {e.Seconds} s",
        MagicEffectKind.CreateItem => $"{Range(e)} for the caster",
        MagicEffectKind.CreateField => $"on the spot for {e.Seconds} s",
        MagicEffectKind.MagicUnlock => "The door or chest clicked",
        MagicEffectKind.MagicLock => "The door or chest clicked",
        MagicEffectKind.DouseLights => "Lights in reach go out",
        MagicEffectKind.IgniteLights => "Lights in reach are lit",
        MagicEffectKind.Telekinesis => "Uses the thing clicked from afar",
        MagicEffectKind.Charm => "Charmed (fights for the other side)",
        MagicEffectKind.Fear => $"Runs away for {e.Seconds} s",
        MagicEffectKind.Dance => $"Dances for {e.Seconds} s",
        MagicEffectKind.Resurrect => "The body clicked lives again",
        MagicEffectKind.Clone => $"A copy fights for the caster {e.Seconds} s",
        MagicEffectKind.TimeStop => $"Everyone else frozen {e.Seconds} s",
        MagicEffectKind.Tremor => $"The ground shakes ({Range(e)})",
        MagicEffectKind.Locate => "Where the caster stands",
        MagicEffectKind.DestroyField => "Fields in reach are gone",
        MagicEffectKind.CommonEvent => "Its Common Event runs on them",
        MagicEffectKind.Levitate => $"Up to 3 floors, walking in the air, {e.Seconds} s",
        MagicEffectKind.Translate => $"Every language reads plainly for {e.Seconds} s",
        _ => KindName(e.Kind)
    };

    /// <summary>Whether the amount (Min..Max) means anything for this effect.</summary>
    public static bool HasAmount(MagicEffectKind kind) => kind is MagicEffectKind.Heal or MagicEffectKind.RestoreMana or MagicEffectKind.Light or MagicEffectKind.Damage or MagicEffectKind.Nourish
        or MagicEffectKind.Summon or MagicEffectKind.CreateItem or MagicEffectKind.Tremor;

    private static string Range(MagicEffect e) => e.Min == e.Max ? e.Min.ToString() : $"{e.Min}-{e.Max}";
}

/// <summary>A custom effect (Object Editor > Add Custom Stat): a name, and the event commands it runs on whoever it's used on (as the Activator).</summary>
public class CustomEffect
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Custom Stat";
    public List<EventCommand> Commands { get; set; } = new();
}

/// <summary>A window: its own sprite is how it looks closed (or, without shutters, always).</summary>
public class WindowSettings
{
    /// <summary>Shutters: double-click opens and closes them.</summary>
    public bool HasShutters { get; set; }
    public SpriteLink OpenSprite { get; set; } = new();
    public bool StartsOpen { get; set; }
    /// <summary>Closed shutters block sight.</summary>
    public bool ClosedBlocksSight { get; set; } = true;
    /// <summary>How it looks once its glass is broken (it then stays, broken). Empty: a broken window is gone.</summary>
    public SpriteLink BrokenSprite { get; set; } = new();
    public int OpenSoundResourceId { get; set; } = -1;
    public int CloseSoundResourceId { get; set; } = -1;
}

/// <summary>A plant: stage pictures it grows through, and what it gives when harvested.</summary>
public class PlantSettings
{
    /// <summary>Growth stages, first (a seedling) to last (ripe). The object's own sprite is used for any stage left empty.</summary>
    public List<SpriteLink> Stages { get; set; } = new() { new(), new(), new() };
    /// <summary>Game hours from one stage to the next (0 = it never grows).</summary>
    public double HoursPerStage { get; set; } = 24;
    /// <summary>The object it gives when harvested (-1 = nothing: a decoration).</summary>
    public int ProduceObjectId { get; set; } = -1;
    public int ProduceQuantity { get; set; } = 1;
    /// <summary>A Tool of this kind is needed to harvest it (e.g. "Scythe"); empty = by hand (double-click).</summary>
    public string HarvestToolKind { get; set; } = string.Empty;
    /// <summary>Only the last stage can be harvested.</summary>
    public bool OnlyWhenRipe { get; set; } = true;
    /// <summary>The stage it goes back to after a harvest (0 = the first).</summary>
    public int StageAfterHarvest { get; set; }
    /// <summary>Harvesting takes the plant itself (a cabbage, a pumpkin).</summary>
    public bool RemovedWhenHarvested { get; set; }
    public int HarvestSoundResourceId { get; set; } = -1;
    /// <summary>Sowing: this seed (an object) dropped on this soil (an object) makes the soil this plant, at its first stage. -1 = not sown.</summary>
    public int SeedObjectId { get; set; } = -1;
    /// <summary>The soil it's sown in - and, when harvesting takes the whole plant, what it leaves behind, ready to sow again.</summary>
    public int SoilObjectId { get; set; } = -1;
}

/// <summary>Exult's keyring (keyring.uc): the keys it starts with; its frame shows how many it holds (0, 1, 2, 3, 4 or more).</summary>
public class KeychainSettings
{
    public List<int> StartingKeyIds { get; set; } = new();
    public int AddKeySoundResourceId { get; set; } = -1;
}

/// <summary>A sextant (Exult's coordinates, cheat_screen.cc: (tile - origin) / 10, North/South and East/West).</summary>
public class SextantSettings
{
    /// <summary>The map cell that is 0 North/South, 0 East/West (-1 = the middle of the map).</summary>
    public int OriginCellX { get; set; } = -1;
    public int OriginCellY { get; set; } = -1;
    /// <summary>Exult tiles (8 pixels) per degree: Ultima VII uses 10.</summary>
    public int TilesPerDegree { get; set; } = 10;
}

/// <summary>A map: the picture shown when it's used (Exult's display_map).</summary>
public class MapSettings
{
    public SpriteLink Picture { get; set; } = new();
    /// <summary>Its paperdoll: the art the map is shown within (a parchment, a scroll's rollers, a frame). Empty: the map picture alone.</summary>
    public SpriteLink PaperdollSprite { get; set; } = new();
    /// <summary>Where the map picture sits inside the paperdoll, in the paperdoll's own pixels (0 = a 10% border all round).</summary>
    public int PictureLeft { get; set; }
    public int PictureTop { get; set; }
    public int PictureWidth { get; set; }
    public int PictureHeight { get; set; }
    /// <summary>Played when it's opened (unrolled).</summary>
    public int OpenSoundResourceId { get; set; } = -1;
    /// <summary>Mark where the party is - Exult does when someone in the party carries a sextant and they're outdoors.</summary>
    public bool ShowLocation { get; set; } = true;
    /// <summary>Exult: the mark needs a sextant in the party. Unticked: always shown (outdoors).</summary>
    public bool LocationNeedsSextant { get; set; } = true;
    /// <summary>Exult: no mark while inside (under a roof - a house, a cave with a ceiling). Unticked: marked inside too (a map of a dungeon).</summary>
    public bool OutdoorsOnly { get; set; } = true;
    /// <summary>The part of the game map the picture shows, in cells (-1 = the whole map): a map of one cave, maze or house. Outside it, there's no mark.</summary>
    public int AreaLeft { get; set; } = -1;
    public int AreaTop { get; set; } = -1;
    public int AreaRight { get; set; } = -1;
    public int AreaBottom { get; set; } = -1;
}

/// <summary>
/// Food (Exult's Food shape#0x179 / consumeEdible). Solid food is eaten;
/// liquid food (milk, water, wine, blood) is what liquid containers hold:
/// it says what the liquid does when drunk, and each container says how it
/// looks full of it (LiquidContainerSettings).
/// </summary>
public class FoodSettings
{
    /// <summary>A liquid (milk, water, wine): carried in liquid containers, filled at sources (a well, a cow).</summary>
    public bool IsLiquid { get; set; }
    /// <summary>A liquid that douses lit lights and fires (Exult's bucket of water).</summary>
    public bool PutsOutFires { get; set; }
    /// <summary>A liquid nobody drinks ("No, thank thee." - Exult's bucket of blood).</summary>
    public bool Undrinkable { get; set; }
    /// <summary>How much it fills (Exult: an apple 2, bread 4, a ham 24). Food level runs 0-31; at 25 or more they're full.</summary>
    public int Nutrition { get; set; } = 4;
    public int EatSoundResourceId { get; set; } = -1;
    /// <summary>Anything else it does (poisoned food, a healing herb).</summary>
    public List<MagicEffect> Effects { get; set; } = new();
}

/// <summary>A potion: its effects on whoever drinks it.</summary>
public class PotionSettings
{
    public List<MagicEffect> Effects { get; set; } = new() { new() { Kind = MagicEffectKind.Heal, Min = 5, Max = 10 } };
    public int DrinkSoundResourceId { get; set; } = -1;
    /// <summary>What's left once drunk (an empty bottle object; -1 = nothing).</summary>
    public int EmptyObjectId { get; set; } = -1;
}

/// <summary>Breakable glass: shatters when struck (its hit points), maybe when dropped too.</summary>
public class GlassSettings
{
    /// <summary>Dropped onto the floor (or falling) from this many lifts or more, it shatters (0 = never by dropping).</summary>
    public int BreaksFromLifts { get; set; } = 2;
    /// <summary>What it leaves behind when it shatters (shards, a junk object; -1 = nothing).</summary>
    public int ShardsObjectId { get; set; } = -1;
}

/// <summary>One spell in a spellbook (Exult Spellbook_object::can_do_spell / do_spell).</summary>
public class SpellDefinition
{
    public string Name { get; set; } = "New Spell";
    /// <summary>0 = Linear, 1-8 = the circles. Exult: casting needs Level at least the circle, and costs the circle in mana.</summary>
    public int Circle { get; set; } = 1;
    /// <summary>Mana it costs; -1 = its circle (Exult).</summary>
    public int ManaCost { get; set; } = -1;
    /// <summary>The words of power said while casting ("In Lor").</summary>
    public string Words { get; set; } = string.Empty;
    /// <summary>The caster clicks who (or what) it's cast on; unticked: it's cast on the caster.</summary>
    public bool NeedsTarget { get; set; }
    /// <summary>Reagents: one of each is used up (Exult).</summary>
    public List<int> ReagentObjectIds { get; set; } = new();
    /// <summary>Reagents and how many of each are used up (4 garlic, 2 spider silk). Older projects' ReagentObjectIds (one of each) count until this is filled.</summary>
    public List<RecipeInput> Reagents { get; set; } = new();

    /// <summary>Every reagent it needs, with how many: Reagents, else the older one-of-each list.</summary>
    public IEnumerable<RecipeInput> AllReagents() => Reagents.Count > 0
        ? Reagents.Where(r => r.ObjectId >= 0)
        : ReagentObjectIds.Distinct().Select(id => new RecipeInput { ObjectId = id, Quantity = 1 });
    public List<MagicEffect> Effects { get; set; } = new();
    public int CastSoundResourceId { get; set; } = -1;
    /// <summary>Its picture on the page of the book (Exult's spell icons).</summary>
    public SpriteLink Icon { get; set; } = new();
    /// <summary>A missile (a Projectile object) flies from the caster to the target, and the spell works where it lands (Fire Blast, Lightning, Death Bolt). -1 = straight there.</summary>
    public int MissileObjectId { get; set; } = -1;
    /// <summary>Everyone within this many tiles of the target (or of the caster, cast on no one) - Mass Cure, Explosion. 0 = the one target.</summary>
    public int RadiusTiles { get; set; }
    /// <summary>With a reach: everyone, foes only, or friends only.</summary>
    public SpellAffects Affects { get; set; } = SpellAffects.Everyone;
    /// <summary>Played over each one it touches (or on the spot).</summary>
    public SpriteLink Animation { get; set; } = new();

    public int EffectiveManaCost => ManaCost >= 0 ? ManaCost : Math.Max(0, Circle);
}

public class SpellbookSettings
{
    public List<SpellDefinition> Spells { get; set; } = new();
    /// <summary>Ticked: every copy of the book knows all its spells. Unticked: a copy knows only those it learned (Learn Spell) - Exult's spellbooks.</summary>
    public bool StartsWithAllSpells { get; set; } = true;
    /// <summary>Who can read it (characters' ids); empty = anyone who can cast magic (Character Editor).</summary>
    public List<int> AllowedCharacterIds { get; set; } = new();
    /// <summary>The open book, both pages (Exult's spellbook gump). Not set: a plain book is drawn.</summary>
    public SpriteLink OpenBookSprite { get; set; } = new();
    /// <summary>The ribbon marking the last spell chosen (Exult's bookmark).</summary>
    public SpriteLink BookmarkSprite { get; set; } = new();
    public int PageTurnSoundResourceId { get; set; } = -1;
}

/// <summary>A useable object: charges, what it does and how it looks used up.</summary>
public class UseableSettings
{
    /// <summary>How many times it can be used (0 = no limit).</summary>
    public int Charges { get; set; }
    /// <summary>Used up, it's gone (a scroll); unticked: it stays, showing its Used-up sprite (a wand).</summary>
    public bool RemovedWhenUsedUp { get; set; } = true;
    public SpriteLink UsedUpSprite { get; set; } = new();
    /// <summary>Its effects go on a target you click (a magic scroll), not on you.</summary>
    public bool AsksForTarget { get; set; }
    public List<MagicEffect> Effects { get; set; } = new();
    public int UseSoundResourceId { get; set; } = -1;
}

public enum LeverKind { Lever, Switch, Button }

/// <summary>A lever, switch or button. Each placed one is linked (Map Editor, right-click > Link) to the doors, lights and other things it works.</summary>
public class LeverSettings
{
    public LeverKind Kind { get; set; } = LeverKind.Lever;
    /// <summary>How it looks on (pulled, pressed). Its own sprite is off.</summary>
    public SpriteLink OnSprite { get; set; } = new();
    public bool StartsOn { get; set; }
    /// <summary>A button springs back after this many seconds.</summary>
    public double ButtonResetSeconds { get; set; } = 2;
    public int OnSoundResourceId { get; set; } = -1;
    public int OffSoundResourceId { get; set; } = -1;
}

public enum StairsKind { Stairs, Ladder }

/// <summary>Stairs rise a lift a step (Exult's stairs, climbed by its walking rule); a ladder takes you to its destination (Map Editor, right-click).</summary>
public class StairsSettings
{
    public StairsKind Kind { get; set; } = StairsKind.Stairs;
    /// <summary>Stairs: walking this way goes up.</summary>
    public SpriteDirection RisesToward { get; set; } = SpriteDirection.North;
    public int ClimbSoundResourceId { get; set; } = -1;
    /// <summary>Stairs: the Staircase Designer design it is built from (-1 = none: its own sprite, cut into steps by its footprint).</summary>
    public int DesignId { get; set; } = -1;
}

/// <summary>A roof: how high it sits (Exult: a storey is 5 lifts).</summary>
public class RoofSettings
{
    public int HeightLifts { get; set; } = 5;
}

/// <summary>A clock (Exult's pocketwatch: " 3:05pm" over it) or a sundial (by daylight only).</summary>
public class ClockSettings
{
    public bool Sundial { get; set; }
}

/// <summary>A tool, by kind: plants and crafting stations ask for a kind ("Scythe", "Hammer").</summary>
public class ToolSettings
{
    public string ToolKind { get; set; } = "Hammer";
    public int UseSoundResourceId { get; set; } = -1;
}

public class RecipeInput
{
    public int ObjectId { get; set; } = -1;
    public int Quantity { get; set; } = 1;
}

/// <summary>One thing a crafting station makes (Exult's millstone: wheat -> flour, beside the millstone, after the work).</summary>
public class CraftingRecipe
{
    public string Name { get; set; } = "New Recipe";
    /// <summary>What it takes - carried by the party, used up.</summary>
    public List<RecipeInput> Inputs { get; set; } = new();
    /// <summary>A Tool of this kind must be used on the station (empty = double-click the station).</summary>
    public string ToolKind { get; set; } = string.Empty;
    public int OutputObjectId { get; set; } = -1;
    public int OutputQuantity { get; set; } = 1;
    /// <summary>How long the work takes (Exult's millstone: three turns, about 2.4 seconds).</summary>
    public double WorkSeconds { get; set; } = 2.4;
    public int WorkSoundResourceId { get; set; } = -1;
}

public class CraftingSettings
{
    public List<CraftingRecipe> Recipes { get; set; } = new();
    /// <summary>How it looks while working (an animation). Empty: as always.</summary>
    public SpriteLink WorkingSprite { get; set; } = new();
    /// <summary>Said by the party when it's used with nothing to make (Exult: "Try grinding wheat upon this millstone.").</summary>
    public string Hint { get; set; } = string.Empty;
}

public class InstrumentSettings
{
    /// <summary>The tunes it plays (one picked each time).</summary>
    public List<int> SongSoundIds { get; set; } = new();
    /// <summary>The player's pose while playing (a Character Editor pose; empty = as they stand).</summary>
    public string PlayingPose { get; set; } = string.Empty;
}

/// <summary>An explosive (Ultima VII's powder keg).</summary>
public class ExplosiveSettings
{
    public bool LitByDoubleClick { get; set; } = true;
    public bool ExplodesWhenStruck { get; set; } = true;
    public double FuseSeconds { get; set; } = 3;
    /// <summary>How far the blast reaches, in Exult tiles (8 pixels).</summary>
    public int RadiusTiles { get; set; } = 5;
    public int DamageMin { get; set; } = 8;
    public int DamageMax { get; set; } = 20;
    /// <summary>How it looks with its fuse burning.</summary>
    public SpriteLink LitSprite { get; set; } = new();
    /// <summary>The blast, played once where it was.</summary>
    public SpriteLink ExplosionSprite { get; set; } = new();
    public int FuseSoundResourceId { get; set; } = -1;
    public int ExplosionSoundResourceId { get; set; } = -1;
}

/// <summary>A bedroll (Exult's rest, advanceTimeDueToRest).</summary>
public class BedrollSettings
{
    public int SleepHours { get; set; } = 8;
    /// <summary>Sleep until 6 in the morning instead of for Sleep Hours.</summary>
    public bool WakeAtDawn { get; set; }
    /// <summary>How it looks laid out while slept on (its own sprite: rolled up).</summary>
    public SpriteLink LaidOutSprite { get; set; } = new();
}

/// <summary>One liquid a container can hold: which liquid (a Food object set to Liquid), and how this container looks full of it.</summary>
public class LiquidDefinition
{
    /// <summary>The liquid - a Food object set to Liquid (what drinking it does is set there).</summary>
    public int FoodId { get; set; } = -1;
    /// <summary>How this container looks full of it (a steel bucket of milk, a glass bottle of wine).</summary>
    public SpriteLink Sprite { get; set; } = new();
}

/// <summary>A liquid container (Exult's bucket.uc): empty (its own sprite) or full of one of the liquids it can hold.</summary>
public class LiquidContainerSettings
{
    /// <summary>The liquids it can hold, each with its own full look. One not listed can't go in it.</summary>
    public List<LiquidDefinition> Liquids { get; set; } = new();
    /// <summary>The liquid (Food id) it starts full of; -1 = it starts empty.</summary>
    public int StartsFullOfId { get; set; } = -1;
    public int FillSoundResourceId { get; set; } = -1;
    public int PourSoundResourceId { get; set; } = -1;
    /// <summary>What a spill leaves on the floor (a puddle object; -1 = nothing).</summary>
    public int SpillObjectId { get; set; } = -1;
    /// <summary>Gone once poured into another container (a bottle thrown away); otherwise it stays, empty.</summary>
    public bool UsedUpWhenPouredOut { get; set; }
}
