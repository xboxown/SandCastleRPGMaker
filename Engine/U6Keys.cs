using System.Text.Json;

namespace SandcastleRpgMaker.Engine;

/// <summary>
/// The keys of an Ultima 6 project: Ultima 6's own by default (its reference card: the first letter of each verb, B begins or
/// breaks off combat, Tab moves the cursor to the inventory, F1-F9 show a member, F10 the party, 1-9 solo, 0 party, * portrait),
/// what the maker set (System Settings > Ultima 6) and what the player changed in the game (the Ultima 6 tab's commands) -
/// kept beside the saves, through new games and loads. Key names and their words are Ultima 8's (U8Keys): the same keyboard.
/// </summary>
public static class U6Keys
{
    public static readonly U8KeyAction[] Actions =
    {
        new("Up", "Walk north / cursor up", "Up"),
        new("Down", "Walk south / cursor down", "Down"),
        new("Left", "Walk west / cursor left", "Left"),
        new("Right", "Walk east / cursor right", "Right"),
        new("UpLeft", "Walk north-west", "NumPad7"),
        new("UpRight", "Walk north-east", "NumPad9"),
        new("DownLeft", "Walk south-west", "NumPad1"),
        new("DownRight", "Walk south-east", "NumPad3"),
        new("Attack", "Attack", "A"),
        new("Cast", "Cast", "C"),
        new("Talk", "Talk", "T"),
        new("Look", "Look", "L"),
        new("Get", "Get", "G"),
        new("Drop", "Drop", "D"),
        new("Move", "Move (push, between members, into containers)", "M"),
        new("Use", "Use", "U"),
        new("Rest", "Rest", "R"),
        new("Combat", "Begin / break off combat", "B"),
        new("ToggleCursor", "Cursor to the inventory and back", "Tab"),
        new("TogglePortrait", "Portrait / inventory", "Multiply"),
        new("Member1", "Show member 1", "F1"), new("Member2", "Show member 2", "F2"), new("Member3", "Show member 3", "F3"),
        new("Member4", "Show member 4", "F4"), new("Member5", "Show member 5", "F5"), new("Member6", "Show member 6", "F6"),
        new("Member7", "Show member 7", "F7"), new("Member8", "Show member 8", "F8"), new("Member9", "Show member 9", "F9"),
        new("PartyView", "Show the party", "F10"),
        new("Solo1", "Solo: member 1", "D1"), new("Solo2", "Solo: member 2", "D2"), new("Solo3", "Solo: member 3", "D3"),
        new("Solo4", "Solo: member 4", "D4"), new("Solo5", "Solo: member 5", "D5"), new("Solo6", "Solo: member 6", "D6"),
        new("Solo7", "Solo: member 7", "D7"), new("Solo8", "Solo: member 8", "D8"), new("Solo9", "Solo: member 9", "D9"),
        new("PartyMode", "Party mode (the party follows again)", "D0"),
        new("Save", "Save the game", "Ctrl+S"),
        new("Load", "Load a game", "Ctrl+R"),
        new("TestNextPeriod", "Game Test: next schedule period", "F11"),
        new("TestScriptState", "Game Test: script data", "F12"),
        new("Quit", "Quit prompt", "Ctrl+Q", U8KeyGroup.Everywhere),
        new("Cancel", "Cancel", "Escape", U8KeyGroup.Everywhere),
        new("Accept", "Pick / Enter", "Enter", U8KeyGroup.Everywhere),
        new("Accept2", "Pick (second key)", "Space", U8KeyGroup.World),
        new("Erase", "Erase a typed letter", "Back", U8KeyGroup.Screens),
    };

    public static readonly string[] ActionNames = Actions.Select(a => a.Name).ToArray();
    public static U8KeyAction? ByName(string name) => Actions.FirstOrDefault(a => a.Name == name);

    /// <summary>Ultima 6's keys under the maker's (System Settings > Ultima 6).</summary>
    public static Dictionary<string, string> MakerKeys(Ultima6Settings settings)
    {
        var keys = Actions.ToDictionary(a => a.Id, a => a.DefaultKey);
        foreach (var (id, key) in settings.Keys)
            if (keys.ContainsKey(id) && U8Keys.IsKeyName(key)) keys[id] = key;
        return keys;
    }

