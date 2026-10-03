using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Book Editor Preview's real implementation of IEventExecutionContext.
/// Play Sound genuinely resolves the real ResourceEntry and plays it via
/// the shared SoundPlayback helper. Switch/Variable storage is a plain
/// in-memory dictionary scoped to one Preview run - there's no real
/// running game yet to hold that state persistently, so this is honest
/// scaffolding, not a stand-in for a real save system.
///
/// Shake Screen and Change Day/Night are visual effects that only make
/// sense against a real window - this class has no window of its own, so
/// it exposes optional callbacks the host (BookPreviewWindow) wires up to
/// its own visuals. A caller with no callback set just gets a no-op,
/// exactly like every other optional dependency in this project.
/// </summary>
public class BookEventPreviewExecutionContext : IEventExecutionContext
{
    private readonly ResourceDatabase _resources;
    private readonly Dictionary<string, bool> _switches = new();
    private readonly Dictionary<string, double> _variables = new();
    /// <summary>Runtime-only content overrides, keyed by (which book, which page, which frame). Never touches the book's own authored data - this is exactly the "only during runtime, through the command" storage the spec calls for. Exposed via GetContentOverride for a real future engine to consult; the Book Editor's own Preview deliberately never reads this to change what it shows.</summary>
    private readonly Dictionary<(int BookId, int PageIndex, int FrameId), string> _contentOverrides = new();
    /// <summary>Runtime-only book SETTING overrides for Change Book Settings - keyed by (which book, which field name: "Name", "Weight", "IsStackable", etc), storing the new value as text. Same principle as _contentOverrides: never touches the book's own saved PlaceableItemBase fields, and there is no save-game system yet for this to persist into, so it resets whenever a new Game Test session starts.</summary>
    private readonly Dictionary<(int BookId, string Field), string> _bookSettingOverrides = new();

    public Action<int, double, bool>? OnShakeScreen { get; set; }
    public Action<string>? OnChangeDayNight { get; set; }
    public Action<string, double>? OnApplyPaletteColor { get; set; }
    public Action<string>? OnShowText { get; set; }
    /// <summary>Change Faction Relationship: (viewer faction, target faction, "Set"/"Add"/"Subtract", amount, both ways). Only a running game has factions to change.</summary>
    public Action<int, int, string, int, bool>? OnChangeFactionRelationship { get; set; }
    /// <summary>Change Faction: (character id or "Player", new faction id or -1) - false when there's no such character.</summary>
    public Func<string, int, bool>? OnChangeFaction { get; set; }
    /// <summary>Show Text as a real dialog box (speaker's face, position, background) - when set, used instead of OnShowText. False when it couldn't be shown (speaker out of sight).</summary>
    public Func<EventCommand, bool>? OnShowMessage { get; set; }
    /// <summary>Teleport: (who - "Activator", "Player" or a character id, cell x, cell y).</summary>
    public Func<string, int, int, bool>? OnTeleport { get; set; }
    /// <summary>Change Behavior: (character - "Activator" or an id, a schedule activity name or "Resume").</summary>
    public Func<string, string, bool>? OnChangeBehavior { get; set; }

    /// <summary>
    /// Which book "self" refers to for ChangeSelfBookContent - a plain
    /// mutable property, not constructor-only, because ONE shared context
    /// now lives for a whole Game Test session (not a fresh one per book
    /// like the Book Editor's own isolated Preview), and gets reused
    /// across many different books as the player interacts with each one.
    /// The caller sets this to the book actually being interacted with
    /// right before running its commands.
    /// </summary>
    public int SelfBookId { get; set; }

    public BookEventPreviewExecutionContext(ResourceDatabase resources, int selfBookId = -1)
    {
        _resources = resources;
        SelfBookId = selfBookId;
    }

