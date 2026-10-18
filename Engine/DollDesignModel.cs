namespace SandcastleRpgMaker.Engine;

/// <summary>
/// The kinds of part a paperdoll (PaperDoll Editor) is built from - the
/// toolbox. Every part's look, animation and sound come from the Sprite
/// Editor; what it does beyond its own built-in behaviour is programmed on
/// the part itself (its event pages).
/// </summary>
public enum DollPartKind
{
    /// <summary>A still or animated picture: the body of a radio, a coffin's wood, a robot's flashing lights.</summary>
    Picture,
    /// <summary>Shows its Pressed look while clicked: Play, Rec, Eject.</summary>
    Button,
    /// <summary>Steps through its looks one click at a time and stays there: a power switch, a lever with three positions.</summary>
    Switch,
    /// <summary>Closed / open, toggled by double-click; while closed, the parts it covers are hidden or out of reach: a cassette door, a coffin lid.</summary>
    Lid,
    /// <summary>A hole that takes only the items it lists, each shown in its own fitted look while inside: a cassette tray, a battery holder, a missing machine part.</summary>
    Slot,
    /// <summary>A free area that holds any items, like a container: inside a coffin, a shelf.</summary>
    ItemArea,
    /// <summary>Words, in a font from the Sprite Editor's Fonts: a sign, a plaque, a gravestone.</summary>
    Text,
    /// <summary>A knob dragged along a track for a value between its Min and Max: volume, a safe's dial.</summary>
    Slider,
    /// <summary>Shows a value as the frame of its sprite (frame 0 = the lowest): a tape counter, a gauge.</summary>
    Display,
    /// <summary>Off / on, not clickable; switched by events: a power light.</summary>
    Light,
    /// <summary>An invisible clickable area over part of a picture: a keyhole, a crack.</summary>
    Hotspot,
    /// <summary>Closes the paperdoll when clicked.</summary>
    CloseButton,
    /// <summary>Save/Load screen: the list of saved games, newest first - click picks one, double-click loads it, the mouse wheel scrolls.</summary>
    SaveList,
    /// <summary>Save/Load screen: where the player types the save's name (picking a save puts its name here).</summary>
    SaveName,
    /// <summary>Save/Load screen: the picked save's screenshot, fitted into its area.</summary>
    SaveScreenshot,
    /// <summary>Save/Load screen: the picked save's details - its name, the game's day and time, when it was saved.</summary>
    SaveDetails,
    /// <summary>Save/Load screen: saves the game under the typed name (over the picked save, after asking).</summary>
    SaveButton,
    /// <summary>Save/Load screen: loads the picked save.</summary>
    LoadButton,
    /// <summary>Save/Load screen: deletes the picked save, after asking.</summary>
    DeleteSaveButton
}

/// <summary>One look (state) of a part: its name and its sprite.</summary>
public class DollPartLook
{
    public string Name { get; set; } = string.Empty;
    public SpriteLink Sprite { get; set; } = new();
}

/// <summary>An item a Slot takes, and how it shows while inside (its fitted sprite, else its own look), placed this far from the slot's top-left.</summary>
public class DollSlotFit
{
    public ItemReference Item { get; set; } = new() { Category = ReferencedItemCategory.Object };
    public SpriteLink FittedSprite { get; set; } = new();
    public int OffsetX { get; set; }
    public int OffsetY { get; set; }
}

