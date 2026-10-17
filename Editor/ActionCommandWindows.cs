using System.Windows;
using System.Windows.Controls;
using SandcastleRpgMaker.Engine;

namespace SandcastleRpgMaker.Editor;

/// <summary>
/// A thing placed on the map, picked in a command window: a cell picked on
/// the map picture, then which of the things on it (only the kinds the
/// command can use). Saved as Thing (its key), ThingName, X and Y.
/// </summary>
internal sealed class MapThingField
{
    private sealed record ThingChoice(string Key, string Name)
    {
        public override string ToString() => Name;
    }

    private readonly Window _owner;
    private readonly Func<string, bool> _accepts;
    private readonly ComboBox _things = new() { Width = 220, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock _cellText = new() { Width = 90, VerticalAlignment = VerticalAlignment.Center };
    private readonly string _what;
    private (int X, int Y)? _cell;

    public MapThingField(Window owner, StackPanel rows, EventCommand? existing, string what, Func<string, bool> accepts)
    {
        _owner = owner;
        _accepts = accepts;
        _what = what;
        if (int.TryParse(existing?.Get("X"), out int x) && int.TryParse(existing?.Get("Y"), out int y)) _cell = (x, y);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        var pick = new Button { Content = "Pick on Map...", Padding = new Thickness(8, 1, 8, 1) };
        pick.Click += (_, _) => Pick();
        row.Children.Add(_cellText);
        row.Children.Add(pick);
        row.Children.Add(_things);
        rows.Children.Add(row);
        Fill(existing?.Get("Thing"), existing?.Get("ThingName"));
    }

    private void Pick()
    {
        if (ProjectSession.MapSnapshotProvider?.Invoke() is not { } map)
        {
            MessageBox.Show(_owner, "Open or create a map in the Map Editor first.", _owner.Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var picker = new MapCellPickerWindow(map.Picture, map.TileWidth, map.TileHeight, map.Columns, map.Rows, _cell, null, _accepts) { Owner = _owner };
        if (picker.ShowDialog() != true || picker.Picked is not { } cell) return;
        _cell = cell;
        Fill(picker.PickedThingKey, null); // the very thing clicked on
        if (_things.Items.Count == 0) MessageBox.Show(_owner, $"There's no {_what} on that cell - pick the cell the {_what} stands on.", _owner.Title, MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Fill(string? selectedKey, string? savedName)
    {
        _cellText.Text = _cell is { } c ? $"cell ({c.X}, {c.Y})" : "(none picked)";
        var choices = _cell is { } cell
            ? (ProjectSession.MapThingsProvider?.Invoke(cell.X, cell.Y) ?? new List<(string Key, string Name)>()).Where(t => _accepts(t.Key)).Select(t => new ThingChoice(t.Key, t.Name)).ToList()
            : new List<ThingChoice>();
        // Saved with a map that isn't open now: keep what was chosen.
        if (selectedKey is { Length: > 0 } && choices.All(t => t.Key != selectedKey)) choices.Insert(0, new ThingChoice(selectedKey, savedName ?? _what));
        _things.ItemsSource = choices;
        _things.SelectedItem = choices.FirstOrDefault(t => t.Key == selectedKey) ?? choices.FirstOrDefault();
    }

    public bool Validate()
    {
        if (_cell is not null && _things.SelectedItem is ThingChoice) return true;
        MessageBox.Show(_owner, $"Pick the {_what} on the map.", _owner.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    public void Save(EventCommand command)
    {
        if (_things.SelectedItem is not ThingChoice thing || _cell is not { } cell) return;
        command.Parameters["Thing"] = thing.Key;
        command.Parameters["ThingName"] = thing.Name;
        command.Parameters["X"] = cell.X.ToString();
        command.Parameters["Y"] = cell.Y.ToString();
    }

    // ---- What the keys say ----

    /// <summary>The Object Editor entry of an "Object:{instance}:{object id}" key, or null.</summary>
    public static WorldObject? ObjectOf(string key)
    {
        var parts = key.Split(':');
        return parts.Length == 3 && parts[0] == "Object" && int.TryParse(parts[2], out int id) ? ProjectSession.ObjectsProvider?.Invoke()?.Objects.FirstOrDefault(o => o.Id == id) : null;
    }

    public static bool IsObjectOfType(string key, params ObjectType[] types) => ObjectOf(key) is { } obj && types.Contains(obj.Type);
    public static bool IsContainer(string key) => key.StartsWith("Item:Container:");
    public static bool IsAnyThing(string key) => true;

    public static string Describe(EventCommand command) =>
        $"{(command.Get("ThingName") is { Length: > 0 } n ? n : "?")} at ({command.Get("X")}, {command.Get("Y")})";
}

/// <summary>A map cell picked in a command window (Walk To Location, Drop Item's spot, Play Animation's spot).</summary>
internal sealed class MapCellField
{
    private readonly Window _owner;
    private readonly TextBlock _text = new() { Width = 150, VerticalAlignment = VerticalAlignment.Center };
    public (int X, int Y)? Cell { get; private set; }
    public StackPanel Row { get; } = new() { Orientation = Orientation.Horizontal };

    public MapCellField(Window owner, StackPanel rows, EventCommand? existing, string prefix = "")
    {
        _owner = owner;
        if (int.TryParse(existing?.Get(prefix + "X"), out int x) && int.TryParse(existing?.Get(prefix + "Y"), out int y)) Cell = (x, y);
        var pick = new Button { Content = "Pick on Map...", Padding = new Thickness(8, 1, 8, 1) };
        pick.Click += (_, _) =>
        {
            if (ProjectSession.MapSnapshotProvider?.Invoke() is not { } map)
            {
                MessageBox.Show(_owner, "Open or create a map in the Map Editor first.", _owner.Title, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var picker = new MapCellPickerWindow(map.Picture, map.TileWidth, map.TileHeight, map.Columns, map.Rows, Cell, null) { Owner = _owner };
            if (picker.ShowDialog() == true && picker.Picked is { } cell) { Cell = cell; Show(); }
        };
        Row.Children.Add(_text);
        Row.Children.Add(pick);
        rows.Children.Add(Row);
        Show();
    }

    private void Show() => _text.Text = Cell is { } c ? $"cell ({c.X}, {c.Y})" : "(no spot chosen)";

    public bool Validate()
    {
        if (Cell is not null) return true;
        MessageBox.Show(_owner, "Pick the spot on the map.", _owner.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    public void Save(EventCommand command, string prefix = "")
    {
        if (Cell is not { } cell) return;
        command.Parameters[prefix + "X"] = cell.X.ToString();
        command.Parameters[prefix + "Y"] = cell.Y.ToString();
    }
}

/// <summary>An item kind (Book / Container / Object) picked with Select Item: saved as Category and ItemId.</summary>
internal sealed class ItemKindField
{
    private readonly Window _owner;
    private readonly Button _button = new() { Padding = new Thickness(8, 2, 8, 2), MinWidth = 200, HorizontalContentAlignment = HorizontalAlignment.Left, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly string? _anyText;
    public ItemReference? Item { get; private set; }

    public ItemKindField(Window owner, Panel parent, EventCommand? existing, string? anyText = null)
    {
        _owner = owner;
        _anyText = anyText;
        if (existing is not null && int.TryParse(existing.Get("ItemId"), out int id) && id >= 0 && Enum.TryParse<ReferencedItemCategory>(existing.Get("Category"), out var category))
            Item = new ItemReference { Category = category, ItemId = id, DisplayName = CommandFields.ItemName(existing) };
        _button.Click += (_, _) =>
        {
            var picker = new SelectItemPickerWindow(ProjectSession.BooksProvider?.Invoke() ?? new BookDatabase(), ProjectSession.ObjectsProvider?.Invoke() ?? new ObjectDatabase(),
                ProjectSession.ContainersProvider?.Invoke() ?? new ContainerDatabase(), ProjectSession.Sprites, Item) { Owner = _owner };
            if (picker.ShowDialog() == true) Item = picker.Result is { ItemId: >= 0 } r ? new ItemReference { Category = r.Category, ItemId = r.ItemId, DisplayName = r.DisplayName } : null;
            Show();
        };
        parent.Children.Add(_button);
        if (anyText is not null)
        {
            var any = new Button { Content = anyText, Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(6, 0, 0, 0) };
            any.Click += (_, _) => { Item = null; Show(); };
            parent.Children.Add(any);
        }
        Show();
    }

    private void Show() => _button.Content = Item is { ItemId: >= 0 } item ? item.DisplayName : _anyText is null ? "(pick an item...)" : _anyText + " - pick...";

    public bool Validate()
    {
        if (Item is not null || _anyText is not null) return true;
        MessageBox.Show(_owner, "Pick the item.", _owner.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    public void Save(EventCommand command)
    {
        command.Parameters["Category"] = Item?.Category.ToString() ?? "";
        command.Parameters["ItemId"] = Item?.ItemId.ToString() ?? "-1";
    }
}

/// <summary>A pose and which way to face, for Play Pose and Change Pose.</summary>
internal sealed class PoseFields
{
    public readonly ComboBox Pose = new(), Facing = new();

    public PoseFields(FactionCommandWindowBase window, StackPanel rows, EventCommand? existing, Action<string, bool> label)
    {
        label("Pose", true);
        var poses = ProjectSession.Poses.Poses.Select(p => p.Name).ToList();
        Pose.ItemsSource = poses;
        Pose.SelectedItem = poses.FirstOrDefault(p => p == existing?.Get("Pose")) ?? poses.FirstOrDefault(p => p == "Picking") ?? poses.FirstOrDefault();
        rows.Children.Add(Pose);
        label("Facing", true);
        Facing.Items.Add("(Keep the way they face)");
        foreach (var direction in Enum.GetValues<SpriteDirection>()) Facing.Items.Add(direction.ToString());
        Facing.SelectedItem = Facing.Items.Cast<string>().FirstOrDefault(f => f == existing?.Get("Facing")) ?? Facing.Items[0];
        rows.Children.Add(Facing);
    }

    public void Save(EventCommand command)
    {
        command.Parameters["Pose"] = Pose.SelectedItem as string ?? "Idle";
        command.Parameters["Facing"] = Facing.SelectedIndex <= 0 ? "" : Facing.SelectedItem as string ?? "";
    }
}

/// <summary>How long something lasts: a number and a unit (real seconds, game minutes / hours, loops, or till its animation has played once).</summary>
internal sealed class DurationFields
{
    private readonly TextBox _amount;
    private readonly ComboBox _unit = new() { Width = 220, Margin = new Thickness(6, 0, 0, 0) };
    private readonly (string Key, string Label)[] _units;

    public DurationFields(StackPanel rows, EventCommand? existing, bool loops)
    {
        _units = loops
            ? new[] { ("Done", "times through (ends on its last frame)"), ("Seconds", "seconds"), ("Minutes", "game minutes"), ("Hours", "game hours") }
            : new[] { ("Done", "until the animation has played once"), ("Seconds", "seconds"), ("Minutes", "game minutes"), ("Hours", "game hours") };
        var row = CommandFields.Row(rows);
        _amount = CommandFields.Number(row, existing?.Get("Duration"), "1", 50);
        foreach (var (_, label) in _units) _unit.Items.Add(label);
        int index = Array.FindIndex(_units, u => u.Key == existing?.Get("Unit"));
        _unit.SelectedIndex = index >= 0 ? index : 0;
        row.Children.Add(_unit);
        void Show() => _amount.IsEnabled = _units[Math.Max(0, _unit.SelectedIndex)].Key != "Done" || loops;
        _unit.SelectionChanged += (_, _) => Show();
        Show();
    }

    public bool Validate(Window owner)
    {
        if (double.TryParse(_amount.Text.Trim(), out double amount) && amount > 0) return true;
        MessageBox.Show(owner, "How long must be a number above 0.", owner.Title, MessageBoxButton.OK, MessageBoxImage.Information);
        return false;
    }

    public void Save(EventCommand command)
    {
        command.Parameters["Duration"] = _amount.Text.Trim();
        command.Parameters["Unit"] = _units[Math.Max(0, _unit.SelectedIndex)].Key;
    }

    public static string Describe(EventCommand command) => command.Get("Unit") switch
    {
        "Done" or "" => command.Get("Duration") is { Length: > 0 } d && d != "1" && command.Type == EventCommandType.PlayAnimation ? $"{d} times through" : "until the animation has played",
        "Loops" => $"{command.Get("Duration")} loops",
        "Seconds" => $"{command.Get("Duration")} s",
        "Minutes" => $"{command.Get("Duration")} game minutes",
        _ => $"{command.Get("Duration")} game hours"
    };
}

/// <summary>The base for the action commands: a Who, then the command's own rows, and a note that the commands after it wait.</summary>
public abstract class ActionCommandWindowBase : FactionCommandWindowBase
{
    protected ComboBox? WhoCombo;

    protected ActionCommandWindowBase(string title, EventCommand? existing, bool withWho = true) : base(title)
    {
        Width = 480;
        if (!withWho) return;
        AddLabel("Who");
        WhoCombo = CommandFields.Who(Rows, existing?.Get("Who"), includePlayer: true);
    }

    protected void AddWaitNote(string text) =>
        AddLabel(text + " The commands after this one wait till it's done; if it can't be done it is cancelled and they go on - Advance IF > Last command succeeded tells which.", bold: false);

    protected EventCommand NewCommand(EventCommandType type)
    {
        var command = new EventCommand { Type = type };
        if (WhoCombo is not null) command.Parameters["Who"] = CommandFields.WhoKey(WhoCombo);
        return command;
    }

    protected static string Who(EventCommand command) => WhoChoice.Describe(command.Get("Who"));
}

public class PickupItemCommandWindow : ActionCommandWindowBase
{
    private readonly MapThingField _thing;

    public PickupItemCommandWindow(EventCommand? existing = null) : base("Pickup Item", existing)
    {
        AddLabel("Picks up");
        _thing = new MapThingField(this, Rows, existing, "thing", MapThingField.IsAnyThing);
        AddWaitNote("They walk to it (round whatever is in the way), play the Picking pose and take it as if it were dragged onto them - its weight, room in their pack or hands. Too heavy, no room, or no way there: cancelled.");
    }

    protected override bool TryBuild()
    {
        if (!_thing.Validate()) return false;
        var command = NewCommand(EventCommandType.PickupItem);
        _thing.Save(command);
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => $"{Who(command)} picks up {MapThingField.Describe(command)}";
}

public class DropItemCommandWindow : ActionCommandWindowBase
{
    private readonly ItemKindField _item;
    private readonly TextBox _quantity;
    private readonly CheckBox _atSpot;
    private readonly MapCellField _spot;

    public DropItemCommandWindow(EventCommand? existing = null) : base("Drop Item", existing)
    {
        AddLabel("Drops (something they carry, wear or hold)");
        var row = CommandFields.Row(Rows);
        _item = new ItemKindField(this, row, existing);
        row.Children.Add(new TextBlock { Text = "   How many", VerticalAlignment = VerticalAlignment.Center });
        _quantity = CommandFields.Number(row, existing?.Get("Quantity"), "1", 45);
        _quantity.Margin = new Thickness(6, 0, 0, 0);
        _atSpot = new CheckBox { Content = "At a spot (not checked: at their feet)", IsChecked = existing?.Get("AtSpot") == "True", Margin = new Thickness(0, 10, 0, 4) };
        Rows.Children.Add(_atSpot);
        _spot = new MapCellField(this, Rows, existing);
        void Show() => _spot.Row.IsEnabled = _atSpot.IsChecked == true;
        _atSpot.Checked += (_, _) => Show();
        _atSpot.Unchecked += (_, _) => Show();
        Show();
        AddWaitNote("At a spot, they walk there first and put it down on it (on a table, it lands on the table). Worn or readied things are taken off first. They haven't got it, or can't get there: cancelled.");
    }

    protected override bool TryBuild()
    {
        if (!_item.Validate() || (_atSpot.IsChecked == true && !_spot.Validate())) return false;
        if (!int.TryParse(_quantity.Text.Trim(), out int quantity) || quantity < 1)
        {
            MessageBox.Show(this, "How many must be a whole number, 1 or more.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var command = NewCommand(EventCommandType.DropItem);
        _item.Save(command);
        command.Parameters["Quantity"] = quantity.ToString();
        command.Parameters["AtSpot"] = _atSpot.IsChecked == true ? "True" : "False";
        _spot.Save(command);
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) =>
        $"{Who(command)} drops {CommandFields.ItemName(command)}{(command.Get("Quantity") is { Length: > 0 } q && q != "1" ? " x" + q : "")} {(command.Get("AtSpot") == "True" ? $"at ({command.Get("X")}, {command.Get("Y")})" : "at their feet")}";
}

public class EquipItemCommandWindow : ActionCommandWindowBase
{
    private readonly ItemKindField _item;

    public EquipItemCommandWindow(EventCommand? existing = null) : base("Equip Equipment", existing)
    {
        AddLabel("Readies (a weapon, armor, ammo... they carry)");
        _item = new ItemKindField(this, Rows, existing);
        AddLabel("It goes in its own slot (Object Editor > Weapon / Armor / Ammo; a container in its Location slot; anything else in a hand). Something already there is taken off and put in their pack - no room there, it's dropped at their feet. They haven't got it: cancelled.", bold: false);
    }

    protected override bool TryBuild()
    {
        if (!_item.Validate()) return false;
        var command = NewCommand(EventCommandType.EquipItem);
        _item.Save(command);
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => $"{Who(command)} readies {CommandFields.ItemName(command)}";
}

public class UnequipItemCommandWindow : ActionCommandWindowBase
{
    private readonly ItemKindField _item;

    public UnequipItemCommandWindow(EventCommand? existing = null) : base("Unequip Equipment", existing)
    {
        AddLabel("Takes off (worn, readied or held)");
        _item = new ItemKindField(this, Rows, existing);
        AddLabel("It goes into their pack (Backpack, then Pouch); no pack or no room - it's dropped at their feet. Not wearing it: cancelled.", bold: false);
    }

    protected override bool TryBuild()
    {
        if (!_item.Validate()) return false;
        var command = NewCommand(EventCommandType.UnequipItem);
        _item.Save(command);
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => $"{Who(command)} takes off {CommandFields.ItemName(command)}";
}

public class PlayPoseCommandWindow : ActionCommandWindowBase
{
    private readonly PoseFields _pose;
    private readonly DurationFields _duration;

    public PlayPoseCommandWindow(EventCommand? existing = null) : base("Play Pose", existing)
    {
        _pose = new PoseFields(this, Rows, existing, AddLabel);
        AddLabel("For");
        _duration = new DurationFields(Rows, existing, loops: false);
        AddWaitNote("The pose (its animation and sounds) plays for that long - or, \"until the animation has played once\", till its last frame has shown. Then they stand (Idle) again.");
    }

    protected override bool TryBuild()
    {
        if (!_duration.Validate(this)) return false;
        var command = NewCommand(EventCommandType.PlayPose);
        _pose.Save(command);
        _duration.Save(command);
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => $"{Who(command)} {command.Get("Pose")}{(command.Get("Facing") is { Length: > 0 } f ? " facing " + f : "")}, {DurationFields.Describe(command)}";
}

public class ChangePoseCommandWindow : ActionCommandWindowBase
{
    private readonly PoseFields _pose;

    public ChangePoseCommandWindow(EventCommand? existing = null) : base("Change Pose", existing)
    {
        _pose = new PoseFields(this, Rows, existing, AddLabel);
        AddLabel("They show this pose (facing that way) and keep it until they move or do something else. At once - the commands after it don't wait.", bold: false);
    }

    protected override bool TryBuild()
    {
        var command = NewCommand(EventCommandType.ChangePose);
        _pose.Save(command);
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => $"{Who(command)} {command.Get("Pose")}{(command.Get("Facing") is { Length: > 0 } f ? " facing " + f : "")}";
}

public class WalkToLocationCommandWindow : ActionCommandWindowBase
{
    private readonly MapCellField _spot;

    public WalkToLocationCommandWindow(EventCommand? existing = null) : base("Walk To Location", existing)
    {
        AddLabel("To");
        _spot = new MapCellField(this, Rows, existing);
        AddWaitNote("They walk there by the walking rules (stairs, doors opened on the way), finding a way round whatever is in the way, leg by leg. Only when there truly is no way (5 tries getting no nearer) is it cancelled. Someone standing on the spot: right beside it will do.");
    }

    protected override bool TryBuild()
    {
        if (!_spot.Validate()) return false;
        var command = NewCommand(EventCommandType.WalkToLocation);
        _spot.Save(command);
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => $"{Who(command)} to ({command.Get("X")}, {command.Get("Y")})";
}

public class AttackObjectCommandWindow : ActionCommandWindowBase
{
    private readonly MapThingField _thing;

    public AttackObjectCommandWindow(EventCommand? existing = null) : base("Attack Object", existing)
    {
        AddLabel("Attacks");
        _thing = new MapThingField(this, Rows, existing, "thing", MapThingField.IsAnyThing);
        AddWaitNote("They walk up to it and strike it (Attacking Melee pose) until it breaks, as a blow in combat does. Still standing after a minute of blows (it can't be broken, or is too strong): cancelled.");
    }

    protected override bool TryBuild()
    {
        if (!_thing.Validate()) return false;
        var command = NewCommand(EventCommandType.AttackObject);
        _thing.Save(command);
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => $"{Who(command)} attacks {MapThingField.Describe(command)}";
}

public class PlayAnimationCommandWindow : ActionCommandWindowBase
{
    private readonly ComboBox _group = new(), _sprite = new(), _where = new();
    private readonly ComboBox _over;
    private readonly MapCellField _spot;
    private readonly DurationFields _duration;
    private readonly CheckBox _wait;

    public PlayAnimationCommandWindow(EventCommand? existing = null) : base("Play Animation", existing, withWho: false)
    {
        AddLabel("Sprite (its animation and sounds)");
        var groups = ProjectSession.Sprites.Groups.Select(g => g.Name).ToList();
        _group.ItemsSource = groups;
        _group.SelectionChanged += (_, _) =>
        {
            var sprites = ProjectSession.Sprites.Groups.FirstOrDefault(g => g.Name == _group.SelectedItem as string)?.Sprites.Select(s => s.Name).ToList() ?? new List<string>();
            _sprite.ItemsSource = sprites;
            _sprite.SelectedItem = sprites.FirstOrDefault(s => s == existing?.Get("SpriteName")) ?? sprites.FirstOrDefault();
        };
        _group.SelectedItem = groups.FirstOrDefault(g => g == existing?.Get("SpriteGroup")) ?? groups.FirstOrDefault();
        var spriteRow = CommandFields.Row(Rows);
        _group.Width = 160;
        _sprite.Width = 200;
        _sprite.Margin = new Thickness(6, 0, 0, 0);
        spriteRow.Children.Add(_group);
        spriteRow.Children.Add(_sprite);

        AddLabel("Where");
        _where.Items.Add("At a spot on the map");
        _where.Items.Add("Over a character");
        _where.SelectedIndex = existing?.Get("Where") == "Character" ? 1 : 0;
        Rows.Children.Add(_where);
        _spot = new MapCellField(this, Rows, existing);
        _over = CommandFields.Who(Rows, existing?.Get("Who"), includePlayer: true);
        void Show()
        {
            _spot.Row.Visibility = _where.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
            _over.Visibility = _where.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        }
        _where.SelectionChanged += (_, _) => Show();
        Show();

        AddLabel("For");
        _duration = new DurationFields(Rows, existing, loops: true);
        _wait = new CheckBox { Content = "The commands after it wait till it's over", IsChecked = existing?.Get("Wait") != "False", Margin = new Thickness(0, 8, 0, 0) };
        Rows.Children.Add(_wait);
        AddLabel("Drawn in the world at that spot (bottom-centre on the cell, over everything standing there), its frames' sounds playing as they come, then gone.", bold: false);
    }

    protected override bool TryBuild()
    {
        if (_sprite.SelectedItem is not string spriteName || _group.SelectedItem is not string groupName)
        {
            MessageBox.Show(this, "Pick the sprite to play.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if ((_where.SelectedIndex == 0 && !_spot.Validate()) || !_duration.Validate(this)) return false;
        var command = NewCommand(EventCommandType.PlayAnimation);
        command.Parameters["SpriteGroup"] = groupName;
        command.Parameters["SpriteName"] = spriteName;
        command.Parameters["Where"] = _where.SelectedIndex == 1 ? "Character" : "Spot";
        command.Parameters["Who"] = CommandFields.WhoKey(_over);
        _spot.Save(command);
        _duration.Save(command);
        command.Parameters["Wait"] = _wait.IsChecked == true ? "True" : "False";
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) =>
        $"{command.Get("SpriteName")} {(command.Get("Where") == "Character" ? "over " + Who(command) : $"at ({command.Get("X")}, {command.Get("Y")})")}, {DurationFields.Describe(command)}{(command.Get("Wait") == "False" ? "" : ", waits")}";
}

public class SitCommandWindow : ActionCommandWindowBase
{
    private readonly MapThingField _thing;

    public SitCommandWindow(EventCommand? existing = null) : base("Sit", existing)
    {
        AddLabel("Sits on (a chair)");
        _thing = new MapThingField(this, Rows, existing, "chair", key => MapThingField.IsObjectOfType(key, ObjectType.Chair));
        AddWaitNote("They walk to the chair and sit on its free seat nearest them, as double-clicking it (or the Sit schedule) does. A character with nothing else to do then sits for 10 to 25 seconds before going back to its schedule. No free seat, or no way there: cancelled.");
    }

    protected override bool TryBuild()
    {
        if (!_thing.Validate()) return false;
        var command = NewCommand(EventCommandType.Sit);
        _thing.Save(command);
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => $"{Who(command)} on {MapThingField.Describe(command)}";
}

public class StandCommandWindow : ActionCommandWindowBase
{
    public StandCommandWindow(EventCommand? existing = null) : base("Stand", existing)
    {
        AddLabel("They get up from the chair or bed they're on, back to where they stood (a bed is made again). Not sitting or lying: nothing happens. At once.", bold: false);
    }

    protected override bool TryBuild()
    {
        Result = NewCommand(EventCommandType.Stand);
        return true;
    }

    public static string Describe(EventCommand command) => Who(command);
}

public class OpenDoorCommandWindow : ActionCommandWindowBase
{
    private readonly MapThingField _thing;

    public OpenDoorCommandWindow(EventCommand? existing = null) : base("Open Door", existing)
    {
        AddLabel("Opens (a door)");
        _thing = new MapThingField(this, Rows, existing, "door", key => MapThingField.IsObjectOfType(key, ObjectType.Door));
        AddWaitNote("They walk to the door and open it. Locked: if they carry its key (or a keyring holding it) they unlock it as the main character using the key does - the unlock sound, \"Unlocked\" - then open it. Locked with no key, or magically locked: cancelled.");
    }

    protected override bool TryBuild()
    {
        if (!_thing.Validate()) return false;
        var command = NewCommand(EventCommandType.OpenDoor);
        _thing.Save(command);
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => $"{Who(command)} opens {MapThingField.Describe(command)}";
}

public class PicklockDoorCommandWindow : ActionCommandWindowBase
{
    private readonly MapThingField _thing;

    public PicklockDoorCommandWindow(EventCommand? existing = null) : base("Picklock Door", existing)
    {
        AddLabel("Picks the lock of (a door)");
        _thing = new MapThingField(this, Rows, existing, "door", key => MapThingField.IsObjectOfType(key, ObjectType.Door));
        AddWaitNote("They walk to the door and work at the lock with a lockpick they carry - Dexterity against the door's Pick-lock difficulty, as for the main character. A failure breaks that lockpick and they try again with the next. Unlocked: done (the door stays closed). Out of lockpicks and still locked, or magically locked: cancelled.");
    }

    protected override bool TryBuild()
    {
        if (!_thing.Validate()) return false;
        var command = NewCommand(EventCommandType.PicklockDoor);
        _thing.Save(command);
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) => $"{Who(command)} picks {MapThingField.Describe(command)}";
}

public class TakeFromContainerCommandWindow : ActionCommandWindowBase
{
    private readonly MapThingField _thing;
    private readonly ItemKindField _item;
    private readonly TextBox _quantity;

    public TakeFromContainerCommandWindow(EventCommand? existing = null) : base("Pick Item From Container", existing)
    {
        AddLabel("From (a container on the map)");
        _thing = new MapThingField(this, Rows, existing, "container", MapThingField.IsContainer);
        AddLabel("Takes");
        var row = CommandFields.Row(Rows);
        _item = new ItemKindField(this, row, existing, anyText: "(everything)");
        row.Children.Add(new TextBlock { Text = "   How many", VerticalAlignment = VerticalAlignment.Center });
        _quantity = CommandFields.Number(row, existing?.Get("Quantity"), "1", 45);
        _quantity.Margin = new Thickness(6, 0, 0, 0);
        AddWaitNote("They walk to it and take the items out as if dragged onto them (weight, room in their pack or hands). The container must be unlocked - locked or magically locked: cancelled. What they can't carry stays inside; nothing at all taken: cancelled.");
    }

    protected override bool TryBuild()
    {
        if (!_thing.Validate()) return false;
        if (!int.TryParse(_quantity.Text.Trim(), out int quantity) || quantity < 1)
        {
            MessageBox.Show(this, "How many must be a whole number, 1 or more.", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        var command = NewCommand(EventCommandType.TakeFromContainer);
        _thing.Save(command);
        _item.Save(command);
        command.Parameters["Quantity"] = quantity.ToString();
        Result = command;
        return true;
    }

    public static string Describe(EventCommand command) =>
        $"{Who(command)} takes {(command.Get("ItemId") is { Length: > 0 } id && id != "-1" ? CommandFields.ItemName(command) + (command.Get("Quantity") is { Length: > 0 } q && q != "1" ? " x" + q : "") : "everything")} from {MapThingField.Describe(command)}";
}
