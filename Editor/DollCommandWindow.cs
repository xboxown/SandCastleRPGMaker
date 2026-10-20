using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// The Paperdoll command: what an event does to a paperdoll (PaperDoll
/// Editor) - its parts' looks, visibility, values, lids, what's in its
/// slots, its sounds - or to the object itself (turn it into another
/// object). It acts on "this" paperdoll: the one whose part's event is
/// running, or the paperdoll of the object whose own event is running.
/// </summary>
public class DollCommandWindow : FactionCommandWindowBase
{
    /// <summary>The paperdoll being programmed (set by the part event window while it's open), so its parts can be listed.</summary>
    public static DollDesign? CurrentDesign { get; set; }

    /// <summary>Every action, its key, its label, and what it needs.</summary>
    private sealed record ActionChoice(string Key, string Label, bool NeedsPart, Func<DollPart, bool>? PartFilter = null)
    {
        public override string ToString() => Label;
    }

    private static readonly ActionChoice[] Actions =
    {
        new("SetLook", "Change a part's look", true, p => p.Looks.Count > 1),
        new("NextLook", "Move a part to its next look", true, p => p.Looks.Count > 1),
        new("Show", "Show a part", true),
        new("Hide", "Hide a part", true),
        new("SetValue", "Set a part's value (slider, display)", true, p => p.Kind is DollPartKind.Slider or DollPartKind.Display),
        new("AddValue", "Add to a part's value (negative = take away)", true, p => p.Kind is DollPartKind.Slider or DollPartKind.Display),
        new("OpenLid", "Open a lid", true, p => p.Kind == DollPartKind.Lid),
        new("CloseLid", "Close a lid", true, p => p.Kind == DollPartKind.Lid),
        new("PutItem", "Put a new item into a slot or item area", true, p => p.Kind is DollPartKind.Slot or DollPartKind.ItemArea),
        new("UseUpItems", "Use up everything in a slot or item area", true, p => p.Kind is DollPartKind.Slot or DollPartKind.ItemArea),
        new("UseUpCombined", "Use up the item just combined with it", false),
        new("ToVariable", "Copy a part's state into a variable", true),
        new("PlaySound", "Play a sound that keeps playing after closing", false),
        new("StopSounds", "Stop this paperdoll's sounds", false),
        new("Close", "Close the paperdoll", false),
        new("TurnInto", "Turn the object into another object", false)
    };

    private static readonly (string Key, string Label)[] StateKinds =
    {
        ("Look", "its look number (1 = the first)"),
        ("Value", "its value"),
        ("Items", "how many items it holds"),
        ("Visible", "shown (1) or hidden (0)")
    };

    private readonly ComboBox _design, _action, _part, _look, _state, _variable, _sound, _object;
    private readonly TextBox _value;
    private readonly CheckBox _loop;
    private readonly Button _itemButton;
    private ItemReference? _item;
    private readonly Dictionary<string, FrameworkElement> _rows = new();
    private readonly TextBlock _help = new() { TextWrapping = TextWrapping.Wrap, Opacity = 0.75, FontSize = 11, Margin = new Thickness(0, 8, 0, 0) };

    private static ObjectDatabase Objects => ProjectSession.ObjectsProvider?.Invoke() ?? new ObjectDatabase();