/// <summary>One part of a paperdoll, drawn in list order (later parts on top).</summary>
public class DollPart
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DollPartKind Kind { get; set; } = DollPartKind.Picture;
    /// <summary>Top-left corner on the paperdoll, in pixels.</summary>
    public int X { get; set; }
    public int Y { get; set; }
    /// <summary>Size of a part drawn without a sprite (a slot's or area's hole, a hotspot, a text's wrapping width, a slider's track).</summary>
    public int Width { get; set; } = 32;
    public int Height { get; set; } = 32;
    public bool StartsVisible { get; set; } = true;
    /// <summary>In play it can be dragged onto other parts (By Combined With A Part on them) - it goes back to its place when let go: a key part onto a keyhole.</summary>
    public bool Draggable { get; set; }
    /// <summary>Its looks (Button: Normal, Pressed. Switch: one per position. Lid: Closed, Open. Light: Off, On. Slider: Track, Knob. Slot / Item area: a picture behind the items).</summary>
    public List<DollPartLook> Looks { get; set; } = new();
    /// <summary>The look it starts with (a Lid starting open = 1).</summary>
    public int StartLook { get; set; }

    // ---- Lid ----
    /// <summary>The parts it covers (by id).</summary>
    public List<int> Covers { get; set; } = new();
    /// <summary>Closed: the covered parts are hidden (true), or still seen but out of reach (false, a glass door).</summary>
    public bool HidesWhatItCovers { get; set; } = true;

    // ---- Slot / Item area ----
    /// <summary>Slot: the items it takes (none listed = anything).</summary>
    public List<DollSlotFit> Accepts { get; set; } = new();
    /// <summary>How many items it holds (a stack counts once).</summary>
    public int Capacity { get; set; } = 1;
    /// <summary>What it holds at the start (each placed copy gets its own).</summary>
    public List<ItemReference> StartsWith { get; set; } = new();

    // ---- Text ----
    public string Text { get; set; } = string.Empty;
    /// <summary>A font from the Sprite Editor's Fonts ("" = the in-game font).</summary>
    public string FontName { get; set; } = string.Empty;
    public int TextScale { get; set; } = 1;
    /// <summary>Tint, #RRGGBB ("" = the font's own colours).</summary>
    public string TextColor { get; set; } = string.Empty;

    // ---- Slider / Display ----
    public int Min { get; set; }
    public int Max { get; set; } = 10;
    public int StartValue { get; set; }
    public bool Vertical { get; set; }
    /// <summary>Display: the slider whose value it shows (-1 = its own value, set by events).</summary>
    public int ShowsPartId { get; set; } = -1;

    // ---- Save slot ----
    /// <summary>Which saved game it holds (1, 2, 3...); 0 = by its place among the paperdoll's Save slots.</summary>
    public int SaveSlot { get; set; }

    /// <summary>What it does when used (PaperDoll Editor: double-click the part).</summary>
    public List<BookEventPage> EventPages { get; set; } = new();

    public static string KindName(DollPartKind kind) => kind switch
    {
        DollPartKind.ItemArea => "Item area",
        DollPartKind.CloseButton => "Close button",
        DollPartKind.Lid => "Lid / door / hatch",
        DollPartKind.Slider => "Dial / slider",
        DollPartKind.SaveList => "Save list",
        DollPartKind.SaveName => "Save slot",
        DollPartKind.SaveScreenshot => "Save screenshot",
        DollPartKind.SaveDetails => "Save details",
        DollPartKind.SaveButton => "Save button",
        DollPartKind.LoadButton => "Load button",
        DollPartKind.DeleteSaveButton => "Delete save button",
        _ => kind.ToString()
    };

    /// <summary>The names of the looks a new part of this kind starts with.</summary>
    public static string[] DefaultLooks(DollPartKind kind) => kind switch
    {
        DollPartKind.Picture => new[] { "Picture" },
        DollPartKind.Button or DollPartKind.CloseButton or DollPartKind.SaveButton or DollPartKind.LoadButton or DollPartKind.DeleteSaveButton => new[] { "Normal", "Pressed" },
        DollPartKind.SaveList => new[] { "Behind the list", "Picked row" },
        DollPartKind.SaveName => new[] { "Slot", "Picked slot" },
        DollPartKind.SaveScreenshot => new[] { "Frame (drawn over the screenshot)" },
        DollPartKind.SaveDetails => new[] { "Behind the details" },
        DollPartKind.Switch => new[] { "Off", "On" },
        DollPartKind.Lid => new[] { "Closed", "Open" },
        DollPartKind.Light => new[] { "Off", "On" },
        DollPartKind.Slider => new[] { "Track", "Knob" },
        DollPartKind.Display => new[] { "Frames (one per value)" },
        DollPartKind.Slot or DollPartKind.ItemArea => new[] { "Behind the items" },
        _ => Array.Empty<string>()
    };

    /// <summary>The Save/Load screen's own parts (System Settings > Save / Load): they work only on that paperdoll.</summary>
    public static bool IsSaveLoadKind(DollPartKind kind) => kind >= DollPartKind.SaveList;

    /// <summary>Parts drawn as an area of their own Width x Height, whatever their sprite (a list, a name box, a screenshot).</summary>
    public static bool IsAreaKind(DollPartKind kind) =>
        kind is DollPartKind.Slot or DollPartKind.ItemArea or DollPartKind.Hotspot or DollPartKind.Text
            or DollPartKind.SaveList or DollPartKind.SaveName or DollPartKind.SaveScreenshot or DollPartKind.SaveDetails;

    /// <summary>Kinds whose list of looks can grow (a switch with three positions).</summary>
    public static bool LooksCanGrow(DollPartKind kind) => kind is DollPartKind.Switch;

    /// <summary>The saved game a Save slot holds: its own number, or (none set) its place among the paperdoll's Save slots.</summary>
    public static int SlotNumber(DollDesign design, DollPart part) =>
        part.SaveSlot > 0 ? part.SaveSlot : design.Parts.Where(p => p.Kind == DollPartKind.SaveName).ToList().IndexOf(part) + 1;

    public DollPart Clone(int newId) => new()
    {
        Id = newId, Name = Name, Kind = Kind, X = X, Y = Y, Width = Width, Height = Height, StartsVisible = StartsVisible, Draggable = Draggable,
        Looks = Looks.Select(l => new DollPartLook { Name = l.Name, Sprite = new SpriteLink { SpriteGroup = l.Sprite.SpriteGroup, SpriteName = l.Sprite.SpriteName } }).ToList(),
        StartLook = StartLook, Covers = new List<int>(Covers), HidesWhatItCovers = HidesWhatItCovers,
        Accepts = Accepts.Select(a => new DollSlotFit
        {
            Item = new ItemReference { Category = a.Item.Category, ItemId = a.Item.ItemId, DisplayName = a.Item.DisplayName },
            FittedSprite = new SpriteLink { SpriteGroup = a.FittedSprite.SpriteGroup, SpriteName = a.FittedSprite.SpriteName },
            OffsetX = a.OffsetX, OffsetY = a.OffsetY
        }).ToList(),
        Capacity = Capacity,
        StartsWith = StartsWith.Select(s => new ItemReference { Category = s.Category, ItemId = s.ItemId, DisplayName = s.DisplayName }).ToList(),
        Text = Text, FontName = FontName, TextScale = TextScale, TextColor = TextColor,
        Min = Min, Max = Max, StartValue = StartValue, Vertical = Vertical, ShowsPartId = ShowsPartId, SaveSlot = SaveSlot
        // Event pages are not copied: a duplicate starts with no behaviour of its own.
    };
}