    /// <summary>Gives an action a key; another action that clashes takes this one's old key (swap) or nothing changes (false). As U8Keys.Assign.</summary>
    public static bool Assign(Dictionary<string, string> keys, string id, string key, bool swap)
    {
        string old = keys[id];
        if (old == key) return true;
        var action = Actions.First(a => a.Id == id);
        var holders = Actions.Where(a => a.Id != id && keys[a.Id] == key && Clash(a, action)).ToList();
        if (holders.Count > 0 && !swap) return false;
        if (holders.Any(h => Actions.Any(o => o.Id != id && o.Id != h.Id && keys[o.Id] == old && Clash(o, h)))) return false;
        foreach (var holder in holders) keys[holder.Id] = old;
        keys[id] = key;
        return true;
    }

    private static bool Clash(U8KeyAction a, U8KeyAction b) =>
        a.Group == U8KeyGroup.Everywhere || b.Group == U8KeyGroup.Everywhere || a.Group == b.Group;

    private const string PlayerFileName = "keys.json";
    private static string PlayerFile(Guid projectId) => Path.Combine(GameSaveStore.GameFolder(projectId), PlayerFileName);

    public static Dictionary<string, string> Load(Ultima6Settings settings, Guid? projectId)
    {
        var keys = MakerKeys(settings);
        if (projectId is not { } id) return keys;
        try
        {
            string file = PlayerFile(id);
            if (!File.Exists(file)) return keys;
            foreach (var (action, key) in JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file)) ?? new())
                if (keys.ContainsKey(action) && U8Keys.IsKeyName(key)) keys[action] = key;
        }
        catch { /* a broken file: the maker's keys */ }
        return keys;
    }

    public static void Save(Ultima6Settings settings, Guid? projectId, Dictionary<string, string> keys)
    {
        if (projectId is not { } id) return;
        var maker = MakerKeys(settings);
        var changed = keys.Where(kv => maker.TryGetValue(kv.Key, out var m) && m != kv.Value).ToDictionary(kv => kv.Key, kv => kv.Value);
        string file = PlayerFile(id);
        if (changed.Count == 0)
        {
            if (File.Exists(file)) File.Delete(file);
            return;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        string temp = file + ".saving";
        File.WriteAllText(temp, JsonSerializer.Serialize(changed));
        File.Move(temp, file, overwrite: true);
    }

    public static bool HasPlayerKeys(Guid projectId) => File.Exists(PlayerFile(projectId));

    public static void ForgetPlayerKeys(Guid projectId)
    {
        string file = PlayerFile(projectId);
        if (File.Exists(file)) File.Delete(file);
    }
}

