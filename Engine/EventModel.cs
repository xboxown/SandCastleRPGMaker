namespace SandcastleRpgMaker.Engine;

/// <summary>
/// The kind of instruction a single EventCommand represents. Kept as an open
/// enum-plus-Parameters shape (RPG-Maker style) rather than one class per
/// command, so new command types are cheap to add later without touching
/// every consumer of the list.
/// </summary>
public enum EventCommandType
{
    ShowText,
    SetSwitch,
    SetVariable,
    ConditionalBranchSwitch,
    ConditionalBranchVariable,
    Else,
    EndBranch,
    Teleport,
    Wait,
    PlaySound,
    PlayMusic,
    ChangeTile,
    GiveItem,
    RemoveItem,
    Label,
    GoToLabel,
    ChangeHp,
    ShakeScreen,
    ChangeDayNight,
    ApplyPaletteColor,
    ChangeSelfBookContent,
    ChangeExternalBookContent,
    ChangeBookSettings,
    ChangeFactionRelationship,
    ChangeFaction,
    ChangeBehavior,
    ChangeFoodLevel,
    ModifyAilments,
    Attack,
    End,
    /// <summary>"When [reply]:" - the start of one reply's branch under a Show Text with replies (its commands are one level deeper). Added by the Show Text window, never on its own.</summary>
    WhenChoice,
    /// <summary>The end of a Show Text's reply branches.</summary>
    EndChoices,
    /// <summary>Stop Conversation: the conversation ends here - pinned replies no longer bring their box back, and once the lines already up are read the box closes for good. Commands after it still run (with no box up).</summary>
    StopConversation,
    /// <summary>Paperdoll: changes a paperdoll's parts (look, shown, value, lid, what's inside), plays or stops its sounds, closes it, or turns its object into another object (PaperDoll Editor).</summary>
    Paperdoll,
    /// <summary>Sleep On Bed: the main character walks to a bed and lies down on it, as double-clicking it does.</summary>
    SleepOnBed,
    /// <summary>Exchange Currency: a money changer changes the party's money of one currency into another (Currency Editor rates).</summary>
    ExchangeCurrency,
    /// <summary>Change Weather: rain, snow, hail, a storm or clear, and the clouds (Exult's weather eggs).</summary>
    ChangeWeather,
    /// <summary>Advance IF: a branch on any one game condition (its numbered tab), with Else / End If like Conditional Branch.</summary>
    AdvancedIf,
    /// <summary>Stop Sound: stops a sound that is playing (a looping Play Sound, say), or every sound.</summary>
    StopSound
}

/// <summary>
/// One instruction in an event's command list. Parameters are stored as a
/// generic string dictionary so the command set can grow without changing
/// the serialized shape - a new command type just defines which keys it
/// reads. IndentLevel mirrors RPG Maker's visual nesting for branches
/// (ConditionalBranch.../Else/EndBranch).
/// </summary>
public class EventCommand
{
    public EventCommandType Type { get; set; }
    public int IndentLevel { get; set; }
    public Dictionary<string, string> Parameters { get; set; } = new();

    public string Get(string key) => Parameters.TryGetValue(key, out var v) ? v : string.Empty;
}

/// <summary>
/// One "page" of commands as the SHARED Event Editor engine understands it -
/// deliberately generic (just a label + a command list), with no knowledge
/// of what conditions govern the page or which editor it belongs to. Every
/// consuming editor (Book now, NPC/Item later) adapts its own page type
/// into/out of this shape when attaching the shared engine control, keeping
/// the engine itself reusable rather than book-specific.
/// </summary>
public class EventCommandPage
{
    public string Label { get; set; } = "1";
    public List<EventCommand> Commands { get; set; } = new();
}

/// <summary>
/// A single event: where it lives, what starts it, and the command list that
/// runs. Events live on a MapData (or, later, inside a House/Transport's
/// internal mini-map, which is itself just a MapData).
/// </summary>
/// <summary>
/// A placed map event marker. Uses the exact same multi-page structure as
/// BookData.EventPages (a List of BookEventPage, each its own Conditions +
/// Commands) rather than a single Conditions/Commands pair - this is what
/// lets EventEditorControl (multi-page UI, New/Copy/Delete Event Page, all
/// of it) be reused completely unmodified, not rebuilt in a "single page
/// only" variant. Reusing BookEventConditions wholesale for Conditions is
/// deliberate and temporary ("duplicate for now... until further update or
/// notice") - some of its trigger types (By Pickup/Drop/After Read Item)
/// don't really make sense for a fixed map point yet, which is accepted,
/// known scope for today, not an oversight.
/// </summary>
public class GameEvent
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Event";
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>The floor (1-9) it's on: only someone on that floor steps on it.</summary>
    public int Floor { get; set; } = 1;
    public List<BookEventPage> EventPages { get; set; } = new();
}
