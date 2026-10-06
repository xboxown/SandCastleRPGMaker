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
    Uneasy
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
        _ => ""
    };
}