/// <summary>A paperdoll made in the PaperDoll Editor; any object can open one (Object Editor), as Exult's objects open their gumps.</summary>
public class DollDesign
{
    public int Id { get; set; }
    public string Name { get; set; } = "New Paperdoll";
    /// <summary>The canvas, in pixels (the game shows it at the same scale as a container's window).</summary>
    public int Width { get; set; } = 320;
    public int Height { get; set; } = 240;
    public int OpenSoundResourceId { get; set; } = -1;
    public int CloseSoundResourceId { get; set; } = -1;
    public List<DollPart> Parts { get; set; } = new();

    public int NextPartId() => Parts.Count == 0 ? 1 : Parts.Max(p => p.Id) + 1;
    public DollPart? Part(int id) => Parts.FirstOrDefault(p => p.Id == id);
}

/// <summary>One part of one object's paperdoll as it is now, in play.</summary>
public class DollPartRuntime
{
    public int Look { get; set; }
    public bool Visible { get; set; } = true;
    public int Value { get; set; }
    public List<InventoryItemInstance> Items { get; set; } = new();
}

/// <summary>
/// An object's own paperdoll, as it is now: every placed copy (and every
/// carried one) keeps its own - this radio's door open with its tape in,
/// that one shut and empty. It travels with the object when it's picked up
/// and put down. Made from the design the first time it's opened.
/// </summary>
public class DollState
{
    public int DesignId { get; set; } = -1;
    public Dictionary<int, DollPartRuntime> Parts { get; set; } = new();
    public int NextItemInstanceId { get; set; } = 1;

    /// <summary>The game's "no spot yet" mark for an item in an area (GameTestWindow.UnplacedSpot).</summary>
    private const double UnplacedSpot = -100000;

    public DollPartRuntime Of(DollPart part)
    {
        if (!Parts.TryGetValue(part.Id, out var runtime))
        {
            // A part added in the PaperDoll Editor after this copy was first opened starts as designed.
            runtime = new DollPartRuntime { Look = part.StartLook, Visible = part.StartsVisible, Value = Math.Clamp(part.StartValue, part.Min, Math.Max(part.Min, part.Max)) };
            Parts[part.Id] = runtime;
        }
        return runtime;
    }

    /// <summary>A fresh state for a design: every part as it starts, slots and areas holding their starting items.</summary>
    public static DollState StartOf(DollDesign design)
    {
        var state = new DollState { DesignId = design.Id };
        foreach (var part in design.Parts)
        {
            var runtime = state.Of(part);
            foreach (var start in part.StartsWith.Where(s => s.ItemId >= 0))
                runtime.Items.Add(new InventoryItemInstance
                {
                    InstanceId = state.NextItemInstanceId++, Category = start.Category, ItemId = start.ItemId, Quantity = 1,
                    ContainerX = UnplacedSpot, ContainerY = UnplacedSpot // no spot yet: laid out in a row when first shown, as a container's are
                });
        }
        return state;
    }
}
