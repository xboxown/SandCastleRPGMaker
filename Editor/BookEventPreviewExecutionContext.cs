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
public class BookEventPreviewExecutionContext : IEventExecutionContext, IDialogChoiceHost, IGameConditionContext, IActionCommandHost, ICinemaHost, IEventRunHost,
    IScriptDataHost, IScriptYieldHost, IScriptWaitHost, IScriptLibraryHost, IScriptGameFunctions
{
    /// <summary>Text variables, arrays, grids, self switches and the debug log (Engine/ScriptData.cs).</summary>
    public ScriptData ScriptData { get; } = new();
    /// <summary>Who owns the event music (Play Music): Stop / Fade Music and Resume Map Music stop it.</summary>
    public static readonly object MusicOwner = new();
    /// <summary>The game carries a long command list on next frame; without it (the editor's preview) a list stops after its steps for one go.</summary>
    public Action<Action>? OnDefer { get; set; }
    /// <summary>Wait Until: the game checks the condition every frame, then goes on (after the timeout in seconds, 0 = never).</summary>
    public Action<Func<bool>, double, Action>? OnWaitUntil { get; set; }
    /// <summary>The game's own formula functions (hp(Player), hour()...): null when it has no such function.</summary>
    public Func<string, IReadOnlyList<object>, object?>? OnGameFunction { get; set; }

    public void Defer(Action goOn)
    {
        if (OnDefer is not null) OnDefer(goOn);
        else ScriptData.AddLog("(a long loop stopped here - in the preview it runs only so many steps; it runs on in the game)");
    }

    public void WaitUntil(Func<bool> condition, double timeoutSeconds, Action goOn)
    {
        if (OnWaitUntil is not null) OnWaitUntil(condition, timeoutSeconds, goOn);
        else goOn(); // the preview doesn't wait
    }

    public CommonEventDefinition? GetCommonEvent(int id) => ProjectSession.SettingsProvider?.Invoke()?.CommonEvents.FirstOrDefault(e => e.Id == id) ?? CommonEvents?.FirstOrDefault(e => e.Id == id);
    /// <summary>The Common Events when there's no project session (a built game sets them from its settings).</summary>
    public List<CommonEventDefinition>? CommonEvents { get; set; }

    public bool TryCall(string name, IReadOnlyList<object> args, out object result)
    {
        result = 0.0;
        if (OnGameFunction?.Invoke(name, args) is not { } value) return false;
        result = value;
        return true;
    }

    private readonly ResourceDatabase _resources;
    private readonly Dictionary<string, double> _variables = new();
    /// <summary>Runtime-only content overrides, keyed by (which book, which page, which frame). Never touches the book's own authored data - this is exactly the "only during runtime, through the command" storage the spec calls for. Exposed via GetContentOverride for a real future engine to consult; the Book Editor's own Preview deliberately never reads this to change what it shows.</summary>
    private readonly Dictionary<(int BookId, int PageIndex, int FrameId), string> _contentOverrides = new();
    /// <summary>Runtime-only book SETTING overrides for Change Book Settings - keyed by (which book, which field name: "Name", "Weight", "IsStackable", etc), storing the new value as text. Same principle as _contentOverrides: never touches the book's own saved PlaceableItemBase fields, and there is no save-game system yet for this to persist into, so it resets whenever a new Game Test session starts.</summary>
    private readonly Dictionary<(int BookId, string Field), string> _bookSettingOverrides = new();

    /// <summary>A saved game: the variables, switches and book overrides as they are now.</summary>
    public void SaveState(GameSaveData save)
    {
        save.Variables = new Dictionary<string, double>(_variables);
        save.Script = ScriptData.ToSave();
        save.BookContents = _contentOverrides.Select(kv => new SavedBookContent { BookId = kv.Key.BookId, PageIndex = kv.Key.PageIndex, FrameId = kv.Key.FrameId, Text = kv.Value }).ToList();
        save.BookSettings = _bookSettingOverrides.Select(kv => new SavedBookSetting { BookId = kv.Key.BookId, Field = kv.Key.Field, Value = kv.Value }).ToList();
    }

    /// <summary>A saved game being loaded: its variables, switches and book overrides back.</summary>
    public void LoadState(GameSaveData save)
    {
        _variables.Clear();
        foreach (var (name, value) in save.Variables) _variables[name] = value;
        ScriptData.Load(save.Script);
        _contentOverrides.Clear();
        foreach (var c in save.BookContents) _contentOverrides[(c.BookId, c.PageIndex, c.FrameId)] = c.Text;
        _bookSettingOverrides.Clear();
        foreach (var s in save.BookSettings) _bookSettingOverrides[(s.BookId, s.Field)] = s.Value;
    }

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
    /// <summary>Show Text with replies: put the box up and call back with the picked reply's Id. Only a running game can ask; without it the first reply is taken.</summary>
    public Func<EventCommand, List<DialogChoice>, Action<int>, bool>? OnAskChoice { get; set; }

    public bool CanAskChoice => OnAskChoice is not null;
    public bool AskChoice(EventCommand showText, List<DialogChoice> choices, Action<int> chosen) => OnAskChoice?.Invoke(showText, choices, chosen) ?? false;
    /// <summary>Stop Conversation: wait for the boxes up now to be read, then call back.</summary>
    public Func<Action, bool>? OnWaitUntilRead { get; set; }
    public bool WaitUntilRead(Action goOn) => OnWaitUntilRead?.Invoke(goOn) ?? false;
    /// <summary>Teleport: (who - "Activator", "Player" or a character id, cell x, cell y).</summary>
    public Func<string, int, int, bool>? OnTeleport { get; set; }
    /// <summary>Teleport with its floor and map (the game): the whole command; when set, used instead of OnTeleport.</summary>
    public Func<EventCommand, bool>? OnTeleportCommand { get; set; }
    /// <summary>Change Behavior: (character - "Activator" or an id, a schedule activity name or "Resume").</summary>
    public Func<string, string, bool>? OnChangeBehavior { get; set; }
    /// <summary>Food Level: (who, "Set"/"Add"/"Subtract", amount).</summary>
    public Func<string, string, int, bool>? OnChangeFood { get; set; }
    /// <summary>Modify Ailments: (who, ailment name, true = inflict / false = cure).</summary>
    public Func<string, string, bool, bool>? OnModifyAilment { get; set; }
    /// <summary>Attack: (attacker, target) - each "Activator", "Player" or a character id.</summary>
    public Func<string, string, bool>? OnAttack { get; set; }
    /// <summary>Paperdoll: the whole command (the game knows which paperdoll is running it).</summary>
    public Func<EventCommand, bool>? OnPaperdoll { get; set; }
    /// <summary>Sleep On Bed: ("Under" or "Nearest").</summary>
    public Func<string, bool>? OnSleepOnBed { get; set; }
    /// <summary>Exchange Currency: (from currency id, to currency id, "All" or a number).</summary>
    public Func<int, int, string, bool>? OnExchangeCurrency { get; set; }
    /// <summary>Change Weather: (kind, intensity 1-100, clouds on / off / unchanged).</summary>
    public Func<WeatherKind, int, bool?, bool>? OnChangeWeather { get; set; }
    /// <summary>Advance IF: the game tests the condition (the editor's preview can't: false).</summary>
    public Func<GameCondition, bool>? OnEvaluateCondition { get; set; }
    public bool EvaluateGameCondition(GameCondition condition) => OnEvaluateCondition?.Invoke(condition) ?? false;
    /// <summary>The commands only a running game can do (Give Item, Change Stat, Train...): the game puts its own handler here for each; without one (the editor's preview) they're logged as not done.</summary>
    public Dictionary<EventCommandType, Func<EventCommand, bool>> GameHandlers { get; } = new();
    /// <summary>The commands that take time (Walk To Location, Sit, Pickup Item...): the game starts one and calls back when it's done. Returns false if it failed at once.</summary>
    public Dictionary<EventCommandType, Func<EventCommand, Action<bool>, bool>> GameActions { get; } = new();
    /// <summary>Whether the last command that can fail worked (Advance IF > Last command succeeded).</summary>
    public bool LastResult { get; set; } = true;

    /// <summary>Cinema Mode: (the running list, true = begin / false = end). Only a running game has controls to take.</summary>
    public Action<object, bool>? OnCinema { get; set; }
    public void BeginCinema(object run) => OnCinema?.Invoke(run, true);
    public void EndCinema(object run) => OnCinema?.Invoke(run, false);

    /// <summary>An event's run is over (GameTestWindow: the characters it moved go back to their schedules).</summary>
    public Action<object>? OnRunEnded { get; set; }
    public void RunEnded(object run) => OnRunEnded?.Invoke(run);

    /// <summary>The game remembers now who set the event off, and the action it returns runs as them, however much later.</summary>
    public Func<Action, Action>? OnKeepWhoSetItOff { get; set; }

    public bool TryBegin(EventCommand command, Action<bool> done)
    {
        if (!GameActions.TryGetValue(command.Type, out var start)) return false;
        bool once = false;
        bool result = false;
        Action goOn = () => done(result);
        goOn = OnKeepWhoSetItOff?.Invoke(goOn) ?? goOn;
        void Finish(bool ok)
        {
            if (once) return;
            once = true;
            LastResult = ok;
            result = ok;
            goOn();
        }
        if (!start(command, Finish)) Finish(false);
        return true;
    }

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
                double? volumeOverride = double.TryParse(command.Get("Volume"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double v)
                    || double.TryParse(command.Get("Volume"), out v) ? Math.Clamp(v, 0, 1) : null;
                return SoundPlayback.TryPlay(_resources, id, volumeOverride, loop: command.Get("Loop") == "True");
            }
            case EventCommandType.StopSound:
            {
                // "All" stops every sound playing; otherwise one sound (every copy of it, looping ones too).
                if (command.Get("SoundResourceId") == "All") { SoundPlayback.StopAll(); return true; }
                if (!int.TryParse(command.Get("SoundResourceId"), out int id)) return false;
                SoundPlayback.StopSound(id);
                return true;
            }
            case EventCommandType.PlayMusic:
            {
                if (!int.TryParse(command.Get("MusicResourceId"), out int id)) return false;
                double? volumeOverride = double.TryParse(command.Get("Volume"), out double v) ? v : null;
                SoundPlayback.StopFor(MusicOwner); // one piece of event music at a time
                return SoundPlayback.TryPlayFor(MusicOwner, _resources, id, loop: command.Get("Loop") == "True", volumeOverride);
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
                if (OnTeleportCommand is not null) return OnTeleportCommand(command);
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
            case EventCommandType.ChangeFoodLevel:
            {
                if (OnChangeFood is null || !int.TryParse(command.Get("Value"), out int amount)) return false;
                string who = command.Get("Who");
                return OnChangeFood(string.IsNullOrEmpty(who) ? "Activator" : who, command.Get("Operation"), amount);
            }
            case EventCommandType.ChangeWeather:
                return OnChangeWeather is not null && Enum.TryParse<WeatherKind>(command.Get("Kind"), out var weatherKind)
                    && OnChangeWeather(weatherKind, int.TryParse(command.Get("Intensity"), out int weatherIntensity) ? weatherIntensity : 50,
                        command.Get("Clouds") switch { "True" => true, "False" => false, _ => null });
            case EventCommandType.ExchangeCurrency:
                return OnExchangeCurrency is not null && int.TryParse(command.Get("FromId"), out int fromId) && int.TryParse(command.Get("ToId"), out int toId)
                    && OnExchangeCurrency(fromId, toId, command.Get("Amount"));
            case EventCommandType.SleepOnBed:
                return OnSleepOnBed is not null && OnSleepOnBed(command.Get("Which"));
            case EventCommandType.Paperdoll:
                return OnPaperdoll is not null && OnPaperdoll(command);
            case EventCommandType.ModifyAilments:
            {
                if (OnModifyAilment is null) return false;
                string who = command.Get("Who");
                return OnModifyAilment(string.IsNullOrEmpty(who) ? "Activator" : who, command.Get("Ailment"), command.Get("Action") != "Cure");
            }
            case EventCommandType.Attack:
            {
                if (OnAttack is null) return false;
                string attacker = command.Get("Attacker"), target = command.Get("Target");
                return OnAttack(string.IsNullOrEmpty(attacker) ? "Activator" : attacker, string.IsNullOrEmpty(target) ? "Player" : target);
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
                if (!GameHandlers.TryGetValue(command.Type, out var handler)) return false;
                LastResult = handler(command);
                return LastResult;
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

    // A switch is a Boolean variable (Control Switches picks one): one value, so Control Switches,
    // Control Variables, Conditional Branch and every "In A Condition" (reply conditions too) agree.
    public bool GetSwitch(string name) => GetVariable(name) != 0;
    public void SetSwitch(string name, bool value) => _variables[name] = value ? 1 : 0;
    public double GetVariable(string name) => _variables.TryGetValue(name, out var v) ? v : 0;
    /// <summary>Every number variable and switch set so far (Game Test's Script Data window).</summary>
    public IReadOnlyDictionary<string, double> Variables => _variables;
    public void SetVariable(string name, double value) => _variables[name] = value;
    /// <summary>Whether anything has set this variable yet (a paperdoll control then shows it, rather than its own start).</summary>
    public bool HasVariable(string name) => _variables.ContainsKey(name);
}
