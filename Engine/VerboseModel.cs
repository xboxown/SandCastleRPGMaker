namespace SandcastleRpgMaker.Engine;

/// <summary>The situations a character speaks up in (Character Editor > Verbose).</summary>
public enum VerboseKind
{
    Hungry,
    VeryHungry,
    Starving,
    Hurt,
    Fleeing,
    CallForHelp,
    TheftWarning,
    HeardNoise,
    WrongKey,
    LockpickFailed,
    Uneasy,
    Munching,
    WantsFood,
    FoodStolen,
    WaiterAsks,
    WaiterServes,
    CantSit,
    OutdoorsOnly,
    FeelingCold,
    VeryCold,
    Frostbite,
    Frozen,
    WarmingUp,
    FeelingHot,
    VeryHot,
    Heatstroke,
    Scorched,
    CoolingDown,
    PartyFull
}

/// <summary>
/// What a character says in each situation - its barks, the words floating
/// over its head. Defaults are Ultima VII's own lines, as Exult says them
/// (textmsg: hunger 0x77-0x7f, ouch 0x29-0x2c, flee 0x37 / 0x48-0x4e, help
/// 0x30-0x33 / 0x69-0x6b, theft 0x6e-0x70, heard something 0x95; the key
/// barks from Exult's keyring.uc). A situation never set uses the defaults;
/// one emptied says nothing.
/// </summary>
public class VerboseLines
{
    public Dictionary<VerboseKind, List<string>> Lines { get; set; } = new();

    public static IReadOnlyList<string> Defaults(VerboseKind kind) => kind switch
    {
        VerboseKind.Hungry => new[] { "I could use a little food.", "I am a bit hungry.", "When do we eat?" },
        VerboseKind.VeryHungry => new[] { "I am very hungry.", "I must eat now.", "I must have food." },
        VerboseKind.Starving => new[] { "I'm famished!", "I'm starving!", "I need food!" },
        VerboseKind.Hurt => new[] { "Ouch!", "Ooof!", "Argh!", "Eeow!" },
        VerboseKind.Fleeing => new[] { "Aiiieeee!", "Mercy!", "Pity me!", "Spare me!", "Let me be!", "I surrender!", "I give up!", "Please, no more!" },
        VerboseKind.CallForHelp => new[] { "Help!", "Save me!", "Guards!  Guards!", "To my side!", "Defend me!", "Protect me!" },
        VerboseKind.TheftWarning => new[] { "Do not touch that!", "Stop that, or else!", "Leave that be!" },
        VerboseKind.HeardNoise => new[] { "Did you hear something?", "What was that noise?" },
        VerboseKind.WrongKey => new[] { "The key doesn't fit.", "Maybe it is another key." },
        VerboseKind.LockpickFailed => new[] { "The lockpick broke!", "Blast, it snapped!" },
        VerboseKind.Uneasy => new[] { "I do not like this.", "Something is not right here.", "I feel uneasy..." },
        VerboseKind.Munching => new[] { "Mmmm!", "Delicious.", "*munch munch*", "Tasty!" },
        VerboseKind.WantsFood => new[] { "Where is my food?", "I am famished - bring me something!", "Food, please!", "Is anyone serving here?", "More food!" },
        VerboseKind.FoodStolen => new[] { "Hey! That was my food!", "Who took my supper?", "Thief! My food!" },
        VerboseKind.WaiterAsks => new[] { "What wouldst thou like?", "Hungry, art thou?", "May I bring thee something?", "What shall it be?", "Thou lookest hungry." },
        VerboseKind.WaiterServes => new[] { "Here thou art.", "Enjoy thy meal." },
        VerboseKind.CantSit => new[] { "I cannot get there.", "Something is in the way." },
        VerboseKind.OutdoorsOnly => new[] { "Not in here!", "I need open sky for that." },
        VerboseKind.FeelingCold => new[] { "It is chilly here.", "Brrr!", "It is cold!" },
        VerboseKind.VeryCold => new[] { "It is very cold!", "I am freezing!" },
        VerboseKind.Frostbite => new[] { "I cannot feel my fingers!", "I have frostbite!", "This cold is killing me!" },
        VerboseKind.Frozen => new[] { "I am frozen!", "I... cannot... move..." },
        VerboseKind.WarmingUp => new[] { "I am warming up.", "That is better." },
        VerboseKind.FeelingHot => new[] { "It is warm here.", "Phew!", "It is hot!" },
        VerboseKind.VeryHot => new[] { "It is very hot!", "I am sweating!" },
        VerboseKind.Heatstroke => new[] { "I feel faint!", "This heat is killing me!" },
        VerboseKind.Scorched => new[] { "I am burning up!", "I... cannot... breathe..." },
        VerboseKind.CoolingDown => new[] { "I am cooling down.", "That is better." },
        VerboseKind.PartyFull => new[] { "Thy party is full. I cannot join thee.", "There is no room for me with thee.", "Perhaps another time - thou hast enough companions." },
        _ => Array.Empty<string>()
    };

    public IReadOnlyList<string> Get(VerboseKind kind) => Lines.TryGetValue(kind, out var lines) ? lines : Defaults(kind);

