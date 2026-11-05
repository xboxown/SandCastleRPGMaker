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
    StopSound,
    /// <summary>Change Stat: set, raise or lower one stat of someone - for ever, or for some game minutes / hours.</summary>
    ChangeStat,
    /// <summary>Reset Stat: takes back every Change Stat on a stat (or all stats); training and levels are kept.</summary>
    ResetStat,
    /// <summary>Train: a trainer raises stats for training points and money (Ultima 7's trainers).</summary>
    Train,
    /// <summary>Kill Character: a fatal blow 0.05 s later - the hit flash - and death.</summary>
    KillCharacter,
    // ---- Actions: they take time; the commands after them wait till they're done (or cancelled) ----
    /// <summary>Pickup Item: walks to a thing on the map and picks it up (as if dragged onto them).</summary>
    PickupItem,
    /// <summary>Drop Item: puts something they carry (or wear) down at their feet, or at a spot.</summary>
    DropItem,
    /// <summary>Equip Equipment: readies a weapon or armor they carry in its slot.</summary>
    EquipItem,
    /// <summary>Unequip Equipment: takes it off into their pack (or drops it).</summary>
    UnequipItem,
    /// <summary>Play Pose: shows a pose for a time, or until its animation has played once.</summary>
    PlayPose,
    /// <summary>Walk To Location: walks to a cell, round whatever is in the way.</summary>
    WalkToLocation,
    /// <summary>Change Pose: shows a pose (and facing) and keeps it.</summary>
    ChangePose,
    /// <summary>Attack Object: walks to a thing and strikes it until it breaks (gives up after a minute).</summary>
    AttackObject,
    /// <summary>Play Animation: a sprite (its animation and sounds) played at a spot.</summary>
    PlayAnimation,
    /// <summary>Sit: walks to a chair and sits on it.</summary>
    Sit,
    /// <summary>Stand: gets up from a chair or bed.</summary>
    Stand,
    /// <summary>Open Door: walks to a door and opens it - unlocking it first with their key.</summary>
    OpenDoor,
    /// <summary>Picklock Door: walks to a locked door and picks the lock, lockpick after lockpick.</summary>
    PicklockDoor,
    /// <summary>Pick Item From Container: walks to an unlocked container and takes things out of it.</summary>
    TakeFromContainer,
    /// <summary>Cinema Mode: from here to its End Cinema Mode the player can't move or click anything in the world - only read the boxes and pick replies.</summary>
    CinemaMode,
    /// <summary>The end of a Cinema Mode: the player has control again (so does the event's end, wherever it ends).</summary>
    EndCinemaMode,
    /// <summary>Walk To Floor Location: walks to a cell on a given floor - up or down the stairs to it, round whatever is in the way.</summary>
    WalkToFloorLocation,
    /// <summary>Play Video: plays a video from the Video Library over the whole game, the game paused till it ends (or is skipped).</summary>
    PlayVideo,
    /// <summary>Open Paperdoll: opens a PaperDoll Editor paperdoll - modal or not, waiting for it to close or not, its answer (Yes / OK = 1, else 0) into a variable.</summary>
    OpenPaperdoll,

    // ---- Script commands (Engine/ScriptCommands.cs has what each one is). Appended only: saved as numbers. ----
    InputNumber, SelectItem, ControlSelfSwitch, ChangeState,
    Calculate, InputText, TextOperation, GetGameData, SwapVariables,
    CreateArray, SetElement, GetElement, AddToArray, InsertAt, RemoveAt, ArrayLength, FindInArray, ReorderArray, FillArray, ClearArray, CopyArray, ArrayStatistic,
    CreateGrid, SetCell, GetCell, FillGrid, GridSize, ResizeGrid, FindInGrid, GridLineToArray, ArrayToGridLine, GridFromMap, GridToMap,
    While, EndWhile, RepeatTimes, EndRepeat, ForEach, EndForEach, BreakLoop, NextLoop, SelectCase, Case, CaseElse, EndSelect, WaitUntil, CallCommonEvent, ReturnFromCommon, Comment,
    ClosePaperdoll, ShowHidePart, SetPartLook, SetPartText, SetPartValue, GetPartValue, FillListFromArray, GetPickedChoice, EnablePart, MovePart, PutItemInSlot, TakeItemFromSlot, MovePaperdoll, FocusTextBox,
    JoinParty, ChangeSchedule, PlaceCharacter, TurnToFace, FollowCharacter, ChangeName, ChangeLook, SetAttackMode, Resurrect, ShowBark,
    CreateObject, DeleteObject, MoveObject, ChangeObjectLook, SetDoorState, CountItems, SetContainerContents,
    FadeScreen, FlashScreen, MoveCamera, CameraFollow, ShowPicture, ErasePicture, SetTime, ChangeMap,
    StopMusic, FadeMusicIn, SetVolume, ResumeMapMusic,
    SaveGame, LoadGame, OpenSaveLoadScreen, AllowSaving, AllowControls, GameOver, ReturnToTitle, QuitGame, LearnSpell, CastSpell,
    DebugLog, PauseHere,
    ShowError,
    DriveVehicle, StopDriving, TravelVehicle, SetVehicleSpeed,
    Earthquake,
    LearnLanguage,
    Shop,
    U8AskKey, U8SetKey, U8GetKeyName, U8ResetKeys,
    DeleteObjectSelf,
    FeignDeath, WithstandDeath, Changeling,
    FindObjectsInArea, DeleteObjectsInArea, ItemsInside, ItemCharges, HurtInArea
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