    public bool Execute(EventCommand command)
    {
        switch (command.Type)
        {
            case EventCommandType.PlaySound:
            {
                if (!int.TryParse(command.Get("SoundResourceId"), out int id)) return false;
                double? volumeOverride = double.TryParse(command.Get("Volume"), out double v) ? v : null;
                return SoundPlayback.TryPlay(_resources, id, volumeOverride);
            }
            case EventCommandType.PlayMusic:
            {
                if (!int.TryParse(command.Get("MusicResourceId"), out int id)) return false;
                double? volumeOverride = double.TryParse(command.Get("Volume"), out double v) ? v : null;
                return SoundPlayback.TryPlay(_resources, id, volumeOverride);
            }
            case EventCommandType.SetSwitch:
            {
                string name = command.Get("SwitchName");
                if (string.IsNullOrEmpty(name)) return false;
                bool.TryParse(command.Get("Value"), out bool value);
                SetSwitch(name, value);
                return true;
            }
            case EventCommandType.SetVariable:
            {
                string name = command.Get("VariableName");
                if (string.IsNullOrEmpty(name) || !double.TryParse(command.Get("Value"), out double amount)) return false;
                double current = GetVariable(name);
                double result = command.Get("Operation") switch
                {
                    "Add" => current + amount,
                    "Subtract" => current - amount,
                    "Multiply" => current * amount,
                    "Divide" => amount != 0 ? current / amount : current, // divide by zero: leave the variable unchanged rather than produce Infinity/NaN that would silently poison every later comparison against it
                    "Modulo" => amount != 0 ? current % amount : current,
                    _ => amount // "Set"
                };
                SetVariable(name, result);
                return true;
            }
            case EventCommandType.ShakeScreen:
            {
                if (OnShakeScreen is null) return false;
                if (!int.TryParse(command.Get("Intensity"), out int intensity)) intensity = 5;
                if (!double.TryParse(command.Get("DurationSeconds"), out double duration)) duration = 1.0;
                bool affectsCharacters = command.Get("AffectsCharacters") == "True";
                OnShakeScreen(intensity, duration, affectsCharacters);
                return true;
            }
            case EventCommandType.ShowText when OnShowMessage is not null:
                return OnShowMessage(command);
            case EventCommandType.Teleport:
            {
                if (OnTeleport is null) return false;
                if (!int.TryParse(command.Get("X"), out int x) || !int.TryParse(command.Get("Y"), out int y)) return false;
                string who = command.Get("Who");
                return OnTeleport(string.IsNullOrEmpty(who) ? "Activator" : who, x, y);
            }
            case EventCommandType.ChangeBehavior:
            {
                if (OnChangeBehavior is null) return false;
                string who = command.Get("CharacterId"), behavior = command.Get("Behavior");
                return !string.IsNullOrEmpty(behavior) && OnChangeBehavior(string.IsNullOrEmpty(who) ? "Activator" : who, behavior);
            }
            case EventCommandType.ShowText:
            {
                if (OnShowText is null) return false;
                string text = command.Get("Text");
                if (string.IsNullOrEmpty(text)) return false;
                OnShowText(text);
                return true;
            }
            case EventCommandType.ChangeDayNight:
            {
                if (OnChangeDayNight is null) return false;
                OnChangeDayNight(command.Get("Period"));
                return true;
            }
            case EventCommandType.ApplyPaletteColor:
            {
                if (OnApplyPaletteColor is null) return false;
                string hex = command.Get("Hex");
                if (string.IsNullOrEmpty(hex)) return false;
                if (!int.TryParse(command.Get("OpacityPercent"), out int opacityPercent)) opacityPercent = 40;
                OnApplyPaletteColor(hex, opacityPercent / 100.0);
                return true;
            }
            case EventCommandType.ChangeSelfBookContent:
            case EventCommandType.ChangeExternalBookContent:
            {
                int bookId = command.Type == EventCommandType.ChangeSelfBookContent
                    ? SelfBookId
                    : (int.TryParse(command.Get("TargetBookId"), out int targetId) ? targetId : -1);
                if (bookId < 0) return false;
                if (!int.TryParse(command.Get("PageIndex"), out int pageIndex)) return false;
                if (!int.TryParse(command.Get("FrameId"), out int frameId)) return false;
                string replacement = command.Get("ReplacementXaml");
                if (string.IsNullOrEmpty(replacement)) return false;
                _contentOverrides[(bookId, pageIndex, frameId)] = replacement;
                return true;
            }
            case EventCommandType.ChangeFactionRelationship:
            {
                if (OnChangeFactionRelationship is null) return false;
                if (!int.TryParse(command.Get("ViewerFactionId"), out int viewer) || !int.TryParse(command.Get("TargetFactionId"), out int target)) return false;
                if (!int.TryParse(command.Get("Value"), out int amount)) return false;
                OnChangeFactionRelationship(viewer, target, command.Get("Operation"), amount, command.Get("BothWays") == "True");
                return true;
            }
            case EventCommandType.ChangeFaction:
            {
                if (OnChangeFaction is null) return false;
                if (!int.TryParse(command.Get("FactionId"), out int factionId)) return false;
                string who = command.Get("CharacterId");
                return !string.IsNullOrEmpty(who) && OnChangeFaction(who, factionId);
            }
            case EventCommandType.ChangeBookSettings:
            {
                if (!int.TryParse(command.Get("TargetBookId"), out int bookId)) return false;
                bool appliedAny = false;
                foreach (string field in BookSettingFields)
                {
                    if (command.Get("Override_" + field) != "True") continue;
                    _bookSettingOverrides[(bookId, field)] = command.Get("Value_" + field);
                    appliedAny = true;
                }
                return appliedAny;
            }
            default:
                return false;
        }
    }

