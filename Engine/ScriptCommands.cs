namespace SandcastleRpgMaker.Engine;

/// <summary>What one box of a script command's window holds.</summary>
public enum ScriptFieldKind
{
    /// <summary>A formula (ScriptExpression): a number, "a text", a variable, Items[i], Gold + 5...</summary>
    Formula,
    /// <summary>The name of a variable (number or text) - picked from the project's or typed.</summary>
    Variable,
    /// <summary>Words, used as typed (not a formula).</summary>
    Text,
    /// <summary>Several lines of words.</summary>
    LongText,
    /// <summary>One of Options.</summary>
    Choice,
    /// <summary>Ticked or not: "True" / "False".</summary>
    Check,
    /// <summary>The name of an array.</summary>
    Array,
    /// <summary>The name of a grid.</summary>
    Grid,
    /// <summary>Whoever set the event off, the main player, or a character (key Who).</summary>
    Who,
    /// <summary>An item kind (keys Category, ItemId).</summary>
    Item,
    /// <summary>A thing on the map (keys Thing, ThingName, X, Y).</summary>
    Thing,
    /// <summary>A cell of the map with its floor (keys X, Y, Floor).</summary>
    Spot,
    /// <summary>A PaperDoll Editor paperdoll (keys DesignId, DesignName); "" = the one running the event.</summary>
    Paperdoll,
    /// <summary>A sprite (keys SpriteGroup, SpriteName).</summary>
    Sprite,
    /// <summary>A sound or a piece of music from the Sound list (its id).</summary>
    Sound,
    /// <summary>A Common Event (its id).</summary>
    CommonEvent,
    /// <summary>A condition: a formula, or a game condition (Advance IF) - keys Mode, Formula, Condition, Summary.</summary>
    Condition,
    /// <summary>A pose by name.</summary>
    Pose,
    /// <summary>A faction (its id).</summary>
    Faction,
    /// <summary>Words as typed (no quotes needed) - or, switched to Formula, a formula. Its mode is kept as key + "Mode" ("Words" / "Formula"; none = Formula, as commands made before it).</summary>
    Words,
    /// <summary>A world (Maps tab) - keys World (its id; -1 = the one the party is in), WorldName.</summary>
    World,
    /// <summary>A vehicle (Map Editor > Vehicles tab) - keys Vehicle (its id), VehicleName; in play the copy of it nearest the player.</summary>
    Vehicle,
    /// <summary>A language (System Settings > Languages) - keys Language (its id), LanguageName.</summary>
    Language
}

public sealed record ScriptField(string Key, string Label, ScriptFieldKind Kind, string Default = "", string[]? Options = null, string Help = "");

/// <summary>Where a command stands in a block (a loop or a Select Case).</summary>
public enum ScriptBlockRole { None, Opens, Middle, Closes }

/// <summary>
/// One script command: its name, where its button is (Tab and Group in the
/// command picker), what it does, its boxes, how it reads in the list
/// (Summary: {Key} is that box's value) and the lines added with it (an
/// opener's End...). Commands with no Tab have their own button already.
/// </summary>
public sealed record ScriptCommandSpec(
    EventCommandType Type, string Name, string Tab, string Group, string Help, ScriptField[] Fields, string Summary,
    ScriptBlockRole Role = ScriptBlockRole.None, EventCommandType[]? AddsAfter = null, bool HasWindow = true);

/// <summary>Every script command (CommandList groups 1-12), in picker order.</summary>
public static class ScriptCommands
{
    private const string Data = "Data", Loops = "Loops", Dolls = "Paperdolls", World = "World", Game = "Screen & Game";
    /// <summary>The tab of the commands only an Ultima 8 project has: the picker shows it in no other.</summary>
    public const string Ultima8Tab = "Ultima 8";
    /// <summary>Ultima 8 projects only too: what came from its engine's other games (Crusader).</summary>
    public const string CrusaderTab = "Crusader";
    /// <summary>Areas and rituals: their own tab, so World doesn't grow too long.</summary>
    public const string AreasTab = "Areas";
    /// <summary>Tabs an Ultima 7 project never shows.</summary>
    public static bool IsUltima8Only(string tab) => tab is Ultima8Tab or CrusaderTab;

    private static ScriptField F(string key, string label, ScriptFieldKind kind = ScriptFieldKind.Formula, string def = "", string help = "", params string[] options) =>
        new(key, label, kind, def, options.Length > 0 ? options : null, help);
    private static ScriptField Choice(string key, string label, params string[] options) => new(key, label, ScriptFieldKind.Choice, options[0], options);
    private static ScriptField Var(string key = "Variable", string label = "Into the variable") => new(key, label, ScriptFieldKind.Variable);
    private static ScriptField Arr(string key = "Array", string label = "Array") => new(key, label, ScriptFieldKind.Array);
    private static ScriptField Grd(string key = "Grid", string label = "Grid") => new(key, label, ScriptFieldKind.Grid);
    private static ScriptField Who(string label = "Who") => new("Who", label, ScriptFieldKind.Who);
    private static ScriptField Check(string key, string label, bool on = false) => new(key, label, ScriptFieldKind.Check, on ? "True" : "False");
    private static ScriptField Doll() => new("DesignId", "Paperdoll", ScriptFieldKind.Paperdoll);
    private static ScriptField Part() => new("PartName", "Part (its name in the PaperDoll Editor)", ScriptFieldKind.Text);