    public DollCommandWindow(EventCommand? existing = null) : base("Paperdoll")
    {
        Width = 460;
        var designs = Objects.DollDesigns.ToList();
        int existingDesign = int.TryParse(existing?.Get("DesignId"), out int d) ? d : CurrentDesign?.Id ?? -1;

        AddLabel("Paperdoll (to pick its parts from)");
        _design = new ComboBox { ItemsSource = designs, DisplayMemberPath = nameof(DollDesign.Name) };
        _design.SelectedItem = designs.FirstOrDefault(x => x.Id == existingDesign) ?? designs.FirstOrDefault(x => x.Id == CurrentDesign?.Id) ?? designs.FirstOrDefault();
        Rows.Children.Add(_design);

        AddLabel("Do");
        _action = new ComboBox { ItemsSource = Actions };
        _action.SelectedItem = Actions.FirstOrDefault(a => a.Key == existing?.Get("Action")) ?? Actions[0];
        Rows.Children.Add(_action);

        _part = new ComboBox { DisplayMemberPath = nameof(DollPart.Name) };
        AddRow("Part", "Part", _part);
        _look = new ComboBox();
        AddRow("Look", "Look", _look);
        _value = new TextBox { Width = 80, HorizontalAlignment = HorizontalAlignment.Left, Text = existing?.Get("Value") is { Length: > 0 } v ? v : "1" };
        AddRow("Value", "Value", _value);
        _itemButton = new Button { HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(8, 2, 8, 2) };
        _itemButton.Click += (_, _) => PickItem();
        AddRow("Item", "Item", _itemButton);
        _state = new ComboBox { ItemsSource = StateKinds.Select(s => s.Label).ToList() };
        _state.SelectedIndex = Math.Max(0, Array.FindIndex(StateKinds, s => s.Key == existing?.Get("State")));
        AddRow("State", "Copy", _state);
        var variables = ProjectSession.VariablesProvider?.Invoke()?.Variables.Select(x => x.Name).ToList() ?? new List<string>();
        _variable = new ComboBox { ItemsSource = variables, IsEditable = true, Text = existing?.Get("VariableName") ?? "" };
        AddRow("Variable", "Into the variable", _variable);
        var sounds = new List<ResourceEntry> { new() { Id = -1, Name = "(None)" } };
        sounds.AddRange(ProjectSession.ResourcesProvider?.Invoke()?.Entries.Where(r => r.Kind == ResourceKind.Sound) ?? Enumerable.Empty<ResourceEntry>());
        _sound = new ComboBox { ItemsSource = sounds, DisplayMemberPath = "Name" };
        _sound.SelectedItem = sounds.FirstOrDefault(s => s.Id.ToString() == existing?.Get("SoundResourceId")) ?? sounds[0];
        _loop = new CheckBox { Content = "Loop until stopped (a tape playing)", IsChecked = existing?.Get("Loop") == "True", Margin = new Thickness(0, 4, 0, 0) };
        var soundPanel = new StackPanel();
        soundPanel.Children.Add(_sound);
        soundPanel.Children.Add(_loop);
        AddRow("Sound", "Sound", soundPanel);
        var objects = Objects.Objects.ToList();
        _object = new ComboBox { ItemsSource = objects, DisplayMemberPath = nameof(WorldObject.Name) };
        _object.SelectedItem = objects.FirstOrDefault(o => o.Id.ToString() == existing?.Get("ObjectId")) ?? objects.FirstOrDefault();
        AddRow("Object", "Becomes", _object);
        Rows.Children.Add(_help);

        if (existing is not null && int.TryParse(existing.Get("ItemId"), out int itemId) && Enum.TryParse<ReferencedItemCategory>(existing.Get("ItemCategory"), out var category))
            _item = new ItemReference { Category = category, ItemId = itemId };

        _design.SelectionChanged += (_, _) => FillParts(null);
        _action.SelectionChanged += (_, _) => { FillParts((_part.SelectedItem as DollPart)?.Id); Arrange(); };
        _part.SelectionChanged += (_, _) => FillLooks(null);
        FillParts(int.TryParse(existing?.Get("PartId"), out int partId) ? partId : null);
        FillLooks(int.TryParse(existing?.Get("Look"), out int look) ? look : null);
        Arrange();
    }