    /// <summary>Every field Change Book Settings can override - the single list both Execute and GetBookSettingOverride key off of, so the two can never quietly drift apart.</summary>
    public static readonly string[] BookSettingFields =
    {
        "Name", "Weight", "Volume", "Price",
        "Destructible", "CanBeSold", "CanBeTraded", "CanBeDropped", "CanBePicked",
        "IsStackable", "AllowElevation"
    };

    /// <summary>What a real engine calls to find out whether a book's given setting has a runtime override waiting - e.g. GetBookSettingOverride(bookId, "Name"). Returns null when there isn't one, meaning "use the book's own real, saved value."</summary>
    public string? GetBookSettingOverride(int bookId, string field) =>
        _bookSettingOverrides.TryGetValue((bookId, field), out var value) ? value : null;

    /// <summary>Convenience for the common case of just reading a book's current effective Name/Weight/etc as a live-resolved string, without the caller needing to know or care whether an override is active.</summary>
    public string GetEffectiveBookSetting(BookData book, string field, string realValue) =>
        GetBookSettingOverride(book.Id, field) ?? realValue;

    /// <summary>What a real engine would call to find out whether this (book, page, frame) has a runtime replacement waiting. Returns null when there isn't one, meaning "use the book's own real content."</summary>
    public string? GetContentOverride(int bookId, int pageIndex, int frameId) =>
        _contentOverrides.TryGetValue((bookId, pageIndex, frameId), out var xaml) ? xaml : null;

    /// <summary>
    /// Resolves "self" vs "external" and every parameter exactly the same
    /// way Execute does, so this can never disagree with what Execute
    /// actually did. Checks the STORED CONTENT matches THIS command's own
    /// ReplacementXaml, not just "does something exist at this slot" -
    /// multiple commands can legitimately target the same (book, page,
    /// frame), and a same-slot check alone would say "Yes" for a later
    /// command that hasn't run yet, as long as an earlier one already
    /// wrote there. Comparing actual content is what makes this genuinely
    /// about THIS line, not just this slot.
    /// </summary>
    public bool HasContentOverrideFor(EventCommand command)
    {
        if (command.Type is not (EventCommandType.ChangeSelfBookContent or EventCommandType.ChangeExternalBookContent)) return false;

        int bookId = command.Type == EventCommandType.ChangeSelfBookContent
            ? SelfBookId
            : (int.TryParse(command.Get("TargetBookId"), out int targetId) ? targetId : -1);
        if (bookId < 0) return false;
        if (!int.TryParse(command.Get("PageIndex"), out int pageIndex)) return false;
        if (!int.TryParse(command.Get("FrameId"), out int frameId)) return false;

        return _contentOverrides.TryGetValue((bookId, pageIndex, frameId), out var stored)
            && stored == command.Get("ReplacementXaml");
    }

    public bool GetSwitch(string name) => _switches.TryGetValue(name, out var v) && v;
    public void SetSwitch(string name, bool value) => _switches[name] = value;
    public double GetVariable(string name) => _variables.TryGetValue(name, out var v) ? v : 0;
    public void SetVariable(string name, double value) => _variables[name] = value;
}