/// <summary>
/// The words an Ultima 6 game writes in its text area (System Settings > Ultima 6 > Words): the verbs as they show ("Attack-")
/// and what happens ("You see {0}."). {0} is filled in with the thing, {1} with whoever.
/// </summary>
public static class U6Words
{
    public static readonly (string Id, string Default, string What)[] All =
    {
        ("Attack", "Attack", "The verb, as typed (Attack-)"),
        ("Cast", "Cast", "The verb"),
        ("Talk", "Talk", "The verb"),
        ("Look", "Look", "The verb"),
        ("Get", "Get", "The verb"),
        ("Drop", "Drop", "The verb"),
        ("Move", "Move", "The verb"),
        ("Use", "Use", "The verb"),
        ("Rest", "Rest", "The verb"),
        ("Combat", "Combat", "The verb (begin / break off)"),
        ("YouSee", "You see {0}.", "Look at something"),
        ("Nothing", "nothing", "Nothing there"),
        ("OutOfRange", "Out of range!", "Too far to reach"),
        ("NotPossible", "Not possible.", "It can't be done"),
        ("WhatPlace", "Where?", "Asking for the place (Drop, Move)"),
        ("ToWhom", "To whom?", "Moving a thing: which member gets it"),
        ("Gives", "{1} gets {0}.", "A thing moved to another member"),
        ("Readies", "{1} readies {0}.", "Put on / taken in hand"),
        ("Unreadies", "{1} puts away {0}.", "Taken off into the pack"),
        ("Gets", "{1} gets {0}.", "Picked up"),
        ("Drops", "{1} drops {0}.", "Put down"),
        ("Solo", "Solo mode: {1}.", "Solo mode on a member"),
        ("PartyMode", "Party mode.", "The party follows again"),
        ("NotInSolo", "Not in solo mode.", "Talking etc. refused in solo mode"),
        ("PressAKey", "Press a key...", "Ask For Key waiting"),
        ("YouSay", "you say: ", "Talking: the player's line, as typed"),
        ("Bye", "bye", "Talking: what nothing typed (or Escape) asks"),
        ("NoHelp", "\"I cannot help thee with that.\"", "Talking: a word with no answer ({0} the word)"),
        ("CombatBegin", "Begin combat!", "Combat mode on"),
        ("TurnOf", "{1}:", "Turn by turn: whose turn ({1} the member)"),
        ("Pass", "Pass", "Turn by turn: the turn passed"),
        ("Attacks", "{1} attacks {0}.", "A blow: {1} the attacker, {0} the one attacked"),
        ("Missed", "Missed!", "A blow that missed"),
        ("Barely", "{0} barely wounded.", "Hurt, more than 3/4 of its hit points left"),
        ("Lightly", "{0} lightly wounded.", "Hurt, half to 3/4 left"),
        ("Heavily", "{0} heavily wounded.", "Hurt, a quarter to half left"),
        ("Critical", "{0} critically wounded!", "Hurt, less than a quarter left"),
        ("KnockedOut", "{0} is knocked out!", "Hurt to 0"),
        ("Killed", "{0} killed!", "Killed"),
        ("CombatBreakOff", "Break off combat!", "Combat mode off"),
        ("BarelyBy", "{0} barely damaged by {1}.", "Hurt by something not a blow (poison, starvation, fire, a trap: {1}), more than 3/4 left"),
        ("LightlyBy", "{0} lightly damaged by {1}.", "Hurt by something not a blow, half to 3/4 left"),
        ("HeavilyBy", "{0} heavily damaged by {1}.", "Hurt by something not a blow, a quarter to half left"),
        ("CriticalBy", "{0} critically damaged by {1}!", "Hurt by something not a blow, under a quarter left"),
        ("KnockedOutBy", "{0} is knocked out by {1}!", "Knocked out by something not a blow"),
        ("KilledBy", "{0} killed by {1}!", "Killed by something not a blow"),
        ("SaysLine", "{1}: \"{0}\"", "A character's own line (Character Editor > Verbose): {1} the one, {0} the words"),
        ("PartyFull", "The party is full: {1} doesn't join.", "Join Party with the party full ({1} the one)"),
        ("ErrorLine", "Error {1}: {0}", "Any error (disk full, a damaged save...): {1} its number, {0} its words (System Settings > Errors)"),
        ("QuitAsk", "Quit the game? (Y/N) ", "Ctrl+Q / Quit: the question"),
        ("Saved", "Game saved.", "Ctrl+S: the game saved (Apple IIgs)"),
        ("Loaded", "Game loaded.", "Ctrl+R: a saved game back (Apple IIgs)"),
        ("NoSaving", "Saving isn't allowed here.", "Saving while an event has forbidden it (Allow / Forbid Saving)"),
        ("NoSavedGame", "There is no saved game.", "Ctrl+R with nothing saved"),
        ("LogError", "Log error generated.", "Apple IIgs: Debug Log, Pause Here or Show Error wrote a line in LOG.TXT"),
        ("NoSave", "No saved game.", "Ctrl+R with nothing saved, or a save that can't be written (Apple IIgs)"),
        ("TalkSelf", "Talking to yourself?", "Talking to the avatar"),
        ("NoResponse", "No response.", "Talking to someone asleep, hostile, fighting or dead"),
        ("FunnyNoResponse", "Funny, no response.", "Talking to someone with nothing to say"),
        ("Searching", "Searching here, you find {0}.", "A container used on the map: what spills out of it"),
        ("SearchNothing", "Searching here, you find nothing.", "A container used on the map, empty"),
        ("Locked", "Locked.", "A locked container used"),
        ("OnWhat", "On what? ", "A key or lockpick used: asking for the door or chest (Apple IIgs)"),
        ("Unlocked", "Unlocked.", "A key or lockpick opened a lock (Apple IIgs)"),
        ("KeyNoFit", "The key doesn't fit.", "A key used on a lock it isn't for (Apple IIgs)"),
        ("PickBroke", "The lockpick broke!", "A lockpick that failed (Apple IIgs)"),
        ("NotWhileCombat", "-Not while in Combat!", "Rest refused: combat mode"),
        ("FoesNear", "-Not while foes are near!", "Rest refused: enemies about"),
        ("OthersNear", "-Not while others are near!", "Rest refused: someone not in the party within 5"),
        ("RestNotSolo", "-Not in solo mode!", "Rest refused: solo mode"),
        ("HowManyHours", "How many hours? ", "Rest: the question (0-9)"),
        ("WhoGuards", "Who will guard? ", "Rest: the question (a member's number, 0 none)"),
        ("NoOne", "none", "Rest: no one guards"),
        ("Mealtime", "Mealtime!", "Rest: the party eats"),
        ("HasFood", "{1} has food.", "Rest: a member eats"),
        ("NoFood", "{1} has no food.", "Rest: a member with nothing to eat"),
        ("StandsGuard", "{1} stands guard while the party rests.", "Rest: the guard"),
        ("Healed", "{1} has healed.", "Rest: hit points back (fed, once every 12 hours)"),
        ("NewLevel", "{1} is now level {0}!", "A level taken ({0} the level)"),
        ("Gains", "{1} gains {0}.", "A level's points ({0}: \"3 intelligence\")"),
        ("NotReady", "{1} is not ready.", "A shrine: no one has earned a level"),
        ("Dream", "{1} has a dream. Three stone obelisks... {1} feels drawn to one of them. Which: S, D or I?", "Resting: a level earned (Savage Empire / Martian Dreams way)"),
        ("NoSpellbook", "No spellbook is readied.", "Cast: no spellbook in a hand"),
        ("NoSpells", "No spells in the spellbook.", "Cast: the book knows none"),
        ("NotInBook", "That spell is not in thy spellbook!", "Cast: syllables of no spell in the book"),
        ("LevelTooLow", "Your level is not high enough.", "Cast: level under the spell's circle"),
        ("NoMagicPoints", "Not enough magic points.", "Cast: too few magic points"),
        ("NoReagents", "No Reagents.", "Cast: a reagent missing"),
        ("OnWhom", "On whom? ", "Cast: asking for the spell's target"),
        ("PickReply", "Choose: ", "Conversation Style - picked from a list: the prompt"),
        ("NothingWritten", "There is nothing written.", "A book with no words"),
        ("More", "<more>", "A book: more to read (any key goes on, Escape stops)"),
        ("PointsLeft", "Points left:", "Scene Distribute Points: the points still to give"),
        ("TitleIntro", "Introduction", "Title menu: the intro video"),
        ("TitleCreate", "Create New Character", "Title menu: a new game"),
        ("TitleAcks", "Acknowledgments", "Title menu: the maker's words"),
        ("TitleJourney", "Journey Onward", "Title menu: the saved game"),
        ("ModeCommand", "COMMAND", "Combat mode, as shown in the panel"),
        ("ModeFront", "FRONT", "Combat mode"),
        ("ModeRear", "REAR", "Combat mode"),
        ("ModeFlank", "FLANK", "Combat mode"),
        ("ModeBerserk", "BERSERK", "Combat mode"),
        ("ModeRetreat", "RETREAT", "Combat mode"),
        ("ModeAssault", "ASSAULT", "Combat mode"),
        ("Strength", "strength", "A stat's name"),
        ("Dexterity", "dexterity", "A stat's name"),
        ("Intelligence", "intelligence", "A stat's name"),
        ("Blood", "blood", "Look at the blood a death leaves"),
        ("TooHeavy", "Too heavy!", "Apple IIgs: picking up or giving more than one can carry (2 x Strength)"),
        ("TrapOff", "{0} goes off!", "Apple IIgs: a trap stepped on"),
        ("BurntOut", "{0} has burnt out.", "Apple IIgs: a carried light spent"),
        ("TempCold", "{1} is cold.", "Apple IIgs: in a Cold zone"),
        ("TempFreezing", "{1} is freezing!", "Apple IIgs: badly cold"),
        ("TempHot", "{1} is hot.", "Apple IIgs: in a Hot zone"),
        ("TempBurning", "{1} is burning up!", "Apple IIgs: badly hot"),
    };

    public static string Of(Ultima6Settings settings, string id) =>
        settings.Words.TryGetValue(id, out var w) && w.Length > 0 ? w : All.FirstOrDefault(a => a.Id == id).Default ?? id;
}
