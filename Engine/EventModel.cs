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
    End
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
    public List<BookEventPage> EventPages { get; set; } = new();
}
