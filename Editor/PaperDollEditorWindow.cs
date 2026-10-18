using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The PaperDoll Editor: a canvas and a toolbox of parts - pictures, buttons,
/// switches, lids, slots that take only certain items, item areas, text,
/// dials, displays, lights, hotspots, close buttons. Every part's look,
/// animation and sound come from the Sprite Editor. Any object can open a
/// paperdoll (Object Editor > Opens paperdoll), as Exult's objects open
/// their gumps; in play each placed copy keeps its own (this radio's door
/// open with its tape in, that one shut). Double-click a part to program
/// what it does.
/// </summary>
public class PaperDollEditorWindow : Window
{
    private readonly ObjectDatabase _objects;
    private readonly SpriteDatabase _sprites;
    private readonly ResourceDatabase _resources;
    private readonly BookDatabase _books;
    private readonly ContainerDatabase _containers;
    private readonly VariableDatabase? _variables;
    private readonly StatDatabase? _stats;

    private readonly ListBox _designList = new() { Margin = new Thickness(0, 4, 0, 4), DisplayMemberPath = nameof(DollDesign.Name) };
    private readonly StackPanel _designProps = new();
    private readonly Canvas _canvas = new() { Background = new SolidColorBrush(Color.FromRgb(0x1c, 0x1c, 0x1c)), ClipToBounds = true, Focusable = true };
    private readonly ListBox _partList = new() { Height = 170, Margin = new Thickness(0, 4, 0, 4) };
    private readonly StackPanel _partProps = new();
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11, Margin = new Thickness(0, 6, 0, 0) };
    private readonly ComboBox _zoomBox = new() { Width = 60, ItemsSource = new[] { 1, 2, 3, 4 }, SelectedItem = 2 };
    private readonly CheckBox _showNames = new() { Content = "Show names", IsChecked = true, Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };

    private int _selectedPartId = -1;
    private bool _refreshing;
    private (Point Start, int X, int Y)? _drag;

    private const int PageMargin = 24; // around the paperdoll on the canvas

    private int Zoom => _zoomBox.SelectedItem is int z ? z : 2;
    private DollDesign? Design => _designList.SelectedItem as DollDesign;
    private DollPart? Part => Design?.Part(_selectedPartId);

    public int SelectedDesignId => Design?.Id ?? -1;

    public PaperDollEditorWindow(ObjectDatabase objects, SpriteDatabase sprites, ResourceDatabase resources, BookDatabase books, ContainerDatabase containers,
        VariableDatabase? variables = null, StatDatabase? stats = null, int selectDesignId = -1)
    {
        _objects = objects;
        _sprites = sprites;
        _resources = resources;
        _books = books;
        _containers = containers;
        _variables = variables;
        _stats = stats;
        Title = $"PaperDoll Editor [{BuildInfo.CurrentBuild}]";
        Width = 1360;
        Height = 860;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var root = new Grid { Margin = new Thickness(8) };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(230) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(380) });

        // ---- Paperdolls ----
        var left = new DockPanel { Margin = new Thickness(0, 0, 8, 0) };
        var leftTop = new StackPanel();
        leftTop.Children.Add(new TextBlock { Text = "Paperdolls", FontWeight = FontWeights.Bold });
        var designButtons = new WrapPanel();
        designButtons.Children.Add(MakeButton("New", NewDesign));
        designButtons.Children.Add(MakeButton("Duplicate", DuplicateDesign));
        designButtons.Children.Add(MakeButton("Delete", DeleteDesign));
        leftTop.Children.Add(designButtons);
        DockPanel.SetDock(leftTop, Dock.Top);
        left.Children.Add(leftTop);
        DockPanel.SetDock(_designProps, Dock.Bottom);
        left.Children.Add(_designProps);
        left.Children.Add(_designList);
        Grid.SetColumn(left, 0);
        root.Children.Add(left);

        // ---- Toolbox and canvas ----
        var middle = new DockPanel();
        var top = new StackPanel();
        // The toolbox, as a form designer's: the paperdoll's own parts, the form controls, the Save/Load screen's parts.
        var kinds = Enum.GetValues<DollPartKind>();
        foreach (var (label, group) in new[]
        {
            ("Parts:", kinds.Where(k => !DollPart.IsSaveLoadKind(k) && !(DollPart.IsControlKind(k) && k is not (DollPartKind.Button or DollPartKind.CloseButton)))),
            ("Controls:", kinds.Where(k => DollPart.IsControlKind(k) && !DollPart.IsSaveLoadKind(k) && k is not (DollPartKind.Button or DollPartKind.CloseButton))),
            ("Save / Load:", kinds.Where(DollPart.IsSaveLoadKind))
        })
        {
            var toolbox = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            toolbox.Children.Add(new TextBlock { Text = label, Width = 78, FontWeight = FontWeights.Bold, VerticalAlignment = VerticalAlignment.Center });
            foreach (var kind in group)
            {
                var k = kind;
                var button = MakeButton(DollPart.KindName(kind), () => AddPart(k));
                button.ToolTip = KindHelp(kind);
                toolbox.Children.Add(button);
            }
            top.Children.Add(toolbox);
        }
        var tools = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        tools.Children.Add(new TextBlock { Text = "Zoom", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) });
        tools.Children.Add(_zoomBox);
        tools.Children.Add(_showNames);
        top.Children.Add(tools);
        top.Children.Add(Note("Click a part to pick it; drag to move it. Arrows: 1px (Shift: 8px). Page Up / Page Down: in front of / behind the part next to it. Ctrl+D: duplicate. Delete: remove. " +
                              "Double-click a part to program what it does. Parts are shown as they start; faint = starts hidden, or under a closed lid."));
        DockPanel.SetDock(top, Dock.Top);
        middle.Children.Add(top);
        DockPanel.SetDock(_status, Dock.Bottom);
        middle.Children.Add(_status);
        middle.Children.Add(new ScrollViewer { Content = _canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Grid.SetColumn(middle, 1);
        root.Children.Add(middle);

        // ---- Parts ----
        var right = new StackPanel { Margin = new Thickness(8, 0, 0, 0) };
        right.Children.Add(new TextBlock { Text = "Parts (top of the list = drawn at the back)", FontWeight = FontWeights.Bold });
        var partButtons = new WrapPanel();
        partButtons.Children.Add(MakeButton("Duplicate", DuplicatePart));
        partButtons.Children.Add(MakeButton("Delete", DeletePart));
        partButtons.Children.Add(MakeButton("Backward", () => MovePart(-1)));
        partButtons.Children.Add(MakeButton("Forward", () => MovePart(1)));
        partButtons.Children.Add(MakeButton("Events...", OpenPartEvents));
        right.Children.Add(partButtons);
        right.Children.Add(_partList);
        right.Children.Add(_partProps);
        var rightScroll = new ScrollViewer { Content = right, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetColumn(rightScroll, 2);
        root.Children.Add(rightScroll);

        Content = root;

        _designList.ItemsSource = _objects.DollDesigns;
        _designList.SelectionChanged += (_, _) => { _selectedPartId = Design?.Parts.LastOrDefault()?.Id ?? -1; RefreshAll(); };
        _partList.SelectionChanged += (_, _) =>
        {
            if (_refreshing || Design is not { } design) return;
            _selectedPartId = _partList.SelectedIndex >= 0 && _partList.SelectedIndex < design.Parts.Count ? design.Parts[_partList.SelectedIndex].Id : -1;
            ShowPartProps();
            Redraw();
        };
        _zoomBox.SelectionChanged += (_, _) => Redraw();
        _showNames.Click += (_, _) => Redraw();
        _canvas.MouseLeftButtonDown += Canvas_MouseLeftButtonDown;
        _canvas.MouseMove += Canvas_MouseMove;
        _canvas.MouseLeftButtonUp += (_, _) => { if (_drag is not null) { _drag = null; _canvas.ReleaseMouseCapture(); ShowPartProps(); } };
        PreviewKeyDown += Window_PreviewKeyDown;

        _designList.SelectedItem = _objects.DollDesigns.FirstOrDefault(d => d.Id == selectDesignId) ?? _objects.DollDesigns.FirstOrDefault();
        RefreshAll();
    }

    private static string KindHelp(DollPartKind kind) => kind switch
    {
        DollPartKind.Picture => "A still or animated picture (its sprite's frames and sounds): the body of a radio, a coffin's wood, flashing lights.",
        DollPartKind.Button => "Shows its Pressed look while clicked: Play, Rec, Eject.",
        DollPartKind.Switch => "Each click moves it to its next look, and it stays there: a power switch, a three-way lever.",
        DollPartKind.Lid => "Double-click opens / closes it. While closed, what it covers is hidden (or seen but out of reach): a cassette door, a coffin lid.",
        DollPartKind.Slot => "A hole that takes only the items you list, each shown in its own fitted look while inside: a cassette tray, a battery holder, a missing part.",
        DollPartKind.ItemArea => "Holds any items, like a container: inside a coffin, a shelf.",
        DollPartKind.Text => "Words in a font from the Sprite Editor's Fonts: a sign, a plaque, a gravestone.",
        DollPartKind.Slider => "Click along its track to set its value: volume, a safe's dial. A Display can show it.",
        DollPartKind.Display => "Shows a value as its sprite's frame (frame 1 = the lowest value): a counter, a gauge.",
        DollPartKind.Light => "Off / on; not clickable - switched by events: a power light.",
        DollPartKind.Hotspot => "An invisible clickable area over part of a picture: a keyhole, a crack.",
        DollPartKind.CloseButton => "Closes the paperdoll when clicked.",
        DollPartKind.SaveList => "Save/Load screen: the saved games, newest first, in its font. Click picks one, double-click loads it, the mouse wheel scrolls. Its \"Picked row\" look is drawn behind the picked one.",
        DollPartKind.SaveName => "Save/Load screen: one saved game, as Exult's slots - add as many as you like, each holds its own save (its slot number). Click it to pick it, type the save's name in it, then Save, Load or Delete; double-click loads it.",
        DollPartKind.SaveScreenshot => "Save/Load screen: the picked save's screenshot, fitted into this area (its Frame look is drawn over it).",
        DollPartKind.SaveDetails => "Save/Load screen: the picked save's name, the game's day and time, and when it was saved, in its font.",
        DollPartKind.SaveButton => "Save/Load screen: saves into the picked Save slot under its typed name (asking first when the slot holds a save). With a Save list and no slot picked: a new save, or over the one with the same name.",
        DollPartKind.LoadButton => "Save/Load screen: loads the picked save (double-clicking it does too).",
        DollPartKind.DeleteSaveButton => "Save/Load screen: deletes the picked save, after asking.",
        DollPartKind.ConfirmButton => "Yes / OK: closes its paperdoll answering yes. On the \"Are you sure?\" save prompt (System Settings > Save / Load) the save goes ahead; opened by Open Paperdoll, its answer (1) goes into a variable. Its caption can be words or its sprite.",
        DollPartKind.CancelButton => "No / Cancel: closes its paperdoll answering no, back to the paperdoll behind it. Caption it OK for a message that only needs closing.",
        DollPartKind.TextBox => "A box the player clicks and types in (Enter ends it). Numbers only: its number goes into its variable.",
        DollPartKind.CheckBox => "Ticked / unticked by a click, its words beside it: 1 / 0 in its variable.",
        DollPartKind.RadioButton => "One choice of a group (same Radio group): picking it unpicks the others. 1 / 0 in its variable.",
        DollPartKind.DropDown => "A box that opens its list of choices when clicked; the picked one (1, 2, 3...) goes into its variable.",
        DollPartKind.ListBox => "A list of choices always shown; a click picks one (1, 2, 3...) - into its variable.",
        DollPartKind.ProgressBar => "A bar filled up to its value between Min and Max - its own (set by events or its variable) or a slider's. Not clickable: health, loading.",
        _ => ""
    };

    // ================= Paperdolls =================

    private void NewDesign()
    {
        var design = new DollDesign { Id = _objects.NextDollDesignId(), Name = $"Paperdoll {_objects.DollDesigns.Count + 1}" };
        _objects.DollDesigns.Add(design);
        _designList.Items.Refresh();
        _designList.SelectedItem = design;
        _status.Text = "New paperdoll: start with a Picture for its body, then add buttons, lids, slots... from the toolbox above.";
    }

    private void DuplicateDesign()
    {
        if (Design is not { } source) return;
        var copy = new DollDesign
        {
            Id = _objects.NextDollDesignId(), Name = source.Name + " copy", Width = source.Width, Height = source.Height,
            OpenSoundResourceId = source.OpenSoundResourceId, CloseSoundResourceId = source.CloseSoundResourceId
        };
        // Same part ids, so lids still cover the same parts and displays still follow the same sliders.
        foreach (var part in source.Parts) copy.Parts.Add(part.Clone(part.Id));
        _objects.DollDesigns.Add(copy);
        _designList.Items.Refresh();
        _designList.SelectedItem = copy;
    }

    private void DeleteDesign()
    {
        if (Design is not { } design) return;
        var users = _objects.Objects.Where(o => o.DollDesignId == design.Id).Select(o => o.Name).ToList();
        string question = users.Count == 0 ? $"Delete \"{design.Name}\"?" : $"Delete \"{design.Name}\"? It is opened by: {string.Join(", ", users)} - they won't open a paperdoll any more.";
        if (MessageBox.Show(this, question, "PaperDoll Editor", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var obj in _objects.Objects.Where(o => o.DollDesignId == design.Id)) obj.DollDesignId = -1;
        _objects.DollDesigns.Remove(design);
        _designList.Items.Refresh();
        _designList.SelectedItem = _objects.DollDesigns.FirstOrDefault();
    }

    private void ShowDesignProps()
    {
        _designProps.Children.Clear();
        if (Design is not { } design) { _designProps.Children.Add(Note("Make a paperdoll with New.")); return; }
        _designProps.Children.Add(Field("Name", design.Name, v => { design.Name = v; _designList.Items.Refresh(); }, 210));
        var size = new WrapPanel();
        size.Children.Add(IntField("Width (px)", () => design.Width, v => design.Width = Math.Clamp(v, 16, 2000)));
        size.Children.Add(IntField("Height (px)", () => design.Height, v => design.Height = Math.Clamp(v, 16, 2000)));
        _designProps.Children.Add(size);
        _designProps.Children.Add(Note("Opening sound: System Settings > Sounds > Paperdoll open. Every click on a button, box, slot or list: System Settings > Sounds > Drop in a window. (None set there = silent.)"));
        _designProps.Children.Add(SoundPicker("Close sound", () => design.CloseSoundResourceId, v => design.CloseSoundResourceId = v));
        var users = _objects.Objects.Where(o => o.DollDesignId == design.Id).Select(o => o.Name).ToList();
        _designProps.Children.Add(Note(users.Count == 0
            ? "No object opens it yet: Object Editor > an object > Opens paperdoll."
            : $"Opened by: {string.Join(", ", users)}. In play: double-click it."));
        _designProps.Children.Add(Note("320 x 240 is the size of a container's window in the game."));
    }

    // ================= Parts =================

    private void AddPart(DollPartKind kind)
    {
        if (Design is null) NewDesign();
        var design = Design!;
        var part = new DollPart
        {
            Id = design.NextPartId(), Kind = kind, Name = $"{DollPart.KindName(kind)} {design.Parts.Count(p => p.Kind == kind) + 1}",
            X = Math.Max(0, design.Width / 2 - 16), Y = Math.Max(0, design.Height / 2 - 16)
        };
        foreach (var look in DollPart.DefaultLooks(kind)) part.Looks.Add(new DollPartLook { Name = look });
        switch (kind)
        {
            case DollPartKind.Picture when design.Parts.Count == 0:
                part.Name = "Body";
                part.X = 0;
                part.Y = 0;
                break;
            case DollPartKind.ItemArea:
                part.Width = 96;
                part.Height = 64;
                part.Capacity = 20;
                break;
            case DollPartKind.Text:
                part.Width = 120;
                part.Height = 24;
                part.Text = "Text";
                break;
            case DollPartKind.Slider:
                part.Width = 64;
                part.Height = 12;
                break;
            case DollPartKind.Lid:
                // A new lid covers whatever its area overlaps that's already there (adjust in its list).
                break;
            case DollPartKind.SaveList:
                part.Width = 160;
                part.Height = 100;
                break;
            case DollPartKind.SaveName:
                part.Width = 160;
                part.Height = 14;
                part.SaveSlot = NextSaveSlot(design);
                part.Name = $"Save slot {part.SaveSlot}";
                break;
            case DollPartKind.SaveScreenshot:
                part.Width = 96;
                part.Height = 60;
                break;
            case DollPartKind.SaveDetails:
                part.Width = 160;
                part.Height = 36;
                break;
            case DollPartKind.SaveButton or DollPartKind.LoadButton or DollPartKind.DeleteSaveButton:
                part.Width = 40;
                part.Height = 14;
                part.Text = kind switch { DollPartKind.SaveButton => "Save", DollPartKind.LoadButton => "Load", _ => "Delete" };
                break;
            case DollPartKind.Button or DollPartKind.CloseButton or DollPartKind.ConfirmButton or DollPartKind.CancelButton:
                part.Width = 48;
                part.Height = 16;
                part.Text = kind switch { DollPartKind.ConfirmButton => "Yes", DollPartKind.CancelButton => "No", DollPartKind.CloseButton => "Close", _ => "" };
                break;
            case DollPartKind.TextBox or DollPartKind.DropDown:
                part.Width = 100;
                part.Height = 14;
                part.Text = "";
                if (kind == DollPartKind.DropDown) { part.Options.AddRange(new[] { "First", "Second", "Third" }); part.StartValue = 1; }
                break;
            case DollPartKind.ListBox:
                part.Width = 100;
                part.Height = 48;
                part.Options.AddRange(new[] { "First", "Second", "Third" });
                part.StartValue = 1;
                break;
            case DollPartKind.CheckBox or DollPartKind.RadioButton:
                part.Text = kind == DollPartKind.CheckBox ? "Check box" : "Choice";
                part.RadioGroup = kind == DollPartKind.RadioButton ? "Group 1" : "";
                if (kind == DollPartKind.RadioButton && !design.Parts.Any(p => p.Kind == DollPartKind.RadioButton && p.RadioGroup == "Group 1")) part.StartValue = 1; // the first of a group starts picked
                break;
            case DollPartKind.ProgressBar:
                part.Width = 80;
                part.Height = 8;
                part.Min = 0;
                part.Max = 100;
                part.StartValue = 50;
                break;
        }
        design.Parts.Add(part);
        _selectedPartId = part.Id;
        RefreshAll();
        _status.Text = KindHelp(kind);
    }

    /// <summary>The first slot number none of the paperdoll's Save slots holds yet.</summary>
    private static int NextSaveSlot(DollDesign design)
    {
        var taken = design.Parts.Where(p => p.Kind == DollPartKind.SaveName).Select(p => DollPart.SlotNumber(design, p)).ToHashSet();
        int slot = 1;
        while (taken.Contains(slot)) slot++;
        return slot;
    }

    private void DuplicatePart()
    {
        if (Design is not { } design || Part is not { } part) return;
        var copy = part.Clone(design.NextPartId());
        copy.Name = part.Name + " copy";
        copy.X += 8;
        copy.Y += 8;
        if (copy.Kind == DollPartKind.SaveName) copy.SaveSlot = NextSaveSlot(design); // a copied slot holds a save of its own
        design.Parts.Insert(design.Parts.IndexOf(part) + 1, copy);
        _selectedPartId = copy.Id;
        RefreshAll();
    }

    private void DeletePart()
    {
        if (Design is not { } design || Part is not { } part) return;
        int index = design.Parts.IndexOf(part);
        design.Parts.Remove(part);
        foreach (var other in design.Parts)
        {
            other.Covers.Remove(part.Id);
            if (other.ShowsPartId == part.Id) other.ShowsPartId = -1;
        }
        _selectedPartId = design.Parts.Count == 0 ? -1 : design.Parts[Math.Clamp(index - 1, 0, design.Parts.Count - 1)].Id;
        RefreshAll();
    }

    private void MovePart(int by)
    {
        if (Design is not { } design || Part is not { } part) return;
        int index = design.Parts.IndexOf(part), to = index + by;
        if (to < 0 || to >= design.Parts.Count) return;
        design.Parts.RemoveAt(index);
        design.Parts.Insert(to, part);
        RefreshAll();
    }

    private void OpenPartEvents()
    {
        if (Design is not { } design || Part is not { } part) { _status.Text = "Pick a part first."; return; }
        if (_variables is null || _stats is null) { _status.Text = "Open the PaperDoll Editor from the Tools menu to program its parts."; return; }
        new DollPartEventWindow(design, part, _objects, _sprites, _resources, _books, _containers, _variables, _stats) { Owner = this }.ShowDialog();
        RefreshPartList();
    }

    // ================= Showing =================

    private void RefreshAll()
    {
        ShowDesignProps();
        RefreshPartList();
        ShowPartProps();
        Redraw();
    }

    private void RefreshPartList()
    {
        _refreshing = true;
        var design = Design;
        _partList.ItemsSource = design?.Parts.Select(p =>
        {
            int events = p.EventPages.Sum(page => page.Commands.Count);
            return $"{p.Name}   ({DollPart.KindName(p.Kind)}){(events > 0 ? "  - programmed" : "")}";
        }).ToList();
        _partList.SelectedIndex = design is null ? -1 : design.Parts.FindIndex(p => p.Id == _selectedPartId);
        _refreshing = false;
    }

    private void ShowPartProps()
    {
        _partProps.Children.Clear();
        if (Design is not { } design) return;
        if (Part is not { } part) { _partProps.Children.Add(Note("No part picked: add one from the toolbox above the canvas.")); return; }
        if (part.Kind == DollPartKind.SaveName && part.Looks.Count < 2) // made as a "Save name" (b336): it gains the Picked slot look
            part.Looks.Add(new DollPartLook { Name = "Picked slot" });
        var panel = new StackPanel();
        panel.Children.Add(Note(KindHelp(part.Kind)));
        panel.Children.Add(Field("Name (shown when it's clicked in play)", part.Name, v => { part.Name = v; RefreshPartList(); Redraw(); }, 250));
        var place = new WrapPanel();
        place.Children.Add(IntField("X", () => part.X, v => part.X = v));
        place.Children.Add(IntField("Y", () => part.Y, v => part.Y = v));
        if (DollPart.IsAreaKind(part.Kind) || DollPart.IsButtonKind(part.Kind) || part.Kind is DollPartKind.Slider or DollPartKind.Switch or DollPartKind.Lid)
        {
            place.Children.Add(IntField(part.Kind == DollPartKind.Text ? "Wrap width" : "Width", () => part.Width, v => part.Width = Math.Max(4, v)));
            if (part.Kind != DollPartKind.Text) place.Children.Add(IntField("Height", () => part.Height, v => part.Height = Math.Max(4, v)));
        }
        panel.Children.Add(place);
        if (DollPart.IsButtonKind(part.Kind) || part.Kind is DollPartKind.Switch or DollPartKind.Lid or DollPartKind.Slider)
            panel.Children.Add(Note("Width / Height are used only while it has no sprite (a button without one is drawn plain, its caption on it)."));
        if (DollPart.IsSaveLoadKind(part.Kind))
            panel.Children.Add(Note("Works on the paperdoll picked as the Save/Load screen (System Settings > Save / Load)."));
        panel.Children.Add(Check("Starts visible (events can show / hide it)", () => part.StartsVisible, v => { part.StartsVisible = v; Redraw(); }));
        if (!DollPart.IsAreaKind(part.Kind) && part.Kind is not DollPartKind.Slider && !DollPart.IsSaveLoadKind(part.Kind))
            panel.Children.Add(Check("Can be dragged onto other parts (theirs: By Combined With A Part) - it goes back to its place when let go", () => part.Draggable, v => part.Draggable = v));

        if (part.Looks.Count > 0) panel.Children.Add(LooksEditor(part));

        switch (part.Kind)
        {
            case DollPartKind.Lid: panel.Children.Add(LidEditor(design, part)); break;
            case DollPartKind.Slot: panel.Children.Add(SlotEditor(part)); break;
            case DollPartKind.ItemArea: panel.Children.Add(AreaEditor(part)); break;
            case DollPartKind.Text: panel.Children.Add(TextEditor(part)); break;
            case DollPartKind.Slider: panel.Children.Add(RangeEditor(part, "Starts at")); break;
            case DollPartKind.Display: panel.Children.Add(DisplayEditor(design, part)); break;
            case DollPartKind.SaveName:
                panel.Children.Add(IntField("Slot number (each slot holds its own save)", () => DollPart.SlotNumber(design, part), v => part.SaveSlot = Math.Clamp(v, 1, 999)));
                if (design.Parts.Any(p => p != part && p.Kind == DollPartKind.SaveName && DollPart.SlotNumber(design, p) == DollPart.SlotNumber(design, part)))
                    panel.Children.Add(Note("Another Save slot has the same number - both would show the same save."));
                panel.Children.Add(new GroupBox { Header = "Font", Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 0), Content = FontRow(part) });
                break;
            case DollPartKind.SaveList or DollPartKind.SaveDetails:
                panel.Children.Add(new GroupBox { Header = "Font", Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 0), Content = FontRow(part) });
                break;
            case var kind when DollPart.IsControlKind(kind):
                panel.Children.Add(ControlEditor(design, part));
                break;
        }

        int programmed = part.EventPages.Sum(p => p.Commands.Count);
        var events = MakeButton(programmed > 0 ? $"Events... ({programmed} command{(programmed == 1 ? "" : "s")})" : "Events...", OpenPartEvents);
        events.Margin = new Thickness(0, 8, 0, 0);
        events.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(events);
        _partProps.Children.Add(new GroupBox { Header = $"{part.Name} - {DollPart.KindName(part.Kind)}", Padding = new Thickness(6), Content = panel });
    }

    private UIElement LooksEditor(DollPart part)
    {
        var body = new StackPanel();
        bool chooseStart = part.Kind is DollPartKind.Switch or DollPartKind.Lid or DollPartKind.Light;
        for (int i = 0; i < part.Looks.Count; i++)
        {
            var look = part.Looks[i];
            int index = i;
            var row = new WrapPanel { Margin = new Thickness(0, 2, 0, 2) };
            if (DollPart.LooksCanGrow(part.Kind))
                row.Children.Add(Field("Look name", look.Name, v => { look.Name = v; ShowPartProps(); }, 90));
            row.Children.Add(SpritePicker(DollPart.LooksCanGrow(part.Kind) ? "Sprite" : look.Name, look.Sprite, link => { look.Sprite = link; Redraw(); }));
            if (chooseStart)
            {
                var start = new RadioButton { Content = "starts so", GroupName = $"start{part.Id}", IsChecked = part.StartLook == index, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 0, 6) };
                start.Checked += (_, _) => { part.StartLook = index; Redraw(); };
                row.Children.Add(start);
            }
            if (DollPart.LooksCanGrow(part.Kind) && part.Looks.Count > 2)
                row.Children.Add(MakeButton("x", () => { part.Looks.RemoveAt(index); part.StartLook = Math.Min(part.StartLook, part.Looks.Count - 1); ShowPartProps(); Redraw(); }));
            body.Children.Add(row);
        }
        if (DollPart.LooksCanGrow(part.Kind))
            body.Children.Add(MakeButton("Add a look", () => { part.Looks.Add(new DollPartLook { Name = $"Position {part.Looks.Count + 1}" }); ShowPartProps(); }));
        string note = part.Kind switch
        {
            DollPartKind.Picture => "Animated sprites play their frames and frame sounds, as the Sprite Editor made them.",
            DollPartKind.Display => "Frame 1 shows the lowest value, frame 2 the next...",
            DollPartKind.Slot or DollPartKind.ItemArea => "Optional: a picture drawn behind what's inside (the tray's own art).",
            DollPartKind.Switch => "Each click moves it to the next look (after the last: back to the first).",
            _ => ""
        };
        if (note.Length > 0) body.Children.Add(Note(note));
        return new GroupBox { Header = "Looks (sprites from the Sprite Editor)", Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 0), Content = body };
    }

    private UIElement LidEditor(DollDesign design, DollPart lid)
    {
        var body = new StackPanel();
        body.Children.Add(new TextBlock { Text = "It covers:", FontSize = 11 });
        foreach (var other in design.Parts.Where(p => p.Id != lid.Id))
        {
            var o = other;
            body.Children.Add(Check($"{o.Name} ({DollPart.KindName(o.Kind)})", () => lid.Covers.Contains(o.Id), v =>
            {
                if (v && !lid.Covers.Contains(o.Id)) lid.Covers.Add(o.Id);
                if (!v) lid.Covers.Remove(o.Id);
                Redraw();
            }));
        }
        body.Children.Add(Check("Closed, it hides what it covers (off: seen through, but out of reach - a glass door)", () => lid.HidesWhatItCovers, v => { lid.HidesWhatItCovers = v; Redraw(); }));
        return new GroupBox { Header = "Lid", Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 0), Content = body };
    }

    private UIElement SlotEditor(DollPart slot)
    {
        var body = new StackPanel();
        body.Children.Add(IntField("Holds how many", () => slot.Capacity, v => slot.Capacity = Math.Clamp(v, 1, 99)));
        body.Children.Add(new TextBlock { Text = "It takes (none listed = anything, in its own look):", FontSize = 11, Margin = new Thickness(0, 6, 0, 2) });
        foreach (var fit in slot.Accepts.ToList())
        {
            var f = fit;
            var box = new StackPanel { Margin = new Thickness(0, 2, 0, 6) };
            var head = new WrapPanel();
            head.Children.Add(new TextBlock { Text = DollLook.ItemName(f.Item, _objects, _books, _containers), FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            head.Children.Add(MakeButton("Remove", () => { slot.Accepts.Remove(f); ShowPartProps(); Redraw(); }));
            box.Children.Add(head);
            box.Children.Add(SpritePicker("Fitted look inside (none = its own look)", f.FittedSprite, link => { f.FittedSprite = link; Redraw(); }));
            var offset = new WrapPanel();
            offset.Children.Add(IntField("Offset X", () => f.OffsetX, v => f.OffsetX = v));
            offset.Children.Add(IntField("Offset Y", () => f.OffsetY, v => f.OffsetY = v));
            box.Children.Add(offset);
            body.Children.Add(new Border { BorderBrush = Brushes.Gray, BorderThickness = new Thickness(0, 0, 0, 1), Child = box });
        }
        body.Children.Add(MakeButton("Add an item it takes...", () =>
        {
            if (PickItem() is not { } item) return;
            if (slot.Accepts.Any(a => a.Item.Category == item.Category && a.Item.ItemId == item.ItemId)) return;
            slot.Accepts.Add(new DollSlotFit { Item = item });
            ShowPartProps();
            Redraw();
        }));
        body.Children.Add(StartsWithEditor(slot));
        body.Children.Add(Note("In play: drag an item onto the slot to put it in (only what it takes, while it has room); drag it out to take it back - it turns back into its own look."));
        return new GroupBox { Header = "Slot", Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 0), Content = body };
    }

    private UIElement AreaEditor(DollPart area)
    {
        var body = new StackPanel();
        body.Children.Add(IntField("Holds how many", () => area.Capacity, v => area.Capacity = Math.Clamp(v, 1, 999)));
        body.Children.Add(StartsWithEditor(area));
        body.Children.Add(Note("In play: drop anything inside its area; drag it out again. Items keep where they were dropped."));
        return new GroupBox { Header = "Item area", Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 0), Content = body };
    }

    private UIElement StartsWithEditor(DollPart part)
    {
        var body = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        body.Children.Add(new TextBlock { Text = "Holds at the start (every placed copy gets its own):", FontSize = 11 });
        foreach (var start in part.StartsWith.ToList())
        {
            var s = start;
            var row = new WrapPanel();
            row.Children.Add(new TextBlock { Text = DollLook.ItemName(s, _objects, _books, _containers), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            row.Children.Add(MakeButton("Remove", () => { part.StartsWith.Remove(s); ShowPartProps(); Redraw(); }));
            body.Children.Add(row);
        }
        if (part.StartsWith.Count < part.Capacity)
            body.Children.Add(MakeButton("Add a starting item...", () =>
            {
                if (PickItem() is not { } item) return;
                if (part.Kind == DollPartKind.Slot && DollLook.FitFor(part, item.Category, item.ItemId) is null)
                {
                    MessageBox.Show(this, $"\"{part.Name}\" doesn't take {DollLook.ItemName(item, _objects, _books, _containers)} - add it to what it takes first.", "PaperDoll Editor");
                    return;
                }
                part.StartsWith.Add(item);
                ShowPartProps();
                Redraw();
            }));
        return body;
    }

    private UIElement TextEditor(DollPart part)
    {
        var body = new StackPanel();
        var text = new TextBox { Text = part.Text, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 70, Width = 330 };
        text.TextChanged += (_, _) => { part.Text = text.Text; Redraw(); };
        body.Children.Add(new TextBlock { Text = "Text", FontSize = 11 });
        body.Children.Add(text);
        body.Children.Add(FontRow(part));
        return new GroupBox { Header = "Text", Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 0), Content = body };
    }

    /// <summary>A form control's own settings: its caption / words and font, its choices, its variable, its group.</summary>
    private UIElement ControlEditor(DollDesign design, DollPart part)
    {
        var body = new StackPanel();
        if (part.Kind != DollPartKind.ProgressBar)
        {
            string label = part.Kind switch
            {
                DollPartKind.TextBox => "Starts with (typed text)",
                DollPartKind.CheckBox or DollPartKind.RadioButton => "Words beside it",
                DollPartKind.DropDown or DollPartKind.ListBox => "",
                _ => "Caption (words on it; empty = only its sprite)"
            };
            if (label.Length > 0)
            {
                var text = new TextBox { Text = part.Text, Width = 330 };
                text.TextChanged += (_, _) => { part.Text = text.Text; Redraw(); };
                body.Children.Add(Labeled(label, text));
            }
            body.Children.Add(FontRow(part));
        }
        switch (part.Kind)
        {
            case DollPartKind.DropDown or DollPartKind.ListBox:
            {
                var options = new TextBox { Text = string.Join("\n", part.Options), AcceptsReturn = true, Height = 80, Width = 330, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                options.TextChanged += (_, _) =>
                {
                    part.Options = options.Text.Replace("\r", "").Split('\n').Select(o => o.Trim()).Where(o => o.Length > 0).ToList();
                    Redraw();
                };
                body.Children.Add(Labeled("Choices (one per line)", options));
                body.Children.Add(IntField("Starts picked (1, 2, 3... 0 = none)", () => part.StartValue, v => part.StartValue = Math.Clamp(v, 0, part.Options.Count)));
                break;
            }
            case DollPartKind.TextBox:
                body.Children.Add(Check("Numbers only (its number goes into its variable)", () => part.NumbersOnly, v => part.NumbersOnly = v));
                body.Children.Add(IntField("Longest (letters)", () => part.MaxLength, v => part.MaxLength = Math.Clamp(v, 1, 200)));
                break;
            case DollPartKind.CheckBox:
                body.Children.Add(Check("Starts ticked", () => part.StartValue != 0, v => { part.StartValue = v ? 1 : 0; Redraw(); }));
                break;
            case DollPartKind.RadioButton:
                body.Children.Add(Field("Radio group (the same name = one choice)", part.RadioGroup, v => part.RadioGroup = v, 160));
                body.Children.Add(Check("Starts picked", () => part.StartValue != 0, v =>
                {
                    part.StartValue = v ? 1 : 0;
                    if (v) foreach (var other in design.Parts.Where(o => o != part && o.Kind == DollPartKind.RadioButton && o.RadioGroup == part.RadioGroup)) other.StartValue = 0;
                    Redraw();
                }));
                break;
            case DollPartKind.ProgressBar:
                body.Children.Add(DisplayEditor(design, part));
                body.Children.Add(Check("Upright (fills from the bottom up)", () => part.Vertical, v => { part.Vertical = v; Redraw(); }));
                break;
        }
        if (DollPart.HasVariable(part.Kind))
        {
            var variables = ProjectSession.VariablesProvider?.Invoke()?.Variables.Select(x => x.Name).ToList() ?? new List<string>();
            var variable = new ComboBox { ItemsSource = variables, IsEditable = true, Text = part.VariableName, Width = 200 };
            void Set() => part.VariableName = variable.Text.Trim();
            variable.LostFocus += (_, _) => Set();
            variable.SelectionChanged += (_, _) => { if (variable.SelectedItem is string picked) part.VariableName = picked; };
            body.Children.Add(Labeled("Kept in the variable (empty = none)", variable));
            body.Children.Add(Note(part.Kind switch
            {
                DollPartKind.CheckBox or DollPartKind.RadioButton => "1 = ticked / picked, 0 = not. Read when the paperdoll opens, written on every click - events read and set it as any variable.",
                DollPartKind.DropDown or DollPartKind.ListBox => "The picked choice's number (1 = the first, 0 = none). Read when the paperdoll opens, written on every pick.",
                DollPartKind.TextBox => "The typed number (Numbers only), written when Enter ends the typing. Read when the paperdoll opens.",
                _ => "Its value. Read when the paperdoll opens, written when it changes."
            }));
        }
        body.Children.Add(Note(DollPart.IsButtonKind(part.Kind) || part.Kind == DollPartKind.ProgressBar
            ? "Without sprites it's drawn plain, so a paperdoll works before its art is made. Its events: Events... (By Use = pressed)."
            : "Without sprites it's drawn plain, so a paperdoll works before its art is made. Its events (Events..., By Use) run whenever the player changes it."));
        return new GroupBox { Header = DollPart.KindName(part.Kind), Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 0), Content = body };
    }

    /// <summary>A part's font, its size and tint.</summary>
    private UIElement FontRow(DollPart part)
    {
        var fonts = new List<string> { "(in-game font)" };
        fonts.AddRange(_sprites.Groups.FirstOrDefault(g => g.Name == SpriteDatabase.FontsGroupName)?.Sprites.Select(s => s.Name) ?? Enumerable.Empty<string>());
        var font = new ComboBox { Width = 180, ItemsSource = fonts, SelectedItem = string.IsNullOrEmpty(part.FontName) ? fonts[0] : fonts.Contains(part.FontName) ? part.FontName : fonts[0] };
        font.SelectionChanged += (_, _) => { part.FontName = font.SelectedIndex <= 0 ? "" : (string)font.SelectedItem; Redraw(); };
        var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        row.Children.Add(Labeled("Font (Sprite Editor > Fonts)", font));
        row.Children.Add(IntField("Size x", () => part.TextScale, v => part.TextScale = Math.Clamp(v, 1, 8)));
        row.Children.Add(Field("Tint (#RRGGBB, empty = none)", part.TextColor, v => { part.TextColor = v; Redraw(); }, 90));
        return row;
    }

    private UIElement RangeEditor(DollPart part, string startLabel)
    {
        var body = new WrapPanel();
        body.Children.Add(IntField("Min", () => part.Min, v => part.Min = v));
        body.Children.Add(IntField("Max", () => part.Max, v => part.Max = Math.Max(part.Min, v)));
        body.Children.Add(IntField(startLabel, () => part.StartValue, v => part.StartValue = Math.Clamp(v, part.Min, Math.Max(part.Min, part.Max))));
        var panel = new StackPanel();
        panel.Children.Add(body);
        if (part.Kind == DollPartKind.Slider)
            panel.Children.Add(Check("Upright (up = more)", () => part.Vertical, v => { part.Vertical = v; Redraw(); }));
        return new GroupBox { Header = "Value", Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 0), Content = panel };
    }

    private UIElement DisplayEditor(DollDesign design, DollPart display)
    {
        var body = new StackPanel();
        var none = new DollPart { Id = -1, Name = "(its own value - set by events)" };
        var sources = new List<DollPart> { none };
        sources.AddRange(design.Parts.Where(p => p.Kind == DollPartKind.Slider));
        var combo = new ComboBox { Width = 260, ItemsSource = sources, DisplayMemberPath = nameof(DollPart.Name), SelectedItem = sources.FirstOrDefault(s => s.Id == display.ShowsPartId) ?? none };
        combo.SelectionChanged += (_, _) => { display.ShowsPartId = (combo.SelectedItem as DollPart)?.Id ?? -1; ShowPartProps(); Redraw(); };
        body.Children.Add(Labeled("Shows the value of", combo));
        if (display.ShowsPartId < 0) body.Children.Add(RangeEditor(display, "Starts at"));
        return new GroupBox { Header = "Display", Padding = new Thickness(6), Margin = new Thickness(0, 6, 0, 0), Content = body };
    }

    // ================= Drawing =================

    private void Redraw()
    {
        _canvas.Children.Clear();
        double z = Zoom;
        if (Design is not { } design) { _canvas.Width = 400; _canvas.Height = 300; return; }
        _canvas.Width = (design.Width + PageMargin * 2) * z;
        _canvas.Height = (design.Height + PageMargin * 2) * z;
        var page = new Rectangle { Width = design.Width * z, Height = design.Height * z, Fill = new SolidColorBrush(Color.FromRgb(0x30, 0x30, 0x34)), Stroke = Brushes.Gray, StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 4, 3 } };
        Canvas.SetLeft(page, PageMargin * z);
        Canvas.SetTop(page, PageMargin * z);
        _canvas.Children.Add(page);

        (int Look, bool Visible) StartOf(DollPart p) => (p.StartLook, p.StartsVisible);
        foreach (var part in design.Parts)
        {
            bool faint = !part.StartsVisible || DollLook.ClosedLidOver(part, design, StartOf) is { HidesWhatItCovers: true };
            var visual = PartVisual(design, part, z);
            visual.Opacity = faint ? 0.3 : 1;
            visual.IsHitTestVisible = false;
            Canvas.SetLeft(visual, (PageMargin + part.X) * z);
            Canvas.SetTop(visual, (PageMargin + part.Y) * z);
            _canvas.Children.Add(visual);
        }
        foreach (var part in design.Parts)
        {
            var size = DollLook.SizeOf(part, part.StartLook, _sprites);
            bool picked = part.Id == _selectedPartId;
            if (picked)
            {
                var outline = new Rectangle { Width = size.Width * z + 2, Height = size.Height * z + 2, Stroke = Brushes.White, StrokeThickness = 1.5, StrokeDashArray = new DoubleCollection { 4, 2 }, IsHitTestVisible = false };
                Canvas.SetLeft(outline, (PageMargin + part.X) * z - 1);
                Canvas.SetTop(outline, (PageMargin + part.Y) * z - 1);
                _canvas.Children.Add(outline);
            }
            if (_showNames.IsChecked == true || picked)
            {
                var label = new TextBlock
                {
                    Text = part.Name, FontSize = 10, Foreground = picked ? Brushes.White : Brushes.LightSkyBlue, IsHitTestVisible = false,
                    Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), Padding = new Thickness(2, 0, 2, 0)
                };
                Canvas.SetLeft(label, (PageMargin + part.X) * z);
                Canvas.SetTop(label, (PageMargin + part.Y) * z - 13);
                _canvas.Children.Add(label);
            }
        }
    }

    /// <summary>One part as it starts, at this zoom.</summary>
    private FrameworkElement PartVisual(DollDesign design, DollPart part, double z)
    {
        if (DollPart.IsControlKind(part.Kind)) // drawn as in play (DollControls), still frames, at this zoom
        {
            int? bar = part.Kind == DollPartKind.ProgressBar && part.ShowsPartId >= 0 && design.Part(part.ShowsPartId) is { } source ? source.StartValue : null;
            var control = DollControls.Build(part, DollControls.StartOf(part), _sprites, sprite => DollLook.Frame(sprite) is { } frame ? Pixel(frame, frame.PixelWidth, frame.PixelHeight, 1) : new Border(), bar);
            control.LayoutTransform = new ScaleTransform(z, z);
            return control;
        }
        var size = DollLook.SizeOf(part, part.StartLook, _sprites);
        var holder = new Canvas { Width = size.Width * z, Height = size.Height * z };
        switch (part.Kind)
        {
            case DollPartKind.Hotspot:
                holder.Children.Add(Dashed(size, z, Brushes.Gold, "hotspot"));
                return holder;
            case DollPartKind.Text:
                if (DollLook.TextPicture(part, _sprites) is { } words)
                    holder.Children.Add(Pixel(words, words.PixelWidth, words.PixelHeight, z));
                else
                    holder.Children.Add(new TextBlock { Text = part.Text, Foreground = Brushes.White, FontSize = 11 * z, TextWrapping = TextWrapping.Wrap, Width = size.Width * z });
                return holder;
            case DollPartKind.Slider:
            {
                var track = DollLook.Frame(DollLook.LookSprite(part, 0, _sprites));
                if (track is not null) holder.Children.Add(Pixel(track, track.PixelWidth, track.PixelHeight, z));
                else holder.Children.Add(Dashed(size, z, Brushes.SlateGray, "track"));
                var knobBitmap = DollLook.Frame(DollLook.LookSprite(part, 1, _sprites));
                var knobSize = knobBitmap is not null ? new Size(knobBitmap.PixelWidth, knobBitmap.PixelHeight) : new Size(Math.Min(8, size.Width), size.Height);
                FrameworkElement knob = knobBitmap is not null ? Pixel(knobBitmap, knobBitmap.PixelWidth, knobBitmap.PixelHeight, z)
                    : new Rectangle { Width = knobSize.Width * z, Height = knobSize.Height * z, Fill = Brushes.Silver };
                var at = DollLook.KnobAt(part, part.StartValue, size, knobSize);
                Canvas.SetLeft(knob, at.X * z);
                Canvas.SetTop(knob, at.Y * z);
                holder.Children.Add(knob);
                return holder;
            }
            case DollPartKind.Display:
            {
                int value = DollLook.DisplayValue(part, design, p => p.StartValue);
                var sprite = DollLook.LookSprite(part, 0, _sprites);
                int min = part.ShowsPartId >= 0 && design.Part(part.ShowsPartId) is { } source ? source.Min : part.Min;
                if (DollLook.Frame(sprite, value - min) is { } frame) holder.Children.Add(Pixel(frame, frame.PixelWidth, frame.PixelHeight, z));
                else holder.Children.Add(Dashed(size, z, Brushes.SlateGray, $"= {value}"));
                return holder;
            }
            case DollPartKind.Slot:
            case DollPartKind.ItemArea:
            {
                if (DollLook.Frame(DollLook.LookSprite(part, 0, _sprites)) is { } behind) holder.Children.Add(Pixel(behind, behind.PixelWidth, behind.PixelHeight, z));
                holder.Children.Add(Dashed(size, z, part.Kind == DollPartKind.Slot ? Brushes.LimeGreen : Brushes.MediumPurple, null));
                double x = 0;
                foreach (var start in part.StartsWith)
                {
                    var fit = DollLook.FitFor(part, start.Category, start.ItemId);
                    var sprite = fit is { FittedSprite.IsSet: true } ? DollLook.SpriteOf(fit.FittedSprite, _sprites)
                        : DollLook.ItemSprite(start.Category, start.ItemId, _objects, _books, _containers, _sprites);
                    if (DollLook.Frame(sprite) is not { } bitmap) continue;
                    var image = Pixel(bitmap, bitmap.PixelWidth, bitmap.PixelHeight, z);
                    if (part.Kind == DollPartKind.Slot && fit is not null)
                    {
                        Canvas.SetLeft(image, fit.FittedSprite.IsSet ? fit.OffsetX * z : (size.Width - bitmap.PixelWidth) / 2 * z);
                        Canvas.SetTop(image, fit.FittedSprite.IsSet ? fit.OffsetY * z : (size.Height - bitmap.PixelHeight) / 2 * z);
                    }
                    else
                    {
                        Canvas.SetLeft(image, x * z);
                        x += bitmap.PixelWidth + 2;
                    }
                    holder.Children.Add(image);
                }
                return holder;
            }
            default:
            {
                if (DollLook.Frame(DollLook.LookSprite(part, part.StartLook, _sprites)) is { } bitmap) holder.Children.Add(Pixel(bitmap, bitmap.PixelWidth, bitmap.PixelHeight, z));
                else holder.Children.Add(Dashed(size, z, Brushes.Tan, DollPart.KindName(part.Kind).ToLowerInvariant()));
                return holder;
            }
        }
    }

    private static Image Pixel(ImageSource source, double width, double height, double z)
    {
        var image = new Image { Source = source, Width = width * z, Height = height * z, Stretch = Stretch.Fill };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }

    private static FrameworkElement Dashed(Size size, double z, Brush brush, string? text) => new Border
    {
        Width = size.Width * z, Height = size.Height * z, BorderBrush = brush, BorderThickness = new Thickness(1),
        Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
        Child = text is null ? null : new TextBlock { Text = text, Foreground = brush, FontSize = 9, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
    };

    // ================= Mouse and keys =================

    private DollPart? PartAt(Point p)
    {
        if (Design is not { } design) return null;
        double z = Zoom;
        for (int i = design.Parts.Count - 1; i >= 0; i--)
        {
            var part = design.Parts[i];
            var size = DollLook.SizeOf(part, part.StartLook, _sprites);
            if (new Rect((PageMargin + part.X) * z, (PageMargin + part.Y) * z, size.Width * z, size.Height * z).Contains(p)) return part;
        }
        return null;
    }

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _canvas.Focus();
        var p = e.GetPosition(_canvas);
        // The picked part keeps the click while it's under the mouse, so a small part on top of it can't steal a drag.
        var hit = Part is { } picked && PartContains(picked, p) ? picked : PartAt(p);
        if (hit is null) return;
        _selectedPartId = hit.Id;
        if (e.ClickCount == 2) { RefreshPartList(); ShowPartProps(); Redraw(); OpenPartEvents(); return; }
        _drag = (p, hit.X, hit.Y);
        _canvas.CaptureMouse();
        RefreshPartList();
        ShowPartProps();
        Redraw();
    }

    private bool PartContains(DollPart part, Point p)
    {
        double z = Zoom;
        var size = DollLook.SizeOf(part, part.StartLook, _sprites);
        return new Rect((PageMargin + part.X) * z, (PageMargin + part.Y) * z, size.Width * z, size.Height * z).Contains(p);
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_drag is not { } drag || Part is not { } part || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(_canvas);
        part.X = drag.X + (int)Math.Round((p.X - drag.Start.X) / Zoom);
        part.Y = drag.Y + (int)Math.Round((p.Y - drag.Start.Y) / Zoom);
        Redraw();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        if (Part is not { } part) return;
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control), shift = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        int step = shift ? 8 : 1;
        switch (e.Key)
        {
            case Key.Left: part.X -= step; break;
            case Key.Right: part.X += step; break;
            case Key.Up: part.Y -= step; break;
            case Key.Down: part.Y += step; break;
            case Key.PageUp: MovePart(1); e.Handled = true; return;
            case Key.PageDown: MovePart(-1); e.Handled = true; return;
            case Key.Delete: DeletePart(); e.Handled = true; return;
            case Key.D when ctrl: DuplicatePart(); e.Handled = true; return;
            default: return;
        }
        e.Handled = true;
        ShowPartProps();
        Redraw();
    }

    // ================= Building blocks =================

    private ItemReference? PickItem()
    {
        var picker = new SelectItemPickerWindow(_books, _objects, _containers, _sprites, null) { Owner = this };
        return picker.ShowDialog() == true && picker.Result is { ItemId: >= 0 } result
            ? new ItemReference { Category = result.Category, ItemId = result.ItemId, DisplayName = result.DisplayName }
            : null;
    }

    private sealed record SpriteChoice(string Group, string Name, string Display);

    private List<SpriteChoice> SpriteChoices()
    {
        var choices = new List<SpriteChoice> { new("", "", "(none)") };
        foreach (var group in _sprites.Groups.Where(g => g.Name != SpriteDatabase.TilesetGroupName && g.Name != SpriteDatabase.FontsGroupName))
            choices.AddRange(group.Sprites.Select(s => new SpriteChoice(group.Name, s.Name, $"{s.Name}   ({group.Name})")));
        return choices;
    }

    private FrameworkElement SpritePicker(string label, SpriteLink current, Action<SpriteLink> set)
    {
        var choices = SpriteChoices();
        var combo = new ComboBox { Width = 250, ItemsSource = choices, DisplayMemberPath = nameof(SpriteChoice.Display) };
        combo.SelectedItem = choices.FirstOrDefault(c => c.Name == current.SpriteName && (c.Group == current.SpriteGroup || string.IsNullOrEmpty(current.SpriteGroup))) ?? choices[0];
        combo.SelectionChanged += (_, _) =>
        {
            var c = combo.SelectedItem as SpriteChoice;
            set(new SpriteLink { SpriteGroup = c?.Group ?? "", SpriteName = c?.Name ?? "" });
        };
        return Labeled(label, combo);
    }

    private FrameworkElement SoundPicker(string label, Func<int> get, Action<int> set)
    {
        var none = new ResourceEntry { Id = -1, Name = "(None)" };
        var sounds = new List<ResourceEntry> { none };
        sounds.AddRange(_resources.Entries.Where(r => r.Kind == ResourceKind.Sound));
        var combo = new ComboBox { Width = 200, ItemsSource = sounds, DisplayMemberPath = "Name", SelectedItem = sounds.FirstOrDefault(s => s.Id == get()) ?? none };
        combo.SelectionChanged += (_, _) => set((combo.SelectedItem as ResourceEntry)?.Id ?? -1);
        return Labeled(label, combo);
    }

    private FrameworkElement IntField(string label, Func<int> get, Action<int> set)
    {
        var box = new TextBox { Width = 56, Text = get().ToString() };
        void Commit()
        {
            if (int.TryParse(box.Text.Trim(), out int v)) { set(v); Redraw(); }
            box.Text = get().ToString();
        }
        box.LostFocus += (_, _) => Commit();
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        return Labeled(label, box);
    }

    private static FrameworkElement Field(string label, string value, Action<string> set, double width)
    {
        var box = new TextBox { Width = width, Text = value };
        box.LostFocus += (_, _) => set(box.Text.Trim());
        box.KeyDown += (_, e) => { if (e.Key == Key.Enter) set(box.Text.Trim()); };
        return Labeled(label, box);
    }

    private static CheckBox Check(string label, Func<bool> get, Action<bool> set)
    {
        var check = new CheckBox { Content = new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap, MaxWidth = 320 }, IsChecked = get(), Margin = new Thickness(0, 3, 0, 3) };
        check.Checked += (_, _) => set(true);
        check.Unchecked += (_, _) => set(false);
        return check;
    }

    private static FrameworkElement Labeled(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 10, 4) };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11 });
        control.HorizontalAlignment = HorizontalAlignment.Left;
        panel.Children.Add(control);
        return panel;
    }

    private static TextBlock Note(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11, Opacity = 0.7, Margin = new Thickness(0, 2, 0, 4) };

    private static Button MakeButton(string text, Action click)
    {
        var button = new Button { Content = text, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(0, 2, 4, 2) };
        button.Click += (_, _) => click();
        return button;
    }
}