    public static string Title(VerboseKind kind) => kind switch
    {
        VerboseKind.Hungry => "Hungry",
        VerboseKind.VeryHungry => "Very hungry",
        VerboseKind.Starving => "Starving",
        VerboseKind.Hurt => "Getting hit",
        VerboseKind.Fleeing => "Screaming in fear",
        VerboseKind.CallForHelp => "Calling for help",
        VerboseKind.TheftWarning => "Getting angry at theft (one line per warning, in order)",
        VerboseKind.HeardNoise => "Heard a noise",
        VerboseKind.WrongKey => "Wrong key",
        VerboseKind.LockpickFailed => "Lockpick broke",
        VerboseKind.Uneasy => "Not feeling safe",
        VerboseKind.Munching => "Eating",
        VerboseKind.WantsFood => "Asking for food (at the inn)",
        VerboseKind.FoodStolen => "Its food was taken",
        VerboseKind.WaiterAsks => "Waiter: taking an order",
        VerboseKind.WaiterServes => "Waiter: serving",
        VerboseKind.CantSit => "Can't reach a chair or bed",
        VerboseKind.OutdoorsOnly => "Using an outdoors-only object indoors",
        VerboseKind.FeelingCold => "Cold: starting to feel it",
        VerboseKind.VeryCold => "Cold: very cold",
        VerboseKind.Frostbite => "Cold: frostbite",
        VerboseKind.Frozen => "Cold: frozen",
        VerboseKind.WarmingUp => "Cold: warming up",
        VerboseKind.FeelingHot => "Heat: starting to feel it",
        VerboseKind.VeryHot => "Heat: very hot",
        VerboseKind.Heatstroke => "Heat: heatstroke",
        VerboseKind.Scorched => "Heat: burning up",
        VerboseKind.CoolingDown => "Heat: cooling down",
        VerboseKind.PartyFull => "Party is full (Ultima 8)",
        _ => kind.ToString()
    };

    public static string When(VerboseKind kind) => kind switch
    {
        VerboseKind.Hungry => "Each game hour while its food is at or below its Hungry level (Health tab) - half the time (Exult).",
        VerboseKind.VeryHungry => "Each game hour while its food is 4 or less - two times in three (Exult).",
        VerboseKind.Starving => "Each game hour while its food is 0 - three times in four (Exult).",
        VerboseKind.Hurt => "Hit and dropping below half its health - half the time (Exult's ouch).",
        VerboseKind.Fleeing => "When it starts running away. The first line is screamed three times in four, another line otherwise (Exult: \"Aiiieeee!\", or a plea).",
        VerboseKind.CallForHelp => "When it calls its friends to attack you: you hit it, or you kept stealing after its warnings.",
        VerboseKind.TheftWarning => "It sees you take what's its (or its side's): the 1st theft gets the 1st line, the 2nd the 2nd... after the last line, it calls for help and attacks. Exult warns up to three times with these three lines. An empty list: it attacks at once.",
        VerboseKind.HeardNoise => "Something of its side was taken nearby, but it didn't see who (half the time, Exult).",
        VerboseKind.WrongKey => "Said by a party member when a key that doesn't fit is tried (Exult's keyring).",
        VerboseKind.LockpickFailed => "Said by whoever was picking the lock when the lockpick breaks.",
        VerboseKind.Uneasy => "Not in Exult: now and then when it sees a fight it's not part of.",
        VerboseKind.Munching => "Eating a dish or its own food, three times in four (Exult's eat_at_inn munch lines - the original's words aren't in Exult's source, so these are ours).",
        VerboseKind.WantsFood => "Sitting at the inn with nothing in front of it, three times in four now and then (Exult's eat_at_inn \"more food\" lines).",
        VerboseKind.FoodStolen => "Not in Exult: the dish served to it, or its own food it was going to eat, was taken.",
        VerboseKind.WaiterAsks => "As a Waiter, at a customer's table, taking the order (Exult's waiter ask lines).",
        VerboseKind.WaiterServes => "As a Waiter, putting a dish down, two times in three (Exult's waiter serve lines).",
        VerboseKind.CantSit => "It was told to sit or lie down, but there's no way to walk up to the chair or bed.",
        VerboseKind.OutdoorsOnly => "It tried to use something that only works outdoors (a kite) under a roof.",
        VerboseKind.FeelingCold => "In a Cold zone without enough warmth worn, its temperature under 20 - a third of the game minutes (Exult's chilly, cold).",
        VerboseKind.VeryCold => "The same, 20 to 29 (Exult's colder).",
        VerboseKind.Frostbite => "The same, 30 to 59: from 50 it's hurt as it says so (Exult's frostbite).",
        VerboseKind.Frozen => "The same, 60 or more: hurt 1 to 3 as it says so (Exult's frozen).",
        VerboseKind.WarmingUp => "Out of the cold, or wearing enough warmth, as it recovers - a third of the game minutes.",
        VerboseKind.FeelingHot => "In a Hot zone without enough cooling worn, under 20.",
        VerboseKind.VeryHot => "The same, 20 to 29.",
        VerboseKind.Heatstroke => "The same, 30 to 59: from 50 it's hurt as it says so.",
        VerboseKind.Scorched => "The same, 60 or more: hurt 1 to 3 as it says so.",
        VerboseKind.CoolingDown => "Out of the heat, or wearing enough cooling, as it recovers.",
        VerboseKind.PartyFull => "Ultima 8 projects: Join Party when the party already has System Settings > Ultima 8 > The party's most members. It doesn't join.",
        _ => ""
    };
}