    public static readonly ScriptCommandSpec[] All =
    {
        // ---- 1. The buttons that were greyed out (their buttons are on Page 1) ----
        new(EventCommandType.InputNumber, "Input Number", "", "", "The player types a number; it goes into a variable. The game waits for it.",
            new[] { Var(), F("Digits", "Longest (digits)", def: "4"), F("Prompt", "Words above the box", ScriptFieldKind.Text, "How many?") }, "{Variable} (up to {Digits} digits)"),
        new(EventCommandType.SelectItem, "Select Item", "", "", "The player clicks an item they carry; its name goes into a text variable, its Object Editor id into another (\"\" / -1 if they press Escape). The game waits for it.",
            new[] { Var("Variable", "Its name into"), Var("IdVariable", "Its id into (optional)"), F("Prompt", "Words to show", ScriptFieldKind.Text, "Which item?") }, "{Variable}"),
        new(EventCommandType.ControlSelfSwitch, "Control Self Switch", "", "", "Switches A, B, C and D that belong to this event alone (each placed thing has its own) - a chest that stays opened, without using a global switch. Test it with a formula: self(\"A\").",
            new[] { Choice("Letter", "Self switch", "A", "B", "C", "D"), Choice("Value", "Turn it", "On", "Off") }, "{Letter} = {Value}"),
        new(EventCommandType.RemoveItem, "Remove Item", "", "", "Takes an item (some of a stack) away from a character - or from anyone in the party who has it. Not enough: as many as they have are taken, and Advance IF > Last command succeeded is false.",
            new[] { Who(), new("Category", "Item", ScriptFieldKind.Item), F("Quantity", "How many (formula)", def: "1"), Check("FromParty", "From anyone in the party") }, "{Quantity} x {Category} from {Who}"),
        new(EventCommandType.ChangeHp, "Change HP", "", "", "Adds to or takes from a character's health, or sets it. Taking can knock them out - or kill them only if \"Can kill\" is ticked (as Exult: dead below -(Strength / 3)).",
            new[] { Who(), Choice("Operation", "Operation", "Add", "Subtract", "Set"), F("Amount", "Amount (formula)", def: "10"), Check("CanKill", "Can kill") }, "{Who} {Operation} {Amount}"),
        new(EventCommandType.ChangeTile, "Change Tile", "", "", "Changes the floor tile of a map cell (its number in the tileset, from 0) - a bridge appears, a wall crumbles.",
            new[] { new("X", "Cell", ScriptFieldKind.Spot), F("TileIndex", "New tile number (formula)", def: "0") }, "{X} -> tile {TileIndex}"),
        new(EventCommandType.ChangeState, "Change State", "", "", "Sets one state on a character at once: asleep, paralyzed, poisoned, charmed, cursed, invisible or protected - on or off.",
            new[] { Who(), Choice("State", "State", "Asleep", "Paralyzed", "Poisoned", "Charmed", "Cursed", "Invisible", "Protected"), Choice("Value", "Turn it", "On", "Off") }, "{Who}: {State} {Value}"),

        // ---- 2. Variables, math and text ----
        new(EventCommandType.Calculate, "Calculate", Data, "Variables & Text", "A variable = any formula: Gold + 5, random(1, 6), max(a, b), round(x), \"Hello \" + Name, Items[i]... It holds a number or a text.",
            new[] { Var("Variable", "Variable"), F("Formula", "= (formula)") }, "{Variable} = {Formula}"),
        new(EventCommandType.InputText, "Input Text", Data, "Variables & Text", "The player types words - the hero's name, a password, a riddle's answer; they go into a text variable. The game waits for it.",
            new[] { Var(), F("Letters", "Longest (letters)", def: "16"), F("Prompt", "Words above the box", ScriptFieldKind.Text, "Your name?"), F("Start", "Starts with (formula)", def: "\"\"") }, "{Variable} (up to {Letters} letters)"),
        new(EventCommandType.TextOperation, "Text Operation", Data, "Variables & Text", "Works on a text: join, length, a part of it, upper / lower case, trim, replace, contains, number to text and back.",
            new[] { Var(), Choice("Operation", "Operation", "Join", "Length", "Part", "Upper case", "Lower case", "Trim", "Replace", "Contains", "Find", "Number to text", "Text to number"),
                    F("A", "Text (formula)"), F("B", "Second value (Join: the other text; Part: start from 0; Replace / Contains / Find: what to look for)"), F("C", "Third value (Part: how many letters; Replace: put instead)") },
            "{Variable} = {Operation} of {A}"),
        new(EventCommandType.GetGameData, "Get Game Data", Data, "Variables & Text", "Puts a fact about the game into a variable.",
            new[] { Var(), Who("Of whom (for a character's facts)"),
                    Choice("What", "What", "X (cell)", "Y (cell)", "Floor", "HP", "Max HP", "Stat", "Gold (all money)", "Item count", "Food level", "Name", "Is dead", "Is in party", "Party size",
                        "Hour", "Minute", "Day", "Map name", "Leader's name", "Facing"),
                    F("StatName", "Stat name (for Stat)", ScriptFieldKind.Text), new("Category", "Item (for Item count)", ScriptFieldKind.Item) },
            "{Variable} = {What} of {Who}"),
        new(EventCommandType.SwapVariables, "Swap Variables", Data, "Variables & Text", "Exchanges two variables' values.",
            new[] { Var("A", "Variable"), Var("B", "and variable") }, "{A} <-> {B}"),

        // ---- 3. Arrays ----
        new(EventCommandType.CreateArray, "Create Array", Data, "Arrays (lists)", "A named list. Each element is a number or a text; the first is element 0. Creating it again starts it anew.",
            new[] { Arr("Array", "Name"), F("Size", "How many elements", def: "0"), F("Fill", "Each starts as (formula)", def: "0") }, "{Array}, {Size} x {Fill}"),
        new(EventCommandType.SetElement, "Set Element", Data, "Arrays (lists)", "Array[index] = a value.",
            new[] { Arr(), F("Index", "Index (from 0)"), F("Value", "= (formula)") }, "{Array}[{Index}] = {Value}"),
        new(EventCommandType.GetElement, "Get Element", Data, "Arrays (lists)", "A variable = Array[index].",
            new[] { Arr(), F("Index", "Index (from 0)"), Var() }, "{Variable} = {Array}[{Index}]"),
        new(EventCommandType.AddToArray, "Add To Array", Data, "Arrays (lists)", "Adds a value at the end.",
            new[] { Arr(), F("Value", "Value (formula)") }, "{Array} + {Value}"),
        new(EventCommandType.InsertAt, "Insert At", Data, "Arrays (lists)", "Puts a value in at a position; the ones after it move up one.",
            new[] { Arr(), F("Index", "At index (from 0)"), F("Value", "Value (formula)") }, "{Array}[{Index}] <- {Value}"),
        new(EventCommandType.RemoveAt, "Remove At", Data, "Arrays (lists)", "Takes out the element at a position (or the first one equal to a value); the ones after it move down one.",
            new[] { Arr(), Choice("By", "Remove", "At index", "First equal to"), F("Index", "Index or value (formula)") }, "{Array}: {By} {Index}"),
        new(EventCommandType.ArrayLength, "Array Length", Data, "Arrays (lists)", "How many elements it has, into a variable.",
            new[] { Arr(), Var() }, "{Variable} = length of {Array}"),
        new(EventCommandType.FindInArray, "Find In Array", Data, "Arrays (lists)", "The index of the first element equal to a value, into a variable (-1 = not there).",
            new[] { Arr(), F("Value", "Value (formula)"), Var() }, "{Variable} = where {Value} is in {Array}"),
        new(EventCommandType.ReorderArray, "Sort / Shuffle / Reverse", Data, "Arrays (lists)", "Puts the elements in order: small to big, big to small, random, or backwards.",
            new[] { Arr(), Choice("Order", "Order", "Small to big", "Big to small", "Shuffle", "Reverse") }, "{Array}: {Order}"),
        new(EventCommandType.FillArray, "Fill Array", Data, "Arrays (lists)", "Every element set to one value.",
            new[] { Arr(), F("Value", "Value (formula)") }, "{Array} all = {Value}"),
        new(EventCommandType.ClearArray, "Clear Array", Data, "Arrays (lists)", "Empties it (length 0).",
            new[] { Arr() }, "{Array}"),
        new(EventCommandType.CopyArray, "Copy Array", Data, "Arrays (lists)", "Makes one array the same as another.",
            new[] { Arr("From", "Copy"), Arr("Array", "Into") }, "{From} -> {Array}"),
        new(EventCommandType.ArrayStatistic, "Array Sum / Min / Max", Data, "Arrays (lists)", "The sum, smallest, biggest or average of its numbers, into a variable.",
            new[] { Arr(), Choice("What", "What", "Sum", "Smallest", "Biggest", "Average"), Var() }, "{Variable} = {What} of {Array}"),

        // ---- 4. Grids ----
        new(EventCommandType.CreateGrid, "Create Grid", Data, "Grids (2 dimensions)", "A named grid of columns x rows - a puzzle board, a field, a little map. Cells are Grid[x, y], both from 0.",
            new[] { Grd("Grid", "Name"), F("Columns", "Columns", def: "8"), F("Rows", "Rows", def: "8"), F("Fill", "Each cell starts as (formula)", def: "0") }, "{Grid}, {Columns} x {Rows}"),
        new(EventCommandType.SetCell, "Set Cell", Data, "Grids (2 dimensions)", "Grid[x, y] = a value.",
            new[] { Grd(), F("X", "x (column, from 0)"), F("Y", "y (row, from 0)"), F("Value", "= (formula)") }, "{Grid}[{X}, {Y}] = {Value}"),
        new(EventCommandType.GetCell, "Get Cell", Data, "Grids (2 dimensions)", "A variable = Grid[x, y].",
            new[] { Grd(), F("X", "x (column, from 0)"), F("Y", "y (row, from 0)"), Var() }, "{Variable} = {Grid}[{X}, {Y}]"),
        new(EventCommandType.FillGrid, "Fill Grid / Fill Area", Data, "Grids (2 dimensions)", "Every cell - or a rectangle of cells - set to one value.",
            new[] { Grd(), F("Value", "Value (formula)"), Check("Area", "Only an area"), F("X", "Area: from x", def: "0"), F("Y", "Area: from y", def: "0"), F("Width", "Area: width", def: "1"), F("Height", "Area: height", def: "1") }, "{Grid} = {Value}"),
        new(EventCommandType.GridSize, "Grid Size", Data, "Grids (2 dimensions)", "Its columns and rows, into two variables.",
            new[] { Grd(), Var("Variable", "Columns into"), Var("Variable2", "Rows into") }, "{Variable}, {Variable2} = size of {Grid}"),
        new(EventCommandType.ResizeGrid, "Resize Grid", Data, "Grids (2 dimensions)", "A new size; what fits stays where it was, new cells get the fill value.",
            new[] { Grd(), F("Columns", "Columns"), F("Rows", "Rows"), F("Fill", "New cells (formula)", def: "0") }, "{Grid} to {Columns} x {Rows}"),
        new(EventCommandType.FindInGrid, "Find In Grid", Data, "Grids (2 dimensions)", "The x and y of the first cell equal to a value (row by row), into two variables (-1, -1 = not there).",
            new[] { Grd(), F("Value", "Value (formula)"), Var("Variable", "x into"), Var("Variable2", "y into") }, "{Variable}, {Variable2} = where {Value} is in {Grid}"),
        new(EventCommandType.GridLineToArray, "Row / Column To Array", Data, "Grids (2 dimensions)", "Copies one row or one column of a grid into an array.",
            new[] { Grd(), Choice("Line", "Copy", "Row", "Column"), F("Index", "Which (from 0)"), Arr("Array", "Into the array") }, "{Grid} {Line} {Index} -> {Array}"),
        new(EventCommandType.ArrayToGridLine, "Array To Row / Column", Data, "Grids (2 dimensions)", "Copies an array into one row or column of a grid (as much as fits).",
            new[] { Arr("Array", "Copy the array"), Grd(), Choice("Line", "Into", "Row", "Column"), F("Index", "Which (from 0)") }, "{Array} -> {Grid} {Line} {Index}"),
        new(EventCommandType.GridFromMap, "Grid From Map", Data, "Grids (2 dimensions)", "Copies the tile numbers of an area of the map (a floor's tiles) into a new grid - for puzzles that look at the floor.",
            new[] { Grd(), new("X", "Area's top-left cell", ScriptFieldKind.Spot), F("Width", "Width (cells)", def: "8"), F("Height", "Height (cells)", def: "8") }, "{Grid} from the map"),
        new(EventCommandType.GridToMap, "Grid To Map", Data, "Grids (2 dimensions)", "Draws a grid's tile numbers back onto the map from a cell (-1 or less = leave that cell).",
            new[] { Grd(), new("X", "Top-left cell", ScriptFieldKind.Spot) }, "{Grid} onto the map"),

        // ---- 5. Loops and flow ----
        new(EventCommandType.While, "While ... End While", Loops, "Loops", "Repeats the commands inside while the condition is true (a formula like Count < length(Items), or any Advance IF condition). It's checked before each turn.",
            new[] { new ScriptField("Mode", "While", ScriptFieldKind.Condition) }, "{Mode}", ScriptBlockRole.Opens, new[] { EventCommandType.EndWhile }),
        new(EventCommandType.EndWhile, "End While", "", "", "", Array.Empty<ScriptField>(), "", ScriptBlockRole.Closes, HasWindow: false),
        new(EventCommandType.RepeatTimes, "Repeat N Times ... End Repeat", Loops, "Loops", "Runs the commands inside a number of times; the counter variable (optional) goes 1, 2, 3...",
            new[] { F("Count", "How many times (formula)", def: "3"), Var("Variable", "Counter variable (optional)") }, "{Count} times ({Variable})", ScriptBlockRole.Opens, new[] { EventCommandType.EndRepeat }),
        new(EventCommandType.EndRepeat, "End Repeat", "", "", "", Array.Empty<ScriptField>(), "", ScriptBlockRole.Closes, HasWindow: false),
        new(EventCommandType.ForEach, "For Each ... End For Each", Loops, "Loops", "Runs the commands inside once for every element of an array (its index and value into variables) - or every cell of a grid, row by row (x, y and value).",
            new[] { Choice("Of", "For each", "Element of an array", "Cell of a grid"), new("Array", "Array or grid", ScriptFieldKind.Array), Var("Variable", "Value into"), Var("Index", "Index (array) or x (grid) into"), Var("Index2", "y (grid) into") },
            "{Of} {Array}: {Variable}", ScriptBlockRole.Opens, new[] { EventCommandType.EndForEach }),
        new(EventCommandType.EndForEach, "End For Each", "", "", "", Array.Empty<ScriptField>(), "", ScriptBlockRole.Closes, HasWindow: false),
        new(EventCommandType.BreakLoop, "Break Loop", Loops, "Loops", "Leaves the loop it is in now (While, Repeat or For Each), going on after its End.", Array.Empty<ScriptField>(), "", HasWindow: false),
        new(EventCommandType.NextLoop, "Next Loop", Loops, "Loops", "Skips the rest of this turn of the loop and goes on with its next turn.", Array.Empty<ScriptField>(), "", HasWindow: false),
        new(EventCommandType.SelectCase, "Select Case ... End Select", Loops, "Choosing", "One of many branches by a value: the commands under the first Case whose value matches run (a Case can list several: 1, 2, 3); none - Case Else.",
            new[] { F("Formula", "Value to look at (formula)"), F("Cases", "Cases (one value per line; you can add more with Case later)", ScriptFieldKind.LongText, "1\n2"), Check("WithElse", "With a Case Else", true) },
            "{Formula}", ScriptBlockRole.Opens, new[] { EventCommandType.EndSelect }),
        new(EventCommandType.Case, "Case", Loops, "Choosing", "One more Case of the Select Case it is put in (select a line inside the Select Case first).",
            new[] { F("Formula", "Value(s), comma between (formulas)") }, "{Formula}", ScriptBlockRole.Middle),
        new(EventCommandType.CaseElse, "Case Else", "", "", "", Array.Empty<ScriptField>(), "", ScriptBlockRole.Middle, HasWindow: false),
        new(EventCommandType.EndSelect, "End Select", "", "", "", Array.Empty<ScriptField>(), "", ScriptBlockRole.Closes, HasWindow: false),
        new(EventCommandType.WaitUntil, "Wait Until", Loops, "Flow", "Waits until the condition is true (checked every moment) - a door opened, someone arrived, a variable set elsewhere. Optional longest wait: then it goes on anyway.",
            new[] { new ScriptField("Mode", "Until", ScriptFieldKind.Condition), F("Timeout", "Longest wait in seconds (0 = for ever)", def: "0") }, "{Mode}"),
        new(EventCommandType.CallCommonEvent, "Call Common Event", Loops, "Flow", "Runs a Common Event (Tools > Common Events: a list of commands made once and used from anywhere), then comes back here. Its parameters get these values.",
            new[] { new ScriptField("CommonEventId", "Common Event", ScriptFieldKind.CommonEvent), F("Args", "Parameter values, comma between (formulas)") }, "{CommonEventId}({Args})"),
        new(EventCommandType.ReturnFromCommon, "Return", Loops, "Flow", "Ends the Common Event (or event) here - back to whoever called it. An optional value goes into the variable \"Result\".",
            new[] { F("Value", "Value to give back (optional formula)") }, "{Value}"),
        new(EventCommandType.Comment, "Comment", Loops, "Flow", "A note for you in the command list; it does nothing.",
            new[] { F("Text", "Note", ScriptFieldKind.LongText) }, "{Text}"),

        // ---- 6. Paperdolls ----
        new(EventCommandType.ClosePaperdoll, "Close Paperdoll", Dolls, "Paperdolls", "Closes this paperdoll (the one running the event), one by name, or every one open.",
            new[] { Choice("Which", "Close", "This paperdoll", "One paperdoll", "Every paperdoll"), Doll() }, "{Which} {DesignId}"),
        new(EventCommandType.ShowHidePart, "Show / Hide Part", Dolls, "Parts", "A part of an open paperdoll appears or disappears.",
            new[] { Doll(), Part(), Choice("Value", "Make it", "Shown", "Hidden", "Toggled") }, "{DesignId} {PartName}: {Value}"),
        new(EventCommandType.SetPartLook, "Set Part Look", Dolls, "Parts", "Shows one of a part's looks, by its number (from 1) or its name (a light on, a gem lit).",
            new[] { Doll(), Part(), F("Look", "Look number, or its name in quotes (formula)", def: "1") }, "{DesignId} {PartName} look {Look}"),
        new(EventCommandType.SetPartText, "Set Part Text", Dolls, "Parts", "Changes a Text part's words or a button's caption - from a formula: \"Gold: \" + Gold.",
            new[] { Doll(), Part(), new("Value", "Words", ScriptFieldKind.Words) }, "{DesignId} {PartName} = {Value}"),
        new(EventCommandType.SetPartValue, "Set Part Value", Dolls, "Parts", "Sets a slider, progress bar, check box (0 / 1), radio button (1 = picked), text box, list or drop-down (picked row from 1).",
            new[] { Doll(), Part(), F("Value", "Value (formula)") }, "{DesignId} {PartName} = {Value}"),
        new(EventCommandType.GetPartValue, "Get Part Value", Dolls, "Parts", "Reads a control's value (slider, check box, text box's words, list's picked row) into a variable.",
            new[] { Doll(), Part(), Var() }, "{Variable} = {DesignId} {PartName}"),
        new(EventCommandType.FillListFromArray, "Fill List From Array", Dolls, "Lists", "A list box or drop-down gets its choices from an array.",
            new[] { Doll(), Part(), Arr() }, "{DesignId} {PartName} <- {Array}"),
        new(EventCommandType.GetPickedChoice, "Get Picked Choice", Dolls, "Lists", "The picked row of a list box or drop-down: its number (from 1, 0 = none) and its words into variables.",
            new[] { Doll(), Part(), Var("Variable", "Its number into"), Var("Variable2", "Its words into") }, "{Variable}, {Variable2} = {DesignId} {PartName}"),
        new(EventCommandType.EnablePart, "Enable / Disable Part", Dolls, "Parts", "Disabled: greyed out and can't be clicked, but still shown.",
            new[] { Doll(), Part(), Choice("Value", "Make it", "Enabled", "Disabled") }, "{DesignId} {PartName}: {Value}"),
        new(EventCommandType.MovePart, "Move Part", Dolls, "Parts", "Moves a part to an X / Y on its paperdoll - at once or sliding over a time.",
            new[] { Doll(), Part(), F("X", "X (pixels, formula)"), F("Y", "Y (pixels, formula)"), F("Seconds", "Over how many seconds (0 = at once)", def: "0") }, "{DesignId} {PartName} to ({X}, {Y})"),
        new(EventCommandType.PutItemInSlot, "Put Item In Slot", Dolls, "Items", "Puts an item (a new one) into a paperdoll's slot or item area.",
            new[] { Doll(), Part(), new("Category", "Item", ScriptFieldKind.Item), F("Quantity", "How many", def: "1") }, "{Category} into {DesignId} {PartName}"),
        new(EventCommandType.TakeItemFromSlot, "Take Item From Slot", Dolls, "Items", "Empties a paperdoll's slot or item area - the items go to the main player, or are gone.",
            new[] { Doll(), Part(), Choice("To", "The items", "Go to the main player", "Are gone") }, "{DesignId} {PartName}: {To}"),
        new(EventCommandType.MovePaperdoll, "Move Paperdoll / Bring To Front", Dolls, "Paperdolls", "Where an open paperdoll sits on the screen (pixels from the top-left of the game), and / or puts it on top of the others.",
            new[] { Doll(), Check("Move", "Move it", true), F("X", "X (formula)", def: "0"), F("Y", "Y (formula)", def: "0"), Check("Front", "Bring it to the front", true) }, "{DesignId}"),
        new(EventCommandType.FocusTextBox, "Focus Text Box", Dolls, "Parts", "The player can type into a text box straight away.",
            new[] { Doll(), Part() }, "{DesignId} {PartName}"),

        // ---- 7. Characters and party ----
        new(EventCommandType.JoinParty, "Join / Leave Party", World, "Characters", "A character joins the party, or leaves it (and goes back to its schedule).",
            new[] { Who("Character"), Choice("Value", "They", "Join the party", "Leave the party") }, "{Who}: {Value}"),
        new(EventCommandType.ChangeSchedule, "Change Schedule", World, "Characters", "Gives a character a new activity right where it is, for some game hours (or until its schedule next changes): stand, loiter, wander, sleep, sit, dance, pace, eat at the inn, wait tables - or back to its schedule now.",
            new[] { Who("Character"), Choice("Activity", "Activity", "Stand", "Loiter", "Wander", "Sleep", "Wait", "Sit", "Dance", "Pace east-west", "Pace north-south", "Eat at inn", "Waiter", "Back to its schedule"), F("Hours", "For how many game hours (0 = from now on)", def: "0") }, "{Who}: {Activity}"),
        new(EventCommandType.PlaceCharacter, "Place / Remove Character", World, "Characters", "Puts a character on the map at a spot (coming in from wherever it was), or takes it off the map.",
            new[] { Who("Character"), Choice("Value", "Do", "Place it", "Remove it from the map"), new("X", "At", ScriptFieldKind.Spot) }, "{Who}: {Value}"),
        new(EventCommandType.TurnToFace, "Turn To Face", World, "Characters", "Turns a character to a direction, to the main player, or to another character.",
            new[] { Who(), Choice("Towards", "Face", "North", "South", "East", "West", "The main player", "Another character", "Whoever set it off"), new("Other", "The other character", ScriptFieldKind.Who) }, "{Who} faces {Towards}"),
        new(EventCommandType.FollowCharacter, "Follow / Stop Following", World, "Characters", "A character walks after another one (keeping close), until told to stop.",
            new[] { Who("Who follows"), Choice("Value", "Do", "Follow", "Stop following"), new("Other", "Whom", ScriptFieldKind.Who) }, "{Who} {Value} {Other}"),
        new(EventCommandType.ChangeName, "Change Name", World, "Characters", "A character's name, from a formula (Name is a text variable typed with Input Text, say).",
            new[] { Who(), new("Value", "New name", ScriptFieldKind.Words) }, "{Who} = {Value}"),
        new(EventCommandType.ChangeLook, "Change Look", World, "Characters", "A character looks like another character (its sprites and poses) - a disguise, a transformation; back to its own look with \"Its own\".",
            new[] { Who(), new("Other", "Looks like", ScriptFieldKind.Who), Check("Own", "Back to its own look") }, "{Who} looks like {Other}"),
        new(EventCommandType.SetAttackMode, "Set Attack Mode", World, "Characters", "Exult's combat modes: who it attacks in combat.",
            new[] { Who(), Choice("Mode", "Mode", "Nearest", "Weakest", "Strongest", "Berserk", "Protect", "Defend", "Flank", "Flee", "Random", "Manual") }, "{Who}: {Mode}"),
        new(EventCommandType.Resurrect, "Resurrect / Heal Fully", World, "Characters", "Heals fully (all health, no ailments) - or brings a dead character back to life: any character, the main character too (in its By Death event - as Lord British raises the Avatar: the game goes on). Where its body lies, or at a spot; with all its health, or so many hit points.",
            new[] { Who(), Choice("Value", "Do", "Heal fully", "Resurrect"), Choice("Where", "Where (Resurrect)", "Where its body lies", "At a spot"), new("X", "The spot (At a spot)", ScriptFieldKind.Spot),
                    Choice("Health", "Health (Resurrect)", "All of it", "This many hit points"), F("HP", "Hit points (formula)", def: "10") }, "{Who}: {Value}"),
        new(EventCommandType.LearnLanguage, "Learn / Forget Language", World, "Characters", "A character comes to know a language (System Settings > Languages) - or forgets it. While someone in the party knows it, words written in it (a Show Text, a book) read plainly; else they show in its font, as Serpent Isle's runes.",
            new[] { Who(), new("Language", "Language", ScriptFieldKind.Language), Choice("Value", "Do", "Learn", "Forget") }, "{Who}: {Value} {LanguageName}"),
        new(EventCommandType.ShowBark, "Show Bark", World, "Characters", "Words floating over a character's head for a moment (Exult's item_say) - not a dialog box. The event doesn't wait.",
            new[] { Who(), new("Value", "Words", ScriptFieldKind.Words, "Hello!"), F("Seconds", "For how many seconds", def: "3") }, "{Who}: {Value}"),

        // ---- 8. Objects and the map ----
        new(EventCommandType.CreateObject, "Create Object", World, "Objects & Map", "Puts a new object (or item) on the map at a spot.",
            new[] { new("Category", "Object or item", ScriptFieldKind.Item), new("X", "At", ScriptFieldKind.Spot), F("Quantity", "How many (a stack)", def: "1") }, "{Category}"),
        new(EventCommandType.DeleteObject, "Delete Object", World, "Objects & Map", "Takes a thing off the map for good.",
            new ScriptField[] { new("Thing", "Thing", ScriptFieldKind.Thing) }, "{Thing}"),
        new(EventCommandType.DeleteObjectSelf, "Delete Object Self", World, "Objects & Map", "Takes this object off the map for good: the very copy whose event this is, wherever it was placed. On an object's own page (a fire's By After Put Out). Nothing happens if the object is carried.",
            Array.Empty<ScriptField>(), "", HasWindow: false),
        new(EventCommandType.MoveObject, "Move Object", World, "Objects & Map", "Moves a thing on the map to another spot (and floor).",
            new ScriptField[] { new("Thing", "Thing", ScriptFieldKind.Thing), new("ToX", "To", ScriptFieldKind.Spot) }, "{Thing}"),
        new(EventCommandType.ChangeObjectLook, "Change Object Look", World, "Objects & Map", "Shows a thing with another sprite (a torch lit, a lever pulled) - or back to its own.",
            new[] { new("Thing", "Thing", ScriptFieldKind.Thing), new("SpriteGroup", "New look", ScriptFieldKind.Sprite), Check("Own", "Back to its own look") }, "{Thing}"),
        new(EventCommandType.SetDoorState, "Lock / Unlock / Close Door", World, "Objects & Map", "Locks, unlocks, opens or closes a door (or a chest) at once - no key needed.",
            new[] { new("Thing", "Door or chest", ScriptFieldKind.Thing), Choice("Value", "Make it", "Locked", "Unlocked", "Closed", "Open") }, "{Thing}: {Value}"),
        new(EventCommandType.CountItems, "Count Items", World, "Objects & Map", "How many of an item someone carries (or the whole party, or a container on the map) - into a variable.",
            new[] { new("Category", "Item", ScriptFieldKind.Item), Choice("Where", "Carried by", "Who (below)", "The whole party", "A container on the map"), Who(), new("Thing", "Container (for a container)", ScriptFieldKind.Thing), Var() }, "{Variable} = {Category} of {Where}"),
        new(EventCommandType.SetContainerContents, "Set Container Contents", World, "Objects & Map", "Puts items into a container on the map - or empties it first.",
            new[] { new("Thing", "Container", ScriptFieldKind.Thing), Check("Empty", "Empty it first"), new("Category", "Item to put in (optional)", ScriptFieldKind.Item), F("Quantity", "How many", def: "1") }, "{Thing}"),

        // ---- 9. Screen, camera and time ----
        new(EventCommandType.FadeScreen, "Fade Out / Fade In", Game, "Screen & Camera", "To black (or a color) over a time, and back. The commands after it wait till it's done.",
            new[] { Choice("Value", "Fade", "Out", "In"), F("Seconds", "Over how many seconds", def: "1"), F("Color", "Color (#RRGGBB)", ScriptFieldKind.Text, "#000000") }, "{Value} over {Seconds} s"),
        new(EventCommandType.FlashScreen, "Flash Screen", Game, "Screen & Camera", "A quick color flash over the game (lightning, a spell).",
            new[] { F("Color", "Color (#RRGGBB)", ScriptFieldKind.Text, "#FFFFFF"), F("Seconds", "For how many seconds", def: "0.3") }, "{Color}"),
        new(EventCommandType.Earthquake, "Earthquake", Game, "Screen & Camera", "Exult's earthquake: the view shakes (a few pixels each way, every tenth of a second) for a while, with its sound once. As in Ultima VII's quakes, the main character and the party stop where they are and fall and get up again, over and over, till it's over (they can't be moved meanwhile); characters the game controls aren't thrown.",
            new[] { F("Seconds", "How long (seconds)", def: "3"), new("SoundId", "Sound (once, as it starts)", ScriptFieldKind.Sound), Check("Falls", "The party falls and gets up again", true), Check("Wait", "The commands after it wait till it's over", true) }, "{Seconds} s"),
        new(EventCommandType.MoveCamera, "Move Camera", Game, "Screen & Camera", "Slides the view to a spot (the player stays where they are); \"Back to the player\" returns it. The commands after it wait till it arrives.",
            new[] { Choice("Value", "Move", "To a spot", "Back to the player"), new("X", "Spot", ScriptFieldKind.Spot), F("Seconds", "Over how many seconds", def: "1") }, "{Value}"),
        new(EventCommandType.CameraFollow, "Camera Follows", Game, "Screen & Camera", "The view follows a character other than the player, until it's back on the player.",
            new[] { Who("Character (Main Player = back to normal)") }, "{Who}"),
        new(EventCommandType.ShowPicture, "Show Picture", Game, "Screen & Camera", "A sprite shown over the game (a title card, a map, a portrait) at a position, numbered so it can be erased.",
            new[] { F("Number", "Picture number (1-50)", def: "1"), new("SpriteGroup", "Sprite", ScriptFieldKind.Sprite), F("X", "X (pixels from the left, formula)", def: "0"), F("Y", "Y (pixels from the top, formula)", def: "0"), F("Opacity", "Opacity (0-100)", def: "100") }, "#{Number}"),
        new(EventCommandType.ErasePicture, "Erase Picture", Game, "Screen & Camera", "Takes a shown picture away (0 = every picture).",
            new[] { F("Number", "Picture number (0 = all)", def: "1") }, "#{Number}"),
        new(EventCommandType.SetTime, "Set Time / Advance Time", Game, "Time", "Jumps the game clock to an hour, or forward by minutes or hours.",
            new[] { Choice("Value", "Do", "Set the time to", "Advance by minutes", "Advance by hours"), F("Hour", "Hour (0-23) or how many", def: "8"), F("Minute", "Minute (for Set)", def: "0") }, "{Value} {Hour}"),
        new(EventCommandType.ChangeMap, "Change Map", Game, "Time", "Takes the party to a spot of this world, or to another world (Maps tab: one map is one world - a portal to a dark world, say). Another world without a spot: its Start Position. The world left is remembered as it was. Commands after going to another world don't run - put them in that world's events.",
            new[] { new("World", "World", ScriptFieldKind.World), new("X", "To (empty = the world's Start Position)", ScriptFieldKind.Spot), Check("Fade", "Fade out and in", true) }, "{WorldName} ({X}, {Y})"),

        // ---- 10. Sound and music ----
        new(EventCommandType.StopMusic, "Stop / Fade Music", Game, "Sound & Music", "Stops the music (Play Music) - at once or fading out over a time.",
            new[] { F("Seconds", "Fade over how many seconds (0 = at once)", def: "0") }, "{Seconds} s"),
        new(EventCommandType.FadeMusicIn, "Fade Music In", Game, "Sound & Music", "Starts a piece of music quietly and fades it up.",
            new[] { new("SoundId", "Music", ScriptFieldKind.Sound), F("Seconds", "Over how many seconds", def: "2"), Check("Loop", "Loops", true) }, "{SoundId}"),
        new(EventCommandType.SetVolume, "Set Volume", Game, "Sound & Music", "The game's music and sound volume (for a cutscene, say), 0-100.",
            new[] { F("Music", "Music (0-100, formula)", def: "100"), F("Sounds", "Sounds (0-100, formula)", def: "100") }, "music {Music}, sounds {Sounds}"),
        new(EventCommandType.ResumeMapMusic, "Resume Map Music", Game, "Sound & Music", "Stops event music and goes back to the map's own sound (ambient, combat music).", Array.Empty<ScriptField>(), "", HasWindow: false),

        // ---- 11. The game itself ----
        new(EventCommandType.SaveGame, "Save Game", Game, "The Game", "Saves the game into a slot (a number), with a name.",
            new[] { F("Slot", "Slot number (formula)", def: "1"), F("Name", "Save name (formula)", def: "\"Autosave\"") }, "slot {Slot}"),
        new(EventCommandType.LoadGame, "Load Game", Game, "The Game", "Loads the save in a slot (nothing happens if it's empty).",
            new[] { F("Slot", "Slot number (formula)", def: "1") }, "slot {Slot}"),
        new(EventCommandType.OpenSaveLoadScreen, "Open Save/Load Screen", Game, "The Game", "The same as pressing S.", Array.Empty<ScriptField>(), "", HasWindow: false),
        new(EventCommandType.AllowSaving, "Allow / Forbid Saving", Game, "The Game", "No saving in a dungeon or during a cutscene - until allowed again.",
            new[] { Choice("Value", "Saving is", "Forbidden", "Allowed") }, "{Value}"),
        new(EventCommandType.AllowControls, "Allow / Forbid Controls", Game, "The Game", "Combat mode (C), the keys (inventory, Save/Load...) or the whole player control, for a scripted scene - until allowed again.",
            new[] { Choice("What", "What", "Combat mode", "Keys", "Player control"), Choice("Value", "Is", "Forbidden", "Allowed") }, "{What} {Value}"),
        new(EventCommandType.GameOver, "Game Over", Game, "The Game", "The game ends: the death music (System Settings > Combat), and only loading a save or quitting is left.", Array.Empty<ScriptField>(), "", HasWindow: false),
        new(EventCommandType.ReturnToTitle, "Return To Title", Game, "The Game", "Back to the Title Screen (Tools > Title Screen).", Array.Empty<ScriptField>(), "", HasWindow: false),
        new(EventCommandType.ShowError, "Show Error", Game, "The Game", "Shows an error by its number in the error paperdoll (System Settings > Errors): the engine's numbers, or your own added there. Detail fills %errdetail (and the words of a number not in the list). The event goes on once it's closed.",
            new[] { F("Number", "Error number (formula)", def: "1000"), new("Detail", "Detail (fills %errdetail)", ScriptFieldKind.Words) }, "#{Number} {Detail}"),
        new(EventCommandType.QuitGame, "Quit Game", Game, "The Game", "Closes the game at once (no question - use the Quit paperdoll for that).", Array.Empty<ScriptField>(), "", HasWindow: false),
        new(EventCommandType.LearnSpell, "Learn Spell", Game, "The Game", "Puts a spell into a character's spellbook (the spellbook item they carry): the spell's name as written in the book.",
            new[] { Who(), F("Value", "Spell name (formula)", def: "\"\"") }, "{Who}: {Value}"),
        new(EventCommandType.CastSpell, "Cast Spell", Game, "The Game", "A character casts a spell from its spellbook (reagents and mana as the book says) - at a target if the spell needs one.",
            new[] { Who("Caster"), F("Value", "Spell name (formula)", def: "\"\""), new("Other", "At (target, optional)", ScriptFieldKind.Who) }, "{Who}: {Value}"),

        // ---- From Ultima 8, for every game (build 511) ----
        new(EventCommandType.FeignDeath, "Feign Death", World, "Characters", "They fall and get up again (the Die or Body pose, then Stand Up, if they have them). For the seconds after that, characters marked Fooled by Feign Death (Character Editor > Health) leave them be. Ultima 8's Mask of Death; there it fools undead and demons, for 30 seconds.",
            new[] { Who(), F("Seconds", "How long (seconds)", def: "30"), new("SoundId", "Sound when it ends", ScriptFieldKind.Sound) }, "{Who} for {Seconds} s"),
        new(EventCommandType.WithstandDeath, "Withstand Death", World, "Characters", "The next blow that would kill them restores their full health instead, once. It lasts until it is used. Ultima 8's Withstand Death.",
            new[] { Who(), Choice("Value", "Do", "Give it", "Take it away") }, "{Who}: {Value}"),
        new(EventCommandType.Changeling, "Changeling", World, "Characters", "They look like another character (a tree: a character whose poses show a tree) and stand waiting, after the event has gone on. When an enemy comes within reach, or they are hurt, they look like themselves again. Ultima 8's changelings. Not the main character.",
            new[] { Who(), new("Other", "Looks like", ScriptFieldKind.Who), F("Cells", "An enemy within (map cells)", def: "5"), Check("Attacks", "Then attacks it", true) }, "{Who} looks like {Other}, within {Cells}"),
        new(EventCommandType.FindObjectsInArea, "Find Objects In Area", AreasTab, "Areas & Rituals", "How many of an object lie around someone, a spot or this object, on that floor - into a variable. A ritual: are the reagents in their circles, are the candles lit?",
            new[] { Choice("Around", "Around", "Who (below)", "A spot (below)", "This object"), Who(), new("X", "Spot", ScriptFieldKind.Spot), F("Cells", "Within (map cells; 0: that cell)", def: "0"), new("Category", "Object or item (optional: anything)", ScriptFieldKind.Item), Check("OnlyLit", "Lit ones only"), Var() }, "{Variable} = {Category} around {Around}, {Cells} cells"),
        new(EventCommandType.DeleteObjectsInArea, "Delete Objects In Area", AreasTab, "Areas & Rituals", "Takes things off the map for good, around someone, a spot or this object, on that floor: the reagents a ritual uses up, the wreck of a blast.",
            new[] { Choice("Around", "Around", "Who (below)", "A spot (below)", "This object"), Who(), new("X", "Spot", ScriptFieldKind.Spot), F("Cells", "Within (map cells; 0: that cell)", def: "0"), new("Category", "Object or item (optional: anything)", ScriptFieldKind.Item), Check("OnlyLit", "Lit ones only"), F("Most", "At most (0: all)", def: "0") }, "{Category} around {Around}, {Cells} cells"),
        new(EventCommandType.ItemsInside, "Items Inside", AreasTab, "Areas & Rituals", "How many of an item are in a bag someone carries - into a variable, and taken away if asked. A ritual: the right reagents in the bag, used up as the focus is made.",
            new[] { Who("Who carries it"), F("BagName", "The bag (its name in its editor)", ScriptFieldKind.Text), new("Category", "Item (optional: anything)", ScriptFieldKind.Item), Var(), Check("Remove", "Take them away") }, "{Variable} = {Category} in {BagName} of {Who}"),
        new(EventCommandType.ItemCharges, "Item Charges", AreasTab, "Areas & Rituals", "Reads or changes the charges of an item someone carries: a focus, a wand, a charged book.",
            new[] { Who("Who carries it"), new("Category", "Item", ScriptFieldKind.Item), Choice("Operation", "Do", "Read", "Set", "Add"), F("Amount", "Amount (formula)", def: "1"), Var("Variable", "Into the variable (optional)") }, "{Operation} {Category} of {Who}"),
        new(EventCommandType.HurtInArea, "Hurt In Area", AreasTab, "Areas & Rituals", "Everyone chosen around someone, a spot or this object takes this damage, of this type, by their own rules for it (Character Editor > Health). Grant Peace: Undead, only those with a rule for it. Who is the one doing it: never hurt, and gets the experience for a kill.",
            new[] { Choice("Around", "Around", "Who (below)", "A spot (below)", "This object"), Who("Who does it"), new("X", "Spot", ScriptFieldKind.Spot), F("Cells", "Within (map cells)", def: "5"), F("Amount", "Damage (formula)", def: "10"), Choice("DamageType", "Damage type", Enum.GetNames<DamageType>()), Choice("Affects", "Who it strikes", "Everyone else", "Enemies of Who", "Only those with a rule for this type") }, "{Amount} {DamageType} within {Cells}"),

        // ---- Ultima 8 projects only: the keyboard (Engine/U8Keys.cs) ----
        new(EventCommandType.U8AskKey, "Ask For Key", Ultima8Tab, "Keyboard", "The event waits for the player to press a key, and that key does this action from now on - kept for good, through new games and loads. %key... on a paperdoll shows the waiting words meanwhile. A key can be held with Ctrl, Shift or Alt. The Cancel key: no change (\"last command succeeded\" is false).",
            new[] { Choice("Action", "The action", U8Keys.ActionNames), Choice("IfUsed", "If another action has that key", "Swap", "Fail") }, "{Action}"),
        new(EventCommandType.U8SetKey, "Set Key", Ultima8Tab, "Keyboard", "This key does this action from now on - kept for good, through new games and loads.",
            new[] { Choice("Action", "The action", U8Keys.ActionNames), Choice("Key", "The key", U8Keys.BindableWords), Choice("With", "Held with", new[] { "Nothing" }.Concat(U8Keys.HeldWith).ToArray()), Choice("IfUsed", "If another action has that key", "Swap", "Fail") }, "{Action}: {Key}"),
        new(EventCommandType.U8GetKeyName, "Get Key Name", Ultima8Tab, "Keyboard", "The name of an action's key, as words, into a variable (for a Show Text: \"Press Space to jump\").",
            new[] { Choice("Action", "The action", U8Keys.ActionNames), Var() }, "{Action} into {Variable}"),
        new(EventCommandType.U8ResetKeys, "Reset Keys", Ultima8Tab, "Keyboard", "Forgets what the player changed: an action, or every key, is back to the keys in System Settings > Ultima 8.",
            new[] { Choice("Action", "Which", new[] { "Every key" }.Concat(U8Keys.ActionNames).ToArray()) }, "{Action}"),

        new(EventCommandType.U8BuildUpStat, "Build Up Stat", Ultima8Tab, "Stats", "Adds points toward a stat, when System Settings > Ultima 8 has Stats grow by use on: each time a chance of a gain, certain at the points set there. In Ultima 8 Intelligence grows only this way: reading, a teacher, a spell learned.",
            new[] { Who(), Choice("Stat", "Stat", "Intelligence", "Strength", "Dexterity"), F("Points", "Points (formula)", def: "5") }, "{Who}: {Points} toward {Stat}"),

        new(EventCommandType.U8MovePlatform, "Move Platform", Ultima8Tab, "World", "A placed thing goes up or down, carrying whatever is on it - things and people. Below 0 goes down.",
            new[] { new("Thing", "Platform", ScriptFieldKind.Thing), F("Lifts", "Lifts (below 0: down)", def: "4"), F("Seconds", "Over (seconds)", def: "2"), Check("Wait", "The event waits till it stops", true) }, "{Thing} by {Lifts}"),
        new(EventCommandType.U8WeightOn, "Weight On", Ultima8Tab, "World", "How much lies on a placed thing, into a variable: objects, containers and whoever stands on it (their Body weight). A pressure plate. Advance IF has it too: Weight on an object.",
            new[] { new("Thing", "Thing", ScriptFieldKind.Thing), Var() }, "{Variable} = weight on {Thing}"),
        new(EventCommandType.U8FlipScreen, "Flip Screen", Ultima8Tab, "Screen", "The world turns upside down over a time, or back (Ultima 8's inverter). The event waits for it.",
            new[] { Choice("Value", "Turn it", "The other way", "Upside down", "Right way up"), F("Seconds", "Over (seconds)", def: "1") }, "{Value}"),
        new(EventCommandType.U8Speak, "Speak", Ultima8Tab, "Screen", "Words over a character's head with their voice: a sound from the Sound list, for as long as you say.",
            new[] { Who(), F("Value", "Words", ScriptFieldKind.Words), new("SoundId", "Voice (a sound)", ScriptFieldKind.Sound), F("Seconds", "Stays (seconds)", def: "3"), Check("Wait", "The event waits") }, "{Who}: {Value}"),
        new(EventCommandType.U8ReadingScreen, "Reading Screen", Ultima8Tab, "Screen", "A picture with words on it in the middle of the screen: a scroll, a plaque, a gravestone. A click or a key closes it; the event waits, the world goes on.",
            new[] { new("SpriteGroup", "Picture", ScriptFieldKind.Sprite), F("Value", "Words", ScriptFieldKind.Words) }, "{Value}"),
        new(EventCommandType.U8Keypad, "Keypad", CrusaderTab, "Crusader", "A keypad: the player presses digits and OK. The code matched: Last command succeeded is true. The event waits.",
            new[] { F("Code", "The code (formula)", def: "1234"), F("Prompt", "Words above it", ScriptFieldKind.Text), Var("Variable", "What was typed, into (optional)") }, "code {Code}"),
        new(EventCommandType.U8Computer, "Computer", CrusaderTab, "Crusader", "Words typed out on a computer screen, a letter at a time. A click shows them all, the next goes on. The event waits.",
            new[] { F("Value", "Words", ScriptFieldKind.Words), F("MsPerLetter", "Milliseconds a letter", def: "30") }, "{Value}"),
        new(EventCommandType.U8Recharge, "Recharge", CrusaderTab, "Crusader", "Health or Mana fills up over time, until full or the seconds run out: a charger, a healing station. The event waits; the character stands there.",
            new[] { Who(), Choice("Stat", "What", "Mana", "Health"), F("PerSecond", "A second", def: "5"), F("Seconds", "At most (seconds)", def: "20") }, "{Who}: {Stat}"),
        new(EventCommandType.U8Alarm, "Alarm", CrusaderTab, "Crusader", "Everyone of a faction within reach goes for someone. Those who surrendered don't.",
            new[] { new("Faction", "Faction", ScriptFieldKind.Faction), Who("Goes for"), F("Cells", "Within (map cells)", def: "30") }, "{Faction} go for {Who}"),

        // ---- Vehicles (Map Editor > Vehicles tab) ----
        new(EventCommandType.DriveVehicle, "Drive Vehicle", World, "Vehicles", "Puts the main player at the controls of a vehicle (the copy of it nearest them): the direction keys drive it, as if they had taken the controls themselves - no deed asked. A flying one takes off.",
            new[] { new ScriptField("Vehicle", "Vehicle", ScriptFieldKind.Vehicle) }, "{VehicleName}"),
        new(EventCommandType.StopDriving, "Stop Driving", World, "Vehicles", "Takes the controls from the player. A flying vehicle comes down - if the ground under it is clear and level; otherwise it stays up.", Array.Empty<ScriptField>(), "", HasWindow: false),
        new(EventCommandType.TravelVehicle, "Travel Vehicle", World, "Vehicles", "The vehicle goes to a spot by itself (Final Fantasy's airship): everyone aboard can walk about it, talk and open things on the way. A flying one takes off, flies over everything and lands there if it can. Blocked on the way, it stops.",
            new[] { new ScriptField("Vehicle", "Vehicle", ScriptFieldKind.Vehicle), new("X", "To (its middle there)", ScriptFieldKind.Spot), Check("Wait", "The event waits till it gets there", true) }, "{VehicleName} to ({X}, {Y})"),
        new(EventCommandType.SetVehicleSpeed, "Set Vehicle Speed", World, "Vehicles", "How fast a vehicle goes from now on, in map cells a second (walking is about 3) - a dashboard's gas pedal or gear buttons. 0 = its own speed (Vehicles tab > Settings).",
            new[] { new ScriptField("Vehicle", "Vehicle", ScriptFieldKind.Vehicle), F("Speed", "Speed (map cells a second, formula)", def: "5") }, "{VehicleName}: {Speed}"),

        // ---- 12. Testing helpers ----
        new(EventCommandType.DebugLog, "Debug Log", Loops, "Testing", "Writes a line into the Game Test log (F9 shows it) - with values: \"i = \" + i. A built game ignores it.",
            new[] { F("Value", "Line (formula)", def: "\"Here\"") }, "{Value}"),
        new(EventCommandType.PauseHere, "Pause Here", Loops, "Testing", "Stops the event here in Game Test and shows every variable, text, array and grid; Continue goes on. A built game ignores it.",
            Array.Empty<ScriptField>(), "", HasWindow: false),
    };

