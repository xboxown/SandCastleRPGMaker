namespace SandcastleRpgMaker.Engine;

/// <summary>
/// Ultima 6 Game Test's debugging notes: why a step was refused, a step up or down, a vehicle with no room to turn, the test
/// key's clock skip. They help the maker find what's wrong with a map (a tile not walkable, a thing too tall); a played game
/// never says them. Each can be shown in the text area or not, and its words changed (System Settings > Debugging, Ultima 6 only).
/// {0}, {1}, {2} are filled in by the engine - Blanks says with what.
/// </summary>
public static class U6DebugNotes
{
    public sealed record Note(string Id, string Default, string Meaning, (string Blank, string FilledWith)[] Blanks);

    public static readonly Note[] All =
    {
        new("EdgeOfMap", "Blocked: edge of the map.",
            "The step would go past the edge of the map.", Array.Empty<(string, string)>()),
        new("NotWalkable", "Blocked: the ground there isn't walkable.",
            "The tile there has Walkable off (Tile Properties) or is set to Blocks Movement.", Array.Empty<(string, string)>()),
        new("RisesTooHigh", "Blocked: \"{0}\" rises {1}px above where you stand (lift {2}) - you can only step up 4px (1 lift) at a time.",
            "Something in the way is taller than a walker can step up onto. Lower it (Object Editor height) or move it.",
            new[] { ("{0}", "the name of the thing in the way"), ("{1}", "how many pixels it rises above the walker's feet"), ("{2}", "the lift (height level) the walker stands on") }),
        new("DropTooFar", "Blocked: that's a drop of {0}px - you can only step down 4px (1 lift) at a time.",
            "The ground ahead is too far below the walker to step down to.",
            new[] { ("{0}", "how many pixels down the drop is") }),
        new("DropLifts", "Blocked: a drop of {0} lifts - a walker steps down only {1} at a time.",
            "Where the walker would stand is too far below where it stands now.",
            new[] { ("{0}", "how many lifts (height levels) down"), ("{1}", "the most lifts a walker steps down at once") }),
        new("HeadHeight", "Blocked: \"{0}\" is in the way at head height - you need {1}px of headroom.",
            "Something hangs too low over the square for the walker to fit under it.",
            new[] { ("{0}", "the name of the thing in the way"), ("{1}", "the headroom the walker needs, in pixels") }),
        new("Blocked", "Blocked.",
            "Refused, with no more said (a step the engine tries for the party or a planned walk).", Array.Empty<(string, string)>()),
        new("NoRoomToFly", "Blocked: no room to fly there.",
            "A flyer's way is full.", Array.Empty<(string, string)>()),
        new("SeaCreature", "Blocked: a sea creature stays in the water.",
            "A swimmer won't leave the water.", Array.Empty<(string, string)>()),
        new("ShipOnWater", "Blocked: a ship sails only on water.",
            "A ship tried to go onto land.", Array.Empty<(string, string)>()),
        new("VehicleGround", "Blocked: it can't go over that ground.",
            "A land vehicle (a cart, a horse) can't go over that ground: not walkable, or water.", Array.Empty<(string, string)>()),
        new("VehicleThingInWay", "Blocked: {0} is in the way.",
            "Something stands where the vehicle would go.",
            new[] { ("{0}", "the name of the thing in the way") }),
        new("VehicleYouInWay", "Blocked: you are in the way.",
            "The player stands where the vehicle would go.", Array.Empty<(string, string)>()),
        new("NoRoomToTurn", "No room to turn: {0}",
            "A vehicle can't turn: its new facing would not fit.",
            new[] { ("{0}", "why: the words of the Blocked note that stopped it, without \"Blocked:\"") }),
        new("SteppedUp", "Stepped up to lift {0} ({1}px).",
            "The walker went up onto something (a step, a platform).",
            new[] { ("{0}", "the lift (height level) it stands on now"), ("{1}", "that height in pixels") }),
        new("SteppedDown", "Stepped down to lift {0} ({1}px).",
            "The walker went down to something lower.",
            new[] { ("{0}", "the lift (height level) it stands on now"), ("{1}", "that height in pixels") }),
        new("SkippedTo", "Skipped to {0} ({1} period).",
            "The test key (F11, or F6) moved the clock on to the next period, to check where characters go by their schedules.",
            new[] { ("{0}", "the day and time now"), ("{1}", "the schedule period it is now") }),
    };

    private static readonly Dictionary<string, Note> ById = All.ToDictionary(n => n.Id);
    public static Note? Find(string id) => ById.TryGetValue(id, out var n) ? n : null;

    /// <summary>The note's words (the maker's, or the standard ones) with its blanks filled in.</summary>
    public static string Format(Ultima6Settings u6, string id, params object[] values)
    {
        string text = u6.DebugNoteWords.TryGetValue(id, out var own) && own.Length > 0 ? own : Find(id)?.Default ?? id;
        for (int i = 0; i < values.Length; i++) text = text.Replace("{" + i + "}", values[i]?.ToString() ?? "");
        return text;
    }

    /// <summary>Said in the text area while testing (ticked in System Settings > Debugging).</summary>
    public static bool Shown(Ultima6Settings u6, string id) => !u6.HiddenDebugNotes.Contains(id);
}