    private void AddRow(string key, string label, FrameworkElement control)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 6, 0, 2) });
        panel.Children.Add(control);
        _rows[key] = panel;
        Rows.Children.Add(panel);
    }

    private ActionChoice Action => _action.SelectedItem as ActionChoice ?? Actions[0];

    private void FillParts(int? keepId)
    {
        var parts = (_design.SelectedItem as DollDesign)?.Parts.Where(p => Action.PartFilter?.Invoke(p) ?? true).ToList() ?? new List<DollPart>();
        _part.ItemsSource = parts;
        _part.SelectedItem = parts.FirstOrDefault(p => p.Id == keepId) ?? parts.FirstOrDefault();
    }

    private void FillLooks(int? keep)
    {
        var looks = (_part.SelectedItem as DollPart)?.Looks.Select((l, i) => $"{i + 1}. {l.Name}").ToList() ?? new List<string>();
        _look.ItemsSource = looks;
        _look.SelectedIndex = looks.Count == 0 ? -1 : Math.Clamp(keep ?? _look.SelectedIndex, 0, looks.Count - 1);
    }

    private void Arrange()
    {
        string key = Action.Key;
        void Show(string row, bool on) => _rows[row].Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        Show("Part", Action.NeedsPart);
        Show("Look", key == "SetLook");
        Show("Value", key is "SetValue" or "AddValue");
        Show("Item", key == "PutItem");
        Show("State", key == "ToVariable");
        Show("Variable", key == "ToVariable");
        Show("Sound", key == "PlaySound");
        Show("Object", key == "TurnInto");
        _itemButton.Content = _item is null ? "Pick an item..." : DollLook.ItemName(_item, Objects, ProjectSession.BooksProvider?.Invoke() ?? new BookDatabase(), ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase());
        _help.Text = key switch
        {
            "UseUpCombined" => "For By Combined With An Item: the item dropped on the part is used up (otherwise it stays where it came from).",
            "ToVariable" => "Then test the variable with Conditional Branch - e.g. copy how many items each hole holds, and turn the machine into its finished form when all are filled.",
            "PlaySound" => "Keeps playing after the paperdoll is closed. Stop it with \"Stop this paperdoll's sounds\" (a Stop button).",
            "TurnInto" => "The object (this placed or carried copy) becomes the other object, keeping its place. Its paperdoll closes; the new object starts with its own. Works in any object's own events too, paperdoll or not (cloth cut into a bandage, seeds sown into soil becoming a seedling).",
            "PutItem" => "A new copy of the item appears inside (if there's room).",
            _ => "Acts on this paperdoll: the one whose part's event is running, or the paperdoll of the object whose own event is running."
        };
    }

    private void PickItem()
    {
        var picker = new SelectItemPickerWindow(ProjectSession.BooksProvider?.Invoke() ?? new BookDatabase(), Objects,
            ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase(), ProjectSession.Sprites, _item) { Owner = this };
        if (picker.ShowDialog() == true && picker.Result is { ItemId: >= 0 } result) _item = new ItemReference { Category = result.Category, ItemId = result.ItemId, DisplayName = result.DisplayName };
        Arrange();
    }

    protected override bool TryBuild()
    {
        string key = Action.Key;
        var command = new EventCommand { Type = EventCommandType.Paperdoll };
        command.Parameters["Action"] = key;
        if (_design.SelectedItem is DollDesign design) command.Parameters["DesignId"] = design.Id.ToString();
        if (Action.NeedsPart)
        {
            if (_part.SelectedItem is not DollPart part) { MessageBox.Show(this, "Pick a part (the paperdoll has none that fits this).", Title); return false; }
            command.Parameters["PartId"] = part.Id.ToString();
            command.Parameters["PartName"] = part.Name;
        }
        switch (key)
        {
            case "SetLook":
                command.Parameters["Look"] = Math.Max(0, _look.SelectedIndex).ToString();
                break;
            case "SetValue" or "AddValue":
                if (!int.TryParse(_value.Text.Trim(), out int value)) { MessageBox.Show(this, "The value must be a whole number.", Title); return false; }
                command.Parameters["Value"] = value.ToString();
                break;
            case "PutItem":
                if (_item is null) { MessageBox.Show(this, "Pick the item to put in.", Title); return false; }
                command.Parameters["ItemCategory"] = _item.Category.ToString();
                command.Parameters["ItemId"] = _item.ItemId.ToString();
                break;
            case "ToVariable":
                if (string.IsNullOrWhiteSpace(_variable.Text)) { MessageBox.Show(this, "Pick the variable to copy into.", Title); return false; }
                command.Parameters["State"] = StateKinds[Math.Max(0, _state.SelectedIndex)].Key;
                command.Parameters["VariableName"] = _variable.Text.Trim();
                break;
            case "PlaySound":
                if (_sound.SelectedItem is not ResourceEntry { Id: >= 0 } sound) { MessageBox.Show(this, "Pick a sound.", Title); return false; }
                command.Parameters["SoundResourceId"] = sound.Id.ToString();
                command.Parameters["SoundName"] = sound.Name;
                command.Parameters["Loop"] = (_loop.IsChecked == true).ToString();
                break;
            case "TurnInto":
                if (_object.SelectedItem is not WorldObject obj) { MessageBox.Show(this, "Pick the object it becomes.", Title); return false; }
                command.Parameters["ObjectId"] = obj.Id.ToString();
                command.Parameters["ObjectName"] = obj.Name;
                break;
        }
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command)
    {
        string part = $"\"{command.Get("PartName")}\"";
        return command.Get("Action") switch
        {
            "SetLook" => $"{part} look {(int.TryParse(command.Get("Look"), out int l) ? l + 1 : 1)}",
            "NextLook" => $"{part} next look",
            "Show" => $"show {part}",
            "Hide" => $"hide {part}",
            "SetValue" => $"{part} = {command.Get("Value")}",
            "AddValue" => $"{part} + {command.Get("Value")}",
            "OpenLid" => $"open {part}",
            "CloseLid" => $"close {part}",
            "PutItem" => $"put item #{command.Get("ItemId")} into {part}",
            "UseUpItems" => $"use up what's in {part}",
            "UseUpCombined" => "use up the combined item",
            "ToVariable" => $"{command.Get("VariableName")} = {part} {command.Get("State").ToLowerInvariant()}",
            "PlaySound" => $"play \"{command.Get("SoundName")}\"{(command.Get("Loop") == "True" ? " (looping)" : "")}",
            "StopSounds" => "stop its sounds",
            "Close" => "close it",
            "TurnInto" => $"turn the object into \"{command.Get("ObjectName")}\"",
            _ => command.Get("Action")
        };
    }
}