    private static readonly Dictionary<EventCommandType, ScriptCommandSpec> ByType = All.ToDictionary(s => s.Type);

    public static ScriptCommandSpec? Of(EventCommandType type) => ByType.TryGetValue(type, out var spec) ? spec : null;

    public static bool IsScript(EventCommandType type) => ByType.ContainsKey(type);

    public static ScriptBlockRole RoleOf(EventCommandType type) => ByType.TryGetValue(type, out var spec) ? spec.Role : ScriptBlockRole.None;

    public static bool IsLoopOpener(EventCommandType type) => type is EventCommandType.While or EventCommandType.RepeatTimes or EventCommandType.ForEach;

    /// <summary>The End line that closes an opener.</summary>
    public static EventCommandType? CloserOf(EventCommandType type) => type switch
    {
        EventCommandType.While => EventCommandType.EndWhile,
        EventCommandType.RepeatTimes => EventCommandType.EndRepeat,
        EventCommandType.ForEach => EventCommandType.EndForEach,
        EventCommandType.SelectCase => EventCommandType.EndSelect,
        _ => null
    };

    public static EventCommandType? OpenerOf(EventCommandType type) => type switch
    {
        EventCommandType.EndWhile => EventCommandType.While,
        EventCommandType.EndRepeat => EventCommandType.RepeatTimes,
        EventCommandType.EndForEach => EventCommandType.ForEach,
        EventCommandType.EndSelect => EventCommandType.SelectCase,
        _ => null
    };
}
